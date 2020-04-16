#region Related components
using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Collections.Generic;
using System.Dynamic;
using System.ComponentModel;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using net.vieapps.Components.Utility;
using net.vieapps.Components.Security;
using net.vieapps.Components.Repository;
#endregion

namespace net.vieapps.Services.Portals
{
	public class ServiceComponent : ServiceBase
	{
		public override string ServiceName => "Portals";

		public override async Task<JToken> ProcessRequestAsync(RequestInfo requestInfo, CancellationToken cancellationToken = default)
		{
			var stopwatch = Stopwatch.StartNew();
			this.WriteLogs(requestInfo, $"Begin request ({requestInfo.Verb} {requestInfo.GetURI()})");
			try
			{
				JToken json = null;
				switch (requestInfo.ObjectName.ToLower())
				{
					case "organization":
						json = await this.ProcessOrganizationAsync(requestInfo, cancellationToken).ConfigureAwait(false);
						break;

					case "role":
						json = await this.ProcessRoleAsync(requestInfo, cancellationToken).ConfigureAwait(false);
						break;

					case "site":
						json = await this.ProcessSiteAsync(requestInfo, cancellationToken).ConfigureAwait(false);
						break;

					case "definitions":
						switch (requestInfo.GetObjectIdentity())
						{
							case "social":
							case "socials":
								json = UtilityService.GetAppSetting("Portals:Socials", "Facebook,Twitter").ToArray().ToJArray();
								break;

							case "tracking":
							case "trackings":
								json = UtilityService.GetAppSetting("Portals:Trackings", "GoogleAnalytics,FacebookPixel").ToArray().ToJArray();
								break;

							case "organization":
								json = this.GenerateFormControls<Organization>();
								break;

							case "role":
								json = this.GenerateFormControls<Role>();
								break;

							case "module":
								json = this.GenerateFormControls<Module>();
								break;

							case "contenttype":
							case "content-type":
								json = this.GenerateFormControls<ContentType>();
								break;

							case "site":
								json = this.GenerateFormControls<Site>();
								break;

							case "desktop":
								json = this.GenerateFormControls<Desktop>();
								break;

							default:
								throw new InvalidRequestException($"The request is invalid [({requestInfo.Verb}): {requestInfo.GetURI()}]");
						}
						break;

					default:
						throw new InvalidRequestException($"The request is invalid [({requestInfo.Verb}): {requestInfo.GetURI()}]");
				}
				stopwatch.Stop();
				this.WriteLogs(requestInfo, $"Success response - Execution times: {stopwatch.GetElapsedTimes()}");
				if (this.IsDebugResultsEnabled)
					this.WriteLogs(requestInfo,
						$"- Request: {requestInfo.ToString(this.IsDebugLogEnabled ? Formatting.Indented : Formatting.None)}" + "\r\n" +
						$"- Response: {json?.ToString(this.IsDebugLogEnabled ? Formatting.Indented : Formatting.None)}"
					);
				return json;
			}
			catch (Exception ex)
			{
				throw this.GetRuntimeException(requestInfo, ex, stopwatch);
			}
		}

		Task<JObject> ProcessOrganizationAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			switch (requestInfo.Verb)
			{
				case "GET":
					if ("search".IsEquals(requestInfo.GetObjectIdentity()))
						return this.SearchOrganizationsAsync(requestInfo, cancellationToken);
					else
						return this.GetOrganizationAsync(requestInfo, cancellationToken);

				case "POST":
					return this.CreateOrganizationAsync(requestInfo, cancellationToken);

				case "PUT":
					return this.UpdateOrganizationAsync(requestInfo, cancellationToken);

				case "DELETE":
					return this.DeleteOrganizationAsync(requestInfo, cancellationToken);
			}

			return Task.FromException<JObject>(new MethodNotAllowedException(requestInfo.Verb));
		}

		#region Search organizations
		async Task<JObject> SearchOrganizationsAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			// prepare
			var request = requestInfo.GetRequestExpando();

			var query = request.Get<string>("FilterBy.Query");

			var filter = request.Get<ExpandoObject>("FilterBy", null)?.ToFilterBy<Organization>() ?? Filters<Organization>.And();

			var sort = request.Get<ExpandoObject>("SortBy", null)?.ToSortBy<Organization>();
			if (sort == null && string.IsNullOrWhiteSpace(query))
				sort = Sorts<Organization>.Ascending("Title");

			var pagination = request.Has("Pagination")
				? request.Get<ExpandoObject>("Pagination").GetPagination()
				: new Tuple<long, int, int, int>(-1, 0, 20, 1);

			var pageSize = pagination.Item3;
			var pageNumber = pagination.Item4;

			// check permissions
			var gotRights = await this.IsSystemAdministratorAsync(requestInfo).ConfigureAwait(false) || await this.IsAuthorizedAsync(requestInfo, "Organization", Components.Security.Action.Approve, cancellationToken).ConfigureAwait(false);
			if (!gotRights)
				throw new AccessDeniedException();

			// process cache
			var json = string.IsNullOrWhiteSpace(query)
				? await Utility.Cache.GetAsync<string>(this.GetCacheKeyOfObjectsJson(filter, sort, pageNumber, pageSize), cancellationToken).ConfigureAwait(false)
				: "";

			if (!string.IsNullOrWhiteSpace(json))
				return JObject.Parse(json);

			// prepare pagination
			var totalRecords = pagination.Item1 > -1
				? pagination.Item1
				:  -1;

			if (totalRecords < 0)
				totalRecords = string.IsNullOrWhiteSpace(query)
					? await Organization.CountAsync(filter, this.GetCacheKeyOfTotalObjects(filter, sort), cancellationToken).ConfigureAwait(false)
					: await Organization.CountAsync(query, filter, cancellationToken).ConfigureAwait(false);

			var totalPages = new Tuple<long, int>(totalRecords, pageSize).GetTotalPages();
			if (totalPages > 0 && pageNumber > totalPages)
				pageNumber = totalPages;

			// search
			var objects = totalRecords > 0
				? string.IsNullOrWhiteSpace(query)
					? await Organization.FindAsync(filter, sort, pageSize, pageNumber, this.GetCacheKey(filter, sort, pageNumber, pageSize), cancellationToken).ConfigureAwait(false)
					: await Organization.SearchAsync(query, filter, pageSize, pageNumber, cancellationToken).ConfigureAwait(false)
				: new List<Organization>();

			// build result
			pagination = new Tuple<long, int, int, int>(totalRecords, totalPages, pageSize, pageNumber);

			var result = new JObject()
			{
				{ "FilterBy", (filter ?? new FilterBys<Organization>()).ToClientJson(query) },
				{ "SortBy", sort?.ToClientJson() },
				{ "Pagination", pagination.GetPagination() },
				{ "Objects", objects.ToJsonArray() }
			};

			// update cache
			if (string.IsNullOrWhiteSpace(query))
			{
#if DEBUG
				json = result.ToString(Formatting.Indented);
#else
				json = result.ToString(Formatting.None);
#endif
				await Utility.Cache.SetAsync(this.GetCacheKeyOfObjectsJson(filter, sort, pageNumber, pageSize), json, Utility.Cache.ExpirationTime / 2).ConfigureAwait(false);
			}

			// return the result
			return result;
		}
		#endregion

		#region Create an organization
		async Task<JObject> CreateOrganizationAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			// check permission
			var isCreatedByOtherService = requestInfo.Extra != null && requestInfo.Extra.TryGetValue("x-create", out var xcreate) && xcreate.IsEquals(requestInfo.Session.SessionID.Encrypt());
			var isSystemAdministrator = await this.IsSystemAdministratorAsync(requestInfo).ConfigureAwait(false) || await this.IsAuthorizedAsync(requestInfo, "Organization", Components.Security.Action.Approve, cancellationToken).ConfigureAwait(false);
			if (!isSystemAdministrator && !isCreatedByOtherService)
				throw new AccessDeniedException();

			// check the exising the the alias
			var info = requestInfo.GetBodyExpando();
			var alias = info.Get<string>("Alias");
			if (!string.IsNullOrWhiteSpace(alias))
			{
				var existing = await Utility.GetOrganizationByAliasAsync(alias, cancellationToken).ConfigureAwait(false);
				if (existing != null)
					throw new InformationExistedException("The alias (" + alias + ") is used by another organization");
			}

			// prepare
			var organization = info.Copy<Organization>("Status,Instructions,Privileges,OriginalPrivileges,Created,CreatedID,LastUpdated,LastUpdatedID".ToHashSet());

			organization.ID = string.IsNullOrWhiteSpace(organization.ID) || !organization.ID.IsValidUUID() ? UtilityService.NewUUID : organization.ID;
			organization.Alias = string.IsNullOrWhiteSpace(organization.Alias) ? organization.Title.GetANSIUri() + organization.ID : organization.Alias;
			organization.OwnerID = string.IsNullOrWhiteSpace(organization.OwnerID) || !organization.OwnerID.IsValidUUID() ? requestInfo.Session.User.ID : organization.OwnerID;

			organization.Status = isSystemAdministrator
				? info.Get("Status", "Pending").TryToEnum(out ApprovalStatus statusByAdmin) ? statusByAdmin : ApprovalStatus.Pending
				: isCreatedByOtherService 
					? requestInfo.Extra.TryGetValue("x-status", out var xstatus) && xstatus.TryToEnum(out ApprovalStatus statusByOtherService) ? statusByOtherService : ApprovalStatus.Pending
					 : ApprovalStatus.Pending;

			organization.Instructions = Organization.GetInstructions(info.Get<ExpandoObject>("Instructions"));
			organization.NormalizeSettings();

			organization.OriginalPrivileges =(isSystemAdministrator ? info.Get<Privileges>("OriginalPrivileges") : null) ?? new Privileges(true);

			organization.Created = organization.LastModified = DateTime.Now;
			organization.CreatedID = organization.LastModifiedID = requestInfo.Session.User.ID;

			// create new
			await Organization.CreateAsync(organization, cancellationToken).ConfigureAwait(false);
			await Utility.Cache.RemoveAsync(this.GetRelatedCacheKeys(null, Sorts<Organization>.Ascending("Title")), cancellationToken).ConfigureAwait(false);

			// send update message
			await this.SendUpdateMessageAsync(new UpdateMessage
			{
				Type = $"{this.ServiceName}#{organization.GetTypeName(true)}#Create",
				DeviceID = "*",
				ExcludedDeviceID = requestInfo.Session.DeviceID,
				Data = organization.ToJson()
			}, cancellationToken).ConfigureAwait(false);

			return organization.ToJson();
		}
		#endregion

		#region Get an organization
		async Task<JObject> GetOrganizationAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			// get the organization
			var organization = await Organization.GetAsync<Organization>(requestInfo.GetObjectIdentity(), cancellationToken).ConfigureAwait(false);
			if (organization == null)
				throw new InformationNotFoundException();

			// check permission
			var isSystemAdministrator = await this.IsSystemAdministratorAsync(requestInfo).ConfigureAwait(false) || await this.IsAuthorizedAsync(requestInfo, "Organization", Components.Security.Action.Approve, cancellationToken).ConfigureAwait(false);
			var gotRights = isSystemAdministrator || requestInfo.Session.User.ID.IsEquals(organization.OwnerID) || requestInfo.Session.User.IsAdministrator(organization.WorkingPrivileges);
			if (!gotRights)
				throw new AccessDeniedException();

			// response
			return organization.ToJson();
		}
		#endregion

		#region Update an organization
		async Task<JObject> UpdateOrganizationAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			// get the organization
			var organization = await Utility.GetOrganizationByIDAsync(requestInfo.GetObjectIdentity(), cancellationToken).ConfigureAwait(false);
			if (organization == null)
				throw new InformationNotFoundException();

			// check permission
			var isSystemAdministrator = await this.IsSystemAdministratorAsync(requestInfo).ConfigureAwait(false) || await this.IsAuthorizedAsync(requestInfo, "Organization", Components.Security.Action.Approve, cancellationToken).ConfigureAwait(false);
			var gotRights = isSystemAdministrator || requestInfo.Session.User.ID.IsEquals(organization.OwnerID) || requestInfo.Session.User.IsAdministrator(organization.WorkingPrivileges);
			if (!gotRights)
				throw new AccessDeniedException();

			// check the exising the the alias
			var info = requestInfo.GetBodyExpando();
			var oldAlias = organization.Alias;
			var alias = info.Get<string>("Alias");
			if (!string.IsNullOrWhiteSpace(alias))
			{
				var existing = await Utility.GetOrganizationByAliasAsync(alias, cancellationToken).ConfigureAwait(false);
				if (existing != null && !existing.ID.Equals(organization.ID))
					throw new InformationExistedException($"The alias ({alias}) is used by another organization");
			}

			// prepare
			organization.CopyFrom(info, "ID,OwnerID,Status,Instructions,Privileges,OriginalPrivileges,Created,CreatedID,LastUpdated,LastUpdatedID".ToHashSet());

			organization.OwnerID = isSystemAdministrator ? info.Get("OwnerID", organization.OwnerID) : organization.OwnerID;
			organization.Alias = string.IsNullOrWhiteSpace(organization.Alias) ? oldAlias : organization.Alias;

			organization.Instructions = Organization.GetInstructions(info.Get<ExpandoObject>("Instructions"));
			organization.NormalizeSettings();

			organization.OriginalPrivileges = info.Get("OriginalPrivileges", new Privileges(true));

			organization.LastModified = DateTime.Now;
			organization.LastModifiedID = requestInfo.Session.User.ID;

			// update
			await Organization.UpdateAsync(organization, requestInfo.Session.User.ID, cancellationToken).ConfigureAwait(false);
			await Utility.Cache.RemoveAsync(this.GetRelatedCacheKeys(null, Sorts<Organization>.Ascending("Title")), cancellationToken).ConfigureAwait(false);

			// send update message
			await this.SendUpdateMessageAsync(new UpdateMessage
			{
				Type = $"{this.ServiceName}#{organization.GetTypeName(true)}#Update",
				DeviceID = "*",
				ExcludedDeviceID = requestInfo.Session.DeviceID,
				Data = organization.ToJson()
			}, cancellationToken).ConfigureAwait(false);

			return organization.ToJson();
		}
		#endregion

		#region Delete an organization
		Task<JObject> DeleteOrganizationAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			throw new MethodNotAllowedException(requestInfo.Verb);
		}
		#endregion

		Task<JObject> ProcessRoleAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			switch (requestInfo.Verb)
			{
				case "GET":
					if ("search".IsEquals(requestInfo.GetObjectIdentity()))
						return this.SearchRolesAsync(requestInfo, cancellationToken);
					else
						return this.GetRoleAsync(requestInfo, cancellationToken);

				case "POST":
					return this.CreateRoleAsync(requestInfo, cancellationToken);

				case "PUT":
					return this.UpdateRoleAsync(requestInfo, cancellationToken);

				case "DELETE":
					return this.DeleteRoleAsync(requestInfo, cancellationToken);
			}

			return Task.FromException<JObject>(new MethodNotAllowedException(requestInfo.Verb));
		}

		#region Search roles
		async Task<JObject> SearchRolesAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			// prepare
			var request = requestInfo.GetRequestExpando();

			var query = request.Get<string>("FilterBy.Query");

			var filter = request.Get<ExpandoObject>("FilterBy", null)?.ToFilterBy<Role>() ?? Filters<Role>.And();
			if (filter is FilterBys<Role> && (filter as FilterBys<Role>).Children.FirstOrDefault(exp => (exp as FilterBy<Role>).Attribute.IsEquals("ParentID")) == null)
				(filter as FilterBys<Role>).Children.Add(Filters<Role>.IsNull("ParentID"));

			var sort = request.Get<ExpandoObject>("SortBy", null)?.ToSortBy<Role>();
			if (string.IsNullOrWhiteSpace(query))
				sort = sort ?? Sorts<Role>.Ascending("Title");
			else if (filter is FilterBys<Role>)
			{
				var index = (filter as FilterBys<Role>).Children.FindIndex(exp => (exp as FilterBy<Role>).Attribute.IsEquals("ParentID"));
				if (index > -1)
					(filter as FilterBys<Role>).Children.RemoveAt(index);
			}

			var pagination = request.Has("Pagination")
				? request.Get<ExpandoObject>("Pagination").GetPagination()
				: new Tuple<long, int, int, int>(-1, 0, 20, 1);

			var pageSize = pagination.Item3;
			var pageNumber = pagination.Item4;

			// get organization
			var organizationID = filter != null && filter is FilterBys<Role>
				? ((filter as FilterBys<Role>).Children.FirstOrDefault(exp => (exp as FilterBy<Role>).Attribute.IsEquals("SystemID")) as FilterBy<Role>)?.Value as string
				: null;
			var organization = await Utility.GetOrganizationByIDAsync(organizationID, cancellationToken).ConfigureAwait(false);
			if (organization == null)
				throw new InformationExistedException("The organization is invalid");

			// check permission
			var isSystemAdministrator = await this.IsSystemAdministratorAsync(requestInfo).ConfigureAwait(false) || await this.IsAuthorizedAsync(requestInfo, "Organization", Components.Security.Action.Approve, cancellationToken).ConfigureAwait(false);
			var gotRights = isSystemAdministrator || requestInfo.Session.User.ID.IsEquals(organization.OwnerID) || requestInfo.Session.User.IsViewer(organization.WorkingPrivileges);
			if (!gotRights)
				throw new AccessDeniedException();

			// process cache
			var json = string.IsNullOrWhiteSpace(query)
				? await Utility.Cache.GetAsync<string>(this.GetCacheKeyOfObjectsJson(filter, sort, pageNumber, pageSize), cancellationToken).ConfigureAwait(false)
				: "";

			if (!string.IsNullOrWhiteSpace(json))
				return JObject.Parse(json);

			// prepare pagination
			var totalRecords = pagination.Item1 > -1
				? pagination.Item1
				: -1;

			if (totalRecords < 0)
				totalRecords = string.IsNullOrWhiteSpace(query)
					? await Role.CountAsync(filter, this.GetCacheKeyOfTotalObjects(filter, sort), cancellationToken).ConfigureAwait(false)
					: await Role.CountAsync(query, filter, cancellationToken).ConfigureAwait(false);

			var totalPages = (new Tuple<long, int>(totalRecords, pageSize)).GetTotalPages();
			if (totalPages > 0 && pageNumber > totalPages)
				pageNumber = totalPages;

			// search
			var objects = totalRecords > 0
				? string.IsNullOrWhiteSpace(query)
					? await Role.FindAsync(filter, sort, pageSize, pageNumber, this.GetCacheKey(filter, sort, pageNumber, pageSize), cancellationToken).ConfigureAwait(false)
					: await Role.SearchAsync(query, filter, pageSize, pageNumber, cancellationToken).ConfigureAwait(false)
				: new List<Role>();

			// build result
			pagination = new Tuple<long, int, int, int>(totalRecords, totalPages, pageSize, pageNumber);

			var result = new JObject()
			{
				{ "FilterBy", filter.ToClientJson(query) },
				{ "SortBy", sort?.ToClientJson() },
				{ "Pagination", pagination.GetPagination() },
				{ "Objects", objects.ToJsonArray() }
			};

			// update cache
			if (string.IsNullOrWhiteSpace(query))
			{
#if DEBUG
				json = result.ToString(Formatting.Indented);
#else
				json = result.ToString(Formatting.None);
#endif
				await Utility.Cache.SetAsync(this.GetCacheKeyOfObjectsJson(filter, sort, pageNumber, pageSize), json, Utility.Cache.ExpirationTime / 2).ConfigureAwait(false);
			}

			// return the result
			return result;
		}
		#endregion

		#region Create an role
		async Task<JObject> CreateRoleAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			// prepare
			var info = requestInfo.GetBodyExpando();
			var organizationID = info.Get<string>("SystemID");
			var organization = await Utility.GetOrganizationByIDAsync(organizationID, cancellationToken).ConfigureAwait(false);
			if (organization == null)
				throw new InformationExistedException("The organization is invalid");

			// check permission
			var isSystemAdministrator = await this.IsSystemAdministratorAsync(requestInfo).ConfigureAwait(false) || await this.IsAuthorizedAsync(requestInfo, "Organization", Components.Security.Action.Approve, cancellationToken).ConfigureAwait(false);
			var gotRights = isSystemAdministrator || requestInfo.Session.User.ID.IsEquals(organization.OwnerID) || requestInfo.Session.User.IsModerator(organization.WorkingPrivileges);
			if (!gotRights)
				throw new AccessDeniedException();

			// create new
			var role = info.Copy<Role>("SystemID,Privileges,OriginalPrivileges,Created,CreatedID,LastUpdated,LastUpdatedID".ToHashSet());

			role.ID = string.IsNullOrWhiteSpace(role.ID) || !role.ID.IsValidUUID() ? UtilityService.NewUUID : role.ID;
			role.ParentID = role.ParentRole != null ? role.ParentID : null;
			role.SystemID = organization.ID;
			role.Created = role.LastModified = DateTime.Now;
			role.CreatedID = role.LastModifiedID = requestInfo.Session.User.ID;

			await Role.CreateAsync(role, cancellationToken).ConfigureAwait(false);
			await Utility.Cache.RemoveAsync(this.GetRelatedCacheKeys(Utility.GetRolesFilter(role.SystemID, role.ParentID), Sorts<Role>.Ascending("Title")), cancellationToken).ConfigureAwait(false);
			Utility.UpdateRole(role);
			if (role.ParentRole != null)
			{
				role.ParentRole.ChildrenIDs.Add(role.ID);
				Utility.UpdateRole(role.ParentRole);
			}

			// send update message
			await this.SendUpdateMessageAsync(new UpdateMessage
			{
				Type = $"{this.ServiceName}#{role.GetTypeName(true)}#Create",
				DeviceID = "*",
				ExcludedDeviceID = requestInfo.Session.DeviceID,
				Data = role.ToJson()
			}, cancellationToken).ConfigureAwait(false);

			return role.ToJson();
		}
		#endregion

		#region Get a role
		async Task<JObject> GetRoleAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			// get the role
			var role = await Role.GetAsync<Role>(requestInfo.GetObjectIdentity(), cancellationToken).ConfigureAwait(false);
			if (role == null)
				throw new InformationNotFoundException();

			// check permission
			var gotRights = await this.IsSystemAdministratorAsync(requestInfo).ConfigureAwait(false);
			if (!gotRights)
				gotRights = await this.IsAuthorizedAsync(requestInfo, role.ID, Components.Security.Action.View).ConfigureAwait(false);
			if (!gotRights)
				throw new AccessDeniedException();

			// response
			return role.ToJson();
		}
		#endregion

		#region Update a role
		async Task<JObject> UpdateRoleAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			// prepare
			var role = await Utility.GetRoleByIDAsync(requestInfo.GetObjectIdentity(), cancellationToken).ConfigureAwait(false);
			if (role == null)
				throw new InformationNotFoundException();
			else if (role.Organization == null)
				throw new InformationExistedException("The organization is invalid");

			// check permission
			var isSystemAdministrator = await this.IsSystemAdministratorAsync(requestInfo).ConfigureAwait(false) || await this.IsAuthorizedAsync(requestInfo, "Organization", Components.Security.Action.Approve, cancellationToken).ConfigureAwait(false);
			var gotRights = isSystemAdministrator || requestInfo.Session.User.ID.IsEquals(role.Organization.OwnerID) || requestInfo.Session.User.IsModerator(role.Organization.WorkingPrivileges);
			if (!gotRights)
				throw new AccessDeniedException();

			// update
			var info = requestInfo.GetBodyExpando();
			var oldParentID = role.ParentID;

			role.CopyFrom(info, "ID,SystemID,Privileges,OriginalPrivileges,Created,CreatedID,LastUpdated,LastUpdatedID".ToHashSet());
			role.LastModified = DateTime.Now;
			role.LastModifiedID = requestInfo.Session.User.ID;

			await Role.UpdateAsync(role, requestInfo.Session.User.ID, cancellationToken).ConfigureAwait(false);
			await Utility.Cache.RemoveAsync(this.GetRelatedCacheKeys(Utility.GetRolesFilter(role.SystemID, role.ParentID), Sorts<Role>.Ascending("Title")), cancellationToken).ConfigureAwait(false);
			Utility.UpdateRole(role);
			if (role.ParentRole != null)
			{
				role.ParentRole.ChildrenIDs.Add(role.ID);
				Utility.UpdateRole(role.ParentRole);
				await Utility.Cache.SetAsync(role.ParentRole, cancellationToken).ConfigureAwait(false);
			}
			if (!string.IsNullOrWhiteSpace(oldParentID) && !oldParentID.IsEquals(role.ParentID))
			{
				var parentRole = await Utility.GetRoleByIDAsync(oldParentID, cancellationToken).ConfigureAwait(false);
				if (parentRole != null)
				{
					parentRole.ChildrenIDs.Remove(role.ID);
					Utility.UpdateRole(parentRole);
					await Utility.Cache.SetAsync(parentRole, cancellationToken).ConfigureAwait(false);
				}
			}

			// send update message
			await this.SendUpdateMessageAsync(new UpdateMessage
			{
				Type = $"{this.ServiceName}#{role.GetTypeName(true)}#Update",
				DeviceID = "*",
				ExcludedDeviceID = requestInfo.Session.DeviceID,
				Data = role.ToJson()
			}, cancellationToken).ConfigureAwait(false);

			return role.ToJson();
		}
		#endregion

		#region Delete an role
		Task<JObject> DeleteRoleAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			throw new MethodNotAllowedException(requestInfo.Verb);
		}
		#endregion

		Task<JObject> ProcessSiteAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			switch (requestInfo.Verb)
			{
				case "GET":
					return this.GetSiteAsync(requestInfo, cancellationToken);

				case "POST":
					return this.CreateSiteAsync(requestInfo, cancellationToken);

				case "PUT":
					return this.UpdateSiteAsync(requestInfo, cancellationToken);
			}

			return Task.FromException<JObject>(new MethodNotAllowedException(requestInfo.Verb));
		}

		#region Create a site of an organization
		async Task<JObject> CreateSiteAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			// prepare identity
			var id = requestInfo.GetObjectIdentity() ?? requestInfo.Session.User.ID;

			// check permission
			var isSystemAdministrator = await this.IsSystemAdministratorAsync(requestInfo).ConfigureAwait(false);
			if (requestInfo.Extra != null && requestInfo.Extra.ContainsKey("x-convert"))
			{
				if (!isSystemAdministrator)
					throw new AccessDeniedException();
			}

			// check permission on create
			else
			{
				var gotRights = isSystemAdministrator || (this.IsAuthenticated(requestInfo) && requestInfo.Session.User.ID.IsEquals(id));
				if (!gotRights)
					throw new AccessDeniedException();
			}

			// create site profile
			var site = requestInfo.GetBodyJson().Copy<Site>();

			// reassign identity
			if (requestInfo.Extra == null || !requestInfo.Extra.ContainsKey("x-convert"))
				site.ID = id;

			// update database
			await Site.CreateAsync(site, cancellationToken).ConfigureAwait(false);
			return site.ToJson();
		}
		#endregion

		#region Get a site of an organization
		async Task<JObject> GetSiteAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			// check permissions
			var id = requestInfo.GetObjectIdentity() ?? requestInfo.Session.User.ID;
			var gotRights = this.IsAuthenticated(requestInfo) && requestInfo.Session.User.ID.IsEquals(id);
			if (!gotRights)
				gotRights = await this.IsSystemAdministratorAsync(requestInfo).ConfigureAwait(false);
			if (!gotRights)
				gotRights = await this.IsAuthorizedAsync(requestInfo, "site", Components.Security.Action.View, cancellationToken).ConfigureAwait(false);
			if (!gotRights)
				throw new AccessDeniedException();

			// get information
			var site = await Site.GetAsync<Site>(id, cancellationToken).ConfigureAwait(false);

			// special: not found
			if (site == null)
			{
				if (id.Equals(requestInfo.Session.User.ID))
				{
					site = new Site()
					{
						ID = id
					};
					await Site.CreateAsync(site).ConfigureAwait(false);
				}
				else
					throw new InformationNotFoundException();
			}

			// return JSON
			return site.ToJson();
		}
		#endregion

		#region Update a site of an organization
		async Task<JObject> UpdateSiteAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			// check permissions
			var id = requestInfo.GetObjectIdentity() ?? requestInfo.Session.User.ID;
			//var gotRights = this.IsAuthenticated(requestInfo) && requestInfo.Session.User.ID.IsEquals(id);
			//if (!gotRights)
			//	gotRights = await this.IsSystemAdministratorAsync(requestInfo).ConfigureAwait(false);
			//if (!gotRights)
			//	gotRights = await this.IsAuthorizedAsync(requestInfo, Components.Security.Action.Update, null, this.GetPrivileges, this.GetPrivilegeActions).ConfigureAwait(false);
			//if (!gotRights)
			//	throw new AccessDeniedException();

			// get existing information
			var site = await Site.GetAsync<Site>(id, cancellationToken).ConfigureAwait(false);
			if (site == null)
				throw new InformationNotFoundException();

			// update
			site.CopyFrom(requestInfo.GetBodyJson());
			site.ID = id;

			await Site.UpdateAsync(site, requestInfo.Session.User.ID, cancellationToken).ConfigureAwait(false);
			return site.ToJson();
		}
		#endregion

		#region Delete a site of an organization
		Task<JObject> DeleteSiteAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			throw new MethodNotAllowedException(requestInfo.Verb);
		}
		#endregion

	}
}
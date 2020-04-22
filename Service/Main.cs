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

					case "desktop":
						json = await this.ProcessDesktopAsync(requestInfo, cancellationToken).ConfigureAwait(false);
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

							case "desktop":
								json = this.GenerateFormControls<Desktop>();
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
					return "search".IsEquals(requestInfo.GetObjectIdentity())
						? this.SearchOrganizationsAsync(requestInfo, cancellationToken)
						: this.GetOrganizationAsync(requestInfo, cancellationToken);

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
				? await Utility.Cache.GetAsync<string>(this.GetCacheKeyOfObjectsJson(filter, sort, pageSize, pageNumber), cancellationToken).ConfigureAwait(false)
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
					? await Organization.FindAsync(filter, sort, pageSize, pageNumber, this.GetCacheKey(filter, sort, pageSize, pageNumber), cancellationToken).ConfigureAwait(false)
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
				await Utility.Cache.SetAsync(this.GetCacheKeyOfObjectsJson(filter, sort, pageSize, pageNumber), json, Utility.Cache.ExpirationTime / 2).ConfigureAwait(false);
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
				var existing = await Utility.GetOrganizationByAliasAsync(alias.NormalizeAlias(false), cancellationToken).ConfigureAwait(false);
				if (existing != null)
					throw new InformationExistedException($"The alias ({alias.NormalizeAlias(false)}) is used by another organization");
			}

			// prepare
			var organization = info.Copy<Organization>("Status,Instructions,Privileges,OriginalPrivileges,Created,CreatedID,LastModified,LastModifiedID".ToHashSet());

			organization.ID = string.IsNullOrWhiteSpace(organization.ID) || !organization.ID.IsValidUUID() ? UtilityService.NewUUID : organization.ID;
			organization.Alias = string.IsNullOrWhiteSpace(organization.Alias) ? organization.Title.NormalizeAlias(false) + organization.ID : organization.Alias.NormalizeAlias(false);
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
				var existing = await Utility.GetOrganizationByAliasAsync(alias.NormalizeAlias(false), cancellationToken).ConfigureAwait(false);
				if (existing != null && !existing.ID.Equals(organization.ID))
					throw new InformationExistedException($"The alias ({alias.NormalizeAlias(false)}) is used by another organization");
			}

			// prepare
			organization.CopyFrom(info, "ID,OwnerID,Status,Instructions,Privileges,OriginalPrivileges,Created,CreatedID,LastModified,LastModifiedID".ToHashSet());

			organization.OwnerID = isSystemAdministrator ? info.Get("OwnerID", organization.OwnerID) : organization.OwnerID;
			organization.Alias = string.IsNullOrWhiteSpace(organization.Alias) ? oldAlias : organization.Alias.NormalizeAlias(false);

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
					return "search".IsEquals(requestInfo.GetObjectIdentity())
						? this.SearchRolesAsync(requestInfo, cancellationToken)
						: this.GetRoleAsync(requestInfo, cancellationToken);

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
				? await Utility.Cache.GetAsync<string>(this.GetCacheKeyOfObjectsJson(filter, sort, pageSize, pageNumber), cancellationToken).ConfigureAwait(false)
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
					? await Role.FindAsync(filter, sort, pageSize, pageNumber, this.GetCacheKey(filter, sort, pageSize, pageNumber), cancellationToken).ConfigureAwait(false)
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
				await Utility.Cache.SetAsync(this.GetCacheKeyOfObjectsJson(filter, sort, pageSize, pageNumber), json, Utility.Cache.ExpirationTime / 2).ConfigureAwait(false);
			}

			// return the result
			return result;
		}
		#endregion

		#region Create a role
		async Task<JObject> CreateRoleAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			// prepare
			var info = requestInfo.GetBodyExpando();
			var organizationID = info.Get<string>("SystemID");
			var organization = await Utility.GetOrganizationByIDAsync(organizationID, cancellationToken).ConfigureAwait(false);
			if (organization == null)
				throw new InformationInvalidException("The organization is invalid");

			// check permission
			var isSystemAdministrator = await this.IsSystemAdministratorAsync(requestInfo).ConfigureAwait(false) || await this.IsAuthorizedAsync(requestInfo, "Organization", Components.Security.Action.Approve, cancellationToken).ConfigureAwait(false);
			var gotRights = isSystemAdministrator || requestInfo.Session.User.ID.IsEquals(organization.OwnerID) || requestInfo.Session.User.IsModerator(organization.WorkingPrivileges);
			if (!gotRights)
				throw new AccessDeniedException();

			// create new
			var role = info.Copy<Role>("SystemID,Privileges,OriginalPrivileges,Created,CreatedID,LastModified,LastModifiedID".ToHashSet());

			role.ID = string.IsNullOrWhiteSpace(role.ID) || !role.ID.IsValidUUID() ? UtilityService.NewUUID : role.ID;
			role.SystemID = organization.ID;
			role.ParentID = role.ParentRole != null ? role.ParentID : null;
			role.Created = role.LastModified = DateTime.Now;
			role.CreatedID = role.LastModifiedID = requestInfo.Session.User.ID;
			role._childrenIDs = new List<string>();

			await Task.WhenAll(
				Role.CreateAsync(role, cancellationToken),
				Utility.Cache.RemoveAsync(this.GetRelatedCacheKeys(Utility.GetRolesFilter(role.SystemID, role.ParentID), Sorts<Role>.Ascending("Title")), cancellationToken),
				Utility.UpdateRoleAsync(role, false, cancellationToken)
			).ConfigureAwait(false);

			// update parent
			List<UpdateMessage> updateMessages = new List<UpdateMessage>();

			if (role.ParentRole != null)
			{
				await role.ParentRole.GetChildrenAsync(cancellationToken, false).ConfigureAwait(false);
				role.ParentRole._childrenIDs.Add(role.ID);
				await Utility.UpdateRoleAsync(role.ParentRole, true).ConfigureAwait(false);

				updateMessages.Add(new UpdateMessage
				{
					Type = $"{this.ServiceName}#{role.GetTypeName(true)}#Update",
					DeviceID = "*",
					Data = role.ParentRole.ToJson(true, false)
				});
			}

			// send update message and response
			var response = role.ToJson(true, false);

			if (role.ParentRole == null)
				updateMessages.Add(new UpdateMessage
				{
					Type = $"{this.ServiceName}#{role.GetTypeName(true)}#Update",
					DeviceID = "*",
					ExcludedDeviceID = requestInfo.Session.DeviceID,
					Data = response
				});

			await updateMessages.ForEachAsync((updateMessage, token) => this.SendUpdateMessageAsync(updateMessage, token), cancellationToken, true, false).ConfigureAwait(false);
			return response;
		}
		#endregion

		#region Get a role
		async Task<JObject> GetRoleAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			// prepare
			var role = await Utility.GetRoleByIDAsync(requestInfo.GetObjectIdentity(), cancellationToken).ConfigureAwait(false);
			if (role == null)
				throw new InformationNotFoundException();
			else if (role.Organization == null)
				throw new InformationInvalidException("The organization is invalid");

			// check permission
			var isSystemAdministrator = await this.IsSystemAdministratorAsync(requestInfo).ConfigureAwait(false) || await this.IsAuthorizedAsync(requestInfo, "Organization", Components.Security.Action.Approve, cancellationToken).ConfigureAwait(false);
			var gotRights = isSystemAdministrator || requestInfo.Session.User.ID.IsEquals(role.Organization.OwnerID) || requestInfo.Session.User.IsViewer(role.Organization.WorkingPrivileges);
			if (!gotRights)
				throw new AccessDeniedException();

			// prepare the response
			if (role._childrenIDs == null)
			{
				await role.GetChildrenAsync(cancellationToken, false).ConfigureAwait(false);
				await Utility.UpdateRoleAsync(role, true, cancellationToken).ConfigureAwait(false);
			}

			var response = role.ToJson(true, false);

			// send update message and response
			await this.SendUpdateMessageAsync(new UpdateMessage
			{
				Type = $"{this.ServiceName}#{role.GetTypeName(true)}#Update",
				DeviceID = "*",
				ExcludedDeviceID = requestInfo.Session.DeviceID,
				Data = response
			}, cancellationToken).ConfigureAwait(false);

			return response;
		}
		#endregion

		#region Update a role
		async Task<JObject> UpdateRoleAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			// prepare
			var roleID = requestInfo.GetObjectIdentity();
			var role = await Utility.GetRoleByIDAsync(roleID, cancellationToken).ConfigureAwait(false);
			if (role == null)
				throw new InformationNotFoundException();
			else if (role.Organization == null)
				throw new InformationInvalidException("The organization is invalid");

			// check permission
			var isSystemAdministrator = await this.IsSystemAdministratorAsync(requestInfo).ConfigureAwait(false) || await this.IsAuthorizedAsync(requestInfo, "Organization", Components.Security.Action.Approve, cancellationToken).ConfigureAwait(false);
			var gotRights = isSystemAdministrator || requestInfo.Session.User.ID.IsEquals(role.Organization.OwnerID) || requestInfo.Session.User.IsModerator(role.Organization.WorkingPrivileges);
			if (!gotRights)
				throw new AccessDeniedException();

			// update
			var oldParentID = role.ParentID;
			var info = requestInfo.GetBodyExpando();

			role.CopyFrom(info, "ID,SystemID,Privileges,OriginalPrivileges,Created,CreatedID,LastModified,LastModifiedID".ToHashSet());
			role.LastModified = DateTime.Now;
			role.LastModifiedID = requestInfo.Session.User.ID;
			await role.GetChildrenAsync(cancellationToken, false).ConfigureAwait(false);

			await Task.WhenAll(
				Role.UpdateAsync(role, requestInfo.Session.User.ID, cancellationToken),
				Utility.UpdateRoleAsync(role, false, cancellationToken)
			).ConfigureAwait(false);

			await Task.WhenAll(
				Utility.Cache.RemoveAsync(this.GetRelatedCacheKeys(Utility.GetRolesFilter(role.SystemID, role.ParentID), Sorts<Role>.Ascending("Title")), cancellationToken),
				Utility.Cache.RemoveAsync(this.GetRelatedCacheKeys(Utility.GetRolesFilter(role.SystemID, oldParentID), Sorts<Role>.Ascending("Title")), cancellationToken)
			).ConfigureAwait(false);

			// update parent
			List<UpdateMessage> updateMessages = new List<UpdateMessage>();

			if (role.ParentRole != null && !role.ParentID.IsEquals(oldParentID))
			{
				await role.ParentRole.GetChildrenAsync(cancellationToken, false).ConfigureAwait(false);
				role.ParentRole._childrenIDs.Add(role.ID);
				await Utility.UpdateRoleAsync(role.ParentRole, true).ConfigureAwait(false);

				updateMessages.Add(new UpdateMessage
				{
					Type = $"{this.ServiceName}#{role.GetTypeName(true)}#Update",
					DeviceID = "*",
					Data = role.ParentRole.ToJson(true, false)
				});
			}

			if (!string.IsNullOrWhiteSpace(oldParentID) && !oldParentID.IsEquals(role.ParentID))
			{
				var parentRole = await Utility.GetRoleByIDAsync(oldParentID, cancellationToken).ConfigureAwait(false);
				if (parentRole != null)
				{
					await parentRole.GetChildrenAsync(cancellationToken, false).ConfigureAwait(false);
					parentRole._childrenIDs.Remove(role.ID);
					await Utility.UpdateRoleAsync(parentRole, true).ConfigureAwait(false);

					updateMessages.Add(new UpdateMessage
					{
						Type = $"{this.ServiceName}#{role.GetTypeName(true)}#Update",
						DeviceID = "*",
						Data = parentRole.ToJson(true, false)
					});
				}
			}

			// send update message and response
			var response = role.ToJson(true, false);

			if (string.IsNullOrWhiteSpace(oldParentID) && role.ParentRole == null)
				updateMessages.Add(new UpdateMessage
				{
					Type = $"{this.ServiceName}#{role.GetTypeName(true)}#Update",
					DeviceID = "*",
					ExcludedDeviceID = requestInfo.Session.DeviceID,
					Data = response
				});

			await updateMessages.ForEachAsync((updateMessage, token) => this.SendUpdateMessageAsync(updateMessage, token), cancellationToken, true, false).ConfigureAwait(false);
			return response;
		}
		#endregion

		#region Delete a role
		async Task<JObject> DeleteRoleAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			// prepare
			var role = await Role.GetAsync<Role>(requestInfo.GetObjectIdentity(), cancellationToken).ConfigureAwait(false);
			if (role == null)
				throw new InformationNotFoundException();

			var organization = await Utility.GetOrganizationByIDAsync(role.SystemID, cancellationToken).ConfigureAwait(false);
			if (organization == null)
				throw new InformationExistedException("The organization is invalid");

			// check permission
			var isSystemAdministrator = await this.IsSystemAdministratorAsync(requestInfo).ConfigureAwait(false) || await this.IsAuthorizedAsync(requestInfo, "Organization", Components.Security.Action.Approve, cancellationToken).ConfigureAwait(false);
			var gotRights = isSystemAdministrator || requestInfo.Session.User.ID.IsEquals(organization.OwnerID) || requestInfo.Session.User.IsModerator(organization.WorkingPrivileges);
			if (!gotRights)
				throw new AccessDeniedException();

			// delete
			List<UpdateMessage> updateMessages = new List<UpdateMessage>();
			var updateChildren = requestInfo.Header.TryGetValue("x-children", out var childrenMode) && "set-null".IsEquals(childrenMode);

			await (await role.GetChildrenAsync(cancellationToken, false).ConfigureAwait(false)).ForEachAsync(async (child, token) =>
			{
				if (updateChildren)
				{
					child.ParentID = null;
					child.LastModified = DateTime.Now;
					child.LastModifiedID = requestInfo.Session.User.ID;
					await Role.UpdateAsync(child, requestInfo.Session.User.ID, token).ConfigureAwait(false);
					Utility.UpdateRole(child);
					updateMessages.Add(new UpdateMessage
					{
						Type = $"{this.ServiceName}#{role.GetTypeName(true)}#Update",
						DeviceID = "*",
						Data = child.ToJson(true, false)
					});
				}
				else
					updateMessages = updateMessages.Concat(await this.DeleteChildRoleAsync(child, requestInfo.Session.User.ID, token).ConfigureAwait(false)).ToList();
			}, cancellationToken, true, false).ConfigureAwait(false);

			await Role.DeleteAsync<Role>(role.ID, requestInfo.Session.User.ID, cancellationToken).ConfigureAwait(false);
			await Task.WhenAll(
				updateChildren ? Utility.Cache.RemoveAsync(this.GetRelatedCacheKeys(Utility.GetRolesFilter(role.SystemID, null), Sorts<Role>.Ascending("Title")), cancellationToken) : Task.CompletedTask,
				Utility.Cache.RemoveAsync(this.GetRelatedCacheKeys(Utility.GetRolesFilter(role.SystemID, role.ParentID), Sorts<Role>.Ascending("Title")), cancellationToken)
			).ConfigureAwait(false);
			Utility.Roles.Remove(role.ID);

			var response = role.ToJson();
			updateMessages.Add(new UpdateMessage
			{
				Type = $"{this.ServiceName}#{role.GetTypeName(true)}#Delete",
				DeviceID = "*",
				Data = response
			});

			await updateMessages.ForEachAsync((updateMessage, token) => this.SendUpdateMessageAsync(updateMessage, token), cancellationToken, true, false).ConfigureAwait(false);
			return response;
		}

		async Task<List<UpdateMessage>> DeleteChildRoleAsync(Role role, string userID, CancellationToken cancellationToken)
		{
			List<UpdateMessage> updateMessages = new List<UpdateMessage>();

			var children = await role.GetChildrenAsync(cancellationToken, false).ConfigureAwait(false);
			await children.ForEachAsync(async (child, token) =>
			{
				updateMessages = updateMessages.Concat(await this.DeleteChildRoleAsync(child, userID, token).ConfigureAwait(false)).ToList();
			}, cancellationToken, true, false).ConfigureAwait(false);

			await Role.DeleteAsync<Role>(role.ID, userID, cancellationToken).ConfigureAwait(false);
			Utility.Roles.Remove(role.ID);

			updateMessages.Add(new UpdateMessage
			{
				Type = $"{this.ServiceName}#{role.GetTypeName(true)}#Delete",
				DeviceID = "*",
				Data = role.ToJson()
			});
			return updateMessages;
		}
		#endregion

		Task<JObject> ProcessDesktopAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			switch (requestInfo.Verb)
			{
				case "GET":
					return "search".IsEquals(requestInfo.GetObjectIdentity())
						? this.SearchDesktopsAsync(requestInfo, cancellationToken)
						: this.GetDesktopAsync(requestInfo, cancellationToken);

				case "POST":
					return this.CreateDesktopAsync(requestInfo, cancellationToken);

				case "PUT":
					return this.UpdateDesktopAsync(requestInfo, cancellationToken);

				case "DELETE":
					return this.DeleteDesktopAsync(requestInfo, cancellationToken);
			}

			return Task.FromException<JObject>(new MethodNotAllowedException(requestInfo.Verb));
		}

		#region Search desktops
		async Task<JObject> SearchDesktopsAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			// prepare
			var request = requestInfo.GetRequestExpando();

			var query = request.Get<string>("FilterBy.Query");

			var filter = request.Get<ExpandoObject>("FilterBy", null)?.ToFilterBy<Desktop>() ?? Filters<Desktop>.And();
			if (filter is FilterBys<Desktop> && (filter as FilterBys<Desktop>).Children.FirstOrDefault(exp => (exp as FilterBy<Desktop>).Attribute.IsEquals("ParentID")) == null)
				(filter as FilterBys<Desktop>).Children.Add(Filters<Desktop>.IsNull("ParentID"));

			var sort = request.Get<ExpandoObject>("SortBy", null)?.ToSortBy<Desktop>();
			if (string.IsNullOrWhiteSpace(query))
				sort = sort ?? Sorts<Desktop>.Ascending("Title");
			else if (filter is FilterBys<Desktop>)
			{
				var index = (filter as FilterBys<Desktop>).Children.FindIndex(exp => (exp as FilterBy<Desktop>).Attribute.IsEquals("ParentID"));
				if (index > -1)
					(filter as FilterBys<Desktop>).Children.RemoveAt(index);
			}

			var pagination = request.Has("Pagination")
				? request.Get<ExpandoObject>("Pagination").GetPagination()
				: new Tuple<long, int, int, int>(-1, 0, 20, 1);

			var pageSize = pagination.Item3;
			var pageNumber = pagination.Item4;

			// get organization
			var organizationID = filter != null && filter is FilterBys<Desktop>
				? ((filter as FilterBys<Desktop>).Children.FirstOrDefault(exp => (exp as FilterBy<Desktop>).Attribute.IsEquals("SystemID")) as FilterBy<Desktop>)?.Value as string
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
				? await Utility.Cache.GetAsync<string>(this.GetCacheKeyOfObjectsJson(filter, sort, pageSize, pageNumber), cancellationToken).ConfigureAwait(false)
				: "";

			if (!string.IsNullOrWhiteSpace(json))
				return JObject.Parse(json);

			// prepare pagination
			var totalRecords = pagination.Item1 > -1
				? pagination.Item1
				: -1;

			if (totalRecords < 0)
				totalRecords = string.IsNullOrWhiteSpace(query)
					? await Desktop.CountAsync(filter, this.GetCacheKeyOfTotalObjects(filter, sort), cancellationToken).ConfigureAwait(false)
					: await Desktop.CountAsync(query, filter, cancellationToken).ConfigureAwait(false);

			var totalPages = (new Tuple<long, int>(totalRecords, pageSize)).GetTotalPages();
			if (totalPages > 0 && pageNumber > totalPages)
				pageNumber = totalPages;

			// search
			var objects = totalRecords > 0
				? string.IsNullOrWhiteSpace(query)
					? await Desktop.FindAsync(filter, sort, pageSize, pageNumber, this.GetCacheKey(filter, sort, pageSize, pageNumber), cancellationToken).ConfigureAwait(false)
					: await Desktop.SearchAsync(query, filter, pageSize, pageNumber, cancellationToken).ConfigureAwait(false)
				: new List<Desktop>();

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
				await Utility.Cache.SetAsync(this.GetCacheKeyOfObjectsJson(filter, sort, pageSize, pageNumber), json, Utility.Cache.ExpirationTime / 2).ConfigureAwait(false);
			}

			// return the result
			return result;
		}
		#endregion

		#region Create a desktop
		async Task<JObject> CreateDesktopAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			// prepare
			var info = requestInfo.GetBodyExpando();
			var organizationID = info.Get<string>("SystemID");
			var organization = await Utility.GetOrganizationByIDAsync(organizationID, cancellationToken).ConfigureAwait(false);
			if (organization == null)
				throw new InformationInvalidException("The organization is invalid");

			// check permission
			var isSystemAdministrator = await this.IsSystemAdministratorAsync(requestInfo).ConfigureAwait(false) || await this.IsAuthorizedAsync(requestInfo, "Organization", Components.Security.Action.Approve, cancellationToken).ConfigureAwait(false);
			var gotRights = isSystemAdministrator || requestInfo.Session.User.ID.IsEquals(organization.OwnerID) || requestInfo.Session.User.IsModerator(organization.WorkingPrivileges);
			if (!gotRights)
				throw new AccessDeniedException();

			// check alias
			var alias = info.Get<string>("Alias");
			if (!string.IsNullOrWhiteSpace(alias))
			{
				var existing = await Utility.GetDesktopByAliasAsync(organization.ID, alias.NormalizeAlias(), cancellationToken).ConfigureAwait(false);
				if (existing != null)
					throw new InformationExistedException($"The alias ({alias.NormalizeAlias()}) is used by another desktop");
			}

			// create new
			var desktop = info.Copy<Desktop>("SystemID,Privileges,OriginalPrivileges,Created,CreatedID,LastModified,LastModifiedID".ToHashSet());
			desktop.ID = string.IsNullOrWhiteSpace(desktop.ID) || !desktop.ID.IsValidUUID() ? UtilityService.NewUUID : desktop.ID;
			desktop.Alias = string.IsNullOrWhiteSpace(desktop.Alias) ? desktop.Title.NormalizeAlias() : desktop.Alias.NormalizeAlias();
			desktop.Aliases = string.IsNullOrWhiteSpace(desktop.Aliases) ? null : desktop.Aliases.ToList(";", true, true).Select(a => a.NormalizeAlias()).Where(a => !a.IsEquals(desktop.Alias)).Join(";");

			desktop.SEOSettings = desktop.SEOSettings ?? new Settings.SEO();
			"TitleMode,DescriptionMode,KeywordsMode".ToList().ForEach(name =>
			{
				var value = info.Get<string>($"SEOSettings.{name}");
				desktop.SEOSettings.SetAttributeValue(name, !string.IsNullOrWhiteSpace(value) && value.TryToEnum(out Settings.SEOMode mode) ? mode as object : null);
			});

			desktop.SystemID = organization.ID;
			desktop.ParentID = desktop.ParentDesktop != null ? desktop.ParentID : null;
			desktop.Created = desktop.LastModified = DateTime.Now;
			desktop.CreatedID = desktop.LastModifiedID = requestInfo.Session.User.ID;
			desktop._childrenIDs = new List<string>();

			await Task.WhenAll(
				Desktop.CreateAsync(desktop, cancellationToken),
				Utility.Cache.RemoveAsync(this.GetRelatedCacheKeys(Utility.GetDesktopsFilter(desktop.SystemID, desktop.ParentID), Sorts<Desktop>.Ascending("Title")), cancellationToken),
				Utility.UpdateDesktopAsync(desktop, false, false, cancellationToken)
			).ConfigureAwait(false);

			// update parent
			List<UpdateMessage> updateMessages = new List<UpdateMessage>();

			if (desktop.ParentDesktop != null)
			{
				await desktop.ParentDesktop.GetChildrenAsync(cancellationToken, false).ConfigureAwait(false);
				desktop.ParentDesktop._childrenIDs.Add(desktop.ID);
				await Utility.UpdateDesktopAsync(desktop.ParentDesktop, false, true, cancellationToken).ConfigureAwait(false);

				updateMessages.Add(new UpdateMessage
				{
					Type = $"{this.ServiceName}#{desktop.GetTypeName(true)}#Update",
					DeviceID = "*",
					Data = desktop.ParentDesktop.ToJson(true, false)
				});
			}

			// send update message and response
			var response = desktop.ToJson(true, false);

			if (desktop.ParentDesktop == null)
				updateMessages.Add(new UpdateMessage
				{
					Type = $"{this.ServiceName}#{desktop.GetTypeName(true)}#Update",
					DeviceID = "*",
					ExcludedDeviceID = requestInfo.Session.DeviceID,
					Data = response
				});

			await updateMessages.ForEachAsync((updateMessage, token) => this.SendUpdateMessageAsync(updateMessage, token), cancellationToken, true, false).ConfigureAwait(false);
			return response;
		}
		#endregion

		#region Get a desktop
		async Task<JObject> GetDesktopAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			// prepare
			var desktop = await Utility.GetDesktopByIDAsync(requestInfo.GetObjectIdentity(), cancellationToken).ConfigureAwait(false);
			if (desktop == null)
				throw new InformationNotFoundException();
			else if (desktop.Organization == null)
				throw new InformationInvalidException("The organization is invalid");

			// check permission
			var isSystemAdministrator = await this.IsSystemAdministratorAsync(requestInfo).ConfigureAwait(false) || await this.IsAuthorizedAsync(requestInfo, "Organization", Components.Security.Action.Approve, cancellationToken).ConfigureAwait(false);
			var gotRights = isSystemAdministrator || requestInfo.Session.User.ID.IsEquals(desktop.Organization.OwnerID) || requestInfo.Session.User.IsViewer(desktop.Organization.WorkingPrivileges);
			if (!gotRights)
				throw new AccessDeniedException();

			// prepare the response
			if (desktop._childrenIDs == null)
			{
				await desktop.GetChildrenAsync(cancellationToken, false).ConfigureAwait(false);
				await Utility.UpdateDesktopAsync(desktop, false, true, cancellationToken).ConfigureAwait(false);
			}

			var response = desktop.ToJson(true, false);

			// send update message and response
			await this.SendUpdateMessageAsync(new UpdateMessage
			{
				Type = $"{this.ServiceName}#{desktop.GetTypeName(true)}#Update",
				DeviceID = "*",
				ExcludedDeviceID = requestInfo.Session.DeviceID,
				Data = response
			}, cancellationToken).ConfigureAwait(false);

			return response;
		}
		#endregion

		#region Update a desktop
		async Task<JObject> UpdateDesktopAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			// prepare
			var desktopID = requestInfo.GetObjectIdentity();
			var desktop = await Utility.GetDesktopByIDAsync(desktopID, cancellationToken).ConfigureAwait(false);
			if (desktop == null)
				throw new InformationNotFoundException();
			else if (desktop.Organization == null)
				throw new InformationInvalidException("The organization is invalid");

			// check permission
			var isSystemAdministrator = await this.IsSystemAdministratorAsync(requestInfo).ConfigureAwait(false) || await this.IsAuthorizedAsync(requestInfo, "Organization", Components.Security.Action.Approve, cancellationToken).ConfigureAwait(false);
			var gotRights = isSystemAdministrator || requestInfo.Session.User.ID.IsEquals(desktop.Organization.OwnerID) || requestInfo.Session.User.IsModerator(desktop.Organization.WorkingPrivileges);
			if (!gotRights)
				throw new AccessDeniedException();

			// update
			var info = requestInfo.GetBodyExpando();
			var oldParentID = desktop.ParentID;

			var oldAlias = desktop.Alias;
			var alias = info.Get<string>("Alias");
			if (!string.IsNullOrWhiteSpace(alias))
			{
				var existing = await Utility.GetDesktopByAliasAsync(desktop.SystemID, alias.NormalizeAlias(), cancellationToken).ConfigureAwait(false);
				if (existing != null && !existing.ID.Equals(desktop.ID))
					throw new InformationExistedException($"The alias ({alias.NormalizeAlias()}) is used by another desktop");
			}

			desktop.CopyFrom(info, "ID,SystemID,Privileges,OriginalPrivileges,Created,CreatedID,LastModified,LastModifiedID".ToHashSet());

			desktop.Alias = string.IsNullOrWhiteSpace(desktop.Alias) ? oldAlias : desktop.Alias.NormalizeAlias();
			desktop.Aliases = string.IsNullOrWhiteSpace(desktop.Aliases) ? null : desktop.Aliases.ToList(";", true, true).Select(a => a.NormalizeAlias()).Where(a => !a.IsEquals(desktop.Alias)).Join(";");

			desktop.SEOSettings = desktop.SEOSettings ?? new Settings.SEO();
			"TitleMode,DescriptionMode,KeywordsMode".ToList().ForEach(name =>
			{
				var value = info.Get<string>($"SEOSettings.{name}");
				desktop.SEOSettings.SetAttributeValue(name, !string.IsNullOrWhiteSpace(value) && value.TryToEnum(out Settings.SEOMode mode) ? mode as object : null);
			});

			desktop.LastModified = DateTime.Now;
			desktop.LastModifiedID = requestInfo.Session.User.ID;
			await desktop.GetChildrenAsync(cancellationToken, false).ConfigureAwait(false);

			await Task.WhenAll(
				Desktop.UpdateAsync(desktop, requestInfo.Session.User.ID, cancellationToken),
				Utility.UpdateDesktopAsync(desktop, false, false, cancellationToken)
			).ConfigureAwait(false);

			await Task.WhenAll(
				Utility.Cache.RemoveAsync(this.GetRelatedCacheKeys(Utility.GetDesktopsFilter(desktop.SystemID, desktop.ParentID), Sorts<Desktop>.Ascending("Title")), cancellationToken),
				Utility.Cache.RemoveAsync(this.GetRelatedCacheKeys(Utility.GetDesktopsFilter(desktop.SystemID, oldParentID), Sorts<Desktop>.Ascending("Title")), cancellationToken)
			).ConfigureAwait(false);

			// update parent
			List<UpdateMessage> updateMessages = new List<UpdateMessage>();

			if (desktop.ParentDesktop != null && !desktop.ParentID.IsEquals(oldParentID))
			{
				await desktop.ParentDesktop.GetChildrenAsync(cancellationToken, false).ConfigureAwait(false);
				desktop.ParentDesktop._childrenIDs.Add(desktop.ID);
				await Utility.UpdateDesktopAsync(desktop.ParentDesktop, false, true, cancellationToken).ConfigureAwait(false);

				updateMessages.Add(new UpdateMessage
				{
					Type = $"{this.ServiceName}#{desktop.GetTypeName(true)}#Update",
					DeviceID = "*",
					Data = desktop.ParentDesktop.ToJson(true, false)
				});
			}

			if (!string.IsNullOrWhiteSpace(oldParentID) && !oldParentID.IsEquals(desktop.ParentID))
			{
				var parentDesktop = await Utility.GetDesktopByIDAsync(oldParentID, cancellationToken).ConfigureAwait(false);
				if (parentDesktop != null)
				{
					await parentDesktop.GetChildrenAsync(cancellationToken, false).ConfigureAwait(false);
					parentDesktop._childrenIDs.Remove(desktop.ID);
					await Utility.UpdateDesktopAsync(parentDesktop, false, true, cancellationToken).ConfigureAwait(false);

					updateMessages.Add(new UpdateMessage
					{
						Type = $"{this.ServiceName}#{desktop.GetTypeName(true)}#Update",
						DeviceID = "*",
						Data = parentDesktop.ToJson(true, false)
					});
				}
			}

			// send update message and response
			var response = desktop.ToJson(true, false);

			if (string.IsNullOrWhiteSpace(oldParentID) && desktop.ParentDesktop == null)
				updateMessages.Add(new UpdateMessage
				{
					Type = $"{this.ServiceName}#{desktop.GetTypeName(true)}#Update",
					DeviceID = "*",
					ExcludedDeviceID = requestInfo.Session.DeviceID,
					Data = response
				});

			await updateMessages.ForEachAsync((updateMessage, token) => this.SendUpdateMessageAsync(updateMessage, token), cancellationToken, true, false).ConfigureAwait(false);
			return response;
		}
		#endregion

		#region Delete a desktop
		async Task<JObject> DeleteDesktopAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			// prepare
			var desktop = await Desktop.GetAsync<Desktop>(requestInfo.GetObjectIdentity(), cancellationToken).ConfigureAwait(false);
			if (desktop == null)
				throw new InformationNotFoundException();

			var organization = await Utility.GetOrganizationByIDAsync(desktop.SystemID, cancellationToken).ConfigureAwait(false);
			if (organization == null)
				throw new InformationExistedException("The organization is invalid");

			// check permission
			var isSystemAdministrator = await this.IsSystemAdministratorAsync(requestInfo).ConfigureAwait(false) || await this.IsAuthorizedAsync(requestInfo, "Organization", Components.Security.Action.Approve, cancellationToken).ConfigureAwait(false);
			var gotRights = isSystemAdministrator || requestInfo.Session.User.ID.IsEquals(organization.OwnerID) || requestInfo.Session.User.IsModerator(organization.WorkingPrivileges);
			if (!gotRights)
				throw new AccessDeniedException();

			// delete
			List<UpdateMessage> updateMessages = new List<UpdateMessage>();
			var updateChildren = requestInfo.Header.TryGetValue("x-children", out var childrenMode) && "set-null".IsEquals(childrenMode);

			await (await desktop.GetChildrenAsync(cancellationToken, false).ConfigureAwait(false)).ForEachAsync(async (child, token) =>
			{
				if (updateChildren)
				{
					child.ParentID = null;
					child.LastModified = DateTime.Now;
					child.LastModifiedID = requestInfo.Session.User.ID;
					await Desktop.UpdateAsync(child, requestInfo.Session.User.ID, token).ConfigureAwait(false);
					Utility.UpdateDesktop(child);
					updateMessages.Add(new UpdateMessage
					{
						Type = $"{this.ServiceName}#{desktop.GetTypeName(true)}#Update",
						DeviceID = "*",
						Data = child.ToJson(true, false)
					});
				}
				else
					updateMessages = updateMessages.Concat(await this.DeleteChildDesktopAsync(child, requestInfo.Session.User.ID, token).ConfigureAwait(false)).ToList();
			}, cancellationToken, true, false).ConfigureAwait(false);

			await Desktop.DeleteAsync<Desktop>(desktop.ID, requestInfo.Session.User.ID, cancellationToken).ConfigureAwait(false);
			await Task.WhenAll(
				updateChildren ? Utility.Cache.RemoveAsync(this.GetRelatedCacheKeys(Utility.GetDesktopsFilter(desktop.SystemID, null), Sorts<Desktop>.Ascending("Title")), cancellationToken) : Task.CompletedTask,
				Utility.Cache.RemoveAsync(this.GetRelatedCacheKeys(Utility.GetDesktopsFilter(desktop.SystemID, desktop.ParentID), Sorts<Desktop>.Ascending("Title")), cancellationToken)
			).ConfigureAwait(false);
			Utility.Desktops.Remove(desktop.ID);

			var response = desktop.ToJson();
			updateMessages.Add(new UpdateMessage
			{
				Type = $"{this.ServiceName}#{desktop.GetTypeName(true)}#Delete",
				DeviceID = "*",
				Data = response
			});

			await updateMessages.ForEachAsync((updateMessage, token) => this.SendUpdateMessageAsync(updateMessage, token), cancellationToken, true, false).ConfigureAwait(false);
			return response;
		}

		async Task<List<UpdateMessage>> DeleteChildDesktopAsync(Desktop desktop, string userID, CancellationToken cancellationToken)
		{
			List<UpdateMessage> updateMessages = new List<UpdateMessage>();

			var children = await desktop.GetChildrenAsync(cancellationToken, false).ConfigureAwait(false);
			await children.ForEachAsync(async (child, token) =>
			{
				updateMessages = updateMessages.Concat(await this.DeleteChildDesktopAsync(child, userID, token).ConfigureAwait(false)).ToList();
			}, cancellationToken, true, false).ConfigureAwait(false);

			await Desktop.DeleteAsync<Desktop>(desktop.ID, userID, cancellationToken).ConfigureAwait(false);
			Utility.Desktops.Remove(desktop.ID);

			updateMessages.Add(new UpdateMessage
			{
				Type = $"{this.ServiceName}#{desktop.GetTypeName(true)}#Delete",
				DeviceID = "*",
				Data = desktop.ToJson()
			});
			return updateMessages;
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
#region Related components
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Collections.Generic;
using System.Dynamic;
using System.IO;
using System.Text;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using net.vieapps.Components.Utility;
using net.vieapps.Components.Security;
using net.vieapps.Components.Repository;
#endregion

namespace net.vieapps.Services.Systems
{
	public class ServiceComponent : ServiceBase
	{
		public ServiceComponent() : base() { }

		public override string ServiceName { get { return "systems"; } }

		public override async Task<JObject> ProcessRequestAsync(RequestInfo requestInfo, CancellationToken cancellationToken = default(CancellationToken))
		{
			// track
			var stopwatch = new Stopwatch();
			stopwatch.Start();
			var logs = new List<string>() { $"Begin process ({requestInfo.Verb}): {requestInfo.URI}" };
#if DEBUG || REQUESTLOGS
			logs.Add($"Request ==> {requestInfo.ToJson().ToString(Formatting.Indented)}");
#endif
			await this.WriteLogsAsync(requestInfo.CorrelationID, logs).ConfigureAwait(false);

			// process
			try
			{
				switch (requestInfo.ObjectName.ToLower())
				{
					case "organization":
						return await this.ProcessOrganizationAsync(requestInfo, cancellationToken).ConfigureAwait(false);

					case "site":
						return await this.ProcessSiteAsync(requestInfo, cancellationToken).ConfigureAwait(false);
				}
				throw new InvalidRequestException($"The request is invalid [({requestInfo.Verb}): {requestInfo.URI}]");
			}
			catch (Exception ex)
			{
				await this.WriteLogAsync(requestInfo.CorrelationID, "Error occurred while processing", ex).ConfigureAwait(false);
				throw this.GetRuntimeException(requestInfo, ex);
			}
			finally
			{
				stopwatch.Stop();
				await this.WriteLogAsync(requestInfo.CorrelationID, $"End process - Execution times: {stopwatch.GetElapsedTimes()}").ConfigureAwait(false);
			}
		}

		protected override List<Privilege> GetPrivileges(User user, Privileges privileges)
		{
			var role = this.GetPrivilegeRole(user);
			return "organization,site,desktop,role,module,content-type,expression".ToList()
				.Select(o => new Privilege(this.ServiceName, o, null, role))
				.ToList();
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
			// check permissions
			if (!await this.IsAuthorizedAsync(
					requestInfo, Components.Security.Action.View, null,
					(user, privileges) => this.GetPrivileges(user, privileges),
					(role) => this.GetPrivilegeActions(role)).ConfigureAwait(false)
				)
				throw new AccessDeniedException();

			// prepare
			var request = requestInfo.GetRequestExpando();

			var query = request.Get<string>("FilterBy.Query");

			var filter = request.Get<ExpandoObject>("FilterBy", null)?.ToFilterBy<Organization>();
			if (requestInfo.Extra.ContainsKey("x-refer-section"))
			{
				if (filter == null)
					filter = Filters<Organization>.Equals("ReferSection", requestInfo.Extra["x-refer-section"]);
				else if (filter is FilterBys<Organization> && (filter as FilterBys<Organization>).Children.FirstOrDefault(e => (e as FilterBy<Organization>).Attribute.IsEquals("ReferSection")) == null)
					(filter as FilterBys<Organization>).Children.Add(Filters<Organization>.Equals("ReferSection", requestInfo.Extra["x-refer-section"]));
			}

			var sort = request.Get<ExpandoObject>("SortBy", null)?.ToSortBy<Organization>();
			if (sort == null && string.IsNullOrWhiteSpace(query))
				sort = Sorts<Organization>.Ascending("Title");

			var pagination = request.Has("Pagination")
				? request.Get<ExpandoObject>("Pagination").GetPagination()
				: new Tuple<long, int, int, int>(-1, 0, 20, 1);

			var pageNumber = pagination.Item4;

			// check cache
			var cacheKey = string.IsNullOrWhiteSpace(query)
				? this.GetCacheKey<Organization>(filter, sort)
				: "";

			var json = !cacheKey.Equals("")
				? await Utility.Cache.GetAsync<string>(cacheKey + ":" + pageNumber.ToString() + "-json").ConfigureAwait(false)
				: "";

			if (!string.IsNullOrWhiteSpace(json))
				return JObject.Parse(json);

			// prepare pagination
			var totalRecords = pagination.Item1 > -1
				? pagination.Item1
				:  -1;

			if (totalRecords < 0)
				totalRecords = string.IsNullOrWhiteSpace(query)
					? await Organization.CountAsync(filter, cacheKey + "-total", cancellationToken).ConfigureAwait(false)
					: await Organization.CountByQueryAsync(query, filter, cancellationToken).ConfigureAwait(false);

			var pageSize = pagination.Item3;

			var totalPages = (new Tuple<long, int>(totalRecords, pageSize)).GetTotalPages();
			if (totalPages > 0 && pageNumber > totalPages)
				pageNumber = totalPages;

			// search
			var objects = totalRecords > 0
				? string.IsNullOrWhiteSpace(query)
					? await Organization.FindAsync(filter, sort, pageSize, pageNumber, cacheKey + ":" + pageNumber.ToString(), cancellationToken).ConfigureAwait(false)
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
			if (!cacheKey.Equals(""))
			{
#if DEBUG
				json = result.ToString(Formatting.Indented);
#else
				json = result.ToString(Formatting.None);
#endif
				await Utility.Cache.SetAsync(cacheKey + ":" + pageNumber.ToString() + "-json", json, Utility.CacheExpirationTime / 2).ConfigureAwait(false);
			}

			// return the result
			return result;
		}
		#endregion

		#region Create an organization
		async Task<JObject> CreateOrganizationAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			// check permission
			var isCreatedByOtherService = false;
			var gotRights = await this.IsSystemAdministratorAsync(requestInfo).ConfigureAwait(false);
			if (!gotRights && requestInfo.Extra != null)
				gotRights = isCreatedByOtherService = requestInfo.Extra.ContainsKey("x-create") && requestInfo.Extra["x-create"].IsEquals(requestInfo.Session.SessionID.Encrypt());
			if (!gotRights)
				throw new AccessDeniedException();

			// check the exising the the alias
			var info = requestInfo.GetBodyExpando();
			var alias = info.Get<string>("Alias");
			if (!string.IsNullOrWhiteSpace(alias))
			{
				var existing = await Organization.GetByAliasAsync(alias).ConfigureAwait(false);
				if (existing != null)
					throw new InformationExistedException("The alias (" + alias + ") is used by another organization");
			}

			// prepare
			var organization = info.Copy<Organization>("Status,ReferSection,ReferIDs,Privileges,Created,CreatedID,LastUpdated,LastUpdatedID".ToHashSet());

			if (string.IsNullOrWhiteSpace(organization.ID) || !organization.ID.IsValidUUID())
				organization.ID = UtilityService.NewUID;

			if (string.IsNullOrWhiteSpace(organization.Alias))
				organization.Alias = organization.Title.GetANSIUri() + "-" + organization.ID;

			if (string.IsNullOrWhiteSpace(organization.OwnerID))
				organization.OwnerID = requestInfo.Session.User.ID;

			organization.Status = isCreatedByOtherService && requestInfo.Extra.ContainsKey("x-status")
				? requestInfo.Extra["x-status"].ToEnum<ApprovalStatus>()
				: ApprovalStatus.Pending;

			if (requestInfo.Extra.ContainsKey("x-refer"))
			{
				organization.ReferSection = requestInfo.Extra["x-refer-section"];
				organization.ReferIDs = requestInfo.Extra.ContainsKey("x-refer-ids") ? requestInfo.Extra["x-refer-ids"] : "";
			}

			organization.Created = organization.LastUpdated = DateTime.Now;
			organization.CreatedID = organization.LastUpdatedID = requestInfo.Session.User.ID;

			// create new
			await Organization.CreateAsync(organization, cancellationToken).ConfigureAwait(false);
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
			var gotRights = await this.IsSystemAdministratorAsync(requestInfo).ConfigureAwait(false);
			if (!gotRights)
				gotRights = requestInfo.Session.User.CanView(organization.WorkingPrivileges);
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
			var organization = await Organization.GetAsync<Organization>(requestInfo.GetObjectIdentity(), cancellationToken).ConfigureAwait(false);
			if (organization == null)
				throw new InformationNotFoundException();

			// check permission
			var gotRights = await this.IsSystemAdministratorAsync(requestInfo).ConfigureAwait(false);
			if (!gotRights)
				gotRights = requestInfo.Session.User.ID.IsEquals(organization.OwnerID) || requestInfo.Session.User.CanManage(organization.WorkingPrivileges);
			if (!gotRights)
				throw new AccessDeniedException();

			// check the exising the the alias
			var info = requestInfo.GetBodyExpando();
			var oldAlias = organization.Alias;
			var alias = info.Get<string>("Alias");
			if (!string.IsNullOrWhiteSpace(alias))
			{
				var existing = await Organization.GetByAliasAsync(alias).ConfigureAwait(false);
				if (existing != null && !existing.ID.Equals(organization.ID))
					throw new InformationExistedException("The alias (" + alias + ") is used by another organization");
			}

			// prepare
			organization.CopyFrom(info, "ID,OwnerID,Status,ReferSection,ReferIDs,Privileges,Created,CreatedID,LastUpdated,LastUpdatedID".ToHashSet());
			if (string.IsNullOrWhiteSpace(organization.Alias))
				organization.Alias = oldAlias;
			organization.LastUpdated = DateTime.Now;
			organization.LastUpdatedID = requestInfo.Session.User.ID;

			// update
			await Organization.UpdateAsync(organization, cancellationToken).ConfigureAwait(false);
			return organization.ToJson();
		}
		#endregion

		#region Delete an organization
		Task<JObject> DeleteOrganizationAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
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
				gotRights = await this.IsAuthorizedAsync(requestInfo, Components.Security.Action.View, null, this.GetPrivileges, this.GetPrivilegeActions).ConfigureAwait(false);
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
			var gotRights = this.IsAuthenticated(requestInfo) && requestInfo.Session.User.ID.IsEquals(id);
			if (!gotRights)
				gotRights = await this.IsSystemAdministratorAsync(requestInfo).ConfigureAwait(false);
			if (!gotRights)
				gotRights = await this.IsAuthorizedAsync(requestInfo, Components.Security.Action.Update, null, this.GetPrivileges, this.GetPrivilegeActions).ConfigureAwait(false);
			if (!gotRights)
				throw new AccessDeniedException();

			// get existing information
			var site = await Site.GetAsync<Site>(id, cancellationToken).ConfigureAwait(false);
			if (site == null)
				throw new InformationNotFoundException();

			// update
			site.CopyFrom(requestInfo.GetBodyJson());
			site.ID = id;

			await Site.UpdateAsync(site, cancellationToken).ConfigureAwait(false);
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
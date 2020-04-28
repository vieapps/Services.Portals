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
	public class ServiceComponent : ServiceBase, ICmsPortalsService
	{

		#region Definitions
		IAsyncDisposable ServiceInstance { get; set; }

		IDisposable ServiceCommunicator { get; set; }

		public override string ServiceName => "Portals";

		public ModuleDefinition GetDefinition()
			=> new ModuleDefinition(RepositoryMediator.GetEntityDefinition<Organization>().RepositoryDefinition);
		#endregion

		#region Start/Stop the service
		public override Task RegisterServiceAsync(IEnumerable<string> args, Action<ServiceBase> onSuccess = null, Action<Exception> onError = null)
			=> base.RegisterServiceAsync(
				args,
				async _ =>
				{
					onSuccess?.Invoke(this);
					this.ServiceInstance = await Router.IncomingChannel.RealmProxy.Services.RegisterCallee<ICmsPortalsService>(() => this, RegistrationInterceptor.Create(this.ServiceName)).ConfigureAwait(false);
					this.ServiceCommunicator?.Dispose();
					this.ServiceCommunicator = CmsPortalsServiceExtensions.RegisterServiceCommunicator(
						async message => await this.ProcessCommunicateMessageAsync(message).ConfigureAwait(false),
						exception => this.Logger?.LogError($"Error occurred while fetching an communicate message of CMS Portals => {exception.Message}", this.State == ServiceState.Connected ? exception : null)
					);
					this.Logger?.LogDebug($"The service was{(this.State == ServiceState.Disconnected ? " re-" : " ")}registered successfully with CMS Portals");
				},
				onError
			);

		public override Task UnregisterServiceAsync(IEnumerable<string> args, bool available = true, Action<ServiceBase> onSuccess = null, Action<Exception> onError = null)
			=> base.UnregisterServiceAsync(
				args,
				available,
				async _ =>
				{
					onSuccess?.Invoke(this);
					if (this.ServiceInstance != null)
						try
						{
							await this.ServiceInstance.DisposeAsync().ConfigureAwait(false);
						}
						catch { }
					this.ServiceInstance = null;
					this.ServiceCommunicator?.Dispose();
					this.ServiceCommunicator = null;
					this.Logger?.LogDebug($"The service was unregistered successfully with CMS Portals");
				},
				onError
			);

		public override Task StartAsync(string[] args = null, bool initializeRepository = true, Action<IService> next = null)
			=> base.StartAsync(args, initializeRepository, _ =>
			{
				Utility.APIsHttpURI = this.GetHttpURI("APIs", "https://apis.vieapps.net");
				while (Utility.APIsHttpURI.EndsWith("/"))
					Utility.APIsHttpURI = Utility.APIsHttpURI.Left(Utility.APIsHttpURI.Length - 1);

				Utility.FilesHttpURI = this.GetHttpURI("Files", "https://fs.vieapps.net");
				while (Utility.FilesHttpURI.EndsWith("/"))
					Utility.FilesHttpURI = Utility.FilesHttpURI.Left(Utility.FilesHttpURI.Length - 1);

				Utility.PortalsHttpURI = this.GetHttpURI("Portals", "https://portals.vieapps.net");
				while (Utility.PortalsHttpURI.EndsWith("/"))
					Utility.PortalsHttpURI = Utility.PortalsHttpURI.Left(Utility.PortalsHttpURI.Length - 1);

				Utility.PassportsHttpURI = this.GetHttpURI("Passports", "https://id.vieapps.net");
				while (Utility.PassportsHttpURI.EndsWith("/"))
					Utility.PassportsHttpURI = Utility.PassportsHttpURI.Left(Utility.PassportsHttpURI.Length - 1);

				Utility.DefaultSite = UtilityService.GetAppSetting("Portals:DefaultSiteID", "").GetSiteByID();
				Utility.DataFilesDirectory = UtilityService.GetAppSetting("Path:Portals");
				this.StartTimer(() => this.SendDefinitionInfoAsync(this.CancellationTokenSource.Token), 15 * 60);

				this.Logger?.LogDebug($"The default site: {(Utility.DefaultSite != null ? $"{Utility.DefaultSite.Title} [{Utility.DefaultSite.ID}]" : "None")}");
				this.Logger?.LogDebug($"Portals' files directory: {Utility.DataFilesDirectory ?? "None"}");

				Task.Run(async () =>
				{
					try
					{
						await Task.Delay(UtilityService.GetRandomNumber(678, 789), this.CancellationTokenSource.Token).ConfigureAwait(false);
						await this.SendInterCommunicateMessageAsync(new CommunicateMessage("CMS.Portals")
						{
							Type = "Definition#RequestInfo"
						}, this.CancellationTokenSource.Token).ConfigureAwait(false);
					}
					catch (Exception ex)
					{
						await this.WriteLogsAsync(UtilityService.NewUUID, $"Error occurred while sending a request for gathering module definitions => {ex.Message}", ex, this.ServiceName, "CMS", Microsoft.Extensions.Logging.LogLevel.Error).ConfigureAwait(false);
					}
				});
				next?.Invoke(this);
			});
		#endregion

		public override async Task<JToken> ProcessRequestAsync(RequestInfo requestInfo, CancellationToken cancellationToken = default)
		{
			var stopwatch = Stopwatch.StartNew();
			this.WriteLogs(requestInfo, $"Begin request ({requestInfo.Verb} {requestInfo.GetURI()})");
			try
			{
				JToken json = null;
				switch (requestInfo.ObjectName.ToLower())
				{

					#region process the request of Portals objects
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

					case "module":
						json = await this.ProcessModuleAsync(requestInfo, cancellationToken).ConfigureAwait(false);
						break;

					case "contenttype":
					case "content.type":
					case "content-type":
						json = await this.ProcessContentTypeAsync(requestInfo, cancellationToken).ConfigureAwait(false);
						break;
					#endregion

					#region process the request of definitions
					case "definitions":
						var mode = requestInfo.GetQueryParameter("mode");
						switch (requestInfo.GetObjectIdentity())
						{
							case "moduledefinitions":
							case "module.definitions":
							case "module-definitions":
								json = Utility.ModuleDefinitions.Values.OrderBy(definition => definition.Title).ToJArray();
								break;

							case "social":
							case "socials":
								json = UtilityService.GetAppSetting("Portals:Socials", "Facebook,Twitter").ToArray().ToJArray();
								break;

							case "tracking":
							case "trackings":
								json = UtilityService.GetAppSetting("Portals:Trackings", "GoogleAnalytics,FacebookPixel").ToArray().ToJArray();
								break;

							case "theme":
							case "themes":
								json = await this.GetThemesAsync(cancellationToken).ConfigureAwait(false);
								break;

							case "organization":
								json = "view-controls".IsEquals(mode) ? this.GenerateViewControls<Organization>() : this.GenerateFormControls<Organization>();
								break;

							case "role":
								json = "view-controls".IsEquals(mode) ? this.GenerateViewControls<Role>() : this.GenerateFormControls<Role>();
								break;

							case "desktop":
								json = "view-controls".IsEquals(mode) ? this.GenerateViewControls<Desktop>() : this.GenerateFormControls<Desktop>();
								break;

							case "module":
								json = "view-controls".IsEquals(mode) ? this.GenerateViewControls<Module>() : this.GenerateFormControls<Module>();
								break;

							case "contenttype":
							case "content.type":
							case "content-type":
								json = "view-controls".IsEquals(mode) ? this.GenerateViewControls<ContentType>() : this.GenerateFormControls<ContentType>();
								break;

							case "site":
								json = "view-controls".IsEquals(mode) ? this.GenerateViewControls<Site>() : this.GenerateFormControls<Site>();
								break;

							default:
								throw new InvalidRequestException($"The request is invalid [({requestInfo.Verb}): {requestInfo.GetURI()}]");
						}
						break;
					#endregion

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

		#region Get themes
		async Task<JArray> GetThemesAsync(CancellationToken cancellationToken)
		{
			var themes = new JArray();
			if (string.IsNullOrWhiteSpace(Utility.DataFilesDirectory))
				themes.Add(new JObject
				{
					{ "name", "default" },
					{ "displayName", "Default theme" },
					{ "description", "The theme with default styles and coloring codes" },
					{ "author", "System" }
				});
			else if (Directory.Exists(Path.Combine(Utility.DataFilesDirectory, "themes")))
				await Directory.GetDirectories(Path.Combine(Utility.DataFilesDirectory, "themes")).ForEachAsync(async (directory, token) =>
				{
					var name = Path.GetFileName(directory).ToLower();
					var packageInfo = new JObject
					{
						{ "name", name },
						{ "displayName", name.GetCapitalizedFirstLetter() },
						{ "description", "" },
						{ "author", "System" }
					};
					if (File.Exists(Path.Combine(directory, "package.json")))
						try
						{
							packageInfo = JObject.Parse(await UtilityService.ReadTextFileAsync(Path.Combine(directory, "package.json")).ConfigureAwait(false));
						}
						catch { }
					themes.Add(packageInfo);
				}, cancellationToken, true, false).ConfigureAwait(false);
			return themes;
		}
		#endregion

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
			var filter = request.Get<ExpandoObject>("FilterBy")?.ToFilterBy<Organization>() ?? Filters<Organization>.And();
			var sort = string.IsNullOrWhiteSpace(query) ? request.Get<ExpandoObject>("SortBy")?.ToSortBy<Organization>() ?? Sorts<Organization>.Ascending("Title") : null;

			var pagination = request.Get<ExpandoObject>("Pagination")?.GetPagination() ?? new Tuple<long, int, int, int>(-1, 0, 20, 1);
			var pageSize = pagination.Item3;
			var pageNumber = pagination.Item4;

			// check permissions
			var gotRights = await this.IsSystemAdministratorAsync(requestInfo).ConfigureAwait(false) || await this.IsAuthorizedAsync(requestInfo, "Organization", Components.Security.Action.Approve, cancellationToken).ConfigureAwait(false);
			if (!gotRights)
				throw new AccessDeniedException();

			// process cache
			var json = string.IsNullOrWhiteSpace(query) ? await Utility.Cache.GetAsync<string>(this.GetCacheKeyOfObjectsJson(filter, sort, pageSize, pageNumber), cancellationToken).ConfigureAwait(false) : null;
			if (!string.IsNullOrWhiteSpace(json))
				return JObject.Parse(json);

			// prepare pagination
			var totalRecords = pagination.Item1 > -1 ? pagination.Item1 : -1;
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
			var response = new JObject
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
				json = response.ToString(Formatting.Indented);
#else
				json = response.ToString(Formatting.Indented);
#endif
				await Utility.Cache.SetAsync(this.GetCacheKeyOfObjectsJson(filter, sort, pageSize, pageNumber), json, Utility.Cache.ExpirationTime / 2).ConfigureAwait(false);
			}

			// response
			return response;
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
			var requestBody = requestInfo.GetBodyExpando();
			var alias = requestBody.Get<string>("Alias");
			if (!string.IsNullOrWhiteSpace(alias))
			{
				var existing = await alias.NormalizeAlias(false).GetOrganizationByAliasAsync(cancellationToken).ConfigureAwait(false);
				if (existing != null)
					throw new InformationExistedException($"The alias ({alias.NormalizeAlias(false)}) is used by another organization");
			}

			// create new
			var organization = requestBody.CreateOrganizationInstance("Status,Instructions,Privileges,OriginalPrivileges,Created,CreatedID,LastModified,LastModifiedID", xorganization =>
			{
				xorganization.ID = string.IsNullOrWhiteSpace(xorganization.ID) || !xorganization.ID.IsValidUUID() ? UtilityService.NewUUID : xorganization.ID;
				xorganization.Alias = string.IsNullOrWhiteSpace(xorganization.Alias) ? xorganization.Title.NormalizeAlias(false) + xorganization.ID : xorganization.Alias.NormalizeAlias(false);
				xorganization.OwnerID = string.IsNullOrWhiteSpace(xorganization.OwnerID) || !xorganization.OwnerID.IsValidUUID() ? requestInfo.Session.User.ID : xorganization.OwnerID;
				xorganization.Status = isSystemAdministrator
					? requestBody.Get("Status", "Pending").TryToEnum(out ApprovalStatus statusByAdmin) ? statusByAdmin : ApprovalStatus.Pending
					: isCreatedByOtherService
						? requestInfo.Extra.TryGetValue("x-status", out var xstatus) && xstatus.TryToEnum(out ApprovalStatus statusByOtherService) ? statusByOtherService : ApprovalStatus.Pending
						 : ApprovalStatus.Pending;
				xorganization.OriginalPrivileges = (isSystemAdministrator ? requestBody.Get<Privileges>("OriginalPrivileges") : null) ?? new Privileges(true);
				xorganization.Created = xorganization.LastModified = DateTime.Now;
				xorganization.CreatedID = xorganization.LastModifiedID = requestInfo.Session.User.ID;
				xorganization.NormalizeExtras();
			});
			await Organization.CreateAsync(organization, cancellationToken).ConfigureAwait(false);
			await Task.WhenAll(
				Utility.Cache.RemoveAsync(this.GetRelatedCacheKeys(Filters<Organization>.And(), Sorts<Organization>.Ascending("Title")), cancellationToken),
				organization.SetAsync(false, false, cancellationToken)
			).ConfigureAwait(false);

			// send update messages
			var response = organization.ToJson();
			var objectName = organization.GetTypeName(true);
			await Task.WhenAll(
				this.SendUpdateMessageAsync(new UpdateMessage
				{
					Type = $"{this.ServiceName}#{objectName}#Update",
					Data = response,
					DeviceID = "*",
					ExcludedDeviceID = requestInfo.Session.DeviceID
				}, cancellationToken),
				this.SendInterCommunicateMessageAsync(new CommunicateMessage(this.ServiceName)
				{
					Type = $"{objectName}#Update",
					Data = response,
					ExcludedNodeID = this.NodeID
				}, cancellationToken)
			).ConfigureAwait(false);

			// response
			return response;
		}
		#endregion

		#region Get an organization
		async Task<JObject> GetOrganizationAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			// get the organization
			var identity = requestInfo.GetObjectIdentity() ?? "";
			var organization = await (identity.IsValidUUID() ? identity.GetOrganizationByIDAsync(cancellationToken) : identity.GetOrganizationByAliasAsync(cancellationToken)).ConfigureAwait(false);
			if (organization == null)
				throw new InformationNotFoundException();

			/*
			// check permission
			var isSystemAdministrator = await this.IsSystemAdministratorAsync(requestInfo).ConfigureAwait(false) || await this.IsAuthorizedAsync(requestInfo, "Organization", Components.Security.Action.Approve, cancellationToken).ConfigureAwait(false);
			var gotRights = isSystemAdministrator || requestInfo.Session.User.ID.IsEquals(organization.OwnerID) || requestInfo.Session.User.IsAdministrator(organization.WorkingPrivileges);
			if (!gotRights)
				throw new AccessDeniedException();
			*/

			// response
			return identity.IsValidUUID()
				? organization.ToJson()
				: new JObject
				{
					{ "ID", organization.ID },
					{ "Title", organization.Title },
					{ "Alias", organization.Alias }
				};
		}
		#endregion

		#region Update an organization
		async Task<JObject> UpdateOrganizationAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			// get the organization
			var organization = await (requestInfo.GetObjectIdentity() ?? "").GetOrganizationByIDAsync(cancellationToken).ConfigureAwait(false);
			if (organization == null)
				throw new InformationNotFoundException();

			// check permission
			var isSystemAdministrator = await this.IsSystemAdministratorAsync(requestInfo).ConfigureAwait(false) || await this.IsAuthorizedAsync(requestInfo, "Organization", Components.Security.Action.Approve, cancellationToken).ConfigureAwait(false);
			var gotRights = isSystemAdministrator || requestInfo.Session.User.ID.IsEquals(organization.OwnerID) || requestInfo.Session.User.IsAdministrator(organization.WorkingPrivileges);
			if (!gotRights)
				throw new AccessDeniedException();

			// check the exising the the alias
			var requestBody = requestInfo.GetBodyExpando();
			var oldAlias = organization.Alias;
			var alias = requestBody.Get<string>("Alias");
			if (!string.IsNullOrWhiteSpace(alias))
			{
				var existing = await alias.NormalizeAlias(false).GetOrganizationByAliasAsync(cancellationToken).ConfigureAwait(false);
				if (existing != null && !existing.ID.Equals(organization.ID))
					throw new InformationExistedException($"The alias ({alias.NormalizeAlias(false)}) is used by another organization");
			}

			// update
			organization.UpdateOrganizationInstance(requestBody, "ID,OwnerID,Status,Instructions,Privileges,Created,CreatedID,LastModified,LastModifiedID", xorganization =>
			{
				xorganization.OwnerID = isSystemAdministrator ? requestBody.Get("OwnerID", organization.OwnerID) : organization.OwnerID;
				xorganization.Alias = string.IsNullOrWhiteSpace(organization.Alias) ? oldAlias : organization.Alias.NormalizeAlias(false);
				xorganization.OriginalPrivileges = organization.OriginalPrivileges ?? new Privileges(true);
				xorganization.LastModified = DateTime.Now;
				xorganization.LastModifiedID = requestInfo.Session.User.ID;
				xorganization.NormalizeExtras();
			});
			await Organization.UpdateAsync(organization, requestInfo.Session.User.ID, cancellationToken).ConfigureAwait(false);
			await Task.WhenAll(
				Utility.Cache.RemoveAsync(this.GetRelatedCacheKeys(Filters<Organization>.And(), Sorts<Organization>.Ascending("Title")), cancellationToken),
				organization.SetAsync(false, false, cancellationToken)
			).ConfigureAwait(false);

			// send update messages
			var response = organization.ToJson();
			var objectName = organization.GetTypeName(true);
			await Task.WhenAll(
				this.SendUpdateMessageAsync(new UpdateMessage
				{
					Type = $"{this.ServiceName}#{objectName}#Update",
					Data = response,
					DeviceID = "*",
					ExcludedDeviceID = requestInfo.Session.DeviceID
				}, cancellationToken),
				this.SendInterCommunicateMessageAsync(new CommunicateMessage(this.ServiceName)
				{
					Type = $"{objectName}#Update",
					Data = response,
					ExcludedNodeID = this.NodeID
				}, cancellationToken)
			).ConfigureAwait(false);

			// response
			return response;
		}
		#endregion

		#region Delete an organization
		Task<JObject> DeleteOrganizationAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			return Task.FromException<JObject>(new MethodNotAllowedException(requestInfo.Verb));
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
			var filter = request.Get<ExpandoObject>("FilterBy")?.ToFilterBy<Role>() ?? Filters<Role>.And();
			if (filter is FilterBys<Role>)
			{
				if (!string.IsNullOrWhiteSpace(query))
				{
					var index = (filter as FilterBys<Role>).Children.FindIndex(exp => (exp as FilterBy<Role>).Attribute.IsEquals("ParentID"));
					if (index > -1)
						(filter as FilterBys<Role>).Children.RemoveAt(index);
				}
				else if ((filter as FilterBys<Role>).Children.FirstOrDefault(exp => (exp as FilterBy<Role>).Attribute.IsEquals("ParentID")) == null)
					(filter as FilterBys<Role>).Children.Add(Filters<Role>.IsNull("ParentID"));
			}
			var sort = string.IsNullOrWhiteSpace(query) ? request.Get<ExpandoObject>("SortBy")?.ToSortBy<Role>() ?? Sorts<Role>.Ascending("Title") : null;

			var pagination = request.Get<ExpandoObject>("Pagination")?.GetPagination() ?? new Tuple<long, int, int, int>(-1, 0, 20, 1);
			var pageSize = pagination.Item3;
			var pageNumber = pagination.Item4;

			// get organization
			var organizationID = filter is FilterBys<Role>
					? ((filter as FilterBys<Role>).Children.FirstOrDefault(exp => (exp as FilterBy<Role>).Attribute.IsEquals("SystemID")) as FilterBy<Role>)?.Value as string
					: null;
			if (string.IsNullOrWhiteSpace(organizationID))
				organizationID = requestInfo.GetParameter("x-system") ?? requestInfo.GetParameter("SystemID");
			var organization = await (organizationID ?? "").GetOrganizationByIDAsync(cancellationToken).ConfigureAwait(false);
			if (organization == null)
				throw new InformationExistedException("The organization is invalid");

			// check permission
			var isSystemAdministrator = await this.IsSystemAdministratorAsync(requestInfo).ConfigureAwait(false) || await this.IsAuthorizedAsync(requestInfo, "Organization", Components.Security.Action.Approve, cancellationToken).ConfigureAwait(false);
			var gotRights = isSystemAdministrator || requestInfo.Session.User.ID.IsEquals(organization.OwnerID) || requestInfo.Session.User.IsViewer(organization.WorkingPrivileges);
			if (!gotRights)
				throw new AccessDeniedException();

			// process cache
			var json = string.IsNullOrWhiteSpace(query) ? await Utility.Cache.GetAsync<string>(this.GetCacheKeyOfObjectsJson(filter, sort, pageSize, pageNumber), cancellationToken).ConfigureAwait(false) : null;
			if (!string.IsNullOrWhiteSpace(json))
				return JObject.Parse(json);

			// prepare pagination
			var totalRecords = pagination.Item1 > -1 ? pagination.Item1 : -1;
			if (totalRecords < 0)
				totalRecords = string.IsNullOrWhiteSpace(query)
					? await Role.CountAsync(filter, this.GetCacheKeyOfTotalObjects(filter, sort), cancellationToken).ConfigureAwait(false)
					: await Role.CountAsync(query, filter, cancellationToken).ConfigureAwait(false);

			var totalPages = new Tuple<long, int>(totalRecords, pageSize).GetTotalPages();
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
			var response = new JObject
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
				json = response.ToString(Formatting.Indented);
#else
				json = response.ToString(Formatting.Indented);
#endif
				await Utility.Cache.SetAsync(this.GetCacheKeyOfObjectsJson(filter, sort, pageSize, pageNumber), json, Utility.Cache.ExpirationTime / 2).ConfigureAwait(false);
			}

			// response
			return response;
		}
		#endregion

		#region Create a role
		async Task<JObject> CreateRoleAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			// prepare
			var requestBody = requestInfo.GetBodyExpando();
			var organizationID = requestBody.Get<string>("SystemID") ?? requestInfo.GetParameter("x-system") ?? requestInfo.GetParameter("SystemID");
			var organization = await (organizationID ?? "").GetOrganizationByIDAsync(cancellationToken).ConfigureAwait(false);
			if (organization == null)
				throw new InformationInvalidException("The organization is invalid");

			// check permission
			var isSystemAdministrator = await this.IsSystemAdministratorAsync(requestInfo).ConfigureAwait(false) || await this.IsAuthorizedAsync(requestInfo, "Organization", Components.Security.Action.Approve, cancellationToken).ConfigureAwait(false);
			var gotRights = isSystemAdministrator || requestInfo.Session.User.ID.IsEquals(organization.OwnerID) || requestInfo.Session.User.IsModerator(organization.WorkingPrivileges);
			if (!gotRights)
				throw new AccessDeniedException();

			// create new
			var role = requestBody.CreateRoleInstance("SystemID,Privileges,OriginalPrivileges,Created,CreatedID,LastModified,LastModifiedID", xrole =>
			{
				xrole.ID = string.IsNullOrWhiteSpace(xrole.ID) || !xrole.ID.IsValidUUID() ? UtilityService.NewUUID : xrole.ID;
				xrole.SystemID = organization.ID;
				xrole.ParentID = xrole.ParentRole != null ? xrole.ParentID : null;
				xrole.Created = xrole.LastModified = DateTime.Now;
				xrole.CreatedID = xrole.LastModifiedID = requestInfo.Session.User.ID;
				xrole._childrenIDs = new List<string>();
			});
			await Task.WhenAll(
				Role.CreateAsync(role, cancellationToken),
				Utility.Cache.RemoveAsync(this.GetRelatedCacheKeys(role.SystemID.GetRolesFilter(role.ParentID), Sorts<Role>.Ascending("Title")), cancellationToken),
				role.SetAsync(false, cancellationToken)
			).ConfigureAwait(false);

			var updateMessages = new List<UpdateMessage>();
			var communicateMessages = new List<CommunicateMessage>();
			var objectName = role.GetTypeName(true);

			// update parent
			if (role.ParentRole != null)
			{
				await role.ParentRole.GetChildrenAsync(cancellationToken).ConfigureAwait(false);
				role.ParentRole._childrenIDs.Add(role.ID);
				await role.ParentRole.SetAsync(true, cancellationToken).ConfigureAwait(false);

				// message to update to all connected clients
				var json = role.ParentRole.ToJson(true, false);
				updateMessages.Add(new UpdateMessage
				{
					Type = $"{this.ServiceName}#{objectName}#Update",
					Data = json,
					DeviceID = "*"
				});

				// message to update to all service instances (on all other nodes)
				communicateMessages.Add(new CommunicateMessage(this.ServiceName)
				{
					Type = $"{objectName}#Update",
					Data = json,
					ExcludedNodeID = this.NodeID
				});
			}

			// message to update to all other connected clients
			var response = role.ToJson(true, false);
			if (role.ParentRole == null)
				updateMessages.Add(new UpdateMessage
				{
					Type = $"{this.ServiceName}#{objectName}#Update",
					Data = response,
					DeviceID = "*",
					ExcludedDeviceID = requestInfo.Session.DeviceID
				});

			// message to update to all service instances (on all other nodes)
			communicateMessages.Add(new CommunicateMessage(this.ServiceName)
			{
				Type = $"{objectName}#Update",
				Data = response,
				ExcludedNodeID = this.NodeID
			});

			// send the messages and response
			await Task.WhenAll(
				updateMessages.ForEachAsync((message, token) => this.SendUpdateMessageAsync(message, token), cancellationToken, true, false),
				communicateMessages.ForEachAsync((message, token) => this.SendInterCommunicateMessageAsync(message, token), cancellationToken)
			).ConfigureAwait(false);
			return response;
		}
		#endregion

		#region Get a role
		async Task<JObject> GetRoleAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			// prepare
			var role = await (requestInfo.GetObjectIdentity() ?? "").GetRoleByIDAsync(cancellationToken).ConfigureAwait(false);
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
				await role.GetChildrenAsync(cancellationToken).ConfigureAwait(false);
				await role.SetAsync(true, cancellationToken).ConfigureAwait(false);
			}

			// send the update message to update to all other connected clients and response
			var response = role.ToJson(true, false);
			await this.SendUpdateMessageAsync(new UpdateMessage
			{
				Type = $"{this.ServiceName}#{role.GetTypeName(true)}#Update",
				Data = response,
				DeviceID = "*",
				ExcludedDeviceID = requestInfo.Session.DeviceID
			}, cancellationToken).ConfigureAwait(false);
			return response;
		}
		#endregion

		#region Update a role
		async Task<JObject> UpdateRoleAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			// prepare
			var role = await (requestInfo.GetObjectIdentity() ?? "").GetRoleByIDAsync(cancellationToken).ConfigureAwait(false);
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
			var requestBody = requestInfo.GetBodyExpando();

			role.UpdateRoleInstance(requestBody, "ID,SystemID,Privileges,OriginalPrivileges,Created,CreatedID,LastModified,LastModifiedID", async xrole =>
			{
				xrole.LastModified = DateTime.Now;
				xrole.LastModifiedID = requestInfo.Session.User.ID;
				await xrole.GetChildrenAsync(cancellationToken).ConfigureAwait(false);
			});
			await Task.WhenAll(
				Role.UpdateAsync(role, requestInfo.Session.User.ID, cancellationToken),
				role.SetAsync(false, cancellationToken)
			).ConfigureAwait(false);

			await Task.WhenAll(
				Utility.Cache.RemoveAsync(this.GetRelatedCacheKeys(role.SystemID.GetRolesFilter(role.ParentID), Sorts<Role>.Ascending("Title")), cancellationToken),
				Utility.Cache.RemoveAsync(this.GetRelatedCacheKeys(role.SystemID.GetRolesFilter(oldParentID), Sorts<Role>.Ascending("Title")), cancellationToken)
			).ConfigureAwait(false);

			var updateMessages = new List<UpdateMessage>();
			var communicateMessages = new List<CommunicateMessage>();
			var objectName = role.GetTypeName(true);

			// update parent
			if (role.ParentRole != null && !role.ParentID.IsEquals(oldParentID))
			{
				await role.ParentRole.GetChildrenAsync(cancellationToken).ConfigureAwait(false);
				role.ParentRole._childrenIDs.Add(role.ID);
				await role.ParentRole.SetAsync(true).ConfigureAwait(false);

				var json = role.ParentRole.ToJson(true, false);

				// message to update to all connected clients
				updateMessages.Add(new UpdateMessage
				{
					Type = $"{this.ServiceName}#{objectName}#Update",
					Data = json,
					DeviceID = "*"
				});

				// message to update to all service instances (on all other nodes)
				communicateMessages.Add(new CommunicateMessage(this.ServiceName)
				{
					Type = $"{objectName}#Update",
					Data = json,
					ExcludedNodeID = this.NodeID
				});
			}

			// update old parent
			if (!string.IsNullOrWhiteSpace(oldParentID) && !oldParentID.IsEquals(role.ParentID))
			{
				var parentRole = await oldParentID.GetRoleByIDAsync(cancellationToken).ConfigureAwait(false);
				if (parentRole != null)
				{
					await parentRole.GetChildrenAsync(cancellationToken).ConfigureAwait(false);
					parentRole._childrenIDs.Remove(role.ID);
					await parentRole.SetAsync(true, cancellationToken).ConfigureAwait(false);

					var json = parentRole.ToJson(true, false);

					// message to update to all connected clients
					updateMessages.Add(new UpdateMessage
					{
						Type = $"{this.ServiceName}#{objectName}#Update",
						Data = json,
						DeviceID = "*"
					});

					// message to update to all service instances (on all other nodes)
					communicateMessages.Add(new CommunicateMessage(this.ServiceName)
					{
						Type = $"{objectName}#Update",
						Data = json,
						ExcludedNodeID = this.NodeID
					});
				}
			}

			// message to update to all other connected clients
			var response = role.ToJson(true, false);
			if (string.IsNullOrWhiteSpace(oldParentID) && role.ParentRole == null)
				updateMessages.Add(new UpdateMessage
				{
					Type = $"{this.ServiceName}#{objectName}#Update",
					Data = response,
					DeviceID = "*",
					ExcludedDeviceID = requestInfo.Session.DeviceID
				});

			// message to update to all service instances (on all other nodes)
			communicateMessages.Add(new CommunicateMessage(this.ServiceName)
			{
				Type = $"{objectName}#Update",
				Data = response,
				ExcludedNodeID = this.NodeID
			});

			// send messages and response
			await Task.WhenAll(
				updateMessages.ForEachAsync((message, token) => this.SendUpdateMessageAsync(message, token), cancellationToken, true, false),
				communicateMessages.ForEachAsync((message, token) => this.SendInterCommunicateMessageAsync(message, token), cancellationToken)
			).ConfigureAwait(false);
			return response;
		}
		#endregion

		#region Delete a role
		async Task<JObject> DeleteRoleAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			// prepare
			var role = await (requestInfo.GetObjectIdentity() ?? "").GetRoleByIDAsync(cancellationToken).ConfigureAwait(false);
			if (role == null)
				throw new InformationNotFoundException();
			else if (role.Organization == null)
				throw new InformationInvalidException("The organization is invalid");

			// check permission
			var isSystemAdministrator = await this.IsSystemAdministratorAsync(requestInfo).ConfigureAwait(false) || await this.IsAuthorizedAsync(requestInfo, "Organization", Components.Security.Action.Approve, cancellationToken).ConfigureAwait(false);
			var gotRights = isSystemAdministrator || requestInfo.Session.User.ID.IsEquals(role.Organization.OwnerID) || requestInfo.Session.User.IsModerator(role.Organization.WorkingPrivileges);
			if (!gotRights)
				throw new AccessDeniedException();

			var updateMessages = new List<UpdateMessage>();
			var communicateMessages = new List<CommunicateMessage>();
			var objectName = role.GetTypeName(true);
			var updateChildren = requestInfo.Header.TryGetValue("x-children", out var childrenMode) && "set-null".IsEquals(childrenMode);

			// delete
			await (await role.GetChildrenAsync(cancellationToken).ConfigureAwait(false)).ForEachAsync(async (child, token) =>
			{
				// update children to root
				if (updateChildren)
				{
					child.ParentID = null;
					child.LastModified = DateTime.Now;
					child.LastModifiedID = requestInfo.Session.User.ID;

					await Task.WhenAll(
						Role.UpdateAsync(child, requestInfo.Session.User.ID, token),
						child.SetAsync(false, token)
					).ConfigureAwait(false);

					var json = child.ToJson(true, false);
					updateMessages.Add(new UpdateMessage
					{
						Type = $"{this.ServiceName}#{objectName}#Delete",
						Data = json,
						DeviceID = "*"
					});
					communicateMessages.Add(new CommunicateMessage(this.ServiceName)
					{
						ServiceName = this.ServiceName,
						Type = $"{objectName}#Delete",
						Data = json,
						ExcludedNodeID = this.NodeID
					});
				}

				// delete children
				else
				{
					var messages = await this.DeleteChildRoleAsync(child, requestInfo.Session.User.ID, token).ConfigureAwait(false);
					updateMessages = updateMessages.Concat(messages.Item1).ToList();
					communicateMessages = communicateMessages.Concat(messages.Item2).ToList();
				}
			}, cancellationToken, true, false).ConfigureAwait(false);

			await Role.DeleteAsync<Role>(role.ID, requestInfo.Session.User.ID, cancellationToken).ConfigureAwait(false);
			role.Remove();

			await Task.WhenAll(
				updateChildren ? Utility.Cache.RemoveAsync(this.GetRelatedCacheKeys(role.SystemID.GetRolesFilter(null), Sorts<Role>.Ascending("Title")), cancellationToken) : Task.CompletedTask,
				Utility.Cache.RemoveAsync(this.GetRelatedCacheKeys(role.SystemID.GetRolesFilter(role.ParentID), Sorts<Role>.Ascending("Title")), cancellationToken)
			).ConfigureAwait(false);

			// message to update to all other connected clients
			var response = role.ToJson();
			updateMessages.Add(new UpdateMessage
			{
				Type = $"{this.ServiceName}#{objectName}#Delete",
				DeviceID = "*",
				Data = response
			});

			// message to update to all service instances (on all other nodes)
			communicateMessages.Add(new CommunicateMessage(this.ServiceName)
			{
				Type = $"{objectName}#Delete",
				Data = response,
				ExcludedNodeID = this.NodeID
			});

			// send messages and response
			await Task.WhenAll(
				updateMessages.ForEachAsync((message, token) => this.SendUpdateMessageAsync(message, token), cancellationToken, true, false),
				communicateMessages.ForEachAsync((message, token) => this.SendInterCommunicateMessageAsync(message, token), cancellationToken)
			).ConfigureAwait(false);
			return response;
		}

		async Task<Tuple<List<UpdateMessage>, List<CommunicateMessage>>> DeleteChildRoleAsync(Role role, string userID, CancellationToken cancellationToken)
		{
			var updateMessages = new List<UpdateMessage>();
			var communicateMessages = new List<CommunicateMessage>();
			var objectName = role.GetTypeName(true);

			var children = await role.GetChildrenAsync(cancellationToken).ConfigureAwait(false);
			await children.ForEachAsync(async (child, token) =>
			{
				var messages = await this.DeleteChildRoleAsync(child, userID, token).ConfigureAwait(false);
				updateMessages = updateMessages.Concat(messages.Item1).ToList();
				communicateMessages = communicateMessages.Concat(messages.Item2).ToList();
			}, cancellationToken, true, false).ConfigureAwait(false);

			await Role.DeleteAsync<Role>(role.ID, userID, cancellationToken).ConfigureAwait(false);
			role.Remove();

			var json = role.ToJson();
			updateMessages.Add(new UpdateMessage
			{
				Type = $"{this.ServiceName}#{objectName}#Delete",
				Data = json,
				DeviceID = "*"
			});
			communicateMessages.Add(new CommunicateMessage(this.ServiceName)
			{
				Type = $"{objectName}#Delete",
				Data = json,
				ExcludedNodeID = this.NodeID
			});
			return new Tuple<List<UpdateMessage>, List<CommunicateMessage>>(updateMessages, communicateMessages);
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
			if (filter is FilterBys<Desktop>)
			{
				if (!string.IsNullOrWhiteSpace(query))
				{
					var index = (filter as FilterBys<Desktop>).Children.FindIndex(exp => (exp as FilterBy<Desktop>).Attribute.IsEquals("ParentID"));
					if (index > -1)
						(filter as FilterBys<Desktop>).Children.RemoveAt(index);
				}
				else if ((filter as FilterBys<Desktop>).Children.FirstOrDefault(exp => (exp as FilterBy<Desktop>).Attribute.IsEquals("ParentID")) == null)
					(filter as FilterBys<Desktop>).Children.Add(Filters<Desktop>.IsNull("ParentID"));
			}

			var sort = string.IsNullOrWhiteSpace(query) ? request.Get<ExpandoObject>("SortBy")?.ToSortBy<Desktop>() ?? Sorts<Desktop>.Ascending("Title") : null;

			var pagination = request.Get<ExpandoObject>("Pagination")?.GetPagination() ?? new Tuple<long, int, int, int>(-1, 0, 20, 1);
			var pageSize = pagination.Item3;
			var pageNumber = pagination.Item4;

			// get organization
			var organizationID = filter is FilterBys<Desktop>
				? ((filter as FilterBys<Desktop>).Children.FirstOrDefault(exp => (exp as FilterBy<Desktop>).Attribute.IsEquals("SystemID")) as FilterBy<Desktop>)?.Value as string
				: null;
			if (string.IsNullOrWhiteSpace(organizationID))
				organizationID = requestInfo.GetParameter("x-system") ?? requestInfo.GetParameter("SystemID");
			var organization = await (organizationID ?? "").GetOrganizationByIDAsync(cancellationToken).ConfigureAwait(false);
			if (organization == null)
				throw new InformationExistedException("The organization is invalid");

			// check permission
			var isSystemAdministrator = await this.IsSystemAdministratorAsync(requestInfo).ConfigureAwait(false) || await this.IsAuthorizedAsync(requestInfo, "Organization", Components.Security.Action.Approve, cancellationToken).ConfigureAwait(false);
			var gotRights = isSystemAdministrator || requestInfo.Session.User.ID.IsEquals(organization.OwnerID) || requestInfo.Session.User.IsViewer(organization.WorkingPrivileges);
			if (!gotRights)
				throw new AccessDeniedException();

			// process cache
			var json = string.IsNullOrWhiteSpace(query) ? await Utility.Cache.GetAsync<string>(this.GetCacheKeyOfObjectsJson(filter, sort, pageSize, pageNumber), cancellationToken).ConfigureAwait(false) : null;
			if (!string.IsNullOrWhiteSpace(json))
				return JObject.Parse(json);

			// prepare pagination
			var totalRecords = pagination.Item1 > -1 ? pagination.Item1 : -1;
			if (totalRecords < 0)
				totalRecords = string.IsNullOrWhiteSpace(query)
					? await Desktop.CountAsync(filter, this.GetCacheKeyOfTotalObjects(filter, sort), cancellationToken).ConfigureAwait(false)
					: await Desktop.CountAsync(query, filter, cancellationToken).ConfigureAwait(false);

			var totalPages = new Tuple<long, int>(totalRecords, pageSize).GetTotalPages();
			if (totalPages > 0 && pageNumber > totalPages)
				pageNumber = totalPages;

			// search
			var objects = totalRecords > 0
				? string.IsNullOrWhiteSpace(query)
					? await Desktop.FindAsync(filter, sort, pageSize, pageNumber, this.GetCacheKey(filter, sort, pageSize, pageNumber), cancellationToken).ConfigureAwait(false)
					: await Desktop.SearchAsync(query, filter, pageSize, pageNumber, cancellationToken).ConfigureAwait(false)
				: new List<Desktop>();

			// build response
			pagination = new Tuple<long, int, int, int>(totalRecords, totalPages, pageSize, pageNumber);
			var response = new JObject()
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
				json = response.ToString(Formatting.Indented);
#else
				json = response.ToString(Formatting.Indented);
#endif
				await Utility.Cache.SetAsync(this.GetCacheKeyOfObjectsJson(filter, sort, pageSize, pageNumber), json, Utility.Cache.ExpirationTime / 2).ConfigureAwait(false);
			}

			// response
			return response;
		}
		#endregion

		#region Create a desktop
		async Task<JObject> CreateDesktopAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			// prepare
			var requestBody = requestInfo.GetBodyExpando();
			var organizationID = requestBody.Get<string>("SystemID") ?? requestInfo.GetParameter("x-system") ?? requestInfo.GetParameter("SystemID");
			var organization = await (organizationID ?? "").GetOrganizationByIDAsync(cancellationToken).ConfigureAwait(false);
			if (organization == null)
				throw new InformationInvalidException("The organization is invalid");

			// check permission
			var isSystemAdministrator = await this.IsSystemAdministratorAsync(requestInfo).ConfigureAwait(false) || await this.IsAuthorizedAsync(requestInfo, "Organization", Components.Security.Action.Approve, cancellationToken).ConfigureAwait(false);
			var gotRights = isSystemAdministrator || requestInfo.Session.User.ID.IsEquals(organization.OwnerID) || requestInfo.Session.User.IsModerator(organization.WorkingPrivileges);
			if (!gotRights)
				throw new AccessDeniedException();

			// check alias
			var alias = requestBody.Get<string>("Alias");
			if (!string.IsNullOrWhiteSpace(alias))
			{
				if (DesktopExtensions.ExcludedAliases.Contains(alias.NormalizeAlias()))
					throw new InformationExistedException($"The alias ({alias.NormalizeAlias()}) is used by another purpose");
				var existing = await organization.ID.GetDesktopByAliasAsync(alias.NormalizeAlias(), cancellationToken).ConfigureAwait(false);
				if (existing != null)
					throw new InformationExistedException($"The alias ({alias.NormalizeAlias()}) is used by another desktop");
			}

			// create new
			var desktop = requestBody.CreateDesktopInstance("SystemID,Privileges,OriginalPrivileges,Created,CreatedID,LastModified,LastModifiedID", xdesktop =>
			{
				xdesktop.SystemID = organization.ID;
				xdesktop.ParentID = xdesktop.ParentDesktop != null ? xdesktop.ParentID : null;
				xdesktop.ID = string.IsNullOrWhiteSpace(xdesktop.ID) || !xdesktop.ID.IsValidUUID() ? UtilityService.NewUUID : xdesktop.ID;
				xdesktop.Created = xdesktop.LastModified = DateTime.Now;
				xdesktop.CreatedID = xdesktop.LastModifiedID = requestInfo.Session.User.ID;
				xdesktop.NormalizeExtras();
				xdesktop._childrenIDs = new List<string>();
			});
			await Task.WhenAll(
				Desktop.CreateAsync(desktop, cancellationToken),
				Utility.Cache.RemoveAsync(this.GetRelatedCacheKeys(desktop.SystemID.GetDesktopsFilter(desktop.ParentID), Sorts<Desktop>.Ascending("Title")), cancellationToken),
				desktop.SetAsync(false, false, cancellationToken)
			).ConfigureAwait(false);

			var updateMessages = new List<UpdateMessage>();
			var communicateMessages = new List<CommunicateMessage>();
			var objectName = desktop.GetTypeName(true);

			// update parent
			if (desktop.ParentDesktop != null)
			{
				await desktop.ParentDesktop.GetChildrenAsync(cancellationToken, false).ConfigureAwait(false);
				desktop.ParentDesktop._childrenIDs.Add(desktop.ID);
				await desktop.ParentDesktop.SetAsync(false, true, cancellationToken).ConfigureAwait(false);

				var json = desktop.ParentDesktop.ToJson(true, false);
				updateMessages.Add(new UpdateMessage
				{
					Type = $"{this.ServiceName}#{objectName}#Update",
					Data = json,
					DeviceID = "*"
				});
				communicateMessages.Add(new CommunicateMessage(this.ServiceName)
				{
					Type = $"{objectName}#Update",
					Data = json,
					ExcludedNodeID = this.NodeID
				});
			}

			// message to update to all other connected clients
			var response = desktop.ToJson(true, false);
			if (desktop.ParentDesktop == null)
				updateMessages.Add(new UpdateMessage
				{
					Type = $"{this.ServiceName}#{objectName}#Update",
					DeviceID = "*",
					ExcludedDeviceID = requestInfo.Session.DeviceID,
					Data = response
				});

			// message to update to all service instances (on all other nodes)
			communicateMessages.Add(new CommunicateMessage(this.ServiceName)
			{
				Type = $"{objectName}#Update",
				Data = response,
				ExcludedNodeID = this.NodeID
			});

			// send messages and response
			await Task.WhenAll(
				updateMessages.ForEachAsync((message, token) => this.SendUpdateMessageAsync(message, token), cancellationToken, true, false),
				communicateMessages.ForEachAsync((message, token) => this.SendInterCommunicateMessageAsync(message, token), cancellationToken)
			).ConfigureAwait(false);
			return response;
		}
		#endregion

		#region Get a desktop
		async Task<JObject> GetDesktopAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			// prepare
			var identity = requestInfo.GetObjectIdentity() ?? "";
			var desktop = await (identity.IsValidUUID() ? identity.GetDesktopByIDAsync(cancellationToken) : (requestInfo.GetParameter("x-system") ?? requestInfo.GetParameter("SystemID") ?? "").GetDesktopByAliasAsync(identity, cancellationToken)).ConfigureAwait(false);
			if (desktop == null)
				throw new InformationNotFoundException();
			else if (desktop.Organization == null)
				throw new InformationInvalidException("The organization is invalid");

			// check permission
			var isSystemAdministrator = await this.IsSystemAdministratorAsync(requestInfo).ConfigureAwait(false) || await this.IsAuthorizedAsync(requestInfo, "Organization", Components.Security.Action.Approve, cancellationToken).ConfigureAwait(false);
			var gotRights = isSystemAdministrator || requestInfo.Session.User.ID.IsEquals(desktop.Organization.OwnerID) || requestInfo.Session.User.IsViewer(desktop.Organization.WorkingPrivileges);
			if (!gotRights)
				throw new AccessDeniedException();

			if (!identity.IsValidUUID())
				return new JObject
				{
					{ "ID", desktop.ID },
					{ "Title", desktop.Title },
					{ "Alias", desktop.Alias }
				};

			// prepare the response
			if (desktop._childrenIDs == null)
			{
				await desktop.GetChildrenAsync(cancellationToken, false).ConfigureAwait(false);
				await desktop.SetAsync(false, true, cancellationToken).ConfigureAwait(false);
			}

			// send update message and response
			var response = desktop.ToJson(true, false);
			await this.SendUpdateMessageAsync(new UpdateMessage
			{
				Type = $"{this.ServiceName}#{desktop.GetTypeName(true)}#Update",
				Data = response,
				DeviceID = "*",
				ExcludedDeviceID = requestInfo.Session.DeviceID
			}, cancellationToken).ConfigureAwait(false);
			return response;
		}
		#endregion

		#region Update a desktop
		async Task<JObject> UpdateDesktopAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			// prepare
			var desktop = await (requestInfo.GetObjectIdentity() ?? "").GetDesktopByIDAsync(cancellationToken).ConfigureAwait(false);
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
			var requestBody = requestInfo.GetBodyExpando();
			var oldParentID = desktop.ParentID;

			var oldAlias = desktop.Alias;
			var alias = requestBody.Get<string>("Alias");
			if (!string.IsNullOrWhiteSpace(alias))
			{
				if (DesktopExtensions.ExcludedAliases.Contains(alias.NormalizeAlias()))
					throw new InformationExistedException($"The alias ({alias.NormalizeAlias()}) is used by another purpose");
				var existing = await desktop.SystemID.GetDesktopByAliasAsync(alias.NormalizeAlias(), cancellationToken).ConfigureAwait(false);
				if (existing != null && !existing.ID.Equals(desktop.ID))
					throw new InformationExistedException($"The alias ({alias.NormalizeAlias()}) is used by another desktop");
			}

			desktop.UpdateDesktopInstance(requestBody, "ID,SystemID,Privileges,OriginalPrivileges,Created,CreatedID,LastModified,LastModifiedID", async xdesktop =>
			{
				xdesktop.LastModified = DateTime.Now;
				xdesktop.LastModifiedID = requestInfo.Session.User.ID;
				xdesktop.NormalizeExtras();
				await xdesktop.GetChildrenAsync(cancellationToken, false).ConfigureAwait(false);
			});
			await Task.WhenAll(
				Desktop.UpdateAsync(desktop, requestInfo.Session.User.ID, cancellationToken),
				desktop.SetAsync(false, false, cancellationToken)
			).ConfigureAwait(false);

			await Task.WhenAll(
				Utility.Cache.RemoveAsync(this.GetRelatedCacheKeys(desktop.SystemID.GetDesktopsFilter(desktop.ParentID), Sorts<Desktop>.Ascending("Title")), cancellationToken),
				Utility.Cache.RemoveAsync(this.GetRelatedCacheKeys(desktop.SystemID.GetDesktopsFilter(oldParentID), Sorts<Desktop>.Ascending("Title")), cancellationToken)
			).ConfigureAwait(false);

			var updateMessages = new List<UpdateMessage>();
			var communicateMessages = new List<CommunicateMessage>();
			var objectName = desktop.GetTypeName(true);

			// update parent
			if (desktop.ParentDesktop != null && !desktop.ParentID.IsEquals(oldParentID))
			{
				await desktop.ParentDesktop.GetChildrenAsync(cancellationToken, false).ConfigureAwait(false);
				desktop.ParentDesktop._childrenIDs.Add(desktop.ID);
				await desktop.ParentDesktop.SetAsync(false, true, cancellationToken).ConfigureAwait(false);

				var json = desktop.ParentDesktop.ToJson(true, false);
				updateMessages.Add(new UpdateMessage
				{
					Type = $"{this.ServiceName}#{objectName}#Update",
					Data = json,
					DeviceID = "*"
				});
				communicateMessages.Add(new CommunicateMessage(this.ServiceName)
				{
					Type = $"{objectName}#Update",
					Data = json,
					ExcludedNodeID = this.NodeID
				});
			}

			// update old parent
			if (!string.IsNullOrWhiteSpace(oldParentID) && !oldParentID.IsEquals(desktop.ParentID))
			{
				var parentDesktop = await oldParentID.GetDesktopByIDAsync(cancellationToken).ConfigureAwait(false);
				if (parentDesktop != null)
				{
					await parentDesktop.GetChildrenAsync(cancellationToken, false).ConfigureAwait(false);
					parentDesktop._childrenIDs.Remove(desktop.ID);
					await parentDesktop.SetAsync(false, true, cancellationToken).ConfigureAwait(false);

					var json = parentDesktop.ToJson(true, false);
					updateMessages.Add(new UpdateMessage
					{
						Type = $"{this.ServiceName}#{objectName}#Update",
						Data = json,
						DeviceID = "*"
					});
					communicateMessages.Add(new CommunicateMessage(this.ServiceName)
					{
						Type = $"{objectName}#Update",
						Data = json,
						ExcludedNodeID = this.NodeID
					});
				}
			}

			// message to update to all other connected clients
			var response = desktop.ToJson(true, false);
			if (desktop.ParentDesktop == null)
				updateMessages.Add(new UpdateMessage
				{
					Type = $"{this.ServiceName}#{objectName}#Update",
					Data = response,
					DeviceID = "*",
					ExcludedDeviceID = requestInfo.Session.DeviceID
				});

			// message to update to all service instances (on all other nodes)
			communicateMessages.Add(new CommunicateMessage(this.ServiceName)
			{
				Type = $"{objectName}#Update",
				Data = response,
				ExcludedNodeID = this.NodeID
			});

			// send messages and response
			await Task.WhenAll(
				updateMessages.ForEachAsync((message, token) => this.SendUpdateMessageAsync(message, token), cancellationToken, true, false),
				communicateMessages.ForEachAsync((message, token) => this.SendInterCommunicateMessageAsync(message, token), cancellationToken)
			).ConfigureAwait(false);
			return response;
		}
		#endregion

		#region Delete a desktop
		async Task<JObject> DeleteDesktopAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			// prepare
			var desktop = await (requestInfo.GetObjectIdentity() ?? "").GetDesktopByIDAsync(cancellationToken).ConfigureAwait(false);
			if (desktop == null)
				throw new InformationNotFoundException();
			else if (desktop.Organization == null)
				throw new InformationInvalidException("The organization is invalid");

			// check permission
			var isSystemAdministrator = await this.IsSystemAdministratorAsync(requestInfo).ConfigureAwait(false) || await this.IsAuthorizedAsync(requestInfo, "Organization", Components.Security.Action.Approve, cancellationToken).ConfigureAwait(false);
			var gotRights = isSystemAdministrator || requestInfo.Session.User.ID.IsEquals(desktop.Organization.OwnerID) || requestInfo.Session.User.IsModerator(desktop.Organization.WorkingPrivileges);
			if (!gotRights)
				throw new AccessDeniedException();

			// delete
			var updateMessages = new List<UpdateMessage>();
			var communicateMessages = new List<CommunicateMessage>();
			var objectName = desktop.GetTypeName(true);
			var updateChildren = requestInfo.Header.TryGetValue("x-children", out var childrenMode) && "set-null".IsEquals(childrenMode);

			await (await desktop.GetChildrenAsync(cancellationToken, false).ConfigureAwait(false)).ForEachAsync(async (child, token) =>
			{
				// update children to root
				if (updateChildren)
				{
					child.ParentID = null;
					child.LastModified = DateTime.Now;
					child.LastModifiedID = requestInfo.Session.User.ID;

					await Task.WhenAll(
						Role.UpdateAsync(child, requestInfo.Session.User.ID, token),
						child.SetAsync(false, false, token)
					).ConfigureAwait(false);

					var json = child.ToJson(true, false);
					updateMessages.Add(new UpdateMessage
					{
						Type = $"{this.ServiceName}#{objectName}#Delete",
						Data = json,
						DeviceID = "*"
					});
					communicateMessages.Add(new CommunicateMessage(this.ServiceName)
					{
						Type = $"{objectName}#Delete",
						Data = json,
						ExcludedNodeID = this.NodeID
					});
				}

				// delete children
				else
				{
					var messages = await this.DeleteChildDesktopAsync(child, requestInfo.Session.User.ID, token).ConfigureAwait(false);
					updateMessages = updateMessages.Concat(messages.Item1).ToList();
					communicateMessages = communicateMessages.Concat(messages.Item2).ToList();
				}
			}, cancellationToken, true, false).ConfigureAwait(false);

			await Desktop.DeleteAsync<Desktop>(desktop.ID, requestInfo.Session.User.ID, cancellationToken).ConfigureAwait(false);
			desktop.Remove();

			await Task.WhenAll(
				updateChildren ? Utility.Cache.RemoveAsync(this.GetRelatedCacheKeys(desktop.SystemID.GetDesktopsFilter(null), Sorts<Desktop>.Ascending("Title")), cancellationToken) : Task.CompletedTask,
				Utility.Cache.RemoveAsync(this.GetRelatedCacheKeys(desktop.SystemID.GetDesktopsFilter(desktop.ParentID), Sorts<Desktop>.Ascending("Title")), cancellationToken)
			).ConfigureAwait(false);

			// message to update to all other connected clients
			var response = desktop.ToJson();
			updateMessages.Add(new UpdateMessage
			{
				Type = $"{this.ServiceName}#{objectName}#Delete",
				Data = response,
				DeviceID = "*",
				ExcludedDeviceID = requestInfo.Session.DeviceID
			});

			// message to update to all service instances (on all other nodes)
			communicateMessages.Add(new CommunicateMessage(this.ServiceName)
			{
				Type = $"{objectName}#Delete",
				Data = response,
				ExcludedNodeID = this.NodeID
			});

			// send messages and response
			await Task.WhenAll(
				updateMessages.ForEachAsync((message, token) => this.SendUpdateMessageAsync(message, token), cancellationToken, true, false),
				communicateMessages.ForEachAsync((message, token) => this.SendInterCommunicateMessageAsync(message, token), cancellationToken)
			).ConfigureAwait(false);
			return response;
		}

		async Task<Tuple<List<UpdateMessage>, List<CommunicateMessage>>> DeleteChildDesktopAsync(Desktop desktop, string userID, CancellationToken cancellationToken)
		{
			var updateMessages = new List<UpdateMessage>();
			var communicateMessages = new List<CommunicateMessage>();
			var objectName = desktop.GetTypeName(true);

			var children = await desktop.GetChildrenAsync(cancellationToken, false).ConfigureAwait(false);
			await children.ForEachAsync(async (child, token) =>
			{
				var messages = await this.DeleteChildDesktopAsync(child, userID, token).ConfigureAwait(false);
				updateMessages = updateMessages.Concat(messages.Item1).ToList();
				communicateMessages = communicateMessages.Concat(messages.Item2).ToList();
			}, cancellationToken, true, false).ConfigureAwait(false);

			await Desktop.DeleteAsync<Desktop>(desktop.ID, userID, cancellationToken).ConfigureAwait(false);
			desktop.Remove();

			var json = desktop.ToJson();
			updateMessages.Add(new UpdateMessage
			{
				Type = $"{this.ServiceName}#{objectName}#Delete",
				Data = json,
				DeviceID = "*"
			});
			communicateMessages.Add(new CommunicateMessage(this.ServiceName)
			{
				Type = $"{objectName}#Delete",
				Data = json,
				ExcludedNodeID = this.NodeID
			});
			return new Tuple<List<UpdateMessage>, List<CommunicateMessage>>(updateMessages, communicateMessages);
		}
		#endregion

		Task<JObject> ProcessSiteAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			switch (requestInfo.Verb)
			{
				case "GET":
					return "search".IsEquals(requestInfo.GetObjectIdentity())
						? this.SearchSitesAsync(requestInfo, cancellationToken)
						: this.GetSiteAsync(requestInfo, cancellationToken);

				case "POST":
					return this.CreateSiteAsync(requestInfo, cancellationToken);

				case "PUT":
					return this.UpdateSiteAsync(requestInfo, cancellationToken);

				case "DELETE":
					return this.DeleteSiteAsync(requestInfo, cancellationToken);
			}

			return Task.FromException<JObject>(new MethodNotAllowedException(requestInfo.Verb));
		}

		#region Search sites
		async Task<JObject> SearchSitesAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			// prepare
			var request = requestInfo.GetRequestExpando();

			var query = request.Get<string>("FilterBy.Query");
			var filter = request.Get<ExpandoObject>("FilterBy", null)?.ToFilterBy<Site>() ?? Filters<Site>.And();
			var sort = string.IsNullOrWhiteSpace(query) ? request.Get<ExpandoObject>("SortBy")?.ToSortBy<Site>() ?? Sorts<Site>.Ascending("Title") : null;

			var pagination = request.Get<ExpandoObject>("Pagination")?.GetPagination() ?? new Tuple<long, int, int, int>(-1, 0, 20, 1);
			var pageSize = pagination.Item3;
			var pageNumber = pagination.Item4;

			// check permission
			var isSystemAdministrator = await this.IsSystemAdministratorAsync(requestInfo).ConfigureAwait(false) || await this.IsAuthorizedAsync(requestInfo, "Organization", Components.Security.Action.Approve, cancellationToken).ConfigureAwait(false);
			var gotRights = isSystemAdministrator;
			if (!gotRights)
			{
				// get organization
				var organizationID = filter is FilterBys<Site>
					? ((filter as FilterBys<Site>).Children.FirstOrDefault(exp => (exp as FilterBy<Site>).Attribute.IsEquals("SystemID")) as FilterBy<Site>)?.Value as string
					: null;
				if (string.IsNullOrWhiteSpace(organizationID))
					organizationID = requestInfo.GetParameter("x-system") ?? requestInfo.GetParameter("SystemID");
				var organization = await (organizationID ?? "").GetOrganizationByIDAsync(cancellationToken).ConfigureAwait(false);
				if (organization == null)
					throw new InformationExistedException("The organization is invalid");

				gotRights = requestInfo.Session.User.ID.IsEquals(organization.OwnerID) || requestInfo.Session.User.IsModerator(organization.WorkingPrivileges);
				if (!gotRights)
					throw new AccessDeniedException();
			}

			// process cache
			var json = string.IsNullOrWhiteSpace(query) ? await Utility.Cache.GetAsync<string>(this.GetCacheKeyOfObjectsJson(filter, sort, pageSize, pageNumber), cancellationToken).ConfigureAwait(false) : null;
			if (!string.IsNullOrWhiteSpace(json))
				return JObject.Parse(json);

			// prepare pagination
			var totalRecords = pagination.Item1 > -1 ? pagination.Item1 : -1;
			if (totalRecords < 0)
				totalRecords = string.IsNullOrWhiteSpace(query)
					? await Site.CountAsync(filter, this.GetCacheKeyOfTotalObjects(filter, sort), cancellationToken).ConfigureAwait(false)
					: await Site.CountAsync(query, filter, cancellationToken).ConfigureAwait(false);

			var totalPages = new Tuple<long, int>(totalRecords, pageSize).GetTotalPages();
			if (totalPages > 0 && pageNumber > totalPages)
				pageNumber = totalPages;

			// search
			var objects = totalRecords > 0
				? string.IsNullOrWhiteSpace(query)
					? await Site.FindAsync(filter, sort, pageSize, pageNumber, this.GetCacheKey(filter, sort, pageSize, pageNumber), cancellationToken).ConfigureAwait(false)
					: await Site.SearchAsync(query, filter, pageSize, pageNumber, cancellationToken).ConfigureAwait(false)
				: new List<Site>();

			// build response
			pagination = new Tuple<long, int, int, int>(totalRecords, totalPages, pageSize, pageNumber);
			var response = new JObject()
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
				json = response.ToString(Formatting.Indented);
#else
				json = response.ToString(Formatting.Indented);
#endif
				await Utility.Cache.SetAsync(this.GetCacheKeyOfObjectsJson(filter, sort, pageSize, pageNumber), json, Utility.Cache.ExpirationTime / 2).ConfigureAwait(false);
			}

			// response
			return response;
		}
		#endregion

		#region Create a site
		async Task<JObject> CreateSiteAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			// prepare
			var requestBody = requestInfo.GetBodyExpando();
			var organizationID = requestBody.Get<string>("SystemID") ?? requestInfo.GetParameter("x-system") ?? requestInfo.GetParameter("SystemID");
			var organization = await (organizationID ?? "").GetOrganizationByIDAsync(cancellationToken).ConfigureAwait(false);
			if (organization == null)
				throw new InformationInvalidException("The organization is invalid");

			// check permission
			var isSystemAdministrator = await this.IsSystemAdministratorAsync(requestInfo).ConfigureAwait(false) || await this.IsAuthorizedAsync(requestInfo, "Organization", Components.Security.Action.Approve, cancellationToken).ConfigureAwait(false);
			var gotRights = isSystemAdministrator || requestInfo.Session.User.ID.IsEquals(organization.OwnerID) || requestInfo.Session.User.IsModerator(organization.WorkingPrivileges);
			if (!gotRights)
				throw new AccessDeniedException();

			// check domain
			var domain = $"{requestBody.Get<string>("SubDomain")}.{requestBody.Get<string>("PrimaryDomain")}";
			var existing = await organization.ID.GetSiteByDomainAsync(domain, cancellationToken).ConfigureAwait(false);
			if (existing != null)
				throw new InformationExistedException($"The alias ({domain.NormalizeAlias()}) is used by another site");

			// create new
			var site = requestBody.CreateSiteInstance("SystemID,Privileges,OriginalPrivileges,Created,CreatedID,LastModified,LastModifiedID", xsite =>
			{
				xsite.ID = string.IsNullOrWhiteSpace(xsite.ID) || !xsite.ID.IsValidUUID() ? UtilityService.NewUUID : xsite.ID;
				xsite.SystemID = organization.ID;
				xsite.Created = xsite.LastModified = DateTime.Now;
				xsite.CreatedID = xsite.LastModifiedID = requestInfo.Session.User.ID;
				xsite.NormalizeExtras();
			});
			await Task.WhenAll(
				Site.CreateAsync(site, cancellationToken),
				Utility.Cache.RemoveAsync(this.GetRelatedCacheKeys(Filters<Site>.And(), Sorts<Site>.Ascending("Title")), cancellationToken),
				Utility.Cache.RemoveAsync(this.GetRelatedCacheKeys(Filters<Site>.And(Filters<Site>.Equals("SystemID", site.SystemID)), Sorts<Site>.Ascending("Title")), cancellationToken),
				site.SetAsync(false, false, cancellationToken)
			).ConfigureAwait(false);

			// send update messages
			var response = site.ToJson();
			var objectName = site.GetTypeName(true);
			await Task.WhenAll(
				this.SendUpdateMessageAsync(new UpdateMessage
				{
					Type = $"{this.ServiceName}#{objectName}#Update",
					Data = response,
					DeviceID = "*",
					ExcludedDeviceID = requestInfo.Session.DeviceID
				}, cancellationToken),
				this.SendInterCommunicateMessageAsync(new CommunicateMessage(this.ServiceName)
				{
					Type = $"{objectName}#Update",
					Data = response,
					ExcludedNodeID = this.NodeID
				}, cancellationToken)
			).ConfigureAwait(false);

			// response
			return response;
		}
		#endregion

		#region Get a site
		async Task<JObject> GetSiteAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			// prepare
			var identity = requestInfo.GetObjectIdentity() ?? "";
			var site = await (identity.IsValidUUID() ? identity.GetSiteByIDAsync(cancellationToken) : (requestInfo.GetParameter("x-system") ?? requestInfo.GetParameter("SystemID") ?? "").GetSiteByDomainAsync(identity, cancellationToken)).ConfigureAwait(false);
			if (site == null)
				throw new InformationNotFoundException();
			else if (site.Organization == null)
				throw new InformationInvalidException("The organization is invalid");

			// check permission
			var isSystemAdministrator = await this.IsSystemAdministratorAsync(requestInfo).ConfigureAwait(false) || await this.IsAuthorizedAsync(requestInfo, "Organization", Components.Security.Action.Approve, cancellationToken).ConfigureAwait(false);
			var gotRights = isSystemAdministrator || requestInfo.Session.User.ID.IsEquals(site.Organization.OwnerID) || requestInfo.Session.User.IsViewer(site.Organization.WorkingPrivileges);
			if (!gotRights)
				throw new AccessDeniedException();

			if (!identity.IsValidUUID())
				return new JObject
				{
					{ "ID", site.ID },
					{ "Title", site.Title },
					{ "Domain", (site.SubDomain.Equals("*") ? "" : site.SubDomain + ".") + site.PrimaryDomain }
				};

			// send update message and response
			var response = site.ToJson();
			await this.SendUpdateMessageAsync(new UpdateMessage
			{
				Type = $"{this.ServiceName}#{site.GetTypeName(true)}#Update",
				Data = response,
				DeviceID = "*",
				ExcludedDeviceID = requestInfo.Session.DeviceID
			}, cancellationToken).ConfigureAwait(false);
			return response;
		}
		#endregion

		#region Update a site
		async Task<JObject> UpdateSiteAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			// prepare
			var site = await (requestInfo.GetObjectIdentity() ?? "").GetSiteByIDAsync(cancellationToken).ConfigureAwait(false);
			if (site == null)
				throw new InformationNotFoundException();
			else if (site.Organization == null)
				throw new InformationInvalidException("The organization is invalid");

			// check permission
			var isSystemAdministrator = await this.IsSystemAdministratorAsync(requestInfo).ConfigureAwait(false) || await this.IsAuthorizedAsync(requestInfo, "Organization", Components.Security.Action.Approve, cancellationToken).ConfigureAwait(false);
			var gotRights = isSystemAdministrator || requestInfo.Session.User.ID.IsEquals(site.Organization.OwnerID) || requestInfo.Session.User.IsModerator(site.Organization.WorkingPrivileges);
			if (!gotRights)
				throw new AccessDeniedException();

			// check domain
			var requestBody = requestInfo.GetBodyExpando();
			var domain = $"{requestBody.Get<string>("SubDomain")}.{requestBody.Get<string>("PrimaryDomain")}";
			var existing = await site.Organization.ID.GetSiteByDomainAsync(domain, cancellationToken).ConfigureAwait(false);
			if (existing != null)
				throw new InformationExistedException($"The domain ({domain}) is used by another site");

			// update
			site.UpdateSiteInstance(requestBody, "ID,SystemID,Privileges,OriginalPrivileges,Created,CreatedID,LastModified,LastModifiedID", xsite =>
			{
				xsite.LastModified = DateTime.Now;
				xsite.LastModifiedID = requestInfo.Session.User.ID;
				xsite.NormalizeExtras();
			});
			await Task.WhenAll(
				Site.UpdateAsync(site, requestInfo.Session.User.ID, cancellationToken),
				Utility.Cache.RemoveAsync(this.GetRelatedCacheKeys(Filters<Site>.And(), Sorts<Site>.Ascending("Title")), cancellationToken),
				Utility.Cache.RemoveAsync(this.GetRelatedCacheKeys(Filters<Site>.And(Filters<Site>.Equals("SystemID", site.SystemID)), Sorts<Site>.Ascending("Title")), cancellationToken),
				site.SetAsync(false, false, cancellationToken)
			).ConfigureAwait(false);

			// send update messages
			var response = site.ToJson();
			var objectName = site.GetTypeName(true);
			await Task.WhenAll(
				this.SendUpdateMessageAsync(new UpdateMessage
				{
					Type = $"{this.ServiceName}#{objectName}#Update",
					Data = response,
					DeviceID = "*",
					ExcludedDeviceID = requestInfo.Session.DeviceID
				}, cancellationToken),
				this.SendInterCommunicateMessageAsync(new CommunicateMessage(this.ServiceName)
				{
					Type = $"{objectName}#Update",
					Data = response,
					ExcludedNodeID = this.NodeID
				}, cancellationToken)
			).ConfigureAwait(false);

			// response
			return response;
		}
		#endregion

		#region Delete a site
		async Task<JObject> DeleteSiteAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			// prepare
			var site = await (requestInfo.GetObjectIdentity() ?? "").GetSiteByIDAsync(cancellationToken).ConfigureAwait(false);
			if (site == null)
				throw new InformationNotFoundException();
			else if (site.Organization == null)
				throw new InformationInvalidException("The organization is invalid");

			// check permission
			var isSystemAdministrator = await this.IsSystemAdministratorAsync(requestInfo).ConfigureAwait(false) || await this.IsAuthorizedAsync(requestInfo, "Organization", Components.Security.Action.Approve, cancellationToken).ConfigureAwait(false);
			var gotRights = isSystemAdministrator || requestInfo.Session.User.ID.IsEquals(site.Organization.OwnerID) || requestInfo.Session.User.IsModerator(site.Organization.WorkingPrivileges);
			if (!gotRights)
				throw new AccessDeniedException();

			// delete
			await Site.DeleteAsync<Site>(site.ID, requestInfo.Session.User.ID, cancellationToken).ConfigureAwait(false);
			site.Remove();
			await Utility.Cache.RemoveAsync(this.GetRelatedCacheKeys(Filters<Site>.And(Filters<Site>.Equals("SystemID", site.SystemID)), Sorts<Site>.Ascending("Title")), cancellationToken).ConfigureAwait(false);

			// send update messages
			var response = site.ToJson();
			var objectName = site.GetTypeName(true);
			await Task.WhenAll(
				this.SendUpdateMessageAsync(new UpdateMessage
				{
					Type = $"{this.ServiceName}#{objectName}#Delete",
					Data = response,
					DeviceID = "*",
					ExcludedDeviceID = requestInfo.Session.DeviceID
				}, cancellationToken),
				this.SendInterCommunicateMessageAsync(new CommunicateMessage(this.ServiceName)
				{
					Type = $"{objectName}#Delete",
					Data = response,
					ExcludedNodeID = this.NodeID
				}, cancellationToken)
			).ConfigureAwait(false);

			// response
			return response;
		}
		#endregion

		Task<JObject> ProcessModuleAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			switch (requestInfo.Verb)
			{
				case "GET":
					return "search".IsEquals(requestInfo.GetObjectIdentity())
						? this.SearchModulesAsync(requestInfo, cancellationToken)
						: this.GetModuleAsync(requestInfo, cancellationToken);

				case "POST":
					return this.CreateModuleAsync(requestInfo, cancellationToken);

				case "PUT":
					return this.UpdateModuleAsync(requestInfo, cancellationToken);

				case "DELETE":
					return this.DeleteModuleAsync(requestInfo, cancellationToken);
			}

			return Task.FromException<JObject>(new MethodNotAllowedException(requestInfo.Verb));
		}

		#region Search modules
		async Task<JObject> SearchModulesAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			// prepare
			var request = requestInfo.GetRequestExpando();

			var query = request.Get<string>("FilterBy.Query");
			var filter = request.Get<ExpandoObject>("FilterBy")?.ToFilterBy<Module>() ?? Filters<Module>.And();
			var sort = string.IsNullOrWhiteSpace(query) ? request.Get<ExpandoObject>("SortBy")?.ToSortBy<Module>() ?? Sorts<Module>.Ascending("Title") : null;

			var pagination = request.Get<ExpandoObject>("Pagination")?.GetPagination() ?? new Tuple<long, int, int, int>(-1, 0, 20, 1);
			var pageSize = pagination.Item3;
			var pageNumber = pagination.Item4;

			// check permission
			var isSystemAdministrator = await this.IsSystemAdministratorAsync(requestInfo).ConfigureAwait(false) || await this.IsAuthorizedAsync(requestInfo, "Organization", Components.Security.Action.Approve, cancellationToken).ConfigureAwait(false);
			var gotRights = isSystemAdministrator;
			if (!gotRights)
			{
				// get organization
				var organizationID = filter is FilterBys<Site>
					? ((filter as FilterBys<Site>).Children.FirstOrDefault(exp => (exp as FilterBy<Site>).Attribute.IsEquals("SystemID")) as FilterBy<Site>)?.Value as string
					: null;
				if (string.IsNullOrWhiteSpace(organizationID))
					organizationID = requestInfo.GetParameter("x-system") ?? requestInfo.GetParameter("SystemID");
				var organization = await (organizationID ?? "").GetOrganizationByIDAsync(cancellationToken).ConfigureAwait(false);
				if (organization == null)
					throw new InformationExistedException("The organization is invalid");

				gotRights = requestInfo.Session.User.ID.IsEquals(organization.OwnerID) || requestInfo.Session.User.IsModerator(organization.WorkingPrivileges);
				if (!gotRights)
					throw new AccessDeniedException();
			}

			// process cache
			var json = string.IsNullOrWhiteSpace(query) ? await Utility.Cache.GetAsync<string>(this.GetCacheKeyOfObjectsJson(filter, sort, pageSize, pageNumber), cancellationToken).ConfigureAwait(false) : null;
			if (!string.IsNullOrWhiteSpace(json))
				return JObject.Parse(json);

			// prepare pagination
			var totalRecords = pagination.Item1 > -1 ? pagination.Item1 : -1;
			if (totalRecords < 0)
				totalRecords = string.IsNullOrWhiteSpace(query)
					? await Module.CountAsync(filter, this.GetCacheKeyOfTotalObjects(filter, sort), cancellationToken).ConfigureAwait(false)
					: await Module.CountAsync(query, filter, cancellationToken).ConfigureAwait(false);

			var totalPages = new Tuple<long, int>(totalRecords, pageSize).GetTotalPages();
			if (totalPages > 0 && pageNumber > totalPages)
				pageNumber = totalPages;

			// search
			var objects = totalRecords > 0
				? string.IsNullOrWhiteSpace(query)
					? await Module.FindAsync(filter, sort, pageSize, pageNumber, this.GetCacheKey(filter, sort, pageSize, pageNumber), cancellationToken).ConfigureAwait(false)
					: await Module.SearchAsync(query, filter, pageSize, pageNumber, cancellationToken).ConfigureAwait(false)
				: new List<Module>();

			// build result
			pagination = new Tuple<long, int, int, int>(totalRecords, totalPages, pageSize, pageNumber);
			var response = new JObject
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
				json = response.ToString(Formatting.Indented);
#else
				json = response.ToString(Formatting.Indented);
#endif
				await Utility.Cache.SetAsync(this.GetCacheKeyOfObjectsJson(filter, sort, pageSize, pageNumber), json, Utility.Cache.ExpirationTime / 2).ConfigureAwait(false);
			}

			// response
			return response;
		}
		#endregion

		#region Create a module
		async Task<JObject> CreateModuleAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			// prepare
			var requestBody = requestInfo.GetBodyExpando();
			var organizationID = requestBody.Get<string>("SystemID") ?? requestInfo.GetParameter("x-system") ?? requestInfo.GetParameter("SystemID");
			var organization = await (organizationID ?? "").GetOrganizationByIDAsync(cancellationToken).ConfigureAwait(false);
			if (organization == null)
				throw new InformationInvalidException("The organization is invalid");

			// check permission
			var isSystemAdministrator = await this.IsSystemAdministratorAsync(requestInfo).ConfigureAwait(false) || await this.IsAuthorizedAsync(requestInfo, "Organization", Components.Security.Action.Approve, cancellationToken).ConfigureAwait(false);
			var gotRights = isSystemAdministrator || requestInfo.Session.User.ID.IsEquals(organization.OwnerID) || requestInfo.Session.User.IsModerator(organization.WorkingPrivileges);
			if (!gotRights)
				throw new AccessDeniedException();

			// create new
			var module = requestBody.CreateModuleInstance("SystemID,Privileges,Created,CreatedID,LastModified,LastModifiedID", xmodule =>
			{
				xmodule.ID = string.IsNullOrWhiteSpace(xmodule.ID) || !xmodule.ID.IsValidUUID() ? UtilityService.NewUUID : xmodule.ID;
				xmodule.SystemID = organization.ID;
				xmodule.Created = xmodule.LastModified = DateTime.Now;
				xmodule.CreatedID = xmodule.LastModifiedID = requestInfo.Session.User.ID;
				xmodule.NormalizeExtras();
			});
			await Task.WhenAll(
				Module.CreateAsync(module, cancellationToken),
				Utility.Cache.RemoveAsync(this.GetRelatedCacheKeys(Filters<Module>.And(), Sorts<Module>.Ascending("Title")), cancellationToken),
				Utility.Cache.RemoveAsync(this.GetRelatedCacheKeys(module.SystemID.GetModulesFilter(), Sorts<Module>.Ascending("Title")), cancellationToken),
				Utility.Cache.RemoveAsync(this.GetRelatedCacheKeys(module.SystemID.GetModulesFilter(module.ModuleDefinitionID), Sorts<Module>.Ascending("Title")), cancellationToken),
				module.SetAsync(false, cancellationToken)
			).ConfigureAwait(false);

			// send update messages
			var response = module.ToJson();
			var objectName = module.GetTypeName(true);
			await Task.WhenAll(
				this.SendUpdateMessageAsync(new UpdateMessage
				{
					Type = $"{this.ServiceName}#{objectName}#Update",
					Data = response,
					DeviceID = "*",
					ExcludedDeviceID = requestInfo.Session.DeviceID
				}, cancellationToken),
				this.SendInterCommunicateMessageAsync(new CommunicateMessage(this.ServiceName)
				{
					Type = $"{objectName}#Update",
					Data = response,
					ExcludedNodeID = this.NodeID
				}, cancellationToken)
			).ConfigureAwait(false);

			// response
			return response;
		}
		#endregion

		#region Get a module
		async Task<JObject> GetModuleAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			// prepare
			var module = await (requestInfo.GetObjectIdentity() ?? "").GetModuleByIDAsync(cancellationToken).ConfigureAwait(false);
			if (module == null)
				throw new InformationNotFoundException();
			else if (module.Organization == null)
				throw new InformationInvalidException("The organization is invalid");

			// check permission
			var isSystemAdministrator = await this.IsSystemAdministratorAsync(requestInfo).ConfigureAwait(false) || await this.IsAuthorizedAsync(requestInfo, "Organization", Components.Security.Action.Approve, cancellationToken).ConfigureAwait(false);
			var gotRights = isSystemAdministrator || requestInfo.Session.User.ID.IsEquals(module.Organization.OwnerID) || requestInfo.Session.User.IsViewer(module.Organization.WorkingPrivileges);
			if (!gotRights)
				throw new AccessDeniedException();

			await module.GetContentTypeAsync(cancellationToken).ConfigureAwait(false);
			await module.SetAsync(true, cancellationToken).ConfigureAwait(false);

			// send the update message to update to all other connected clients and response
			var response = module.ToJson(true, false);
			await this.SendUpdateMessageAsync(new UpdateMessage
			{
				Type = $"{this.ServiceName}#{module.GetTypeName(true)}#Update",
				Data = response,
				DeviceID = "*",
				ExcludedDeviceID = requestInfo.Session.DeviceID
			}, cancellationToken).ConfigureAwait(false);
			return response;
		}
		#endregion

		#region Update a module
		async Task<JObject> UpdateModuleAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			// prepare
			var module = await (requestInfo.GetObjectIdentity() ?? "").GetModuleByIDAsync(cancellationToken).ConfigureAwait(false);
			if (module == null)
				throw new InformationNotFoundException();
			else if (module.Organization == null)
				throw new InformationInvalidException("The organization is invalid");

			// check permission
			var isSystemAdministrator = await this.IsSystemAdministratorAsync(requestInfo).ConfigureAwait(false) || await this.IsAuthorizedAsync(requestInfo, "Organization", Components.Security.Action.Approve, cancellationToken).ConfigureAwait(false);
			var gotRights = isSystemAdministrator || requestInfo.Session.User.ID.IsEquals(module.Organization.OwnerID) || requestInfo.Session.User.IsModerator(module.Organization.WorkingPrivileges);
			if (!gotRights)
				throw new AccessDeniedException();

			// update
			module.UpdateModuleInstance(requestInfo.GetBodyExpando(), "ID,SystemID,Privileges,Created,CreatedID,LastModified,LastModifiedID", xmodule =>
			{
				xmodule.LastModified = DateTime.Now;
				xmodule.LastModifiedID = requestInfo.Session.User.ID;
				xmodule.NormalizeExtras();
			});
			await Task.WhenAll(
				Module.UpdateAsync(module, requestInfo.Session.User.ID, cancellationToken),
				Utility.Cache.RemoveAsync(this.GetRelatedCacheKeys(Filters<Module>.And(), Sorts<Module>.Ascending("Title")), cancellationToken),
				Utility.Cache.RemoveAsync(this.GetRelatedCacheKeys(module.SystemID.GetModulesFilter(), Sorts<Module>.Ascending("Title")), cancellationToken),
				Utility.Cache.RemoveAsync(this.GetRelatedCacheKeys(module.SystemID.GetModulesFilter(module.ModuleDefinitionID), Sorts<Module>.Ascending("Title")), cancellationToken),
				module.SetAsync(false, cancellationToken)
			).ConfigureAwait(false);

			// send update messages
			var response = module.ToJson();
			var objectName = module.GetTypeName(true);
			await Task.WhenAll(
				this.SendUpdateMessageAsync(new UpdateMessage
				{
					Type = $"{this.ServiceName}#{objectName}#Update",
					Data = response,
					DeviceID = "*",
					ExcludedDeviceID = requestInfo.Session.DeviceID
				}, cancellationToken),
				this.SendInterCommunicateMessageAsync(new CommunicateMessage(this.ServiceName)
				{
					Type = $"{objectName}#Update",
					Data = response,
					ExcludedNodeID = this.NodeID
				}, cancellationToken)
			).ConfigureAwait(false);

			// response
			return response;
		}
		#endregion

		#region Delete a module
		async Task<JObject> DeleteModuleAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			// prepare
			var module = await (requestInfo.GetObjectIdentity() ?? "").GetModuleByIDAsync(cancellationToken).ConfigureAwait(false);
			if (module == null)
				throw new InformationNotFoundException();
			else if (module.Organization == null)
				throw new InformationInvalidException("The organization is invalid");

			// check permission
			var isSystemAdministrator = await this.IsSystemAdministratorAsync(requestInfo).ConfigureAwait(false) || await this.IsAuthorizedAsync(requestInfo, "Organization", Components.Security.Action.Approve, cancellationToken).ConfigureAwait(false);
			var gotRights = isSystemAdministrator || requestInfo.Session.User.ID.IsEquals(module.Organization.OwnerID) || requestInfo.Session.User.IsModerator(module.Organization.WorkingPrivileges);
			if (!gotRights)
				throw new AccessDeniedException();

			// TO DO: delete all content-types and business objects first
			// .......

			// delete
			await Module.DeleteAsync<Module>(module.ID, requestInfo.Session.User.ID, cancellationToken).ConfigureAwait(false);
			module.Remove();

			await Task.WhenAll(
				Utility.Cache.RemoveAsync(this.GetRelatedCacheKeys(Filters<Module>.And(), Sorts<Module>.Ascending("Title")), cancellationToken),
				Utility.Cache.RemoveAsync(this.GetRelatedCacheKeys(module.SystemID.GetModulesFilter(), Sorts<Module>.Ascending("Title")), cancellationToken),
				Utility.Cache.RemoveAsync(this.GetRelatedCacheKeys(module.SystemID.GetModulesFilter(module.ModuleDefinitionID), Sorts<Module>.Ascending("Title")), cancellationToken)
			).ConfigureAwait(false);

			// send update messages
			var response = module.ToJson();
			var objectName = module.GetTypeName(true);
			await Task.WhenAll(
				this.SendUpdateMessageAsync(new UpdateMessage
				{
					Type = $"{this.ServiceName}#{objectName}#Delete",
					Data = response,
					DeviceID = "*",
					ExcludedDeviceID = requestInfo.Session.DeviceID
				}, cancellationToken),
				this.SendInterCommunicateMessageAsync(new CommunicateMessage(this.ServiceName)
				{
					Type = $"{objectName}#Delete",
					Data = response,
					ExcludedNodeID = this.NodeID
				}, cancellationToken)
			).ConfigureAwait(false);

			// response
			return response;
		}
		#endregion

		Task<JObject> ProcessContentTypeAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			switch (requestInfo.Verb)
			{
				case "GET":
					return "search".IsEquals(requestInfo.GetObjectIdentity())
						? this.SearchContentTypesAsync(requestInfo, cancellationToken)
						: this.GetContentTypeAsync(requestInfo, cancellationToken);

				case "POST":
					return this.CreateContentTypeAsync(requestInfo, cancellationToken);

				case "PUT":
					return this.UpdateContentTypeAsync(requestInfo, cancellationToken);

				case "DELETE":
					return this.DeleteContentTypeAsync(requestInfo, cancellationToken);
			}

			return Task.FromException<JObject>(new MethodNotAllowedException(requestInfo.Verb));
		}

		#region Search content-types
		async Task<JObject> SearchContentTypesAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			// prepare
			var request = requestInfo.GetRequestExpando();

			var query = request.Get<string>("FilterBy.Query");
			var filter = request.Get<ExpandoObject>("FilterBy")?.ToFilterBy<ContentType>() ?? Filters<ContentType>.And();
			var sort = string.IsNullOrWhiteSpace(query) ? request.Get<ExpandoObject>("SortBy")?.ToSortBy<ContentType>() ?? Sorts<ContentType>.Ascending("Title") : null;

			var pagination = request.Get<ExpandoObject>("Pagination")?.GetPagination() ?? new Tuple<long, int, int, int>(-1, 0, 20, 1);
			var pageSize = pagination.Item3;
			var pageNumber = pagination.Item4;

			// check permission
			var isSystemAdministrator = await this.IsSystemAdministratorAsync(requestInfo).ConfigureAwait(false) || await this.IsAuthorizedAsync(requestInfo, "Organization", Components.Security.Action.Approve, cancellationToken).ConfigureAwait(false);
			var gotRights = isSystemAdministrator;
			if (!gotRights)
			{
				// get organization
				var organizationID = filter is FilterBys<Site>
					? ((filter as FilterBys<Site>).Children.FirstOrDefault(exp => (exp as FilterBy<Site>).Attribute.IsEquals("SystemID")) as FilterBy<Site>)?.Value as string
					: null;
				if (string.IsNullOrWhiteSpace(organizationID))
					organizationID = requestInfo.GetParameter("x-system") ?? requestInfo.GetParameter("SystemID");
				var organization = await (organizationID ?? "").GetOrganizationByIDAsync(cancellationToken).ConfigureAwait(false);
				if (organization == null)
					throw new InformationExistedException("The organization is invalid");

				gotRights = requestInfo.Session.User.ID.IsEquals(organization.OwnerID) || requestInfo.Session.User.IsModerator(organization.WorkingPrivileges);
				if (!gotRights)
					throw new AccessDeniedException();
			}

			// process cache
			var json = string.IsNullOrWhiteSpace(query) ? await Utility.Cache.GetAsync<string>(this.GetCacheKeyOfObjectsJson(filter, sort, pageSize, pageNumber), cancellationToken).ConfigureAwait(false) : null;
			if (!string.IsNullOrWhiteSpace(json))
				return JObject.Parse(json);

			// prepare pagination
			var totalRecords = pagination.Item1 > -1 ? pagination.Item1 : -1;
			if (totalRecords < 0)
				totalRecords = string.IsNullOrWhiteSpace(query)
					? await ContentType.CountAsync(filter, this.GetCacheKeyOfTotalObjects(filter, sort), cancellationToken).ConfigureAwait(false)
					: await ContentType.CountAsync(query, filter, cancellationToken).ConfigureAwait(false);

			var totalPages = new Tuple<long, int>(totalRecords, pageSize).GetTotalPages();
			if (totalPages > 0 && pageNumber > totalPages)
				pageNumber = totalPages;

			// search
			var objects = totalRecords > 0
				? string.IsNullOrWhiteSpace(query)
					? await ContentType.FindAsync(filter, sort, pageSize, pageNumber, this.GetCacheKey(filter, sort, pageSize, pageNumber), cancellationToken).ConfigureAwait(false)
					: await ContentType.SearchAsync(query, filter, pageSize, pageNumber, cancellationToken).ConfigureAwait(false)
				: new List<ContentType>();

			// build result
			pagination = new Tuple<long, int, int, int>(totalRecords, totalPages, pageSize, pageNumber);
			var response = new JObject
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
				json = response.ToString(Formatting.Indented);
#else
				json = response.ToString(Formatting.Indented);
#endif
				await Utility.Cache.SetAsync(this.GetCacheKeyOfObjectsJson(filter, sort, pageSize, pageNumber), json, Utility.Cache.ExpirationTime / 2).ConfigureAwait(false);
			}

			// response
			return response;
		}
		#endregion

		#region Create a content-type
		async Task<JObject> CreateContentTypeAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			// prepare
			var requestBody = requestInfo.GetBodyExpando();
			var organizationID = requestBody.Get<string>("SystemID") ?? requestInfo.GetParameter("x-system") ?? requestInfo.GetParameter("SystemID");
			var organization = await (organizationID ?? "").GetOrganizationByIDAsync(cancellationToken).ConfigureAwait(false);
			if (organization == null)
				throw new InformationInvalidException("The organization is invalid");

			// check permission
			var isSystemAdministrator = await this.IsSystemAdministratorAsync(requestInfo).ConfigureAwait(false) || await this.IsAuthorizedAsync(requestInfo, "Organization", Components.Security.Action.Approve, cancellationToken).ConfigureAwait(false);
			var gotRights = isSystemAdministrator || requestInfo.Session.User.ID.IsEquals(organization.OwnerID) || requestInfo.Session.User.IsModerator(organization.WorkingPrivileges);
			if (!gotRights)
				throw new AccessDeniedException();

			// create new
			var contentType = requestBody.CreateContentTypeInstance("SystemID,Privileges,Created,CreatedID,LastModified,LastModifiedID", xcontentType =>
			{
				xcontentType.ID = string.IsNullOrWhiteSpace(xcontentType.ID) || !xcontentType.ID.IsValidUUID() ? UtilityService.NewUUID : xcontentType.ID;
				xcontentType.SystemID = organization.ID;
				xcontentType.Created = xcontentType.LastModified = DateTime.Now;
				xcontentType.CreatedID = xcontentType.LastModifiedID = requestInfo.Session.User.ID;
				xcontentType.NormalizeExtras();
			});
			await Task.WhenAll(
				ContentType.CreateAsync(contentType, cancellationToken),
				Utility.Cache.RemoveAsync(this.GetRelatedCacheKeys(Filters<ContentType>.And(), Sorts<ContentType>.Ascending("Title")), cancellationToken),
				Utility.Cache.RemoveAsync(this.GetRelatedCacheKeys(contentType.SystemID.GetContentTypesFilter(null), Sorts<ContentType>.Ascending("Title")), cancellationToken),
				Utility.Cache.RemoveAsync(this.GetRelatedCacheKeys(contentType.SystemID.GetContentTypesFilter(contentType.RepositoryID), Sorts<ContentType>.Ascending("Title")), cancellationToken),
				Utility.Cache.RemoveAsync(this.GetRelatedCacheKeys(contentType.SystemID.GetContentTypesFilter(null, contentType.ContentTypeDefinitionID), Sorts<ContentType>.Ascending("Title")), cancellationToken),
				Utility.Cache.RemoveAsync(this.GetRelatedCacheKeys(contentType.SystemID.GetContentTypesFilter(contentType.RepositoryID, contentType.ContentTypeDefinitionID), Sorts<ContentType>.Ascending("Title")), cancellationToken),
				contentType.SetAsync(false, cancellationToken)
			).ConfigureAwait(false);

			// send update messages
			var response = contentType.ToJson();
			var objectName = contentType.GetTypeName(true);
			await Task.WhenAll(
				this.SendUpdateMessageAsync(new UpdateMessage
				{
					Type = $"{this.ServiceName}#{objectName}#Update",
					Data = response,
					DeviceID = "*",
					ExcludedDeviceID = requestInfo.Session.DeviceID
				}, cancellationToken),
				this.SendInterCommunicateMessageAsync(new CommunicateMessage(this.ServiceName)
				{
					Type = $"{objectName}#Update",
					Data = response,
					ExcludedNodeID = this.NodeID
				}, cancellationToken)
			).ConfigureAwait(false);

			// response
			return response;
		}
		#endregion

		#region Get a content-type
		async Task<JObject> GetContentTypeAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			// prepare
			var contentType = await (requestInfo.GetObjectIdentity() ?? "").GetContentTypeByIDAsync(cancellationToken).ConfigureAwait(false);
			if (contentType == null)
				throw new InformationNotFoundException();
			else if (contentType.Organization == null)
				throw new InformationInvalidException("The organization is invalid");

			// check permission
			var isSystemAdministrator = await this.IsSystemAdministratorAsync(requestInfo).ConfigureAwait(false) || await this.IsAuthorizedAsync(requestInfo, "Organization", Components.Security.Action.Approve, cancellationToken).ConfigureAwait(false);
			var gotRights = isSystemAdministrator || requestInfo.Session.User.ID.IsEquals(contentType.Organization.OwnerID) || requestInfo.Session.User.IsViewer(contentType.Organization.WorkingPrivileges);
			if (!gotRights)
				throw new AccessDeniedException();

			// send the update message to update to all other connected clients and response
			var response = contentType.ToJson();
			await this.SendUpdateMessageAsync(new UpdateMessage
			{
				Type = $"{this.ServiceName}#{contentType.GetTypeName(true)}#Update",
				Data = response,
				DeviceID = "*",
				ExcludedDeviceID = requestInfo.Session.DeviceID
			}, cancellationToken).ConfigureAwait(false);
			return response;
		}
		#endregion

		#region Update a content-type
		async Task<JObject> UpdateContentTypeAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			// prepare
			var contentType = await (requestInfo.GetObjectIdentity() ?? "").GetContentTypeByIDAsync(cancellationToken).ConfigureAwait(false);
			if (contentType == null)
				throw new InformationNotFoundException();
			else if (contentType.Organization == null)
				throw new InformationInvalidException("The organization is invalid");

			// check permission
			var isSystemAdministrator = await this.IsSystemAdministratorAsync(requestInfo).ConfigureAwait(false) || await this.IsAuthorizedAsync(requestInfo, "Organization", Components.Security.Action.Approve, cancellationToken).ConfigureAwait(false);
			var gotRights = isSystemAdministrator || requestInfo.Session.User.ID.IsEquals(contentType.Organization.OwnerID) || requestInfo.Session.User.IsModerator(contentType.Organization.WorkingPrivileges);
			if (!gotRights)
				throw new AccessDeniedException();

			// update
			contentType.UpdateContentTypeInstance(requestInfo.GetBodyExpando(), "ID,SystemID,RepositoryID,EntityID,Privileges,Created,CreatedID,LastModified,LastModifiedID", xcontentType =>
			{
				xcontentType.LastModified = DateTime.Now;
				xcontentType.LastModifiedID = requestInfo.Session.User.ID;
				xcontentType.NormalizeExtras();
			});
			await Task.WhenAll(
				ContentType.UpdateAsync(contentType, requestInfo.Session.User.ID, cancellationToken),
				Utility.Cache.RemoveAsync(this.GetRelatedCacheKeys(Filters<ContentType>.And(), Sorts<ContentType>.Ascending("Title")), cancellationToken),
				Utility.Cache.RemoveAsync(this.GetRelatedCacheKeys(contentType.SystemID.GetContentTypesFilter(null), Sorts<ContentType>.Ascending("Title")), cancellationToken),
				Utility.Cache.RemoveAsync(this.GetRelatedCacheKeys(contentType.SystemID.GetContentTypesFilter(contentType.RepositoryID), Sorts<ContentType>.Ascending("Title")), cancellationToken),
				Utility.Cache.RemoveAsync(this.GetRelatedCacheKeys(contentType.SystemID.GetContentTypesFilter(null, contentType.ContentTypeDefinitionID), Sorts<ContentType>.Ascending("Title")), cancellationToken),
				Utility.Cache.RemoveAsync(this.GetRelatedCacheKeys(contentType.SystemID.GetContentTypesFilter(contentType.RepositoryID, contentType.ContentTypeDefinitionID), Sorts<ContentType>.Ascending("Title")), cancellationToken),
				contentType.SetAsync(false, cancellationToken)
			).ConfigureAwait(false);

			// send update messages
			var response = contentType.ToJson();
			var objectName = contentType.GetTypeName(true);
			await Task.WhenAll(
				this.SendUpdateMessageAsync(new UpdateMessage
				{
					Type = $"{this.ServiceName}#{objectName}#Update",
					Data = response,
					DeviceID = "*",
					ExcludedDeviceID = requestInfo.Session.DeviceID
				}, cancellationToken),
				this.SendInterCommunicateMessageAsync(new CommunicateMessage(this.ServiceName)
				{
					Type = $"{objectName}#Update",
					Data = response,
					ExcludedNodeID = this.NodeID
				}, cancellationToken)
			).ConfigureAwait(false);

			// response
			return response;
		}
		#endregion

		#region Delete a content-type
		async Task<JObject> DeleteContentTypeAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			// prepare
			var contentType = await (requestInfo.GetObjectIdentity() ?? "").GetContentTypeByIDAsync(cancellationToken).ConfigureAwait(false);
			if (contentType == null)
				throw new InformationNotFoundException();
			else if (contentType.Organization == null)
				throw new InformationInvalidException("The organization is invalid");

			// check permission
			var isSystemAdministrator = await this.IsSystemAdministratorAsync(requestInfo).ConfigureAwait(false) || await this.IsAuthorizedAsync(requestInfo, "Organization", Components.Security.Action.Approve, cancellationToken).ConfigureAwait(false);
			var gotRights = isSystemAdministrator || requestInfo.Session.User.ID.IsEquals(contentType.Organization.OwnerID) || requestInfo.Session.User.IsModerator(contentType.Organization.WorkingPrivileges);
			if (!gotRights)
				throw new AccessDeniedException();

			// TO DO: delete all business objects first
			// .......

			// delete
			await ContentType.DeleteAsync<ContentType>(contentType.ID, requestInfo.Session.User.ID, cancellationToken).ConfigureAwait(false);
			contentType.Remove();

			await Task.WhenAll(
				Utility.Cache.RemoveAsync(this.GetRelatedCacheKeys(Filters<ContentType>.And(), Sorts<ContentType>.Ascending("Title")), cancellationToken),
				Utility.Cache.RemoveAsync(this.GetRelatedCacheKeys(contentType.SystemID.GetContentTypesFilter(null), Sorts<ContentType>.Ascending("Title")), cancellationToken),
				Utility.Cache.RemoveAsync(this.GetRelatedCacheKeys(contentType.SystemID.GetContentTypesFilter(contentType.RepositoryID), Sorts<ContentType>.Ascending("Title")), cancellationToken),
				Utility.Cache.RemoveAsync(this.GetRelatedCacheKeys(contentType.SystemID.GetContentTypesFilter(null, contentType.ContentTypeDefinitionID), Sorts<ContentType>.Ascending("Title")), cancellationToken),
				Utility.Cache.RemoveAsync(this.GetRelatedCacheKeys(contentType.SystemID.GetContentTypesFilter(contentType.RepositoryID, contentType.ContentTypeDefinitionID), Sorts<ContentType>.Ascending("Title")), cancellationToken)
			).ConfigureAwait(false);

			// send update messages
			var response = contentType.ToJson();
			var objectName = contentType.GetTypeName(true);
			await Task.WhenAll(
				this.SendUpdateMessageAsync(new UpdateMessage
				{
					Type = $"{this.ServiceName}#{objectName}#Delete",
					Data = response,
					DeviceID = "*",
					ExcludedDeviceID = requestInfo.Session.DeviceID
				}, cancellationToken),
				this.SendInterCommunicateMessageAsync(new CommunicateMessage(this.ServiceName)
				{
					Type = $"{objectName}#Delete",
					Data = response,
					ExcludedNodeID = this.NodeID
				}, cancellationToken)
			).ConfigureAwait(false);

			// response
			return response;
		}
		#endregion

		#region Process communicate message of Portals service
		protected override async Task ProcessInterCommunicateMessageAsync(CommunicateMessage message, CancellationToken cancellationToken = default)
		{
			// prepare
			var correlationID = UtilityService.NewUUID;
			if (this.IsDebugLogEnabled)
				this.WriteLogs(correlationID, $"Process an inter-communicate message\r\n{message?.ToJson()}");

			var request = message.Data?.ToExpandoObject();
			if (request == null)
				return;

			// update an orgaization
			if (message.Type.IsEquals("Organization#Update"))
				await message.Data.ToExpandoObject().CreateOrganizationInstance().SetAsync(true, false, cancellationToken).ConfigureAwait(false);

			// delete an orgaization
			else if (message.Type.IsEquals("Organization#Delete"))
				message.Data.ToExpandoObject().CreateOrganizationInstance().Remove();

			// update a role
			else if (message.Type.IsEquals("Role#Update"))
				await message.Data.ToExpandoObject().CreateRoleInstance().SetAsync(false, cancellationToken).ConfigureAwait(false);

			// delete a role
			else if (message.Type.IsEquals("Role#Delete"))
				message.Data.ToExpandoObject().CreateRoleInstance().Remove();

			// update a site
			else if (message.Type.IsEquals("Site#Update"))
				await message.Data.ToExpandoObject().CreateSiteInstance().SetAsync(true, false, cancellationToken).ConfigureAwait(false);

			// delete a site
			else if (message.Type.IsEquals("Site#Delete"))
				message.Data.ToExpandoObject().CreateSiteInstance().Remove();

			// update a desktop
			else if (message.Type.IsEquals("Desktop#Update"))
				await message.Data.ToExpandoObject().CreateDesktopInstance().SetAsync(true, false, cancellationToken).ConfigureAwait(false);

			// delete a desktop
			else if (message.Type.IsEquals("Desktop#Delete"))
				message.Data.ToExpandoObject().CreateDesktopInstance().Remove();
		}
		#endregion

		#region Process communicate message of CMS Portals service
		async Task ProcessCommunicateMessageAsync(CommunicateMessage message, CancellationToken cancellationToken = default)
		{
			var correlationID = UtilityService.NewUUID;

			if (message.Type.IsEquals("Definition#RequestInfo"))
				await this.SendDefinitionInfoAsync(cancellationToken).ConfigureAwait(false);

			else if (message.Type.IsEquals("Definition#Info"))
			{
				var definition = message.Data?.ToExpandoObject()?.Copy<ModuleDefinition>();
				if (this.IsDebugLogEnabled)
					await this.WriteLogsAsync(correlationID, $"Got an update of a module definition\r\n{message.Data}", null, this.ServiceName, "CMS.Portals").ConfigureAwait(false);

				if (definition != null && !Utility.ModuleDefinitions.ContainsKey(definition.ID))
				{
					Utility.ModuleDefinitions[definition.ID] = definition;
					definition.ContentTypeDefinitions.ForEach(contentTypeDefinition =>
					{
						contentTypeDefinition.ModuleDefinition = definition;
						Utility.ContentTypeDefinitions[contentTypeDefinition.ID] = contentTypeDefinition;
					});
					if (this.IsDebugLogEnabled)
						await this.WriteLogsAsync(correlationID, $"Update the module definition into the collection of definitions\r\n{definition.ToJson()}", null, this.ServiceName, "CMS").ConfigureAwait(false);
				}
			}
		}

		Task SendDefinitionInfoAsync(CancellationToken cancellationToken = default)
			=> this.SendInterCommunicateMessageAsync(new CommunicateMessage("CMS.Portals")
			{
				Type = "Definition#Info",
				Data = this.GetDefinition().ToJson()
			}, cancellationToken);
		#endregion

	}
}
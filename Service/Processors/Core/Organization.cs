#region Related components
using System;
using System.Linq;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using System.Dynamic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using net.vieapps.Components.Utility;
using net.vieapps.Components.Repository;
using net.vieapps.Components.Security;
using net.vieapps.Services.Portals.Exceptions;
#endregion

namespace net.vieapps.Services.Portals
{
	public static class OrganizationProcessor
	{
		internal static ConcurrentDictionary<string, Organization> Organizations { get; } = new ConcurrentDictionary<string, Organization>(StringComparer.OrdinalIgnoreCase);

		internal static ConcurrentDictionary<string, Organization> OrganizationsByAlias { get; } = new ConcurrentDictionary<string, Organization>(StringComparer.OrdinalIgnoreCase);

		internal static HashSet<string> ExtraProperties { get; } = "Notifications,Instructions,Socials,Trackings,MetaTags,ScriptLibraries,Scripts,AlwaysUseHtmlSuffix,RefreshUrls,RedirectUrls,EmailSettings,WebHookSettings,HttpIndicators,FakeFilesHttpURI,FakePortalsHttpURI".ToHashSet();

		public static Organization CreateOrganizationInstance(this ExpandoObject data, string excluded = null, Action<Organization> onCompleted = null)
			=> Organization.CreateInstance(data, excluded?.ToHashSet(), organization =>
			{
				organization.Instructions = Settings.Instruction.Parse(data.Get<ExpandoObject>("Instructions"));
				organization.Alias = organization.Alias?.ToLower().Trim();
				onCompleted?.Invoke(organization);
			});

		public static Organization UpdateOrganizationInstance(this Organization organization, ExpandoObject data, string excluded = null, Action<Organization> onCompleted = null)
			=> organization.Fill(data, excluded?.ToHashSet(), _ =>
			{
				organization.Instructions = Settings.Instruction.Parse(data.Get<ExpandoObject>("Instructions"));
				organization.Alias = organization.Alias?.ToLower().Trim();
				onCompleted?.Invoke(organization);
			});

		internal static Organization Set(this Organization organization, bool clear = false, bool updateCache = false)
		{
			if (organization != null && !string.IsNullOrWhiteSpace(organization.ID) && !string.IsNullOrWhiteSpace(organization.Title))
			{
				if (clear)
					organization.Remove();

				OrganizationProcessor.Organizations[organization.ID] = organization;
				OrganizationProcessor.OrganizationsByAlias[organization.Alias] = organization;
				Utility.NotRecognizedAliases.Remove($"Organization:{organization.Alias}");

				if (updateCache)
					Utility.Cache.Set(organization);
			}
			return organization;
		}

		internal static async Task<Organization> SetAsync(this Organization organization, bool clear = false, bool updateCache = false, CancellationToken cancellationToken = default)
		{
			organization?.Set(clear);
			await (updateCache && organization != null && !string.IsNullOrWhiteSpace(organization.ID) && !string.IsNullOrWhiteSpace(organization.Title) ? Utility.Cache.SetAsync(organization, cancellationToken) : Task.CompletedTask).ConfigureAwait(false);
			return organization;
		}

		internal static Organization Remove(this Organization organization)
			=> (organization?.ID ?? "").RemoveOrganization();

		internal static Organization RemoveOrganization(this string id)
		{
			if (string.IsNullOrWhiteSpace(id) || !OrganizationProcessor.Organizations.TryRemove(id, out var organization) || organization == null)
				return null;
			OrganizationProcessor.OrganizationsByAlias.Remove(organization.Alias);
			Utility.NotRecognizedAliases.Remove($"Organization:{organization.Alias}");
			return organization;
		}

		public static Organization GetOrganizationByID(this string id, bool force = false, bool fetchRepository = true)
			=> !force && !string.IsNullOrWhiteSpace(id) && OrganizationProcessor.Organizations.ContainsKey(id)
				? OrganizationProcessor.Organizations[id]
				: fetchRepository && !string.IsNullOrWhiteSpace(id)
					? Organization.Get<Organization>(id)?.Set()
					: null;

		public static async Task<Organization> GetOrganizationByIDAsync(this string id, CancellationToken cancellationToken = default, bool force = false)
			=> (id ?? "").GetOrganizationByID(force, false) ?? (await Organization.GetAsync<Organization>(id, cancellationToken).ConfigureAwait(false))?.Set();

		public static Organization GetOrganizationByAlias(this string alias, bool fetchRepository = true)
		{
			if (string.IsNullOrWhiteSpace(alias) || Utility.NotRecognizedAliases.Contains($"Organization:{alias}"))
				return null;

			var organization = OrganizationProcessor.OrganizationsByAlias.ContainsKey(alias)
				? OrganizationProcessor.OrganizationsByAlias[alias]
				: null;

			if (organization == null && fetchRepository)
			{
				organization = Organization.Get<Organization>(Filters<Organization>.Equals("Alias", alias), null, null)?.Set();
				if (organization == null)
					Utility.NotRecognizedAliases.Add($"Organization:{alias}");
			}

			return organization;
		}

		public static async Task<Organization> GetOrganizationByAliasAsync(this string alias, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(alias) || Utility.NotRecognizedAliases.Contains($"Organization:{alias}"))
				return null;

			var organization = alias.GetOrganizationByAlias(false) ?? (await Organization.GetAsync<Organization>(Filters<Organization>.Equals("Alias", alias), null, null, cancellationToken).ConfigureAwait(false))?.Set();
			if (organization == null)
				Utility.NotRecognizedAliases.Add($"Organization:{alias}");
			return organization;
		}

		internal static async Task ProcessInterCommunicateMessageOfOrganizationAsync(this CommunicateMessage message, CancellationToken cancellationToken = default)
		{
			if (message.Type.IsEndsWith("#Create"))
			{
				var organization = message.Data.ToExpandoObject().CreateOrganizationInstance();
				organization._siteIDs = organization._moduleIDs = null;
				await Task.WhenAll
				(
					organization.FindSitesAsync(cancellationToken, false),
					organization.FindModulesAsync(cancellationToken, false)
				).ConfigureAwait(false);
				organization.Set();
			}

			else if (message.Type.IsEndsWith("#Update"))
			{
				var organization = message.Data.Get("ID", "").GetOrganizationByID(false, false);
				organization = organization == null
					? message.Data.ToExpandoObject().CreateOrganizationInstance()
					: organization.UpdateOrganizationInstance(message.Data.ToExpandoObject());
				organization._siteIDs = organization._moduleIDs = null;
				await Task.WhenAll
				(
					organization.FindSitesAsync(cancellationToken, false),
					organization.FindModulesAsync(cancellationToken, false)
				).ConfigureAwait(false);
				organization.Set();
			}

			else if (message.Type.IsEndsWith("#Delete"))
				message.Data.ToExpandoObject().CreateOrganizationInstance().Remove();
		}

		internal static async Task ClearRelatedCacheAsync(this Organization organization, CancellationToken cancellationToken, string correlationID = null, bool clearDataCache = true, bool clearHtmlCache = true, bool doRefresh = true)
		{
			// data cache keys
			var dataCacheKeys = clearDataCache
				? Extensions.GetRelatedCacheKeys(Filters<Organization>.And(), Sorts<Organization>.Ascending("Title"))
					.Concat(Extensions.GetRelatedCacheKeys(Filters<Organization>.And(Filters<Organization>.Equals("OwnerID", organization.OwnerID)), Sorts<Organization>.Ascending("Title")))
					.Distinct(StringComparer.OrdinalIgnoreCase)
					.ToList()
				: new List<string>();

			// html cache keys (desktop HTMLs)
			var htmlCacheKeys = clearHtmlCache
				? organization.GetDesktopCacheKey().Concat(new[] { $"js#o_{organization.ID}", $"js#o_{organization.ID}:time" }).ToList()
				: new List<string>();

			// clear related cache
			await Utility.Cache.RemoveAsync(htmlCacheKeys.Concat(dataCacheKeys).Distinct(StringComparer.OrdinalIgnoreCase).ToList(), cancellationToken).ConfigureAwait(false);
			await Task.WhenAll
			(
				Utility.WriteCacheLogs ? Utility.WriteLogAsync(correlationID, $"Clear related cache of an organization [{organization.Title} - ID: {organization.ID}]\r\n- {dataCacheKeys.Count} data keys => {dataCacheKeys.Join(", ")}\r\n- {htmlCacheKeys.Count} html keys => {htmlCacheKeys.Join(", ")}", cancellationToken, "Caches") : Task.CompletedTask,
				doRefresh ? $"{Utility.PortalsHttpURI}/~{organization.Alias}/".RefreshWebPageAsync(1, correlationID, $"Refresh desktop when related cache of an organization was clean [{organization.Title} - ID: {organization.ID}]") : Task.CompletedTask
			).ConfigureAwait(false);
		}

		internal static async Task ClearCacheAsync(this Organization organization, CancellationToken cancellationToken, string correlationID = null, bool clearObjectsCache = true, bool clearRelatedDataCache = true, bool clearRelatedHtmlCache = true, bool doRefresh = true)
		{
			// clear cache of home desktop (html)
			var tasks = new List<Task>
			{
				organization.ClearRelatedCacheAsync(cancellationToken, correlationID, clearRelatedDataCache, clearRelatedHtmlCache, false)
			};

			// clear cache of objects
			if (clearObjectsCache)
			{
				// clear cache of expressions
				var expressions = await Expression.FindAsync(Filters<Expression>.And(Filters<Expression>.Equals("SystemID", organization.ID)), null, 0, 1, null, cancellationToken).ConfigureAwait(false);
				tasks = tasks.Concat(expressions.Select(expression => expression.ClearCacheAsync(cancellationToken, correlationID, clearRelatedDataCache, clearRelatedHtmlCache, doRefresh))).ToList();

				// clear cache of roles
				var roles = await Role.FindAsync(Filters<Role>.And(Filters<Role>.Equals("SystemID", organization.ID)), null, 0, 1, null, cancellationToken).ConfigureAwait(false);
				tasks = tasks.Concat(roles.Select(role => role.ClearCacheAsync(cancellationToken, correlationID, clearRelatedDataCache))).ToList();

				// clear cache of modules, content-types and business objects
				tasks = tasks.Concat(organization.Modules.Select(module => module.ClearCacheAsync(cancellationToken, correlationID, clearObjectsCache, clearRelatedDataCache, clearRelatedHtmlCache, doRefresh))).ToList();

				// clear cache of desktops
				var desktops = await Desktop.FindAsync(Filters<Desktop>.And(Filters<Desktop>.Equals("SystemID", organization.ID)), null, 0, 1, null, cancellationToken).ConfigureAwait(false);
				tasks = tasks.Concat(desktops.Select(desktop => desktop.ClearCacheAsync(cancellationToken, correlationID, clearRelatedDataCache, clearRelatedHtmlCache, doRefresh))).ToList();

				// clear cache of sites
				tasks = tasks.Concat(organization.Sites.Select(site => site.ClearCacheAsync(cancellationToken, correlationID, clearRelatedDataCache, clearRelatedHtmlCache, doRefresh))).ToList();
			}

			// clear cache of the organization
			tasks = tasks.Concat(new[]
			{
				Utility.Cache.RemoveAsync(organization.Remove(), cancellationToken),
				new CommunicateMessage(ServiceBase.ServiceComponent.ServiceName)
				{
					Type = $"{organization.GetObjectName()}#Delete",
					Data = organization.ToJson(),
					ExcludedNodeID = Utility.NodeID
				}.SendAsync(),
				Utility.WriteCacheLogs ? Utility.WriteLogAsync(correlationID, $"Clear cache of an organization [{organization.Title} - ID: {organization.ID}]", cancellationToken, "Caches") : Task.CompletedTask
			}).ToList();

			await Task.WhenAll(tasks).ConfigureAwait(false);

			// re-load organization & sites/modules/content-types
			organization = await organization.ID.GetOrganizationByIDAsync(cancellationToken).ConfigureAwait(false);
			await Task.WhenAll
			(
				organization.FindSitesAsync(cancellationToken, false),
				organization.FindModulesAsync(cancellationToken, false)
			).ConfigureAwait(false);
			await organization.Modules.ForEachAsync(async module =>
			{
				await module.FindContentTypesAsync(cancellationToken, false).ConfigureAwait(false);
				await Task.WhenAll
				(
					Utility.WriteCacheLogs ? Utility.WriteLogAsync(correlationID, $"The module was reloaded when all cache were clean\r\n{module.ToJson()}", cancellationToken, "Caches") : Task.CompletedTask,
					Utility.WriteCacheLogs ? Utility.WriteLogAsync(correlationID, $"The content-types were reloaded when all cache were clean\r\n{module.ContentTypes.Select(contentType => contentType.ToJson().ToString(Formatting.Indented)).Join("\r\n")}", cancellationToken, "Caches") : Task.CompletedTask
				).ConfigureAwait(false);
			}, true, false).ConfigureAwait(false);
			await Task.WhenAll
			(
				organization.SetAsync(false, true, cancellationToken),
				Task.WhenAll(organization.Sites.Select(site => site.SetAsync(false, true, cancellationToken))),
				Task.WhenAll(organization.Modules.Select(module => module.SetAsync(true, cancellationToken))),
				Task.WhenAll(organization.Modules.Select(module => Task.WhenAll(module.ContentTypes.Select(contentType => contentType.SetAsync(true, cancellationToken)))))
			).ConfigureAwait(false);

			// re-load and refresh the home desktop
			var homedesktop = await Desktop.GetAsync<Desktop>(organization.HomeDesktopID, cancellationToken).ConfigureAwait(false);
			await Task.WhenAll
			(
				homedesktop.FindChildrenAsync(cancellationToken, false),
				homedesktop.FindPortletsAsync(cancellationToken, false)
			).ConfigureAwait(false);
			await homedesktop.SetAsync(false, true, cancellationToken).ConfigureAwait(false);

			await Task.WhenAll
			(
				$"{Utility.PortalsHttpURI}/~{organization.Alias}/".RefreshWebPageAsync(0, correlationID, $"Refresh the home desktop when all cache of an organization were clean [{organization.Title} - ID: {organization.ID}]"),
				Utility.WriteCacheLogs ? Utility.WriteLogAsync(correlationID, $"The organization was reloaded when all cache were clean\r\n{organization.ToJson()}", cancellationToken, "Caches") : Task.CompletedTask
			).ConfigureAwait(false);
		}

		internal static async Task<JObject> SearchOrganizationsAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, CancellationToken cancellationToken = default)
		{
			// check permissions
			if (!isSystemAdministrator)
				throw new AccessDeniedException();

			// prepare
			var request = requestInfo.GetRequestExpando();

			var query = request.Get<string>("FilterBy.Query");
			var filter = request.Get<ExpandoObject>("FilterBy")?.ToFilterBy<Organization>() ?? Filters<Organization>.And();
			var sort = string.IsNullOrWhiteSpace(query) ? request.Get<ExpandoObject>("SortBy")?.ToSortBy<Organization>() ?? Sorts<Organization>.Ascending("Title") : null;

			var pagination = request.Get<ExpandoObject>("Pagination")?.GetPagination() ?? new Tuple<long, int, int, int>(-1, 0, 20, 1);
			var pageSize = pagination.Item3;
			var pageNumber = pagination.Item4;

			// process cache
			var json = string.IsNullOrWhiteSpace(query) ? await Utility.Cache.GetAsync<string>(Extensions.GetCacheKeyOfObjectsJson(filter, sort, pageSize, pageNumber), cancellationToken).ConfigureAwait(false) : null;
			if (!string.IsNullOrWhiteSpace(json))
				return JObject.Parse(json);

			// prepare pagination
			var totalRecords = pagination.Item1 > -1 ? pagination.Item1 : -1;
			if (totalRecords < 0)
				totalRecords = string.IsNullOrWhiteSpace(query)
					? await Organization.CountAsync(filter, Extensions.GetCacheKeyOfTotalObjects(filter, sort), cancellationToken).ConfigureAwait(false)
					: await Organization.CountAsync(query, filter, cancellationToken).ConfigureAwait(false);

			var totalPages = new Tuple<long, int>(totalRecords, pageSize).GetTotalPages();
			if (totalPages > 0 && pageNumber > totalPages)
				pageNumber = totalPages;

			// search
			var objects = totalRecords > 0
				? string.IsNullOrWhiteSpace(query)
					? await Organization.FindAsync(filter, sort, pageSize, pageNumber, Extensions.GetCacheKey(filter, sort, pageSize, pageNumber), cancellationToken).ConfigureAwait(false)
					: await Organization.SearchAsync(query, filter, null, pageSize, pageNumber, cancellationToken).ConfigureAwait(false)
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
				await Utility.Cache.SetAsync(Extensions.GetCacheKeyOfObjectsJson(filter, sort, pageSize, pageNumber), response.ToString(Formatting.None), cancellationToken).ConfigureAwait(false);

			// response
			return response;
		}

		internal static async Task<JObject> CreateOrganizationAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, CancellationToken cancellationToken = default)
		{
			// check permission
			var isCreatedByOtherService = requestInfo.Extra != null && requestInfo.Extra.TryGetValue("x-create", out var xcreate) && xcreate.IsEquals(requestInfo.Session.SessionID.Encrypt());
			if (!isSystemAdministrator && !isCreatedByOtherService)
				throw new AccessDeniedException();

			// check the exising the the alias
			var request = requestInfo.GetBodyExpando();
			var alias = request.Get<string>("Alias");
			if (!string.IsNullOrWhiteSpace(alias))
			{
				var existing = await alias.NormalizeAlias(false).GetOrganizationByAliasAsync(cancellationToken).ConfigureAwait(false);
				if (existing != null)
					throw new AliasIsExistedException($"The alias ({alias.NormalizeAlias(false)}) is used by another organization");
			}

			// create new
			var organization = request.CreateOrganizationInstance("Status,Instructions,Privileges,OriginalPrivileges,Created,CreatedID,LastModified,LastModifiedID", obj =>
			{
				obj.ID = string.IsNullOrWhiteSpace(obj.ID) || !obj.ID.IsValidUUID() ? UtilityService.NewUUID : obj.ID;
				obj.Alias = string.IsNullOrWhiteSpace(obj.Alias) ? obj.Title.NormalizeAlias(false) + obj.ID : obj.Alias.NormalizeAlias(false);
				obj.OwnerID = string.IsNullOrWhiteSpace(obj.OwnerID) || !obj.OwnerID.IsValidUUID() ? requestInfo.Session.User.ID : obj.OwnerID;
				obj.Status = isSystemAdministrator
					? request.Get("Status", "Pending").TryToEnum(out ApprovalStatus statusByAdmin) ? statusByAdmin : ApprovalStatus.Pending
					: isCreatedByOtherService
						? requestInfo.Extra.TryGetValue("x-status", out var xstatus) && xstatus.TryToEnum(out ApprovalStatus statusByOtherService) ? statusByOtherService : ApprovalStatus.Pending
						: ApprovalStatus.Pending;
				obj.OriginalPrivileges = (isSystemAdministrator ? request.Get<Privileges>("OriginalPrivileges") : null) ?? new Privileges(true);
				obj.Created = obj.LastModified = DateTime.Now;
				obj.CreatedID = obj.LastModifiedID = requestInfo.Session.User.ID;
				obj.NormalizeExtras();
			});
			await Organization.CreateAsync(organization, cancellationToken).ConfigureAwait(false);

			// update cache
			await organization.Set().ClearRelatedCacheAsync(cancellationToken, requestInfo.CorrelationID).ConfigureAwait(false);

			// send update messages
			var response = organization.ToJson();
			var objectName = organization.GetTypeName(true);
			new UpdateMessage
			{
				Type = $"{requestInfo.ServiceName}#{objectName}#Create",
				Data = response,
				DeviceID = "*"
			}.Send();
			new CommunicateMessage(requestInfo.ServiceName)
			{
				Type = $"{objectName}#Create",
				Data = response,
				ExcludedNodeID = Utility.NodeID
			}.Send();

			// send notification
			await organization.SendNotificationAsync("Create", organization.Notifications, ApprovalStatus.Draft, organization.Status, requestInfo, cancellationToken).ConfigureAwait(false);

			// start the refresh timer
			new CommunicateMessage(requestInfo.ServiceName)
			{
				Type = "RefreshTimer#Start",
				Data = response
			}.Send();

			// response
			return response;
		}

		internal static async Task<JObject> GetOrganizationAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, CancellationToken cancellationToken = default)
		{
			// get the organization
			var identity = requestInfo.GetObjectIdentity(true, true) ?? "";
			var organization = await (identity.IsValidUUID() ? identity.GetOrganizationByIDAsync(cancellationToken) : identity.GetOrganizationByAliasAsync(cancellationToken)).ConfigureAwait(false);
			if (organization == null)
				throw new InformationNotFoundException();

			// check permission
			var gotRights = isSystemAdministrator || requestInfo.Session.User.IsViewer(null, null, organization, requestInfo.CorrelationID);
			if (!gotRights)
				throw new AccessDeniedException();

			// get site & modules
			if (organization._siteIDs == null || organization._moduleIDs == null)
			{
				if (organization._siteIDs == null)
					await organization.FindSitesAsync(cancellationToken, false).ConfigureAwait(false);

				if (organization._moduleIDs == null)
				{
					await organization.FindModulesAsync(cancellationToken).ConfigureAwait(false);
					await organization.Modules.ForEachAsync(async module => await (module._contentTypeIDs == null ? module.FindContentTypesAsync(cancellationToken) : Task.CompletedTask).ConfigureAwait(false), true, false).ConfigureAwait(false);
				}

				await organization.SetAsync(false, true, cancellationToken).ConfigureAwait(false);
			}

			// response
			return identity.IsValidUUID()
				? organization.ToJson(true, false)
				: new JObject
				{
					{ "ID", organization.ID },
					{ "Title", organization.Title },
					{ "Alias", organization.Alias }
				};
		}

		internal static async Task<JObject> UpdateAsync(this Organization organization, RequestInfo requestInfo, ApprovalStatus oldStatus, CancellationToken cancellationToken, bool clearObjectsCache = false)
		{
			// update
			await Organization.UpdateAsync(organization, requestInfo.Session.User.ID, cancellationToken).ConfigureAwait(false);

			// clear cache
			await organization.ClearCacheAsync(cancellationToken, requestInfo.CorrelationID, clearObjectsCache, true, false, false).ConfigureAwait(false);

			// send update messages
			await organization.SetAsync(false, true, cancellationToken).ConfigureAwait(false);
			var response = organization.ToJson();
			var objectName = organization.GetTypeName(true);
			new UpdateMessage
			{
				Type = $"{requestInfo.ServiceName}#{objectName}#Update",
				Data = response,
				DeviceID = "*"
			}.Send();
			new CommunicateMessage(requestInfo.ServiceName)
			{
				Type = $"{objectName}#Update",
				Data = response,
				ExcludedNodeID = Utility.NodeID
			}.Send();

			// send notification
			await organization.SendNotificationAsync("Update", organization.Notifications, oldStatus, organization.Status, requestInfo, cancellationToken).ConfigureAwait(false);

			// restart the refresh timer
			new CommunicateMessage(requestInfo.ServiceName)
			{
				Type = "RefreshTimer#Restart",
				Data = response
			}.Send();

			// response
			return response;
		}

		internal static async Task<JObject> UpdateOrganizationAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, CancellationToken cancellationToken = default)
		{
			// get the organization
			var organization = await (requestInfo.GetObjectIdentity() ?? "").GetOrganizationByIDAsync(cancellationToken).ConfigureAwait(false);
			if (organization == null)
				throw new InformationNotFoundException();

			// check permission
			var gotRights = isSystemAdministrator || requestInfo.Session.User.IsAdministrator(null, null, organization, requestInfo.CorrelationID);
			if (!gotRights)
				throw new AccessDeniedException();

			// check the exising the the alias
			var request = requestInfo.GetBodyExpando();
			var oldAlias = organization.Alias;
			var oldStatus = organization.Status;

			var alias = request.Get<string>("Alias");
			if (!string.IsNullOrWhiteSpace(alias))
			{
				var existing = await alias.NormalizeAlias(false).GetOrganizationByAliasAsync(cancellationToken).ConfigureAwait(false);
				if (existing != null && !existing.ID.Equals(organization.ID))
					throw new AliasIsExistedException($"The alias ({alias.NormalizeAlias(false)}) is used by another organization");
				organization.Remove();
			}

			// update
			var privileges = organization.OriginalPrivileges?.Copy();
			organization.UpdateOrganizationInstance(request, "ID,OwnerID,Status,Instructions,Privileges,Created,CreatedID,LastModified,LastModifiedID", obj =>
			{
				obj.OwnerID = isSystemAdministrator ? request.Get("OwnerID", organization.OwnerID) : organization.OwnerID;
				obj.Status = isSystemAdministrator ? request.Get("Status", organization.Status.ToString()).ToEnum<ApprovalStatus>() : organization.Status;
				obj.Alias = string.IsNullOrWhiteSpace(obj.Alias) ? oldAlias : obj.Alias.NormalizeAlias(false);
				obj.OriginalPrivileges = organization.OriginalPrivileges ?? new Privileges(true);
				obj.LastModified = DateTime.Now;
				obj.LastModifiedID = requestInfo.Session.User.ID;
				obj.NormalizeExtras();
			});
			var clearObjectsCache = !organization.OriginalPrivileges.IsEquals(privileges);
			var response = await organization.UpdateAsync(requestInfo, oldStatus, cancellationToken, clearObjectsCache).ConfigureAwait(false);

			// broadcast update when the privileges were changed
			// ...

			return response;
		}

		internal static Task<JObject> DeleteOrganizationAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, CancellationToken cancellationToken = default)
			=> Task.FromException<JObject>(new MethodNotAllowedException(requestInfo.Verb));

		internal static async Task<JObject> SyncOrganizationAsync(this RequestInfo requestInfo, CancellationToken cancellationToken, bool sendNotifications = false)
		{
			var @event = requestInfo.GetHeaderParameter("Event");
			if (string.IsNullOrWhiteSpace(@event) || !@event.IsEquals("Delete"))
				@event = "Update";

			var data = requestInfo.GetBodyExpando();
			var organization = await data.Get<string>("ID").GetOrganizationByIDAsync(cancellationToken).ConfigureAwait(false);
			var oldStatus = organization != null ? organization.Status : ApprovalStatus.Pending;

			if (!@event.IsEquals("Delete"))
			{
				if (organization == null)
				{
					organization = Organization.CreateInstance(data);
					organization.NormalizeExtras();
					organization.Extras = data.Get<string>("Extras") ?? organization.Extras;
					await Organization.CreateAsync(organization, cancellationToken).ConfigureAwait(false);
				}
				else
				{
					organization.Fill(data);
					organization.NormalizeExtras();
					organization.Extras = data.Get<string>("Extras") ?? organization.Extras;
					await Organization.UpdateAsync(organization, true, cancellationToken).ConfigureAwait(false);
				}
			}
			else if (organization != null)
				await Organization.DeleteAsync<Organization>(organization.ID, organization.LastModifiedID, cancellationToken).ConfigureAwait(false);

			// clear related cache
			if (requestInfo.GetHeaderParameter("x-converter") == null || @event.IsEquals("Delete"))
				await organization.ClearCacheAsync(cancellationToken, requestInfo.CorrelationID).ConfigureAwait(false);
			else
				await organization.ClearRelatedCacheAsync(cancellationToken, requestInfo.CorrelationID).ConfigureAwait(false);

			// send notifications
			if (sendNotifications)
				await organization.SendNotificationAsync(@event, organization.Notifications, oldStatus, organization.Status, requestInfo, cancellationToken).ConfigureAwait(false);

			// send update messages
			var json = @event.IsEquals("Delete")
				? organization.Remove().ToJson()
				: organization.Set().ToJson();
			var objectName = organization.GetTypeName(true);
			new UpdateMessage
			{
				Type = $"{requestInfo.ServiceName}#{objectName}#{@event}",
				Data = json,
				DeviceID = "*"
			}.Send();
			new CommunicateMessage(requestInfo.ServiceName)
			{
				Type = $"{objectName}#{@event}",
				Data = json,
				ExcludedNodeID = Utility.NodeID
			}.Send();

			// return the response
			return new JObject
			{
				{ "ID", organization.ID },
				{ "Type", objectName }
			};
		}
	}
}
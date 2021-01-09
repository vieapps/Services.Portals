#region Related components
using System;
using System.Linq;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using System.Dynamic;
using Microsoft.Extensions.Logging;
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

		internal static HashSet<string> ExtraProperties { get; } = "Notifications,Instructions,Socials,Trackings,MetaTags,ScriptLibraries,Scripts,AlwaysUseHtmlSuffix,RefreshUrls,RedirectUrls,EmailSettings,HttpIndicators".ToHashSet();

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
					Utility.Cache.SetAsync(organization).Run();
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
				await Task.WhenAll(
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
				await Task.WhenAll(
					organization.FindSitesAsync(cancellationToken, false),
					organization.FindModulesAsync(cancellationToken, false)
				).ConfigureAwait(false);
				organization.Set();
			}

			else if (message.Type.IsEndsWith("#Delete"))
				message.Data.ToExpandoObject().CreateOrganizationInstance().Remove();
		}

		internal static async Task ClearRelatedCacheAsync(this Organization organization, CancellationToken cancellationToken, string correlationID = null)
		{
			// data cache keys
			var dataCacheKeys = Extensions.GetRelatedCacheKeys(Filters<Organization>.And(), Sorts<Organization>.Ascending("Title"))
				.Concat(Extensions.GetRelatedCacheKeys(Filters<Organization>.And(Filters<Organization>.Equals("OwnerID", organization.OwnerID)), Sorts<Organization>.Ascending("Title")))
				.Distinct(StringComparer.OrdinalIgnoreCase)				
				.ToList();

			// html cache keys (desktop HTMLs)
			var htmlCacheKeys = organization.GetDesktopCacheKey().Concat(new[] { $"css#o_{organization.ID}", $"css#o_{organization.ID}:time", $"js#o_{organization.ID}", $"js#o_{organization.ID}:time" }).ToList();

			// clear related cache
			await Utility.Cache.RemoveAsync(htmlCacheKeys.Concat(dataCacheKeys).Distinct(StringComparer.OrdinalIgnoreCase).ToList(), cancellationToken).ConfigureAwait(false);
			await Task.WhenAll
			(
				Utility.WriteCacheLogs ? Utility.WriteLogAsync(correlationID, $"Clear related cache of a organization [{organization.Title} - ID: {organization.ID}]\r\n- {dataCacheKeys.Count} data keys => {dataCacheKeys.Join(", ")}\r\n- {htmlCacheKeys.Count} html keys => {htmlCacheKeys.Join(", ")}", CancellationToken.None, "Caches") : Task.CompletedTask,
				$"{Utility.PortalsHttpURI}/~{organization.Alias}/".RefreshWebPageAsync(1, correlationID, $"Refresh desktop when related cache of a organization was clean [{organization.Title} - ID: {organization.ID}]")
			).ConfigureAwait(false);
		}

		internal static Task ClearRelatedCacheAsync(this Organization organization, string correlationID = null)
			=> organization.ClearRelatedCacheAsync(CancellationToken.None, correlationID);

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
				await Utility.Cache.SetAsync(Extensions.GetCacheKeyOfObjectsJson(filter, sort, pageSize, pageNumber), response.ToString(Formatting.None), Utility.Cache.ExpirationTime / 2).ConfigureAwait(false);

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
			organization.Set().ClearRelatedCacheAsync(requestInfo.CorrelationID).Run();

			// send update messages
			var response = organization.ToJson();
			var objectName = organization.GetTypeName(true);
			await Task.WhenAll(
				Utility.RTUService.SendUpdateMessageAsync(new UpdateMessage
				{
					Type = $"{requestInfo.ServiceName}#{objectName}#Create",
					Data = response,
					DeviceID = "*"
				}, cancellationToken),
				Utility.RTUService.SendInterCommunicateMessageAsync(new CommunicateMessage(requestInfo.ServiceName)
				{
					Type = $"{objectName}#Create",
					Data = response,
					ExcludedNodeID = Utility.NodeID
				}, cancellationToken)
			).ConfigureAwait(false);

			// send notification
			organization.SendNotificationAsync("Create", organization.Notifications, ApprovalStatus.Draft, organization.Status, requestInfo, cancellationToken).Run();

			// start the refresh timer
			await Utility.RTUService.SendInterCommunicateMessageAsync(new CommunicateMessage(requestInfo.ServiceName)
			{
				Type = "RefreshTimer#Start",
				Data = response
			}, cancellationToken).ConfigureAwait(false);

			// response
			return response;
		}

		internal static async Task<JObject> GetOrganizationAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, CancellationToken cancellationToken = default)
		{
			// get the organization
			var identity = requestInfo.GetObjectIdentity() ?? "";
			var organization = await (identity.IsValidUUID() ? identity.GetOrganizationByIDAsync(cancellationToken) : identity.GetOrganizationByAliasAsync(cancellationToken)).ConfigureAwait(false);
			if (organization == null)
				throw new InformationNotFoundException();

			// check permission
			var gotRights = isSystemAdministrator || requestInfo.Session.User.ID.IsEquals(organization.OwnerID) || requestInfo.Session.User.IsViewer(organization.WorkingPrivileges);
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
					await organization.Modules.ForEachAsync(async (module, token) => await (module._contentTypeIDs == null ? module.FindContentTypesAsync(token) : Task.CompletedTask).ConfigureAwait(false), cancellationToken, true, false).ConfigureAwait(false);
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

		internal static async Task<JObject> UpdateOrganizationAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, CancellationToken cancellationToken = default)
		{
			// get the organization
			var organization = await (requestInfo.GetObjectIdentity() ?? "").GetOrganizationByIDAsync(cancellationToken).ConfigureAwait(false);
			if (organization == null)
				throw new InformationNotFoundException();

			// check permission
			var gotRights = isSystemAdministrator || requestInfo.Session.User.ID.IsEquals(organization.OwnerID) || requestInfo.Session.User.IsAdministrator(organization.WorkingPrivileges);
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
			organization.UpdateOrganizationInstance(request, "ID,OwnerID,Status,Instructions,Privileges,Created,CreatedID,LastModified,LastModifiedID", obj =>
			{
				obj.OwnerID = isSystemAdministrator ? request.Get("OwnerID", organization.OwnerID) : organization.OwnerID;
				obj.Alias = string.IsNullOrWhiteSpace(obj.Alias) ? oldAlias : obj.Alias.NormalizeAlias(false);
				obj.OriginalPrivileges = organization.OriginalPrivileges ?? new Privileges(true);
				obj.LastModified = DateTime.Now;
				obj.LastModifiedID = requestInfo.Session.User.ID;
				obj.NormalizeExtras();
			});
			await Organization.UpdateAsync(organization, requestInfo.Session.User.ID, cancellationToken).ConfigureAwait(false);

			// update cache
			organization.Set().ClearRelatedCacheAsync(requestInfo.CorrelationID).Run();

			// send update messages
			var response = organization.ToJson();
			var objectName = organization.GetTypeName(true);
			await Task.WhenAll(
				Utility.RTUService.SendUpdateMessageAsync(new UpdateMessage
				{
					Type = $"{requestInfo.ServiceName}#{objectName}#Update",
					Data = response,
					DeviceID = "*"
				}, cancellationToken),
				Utility.RTUService.SendInterCommunicateMessageAsync(new CommunicateMessage(requestInfo.ServiceName)
				{
					Type = $"{objectName}#Update",
					Data = response,
					ExcludedNodeID = Utility.NodeID
				}, cancellationToken)
			).ConfigureAwait(false);

			// send notification
			organization.SendNotificationAsync("Update", organization.Notifications, oldStatus, organization.Status, requestInfo, cancellationToken).Run();

			// restart the refresh timer
			await Utility.RTUService.SendInterCommunicateMessageAsync(new CommunicateMessage(requestInfo.ServiceName)
			{
				Type = "RefreshTimer#Restart",
				Data = response
			}, cancellationToken).ConfigureAwait(false);

			// response
			return response;
		}

		internal static Task<JObject> DeleteOrganizationAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, CancellationToken cancellationToken = default)
			=> Task.FromException<JObject>(new MethodNotAllowedException(requestInfo.Verb));

		internal static async Task<JObject> SyncOrganizationAsync(this RequestInfo requestInfo, CancellationToken cancellationToken = default)
		{
			var data = requestInfo.GetBodyExpando();
			var organization = await data.Get<string>("ID").GetOrganizationByIDAsync(cancellationToken).ConfigureAwait(false);
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

			// clear related cache
			if (requestInfo.GetHeaderParameter("x-converter") == null)
				organization.ClearRelatedCacheAsync(requestInfo.CorrelationID).Run();

			// send update messages
			var json = organization.Set().ToJson();
			var objectName = organization.GetTypeName(true);
			await Task.WhenAll(
				Utility.RTUService.SendUpdateMessageAsync(new UpdateMessage
				{
					Type = $"{requestInfo.ServiceName}#{objectName}#Update",
					Data = json,
					DeviceID = "*"
				}, cancellationToken),
				Utility.RTUService.SendInterCommunicateMessageAsync(new CommunicateMessage(requestInfo.ServiceName)
				{
					Type = $"{objectName}#Update",
					Data = json,
					ExcludedNodeID = Utility.NodeID
				}, cancellationToken)
			).ConfigureAwait(false);

			// return the response
			return new JObject
			{
				{ "Sync", "Success" },
				{ "ID", organization.ID },
				{ "Type", objectName }
			};
		}
	}
}
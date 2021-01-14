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
using net.vieapps.Components.Security;
using net.vieapps.Components.Repository;
#endregion

namespace net.vieapps.Services.Portals
{
	public static class ModuleProcessor
	{
		internal static ConcurrentDictionary<string, Module> Modules { get; } = new ConcurrentDictionary<string, Module>(StringComparer.OrdinalIgnoreCase);

		internal static HashSet<string> ExtraProperties { get; } = "Notifications,Trackings,EmailSettings".ToHashSet();

		public static Module CreateModuleInstance(this ExpandoObject data, string excluded = null, Action<Module> onCompleted = null)
			=> Module.CreateInstance(data, excluded?.ToHashSet(), onCompleted);

		public static Module UpdateModuleInstance(this Module module, ExpandoObject data, string excluded = null, Action<Module> onCompleted = null)
			=> module.Fill(data, excluded?.ToHashSet(), onCompleted);

		internal static Module Set(this Module module, bool updateCache = false)
		{
			if (module != null && !string.IsNullOrWhiteSpace(module.ID) && !string.IsNullOrWhiteSpace(module.Title))
			{
				ModuleProcessor.Modules[module.ID] = module;
				if (updateCache)
					Utility.Cache.SetAsync(module).Run();
				var definition = module.RepositoryDefinition;
				if (definition != null)
					definition.Register(module);
			}
			return module;
		}

		internal static async Task<Module> SetAsync(this Module module, bool updateCache = false, CancellationToken cancellationToken = default)
		{
			module?.Set();
			await (updateCache && module != null && !string.IsNullOrWhiteSpace(module.ID) && !string.IsNullOrWhiteSpace(module.Title) ? Utility.Cache.SetAsync(module, cancellationToken) : Task.CompletedTask).ConfigureAwait(false);
			return module;
		}

		internal static Module Remove(this Module module)
			=> (module?.ID ?? "").RemoveModule();

		internal static Module RemoveModule(this string id)
		{
			if (!string.IsNullOrWhiteSpace(id) && ModuleProcessor.Modules.TryRemove(id, out var module) && module != null)
			{
				module.RepositoryDefinition?.Unregister(module);
				return module;
			}
			return null;
		}

		public static Module GetModuleByID(this string id, bool force = false, bool fetchRepository = true)
			=> !force && !string.IsNullOrWhiteSpace(id) && ModuleProcessor.Modules.ContainsKey(id)
				? ModuleProcessor.Modules[id]
				: fetchRepository && !string.IsNullOrWhiteSpace(id)
					? Module.Get<Module>(id)?.Set()
					: null;

		public static async Task<Module> GetModuleByIDAsync(this string id, CancellationToken cancellationToken = default, bool force = false)
			=> (id ?? "").GetModuleByID(force, false) ?? (await Module.GetAsync<Module>(id, cancellationToken).ConfigureAwait(false))?.Set();

		public static IFilterBy<Module> GetModulesFilter(string systemID, string definitionID = null)
		{
			var filter = Filters<Module>.And(Filters<Module>.Equals("SystemID", systemID));
			if (!string.IsNullOrWhiteSpace(definitionID))
				filter.Add(Filters<Module>.Equals("DefinitionID", definitionID));
			return filter;
		}

		public static List<Module> FindModules(this string systemID, string definitionID = null, bool updateCache = true)
		{
			if (string.IsNullOrWhiteSpace(systemID))
				return new List<Module>();

			var filter = ModuleProcessor.GetModulesFilter(systemID, definitionID);
			var sort = Sorts<Module>.Ascending("Title");
			var modules = Module.Find(filter, sort, 0, 1, Extensions.GetCacheKey(filter, sort, 0, 1));
			modules.ForEach(module =>
			{
				if (module.ID.GetModuleByID(false, false) == null)
					module.Set(updateCache);
			});

			return modules;
		}

		public static async Task<List<Module>> FindModulesAsync(this string systemID, string definitionID = null, CancellationToken cancellationToken = default, bool updateCache = true)
		{
			if (string.IsNullOrWhiteSpace(systemID))
				return new List<Module>();

			var filter = ModuleProcessor.GetModulesFilter(systemID, definitionID);
			var sort = Sorts<Module>.Ascending("Title");
			var modules = await Module.FindAsync(filter, sort, 0, 1, Extensions.GetCacheKey(filter, sort, 0, 1), cancellationToken).ConfigureAwait(false);
			modules.ForEach(module =>
			{
				if (module.ID.GetModuleByID(false, false) == null)
					module.Set(updateCache);
			});

			return modules;
		}

		internal static async Task ProcessInterCommunicateMessageOfModuleAsync(this CommunicateMessage message, CancellationToken cancellationToken = default)
		{
			if (message.Type.IsEndsWith("#Create"))
			{
				var module = message.Data.ToExpandoObject().CreateModuleInstance();
				module._contentTypeIDs = null;
				await module.FindContentTypesAsync(cancellationToken, false).ConfigureAwait(false);
				module.Set();
			}

			else if (message.Type.IsEndsWith("#Update"))
			{
				var module = message.Data.Get("ID", "").GetModuleByID(false, false);
				module = module == null
					? message.Data.ToExpandoObject().CreateModuleInstance()
					: module.UpdateModuleInstance(message.Data.ToExpandoObject());
				module._contentTypeIDs = null;
				await module.FindContentTypesAsync(cancellationToken, false).ConfigureAwait(false);
				module.Set();
			}

			else if (message.Type.IsEndsWith("#Delete"))
				message.Data.ToExpandoObject().CreateModuleInstance().Remove();
		}

		internal static async Task ClearRelatedCacheAsync(this Module module, CancellationToken cancellationToken = default, string correlationID = null, bool clearHtmlCacheKeys = true, bool doRefresh = true)
		{
			// data cache keys
			var sort = Sorts<Module>.Ascending("Title");
			var dataCacheKeys = Extensions.GetRelatedCacheKeys(Filters<Module>.And(), sort)
				.Concat(Extensions.GetRelatedCacheKeys(ModuleProcessor.GetModulesFilter(module.SystemID), sort))
				.Concat(Extensions.GetRelatedCacheKeys(ModuleProcessor.GetModulesFilter(module.SystemID, module.ModuleDefinitionID), sort))
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.ToList();

			// html cache keys (desktop HTMLs)
			var htmlCacheKeys = new List<string>();

			if (clearHtmlCacheKeys)
			{
				htmlCacheKeys = new[] { module.Desktop?.GetSetCacheKey() }.Concat(module.Organization?.GetDesktopCacheKey() ?? new List<string>()).ToList();
				var desktopSetCacheKeys = new List<string>();
				await module.ContentTypes.ForEachAsync(async (contentType, _) =>
				{
					desktopSetCacheKeys = desktopSetCacheKeys.Concat(await contentType.GetSetCacheKeysAsync(cancellationToken).ConfigureAwait(false) ?? new List<string>()).ToList();
				}, cancellationToken, true, false).ConfigureAwait(false);
				await desktopSetCacheKeys.Where(id => !string.IsNullOrWhiteSpace(id))
					.Distinct(StringComparer.OrdinalIgnoreCase)
					.ToList()
					.ForEachAsync(async (desktopSetCacheKey, _) =>
					{
						var cacheKeys = await Utility.Cache.GetSetMembersAsync(desktopSetCacheKey, cancellationToken).ConfigureAwait(false);
						if (cacheKeys != null && cacheKeys.Count > 0)
							htmlCacheKeys = htmlCacheKeys.Concat(cacheKeys).Concat(new[] { desktopSetCacheKey }).ToList();
					}, cancellationToken, true, false).ConfigureAwait(false);
			}

			htmlCacheKeys = htmlCacheKeys.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

			// clear related cache
			await Utility.Cache.RemoveAsync(htmlCacheKeys.Concat(dataCacheKeys).Distinct(StringComparer.OrdinalIgnoreCase).ToList(), cancellationToken).ConfigureAwait(false);
			await Task.WhenAll
			(
				Utility.WriteCacheLogs ? Utility.WriteLogAsync(correlationID, $"Clear related cache of a module [{module.Title} - ID: {module.ID}]\r\n- {dataCacheKeys.Count} data keys => {dataCacheKeys.Join(", ")}\r\n- {htmlCacheKeys.Count} html keys => {htmlCacheKeys.Join(", ")}", CancellationToken.None, "Caches") : Task.CompletedTask,
				doRefresh ? $"{Utility.PortalsHttpURI}/~{module.Organization.Alias}/".RefreshWebPageAsync(1, correlationID, $"Refresh desktop when related cache of a module was clean [{module.Title} - ID: {module.ID}]") : Task.CompletedTask
			).ConfigureAwait(false);
		}

		internal static Task ClearRelatedCacheAsync(this Module module, string correlationID = null)
			=> module.ClearRelatedCacheAsync(CancellationToken.None, correlationID);

		internal static List<Task> ClearRelatedCacheAsync(this Module module, RequestInfo requestInfo, CancellationToken cancellationToken, bool clearHtmlCacheKeys = false)
		{
			module.Remove();
			return module.ContentTypes.Select(contentType => contentType.ClearRelatedCacheAsync(requestInfo, cancellationToken, clearHtmlCacheKeys)).SelectMany(tasks => tasks).Concat(new[]
			{
				module.ClearRelatedCacheAsync(cancellationToken, requestInfo.CorrelationID, clearHtmlCacheKeys, false),
				Utility.Cache.RemoveAsync(module, cancellationToken),
				Utility.RTUService.SendInterCommunicateMessageAsync(new CommunicateMessage(requestInfo.ServiceName)
				{
					Type = $"{module.GetObjectName()}#Delete",
					Data = module.ToJson(),
					ExcludedNodeID = Utility.NodeID
				}, cancellationToken)
			}).ToList();
		}

		internal static async Task<JObject> SearchModulesAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, CancellationToken cancellationToken = default)
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
			var gotRights = isSystemAdministrator;
			if (!gotRights)
			{
				// get organization
				var organizationID = filter.GetValue("SystemID") ?? requestInfo.GetParameter("x-system-id") ?? requestInfo.GetParameter("SystemID");
				var organization = await (organizationID ?? "").GetOrganizationByIDAsync(cancellationToken).ConfigureAwait(false);
				if (organization == null)
					throw new InformationExistedException("The organization is invalid");

				gotRights = "true".IsEquals(requestInfo.GetHeaderParameter("x-init")) || requestInfo.Session.User.ID.IsEquals(organization.OwnerID) || requestInfo.Session.User.IsModerator(organization.WorkingPrivileges);
				if (!gotRights)
					throw new AccessDeniedException();
			}

			// process cache
			var json = string.IsNullOrWhiteSpace(query) ? await Utility.Cache.GetAsync<string>(Extensions.GetCacheKeyOfObjectsJson(filter, sort, pageSize, pageNumber), cancellationToken).ConfigureAwait(false) : null;
			if (!string.IsNullOrWhiteSpace(json))
				return JObject.Parse(json);

			// prepare pagination
			var totalRecords = pagination.Item1 > -1 ? pagination.Item1 : -1;
			if (totalRecords < 0)
				totalRecords = string.IsNullOrWhiteSpace(query)
					? await Module.CountAsync(filter, Extensions.GetCacheKeyOfTotalObjects(filter, sort), cancellationToken).ConfigureAwait(false)
					: await Module.CountAsync(query, filter, cancellationToken).ConfigureAwait(false);

			var totalPages = new Tuple<long, int>(totalRecords, pageSize).GetTotalPages();
			if (totalPages > 0 && pageNumber > totalPages)
				pageNumber = totalPages;

			// search
			var objects = totalRecords > 0
				? string.IsNullOrWhiteSpace(query)
					? await Module.FindAsync(filter, sort, pageSize, pageNumber, Extensions.GetCacheKey(filter, sort, pageSize, pageNumber), cancellationToken).ConfigureAwait(false)
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
				await Utility.Cache.SetAsync(Extensions.GetCacheKeyOfObjectsJson(filter, sort, pageSize, pageNumber), response.ToString(Formatting.None), cancellationToken).ConfigureAwait(false);

			// response
			return response;
		}

		internal static async Task<JObject> CreateModuleAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, CancellationToken cancellationToken = default)
		{
			// prepare
			var requestBody = requestInfo.GetBodyExpando();
			var organizationID = requestBody.Get<string>("SystemID") ?? requestInfo.GetParameter("x-system-id") ?? requestInfo.GetParameter("SystemID");
			var organization = await (organizationID ?? "").GetOrganizationByIDAsync(cancellationToken).ConfigureAwait(false);
			if (organization == null)
				throw new InformationInvalidException("The organization is invalid");

			// check permission
			var gotRights = isSystemAdministrator || requestInfo.Session.User.ID.IsEquals(organization.OwnerID) || requestInfo.Session.User.IsModerator(organization.WorkingPrivileges);
			if (!gotRights)
				throw new AccessDeniedException();

			// create new module
			var module = requestBody.CreateModuleInstance("SystemID,Privileges,Created,CreatedID,LastModified,LastModifiedID", obj =>
			{
				obj.ID = string.IsNullOrWhiteSpace(obj.ID) || !obj.ID.IsValidUUID() ? UtilityService.NewUUID : obj.ID;
				obj.SystemID = organization.ID;
				obj.Created = obj.LastModified = DateTime.Now;
				obj.CreatedID = obj.LastModifiedID = requestInfo.Session.User.ID;
				obj.NormalizeExtras();
			});
			await Task.WhenAll(
				Module.CreateAsync(module, cancellationToken),
				module.SetAsync(false, cancellationToken)
			).ConfigureAwait(false);
			module.ClearRelatedCacheAsync(requestInfo.CorrelationID).Run();

			// create new content-types
			var contentTypeJson = new JObject
			{
				{ "SystemID", module.SystemID },
				{ "RepositoryID", module.ID }
			};
			module._contentTypeIDs = new List<string>();
			await Utility.ModuleDefinitions[module.ModuleDefinitionID].ContentTypeDefinitions.ForEachAsync(async (contentTypeDefinition, token) =>
			{
				contentTypeJson["ContentTypeDefinitionID"] = contentTypeDefinition.ID;
				contentTypeJson["Title"] = contentTypeDefinition.Title;
				contentTypeJson["Description"] = contentTypeDefinition.Description;
				await contentTypeJson.CreateContentTypeAsync(organization.ID, requestInfo.Session.User.ID, null, requestInfo.ServiceName, token, requestInfo.CorrelationID).ConfigureAwait(false);
			}, cancellationToken, true, false).ConfigureAwait(false);

			// update instance/cache of organization
			if (organization._moduleIDs == null)
				await organization.FindModulesAsync(cancellationToken).ConfigureAwait(false);
			else
			{
				organization._moduleIDs.Add(module.ID);
				await organization.SetAsync(false, true, cancellationToken).ConfigureAwait(false);
			}

			// send update messages
			var response = module.ToJson(true, false);
			var objectName = module.GetTypeName(true);
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
			module.SendNotificationAsync("Create", module.Organization.Notifications, ApprovalStatus.Published, ApprovalStatus.Published, requestInfo, cancellationToken).Run();

			// response
			return response;
		}

		internal static async Task<JObject> GetModuleAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, CancellationToken cancellationToken = default)
		{
			// prepare
			var module = await (requestInfo.GetObjectIdentity() ?? "").GetModuleByIDAsync(cancellationToken).ConfigureAwait(false);
			if (module == null)
				throw new InformationNotFoundException();
			else if (module.Organization == null)
				throw new InformationInvalidException("The organization is invalid");

			// check permission
			var gotRights = isSystemAdministrator || requestInfo.Session.User.ID.IsEquals(module.Organization.OwnerID) || requestInfo.Session.User.IsViewer(module.Organization.WorkingPrivileges);
			if (!gotRights)
				throw new AccessDeniedException();

			// get content-types
			if (module._contentTypeIDs == null)
			{
				await module.FindContentTypesAsync(cancellationToken).ConfigureAwait(false);
				await module.SetAsync(true, cancellationToken).ConfigureAwait(false);
			}

			// send the update message to update to all other connected clients
			var response = module.ToJson(true, false);
			await Utility.RTUService.SendUpdateMessageAsync(new UpdateMessage
			{
				Type = $"{requestInfo.ServiceName}#{module.GetTypeName(true)}#Update",
				Data = response,
				DeviceID = "*"
			}, cancellationToken).ConfigureAwait(false);

			// response
			return response;
		}

		internal static async Task<JObject> UpdateModuleAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, CancellationToken cancellationToken = default)
		{
			// prepare
			var module = await (requestInfo.GetObjectIdentity() ?? "").GetModuleByIDAsync(cancellationToken).ConfigureAwait(false);
			if (module == null)
				throw new InformationNotFoundException();
			else if (module.Organization == null)
				throw new InformationInvalidException("The organization is invalid");

			// check permission
			var gotRights = isSystemAdministrator || requestInfo.Session.User.ID.IsEquals(module.Organization.OwnerID) || requestInfo.Session.User.IsModerator(module.Organization.WorkingPrivileges);
			if (!gotRights)
				throw new AccessDeniedException();

			// update
			module.UpdateModuleInstance(requestInfo.GetBodyExpando(), "ID,SystemID,Privileges,Created,CreatedID,LastModified,LastModifiedID", obj =>
			{
				obj.LastModified = DateTime.Now;
				obj.LastModifiedID = requestInfo.Session.User.ID;
				obj.NormalizeExtras();
			});
			await Task.WhenAll(
				Module.UpdateAsync(module, requestInfo.Session.User.ID, cancellationToken),
				module.SetAsync(false, cancellationToken)
			).ConfigureAwait(false);
			module.ClearRelatedCacheAsync(requestInfo.CorrelationID).Run();

			// send update messages
			var response = module.ToJson();
			var objectName = module.GetTypeName(true);
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
			module.SendNotificationAsync("Update", module.Organization.Notifications, ApprovalStatus.Published, ApprovalStatus.Published, requestInfo, cancellationToken).Run();

			// response
			return response;
		}

		internal static async Task<JObject> DeleteModuleAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, CancellationToken cancellationToken = default)
		{
			// prepare
			var module = await (requestInfo.GetObjectIdentity() ?? "").GetModuleByIDAsync(cancellationToken).ConfigureAwait(false);
			if (module == null)
				throw new InformationNotFoundException();
			else if (module.Organization == null)
				throw new InformationInvalidException("The organization is invalid");

			// check permission
			var gotRights = isSystemAdministrator || requestInfo.Session.User.ID.IsEquals(module.Organization.OwnerID) || requestInfo.Session.User.IsModerator(module.Organization.WorkingPrivileges);
			if (!gotRights)
				throw new AccessDeniedException();

			// TO DO: delete all content-types (and business objects) first
			// .......

			// delete
			await Module.DeleteAsync<Module>(module.ID, requestInfo.Session.User.ID, cancellationToken).ConfigureAwait(false);
			await module.Remove().ClearRelatedCacheAsync(cancellationToken, requestInfo.CorrelationID).ConfigureAwait(false);

			// update instance/cache of organization
			if (module.Organization._moduleIDs != null)
			{
				module.Organization._moduleIDs.Remove(module.ID);
				await module.Organization.SetAsync(false, true, cancellationToken).ConfigureAwait(false);
			}

			// send update messages
			var response = module.ToJson();
			var objectName = module.GetTypeName(true);
			await Task.WhenAll(
				Utility.RTUService.SendUpdateMessageAsync(new UpdateMessage
				{
					Type = $"{requestInfo.ServiceName}#{objectName}#Delete",
					Data = response,
					DeviceID = "*"
				}, cancellationToken),
				Utility.RTUService.SendInterCommunicateMessageAsync(new CommunicateMessage(requestInfo.ServiceName)
				{
					Type = $"{objectName}#Delete",
					Data = response,
					ExcludedNodeID = Utility.NodeID
				}, cancellationToken)
			).ConfigureAwait(false);

			// send notification
			module.SendNotificationAsync("Delete", module.Organization.Notifications, ApprovalStatus.Published, ApprovalStatus.Published, requestInfo, cancellationToken).Run();

			// response
			return response;
		}

		internal static async Task<JObject> SyncModuleAsync(this RequestInfo requestInfo, CancellationToken cancellationToken = default)
		{
			var data = requestInfo.GetBodyExpando();
			var module = await data.Get<string>("ID").GetModuleByIDAsync(cancellationToken).ConfigureAwait(false);
			if (module == null)
			{
				module = Module.CreateInstance(data);
				module.NormalizeExtras();
				module.Extras = data.Get<string>("Extras") ?? module.Extras;
				await Module.CreateAsync(module, cancellationToken).ConfigureAwait(false);
			}
			else
			{
				module.Fill(data);
				module.NormalizeExtras();
				module.Extras = data.Get<string>("Extras") ?? module.Extras;
				await Module.UpdateAsync(module, true, cancellationToken).ConfigureAwait(false);
			}

			// clear related cache
			if (requestInfo.GetHeaderParameter("x-converter") == null)
				module.ClearRelatedCacheAsync(requestInfo.CorrelationID).Run();

			// send update messages
			var json = module.Set().ToJson();
			var objectName = module.GetTypeName(true);
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
				{ "ID", module.ID },
				{ "Type", objectName }
			};
		}
	}
}
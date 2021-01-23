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
using net.vieapps.Components.Security;
using net.vieapps.Components.Repository;
#endregion

namespace net.vieapps.Services.Portals
{
	public static class ContentTypeProcessor
	{
		internal static ConcurrentDictionary<string, ContentType> ContentTypes { get; } = new ConcurrentDictionary<string, ContentType>(StringComparer.OrdinalIgnoreCase);

		public static ContentType CreateContentTypeInstance(this ExpandoObject data, string excluded = null, Action<ContentType> onCompleted = null)
			=> ContentType.CreateInstance(data, excluded?.ToHashSet(), onCompleted);

		public static ContentType UpdateContentTypeInstance(this ContentType contentType, ExpandoObject data, string excluded = null, Action<ContentType> onCompleted = null)
			=> contentType?.Fill(data, excluded?.ToHashSet(), onCompleted);

		internal static ContentType Set(this ContentType contentType, bool updateCache = false)
		{
			if (contentType != null && !string.IsNullOrWhiteSpace(contentType.ID) && !string.IsNullOrWhiteSpace(contentType.Title))
			{
				ContentTypeProcessor.ContentTypes[contentType.ID] = contentType;
				contentType.EntityDefinition?.Register(contentType);
				if (updateCache)
					Utility.Cache.SetAsync(contentType).Run();
			}
			return contentType;
		}

		internal static async Task<ContentType> SetAsync(this ContentType contentType, bool updateCache = false, CancellationToken cancellationToken = default)
		{
			contentType?.Set();
			await (updateCache && contentType != null && !string.IsNullOrWhiteSpace(contentType.ID) && !string.IsNullOrWhiteSpace(contentType.Title) ? Utility.Cache.SetAsync(contentType, cancellationToken) : Task.CompletedTask).ConfigureAwait(false);
			return contentType;
		}

		internal static ContentType Remove(this ContentType contentType)
			=> (contentType?.ID ?? "").RemoveContentType();

		internal static ContentType RemoveContentType(this string id)
		{
			if (!string.IsNullOrWhiteSpace(id) && ContentTypeProcessor.ContentTypes.TryRemove(id, out var contentType) && contentType != null)
			{
				contentType.EntityDefinition?.Unregister(contentType);
				return contentType;
			}
			return null;
		}

		public static ContentType GetContentTypeByID(this string id, bool force = false, bool fetchRepository = true)
			=> !force && !string.IsNullOrWhiteSpace(id) && ContentTypeProcessor.ContentTypes.ContainsKey(id)
				? ContentTypeProcessor.ContentTypes[id]
				: fetchRepository && !string.IsNullOrWhiteSpace(id)
					? ContentType.Get<ContentType>(id)?.Set()
					: null;

		public static async Task<ContentType> GetContentTypeByIDAsync(this string id, CancellationToken cancellationToken = default, bool force = false)
			=> (id ?? "").GetContentTypeByID(force, false) ?? (await ContentType.GetAsync<ContentType>(id, cancellationToken).ConfigureAwait(false))?.Set();

		public static IFilterBy<ContentType> GetContentTypesFilter(string systemID, string repositoryID = null, string definitionID = null)
		{
			var filter = Filters<ContentType>.And(Filters<ContentType>.Equals("SystemID", systemID));
			if (!string.IsNullOrWhiteSpace(repositoryID))
				filter.Add(Filters<ContentType>.Equals("RepositoryID", repositoryID));
			if (!string.IsNullOrWhiteSpace(definitionID))
				filter.Add(Filters<ContentType>.Equals("DefinitionID", definitionID));
			return filter;
		}

		public static List<ContentType> FindContentTypes(this string systemID, string repositoryID = null, string definitionID = null, bool updateCache = true)
		{
			if (string.IsNullOrWhiteSpace(systemID))
				return new List<ContentType>();

			var filter = ContentTypeProcessor.GetContentTypesFilter(systemID, repositoryID, definitionID);
			var sort = Sorts<ContentType>.Ascending("Title");
			var contentTypes = ContentType.Find(filter, sort, 0, 1, Extensions.GetCacheKey(filter, sort, 0, 1));
			contentTypes.ForEach(contentType =>
			{
				if (contentType.ID.GetContentTypeByID(false, false) == null)
					contentType.Set(updateCache);
			});

			return contentTypes;
		}

		public static async Task<List<ContentType>> FindContentTypesAsync(this string systemID, string repositoryID = null, string definitionID = null, CancellationToken cancellationToken = default, bool updateCache = true)
		{
			if (string.IsNullOrWhiteSpace(systemID))
				return new List<ContentType>();

			var filter = ContentTypeProcessor.GetContentTypesFilter(systemID, repositoryID, definitionID);
			var sort = Sorts<ContentType>.Ascending("Title");
			var contentTypes = await ContentType.FindAsync(filter, sort, 0, 1, Extensions.GetCacheKey(filter, sort, 0, 1), cancellationToken).ConfigureAwait(false);
			contentTypes.ForEach(contentType =>
			{
				if (contentType.ID.GetContentTypeByID(false, false) == null)
					contentType.Set(updateCache);
			});

			return contentTypes;
		}

		internal static Task ProcessInterCommunicateMessageOfContentTypeAsync(this CommunicateMessage message, CancellationToken cancellationToken = default)
		{
			if (message.Type.IsEndsWith("#Create"))
				message.Data.ToExpandoObject().CreateContentTypeInstance().Set();

			else if (message.Type.IsEndsWith("#Update"))
			{
				var contentType = message.Data.Get("ID", "").GetContentTypeByID(false, false);
				contentType = contentType == null
					? message.Data.ToExpandoObject().CreateContentTypeInstance()
					: contentType.UpdateContentTypeInstance(message.Data.ToExpandoObject());
				contentType.Set();
			}

			else if (message.Type.IsEndsWith("#Delete"))
				message.Data.ToExpandoObject().CreateContentTypeInstance().Remove();

			return Task.CompletedTask;
		}

		internal static async Task ClearRelatedCacheAsync(this ContentType contentType, CancellationToken cancellationToken, string correlationID = null, bool clearDataCache = true, bool clearHtmlCache = true, bool doRefresh = true)
		{
			// data cache keys
			var sort = Sorts<ContentType>.Ascending("Title");
			var dataCacheKeys = clearDataCache
				? Extensions.GetRelatedCacheKeys(Filters<ContentType>.And(), sort)
					.Concat(Extensions.GetRelatedCacheKeys(ContentTypeProcessor.GetContentTypesFilter(contentType.SystemID), sort))
					.Concat(Extensions.GetRelatedCacheKeys(ContentTypeProcessor.GetContentTypesFilter(contentType.SystemID, contentType.RepositoryID, contentType.ContentTypeDefinitionID), sort))
					.Concat(Extensions.GetRelatedCacheKeys(ContentTypeProcessor.GetContentTypesFilter(contentType.SystemID, contentType.RepositoryID, null), sort))
					.Concat(Extensions.GetRelatedCacheKeys(ContentTypeProcessor.GetContentTypesFilter(contentType.SystemID, null, contentType.ContentTypeDefinitionID), sort))
					.Concat(await Utility.Cache.GetSetMembersAsync(contentType.GetSetCacheKey(), cancellationToken).ConfigureAwait(false))
					.Concat(new[] { contentType.GetSetCacheKey() })
					.Distinct(StringComparer.OrdinalIgnoreCase)
					.ToList()
				: new List<string>();

			// html cache keys (desktop HTMLs)
			var htmlCacheKeys = new List<string>();
			if (clearHtmlCache)
			{
				htmlCacheKeys = contentType.Organization?.GetDesktopCacheKey() ?? new List<string>();
				await new[] { contentType.Desktop?.GetSetCacheKey() }
					.Concat(await contentType.GetSetCacheKeysAsync(cancellationToken).ConfigureAwait(false) ?? new List<string>())
					.Where(id => !string.IsNullOrWhiteSpace(id))
					.Distinct(StringComparer.OrdinalIgnoreCase)
					.ToList()
					.ForEachAsync(async desktopSetCacheKey =>
					{
						var cacheKeys = await Utility.Cache.GetSetMembersAsync(desktopSetCacheKey, cancellationToken).ConfigureAwait(false);
						if (cacheKeys != null && cacheKeys.Count > 0)
							htmlCacheKeys = htmlCacheKeys.Concat(cacheKeys).Concat(new[] { desktopSetCacheKey }).ToList();
					}, true, false).ConfigureAwait(false);
			}
			htmlCacheKeys = htmlCacheKeys.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

			// clear related cache
			await Utility.Cache.RemoveAsync(htmlCacheKeys.Concat(dataCacheKeys).Distinct(StringComparer.OrdinalIgnoreCase).ToList(), cancellationToken).ConfigureAwait(false);
			await Task.WhenAll
			(
				Utility.WriteCacheLogs ? Utility.WriteLogAsync(correlationID, $"Clear related cache of a content-type [{contentType.Title} - ID: {contentType.ID}]\r\n- {dataCacheKeys.Count} data keys => {dataCacheKeys.Join(", ")}\r\n- {htmlCacheKeys.Count} html keys => {htmlCacheKeys.Join(", ")}", ServiceBase.ServiceComponent.CancellationToken, "Caches") : Task.CompletedTask,
				doRefresh ? $"{Utility.PortalsHttpURI}/~{contentType.Organization.Alias}/".RefreshWebPageAsync(1, correlationID, $"Refresh desktop when related cache of a content-type was clean [{contentType.Title} - ID: {contentType.ID}]") : Task.CompletedTask
			).ConfigureAwait(false);
		}

		internal static async Task ClearCacheAsync(this ContentType contentType, CancellationToken cancellationToken = default, string correlationID = null, bool clearObjectsCache = true, bool clearRelatedDataCache = true, bool clearRelatedHtmlCache = true, bool doRefresh = true)
		{
			// clear related cache
			var tasks = new List<Task>
			{
				contentType.ClearRelatedCacheAsync(cancellationToken, correlationID, clearRelatedDataCache, clearRelatedHtmlCache, doRefresh)
			};

			// clear cache of business objects
			if (clearObjectsCache)
			{
				var objectKeys = await Utility.Cache.GetSetMembersAsync(contentType.ObjectCacheKeys, cancellationToken).ConfigureAwait(false);
				if (objectKeys.Count > 0)
					tasks.Add(Utility.Cache.RemoveAsync(new[] { contentType.ObjectCacheKeys }.Concat(objectKeys), cancellationToken));
			}

			// clear object cache
			tasks = tasks.Concat(new[]
			{
				Utility.Cache.RemoveAsync(contentType.Remove(), cancellationToken),
				Utility.RTUService.SendInterCommunicateMessageAsync(new CommunicateMessage(ServiceBase.ServiceComponent.ServiceName)
				{
					Type = $"{contentType.GetObjectName()}#Delete",
					Data = contentType.ToJson(),
					ExcludedNodeID = Utility.NodeID
				}, cancellationToken),
				Utility.WriteCacheLogs ? Utility.WriteLogAsync(correlationID, $"Clear cache of a content-type [{contentType.Title} - ID: {contentType.ID}]", ServiceBase.ServiceComponent.CancellationToken, "Caches") : Task.CompletedTask
			}).ToList();

			await Task.WhenAll(tasks).ConfigureAwait(false);
		}

		internal static async Task<JObject> SearchContentTypesAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, CancellationToken cancellationToken = default)
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
			var gotRights = isSystemAdministrator;
			if (!gotRights)
			{
				// get organization
				var organizationID = filter.GetValue("SystemID") ?? requestInfo.GetParameter("SystemID") ?? requestInfo.GetParameter("x-system-id") ?? requestInfo.GetParameter("OrganizationID");
				var organization = await (organizationID ?? "").GetOrganizationByIDAsync(cancellationToken).ConfigureAwait(false);
				if (organization == null)
					throw new InformationExistedException("The organization is invalid");

				var moduleID = filter.GetValue("RepositoryID") ?? requestInfo.GetParameter("RepositoryID") ?? requestInfo.GetParameter("x-module-id") ?? requestInfo.GetParameter("ModuleID");
				var module = await (moduleID ?? "").GetModuleByIDAsync(cancellationToken).ConfigureAwait(false);

				gotRights = requestInfo.Session.User.IsEditor(module?.WorkingPrivileges, null, organization, requestInfo.CorrelationID);
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
					? await ContentType.CountAsync(filter, Extensions.GetCacheKeyOfTotalObjects(filter, sort), cancellationToken).ConfigureAwait(false)
					: await ContentType.CountAsync(query, filter, cancellationToken).ConfigureAwait(false);

			var totalPages = new Tuple<long, int>(totalRecords, pageSize).GetTotalPages();
			if (totalPages > 0 && pageNumber > totalPages)
				pageNumber = totalPages;

			// search
			var objects = totalRecords > 0
				? string.IsNullOrWhiteSpace(query)
					? await ContentType.FindAsync(filter, sort, pageSize, pageNumber, Extensions.GetCacheKey(filter, sort, pageSize, pageNumber), cancellationToken).ConfigureAwait(false)
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
				await Utility.Cache.SetAsync(Extensions.GetCacheKeyOfObjectsJson(filter, sort, pageSize, pageNumber), response.ToString(Formatting.None), cancellationToken).ConfigureAwait(false);

			// response
			return response;
		}

		internal static async Task<ContentType> CreateContentTypeAsync(this ExpandoObject data, string systemID, string userID, string excludedProperties = null, string serviceName = null, CancellationToken cancellationToken = default, string correlationID = null)
		{
			// prepare
			var contentType = data.CreateContentTypeInstance(excludedProperties, obj =>
			{
				obj.ID = string.IsNullOrWhiteSpace(obj.ID) || !obj.ID.IsValidUUID() ? UtilityService.NewUUID : obj.ID;
				obj.SystemID = systemID;
				obj.Created = obj.LastModified = DateTime.Now;
				obj.CreatedID = obj.LastModifiedID = userID;
				obj.NormalizeExtras();
			});

			// validate extended properties
			contentType.EntityDefinition?.ValidateExtendedPropertyDefinitions(contentType.ID);

			// create new
			await ContentType.CreateAsync(contentType, cancellationToken).ConfigureAwait(false);
			contentType.ClearRelatedCacheAsync(ServiceBase.ServiceComponent.CancellationToken, correlationID).Run();

			// update instance/cache of module
			var module = contentType.Module;
			if (module._contentTypeIDs == null)
				await module.FindContentTypesAsync(cancellationToken).ConfigureAwait(false);
			else
			{
				module._contentTypeIDs.Add(contentType.ID);
				await module.SetAsync(true, cancellationToken).ConfigureAwait(false);
			}

			// send update messages
			var json = contentType.ToJson();
			var objectName = contentType.GetTypeName(true);
			await Task.WhenAll(
				Utility.RTUService.SendUpdateMessageAsync(new UpdateMessage
				{
					Type = $"{serviceName}#{objectName}#Create",
					Data = json,
					DeviceID = "*"
				}, cancellationToken),
				Utility.RTUService.SendInterCommunicateMessageAsync(new CommunicateMessage(serviceName)
				{
					Type = $"{objectName}#Create",
					Data = json,
					ExcludedNodeID = Utility.NodeID
				}, cancellationToken),
				Utility.RTUService.SendUpdateMessageAsync(new UpdateMessage
				{
					Type = $"{serviceName}#{module.GetObjectName()}#Update",
					Data = module.ToJson(),
					DeviceID = "*"
				}, cancellationToken),
				Utility.RTUService.SendInterCommunicateMessageAsync(new CommunicateMessage(serviceName)
				{
					Type = $"{module.GetObjectName()}#Update",
					Data = module.ToJson(),
					ExcludedNodeID = Utility.NodeID
				}, cancellationToken)
			).ConfigureAwait(false);

			// return the object
			return contentType.Set();
		}

		internal static Task<ContentType> CreateContentTypeAsync(this JObject data, string systemID, string userID, string excludedProperties = null, string serviceName = null, CancellationToken cancellationToken = default, string correlationID = null)
			=> data.ToExpandoObject().CreateContentTypeAsync(systemID, userID, excludedProperties, serviceName, cancellationToken, correlationID);

		internal static async Task<JObject> CreateContentTypeAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, CancellationToken cancellationToken = default)
		{
			// prepare
			var requestBody = requestInfo.GetBodyExpando();
			var organizationID = requestBody.Get<string>("SystemID") ?? requestInfo.GetParameter("SystemID") ?? requestInfo.GetParameter("x-system-id") ?? requestInfo.GetParameter("OrganizationID");
			var organization = await (organizationID ?? "").GetOrganizationByIDAsync(cancellationToken).ConfigureAwait(false);
			if (organization == null)
				throw new InformationInvalidException("The organization is invalid");

			var moduleID = requestBody.Get<string>("RepositoryID") ?? requestInfo.GetParameter("RepositoryID") ?? requestInfo.GetParameter("x-module-id") ?? requestInfo.GetParameter("ModuleID");
			var module = await (moduleID ?? "").GetModuleByIDAsync(cancellationToken).ConfigureAwait(false);

			// check permission
			var gotRights = isSystemAdministrator || requestInfo.Session.User.IsModerator(module?.WorkingPrivileges, null, organization, requestInfo.CorrelationID);
			if (!gotRights)
				throw new AccessDeniedException();

			// create new
			var contentType = await requestBody.CreateContentTypeAsync(organization.ID, requestInfo.Session.User.ID, "SystemID,Privileges,Created,CreatedID,LastModified,LastModifiedID", requestInfo.ServiceName, cancellationToken, requestInfo.CorrelationID).ConfigureAwait(false);

			// send notification
			contentType.SendNotificationAsync("Create", contentType.Organization.Notifications, ApprovalStatus.Published, ApprovalStatus.Published, requestInfo, ServiceBase.ServiceComponent.CancellationToken).Run();

			// response
			return contentType.ToJson();
		}

		internal static async Task<JObject> GetContentTypeAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, CancellationToken cancellationToken = default)
		{
			// prepare
			var contentType = await (requestInfo.GetObjectIdentity() ?? "").GetContentTypeByIDAsync(cancellationToken).ConfigureAwait(false);
			if (contentType == null)
				throw new InformationNotFoundException();
			else if (contentType.Organization == null)
				throw new InformationInvalidException("The organization is invalid");

			// check permission
			var gotRights = isSystemAdministrator || requestInfo.Session.User.IsViewer(contentType.Module?.WorkingPrivileges, null, contentType.Organization, requestInfo.CorrelationID);
			if (!gotRights)
				throw new AccessDeniedException();

			// send the update message to update to all other connected clients and response
			var response = contentType.ToJson();
			await Utility.RTUService.SendUpdateMessageAsync(new UpdateMessage
			{
				Type = $"{requestInfo.ServiceName}#{contentType.GetTypeName(true)}#Update",
				Data = response,
				DeviceID = "*",
				ExcludedDeviceID = requestInfo.Session.DeviceID
			}, cancellationToken).ConfigureAwait(false);
			return response;
		}

		internal static async Task<JObject> UpdateAsync(this ContentType contentType, RequestInfo requestInfo, CancellationToken cancellationToken, bool clearObjectsCache = false)
		{
			// update
			await ContentType.UpdateAsync(contentType, requestInfo.Session.User.ID, cancellationToken).ConfigureAwait(false);

			// clear cache
			await contentType.ClearCacheAsync(cancellationToken, requestInfo.CorrelationID, clearObjectsCache, true, false, false).ConfigureAwait(false);

			// send update messages
			await contentType.SetAsync(true, cancellationToken).ConfigureAwait(false);
			var response = contentType.ToJson();
			var objectName = contentType.GetTypeName(true);
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
			contentType.SendNotificationAsync("Update", contentType.Organization.Notifications, ApprovalStatus.Published, ApprovalStatus.Published, requestInfo).Run();

			// response
			return response;
		}

		internal static async Task<JObject> UpdateContentTypeAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, CancellationToken cancellationToken = default)
		{
			// prepare
			var contentType = await (requestInfo.GetObjectIdentity() ?? "").GetContentTypeByIDAsync(cancellationToken).ConfigureAwait(false);
			if (contentType == null)
				throw new InformationNotFoundException();
			else if (contentType.Organization == null || contentType.Module == null)
				throw new InformationInvalidException("The organization or module is invalid");

			// check permission
			var gotRights = isSystemAdministrator || requestInfo.Session.User.IsModerator(contentType.Module.WorkingPrivileges, null, contentType.Organization, requestInfo.CorrelationID);
			if (!gotRights)
				throw new AccessDeniedException();

			// gathering formation
			var privileges = contentType.OriginalPrivileges?.Copy();
			contentType.UpdateContentTypeInstance(requestInfo.GetBodyExpando(), "ID,SystemID,RepositoryID,ContentTypeDefinitionID,Privileges,Created,CreatedID,LastModified,LastModifiedID", obj =>
			{
				obj.LastModified = DateTime.Now;
				obj.LastModifiedID = requestInfo.Session.User.ID;
				obj.NormalizeExtras();
			});

			// validate extended properties
			contentType.EntityDefinition?.ValidateExtendedPropertyDefinitions(contentType.ID);

			// update
			var clearObjectsCache = !(contentType.OriginalPrivileges ?? new Privileges()).IsEquals(privileges);
			var response = await contentType.UpdateAsync(requestInfo, cancellationToken, clearObjectsCache).ConfigureAwait(false);

			// broadcast update when the privileges were changed
			// ...

			return response;
		}

		internal static async Task<JObject> DeleteContentTypeAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, CancellationToken cancellationToken = default)
		{
			// prepare
			var contentType = await (requestInfo.GetObjectIdentity() ?? "").GetContentTypeByIDAsync(cancellationToken).ConfigureAwait(false);
			if (contentType == null)
				throw new InformationNotFoundException();
			else if (contentType.Organization == null || contentType.Module == null)
				throw new InformationInvalidException("The organization or module is invalid");

			// check permission
			var gotRights = isSystemAdministrator || requestInfo.Session.User.IsAdministrator(contentType.Module.WorkingPrivileges, null, contentType.Organization, requestInfo.CorrelationID);
			if (!gotRights)
				throw new AccessDeniedException();

			// TO DO: delete all business objects first
			// .......

			// delete
			await ContentType.DeleteAsync<ContentType>(contentType.ID, requestInfo.Session.User.ID, cancellationToken).ConfigureAwait(false);
			await contentType.ClearCacheAsync(cancellationToken, requestInfo.CorrelationID, false, true, false, false).ConfigureAwait(false);

			// update instance/cache of module
			var module = contentType.Module;
			if (module != null && module._contentTypeIDs != null)
			{
				module._contentTypeIDs.Remove(contentType.ID);
				await module.SetAsync(true, cancellationToken).ConfigureAwait(false);
				await Task.WhenAll(
					Utility.RTUService.SendUpdateMessageAsync(new UpdateMessage
					{
						Type = $"{requestInfo.ServiceName}#{module.GetObjectName()}#Update",
						Data = module.ToJson(),
						DeviceID = "*"
					}, cancellationToken),
					Utility.RTUService.SendInterCommunicateMessageAsync(new CommunicateMessage(requestInfo.ServiceName)
					{
						Type = $"{module.GetObjectName()}#Update",
						Data = module.ToJson(),
						ExcludedNodeID = Utility.NodeID
					}, cancellationToken)
				).ConfigureAwait(false);
			}

			// send update messages
			var response = contentType.ToJson();
			var objectName = contentType.GetTypeName(true);
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
			contentType.SendNotificationAsync("Delete", contentType.Organization.Notifications, ApprovalStatus.Published, ApprovalStatus.Published, requestInfo, ServiceBase.ServiceComponent.CancellationToken).Run();

			// response
			return response;
		}

		internal static async Task<JObject> SyncContentTypeAsync(this RequestInfo requestInfo, CancellationToken cancellationToken = default)
		{
			var data = requestInfo.GetBodyExpando();
			var contentType = await data.Get<string>("ID").GetContentTypeByIDAsync(cancellationToken).ConfigureAwait(false);
			if (contentType == null)
			{
				contentType = ContentType.CreateInstance(data);
				contentType.NormalizeExtras();
				contentType.Extras = data.Get<string>("Extras") ?? contentType.Extras;
				await ContentType.CreateAsync(contentType, cancellationToken).ConfigureAwait(false);
			}
			else
			{
				contentType.Fill(data);
				contentType.NormalizeExtras();
				contentType.Extras = data.Get<string>("Extras") ?? contentType.Extras;
				await ContentType.UpdateAsync(contentType, true, cancellationToken).ConfigureAwait(false);
			}

			// clear related cache
			if (requestInfo.GetHeaderParameter("x-converter") != null)
				contentType.ClearRelatedCacheAsync(ServiceBase.ServiceComponent.CancellationToken, requestInfo.CorrelationID).Run();
			else
				await contentType.ClearCacheAsync(cancellationToken, requestInfo.CorrelationID).ConfigureAwait(false);

			// send update messages
			var json = contentType.Set().ToJson();
			var objectName = contentType.GetTypeName(true);
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
				{ "ID", contentType.ID },
				{ "Type", objectName }
			};
		}
	}
}
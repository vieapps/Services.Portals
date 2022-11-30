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

		internal static HashSet<string> ExtraProperties { get; } = "Notifications,Trackings,EmailSettings,WebHookNotifications,WebHookAdapters,SubTitleFormula".ToHashSet();

		public static ContentType CreateContentType(this ExpandoObject data, string excluded = null, Action<ContentType> onCompleted = null)
			=> ContentType.CreateInstance(data, excluded?.ToHashSet(), contentType =>
			{
				contentType.NormalizeExtras();
				onCompleted?.Invoke(contentType);
			});

		public static ContentType Update(this ContentType contentType, ExpandoObject data, string excluded = null, Action<ContentType> onCompleted = null)
			=> contentType?.Fill(data, excluded?.ToHashSet(), _ =>
			{
				contentType.NormalizeExtras();
				onCompleted?.Invoke(contentType);
			});

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
			await contentTypes.ForEachAsync(async contentType =>
			{
				if (contentType.ID.GetContentTypeByID(false, false) == null)
					await contentType.SetAsync(updateCache, cancellationToken).ConfigureAwait(false);
			}).ConfigureAwait(false);

			return contentTypes;
		}

		internal static Task ProcessInterCommunicateMessageOfContentTypeAsync(this CommunicateMessage message, CancellationToken cancellationToken = default)
		{
			if (message.Type.IsEndsWith("#Create"))
				message.Data.ToExpandoObject().CreateContentType().Set();

			else if (message.Type.IsEndsWith("#Update"))
			{
				var contentType = message.Data.Get("ID", "").GetContentTypeByID(false, false);
				contentType = contentType == null
					? message.Data.ToExpandoObject().CreateContentType()
					: contentType.Update(message.Data.ToExpandoObject());
				contentType.Set();
			}

			else if (message.Type.IsEndsWith("#Delete"))
				message.Data.ToExpandoObject().CreateContentType().Remove();

			return Task.CompletedTask;
		}

		internal static async Task ClearRelatedCacheAsync(this ContentType contentType, CancellationToken cancellationToken, string correlationID = null, bool clearDataCache = true, bool clearHtmlCache = true, bool doRefresh = true)
		{
			// tasks for updating sets
			var setTasks = new List<Task>();

			// data cache keys
			var sort = Sorts<ContentType>.Ascending("Title");
			var dataCacheKeys = clearDataCache
				? Extensions.GetRelatedCacheKeys(Filters<ContentType>.And(), sort)
					.Concat(Extensions.GetRelatedCacheKeys(ContentTypeProcessor.GetContentTypesFilter(contentType.SystemID), sort))
					.Concat(Extensions.GetRelatedCacheKeys(ContentTypeProcessor.GetContentTypesFilter(contentType.SystemID, contentType.RepositoryID, contentType.ContentTypeDefinitionID), sort))
					.Concat(Extensions.GetRelatedCacheKeys(ContentTypeProcessor.GetContentTypesFilter(contentType.SystemID, contentType.RepositoryID, null), sort))
					.Concat(Extensions.GetRelatedCacheKeys(ContentTypeProcessor.GetContentTypesFilter(contentType.SystemID, null, contentType.ContentTypeDefinitionID), sort))
					.Distinct(StringComparer.OrdinalIgnoreCase)
					.ToList()
				: new List<string>();

			if (clearDataCache)
			{
				var cacheKeys = await Utility.Cache.GetSetMembersAsync(contentType.GetSetCacheKey(), cancellationToken).ConfigureAwait(false);
				if (cacheKeys != null && cacheKeys.Any())
				{
					setTasks.Add(Utility.Cache.RemoveSetMembersAsync(contentType.GetSetCacheKey(), cacheKeys, cancellationToken));
					dataCacheKeys = dataCacheKeys.Concat(cacheKeys).ToList();
				}
			}

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
						if (cacheKeys != null && cacheKeys.Any())
						{
							setTasks.Add(Utility.Cache.RemoveSetMembersAsync(desktopSetCacheKey, cacheKeys, cancellationToken));
							htmlCacheKeys = htmlCacheKeys.Concat(cacheKeys).ToList();
						}
					}, true, false).ConfigureAwait(false);
			}
			htmlCacheKeys = htmlCacheKeys.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

			// clear related cache
			await Task.WhenAll
			(
				Task.WhenAll(setTasks),
				Utility.Cache.RemoveAsync(htmlCacheKeys.Concat(dataCacheKeys).Distinct(StringComparer.OrdinalIgnoreCase).ToList(), cancellationToken),
				Utility.IsCacheLogEnabled ? Utility.WriteLogAsync(correlationID, $"Clear related cache of a content-type [{contentType.Title} - ID: {contentType.ID}]\r\n- {dataCacheKeys.Count} data keys => {dataCacheKeys.Join(", ")}\r\n- {htmlCacheKeys.Count} html keys => {htmlCacheKeys.Join(", ")}", "Caches") : Task.CompletedTask,
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
				if (objectKeys != null && objectKeys.Any())
				{
					tasks.Add(Utility.Cache.RemoveSetMembersAsync(contentType.ObjectCacheKeys, objectKeys, cancellationToken));
					tasks.Add(Utility.Cache.RemoveAsync(objectKeys, cancellationToken));
				}
			}

			// clear object cache
			tasks = tasks.Concat(new[]
			{
				Utility.Cache.RemoveAsync(contentType.Remove(), cancellationToken),
				new CommunicateMessage(ServiceBase.ServiceComponent.ServiceName)
				{
					Type = $"{contentType.GetObjectName()}#Delete",
					Data = contentType.ToJson(),
					ExcludedNodeID = Utility.NodeID
				}.SendAsync(),
				Utility.IsCacheLogEnabled ? Utility.WriteLogAsync(correlationID, $"Clear cache of a content-type [{contentType.Title} - ID: {contentType.ID}]", "Caches") : Task.CompletedTask
			}).ToList();

			await Task.WhenAll(tasks).ConfigureAwait(false);
		}

		internal static async Task<JObject> SearchContentTypesAsync(this RequestInfo requestInfo, bool isSystemAdministrator, CancellationToken cancellationToken)
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

				gotRights = requestInfo.Session.User.IsEditor(module?.WorkingPrivileges, null, organization);
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
					: await ContentType.SearchAsync(query, filter, null, pageSize, pageNumber, cancellationToken).ConfigureAwait(false)
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

		static void Validate(this ContentType contentType, RequestInfo requestInfo = null)
		{
			contentType.Notifications?.WebHooks?.Validate(requestInfo, contentType);
			contentType.EntityDefinition?.ValidateExtendedPropertyDefinitions(contentType.ID);
			if (!string.IsNullOrWhiteSpace(contentType.SubTitleFormula) && contentType.SubTitleFormula.StartsWith("@"))
				try
				{
					contentType.SubTitleFormula.Evaluate(new JObject
					{
						["ID"] = "ID",
						["Title"] = "Title",
						["Body"] = new JObject { ["ID"] = "ID", ["Title"] = "Title" }
					}.ToExpandoObject());
				}
				catch (Exception ex)
				{
					throw new InformationInvalidException($"SubTitle => {ex.Message}", ex);
				}

			if (contentType.WebHookNotifications != null && contentType.WebHookNotifications.Any())
			{
				foreach (var webhook in contentType.WebHookNotifications)
					webhook?.Normalize().Validate(requestInfo, contentType);
				contentType.WebHookNotifications = contentType.WebHookNotifications.Normalize();
			}
			else
				contentType.WebHookNotifications = null;

			if (contentType.WebHookAdapters != null && contentType.WebHookAdapters.Any())
			{
				foreach (var kvp in contentType.WebHookAdapters)
					kvp.Value?.Validate(requestInfo, contentType);
				contentType.WebHookAdapters = contentType.WebHookAdapters.Normalize();
			}
			else
				contentType.WebHookAdapters = null;
		}

		internal static List<Settings.WebHookNotification> Normalize(this List<Settings.WebHookNotification> webhooks)
		{
			webhooks = webhooks.Where(webhook => webhook != null && webhook.EndpointURLs != null && webhook.EndpointURLs.Any()).ToList();
			return webhooks.Any() ? webhooks : null;
		}

		internal static Dictionary<string, Settings.WebHookSetting> Normalize(this Dictionary<string, Settings.WebHookSetting> webhooks)
		{
			webhooks = webhooks.Where(kvp => kvp.Key.IsEquals("default"))
				.Concat(webhooks.Where(kvp => !kvp.Key.IsEquals("default")).OrderBy(kvp => kvp.Key))
				.Where(kvp => kvp.Value?.Normalize() != null)
				.ToDictionary(kvp => kvp.Key.NormalizeAlias(false), kvp => kvp.Value);
			return webhooks.Any() ? webhooks : null;
		}

		internal static async Task<ContentType> CreateContentTypeAsync(this ExpandoObject data, string systemID, string userID, string excludedProperties = null, string serviceName = null, CancellationToken cancellationToken = default, string correlationID = null)
		{
			// prepare
			var contentType = data.CreateContentType(excludedProperties, obj =>
			{
				obj.ID = string.IsNullOrWhiteSpace(obj.ID) || !obj.ID.IsValidUUID() ? UtilityService.NewUUID : obj.ID;
				obj.SystemID = systemID;
				obj.Created = obj.LastModified = DateTime.Now;
				obj.CreatedID = obj.LastModifiedID = userID;
			});

			// validate
			contentType.Validate();

			// create new
			await ContentType.CreateAsync(contentType, cancellationToken).ConfigureAwait(false);
			await contentType.ClearRelatedCacheAsync(cancellationToken, correlationID).ConfigureAwait(false);

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
			new UpdateMessage
			{
				Type = $"{serviceName}#{objectName}#Create",
				Data = json,
				DeviceID = "*"
			}.Send();
			new CommunicateMessage(serviceName)
			{
				Type = $"{objectName}#Create",
				Data = json,
				ExcludedNodeID = Utility.NodeID
			}.Send();
			new UpdateMessage
			{
				Type = $"{serviceName}#{module.GetObjectName()}#Update",
				Data = module.ToJson(),
				DeviceID = "*"
			}.Send();
			new CommunicateMessage(serviceName)
			{
				Type = $"{module.GetObjectName()}#Update",
				Data = module.ToJson(),
				ExcludedNodeID = Utility.NodeID
			}.Send();

			// return the object
			return contentType.Set();
		}

		internal static Task<ContentType> CreateContentTypeAsync(this JObject data, string systemID, string userID, string excludedProperties = null, string serviceName = null, CancellationToken cancellationToken = default, string correlationID = null)
			=> data.ToExpandoObject().CreateContentTypeAsync(systemID, userID, excludedProperties, serviceName, cancellationToken, correlationID);

		internal static async Task<JObject> CreateContentTypeAsync(this RequestInfo requestInfo, bool isSystemAdministrator, CancellationToken cancellationToken)
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
			var gotRights = isSystemAdministrator || requestInfo.Session.User.IsModerator(module?.WorkingPrivileges, null, organization);
			if (!gotRights)
				throw new AccessDeniedException();

			// create new
			var contentType = await requestBody.CreateContentTypeAsync(organization.ID, requestInfo.Session.User.ID, "SystemID,Privileges,Created,CreatedID,LastModified,LastModifiedID", requestInfo.ServiceName, cancellationToken, requestInfo.CorrelationID).ConfigureAwait(false);

			// send notification
			await contentType.SendNotificationAsync("Create", contentType.Organization.Notifications, ApprovalStatus.Published, ApprovalStatus.Published, requestInfo, cancellationToken).ConfigureAwait(false);

			// response
			return contentType.ToJson();
		}

		internal static async Task<JObject> GetContentTypeAsync(this RequestInfo requestInfo, bool isSystemAdministrator, CancellationToken cancellationToken)
		{
			// prepare
			var identity = requestInfo.GetObjectIdentity(true, true) ?? "";
			var contentType = await identity.GetContentTypeByIDAsync(cancellationToken).ConfigureAwait(false);
			if (contentType == null)
				throw new InformationNotFoundException();
			else if (contentType.Organization == null)
				throw new InformationInvalidException("The organization is invalid");

			// check permission
			var gotRights = isSystemAdministrator || requestInfo.Session.User.IsViewer(contentType.Module?.WorkingPrivileges, null, contentType.Organization);
			if (!gotRights)
				throw new AccessDeniedException();

			// refresh (clear cached and reload)
			var isRefresh = "refresh".IsEquals(requestInfo.GetObjectIdentity());
			if (isRefresh)
			{
				await Utility.Cache.RemoveAsync(contentType, cancellationToken).ConfigureAwait(false);
				contentType = await contentType.Remove().ID.GetContentTypeByIDAsync(cancellationToken, true).ConfigureAwait(false);
			}

			// send the update message to update to all other connected clients
			var response = contentType.ToJson();
			var objectName = contentType.GetObjectName();
			var versions = await contentType.FindVersionsAsync(cancellationToken, false).ConfigureAwait(false);
			new UpdateMessage
			{
				Type = $"{requestInfo.ServiceName}#{objectName}#Update",
				Data = response.UpdateVersions(versions),
				DeviceID = "*"
			}.Send();
			if (isRefresh)
				new CommunicateMessage(requestInfo.ServiceName)
				{
					Type = $"{objectName}#Update",
					Data = response,
					ExcludedNodeID = Utility.NodeID
				}.Send();

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
			var versions = await contentType.FindVersionsAsync(cancellationToken, false).ConfigureAwait(false);
			var response = contentType.ToJson();
			var objectName = contentType.GetTypeName(true);
			new UpdateMessage
			{
				Type = $"{requestInfo.ServiceName}#{objectName}#Update",
				Data = response.UpdateVersions(versions),
				DeviceID = "*"
			}.Send();
			new CommunicateMessage(requestInfo.ServiceName)
			{
				Type = $"{objectName}#Update",
				Data = response,
				ExcludedNodeID = Utility.NodeID
			}.Send();

			// send notification
			await contentType.SendNotificationAsync("Update", contentType.Organization.Notifications, ApprovalStatus.Published, ApprovalStatus.Published, requestInfo).ConfigureAwait(false);

			// response
			return response;
		}

		internal static async Task<JObject> UpdateContentTypeAsync(this RequestInfo requestInfo, bool isSystemAdministrator, CancellationToken cancellationToken)
		{
			// prepare
			var contentType = await (requestInfo.GetObjectIdentity() ?? "").GetContentTypeByIDAsync(cancellationToken).ConfigureAwait(false);
			if (contentType == null)
				throw new InformationNotFoundException();
			else if (contentType.Organization == null || contentType.Module == null)
				throw new InformationInvalidException("The organization or module is invalid");

			// check permission
			var gotRights = isSystemAdministrator || requestInfo.Session.User.IsModerator(contentType.Module.WorkingPrivileges, null, contentType.Organization);
			if (!gotRights)
				throw new AccessDeniedException();

			// gathering formation
			var privileges = contentType.OriginalPrivileges?.Copy();
			contentType.Update(requestInfo.GetBodyExpando(), "ID,SystemID,RepositoryID,ContentTypeDefinitionID,Privileges,Created,CreatedID,LastModified,LastModifiedID", obj =>
			{
				obj.LastModified = DateTime.Now;
				obj.LastModifiedID = requestInfo.Session.User.ID;
			});

			// validate
			contentType.Validate(requestInfo);

			// update
			var clearObjectsCache = !(contentType.OriginalPrivileges ?? new Privileges()).IsEquals(privileges);
			var response = await contentType.UpdateAsync(requestInfo, cancellationToken, clearObjectsCache).ConfigureAwait(false);

			// broadcast update when the privileges were changed
			// ...

			return response;
		}

		internal static async Task<JObject> DeleteContentTypeAsync(this RequestInfo requestInfo, bool isSystemAdministrator, CancellationToken cancellationToken)
		{
			// prepare
			var contentType = await (requestInfo.GetObjectIdentity() ?? "").GetContentTypeByIDAsync(cancellationToken).ConfigureAwait(false);
			if (contentType == null)
				throw new InformationNotFoundException();
			else if (contentType.Organization == null || contentType.Module == null)
				throw new InformationInvalidException("The organization or module is invalid");

			// check permission
			var gotRights = isSystemAdministrator || requestInfo.Session.User.IsAdministrator(contentType.Module.WorkingPrivileges, null, contentType.Organization);
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
				new UpdateMessage
				{
					Type = $"{requestInfo.ServiceName}#{module.GetObjectName()}#Update",
					Data = module.ToJson(),
					DeviceID = "*"
				}.Send();
				new CommunicateMessage(requestInfo.ServiceName)
				{
					Type = $"{module.GetObjectName()}#Update",
					Data = module.ToJson(),
					ExcludedNodeID = Utility.NodeID
				}.Send();
			}

			// send update messages
			var response = contentType.ToJson();
			var objectName = contentType.GetTypeName(true);
			new UpdateMessage
			{
				Type = $"{requestInfo.ServiceName}#{objectName}#Delete",
				Data = response,
				DeviceID = "*"
			}.Send();
			new CommunicateMessage(requestInfo.ServiceName)
			{
				Type = $"{objectName}#Delete",
				Data = response,
				ExcludedNodeID = Utility.NodeID
			}.Send();

			// send notification
			await contentType.SendNotificationAsync("Delete", contentType.Organization.Notifications, ApprovalStatus.Published, ApprovalStatus.Published, requestInfo, cancellationToken).ConfigureAwait(false);

			// response
			return response;
		}

		internal static async Task<JObject> SyncContentTypeAsync(this RequestInfo requestInfo, CancellationToken cancellationToken, bool sendNotifications = false, bool dontCreateNewVersion = false)
		{
			var @event = requestInfo.GetParameter("event") ?? requestInfo.GetParameter("x-original-event");
			if (string.IsNullOrWhiteSpace(@event) || !@event.IsEquals("Delete"))
				@event = "Update";

			var data = requestInfo.GetBodyExpando();
			var contentType = await data.Get<string>("ID").GetContentTypeByIDAsync(cancellationToken).ConfigureAwait(false);

			if (!@event.IsEquals("Delete"))
			{
				if (contentType == null)
				{
					contentType = ContentType.CreateInstance(data, null, obj => obj.Extras = data.Get<string>("Extras") ?? obj.Extras);
					await ContentType.CreateAsync(contentType, cancellationToken).ConfigureAwait(false);
				}
				else
					await ContentType.UpdateAsync(contentType.Update(data, null, obj => obj.Extras = data.Get<string>("Extras") ?? obj.Extras), dontCreateNewVersion, cancellationToken).ConfigureAwait(false);
			}
			else if (contentType != null)
				await ContentType.DeleteAsync<ContentType>(contentType.ID, contentType.LastModifiedID, cancellationToken).ConfigureAwait(false);

			// clear related cache
			if (requestInfo.GetHeaderParameter("x-converter") == null || @event.IsEquals("Delete"))
				await contentType.ClearCacheAsync(cancellationToken, requestInfo.CorrelationID).ConfigureAwait(false);
			else
				await contentType.ClearRelatedCacheAsync(cancellationToken, requestInfo.CorrelationID).ConfigureAwait(false);

			// send notifications
			if (sendNotifications)
				await contentType.SendNotificationAsync(@event, contentType.Organization.Notifications, ApprovalStatus.Published, ApprovalStatus.Published, requestInfo, cancellationToken).ConfigureAwait(false);

			// send update messages
			var json = @event.IsEquals("Delete")
				? contentType.Remove().ToJson()
				: contentType.Set().ToJson();
			var objectName = contentType.GetTypeName(true);
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
			return json;
		}

		internal static async Task<JObject> RollbackContentTypeAsync(this RequestInfo requestInfo, bool isSystemAdministrator, CancellationToken cancellationToken)
		{
			// prepare
			var contentType = await (requestInfo.GetObjectIdentity() ?? "").GetContentTypeByIDAsync(cancellationToken).ConfigureAwait(false);
			if (contentType == null)
				throw new InformationNotFoundException();
			else if (contentType.Organization == null || contentType.Module == null)
				throw new InformationInvalidException("The organization or module is invalid");

			// check permission
			var gotRights = isSystemAdministrator || requestInfo.Session.User.IsModerator(contentType.Module.WorkingPrivileges, null, contentType.Organization);
			if (!gotRights)
				throw new AccessDeniedException();

			// rollback
			contentType = await RepositoryMediator.RollbackAsync<ContentType>(requestInfo.GetParameter("x-version-id") ?? "", requestInfo.Session.User.ID, cancellationToken).ConfigureAwait(false);
			await Task.WhenAll
			(
				contentType.ClearCacheAsync(cancellationToken, requestInfo.CorrelationID, true, true, false, false),
				contentType.SendNotificationAsync("Rollback", contentType.Organization.Notifications, ApprovalStatus.Published, ApprovalStatus.Published, requestInfo, cancellationToken)
			).ConfigureAwait(false);

			// send update messages
			var versions = await contentType.FindVersionsAsync(cancellationToken, false).ConfigureAwait(false);
			var response = contentType.Set(true).ToJson();
			var objectName = contentType.GetTypeName(true);
			new UpdateMessage
			{
				Type = $"{requestInfo.ServiceName}#{objectName}#Update",
				Data = response.UpdateVersions(versions),
				DeviceID = "*"
			}.Send();
			new CommunicateMessage(requestInfo.ServiceName)
			{
				Type = $"{objectName}#Update",
				Data = response,
				ExcludedNodeID = Utility.NodeID
			}.Send();
			return response;
		}
	}
}
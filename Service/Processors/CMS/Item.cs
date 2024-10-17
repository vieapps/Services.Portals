#region Related components
using System;
using System.Linq;
using System.Xml.Linq;
using System.Dynamic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using net.vieapps.Components.Security;
using net.vieapps.Components.Repository;
using net.vieapps.Components.Utility;
using net.vieapps.Services.Portals.Crawlers;
#endregion

namespace net.vieapps.Services.Portals
{
	public static class ItemProcessor
	{
		public static Item CreateItem(this ExpandoObject data, string excluded = null, Action<Item> onCompleted = null)
			=> Item.CreateInstance(data, excluded?.ToHashSet(), item =>
			{
				item.NormalizeHTMLs();
				item.Alias = (string.IsNullOrWhiteSpace(item.Alias) ? item.Title : item.Alias).NormalizeAlias();
				item.Tags = item.Tags?.Replace(";", ",").ToList(",", true).Where(tag => !string.IsNullOrWhiteSpace(tag)).Join(",");
				item.Tags = string.IsNullOrWhiteSpace(item.Tags) ? null : item.Tags;
				onCompleted?.Invoke(item);
			});

		public static Item Update(this Item item, ExpandoObject data, string excluded = null, Action<Item> onCompleted = null)
			=> item.Fill(data, excluded?.ToHashSet(), _ =>
			{
				item.NormalizeHTMLs();
				item.Alias = (string.IsNullOrWhiteSpace(item.Alias) ? item.Title : item.Alias).NormalizeAlias();
				item.Tags = item.Tags?.Replace(";", ",").ToList(",", true).Where(tag => !string.IsNullOrWhiteSpace(tag)).Join(",");
				item.Tags = string.IsNullOrWhiteSpace(item.Tags) ? null : item.Tags;
				onCompleted?.Invoke(item);
			});

		internal static string GetCacheKeyOfAliasedItem(this string contentTypeID, string alias)
			=> !string.IsNullOrWhiteSpace(contentTypeID) && !string.IsNullOrWhiteSpace(alias)
				? $"e:{contentTypeID}#a:{alias.NormalizeAlias().GenerateUUID()}".GetCacheKey<Item>()
				: null;

		internal static string GetCacheKeyOfAliasedItem(this Item item)
			=> item?.ContentType?.ID?.GetCacheKeyOfAliasedItem(item?.Alias);

		public static IFilterBy<Item> GetItemsFilter(string systemID, string repositoryID = null, string repositoryEntityID = null)
		{
			var filter = Filters<Item>.And();
			if (!string.IsNullOrWhiteSpace(systemID))
				filter.Add(Filters<Item>.Equals("SystemID", systemID));
			if (!string.IsNullOrWhiteSpace(repositoryID))
				filter.Add(Filters<Item>.Equals("RepositoryID", repositoryID));
			if (!string.IsNullOrWhiteSpace(repositoryEntityID))
				filter.Add(Filters<Item>.Equals("RepositoryEntityID", repositoryEntityID));
			return filter;
		}

		internal static async Task ClearRelatedCacheAsync(this Item item, CancellationToken cancellationToken = default, string correlationID = null, bool clearDataCache = true, bool clearHtmlCache = true, bool doRefresh = true)
		{
			// tasks for updating sets
			var setTasks = new List<Task>();

			// data cache keys
			var dataCacheKeys = clearDataCache && item != null
				? Extensions.GetRelatedCacheKeys(item.GetCacheKey()).Concat(new[] { item.GetCacheKeyOfAliasedItem() }).Where(key => key != null).ToList()
				: new List<string>();
			if (clearDataCache && item?.ContentType != null)
			{
				var cacheKeys = await Utility.Cache.GetSetMembersAsync(item.ContentType.GetSetCacheKey(), cancellationToken).ConfigureAwait(false);
				if (cacheKeys != null && cacheKeys.Any())
				{
					setTasks.Add(Utility.Cache.RemoveSetMembersAsync(item.ContentType.GetSetCacheKey(), cacheKeys, cancellationToken));
					dataCacheKeys = dataCacheKeys.Concat(cacheKeys).ToList();
				}
			}
			dataCacheKeys = dataCacheKeys.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

			// html cache keys (desktop HTMLs)
			Desktop desktop = null;
			var htmlCacheKeys = new List<string>();
			if (clearHtmlCache)
			{
				desktop = item?.ContentType?.Desktop;
				htmlCacheKeys = item?.Organization?.GetDesktopCacheKey() ?? new List<string>();
				await new[] { desktop?.GetSetCacheKey() }
					.Concat(item?.ContentType != null ? await item.ContentType.GetSetCacheKeysAsync(cancellationToken).ConfigureAwait(false) : new List<string>())
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

			// remove cache
			await Task.WhenAll
			(
				Task.WhenAll(setTasks),
				Utility.Cache.RemoveAsync(htmlCacheKeys.Concat(dataCacheKeys).Distinct(StringComparer.OrdinalIgnoreCase).ToList(), cancellationToken),
				Utility.IsCacheLogEnabled && item != null ? Utility.WriteLogAsync(correlationID, $"Clear related cache of a CMS item [{item.Title} - ID: {item.ID}]\r\n- {dataCacheKeys.Count} data keys => {dataCacheKeys.Join(", ")}\r\n- {htmlCacheKeys.Count} html keys => {htmlCacheKeys.Join(", ")}", "Caches") : Task.CompletedTask,
				doRefresh && item != null
					? Task.WhenAll
					(
						item.Status.Equals(ApprovalStatus.Published) ? $"{item.GetURL()}?x-force-cache=x".Replace("~/", $"{item.Organization?.URL}/").RefreshWebPageAsync(1, correlationID, $"Refresh desktop when related cache of a CMS item was clean [{item.Title} - ID: {item.ID}]") : Task.CompletedTask,
						desktop != null ? $"{item.Organization?.URL}/{desktop.Alias ?? "-default"}/{item.ContentType?.Title.GetANSIUri() ?? "-"}?x-force-cache=x".RefreshWebPageAsync(1, correlationID, $"Refresh desktop when related cache of a CMS item was clean [{item.Title} - ID: {item.ID}]") : Task.CompletedTask,
						$"{item.Organization?.URL}?x-force-cache=x".RefreshWebPageAsync(1, correlationID, $"Refresh desktop when related cache of a CMS item was clean [{item.Title} - ID: {item.ID}]")
					) : Task.CompletedTask
				).ConfigureAwait(false);
		}

		static async Task<(long TotalRecords, List<Item> Objects, JToken Thumbnails, List<string> CacheKeys)> SearchAsync(this RequestInfo requestInfo, string query, IFilterBy<Item> filter, SortBy<Item> sort, int pageSize, int pageNumber, string contentTypeID = null, long totalRecords = -1, CancellationToken cancellationToken = default, bool searchThumbnails = true)
		{
			// cache keys
			var cacheKeyOfObjects = string.IsNullOrWhiteSpace(query) ? Extensions.GetCacheKey(filter, sort, pageSize, pageNumber) : null;
			var cacheKeyOfTotalObjects = string.IsNullOrWhiteSpace(query) ? Extensions.GetCacheKeyOfTotalObjects(filter, sort) : null;
			var cacheKeys = string.IsNullOrWhiteSpace(query) ? new List<string> { cacheKeyOfObjects, cacheKeyOfTotalObjects } : new List<string>();

			// count
			totalRecords = totalRecords > -1
				? totalRecords
				: string.IsNullOrWhiteSpace(query)
					? await Item.CountAsync(filter, contentTypeID, cacheKeyOfTotalObjects, cancellationToken).ConfigureAwait(false)
					: await Item.CountAsync(query, filter, contentTypeID, cancellationToken).ConfigureAwait(false);

			// search objects
			var objects = totalRecords > 0
				? string.IsNullOrWhiteSpace(query)
					? await Item.FindAsync(filter, sort, pageSize, pageNumber, contentTypeID, cacheKeyOfObjects, cancellationToken).ConfigureAwait(false)
					: await Item.SearchAsync(query, filter, null, pageSize, pageNumber, contentTypeID, cancellationToken).ConfigureAwait(false)
				: new List<Item>();

			// search thumbnails
			JToken thumbnails = null;
			if (objects.Count > 0 && searchThumbnails)
			{
				requestInfo.Header["x-thumbnails-as-attachments"] = "true";
				thumbnails = objects.Count == 1
					? await requestInfo.GetThumbnailsAsync(objects[0].ID, objects[0].Title.Url64Encode(), Utility.ValidationKey, cancellationToken).ConfigureAwait(false)
					: await requestInfo.GetThumbnailsAsync(objects.Select(@object => @object.ID).Join(","), objects.ToJObject("ID", @object => new JValue(@object.Title.Url64Encode())).ToString(Formatting.None), Utility.ValidationKey, cancellationToken).ConfigureAwait(false);
			}

			// page size to clear related cached
			if (string.IsNullOrWhiteSpace(query))
				await Utility.SetCacheOfPageSizeAsync(filter, sort, pageSize, cancellationToken).ConfigureAwait(false);

			// store object identities to clear related cached
			var contentType = objects.FirstOrDefault()?.ContentType;
			if (contentType != null)
				await Utility.Cache.AddSetMembersAsync(contentType.ObjectCacheKeys, objects.Select(@object => @object.GetCacheKey()), cancellationToken).ConfigureAwait(false);

			// return the results
			return (totalRecords, objects, thumbnails, cacheKeys);
		}

		internal static async Task<JObject> SearchItemsAsync(this RequestInfo requestInfo, bool isSystemAdministrator, CancellationToken cancellationToken)
		{
			// prepare
			var request = requestInfo.GetRequestExpando();

			var query = request.Get<string>("FilterBy.Query");

			var filter = request.Get<ExpandoObject>("FilterBy")?.ToFilterBy<Item>() ?? Filters<Item>.And();
			var sort = string.IsNullOrWhiteSpace(query) ? request.Get<ExpandoObject>("SortBy")?.ToSortBy<Item>() ?? Sorts<Item>.Descending("Created").ThenByAscending("Title") : null;

			var pagination = request.Get<ExpandoObject>("Pagination")?.GetPagination() ?? new Tuple<long, int, int, int>(-1, 0, 20, 1);
			var pageSize = pagination.Item3;
			var pageNumber = pagination.Item4;

			var organizationID = filter.GetValue("SystemID") ?? requestInfo.GetParameter("SystemID") ?? requestInfo.GetParameter("x-system-id");
			var organization = await (organizationID ?? "").GetOrganizationByIDAsync(cancellationToken).ConfigureAwait(false) ?? throw new InformationExistedException("The organization is invalid");

			var moduleID = filter.GetValue("RepositoryID") ?? requestInfo.GetParameter("RepositoryID") ?? requestInfo.GetParameter("x-module-id");
			var module = await (moduleID ?? "").GetModuleByIDAsync(cancellationToken).ConfigureAwait(false);
			if ((module == null && string.IsNullOrWhiteSpace(query)) || (module != null && !organization.ID.IsEquals(module.SystemID)))
				throw new InformationInvalidException("The module is invalid");

			var contentTypeID = filter.GetValue("RepositoryEntityID") ?? requestInfo.GetParameter("RepositoryEntityID") ?? requestInfo.GetParameter("x-content-type-id");
			var contentType = await (contentTypeID ?? "").GetContentTypeByIDAsync(cancellationToken).ConfigureAwait(false);
			if ((contentType == null && string.IsNullOrWhiteSpace(query)) || (contentType != null && (!organization.ID.IsEquals(contentType.SystemID) || (module != null && !module.ID.IsEquals(contentType.RepositoryID)))))
				throw new InformationInvalidException("The content-type is invalid");

			// check permission
			var gotRights = isSystemAdministrator || requestInfo.Session.User.IsViewer(contentType?.WorkingPrivileges, null, organization);
			if (!gotRights)
				throw new AccessDeniedException();

			// normalize filter
			if (filter == null || !(filter is FilterBys<Item>) || (filter as FilterBys<Item>).Children == null || (filter as FilterBys<Item>).Children.Count < 1)
				filter = ItemProcessor.GetItemsFilter(organization.ID, module?.ID, contentType?.ID);
			if (!requestInfo.Session.User.IsAuthenticated)
			{
				if (!(filter.GetChild("Status") is FilterBy<Item> filterByStatus))
					(filter as FilterBys<Item>).Add(Filters<Item>.Equals("Status", ApprovalStatus.Published.ToString()));
				else if (filterByStatus.Value == null || !(filterByStatus.Value as string).IsEquals(ApprovalStatus.Published.ToString()))
					filterByStatus.Value = ApprovalStatus.Published.ToString();
			}
			filter.Prepare(requestInfo);

			// process cache
			var cacheKeyOfObjectsJson = string.IsNullOrWhiteSpace(query) ? Extensions.GetCacheKeyOfObjectsJson(filter, sort, pageSize, pageNumber) : null;
			if (cacheKeyOfObjectsJson != null)
			{
				var json = await Utility.Cache.GetAsync<string>(cacheKeyOfObjectsJson, cancellationToken).ConfigureAwait(false);
				if (!string.IsNullOrWhiteSpace(json))
					return JObject.Parse(json);
			}

			// search if has no cache
			var (totalRecords, objects, thumbnails, cacheKeys) = await requestInfo.SearchAsync(query, filter, sort, pageSize, pageNumber, contentType?.ID, pagination.Item1 > -1 ? pagination.Item1 : -1, cancellationToken).ConfigureAwait(false);
			JToken attachments = null;
			var showAttachments = requestInfo.GetParameter("ShowAttachments") != null;
			if (objects.Count > 0 && showAttachments)
			{
				attachments = objects.Count == 1
					? await requestInfo.GetAttachmentsAsync(objects[0].ID, objects[0].Title.Url64Encode(), Utility.ValidationKey, cancellationToken).ConfigureAwait(false)
					: await requestInfo.GetAttachmentsAsync(objects.Select(@object => @object.ID).Join(","), objects.ToJObject("ID", @object => new JValue(@object.Title.Url64Encode())).ToString(Formatting.None), Utility.ValidationKey, cancellationToken).ConfigureAwait(false);
			}

			// build response
			var totalPages = new Tuple<long, int>(totalRecords, pageSize).GetTotalPages();
			if (totalPages > 0 && pageNumber > totalPages)
				pageNumber = totalPages;
			pagination = new Tuple<long, int, int, int>(totalRecords, totalPages, pageSize, pageNumber);

			var showURLs = requestInfo.GetParameter("ShowURLs") != null;
			var siteURL = showURLs ? organization.DefaultSite?.GetURL(requestInfo.GetHeaderParameter("x-srp-host"), requestInfo.GetParameter("x-url")) + "/" : null;

			var response = new JObject()
			{
				{ "FilterBy", filter.ToClientJson(query) },
				{ "SortBy", sort?.ToClientJson() },
				{ "Pagination", pagination.GetPagination() },
				{
					"Objects",
					objects.Select(@object => @object.ToJson(false, json =>
					{
						json["Thumbnails"] = (thumbnails == null ? null : objects.Count == 1 ? thumbnails : thumbnails[@object.ID])?.NormalizeURIs(organization.FakeFilesHttpURI);
						if (showAttachments)
							json["Attachments"] = (attachments == null ? null : objects.Count == 1 ? attachments : attachments[@object.ID])?.NormalizeURIs(organization.FakeFilesHttpURI);
						if (showURLs)
						{
							json["URL"] = organization.NormalizeURLs(@object.GetURL(), true, siteURL);
							json["Summary"] = @object.Summary?.NormalizeHTMLBreaks();
						}
					})).ToJArray()
				}
			};

			// update cache
			if (string.IsNullOrWhiteSpace(query))
			{
				cacheKeys = cacheKeys.Concat(new[] { cacheKeyOfObjectsJson }).ToList();
				Task.WhenAll
				(
					Utility.Cache.SetAsync(cacheKeyOfObjectsJson, response.ToString(Formatting.None)),
					contentType != null ? Utility.Cache.AddSetMembersAsync(contentType.GetSetCacheKey(), cacheKeys, Utility.CancellationToken) : Task.CompletedTask,
					Utility.IsCacheLogEnabled ? Utility.WriteLogAsync(requestInfo, $"Update cache when search CMS items\r\n- Cache key of JSON: {cacheKeyOfObjectsJson}\r\n{(contentType != null ? $"- Cache key of Content-Type's set: {contentType.GetSetCacheKey()}\r\n" : "")}- Related cache keys: {cacheKeys.Join(", ")}", "Caches") : Task.CompletedTask
				).Run();
			}

			// response
			return response;
		}

		internal static async Task<JObject> CreateItemAsync(this RequestInfo requestInfo, bool isSystemAdministrator, CancellationToken cancellationToken)
		{
			// prepare
			var request = requestInfo.GetBodyExpando();

			var organizationID = request.Get<string>("SystemID") ?? requestInfo.GetParameter("SystemID") ?? requestInfo.GetParameter("x-system-id") ?? requestInfo.GetParameter("OrganizationID");
			var organization = await (organizationID ?? "").GetOrganizationByIDAsync(cancellationToken).ConfigureAwait(false);
			if (organization == null)
				throw new InformationInvalidException("The organization is invalid");

			var moduleID = request.Get<string>("RepositoryID") ?? requestInfo.GetParameter("RepositoryID") ?? requestInfo.GetParameter("x-module-id") ?? requestInfo.GetParameter("ModuleID");
			var module = await (moduleID ?? "").GetModuleByIDAsync(cancellationToken).ConfigureAwait(false);
			if (module == null || !module.SystemID.IsEquals(organization.ID))
				throw new InformationInvalidException("The module is invalid");

			var contentTypeID = request.Get<string>("RepositoryEntityID") ?? requestInfo.GetParameter("RepositoryEntityID") ?? requestInfo.GetParameter("x-content-type-id") ?? requestInfo.GetParameter("ContentTypeID");
			var contentType = await (contentTypeID ?? "").GetContentTypeByIDAsync(cancellationToken).ConfigureAwait(false);
			if (contentType == null || !contentType.SystemID.IsEquals(organization.ID) || !contentType.RepositoryID.IsEquals(module.ID))
				throw new InformationInvalidException("The content-type is invalid");

			// check permission
			var gotRights = isSystemAdministrator || requestInfo.Session.User.IsContributor(contentType.WorkingPrivileges, null, organization);
			if (!gotRights)
				throw new AccessDeniedException();

			// get data
			var item = request.CreateItem("Privileges,Created,CreatedID,LastModified,LastModifiedID", obj =>
			{
				obj.ID = string.IsNullOrWhiteSpace(obj.ID) || !obj.ID.IsValidUUID() ? UtilityService.NewUUID : obj.ID;
				obj.SystemID = organization.ID;
				obj.RepositoryID = module.ID;
				obj.RepositoryEntityID = contentType.ID;
				obj.Created = obj.LastModified = DateTime.Now;
				obj.CreatedID = obj.LastModifiedID = requestInfo.Session.User.ID;
			});

			var existing = await Item.GetItemByAliasAsync(item.ContentType, item.Alias, cancellationToken).ConfigureAwait(false);
			if (existing != null && !existing.ID.IsEquals(item.ID))
				item.Alias = $"{item.Alias}-{DateTime.Now.ToUnixTimestamp()}-{UtilityService.GetRandomNumber()}";

			// create new
			await Item.CreateAsync(item, cancellationToken).ConfigureAwait(false);
			Utility.Cache.SetAsync(item.GetCacheKeyOfAliasedItem(), item.ID, Utility.CancellationToken).Run();

			// send update message
			var thumbnailsTask = requestInfo.GetThumbnailsAsync(item.ID, item.Title.Url64Encode(), Utility.ValidationKey, cancellationToken);
			var attachmentsTask = requestInfo.GetAttachmentsAsync(item.ID, item.Title.Url64Encode(), Utility.ValidationKey, cancellationToken);
			await Task.WhenAll(thumbnailsTask, attachmentsTask).ConfigureAwait(false);

			var response = item.ToJson(json =>
			{
				json.UpdateVersions(new List<VersionContent>());
				json["Thumbnails"] = thumbnailsTask.Result;
				json["Attachments"] = attachmentsTask.Result;
			});
			new UpdateMessage
			{
				Type = $"{requestInfo.ServiceName}#{item.GetObjectName()}#Create",
				DeviceID = "*",
				Data = response
			}.Send();

			// clear related cache & send notification
			Task.WhenAll
			(
				item.ClearRelatedCacheAsync(Utility.CancellationToken, requestInfo.CorrelationID),
				item.SendNotificationAsync("Create", item.ContentType.Notifications, ApprovalStatus.Draft, item.Status, requestInfo, Utility.CancellationToken),
				Utility.Cache.AddSetMemberAsync(item.ContentType.ObjectCacheKeys, item.GetCacheKey(), Utility.CancellationToken)
			).Run();
			return response;
		}

		internal static async Task<JObject> GetItemAsync(this RequestInfo requestInfo, bool isSystemAdministrator, CancellationToken cancellationToken)
		{
			// prepare
			var identity = requestInfo.GetObjectIdentity(true, true) ?? "";
			var item = identity.IsValidUUID()
				? await Item.GetAsync<Item>(identity, cancellationToken).ConfigureAwait(false)
				: await Item.GetItemByAliasAsync(requestInfo.GetParameter("RepositoryEntityID") ?? requestInfo.GetParameter("x-content-type-id"), identity, cancellationToken).ConfigureAwait(false);
			if (item == null)
				throw new InformationNotFoundException();
			if (item.Organization == null || item.Module == null || item.ContentType == null)
				throw new InformationInvalidException("The organization/module/item-type is invalid");

			// check permission
			var gotRights = isSystemAdministrator || requestInfo.Session.User.ID.IsEquals(item.Organization.OwnerID);
			if (!gotRights)
				gotRights = item.Status.Equals(ApprovalStatus.Published)
					? requestInfo.Session.User.IsViewer(item.WorkingPrivileges, item.ContentType.WorkingPrivileges, item.Organization)
					: requestInfo.Session.User.ID.IsEquals(item.CreatedID) || requestInfo.Session.User.IsEditor(item.WorkingPrivileges, item.ContentType.WorkingPrivileges, item.Organization);
			if (!gotRights)
				throw new AccessDeniedException();

			if (!identity.IsValidUUID())
				return new JObject
				{
					{ "ID", item.ID },
					{ "Title", item.Title },
					{ "Alias", item.Alias }
				};

			// refresh
			var isRefresh = "refresh".IsEquals(requestInfo.GetObjectIdentity());
			if (isRefresh)
			{
				await item.ClearRelatedCacheAsync(cancellationToken, requestInfo.CorrelationID).ConfigureAwait(false);
				await Utility.Cache.RemoveAsync(item, cancellationToken).ConfigureAwait(false);
				item = await Item.GetAsync<Item>(item.ID, cancellationToken).ConfigureAwait(false);
			}

			// store object cache key to clear related cached
			await Utility.Cache.AddSetMemberAsync(item.ContentType.ObjectCacheKeys, item.GetCacheKey(), cancellationToken).ConfigureAwait(false);

			// send update message
			var versions = await item.FindVersionsAsync(cancellationToken, false).ConfigureAwait(false);
			var thumbnailsTask = requestInfo.GetThumbnailsAsync(item.ID, item.Title.Url64Encode(), Utility.ValidationKey, cancellationToken);
			var attachmentsTask = requestInfo.GetAttachmentsAsync(item.ID, item.Title.Url64Encode(), Utility.ValidationKey, cancellationToken);
			await Task.WhenAll(thumbnailsTask, attachmentsTask).ConfigureAwait(false);
			var response = item.ToJson(json =>
			{
				json.UpdateVersions(versions);
				json["Thumbnails"] = thumbnailsTask.Result;
				json["Attachments"] = attachmentsTask.Result;
			});
			new UpdateMessage
			{
				Type = $"{requestInfo.ServiceName}#{item.GetObjectName()}#Update",
				Data = response,
				DeviceID = "*",
				ExcludedDeviceID = isRefresh ? "" : requestInfo.Session.DeviceID
			}.Send();
			return response;
		}

		internal static async Task<JObject> UpdateAsync(this Item item, RequestInfo requestInfo, ApprovalStatus oldStatus, CancellationToken cancellationToken, string @event = null)
		{
			// update
			await Item.UpdateAsync(item, requestInfo.Session.User.ID, cancellationToken).ConfigureAwait(false);
			Utility.Cache.SetAsync(item.GetCacheKeyOfAliasedItem(), item.ID, Utility.CancellationToken).Run();

			// send update message
			var versionsTask = item.FindVersionsAsync(cancellationToken, false);
			var thumbnailsTask = requestInfo.GetThumbnailsAsync(item.ID, item.Title.Url64Encode(), Utility.ValidationKey, cancellationToken);
			var attachmentsTask = requestInfo.GetAttachmentsAsync(item.ID, item.Title.Url64Encode(), Utility.ValidationKey, cancellationToken);
			await Task.WhenAll(versionsTask, thumbnailsTask, attachmentsTask).ConfigureAwait(false);

			var response = item.ToJson(json =>
			{
				json.UpdateVersions(versionsTask.Result);
				json["Thumbnails"] = thumbnailsTask.Result;
				json["Attachments"] = attachmentsTask.Result;
			});
			new UpdateMessage
			{
				Type = $"{requestInfo.ServiceName}#{item.GetObjectName()}#Update",
				DeviceID = "*",
				Data = response
			}.Send();

			// clear related cache & send notification
			Task.WhenAll
			(
				item.ClearRelatedCacheAsync(Utility.CancellationToken, requestInfo.CorrelationID),
				item.SendNotificationAsync(@event ?? "Update", item.ContentType.Notifications, oldStatus, item.Status, requestInfo, Utility.CancellationToken),
				Utility.Cache.AddSetMemberAsync(item.ContentType.ObjectCacheKeys, item.GetCacheKey(),	Utility.CancellationToken)
			).Run();
			return response;
		}

		internal static async Task<JObject> UpdateItemAsync(this RequestInfo requestInfo, bool isSystemAdministrator, CancellationToken cancellationToken)
		{
			// prepare
			var item = await Item.GetAsync<Item>(requestInfo.GetObjectIdentity() ?? "", cancellationToken).ConfigureAwait(false);
			if (item == null)
				throw new InformationNotFoundException();
			else if (item.Organization == null || item.Module == null || item.ContentType == null)
				throw new InformationInvalidException("The organization/module/item-type is invalid");

			// check permission
			var gotRights = isSystemAdministrator || requestInfo.Session.User.IsEditor(item.WorkingPrivileges, item.ContentType.WorkingPrivileges, item.Organization);
			if (!gotRights)
				gotRights = item.Status.Equals(ApprovalStatus.Draft) || item.Status.Equals(ApprovalStatus.Pending) || item.Status.Equals(ApprovalStatus.Rejected)
					? requestInfo.Session.User.ID.IsEquals(item.CreatedID)
					: requestInfo.Session.User.IsEditor(item.WorkingPrivileges, item.ContentType.WorkingPrivileges, item.Organization);
			if (!gotRights)
				throw new AccessDeniedException();

			// prepare data
			var oldAlias = item.Alias;
			var oldStatus = item.Status;
			item.Update(requestInfo.GetBodyExpando(), "ID,SystemID,RepositoryID,RepositoryEntityID,Privileges,Created,CreatedID,LastModified,LastModifiedID", _ =>
			{
				item.Alias = (string.IsNullOrWhiteSpace(item.Alias) ? oldAlias : item.Alias).NormalizeAlias();
				item.LastModified = DateTime.Now;
				item.LastModifiedID = requestInfo.Session.User.ID;
			});

			var existing = await Item.GetItemByAliasAsync(item.ContentType, item.Alias, cancellationToken).ConfigureAwait(false);
			if (existing != null && !existing.ID.IsEquals(item.ID))
				item.Alias = $"{item.Alias}-{DateTime.Now.ToUnixTimestamp()}-{UtilityService.GetRandomNumber()}";

			// update
			return await item.UpdateAsync(requestInfo, oldStatus, cancellationToken).ConfigureAwait(false);
		}

		internal static async Task<JObject> DeleteItemAsync(this RequestInfo requestInfo, bool isSystemAdministrator, CancellationToken cancellationToken)
		{
			// prepare
			var item = await Item.GetAsync<Item>(requestInfo.GetObjectIdentity() ?? "", cancellationToken).ConfigureAwait(false);
			if (item == null)
				throw new InformationNotFoundException();
			if (item.Organization == null || item.Module == null || item.ContentType == null)
				throw new InformationInvalidException("The organization/module/item-type is invalid");

			// check permission
			var gotRights = isSystemAdministrator || requestInfo.Session.User.IsModerator(item.WorkingPrivileges, item.ContentType.WorkingPrivileges, item.Organization);
			if (!gotRights)
				gotRights = item.Status.Equals(ApprovalStatus.Draft) || item.Status.Equals(ApprovalStatus.Pending) || item.Status.Equals(ApprovalStatus.Rejected)
					? requestInfo.Session.User.ID.IsEquals(item.CreatedID) || requestInfo.Session.User.IsEditor(item.WorkingPrivileges, item.ContentType.WorkingPrivileges, item.Organization)
					: requestInfo.Session.User.IsModerator(item.WorkingPrivileges, item.ContentType.WorkingPrivileges, item.Organization);
			if (!gotRights)
				throw new AccessDeniedException();

			// delete files
			try
			{
				await requestInfo.DeleteFilesAsync(item.SystemID, item.RepositoryEntityID, item.ID, Utility.ValidationKey, cancellationToken).ConfigureAwait(false);
			}
			catch (Exception ex)
			{
				await requestInfo.WriteErrorAsync(ex, $"Error occurred while deleting files => {ex.Message}", "CMS.Item").ConfigureAwait(false);
				throw;
			}

			// delete
			await Item.DeleteAsync<Item>(item.ID, requestInfo.Session.User.ID, cancellationToken).ConfigureAwait(false);

			// send update message
			var response = item.ToJson();
			new UpdateMessage
			{
				Type = $"{requestInfo.ServiceName}#{item.GetObjectName()}#Delete",
				DeviceID = "*",
				Data = response
			}.Send();

			// clear related cache & send notification
			Task.WhenAll
			(
				item.ClearRelatedCacheAsync(Utility.CancellationToken, requestInfo.CorrelationID),
				item.SendNotificationAsync("Delete", item.ContentType.Notifications, item.Status, item.Status, requestInfo, Utility.CancellationToken),
				Utility.Cache.AddSetMemberAsync(item.ContentType.ObjectCacheKeys, item.GetCacheKey(), Utility.CancellationToken)
			).Run();
			return response;
		}

		internal static async Task<JObject> GenerateAsync(RequestInfo requestInfo, bool isSystemAdministrator, CancellationToken cancellationToken)
		{
			// prepare
			var requestJson = requestInfo.BodyAsJson;
			var optionsJson = requestJson.Get("Options", new JObject());
			var options = optionsJson.ToExpandoObject();

			var organizationJson = requestJson.Get("Organization", new JObject());
			var moduleJson = requestJson.Get("Module", new JObject());
			var contentTypeJson = requestJson.Get("ContentType", new JObject());
			var expressionJson = requestJson.Get("Expression", new JObject());
			var desktopsJson = requestJson.Get("Desktops", new JObject());

			var paginationJson = requestJson.Get("Pagination", new JObject());
			var pageSize = paginationJson.Get("PageSize", 7);
			var pageNumber = paginationJson.Get("PageNumber", 1);
			var showPageLinks = paginationJson.Get("ShowPageLinks", true);
			var numberOfPageLinks = paginationJson.Get("NumberOfPageLinks", 7);

			var contentTypeID = contentTypeJson.Get<string>("ID");
			var parentIdentity = requestJson.Get<string>("ParentIdentity");
			parentIdentity = string.IsNullOrWhiteSpace(parentIdentity) ? null : parentIdentity.Trim();

			var cultureInfo = CultureInfo.GetCultureInfo(requestJson.Get("Language", "vi-VN"));
			var action = requestJson.Get<string>("Action");
			var isList = string.IsNullOrWhiteSpace(action) || "List".IsEquals(action);

			var desktop = desktopsJson.Get<string>("Specified");
			desktop = !string.IsNullOrWhiteSpace(desktop) ? desktop : desktopsJson.Get<string>("ContentType");
			desktop = !string.IsNullOrWhiteSpace(desktop) ? desktop : desktopsJson.Get<string>("Module");
			desktop = !string.IsNullOrWhiteSpace(desktop) ? desktop : desktopsJson.Get<string>("Default");

			JObject pagination = null, seoInfo, filterBy = null, sortBy = null;
			string coverURI = null, data = null, ids = null;

			var showThumbnails = options.Get("ShowThumbnails", options.Get("ShowThumbnail", false)) || options.Get("ShowPngThumbnails", false) || options.Get("ShowAsPngThumbnails", false) || options.Get("ShowBigThumbnails", false) || options.Get("ShowAsBigThumbnails", false);
			var pngThumbnails = options.Get("ThumbnailsAsPng", options.Get("ThumbnailAsPng", options.Get("ShowPngThumbnails", options.Get("ShowAsPngThumbnails", false))));
			var bigThumbnails = options.Get("ThumbnailsAsBig", options.Get("ThumbnailAsBig", options.Get("ShowBigThumbnails", options.Get("ShowAsBigThumbnails", false))));
			var thumbnailsWidth = options.Get("ThumbnailsWidth", options.Get("ThumbnailWidth", 0));
			var thumbnailsHeight = options.Get("ThumbnailsHeight", options.Get("ThumbnailHeight", 0));

			var showAttachments = options.Get("ShowAttachments", false);
			var showPagination = options.Get("ShowPagination", false);
			var forceCache = requestInfo.GetParameter("x-force-cache") != null || requestInfo.GetParameter("x-no-cache") != null;

			// generate list
			if (isList)
			{
				// check permission
				var contentType = await (contentTypeID ?? "").GetContentTypeByIDAsync(cancellationToken).ConfigureAwait(false);
				var gotRights = isSystemAdministrator || requestInfo.Session.User.IsViewer(contentType?.WorkingPrivileges, null, contentType?.Organization);
				if (!gotRights)
				{
					var organization = contentType?.Organization ?? await organizationJson.Get("ID", "").GetOrganizationByIDAsync(cancellationToken).ConfigureAwait(false);
					gotRights = requestInfo.Session.User.IsViewer(contentType?.WorkingPrivileges, null, organization);
				}
				if (!gotRights)
					throw new AccessDeniedException();

				// prepare filtering expression
				if (!(expressionJson.Get<JObject>("FilterBy").ToFilter<Item>() is FilterBys<Item> filter) || filter.Children == null || filter.Children.Count < 1)
					filter = Filters<Item>.And
					(
						Filters<Item>.Equals("SystemID", "@request.Body(Organization.ID)"),
						Filters<Item>.Equals("RepositoryID", "@request.Body(Module.ID)"),
						Filters<Item>.Equals("RepositoryEntityID", "@request.Body(ContentType.ID)"),
						Filters<Item>.Equals("Status", ApprovalStatus.Published.ToString())
					);

				if (filter.GetChild("RepositoryEntityID") == null && contentType != null)
					filter.Add(Filters<Item>.Equals("RepositoryEntityID", contentType.ID));

				if (filter.GetChild("Status") == null)
					filter.Add(Filters<Item>.Equals("Status", ApprovalStatus.Published.ToString()));

				filterBy = new JObject
				{
					{ "API", filter.ToJson().ToString(Formatting.None) },
				};
				filter.Prepare(requestInfo);
				filterBy["App"] = filter.ToClientJson().ToString(Formatting.None);

				// prepare sorting expression
				var sort = expressionJson.Get<JObject>("SortBy")?.ToSort<Item>() ?? Sorts<Item>.Descending("Created");
				sortBy = new JObject
				{
					{ "API", sort.ToJson().ToString(Formatting.None) },
					{ "App", sort.ToClientJson().ToString(Formatting.None) }
				};

				// prepare cache
				var cacheKey = Extensions.GetCacheKeyOfObjectsXml(filter, sort, pageSize, pageNumber, $":o#{optionsJson.ToString(Formatting.None).GenerateUUID()}");
				if (forceCache)
					await Utility.Cache.RemoveAsync(new[] { cacheKey, Extensions.GetCacheKeyOfTotalObjects(filter, sort), Extensions.GetCacheKey(filter, sort, pageSize, pageNumber) }, cancellationToken).ConfigureAwait(false);

				// get cache
				long totalRecords = 0;
				data = await Utility.Cache.GetAsync<string>(cacheKey, cancellationToken).ConfigureAwait(false);

				// process if has no cache
				if (string.IsNullOrWhiteSpace(data))
				{
					// search
					var results = await requestInfo.SearchAsync(null, filter, sort, pageSize, pageNumber, contentTypeID, -1, cancellationToken).ConfigureAwait(false);
					totalRecords = results.TotalRecords;
					var objects = results.Objects;
					var thumbnails = results.Thumbnails;

					// attachments
					JToken attachments = null;
					if (objects.Count > 0 && showAttachments)
						attachments = objects.Count == 1
							? await requestInfo.GetAttachmentsAsync(objects[0].ID, objects[0].Title.Url64Encode(), Utility.ValidationKey, cancellationToken).ConfigureAwait(false)
							: await requestInfo.GetAttachmentsAsync(objects.Select(@object => @object.ID).Join(","), objects.ToJObject("ID", @object => new JValue(@object.Title.Url64Encode())).ToString(Formatting.None), Utility.ValidationKey, cancellationToken).ConfigureAwait(false);

					// generate XML
					Exception exception = null;
					var dataXml = XElement.Parse("<Data/>");
					objects.ForEach(@object =>
					{
						// check
						if (exception != null)
							return;

						// generate
						try
						{
							dataXml.Add(@object.ToXml(false, cultureInfo, element =>
							{
								if (!string.IsNullOrWhiteSpace(@object.Summary))
									element.Element("Summary").Value = @object.Summary.NormalizeHTMLBreaks();
								element.Add(new XElement("URL", @object.GetURL(desktop, false, parentIdentity) ?? ""));
								element.AddThumbnail(thumbnails?.GetThumbnailURL(@object.ID, pngThumbnails, bigThumbnails, thumbnailsWidth, thumbnailsHeight), pngThumbnails);
								if (showAttachments)
								{
									var xmlAttachments = new XElement("Attachments");
									attachments?.GetAttachments(@object.ID)?.Select(attachment => new JObject
									{
										{ "Title", attachment["Title"] },
										{ "Filename", attachment["Filename"] },
										{ "Size", attachment["Size"] },
										{ "ContentType", attachment["ContentType"] },
										{ "Downloads", attachment["Downloads"] },
										{ "URIs", attachment["URIs"] }
									}).ForEach(attachment => xmlAttachments.Add(attachment.ToXml("Attachment", x => x.Element("Size").UpdateNumber(false, cultureInfo))));
									element.Add(xmlAttachments);
								}
							}));
						}
						catch (Exception ex)
						{
							exception = requestInfo.GetRuntimeException(ex, null, async (msg, exc) => await requestInfo.WriteErrorAsync(exc, $"Error occurred while generating an item => {msg} : {@object.ToJson()}", "Errors").ConfigureAwait(false));
						}
					});

					// get data
					data = dataXml.CleanInvalidCharacters().ToString(SaveOptions.DisableFormatting);

					// update cache
					await Task.WhenAll
					(
						Utility.Cache.SetAsync(cacheKey, data, cancellationToken),
						contentType != null ? Utility.Cache.AddSetMembersAsync(contentType.GetSetCacheKey(), results.CacheKeys.Concat(new[] { cacheKey }), cancellationToken) : Task.CompletedTask,
						Utility.IsCacheLogEnabled ? Utility.WriteLogAsync(requestInfo, $"Update related keys into Content-Type's set when generate collection of CMS.Item [{contentType?.Title} - ID: {contentType?.ID} - Set: {contentType?.GetSetCacheKey()}]\r\n- Related cache keys ({results.Item4.Count + 1}): {results.Item4.Concat(new[] { cacheKey }).Join(", ")}", "Caches") : Task.CompletedTask
					).ConfigureAwait(false);
				}
				else if (showPagination)
				{
					var cacheKeyOfTotalObjects = Extensions.GetCacheKeyOfTotalObjects(filter, sort);
					totalRecords = await Utility.Cache.GetAsync<long>(cacheKeyOfTotalObjects, cancellationToken).ConfigureAwait(false);
					if (totalRecords < 1)
					{
						await Utility.Cache.RemoveAsync(cacheKeyOfTotalObjects, cancellationToken).ConfigureAwait(false);
						totalRecords = await Item.CountAsync(filter, contentTypeID, cacheKeyOfTotalObjects, cancellationToken).ConfigureAwait(false);
						if (contentType != null)
							await Utility.Cache.AddSetMemberAsync(contentType.GetSetCacheKey(), cacheKeyOfTotalObjects, cancellationToken).ConfigureAwait(false);
					}
				}

				// prepare pagination
				if (showPagination)
				{
					var totalPages = new Tuple<long, int>(totalRecords, pageSize).GetTotalPages();
					if (totalPages > 0 && pageNumber > totalPages)
						pageNumber = totalPages;
					pagination = Utility.GeneratePagination(totalRecords, totalPages, pageSize, pageNumber, $"~/{desktop ?? "-default"}/{parentIdentity ?? contentType?.Title.GetANSIUri() ?? "-"}" + "/{{pageNumber}}" + $"{(organizationJson.Get<bool>("AlwaysUseHtmlSuffix", true) ? ".html" : "")}", showPageLinks, numberOfPageLinks, requestInfo.Query?.Where(kvp => kvp.Key.IsStartsWith("ngx-")).Select(kvp => $"{kvp.Key}={kvp.Value?.UrlEncode()}").Join("&"));
				}

				// prepare SEO info
				seoInfo = new JObject
				{
					{ "Title", contentType?.Title },
					{ "Description", contentType?.Description }
				};

				// other info
				ids = "system:" + (contentType != null ? $"\"{contentType.SystemID}\"" : null) + ","
					+ "repository:" + (contentType != null ? $"\"{contentType.RepositoryID}\"" : null) + ","
					+ "entity:" + (contentType != null ? $"\"{contentType.ID}\"" : null);
			}

			// generate details
			else
			{
				// get the requested object
				var @object = await Item.GetItemByAliasAsync(contentTypeID, requestJson.Get<string>("ContentIdentity"), cancellationToken).ConfigureAwait(false);
				if (@object == null)
					throw new InformationNotFoundException();
				if (@object.Organization == null || @object.Module == null || @object.ContentType == null)
					throw new InformationInvalidException("The organization/module/content-type is invalid");

				// check permission
				var gotRights = isSystemAdministrator || requestInfo.Session.User.ID.IsEquals(@object.Organization.OwnerID) || @object.Status.Equals(ApprovalStatus.Published)
					? requestInfo.Session.User.IsViewer(@object.WorkingPrivileges, @object.ContentType.WorkingPrivileges, @object.Organization)
					: requestInfo.Session.User.ID.IsEquals(@object.CreatedID) || requestInfo.Session.User.IsEditor(@object.WorkingPrivileges, @object.ContentType.WorkingPrivileges, @object.Organization);
				if (!gotRights)
					throw new AccessDeniedException();

				// get cache
				Task<JToken> thumbnailsTask = null;
				var cacheKey = $"{@object.ID}:xml:o#{optionsJson.ToString(Formatting.None).GenerateUUID()}:p#{paginationJson.ToString(Formatting.None).GenerateUUID()}";
				data = forceCache ? null : await Utility.Cache.GetAsync<string>(cacheKey, cancellationToken).ConfigureAwait(false);

				// process if has no cache
				if (string.IsNullOrWhiteSpace(data))
				{
					// get other contents
					var showOthers = options.Get("ShowOthers", false);
					Task<List<Item>> newersTask, oldersTask;
					if (showOthers)
					{
						var numberOfOthers = options.Get("NumberOfOthers", 10) / 2;

						newersTask = Item.FindAsync(Filters<Item>.And
						(
							Filters<Item>.Equals("RepositoryEntityID", "@request.Body(ContentType.ID)"),
							Filters<Item>.Equals("Status", ApprovalStatus.Published.ToString()),
							Filters<Item>.GreaterOrEquals("Created", @object.Created)
						).Prepare(requestInfo), null, numberOfOthers, 1, contentTypeID, null, cancellationToken);

						oldersTask = Item.FindAsync(Filters<Item>.And
						(
							Filters<Item>.Equals("RepositoryEntityID", "@request.Body(ContentType.ID)"),
							Filters<Item>.Equals("Status", ApprovalStatus.Published.ToString()),
							Filters<Item>.LessThanOrEquals("Created", @object.Created)
						).Prepare(requestInfo), null, numberOfOthers, 1, contentTypeID, null, cancellationToken);
					}
					else
					{
						newersTask = Task.FromResult(new List<Item>());
						oldersTask = Task.FromResult(new List<Item>());
					}

					// get files
					requestInfo.Header["x-thumbnails-as-attachments"] = "true";
					thumbnailsTask = showThumbnails ? requestInfo.GetThumbnailsAsync(@object.ID, @object.Title.Url64Encode(), Utility.ValidationKey, cancellationToken) : Task.FromResult<JToken>(new JArray());
					var attachmentsTask = showAttachments ? requestInfo.GetAttachmentsAsync(@object.ID, @object.Title.Url64Encode(), Utility.ValidationKey, cancellationToken) : Task.FromResult<JToken>(new JArray());

					// wait for all tasks are completed
					await Task.WhenAll(newersTask, oldersTask, thumbnailsTask, attachmentsTask).ConfigureAwait(false);

					var others = new List<Item>();
					JToken otherThumbnails = null;
					if (showOthers)
					{
						newersTask.Result.ForEach(other => others.Add(other));
						oldersTask.Result.ForEach(other => others.Add(other));
						others = others.Where(other => other.ID != @object.ID).OrderByDescending(other => other.Created).ToList();
						otherThumbnails = others.Count < 1
							? null
							: others.Count == 1
								? await requestInfo.GetThumbnailsAsync(others[0].ID, others[0].Title.Url64Encode(), Utility.ValidationKey, cancellationToken).ConfigureAwait(false)
								: await requestInfo.GetThumbnailsAsync(others.Select(obj => obj.ID).Join(","), others.ToJObject("ID", obj => new JValue(obj.Title.Url64Encode())).ToString(Formatting.None), Utility.ValidationKey, cancellationToken).ConfigureAwait(false);
					}

					// generate XML
					var dataXml = XElement.Parse("<Data/>");
					dataXml.Add(@object.ToXml(false, cultureInfo, element =>
					{
						element.NormalizeHTMLs(@object);

						if (!string.IsNullOrWhiteSpace(@object.Tags))
						{
							var tagsXml = element.Element("Tags");
							tagsXml.Value = "";
							@object.Tags.ToArray(",", true).ForEach(tag => tagsXml.Add(new XElement("Tag", tag)));
						}

						if (!string.IsNullOrWhiteSpace(@object.Summary))
							element.Element("Summary").Value = @object.Summary.NormalizeHTMLBreaks();

						element.Add(new XElement("URL", @object.GetURL(desktop, false, parentIdentity)));

						if (showThumbnails)
						{
							var thumbnails = new XElement("Thumbnails");
							(thumbnailsTask.Result as JArray)?.ForEach(thumbnail => thumbnails.Add((thumbnail.Get<string>("URI")?.GetThumbnailURL(pngThumbnails, bigThumbnails, thumbnailsWidth, thumbnailsHeight) ?? "").GetThumbnail(pngThumbnails, "Thumbnail")));
							element.Add(thumbnails);
						}

						if (showAttachments)
						{
							var attachments = new XElement("Attachments");
							(attachmentsTask.Result as JArray)?.Select(attachment => new JObject
							{
								{ "Title", attachment["Title"] },
								{ "Filename", attachment["Filename"] },
								{ "Size", attachment["Size"] },
								{ "ContentType", attachment["ContentType"] },
								{ "Downloads", attachment["Downloads"] },
								{ "URIs", attachment["URIs"] }
							}).ForEach(attachment => attachments.Add(attachment.ToXml("Attachment", x => x.Element("Size").UpdateNumber(false, cultureInfo))));
							element.Add(attachments);
						}
					}));

					if (showOthers)
					{
						var othersXml = new XElement("Others");
						others.ForEach(other => othersXml.Add(other.ToXml(false, cultureInfo, otherXml =>
						{
							otherXml.Add(new XElement("URL", other.GetURL(desktop, false, parentIdentity) ?? ""));
							otherXml.AddThumbnail(otherThumbnails?.GetThumbnailURL(other.ID, pngThumbnails, bigThumbnails, thumbnailsWidth, thumbnailsHeight), pngThumbnails);
						})));
						dataXml.Add(othersXml);
					}

					// get xml data
					data = dataXml.CleanInvalidCharacters().ToString(SaveOptions.DisableFormatting);

					// update cache
					await Task.WhenAll
					(
						Utility.Cache.SetAsync(cacheKey, data, cancellationToken),
						@object.ContentType != null ? Utility.Cache.AddSetMemberAsync(@object.ContentType.ObjectCacheKeys, @object.GetCacheKey(), cancellationToken) : Task.CompletedTask,
						@object.ContentType != null ? Utility.Cache.AddSetMembersAsync(@object.ContentType.GetSetCacheKey(), new[] { cacheKey }, cancellationToken) : Task.CompletedTask,
						Utility.IsCacheLogEnabled ? Utility.WriteLogAsync(requestInfo, $"Update related keys into Content-Type's set when generate details of CMS.Item [{@object.ContentType?.Title} - ID: {@object.ContentType?.ID} - Set: {@object.ContentType?.GetSetCacheKey()}]\r\n- Related cache keys (1): {cacheKey}", "Caches") : Task.CompletedTask
					).ConfigureAwait(false);
				}

				// build others
				pagination = showPagination ? Utility.GeneratePagination(1, 1, 0, pageNumber, @object.GetURL(desktop, true), showPageLinks, numberOfPageLinks) : null;
				seoInfo = new JObject
				{
					{ "Title", @object.Title },
					{ "Description", @object.Summary },
					{ "Keywords", @object.Tags }
				};

				thumbnailsTask = thumbnailsTask ?? requestInfo.GetThumbnailsAsync(@object.ID, @object.Title.Url64Encode(), Utility.ValidationKey, cancellationToken);
				await thumbnailsTask.ConfigureAwait(false);
				coverURI = (thumbnailsTask.Result as JArray)?.First()?.Get<string>("URI");
				ids = $"system:\"{@object.SystemID}\",repository:\"{@object.RepositoryID}\",entity:\"{@object.RepositoryEntityID}\",id:\"{@object.ID}\"";
			}

			// response
			var contentTypeDefinitionJson = requestJson.Get<JObject>("ContentTypeDefinition");
			var moduleDefinitionJson = requestJson.Get<JObject>("ModuleDefinition");
			return new JObject
			{
				{ "Data", data },
				{ "Pagination", pagination },
				{ "FilterBy", filterBy },
				{ "SortBy", sortBy },
				{ "SEOInfo", seoInfo },
				{ "CoverURI", coverURI },
				{ "IDs", ids + $",service:\"{moduleDefinitionJson.Get<string>("ServiceName").ToLower()}\",object:\"{contentTypeDefinitionJson.Get<string>("ObjectNamePrefix")?.ToLower()}{contentTypeDefinitionJson.Get<string>("ObjectName").ToLower()}{contentTypeDefinitionJson.Get<string>("ObjectNameSuffix")?.ToLower()}\"" }
			};
		}

		internal static async Task<JObject> SyncItemAsync(this RequestInfo requestInfo, CancellationToken cancellationToken, bool sendNotifications = false, bool dontCreateNewVersion = false)
		{
			// prepare
			var @event = requestInfo.GetParameter("event") ?? requestInfo.GetParameter("x-original-event");
			if (string.IsNullOrWhiteSpace(@event) || !@event.IsEquals("Delete"))
				@event = "Update";

			var data = requestInfo.GetBodyExpando();
			var item = await Item.GetAsync<Item>(data.Get<string>("ID"), cancellationToken).ConfigureAwait(false);
			var oldStatus = item != null ? item.Status : ApprovalStatus.Pending;

			// sync
			if (!@event.IsEquals("Delete"))
			{
				if (item == null)
				{
					item = data.CreateItem();
					var existing = await Item.GetItemByAliasAsync(item.ContentType, item.Alias, cancellationToken).ConfigureAwait(false);
					if (existing != null)
						item.Alias = $"{item.Alias}-{DateTime.Now.ToUnixTimestamp()}-{UtilityService.GetRandomNumber()}";
					await Item.CreateAsync(item, cancellationToken).ConfigureAwait(false);
				}
				else
				{
					item.Update(data);
					var existing = await Item.GetItemByAliasAsync(item.ContentType, item.Alias, cancellationToken).ConfigureAwait(false);
					if (existing != null && !existing.ID.IsEquals(item.ID))
						item.Alias = $"{item.Alias}-{DateTime.Now.ToUnixTimestamp()}-{UtilityService.GetRandomNumber()}";
					await Item.UpdateAsync(item, dontCreateNewVersion, cancellationToken).ConfigureAwait(false);
				}
			}
			else if (item != null)
				await Item.DeleteAsync<Item>(item.ID, item.LastModifiedID, cancellationToken).ConfigureAwait(false);

			// stop if has no info
			if (item == null)
				return new JObject();

			// update cache & send notification
			if (@event.IsEquals("Delete"))
				await Utility.Cache.RemoveSetMemberAsync(item.ContentType.ObjectCacheKeys, item.GetCacheKey(), cancellationToken).ConfigureAwait(false);
			else
				await Utility.Cache.AddSetMemberAsync(item.ContentType.ObjectCacheKeys, item.GetCacheKey(), cancellationToken).ConfigureAwait(false);

			await Task.WhenAll
			(
				item.ClearRelatedCacheAsync(cancellationToken, requestInfo.CorrelationID),
				sendNotifications ? item.SendNotificationAsync(@event, item.ContentType.Notifications, oldStatus, item.Status, requestInfo, cancellationToken) : Task.CompletedTask
			).ConfigureAwait(false);

			// send update message
			var response = item.ToJson();
			if (!@event.IsEquals("Delete"))
			{
				var thumbnailsTask = requestInfo.GetThumbnailsAsync(item.ID, item.Title.Url64Encode(), Utility.ValidationKey, cancellationToken);
				var attachmentsTask = requestInfo.GetAttachmentsAsync(item.ID, item.Title.Url64Encode(), Utility.ValidationKey, cancellationToken);
				var versionsTask = item.FindVersionsAsync(cancellationToken, false);
				await Task.WhenAll(thumbnailsTask, attachmentsTask, versionsTask).ConfigureAwait(false);
				response.UpdateVersions(versionsTask.Result);
				response["Thumbnails"] = thumbnailsTask.Result;
				response["Attachments"] = attachmentsTask.Result;
				response["URL"] = item.GetURL();
			}
			new UpdateMessage
			{
				Type = $"{requestInfo.ServiceName}#{item.GetObjectName()}#{@event}",
				Data = response,
				DeviceID = "*"
			}.Send();
			return response;
		}

		internal static async Task<JToken> RollbackItemAsync(this RequestInfo requestInfo, bool isSystemAdministrator, CancellationToken cancellationToken)
		{
			// prepare
			var item = await Item.GetAsync<Item>(requestInfo.GetObjectIdentity() ?? "", cancellationToken).ConfigureAwait(false);
			if (item == null)
				throw new InformationNotFoundException();
			else if (item.Organization == null || item.Module == null || item.ContentType == null)
				throw new InformationInvalidException("The organization/module/item-type is invalid");

			// check permission
			var gotRights = isSystemAdministrator || requestInfo.Session.User.IsEditor(item.WorkingPrivileges, item.ContentType.WorkingPrivileges, item.Organization);
			if (!gotRights)
				gotRights = item.Status.Equals(ApprovalStatus.Draft) || item.Status.Equals(ApprovalStatus.Pending) || item.Status.Equals(ApprovalStatus.Rejected)
					? requestInfo.Session.User.ID.IsEquals(item.CreatedID)
					: requestInfo.Session.User.IsEditor(item.WorkingPrivileges, item.ContentType.WorkingPrivileges, item.Organization);
			if (!gotRights)
				throw new AccessDeniedException();

			// rollback
			var oldStatus = item.Status;
			item = await RepositoryMediator.RollbackAsync<Item>(requestInfo.GetParameter("x-version-id") ?? "", requestInfo.Session.User.ID, cancellationToken).ConfigureAwait(false);

			// update cache & send notification
			await Task.WhenAll
			(
				item.ClearRelatedCacheAsync(cancellationToken, requestInfo.CorrelationID),
				item.SendNotificationAsync("Rollback", item.ContentType.Notifications, oldStatus, item.Status, requestInfo, cancellationToken),
				Utility.Cache.SetAsync(item, cancellationToken)
			).ConfigureAwait(false);

			// send update message
			var thumbnailsTask = requestInfo.GetThumbnailsAsync(item.ID, item.Title.Url64Encode(), Utility.ValidationKey, cancellationToken);
			var attachmentsTask = requestInfo.GetAttachmentsAsync(item.ID, item.Title.Url64Encode(), Utility.ValidationKey, cancellationToken);
			var versionsTask = item.FindVersionsAsync(cancellationToken, false);
			await Task.WhenAll(thumbnailsTask, attachmentsTask, versionsTask).ConfigureAwait(false);
			var response = item.ToJson(json =>
			{
				json.UpdateVersions(versionsTask.Result);
				json["Thumbnails"] = thumbnailsTask.Result;
				json["Attachments"] = attachmentsTask.Result;
				json["URL"] = item.GetURL();
			});
			new UpdateMessage
			{
				Type = $"{requestInfo.ServiceName}#{item.GetObjectName()}#Update",
				Data = response,
				DeviceID = "*"
			}.Send();
			return response;
		}
	}
}
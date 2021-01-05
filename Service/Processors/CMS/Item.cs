#region Related components
using System;
using System.Linq;
using System.Xml.Linq;
using System.Dynamic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using net.vieapps.Components.Security;
using net.vieapps.Components.Repository;
using net.vieapps.Components.Utility;
#endregion

namespace net.vieapps.Services.Portals
{
	public static class ItemProcessor
	{
		public static Item CreateItemInstance(this ExpandoObject data, string excluded = null, Action<Item> onCompleted = null)
			=> Item.CreateInstance(data, excluded?.ToHashSet(), item =>
			{
				item.NormalizeHTMLs();
				item.Tags = item.Tags?.Replace(";", ",").ToList(",", true).Where(tag => !string.IsNullOrWhiteSpace(tag)).Join(",");
				item.Tags = string.IsNullOrWhiteSpace(item.Tags) ? null : item.Tags;
				onCompleted?.Invoke(item);
			});

		public static Item UpdateItemInstance(this Item item, ExpandoObject data, string excluded = null, Action<Item> onCompleted = null)
			=> item.Fill(data, excluded?.ToHashSet(), _ =>
			{
				item.NormalizeHTMLs();
				item.Tags = item.Tags?.Replace(";", ",").ToList(",", true).Where(tag => !string.IsNullOrWhiteSpace(tag)).Join(",");
				item.Tags = string.IsNullOrWhiteSpace(item.Tags) ? null : item.Tags;
				onCompleted?.Invoke(item);
			});

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

		internal static async Task ClearRelatedCacheAsync(this Item item, SortBy<Item> sort, CancellationToken cancellationToken = default, string correlationID = null, int pageSize = 0)
		{
			// cache keys of the individual content
			sort = sort ?? Sorts<Item>.Descending("Created").ThenByAscending("Title");
			var dataCacheKeys = Extensions.GetRelatedCacheKeys(item.GetCacheKey(), pageSize).Concat(Extensions.GetRelatedCacheKeys(ItemProcessor.GetItemsFilter(item.SystemID, item.RepositoryID, item.RepositoryEntityID), sort, pageSize)).ToList();

			// cache keys of the content-type
			if (item.ContentType != null)
				dataCacheKeys = (await Utility.Cache.GetSetMembersAsync(item.ContentType.GetSetCacheKey(), cancellationToken).ConfigureAwait(false)).Concat(dataCacheKeys).ToList();

			dataCacheKeys = dataCacheKeys.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

			var desktop = item.ContentType?.Desktop;
			var htmlCacheKeys = desktop != null
				? await Utility.Cache.GetSetMembersAsync(desktop.GetSetCacheKey(), cancellationToken).ConfigureAwait(false)
				: new HashSet<string>();

			if (desktop != null)
			{
				var desktopCacheKey = desktop.GetDesktopCacheKey(item.GetURL());
				htmlCacheKeys.Append(new[] { desktopCacheKey, $"{desktopCacheKey}:time" });
				var desktopURL = $"~/{desktop.Alias}/{item.ContentType?.Title.GetANSIUri() ?? "-"}" + "/{{pageNumber}}";
				for (var page = 1; page <= 10; page++)
				{
					desktopCacheKey = desktop.GetDesktopCacheKey(desktopURL.Replace(StringComparison.OrdinalIgnoreCase, "/{{pageNumber}}", page > 1 ? $"/{page}" : ""));
					htmlCacheKeys.Append(new[] { desktopCacheKey, $"{desktopCacheKey}:time" });
				}
			}

			htmlCacheKeys.Append(item.Organization.GetDesktopCacheKey());

			if (Utility.Logger.IsEnabled(LogLevel.Debug))
				await Utility.WriteLogAsync(correlationID, $"Clear related cache of CMS item [{item.ID} => {item.Title}]\r\n- {dataCacheKeys.Count} data keys => {dataCacheKeys.Join(", ")}\r\n- {htmlCacheKeys.Count} html keys => {htmlCacheKeys.Join(", ")}", CancellationToken.None, "Caches").ConfigureAwait(false);
			await Utility.Cache.RemoveAsync(htmlCacheKeys.Concat(dataCacheKeys).Distinct(StringComparer.OrdinalIgnoreCase).ToList(), cancellationToken).ConfigureAwait(false);

			await Task.WhenAll
			(
				item.Status != ApprovalStatus.Published ? Task.CompletedTask : item.GetURL().Replace("~/", $"{Utility.PortalsHttpURI}/~{item.Organization.Alias}/").RefreshWebPageAsync(1, correlationID),
				$"{Utility.PortalsHttpURI}/~{item.Organization.Alias}/{desktop?.Alias ?? "-default"}/{item.ContentType?.Title.GetANSIUri() ?? "-"}".RefreshWebPageAsync(1, correlationID),
				$"{Utility.PortalsHttpURI}/~{item.Organization.Alias}/".RefreshWebPageAsync(1, correlationID)
			).ConfigureAwait(false);
		}

		internal static Task ClearRelatedCacheAsync(this Item item, SortBy<Item> sort, string correlationID, int pageSize = 0)
			=> item.ClearRelatedCacheAsync(sort, CancellationToken.None, correlationID, pageSize);

		static async Task<Tuple<long, List<Item>, JToken, List<string>>> SearchAsync(this RequestInfo requestInfo, string query, IFilterBy<Item> filter, SortBy<Item> sort, int pageSize, int pageNumber, string contentTypeID = null, long totalRecords = -1, CancellationToken cancellationToken = default, bool searchThumbnails = true)
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
					: await Item.SearchAsync(query, filter, pageSize, pageNumber, contentTypeID, cancellationToken).ConfigureAwait(false)
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
				Utility.SetCacheOfPageSizeAsync(filter, sort, pageSize, cancellationToken).Run();

			// return the results
			return new Tuple<long, List<Item>, JToken, List<string>>(totalRecords, objects, thumbnails, cacheKeys);
		}

		internal static async Task<JObject> SearchItemsAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, CancellationToken cancellationToken = default)
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
			var organization = await (organizationID ?? "").GetOrganizationByIDAsync(cancellationToken).ConfigureAwait(false);
			if (organization == null)
				throw new InformationExistedException("The organization is invalid");

			var moduleID = filter.GetValue("RepositoryID") ?? requestInfo.GetParameter("RepositoryID") ?? requestInfo.GetParameter("x-module-id");
			var module = await (moduleID ?? "").GetModuleByIDAsync(cancellationToken).ConfigureAwait(false);
			if (module == null || !module.SystemID.IsEquals(organization.ID))
				throw new InformationInvalidException("The module is invalid");

			var contentTypeID = filter.GetValue("RepositoryEntityID") ?? requestInfo.GetParameter("RepositoryEntityID") ?? requestInfo.GetParameter("x-content-type-id");
			var contentType = await (contentTypeID ?? "").GetContentTypeByIDAsync(cancellationToken).ConfigureAwait(false);
			if (contentType == null || !contentType.SystemID.IsEquals(organization.ID) || !contentType.RepositoryID.IsEquals(module.ID))
				throw new InformationInvalidException("The content-type is invalid");

			// check permission
			var gotRights = isSystemAdministrator || requestInfo.Session.User.ID.IsEquals(organization.OwnerID) || requestInfo.Session.User.IsViewer(contentType.WorkingPrivileges);
			if (!gotRights)
				throw new AccessDeniedException();

			// normalize filter
			if (filter == null || !(filter is FilterBys<Item>) || (filter as FilterBys<Item>).Children == null || (filter as FilterBys<Item>).Children.Count < 1)
				filter = ItemProcessor.GetItemsFilter(organization.ID, module.ID, contentType.ID);
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
			var results = await requestInfo.SearchAsync(query, filter, sort, pageSize, pageNumber, contentType.ID, pagination.Item1 > -1 ? pagination.Item1 : -1, cancellationToken).ConfigureAwait(false);
			var totalRecords = results.Item1;
			var objects = results.Item2;
			var thumbnails = results.Item3;

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

			var response = new JObject()
			{
				{ "FilterBy", filter.ToClientJson(query) },
				{ "SortBy", sort?.ToClientJson() },
				{ "Pagination", pagination.GetPagination() },
				{
					"Objects",
					objects.Select(@object => @object.ToJson(false, cjson =>
					{
						cjson["Thumbnails"] = thumbnails == null ? null : objects.Count == 1 ? thumbnails : thumbnails[@object.ID];
						if (showAttachments)
							cjson["Attachments"] = attachments == null ? null : objects.Count == 1 ? attachments : attachments[@object.ID];
					})).ToJArray()
				}
			};

			// update cache
			if (string.IsNullOrWhiteSpace(query))
			{
				await Utility.Cache.SetAsync(cacheKeyOfObjectsJson, response.ToString(Formatting.None)).ConfigureAwait(false);
				var cacheKeys = new[] { cacheKeyOfObjectsJson }.Concat(results.Item4).ToList();
				Task.WhenAll(
					Utility.Cache.AddSetMembersAsync(contentType.GetSetCacheKey(), cacheKeys),
					Utility.Logger.IsEnabled(LogLevel.Debug) ? Utility.WriteLogAsync(requestInfo, $"Update cache when search CMS items\r\n- Cache key of JSON: {cacheKeyOfObjectsJson}\r\n- Cache key of realated sets: {contentType.GetSetCacheKey()}\r\n- Related cache keys: {cacheKeys.Join(", ")}", CancellationToken.None, "Caches") : Task.CompletedTask
				).Run();
			}

			// response
			return response;
		}

		internal static async Task<JObject> CreateItemAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, CancellationToken cancellationToken = default)
		{
			// prepare
			var request = requestInfo.GetBodyExpando();

			var organizationID = request.Get<string>("SystemID") ?? requestInfo.GetParameter("SystemID") ?? requestInfo.GetParameter("x-system-id");
			var organization = await (organizationID ?? "").GetOrganizationByIDAsync(cancellationToken).ConfigureAwait(false);
			if (organization == null)
				throw new InformationInvalidException("The organization is invalid");

			var moduleID = request.Get<string>("RepositoryID") ?? requestInfo.GetParameter("RepositoryID") ?? requestInfo.GetParameter("x-module-id");
			var module = await (moduleID ?? "").GetModuleByIDAsync(cancellationToken).ConfigureAwait(false);
			if (module == null || !module.SystemID.IsEquals(organization.ID))
				throw new InformationInvalidException("The module is invalid");

			var contentTypeID = request.Get<string>("RepositoryEntityID") ?? requestInfo.GetParameter("RepositoryEntityID") ?? requestInfo.GetParameter("x-content-type-id");
			var contentType = await (contentTypeID ?? "").GetContentTypeByIDAsync(cancellationToken).ConfigureAwait(false);
			if (contentType == null || !contentType.SystemID.IsEquals(organization.ID) || !contentType.RepositoryID.IsEquals(module.ID))
				throw new InformationInvalidException("The content-type is invalid");

			// check permission
			var gotRights = isSystemAdministrator || requestInfo.Session.User.ID.IsEquals(organization.OwnerID) || requestInfo.Session.User.IsContributor(contentType.WorkingPrivileges);
			if (!gotRights)
				throw new AccessDeniedException();

			// get data
			var item = request.CreateItemInstance("Privileges,Created,CreatedID,LastModified,LastModifiedID", obj =>
			{
				obj.SystemID = organization.ID;
				obj.RepositoryID = module.ID;
				obj.RepositoryEntityID = contentType.ID;
				obj.ID = string.IsNullOrWhiteSpace(obj.ID) || !obj.ID.IsValidUUID() ? UtilityService.NewUUID : obj.ID;
				obj.Created = obj.LastModified = DateTime.Now;
				obj.CreatedID = obj.LastModifiedID = requestInfo.Session.User.ID;
			});

			item.Alias = string.IsNullOrWhiteSpace(item.Alias) ? item.Title.NormalizeAlias() : item.Alias.NormalizeAlias();
			var existing = await Item.GetItemByAliasAsync(contentType, item.Alias, cancellationToken).ConfigureAwait(false);
			if (existing != null)
				item.Alias += $"-{DateTime.Now.ToUnixTimestamp()}";

			// create new
			await Item.CreateAsync(item, cancellationToken).ConfigureAwait(false);
			await Utility.Cache.SetAsync($"e:{item.ContentTypeID}#a:{item.Alias.GenerateUUID()}".GetCacheKey<Item>(), item.ID, cancellationToken).ConfigureAwait(false);
			item.ClearRelatedCacheAsync(null, requestInfo.CorrelationID).Run();

			// send update message
			var response = item.ToJson();
			response["Thumbnails"] = await requestInfo.GetThumbnailsAsync(item.ID, item.Title.Url64Encode(), Utility.ValidationKey, cancellationToken).ConfigureAwait(false);
			response["Attachments"] = await requestInfo.GetAttachmentsAsync(item.ID, item.Title.Url64Encode(), Utility.ValidationKey, cancellationToken).ConfigureAwait(false);
			await Utility.RTUService.SendUpdateMessageAsync(new UpdateMessage
			{
				Type = $"{requestInfo.ServiceName}#{item.GetObjectName()}#Create",
				DeviceID = "*",
				Data = response
			}, cancellationToken).ConfigureAwait(false);

			// send notification
			item.SendNotificationAsync("Create", item.ContentType.Notifications, ApprovalStatus.Draft, item.Status, requestInfo, cancellationToken).Run();

			// response
			return response;
		}

		internal static async Task<JObject> GetItemAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, CancellationToken cancellationToken = default)
		{
			// prepare
			var identity = requestInfo.GetObjectIdentity() ?? "";
			var item = await (identity.IsValidUUID() ? Item.GetAsync<Item>(identity, cancellationToken) : Item.GetItemByAliasAsync(requestInfo.GetParameter("RepositoryEntityID") ?? requestInfo.GetParameter("x-content-type-id"), identity, cancellationToken)).ConfigureAwait(false);
			if (item == null)
				throw new InformationNotFoundException();
			else if (item.Organization == null || item.Module == null || item.ContentType == null)
				throw new InformationInvalidException("The organization/module/item-type is invalid");

			// check permission
			var gotRights = isSystemAdministrator || requestInfo.Session.User.ID.IsEquals(item.Organization.OwnerID) || requestInfo.Session.User.IsViewer(item.WorkingPrivileges);
			if (!gotRights)
				throw new AccessDeniedException();

			if (!identity.IsValidUUID())
				return new JObject
				{
					{ "ID", item.ID },
					{ "Title", item.Title },
					{ "Alias", item.Alias }
				};

			// send update message
			var response = item.ToJson();
			response["Thumbnails"] = await requestInfo.GetThumbnailsAsync(item.ID, item.Title.Url64Encode(), Utility.ValidationKey, cancellationToken).ConfigureAwait(false);
			response["Attachments"] = await requestInfo.GetAttachmentsAsync(item.ID, item.Title.Url64Encode(), Utility.ValidationKey, cancellationToken).ConfigureAwait(false);
			await Utility.RTUService.SendUpdateMessageAsync(new UpdateMessage
			{
				Type = $"{requestInfo.ServiceName}#{item.GetObjectName()}#Update",
				DeviceID = "*",
				ExcludedDeviceID = requestInfo.Session.DeviceID,
				Data = response
			}, cancellationToken).ConfigureAwait(false);

			// response
			return response;
		}

		internal static async Task<JObject> UpdateItemAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, CancellationToken cancellationToken = default)
		{
			// prepare
			var item = await Item.GetAsync<Item>(requestInfo.GetObjectIdentity() ?? "", cancellationToken).ConfigureAwait(false);
			if (item == null)
				throw new InformationNotFoundException();
			else if (item.Organization == null || item.Module == null || item.ContentType == null)
				throw new InformationInvalidException("The organization/module/item-type is invalid");

			// check permission
			var gotRights = isSystemAdministrator || requestInfo.Session.User.ID.IsEquals(item.Organization.OwnerID) || requestInfo.Session.User.IsEditor(item.WorkingPrivileges);
			if (!gotRights)
				gotRights = item.Status.Equals(ApprovalStatus.Draft) || item.Status.Equals(ApprovalStatus.Pending) || item.Status.Equals(ApprovalStatus.Rejected)
					? requestInfo.Session.User.ID.IsEquals(item.CreatedID)
					: requestInfo.Session.User.IsEditor(item.WorkingPrivileges);
			if (!gotRights)
				throw new AccessDeniedException();

			// prepare data
			var oldAlias = item.Alias;
			var oldStatus = item.Status;
			item.UpdateItemInstance(requestInfo.GetBodyExpando(), "ID,SystemID,RepositoryID,RepositoryEntityID,Privileges,Created,CreatedID,LastModified,LastModifiedID", obj =>
			{
				obj.LastModified = DateTime.Now;
				obj.LastModifiedID = requestInfo.Session.User.ID;
			});

			item.Alias = string.IsNullOrWhiteSpace(item.Alias) ? oldAlias : item.Alias.NormalizeAlias();
			var existing = await Item.GetItemByAliasAsync(item.RepositoryEntityID, item.Alias, cancellationToken).ConfigureAwait(false);
			if (existing != null && !existing.ID.IsEquals(item.ID))
				item.Alias += $"-{DateTime.Now.ToUnixTimestamp()}";

			// update
			await Item.UpdateAsync(item, requestInfo.Session.User.ID, cancellationToken).ConfigureAwait(false);
			await Utility.Cache.SetAsync($"e:{item.ContentTypeID}#a:{item.Alias.GenerateUUID()}".GetCacheKey<Item>(), item.ID, cancellationToken).ConfigureAwait(false);
			item.ClearRelatedCacheAsync(null, requestInfo.CorrelationID).Run();

			// send update message
			var response = item.ToJson();
			response["Thumbnails"] = await requestInfo.GetThumbnailsAsync(item.ID, item.Title.Url64Encode(), Utility.ValidationKey, cancellationToken).ConfigureAwait(false);
			response["Attachments"] = await requestInfo.GetAttachmentsAsync(item.ID, item.Title.Url64Encode(), Utility.ValidationKey, cancellationToken).ConfigureAwait(false);
			await Utility.RTUService.SendUpdateMessageAsync(new UpdateMessage
			{
				Type = $"{requestInfo.ServiceName}#{item.GetObjectName()}#Update",
				DeviceID = "*",
				Data = response
			}, cancellationToken).ConfigureAwait(false);

			// send notification
			item.SendNotificationAsync("Update", item.ContentType.Notifications, oldStatus, item.Status, requestInfo, cancellationToken).Run();

			// response
			return response;
		}

		internal static async Task<JObject> DeleteItemAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, CancellationToken cancellationToken = default)
		{
			// prepare
			var item = await Item.GetAsync<Item>(requestInfo.GetObjectIdentity() ?? "", cancellationToken).ConfigureAwait(false);
			if (item == null)
				throw new InformationNotFoundException();
			else if (item.Organization == null || item.Module == null || item.ContentType == null)
				throw new InformationInvalidException("The organization/module/item-type is invalid");

			// check permission
			var gotRights = isSystemAdministrator || requestInfo.Session.User.ID.IsEquals(item.Organization.OwnerID) || requestInfo.Session.User.IsModerator(item.WorkingPrivileges);
			if (!gotRights)
				gotRights = item.Status.Equals(ApprovalStatus.Draft) || item.Status.Equals(ApprovalStatus.Pending) || item.Status.Equals(ApprovalStatus.Rejected)
					? requestInfo.Session.User.ID.IsEquals(item.CreatedID) || requestInfo.Session.User.IsEditor(item.WorkingPrivileges)
					: requestInfo.Session.User.IsModerator(item.WorkingPrivileges);
			if (!gotRights)
				throw new AccessDeniedException();

			// delete files
			await requestInfo.DeleteFilesAsync(item.SystemID, item.RepositoryEntityID, item.ID, Utility.ValidationKey, cancellationToken).ConfigureAwait(false);

			// delete
			await Item.DeleteAsync<Item>(item.ID, requestInfo.Session.User.ID, cancellationToken).ConfigureAwait(false);
			await item.ClearRelatedCacheAsync(null, cancellationToken, requestInfo.CorrelationID).ConfigureAwait(false);

			// send update message
			var response = item.ToJson();
			await Utility.RTUService.SendUpdateMessageAsync(new UpdateMessage
			{
				Type = $"{requestInfo.ServiceName}#{item.GetObjectName()}#Delete",
				DeviceID = "*",
				Data = response
			}, cancellationToken).ConfigureAwait(false);

			// send notification
			item.SendNotificationAsync("Delete", item.ContentType.Notifications, item.Status, item.Status, requestInfo, cancellationToken).Run();

			// response
			return response;
		}

		internal static async Task<JObject> GenerateAsync(RequestInfo requestInfo, bool isSystemAdministrator = false, CancellationToken cancellationToken = default)
		{
			// prepare
			var requestJson = requestInfo.BodyAsJson;
			var options = requestJson.Get("Options", new JObject()).ToExpandoObject();

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
			string coverURI = null, data = null;

			var showThumbnails = options.Get("ShowThumbnails", options.Get("ShowThumbnail", false)) || options.Get("ShowPngThumbnails", false) || options.Get("ShowAsPngThumbnails", false) || options.Get("ShowBigThumbnails", false) || options.Get("ShowAsBigThumbnails", false);
			var pngThumbnails = options.Get("ThumbnailsAsPng", options.Get("ThumbnailAsPng", options.Get("ShowPngThumbnails", options.Get("ShowAsPngThumbnails", false))));
			var bigThumbnails = options.Get("ThumbnailsAsBig", options.Get("ThumbnailAsBig", options.Get("ShowBigThumbnails", options.Get("ShowAsBigThumbnails", false))));
			var thumbnailsWidth = options.Get("ThumbnailsWidth", options.Get("ThumbnailWidth", 0));
			var thumbnailsHeight = options.Get("ThumbnailsHeight", options.Get("ThumbnailHeight", 0));

			var showAttachments = options.Get("ShowAttachments", false);
			var showPagination = options.Get("ShowPagination", false);

			// generate list
			if (isList)
			{
				// check permission
				var contentType = await (contentTypeID ?? "").GetContentTypeByIDAsync(cancellationToken).ConfigureAwait(false);
				var gotRights = isSystemAdministrator || requestInfo.Session.User.IsViewer(contentType?.WorkingPrivileges);
				if (!gotRights)
				{
					var organization = contentType?.Organization ?? await organizationJson.Get<string>("ID", "").GetOrganizationByIDAsync(cancellationToken).ConfigureAwait(false);
					gotRights = requestInfo.Session.User.ID.IsEquals(organization?.OwnerID);
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

				// search
				var results = await requestInfo.SearchAsync(null, filter, sort, pageSize, pageNumber, contentTypeID, -1, cancellationToken).ConfigureAwait(false);
				var totalRecords = results.Item1;
				var objects = results.Item2;
				var thumbnails = results.Item3;

				// attachments
				JToken attachments = null;
				if (objects.Count > 0 && showAttachments)
				{
					attachments = objects.Count == 1
						? await requestInfo.GetAttachmentsAsync(objects[0].ID, objects[0].Title.Url64Encode(), Utility.ValidationKey, cancellationToken).ConfigureAwait(false)
						: await requestInfo.GetAttachmentsAsync(objects.Select(@object => @object.ID).Join(","), objects.ToJObject("ID", @object => new JValue(@object.Title.Url64Encode())).ToString(Formatting.None), Utility.ValidationKey, cancellationToken).ConfigureAwait(false);
				}

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
								element.Element("Summary").Value = @object.Summary.Replace("\r", "").Replace("\n", "<br/>");
							element.Add(new XElement("URL", @object.GetURL(desktop, false, parentIdentity)));
							var thumbnailURL = thumbnails?.GetThumbnailURL(@object.ID, pngThumbnails, bigThumbnails, thumbnailsWidth, thumbnailsHeight);
							element.Add(new XElement("ThumbnailURL", thumbnailURL, new XAttribute("Alternative", thumbnailURL?.GetWebpImageURL(pngThumbnails) ?? "")));
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
						exception = requestInfo.GetRuntimeException(ex, null, (msg, exc) => requestInfo.WriteErrorAsync(exc, cancellationToken, $"Error occurred while generating an item => {msg} : {@object.ToJson()}", "Errors").Run());
					}
				});

				// get data
				data = dataXml.CleanInvalidCharacters().ToString(SaveOptions.DisableFormatting);

				// update cache
				var expression = await expressionJson.Get("ID", "").GetExpressionByIDAsync(cancellationToken).ConfigureAwait(false);
				Task.WhenAll(
					contentType != null ? Utility.Cache.AddSetMembersAsync(contentType.GetSetCacheKey(), results.Item4) : Task.CompletedTask,
					expression != null ? Utility.Cache.AddSetMembersAsync(expression.GetSetCacheKey(), results.Item4) : Task.CompletedTask,
					Utility.Logger.IsEnabled(LogLevel.Debug) ? Utility.WriteLogAsync(requestInfo, $"Update cache keys into related sets when generate CMS items\r\n- Related sets: {new[] { contentType != null ? contentType.GetSetCacheKey() : null, expression != null ? expression.GetSetCacheKey() : null }.Where(key => key != null).Join(", ")}\r\n- Related cache keys ({results.Item4.Count}): {results.Item4.Join(", ")}", CancellationToken.None, "Caches") : Task.CompletedTask
				).Run();

				// prepare pagination
				if (showPagination)
				{
					var totalPages = new Tuple<long, int>(totalRecords, pageSize).GetTotalPages();
					if (totalPages > 0 && pageNumber > totalPages)
						pageNumber = totalPages;
					pagination = Utility.GeneratePagination(totalRecords, totalPages, pageSize, pageNumber, $"~/{desktop ?? "-default"}/{parentIdentity ?? contentType?.Title.GetANSIUri() ?? "-"}" + "/{{pageNumber}}" + $"{(organizationJson.Get<bool>("AlwaysUseHtmlSuffix", true) ? ".html" : "")}", showPageLinks, numberOfPageLinks, requestInfo.Query?.Where(kvp => !kvp.Key.IsStartsWith("x-")).Select(kvp => $"{kvp.Key}={kvp.Value?.UrlEncode()}").Join("&"));
				}

				// prepare SEO info
				seoInfo = new JObject
				{
					{ "Title", contentType?.Title },
					{ "Description", contentType?.Description }
				};
			}

			// generate details
			else
			{
				// get the requested object
				var @object = await Item.GetItemByAliasAsync(contentTypeID, requestJson.Get<string>("ContentIdentity"), cancellationToken).ConfigureAwait(false);
				if (@object == null)
					throw new InformationNotFoundException();
				else if (@object.Organization == null || @object.Module == null || @object.ContentType == null)
					throw new InformationInvalidException("The organization/module/content-type is invalid");

				// check permission
				var gotRights = isSystemAdministrator || requestInfo.Session.User.ID.IsEquals(@object.Organization.OwnerID) || @object.Status.Equals(ApprovalStatus.Published)
					? requestInfo.Session.User.IsViewer(@object.WorkingPrivileges)
					: requestInfo.Session.User.ID.IsEquals(@object.CreatedID) || requestInfo.Session.User.IsEditor(@object.WorkingPrivileges);
				if (!gotRights)
					throw new AccessDeniedException();

				var showOthers = options.Get("ShowOthers", false);

				// get other contents
				Task<List<Item>> newersTask, oldersTask;
				if (showOthers)
				{
					var numberOfOthers = options.Get<int>("NumberOfOthers", 10) / 2;

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
				var thumbnailsTask = showThumbnails ? requestInfo.GetThumbnailsAsync(@object.ID, @object.Title.Url64Encode(), Utility.ValidationKey, cancellationToken) : Task.FromResult<JToken>(new JArray());
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

				// generate XML elements
				var elements = XElement.Parse("<Data/>");
				elements.Add(@object.ToXml(false, cultureInfo, element =>
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
						(thumbnailsTask.Result as JArray)?.ForEach(thumbnail =>
						{
							var thumbnailURL = thumbnail.Get<string>("URI")?.GetThumbnailURL(pngThumbnails, bigThumbnails, thumbnailsWidth, thumbnailsHeight);
							thumbnails.Add(new XElement("Thumbnail", thumbnailURL, new XAttribute("Alternative", thumbnailURL?.GetWebpImageURL(pngThumbnails) ?? "")));
						});
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
						otherXml.Add(new XElement("URL", other.GetURL(desktop, false, parentIdentity)));
						otherXml.Add(new XElement("ThumbnailURL", otherThumbnails?.GetThumbnailURL(other.ID)));
					})));
					elements.Add(othersXml);
				}

				// build others
				pagination = showPagination ? Utility.GeneratePagination(1, 1, 0, pageNumber, @object.GetURL(desktop, true), showPageLinks, numberOfPageLinks) : null;
				seoInfo = new JObject
				{
					{ "Title", @object.Title },
					{ "Description", @object.Summary },
					{ "Keywords", @object.Tags }
				};
				coverURI = (thumbnailsTask.Result as JArray)?.First()?.Get<string>("URI");

				// get xml data
				data = elements.CleanInvalidCharacters().ToString(SaveOptions.DisableFormatting);
			}

			// response
			return new JObject
			{
				{ "Data", data },
				{ "Pagination", pagination },
				{ "FilterBy", filterBy },
				{ "SortBy", sortBy },
				{ "SEOInfo", seoInfo },
				{ "CoverURI", coverURI }
			};
		}

		internal static async Task<JObject> SyncItemAsync(this RequestInfo requestInfo, CancellationToken cancellationToken = default)
		{
			var data = requestInfo.GetBodyExpando();
			var item = await Item.GetAsync<Item>(data.Get<string>("ID"), cancellationToken).ConfigureAwait(false);
			if (item == null)
			{
				item = Item.CreateInstance(data);
				await Item.CreateAsync(item, cancellationToken).ConfigureAwait(false);
			}
			else
			{
				item.Fill(data);
				await Item.UpdateAsync(item, true, cancellationToken).ConfigureAwait(false);
			}

			// clear related cache
			//item.ClearRelatedCacheAsync(null, requestInfo.CorrelationID).Run();

			// send update messages
			var json = item.ToJson();
			var objectName = item.GetObjectName();
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
				{ "ID", item.ID },
				{ "Type", objectName }
			};
		}
	}
}
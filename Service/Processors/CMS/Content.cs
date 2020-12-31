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
	public static class ContentProcessor
	{
		public static Content CreateContentInstance(this ExpandoObject data, string excluded = null, Action<Content> onCompleted = null)
			=> Content.CreateInstance(data, excluded?.ToHashSet(), content =>
			{
				content.NormalizeHTMLs();
				content.Tags = content.Tags?.Replace(";", ",").ToList(",", true).Where(tag => !string.IsNullOrWhiteSpace(tag)).Join(",");
				content.Tags = string.IsNullOrWhiteSpace(content.Tags) ? null : content.Tags;
				onCompleted?.Invoke(content);
			});

		public static Content UpdateContentInstance(this Content content, ExpandoObject data, string excluded = null, Action<Content> onCompleted = null)
			=> content.Fill(data, excluded?.ToHashSet(), _ =>
			{
				content.NormalizeHTMLs();
				content.Tags = content.Tags?.Replace(";", ",").ToList(",", true).Where(tag => !string.IsNullOrWhiteSpace(tag)).Join(",");
				content.Tags = string.IsNullOrWhiteSpace(content.Tags) ? null : content.Tags;
				onCompleted?.Invoke(content);
			});

		public static IFilterBy<Content> GetContentsFilter(string systemID, string repositoryID = null, string repositoryEntityID = null, string categoryID = null, Action<FilterBys<Content>> onCompleted = null)
		{
			var filter = Filters<Content>.And();
			if (!string.IsNullOrWhiteSpace(systemID))
				filter.Add(Filters<Content>.Equals("SystemID", systemID));
			if (!string.IsNullOrWhiteSpace(repositoryID))
				filter.Add(Filters<Content>.Equals("RepositoryID", repositoryID));
			if (!string.IsNullOrWhiteSpace(repositoryEntityID))
				filter.Add(Filters<Content>.Equals("RepositoryEntityID", repositoryEntityID));
			if (!string.IsNullOrWhiteSpace(categoryID))
				filter.Add(Filters<Content>.Equals("CategoryID", categoryID));
			onCompleted?.Invoke(filter);
			return filter;
		}

		internal static IFilterBy<Content> GetContentByAliasFilter(this ContentType contentType, Category category, string alias)
			=> Filters<Content>.And
			(
				Filters<Content>.Equals("RepositoryEntityID", contentType.ID),
				Filters<Content>.Equals("CategoryID", category.ID),
				Filters<Content>.Equals("Alias", alias.NormalizeAlias())
			);

		internal static async Task ClearRelatedCacheAsync(this Content content, IEnumerable<string> categoryIDs, CancellationToken cancellationToken = default, string correlationID = null, int pageSize = 0)
		{
			// cache keys of the individual content
			var dataCacheKeys = Extensions.GetRelatedCacheKeys(content.GetCacheKey(), pageSize);

			// cache keys of the content-type
			var sort = Sorts<Content>.Descending("StartDate").ThenByDescending("PublishedTime");
			dataCacheKeys = Extensions.GetRelatedCacheKeys(ContentProcessor.GetContentsFilter(content.SystemID, content.RepositoryID, content.RepositoryEntityID, null), sort, pageSize).Concat(dataCacheKeys).ToList();
			if (content.ContentType != null)
				dataCacheKeys = (await Utility.Cache.GetSetMembersAsync(content.ContentType.GetSetCacheKey(), cancellationToken).ConfigureAwait(false)).Concat(dataCacheKeys).ToList();

			// cache keys of category
			dataCacheKeys = Extensions.GetRelatedCacheKeys(ContentProcessor.GetContentsFilter(content.SystemID, content.RepositoryID, content.RepositoryEntityID, content.CategoryID), sort, pageSize).Concat(dataCacheKeys).ToList();
			if (content.Category != null)
				dataCacheKeys = (await Utility.Cache.GetSetMembersAsync(content.Category.GetSetCacheKey(), cancellationToken).ConfigureAwait(false)).Concat(dataCacheKeys).ToList();

			var otherCategoryIDs = (content.OtherCategories ?? new List<string>()).Concat(categoryIDs ?? new List<string>())
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.Where(categoryID => !string.IsNullOrWhiteSpace(categoryID) && !categoryID.IsEquals(content.CategoryID) && categoryID.IsValidUUID())
				.ToList();

			await otherCategoryIDs.ForEachAsync(async (categoryID, _) =>
			{
				dataCacheKeys = Extensions.GetRelatedCacheKeys(ContentProcessor.GetContentsFilter(content.SystemID, content.RepositoryID, content.RepositoryEntityID, categoryID), sort, pageSize).Concat(dataCacheKeys).ToList();
				var category = await categoryID.GetCategoryByIDAsync(cancellationToken).ConfigureAwait(false);
				if (category != null)
					dataCacheKeys = (await Utility.Cache.GetSetMembersAsync(category.GetSetCacheKey(), cancellationToken).ConfigureAwait(false)).Concat(dataCacheKeys).ToList();
			}, cancellationToken, true, false).ConfigureAwait(false);

			dataCacheKeys = dataCacheKeys.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

			var desktop = content.Desktop;
			var htmlCacheKeys = desktop != null
				? await Utility.Cache.GetSetMembersAsync(desktop.GetSetCacheKey(), cancellationToken).ConfigureAwait(false)
				: new HashSet<string>();

			if (desktop != null)
			{
				var desktopURL = content.GetURL(null, true);
				for (var page = 1; page < 10; page++)
				{
					var desktopCacheKey = desktop.GetDesktopCacheKey(desktopURL.Replace(StringComparison.OrdinalIgnoreCase, "/{{pageNumber}}", page > 1 ? $"/{page}" : ""));
					htmlCacheKeys.Append(new[] { desktopCacheKey, $"{desktopCacheKey}:time" });
				}
			}

			await otherCategoryIDs.Concat(new[] { content.CategoryID }).ForEachAsync(async (categoryID, _) =>
			{
				var category = await categoryID.GetCategoryByIDAsync(cancellationToken).ConfigureAwait(false);
				desktop = category?.Desktop ?? content?.ContentType.Desktop;
				if (desktop != null)
				{
					var desktopURL = category.GetURL(null, true);
					for (var page = 1; page <= 10; page++)
					{
						var desktopCacheKey = desktop.GetDesktopCacheKey(desktopURL.Replace(StringComparison.OrdinalIgnoreCase, "/{{pageNumber}}", page > 1 ? $"/{page}" : ""));
						htmlCacheKeys.Append(new[] { desktopCacheKey, $"{desktopCacheKey}:time" });
					}
				}
			}, cancellationToken, true, false).ConfigureAwait(false);

			htmlCacheKeys.Append(content.Organization.GetDesktopCacheKey());

			if (Utility.Logger.IsEnabled(LogLevel.Debug))
				await Utility.WriteLogAsync(correlationID, $"Clear related cache of CMS content [{content.ID} => {content.Title}]\r\n- {dataCacheKeys.Count} data keys => {dataCacheKeys.Join(", ")}\r\n- {htmlCacheKeys.Count} html keys => {htmlCacheKeys.Join(", ")}", CancellationToken.None, "Caches").ConfigureAwait(false);
			await Utility.Cache.RemoveAsync(htmlCacheKeys.Concat(dataCacheKeys).Distinct(StringComparer.OrdinalIgnoreCase).ToList(), cancellationToken).ConfigureAwait(false);

			await Task.WhenAll
			(
				content.Status != ApprovalStatus.Published ? Task.CompletedTask : content.GetURL().Replace("~/", $"{Utility.PortalsHttpURI}/~{content.Organization.Alias}/").RefreshWebPageAsync(1, correlationID),
				content.Category.GetURL().Replace("~/", $"{Utility.PortalsHttpURI}/~{content.Organization.Alias}/").RefreshWebPageAsync(1, correlationID),
				$"{Utility.PortalsHttpURI}/~{content.Organization.Alias}/".RefreshWebPageAsync(1, correlationID)
			).ConfigureAwait(false);
		}

		internal static Task ClearRelatedCacheAsync(this Content content, IEnumerable<string> categoryIDs, string correlationID, int pageSize = 0)
			=> content.ClearRelatedCacheAsync(categoryIDs, CancellationToken.None, correlationID, pageSize);

		static async Task<Tuple<long, List<Content>, JToken, List<string>>> SearchAsync(this RequestInfo requestInfo, string query, IFilterBy<Content> filter, SortBy<Content> sort, int pageSize, int pageNumber, string contentTypeID = null, long totalRecords = -1, CancellationToken cancellationToken = default, bool searchThumbnails = true)
		{
			// cache keys
			var cacheKeyOfObjects = string.IsNullOrWhiteSpace(query) ? Extensions.GetCacheKey(filter, sort, pageSize, pageNumber) : null;
			var cacheKeyOfTotalObjects = string.IsNullOrWhiteSpace(query) ? Extensions.GetCacheKeyOfTotalObjects(filter, sort) : null;
			var cacheKeys = string.IsNullOrWhiteSpace(query) ? new List<string> { cacheKeyOfObjects, cacheKeyOfTotalObjects } : new List<string>();

			// count
			totalRecords = totalRecords > -1
				? totalRecords
				: string.IsNullOrWhiteSpace(query)
					? await Content.CountAsync(filter, contentTypeID, true, cacheKeyOfTotalObjects, 0, cancellationToken).ConfigureAwait(false)
					: await Content.CountAsync(query, filter, contentTypeID, cancellationToken).ConfigureAwait(false);

			// search objects
			var objects = totalRecords > 0
				? string.IsNullOrWhiteSpace(query)
					? await Content.FindAsync(filter, sort, pageSize, pageNumber, contentTypeID, true, cacheKeyOfObjects, 0, cancellationToken).ConfigureAwait(false)
					: await Content.SearchAsync(query, filter, pageSize, pageNumber, contentTypeID, cancellationToken).ConfigureAwait(false)
				: new List<Content>();

			// search thumbnails
			objects = objects.Where(@object => @object != null && !string.IsNullOrWhiteSpace(@object.ID)).ToList();

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
			return new Tuple<long, List<Content>, JToken, List<string>>(totalRecords, objects, thumbnails, cacheKeys);
		}

		internal static async Task<JObject> SearchContentsAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, CancellationToken cancellationToken = default)
		{
			// prepare
			var request = requestInfo.GetRequestExpando();

			var query = request.Get<string>("FilterBy.Query");

			var filter = request.Get<ExpandoObject>("FilterBy")?.ToFilterBy<Content>() ?? Filters<Content>.And();
			var sort = string.IsNullOrWhiteSpace(query) ? request.Get<ExpandoObject>("SortBy")?.ToSortBy<Content>() ?? Sorts<Content>.Descending("StartDate").ThenByDescending("PublishedTime") : null;

			var pagination = request.Get<ExpandoObject>("Pagination")?.GetPagination() ?? new Tuple<long, int, int, int>(-1, 0, 20, 1);
			var pageSize = pagination.Item3;
			var pageNumber = pagination.Item4;

			var organizationID = filter?.GetValue("SystemID") ?? requestInfo.GetParameter("SystemID") ?? requestInfo.GetParameter("x-system-id");
			var organization = await (organizationID ?? "").GetOrganizationByIDAsync(cancellationToken).ConfigureAwait(false);
			if (organization == null)
				throw new InformationExistedException("The organization is invalid");

			var moduleID = filter?.GetValue("RepositoryID") ?? requestInfo.GetParameter("RepositoryID") ?? requestInfo.GetParameter("x-module-id");
			var module = await (moduleID ?? "").GetModuleByIDAsync(cancellationToken).ConfigureAwait(false);
			if (module == null || !module.SystemID.IsEquals(organization.ID))
				throw new InformationInvalidException("The module is invalid");

			var contentTypeID = filter?.GetValue("RepositoryEntityID") ?? requestInfo.GetParameter("RepositoryEntityID") ?? requestInfo.GetParameter("x-content-type-id");
			var contentType = await (contentTypeID ?? "").GetContentTypeByIDAsync(cancellationToken).ConfigureAwait(false);
			if (contentType == null || !contentType.SystemID.IsEquals(organization.ID) || !contentType.RepositoryID.IsEquals(module.ID))
				throw new InformationInvalidException("The content-type is invalid");

			var categoryID = filter?.GetValue("CategoryID") ?? requestInfo.GetParameter("CategoryID") ?? requestInfo.GetParameter("x-category-id");
			var category = await (categoryID ?? "").GetCategoryByIDAsync(cancellationToken).ConfigureAwait(false);

			// check permission
			var gotRights = isSystemAdministrator || requestInfo.Session.User.ID.IsEquals(organization.OwnerID) || requestInfo.Session.User.IsViewer(category?.WorkingPrivileges, contentType.WorkingPrivileges);
			if (!gotRights)
				throw new AccessDeniedException();

			// normalize filter
			if (filter == null || !(filter is FilterBys<Content>) || (filter as FilterBys<Content>).Children == null || (filter as FilterBys<Content>).Children.Count < 1)
				filter = ContentProcessor.GetContentsFilter(organization.ID, module.ID, contentType.ID, category?.ID);
			if (!requestInfo.Session.User.IsAuthenticated)
			{
				if (!(filter.GetChild("Status") is FilterBy<Content> filterByStatus))
					(filter as FilterBys<Content>).Add(Filters<Content>.Equals("Status", ApprovalStatus.Published.ToString()));
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
						cjson["Thumbnails"] = thumbnails?.GetThumbnails(@object.ID);
						cjson["Details"] = organization.NormalizeURLs(@object.Details);
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
					Utility.Logger.IsEnabled(LogLevel.Debug) ? Utility.WriteLogAsync(requestInfo, $"Update cache when search CMS contents\r\n- Cache key of JSON: {cacheKeyOfObjectsJson}\r\n- Cache key of realated sets: {contentType.GetSetCacheKey()}\r\n- Related cache keys: {cacheKeys.Join(", ")}", CancellationToken.None, "Caches") : Task.CompletedTask
				).Run();
			}

			// response
			return response;
		}

		internal static async Task<JObject> CreateContentAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, CancellationToken cancellationToken = default)
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

			var categoryID = request.Get<string>("CategoryID") ?? requestInfo.GetParameter("CategoryID") ?? requestInfo.GetParameter("x-category-id");
			var category = await (categoryID ?? "").GetCategoryByIDAsync(cancellationToken).ConfigureAwait(false);
			if (category == null || !category.SystemID.IsEquals(organization.ID) || !category.RepositoryID.IsEquals(module.ID))
				throw new InformationInvalidException("The category is invalid");

			// check permission
			var gotRights = isSystemAdministrator || requestInfo.Session.User.ID.IsEquals(organization.OwnerID) || requestInfo.Session.User.IsContributor(category.WorkingPrivileges, contentType.WorkingPrivileges);
			if (!gotRights)
				throw new AccessDeniedException();

			// get data
			var content = request.CreateContentInstance("SystemID,RepositoryID,RepositoryEntityID,Privileges,Created,CreatedID,LastModified,LastModifiedID", obj =>
			{
				obj.SystemID = organization.ID;
				obj.RepositoryID = module.ID;
				obj.RepositoryEntityID = contentType.ID;
				obj.ID = string.IsNullOrWhiteSpace(obj.ID) || !obj.ID.IsValidUUID() ? UtilityService.NewUUID : obj.ID;
				obj.Created = obj.LastModified = DateTime.Now;
				obj.CreatedID = obj.LastModifiedID = requestInfo.Session.User.ID;
			});

			content.Alias = string.IsNullOrWhiteSpace(content.Alias) ? content.Title.NormalizeAlias() : content.Alias.NormalizeAlias();
			var existing = await Content.GetContentByAliasAsync(contentType, content.Alias, category.ID, cancellationToken).ConfigureAwait(false);
			if (existing != null)
				content.Alias += $"-{DateTime.Now.ToUnixTimestamp()}";

			var dateString = request.Get<string>("StartDate");
			content.StartDate = !string.IsNullOrWhiteSpace(dateString) && DateTime.TryParse(dateString, out var date)
				? date.ToDTString(false, false)
				: DateTime.Now.ToDTString(false, false);

			content.EndDate = null;
			dateString = request.Get<string>("EndDate");
			if (!string.IsNullOrWhiteSpace(dateString) && DateTime.TryParse(dateString, out date))
				content.EndDate = date.ToDTString(false, false);

			content.PublishedTime = null;
			dateString = request.Get<string>("PublishedTime");
			if (!string.IsNullOrWhiteSpace(dateString) && DateTime.TryParse(dateString, out date))
				content.PublishedTime = date;
			if (content.PublishedTime == null && content.Status.Equals(ApprovalStatus.Published))
				content.PublishedTime = DateTime.Now;

			content.OtherCategories = content.OtherCategories?.Where(id => !content.CategoryID.IsEquals(id)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
			content.OtherCategories = content.OtherCategories != null && content.OtherCategories.Count > 0 ? content.OtherCategories : null;

			content.Details = organization.NormalizeURLs(content.Details, false);

			content.Relateds = content.Relateds?.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
			content.Relateds = content.Relateds != null && content.Relateds.Count > 0 ? content.Relateds : null;

			if (content.ExternalRelateds != null)
			{
				var index = 0;
				while (index < content.ExternalRelateds.Count)
				{
					if (content.ExternalRelateds[index] == null || string.IsNullOrWhiteSpace(content.ExternalRelateds[index].Title) || string.IsNullOrWhiteSpace(content.ExternalRelateds[index].URL))
						content.ExternalRelateds.RemoveAt(index);
					else
						index++;
				}
			}
			content.ExternalRelateds = content.ExternalRelateds != null && content.ExternalRelateds.Count > 0 ? content.ExternalRelateds : null;

			// create new
			await Content.CreateAsync(content, cancellationToken).ConfigureAwait(false);
			await Utility.Cache.SetAsync($"e:{content.ContentTypeID}#c:{content.CategoryID}#a:{content.Alias.GenerateUUID()}".GetCacheKey<Content>(), content.ID, cancellationToken).ConfigureAwait(false);
			content.ClearRelatedCacheAsync(null, requestInfo.CorrelationID).Run();

			// prepare the response
			var thumbnailsTask = requestInfo.GetThumbnailsAsync(content.ID, content.Title.Url64Encode(), Utility.ValidationKey, cancellationToken);
			var attachmentsTask = requestInfo.GetAttachmentsAsync(content.ID, content.Title.Url64Encode(), Utility.ValidationKey, cancellationToken);
			await Task.WhenAll(thumbnailsTask, attachmentsTask).ConfigureAwait(false);

			var response = content.ToJson(json =>
			{
				json["Thumbnails"] = thumbnailsTask.Result;
				json["Attachments"] = attachmentsTask.Result;
				json["Details"] = organization.NormalizeURLs(content.Details);
			});

			// send update message
			await (Utility.RTUService.SendUpdateMessageAsync(new UpdateMessage
			{
				Type = $"{requestInfo.ServiceName}#{content.GetObjectName()}#Create",
				DeviceID = "*",
				Data = response
			}, cancellationToken)).ConfigureAwait(false);

			// send notification
			content.SendNotificationAsync("Create", content.Category.Notifications, ApprovalStatus.Draft, content.Status, requestInfo, cancellationToken).Run();

			// response
			return response;
		}

		internal static async Task<JObject> GetContentAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, CancellationToken cancellationToken = default)
		{
			// prepare
			var identity = requestInfo.GetObjectIdentity() ?? "";
			var content = await (identity.IsValidUUID() ? Content.GetAsync<Content>(identity, cancellationToken) : Content.GetContentByAliasAsync(requestInfo.GetParameter("RepositoryEntityID") ?? requestInfo.GetParameter("x-content-type-id"), identity, requestInfo.GetParameter("Category") ?? requestInfo.GetParameter("x-category-id"), cancellationToken)).ConfigureAwait(false);
			if (content == null)
				throw new InformationNotFoundException();
			else if (content.Organization == null || content.Module == null || content.ContentType == null)
				throw new InformationInvalidException("The organization/module/content-type is invalid");

			// check permission
			var gotRights = isSystemAdministrator || requestInfo.Session.User.ID.IsEquals(content.Organization.OwnerID);
			if (!gotRights)
				gotRights = content.Status.Equals(ApprovalStatus.Published)
					? requestInfo.Session.User.IsViewer(content.WorkingPrivileges)
					: requestInfo.Session.User.ID.IsEquals(content.CreatedID) || requestInfo.Session.User.IsEditor(content.WorkingPrivileges);
			if (!gotRights)
				throw new AccessDeniedException();

			if (!identity.IsValidUUID())
				return new JObject
				{
					{ "ID", content.ID },
					{ "Title", content.Title },
					{ "Alias", content.Alias }
				};

			// prepare the response
			var thumbnailsTask = requestInfo.GetThumbnailsAsync(content.ID, content.Title.Url64Encode(), Utility.ValidationKey, cancellationToken);
			var attachmentsTask = requestInfo.GetAttachmentsAsync(content.ID, content.Title.Url64Encode(), Utility.ValidationKey, cancellationToken);
			await Task.WhenAll(thumbnailsTask, attachmentsTask).ConfigureAwait(false);

			var response = content.ToJson(json =>
			{
				json["Thumbnails"] = thumbnailsTask.Result;
				json["Attachments"] = attachmentsTask.Result;
				json["Details"] = content.Organization.NormalizeURLs(content.Details);
			});

			// send update message
			await Utility.RTUService.SendUpdateMessageAsync(new UpdateMessage
			{
				Type = $"{requestInfo.ServiceName}#{content.GetObjectName()}#Update",
				DeviceID = "*",
				ExcludedDeviceID = requestInfo.Session.DeviceID,
				Data = response
			}, cancellationToken).ConfigureAwait(false);

			// response
			return response;
		}

		internal static async Task<JObject> UpdateContentAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, CancellationToken cancellationToken = default)
		{
			// prepare
			var content = await Content.GetAsync<Content>(requestInfo.GetObjectIdentity() ?? "", cancellationToken).ConfigureAwait(false);
			if (content == null)
				throw new InformationNotFoundException();
			else if (content.Organization == null || content.Module == null || content.ContentType == null)
				throw new InformationInvalidException("The organization/module/content-type is invalid");

			// check permission
			var gotRights = isSystemAdministrator || requestInfo.Session.User.ID.IsEquals(content.Organization.OwnerID) || requestInfo.Session.User.IsEditor(content.WorkingPrivileges);
			if (!gotRights)
				gotRights = content.Status.Equals(ApprovalStatus.Draft) || content.Status.Equals(ApprovalStatus.Pending) || content.Status.Equals(ApprovalStatus.Rejected)
					? requestInfo.Session.User.ID.IsEquals(content.CreatedID)
					: requestInfo.Session.User.IsEditor(content.WorkingPrivileges);
			if (!gotRights)
				throw new AccessDeniedException();

			// prepare data
			var request = requestInfo.GetBodyExpando();
			var oldCategoryID = content.CategoryID;
			var oldOtherCategories = content.OtherCategories?.Select(id => id).ToList();
			var oldAlias = content.Alias;
			var oldStatus = content.Status;

			content.UpdateContentInstance(request, "ID,SystemID,RepositoryID,RepositoryEntityID,Privileges,Created,CreatedID,LastModified,LastModifiedID", obj =>
			{
				obj.LastModified = DateTime.Now;
				obj.LastModifiedID = requestInfo.Session.User.ID;
			});

			content.Alias = string.IsNullOrWhiteSpace(content.Alias) ? oldAlias : content.Alias.NormalizeAlias();
			var existing = await Content.GetContentByAliasAsync(content.RepositoryEntityID, content.Alias, content.CategoryID, cancellationToken).ConfigureAwait(false);
			if (existing != null && !existing.ID.IsEquals(content.ID))
				content.Alias += $"-{DateTime.Now.ToUnixTimestamp()}";

			var dateString = request.Get<string>("StartDate");
			content.StartDate = !string.IsNullOrWhiteSpace(dateString) && DateTime.TryParse(dateString, out var date)
				? date.ToDTString(false, false)
				: DateTime.Now.ToDTString(false, false);

			content.EndDate = null;
			dateString = request.Get<string>("EndDate");
			if (!string.IsNullOrWhiteSpace(dateString) && DateTime.TryParse(dateString, out date))
				content.EndDate = date.ToDTString(false, false);

			content.PublishedTime = null;
			dateString = request.Get<string>("PublishedTime");
			if (!string.IsNullOrWhiteSpace(dateString) && DateTime.TryParse(dateString, out date))
				content.PublishedTime = date;
			if (content.PublishedTime == null && content.Status.Equals(ApprovalStatus.Published))
				content.PublishedTime = DateTime.Now;

			content.OtherCategories = content.OtherCategories?.Where(id => !content.CategoryID.IsEquals(id)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
			content.OtherCategories = content.OtherCategories != null && content.OtherCategories.Count > 0 ? content.OtherCategories : null;

			content.Details = content.Organization.NormalizeURLs(content.Details, false);

			content.Relateds = content.Relateds?.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
			content.Relateds = content.Relateds != null && content.Relateds.Count > 0 ? content.Relateds : null;

			if (content.ExternalRelateds != null)
			{
				var index = 0;
				while (index < content.ExternalRelateds.Count)
				{
					if (content.ExternalRelateds[index] == null || string.IsNullOrWhiteSpace(content.ExternalRelateds[index].Title) || string.IsNullOrWhiteSpace(content.ExternalRelateds[index].URL))
						content.ExternalRelateds.RemoveAt(index);
					else
						index++;
				}
			}
			content.ExternalRelateds = content.ExternalRelateds != null && content.ExternalRelateds.Count > 0 ? content.ExternalRelateds : null;

			// update
			await Content.UpdateAsync(content, requestInfo.Session.User.ID, cancellationToken).ConfigureAwait(false);
			await Utility.Cache.SetAsync($"e:{content.ContentTypeID}#c:{content.CategoryID}#a:{content.Alias.GenerateUUID()}".GetCacheKey<Content>(), content.ID, cancellationToken).ConfigureAwait(false);
			content.ClearRelatedCacheAsync(new[] { oldCategoryID }.Concat(oldOtherCategories ?? new List<string>()), requestInfo.CorrelationID).Run();

			// prepare the response
			var thumbnailsTask = requestInfo.GetThumbnailsAsync(content.ID, content.Title.Url64Encode(), Utility.ValidationKey, cancellationToken);
			var attachmentsTask = requestInfo.GetAttachmentsAsync(content.ID, content.Title.Url64Encode(), Utility.ValidationKey, cancellationToken);
			await Task.WhenAll(thumbnailsTask, attachmentsTask).ConfigureAwait(false);

			var response = content.ToJson(json =>
			{
				json["Thumbnails"] = thumbnailsTask.Result;
				json["Attachments"] = attachmentsTask.Result;
				json["Details"] = content.Organization.NormalizeURLs(content.Details);
			});

			// send update message
			await Utility.RTUService.SendUpdateMessageAsync(new UpdateMessage
			{
				Type = $"{requestInfo.ServiceName}#{content.GetObjectName()}#Update",
				DeviceID = "*",
				Data = response
			}, cancellationToken).ConfigureAwait(false);

			// send notification
			content.SendNotificationAsync("Update", content.Category.Notifications, oldStatus, content.Status, requestInfo, cancellationToken).Run();

			// response
			return response;
		}

		internal static async Task<JObject> DeleteContentAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, CancellationToken cancellationToken = default)
		{
			// prepare
			var content = await Content.GetAsync<Content>(requestInfo.GetObjectIdentity() ?? "", cancellationToken).ConfigureAwait(false);
			if (content == null)
				throw new InformationNotFoundException();
			else if (content.Organization == null || content.Module == null || content.ContentType == null)
				throw new InformationInvalidException("The organization/module/content-type is invalid");

			// check permission
			var gotRights = isSystemAdministrator || requestInfo.Session.User.ID.IsEquals(content.Organization.OwnerID) || requestInfo.Session.User.IsModerator(content.WorkingPrivileges);
			if (!gotRights)
				gotRights = content.Status.Equals(ApprovalStatus.Draft) || content.Status.Equals(ApprovalStatus.Pending) || content.Status.Equals(ApprovalStatus.Rejected)
					? requestInfo.Session.User.ID.IsEquals(content.CreatedID) || requestInfo.Session.User.IsEditor(content.WorkingPrivileges)
					: requestInfo.Session.User.IsModerator(content.WorkingPrivileges);
			if (!gotRights)
				throw new AccessDeniedException();

			// delete files
			await requestInfo.DeleteFilesAsync(content.SystemID, content.RepositoryEntityID, content.ID, Utility.ValidationKey, cancellationToken).ConfigureAwait(false);

			// delete content
			await Content.DeleteAsync<Content>(content.ID, requestInfo.Session.User.ID, cancellationToken).ConfigureAwait(false);
			await content.ClearRelatedCacheAsync(null, cancellationToken, requestInfo.CorrelationID).ConfigureAwait(false);

			// send update message
			var response = content.ToJson();
			await Utility.RTUService.SendUpdateMessageAsync(new UpdateMessage
			{
				Type = $"{requestInfo.ServiceName}#{content.GetObjectName()}#Delete",
				DeviceID = "*",
				Data = response
			}, cancellationToken).ConfigureAwait(false);

			// send notification
			content.SendNotificationAsync("Delete", content.Category.Notifications, content.Status, content.Status, requestInfo, cancellationToken).Run();

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
			var parentContentTypeJson = requestJson.Get("ParentContentType", new JObject());
			var expressionJson = requestJson.Get("Expression", new JObject());
			var desktopsJson = requestJson.Get("Desktops", new JObject());

			var contentTypeID = contentTypeJson.Get<string>("ID");
			var parentIdentity = requestJson.Get<string>("ParentIdentity");
			parentIdentity = string.IsNullOrWhiteSpace(parentIdentity) ? null : parentIdentity.Trim();
			var category = await parentContentTypeJson.Get("ID", "").GetCategoryByAliasAsync(parentIdentity, cancellationToken).ConfigureAwait(false);

			var paginationJson = requestJson.Get("Pagination", new JObject());
			var pageSize = paginationJson.Get("PageSize", 7);
			var pageNumber = paginationJson.Get("PageNumber", 1);
			var showPageLinks = paginationJson.Get("ShowPageLinks", true);
			var numberOfPageLinks = paginationJson.Get("NumberOfPageLinks", 7);

			var cultureInfo = CultureInfo.GetCultureInfo(requestJson.Get("Language", "vi-VN"));
			var customDateTimeFormat = options.Get<string>("CustomDateTimeFormat");
			var action = requestJson.Get<string>("Action");
			var isList = string.IsNullOrWhiteSpace(action) || "List".IsEquals(action);

			var desktop = desktopsJson.Get<string>("Specified");
			desktop = !string.IsNullOrWhiteSpace(desktop) ? desktop : desktopsJson.Get<string>("ContentType");
			desktop = !string.IsNullOrWhiteSpace(desktop) ? desktop : desktopsJson.Get<string>("Module");
			desktop = !string.IsNullOrWhiteSpace(desktop) ? desktop : desktopsJson.Get<string>("Default");

			JArray breadcrumbs = null;
			JObject pagination = null, seoInfo = null, filterBy = null, sortBy = null;
			string coverURI = null, seoTitle = null, seoDescription = null, seoKeywords = null, data = null;

			var showThumbnails = options.Get("ShowThumbnails", options.Get("ShowThumbnail", true)) || options.Get("ShowPngThumbnails", false) || options.Get("ShowAsPngThumbnails", false) || options.Get("ShowBigThumbnails", false) || options.Get("ShowAsBigThumbnails", false);
			var pngThumbnails = options.Get("ThumbnailsAsPng", options.Get("ThumbnailAsPng", options.Get("ShowPngThumbnails", options.Get("ShowAsPngThumbnails", false))));
			var bigThumbnails = options.Get("ThumbnailsAsBig", options.Get("ThumbnailAsBig", options.Get("ShowBigThumbnails", options.Get("ShowAsBigThumbnails", false))));
			var thumbnailsWidth = options.Get("ThumbnailsWidth", options.Get("ThumbnailWidth", 0));
			var thumbnailsHeight = options.Get("ThumbnailsHeight", options.Get("ThumbnailHeight", 0));

			var showBreadcrumbs = options.Get("ShowBreadcrumbs", false);
			var showPagination = options.Get("ShowPagination", false);

			// generate list
			if (isList)
			{
				// check permission
				var contentType = await (contentTypeID ?? "").GetContentTypeByIDAsync(cancellationToken).ConfigureAwait(false);
				var gotRights = isSystemAdministrator || requestInfo.Session.User.IsViewer(category?.WorkingPrivileges, contentType?.WorkingPrivileges);
				if (!gotRights)
				{
					var organization = contentType?.Organization ?? await organizationJson.Get("ID", "").GetOrganizationByIDAsync(cancellationToken).ConfigureAwait(false);
					gotRights = requestInfo.Session.User.ID.IsEquals(organization?.OwnerID);
				}
				if (!gotRights)
					throw new AccessDeniedException();

				// prepare filtering expression
				if (!(expressionJson.Get<JObject>("FilterBy")?.ToFilter<Content>() is FilterBys<Content> filter) || filter.Children == null || filter.Children.Count < 1)
				{
					filter = Filters<Content>.And
					(
						Filters<Content>.Equals("SystemID", "@request.Body(Organization.ID)"),
						Filters<Content>.Equals("RepositoryID", "@request.Body(Module.ID)"),
						Filters<Content>.Equals("RepositoryEntityID", "@request.Body(ContentType.ID)")
					);

					if (category != null)
						filter.Add(Filters<Content>.Equals("CategoryID", category.ID));

					filter.Add
					(
						Filters<Content>.LessThanOrEquals("StartDate", "@today"),
						Filters<Content>.Or
						(
							Filters<Content>.IsNull("EndDate"),
							Filters<Content>.GreaterOrEquals("EndDate", "@today")
						),
						Filters<Content>.Equals("Status", ApprovalStatus.Published.ToString())
					);
				}

				if (filter.GetChild("RepositoryEntityID") == null && contentType != null)
					filter.Add(Filters<Content>.Equals("RepositoryEntityID", contentType.ID));

				if (filter.GetChild("StartDate") == null)
					filter.Add(Filters<Content>.LessThanOrEquals("StartDate", "@today"));

				if (filter.GetChild("EndDate") == null)
					filter.Add(Filters<Content>.Or
					(
						Filters<Content>.IsNull("EndDate"),
						Filters<Content>.GreaterOrEquals("EndDate", "@today")
					));

				if (filter.GetChild("Status") == null)
					filter.Add(Filters<Content>.Equals("Status", ApprovalStatus.Published.ToString()));

				filterBy = new JObject
				{
					{ "API", filter.ToJson().ToString(Formatting.None) },
				};
				filter.Prepare(requestInfo, filterBys =>
				{
					var filters = (filterBys as FilterBys).Children.Where(filterby => filterby is FilterBy thefilterby && thefilterby.Value != null && thefilterby.Value is string).Select(filterby => filterby as FilterBy);
					filters.Where(thefilterby => thefilterby.Value is string value && value.IsStartsWith("@parent")).ForEach(filterby => filterby.Value = category?.ID);
				});
				filterBy["App"] = filter.ToClientJson().ToString(Formatting.None);

				// prepare sorting expression
				var sort = expressionJson.Get<JObject>("SortBy")?.ToSort<Content>() ?? Sorts<Content>.Descending("StartDate").ThenByDescending("PublishedTime");
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

				// generate xml
				JToken categoryThumbnails = null;
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
							element.Element("Details")?.Remove();
							element.Element("StartDate")?.UpdateDateTime(cultureInfo, customDateTimeFormat);
							element.Element("EndDate")?.UpdateDateTime(cultureInfo, customDateTimeFormat);
							element.Element("PublishedTime")?.UpdateDateTime(cultureInfo, customDateTimeFormat);
							if (!string.IsNullOrWhiteSpace(@object.Summary))
								element.Element("Summary").Value = @object.Summary.NormalizeHTMLBreaks();
							element.Add(new XElement("Category", @object.Category?.Title, new XAttribute("URL", @object.Category?.GetURL(desktop))));
							element.Add(new XElement("URL", @object.GetURL(desktop)));
							var thumbnailURL = thumbnails?.GetThumbnailURL(@object.ID, pngThumbnails, bigThumbnails, thumbnailsWidth, thumbnailsHeight);
							element.Add(new XElement("ThumbnailURL", thumbnailURL, new XAttribute("Alternative", thumbnailURL?.GetWebpImageURL(pngThumbnails) ?? "")));
						}));
					}
					catch (Exception ex)
					{
						exception = requestInfo.GetRuntimeException(ex, null, (msg, exc) => requestInfo.WriteErrorAsync(exc, cancellationToken, $"Error occurred while generating a content => {msg} : {@object.ToJson()}", "Errors").Run());
					}
				});

				// check error
				if (exception != null)
					throw exception;

				// main category
				if (category != null)
				{
					var categoryID = filter?.GetValue("CategoryID");
					if (!string.IsNullOrWhiteSpace(categoryID) && categoryID.IsValidUUID() && !categoryID.IsEquals(category.ID))
						category = await categoryID.GetCategoryByIDAsync(cancellationToken).ConfigureAwait(false);
				}

				if (category != null)
				{
					requestInfo.Header["x-thumbnails-as-attachments"] = "true";
					categoryThumbnails = await requestInfo.GetThumbnailsAsync(category.ID, category.Title.Url64Encode(), Utility.ValidationKey, cancellationToken).ConfigureAwait(false);
					var thumbnailURL = categoryThumbnails?.GetThumbnailURL(category.ID, pngThumbnails, bigThumbnails, thumbnailsWidth, thumbnailsHeight) ?? "";
					dataXml.Add(new XElement(
						"Parent",
						new XElement("Title", category.Title),
						new XElement("Description", category.Description?.NormalizeHTMLBreaks()),
						new XElement("Notes", category.Notes?.NormalizeHTMLBreaks()),
						new XElement("URL", category.GetURL(desktop)),
						new XElement("ThumbnailURL", thumbnailURL, new XAttribute("Alternative", thumbnailURL?.GetWebpImageURL(pngThumbnails) ?? ""))
					));
				}

				// get data
				data = dataXml.CleanInvalidCharacters().ToString(SaveOptions.DisableFormatting);

				// update cache
				var expression = await expressionJson.Get("ID", "").GetExpressionByIDAsync(cancellationToken).ConfigureAwait(false);
				Task.WhenAll(
					category != null ? Utility.Cache.AddSetMembersAsync(category.GetSetCacheKey(), results.Item4) : Task.CompletedTask,
					contentType != null ? Utility.Cache.AddSetMembersAsync(contentType.GetSetCacheKey(), results.Item4) : Task.CompletedTask,
					expression != null ? Utility.Cache.AddSetMembersAsync(expression.GetSetCacheKey(), results.Item4) : Task.CompletedTask,
					Utility.Logger.IsEnabled(LogLevel.Debug) ? Utility.WriteLogAsync(requestInfo, $"Update cache keys into related sets when generate CMS contents\r\n- Related sets: {new[] { category != null ? category.GetSetCacheKey() : null, contentType != null ? contentType.GetSetCacheKey() : null, expression != null ? expression.GetSetCacheKey() : null }.Where(key => key != null).Join(", ")}\r\n- Related cache keys ({results.Item4.Count}): {results.Item4.Join(", ")}", CancellationToken.None, "Caches") : Task.CompletedTask
				).Run();

				// prepare breadcrumbs
				if (showBreadcrumbs && category != null)
					breadcrumbs = category?.GenerateBreadcrumbs(desktop) ?? new JArray();

				// prepare pagination
				if (showPagination)
				{
					var totalPages = new Tuple<long, int>(totalRecords, pageSize).GetTotalPages();
					if (totalPages > 0 && pageNumber > totalPages)
						pageNumber = totalPages;
					pagination = Utility.GeneratePagination(totalRecords, totalPages, pageSize, pageNumber, category?.GetURL(desktop, true), showPageLinks, numberOfPageLinks, requestInfo.Query?.Where(kvp => !kvp.Key.IsStartsWith("x-")).Select(kvp => $"{kvp.Key}={kvp.Value?.UrlEncode()}").Join("&"));
				}

				// prepare SEO
				seoTitle = category?.Title;
				seoDescription = category?.Description;

				// prepare other info
				categoryThumbnails = category != null ? categoryThumbnails ?? await requestInfo.GetThumbnailsAsync(category.ID, category.Title.Url64Encode(), Utility.ValidationKey, cancellationToken).ConfigureAwait(false) : null;
				coverURI = (categoryThumbnails as JArray)?.First()?.Get<string>("URI")?.GetThumbnailURL(pngThumbnails, bigThumbnails, thumbnailsWidth, thumbnailsHeight);
			}

			// generate details
			else
			{
				// get the requested object
				var @object = await Content.GetContentByAliasAsync(contentTypeID, requestJson.Get<string>("ContentIdentity"), category?.ID, cancellationToken).ConfigureAwait(false);
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

				// validate the published time
				var validatePublishedTime = options.Get("ValidatePublished", options.Get("ValidatePublishedTime", options.Get("ValidateWithPublishedTime", false)));
				if (validatePublishedTime && @object.Status.Equals(ApprovalStatus.Published) && @object.PublishedTime.Value > DateTime.Now)
				{
					if (!isSystemAdministrator && !requestInfo.Session.User.ID.IsEquals(@object.Organization.OwnerID) && !requestInfo.Session.User.ID.IsEquals(@object.CreatedID) && !requestInfo.Session.User.IsEditor(@object.WorkingPrivileges))
						throw new AccessDeniedException();
				}

				var showAttachments = options.Get("ShowAttachments", false);
				var showRelateds = options.Get("ShowRelateds", false);
				var showOthers = options.Get("ShowOthers", false);
				contentTypeID = @object.ContentTypeID;

				// get related contents
				var relateds = new List<Content>();
				var relatedsTask = showRelateds && @object.Relateds != null
					? @object.Relateds.ForEachAsync(async (id, token) => relateds.Add(await Content.GetAsync<Content>(id, token).ConfigureAwait(false)), cancellationToken)
					: Task.CompletedTask;

				// get other contents
				Task<List<Content>> newersTask, oldersTask;
				var numberOfOthers = options.Get("NumberOfOthers", 12);
				numberOfOthers = numberOfOthers > 0 ? numberOfOthers : 12;
				var others = new List<Content>();

				if (showOthers)
				{
					var otherIDs = await Utility.Cache.GetAsync<List<string>>($"{@object.GetCacheKey()}:others", cancellationToken).ConfigureAwait(false);
					if (otherIDs != null && otherIDs.Count > 0)
					{
						newersTask = Task.FromResult(new List<Content>());
						oldersTask = Task.FromResult(new List<Content>());
						await otherIDs.ForEachAsync(async (id, token) => others.Add(await Content.GetAsync<Content>(id, token).ConfigureAwait(false)), cancellationToken).ConfigureAwait(false);
					}
					else
					{
						newersTask = Content.FindAsync(Filters<Content>.And
						(
							Filters<Content>.Equals("RepositoryEntityID", @object.RepositoryEntityID),
							Filters<Content>.Equals("CategoryID", @object.CategoryID),
							Filters<Content>.LessThanOrEquals("StartDate", DateTime.Now.ToDTString(false, false)),
							Filters<Content>.Or
							(
								Filters<Content>.IsNull("EndDate"),
								Filters<Content>.GreaterOrEquals("EndDate", DateTime.Now.ToDTString(false, false))
							),
							Filters<Content>.Equals("Status", ApprovalStatus.Published.ToString()),
							Filters<Content>.GreaterOrEquals("PublishedTime", @object.PublishedTime.Value.GetTimeQuarter())
						), Sorts<Content>.Descending("StartDate").ThenByDescending("PublishedTime"), numberOfOthers, 1, contentTypeID, null, cancellationToken);
						oldersTask = Content.FindAsync(Filters<Content>.And
						(
							Filters<Content>.Equals("RepositoryEntityID", @object.RepositoryEntityID),
							Filters<Content>.Equals("CategoryID", @object.CategoryID),
							Filters<Content>.LessThanOrEquals("StartDate", DateTime.Now.ToDTString(false, false)),
							Filters<Content>.Or
							(
								Filters<Content>.IsNull("EndDate"),
								Filters<Content>.GreaterOrEquals("EndDate", DateTime.Now.ToDTString(false, false))
							),
							Filters<Content>.Equals("Status", ApprovalStatus.Published.ToString()),
							Filters<Content>.LessThanOrEquals("PublishedTime", @object.PublishedTime.Value.GetTimeQuarter())
						), Sorts<Content>.Descending("StartDate").ThenByDescending("PublishedTime"), numberOfOthers, 1, contentTypeID, null, cancellationToken);
					}
				}
				else
				{
					newersTask = Task.FromResult(new List<Content>());
					oldersTask = Task.FromResult(new List<Content>());
				}

				// get files
				requestInfo.Header["x-thumbnails-as-attachments"] = "true";
				var thumbnailsTask = showThumbnails ? requestInfo.GetThumbnailsAsync(@object.ID, @object.Title.Url64Encode(), Utility.ValidationKey, cancellationToken) : Task.FromResult<JToken>(new JArray());
				var attachmentsTask = showAttachments ? requestInfo.GetAttachmentsAsync(@object.ID, @object.Title.Url64Encode(), Utility.ValidationKey, cancellationToken) : Task.FromResult<JToken>(new JArray());

				// wait for all tasks are completed
				await Task.WhenAll(relatedsTask, newersTask, oldersTask, thumbnailsTask, attachmentsTask).ConfigureAwait(false);

				JToken otherThumbnails = null;
				if (showOthers)
				{
					if (newersTask.Result.Count + oldersTask.Result.Count > 0)
					{
						numberOfOthers = newersTask.Result.Count + oldersTask.Result.Count > numberOfOthers ? numberOfOthers / 2 : numberOfOthers;
						others = newersTask.Result.Take(numberOfOthers).Concat(oldersTask.Result.Take(numberOfOthers)).ToList();
					}

					others = others.Where(other => other != null && other.ID != null && other.ID != @object.ID && other.Status.Equals(ApprovalStatus.Published) && other.PublishedTime.Value <= DateTime.Now).ToList();
					if (others.Count > 0)
					{
						var cacheKeyOfOthers = $"{@object.GetCacheKey()}:others";
						if (!await Utility.Cache.ExistsAsync(cacheKeyOfOthers, cancellationToken).ConfigureAwait(false))
							await Utility.Cache.SetAsync(cacheKeyOfOthers, others.Select(other => other.ID).ToList(), Utility.Cache.ExpirationTime / 2, cancellationToken).ConfigureAwait(false);
					}

					otherThumbnails = others.Count < 1
						? null
						: others.Count == 1
							? await requestInfo.GetThumbnailsAsync(others[0].ID, others[0].Title.Url64Encode(), Utility.ValidationKey, cancellationToken).ConfigureAwait(false)
							: await requestInfo.GetThumbnailsAsync(others.Select(obj => obj.ID).Join(","), others.ToJObject("ID", obj => new JValue(obj.Title.Url64Encode())).ToString(Formatting.None), Utility.ValidationKey, cancellationToken).ConfigureAwait(false);
				}

				// generate XML
				var dataXml = XElement.Parse("<Data/>");
				dataXml.Add(@object.ToXml(false, cultureInfo, xml =>
				{
					xml.NormalizeHTMLs(@object);
					xml.Element("StartDate")?.UpdateDateTime(cultureInfo);
					xml.Element("EndDate")?.UpdateDateTime(cultureInfo);

					if (!string.IsNullOrWhiteSpace(@object.Tags))
					{
						var tagsXml = xml.Element("Tags");
						tagsXml.Value = "";
						@object.Tags.ToArray(",", true).ForEach(tag => tagsXml.Add(new XElement("Tag", tag)));
					}

					if (!string.IsNullOrWhiteSpace(@object.Summary))
						xml.Element("Summary").Value = @object.Summary.NormalizeHTMLBreaks();

					xml.Add(new XElement("Category", @object.Category?.Title, new XAttribute("URL", @object.Category?.GetURL(desktop))));
					xml.Add(new XElement("URL", @object.GetURL(desktop)));

					if (showThumbnails)
					{
						var thumbnails = new XElement("Thumbnails");
						(thumbnailsTask.Result as JArray)?.ForEach(thumbnail =>
						{
							var thumbnailURL = thumbnail.Get<string>("URI")?.GetThumbnailURL(pngThumbnails, bigThumbnails, thumbnailsWidth, thumbnailsHeight);
							thumbnails.Add(new XElement("Thumbnail", thumbnailURL, new XAttribute("Alternative", thumbnailURL?.GetWebpImageURL(pngThumbnails) ?? "")));
						});
						xml.Add(thumbnails);
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
						xml.Add(attachments);
					}
				}));

				if (showRelateds)
				{
					var relatedsXml = new XElement("Relateds");
					relateds = relateds.Where(related => related != null && related.ID != null && related.ID != @object.ID && related.Status.Equals(ApprovalStatus.Published) && related.PublishedTime.Value <= DateTime.Now).ToList();
					relateds.OrderByDescending(related => related.StartDate).ThenByDescending(related => related.PublishedTime).ForEach(related =>
					{
						var relatedXml = new XElement("Content", new XElement("ID", related.ID));
						relatedXml.Add(new XElement("Title", related.Title), new XElement("Summary", related.Summary?.NormalizeHTMLBreaks()));
						relatedXml.Add(new XElement("PublishedTime", related.PublishedTime.Value).UpdateDateTime(cultureInfo, customDateTimeFormat));
						relatedXml.Add(new XElement("URL", related.GetURL(desktop)));
						relatedXml.Add(new XElement("Category", related.Category?.Title, new XAttribute("URL", related.Category?.GetURL(desktop))));
						relatedsXml.Add(relatedXml);
					});
					dataXml.Add(relatedsXml);

					var externalsXml = new XElement("ExternalRelateds");
					@object.ExternalRelateds?.ForEach(external => externalsXml.Add(external.ToXml(externalXml =>
					{
						if (!string.IsNullOrWhiteSpace(external.Summary))
							externalXml.Element("Summary").Value = external.Summary.NormalizeHTMLBreaks();
					})));
					dataXml.Add(externalsXml);
				}

				if (showOthers)
				{
					var othersXml = new XElement("Others");
					others.OrderByDescending(other => other.StartDate).ThenByDescending(other => other.PublishedTime).ForEach(other => othersXml.Add(other.ToXml(false, cultureInfo, otherXml =>
					{
						otherXml.Element("Details")?.Remove();
						otherXml.Element("StartDate")?.UpdateDateTime(cultureInfo, customDateTimeFormat);
						otherXml.Element("EndDate")?.UpdateDateTime(cultureInfo, customDateTimeFormat);
						otherXml.Element("PublishedTime")?.UpdateDateTime(cultureInfo, customDateTimeFormat);
						otherXml.Add(new XElement("Category", other.Category?.Title, new XAttribute("URL", other.Category?.GetURL(desktop))));
						otherXml.Add(new XElement("URL", other.GetURL(desktop)));
						var thumbnailURL = otherThumbnails?.GetThumbnailURL(other.ID, pngThumbnails, bigThumbnails, thumbnailsWidth, thumbnailsHeight);						
						otherXml.Add(new XElement("ThumbnailURL", thumbnailURL, new XAttribute("Alternative", thumbnailURL?.GetWebpImageURL(pngThumbnails) ?? "")));
						if (!string.IsNullOrWhiteSpace(other.Summary))
							otherXml.Element("Summary").Value = other.Summary.NormalizeHTMLBreaks();
					})));
					dataXml.Add(othersXml);
				}

				// build others
				breadcrumbs = showBreadcrumbs ? @object.Category?.GenerateBreadcrumbs(desktop) ?? new JArray() : null;
				pagination = showPagination ? Utility.GeneratePagination(1, 1, 0, pageNumber, @object.GetURL(desktop, true), showPageLinks, numberOfPageLinks) : null;
				coverURI = (thumbnailsTask.Result as JArray)?.First()?.Get<string>("URI")?.GetThumbnailURL(pngThumbnails, bigThumbnails, thumbnailsWidth, thumbnailsHeight);
				seoTitle = @object.Title;
				seoDescription = @object.Summary;
				seoKeywords = @object.Tags;

				// main category
				if (category != null)
				{
					requestInfo.Header["x-thumbnails-as-attachments"] = "true";
					var categoryThumbnails = await requestInfo.GetThumbnailsAsync(category.ID, category.Title.Url64Encode(), Utility.ValidationKey, cancellationToken).ConfigureAwait(false);
					var thumbnailURL = categoryThumbnails?.GetThumbnailURL(category.ID, pngThumbnails, bigThumbnails, thumbnailsWidth, thumbnailsHeight);
					dataXml.Add(new XElement(
						"Parent",
						new XElement("Title", category.Title),
						new XElement("Description", category.Description?.NormalizeHTMLBreaks()),
						new XElement("Notes", category.Notes?.NormalizeHTMLBreaks()),
						new XElement("URL", category.GetURL(desktop)),
						new XElement("ThumbnailURL", thumbnailURL, new XAttribute("Alternative", thumbnailURL?.GetWebpImageURL(pngThumbnails) ?? ""))
					));
				}

				// validate and get data of xml
				data = dataXml.CleanInvalidCharacters().ToString(SaveOptions.DisableFormatting);
			}

			// SEO
			seoInfo = new JObject
			{
				{ "Title", seoTitle },
				{ "Description", string.IsNullOrWhiteSpace(seoDescription) || seoDescription.IsStartsWith("~~/") || seoDescription.IsStartsWith("http://") || seoDescription.IsStartsWith("https://") ? null : seoDescription },
				{ "Keywords", seoKeywords }
			};

			// response
			return new JObject
			{
				{ "Data", data },
				{ "Breadcrumbs", breadcrumbs },
				{ "Pagination", pagination },
				{ "FilterBy", filterBy },
				{ "SortBy", sortBy },
				{ "SEOInfo", seoInfo },
				{ "CoverURI", coverURI }
			};
		}

		internal static async Task<JObject> SyncContentAsync(this RequestInfo requestInfo, CancellationToken cancellationToken = default)
		{
			var data = requestInfo.GetBodyExpando();
			var content = await Content.GetAsync<Content>(data.Get<string>("ID"), cancellationToken).ConfigureAwait(false);
			if (content == null)
			{
				content = Content.CreateInstance(data);
				await Content.CreateAsync(content, cancellationToken).ConfigureAwait(false);
			}
			else
			{
				content.Fill(data);
				await Content.UpdateAsync(content, true, cancellationToken).ConfigureAwait(false);
			}

			// clear related cache
			//content.ClearRelatedCacheAsync(null, requestInfo.CorrelationID).Run();

			// send update messages
			var json = content.ToJson();
			var objectName = content.GetObjectName();
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
				{ "ID", content.ID },
				{ "Type", objectName }
			};
		}
	}
}
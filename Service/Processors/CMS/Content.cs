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
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using net.vieapps.Components.Utility;
using net.vieapps.Components.Security;
using net.vieapps.Components.Repository;
using System.Runtime.CompilerServices;
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

		static async Task ClearRelatedCacheAsync(this Content content, IEnumerable<string> categoryIDs, CancellationToken cancellationToken = default)
		{
			var sort = Sorts<Content>.Descending("StartDate").ThenByDescending("PublishedTime");
			var tasks = new List<Task>
			{
				Utility.Cache.RemoveAsync(Extensions.GetRelatedCacheKeys(content.GetCacheKey()), cancellationToken),
				Utility.Cache.RemoveAsync(Extensions.GetRelatedCacheKeys(ContentProcessor.GetContentsFilter(content.SystemID, content.RepositoryID, content.RepositoryEntityID, null), sort), cancellationToken),
				Utility.Cache.RemoveAsync(Extensions.GetRelatedCacheKeys(ContentProcessor.GetContentsFilter(content.SystemID, content.RepositoryID, content.RepositoryEntityID, content.CategoryID), sort), cancellationToken)
			};

			var relatedIDs = (content.OtherCategories ?? new List<string>()).Concat(categoryIDs ?? new List<string>())
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.Where(categoryID => !string.IsNullOrWhiteSpace(categoryID) && !categoryID.IsEquals(content.CategoryID) && categoryID.IsValidUUID());

			relatedIDs.ForEach(categoryID => tasks.Add(Utility.Cache.RemoveAsync(Extensions.GetRelatedCacheKeys(ContentProcessor.GetContentsFilter(content.SystemID, content.RepositoryID, content.RepositoryEntityID, categoryID), sort), cancellationToken)));

			var categoryAliases = new List<string>();
			await new[] { content.CategoryID }.Concat(relatedIDs).Distinct(StringComparer.OrdinalIgnoreCase).ForEachAsync(async (categoryID, token) => categoryAliases.Add((await categoryID.GetCategoryByIDAsync(cancellationToken).ConfigureAwait(false))?.Alias), cancellationToken).ConfigureAwait(false);
			tasks.Add(Utility.RTUService.SendClearCacheRequestAsync(content.ContentType?.ID, Extensions.GetCacheKey<Content>(), categoryAliases, cancellationToken));

			await Task.WhenAll(tasks).ConfigureAwait(false);
		}

		static async Task<Tuple<long, List<Content>, JToken>> SearchAsync(this RequestInfo requestInfo, string query, IFilterBy<Content> filter, SortBy<Content> sort, int pageSize, int pageNumber, string contentTypeID = null, long totalRecords = -1, CancellationToken cancellationToken = default, string cacheKeyPrefix = null, bool searchThumbnails = true, bool pngThumbnails = false, bool bigThumbnails = false, int thumbnailsWidth = 0, int thumbnailsHeight = 0)
		{
			// count
			totalRecords = totalRecords > -1
				? totalRecords
				: string.IsNullOrWhiteSpace(query)
					? await Content.CountAsync(filter, contentTypeID, true, string.IsNullOrWhiteSpace(cacheKeyPrefix) ? Extensions.GetCacheKeyOfTotalObjects(filter, sort) : Extensions.GetCacheKeyOfTotalObjects<Content>(cacheKeyPrefix), 0, cancellationToken).ConfigureAwait(false)
					: await Content.CountAsync(query, filter, contentTypeID, cancellationToken).ConfigureAwait(false);

			// search objects
			var objects = totalRecords > 0
				? string.IsNullOrWhiteSpace(query)
					? await Content.FindAsync(filter, sort, pageSize, pageNumber, contentTypeID, true, string.IsNullOrWhiteSpace(cacheKeyPrefix) ? Extensions.GetCacheKey(filter, sort, pageSize, pageNumber) : Extensions.GetCacheKey<Content>(cacheKeyPrefix, pageSize, pageNumber), 0, cancellationToken).ConfigureAwait(false)
					: await Content.SearchAsync(query, filter, pageSize, pageNumber, contentTypeID, cancellationToken).ConfigureAwait(false)
				: new List<Content>();

			// search thumbnails
			objects = objects.Where(@object => @object != null && !string.IsNullOrWhiteSpace(@object.ID)).ToList();

			JToken thumbnails = null;
			if (objects.Count > 0 && searchThumbnails)
			{
				requestInfo.Header["x-thumbnails-as-attachments"] = "true";
				requestInfo.Header["x-thumbnails-as-png"] = $"{pngThumbnails}".ToLower();
				requestInfo.Header["x-thumbnails-as-big"] = $"{bigThumbnails}".ToLower();
				requestInfo.Header["x-thumbnails-width"] = $"{thumbnailsWidth}";
				requestInfo.Header["x-thumbnails-height"] = $"{thumbnailsHeight}";
				thumbnails = objects.Count == 1
					? await requestInfo.GetThumbnailsAsync(objects[0].ID, objects[0].Title.Url64Encode(), Utility.ValidationKey, cancellationToken).ConfigureAwait(false)
					: await requestInfo.GetThumbnailsAsync(objects.Select(@object => @object.ID).Join(","), objects.ToJObject("ID", @object => new JValue(@object.Title.Url64Encode())).ToString(Formatting.None), Utility.ValidationKey, cancellationToken).ConfigureAwait(false);
			}

			// page size to clear related cached
			Utility.SetCacheOfPageSizeAsync(filter, sort, cacheKeyPrefix, pageSize, cancellationToken).Run();

			// return the results
			return new Tuple<long, List<Content>, JToken>(totalRecords, objects, thumbnails);
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
			filter = filter == null || !(filter is FilterBys<Content>) || (filter as FilterBys<Content>).Children == null || (filter as FilterBys<Content>).Children.Count < 1
				? ContentProcessor.GetContentsFilter(organization.ID, module.ID, contentType.ID, category?.ID)
				: filter.Prepare(requestInfo);

			// process cache
			var json = string.IsNullOrWhiteSpace(query) ? await Utility.Cache.GetAsync<string>(Extensions.GetCacheKeyOfObjectsJson(filter, sort, pageSize, pageNumber), cancellationToken).ConfigureAwait(false) : null;
			if (!string.IsNullOrWhiteSpace(json))
				return JObject.Parse(json);

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
				{ "Objects", objects.Select(@object => @object.ToJson(false, cjson =>
					{
						cjson["Thumbnails"] = thumbnails?.GetThumbnails(@object.ID);
						cjson["Details"] = organization.NormalizeURLs(@object.Details);
					})).ToJArray()
				}
			};

			// update cache
			if (string.IsNullOrWhiteSpace(query))
			{
				json = response.ToString(Formatting.None);
				Utility.Cache.SetAsync(Extensions.GetCacheKeyOfObjectsJson(filter, sort, pageSize, pageNumber), json, Utility.Cache.ExpirationTime / 2).Run();
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
			content.ClearRelatedCacheAsync(null, cancellationToken).Run();

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
				ExcludedDeviceID = requestInfo.Session.DeviceID,
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
			content.ClearRelatedCacheAsync(new[] { oldCategoryID }.Concat(oldOtherCategories ?? new List<string>()), cancellationToken).Run();

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
			content.ClearRelatedCacheAsync(null, cancellationToken).Run();

			// send update message
			var response = content.ToJson();
			await Utility.RTUService.SendUpdateMessageAsync(new UpdateMessage
			{
				Type = $"{requestInfo.ServiceName}#{content.GetObjectName()}#Delete",
				DeviceID = "*",
				ExcludedDeviceID = requestInfo.Session.DeviceID,
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
			var category = await parentContentTypeJson.Get<string>("ID", "").GetCategoryByAliasAsync(requestJson.Get<string>("ParentIdentity"), cancellationToken).ConfigureAwait(false);

			var paginationJson = requestJson.Get("Pagination", new JObject());
			var pageSize = paginationJson.Get<int>("PageSize", 7);
			var pageNumber = paginationJson.Get<int>("PageNumber", 1);
			var showPageLinks = paginationJson.Get<bool>("ShowPageLinks", true);
			var numberOfPageLinks = paginationJson.Get<int>("NumberOfPageLinks", 7);

			var cultureInfo = CultureInfo.GetCultureInfo(requestJson.Get("Language", "vi-VN"));
			var action = requestJson.Get<string>("Action");
			var isList = string.IsNullOrWhiteSpace(action) || "List".IsEquals(action);

			var desktop = desktopsJson.Get<string>("Specified");
			desktop = !string.IsNullOrWhiteSpace(desktop) ? desktop : desktopsJson.Get<string>("ContentType");
			desktop = !string.IsNullOrWhiteSpace(desktop) ? desktop : desktopsJson.Get<string>("Module");
			desktop = !string.IsNullOrWhiteSpace(desktop) ? desktop : desktopsJson.Get<string>("Default");

			XElement data;
			JArray breadcrumbs;
			JObject pagination, seoInfo, filterBy = null, sortBy = null;
			string coverURI = null;

			var showThumbnails = options.Get("ShowThumbnails", options.Get("ShowThumbnail", true)) || options.Get("ShowPngThumbnails", false) || options.Get("ShowAsPngThumbnails", false) || options.Get("ShowBigThumbnails", false) || options.Get("ShowAsBigThumbnails", false);
			var pngThumbnails = options.Get("ThumbnailsAsPng", options.Get("ThumbnailAsPng", options.Get("ShowPngThumbnails", options.Get("ShowAsPngThumbnails", false))));
			var bigThumbnails = options.Get("ThumbnailsAsBig", options.Get("ThumbnailAsBig", options.Get("ShowBigThumbnails", options.Get("ShowAsBigThumbnails", false))));
			var thumbnailsWidth = options.Get("ThumbnailsWidth", options.Get("ThumbnailWidth", 0));
			var thumbnailsHeight = options.Get("ThumbnailsHeight", options.Get("ThumbnailHeight", 0));

			// generate list
			if (isList)
			{
				// check permission
				var contentType = await (contentTypeID ?? "").GetContentTypeByIDAsync(cancellationToken).ConfigureAwait(false);
				var gotRights = isSystemAdministrator || requestInfo.Session.User.IsViewer(category?.WorkingPrivileges, contentType?.WorkingPrivileges);
				if (!gotRights)
				{
					var organization = contentType?.Organization ?? await organizationJson.Get<string>("ID", "").GetOrganizationByIDAsync(cancellationToken).ConfigureAwait(false);
					gotRights = requestInfo.Session.User.ID.IsEquals(organization?.OwnerID);
				}
				if (!gotRights)
					throw new AccessDeniedException();

				// prepare filtering expression
				if (!(expressionJson.Get<JObject>("FilterBy")?.ToFilter<Content>() is FilterBys<Content> filter) || filter.Children == null || filter.Children.Count < 1)
				{
					filter = Filters<Content>.And(
						Filters<Content>.Equals("SystemID", "@request.Body(Organization.ID)"),
						Filters<Content>.Equals("RepositoryID", "@request.Body(Module.ID)"),
						Filters<Content>.Equals("RepositoryEntityID", "@request.Body(ContentType.ID)")
					);

					if (category != null)
						filter.Add(Filters<Content>.Equals("CategoryID", category.ID));

					filter.Add(
						Filters<Content>.LessThanOrEquals("StartDate", "@today"),
						Filters<Content>.Or(
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
					filter.Add(Filters<Content>.Or(
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

				// get XML from cache
				var cacheKeyPrefix = requestJson.GetCacheKeyPrefix();
				var cacheKey = cacheKeyPrefix != null ? Extensions.GetCacheKeyOfObjectsXml<Content>(cacheKeyPrefix, pageSize, pageNumber) : null;
				var xml = cacheKey != null ? await Utility.Cache.GetAsync<string>(cacheKey, cancellationToken).ConfigureAwait(false) : null;

				JToken thumbnails = null, categoryThumbnails = null;
				long totalRecords = 0;

				// search and build XML if has no cache
				if (string.IsNullOrWhiteSpace(xml))
				{
					// search
					var results = await requestInfo.SearchAsync(null, filter, sort, pageSize, pageNumber, contentTypeID, -1, cancellationToken, cacheKeyPrefix, showThumbnails, pngThumbnails, bigThumbnails, thumbnailsWidth, thumbnailsHeight).ConfigureAwait(false);
					totalRecords = results.Item1;
					var objects = results.Item2;
					thumbnails = results.Item3;

					// generate xml
					data = XElement.Parse("<Data/>");
					objects.ForEach(@object => data.Add(@object.ToXml(false, cultureInfo, element =>
					{
						element.Element("Details")?.Remove();
						element.Element("StartDate")?.UpdateDateTime(cultureInfo);
						element.Element("EndDate")?.UpdateDateTime(cultureInfo);
						if (!string.IsNullOrWhiteSpace(@object.Summary))
							element.Element("Summary").Value = @object.Summary.NormalizeHTMLBreaks().CleanInvalidXmlCharacters();
						element.Add(new XElement("Category", @object.Category?.Title, new XAttribute("URL", @object.Category?.GetURL(desktop))));
						element.Add(new XElement("URL", @object.GetURL(desktop)));
						element.Add(new XElement("ThumbnailURL", thumbnails?.GetThumbnailURL(@object.ID)));
					})));

					// main category
					if (category != null)
					{
						if (objects.Count < 1 || !showThumbnails)
						{
							requestInfo.Header["x-thumbnails-as-attachments"] = "true";
							requestInfo.Header["x-thumbnails-as-png"] = $"{pngThumbnails}".ToLower();
							requestInfo.Header["x-thumbnails-as-big"] = $"{bigThumbnails}".ToLower();
							requestInfo.Header["x-thumbnails-width"] = $"{thumbnailsWidth}";
							requestInfo.Header["x-thumbnails-height"] = $"{thumbnailsHeight}";
						}
						categoryThumbnails = await requestInfo.GetThumbnailsAsync(category.ID, category.Title.Url64Encode(), Utility.ValidationKey, cancellationToken).ConfigureAwait(false);
						data.Add(new XElement(
							"Parent",
							new XElement("Title", category.Title.CleanInvalidXmlCharacters()),
							new XElement("Description", category.Description?.NormalizeHTMLBreaks().CleanInvalidXmlCharacters()),
							new XElement("URL", category.GetURL(desktop).CleanInvalidXmlCharacters()),
							new XElement("ThumbnailURL", categoryThumbnails?.GetThumbnailURL(category.ID) ?? "")
						));
					}

					// update XML into cache
					if (cacheKey != null)
						Utility.Cache.SetAsync(cacheKey, data.ToString(SaveOptions.DisableFormatting), cancellationToken).Run();
				}
				else
				{
					data = XElement.Parse(xml);
					totalRecords = await Content.CountAsync(filter, contentTypeID, true, Extensions.GetCacheKeyOfTotalObjects<Content>(cacheKeyPrefix), 0, cancellationToken).ConfigureAwait(false);
				}

				// prepare breadcrumbs
				breadcrumbs = category?.GenerateBreadcrumbs(desktop) ?? new JArray();

				// prepare pagination
				var totalPages = new Tuple<long, int>(totalRecords, pageSize).GetTotalPages();
				if (totalPages > 0 && pageNumber > totalPages)
					pageNumber = totalPages;
				pagination = Utility.GeneratePagination(totalRecords, totalPages, pageSize, pageNumber, category?.GetURL(desktop, true), showPageLinks, numberOfPageLinks);

				// prepare SEO and other info
				seoInfo = new JObject
				{
					{ "Title", category?.Title },
					{ "Description", category?.Description }
				};
				categoryThumbnails = category != null ? categoryThumbnails ?? await requestInfo.GetThumbnailsAsync(category.ID, category.Title.Url64Encode(), Utility.ValidationKey, cancellationToken).ConfigureAwait(false) : null;
				coverURI = (categoryThumbnails as JArray)?.First()?.Get<string>("URI");
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
				Task<List<Content>>  newersTask, oldersTask;
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
						newersTask = Content.FindAsync(Filters<Content>.And(
							Filters<Content>.Equals("RepositoryEntityID", @object.RepositoryEntityID),
							Filters<Content>.Equals("CategoryID", @object.CategoryID),
							Filters<Content>.LessThanOrEquals("StartDate", DateTime.Now.ToDTString(false, false)),
							Filters<Content>.Or(
								Filters<Content>.IsNull("EndDate"),
								Filters<Content>.GreaterOrEquals("EndDate", DateTime.Now.ToDTString(false, false))
							),
							Filters<Content>.Equals("Status", ApprovalStatus.Published.ToString()),
							Filters<Content>.GreaterOrEquals("PublishedTime", @object.PublishedTime.Value.GetTimeQuarter())
						), Sorts<Content>.Descending("StartDate").ThenByDescending("PublishedTime"), numberOfOthers, 1, contentTypeID, null, cancellationToken);
						oldersTask = Content.FindAsync(Filters<Content>.And(
							Filters<Content>.Equals("RepositoryEntityID", @object.RepositoryEntityID),
							Filters<Content>.Equals("CategoryID", @object.CategoryID),
							Filters<Content>.LessThanOrEquals("StartDate", DateTime.Now.ToDTString(false, false)),
							Filters<Content>.Or(
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
				if (showThumbnails)
				{
					requestInfo.Header["x-thumbnails-as-attachments"] = "true";
					requestInfo.Header["x-thumbnails-as-png"] = $"{pngThumbnails}".ToLower();
					requestInfo.Header["x-thumbnails-as-big"] = $"{bigThumbnails}".ToLower();
					requestInfo.Header["x-thumbnails-width"] = $"{thumbnailsWidth}";
					requestInfo.Header["x-thumbnails-height"] = $"{thumbnailsHeight}";
				}
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
				data = XElement.Parse("<Data/>");
				data.Add(@object.ToXml(false, cultureInfo, xml =>
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
						xml.Element("Summary").Value = @object.Summary.NormalizeHTMLBreaks().CleanInvalidXmlCharacters();

					xml.Add(new XElement("Category", @object.Category?.Title, new XAttribute("URL", @object.Category?.GetURL(desktop))));
					xml.Add(new XElement("URL", @object.GetURL(desktop)));

					if (showThumbnails)
					{
						var thumbnails = new XElement("Thumbnails");
						(thumbnailsTask.Result as JArray)?.ForEach(thumbnail => thumbnails.Add(new XElement("Thumbnail", thumbnail.Get<string>("URI"))));
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
						relatedXml.Add(new XElement("Title", related.Title), new XElement("Summary", related.Summary?.NormalizeHTMLBreaks().CleanInvalidXmlCharacters()));
						relatedXml.Add(new XElement("PublishedTime", related.PublishedTime.Value).UpdateDateTime(cultureInfo));
						relatedXml.Add(new XElement("URL", related.GetURL(desktop)));
						relatedsXml.Add(relatedXml);
					});
					data.Add(relatedsXml);

					var externalsXml = new XElement("ExternalRelateds");
					@object.ExternalRelateds?.ForEach(external => externalsXml.Add(external.ToXml(externalXml =>
					{
						if (!string.IsNullOrWhiteSpace(external.Summary))
							externalXml.Element("Summary").Value = external.Summary.NormalizeHTMLBreaks().CleanInvalidXmlCharacters();
					})));
					data.Add(externalsXml);
				}

				if (showOthers)
				{
					var othersXml = new XElement("Others");
					others.OrderByDescending(other => other.StartDate).ThenByDescending(other => other.PublishedTime).ForEach(other => othersXml.Add(other.ToXml(false, cultureInfo, otherXml =>
					{
						otherXml.Element("Details")?.Remove();
						otherXml.Element("StartDate")?.UpdateDateTime(cultureInfo);
						otherXml.Element("EndDate")?.UpdateDateTime(cultureInfo);
						if (!string.IsNullOrWhiteSpace(other.Summary))
							otherXml.Element("Summary").Value = other.Summary.NormalizeHTMLBreaks().CleanInvalidXmlCharacters();
						otherXml.Add(new XElement("Category", other.Category?.Title, new XAttribute("URL", other.Category?.GetURL(desktop))));
						otherXml.Add(new XElement("URL", other.GetURL(desktop)));
						otherXml.Add(new XElement("ThumbnailURL", otherThumbnails?.GetThumbnailURL(other.ID)));
					})));
					data.Add(othersXml);
				}

				// build others
				breadcrumbs = @object.Category?.GenerateBreadcrumbs(desktop) ?? new JArray();
				pagination = Utility.GeneratePagination(1, 1, 0, pageNumber, @object.GetURL(desktop, true), showPageLinks, numberOfPageLinks);
				coverURI = (thumbnailsTask.Result as JArray)?.First()?.Get<string>("URI");
				seoInfo = new JObject
				{
					{ "Title", @object.Title },
					{ "Description", @object.Summary },
					{ "Keywords", @object.Tags }
				};

				// main category
				if (category != null)
				{
					if (!showThumbnails)
					{
						requestInfo.Header["x-thumbnails-as-attachments"] = "true";
						requestInfo.Header["x-thumbnails-as-png"] = $"{pngThumbnails}".ToLower();
						requestInfo.Header["x-thumbnails-as-big"] = $"{bigThumbnails}".ToLower();
						requestInfo.Header["x-thumbnails-width"] = $"{thumbnailsWidth}";
						requestInfo.Header["x-thumbnails-height"] = $"{thumbnailsHeight}";
					}
					var categoryThumbnails = await requestInfo.GetThumbnailsAsync(category.ID, category.Title.Url64Encode(), Utility.ValidationKey, cancellationToken).ConfigureAwait(false);
					data.Add(new XElement(
						"Parent",
						new XElement("Title", category.Title.CleanInvalidXmlCharacters()),
						new XElement("Description", category.Description?.NormalizeHTMLBreaks().CleanInvalidXmlCharacters()),
						new XElement("URL", category.GetURL(desktop).CleanInvalidXmlCharacters()),
						new XElement("ThumbnailURL", categoryThumbnails?.GetThumbnailURL(category.ID) ?? "")
					));
				}
			}

			// response
			return new JObject
			{
				{ "Data", data.ToString(SaveOptions.DisableFormatting) },
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
﻿#region Related components
using System;
using System.Linq;
using System.Xml.Linq;
using System.Dynamic;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Concurrent;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using net.vieapps.Components.Utility;
using net.vieapps.Components.Security;
using net.vieapps.Components.Repository;
#endregion

namespace net.vieapps.Services.Portals
{
	public static class ContentProcessor
	{
		public static Content CreateContentInstance(this ExpandoObject requestBody, string excluded = null, Action<Content> onCompleted = null)
			=> requestBody.Copy<Content>(excluded?.ToHashSet(), content =>
			{
				content.TrimAll();
				content.OriginalPrivileges = content.OriginalPrivileges?.Normalize();
				content.Tags = string.IsNullOrWhiteSpace(content.Tags) ? null : content.Tags.Replace(";", ",").ToList(",", true).Where(tag => !string.IsNullOrWhiteSpace(tag)).Join(",");
				onCompleted?.Invoke(content);
			});

		public static Content UpdateContentInstance(this Content content, ExpandoObject requestBody, string excluded = null, Action<Content> onCompleted = null)
		{
			content.CopyFrom(requestBody, excluded?.ToHashSet());
			content.TrimAll();
			content.OriginalPrivileges = content.OriginalPrivileges?.Normalize();
			content.Tags = string.IsNullOrWhiteSpace(content.Tags) ? null : content.Tags.Replace(";", ",").ToList(",", true).Where(tag => !string.IsNullOrWhiteSpace(tag)).Join(",");
			onCompleted?.Invoke(content);
			return content;
		}

		public static IFilterBy<Content> GetContentsFilter(this string systemID, string repositoryID = null, string repositoryEntityID = null, string categoryID = null, Action<FilterBys<Content>> onCompleted = null)
		{
			var filter = Filters<Content>.And(Filters<Content>.Equals("SystemID", systemID));
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

		static Task ClearRelatedCache(this Content content, string oldCategoryID = null, CancellationToken cancellationToken = default)
		{
			var sort = Sorts<Content>.Descending("StartDate").ThenByDescending("PublishedTime");
			var tasks = new List<Task>
			{
				Utility.Cache.RemoveAsync(Extensions.GetRelatedCacheKeys(content.SystemID.GetContentsFilter(content.RepositoryID, content.RepositoryEntityID, null), sort), cancellationToken),
				Utility.Cache.RemoveAsync(Extensions.GetRelatedCacheKeys(content.SystemID.GetContentsFilter(content.RepositoryID, content.RepositoryEntityID, content.CategoryID), sort), cancellationToken)
			};
			content.OtherCategories?.ForEach(categoryID => tasks.Add(Utility.Cache.RemoveAsync(Extensions.GetRelatedCacheKeys(content.SystemID.GetContentsFilter(content.RepositoryID, content.RepositoryEntityID, categoryID), sort), cancellationToken)));
			if (!string.IsNullOrWhiteSpace(oldCategoryID) && oldCategoryID.IsValidUUID())
				tasks.Add(Utility.Cache.RemoveAsync(Extensions.GetRelatedCacheKeys(content.SystemID.GetContentsFilter(content.RepositoryID, content.RepositoryEntityID, oldCategoryID), sort), cancellationToken));
			return Task.WhenAll(tasks);
		}

		static async Task<Tuple<long, List<Content>, JToken>> SearchAsync(this RequestInfo requestInfo, string query, IFilterBy<Content> filter, SortBy<Content> sort, int pageSize, int pageNumber, string contentTypeID = null, long totalRecords = -1, string validationKey = null, CancellationToken cancellationToken = default, bool searchThumbnails = true)
		{
			// count
			totalRecords = totalRecords > -1
				? totalRecords
				: string.IsNullOrWhiteSpace(query)
					? await Content.CountAsync(filter, contentTypeID, true, Extensions.GetCacheKeyOfTotalObjects(filter, sort), 0, cancellationToken).ConfigureAwait(false)
					: await Content.CountAsync(query, filter, contentTypeID, cancellationToken).ConfigureAwait(false);

			// search objects
			var objects = totalRecords > 0
				? string.IsNullOrWhiteSpace(query)
					? await Content.FindAsync(filter, sort, pageSize, pageNumber, contentTypeID, true, Extensions.GetCacheKey(filter, sort, pageSize, pageNumber), 0, cancellationToken).ConfigureAwait(false)
					: await Content.SearchAsync(query, filter, pageSize, pageNumber, contentTypeID, cancellationToken).ConfigureAwait(false)
				: new List<Content>();

			// search thumbnails
			requestInfo.Header["x-as-attachments"] = "true";
			var thumbnails = objects.Count < 1 || !searchThumbnails
				? null
				: objects.Count == 1
					? await requestInfo.GetThumbnailsAsync(objects[0].ID, objects[0].Title.Url64Encode(), cancellationToken, validationKey).ConfigureAwait(false)
					: await requestInfo.GetThumbnailsAsync(objects.Select(@object => @object.ID).Join(","), objects.ToJObject("ID", @object => new JValue(@object.Title.Url64Encode())).ToString(Formatting.None), cancellationToken, validationKey).ConfigureAwait(false);

			// return the results
			return new Tuple<long, List<Content>, JToken>(totalRecords, objects, thumbnails);
		}

		static Task<Tuple<long, List<Content>, JToken>> SearchAsync(this RequestInfo requestInfo, IFilterBy<Content> filter, SortBy<Content> sort, int pageSize, int pageNumber, string contentTypeID = null, long totalRecords = -1, string validationKey = null, CancellationToken cancellationToken = default, bool searchThumbnails = true)
			=> requestInfo.SearchAsync(null, filter, sort, pageSize, pageNumber, contentTypeID, totalRecords, validationKey, cancellationToken, searchThumbnails);

		internal static async Task<JObject> SearchContentsAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, string validationKey = null, CancellationToken cancellationToken = default)
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

			var moduleID = filter?.GetValue("RepositoryID") ?? requestInfo.GetParameter("RepositoryID") ?? requestInfo.GetParameter("x-module");
			var module = await (moduleID ?? "").GetModuleByIDAsync(cancellationToken).ConfigureAwait(false);
			if (module == null || !module.SystemID.IsEquals(organization.ID))
				throw new InformationInvalidException("The module is invalid");

			var contentTypeID = filter?.GetValue("RepositoryEntityID") ?? requestInfo.GetParameter("RepositoryEntityID") ?? requestInfo.GetParameter("x-content-type");
			var contentType = await (contentTypeID ?? "").GetContentTypeByIDAsync(cancellationToken).ConfigureAwait(false);
			if (contentType == null || !contentType.SystemID.IsEquals(organization.ID) || !contentType.RepositoryID.IsEquals(module.ID))
				throw new InformationInvalidException("The content-type is invalid");

			var categoryID = filter?.GetValue("CategoryID") ?? requestInfo.GetParameter("CategoryID") ?? requestInfo.GetParameter("x-category");
			var category = await (categoryID ?? "").GetCategoryByIDAsync(cancellationToken).ConfigureAwait(false);

			// check permission
			var gotRights = isSystemAdministrator || requestInfo.Session.User.ID.IsEquals(organization.OwnerID) || requestInfo.Session.User.IsViewer(category?.WorkingPrivileges, contentType.WorkingPrivileges);
			if (!gotRights)
				throw new AccessDeniedException();

			// normalize filter
			filter = filter == null || !(filter is FilterBys<Content>) || (filter as FilterBys<Content>).Children == null || (filter as FilterBys<Content>).Children.Count < 1
				? organization.ID.GetContentsFilter(module.ID, contentType.ID, category?.ID)
				: filter.Prepare(requestInfo);

			// process cache
			var json = string.IsNullOrWhiteSpace(query) ? await Utility.Cache.GetAsync<string>(Extensions.GetCacheKeyOfObjectsJson(filter, sort, pageSize, pageNumber), cancellationToken).ConfigureAwait(false) : null;
			if (!string.IsNullOrWhiteSpace(json))
				return JObject.Parse(json);

			// search if has no cache
			var results = await requestInfo.SearchAsync(query, filter, sort, pageSize, pageNumber, contentType.ID, pagination.Item1 > -1 ? pagination.Item1 : -1, validationKey, cancellationToken).ConfigureAwait(false);
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
				{ "Objects", objects.Select(@object => @object.ToJson(false, cjson => cjson["Thumbnails"] = thumbnails == null ? null : objects.Count == 1 ? thumbnails : thumbnails[@object.ID] ?? thumbnails["@" + @object.ID])).ToJArray() }
			};

			// update cache
			if (string.IsNullOrWhiteSpace(query))
			{
#if DEBUG
				json = response.ToString(Formatting.Indented);
#else
				json = response.ToString(Formatting.Indented);
#endif
				await Utility.Cache.SetAsync(Extensions.GetCacheKeyOfObjectsJson(filter, sort, pageSize, pageNumber), json, Utility.Cache.ExpirationTime / 2).ConfigureAwait(false);
			}

			// response
			return response;
		}

		internal static async Task<JObject> CreateContentAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, IRTUService rtuService = null, string validationKey = null, CancellationToken cancellationToken = default)
		{
			// prepare
			var request = requestInfo.GetBodyExpando();

			var organizationID = request.Get<string>("SystemID") ?? requestInfo.GetParameter("SystemID") ?? requestInfo.GetParameter("x-system-id");
			var organization = await (organizationID ?? "").GetOrganizationByIDAsync(cancellationToken).ConfigureAwait(false);
			if (organization == null)
				throw new InformationInvalidException("The organization is invalid");

			var moduleID = request.Get<string>("RepositoryID") ?? requestInfo.GetParameter("RepositoryID") ?? requestInfo.GetParameter("x-module");
			var module = await (moduleID ?? "").GetModuleByIDAsync(cancellationToken).ConfigureAwait(false);
			if (module == null || !module.SystemID.IsEquals(organization.ID))
				throw new InformationInvalidException("The module is invalid");

			var contentTypeID = request.Get<string>("RepositoryEntityID") ?? requestInfo.GetParameter("RepositoryEntityID") ?? requestInfo.GetParameter("x-content-type");
			var contentType = await (contentTypeID ?? "").GetContentTypeByIDAsync(cancellationToken).ConfigureAwait(false);
			if (contentType == null || !contentType.SystemID.IsEquals(organization.ID) || !contentType.RepositoryID.IsEquals(module.ID))
				throw new InformationInvalidException("The content-type is invalid");

			var categoryID = request.Get<string>("CategoryID") ?? requestInfo.GetParameter("CategoryID") ?? requestInfo.GetParameter("x-category");
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

			content.OtherCategories = content.OtherCategories?.Where(id => !content.CategoryID.IsEquals(id)).ToList();
			content.OtherCategories = content.OtherCategories != null && content.OtherCategories.Count > 0 ? content.OtherCategories : null;

			content.Relateds = content.Relateds?.Where(id => !string.IsNullOrWhiteSpace(id)).ToList();
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
			await Task.WhenAll(
				Content.CreateAsync(content, cancellationToken),
				content.ClearRelatedCache(null, cancellationToken)
			).ConfigureAwait(false);

			// send update message and response
			var response = content.ToJson();
			response["Thumbnails"] = await requestInfo.GetThumbnailsAsync(content.ID, content.Title.Url64Encode(), cancellationToken, validationKey).ConfigureAwait(false);
			response["Attachments"] = await requestInfo.GetAttachmentsAsync(content.ID, content.Title.Url64Encode(), cancellationToken, validationKey).ConfigureAwait(false);
			await (rtuService == null ? Task.CompletedTask : rtuService.SendUpdateMessageAsync(new UpdateMessage
			{
				Type = $"{requestInfo.ServiceName}#{content.GetObjectName()}#Create",
				DeviceID = "*",
				ExcludedDeviceID = requestInfo.Session.DeviceID,
				Data = response
			})).ConfigureAwait(false);
			return response;
		}

		internal static async Task<JObject> GetContentAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, IRTUService rtuService = null, string validationKey = null, CancellationToken cancellationToken = default)
		{
			// prepare
			var identity = requestInfo.GetObjectIdentity() ?? "";
			var content = await (identity.IsValidUUID() ? Content.GetAsync<Content>(identity, cancellationToken) : Content.GetContentByAliasAsync(requestInfo.GetParameter("RepositoryEntityID") ?? requestInfo.GetParameter("x-content-type"), identity, requestInfo.GetParameter("Category") ?? requestInfo.GetParameter("x-category"), cancellationToken)).ConfigureAwait(false);
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

			// send update message and response
			var response = content.ToJson();
			response["Thumbnails"] = await requestInfo.GetThumbnailsAsync(content.ID, content.Title.Url64Encode(), cancellationToken, validationKey).ConfigureAwait(false);
			response["Attachments"] = await requestInfo.GetAttachmentsAsync(content.ID, content.Title.Url64Encode(), cancellationToken, validationKey).ConfigureAwait(false);
			await (rtuService == null ? Task.CompletedTask : rtuService.SendUpdateMessageAsync(new UpdateMessage
			{
				Type = $"{requestInfo.ServiceName}#{content.GetObjectName()}#Update",
				DeviceID = "*",
				ExcludedDeviceID = requestInfo.Session.DeviceID,
				Data = response
			})).ConfigureAwait(false);
			return response;
		}

		internal static async Task<JObject> UpdateContentAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, IRTUService rtuService = null, string validationKey = null, CancellationToken cancellationToken = default)
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
			var oldAlias = content.Alias;

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

			content.OtherCategories = content.OtherCategories?.Where(id => !content.CategoryID.IsEquals(id)).ToList();
			content.OtherCategories = content.OtherCategories != null && content.OtherCategories.Count > 0 ? content.OtherCategories : null;

			content.Relateds = content.Relateds?.Where(id => !string.IsNullOrWhiteSpace(id)).ToList();
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
			await Task.WhenAll(
				Content.UpdateAsync(content, requestInfo.Session.User.ID, cancellationToken),
				content.ClearRelatedCache(oldCategoryID, cancellationToken)
			).ConfigureAwait(false);

			// send update message and response
			var response = content.ToJson();
			response["Thumbnails"] = await requestInfo.GetThumbnailsAsync(content.ID, content.Title.Url64Encode(), cancellationToken, validationKey).ConfigureAwait(false);
			response["Attachments"] = await requestInfo.GetAttachmentsAsync(content.ID, content.Title.Url64Encode(), cancellationToken, validationKey).ConfigureAwait(false);
			await (rtuService == null ? Task.CompletedTask : rtuService.SendUpdateMessageAsync(new UpdateMessage
			{
				Type = $"{requestInfo.ServiceName}#{content.GetObjectName()}#Update",
				DeviceID = "*",
				ExcludedDeviceID = requestInfo.Session.DeviceID,
				Data = response
			})).ConfigureAwait(false);
			return response;
		}

		internal static async Task<JObject> DeleteContentAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, IRTUService rtuService = null, string validationKey = null, CancellationToken cancellationToken = default)
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
			await requestInfo.DeleteFilesAsync(content.SystemID, content.RepositoryEntityID, content.ID, cancellationToken, validationKey).ConfigureAwait(false);

			// delete content
			await Content.DeleteAsync<Content>(content.ID, requestInfo.Session.User.ID, cancellationToken).ConfigureAwait(false);
			await content.ClearRelatedCache(null, cancellationToken).ConfigureAwait(false);

			// send update message and response
			var response = content.ToJson();
			await (rtuService == null ? Task.CompletedTask : rtuService.SendUpdateMessageAsync(new UpdateMessage
			{
				Type = $"{requestInfo.ServiceName}#{content.GetObjectName()}#Delete",
				DeviceID = "*",
				ExcludedDeviceID = requestInfo.Session.DeviceID,
				Data = response
			})).ConfigureAwait(false);
			return response;
		}

		static JArray GenerateBreadcrumb(this Category category, JToken requestJson, bool alwaysUseHtmlSuffix)
		{
			var breadcrumbs = new List<Tuple<string, string>>();
			var desktop = requestJson.Get<JObject>("ContentType")?.Get<string>("Desktop") ?? requestJson.Get<JObject>("Module")?.Get<string>("Desktop") ?? requestJson.Get<JObject>("Organization")?.Get<string>("DefaultDesktop") ?? requestJson.Get<string>("Desktop");

			var url = category.OpenBy.Equals(OpenBy.DesktopOnly)
				? $"~/{category.Desktop?.Alias ?? desktop ?? "-default"}{(alwaysUseHtmlSuffix ? ".html" : "")}"
				: category.OpenBy.Equals(OpenBy.SpecifiedURI)
					? category.SpecifiedURI ?? "~/"
					: $"~/{category.Desktop?.Alias ?? desktop ?? "-default"}/{category.Alias}{(alwaysUseHtmlSuffix ? ".html" : "")}";
			breadcrumbs.Add(new Tuple<string, string>(category.Title, url));

			var parentCategory = category.ParentCategory;
			while (parentCategory != null)
			{
				url = parentCategory.OpenBy.Equals(OpenBy.DesktopOnly)
				? $"~/{parentCategory.Desktop?.Alias ?? desktop ?? "-default"}{(alwaysUseHtmlSuffix ? ".html" : "")}"
				: parentCategory.OpenBy.Equals(OpenBy.SpecifiedURI)
					? parentCategory.SpecifiedURI ?? "~/"
					: $"~/{parentCategory.Desktop?.Alias ?? desktop ?? "-default"}/{parentCategory.Alias}{(alwaysUseHtmlSuffix ? ".html" : "")}";
				breadcrumbs.Insert(0, new Tuple<string, string>(parentCategory.Title, url));
				parentCategory = parentCategory.ParentCategory;
			}

			return breadcrumbs.Select(breadcrumb => new JObject
			{
				{ "Text", breadcrumb.Item1 },
				{ "URL", breadcrumb.Item2 }
			}).ToJArray();
		}

		internal static async Task<JObject> GenerateAsync(RequestInfo requestInfo, bool isSystemAdministrator = false, IRTUService rtuService = null, string validationKey = null, CancellationToken cancellationToken = default)
		{
			// prepare
			var requestJson = requestInfo.GetBodyJson();
			var contentTypeID = requestJson.Get<JObject>("ContentType")?.Get<string>("ID");
			var category = await (requestJson.Get<JObject>("ParentContentType")?.Get<string>("ID") ?? "").GetCategoryByAliasAsync(requestJson.Get<string>("ParentIdentity"), cancellationToken).ConfigureAwait(false);
			var desktop = requestJson.Get<JObject>("ContentType")?.Get<string>("Desktop") ?? requestJson.Get<JObject>("Module")?.Get<string>("Desktop") ?? requestJson.Get<JObject>("Organization")?.Get<string>("Desktop") ?? requestJson.Get<string>("Desktop");
			var pageNumber = requestJson.Get("PageNumber", 1);
			var alwaysUseHtmlSuffix = requestJson.Get<JObject>("Organization")?.Get<bool>("AlwaysUseHtmlSuffix") ?? true;
			var action = requestJson.Get<string>("Action");
			var isList = string.IsNullOrWhiteSpace(action) || "List".IsEquals(action);

			XDocument data;
			JArray breadcrumb;
			JObject pagination, seoInfo, filterBy = null, sortBy = null;
			string coverURI = null;

			// generate list
			if (isList)
			{
				// check permission
				var contentType = await (contentTypeID ?? "").GetContentTypeByIDAsync(cancellationToken).ConfigureAwait(false);
				var gotRights = isSystemAdministrator || requestInfo.Session.User.IsViewer(category?.WorkingPrivileges, contentType?.WorkingPrivileges);
				if (!gotRights)
				{
					var organization = contentType?.Organization ?? await (requestJson.Get<JObject>("Organization")?.Get<string>("ID") ?? "").GetOrganizationByIDAsync(cancellationToken).ConfigureAwait(false);
					gotRights = requestInfo.Session.User.ID.IsEquals(organization?.OwnerID);
				}
				if (!gotRights)
					throw new AccessDeniedException();

				// prepare filtering expression
				if (!(requestJson.Get<JObject>("FilterBy")?.ToFilter<Content>() is FilterBys<Content> filter) || filter.Children == null || filter.Children.Count < 1)
				{
					filter = Filters<Content>.And(
						Filters<Content>.Equals("SystemID", "@body[Organization.ID]"),
						Filters<Content>.Equals("RepositoryID", "@body[Module.ID]"),
						Filters<Content>.Equals("RepositoryEntityID", "@body[ContentType.ID]")
					);

					if (category != null)
						filter.Add(Filters<Content>.Equals("CategoryID", category.ID));

					filter.Add(
						Filters<Content>.LessThanOrEquals("StartDate", "@today"),
						Filters<Content>.Or(
							Filters<Content>.GreaterOrEquals("EndDate", "@today"),
							Filters<Content>.IsNull("EndDate")
						),
						Filters<Content>.Equals("Status", ApprovalStatus.Published.ToString())
					);
				}

				if (filter.GetChild("StartDate") == null)
					filter.Add(Filters<Content>.LessThanOrEquals("StartDate", "@today"));

				if (filter.GetChild("Status") == null)
					filter.Add(Filters<Content>.Equals("Status", ApprovalStatus.Published.ToString()));

				filterBy = new JObject
				{
					{ "API", filter.ToJson().ToString(Formatting.None) },
				};
				filter.Prepare(requestInfo, filterBys =>
				{
					var filters = (filterBys as FilterBys).Children.Where(filterby => filterby is FilterBy thefilterby && thefilterby.Value != null && thefilterby.Value is string).Select(filterby => filterby as FilterBy);
					filters.Where(thefilterby => thefilterby.Value is string value && (value.IsEquals("@[parentIdentity]") || value.IsEquals("@[parent-identity]"))).ForEach(filterby => filterby.Value = category?.ID);
				});
				filterBy["App"] = filter.ToClientJson().ToString(Formatting.None);

				// prepare sorting expression
				var sort = requestJson.Get<JObject>("SortBy")?.ToSort<Content>() ?? Sorts<Content>.Descending("StartDate").ThenByDescending("PublishedTime").ThenByDescending("Created");
				sortBy = new JObject
				{
					{ "API", sort.ToJson().ToString(Formatting.None) },
					{ "App", sort.ToClientJson().ToString(Formatting.None) }
				};

				// search the matched objects
				var pageSize = requestJson.Get("PageSize", 7);
				var results = await requestInfo.SearchAsync(filter, sort, pageSize, pageNumber, contentTypeID, -1, validationKey, cancellationToken, requestJson.Get<JObject>("Options")?.Get<bool>("ShowThumbnail") ?? true).ConfigureAwait(false);
				var totalRecords = results.Item1;
				var objects = results.Item2;
				var thumbnails = results.Item3;

				// prepare pagination
				var totalPages = new Tuple<long, int>(totalRecords, pageSize).GetTotalPages();
				if (totalPages > 0 && pageNumber > totalPages)
					pageNumber = totalPages;

				// generate xml
				data = XDocument.Parse("<Data/>");
				objects.ForEach(@object => data.Root.Add(@object.ToXml(false, xml =>
				{
					xml.Add(new XElement("Category", @object.Category?.Title));
					xml.Add(new XElement("URL", $"~/{@object.Category?.Desktop?.Alias ?? desktop ?? "-default"}/{@object.Category?.Alias ?? "-"}/{@object.Alias}{(alwaysUseHtmlSuffix ? ".html" : "")}"));
					xml.Add(new XElement("ThumbnailURL", thumbnails?.GetThumbnailURL(@object.ID, objects.Count == 1)));
				})));

				// build others
				breadcrumb = category?.GenerateBreadcrumb(requestJson, alwaysUseHtmlSuffix) ?? new JArray();
				pagination = Utility.GeneratePagination(totalRecords, totalPages, pageSize, pageNumber, $"~/{category?.Desktop?.Alias ?? desktop ?? "-default"}/{category?.Alias ?? "-"}" + "/{{pageNumber}}" + $"{(alwaysUseHtmlSuffix ? ".html" : "")}");
				seoInfo = new JObject
				{
					{ "Title", category?.Title },
					{ "Description", category?.Description }
				};
				thumbnails = category != null ? await requestInfo.GetThumbnailsAsync(category.ID, category.Title.Url64Encode(), cancellationToken, validationKey).ConfigureAwait(false) : null;
				coverURI = (thumbnails as JArray)?.First()?.Get<string>("URI");
			}

			// generate details
			else
			{
				// get the requested object
				var @object= await Content.GetContentByAliasAsync(contentTypeID, requestJson.Get<string>("ContentIdentity"), category?.ID, cancellationToken).ConfigureAwait(false);
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

				// get files
				var thumbnailsTask = requestInfo.GetThumbnailsAsync(@object.ID, @object.Title.Url64Encode(), cancellationToken, validationKey);
				var attachmentsTask = requestInfo.GetAttachmentsAsync(@object.ID, @object.Title.Url64Encode(), cancellationToken, validationKey);
				await Task.WhenAll(thumbnailsTask, attachmentsTask).ConfigureAwait(false);

				// generate XML
				data = XDocument.Parse("<Data/>");
				data.Root.Add(@object.ToXml(false, xml =>
				{
					xml.Add(new XElement("Category", @object.Category?.Title));
					xml.Add(new XElement("URL", $"~/{@object.Category?.Desktop?.Alias ?? desktop ?? "-default"}/{@object.Category?.Alias ?? "-"}/{@object.Alias}{(alwaysUseHtmlSuffix ? ".html" : "")}"));

					var thumbnails = new XElement("Thumbnails");
					(thumbnailsTask.Result as JArray).ForEach(thumbnail => thumbnails.Add(new XElement("Thumbnail", thumbnail.Get<string>("URI"))));
					xml.Add(thumbnails);

					var attachments = new XElement("Attachments");
					(attachmentsTask.Result as JArray).Select(attachment => new JObject
					{
						{ "Title", attachment["Title"] },
						{ "Filename", attachment["Filename"] },
						{ "Size", attachment["Size"] },
						{ "ContentType", attachment["ContentType"] },
						{ "Downloads", attachment["Downloads"] },
						{ "URIs", attachment["URIs"] }
					}).ForEach(attachment => attachments.Add(attachment.ToXml("Attachment")));
					xml.Add(attachments);
				}));

				// build others
				breadcrumb = @object.Category?.GenerateBreadcrumb(requestJson, alwaysUseHtmlSuffix) ?? new JArray();
				pagination = Utility.GeneratePagination(1, 1, 0, pageNumber, $"~/{@object.Category?.Desktop?.Alias ?? desktop ?? "-default"}/{@object.Category?.Alias}/{@object.Alias}" + "/{{pageNumber}}" + $"{(alwaysUseHtmlSuffix ? ".html" : "")}");
				coverURI = (thumbnailsTask.Result as JArray)?.First()?.Get<string>("URI");
				seoInfo = new JObject
				{
					{ "Title", @object.Title },
					{ "Description", @object.Summary },
					{ "Keywords", @object.Tags }
				};
			}

			// response
			return new JObject
			{
				{ "Data", data.ToString(SaveOptions.DisableFormatting) },
				{ "Breadcrumb", breadcrumb },
				{ "Pagination", pagination },
				{ "FilterBy", filterBy },
				{ "SortBy", sortBy },
				{ "SEOInfo", seoInfo },
				{ "CoverURI", coverURI }
			};
		}
	}
}
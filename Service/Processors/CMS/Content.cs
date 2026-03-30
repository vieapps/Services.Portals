#region Related components
using System;
using System.Linq;
using System.Xml.Linq;
using System.Dynamic;
using System.Diagnostics;
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
#endregion

namespace net.vieapps.Services.Portals
{
	public static class ContentProcessor
	{
		static List<string> ExcludedProperties { get; } = ["Privileges", "OriginalPrivileges", "Relateds", "ExternalRelateds", "OtherCategories", "InlineScripts", "SystemID", "RepositoryID", "RepositoryEntityID"];

		public static Content CreateContent(this ExpandoObject data, string excluded, out Dictionary<string, (string Identifier, string Filename)> inlineImages, Action<Content> onCompleted = null)
		{
			var content = Content.CreateInstance(data, excluded?.ToHashSet());
			content.Compute();
			content.NormalizeHTMLs(out inlineImages);
			content.Alias = (string.IsNullOrWhiteSpace(content.Alias) ? content.Title : content.Alias).NormalizeAlias();
			content.Tags = content.Tags?.Replace(";", ",").ToList(",", true).Where(tag => !string.IsNullOrWhiteSpace(tag)).Join(",");
			content.Tags = string.IsNullOrWhiteSpace(content.Tags) ? null : content.Tags;
			onCompleted?.Invoke(content);
			return content;
		}

		public static Content Update(this Content content, ExpandoObject data, string excluded, out Dictionary<string, (string Identifier, string Filename)> inlineImages, Action<Content> onCompleted = null)
		{
			content.Fill(data, excluded?.ToHashSet());
			content.Compute();
			content.NormalizeHTMLs(out inlineImages);
			content.Alias = (string.IsNullOrWhiteSpace(content.Alias) ? content.Title : content.Alias).NormalizeAlias();
			content.Tags = content.Tags?.Replace(";", ",").ToList(",", true).Where(tag => !string.IsNullOrWhiteSpace(tag)).Join(",");
			content.Tags = string.IsNullOrWhiteSpace(content.Tags) ? null : content.Tags;
			onCompleted?.Invoke(content);
			return content;
		}

		internal static string GetCacheKeyOfAliasedContent(this string contentTypeID, string categoryID, string alias)
			=> !string.IsNullOrWhiteSpace(contentTypeID) && !string.IsNullOrWhiteSpace(categoryID) && !string.IsNullOrWhiteSpace(alias)
				? $"e:{contentTypeID}#c:{categoryID}#a:{alias.NormalizeAlias().GenerateUUID()}".GetCacheKey<Content>()
				: null;

		internal static string GetCacheKeyOfAliasedContent(this ContentType contentType, Category category, string alias)
			=> contentType?.ID?.GetCacheKeyOfAliasedContent(category?.ID, alias);

		internal static string GetCacheKeyOfAliasedContent(this Content content)
			=> content?.ContentType?.GetCacheKeyOfAliasedContent(content?.Category, content?.Alias);

		public static FilterBys<Content> GetContentsFilter(string systemID, string repositoryID = null, string repositoryEntityID = null, string categoryID = null, Action<FilterBys<Content>> onCompleted = null)
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

		public static IFilterBy<Content> GetContentByAliasFilter(this ContentType contentType, Category category, string alias)
			=> Filters<Content>.And
			(
				Filters<Content>.Equals("RepositoryEntityID", contentType.ID),
				Filters<Content>.Equals("CategoryID", category.ID),
				Filters<Content>.Equals("Alias", alias.NormalizeAlias())
			);

		static async Task<Content> RefreshAsync(this Content content, bool force, CancellationToken cancellationToken, bool reloadWebpages = false, bool writeLogs = false, string correlationID = null, string message = null)
		{
			if (force)
			{
				await content.ContentType.ReUpdate().RefreshAsync(cancellationToken).ConfigureAwait(false);
				await content.Module.ReUpdate().RefreshAsync(cancellationToken, false).ConfigureAwait(false);
				await content.Organization.ReUpdate().RefreshAsync(cancellationToken, false).ConfigureAwait(false);
				await (content.Category != null ? content.Category.ReUpdate().RefreshAsync(cancellationToken, false) : Task.CompletedTask).ConfigureAwait(false);
				await Utility.Cache.RemoveAsync(content.ReUpdate(), cancellationToken).ConfigureAwait(false);
				content = await Content.GetAsync(content.ID, cancellationToken).ConfigureAwait(false);
			}
			if (reloadWebpages)
			{
				var urls = new[] { content.Status.Equals(ApprovalStatus.Published) ? content.GetURL() : null }.Concat([content.Organization.URL, content.Category?.GetURL(false)]).ToList();
				var categories = new[] { content.Category }.ToList();
				var parentCategory = content.Category?.ParentCategory;
				while (parentCategory != null)
				{
					urls = urls.Concat([parentCategory.GetURL()]).ToList();
					categories.Add(parentCategory);
					parentCategory = parentCategory.ParentCategory;
				}
				urls = urls.Concat(categories.Where(category => category != null).Select(category => category?.GetURL(true)))
					.Concat((content.OtherCategories ?? []).Select(id => id.GetCategoryByID()).Select(category => category?.GetURL(true)))
					.Where(url => url != null)
					.Distinct(StringComparer.OrdinalIgnoreCase)
					.ToList();
				await content.Organization.RefreshWebPagesAsync(urls, correlationID, (message ?? "Refresh a CMS content") + $" [{content.Title} - ID: {content.ID}]", force, writeLogs, cancellationToken).ConfigureAwait(false);
			}
			return content;
		}

		internal static async Task ClearRelatedCacheAsync(this Content content, CancellationToken cancellationToken = default, string correlationID = null, bool isWriteLogs = false, bool clearDataCache = true, bool clearHtmlCache = true, bool doRefresh = true)
		{
			// tasks for updating sets
			var setTasks = new List<Task>();

			// data cache keys
			var dataCacheKeys = clearDataCache && content != null
				? Extensions.GetRelatedCacheKeys(content.GetCacheKey()).Concat([content.GetCacheKeyOfAliasedContent()]).Where(key => key != null).ToList()
				: new List<string>();
			if (clearDataCache && content?.ContentType != null)
			{
				var cacheKeys = await Utility.Cache.GetSetMembersAsync(content.ContentType.GetSetCacheKey(), cancellationToken).ConfigureAwait(false);
				if (cacheKeys != null && cacheKeys.Any())
				{
					setTasks.Add(Utility.Cache.RemoveSetMembersAsync(content.ContentType.GetSetCacheKey(), cacheKeys, cancellationToken));
					dataCacheKeys = dataCacheKeys.Concat(cacheKeys).ToList();
				}
			}
			dataCacheKeys = dataCacheKeys.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

			// html cache keys (desktop HTMLs)
			var htmlCacheKeys = new List<string>();
			if (clearHtmlCache)
			{
				htmlCacheKeys = content?.Organization?.GetDesktopCacheKeys() ?? new List<string>();
				await new[] { content?.Desktop?.GetSetCacheKey() }
					.Concat(content?.ContentType != null ? await content.ContentType.GetSetCacheKeysAsync(cancellationToken).ConfigureAwait(false) : new List<string>())
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

			// remove related cache & refresh
			var writeLogs = isWriteLogs || Utility.IsCacheLogEnabled;
			await Task.WhenAll
			(
				Task.WhenAll(setTasks),
				Utility.Cache.RemoveAsync(htmlCacheKeys.Concat(dataCacheKeys).Distinct(StringComparer.OrdinalIgnoreCase).ToList(), cancellationToken),
				writeLogs && content != null
					? Utility.WriteLogAsync(correlationID, $"Clear related cache of a CMS content [{content.Title} - ID: {content.ID}]\r\n- {dataCacheKeys.Count} data keys => {dataCacheKeys.Join(", ")}\r\n- {htmlCacheKeys.Count} html keys => {htmlCacheKeys.Join(", ")}", "Caches")
					: Task.CompletedTask
			).ConfigureAwait(false);

			if (content != null)
				await Task.WhenAll
				(
					content.PurgeCloudFlareCacheAsync(correlationID, writeLogs, cancellationToken, doRefresh ? null : _ => content.GetURL().RefreshWebPageAsync(5, correlationID, writeLogs, Utility.CancellationToken).Execute()),
					doRefresh
						? content.RefreshAsync(false, cancellationToken, true, writeLogs, correlationID, "Refresh when related cache of a CMS content was clean")
						: Task.CompletedTask
				).ConfigureAwait(false);
		}

		internal static async Task<(List<Content> Objects, long TotalRecords, int PageNumber, JToken Thumbnails, List<string> CacheKeys)> SearchAsync(this RequestInfo requestInfo, string query, IFilterBy<Content> filter, SortBy<Content> sort, int pageSize, int pageNumber, string contentTypeID = null, long totalRecords = -1, CancellationToken cancellationToken = default, bool searchThumbnails = true, bool randomPage = false, int minRandomPage = 0, int maxRandomPage = 0, int cacheTime = 0)
		{
			// cache keys
			var cacheKeyOfObjects = string.IsNullOrWhiteSpace(query) ? Extensions.GetCacheKey(filter, sort, pageSize, pageNumber) : null;
			var cacheKeyOfTotalObjects = string.IsNullOrWhiteSpace(query) ? Extensions.GetCacheKeyOfTotalObjects(filter, sort) : null;
			var cacheKeys = string.IsNullOrWhiteSpace(query) ? [cacheKeyOfObjects, cacheKeyOfTotalObjects] : new List<string>();

			// count
			totalRecords = totalRecords > -1
				? totalRecords
				: string.IsNullOrWhiteSpace(query)
					? await Content.CountAsync(filter, contentTypeID, true, !Utility.IsCacheDisabled, cacheKeyOfTotalObjects, cacheTime, cancellationToken).ConfigureAwait(false)
					: await Content.CountAsync(query, filter, contentTypeID, cancellationToken).ConfigureAwait(false);

			// page number
			if (randomPage)
			{
				var totalPages = (totalRecords, pageSize).GetTotalPages();
				minRandomPage = minRandomPage > 0 && minRandomPage <= totalPages ? minRandomPage : 1;
				maxRandomPage = maxRandomPage > 0 && maxRandomPage <= totalPages ? maxRandomPage : totalPages;
				pageNumber = UtilityService.GetRandomNumber(minRandomPage, maxRandomPage);
			}

			// search objects
			var objects = totalRecords > 0
				? string.IsNullOrWhiteSpace(query)
					? await Content.FindAsync(filter, sort, pageSize, pageNumber, contentTypeID, true, !Utility.IsCacheDisabled, cacheKeyOfObjects, cacheTime, cancellationToken).ConfigureAwait(false)
					: await Content.SearchAsync(query, filter, null, pageSize, pageNumber, contentTypeID, cancellationToken).ConfigureAwait(false)
				: [];

			// search thumbnails
			objects = objects.Where(@object => @object != null && !string.IsNullOrWhiteSpace(@object.ID)).ToList();

			JToken thumbnails = null;
			if (objects.Count > 0 && searchThumbnails)
			{
				requestInfo.Header["x-thumbnails-as-attachments"] = "true";
				if (requestInfo.ContainsKey("x-logs"))
					requestInfo.Header["x-logs"] = "true";
				if (requestInfo.GetParameter("x-force-cache") != null)
					requestInfo.Header["x-force-cache"] = "true";
				thumbnails = objects.Count == 1
					? await requestInfo.GetThumbnailsAsync(objects[0].ID, objects[0].Title.Url64Encode(), Utility.ValidationKey, cancellationToken).ConfigureAwait(false)
					: await requestInfo.GetThumbnailsAsync(objects.Select(@object => @object.ID).Join(","), objects.ToJObject("ID", @object => new JValue(@object.Title.Url64Encode())).ToString(Formatting.None), Utility.ValidationKey, cancellationToken).ConfigureAwait(false);
			}

			// update cache
			if (string.IsNullOrWhiteSpace(query))
				cacheKeys.Add(await Utility.SetCacheOfPageSizeAsync(filter, sort, pageSize, cancellationToken).ConfigureAwait(false));

			var contentType = objects.FirstOrDefault()?.ContentType;
			await Task.WhenAll
			(
				contentType != null
					? Task.WhenAll
						(
							Utility.Cache.AddSetMembersAsync(contentType.ObjectCacheKeys, objects.Select(@object => @object.GetCacheKey()), cancellationToken),
							requestInfo.IsWriteCacheLogs()
								? requestInfo.WriteLogAsync($"Update Content-Type's set cache when search for contents => {contentType.GetSetCacheKey()} [{objects.Select(@object => @object.GetCacheKey()).Join(", ")}]", "Caches")
								: Task.CompletedTask
						)
					: Task.CompletedTask,
				requestInfo.IsWriteCacheLogs()
					? requestInfo.WriteLogAsync($"Search for CMS.Contents\r\n- Filter: {filter?.ToJson()}\r\n- Sort: {sort?.ToJson()}" + (string.IsNullOrWhiteSpace(query) ? $"\r\n- Cache keys: {cacheKeys.Join(", ")}" : ""), "Caches")
					: Task.CompletedTask
			).ConfigureAwait(false);

			// return the results
			return (objects, totalRecords, pageNumber, thumbnails, cacheKeys);
		}

		internal static async Task<JObject> SearchContentsAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, CancellationToken cancellationToken = default)
		{
			// prepare
			var request = requestInfo.GetRequestExpando();
			var verifyRequest = !isSystemAdministrator || !requestInfo.ContainsKey("x-dont-verify");

			var expression = await (requestInfo.GetParameter("x-expression") ?? requestInfo.GetParameter("x-expression-id") ?? requestInfo.GetParameter("expression-id") ?? requestInfo.GetParameter("ExpressionID") ?? requestInfo.GetParameter("object-extra-identity") ?? "").GetExpressionByIDAsync(cancellationToken).ConfigureAwait(false);

			ExpandoObject cursor = null;
			try
			{
				cursor = requestInfo.GetParameter("x-cursor")?.FromBase64Url().ToJson().ToExpandoObject();
			}
			catch { }
			var useCursor = cursor != null || (expression != null && expression.UseCursor) || requestInfo.ContainsKey("x-use-cursor");

			var query = (useCursor ? cursor?.Get<string>("FilterBy.Query") : null) ?? request.Get<string>("FilterBy.Query");

			var filter = expression?.GetFilterBy<Content>() as FilterBys<Content> ?? ((useCursor ? cursor?.Get<ExpandoObject>("FilterBy") : null) ?? request.Get<ExpandoObject>("FilterBy"))?.ToFilterBy<Content>() as FilterBys<Content> ?? Filters<Content>.And();
			var sort = string.IsNullOrWhiteSpace(query) ? expression?.GetSortBy<Content>() ?? ((useCursor ? cursor?.Get<ExpandoObject>("SortBy") : null) ?? request.Get<ExpandoObject>("SortBy"))?.ToSortBy<Content>() ?? Sorts<Content>.Descending("StartDate").ThenByDescending("PublishedTime") : null;

			var organizationID = expression?.SystemID ?? filter.GetValue("SystemID") ?? requestInfo.GetParameter("SystemID") ?? requestInfo.GetParameter("OrganizationID") ?? requestInfo.GetParameter("x-system-id");
			var organization = await (organizationID ?? "").GetOrganizationByIDAsync(cancellationToken).ConfigureAwait(false) ?? throw new InformationInvalidException("The organization is invalid");

			var moduleID = expression?.RepositoryID ?? filter.GetValue("RepositoryID") ?? requestInfo.GetParameter("RepositoryID") ?? requestInfo.GetParameter("ModuleID") ?? requestInfo.GetParameter("x-module-id");
			var module = await (moduleID ?? "").GetModuleByIDAsync(cancellationToken).ConfigureAwait(false);
			if (verifyRequest && ((module == null && string.IsNullOrWhiteSpace(query)) || (module != null && !organization.ID.IsEquals(module.SystemID))))
				throw new InformationInvalidException("The module is invalid");

			var contentTypeID = expression?.RepositoryEntityID ?? filter.GetValue("RepositoryEntityID") ?? requestInfo.GetParameter("RepositoryEntityID") ?? requestInfo.GetParameter("ContentTypeID") ?? requestInfo.GetParameter("x-content-type-id");
			var contentType = await (contentTypeID ?? "").GetContentTypeByIDAsync(cancellationToken).ConfigureAwait(false);
			if (verifyRequest && ((contentType == null && string.IsNullOrWhiteSpace(query) && expression?.ContentTypeDefinition == null) || (contentType != null && (!organization.ID.IsEquals(contentType.SystemID) || (module != null && !module.ID.IsEquals(contentType.RepositoryID))))))
				throw new InformationInvalidException("The content-type is invalid");

			var categoryID = filter?.GetValue("CategoryID") ?? requestInfo.GetParameter("CategoryID") ?? requestInfo.GetParameter("x-category-id");
			var category = await (categoryID ?? "").GetCategoryByIDAsync(cancellationToken).ConfigureAwait(false);

			// check permission
			var gotRights = isSystemAdministrator || requestInfo.Session.User.IsViewer(category?.WorkingPrivileges, contentType?.WorkingPrivileges, organization);
			if (!gotRights)
				throw new AccessDeniedException();

			// normalize filter
			if (filter == null || filter.Children == null || filter.Children.Count < 1)
				filter = ContentProcessor.GetContentsFilter(organization.ID, module?.ID, contentType?.ID, category?.ID);

			if (verifyRequest)
			{
				if (filter.GetChild("SystemID") is not FilterBy<Content> filterBySystem)
					filter.Add(Filters<Content>.Equals("SystemID", organization.ID));
				else if (filterBySystem.Value == null)
					filterBySystem.Value = organization.ID;
			}

			if (module != null)
			{
				if (filter.GetChild("RepositoryID") is not FilterBy<Content> filterByRepository)
					filter.Add(Filters<Content>.Equals("RepositoryID", module.ID));
				else if (filterByRepository.Value == null)
					filterByRepository.Value = module.ID;
			}

			if (contentType != null)
			{
				if (filter.GetChild("RepositoryEntityID") is not FilterBy<Content> filterByRepositoryEntity)
					filter.Add(Filters<Content>.Equals("RepositoryEntityID", contentType.ID));
				else if (filterByRepositoryEntity.Value == null)
					filterByRepositoryEntity.Value = contentType.ID;
			}

			if (category != null)
			{
				if (filter.GetChild("CategoryID") is not FilterBy<Content> filterByCategory)
					filter.Add(Filters<Content>.Equals("CategoryID", category.ID));
				else if (filterByCategory.Value == null)
					filterByCategory.Value = category.ID;
			}

			if (!requestInfo.Session.User.IsAuthenticated)
			{
				if (filter.GetChild("Status") is not FilterBy<Content> filterByStatus)
					filter.Add(Filters<Content>.Equals("Status", ApprovalStatus.Published.ToString()));
				else
					filterByStatus.Value = ApprovalStatus.Published.ToString();
			}

			filter.Prepare(requestInfo);

			// other parameters
			var showAttachments = "true".IsEquals(requestInfo.GetParameter("x-object-attachments")) || requestInfo.ContainsKey("ShowAttachments");
			var showURLs = "true".IsEquals(requestInfo.GetParameter("x-object-urls")) || requestInfo.ContainsKey("ShowURLs");
			var showCategories = "true".IsEquals(requestInfo.GetParameter("x-object-categories")) || requestInfo.ContainsKey("ShowCategories");
			var showDetails = "false".IsEquals(requestInfo.GetParameter("x-object-details")) || requestInfo.ContainsKey("NoDetails") ? false : true;

			// process cache
			var isWriteCacheLogs = requestInfo.IsWriteCacheLogs();
			var (totalOfRecords, totalPages, pageSize, pageNumber) = ((useCursor ? cursor?.Get<ExpandoObject>("Pagination") : null) ?? request.Get<ExpandoObject>("Pagination"))?.GetPagination() ?? (-1, 0, 20, useCursor ? 0 : 1);
			pageNumber += useCursor ? 1 : 0;

			var cacheKeyOfObjects = string.IsNullOrWhiteSpace(query) ? Extensions.GetCacheKey(filter, sort, pageSize, pageNumber) : null;
			var cacheKeyOfTotalObjects = string.IsNullOrWhiteSpace(query) ? Extensions.GetCacheKeyOfTotalObjects(filter, sort) : null;
			var cacheKeySuffix = string.IsNullOrWhiteSpace(query) ? (showAttachments ? ":a" : "") + (showURLs ? ":u" : "") + (showCategories ? ":c" : "") + (showDetails ? "" : ":d") : null;
			var cacheKeyOfObjectsJson = string.IsNullOrWhiteSpace(query) ? Extensions.GetCacheKeyOfObjectsJson(filter, sort, pageSize, pageNumber, string.IsNullOrWhiteSpace(cacheKeySuffix) ? null : cacheKeySuffix) : null;

			if (requestInfo.IsForceCache())
				await Utility.Cache.RemoveAsync([cacheKeyOfObjectsJson, cacheKeyOfObjects, cacheKeyOfTotalObjects], cancellationToken).ConfigureAwait(false);

			else if (cacheKeyOfObjectsJson != null)
			{
				var json = Utility.IsCacheDisabled ? null : await Utility.Cache.GetAsync<string>(cacheKeyOfObjectsJson, cancellationToken).ConfigureAwait(false);
				if (!string.IsNullOrWhiteSpace(json))
				{
					if (isWriteCacheLogs)
						await requestInfo.WriteLogAsync($"Got JSON of CMS.Contents\r\n{cacheKeyOfObjectsJson}", "Caches").ConfigureAwait(false);
					return useCursor ? JObject.Parse(json).ToCursor() : JObject.Parse(json);
				}
			}

			// search if has no cache
			var (objects, totalRecords, _, thumbnails, cacheKeys) = await requestInfo.SearchAsync(query, filter, sort, pageSize, pageNumber, contentType?.ID, totalOfRecords, cancellationToken).ConfigureAwait(false);
			JToken attachments = null;
			if (objects.Count > 0 && showAttachments)
				attachments = objects.Count == 1
					? await requestInfo.GetAttachmentsAsync(objects[0].ID, objects[0].Title.Url64Encode(), Utility.ValidationKey, cancellationToken).ConfigureAwait(false)
					: await requestInfo.GetAttachmentsAsync(objects.Select(@object => @object.ID).Join(","), objects.ToJObject("ID", @object => new JValue(@object.Title.Url64Encode())).ToString(Formatting.None), Utility.ValidationKey, cancellationToken).ConfigureAwait(false);

			// build response
			totalPages = (totalRecords, pageSize).GetTotalPages();
			if (totalPages > 0 && pageNumber > totalPages)
				pageNumber = totalPages;

			var siteURL = organization.DefaultSite?.GetURL(requestInfo.GetHeaderParameter("x-srp-host"), requestInfo.GetParameter("x-url")) + "/";
			var objectName = objects.Count > 0 ? objects.First().GetObjectName().ToLower() : null;

			var response = new JObject
			{
				{ "FilterBy", filter.ToClientJson(query) },
				{ "SortBy", sort?.ToClientJson() },
				{ "Pagination", (totalRecords, totalPages, pageSize, pageNumber).GetPagination() },
				{ "Objects", objects.Select(@object => !string.IsNullOrWhiteSpace(expression?.SearchTransformScript)
					? expression.SearchTransformScript.JsEvaluate(@object, requestInfo, new JObject
					{
						["URI"] = $"{Utility.ServiceName.ToLower()}://{objectName}/{@object.ID}",
						["URL"] = organization.NormalizeURLs(@object.GetURL(), true, siteURL),
						["Summary"] = @object.Summary?.NormalizeHTMLBreaks(),
						["Details"] = organization.NormalizeURLs(@object.Details),
						["Thumbnails"] = thumbnails?.GetThumbnails(@object.ID)?.NormalizeURIs(organization.FakeFilesHttpURI),
						["Attachments"] = (attachments == null ? null : objects.Count == 1 ? attachments : attachments[@object.ID])?.NormalizeURIs(organization.FakeFilesHttpURI),
						["Category"] = @object.Category?.ToJson(json => json["URL"] = organization.NormalizeURLs(@object.Category.GetURL(), true, siteURL))
					}.ToExpandoObject())?.ToString().ToJson()
					: @object.ToJson(json =>
					{
						json["URI"] = $"{Utility.ServiceName.ToLower()}://{objectName}/{@object.ID}";
						json["Summary"] = @object.Summary?.NormalizeHTMLBreaks();
						if (showDetails)
							json["Details"] = organization.NormalizeURLs(@object.Details);
						else
							json.Remove("Details");
						if (showURLs)
							json["URL"] = organization.NormalizeURLs(@object.GetURL(), true, siteURL);
						json["Thumbnails"] = thumbnails?.GetThumbnails(@object.ID)?.NormalizeURIs(organization.FakeFilesHttpURI);
						if (showAttachments)
							json["Attachments"] = (attachments == null ? null : objects.Count == 1 ? attachments : attachments[@object.ID])?.NormalizeURIs(organization.FakeFilesHttpURI);
						if (showCategories)
							json["Category"] = new JObject
							{
								["Title"] = @object.Category.Title,
								["FullTitle"] = @object.Category.FullTitle,
								["URL"] = organization.NormalizeURLs(@object.Category.GetURL(), true, siteURL)
							};
						if (showURLs || showCategories || !showDetails)
							json.Remove(ExcludedProperties);
					})).ToJArray()
				}
			};

			// update cache & response
			if (string.IsNullOrWhiteSpace(query))
			{
				cacheKeys = cacheKeys.Concat([cacheKeyOfObjectsJson]).ToList();
				await Task.WhenAll
				(
					Utility.Cache.SetAsync(cacheKeyOfObjectsJson, response.ToString(Formatting.None), cancellationToken),
					contentType != null ? Utility.Cache.AddSetMembersAsync(contentType.GetSetCacheKey(), cacheKeys, cancellationToken) : Task.CompletedTask,
					category != null ? Utility.Cache.AddSetMembersAsync(category.GetSetCacheKey(), cacheKeys, cancellationToken) : Task.CompletedTask,
					isWriteCacheLogs ? requestInfo.WriteLogAsync($"Update cache when search CMS contents\r\n- Cache key of JSON: {cacheKeyOfObjectsJson}\r\n{(contentType != null ? $"- Cache key of Content-Type's set: {contentType.GetSetCacheKey()}\r\n" : "")}- Related cache keys: {cacheKeys.Join(", ")}", "Caches") : Task.CompletedTask
				).ConfigureAwait(false);
			}

			if (contentType != null)
				Utility.Cache.AddSetMembersAsync(contentType.GetSetCacheKey(), objects.Select(@object => @object.GetCacheKeyOfAliasedContent()), Utility.CancellationToken).Execute();

			return useCursor ? response.ToCursor() : response;
		}

		internal static async Task<JObject> CreateContentAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, CancellationToken cancellationToken = default)
		{
			// prepare
			var request = requestInfo.GetBodyExpando();

			var organizationID = request.Get<string>("SystemID") ?? requestInfo.GetParameter("SystemID") ?? requestInfo.GetParameter("x-system-id");
			var organization = await (organizationID ?? "").GetOrganizationByIDAsync(cancellationToken).ConfigureAwait(false) ?? throw new InformationInvalidException("The organization is invalid");

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
			var gotRights = isSystemAdministrator || requestInfo.Session.User.IsContributor(category.WorkingPrivileges, contentType.WorkingPrivileges, organization);
			if (!gotRights)
				throw new AccessDeniedException();

			// source to copy from
			var source = await Content.GetAsync(request.Get<string>("CopyFromID"), !Utility.IsCacheDisabled, cancellationToken).ConfigureAwait(false);

			// get data
			Dictionary<string, (string Identifier, string Filename)> inlineImages = null;
			var content = source != null
				? source.Copy("ID,CategoryID,OtherCategories,Alias,Relateds,Privileges,StartDate,EndDate,Created,CreatedID,LastModified,LastModifiedID".ToHashSet(), obj =>
				{
					obj.Alias = source.Title.NormalizeAlias();
					obj.CategoryID = category.ID;
					obj.OtherCategories = request.Get<List<string>>("OtherCategories");
				})
				: request.CreateContent("Privileges,StartDate,EndDate,Created,CreatedID,LastModified,LastModifiedID", out inlineImages);
			content.SystemID = organization.ID;
			content.RepositoryID = module.ID;
			content.RepositoryEntityID = contentType.ID;
			content.Created = content.LastModified = DateTime.Now;
			content.CreatedID = content.LastModifiedID = requestInfo.Session.User.ID;
			content.ID = source != null
				? organization.ID.IsEquals(source.SystemID) ? UtilityService.NewUUID : $"{organization.ID}:{source.ID}".GenerateUUID()
				: string.IsNullOrWhiteSpace(content.ID) || !content.ID.IsValidUUID() ? UtilityService.NewUUID : content.ID;

			var existing = await Content.GetContentByAliasAsync(contentType, content.Alias, category.ID, cancellationToken).ConfigureAwait(false);
			if (existing != null)
				content.Alias = $"{content.Alias}-{DateTime.Now.ToUnixTimestamp()}-{UtilityService.GetRandomNumber()}";

			var dateString = request.Get<string>("StartDate");
			content.StartDate = !string.IsNullOrWhiteSpace(dateString) && DateTime.TryParse(dateString, out var date)
				? date.ToDTString(false, false)
				: DateTime.Now.ToDTString(false, false);

			dateString = request.Get<string>("EndDate");
			content.EndDate = !string.IsNullOrWhiteSpace(dateString) && DateTime.TryParse(dateString, out date)
				? date.ToDTString(false, false)
				: null;

			content.PublishedTime = null;
			dateString = request.Get<string>("PublishedTime");
			if (!string.IsNullOrWhiteSpace(dateString) && DateTime.TryParse(dateString, out date))
				content.PublishedTime = date;
			if (content.PublishedTime == null && content.Status.Equals(ApprovalStatus.Published))
				content.PublishedTime = DateTime.Now;

			content.OtherCategories = content.OtherCategories?.Where(id => !content.CategoryID.IsEquals(id)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
			content.OtherCategories = content.OtherCategories != null && content.OtherCategories.Count > 0 ? content.OtherCategories : null;

			if (inlineImages == null || inlineImages.Count < 1)
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
			Utility.Cache.SetAsync(content.GetCacheKeyOfAliasedContent(), content.ID, Utility.CancellationToken).Execute();

			// upload inline images
			if (inlineImages != null && inlineImages.Count > 0)
			{
				await requestInfo.UploadInlineImagesAsync(inlineImages, content, cancellationToken).ConfigureAwait(false);
				content.Details = organization.NormalizeURLs(content.Details, false);
				await Content.UpdateAsync(content, true, !Utility.IsCacheDisabled, cancellationToken).ConfigureAwait(false);
			}

			// send update message
			var thumbnailsTask = requestInfo.GetThumbnailsAsync(content.ID, content.Title.Url64Encode(), Utility.ValidationKey, cancellationToken);
			var attachmentsTask = requestInfo.GetAttachmentsAsync(content.ID, content.Title.Url64Encode(), Utility.ValidationKey, cancellationToken);
			await Task.WhenAll(thumbnailsTask, attachmentsTask).ConfigureAwait(false);

			var response = content.ToJson(json =>
			{
				json["Thumbnails"] = thumbnailsTask.Result;
				json["Attachments"] = attachmentsTask.Result;
				json["Details"] = organization.NormalizeURLs(content.Details);
				json.UpdateVersions(new List<VersionContent>());
			});
			new UpdateMessage
			{
				Type = $"{requestInfo.ServiceName}#{content.GetObjectName()}#Create",
				DeviceID = "*",
				Data = response
			}.Send();

			// update related cache & send notification
			Task.WhenAll
			(
				content.SendNotificationAsync("Create", content.Category.Notifications, ApprovalStatus.Draft, content.Status, requestInfo, Utility.CancellationToken),
				content.ClearRelatedCacheAsync(Utility.CancellationToken, requestInfo.CorrelationID),
				Utility.Cache.AddSetMemberAsync(content.ContentType.ObjectCacheKeys, content.GetCacheKey(), Utility.CancellationToken)
			).Execute();
			return response;
		}

		internal static async Task<JObject> GetContentAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, CancellationToken cancellationToken = default)
		{
			// prepare
			var identity = requestInfo.GetObjectIdentity(true, true) ?? "";
			var content = identity.IsValidUUID()
				? await Content.GetAsync(identity, !Utility.IsCacheDisabled, cancellationToken).ConfigureAwait(false)
				: await Content.GetContentByAliasAsync(requestInfo.GetParameter("RepositoryEntityID") ?? requestInfo.GetParameter("x-content-type-id"), identity, requestInfo.GetParameter("Category") ?? requestInfo.GetParameter("x-category-id"), cancellationToken).ConfigureAwait(false);

			if (content == null)
				throw new InformationNotFoundException();
			else if (content.Organization == null || content.Module == null || content.ContentType == null)
				throw new InformationInvalidException("The organization/module/content-type is invalid");

			// check permission
			var privileges = content.WorkingPrivileges;
			var parentPrivileges = content.ContentType?.WorkingPrivileges;

			var gotRights = isSystemAdministrator || requestInfo.Session.User.ID.IsEquals(content.Organization.OwnerID);
			if (!gotRights)
				gotRights = content.Status.Equals(ApprovalStatus.Published)
					? requestInfo.Session.User.IsViewer(privileges, parentPrivileges, content.Organization)
					: requestInfo.Session.User.ID.IsEquals(content.CreatedID) || requestInfo.Session.User.IsEditor(privileges, parentPrivileges, content.Organization);

			if (!gotRights)
				throw new AccessDeniedException();

			if (!identity.IsValidUUID())
				return new JObject
				{
					{ "ID", content.ID },
					{ "Title", content.Title },
					{ "Alias", content.Alias }
				};

			// refresh (reload and force cache of HTMLs)
			var isRefresh = "refresh".IsEquals(requestInfo.GetObjectIdentity()) && requestInfo.Session.User.IsAuthenticated;
			if (isRefresh)
			{
				new CommunicateMessage("Files")
				{
					Type = "ClearCache",
					Data = new JObject
					{
						{ "ObjectID", content.ID },
						{ "CorrelationID", requestInfo.CorrelationID }
					}
				}.Send();
				await content.RefreshAsync(true, cancellationToken).ConfigureAwait(false);

				privileges = content.WorkingPrivileges;
				parentPrivileges = content.ContentType?.WorkingPrivileges;
				var isAdministrator = requestInfo.Session.User.IsAdministrator(privileges, parentPrivileges, content.Organization, false);
				var isModerator = requestInfo.Session.User.IsModerator(privileges, parentPrivileges, content.Organization, false);
				var isEditor = requestInfo.Session.User.IsEditor(privileges, parentPrivileges, content.Organization, false);
				var isContributor = requestInfo.Session.User.IsContributor(privileges, parentPrivileges, content.Organization, false);
				var isViewer = requestInfo.Session.User.IsViewer(privileges, parentPrivileges, content.Organization, false);
				await requestInfo.WriteLogAsync($"Refresh an individual CMS content [{content.Title}]" + "\r\n\r\n"
					+ $"- Working privileges: System Administrator => {isSystemAdministrator} - Administrator => {isAdministrator} - Moderator => {isModerator} - Editor => {isEditor} - Contributor => {isContributor} - Viewer => {isViewer}" + "\r\n\r\n"
					+ $"- Content: {content.ToJson()}" + "\r\n\r\n"
					+ $"- Category: {content.Category?.ToJson(false, false)}" + "\r\n\r\n"
					+ $"- Content-Type: {content.ContentType?.ToJson()}" + "\r\n\r\n"
					+ $"- Module: {content.Module?.ToJson(false, false)}" + "\r\n\r\n"
					+ $"- Organization: {content.Organization?.ToJson(false, false)}"
				, "Caches").ConfigureAwait(false);
			}

			// store object cache key to clear related cached
			Utility.Cache.AddSetMemberAsync(content.ContentType.ObjectCacheKeys, content.GetCacheKey(), Utility.CancellationToken).Execute();

			// send update message and return
			var thumbnailsTask = requestInfo.GetThumbnailsAsync(content.ID, content.Title.Url64Encode(), Utility.ValidationKey, cancellationToken);
			var attachmentsTask = requestInfo.GetAttachmentsAsync(content.ID, content.Title.Url64Encode(), Utility.ValidationKey, cancellationToken);
			await Task.WhenAll(thumbnailsTask, attachmentsTask).ConfigureAwait(false);

			var expression = await (requestInfo.GetParameter("object-extra-identity") ?? "").GetExpressionByIDAsync(cancellationToken).ConfigureAwait(false);
			var response = !string.IsNullOrWhiteSpace(expression?.GetTransformScript)
				? expression.GetTransformScript.JsEvaluate(content, requestInfo, new JObject
				{
					["URI"] = $"apis://{Utility.ServiceName.ToLower()}/{content.GetObjectName().ToLower()}/{content.ID}/{(expression != null ? $"/{expression.ID}" : "")}",
					["URL"] = content.GetURL(),
					["Summary"] = content.Summary?.NormalizeHTMLBreaks(),
					["Details"] = content.Organization.NormalizeURLs(content.Details),
					["Thumbnails"] = thumbnailsTask.Result,
					["Attachments"] = attachmentsTask.Result,
					["Category"] = new JObject
					{
						["Title"] = content.Category?.Title,
						["FullTitle"] = content.Category?.FullTitle,
						["URL"] = content.Category?.GetURL()
					}
				}.ToExpandoObject()).ToString().ToJson() as JObject
				: content.ToJson(json =>
				{
					json["URI"] = $"apis://{Utility.ServiceName.ToLower()}/{content.GetObjectName().ToLower()}/{content.ID}/{(expression != null ? $"/{expression.ID}" : "")}";
					json["URL"] = content.GetURL();
					json["Summary"] = content.Summary?.NormalizeHTMLBreaks();
					json["Details"] = content.Organization.NormalizeURLs(content.Details);
					json["Thumbnails"] = thumbnailsTask.Result;
					json["Attachments"] = attachmentsTask.Result;
					if ("true".IsEquals(requestInfo.GetParameter("x-object-categories")) || requestInfo.ContainsKey("ShowCategories"))
						json["Category"] = new JObject
						{
							["Title"] = content.Category?.Title,
							["FullTitle"] = content.Category?.FullTitle,
							["URL"] = content.Category?.GetURL()
						};
				}).UpdateVersions(await content.FindVersionsAsync(!Utility.IsCacheDisabled, cancellationToken, false).ConfigureAwait(false));

			if (expression == null)
				new UpdateMessage
				{
					Type = $"{requestInfo.ServiceName}#{content.GetObjectName()}#Update",
					Data = response,
					DeviceID = "*",
					ExcludedDeviceID = isRefresh ? "" : requestInfo.Session.DeviceID
				}.Send();

			return response;
		}

		internal static async Task<JObject> UpdateAsync(this Content content, RequestInfo requestInfo, ApprovalStatus oldStatus, CancellationToken cancellationToken, string @event = null)
		{
			if (content.Status.Equals(ApprovalStatus.Published) && content.PublishedTime == null)
				content.PublishedTime = DateTime.Now;

			// update
			await Content.UpdateAsync(content, requestInfo.Session.User.ID, !Utility.IsCacheDisabled, cancellationToken).ConfigureAwait(false);

			// send update message
			var thumbnailsTask = requestInfo.GetThumbnailsAsync(content.ID, content.Title.Url64Encode(), Utility.ValidationKey, cancellationToken);
			var attachmentsTask = requestInfo.GetAttachmentsAsync(content.ID, content.Title.Url64Encode(), Utility.ValidationKey, cancellationToken);
			var versionsTask = content.FindVersionsAsync(!Utility.IsCacheDisabled, false, cancellationToken);
			await Task.WhenAll(thumbnailsTask, attachmentsTask, versionsTask).ConfigureAwait(false);

			var response = content.ToJson(json =>
			{
				json.UpdateVersions(versionsTask.Result);
				json["Thumbnails"] = thumbnailsTask.Result;
				json["Attachments"] = attachmentsTask.Result;
				json["Details"] = content.Organization.NormalizeURLs(content.Details);
			});
			new UpdateMessage
			{
				Type = $"{requestInfo.ServiceName}#{content.GetObjectName()}#Update",
				DeviceID = "*",
				Data = response
			}.Send();

			// update related cache & send notification
			Task.WhenAll
			(
				content.SendNotificationAsync(@event ?? "Update", content.Category.Notifications, oldStatus, content.Status, requestInfo, Utility.CancellationToken),
				content.ClearRelatedCacheAsync(Utility.CancellationToken, requestInfo.CorrelationID, requestInfo.IsWriteCacheLogs()),
				Utility.Cache.SetAsync(content.GetCacheKeyOfAliasedContent(), content.ID, Utility.CancellationToken),
				Utility.Cache.AddSetMemberAsync(content.ContentType.ObjectCacheKeys, content.GetCacheKey(), Utility.CancellationToken),
				Utility.Cache.AddSetMembersAsync(content.ContentType.GetSetCacheKey(), [content.GetCacheKey(), content.GetCacheKeyOfAliasedContent()], Utility.CancellationToken)
			).Execute();
			return response;
		}

		internal static async Task<JObject> UpdateContentAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, CancellationToken cancellationToken = default)
		{
			// prepare
			var content = await Content.GetAsync(requestInfo.GetObjectIdentity() ?? "", !Utility.IsCacheDisabled, cancellationToken).ConfigureAwait(false) ?? throw new InformationNotFoundException();
			if (content.Organization == null || content.Module == null || content.ContentType == null)
				throw new InformationInvalidException("The organization/module/content-type is invalid");

			// check permission
			var isAdministrator = isSystemAdministrator || requestInfo.Session.User.IsAdministrator(content.Organization.WorkingPrivileges);
			var gotRights = isAdministrator || requestInfo.Session.User.IsEditor(content.WorkingPrivileges, content.ContentType.WorkingPrivileges, content.Organization);
			if (!gotRights)
				gotRights = content.Status.Equals(ApprovalStatus.Draft) || content.Status.Equals(ApprovalStatus.Pending) || content.Status.Equals(ApprovalStatus.Rejected)
					? requestInfo.Session.User.ID.IsEquals(content.CreatedID)
					: requestInfo.Session.User.IsEditor(content.WorkingPrivileges, content.ContentType.WorkingPrivileges, content.Organization);
			if (!gotRights)
				throw new AccessDeniedException();

			// prepare data
			var request = requestInfo.GetBodyExpando();
			var oldCategoryID = content.CategoryID;
			var oldOtherCategories = content.OtherCategories?.Select(id => id).ToList();
			var oldAlias = content.Alias;
			var oldStatus = content.Status;

			Dictionary<string, (string Identifier, string Filename)> inlineImages = null;
			content.Update(request, "ID,SystemID,RepositoryID,RepositoryEntityID,StartDate,EndDate,PublishedTime,Privileges,Created,CreatedID,LastModified,LastModifiedID", out inlineImages, _ =>
			{
				var lastModified = DateTime.Now;
				var lastModifiedID = requestInfo.Session.User.ID;
				if (isAdministrator && requestInfo.ContainsKey("x-advanced-update"))
					try
					{
						lastModified = request.Get("LastModified", DateTime.Now);
						lastModifiedID = request.Get("LastModifiedID", requestInfo.Session.User.ID);
					}
					catch { }
				content.LastModified = lastModified;
				content.LastModifiedID = lastModifiedID;
			});

			var existing = await Content.GetContentByAliasAsync(content.RepositoryEntityID, content.Alias, content.CategoryID, cancellationToken).ConfigureAwait(false);
			if (existing != null && !existing.ID.IsEquals(content.ID))
				content.Alias = $"{content.Alias}-{DateTime.Now.ToUnixTimestamp()}-{UtilityService.GetRandomNumber()}";

			var dateString = request.Get<string>("StartDate");
			content.StartDate = !string.IsNullOrWhiteSpace(dateString) && DateTime.TryParse(dateString, out var date)
				? date.ToDTString(false, false)
				: DateTime.Now.ToDTString(false, false);

			dateString = request.Get<string>("EndDate");
			content.EndDate = !string.IsNullOrWhiteSpace(dateString) && DateTime.TryParse(dateString, out date)
				? date.ToDTString(false, false)
				: null;

			dateString = request.Get<string>("PublishedTime");
			if (!string.IsNullOrWhiteSpace(dateString) && DateTime.TryParse(dateString, out date))
				content.PublishedTime = date;

			content.OtherCategories = content.OtherCategories?.Where(id => !content.CategoryID.IsEquals(id)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
			content.OtherCategories = content.OtherCategories != null && content.OtherCategories.Count > 0 ? content.OtherCategories : null;

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

			if (inlineImages != null && inlineImages.Count > 0)
				await requestInfo.UploadInlineImagesAsync(inlineImages, content, cancellationToken).ConfigureAwait(false);
			content.Details = content.Organization.NormalizeURLs(content.Details, false);

			// update
			return await content.UpdateAsync(requestInfo, oldStatus, cancellationToken).ConfigureAwait(false);
		}

		internal static async Task<JObject> DeleteContentAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, CancellationToken cancellationToken = default)
		{
			// prepare
			var identity = requestInfo.GetObjectIdentity() ?? "";
			var content = await Content.GetAsync(identity, !Utility.IsCacheDisabled, cancellationToken).ConfigureAwait(false);
			if (content == null)
				throw new InformationNotFoundException($"The content was not found [{identity}]");
			if (content.Organization == null || content.Module == null || content.ContentType == null)
				throw new InformationInvalidException("The organization/module/content-type is invalid");

			// check permission
			var gotRights = isSystemAdministrator || requestInfo.Session.User.IsModerator(content.WorkingPrivileges, content.ContentType.WorkingPrivileges, content.Organization);
			if (!gotRights)
				gotRights = content.Status.Equals(ApprovalStatus.Draft) || content.Status.Equals(ApprovalStatus.Pending) || content.Status.Equals(ApprovalStatus.Rejected)
					? requestInfo.Session.User.ID.IsEquals(content.CreatedID) || requestInfo.Session.User.IsEditor(content.WorkingPrivileges, content.ContentType.WorkingPrivileges, content.Organization)
					: requestInfo.Session.User.IsModerator(content.WorkingPrivileges, content.ContentType.WorkingPrivileges, content.Organization);
			if (!gotRights)
				throw new AccessDeniedException();

			// delete
			return await content.DeleteAsync(requestInfo, true, true, cancellationToken).ConfigureAwait(false);
		}

		internal static async Task<JObject> DeleteAsync(this Content content, RequestInfo requestInfo, bool updateCache, bool sendUpdatingMessages, CancellationToken cancellationToken)
		{
			await requestInfo.DeleteFilesAsync(content.SystemID, content.RepositoryEntityID, content.ID, Utility.ValidationKey, cancellationToken).ConfigureAwait(false);
			await Content.DeleteAsync(content.ID, requestInfo.Session.User.ID, cancellationToken).ConfigureAwait(false);

			if (updateCache)
				Task.WhenAll
				(
					Utility.Cache.RemoveSetMemberAsync(content.ContentType.ObjectCacheKeys, content.GetCacheKey(), Utility.CancellationToken),
					content.ClearRelatedCacheAsync(Utility.CancellationToken, requestInfo.CorrelationID, requestInfo.IsWriteCacheLogs(), true, true, false)
				).Execute();

			var json = sendUpdatingMessages ? content.ToJson(json => json.Remove("Details")) : null;
			if (sendUpdatingMessages)
				new UpdateMessage
				{
					Type = $"{requestInfo.ServiceName}#{content.GetObjectName()}#Delete",
					DeviceID = "*",
					Data = json
				}.Send();

			await content.SendNotificationAsync("Delete", content.Category?.Notifications ?? content.ContentType?.Notifications, content.Status, content.Status, requestInfo, cancellationToken).ConfigureAwait(false);
			return json;
		}

		internal static async Task<JObject> GenerateAsync(RequestInfo requestInfo, bool isSystemAdministrator = false, CancellationToken cancellationToken = default)
		{
			// prepare
			var requestJson = requestInfo.BodyAsJson;
			var optionsJson = requestJson.Get("Options", new JObject());
			var options = optionsJson.ToExpandoObject();

			var organizationJson = requestJson.Get("Organization", new JObject());
			var moduleJson = requestJson.Get("Module", new JObject());
			var contentTypeJson = requestJson.Get("ContentType", new JObject());
			var parentContentTypeJson = requestJson.Get("ParentContentType", new JObject());
			var expressionJson = requestJson.Get("Expression", new JObject());
			var desktopsJson = requestJson.Get("Desktops", new JObject());

			var contentTypeID = contentTypeJson.Get<string>("ID");
			var parentIdentity = requestJson.Get<string>("ParentIdentity");
			parentIdentity = string.IsNullOrWhiteSpace(parentIdentity) ? null : parentIdentity.Trim();

			var paginationJson = requestJson.Get("Pagination", new JObject());
			var pageSize = paginationJson.Get("PageSize", 7);
			var pageNumber = paginationJson.Get("PageNumber", 1);
			var showPageLinks = paginationJson.Get("ShowPageLinks", true);
			var numberOfPageLinks = paginationJson.Get("NumberOfPageLinks", 7);

			var randomPage = false;
			var minRandomPage = 0;
			var maxRandomPage = 0;
			if (requestJson.Get("IsAutoPageNumber", false))
			{
				randomPage = options.Get("RandomPage", false);
				minRandomPage = options.Get("MinRandomPage", 0);
				maxRandomPage = options.Get("MaxRandomPage", 0);
			}

			var cultureInfo = CultureInfo.GetCultureInfo(requestJson.Get("Language", "vi-VN"));
			var customDateTimeFormat = options.Get<string>("CustomDateTimeFormat");
			var action = requestJson.Get<string>("Action");
			var isList = string.IsNullOrWhiteSpace(action) || "List".IsEquals(action);

			var desktop = desktopsJson.Get<string>("Specified");
			desktop = !string.IsNullOrWhiteSpace(desktop) ? desktop : desktopsJson.Get<string>("ContentType");
			desktop = !string.IsNullOrWhiteSpace(desktop) ? desktop : desktopsJson.Get<string>("Module");
			desktop = !string.IsNullOrWhiteSpace(desktop) ? desktop : desktopsJson.Get<string>("IsDefault");

			var category = await parentContentTypeJson.Get("ID", "").GetCategoryByAliasAsync(parentIdentity, cancellationToken).ConfigureAwait(false);
			var categoryURL = category?.GetURL(desktop, true);

			JArray breadcrumbs = null, metaTags = null;
			JObject pagination = null, seoInfo = null, filterBy = null, sortBy = null;
			string coverURI = null, ogURL = null, ogTitle = null, prevURL = null, nextURL = null, seoTitle = null, seoDescription = null, seoKeywords = null, data = null, ids = null, attachmentScripts = null;
			DateTime? expiresAt = null;

			var showThumbnails = options.Get("ShowThumbnails", options.Get("ShowThumbnail", true)) || options.Get("ShowPngThumbnails", false) || options.Get("ShowAsPngThumbnails", false);
			var pngThumbnails = options.Get("ThumbnailsAsPng", options.Get("ThumbnailAsPng", options.Get("ShowPngThumbnails", options.Get("ShowAsPngThumbnails", false))));
			var thumbnailsWidth = options.Get("ThumbnailsWidth", options.Get("ThumbnailWidth", 0));
			var thumbnailsHeight = options.Get("ThumbnailsHeight", options.Get("ThumbnailHeight", 0));

			var showAttachments = options.Get("ShowAttachments", false);
			var showBreadcrumbs = options.Get("ShowBreadcrumbs", false);
			var showPagination = options.Get("ShowPagination", false);

			var isDebugLogEnabled = requestInfo.IsWriteDebugLogs();
			var isCacheLogEnabled = requestInfo.IsWriteCacheLogs();
			var forceCache = requestInfo.IsForceCache();

			// generate list
			if (isList)
			{
				// check permission
				var contentType = await (contentTypeID ?? "").GetContentTypeByIDAsync(cancellationToken).ConfigureAwait(false);
				var organization = category?.Organization ?? contentType?.Organization ?? await organizationJson.Get("ID", "").GetOrganizationByIDAsync(cancellationToken).ConfigureAwait(false);
				var parentCategory = category?.ParentCategory;
				var parentPrivileges = parentCategory?.OriginalPrivileges;
				while (parentCategory != null && parentPrivileges == null)
				{
					parentCategory = parentCategory?.ParentCategory;
					parentPrivileges = parentCategory?.OriginalPrivileges;
				}
				var gotRights = isSystemAdministrator || requestInfo.Session.User.IsViewer(category?.WorkingPrivileges, parentPrivileges ?? contentType?.WorkingPrivileges, organization);
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

				// prepare cache
				var cacheKeyOfTotalObjects = Extensions.GetCacheKeyOfTotalObjects(filter, sort);
				var cacheKeyOfObjectsXml = Extensions.GetCacheKeyOfObjectsXml(filter, sort, pageSize, pageNumber, $":o#{optionsJson.ToString(Formatting.None).GenerateUUID()}");

				if (forceCache)
					Utility.Cache.RemoveAsync([Extensions.GetCacheKey(filter, sort, pageSize, pageNumber), cacheKeyOfTotalObjects, cacheKeyOfObjectsXml], Utility.CancellationToken).Execute();

				var totalRecords = !forceCache && !Utility.IsCacheDisabled && await Utility.Cache.ExistsAsync(cacheKeyOfTotalObjects, cancellationToken).ConfigureAwait(false)
					? await Utility.Cache.GetAsync<long>(cacheKeyOfTotalObjects, cancellationToken).ConfigureAwait(false)
					: -1;
				var totalPages = (totalRecords, pageSize).GetTotalPages();
				if (pageNumber > totalPages && totalRecords > -1)
				{
					pageNumber = totalPages;
					cacheKeyOfObjectsXml = Extensions.GetCacheKeyOfObjectsXml(filter, sort, pageSize, pageNumber, $":o#{optionsJson.ToString(Formatting.None).GenerateUUID()}");
					if (forceCache)
						Utility.Cache.RemoveAsync([Extensions.GetCacheKey(filter, sort, pageSize, pageNumber), cacheKeyOfTotalObjects, cacheKeyOfObjectsXml], Utility.CancellationToken).Execute();
				}

				if (randomPage && totalRecords > -1)
				{
					minRandomPage = minRandomPage > 0 && minRandomPage <= totalPages ? minRandomPage : 1;
					maxRandomPage = maxRandomPage > 0 && maxRandomPage <= totalPages ? maxRandomPage : totalPages;
					pageNumber = UtilityService.GetRandomNumber(minRandomPage, maxRandomPage);
					cacheKeyOfObjectsXml = Extensions.GetCacheKeyOfObjectsXml(filter, sort, pageSize, pageNumber, $":o#{optionsJson.ToString(Formatting.None).GenerateUUID()}");
				}

				JToken categoryThumbnails = null;
				data = forceCache || Utility.IsCacheDisabled ? null : await Utility.Cache.GetAsync<string>(cacheKeyOfObjectsXml, cancellationToken).ConfigureAwait(false);

				// process if has no cache
				if (string.IsNullOrWhiteSpace(data))
				{
					// search
					var results = await requestInfo.SearchAsync(null, filter, sort, pageSize, pageNumber, contentTypeID, -1, cancellationToken, true, randomPage, minRandomPage, maxRandomPage).ConfigureAwait(false);
					var objects = results.Objects;
					totalRecords = results.TotalRecords;
					pageNumber = results.PageNumber;
					var thumbnails = results.Thumbnails;

					// generate xml
					Exception exception = null;
					var dataXml = XElement.Parse("<Data/>");
					objects.ForEach(@object =>
					{
						if (exception == null && @object.Status.Equals(ApprovalStatus.Published))
							try
							{
								if (@object.EndDate != null && DateTime.TryParse($"{@object.EndDate} 23:59:59", out var expires))
								{
									if (expires < DateTime.Now)
										return;
									expiresAt = expiresAt == null || expiresAt < expires
										? expires
										: expiresAt;
								}
								dataXml.Add(@object.ToXml(false, cultureInfo, element =>
								{
									element.CleanInvalidCharacters();
									element.Element("Details")?.Remove();
									element.Element("StartDate")?.UpdateDateTime(cultureInfo, customDateTimeFormat);
									element.Element("EndDate")?.UpdateDateTime(cultureInfo, customDateTimeFormat);
									element.Element("PublishedTime")?.UpdateDateTime(cultureInfo, customDateTimeFormat);
									if (!string.IsNullOrWhiteSpace(@object.Summary))
										element.Element("Summary").Value = @object.Summary.RemoveTags().NormalizeHTMLBreaks();
									element.Add(new XElement("Category", @object.Category?.Title ?? "", new XAttribute("URL", @object.Category?.GetURL(desktop) ?? "")));
									element.Add(new XElement("URL", @object.GetURL(desktop) ?? ""));
									element.AddThumbnail(thumbnails?.GetThumbnailURL(@object.ID, thumbnailsWidth, thumbnailsHeight, pngThumbnails), pngThumbnails);
								}));
							}
							catch (Exception ex)
							{
								exception = requestInfo.GetRuntimeException(ex, null, (msg, exc) => requestInfo.WriteErrorAsync(exc, $"Error occurred while generating a content => {msg} : {@object.ToJson()}", "Errors").Execute());
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
						parentCategory = category.ParentCategory;
						while (parentCategory?.ParentCategory != null)
							parentCategory = parentCategory.ParentCategory;
						requestInfo.Header["x-thumbnails-as-attachments"] = "true";
						categoryThumbnails = await requestInfo.GetThumbnailsAsync(category.ID, category.Title.Url64Encode(), Utility.ValidationKey, cancellationToken).ConfigureAwait(false);
						dataXml.Add(new XElement(
							"Parent",
							new XElement("Title", category.Title),
							new XElement("Description", category.Description?.RemoveTags().NormalizeHTMLBreaks() ?? ""),
							new XElement("Notes", category.Notes?.NormalizeHTMLBreaks() ?? ""),
							new XElement("URL", categoryURL.Replace("/{{pageNumber}}", "")),
							(categoryThumbnails?.GetThumbnailURL(category.ID, thumbnailsWidth, thumbnailsHeight, pngThumbnails) ?? "").GetThumbnail(pngThumbnails),
							new XElement("Root", parentCategory?.Title ?? category.Title, new XAttribute("URL", parentCategory?.GetURL(desktop) ?? categoryURL.Replace("/{{pageNumber}}", "")))
						));
					}

					// get data
					data = dataXml.CleanInvalidCharacters().ToString(SaveOptions.DisableFormatting);

					// update cache
					var cacheKeys = results.CacheKeys.Concat([cacheKeyOfObjectsXml]).ToList();
					Task.WhenAll
					(
						expiresAt != null
							? expiresAt.Value < DateTime.Now
								? Utility.Cache.RemoveAsync(cacheKeys, Utility.CancellationToken)
								: Utility.Cache.SetAsync(cacheKeyOfObjectsXml, data, expiresAt.Value, Utility.CancellationToken)
							: Utility.Cache.SetAsync(cacheKeyOfObjectsXml, data, Utility.CancellationToken),
						contentType != null
							? Utility.Cache.AddSetMembersAsync(contentType.GetSetCacheKey(), cacheKeys.Concat(objects.Select(@object => @object.GetCacheKeyOfAliasedContent())), Utility.CancellationToken)
							: Task.CompletedTask,
						category != null
							? Utility.Cache.AddSetMembersAsync(category.GetSetCacheKey(), cacheKeys, Utility.CancellationToken)
							: Task.CompletedTask,
						isCacheLogEnabled
							? requestInfo.WriteLogAsync($"Update related keys into set (Content-Type and Categoory) when generate collection of CMS.Content [{contentType?.Title} - ID: {contentType?.ID} - Set: {contentType?.GetSetCacheKey()} / {category?.GetSetCacheKey()}]\r\n- Related cache keys: {cacheKeys.Join(", ")}]", "Caches")
							: Task.CompletedTask
					).Execute();

					// preload
					if (Utility.Preload && objects.Count > 0)
						objects.PreloadAsync(Utility.CancellationToken, requestInfo.CorrelationID).Execute();
				}
				else if (showPagination)
				{
					totalRecords = totalRecords < 0 && !Utility.IsCacheDisabled
						? await Utility.Cache.GetAsync<long>(cacheKeyOfTotalObjects, cancellationToken).ConfigureAwait(false)
						: totalRecords;

					if (totalRecords < 1)
					{
						Utility.Cache.RemoveAsync(cacheKeyOfTotalObjects, Utility.CancellationToken).Execute();
						totalRecords = await Content.CountAsync(filter, contentTypeID, true, cacheKeyOfTotalObjects, 0, cancellationToken).ConfigureAwait(false);
						if (contentType != null)
							Utility.Cache.AddSetMemberAsync(contentType.GetSetCacheKey(), cacheKeyOfTotalObjects, Utility.CancellationToken).Execute();
					}
				}

				// other info
				ids = "system:" + (contentType != null ? $"\"{contentType.SystemID}\"" : null) + ","
					+ "repository:" + (contentType != null ? $"\"{contentType.RepositoryID}\"" : null) + ","
					+ "entity:" + (contentType != null ? $"\"{contentType.ID}\"" : null);

				if (category != null)
				{
					var categoryID = filter?.GetValue("CategoryID");
					if (!string.IsNullOrWhiteSpace(categoryID) && categoryID.IsValidUUID() && !categoryID.IsEquals(category.ID))
						category = await categoryID.GetCategoryByIDAsync(cancellationToken).ConfigureAwait(false);
					ids += (ids != "" ? "," : "") + "category:" + (category != null ? $"\"{category.ID}\"" : null);
				}

				// prepare breadcrumbs
				if (showBreadcrumbs)
					breadcrumbs = category?.GenerateBreadcrumbs(desktop) ?? new();

				// prepare pagination
				totalPages = totalPages > 0 ? totalPages : (totalRecords, pageSize).GetTotalPages();
				if (totalPages > 0 && pageNumber > totalPages)
					pageNumber = totalPages;
				if (showPagination)
					pagination = Utility.GeneratePagination(totalRecords, totalPages, pageSize, pageNumber, categoryURL, showPageLinks, numberOfPageLinks, requestInfo.Query?.Where(kvp => kvp.Key.IsStartsWith("ngx-")).Select(kvp => $"{kvp.Key}={kvp.Value?.UrlEncode()}").Join("&"));

				// prepare SEO info
				seoTitle = category?.Title;
				seoDescription = category?.Description;
				categoryThumbnails = category != null ? categoryThumbnails ?? await requestInfo.GetThumbnailsAsync(category.ID, category.Title.Url64Encode(), Utility.ValidationKey, cancellationToken).ConfigureAwait(false) : null;
				coverURI = (categoryThumbnails as JArray)?.FirstOrDefault()?.Get<string>("URI")?.GetThumbnailURL(thumbnailsWidth, thumbnailsHeight, pngThumbnails);
				ogTitle = category?.Title;
				ogURL = categoryURL?.Replace("/{{pageNumber}}", pageNumber > 1 ? $"/{pageNumber}" : "");
				prevURL = pageNumber > 1 ? categoryURL?.Replace("/{{pageNumber}}", pageNumber > 2 ? $"/{pageNumber - 1}" : "") : null;
				nextURL = pageNumber < totalPages ? categoryURL?.Replace("/{{pageNumber}}", $"/{pageNumber + 1}") : null;
			}

			// generate details
			else
			{
				// process cache
				var contentAlias = requestJson.Get<string>("ContentIdentity");
				if (forceCache)
				{
					var cacheKeyOfAlias = contentTypeID.GetCacheKeyOfAliasedContent(category?.ID, contentAlias);
					var contentIdentity = string.IsNullOrWhiteSpace(cacheKeyOfAlias) ? null : await Utility.Cache.GetAsync<string>(cacheKeyOfAlias, cancellationToken).ConfigureAwait(false);
					Utility.Cache.RemoveAsync([cacheKeyOfAlias, contentIdentity?.GetCacheKey<Content>()], Utility.CancellationToken).Execute();
				}

				// get the requested object
				var @object = await Content.GetContentByAliasAsync(contentTypeID, contentAlias, category?.ID, cancellationToken).ConfigureAwait(false) ?? throw new InformationNotFoundException();
				if (@object.Organization == null || @object.Module == null || @object.ContentType == null)
					throw new InformationInvalidException("The organization/module/content-type is invalid");

				// check permission
				var parentCategory = @object?.Category?.ParentCategory;
				var parentPrivileges = parentCategory?.OriginalPrivileges;
				while (parentCategory != null && parentPrivileges == null)
				{
					parentCategory = parentCategory?.ParentCategory;
					parentPrivileges = parentCategory?.OriginalPrivileges;
				}
				var gotRights = isSystemAdministrator || requestInfo.Session.User.ID.IsEquals(@object.Organization.OwnerID) || @object.Status.Equals(ApprovalStatus.Published)
					? requestInfo.Session.User.IsViewer(@object.WorkingPrivileges, parentPrivileges ?? @object.ContentType.WorkingPrivileges, @object.Organization)
					: requestInfo.Session.User.ID.IsEquals(@object.CreatedID) || requestInfo.Session.User.IsEditor(@object.WorkingPrivileges, parentPrivileges ?? @object.ContentType.WorkingPrivileges, @object.Organization);
				if (!gotRights)
					throw new AccessDeniedException();

				// check end date
				if (@object.Status.Equals(ApprovalStatus.Published) && @object.EndDate != null && DateTime.TryParse($"{@object.EndDate} 23:59:59", out var expires))
					expiresAt = expires;
				if (expiresAt != null && expiresAt.Value < DateTime.Now)
				{
					gotRights = isSystemAdministrator || requestInfo.Session.User.ID.IsEquals(@object.Organization.OwnerID) || requestInfo.Session.User.ID.IsEquals(@object.CreatedID) || requestInfo.Session.User.IsEditor(@object.WorkingPrivileges, parentPrivileges ?? @object.ContentType.WorkingPrivileges, @object.Organization);
					if (!gotRights)
						throw new AccessDeniedException();
				}

				// check published time
				var validatePublishedTime = options.Get("ValidatePublished", options.Get("ValidatePublishedTime", options.Get("ValidateWithPublishedTime", false)));
				if (validatePublishedTime && @object.Status.Equals(ApprovalStatus.Published) && @object.PublishedTime != null && @object.PublishedTime.Value > DateTime.Now)
				{
					gotRights = isSystemAdministrator || requestInfo.Session.User.ID.IsEquals(@object.Organization.OwnerID) || requestInfo.Session.User.ID.IsEquals(@object.CreatedID) || requestInfo.Session.User.IsEditor(@object.WorkingPrivileges, parentPrivileges ?? @object.ContentType.WorkingPrivileges, @object.Organization);
					if (!gotRights)
						throw new AccessDeniedException();
				}

				Task<JToken> thumbnailsTask = null, attachmentsTask = null;

				// get cache
				var cacheKey = $"{@object.GetCacheKey()}:xml:o#{optionsJson.ToString(Formatting.None).GenerateUUID()}:p#{paginationJson.ToString(Formatting.None).GenerateUUID()}";
				data = forceCache || Utility.IsCacheDisabled ? null : await Utility.Cache.GetAsync<string>(cacheKey, cancellationToken).ConfigureAwait(false);

				// process if has no cache
				if (string.IsNullOrWhiteSpace(data))
				{
					var showRelateds = options.Get("ShowRelateds", false);
					var showOthers = options.Get("ShowOthers", false);
					contentTypeID = @object.ContentTypeID;

					// get related contents
					var relatedsTask = showRelateds ? @object.LoadRelatedsAsync(cancellationToken, requestInfo.CorrelationID) : Task.FromResult(new List<Content>());

					// get other contents
					var numberOfOthers = options.Get("NumberOfOthers", 12);
					var othersTask = showOthers ? @object.LoadOthersAsync(cancellationToken, requestInfo.CorrelationID, numberOfOthers > 0 ? numberOfOthers : 12, forceCache) : Task.FromResult(new List<Content>());

					// get files
					requestInfo.Header["x-thumbnails-as-attachments"] = "true";
					if (requestInfo.ContainsKey("x-logs"))
						requestInfo.Header["x-logs"] = "true";
					if (requestInfo.ContainsKey("x-force-cache"))
						requestInfo.Header["x-force-cache"] = "true";
					thumbnailsTask = showThumbnails ? requestInfo.GetThumbnailsAsync(@object.ID, @object.Title.Url64Encode(), Utility.ValidationKey, cancellationToken) : Task.FromResult<JToken>(new JArray());
					attachmentsTask = showAttachments ? requestInfo.GetAttachmentsAsync(@object.ID, @object.Title.Url64Encode(), Utility.ValidationKey, cancellationToken) : Task.FromResult<JToken>(new JArray());

					// wait for all tasks are completed
					await Task.WhenAll(relatedsTask, othersTask, thumbnailsTask, attachmentsTask).ConfigureAwait(false);

					var relateds = new List<Content>();
					JToken relatedThumbnails = null;
					if (showRelateds)
					{
						relateds = relatedsTask.Result.Where(related => related != null && related.ID != null && related.ID != @object.ID && related.Status.Equals(ApprovalStatus.Published) && related.PublishedTime != null && related.PublishedTime.Value <= DateTime.Now).ToList();
						relatedThumbnails = relateds.Count < 1
							? null
							: relateds.Count == 1
								? await requestInfo.GetThumbnailsAsync(relateds[0].ID, relateds[0].Title.Url64Encode(), Utility.ValidationKey, cancellationToken).ConfigureAwait(false)
								: await requestInfo.GetThumbnailsAsync(relateds.Select(obj => obj.ID).Join(","), relateds.ToJObject("ID", obj => new JValue(obj.Title.Url64Encode())).ToString(Formatting.None), Utility.ValidationKey, cancellationToken).ConfigureAwait(false);
					}

					var others = new List<Content>();
					JToken otherThumbnails = null;
					if (showOthers)
					{
						others = othersTask.Result;
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
							xml.Element("Summary").Value = @object.Summary.RemoveTags().NormalizeHTMLBreaks();

						xml.Add(new XElement("Category", @object.Category?.Title ?? "", new XAttribute("URL", @object.Category?.GetURL(desktop) ?? "")));
						xml.Add(new XElement("URL", @object.GetURL(desktop) ?? ""));

						if (showThumbnails)
						{
							var thumbnails = new XElement("Thumbnails");
							(thumbnailsTask.Result as JArray)?.ForEach(thumbnail => thumbnails.Add((thumbnail.Get<string>("URI")?.GetThumbnailURL(thumbnailsWidth, thumbnailsHeight, pngThumbnails) ?? "").GetThumbnail(pngThumbnails, "Thumbnail")));
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
						relateds.OrderByDescending(related => related.StartDate).ThenByDescending(related => related.PublishedTime).ForEach(related =>
						{
							var relatedXml = new XElement("Content", new XElement("ID", related.ID));
							relatedXml.Add(new XElement("Title", related.Title), new XElement("Author", related.Author ?? ""), new XElement("Summary", related.Summary?.RemoveTags().NormalizeHTMLBreaks() ?? ""));
							relatedXml.Add(new XElement("PublishedTime", related.PublishedTime != null ? related.PublishedTime.Value : DateTime.Now).UpdateDateTime(cultureInfo, customDateTimeFormat));
							relatedXml.Add(new XElement("Category", related.Category?.Title ?? "", new XAttribute("URL", related.Category?.GetURL(desktop) ?? "")));
							relatedXml.Add(new XElement("URL", related.GetURL(desktop) ?? ""));
							relatedXml.AddThumbnail(relatedThumbnails?.GetThumbnailURL(related.ID, thumbnailsWidth, thumbnailsHeight, pngThumbnails), pngThumbnails);
							if (!string.IsNullOrWhiteSpace(related.Summary))
								relatedXml.Element("Summary").Value = related.Summary.NormalizeHTMLBreaks();
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
							otherXml.Add(new XElement("Category", other.Category?.Title ?? "", new XAttribute("URL", other.Category?.GetURL(desktop) ?? "")));
							otherXml.Add(new XElement("URL", other.GetURL(desktop) ?? ""));
							otherXml.AddThumbnail(otherThumbnails?.GetThumbnailURL(other.ID, thumbnailsWidth, thumbnailsHeight, pngThumbnails), pngThumbnails);
							if (!string.IsNullOrWhiteSpace(other.Summary))
								otherXml.Element("Summary").Value = other.Summary.NormalizeHTMLBreaks();
						})));
						dataXml.Add(othersXml);
					}

					// main category
					if (category != null)
					{
						parentCategory = category.ParentCategory;
						while (parentCategory?.ParentCategory != null)
							parentCategory = parentCategory.ParentCategory;
						requestInfo.Header["x-thumbnails-as-attachments"] = "true";
						var categoryThumbnails = await requestInfo.GetThumbnailsAsync(category.ID, category.Title.Url64Encode(), Utility.ValidationKey, cancellationToken).ConfigureAwait(false);
						var thumbnailURL = categoryThumbnails?.GetThumbnailURL(category.ID, thumbnailsWidth, thumbnailsHeight, pngThumbnails);
						dataXml.Add(new XElement(
							"Parent",
							new XElement("Title", category.Title),
							new XElement("Description", category.Description?.RemoveTags().NormalizeHTMLBreaks() ?? ""),
							new XElement("Notes", category.Notes?.NormalizeHTMLBreaks() ?? ""),
							new XElement("URL", category.GetURL(desktop) ?? ""),
							(categoryThumbnails?.GetThumbnailURL(category.ID, thumbnailsWidth, thumbnailsHeight, pngThumbnails) ?? "").GetThumbnail(pngThumbnails),
							new XElement("Root", parentCategory?.Title ?? category.Title, new XAttribute("URL", parentCategory?.GetURL(desktop) ?? category.GetURL(desktop)))
						));
					}

					// validate and get data of xml
					data = dataXml.CleanInvalidCharacters().ToString(SaveOptions.DisableFormatting);

					// update cache
					Task.WhenAll
					(
						expiresAt != null
							? expiresAt.Value < DateTime.Now
								? Task.CompletedTask
								: Utility.Cache.SetAsync(cacheKey, data, expiresAt.Value, Utility.CancellationToken)
							: Utility.Cache.SetAsync(cacheKey, data, Utility.CancellationToken),
						@object.ContentType != null
							? Utility.Cache.AddSetMemberAsync(@object.ContentType.ObjectCacheKeys, @object.GetCacheKey(), Utility.CancellationToken)
							: Task.CompletedTask,
						@object.ContentType != null
							? Utility.Cache.AddSetMembersAsync(@object.ContentType.GetSetCacheKey(), [cacheKey, @object.GetCacheKeyOfAliasedContent()], Utility.CancellationToken)
							: Task.CompletedTask,
						@object.Category != null
							? Utility.Cache.AddSetMemberAsync(@object.Category.GetSetCacheKey(), cacheKey, Utility.CancellationToken)
							: Task.CompletedTask,
						isCacheLogEnabled
							? requestInfo.WriteLogAsync($"Update related keys into set (Content-Type and Category) set when generate details of CMS.Content [{@object.ContentType?.Title} - ID: {@object.ContentType?.ID} - Set: {@object.ContentType?.GetSetCacheKey()} / {@object.Category?.GetSetCacheKey()}]\r\n- Related cache keys: {cacheKey}]", "Caches")
							: Task.CompletedTask
					).Execute();

					// preload
					if (Utility.Preload && (relateds.Count > 0 || others.Count > 0))
						relateds.Concat(others).PreloadAsync(Utility.CancellationToken, requestInfo.CorrelationID).Execute();
				}

				// build others
				breadcrumbs = showBreadcrumbs ? @object.Category?.GenerateBreadcrumbs(desktop) ?? new() : null;
				pagination = showPagination ? Utility.GeneratePagination(1, 1, 0, pageNumber, @object.GetURL(desktop, true), showPageLinks, numberOfPageLinks) : null;
				thumbnailsTask = thumbnailsTask == null || !showThumbnails ? requestInfo.GetThumbnailsAsync(@object.ID, @object.Title.Url64Encode(), Utility.ValidationKey, cancellationToken) : thumbnailsTask;
				attachmentsTask = attachmentsTask == null || !showAttachments ? requestInfo.GetAttachmentsAsync(@object.ID, @object.Title.Url64Encode(), Utility.ValidationKey, cancellationToken) : attachmentsTask;
				await Task.WhenAll(thumbnailsTask, attachmentsTask).ConfigureAwait(false);
				coverURI = (thumbnailsTask.Result as JArray)?.First()?.Get<string>("URI")?.GetThumbnailURL(thumbnailsWidth, thumbnailsHeight, pngThumbnails);
				metaTags = new[] { $"<meta property=\"og:type\" content=\"{options.Get("Og:Type", "article")}\"/>" }.ToJArray();
				ogURL = @object.GetURL(desktop);
				ogTitle = @object.Title;
				seoTitle = @object.Title;
				seoDescription = @object.Summary;
				seoKeywords = @object.Tags;
				ids = $"system:\"{@object.SystemID}\",repository:\"{@object.RepositoryID}\",entity:\"{@object.RepositoryEntityID}\",category:\"{@object.CategoryID}\",id:\"{@object.ID}\"";
				attachmentScripts = "attachments:["
					+ (attachmentsTask.Result as JArray)?.Select(attachment => attachment.Get<string>("ContentType").IsStartsWith("image/")
						? "{\"id\":\"" + attachment.Get<string>("ID") + "\",\"filename\":\"" + attachment.Get<string>("Filename") + "\",\"content-type\":\"" + attachment.Get<string>("ContentType") + "\"}"
						: null).Where(attachment => attachment != null).Join(",")
					+ "]";
			}

			// SEO
			seoInfo = new JObject
			{
				{ "Title", seoTitle },
				{ "Description", string.IsNullOrWhiteSpace(seoDescription) || seoDescription.IsStartsWith("~~/") || seoDescription.IsStartsWith("http://") || seoDescription.IsStartsWith("https://") ? null : seoDescription.RemoveTags() },
				{ "Keywords", seoKeywords },
				{ "PrevURL", prevURL },
				{ "NextURL", nextURL },
				{ "Og:URL", ogURL },
				{ "Og:Title", ogTitle },
			};

			// response
			var contentTypeDefinitionJson = requestJson.Get<JObject>("ContentTypeDefinition");
			var moduleDefinitionJson = requestJson.Get<JObject>("ModuleDefinition");
			return new JObject
			{
				{ "Data", data },
				{ "Breadcrumbs", breadcrumbs },
				{ "Pagination", pagination },
				{ "FilterBy", filterBy },
				{ "SortBy", sortBy },
				{ "SEOInfo", seoInfo },
				{ "CoverURI", coverURI },
				{ "MetaTags", metaTags },
				{ "CacheExpiration", expiresAt != null ? expiresAt.Value.ToDTString() : (randomPage ? Utility.IsCacheLogEnabled ? 3 : 13 : 0).ToString() },
				{ "IDs", ids + $",service:\"{moduleDefinitionJson.Get<string>("ServiceName").ToLower()}\",object:\"{contentTypeDefinitionJson.Get<string>("ObjectNamePrefix")?.ToLower()}{contentTypeDefinitionJson.Get<string>("ObjectName").ToLower()}{contentTypeDefinitionJson.Get<string>("ObjectNameSuffix")?.ToLower()}\"" },
				{ "AttachmentScripts", attachmentScripts }
			};
		}

		static async Task<List<Content>> LoadRelatedsAsync(this Content @object, CancellationToken cancellationToken = default, string correlationID = null)
		{
			var relateds = new List<Content>();
			if (@object.Relateds != null && @object.Relateds.Count > 0)
			{
				var stopwatch = Stopwatch.StartNew();
				await @object.Relateds.ForEachAsync(async (id, cancellationtoken) => relateds.Add(await Content.GetAsync(id, !Utility.IsCacheDisabled, cancellationtoken).ConfigureAwait(false)), cancellationToken, true, Utility.RunProcessorInParallelsMode).ConfigureAwait(false);

				var cacheKeys = relateds.Select(related => related?.GetCacheKey()).Where(key => !string.IsNullOrWhiteSpace(key)).ToList();
				await Utility.Cache.AddSetMembersAsync(@object.ContentType.ObjectCacheKeys, cacheKeys, cancellationToken).ConfigureAwait(false);

				cacheKeys = relateds.Select(related => new[] { related?.GetCacheKey(), related?.GetCacheKeyOfAliasedContent() }).SelectMany(keys => keys).Where(key => !string.IsNullOrWhiteSpace(key)).ToList();
				await Utility.Cache.AddSetMembersAsync(@object.ContentType.GetSetCacheKey(), cacheKeys, cancellationToken).ConfigureAwait(false);

				stopwatch.Stop();
				if (Utility.IsCacheLogEnabled)
					await Utility.WriteLogAsync(correlationID, $"Update object cache keys into Content-Type's set when load related collection of CMS.Content - Execution times: {stopwatch.GetElapsedTimes()} - [{@object.ContentType.Title} - ID: {@object.ContentType.ID} - Set (objects): {@object.ContentType.ObjectCacheKeys}]\r\n- Objects cache keys ({cacheKeys.Count}): {cacheKeys.Join(", ")}", "Caches").ConfigureAwait(false);
			}
			return relateds;
		}

		static async Task<List<Content>> LoadOthersAsync(this Content @object, CancellationToken cancellationToken = default, string correlationID = null, int numberOfOthers = 12, bool forceCache = false)
		{
			var stopwatch = Stopwatch.StartNew();
			var objectCacheKey = @object.GetCacheKey();

			if (forceCache)
				await Utility.Cache.RemoveAsync([$"{objectCacheKey}:others", $"{objectCacheKey}:newers", $"{objectCacheKey}:olders"], cancellationToken).ConfigureAwait(false);

			var relatedCacheKeys = new List<string>
			{
				$"{objectCacheKey}:others"
			};

			var others = new List<Content>();
			var otherIDs = Utility.IsCacheDisabled ? null : await Utility.Cache.GetAsync<List<string>>($"{objectCacheKey}:others", cancellationToken).ConfigureAwait(false);
			if (otherIDs != null && otherIDs.Count > 0)
				await otherIDs.ForEachAsync(async (id, cancellationtoken) => others.Add(await Content.GetAsync(id, !Utility.IsCacheDisabled, cancellationtoken).ConfigureAwait(false)), cancellationToken, true, Utility.RunProcessorInParallelsMode).ConfigureAwait(false);

			else
			{
				var newersTask = Content.FindAsync(Filters<Content>.And
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
					Filters<Content>.GreaterOrEquals("PublishedTime", @object.PublishedTime != null ? @object.PublishedTime.Value.GetTimeQuarter() : DateTime.Now.GetTimeQuarter())
				), Sorts<Content>.Descending("StartDate").ThenByDescending("PublishedTime"), numberOfOthers, 1, @object.ContentTypeID, $"{objectCacheKey}:newers", cancellationToken);

				var oldersTask = Content.FindAsync(Filters<Content>.And
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
					Filters<Content>.LessThanOrEquals("PublishedTime", @object.PublishedTime != null ? @object.PublishedTime.Value.GetTimeQuarter() : DateTime.Now.GetTimeQuarter())
				), Sorts<Content>.Descending("StartDate").ThenByDescending("PublishedTime"), numberOfOthers, 1, @object.ContentTypeID, !Utility.IsCacheDisabled, $"{objectCacheKey}:olders", cancellationToken);

				if (Utility.RunProcessorInParallelsMode)
					await Task.WhenAll(newersTask, oldersTask).ConfigureAwait(false);
				else
				{
					await newersTask.ConfigureAwait(false);
					await oldersTask.ConfigureAwait(false);
				}

				if (newersTask.Result.Count + oldersTask.Result.Count > 0)
				{
					numberOfOthers = newersTask.Result.Count + oldersTask.Result.Count > numberOfOthers ? numberOfOthers / 2 : numberOfOthers;
					others = newersTask.Result.Take(numberOfOthers).Concat(oldersTask.Result.Take(numberOfOthers)).ToList();
				}

				relatedCacheKeys.Add($"{objectCacheKey}:newers");
				relatedCacheKeys.Add($"{objectCacheKey}:olders");
			}

			others = others.Where(other => other != null && other.ID != null && other.ID != @object.ID && other.Status.Equals(ApprovalStatus.Published) && other.PublishedTime != null && other.PublishedTime.Value <= DateTime.Now).ToList();
			relatedCacheKeys = relatedCacheKeys.Concat(others.Select(obj => new[] { obj?.GetCacheKey(), obj?.GetCacheKeyOfAliasedContent() }).SelectMany(keys => keys).Where(key => !string.IsNullOrWhiteSpace(key))).ToList();
			var objectCacheKeys = others.Select(obj => obj?.GetCacheKey()).Where(key => !string.IsNullOrWhiteSpace(key)).ToList();

			Task.WhenAll
			(
				Utility.Cache.SetAsync($"{objectCacheKey}:others", others.Select(other => other.ID).ToList(), Utility.CancellationToken),
				Utility.Cache.AddSetMembersAsync(@object.ContentType.ObjectCacheKeys, objectCacheKeys, Utility.CancellationToken),
				Utility.Cache.AddSetMembersAsync(@object.ContentType.GetSetCacheKey(), relatedCacheKeys, Utility.CancellationToken)
			).Execute();

			stopwatch.Stop();
			if (Utility.IsCacheLogEnabled)
				await Utility.WriteLogAsync(correlationID, $"Update related keys into Content-Type's set when load other collection of CMS.Content - Execution times: {stopwatch.GetElapsedTimes()} - [{@object.ContentType.Title} - ID: {@object.ContentType.ID}\r\n- Set (objects): {@object.ContentType.ObjectCacheKeys}]\r\n- Object cache keys ({objectCacheKeys.Count}): {objectCacheKeys.Join(", ")}\r\n- Set (relateds): {@object.ContentType.GetSetCacheKey()}]\r\n- Related cache keys ({relatedCacheKeys.Count}): {relatedCacheKeys.Join(", ")}", "Caches").ConfigureAwait(false);

			return others;
		}

		static async Task PreloadAsync(this IEnumerable<Content> objects, CancellationToken cancellationToken = default, string correlationID = null)
		{
			// wait for few times
			await Task.Delay(UtilityService.GetRandomNumber(456, 789), cancellationToken).ConfigureAwait(false);

			// prepare
			var stopwatch = Stopwatch.StartNew();
			objects = objects.Select(@object => @object.ID).Distinct(StringComparer.OrdinalIgnoreCase).Select(id => objects.First(@object => @object.ID == id)).ToList();
			if (Utility.IsCacheLogEnabled)
				await Utility.WriteLogAsync(correlationID, $"Start to pre-load collection of CMS.Content ({objects.Count()})", "Caches").ConfigureAwait(false);

			// load realteds
			await objects.ForEachAsync(@object => @object.LoadRelatedsAsync(cancellationToken, correlationID), true, false).ConfigureAwait(false);

			// load others
			await objects.ForEachAsync(@object => @object.LoadOthersAsync(cancellationToken, correlationID), true, false).ConfigureAwait(false);

			stopwatch.Stop();
			if (Utility.IsCacheLogEnabled)
				await Utility.WriteLogAsync(correlationID, $"Complete pre-load collection of CMS.Content - Execution times: {stopwatch.GetElapsedTimes()}", "Caches").ConfigureAwait(false);
		}

		internal static async Task<JObject> SyncContentAsync(this RequestInfo requestInfo, CancellationToken cancellationToken, bool sendNotifications = false, bool dontCreateNewVersion = false)
		{
			// prepare
			var @event = requestInfo.GetParameter("event") ?? requestInfo.GetParameter("x-original-event");
			if (string.IsNullOrWhiteSpace(@event) || !@event.IsEquals("Delete"))
				@event = "Update";

			var data = requestInfo.GetBodyExpando();
			var content = await Content.GetAsync(data.Get<string>("ID"), !Utility.IsCacheDisabled, cancellationToken).ConfigureAwait(false);
			var oldStatus = content != null ? content.Status : ApprovalStatus.Pending;

			// sync
			if (!@event.IsEquals("Delete"))
			{
				if (content == null)
				{
					content = data.CreateContent(null, out var _, obj =>
					{
						obj.StartDate = string.IsNullOrWhiteSpace(obj.StartDate)
							? obj.PublishedTime != null
								? obj.PublishedTime.Value.ToDTString(false, false)
								: DateTime.Now.ToDTString(false, false)
							: obj.StartDate;
						obj.EndDate = string.IsNullOrWhiteSpace(obj.EndDate) || obj.EndDate.Equals("-") ? null : obj.EndDate;
						obj.Details = obj.Organization.NormalizeURLs(obj.Details, false);
						obj.CreatedID = string.IsNullOrWhiteSpace(obj.CreatedID) ? requestInfo.Session.User.ID : obj.CreatedID;
						obj.LastModifiedID = string.IsNullOrWhiteSpace(obj.LastModifiedID) ? requestInfo.Session.User.ID : obj.LastModifiedID;
					});
					var existing = await Content.GetContentByAliasAsync(content.RepositoryEntityID, content.Alias, content.CategoryID, cancellationToken).ConfigureAwait(false);
					if (existing != null && !existing.ID.IsEquals(content.ID))
						content.Alias = $"{content.Alias}-{DateTime.Now.ToUnixTimestamp()}-{UtilityService.GetRandomNumber()}";
					await Content.CreateAsync(content, cancellationToken).ConfigureAwait(false);
				}
				else
				{
					content.Update(data, null, out var _, obj =>
					{
						obj.StartDate = string.IsNullOrWhiteSpace(obj.StartDate)
							? obj.PublishedTime != null
								? obj.PublishedTime.Value.ToDTString(false, false)
								: DateTime.Now.ToDTString(false, false)
							: obj.StartDate;
						obj.EndDate = string.IsNullOrWhiteSpace(obj.EndDate) || obj.EndDate.Equals("-") ? null : obj.EndDate;
						obj.Details = obj.Organization.NormalizeURLs(obj.Details, false);
						obj.LastModifiedID = string.IsNullOrWhiteSpace(obj.LastModifiedID) ? requestInfo.Session.User.ID : obj.LastModifiedID;
					});
					var existing = await Content.GetContentByAliasAsync(content.RepositoryEntityID, content.Alias, content.CategoryID, cancellationToken).ConfigureAwait(false);
					if (existing != null && !existing.ID.IsEquals(content.ID))
						content.Alias = $"{content.Alias}-{DateTime.Now.ToUnixTimestamp()}-{UtilityService.GetRandomNumber()}";
					await Content.UpdateAsync(content, dontCreateNewVersion, !Utility.IsCacheDisabled, cancellationToken).ConfigureAwait(false);
				}
			}
			else if (content != null)
				await Content.DeleteAsync(content.ID, content.LastModifiedID, cancellationToken).ConfigureAwait(false);

			// stop if has no info
			if (content == null)
				return new JObject();

			// update cache & send notification
			await content.ClearRelatedCacheAsync(cancellationToken, requestInfo.CorrelationID).ConfigureAwait(false);
			if (content.ContentType != null)
			{
				if (@event.IsEquals("Delete"))
					await Utility.Cache.RemoveSetMemberAsync(content.ContentType.ObjectCacheKeys, content.GetCacheKey(), cancellationToken).ConfigureAwait(false);
				else
					await Utility.Cache.AddSetMemberAsync(content.ContentType.ObjectCacheKeys, content.GetCacheKey(), cancellationToken).ConfigureAwait(false);
				if (sendNotifications)
					await content.SendNotificationAsync(@event, content.ContentType.Notifications, oldStatus, content.Status, requestInfo, cancellationToken).ConfigureAwait(false);
			}

			// send update messages
			var response = content.ToJson();
			if (!@event.IsEquals("Delete"))
			{
				var thumbnailsTask = requestInfo.GetThumbnailsAsync(content.ID, content.Title.Url64Encode(), Utility.ValidationKey, cancellationToken);
				var attachmentsTask = requestInfo.GetAttachmentsAsync(content.ID, content.Title.Url64Encode(), Utility.ValidationKey, cancellationToken);
				var versionsTask = content.FindVersionsAsync(cancellationToken, false);
				await Task.WhenAll(thumbnailsTask, attachmentsTask, versionsTask).ConfigureAwait(false);
				response.UpdateVersions(versionsTask.Result);
				response["Thumbnails"] = thumbnailsTask.Result;
				response["Attachments"] = attachmentsTask.Result;
				response["Summary"] = content.Summary?.NormalizeHTMLBreaks();
				response["Details"] = content.Organization.NormalizeURLs(content.Details);
				response["URL"] = content.GetURL();
			}
			new UpdateMessage
			{
				Type = $"{requestInfo.ServiceName}#{content.GetObjectName()}#{@event}",
				Data = response,
				DeviceID = "*"
			}.Send();
			return response;
		}

		internal static async Task<JObject> RollbackContentAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, CancellationToken cancellationToken = default)
		{
			// prepare
			var content = await Content.GetAsync(requestInfo.GetObjectIdentity() ?? "", !Utility.IsCacheDisabled, cancellationToken).ConfigureAwait(false);
			if (content == null)
				throw new InformationNotFoundException();
			else if (content.Organization == null || content.Module == null || content.ContentType == null)
				throw new InformationInvalidException("The organization/module/content-type is invalid");

			// check permission
			var gotRights = isSystemAdministrator || requestInfo.Session.User.IsEditor(content.WorkingPrivileges, content.Category.WorkingPrivileges, content.Organization);
			if (!gotRights)
				gotRights = content.Status.Equals(ApprovalStatus.Draft) || content.Status.Equals(ApprovalStatus.Pending) || content.Status.Equals(ApprovalStatus.Rejected)
					? requestInfo.Session.User.ID.IsEquals(content.CreatedID)
					: requestInfo.Session.User.IsEditor(content.WorkingPrivileges, content.Category.WorkingPrivileges, content.Organization);
			if (!gotRights)
				throw new AccessDeniedException();

			// rollback
			var oldStatus = content.Status;
			content = await Content.RollbackAsync(requestInfo.GetParameter("x-version-id") ?? "", requestInfo.Session.User.ID, cancellationToken).ConfigureAwait(false);

			// update cache & send notification
			Task.WhenAll
			(
				content.ClearRelatedCacheAsync(Utility.CancellationToken, requestInfo.CorrelationID),
				content.SendNotificationAsync("Rollback", content.Category.Notifications, oldStatus, content.Status, requestInfo, Utility.CancellationToken)
			).Execute();

			// send update messages
			var objectName = content.GetObjectName();
			var thumbnailsTask = requestInfo.GetThumbnailsAsync(content.ID, content.Title.Url64Encode(), Utility.ValidationKey, cancellationToken);
			var attachmentsTask = requestInfo.GetAttachmentsAsync(content.ID, content.Title.Url64Encode(), Utility.ValidationKey, cancellationToken);
			var versionsTask = content.FindVersionsAsync(!Utility.IsCacheDisabled, cancellationToken, false);
			await Task.WhenAll(thumbnailsTask, attachmentsTask, versionsTask).ConfigureAwait(false);
			var response = content.ToJson(json =>
			{
				json.UpdateVersions(versionsTask.Result);
				json["Thumbnails"] = thumbnailsTask.Result;
				json["Attachments"] = attachmentsTask.Result;
				json["Summary"] = content.Summary?.NormalizeHTMLBreaks();
				json["Details"] = content.Organization.NormalizeURLs(content.Details);
				json["URL"] = content.GetURL();
			});
			new UpdateMessage
			{
				Type = $"{requestInfo.ServiceName}#{objectName}#Update",
				Data = response,
				DeviceID = "*"
			}.Send();
			return response;
		}

		internal static async Task<JToken> ProcessContentMcpRequestAsync(this RequestInfo requestInfo, ContentType contentType, bool isSystemAdministrator = false, CancellationToken cancellationToken = default)
		{
			var mcpResource = contentType.Organization.McpSettings.Resources.FirstOrDefault(resource => resource.Name.IsEquals(requestInfo.ObjectName));

			var objectIdentity = requestInfo.GetObjectIdentity();
			var mcpTool = mcpResource.Tools?.FirstOrDefault(tool => tool.Name.IsEquals(objectIdentity));

			var expression = await (mcpResource.ExpressionID ?? "").GetExpressionByIDAsync(cancellationToken).ConfigureAwait(false);
			var category = await (mcpResource.CategoryID ?? "").GetCategoryByIDAsync(cancellationToken).ConfigureAwait(false);
			var siteURL = $"{contentType.Organization?.DefaultSite?.GetURL()}/";

			var verb = "SEARCH";
			if (requestInfo.Verb.IsEquals("resources/read"))
				verb = "READ";

			else if (requestInfo.Verb.IsEquals("tools/call"))
			{
				if (mcpTool == null)
					verb = "UNKNOWN";
				else if ("read".IsEquals(mcpTool?.Name))
				{
					verb = "READ";
					objectIdentity = requestInfo.BodyAsJson?.Get<string>("ID");
				}
			}

			JToken response = null;

			if (verb.IsEquals("SEARCH"))
			{
				var gotRights = isSystemAdministrator || requestInfo.Session.User.IsViewer(category?.WorkingPrivileges, contentType?.WorkingPrivileges, contentType.Organization);
				if (!gotRights)
					throw new AccessDeniedException();

				JObject requestJson = null;
				string query = null;
				
				var bodyJson = requestInfo.BodyAsJson as JObject ?? new();
				try
				{
					var cursor = bodyJson.Get<string>("cursor");
					requestJson = string.IsNullOrWhiteSpace(cursor) ? null : cursor.FromBase64Url().ToJSON() as JObject;
				}
				catch { }

				if (requestJson == null)
				{
					query = bodyJson.Get<string>("query");

					var filterBy = new JArray(
						new JObject
						{
							["SystemID"] = new JObject { ["Equals"] = contentType.SystemID }
						},
						new JObject
						{
							["RepositoryID"] = new JObject { ["Equals"] = contentType.RepositoryID }
						},
						new JObject
						{
							["RepositoryEntityID"] = new JObject { ["Equals"] = contentType.ID }
						}
					);

					var categoryID = category?.ID ?? bodyJson.Get<string>("categoryID");
					if (!string.IsNullOrWhiteSpace(categoryID))
						filterBy.Add(new JObject
						{
							["CategoryID"] = new JObject { ["Equals"] = categoryID }
						});

					var from_Date = bodyJson.Get<string>("fromDate");
					var to_Date = bodyJson.Get<string>("toDate");
					if (DateTime.TryParse(from_Date, out var fromDate) && DateTime.TryParse(to_Date, out var toDate))
						filterBy.Add(new JObject
						{
							["And"] = new JArray
							{
								new JObject
								{
									["StartDate"] = new JObject { ["GreaterOrEquals"] = fromDate.ToDTString(false, false) }
								},
								new JObject
								{
									["StartDate"] = new JObject { ["LessThanOrEquals"] = toDate.ToDTString(false, false) }
								}
							}
						});
					else if (DateTime.TryParse(from_Date, out fromDate))
						filterBy.Add(new JObject
						{
							["StartDate"] = new JObject { ["GreaterOrEquals"] = fromDate.ToDTString(false, false) }
						});
					else if (DateTime.TryParse(to_Date, out toDate))
						filterBy.Add(new JObject
						{
							["StartDate"] = new JObject { ["LessThanOrEquals"] = toDate.ToDTString(false, false) }
						});
					else
						filterBy.Add(new JObject
						{
							["StartDate"] = new JObject { ["LessThanOrEquals"] = DateTime.Now.ToDTString(false, false) }
						});

					filterBy.Add(new JObject
					{
						["Or"] = new JArray
						{
							new JObject
							{
								["EndDate"] = "IsNull"
							},
							new JObject
							{
								["EndDate"] = new JObject { ["GreaterOrEquals"] = DateTime.Now.ToDTString(false, false) }
							}
						}
					});

					var status = mcpResource.AllowStatus
						? Enum.TryParse(bodyJson.Get<string>("status"), out ApprovalStatus approvalStatus) ? approvalStatus.ToString() : null
						: ApprovalStatus.Published.ToString();
					if (status != null)
						filterBy.Add(new JObject
						{
							["Status"] = new JObject { ["Equals"] = status }
						});

					requestJson = new JObject
					{
						["FilterBy"] = new JObject
						{
							["Query"] = query,
							["And"] = filterBy
						},
						["SortBy"] = null
					};
				}
				else
					query = requestJson.Get<JObject>("FilterBy")?.Get<string>("Query");

				var pagination = requestJson.Get<JObject>("Pagination");
				var totalRecords = pagination != null ? pagination.Get<long>("TotalRecords") : -1;
				var pageSize = pagination != null ? pagination.Get<int>("PageSize") : Int32.TryParse(bodyJson.Get<string>("pageSize"), out var thePageSize) && thePageSize > 0 ? thePageSize : 20;
				var totalPages = pagination != null ? pagination.Get<int>("TotalPages") : 0;
				var pageNumber = pagination != null ? pagination.Get<int>("PageNumber") + 1 : 1;

				var request = requestJson.ToExpandoObject();
				var filter = expression?.GetFilterBy<Content>() as FilterBys<Content> ?? request.Get<ExpandoObject>("FilterBy")?.ToFilterBy<Content>() as FilterBys<Content> ?? Filters<Content>.And();
				var sort = string.IsNullOrWhiteSpace(query) ? expression?.GetSortBy<Content>() ?? request.Get<ExpandoObject>("SortBy")?.ToSortBy<Content>() ?? Sorts<Content>.Descending("StartDate").ThenByDescending("PublishedTime") : null;
				var result = await new RequestInfo().SearchAsync(query, filter, sort, pageSize, pageNumber, contentType.ID, totalRecords, cancellationToken).ConfigureAwait(false);

				totalPages = (result.TotalRecords, pageSize).GetTotalPages();
				requestJson["Pagination"] = new JObject
				{
					["TotalRecords"] = result.TotalRecords,
					["PageSize"] = pageSize,
					["TotalPages"] = totalPages,
					["PageNumber"] = result.PageNumber
				};

				var lastID = requestJson.Get<string>("LastID");
				var lastIndex = string.IsNullOrWhiteSpace(lastID) ? -1 : result.Objects.FindIndex(@object => @object.ID == lastID);
				if (result.Objects.Count > 0)
					requestJson["LastID"] = result.Objects.Last().ID;

				response = new JObject
				{
					["items"] = result.Objects.Skip(lastIndex + 1).Select(@object => !string.IsNullOrWhiteSpace(mcpTool?.TransformScript)
						? mcpTool.TransformScript.JsEvaluate(@object, requestInfo, new JObject
						{
							["URL"] = @object.GetURL().Replace("~/", siteURL),
							["Category"] = new JObject
							{
								["ID"] = @object.CategoryID,
								["Title"] = @object.Category?.Title,
								["Description"] = @object.Category?.Description,
								["Breadcrumbs"] = @object.Category?.FullTitle,
								["URL"] = @object.Category?.GetURL().Replace("~/", siteURL)
							}
						}.ToExpandoObject())?.ToString().ToJson(json => json["URI"] = $"{Utility.ServiceName.ToLower()}://{mcpResource.Name}/{@object.ID}") as JObject
						: @object.ToJSON(json => json["URI"] = $"{Utility.ServiceName.ToLower()}://{mcpResource.Name}/{@object.ID}")
					).ToJArray(),
					["nextCursor"] = result.PageNumber < totalPages ? requestJson.ToString(Formatting.None).ToBase64Url() : null
				};
			}

			else if (verb.IsEquals("READ"))
			{
				var content = await Content.GetAsync(objectIdentity, cancellationToken).ConfigureAwait(false);
				var privileges = content?.WorkingPrivileges;
				var parentPrivileges = content?.ContentType?.WorkingPrivileges ?? contentType.WorkingPrivileges;

				var gotRights = isSystemAdministrator || requestInfo.Session.User.ID.IsEquals(content?.Organization.OwnerID);
				if (!gotRights)
					gotRights = content != null && content.Status.Equals(ApprovalStatus.Published)
						? requestInfo.Session.User.IsViewer(privileges, parentPrivileges, content?.Organization)
						: requestInfo.Session.User.ID.IsEquals(content?.CreatedID) || requestInfo.Session.User.IsEditor(privileges, parentPrivileges, content.Organization);
				if (!gotRights)
					throw new AccessDeniedException();

				response = !string.IsNullOrWhiteSpace(mcpTool?.TransformScript)
					? mcpTool.TransformScript.JsEvaluate(content, requestInfo, new JObject
					{
						["Details"] = content?.Details?.Replace("~~/", (contentType.Organization?.FakeFilesHttpURI ?? Utility.FilesHttpURI) + "/").Replace("~/", siteURL),
						["URL"] = content?.GetURL().Replace("~/", siteURL),
						["Category"] = new JObject
						{
							["ID"] = content?.CategoryID,
							["Title"] = content?.Category?.Title,
							["Description"] = content?.Category?.Description,
							["Breadcrumbs"] = content?.Category?.FullTitle,
							["URL"] = content?.Category?.GetURL().Replace("~/", siteURL)
						}
					}.ToExpandoObject())?.ToString().ToJson(json => json["URI"] = $"{Utility.ServiceName.ToLower()}://{mcpResource.Name}/{content.ID}") as JObject
					: content?.ToJSON(json => json["URI"] = $"{Utility.ServiceName.ToLower()}://{mcpResource.Name}/{content.ID}");
			}

			return response;
		}

		internal static JObject ToJSON(this Content content, Action<JObject> onCompleted = null)
			=> content?.ToJson(json => json.Remove(["CategoryID", "OtherCategories", "StartDate", "EndDate", "Relateds", "ExternalRelateds", "Alias", "Tags", "AllowComments", "InlineScripts", "CreatedID", "LastModifiedID", "SystemID", "RepositoryID", "RepositoryEntityID", "Privileges"], _ =>
			{
				var siteURL = $"{content.Organization?.DefaultSite?.GetURL()}/";
				json["Details"] = content.Details?.Replace("~~/", (content.Organization?.FakeFilesHttpURI ?? Utility.FilesHttpURI) + "/").Replace("~/", siteURL);
				json["Created"] = content.Created.ToIsoString();
				json["LastModified"] = content.LastModified.ToIsoString();
				json["URL"] = content.GetURL().Replace("~/", siteURL);
				json["Category"] = new JObject
				{
					["ID"] = content.CategoryID,
					["Title"] = content.Category?.Title,
					["Description"] = content.Category?.Description,
					["Breadcrumbs"] = content.Category?.FullTitle,
					["URL"] = content.Category?.GetURL().Replace("~/", siteURL)
				};
				onCompleted?.Invoke(json);
			}));
	}
}
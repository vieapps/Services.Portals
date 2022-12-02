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
		public static Content CreateContent(this ExpandoObject data, string excluded = null, Action<Content> onCompleted = null)
			=> Content.CreateInstance(data, excluded?.ToHashSet(), content =>
			{
				content.NormalizeHTMLs();
				content.Alias = (string.IsNullOrWhiteSpace(content.Alias) ? content.Title : content.Alias).NormalizeAlias();
				content.Tags = content.Tags?.Replace(";", ",").ToList(",", true).Where(tag => !string.IsNullOrWhiteSpace(tag)).Join(",");
				content.Tags = string.IsNullOrWhiteSpace(content.Tags) ? null : content.Tags;
				onCompleted?.Invoke(content);
			});

		public static Content Update(this Content content, ExpandoObject data, string excluded = null, Action<Content> onCompleted = null)
			=> content.Fill(data, excluded?.ToHashSet(), _ =>
			{
				content.NormalizeHTMLs();
				content.Alias = (string.IsNullOrWhiteSpace(content.Alias) ? content.Title : content.Alias).NormalizeAlias();
				content.Tags = content.Tags?.Replace(";", ",").ToList(",", true).Where(tag => !string.IsNullOrWhiteSpace(tag)).Join(",");
				content.Tags = string.IsNullOrWhiteSpace(content.Tags) ? null : content.Tags;
				onCompleted?.Invoke(content);
			});

		internal static string GetCacheKeyOfAliasedContent(this string contentTypeID, string categoryID, string alias)
			=> !string.IsNullOrWhiteSpace(contentTypeID) && !string.IsNullOrWhiteSpace(categoryID) && !string.IsNullOrWhiteSpace(alias)
				? $"e:{contentTypeID}#c:{categoryID}#a:{alias.NormalizeAlias().GenerateUUID()}".GetCacheKey<Content>()
				: null;

		internal static string GetCacheKeyOfAliasedContent(this ContentType contentType, Category category, string alias)
			=> contentType?.ID?.GetCacheKeyOfAliasedContent(category?.ID, alias);

		internal static string GetCacheKeyOfAliasedContent(this Content content)
			=> content?.ContentType?.GetCacheKeyOfAliasedContent(content?.Category, content?.Alias);

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

		public static IFilterBy<Content> GetContentByAliasFilter(this ContentType contentType, Category category, string alias)
			=> Filters<Content>.And
			(
				Filters<Content>.Equals("RepositoryEntityID", contentType.ID),
				Filters<Content>.Equals("CategoryID", category.ID),
				Filters<Content>.Equals("Alias", alias.NormalizeAlias())
			);

		static async Task<Content> RefreshAsync(this Content content, CancellationToken cancellationToken = default, string correlationID = null, string message = null, bool fetchRepository = false, bool reloadWebpages = true)
		{
			if (fetchRepository)
			{
				await Utility.Cache.RemoveAsync(content, cancellationToken).ConfigureAwait(false);
				content = await Content.GetAsync<Content>(content.ID, cancellationToken).ConfigureAwait(false);
			}

			if (reloadWebpages)
			{
				message = (message ?? "Refresh a CMS content") + $" [{content.Title} - ID: {content.ID}]";
				await Task.WhenAll
				(
					content.Status.Equals(ApprovalStatus.Published) ? $"{content.GetURL()}?x-force-cache=x".Replace("~/", $"{content.Organization?.URL}/").RefreshWebPageAsync(1, correlationID, message) : Task.CompletedTask,
					content.Category != null ? $"{content.Category.GetURL()}?x-force-cache=x".Replace("~/", $"{content.Organization?.URL}/").RefreshWebPageAsync(1, correlationID, message) : Task.CompletedTask,
					(content.OtherCategories ?? new List<string>()).Select(id => id.GetCategoryByID()).Where(category => category != null).ForEachAsync(category => $"{category.GetURL()}?x-force-cache=x".Replace("~/", $"{content.Organization?.URL}/").RefreshWebPageAsync(1, correlationID, message))
				).ConfigureAwait(false);
			}

			return content;
		}

		internal static async Task ClearRelatedCacheAsync(this Content content, CancellationToken cancellationToken = default, string correlationID = null, bool clearDataCache = true, bool clearHtmlCache = true, bool doRefresh = true)
		{
			// tasks for updating sets
			var setTasks = new List<Task>();

			// data cache keys
			var dataCacheKeys = clearDataCache && content != null
				? Extensions.GetRelatedCacheKeys(content.GetCacheKey()).Concat(new[] { content.GetCacheKeyOfAliasedContent() }).Where(key => key != null).ToList()
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
				htmlCacheKeys = content?.Organization?.GetDesktopCacheKey() ?? new List<string>();
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
			await Task.WhenAll
			(
				Task.WhenAll(setTasks),
				Utility.Cache.RemoveAsync(htmlCacheKeys.Concat(dataCacheKeys).Distinct(StringComparer.OrdinalIgnoreCase).ToList(), cancellationToken),
				Utility.IsCacheLogEnabled && content != null ? Utility.WriteLogAsync(correlationID, $"Clear related cache of a CMS content [{content.Title} - ID: {content.ID}]\r\n- {dataCacheKeys.Count} data keys => {dataCacheKeys.Join(", ")}\r\n- {htmlCacheKeys.Count} html keys => {htmlCacheKeys.Join(", ")}", "Caches") : Task.CompletedTask,
				doRefresh && content != null
					? Task.WhenAll
					(
						content.RefreshAsync(cancellationToken, correlationID, "Refresh when related cache of a CMS content was clean"),
						$"{content.Organization?.URL}?x-force-cache=x".RefreshWebPageAsync(1, correlationID, $"Refresh when related cache of a CMS content was clean [{content.Title} - ID: {content.ID}]")
					) : Task.CompletedTask
				).ConfigureAwait(false);
		}

		internal static async Task<Tuple<List<Content>, long, int, JToken, List<string>>> SearchAsync(this RequestInfo requestInfo, string query, IFilterBy<Content> filter, SortBy<Content> sort, int pageSize, int pageNumber, string contentTypeID = null, long totalRecords = -1, CancellationToken cancellationToken = default, bool searchThumbnails = true, bool randomPage = false, int minRandomPage = 0, int maxRandomPage = 0, int cacheTime = 0)
		{
			// cache keys
			var cacheKeyOfObjects = string.IsNullOrWhiteSpace(query) ? Extensions.GetCacheKey(filter, sort, pageSize, pageNumber) : null;
			var cacheKeyOfTotalObjects = string.IsNullOrWhiteSpace(query) ? Extensions.GetCacheKeyOfTotalObjects(filter, sort) : null;
			var cacheKeys = string.IsNullOrWhiteSpace(query) ? new List<string> { cacheKeyOfObjects, cacheKeyOfTotalObjects } : new List<string>();

			// count
			totalRecords = totalRecords > -1
				? totalRecords
				: string.IsNullOrWhiteSpace(query)
					? await Content.CountAsync(filter, contentTypeID, true, cacheKeyOfTotalObjects, cacheTime, cancellationToken).ConfigureAwait(false)
					: await Content.CountAsync(query, filter, contentTypeID, cancellationToken).ConfigureAwait(false);

			// page number
			if (randomPage)
			{
				var totalPages = new Tuple<long, int>(totalRecords, pageSize).GetTotalPages();
				minRandomPage = minRandomPage > 0 && minRandomPage <= totalPages ? minRandomPage : 1;
				maxRandomPage = maxRandomPage > 0 && maxRandomPage <= totalPages ? maxRandomPage : totalPages;
				pageNumber = UtilityService.GetRandomNumber(minRandomPage, maxRandomPage);
			}

			// search objects
			var objects = totalRecords > 0
				? string.IsNullOrWhiteSpace(query)
					? await Content.FindAsync(filter, sort, pageSize, pageNumber, contentTypeID, true, cacheKeyOfObjects, cacheTime, cancellationToken).ConfigureAwait(false)
					: await Content.SearchAsync(query, filter, null, pageSize, pageNumber, contentTypeID, cancellationToken).ConfigureAwait(false)
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

			// store page size to clear related cached
			if (string.IsNullOrWhiteSpace(query))
				await Utility.SetCacheOfPageSizeAsync(filter, sort, pageSize, cancellationToken).ConfigureAwait(false);

			// store object identities to clear related cached
			var contentType = objects.FirstOrDefault()?.ContentType;
			if (contentType != null)
				await Utility.Cache.AddSetMembersAsync(contentType.ObjectCacheKeys, objects.Select(@object => @object.GetCacheKey()), cancellationToken).ConfigureAwait(false);

			// return the results
			return new Tuple<List<Content>, long, int, JToken, List<string>>(objects, totalRecords, pageNumber, thumbnails, cacheKeys);
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
			var gotRights = isSystemAdministrator || requestInfo.Session.User.IsViewer(category?.WorkingPrivileges, contentType.WorkingPrivileges, organization);
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

			// other parameters
			var showAttachments = "true".IsEquals(requestInfo.GetParameter("x-object-attachments")) || requestInfo.GetParameter("ShowAttachments") != null;
			var showURLs = "true".IsEquals(requestInfo.GetParameter("x-object-urls")) || requestInfo.GetParameter("ShowURLs") != null;
			var showCategories = "true".IsEquals(requestInfo.GetParameter("x-object-categories")) || requestInfo.GetParameter("ShowCategories") != null;
			var showDetails = "false".IsEquals(requestInfo.GetParameter("x-object-details")) || requestInfo.GetParameter("NoDetails") != null ? false : true;

			// process cache
			var suffix = string.IsNullOrWhiteSpace(query) ? (showAttachments ? ":a" : "") + (showURLs ? ":u" : "") + (showCategories ? ":c" : "") + (showDetails ? "" : ":d") : null;
			var cacheKeyOfObjectsJson = string.IsNullOrWhiteSpace(query) ? Extensions.GetCacheKeyOfObjectsJson(filter, sort, pageSize, pageNumber, string.IsNullOrWhiteSpace(suffix) ? null : suffix) : null;
			if (requestInfo.GetParameter("x-force-cache") != null || requestInfo.GetParameter("x-no-cache") != null)
				await Utility.Cache.RemoveAsync(new[] { cacheKeyOfObjectsJson, Extensions.GetCacheKey(filter, sort, pageSize, pageNumber), Extensions.GetCacheKeyOfTotalObjects(filter, sort) }, cancellationToken).ConfigureAwait(false);
			else if (cacheKeyOfObjectsJson != null)
			{
				var json = await Utility.Cache.GetAsync<string>(cacheKeyOfObjectsJson, cancellationToken).ConfigureAwait(false);
				if (!string.IsNullOrWhiteSpace(json))
					return JObject.Parse(json);
			}

			// search if has no cache
			var results = await requestInfo.SearchAsync(query, filter, sort, pageSize, pageNumber, contentType.ID, pagination.Item1 > -1 ? pagination.Item1 : -1, cancellationToken).ConfigureAwait(false);
			var objects = results.Item1;
			var totalRecords = results.Item2;
			var thumbnails = results.Item4;

			JToken attachments = null;
			if (objects.Count > 0 && showAttachments)
				attachments = objects.Count == 1
					? await requestInfo.GetAttachmentsAsync(objects[0].ID, objects[0].Title.Url64Encode(), Utility.ValidationKey, cancellationToken).ConfigureAwait(false)
					: await requestInfo.GetAttachmentsAsync(objects.Select(@object => @object.ID).Join(","), objects.ToJObject("ID", @object => new JValue(@object.Title.Url64Encode())).ToString(Formatting.None), Utility.ValidationKey, cancellationToken).ConfigureAwait(false);

			// build response
			var totalPages = new Tuple<long, int>(totalRecords, pageSize).GetTotalPages();
			if (totalPages > 0 && pageNumber > totalPages)
				pageNumber = totalPages;
			pagination = new Tuple<long, int, int, int>(totalRecords, totalPages, pageSize, pageNumber);

			var siteURL = organization.DefaultSite?.GetURL(requestInfo.GetHeaderParameter("x-srp-host"), requestInfo.GetParameter("x-url")) + "/";
			var response = new JObject
			{
				{ "FilterBy", filter.ToClientJson(query) },
				{ "SortBy", sort?.ToClientJson() },
				{ "Pagination", pagination.GetPagination() },
				{ "Objects", objects.Select(@object => @object.ToJson(false, json =>
					{
						json["Thumbnails"] = thumbnails?.GetThumbnails(@object.ID)?.NormalizeURIs(organization.FakeFilesHttpURI);
						if (showAttachments)
							json["Attachments"] = (attachments == null ? null : objects.Count == 1 ? attachments : attachments[@object.ID])?.NormalizeURIs(organization.FakeFilesHttpURI);

						if (showURLs)
						{
							json["URL"] = organization.NormalizeURLs(@object.GetURL(), true, siteURL);
							json["Summary"] = @object.Summary?.NormalizeHTMLBreaks();
						}

						if (showCategories)
							json["Category"] = new JObject
							{
								["Title"] = @object.Category.Title,
								["URL"] = organization.NormalizeURLs(@object.Category.GetURL(), true, siteURL)
							};

						if (showDetails)
							json["Details"] = organization.NormalizeURLs(@object.Details);
						else
							json.Remove("Details");

						if (showURLs || showCategories || !showDetails)
							new[] { "Privileges", "OriginalPrivileges", "Relateds", "ExternalRelateds", "OtherCategories", "InlineScripts", "SystemID", "RepositoryID", "RepositoryEntityID" }.ForEach(name => json.Remove(name));
					})).ToJArray()
				}
			};

			// update cache
			if (string.IsNullOrWhiteSpace(query))
			{
				var cacheKeys = new[] { cacheKeyOfObjectsJson }.Concat(results.Item5).ToList();
				await Task.WhenAll
				(
					Utility.Cache.SetAsync(cacheKeyOfObjectsJson, response.ToString(Formatting.None), cancellationToken),
					Utility.Cache.AddSetMembersAsync(contentType.GetSetCacheKey(), cacheKeys, cancellationToken),
					Utility.IsCacheLogEnabled ? Utility.WriteLogAsync(requestInfo, $"Update cache when search CMS contents\r\n- Cache key of JSON: {cacheKeyOfObjectsJson}\r\n- Cache key of Content-Type's set: {contentType.GetSetCacheKey()}\r\n- Related cache keys: {cacheKeys.Join(", ")}", "Caches") : Task.CompletedTask
				).ConfigureAwait(false);
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
			var gotRights = isSystemAdministrator || requestInfo.Session.User.IsContributor(category.WorkingPrivileges, contentType.WorkingPrivileges, organization);
			if (!gotRights)
				throw new AccessDeniedException();

			// get data
			var content = request.CreateContent("Privileges,Created,CreatedID,LastModified,LastModifiedID", obj =>
			{
				obj.ID = string.IsNullOrWhiteSpace(obj.ID) || !obj.ID.IsValidUUID() ? UtilityService.NewUUID : obj.ID;
				obj.SystemID = organization.ID;
				obj.RepositoryID = module.ID;
				obj.RepositoryEntityID = contentType.ID;
				obj.Created = obj.LastModified = DateTime.Now;
				obj.CreatedID = obj.LastModifiedID = requestInfo.Session.User.ID;
			});

			var existing = await Content.GetContentByAliasAsync(contentType, content.Alias, category.ID, cancellationToken).ConfigureAwait(false);
			if (existing != null)
				content.Alias = $"{content.Alias}-{DateTime.Now.ToUnixTimestamp()}-{UtilityService.GetRandomNumber()}";

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
			await Utility.Cache.SetAsync(content.GetCacheKeyOfAliasedContent(), content.ID, cancellationToken).ConfigureAwait(false);

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
			new UpdateMessage
			{
				Type = $"{requestInfo.ServiceName}#{content.GetObjectName()}#Create",
				DeviceID = "*",
				Data = response
			}.Send();

			// clear related cache & send notification
			Task.WhenAll
			(
				content.ClearRelatedCacheAsync(Utility.CancellationToken, requestInfo.CorrelationID),
				content.SendNotificationAsync("Create", content.Category.Notifications, ApprovalStatus.Draft, content.Status, requestInfo, Utility.CancellationToken),
				Utility.Cache.AddSetMemberAsync(content.ContentType.ObjectCacheKeys, content.GetCacheKey(), Utility.CancellationToken)
			).Run();

			// response
			return response;
		}

		internal static async Task<JObject> GetContentAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, CancellationToken cancellationToken = default)
		{
			// prepare
			var identity = requestInfo.GetObjectIdentity(true, true) ?? "";
			var content = identity.IsValidUUID()
				? await Content.GetAsync<Content>(identity, cancellationToken).ConfigureAwait(false)
				: await Content.GetContentByAliasAsync(requestInfo.GetParameter("RepositoryEntityID") ?? requestInfo.GetParameter("x-content-type-id"), identity, requestInfo.GetParameter("Category") ?? requestInfo.GetParameter("x-category-id"), cancellationToken).ConfigureAwait(false);
			if (content == null)
				throw new InformationNotFoundException();
			else if (content.Organization == null || content.Module == null || content.ContentType == null)
				throw new InformationInvalidException("The organization/module/content-type is invalid");

			// check permission
			var gotRights = isSystemAdministrator || requestInfo.Session.User.ID.IsEquals(content.Organization.OwnerID);
			if (!gotRights)
				gotRights = content.Status.Equals(ApprovalStatus.Published)
					? requestInfo.Session.User.IsViewer(content.WorkingPrivileges, content.ContentType?.WorkingPrivileges, content.Organization)
					: requestInfo.Session.User.ID.IsEquals(content.CreatedID) || requestInfo.Session.User.IsEditor(content.WorkingPrivileges, content.ContentType?.WorkingPrivileges, content.Organization);
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
			var isRefresh = "refresh".IsEquals(requestInfo.GetObjectIdentity());
			if (isRefresh)
				await Task.WhenAll
				(
					Utility.IsDebugLogEnabled ? requestInfo.WriteLogAsync($"Refresh the CMS content by a request [{content.Title} - ID: {content.ID}]", "Caches") : Task.CompletedTask,
					content.RefreshAsync(cancellationToken, requestInfo.CorrelationID, "Refresh the CMS content by a request", true, false)
				).ConfigureAwait(false);

			// store object cache key to clear related cached
			await Utility.Cache.AddSetMemberAsync(content.ContentType.ObjectCacheKeys, content.GetCacheKey(), cancellationToken).ConfigureAwait(false);

			// send update message and return
			var objectName = content.GetObjectName();
			var thumbnailsTask = requestInfo.GetThumbnailsAsync(content.ID, content.Title.Url64Encode(), Utility.ValidationKey, cancellationToken);
			var attachmentsTask = requestInfo.GetAttachmentsAsync(content.ID, content.Title.Url64Encode(), Utility.ValidationKey, cancellationToken);
			await Task.WhenAll(thumbnailsTask, attachmentsTask).ConfigureAwait(false);
			var versions = await content.FindVersionsAsync(cancellationToken, false).ConfigureAwait(false);
			var response = content.ToJson(json =>
			{
				json.UpdateVersions(versions);
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

		internal static async Task<JObject> UpdateAsync(this Content content, RequestInfo requestInfo, ApprovalStatus oldStatus, CancellationToken cancellationToken)
		{
			if (content.Status.Equals(ApprovalStatus.Published) && content.PublishedTime == null)
				content.PublishedTime = DateTime.Now;

			// update
			await Content.UpdateAsync(content, requestInfo.Session.User.ID, cancellationToken).ConfigureAwait(false);
			await Utility.Cache.SetAsync(content.GetCacheKeyOfAliasedContent(), content.ID, cancellationToken).ConfigureAwait(false);

			// update cache & send notification
			Task.WhenAll
			(
				content.ClearRelatedCacheAsync(Utility.CancellationToken, requestInfo.CorrelationID),
				content.SendNotificationAsync("Update", content.Category.Notifications, oldStatus, content.Status, requestInfo, Utility.CancellationToken),
				Utility.Cache.AddSetMemberAsync(content.ContentType.ObjectCacheKeys, content.GetCacheKey(), Utility.CancellationToken)
			).Run();

			// send update message
			var thumbnailsTask = requestInfo.GetThumbnailsAsync(content.ID, content.Title.Url64Encode(), Utility.ValidationKey, cancellationToken);
			var attachmentsTask = requestInfo.GetAttachmentsAsync(content.ID, content.Title.Url64Encode(), Utility.ValidationKey, cancellationToken);
			var versionsTask = content.FindVersionsAsync(cancellationToken, false);
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
			var gotRights = isSystemAdministrator || requestInfo.Session.User.IsEditor(content.WorkingPrivileges, content.ContentType.WorkingPrivileges, content.Organization);
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

			content.Update(request, "ID,SystemID,RepositoryID,RepositoryEntityID,Privileges,Created,CreatedID,LastModified,LastModifiedID", obj =>
			{
				obj.LastModified = DateTime.Now;
				obj.LastModifiedID = requestInfo.Session.User.ID;
			});

			var existing = await Content.GetContentByAliasAsync(content.RepositoryEntityID, content.Alias, content.CategoryID, cancellationToken).ConfigureAwait(false);
			if (existing != null && !existing.ID.IsEquals(content.ID))
				content.Alias = $"{content.Alias}-{DateTime.Now.ToUnixTimestamp()}-{UtilityService.GetRandomNumber()}";

			var dateString = request.Get<string>("StartDate");
			content.StartDate = !string.IsNullOrWhiteSpace(dateString) && DateTime.TryParse(dateString, out var date)
				? date.ToDTString(false, false)
				: DateTime.Now.ToDTString(false, false);

			content.EndDate = null;
			dateString = request.Get<string>("EndDate");
			if (!string.IsNullOrWhiteSpace(dateString) && DateTime.TryParse(dateString, out date))
				content.EndDate = date.ToDTString(false, false);

			dateString = request.Get<string>("PublishedTime");
			if (!string.IsNullOrWhiteSpace(dateString) && DateTime.TryParse(dateString, out date))
				content.PublishedTime = date;

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
			return await content.UpdateAsync(requestInfo, oldStatus, cancellationToken).ConfigureAwait(false);
		}

		internal static async Task<JObject> DeleteContentAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, CancellationToken cancellationToken = default)
		{
			// prepare
			var identity = requestInfo.GetObjectIdentity() ?? "";
			var content = await Content.GetAsync<Content>(identity, cancellationToken).ConfigureAwait(false);
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

			// delete files
			await requestInfo.DeleteFilesAsync(content.SystemID, content.RepositoryEntityID, content.ID, Utility.ValidationKey, cancellationToken).ConfigureAwait(false);

			// delete content
			await Content.DeleteAsync<Content>(content.ID, requestInfo.Session.User.ID, cancellationToken).ConfigureAwait(false);

			// send update message
			var response = content.ToJson();
			new UpdateMessage
			{
				Type = $"{requestInfo.ServiceName}#{content.GetObjectName()}#Delete",
				DeviceID = "*",
				Data = response
			}.Send();

			// clear cache and send notification
			Task.WhenAll
			(
				Utility.Cache.RemoveSetMemberAsync(content.ContentType.ObjectCacheKeys, content.GetCacheKey(), Utility.CancellationToken),
				content.ClearRelatedCacheAsync(Utility.CancellationToken, requestInfo.CorrelationID, true, true, false),
				content.SendNotificationAsync("Delete", content.Category.Notifications, content.Status, content.Status, requestInfo, Utility.CancellationToken)
			).Run();

			// response
			return response;
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
			var category = await parentContentTypeJson.Get("ID", "").GetCategoryByAliasAsync(parentIdentity, cancellationToken).ConfigureAwait(false);

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
			desktop = !string.IsNullOrWhiteSpace(desktop) ? desktop : desktopsJson.Get<string>("Default");

			JArray breadcrumbs = null, metaTags = null;
			JObject pagination = null, seoInfo = null, filterBy = null, sortBy = null;
			string coverURI = null, seoTitle = null, seoDescription = null, seoKeywords = null, data = null, ids = null;
			DateTime? expiresAt = null;

			var showThumbnails = options.Get("ShowThumbnails", options.Get("ShowThumbnail", true)) || options.Get("ShowPngThumbnails", false) || options.Get("ShowAsPngThumbnails", false) || options.Get("ShowBigThumbnails", false) || options.Get("ShowAsBigThumbnails", false);
			var pngThumbnails = options.Get("ThumbnailsAsPng", options.Get("ThumbnailAsPng", options.Get("ShowPngThumbnails", options.Get("ShowAsPngThumbnails", false))));
			var bigThumbnails = options.Get("ThumbnailsAsBig", options.Get("ThumbnailAsBig", options.Get("ShowBigThumbnails", options.Get("ShowAsBigThumbnails", false))));
			var thumbnailsWidth = options.Get("ThumbnailsWidth", options.Get("ThumbnailWidth", 0));
			var thumbnailsHeight = options.Get("ThumbnailsHeight", options.Get("ThumbnailHeight", 0));

			var showBreadcrumbs = options.Get("ShowBreadcrumbs", false);
			var showPagination = options.Get("ShowPagination", false);
			var forceCache = requestInfo.GetParameter("x-force-cache") != null || requestInfo.GetParameter("x-no-cache") != null;

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
					await Utility.Cache.RemoveAsync(new[] { cacheKeyOfTotalObjects, cacheKeyOfObjectsXml, Extensions.GetCacheKey(filter, sort, pageSize, pageNumber) }, cancellationToken).ConfigureAwait(false);

				// get cache
				long totalRecords = -1;
				if (randomPage && await Utility.Cache.ExistsAsync(cacheKeyOfTotalObjects, cancellationToken).ConfigureAwait(false))
				{
					totalRecords = await Utility.Cache.GetAsync<long>(cacheKeyOfTotalObjects, cancellationToken).ConfigureAwait(false);
					var totalPages = new Tuple<long, int>(totalRecords, pageSize).GetTotalPages();
					minRandomPage = minRandomPage > 0 && minRandomPage <= totalPages ? minRandomPage : 1;
					maxRandomPage = maxRandomPage > 0 && maxRandomPage <= totalPages ? maxRandomPage : totalPages;
					pageNumber = UtilityService.GetRandomNumber(minRandomPage, maxRandomPage);
				}

				JToken categoryThumbnails = null;
				data = forceCache ? null : await Utility.Cache.GetAsync<string>(cacheKeyOfObjectsXml, cancellationToken).ConfigureAwait(false);

				// process if has no cache
				if (string.IsNullOrWhiteSpace(data))
				{
					// search
					var results = await requestInfo.SearchAsync(null, filter, sort, pageSize, pageNumber, contentTypeID, -1, cancellationToken, true, randomPage, minRandomPage, maxRandomPage).ConfigureAwait(false);
					var objects = results.Item1;
					totalRecords = results.Item2;
					pageNumber = results.Item3;
					var thumbnails = results.Item4;

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
								exception = requestInfo.GetRuntimeException(ex, null, async (msg, exc) => await requestInfo.WriteErrorAsync(exc, $"Error occurred while generating a content => {msg} : {@object.ToJson()}", "Errors").ConfigureAwait(false));
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
						var thumbnailURL = categoryThumbnails?.GetThumbnailURL(category.ID, pngThumbnails, bigThumbnails, thumbnailsWidth, thumbnailsHeight) ?? "";
						dataXml.Add(new XElement(
							"Parent",
							new XElement("Title", category.Title),
							new XElement("Description", category.Description?.NormalizeHTMLBreaks()),
							new XElement("Notes", category.Notes?.NormalizeHTMLBreaks()),
							new XElement("URL", category.GetURL(desktop)),
							new XElement("ThumbnailURL", thumbnailURL, new XAttribute("Alternative", thumbnailURL?.GetWebpImageURL(pngThumbnails) ?? "")),
							new XElement("Root", parentCategory?.Title ?? category.Title, new XAttribute("URL", parentCategory?.GetURL(desktop) ?? category.GetURL(desktop)))
						));
					}

					// get data
					data = dataXml.CleanInvalidCharacters().ToString(SaveOptions.DisableFormatting);

					// update cache
					await Task.WhenAll
					(
						expiresAt != null
							? expiresAt.Value < DateTime.Now
								? Utility.Cache.RemoveAsync(results.Item5.Concat(new[] { cacheKeyOfObjectsXml }), cancellationToken)
								: Utility.Cache.SetAsync(cacheKeyOfObjectsXml, data, expiresAt.Value, cancellationToken)
							: Utility.Cache.SetAsync(cacheKeyOfObjectsXml, data, cancellationToken),
						contentType != null
							? Utility.Cache.AddSetMembersAsync(contentType.GetSetCacheKey(), results.Item5.Concat(new[] { cacheKeyOfObjectsXml }), cancellationToken)
							: Task.CompletedTask,
						Utility.IsCacheLogEnabled
							? Utility.WriteLogAsync(requestInfo, $"Update related keys into Content-Type's set when generate collection of CMS.Content [{contentType?.Title} - ID: {contentType?.ID} - Set: {contentType?.GetSetCacheKey()}]\r\n- Related cache keys ({results.Item5.Count + 1}): {results.Item5.Concat(new[] { cacheKeyOfObjectsXml }).Join(", ")}", "Caches")
							: Task.CompletedTask
					).ConfigureAwait(false);

					// preload
					if (Utility.Preload && objects.Count > 0)
						await objects.PreloadAsync(cancellationToken, requestInfo.CorrelationID).ConfigureAwait(false);
				}
				else if (showPagination)
				{
					totalRecords = totalRecords < 0 ? await Utility.Cache.GetAsync<long>(cacheKeyOfTotalObjects, cancellationToken).ConfigureAwait(false) : totalRecords;
					if (totalRecords < 1)
					{
						await Utility.Cache.RemoveAsync(cacheKeyOfTotalObjects, cancellationToken).ConfigureAwait(false);
						totalRecords = await Content.CountAsync(filter, contentTypeID, true, cacheKeyOfTotalObjects, 0, cancellationToken).ConfigureAwait(false);
						if (contentType != null)
							await Utility.Cache.AddSetMemberAsync(contentType.GetSetCacheKey(), cacheKeyOfTotalObjects, cancellationToken).ConfigureAwait(false);
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
				// process cache
				var contentAlias = requestJson.Get<string>("ContentIdentity");
				if (forceCache)
				{
					var cacheKeyOfAlias = contentTypeID.GetCacheKeyOfAliasedContent(category?.ID, contentAlias);
					var contentIdentity = string.IsNullOrWhiteSpace(cacheKeyOfAlias) ? null : await Utility.Cache.GetAsync<string>(cacheKeyOfAlias, cancellationToken).ConfigureAwait(false);
					await Utility.Cache.RemoveAsync(new[] { cacheKeyOfAlias, contentIdentity?.GetCacheKey<Content>() }, cancellationToken).ConfigureAwait(false);
				}

				// get the requested object
				var @object = await Content.GetContentByAliasAsync(contentTypeID, contentAlias, category?.ID, cancellationToken).ConfigureAwait(false);
				if (@object == null)
					throw new InformationNotFoundException();
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

				// get cache
				Task<JToken> thumbnailsTask = null;
				var cacheKey = $"{@object.GetCacheKey()}:xml:o#{optionsJson.ToString(Formatting.None).GenerateUUID()}:p#{paginationJson.ToString(Formatting.None).GenerateUUID()}";
				data = forceCache ? null : await Utility.Cache.GetAsync<string>(cacheKey, cancellationToken).ConfigureAwait(false);

				// process if has no cache
				if (string.IsNullOrWhiteSpace(data))
				{
					var showAttachments = options.Get("ShowAttachments", false);
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
					thumbnailsTask = showThumbnails ? requestInfo.GetThumbnailsAsync(@object.ID, @object.Title.Url64Encode(), Utility.ValidationKey, cancellationToken) : Task.FromResult<JToken>(new JArray());
					var attachmentsTask = showAttachments ? requestInfo.GetAttachmentsAsync(@object.ID, @object.Title.Url64Encode(), Utility.ValidationKey, cancellationToken) : Task.FromResult<JToken>(new JArray());

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
						relateds.OrderByDescending(related => related.StartDate).ThenByDescending(related => related.PublishedTime).ForEach(related =>
						{
							var relatedXml = new XElement("Content", new XElement("ID", related.ID));
							relatedXml.Add(new XElement("Title", related.Title), new XElement("Author", related.Author ?? ""), new XElement("Summary", related.Summary?.NormalizeHTMLBreaks() ?? ""));
							relatedXml.Add(new XElement("PublishedTime", related.PublishedTime != null ? related.PublishedTime.Value : DateTime.Now).UpdateDateTime(cultureInfo, customDateTimeFormat));
							relatedXml.Add(new XElement("Category", related.Category?.Title ?? "", new XAttribute("URL", related.Category?.GetURL(desktop) ?? "")));
							relatedXml.Add(new XElement("URL", related.GetURL(desktop) ?? ""));
							var thumbnailURL = relatedThumbnails?.GetThumbnailURL(related.ID, pngThumbnails, bigThumbnails, thumbnailsWidth, thumbnailsHeight);
							relatedXml.Add(new XElement("ThumbnailURL", thumbnailURL, new XAttribute("Alternative", thumbnailURL?.GetWebpImageURL(pngThumbnails) ?? "")));
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
							var thumbnailURL = otherThumbnails?.GetThumbnailURL(other.ID, pngThumbnails, bigThumbnails, thumbnailsWidth, thumbnailsHeight);
							otherXml.Add(new XElement("ThumbnailURL", thumbnailURL, new XAttribute("Alternative", thumbnailURL?.GetWebpImageURL(pngThumbnails) ?? "")));
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
						var thumbnailURL = categoryThumbnails?.GetThumbnailURL(category.ID, pngThumbnails, bigThumbnails, thumbnailsWidth, thumbnailsHeight);
						dataXml.Add(new XElement(
							"Parent",
							new XElement("Title", category.Title),
							new XElement("Description", category.Description?.NormalizeHTMLBreaks() ?? ""),
							new XElement("Notes", category.Notes?.NormalizeHTMLBreaks() ?? ""),
							new XElement("URL", category.GetURL(desktop) ?? ""),
							new XElement("ThumbnailURL", thumbnailURL ?? "", new XAttribute("Alternative", thumbnailURL?.GetWebpImageURL(pngThumbnails) ?? "")),
							new XElement("Root", parentCategory?.Title ?? category.Title, new XAttribute("URL", parentCategory?.GetURL(desktop) ?? category.GetURL(desktop)))
						));
					}

					// validate and get data of xml
					data = dataXml.CleanInvalidCharacters().ToString(SaveOptions.DisableFormatting);

					// update cache
					await Task.WhenAll
					(
						expiresAt != null
							? expiresAt.Value < DateTime.Now
								? Task.CompletedTask
								: Utility.Cache.SetAsync(cacheKey, data, expiresAt.Value, cancellationToken)
							: Utility.Cache.SetAsync(cacheKey, data, cancellationToken),
						@object.ContentType != null
							? Utility.Cache.AddSetMemberAsync(@object.ContentType.ObjectCacheKeys, @object.GetCacheKey(), cancellationToken)
							: Task.CompletedTask,
						@object.ContentType != null
							? Utility.Cache.AddSetMembersAsync(@object.ContentType.GetSetCacheKey(), new[] { cacheKey }, cancellationToken)
							: Task.CompletedTask,
						Utility.IsCacheLogEnabled
							? Utility.WriteLogAsync(requestInfo, $"Update related keys into Content-Type's set when generate details of CMS.Content [{@object.ContentType?.Title} - ID: {@object.ContentType?.ID} - Set: {@object.ContentType?.GetSetCacheKey()}]\r\n- Related cache keys (1): {cacheKey}", "Caches")
							: Task.CompletedTask
					).ConfigureAwait(false);

					// preload
					if (Utility.Preload && (relateds.Count > 0 || others.Count > 0))
						await relateds.Concat(others).PreloadAsync(cancellationToken, requestInfo.CorrelationID).ConfigureAwait(false);
				}

				// build others
				breadcrumbs = showBreadcrumbs ? @object.Category?.GenerateBreadcrumbs(desktop) ?? new JArray() : null;
				pagination = showPagination ? Utility.GeneratePagination(1, 1, 0, pageNumber, @object.GetURL(desktop, true), showPageLinks, numberOfPageLinks) : null;
				thumbnailsTask = thumbnailsTask ?? requestInfo.GetThumbnailsAsync(@object.ID, @object.Title.Url64Encode(), Utility.ValidationKey, cancellationToken);
				await thumbnailsTask.ConfigureAwait(false);
				coverURI = (thumbnailsTask.Result as JArray)?.First()?.Get<string>("URI")?.GetThumbnailURL(pngThumbnails, bigThumbnails, thumbnailsWidth, thumbnailsHeight);
				metaTags = new[] { $"<meta property=\"og:type\" content=\"{options.Get("Og:Type", "article")}\"/>" }.ToJArray();
				seoTitle = @object.Title;
				seoDescription = @object.Summary;
				seoKeywords = @object.Tags;
				ids = $"system:\"{@object.SystemID}\",repository:\"{@object.RepositoryID}\",entity:\"{@object.RepositoryEntityID}\",category:\"{@object.CategoryID}\",id:\"{@object.ID}\"";
			}

			// SEO
			seoInfo = new JObject
			{
				{ "Title", seoTitle },
				{ "Description", string.IsNullOrWhiteSpace(seoDescription) || seoDescription.IsStartsWith("~~/") || seoDescription.IsStartsWith("http://") || seoDescription.IsStartsWith("https://") ? null : seoDescription },
				{ "Keywords", seoKeywords }
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
				{ "CacheExpiration", expiresAt != null ? expiresAt.Value.ToDTString() : (randomPage ? Utility.Logger.IsEnabled(LogLevel.Debug) ? 3 : 13 : 0).ToString() },
				{ "IDs", ids + $",service:\"{moduleDefinitionJson.Get<string>("ServiceName").ToLower()}\",object:\"{contentTypeDefinitionJson.Get<string>("ObjectNamePrefix")?.ToLower()}{contentTypeDefinitionJson.Get<string>("ObjectName").ToLower()}{contentTypeDefinitionJson.Get<string>("ObjectNameSuffix")?.ToLower()}\"" }
			};
		}

		static async Task<List<Content>> LoadRelatedsAsync(this Content @object, CancellationToken cancellationToken = default, string correlationID = null)
		{
			var relateds = new List<Content>();
			if (@object.Relateds != null && @object.Relateds.Count > 0)
			{
				var stopwatch = Stopwatch.StartNew();
				await @object.Relateds.ForEachAsync(async id => relateds.Add(await Content.GetAsync<Content>(id, cancellationToken).ConfigureAwait(false)), true, Utility.RunProcessorInParallelsMode).ConfigureAwait(false);

				var cacheKeys = relateds.Select(related => related?.GetCacheKey()).Where(key => !string.IsNullOrWhiteSpace(key)).ToList();
				await Utility.Cache.AddSetMembersAsync(@object.ContentType.ObjectCacheKeys, cacheKeys, cancellationToken).ConfigureAwait(false);

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
				await Utility.Cache.RemoveAsync(new[] { $"{objectCacheKey}:others", $"{objectCacheKey}:newers", $"{objectCacheKey}:olders" }, cancellationToken).ConfigureAwait(false);

			var relatedCacheKeys = new List<string>
			{
				$"{objectCacheKey}:others"
			};

			var others = new List<Content>();
			var otherIDs = await Utility.Cache.GetAsync<List<string>>($"{objectCacheKey}:others", cancellationToken).ConfigureAwait(false);
			if (otherIDs != null && otherIDs.Count > 0)
				await otherIDs.ForEachAsync(async id => others.Add(await Content.GetAsync<Content>(id, cancellationToken).ConfigureAwait(false)), true, Utility.RunProcessorInParallelsMode).ConfigureAwait(false);

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
				), Sorts<Content>.Descending("StartDate").ThenByDescending("PublishedTime"), numberOfOthers, 1, @object.ContentTypeID, $"{objectCacheKey}:olders", cancellationToken);

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
			var objectCacheKeys = others.Select(obj => obj?.GetCacheKey()).Where(key => !string.IsNullOrWhiteSpace(key)).ToList();
			await Task.WhenAll
			(
				Utility.Cache.SetAsync($"{objectCacheKey}:others", others.Select(other => other.ID).ToList(), cancellationToken),
				Utility.Cache.AddSetMembersAsync(@object.ContentType.ObjectCacheKeys, objectCacheKeys, cancellationToken),
				Utility.Cache.AddSetMembersAsync(@object.ContentType.GetSetCacheKey(), relatedCacheKeys, cancellationToken)
			).ConfigureAwait(false);

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
			var content = await Content.GetAsync<Content>(data.Get<string>("ID"), cancellationToken).ConfigureAwait(false);
			var oldStatus = content != null ? content.Status : ApprovalStatus.Pending;

			// sync
			if (!@event.IsEquals("Delete"))
			{
				if (content == null)
				{
					content = data.CreateContent(null, obj =>
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
					content.Update(data, null, obj =>
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
					await Content.UpdateAsync(content, dontCreateNewVersion, cancellationToken).ConfigureAwait(false);
				}
			}
			else if (content != null)
				await Content.DeleteAsync<Content>(content.ID, content.LastModifiedID, cancellationToken).ConfigureAwait(false);

			// stop if has no info
			if (content == null)
				return new JObject();

			// update cache & send notification
			if (@event.IsEquals("Delete"))
				await Utility.Cache.RemoveSetMemberAsync(content.ContentType.ObjectCacheKeys, content.GetCacheKey(), cancellationToken).ConfigureAwait(false);
			else
				await Utility.Cache.AddSetMemberAsync(content.ContentType.ObjectCacheKeys, content.GetCacheKey(), cancellationToken).ConfigureAwait(false);

			await Task.WhenAll
			(
				content.ClearRelatedCacheAsync(cancellationToken, requestInfo.CorrelationID),
				sendNotifications ? content.SendNotificationAsync(@event, content.ContentType.Notifications, oldStatus, content.Status, requestInfo, cancellationToken) : Task.CompletedTask
			).ConfigureAwait(false);

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
			var content = await Content.GetAsync<Content>(requestInfo.GetObjectIdentity() ?? "", cancellationToken).ConfigureAwait(false);
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
			content = await RepositoryMediator.RollbackAsync<Content>(requestInfo.GetParameter("x-version-id") ?? "", requestInfo.Session.User.ID, cancellationToken).ConfigureAwait(false);

			// update cache & send notification
			Task.WhenAll
			(
				content.ClearRelatedCacheAsync(Utility.CancellationToken, requestInfo.CorrelationID),
				content.SendNotificationAsync("Rollback", content.Category.Notifications, oldStatus, content.Status, requestInfo, Utility.CancellationToken)
			).Run();

			// send update messages
			var objectName = content.GetObjectName();
			var thumbnailsTask = requestInfo.GetThumbnailsAsync(content.ID, content.Title.Url64Encode(), Utility.ValidationKey, cancellationToken);
			var attachmentsTask = requestInfo.GetAttachmentsAsync(content.ID, content.Title.Url64Encode(), Utility.ValidationKey, cancellationToken);
			var versionsTask = content.FindVersionsAsync(cancellationToken, false);
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
	}
}
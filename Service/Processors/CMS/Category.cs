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
using net.vieapps.Services.Portals.Exceptions;
using net.vieapps.Services.Portals.Crawlers;
using System.Security.Policy;

#endregion

namespace net.vieapps.Services.Portals
{
	public static class CategoryProcessor
	{
		internal static ConcurrentDictionary<string, Category> Categories { get; } = new ConcurrentDictionary<string, Category>(StringComparer.OrdinalIgnoreCase);

		internal static ConcurrentDictionary<string, Category> CategoriesByAlias { get; } = new ConcurrentDictionary<string, Category>(StringComparer.OrdinalIgnoreCase);

		internal static HashSet<string> ExcludedAliases { get; } = (UtilityService.GetAppSetting("Portals:ExcludedAliases", "") + ",All,Feed,Feeds,Atom,Rss").ToLower().ToHashSet();

		internal static HashSet<string> ExtraProperties { get; } = "PrimaryContentID,Notifications,EmailSettings".ToHashSet();

		public static Category CreateCategory(this ExpandoObject data, string excluded = null, Action<Category> onCompleted = null)
			=> Category.CreateInstance(data, excluded?.ToHashSet(), category =>
			{
				category.Alias = (string.IsNullOrWhiteSpace(category.Alias) ? category.Title : category.Alias).NormalizeAlias();
				category.NormalizeExtras();
				onCompleted?.Invoke(category);
			});

		public static Category Update(this Category category, ExpandoObject data, string excluded = null, Action<Category> onCompleted = null)
			=> category.Fill(data, excluded?.ToHashSet(), _ =>
			{
				category.Alias = category.Alias?.NormalizeAlias();
				category.NormalizeExtras();
				onCompleted?.Invoke(category);
			});

		internal static string GetCacheKeyOfAliasedCategory(this string contentTypeID, string alias)
			=> !string.IsNullOrWhiteSpace(contentTypeID) && !string.IsNullOrWhiteSpace(alias)
				? $"{contentTypeID}:{alias.NormalizeAlias()}"
				: null;

		internal static Category Set(this Category category, bool clear = false, bool updateCache = false, string oldAlias = null)
		{
			if (category != null && !string.IsNullOrWhiteSpace(category.ID) && !string.IsNullOrWhiteSpace(category.Title))
			{
				if (clear)
					category.Remove();

				if (updateCache)
					Utility.Cache.SetAsync(category).Run();

				CategoryProcessor.Categories[category.ID] = category;
				CategoryProcessor.CategoriesByAlias[category.RepositoryEntityID.GetCacheKeyOfAliasedCategory(category.Alias)] = category;

				if (!string.IsNullOrWhiteSpace(oldAlias) && !oldAlias.IsEquals(category.Alias))
					CategoryProcessor.CategoriesByAlias.Remove(category.RepositoryEntityID.GetCacheKeyOfAliasedCategory(oldAlias));
			}
			return category;
		}

		internal static async Task<Category> SetAsync(this Category category, bool clear = false, bool updateCache = false, CancellationToken cancellationToken = default, string oldAlias = null)
		{
			category?.Set(clear, false, oldAlias);
			await (updateCache && category != null && !string.IsNullOrWhiteSpace(category.ID) && !string.IsNullOrWhiteSpace(category.Title) ? Utility.Cache.SetAsync(category, cancellationToken) : Task.CompletedTask).ConfigureAwait(false);
			return category;
		}

		internal static Category Remove(this Category category)
			=> (category?.ID ?? "").RemoveCategory();

		internal static Category RemoveCategory(this string id)
		{
			if (!string.IsNullOrWhiteSpace(id) && CategoryProcessor.Categories.TryRemove(id, out var category) && category != null)
			{
				CategoryProcessor.CategoriesByAlias.Remove(category.RepositoryEntityID.GetCacheKeyOfAliasedCategory(category.Alias));
				return category;
			}
			return null;
		}

		public static Category GetCategoryByID(this string id, bool force = false, bool fetchRepository = true)
			=> !force && !string.IsNullOrWhiteSpace(id) && CategoryProcessor.Categories.ContainsKey(id)
				? CategoryProcessor.Categories[id]
				: fetchRepository && !string.IsNullOrWhiteSpace(id)
					? Category.Get<Category>(id)?.Set()
					: null;

		public static async Task<Category> GetCategoryByIDAsync(this string id, CancellationToken cancellationToken = default, bool force = false)
			=> (id ?? "").GetCategoryByID(force, false) ?? (await Category.GetAsync<Category>(id, cancellationToken).ConfigureAwait(false))?.Set();

		public static Category GetCategoryByAlias(this string repositoryEntityID, string alias, bool fetchRepository = true)
		{
			if (string.IsNullOrWhiteSpace(repositoryEntityID) || string.IsNullOrWhiteSpace(alias))
				return null;

			if ((!CategoryProcessor.CategoriesByAlias.TryGetValue(repositoryEntityID.GetCacheKeyOfAliasedCategory(alias), out var category) || category == null) && fetchRepository)
				category = Category.Get(Filters<Category>.And(Filters<Category>.Equals("RepositoryEntityID", repositoryEntityID), Filters<Category>.Equals("Alias", alias.NormalizeAlias())), null, repositoryEntityID)?.Set();

			return category;
		}

		public static async Task<Category> GetCategoryByAliasAsync(this string repositoryEntityID, string alias, CancellationToken cancellationToken = default)
			=> string.IsNullOrWhiteSpace(repositoryEntityID) || string.IsNullOrWhiteSpace(alias)
				? null
				: repositoryEntityID.GetCategoryByAlias(alias, false) ?? (await Category.GetAsync(Filters<Category>.And(Filters<Category>.Equals("RepositoryEntityID", repositoryEntityID), Filters<Category>.Equals("Alias", alias.NormalizeAlias())), null, repositoryEntityID, cancellationToken).ConfigureAwait(false))?.Set();

		public static IFilterBy<Category> GetCategoriesFilter(string systemID, string repositoryID = null, string repositoryEntityID = null, string parentID = null, Action<FilterBys<Category>> onCompleted = null)
		{
			var filter = Filters<Category>.And();
			if (!string.IsNullOrWhiteSpace(systemID))
				filter.Add(Filters<Category>.Equals("SystemID", systemID));
			if (!string.IsNullOrWhiteSpace(repositoryID))
				filter.Add(Filters<Category>.Equals("RepositoryID", repositoryID));
			if (!string.IsNullOrWhiteSpace(repositoryEntityID))
				filter.Add(Filters<Category>.Equals("RepositoryEntityID", repositoryEntityID));
			filter.Add(string.IsNullOrWhiteSpace(parentID) ? Filters<Category>.IsNull("ParentID") : Filters<Category>.Equals("ParentID", parentID));
			onCompleted?.Invoke(filter);
			return filter;
		}

		public static List<Category> FindCategories(this string systemID, string repositoryID = null, string repositoryEntityID = null, string parentID = null, bool updateCache = true)
		{
			if (string.IsNullOrWhiteSpace(systemID))
				return new List<Category>();
			var filter = CategoryProcessor.GetCategoriesFilter(systemID, repositoryID, repositoryEntityID, parentID);
			var sort = Sorts<Category>.Ascending("OrderIndex").ThenByAscending("Title");
			var categories = Category.Find(filter, sort, 0, 1, Extensions.GetCacheKey(filter, sort, 0, 1));
			categories.ForEach(category => category.Set(false, updateCache));
			return categories;
		}

		public static async Task<List<Category>> FindCategoriesAsync(this string systemID, string repositoryID = null, string repositoryEntityID = null, string parentID = null, CancellationToken cancellationToken = default, bool updateCache = true)
		{
			if (string.IsNullOrWhiteSpace(systemID))
				return new List<Category>();
			var filter = CategoryProcessor.GetCategoriesFilter(systemID, repositoryID, repositoryEntityID, parentID);
			var sort = Sorts<Category>.Ascending("OrderIndex").ThenByAscending("Title");
			var categories = await Category.FindAsync(filter, sort, 0, 1, Extensions.GetCacheKey(filter, sort, 0, 1), cancellationToken).ConfigureAwait(false);
			categories.ForEach(category => category.Set(false, updateCache));
			return categories;
		}

		internal static async Task<int> GetLastOrderIndexAsync(string systemID, string repositoryID = null, string repositoryEntityID = null, string parentID = null, CancellationToken cancellationToken = default)
		{
			var categories = await systemID.FindCategoriesAsync(repositoryID, repositoryEntityID, parentID, cancellationToken).ConfigureAwait(false);
			return categories != null && categories.Count > 0 ? categories.Last().OrderIndex : -1;
		}

		internal static async Task ProcessInterCommunicateMessageOfCategoryAsync(this CommunicateMessage message, CancellationToken cancellationToken = default)
		{
			if (message.Type.IsEndsWith("#Create"))
			{
				var category = message.Data.ToExpandoObject().CreateCategory();
				category._childrenIDs = null;
				await category.FindChildrenAsync(cancellationToken, false).ConfigureAwait(false);
				category.Set();
			}

			else if (message.Type.IsEndsWith("#Update"))
			{
				var category = message.Data.Get("ID", "").GetCategoryByID(false, false);
				var oldAlias = category?.Alias;
				category = category == null
					? message.Data.ToExpandoObject().CreateCategory()
					: category.Update(message.Data.ToExpandoObject());
				category._childrenIDs = null;
				await category.FindChildrenAsync(cancellationToken, false).ConfigureAwait(false);
				category.Set(false, false, oldAlias);
			}

			else if (message.Type.IsEndsWith("#Delete"))
				message.Data.ToExpandoObject().CreateCategory().Remove();
		}

		internal static async Task ClearRelatedCacheAsync(this Category category, CancellationToken cancellationToken = default, string correlationID = null, bool clearDataCache = true, bool clearHtmlCache = true, bool doRefresh = true)
		{
			// tasks for updating sets
			var setTasks = new List<Task>();

			// data cache keys
			var dataCacheKeys = clearDataCache && category != null
				? Extensions.GetRelatedCacheKeys(category.GetCacheKey())
				: new List<string>();

			var childrenContentTypes = clearDataCache && category != null
				? category.ContentType?.GetChildren() ?? new List<ContentType>()
				: new List<ContentType>();
			await childrenContentTypes.ForEachAsync(async contentType =>
			{
				var cacheKeys = await Utility.Cache.GetSetMembersAsync(contentType.GetSetCacheKey(), cancellationToken).ConfigureAwait(false);
				if (cacheKeys != null && cacheKeys.Any())
				{
					setTasks.Add(Utility.Cache.RemoveSetMembersAsync(contentType.GetSetCacheKey(), cacheKeys, cancellationToken));
					dataCacheKeys = dataCacheKeys.Concat(cacheKeys).ToList();
				}
			}).ConfigureAwait(false);

			var linkContentTypes = new Dictionary<string, ContentType>();
			if (clearDataCache)
			{
				var sort = Sorts<Category>.Ascending("OrderIndex").ThenByAscending("Title");
				if (!string.IsNullOrWhiteSpace(category?.ID))
					dataCacheKeys.Add(Extensions.GetCacheKey(CategoryProcessor.GetCategoriesFilter(category.SystemID, category.RepositoryID, category.RepositoryEntityID, category.ID), sort, 0, 1));
				if (!string.IsNullOrWhiteSpace(category?.ParentID))
					dataCacheKeys.Add(Extensions.GetCacheKey(CategoryProcessor.GetCategoriesFilter(category.SystemID, category.RepositoryID, category.RepositoryEntityID, category.ParentID), sort, 0, 1));
				if (category?.ContentType != null)
				{
					var cacheKeys = await Utility.Cache.GetSetMembersAsync(category.ContentType.GetSetCacheKey(), cancellationToken).ConfigureAwait(false);
					if (cacheKeys != null && cacheKeys.Any())
					{
						setTasks.Add(Utility.Cache.RemoveSetMembersAsync(category.ContentType.GetSetCacheKey(), cacheKeys, cancellationToken));
						dataCacheKeys = dataCacheKeys.Concat(cacheKeys).ToList();
					}
				}

				// data cache keys of the related links
				var links = await Link.FindAsync(Filters<Link>.And(Filters<Link>.Equals("SystemID", category.SystemID), Filters<Link>.Equals("LookupRepositoryID", category?.RepositoryID)), Sorts<Link>.Ascending("ParentID").ThenByAscending("OrderIndex"), 0, 1, null, cancellationToken).ConfigureAwait(false);
				await links.ForEachAsync(async link =>
				{
					dataCacheKeys = dataCacheKeys.Concat(Extensions.GetRelatedCacheKeys(link.GetCacheKey())).ToList();
					if (link?.ContentType != null && !linkContentTypes.ContainsKey(link.ContentType.ID))
					{
						linkContentTypes.Add(link.ContentType.ID, link.ContentType);
						var cacheKeys = await Utility.Cache.GetSetMembersAsync(link.ContentType.GetSetCacheKey(), cancellationToken).ConfigureAwait(false);
						if (cacheKeys != null && cacheKeys.Any())
						{
							setTasks.Add(Utility.Cache.RemoveSetMembersAsync(link.ContentType.GetSetCacheKey(), cacheKeys, cancellationToken));
							dataCacheKeys = dataCacheKeys.Concat(cacheKeys).ToList();
						}
					}
				}, true, false).ConfigureAwait(false);
			}
			dataCacheKeys = dataCacheKeys.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

			// html cache keys (desktop HTMLs) that related to links
			var htmlCacheKeys = new List<string>();
			if (clearHtmlCache)
			{
				htmlCacheKeys = category?.Organization?.GetDesktopCacheKey() ?? new List<string>();
				await linkContentTypes.ForEachAsync(async linkContentType =>
				{
					var desktopSetCacheKeys = await linkContentType.GetSetCacheKeysAsync(cancellationToken).ConfigureAwait(false);
					await desktopSetCacheKeys.ForEachAsync(async desktopSetCacheKey =>
					 {
						 var cacheKeys = await Utility.Cache.GetSetMembersAsync(desktopSetCacheKey, cancellationToken).ConfigureAwait(false);
						 if (cacheKeys != null && cacheKeys.Any())
						 {
							 setTasks.Add(Utility.Cache.RemoveSetMembersAsync(desktopSetCacheKey, cacheKeys, cancellationToken));
							 htmlCacheKeys = htmlCacheKeys.Concat(cacheKeys).ToList();
						 }
					 }, true, false).ConfigureAwait(false);
				}, true, false).ConfigureAwait(false);
			}
			htmlCacheKeys = htmlCacheKeys.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

			// remove related cache
			await Task.WhenAll
			(
				Task.WhenAll(setTasks),
				Utility.Cache.RemoveAsync(htmlCacheKeys.Concat(dataCacheKeys).Distinct(StringComparer.OrdinalIgnoreCase).ToList(), cancellationToken),
				Utility.IsCacheLogEnabled && category != null ? Utility.WriteLogAsync(correlationID, $"Clear related cache of a CMS category [{category.Title} - ID: {category.ID}]\r\n- {dataCacheKeys.Count} data keys => {dataCacheKeys.Join(", ")}\r\n- {htmlCacheKeys.Count} html keys => {htmlCacheKeys.Join(", ")}", "Caches") : Task.CompletedTask,
				doRefresh && category != null
					? Task.WhenAll
					(
						category.GetURL().Replace("~/", $"{category.Organization?.URL}/").RefreshWebPageAsync(1, correlationID, $"Refresh desktop when related cache of a CMS category was clean [{category.Title} - ID: {category.ID}]"),
						$"{category.Organization?.URL}/".RefreshWebPageAsync(1, correlationID, $"Refresh desktop when related cache of a CMS category was clean [{category.Title} - ID: {category.ID}]")
					) : Task.CompletedTask
				).ConfigureAwait(false);
		}

		static async Task<Tuple<long, List<Category>, JToken, List<string>>> SearchAsync(this RequestInfo requestInfo, string query, IFilterBy<Category> filter, SortBy<Category> sort, int pageSize, int pageNumber, string contentTypeID = null, long totalRecords = -1, CancellationToken cancellationToken = default, bool searchThumbnails = false)
		{
			// cache keys
			var cacheKeyOfObjects = string.IsNullOrWhiteSpace(query) ? Extensions.GetCacheKey(filter, sort, pageSize, pageNumber) : null;
			var cacheKeyOfTotalObjects = string.IsNullOrWhiteSpace(query) ? Extensions.GetCacheKeyOfTotalObjects(filter, sort) : null;
			var cacheKeys = string.IsNullOrWhiteSpace(query) ? new List<string> { cacheKeyOfObjects, cacheKeyOfTotalObjects } : new List<string>();

			// count
			totalRecords = totalRecords > -1
				? totalRecords
				: string.IsNullOrWhiteSpace(query)
					? await Category.CountAsync(filter, contentTypeID, cacheKeyOfTotalObjects, cancellationToken).ConfigureAwait(false)
					: await Category.CountAsync(query, filter, contentTypeID, cancellationToken).ConfigureAwait(false);

			// search objects
			var objects = totalRecords > 0
				? string.IsNullOrWhiteSpace(query)
					? await Category.FindAsync(filter, sort, pageSize, pageNumber, contentTypeID, cacheKeyOfObjects, cancellationToken).ConfigureAwait(false)
					: await Category.SearchAsync(query, filter, null, pageSize, pageNumber, contentTypeID, cancellationToken).ConfigureAwait(false)
				: new List<Category>();

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
			return new Tuple<long, List<Category>, JToken, List<string>>(totalRecords, objects, thumbnails, cacheKeys);
		}

		internal static async Task<JObject> SearchCategoriesAsync(this RequestInfo requestInfo, bool isSystemAdministrator, CancellationToken cancellationToken)
		{
			// prepare
			var request = requestInfo.GetRequestExpando();

			var query = request.Get<string>("FilterBy.Query");

			var filter = request.Get<ExpandoObject>("FilterBy", null)?.ToFilterBy<Category>() ?? Filters<Category>.And();
			if (filter is FilterBys<Category>)
			{
				if (!string.IsNullOrWhiteSpace(query))
				{
					var index = (filter as FilterBys<Category>).Children.FindIndex(exp => (exp as FilterBy<Category>).Attribute.IsEquals("ParentID"));
					if (index > -1)
						(filter as FilterBys<Category>).Children.RemoveAt(index);
				}
				else if ((filter as FilterBys<Category>).Children.FirstOrDefault(exp => (exp as FilterBy<Category>).Attribute.IsEquals("ParentID")) == null)
					(filter as FilterBys<Category>).Children.Add(Filters<Category>.IsNull("ParentID"));
			}

			var sort = string.IsNullOrWhiteSpace(query) ? request.Get<ExpandoObject>("SortBy")?.ToSortBy<Category>() ?? Sorts<Category>.Ascending("OrderIndex").ThenByAscending("Title") : null;

			var pagination = request.Get<ExpandoObject>("Pagination")?.GetPagination() ?? new Tuple<long, int, int, int>(-1, 0, 20, 1);
			var pageSize = pagination.Item3;
			var pageNumber = pagination.Item4;

			// get organization
			var organizationID = filter.GetValue("SystemID") ?? requestInfo.GetParameter("SystemID") ?? requestInfo.GetParameter("x-system-id") ?? requestInfo.GetParameter("OrganizationID");
			var organization = await (organizationID ?? "").GetOrganizationByIDAsync(cancellationToken).ConfigureAwait(false);
			if (organization == null)
				throw new InformationExistedException("The organization is invalid");

			var moduleID = filter.GetValue("RepositoryID") ?? requestInfo.GetParameter("RepositoryID") ?? requestInfo.GetParameter("x-module-id") ?? requestInfo.GetParameter("ModuleID");
			var module = await (moduleID ?? "").GetModuleByIDAsync(cancellationToken).ConfigureAwait(false);
			if (module == null || !module.SystemID.IsEquals(organization.ID))
				throw new InformationInvalidException("The module is invalid");

			var contentTypeID = filter.GetValue("RepositoryEntityID") ?? requestInfo.GetParameter("RepositoryEntityID") ?? requestInfo.GetParameter("x-content-type-id") ?? requestInfo.GetParameter("ContentTypeID");
			var contentType = await (contentTypeID ?? "").GetContentTypeByIDAsync(cancellationToken).ConfigureAwait(false);
			if (contentType == null || !contentType.SystemID.IsEquals(organization.ID) || !contentType.RepositoryID.IsEquals(module.ID))
				throw new InformationInvalidException("The content-type is invalid");

			// check permission
			var gotRights = isSystemAdministrator || requestInfo.Session.User.IsViewer(module.WorkingPrivileges, null, organization);
			if (!gotRights)
				throw new AccessDeniedException();

			// other parameters
			var showThumbnails = requestInfo.GetParameter("x-object-thumbnails") != null || requestInfo.GetParameter("ShowThumbnails") != null;
			var showURLs = requestInfo.GetParameter("x-object-urls") != null || requestInfo.GetParameter("ShowURLs") != null;
			var showChildren = requestInfo.GetParameter("x-object-children") != null || requestInfo.GetParameter("ShowChildren") != null;
			var addChildren = "true".IsEquals(requestInfo.GetHeaderParameter("x-children"));

			// process cache
			var suffix = string.IsNullOrWhiteSpace(query) ? (showThumbnails ? ":t" : "") + (showURLs ? ":u" : "") + (showChildren ? ":c" : "") : null;
			var cacheKeyOfObjectsJson = string.IsNullOrWhiteSpace(query) ? Extensions.GetCacheKeyOfObjectsJson(filter, sort, pageSize, pageNumber, string.IsNullOrWhiteSpace(suffix) ? null : suffix) : null;
			if (cacheKeyOfObjectsJson != null && !addChildren)
			{
				var json = await Utility.Cache.GetAsync<string>(cacheKeyOfObjectsJson, cancellationToken).ConfigureAwait(false);
				if (!string.IsNullOrWhiteSpace(json))
					return JObject.Parse(json);
			}

			// search if has no cache
			var results = await requestInfo.SearchAsync(query, filter, sort, pageSize, pageNumber, contentTypeID, pagination.Item1 > -1 ? pagination.Item1 : -1, cancellationToken, showThumbnails).ConfigureAwait(false);
			var totalRecords = results.Item1;
			var objects = results.Item2;
			var thumbnails = results.Item3;

			// build response
			var totalPages = new Tuple<long, int>(totalRecords, pageSize).GetTotalPages();
			if (totalPages > 0 && pageNumber > totalPages)
				pageNumber = totalPages;
			pagination = new Tuple<long, int, int, int>(totalRecords, totalPages, pageSize, pageNumber);

			if (addChildren || showChildren)
				await objects.Where(category => category != null && category._childrenIDs == null).ForEachAsync(category => category.FindChildrenAsync(cancellationToken), true, false).ConfigureAwait(false);

			var objectsJson = objects.Where(category => category != null).Select(category => category.ToJson(addChildren, false)).ToList();
			var siteURL = organization.DefaultSite?.GetURL(requestInfo.GetHeaderParameter("x-srp-host"), requestInfo.GetParameter("x-url")) + "/";

			if (showThumbnails || showURLs || (showChildren && !addChildren))
				await objectsJson.ForEachAsync(async json =>
				{
					json.Remove("Privileges");
					json.Remove("OriginalPrivileges");
					json.Remove("Notifications");
					json.Remove("EmailSettings");

					var category = await json.Get<string>("ID").GetCategoryByIDAsync(cancellationToken).ConfigureAwait(false);

					if (showThumbnails)
						json["ThumbnailURL"] = organization.NormalizeURLs(thumbnails?.GetThumbnailURL(category.ID));

					if (showURLs)
						json["URL"] = organization.NormalizeURLs(category.GetURL(), true, siteURL);

					if (showChildren && !addChildren)
					{
						var childrenJson = category.Children?.Where(cat => cat != null).OrderBy(cat => cat.OrderIndex).Select(cat => cat.ToJson(false, false)).ToList();
						await childrenJson.ForEachAsync(async cjson =>
						{
							cjson.Remove("Privileges");
							cjson.Remove("OriginalPrivileges");
							cjson.Remove("Notifications");
							cjson.Remove("EmailSettings");

							var cat = await cjson.Get<string>("ID").GetCategoryByIDAsync(cancellationToken).ConfigureAwait(false);

							if (showThumbnails)
								await cjson.GenerateThumbnailURLAsync(cat, requestInfo, cancellationToken).ConfigureAwait(false);

							if (showURLs)
								cjson["URL"] = organization.NormalizeURLs(cat.GetURL(), true, siteURL);
						}).ConfigureAwait(false);

						json["Children"] = childrenJson.ToJArray();
					}
				}).ConfigureAwait(false);

			var response = new JObject
			{
				{ "FilterBy", filter.ToClientJson(query) },
				{ "SortBy", sort?.ToClientJson() },
				{ "Pagination", pagination.GetPagination() },
				{ "Objects", objectsJson.ToJArray() }
			};

			// update cache
			if (string.IsNullOrWhiteSpace(query) && !addChildren)
			{
				var cacheKeys = new[] { cacheKeyOfObjectsJson }.Concat(results.Item4).ToList();
				await Task.WhenAll
				(
					Utility.Cache.SetAsync(cacheKeyOfObjectsJson, response.ToString(Formatting.None), cancellationToken),
					Utility.Cache.AddSetMembersAsync(contentType.GetSetCacheKey(), cacheKeys, cancellationToken),
					Utility.IsCacheLogEnabled ? Utility.WriteLogAsync(requestInfo, $"Update cache when search CMS categories\r\n- Cache key of JSON: {cacheKeyOfObjectsJson}\r\n- Cache key of Content-Type's set: {contentType.GetSetCacheKey()}\r\n- Related cache keys: {cacheKeys.Join(", ")}", "Caches") : Task.CompletedTask
				).ConfigureAwait(false);
			}

			// response
			return response;
		}

		internal static async Task<JObject> CreateCategoryAsync(this RequestInfo requestInfo, bool isSystemAdministrator, CancellationToken cancellationToken)
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
			var gotRights = isSystemAdministrator || requestInfo.Session.User.IsModerator(module.WorkingPrivileges, null, organization);
			if (!gotRights)
				throw new AccessDeniedException();

			// check the exising the the alias
			var alias = request.Get<string>("Alias");
			if (!string.IsNullOrWhiteSpace(alias) && CategoryProcessor.ExcludedAliases.Contains(alias.NormalizeAlias()))
				throw new AliasIsExistedException($"The alias ({alias.NormalizeAlias()}) is used by another category");

			if (!string.IsNullOrWhiteSpace(alias))
			{
				var existing = await contentType.ID.GetCategoryByAliasAsync(alias.NormalizeAlias(), cancellationToken).ConfigureAwait(false);
				if (existing != null)
					throw new AliasIsExistedException($"The alias ({alias.NormalizeAlias()}) is used by another category");
			}

			// gathering information
			var category = request.CreateCategory("SystemID,RepositoryID,RepositoryEntityID,Privileges,OrderIndex,Created,CreatedID,LastModified,LastModifiedID", obj =>
			{
				obj._childrenIDs = new List<string>();
				obj.ID = string.IsNullOrWhiteSpace(obj.ID) || !obj.ID.IsValidUUID() ? UtilityService.NewUUID : obj.ID;
				obj.SystemID = organization.ID;
				obj.RepositoryID = module.ID;
				obj.RepositoryEntityID = contentType.ID;
				obj.ParentID = obj.ParentCategory != null ? obj.ParentID : null;
				obj.Created = obj.LastModified = DateTime.Now;
				obj.CreatedID = obj.LastModifiedID = requestInfo.Session.User.ID;
			});
			category.Notifications?.WebHooks?.Validate(requestInfo, category.Organization, category.Module, category.ContentType, category);

			// prepare order index
			category.OrderIndex = (await CategoryProcessor.GetLastOrderIndexAsync(category.SystemID, category.RepositoryID, category.RepositoryEntityID, category.ParentID, cancellationToken).ConfigureAwait(false)) + 1;

			// create new
			await Category.CreateAsync(category, cancellationToken).ConfigureAwait(false);
			await category.Set().ClearRelatedCacheAsync(cancellationToken, requestInfo.CorrelationID).ConfigureAwait(false);

			var updateMessages = new List<UpdateMessage>();
			var communicateMessages = new List<CommunicateMessage>();
			var objectName = category.GetObjectName();

			// update parent
			var parentCategory = category.ParentCategory;
			if (parentCategory != null)
			{
				await Utility.Cache.RemoveAsync(Extensions.GetRelatedCacheKeys(CategoryProcessor.GetCategoriesFilter(category.SystemID, category.RepositoryID, category.RepositoryEntityID, category.ParentID), Sorts<Category>.Ascending("OrderIndex").ThenByAscending("Title")), cancellationToken).ConfigureAwait(false);
				await parentCategory.FindChildrenAsync(cancellationToken, false).ConfigureAwait(false);
				parentCategory._children = null;
				if (parentCategory._childrenIDs.IndexOf(category.ID) < 0)
					parentCategory._childrenIDs.Add(category.ID);

				var json = parentCategory.Set(false, true).ToJson(true, false);
				updateMessages.Add(new UpdateMessage
				{
					Type = $"{requestInfo.ServiceName}#{objectName}#Update",
					Data = json,
					DeviceID = "*"
				});
				communicateMessages.Add(new CommunicateMessage(requestInfo.ServiceName)
				{
					Type = $"{objectName}#Update",
					Data = json,
					ExcludedNodeID = Utility.NodeID
				});
			}

			// message to update to all other connected clients
			var response = category.ToJson(true, false);

			if (category.ParentCategory == null)
				updateMessages.Add(new UpdateMessage
				{
					Type = $"{requestInfo.ServiceName}#{objectName}#Create",
					DeviceID = "*",
					Data = response
				});

			// message to update to all service instances (on all other nodes)
			communicateMessages.Add(new CommunicateMessage(requestInfo.ServiceName)
			{
				Type = $"{objectName}#Create",
				Data = response,
				ExcludedNodeID = Utility.NodeID
			});

			// send update messages
			updateMessages.Send();
			communicateMessages.Send();

			// send notification
			await category.SendNotificationAsync("Create", category.ContentType.Notifications, ApprovalStatus.Draft, category.Status, requestInfo, cancellationToken).ConfigureAwait(false);
			category.Organization.SendRefreshingTasks();

			// store object cache key to clear related cached
			await Utility.Cache.AddSetMemberAsync(category.ContentType.ObjectCacheKeys, category.GetCacheKey(), cancellationToken).ConfigureAwait(false);

			// response
			return response;
		}

		internal static async Task<JObject> GetCategoryAsync(this RequestInfo requestInfo, bool isSystemAdministrator, CancellationToken cancellationToken)
		{
			// prepare
			var identity = requestInfo.GetObjectIdentity(true, true) ?? "";
			var contentTypeID = requestInfo.GetParameter("RepositoryEntityID") ?? requestInfo.GetParameter("x-content-type-id") ?? requestInfo.GetParameter("ContentTypeID") ?? "";

			var category = await (identity.IsValidUUID() ? identity.GetCategoryByIDAsync(cancellationToken) : contentTypeID.GetCategoryByAliasAsync(identity, cancellationToken)).ConfigureAwait(false);
			if (category == null)
				throw new InformationNotFoundException();
			else if (category.Organization == null || category.Module == null)
				throw new InformationInvalidException("The organization/module is invalid");

			// check permission
			var gotRights = isSystemAdministrator || requestInfo.Session.User.IsViewer(category.Module.WorkingPrivileges, null, category.Organization);
			if (!gotRights)
				throw new AccessDeniedException();

			// refresh (clear cached and reload)
			var isRefresh = "refresh".IsEquals(requestInfo.GetObjectIdentity());
			if (isRefresh)
			{
				await Utility.Cache.RemoveAsync(category, cancellationToken).ConfigureAwait(false);
				category = await category.Remove().ID.GetCategoryByIDAsync(cancellationToken, true).ConfigureAwait(false);
				category._childrenIDs = null;
			}

			// prepare the response
			if (category._childrenIDs == null)
			{
				await category.FindChildrenAsync(cancellationToken, false).ConfigureAwait(false);
				await category.SetAsync(false, true, cancellationToken).ConfigureAwait(false);
				if (!isRefresh)
					new CommunicateMessage(requestInfo.ServiceName)
					{
						Type = $"{category.GetObjectName()}#Update",
						Data = category.ToJson(false, false),
						ExcludedNodeID = Utility.NodeID
					}.Send();
			}

			// send update message
			var objectName = category.GetObjectName();
			var versions = await category.FindVersionsAsync(cancellationToken, false).ConfigureAwait(false);
			var response = category.ToJson(true, false);
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

			// store object cache key to clear related cached
			await Utility.Cache.AddSetMemberAsync(category.ContentType.ObjectCacheKeys, category.GetCacheKey(), cancellationToken).ConfigureAwait(false);

			// response
			return response;
		}

		static async Task UpdateRelatedOnUpdatedAsync(this Category category, RequestInfo requestInfo, string oldParentID, CancellationToken cancellationToken)
		{
			// update parent
			var objectName = category.GetObjectName();
			var parentCategory = category.ParentCategory;
			if (parentCategory != null && !category.ParentID.IsEquals(oldParentID))
			{
				await Utility.Cache.RemoveAsync(Extensions.GetRelatedCacheKeys(CategoryProcessor.GetCategoriesFilter(category.SystemID, category.RepositoryID, category.RepositoryEntityID, category.ParentID), Sorts<Category>.Ascending("OrderIndex").ThenByAscending("Title")), cancellationToken).ConfigureAwait(false);
				await parentCategory.FindChildrenAsync(cancellationToken, false).ConfigureAwait(false);
				parentCategory._children = null;
				if (parentCategory._childrenIDs.IndexOf(category.ID) < 0)
					parentCategory._childrenIDs.Add(category.ID);

				var json = parentCategory.Set(false, true).ToJson(true, false);
				var versions = await parentCategory.FindVersionsAsync(cancellationToken, false).ConfigureAwait(false);
				new UpdateMessage
				{
					Type = $"{requestInfo.ServiceName}#{objectName}#Update",
					Data = json.UpdateVersions(versions),
					DeviceID = "*"
				}.Send();
				new CommunicateMessage(requestInfo.ServiceName)
				{
					Type = $"{objectName}#Update",
					Data = json,
					ExcludedNodeID = Utility.NodeID
				}.Send();
			}

			// update old parent
			if (!string.IsNullOrWhiteSpace(oldParentID) && !oldParentID.IsEquals(category.ParentID))
			{
				parentCategory = await oldParentID.GetCategoryByIDAsync(cancellationToken).ConfigureAwait(false);
				if (parentCategory != null)
				{
					await Utility.Cache.RemoveAsync(Extensions.GetRelatedCacheKeys(CategoryProcessor.GetCategoriesFilter(category.SystemID, category.RepositoryID, category.RepositoryEntityID, parentCategory.ID), Sorts<Category>.Ascending("OrderIndex").ThenByAscending("Title")), cancellationToken).ConfigureAwait(false);
					await parentCategory.FindChildrenAsync(cancellationToken, false).ConfigureAwait(false);
					parentCategory._children = null;
					parentCategory._childrenIDs.Remove(category.ID);

					var versions = await parentCategory.FindVersionsAsync(cancellationToken, false).ConfigureAwait(false);
					var json = parentCategory.Set(false, true).ToJson(true, false);
					new UpdateMessage
					{
						Type = $"{requestInfo.ServiceName}#{objectName}#Update",
						Data = json.UpdateVersions(versions),
						DeviceID = "*"
					}.Send();
					new CommunicateMessage(requestInfo.ServiceName)
					{
						Type = $"{objectName}#Update",
						Data = json,
						ExcludedNodeID = Utility.NodeID
					}.Send();
				}
			}
		}

		internal static async Task<JObject> UpdateCategoryAsync(this RequestInfo requestInfo, bool isSystemAdministrator, CancellationToken cancellationToken)
		{
			// prepare
			var category = await (requestInfo.GetObjectIdentity() ?? "").GetCategoryByIDAsync(cancellationToken).ConfigureAwait(false);
			if (category == null)
				throw new InformationNotFoundException();
			else if (category.Organization == null || category.Module == null)
				throw new InformationInvalidException("The organization/module is invalid");

			// check permission
			var gotRights = isSystemAdministrator || requestInfo.Session.User.IsModerator(category.Module.WorkingPrivileges, null, category.Organization);
			if (!gotRights)
				throw new AccessDeniedException();

			var request = requestInfo.GetBodyExpando();
			var oldParentID = category.ParentID;
			var oldAlias = category.Alias;
			var oldStatus = category.Status;

			var alias = request.Get<string>("Alias");
			if (!string.IsNullOrWhiteSpace(alias) && CategoryProcessor.ExcludedAliases.Contains(alias.NormalizeAlias()))
				throw new AliasIsExistedException($"The alias ({alias.NormalizeAlias()}) is used by another category");

			if (!string.IsNullOrWhiteSpace(alias) && !alias.IsEquals(oldAlias))
			{
				var existing = await category.RepositoryEntityID.GetCategoryByAliasAsync(alias.NormalizeAlias(), cancellationToken).ConfigureAwait(false);
				if (existing != null && !existing.ID.IsEquals(category.ID))
					throw new InformationExistedException($"The alias ({category.Alias}) was used by another category");
			}

			category.Update(request, "ID,SystemID,RepositoryID,RepositoryEntityID,Privileges,OrderIndex,Created,CreatedID,LastModified,LastModifiedID", obj =>
			{
				obj.Alias = string.IsNullOrWhiteSpace(obj.Alias) ? oldAlias : obj.Alias.NormalizeAlias();
				obj.LastModified = DateTime.Now;
				obj.LastModifiedID = requestInfo.Session.User.ID;
			});
			category.Notifications?.WebHooks?.Validate(requestInfo, category.Organization, category.Module, category.ContentType, category);

			if (category.ParentCategory != null && !category.ParentID.IsEquals(oldParentID))
			{
				category.OrderIndex = (await CategoryProcessor.GetLastOrderIndexAsync(category.SystemID, category.RepositoryID, category.RepositoryEntityID, category.ParentID, cancellationToken).ConfigureAwait(false)) + 1;
				await category.FindChildrenAsync(cancellationToken, false).ConfigureAwait(false);
			}

			// update
			await Category.UpdateAsync(category, requestInfo.Session.User.ID, cancellationToken).ConfigureAwait(false);
			category.Set(false, false, oldAlias);

			// update cache & send notification
			Task.WhenAll
			(
				category.ClearRelatedCacheAsync(cancellationToken, requestInfo.CorrelationID),
				category.UpdateRelatedOnUpdatedAsync(requestInfo, oldParentID, cancellationToken),
				category.SendNotificationAsync("Update", category.ContentType.Notifications, oldStatus, category.Status, requestInfo, cancellationToken)
			).Run();
			category.Organization.SendRefreshingTasks();

			// send update messages
			var objectName = category.GetObjectName();
			var response = category.ToJson(true, false);
			new CommunicateMessage(requestInfo.ServiceName)
			{
				Type = $"{objectName}#Update",
				Data = response,
				ExcludedNodeID = Utility.NodeID
			}.Send();
			if (category.ParentCategory == null)
			{
				var versions = await category.FindVersionsAsync(cancellationToken, false).ConfigureAwait(false);
				new UpdateMessage
				{
					Type = $"{requestInfo.ServiceName}#{objectName}#Update",
					Data = response.UpdateVersions(versions),
					DeviceID = "*"
				}.Send();
			}
			return response;
		}

		internal static async Task<JObject> UpdateCategoriesAsync(this RequestInfo requestInfo, bool isSystemAdministrator, CancellationToken cancellationToken)
		{
			// prepare
			var request = requestInfo.GetBodyJson();

			var category = await (request.Get<string>("CategoryID") ?? requestInfo.GetParameter("CategoryID") ?? requestInfo.GetParameter("x-category-id") ?? "").GetCategoryByIDAsync(cancellationToken).ConfigureAwait(false);
			var organization = category != null
				? category.Organization
				: await (request.Get<string>("SystemID") ?? requestInfo.GetParameter("SystemID") ?? requestInfo.GetParameter("x-system-id") ?? "").GetOrganizationByIDAsync(cancellationToken).ConfigureAwait(false);
			if (organization == null)
				throw new InformationInvalidException("The organization is invalid");
			var module = category != null
				? category.Module
				: await (request.Get<string>("RepositoryID") ?? requestInfo.GetParameter("RepositoryID") ?? requestInfo.GetParameter("x-module-id") ?? "").GetModuleByIDAsync(cancellationToken).ConfigureAwait(false);
			if (module == null || !module.SystemID.IsEquals(organization.ID))
				throw new InformationInvalidException("The module is invalid");
			var contentType = category != null
				? category.ContentType
				: await (request.Get<string>("RepositoryEntityID") ?? requestInfo.GetParameter("RepositoryEntityID") ?? requestInfo.GetParameter("x-content-type-id") ?? "").GetContentTypeByIDAsync(cancellationToken).ConfigureAwait(false);
			if (contentType == null || !contentType.SystemID.IsEquals(organization.ID) || !contentType.RepositoryID.IsEquals(module.ID))
				throw new InformationInvalidException("The content-type is invalid");

			// check permission
			var gotRights = isSystemAdministrator || requestInfo.Session.User.IsModerator(module.WorkingPrivileges, null, organization);
			if (!gotRights)
				throw new AccessDeniedException();

			// update
			var objectName = category != null
				? category.GetObjectName()
				: typeof(Category).GetTypeName(true);

			var items = category != null
				? category.Children
				: await organization.ID.FindCategoriesAsync(module.ID, contentType.ID, null, cancellationToken, false).ConfigureAwait(false);

			Category first = null;
			var notificationTasks = new List<Task>();
			await request.Get<JArray>("Categories").ForEachAsync(async info =>
			{
				var id = info.Get<string>("ID");
				var orderIndex = info.Get<int>("OrderIndex");
				var item = items.Find(i => i.ID.IsEquals(id));
				if (item != null)
				{
					item.OrderIndex = orderIndex;
					item.LastModified = DateTime.Now;
					item.LastModifiedID = requestInfo.Session.User.ID;

					await Category.UpdateAsync(item, requestInfo.Session.User.ID, cancellationToken).ConfigureAwait(false);
					notificationTasks.Add(item.Set().SendNotificationAsync("Update", item.ContentType.Notifications, item.Status, item.Status, requestInfo, cancellationToken));

					var json = item.ToJson(true, false);
					var versions = await item.FindVersionsAsync(cancellationToken, false).ConfigureAwait(false);
					new UpdateMessage
					{
						Type = $"{requestInfo.ServiceName}#{objectName}#Update",
						Data = json.UpdateVersions(versions),
						DeviceID = "*"
					}.Send();
					new CommunicateMessage(requestInfo.ServiceName)
					{
						Type = $"{objectName}#Update",
						Data = json,
						ExcludedNodeID = Utility.NodeID
					}.Send();

					first = first ?? item;
				}
			});

			if (category != null)
			{
				await category.ClearRelatedCacheAsync(cancellationToken, requestInfo.CorrelationID).ConfigureAwait(false);
				category._childrenIDs = null;
				category._children = null;
				await category.FindChildrenAsync(cancellationToken, false).ConfigureAwait(false);
				await category.SetAsync(false, true, cancellationToken).ConfigureAwait(false);

				var json = category.ToJson(true, false);
				var versions = await category.FindVersionsAsync(cancellationToken, false).ConfigureAwait(false);
				new UpdateMessage
				{
					Type = $"{requestInfo.ServiceName}#{objectName}#Update",
					Data = json.UpdateVersions(versions),
					DeviceID = "*"
				}.Send();
				new CommunicateMessage(requestInfo.ServiceName)
				{
					Type = $"{objectName}#Update",
					Data = json,
					ExcludedNodeID = Utility.NodeID
				}.Send();
			}
			else if (first != null)
				await first.ClearRelatedCacheAsync(cancellationToken, requestInfo.CorrelationID).ConfigureAwait(false);

			await Task.WhenAll(notificationTasks).ConfigureAwait(false);
			organization.SendRefreshingTasks();
			return new JObject();
		}

		internal static async Task<JObject> DeleteCategoryAsync(this RequestInfo requestInfo, bool isSystemAdministrator, CancellationToken cancellationToken)
		{
			// prepare
			var category = await (requestInfo.GetObjectIdentity() ?? "").GetCategoryByIDAsync(cancellationToken).ConfigureAwait(false);
			if (category == null)
				throw new InformationNotFoundException();
			else if (category.Organization == null || category.Module == null)
				throw new InformationInvalidException("The organization/module is invalid");

			// check permission
			var gotRights = isSystemAdministrator || requestInfo.Session.User.IsModerator(category.Module.WorkingPrivileges, null, category.Organization);
			if (!gotRights)
				throw new AccessDeniedException();

			var objectName = category.GetObjectName();
			var updateChildren = requestInfo.Header.TryGetValue("x-children", out var childrenMode) && "set-null".IsEquals(childrenMode);

			// children
			var children = await category.FindChildrenAsync(cancellationToken, false).ConfigureAwait(false);
			await children.ForEachAsync(async child =>
			{
				// update to root
				if (updateChildren)
				{
					child.ParentID = null;
					child.LastModified = DateTime.Now;
					child.LastModifiedID = requestInfo.Session.User.ID;

					await Category.UpdateAsync(child, requestInfo.Session.User.ID, cancellationToken).ConfigureAwait(false);
					await child.SendNotificationAsync("Update", child.ContentType.Notifications, child.Status, child.Status, requestInfo, cancellationToken).ConfigureAwait(false);

					var json = child.Set().ToJson(true, false);
					var versions = await child.FindVersionsAsync(cancellationToken, false).ConfigureAwait(false);
					new UpdateMessage
					{
						Type = $"{requestInfo.ServiceName}#{objectName}#Update",
						Data = json.UpdateVersions(versions),
						DeviceID = "*"
					}.Send();
					new CommunicateMessage(requestInfo.ServiceName)
					{
						Type = $"{objectName}#Update",
						Data = json,
						ExcludedNodeID = Utility.NodeID
					}.Send();
				}

				// delete
				else
					await child.DeleteChildrenAsync(requestInfo, cancellationToken).ConfigureAwait(false);
			}, true, false).ConfigureAwait(false);

			// delete
			await requestInfo.DeleteFilesAsync(category.SystemID, category.RepositoryEntityID, category.ID, Utility.ValidationKey, cancellationToken).ConfigureAwait(false);
			await Category.DeleteAsync<Category>(category.ID, requestInfo.Session.User.ID, cancellationToken).ConfigureAwait(false);

			// update cache & send notifications
			Task.WhenAll
			(
				category.Remove().ClearRelatedCacheAsync(cancellationToken, requestInfo.CorrelationID),
				Utility.Cache.RemoveSetMemberAsync(category.ContentType.ObjectCacheKeys, category.GetCacheKey(), cancellationToken),
				category.SendNotificationAsync("Delete", category.ContentType.Notifications, category.Status, category.Status, requestInfo, cancellationToken)
			).Run();
			category.Organization.SendRefreshingTasks();

			// send update messages
			var response = category.ToJson();
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
			return response;
		}

		static async Task DeleteChildrenAsync(this Category category, RequestInfo requestInfo, CancellationToken cancellationToken = default)
		{
			var children = await category.FindChildrenAsync(cancellationToken, false).ConfigureAwait(false);
			await children.ForEachAsync(async child => await child.DeleteChildrenAsync(requestInfo, cancellationToken).ConfigureAwait(false), true, false).ConfigureAwait(false);

			await requestInfo.DeleteFilesAsync(category.SystemID, category.RepositoryEntityID, category.ID, Utility.ValidationKey, cancellationToken).ConfigureAwait(false);
			await Category.DeleteAsync<Category>(category.ID, requestInfo.Session.User.ID, cancellationToken).ConfigureAwait(false);

			Task.WhenAll
			(
				category.Remove().ClearRelatedCacheAsync(cancellationToken),
				Utility.Cache.RemoveSetMemberAsync(category.ContentType.ObjectCacheKeys, category.GetCacheKey(), cancellationToken),
				category.SendNotificationAsync("Delete", category.ContentType.Notifications, category.Status, category.Status, requestInfo, cancellationToken)
			).Run();

			var json = category.ToJson();
			var objectName = category.GetObjectName();
			new UpdateMessage
			{
				Type = $"{requestInfo.ServiceName}#{objectName}#Delete",
				Data = json,
				DeviceID = "*"
			}.Send();
			new CommunicateMessage(requestInfo.ServiceName)
			{
				Type = $"{objectName}#Delete",
				Data = json,
				ExcludedNodeID = Utility.NodeID
			}.Send();
		}

		internal static JArray GenerateBreadcrumbs(this Category category, string desktop = null)
		{
			var breadcrumbs = new List<Tuple<string, string>>
			{
				new Tuple<string, string>(category.Title, category.GetURL(desktop))
			};

			var parentCategory = category.ParentCategory;
			while (parentCategory != null)
			{
				breadcrumbs.Insert(0, new Tuple<string, string>(parentCategory.Title, parentCategory.GetURL(desktop)));
				parentCategory = parentCategory.ParentCategory;
			}

			return breadcrumbs.Select(breadcrumb => new JObject
			{
				{ "Text", breadcrumb.Item1 },
				{ "URL", breadcrumb.Item2 }
			}).ToJArray();
		}

		internal static async Task<JObject> GenerateAsync(RequestInfo requestInfo, bool isSystemAdministrator, CancellationToken cancellationToken)
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

			var contentTypeID = contentTypeJson.Get<string>("ID");
			var parentID = requestJson.Get<string>("ParentIdentity");

			var desktop = desktopsJson.Get<string>("Specified");
			desktop = !string.IsNullOrWhiteSpace(desktop) ? desktop : desktopsJson.Get<string>("ContentType");
			desktop = !string.IsNullOrWhiteSpace(desktop) ? desktop : desktopsJson.Get<string>("Module");
			desktop = !string.IsNullOrWhiteSpace(desktop) ? desktop : desktopsJson.Get<string>("Default");

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
			if (!(expressionJson.Get<JObject>("FilterBy")?.ToFilter<Category>() is FilterBys<Category> filter) || filter.Children == null || filter.Children.Count < 1)
				filter = Filters<Category>.And
				(
					Filters<Category>.Equals("SystemID", "@request.Body(Organization.ID)"),
					Filters<Category>.Equals("RepositoryID", "@request.Body(Module.ID)"),
					Filters<Category>.Equals("RepositoryEntityID", "@request.Body(ContentType.ID)"),
					string.IsNullOrWhiteSpace(parentID) || !parentID.IsValidUUID() ? Filters<Category>.IsNull("ParentID") : Filters<Category>.Equals("ParentID", parentID)
				);

			if (filter.GetChild("RepositoryEntityID") == null && contentType != null)
				filter.Add(Filters<Category>.Equals("RepositoryEntityID", contentType.ID));

			if (filter.GetChild("ParentID") == null)
				filter.Add(string.IsNullOrWhiteSpace(parentID) || !parentID.IsValidUUID() ? Filters<Category>.IsNull("ParentID") : Filters<Category>.Equals("ParentID", parentID));

			var filterBy = new JObject
			{
				{ "API", filter.ToJson().ToString(Formatting.None) },
			};
			filter.Prepare(requestInfo);
			filterBy["App"] = filter.ToClientJson().ToString(Formatting.None);

			// prepare sorting expression
			var sort = expressionJson.Get<JObject>("SortBy")?.ToSort<Category>() ?? Sorts<Category>.Ascending("OrderIndex").ThenByAscending("Title");
			var sortBy = new JObject
			{
				{ "API", sort.ToJson().ToString(Formatting.None) },
				{ "App", sort.ToClientJson().ToString(Formatting.None) }
			};

			// prepare cache
			if (requestInfo.GetParameter("x-no-cache") != null || requestInfo.GetParameter("x-force-cache") != null)
				await Utility.Cache.RemoveAsync(new[] { Extensions.GetCacheKeyOfTotalObjects(filter, sort), Extensions.GetCacheKey(filter, sort, pageSize, pageNumber) }, cancellationToken).ConfigureAwait(false);

			// search
			var showThumbnails = options.Get("ShowThumbnails", options.Get("ShowThumbnail", false)) || options.Get("ShowPngThumbnails", false) || options.Get("ShowAsPngThumbnails", false) || options.Get("ShowBigThumbnails", false) || options.Get("ShowAsBigThumbnails", false);
			var pngThumbnails = options.Get("ThumbnailsAsPng", options.Get("ThumbnailAsPng", options.Get("ShowPngThumbnails", options.Get("ShowAsPngThumbnails", false))));
			var bigThumbnails = options.Get("ThumbnailsAsBig", options.Get("ThumbnailAsBig", options.Get("ShowBigThumbnails", options.Get("ShowAsBigThumbnails", false))));
			var thumbnailsWidth = options.Get("ThumbnailsWidth", options.Get("ThumbnailWidth", 0));
			var thumbnailsHeight = options.Get("ThumbnailsHeight", options.Get("ThumbnailHeight", 0));

			var results = await requestInfo.SearchAsync(null, filter, sort, pageSize, pageNumber, contentTypeID, -1, cancellationToken).ConfigureAwait(false);
			var totalRecords = results.Item1;
			var objects = results.Item2;
			var thumbnails = results.Item3;

			// build response
			var level = options.Get("Level", 1);
			var maxLevel = options.Get("MaxLevel", 0);
			var addChildren = options.Get("ShowChildrens", options.Get("ShowChildren", options.Get("AddChildrens", options.Get("AddChildren", false))));

			if (addChildren)
				await objects.Where(category => category._childrenIDs == null).ForEachAsync(category => category.FindChildrenAsync(cancellationToken), true, false).ConfigureAwait(false);

			var categories = objects.Select(category => category.ToJson(
				addChildren,
				false,
				json =>
				{
					json.Remove("Privileges");
					json.Remove("Notifications");
					json.Remove("EmailSettings");
					json["Summary"] = category.Description?.NormalizeHTMLBreaks();
					json["URL"] = category.GetURL(desktop);
					json["ThumbnailURL"] = thumbnails?.GetThumbnailURL(category.ID, pngThumbnails, bigThumbnails, thumbnailsWidth, thumbnailsHeight);
				},
				async (json, cat) =>
				{
					json.Remove("Privileges");
					json.Remove("Notifications");
					json.Remove("EmailSettings");
					json["Summary"] = cat.Description?.NormalizeHTMLBreaks();
					json["URL"] = cat.GetURL(desktop);
					if (showThumbnails)
						await json.GenerateThumbnailURLAsync(cat, requestInfo, cancellationToken, pngThumbnails, bigThumbnails, thumbnailsWidth, thumbnailsHeight).ConfigureAwait(false);
				},
				level,
				maxLevel
			)).ToJArray();

			// response
			return new JObject
			{
				{ "Data", categories },
				{ "FilterBy", filterBy },
				{ "SortBy", sortBy }
			};
		}

		static async Task GenerateThumbnailURLAsync(this JObject json, Category category, RequestInfo requestInfo, CancellationToken cancellationToken, bool pngThumbnails = false, bool bigThumbnails = false, int thumbnailsWidth = 0, int thumbnailsHeight = 0)
		{
			try
			{
				var thumbs = await requestInfo.GetThumbnailsAsync(category.ID, category.Title.Url64Encode(), Utility.ValidationKey, cancellationToken).ConfigureAwait(false);
				json["ThumbnailURL"] = thumbs?.GetThumbnailURL(category.ID, pngThumbnails, bigThumbnails, thumbnailsWidth, thumbnailsHeight) ?? "";
			}
			catch (Exception ex)
			{
				await requestInfo.WriteErrorAsync(ex, $"Error occurred while fetching thumbnails of a category => {ex.Message}").ConfigureAwait(false);
			}
		}

		internal static async Task<JObject> GenerateMenuAsync(this RequestInfo requestInfo, Category category, string thumbnailURL, int level, int maxLevel = 0, bool pngThumbnails = false, bool bigThumbnails = false, int thumbnailsWidth = 0, int thumbnailsHeight = 0, CancellationToken cancellationToken = default)
		{
			// generate the menu item
			var url = category.GetURL();
			var menu = new JObject
			{
				{ "ID", category.ID },
				{ "Title", category.Title },
				{ "Description", category.Description?.NormalizeHTMLBreaks() },
				{ "Image", thumbnailURL },
				{ "URL", url },
				{ "Target", null },
				{ "Level", level },
				{ "Selected", false }
			};

			// generate children
			JArray subMenu = null;
			if (maxLevel < 1 || level < maxLevel)
			{
				// get children
				if (category._childrenIDs == null)
				{
					await category.FindChildrenAsync(cancellationToken, false).ConfigureAwait(false);
					await category.SetAsync(false, true, cancellationToken).ConfigureAwait(false);
				}

				// generate children
				var children = category.Children;
				if (children.Any())
				{
					var thumbnails = children.Count == 1
						? await requestInfo.GetThumbnailsAsync(children[0].ID, children[0].Title.Url64Encode(), Utility.ValidationKey, cancellationToken).ConfigureAwait(false)
						: await requestInfo.GetThumbnailsAsync(children.Select(child => child.ID).Join(","), children.ToJObject("ID", child => new JValue(child.Title.Url64Encode())).ToString(Formatting.None), Utility.ValidationKey, cancellationToken).ConfigureAwait(false);
					subMenu = new JArray();
					await children.ForEachAsync(async child => subMenu.Add(await requestInfo.GenerateMenuAsync(child, thumbnails?.GetThumbnailURL(child.ID, pngThumbnails, bigThumbnails, thumbnailsWidth, thumbnailsHeight), level + 1, maxLevel, pngThumbnails, bigThumbnails, thumbnailsWidth, thumbnailsHeight, cancellationToken).ConfigureAwait(false)), true, false).ConfigureAwait(false);
				}

				// update children
				if (subMenu != null && subMenu.Any())
					menu["SubMenu"] = new JObject
					{
						{ "Menu", subMenu }
					};
			}

			// return the menu item
			return menu;
		}

		internal static async Task<JObject> SyncCategoryAsync(this RequestInfo requestInfo, CancellationToken cancellationToken, bool sendNotifications = false, bool dontCreateNewVersion = false)
		{
			var @event = requestInfo.GetParameter("event") ?? requestInfo.GetParameter("x-original-event");
			if (string.IsNullOrWhiteSpace(@event) || !@event.IsEquals("Delete"))
				@event = "Update";

			var data = requestInfo.GetBodyExpando();
			var category = await data.Get<string>("ID").GetCategoryByIDAsync(cancellationToken).ConfigureAwait(false);
			var oldAlias = category?.Alias;

			if (!@event.IsEquals("Delete"))
			{
				if (category == null)
				{
					category = Category.CreateInstance(data);
					category.Extras = data.Get<string>("Extras") ?? category.Extras;
					await Category.CreateAsync(category, cancellationToken).ConfigureAwait(false);
				}
				else
				{
					category.Fill(data);
					category.Extras = data.Get<string>("Extras") ?? category.Extras;
					await Category.UpdateAsync(category, dontCreateNewVersion, cancellationToken).ConfigureAwait(false);
				}
			}
			else if (category != null)
				await Category.DeleteAsync<Category>(category.ID, category.LastModifiedID, cancellationToken).ConfigureAwait(false);

			// update cache
			await category.ClearRelatedCacheAsync(cancellationToken, requestInfo.CorrelationID).ConfigureAwait(false);
			if (@event.IsEquals("Delete"))
				await Utility.Cache.RemoveSetMemberAsync(category.ContentType.ObjectCacheKeys, category.GetCacheKey(), cancellationToken).ConfigureAwait(false);
			else
				await Utility.Cache.AddSetMemberAsync(category.ContentType.ObjectCacheKeys, category.GetCacheKey(), cancellationToken).ConfigureAwait(false);

			// send notifications
			if (sendNotifications)
				await category.SendNotificationAsync(@event, category.ContentType.Notifications, category.Status, category.Status, requestInfo, cancellationToken).ConfigureAwait(false);

			// send update messages
			var json = @event.IsEquals("Delete")
				? category.Remove().ToJson()
				: category.Set(false, false, oldAlias).ToJson();
			var objectName = category.GetObjectName();
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

		internal static async Task<JObject> RollbackCategoryAsync(this RequestInfo requestInfo, bool isSystemAdministrator, CancellationToken cancellationToken)
		{
			// prepare
			var category = await (requestInfo.GetObjectIdentity() ?? "").GetCategoryByIDAsync(cancellationToken).ConfigureAwait(false);
			if (category == null)
				throw new InformationNotFoundException();
			else if (category.Organization == null || category.Module == null)
				throw new InformationInvalidException("The organization/module is invalid");

			// check permission
			var gotRights = isSystemAdministrator || requestInfo.Session.User.IsModerator(category.Module.WorkingPrivileges, null, category.Organization);
			if (!gotRights)
				throw new AccessDeniedException();

			// rollback
			var oldParentID = category.ParentID;
			var oldAlias = category.Alias;
			var oldStatus = category.Status;
			category = await RepositoryMediator.RollbackAsync<Category>(requestInfo.GetParameter("x-version-id") ?? "", requestInfo.Session.User.ID, cancellationToken).ConfigureAwait(false);
			category.Set(true, true, oldAlias);

			// update cache & send notification
			Task.WhenAll
			(
				category.ClearRelatedCacheAsync(cancellationToken, requestInfo.CorrelationID),
				category.UpdateRelatedOnUpdatedAsync(requestInfo, oldParentID, cancellationToken),
				category.SendNotificationAsync("Update", category.ContentType.Notifications, oldStatus, category.Status, requestInfo, cancellationToken)
			).Run();
			category.Organization.SendRefreshingTasks();

			// send update messages
			var objectName = category.GetObjectName();
			var response = category.ToJson(true, false);
			new CommunicateMessage(requestInfo.ServiceName)
			{
				Type = $"{objectName}#Update",
				Data = response,
				ExcludedNodeID = Utility.NodeID
			}.Send();
			if (category.ParentCategory == null)
			{
				var versions = await category.FindVersionsAsync(cancellationToken, false).ConfigureAwait(false);
				new UpdateMessage
				{
					Type = $"{requestInfo.ServiceName}#{objectName}#Update",
					Data = response.UpdateVersions(versions),
					DeviceID = "*"
				}.Send();
			}
			return response;
		}
	}
}
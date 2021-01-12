#region Related components
using System;
using System.Linq;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using System.Dynamic;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using net.vieapps.Components.Utility;
using net.vieapps.Components.Security;
using net.vieapps.Components.Repository;
#endregion

namespace net.vieapps.Services.Portals
{
	public static class CategoryProcessor
	{
		internal static ConcurrentDictionary<string, Category> Categories { get; } = new ConcurrentDictionary<string, Category>(StringComparer.OrdinalIgnoreCase);

		internal static ConcurrentDictionary<string, Category> CategoriesByAlias { get; } = new ConcurrentDictionary<string, Category>(StringComparer.OrdinalIgnoreCase);

		internal static HashSet<string> ExtraProperties { get; } = "Notifications,EmailSettings".ToHashSet();

		public static Category CreateCategoryInstance(this ExpandoObject data, string excluded = null, Action<Category> onCompleted = null)
			=> Category.CreateInstance(data, excluded?.ToHashSet(), onCompleted);

		public static Category UpdateCategoryInstance(this Category category, ExpandoObject data, string excluded = null, Action<Category> onCompleted = null)
			=> category.Fill(data, excluded?.ToHashSet(), onCompleted);

		internal static Category Set(this Category category, bool clear = false, bool updateCache = false)
		{
			if (category != null && !string.IsNullOrWhiteSpace(category.ID) && !string.IsNullOrWhiteSpace(category.Title))
			{
				if (clear)
					category.Remove();

				CategoryProcessor.Categories[category.ID] = category;
				CategoryProcessor.CategoriesByAlias[$"{category.RepositoryEntityID}:{category.Alias}"] = category;

				if (updateCache)
					Utility.Cache.SetAsync(category).Run();
			}
			return category;
		}

		internal static async Task<Category> SetAsync(this Category category, bool clear = false, bool updateCache = false, CancellationToken cancellationToken = default)
		{
			category?.Set(clear);
			await (updateCache && category != null && !string.IsNullOrWhiteSpace(category.ID) && !string.IsNullOrWhiteSpace(category.Title) ? Utility.Cache.SetAsync(category, cancellationToken) : Task.CompletedTask).ConfigureAwait(false);
			return category;
		}

		internal static Category Remove(this Category category)
			=> (category?.ID ?? "").RemoveCategory();

		internal static Category RemoveCategory(this string id)
		{
			if (!string.IsNullOrWhiteSpace(id) && CategoryProcessor.Categories.TryRemove(id, out var category) && category != null)
			{
				CategoryProcessor.CategoriesByAlias.Remove($"{category.RepositoryEntityID}:{category.Alias}");
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

			var category = CategoryProcessor.CategoriesByAlias.ContainsKey($"{repositoryEntityID}:{alias.NormalizeAlias()}")
				? CategoryProcessor.CategoriesByAlias[$"{repositoryEntityID}:{alias.NormalizeAlias()}"]
				: null;

			if (category == null && fetchRepository)
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
			await categories.ForEachAsync((category, token) => category.SetAsync(false, updateCache, token), cancellationToken).ConfigureAwait(false);
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
				var category = message.Data.ToExpandoObject().CreateCategoryInstance();
				category._childrenIDs = null;
				await category.FindChildrenAsync(cancellationToken, false).ConfigureAwait(false);
				category.Set();
			}

			else if (message.Type.IsEndsWith("#Update"))
			{
				var category = message.Data.Get("ID", "").GetCategoryByID(false, false);
				category = category == null
					? message.Data.ToExpandoObject().CreateCategoryInstance()
					: category.UpdateCategoryInstance(message.Data.ToExpandoObject());
				category._childrenIDs = null;
				await category.FindChildrenAsync(cancellationToken, false).ConfigureAwait(false);
				category.Set();
			}

			else if (message.Type.IsEndsWith("#Delete"))
				message.Data.ToExpandoObject().CreateCategoryInstance().Remove();
		}

		internal static async Task ClearRelatedCacheAsync(this Category category, CancellationToken cancellationToken, string correlationID = null)
		{
			// data cache keys
			var dataCacheKeys = Extensions.GetRelatedCacheKeys(category.GetCacheKey());
			if (category.ContentType != null)
			{
				var cacheKeys = await Utility.Cache.GetSetMembersAsync(category.ContentType.GetSetCacheKey(), cancellationToken).ConfigureAwait(false);
				if (cacheKeys != null && cacheKeys.Count > 0)
					dataCacheKeys = dataCacheKeys.Concat(cacheKeys).Concat(new[] { category.ContentType.GetSetCacheKey() }).ToList();
			}

			// data cache keys of the related links
			var links = await Link.FindAsync(Filters<Link>.And(Filters<Link>.Equals("SystemID", category.SystemID), Filters<Link>.Equals("LookupRepositoryID", category.RepositoryID)), Sorts<Link>.Ascending("ParentID").ThenByAscending("OrderIndex"), 0, 1, null, cancellationToken).ConfigureAwait(false);
			var linkContentTypes = new Dictionary<string, ContentType>();
			await links.ForEachAsync(async (link, _) =>
			{
				dataCacheKeys = dataCacheKeys.Concat(Extensions.GetRelatedCacheKeys(link.GetCacheKey())).ToList();
				if (link.ContentType != null && !linkContentTypes.ContainsKey(link.ContentType.ID))
				{
					linkContentTypes.Add(link.ContentType.ID, link.ContentType);
					var cacheKeys = await Utility.Cache.GetSetMembersAsync(link.ContentType.GetSetCacheKey(), cancellationToken).ConfigureAwait(false);
					if (cacheKeys != null && cacheKeys.Count > 0)
						dataCacheKeys = dataCacheKeys.Concat(cacheKeys).Concat(new[] { link.ContentType.GetSetCacheKey() }).ToList();
				}
			}, cancellationToken, true, false).ConfigureAwait(false);

			dataCacheKeys = dataCacheKeys.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

			// html cache keys (desktop HTMLs) that related to links
			var htmlCacheKeys = category.Organization?.GetDesktopCacheKey() ?? new List<string>();
			await linkContentTypes.ForEachAsync(async (linkContentType, _) =>
			{
				var desktopSetCacheKeys = await linkContentType.GetSetCacheKeysAsync(cancellationToken).ConfigureAwait(false);
				await desktopSetCacheKeys.ForEachAsync(async (desktopSetCacheKey, __) =>
				 {
					 var cacheKeys = await Utility.Cache.GetSetMembersAsync(desktopSetCacheKey, cancellationToken).ConfigureAwait(false);
					 if (cacheKeys != null && cacheKeys.Count > 0)
						 htmlCacheKeys = htmlCacheKeys.Concat(cacheKeys).Concat(new[] { desktopSetCacheKey }).ToList();
				 }, cancellationToken, true, false).ConfigureAwait(false);
			}, cancellationToken, true, false).ConfigureAwait(false);

			htmlCacheKeys = htmlCacheKeys.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

			// remove related cache
			await Utility.Cache.RemoveAsync(htmlCacheKeys.Concat(dataCacheKeys).Distinct(StringComparer.OrdinalIgnoreCase).ToList(), cancellationToken).ConfigureAwait(false);
			await Task.WhenAll
			(
				Utility.WriteCacheLogs ? Utility.WriteLogAsync(correlationID, $"Clear related cache of a CMS category [{category.Title} - ID: {category.ID}]\r\n- {dataCacheKeys.Count} data keys => {dataCacheKeys.Join(", ")}\r\n- {htmlCacheKeys.Count} html keys => {htmlCacheKeys.Join(", ")}", CancellationToken.None, "Caches") : Task.CompletedTask,
				category.GetURL().Replace("~/", $"{Utility.PortalsHttpURI}/~{category.Organization.Alias}/").RefreshWebPageAsync(1, correlationID, $"Refresh desktop when related cache of a CMS category was clean [{category.Title} - ID: {category.ID}]"),
				$"{Utility.PortalsHttpURI}/~{category.Organization.Alias}/".RefreshWebPageAsync(1, correlationID, $"Refresh desktop when related cache of a CMS category was clean [{category.Title} - ID: {category.ID}]")
			).ConfigureAwait(false);
		}

		internal static Task ClearRelatedCacheAsync(this Category category, string correlationID = null)
			=> category.ClearRelatedCacheAsync(CancellationToken.None, correlationID);

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
					: await Category.SearchAsync(query, filter, pageSize, pageNumber, contentTypeID, cancellationToken).ConfigureAwait(false)
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
				Utility.SetCacheOfPageSizeAsync(filter, sort, pageSize, cancellationToken).Run();

			// return the results
			return new Tuple<long, List<Category>, JToken, List<string>>(totalRecords, objects, thumbnails, cacheKeys);
		}

		internal static async Task<JObject> SearchCategoriesAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, CancellationToken cancellationToken = default)
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
			var organizationID = filter.GetValue("SystemID") ?? requestInfo.GetParameter("x-system-id");
			var organization = await (organizationID ?? "").GetOrganizationByIDAsync(cancellationToken).ConfigureAwait(false);
			if (organization == null)
				throw new InformationExistedException("The organization is invalid");

			var moduleID = filter.GetValue("RepositoryID") ?? requestInfo.GetParameter("x-module-id");
			var module = await (moduleID ?? "").GetModuleByIDAsync(cancellationToken).ConfigureAwait(false);
			if (module == null || !module.SystemID.IsEquals(organization.ID))
				throw new InformationInvalidException("The module is invalid");

			var contentTypeID = filter.GetValue("RepositoryEntityID") ?? requestInfo.GetParameter("x-content-type-id");

			// check permission
			var gotRights = isSystemAdministrator || requestInfo.Session.User.ID.IsEquals(organization.OwnerID) || requestInfo.Session.User.IsViewer(module.WorkingPrivileges);
			if (!gotRights)
				throw new AccessDeniedException();

			// process cache
			var addChildren = "true".IsEquals(requestInfo.GetHeaderParameter("x-children"));
			var cacheKeyOfObjectsJson = string.IsNullOrWhiteSpace(query) ? Extensions.GetCacheKeyOfObjectsJson(filter, sort, pageSize, pageNumber) : null;
			if (cacheKeyOfObjectsJson != null && !addChildren)
			{
				var json = await Utility.Cache.GetAsync<string>(cacheKeyOfObjectsJson, cancellationToken).ConfigureAwait(false);
				if (!string.IsNullOrWhiteSpace(json))
					return JObject.Parse(json);
			}

			// search if has no cache
			var results = await requestInfo.SearchAsync(query, filter, sort, pageSize, pageNumber, contentTypeID, pagination.Item1 > -1 ? pagination.Item1 : -1, cancellationToken).ConfigureAwait(false);
			var totalRecords = results.Item1;
			var objects = results.Item2;
			var thumbnails = results.Item3;

			// build response
			var totalPages = new Tuple<long, int>(totalRecords, pageSize).GetTotalPages();
			if (totalPages > 0 && pageNumber > totalPages)
				pageNumber = totalPages;
			pagination = new Tuple<long, int, int, int>(totalRecords, totalPages, pageSize, pageNumber);

			if (addChildren)
				await objects.Where(category => category._childrenIDs == null).ForEachAsync((category, _) => category.FindChildrenAsync(cancellationToken), cancellationToken, true, false).ConfigureAwait(false);

			var response = new JObject()
			{
				{ "FilterBy", filter.ToClientJson(query) },
				{ "SortBy", sort?.ToClientJson() },
				{ "Pagination", pagination.GetPagination() },
				{ "Objects", objects.Select(category => category.ToJson(addChildren, false)).ToJArray() }
			};

			// update cache
			if (string.IsNullOrWhiteSpace(query) && !addChildren)
				await Utility.Cache.SetAsync(cacheKeyOfObjectsJson, response.ToString(Formatting.None)).ConfigureAwait(false);

			// response
			return response;
		}

		internal static async Task<JObject> CreateCategoryAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, CancellationToken cancellationToken = default)
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
			var gotRights = isSystemAdministrator || requestInfo.Session.User.ID.IsEquals(organization.OwnerID) || requestInfo.Session.User.IsModerator(module.WorkingPrivileges);
			if (!gotRights)
				throw new AccessDeniedException();

			// gathering information
			var category = request.CreateCategoryInstance("SystemID,RepositoryID,RepositoryEntityID,Privileges,OrderIndex,Created,CreatedID,LastModified,LastModifiedID", obj =>
			{
				obj.SystemID = organization.ID;
				obj.RepositoryID = module.ID;
				obj.RepositoryEntityID = contentType.ID;
				obj.ParentID = obj.ParentCategory != null ? obj.ParentID : null;
				obj.ID = string.IsNullOrWhiteSpace(obj.ID) || !obj.ID.IsValidUUID() ? UtilityService.NewUUID : obj.ID;
				obj.Created = obj.LastModified = DateTime.Now;
				obj.CreatedID = obj.LastModifiedID = requestInfo.Session.User.ID;
				obj.NormalizeExtras();
				obj._childrenIDs = new List<string>();
			});

			category.Alias = string.IsNullOrWhiteSpace(category.Alias) ? category.Title.NormalizeAlias() : category.Alias.NormalizeAlias();
			var existing = await contentType.ID.GetCategoryByAliasAsync(category.Alias, cancellationToken).ConfigureAwait(false);
			if (existing != null)
				throw new InformationExistedException($"The alias ({category.Alias}) was used by another category");

			category.OrderIndex = (await CategoryProcessor.GetLastOrderIndexAsync(category.SystemID, category.RepositoryID, category.RepositoryEntityID, category.ParentID, cancellationToken).ConfigureAwait(false)) + 1;

			// create new
			await Category.CreateAsync(category, cancellationToken).ConfigureAwait(false);
			category.Set().ClearRelatedCacheAsync(requestInfo.CorrelationID).Run();

			var updateMessages = new List<UpdateMessage>();
			var communicateMessages = new List<CommunicateMessage>();
			var objectName = category.GetObjectName();

			// update parent
			var parentCategory = category.ParentCategory;
			if (parentCategory != null)
			{
				await Utility.Cache.RemoveAsync(Extensions.GetRelatedCacheKeys(CategoryProcessor.GetCategoriesFilter(category.SystemID, category.RepositoryID, category.RepositoryEntityID, category.ParentID), Sorts<Category>.Ascending("OrderIndex").ThenByAscending("Title")), cancellationToken).ConfigureAwait(false);
				await parentCategory.FindChildrenAsync(cancellationToken, false).ConfigureAwait(false);
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
			await Task.WhenAll(
				updateMessages.ForEachAsync((message, token) => Utility.RTUService.SendUpdateMessageAsync(message, token), cancellationToken, true, false),
				communicateMessages.ForEachAsync((message, token) => Utility.RTUService.SendInterCommunicateMessageAsync(message, token), cancellationToken)
			).ConfigureAwait(false);

			// send notification
			category.SendNotificationAsync("Create", category.ContentType.Notifications, ApprovalStatus.Draft, category.Status, requestInfo, cancellationToken).Run();

			// response
			return response;
		}

		internal static async Task<JObject> GetCategoryAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, CancellationToken cancellationToken = default)
		{
			// prepare
			var identity = requestInfo.GetObjectIdentity(true, true) ?? "";
			var contentTypeID = requestInfo.GetParameter("RepositoryEntityID") ?? requestInfo.GetParameter("x-content-type-id") ?? "";

			var category = await (identity.IsValidUUID() ? identity.GetCategoryByIDAsync(cancellationToken) : contentTypeID.GetCategoryByAliasAsync(identity, cancellationToken)).ConfigureAwait(false);
			if (category == null)
				throw new InformationNotFoundException();
			else if (category.Organization == null || category.Module == null)
				throw new InformationInvalidException("The organization/module is invalid");

			// check permission
			var gotRights = isSystemAdministrator || requestInfo.Session.User.ID.IsEquals(category.Organization.OwnerID) || requestInfo.Session.User.IsViewer(category.Module.WorkingPrivileges);
			if (!gotRights)
				throw new AccessDeniedException();

			if (!identity.IsValidUUID())
				return new JObject
				{
					{ "ID", category.ID },
					{ "Title", category.Title },
					{ "Alias", category.Alias }
				};

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
				await Task.WhenAll(
					category.SetAsync(false, true, cancellationToken),
					Utility.RTUService.SendInterCommunicateMessageAsync(new CommunicateMessage(requestInfo.ServiceName)
					{
						Type = $"{category.GetObjectName()}#Update",
						Data = category.ToJson(false, false),
						ExcludedNodeID = Utility.NodeID
					}, cancellationToken)
				).ConfigureAwait(false);
			}

			// send update message
			var objectName = category.GetObjectName(); 
			var response = category.ToJson(true, false);

			await Utility.RTUService.SendUpdateMessageAsync(new UpdateMessage
			{
				Type = $"{requestInfo.ServiceName}#{objectName}#Update",
				Data = response,
				DeviceID = "*",
				ExcludedDeviceID = isRefresh ? "" : requestInfo.Session.DeviceID
			}, cancellationToken).ConfigureAwait(false);
			if (isRefresh)
				await Utility.RTUService.SendInterCommunicateMessageAsync(new CommunicateMessage(requestInfo.ServiceName)
				{
					Type = $"{objectName}#Update",
					Data = response,
					ExcludedNodeID = Utility.NodeID
				}, cancellationToken).ConfigureAwait(false);

			// response
			return response;
		}

		internal static async Task<JObject> UpdateCategoryAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, CancellationToken cancellationToken = default)
		{
			// prepare
			var category = await (requestInfo.GetObjectIdentity() ?? "").GetCategoryByIDAsync(cancellationToken).ConfigureAwait(false);
			if (category == null)
				throw new InformationNotFoundException();
			else if (category.Organization == null || category.Module == null)
				throw new InformationInvalidException("The organization/module is invalid");

			// check permission
			var gotRights = isSystemAdministrator || requestInfo.Session.User.ID.IsEquals(category.Organization.OwnerID) || requestInfo.Session.User.IsModerator(category.Module.WorkingPrivileges);
			if (!gotRights)
				throw new AccessDeniedException();

			var oldParentID = category.ParentID;
			var oldAlias = category.Alias;
			var oldStatus = category.Status;
			category.UpdateCategoryInstance(requestInfo.GetBodyExpando(), "ID,SystemID,RepositoryID,RepositoryEntityID,Privileges,OrderIndex,Created,CreatedID,LastModified,LastModifiedID", obj =>
			{
				obj.Alias = string.IsNullOrWhiteSpace(obj.Alias) ? oldAlias : obj.Alias.NormalizeAlias();
				obj.LastModified = DateTime.Now;
				obj.LastModifiedID = requestInfo.Session.User.ID;
				obj.NormalizeExtras();
			});

			if (!category.Alias.IsEquals(oldAlias))
			{
				var existing = await category.RepositoryEntityID.GetCategoryByAliasAsync(category.Alias, cancellationToken).ConfigureAwait(false);
				if (existing != null && !existing.ID.Equals(category.ID))
					throw new InformationExistedException($"The alias ({category.Alias}) was used by another category");
			}

			if (category.ParentCategory != null && !category.ParentID.IsEquals(oldParentID))
			{
				category.OrderIndex = (await CategoryProcessor.GetLastOrderIndexAsync(category.SystemID, category.RepositoryID, category.RepositoryEntityID, category.ParentID, cancellationToken).ConfigureAwait(false)) + 1;
				await category.FindChildrenAsync(cancellationToken, false).ConfigureAwait(false);
			}

			// update
			await Category.UpdateAsync(category, requestInfo.Session.User.ID, cancellationToken).ConfigureAwait(false);
			category.Set().ClearRelatedCacheAsync(cancellationToken, requestInfo.CorrelationID).Run();

			var updateMessages = new List<UpdateMessage>();
			var communicateMessages = new List<CommunicateMessage>();
			var objectName = category.GetObjectName();

			// update parent
			var parentCategory = category.ParentCategory;
			if (parentCategory != null && !category.ParentID.IsEquals(oldParentID))
			{
				await Utility.Cache.RemoveAsync(Extensions.GetRelatedCacheKeys(CategoryProcessor.GetCategoriesFilter(category.SystemID, category.RepositoryID, category.RepositoryEntityID, category.ParentID), Sorts<Category>.Ascending("OrderIndex").ThenByAscending("Title")), cancellationToken).ConfigureAwait(false);
				await parentCategory.FindChildrenAsync(cancellationToken, false).ConfigureAwait(false);
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

			// update old parent
			if (!string.IsNullOrWhiteSpace(oldParentID) && !oldParentID.IsEquals(category.ParentID))
			{
				parentCategory = await oldParentID.GetCategoryByIDAsync(cancellationToken).ConfigureAwait(false);
				if (parentCategory != null)
				{
					await Utility.Cache.RemoveAsync(Extensions.GetRelatedCacheKeys(CategoryProcessor.GetCategoriesFilter(category.SystemID, category.RepositoryID, category.RepositoryEntityID, parentCategory.ID), Sorts<Category>.Ascending("OrderIndex").ThenByAscending("Title")), cancellationToken).ConfigureAwait(false);
					await parentCategory.FindChildrenAsync(cancellationToken, false).ConfigureAwait(false);
					parentCategory._childrenIDs.Remove(category.ID);
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
			}

			// message to update to all other connected clients
			var response = category.ToJson(true, false);
			if (category.ParentCategory == null)
				updateMessages.Add(new UpdateMessage
				{
					Type = $"{requestInfo.ServiceName}#{objectName}#Update",
					Data = response,
					DeviceID = "*"
				});

			// message to update to all service instances (on all other nodes)
			communicateMessages.Add(new CommunicateMessage(requestInfo.ServiceName)
			{
				Type = $"{objectName}#Update",
				Data = response,
				ExcludedNodeID = Utility.NodeID
			});

			// send update messages
			await Task.WhenAll(
				updateMessages.ForEachAsync((message, token) => Utility.RTUService.SendUpdateMessageAsync(message, token), cancellationToken, true, false),
				communicateMessages.ForEachAsync((message, token) => Utility.RTUService.SendInterCommunicateMessageAsync(message, token), cancellationToken)
			).ConfigureAwait(false);

			// send notification
			category.SendNotificationAsync("Update", category.ContentType.Notifications, oldStatus, category.Status, requestInfo, cancellationToken).Run();

			// response
			return response;
		}

		internal static async Task<JObject> UpdateCategoriesAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, CancellationToken cancellationToken = default)
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
			var gotRights = isSystemAdministrator || requestInfo.Session.User.ID.IsEquals(organization.OwnerID) || requestInfo.Session.User.IsModerator(module.WorkingPrivileges);
			if (!gotRights)
				throw new AccessDeniedException();

			// update
			var updateMessages = new List<UpdateMessage>();
			var communicateMessages = new List<CommunicateMessage>();
			var objectName = category != null
				? category.GetObjectName()
				: typeof(Category).GetTypeName(true);

			var items = category != null
				? category.Children
				: await organization.ID.FindCategoriesAsync(module.ID, contentType.ID, null, cancellationToken, false).ConfigureAwait(false);

			Category first = null;
			var notificationTasks = new List<Task>();
			await request.Get<JArray>("Categories").ForEachAsync(async (info, _) =>
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
					notificationTasks.Add(item.SendNotificationAsync("Update", item.ContentType.Notifications, item.Status, item.Status, requestInfo, cancellationToken));
					var json = item.Set(false, true).ToJson(true, false);
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

					first = first ?? item;
				}
			});

			if (category != null)
			{
				await category.ClearRelatedCacheAsync(cancellationToken, requestInfo.CorrelationID).ConfigureAwait(false);
				category._childrenIDs = null;
				await category.FindChildrenAsync(cancellationToken, false).ConfigureAwait(false);
				var json = category.Set(false, true).ToJson(true, false);
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
			else if (first != null)
				first.ClearRelatedCacheAsync(requestInfo.CorrelationID).Run();

			// send update messages
			await Task.WhenAll
			(
				updateMessages.ForEachAsync((message, token) => Utility.RTUService.SendUpdateMessageAsync(message, token), cancellationToken, true, false),
				communicateMessages.ForEachAsync((message, token) => Utility.RTUService.SendInterCommunicateMessageAsync(message, token), cancellationToken)
			).ConfigureAwait(false);

			// send notification
			Task.WhenAll(notificationTasks).Run();

			// response
			return new JObject();
		}

		internal static async Task<JObject> DeleteCategoryAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, CancellationToken cancellationToken = default)
		{
			// prepare
			var category = await (requestInfo.GetObjectIdentity() ?? "").GetCategoryByIDAsync(cancellationToken).ConfigureAwait(false);
			if (category == null)
				throw new InformationNotFoundException();
			else if (category.Organization == null || category.Module == null)
				throw new InformationInvalidException("The organization/module is invalid");

			// check permission
			var gotRights = isSystemAdministrator || requestInfo.Session.User.ID.IsEquals(category.Organization.OwnerID) || requestInfo.Session.User.IsModerator(category.Module.WorkingPrivileges);
			if (!gotRights)
				throw new AccessDeniedException();

			var updateMessages = new List<UpdateMessage>();
			var communicateMessages = new List<CommunicateMessage>();
			var objectName = category.GetObjectName();
			var updateChildren = requestInfo.Header.TryGetValue("x-children", out var childrenMode) && "set-null".IsEquals(childrenMode);

			// delete children
			var children = await category.FindChildrenAsync(cancellationToken, false).ConfigureAwait(false);
			await children.ForEachAsync(async (child, _) =>
			{
				// update children to root
				if (updateChildren)
				{
					child.ParentID = null;
					child.LastModified = DateTime.Now;
					child.LastModifiedID = requestInfo.Session.User.ID;

					await Category.UpdateAsync(child, requestInfo.Session.User.ID, cancellationToken).ConfigureAwait(false);
					child.SendNotificationAsync("Update", child.ContentType.Notifications, child.Status, child.Status, requestInfo, cancellationToken).Run();

					var json = child.Set().ToJson(true, false);
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

				// delete children
				else
				{
					// delete files
					await requestInfo.DeleteFilesAsync(child.SystemID, child.RepositoryEntityID, child.ID, Utility.ValidationKey, cancellationToken).ConfigureAwait(false);

					// delete objects
					var messages = await child.DeleteChildrenAsync(requestInfo, cancellationToken).ConfigureAwait(false);
					updateMessages = updateMessages.Concat(messages.Item1).ToList();
					communicateMessages = communicateMessages.Concat(messages.Item2).ToList();
				}
			}, cancellationToken, true, false).ConfigureAwait(false);

			// delete files of category
			await requestInfo.DeleteFilesAsync(category.SystemID, category.RepositoryEntityID, category.ID, Utility.ValidationKey, cancellationToken).ConfigureAwait(false);

			// delete vs
			await Category.DeleteAsync<Category>(category.ID, requestInfo.Session.User.ID, cancellationToken).ConfigureAwait(false);
			await category.Remove().ClearRelatedCacheAsync(requestInfo.CorrelationID).ConfigureAwait(false);

			// message to update to all other connected clients
			var response = category.ToJson();
			updateMessages.Add(new UpdateMessage
			{
				Type = $"{requestInfo.ServiceName}#{objectName}#Delete",
				Data = response,
				DeviceID = "*"
			});

			// message to update to all service instances (on all other nodes)
			communicateMessages.Add(new CommunicateMessage(requestInfo.ServiceName)
			{
				Type = $"{objectName}#Delete",
				Data = response,
				ExcludedNodeID = Utility.NodeID
			});

			// send update messages
			await Task.WhenAll(
				updateMessages.ForEachAsync((message, token) => Utility.RTUService.SendUpdateMessageAsync(message, token), cancellationToken, true, false),
				communicateMessages.ForEachAsync((message, token) => Utility.RTUService.SendInterCommunicateMessageAsync(message, token), cancellationToken)
			).ConfigureAwait(false);

			// send notification
			category.SendNotificationAsync("Delete", category.ContentType.Notifications, category.Status, category.Status, requestInfo, cancellationToken).Run();

			// response
			return response;
		}

		static async Task<Tuple<List<UpdateMessage>, List<CommunicateMessage>>> DeleteChildrenAsync(this Category category, RequestInfo requestInfo, CancellationToken cancellationToken = default)
		{
			var updateMessages = new List<UpdateMessage>();
			var communicateMessages = new List<CommunicateMessage>();
			var entityDefinition = RepositoryMediator.GetEntityDefinition<Category>();
			var objectName = $"{(string.IsNullOrWhiteSpace(entityDefinition.ObjectNamePrefix) ? "" : entityDefinition.ObjectNamePrefix)}{entityDefinition.ObjectName}{(string.IsNullOrWhiteSpace(entityDefinition.ObjectNameSuffix) ? "" : entityDefinition.ObjectNameSuffix)}";

			var children = await category.FindChildrenAsync(cancellationToken, false).ConfigureAwait(false);
			await children.ForEachAsync(async (child, token) =>
			{
				var messages = await child.DeleteChildrenAsync(requestInfo, token).ConfigureAwait(false);
				updateMessages = updateMessages.Concat(messages.Item1).ToList();
				communicateMessages = communicateMessages.Concat(messages.Item2).ToList();
			}, cancellationToken, true, false).ConfigureAwait(false);

			await Category.DeleteAsync<Category>(category.ID, requestInfo.Session.User.ID, cancellationToken).ConfigureAwait(false);
			category.Remove().ClearRelatedCacheAsync(cancellationToken).Run();
			category.SendNotificationAsync("Delete", category.ContentType.Notifications, category.Status, category.Status, requestInfo, cancellationToken).Run();

			var json = category.ToJson();
			updateMessages.Add(new UpdateMessage
			{
				Type = $"{requestInfo.ServiceName}#{objectName}#Delete",
				Data = json,
				DeviceID = "*"
			});
			communicateMessages.Add(new CommunicateMessage(requestInfo.ServiceName)
			{
				Type = $"{objectName}#Delete",
				Data = json,
				ExcludedNodeID = Utility.NodeID
			});
			return new Tuple<List<UpdateMessage>, List<CommunicateMessage>>(updateMessages, communicateMessages);
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
			var pageSize = paginationJson.Get<int>("PageSize", 7);
			var pageNumber = paginationJson.Get<int>("PageNumber", 1);

			var contentTypeID = contentTypeJson.Get<string>("ID");
			var parentID = requestJson.Get<string>("ParentIdentity");

			var desktop = desktopsJson.Get<string>("Specified");
			desktop = !string.IsNullOrWhiteSpace(desktop) ? desktop : desktopsJson.Get<string>("ContentType");
			desktop = !string.IsNullOrWhiteSpace(desktop) ? desktop : desktopsJson.Get<string>("Module");
			desktop = !string.IsNullOrWhiteSpace(desktop) ? desktop : desktopsJson.Get<string>("Default");

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
				await objects.Where(category => category._childrenIDs == null).ForEachAsync((category, _) => category.FindChildrenAsync(cancellationToken), cancellationToken, true, false).ConfigureAwait(false);

			var categories = objects.Select(category => category.ToJson(
				addChildren,
				false,
				json =>
				{
					json["Summary"] = category.Description?.NormalizeHTMLBreaks();
					json["URL"] = category.GetURL(desktop);
					json["ThumbnailURL"] = thumbnails?.GetThumbnailURL(category.ID, pngThumbnails, bigThumbnails, thumbnailsWidth, thumbnailsHeight);
					json.Remove("Privileges");
					json.Remove("Notifications");
					json.Remove("EmailSettings");
				},
				async json =>
				{
					var cat = await json.Get<string>("ID", "").GetCategoryByIDAsync(cancellationToken).ConfigureAwait(false);
					if (cat != null)
					{
						var thumbs = await requestInfo.GetThumbnailsAsync(cat.ID, cat.Title.Url64Encode(), Utility.ValidationKey, cancellationToken).ConfigureAwait(false);
						json["Summary"] = cat.Description?.NormalizeHTMLBreaks();
						json["URL"] = cat.GetURL(desktop);
						json["ThumbnailURL"] = thumbs?.GetThumbnailURL(cat.ID, pngThumbnails, bigThumbnails, thumbnailsWidth, thumbnailsHeight) ?? "";
						json.Remove("Privileges");
						json.Remove("Notifications");
						json.Remove("EmailSettings");
					}
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
				if (children.Count > 0)
				{
					var thumbnails = children.Count == 1
						? await requestInfo.GetThumbnailsAsync(children[0].ID, children[0].Title.Url64Encode(), Utility.ValidationKey, cancellationToken).ConfigureAwait(false)
						: await requestInfo.GetThumbnailsAsync(children.Select(child => child.ID).Join(","), children.ToJObject("ID", child => new JValue(child.Title.Url64Encode())).ToString(Formatting.None), Utility.ValidationKey, cancellationToken).ConfigureAwait(false);
					subMenu = new JArray();
					await children.ForEachAsync(async (child, token) => subMenu.Add(await requestInfo.GenerateMenuAsync(child, thumbnails?.GetThumbnailURL(child.ID, pngThumbnails, bigThumbnails, thumbnailsWidth, thumbnailsHeight), level + 1, maxLevel, pngThumbnails, bigThumbnails, thumbnailsWidth, thumbnailsHeight, token).ConfigureAwait(false)), cancellationToken, true, false).ConfigureAwait(false);
				}

				// update children
				if (subMenu != null && subMenu.Count > 0)
					menu["SubMenu"] = new JObject
					{
						{ "Menu", subMenu }
					};
			}

			// return the menu item
			return menu;
		}

		internal static async Task<JObject> SyncCategoryAsync(this RequestInfo requestInfo, CancellationToken cancellationToken = default)
		{
			var data = requestInfo.GetBodyExpando();
			var category = await data.Get<string>("ID").GetCategoryByIDAsync(cancellationToken).ConfigureAwait(false);
			if (category == null)
			{
				category = Category.CreateInstance(data);
				category.NormalizeExtras();
				category.Extras = data.Get<string>("Extras") ?? category.Extras;
				await Category.CreateAsync(category, cancellationToken).ConfigureAwait(false);
			}
			else
			{
				category.Fill(data);
				category.NormalizeExtras();
				category.Extras = data.Get<string>("Extras") ?? category.Extras;
				await Category.UpdateAsync(category, true, cancellationToken).ConfigureAwait(false);
			}

			// clear related cache
			if (requestInfo.GetHeaderParameter("x-converter") == null)
				category.ClearRelatedCacheAsync(requestInfo.CorrelationID).Run();

			// send update messages
			var json = category.Set().ToJson();
			var objectName = category.GetObjectName();
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
				{ "ID", category.ID },
				{ "Type", objectName }
			};
		}
	}
}
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
			if (category != null)
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
			await (updateCache && category != null ? Utility.Cache.SetAsync(category, cancellationToken) : Task.CompletedTask).ConfigureAwait(false);
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

		public static IFilterBy<Category> GetCategoriesFilter(this string systemID, string repositoryID = null, string repositoryEntityID = null, string parentID = null)
		{
			var filter = Filters<Category>.And(Filters<Category>.Equals("SystemID", systemID));
			if (!string.IsNullOrWhiteSpace(repositoryID))
				filter.Add(Filters<Category>.Equals("RepositoryID", repositoryID));
			if (!string.IsNullOrWhiteSpace(repositoryEntityID))
				filter.Add(Filters<Category>.Equals("RepositoryEntityID", repositoryEntityID));
			filter.Add(string.IsNullOrWhiteSpace(parentID) ? Filters<Category>.IsNull("ParentID") : Filters<Category>.Equals("ParentID", parentID));
			return filter;
		}

		public static List<Category> FindCategories(this string systemID, string repositoryID = null, string repositoryEntityID = null, string parentID = null, bool updateCache = true)
		{
			if (string.IsNullOrWhiteSpace(systemID))
				return new List<Category>();
			var filter = systemID.GetCategoriesFilter(repositoryID, repositoryEntityID, parentID);
			var sort = Sorts<Category>.Ascending("OrderIndex").ThenByAscending("Title");
			var categories = Category.Find(filter, sort, 0, 1, Extensions.GetCacheKey(filter, sort, 0, 1));
			categories.ForEach(category => category.Set(false, updateCache));
			return categories;
		}

		public static async Task<List<Category>> FindCategoriesAsync(this string systemID, string repositoryID = null, string repositoryEntityID = null, string parentID = null, CancellationToken cancellationToken = default, bool updateCache = true)
		{
			if (string.IsNullOrWhiteSpace(systemID))
				return new List<Category>();
			var filter = systemID.GetCategoriesFilter(repositoryID, repositoryEntityID, parentID);
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
				await message.Data.ToExpandoObject().CreateCategoryInstance().SetAsync(true, false, cancellationToken).ConfigureAwait(false);

			else if (message.Type.IsEndsWith("#Update"))
			{
				var category = message.Data.Get("ID", "").GetCategoryByID(false, false);
				await (category == null ? message.Data.ToExpandoObject().CreateCategoryInstance() : category.UpdateCategoryInstance(message.Data.ToExpandoObject())).SetAsync(true, false, cancellationToken).ConfigureAwait(false);
			}

			else if (message.Type.IsEndsWith("#Delete"))
				message.Data.ToExpandoObject().CreateCategoryInstance().Remove();
		}

		static async Task ClearRelatedCacheAsync(this Category category, string oldParentID = null, IRTUService rtuService = null, CancellationToken cancellationToken = default)
		{
			var sort = Sorts<Category>.Ascending("OrderIndex").ThenByAscending("Title");
			var tasks = new List<Task>
			{
				Utility.Cache.RemoveAsync(Extensions.GetRelatedCacheKeys(category.SystemID.GetCategoriesFilter(category.RepositoryID, null, null), sort), cancellationToken),
				Utility.Cache.RemoveAsync(Extensions.GetRelatedCacheKeys(category.SystemID.GetCategoriesFilter(category.RepositoryID, category.RepositoryEntityID, null), sort), cancellationToken)
			};
			if (!string.IsNullOrWhiteSpace(category.ParentID) && category.ParentID.IsValidUUID())
			{
				tasks.Add(Utility.Cache.RemoveAsync(Extensions.GetRelatedCacheKeys(category.SystemID.GetCategoriesFilter(category.RepositoryID, null, category.ParentID), sort), cancellationToken));
				tasks.Add(Utility.Cache.RemoveAsync(Extensions.GetRelatedCacheKeys(category.SystemID.GetCategoriesFilter(category.RepositoryID, category.RepositoryEntityID, category.ParentID), sort), cancellationToken));
			}
			if (!string.IsNullOrWhiteSpace(oldParentID) && oldParentID.IsValidUUID())
			{
				tasks.Add(Utility.Cache.RemoveAsync(Extensions.GetRelatedCacheKeys(category.SystemID.GetCategoriesFilter(category.RepositoryID, null, oldParentID), sort), cancellationToken));
				tasks.Add(Utility.Cache.RemoveAsync(Extensions.GetRelatedCacheKeys(category.SystemID.GetCategoriesFilter(category.RepositoryID, category.RepositoryEntityID, oldParentID), sort), cancellationToken));
			}
			if (rtuService != null)
			{
				var links = await Link.FindAsync(Filters<Link>.And(Filters<Link>.Equals("SystemID", category.SystemID), Filters<Link>.Equals("LookupRepositoryID", category.RepositoryID)), Sorts<Link>.Ascending("ParentID").ThenByAscending("OrderIndex"), 0, 1, null, cancellationToken).ConfigureAwait(false);
				var objectName = typeof(Link).GetTypeName(true);
				links.Select(link => link.RepositoryEntityID).Distinct(StringComparer.OrdinalIgnoreCase).ForEach(contentTypeID => tasks.Add(rtuService.SendClearCacheRequestAsync(contentTypeID, objectName, cancellationToken)));
			}
			await Task.WhenAll(tasks).ConfigureAwait(false);
		}

		internal static async Task<JObject> SearchCategorysAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, CancellationToken cancellationToken = default)
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

			var moduleID = filter.GetValue("RepositoryID") ?? requestInfo.GetParameter("x-module");
			var module = await (moduleID ?? "").GetModuleByIDAsync(cancellationToken).ConfigureAwait(false);
			if (module == null || !module.SystemID.IsEquals(organization.ID))
				throw new InformationInvalidException("The module is invalid");

			// check permission
			var gotRights = isSystemAdministrator || requestInfo.Session.User.ID.IsEquals(organization.OwnerID) || requestInfo.Session.User.IsViewer(module.WorkingPrivileges);
			if (!gotRights)
				throw new AccessDeniedException();

			// process cache
			var addChildren = "true".IsEquals(requestInfo.GetHeaderParameter("x-children"));
			var json = string.IsNullOrWhiteSpace(query) && !addChildren ? await Utility.Cache.GetAsync<string>(Extensions.GetCacheKeyOfObjectsJson(filter, sort, pageSize, pageNumber), cancellationToken).ConfigureAwait(false) : null;
			if (!string.IsNullOrWhiteSpace(json))
				return JObject.Parse(json);

			// prepare pagination
			var totalRecords = pagination.Item1 > -1 ? pagination.Item1 : -1;
			if (totalRecords < 0)
				totalRecords = string.IsNullOrWhiteSpace(query)
					? await Category.CountAsync(filter, Extensions.GetCacheKeyOfTotalObjects(filter, sort), cancellationToken).ConfigureAwait(false)
					: await Category.CountAsync(query, filter, cancellationToken).ConfigureAwait(false);

			var totalPages = new Tuple<long, int>(totalRecords, pageSize).GetTotalPages();
			if (totalPages > 0 && pageNumber > totalPages)
				pageNumber = totalPages;

			// search
			var objects = totalRecords > 0
				? string.IsNullOrWhiteSpace(query)
					? await Category.FindAsync(filter, sort, pageSize, pageNumber, Extensions.GetCacheKey(filter, sort, pageSize, pageNumber), cancellationToken).ConfigureAwait(false)
					: await Category.SearchAsync(query, filter, pageSize, pageNumber, cancellationToken).ConfigureAwait(false)
				: new List<Category>();

			// build response
			pagination = new Tuple<long, int, int, int>(totalRecords, totalPages, pageSize, pageNumber);
			if (addChildren)
				await objects.Where(category => category._childrenIDs == null).ForEachAsync(async (category, token) =>
				{
					await category.FindChildrenAsync(token, false).ConfigureAwait(false);
					await category.SetAsync(false, true, token).ConfigureAwait(false);
				}, cancellationToken, true, false).ConfigureAwait(false);
			var response = new JObject()
			{
				{ "FilterBy", filter.ToClientJson(query) },
				{ "SortBy", sort?.ToClientJson() },
				{ "Pagination", pagination.GetPagination() },
				{ "Objects", objects.Select(category => addChildren ? category.ToJson(true, false) : category.ToJson(false, null)).ToJArray() }
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

		internal static async Task<JObject> CreateCategoryAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, string nodeID = null, IRTUService rtuService = null, CancellationToken cancellationToken = default)
		{
			// prepare
			var request = requestInfo.GetBodyExpando();

			var organizationID = request.Get<string>("SystemID") ?? requestInfo.GetParameter("x-system-id");
			var organization = await (organizationID ?? "").GetOrganizationByIDAsync(cancellationToken).ConfigureAwait(false);
			if (organization == null)
				throw new InformationInvalidException("The organization is invalid");

			var moduleID = request.Get<string>("RepositoryID") ?? requestInfo.GetParameter("x-module");
			var module = await (moduleID ?? "").GetModuleByIDAsync(cancellationToken).ConfigureAwait(false);
			if (module == null || !module.SystemID.IsEquals(organization.ID))
				throw new InformationInvalidException("The module is invalid");

			var contentTypeID = request.Get<string>("RepositoryEntityID") ?? requestInfo.GetParameter("x-content-type");
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
			category.Set().ClearRelatedCacheAsync(null, rtuService, cancellationToken).Run();

			var updateMessages = new List<UpdateMessage>();
			var communicateMessages = new List<CommunicateMessage>();
			var objectName = category.GetObjectName();

			// update parent
			var parentCategory = category.ParentCategory;
			if (parentCategory != null)
			{
				await Utility.Cache.RemoveAsync(Extensions.GetRelatedCacheKeys(category.SystemID.GetCategoriesFilter(category.RepositoryID, category.RepositoryEntityID, category.ParentID), Sorts<Category>.Ascending("OrderIndex").ThenByAscending("Title")), cancellationToken).ConfigureAwait(false);
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
					ExcludedNodeID = nodeID
				});
			}

			// message to update to all other connected clients
			var response = category.ToJson(true, false);

			if (category.ParentCategory == null)
				updateMessages.Add(new UpdateMessage
				{
					Type = $"{requestInfo.ServiceName}#{objectName}#Create",
					DeviceID = "*",
					ExcludedDeviceID = requestInfo.Session.DeviceID,
					Data = response
				});

			// message to update to all service instances (on all other nodes)
			communicateMessages.Add(new CommunicateMessage(requestInfo.ServiceName)
			{
				Type = $"{objectName}#Create",
				Data = response,
				ExcludedNodeID = nodeID
			});

			// send messages and response
			await Task.WhenAll(
				updateMessages.ForEachAsync((message, token) => rtuService == null ? Task.CompletedTask : rtuService.SendUpdateMessageAsync(message, token), cancellationToken, true, false),
				communicateMessages.ForEachAsync((message, token) => rtuService == null ? Task.CompletedTask : rtuService.SendInterCommunicateMessageAsync(message, token), cancellationToken)
			).ConfigureAwait(false);
			return response;
		}

		internal static async Task<JObject> GetCategoryAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, IRTUService rtuService = null, CancellationToken cancellationToken = default)
		{
			// prepare
			var identity = requestInfo.GetObjectIdentity() ?? "";
			var category = await (identity.IsValidUUID() ? identity.GetCategoryByIDAsync(cancellationToken) : (requestInfo.GetParameter("RepositoryEntityID") ?? requestInfo.GetParameter("x-content-type") ?? "").GetCategoryByAliasAsync(identity, cancellationToken)).ConfigureAwait(false);
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

			// prepare the response
			if (category._childrenIDs == null)
			{
				await category.FindChildrenAsync(cancellationToken, false).ConfigureAwait(false);
				category.Set(false, true);
			}

			// send update message and response
			var objectName = category.GetObjectName();
			var response = category.ToJson(true, false);
			await (rtuService == null ? Task.CompletedTask : rtuService.SendUpdateMessageAsync(new UpdateMessage
			{
				Type = $"{requestInfo.ServiceName}#{objectName}#Update",
				Data = response,
				DeviceID = "*",
				ExcludedDeviceID = requestInfo.Session.DeviceID
			}, cancellationToken)).ConfigureAwait(false);
			return response;
		}

		internal static async Task<JObject> UpdateCategoryAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, string nodeID = null, IRTUService rtuService = null, CancellationToken cancellationToken = default)
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
			category.Set().ClearRelatedCacheAsync(oldParentID, rtuService, cancellationToken).Run();

			var updateMessages = new List<UpdateMessage>();
			var communicateMessages = new List<CommunicateMessage>();
			var objectName = category.GetObjectName();

			// update parent
			var parentCategory = category.ParentCategory;
			if (parentCategory != null && !category.ParentID.IsEquals(oldParentID))
			{
				await Utility.Cache.RemoveAsync(Extensions.GetRelatedCacheKeys(category.SystemID.GetCategoriesFilter(category.RepositoryID, category.RepositoryEntityID, category.ParentID), Sorts<Category>.Ascending("OrderIndex").ThenByAscending("Title")), cancellationToken).ConfigureAwait(false);
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
					ExcludedNodeID = nodeID
				});
			}

			// update old parent
			if (!string.IsNullOrWhiteSpace(oldParentID) && !oldParentID.IsEquals(category.ParentID))
			{
				parentCategory = await oldParentID.GetCategoryByIDAsync(cancellationToken).ConfigureAwait(false);
				if (parentCategory != null)
				{
					await Utility.Cache.RemoveAsync(Extensions.GetRelatedCacheKeys(category.SystemID.GetCategoriesFilter(category.RepositoryID, category.RepositoryEntityID, parentCategory.ID), Sorts<Category>.Ascending("OrderIndex").ThenByAscending("Title")), cancellationToken).ConfigureAwait(false);
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
						ExcludedNodeID = nodeID
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
					DeviceID = "*",
					ExcludedDeviceID = requestInfo.Session.DeviceID
				});

			// message to update to all service instances (on all other nodes)
			communicateMessages.Add(new CommunicateMessage(requestInfo.ServiceName)
			{
				Type = $"{objectName}#Update",
				Data = response,
				ExcludedNodeID = nodeID
			});

			// send messages and response
			await Task.WhenAll(
				updateMessages.ForEachAsync((message, token) => rtuService == null ? Task.CompletedTask : rtuService.SendUpdateMessageAsync(message, token), cancellationToken, true, false),
				communicateMessages.ForEachAsync((message, token) => rtuService == null ? Task.CompletedTask : rtuService.SendInterCommunicateMessageAsync(message, token), cancellationToken)
			).ConfigureAwait(false);
			return response;
		}

		internal static async Task<JObject> UpdateCategoriesAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, string nodeID = null, IRTUService rtuService = null, CancellationToken cancellationToken = default)
		{
			// prepare
			var request = requestInfo.GetBodyJson();

			var category = await (request.Get<string>("CategoryID") ?? requestInfo.GetParameter("x-category-id") ?? "").GetCategoryByIDAsync(cancellationToken).ConfigureAwait(false);
			var organization = category != null
				? category.Organization
				: await (request.Get<string>("SystemID") ?? requestInfo.GetParameter("x-system-id") ?? "").GetOrganizationByIDAsync(cancellationToken).ConfigureAwait(false);
			if (organization == null)
				throw new InformationInvalidException("The organization is invalid");
			var module = category != null
				? category.Module
				: await (request.Get<string>("RepositoryID") ?? requestInfo.GetParameter("x-module") ?? "").GetModuleByIDAsync(cancellationToken).ConfigureAwait(false);
			if (module == null || !module.SystemID.IsEquals(organization.ID))
				throw new InformationInvalidException("The module is invalid");
			var contentType = category != null
				? category.ContentType
				: await (request.Get<string>("RepositoryEntityID") ?? requestInfo.GetParameter("x-content-type") ?? "").GetContentTypeByIDAsync(cancellationToken).ConfigureAwait(false);
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
						ExcludedNodeID = nodeID
					});
				}
			});

			if (category != null)
			{
				await category.ClearRelatedCacheAsync(null, rtuService, cancellationToken).ConfigureAwait(false);
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
					ExcludedNodeID = nodeID
				});
			}

			// send messages and response
			await Task.WhenAll(
				updateMessages.ForEachAsync((message, token) => rtuService == null ? Task.CompletedTask : rtuService.SendUpdateMessageAsync(message, token), cancellationToken, true, false),
				communicateMessages.ForEachAsync((message, token) => rtuService == null ? Task.CompletedTask : rtuService.SendInterCommunicateMessageAsync(message, token), cancellationToken)
			).ConfigureAwait(false);
			return new JObject();
		}

		internal static async Task<JObject> DeleteCategoryAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, string nodeID = null, IRTUService rtuService = null, string validationKey = null, CancellationToken cancellationToken = default)
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
			await (await category.FindChildrenAsync(cancellationToken, false).ConfigureAwait(false)).ForEachAsync(async (child, token) =>
			{
				// update children to root
				if (updateChildren)
				{
					child.ParentID = null;
					child.LastModified = DateTime.Now;
					child.LastModifiedID = requestInfo.Session.User.ID;
					await Role.UpdateAsync(child, requestInfo.Session.User.ID, token).ConfigureAwait(false);
					var json = child.Set().ToJson(true, false);
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
						ExcludedNodeID = nodeID
					});
				}

				// delete children
				else
				{
					// delete files
					await requestInfo.DeleteFilesAsync(child.SystemID, child.RepositoryEntityID, child.ID, validationKey, token).ConfigureAwait(false);

					// delete objects
					var messages = await child.DeleteChildrenAsync(requestInfo.Session.User.ID, requestInfo.ServiceName, nodeID, token).ConfigureAwait(false);
					updateMessages = updateMessages.Concat(messages.Item1).ToList();
					communicateMessages = communicateMessages.Concat(messages.Item2).ToList();
				}
			}, cancellationToken, true, false).ConfigureAwait(false);

			// delete files of category
			await requestInfo.DeleteFilesAsync(category.SystemID, category.RepositoryEntityID, category.ID, validationKey, cancellationToken).ConfigureAwait(false);

			// delete vs
			await Category.DeleteAsync<Category>(category.ID, requestInfo.Session.User.ID, cancellationToken).ConfigureAwait(false);
			category.Remove().ClearRelatedCacheAsync(null, rtuService, cancellationToken).Run();

			// message to update to all other connected clients
			var response = category.ToJson();
			updateMessages.Add(new UpdateMessage
			{
				Type = $"{requestInfo.ServiceName}#{objectName}#Delete",
				Data = response,
				DeviceID = "*",
				ExcludedDeviceID = requestInfo.Session.DeviceID
			});

			// message to update to all service instances (on all other nodes)
			communicateMessages.Add(new CommunicateMessage(requestInfo.ServiceName)
			{
				Type = $"{objectName}#Delete",
				Data = response,
				ExcludedNodeID = nodeID
			});

			// send messages and response
			await Task.WhenAll(
				updateMessages.ForEachAsync((message, token) => rtuService == null ? Task.CompletedTask : rtuService.SendUpdateMessageAsync(message, token), cancellationToken, true, false),
				communicateMessages.ForEachAsync((message, token) => rtuService == null ? Task.CompletedTask : rtuService.SendInterCommunicateMessageAsync(message, token), cancellationToken)
			).ConfigureAwait(false);
			return response;
		}

		static async Task<Tuple<List<UpdateMessage>, List<CommunicateMessage>>> DeleteChildrenAsync(this Category category, string userID, string serviceName = null, string nodeID = null, CancellationToken cancellationToken = default)
		{
			var updateMessages = new List<UpdateMessage>();
			var communicateMessages = new List<CommunicateMessage>();
			var entityDefinition = RepositoryMediator.GetEntityDefinition<Category>();
			var objectName = $"{(string.IsNullOrWhiteSpace(entityDefinition.ObjectNamePrefix) ? "" : entityDefinition.ObjectNamePrefix)}{entityDefinition.ObjectName}{(string.IsNullOrWhiteSpace(entityDefinition.ObjectNameSuffix) ? "" : entityDefinition.ObjectNameSuffix)}";

			var children = await category.FindChildrenAsync(cancellationToken, false).ConfigureAwait(false);
			await children.ForEachAsync(async (child, token) =>
			{
				var messages = await child.DeleteChildrenAsync(userID, serviceName, nodeID, token).ConfigureAwait(false);
				updateMessages = updateMessages.Concat(messages.Item1).ToList();
				communicateMessages = communicateMessages.Concat(messages.Item2).ToList();
			}, cancellationToken, true, false).ConfigureAwait(false);

			await Category.DeleteAsync<Category>(category.ID, userID, cancellationToken).ConfigureAwait(false);
			category.Remove().ClearRelatedCacheAsync(null, null, cancellationToken).Run();

			var json = category.ToJson();
			updateMessages.Add(new UpdateMessage
			{
				Type = $"{serviceName}#{objectName}#Delete",
				Data = json,
				DeviceID = "*"
			});
			communicateMessages.Add(new CommunicateMessage(serviceName)
			{
				Type = $"{objectName}#Delete",
				Data = json,
				ExcludedNodeID = nodeID
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

		internal static async Task<JObject> GenerateMenuAsync(this RequestInfo requestInfo, Category category, string thumbnailURL, int level, int maxLevel = 0, string validationKey = null, CancellationToken cancellationToken = default)
		{
			// generate the menu item
			var url = category.GetURL();
			var menu = new JObject
			{
				{ "ID", category.ID },
				{ "Title", category.Title.CleanInvalidXmlCharacters() },
				{ "Description", category.Description?.Replace("\r", "").Replace("\n", "<br/>").CleanInvalidXmlCharacters() },
				{ "Image", thumbnailURL },
				{ "URL", url.CleanInvalidXmlCharacters() },
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
					await category.FindChildrenAsync(cancellationToken).ConfigureAwait(false);

				// generate children
				var children = category.Children;
				if (children.Count > 0)
				{
					requestInfo.Header["x-as-attachments"] = "true";
					var thumbnails = children.Count == 1
						? await requestInfo.GetThumbnailsAsync(children[0].ID, children[0].Title.Url64Encode(), validationKey, cancellationToken).ConfigureAwait(false)
						: await requestInfo.GetThumbnailsAsync(children.Select(child => child.ID).Join(","), children.ToJObject("ID", child => new JValue(child.Title.Url64Encode())).ToString(Formatting.None), validationKey, cancellationToken).ConfigureAwait(false);
					subMenu = new JArray();
					await children.ForEachAsync(async (child, token) => subMenu.Add(await requestInfo.GenerateMenuAsync(child, thumbnails?.GetThumbnailURL(child.ID), level + 1, maxLevel, validationKey, token).ConfigureAwait(false)), cancellationToken, true, false).ConfigureAwait(false);
				}

				// update children
				if (subMenu != null && subMenu.Count > 0)
					menu["SubMenu"] = new JObject
					{
						{ "Menu", subMenu }
					};
			}

			// update 'Selected' state
			menu["Selected"] = subMenu?.Select(smenu => smenu as JObject).FirstOrDefault(smenu => smenu.Get<bool>("Selected", false)) != null || requestInfo.IsSelected(url);

			// return the menu item
			return menu;
		}

		internal static async Task<JObject> SyncCategoryAsync(this RequestInfo requestInfo, string nodeID = null, IRTUService rtuService = null, CancellationToken cancellationToken = default)
		{
			var data = requestInfo.GetBodyExpando();
			var category = await data.Get<string>("ID").GetCategoryByIDAsync(cancellationToken).ConfigureAwait(false);
			if (category == null)
			{
				category = Category.CreateInstance(data);
				await Category.CreateAsync(category, cancellationToken).ConfigureAwait(false);
			}
			else
			{
				category.Fill(data);
				await Category.UpdateAsync(category, true, cancellationToken).ConfigureAwait(false);
			}

			// send update messages
			var json = category.Set().ToJson();
			var objectName = category.GetObjectName();
			await Task.WhenAll(
				rtuService == null ? Task.CompletedTask : rtuService.SendUpdateMessageAsync(new UpdateMessage
				{
					Type = $"{requestInfo.ServiceName}#{objectName}#Update",
					Data = json,
					DeviceID = "*"
				}, cancellationToken),
				rtuService == null ? Task.CompletedTask : rtuService.SendInterCommunicateMessageAsync(new CommunicateMessage(requestInfo.ServiceName)
				{
					Type = $"{objectName}#Update",
					Data = json,
					ExcludedNodeID = nodeID
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
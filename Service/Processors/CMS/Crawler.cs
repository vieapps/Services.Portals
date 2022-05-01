#region Related components
using System;
using System.Linq;
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
	public static class CrawlerProcessor
	{
		internal static ConcurrentDictionary<string, Crawler> Crawlers { get; } = new ConcurrentDictionary<string, Crawler>(StringComparer.OrdinalIgnoreCase);

		internal static HashSet<string> RunningCrawlers { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		public static Crawler CreateCrawler(this ExpandoObject data, string excluded = null, Action<Crawler> onCompleted = null)
			=> Crawler.CreateInstance(data, excluded?.ToHashSet(), onCompleted);

		public static Crawler Update(this Crawler crawler, ExpandoObject data, string excluded = null, Action<Crawler> onCompleted = null)
			=> crawler.Fill(data, excluded?.ToHashSet(), onCompleted);

		internal static Crawler Set(this Crawler crawler, bool clear = false, bool updateCache = false)
		{
			if (crawler != null && !string.IsNullOrWhiteSpace(crawler.ID) && !string.IsNullOrWhiteSpace(crawler.Title))
			{
				if (clear)
					crawler.Remove();

				CrawlerProcessor.Crawlers[crawler.ID] = crawler;

				if (updateCache)
					Utility.Cache.Set(crawler);
			}
			return crawler;
		}

		internal static async Task<Crawler> SetAsync(this Crawler crawler, bool clear = false, bool updateCache = false, CancellationToken cancellationToken = default)
		{
			crawler?.Set(clear);
			await (updateCache && crawler != null && !string.IsNullOrWhiteSpace(crawler.ID) && !string.IsNullOrWhiteSpace(crawler.Title) ? Utility.Cache.SetAsync(crawler, cancellationToken) : Task.CompletedTask).ConfigureAwait(false);
			return crawler;
		}

		internal static Crawler Remove(this Crawler crawler)
			=> (crawler?.ID ?? "").RemoveCrawler();

		internal static Crawler RemoveCrawler(this string id)
			=> !string.IsNullOrWhiteSpace(id) && CrawlerProcessor.Crawlers.TryRemove(id, out var crawler) && crawler != null ? crawler : null;

		public static Crawler GetCrawlerByID(this string id, bool force = false, bool fetchRepository = true)
			=> !force && !string.IsNullOrWhiteSpace(id) && CrawlerProcessor.Crawlers.ContainsKey(id)
				? CrawlerProcessor.Crawlers[id]
				: fetchRepository && !string.IsNullOrWhiteSpace(id)
					? Crawler.Get<Crawler>(id)?.Set()
					: null;

		public static async Task<Crawler> GetCrawlerByIDAsync(this string id, CancellationToken cancellationToken = default, bool force = false)
			=> (id ?? "").GetCrawlerByID(force, false) ?? (await Crawler.GetAsync<Crawler>(id, cancellationToken).ConfigureAwait(false))?.Set();

		public static IFilterBy<Crawler> GetCrawlersFilter(string systemID, string repositoryID = null, string repositoryEntityID = null, Action<FilterBys<Crawler>> onCompleted = null)
		{
			var filter = Filters<Crawler>.And();
			if (!string.IsNullOrWhiteSpace(systemID))
				filter.Add(Filters<Crawler>.Equals("SystemID", systemID));
			if (!string.IsNullOrWhiteSpace(repositoryID))
				filter.Add(Filters<Crawler>.Equals("RepositoryID", repositoryID));
			if (!string.IsNullOrWhiteSpace(repositoryEntityID))
				filter.Add(Filters<Crawler>.Equals("RepositoryEntityID", repositoryEntityID));
			onCompleted?.Invoke(filter);
			return filter;
		}

		public static List<Crawler> FindCrawlers(this string systemID, string repositoryID = null, string repositoryEntityID = null, bool processCache = true)
		{
			if (string.IsNullOrWhiteSpace(systemID))
				return new List<Crawler>();

			var filter = CrawlerProcessor.GetCrawlersFilter(systemID, repositoryID, repositoryEntityID);
			var sort = Sorts<Crawler>.Ascending("OrderIndex").ThenByAscending("Title");
			var cacheKey = processCache ? Extensions.GetCacheKey(filter, sort, 0, 1) : null;

			if (Utility.IsDebugLogEnabled)
				Utility.WriteLogAsync(UtilityService.NewUUID, $"Find crawlers\r\n- Filter: {filter.ToJson()}\r\n- Sort: {sort?.ToJson()}\r\n- Cache key: {cacheKey}", "Crawler").Run();
			return Crawler.Find(filter, sort, 0, 1, cacheKey);
		}

		public static async Task<List<Crawler>> FindCrawlersAsync(this string systemID, string repositoryID = null, string repositoryEntityID = null, CancellationToken cancellationToken = default, bool processCache = true)
		{
			if (string.IsNullOrWhiteSpace(systemID))
				return new List<Crawler>();

			var filter = CrawlerProcessor.GetCrawlersFilter(systemID, repositoryID, repositoryEntityID);
			var sort = Sorts<Crawler>.Ascending("OrderIndex").ThenByAscending("Title");
			var cacheKey = processCache ? Extensions.GetCacheKey(filter, sort, 0, 1) : null;

			if (Utility.IsDebugLogEnabled)
				await Utility.WriteLogAsync(UtilityService.NewUUID, $"Find crawlers\r\n- Filter: {filter.ToJson()}\r\n- Sort: {sort?.ToJson()}\r\n- Cache key: {cacheKey}", "Crawler").ConfigureAwait(false);
			return await Crawler.FindAsync(filter, sort, 0, 1, cacheKey, cancellationToken).ConfigureAwait(false);
		}

		internal static Task ProcessInterCommunicateMessageOfCrawlerAsync(this CommunicateMessage message, CancellationToken cancellationToken = default)
		{
			if (message.Type.IsEndsWith("#Create"))
				message.Data.ToExpandoObject().CreateCrawler().Set();

			else if (message.Type.IsEndsWith("#Update"))
			{
				var crawler = message.Data.Get("ID", "").GetCrawlerByID(false, false);
				(crawler == null ? message.Data.ToExpandoObject().CreateCrawler() : crawler.Update(message.Data.ToExpandoObject())).Set();
			}

			else if (message.Type.IsEndsWith("#Delete"))
				message.Data.ToExpandoObject().CreateCrawler().Remove();

			return Task.CompletedTask;
		}

		static async Task<Tuple<long, List<Crawler>, List<string>>> SearchAsync(this RequestInfo requestInfo, string query, IFilterBy<Crawler> filter, SortBy<Crawler> sort, int pageSize, int pageNumber, long totalRecords = -1, CancellationToken cancellationToken = default)
		{
			// cache keys
			var cacheKeyOfObjects = string.IsNullOrWhiteSpace(query) ? Extensions.GetCacheKey(filter, sort, pageSize, pageNumber) : null;
			var cacheKeyOfTotalObjects = string.IsNullOrWhiteSpace(query) ? Extensions.GetCacheKeyOfTotalObjects(filter, sort) : null;
			var cacheKeys = string.IsNullOrWhiteSpace(query) ? new List<string> { cacheKeyOfObjects, cacheKeyOfTotalObjects } : new List<string>();

			// count
			totalRecords = totalRecords > -1
				? totalRecords
				: string.IsNullOrWhiteSpace(query)
					? await Crawler.CountAsync(filter, null, cacheKeyOfTotalObjects, cancellationToken).ConfigureAwait(false)
					: await Crawler.CountAsync(query, filter, null, cancellationToken).ConfigureAwait(false);

			// search objects
			var objects = totalRecords > 0
				? string.IsNullOrWhiteSpace(query)
					? await Crawler.FindAsync(filter, sort, pageSize, pageNumber, null, cacheKeyOfObjects, cancellationToken).ConfigureAwait(false)
					: await Crawler.SearchAsync(query, filter, null, pageSize, pageNumber, null, cancellationToken).ConfigureAwait(false)
				: new List<Crawler>();

			// page size to clear related cached
			if (string.IsNullOrWhiteSpace(query))
				await Utility.SetCacheOfPageSizeAsync(filter, sort, pageSize, cancellationToken).ConfigureAwait(false);

			// return the results
			return new Tuple<long, List<Crawler>, List<string>>(totalRecords, objects, cacheKeys);
		}

		internal static async Task<JObject> SearchCrawlersAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, CancellationToken cancellationToken = default)
		{
			// prepare
			var request = requestInfo.GetRequestExpando();
			var filter = request.Get<ExpandoObject>("FilterBy")?.ToFilterBy<Crawler>() ?? Filters<Crawler>.And();

			// get organization
			var organizationID = filter.GetValue("SystemID") ?? requestInfo.GetParameter("SystemID") ?? requestInfo.GetParameter("x-system-id") ?? requestInfo.GetParameter("OrganizationID");
			var organization = await (organizationID ?? "").GetOrganizationByIDAsync(cancellationToken).ConfigureAwait(false);
			if (organization == null)
				throw new InformationExistedException("The organization is invalid");

			// check permission
			var gotRights = isSystemAdministrator || requestInfo.Session.User.IsModerator(organization.WorkingPrivileges);
			if (!gotRights)
				throw new AccessDeniedException();

			// normalize
			var repositoryID = filter.GetValue("RepositoryID") ?? requestInfo.GetParameter("RepositoryID") ?? requestInfo.GetParameter("x-repository-id") ?? requestInfo.GetParameter("ModuleID");
			var repositoryEntityID = filter.GetValue("RepositoryEntityID") ?? requestInfo.GetParameter("RepositoryEntityID") ?? requestInfo.GetParameter("x-repository-entiry-id") ?? requestInfo.GetParameter("ContentTypeID");
			var query = request.Get<string>("FilterBy.Query");
			if (filter is FilterBys<Crawler> filterBy)
			{
				if (filterBy.GetChild("SystemID") == null)
					filterBy.Add(Filters<Crawler>.Equals("SystemID", organization.ID));
				if (string.IsNullOrWhiteSpace(query))
				{
					if (filterBy.GetChild("RepositoryID") == null && !string.IsNullOrWhiteSpace(repositoryID))
						filterBy.Add(Filters<Crawler>.Equals("RepositoryID", repositoryID));
					if (filterBy.GetChild("RepositoryEntityID") == null && !string.IsNullOrWhiteSpace(repositoryEntityID))
						filterBy.Add(Filters<Crawler>.Equals("RepositoryEntityID", repositoryEntityID));
				}
			}
			else
				filterBy = null;
			filter = filterBy == null || filterBy.Children == null || filterBy.Children.Count < 1
				? CrawlerProcessor.GetCrawlersFilter(organization.ID, repositoryID, repositoryEntityID)
				: filter.Prepare(requestInfo);
			var sort = string.IsNullOrWhiteSpace(query) ? request.Get<ExpandoObject>("SortBy")?.ToSortBy<Crawler>() ?? Sorts<Crawler>.Ascending("Title").ThenByDescending("LastModified") : null;

			var pagination = string.IsNullOrWhiteSpace(query)
				? new Tuple<long, int, int, int>(-1, 0, 0, 1)
				: request.Get<ExpandoObject>("Pagination")?.GetPagination() ?? new Tuple<long, int, int, int>(-1, 0, 20, 1);
			var pageSize = pagination.Item3;
			var pageNumber = pagination.Item4;

			if (Utility.IsDebugLogEnabled)
				await requestInfo.WriteLogAsync($"Search crawlers (APIs)\r\n- Filter: {filter.ToJson()}\r\n- Sort: {sort?.ToJson()}\r\n- Pagination: {pagination.GetPagination()}", "Crawler").ConfigureAwait(false);

			// process cache
			var cacheKeyOfObjectsJson = string.IsNullOrWhiteSpace(query) ? Extensions.GetCacheKeyOfObjectsJson(filter, sort, pageSize, pageNumber) : null;
			if (cacheKeyOfObjectsJson != null)
			{
				var json = await Utility.Cache.GetAsync<string>(cacheKeyOfObjectsJson, cancellationToken).ConfigureAwait(false);
				if (!string.IsNullOrWhiteSpace(json))
				{
					var result = JObject.Parse(json);
					if (Utility.IsDebugLogEnabled)
						await requestInfo.WriteLogAsync($"Search crawlers (APIs) => cached was found\r\n- Key: {cacheKeyOfObjectsJson} => JSON: {result}", "Crawler").ConfigureAwait(false);
					return result;
				}
			}

			// search if has no cache
			var results = await requestInfo.SearchAsync(query, filter, sort, pageSize, pageNumber, pagination.Item1 > -1 ? pagination.Item1 : -1, cancellationToken).ConfigureAwait(false);
			var totalRecords = results.Item1;
			var objects = results.Item2;

			// build response
			var totalPages = new Tuple<long, int>(totalRecords, pageSize).GetTotalPages();
			if (totalPages > 0 && pageNumber > totalPages)
				pageNumber = totalPages;
			pagination = new Tuple<long, int, int, int>(totalRecords, totalPages, pageSize, pageNumber);

			var response = new JObject
			{
				{ "FilterBy", filter.ToClientJson(query) },
				{ "SortBy", sort?.ToClientJson() },
				{ "Pagination", pagination.GetPagination() },
				{ "Objects", objects.Select(@object => @object.ToJson()).ToJArray() }
			};

			// update cache
			if (string.IsNullOrWhiteSpace(query))
				await Utility.Cache.SetAsync(cacheKeyOfObjectsJson, response.ToString(Formatting.None), cancellationToken).ConfigureAwait(false);

			// response
			return response;
		}

		static Crawlers.ICrawler CreateCrawler(this RequestInfo requestInfo)
		{
			var crawlerInfo = requestInfo.GetBodyExpando().CreateCrawler("Privileges,Created,CreatedID,LastModified,LastModifiedID");
			if (!crawlerInfo.Type.Equals(Portals.Crawlers.Type.Custom))
			{
				var uri = new Uri(crawlerInfo.URL);
				crawlerInfo.URL = $"{uri.Scheme}://{uri.Host}";
			}

			var crawler = crawlerInfo.Type.Equals(Portals.Crawlers.Type.WordPress)
				? new Crawlers.WordPress(crawlerInfo)
				: new Crawlers.Blogger(crawlerInfo) as Crawlers.ICrawler;

			return crawler;
		}

		static Crawlers.ICrawlerAdapter CreateCrawlerNormalizingAdapter(this Crawlers.ICrawler crawler)
		{
			var normalizingAdapter = string.IsNullOrWhiteSpace(crawler.CrawlerInfo.NormalizingAdapter)
				? new Crawlers.NormalizingAdapter(crawler.CrawlerInfo)
				: Type.GetType(crawler.CrawlerInfo.NormalizingAdapter).CreateInstance<Crawlers.ICrawlerAdapter>();

			if (!string.IsNullOrWhiteSpace(crawler.CrawlerInfo.NormalizingAdapter))
				normalizingAdapter.CrawlerInfo = crawler.CrawlerInfo;

			return normalizingAdapter;
		}

		internal static Task<string> FetchAsync(this Crawlers.ICrawlerInfo crawler, string uri, CancellationToken cancellationToken = default)
			=> new Uri(uri).FetchHttpAsync(new Dictionary<string, string> { ["Referer"] = crawler.URL, ["User-Agent"] = UtilityService.DesktopUserAgent }, 90, cancellationToken);

		internal static async Task<JArray> FetchCrawlerCategoriesAsync(this RequestInfo requestInfo, CancellationToken cancellationToken = default)
		{
			var crawler = requestInfo.CreateCrawler();
			var normalizingAdapter = crawler.CreateCrawlerNormalizingAdapter();
			return (await crawler.FetchCategoriesAsync(cancellationToken).ConfigureAwait(false)).Select(category => category.ToJson()).ToJArray();
		}

		internal static async Task<JObject> TestCrawlerAsync(this RequestInfo requestInfo, CancellationToken cancellationToken = default)
		{
			// prepare
			var crawler = requestInfo.CreateCrawler();
			var normalizingAdapter = crawler.CreateCrawlerNormalizingAdapter();

			crawler.CrawlerInfo.Categories = await crawler.FetchCategoriesAsync(cancellationToken).ConfigureAwait(false);
			var contents = await crawler.FetchContentsAsync(null, normalizingAdapter.NormalizeAsync, cancellationToken).ConfigureAwait(false);
			contents.ForEach(content => content.Normalize(crawler.CrawlerInfo));

			return new JObject
			{
				{ "Categories", crawler.CrawlerInfo.Categories.Select(category => category.ToJson()).ToJArray() },
				{ "Contents", contents.Select(content => content.ToJson()).ToJArray() }
			};
		}

		internal static async Task<JObject> CreateCrawlerAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, CancellationToken cancellationToken = default)
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
			var gotRights = isSystemAdministrator || requestInfo.Session.User.IsModerator(organization.WorkingPrivileges);
			if (!gotRights)
				throw new AccessDeniedException();

			var crawler = request.CreateCrawler("Privileges,Created,CreatedID,LastModified,LastModifiedID", obj =>
			{
				obj.ID = string.IsNullOrWhiteSpace(obj.ID) || !obj.ID.IsValidUUID() ? UtilityService.NewUUID : obj.ID;
				obj.SystemID = organization.ID;
				obj.RepositoryID = module.ID;
				obj.RepositoryEntityID = contentType.ID;
				obj.Created = obj.LastModified = DateTime.Now;
				obj.CreatedID = obj.LastModifiedID = requestInfo.Session.User.ID;
			});

			// create new
			await Crawler.CreateAsync(crawler, cancellationToken).ConfigureAwait(false);
			crawler.Set();

			// message to update to all service instances (on all other nodes)
			var objectName = crawler.GetObjectName();
			var response = crawler.ToJson();
			new CommunicateMessage(requestInfo.ServiceName)
			{
				Type = $"{objectName}#Create",
				Data = response,
				ExcludedNodeID = Utility.NodeID
			}.Send();

			// send notification
			//await crawler.SendNotificationAsync("Create", organization.Notifications, ApprovalStatus.Draft, ApprovalStatus.Published, requestInfo, cancellationToken).ConfigureAwait(false);

			// response
			return response;
		}

		internal static async Task<JObject> GetCrawlerAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, CancellationToken cancellationToken = default)
		{
			// prepare
			var identity = requestInfo.GetObjectIdentity(true, true) ?? "";
			var crawler = await Crawler.GetAsync<Crawler>(identity ?? "", cancellationToken).ConfigureAwait(false);
			if (crawler == null)
				throw new InformationNotFoundException();
			else if (crawler.Organization == null)
				throw new InformationInvalidException("The organization is invalid");

			// check permission
			var gotRights = isSystemAdministrator || requestInfo.Session.User.IsModerator(crawler.Organization.WorkingPrivileges);
			if (!gotRights)
				throw new AccessDeniedException();

			// refresh (clear cached and reload)
			var isRefresh = "refresh".IsEquals(requestInfo.GetObjectIdentity());
			if (isRefresh)
			{
				await Utility.Cache.RemoveAsync(crawler, cancellationToken).ConfigureAwait(false);
				crawler = await crawler.Remove().ID.GetCrawlerByIDAsync(cancellationToken, true).ConfigureAwait(false);
			}

			// prepare the response
			var response = crawler.ToJson();

			// send update messages
			var objectName = crawler.GetObjectName();
			new UpdateMessage
			{
				Type = $"{requestInfo.ServiceName}#{objectName}#Update",
				Data = response,
				DeviceID = "*"
			}.Send();
			if (isRefresh)
				new CommunicateMessage(requestInfo.ServiceName)
				{
					Type = $"{objectName}#Update",
					Data = response,
					ExcludedNodeID = Utility.NodeID
				}.Send();

			// response
			return response;
		}

		internal static async Task<JObject> UpdateAsync(this Crawler crawler, RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			// update
			await Crawler.UpdateAsync(crawler, requestInfo.Session.User.ID, cancellationToken).ConfigureAwait(false);
			crawler.Set();

			// message to update to all service instances (on all other nodes)
			var objectName = crawler.GetObjectName();
			var response = crawler.ToJson();
			new CommunicateMessage(requestInfo.ServiceName)
			{
				Type = $"{objectName}#Update",
				Data = response,
				ExcludedNodeID = Utility.NodeID
			}.Send();

			// send notification
			//await crawler.SendNotificationAsync("Update", crawler.ContentType.Notifications, oldStatus, crawler.Status, requestInfo, cancellationToken).ConfigureAwait(false);

			// response
			return response;
		}

		internal static async Task<JObject> UpdateCrawlerAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, CancellationToken cancellationToken = default)
		{
			// prepare
			var crawler = await Crawler.GetAsync<Crawler>(requestInfo.GetObjectIdentity() ?? "", cancellationToken).ConfigureAwait(false);
			if (crawler == null)
				throw new InformationNotFoundException();
			else if (crawler.Organization == null)
				throw new InformationInvalidException("The organization is invalid");

			// check permission
			var gotRights = isSystemAdministrator || requestInfo.Session.User.IsModerator(crawler.Organization.WorkingPrivileges);
			if (!gotRights)
				throw new AccessDeniedException();

			crawler.Update(requestInfo.GetBodyExpando(), "ID,SystemID,RepositoryID,RepositoryEntityID,Privileges,OrderIndex,Created,CreatedID,LastModified,LastModifiedID", obj =>
			{
				obj.LastModified = DateTime.Now;
				obj.LastModifiedID = requestInfo.Session.User.ID;
			});

			// update
			return await crawler.UpdateAsync(requestInfo, cancellationToken).ConfigureAwait(false);
		}

		internal static async Task<JObject> DeleteCrawlerAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, CancellationToken cancellationToken = default)
		{
			// prepare
			var crawler = await Crawler.GetAsync<Crawler>(requestInfo.GetObjectIdentity() ?? "", cancellationToken).ConfigureAwait(false);
			if (crawler == null)
				throw new InformationNotFoundException();
			else if (crawler.Organization == null)
				throw new InformationInvalidException("The organization is invalid");

			// check permission
			var gotRights = isSystemAdministrator || requestInfo.Session.User.IsModerator(crawler.Organization.WorkingPrivileges);
			if (!gotRights)
				throw new AccessDeniedException();

			// delete
			await Crawler.DeleteAsync<Crawler>(crawler.ID, requestInfo.Session.User.ID, cancellationToken).ConfigureAwait(false);

			// message to update to all other connected clients
			var objectName = crawler.GetObjectName();
			var response = crawler.ToJson();
			new UpdateMessage
			{
				Type = $"{requestInfo.ServiceName}#{objectName}#Delete",
				Data = response,
				DeviceID = "*"
			}.Send();

			// message to update to all service instances (on all other nodes)
			new CommunicateMessage(requestInfo.ServiceName)
			{
				Type = $"{objectName}#Delete",
				Data = response,
				ExcludedNodeID = Utility.NodeID
			}.Send();

			// send notification
			//await crawler.SendNotificationAsync("Delete", crawler.ContentType.Notifications, crawler.Status, crawler.Status, requestInfo, cancellationToken).ConfigureAwait(false);

			// response
			return response;
		}

		internal static async Task<JObject> SyncCrawlerAsync(this RequestInfo requestInfo, CancellationToken cancellationToken, bool sendNotifications = false)
		{
			var @event = requestInfo.GetHeaderParameter("Event");
			if (string.IsNullOrWhiteSpace(@event) || !@event.IsEquals("Delete"))
				@event = "Update";

			var data = requestInfo.GetBodyExpando();
			var crawler = await data.Get<string>("ID").GetCrawlerByIDAsync(cancellationToken).ConfigureAwait(false);

			if (!@event.IsEquals("Delete"))
			{
				if (crawler == null)
				{
					crawler = Crawler.CreateInstance(data);
					await Crawler.CreateAsync(crawler, cancellationToken).ConfigureAwait(false);
				}
				else
				{
					crawler.Fill(data);
					await Crawler.UpdateAsync(crawler, true, cancellationToken).ConfigureAwait(false);
				}
			}
			else if (crawler != null)
				await Crawler.DeleteAsync<Crawler>(crawler.ID, crawler.LastModifiedID, cancellationToken).ConfigureAwait(false);

			// send notifications
			//if (sendNotifications)
			//	await crawler.SendNotificationAsync(@event, crawler.ContentType.Notifications, crawler.Status, crawler.Status, requestInfo, cancellationToken).ConfigureAwait(false);

			// send update messages
			var json = @event.IsEquals("Delete")
				? crawler.Remove().ToJson()
				: crawler.Set().ToJson();
			var objectName = crawler.GetObjectName();
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

			// return the response
			return new JObject
			{
				{ "ID", crawler.ID },
				{ "Type", objectName }
			};
		}
	}
}
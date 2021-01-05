#region Related components
using System;
using System.Linq;
using System.Xml.Linq;
using System.Dynamic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using net.vieapps.Components.Utility;
using net.vieapps.Components.Security;
using net.vieapps.Components.Repository;
#endregion

namespace net.vieapps.Services.Portals
{
	public static class LinkProcessor
	{
		public static Link CreateLinkInstance(this ExpandoObject data, string excluded = null, Action<Link> onCompleted = null)
			=> Link.CreateInstance(data, excluded?.ToHashSet(), link =>
			{
				link.NormalizeHTMLs();
				onCompleted?.Invoke(link);
			});

		public static Link UpdateLinkInstance(this Link link, ExpandoObject data, string excluded = null, Action<Link> onCompleted = null)
			=> link.Fill(data, excluded?.ToHashSet(), _ =>
			{
				link.NormalizeHTMLs();
				onCompleted?.Invoke(link);
			});

		public static IFilterBy<Link> GetLinksFilter(string systemID, string repositoryID = null, string repositoryEntityID = null, string parentID = null, Action<FilterBys<Link>> onCompleted = null)
		{
			var filter = Filters<Link>.And();
			if (!string.IsNullOrWhiteSpace(systemID))
				filter.Add(Filters<Link>.Equals("SystemID", systemID));
			if (!string.IsNullOrWhiteSpace(repositoryID))
				filter.Add(Filters<Link>.Equals("RepositoryID", repositoryID));
			if (!string.IsNullOrWhiteSpace(repositoryEntityID))
				filter.Add(Filters<Link>.Equals("RepositoryEntityID", repositoryEntityID));
			filter.Add(string.IsNullOrWhiteSpace(parentID) ? Filters<Link>.IsNull("ParentID") : Filters<Link>.Equals("ParentID", parentID));
			onCompleted?.Invoke(filter);
			return filter;
		}

		public static List<Link> FindLinks(this string systemID, string repositoryID = null, string repositoryEntityID = null, string parentID = null)
		{
			if (string.IsNullOrWhiteSpace(systemID))
				return new List<Link>();
			var filter = LinkProcessor.GetLinksFilter(systemID, repositoryID, repositoryEntityID, parentID);
			var sort = Sorts<Link>.Ascending("OrderIndex").ThenByAscending("Title");
			return Link.Find(filter, sort, 0, 1, Extensions.GetCacheKey(filter, sort, 0, 1));
		}

		public static Task<List<Link>> FindLinksAsync(this string systemID, string repositoryID = null, string repositoryEntityID = null, string parentID = null, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(systemID))
				return Task.FromResult(new List<Link>());
			var filter = LinkProcessor.GetLinksFilter(systemID, repositoryID, repositoryEntityID, parentID);
			var sort = Sorts<Link>.Ascending("OrderIndex").ThenByAscending("Title");
			return Link.FindAsync(filter, sort, 0, 1, Extensions.GetCacheKey(filter, sort, 0, 1), cancellationToken);
		}

		internal static async Task<int> GetLastOrderIndexAsync(string systemID, string repositoryID = null, string repositoryEntityID = null, string parentID = null, CancellationToken cancellationToken = default)
		{
			var links = await systemID.FindLinksAsync(repositoryID, repositoryEntityID, parentID, cancellationToken).ConfigureAwait(false);
			return links != null && links.Count > 0 ? links.Last().OrderIndex : -1;
		}

		internal static async Task ClearRelatedCacheAsync(this Link link, string oldParentID, CancellationToken cancellationToken, string correlationID = null, int pageSize = 0)
		{
			// cache keys of the individual content
			var sort = Sorts<Link>.Ascending("OrderIndex").ThenByAscending("Title");
			var dataCacheKeys = Extensions.GetRelatedCacheKeys(link.GetCacheKey(), pageSize);

			dataCacheKeys = dataCacheKeys.Concat(Extensions.GetRelatedCacheKeys(LinkProcessor.GetLinksFilter(link.SystemID, link.RepositoryID, link.RepositoryEntityID, null), sort))
				.Concat(Extensions.GetRelatedCacheKeys(LinkProcessor.GetLinksFilter(null, null, link.RepositoryEntityID, null), sort))
				.Concat(Extensions.GetRelatedCacheKeys(LinkProcessor.GetLinksFilter(link.SystemID, link.RepositoryID, link.RepositoryEntityID, null, filter => filter.Add(Filters<Link>.Equals("Status", ApprovalStatus.Published.ToString()))), sort))
				.Concat(Extensions.GetRelatedCacheKeys(LinkProcessor.GetLinksFilter(null, null, link.RepositoryEntityID, null, filter => filter.Add(Filters<Link>.Equals("Status", ApprovalStatus.Published.ToString()))), sort))
				.ToList();

			new[] { link.ParentID, oldParentID }.Where(parentID => !string.IsNullOrWhiteSpace(parentID) && parentID.IsValidUUID()).ForEach(parentID => dataCacheKeys.Concat(Extensions.GetRelatedCacheKeys(LinkProcessor.GetLinksFilter(link.SystemID, link.RepositoryID, link.RepositoryEntityID, parentID), sort))
				.Concat(Extensions.GetRelatedCacheKeys(LinkProcessor.GetLinksFilter(null, null, link.RepositoryEntityID, parentID), sort))
				.Concat(Extensions.GetRelatedCacheKeys(LinkProcessor.GetLinksFilter(null, null, null, parentID), sort))
				.Concat(Extensions.GetRelatedCacheKeys(LinkProcessor.GetLinksFilter(link.SystemID, link.RepositoryID, link.RepositoryEntityID, parentID, filter => filter.Add(Filters<Link>.Equals("Status", ApprovalStatus.Published.ToString()))), sort))
				.Concat(Extensions.GetRelatedCacheKeys(LinkProcessor.GetLinksFilter(null, null, link.RepositoryEntityID, parentID, filter => filter.Add(Filters<Link>.Equals("Status", ApprovalStatus.Published.ToString()))), sort))
				.Concat(Extensions.GetRelatedCacheKeys(LinkProcessor.GetLinksFilter(null, null, null, parentID, filter => filter.Add(Filters<Link>.Equals("Status", ApprovalStatus.Published.ToString()))), sort))
				.ToList());

			// cache keys of the content-type
			if (link.ContentType != null)
				dataCacheKeys = (await Utility.Cache.GetSetMembersAsync(link.ContentType.GetSetCacheKey(), cancellationToken).ConfigureAwait(false)).Concat(dataCacheKeys).ToList();

			dataCacheKeys.Add(Extensions.GetCacheKey(LinkProcessor.GetLinksFilter(link.SystemID, link.RepositoryID, link.RepositoryEntityID, link.ID), sort, 0, 1));
			dataCacheKeys.Add(Extensions.GetCacheKey(LinkProcessor.GetLinksFilter(link.SystemID, link.RepositoryID, link.RepositoryEntityID, link.ID, filter => filter.Add(Filters<Link>.Equals("Status", ApprovalStatus.Published.ToString()))), sort, 0, 1));
			dataCacheKeys = dataCacheKeys.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

			var desktop = link.ContentType?.Desktop;
			var htmlCacheKeys = desktop != null
				? await Utility.Cache.GetSetMembersAsync(desktop.GetSetCacheKey(), cancellationToken).ConfigureAwait(false)
				: new HashSet<string>();

			if (desktop != null)
			{
				var desktopCacheKey = desktop.GetDesktopCacheKey(link.GetURL());
				htmlCacheKeys.Append(new[] { desktopCacheKey, $"{desktopCacheKey}:time" });
				var desktopURL = $"~/{desktop.Alias}/{link.ContentType?.Title.GetANSIUri() ?? "-"}" + "/{{pageNumber}}";
				for (var page = 1; page <= 10; page++)
				{
					desktopCacheKey = desktop.GetDesktopCacheKey(desktopURL.Replace(StringComparison.OrdinalIgnoreCase, "/{{pageNumber}}", page > 1 ? $"/{page}" : ""));
					htmlCacheKeys.Append(new[] { desktopCacheKey, $"{desktopCacheKey}:time" });
				}
			}

			htmlCacheKeys.Append(link.Organization.GetDesktopCacheKey());

			if (Utility.Logger.IsEnabled(LogLevel.Debug))
				await Utility.WriteLogAsync(correlationID, $"Clear related cache of CMS link [{link.ID} => {link.Title}]\r\n- {dataCacheKeys.Count} data keys => {dataCacheKeys.Join(", ")}\r\n- {htmlCacheKeys.Count} html keys => {htmlCacheKeys.Join(", ")}", CancellationToken.None, "Caches").ConfigureAwait(false);
			await Utility.Cache.RemoveAsync(htmlCacheKeys.Concat(dataCacheKeys).Distinct(StringComparer.OrdinalIgnoreCase).ToList(), cancellationToken).ConfigureAwait(false);

			await Task.WhenAll
			(
				link.Status != ApprovalStatus.Published ? Task.CompletedTask : link.GetURL().Replace("~/", $"{Utility.PortalsHttpURI}/~{link.Organization.Alias}/").RefreshWebPageAsync(1, correlationID),
				$"{Utility.PortalsHttpURI}/~{link.Organization.Alias}/{desktop?.Alias ?? "-default"}/{link.ContentType?.Title.GetANSIUri() ?? "-"}".RefreshWebPageAsync(1, correlationID),
				$"{Utility.PortalsHttpURI}/~{link.Organization.Alias}/".RefreshWebPageAsync(1, correlationID)
			).ConfigureAwait(false);
		}

		internal static Task ClearRelatedCacheAsync(this Link link, string correlationID, int pageSize = 0)
			=> link.ClearRelatedCacheAsync(null, CancellationToken.None, correlationID, pageSize);

		static async Task<Tuple<long, List<Link>, JToken, List<string>>> SearchAsync(this RequestInfo requestInfo, string query, IFilterBy<Link> filter, SortBy<Link> sort, int pageSize, int pageNumber, string contentTypeID = null, long totalRecords = -1, CancellationToken cancellationToken = default, bool searchThumbnails = true)
		{
			// cache keys
			var cacheKeyOfObjects = string.IsNullOrWhiteSpace(query) ? Extensions.GetCacheKey(filter, sort, pageSize, pageNumber) : null;
			var cacheKeyOfTotalObjects = string.IsNullOrWhiteSpace(query) ? Extensions.GetCacheKeyOfTotalObjects(filter, sort) : null;
			var cacheKeys = string.IsNullOrWhiteSpace(query) ? new List<string> { cacheKeyOfObjects, cacheKeyOfTotalObjects } : new List<string>();

			// count
			totalRecords = totalRecords > -1
				? totalRecords
				: string.IsNullOrWhiteSpace(query)
					? await Link.CountAsync(filter, contentTypeID, cacheKeyOfTotalObjects, cancellationToken).ConfigureAwait(false)
					: await Link.CountAsync(query, filter, contentTypeID, cancellationToken).ConfigureAwait(false);

			// search objects
			var objects = totalRecords > 0
				? string.IsNullOrWhiteSpace(query)
					? await Link.FindAsync(filter, sort, pageSize, pageNumber, contentTypeID, cacheKeyOfObjects, cancellationToken).ConfigureAwait(false)
					: await Link.SearchAsync(query, filter, pageSize, pageNumber, contentTypeID, cancellationToken).ConfigureAwait(false)
				: new List<Link>();

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
			return new Tuple<long, List<Link>, JToken, List<string>>(totalRecords, objects, thumbnails, cacheKeys);
		}

		internal static async Task<JObject> SearchLinksAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, CancellationToken cancellationToken = default)
		{
			// prepare
			var request = requestInfo.GetRequestExpando();
			var query = request.Get<string>("FilterBy.Query");
			var filter = request.Get<ExpandoObject>("FilterBy")?.ToFilterBy<Link>() ?? Filters<Link>.And();
			if (filter != null && filter is FilterBys<Link> filterBy)
			{
				if (!string.IsNullOrWhiteSpace(query))
				{
					var parentExp = filterBy.GetChild("ParentID");
					if (parentExp != null)
						filterBy.Children.Remove(parentExp);
				}
				else if (filterBy.GetChild("ParentID") == null)
					filterBy.Add(Filters<Link>.IsNull("ParentID"));
			}
			var sort = string.IsNullOrWhiteSpace(query) ? request.Get<ExpandoObject>("SortBy")?.ToSortBy<Link>() ?? Sorts<Link>.Ascending("OrderIndex").ThenByAscending("Title") : null;
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
			var contentType = await (contentTypeID ?? "").GetContentTypeByIDAsync(cancellationToken).ConfigureAwait(false);
			if (contentType == null || !contentType.SystemID.IsEquals(organization.ID) || !contentType.RepositoryID.IsEquals(module.ID))
				throw new InformationInvalidException("The content-type is invalid");

			// check permission
			var gotRights = isSystemAdministrator || requestInfo.Session.User.ID.IsEquals(organization.OwnerID) || requestInfo.Session.User.IsViewer(contentType.WorkingPrivileges);
			if (!gotRights)
				throw new AccessDeniedException();

			// normalize filter
			filter = filter == null || !(filter is FilterBys<Link>) || (filter as FilterBys<Link>).Children == null || (filter as FilterBys<Link>).Children.Count < 1
				? LinkProcessor.GetLinksFilter(organization.ID, module.ID, contentType.ID)
				: filter.Prepare(requestInfo);

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
			var results = await requestInfo.SearchAsync(query, filter, sort, pageSize, pageNumber, contentType.ID, pagination.Item1 > -1 ? pagination.Item1 : -1, cancellationToken).ConfigureAwait(false);
			var totalRecords = results.Item1;
			var objects = results.Item2;
			var thumbnails = results.Item3;

			// build response
			var totalPages = new Tuple<long, int>(totalRecords, pageSize).GetTotalPages();
			if (totalPages > 0 && pageNumber > totalPages)
				pageNumber = totalPages;
			pagination = new Tuple<long, int, int, int>(totalRecords, totalPages, pageSize, pageNumber);

			if (addChildren)
				await objects.Where(link => link._childrenIDs == null).ForEachAsync((link, _) => link.FindChildrenAsync(cancellationToken), cancellationToken, true, false).ConfigureAwait(false);

			var response = new JObject()
			{
				{ "FilterBy", filter.ToClientJson(query) },
				{ "SortBy", sort?.ToClientJson() },
				{ "Pagination", pagination.GetPagination() },
				{ "Objects", objects.Select(@object => @object.ToJson(addChildren, false, json => json["Thumbnails"] = thumbnails == null ? null : objects.Count == 1 ? thumbnails : thumbnails[@object.ID])).ToJArray() }
			};

			// update cache
			if (string.IsNullOrWhiteSpace(query) && !addChildren)
			{
				await Utility.Cache.SetAsync(cacheKeyOfObjectsJson, response.ToString(Formatting.None)).ConfigureAwait(false);
				var cacheKeys = new[] { cacheKeyOfObjectsJson }.Concat(results.Item4).ToList();
				Task.WhenAll(
					Utility.Cache.AddSetMembersAsync(contentType.GetSetCacheKey(), cacheKeys),
					Utility.Logger.IsEnabled(LogLevel.Debug) ? Utility.WriteLogAsync(requestInfo, $"Update cache when search CMS links\r\n- Cache key of JSON: {cacheKeyOfObjectsJson}\r\n- Cache key of realated sets: {contentType.GetSetCacheKey()}\r\n- Related cache keys: {cacheKeys.Join(", ")}", CancellationToken.None, "Caches") : Task.CompletedTask
				).Run();
			}

			// response
			return response;
		}

		internal static async Task<JObject> CreateLinkAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, CancellationToken cancellationToken = default)
		{
			// prepare
			var request = requestInfo.GetBodyExpando();

			var organizationID = request.Get<string>("SystemID") ?? requestInfo.GetParameter("x-system-id");
			var organization = await (organizationID ?? "").GetOrganizationByIDAsync(cancellationToken).ConfigureAwait(false);
			if (organization == null)
				throw new InformationInvalidException("The organization is invalid");

			var moduleID = request.Get<string>("RepositoryID") ?? requestInfo.GetParameter("x-module-id");
			var module = await (moduleID ?? "").GetModuleByIDAsync(cancellationToken).ConfigureAwait(false);
			if (module == null || !module.SystemID.IsEquals(organization.ID))
				throw new InformationInvalidException("The module is invalid");

			var contentTypeID = request.Get<string>("RepositoryEntityID") ?? requestInfo.GetParameter("x-content-type-id");
			var contentType = await (contentTypeID ?? "").GetContentTypeByIDAsync(cancellationToken).ConfigureAwait(false);
			if (contentType == null || !contentType.SystemID.IsEquals(organization.ID) || !contentType.RepositoryID.IsEquals(module.ID))
				throw new InformationInvalidException("The content-type is invalid");

			// check permission
			var gotRights = isSystemAdministrator || requestInfo.Session.User.ID.IsEquals(organization.OwnerID) || requestInfo.Session.User.IsModerator(contentType.WorkingPrivileges);
			if (!gotRights)
				throw new AccessDeniedException();

			var link = request.CreateLinkInstance("Privileges,OrderIndex,Created,CreatedID,LastModified,LastModifiedID", obj =>
			{
				obj.SystemID = organization.ID;
				obj.RepositoryID = module.ID;
				obj.RepositoryEntityID = contentType.ID;
				obj.ParentID = obj.ParentLink != null ? obj.ParentID : null;
				obj.ID = string.IsNullOrWhiteSpace(obj.ID) || !obj.ID.IsValidUUID() ? UtilityService.NewUUID : obj.ID;
				obj.Created = obj.LastModified = DateTime.Now;
				obj.CreatedID = obj.LastModifiedID = requestInfo.Session.User.ID;
				obj._childrenIDs = new List<string>();
			});

			link.OrderIndex = (await LinkProcessor.GetLastOrderIndexAsync(link.SystemID, link.RepositoryID, link.RepositoryEntityID, link.ParentID, cancellationToken).ConfigureAwait(false)) + 1;

			if (link.ChildrenMode.Equals(ChildrenMode.Normal) || string.IsNullOrWhiteSpace(link.LookupRepositoryID) || string.IsNullOrWhiteSpace(link.LookupRepositoryEntityID) || string.IsNullOrWhiteSpace(link.LookupRepositoryObjectID))
			{
				link.ChildrenMode = ChildrenMode.Normal;
				link.LookupRepositoryID = link.LookupRepositoryEntityID = link.LookupRepositoryObjectID = null;
			}

			// create new
			await Link.CreateAsync(link, cancellationToken).ConfigureAwait(false);
			link.ClearRelatedCacheAsync(requestInfo.CorrelationID).Run();

			var updateMessages = new List<UpdateMessage>();
			var communicateMessages = new List<CommunicateMessage>();
			var objectName = link.GetObjectName();

			// update parent
			var parentLink = link.ParentLink;
			while (parentLink != null)
			{
				parentLink.ClearRelatedCacheAsync(null, cancellationToken).Run();
				parentLink._children = null;
				parentLink._childrenIDs = null;
				await parentLink.FindChildrenAsync(cancellationToken, false).ConfigureAwait(false);
				await Utility.Cache.SetAsync(parentLink, cancellationToken).ConfigureAwait(false);

				var json = parentLink.ToJson(true, false);
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
				parentLink = parentLink.ParentLink;
			}

			// message to update to all other connected clients
			var response = link.ToJson(true, false);

			if (link.ParentLink == null)
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
			link.SendNotificationAsync("Create", link.ContentType.Notifications, ApprovalStatus.Draft, link.Status, requestInfo, cancellationToken).Run();

			// response
			return response;
		}

		internal static async Task<JObject> GetLinkAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, CancellationToken cancellationToken = default)
		{
			// prepare
			var link = await Link.GetAsync<Link>(requestInfo.GetObjectIdentity(true, true) ?? "", cancellationToken).ConfigureAwait(false);
			if (link == null)
				throw new InformationNotFoundException();
			else if (link.Organization == null || link.Module == null || link.ContentType == null)
				throw new InformationInvalidException("The organization/module/content-type is invalid");

			// check permission
			var gotRights = isSystemAdministrator || requestInfo.Session.User.ID.IsEquals(link.Organization.OwnerID) || requestInfo.Session.User.IsViewer(link.WorkingPrivileges);
			if (!gotRights)
				throw new AccessDeniedException();

			// refresh (clear cached and reload)
			var isRefresh = "refresh".IsEquals(requestInfo.GetObjectIdentity());
			if (isRefresh)
			{
				await link.ClearRelatedCacheAsync(null, cancellationToken).ConfigureAwait(false);
				await Utility.Cache.RemoveAsync(link, cancellationToken).ConfigureAwait(false);
				link = await Link.GetAsync<Link>(link.ID, cancellationToken).ConfigureAwait(false);
				link._childrenIDs = null;
			}

			// prepare the response
			if (link._childrenIDs == null)
			{
				await link.FindChildrenAsync(cancellationToken, false).ConfigureAwait(false);
				await Utility.Cache.SetAsync(link, cancellationToken).ConfigureAwait(false);
			}

			// send update messages
			var response = link.ToJson(true, false);
			await Utility.RTUService.SendUpdateMessageAsync(new UpdateMessage
			{
				Type = $"{requestInfo.ServiceName}#{link.GetObjectName()}#Update",
				Data = response,
				DeviceID = "*",
				ExcludedDeviceID = requestInfo.Session.DeviceID
			}, cancellationToken).ConfigureAwait(false);

			// response
			return response;
		}

		internal static async Task<JObject> UpdateLinkAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, CancellationToken cancellationToken = default)
		{
			// prepare
			var link = await Link.GetAsync<Link>(requestInfo.GetObjectIdentity() ?? "", cancellationToken).ConfigureAwait(false);
			if (link == null)
				throw new InformationNotFoundException();
			else if (link.Organization == null || link.Module == null || link.ContentType == null)
				throw new InformationInvalidException("The organization/module/content-type is invalid");

			// check permission
			var gotRights = isSystemAdministrator || requestInfo.Session.User.ID.IsEquals(link.Organization.OwnerID) || requestInfo.Session.User.IsEditor(link.WorkingPrivileges);
			if (!gotRights)
				throw new AccessDeniedException();

			var oldParentID = link.ParentID;
			var oldStatus = link.Status;
			link.UpdateLinkInstance(requestInfo.GetBodyExpando(), "ID,SystemID,RepositoryID,RepositoryEntityID,Privileges,OrderIndex,Created,CreatedID,LastModified,LastModifiedID", obj =>
			{
				obj.LastModified = DateTime.Now;
				obj.LastModifiedID = requestInfo.Session.User.ID;
			});

			if (link.ChildrenMode.Equals(ChildrenMode.Normal) || string.IsNullOrWhiteSpace(link.LookupRepositoryID) || string.IsNullOrWhiteSpace(link.LookupRepositoryEntityID) || string.IsNullOrWhiteSpace(link.LookupRepositoryObjectID))
			{
				link.ChildrenMode = ChildrenMode.Normal;
				link.LookupRepositoryID = link.LookupRepositoryEntityID = link.LookupRepositoryObjectID = null;
			}

			if (link.ParentLink != null && !link.ParentID.IsEquals(oldParentID))
			{
				link.OrderIndex = (await LinkProcessor.GetLastOrderIndexAsync(link.SystemID, link.RepositoryID, link.RepositoryEntityID, link.ParentID, cancellationToken).ConfigureAwait(false)) + 1;
				await link.FindChildrenAsync(cancellationToken, false).ConfigureAwait(false);
			}

			// update
			await Link.UpdateAsync(link, requestInfo.Session.User.ID, cancellationToken).ConfigureAwait(false);
			link.ClearRelatedCacheAsync(oldParentID, cancellationToken, requestInfo.CorrelationID).Run();

			var updateMessages = new List<UpdateMessage>();
			var communicateMessages = new List<CommunicateMessage>();
			var objectName = link.GetObjectName();

			// update parent
			var parentLink = link.ParentLink;
			while (parentLink != null)
			{
				parentLink.ClearRelatedCacheAsync(null, cancellationToken).Run();
				parentLink._children = null;
				parentLink._childrenIDs = null;
				await parentLink.FindChildrenAsync(cancellationToken, false).ConfigureAwait(false);
				await Utility.Cache.SetAsync(parentLink, cancellationToken).ConfigureAwait(false);

				var json = parentLink.ToJson(true, false);
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
				parentLink = parentLink.ParentLink;
			}

			// update old parent
			if (!string.IsNullOrWhiteSpace(oldParentID) && !oldParentID.IsEquals(link.ParentID))
			{
				parentLink = await Link.GetAsync<Link>(oldParentID, cancellationToken).ConfigureAwait(false);
				if (parentLink != null)
				{
					parentLink.ClearRelatedCacheAsync(null, cancellationToken).Run();
					parentLink._children = null;
					parentLink._childrenIDs = null;
					await parentLink.FindChildrenAsync(cancellationToken, false).ConfigureAwait(false);
					parentLink._childrenIDs.Remove(link.ID);
					await Utility.Cache.SetAsync(parentLink, cancellationToken).ConfigureAwait(false);

					var json = parentLink.ToJson(true, false);
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
					parentLink = parentLink.ParentLink;
				}
			}

			// message to update to all other connected clients
			var response = link.ToJson(true, false);
			if (link.ParentLink == null)
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

			// send messages
			await Task.WhenAll(
				updateMessages.ForEachAsync((message, token) => Utility.RTUService.SendUpdateMessageAsync(message, token), cancellationToken, true, false),
				communicateMessages.ForEachAsync((message, token) => Utility.RTUService.SendInterCommunicateMessageAsync(message, token), cancellationToken)
			).ConfigureAwait(false);

			// send notification
			link.SendNotificationAsync("Update", link.ContentType.Notifications, oldStatus, link.Status, requestInfo, cancellationToken).Run();

			// response
			return response;
		}

		internal static async Task<JObject> UpdateLinksAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, CancellationToken cancellationToken = default)
		{
			// prepare
			var request = requestInfo.GetBodyJson();

			var link = await Link.GetAsync<Link>(request.Get<string>("LinkID") ?? requestInfo.GetParameter("x-link-id") ?? "", cancellationToken).ConfigureAwait(false);
			var organization = link != null
				? link.Organization
				: await (request.Get<string>("SystemID") ?? requestInfo.GetParameter("x-system-id") ?? "").GetOrganizationByIDAsync(cancellationToken).ConfigureAwait(false);
			if (organization == null)
				throw new InformationInvalidException("The organization is invalid");
			var module = link != null
				? link.Module
				: await (request.Get<string>("RepositoryID") ?? requestInfo.GetParameter("x-module-id") ?? "").GetModuleByIDAsync(cancellationToken).ConfigureAwait(false);
			if (module == null || !module.SystemID.IsEquals(organization.ID))
				throw new InformationInvalidException("The module is invalid");
			var contentType = link != null
				? link.ContentType
				: await (request.Get<string>("RepositoryEntityID") ?? requestInfo.GetParameter("x-content-type-id") ?? "").GetContentTypeByIDAsync(cancellationToken).ConfigureAwait(false);
			if (contentType == null || !contentType.SystemID.IsEquals(organization.ID) || !contentType.RepositoryID.IsEquals(module.ID))
				throw new InformationInvalidException("The content-type is invalid");

			// check permission
			var gotRights = isSystemAdministrator || requestInfo.Session.User.ID.IsEquals(organization.OwnerID) || requestInfo.Session.User.IsModerator(contentType.WorkingPrivileges);
			if (!gotRights)
				throw new AccessDeniedException();

			// update
			var updateMessages = new List<UpdateMessage>();
			var communicateMessages = new List<CommunicateMessage>();
			var objectName = link != null
				? link.GetObjectName()
				: typeof(Link).GetTypeName(true);

			var items = link != null
				? link.Children
				: await organization.ID.FindLinksAsync(module.ID, contentType.ID, null, cancellationToken).ConfigureAwait(false);

			var notificationTasks = new List<Task>();
			await request.Get<JArray>("Links").ForEachAsync(async (info, _) =>
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

					var json = item.ToJson(true, false);
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
			});

			if (link != null)
			{
				await link.ClearRelatedCacheAsync(null, cancellationToken).ConfigureAwait(false);
				link._childrenIDs = null;
				await link.FindChildrenAsync(cancellationToken, false).ConfigureAwait(false);
				await Utility.Cache.SetAsync(link, cancellationToken).ConfigureAwait(false);
				var json = link.ToJson(true, false);
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

			// send update messages
			await Task.WhenAll(
				updateMessages.ForEachAsync((message, token) => Utility.RTUService.SendUpdateMessageAsync(message, token), cancellationToken, true, false),
				communicateMessages.ForEachAsync((message, token) => Utility.RTUService.SendInterCommunicateMessageAsync(message, token), cancellationToken)
			).ConfigureAwait(false);

			// send notifications
			Task.WhenAll(notificationTasks).Run();

			// response
			return new JObject();
		}

		internal static async Task<JObject> DeleteLinkAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, CancellationToken cancellationToken = default)
		{
			// prepare
			var link = await Link.GetAsync<Link>(requestInfo.GetObjectIdentity() ?? "", cancellationToken).ConfigureAwait(false);
			if (link == null)
				throw new InformationNotFoundException();
			else if (link.Organization == null || link.Module == null || link.ContentType == null)
				throw new InformationInvalidException("The organization/module/content-type is invalid");

			// check permission
			var gotRights = isSystemAdministrator || requestInfo.Session.User.ID.IsEquals(link.Organization.OwnerID) || requestInfo.Session.User.IsModerator(link.WorkingPrivileges);
			if (!gotRights)
				throw new AccessDeniedException();

			// delete
			var updateMessages = new List<UpdateMessage>();
			var communicateMessages = new List<CommunicateMessage>();
			var objectName = link.GetObjectName();
			var updateChildren = requestInfo.Header.TryGetValue("x-children", out var childrenMode) && "set-null".IsEquals(childrenMode);

			var children = await link.FindChildrenAsync(cancellationToken, false).ConfigureAwait(false) ?? new List<Link>();
			await children.Where(child => child != null).ForEachAsync(async (child, _) =>
			{
				// update children to root
				if (updateChildren)
				{
					child.ParentID = null;
					child.LastModified = DateTime.Now;
					child.LastModifiedID = requestInfo.Session.User.ID;

					await Link.UpdateAsync(child, requestInfo.Session.User.ID, cancellationToken).ConfigureAwait(false);
					child.SendNotificationAsync("Update", child.ContentType.Notifications, child.Status, child.Status, requestInfo, cancellationToken).Run();

					var json = child.ToJson(true, false);
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
					var messages = await child.DeleteChildrenAsync(requestInfo, cancellationToken).ConfigureAwait(false);
					updateMessages = updateMessages.Concat(messages.Item1).ToList();
					communicateMessages = communicateMessages.Concat(messages.Item2).ToList();
				}
			}, cancellationToken, true, false).ConfigureAwait(false);

			await Link.DeleteAsync<Link>(link.ID, requestInfo.Session.User.ID, cancellationToken).ConfigureAwait(false);
			await link.ClearRelatedCacheAsync(requestInfo.CorrelationID).ConfigureAwait(false);

			// message to update to all other connected clients
			var response = link.ToJson();
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
			await Task.WhenAll
			(
				updateMessages.ForEachAsync((message, _) => Utility.RTUService.SendUpdateMessageAsync(message, cancellationToken), cancellationToken, true, false),
				communicateMessages.ForEachAsync((message, _) => Utility.RTUService.SendInterCommunicateMessageAsync(message, cancellationToken), cancellationToken)
			).ConfigureAwait(false);

			// send notification
			link.SendNotificationAsync("Delete", link.ContentType.Notifications, link.Status, link.Status, requestInfo, cancellationToken).Run();

			// response
			return response;
		}

		static async Task<Tuple<List<UpdateMessage>, List<CommunicateMessage>>> DeleteChildrenAsync(this Link link, RequestInfo requestInfo, CancellationToken cancellationToken = default)
		{
			var updateMessages = new List<UpdateMessage>();
			var communicateMessages = new List<CommunicateMessage>();
			var objectName = link.GetObjectName();

			var children = await link.FindChildrenAsync(cancellationToken, false).ConfigureAwait(false) ?? new List<Link>();
			await children.Where(child => child != null).ForEachAsync(async (child, _) =>
			{
				var messages = await child.DeleteChildrenAsync(requestInfo, cancellationToken).ConfigureAwait(false);
				updateMessages = updateMessages.Concat(messages.Item1).ToList();
				communicateMessages = communicateMessages.Concat(messages.Item2).ToList();
			}, cancellationToken, true, false).ConfigureAwait(false);

			await Link.DeleteAsync<Link>(link.ID, requestInfo.Session.User.ID, cancellationToken).ConfigureAwait(false);

			var json = link.ToJson();
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

			link.SendNotificationAsync("Delete", link.ContentType.Notifications, link.Status, link.Status, requestInfo, cancellationToken).Run();
			return new Tuple<List<UpdateMessage>, List<CommunicateMessage>>(updateMessages, communicateMessages);
		}

		internal static async Task<JObject> GenerateAsync(RequestInfo requestInfo, bool isSystemAdministrator = false, CancellationToken cancellationToken = default)
		{
			// prepare
			var requestJson = requestInfo.BodyAsJson;
			var id = requestJson.Get<string>("ID");
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
			var parentID = requestJson.Get<string>("ParentIdentity");
			var cultureInfo = CultureInfo.GetCultureInfo(requestJson.Get("Language", "vi-VN"));

			var asMenu = "Menu".IsEquals(options.Get<string>("DisplayMode")) || options.Get("AsMenu", options.Get("ShowAsMenu", options.Get("GenerateAsMenu", false)));
			var asBanner = !asMenu && ("Banner".IsEquals(options.Get<string>("DisplayMode")) || options.Get("AsBanner", options.Get("ShowAsBanner", options.Get("GenerateAsBanner", false))));
			var xslFilename = asMenu ? "menu.xsl" : asBanner ? "banner.xsl" : null;

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
			if (!(expressionJson.Get<JObject>("FilterBy")?.ToFilter<Link>() is FilterBys<Link> filter) || filter.Children == null || filter.Children.Count < 1)
				filter = Filters<Link>.And
				(
					Filters<Link>.Equals("SystemID", "@request.Body(Organization.ID)"),
					Filters<Link>.Equals("RepositoryID", "@request.Body(Module.ID)"),
					Filters<Link>.Equals("RepositoryEntityID", "@request.Body(ContentType.ID)"),
					string.IsNullOrWhiteSpace(parentID) || !parentID.IsValidUUID() ? Filters<Link>.IsNull("ParentID") : Filters<Link>.Equals("ParentID", parentID),
					Filters<Link>.Equals("Status", ApprovalStatus.Published.ToString())
				);

			if (filter.GetChild("RepositoryEntityID") == null && contentType != null)
				filter.Add(Filters<Link>.Equals("RepositoryEntityID", contentType.ID));

			if (filter.GetChild("ParentID") == null)
				filter.Add(string.IsNullOrWhiteSpace(parentID) || !parentID.IsValidUUID() ? Filters<Link>.IsNull("ParentID") : Filters<Link>.Equals("ParentID", parentID));

			if (filter.GetChild("Status") == null)
				filter.Add(Filters<Link>.Equals("Status", ApprovalStatus.Published.ToString()));

			var filterBy = new JObject
			{
				{ "API", filter.ToJson().ToString(Formatting.None) },
			};
			filter.Prepare(requestInfo);
			filterBy["App"] = filter.ToClientJson().ToString(Formatting.None);

			// prepare sorting expression
			var sort = expressionJson.Get<JObject>("SortBy")?.ToSort<Link>() ?? Sorts<Link>.Ascending("OrderIndex").ThenByAscending("Title");
			var sortBy = new JObject
			{
				{ "API", sort.ToJson().ToString(Formatting.None) },
				{ "App", sort.ToClientJson().ToString(Formatting.None) }
			};

			// options
			var showThumbnails = options.Get("ShowThumbnails", options.Get("ShowThumbnail", false)) || options.Get("ShowPngThumbnails", false) || options.Get("ShowAsPngThumbnails", false) || options.Get("ShowBigThumbnails", false) || options.Get("ShowAsBigThumbnails", false);
			var pngThumbnails = options.Get("ThumbnailsAsPng", options.Get("ThumbnailAsPng", options.Get("ShowPngThumbnails", options.Get("ShowAsPngThumbnails", false))));
			var bigThumbnails = options.Get("ThumbnailsAsBig", options.Get("ThumbnailAsBig", options.Get("ShowBigThumbnails", options.Get("ShowAsBigThumbnails", false))));
			var thumbnailsWidth = options.Get("ThumbnailsWidth", options.Get("ThumbnailWidth", 0));
			var thumbnailsHeight = options.Get("ThumbnailsHeight", options.Get("ThumbnailHeight", 0));

			var level = options.Get("Level", 1);
			var maxLevel = options.Get("MaxLevel", 0);
			var addChildren = options.Get("ShowChildrens", options.Get("ShowChildren", options.Get("AddChildrens", options.Get("AddChildren", false))));
			string data = null;

			// get parent
			var parent = filter.GetChild("ParentID") is FilterBy<Link> parentFilter && parentFilter.Operator.Equals(CompareOperator.Equals) && parentFilter.Value != null
				? await Link.GetAsync<Link>(parentFilter.Value as string, cancellationToken).ConfigureAwait(false)
				: null;

			// as lookup
			var asLookup = options.Get("AsLookup", false);
			if (asLookup && parent != null)
			{
				// prepare parent info
				requestInfo.Header["x-thumbnails-as-attachments"] = "true";
				var thumbnails = await requestInfo.GetThumbnailsAsync(parent.ID, parent.Title.Url64Encode(), Utility.ValidationKey, cancellationToken).ConfigureAwait(false);
				var thumbnailURL = thumbnails?.GetThumbnailURL(parent.ID, pngThumbnails, bigThumbnails, thumbnailsWidth, thumbnailsHeight) ?? "";

				// generate links (as lookup)
				var linkJson = await requestInfo.GenerateLinkAsync(parent, thumbnailURL, addChildren, level, maxLevel, pngThumbnails, bigThumbnails, thumbnailsWidth, thumbnailsHeight, cancellationToken).ConfigureAwait(false);

				// generate xml
				var dataXml = linkJson.Get<JObject>("Children").ToXml("Data", xml =>
				{
					var element = xml.Element("ThumbnailURL");
					if (element != null)
						element.Add(new XAttribute("Alternative", element.Value?.GetWebpImageURL(pngThumbnails)));
				});
				dataXml.Add(new XElement(
					"Parent",
					new XElement("Title", parent.Title),
					new XElement("Description", parent.Summary?.NormalizeHTMLBreaks()),
					new XElement("URL", parent.GetURL(desktop)),
					new XElement("ThumbnailURL", thumbnailURL, new XAttribute("Alternative", thumbnailURL?.GetWebpImageURL(pngThumbnails) ?? ""))
				));

				// get xml data
				data = dataXml.CleanInvalidCharacters().ToString(SaveOptions.DisableFormatting);
			}
			else
			{
				var results = await requestInfo.SearchAsync(null, filter, sort, pageSize, pageNumber, contentTypeID, -1, cancellationToken).ConfigureAwait(false);
				var totalRecords = results.Item1;
				var objects = results.Item2;
				var thumbnails = results.Item3;

				// prepare pagination
				var totalPages = new Tuple<long, int>(totalRecords, pageSize).GetTotalPages();
				if (totalPages > 0 && pageNumber > totalPages)
					pageNumber = totalPages;

				// generate xml
				Exception exception = null;
				var dataXml = XElement.Parse("<Data/>");
				await objects.ForEachAsync(async (@object, _) =>
				{
					// check
					if (exception != null)
						return;

					// generate
					try
					{
						// get thumbnails
						var thumbnailURL = thumbnails?.GetThumbnailURL(@object.ID, pngThumbnails, bigThumbnails, thumbnailsWidth, thumbnailsHeight);

						// generate xml of each item
						var itemXml = asMenu
							? (await requestInfo.GenerateMenuAsync(@object, thumbnailURL, level, maxLevel, pngThumbnails, bigThumbnails, thumbnailsWidth, thumbnailsHeight, cancellationToken).ConfigureAwait(false)).ToXml("Menu")
							: (await requestInfo.GenerateLinkAsync(@object, thumbnailURL, addChildren, level, maxLevel, pngThumbnails, bigThumbnails, thumbnailsWidth, thumbnailsHeight, cancellationToken).ConfigureAwait(false)).ToXml("Link", xml =>
							{
								var element = xml.Element("ThumbnailURL");
								if (element != null)
									element.Add(new XAttribute("Alternative", element.Value?.GetWebpImageURL(pngThumbnails)));
							});

						// get and generate attachments when show as a banner
						if (asBanner)
						{
							var attachments = new XElement("Attachments");
							(await requestInfo.GetAttachmentsAsync(@object.ID, @object.Title.Url64Encode(), Utility.ValidationKey, cancellationToken).ConfigureAwait(false) as JArray)?.Select(attachment => new JObject
							{
								{ "Title", attachment["Title"] },
								{ "Filename", attachment["Filename"] },
								{ "Size", attachment["Size"] },
								{ "ContentType", attachment["ContentType"] },
								{ "Downloads", attachment["Downloads"] },
								{ "URIs", attachment["URIs"] }
							}).ForEach(attachment => attachments.Add(attachment.ToXml("Attachment")));
							itemXml.Add(attachments);
						}

						// update the element
						dataXml.Add(itemXml);
					}
					catch (Exception ex)
					{
						exception = requestInfo.GetRuntimeException(ex, null, (msg, exc) => requestInfo.WriteErrorAsync(exc, cancellationToken, $"Error occurred while generating a link => {msg} : {@object.ToJson()}", "Errors").Run());
					}
				}, cancellationToken, true, false).ConfigureAwait(false);

				// check error
				if (exception != null)
					throw exception;

				// update parent
				if (parent != null)
				{
					requestInfo.Header["x-thumbnails-as-attachments"] = "true";
					thumbnails = await requestInfo.GetThumbnailsAsync(parent.ID, parent.Title.Url64Encode(), Utility.ValidationKey, cancellationToken).ConfigureAwait(false);
					var thumbnailURL = thumbnails?.GetThumbnailURL(parent.ID, pngThumbnails, bigThumbnails, thumbnailsWidth, thumbnailsHeight) ?? "";
					dataXml.Add(new XElement(
						"Parent",
						new XElement("Title", parent.Title),
						new XElement("Description", parent.Summary?.NormalizeHTMLBreaks()),
						new XElement("URL", parent.GetURL(desktop)),
						new XElement("ThumbnailURL", thumbnailURL, new XAttribute("Alternative", thumbnailURL?.GetWebpImageURL(pngThumbnails) ?? ""))
					));
				}

				// get data
				data = dataXml.CleanInvalidCharacters().ToString(SaveOptions.DisableFormatting);

				// update cache
				var expression = await expressionJson.Get("ID", "").GetExpressionByIDAsync(cancellationToken).ConfigureAwait(false);
				Task.WhenAll(
					contentType != null ? Utility.Cache.AddSetMembersAsync(contentType.GetSetCacheKey(), results.Item4) : Task.CompletedTask,
					expression != null ? Utility.Cache.AddSetMembersAsync(expression.GetSetCacheKey(), results.Item4) : Task.CompletedTask,
					Utility.Logger.IsEnabled(LogLevel.Debug) ? Utility.WriteLogAsync(requestInfo, $"Update cache keys into related sets when generate CMS links\r\n- Related sets: {new[] { contentType != null ? contentType.GetSetCacheKey() : null, expression != null ? expression.GetSetCacheKey() : null }.Where(key => key != null).Join(", ")}\r\n- Related cache keys ({results.Item4.Count}): {results.Item4.Join(", ")}", CancellationToken.None, "Caches") : Task.CompletedTask
				).Run();
			}

			// response
			return new JObject
			{
				{ "Data", data },
				{ "XslFilename", xslFilename },
				{ "FilterBy", filterBy },
				{ "SortBy", sortBy }
			};
		}

		internal static async Task<JObject> GenerateLinkAsync(this RequestInfo requestInfo, Link link, string thumbnailURL, bool addChildren, int level, int maxLevel = 0, bool pngThumbnails = false, bool bigThumbnails = false, int thumbnailsWidth = 0, int thumbnailsHeight = 0, CancellationToken cancellationToken = default)
		{
			var linkJson = link.ToJson(
				addChildren,
				false,
				json =>
				{
					json["URL"] = link.GetURL();
					json["ThumbnailURL"] = thumbnailURL;
					json.Remove("Privileges");
				},
				async json =>
				{
					var clink = await Link.GetAsync<Link>(json.Get<string>("ID"), cancellationToken).ConfigureAwait(false);
					if (clink != null)
					{
						var thumbnails = await requestInfo.GetThumbnailsAsync(clink.ID, clink.Title.Url64Encode(), Utility.ValidationKey, cancellationToken).ConfigureAwait(false);
						json["URL"] = clink.GetURL();
						json["ThumbnailURL"] = thumbnails?.GetThumbnailURL(clink.ID, pngThumbnails, bigThumbnails, thumbnailsWidth, thumbnailsHeight) ?? "";
					}
					json.Remove("Privileges");
				},
				level,
				maxLevel
			);

			if (addChildren && (maxLevel < 1 || level < maxLevel))
			{
				if (link.ChildrenMode.Equals(ChildrenMode.Normal))
				{
					var children = linkJson.Get<JArray>("Children");
					if (children != null && children.Count > 0)
						linkJson["Children"] = new JObject
						{
							{ "Link", children }
						};
				}

				else if (!string.IsNullOrWhiteSpace(link.LookupRepositoryID) && !string.IsNullOrWhiteSpace(link.LookupRepositoryEntityID) && !string.IsNullOrWhiteSpace(link.LookupRepositoryObjectID))
				{
					var contentType = await link.LookupRepositoryEntityID.GetContentTypeByIDAsync(cancellationToken).ConfigureAwait(false);
					if (contentType != null && contentType.RepositoryID.IsEquals(link.LookupRepositoryID))
					{
						var requestJson = requestInfo.BodyAsJson;
						requestJson["ContentTypeDefinition"] = contentType.ContentTypeDefinition?.ToJson();
						requestJson["ModuleDefinition"] = contentType.ContentTypeDefinition?.ModuleDefinition?.ToJson(json =>
						{
							(json as JObject).Remove("ContentTypeDefinitions");
							(json as JObject).Remove("ObjectDefinitions");
						});
						requestJson["Module"] = contentType.Module?.ToJson(json =>
						{
							ModuleProcessor.ExtraProperties.ForEach(name => json.Remove(name));
							json.Remove("Privileges");
							json.Remove("OriginalPrivileges");
							json["Description"] = contentType.Module.Description?.NormalizeHTMLBreaks();
						});
						requestJson["ContentType"] = contentType.ToJson(json =>
						{
							ModuleProcessor.ExtraProperties.ForEach(name => json.Remove(name));
							json.Remove("Privileges");
							json.Remove("OriginalPrivileges");
							json["Description"] = contentType.Description?.NormalizeHTMLBreaks();
						});

						requestJson["Expression"] = new JObject();
						requestJson["ParentIdentity"] = link.LookupRepositoryObjectID;
						requestJson.Get<JObject>("Options").Add("Level", new JValue(level + 1));

						var request = new RequestInfo(requestInfo)
						{
							ObjectName = contentType.ContentTypeDefinition.GetObjectName(),
							Body = requestJson.ToString(Formatting.None)
						};
						try
						{
							var children = await contentType.GetService().GenerateAsync(request, cancellationToken).ConfigureAwait(false);
							var links = children?.Get<JArray>("Data");
							if (links != null && links.Count > 0)
								linkJson["Children"] = new JObject
								{
									{ "Link", links }
								};
						}
						catch (Exception ex)
						{
							await request.WriteErrorAsync(ex, cancellationToken, "Error occurred while fetching links from other module/content-type", "Links", $"> Parent: {link.ToJson()}\r\n> Request: {request.ToJson()}").ConfigureAwait(false);
						}
					}
				}
			}

			return linkJson;
		}

		internal static async Task<JObject> GenerateMenuAsync(this RequestInfo requestInfo, Link link, string thumbnailURL, int level, int maxLevel = 0, bool pngThumbnails = false, bool bigThumbnails = false, int thumbnailsWidth = 0, int thumbnailsHeight = 0, CancellationToken cancellationToken = default)
		{
			// generate the menu item
			var menu = new JObject
			{
				{ "ID", link.ID },
				{ "Title", link.Title },
				{ "Description", link.Summary?.NormalizeHTMLBreaks() },
				{ "Image", thumbnailURL },
				{ "URL", link.GetURL() },
				{ "Target", link.Target },
				{ "Level", level },
				{ "Selected", false }
			};

			// generate children
			JArray subMenu = null;
			if (maxLevel < 1 || level < maxLevel)
			{
				// normal children
				if (link.ChildrenMode.Equals(ChildrenMode.Normal))
				{
					if (link._childrenIDs == null)
						await link.FindChildrenAsync(cancellationToken).ConfigureAwait(false);

					var children = link.Children;
					if (children.Count > 0)
					{
						var thumbnails = children.Count == 1
							? await requestInfo.GetThumbnailsAsync(children[0].ID, children[0].Title.Url64Encode(), Utility.ValidationKey, cancellationToken).ConfigureAwait(false)
							: await requestInfo.GetThumbnailsAsync(children.Select(child => child.ID).Join(","), children.ToJObject("ID", child => new JValue(child.Title.Url64Encode())).ToString(Formatting.None), Utility.ValidationKey, cancellationToken).ConfigureAwait(false);
						subMenu = new JArray();
						await children.ForEachAsync(async (child, token) => subMenu.Add(await requestInfo.GenerateMenuAsync(child, thumbnails?.GetThumbnailURL(child.ID, pngThumbnails, bigThumbnails, thumbnailsWidth, thumbnailsHeight), level + 1, maxLevel, pngThumbnails, bigThumbnails, thumbnailsWidth, thumbnailsHeight, token).ConfigureAwait(false)), cancellationToken, true, false).ConfigureAwait(false);
					}
				}

				// children that looked-up from other module/content-type
				else if (!string.IsNullOrWhiteSpace(link.LookupRepositoryID) && !string.IsNullOrWhiteSpace(link.LookupRepositoryEntityID) && !string.IsNullOrWhiteSpace(link.LookupRepositoryObjectID))
				{
					var contentType = await link.LookupRepositoryEntityID.GetContentTypeByIDAsync(cancellationToken).ConfigureAwait(false);
					subMenu = contentType == null ? null : await contentType.GetService().GenerateMenuAsync(new RequestInfo(requestInfo)
					{
						Header = new Dictionary<string, string>(requestInfo.Header, StringComparer.OrdinalIgnoreCase)
						{
							{ "x-menu-repository-id", link.LookupRepositoryID },
							{ "x-menu-repository-entity-id", link.LookupRepositoryEntityID },
							{ "x-menu-repository-object-id", link.LookupRepositoryObjectID },
							{ "x-menu-level", $"{level + 1}" },
							{ "x-menu-maxlevel", $"{maxLevel}" }
						}
					}, cancellationToken).ConfigureAwait(false);
				}

				// update children
				if (subMenu != null && subMenu.Count > 0)
					menu["SubMenu"] = new JObject
					{
						{ "Menu", subMenu }
					};
			}

			// update 'Selected' state
			menu["Selected"] = subMenu?.Select(smenu => smenu as JObject).FirstOrDefault(smenu => smenu.Get<bool>("Selected", false)) != null || requestInfo.IsSelected(link.URL);

			// return the menu item
			return menu;
		}

		internal static bool IsSelected(this RequestInfo requestInfo, string url)
		{
			if (!string.IsNullOrWhiteSpace(url))
			{
				var requestedURL = requestInfo.GetParameter("x-url") ?? requestInfo.GetParameter("x-uri");
				var requestedURI = new Uri(requestedURL);
				requestedURL = requestedURL.Replace(StringComparison.OrdinalIgnoreCase, $"{requestedURI.Scheme}://{requestedURI.Host}/", "~/").Replace(StringComparison.OrdinalIgnoreCase, $"/~{requestInfo.GetParameter("x-system")}", "/").Replace("//", "/");
				var position = requestedURL.IndexOf(".html");
				if (position > 0)
					requestedURL = requestedURL.Left(position);
				position = requestedURL.IndexOf(".aspx");
				if (position > 0)
					requestedURL = requestedURL.Left(position);
				position = requestedURL.IndexOf(".php");
				if (position > 0)
					requestedURL = requestedURL.Left(position);
				return url.IsEquals("~/") || url.IsEquals("~/index") || url.IsEquals("~/index.html") || url.IsEquals("~/index.aspx") || url.IsEquals("~/default") || url.IsEquals("~/default.html") || url.IsEquals("~/default.aspx")
					? requestedURL.IsEquals("~/") || requestedURL.IsEquals("~/index") || requestedURL.IsEquals("~/default")
					: requestedURL.IsStartsWith(url.Replace(".html", ""));
			}
			return false;
		}

		internal static async Task<JObject> SyncLinkAsync(this RequestInfo requestInfo, CancellationToken cancellationToken = default)
		{
			var data = requestInfo.GetBodyExpando();
			var link = await Link.GetAsync<Link>(data.Get<string>("ID"), cancellationToken).ConfigureAwait(false);
			if (link == null)
			{
				link = Link.CreateInstance(data);
				await Link.CreateAsync(link, cancellationToken).ConfigureAwait(false);
			}
			else
			{
				link.Fill(data);
				await Link.UpdateAsync(link, true, cancellationToken).ConfigureAwait(false);
			}

			// clear related cache
			//link.ClearRelatedCacheAsync(requestInfo.CorrelationID).Run();

			// send update messages
			var json = link.ToJson();
			var objectName = link.GetObjectName();
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
				{ "ID", link.ID },
				{ "Type", objectName }
			};
		}
	}
}
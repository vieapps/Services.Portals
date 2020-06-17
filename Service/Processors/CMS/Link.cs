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
#endregion

namespace net.vieapps.Services.Portals
{
	public static class LinkProcessor
	{
		public static Link CreateLinkInstance(this ExpandoObject requestBody, string excluded = null, Action<Link> onCompleted = null)
			=> requestBody.Copy<Link>(excluded?.ToHashSet(), link =>
			{
				link.OriginalPrivileges = link.OriginalPrivileges?.Normalize();
				link.TrimAll();
				onCompleted?.Invoke(link);
			});

		public static Link UpdateLinkInstance(this Link link, ExpandoObject requestBody, string excluded = null, Action<Link> onCompleted = null)
		{
			link.CopyFrom(requestBody, excluded?.ToHashSet());
			link.OriginalPrivileges = link.OriginalPrivileges?.Normalize();
			link.TrimAll();
			onCompleted?.Invoke(link);
			return link;
		}

		public static IFilterBy<Link> GetLinksFilter(string systemID, string repositoryID = null, string repositoryEntityID = null, string parentID = null)
		{
			var filter = Filters<Link>.And();
			if (!string.IsNullOrWhiteSpace(systemID))
				filter.Add(Filters<Link>.Equals("SystemID", systemID));
			if (!string.IsNullOrWhiteSpace(repositoryID))
				filter.Add(Filters<Link>.Equals("RepositoryID", repositoryID));
			if (!string.IsNullOrWhiteSpace(repositoryEntityID))
				filter.Add(Filters<Link>.Equals("RepositoryEntityID", repositoryEntityID));
			filter.Add(string.IsNullOrWhiteSpace(parentID) ? Filters<Link>.IsNull("ParentID") : Filters<Link>.Equals("ParentID", parentID));
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

		static Task ClearRelatedCacheAsync(this Link link, string oldParentID = null, IRTUService rtuService = null, CancellationToken cancellationToken = default)
		{
			var sort = Sorts<Link>.Ascending("OrderIndex").ThenByAscending("Title");
			var tasks = new List<Task>
			{
				Utility.Cache.RemoveAsync(Extensions.GetRelatedCacheKeys(link.GetCacheKey()), cancellationToken),
				Utility.Cache.RemoveAsync(Extensions.GetRelatedCacheKeys(LinkProcessor.GetLinksFilter(link.SystemID, link.RepositoryID, link.RepositoryEntityID, null), sort), cancellationToken),
				Utility.Cache.RemoveAsync(Extensions.GetRelatedCacheKeys(LinkProcessor.GetLinksFilter(null, null, link.RepositoryEntityID, null), sort), cancellationToken),
				rtuService == null ? Task.CompletedTask : rtuService.SendClearCacheRequestAsync(link.ContentType?.ID, Extensions.GetCacheKey<Link>(), cancellationToken)
			};

			new[] { link.ParentID, oldParentID }.Where(parentID => !string.IsNullOrWhiteSpace(parentID) && parentID.IsValidUUID()).ForEach(parentID =>
			{
				tasks.Add(Utility.Cache.RemoveAsync(Extensions.GetRelatedCacheKeys(LinkProcessor.GetLinksFilter(link.SystemID, link.RepositoryID, link.RepositoryEntityID, parentID), sort), cancellationToken));
				tasks.Add(Utility.Cache.RemoveAsync(Extensions.GetRelatedCacheKeys(LinkProcessor.GetLinksFilter(null, null, link.RepositoryEntityID, parentID), sort), cancellationToken));
			});

			return Task.WhenAll(tasks);
		}

		static async Task<Tuple<long, List<Link>, JToken>> SearchAsync(this RequestInfo requestInfo, string query, IFilterBy<Link> filter, SortBy<Link> sort, int pageSize, int pageNumber, string contentTypeID = null, long totalRecords = -1, string validationKey = null, CancellationToken cancellationToken = default, bool searchThumbnails = false, string cacheKeyPrefix = null)
		{
			// count
			totalRecords = totalRecords > -1
				? totalRecords
				: string.IsNullOrWhiteSpace(query)
					? await Link.CountAsync(filter, contentTypeID, string.IsNullOrWhiteSpace(cacheKeyPrefix) ? Extensions.GetCacheKeyOfTotalObjects(filter, sort) : Extensions.GetCacheKeyOfTotalObjects<Link>(cacheKeyPrefix), cancellationToken).ConfigureAwait(false)
					: await Link.CountAsync(query, filter, contentTypeID, cancellationToken).ConfigureAwait(false);

			// search objects
			var objects = totalRecords > 0
				? string.IsNullOrWhiteSpace(query)
					? await Link.FindAsync(filter, sort, pageSize, pageNumber, contentTypeID, string.IsNullOrWhiteSpace(cacheKeyPrefix) ? Extensions.GetCacheKey(filter, sort, pageSize, pageNumber) : Extensions.GetCacheKey<Link>(cacheKeyPrefix, pageSize, pageNumber), cancellationToken).ConfigureAwait(false)
					: await Link.SearchAsync(query, filter, pageSize, pageNumber, contentTypeID, cancellationToken).ConfigureAwait(false)
				: new List<Link>();

			// search thumbnails
			requestInfo.Header["x-as-attachments"] = "true";
			var thumbnails = objects.Count < 1 || !searchThumbnails
				? null
				: objects.Count == 1
					? await requestInfo.GetThumbnailsAsync(objects[0].ID, objects[0].Title.Url64Encode(), validationKey, cancellationToken).ConfigureAwait(false)
					: await requestInfo.GetThumbnailsAsync(objects.Select(@object => @object.ID).Join(","), objects.ToJObject("ID", @object => new JValue(@object.Title.Url64Encode())).ToString(Formatting.None), validationKey, cancellationToken).ConfigureAwait(false);

			// page size to clear related cached
			await Utility.SetCacheOfPageSizeAsync(filter, sort, cacheKeyPrefix, pageSize, cancellationToken).ConfigureAwait(false);

			// return the results
			return new Tuple<long, List<Link>, JToken>(totalRecords, objects, thumbnails);
		}

		internal static async Task<JObject> SearchLinksAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, string validationKey = null, CancellationToken cancellationToken = default)
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
			var organizationID = filter.GetValue("SystemID") ?? requestInfo.GetParameter("SystemID") ?? requestInfo.GetParameter("x-system-id");
			var organization = await (organizationID ?? "").GetOrganizationByIDAsync(cancellationToken).ConfigureAwait(false);
			if (organization == null)
				throw new InformationExistedException("The organization is invalid");

			var moduleID = filter.GetValue("RepositoryID") ?? requestInfo.GetParameter("RepositoryID") ?? requestInfo.GetParameter("x-module");
			var module = await (moduleID ?? "").GetModuleByIDAsync(cancellationToken).ConfigureAwait(false);
			if (module == null || !module.SystemID.IsEquals(organization.ID))
				throw new InformationInvalidException("The module is invalid");

			var contentTypeID = filter.GetValue("RepositoryEntityID") ?? requestInfo.GetParameter("RepositoryEntityID") ?? requestInfo.GetParameter("x-content-type");
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

			// process cached
			var addChildren = "true".IsEquals(requestInfo.GetHeaderParameter("x-children"));
			var json = string.IsNullOrWhiteSpace(query) && !addChildren ? await Utility.Cache.GetAsync<string>(Extensions.GetCacheKeyOfObjectsJson(filter, sort, pageSize, pageNumber), cancellationToken).ConfigureAwait(false) : null;
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

			if (addChildren)
				await objects.Where(@object => @object._childrenIDs == null).ForEachAsync(async (@object, token) => await @object.FindChildrenAsync(token, false).ConfigureAwait(false), cancellationToken, true, false).ConfigureAwait(false);

			var response = new JObject()
			{
				{ "FilterBy", filter.ToClientJson(query) },
				{ "SortBy", sort?.ToClientJson() },
				{ "Pagination", pagination.GetPagination() },
				{ "Objects", objects.Select(@object => @object.ToJson(addChildren, false, cjson => cjson["Thumbnails"] = thumbnails == null ? null : objects.Count == 1 ? thumbnails : thumbnails[@object.ID])).ToJArray() }
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

		internal static async Task<JObject> CreateLinkAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, string nodeID = null, IRTUService rtuService = null, CancellationToken cancellationToken = default)
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

			// check permission
			var gotRights = isSystemAdministrator || requestInfo.Session.User.ID.IsEquals(organization.OwnerID) || requestInfo.Session.User.IsModerator(contentType.WorkingPrivileges);
			if (!gotRights)
				throw new AccessDeniedException();

			var link = request.CreateLinkInstance("SystemID,RepositoryID,RepositoryEntityID,Privileges,OrderIndex,Created,CreatedID,LastModified,LastModifiedID", obj =>
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
			link.ClearRelatedCacheAsync(null, rtuService, cancellationToken).Run();

			var updateMessages = new List<UpdateMessage>();
			var communicateMessages = new List<CommunicateMessage>();
			var objectName = link.GetObjectName();

			// update parent
			var parentLink = link.ParentLink;
			while (parentLink != null)
			{
				parentLink.ClearRelatedCacheAsync(null, rtuService, cancellationToken).Run();
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
					ExcludedNodeID = nodeID
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

		internal static async Task<JObject> GetLinkAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, IRTUService rtuService = null, CancellationToken cancellationToken = default)
		{
			// prepare
			var link = await Link.GetAsync<Link>(requestInfo.GetObjectIdentity() ?? "", cancellationToken).ConfigureAwait(false);
			if (link == null)
				throw new InformationNotFoundException();
			else if (link.Organization == null || link.Module == null || link.ContentType == null)
				throw new InformationInvalidException("The organization/module/content-type is invalid");

			// check permission
			var gotRights = isSystemAdministrator || requestInfo.Session.User.ID.IsEquals(link.Organization.OwnerID) || requestInfo.Session.User.IsViewer(link.WorkingPrivileges);
			if (!gotRights)
				throw new AccessDeniedException();

			// prepare the response
			if (link._childrenIDs == null)
				await link.FindChildrenAsync(cancellationToken, false).ConfigureAwait(false);

			// send update message and response
			var response = link.ToJson(true, false);
			await (rtuService == null ? Task.CompletedTask : rtuService.SendUpdateMessageAsync(new UpdateMessage
			{
				Type = $"{requestInfo.ServiceName}#{link.GetObjectName()}#Update",
				Data = response,
				DeviceID = "*",
				ExcludedDeviceID = requestInfo.Session.DeviceID
			}, cancellationToken)).ConfigureAwait(false);
			return response;
		}

		internal static async Task<JObject> UpdateLinkAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, string nodeID = null, IRTUService rtuService = null, CancellationToken cancellationToken = default)
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
			link.ClearRelatedCacheAsync(oldParentID, rtuService, cancellationToken).Run();

			var updateMessages = new List<UpdateMessage>();
			var communicateMessages = new List<CommunicateMessage>();
			var objectName = link.GetObjectName();

			// update parent
			var parentLink = link.ParentLink;
			while (parentLink != null)
			{
				parentLink.ClearRelatedCacheAsync(null, rtuService, cancellationToken).Run();
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
					ExcludedNodeID = nodeID
				});
				parentLink = parentLink.ParentLink;
			}

			// update old parent
			if (!string.IsNullOrWhiteSpace(oldParentID) && !oldParentID.IsEquals(link.ParentID))
			{
				parentLink = await Link.GetAsync<Link>(oldParentID, cancellationToken).ConfigureAwait(false);
				if (parentLink != null)
				{
					parentLink.ClearRelatedCacheAsync(null, rtuService, cancellationToken).Run();
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
						ExcludedNodeID = nodeID
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

		internal static async Task<JObject> DeleteLinkAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, string nodeID = null, IRTUService rtuService = null, CancellationToken cancellationToken = default)
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

			await (await link.FindChildrenAsync(cancellationToken, false).ConfigureAwait(false)).ForEachAsync(async (child, token) =>
			{
				// update children to root
				if (updateChildren)
				{
					child.ParentID = null;
					child.LastModified = DateTime.Now;
					child.LastModifiedID = requestInfo.Session.User.ID;

					await Link.UpdateAsync(child, requestInfo.Session.User.ID, token).ConfigureAwait(false);

					var json = child.ToJson(true, false);
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
					var messages = await child.DeleteChildrenAsync(requestInfo.Session.User.ID, requestInfo.ServiceName, nodeID, token).ConfigureAwait(false);
					updateMessages = updateMessages.Concat(messages.Item1).ToList();
					communicateMessages = communicateMessages.Concat(messages.Item2).ToList();
				}
			}, cancellationToken, true, false).ConfigureAwait(false);

			await Link.DeleteAsync<Link>(link.ID, requestInfo.Session.User.ID, cancellationToken).ConfigureAwait(false);
			link.ClearRelatedCacheAsync(null, rtuService, cancellationToken).Run();

			// message to update to all other connected clients
			var response = link.ToJson();
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

		static async Task<Tuple<List<UpdateMessage>, List<CommunicateMessage>>> DeleteChildrenAsync(this Link link, string userID, string serviceName = null, string nodeID = null, CancellationToken cancellationToken = default)
		{
			var updateMessages = new List<UpdateMessage>();
			var communicateMessages = new List<CommunicateMessage>();
			var objectName = link.GetObjectName();

			var children = await link.FindChildrenAsync(cancellationToken, false).ConfigureAwait(false);
			await children.ForEachAsync(async (child, token) =>
			{
				var messages = await child.DeleteChildrenAsync(userID, serviceName, nodeID, token).ConfigureAwait(false);
				updateMessages = updateMessages.Concat(messages.Item1).ToList();
				communicateMessages = communicateMessages.Concat(messages.Item2).ToList();
			}, cancellationToken, true, false).ConfigureAwait(false);

			await Link.DeleteAsync<Link>(link.ID, userID, cancellationToken).ConfigureAwait(false);

			var json = link.ToJson();
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

		internal static async Task<JObject> GenerateAsync(RequestInfo requestInfo, bool isSystemAdministrator = false, IRTUService rtuService = null, string validationKey = null, CancellationToken cancellationToken = default)
		{
			// prepare
			var requestJson = requestInfo.GetBodyJson();
			var contentTypeID = requestJson.Get<JObject>("ContentType")?.Get<string>("ID");
			var desktop = requestJson.Get<JObject>("ContentType")?.Get<string>("Desktop") ?? requestJson.Get<JObject>("Module")?.Get<string>("Desktop") ?? requestJson.Get<JObject>("Organization")?.Get<string>("Desktop") ?? requestJson.Get<string>("Desktop");
			var expression = requestJson.Get<JObject>("Expression");
			var pageSize = requestJson.Get("PageSize", 0);
			var pageNumber = requestJson.Get("PageNumber", 1);
			var options = requestJson.Get("Options", new JObject());
			var asMenu = "Menu".IsEquals(options.Get<string>("DisplayMode")) || options.Get<bool>("AsMenu", false) || options.Get<bool>("ShowAsMenu", false) || options.Get<bool>("GenerateAsMenu", false);
			var asBanner = !asMenu && ("Banner".IsEquals(options.Get<string>("DisplayMode")) || options.Get<bool>("AsBanner", false) || options.Get<bool>("ShowAsBanner", false) || options.Get<bool>("GenerateAsBanner", false));
			var xslFilename = asMenu ? "menu.xsl" : asBanner ? "banner.xsl" : null;
			var cultureInfo = CultureInfo.GetCultureInfo(requestJson.Get("Language", "vi-VN"));

			// check permission
			var contentType = await (contentTypeID ?? "").GetContentTypeByIDAsync(cancellationToken).ConfigureAwait(false);
			var gotRights = isSystemAdministrator || requestInfo.Session.User.IsViewer(contentType?.WorkingPrivileges);
			if (!gotRights)
			{
				var organization = contentType?.Organization ?? await (requestJson.Get<JObject>("Organization")?.Get<string>("ID") ?? "").GetOrganizationByIDAsync(cancellationToken).ConfigureAwait(false);
				gotRights = requestInfo.Session.User.ID.IsEquals(organization?.OwnerID);
			}
			if (!gotRights)
				throw new AccessDeniedException();

			// prepare filtering expression
			if (!(expression?.Get<JObject>("FilterBy")?.ToFilter<Link>() is FilterBys<Link> filter) || filter.Children == null || filter.Children.Count < 1)
				filter = Filters<Link>.And(
					Filters<Link>.Equals("SystemID", "@body[Organization.ID]"),
					Filters<Link>.Equals("RepositoryID", "@body[Module.ID]"),
					Filters<Link>.Equals("RepositoryEntityID", "@body[ContentType.ID]"),
					Filters<Link>.IsNull("ParentID"),
					Filters<Link>.Equals("Status", ApprovalStatus.Published.ToString())
				);

			if (filter.GetChild("Status") == null)
				filter.Add(Filters<Link>.Equals("Status", ApprovalStatus.Published.ToString()));

			if (filter.GetChild("ParentID") == null)
				filter.Add(Filters<Link>.IsNull("ParentID"));

			var filterBy = new JObject
			{
				{ "API", filter.ToJson().ToString(Formatting.None) },
			};
			filter.Prepare(requestInfo);
			filterBy["App"] = filter.ToClientJson().ToString(Formatting.None);

			// prepare sorting expression
			var sort = expression?.Get<JObject>("SortBy")?.ToSort<Link>() ?? Sorts<Link>.Ascending("OrderIndex").ThenByDescending("Created");
			var sortBy = new JObject
			{
				{ "API", sort.ToJson().ToString(Formatting.None) },
				{ "App", sort.ToClientJson().ToString(Formatting.None) }
			};

			// get XML from cache
			XElement data;
			var cacheKeyPrefix = requestJson.GetCacheKeyPrefix();
			var cacheKey = cacheKeyPrefix != null ? Extensions.GetCacheKeyOfObjectsXml<Link>(cacheKeyPrefix, pageSize, pageNumber) : null;
			var xml = cacheKey != null ? await Utility.Cache.GetAsync<string>(cacheKey, cancellationToken).ConfigureAwait(false) : null;

			// search and build XML if has no cache
			if (string.IsNullOrWhiteSpace(xml))
			{
				var results = await requestInfo.SearchAsync(null, filter, sort, pageSize, pageNumber, contentTypeID, -1, validationKey, cancellationToken, options.Get<bool>("ShowThumbnail", false), requestJson.GetCacheKeyPrefix()).ConfigureAwait(false);
				var totalRecords = results.Item1;
				var objects = results.Item2;
				var thumbnails = results.Item3;

				// prepare pagination
				var totalPages = new Tuple<long, int>(totalRecords, pageSize).GetTotalPages();
				if (totalPages > 0 && pageNumber > totalPages)
					pageNumber = totalPages;

				// generate xml
				data = XElement.Parse("<Data/>");
				await objects.ForEachAsync(async (@object, token) =>
				{
					var thumbnailURL = thumbnails?.GetThumbnailURL(@object.ID);
					var element = asMenu ? (await requestInfo.GenerateMenuAsync(@object, thumbnailURL, 1, options.Get<int>("MaxLevel", 0), validationKey, token).ConfigureAwait(false)).ToXml("Menu") : @object.ToXml(false, cultureInfo, x => x.Add(new XElement("ThumbnailURL", thumbnailURL)));
					if (asBanner)
					{
						var attachments = await requestInfo.GetAttachmentsAsync(@object.ID, @object.Title.Url64Encode(), validationKey, cancellationToken).ConfigureAwait(false);
						var attachmentsXml = new XElement("Attachments");
						(attachments as JArray).Select(attachment => new JObject
						{
						{ "Title", attachment["Title"] },
						{ "Filename", attachment["Filename"] },
						{ "Size", attachment["Size"] },
						{ "ContentType", attachment["ContentType"] },
						{ "Downloads", attachment["Downloads"] },
						{ "URIs", attachment["URIs"] }
						}).ForEach(attachment => attachmentsXml.Add(attachment.ToXml("Attachment")));
						element.Add(attachmentsXml);
					}
					data.Add(element);
				}, cancellationToken, true, false).ConfigureAwait(false);

				// update XML into cache
				if (cacheKey != null)
					await Utility.Cache.SetAsync(cacheKey, data.ToString(SaveOptions.DisableFormatting), cancellationToken).ConfigureAwait(false);
			}
			else
				data = XElement.Parse(xml);

			// response
			return new JObject
			{
				{ "Data", data.ToString(SaveOptions.DisableFormatting) },
				{ "XslFilename", xslFilename },
				{ "FilterBy", filterBy },
				{ "SortBy", sortBy }
			};
		}

		internal static async Task<JObject> GenerateMenuAsync(this RequestInfo requestInfo, Link link, string thumbnailURL, int level, int maxLevel = 0, string validationKey = null, CancellationToken cancellationToken = default)
		{
			// generate the menu item
			var menu = new JObject
			{
				{ "ID", link.ID },
				{ "Text", link.Title },
				{ "Description", link.Summary },
				{ "Image", thumbnailURL },
				{ "URL", link.URL },
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
						requestInfo.Header["x-as-attachments"] = "true";
						var thumbnails = children.Count == 1
							? await requestInfo.GetThumbnailsAsync(children[0].ID, children[0].Title.Url64Encode(), validationKey, cancellationToken).ConfigureAwait(false)
							: await requestInfo.GetThumbnailsAsync(children.Select(child => child.ID).Join(","), children.ToJObject("ID", child => new JValue(child.Title.Url64Encode())).ToString(Formatting.None), validationKey, cancellationToken).ConfigureAwait(false);
						subMenu = new JArray();
						await children.ForEachAsync(async (child, token) => subMenu.Add(await requestInfo.GenerateMenuAsync(child, thumbnails?.GetThumbnailURL(child.ID), level + 1, maxLevel, validationKey, token).ConfigureAwait(false)), cancellationToken, true, false).ConfigureAwait(false);
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
					menu["SubMenu"] = new JObject { { "Menu", subMenu } };
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
				requestedURL = requestedURL.Replace($"{requestedURI.Scheme}://{requestedURI.Host}/", "~/").Replace($"/~{requestInfo.GetParameter("x-system")}/", "/");
				var position = requestedURL.IndexOf(".html");
				if (position > 0)
					requestedURL = requestedURL.Left(position);
				return url.IsEquals("~/") || url.IsEquals("~/index") || url.IsEquals("~/index.html") || url.IsEquals("~/default") || url.IsEquals("~/default.html")
					? requestedURL.IsEquals("~/") || requestedURL.IsEquals("~/index") || requestedURL.IsEquals("~/default")
					: requestedURL.IsStartsWith(url.Replace(".html", ""));
			}
			return false;
		}
	}
}
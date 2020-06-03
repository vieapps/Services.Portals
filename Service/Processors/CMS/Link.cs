#region Related components
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

		public static IFilterBy<Link> GetLinksFilter(this string systemID, string repositoryID = null, string repositoryEntityID = null, string parentID = null)
		{
			var filter = Filters<Link>.And(Filters<Link>.Equals("SystemID", systemID));
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
			var filter = systemID.GetLinksFilter(repositoryID, repositoryEntityID, parentID);
			var sort = Sorts<Link>.Ascending("OrderIndex").ThenByAscending("Title");
			return Link.Find(filter, sort, 0, 1, Extensions.GetCacheKey(filter, sort, 0, 1));
		}

		public static Task<List<Link>> FindLinksAsync(this string systemID, string repositoryID = null, string repositoryEntityID = null, string parentID = null, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(systemID))
				return Task.FromResult(new List<Link>());
			var filter = systemID.GetLinksFilter(repositoryID, repositoryEntityID, parentID);
			var sort = Sorts<Link>.Ascending("OrderIndex").ThenByAscending("Title");
			return Link.FindAsync(filter, sort, 0, 1, Extensions.GetCacheKey(filter, sort, 0, 1), cancellationToken);
		}

		internal static async Task<int> GetLastOrderIndexAsync(string systemID, string repositoryID = null, string repositoryEntityID = null, string parentID = null, CancellationToken cancellationToken = default)
		{
			var links = await systemID.FindLinksAsync(repositoryID, repositoryEntityID, parentID, cancellationToken).ConfigureAwait(false);
			return links != null && links.Count > 0 ? links.Last().OrderIndex : -1;
		}

		static Task ClearRelatedCache(this Link link, string oldParentID = null, CancellationToken cancellationToken = default)
		{
			var sort = Sorts<Link>.Ascending("OrderIndex").ThenByAscending("Title");
			var tasks = new List<Task>
			{
				Utility.Cache.RemoveAsync(Extensions.GetRelatedCacheKeys(link.SystemID.GetLinksFilter(link.RepositoryID, link.RepositoryEntityID, null), sort), cancellationToken)
			};
			if (!string.IsNullOrWhiteSpace(link.ParentID) && link.ParentID.IsValidUUID())
				tasks.Add(Utility.Cache.RemoveAsync(Extensions.GetRelatedCacheKeys(link.SystemID.GetLinksFilter(link.RepositoryID, link.RepositoryEntityID, link.ParentID), sort), cancellationToken));
			if (!string.IsNullOrWhiteSpace(oldParentID) && oldParentID.IsValidUUID())
				tasks.Add(Utility.Cache.RemoveAsync(Extensions.GetRelatedCacheKeys(link.SystemID.GetLinksFilter(link.RepositoryID, link.RepositoryEntityID, oldParentID), sort), cancellationToken));
			return Task.WhenAll(tasks);
		}

		static async Task<Tuple<long, List<Link>, JToken>> SearchAsync(this RequestInfo requestInfo, string query, IFilterBy<Link> filter, SortBy<Link> sort, int pageSize, int pageNumber, string contentTypeID = null, long totalRecords = -1, string validationKey = null, CancellationToken cancellationToken = default, bool searchThumbnails = false)
		{
			// count
			totalRecords = totalRecords > -1
				? totalRecords
				: string.IsNullOrWhiteSpace(query)
					? await Link.CountAsync(filter, contentTypeID, Extensions.GetCacheKeyOfTotalObjects(filter, sort), cancellationToken).ConfigureAwait(false)
					: await Link.CountAsync(query, filter, contentTypeID, cancellationToken).ConfigureAwait(false);

			// search objects
			var objects = totalRecords > 0
				? string.IsNullOrWhiteSpace(query)
					? await Link.FindAsync(filter, sort, pageSize, pageNumber, contentTypeID, Extensions.GetCacheKey(filter, sort, pageSize, pageNumber), cancellationToken).ConfigureAwait(false)
					: await Link.SearchAsync(query, filter, pageSize, pageNumber, contentTypeID, cancellationToken).ConfigureAwait(false)
				: new List<Link>();

			// search thumbnails
			requestInfo.Header["x-as-attachments"] = "true";
			var thumbnails = objects.Count < 1 || !searchThumbnails
				? null
				: objects.Count == 1
					? await requestInfo.GetThumbnailsAsync(objects[0].ID, objects[0].Title.Url64Encode(), cancellationToken, validationKey).ConfigureAwait(false)
					: await requestInfo.GetThumbnailsAsync(objects.Select(@object => @object.ID).Join(","), objects.ToJObject("ID", @object => new JValue(@object.Title.Url64Encode())).ToString(Formatting.None), cancellationToken, validationKey).ConfigureAwait(false);

			// return the results
			return new Tuple<long, List<Link>, JToken>(totalRecords, objects, thumbnails);
		}

		static Task<Tuple<long, List<Link>, JToken>> SearchAsync(this RequestInfo requestInfo, IFilterBy<Link> filter, SortBy<Link> sort, int pageSize, int pageNumber, string contentTypeID = null, long totalRecords = -1, string validationKey = null, CancellationToken cancellationToken = default, bool searchThumbnails = false)
			=> requestInfo.SearchAsync(null, filter, sort, pageSize, pageNumber, contentTypeID, totalRecords, validationKey, cancellationToken, searchThumbnails);

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
				? organization.ID.GetLinksFilter(module.ID, contentType.ID)
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
			if (link.ChildrenMode.Equals(ChildrenMode.Normal))
				link.RepositoryID = link.RepositoryEntityID = link.LookupRepositoryObjectID = null;
			else if (string.IsNullOrWhiteSpace(link.RepositoryID) || string.IsNullOrWhiteSpace(link.RepositoryEntityID) || string.IsNullOrWhiteSpace(link.LookupRepositoryObjectID))
			{
				link.ChildrenMode = ChildrenMode.Normal;
				link.RepositoryID = link.RepositoryEntityID = link.LookupRepositoryObjectID = null;
			}

			// create new
			await Task.WhenAll(
				Link.CreateAsync(link, cancellationToken),
				link.ClearRelatedCache(null, cancellationToken)
			).ConfigureAwait(false);

			var updateMessages = new List<UpdateMessage>();
			var communicateMessages = new List<CommunicateMessage>();
			var objectName = link.GetObjectName();

			// update parent
			var parentLink = link.ParentLink;
			if (parentLink != null)
			{
				await Utility.Cache.RemoveAsync(Extensions.GetRelatedCacheKeys(link.SystemID.GetLinksFilter(link.RepositoryID, link.RepositoryEntityID, link.ParentID), Sorts<Link>.Ascending("OrderIndex").ThenByAscending("Title")), cancellationToken).ConfigureAwait(false);
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

			if (link.ChildrenMode.Equals(ChildrenMode.Normal))
				link.RepositoryID = link.RepositoryEntityID = link.LookupRepositoryObjectID = null;
			else if (string.IsNullOrWhiteSpace(link.RepositoryID) || string.IsNullOrWhiteSpace(link.RepositoryEntityID) || string.IsNullOrWhiteSpace(link.LookupRepositoryObjectID))
			{
				link.ChildrenMode = ChildrenMode.Normal;
				link.RepositoryID = link.RepositoryEntityID = link.LookupRepositoryObjectID = null;
			}

			if (link.ParentLink != null && !link.ParentID.IsEquals(oldParentID))
			{
				link.OrderIndex = (await LinkProcessor.GetLastOrderIndexAsync(link.SystemID, link.RepositoryID, link.RepositoryEntityID, link.ParentID, cancellationToken).ConfigureAwait(false)) + 1;
				await link.FindChildrenAsync(cancellationToken, false).ConfigureAwait(false);
			}

			// update
			await Task.WhenAll(
				Link.UpdateAsync(link, requestInfo.Session.User.ID, cancellationToken),
				link.ClearRelatedCache(oldParentID, cancellationToken)
			).ConfigureAwait(false);

			var updateMessages = new List<UpdateMessage>();
			var communicateMessages = new List<CommunicateMessage>();
			var objectName = link.GetObjectName();

			// update parent
			var parentLink = link.ParentLink;
			if (parentLink != null && !link.ParentID.IsEquals(oldParentID))
			{
				await Utility.Cache.RemoveAsync(Extensions.GetRelatedCacheKeys(link.SystemID.GetLinksFilter(link.RepositoryID, link.RepositoryEntityID, link.ParentID), Sorts<Link>.Ascending("OrderIndex").ThenByAscending("Title")), cancellationToken).ConfigureAwait(false);
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
			}

			// update old parent
			if (!string.IsNullOrWhiteSpace(oldParentID) && !oldParentID.IsEquals(link.ParentID))
			{
				parentLink = await Link.GetAsync<Link>(oldParentID, cancellationToken).ConfigureAwait(false);
				if (parentLink != null)
				{
					await Utility.Cache.RemoveAsync(Extensions.GetRelatedCacheKeys(link.SystemID.GetLinksFilter(link.RepositoryID, link.RepositoryEntityID, parentLink.ID), Sorts<Link>.Ascending("OrderIndex").ThenByAscending("Title")), cancellationToken).ConfigureAwait(false);
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
			await link.ClearRelatedCache(null, cancellationToken).ConfigureAwait(false);

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
			var desktop = requestJson.Get<JObject>("ContentType")?.Get<string>("Desktop") ?? requestJson.Get<JObject>("Module")?.Get<string>("Desktop") ?? requestJson.Get<JObject>("Organization")?.Get<string>("DefaultDesktop") ?? requestJson.Get<string>("Desktop");
			var pageNumber = requestJson.Get("PageNumber", 1);
			var filter = requestJson["FilterBy"] == null ? null : new FilterBys<Link>(requestJson.Get<JObject>("FilterBy"));
			var sort = requestJson["SortBy"] == null ? null : new SortBy<Link>(requestJson.Get<JObject>("SortBy"));
			var alwaysUseHtmlSuffix = requestJson.Get<JObject>("Organization")?.Get<bool>("AlwaysUseHtmlSuffix") ?? true;
			var action = requestJson.Get<string>("Action");
			var isList = string.IsNullOrWhiteSpace(action) || "List".IsEquals(action);

			XDocument data;
			JObject pagination, seoInfo;
			string coverURI = null;

			// generate list
			if (isList)
			{
				// check permission
				var organization = await (requestJson.Get<JObject>("Organization")?.Get<string>("ID") ?? "").GetOrganizationByIDAsync(cancellationToken).ConfigureAwait(false);
				var contentType = await (contentTypeID ?? "").GetContentTypeByIDAsync(cancellationToken).ConfigureAwait(false);
				var gotRights = isSystemAdministrator || requestInfo.Session.User.ID.IsEquals(organization?.OwnerID) || requestInfo.Session.User.IsViewer(contentType?.WorkingPrivileges);
				if (!gotRights)
					throw new AccessDeniedException();

				// prepare filtering expression
				if (filter == null || filter.Children == null || filter.Children.Count < 1)
					filter = Filters<Link>.And(
						Filters<Link>.Equals("SystemID", "@body[Organization.ID]"),
						Filters<Link>.Equals("RepositoryID", "@body[Module.ID]"),
						Filters<Link>.Equals("RepositoryEntityID", "@body[ContentType.ID]"),
						Filters<Link>.Equals("Status", ApprovalStatus.Published.ToString())
					);
				else if (filter.GetChild("Status") == null)
					filter.Add(Filters<Link>.Equals("Status", ApprovalStatus.Published.ToString()));

				if (filter.GetChild("ParentID") == null)
				{
					var parentID = requestJson.Get<string>("ContentIdentity");
					filter.Add(string.IsNullOrWhiteSpace(parentID) ? Filters<Link>.IsNull("ParentID") : Filters<Link>.Equals("ParentID", parentID));
				}
				filter.Prepare(requestInfo);

				// prepare sorting expression
				if (sort == null)
					sort = Sorts<Link>.Descending("Created");

				// search the matched objects
				var pageSize = requestJson.Get("PageSize", 0);
				var results = await requestInfo.SearchAsync(filter, sort, pageSize, pageNumber, contentTypeID, -1, validationKey, cancellationToken, requestJson.Get<JObject>("Options")?.Get<bool>("ShowThumbnail") ?? false).ConfigureAwait(false);
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
					xml.Add(new XElement("URL", $"~/{desktop ?? "-default"}/{@object.ContentType?.Title.GetANSIUri() ?? "-"}/{@object.ID}{(alwaysUseHtmlSuffix ? ".html" : "")}"));
					if (thumbnails != null)
					{
						var thumbs = objects.Count == 1 ? thumbnails : thumbnails[@object.ID] ?? thumbnails["@" + @object.ID];
						xml.Add(new XElement("ThumbnailURL", thumbs?.First()?.Get<JObject>("URIs")?.Get<string>("Direct")));
					}
				})));

				// build others
				pagination = Utility.GeneratePagination(totalRecords, totalPages, pageSize, pageNumber, $"~/{desktop ?? "-default"}/{contentType?.Title.GetANSIUri() ?? "-"}" + "/{{pageNumber}}" + $"{(alwaysUseHtmlSuffix ? ".html" : "")}");
				seoInfo = new JObject
				{
					{ "Title", contentType?.Title },
					{ "Description", contentType?.Description }
				};
			}

			// generate details
			else
			{
				// get the requested object
				var @object = await Link.GetAsync<Link>(requestJson.Get<string>("ContentIdentity"), cancellationToken).ConfigureAwait(false);
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
					xml.Add(new XElement("URL", $"~/{desktop ?? "-default"}/{@object.ContentType?.Title.GetANSIUri() ?? "-"}/{@object.ID}{(alwaysUseHtmlSuffix ? ".html" : "")}"));

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
					}).ForEach(attachment => attachments.Add(JsonConvert.DeserializeXNode(attachment.ToString(), "Attachment")?.Root));
					xml.Add(attachments);
				}));

				// build others
				pagination = Utility.GeneratePagination(1, 1, 0, pageNumber, $"~/{desktop ?? "-default"}/{@object.ContentType?.Title.GetANSIUri() ?? "-"}/{@object.ID}" + "/{{pageNumber}}" + $"{(alwaysUseHtmlSuffix ? ".html" : "")}");
				coverURI = (thumbnailsTask.Result as JArray)?.First()?.Get<string>("URI");
				seoInfo = new JObject
				{
					{ "Title", @object.Title },
					{ "Description", @object.Summary }
				};
			}

			// response
			return new JObject
			{
				{ "Data", data.ToString(SaveOptions.DisableFormatting) },
				{ "Pagination", pagination },
				{ "FilterBy", filter?.ToClientJson() },
				{ "SortBy", sort?.ToClientJson() },
				{ "SEOInfo", seoInfo },
				{ "CoverURI", coverURI }
			};
		}
	}
}
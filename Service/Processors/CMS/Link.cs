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

		internal static async Task<JObject> SearchLinksAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, CancellationToken cancellationToken = default)
		{
			// prepare
			var request = requestInfo.GetRequestExpando();

			var query = request.Get<string>("FilterBy.Query");

			var filter = request.Get<ExpandoObject>("FilterBy", null)?.ToFilterBy<Link>() ?? Filters<Link>.And();
			if (filter is FilterBys<Link>)
			{
				if (!string.IsNullOrWhiteSpace(query))
				{
					var index = (filter as FilterBys<Link>).Children.FindIndex(exp => (exp as FilterBy<Link>).Attribute.IsEquals("ParentID"));
					if (index > -1)
						(filter as FilterBys<Link>).Children.RemoveAt(index);
				}
				else if ((filter as FilterBys<Link>).Children.FirstOrDefault(exp => (exp as FilterBy<Link>).Attribute.IsEquals("ParentID")) == null)
					(filter as FilterBys<Link>).Children.Add(Filters<Link>.IsNull("ParentID"));
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

			// process cache
			var addChildren = "true".IsEquals(requestInfo.GetHeaderParameter("x-children"));
			var json = string.IsNullOrWhiteSpace(query) && !addChildren ? await Utility.Cache.GetAsync<string>(Extensions.GetCacheKeyOfObjectsJson(filter, sort, pageSize, pageNumber), cancellationToken).ConfigureAwait(false) : null;
			if (!string.IsNullOrWhiteSpace(json))
				return JObject.Parse(json);

			// prepare pagination
			var totalRecords = pagination.Item1 > -1 ? pagination.Item1 : -1;
			if (totalRecords < 0)
				totalRecords = string.IsNullOrWhiteSpace(query)
					? await Link.CountAsync(filter, Extensions.GetCacheKeyOfTotalObjects(filter, sort), cancellationToken).ConfigureAwait(false)
					: await Link.CountAsync(query, filter, cancellationToken).ConfigureAwait(false);

			var totalPages = new Tuple<long, int>(totalRecords, pageSize).GetTotalPages();
			if (totalPages > 0 && pageNumber > totalPages)
				pageNumber = totalPages;

			// search
			var objects = totalRecords > 0
				? string.IsNullOrWhiteSpace(query)
					? await Link.FindAsync(filter, sort, pageSize, pageNumber, Extensions.GetCacheKey(filter, sort, pageSize, pageNumber), cancellationToken).ConfigureAwait(false)
					: await Link.SearchAsync(query, filter, pageSize, pageNumber, cancellationToken).ConfigureAwait(false)
				: new List<Link>();

			// build response
			pagination = new Tuple<long, int, int, int>(totalRecords, totalPages, pageSize, pageNumber);
			if (addChildren)
				await objects.Where(link => link._childrenIDs == null).ForEachAsync(async (link, token) =>
				{
					await link.FindChildrenAsync(token, false).ConfigureAwait(false);
				}, cancellationToken, true, false).ConfigureAwait(false);
			var response = new JObject()
			{
				{ "FilterBy", filter.ToClientJson(query) },
				{ "SortBy", sort?.ToClientJson() },
				{ "Pagination", pagination.GetPagination() },
				{ "Objects", objects.Select(link => addChildren ? link.ToJson(true, false) : link.ToJson(false, null)).ToJArray() }
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
	}
}
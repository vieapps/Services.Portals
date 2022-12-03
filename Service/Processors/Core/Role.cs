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
using net.vieapps.Components.Security;
using net.vieapps.Components.Repository;
using net.vieapps.Components.Utility;
#endregion

namespace net.vieapps.Services.Portals
{
	public static class RoleProcessor
	{
		internal static ConcurrentDictionary<string, Role> Roles { get; } = new ConcurrentDictionary<string, Role>(StringComparer.OrdinalIgnoreCase);

		public static Role CreateRole(this ExpandoObject data, string excluded = null, Action<Role> onCompleted = null)
			=> Role.CreateInstance(data, excluded?.ToHashSet(), onCompleted);

		public static Role Update(this Role role, ExpandoObject data, string excluded = null, Action<Role> onCompleted = null)
			=> role.Fill(data, excluded?.ToHashSet(), onCompleted);

		internal static Role Set(this Role role, bool updateCache = false)
		{
			if (role != null && !string.IsNullOrWhiteSpace(role.ID) && !string.IsNullOrWhiteSpace(role.Title))
			{
				RoleProcessor.Roles[role.ID] = role;
				if (updateCache)
					Utility.Cache.SetAsync(role).Run();
			}
			return role;
		}

		internal static async Task<Role> SetAsync(this Role role, bool updateCache = false, CancellationToken cancellationToken = default)
		{
			role?.Set();
			await (updateCache && role != null && !string.IsNullOrWhiteSpace(role.ID) && !string.IsNullOrWhiteSpace(role.Title) ? Utility.Cache.SetAsync(role, cancellationToken) : Task.CompletedTask).ConfigureAwait(false);
			return role;
		}

		internal static Role Remove(this Role role)
			=> (role?.ID ?? "").RemoveRole();

		internal static Role RemoveRole(this string id)
			=> !string.IsNullOrWhiteSpace(id) && RoleProcessor.Roles.TryRemove(id, out var role) ? role : null;

		public static Role GetRoleByID(this string id, bool force = false, bool fetchRepository = true)
			=> !force && !string.IsNullOrWhiteSpace(id) && RoleProcessor.Roles.ContainsKey(id)
				? RoleProcessor.Roles[id]
				: fetchRepository && !string.IsNullOrWhiteSpace(id)
					? Role.Get<Role>(id)?.Set()
					: null;

		public static async Task<Role> GetRoleByIDAsync(this string id, CancellationToken cancellationToken = default, bool force = false)
			=> (id ?? "").GetRoleByID(force, false) ?? (await Role.GetAsync<Role>(id, cancellationToken).ConfigureAwait(false))?.Set();

		public static IFilterBy<Role> GetRolesFilter(string systemID, string parentID = null)
			=> Filters<Role>.And
			(
				Filters<Role>.Equals("SystemID", systemID),
				string.IsNullOrWhiteSpace(parentID) ? Filters<Role>.IsNull("ParentID") : Filters<Role>.Equals("ParentID", parentID)
			);

		public static List<Role> FindRoles(this string systemID, string parentID, bool updateCache = true)
		{
			if (string.IsNullOrWhiteSpace(systemID))
				return new List<Role>();
			var filter = RoleProcessor.GetRolesFilter(systemID, parentID);
			var sort = Sorts<Role>.Ascending("Title");
			var roles = Role.Find(filter, sort, 0, 1, Extensions.GetCacheKey(filter, sort, 0, 1));
			roles.ForEach(role => role.Set(updateCache));
			return roles;
		}

		public static async Task<List<Role>> FindRolesAsync(this string systemID, string parentID, CancellationToken cancellationToken = default, bool updateCache = true)
		{
			if (string.IsNullOrWhiteSpace(systemID))
				return new List<Role>();
			var filter = RoleProcessor.GetRolesFilter(systemID, parentID);
			var sort = Sorts<Role>.Ascending("Title");
			var roles = await Role.FindAsync(filter, sort, 0, 1, Extensions.GetCacheKey(filter, sort, 0, 1), cancellationToken).ConfigureAwait(false);
			await roles.ForEachAsync(async role => await role.SetAsync(updateCache, cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
			return roles;
		}

		internal static async Task ProcessInterCommunicateMessageOfRoleAsync(this CommunicateMessage message, CancellationToken cancellationToken = default)
		{
			if (message.Type.IsEndsWith("#Create"))
			{
				var role = message.Data.ToExpandoObject().CreateRole();
				role._childrenIDs = null;
				await role.FindChildrenAsync(cancellationToken, false).ConfigureAwait(false);
				role.Set();
			}

			else if (message.Type.IsEndsWith("#Update"))
			{
				var role = message.Data.Get("ID", "").GetRoleByID(false, false);
				role = role == null
					? message.Data.ToExpandoObject().CreateRole()
					: role.Update(message.Data.ToExpandoObject());
				role._childrenIDs = null;
				await role.FindChildrenAsync(cancellationToken, false).ConfigureAwait(false);
				role.Set();
			}

			else if (message.Type.IsEndsWith("#Delete"))
				message.Data.ToExpandoObject().CreateRole().Remove();
		}

		internal static Task ClearRelatedCacheAsync(this Role role, string oldParentID, CancellationToken cancellationToken, string correlationID = null)
		{
			var sort = Sorts<Role>.Ascending("Title");
			var dataCacheKeys = Extensions.GetRelatedCacheKeys(RoleProcessor.GetRolesFilter(role.SystemID), sort);
			if (!string.IsNullOrWhiteSpace(role.ParentID) && role.ParentID.IsValidUUID())
				dataCacheKeys = Extensions.GetRelatedCacheKeys(RoleProcessor.GetRolesFilter(role.SystemID, role.ParentID), sort).Concat(dataCacheKeys).ToList();
			if (!string.IsNullOrWhiteSpace(oldParentID) && oldParentID.IsValidUUID())
				dataCacheKeys = Extensions.GetRelatedCacheKeys(RoleProcessor.GetRolesFilter(role.SystemID, oldParentID), sort).Concat(dataCacheKeys).ToList();
			dataCacheKeys = dataCacheKeys.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
			return Task.WhenAll
			(
				Utility.IsCacheLogEnabled ? Utility.WriteLogAsync(correlationID, $"Clear related cache of role [{role.ID} => {role.Title}]\r\n{dataCacheKeys.Count} keys => {dataCacheKeys.Join(", ")}", "Caches") : Task.CompletedTask,
				Utility.Cache.RemoveAsync(dataCacheKeys, cancellationToken)
			);
		}

		internal static Task ClearCacheAsync(this Role role, CancellationToken cancellationToken, string correlationID = null, bool clearRelatedDataCache = true)
			=> Task.WhenAll(new[]
			{
				clearRelatedDataCache ? role.ClearRelatedCacheAsync(null, cancellationToken, correlationID) : Task.CompletedTask,
				Utility.Cache.RemoveAsync(role.Remove(), cancellationToken),
				new CommunicateMessage(Utility.ServiceName)
				{
					Type = $"{role.GetObjectName()}#Delete",
					Data = role.ToJson(),
					ExcludedNodeID = Utility.NodeID
				}.SendAsync(),
				Utility.IsCacheLogEnabled ? Utility.WriteLogAsync(correlationID, $"Clear cache of a role [{role.Title} - ID: {role.ID}]", "Caches") : Task.CompletedTask
			});

		internal static async Task<JObject> SearchRolesAsync(this RequestInfo requestInfo, bool isSystemAdministrator, CancellationToken cancellationToken)
		{
			// prepare
			var request = requestInfo.GetRequestExpando();

			var query = request.Get<string>("FilterBy.Query");
			var filter = request.Get<ExpandoObject>("FilterBy")?.ToFilterBy<Role>() ?? Filters<Role>.And();
			if (filter is FilterBys<Role>)
			{
				if (!string.IsNullOrWhiteSpace(query))
				{
					var index = (filter as FilterBys<Role>).Children.FindIndex(exp => (exp as FilterBy<Role>).Attribute.IsEquals("ParentID"));
					if (index > -1)
						(filter as FilterBys<Role>).Children.RemoveAt(index);
				}
				else if ((filter as FilterBys<Role>).Children.FirstOrDefault(exp => (exp as FilterBy<Role>).Attribute.IsEquals("ParentID")) == null)
					(filter as FilterBys<Role>).Children.Add(Filters<Role>.IsNull("ParentID"));
			}
			var sort = string.IsNullOrWhiteSpace(query) ? request.Get<ExpandoObject>("SortBy")?.ToSortBy<Role>() ?? Sorts<Role>.Ascending("Title") : null;

			var pagination = request.Get<ExpandoObject>("Pagination")?.GetPagination() ?? new Tuple<long, int, int, int>(-1, 0, 20, 1);
			var pageSize = pagination.Item3;
			var pageNumber = pagination.Item4;

			// get organization
			var organizationID = filter.GetValue("SystemID") ?? requestInfo.GetParameter("SystemID") ?? requestInfo.GetParameter("x-system-id") ?? requestInfo.GetParameter("OrganizationID");
			var organization = await (organizationID ?? "").GetOrganizationByIDAsync(cancellationToken).ConfigureAwait(false);
			if (organization == null)
				throw new InformationExistedException("The organization is invalid");

			// check permission
			var gotRights = isSystemAdministrator || requestInfo.Session.User.IsViewer(null, null, organization);
			if (!gotRights)
				throw new AccessDeniedException();

			// process cache
			var addChildren = "true".IsEquals(requestInfo.GetHeaderParameter("x-children"));
			var cachedJson = string.IsNullOrWhiteSpace(query) && !addChildren ? await Utility.Cache.GetAsync<string>(Extensions.GetCacheKeyOfObjectsJson(filter, sort, pageSize, pageNumber), cancellationToken).ConfigureAwait(false) : null;
			if (!string.IsNullOrWhiteSpace(cachedJson))
				return JObject.Parse(cachedJson);

			// prepare pagination
			var totalRecords = pagination.Item1 > -1 ? pagination.Item1 : -1;
			if (totalRecords < 0)
				totalRecords = string.IsNullOrWhiteSpace(query)
					? await Role.CountAsync(filter, Extensions.GetCacheKeyOfTotalObjects(filter, sort), cancellationToken).ConfigureAwait(false)
					: await Role.CountAsync(query, filter, cancellationToken).ConfigureAwait(false);

			var totalPages = new Tuple<long, int>(totalRecords, pageSize).GetTotalPages();
			if (totalPages > 0 && pageNumber > totalPages)
				pageNumber = totalPages;

			// search
			var objects = totalRecords > 0
				? string.IsNullOrWhiteSpace(query)
					? await Role.FindAsync(filter, sort, pageSize, pageNumber, Extensions.GetCacheKey(filter, sort, pageSize, pageNumber), cancellationToken).ConfigureAwait(false)
					: await Role.SearchAsync(query, filter, null, pageSize, pageNumber, cancellationToken).ConfigureAwait(false)
				: new List<Role>();

			// build result
			pagination = new Tuple<long, int, int, int>(totalRecords, totalPages, pageSize, pageNumber);

			if (addChildren)
				await objects.Where(role => role._childrenIDs == null).ForEachAsync(async role => await role.FindChildrenAsync(cancellationToken).ConfigureAwait(false), true, false).ConfigureAwait(false);

			var response = new JObject
			{
				{ "FilterBy", filter.ToClientJson(query) },
				{ "SortBy", sort?.ToClientJson() },
				{ "Pagination", pagination.GetPagination() },
				{ "Objects", objects.Select(role => role.ToJson(addChildren, false)).ToJArray() }
			};

			// update cache
			if (string.IsNullOrWhiteSpace(query) && !addChildren)
				await Utility.Cache.SetAsync(Extensions.GetCacheKeyOfObjectsJson(filter, sort, pageSize, pageNumber), response.ToString(Formatting.None), cancellationToken).ConfigureAwait(false);

			// response
			return response;
		}

		internal static async Task<JObject> CreateRoleAsync(this RequestInfo requestInfo, bool isSystemAdministrator, Func<RequestInfo, CancellationToken, Task> serviceCaller, Action<RequestInfo, string, Exception> onServiceCallerGotError, CancellationToken cancellationToken)
		{
			// prepare
			var request = requestInfo.GetBodyExpando();
			var organizationID = request.Get<string>("SystemID") ?? requestInfo.GetParameter("x-system-id") ?? requestInfo.GetParameter("SystemID");
			var organization = await (organizationID ?? "").GetOrganizationByIDAsync(cancellationToken).ConfigureAwait(false);
			if (organization == null)
				throw new InformationInvalidException("The organization is invalid");

			// check permission
			var gotRights = isSystemAdministrator || requestInfo.Session.User.IsModerator(null, null, organization);
			if (!gotRights)
				throw new AccessDeniedException();

			// create new
			var role = request.CreateRole("SystemID,Privileges,OriginalPrivileges,Created,CreatedID,LastModified,LastModifiedID", obj =>
			{
				obj.ID = string.IsNullOrWhiteSpace(obj.ID) || !obj.ID.IsValidUUID() ? UtilityService.NewUUID : obj.ID;
				obj.SystemID = organization.ID;
				obj.ParentID = obj.ParentRole != null ? obj.ParentID : null;
				obj.Created = obj.LastModified = DateTime.Now;
				obj.CreatedID = obj.LastModifiedID = requestInfo.Session.User.ID;
				obj._childrenIDs = new List<string>();
			});
			await Role.CreateAsync(role, cancellationToken).ConfigureAwait(false);
			await role.Set().ClearRelatedCacheAsync(null, cancellationToken, requestInfo.CorrelationID).ConfigureAwait(false);

			// update users
			var requestUser = new RequestInfo(requestInfo)
			{
				ServiceName = "Users",
				ObjectName = "Privileges",
				Verb = "POST",
				Query = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
				{
					{ "related-service", requestInfo.ServiceName },
					{ "related-object", "Role" },
					{ "related-system", role.SystemID },
					{ "related-entity", typeof(Role).GetTypeName() },
					{ "related-object-identity", role.ID }
				},
				Extra = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
				{
					{ "AddedRoles", new[] { role.ID }.ToJArray().ToString(Formatting.None).Encrypt(Utility.EncryptionKey) }
				}
			};

			var userIDs = role.UserIDs ?? new List<string>();
			var parentRole = role.ParentRole;
			while (parentRole != null)
			{
				userIDs = userIDs.Concat(parentRole.UserIDs ?? new List<string>()).ToList();
				parentRole = parentRole.ParentRole;
			}

			await userIDs.Distinct(StringComparer.OrdinalIgnoreCase).ForEachAsync(async userID =>
			{
				try
				{
					requestUser.Query["object-identity"] = userID;
					await (serviceCaller == null ? Task.CompletedTask : serviceCaller(requestUser, cancellationToken)).ConfigureAwait(false);
				}
				catch (Exception ex)
				{
					onServiceCallerGotError?.Invoke(requestUser, $"Error occurred while updating roles of an user account [{userID}] => {ex.Message}", ex);
				}
			}, true, false).ConfigureAwait(false);

			// update parent
			var updateMessages = new List<UpdateMessage>();
			var communicateMessages = new List<CommunicateMessage>();
			var objectName = role.GetTypeName(true);

			if (role.ParentRole != null)
			{
				await role.ParentRole.FindChildrenAsync(cancellationToken).ConfigureAwait(false);
				role.ParentRole._childrenIDs.Add(role.ID);
				await role.ParentRole.SetAsync(true, cancellationToken).ConfigureAwait(false);

				// message to update to all connected clients
				var json = role.ParentRole.ToJson(true, false);
				updateMessages.Add(new UpdateMessage
				{
					Type = $"{requestInfo.ServiceName}#{objectName}#Update",
					Data = json,
					DeviceID = "*"
				});

				// message to update to all service instances (on all other nodes)
				communicateMessages.Add(new CommunicateMessage(requestInfo.ServiceName)
				{
					Type = $"{objectName}#Update",
					Data = json,
					ExcludedNodeID = Utility.NodeID
				});
			}

			// message to update to all other connected clients
			var response = role.ToJson();
			if (role.ParentRole == null)
				updateMessages.Add(new UpdateMessage
				{
					Type = $"{requestInfo.ServiceName}#{objectName}#Create",
					Data = response,
					DeviceID = "*"
				});

			// message to update to all service instances (on all other nodes)
			communicateMessages.Add(new CommunicateMessage(requestInfo.ServiceName)
			{
				Type = $"{objectName}#Create",
				Data = response,
				ExcludedNodeID = Utility.NodeID
			});

			// send the messages
			updateMessages.Send();
			communicateMessages.Send();

			// send notification
			await role.SendNotificationAsync("Create", organization.Notifications, ApprovalStatus.Draft, ApprovalStatus.Published, requestInfo, cancellationToken).ConfigureAwait(false);

			// response
			return response;
		}

		internal static async Task<JObject> GetRoleAsync(this RequestInfo requestInfo, bool isSystemAdministrator, CancellationToken cancellationToken)
		{
			// prepare
			var role = await (requestInfo.GetObjectIdentity(true, true) ?? "").GetRoleByIDAsync(cancellationToken).ConfigureAwait(false);
			if (role == null)
				throw new InformationNotFoundException();
			else if (role.Organization == null)
				throw new InformationInvalidException("The organization is invalid");

			// check permission
			var gotRights = isSystemAdministrator || requestInfo.Session.User.IsViewer(null, null, role.Organization);
			if (!gotRights)
				throw new AccessDeniedException();

			// refresh (clear cached and reload)
			var isRefresh = "refresh".IsEquals(requestInfo.GetObjectIdentity());
			if (isRefresh)
			{
				await role.ClearRelatedCacheAsync("", cancellationToken, requestInfo.CorrelationID).ConfigureAwait(false);
				await Utility.Cache.RemoveAsync(role, cancellationToken).ConfigureAwait(false);
				role = await role.ID.GetRoleByIDAsync(cancellationToken, true).ConfigureAwait(false);
			}

			// prepare the response
			if (role._childrenIDs == null)
			{
				await role.FindChildrenAsync(cancellationToken).ConfigureAwait(false);
				role.Set();
			}

			// send the update message to update to all other connected clients
			var objectName = role.GetTypeName(true);
			var versions = await role.FindVersionsAsync(cancellationToken, false).ConfigureAwait(false);
			var response = role.ToJson(true, false);
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

			// response
			return response;
		}

		internal static async Task<JObject> UpdateRoleAsync(this RequestInfo requestInfo, bool isSystemAdministrator, Func<RequestInfo, CancellationToken, Task> serviceCaller, Action<RequestInfo, string, Exception> onServiceCallerGotError, CancellationToken cancellationToken)
		{
			// prepare
			var role = await (requestInfo.GetObjectIdentity() ?? "").GetRoleByIDAsync(cancellationToken).ConfigureAwait(false);
			if (role == null)
				throw new InformationNotFoundException();
			else if (role.Organization == null)
				throw new InformationInvalidException("The organization is invalid");

			// check permission
			var gotRights = isSystemAdministrator || requestInfo.Session.User.IsModerator(null, null, role.Organization);
			if (!gotRights)
				throw new AccessDeniedException();

			// update
			var oldParentID = role.ParentID;
			var oldUserIDs = role.UserIDs ?? new List<string>();

			role.Update(requestInfo.GetBodyExpando(), "ID,SystemID,Privileges,OriginalPrivileges,Created,CreatedID,LastModified,LastModifiedID", async obj =>
			{
				obj.LastModified = DateTime.Now;
				obj.LastModifiedID = requestInfo.Session.User.ID;
				await obj.FindChildrenAsync(cancellationToken).ConfigureAwait(false);
			});
			await Role.UpdateAsync(role, requestInfo.Session.User.ID, cancellationToken).ConfigureAwait(false);
			await role.Set().ClearRelatedCacheAsync(oldParentID, cancellationToken, requestInfo.CorrelationID).ConfigureAwait(false);

			// update users
			var beAddedUserIDs = (role.UserIDs ?? new List<string>()).Except(oldUserIDs).ToList();
			var parentRole = role.ParentRole;
			while (parentRole != null)
			{
				beAddedUserIDs = beAddedUserIDs.Concat(parentRole.UserIDs ?? new List<string>()).ToList();
				parentRole = parentRole.ParentRole;
			}
			beAddedUserIDs = beAddedUserIDs.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

			var beRemovedUserIDs = oldUserIDs.Except(role.UserIDs ?? new List<string>()).ToList();
			if (!string.IsNullOrWhiteSpace(oldParentID) && !oldParentID.IsEquals(role.ParentID))
			{
				parentRole = await oldParentID.GetRoleByIDAsync(cancellationToken).ConfigureAwait(false);
				while (parentRole != null)
				{
					beRemovedUserIDs = beRemovedUserIDs.Concat(parentRole.UserIDs ?? new List<string>()).ToList();
					parentRole = parentRole.ParentRole;
				}
			}
			beRemovedUserIDs = beRemovedUserIDs.Distinct(StringComparer.OrdinalIgnoreCase).Except(beAddedUserIDs).ToList();

			var requestUser = new RequestInfo(requestInfo)
			{
				ServiceName = "Users",
				ObjectName = "Privileges",
				Verb = "POST",
				Query = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
				{
					{ "related-service", requestInfo.ServiceName },
					{ "related-object", "Role" },
					{ "related-system", role.SystemID },
					{ "related-entity", typeof(Role).GetTypeName() },
					{ "related-object-identity", role.ID }
				},
				Extra = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
			};

			requestUser.Extra.Clear();
			requestUser.Extra["RemovedRoles"] = new[] { role.ID }.ToJArray().ToString(Formatting.None).Encrypt(Utility.EncryptionKey);
			await beRemovedUserIDs.ForEachAsync(async userID =>
			{
				try
				{
					requestUser.Query["object-identity"] = userID;
					await (serviceCaller == null ? Task.CompletedTask : serviceCaller(requestUser, cancellationToken)).ConfigureAwait(false);
				}
				catch (Exception ex)
				{
					onServiceCallerGotError?.Invoke(requestUser, $"Error occurred while updating roles of an user account [{userID}] => {ex.Message}", ex);
				}
			}, true, false).ConfigureAwait(false);

			requestUser.Extra.Clear();
			requestUser.Extra["AddedRoles"] = new[] { role.ID }.ToJArray().ToString(Formatting.None).Encrypt(Utility.EncryptionKey);
			await beAddedUserIDs.ForEachAsync(async userID =>
			{
				try
				{
					requestUser.Query["object-identity"] = userID;
					await (serviceCaller == null ? Task.CompletedTask : serviceCaller(requestUser, cancellationToken)).ConfigureAwait(false);
				}
				catch (Exception ex)
				{
					onServiceCallerGotError?.Invoke(requestUser, $"Error occurred while updating roles of an user account [{userID}] => {ex.Message}", ex);
				}
			}, true, false).ConfigureAwait(false);

			// update parent
			var updateMessages = new List<UpdateMessage>();
			var communicateMessages = new List<CommunicateMessage>();
			var objectName = role.GetTypeName(true);

			if (role.ParentRole != null && !role.ParentID.IsEquals(oldParentID))
			{
				await role.ParentRole.FindChildrenAsync(cancellationToken).ConfigureAwait(false);
				role.ParentRole._childrenIDs.Add(role.ID);
				await role.ParentRole.SetAsync(true, cancellationToken).ConfigureAwait(false);

				var json = role.ParentRole.ToJson(true, false);

				// message to update to all connected clients
				updateMessages.Add(new UpdateMessage
				{
					Type = $"{requestInfo.ServiceName}#{objectName}#Update",
					Data = json,
					DeviceID = "*"
				});

				// message to update to all service instances (on all other nodes)
				communicateMessages.Add(new CommunicateMessage(requestInfo.ServiceName)
				{
					Type = $"{objectName}#Update",
					Data = json,
					ExcludedNodeID = Utility.NodeID
				});
			}

			// update old parent
			if (!string.IsNullOrWhiteSpace(oldParentID) && !oldParentID.IsEquals(role.ParentID))
			{
				parentRole = await oldParentID.GetRoleByIDAsync(cancellationToken).ConfigureAwait(false);
				if (parentRole != null)
				{
					await parentRole.FindChildrenAsync(cancellationToken).ConfigureAwait(false);
					parentRole._childrenIDs.Remove(role.ID);
					await parentRole.SetAsync(true, cancellationToken).ConfigureAwait(false);

					var json = parentRole.ToJson(true, false);

					// message to update to all connected clients
					updateMessages.Add(new UpdateMessage
					{
						Type = $"{requestInfo.ServiceName}#{objectName}#Update",
						Data = json,
						DeviceID = "*"
					});

					// message to update to all service instances (on all other nodes)
					communicateMessages.Add(new CommunicateMessage(requestInfo.ServiceName)
					{
						Type = $"{objectName}#Update",
						Data = json,
						ExcludedNodeID = Utility.NodeID
					});
				}
			}

			// message to update to all other connected clients
			var response = role.ToJson(true, false);
			var versions = await role.FindVersionsAsync(cancellationToken, false).ConfigureAwait(false);
			updateMessages.Add(new UpdateMessage
			{
				Type = $"{requestInfo.ServiceName}#{objectName}#Update",
				Data = response.UpdateVersions(versions),
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
			updateMessages.Send();
			communicateMessages.Send();

			// send notification
			await role.SendNotificationAsync("Update", role.Organization.Notifications, ApprovalStatus.Published, ApprovalStatus.Published, requestInfo, cancellationToken).ConfigureAwait(false);

			// response
			return response;
		}

		internal static async Task<JObject> DeleteRoleAsync(this RequestInfo requestInfo, bool isSystemAdministrator, Func<RequestInfo, CancellationToken, Task> serviceCaller, Action<RequestInfo, string, Exception> onServiceCallerGotError, CancellationToken cancellationToken)
		{
			// prepare
			var role = await (requestInfo.GetObjectIdentity() ?? "").GetRoleByIDAsync(cancellationToken).ConfigureAwait(false);
			if (role == null)
				throw new InformationNotFoundException();
			else if (role.Organization == null)
				throw new InformationInvalidException("The organization is invalid");

			// check permission
			var gotRights = isSystemAdministrator || requestInfo.Session.User.IsAdministrator(null, null, role.Organization);
			if (!gotRights)
				throw new AccessDeniedException();

			var objectName = role.GetObjectName();
			var updateChildren = requestInfo.Header.TryGetValue("x-children", out var childrenMode) && "set-null".IsEquals(childrenMode);

			// delete
			var children = await role.FindChildrenAsync(cancellationToken).ConfigureAwait(false);
			await children.ForEachAsync(async child =>
			{
				if (updateChildren)
				{
					child.ParentID = null;
					child.LastModified = DateTime.Now;
					child.LastModifiedID = requestInfo.Session.User.ID;
					await Role.UpdateAsync(child, requestInfo.Session.User.ID, cancellationToken).ConfigureAwait(false);

					await Task.WhenAll
					(
						child.Set().ClearRelatedCacheAsync(null, cancellationToken, requestInfo.CorrelationID),
						child.SendNotificationAsync("Update", child.Organization.Notifications, ApprovalStatus.Published, ApprovalStatus.Published, requestInfo, cancellationToken)
					).ConfigureAwait(false);

					var json = child.ToJson(true, false);
					new UpdateMessage
					{
						Type = $"{requestInfo.ServiceName}#{objectName}#Update",
						Data = json,
						DeviceID = "*"
					}.Send();
					new CommunicateMessage(requestInfo.ServiceName)
					{
						ServiceName = requestInfo.ServiceName,
						Type = $"{objectName}#Update",
						Data = json,
						ExcludedNodeID = Utility.NodeID
					}.Send();
				}
				else
					await child.DeleteChildrenAsync(requestInfo, serviceCaller, onServiceCallerGotError, cancellationToken).ConfigureAwait(false);
			}, true, false).ConfigureAwait(false);

			await Role.DeleteAsync<Role>(role.ID, requestInfo.Session.User.ID, cancellationToken).ConfigureAwait(false);
			await role.Remove().ClearCacheAsync(cancellationToken, requestInfo.CorrelationID, true).ConfigureAwait(false);
			await role.SendNotificationAsync("Delete", role.Organization.Notifications, ApprovalStatus.Published, ApprovalStatus.Published, requestInfo, cancellationToken).ConfigureAwait(false);

			// update users
			var beRemovedUserIDs = role.UserIDs ?? new List<string>();
			var parentRole = role.ParentRole;
			while (parentRole != null)
			{
				beRemovedUserIDs = beRemovedUserIDs.Concat(parentRole.UserIDs ?? new List<string>()).ToList();
				parentRole = parentRole.ParentRole;
			}
			beRemovedUserIDs = beRemovedUserIDs.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
			var requestUser = new RequestInfo(requestInfo)
			{
				ServiceName = "Users",
				ObjectName = "Privileges",
				Verb = "POST",
				Query = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
				{
					{ "related-service", requestInfo.ServiceName },
					{ "related-object", "Role" },
					{ "related-system", role.SystemID },
					{ "related-entity", typeof(Role).GetTypeName() },
					{ "related-object-identity", role.ID }
				},
				Extra = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
				{
					{ "RemovedRoles", new[] { role.ID }.ToJArray().ToString(Formatting.None).Encrypt(Utility.EncryptionKey) }
				}
			};
			await beRemovedUserIDs.ForEachAsync(async userID =>
			{
				try
				{
					requestUser.Query["object-identity"] = userID;
					await (serviceCaller == null ? Task.CompletedTask : serviceCaller(requestUser, cancellationToken)).ConfigureAwait(false);
				}
				catch (Exception ex)
				{
					onServiceCallerGotError?.Invoke(requestUser, $"Error occurred while updating roles of an user account [{userID}] => {ex.Message}", ex);
				}
			}, true, false).ConfigureAwait(false);

			// send update  messages
			var response = role.ToJson();
			new UpdateMessage
			{
				Type = $"{requestInfo.ServiceName}#{objectName}#Delete",
				DeviceID = "*",
				Data = response
			}.Send();
			new CommunicateMessage(requestInfo.ServiceName)
			{
				Type = $"{objectName}#Delete",
				Data = response,
				ExcludedNodeID = Utility.NodeID
			}.Send();
			return response;
		}

		static async Task DeleteChildrenAsync(this Role role, RequestInfo requestInfo, Func<RequestInfo, CancellationToken, Task> serviceCaller, Action<RequestInfo, string, Exception> onServiceCallerGotError, CancellationToken cancellationToken)
		{
			var children = await role.FindChildrenAsync(cancellationToken).ConfigureAwait(false);
			await children.ForEachAsync(async child => await child.DeleteChildrenAsync(requestInfo, serviceCaller, onServiceCallerGotError, cancellationToken).ConfigureAwait(false), true, false).ConfigureAwait(false);

			await Role.DeleteAsync<Role>(role.ID, requestInfo.Session.User.ID, cancellationToken).ConfigureAwait(false);
			await Task.WhenAll
			(
				role.ClearCacheAsync(cancellationToken, requestInfo.CorrelationID, true),
				role.SendNotificationAsync("Delete", role.Organization.Notifications, ApprovalStatus.Published, ApprovalStatus.Published, requestInfo, cancellationToken)
			).ConfigureAwait(false);

			// update users
			var beRemovedUserIDs = role.UserIDs ?? new List<string>();
			var parentRole = role.ParentRole;
			while (parentRole != null)
			{
				beRemovedUserIDs = beRemovedUserIDs.Concat(parentRole.UserIDs ?? new List<string>()).ToList();
				parentRole = parentRole.ParentRole;
			}
			beRemovedUserIDs = beRemovedUserIDs.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
			var requestUser = new RequestInfo(requestInfo)
			{
				ServiceName = "Users",
				ObjectName = "Privileges",
				Verb = "POST",
				Query = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
				{
					{ "related-service", requestInfo.ServiceName },
					{ "related-object", "Role" },
					{ "related-system", role.SystemID },
					{ "related-entity", typeof(Role).GetTypeName() },
					{ "related-object-identity", role.ID }
				},
				Extra = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
				{
					{ "RemovedRoles", new[] { role.ID }.ToJArray().ToString(Formatting.None).Encrypt(Utility.EncryptionKey) }
				}
			};
			await beRemovedUserIDs.ForEachAsync(async userID =>
			{
				try
				{
					requestUser.Query["object-identity"] = userID;
					await (serviceCaller == null ? Task.CompletedTask : serviceCaller(requestUser, cancellationToken)).ConfigureAwait(false);
				}
				catch (Exception ex)
				{
					onServiceCallerGotError?.Invoke(requestUser, $"Error occurred while updating roles of an user account [{userID}] => {ex.Message}", ex);
				}
			}, true, false).ConfigureAwait(false);

			var json = role.ToJson();
			var objectName = role.GetObjectName();
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

		internal static async Task<JObject> SyncRoleAsync(this RequestInfo requestInfo, CancellationToken cancellationToken, bool sendNotifications = false, bool dontCreateNewVersion = false)
		{
			var @event = requestInfo.GetParameter("event") ?? requestInfo.GetParameter("x-original-event");
			if (string.IsNullOrWhiteSpace(@event) || !@event.IsEquals("Delete"))
				@event = "Update";

			var data = requestInfo.GetBodyExpando();
			var role = await data.Get<string>("ID").GetRoleByIDAsync(cancellationToken).ConfigureAwait(false);

			if (!@event.IsEquals("Delete"))
			{
				if (role == null)
				{
					role = Role.CreateInstance(data);
					await Role.CreateAsync(role, cancellationToken).ConfigureAwait(false);
				}
				else
					await Role.UpdateAsync(role.Update(data), dontCreateNewVersion, cancellationToken).ConfigureAwait(false);
			}
			else if (role != null)
				await Role.DeleteAsync<Role>(role.ID, role.LastModifiedID, cancellationToken).ConfigureAwait(false);

			// clear related cache
			if (requestInfo.GetHeaderParameter("x-converter") == null || @event.IsEquals("Delete"))
				await role.ClearCacheAsync(cancellationToken, requestInfo.CorrelationID).ConfigureAwait(false);
			else
				await role.ClearRelatedCacheAsync(null, cancellationToken, requestInfo.CorrelationID).ConfigureAwait(false);

			// send notifications
			if (sendNotifications)
				await role.SendNotificationAsync(@event, role.Organization.Notifications, ApprovalStatus.Published, ApprovalStatus.Published, requestInfo, cancellationToken).ConfigureAwait(false);

			// send update messages
			var json = role.Set().ToJson();
			var objectName = role.GetTypeName(true);
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

		internal static async Task<JObject> RollbackRoleAsync(this RequestInfo requestInfo, bool isSystemAdministrator, CancellationToken cancellationToken)
		{
			var role = await (requestInfo.GetObjectIdentity() ?? "").GetRoleByIDAsync(cancellationToken).ConfigureAwait(false);
			if (role == null)
				throw new InformationNotFoundException();
			else if (role.Organization == null)
				throw new InformationInvalidException("The organization is invalid");

			// check permission
			var gotRights = isSystemAdministrator || requestInfo.Session.User.IsModerator(null, null, role.Organization);
			if (!gotRights)
				throw new AccessDeniedException();

			// rollback
			var oldParentID = role.ParentID;
			role = await RepositoryMediator.RollbackAsync<Role>(requestInfo.GetParameter("x-version-id") ?? "", requestInfo.Session.User.ID, cancellationToken).ConfigureAwait(false);
			await Task.WhenAll
			(
				role.ClearRelatedCacheAsync(oldParentID, cancellationToken, requestInfo.CorrelationID),
				role.SendNotificationAsync("Rollback", role.Organization.Notifications, ApprovalStatus.Published, ApprovalStatus.Published, requestInfo, cancellationToken)
			).ConfigureAwait(false);

			// send update messages
			var versions = await role.FindVersionsAsync(cancellationToken, false).ConfigureAwait(false);
			var response = role.Set(true).ToJson(true, false);
			var objectName = role.GetTypeName(true);
			new UpdateMessage
			{
				Type = $"{requestInfo.ServiceName}#{objectName}#Update",
				Data = response.UpdateVersions(versions),
				DeviceID = "*"
			}.Send();
			new CommunicateMessage(requestInfo.ServiceName)
			{
				Type = $"{objectName}#Update",
				Data = response,
				ExcludedNodeID = Utility.NodeID
			}.Send();
			return response;
		}
	}
}
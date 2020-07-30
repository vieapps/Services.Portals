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

		public static Role CreateRoleInstance(this ExpandoObject data, string excluded = null, Action<Role> onCompleted = null)
			=> Role.CreateInstance(data, excluded?.ToHashSet(), onCompleted);

		public static Role UpdateRoleInstance(this Role role, ExpandoObject data, string excluded = null, Action<Role> onCompleted = null)
			=> role.Fill(data, excluded?.ToHashSet(), onCompleted);

		internal static Role Set(this Role role, bool updateCache = false)
		{
			if (role != null)
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
			await (updateCache && role != null ? Utility.Cache.SetAsync(role, cancellationToken) : Task.CompletedTask).ConfigureAwait(false);
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

		public static IFilterBy<Role> GetRolesFilter(this string systemID, string parentID)
			=> Filters<Role>.And(Filters<Role>.Equals("SystemID", systemID), string.IsNullOrWhiteSpace(parentID) ? Filters<Role>.IsNull("ParentID") : Filters<Role>.Equals("ParentID", parentID));

		public static List<Role> FindRoles(this string systemID, string parentID, bool updateCache = true)
		{
			if (string.IsNullOrWhiteSpace(systemID))
				return new List<Role>();
			var filter = systemID.GetRolesFilter(parentID);
			var sort = Sorts<Role>.Ascending("Title");
			var roles = Role.Find(filter, sort, 0, 1, Extensions.GetCacheKey(filter, sort, 0, 1));
			roles.ForEach(role => role.Set(updateCache));
			return roles;
		}

		public static async Task<List<Role>> FindRolesAsync(this string systemID, string parentID, CancellationToken cancellationToken = default, bool updateCache = true)
		{
			if (string.IsNullOrWhiteSpace(systemID))
				return new List<Role>();
			var filter = systemID.GetRolesFilter(parentID);
			var sort = Sorts<Role>.Ascending("Title");
			var roles = await Role.FindAsync(filter, sort, 0, 1, Extensions.GetCacheKey(filter, sort, 0, 1), cancellationToken).ConfigureAwait(false);
			await roles.ForEachAsync((role, token) => role.SetAsync(updateCache, token), cancellationToken).ConfigureAwait(false);
			return roles;
		}

		internal static async Task ProcessInterCommunicateMessageOfRoleAsync(this CommunicateMessage message, CancellationToken cancellationToken = default)
		{
			if (message.Type.IsEndsWith("#Create"))
				await message.Data.ToExpandoObject().CreateRoleInstance().SetAsync(false, cancellationToken).ConfigureAwait(false);

			else if (message.Type.IsEndsWith("#Update"))
			{
				var role = message.Data.Get("ID", "").GetRoleByID(false, false);
				await (role == null ? message.Data.ToExpandoObject().CreateRoleInstance() : role.UpdateRoleInstance(message.Data.ToExpandoObject())).SetAsync(false, cancellationToken).ConfigureAwait(false);
			}

			else if (message.Type.IsEndsWith("#Delete"))
				message.Data.ToExpandoObject().CreateRoleInstance().Remove();
		}

		static Task ClearRelatedCacheAsync(this Role role, string oldParentID = null, CancellationToken cancellationToken = default)
		{
			var tasks = new List<Task> { Utility.Cache.RemoveAsync(Extensions.GetRelatedCacheKeys(role.SystemID.GetRolesFilter(null), Sorts<Role>.Ascending("Title")), cancellationToken) };
			if (!string.IsNullOrWhiteSpace(role.ParentID) && role.ParentID.IsValidUUID())
				tasks.Add(Utility.Cache.RemoveAsync(Extensions.GetRelatedCacheKeys(role.SystemID.GetRolesFilter(role.ParentID), Sorts<Role>.Ascending("Title")), cancellationToken));
			if (!string.IsNullOrWhiteSpace(oldParentID) && oldParentID.IsValidUUID())
				tasks.Add(Utility.Cache.RemoveAsync(Extensions.GetRelatedCacheKeys(role.SystemID.GetRolesFilter(oldParentID), Sorts<Role>.Ascending("Title")), cancellationToken));
			return Task.WhenAll(tasks);
		}

		internal static async Task<JObject> SearchRolesAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, CancellationToken cancellationToken = default)
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
			var organizationID = filter.GetValue("SystemID") ?? requestInfo.GetParameter("x-system-id") ?? requestInfo.GetParameter("SystemID");
			var organization = await (organizationID ?? "").GetOrganizationByIDAsync(cancellationToken).ConfigureAwait(false);
			if (organization == null)
				throw new InformationExistedException("The organization is invalid");

			// check permission
			var gotRights = isSystemAdministrator || requestInfo.Session.User.ID.IsEquals(organization.OwnerID) || requestInfo.Session.User.IsViewer(organization.WorkingPrivileges);
			if (!gotRights)
				throw new AccessDeniedException();

			// process cache
			var json = string.IsNullOrWhiteSpace(query) ? await Utility.Cache.GetAsync<string>(Extensions.GetCacheKeyOfObjectsJson(filter, sort, pageSize, pageNumber), cancellationToken).ConfigureAwait(false) : null;
			if (!string.IsNullOrWhiteSpace(json))
				return JObject.Parse(json);

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
					: await Role.SearchAsync(query, filter, pageSize, pageNumber, cancellationToken).ConfigureAwait(false)
				: new List<Role>();

			// build result
			pagination = new Tuple<long, int, int, int>(totalRecords, totalPages, pageSize, pageNumber);
			var response = new JObject
			{
				{ "FilterBy", filter.ToClientJson(query) },
				{ "SortBy", sort?.ToClientJson() },
				{ "Pagination", pagination.GetPagination() },
				{ "Objects", objects.ToJsonArray() }
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

		internal static async Task<JObject> CreateRoleAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, string encryptionKey = null, Func<RequestInfo, CancellationToken, Task> serviceCaller = null, Action<RequestInfo, string, Exception> onServiceCallerGotError = null, string nodeID = null, IRTUService rtuService = null, CancellationToken cancellationToken = default)
		{
			// prepare
			var request = requestInfo.GetBodyExpando();
			var organizationID = request.Get<string>("SystemID") ?? requestInfo.GetParameter("x-system-id") ?? requestInfo.GetParameter("SystemID");
			var organization = await (organizationID ?? "").GetOrganizationByIDAsync(cancellationToken).ConfigureAwait(false);
			if (organization == null)
				throw new InformationInvalidException("The organization is invalid");

			// check permission
			var gotRights = isSystemAdministrator || requestInfo.Session.User.ID.IsEquals(organization.OwnerID) || requestInfo.Session.User.IsModerator(organization.WorkingPrivileges);
			if (!gotRights)
				throw new AccessDeniedException();

			// create new
			var role = request.CreateRoleInstance("SystemID,Privileges,OriginalPrivileges,Created,CreatedID,LastModified,LastModifiedID", obj =>
			{
				obj.ID = string.IsNullOrWhiteSpace(obj.ID) || !obj.ID.IsValidUUID() ? UtilityService.NewUUID : obj.ID;
				obj.SystemID = organization.ID;
				obj.ParentID = obj.ParentRole != null ? obj.ParentID : null;
				obj.Created = obj.LastModified = DateTime.Now;
				obj.CreatedID = obj.LastModifiedID = requestInfo.Session.User.ID;
				obj._childrenIDs = new List<string>();
			});
			await Task.WhenAll(
				Role.CreateAsync(role, cancellationToken),
				role.SetAsync(false, cancellationToken)
			).ConfigureAwait(false);
			role.ClearRelatedCacheAsync(null, cancellationToken).Run();

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
					{ "AddedRoles", new[] { role.ID }.ToJArray().ToString(Formatting.None).Encrypt(encryptionKey) }
				}
			};

			var userIDs = role.UserIDs ?? new List<string>();
			var parentRole = role.ParentRole;
			while (parentRole != null)
			{
				userIDs = userIDs.Concat(parentRole.UserIDs ?? new List<string>()).ToList();
				parentRole = parentRole.ParentRole;
			}

			await userIDs.Distinct(StringComparer.OrdinalIgnoreCase).ForEachAsync(async (userID, token) =>
			{
				try
				{
					requestUser.Query["object-identity"] = userID;
					await (serviceCaller == null ? Task.CompletedTask : serviceCaller(requestUser, token)).ConfigureAwait(false);
				}
				catch (Exception ex)
				{
					onServiceCallerGotError?.Invoke(requestUser, $"Error occurred while updating roles of an user account [{userID}] => {ex.Message}", ex);
				}
			}, cancellationToken, true, false).ConfigureAwait(false);

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
					ExcludedNodeID = nodeID
				});
			}

			// message to update to all other connected clients
			var response = role.ToJson(true, false);
			if (role.ParentRole == null)
				updateMessages.Add(new UpdateMessage
				{
					Type = $"{requestInfo.ServiceName}#{objectName}#Create",
					Data = response,
					DeviceID = "*",
					ExcludedDeviceID = requestInfo.Session.DeviceID
				});

			// message to update to all service instances (on all other nodes)
			communicateMessages.Add(new CommunicateMessage(requestInfo.ServiceName)
			{
				Type = $"{objectName}#Create",
				Data = response,
				ExcludedNodeID = nodeID
			});

			// send the messages and response
			await Task.WhenAll(
				updateMessages.ForEachAsync((message, token) => rtuService == null ? Task.CompletedTask : rtuService.SendUpdateMessageAsync(message, token), cancellationToken, true, false),
				communicateMessages.ForEachAsync((message, token) => rtuService == null ? Task.CompletedTask : rtuService.SendInterCommunicateMessageAsync(message, token), cancellationToken)
			).ConfigureAwait(false);
			return response;
		}

		internal static async Task<JObject> GetRoleAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, IRTUService rtuService = null, CancellationToken cancellationToken = default)
		{
			// prepare
			var role = await (requestInfo.GetObjectIdentity() ?? "").GetRoleByIDAsync(cancellationToken).ConfigureAwait(false);
			if (role == null)
				throw new InformationNotFoundException();
			else if (role.Organization == null)
				throw new InformationInvalidException("The organization is invalid");

			// check permission
			var gotRights = isSystemAdministrator || requestInfo.Session.User.ID.IsEquals(role.Organization.OwnerID) || requestInfo.Session.User.IsViewer(role.Organization.WorkingPrivileges);
			if (!gotRights)
				throw new AccessDeniedException();

			// prepare the response
			if (role._childrenIDs == null)
			{
				await role.FindChildrenAsync(cancellationToken).ConfigureAwait(false);
				await role.SetAsync(true, cancellationToken).ConfigureAwait(false);
			}

			// send the update message to update to all other connected clients and response
			var response = role.ToJson(true, false);
			await (rtuService == null ? Task.CompletedTask : rtuService.SendUpdateMessageAsync(new UpdateMessage
			{
				Type = $"{requestInfo.ServiceName}#{role.GetTypeName(true)}#Update",
				Data = response,
				DeviceID = "*",
				ExcludedDeviceID = requestInfo.Session.DeviceID
			}, cancellationToken)).ConfigureAwait(false);
			return response;
		}

		internal static async Task<JObject> UpdateRoleAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, string encryptionKey = null, Func<RequestInfo, CancellationToken, Task> serviceCaller = null, Action<RequestInfo, string, Exception> onServiceCallerGotError = null, string nodeID = null, IRTUService rtuService = null, CancellationToken cancellationToken = default)
		{
			// prepare
			var role = await (requestInfo.GetObjectIdentity() ?? "").GetRoleByIDAsync(cancellationToken).ConfigureAwait(false);
			if (role == null)
				throw new InformationNotFoundException();
			else if (role.Organization == null)
				throw new InformationInvalidException("The organization is invalid");

			// check permission
			var gotRights = isSystemAdministrator || requestInfo.Session.User.ID.IsEquals(role.Organization.OwnerID) || requestInfo.Session.User.IsModerator(role.Organization.WorkingPrivileges);
			if (!gotRights)
				throw new AccessDeniedException();

			// update
			var oldParentID = role.ParentID;
			var oldUserIDs = role.UserIDs ?? new List<string>();

			role.UpdateRoleInstance(requestInfo.GetBodyExpando(), "ID,SystemID,Privileges,OriginalPrivileges,Created,CreatedID,LastModified,LastModifiedID", async obj =>
			{
				obj.LastModified = DateTime.Now;
				obj.LastModifiedID = requestInfo.Session.User.ID;
				await obj.FindChildrenAsync(cancellationToken).ConfigureAwait(false);
			});
			await Task.WhenAll(
				Role.UpdateAsync(role, requestInfo.Session.User.ID, cancellationToken),
				role.SetAsync(false, cancellationToken)
			).ConfigureAwait(false);
			role.ClearRelatedCacheAsync(oldParentID, cancellationToken).Run();

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
			requestUser.Extra["RemovedRoles"] = new[] { role.ID }.ToJArray().ToString(Formatting.None).Encrypt(encryptionKey);
			await beRemovedUserIDs.ForEachAsync(async (userID, token) =>
			{
				try
				{
					requestUser.Query["object-identity"] = userID;
					await (serviceCaller == null ? Task.CompletedTask : serviceCaller(requestUser, token)).ConfigureAwait(false);
				}
				catch (Exception ex)
				{
					onServiceCallerGotError?.Invoke(requestUser, $"Error occurred while updating roles of an user account [{userID}] => {ex.Message}", ex);
				}
			}, cancellationToken, true, false).ConfigureAwait(false);

			requestUser.Extra.Clear();
			requestUser.Extra["AddedRoles"] = new[] { role.ID }.ToJArray().ToString(Formatting.None).Encrypt(encryptionKey);
			await beAddedUserIDs.ForEachAsync(async (userID, token) =>
			{
				try
				{
					requestUser.Query["object-identity"] = userID;
					await (serviceCaller == null ? Task.CompletedTask : serviceCaller(requestUser, token)).ConfigureAwait(false);
				}
				catch (Exception ex)
				{
					onServiceCallerGotError?.Invoke(requestUser, $"Error occurred while updating roles of an user account [{userID}] => {ex.Message}", ex);
				}
			}, cancellationToken, true, false).ConfigureAwait(false);

			// update parent
			var updateMessages = new List<UpdateMessage>();
			var communicateMessages = new List<CommunicateMessage>();
			var objectName = role.GetTypeName(true);

			if (role.ParentRole != null && !role.ParentID.IsEquals(oldParentID))
			{
				await role.ParentRole.FindChildrenAsync(cancellationToken).ConfigureAwait(false);
				role.ParentRole._childrenIDs.Add(role.ID);
				await role.ParentRole.SetAsync(true).ConfigureAwait(false);

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
					ExcludedNodeID = nodeID
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
						ExcludedNodeID = nodeID
					});
				}
			}

			// message to update to all other connected clients
			var response = role.ToJson(true, false);
			if (string.IsNullOrWhiteSpace(oldParentID) && role.ParentRole == null)
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

		internal static async Task<JObject> DeleteRoleAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, string encryptionKey = null, Func<RequestInfo, CancellationToken, Task> serviceCaller = null, Action<RequestInfo, string, Exception> onServiceCallerGotError = null, string nodeID = null, IRTUService rtuService = null, CancellationToken cancellationToken = default)
		{
			// prepare
			var role = await (requestInfo.GetObjectIdentity() ?? "").GetRoleByIDAsync(cancellationToken).ConfigureAwait(false);
			if (role == null)
				throw new InformationNotFoundException();
			else if (role.Organization == null)
				throw new InformationInvalidException("The organization is invalid");

			// check permission
			var gotRights = isSystemAdministrator || requestInfo.Session.User.ID.IsEquals(role.Organization.OwnerID) || requestInfo.Session.User.IsModerator(role.Organization.WorkingPrivileges);
			if (!gotRights)
				throw new AccessDeniedException();

			var updateMessages = new List<UpdateMessage>();
			var communicateMessages = new List<CommunicateMessage>();
			var objectName = role.GetTypeName(true);
			var updateChildren = requestInfo.Header.TryGetValue("x-children", out var childrenMode) && "set-null".IsEquals(childrenMode);

			// delete
			await (await role.FindChildrenAsync(cancellationToken).ConfigureAwait(false)).ForEachAsync(async (child, token) =>
			{
				// update children to root
				if (updateChildren)
				{
					child.ParentID = null;
					child.LastModified = DateTime.Now;
					child.LastModifiedID = requestInfo.Session.User.ID;

					await Task.WhenAll(
						Role.UpdateAsync(child, requestInfo.Session.User.ID, token),
						child.SetAsync(false, token)
					).ConfigureAwait(false);

					var json = child.ToJson(true, false);
					updateMessages.Add(new UpdateMessage
					{
						Type = $"{requestInfo.ServiceName}#{objectName}#Delete",
						Data = json,
						DeviceID = "*"
					});
					communicateMessages.Add(new CommunicateMessage(requestInfo.ServiceName)
					{
						ServiceName = requestInfo.ServiceName,
						Type = $"{objectName}#Delete",
						Data = json,
						ExcludedNodeID = nodeID
					});
				}

				// delete children
				else
				{
					var messages = await child.DeleteChildrenAsync(requestInfo, encryptionKey, serviceCaller, onServiceCallerGotError, nodeID, token).ConfigureAwait(false);
					updateMessages = updateMessages.Concat(messages.Item1).ToList();
					communicateMessages = communicateMessages.Concat(messages.Item2).ToList();
				}
			}, cancellationToken, true, false).ConfigureAwait(false);

			await Role.DeleteAsync<Role>(role.ID, requestInfo.Session.User.ID, cancellationToken).ConfigureAwait(false);
			role.ClearRelatedCacheAsync(null, cancellationToken).Run();
			role.Remove();

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
					{ "RemovedRoles", new[] { role.ID }.ToJArray().ToString(Formatting.None).Encrypt(encryptionKey) }
				}
			};
			await beRemovedUserIDs.ForEachAsync(async (userID, token) =>
			{
				try
				{
					requestUser.Query["object-identity"] = userID;
					await (serviceCaller == null ? Task.CompletedTask : serviceCaller(requestUser, token)).ConfigureAwait(false);
				}
				catch (Exception ex)
				{
					onServiceCallerGotError?.Invoke(requestUser, $"Error occurred while updating roles of an user account [{userID}] => {ex.Message}", ex);
				}
			}, cancellationToken, true, false).ConfigureAwait(false);

			// message to update to all other connected clients
			var response = role.ToJson();
			updateMessages.Add(new UpdateMessage
			{
				Type = $"{requestInfo.ServiceName}#{objectName}#Delete",
				DeviceID = "*",
				Data = response
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

		static async Task<Tuple<List<UpdateMessage>, List<CommunicateMessage>>> DeleteChildrenAsync(this Role role, RequestInfo requestInfo, string encryptionKey = null, Func<RequestInfo, CancellationToken, Task> serviceCaller = null, Action<RequestInfo, string, Exception> onServiceCallerGotError = null, string nodeID = null, CancellationToken cancellationToken = default)
		{
			var updateMessages = new List<UpdateMessage>();
			var communicateMessages = new List<CommunicateMessage>();
			var objectName = role.GetTypeName(true);

			var children = await role.FindChildrenAsync(cancellationToken).ConfigureAwait(false);
			await children.ForEachAsync(async (child, token) =>
			{
				var messages = await child.DeleteChildrenAsync(requestInfo, encryptionKey, serviceCaller, onServiceCallerGotError, nodeID, token).ConfigureAwait(false);
				updateMessages = updateMessages.Concat(messages.Item1).ToList();
				communicateMessages = communicateMessages.Concat(messages.Item2).ToList();
			}, cancellationToken, true, false).ConfigureAwait(false);

			await Role.DeleteAsync<Role>(role.ID, requestInfo.Session.User.ID, cancellationToken).ConfigureAwait(false);
			role.ClearRelatedCacheAsync(null, cancellationToken).Run();
			role.Remove();

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
					{ "RemovedRoles", new[] { role.ID }.ToJArray().ToString(Formatting.None).Encrypt(encryptionKey) }
				}
			};
			await beRemovedUserIDs.ForEachAsync(async (userID, token) =>
			{
				try
				{
					requestUser.Query["object-identity"] = userID;
					await (serviceCaller == null ? Task.CompletedTask : serviceCaller(requestUser, token)).ConfigureAwait(false);
				}
				catch (Exception ex)
				{
					onServiceCallerGotError?.Invoke(requestUser, $"Error occurred while updating roles of an user account [{userID}] => {ex.Message}", ex);
				}
			}, cancellationToken, true, false).ConfigureAwait(false);

			var json = role.ToJson();
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
			return new Tuple<List<UpdateMessage>, List<CommunicateMessage>>(updateMessages, communicateMessages);
		}

		internal static async Task<JObject> SyncRoleAsync(this RequestInfo requestInfo, string nodeID = null, IRTUService rtuService = null, CancellationToken cancellationToken = default)
		{
			var data = requestInfo.GetBodyExpando();
			var role = await data.Get<string>("ID").GetRoleByIDAsync(cancellationToken).ConfigureAwait(false);
			if (role == null)
			{
				role = Role.CreateInstance(data);
				await Role.CreateAsync(role, cancellationToken).ConfigureAwait(false);
			}
			else
			{
				role.Fill(data);
				await Role.UpdateAsync(role, true, cancellationToken).ConfigureAwait(false);
			}

			// send update messages
			var json = role.Set().ToJson();
			var objectName = role.GetTypeName(true);
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
				{ "ID", role.ID },
				{ "Type", objectName }
			};
		}
	}
}
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
	public static class ModuleProcessor
	{
		internal static ConcurrentDictionary<string, Module> Modules { get; } = new ConcurrentDictionary<string, Module>(StringComparer.OrdinalIgnoreCase);

		internal static HashSet<string> ExtraProperties { get; } = "Notifications,Trackings,EmailSettings".ToHashSet();

		public static Module CreateModuleInstance(this ExpandoObject requestBody, string excluded = null, Action<Module> onCompleted = null)
			=> requestBody.Copy<Module>(excluded?.ToHashSet(), module =>
			{
				module.OriginalPrivileges = module.OriginalPrivileges?.Normalize();
				module.TrimAll();
				onCompleted?.Invoke(module);
			});

		public static Module UpdateModuleInstance(this Module module, ExpandoObject requestBody, string excluded = null, Action<Module> onCompleted = null)
		{
			module.CopyFrom(requestBody, excluded?.ToHashSet());
			module.OriginalPrivileges = module.OriginalPrivileges?.Normalize();
			module.TrimAll();
			onCompleted?.Invoke(module);
			return module;
		}

		internal static Module Set(this Module module, bool updateCache = false)
		{
			if (module != null)
			{
				ModuleProcessor.Modules[module.ID] = module;
				module.RepositoryDefinition.Register(module);
				if (updateCache)
					Utility.Cache.Set(module);
			}
			return module;
		}

		internal static async Task<Module> SetAsync(this Module module, bool updateCache = false, CancellationToken cancellationToken = default)
		{
			module?.Set();
			await (updateCache && module != null ? Utility.Cache.SetAsync(module, cancellationToken) : Task.CompletedTask).ConfigureAwait(false);
			return module;
		}

		internal static Module Remove(this Module module)
			=> (module?.ID ?? "").RemoveModule();

		internal static Module RemoveModule(this string id)
		{
			if (!string.IsNullOrWhiteSpace(id) && ModuleProcessor.Modules.TryRemove(id, out var module) && module != null)
			{
				module.RepositoryDefinition.Unregister(module);
				return module;
			}
			return null;
		}

		public static Module GetModuleByID(this string id, bool force = false, bool fetchRepository = true)
			=> !force && !string.IsNullOrWhiteSpace(id) && ModuleProcessor.Modules.ContainsKey(id)
				? ModuleProcessor.Modules[id]
				: fetchRepository && !string.IsNullOrWhiteSpace(id)
					? Module.Get<Module>(id)?.Set()
					: null;

		public static async Task<Module> GetModuleByIDAsync(this string id, CancellationToken cancellationToken = default, bool force = false)
			=> (id ?? "").GetModuleByID(force, false) ?? (await Module.GetAsync<Module>(id, cancellationToken).ConfigureAwait(false))?.Set();

		public static IFilterBy<Module> GetModulesFilter(this string systemID, string definitionID = null)
		{
			var filter = Filters<Module>.And(Filters<Module>.Equals("SystemID", systemID));
			if (!string.IsNullOrWhiteSpace(definitionID))
				filter.Add(Filters<Module>.Equals("DefinitionID", definitionID));
			return filter;
		}

		public static List<Module> FindModules(this string systemID, string definitionID = null, bool updateCache = true)
		{
			if (string.IsNullOrWhiteSpace(systemID))
				return new List<Module>();
			var filter = systemID.GetModulesFilter(definitionID);
			var sort = Sorts<Module>.Ascending("Title");
			var modules = Module.Find(filter, sort, 0, 1, Extensions.GetCacheKey(filter, sort, 0, 1));
			modules.ForEach(module => module.Set(updateCache));
			return modules;
		}

		public static async Task<List<Module>> FindModulesAsync(this string systemID, string definitionID = null, CancellationToken cancellationToken = default, bool updateCache = true)
		{
			if (string.IsNullOrWhiteSpace(systemID))
				return new List<Module>();
			var filter = systemID.GetModulesFilter(definitionID);
			var sort = Sorts<Module>.Ascending("Title");
			var modules = await Module.FindAsync(filter, sort, 0, 1, Extensions.GetCacheKey(filter, sort, 0, 1), cancellationToken).ConfigureAwait(false);
			await modules.ForEachAsync((module, token) => module.SetAsync(updateCache, token), cancellationToken).ConfigureAwait(false);
			return modules;
		}

		internal static async Task ProcessInterCommunicateMessageOfModuleAsync(this CommunicateMessage message, CancellationToken cancellationToken = default)
		{
			if (message.Type.IsEndsWith("#Create"))
				await message.Data.ToExpandoObject().CreateModuleInstance().SetAsync(false, cancellationToken).ConfigureAwait(false);

			else if (message.Type.IsEndsWith("#Update"))
			{
				var module = message.Data.Get("ID", "").GetModuleByID(false, false);
				await (module == null ? message.Data.ToExpandoObject().CreateModuleInstance() : module.UpdateModuleInstance(message.Data.ToExpandoObject())).SetAsync(false, cancellationToken).ConfigureAwait(false);
			}

			else if (message.Type.IsEndsWith("#Delete"))
				message.Data.ToExpandoObject().CreateModuleInstance().Remove();
		}

		static Task ClearRelatedCache(this Module module, CancellationToken cancellationToken = default)
			=> Task.WhenAll
			(
				Utility.Cache.RemoveAsync(Extensions.GetRelatedCacheKeys(Filters<Module>.And(), Sorts<Module>.Ascending("Title")), cancellationToken),
				Utility.Cache.RemoveAsync(Extensions.GetRelatedCacheKeys(module.SystemID.GetModulesFilter(), Sorts<Module>.Ascending("Title")), cancellationToken),
				Utility.Cache.RemoveAsync(Extensions.GetRelatedCacheKeys(module.SystemID.GetModulesFilter(module.ModuleDefinitionID), Sorts<Module>.Ascending("Title")), cancellationToken)
			);

		internal static async Task<JObject> SearchModulesAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, CancellationToken cancellationToken = default)
		{
			// prepare
			var request = requestInfo.GetRequestExpando();

			var query = request.Get<string>("FilterBy.Query");
			var filter = request.Get<ExpandoObject>("FilterBy")?.ToFilterBy<Module>() ?? Filters<Module>.And();
			var sort = string.IsNullOrWhiteSpace(query) ? request.Get<ExpandoObject>("SortBy")?.ToSortBy<Module>() ?? Sorts<Module>.Ascending("Title") : null;

			var pagination = request.Get<ExpandoObject>("Pagination")?.GetPagination() ?? new Tuple<long, int, int, int>(-1, 0, 20, 1);
			var pageSize = pagination.Item3;
			var pageNumber = pagination.Item4;

			// check permission
			var gotRights = isSystemAdministrator;
			if (!gotRights)
			{
				// get organization
				var organizationID = filter.GetValue("SystemID") ?? requestInfo.GetParameter("x-system") ?? requestInfo.GetParameter("SystemID");
				var organization = await (organizationID ?? "").GetOrganizationByIDAsync(cancellationToken).ConfigureAwait(false);
				if (organization == null)
					throw new InformationExistedException("The organization is invalid");

				gotRights = requestInfo.Session.User.ID.IsEquals(organization.OwnerID) || requestInfo.Session.User.IsModerator(organization.WorkingPrivileges);
				if (!gotRights)
					throw new AccessDeniedException();
			}

			// process cache
			var json = string.IsNullOrWhiteSpace(query) ? await Utility.Cache.GetAsync<string>(Extensions.GetCacheKeyOfObjectsJson(filter, sort, pageSize, pageNumber), cancellationToken).ConfigureAwait(false) : null;
			if (!string.IsNullOrWhiteSpace(json))
				return JObject.Parse(json);

			// prepare pagination
			var totalRecords = pagination.Item1 > -1 ? pagination.Item1 : -1;
			if (totalRecords < 0)
				totalRecords = string.IsNullOrWhiteSpace(query)
					? await Module.CountAsync(filter, Extensions.GetCacheKeyOfTotalObjects(filter, sort), cancellationToken).ConfigureAwait(false)
					: await Module.CountAsync(query, filter, cancellationToken).ConfigureAwait(false);

			var totalPages = new Tuple<long, int>(totalRecords, pageSize).GetTotalPages();
			if (totalPages > 0 && pageNumber > totalPages)
				pageNumber = totalPages;

			// search
			var objects = totalRecords > 0
				? string.IsNullOrWhiteSpace(query)
					? await Module.FindAsync(filter, sort, pageSize, pageNumber, Extensions.GetCacheKey(filter, sort, pageSize, pageNumber), cancellationToken).ConfigureAwait(false)
					: await Module.SearchAsync(query, filter, pageSize, pageNumber, cancellationToken).ConfigureAwait(false)
				: new List<Module>();

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

		internal static async Task<JObject> CreateModuleAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, string nodeID = null, IRTUService rtuService = null, CancellationToken cancellationToken = default)
		{
			// prepare
			var requestBody = requestInfo.GetBodyExpando();
			var organizationID = requestBody.Get<string>("SystemID") ?? requestInfo.GetParameter("x-system") ?? requestInfo.GetParameter("SystemID");
			var organization = await (organizationID ?? "").GetOrganizationByIDAsync(cancellationToken).ConfigureAwait(false);
			if (organization == null)
				throw new InformationInvalidException("The organization is invalid");

			// check permission
			var gotRights = isSystemAdministrator || requestInfo.Session.User.ID.IsEquals(organization.OwnerID) || requestInfo.Session.User.IsModerator(organization.WorkingPrivileges);
			if (!gotRights)
				throw new AccessDeniedException();

			// create new module
			var module = requestBody.CreateModuleInstance("SystemID,Privileges,Created,CreatedID,LastModified,LastModifiedID", obj =>
			{
				obj.ID = string.IsNullOrWhiteSpace(obj.ID) || !obj.ID.IsValidUUID() ? UtilityService.NewUUID : obj.ID;
				obj.SystemID = organization.ID;
				obj.Created = obj.LastModified = DateTime.Now;
				obj.CreatedID = obj.LastModifiedID = requestInfo.Session.User.ID;
				obj.NormalizeExtras();
			});
			await Task.WhenAll(
				Module.CreateAsync(module, cancellationToken),
				module.ClearRelatedCache(cancellationToken),
				module.SetAsync(false, cancellationToken)
			).ConfigureAwait(false);

			// create new content-types
			var contentTypeJson = new JObject
			{
				{ "SystemID", module.SystemID },
				{ "RepositoryID", module.ID }
			};
			module._contentTypeIDs = new List<string>();
			await Utility.ModuleDefinitions[module.ModuleDefinitionID].ContentTypeDefinitions.ForEachAsync(async (contentTypeDefinition, token) =>
			{
				contentTypeJson["ContentTypeDefinitionID"] = contentTypeDefinition.ID;
				contentTypeJson["Title"] = contentTypeDefinition.Title;
				contentTypeJson["Description"] = contentTypeDefinition.Description;
				await contentTypeJson.CreateContentTypeAsync(organization.ID, requestInfo.Session.User.ID, null, null, requestInfo.ServiceName, nodeID, rtuService, token).ConfigureAwait(false);
			}, cancellationToken, true, false).ConfigureAwait(false);

			// update instance/cache of organization
			if (organization._moduleIDs == null)
				await organization.FindModulesAsync(cancellationToken).ConfigureAwait(false);
			else
			{
				organization._moduleIDs.Add(module.ID);
				await organization.SetAsync(false, true, cancellationToken).ConfigureAwait(false);
			}

			// send update messages
			var response = module.ToJson(true, false);
			var objectName = module.GetTypeName(true);
			await Task.WhenAll(
				rtuService == null ? Task.CompletedTask : rtuService.SendUpdateMessageAsync(new UpdateMessage
				{
					Type = $"{requestInfo.ServiceName}#{objectName}#Create",
					Data = response,
					DeviceID = "*",
					ExcludedDeviceID = requestInfo.Session.DeviceID
				}, cancellationToken),
				rtuService == null ? Task.CompletedTask : rtuService.SendInterCommunicateMessageAsync(new CommunicateMessage(requestInfo.ServiceName)
				{
					Type = $"{objectName}#Create",
					Data = response,
					ExcludedNodeID = nodeID
				}, cancellationToken)
			).ConfigureAwait(false);

			// response
			return response;
		}

		internal static async Task<JObject> GetModuleAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, IRTUService rtuService = null, CancellationToken cancellationToken = default)
		{
			// prepare
			var module = await (requestInfo.GetObjectIdentity() ?? "").GetModuleByIDAsync(cancellationToken).ConfigureAwait(false);
			if (module == null)
				throw new InformationNotFoundException();
			else if (module.Organization == null)
				throw new InformationInvalidException("The organization is invalid");

			// check permission
			var gotRights = isSystemAdministrator || requestInfo.Session.User.ID.IsEquals(module.Organization.OwnerID) || requestInfo.Session.User.IsViewer(module.Organization.WorkingPrivileges);
			if (!gotRights)
				throw new AccessDeniedException();

			// get content-types
			if (module._contentTypeIDs == null)
			{
				await module.FindContentTypesAsync(cancellationToken).ConfigureAwait(false);
				await module.SetAsync(true, cancellationToken).ConfigureAwait(false);
			}

			// send the update message to update to all other connected clients and response
			var response = module.ToJson(true, false);
			await (rtuService == null ? Task.CompletedTask : rtuService.SendUpdateMessageAsync(new UpdateMessage
			{
				Type = $"{requestInfo.ServiceName}#{module.GetTypeName(true)}#Update",
				Data = response,
				DeviceID = "*",
				ExcludedDeviceID = requestInfo.Session.DeviceID
			}, cancellationToken)).ConfigureAwait(false);
			return response;
		}

		internal static async Task<JObject> UpdateModuleAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, string nodeID = null, IRTUService rtuService = null, CancellationToken cancellationToken = default)
		{
			// prepare
			var module = await (requestInfo.GetObjectIdentity() ?? "").GetModuleByIDAsync(cancellationToken).ConfigureAwait(false);
			if (module == null)
				throw new InformationNotFoundException();
			else if (module.Organization == null)
				throw new InformationInvalidException("The organization is invalid");

			// check permission
			var gotRights = isSystemAdministrator || requestInfo.Session.User.ID.IsEquals(module.Organization.OwnerID) || requestInfo.Session.User.IsModerator(module.Organization.WorkingPrivileges);
			if (!gotRights)
				throw new AccessDeniedException();

			// update
			module.UpdateModuleInstance(requestInfo.GetBodyExpando(), "ID,SystemID,Privileges,Created,CreatedID,LastModified,LastModifiedID", obj =>
			{
				obj.LastModified = DateTime.Now;
				obj.LastModifiedID = requestInfo.Session.User.ID;
				obj.NormalizeExtras();
			});
			await Task.WhenAll(
				Module.UpdateAsync(module, requestInfo.Session.User.ID, cancellationToken),
				module.ClearRelatedCache(cancellationToken),
				module.SetAsync(false, cancellationToken)
			).ConfigureAwait(false);

			// send update messages
			var response = module.ToJson();
			var objectName = module.GetTypeName(true);
			await Task.WhenAll(
				rtuService == null ? Task.CompletedTask : rtuService.SendUpdateMessageAsync(new UpdateMessage
				{
					Type = $"{requestInfo.ServiceName}#{objectName}#Update",
					Data = response,
					DeviceID = "*",
					ExcludedDeviceID = requestInfo.Session.DeviceID
				}, cancellationToken),
				rtuService == null ? Task.CompletedTask : rtuService.SendInterCommunicateMessageAsync(new CommunicateMessage(requestInfo.ServiceName)
				{
					Type = $"{objectName}#Update",
					Data = response,
					ExcludedNodeID = nodeID
				}, cancellationToken)
			).ConfigureAwait(false);

			// response
			return response;
		}

		internal static async Task<JObject> DeleteModuleAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, string nodeID = null, IRTUService rtuService = null, CancellationToken cancellationToken = default)
		{
			// prepare
			var module = await (requestInfo.GetObjectIdentity() ?? "").GetModuleByIDAsync(cancellationToken).ConfigureAwait(false);
			if (module == null)
				throw new InformationNotFoundException();
			else if (module.Organization == null)
				throw new InformationInvalidException("The organization is invalid");

			// check permission
			var gotRights = isSystemAdministrator || requestInfo.Session.User.ID.IsEquals(module.Organization.OwnerID) || requestInfo.Session.User.IsModerator(module.Organization.WorkingPrivileges);
			if (!gotRights)
				throw new AccessDeniedException();

			// TO DO: delete all content-types (and business objects) first
			// .......

			// delete
			await Module.DeleteAsync<Module>(module.ID, requestInfo.Session.User.ID, cancellationToken).ConfigureAwait(false);
			module.Remove();
			await module.ClearRelatedCache(cancellationToken).ConfigureAwait(false);

			// update instance/cache of organization
			if (module.Organization._moduleIDs != null)
			{
				module.Organization._moduleIDs.Remove(module.ID);
				await module.Organization.SetAsync(false, true, cancellationToken).ConfigureAwait(false);
			}

			// send update messages
			var response = module.ToJson();
			var objectName = module.GetTypeName(true);
			await Task.WhenAll(
				rtuService == null ? Task.CompletedTask : rtuService.SendUpdateMessageAsync(new UpdateMessage
				{
					Type = $"{requestInfo.ServiceName}#{objectName}#Delete",
					Data = response,
					DeviceID = "*",
					ExcludedDeviceID = requestInfo.Session.DeviceID
				}, cancellationToken),
				rtuService == null ? Task.CompletedTask : rtuService.SendInterCommunicateMessageAsync(new CommunicateMessage(requestInfo.ServiceName)
				{
					Type = $"{objectName}#Delete",
					Data = response,
					ExcludedNodeID = nodeID
				}, cancellationToken)
			).ConfigureAwait(false);

			// response
			return response;
		}
	}
}
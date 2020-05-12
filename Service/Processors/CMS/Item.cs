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
	public static class ItemProcessor
	{
		public static Item CreateItemInstance(this ExpandoObject requestBody, string excluded = null, Action<Item> onCompleted = null)
			=> requestBody.Copy<Item>(excluded?.ToHashSet(), item =>
			{
				item.TrimAll();
				item.OriginalPrivileges = item.OriginalPrivileges?.Normalize();
				item.Tags = string.IsNullOrWhiteSpace(item.Tags) ? null : item.Tags.Replace(";", ",").ToList(",", true).Where(tag => !string.IsNullOrWhiteSpace(tag)).Join(",");
				onCompleted?.Invoke(item);
			});

		public static Item UpdateItemInstance(this Item item, ExpandoObject requestBody, string excluded = null, Action<Item> onCompleted = null)
		{
			item.CopyFrom(requestBody, excluded?.ToHashSet());
			item.TrimAll();
			item.OriginalPrivileges = item.OriginalPrivileges?.Normalize();
			item.Tags = string.IsNullOrWhiteSpace(item.Tags) ? null : item.Tags.Replace(";", ",").ToList(",", true).Where(tag => !string.IsNullOrWhiteSpace(tag)).Join(",");
			onCompleted?.Invoke(item);
			return item;
		}

		public static IFilterBy<Item> GetItemsFilter(this string systemID, string repositoryID = null, string repositoryEntityID = null)
		{
			var filter = Filters<Item>.And(Filters<Item>.Equals("SystemID", systemID));
			if (!string.IsNullOrWhiteSpace(repositoryID))
				filter.Add(Filters<Item>.Equals("RepositoryID", repositoryID));
			if (!string.IsNullOrWhiteSpace(repositoryEntityID))
				filter.Add(Filters<Item>.Equals("RepositoryEntityID", repositoryEntityID));
			return filter;
		}

		static Task ClearRelatedCache(this Item item, SortBy<Item> sort, CancellationToken cancellationToken = default)
			=> Utility.Cache.RemoveAsync(Extensions.GetRelatedCacheKeys(item.SystemID.GetItemsFilter(item.RepositoryID, item.RepositoryEntityID), sort ?? Sorts<Item>.Descending("Created").ThenByAscending("Title")), cancellationToken);

		internal static async Task<JObject> SearchItemsAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, CancellationToken cancellationToken = default)
		{
			// prepare
			var request = requestInfo.GetRequestExpando();

			var query = request.Get<string>("FilterBy.Query");

			var filter = request.Get<ExpandoObject>("FilterBy", null)?.ToFilterBy<Item>() ?? Filters<Item>.And();
			var sort = string.IsNullOrWhiteSpace(query) ? request.Get<ExpandoObject>("SortBy")?.ToSortBy<Item>() ?? Sorts<Item>.Descending("Created").ThenByAscending("Title") : null;

			var pagination = request.Get<ExpandoObject>("Pagination")?.GetPagination() ?? new Tuple<long, int, int, int>(-1, 0, 20, 1);
			var pageSize = pagination.Item3;
			var pageNumber = pagination.Item4;

			var organizationID = filter.GetValue("SystemID") ?? requestInfo.GetParameter("SystemID") ?? requestInfo.GetParameter("x-system");
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

			if (filter.Equals(Filters<Item>.And()))
				filter = organization.ID.GetItemsFilter(module.ID, contentType.ID);

			// process cache
			var json = string.IsNullOrWhiteSpace(query) ? await Utility.Cache.GetAsync<string>(Extensions.GetCacheKeyOfObjectsJson(filter, sort, pageSize, pageNumber), cancellationToken).ConfigureAwait(false) : null;
			if (!string.IsNullOrWhiteSpace(json))
				return JObject.Parse(json);

			// prepare pagination
			var totalRecords = pagination.Item1 > -1 ? pagination.Item1 : -1;
			if (totalRecords < 0)
				totalRecords = string.IsNullOrWhiteSpace(query)
					? await Item.CountAsync(filter, contentType.ID, Extensions.GetCacheKeyOfTotalObjects(filter, sort), cancellationToken).ConfigureAwait(false)
					: await Item.CountAsync(query, filter, contentType.ID, cancellationToken).ConfigureAwait(false);

			var totalPages = new Tuple<long, int>(totalRecords, pageSize).GetTotalPages();
			if (totalPages > 0 && pageNumber > totalPages)
				pageNumber = totalPages;

			// search
			var objects = totalRecords > 0
				? string.IsNullOrWhiteSpace(query)
					? await Item.FindAsync(filter, sort, pageSize, pageNumber, contentType.ID, Extensions.GetCacheKey(filter, sort, pageSize, pageNumber), cancellationToken).ConfigureAwait(false)
					: await Item.SearchAsync(query, filter, pageSize, pageNumber, contentType.ID, cancellationToken).ConfigureAwait(false)
				: new List<Item>();

			// build response
			pagination = new Tuple<long, int, int, int>(totalRecords, totalPages, pageSize, pageNumber);
			var response = new JObject()
			{
				{ "FilterBy", filter.ToClientJson(query) },
				{ "SortBy", sort?.ToClientJson() },
				{ "Pagination", pagination.GetPagination() },
				{ "Objects", objects.Select(item => item.ToJson(false, null)).ToJArray() }
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

		internal static async Task<JObject> CreateItemAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, IRTUService rtuService = null, CancellationToken cancellationToken = default)
		{
			// prepare
			var request = requestInfo.GetBodyExpando();

			var organizationID = request.Get<string>("SystemID") ?? requestInfo.GetParameter("SystemID") ?? requestInfo.GetParameter("x-system");
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
			var gotRights = isSystemAdministrator || requestInfo.Session.User.ID.IsEquals(organization.OwnerID) || requestInfo.Session.User.IsContributor(contentType.WorkingPrivileges);
			if (!gotRights)
				throw new AccessDeniedException();

			// get data
			var item = request.CreateItemInstance("SystemID,RepositoryID,RepositoryEntityID,Privileges,Created,CreatedID,LastModified,LastModifiedID", obj =>
			{
				obj.SystemID = organization.ID;
				obj.RepositoryID = module.ID;
				obj.RepositoryEntityID = contentType.ID;
				obj.ID = string.IsNullOrWhiteSpace(obj.ID) || !obj.ID.IsValidUUID() ? UtilityService.NewUUID : obj.ID;
				obj.Created = obj.LastModified = DateTime.Now;
				obj.CreatedID = obj.LastModifiedID = requestInfo.Session.User.ID;
			});

			item.Alias = string.IsNullOrWhiteSpace(item.Alias) ? item.Title.NormalizeAlias() : item.Alias.NormalizeAlias();
			var existing = await Item.GetItemByAliasAsync(contentType, item.Alias, cancellationToken).ConfigureAwait(false);
			if (existing != null)
				item.Alias += $"-{DateTime.Now.ToUnixTimestamp()}";

			// create new
			await Task.WhenAll(
				Item.CreateAsync(item, cancellationToken),
				item.ClearRelatedCache(null, cancellationToken)
			).ConfigureAwait(false);

			// send update message and response
			var entityDefinition = RepositoryMediator.GetEntityDefinition<Item>();
			var objectName = $"{(string.IsNullOrWhiteSpace(entityDefinition.ObjectNamePrefix) ? "" : entityDefinition.ObjectNamePrefix)}{entityDefinition.ObjectName}{(string.IsNullOrWhiteSpace(entityDefinition.ObjectNameSuffix) ? "" : entityDefinition.ObjectNameSuffix)}";
			var response = item.ToJson();
			await (rtuService == null ? Task.CompletedTask : rtuService.SendUpdateMessageAsync(new UpdateMessage
			{
				Type = $"{requestInfo.ServiceName}#{objectName}#Create",
				DeviceID = "*",
				ExcludedDeviceID = requestInfo.Session.DeviceID,
				Data = response
			})).ConfigureAwait(false);
			return response;
		}

		internal static async Task<JObject> GetItemAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, IRTUService rtuService = null, CancellationToken cancellationToken = default)
		{
			// prepare
			var identity = requestInfo.GetObjectIdentity() ?? "";
			var item = await (identity.IsValidUUID() ? Item.GetAsync<Item>(identity, cancellationToken) : Item.GetItemByAliasAsync(requestInfo.GetParameter("RepositoryEntityID") ?? requestInfo.GetParameter("x-content-type"), identity, cancellationToken)).ConfigureAwait(false);
			if (item == null)
				throw new InformationNotFoundException();
			else if (item.Organization == null || item.Module == null || item.ContentType == null)
				throw new InformationInvalidException("The organization/module/item-type is invalid");

			// check permission
			var gotRights = isSystemAdministrator || requestInfo.Session.User.ID.IsEquals(item.Organization.OwnerID) || requestInfo.Session.User.IsViewer(item.WorkingPrivileges);
			if (!gotRights)
				throw new AccessDeniedException();

			if (!identity.IsValidUUID())
				return new JObject
				{
					{ "ID", item.ID },
					{ "Title", item.Title },
					{ "Alias", item.Alias }
				};

			// send update message and response
			var entityDefinition = RepositoryMediator.GetEntityDefinition<Item>();
			var objectName = $"{(string.IsNullOrWhiteSpace(entityDefinition.ObjectNamePrefix) ? "" : entityDefinition.ObjectNamePrefix)}{entityDefinition.ObjectName}{(string.IsNullOrWhiteSpace(entityDefinition.ObjectNameSuffix) ? "" : entityDefinition.ObjectNameSuffix)}";
			var response = item.ToJson();
			await (rtuService == null ? Task.CompletedTask : rtuService.SendUpdateMessageAsync(new UpdateMessage
			{
				Type = $"{requestInfo.ServiceName}#{objectName}#Update",
				DeviceID = "*",
				ExcludedDeviceID = requestInfo.Session.DeviceID,
				Data = response
			})).ConfigureAwait(false);
			return response;
		}

		internal static async Task<JObject> UpdateItemAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, IRTUService rtuService = null, CancellationToken cancellationToken = default)
		{
			// prepare
			var item = await Item.GetAsync<Item>(requestInfo.GetObjectIdentity() ?? "", cancellationToken).ConfigureAwait(false);
			if (item == null)
				throw new InformationNotFoundException();
			else if (item.Organization == null || item.Module == null || item.ContentType == null)
				throw new InformationInvalidException("The organization/module/item-type is invalid");

			// check permission
			var gotRights = isSystemAdministrator || requestInfo.Session.User.ID.IsEquals(item.Organization.OwnerID) || requestInfo.Session.User.IsEditor(item.WorkingPrivileges);
			if (!gotRights)
				gotRights = item.Status.Equals(ApprovalStatus.Draft) || item.Status.Equals(ApprovalStatus.Pending) || item.Status.Equals(ApprovalStatus.Rejected)
					? requestInfo.Session.User.ID.IsEquals(item.CreatedID)
					: requestInfo.Session.User.IsEditor(item.WorkingPrivileges);
			if (!gotRights)
				throw new AccessDeniedException();

			// prepare data
			var oldAlias = item.Alias;
			item.UpdateItemInstance(requestInfo.GetBodyExpando(), "ID,SystemID,RepositoryID,RepositoryEntityID,Privileges,Created,CreatedID,LastModified,LastModifiedID", obj =>
			{
				obj.LastModified = DateTime.Now;
				obj.LastModifiedID = requestInfo.Session.User.ID;
			});

			item.Alias = string.IsNullOrWhiteSpace(item.Alias) ? oldAlias : item.Alias.NormalizeAlias();
			var existing = await Item.GetItemByAliasAsync(item.RepositoryEntityID, item.Alias, cancellationToken).ConfigureAwait(false);
			if (existing != null && !existing.ID.IsEquals(item.ID))
				item.Alias += $"-{DateTime.Now.ToUnixTimestamp()}";

			// update
			await Task.WhenAll(
				Item.UpdateAsync(item, requestInfo.Session.User.ID, cancellationToken),
				item.ClearRelatedCache(null, cancellationToken)
			).ConfigureAwait(false);

			// send update message and response
			var entityDefinition = RepositoryMediator.GetEntityDefinition<Item>();
			var objectName = $"{(string.IsNullOrWhiteSpace(entityDefinition.ObjectNamePrefix) ? "" : entityDefinition.ObjectNamePrefix)}{entityDefinition.ObjectName}{(string.IsNullOrWhiteSpace(entityDefinition.ObjectNameSuffix) ? "" : entityDefinition.ObjectNameSuffix)}";
			var response = item.ToJson();
			await (rtuService == null ? Task.CompletedTask : rtuService.SendUpdateMessageAsync(new UpdateMessage
			{
				Type = $"{requestInfo.ServiceName}#{objectName}#Update",
				DeviceID = "*",
				ExcludedDeviceID = requestInfo.Session.DeviceID,
				Data = response
			})).ConfigureAwait(false);
			return response;
		}

		internal static async Task<JObject> DeleteItemAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, IRTUService rtuService = null, CancellationToken cancellationToken = default)
		{
			// prepare
			var item = await Item.GetAsync<Item>(requestInfo.GetObjectIdentity() ?? "", cancellationToken).ConfigureAwait(false);
			if (item == null)
				throw new InformationNotFoundException();
			else if (item.Organization == null || item.Module == null || item.ContentType == null)
				throw new InformationInvalidException("The organization/module/item-type is invalid");

			// check permission
			var gotRights = isSystemAdministrator || requestInfo.Session.User.ID.IsEquals(item.Organization.OwnerID) || requestInfo.Session.User.IsModerator(item.WorkingPrivileges);
			if (!gotRights)
				gotRights = item.Status.Equals(ApprovalStatus.Draft) || item.Status.Equals(ApprovalStatus.Pending) || item.Status.Equals(ApprovalStatus.Rejected)
					? requestInfo.Session.User.ID.IsEquals(item.CreatedID) || requestInfo.Session.User.IsEditor(item.WorkingPrivileges)
					: requestInfo.Session.User.IsModerator(item.WorkingPrivileges);
			if (!gotRights)
				throw new AccessDeniedException();

			// delete
			await Item.DeleteAsync<Item>(item.ID, requestInfo.Session.User.ID, cancellationToken).ConfigureAwait(false);
			await item.ClearRelatedCache(null, cancellationToken).ConfigureAwait(false);

			// send update message and response
			var entityDefinition = RepositoryMediator.GetEntityDefinition<Item>();
			var objectName = $"{(string.IsNullOrWhiteSpace(entityDefinition.ObjectNamePrefix) ? "" : entityDefinition.ObjectNamePrefix)}{entityDefinition.ObjectName}{(string.IsNullOrWhiteSpace(entityDefinition.ObjectNameSuffix) ? "" : entityDefinition.ObjectNameSuffix)}";
			var response = item.ToJson();
			await (rtuService == null ? Task.CompletedTask : rtuService.SendUpdateMessageAsync(new UpdateMessage
			{
				Type = $"{requestInfo.ServiceName}#{objectName}#Delete",
				DeviceID = "*",
				ExcludedDeviceID = requestInfo.Session.DeviceID,
				Data = response
			})).ConfigureAwait(false);
			return response;
		}
	}
}
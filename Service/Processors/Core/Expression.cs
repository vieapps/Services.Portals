﻿#region Related components
using System;
using System.Linq;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using System.Dynamic;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using net.vieapps.Components.Utility;
using net.vieapps.Components.Security;
using net.vieapps.Components.Repository;
#endregion

namespace net.vieapps.Services.Portals
{
	public static class ExpressionProcessor
	{
		internal static ConcurrentDictionary<string, Expression> Expressions { get; } = new ConcurrentDictionary<string, Expression>(StringComparer.OrdinalIgnoreCase);

		public static Expression CreateExpressionInstance(this ExpandoObject data, string excluded = null, Action<Expression> onCompleted = null)
			=> Expression.CreateInstance(data, excluded?.ToHashSet(), onCompleted);

		public static Expression UpdateExpressionInstance(this Expression expression, ExpandoObject requestBody, string excluded = null, Action<Expression> onCompleted = null)
			=> expression.Fill(requestBody, excluded?.ToHashSet(), onCompleted);

		internal static Expression Set(this Expression expression, bool updateCache = false)
		{
			if (expression != null && !string.IsNullOrWhiteSpace(expression.ID) && !string.IsNullOrWhiteSpace(expression.Title))
			{
				ExpressionProcessor.Expressions[expression.ID] = expression;
				if (updateCache)
					Utility.Cache.SetAsync(expression).Run();
			}
			return expression;
		}

		internal static async Task<Expression> SetAsync(this Expression expression, bool updateCache = false, CancellationToken cancellationToken = default)
		{
			expression?.Set();
			await (updateCache && expression != null && !string.IsNullOrWhiteSpace(expression.ID) && !string.IsNullOrWhiteSpace(expression.Title) ? Utility.Cache.SetAsync(expression, cancellationToken) : Task.CompletedTask).ConfigureAwait(false);
			return expression;
		}

		internal static Expression Remove(this Expression expression)
			=> (expression?.ID ?? "").RemoveExpression();

		internal static Expression RemoveExpression(this string id)
			=> !string.IsNullOrWhiteSpace(id) && ExpressionProcessor.Expressions.TryRemove(id, out var expression)
				? expression
				: null;

		public static Expression GetExpressionByID(this string id, bool force = false, bool fetchRepository = true)
			=> !force && !string.IsNullOrWhiteSpace(id) && ExpressionProcessor.Expressions.ContainsKey(id)
				? ExpressionProcessor.Expressions[id]
				: fetchRepository && !string.IsNullOrWhiteSpace(id)
					? Expression.Get<Expression>(id)?.Prepare()
					: null;

		public static async Task<Expression> GetExpressionByIDAsync(this string id, CancellationToken cancellationToken = default, bool force = false)
			=> (id ?? "").GetExpressionByID(force, false) ?? (await Expression.GetAsync<Expression>(id, cancellationToken).ConfigureAwait(false))?.Prepare();

		public static IFilterBy<Expression> GetExpressionsFilter(this string systemID, string repositoryID = null, string repositoryEntityID = null, string contentTypeDefinitionID = null)
		{
			var filter = Filters<Expression>.And(Filters<Expression>.Equals("SystemID", systemID));
			if (!string.IsNullOrWhiteSpace(repositoryID))
				filter.Add(Filters<Expression>.Equals("RepositoryID", repositoryID));
			if (!string.IsNullOrWhiteSpace(repositoryEntityID))
				filter.Add(Filters<Expression>.Equals("RepositoryEntityID", repositoryEntityID));
			if (!string.IsNullOrWhiteSpace(contentTypeDefinitionID))
				filter.Add(Filters<Expression>.Equals("ContentTypeDefinitionID", contentTypeDefinitionID));
			return filter;
		}

		public static List<Expression> FindExpressions(this string systemID, string repositoryID = null, string repositoryEntityID = null, string contentTypeDefinitionID = null, bool updateCache = true)
		{
			if (string.IsNullOrWhiteSpace(systemID))
				return new List<Expression>();
			var filter = systemID.GetExpressionsFilter(repositoryID, repositoryEntityID, contentTypeDefinitionID);
			var sort = Sorts<Expression>.Ascending("Title");
			var expressions = Expression.Find(filter, sort, 0, 1, Extensions.GetCacheKey(filter, sort, 0, 1));
			expressions.ForEach(expression => expression.Set(updateCache));
			return expressions;
		}

		public static async Task<List<Expression>> FindExpressionsAsync(this string systemID, string repositoryID = null, string repositoryEntityID = null, string contentTypeDefinitionID = null, CancellationToken cancellationToken = default, bool updateCache = true)
		{
			if (string.IsNullOrWhiteSpace(systemID)) 
				return new List<Expression>();
			var filter = systemID.GetExpressionsFilter(repositoryID, repositoryEntityID,contentTypeDefinitionID);
			var sort = Sorts<Expression>.Ascending("Title");
			var expressions = await Expression.FindAsync(filter, sort, 0, 1, Extensions.GetCacheKey(filter, sort, 0, 1), cancellationToken).ConfigureAwait(false);
			await expressions.ForEachAsync((expression, token) => expression.SetAsync(updateCache, token), cancellationToken).ConfigureAwait(false);
			return expressions;
		}

		internal static Task ProcessInterCommunicateMessageOfExpressionAsync(this CommunicateMessage message, CancellationToken cancellationToken = default)
		{
			if (message.Type.IsEndsWith("#Create"))
				message.Data.ToExpandoObject().CreateExpressionInstance().Set();

			else if (message.Type.IsEndsWith("#Update"))
			{
				var expression = message.Data.Get("ID", "").GetExpressionByID(false, false);
				expression = expression == null
					? message.Data.ToExpandoObject().CreateExpressionInstance()
					: expression.UpdateExpressionInstance(message.Data.ToExpandoObject());
				expression.Set();
			}

			else if (message.Type.IsEndsWith("#Delete"))
				message.Data.ToExpandoObject().CreateExpressionInstance().Remove();

			return Task.CompletedTask;
		}

		internal static async Task ClearRelatedCacheAsync(this Expression expression, CancellationToken cancellationToken, string correlationID = null)
		{
			// cache keys of data
			var sort = Sorts<Expression>.Ascending("Title");
			var dataCacheKeys = Extensions.GetRelatedCacheKeys(Filters<Expression>.And(), sort)
				.Concat(Extensions.GetRelatedCacheKeys(expression.SystemID.GetExpressionsFilter(null), sort))
				.Concat(Extensions.GetRelatedCacheKeys(expression.SystemID.GetExpressionsFilter(expression.RepositoryID), sort))
				.Concat(Extensions.GetRelatedCacheKeys(expression.SystemID.GetExpressionsFilter(expression.RepositoryID, expression.RepositoryEntityID, expression.ContentTypeDefinitionID), sort))
				.Concat(Extensions.GetRelatedCacheKeys(expression.SystemID.GetExpressionsFilter(expression.RepositoryID, expression.RepositoryEntityID, null), sort))
				.Concat(Extensions.GetRelatedCacheKeys(expression.SystemID.GetExpressionsFilter(expression.RepositoryID, null, expression.ContentTypeDefinitionID), sort))
				.Concat(await Utility.Cache.GetSetMembersAsync(expression.GetSetCacheKey(), cancellationToken).ConfigureAwait(false))
				.ToList();

			if (!string.IsNullOrWhiteSpace(expression.RepositoryEntityID) && !string.IsNullOrWhiteSpace(expression.ContentTypeDefinitionID))
			{
				var filter = Filters<Expression>.Or
				(
					Filters<Expression>.Equals("ContentTypeDefinitionID", expression.ContentTypeDefinitionID),
					Filters<Expression>.Equals("RepositoryEntityID", expression.RepositoryEntityID)
				);
				dataCacheKeys = Extensions.GetRelatedCacheKeys(Filters<Expression>.And(Filters<Expression>.Equals("RepositoryID", expression.RepositoryID), filter), sort)
					.Concat(Extensions.GetRelatedCacheKeys(filter, sort))
					.Concat(dataCacheKeys)
					.ToList();
			}
			dataCacheKeys = dataCacheKeys.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

			// cache keys of desktop HTMLs
			var htmlCacheKeys = new List<string>();
			var portletsSort = Sorts<Portlet>.Ascending("DesktopID").ThenByAscending("Zone").ThenByAscending("OrderIndex");
			var portlets = await Portlet.FindAsync(Filters<Portlet>.And(Filters<Portlet>.IsNull("OriginalPortletID"), Filters<Portlet>.Equals("ExpressionID", expression.ID)), portletsSort, 0, 1, null, cancellationToken).ConfigureAwait(false);
			await portlets.ForEachAsync(async (portlet, _) =>
			{
				var desktop = await portlet.DesktopID.GetDesktopByIDAsync(cancellationToken).ConfigureAwait(false);
				if (desktop != null)
					htmlCacheKeys = (await Utility.Cache.GetSetMembersAsync(desktop.GetSetCacheKey(), cancellationToken).ConfigureAwait(false)).Concat(htmlCacheKeys).ToList();

				var theportlets = await Portlet.FindAsync(Filters<Portlet>.Equals("OriginalPortletID", portlet.ID), portletsSort, 0, 1, null, cancellationToken).ConfigureAwait(false);
				await theportlets.ForEachAsync(async (theportlet, __) =>
				{
					desktop = theportlet.Desktop;
					if (desktop != null)
						htmlCacheKeys = (await Utility.Cache.GetSetMembersAsync(desktop.GetSetCacheKey(), cancellationToken).ConfigureAwait(false)).Concat(htmlCacheKeys).ToList();
				}, cancellationToken, true, false).ConfigureAwait(false);
			}, cancellationToken, true, false).ConfigureAwait(false);
			htmlCacheKeys = htmlCacheKeys.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

			if (Utility.Logger.IsEnabled(LogLevel.Debug))
				await Utility.WriteLogAsync(correlationID, $"Clear related cache of expression [{expression.ID} => {expression.Title}]\r\n- {dataCacheKeys.Count} data keys => {dataCacheKeys.Join(", ")}\r\n- {htmlCacheKeys.Count} html keys => {htmlCacheKeys.Join(", ")}", CancellationToken.None, "Caches").ConfigureAwait(false);
			await Utility.Cache.RemoveAsync(htmlCacheKeys.Concat(dataCacheKeys).Distinct(StringComparer.OrdinalIgnoreCase).ToList(), cancellationToken).ConfigureAwait(false);
		}

		internal static Task ClearRelatedCacheAsync(this Expression expression, string correlationID = null)
			=> expression.ClearRelatedCacheAsync(CancellationToken.None, correlationID);

		internal static async Task<JObject> SearchExpressionsAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, CancellationToken cancellationToken = default)
		{
			// prepare
			var request = requestInfo.GetRequestExpando();

			var query = request.Get<string>("FilterBy.Query");
			var filter = request.Get<ExpandoObject>("FilterBy")?.ToFilterBy<Expression>() ?? Filters<Expression>.And();
			var sort = string.IsNullOrWhiteSpace(query) ? request.Get<ExpandoObject>("SortBy")?.ToSortBy<Expression>() ?? Sorts<Expression>.Ascending("Title") : null;

			var pagination = request.Get<ExpandoObject>("Pagination")?.GetPagination() ?? new Tuple<long, int, int, int>(-1, 0, 20, 1);
			var pageSize = pagination.Item3;
			var pageNumber = pagination.Item4;

			// check permission
			var gotRights = isSystemAdministrator;
			if (!gotRights)
			{
				// get organization
				var organizationID = filter.GetValue("SystemID") ?? requestInfo.GetParameter("x-system-id") ?? requestInfo.GetParameter("SystemID");
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
					? await Expression.CountAsync(filter, Extensions.GetCacheKeyOfTotalObjects(filter, sort), cancellationToken).ConfigureAwait(false)
					: await Expression.CountAsync(query, filter, cancellationToken).ConfigureAwait(false);

			var totalPages = new Tuple<long, int>(totalRecords, pageSize).GetTotalPages();
			if (totalPages > 0 && pageNumber > totalPages)
				pageNumber = totalPages;

			// search
			var objects = totalRecords > 0
				? string.IsNullOrWhiteSpace(query)
					? await Expression.FindAsync(filter, sort, pageSize, pageNumber, Extensions.GetCacheKey(filter, sort, pageSize, pageNumber), cancellationToken).ConfigureAwait(false)
					: await Expression.SearchAsync(query, filter, pageSize, pageNumber, cancellationToken).ConfigureAwait(false)
				: new List<Expression>();

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
				await Utility.Cache.SetAsync(Extensions.GetCacheKeyOfObjectsJson(filter, sort, pageSize, pageNumber), response.ToString(Formatting.None), cancellationToken).ConfigureAwait(false);

			// response
			return response;
		}

		internal static async Task<JObject> CreateExpressionAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, CancellationToken cancellationToken = default)
		{
			// prepare
			var request = requestInfo.GetBodyJson();
			var organizationID = request.Get<string>("SystemID") ?? requestInfo.GetParameter("x-system-id") ?? requestInfo.GetParameter("SystemID");
			var organization = await (organizationID ?? "").GetOrganizationByIDAsync(cancellationToken).ConfigureAwait(false);
			if (organization == null)
				throw new InformationInvalidException("The organization is invalid");

			// check permission
			var gotRights = isSystemAdministrator || requestInfo.Session.User.ID.IsEquals(organization.OwnerID) || requestInfo.Session.User.IsModerator(organization.WorkingPrivileges);
			if (!gotRights)
				throw new AccessDeniedException();

			// create new
			var expression = request.ToExpandoObject().CreateExpressionInstance("SystemID,Privileges,Filter,Sorts,FilterBy,SortBy,Created,CreatedID,LastModified,LastModifiedID", obj =>
			{
				obj.ID = string.IsNullOrWhiteSpace(obj.ID) || !obj.ID.IsValidUUID() ? UtilityService.NewUUID : obj.ID;
				obj.SystemID = organization.ID;
				obj.Created = obj.LastModified = DateTime.Now;
				obj.CreatedID = obj.LastModifiedID = requestInfo.Session.User.ID;
				obj.Normalize(request.Get<JObject>("Filter"), request.Get<JArray>("Sorts"));
			});

			await Expression.CreateAsync(expression, cancellationToken).ConfigureAwait(false);
			expression.Set().ClearRelatedCacheAsync(requestInfo.CorrelationID).Run();

			// send update messages
			var json = expression.ToJson();
			var objectName = expression.GetTypeName(true);
			await Task.WhenAll(
				Utility.RTUService.SendUpdateMessageAsync(new UpdateMessage
				{
					Type = $"{requestInfo.ServiceName}#{objectName}#Create",
					Data = json,
					DeviceID = "*"
				}, cancellationToken),
				Utility.RTUService.SendInterCommunicateMessageAsync(new CommunicateMessage(requestInfo.ServiceName)
				{
					Type = $"{objectName}#Create",
					Data = json,
					ExcludedNodeID = Utility.NodeID
				}, cancellationToken)
			).ConfigureAwait(false);

			// send notification
			expression.SendNotificationAsync("Create", organization.Notifications, ApprovalStatus.Draft, ApprovalStatus.Published, requestInfo, cancellationToken).Run();

			// response
			return expression.ToJson();
		}

		internal static async Task<JObject> GetExpressionAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, CancellationToken cancellationToken = default)
		{
			// prepare
			var expression = await (requestInfo.GetObjectIdentity() ?? "").GetExpressionByIDAsync(cancellationToken).ConfigureAwait(false);
			if (expression == null)
				throw new InformationNotFoundException();
			else if (expression.Organization == null)
				throw new InformationInvalidException("The organization is invalid");

			// check permission
			var gotRights = isSystemAdministrator || requestInfo.Session.User.ID.IsEquals(expression.Organization.OwnerID) || requestInfo.Session.User.IsViewer(expression.Organization.WorkingPrivileges);
			if (!gotRights)
				throw new AccessDeniedException();

			// send the update message to update to all other connected clients
			var response = expression.ToJson();
			await Utility.RTUService.SendUpdateMessageAsync(new UpdateMessage
			{
				Type = $"{requestInfo.ServiceName}#{expression.GetTypeName(true)}#Update",
				Data = response,
				DeviceID = "*",
				ExcludedDeviceID = requestInfo.Session.DeviceID
			}, cancellationToken).ConfigureAwait(false);

			// response
			return response;
		}

		internal static async Task<JObject> UpdateExpressionAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, CancellationToken cancellationToken = default)
		{
			// prepare
			var expression = await (requestInfo.GetObjectIdentity() ?? "").GetExpressionByIDAsync(cancellationToken).ConfigureAwait(false);
			if (expression == null)
				throw new InformationNotFoundException();
			else if (expression.Organization == null)
				throw new InformationInvalidException("The organization is invalid");

			// check permission
			var gotRights = isSystemAdministrator || requestInfo.Session.User.ID.IsEquals(expression.Organization.OwnerID) || requestInfo.Session.User.IsModerator(expression.Organization.WorkingPrivileges);
			if (!gotRights)
				throw new AccessDeniedException();

			// update
			var request = requestInfo.GetBodyJson();
			expression.UpdateExpressionInstance(request.ToExpandoObject(), "ID,SystemID,RepositoryID,RepositoryEntityID,ContentTypeDefinitionID,Privileges,Filter,Sorts,FilterBy,SortBy,Created,CreatedID,LastModified,LastModifiedID", obj =>
			{
				obj.LastModified = DateTime.Now;
				obj.LastModifiedID = requestInfo.Session.User.ID;
				obj.Normalize(request.Get<JObject>("Filter"), request.Get<JArray>("Sorts"));
			});

			await Expression.UpdateAsync(expression, requestInfo.Session.User.ID, cancellationToken).ConfigureAwait(false);
			expression.Set().ClearRelatedCacheAsync(requestInfo.CorrelationID).Run();

			// send update messages
			var response = expression.ToJson();
			var objectName = expression.GetTypeName(true);
			await Task.WhenAll(
				Utility.RTUService.SendUpdateMessageAsync(new UpdateMessage
				{
					Type = $"{requestInfo.ServiceName}#{objectName}#Update",
					Data = response,
					DeviceID = "*"
				}, cancellationToken),
				Utility.RTUService.SendInterCommunicateMessageAsync(new CommunicateMessage(requestInfo.ServiceName)
				{
					Type = $"{objectName}#Update",
					Data = response,
					ExcludedNodeID = Utility.NodeID
				}, cancellationToken)
			).ConfigureAwait(false);

			// send notification
			expression.SendNotificationAsync("Update", expression.Organization.Notifications, ApprovalStatus.Published, ApprovalStatus.Published, requestInfo, cancellationToken).Run();

			// response
			return response;
		}

		internal static async Task<JObject> DeleteExpressionAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, CancellationToken cancellationToken = default)
		{
			// prepare
			var expression = await (requestInfo.GetObjectIdentity() ?? "").GetExpressionByIDAsync(cancellationToken).ConfigureAwait(false);
			if (expression == null)
				throw new InformationNotFoundException();
			else if (expression.Organization == null)
				throw new InformationInvalidException("The organization is invalid");

			// check permission
			var gotRights = isSystemAdministrator || requestInfo.Session.User.ID.IsEquals(expression.Organization.OwnerID) || requestInfo.Session.User.IsModerator(expression.Organization.WorkingPrivileges);
			if (!gotRights)
				throw new AccessDeniedException();

			// delete
			await Expression.DeleteAsync<Expression>(expression.ID, requestInfo.Session.User.ID, cancellationToken).ConfigureAwait(false);
			expression.Remove().ClearRelatedCacheAsync(requestInfo.CorrelationID).Run();

			// send update messages
			var response = expression.ToJson();
			var objectName = expression.GetTypeName(true);
			await Task.WhenAll(
				Utility.RTUService.SendUpdateMessageAsync(new UpdateMessage
				{
					Type = $"{requestInfo.ServiceName}#{objectName}#Delete",
					Data = response,
					DeviceID = "*"
				}, cancellationToken),
				Utility.RTUService.SendInterCommunicateMessageAsync(new CommunicateMessage(requestInfo.ServiceName)
				{
					Type = $"{objectName}#Delete",
					Data = response,
					ExcludedNodeID = Utility.NodeID
				}, cancellationToken)
			).ConfigureAwait(false);

			// send notification
			expression.SendNotificationAsync("Delete", expression.Organization.Notifications, ApprovalStatus.Published, ApprovalStatus.Published, requestInfo, cancellationToken).Run();

			// response
			return response;
		}

		internal static async Task<JObject> SyncExpressionAsync(this RequestInfo requestInfo, CancellationToken cancellationToken = default)
		{
			var data = requestInfo.GetBodyExpando();
			var expression = await data.Get<string>("ID").GetExpressionByIDAsync(cancellationToken).ConfigureAwait(false);
			if (expression == null)
			{
				expression = Expression.CreateInstance(data);
				await Expression.CreateAsync(expression, cancellationToken).ConfigureAwait(false);
			}
			else
			{
				expression.Fill(data);
				await Expression.UpdateAsync(expression, true, cancellationToken).ConfigureAwait(false);
			}

			// clear related cache
			expression.ClearRelatedCacheAsync(requestInfo.CorrelationID).Run();

			// send update messages
			var json = expression.Set().ToJson();
			var objectName = expression.GetTypeName(true);
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
				{ "ID", expression.ID },
				{ "Type", objectName }
			};
		}
	}
}
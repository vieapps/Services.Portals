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
	public static class ExpressionProcessor
	{
		internal static ConcurrentDictionary<string, Expression> Expressions { get; } = new ConcurrentDictionary<string, Expression>(StringComparer.OrdinalIgnoreCase);

		public static Expression CreateExpression(this ExpandoObject data, string excluded = null, Action<Expression> onCompleted = null)
			=> Expression.CreateInstance(data, excluded?.ToHashSet(), onCompleted);

		public static Expression Update(this Expression expression, ExpandoObject requestBody, string excluded = null, Action<Expression> onCompleted = null)
			=> expression.Fill(requestBody, excluded?.ToHashSet(), onCompleted);

		internal static Expression Set(this Expression expression, bool updateCache = false)
		{
			if (expression != null && !string.IsNullOrWhiteSpace(expression.ID) && !string.IsNullOrWhiteSpace(expression.Title))
			{
				ExpressionProcessor.Expressions[expression.ID] = expression;
				if (updateCache)
					Utility.Cache.Set(expression);
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
			var filter = systemID.GetExpressionsFilter(repositoryID, repositoryEntityID, contentTypeDefinitionID);
			var sort = Sorts<Expression>.Ascending("Title");
			var expressions = await Expression.FindAsync(filter, sort, 0, 1, Extensions.GetCacheKey(filter, sort, 0, 1), cancellationToken).ConfigureAwait(false);
			await expressions.ForEachAsync(async expression => await expression.SetAsync(updateCache, cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
			return expressions;
		}

		internal static Task ProcessInterCommunicateMessageOfExpressionAsync(this CommunicateMessage message, CancellationToken cancellationToken = default)
		{
			if (message.Type.IsEndsWith("#Create"))
				message.Data.ToExpandoObject().CreateExpression().Set();

			else if (message.Type.IsEndsWith("#Update"))
			{
				var expression = message.Data.Get("ID", "").GetExpressionByID(false, false);
				expression = expression == null
					? message.Data.ToExpandoObject().CreateExpression()
					: expression.Update(message.Data.ToExpandoObject());
				expression.Set();
			}

			else if (message.Type.IsEndsWith("#Delete"))
				message.Data.ToExpandoObject().CreateExpression().Remove();

			return Task.CompletedTask;
		}

		internal static async Task ClearRelatedCacheAsync(this Expression expression, CancellationToken cancellationToken, string correlationID = null, bool clearDataCache = true, bool clearHtmlCache = true, bool doRefresh = false)
		{
			// data cache keys
			var sort = Sorts<Expression>.Ascending("Title");
			var dataCacheKeys = clearDataCache
				? Extensions.GetRelatedCacheKeys(Filters<Expression>.And(), sort)
					.Concat(Extensions.GetRelatedCacheKeys(expression.SystemID.GetExpressionsFilter(null), sort))
					.Concat(Extensions.GetRelatedCacheKeys(expression.SystemID.GetExpressionsFilter(expression.RepositoryID), sort))
					.Concat(Extensions.GetRelatedCacheKeys(expression.SystemID.GetExpressionsFilter(expression.RepositoryID, expression.RepositoryEntityID, expression.ContentTypeDefinitionID), sort))
					.Concat(Extensions.GetRelatedCacheKeys(expression.SystemID.GetExpressionsFilter(expression.RepositoryID, expression.RepositoryEntityID, null), sort))
					.Concat(Extensions.GetRelatedCacheKeys(expression.SystemID.GetExpressionsFilter(expression.RepositoryID, null, expression.ContentTypeDefinitionID), sort))
					.ToList()
				: new List<string>();

			if (clearDataCache)
			{
				if (!string.IsNullOrWhiteSpace(expression.RepositoryEntityID) && !string.IsNullOrWhiteSpace(expression.ContentTypeDefinitionID))
				{
					var filter = Filters<Expression>.Or
					(
						Filters<Expression>.Equals("ContentTypeDefinitionID", expression.ContentTypeDefinitionID),
						Filters<Expression>.Equals("RepositoryEntityID", expression.RepositoryEntityID)
					);
					dataCacheKeys = dataCacheKeys.Concat(Extensions.GetRelatedCacheKeys(filter, sort))
						.Concat(Extensions.GetRelatedCacheKeys(Filters<Expression>.And(Filters<Expression>.Equals("RepositoryID", expression.RepositoryID), filter), sort))
						.ToList();
				}
				if (expression.ContentType != null)
					dataCacheKeys = dataCacheKeys.Concat(await Utility.Cache.GetSetMembersAsync(expression.ContentType.GetSetCacheKey(), cancellationToken).ConfigureAwait(false)).ToList();
			}
			dataCacheKeys = dataCacheKeys.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

			// html cache keys (desktop HTMLs)
			var htmlCacheKeys = clearHtmlCache ? await expression.GetSetCacheKeysAsync(cancellationToken).ConfigureAwait(false) : new List<string>();

			// clear related cache
			await Utility.Cache.RemoveAsync(htmlCacheKeys.Concat(dataCacheKeys).Distinct(StringComparer.OrdinalIgnoreCase).ToList(), cancellationToken).ConfigureAwait(false);
			await Task.WhenAll
			(
				Utility.IsCacheLogEnabled ? Utility.WriteLogAsync(correlationID, $"Clear related cache of an expression [{expression.Title} - ID: {expression.ID}]\r\n- {dataCacheKeys.Count} data keys => {dataCacheKeys.Join(", ")}\r\n- {htmlCacheKeys.Count} html keys => {htmlCacheKeys.Join(", ")}", "Caches") : Task.CompletedTask,
				doRefresh ? $"{Utility.PortalsHttpURI}/~{expression.Organization.Alias}/".RefreshWebPageAsync(1, correlationID, $"Refresh desktop when related cache of an expression was clean [{expression.Title} - ID: {expression.ID}]") : Task.CompletedTask
			).ConfigureAwait(false);
		}

		internal static Task ClearCacheAsync(this Expression expression, CancellationToken cancellationToken, string correlationID = null, bool clearRelatedDataCache = true, bool clearRelatedHtmlCache = true, bool doRefresh = true)
			=> Task.WhenAll(new[]
			{
				expression.ClearRelatedCacheAsync(cancellationToken, correlationID, clearRelatedDataCache, clearRelatedHtmlCache, doRefresh),
				Utility.Cache.RemoveAsync(expression.Remove(), cancellationToken),
				new CommunicateMessage(ServiceBase.ServiceComponent.ServiceName)
				{
					Type = $"{expression.GetObjectName()}#Delete",
					Data = expression.ToJson(),
					ExcludedNodeID = Utility.NodeID
				}.SendAsync(),
				Utility.IsCacheLogEnabled ? Utility.WriteLogAsync(correlationID, $"Clear cache of an expression [{expression.Title} - ID: {expression.ID}]", "Caches") : Task.CompletedTask
			});

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
				var organizationID = filter.GetValue("SystemID") ?? requestInfo.GetParameter("SystemID") ?? requestInfo.GetParameter("x-system-id") ?? requestInfo.GetParameter("OrganizationID");
				var organization = await (organizationID ?? "").GetOrganizationByIDAsync(cancellationToken).ConfigureAwait(false);
				if (organization == null)
					throw new InformationExistedException("The organization is invalid");

				gotRights = requestInfo.Session.User.IsViewer(null, null, organization);
				if (!gotRights)
					throw new AccessDeniedException();
			}

			// process cache
			var json = string.IsNullOrWhiteSpace(query)
				? await Utility.Cache.GetAsync<string>(Extensions.GetCacheKeyOfObjectsJson(filter, sort, pageSize, pageNumber), cancellationToken).ConfigureAwait(false)
				: null;
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
					: await Expression.SearchAsync(query, filter, null, pageSize, pageNumber, cancellationToken).ConfigureAwait(false)
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
			var gotRights = isSystemAdministrator || requestInfo.Session.User.IsModerator(null, null, organization);
			if (!gotRights)
				throw new AccessDeniedException();

			// create new
			var expression = request.ToExpandoObject().CreateExpression("SystemID,Privileges,Filter,Sorts,FilterBy,SortBy,Created,CreatedID,LastModified,LastModifiedID", obj =>
			{
				obj.ID = string.IsNullOrWhiteSpace(obj.ID) || !obj.ID.IsValidUUID() ? UtilityService.NewUUID : obj.ID;
				obj.SystemID = organization.ID;
				obj.Created = obj.LastModified = DateTime.Now;
				obj.CreatedID = obj.LastModifiedID = requestInfo.Session.User.ID;
				obj.Normalize(request.Get<JObject>("Filter"), request.Get<JArray>("Sorts"));
			});

			await Expression.CreateAsync(expression, cancellationToken).ConfigureAwait(false);
			await expression.Set().ClearRelatedCacheAsync(cancellationToken, requestInfo.CorrelationID).ConfigureAwait(false);

			// send update messages
			var response = expression.ToJson();
			var objectName = expression.GetTypeName(true);
			new UpdateMessage
			{
				Type = $"{requestInfo.ServiceName}#{objectName}#Create",
				Data = response,
				DeviceID = "*"
			}.Send();
			new CommunicateMessage(requestInfo.ServiceName)
			{
				Type = $"{objectName}#Create",
				Data = response,
				ExcludedNodeID = Utility.NodeID
			}.Send();

			// send notification
			await expression.SendNotificationAsync("Create", organization.Notifications, ApprovalStatus.Draft, ApprovalStatus.Published, requestInfo, cancellationToken).ConfigureAwait(false);

			// response
			return response;
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
			var gotRights = isSystemAdministrator || requestInfo.Session.User.IsViewer(null, null, expression.Organization);
			if (!gotRights)
				throw new AccessDeniedException();

			// send the update message to update to all other connected clients
			var response = expression.ToJson();
			new UpdateMessage
			{
				Type = $"{requestInfo.ServiceName}#{expression.GetTypeName(true)}#Update",
				Data = response,
				DeviceID = "*",
				ExcludedDeviceID = requestInfo.Session.DeviceID
			}.Send();

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
			var gotRights = isSystemAdministrator || requestInfo.Session.User.IsModerator(null, null, expression.Organization);
			if (!gotRights)
				throw new AccessDeniedException();

			// update
			var request = requestInfo.GetBodyJson();
			expression.Update(request.ToExpandoObject(), "ID,SystemID,RepositoryID,RepositoryEntityID,ContentTypeDefinitionID,Privileges,Filter,Sorts,FilterBy,SortBy,Created,CreatedID,LastModified,LastModifiedID", obj =>
			{
				obj.LastModified = DateTime.Now;
				obj.LastModifiedID = requestInfo.Session.User.ID;
				obj.Normalize(request.Get<JObject>("Filter"), request.Get<JArray>("Sorts"));
			});

			await Expression.UpdateAsync(expression, requestInfo.Session.User.ID, cancellationToken).ConfigureAwait(false);
			await expression.Set().ClearRelatedCacheAsync(cancellationToken, requestInfo.CorrelationID).ConfigureAwait(false);

			// send update messages
			var response = expression.ToJson();
			var objectName = expression.GetTypeName(true);
			new UpdateMessage
			{
				Type = $"{requestInfo.ServiceName}#{objectName}#Update",
				Data = response,
				DeviceID = "*"
			}.Send();
			new CommunicateMessage(requestInfo.ServiceName)
			{
				Type = $"{objectName}#Update",
				Data = response,
				ExcludedNodeID = Utility.NodeID
			}.Send();

			// send notification
			await expression.SendNotificationAsync("Update", expression.Organization.Notifications, ApprovalStatus.Published, ApprovalStatus.Published, requestInfo, cancellationToken).ConfigureAwait(false);

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
			var gotRights = isSystemAdministrator || requestInfo.Session.User.IsAdministrator(null, null, expression.Organization);
			if (!gotRights)
				throw new AccessDeniedException();

			// delete
			await Expression.DeleteAsync<Expression>(expression.ID, requestInfo.Session.User.ID, cancellationToken).ConfigureAwait(false);
			await expression.ClearCacheAsync(cancellationToken, requestInfo.CorrelationID, true, true, false).ConfigureAwait(false);

			// send update messages
			var response = expression.ToJson();
			var objectName = expression.GetTypeName(true);
			new UpdateMessage
			{
				Type = $"{requestInfo.ServiceName}#{objectName}#Delete",
				Data = response,
				DeviceID = "*"
			}.Send();
			new CommunicateMessage(requestInfo.ServiceName)
			{
				Type = $"{objectName}#Delete",
				Data = response,
				ExcludedNodeID = Utility.NodeID
			}.Send();

			// send notification
			await expression.SendNotificationAsync("Delete", expression.Organization.Notifications, ApprovalStatus.Published, ApprovalStatus.Published, requestInfo, cancellationToken).ConfigureAwait(false);

			// response
			return response;
		}

		internal static async Task<JObject> SyncExpressionAsync(this RequestInfo requestInfo, CancellationToken cancellationToken, bool sendNotifications = false)
		{
			var @event = requestInfo.GetHeaderParameter("Event");
			if (string.IsNullOrWhiteSpace(@event) || !@event.IsEquals("Delete"))
				@event = "Update";

			var data = requestInfo.GetBodyExpando();
			var expression = await data.Get<string>("ID").GetExpressionByIDAsync(cancellationToken).ConfigureAwait(false);

			if (!@event.IsEquals("Delete"))
			{
				if (expression == null)
				{
					expression = Expression.CreateInstance(data);
					await Expression.CreateAsync(expression, cancellationToken).ConfigureAwait(false);
				}
				else
					await Expression.UpdateAsync(expression.Update(data), true, cancellationToken).ConfigureAwait(false);
			}
			else if (expression != null)
				await Expression.DeleteAsync<Expression>(expression.ID, expression.LastModifiedID, cancellationToken).ConfigureAwait(false);

			// clear related cache
			if (requestInfo.GetHeaderParameter("x-converter") == null || @event.IsEquals("Delete"))
				await expression.ClearCacheAsync(cancellationToken, requestInfo.CorrelationID).ConfigureAwait(false);
			else
				await expression.ClearRelatedCacheAsync(cancellationToken, requestInfo.CorrelationID).ConfigureAwait(false);

			// send notifications
			if (sendNotifications)
				await expression.SendNotificationAsync(@event, expression.Organization.Notifications, ApprovalStatus.Published, ApprovalStatus.Published, requestInfo, cancellationToken).ConfigureAwait(false);

			// send update messages
			var json = @event.IsEquals("Delete")
				? expression.Remove().ToJson()
				: expression.Set().ToJson();
			var objectName = expression.GetTypeName(true);
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
				{ "ID", expression.ID },
				{ "Type", objectName }
			};
		}
	}
}
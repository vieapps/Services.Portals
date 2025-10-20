#region Related components
using net.vieapps.Components.Repository;
using net.vieapps.Components.Security;
using net.vieapps.Components.Utility;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Dynamic;
using System.Linq;
using System.Reactive.Concurrency;
using System.Threading;
using System.Threading.Tasks;

#endregion

namespace net.vieapps.Services.Portals
{
	public static class SchedulingTaskProcessor
	{
		internal static ConcurrentDictionary<string, SchedulingTask> SchedulingTasks { get; } = new ConcurrentDictionary<string, SchedulingTask>(StringComparer.OrdinalIgnoreCase);

		public static SchedulingTask CreateSchedulingTask(this ExpandoObject data, string excluded = null, Action<SchedulingTask> onCompleted = null)
			=> SchedulingTask.CreateInstance(data, excluded?.ToHashSet(), onCompleted);

		public static SchedulingTask Update(this SchedulingTask schedulingTask, ExpandoObject data, string excluded = null, Action<SchedulingTask> onCompleted = null)
			=> schedulingTask.Fill(data, excluded?.ToHashSet(), onCompleted);

		internal static SchedulingTask Set(this SchedulingTask schedulingTask, bool updateCache = false)
		{
			if (schedulingTask != null && !string.IsNullOrWhiteSpace(schedulingTask.ID) && !string.IsNullOrWhiteSpace(schedulingTask.Title))
			{
				SchedulingTaskProcessor.SchedulingTasks[schedulingTask.ID] = schedulingTask;
				if (updateCache)
					Utility.Cache.SetAsync(schedulingTask).Run();
			}
			return schedulingTask;
		}

		internal static async Task<SchedulingTask> SetAsync(this SchedulingTask schedulingTask, bool updateCache = false, CancellationToken cancellationToken = default)
		{
			schedulingTask?.Set();
			await (updateCache && schedulingTask != null && !string.IsNullOrWhiteSpace(schedulingTask.ID) && !string.IsNullOrWhiteSpace(schedulingTask.Title) ? Utility.Cache.SetAsync(schedulingTask, cancellationToken) : Task.CompletedTask).ConfigureAwait(false);
			return schedulingTask;
		}

		internal static SchedulingTask Remove(this SchedulingTask schedulingTask)
			=> (schedulingTask?.ID ?? "").RemoveSchedulingTask();

		internal static SchedulingTask RemoveSchedulingTask(this string id)
			=> !string.IsNullOrWhiteSpace(id) && SchedulingTaskProcessor.SchedulingTasks.TryRemove(id, out var schedulingTask) && schedulingTask != null
				? schedulingTask
				: null;

		internal static async Task<SchedulingTask> GetSchedulingTaskByIDAsync(this string id, CancellationToken cancellationToken = default, bool force = false, bool fetchRepository = true)
			=> !force && !string.IsNullOrWhiteSpace(id) && SchedulingTaskProcessor.SchedulingTasks.ContainsKey(id)
				? SchedulingTaskProcessor.SchedulingTasks[id]
				: fetchRepository && !string.IsNullOrWhiteSpace(id)
					? (await SchedulingTask.GetAsync<SchedulingTask>(id, cancellationToken).ConfigureAwait(false))?.Set()
					: null;

		internal static SchedulingTask Normalize(this SchedulingTask schedulingTask, ExpandoObject data, Action<SchedulingTask> onCompleted = null)
		{
			schedulingTask.Persistance = data.Has("Persistance") && data.Get<bool>("Persistance");
			onCompleted?.Invoke(schedulingTask);
			return schedulingTask;
		}

		internal static JToken SendMessage(this SchedulingTask schedulingTask, string deviceID = null, string excludedDeviceID = null, string @event = null, JToken data = null)
		{
			data = data ?? schedulingTask.ToJson();
			new UpdateMessage
			{
				Type = $"{Utility.ServiceName}#{schedulingTask.GetObjectName()}#{@event ?? "Update"}",
				Data = data,
				DeviceID = deviceID ?? "*",
				ExcludedDeviceID = excludedDeviceID ?? ""
			}.Send();
			return data;
		}

		internal static void SendMessages(this SchedulingTask schedulingTask, string @event = null, JToken data = null, string excludedNodeID = null, string deviceID = null, string excludedDeviceID = null)
			=> new CommunicateMessage(Utility.ServiceName)
			{
				Type = $"{schedulingTask.GetObjectName()}#{@event ?? "Update"}",
				Data = schedulingTask.SendMessage(deviceID, excludedDeviceID, @event, data ?? schedulingTask.ToJson(json => json.Remove("Privileges"))),
				ExcludedNodeID = excludedNodeID
			}.Send();

		internal static async Task ProcessInterCommunicateMessageOfSchedulingTaskAsync(this CommunicateMessage message, CancellationToken cancellationToken = default)
		{
			var data = message.Data.ToExpandoObject();
			var schedulingTask = data.CreateSchedulingTask(null, task => task.Normalize(data));

			if (message.Type.IsEndsWith("#Create"))
				await schedulingTask.SetAsync(false, cancellationToken).ConfigureAwait(false);

			else if (message.Type.IsEndsWith("#Update"))
			{
				if (!string.IsNullOrWhiteSpace(schedulingTask?.ID) && schedulingTask.Persistance)
					schedulingTask = await schedulingTask.ID.GetSchedulingTaskByIDAsync(cancellationToken, false, false).ConfigureAwait(false);
				schedulingTask = schedulingTask == null
					? data.CreateSchedulingTask(null, task => task.Normalize(data))
					: schedulingTask.Update(data, null, task => task.Normalize(data));
				await schedulingTask.SetAsync(false, cancellationToken).ConfigureAwait(false);
			}

			else if (message.Type.IsEndsWith("#Delete"))
				schedulingTask.Remove();
		}

		internal static Task ClearRelatedCacheAsync(this SchedulingTask schedulingTask, CancellationToken cancellationToken = default)
			=> Utility.Cache.RemoveAsync(Extensions.GetRelatedCacheKeys(Filters<SchedulingTask>.And(Filters<SchedulingTask>.Equals("SystemID", schedulingTask.SystemID)), Sorts<SchedulingTask>.Ascending("Time")), cancellationToken);

		internal static async Task<(long TotalRecords, List<SchedulingTask> Objects, List<string> CacheKeys)> SearchAsync(string query, IFilterBy<SchedulingTask> filter, SortBy<SchedulingTask> sort, int pageSize, int pageNumber, long totalRecords = -1, CancellationToken cancellationToken = default)
		{
			// cache keys
			var cacheKeyOfObjects = string.IsNullOrWhiteSpace(query) && pageSize > 0 ? Extensions.GetCacheKey(filter, sort, pageSize, pageNumber) : null;
			var cacheKeyOfTotalObjects = string.IsNullOrWhiteSpace(query) && pageSize > 0 ? Extensions.GetCacheKeyOfTotalObjects(filter, sort) : null;
			var cacheKeys = string.IsNullOrWhiteSpace(query) && pageSize > 0 ? new List<string> { cacheKeyOfObjects, cacheKeyOfTotalObjects } : new List<string>();

			// count
			totalRecords = totalRecords > -1
				? totalRecords
				: string.IsNullOrWhiteSpace(query)
					? await SchedulingTask.CountAsync(filter, cacheKeyOfTotalObjects, cancellationToken).ConfigureAwait(false)
					: await SchedulingTask.CountAsync(query, filter, cancellationToken).ConfigureAwait(false);

			// search objects
			var objects = totalRecords > 0
				? string.IsNullOrWhiteSpace(query)
					? await SchedulingTask.FindAsync(filter, sort, pageSize, pageNumber, cacheKeyOfObjects, cancellationToken).ConfigureAwait(false)
					: await SchedulingTask.SearchAsync(query, filter, null, pageSize, pageNumber, cancellationToken).ConfigureAwait(false)
				: new List<SchedulingTask>();

			// page size to clear related cached
			if (string.IsNullOrWhiteSpace(query) && pageSize > 0)
				await Utility.SetCacheOfPageSizeAsync(filter, sort, pageSize, cancellationToken).ConfigureAwait(false);

			// return the results
			return (totalRecords, objects, cacheKeys);
		}

		internal static async Task<List<SchedulingTask>> SearchAsync(IFilterBy<SchedulingTask> filter, CancellationToken cancellationToken)
			=> (await SchedulingTaskProcessor.SearchAsync(null, filter, Sorts<SchedulingTask>.Ascending("Time"), 0, 1, -1, cancellationToken).ConfigureAwait(false)).Objects;

		internal static async Task<JObject> SearchSchedulingTasksAsync(this RequestInfo requestInfo, bool isSystemAdministrator, CancellationToken cancellationToken)
		{
			// prepare
			var request = requestInfo.GetRequestExpando();

			var query = request.Get<string>("FilterBy.Query");
			var filter = request.Get<ExpandoObject>("FilterBy")?.ToFilterBy<SchedulingTask>() ?? Filters<SchedulingTask>.And();
			var sort = string.IsNullOrWhiteSpace(query) ? request.Get<ExpandoObject>("SortBy")?.ToSortBy<SchedulingTask>() ?? Sorts<SchedulingTask>.Ascending("Time") : null;

			var (totalOfRecords, _, pageSize, pageNumber) = request.Get<ExpandoObject>("Pagination")?.GetPagination() ?? (-1, 0, 20, 1);

			// check permission
			var gotRights = isSystemAdministrator;
			if (!gotRights)
			{
				// get organization
				var organizationID = filter.GetValue("SystemID") ?? requestInfo.GetParameter("SystemID") ?? requestInfo.GetParameter("x-system-id") ?? requestInfo.GetParameter("OrganizationID");
				var organization = await (organizationID ?? "").GetOrganizationByIDAsync(cancellationToken).ConfigureAwait(false);
				if (organization == null)
					throw new InformationInvalidException("The organization is invalid");

				gotRights = "true".IsEquals(requestInfo.GetHeaderParameter("x-init")) || requestInfo.Session.User.IsModerator(null, null, organization);
				if (!gotRights)
					throw new AccessDeniedException();
			}

			// process cache
			var json = string.IsNullOrWhiteSpace(query)
				? await Utility.Cache.GetAsync<string>(Extensions.GetCacheKeyOfObjectsJson(filter, sort, pageSize, pageNumber), cancellationToken).ConfigureAwait(false)
				: null;
			if (!string.IsNullOrWhiteSpace(json))
				return JObject.Parse(json);

			// search if has no cache
			var (totalRecords, objects, _) = await SchedulingTaskProcessor.SearchAsync(query, filter, sort, pageSize, pageNumber, totalOfRecords, cancellationToken).ConfigureAwait(false);
			var totalPages = (totalRecords, pageSize).GetTotalPages();
			if (totalPages > 0 && pageNumber > totalPages)
				pageNumber = totalPages;

			// build result
			var response = new JObject
			{
				{ "FilterBy", filter.ToClientJson(query) },
				{ "SortBy", sort?.ToClientJson() },
				{ "Pagination", (totalRecords, totalPages, pageSize, pageNumber).GetPagination() },
				{ "Objects", objects.ToJsonArray() }
			};

			// update cache
			if (string.IsNullOrWhiteSpace(query))
				Utility.Cache.SetAsync(Extensions.GetCacheKeyOfObjectsJson(filter, sort, pageSize, pageNumber), response.ToString(Formatting.None)).Run();

			// response
			return response;
		}

		internal static async Task<JObject> CreateSchedulingTaskAsync(this RequestInfo requestInfo, bool isSystemAdministrator, CancellationToken cancellationToken)
		{
			// prepare
			var requestBody = requestInfo.GetBodyExpando();
			var organizationID = requestBody.Get<string>("SystemID") ?? requestInfo.GetParameter("x-system-id") ?? requestInfo.GetParameter("SystemID");
			var organization = await (organizationID ?? "").GetOrganizationByIDAsync(cancellationToken).ConfigureAwait(false);
			if (organization == null)
				throw new InformationInvalidException("The organization is invalid");

			// check permission
			var gotRights = isSystemAdministrator || requestInfo.Session.User.IsModerator(null, null, organization);
			if (!gotRights)
				throw new AccessDeniedException();

			// create new schedulingTask
			var schedulingTask = requestBody.CreateSchedulingTask("SystemID,Privileges,Time,Created,CreatedID,LastModified,LastModifiedID", obj =>
			{
				obj.ID = string.IsNullOrWhiteSpace(obj.ID) || !obj.ID.IsValidUUID() ? UtilityService.NewUUID : obj.ID;
				obj.SystemID = organization.ID;
				obj.Created = obj.LastModified = DateTime.Now;
				obj.CreatedID = obj.LastModifiedID = requestInfo.Session.User.ID;
				obj.SetTime(requestBody.Get<DateTime>("Time"));
			});
			await SchedulingTask.CreateAsync(schedulingTask, cancellationToken).ConfigureAwait(false);
			schedulingTask.Set().ClearRelatedCacheAsync(Utility.CancellationToken).Run();

			// send update messages
			var response = schedulingTask.ToJson();
			schedulingTask.SendMessages("Create", response, Utility.NodeID);

			// send notification
			schedulingTask.SendNotification("Create", schedulingTask.Organization.Notifications, ApprovalStatus.Published, ApprovalStatus.Published, requestInfo);

			// response
			return response;
		}

		internal static async Task<JObject> GetSchedulingTaskAsync(this RequestInfo requestInfo, bool isSystemAdministrator, CancellationToken cancellationToken)
		{
			// prepare
			var identity = requestInfo.GetObjectIdentity(true, true) ?? "";
			var schedulingTask = await identity.GetSchedulingTaskByIDAsync(cancellationToken).ConfigureAwait(false);
			if (schedulingTask == null)
				throw new InformationNotFoundException();
			else if (schedulingTask.Organization == null)
				throw new InformationInvalidException("The organization is invalid");

			// check permission
			var gotRights = isSystemAdministrator || requestInfo.Session.User.IsModerator(null, null, schedulingTask.Organization);
			if (!gotRights)
				throw new AccessDeniedException();

			// refresh (clear cached and reload)
			var isRefresh = "refresh".IsEquals(requestInfo.GetObjectIdentity());
			if (isRefresh)
			{
				await Utility.Cache.RemoveAsync(schedulingTask, cancellationToken).ConfigureAwait(false);
				schedulingTask = await schedulingTask.Remove().ID.GetSchedulingTaskByIDAsync(cancellationToken, true).ConfigureAwait(false);
			}

			// send the update message to update to all other connected clients
			var versions = await schedulingTask.FindVersionsAsync(cancellationToken, false).ConfigureAwait(false);
			var response = schedulingTask.ToJson(json => json.UpdateVersions(versions));

			if (isRefresh)
			{
				if (schedulingTask.Time > DateTime.Now && schedulingTask.Status != Status.Awaiting)
				{
					response["Status"] = Status.Awaiting.ToString();
					schedulingTask.SetStatus(Status.Awaiting).Set(true);
					if (schedulingTask.Persistance)
						SchedulingTask.UpdateAsync(schedulingTask, true, Utility.CancellationToken).Run();
				}
				schedulingTask.SendMessages("Update", response, Utility.NodeID);
			}

			// response
			return response;
		}

		internal static async Task<JObject> UpdateSchedulingTaskAsync(this RequestInfo requestInfo, bool isSystemAdministrator, CancellationToken cancellationToken)
		{
			// prepare
			var schedulingTask = await (requestInfo.GetObjectIdentity() ?? "").GetSchedulingTaskByIDAsync(cancellationToken).ConfigureAwait(false);
			if (schedulingTask == null)
				throw new InformationNotFoundException();
			else if (schedulingTask.Organization == null)
				throw new InformationInvalidException("The organization is invalid");

			// check permission
			var gotRights = isSystemAdministrator || requestInfo.Session.User.IsModerator(null, null, schedulingTask.Organization);
			if (!gotRights)
				throw new AccessDeniedException();

			// update
			var requestBody = requestInfo.GetBodyExpando();
			schedulingTask.Update(requestBody, "ID,SystemID,Privileges,Time,Created,CreatedID,LastModified,LastModifiedID", obj =>
			{
				obj.LastModified = DateTime.Now;
				obj.LastModifiedID = requestInfo.Session.User.ID;
				obj.SetTime(requestBody.Get<DateTime>("Time"));
			});
			await SchedulingTask.UpdateAsync(schedulingTask, requestInfo.Session.User.ID, cancellationToken).ConfigureAwait(false);
			schedulingTask.Set().ClearRelatedCacheAsync(Utility.CancellationToken).Run();

			// send update messages
			var versions = await schedulingTask.FindVersionsAsync(cancellationToken, false).ConfigureAwait(false);
			var response = schedulingTask.ToJson(json => json.UpdateVersions(versions));
			schedulingTask.SendMessages("Update", response, Utility.NodeID);

			// send notification
			schedulingTask.SendNotification("Update", schedulingTask.Organization.Notifications, ApprovalStatus.Published, ApprovalStatus.Published, requestInfo);

			// response
			return response;
		}

		internal static async Task<JObject> DeleteSchedulingTaskAsync(this RequestInfo requestInfo, bool isSystemAdministrator, CancellationToken cancellationToken)
		{
			// prepare
			var schedulingTask = await (requestInfo.GetObjectIdentity() ?? "").GetSchedulingTaskByIDAsync(cancellationToken).ConfigureAwait(false);
			if (schedulingTask == null)
				throw new InformationNotFoundException();
			else if (schedulingTask.Organization == null)
				throw new InformationInvalidException("The organization is invalid");

			// check permission
			var gotRights = isSystemAdministrator || requestInfo.Session.User.IsModerator(null, null, schedulingTask.Organization);
			if (!gotRights)
				throw new AccessDeniedException();

			// delete
			return await schedulingTask.DeleteAsync(requestInfo, true, true, cancellationToken).ConfigureAwait(false);
		}

		internal static async Task<JObject> DeleteAsync(this SchedulingTask schedulingTask, RequestInfo requestInfo, bool updateCache, bool sendUpdatingMessages, CancellationToken cancellationToken)
		{
			await SchedulingTask.DeleteAsync<SchedulingTask>(schedulingTask.ID, requestInfo.Session.User.ID, cancellationToken).ConfigureAwait(false);

			if (updateCache)
				await schedulingTask.ClearRelatedCacheAsync(cancellationToken).ConfigureAwait(false);

			var json = sendUpdatingMessages ? schedulingTask.ToJson() : null;
			if (sendUpdatingMessages)
				schedulingTask.SendMessages("Delete", json, Utility.NodeID);

			schedulingTask.Remove();
			await schedulingTask.SendNotificationAsync("Delete", schedulingTask.Organization?.Notifications, ApprovalStatus.Published, ApprovalStatus.Published, requestInfo, cancellationToken).ConfigureAwait(false);
			return json;
		}

		internal static async Task<JObject> SyncSchedulingTaskAsync(this RequestInfo requestInfo, CancellationToken cancellationToken, bool sendNotifications = false, bool dontCreateNewVersion = false)
		{
			var @event = requestInfo.GetParameter("event") ?? requestInfo.GetParameter("x-original-event");
			if (string.IsNullOrWhiteSpace(@event) || !@event.IsEquals("Delete"))
				@event = "Update";

			var data = requestInfo.GetBodyExpando();
			var schedulingTask = await data.Get<string>("ID").GetSchedulingTaskByIDAsync(cancellationToken).ConfigureAwait(false);

			if (!@event.IsEquals("Delete"))
			{
				if (schedulingTask == null)
				{
					schedulingTask = SchedulingTask.CreateInstance(data);
					await SchedulingTask.CreateAsync(schedulingTask, cancellationToken).ConfigureAwait(false);
				}
				else
				{
					schedulingTask.Fill(data);
					await SchedulingTask.UpdateAsync(schedulingTask, dontCreateNewVersion, cancellationToken).ConfigureAwait(false);
				}
			}
			else if (schedulingTask != null)
				await SchedulingTask.DeleteAsync<SchedulingTask>(schedulingTask.ID, schedulingTask.LastModifiedID, cancellationToken).ConfigureAwait(false);

			// stop if has no info
			if (schedulingTask == null)
				return new JObject();

			// clear cache
			await schedulingTask.ClearRelatedCacheAsync(cancellationToken).ConfigureAwait(false);

			// send notifications
			if (sendNotifications)
				await schedulingTask.SendNotificationAsync(@event, schedulingTask.Organization.Notifications, ApprovalStatus.Published, ApprovalStatus.Published, requestInfo, cancellationToken).ConfigureAwait(false);

			// send update messages
			var json = @event.IsEquals("Delete") ? schedulingTask.Remove().ToJson() : schedulingTask.Set().ToJson();
			schedulingTask.SendMessages(@event, json, Utility.NodeID);
			return json;
		}

		internal static async Task<JObject> RollbackSchedulingTaskAsync(this RequestInfo requestInfo, bool isSystemAdministrator, CancellationToken cancellationToken)
		{
			// prepare
			var schedulingTask = await (requestInfo.GetObjectIdentity() ?? "").GetSchedulingTaskByIDAsync(cancellationToken).ConfigureAwait(false) ?? throw new InformationNotFoundException();
			if (schedulingTask.Organization == null)
				throw new InformationInvalidException("The organization is invalid");

			// check permission
			var gotRights = isSystemAdministrator || requestInfo.Session.User.IsModerator(null, null, schedulingTask.Organization);
			if (!gotRights)
				throw new AccessDeniedException();

			// rollback
			schedulingTask = await RepositoryMediator.RollbackAsync<SchedulingTask>(requestInfo.GetParameter("x-version-id") ?? "", requestInfo.Session.User.ID, cancellationToken).ConfigureAwait(false);
			await schedulingTask.Set(true).ClearRelatedCacheAsync(cancellationToken).ConfigureAwait(false);
			await schedulingTask.SendNotificationAsync("Update", schedulingTask.Organization.Notifications, ApprovalStatus.Published, ApprovalStatus.Published, requestInfo, cancellationToken).ConfigureAwait(false);

			// send update messages
			var versions = await schedulingTask.FindVersionsAsync(cancellationToken, false).ConfigureAwait(false);
			var response = schedulingTask.ToJson(json => json.UpdateVersions(versions));
			schedulingTask.SendMessages("Update", response, Utility.NodeID);
			return response;
		}

		internal static async Task<JObject> FetchSchedulingTasksAsync(this RequestInfo requestInfo, CancellationToken cancellationToken = default)
		{
			var filter = requestInfo.GetRequestExpando().Get<ExpandoObject>("FilterBy")?.ToFilterBy<SchedulingTask>() ?? Filters<SchedulingTask>.And();
			var organizationID = filter.GetValue("SystemID") ?? requestInfo.GetParameter("x-system-id") ?? requestInfo.GetParameter("SystemID") ?? requestInfo.GetParameter("x-organization-id") ?? requestInfo.GetParameter("OrganizationID");
			var organization = await (organizationID ?? "").GetOrganizationByIDAsync(cancellationToken).ConfigureAwait(false) ?? throw new InformationInvalidException("The organization is invalid");
			var schedulingTasks = await organization.GetSchedulingTasksAsync(cancellationToken, false).ConfigureAwait(false);
			if ("false".IsEquals(requestInfo.GetParameter("x-update-messagae")))
				return new JObject
				{
					{ "FilterBy", Filters<SchedulingTask>.And(Filters<SchedulingTask>.Equals("SystemID", organization.ID)).ToClientJson() },
					{ "SortBy", Sorts<SchedulingTask>.Ascending("Time").ToClientJson() },
					{ "Pagination", (schedulingTasks.Count.CastAs<long>(), 1, 0, 1).GetPagination() },
					{ "Objects", schedulingTasks.Select(schedulingTask => schedulingTask.ToJson()).ToJArray() }
				};
			schedulingTasks.ForEach(schedulingTask => schedulingTask.SendMessage(requestInfo.Session.DeviceID));
			return new JObject();
		}

		internal static async Task<JObject> RunSchedulingTaskAsync(this RequestInfo requestInfo, bool isSystemAdministrator, CancellationToken cancellationToken)
		{
			// prepare
			var schedulingTask = await (requestInfo.GetObjectIdentity(true, true) ?? "").GetSchedulingTaskByIDAsync(cancellationToken).ConfigureAwait(false);
			if (schedulingTask == null)
				throw new InformationNotFoundException();
			if (schedulingTask.Organization == null)
				throw new InformationInvalidException("The organization is invalid");

			// check
			var gotRights = isSystemAdministrator || requestInfo.Session.User.IsModerator(null, null, schedulingTask.Organization);
			if (!gotRights)
				throw new AccessDeniedException();

			// run
			if (schedulingTask.Status.Equals(Status.Awaiting))
				schedulingTask.RunAsync(requestInfo.CorrelationID, Utility.CancellationToken).Run(async ex => await requestInfo.WriteErrorAsync(ex, $"Error occurred while running a scheduling task => {ex.Message} [{ex.GetType()}]", "Tasks").ConfigureAwait(false));

			return new JObject
			{
				{ "ID", schedulingTask.ID },
				{ "Status", Status.Running.ToString() }
			};
		}

		internal static async Task RunSchedulingTasksAsync(string correlationID)
		{
			var schedulingTasks = SchedulingTaskProcessor.SchedulingTasks.Select(kvp => kvp.Value).Where(schedulingTask => schedulingTask.Status != Status.Awaiting && schedulingTask.Time > DateTime.Now).ToList();
			schedulingTasks.ForEach(schedulingTask =>
			{
				schedulingTask.SetStatus(Status.Awaiting).Set(true).SendMessages();
				if (schedulingTask.Persistance)
					SchedulingTask.UpdateAsync(schedulingTask, true, Utility.CancellationToken).Run();
			});

			await Task.Delay(UtilityService.GetRandomNumber(123, 456), Utility.CancellationToken).ConfigureAwait(false);
			schedulingTasks = SchedulingTaskProcessor.SchedulingTasks.Select(kvp => kvp.Value).Where(schedulingTask => schedulingTask.Status == Status.Awaiting && schedulingTask.Time <= DateTime.Now).ToList();
			if (!schedulingTasks.Any())
				return;

			var ids = schedulingTasks.Select(schedulingTask => schedulingTask.ID).ToHashSet();
			schedulingTasks.ForEach(schedulingTask => schedulingTask.SetStatus(Status.Acquired).SendMessages());

			await Task.Delay(UtilityService.GetRandomNumber(123, 456), Utility.CancellationToken).ConfigureAwait(false);
			schedulingTasks = SchedulingTaskProcessor.SchedulingTasks.Select(kvp => kvp.Value).Where(schedulingTask => ids.Contains(schedulingTask.ID) && schedulingTask.Status == Status.Acquired && schedulingTask.Time <= DateTime.Now).ToList();
			if (Utility.IsDebugLogEnabled)
				await Utility.WriteLogAsync(correlationID, $"Run {schedulingTasks.Count} scheduling task(s)", "Tasks").ConfigureAwait(false);

			await schedulingTasks.ForEachAsync(async schedulingTask =>
			{
				try
				{
					await schedulingTask.RunAsync(correlationID, Utility.CancellationToken).ConfigureAwait(false);
				}
				catch (Exception ex)
				{
					await Utility.WriteErrorAsync(ex, $"Error occurred while running a scheduling task => {ex.Message} [{ex.GetType()}] --> {schedulingTask.ToJson(json => json.Remove("Privileges"))}", "Tasks", correlationID).ConfigureAwait(false);
				}
			}).ConfigureAwait(false);

			if (schedulingTasks.Any() && DateTime.Now.Hour > 2 && DateTime.Now.Hour < 4 && DateTime.Now.Minute > 26 && DateTime.Now.Minute < 30)
			{
				var filter = Filters<SchedulingTask>.Or
				(
					Filters<SchedulingTask>.And
					(
						Filters<SchedulingTask>.Equals("Status", Status.Completed.ToString()),
						Filters<SchedulingTask>.LessThanOrEquals("Time", DateTime.Parse($"{DateTime.Now.AddDays(-7):yyyy/MM/dd} 00:00:00"))
					),
					Filters<SchedulingTask>.And
					(
						Filters<SchedulingTask>.Equals("Status", Status.Awaiting.ToString()),
						Filters<SchedulingTask>.LessThanOrEquals("Time", DateTime.Now.AddHours(-48))
					)
				);
				schedulingTasks = await SchedulingTaskProcessor.SearchAsync(filter, Utility.CancellationToken).ConfigureAwait(false);
				if (Utility.IsDebugLogEnabled)
					await Utility.WriteLogAsync(correlationID, $"Delete {schedulingTasks.Count} archived scheduling tasks", "Tasks").ConfigureAwait(false);
				await schedulingTasks.ForEachAsync(async schedulingTask =>
				{
					await SchedulingTask.DeleteAsync<SchedulingTask>(schedulingTask.ID, null, Utility.CancellationToken).ConfigureAwait(false);
					schedulingTask.Remove().SendMessages("Delete");
				}, true, false).ConfigureAwait(false);
			}

			schedulingTasks = SchedulingTaskProcessor.SchedulingTasks.Select(kvp => kvp.Value).Where(schedulingTask => ids.Contains(schedulingTask.ID) && schedulingTask.Status == Status.Acquired && schedulingTask.Time <= DateTime.Now).ToList();
			schedulingTasks.ForEach(schedulingTask =>
			{
				schedulingTask.SetStatus(Status.Awaiting).SetTime().Set(true).SendMessages();
				if (schedulingTask.Persistance)
					SchedulingTask.UpdateAsync(schedulingTask, true, Utility.CancellationToken).Run();
			});
		}

		internal static async Task RunAsync(this SchedulingTask schedulingTask, string correlationID, CancellationToken cancellationToken)
		{
			// prepare
			var stopwatch = Stopwatch.StartNew();
			schedulingTask.SetStatus(Status.Running).SendMessages();

			var isForceRefreshPredefinedURLs = schedulingTask.SchedulingType.Equals(SchedulingType.Refresh) && schedulingTask.ID.IsEquals($"{schedulingTask.SystemID}:URLs:Force".GenerateUUID());
			await Utility.WriteLogAsync(correlationID, $"Run a scheduling task [{schedulingTask.Title} @ {schedulingTask.Organization.Title} - ID: {schedulingTask.ID}]{(Utility.IsDebugLogEnabled || isForceRefreshPredefinedURLs ? $"\r\n{schedulingTask.ToJson(json => json.Remove("Privileges"))}" : "")}", "Tasks").ConfigureAwait(false);

			if (schedulingTask.Persistance)
				SchedulingTask.UpdateAsync(schedulingTask, true, cancellationToken).Run();

			// update
			if (schedulingTask.SchedulingType.Equals(SchedulingType.Update))
				try
				{
					if (!(await RepositoryMediator.GetAsync(schedulingTask.EntityInfo, schedulingTask.ObjectID, cancellationToken).ConfigureAwait(false) is IBusinessObject @object) || @object.Organization is not Organization)
						throw new InformationInvalidException($"The object for updating is invalid");

					var status = @object.Status;
					var json = schedulingTask.DataAsJson;
					var expando = json.Get<JObject>("Object")?.ToExpandoObject() ?? json.ToExpandoObject();

					var requestInfo = new RequestInfo(new Session { SessionID = UtilityService.NewUUID }, Utility.ServiceName, (@object as RepositoryBase).GetObjectName(), "PUT");
					requestInfo.Session.User.SessionID = requestInfo.Session.SessionID;
					requestInfo.Session.User.ID = schedulingTask.UserID ?? "";
					requestInfo.Query["object-identity"] = (@object as IPortalObject).ID;
					requestInfo.CorrelationID = UtilityService.NewUUID;

					if (@object is Content content)
						await content.Update(expando, "ID,SystemID,RepositoryID,RepositoryEntityID,Privileges,Created,CreatedID,LastModified,LastModifiedID", out var _, obj =>
						{
							obj.LastModified = DateTime.Now;
							obj.LastModifiedID = requestInfo.Session.User.ID;
							if (!string.IsNullOrWhiteSpace(expando?.Get<string>("Details")))
								obj.Details = obj.Organization.NormalizeURLs(obj.Details, false);
						}).UpdateAsync(requestInfo, status, cancellationToken).ConfigureAwait(false);

					else if (@object is Item item)
						await item.Update(expando, "ID,SystemID,RepositoryID,RepositoryEntityID,Privileges,Created,CreatedID,LastModified,LastModifiedID", obj =>
						{
							obj.LastModified = DateTime.Now;
							obj.LastModifiedID = requestInfo.Session.User.ID;
						}).UpdateAsync(requestInfo, status, cancellationToken).ConfigureAwait(false);

					else if (@object is Link link)
					{
						var parentID = link.ParentID;
						await link.Update(expando, "ID,SystemID,RepositoryID,RepositoryEntityID,Privileges,Created,CreatedID,LastModified,LastModifiedID", obj =>
						{
							obj.LastModified = DateTime.Now;
							obj.LastModifiedID = requestInfo.Session.User.ID;
						}).UpdateAsync(requestInfo, status, parentID, cancellationToken).ConfigureAwait(false);
					}

					if (@object.Status.Equals(ApprovalStatus.Published))
					{
						var rootURL = $"{schedulingTask.Organization.URL}/";
						var urls = await (@object.Organization as Organization).GetRefreshingURLsAsync(json?.Get<JArray>("URLs")?.Select(value => value as JValue).Select(value => value.ToString()) ?? []).ConfigureAwait(false);
						await urls.Select(url => string.IsNullOrWhiteSpace(url) ? "" : url.Replace("~/", rootURL))
							.Where(url => url.IsStartsWith("https://") || url.IsStartsWith("http://"))
							.Select(url => url.IsContains("?x-force-cache")  || url.IsContains("&x-force-cache") ? url : $"{url}{(url.IndexOf("?") > 0 ? "&" : "?")}x-force-cache")
							.Distinct(StringComparer.OrdinalIgnoreCase)
							.ToList()
							.ForEachAsync(url => url.RefreshWebPageAsync(requestInfo.CorrelationID), true, false).ConfigureAwait(false);
					}
				}
				catch (Exception ex)
				{
					await Utility.WriteErrorAsync(ex, $"Error occurred while running a scheduling task for updating => {ex.Message} [{ex.GetType()}]\r\nUpdating data: {schedulingTask.DataAsJson}", "Tasks", correlationID).ConfigureAwait(false);
				}

			// refresh webpages
			else if (schedulingTask.SchedulingType.Equals(SchedulingType.Refresh))
				try
				{
					var stepwatch = Stopwatch.StartNew();
					var rootURL = $"{schedulingTask.Organization.URL}/";
					var addresses = (schedulingTask.DataAsJson as JArray).Select(value => value as JValue).Select(value => value.ToString()).ToList();

					var organizationURLs = await schedulingTask.Organization.GetRefreshingURLsAsync(true).ConfigureAwait(false);
					var refreshingURLs = addresses.Where(url => !url.IsStartsWith("@organization:") && !url.IsStartsWith("@organization("))
						.Concat(organizationURLs.Select(url => isForceRefreshPredefinedURLs ? $"{url}{(url.IndexOf("?") > 0 ? "&" : "?")}x-force-cache" : url))
						.ToList();

					var organizationIDs = addresses.Where(url => url.IsStartsWith("@organization:") || url.IsStartsWith("@organization("))
						.Select(url => url.Replace(StringComparison.OrdinalIgnoreCase, "@organization:", "").Replace(StringComparison.OrdinalIgnoreCase, "@organization(", "").Replace(")", "").Trim()).ToList();
					await organizationIDs.ForEachAsync(async organizationID =>
					{
						var organization = await organizationID.GetOrganizationByIDAsync(Utility.CancellationToken).ConfigureAwait(false);
						if (organization != null)
							refreshingURLs = refreshingURLs.Concat([$"{organization.URL}/"])
								.Concat((organization.Sites ?? []).Where(site => !site.ID.IsEquals(organization.DefaultSite?.ID)).Select(site => $"{site.GetURL()}/{(organization.AlwaysUseHtmlSuffix ? "index.html" : "")}"))
								.Concat(await organization.GetRefreshingURLsAsync().ConfigureAwait(false))
								.Concat(await organization.GetRefreshingURLsAsync(true).ConfigureAwait(false))
								.Select(url => $"{url}{(url.IndexOf("?") > 0 ? "&" : "?")}x-force-cache")
								.ToList();
					}, true, false).ConfigureAwait(false);

					refreshingURLs = refreshingURLs.Select(url => string.IsNullOrWhiteSpace(url) ? rootURL : url.Replace("~/", rootURL))
						.Where(url => url.IsStartsWith("https://") || url.IsStartsWith("http://")).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
					await refreshingURLs.ForEachAsync(url => url.RefreshWebPageAsync(correlationID), true, false).ConfigureAwait(false);

					stepwatch.Stop();
					if (Utility.IsDebugLogEnabled || isForceRefreshPredefinedURLs)
						await Utility.WriteLogAsync(correlationID, $"Force refresh all pre-defined URLs of '{schedulingTask.Organization.Title}' successful - Execution times: {stepwatch.GetElapsedTimes()}\r\nURLs:\r\n\t- {refreshingURLs.Join("\r\n\t- ")}", "Tasks").ConfigureAwait(false);
				}
				catch (Exception ex)
				{
					await Utility.WriteErrorAsync(ex, $"Error occurred while running a scheduling task for refreshing webpages => {ex.Message} [{ex.GetType()}]\r\nURLs: {schedulingTask.DataAsJson}", "Tasks", correlationID).ConfigureAwait(false);
				}

			// send a notification
			else if (schedulingTask.SchedulingType.Equals(SchedulingType.SendNotification))
				try
				{
				}
				catch (Exception ex)
				{
					await Utility.WriteErrorAsync(ex, $"Error occurred while running a scheduling task for sending a notification => {ex.Message} [{ex.GetType()}]\r\nNotification data: {schedulingTask.DataAsJson}", "Tasks", correlationID).ConfigureAwait(false);
				}

			// run a crawler
			else if (schedulingTask.SchedulingType.Equals(SchedulingType.RunCrawler))
				try
				{
				}
				catch (Exception ex)
				{
					await Utility.WriteErrorAsync(ex, $"Error occurred while running a scheduling task for crawling data => {ex.Message} [{ex.GetType()}]", "Tasks", correlationID).ConfigureAwait(false);
				}

			// update next run (if still existed)
			if (SchedulingTaskProcessor.SchedulingTasks.ContainsKey(schedulingTask.ID))
			{
				schedulingTask.SetStatus(schedulingTask.RecurringUnit > 0 ? Status.Awaiting : Status.Completed).SetTime().SendMessages();
				if (schedulingTask.Persistance)
					SchedulingTask.UpdateAsync(schedulingTask, true, cancellationToken).Run();
			}

			stopwatch.Stop();
			await Utility.WriteLogAsync(correlationID, $"Task was ran successful [{schedulingTask.ID}] - Execution times: {stopwatch.GetElapsedTimes()}", "Tasks").ConfigureAwait(false);
		}
	}
}
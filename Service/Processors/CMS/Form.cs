#region Related components
using System;
using System.Linq;
using System.Xml.Linq;
using System.Dynamic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using net.vieapps.Components.Security;
using net.vieapps.Components.Repository;
using net.vieapps.Components.Utility;
#endregion

namespace net.vieapps.Services.Portals
{
	public static class FormProcessor
	{
		public static Form CreateFormInstance(this ExpandoObject data, string excluded = null, Action<Form> onCompleted = null)
			=> Form.CreateInstance(data, excluded?.ToHashSet(), form =>
			{
				form.NormalizeHTMLs();
				onCompleted?.Invoke(form);
			});

		public static Form UpdateFormInstance(this Form form, ExpandoObject data, string excluded = null, Action<Form> onCompleted = null)
			=> form.Fill(data, excluded?.ToHashSet(), _ =>
			{
				form.NormalizeHTMLs();
				onCompleted?.Invoke(form);
			});

		public static Form Normalize(this Form form, RequestInfo requestInfo)
			=> form.Compute(requestInfo, () => form.Validate((name, value) =>
			{
				if (name == "Phone")
				{
					if (form.Phone.IsValidPhone(out var phone))
						form.Phone = phone;
					else
						throw new InformationInvalidException("Phone is invalid");
				}
				else if (name == "Email")
				{
					if (form.Email.IsValidEmail(out var email))
						form.Email = email;
					else
						throw new InformationInvalidException("Email is invalid");
				}
			}));

		public static IFilterBy<Form> GetFormsFilter(string systemID, string repositoryID = null, string repositoryEntityID = null)
		{
			var filter = Filters<Form>.And();
			if (!string.IsNullOrWhiteSpace(systemID))
				filter.Add(Filters<Form>.Equals("SystemID", systemID));
			if (!string.IsNullOrWhiteSpace(repositoryID))
				filter.Add(Filters<Form>.Equals("RepositoryID", repositoryID));
			if (!string.IsNullOrWhiteSpace(repositoryEntityID))
				filter.Add(Filters<Form>.Equals("RepositoryEntityID", repositoryEntityID));
			return filter;
		}

		internal static async Task ClearRelatedCacheAsync(this Form form, CancellationToken cancellationToken = default, string correlationID = null, bool clearDataCache = true)
		{
			var dataCacheKeys = clearDataCache && form != null
				? Extensions.GetRelatedCacheKeys(form.GetCacheKey())
				: new List<string>();
			if (clearDataCache && form?.ContentType != null)
			{
				var cacheKeys = await Utility.Cache.GetSetMembersAsync(form.ContentType.GetSetCacheKey(), cancellationToken).ConfigureAwait(false);
				if (cacheKeys != null && cacheKeys.Count > 0)
					dataCacheKeys = dataCacheKeys.Concat(cacheKeys).Concat(new[] { form.ContentType.GetSetCacheKey() }).ToList();
			}
			dataCacheKeys = dataCacheKeys.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

			await Utility.Cache.RemoveAsync(dataCacheKeys, cancellationToken).ConfigureAwait(false);
			if (Utility.WriteCacheLogs && form != null)
				await Utility.WriteLogAsync(correlationID, $"Clear related cache of a CMS form [{form.Title} - ID: {form.ID}]\r\n- {dataCacheKeys.Count} data keys => {dataCacheKeys.Join(", ")}", cancellationToken, "Caches").ConfigureAwait(false);
		}

		static async Task<Tuple<long, List<Form>, JToken, List<string>>> SearchAsync(this RequestInfo requestInfo, string query, IFilterBy<Form> filter, SortBy<Form> sort, int pageSize, int pageNumber, string contentTypeID = null, long totalRecords = -1, CancellationToken cancellationToken = default, bool searchThumbnails = false)
		{
			// cache keys
			var cacheKeyOfObjects = string.IsNullOrWhiteSpace(query) ? Extensions.GetCacheKey(filter, sort, pageSize, pageNumber) : null;
			var cacheKeyOfTotalObjects = string.IsNullOrWhiteSpace(query) ? Extensions.GetCacheKeyOfTotalObjects(filter, sort) : null;
			var cacheKeys = string.IsNullOrWhiteSpace(query) ? new List<string> { cacheKeyOfObjects, cacheKeyOfTotalObjects } : new List<string>();

			// count
			totalRecords = totalRecords > -1
				? totalRecords
				: string.IsNullOrWhiteSpace(query)
					? await Form.CountAsync(filter, contentTypeID, cacheKeyOfTotalObjects, cancellationToken).ConfigureAwait(false)
					: await Form.CountAsync(query, filter, contentTypeID, cancellationToken).ConfigureAwait(false);

			// search objects
			var objects = totalRecords > 0
				? string.IsNullOrWhiteSpace(query)
					? await Form.FindAsync(filter, sort, pageSize, pageNumber, contentTypeID, cacheKeyOfObjects, cancellationToken).ConfigureAwait(false)
					: await Form.SearchAsync(query, filter, null, pageSize, pageNumber, contentTypeID, cancellationToken).ConfigureAwait(false)
				: new List<Form>();

			// search thumbnails
			JToken thumbnails = null;
			if (objects.Count > 0 && searchThumbnails)
			{
				requestInfo.Header["x-thumbnails-as-attachments"] = "true";
				thumbnails = objects.Count == 1
					? await requestInfo.GetThumbnailsAsync(objects[0].ID, objects[0].Title.Url64Encode(), Utility.ValidationKey, cancellationToken).ConfigureAwait(false)
					: await requestInfo.GetThumbnailsAsync(objects.Select(@object => @object.ID).Join(","), objects.ToJObject("ID", @object => new JValue(@object.Title.Url64Encode())).ToString(Formatting.None), Utility.ValidationKey, cancellationToken).ConfigureAwait(false);
			}

			// page size to clear related cached
			if (string.IsNullOrWhiteSpace(query))
				await Utility.SetCacheOfPageSizeAsync(filter, sort, pageSize, cancellationToken).ConfigureAwait(false);

			// store object identities to clear related cached
			var contentType = objects.FirstOrDefault()?.ContentType;
			if (contentType != null)
				await Utility.Cache.AddSetMembersAsync(contentType.ObjectCacheKeys, objects.Select(@object => @object.GetCacheKey()), cancellationToken).ConfigureAwait(false);

			// return the results
			return new Tuple<long, List<Form>, JToken, List<string>>(totalRecords, objects, thumbnails, cacheKeys);
		}

		internal static async Task<JObject> SearchFormsAsync(this RequestInfo requestInfo, bool isSystemAdministrator, CancellationToken cancellationToken)
		{
			// prepare
			var request = requestInfo.GetRequestExpando();
			var query = request.Get<string>("FilterBy.Query");
			var filter = request.Get<ExpandoObject>("FilterBy")?.ToFilterBy<Form>() ?? Filters<Form>.And();
			var sort = string.IsNullOrWhiteSpace(query) ? request.Get<ExpandoObject>("SortBy")?.ToSortBy<Form>() ?? Sorts<Form>.Descending("Created").ThenByAscending("Title") : null;
			var pagination = request.Get<ExpandoObject>("Pagination")?.GetPagination() ?? new Tuple<long, int, int, int>(-1, 0, 20, 1);
			var pageSize = pagination.Item3;
			var pageNumber = pagination.Item4;
			var organizationID = filter.GetValue("SystemID") ?? requestInfo.GetParameter("SystemID") ?? requestInfo.GetParameter("x-system-id");
			var organization = await (organizationID ?? "").GetOrganizationByIDAsync(cancellationToken).ConfigureAwait(false);
			if (organization == null)
				throw new InformationExistedException("The organization is invalid");

			var moduleID = filter.GetValue("RepositoryID") ?? requestInfo.GetParameter("RepositoryID") ?? requestInfo.GetParameter("x-module-id");
			var module = await (moduleID ?? "").GetModuleByIDAsync(cancellationToken).ConfigureAwait(false);
			if (module == null || !module.SystemID.IsEquals(organization.ID))
				throw new InformationInvalidException("The module is invalid");

			var contentTypeID = filter.GetValue("RepositoryEntityID") ?? requestInfo.GetParameter("RepositoryEntityID") ?? requestInfo.GetParameter("x-content-type-id");
			var contentType = await (contentTypeID ?? "").GetContentTypeByIDAsync(cancellationToken).ConfigureAwait(false);
			if (contentType == null || !contentType.SystemID.IsEquals(organization.ID) || !contentType.RepositoryID.IsEquals(module.ID))
				throw new InformationInvalidException("The content-type is invalid");

			// check permission
			var gotRights = isSystemAdministrator || requestInfo.Session.User.IsViewer(contentType.WorkingPrivileges, null, organization, requestInfo.CorrelationID);
			if (!gotRights)
				throw new AccessDeniedException();

			// normalize filter
			if (filter == null || !(filter is FilterBys<Form>) || (filter as FilterBys<Form>).Children == null || (filter as FilterBys<Form>).Children.Count < 1)
				filter = FormProcessor.GetFormsFilter(organization.ID, module.ID, contentType.ID);
			if (!requestInfo.Session.User.IsAuthenticated)
			{
				if (!(filter.GetChild("Status") is FilterBy<Form> filterByStatus))
					(filter as FilterBys<Form>).Add(Filters<Form>.Equals("Status", ApprovalStatus.Published.ToString()));
				else if (filterByStatus.Value == null || !(filterByStatus.Value as string).IsEquals(ApprovalStatus.Published.ToString()))
					filterByStatus.Value = ApprovalStatus.Published.ToString();
			}
			filter.Prepare(requestInfo);

			// process cache
			var cacheKeyOfObjectsJson = string.IsNullOrWhiteSpace(query) ? Extensions.GetCacheKeyOfObjectsJson(filter, sort, pageSize, pageNumber) : null;
			if (cacheKeyOfObjectsJson != null)
			{
				var json = await Utility.Cache.GetAsync<string>(cacheKeyOfObjectsJson, cancellationToken).ConfigureAwait(false);
				if (!string.IsNullOrWhiteSpace(json))
					return JObject.Parse(json);
			}

			// search if has no cache
			var results = await requestInfo.SearchAsync(query, filter, sort, pageSize, pageNumber, contentType.ID, pagination.Item1 > -1 ? pagination.Item1 : -1, cancellationToken).ConfigureAwait(false);
			var totalRecords = results.Item1;
			var objects = results.Item2;

			// build response
			var totalPages = new Tuple<long, int>(totalRecords, pageSize).GetTotalPages();
			if (totalPages > 0 && pageNumber > totalPages)
				pageNumber = totalPages;
			pagination = new Tuple<long, int, int, int>(totalRecords, totalPages, pageSize, pageNumber);

			var showURLs = requestInfo.GetParameter("ShowURLs") != null;
			var response = new JObject()
			{
				{ "FilterBy", filter.ToClientJson(query) },
				{ "SortBy", sort?.ToClientJson() },
				{ "Pagination", pagination.GetPagination() },
				{ "Objects", objects.Select(@object => @object.ToJson(false)).ToJArray() }
			};

			// update cache
			if (string.IsNullOrWhiteSpace(query))
			{
				await Utility.Cache.SetAsync(cacheKeyOfObjectsJson, response.ToString(Formatting.None), cancellationToken).ConfigureAwait(false);
				var cacheKeys = new[] { cacheKeyOfObjectsJson }.Concat(results.Item4).ToList();
				await Task.WhenAll
				(
					Utility.Cache.AddSetMembersAsync(contentType.GetSetCacheKey(), cacheKeys, cancellationToken),
					Utility.Logger.IsEnabled(LogLevel.Debug) ? Utility.WriteLogAsync(requestInfo, $"Update cache when search CMS forms\r\n- Cache key of JSON: {cacheKeyOfObjectsJson}\r\n- Cache key of realated sets: {contentType.GetSetCacheKey()}\r\n- Related cache keys: {cacheKeys.Join(", ")}", cancellationToken, "Caches") : Task.CompletedTask
				).ConfigureAwait(false);
			}

			// response
			return response;
		}

		internal static async Task<JObject> CreateFormAsync(this RequestInfo requestInfo, bool isSystemAdministrator, CancellationToken cancellationToken)
		{
			// prepare
			var request = requestInfo.GetBodyExpando();

			var organizationID = request.Get<string>("SystemID") ?? requestInfo.GetParameter("SystemID") ?? requestInfo.GetParameter("x-system-id") ?? requestInfo.GetParameter("OrganizationID");
			var organization = await (organizationID ?? "").GetOrganizationByIDAsync(cancellationToken).ConfigureAwait(false);
			if (organization == null)
				throw new InformationInvalidException("The organization is invalid");

			var moduleID = request.Get<string>("RepositoryID") ?? requestInfo.GetParameter("RepositoryID") ?? requestInfo.GetParameter("x-module-id") ?? requestInfo.GetParameter("ModuleID");
			var module = await (moduleID ?? "").GetModuleByIDAsync(cancellationToken).ConfigureAwait(false);
			if (module == null || !module.SystemID.IsEquals(organization.ID))
				throw new InformationInvalidException("The module is invalid");

			var contentTypeID = request.Get<string>("RepositoryEntityID") ?? requestInfo.GetParameter("RepositoryEntityID") ?? requestInfo.GetParameter("x-content-type-id") ?? requestInfo.GetParameter("ContentTypeID");
			var contentType = await (contentTypeID ?? "").GetContentTypeByIDAsync(cancellationToken).ConfigureAwait(false);
			if (contentType == null || !contentType.SystemID.IsEquals(organization.ID) || !contentType.RepositoryID.IsEquals(module.ID))
				throw new InformationInvalidException("The content-type is invalid");

			// check permission
			var gotRights = isSystemAdministrator || requestInfo.Session.User.IsContributor(contentType.WorkingPrivileges, null, organization, requestInfo.CorrelationID);
			if (!gotRights)
				throw new AccessDeniedException();

			// check captcha
			if (!requestInfo.Session.User.IsAuthenticated)
			{
				var captcha = request.Get<ExpandoObject>("Captcha");
				var registered = captcha?.Get<string>("Registered");
				var code = captcha?.Get<string>("Code");
				if (!CaptchaService.IsCodeValid(registered, code))
					throw new InvalidRequestException("Captcha code is invalid");
			}

			// get data
			var form = request.CreateFormInstance("Privileges,Created,CreatedID,LastModified,LastModifiedID,Captcha", obj =>
			{
				obj.SystemID = organization.ID;
				obj.RepositoryID = module.ID;
				obj.RepositoryEntityID = contentType.ID;
				obj.ID = string.IsNullOrWhiteSpace(obj.ID) || !obj.ID.IsValidUUID() ? UtilityService.NewUUID : obj.ID;
				obj.Created = obj.LastModified = DateTime.Now;
				obj.CreatedID = obj.LastModifiedID = requestInfo.Session.User.IsAuthenticated ? requestInfo.Session.User.ID : null;
				obj.IPAddress = requestInfo.Session.IP;
				obj.Profiles = requestInfo.Session.User.IsAuthenticated ? new Dictionary<string, string> { ["vieapps"] = requestInfo.Session.User.ID } : null;
			});

			// compute & validate
			form.Normalize(requestInfo);

			// create new
			await Form.CreateAsync(form, cancellationToken).ConfigureAwait(false);
			await form.ClearRelatedCacheAsync(cancellationToken, requestInfo.CorrelationID).ConfigureAwait(false);

			// send update message
			var response = form.ToJson();
			new UpdateMessage
			{
				Type = $"{requestInfo.ServiceName}#{form.GetObjectName()}#Create",
				DeviceID = "*",
				Data = response
			}.Send();

			// send notification
			await form.SendNotificationAsync("Create", form.ContentType.Notifications, ApprovalStatus.Draft, form.Status, requestInfo, cancellationToken).ConfigureAwait(false);

			// store object cache key to clear related cached
			await Utility.Cache.AddSetMemberAsync(form.ContentType.ObjectCacheKeys, form.GetCacheKey(), cancellationToken).ConfigureAwait(false);

			// response
			return response;
		}

		internal static async Task<JObject> GetFormAsync(this RequestInfo requestInfo, bool isSystemAdministrator, CancellationToken cancellationToken)
		{
			// prepare
			var identity = requestInfo.GetObjectIdentity(true, true) ?? "";
			var form = await Form.GetAsync<Form>(identity, cancellationToken).ConfigureAwait(false);
			if (form == null)
				throw new InformationNotFoundException();
			else if (form.Organization == null || form.Module == null || form.ContentType == null)
				throw new InformationInvalidException("The organization/module/form-type is invalid");

			// check permission
			var gotRights = isSystemAdministrator || requestInfo.Session.User.IsViewer(form.WorkingPrivileges, form.ContentType.WorkingPrivileges, form.Organization, requestInfo.CorrelationID);
			if (!gotRights)
				throw new AccessDeniedException();

			// refresh (clear cache)
			var isRefresh = "refresh".IsEquals(requestInfo.GetObjectIdentity());
			if (isRefresh)
				await form.ClearRelatedCacheAsync(cancellationToken, requestInfo.CorrelationID).ConfigureAwait(false);

			// prepare the response
			var response = form.ToJson();

			// send update message
			var objectName = form.GetObjectName();
			new UpdateMessage
			{
				Type = $"{requestInfo.ServiceName}#{objectName}#Update",
				Data = response,
				DeviceID = "*",
				ExcludedDeviceID = isRefresh ? "" : requestInfo.Session.DeviceID
			}.Send();
			if (isRefresh)
				new CommunicateMessage(requestInfo.ServiceName)
				{
					Type = $"{objectName}#Update",
					Data = response,
					ExcludedNodeID = Utility.NodeID
				}.Send();

			// store object cache key to clear related cached
			await Utility.Cache.AddSetMemberAsync(form.ContentType.ObjectCacheKeys, form.GetCacheKey(), cancellationToken).ConfigureAwait(false);

			// response
			return response;
		}

		internal static async Task<JObject> UpdateAsync(this Form form, RequestInfo requestInfo, ApprovalStatus oldStatus, CancellationToken cancellationToken)
		{
			// update
			await Form.UpdateAsync(form, requestInfo.Session.User.ID, cancellationToken).ConfigureAwait(false);
			await form.ClearRelatedCacheAsync(cancellationToken, requestInfo.CorrelationID).ConfigureAwait(false);

			// send update message
			var response = form.ToJson();
			new UpdateMessage
			{
				Type = $"{requestInfo.ServiceName}#{form.GetObjectName()}#Update",
				DeviceID = "*",
				Data = response
			}.Send();

			// send notification
			await form.SendNotificationAsync("Update", form.ContentType.Notifications, oldStatus, form.Status, requestInfo, cancellationToken).ConfigureAwait(false);

			// response
			return response;
		}

		internal static async Task<JObject> UpdateFormAsync(this RequestInfo requestInfo, bool isSystemAdministrator, CancellationToken cancellationToken)
		{
			// prepare
			var form = await Form.GetAsync<Form>(requestInfo.GetObjectIdentity() ?? "", cancellationToken).ConfigureAwait(false);
			if (form == null)
				throw new InformationNotFoundException();
			else if (form.Organization == null || form.Module == null || form.ContentType == null)
				throw new InformationInvalidException("The organization/module/form-type is invalid");

			// check permission
			var gotRights = isSystemAdministrator || requestInfo.Session.User.IsEditor(form.WorkingPrivileges, form.ContentType.WorkingPrivileges, form.Organization, requestInfo.CorrelationID);
			if (!gotRights)
				gotRights = form.Status.Equals(ApprovalStatus.Draft) || form.Status.Equals(ApprovalStatus.Pending) || form.Status.Equals(ApprovalStatus.Rejected)
					? requestInfo.Session.User.ID.IsEquals(form.CreatedID)
					: requestInfo.Session.User.IsEditor(form.WorkingPrivileges, form.ContentType.WorkingPrivileges, form.Organization, requestInfo.CorrelationID);
			if (!gotRights)
				throw new AccessDeniedException();

			// prepare data
			var oldStatus = form.Status;
			form.UpdateFormInstance(requestInfo.GetBodyExpando(), "ID,SystemID,RepositoryID,RepositoryEntityID,Privileges,Created,CreatedID,LastModified,LastModifiedID,Profiles", obj =>
			{
				obj.LastModified = DateTime.Now;
				obj.LastModifiedID = requestInfo.Session.User.ID;
			});

			// compute & validate
			form.Normalize(requestInfo);

			// update
			return await form.UpdateAsync(requestInfo, oldStatus, cancellationToken).ConfigureAwait(false);
		}

		internal static async Task<JObject> DeleteFormAsync(this RequestInfo requestInfo, bool isSystemAdministrator, CancellationToken cancellationToken)
		{
			// prepare
			var form = await Form.GetAsync<Form>(requestInfo.GetObjectIdentity() ?? "", cancellationToken).ConfigureAwait(false);
			if (form == null)
				throw new InformationNotFoundException();
			else if (form.Organization == null || form.Module == null || form.ContentType == null)
				throw new InformationInvalidException("The organization/module/form-type is invalid");

			// check permission
			var gotRights = isSystemAdministrator || requestInfo.Session.User.IsModerator(form.WorkingPrivileges, form.ContentType.WorkingPrivileges, form.Organization, requestInfo.CorrelationID);
			if (!gotRights)
				gotRights = form.Status.Equals(ApprovalStatus.Draft) || form.Status.Equals(ApprovalStatus.Pending) || form.Status.Equals(ApprovalStatus.Rejected)
					? requestInfo.Session.User.ID.IsEquals(form.CreatedID) || requestInfo.Session.User.IsEditor(form.WorkingPrivileges, form.ContentType.WorkingPrivileges, form.Organization, requestInfo.CorrelationID)
					: requestInfo.Session.User.IsModerator(form.WorkingPrivileges, form.ContentType.WorkingPrivileges, form.Organization, requestInfo.CorrelationID);
			if (!gotRights)
				throw new AccessDeniedException();

			// delete files
			try
			{
				await requestInfo.DeleteFilesAsync(form.SystemID, form.RepositoryEntityID, form.ID, Utility.ValidationKey, cancellationToken).ConfigureAwait(false);
			}
			catch (Exception ex)
			{
				await requestInfo.WriteErrorAsync(ex, cancellationToken, $"Error occurred while deleting files => {ex.Message}", "CMS.Form").ConfigureAwait(false);
				throw;
			}

			// delete
			await Form.DeleteAsync<Form>(form.ID, requestInfo.Session.User.ID, cancellationToken).ConfigureAwait(false);
			await form.ClearRelatedCacheAsync(cancellationToken, requestInfo.CorrelationID).ConfigureAwait(false);

			// send update message
			var response = form.ToJson();
			new UpdateMessage
			{
				Type = $"{requestInfo.ServiceName}#{form.GetObjectName()}#Delete",
				DeviceID = "*",
				Data = response
			}.Send();

			// send notification
			await form.SendNotificationAsync("Delete", form.ContentType.Notifications, form.Status, form.Status, requestInfo, cancellationToken).ConfigureAwait(false);

			// store object cache key to clear related cached
			await Utility.Cache.RemoveSetMemberAsync(form.ContentType.ObjectCacheKeys, form.GetCacheKey(), cancellationToken).ConfigureAwait(false);

			// response
			return response;
		}

		internal static JObject Generate(RequestInfo requestInfo)
		{
			var requestJson = requestInfo.BodyAsJson;
			var portletID = requestJson.Get<string>("ID");
			var organizationID = requestJson.Get<JObject>("Organization")?.Get<string>("ID");
			var moduleID = requestJson.Get<JObject>("Module")?.Get<string>("ID");
			var contentTypeID = requestJson.Get<JObject>("ContentType")?.Get<string>("ID");
			var options = requestJson.Get<JObject>("Options");

			var data = "<div id=\"" + (portletID ?? "undefined") + "\"><div class=\"loading\"></div></div>" + @"
			<script>
			__vieapps.forms.request = {
				id: " + (string.IsNullOrWhiteSpace(contentTypeID) ? "undefined" : $"\"{contentTypeID}\"") + @",
				repository: " + (string.IsNullOrWhiteSpace(moduleID) ? "undefined" : $"\"{moduleID}\"") + @",
				system: " + (string.IsNullOrWhiteSpace(organizationID) ? "undefined" : $"\"{organizationID}\"") + @",
				form: " + (string.IsNullOrWhiteSpace(portletID) ? "undefined" : $"\"{portletID}\"") + @",
				options: " + (options?.ToString(Formatting.None) ?? "undefined") + @"
			};
			</script>";

			return new JObject
			{
				{ "Data", data },
				{ "RawHTML", true }
			};
		}

		internal static async Task<JObject> SyncFormAsync(this RequestInfo requestInfo, CancellationToken cancellationToken, bool sendNotifications = false)
		{
			var @event = requestInfo.GetHeaderParameter("Event");
			if (string.IsNullOrWhiteSpace(@event) || !@event.IsEquals("Delete"))
				@event = "Update";

			var data = requestInfo.GetBodyExpando();
			var form = await Form.GetAsync<Form>(data.Get<string>("ID"), cancellationToken).ConfigureAwait(false);
			var oldStatus = form != null ? form.Status : ApprovalStatus.Pending;

			if (!@event.IsEquals("Delete"))
			{
				if (form == null)
				{
					form = Form.CreateInstance(data);
					await Form.CreateAsync(form, cancellationToken).ConfigureAwait(false);
				}
				else
				{
					form.Fill(data);
					await Form.UpdateAsync(form, true, cancellationToken).ConfigureAwait(false);
				}
			}
			else if (form != null)
				await Form.DeleteAsync<Form>(form.ID, form.LastModifiedID, cancellationToken).ConfigureAwait(false);

			// update cache
			await form.ClearRelatedCacheAsync(cancellationToken, requestInfo.CorrelationID).ConfigureAwait(false);
			if (@event.IsEquals("Delete"))
				await Utility.Cache.RemoveSetMemberAsync(form.ContentType.ObjectCacheKeys, form.GetCacheKey(), cancellationToken).ConfigureAwait(false);
			else
				await Utility.Cache.AddSetMemberAsync(form.ContentType.ObjectCacheKeys, form.GetCacheKey(), cancellationToken).ConfigureAwait(false);

			// send notifications
			if (sendNotifications)
				await form.SendNotificationAsync(@event, form.ContentType.Notifications, oldStatus, form.Status, requestInfo, cancellationToken).ConfigureAwait(false);

			// send update messages
			var json = form.ToJson();
			var objectName = form.GetObjectName();
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
				{ "ID", form.ID },
				{ "Type", objectName }
			};
		}
	}
}
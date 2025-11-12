#region Related components
using System;
using System.Linq;
using System.Dynamic;
using System.Xml.Linq;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using net.vieapps.Components.Repository;
using net.vieapps.Components.Security;
using net.vieapps.Components.Utility;
#endregion

namespace net.vieapps.Services.Portals
{
	public static class FormProcessor
	{
		static List<string> ExtraSections { get; } = ["Submited", "Opened", "Confirmed", "Unsubscribed", "Cancelled", "Paid", "Completed", "Refunded", "Others"];

		public static async Task<Form> NormalizeAsync(this Form form, JToken extras, RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			extras ??= (form.Extras ?? "{}").ToJson();
			await FormProcessor.ExtraSections.ForEachAsync(async name =>
			{
				var section = extras.Get<JToken>(name);
				if (section is JArray sectionArray)
					await sectionArray.ForEachAsync(async sectionObject =>
					{
						var userAgent = sectionObject.Get<string>("UserAgent");
						if (userAgent != null)
						{
							sectionObject["OSInfo"] = $"{Extensions.GetOSInfo(userAgent)} [{userAgent}]";
							(sectionObject as JObject).Remove("UserAgent");
						}
						if (sectionObject.Get<string>("IP") != null && sectionObject.Get<string>("Location") == null)
							try
							{
								sectionObject["Location"] = await requestInfo.Session.GetLocationAsync(sectionObject.Get<string>("IP"), requestInfo.CorrelationID, cancellationToken).ConfigureAwait(false);
							}
							catch { }
					}, true, false).ConfigureAwait(false);
				else if (section is JObject sectionObject)
				{
					var userAgent = sectionObject.Get<string>("UserAgent");
					if (userAgent != null)
					{
						sectionObject["OSInfo"] = $"{Extensions.GetOSInfo(userAgent)} [{userAgent}]";
						sectionObject.Remove("UserAgent");
					}
					if (sectionObject.Get<string>("IP") != null && sectionObject.Get<string>("Location") == null)
						try
						{
							sectionObject["Location"] = await requestInfo.Session.GetLocationAsync(sectionObject.Get<string>("IP"), requestInfo.CorrelationID, cancellationToken).ConfigureAwait(false);
						}
						catch { }
				}
			}, true, false).ConfigureAwait(false);
			form.Extras = extras.ToString(Formatting.Indented);
			form.Extras = string.IsNullOrWhiteSpace(form.Extras) || form.Extras == "{}" ? null : form.Extras;
			return form;
		}

		public static Form UpdateExtras(this Form form, JToken extras, string name, RequestInfo requestInfo, Action<JObject> onPrepared = null)
		{
			var info = new JObject
			{
				["Time"] = DateTime.Now.ToIsoString(true),
				["URL"] = new Uri(requestInfo.GetParameter("x-url")).GetURLPath(),
				["DeviceID"] = requestInfo.Session.DeviceID,
				["IP"] = requestInfo.Session.IP,
				["OSInfo"] = $"{Extensions.GetOSInfo(requestInfo.Session.AppAgent)} [{requestInfo.Session.AppAgent}]"
			};
			onPrepared?.Invoke(info);
			extras ??= (form.Extras ?? "{}").ToJson();
			var section = extras.Get<JToken>(name);
			if (section is JArray sectionArray)
				sectionArray.Add(info);
			else if (section is JObject sectionObject)
				extras[name] = new JArray(sectionObject, info);
			else
				extras[name] = info;
			form.Extras = extras.ToString(Formatting.Indented);
			form.Extras = string.IsNullOrWhiteSpace(form.Extras) || form.Extras == "{}" ? null : form.Extras;
			return form;
		}

		public static Form FillProperties(this Form form, RequestInfo requestInfo, ExpandoObject data, string excluded = null, Action<Form> onFilled = null)
		{
			data ??= requestInfo.GetBodyExpando();

			var details = form.Details;
			var notes = form.Notes;
			var tags = form.Tags;
			var extras = form.Extras;
			var extrasJson = (extras ?? "{}").ToJson() as JObject;

			var excludedProperties = (excluded ?? "Privileges").ToHashSet();
			if (requestInfo.ContainsKey("x-update-details"))
				excludedProperties.Add("Details");
			if (requestInfo.ContainsKey("x-update-notes"))
				excludedProperties.Add("Notes");
			if (requestInfo.ContainsKey("x-update-tags"))
				excludedProperties.Add("Tags");
			if (requestInfo.ContainsKey("x-update-extras"))
				excludedProperties.Add("Extras");

			form.Fill(data, excludedProperties, _ =>
			{
				if (requestInfo.ContainsKey("x-email-as-identity"))
					form.ID = form.Email?.GenerateUUID();
				else if (requestInfo.ContainsKey("x-phone-as-identity"))
					form.ID = form.Phone?.GenerateUUID();
				form.ID = string.IsNullOrWhiteSpace(form.ID) || !form.ID.IsValidUUID() ? UtilityService.NewUUID : form.ID;

				form.Compute(requestInfo, _ => form.Validate((name, value) =>
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
							form.Email = email.ToLower();
						else
							throw new InformationInvalidException("Email is invalid");
					}
				}));

				if (requestInfo.ContainsKey("x-normalize-name"))
					form.Name = (form.Name ?? "").GetCapitalizedWords();

				form.Title = string.IsNullOrWhiteSpace(form.Title) ? $"Request from {form.Name} ({form.Phone})" : form.Title;
				form.NormalizeHTMLs(out var _);

				if (requestInfo.ContainsKey("x-update-details"))
				{
					form.Details = $"{(string.IsNullOrWhiteSpace(details) ? "" : $"{details}\r\n--------------------\r\n")}{data.Get<string>("Details")}";
					form.Details = string.IsNullOrWhiteSpace(form.Details) ? null : form.Details;
				}

				if (requestInfo.ContainsKey("x-update-notes"))
				{
					form.Notes = $"{(string.IsNullOrWhiteSpace(notes) ? "" : $"{notes}\r\n--------------------\r\n")}{data.Get<string>("Notes")}";
					form.Notes = string.IsNullOrWhiteSpace(form.Notes) ? null : form.Notes;
				}

				if (requestInfo.ContainsKey("x-update-tags"))
					form.Tags = $"{(string.IsNullOrWhiteSpace(tags) ? "" : $"{tags};")}{data.Get<string>("Tags")}";
				form.Tags = string.IsNullOrWhiteSpace(form.Tags) ? null : form.Tags.ToList(";", true).Distinct(StringComparer.OrdinalIgnoreCase).Join(";");

				if (requestInfo.ContainsKey("x-update-extras"))
					try
					{
						((data.Get<string>("Extras") ?? "{}").ToJson() as JObject).ForEach(kvp =>
						{
							if (extrasJson[kvp.Key] is JArray sectionArray)
								sectionArray.Add(kvp.Value);
							else if (extrasJson[kvp.Key] is JObject sectionObject)
								extrasJson[kvp.Key] = new JArray(sectionObject, kvp.Value);
							else
								extrasJson[kvp.Key] = kvp.Value;
						});
					}
					finally
					{
						form.Extras = extrasJson.ToString(Formatting.Indented);
						form.Extras = string.IsNullOrWhiteSpace(form.Extras) || form.Extras == "{}" ? null : form.Extras;
					}
				else
					try
					{
						extrasJson = (form.Extras ?? "{}").ToJson() as JObject;
					}
					catch
					{
						form.Extras = extras;
						extrasJson = (form.Extras ?? "{}").ToJson() as JObject;
					}

				if (requestInfo.TryGetParameter("x-delete-extras", out var deleteExtras) && !string.IsNullOrWhiteSpace(deleteExtras))
					try
					{
						deleteExtras.ToList(";", true).ForEach(name => extrasJson.Remove(name));
					}
					finally
					{
						form.Extras = extrasJson.ToString(Formatting.Indented);
						form.Extras = string.IsNullOrWhiteSpace(form.Extras) || form.Extras == "{}" ? null : form.Extras;
					}
			});

			onFilled?.Invoke(form);
			return form;
		}

		public static FilterBys<Form> GetFormsFilter(string systemID, string repositoryID = null, string repositoryEntityID = null)
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
			if (Utility.IsCacheLogEnabled && form != null)
				await Utility.WriteLogAsync(correlationID, $"Clear related cache of a CMS form [{form.Title} - ID: {form.ID}]\r\n- {dataCacheKeys.Count} request keys => {dataCacheKeys.Join(", ")}", "Caches").ConfigureAwait(false);
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
			var verifyRequest = !isSystemAdministrator || !requestInfo.ContainsKey("x-dont-verify");

			var query = request.Get<string>("FilterBy.Query");

			var filter = request.Get<ExpandoObject>("FilterBy")?.ToFilterBy<Form>() as FilterBys<Form> ?? Filters<Form>.And();
			var sort = string.IsNullOrWhiteSpace(query) ? request.Get<ExpandoObject>("SortBy")?.ToSortBy<Form>() ?? Sorts<Form>.Descending("Created").ThenByAscending("Title") : null;

			var expression = await (requestInfo.GetParameter("x-expression") ?? requestInfo.GetParameter("x-expression-id") ?? requestInfo.GetParameter("expression-id") ?? requestInfo.GetParameter("ExpressionID") ?? requestInfo.GetParameter("object-extra-identity") ?? "").GetExpressionByIDAsync(cancellationToken).ConfigureAwait(false);
			if (expression != null)
			{
				filter = expression.GetFilterBy<Form>() as FilterBys<Form> ?? filter;
				sort = expression.GetSortBy<Form>() ?? sort;
			}

			var pagination = request.Get<ExpandoObject>("Pagination")?.GetPagination() ?? (-1, 0, 20, 1);
			var pageSize = pagination.PageSize;
			var pageNumber = pagination.PageNumber;

			var organizationID = expression?.SystemID ?? filter.GetValue("SystemID") ?? requestInfo.GetParameter("SystemID") ?? requestInfo.GetParameter("OrganizationID") ?? requestInfo.GetParameter("x-system-id");
			var organization = await (organizationID ?? "").GetOrganizationByIDAsync(cancellationToken).ConfigureAwait(false) ?? throw new InformationInvalidException("The organization is invalid");

			var moduleID = expression?.RepositoryID ?? filter.GetValue("RepositoryID") ?? requestInfo.GetParameter("RepositoryID") ?? requestInfo.GetParameter("ModuleID") ?? requestInfo.GetParameter("x-module-id");
			var module = await (moduleID ?? "").GetModuleByIDAsync(cancellationToken).ConfigureAwait(false);
			if (verifyRequest && ((module == null && string.IsNullOrWhiteSpace(query)) || (module != null && !organization.ID.IsEquals(module.SystemID))))
				throw new InformationInvalidException("The module is invalid");

			var contentTypeID = expression?.RepositoryEntityID ?? filter.GetValue("RepositoryEntityID") ?? requestInfo.GetParameter("RepositoryEntityID") ?? requestInfo.GetParameter("ContentTypeID") ?? requestInfo.GetParameter("x-content-type-id");
			var contentType = await (contentTypeID ?? "").GetContentTypeByIDAsync(cancellationToken).ConfigureAwait(false);
			if (verifyRequest && ((contentType == null && string.IsNullOrWhiteSpace(query) && expression?.ContentTypeDefinition == null) || (contentType != null && (!organization.ID.IsEquals(contentType.SystemID) || (module != null && !module.ID.IsEquals(contentType.RepositoryID))))))
				throw new InformationInvalidException("The content-type is invalid");

			// check permission
			var gotRights = isSystemAdministrator || requestInfo.Session.User.IsViewer(contentType.WorkingPrivileges, null, organization);
			if (!gotRights)
				throw new AccessDeniedException();

			// normalize filter
			if (filter == null || filter.Children == null || filter.Children.Count < 1)
				filter = FormProcessor.GetFormsFilter(organization.ID, module.ID, contentType.ID);

			if (verifyRequest)
			{
				if (filter.GetChild("SystemID") is not FilterBy<Form> filterBySystem)
					filter.Add(Filters<Form>.Equals("SystemID", organization.ID));
				else if (filterBySystem.Value == null)
					filterBySystem.Value = organization.ID;
			}

			if (module != null)
			{
				if (filter.GetChild("RepositoryID") is not FilterBy<Form> filterByRepository)
					filter.Add(Filters<Form>.Equals("RepositoryID", module.ID));
				else if (filterByRepository.Value == null)
					filterByRepository.Value = module.ID;
			}

			if (contentType != null)
			{
				if (filter.GetChild("RepositoryEntityID") is not FilterBy<Form> filterByRepositoryEntity)
					filter.Add(Filters<Form>.Equals("RepositoryEntityID", contentType.ID));
				else if (filterByRepositoryEntity.Value == null)
					filterByRepositoryEntity.Value = contentType.ID;
			}

			if (!requestInfo.Session.User.IsAuthenticated)
			{
				if (filter.GetChild("Status") is not FilterBy<Form> filterByStatus)
					filter.Add(Filters<Form>.Equals("Status", ApprovalStatus.Published.ToString()));
				else
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
			var results = await requestInfo.SearchAsync(query, filter, sort, pageSize, pageNumber, contentType?.ID, pagination.TotalRecords > -1 ? pagination.TotalRecords : -1, cancellationToken).ConfigureAwait(false);
			var totalRecords = results.Item1;
			var objects = results.Item2;

			// build response
			var totalPages = new Tuple<long, int>(totalRecords, pageSize).GetTotalPages();
			if (totalPages > 0 && pageNumber > totalPages)
				pageNumber = totalPages;

			var showURLs = requestInfo.GetParameter("ShowURLs") != null;
			var response = new JObject()
			{
				{ "FilterBy", filter.ToClientJson(query) },
				{ "SortBy", sort?.ToClientJson() },
				{ "Pagination", (totalRecords, totalPages, pageSize, pageNumber).GetPagination() },
				{ "Objects", objects.Select(@object => @object.ToJson(false)).ToJArray() }
			};

			// update cache
			if (string.IsNullOrWhiteSpace(query))
			{
				//await Utility.Cache.SetAsync(cacheKeyOfObjectsJson, response.ToString(Formatting.None), cancellationToken).ConfigureAwait(false);
				var cacheKeys = new[] { cacheKeyOfObjectsJson }.Concat(results.Item4).ToList();
				Task.WhenAll
				(
					Utility.Cache.SetAsync(cacheKeyOfObjectsJson, response.ToString(Formatting.None)),
					contentType != null ? Utility.Cache.AddSetMembersAsync(contentType.GetSetCacheKey(), cacheKeys) : Task.CompletedTask,
					Utility.IsCacheLogEnabled ? Utility.WriteLogAsync(requestInfo, $"Update cache when search CMS forms\r\n- Cache key of JSON: {cacheKeyOfObjectsJson}\r\n- Cache key of realated sets: {contentType?.GetSetCacheKey()}\r\n- Related cache keys: {cacheKeys.Join(", ")}", "Caches") : Task.CompletedTask
				).Execute();
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
			var gotRights = isSystemAdministrator || requestInfo.Session.User.IsContributor(contentType.WorkingPrivileges, null, organization);
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

			// prepare properties
			var form = await Form.CreateInstance(request).FillProperties(requestInfo, request, "Privileges,Created,CreatedID,LastModified,LastModifiedID", obj =>
			{
				obj.SystemID = organization.ID;
				obj.RepositoryID = module.ID;
				obj.RepositoryEntityID = contentType.ID;
				obj.ID = string.IsNullOrWhiteSpace(obj.ID) || !obj.ID.IsValidUUID() ? UtilityService.NewUUID : obj.ID;
				obj.Created = obj.LastModified = DateTime.Now;
				obj.CreatedID = obj.LastModifiedID = requestInfo.Session.User.IsAuthenticated ? requestInfo.Session.User.ID : null;
				obj.DeviceID = requestInfo.Session.DeviceID;
				obj.IPAddress = requestInfo.Session.IP;
				obj.Profiles = requestInfo.Session.User.IsAuthenticated ? new Dictionary<string, string> { ["vieapps"] = requestInfo.Session.User.ID } : null;
			}).NormalizeAsync(null, requestInfo, cancellationToken).ConfigureAwait(false);

			// create new
			try
			{
				await Form.CreateAsync(form, cancellationToken).ConfigureAwait(false);
			}
			catch (RepositoryOperationException ex)
			{
				if (ex.InnerException is InformationExistedException && requestInfo.ContainsKey("x-update-if-existed"))
				{
					form.Created = request.Get<DateTime>("Created");
					form.CreatedID = request.Get<string>("CreatedID");
					form.LastModified = request.Get<DateTime>("LastModified");
					form.LastModifiedID = request.Get<string>("LastModifiedID");
					form.DeviceID = request.Get<string>("DeviceID");
					form.IPAddress = request.Get<string>("IPAddress");
					await Form.UpdateAsync(form, false, cancellationToken).ConfigureAwait(false);
				}
				else
					throw;
			}
			catch (Exception)
			{
				throw;
			}

			// update cache
			Task.WhenAll
			(
				form.ClearRelatedCacheAsync(cancellationToken, requestInfo.CorrelationID),
				Utility.Cache.AddSetMemberAsync(form.ContentType.ObjectCacheKeys, form.GetCacheKey(), cancellationToken)
			).Execute();

			// send update message
			var response = form.ToJson();
			new UpdateMessage
			{
				Type = $"{requestInfo.ServiceName}#{form.GetObjectName()}#Create",
				DeviceID = "*",
				Data = response
			}.Send();

			// send notifications and fire a trigger
			await form.SendNotificationAsync("Create", form.ContentType.Notifications, ApprovalStatus.Draft, form.Status, requestInfo, cancellationToken).ConfigureAwait(false);
			var extras = (form.Extras ?? "{}").ToJson();
			var triggerURL = extras.Get<string>("OnChanged");
			if (!string.IsNullOrWhiteSpace(triggerURL))
			{
				triggerURL += triggerURL.IsContains("?") ? "&" : "?";
				triggerURL += $"x-identity={form.ID}&x-event=Created&x-status=${form.Status}&x-previous-status=${ApprovalStatus.Draft}";
				requestInfo.ProcessWebHookTriggerAsync(triggerURL, response.ToString(Formatting.None)).Execute(ex => Utility.WriteLogsAsync(null, null, "WebHooks", new List<string> { $"Error in trigger URL [{triggerURL}] => {ex.Message}" }, ex, requestInfo.CorrelationID));
			}

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
			var gotRights = isSystemAdministrator || requestInfo.Session.User.IsViewer(form.WorkingPrivileges, form.ContentType.WorkingPrivileges, form.Organization);
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

			// send notification and fire a trigger
			await form.SendNotificationAsync("Update", form.ContentType.Notifications, oldStatus, form.Status, requestInfo, cancellationToken).ConfigureAwait(false);
			var extras = (form.Extras ?? "{}").ToJson();
			var triggerURL = extras.Get<string>("OnChanged");
			if (!string.IsNullOrWhiteSpace(triggerURL))
			{
				triggerURL += triggerURL.IsContains("?") ? "&" : "?";
				triggerURL += $"x-identity={form.ID}&x-event=Updated&x-status=${form.Status}&x-previous-status=${oldStatus}";
				requestInfo.ProcessWebHookTriggerAsync(triggerURL, response.ToString(Formatting.None)).Execute(ex => Utility.WriteLogsAsync(null, null, "WebHooks", new List<string> { $"Error in trigger URL [{triggerURL}] => {ex.Message}" }, ex, requestInfo.CorrelationID));
			}

			// response
			return response;
		}

		internal static async Task<JObject> UpdateFormAsync(this RequestInfo requestInfo, bool isSystemAdministrator, CancellationToken cancellationToken)
		{
			var form = await Form.GetAsync<Form>(requestInfo.GetObjectIdentity() ?? "", cancellationToken).ConfigureAwait(false);
			if (form == null)
				throw new InformationNotFoundException();
			else if (form.Organization == null || form.Module == null || form.ContentType == null)
				throw new InformationInvalidException("The organization/module/form-type is invalid");

			var gotRights = isSystemAdministrator || requestInfo.Session.User.IsEditor(form.WorkingPrivileges, form.ContentType.WorkingPrivileges, form.Organization);
			if (!gotRights)
				gotRights = form.Status.Equals(ApprovalStatus.Draft) || form.Status.Equals(ApprovalStatus.Pending) || form.Status.Equals(ApprovalStatus.Rejected)
					? requestInfo.Session.User.ID.IsEquals(form.CreatedID)
					: requestInfo.Session.User.IsEditor(form.WorkingPrivileges, form.ContentType.WorkingPrivileges, form.Organization);
			if (!gotRights)
				throw new AccessDeniedException();

			var oldStatus = form.Status;
			await form.FillProperties(requestInfo, requestInfo.GetBodyExpando(), "ID,SystemID,RepositoryID,RepositoryEntityID,Privileges,Created,CreatedID,LastModified,LastModifiedID,Profiles", _ =>
			{
				form.LastModified = DateTime.Now;
				form.LastModifiedID = requestInfo.Session.User.ID;
			}).NormalizeAsync((form.Extras ?? "{}").ToJson(), requestInfo, cancellationToken).ConfigureAwait(false);
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
			var gotRights = isSystemAdministrator || requestInfo.Session.User.IsModerator(form.WorkingPrivileges, form.ContentType.WorkingPrivileges, form.Organization);
			if (!gotRights)
				gotRights = form.Status.Equals(ApprovalStatus.Draft) || form.Status.Equals(ApprovalStatus.Pending) || form.Status.Equals(ApprovalStatus.Rejected)
					? requestInfo.Session.User.ID.IsEquals(form.CreatedID) || requestInfo.Session.User.IsEditor(form.WorkingPrivileges, form.ContentType.WorkingPrivileges, form.Organization)
					: requestInfo.Session.User.IsModerator(form.WorkingPrivileges, form.ContentType.WorkingPrivileges, form.Organization);
			if (!gotRights)
				throw new AccessDeniedException();

			// delete
			return await form.DeleteAsync(requestInfo, true, true, cancellationToken).ConfigureAwait(false);
		}

		internal static async Task<JObject> DeleteAsync(this Form form, RequestInfo requestInfo, bool updateCache, bool sendUpdatingMessages, CancellationToken cancellationToken)
		{
			await requestInfo.DeleteFilesAsync(form.SystemID, form.RepositoryEntityID, form.ID, Utility.ValidationKey, cancellationToken).ConfigureAwait(false);
			await Form.DeleteAsync<Form>(form.ID, requestInfo.Session.User.ID, cancellationToken).ConfigureAwait(false);

			if (updateCache)
				Task.WhenAll
				(
					Utility.Cache.RemoveSetMemberAsync(form.ContentType.ObjectCacheKeys, form.GetCacheKey(), Utility.CancellationToken),
					form.ClearRelatedCacheAsync(Utility.CancellationToken, requestInfo.CorrelationID)
				).Execute();

			// send update messages
			var json = sendUpdatingMessages ? form.ToJson() : null;
			if (sendUpdatingMessages)
				new UpdateMessage
				{
					Type = $"{requestInfo.ServiceName}#{form.GetObjectName()}#Delete",
					DeviceID = "*",
					Data = json
				}.Send();

			// send notification and fire a trigger
			await form.SendNotificationAsync("Delete", form.ContentType?.Notifications, form.Status, form.Status, requestInfo, cancellationToken).ConfigureAwait(false);
			var extras = (form.Extras ?? "{}").ToJson();
			var triggerURL = extras.Get<string>("OnChanged");
			if (!string.IsNullOrWhiteSpace(triggerURL))
			{
				triggerURL += triggerURL.IsContains("?") ? "&" : "?";
				triggerURL += $"x-identity={form.ID}&x-event=Deleted&x-status=${form.Status}&x-previous-status=${form.Status}";
				requestInfo.ProcessWebHookTriggerAsync(triggerURL, (json ?? form.ToJson()).ToString(Formatting.None)).Execute(ex => Utility.WriteLogsAsync(null, null, "WebHooks", new List<string> { $"Error in trigger URL [{triggerURL}] => {ex.Message}" }, ex, requestInfo.CorrelationID));
			}

			// delete
			return json;
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

		internal static async Task<JObject> SyncFormAsync(this RequestInfo requestInfo, CancellationToken cancellationToken, bool sendNotifications = false, bool dontCreateNewVersion = false)
		{
			var @event = requestInfo.GetParameter("event") ?? requestInfo.GetParameter("x-original-event");
			if (string.IsNullOrWhiteSpace(@event) || !@event.IsEquals("Delete"))
				@event = "Update";

			var request = requestInfo.GetBodyExpando();
			var identity = requestInfo.ContainsKey("x-email-as-identity")
				? request.Get<string>("Email")?.GenerateUUID()
				: requestInfo.ContainsKey("x-phone-as-identity")
					? request.Get<string>("Phone")?.GenerateUUID()
					: null;

			var form = await Form.GetAsync<Form>(identity ?? request.Get<string>("ID"), cancellationToken).ConfigureAwait(false);
			var oldStatus = form != null ? form.Status : ApprovalStatus.Pending;

			if (!@event.IsEquals("Delete"))
			{
				if (form == null)
				{
					form = await ObjectService.CreateInstance<Form>().FillProperties(requestInfo, request, null, obj =>
					{
						obj.Created = request.Has("Created") ? request.Get<DateTime>("Created") : DateTime.Now;
						obj.CreatedID = request.Get("CreatedID", requestInfo.Session.User.IsAuthenticated ? requestInfo.Session.User.ID : null);
						obj.LastModified = request.Has("LastModified") ? request.Get<DateTime>("LastModified") : DateTime.Now;
						obj.LastModifiedID = request.Get("LastModifiedID", requestInfo.Session.User.IsAuthenticated ? requestInfo.Session.User.ID : null);
						obj.DeviceID = string.IsNullOrWhiteSpace(obj.DeviceID) ? requestInfo.Session.DeviceID : obj.DeviceID;
						obj.IPAddress = string.IsNullOrWhiteSpace(obj.IPAddress) ? requestInfo.Session.IP : obj.IPAddress;
					}).NormalizeAsync(null, requestInfo, cancellationToken).ConfigureAwait(false);
					try
					{
						await Form.CreateAsync(form, cancellationToken).ConfigureAwait(false);
					}
					catch (RepositoryOperationException ex)
					{
						if (ex.InnerException is InformationExistedException && requestInfo.ContainsKey("x-update-if-existed"))
						{
							form.Created = request.Has("Created") ? request.Get<DateTime>("Created") : form.Created;
							form.CreatedID = request.Get("CreatedID", form.CreatedID);
							form.LastModified = request.Has("LastModified") ? request.Get<DateTime>("LastModified") : form.LastModified;
							form.LastModifiedID = request.Get("LastModifiedID", form.LastModifiedID);
							form.DeviceID = request.Get("DeviceID", form.DeviceID);
							form.IPAddress = request.Get("IPAddress", form.IPAddress);
							await Form.UpdateAsync(form, false, cancellationToken).ConfigureAwait(false);
						}
						else
							throw;
					}
					catch (Exception)
					{
						throw;
					}
				}
				else
				{
					await form.FillProperties(requestInfo, request).NormalizeAsync((form.Extras ?? "{}").ToJson(), requestInfo, cancellationToken).ConfigureAwait(false);
					await Form.UpdateAsync(form, dontCreateNewVersion, cancellationToken).ConfigureAwait(false);
				}
			}
			else if (form != null)
				await form.DeleteAsync(requestInfo, false, false, cancellationToken).ConfigureAwait(false);

			// stop if has no info
			if (form == null)
				return new JObject();

			// update cache
			await form.ClearRelatedCacheAsync(cancellationToken, requestInfo.CorrelationID).ConfigureAwait(false);
			if (@event.IsEquals("Delete"))
				await Utility.Cache.RemoveSetMemberAsync(form.ContentType.ObjectCacheKeys, form.GetCacheKey(), cancellationToken).ConfigureAwait(false);
			else
				await Utility.Cache.AddSetMemberAsync(form.ContentType.ObjectCacheKeys, form.GetCacheKey(), cancellationToken).ConfigureAwait(false);

			// send notifications
			if (sendNotifications)
				await form.SendNotificationAsync(@event, form.ContentType.Notifications, oldStatus, form.Status, requestInfo, cancellationToken).ConfigureAwait(false);

			// response
			var response = form.ToJson();
			new UpdateMessage
			{
				Type = $"{requestInfo.ServiceName}#{form.GetObjectName()}#{@event}",
				Data = response,
				DeviceID = "*"
			}.Send();
			return response;
		}
	}
}
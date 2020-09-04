#region Related components
using System;
using System.Linq;
using System.Xml.Linq;
using System.Dynamic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Concurrent;
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
		public static Item CreateItemInstance(this ExpandoObject data, string excluded = null, Action<Item> onCompleted = null)
			=> Item.CreateInstance(data, excluded?.ToHashSet(), item =>
			{
				item.NormalizeHTMLs();
				item.Tags = item.Tags?.Replace(";", ",").ToList(",", true).Where(tag => !string.IsNullOrWhiteSpace(tag)).Join(",");
				item.Tags = string.IsNullOrWhiteSpace(item.Tags) ? null : item.Tags;
				onCompleted?.Invoke(item);
			});

		public static Item UpdateItemInstance(this Item item, ExpandoObject data, string excluded = null, Action<Item> onCompleted = null)
			=> item.Fill(data, excluded?.ToHashSet(), _ =>
			{
				item.NormalizeHTMLs();
				item.Tags = item.Tags?.Replace(";", ",").ToList(",", true).Where(tag => !string.IsNullOrWhiteSpace(tag)).Join(",");
				item.Tags = string.IsNullOrWhiteSpace(item.Tags) ? null : item.Tags;
				onCompleted?.Invoke(item);
			});

		public static IFilterBy<Item> GetItemsFilter(this string systemID, string repositoryID = null, string repositoryEntityID = null)
		{
			var filter = Filters<Item>.And();
			if (!string.IsNullOrWhiteSpace(systemID))
				filter.Add(Filters<Item>.Equals("SystemID", systemID));
			if (!string.IsNullOrWhiteSpace(repositoryID))
				filter.Add(Filters<Item>.Equals("RepositoryID", repositoryID));
			if (!string.IsNullOrWhiteSpace(repositoryEntityID))
				filter.Add(Filters<Item>.Equals("RepositoryEntityID", repositoryEntityID));
			return filter;
		}

		static Task ClearRelatedCacheAsync(this Item item, SortBy<Item> sort, CancellationToken cancellationToken = default)
			=> Task.WhenAll
			(
				Utility.Cache.RemoveAsync(Extensions.GetRelatedCacheKeys(item.GetCacheKey()), cancellationToken),
				Utility.Cache.RemoveAsync(Extensions.GetRelatedCacheKeys(item.SystemID.GetItemsFilter(item.RepositoryID, item.RepositoryEntityID), sort ?? Sorts<Item>.Descending("Created").ThenByAscending("Title")), cancellationToken),
				Utility.RTUService.SendClearCacheRequestAsync(item.ContentType?.ID, Extensions.GetCacheKey<Item>(), cancellationToken)
			);

		static async Task<Tuple<long, List<Item>, JToken>> SearchAsync(this RequestInfo requestInfo, string query, IFilterBy<Item> filter, SortBy<Item> sort, int pageSize, int pageNumber, string contentTypeID = null, long totalRecords = -1, CancellationToken cancellationToken = default, bool searchThumbnails = true, string cacheKeyPrefix = null)
		{
			// count
			totalRecords = totalRecords > -1
				? totalRecords
				: string.IsNullOrWhiteSpace(query)
					? await Item.CountAsync(filter, contentTypeID, string.IsNullOrWhiteSpace(cacheKeyPrefix) ? Extensions.GetCacheKeyOfTotalObjects(filter, sort) : Extensions.GetCacheKeyOfTotalObjects<Item>(cacheKeyPrefix), cancellationToken).ConfigureAwait(false)
					: await Item.CountAsync(query, filter, contentTypeID, cancellationToken).ConfigureAwait(false);

			// search objects
			var objects = totalRecords > 0
				? string.IsNullOrWhiteSpace(query)
					? await Item.FindAsync(filter, sort, pageSize, pageNumber, contentTypeID, string.IsNullOrWhiteSpace(cacheKeyPrefix) ? Extensions.GetCacheKey(filter, sort, pageSize, pageNumber) : Extensions.GetCacheKey<Item>(cacheKeyPrefix, pageSize, pageNumber), cancellationToken).ConfigureAwait(false)
					: await Item.SearchAsync(query, filter, pageSize, pageNumber, contentTypeID, cancellationToken).ConfigureAwait(false)
				: new List<Item>();

			// search thumbnails
			requestInfo.Header["x-as-attachments"] = "true";
			var thumbnails = objects.Count < 1 || !searchThumbnails
				? null
				: objects.Count == 1
					? await requestInfo.GetThumbnailsAsync(objects[0].ID, objects[0].Title.Url64Encode(), Utility.ValidationKey, cancellationToken).ConfigureAwait(false)
					: await requestInfo.GetThumbnailsAsync(objects.Select(@object => @object.ID).Join(","), objects.ToJObject("ID", @object => new JValue(@object.Title.Url64Encode())).ToString(Formatting.None), Utility.ValidationKey, cancellationToken).ConfigureAwait(false);

			// page size to clear related cached
			await Utility.SetCacheOfPageSizeAsync(filter, sort, cacheKeyPrefix, pageSize, cancellationToken).ConfigureAwait(false);

			// return the results
			return new Tuple<long, List<Item>, JToken>(totalRecords, objects, thumbnails);
		}

		internal static async Task<JObject> SearchItemsAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, CancellationToken cancellationToken = default)
		{
			// prepare
			var request = requestInfo.GetRequestExpando();
			var query = request.Get<string>("FilterBy.Query");
			var filter = request.Get<ExpandoObject>("FilterBy")?.ToFilterBy<Item>() ?? Filters<Item>.And();
			var sort = string.IsNullOrWhiteSpace(query) ? request.Get<ExpandoObject>("SortBy")?.ToSortBy<Item>() ?? Sorts<Item>.Descending("Created").ThenByAscending("Title") : null;
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
			var gotRights = isSystemAdministrator || requestInfo.Session.User.ID.IsEquals(organization.OwnerID) || requestInfo.Session.User.IsViewer(contentType.WorkingPrivileges);
			if (!gotRights)
				throw new AccessDeniedException();

			// normalize filter
			filter = filter == null || !(filter is FilterBys<Item>) || (filter as FilterBys<Item>).Children == null || (filter as FilterBys<Item>).Children.Count < 1
				? organization.ID.GetItemsFilter(module.ID, contentType.ID)
				: filter.Prepare(requestInfo);

			// process cached
			var json = string.IsNullOrWhiteSpace(query) ? await Utility.Cache.GetAsync<string>(Extensions.GetCacheKeyOfObjectsJson(filter, sort, pageSize, pageNumber), cancellationToken).ConfigureAwait(false) : null;
			if (!string.IsNullOrWhiteSpace(json))
				return JObject.Parse(json);

			// search if has no cache
			var results = await requestInfo.SearchAsync(query, filter, sort, pageSize, pageNumber, contentType.ID, pagination.Item1 > -1 ? pagination.Item1 : -1, cancellationToken).ConfigureAwait(false);
			var totalRecords = results.Item1;
			var objects = results.Item2;
			var thumbnails = results.Item3;

			// build response
			var totalPages = new Tuple<long, int>(totalRecords, pageSize).GetTotalPages();
			if (totalPages > 0 && pageNumber > totalPages)
				pageNumber = totalPages;
			pagination = new Tuple<long, int, int, int>(totalRecords, totalPages, pageSize, pageNumber);

			var response = new JObject()
			{
				{ "FilterBy", filter.ToClientJson(query) },
				{ "SortBy", sort?.ToClientJson() },
				{ "Pagination", pagination.GetPagination() },
				{ "Objects", objects.Select(@object => @object.ToJson(false, cjson => cjson["Thumbnails"] = thumbnails == null ? null : objects.Count == 1 ? thumbnails : thumbnails[@object.ID])).ToJArray() }
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

		internal static async Task<JObject> CreateItemAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, CancellationToken cancellationToken = default)
		{
			// prepare
			var request = requestInfo.GetBodyExpando();

			var organizationID = request.Get<string>("SystemID") ?? requestInfo.GetParameter("SystemID") ?? requestInfo.GetParameter("x-system-id");
			var organization = await (organizationID ?? "").GetOrganizationByIDAsync(cancellationToken).ConfigureAwait(false);
			if (organization == null)
				throw new InformationInvalidException("The organization is invalid");

			var moduleID = request.Get<string>("RepositoryID") ?? requestInfo.GetParameter("RepositoryID") ?? requestInfo.GetParameter("x-module-id");
			var module = await (moduleID ?? "").GetModuleByIDAsync(cancellationToken).ConfigureAwait(false);
			if (module == null || !module.SystemID.IsEquals(organization.ID))
				throw new InformationInvalidException("The module is invalid");

			var contentTypeID = request.Get<string>("RepositoryEntityID") ?? requestInfo.GetParameter("RepositoryEntityID") ?? requestInfo.GetParameter("x-content-type-id");
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
			await Item.CreateAsync(item, cancellationToken).ConfigureAwait(false);
			await Utility.Cache.SetAsync($"e:{item.ContentTypeID}#a:{item.Alias.GenerateUUID()}".GetCacheKey<Item>(), item.ID, cancellationToken).ConfigureAwait(false);
			item.ClearRelatedCacheAsync(null, cancellationToken).Run();

			// send update message
			var response = item.ToJson();
			response["Thumbnails"] = await requestInfo.GetThumbnailsAsync(item.ID, item.Title.Url64Encode(), Utility.ValidationKey, cancellationToken).ConfigureAwait(false);
			response["Attachments"] = await requestInfo.GetAttachmentsAsync(item.ID, item.Title.Url64Encode(), Utility.ValidationKey, cancellationToken).ConfigureAwait(false);
			await Utility.RTUService.SendUpdateMessageAsync(new UpdateMessage
			{
				Type = $"{requestInfo.ServiceName}#{item.GetObjectName()}#Create",
				DeviceID = "*",
				ExcludedDeviceID = requestInfo.Session.DeviceID,
				Data = response
			}, cancellationToken).ConfigureAwait(false);

			// send notification
			item.SendNotificationAsync("Create", item.ContentType.Notifications, ApprovalStatus.Draft, item.Status, requestInfo, cancellationToken).Run();

			// response
			return response;
		}

		internal static async Task<JObject> GetItemAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, CancellationToken cancellationToken = default)
		{
			// prepare
			var identity = requestInfo.GetObjectIdentity() ?? "";
			var item = await (identity.IsValidUUID() ? Item.GetAsync<Item>(identity, cancellationToken) : Item.GetItemByAliasAsync(requestInfo.GetParameter("RepositoryEntityID") ?? requestInfo.GetParameter("x-content-type-id"), identity, cancellationToken)).ConfigureAwait(false);
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

			// send update message
			var response = item.ToJson();
			response["Thumbnails"] = await requestInfo.GetThumbnailsAsync(item.ID, item.Title.Url64Encode(), Utility.ValidationKey, cancellationToken).ConfigureAwait(false);
			response["Attachments"] = await requestInfo.GetAttachmentsAsync(item.ID, item.Title.Url64Encode(), Utility.ValidationKey, cancellationToken).ConfigureAwait(false);
			await Utility.RTUService.SendUpdateMessageAsync(new UpdateMessage
			{
				Type = $"{requestInfo.ServiceName}#{item.GetObjectName()}#Update",
				DeviceID = "*",
				ExcludedDeviceID = requestInfo.Session.DeviceID,
				Data = response
			}, cancellationToken).ConfigureAwait(false);

			// response
			return response;
		}

		internal static async Task<JObject> UpdateItemAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, CancellationToken cancellationToken = default)
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
			var oldStatus = item.Status;
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
			await Item.UpdateAsync(item, requestInfo.Session.User.ID, cancellationToken).ConfigureAwait(false);
			await Utility.Cache.SetAsync($"e:{item.ContentTypeID}#a:{item.Alias.GenerateUUID()}".GetCacheKey<Item>(), item.ID, cancellationToken).ConfigureAwait(false);
			item.ClearRelatedCacheAsync(null, cancellationToken).Run();

			// send update message
			var response = item.ToJson();
			response["Thumbnails"] = await requestInfo.GetThumbnailsAsync(item.ID, item.Title.Url64Encode(), Utility.ValidationKey, cancellationToken).ConfigureAwait(false);
			response["Attachments"] = await requestInfo.GetAttachmentsAsync(item.ID, item.Title.Url64Encode(), Utility.ValidationKey, cancellationToken).ConfigureAwait(false);
			await Utility.RTUService.SendUpdateMessageAsync(new UpdateMessage
			{
				Type = $"{requestInfo.ServiceName}#{item.GetObjectName()}#Update",
				DeviceID = "*",
				ExcludedDeviceID = requestInfo.Session.DeviceID,
				Data = response
			}, cancellationToken).ConfigureAwait(false);

			// send notification
			item.SendNotificationAsync("Update", item.ContentType.Notifications, oldStatus, item.Status, requestInfo, cancellationToken).Run();

			// response
			return response;
		}

		internal static async Task<JObject> DeleteItemAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, CancellationToken cancellationToken = default)
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

			// delete files
			await requestInfo.DeleteFilesAsync(item.SystemID, item.RepositoryEntityID, item.ID, Utility.ValidationKey, cancellationToken).ConfigureAwait(false);

			// delete
			await Item.DeleteAsync<Item>(item.ID, requestInfo.Session.User.ID, cancellationToken).ConfigureAwait(false);
			item.ClearRelatedCacheAsync(null, cancellationToken).Run();

			// send update message
			var response = item.ToJson();
			await Utility.RTUService.SendUpdateMessageAsync(new UpdateMessage
			{
				Type = $"{requestInfo.ServiceName}#{item.GetObjectName()}#Delete",
				DeviceID = "*",
				ExcludedDeviceID = requestInfo.Session.DeviceID,
				Data = response
			}, cancellationToken).ConfigureAwait(false);

			// send notification
			item.SendNotificationAsync("Delete", item.ContentType.Notifications, item.Status, item.Status, requestInfo, cancellationToken).Run();

			// response
			return response;
		}

		internal static async Task<JObject> GenerateAsync(RequestInfo requestInfo, bool isSystemAdministrator = false, CancellationToken cancellationToken = default)
		{
			// prepare
			var requestJson = requestInfo.GetBodyJson();

			var organizationJson = requestJson.Get("Organization", new JObject());
			var moduleJson = requestJson.Get("Module", new JObject());
			var contentTypeJson = requestJson.Get("ContentType", new JObject());
			var expressionJson = requestJson.Get("Expression", new JObject());

			var desktopsJson = requestJson.Get("Desktops", new JObject());
			var optionsJson = requestJson.Get("Options", new JObject());

			var paginationJson = requestJson.Get("Pagination", new JObject());
			var pageSize = paginationJson.Get<int>("PageSize", 7);
			var pageNumber = paginationJson.Get<int>("PageNumber", 1);
			var showPageLinks = paginationJson.Get<bool>("ShowPageLinks", true);
			var numberOfPageLinks = paginationJson.Get<int>("NumberOfPageLinks", 7);

			var contentTypeID = contentTypeJson.Get<string>("ID");
			var cultureInfo = CultureInfo.GetCultureInfo(requestJson.Get("Language", "vi-VN"));
			var action = requestJson.Get<string>("Action");
			var isList = string.IsNullOrWhiteSpace(action) || "List".IsEquals(action);

			var desktop = desktopsJson.Get<string>("Specified");
			desktop = !string.IsNullOrWhiteSpace(desktop) ? desktop : desktopsJson.Get<string>("ContentType");
			desktop = !string.IsNullOrWhiteSpace(desktop) ? desktop : desktopsJson.Get<string>("Module");
			desktop = !string.IsNullOrWhiteSpace(desktop) ? desktop : desktopsJson.Get<string>("Default");

			XElement data;
			JObject pagination, seoInfo, filterBy = null, sortBy = null;
			string coverURI = null;

			// generate list
			if (isList)
			{
				// check permission
				var contentType = await (contentTypeID ?? "").GetContentTypeByIDAsync(cancellationToken).ConfigureAwait(false);
				var gotRights = isSystemAdministrator || requestInfo.Session.User.IsViewer(contentType?.WorkingPrivileges);
				if (!gotRights)
				{
					var organization = contentType?.Organization ?? await organizationJson.Get<string>("ID", "").GetOrganizationByIDAsync(cancellationToken).ConfigureAwait(false);
					gotRights = requestInfo.Session.User.ID.IsEquals(organization?.OwnerID);
				}
				if (!gotRights)
					throw new AccessDeniedException();

				// prepare filtering expression
				if (!(expressionJson.Get<JObject>("FilterBy").ToFilter<Item>() is FilterBys<Item> filter) || filter.Children == null || filter.Children.Count < 1)
					filter = Filters<Item>.And(
						Filters<Item>.Equals("SystemID", "@request.Body(Organization.ID)"),
						Filters<Item>.Equals("RepositoryID", "@request.Body(Module.ID)"),
						Filters<Item>.Equals("RepositoryEntityID", "@request.Body(ContentType.ID)"),
						Filters<Item>.Equals("Status", ApprovalStatus.Published.ToString())
					);

				if (filter.GetChild("RepositoryEntityID") == null && contentType != null)
					filter.Add(Filters<Item>.Equals("RepositoryEntityID", contentType.ID));

				if (filter.GetChild("Status") == null)
					filter.Add(Filters<Item>.Equals("Status", ApprovalStatus.Published.ToString()));

				filterBy = new JObject
				{
					{ "API", filter.ToJson().ToString(Formatting.None) },
				};
				filter.Prepare(requestInfo);
				filterBy["App"] = filter.ToClientJson().ToString(Formatting.None);

				// prepare sorting expression
				var sort = expressionJson.Get<JObject>("SortBy")?.ToSort<Item>() ?? Sorts<Item>.Descending("Created");
				sortBy = new JObject
				{
					{ "API", sort.ToJson().ToString(Formatting.None) },
					{ "App", sort.ToClientJson().ToString(Formatting.None) }
				};

				// get XML from cache
				var cacheKeyPrefix = requestJson.GetCacheKeyPrefix();
				var cacheKey = cacheKeyPrefix != null ? Extensions.GetCacheKeyOfObjectsXml<Item>(cacheKeyPrefix, pageSize, pageNumber) : null;
				var xml = cacheKey != null ? await Utility.Cache.GetAsync<string>(cacheKey, cancellationToken).ConfigureAwait(false) : null;

				JToken thumbnails = null;
				long totalRecords = 0;

				// search and build XML if has no cache
				if (string.IsNullOrWhiteSpace(xml))
				{
					// search
					var results = await requestInfo.SearchAsync(null, filter, sort, pageSize, pageNumber, contentTypeID, -1, cancellationToken, optionsJson.Get<bool>("ShowThumbnail", true), cacheKeyPrefix).ConfigureAwait(false);
					totalRecords = results.Item1;
					var objects = results.Item2;
					thumbnails = results.Item3;

					// generate xml
					data = XElement.Parse("<Data/>");
					objects.ForEach(@object => data.Add(@object.ToXml(false, cultureInfo, element =>
					{
						if (!string.IsNullOrWhiteSpace(@object.Summary))
							element.Element("Summary").Value = @object.Summary.Replace("\r", "").Replace("\n", "<br/>");
						element.Add(new XElement("URL", @object.GetURL(desktop)));
						element.Add(new XElement("ThumbnailURL", thumbnails?.GetThumbnailURL(@object.ID)));
					})));

					// update XML into cache
					if (cacheKey != null)
						await Utility.Cache.SetAsync(cacheKey, data.ToString(SaveOptions.DisableFormatting), cancellationToken).ConfigureAwait(false);
				}
				else
				{
					data = XElement.Parse(xml);
					totalRecords = await Content.CountAsync(filter, contentTypeID, true, Extensions.GetCacheKeyOfTotalObjects<Item>(cacheKeyPrefix), 0, cancellationToken).ConfigureAwait(false);
				}

				// prepare pagination
				var totalPages = new Tuple<long, int>(totalRecords, pageSize).GetTotalPages();
				if (totalPages > 0 && pageNumber > totalPages)
					pageNumber = totalPages;
				pagination = Utility.GeneratePagination(totalRecords, totalPages, pageSize, pageNumber, $"~/{desktop ?? "-default"}/{contentType?.Title.GetANSIUri() ?? "-"}" + "/{{pageNumber}}" + $"{(organizationJson.Get<bool>("AlwaysUseHtmlSuffix", true) ? ".html" : "")}", showPageLinks, numberOfPageLinks);

				// prepare SEO info
				seoInfo = new JObject
				{
					{ "Title", contentType?.Title },
					{ "Description", contentType?.Description }
				};
			}

			// generate details
			else
			{
				// get the requested object
				var @object = await Item.GetItemByAliasAsync(contentTypeID, requestJson.Get<string>("ContentIdentity"), cancellationToken).ConfigureAwait(false);
				if (@object == null)
					throw new InformationNotFoundException();
				else if (@object.Organization == null || @object.Module == null || @object.ContentType == null)
					throw new InformationInvalidException("The organization/module/content-type is invalid");

				// check permission
				var gotRights = isSystemAdministrator || requestInfo.Session.User.ID.IsEquals(@object.Organization.OwnerID) || @object.Status.Equals(ApprovalStatus.Published)
					? requestInfo.Session.User.IsViewer(@object.WorkingPrivileges)
					: requestInfo.Session.User.ID.IsEquals(@object.CreatedID) || requestInfo.Session.User.IsEditor(@object.WorkingPrivileges);
				if (!gotRights)
					throw new AccessDeniedException();

				var showThumbnails = optionsJson.Get<bool>("ShowThumbnail", optionsJson.Get<bool>("ShowThumbnails", false));
				var showAttachments = optionsJson.Get<bool>("ShowAttachments", false);
				var showOthers = optionsJson.Get<bool>("ShowOthers", false);

				// get other contents
				Task<List<Item>> newersTask, oldersTask;
				if (showOthers)
				{
					var numberOfOthers = optionsJson.Get<int>("NumberOfOthers", 10) / 2;

					newersTask = Item.FindAsync(Filters<Item>.And(
						Filters<Item>.Equals("RepositoryEntityID", "@request.Body(ContentType.ID)"),
						Filters<Item>.Equals("Status", ApprovalStatus.Published.ToString()),
						Filters<Item>.GreaterOrEquals("Created", @object.Created)
					).Prepare(requestInfo), null, numberOfOthers, 1, contentTypeID, null, cancellationToken);

					oldersTask = Item.FindAsync(Filters<Item>.And(
						Filters<Item>.Equals("RepositoryEntityID", "@request.Body(ContentType.ID)"),
						Filters<Item>.Equals("Status", ApprovalStatus.Published.ToString()),
						Filters<Item>.LessThanOrEquals("Created", @object.Created)
					).Prepare(requestInfo), null, numberOfOthers, 1, contentTypeID, null, cancellationToken);
				}
				else
				{
					newersTask = Task.FromResult(new List<Item>());
					oldersTask = Task.FromResult(new List<Item>());
				}

				// get files
				var thumbnailsTask = showThumbnails ? requestInfo.GetThumbnailsAsync(@object.ID, @object.Title.Url64Encode(), Utility.ValidationKey, cancellationToken) : Task.FromResult<JToken>(new JArray());
				var attachmentsTask = showAttachments ? requestInfo.GetAttachmentsAsync(@object.ID, @object.Title.Url64Encode(), Utility.ValidationKey, cancellationToken) : Task.FromResult<JToken>(new JArray());

				// wait for all tasks are completed
				await Task.WhenAll(newersTask, oldersTask, thumbnailsTask, attachmentsTask).ConfigureAwait(false);

				var others = new List<Item>();
				JToken otherThumbnails = null;
				if (showOthers)
				{
					newersTask.Result.ForEach(other => others.Add(other));
					oldersTask.Result.ForEach(other => others.Add(other));
					others = others.Where(other => other.ID != @object.ID).OrderByDescending(other => other.Created).ToList();
					requestInfo.Header["x-as-attachments"] = "true";
					otherThumbnails = others.Count < 1
						? null
						: others.Count == 1
							? await requestInfo.GetThumbnailsAsync(others[0].ID, others[0].Title.Url64Encode(), Utility.ValidationKey, cancellationToken).ConfigureAwait(false)
							: await requestInfo.GetThumbnailsAsync(others.Select(obj => obj.ID).Join(","), others.ToJObject("ID", obj => new JValue(obj.Title.Url64Encode())).ToString(Formatting.None), Utility.ValidationKey, cancellationToken).ConfigureAwait(false);
				}

				// generate XML
				data = XElement.Parse("<Data/>");
				data.Add(@object.ToXml(false, cultureInfo, xml =>
				{
					xml.NormalizeHTMLs(@object);

					if (!string.IsNullOrWhiteSpace(@object.Tags))
					{
						var tagsXml = xml.Element("Tags");
						tagsXml.Value = "";
						@object.Tags.ToArray(",", true).ForEach(tag => tagsXml.Add(new XElement("Tag", tag)));
					}

					if (!string.IsNullOrWhiteSpace(@object.Summary))
						xml.Element("Summary").Value = @object.Summary.Replace("\r", "").Replace("\n", "<br/>");

					xml.Add(new XElement("URL", @object.GetURL(desktop)));

					if (showThumbnails)
					{
						var thumbnails = new XElement("Thumbnails");
						(thumbnailsTask.Result as JArray)?.ForEach(thumbnail => thumbnails.Add(new XElement("Thumbnail", thumbnail.Get<string>("URI"))));
						xml.Add(thumbnails);
					}

					if (showAttachments)
					{
						var attachments = new XElement("Attachments");
						(attachmentsTask.Result as JArray)?.Select(attachment => new JObject
						{
							{ "Title", attachment["Title"] },
							{ "Filename", attachment["Filename"] },
							{ "Size", attachment["Size"] },
							{ "ContentType", attachment["ContentType"] },
							{ "Downloads", attachment["Downloads"] },
							{ "URIs", attachment["URIs"] }
						}).ForEach(attachment => attachments.Add(attachment.ToXml("Attachment", x => x.Element("Size").UpdateNumber(false, cultureInfo))));
						xml.Add(attachments);
					}

					if (showOthers)
					{
						var othersXml = new XElement("Others");
						others.ForEach(other => othersXml.Add(other.ToXml(false, cultureInfo, otherXml =>
						{
							otherXml.Add(new XElement("URL", other.GetURL(desktop)));
							otherXml.Add(new XElement("ThumbnailURL", otherThumbnails?.GetThumbnailURL(other.ID)));
						})));
						xml.Add(othersXml);
					}
				}));

				// build others
				pagination = Utility.GeneratePagination(1, 1, 0, pageNumber, @object.GetURL(desktop, true), showPageLinks, numberOfPageLinks);
				seoInfo = new JObject
				{
					{ "Title", @object.Title },
					{ "Description", @object.Summary },
					{ "Keywords", @object.Tags }
				};
				coverURI = (thumbnailsTask.Result as JArray)?.First()?.Get<string>("URI");
			}

			// response
			return new JObject
			{
				{ "Data", data.ToString(SaveOptions.DisableFormatting) },
				{ "Pagination", pagination },
				{ "FilterBy", filterBy },
				{ "SortBy", sortBy },
				{ "SEOInfo", seoInfo },
				{ "CoverURI", coverURI }
			};
		}

		internal static async Task<JObject> SyncItemAsync(this RequestInfo requestInfo, CancellationToken cancellationToken = default)
		{
			var data = requestInfo.GetBodyExpando();
			var item = await Item.GetAsync<Item>(data.Get<string>("ID"), cancellationToken).ConfigureAwait(false);
			if (item == null)
			{
				item = Item.CreateInstance(data);
				await Item.CreateAsync(item, cancellationToken).ConfigureAwait(false);
			}
			else
			{
				item.Fill(data);
				await Item.UpdateAsync(item, true, cancellationToken).ConfigureAwait(false);
			}

			// send update messages
			var json = item.ToJson();
			var objectName = item.GetObjectName();
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
				{ "ID", item.ID },
				{ "Type", objectName }
			};
		}
	}
}
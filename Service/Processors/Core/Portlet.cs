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
	public static class PortletProcessor
	{
		public static Portlet CreatePortletInstance(this ExpandoObject requestBody, string excluded = null, Action<Portlet> onCompleted = null)
			=> requestBody.Copy<Portlet>(excluded?.ToHashSet(), portlet =>
			{
				portlet.TrimAll();
				portlet.Normalize();
				onCompleted?.Invoke(portlet);
			});

		public static Portlet UpdatePortletInstance(this Portlet portlet, ExpandoObject requestBody, string excluded = null, Action<Portlet> onCompleted = null)
		{
			portlet.CopyFrom(requestBody, excluded?.ToHashSet());
			portlet.TrimAll();
			portlet.Normalize();
			onCompleted?.Invoke(portlet);
			return portlet;
		}

		public static List<Portlet> FindPortlets(this string desktopID)
			=> string.IsNullOrWhiteSpace(desktopID)
				? new List<Portlet>()
				: Portlet.Find(Filters<Portlet>.Equals("DesktopID", desktopID), Sorts<Portlet>.Ascending("Zone").ThenByAscending("OrderIndex"), 0, 1, null);

		public static Task<List<Portlet>> FindPortletsAsync(this string desktopID, CancellationToken cancellationToken = default)
			=> string.IsNullOrWhiteSpace(desktopID)
				? Task.FromResult(new List<Portlet>())
				: Portlet.FindAsync(Filters<Portlet>.Equals("DesktopID", desktopID), Sorts<Portlet>.Ascending("Zone").ThenByAscending("OrderIndex"), 0, 1, null, cancellationToken);

		public static List<Portlet> FindMappingPortlets(this Portlet portlet)
			=> string.IsNullOrWhiteSpace(portlet?.ID)
				? new List<Portlet>()
				: Portlet.Find(Filters<Portlet>.Equals("OriginalPortletID", portlet.ID), null, 0, 1, null);

		public static Task<List<Portlet>> FindMappingPortletsAsync(this Portlet portlet, CancellationToken cancellationToken = default)
			=> string.IsNullOrWhiteSpace(portlet?.ID)
				? Task.FromResult(new List<Portlet>())
				: Portlet.FindAsync(Filters<Portlet>.Equals("OriginalPortletID", portlet.ID), null, 0, 1, null, cancellationToken);

		internal static async Task<int> GetLastOrderIndexAsync(string desktopID, string zone, CancellationToken cancellationToken = default)
		{
			var portlets = await Portlet.FindAsync(Filters<Portlet>.And(Filters<Portlet>.Equals("DesktopID", desktopID), Filters<Portlet>.Equals("Zone", zone)), Sorts<Portlet>.Ascending("Zone").ThenByAscending("OrderIndex"), 0, 1, null, cancellationToken).ConfigureAwait(false);
			return portlets != null && portlets.Count > 0 ? portlets.Last().OrderIndex : -1;
		}

		internal static async Task<JObject> SearchPortletsAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, CancellationToken cancellationToken = default)
		{
			// prepare
			var request = requestInfo.GetRequestExpando();

			var query = request.Get<string>("FilterBy.Query");
			var filter = request.Get<ExpandoObject>("FilterBy")?.ToFilterBy<Portlet>() ?? Filters<Portlet>.And();
			var sort = string.IsNullOrWhiteSpace(query) ? request.Get<ExpandoObject>("SortBy")?.ToSortBy<Portlet>() ?? Sorts<Portlet>.Ascending("Zone").ThenByAscending("OrderIndex") : null;

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

				gotRights = requestInfo.Session.User.ID.IsEquals(organization.OwnerID) || requestInfo.Session.User.IsViewer(organization.WorkingPrivileges);
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
					? await Portlet.CountAsync(filter, Extensions.GetCacheKeyOfTotalObjects(filter, sort), cancellationToken).ConfigureAwait(false)
					: await Portlet.CountAsync(query, filter, cancellationToken).ConfigureAwait(false);

			var totalPages = new Tuple<long, int>(totalRecords, pageSize).GetTotalPages();
			if (totalPages > 0 && pageNumber > totalPages)
				pageNumber = totalPages;

			// search
			var objects = totalRecords > 0
				? string.IsNullOrWhiteSpace(query)
					? await Portlet.FindAsync(filter, sort, pageSize, pageNumber, Extensions.GetCacheKey(filter, sort, pageSize, pageNumber), cancellationToken).ConfigureAwait(false)
					: await Portlet.SearchAsync(query, filter, pageSize, pageNumber, cancellationToken).ConfigureAwait(false)
				: new List<Portlet>();

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

		internal static async Task<JObject> CreatePortletAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, IRTUService rtuService = null, CancellationToken cancellationToken = default)
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
			var portlet = request.CreatePortletInstance("SystemID,Privileges,OrderIndex,Created,CreatedID,LastModified,LastModifiedID", obj =>
			{
				obj.ID = string.IsNullOrWhiteSpace(obj.ID) || !obj.ID.IsValidUUID() ? UtilityService.NewUUID : obj.ID;
				obj.SystemID = organization.ID;
				obj.Created = obj.LastModified = DateTime.Now;
				obj.CreatedID = obj.LastModifiedID = requestInfo.Session.User.ID;
			});

			portlet.OrderIndex = await PortletProcessor.GetLastOrderIndexAsync(portlet.DesktopID, portlet.Zone, cancellationToken).ConfigureAwait(false) + 1;
			await Portlet.CreateAsync(portlet, cancellationToken).ConfigureAwait(false);

			// update desktop
			var desktop = portlet.Desktop;
			if (desktop != null && desktop._portlets != null && desktop._portlets.FindIndex(p => p.ID.IsEquals(portlet.ID)) < 0)
			{
				desktop._portlets.Add(portlet);
				await desktop.SetAsync(false, true, cancellationToken).ConfigureAwait(false);
			}

			// update mapping portlets
			if (!string.IsNullOrWhiteSpace(portlet.OriginalPortletID))
			{
				portlet._originalPortlet = await Portlet.GetAsync<Portlet>(portlet.OriginalPortletID, cancellationToken).ConfigureAwait(false);
				await Utility.Cache.SetAsync(portlet, cancellationToken).ConfigureAwait(false);
			}

			// send update messages and response
			var json = portlet.ToJson();
			await (rtuService == null ? Task.CompletedTask : rtuService.SendUpdateMessageAsync(new UpdateMessage
			{
				Type = $"{requestInfo.ServiceName}#{portlet.GetTypeName(true)}#Create",
				Data = json,
				DeviceID = "*",
				ExcludedDeviceID = requestInfo.Session.DeviceID
			}, cancellationToken)).ConfigureAwait(false);
			return json;
		}

		internal static async Task<JObject> GetPortletAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, IRTUService rtuService = null, CancellationToken cancellationToken = default)
		{
			// prepare
			var portlet = await Portlet.GetAsync<Portlet>(requestInfo.GetObjectIdentity() ?? "", cancellationToken).ConfigureAwait(false);
			if (portlet == null)
				throw new InformationNotFoundException();
			else if (portlet.Organization == null)
				throw new InformationInvalidException("The organization is invalid");

			// check permission
			var gotRights = isSystemAdministrator || requestInfo.Session.User.ID.IsEquals(portlet.Organization.OwnerID) || requestInfo.Session.User.IsViewer(portlet.Organization.WorkingPrivileges);
			if (!gotRights)
				throw new AccessDeniedException();

			// send the update message to update to all other connected clients and response
			var response = portlet.ToJson();
			await (rtuService == null ? Task.CompletedTask : rtuService.SendUpdateMessageAsync(new UpdateMessage
			{
				Type = $"{requestInfo.ServiceName}#{portlet.GetTypeName(true)}#Update",
				Data = response,
				DeviceID = "*",
				ExcludedDeviceID = requestInfo.Session.DeviceID
			}, cancellationToken)).ConfigureAwait(false);
			return response;
		}

		internal static async Task<JObject> UpdatePortletAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, IRTUService rtuService = null, CancellationToken cancellationToken = default)
		{
			// prepare
			var portlet = await Portlet.GetAsync<Portlet>(requestInfo.GetObjectIdentity() ?? "", cancellationToken).ConfigureAwait(false);
			if (portlet == null)
				throw new InformationNotFoundException();
			else if (portlet.Organization == null)
				throw new InformationInvalidException("The organization is invalid");

			// check permission
			var gotRights = isSystemAdministrator || requestInfo.Session.User.ID.IsEquals(portlet.Organization.OwnerID) || requestInfo.Session.User.IsModerator(portlet.Organization.WorkingPrivileges);
			if (!gotRights)
				throw new AccessDeniedException();

			// update
			string oldDesktopID = portlet.DesktopID;
			string oldZone = portlet.Zone;
			portlet.UpdatePortletInstance(requestInfo.GetBodyExpando(), "ID,SystemID,RepositoryID,RepositoryEntityID,ContentTypeDefinitionID,Privileges,OrderIndex,Created,CreatedID,LastModified,LastModifiedID", obj =>
			{
				obj.LastModified = DateTime.Now;
				obj.LastModifiedID = requestInfo.Session.User.ID;
			});

			if (!portlet.DesktopID.IsEquals(oldDesktopID) || !portlet.Zone.IsEquals(oldZone))
				portlet.OrderIndex = await PortletProcessor.GetLastOrderIndexAsync(portlet.DesktopID, portlet.Zone, cancellationToken).ConfigureAwait(false) + 1;
			await Portlet.UpdateAsync(portlet, requestInfo.Session.User.ID, cancellationToken).ConfigureAwait(false);

			// update desktop
			var desktop = portlet.Desktop;
			if (desktop != null && desktop._portlets != null)
			{
				var index = desktop._portlets.FindIndex(p => p.ID.IsEquals(portlet.ID));
				if (index < 0)
					desktop._portlets.Add(portlet);
				else
					desktop._portlets[index] = portlet;
				await desktop.SetAsync(false, true, cancellationToken).ConfigureAwait(false);
			}

			if (!portlet.DesktopID.IsEquals(oldDesktopID))
			{
				desktop = await oldDesktopID.GetDesktopByIDAsync(cancellationToken).ConfigureAwait(false);
				if (desktop != null && desktop._portlets == null)
				{
					var index = desktop._portlets.FindIndex(p => p.ID.IsEquals(portlet.ID));
					if (index > -1)
					{
						desktop._portlets.RemoveAt(index);
						await desktop.SetAsync(false, true, cancellationToken).ConfigureAwait(false);
					}
				}
			}

			// update mapping portlets
			var updateMessages = new List<UpdateMessage>();
			var objectName = portlet.GetTypeName(true);

			if (string.IsNullOrWhiteSpace(portlet.OriginalPortletID))
				await (await portlet.FindMappingPortletsAsync(cancellationToken).ConfigureAwait(false)).ForEachAsync(async (mappingPortlet, token) =>
				{
					mappingPortlet._originalPortlet = portlet;
					if (!mappingPortlet.Zone.IsEquals(portlet.Zone))
					{
						mappingPortlet.Zone = portlet.Zone;
						mappingPortlet.OrderIndex = await PortletProcessor.GetLastOrderIndexAsync(mappingPortlet.DesktopID, mappingPortlet.Zone, cancellationToken).ConfigureAwait(false) + 1;
						await Portlet.UpdateAsync(mappingPortlet, requestInfo.Session.User.ID, cancellationToken).ConfigureAwait(false);
						desktop = mappingPortlet.Desktop;
						if (desktop != null && desktop._portlets != null)
						{
							var index = desktop._portlets.FindIndex(p => p.ID.IsEquals(mappingPortlet.ID));
							if (index < 0)
								desktop._portlets.Add(mappingPortlet);
							else
								desktop._portlets[index] = mappingPortlet;
							await desktop.SetAsync(false, true, cancellationToken).ConfigureAwait(false);
						}
					}
					else
						await Utility.Cache.SetAsync(mappingPortlet, token).ConfigureAwait(false);
					updateMessages.Add(new UpdateMessage
					{
						Type = $"{requestInfo.ServiceName}#{objectName}#Update",
						Data = mappingPortlet.ToJson(),
						DeviceID = "*"
					});
				}, cancellationToken).ConfigureAwait(false);
			else
			{
				portlet._originalPortlet = await Portlet.GetAsync<Portlet>(portlet.OriginalPortletID, cancellationToken).ConfigureAwait(false);
				await Utility.Cache.SetAsync(portlet, cancellationToken).ConfigureAwait(false);
			}

			// send update messages and responses
			var response = portlet.ToJson();
			updateMessages.Add(new UpdateMessage
			{
				Type = $"{requestInfo.ServiceName}#{objectName}#Update",
				Data = response,
				DeviceID = "*",
				ExcludedDeviceID = requestInfo.Session.DeviceID
			});
			await (rtuService == null ? Task.CompletedTask : updateMessages.ForEachAsync((message, token) => rtuService.SendUpdateMessageAsync(message, token))).ConfigureAwait(false);
			return response;
		}

		internal static async Task<JObject> DeletePortletAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, IRTUService rtuService = null, CancellationToken cancellationToken = default)
		{
			// prepare
			var portlet = await Portlet.GetAsync<Portlet>(requestInfo.GetObjectIdentity() ?? "", cancellationToken).ConfigureAwait(false);
			if (portlet == null)
				throw new InformationNotFoundException();
			else if (portlet.Organization == null)
				throw new InformationInvalidException("The organization is invalid");

			// check permission
			var gotRights = isSystemAdministrator || requestInfo.Session.User.ID.IsEquals(portlet.Organization.OwnerID) || requestInfo.Session.User.IsModerator(portlet.Organization.WorkingPrivileges);
			if (!gotRights)
				throw new AccessDeniedException();

			// update desktop
			var desktop = portlet.Desktop;
			if (desktop != null && desktop._portlets != null)
			{
				var index = desktop._portlets.FindIndex(p => p.ID.IsEquals(portlet.ID));
				if (index > -1)
				{
					desktop._portlets.RemoveAt(index);
					await desktop.SetAsync(false, true, cancellationToken).ConfigureAwait(false);
				}
			}

			// delete mapping portlets
			var updateMessages = new List<UpdateMessage>();
			var objectName = portlet.GetTypeName(true);

			if (string.IsNullOrWhiteSpace(portlet.OriginalPortletID))
				await (await portlet.FindMappingPortletsAsync(cancellationToken).ConfigureAwait(false)).ForEachAsync(async (mappingPortlet, token) =>
				{
					desktop = mappingPortlet.Desktop;
					if (desktop != null && desktop._portlets != null)
					{
						var index = desktop._portlets.FindIndex(p => p.ID.IsEquals(mappingPortlet.ID));
						if (index > -1)
						{
							desktop._portlets.RemoveAt(index);
							await desktop.SetAsync(false, true, cancellationToken).ConfigureAwait(false);
						}
					}
					await Portlet.DeleteAsync<Portlet>(mappingPortlet.ID, requestInfo.Session.User.ID, cancellationToken).ConfigureAwait(false);
					updateMessages.Add(new UpdateMessage
					{
						Type = $"{requestInfo.ServiceName}#{objectName}#Delete",
						Data = mappingPortlet.ToJson(),
						DeviceID = "*"
					});
				}, cancellationToken).ConfigureAwait(false);

			// delete portlet
			await Portlet.DeleteAsync<Portlet>(portlet.ID, requestInfo.Session.User.ID, cancellationToken).ConfigureAwait(false);

			// send update messages and responses
			var response = portlet.ToJson();
			updateMessages.Add(new UpdateMessage
			{
				Type = $"{requestInfo.ServiceName}#{objectName}#Delete",
				Data = response,
				DeviceID = "*",
				ExcludedDeviceID = requestInfo.Session.DeviceID
			});
			await (rtuService == null ? Task.CompletedTask : updateMessages.ForEachAsync((message, token) => rtuService.SendUpdateMessageAsync(message, token))).ConfigureAwait(false);
			return response;
		}
	}
}
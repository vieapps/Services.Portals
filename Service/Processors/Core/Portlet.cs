#region Related components
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
	public static class PortletProcessor
	{
		public static Portlet CreatePortletInstance(this ExpandoObject data, string excluded = null, Action<Portlet> onCompleted = null)
			=> Portlet.CreateInstance(data, excluded?.ToHashSet(), portlet =>
			{
				portlet.Normalize();
				onCompleted?.Invoke(portlet);
			});

		public static Portlet UpdatePortletInstance(this Portlet portlet, ExpandoObject data, string excluded = null, Action<Portlet> onCompleted = null)
			=> portlet.Fill(data, excluded?.ToHashSet(), _ =>
			{
				portlet.Normalize();
				onCompleted?.Invoke(portlet);
			});

		public static List<Portlet> FindPortlets(this string desktopID)
		{
			if (string.IsNullOrWhiteSpace(desktopID) || !desktopID.IsValidUUID())
				return new List<Portlet>();
			var filter = Filters<Portlet>.And(Filters<Portlet>.Equals("DesktopID", desktopID));
			var sort = Sorts<Portlet>.Ascending("Zone").ThenByAscending("OrderIndex");
			return Portlet.Find(filter, sort, 0, 1, Extensions.GetCacheKey(filter, sort));
		}

		public static Task<List<Portlet>> FindPortletsAsync(this string desktopID, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(desktopID) || !desktopID.IsValidUUID())
				return Task.FromResult(new List<Portlet>());
			var filter = Filters<Portlet>.And(Filters<Portlet>.Equals("DesktopID", desktopID));
			var sort = Sorts<Portlet>.Ascending("Zone").ThenByAscending("OrderIndex");
			return Portlet.FindAsync(filter, sort, 0, 1, Extensions.GetCacheKey(filter, sort), cancellationToken);
		}

		public static List<Portlet> FindPortlets(this Portlet portlet)
		{
			if (string.IsNullOrWhiteSpace(portlet?.ID) || !portlet.ID.IsValidUUID() || !string.IsNullOrWhiteSpace(portlet.OriginalPortletID))
				return new List<Portlet>();
			var filter = Filters<Portlet>.Equals("OriginalPortletID", portlet.ID);
			var sort = Sorts<Portlet>.Ascending("DesktopID").ThenByAscending("Zone").ThenByAscending("OrderIndex");
			return Portlet.Find(filter, sort, 0, 1, Extensions.GetCacheKey(filter, sort));
		}

		public static Task<List<Portlet>> FindPortletsAsync(this Portlet portlet, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(portlet?.ID) || !portlet.ID.IsValidUUID() || !string.IsNullOrWhiteSpace(portlet.OriginalPortletID))
				return Task.FromResult(new List<Portlet>());
			var filter = Filters<Portlet>.Equals("OriginalPortletID", portlet.ID);
			var sort = Sorts<Portlet>.Ascending("DesktopID").ThenByAscending("Zone").ThenByAscending("OrderIndex");
			return Portlet.FindAsync(filter, sort, 0, 1, Extensions.GetCacheKey(filter, sort), cancellationToken);
		}

		internal static async Task ProcessInterCommunicateMessageOfPortletAsync(this CommunicateMessage message, CancellationToken cancellationToken = default)
		{
			if (message.Type.IsEndsWith("#Create") || message.Type.IsEndsWith("#Update"))
			{
				Portlet portlet = null;
				if (message.Type.IsEndsWith("#Create"))
					portlet = message.Data.ToExpandoObject().CreatePortletInstance();
				else
				{
					portlet = await Portlet.GetAsync<Portlet>(message.Data.Get<string>("ID"), cancellationToken).ConfigureAwait(false);
					portlet = portlet == null
						? message.Data.ToExpandoObject().CreatePortletInstance()
						: portlet.UpdatePortletInstance(message.Data.ToExpandoObject());
				}
				var desktop = await (portlet.DesktopID ?? "").GetDesktopByIDAsync(cancellationToken).ConfigureAwait(false);
				if (desktop != null && desktop._portlets != null)
				{
					if (!string.IsNullOrWhiteSpace(portlet.OriginalPortletID))
						portlet._originalPortlet = await Portlet.GetAsync<Portlet>(portlet.OriginalPortletID, cancellationToken).ConfigureAwait(false);
					var index = desktop._portlets.FindIndex(p => p.ID.IsEquals(portlet.ID));
					if (index < 0)
						desktop._portlets.Add(portlet);
					else
						desktop._portlets[index] = portlet;
					desktop.Set();
				}
			}
			else if (message.Type.IsEndsWith("#Delete"))
			{
				var portlet = message.Data.ToExpandoObject().CreatePortletInstance();
				var desktop = await (portlet.DesktopID ?? "").GetDesktopByIDAsync(cancellationToken).ConfigureAwait(false);
				if (desktop != null && desktop._portlets != null)
				{
					var index = desktop._portlets.FindIndex(p => p.ID.IsEquals(portlet.ID));
					if (index > -1)
					{
						desktop._portlets.RemoveAt(index);
						desktop.Set();
					}
				}
			}
		}

		internal static async Task<int> GetLastOrderIndexAsync(string desktopID, string zone, CancellationToken cancellationToken = default)
		{
			var portlets = await Portlet.FindAsync(Filters<Portlet>.And(Filters<Portlet>.Equals("DesktopID", desktopID), Filters<Portlet>.Equals("Zone", zone)), Sorts<Portlet>.Ascending("Zone").ThenByAscending("OrderIndex"), 0, 1, null, cancellationToken).ConfigureAwait(false);
			return portlets != null && portlets.Count > 0 ? portlets.Last().OrderIndex : -1;
		}

		internal static async Task ClearRelatedCacheAsync(this Portlet portlet, CancellationToken cancellationToken = default, string correlationID = null)
		{
			// data cache keys
			var dataCacheKeys = Extensions.GetRelatedCacheKeys(Filters<Portlet>.And(Filters<Portlet>.Equals("DesktopID", portlet.DesktopID)), Sorts<Portlet>.Ascending("Zone").ThenByAscending("OrderIndex"));
			if (string.IsNullOrWhiteSpace(portlet.OriginalPortletID))
				dataCacheKeys = Extensions.GetRelatedCacheKeys(Filters<Portlet>.Equals("OriginalPortletID", portlet.ID), Sorts<Portlet>.Ascending("DesktopID").ThenByAscending("Zone").ThenByAscending("OrderIndex")).Concat(dataCacheKeys).ToList();
			dataCacheKeys = dataCacheKeys.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

			// html cache keys (desktop HTMLs)
			var htmlCacheKeys = new List<string>();
			var desktopSetCacheKeys = await Utility.GetSetCacheKeysAsync(Filters<Portlet>.Equals("ID", portlet.ID), cancellationToken).ConfigureAwait(false);
			await desktopSetCacheKeys.ForEachAsync(async (desktopSetCacheKey, _) =>
			{
				var cacheKeys = await Utility.Cache.GetSetMembersAsync(desktopSetCacheKey, cancellationToken).ConfigureAwait(false);
				if (cacheKeys != null && cacheKeys.Count > 0)
					htmlCacheKeys = htmlCacheKeys.Concat(cacheKeys).Concat(new[] { desktopSetCacheKey }).ToList();
			}, cancellationToken, true, false).ConfigureAwait(false);
			htmlCacheKeys = htmlCacheKeys.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

			// clear related cache
			await Utility.Cache.RemoveAsync(htmlCacheKeys.Concat(dataCacheKeys).Distinct(StringComparer.OrdinalIgnoreCase).ToList(), cancellationToken).ConfigureAwait(false);
			await Task.WhenAll
			(
				Utility.WriteCacheLogs ? Utility.WriteLogAsync(correlationID, $"Clear related cache of a portlet [{portlet.Title} - ID: {portlet.ID}]\r\n- {dataCacheKeys.Count} data keys => {dataCacheKeys.Join(", ")}\r\n- {htmlCacheKeys.Count} html keys => {htmlCacheKeys.Join(", ")}", CancellationToken.None, "Caches") : Task.CompletedTask,
				$"{Utility.PortalsHttpURI}/~{portlet.Organization.Alias}/".RefreshWebPageAsync(1, correlationID, $"Refresh desktop when related cache of a portlet was clean [{portlet.Title} - ID: {portlet.ID}]")
			).ConfigureAwait(false);
		}

		internal static Task ClearRelatedCacheAsync(this Portlet portlet, string correlationID = null)
			=> portlet.ClearRelatedCacheAsync(CancellationToken.None, correlationID);

		internal static async Task<JObject> SearchPortletsAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, CancellationToken cancellationToken = default)
		{
			// prepare
			var request = requestInfo.GetRequestExpando();

			var query = request.Get<string>("FilterBy.Query");
			var filter = request.Get<ExpandoObject>("FilterBy")?.ToFilterBy<Portlet>() ?? Filters<Portlet>.And();
			var sort = string.IsNullOrWhiteSpace(query) ? request.Get<ExpandoObject>("SortBy")?.ToSortBy<Portlet>() ?? Sorts<Portlet>.Ascending("DesktopID").ThenByAscending("Zone").ThenByAscending("OrderIndex") : null;

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
				json = response.ToString(Formatting.None);
				Utility.Cache.SetAsync(Extensions.GetCacheKeyOfObjectsJson(filter, sort, pageSize, pageNumber), json, Utility.Cache.ExpirationTime / 2).Run();
			}

			// response
			return response;
		}

		internal static async Task<JObject> CreatePortletAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, CancellationToken cancellationToken = default)
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
			portlet.ClearRelatedCacheAsync(requestInfo.CorrelationID).Run();

			var response = portlet.ToJson();
			var objectName = portlet.GetTypeName(true);
			var updateMessages = new List<UpdateMessage>();
			var communicateMessages = new List<CommunicateMessage>
			{
				new CommunicateMessage(requestInfo.ServiceName)
				{
					Type = $"{objectName}#Create",
					Data = response,
					ExcludedNodeID = Utility.NodeID
				}
			};

			// update desktop
			var desktop = portlet.Desktop;
			if (desktop != null && desktop._portlets != null)
			{
				desktop._portlets.Add(portlet);
				await desktop.SetAsync(false, true, cancellationToken).ConfigureAwait(false);
			}

			// create mapping portlets
			if (string.IsNullOrWhiteSpace(portlet.OriginalPortletID))
			{
				var otherDesktops = request.Get<List<string>>("OtherDesktops")?.Except(new[] { portlet.DesktopID }).Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? new List<string>();
				await otherDesktops.ForEachAsync(async (desktopID, token) =>
				{
					// create new
					var mappingPortlet = new Portlet
					{
						ID = UtilityService.NewUUID,
						Title = portlet.Title,
						SystemID = portlet.SystemID,
						DesktopID = desktopID,
						Zone = portlet.Zone,
						OrderIndex = await PortletProcessor.GetLastOrderIndexAsync(desktopID, portlet.Zone, cancellationToken).ConfigureAwait(false) + 1,
						OriginalPortletID = portlet.ID,
						Created = DateTime.Now,
						CreatedID = requestInfo.Session.User.ID,
						LastModified = DateTime.Now,
						LastModifiedID = requestInfo.Session.User.ID,
						_originalPortlet = portlet
					};
					await Portlet.CreateAsync(mappingPortlet, cancellationToken).ConfigureAwait(false);

					var json = mappingPortlet.ToJson();
					updateMessages.Add(new UpdateMessage
					{
						Type = $"{requestInfo.ServiceName}#{objectName}#Create",
						Data = json,
						DeviceID = "*"
					});

					// update desktop
					desktop = mappingPortlet.Desktop;
					if (desktop != null && desktop._portlets != null)
					{
						desktop._portlets.Add(mappingPortlet);
						await desktop.SetAsync(false, true, cancellationToken).ConfigureAwait(false);
						communicateMessages.Add(new CommunicateMessage(requestInfo.ServiceName)
						{
							Type = $"{objectName}#Create",
							Data = json,
							ExcludedNodeID = Utility.NodeID
						});
					}
				}, cancellationToken, true, false).ConfigureAwait(false);

				// update response JSON with other desktops
				response["OtherDesktops"] = otherDesktops.ToJArray();
			}

			// fetch original portlet
			else
			{
				portlet._originalPortlet = await Portlet.GetAsync<Portlet>(portlet.OriginalPortletID, cancellationToken).ConfigureAwait(false);
				await Utility.Cache.SetAsync(portlet, cancellationToken).ConfigureAwait(false);
			}

			// send update messages
			updateMessages.Add(new UpdateMessage
			{
				Type = $"{requestInfo.ServiceName}#{objectName}#Create",
				Data = response,
				DeviceID = "*"
			});
			await Task.WhenAll(
				updateMessages.ForEachAsync((message, token) => Utility.RTUService.SendUpdateMessageAsync(message, token), cancellationToken, true, false),
				communicateMessages.ForEachAsync((message, token) => Utility.RTUService.SendInterCommunicateMessageAsync(message, token), cancellationToken)
			).ConfigureAwait(false);

			// response
			return response;
		}

		internal static async Task<JObject> GetPortletAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, CancellationToken cancellationToken = default)
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

			// prepare the response
			var response = portlet.ToJson();
			if (string.IsNullOrWhiteSpace(portlet.OriginalPortletID))
			{
				var mappingPortlets = await portlet.FindPortletsAsync(cancellationToken).ConfigureAwait(false) ?? new List<Portlet>();
				response["OtherDesktops"] = mappingPortlets.Where(mappingPortlet => mappingPortlet != null).Select(mappingPortlet => mappingPortlet.DesktopID).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase).ToJArray();
			}

			// send the update messages and response
			await Utility.RTUService.SendUpdateMessageAsync(new UpdateMessage
			{
				Type = $"{requestInfo.ServiceName}#{portlet.GetTypeName(true)}#Update",
				Data = response,
				DeviceID = "*",
				ExcludedDeviceID = requestInfo.Session.DeviceID
			}, cancellationToken).ConfigureAwait(false);
			return response;
		}

		internal static async Task<JObject> UpdatePortletAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, CancellationToken cancellationToken = default)
		{
			// prepare
			var portlet = await Portlet.GetAsync<Portlet>(requestInfo.GetObjectIdentity() ?? "", cancellationToken).ConfigureAwait(false);
			if (portlet == null)
				throw new InformationNotFoundException();

			// is mapping portlet => then get the original portlet
			if (!string.IsNullOrWhiteSpace(portlet.OriginalPortletID))
				portlet = await Portlet.GetAsync<Portlet>(portlet.OriginalPortletID, cancellationToken).ConfigureAwait(false);
			if (portlet == null)
				throw new InformationNotFoundException();

			// validate check permission
			if (portlet.Organization == null)
				throw new InformationInvalidException("The organization is invalid");

			var gotRights = isSystemAdministrator || requestInfo.Session.User.ID.IsEquals(portlet.Organization.OwnerID) || requestInfo.Session.User.IsModerator(portlet.Organization.WorkingPrivileges);
			if (!gotRights)
				throw new AccessDeniedException();

			// update
			var oldDesktopID = portlet.DesktopID;
			var oldZone = portlet.Zone;
			var request = requestInfo.GetBodyExpando();
			portlet.UpdatePortletInstance(request, "ID,SystemID,RepositoryID,RepositoryEntityID,OriginalPortletID,Privileges,OrderIndex,Created,CreatedID,LastModified,LastModifiedID", obj =>
			{
				obj.LastModified = DateTime.Now;
				obj.LastModifiedID = requestInfo.Session.User.ID;
			});

			if ("true".IsEquals(requestInfo.GetParameter("IsAdvancedMode")))
				portlet.RepositoryEntityID = request.Get("RepositoryEntityID", portlet.RepositoryEntityID);

			if (!portlet.DesktopID.IsEquals(oldDesktopID) || !portlet.Zone.IsEquals(oldZone))
				portlet.OrderIndex = await PortletProcessor.GetLastOrderIndexAsync(portlet.DesktopID, portlet.Zone, cancellationToken).ConfigureAwait(false) + 1;

			await Portlet.UpdateAsync(portlet, requestInfo.Session.User.ID, cancellationToken).ConfigureAwait(false);
			portlet.ClearRelatedCacheAsync(requestInfo.CorrelationID).Run();

			var response = portlet.ToJson();
			var objectName = portlet.GetTypeName(true);
			var updateMessages = new List<UpdateMessage>();
			var communicateMessages = new List<CommunicateMessage>
			{
				new CommunicateMessage(requestInfo.ServiceName)
				{
					Type = $"{objectName}#Update",
					Data = response,
					ExcludedNodeID = Utility.NodeID
				}
			};

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

			// update old desktop
			if (!portlet.DesktopID.IsEquals(oldDesktopID))
			{
				desktop = await oldDesktopID.GetDesktopByIDAsync(cancellationToken).ConfigureAwait(false);
				if (desktop != null)
				{
					if (desktop._portlets == null)
					{
						var index = desktop._portlets.FindIndex(p => p.ID.IsEquals(portlet.ID));
						if (index > -1)
						{
							desktop._portlets.RemoveAt(index);
							await desktop.SetAsync(false, true, cancellationToken).ConfigureAwait(false);
						}
					}
					updateMessages.Add(new UpdateMessage
					{
						Type = $"{requestInfo.ServiceName}#{objectName}#Delete",
						Data = response,
						DeviceID = "*"
					});
					communicateMessages.Add(new CommunicateMessage(requestInfo.ServiceName)
					{
						Type = $"{objectName}#Delete",
						Data = response,
						ExcludedNodeID = Utility.NodeID
					});
				}
			}

			// update mapping portlets
			var mappingPortlets = await portlet.FindPortletsAsync(cancellationToken).ConfigureAwait(false) ?? new List<Portlet>();
			var mappingDesktops = mappingPortlets.Select(mappingPortlet => mappingPortlet.DesktopID).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
			var otherDesktops = request.Get<List<string>>("OtherDesktops").Except(new[] { portlet.DesktopID }).Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? new List<string>();

			// add new
			var beAdded = otherDesktops.Except(mappingDesktops).ToList();
			await beAdded.Select(desktopID => new Portlet
			{
				ID = UtilityService.NewUUID,
				Title = portlet.Title,
				SystemID = portlet.SystemID,
				DesktopID = desktopID,
				Zone = portlet.Zone,
				OriginalPortletID = portlet.ID,
				Created = DateTime.Now,
				CreatedID = requestInfo.Session.User.ID,
				LastModified = DateTime.Now,
				LastModifiedID = requestInfo.Session.User.ID,
				_originalPortlet = portlet
			})
			.ForEachAsync(async (mappingPortlet, token) =>
			{
				// create portlet
				mappingPortlet.OrderIndex = await PortletProcessor.GetLastOrderIndexAsync(mappingPortlet.DesktopID, mappingPortlet.Zone, token).ConfigureAwait(false) + 1;
				await Portlet.CreateAsync(mappingPortlet, cancellationToken).ConfigureAwait(false);
				mappingPortlet.ClearRelatedCacheAsync(requestInfo.CorrelationID).Run();

				var json = mappingPortlet.ToJson();
				updateMessages.Add(new UpdateMessage
				{
					Type = $"{requestInfo.ServiceName}#{objectName}#Create",
					Data = json,
					DeviceID = "*"
				});

				// update desktop
				desktop = mappingPortlet.Desktop;
				if (desktop != null)
				{
					if (desktop._portlets != null)
					{
						desktop._portlets.Add(mappingPortlet);
						await desktop.SetAsync(false, true, cancellationToken).ConfigureAwait(false);
					}
					communicateMessages.Add(new CommunicateMessage(requestInfo.ServiceName)
					{
						Type = $"{objectName}#Update",
						Data = json,
						ExcludedNodeID = Utility.NodeID
					});
				}
			}, cancellationToken, true, false).ConfigureAwait(false);

			// delete
			var beDeleted = mappingDesktops.Except(otherDesktops).ToHashSet();
			await mappingPortlets.Where(mappingPortlet => beDeleted.Contains(mappingPortlet.DesktopID)).ForEachAsync(async (mappingPortlet, token) =>
			{
				// delete portlet
				await Portlet.DeleteAsync<Portlet>(mappingPortlet.ID, requestInfo.Session.User.ID, token).ConfigureAwait(false);
				mappingPortlet.ClearRelatedCacheAsync(requestInfo.CorrelationID).Run();

				var json = mappingPortlet.ToJson();
				updateMessages.Add(new UpdateMessage
				{
					Type = $"{requestInfo.ServiceName}#{objectName}#Delete",
					Data = json,
					DeviceID = "*"
				});

				// update desktop
				desktop = mappingPortlet.Desktop;
				if (desktop != null)
				{
					if (desktop._portlets != null)
					{
						var index = desktop._portlets.FindIndex(p => p.ID.IsEquals(mappingPortlet.ID));
						if (index > -1)
						{
							desktop._portlets.RemoveAt(index);
							await desktop.SetAsync(false, true, cancellationToken).ConfigureAwait(false);
						}
					}
					communicateMessages.Add(new CommunicateMessage(requestInfo.ServiceName)
					{
						Type = $"{objectName}#Delete",
						Data = json,
						ExcludedNodeID = Utility.NodeID
					});
				}
			}, cancellationToken, true, false).ConfigureAwait(false);

			// update
			var beUpdated = otherDesktops.Except(beDeleted).Except(beAdded).ToHashSet();
			await mappingPortlets.Where(mappingPortlet => beUpdated.Contains(mappingPortlet.DesktopID)).ForEachAsync(async (mappingPortlet, token) =>
			{
				// update portlet
				mappingPortlet._originalPortlet = portlet;
				if (!mappingPortlet.Title.IsEquals(portlet.Title) || !mappingPortlet.Zone.IsEquals(portlet.Zone))
				{
					mappingPortlet.Title = portlet.Title;
					if (!mappingPortlet.Zone.IsEquals(portlet.Zone))
					{
						mappingPortlet.Zone = portlet.Zone;
						mappingPortlet.OrderIndex = await PortletProcessor.GetLastOrderIndexAsync(mappingPortlet.DesktopID, mappingPortlet.Zone, cancellationToken).ConfigureAwait(false) + 1;
					}
					mappingPortlet.LastModified = DateTime.Now;
					mappingPortlet.LastModifiedID = requestInfo.Session.User.ID;
					await Portlet.UpdateAsync(mappingPortlet, requestInfo.Session.User.ID, cancellationToken).ConfigureAwait(false);
					mappingPortlet.ClearRelatedCacheAsync(requestInfo.CorrelationID).Run();
				}
				else
					await Utility.Cache.SetAsync(mappingPortlet, cancellationToken).ConfigureAwait(false);

				var json = mappingPortlet.ToJson();
				updateMessages.Add(new UpdateMessage
				{
					Type = $"{requestInfo.ServiceName}#{objectName}#Update",
					Data = json,
					DeviceID = "*"
				});

				// update desktop
				desktop = mappingPortlet.Desktop;
				if (desktop != null)
				{
					if (desktop._portlets != null)
					{
						var index = desktop._portlets.FindIndex(p => p.ID.IsEquals(mappingPortlet.ID));
						if (index < 0)
							desktop._portlets.Add(mappingPortlet);
						else
							desktop._portlets[index] = mappingPortlet;
						await desktop.SetAsync(false, true, cancellationToken).ConfigureAwait(false);
					}
					communicateMessages.Add(new CommunicateMessage(requestInfo.ServiceName)
					{
						Type = $"{objectName}#Update",
						Data = json,
						ExcludedNodeID = Utility.NodeID
					});
				}
			}, cancellationToken, true, false).ConfigureAwait(false);

			// update response JSON with other desktops
			response["OtherDesktops"] = otherDesktops.ToJArray();

			// send messages and response
			updateMessages.Add(new UpdateMessage
			{
				Type = $"{requestInfo.ServiceName}#{objectName}#Update",
				Data = response,
				DeviceID = "*"
			});
			await Task.WhenAll(
				updateMessages.ForEachAsync((message, token) => Utility.RTUService.SendUpdateMessageAsync(message, token), cancellationToken, true, false),
				communicateMessages.ForEachAsync((message, token) => Utility.RTUService.SendInterCommunicateMessageAsync(message, token), cancellationToken)
			).ConfigureAwait(false);
			return response;
		}

		internal static async Task<JObject> DeletePortletAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, CancellationToken cancellationToken = default)
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

			// delete portlet
			await Portlet.DeleteAsync<Portlet>(portlet.ID, requestInfo.Session.User.ID, cancellationToken).ConfigureAwait(false);
			portlet.ClearRelatedCacheAsync(requestInfo.CorrelationID).Run();

			var response = portlet.ToJson();
			var objectName = portlet.GetTypeName(true);
			var updateMessages = new List<UpdateMessage>
			{
				new UpdateMessage
				{
					Type = $"{requestInfo.ServiceName}#{objectName}#Delete",
					Data = response,
					DeviceID = "*"
				}
			};
			var communicateMessages = new List<CommunicateMessage>
			{
				new CommunicateMessage(requestInfo.ServiceName)
				{
					Type = $"{objectName}#Delete",
					Data = response,
					ExcludedNodeID = Utility.NodeID
				}
			};

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
			if (string.IsNullOrWhiteSpace(portlet.OriginalPortletID))
			{
				var mappingPortlets = await portlet.FindPortletsAsync(cancellationToken).ConfigureAwait(false) ?? new List<Portlet>();
				await mappingPortlets.ForEachAsync(async (mappingPortlet, token) =>
				{
					// delete portlet
					await Portlet.DeleteAsync<Portlet>(mappingPortlet.ID, requestInfo.Session.User.ID, cancellationToken).ConfigureAwait(false);
					var json = mappingPortlet.ToJson();
					updateMessages.Add(new UpdateMessage
					{
						Type = $"{requestInfo.ServiceName}#{objectName}#Delete",
						Data = json,
						DeviceID = "*"
					});

					// update desktop
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
					communicateMessages.Add(new CommunicateMessage(requestInfo.ServiceName)
					{
						Type = $"{objectName}#Delete",
						Data = json,
						ExcludedNodeID = Utility.NodeID
					});
				}, cancellationToken).ConfigureAwait(false);
			}
			else
			{
				var originalPortlet = portlet.OriginalPortlet;
				if (originalPortlet != null)
				{
					await originalPortlet.ClearRelatedCacheAsync(cancellationToken, requestInfo.CorrelationID).ConfigureAwait(false);
					var json = originalPortlet.ToJson(async originalPortletJson =>
					{
						var mappingPortlets = await originalPortlet.FindPortletsAsync(cancellationToken).ConfigureAwait(false) ?? new List<Portlet>();
						originalPortletJson["OtherDesktops"] = mappingPortlets.Where(mappingPortlet => mappingPortlet != null).Select(mappingPortlet => mappingPortlet.DesktopID).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase).ToJArray();
					});
					updateMessages.Add(new UpdateMessage
					{
						Type = $"{requestInfo.ServiceName}#{objectName}#Update",
						Data = json,
						DeviceID = "*"
					});
					communicateMessages.Add(new CommunicateMessage(requestInfo.ServiceName)
					{
						Type = $"{objectName}#Update",
						Data = json
					});
				}
			}

			// send messages and response
			await Task.WhenAll(
				updateMessages.ForEachAsync((message, token) => Utility.RTUService.SendUpdateMessageAsync(message, token), cancellationToken, true, false),
				communicateMessages.ForEachAsync((message, token) => Utility.RTUService.SendInterCommunicateMessageAsync(message, token), cancellationToken)
			).ConfigureAwait(false);
			return response;
		}

		internal static async Task<JObject> SyncPortletAsync(this RequestInfo requestInfo, CancellationToken cancellationToken = default)
		{
			var data = requestInfo.GetBodyExpando();
			var portlet = await Portlet.GetAsync<Portlet>(data.Get<string>("ID"), cancellationToken).ConfigureAwait(false);
			if (portlet == null)
			{
				portlet = Portlet.CreateInstance(data);
				await Portlet.CreateAsync(portlet, cancellationToken).ConfigureAwait(false);
			}
			else
			{
				portlet.Fill(data);
				await Portlet.UpdateAsync(portlet, true, cancellationToken).ConfigureAwait(false);
			}

			// clear related cache
			portlet.ClearRelatedCacheAsync(requestInfo.CorrelationID).Run();

			// send update messages
			var json = portlet.ToJson();
			var objectName = portlet.GetTypeName(true);
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
				{ "ID", portlet.ID },
				{ "Type", objectName }
			};
		}
	}
}
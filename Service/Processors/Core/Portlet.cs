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
		{
			if (string.IsNullOrWhiteSpace(desktopID) || !desktopID.IsValidUUID())
				return new List<Portlet>();
			var filter = Filters<Portlet>.And(Filters<Portlet>.Equals("DesktopID", desktopID));
			var sort = Sorts<Portlet>.Ascending("DesktopID").ThenByAscending("Zone").ThenByAscending("OrderIndex");
			return Portlet.Find(filter, sort, 0, 1, Extensions.GetCacheKey(filter, sort));
		}

		public static Task<List<Portlet>> FindPortletsAsync(this string desktopID, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(desktopID) || !desktopID.IsValidUUID())
				return Task.FromResult(new List<Portlet>());
			var filter = Filters<Portlet>.And(Filters<Portlet>.Equals("DesktopID", desktopID));
			var sort = Sorts<Portlet>.Ascending("DesktopID").ThenByAscending("Zone").ThenByAscending("OrderIndex");
			return Portlet.FindAsync(filter, sort, 0, 1, Extensions.GetCacheKey(filter, sort), cancellationToken);
		}

		public static List<Portlet> FindPortlets(this Portlet portlet)
		{
			if (string.IsNullOrWhiteSpace(portlet?.ID) || !portlet.ID.IsValidUUID() || !string.IsNullOrWhiteSpace(portlet.OriginalPortletID))
				return new List<Portlet>();
			var filter = Filters<Portlet>.And(Filters<Portlet>.Equals("OriginalPortletID", portlet.ID));
			var sort = Sorts<Portlet>.Ascending("DesktopID").ThenByAscending("Zone").ThenByAscending("OrderIndex");
			return Portlet.Find(filter, sort, 0, 1, Extensions.GetCacheKey(filter, sort));
		}

		public static Task<List<Portlet>> FindPortletsAsync(this Portlet portlet, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(portlet?.ID) || !portlet.ID.IsValidUUID() || !string.IsNullOrWhiteSpace(portlet.OriginalPortletID))
				return Task.FromResult(new List<Portlet>());
			var filter = Filters<Portlet>.And(Filters<Portlet>.Equals("OriginalPortletID", portlet.ID));
			var sort = Sorts<Portlet>.Ascending("DesktopID").ThenByAscending("Zone").ThenByAscending("OrderIndex");
			return Portlet.FindAsync(filter, sort, 0, 1, Extensions.GetCacheKey(filter, sort), cancellationToken);
		}

		internal static async Task<int> GetLastOrderIndexAsync(string desktopID, string zone, CancellationToken cancellationToken = default)
		{
			var portlets = await Portlet.FindAsync(Filters<Portlet>.And(Filters<Portlet>.Equals("DesktopID", desktopID), Filters<Portlet>.Equals("Zone", zone)), Sorts<Portlet>.Ascending("Zone").ThenByAscending("OrderIndex"), 0, 1, null, cancellationToken).ConfigureAwait(false);
			return portlets != null && portlets.Count > 0 ? portlets.Last().OrderIndex : -1;
		}

		static Task ClearRelatedCacheAsync(this Portlet portlet, IRTUService rtuService = null, CancellationToken cancellationToken = default)
		{
			var filter = string.IsNullOrWhiteSpace(portlet.OriginalPortletID)
				? Filters<Portlet>.And(Filters<Portlet>.Equals("OriginalPortletID", portlet.ID))
				: Filters<Portlet>.And(Filters<Portlet>.Equals("DesktopID", portlet.DesktopID));
			var sort = Sorts<Portlet>.Ascending("DesktopID").ThenByAscending("Zone").ThenByAscending("OrderIndex");
			var tasks = new List<Task>
			{
				Utility.Cache.RemoveAsync(Extensions.GetRelatedCacheKeys(filter, sort), cancellationToken)
			};
			if (rtuService != null)
			{
				var contentType = string.IsNullOrWhiteSpace(portlet.OriginalPortletID) ? portlet.ContentType : portlet.OriginalPortlet?.ContentType;
				if (contentType != null)
				{
					if ("Content".IsEquals(portlet.ContentTypeDefinition?.ObjectName))
					{
						var parentContentType = portlet.ContentType.GetParent();
						if (parentContentType != null)
						{
							var parentIdentities = CategoryProcessor.Categories.Values.Where(category => parentContentType.ID.IsEquals(category.ContentTypeID)).Select(category => category.Alias).ToList();
							tasks.Add(rtuService.SendClearCacheRequestAsync(portlet.ContentType.ID, portlet.ContentTypeDefinition.ObjectName, parentIdentities, cancellationToken));
						}
					}
					else
						tasks.Add(rtuService.SendClearCacheRequestAsync(portlet.ContentType.ID, portlet.ContentTypeDefinition?.ObjectName, cancellationToken));
				}
			}
			return Task.WhenAll(tasks);
		}

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

		internal static async Task<JObject> CreatePortletAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, string nodeID = null, IRTUService rtuService = null, CancellationToken cancellationToken = default)
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
			await portlet.ClearRelatedCacheAsync(rtuService, cancellationToken).ConfigureAwait(false);

			var response = portlet.ToJson();
			var portletObjectName = typeof(Portlet).GetTypeName(true);
			var updateMessages = new List<UpdateMessage>();

			// update desktop
			var desktop = portlet.Desktop;
			if (desktop != null && desktop._portlets != null)
			{
				desktop._portlets.Add(portlet);
				await desktop.SetAsync(false, true, cancellationToken).ConfigureAwait(false);
			}

			var desktopObjectName = typeof(Desktop).GetTypeName(true);
			var communicateMessages = new List<CommunicateMessage>
			{
				new CommunicateMessage(requestInfo.ServiceName)
				{
					Type = $"{desktopObjectName}#Update#Portlet",
					Data = response,
					ExcludedNodeID = nodeID
				}
			};

			// create mapping portlets
			if (string.IsNullOrWhiteSpace(portlet.OriginalPortletID))
			{
				var otherDesktops = request.Get<List<string>>("OtherDesktops");
				if (otherDesktops != null && otherDesktops.Count > 0)
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
							Type = $"{requestInfo.ServiceName}#{portletObjectName}#Create",
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
								Type = $"{desktopObjectName}#Update#Portlet",
								Data = json,
								ExcludedNodeID = nodeID
							});
						}
					}, cancellationToken, true, false).ConfigureAwait(false);

				// update response JSON with other desktops
				response["OtherDesktops"] = (otherDesktops ?? new List<string>()).ToJArray();
			}

			// fetch original portlet
			else
			{
				portlet._originalPortlet = await Portlet.GetAsync<Portlet>(portlet.OriginalPortletID, cancellationToken).ConfigureAwait(false);
				await Utility.Cache.SetAsync(portlet, cancellationToken).ConfigureAwait(false);
			}

			// send messages and response
			updateMessages.Add(new UpdateMessage
			{
				Type = $"{requestInfo.ServiceName}#{portletObjectName}#Create",
				Data = response,
				DeviceID = "*",
				ExcludedDeviceID = requestInfo.Session.DeviceID
			});
			await Task.WhenAll(
				updateMessages.ForEachAsync((message, token) => rtuService == null ? Task.CompletedTask : rtuService.SendUpdateMessageAsync(message, token), cancellationToken, true, false),
				communicateMessages.ForEachAsync((message, token) => rtuService == null ? Task.CompletedTask : rtuService.SendInterCommunicateMessageAsync(message, token), cancellationToken)
			).ConfigureAwait(false);
			return response;
		}

		internal static async Task<JObject> GetPortletAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, string nodeID = null, IRTUService rtuService = null, CancellationToken cancellationToken = default)
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
				var mappingPortlets = await portlet.FindPortletsAsync(cancellationToken).ConfigureAwait(false);
				response["OtherDesktops"] = mappingPortlets?.Where(mappingPortlet => mappingPortlet != null).Select(mappingPortlet => mappingPortlet.DesktopID).Where(id => !string.IsNullOrWhiteSpace(id)).ToJArray();
			}

			// send the update messages and response
			await (rtuService == null ? Task.CompletedTask : rtuService.SendUpdateMessageAsync(new UpdateMessage
			{
				Type = $"{requestInfo.ServiceName}#{portlet.GetTypeName(true)}#Update",
				Data = response,
				DeviceID = "*",
				ExcludedDeviceID = requestInfo.Session.DeviceID
			}, cancellationToken)).ConfigureAwait(false);
			return response;
		}

		internal static async Task<JObject> UpdatePortletAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, string nodeID = null, IRTUService rtuService = null, CancellationToken cancellationToken = default)
		{
			// prepare
			var portlet = await Portlet.GetAsync<Portlet>(requestInfo.GetObjectIdentity() ?? "", cancellationToken).ConfigureAwait(false);
			if (portlet == null)
				throw new InformationNotFoundException();

			if (!string.IsNullOrWhiteSpace(portlet.OriginalPortletID))
				portlet = await Portlet.GetAsync<Portlet>(portlet.OriginalPortletID, cancellationToken).ConfigureAwait(false);
			if (portlet == null)
				throw new InformationNotFoundException();

			if (portlet.Organization == null)
				throw new InformationInvalidException("The organization is invalid");

			// check permission
			var gotRights = isSystemAdministrator || requestInfo.Session.User.ID.IsEquals(portlet.Organization.OwnerID) || requestInfo.Session.User.IsModerator(portlet.Organization.WorkingPrivileges);
			if (!gotRights)
				throw new AccessDeniedException();

			// update
			var oldDesktopID = portlet.DesktopID;
			var oldZone = portlet.Zone;
			var request = requestInfo.GetBodyExpando();
			portlet.UpdatePortletInstance(request, "ID,SystemID,RepositoryID,RepositoryEntityID,ContentTypeDefinitionID,OriginalPortletID,Privileges,OrderIndex,Created,CreatedID,LastModified,LastModifiedID", obj =>
			{
				obj.LastModified = DateTime.Now;
				obj.LastModifiedID = requestInfo.Session.User.ID;
			});

			if (!portlet.DesktopID.IsEquals(oldDesktopID) || !portlet.Zone.IsEquals(oldZone))
				portlet.OrderIndex = await PortletProcessor.GetLastOrderIndexAsync(portlet.DesktopID, portlet.Zone, cancellationToken).ConfigureAwait(false) + 1;

			await Portlet.UpdateAsync(portlet, requestInfo.Session.User.ID, cancellationToken).ConfigureAwait(false);
			await portlet.ClearRelatedCacheAsync(rtuService, cancellationToken).ConfigureAwait(false);

			var response = portlet.ToJson();
			var portletObjectName = typeof(Portlet).GetTypeName(true);
			var updateMessages = new List<UpdateMessage>();

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

			var desktopObjectName = typeof(Desktop).GetTypeName(true);
			var communicateMessages = new List<CommunicateMessage>
			{
				new CommunicateMessage(requestInfo.ServiceName)
				{
					Type = $"{desktopObjectName}#Update#Portlet",
					Data = new JObject
					{
						{ "ID", portlet.DesktopID },
						{ "PortletID", portlet.ID }
					},
					ExcludedNodeID = nodeID
				}
			};

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
						Type = $"{requestInfo.ServiceName}#{portletObjectName}#Delete",
						Data = new JObject
						{
							{ "ID", portlet.ID },
							{ "DesktopID", desktop.ID }
						},
						DeviceID = "*"
					});
					communicateMessages.Add(new CommunicateMessage(requestInfo.ServiceName)
					{
						Type = $"{desktopObjectName}#Remove#Portlet",
						Data = new JObject
						{
							{ "ID", desktop.ID },
							{ "PortletID", portlet.ID }
						},
						ExcludedNodeID = nodeID
					});
				}
			}

			// update mapping portlets
			if (string.IsNullOrWhiteSpace(portlet.OriginalPortletID))
			{
				var mappingPortlets = await portlet.FindPortletsAsync(cancellationToken).ConfigureAwait(false);
				var mappingDesktops = mappingPortlets.Select(mappingPortlet => mappingPortlet.DesktopID).ToList();
				var otherDesktops = request.Get("OtherDesktops", new List<string>());

				// add new
				await otherDesktops.Except(mappingDesktops).Select(desktopID => new Portlet
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
					await mappingPortlet.ClearRelatedCacheAsync(null, token).ConfigureAwait(false);

					updateMessages.Add(new UpdateMessage
					{
						Type = $"{requestInfo.ServiceName}#{portletObjectName}#Create",
						Data = mappingPortlet.ToJson(),
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
							Type = $"{desktopObjectName}#Update#Portlet",
							Data = new JObject
							{
								{ "ID", mappingPortlet.DesktopID },
								{ "PortletID", mappingPortlet.ID }
							},
							ExcludedNodeID = nodeID
						});
					}
				}, cancellationToken, true, false).ConfigureAwait(false);

				// remove
				var beRemoved = mappingDesktops.Except(otherDesktops).ToHashSet();
				await mappingPortlets.Where(mappingPortlet => beRemoved.Contains(mappingPortlet.DesktopID)).ForEachAsync(async (mappingPortlet, token) =>
				{
					// remove portlet
					await Portlet.DeleteAsync<Portlet>(mappingPortlet.ID, requestInfo.Session.User.ID, token).ConfigureAwait(false);
					await mappingPortlet.ClearRelatedCacheAsync(null, token).ConfigureAwait(false);

					updateMessages.Add(new UpdateMessage
					{
						Type = $"{requestInfo.ServiceName}#{portletObjectName}#Delete",
						Data = mappingPortlet.ToJson(),
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
							Type = $"{desktopObjectName}#Remove#Portlet",
							Data = new JObject
							{
								{ "ID", mappingPortlet.DesktopID },
								{ "PortletID", mappingPortlet.ID }
							},
							ExcludedNodeID = nodeID
						});
					}
				}, cancellationToken, true, false).ConfigureAwait(false);

				// update
				var beUpdated = otherDesktops.Except(beRemoved).ToHashSet();
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
						await mappingPortlet.ClearRelatedCacheAsync(null, token).ConfigureAwait(false);
					}
					else
						await Utility.Cache.SetAsync(mappingPortlet, cancellationToken).ConfigureAwait(false);

					updateMessages.Add(new UpdateMessage
					{
						Type = $"{requestInfo.ServiceName}#{portletObjectName}#Update",
						Data = mappingPortlet.ToJson(),
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
							Type = $"{desktopObjectName}#Update#Portlet",
							Data = new JObject
							{
								{ "ID", mappingPortlet.DesktopID },
								{ "PortletID", mappingPortlet.ID }
							},
							ExcludedNodeID = nodeID
						});
					}
				}, cancellationToken, true, false).ConfigureAwait(false);

				// update response JSON with other desktops
				response["OtherDesktops"] = otherDesktops.ToJArray();
			}
			else
			{
				portlet._originalPortlet = await Portlet.GetAsync<Portlet>(portlet.OriginalPortletID, cancellationToken).ConfigureAwait(false);
				await Utility.Cache.SetAsync(portlet, cancellationToken).ConfigureAwait(false);
			}

			// send messages and response
			updateMessages.Add(new UpdateMessage
			{
				Type = $"{requestInfo.ServiceName}#{portletObjectName}#Update",
				Data = response,
				DeviceID = "*",
				ExcludedDeviceID = requestInfo.Session.DeviceID
			});
			await Task.WhenAll(
				updateMessages.ForEachAsync((message, token) => rtuService == null ? Task.CompletedTask : rtuService.SendUpdateMessageAsync(message, token), cancellationToken, true, false),
				communicateMessages.ForEachAsync((message, token) => rtuService == null ? Task.CompletedTask : rtuService.SendInterCommunicateMessageAsync(message, token), cancellationToken)
			).ConfigureAwait(false);
			return response;
		}

		internal static async Task<JObject> DeletePortletAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, string nodeID = null, IRTUService rtuService = null, CancellationToken cancellationToken = default)
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
			await portlet.ClearRelatedCacheAsync(rtuService, cancellationToken).ConfigureAwait(false);

			var response = portlet.ToJson();
			var portletObjectName = typeof(Portlet).GetTypeName(true);
			var updateMessages = new List<UpdateMessage>
			{
				new UpdateMessage
				{
					Type = $"{requestInfo.ServiceName}#{portletObjectName}#Delete",
					Data = response,
					DeviceID = "*",
					ExcludedDeviceID = requestInfo.Session.DeviceID
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

			var desktopObjectName = typeof(Desktop).GetTypeName(true);
			var communicateMessages = new List<CommunicateMessage>
			{
				new CommunicateMessage(requestInfo.ServiceName)
				{
					Type = $"{desktopObjectName}#Delete#Portlet",
					Data = response,
					ExcludedNodeID = nodeID
				}
			};

			// delete mapping portlets
			if (string.IsNullOrWhiteSpace(portlet.OriginalPortletID))
			{
				var mappingPortlets = await portlet.FindPortletsAsync(cancellationToken).ConfigureAwait(false);
				await mappingPortlets.ForEachAsync(async (mappingPortlet, token) =>
				{
					// delete portlet
					await Portlet.DeleteAsync<Portlet>(mappingPortlet.ID, requestInfo.Session.User.ID, cancellationToken).ConfigureAwait(false);
					var json = mappingPortlet.ToJson();
					updateMessages.Add(new UpdateMessage
					{
						Type = $"{requestInfo.ServiceName}#{portletObjectName}#Delete",
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
						Type = $"{desktopObjectName}#Delete#Portlet",
						Data = json,
						ExcludedNodeID = nodeID
					});
				}, cancellationToken).ConfigureAwait(false);
			}

			// send messages and response
			await Task.WhenAll(
				updateMessages.ForEachAsync((message, token) => rtuService == null ? Task.CompletedTask : rtuService.SendUpdateMessageAsync(message, token), cancellationToken, true, false),
				communicateMessages.ForEachAsync((message, token) => rtuService == null ? Task.CompletedTask : rtuService.SendInterCommunicateMessageAsync(message, token), cancellationToken)
			).ConfigureAwait(false);
			return response;
		}
	}
}
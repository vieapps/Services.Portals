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
using net.vieapps.Components.Security;
using net.vieapps.Components.Repository;
using net.vieapps.Components.Utility;
using net.vieapps.Services.Portals.Exceptions;
#endregion

namespace net.vieapps.Services.Portals
{
	public static class DesktopProcessor
	{
		internal static ConcurrentDictionary<string, Desktop> Desktops { get; } = new ConcurrentDictionary<string, Desktop>(StringComparer.OrdinalIgnoreCase);

		internal static ConcurrentDictionary<string, Desktop> DesktopsByAlias { get; } = new ConcurrentDictionary<string, Desktop>(StringComparer.OrdinalIgnoreCase);

		internal static HashSet<string> ExtraProperties { get; } = "UISettings,IconURI,CoverURI,MetaTags,Scripts,MainPortletID,SEOSettings".ToHashSet();

		internal static HashSet<string> ExcludedAliases { get; } = (UtilityService.GetAppSetting("Portals:ExcludedAliases", "") + ",Files,Downloads,Images,Thumbnails,ThumbnailPngs,ThumbnailBigs,ThumbnailBigPngs,Default,Index").ToLower().ToHashSet();

		public static Desktop CreateDesktopInstance(this ExpandoObject data, string excluded = null, Action<Desktop> onCompleted = null)
			=> Desktop.CreateInstance(data, excluded?.ToHashSet(), desktop =>
			{
				desktop.Alias = string.IsNullOrWhiteSpace(desktop.Alias) ? desktop.Title.NormalizeAlias() : desktop.Alias.NormalizeAlias();
				desktop.Aliases = string.IsNullOrWhiteSpace(desktop.Aliases) ? null : desktop.Aliases.Replace(",", ";").ToList(";", true, true).Select(alias => alias.NormalizeAlias()).Where(alias => !DesktopProcessor.ExcludedAliases.Contains(alias) && !alias.IsEquals(desktop.Alias)).Join(";");
				desktop.SEOSettings = desktop.SEOSettings ?? new Settings.SEO();
				"TitleMode,DescriptionMode,KeywordsMode".ToList().ForEach(name =>
				{
					var value = data.Get<string>($"SEOSettings.{name}");
					desktop.SEOSettings.SetAttributeValue(name, !string.IsNullOrWhiteSpace(value) && value.TryToEnum(out Settings.SEOMode mode) ? mode as object : null);
				});
				desktop.SEOSettings = desktop.SEOSettings != null && desktop.SEOSettings.SEOInfo == null && desktop.SEOSettings.TitleMode == null && desktop.SEOSettings.DescriptionMode == null && desktop.SEOSettings.KeywordsMode == null ? null : desktop.SEOSettings;
				onCompleted?.Invoke(desktop);
			});

		public static Desktop UpdateDesktopInstance(this Desktop desktop, ExpandoObject data, string excluded = null, Action<Desktop> onCompleted = null)
			=> desktop.Fill(data, excluded?.ToHashSet(), _ =>
			{
				desktop.Alias = string.IsNullOrWhiteSpace(desktop.Alias) ? desktop.Title.NormalizeAlias() : desktop.Alias.NormalizeAlias();
				desktop.Aliases = string.IsNullOrWhiteSpace(desktop.Aliases) ? null : desktop.Aliases.Replace(",", ";").ToList(";", true, true).Select(alias => alias.NormalizeAlias()).Where(alias => !DesktopProcessor.ExcludedAliases.Contains(alias) && !alias.IsEquals(desktop.Alias)).Join(";");
				desktop.SEOSettings = desktop.SEOSettings ?? new Settings.SEO();
				"TitleMode,DescriptionMode,KeywordsMode".ToList().ForEach(name =>
				{
					var value = data.Get<string>($"SEOSettings.{name}");
					desktop.SEOSettings.SetAttributeValue(name, !string.IsNullOrWhiteSpace(value) && value.TryToEnum(out Settings.SEOMode mode) ? mode as object : null);
				});
				desktop.SEOSettings = desktop.SEOSettings != null && desktop.SEOSettings.SEOInfo == null && desktop.SEOSettings.TitleMode == null && desktop.SEOSettings.DescriptionMode == null && desktop.SEOSettings.KeywordsMode == null ? null : desktop.SEOSettings;
				onCompleted?.Invoke(desktop);
			});

		internal static Desktop Set(this Desktop desktop, bool clear = false, bool updateCache = false)
		{
			if (desktop != null)
			{
				if (clear)
				{
					var current = desktop.Remove();
					if (current != null && !current.ParentID.IsEquals(desktop.ParentID))
					{
						if (current.ParentDesktop != null)
							current.ParentDesktop._childrenIDs = null;
						if (desktop.ParentDesktop != null)
							desktop.ParentDesktop._childrenIDs = null;
					}
				}

				DesktopProcessor.Desktops[desktop.ID] = desktop;
				DesktopProcessor.DesktopsByAlias[$"{desktop.SystemID}:{desktop.Alias}"] = desktop;
				Utility.NotRecognizedAliases.Remove($"Desktop:{desktop.SystemID}:{desktop.Alias}");

				if (!string.IsNullOrWhiteSpace(desktop.Aliases))
					desktop.Aliases.ToList(";").ForEach(alias =>
					{
						if (DesktopProcessor.DesktopsByAlias.TryAdd($"{desktop.SystemID}:{alias}", desktop))
							Utility.NotRecognizedAliases.Remove($"Desktop:{desktop.SystemID}:{alias}");
					});

				if (updateCache)
					Utility.Cache.SetAsync(desktop).Run();
			}
			return desktop;
		}

		internal static async Task<Desktop> SetAsync(this Desktop desktop, bool clear = false, bool updateCache = false, CancellationToken cancellationToken = default)
		{
			desktop?.Set(clear);
			await (updateCache && desktop != null ? Utility.Cache.SetAsync(desktop, cancellationToken) : Task.CompletedTask).ConfigureAwait(false);
			return desktop;
		}

		internal static Desktop Remove(this Desktop desktop)
			=> (desktop?.ID ?? "").RemoveDesktop();

		internal static Desktop RemoveDesktop(this string id)
		{
			if (!string.IsNullOrWhiteSpace(id) && DesktopProcessor.Desktops.TryRemove(id, out var desktop) && desktop != null)
			{
				DesktopProcessor.Desktops.Remove(desktop.ID);
				DesktopProcessor.DesktopsByAlias.Remove($"{desktop.SystemID}:{desktop.Alias}");
				if (!string.IsNullOrWhiteSpace(desktop.Aliases))
					desktop.Aliases.ToList(";").ForEach(alias => DesktopProcessor.DesktopsByAlias.Remove($"{desktop.SystemID}:{alias}"));
				return desktop;
			}
			return null;
		}

		public static Desktop GetDesktopByID(this string id, bool force = false, bool fetchRepository = true)
			=> !force && !string.IsNullOrWhiteSpace(id) && DesktopProcessor.Desktops.ContainsKey(id)
				? DesktopProcessor.Desktops[id]
				: fetchRepository && !string.IsNullOrWhiteSpace(id)
					? Desktop.Get<Desktop>(id)?.Set()
					: null;

		public static async Task<Desktop> GetDesktopByIDAsync(this string id, CancellationToken cancellationToken = default, bool force = false)
			=> (id ?? "").GetDesktopByID(force, false) ?? (await Desktop.GetAsync<Desktop>(id, cancellationToken).ConfigureAwait(false))?.Set();

		public static Desktop GetDesktopByAlias(this string systemID, string alias, bool force = false, bool fetchRepository = true)
		{
			if (string.IsNullOrWhiteSpace(systemID) || string.IsNullOrWhiteSpace(alias) || Utility.NotRecognizedAliases.Contains($"Desktop:{systemID}:{alias}"))
				return null;

			var desktop = !force && DesktopProcessor.DesktopsByAlias.ContainsKey($"{systemID}:{alias}")
				? DesktopProcessor.DesktopsByAlias[$"{systemID}:{alias}"]
				: null;

			if (desktop == null && fetchRepository)
			{
				desktop = Desktop.Get<Desktop>(Filters<Desktop>.And(Filters<Desktop>.Equals("SystemID", systemID), Filters<Desktop>.Equals("Alias", alias)), null, null)?.Set();
				if (desktop == null)
					Utility.NotRecognizedAliases.Add($"Desktop:{systemID}:{alias}");
			}

			return desktop;
		}

		public static async Task<Desktop> GetDesktopByAliasAsync(this string systemID, string alias, CancellationToken cancellationToken = default, bool force = false)
		{
			if (string.IsNullOrWhiteSpace(systemID) || string.IsNullOrWhiteSpace(alias) || Utility.NotRecognizedAliases.Contains($"Desktop:{systemID}:{alias}"))
				return null;

			var desktop = systemID.GetDesktopByAlias(alias, force, false) ?? (await Desktop.GetAsync<Desktop>(Filters<Desktop>.And(Filters<Desktop>.Equals("SystemID", systemID), Filters<Desktop>.Equals("Alias", alias)), null, null, cancellationToken).ConfigureAwait(false))?.Set();
			if (desktop == null)
				Utility.NotRecognizedAliases.Add($"Desktop:{systemID}:{alias}");
			return desktop;
		}

		public static IFilterBy<Desktop> GetDesktopsFilter(this string systemID, string parentID)
			=> Filters<Desktop>.And(Filters<Desktop>.Equals("SystemID", systemID), string.IsNullOrWhiteSpace(parentID) ? Filters<Desktop>.IsNull("ParentID") : Filters<Desktop>.Equals("ParentID", parentID));

		public static List<Desktop> FindDesktops(this string systemID, string parentID, bool updateCache = true)
		{
			if (string.IsNullOrWhiteSpace(systemID))
				return new List<Desktop>();
			var filter = systemID.GetDesktopsFilter(parentID);
			var sort = Sorts<Desktop>.Ascending("Title");
			var desktops = Desktop.Find(filter, sort, 0, 1, Extensions.GetCacheKey(filter, sort, 0, 1));
			desktops.ForEach(desktop => desktop.Set(false, updateCache));
			return desktops;
		}

		public static async Task<List<Desktop>> FindDesktopsAsync(this string systemID, string parentID, CancellationToken cancellationToken = default, bool updateCache = true)
		{
			if (string.IsNullOrWhiteSpace(systemID))
				return new List<Desktop>();
			var filter = systemID.GetDesktopsFilter(parentID);
			var sort = Sorts<Desktop>.Ascending("Title");
			var desktops = await Desktop.FindAsync(filter, sort, 0, 1, Extensions.GetCacheKey(filter, sort, 0, 1), cancellationToken).ConfigureAwait(false);
			await desktops.ForEachAsync((desktop, token) => desktop.SetAsync(false, updateCache, token), cancellationToken).ConfigureAwait(false);
			return desktops;
		}

		internal static async Task ProcessInterCommunicateMessageOfDesktopAsync(this CommunicateMessage message, CancellationToken cancellationToken = default)
		{
			if (message.Type.IsEndsWith("#Create"))
				await message.Data.ToExpandoObject().CreateDesktopInstance().SetAsync(true, false, cancellationToken).ConfigureAwait(false);

			else if (message.Type.IsEndsWith("#Update"))
			{
				var desktop = message.Data.Get("ID", "").GetDesktopByID(false, false);
				if (desktop == null)
					desktop = message.Data.ToExpandoObject().CreateDesktopInstance();
				else
					desktop.UpdateDesktopInstance(message.Data.ToExpandoObject());
				desktop._portlets = null;
				desktop._childrenIDs = null;
				await Task.WhenAll(
					desktop.FindPortletsAsync(cancellationToken, false),
					desktop.FindChildrenAsync(cancellationToken, false)
				).ConfigureAwait(false);
				await desktop.Portlets.Where(portlet => !string.IsNullOrWhiteSpace(portlet.OriginalPortletID)).ForEachAsync(async (portlet, token) => portlet._originalPortlet = await Portlet.GetAsync<Portlet>(portlet.OriginalPortletID, token).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
				desktop.SetAsync(false, true, cancellationToken).Run();
			}

			else if (message.Type.IsEndsWith("#Delete"))
				message.Data.ToExpandoObject().CreateDesktopInstance().Remove();
		}

		static Task ClearRelatedCacheAsync(this Desktop desktop, string oldParentID = null, CancellationToken cancellationToken = default)
		{
			var sort = Sorts<Desktop>.Ascending("Title");
			var tasks = new List<Task>
			{
				Utility.Cache.RemoveAsync(Extensions.GetRelatedCacheKeys(desktop.SystemID.GetDesktopsFilter(null), sort), cancellationToken)
			};
			if (!string.IsNullOrWhiteSpace(desktop.ParentID) && desktop.ParentID.IsValidUUID())
				tasks.Add(Utility.Cache.RemoveAsync(Extensions.GetRelatedCacheKeys(desktop.SystemID.GetDesktopsFilter(desktop.ParentID), sort), cancellationToken));
			if (!string.IsNullOrWhiteSpace(oldParentID) && oldParentID.IsValidUUID())
				tasks.Add(Utility.Cache.RemoveAsync(Extensions.GetRelatedCacheKeys(desktop.SystemID.GetDesktopsFilter(oldParentID), sort), cancellationToken));
			return Task.WhenAll(tasks);
		}

		internal static async Task<JObject> SearchDesktopsAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, CancellationToken cancellationToken = default)
		{
			// prepare
			var request = requestInfo.GetRequestExpando();

			var query = request.Get<string>("FilterBy.Query");

			var filter = request.Get<ExpandoObject>("FilterBy", null)?.ToFilterBy<Desktop>() ?? Filters<Desktop>.And();
			if (filter is FilterBys<Desktop> filterBy)
			{
				if (!string.IsNullOrWhiteSpace(query))
				{
					var filterByParent = filterBy.GetChild("ParentID");
					if (filterByParent != null)
						filterBy.Children.Remove(filterByParent);
				}
				else if (filterBy.GetChild("ParentID") == null)
					filterBy.Children.Add(Filters<Desktop>.IsNull("ParentID"));
			}

			var sort = string.IsNullOrWhiteSpace(query) ? request.Get<ExpandoObject>("SortBy")?.ToSortBy<Desktop>() ?? Sorts<Desktop>.Ascending("Title") : null;

			var pagination = request.Get<ExpandoObject>("Pagination")?.GetPagination() ?? new Tuple<long, int, int, int>(-1, 0, 20, 1);
			var pageSize = pagination.Item3;
			var pageNumber = pagination.Item4;

			// get organization
			var organizationID = filter.GetValue("SystemID") ?? requestInfo.GetParameter("x-system-id");
			var organization = await (organizationID ?? "").GetOrganizationByIDAsync(cancellationToken).ConfigureAwait(false);
			if (organization == null)
				throw new InformationExistedException("The organization is invalid");

			// check permission
			var gotRights = isSystemAdministrator || requestInfo.Session.User.ID.IsEquals(organization.OwnerID) || requestInfo.Session.User.IsViewer(organization.WorkingPrivileges);
			if (!gotRights)
				throw new AccessDeniedException();

			// process cache
			var json = string.IsNullOrWhiteSpace(query) ? await Utility.Cache.GetAsync<string>(Extensions.GetCacheKeyOfObjectsJson(filter, sort, pageSize, pageNumber), cancellationToken).ConfigureAwait(false) : null;
			if (!string.IsNullOrWhiteSpace(json))
				return JObject.Parse(json);

			// prepare pagination
			var totalRecords = pagination.Item1 > -1 ? pagination.Item1 : -1;
			if (totalRecords < 0)
				totalRecords = string.IsNullOrWhiteSpace(query)
					? await Desktop.CountAsync(filter, Extensions.GetCacheKeyOfTotalObjects(filter, sort), cancellationToken).ConfigureAwait(false)
					: await Desktop.CountAsync(query, filter, cancellationToken).ConfigureAwait(false);

			var totalPages = new Tuple<long, int>(totalRecords, pageSize).GetTotalPages();
			if (totalPages > 0 && pageNumber > totalPages)
				pageNumber = totalPages;

			// search
			var objects = totalRecords > 0
				? string.IsNullOrWhiteSpace(query)
					? await Desktop.FindAsync(filter, sort, pageSize, pageNumber, Extensions.GetCacheKey(filter, sort, pageSize, pageNumber), cancellationToken).ConfigureAwait(false)
					: await Desktop.SearchAsync(query, filter, pageSize, pageNumber, cancellationToken).ConfigureAwait(false)
				: new List<Desktop>();

			// build response
			pagination = new Tuple<long, int, int, int>(totalRecords, totalPages, pageSize, pageNumber);
			var response = new JObject()
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

		internal static async Task<JObject> CreateDesktopAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, CancellationToken cancellationToken = default)
		{
			// prepare
			var request = requestInfo.GetBodyExpando();
			var organizationID = request.Get<string>("SystemID") ?? requestInfo.GetParameter("x-system-id");
			var organization = await (organizationID ?? "").GetOrganizationByIDAsync(cancellationToken).ConfigureAwait(false);
			if (organization == null)
				throw new InformationInvalidException("The organization is invalid");

			// check permission
			var gotRights = isSystemAdministrator || requestInfo.Session.User.ID.IsEquals(organization.OwnerID) || requestInfo.Session.User.IsModerator(organization.WorkingPrivileges);
			if (!gotRights)
				throw new AccessDeniedException();

			// check alias
			var alias = request.Get("Alias", "");
			if (!string.IsNullOrWhiteSpace(alias))
			{
				if (DesktopProcessor.ExcludedAliases.Contains(alias.NormalizeAlias()))
					throw new AliasIsExistedException($"The alias ({alias.NormalizeAlias()}) is used by another purpose");
				var existing = await organization.ID.GetDesktopByAliasAsync(alias.NormalizeAlias(), cancellationToken).ConfigureAwait(false);
				if (existing != null)
					throw new AliasIsExistedException($"The alias ({alias.NormalizeAlias()}) is used by another desktop");
			}

			// validate template, meta-tags and scripts
			request.Get("Template", "").ValidateTemplate();
			request.Get("MetaTags", "").ValidateMetaTagsOrScripts();
			request.Get("Scripts", "").ValidateMetaTagsOrScripts(true);

			// create new
			var desktop = request.CreateDesktopInstance("SystemID,Privileges,OriginalPrivileges,Created,CreatedID,LastModified,LastModifiedID", obj =>
			{
				obj.SystemID = organization.ID;
				obj.ParentID = obj.ParentDesktop != null ? obj.ParentID : null;
				obj.ID = string.IsNullOrWhiteSpace(obj.ID) || !obj.ID.IsValidUUID() ? UtilityService.NewUUID : obj.ID;
				obj.Created = obj.LastModified = DateTime.Now;
				obj.CreatedID = obj.LastModifiedID = requestInfo.Session.User.ID;
				obj.NormalizeExtras();
				obj._childrenIDs = new List<string>();
			});
			await Desktop.CreateAsync(desktop, cancellationToken).ConfigureAwait(false);
			desktop.Set().ClearRelatedCacheAsync(null, cancellationToken).Run();

			var updateMessages = new List<UpdateMessage>();
			var communicateMessages = new List<CommunicateMessage>();
			var objectName = desktop.GetTypeName(true);

			// update parent
			var parentDesktop = desktop.ParentDesktop;
			if (parentDesktop != null)
			{
				await parentDesktop.FindChildrenAsync(cancellationToken, false).ConfigureAwait(false);
				if (parentDesktop._childrenIDs.IndexOf(desktop.ID) < 0)
					parentDesktop._childrenIDs.Add(desktop.ID);
				await parentDesktop.SetAsync(false, true, cancellationToken).ConfigureAwait(false);

				var json = parentDesktop.ToJson(true, false);
				updateMessages.Add(new UpdateMessage
				{
					Type = $"{requestInfo.ServiceName}#{objectName}#Update",
					Data = json,
					DeviceID = "*"
				});
				communicateMessages.Add(new CommunicateMessage(requestInfo.ServiceName)
				{
					Type = $"{objectName}#Create",
					Data = json,
					ExcludedNodeID = Utility.NodeID
				});
			}

			// message to update to all other connected clients
			var response = desktop.ToJson(true, false);
			if (desktop.ParentDesktop == null)
				updateMessages.Add(new UpdateMessage
				{
					Type = $"{requestInfo.ServiceName}#{objectName}#Create",
					DeviceID = "*",
					ExcludedDeviceID = requestInfo.Session.DeviceID,
					Data = response
				});

			// message to update to all service instances (on all other nodes)
			communicateMessages.Add(new CommunicateMessage(requestInfo.ServiceName)
			{
				Type = $"{objectName}#Create",
				Data = response,
				ExcludedNodeID = Utility.NodeID
			});

			// send update messages
			await Task.WhenAll(
				updateMessages.ForEachAsync((message, token) => Utility.RTUService.SendUpdateMessageAsync(message, token), cancellationToken, true, false),
				communicateMessages.ForEachAsync((message, token) => Utility.RTUService.SendInterCommunicateMessageAsync(message, token), cancellationToken)
			).ConfigureAwait(false);

			// send notification
			desktop.SendNotificationAsync("Create", desktop.Organization.Notifications, ApprovalStatus.Published, ApprovalStatus.Published, requestInfo, cancellationToken).Run();

			// response
			return response;
		}

		internal static async Task<JObject> GetDesktopAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, CancellationToken cancellationToken = default)
		{
			// prepare
			var identity = requestInfo.GetObjectIdentity() ?? "";
			var desktop = await (identity.IsValidUUID() ? identity.GetDesktopByIDAsync(cancellationToken) : identity.GetDesktopByAliasAsync(identity, cancellationToken)).ConfigureAwait(false);
			if (desktop == null)
				throw new InformationNotFoundException();
			else if (desktop.Organization == null)
				throw new InformationInvalidException("The organization is invalid");

			// check permission
			var gotRights = isSystemAdministrator || requestInfo.Session.User.ID.IsEquals(desktop.Organization.OwnerID) || requestInfo.Session.User.IsViewer(desktop.Organization.WorkingPrivileges);
			if (!gotRights)
				throw new AccessDeniedException();

			if (!identity.IsValidUUID())
				return new JObject
				{
					{ "ID", desktop.ID },
					{ "Title", desktop.Title },
					{ "Alias", desktop.Alias }
				};

			// prepare the response
			if (desktop._childrenIDs == null || desktop._portlets == null)
			{
				if (desktop._childrenIDs == null)
					await desktop.FindChildrenAsync(cancellationToken, false).ConfigureAwait(false);
				if (desktop._portlets == null)
					await desktop.FindPortletsAsync(cancellationToken, false).ConfigureAwait(false);
				await desktop.SetAsync(false, true, cancellationToken).ConfigureAwait(false);
			}

			// send update message
			var response = desktop.ToJson(true, false);
			await Utility.RTUService.SendUpdateMessageAsync(new UpdateMessage
			{
				Type = $"{requestInfo.ServiceName}#{desktop.GetTypeName(true)}#Update",
				Data = response,
				DeviceID = "*",
				ExcludedDeviceID = requestInfo.Session.DeviceID
			}, cancellationToken).ConfigureAwait(false);

			// response
			return response;
		}

		internal static async Task<JObject> UpdateDesktopAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, CancellationToken cancellationToken = default)
		{
			// prepare
			var desktop = await (requestInfo.GetObjectIdentity() ?? "").GetDesktopByIDAsync(cancellationToken).ConfigureAwait(false);
			if (desktop == null)
				throw new InformationNotFoundException();
			else if (desktop.Organization == null)
				throw new InformationInvalidException("The organization is invalid");

			// check
			var gotRights = isSystemAdministrator || requestInfo.Session.User.ID.IsEquals(desktop.Organization.OwnerID) || requestInfo.Session.User.IsModerator(desktop.Organization.WorkingPrivileges);
			if (!gotRights)
				throw new AccessDeniedException();

			var request = requestInfo.GetBodyExpando();
			var oldParentID = desktop.ParentID;

			var oldAlias = desktop.Alias;
			var alias = request.Get("Alias", "");
			if (!string.IsNullOrWhiteSpace(alias))
			{
				if (DesktopProcessor.ExcludedAliases.Contains(alias.NormalizeAlias()))
					throw new AliasIsExistedException($"The alias ({alias.NormalizeAlias()}) is used by another purpose");
				var existing = await desktop.SystemID.GetDesktopByAliasAsync(alias.NormalizeAlias(), cancellationToken).ConfigureAwait(false);
				if (existing != null && !existing.ID.Equals(desktop.ID))
					throw new AliasIsExistedException($"The alias ({alias.NormalizeAlias()}) is used by another desktop");
			}

			// validate template, meta-tags and scripts
			request.Get("Template", "").ValidateTemplate();
			request.Get("MetaTags", "").ValidateMetaTagsOrScripts();
			request.Get("Scripts", "").ValidateMetaTagsOrScripts(true);

			// update
			desktop.UpdateDesktopInstance(request, "ID,SystemID,Privileges,OriginalPrivileges,Created,CreatedID,LastModified,LastModifiedID", async _ =>
			{
				desktop.LastModified = DateTime.Now;
				desktop.LastModifiedID = requestInfo.Session.User.ID;
				desktop.NormalizeExtras();
				await desktop.FindChildrenAsync(cancellationToken, false).ConfigureAwait(false);
			});
			await Desktop.UpdateAsync(desktop, requestInfo.Session.User.ID, cancellationToken).ConfigureAwait(false);
			desktop.Set().ClearRelatedCacheAsync(oldParentID, cancellationToken).Run();

			var updateMessages = new List<UpdateMessage>();
			var communicateMessages = new List<CommunicateMessage>();
			var objectName = desktop.GetTypeName(true);

			// update parent
			var parentDesktop = desktop.ParentDesktop;
			if (parentDesktop != null && !desktop.ParentID.IsEquals(oldParentID))
			{
				await parentDesktop.FindChildrenAsync(cancellationToken, false).ConfigureAwait(false);
				if (parentDesktop._childrenIDs.IndexOf(desktop.ID) < 0)
					parentDesktop._childrenIDs.Add(desktop.ID);
				await parentDesktop.SetAsync(false, true, cancellationToken).ConfigureAwait(false);

				var json = parentDesktop.ToJson(true, false);
				updateMessages.Add(new UpdateMessage
				{
					Type = $"{requestInfo.ServiceName}#{objectName}#Update",
					Data = json,
					DeviceID = "*"
				});
				communicateMessages.Add(new CommunicateMessage(requestInfo.ServiceName)
				{
					Type = $"{objectName}#Update",
					Data = json,
					ExcludedNodeID = Utility.NodeID
				});
			}

			// update old parent
			if (!string.IsNullOrWhiteSpace(oldParentID) && !oldParentID.IsEquals(desktop.ParentID))
			{
				parentDesktop = await oldParentID.GetDesktopByIDAsync(cancellationToken).ConfigureAwait(false);
				if (parentDesktop != null)
				{
					await parentDesktop.FindChildrenAsync(cancellationToken, false).ConfigureAwait(false);
					parentDesktop._childrenIDs.Remove(desktop.ID);
					await parentDesktop.SetAsync(false, true, cancellationToken).ConfigureAwait(false);

					var json = parentDesktop.ToJson(true, false);
					updateMessages.Add(new UpdateMessage
					{
						Type = $"{requestInfo.ServiceName}#{objectName}#Update",
						Data = json,
						DeviceID = "*"
					});
					communicateMessages.Add(new CommunicateMessage(requestInfo.ServiceName)
					{
						Type = $"{objectName}#Update",
						Data = json,
						ExcludedNodeID = Utility.NodeID
					});
				}
			}

			// message to update to all other connected clients
			var response = desktop.ToJson(true, false);
			if (desktop.ParentDesktop == null)
				updateMessages.Add(new UpdateMessage
				{
					Type = $"{requestInfo.ServiceName}#{objectName}#Update",
					Data = response,
					DeviceID = "*",
					ExcludedDeviceID = requestInfo.Session.DeviceID
				});

			// message to update to all service instances (on all other nodes)
			communicateMessages.Add(new CommunicateMessage(requestInfo.ServiceName)
			{
				Type = $"{objectName}#Update",
				Data = response,
				ExcludedNodeID = Utility.NodeID
			});

			// send update messages
			await Task.WhenAll(
				updateMessages.ForEachAsync((message, token) => Utility.RTUService.SendUpdateMessageAsync(message, token), cancellationToken, true, false),
				communicateMessages.ForEachAsync((message, token) => Utility.RTUService.SendInterCommunicateMessageAsync(message, token), cancellationToken)
			).ConfigureAwait(false);

			// send notification
			desktop.SendNotificationAsync("Update", desktop.Organization.Notifications, ApprovalStatus.Published, ApprovalStatus.Published, requestInfo, cancellationToken).Run();

			// response
			return response;
		}

		internal static async Task<JObject> UpdateDesktopPortletsAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, CancellationToken cancellationToken = default)
		{
			// prepare
			var desktop = await (requestInfo.GetObjectIdentity() ?? "").GetDesktopByIDAsync(cancellationToken).ConfigureAwait(false);
			if (desktop == null)
				throw new InformationNotFoundException();
			else if (desktop.Organization == null)
				throw new InformationInvalidException("The organization is invalid");

			// check
			var gotRights = isSystemAdministrator || requestInfo.Session.User.ID.IsEquals(desktop.Organization.OwnerID) || requestInfo.Session.User.IsModerator(desktop.Organization.WorkingPrivileges);
			if (!gotRights)
				throw new AccessDeniedException();

			// update portlets
			await desktop.FindPortletsAsync(cancellationToken, false).ConfigureAwait(false);
			var portlets = desktop.Portlets;
			await requestInfo.GetBodyJson().Get<JArray>("Portlets").ForEachAsync(async (info, _) =>
			{
				var id = info.Get<string>("ID");
				var orderIndex = info.Get<int>("OrderIndex");
				var portlet = portlets.Find(p => p.ID == id);
				if (portlet != null)
				{
					portlet.OrderIndex = orderIndex;
					portlet.LastModified = DateTime.Now;
					portlet.LastModifiedID = requestInfo.Session.User.ID;
					if (!string.IsNullOrWhiteSpace(portlet.OriginalPortletID) && portlet._originalPortlet == null)
						portlet._originalPortlet = await Portlet.GetAsync<Portlet>(portlet.OriginalPortletID, cancellationToken).ConfigureAwait(false);
					await Portlet.UpdateAsync(portlet, requestInfo.Session.User.ID, cancellationToken).ConfigureAwait(false);
				}
			}, cancellationToken, true, false).ConfigureAwait(false);
			desktop.SetAsync(false, true, cancellationToken).Run();

			// send update messages
			var objectName = desktop.GetTypeName(true);
			var response = desktop.ToJson(true, false);
			await Task.WhenAll(
				Utility.RTUService.SendUpdateMessageAsync(new UpdateMessage
				{
					Type = $"{requestInfo.ServiceName}#{objectName}#Update",
					Data = response,
					DeviceID = "*",
					ExcludedDeviceID = requestInfo.Session.DeviceID
				}, cancellationToken),
				Utility.RTUService.SendInterCommunicateMessageAsync(new CommunicateMessage(requestInfo.ServiceName)
				{
					Type = $"{objectName}#Update",
					Data = response,
					ExcludedNodeID = Utility.NodeID
				}, cancellationToken)
			).ConfigureAwait(false);

			// response
			return response;
		}

		internal static async Task<JObject> DeleteDesktopAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, CancellationToken cancellationToken = default)
		{
			// prepare
			var desktop = await (requestInfo.GetObjectIdentity() ?? "").GetDesktopByIDAsync(cancellationToken).ConfigureAwait(false);
			if (desktop == null)
				throw new InformationNotFoundException();
			else if (desktop.Organization == null)
				throw new InformationInvalidException("The organization is invalid");

			// check permission
			var gotRights = isSystemAdministrator || requestInfo.Session.User.ID.IsEquals(desktop.Organization.OwnerID) || requestInfo.Session.User.IsModerator(desktop.Organization.WorkingPrivileges);
			if (!gotRights)
				throw new AccessDeniedException();

			// delete
			var updateMessages = new List<UpdateMessage>();
			var communicateMessages = new List<CommunicateMessage>();
			var objectName = desktop.GetTypeName(true);
			var updateChildren = requestInfo.Header.TryGetValue("x-children", out var childrenMode) && "set-null".IsEquals(childrenMode);

			var children = await desktop.FindChildrenAsync(cancellationToken, false).ConfigureAwait(false);
			await children.ForEachAsync(async (child, _) =>
			{
				// update children to root
				if (updateChildren)
				{
					child.ParentID = null;
					child.LastModified = DateTime.Now;
					child.LastModifiedID = requestInfo.Session.User.ID;

					await Task.WhenAll(
						Role.UpdateAsync(child, requestInfo.Session.User.ID, cancellationToken),
						child.SetAsync(false, false, cancellationToken)
					).ConfigureAwait(false);
					child.SendNotificationAsync("Update", child.Organization.Notifications, ApprovalStatus.Published, ApprovalStatus.Published, requestInfo, cancellationToken).Run();

					var json = child.ToJson(true, false);
					updateMessages.Add(new UpdateMessage
					{
						Type = $"{requestInfo.ServiceName}#{objectName}#Delete",
						Data = json,
						DeviceID = "*"
					});
					communicateMessages.Add(new CommunicateMessage(requestInfo.ServiceName)
					{
						Type = $"{objectName}#Delete",
						Data = json,
						ExcludedNodeID = Utility.NodeID
					});
				}

				// delete children
				else
				{
					var messages = await child.DeleteChildrenAsync(requestInfo, cancellationToken).ConfigureAwait(false);
					updateMessages = updateMessages.Concat(messages.Item1).ToList();
					communicateMessages = communicateMessages.Concat(messages.Item2).ToList();
				}
			}, cancellationToken, true, false).ConfigureAwait(false);

			await Desktop.DeleteAsync<Desktop>(desktop.ID, requestInfo.Session.User.ID, cancellationToken).ConfigureAwait(false);
			desktop.Remove().ClearRelatedCacheAsync(null, cancellationToken).Run();

			// message to update to all other connected clients
			var response = desktop.ToJson();
			updateMessages.Add(new UpdateMessage
			{
				Type = $"{requestInfo.ServiceName}#{objectName}#Delete",
				Data = response,
				DeviceID = "*",
				ExcludedDeviceID = requestInfo.Session.DeviceID
			});

			// message to update to all service instances (on all other nodes)
			communicateMessages.Add(new CommunicateMessage(requestInfo.ServiceName)
			{
				Type = $"{objectName}#Delete",
				Data = response,
				ExcludedNodeID = Utility.NodeID
			});

			// send update messages
			await Task.WhenAll(
				updateMessages.ForEachAsync((message, token) => Utility.RTUService.SendUpdateMessageAsync(message, token), cancellationToken, true, false),
				communicateMessages.ForEachAsync((message, token) => Utility.RTUService.SendInterCommunicateMessageAsync(message, token), cancellationToken)
			).ConfigureAwait(false);

			// send notification
			desktop.SendNotificationAsync("Delete", desktop.Organization.Notifications, ApprovalStatus.Published, ApprovalStatus.Published, requestInfo, cancellationToken).Run();

			// response
			return response;
		}

		static async Task<Tuple<List<UpdateMessage>, List<CommunicateMessage>>> DeleteChildrenAsync(this Desktop desktop, RequestInfo requestInfo, CancellationToken cancellationToken = default)
		{
			// prepare
			var updateMessages = new List<UpdateMessage>();
			var communicateMessages = new List<CommunicateMessage>();
			var objectName = desktop.GetTypeName(true);

			// delete childrenn
			var children = await desktop.FindChildrenAsync(cancellationToken, false).ConfigureAwait(false);
			await children.ForEachAsync(async (child, _) =>
			{
				var messages = await child.DeleteChildrenAsync(requestInfo, cancellationToken).ConfigureAwait(false);
				updateMessages = updateMessages.Concat(messages.Item1).ToList();
				communicateMessages = communicateMessages.Concat(messages.Item2).ToList();
			}, cancellationToken, true, false).ConfigureAwait(false);

			// delete
			await Desktop.DeleteAsync<Desktop>(desktop.ID, requestInfo.Session.User.ID, cancellationToken).ConfigureAwait(false);
			desktop.Remove().ClearRelatedCacheAsync(null, cancellationToken).Run();

			// send notification
			desktop.SendNotificationAsync("Delete", desktop.Organization.Notifications, ApprovalStatus.Published, ApprovalStatus.Published, requestInfo, cancellationToken).Run();

			// prepare update messages
			var json = desktop.ToJson();
			updateMessages.Add(new UpdateMessage
			{
				Type = $"{requestInfo.ServiceName}#{objectName}#Delete",
				Data = json,
				DeviceID = "*"
			});
			communicateMessages.Add(new CommunicateMessage(requestInfo.ServiceName)
			{
				Type = $"{objectName}#Delete",
				Data = json,
				ExcludedNodeID = Utility.NodeID
			});
			return new Tuple<List<UpdateMessage>, List<CommunicateMessage>>(updateMessages, communicateMessages);
		}

		internal static async Task<JObject> SyncDesktopAsync(this RequestInfo requestInfo, CancellationToken cancellationToken = default)
		{
			var data = requestInfo.GetBodyExpando();
			var desktop = await data.Get<string>("ID").GetDesktopByIDAsync(cancellationToken).ConfigureAwait(false);
			if (desktop == null)
			{
				desktop = Desktop.CreateInstance(data);
				desktop.Extras = data.Get<string>("Extras") ?? desktop.Extras;
				await Desktop.CreateAsync(desktop, cancellationToken).ConfigureAwait(false);
			}
			else
			{
				desktop.Fill(data);
				desktop.Extras = data.Get<string>("Extras") ?? desktop.Extras;
				await Desktop.UpdateAsync(desktop, true, cancellationToken).ConfigureAwait(false);
			}

			// send update messages
			var json = desktop.Set().ToJson();
			var objectName = desktop.GetTypeName(true);
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
				{ "ID", desktop.ID },
				{ "Type", objectName }
			};
		}
	}
}
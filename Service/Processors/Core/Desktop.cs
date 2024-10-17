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

		internal static HashSet<string> ExtraProperties { get; } = "UISettings,IconURI,CoverURI,MetaTags,Stylesheets,ScriptLibraries,Scripts,MainPortletID,SEOSettings".ToHashSet();

		internal static HashSet<string> ExcludedAliases { get; } = (UtilityService.GetAppSetting("Portals:ExcludedAliases", "") + ",Files,Downloads,Images,Thumbnails,ThumbnailPngs,ThumbnailBigs,ThumbnailBigPngs,Default,Index,Feed,Atom,Rss").ToLower().ToHashSet();

		public static Desktop CreateDesktop(this ExpandoObject data, string excluded = null, Action<Desktop> onCompleted = null)
			=> Desktop.CreateInstance(data, excluded?.ToHashSet(), desktop =>
			{
				desktop.Alias = string.IsNullOrWhiteSpace(desktop.Alias) ? desktop.Title.NormalizeAlias() : desktop.Alias.NormalizeAlias();
				desktop.Aliases = string.IsNullOrWhiteSpace(desktop.Aliases) ? null : desktop.Aliases.Replace(",", ";").ToArray(";", true).Select(alias => alias.NormalizeAlias()).Where(alias => !DesktopProcessor.ExcludedAliases.Contains(alias) && !alias.IsEquals(desktop.Alias)).Join(";");
				desktop.SEOSettings = desktop.SEOSettings ?? new Settings.SEO();
				"TitleMode,DescriptionMode,KeywordsMode".ToList().ForEach(name =>
				{
					var value = data.Get<string>($"SEOSettings.{name}");
					desktop.SEOSettings.SetAttributeValue(name, !string.IsNullOrWhiteSpace(value) && value.TryToEnum(out Settings.SEOMode mode) ? mode as object : null);
				});
				desktop.SEOSettings = desktop.SEOSettings != null && desktop.SEOSettings.SEOInfo == null && desktop.SEOSettings.TitleMode == null && desktop.SEOSettings.DescriptionMode == null && desktop.SEOSettings.KeywordsMode == null ? null : desktop.SEOSettings;
				desktop.NormalizeExtras();
				onCompleted?.Invoke(desktop);
			});

		public static Desktop Update(this Desktop desktop, ExpandoObject data, string excluded = null, Action<Desktop> onCompleted = null)
			=> desktop.Fill(data, excluded?.ToHashSet(), _ =>
			{
				desktop.Alias = string.IsNullOrWhiteSpace(desktop.Alias) ? desktop.Title.NormalizeAlias() : desktop.Alias.NormalizeAlias();
				desktop.Aliases = string.IsNullOrWhiteSpace(desktop.Aliases) ? null : desktop.Aliases.Replace(",", ";").ToArray(";", true).Select(alias => alias.NormalizeAlias()).Where(alias => !DesktopProcessor.ExcludedAliases.Contains(alias) && !alias.IsEquals(desktop.Alias)).Join(";");
				desktop.SEOSettings = desktop.SEOSettings ?? new Settings.SEO();
				"TitleMode,DescriptionMode,KeywordsMode".ToList().ForEach(name =>
				{
					var value = data.Get<string>($"SEOSettings.{name}");
					desktop.SEOSettings.SetAttributeValue(name, !string.IsNullOrWhiteSpace(value) && value.TryToEnum(out Settings.SEOMode mode) ? mode as object : null);
				});
				desktop.SEOSettings = desktop.SEOSettings != null && desktop.SEOSettings.SEOInfo == null && desktop.SEOSettings.TitleMode == null && desktop.SEOSettings.DescriptionMode == null && desktop.SEOSettings.KeywordsMode == null ? null : desktop.SEOSettings;
				desktop.NormalizeExtras();
				onCompleted?.Invoke(desktop);
			});

		internal static Desktop Set(this Desktop desktop, bool clear = false, bool updateCache = false, IEnumerable<string> oldAliases = null)
		{
			if (desktop != null && !string.IsNullOrWhiteSpace(desktop.ID) && !string.IsNullOrWhiteSpace(desktop.Title))
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

				var newAliases = (desktop.Aliases ?? "").ToArray(";")
					.Where(alias => !string.IsNullOrWhiteSpace(alias))
					.Distinct(StringComparer.OrdinalIgnoreCase)
					.ToList();

				newAliases.ForEach(alias =>
				{
					var success = DesktopProcessor.DesktopsByAlias.TryAdd($"{desktop.SystemID}:{alias}", desktop);
					if (!success && DesktopProcessor.DesktopsByAlias.TryGetValue($"{desktop.SystemID}:{alias}", out var old))
						success = DesktopProcessor.DesktopsByAlias.TryUpdate($"{desktop.SystemID}:{alias}", desktop, old);
					if (success)
						Utility.NotRecognizedAliases.Remove($"Desktop:{desktop.SystemID}:{alias}");
				});

				(oldAliases ?? new List<string>())
					.Where(alias => !string.IsNullOrWhiteSpace(alias))
					.Except(newAliases.Concat(new[] { desktop.Alias }))
					.Distinct(StringComparer.OrdinalIgnoreCase)
					.ToList()
					.ForEach(alias =>
					{
						DesktopProcessor.DesktopsByAlias.Remove($"{desktop.SystemID}:{alias}");
						Utility.NotRecognizedAliases.Remove($"Desktop:{desktop.SystemID}:{alias}");
					});

				if (updateCache)
					Utility.Cache.SetAsync(desktop).Run();
			}
			return desktop;
		}

		internal static async Task<Desktop> SetAsync(this Desktop desktop, bool clear = false, bool updateCache = false, CancellationToken cancellationToken = default, IEnumerable<string> oldAliases = null)
		{
			desktop?.Set(clear, false, oldAliases);
			await (updateCache && desktop != null && !string.IsNullOrWhiteSpace(desktop.ID) && !string.IsNullOrWhiteSpace(desktop.Title) ? Utility.Cache.SetAsync(desktop, cancellationToken) : Task.CompletedTask).ConfigureAwait(false);
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
				(desktop.Aliases ?? "").ToArray(";").ForEach(alias => DesktopProcessor.DesktopsByAlias.Remove($"{desktop.SystemID}:{alias}"));
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

			var desktop = !force && DesktopProcessor.DesktopsByAlias.TryGetValue($"{systemID}:{alias}", out var dvalue) ? dvalue : null;
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
			await desktops.ForEachAsync(async desktop => await desktop.SetAsync(false, updateCache, cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
			return desktops;
		}

		internal static async Task ProcessInterCommunicateMessageOfDesktopAsync(this CommunicateMessage message, CancellationToken cancellationToken = default)
		{
			if (message.Type.IsEndsWith("#Create"))
			{
				var desktop = message.Data.ToExpandoObject().CreateDesktop();
				desktop._portlets = null;
				desktop._childrenIDs = null;
				await Task.WhenAll
				(
					desktop.FindPortletsAsync(cancellationToken, false),
					desktop.FindChildrenAsync(cancellationToken, false)
				).ConfigureAwait(false);
				await desktop.Portlets.Where(portlet => !string.IsNullOrWhiteSpace(portlet.OriginalPortletID)).ForEachAsync(async portlet => portlet._originalPortlet = await Portlet.GetAsync<Portlet>(portlet.OriginalPortletID, cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
				desktop.Set(true);
			}

			else if (message.Type.IsEndsWith("#Update"))
			{
				var desktop = message.Data.Get("ID", "").GetDesktopByID(false, false);
				var oldAliases = (desktop?.Aliases ?? "").ToArray(";", true).Concat(new[] { desktop?.Alias }).ToList();
				desktop = desktop == null
					? message.Data.ToExpandoObject().CreateDesktop()
					: desktop.Update(message.Data.ToExpandoObject());
				desktop._portlets = null;
				desktop._childrenIDs = null;
				await Task.WhenAll
				(
					desktop.FindPortletsAsync(cancellationToken, false),
					desktop.FindChildrenAsync(cancellationToken, false)
				).ConfigureAwait(false);
				if (desktop.Portlets != null)
					await desktop.Portlets.Where(portlet => !string.IsNullOrWhiteSpace(portlet.OriginalPortletID)).ForEachAsync(async portlet => portlet._originalPortlet = await Portlet.GetAsync<Portlet>(portlet.OriginalPortletID, cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
				desktop.Set(true, false, oldAliases);
			}

			else if (message.Type.IsEndsWith("#Delete"))
				message.Data.ToExpandoObject().CreateDesktop().Remove();
		}

		internal static async Task ClearRelatedCacheAsync(this Desktop desktop, string oldParentID = null, CancellationToken cancellationToken = default, string correlationID = null, bool clearDataCache = true, bool clearHtmlCache = true, bool doRefresh = false)
		{
			// data cache keys
			var sort = Sorts<Desktop>.Ascending("Title");
			var dataCacheKeys = new List<string>();
			if (clearDataCache)
			{
				dataCacheKeys = Extensions.GetRelatedCacheKeys(DesktopProcessor.GetDesktopsFilter(desktop.SystemID, null), sort);
				if (!string.IsNullOrWhiteSpace(desktop.ParentID) && desktop.ParentID.IsValidUUID())
					dataCacheKeys = Extensions.GetRelatedCacheKeys(DesktopProcessor.GetDesktopsFilter(desktop.SystemID, desktop.ParentID), sort).Concat(dataCacheKeys).ToList();
				if (!string.IsNullOrWhiteSpace(oldParentID) && oldParentID.IsValidUUID())
					dataCacheKeys = Extensions.GetRelatedCacheKeys(DesktopProcessor.GetDesktopsFilter(desktop.SystemID, oldParentID), sort).Concat(dataCacheKeys).ToList();
				dataCacheKeys = dataCacheKeys.Distinct(StringComparer.OrdinalIgnoreCase).Concat(new[] { $"css#d_{desktop.ID}", $"css#d_{desktop.ID}:time", $"js#d_{desktop.ID}", $"js#d_{desktop.ID}:time" }).ToList();
			}

			// html cache keys (desktop HTMLs)
			var setKeys = clearHtmlCache ? await desktop.GetSetCacheKeysAsync(cancellationToken, true).ConfigureAwait(false) : new List<string>();
			var htmlCacheKeys = clearHtmlCache
				? desktop.GetDesktopCacheKeys($"{Utility.PortalsHttpURI}/~{desktop.Organization.Alias}/{desktop.Alias}").Concat(setKeys).ToList()
				: new List<string>();

			await Task.WhenAll
			(
				clearHtmlCache && setKeys.Any() ? Utility.Cache.RemoveSetMembersAsync(desktop.GetSetCacheKey(), setKeys, cancellationToken) : Task.CompletedTask,
				Utility.Cache.RemoveAsync(htmlCacheKeys.Concat(dataCacheKeys).Distinct(StringComparer.OrdinalIgnoreCase).ToList(), cancellationToken),
				Utility.IsCacheLogEnabled ? Utility.WriteLogAsync(correlationID, $"Clear related cache of desktop [{desktop.ID} => {desktop.Title}]\r\n- {dataCacheKeys.Count} data keys => {dataCacheKeys.Join(", ")}\r\n- {htmlCacheKeys.Count} html keys => {htmlCacheKeys.Join(", ")}", "Caches") : Task.CompletedTask,
				doRefresh ? $"{Utility.PortalsHttpURI}/~{desktop.Organization.Alias}/{desktop.Alias}?x-force-cache=x".RefreshWebPageAsync(1, correlationID, $"Refresh desktop when related cache of a desktop was clean [{desktop.Title} - ID: {desktop.ID}]") : Task.CompletedTask
			).ConfigureAwait(false);
		}

		internal static Task ClearCacheAsync(this Desktop desktop, CancellationToken cancellationToken, string correlationID = null, bool clearRelatedDataCache = true, bool clearRelatedHtmlCache = true, bool clearChildrenCache = false, bool doRefresh = false)
			=> Task.WhenAll((desktop._portlets ?? new List<Portlet>()).Select(portlet => portlet.ClearCacheAsync(cancellationToken, correlationID, clearRelatedDataCache, clearRelatedHtmlCache, doRefresh)).Concat(new[]
			{
				desktop.ClearRelatedCacheAsync(null, cancellationToken, correlationID, clearRelatedDataCache, clearRelatedHtmlCache, doRefresh),
				Utility.Cache.RemoveAsync(desktop.Remove(), cancellationToken),
				new CommunicateMessage(Utility.ServiceName)
				{
					Type = $"{desktop.GetObjectName()}#Delete",
					Data = desktop.ToJson(),
					ExcludedNodeID = Utility.NodeID
				}.SendAsync(),
				Utility.IsCacheLogEnabled ? Utility.WriteLogAsync(correlationID, $"Clear cache of a desktop [{desktop.Title} - ID: {desktop.ID}]", "Caches") : Task.CompletedTask,
				clearChildrenCache ? Task.WhenAll((desktop.Children ?? new List<Desktop>()).Select(webdesktop => webdesktop.ClearCacheAsync(cancellationToken, correlationID, clearRelatedDataCache, clearRelatedHtmlCache, clearChildrenCache, doRefresh))) : Task.CompletedTask
			}));

		internal static async Task<JObject> SearchDesktopsAsync(this RequestInfo requestInfo, bool isSystemAdministrator, CancellationToken cancellationToken)
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
			var organizationID = filter.GetValue("SystemID") ?? requestInfo.GetParameter("SystemID") ?? requestInfo.GetParameter("x-system-id") ?? requestInfo.GetParameter("OrganizationID");
			var organization = await (organizationID ?? "").GetOrganizationByIDAsync(cancellationToken).ConfigureAwait(false);
			if (organization == null)
				throw new InformationExistedException("The organization is invalid");

			// check permission
			var gotRights = isSystemAdministrator || requestInfo.Session.User.IsViewer(null, null, organization);
			if (!gotRights)
				throw new AccessDeniedException();

			// process cache
			var addChildren = "true".IsEquals(requestInfo.GetHeaderParameter("x-children"));
			var cachedJson = string.IsNullOrWhiteSpace(query) && !addChildren
				? await Utility.Cache.GetAsync<string>(Extensions.GetCacheKeyOfObjectsJson(filter, sort, pageSize, pageNumber), cancellationToken).ConfigureAwait(false)
				: null;
			if (!string.IsNullOrWhiteSpace(cachedJson))
				return JObject.Parse(cachedJson);

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
					: await Desktop.SearchAsync(query, filter, null, pageSize, pageNumber, cancellationToken).ConfigureAwait(false)
				: new List<Desktop>();

			// build response
			pagination = new Tuple<long, int, int, int>(totalRecords, totalPages, pageSize, pageNumber);

			if (addChildren)
				await objects.Where(desktop => desktop._childrenIDs == null || desktop._portlets == null).ForEachAsync(async desktop =>
				{
					if (desktop._childrenIDs == null)
						await desktop.FindChildrenAsync(cancellationToken, false).ConfigureAwait(false);
					if (desktop._portlets == null)
						await desktop.FindPortletsAsync(cancellationToken, false).ConfigureAwait(false);
					await desktop.SetAsync(false, true, cancellationToken).ConfigureAwait(false);
				}, true, false).ConfigureAwait(false);

			var response = new JObject()
			{
				{ "FilterBy", filter.ToClientJson(query) },
				{ "SortBy", sort?.ToClientJson() },
				{ "Pagination", pagination.GetPagination() },
				{ "Objects", objects.Select(desktop => desktop.ToJson(addChildren, false)).ToJArray() }
			};

			// update cache
			if (string.IsNullOrWhiteSpace(query) && !addChildren)
				Utility.Cache.SetAsync(Extensions.GetCacheKeyOfObjectsJson(filter, sort, pageSize, pageNumber), response.ToString(Formatting.None)).Run();

			// response
			return response;
		}

		internal static async Task<JObject> CreateDesktopAsync(this RequestInfo requestInfo, bool isSystemAdministrator, CancellationToken cancellationToken)
		{
			// prepare
			var request = requestInfo.GetBodyExpando();
			var organizationID = request.Get<string>("SystemID") ?? requestInfo.GetParameter("SystemID") ?? requestInfo.GetParameter("x-system-id") ?? requestInfo.GetParameter("OrganizationID");
			var organization = await (organizationID ?? "").GetOrganizationByIDAsync(cancellationToken).ConfigureAwait(false) ?? throw new InformationInvalidException("The organization is invalid");

			// check permission
			var gotRights = isSystemAdministrator || requestInfo.Session.User.IsModerator(null, null, organization);
			if (!gotRights)
				throw new AccessDeniedException();

			// check alias
			var alias = request.Get("Alias", "");
			if (!string.IsNullOrWhiteSpace(alias) && DesktopProcessor.ExcludedAliases.Contains(alias.NormalizeAlias()))
				throw new AliasIsExistedException($"The alias ({alias.NormalizeAlias()}) is used by another desktop");

			var existing = string.IsNullOrWhiteSpace(alias) ? null : await organization.ID.GetDesktopByAliasAsync(alias.NormalizeAlias(), cancellationToken).ConfigureAwait(false);
			if (existing != null && existing.Alias.IsEquals(alias))
				throw new AliasIsExistedException($"The alias ({alias.NormalizeAlias()}) is used by another desktop");

			// source to copy from
			var source = await request.Get("CopyFromID", "").GetDesktopByIDAsync(cancellationToken).ConfigureAwait(false);

			// validate template, meta-tags and scripts
			if (source == null)
			{
				request.Get("Template", "").ValidateTemplate();
				request.Get("MetaTags", "").ValidateTags();
			}

			// create new
			var desktop = source != null
				? source.Copy("ID,Title,Alias,Aliases,Created,CreatedID,LastModified,LastModifiedID,Portlets".ToHashSet(), obj =>
				{
					obj.ID = UtilityService.NewUUID;
					obj.ParentID = request.Get<string>("ParentID");
					obj.Title = request.Get("Title", $"{source.Title} (Copied)");
					obj._childrenIDs = new List<string>();
				})
				: request.CreateDesktop("SystemID,Privileges,OriginalPrivileges,Alias,Created,CreatedID,LastModified,LastModifiedID", obj =>
				{
					obj.ID = string.IsNullOrWhiteSpace(obj.ID) || !obj.ID.IsValidUUID() ? UtilityService.NewUUID : obj.ID;
					obj._childrenIDs = new List<string>();
				});

			desktop.SystemID = organization.ID;
			desktop.ParentID = desktop.ParentDesktop?.ID;
			desktop.Alias = (string.IsNullOrWhiteSpace(alias) ? $"{desktop.Title}-{UtilityService.GetUUID(desktop.ID, null, true)}" : alias).NormalizeAlias();
			desktop.Created = desktop.LastModified = DateTime.Now;
			desktop.CreatedID = desktop.LastModifiedID = requestInfo.Session.User.ID;

			await Desktop.CreateAsync(desktop, cancellationToken).ConfigureAwait(false);
			await desktop.Set(existing != null).ClearRelatedCacheAsync(null, cancellationToken, requestInfo.CorrelationID, false, false).ConfigureAwait(false);

			// copy portlets
			if (source != null)
			{
				desktop._portlets = new List<Portlet>();
				var excluded = "ID,DesktopID,Created,CreatedID,LastModified,LastModifiedID".ToHashSet();
				await (source.Portlets ?? new List<Portlet>()).ForEachAsync(async originalPortlet =>
				{
					var copiedPortlet = originalPortlet.Copy(excluded, obj =>
					{
						obj.ID = UtilityService.NewUUID;
						obj.Created = obj.LastModified = DateTime.Now;
						obj.CreatedID = obj.LastModifiedID = requestInfo.Session.User.ID;
						obj.DesktopID = desktop.ID;
					});
					await Portlet.CreateAsync(copiedPortlet, cancellationToken).ConfigureAwait(false);
					desktop._portlets.Add(copiedPortlet);
				}, true, false).ConfigureAwait(false);
				await desktop.SetAsync(false, true, cancellationToken).ConfigureAwait(false);
			}

			var objectName = desktop.GetObjectName();

			// update parent
			var parentDesktop = desktop.ParentDesktop;
			if (parentDesktop != null)
			{
				await parentDesktop.FindChildrenAsync(cancellationToken, false).ConfigureAwait(false);
				if (parentDesktop._childrenIDs.IndexOf(desktop.ID) < 0)
					parentDesktop._childrenIDs.Add(desktop.ID);
				await parentDesktop.SetAsync(false, true, cancellationToken).ConfigureAwait(false);

				var json = parentDesktop.ToJson(true, false);
				new UpdateMessage
				{
					Type = $"{requestInfo.ServiceName}#{objectName}#Update",
					Data = json,
					DeviceID = "*"
				}.Send();
				new CommunicateMessage(requestInfo.ServiceName)
				{
					Type = $"{objectName}#Create",
					Data = json,
					ExcludedNodeID = Utility.NodeID
				}.Send();
			}

			// message to update to all other connected clients
			var response = desktop.ToJson();
			if (desktop.ParentDesktop == null)
				new UpdateMessage
				{
					Type = $"{requestInfo.ServiceName}#{objectName}#Create",
					DeviceID = "*",
					Data = response
				}.Send();

			// message to update to all service instances (on all other nodes)
			new CommunicateMessage(requestInfo.ServiceName)
			{
				Type = $"{objectName}#Create",
				Data = response,
				ExcludedNodeID = Utility.NodeID
			}.Send();

			// send notification
			await desktop.SendNotificationAsync("Create", desktop.Organization.Notifications, ApprovalStatus.Published, ApprovalStatus.Published, requestInfo, cancellationToken).ConfigureAwait(false);

			// response
			return response;
		}

		internal static async Task<JObject> GetDesktopAsync(this RequestInfo requestInfo, bool isSystemAdministrator, CancellationToken cancellationToken)
		{
			// prepare
			var identity = requestInfo.GetObjectIdentity(true, true) ?? "";
			var desktop = await (identity.IsValidUUID() ? identity.GetDesktopByIDAsync(cancellationToken) : identity.GetDesktopByAliasAsync(identity, cancellationToken)).ConfigureAwait(false);
			if (desktop == null)
				throw new InformationNotFoundException();
			else if (desktop.Organization == null)
				throw new InformationInvalidException("The organization is invalid");

			// check permission
			var gotRights = isSystemAdministrator || requestInfo.Session.User.IsViewer(null, null, desktop.Organization);
			if (!gotRights)
				throw new AccessDeniedException();

			if (!identity.IsValidUUID())
				return new JObject
				{
					{ "ID", desktop.ID },
					{ "Title", desktop.Title },
					{ "Alias", desktop.Alias }
				};

			// refresh (clear cached and reload)
			var isRefresh = "refresh".IsEquals(requestInfo.GetObjectIdentity());
			if (isRefresh || desktop._childrenIDs == null || desktop._portlets == null)
			{
				if (isRefresh)
				{
					await desktop.ClearRelatedCacheAsync(null, cancellationToken, requestInfo.CorrelationID, true, false).ConfigureAwait(false);
					await Utility.Cache.RemoveAsync(desktop, cancellationToken).ConfigureAwait(false);
					desktop = await desktop.Remove().ID.GetDesktopByIDAsync(cancellationToken, true).ConfigureAwait(false);
					desktop._childrenIDs = null;
					desktop._portlets = null;
				}
				if (desktop._childrenIDs == null)
					await desktop.FindChildrenAsync(cancellationToken, false).ConfigureAwait(false);
				if (desktop._portlets == null)
					await desktop.FindPortletsAsync(cancellationToken, false).ConfigureAwait(false);
				await desktop.SetAsync(false, true, cancellationToken).ConfigureAwait(false);
			}

			// response
			var versions = await desktop.FindVersionsAsync(cancellationToken, false).ConfigureAwait(false);
			var response = desktop.ToJson(true, false);
			new UpdateMessage
			{
				Type = $"{requestInfo.ServiceName}#{desktop.GetObjectName()}#Update",
				Data = response.UpdateVersions(versions),
				DeviceID = "*",
				ExcludedDeviceID = isRefresh ? "" : requestInfo.Session.DeviceID
			}.Send();
			if (isRefresh)
				new CommunicateMessage(requestInfo.ServiceName)
				{
					Type = $"{desktop.GetObjectName()}#Update",
					Data = response,
					ExcludedNodeID = Utility.NodeID
				}.Send();
			return response;
		}

		internal static async Task<JObject> UpdateDesktopAsync(this RequestInfo requestInfo, bool isSystemAdministrator, CancellationToken cancellationToken)
		{
			// prepare
			var desktop = await (requestInfo.GetObjectIdentity() ?? "").GetDesktopByIDAsync(cancellationToken).ConfigureAwait(false);
			if (desktop == null)
				throw new InformationNotFoundException();
			else if (desktop.Organization == null)
				throw new InformationInvalidException("The organization is invalid");

			// check
			var gotRights = isSystemAdministrator || requestInfo.Session.User.IsModerator(null, null, desktop.Organization);
			if (!gotRights)
				throw new AccessDeniedException();

			var request = requestInfo.GetBodyExpando();
			var oldParentID = desktop.ParentID;

			var oldAlias = desktop.Alias;
			var alias = request.Get("Alias", "");
			if (!string.IsNullOrWhiteSpace(alias) && !oldAlias.IsEquals(alias) && DesktopProcessor.ExcludedAliases.Contains(alias.NormalizeAlias()))
				throw new AliasIsExistedException($"The alias ({alias.NormalizeAlias()}) is used by another purpose");

			var existing = string.IsNullOrWhiteSpace(alias) || oldAlias.IsEquals(alias) ? null : await desktop.SystemID.GetDesktopByAliasAsync(alias.NormalizeAlias(), cancellationToken).ConfigureAwait(false);
			if (existing != null && !existing.ID.Equals(desktop.ID) && existing.Alias.IsEquals(alias))
				throw new AliasIsExistedException($"The alias ({alias.NormalizeAlias()}) is used by another desktop");

			// validate template & meta-tags
			request.Get("Template", "").ValidateTemplate();
			request.Get("MetaTags", "").ValidateTags();

			// update
			var oldAliases = (desktop.Aliases ?? "").ToArray(";", true).Concat(new[] { oldAlias }).ToList();
			desktop.Update(request, "ID,SystemID,Privileges,OriginalPrivileges,Created,CreatedID,LastModified,LastModifiedID", async _ =>
			{
				desktop.LastModified = DateTime.Now;
				desktop.LastModifiedID = requestInfo.Session.User.ID;
				await desktop.FindChildrenAsync(cancellationToken, false).ConfigureAwait(false);
			});
			await Desktop.UpdateAsync(desktop, requestInfo.Session.User.ID, cancellationToken).ConfigureAwait(false);
			await desktop.Set(existing != null, false, oldAliases).ClearRelatedCacheAsync(oldParentID, cancellationToken, requestInfo.CorrelationID).ConfigureAwait(false);

			var objectName = desktop.GetObjectName();

			// update parent
			var parentDesktop = desktop.ParentDesktop;
			if (parentDesktop != null && !desktop.ParentID.IsEquals(oldParentID))
			{
				await parentDesktop.FindChildrenAsync(cancellationToken, false).ConfigureAwait(false);
				if (parentDesktop._childrenIDs.IndexOf(desktop.ID) < 0)
					parentDesktop._childrenIDs.Add(desktop.ID);
				await parentDesktop.SetAsync(false, true, cancellationToken).ConfigureAwait(false);

				var json = parentDesktop.ToJson(true, false);
				new UpdateMessage
				{
					Type = $"{requestInfo.ServiceName}#{objectName}#Update",
					Data = json,
					DeviceID = "*"
				}.Send();
				new CommunicateMessage(requestInfo.ServiceName)
				{
					Type = $"{objectName}#Update",
					Data = json,
					ExcludedNodeID = Utility.NodeID
				}.Send();
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
					new UpdateMessage
					{
						Type = $"{requestInfo.ServiceName}#{objectName}#Update",
						Data = json,
						DeviceID = "*"
					}.Send();
					new CommunicateMessage(requestInfo.ServiceName)
					{
						Type = $"{objectName}#Update",
						Data = json,
						ExcludedNodeID = Utility.NodeID
					}.Send();
				}
			}

			// message to update to all other connected clients
			var response = desktop.ToJson(true, false);
			var versions = await desktop.FindVersionsAsync(cancellationToken, false).ConfigureAwait(false);
			new UpdateMessage
			{
				Type = $"{requestInfo.ServiceName}#{objectName}#Update",
				Data = response.UpdateVersions(versions),
				DeviceID = "*"
			}.Send();

			// message to update to all service instances (on all other nodes)
			new CommunicateMessage(requestInfo.ServiceName)
			{
				Type = $"{objectName}#Update",
				Data = response,
				ExcludedNodeID = Utility.NodeID
			}.Send();

			// send notification
			await desktop.SendNotificationAsync("Update", desktop.Organization.Notifications, ApprovalStatus.Published, ApprovalStatus.Published, requestInfo, cancellationToken).ConfigureAwait(false);

			// response
			return response;
		}

		internal static async Task<JObject> UpdateDesktopPortletsAsync(this RequestInfo requestInfo, bool isSystemAdministrator, CancellationToken cancellationToken)
		{
			// prepare
			var desktop = await (requestInfo.GetObjectIdentity() ?? "").GetDesktopByIDAsync(cancellationToken).ConfigureAwait(false);
			if (desktop == null)
				throw new InformationNotFoundException();
			else if (desktop.Organization == null)
				throw new InformationInvalidException("The organization is invalid");

			// check
			var gotRights = isSystemAdministrator || requestInfo.Session.User.IsModerator(null, null, desktop.Organization);
			if (!gotRights)
				throw new AccessDeniedException();

			// update portlets
			await desktop.FindPortletsAsync(cancellationToken, false).ConfigureAwait(false);
			var portlets = desktop.Portlets;
			await requestInfo.GetBodyJson().Get<JArray>("Portlets").ForEachAsync(async info =>
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
			}, true, false).ConfigureAwait(false);
			await desktop.Set(false, true).ClearRelatedCacheAsync(null, cancellationToken, requestInfo.CorrelationID, false, true, false).ConfigureAwait(false);

			// send update messages
			var objectName = desktop.GetObjectName();
			var response = desktop.ToJson(true, false);
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

			// response
			return response;
		}

		internal static async Task<JObject> DeleteDesktopAsync(this RequestInfo requestInfo, bool isSystemAdministrator, CancellationToken cancellationToken)
		{
			// prepare
			var desktop = await (requestInfo.GetObjectIdentity() ?? "").GetDesktopByIDAsync(cancellationToken).ConfigureAwait(false);
			if (desktop == null)
				throw new InformationNotFoundException();
			else if (desktop.Organization == null)
				throw new InformationInvalidException("The organization is invalid");

			// check permission
			var gotRights = isSystemAdministrator || requestInfo.Session.User.IsAdministrator(null, null, desktop.Organization);
			if (!gotRights)
				throw new AccessDeniedException();

			// delete
			var objectName = desktop.GetObjectName();
			var updateChildren = requestInfo.Header.TryGetValue("x-children", out var childrenMode) && "set-null".IsEquals(childrenMode);

			var children = await desktop.FindChildrenAsync(cancellationToken, false).ConfigureAwait(false) ?? new List<Desktop>();
			await children.ForEachAsync(async child =>
			{
				// update children to root
				if (updateChildren)
				{
					child.ParentID = null;
					child.LastModified = DateTime.Now;
					child.LastModifiedID = requestInfo.Session.User.ID;

					await Desktop.UpdateAsync(child, requestInfo.Session.User.ID, cancellationToken).ConfigureAwait(false);
					await Task.WhenAll
					(
						child.Set().ClearRelatedCacheAsync(null, cancellationToken, requestInfo.CorrelationID, true, false, false),
						child.SendNotificationAsync("Update", child.Organization.Notifications, ApprovalStatus.Published, ApprovalStatus.Published, requestInfo, cancellationToken)
					).ConfigureAwait(false);

					var json = child.ToJson(true, false);
					new UpdateMessage
					{
						Type = $"{requestInfo.ServiceName}#{objectName}#Update",
						Data = json,
						DeviceID = "*"
					}.Send();
					new CommunicateMessage(requestInfo.ServiceName)
					{
						Type = $"{objectName}#Update",
						Data = json,
						ExcludedNodeID = Utility.NodeID
					}.Send();
				}

				// delete children
				else
					await child.DeleteChildrenAsync(requestInfo, cancellationToken).ConfigureAwait(false);
			}, true, false).ConfigureAwait(false);

			await Desktop.DeleteAsync<Desktop>(desktop.ID, requestInfo.Session.User.ID, cancellationToken).ConfigureAwait(false);
			await desktop.ClearCacheAsync(cancellationToken, requestInfo.CorrelationID).ConfigureAwait(false);

			// message to update to all other connected clients
			var response = desktop.ToJson();
			new UpdateMessage
			{
				Type = $"{requestInfo.ServiceName}#{objectName}#Delete",
				Data = response,
				DeviceID = "*"
			}.Send();

			// message to update to all service instances (on all other nodes)
			new CommunicateMessage(requestInfo.ServiceName)
			{
				Type = $"{objectName}#Delete",
				Data = response,
				ExcludedNodeID = Utility.NodeID
			}.Send();

			// send notification
			await desktop.SendNotificationAsync("Delete", desktop.Organization.Notifications, ApprovalStatus.Published, ApprovalStatus.Published, requestInfo, cancellationToken).ConfigureAwait(false);

			// response
			return response;
		}

		static async Task DeleteChildrenAsync(this Desktop desktop, RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			// prepare
			var objectName = desktop.GetObjectName();

			// delete childrenn
			var children = await desktop.FindChildrenAsync(cancellationToken, false).ConfigureAwait(false) ?? new List<Desktop>();
			await children.ForEachAsync(async child => await child.DeleteChildrenAsync(requestInfo, cancellationToken).ConfigureAwait(false), true, false).ConfigureAwait(false);

			// delete
			await Desktop.DeleteAsync<Desktop>(desktop.ID, requestInfo.Session.User.ID, cancellationToken).ConfigureAwait(false);
			await desktop.ClearCacheAsync(cancellationToken, requestInfo.CorrelationID).ConfigureAwait(false);

			// send notification
			await desktop.SendNotificationAsync("Delete", desktop.Organization.Notifications, ApprovalStatus.Published, ApprovalStatus.Published, requestInfo, cancellationToken).ConfigureAwait(false);

			// prepare update messages
			var json = desktop.ToJson();
			new UpdateMessage
			{
				Type = $"{requestInfo.ServiceName}#{objectName}#Delete",
				Data = json,
				DeviceID = "*"
			}.Send();
			new CommunicateMessage(requestInfo.ServiceName)
			{
				Type = $"{objectName}#Delete",
				Data = json,
				ExcludedNodeID = Utility.NodeID
			}.Send();
		}

		internal static async Task<JObject> SyncDesktopAsync(this RequestInfo requestInfo, CancellationToken cancellationToken, bool sendNotifications = false, bool dontCreateNewVersion = false)
		{
			var @event = requestInfo.GetParameter("event") ?? requestInfo.GetParameter("x-original-event");
			if (string.IsNullOrWhiteSpace(@event) || !@event.IsEquals("Delete"))
				@event = "Update";

			var data = requestInfo.GetBodyExpando();
			var desktop = await data.Get<string>("ID").GetDesktopByIDAsync(cancellationToken).ConfigureAwait(false);
			var oldAliases = (desktop?.Aliases ?? "").ToArray(";", true).Concat(new[] { desktop?.Alias }).ToList();

			if (!@event.IsEquals("Delete"))
			{
				if (desktop == null)
				{
					desktop = Desktop.CreateInstance(data, null, obj => obj.Extras = data.Get<string>("Extras") ?? obj.Extras);
					await Desktop.CreateAsync(desktop, cancellationToken).ConfigureAwait(false);
				}
				else
					await Desktop.UpdateAsync(desktop.Update(data, null, obj => obj.Extras = data.Get<string>("Extras") ?? obj.Extras), dontCreateNewVersion, cancellationToken).ConfigureAwait(false);
			}
			else if (desktop != null)
				await Desktop.DeleteAsync<Desktop>(desktop.ID, desktop.LastModifiedID, cancellationToken).ConfigureAwait(false);

			// clear related cache
			if (requestInfo.GetHeaderParameter("x-converter") == null || @event.IsEquals("Delete"))
				await desktop.ClearCacheAsync(cancellationToken, requestInfo.CorrelationID).ConfigureAwait(false);
			else
				await desktop.ClearRelatedCacheAsync(null, cancellationToken, requestInfo.CorrelationID).ConfigureAwait(false);

			// send notifications
			if (sendNotifications)
				await desktop.SendNotificationAsync(@event, desktop.Organization.Notifications, ApprovalStatus.Published, ApprovalStatus.Published, requestInfo, cancellationToken).ConfigureAwait(false);

			// send update messages
			var json = @event.IsEquals("Delete")
				? desktop.Remove().ToJson()
				: desktop.Set(false, false, oldAliases).ToJson();
			var objectName = desktop.GetObjectName();
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
			return json;
		}

		internal static async Task<JObject> RollbackDesktopAsync(this RequestInfo requestInfo, bool isSystemAdministrator, CancellationToken cancellationToken)
		{
			// prepare
			var desktop = await (requestInfo.GetObjectIdentity() ?? "").GetDesktopByIDAsync(cancellationToken).ConfigureAwait(false);
			if (desktop == null)
				throw new InformationNotFoundException();
			else if (desktop.Organization == null)
				throw new InformationInvalidException("The organization is invalid");

			// check
			var gotRights = isSystemAdministrator || requestInfo.Session.User.IsModerator(null, null, desktop.Organization);
			if (!gotRights)
				throw new AccessDeniedException();

			// rollback
			var oldParentID = desktop.ParentID;
			var oldAliases = (desktop.Aliases ?? "").ToArray(";", true).Concat(new[] { desktop.Alias }).ToList();
			desktop = await RepositoryMediator.RollbackAsync<Desktop>(requestInfo.GetParameter("x-version-id") ?? "", requestInfo.Session.User.ID, cancellationToken).ConfigureAwait(false);
			await Task.WhenAll
			(
				desktop.ClearRelatedCacheAsync(oldParentID, cancellationToken, requestInfo.CorrelationID),
				desktop.SendNotificationAsync("Rollback", desktop.Organization.Notifications, ApprovalStatus.Published, ApprovalStatus.Published, requestInfo, cancellationToken)
			).ConfigureAwait(false);

			// send update messages
			var objectName = desktop.GetObjectName();
			var versions = await desktop.FindVersionsAsync(cancellationToken, false).ConfigureAwait(false);
			var response = desktop.Set(true, true, oldAliases).ToJson(true, false);
			new UpdateMessage
			{
				Type = $"{requestInfo.ServiceName}#{objectName}#Update",
				Data = response.UpdateVersions(versions),
				DeviceID = "*"
			}.Send();
			new CommunicateMessage(requestInfo.ServiceName)
			{
				Type = $"{objectName}#Update",
				Data = response,
				ExcludedNodeID = Utility.NodeID
			}.Send();
			return response;
		}
	}
}
﻿#region Related components
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
#endregion

namespace net.vieapps.Services.Portals
{
	public static class SiteProcessor
	{
		internal static ConcurrentDictionary<string, Site> Sites { get; } = new ConcurrentDictionary<string, Site>(StringComparer.OrdinalIgnoreCase);

		internal static ConcurrentDictionary<string, Site> SitesByDomain { get; } = new ConcurrentDictionary<string, Site>(StringComparer.OrdinalIgnoreCase);

		internal static HashSet<string> ExtraProperties { get; } = "AlwaysUseHTTPs,AlwaysReturnHTTPs,UISettings,IconURI,CoverURI,MetaTags,Stylesheets,ScriptLibraries,Scripts,RedirectToNoneWWW,UseInlineStylesheets,UseInlineScripts,CanonicalHost,SEOInfo".ToHashSet();

		public static Site CreateSite(this ExpandoObject data, string excluded = null, Action<Site> onCompleted = null)
			=> Site.CreateInstance(data, excluded?.ToHashSet(), site =>
			{
				site.PrimaryDomain = site.PrimaryDomain.Trim().ToArray(".").Select(name => name.NormalizeAlias(false)).Join(".");
				site.SubDomain = site.SubDomain.Trim().Equals("*") ? site.SubDomain.Trim() : site.SubDomain.NormalizeAlias(false);
				site.OtherDomains = string.IsNullOrWhiteSpace(site.OtherDomains) ? null : site.OtherDomains.Replace(",", ";").ToArray(";", true).Select(domain => domain.ToArray(".").Select(name => name.NormalizeAlias(false)).Join(".")).Where(domain => !domain.IsEquals(site.PrimaryDomain)).Join(";");
				site.NormalizeExtras();
				onCompleted?.Invoke(site);
			});

		public static Site Update(this Site site, ExpandoObject data, string excluded = null, Action<Site> onCompleted = null)
			=> site.Fill(data, excluded?.ToHashSet(), _ =>
			{
				site.PrimaryDomain = site.PrimaryDomain.Trim().ToArray(".").Select(name => name.NormalizeAlias(false)).Join(".");
				site.SubDomain = site.SubDomain.Trim().Equals("*") ? site.SubDomain.Trim() : site.SubDomain.NormalizeAlias(false);
				site.OtherDomains = string.IsNullOrWhiteSpace(site.OtherDomains) ? null : site.OtherDomains.Replace(",", ";").ToArray(";", true).Select(domain => domain.ToArray(".").Select(name => name.NormalizeAlias(false)).Join(".")).Where(domain => !domain.IsEquals(site.PrimaryDomain)).Join(";");
				site.NormalizeExtras();
				onCompleted?.Invoke(site);
			});

		internal static Site Prepare(this Site site, string domain, bool update = true)
		{
			if (!string.IsNullOrWhiteSpace(site.Organization?.FakePortalsHttpURI) && new Uri(site.Organization.FakePortalsHttpURI).Host.IsEquals(domain))
			{
				Utility.NotRecognizedAliases.Add($"Site:{domain}");
				site = null;
			}
			else if (update)
			{
				Utility.NotRecognizedAliases.Remove($"Site:{domain}");
				new CommunicateMessage(Utility.ServiceName)
				{
					Type = $"{site.GetObjectName()}#Update",
					Data = site.ToJson(),
					ExcludedNodeID = Utility.NodeID
				}.Send();
			}
			return site;
		}

		internal static Site Set(this Site site, bool clear = false, bool updateCache = false, IEnumerable<string> oldDomains = null)
		{
			if (site != null && !string.IsNullOrWhiteSpace(site.ID) && !string.IsNullOrWhiteSpace(site.Title))
			{
				if (clear)
					site.Remove();

				if (updateCache)
					Utility.Cache.SetAsync(site).Run();

				SiteProcessor.Sites[site.ID] = site;

				var newDomains = new[] { $"{site.SubDomain}.{site.PrimaryDomain}" }
					.Concat((site.OtherDomains ?? "").ToArray(";", true))
					.Where(domain => !string.IsNullOrWhiteSpace(domain))
					.Select(domain => domain.NormalizeDomain().Replace("*.", "").ToLower())
					.Distinct(StringComparer.OrdinalIgnoreCase)
					.ToList();

				newDomains.ForEach(domain =>
				{
					var success = SiteProcessor.SitesByDomain.TryAdd($"*.{domain}", site);
					if (!success && SiteProcessor.SitesByDomain.TryGetValue($"*.{domain}", out var old))
						success = SiteProcessor.SitesByDomain.TryUpdate($"*.{domain}", site, old);
					if (success)
						Utility.NotRecognizedAliases.Remove($"Site:{domain}");
				});

				(oldDomains ?? new List<string>())
					.Where(domain => !string.IsNullOrWhiteSpace(domain))
					.Select(domain => domain.NormalizeDomain().Replace("*.", "").ToLower())
					.Except(newDomains)
					.Distinct(StringComparer.OrdinalIgnoreCase)
					.ToList()
					.ForEach(domain =>
					{
						SiteProcessor.SitesByDomain.Remove($"*.{domain}");
						Utility.NotRecognizedAliases.Remove($"Site:{domain}");
					});
			}

			return site;
		}

		internal static async Task<Site> SetAsync(this Site site, bool clear = false, bool updateCache = false, CancellationToken cancellationToken = default, IEnumerable<string> oldDomains = null)
		{
			site?.Set(clear, false, oldDomains);
			await (updateCache && site != null && !string.IsNullOrWhiteSpace(site.ID) && !string.IsNullOrWhiteSpace(site.Title) ? Utility.Cache.SetAsync(site, cancellationToken) : Task.CompletedTask).ConfigureAwait(false);
			return site;
		}

		internal static Site Remove(this Site site)
			=> (site?.ID ?? "").RemoveSite();

		internal static Site RemoveSite(this string id)
		{
			if (!string.IsNullOrWhiteSpace(id) && SiteProcessor.Sites.TryGetValue(id, out var site) && site != null)
			{
				SiteProcessor.Sites.Remove(site.ID);
				new[] { $"{site.SubDomain}.{site.PrimaryDomain}" }
					.Concat((site.OtherDomains ?? "").ToArray(";", true))
					.Where(domain => !string.IsNullOrWhiteSpace(domain))
					.Select(domain => domain.NormalizeDomain().Replace("*.", "").ToLower())
					.Distinct(StringComparer.OrdinalIgnoreCase)
					.ForEach(domain =>
					{
						SiteProcessor.SitesByDomain.Remove($"*.{domain}");
						Utility.NotRecognizedAliases.Remove($"Site:{domain}");
					});
				return site;
			}
			return null;
		}

		public static Site GetSiteByID(this string id, bool force = false, bool fetchRepository = true)
			=> !force && !string.IsNullOrWhiteSpace(id) && SiteProcessor.Sites.TryGetValue(id, out var site)
				? site
				: fetchRepository && !string.IsNullOrWhiteSpace(id)
					? Site.Get<Site>(id)?.Set()
					: null;

		public static async Task<Site> GetSiteByIDAsync(this string id, CancellationToken cancellationToken = default, bool force = false)
			=> (id ?? "").GetSiteByID(force, false) ?? (await Site.GetAsync<Site>(id, cancellationToken).ConfigureAwait(false))?.Set();

		static FilterBys<Site> GetFilterBy(this string domain, bool searchOtherDomains = true)
		{
			var host = domain.NormalizeDomain().ToArray(".");
			var subDomain = host.First();
			var primaryDomain = host.Skip(1).Join(".");
			var filter = Filters<Site>.Or(Filters<Site>.And(Filters<Site>.Equals("SubDomain", subDomain), Filters<Site>.Equals("PrimaryDomain", primaryDomain)));
			if (!subDomain.Equals("*"))
				filter.Add(Filters<Site>.And(Filters<Site>.Equals("SubDomain", "*"), Filters<Site>.Equals("PrimaryDomain", primaryDomain)));
			if (searchOtherDomains)
				filter.Add(Filters<Site>.Contains("OtherDomains", subDomain.Equals("*") || subDomain.Equals("www") ? primaryDomain : host.Join(".")));
			return filter;
		}

		static Site GetSiteByDomain(this List<Site> sites, string domain)
		{
			var host = domain.ToArray(".");
			var subDomain = host.First();
			var primaryDomain = host.Skip(1).Join(".");
			return sites.FirstOrDefault(site => (site.SubDomain.Equals("*") ? primaryDomain.IsEquals(site.PrimaryDomain) : domain.IsEquals($"{site.SubDomain}.{site.PrimaryDomain}")) || site.OtherDomains.ToList(";").Any(host => primaryDomain.IsEquals(host)));
		}

		public static Site GetSiteByDomain(this string domain, bool fetchRepository = true)
		{
			var name = (domain ?? "").NormalizeDomain().Replace("*.", "");
			if (string.IsNullOrWhiteSpace(name) || Utility.NotRecognizedAliases.Contains($"Site:{name}"))
				return null;

			if (!SiteProcessor.SitesByDomain.TryGetValue($"*.{name}", out var site) || site == null)
			{
				var host = name;
				var dotOffset = host.IndexOf(".");
				while (site == null && dotOffset > 0)
					if (!SiteProcessor.SitesByDomain.TryGetValue($"*.{host}", out site) || site == null)
					{
						host = host.Right(host.Length - dotOffset - 1);
						dotOffset = host.IndexOf(".");
					}
			}

			if (site == null && fetchRepository && !Utility.NotRecognizedAliases.Contains($"Site:{name}"))
			{
				site = Site.Find(domain.GetFilterBy(), null, 0, 1, null).GetSiteByDomain(domain);
				if (site != null)
					site = site.Prepare(name)?.Set();
				else if (Utility.DefaultSite == null)
					Utility.NotRecognizedAliases.Add($"Site:{name}");
			}

			return site;
		}

		public static async Task<Site> GetSiteByDomainAsync(this string domain, CancellationToken cancellationToken = default, bool fetchRepository = true)
		{
			var name = (domain ?? "").NormalizeDomain().Replace("*.", "");
			var site = name.GetSiteByDomain(false);
			if (site == null && fetchRepository && !string.IsNullOrWhiteSpace(name) && !Utility.NotRecognizedAliases.Contains($"Site:{name}"))
			{
				site = (await Site.FindAsync(domain.GetFilterBy(), null, 0, 1, null, cancellationToken).ConfigureAwait(false)).GetSiteByDomain(name);
				if (site != null)
					site = site.Prepare(name)?.Set();
				else if (Utility.DefaultSite == null)
					Utility.NotRecognizedAliases.Add($"Site:{name}");
			}
			return site;
		}

		public static List<Site> FindSites(this string systemID, bool updateCache = true)
		{
			if (string.IsNullOrWhiteSpace(systemID) || !systemID.IsValidUUID())
				return new List<Site>();

			var filter = Filters<Site>.And(Filters<Site>.Equals("SystemID", systemID));
			var sort = Sorts<Site>.Ascending("PrimaryDomain").ThenByAscending("SubDomain").ThenByAscending("Title");
			var sites = Site.Find(filter, sort, 0, 1, Extensions.GetCacheKey(filter, sort));
			sites.ForEach(site =>
			{
				if (site.ID.GetSiteByID(false, false) == null)
					site.Set(updateCache);
			});

			return sites;
		}

		public static Task<List<Site>> FindSitesAsync(this string systemID, CancellationToken cancellationToken = default, bool updateCache = true)
			=> string.IsNullOrWhiteSpace(systemID) || !systemID.IsValidUUID() ? Task.FromResult(new List<Site>()) : SiteProcessor.FindSitesAsync(systemID, null, updateCache, cancellationToken);

		internal static async Task<List<Site>> FindSitesAsync(string systemID, string cacheKey, bool updateCache, CancellationToken cancellationToken)
		{
			var filter = string.IsNullOrWhiteSpace(systemID) || !systemID.IsValidUUID() ? null : Filters<Site>.And(Filters<Site>.Equals("SystemID", systemID));
			var sort = Sorts<Site>.Ascending("PrimaryDomain").ThenByAscending("SubDomain").ThenByAscending("Title");
			var sites = await Site.FindAsync(filter, sort, 0, 1, cacheKey ?? Extensions.GetCacheKey(filter, sort), cancellationToken).ConfigureAwait(false);
			await sites.ForEachAsync(site => site.ID.GetSiteByID(false, false) == null ? site.SetAsync(false, updateCache, cancellationToken) : Task.CompletedTask).ConfigureAwait(false);
			return sites;
		}

		internal static async Task<Site> RefreshAsync(this Site site, CancellationToken cancellationToken, bool updateCache = true, bool sendCommunicatingMessage = true, bool sendUpdatingMessage = false)
		{
			// refresh (remove cache and reload)
			await Utility.Cache.RemoveAsync(site, cancellationToken).ConfigureAwait(false);
			site = await site.Remove().ID.GetSiteByIDAsync(cancellationToken, true).ConfigureAwait(false);

			// update cache
			await site.SetAsync(false, updateCache, cancellationToken).ConfigureAwait(false);

			// send messages
			if (sendCommunicatingMessage)
				new CommunicateMessage(Utility.ServiceName)
				{
					Type = $"{site.GetObjectName()}#Update",
					Data = site.ToJson(),
					ExcludedNodeID = Utility.NodeID
				}.Send();
			if (sendUpdatingMessage)
				new UpdateMessage
				{
					Type = $"{Utility.ServiceName}#{site.GetObjectName()}#Update",
					Data = site.ToJson(),
					DeviceID = "*"
				}.Send();

			return site;
		}

		internal static Task ProcessInterCommunicateMessageOfSiteAsync(this CommunicateMessage message, CancellationToken cancellationToken = default)
		{
			if (message.Type.IsEndsWith("#Create"))
				message.Data.ToExpandoObject().CreateSite().Set(true);

			else if (message.Type.IsEndsWith("#Update"))
			{
				var site = message.Data.Get("ID", "").GetSiteByID(false, false);
				var oldDomains = site != null ? new[] { $"{site.SubDomain}.{site.PrimaryDomain}" }.Concat((site.OtherDomains ?? "").ToArray(";", true)).ToList() : new List<string>();
				site = site == null
					? message.Data.ToExpandoObject().CreateSite()
					: site.Update(message.Data.ToExpandoObject());
				site.Set(true, false, oldDomains);
			}

			else if (message.Type.IsEndsWith("#Delete"))
				message.Data.ToExpandoObject().CreateSite().Remove();

			return Task.CompletedTask;
		}

		internal static async Task ClearRelatedCacheAsync(this Site site, CancellationToken cancellationToken, string correlationID = null, bool clearDataCache = true, bool clearHtmlCache = true, bool doRefresh = true)
		{
			// data cache keys
			var sort = Sorts<Site>.Ascending("PrimaryDomain").ThenByAscending("SubDomain").ThenByAscending("Title");
			var dataCacheKeys = clearDataCache
				? Extensions.GetRelatedCacheKeys(Filters<Site>.And(), Sorts<Site>.Ascending("Title"))
					.Concat(Extensions.GetRelatedCacheKeys(Filters<Site>.And(), sort))
					.Concat(Extensions.GetRelatedCacheKeys(Filters<Site>.And(Filters<Site>.Equals("SystemID", site.SystemID)), sort))
					.Concat(Extensions.GetRelatedCacheKeys(Filters<Site>.And(Filters<Site>.Equals("SystemID", site.SystemID)), Sorts<Site>.Ascending("Title")))
					.Distinct(StringComparer.OrdinalIgnoreCase)
					.ToList()
				: new List<string>();

			// html cache keys (desktop HTMLs)
			var htmlCacheKeys = clearHtmlCache
				? site.Organization.GetDesktopCacheKey().Concat(await site.GetSetCacheKeysAsync(cancellationToken).ConfigureAwait(false)).ToList()
				: new List<string>();

			// clear related cache
			await Task.WhenAll
			(
				Utility.Cache.RemoveAsync(htmlCacheKeys.Concat(dataCacheKeys).Distinct(StringComparer.OrdinalIgnoreCase).ToList(), cancellationToken),
				Utility.IsCacheLogEnabled ? Utility.WriteLogAsync(correlationID, $"Clear related cache of a site [{site.Title} - ID: {site.ID}]\r\n- {dataCacheKeys.Count} data keys => {dataCacheKeys.Join(", ")}\r\n- {htmlCacheKeys.Count} html keys => {htmlCacheKeys.Join(", ")}", "Caches") : Task.CompletedTask,
				doRefresh ? $"{Utility.PortalsHttpURI}/~{site.Organization.Alias}?x-force-cache=x".RefreshWebPageAsync(1, correlationID, $"Refresh home desktop when related cache of a site was clean [{site.Title} - ID: {site.ID}]") : Task.CompletedTask
			).ConfigureAwait(false);
		}

		internal static Task ClearCacheAsync(this Site site, CancellationToken cancellationToken, string correlationID = null, bool clearRelatedDataCache = true, bool clearRelatedHtmlCache = true, bool doRefresh = true)
			=> Task.WhenAll
			(
				site.ClearRelatedCacheAsync(cancellationToken, correlationID, clearRelatedDataCache, clearRelatedHtmlCache, doRefresh),
				Utility.Cache.RemoveAsync(site.Remove(), cancellationToken),
				new CommunicateMessage(Utility.ServiceName)
				{
					Type = $"{site.GetObjectName()}#Delete",
					Data = site.ToJson(),
					ExcludedNodeID = Utility.NodeID
				}.SendAsync(),
				Utility.IsCacheLogEnabled ? Utility.WriteLogAsync(correlationID, $"Clear cache of a site [{site.Title} - ID: {site.ID}]", "Caches") : Task.CompletedTask
			);

		internal static async Task<JObject> SearchSitesAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, CancellationToken cancellationToken = default)
		{
			// prepare
			var request = requestInfo.GetRequestExpando();

			var query = request.Get<string>("FilterBy.Query");
			var filter = request.Get<ExpandoObject>("FilterBy", null)?.ToFilterBy<Site>() ?? Filters<Site>.And();
			var sort = string.IsNullOrWhiteSpace(query) ? request.Get<ExpandoObject>("SortBy")?.ToSortBy<Site>() ?? Sorts<Site>.Ascending("Title") : null;

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

				gotRights = requestInfo.Session.User.IsModerator(null, null, organization);
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
					? await Site.CountAsync(filter, Extensions.GetCacheKeyOfTotalObjects(filter, sort), cancellationToken).ConfigureAwait(false)
					: await Site.CountAsync(query, filter, cancellationToken).ConfigureAwait(false);

			var totalPages = new Tuple<long, int>(totalRecords, pageSize).GetTotalPages();
			if (totalPages > 0 && pageNumber > totalPages)
				pageNumber = totalPages;

			// search
			var objects = totalRecords > 0
				? string.IsNullOrWhiteSpace(query)
					? await Site.FindAsync(filter, sort, pageSize, pageNumber, Extensions.GetCacheKey(filter, sort, pageSize, pageNumber), cancellationToken).ConfigureAwait(false)
					: await Site.SearchAsync(query, filter, null, pageSize, pageNumber, cancellationToken).ConfigureAwait(false)
				: new List<Site>();

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
				Utility.Cache.SetAsync(Extensions.GetCacheKeyOfObjectsJson(filter, sort, pageSize, pageNumber), response.ToString(Formatting.Indented)).Run();

			// response
			return response;
		}

		internal static async Task<JObject> CreateSiteAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, CancellationToken cancellationToken = default)
		{
			// prepare
			var request = requestInfo.GetBodyExpando();
			var organizationID = request.Get<string>("SystemID") ?? requestInfo.GetParameter("x-system-id") ?? requestInfo.GetParameter("SystemID");
			var organization = await (organizationID ?? "").GetOrganizationByIDAsync(cancellationToken).ConfigureAwait(false);
			if (organization == null)
				throw new InformationInvalidException("The organization is invalid");

			// check permission
			var gotRights = isSystemAdministrator || requestInfo.Session.User.IsModerator(null, null, organization);
			if (!gotRights)
				throw new AccessDeniedException();

			// check domain
			var domain = $"{request.Get<string>("SubDomain")}.{request.Get<string>("PrimaryDomain")}";
			var existing = await domain.GetSiteByDomainAsync(cancellationToken).ConfigureAwait(false);
			if (existing != null && domain.NormalizeDomain().IsEquals($"{existing.SubDomain}.{existing.PrimaryDomain}"))
				throw new InformationExistedException($"The domain ({domain.NormalizeDomain()}) was used by another site");

			// validate meta-tags
			request.Get("MetaTags", "").ValidateTags();

			// create new
			var site = request.CreateSite("SystemID,Privileges,OriginalPrivileges,Created,CreatedID,LastModified,LastModifiedID", obj =>
			{
				obj.ID = string.IsNullOrWhiteSpace(obj.ID) || !obj.ID.IsValidUUID() ? UtilityService.NewUUID : obj.ID;
				obj.SystemID = organization.ID;
				obj.Created = obj.LastModified = DateTime.Now;
				obj.CreatedID = obj.LastModifiedID = requestInfo.Session.User.ID;
			});
			await Site.CreateAsync(site, cancellationToken).ConfigureAwait(false);

			// update cache
			await site.Set(existing != null).ClearRelatedCacheAsync(cancellationToken, requestInfo.CorrelationID).ConfigureAwait(false);

			// update organization
			if (organization._siteIDs == null)
				await organization.FindSitesAsync(cancellationToken).ConfigureAwait(false);
			organization._siteIDs.Add(site.ID);
			organization.Set(false, true);

			// send update messages
			var response = site.ToJson();
			var objectName = site.GetObjectName();
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
			new CommunicateMessage(requestInfo.ServiceName)
			{
				Type = $"{organization.GetTypeName(true)}#Update",
				Data = organization.ToJson(),
				ExcludedNodeID = Utility.NodeID
			}.Send();

			// send notification
			await site.SendNotificationAsync("Create", site.Organization.Notifications, ApprovalStatus.Draft, site.Status, requestInfo, cancellationToken).ConfigureAwait(false);

			// update refreshing task
			site.Organization.SendRefreshingTasks(false, false);

			// response
			return response;
		}

		internal static async Task<JObject> GetSiteAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, CancellationToken cancellationToken = default)
		{
			// prepare
			var identity = requestInfo.GetObjectIdentity(true, true) ?? "";
			var site = await (identity.IsValidUUID() ? identity.GetSiteByIDAsync(cancellationToken) : identity.GetSiteByDomainAsync(cancellationToken)).ConfigureAwait(false);
			if (site == null)
				throw new InformationNotFoundException();
			else if (site.Organization == null)
				throw new InformationInvalidException("The organization is invalid");

			// check permission
			var gotRights = isSystemAdministrator || requestInfo.Session.User.IsViewer(null, null, site.Organization);
			if (!gotRights)
				throw new AccessDeniedException();

			// refresh (clear cached and reload)
			var isRefresh = "refresh".IsEquals(requestInfo.GetObjectIdentity());
			site = isRefresh
				? await site.RefreshAsync(cancellationToken).ConfigureAwait(false)
				: site;

			// response
			var versions = await site.FindVersionsAsync(cancellationToken, false).ConfigureAwait(false);
			var response = site.ToJson();
			new UpdateMessage
			{
				Type = $"{requestInfo.ServiceName}#{site.GetObjectName()}#Update",
				Data = response.UpdateVersions(versions),
				DeviceID = "*",
				ExcludedDeviceID = isRefresh ? "" : requestInfo.Session.DeviceID
			}.Send();
			return response;
		}

		internal static async Task<JObject> UpdateAsync(this Site site, RequestInfo requestInfo, ApprovalStatus oldStatus, CancellationToken cancellationToken, IEnumerable<string> oldDomains = null, string @event = null)
		{
			// update
			await Site.UpdateAsync(site.Set(true, false, oldDomains), requestInfo.Session.User.ID, cancellationToken).ConfigureAwait(false);

			// send update messages
			var response = site.ToJson();
			var objectName = site.GetObjectName();
			var versions = await site.FindVersionsAsync(cancellationToken, false).ConfigureAwait(false);
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

			// update refreshing task
			site.Organization.SendRefreshingTasks(false, false);

			// clear cache & send notification
			Task.WhenAll
			(
				site.ClearRelatedCacheAsync(Utility.CancellationToken, requestInfo.CorrelationID, true, true, false),
				site.SendNotificationAsync(@event ?? "Update", site.Organization.Notifications, oldStatus, site.Status, requestInfo, Utility.CancellationToken)
			).Run();

			// response
			return response;
		}

		internal static async Task<JObject> UpdateSiteAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, CancellationToken cancellationToken = default)
		{
			// prepare
			var site = await (requestInfo.GetObjectIdentity() ?? "").GetSiteByIDAsync(cancellationToken).ConfigureAwait(false);
			if (site == null)
				throw new InformationNotFoundException();
			else if (site.Organization == null)
				throw new InformationInvalidException("The organization is invalid");

			// check permission
			var gotRights = isSystemAdministrator || requestInfo.Session.User.IsModerator(null, null, site.Organization);
			if (!gotRights)
				throw new AccessDeniedException();

			// check domain
			var oldDomains = new[] { $"{site.SubDomain}.{site.PrimaryDomain}" }.Concat((site.OtherDomains ?? "").ToArray(";", true)).ToList();
			var oldStatus = site.Status;

			var request = requestInfo.GetBodyExpando();
			var domain = $"{request.Get<string>("SubDomain")}.{request.Get<string>("PrimaryDomain")}";
			var existing = await domain.GetSiteByDomainAsync(cancellationToken).ConfigureAwait(false);
			if (existing != null && !existing.ID.IsEquals(site.ID) && domain.NormalizeDomain().IsEquals($"{existing.SubDomain}.{existing.PrimaryDomain}"))
				throw new InformationExistedException($"The domain '{domain.NormalizeDomain()}' was used by another site");

			// validate meta-tags
			request.Get("MetaTags", "").ValidateTags();

			// gathering information
			site.Update(request, "ID,SystemID,Privileges,OriginalPrivileges,Created,CreatedID,LastModified,LastModifiedID", _ =>
			{
				site.LastModified = DateTime.Now;
				site.LastModifiedID = requestInfo.Session.User.ID;
			});

			// update
			return await site.UpdateAsync(requestInfo, oldStatus, cancellationToken, oldDomains).ConfigureAwait(false);
		}

		internal static async Task<JObject> DeleteSiteAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, CancellationToken cancellationToken = default)
		{
			// prepare
			var site = await (requestInfo.GetObjectIdentity() ?? "").GetSiteByIDAsync(cancellationToken).ConfigureAwait(false);
			if (site == null)
				throw new InformationNotFoundException();
			else if (site.Organization == null)
				throw new InformationInvalidException("The organization is invalid");

			// check permission
			var gotRights = isSystemAdministrator || requestInfo.Session.User.IsAdministrator(null, null, site.Organization);
			if (!gotRights)
				throw new AccessDeniedException();

			// delete
			await Site.DeleteAsync<Site>(site.ID, requestInfo.Session.User.ID, cancellationToken).ConfigureAwait(false);
			site.Remove();

			// update organization
			var organization = site.Organization;
			if (organization != null && organization._siteIDs != null)
			{
				organization._siteIDs.Remove(site.ID);
				await organization.SetAsync(false, true, cancellationToken).ConfigureAwait(false);
				new UpdateMessage
				{
					Type = $"{requestInfo.ServiceName}#{organization.GetObjectName()}#Update",
					Data = organization.ToJson(),
					DeviceID = "*"
				}.Send();
				new CommunicateMessage(requestInfo.ServiceName)
				{
					Type = $"{organization.GetObjectName()}#Update",
					Data = organization.ToJson(),
					ExcludedNodeID = Utility.NodeID
				}.Send();
			}

			// send update messages
			var response = site.ToJson();
			var objectName = site.GetObjectName();

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

			new CommunicateMessage(requestInfo.ServiceName)
			{
				Type = $"{site.Organization.GetTypeName(true)}#Update",
				Data = site.Organization.ToJson(),
				ExcludedNodeID = Utility.NodeID
			}.Send();

			// update cache & send notification
			Task.WhenAll
			(
				site.ClearCacheAsync(Utility.CancellationToken, requestInfo.CorrelationID, true, true, false),
				site.SendNotificationAsync("Delete", site.Organization.Notifications, site.Status, site.Status, requestInfo, Utility.CancellationToken)
			).Run();

			// response
			return response;
		}

		internal static async Task<JObject> SyncSiteAsync(this RequestInfo requestInfo, CancellationToken cancellationToken, bool sendNotifications = false, bool dontCreateNewVersion = false)
		{
			var @event = requestInfo.GetParameter("event") ?? requestInfo.GetParameter("x-original-event");
			if (string.IsNullOrWhiteSpace(@event) || !@event.IsEquals("Delete"))
				@event = "Update";

			var data = requestInfo.GetBodyExpando();
			var site = await data.Get<string>("ID").GetSiteByIDAsync(cancellationToken).ConfigureAwait(false);
			var oldStatus = site != null ? site.Status : ApprovalStatus.Pending;

			if (!@event.IsEquals("Delete"))
			{
				if (site == null)
				{
					site = Site.CreateInstance(data, null, obj => obj.Extras = data.Get<string>("Extras") ?? obj.Extras);
					await Site.CreateAsync(site, cancellationToken).ConfigureAwait(false);
				}
				else
					await Site.UpdateAsync(site.Update(data, null, obj => obj.Extras = data.Get<string>("Extras") ?? obj.Extras), dontCreateNewVersion, cancellationToken).ConfigureAwait(false);
			}
			else if (site != null)
				await Site.DeleteAsync<Site>(site.ID, site.LastModifiedID, cancellationToken).ConfigureAwait(false);

			// clear related cache
			if (requestInfo.GetHeaderParameter("x-converter") == null || @event.IsEquals("Delete"))
				await site.ClearCacheAsync(cancellationToken, requestInfo.CorrelationID).ConfigureAwait(false);
			else
				await site.ClearRelatedCacheAsync(cancellationToken, requestInfo.CorrelationID).ConfigureAwait(false);

			// send notification
			if (sendNotifications)
				await site.SendNotificationAsync(@event, site.Organization.Notifications, oldStatus, site.Status, requestInfo, cancellationToken).ConfigureAwait(false);

			// send update messages
			var json = @event.IsEquals("Delete")
				? site.Remove().ToJson()
				: site.Set().ToJson();
			var objectName = site.GetObjectName();

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

		internal static async Task<JObject> RollbackSiteAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, CancellationToken cancellationToken = default)
		{
			// prepare
			var site = await (requestInfo.GetObjectIdentity() ?? "").GetSiteByIDAsync(cancellationToken).ConfigureAwait(false);
			if (site == null)
				throw new InformationNotFoundException();
			else if (site.Organization == null)
				throw new InformationInvalidException("The organization is invalid");

			// check permission
			var gotRights = isSystemAdministrator || requestInfo.Session.User.IsModerator(null, null, site.Organization);
			if (!gotRights)
				throw new AccessDeniedException();

			// rollback
			var oldDomains = new[] { $"{site.SubDomain}.{site.PrimaryDomain}" }.Concat((site.OtherDomains ?? "").ToArray(";", true)).ToList();
			var oldStatus = site.Status;
			site = await RepositoryMediator.RollbackAsync<Site>(requestInfo.GetParameter("x-version-id") ?? "", requestInfo.Session.User.ID, cancellationToken).ConfigureAwait(false);
			await Task.WhenAll
			(
				site.ClearRelatedCacheAsync(cancellationToken, requestInfo.CorrelationID),
				site.SendNotificationAsync("Rollback", site.Organization.Notifications, oldStatus, site.Status, requestInfo, cancellationToken)
			).ConfigureAwait(false);

			// send update messages
			var versions = await site.FindVersionsAsync(cancellationToken, false).ConfigureAwait(false);
			var response = site.Set(true, true, oldDomains).ToJson();
			var objectName = site.GetObjectName();
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
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
#endregion

namespace net.vieapps.Services.Portals
{
	public static class SiteProcessor
	{
		internal static ConcurrentDictionary<string, Site> Sites { get; } = new ConcurrentDictionary<string, Site>(StringComparer.OrdinalIgnoreCase);

		internal static ConcurrentDictionary<string, Site> SitesByDomain { get; } = new ConcurrentDictionary<string, Site>(StringComparer.OrdinalIgnoreCase);

		internal static HashSet<string> ExtraProperties { get; } = "AlwaysUseHTTPs,UISettings,IconURI,CoverURI,MetaTags,Stylesheets,ScriptLibraries,Scripts,RedirectToNoneWWW,UseInlineStylesheets,UseInlineScripts,SEOInfo".ToHashSet();

		public static Site CreateSiteInstance(this ExpandoObject data, string excluded = null, Action<Site> onCompleted = null)
			=> Site.CreateInstance(data, excluded?.ToHashSet(), site =>
			{
				site.PrimaryDomain = site.PrimaryDomain.Trim().ToArray(".").Select(name => name.NormalizeAlias(false)).Join(".");
				site.SubDomain = site.SubDomain.Trim().Equals("*") ? site.SubDomain.Trim() : site.SubDomain.NormalizeAlias(false);
				site.OtherDomains = string.IsNullOrWhiteSpace(site.OtherDomains) ? null : site.OtherDomains.Replace(",", ";").ToList(";", true, true).Select(domain => domain.ToArray(".").Select(name => name.NormalizeAlias(false)).Join(".")).Where(domain => !domain.IsEquals(site.PrimaryDomain)).Join(";");
				onCompleted?.Invoke(site);
			});

		public static Site UpdateSiteInstance(this Site site, ExpandoObject data, string excluded = null, Action<Site> onCompleted = null)
			=> site.Fill(data, excluded?.ToHashSet(), _ =>
			{
				site.PrimaryDomain = site.PrimaryDomain.Trim().ToArray(".").Select(name => name.NormalizeAlias(false)).Join(".");
				site.SubDomain = site.SubDomain.Trim().Equals("*") ? site.SubDomain.Trim() : site.SubDomain.NormalizeAlias(false);
				site.OtherDomains = string.IsNullOrWhiteSpace(site.OtherDomains) ? null : site.OtherDomains.Replace(",", ";").ToList(";", true, true).Select(domain => domain.ToArray(".").Select(name => name.NormalizeAlias(false)).Join(".")).Where(domain => !domain.IsEquals(site.PrimaryDomain)).Join(";");
				onCompleted?.Invoke(site);
			});

		internal static Site Set(this Site site, bool clear = false, bool updateCache = false, IEnumerable<string> oldDomains = null)
		{
			if (site != null && !string.IsNullOrWhiteSpace(site.ID) && !string.IsNullOrWhiteSpace(site.Title))
			{
				if (clear)
					site.Remove();

				SiteProcessor.Sites[site.ID] = site;
				if (updateCache)
					Utility.Cache.Set(site);

				var newDomains = new[] { $"{site.SubDomain}.{site.PrimaryDomain}" }
					.Concat(site.OtherDomains.ToList(";"))
					.Where(domain => !string.IsNullOrWhiteSpace(domain))
					.Select(domain => domain.Replace("*.", "").ToLower())
					.Distinct(StringComparer.OrdinalIgnoreCase);

				newDomains.ForEach(domain =>
				{
					if (SiteProcessor.SitesByDomain.TryAdd($"*.{domain}", site))
						Utility.NotRecognizedAliases.Remove($"Site:{domain}");
				});

				(oldDomains ?? new List<string>())
					.Where(domain => !string.IsNullOrWhiteSpace(domain))
					.Select(domain => domain.Replace("*.", "").ToLower())
					.Except(newDomains)
					.Distinct(StringComparer.OrdinalIgnoreCase)
					.ForEach(domain =>
					{
						if (SiteProcessor.SitesByDomain.Remove($"*.{domain}"))
							Utility.NotRecognizedAliases.Remove($"Site:{domain}");
					});
			}

			return site;
		}

		internal static async Task<Site> SetAsync(this Site site, bool clear = false, bool updateCache = false, CancellationToken cancellationToken = default)
		{
			site?.Set(clear);
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
					.Concat(site.OtherDomains.ToList(";"))
					.Where(domain => !string.IsNullOrWhiteSpace(domain))
					.Select(domain => domain.Replace("*.", "").ToLower())
					.Distinct(StringComparer.OrdinalIgnoreCase)
					.ForEach(domain => SiteProcessor.SitesByDomain.Remove($"*.{domain}"));
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

		internal static FilterBys<Site> GetFilterBy(this string domain)
		{
			var host = domain.NormalizeDomain().ToArray(".");
			var filter = Filters<Site>.Or(Filters<Site>.And(Filters<Site>.Equals("SubDomain", host.First()), Filters<Site>.Equals("PrimaryDomain", host.Skip(1).Join("."))));
			if (!host.First().Equals("*"))
				filter.Add(Filters<Site>.And(Filters<Site>.Equals("SubDomain", "*"), Filters<Site>.Equals("PrimaryDomain", domain)));
			return filter;
		}

		internal static Site GetSiteByDomain(this List<Site> sites, string domain)
			=> sites.FirstOrDefault(site => domain.IsEquals($"{site.SubDomain}.{site.PrimaryDomain}".Replace("*.", "")) || site.OtherDomains.ToList(";").Any(host => domain.IsEquals(host)));

		public static Site GetSiteByDomain(this string domain, string defaultSiteIDWhenNotFound = null, bool fetchRepository = true)
		{
			if (string.IsNullOrWhiteSpace(domain) || Utility.NotRecognizedAliases.Contains($"Site:{domain.Replace("*.", "")}"))
				return (defaultSiteIDWhenNotFound ?? "").GetSiteByID(false, false);

			if (!SiteProcessor.SitesByDomain.TryGetValue($"*.{domain.Replace("*.", "")}", out var site) || site == null)
			{
				var host = domain.Replace("*.", "");
				var dotOffset = host.IndexOf(".");
				while (site == null && dotOffset > 0)
					if (!SiteProcessor.SitesByDomain.TryGetValue($"*.{host}", out site) || site == null)
					{
						host = host.Right(host.Length - dotOffset - 1);
						dotOffset = host.IndexOf(".");
					}
			}

			if (site == null && fetchRepository && !Utility.NotRecognizedAliases.Contains($"Site:{domain.Replace("*.", "")}"))
			{
				site = Site.Find(domain.GetFilterBy(), null, 0, 1, null).GetSiteByDomain(domain.Replace("*.", ""))?.Set();
				if (site == null)
					Utility.NotRecognizedAliases.Add($"Site:{domain.Replace("*.", "")}");
				else
					new CommunicateMessage(Utility.ServiceName)
					{
						Type = $"{site.GetTypeName(true)}#Update",
						Data = site.ToJson(),
						ExcludedNodeID = Utility.NodeID
					}.Send();
			}

			return site ?? (defaultSiteIDWhenNotFound ?? "").GetSiteByID(false, false);
		}

		public static async Task<Site> GetSiteByDomainAsync(this string domain, string defaultSiteIDWhenNotFound = null, CancellationToken cancellationToken = default)
		{
			var site = (domain ?? "").GetSiteByDomain(defaultSiteIDWhenNotFound, false);
			if (site == null && !Utility.NotRecognizedAliases.Contains($"Site:{domain?.Replace("*.", "")}"))
			{
				site = (await Site.FindAsync(domain?.GetFilterBy(), null, 0, 1, null, cancellationToken).ConfigureAwait(false)).GetSiteByDomain(domain?.Replace("*.", ""))?.Set();
				if (site == null)
					Utility.NotRecognizedAliases.Add($"Site:{domain?.Replace("*.", "")}");
				else
					new CommunicateMessage(Utility.ServiceName)
					{
						Type = $"{site.GetTypeName(true)}#Update",
						Data = site.ToJson(),
						ExcludedNodeID = Utility.NodeID
					}.Send();
			}
			return site ?? (defaultSiteIDWhenNotFound ?? "").GetSiteByID(false, false);
		}

		public static Task<Site> GetSiteByDomainAsync(this string domain, CancellationToken cancellationToken)
			=> (domain ?? "").GetSiteByDomainAsync(null, cancellationToken);

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

		public static async Task<List<Site>> FindSitesAsync(this string systemID, CancellationToken cancellationToken = default, bool updateCache = true)
		{
			if (string.IsNullOrWhiteSpace(systemID) || !systemID.IsValidUUID())
				return new List<Site>();

			var filter = Filters<Site>.And(Filters<Site>.Equals("SystemID", systemID));
			var sort = Sorts<Site>.Ascending("PrimaryDomain").ThenByAscending("SubDomain").ThenByAscending("Title");
			var sites = await Site.FindAsync(filter, sort, 0, 1, Extensions.GetCacheKey(filter, sort), cancellationToken).ConfigureAwait(false);
			await sites.ForEachAsync(async site =>
			{
				if (site.ID.GetSiteByID(false, false) == null)
					await site.SetAsync(false, updateCache, cancellationToken).ConfigureAwait(false);
			}).ConfigureAwait(false);

			return sites;
		}

		internal static Task ProcessInterCommunicateMessageOfSiteAsync(this CommunicateMessage message, CancellationToken cancellationToken = default)
		{
			if (message.Type.IsEndsWith("#Create"))
				message.Data.ToExpandoObject().CreateSiteInstance().Set();

			else if (message.Type.IsEndsWith("#Update"))
			{
				var site = message.Data.Get("ID", "").GetSiteByID(false, false);
				var oldDomains = site != null ? new[] { $"{site.SubDomain}.{site.PrimaryDomain}" }.Concat(site.OtherDomains.ToList(";")).ToList() : new List<string>();
				site = site == null
					? message.Data.ToExpandoObject().CreateSiteInstance()
					: site.UpdateSiteInstance(message.Data.ToExpandoObject());
				site.Set(false, false, oldDomains);
			}

			else if (message.Type.IsEndsWith("#Delete"))
				message.Data.ToExpandoObject().CreateSiteInstance().Remove();

			return Task.CompletedTask;
		}

		internal static async Task ClearRelatedCacheAsync(this Site site, CancellationToken cancellationToken, string correlationID = null, bool clearDataCache = true, bool clearHtmlCache = true, bool doRefresh = true)
		{
			// data cache keys
			var sort = Sorts<Site>.Ascending("PrimaryDomain").ThenByAscending("SubDomain").ThenByAscending("Title");
			var dataCacheKeys = clearDataCache
				? Extensions.GetRelatedCacheKeys(Filters<Site>.And(), Sorts<Site>.Ascending("Title"))
					.Concat(Extensions.GetRelatedCacheKeys(Filters<Site>.And(Filters<Site>.Equals("SystemID", site.SystemID)), Sorts<Site>.Ascending("Title")))
					.Concat(Extensions.GetRelatedCacheKeys(Filters<Site>.And(), sort))
					.Concat(Extensions.GetRelatedCacheKeys(Filters<Site>.And(Filters<Site>.Equals("SystemID", site.SystemID)), sort))
					.Distinct(StringComparer.OrdinalIgnoreCase)
					.ToList()
				: new List<string>();

			// html cache keys (desktop HTMLs)
			var htmlCacheKeys = clearHtmlCache
				? site.Organization.GetDesktopCacheKey().Concat(new[] { $"css#s_{site.ID}", $"css#s_{site.ID}:time", $"js#s_{site.ID}", $"js#s_{site.ID}:time", $"js#o_{site.OrganizationID}", $"js#o_{site.OrganizationID}:time" }).ToList()
				: new List<string>();

			// clear related cache
			await Utility.Cache.RemoveAsync(htmlCacheKeys.Concat(dataCacheKeys).Distinct(StringComparer.OrdinalIgnoreCase).ToList(), cancellationToken).ConfigureAwait(false);
			await Task.WhenAll
			(
				Utility.WriteCacheLogs ? Utility.WriteLogAsync(correlationID, $"Clear related cache of a site [{site.Title} - ID: {site.ID}]\r\n- {dataCacheKeys.Count} data keys => {dataCacheKeys.Join(", ")}\r\n- {htmlCacheKeys.Count} html keys => {htmlCacheKeys.Join(", ")}", cancellationToken, "Caches") : Task.CompletedTask,
				doRefresh ? $"{Utility.PortalsHttpURI}/~{site.Organization.Alias}/".RefreshWebPageAsync(1, correlationID, $"Refresh desktop when related cache of a site was clean [{site.Title} - ID: {site.ID}]") : Task.CompletedTask
			).ConfigureAwait(false);
		}

		internal static Task ClearCacheAsync(this Site site, CancellationToken cancellationToken, string correlationID = null, bool clearRelatedDataCache = true, bool clearRelatedHtmlCache = true, bool doRefresh = true)
			=> Task.WhenAll(new[]
			{
				site.ClearRelatedCacheAsync(cancellationToken, correlationID, clearRelatedDataCache, clearRelatedHtmlCache, doRefresh),
				Utility.Cache.RemoveAsync(site.Remove(), cancellationToken),
				new CommunicateMessage(ServiceBase.ServiceComponent.ServiceName)
				{
					Type = $"{site.GetObjectName()}#Delete",
					Data = site.ToJson(),
					ExcludedNodeID = Utility.NodeID
				}.SendAsync(),
				Utility.WriteCacheLogs ? Utility.WriteLogAsync(correlationID, $"Clear cache of a site [{site.Title} - ID: {site.ID}]", cancellationToken, "Caches") : Task.CompletedTask
			});

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

				gotRights = requestInfo.Session.User.IsModerator(null, null, organization, requestInfo.CorrelationID);
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
				await Utility.Cache.SetAsync(Extensions.GetCacheKeyOfObjectsJson(filter, sort, pageSize, pageNumber), response.ToString(Formatting.Indented), cancellationToken).ConfigureAwait(false);

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
			var gotRights = isSystemAdministrator || requestInfo.Session.User.IsModerator(null, null, organization, requestInfo.CorrelationID);
			if (!gotRights)
				throw new AccessDeniedException();

			// check domain
			var domain = $"{request.Get<string>("SubDomain")}.{request.Get<string>("PrimaryDomain")}";
			var existing = await domain.GetSiteByDomainAsync(cancellationToken).ConfigureAwait(false);
			if (existing != null)
				throw new InformationExistedException($"The domain ({domain.NormalizeDomain()}) was used by another site");

			// validate meta-tags
			request.Get("MetaTags", "").ValidateTags();

			// create new
			var site = request.CreateSiteInstance("SystemID,Privileges,OriginalPrivileges,Created,CreatedID,LastModified,LastModifiedID", obj =>
			{
				obj.ID = string.IsNullOrWhiteSpace(obj.ID) || !obj.ID.IsValidUUID() ? UtilityService.NewUUID : obj.ID;
				obj.SystemID = organization.ID;
				obj.Created = obj.LastModified = DateTime.Now;
				obj.CreatedID = obj.LastModifiedID = requestInfo.Session.User.ID;
				obj.NormalizeExtras();
			});
			await Site.CreateAsync(site, cancellationToken).ConfigureAwait(false);

			// update cache
			await site.Set().ClearRelatedCacheAsync(cancellationToken, requestInfo.CorrelationID).ConfigureAwait(false);

			// update organization
			if (organization._siteIDs == null)
				await organization.FindSitesAsync(cancellationToken).ConfigureAwait(false);
			organization._siteIDs.Add(site.ID);
			organization.Set(false, true);

			// send update messages
			var response = site.ToJson();
			var objectName = site.GetTypeName(true);
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

			// response
			return response;
		}

		internal static async Task<JObject> GetSiteAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, CancellationToken cancellationToken = default)
		{
			// prepare
			var identity = requestInfo.GetObjectIdentity() ?? "";
			var site = await (identity.IsValidUUID() ? identity.GetSiteByIDAsync(cancellationToken) : identity.GetSiteByDomainAsync(cancellationToken)).ConfigureAwait(false);
			if (site == null)
				throw new InformationNotFoundException();
			else if (site.Organization == null)
				throw new InformationInvalidException("The organization is invalid");

			// check permission
			var gotRights = isSystemAdministrator || requestInfo.Session.User.IsViewer(null, null, site.Organization, requestInfo.CorrelationID);
			if (!gotRights)
				throw new AccessDeniedException();

			if (!identity.IsValidUUID())
				return new JObject
				{
					{ "ID", site.ID },
					{ "Title", site.Title },
					{ "Domain", $"{site.SubDomain}.{site.PrimaryDomain}".Replace("*.", "www.") }
				};

			// send update message
			var response = site.ToJson();
			new UpdateMessage
			{
				Type = $"{requestInfo.ServiceName}#{site.GetTypeName(true)}#Update",
				Data = response,
				DeviceID = "*",
				ExcludedDeviceID = requestInfo.Session.DeviceID
			}.Send();

			// response
			return response;
		}

		internal static async Task<JObject> UpdateAsync(this Site site, RequestInfo requestInfo, ApprovalStatus oldStatus, CancellationToken cancellationToken, IEnumerable<string> oldDomains = null)
		{
			// update
			await Site.UpdateAsync(site.Set(false, false, oldDomains), requestInfo.Session.User.ID, cancellationToken).ConfigureAwait(false);

			// send update messages
			var response = site.ToJson();
			var objectName = site.GetTypeName(true);
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

			// clear cache & send notification
			Task.WhenAll
			(
				site.ClearRelatedCacheAsync(Utility.CancellationToken, requestInfo.CorrelationID, true, true, false),
				site.SendNotificationAsync("Update", site.Organization.Notifications, oldStatus, site.Status, requestInfo, Utility.CancellationToken)
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
			var gotRights = isSystemAdministrator || requestInfo.Session.User.IsModerator(null, null, site.Organization, requestInfo.CorrelationID);
			if (!gotRights)
				throw new AccessDeniedException();

			// check domain
			var oldDomains = new[] { $"{site.SubDomain}.{site.PrimaryDomain}" }.Concat(site.OtherDomains.ToList(";")).ToList();
			var oldStatus = site.Status;

			var request = requestInfo.GetBodyExpando();
			var domain = $"{request.Get<string>("SubDomain")}.{request.Get<string>("PrimaryDomain")}";
			var existing = await domain.GetSiteByDomainAsync(cancellationToken).ConfigureAwait(false);
			if (existing != null && !existing.ID.IsEquals(site.ID))
				throw new InformationExistedException($"The domain '{domain.NormalizeDomain()}' was used by another site");

			// validate meta-tags
			request.Get("MetaTags", "").ValidateTags();

			// gathering information
			site.UpdateSiteInstance(request, "ID,SystemID,Privileges,OriginalPrivileges,Created,CreatedID,LastModified,LastModifiedID", obj =>
			{
				obj.LastModified = DateTime.Now;
				obj.LastModifiedID = requestInfo.Session.User.ID;
				obj.NormalizeExtras();
			});

			// update
			return await site.UpdateAsync(requestInfo, oldStatus, cancellationToken).ConfigureAwait(false);
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
			var gotRights = isSystemAdministrator || requestInfo.Session.User.IsAdministrator(null, null, site.Organization, requestInfo.CorrelationID);
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
			var objectName = site.GetTypeName(true);

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

		internal static async Task<JObject> SyncSiteAsync(this RequestInfo requestInfo, CancellationToken cancellationToken, bool sendNotifications = false)
		{
			var @event = requestInfo.GetHeaderParameter("Event");
			if (string.IsNullOrWhiteSpace(@event) || !@event.IsEquals("Delete"))
				@event = "Update";

			var data = requestInfo.GetBodyExpando();
			var site = await data.Get<string>("ID").GetSiteByIDAsync(cancellationToken).ConfigureAwait(false);
			var oldStatus = site != null ? site.Status : ApprovalStatus.Pending;

			if (!@event.IsEquals("Delete"))
			{
				if (site == null)
				{
					site = Site.CreateInstance(data);
					site.Extras = data.Get<string>("Extras") ?? site.Extras;
					await Site.CreateAsync(site, cancellationToken).ConfigureAwait(false);
				}
				else
				{
					site.Fill(data);
					site.Extras = data.Get<string>("Extras") ?? site.Extras;
					await Site.UpdateAsync(site, true, cancellationToken).ConfigureAwait(false);
				}
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
			var objectName = site.GetTypeName(true);

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
				{ "ID", site.ID },
				{ "Type", objectName }
			};
		}
	}
}
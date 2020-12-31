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

		internal static Site Set(this Site site, bool clear = false, bool updateCache = false)
		{
			if (site != null && !string.IsNullOrWhiteSpace(site.ID) && !string.IsNullOrWhiteSpace(site.Title))
			{
				if (clear)
					site.Remove();

				SiteProcessor.Sites[site.ID] = site;
				SiteProcessor.SitesByDomain[$"{site.SubDomain}.{site.PrimaryDomain}"] = site;
				Utility.NotRecognizedAliases.Remove($"Site:{(site.SubDomain.Equals("*") ? "" : $"{site.SubDomain}.")}{site.PrimaryDomain}");

				if (!string.IsNullOrWhiteSpace(site.OtherDomains))
					site.OtherDomains.ToList(";").Where(domain => !string.IsNullOrWhiteSpace(domain)).ForEach(domain =>
					{
						if (SiteProcessor.SitesByDomain.TryAdd(domain, site))
						{
							SiteProcessor.SitesByDomain.TryAdd($"*.{domain}", site);
							Utility.NotRecognizedAliases.Remove($"Site:{domain}");
						}
					});

				if (updateCache)
					Utility.Cache.SetAsync(site).Run();
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
				SiteProcessor.SitesByDomain.Remove($"{site.SubDomain}.{site.PrimaryDomain}");
				if (!string.IsNullOrWhiteSpace(site.OtherDomains))
					site.OtherDomains.ToList(";").Where(domain => !string.IsNullOrWhiteSpace(domain)).ForEach(domain =>
					{
						if (SiteProcessor.SitesByDomain.Remove(domain))
							SiteProcessor.SitesByDomain.Remove($"*.{domain}");
					});
				return site;
			}
			return null;
		}

		public static Site GetSiteByID(this string id, bool force = false, bool fetchRepository = true)
			=> !force && !string.IsNullOrWhiteSpace(id) && SiteProcessor.Sites.ContainsKey(id)
				? SiteProcessor.Sites[id]
				: fetchRepository && !string.IsNullOrWhiteSpace(id)
					? Site.Get<Site>(id)?.Set()
					: null;

		public static async Task<Site> GetSiteByIDAsync(this string id, CancellationToken cancellationToken = default, bool force = false)
			=> (id ?? "").GetSiteByID(force, false) ?? (await Site.GetAsync<Site>(id, cancellationToken).ConfigureAwait(false))?.Set();

		public static Tuple<string, string> GetSiteDomains(this string domain)
		{
			var info = domain.NormalizeDomain().ToArray(".");
			return new Tuple<string, string>(info.Skip(1).Join("."), info.First());
		}

		public static Site GetSiteByDomain(this string domain, string defaultSiteIDWhenNotFound = null, bool fetchRepository = true)
		{
			if (string.IsNullOrWhiteSpace(domain) || Utility.NotRecognizedAliases.Contains($"Site:{domain}"))
				return (defaultSiteIDWhenNotFound ?? "").GetSiteByID(false, false);

			domain = domain.StartsWith("*.") ? domain.Right(domain.Length - 2) : domain;
			if (!SiteProcessor.SitesByDomain.TryGetValue(domain, out var site))
				SiteProcessor.SitesByDomain.TryGetValue($"*.{domain}", out site);

			if (site == null)
			{
				var name = domain;
				var dotOffset = name.IndexOf(".");
				if (dotOffset < 0)
					SiteProcessor.SitesByDomain.TryGetValue($"*.{name}", out site);
				else
					while (site == null && dotOffset > 0)
					{
						if (!SiteProcessor.SitesByDomain.TryGetValue(name, out site))
							SiteProcessor.SitesByDomain.TryGetValue($"*.{name}", out site);

						if (site == null)
						{
							name = name.Right(name.Length - dotOffset - 1);
							dotOffset = name.IndexOf(".");
						}
					}
			}

			if (site == null && fetchRepository && !Utility.NotRecognizedAliases.Contains($"Site:{domain}"))
			{
				var domains = domain.GetSiteDomains();
				var filter = Filters<Site>.Or(Filters<Site>.And(Filters<Site>.Equals("SubDomain", "*"), Filters<Site>.Equals("PrimaryDomain", domains.Item1)));
				if (!domains.Item2.Equals("*"))
				{
					filter.Add(Filters<Site>.And(Filters<Site>.Equals("SubDomain", domains.Item2), Filters<Site>.Equals("PrimaryDomain", domains.Item1)));
					filter.Add(Filters<Site>.And(Filters<Site>.Equals("SubDomain", "*"), Filters<Site>.Equals("PrimaryDomain", domain)));
					filter.Add(Filters<Site>.Contains("OtherDomains", domain));
				}
				else
					filter.Add(Filters<Site>.Contains("OtherDomains", domains.Item1));
				site = Site.Get<Site>(filter, null, null)?.Set();
				if (site == null)
					Utility.NotRecognizedAliases.Add($"Site:{domain}");
			}

			return site ?? (defaultSiteIDWhenNotFound ?? "").GetSiteByID(false, false);
		}

		public static async Task<Site> GetSiteByDomainAsync(this string domain, string defaultSiteIDWhenNotFound = null, CancellationToken cancellationToken = default)
		{
			var site = (domain ?? "").GetSiteByDomain(defaultSiteIDWhenNotFound, false);
			if (site == null && !Utility.NotRecognizedAliases.Contains($"Site:{domain}"))
			{
				var domains = domain.GetSiteDomains();
				var filter = Filters<Site>.Or(Filters<Site>.And(Filters<Site>.Equals("SubDomain", "*"), Filters<Site>.Equals("PrimaryDomain", domains.Item1)));
				if (!domains.Item2.Equals("*"))
				{
					filter.Add(Filters<Site>.And(Filters<Site>.Equals("SubDomain", domains.Item2), Filters<Site>.Equals("PrimaryDomain", domains.Item1)));
					filter.Add(Filters<Site>.And(Filters<Site>.Equals("SubDomain", "*"), Filters<Site>.Equals("PrimaryDomain", domain)));
					filter.Add(Filters<Site>.Contains("OtherDomains", domain));
				}
				else
					filter.Add(Filters<Site>.Contains("OtherDomains", domains.Item1));
				site = (await Site.GetAsync<Site>(filter, null, null, cancellationToken).ConfigureAwait(false))?.Set();
				if (site == null)
					Utility.NotRecognizedAliases.Add($"Site:{domain}");
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
			sites.ForEach(site =>
			{
				if (site.ID.GetSiteByID(false, false) == null)
					site.Set(updateCache);
			});

			return sites;
		}

		internal static Task ProcessInterCommunicateMessageOfSiteAsync(this CommunicateMessage message, CancellationToken cancellationToken = default)
		{
			if (message.Type.IsEndsWith("#Create"))
				message.Data.ToExpandoObject().CreateSiteInstance().Set();

			else if (message.Type.IsEndsWith("#Update"))
			{
				var site = message.Data.Get("ID", "").GetSiteByID(false, false);
				site = site == null
					? message.Data.ToExpandoObject().CreateSiteInstance()
					: site.UpdateSiteInstance(message.Data.ToExpandoObject());
				site.Set();
			}

			else if (message.Type.IsEndsWith("#Delete"))
				message.Data.ToExpandoObject().CreateSiteInstance().Remove();

			return Task.CompletedTask;
		}

		internal static Task ClearRelatedCacheAsync(this Site site, CancellationToken cancellationToken, string correlationID = null)
		{
			var sort = Sorts<Site>.Ascending("PrimaryDomain").ThenByAscending("SubDomain").ThenByAscending("Title");
			var cacheKeys = Extensions.GetRelatedCacheKeys(Filters<Site>.And(), Sorts<Site>.Ascending("Title"))
				.Concat(Extensions.GetRelatedCacheKeys(Filters<Site>.And(Filters<Site>.Equals("SystemID", site.SystemID)), Sorts<Site>.Ascending("Title")))
				.Concat(Extensions.GetRelatedCacheKeys(Filters<Site>.And(), sort))
				.Concat(Extensions.GetRelatedCacheKeys(Filters<Site>.And(Filters<Site>.Equals("SystemID", site.SystemID)), sort))
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.ToList();
			if (Utility.Logger.IsEnabled(LogLevel.Debug))
				Utility.WriteLogAsync(correlationID, $"Clear related cache of site [{site.ID} => {site.Title}]\r\n{cacheKeys.Count} keys => {cacheKeys.Join(", ")}", CancellationToken.None, "Caches").Run();
			return Utility.Cache.RemoveAsync(cacheKeys, cancellationToken);
		}

		internal static Task ClearRelatedCacheAsync(this Site site, string correlationID = null)
			=> site.ClearRelatedCacheAsync(CancellationToken.None, correlationID);

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
				var organizationID = filter.GetValue("SystemID") ?? requestInfo.GetParameter("x-system-id") ?? requestInfo.GetParameter("SystemID");
				var organization = await (organizationID ?? "").GetOrganizationByIDAsync(cancellationToken).ConfigureAwait(false);
				if (organization == null)
					throw new InformationExistedException("The organization is invalid");

				gotRights = requestInfo.Session.User.ID.IsEquals(organization.OwnerID) || requestInfo.Session.User.IsModerator(organization.WorkingPrivileges);
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
					? await Site.CountAsync(filter, Extensions.GetCacheKeyOfTotalObjects(filter, sort), cancellationToken).ConfigureAwait(false)
					: await Site.CountAsync(query, filter, cancellationToken).ConfigureAwait(false);

			var totalPages = new Tuple<long, int>(totalRecords, pageSize).GetTotalPages();
			if (totalPages > 0 && pageNumber > totalPages)
				pageNumber = totalPages;

			// search
			var objects = totalRecords > 0
				? string.IsNullOrWhiteSpace(query)
					? await Site.FindAsync(filter, sort, pageSize, pageNumber, Extensions.GetCacheKey(filter, sort, pageSize, pageNumber), cancellationToken).ConfigureAwait(false)
					: await Site.SearchAsync(query, filter, pageSize, pageNumber, cancellationToken).ConfigureAwait(false)
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
			var gotRights = isSystemAdministrator || requestInfo.Session.User.ID.IsEquals(organization.OwnerID) || requestInfo.Session.User.IsModerator(organization.WorkingPrivileges);
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
			site.Set().ClearRelatedCacheAsync(requestInfo.CorrelationID).Run();

			// update organization
			if (organization._siteIDs == null)
				await organization.FindSitesAsync(cancellationToken).ConfigureAwait(false);
			organization._siteIDs.Add(site.ID);
			organization.Set(false, true);

			// send update messages
			var response = site.ToJson();
			var objectName = site.GetTypeName(true);
			await Task.WhenAll(
				Utility.RTUService.SendUpdateMessageAsync(new UpdateMessage
				{
					Type = $"{requestInfo.ServiceName}#{objectName}#Create",
					Data = response,
					DeviceID = "*"
				}, cancellationToken),
				Utility.RTUService.SendInterCommunicateMessageAsync(new CommunicateMessage(requestInfo.ServiceName)
				{
					Type = $"{objectName}#Create",
					Data = response,
					ExcludedNodeID = Utility.NodeID
				}, cancellationToken),
				Utility.RTUService.SendInterCommunicateMessageAsync(new CommunicateMessage(requestInfo.ServiceName)
				{
					Type = $"{organization.GetTypeName(true)}#Update",
					Data = organization.ToJson(),
					ExcludedNodeID = Utility.NodeID
				}, cancellationToken)
			).ConfigureAwait(false);

			// send notification
			site.SendNotificationAsync("Create", site.Organization.Notifications, ApprovalStatus.Draft, site.Status, requestInfo, cancellationToken).Run();

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
			var gotRights = isSystemAdministrator || requestInfo.Session.User.ID.IsEquals(site.Organization.OwnerID) || requestInfo.Session.User.IsViewer(site.Organization.WorkingPrivileges);
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
			await Utility.RTUService.SendUpdateMessageAsync(new UpdateMessage
			{
				Type = $"{requestInfo.ServiceName}#{site.GetTypeName(true)}#Update",
				Data = response,
				DeviceID = "*",
				ExcludedDeviceID = requestInfo.Session.DeviceID
			}, cancellationToken).ConfigureAwait(false);

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
			var gotRights = isSystemAdministrator || requestInfo.Session.User.ID.IsEquals(site.Organization.OwnerID) || requestInfo.Session.User.IsModerator(site.Organization.WorkingPrivileges);
			if (!gotRights)
				throw new AccessDeniedException();

			// check domain
			var request = requestInfo.GetBodyExpando();
			var domain = $"{request.Get<string>("SubDomain")}.{request.Get<string>("PrimaryDomain")}";
			var existing = await domain.GetSiteByDomainAsync(cancellationToken).ConfigureAwait(false);
			if (existing != null && !existing.ID.Equals(site.ID))
				throw new InformationExistedException($"The domain ({domain.NormalizeDomain()}) was used by another site");

			// validate meta-tags
			request.Get("MetaTags", "").ValidateTags();

			// update
			var oldStatus = site.Status;
			site.UpdateSiteInstance(request, "ID,SystemID,Privileges,OriginalPrivileges,Created,CreatedID,LastModified,LastModifiedID", obj =>
			{
				obj.LastModified = DateTime.Now;
				obj.LastModifiedID = requestInfo.Session.User.ID;
				obj.NormalizeExtras();
			});
			await Site.UpdateAsync(site, requestInfo.Session.User.ID, cancellationToken).ConfigureAwait(false);

			// update cache
			site.Set().ClearRelatedCacheAsync(requestInfo.CorrelationID).Run();

			// send update messages
			var response = site.ToJson();
			var objectName = site.GetTypeName(true);
			await Task.WhenAll(
				Utility.RTUService.SendUpdateMessageAsync(new UpdateMessage
				{
					Type = $"{requestInfo.ServiceName}#{objectName}#Update",
					Data = response,
					DeviceID = "*"
				}, cancellationToken),
				Utility.RTUService.SendInterCommunicateMessageAsync(new CommunicateMessage(requestInfo.ServiceName)
				{
					Type = $"{objectName}#Update",
					Data = response,
					ExcludedNodeID = Utility.NodeID
				}, cancellationToken)
			).ConfigureAwait(false);

			// send notification
			site.SendNotificationAsync("Update", site.Organization.Notifications, oldStatus, site.Status, requestInfo, cancellationToken).Run();

			// response
			return response;
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
			var gotRights = isSystemAdministrator || requestInfo.Session.User.ID.IsEquals(site.Organization.OwnerID) || requestInfo.Session.User.IsModerator(site.Organization.WorkingPrivileges);
			if (!gotRights)
				throw new AccessDeniedException();

			// delete
			await Site.DeleteAsync<Site>(site.ID, requestInfo.Session.User.ID, cancellationToken).ConfigureAwait(false);

			// update cache
			site.Remove().ClearRelatedCacheAsync(requestInfo.CorrelationID).Run();

			// update organization
			var organization = site.Organization;
			if (organization != null && organization._siteIDs != null)
			{
				organization._siteIDs.Remove(site.ID);
				organization.Set(false, true);
			}

			// send update messages
			var response = site.ToJson();
			var objectName = site.GetTypeName(true);
			await Task.WhenAll(
				Utility.RTUService.SendUpdateMessageAsync(new UpdateMessage
				{
					Type = $"{requestInfo.ServiceName}#{objectName}#Delete",
					Data = response,
					DeviceID = "*"
				}, cancellationToken),
				Utility.RTUService.SendInterCommunicateMessageAsync(new CommunicateMessage(requestInfo.ServiceName)
				{
					Type = $"{objectName}#Delete",
					Data = response,
					ExcludedNodeID = Utility.NodeID
				}, cancellationToken),
				Utility.RTUService.SendInterCommunicateMessageAsync(new CommunicateMessage(requestInfo.ServiceName)
				{
					Type = $"{site.Organization.GetTypeName(true)}#Update",
					Data = site.Organization.ToJson(),
					ExcludedNodeID = Utility.NodeID
				}, cancellationToken)
			).ConfigureAwait(false);

			// send notification
			site.SendNotificationAsync("Delete", site.Organization.Notifications, site.Status, site.Status, requestInfo, cancellationToken).Run();

			// response
			return response;
		}

		internal static async Task<JObject> SyncSiteAsync(this RequestInfo requestInfo, CancellationToken cancellationToken = default)
		{
			var data = requestInfo.GetBodyExpando();
			var site = await data.Get<string>("ID").GetSiteByIDAsync(cancellationToken).ConfigureAwait(false);
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

			// clear related cache
			site.ClearRelatedCacheAsync(requestInfo.CorrelationID).Run();

			// send update messages
			var json = site.Set().ToJson();
			var objectName = site.GetTypeName(true);
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
				{ "ID", site.ID },
				{ "Type", objectName }
			};
		}
	}
}
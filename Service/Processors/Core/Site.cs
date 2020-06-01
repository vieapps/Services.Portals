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

		internal static HashSet<string> ExtraProperties { get; } = "AlwaysUseHTTPs,UISettings,IconURI,CoverURI,MetaTags,Scripts,RedirectToNoneWWW,SEOInfo".ToHashSet();

		public static Site CreateSiteInstance(this ExpandoObject requestBody, string excluded = null, Action<Site> onCompleted = null)
			=> requestBody.Copy<Site>(excluded?.ToHashSet(), site =>
			{
				site.PrimaryDomain = site.PrimaryDomain.Trim().ToArray(".").Select(name => name.NormalizeAlias(false)).Join(".");
				site.SubDomain = site.SubDomain.Trim().Equals("*") ? site.SubDomain.Trim() : site.SubDomain.NormalizeAlias(false);
				site.OtherDomains = string.IsNullOrWhiteSpace(site.OtherDomains) ? null : site.OtherDomains.Replace(",", ";").ToList(";", true, true).Select(domain => domain.ToArray(".").Select(name => name.NormalizeAlias(false)).Join(".")).Where(domain => !domain.IsEquals(site.PrimaryDomain)).Join(";");
				site.TrimAll();
				onCompleted?.Invoke(site);
			});

		public static Site UpdateSiteInstance(this Site site, ExpandoObject requestBody, string excluded = null, Action<Site> onCompleted = null)
		{
			site.CopyFrom(requestBody, excluded?.ToHashSet());
			site.PrimaryDomain = site.PrimaryDomain.Trim().ToArray(".").Select(name => name.NormalizeAlias(false)).Join(".");
			site.SubDomain = site.SubDomain.Trim().Equals("*") ? site.SubDomain.Trim() : site.SubDomain.NormalizeAlias(false);
			site.OtherDomains = string.IsNullOrWhiteSpace(site.OtherDomains) ? null : site.OtherDomains.Replace(",", ";").ToList(";", true, true).Select(domain => domain.ToArray(".").Select(name => name.NormalizeAlias(false)).Join(".")).Where(domain => !domain.IsEquals(site.PrimaryDomain)).Join(";");
			site.TrimAll();
			onCompleted?.Invoke(site);
			return site;
		}

		internal static Site Set(this Site site, bool clear = false, bool updateCache = false)
		{
			if (site != null)
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
					Utility.Cache.Set(site);
			}
			return site;
		}

		internal static async Task<Site> SetAsync(this Site site, bool clear = false, bool updateCache = false, CancellationToken cancellationToken = default)
		{
			site?.Set(clear);
			await (updateCache && site != null ? Utility.Cache.SetAsync(site, cancellationToken) : Task.CompletedTask).ConfigureAwait(false);
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

		internal static async Task ProcessInterCommunicateMessageOfSiteAsync(this CommunicateMessage message, CancellationToken cancellationToken = default)
		{
			if (message.Type.IsEndsWith("#Create"))
				await message.Data.ToExpandoObject().CreateSiteInstance().SetAsync(true, false, cancellationToken).ConfigureAwait(false);

			else if (message.Type.IsEndsWith("#Update"))
			{
				var site = message.Data.Get("ID", "").GetSiteByID(false, false);
				await (site == null ? message.Data.ToExpandoObject().CreateSiteInstance() : site.UpdateSiteInstance(message.Data.ToExpandoObject())).SetAsync(true, false, cancellationToken).ConfigureAwait(false);
			}

			else if (message.Type.IsEndsWith("#Delete"))
				message.Data.ToExpandoObject().CreateSiteInstance().Remove();
		}

		static Task ClearRelatedCache(this Site site, CancellationToken cancellationToken = default)
			=> Task.WhenAll
			(
				Utility.Cache.RemoveAsync(Extensions.GetRelatedCacheKeys(Filters<Site>.And(), Sorts<Site>.Ascending("Title")), cancellationToken),
				Utility.Cache.RemoveAsync(Extensions.GetRelatedCacheKeys(Filters<Site>.And(Filters<Site>.Equals("SystemID", site.SystemID)), Sorts<Site>.Ascending("Title")), cancellationToken)
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

		internal static async Task<JObject> CreateSiteAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, string nodeID = null, IRTUService rtuService = null, CancellationToken cancellationToken = default)
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

			// validate meta-tags and scripts
			request.Get("MetaTags", "").ValidateMetaTagsOrScripts();
			request.Get("Scripts", "").ValidateMetaTagsOrScripts(true);

			// create new
			var site = request.CreateSiteInstance("SystemID,Privileges,OriginalPrivileges,Created,CreatedID,LastModified,LastModifiedID", obj =>
			{
				obj.ID = string.IsNullOrWhiteSpace(obj.ID) || !obj.ID.IsValidUUID() ? UtilityService.NewUUID : obj.ID;
				obj.SystemID = organization.ID;
				obj.Created = obj.LastModified = DateTime.Now;
				obj.CreatedID = obj.LastModifiedID = requestInfo.Session.User.ID;
				obj.NormalizeExtras();
			});
			await Task.WhenAll(
				Site.CreateAsync(site, cancellationToken),
				site.ClearRelatedCache(cancellationToken),
				site.SetAsync(false, false, cancellationToken)
			).ConfigureAwait(false);

			// send update messages
			var response = site.ToJson();
			var objectName = site.GetTypeName(true);
			await Task.WhenAll(
				rtuService == null ? Task.CompletedTask : rtuService.SendUpdateMessageAsync(new UpdateMessage
				{
					Type = $"{requestInfo.ServiceName}#{objectName}#Create",
					Data = response,
					DeviceID = "*",
					ExcludedDeviceID = requestInfo.Session.DeviceID
				}, cancellationToken),
				rtuService == null ? Task.CompletedTask : rtuService.SendInterCommunicateMessageAsync(new CommunicateMessage(requestInfo.ServiceName)
				{
					Type = $"{objectName}#Create",
					Data = response,
					ExcludedNodeID = nodeID
				}, cancellationToken)
			).ConfigureAwait(false);

			// response
			return response;
		}

		internal static async Task<JObject> GetSiteAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, IRTUService rtuService = null, CancellationToken cancellationToken = default)
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
					{ "Domain", (site.SubDomain.Equals("*") ? "" : site.SubDomain + ".") + site.PrimaryDomain }
				};

			// send update message and response
			var response = site.ToJson();
			await (rtuService == null ? Task.CompletedTask : rtuService.SendUpdateMessageAsync(new UpdateMessage
			{
				Type = $"{requestInfo.ServiceName}#{site.GetTypeName(true)}#Update",
				Data = response,
				DeviceID = "*",
				ExcludedDeviceID = requestInfo.Session.DeviceID
			}, cancellationToken)).ConfigureAwait(false);
			return response;
		}

		internal static async Task<JObject> UpdateSiteAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, string nodeID = null, IRTUService rtuService = null, CancellationToken cancellationToken = default)
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

			// validate meta-tags and scripts
			request.Get("MetaTags", "").ValidateMetaTagsOrScripts();
			request.Get("Scripts", "").ValidateMetaTagsOrScripts(true);

			// update
			site.UpdateSiteInstance(request, "ID,SystemID,Privileges,OriginalPrivileges,Created,CreatedID,LastModified,LastModifiedID", obj =>
			{
				obj.LastModified = DateTime.Now;
				obj.LastModifiedID = requestInfo.Session.User.ID;
				obj.NormalizeExtras();
			});
			await Task.WhenAll(
				Site.UpdateAsync(site, requestInfo.Session.User.ID, cancellationToken),
				site.ClearRelatedCache(cancellationToken),
				site.SetAsync(false, false, cancellationToken)
			).ConfigureAwait(false);

			// send update messages
			var response = site.ToJson();
			var objectName = site.GetTypeName(true);
			await Task.WhenAll(
				rtuService == null ? Task.CompletedTask : rtuService.SendUpdateMessageAsync(new UpdateMessage
				{
					Type = $"{requestInfo.ServiceName}#{objectName}#Update",
					Data = response,
					DeviceID = "*",
					ExcludedDeviceID = requestInfo.Session.DeviceID
				}, cancellationToken),
				rtuService == null ? Task.CompletedTask : rtuService.SendInterCommunicateMessageAsync(new CommunicateMessage(requestInfo.ServiceName)
				{
					Type = $"{objectName}#Update",
					Data = response,
					ExcludedNodeID = nodeID
				}, cancellationToken)
			).ConfigureAwait(false);

			// response
			return response;
		}

		internal static async Task<JObject> DeleteSiteAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, string nodeID = null, IRTUService rtuService = null, CancellationToken cancellationToken = default)
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
			site.Remove();
			await site.ClearRelatedCache(cancellationToken).ConfigureAwait(false);

			// send update messages
			var response = site.ToJson();
			var objectName = site.GetTypeName(true);
			await Task.WhenAll(
				rtuService == null ? Task.CompletedTask : rtuService.SendUpdateMessageAsync(new UpdateMessage
				{
					Type = $"{requestInfo.ServiceName}#{objectName}#Delete",
					Data = response,
					DeviceID = "*",
					ExcludedDeviceID = requestInfo.Session.DeviceID
				}, cancellationToken),
				rtuService == null ? Task.CompletedTask : rtuService.SendInterCommunicateMessageAsync(new CommunicateMessage(requestInfo.ServiceName)
				{
					Type = $"{objectName}#Delete",
					Data = response,
					ExcludedNodeID = nodeID
				}, cancellationToken)
			).ConfigureAwait(false);

			// response
			return response;
		}
	}
}
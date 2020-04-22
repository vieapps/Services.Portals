#region Related components
using System;
using System.Linq;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Serialization;

using Newtonsoft.Json;
using MongoDB.Bson.Serialization.Attributes;

using net.vieapps.Components.Utility;
using net.vieapps.Components.Caching;
using net.vieapps.Components.Repository;
#endregion

namespace net.vieapps.Services.Portals
{
	public static partial class Utility
	{

		#region Caching & URIs
		/// <summary>
		/// Gets the cache storage
		/// </summary>
		public static Cache Cache { get; } = new Cache("VIEApps-Services-Portals", UtilityService.GetAppSetting("Cache:ExpirationTime", "30").CastAs<int>(), false, UtilityService.GetAppSetting("Cache:Provider"), Logger.GetLoggerFactory());

		static string _APIsHttpUri, _FilesHttpUri, _PortalsHttpUri, _PassportsHttpUri;

		/// <summary>
		/// Gets the URI of the public APIS
		/// </summary>
		public static string APIsHttpUri
		{
			get
			{
				if (string.IsNullOrWhiteSpace(Utility._APIsHttpUri))
				{
					Utility._APIsHttpUri = UtilityService.GetAppSetting("HttpUri:APIs", "https://apis.vieapps.net");
					while (Utility._APIsHttpUri.EndsWith("/"))
						Utility._APIsHttpUri = Utility._APIsHttpUri.Left(Utility._APIsHttpUri.Length - 1);
				}
				return Utility._APIsHttpUri;
			}
		}

		/// <summary>
		/// Gets the URI of the Files HTTP service
		/// </summary>
		public static string FilesHttpUri
		{
			get
			{
				if (string.IsNullOrWhiteSpace(Utility._FilesHttpUri))
				{
					Utility._FilesHttpUri = UtilityService.GetAppSetting("HttpUri:Files", "https://fs.vieapps.net");
					while (Utility._FilesHttpUri.EndsWith("/"))
						Utility._FilesHttpUri = Utility._FilesHttpUri.Left(Utility._FilesHttpUri.Length - 1);
				}
				return Utility._FilesHttpUri;
			}
		}

		/// <summary>
		/// Gets the URI of the Portals HTTP service
		/// </summary>
		public static string PortalsHttpUri
		{
			get
			{
				if (string.IsNullOrWhiteSpace(Utility._PortalsHttpUri))
				{
					Utility._PortalsHttpUri = UtilityService.GetAppSetting("HttpUri:Portals", "https://portals.vieapps.net");
					while (Utility._PortalsHttpUri.EndsWith("/"))
						Utility._PortalsHttpUri = Utility._PortalsHttpUri.Left(Utility._PortalsHttpUri.Length - 1);
				}
				return Utility._PortalsHttpUri;
			}
		}

		/// <summary>
		/// Gets the URI of the Passports HTTP service
		/// </summary>
		public static string PassportsHttpUri
		{
			get
			{
				if (string.IsNullOrWhiteSpace(Utility._PassportsHttpUri))
				{
					Utility._PassportsHttpUri = UtilityService.GetAppSetting("HttpUri:Passports", "https://id.vieapps.net");
					while (Utility._PassportsHttpUri.EndsWith("/"))
						Utility._PassportsHttpUri = Utility._PassportsHttpUri.Left(Utility._PassportsHttpUri.Length - 1);
				}
				return Utility._PassportsHttpUri;
			}
		}

		public static string NormalizeAlias(this string alias, bool allowMinusSymbols = true)
		{
			alias = alias.GetANSIUri();
			while (alias.StartsWith("-") || alias.StartsWith("_"))
				alias = alias.Right(alias.Length - 1);
			while (alias.EndsWith("-") || alias.EndsWith("_"))
				alias = alias.Left(alias.Length - 1);
			return allowMinusSymbols ? alias : alias.Replace("-", "");
		}
		#endregion

		#region Organizations
		internal static ConcurrentDictionary<string, Organization> Organizations { get; } = new ConcurrentDictionary<string, Organization>(StringComparer.OrdinalIgnoreCase);

		internal static ConcurrentDictionary<string, Organization> OrganizationsByAlias { get; } = new ConcurrentDictionary<string, Organization>(StringComparer.OrdinalIgnoreCase);

		internal static Organization UpdateOrganization(Organization organization, bool clear = false, bool updateCache = false)
		{
			if (organization != null)
			{
				if (clear && Utility.Organizations.TryGetValue(organization.ID, out var old) && old != null)
					Utility.OrganizationsByAlias.Remove(old.Alias);
				Utility.Organizations[organization.ID] = organization;
				Utility.OrganizationsByAlias[organization.Alias] = organization;
				if (updateCache)
					Utility.Cache.Set(organization);
			}
			return organization;
		}

		internal static async Task<Organization> UpdateOrganizationAsync(Organization organization, bool clear = false, bool updateCache = false, CancellationToken cancellationToken = default)
		{
			if (organization != null)
			{
				Utility.UpdateOrganization(organization, clear);
				if (updateCache)
					await Utility.Cache.SetAsync(organization, cancellationToken).ConfigureAwait(false);
			}
			return organization;
		}

		internal static Organization GetOrganizationByID(string id, bool force = false, bool fetchRepository = true)
			=> !force && !string.IsNullOrWhiteSpace(id) && Utility.Organizations.ContainsKey(id)
				? Utility.Organizations[id]
				: fetchRepository
					? Utility.UpdateOrganization(Organization.Get<Organization>(id))
					: null;

		internal static async Task<Organization> GetOrganizationByIDAsync(string id, CancellationToken cancellationToken = default, bool force = false)
			=> Utility.GetOrganizationByID(id, force, false) ?? await Utility.UpdateOrganizationAsync(await Organization.GetAsync<Organization>(id, cancellationToken).ConfigureAwait(false), false, true, cancellationToken).ConfigureAwait(false);

		internal static Organization GetOrganizationByAlias(string alias, bool force = false, bool fetchRepository = true)
		{
			var organization = !force && !string.IsNullOrWhiteSpace(alias) && Utility.OrganizationsByAlias.ContainsKey(alias)
				? Utility.OrganizationsByAlias[alias]
				: null;

			if (organization == null && !force && !string.IsNullOrWhiteSpace(alias))
			{
				organization = Utility.Organizations.Values.FirstOrDefault(org => org.Alias.IsEquals(alias));
				if (organization != null)
					Utility.UpdateOrganization(organization);
			}

			return organization ?? (fetchRepository ? Utility.UpdateOrganization(Organization.Get<Organization>(Filters<Organization>.Equals("Alias", alias), null, null)) : null);
		}

		internal static async Task<Organization> GetOrganizationByAliasAsync(string alias, CancellationToken cancellationToken = default, bool force = false)
			=> Utility.GetOrganizationByAlias(alias, force, false) ?? await Utility.UpdateOrganizationAsync(await Organization.GetAsync<Organization>(Filters<Organization>.Equals("Alias", alias), null, null, cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
		#endregion

		#region Roles 
		internal static ConcurrentDictionary<string, Role> Roles { get; } = new ConcurrentDictionary<string, Role>(StringComparer.OrdinalIgnoreCase);

		internal static Role UpdateRole(Role role, bool updateCache = false)
		{
			if (role != null)
			{
				Utility.Roles[role.ID] = role;
				if (updateCache)
					Utility.Cache.Set(role);
			}
			return role;
		}

		internal static async Task<Role> UpdateRoleAsync(Role role, bool updateCache = false, CancellationToken cancellationToken = default)
		{
			if (role != null)
			{
				Utility.UpdateRole(role);
				if (updateCache)
					await Utility.Cache.SetAsync(role, cancellationToken).ConfigureAwait(false);
			}
			return role;
		}

		internal static Role GetRoleByID(string id, bool force = false, bool fetchRepository = true)
			=> !force && !string.IsNullOrWhiteSpace(id) && Utility.Roles.ContainsKey(id)
				? Utility.Roles[id]
				: fetchRepository
					? Utility.UpdateRole(Role.Get<Role>(id))
					: null;

		internal static async Task<Role> GetRoleByIDAsync(string id, CancellationToken cancellationToken = default, bool force = false)
			=> Utility.GetRoleByID(id, force, false) ?? await Utility.UpdateRoleAsync(await Role.GetAsync<Role>(id, cancellationToken).ConfigureAwait(false), true, cancellationToken).ConfigureAwait(false);

		internal static IFilterBy<Role> GetRolesFilter(string systemID, string parentID)
			=> Filters<Role>.And(Filters<Role>.Equals("SystemID", systemID), string.IsNullOrWhiteSpace(parentID) ? Filters<Role>.IsNull("ParentID") : Filters<Role>.Equals("ParentID", parentID));

		internal static List<Role> GetRolesByParentID(string systemID, string parentID, bool updateCache = true)
		{
			if (string.IsNullOrWhiteSpace(systemID))
				return new List<Role>();
			var filter = Utility.GetRolesFilter(systemID, parentID);
			var sort = Sorts<Role>.Ascending("Title");
			var roles = Role.Find(filter, sort, 0, 1, Extensions.GetCacheKey(filter, sort, 0, 1));
			roles.ForEach(role => Utility.UpdateRole(role, updateCache));
			return roles;
		}

		internal static async Task<List<Role>> GetRolesByParentIDAsync(string systemID, string parentID, CancellationToken cancellationToken = default, bool updateCache = true)
		{
			if (string.IsNullOrWhiteSpace(systemID))
				return new List<Role>();
			var filter = Utility.GetRolesFilter(systemID, parentID);
			var sort = Sorts<Role>.Ascending("Title");
			var roles = await Role.FindAsync(filter, sort, 0, 1, Extensions.GetCacheKey(filter, sort, 0, 1), cancellationToken).ConfigureAwait(false);
			await roles.ForEachAsync((role, token) => Utility.UpdateRoleAsync(role, updateCache, token), cancellationToken).ConfigureAwait(false);
			return roles;
		}
		#endregion

		#region Desktops
		internal static ConcurrentDictionary<string, Desktop> Desktops { get; } = new ConcurrentDictionary<string, Desktop>(StringComparer.OrdinalIgnoreCase);

		internal static ConcurrentDictionary<string, Desktop> DesktopsByAlias { get; } = new ConcurrentDictionary<string, Desktop>(StringComparer.OrdinalIgnoreCase);

		internal static Desktop UpdateDesktop(Desktop desktop, bool clear = false, bool updateCache = false)
		{
			if (desktop != null)
			{
				if (clear && Utility.Desktops.TryGetValue(desktop.ID, out var old) && old != null)
				{
					Utility.DesktopsByAlias.Remove($"{old.SystemID}:{old.Alias}");
					if (!string.IsNullOrWhiteSpace(old.Aliases))
						old.Aliases.Replace(",", ";").ToList(";").ForEach(alias => Utility.DesktopsByAlias.Remove($"{old.SystemID}:{alias}"));
					if (old.ParentID != desktop.ParentID)
					{
						if (old.ParentDesktop != null)
							old.ParentDesktop._childrenIDs = null;
						if (desktop.ParentDesktop != null)
							desktop.ParentDesktop._childrenIDs = null;
					}
				}
				Utility.Desktops[desktop.ID] = desktop;
				Utility.DesktopsByAlias[$"{desktop.SystemID}:{desktop.Alias}"] = desktop;
				if (!string.IsNullOrWhiteSpace(desktop.Aliases))
					desktop.Aliases.Replace(",", ";").ToList(";").ForEach(alias => Utility.DesktopsByAlias.TryAdd($"{desktop.SystemID}:{alias}", desktop));
				if (updateCache)
					Utility.Cache.Set(desktop);
			}
			return desktop;
		}

		internal static async Task<Desktop> UpdateDesktopAsync(Desktop desktop, bool clear = false, bool updateCache = false, CancellationToken cancellationToken = default)
		{
			if (desktop != null)
			{
				Utility.UpdateDesktop(desktop, clear);
				if (updateCache)
					await Utility.Cache.SetAsync(desktop, cancellationToken).ConfigureAwait(false);
			}
			return desktop;
		}

		internal static Desktop GetDesktopByID(string id, bool force = false, bool fetchRepository = true)
			=> !force && !string.IsNullOrWhiteSpace(id) && Utility.Desktops.ContainsKey(id)
				? Utility.Desktops[id]
				: fetchRepository
					? Utility.UpdateDesktop(Desktop.Get<Desktop>(id))
					: null;

		internal static async Task<Desktop> GetDesktopByIDAsync(string id, CancellationToken cancellationToken = default, bool force = false)
			=> Utility.GetDesktopByID(id, force, false) ?? await Utility.UpdateDesktopAsync(await Desktop.GetAsync<Desktop>(id, cancellationToken).ConfigureAwait(false), false, true, cancellationToken).ConfigureAwait(false);

		internal static Desktop GetDesktopByAlias(string systemID, string alias, bool force = false, bool fetchRepository = true)
		{
			if (string.IsNullOrWhiteSpace(systemID) || string.IsNullOrWhiteSpace(alias))
				return null;

			var desktop = !force && Utility.DesktopsByAlias.ContainsKey($"{systemID}:{alias}")
				? Utility.DesktopsByAlias[$"{systemID}:{alias}"]
				: null;

			if (desktop == null && !force)
			{
				desktop = Utility.Desktops.FirstOrDefault(kvp => kvp.Value.SystemID.IsEquals(systemID) && kvp.Value.Alias.IsEquals(alias)).Value;
				if (desktop != null)
					Utility.UpdateDesktop(desktop);
			}

			return desktop ?? (fetchRepository ? Utility.UpdateDesktop(Desktop.Get<Desktop>(Filters<Desktop>.And(Filters<Desktop>.Equals("SystemID", systemID), Filters<Desktop>.Equals("Alias", alias)), null, null)) : null);
		}

		internal static async Task<Desktop> GetDesktopByAliasAsync(string systemID, string alias, CancellationToken cancellationToken = default, bool force = false)
			=> Utility.GetDesktopByAlias(systemID, alias, force, false) ?? await Utility.UpdateDesktopAsync(await Desktop.GetAsync<Desktop>(Filters<Desktop>.And(Filters<Desktop>.Equals("SystemID", systemID), Filters<Desktop>.Equals("Alias", alias)), null, null, cancellationToken).ConfigureAwait(false), false, true, cancellationToken).ConfigureAwait(false);

		internal static IFilterBy<Desktop> GetDesktopsFilter(string systemID, string parentID)
			=> Filters<Desktop>.And(Filters<Desktop>.Equals("SystemID", systemID), string.IsNullOrWhiteSpace(parentID) ? Filters<Desktop>.IsNull("ParentID") : Filters<Desktop>.Equals("ParentID", parentID));

		internal static string GetDesktopsCacheKey(string systemID, string parentID, int pageSize = 0, int pageNumber = 1)
			=> $"{Extensions.GetCacheKey(Extensions.GetCacheKey<Desktop>(), pageSize, pageNumber)}:sp:{systemID.ToLower().Trim()}{(string.IsNullOrWhiteSpace(parentID) ? "" : $":{parentID.ToLower().Trim()}")}";

		internal static List<Desktop> GetDesktopsByParentID(string systemID, string parentID, bool updateCache = true)
		{
			if (string.IsNullOrWhiteSpace(systemID))
				return new List<Desktop>();
			var filter = Utility.GetDesktopsFilter(systemID, parentID);
			var sort = Sorts<Desktop>.Ascending("Title");
			var desktops = Desktop.Find(filter, sort, 0, 1, Extensions.GetCacheKey(filter, sort, 0, 1));
			desktops.ForEach(desktop => Utility.UpdateDesktop(desktop, false, updateCache));
			return desktops;
		}

		internal static async Task<List<Desktop>> GetDesktopsByParentIDAsync(string systemID, string parentID, CancellationToken cancellationToken = default, bool updateCache = true)
		{
			if (string.IsNullOrWhiteSpace(systemID))
				return new List<Desktop>();
			var filter = Utility.GetDesktopsFilter(systemID, parentID);
			var sort = Sorts<Desktop>.Ascending("Title");
			var desktops = await Desktop.FindAsync(filter, sort, 0, 1, Extensions.GetCacheKey(filter, sort, 0, 1), cancellationToken).ConfigureAwait(false);
			await desktops.ForEachAsync((desktop, token) => Utility.UpdateDesktopAsync(desktop, false, updateCache, token), cancellationToken).ConfigureAwait(false);
			return desktops;
		}
		#endregion

		#region Modules 
		internal static ConcurrentDictionary<string, Module> Modules { get; } = new ConcurrentDictionary<string, Module>(StringComparer.OrdinalIgnoreCase);

		internal static Module UpdateModule(Module module)
		{
			if (module != null)
				Utility.Modules[module.ID] = module;
			return module;
		}

		internal static Module GetModuleByID(string id, bool force = false)
			=> !force && !string.IsNullOrWhiteSpace(id) && Utility.Modules.ContainsKey(id)
				? Utility.Modules[id]
				: Utility.UpdateModule(Module.Get<Module>(id));

		internal static async Task<Module> GetModuleByIDAsync(string id, CancellationToken cancellationToken = default, bool force = false)
			=> !force && !string.IsNullOrWhiteSpace(id) && Utility.Modules.ContainsKey(id)
				? Utility.Modules[id]
				: Utility.UpdateModule(await Module.GetAsync<Module>(id, cancellationToken).ConfigureAwait(false));
		#endregion

		#region Content-Types
		internal static ConcurrentDictionary<string, ContentType> ContentTypes { get; } = new ConcurrentDictionary<string, ContentType>(StringComparer.OrdinalIgnoreCase);

		internal static ContentType UpdateContentType(ContentType contentType)
		{
			if (contentType != null)
				Utility.ContentTypes[contentType.ID] = contentType;
			return contentType;
		}

		internal static ContentType GetContentTypeByID(string id, bool force = false)
			=> !force && !string.IsNullOrWhiteSpace(id) && Utility.ContentTypes.ContainsKey(id)
				? Utility.ContentTypes[id]
				: Utility.UpdateContentType(ContentType.Get<ContentType>(id));

		internal static async Task<ContentType> GetContentTypeByIDAsync(string id, CancellationToken cancellationToken = default, bool force = false)
			=> !force && !string.IsNullOrWhiteSpace(id) && Utility.ContentTypes.ContainsKey(id)
				? Utility.ContentTypes[id]
				: Utility.UpdateContentType(await ContentType.GetAsync<ContentType>(id, cancellationToken).ConfigureAwait(false));
		#endregion

		#region Sites
		internal static ConcurrentDictionary<string, Site> Sites { get; } = new ConcurrentDictionary<string, Site>(StringComparer.OrdinalIgnoreCase);

		internal static ConcurrentDictionary<string, Site> SitesByDomain { get; } = new ConcurrentDictionary<string, Site>(StringComparer.OrdinalIgnoreCase);

		internal static Site UpdateSite(Site site, bool clear = false)
		{
			if (site != null)
			{
				if (clear && Utility.Sites.TryGetValue(site.ID, out var old) && old != null)
				{
					Utility.SitesByDomain.Remove($"{old.SubDomain}.{old.PrimaryDomain}");
					if (!string.IsNullOrWhiteSpace(old.OtherDomains))
						old.OtherDomains.Replace(";", ",").ToList().Where(domain => !string.IsNullOrWhiteSpace(domain)).ForEach(domain =>
						{
							if (Utility.SitesByDomain.Remove(domain))
								Utility.SitesByDomain.Remove($"*.{domain}");
						});
				}
				Utility.Sites[site.ID] = site;
				Utility.SitesByDomain[$"{site.SubDomain}.{site.PrimaryDomain}"] = site;
				if (!string.IsNullOrWhiteSpace(site.OtherDomains))
					site.OtherDomains.Replace(";", ",").ToList().Where(domain => !string.IsNullOrWhiteSpace(domain)).ForEach(domain =>
					{
						if (Utility.SitesByDomain.TryAdd(domain, site))
							Utility.SitesByDomain.TryAdd($"*.{domain}", site);
					});
			}
			return site;
		}

		internal static ConcurrentDictionary<string, Site> GetSites()
		{
			Utility.Sites.Clear();
			Utility.SitesByDomain.Clear();
			Site.Find(null, Sorts<Site>.Ascending("PrimaryDomain").ThenByAscending("SubDomain"), 0, 1, null).ForEach(site => Utility.UpdateSite(site));
			return Utility.Sites;
		}

		internal static async Task<ConcurrentDictionary<string, Site>> GetSitesAsync(CancellationToken cancellationToken = default)
		{
			Utility.Sites.Clear();
			Utility.SitesByDomain.Clear();
			(await Site.FindAsync(null, Sorts<Site>.Ascending("PrimaryDomain").ThenByAscending("SubDomain"), 0, 1, null, cancellationToken).ConfigureAwait(false)).ForEach(site => Utility.UpdateSite(site));
			return Utility.Sites;
		}

		internal static Site GetSiteByID(string id, bool force = false)
			=> !force && !string.IsNullOrWhiteSpace(id) && Utility.Sites.ContainsKey(id)
				? Utility.Sites[id]
				: Utility.UpdateSite(Site.Get<Site>(id));

		internal static async Task<Site> GetSiteByIDAsync(string id, CancellationToken cancellationToken = default, bool force = false)
			=> !force && !string.IsNullOrWhiteSpace(id) && Utility.Sites.ContainsKey(id)
				? Utility.Sites[id]
				: Utility.UpdateSite(await Site.GetAsync<Site>(id, cancellationToken).ConfigureAwait(false));

		internal static Site GetSiteByDomain(string domain)
		{
			if (string.IsNullOrWhiteSpace(domain))
				return null;

			var host = domain;
			var site = Utility.SitesByDomain.ContainsKey(host) || Utility.SitesByDomain.ContainsKey($"*.{host}")
				? Utility.SitesByDomain.ContainsKey(host)
					? Utility.SitesByDomain[host]
					: Utility.SitesByDomain[$"*.{host}"]
				: null;

			if (site == null)
			{
				var dotOffset = host.IndexOf(".");
				if (dotOffset < 0)
					Utility.SitesByDomain.TryGetValue($"*.{host}", out site);
				else
					while (site == null && dotOffset > 0)
					{
						site = Utility.SitesByDomain.ContainsKey(host) || Utility.SitesByDomain.ContainsKey($"*.{host}")
							? Utility.SitesByDomain.ContainsKey(host)
								? Utility.SitesByDomain[host]
								: Utility.SitesByDomain[$"*.{host}"]
							: null;
						if (site == null)
						{
							host = host.Right(host.Length - dotOffset - 1);
							dotOffset = host.IndexOf(".");
						}
					}
			}

			return site;
		}
		#endregion

	}

	//  --------------------------------------------------------------------------------------------

	[Serializable, Repository(ID = "00000000000000000000000000000001", Title = "Portals", Description = "Managing core information of portals and related services", Directory = "Portals")]
	public abstract class Repository<T> : RepositoryBase<T> where T : class
	{
		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public override string ServiceName => ServiceBase.ServiceComponent.ServiceName;
	}
}
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
		public static Cache Cache { get; } = new Cache("VIEApps-Services-Portals", UtilityService.GetAppSetting("Cache:ExpirationTime", "30").CastAs<int>(), false, UtilityService.GetAppSetting("Cache:Provider"), Logger.GetLoggerFactory());

		static string _FilesHttpUri = null;

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
		#endregion

		#region Organizations
		internal static ConcurrentDictionary<string, Organization> Organizations { get; } = new ConcurrentDictionary<string, Organization>(StringComparer.OrdinalIgnoreCase);

		internal static ConcurrentDictionary<string, Organization> OrganizationsByAlias { get; } = new ConcurrentDictionary<string, Organization>(StringComparer.OrdinalIgnoreCase);

		internal static Organization UpdateOrganization(Organization organization, bool clear = false)
		{
			if (organization != null)
			{
				if (clear && Utility.Organizations.TryGetValue(organization.ID, out var old) && old != null)
					Utility.OrganizationsByAlias.Remove(old.Alias);
				Utility.Organizations[organization.ID] = organization;
				Utility.OrganizationsByAlias[organization.Alias] = organization;
			}
			return organization;
		}

		internal static Organization GetOrganizationByID(string id, bool force = false)
			=> !force && !string.IsNullOrWhiteSpace(id) && Utility.Organizations.ContainsKey(id)
				? Utility.Organizations[id]
				: Utility.UpdateOrganization(Organization.Get<Organization>(id));

		internal static async Task<Organization> GetOrganizationByIDAsync(string id, CancellationToken cancellationToken = default, bool force = false)
			=> !force && !string.IsNullOrWhiteSpace(id) && Utility.Organizations.ContainsKey(id)
				? Utility.Organizations[id]
				: Utility.UpdateOrganization(await Organization.GetAsync<Organization>(id, cancellationToken).ConfigureAwait(false));

		internal static Organization GetOrganizationByAlias(string alias, bool force = false)
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

			return organization ?? Utility.UpdateOrganization(Organization.Get<Organization>(Filters<Organization>.Equals("Alias", alias), null, null));
		}

		internal static async Task<Organization> GetOrganizationByAliasAsync(string alias, CancellationToken cancellationToken = default, bool force = false)
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

			return organization ?? Utility.UpdateOrganization(await Organization.GetAsync<Organization>(Filters<Organization>.Equals("Alias", alias), null, null, cancellationToken).ConfigureAwait(false));
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

		#region Desktops
		internal static ConcurrentDictionary<string, Desktop> Desktops { get; } = new ConcurrentDictionary<string, Desktop>(StringComparer.OrdinalIgnoreCase);

		internal static ConcurrentDictionary<string, Desktop> DesktopsByAlias { get; } = new ConcurrentDictionary<string, Desktop>(StringComparer.OrdinalIgnoreCase);

		internal static Desktop UpdateDesktop(Desktop desktop, bool clear = false)
		{
			if (desktop != null)
			{
				if (clear && Utility.Desktops.TryGetValue(desktop.ID, out var old) && old != null)
				{
					Utility.DesktopsByAlias.Remove($"{old.SystemID}:{old.Alias}");
					if (!string.IsNullOrWhiteSpace(old.Aliases))
						old.Aliases.Replace(";", ",").ToList().ForEach(alias => Utility.DesktopsByAlias.Remove($"{old.SystemID}:{alias}"));
					if (old.ParentID != desktop.ParentID)
					{
						if (old.ParentDesktop != null)
							old.ParentDesktop.ChildrenIDs = null;
						if (desktop.ParentDesktop != null)
							desktop.ParentDesktop.ChildrenIDs = null;
					}
				}
				Utility.Desktops[desktop.ID] = desktop;
				Utility.DesktopsByAlias[$"{desktop.SystemID}:{desktop.Alias}"] = desktop;
				if (!string.IsNullOrWhiteSpace(desktop.Aliases))
					desktop.Aliases.Replace(";", ",").ToList().ForEach(alias => Utility.DesktopsByAlias.TryAdd($"{desktop.SystemID}:{alias}", desktop));
			}
			return desktop;
		}

		internal static Desktop GetDesktopByID(string id, bool force = false)
			=> !force && !string.IsNullOrWhiteSpace(id) && Utility.Desktops.ContainsKey(id)
				? Utility.Desktops[id]
				: Utility.UpdateDesktop(Desktop.Get<Desktop>(id));

		internal static async Task<Desktop> GetDesktopByIDAsync(string id, CancellationToken cancellationToken = default, bool force = false)
			=> !force && !string.IsNullOrWhiteSpace(id) && Utility.Desktops.ContainsKey(id)
				? Utility.Desktops[id]
				: Utility.UpdateDesktop(await Desktop.GetAsync<Desktop>(id, cancellationToken).ConfigureAwait(false));

		internal static Desktop GetDesktopByAlias(string systemID, string alias, bool force = false)
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

			return desktop ?? Utility.UpdateDesktop(Desktop.Get<Desktop>(Filters<Desktop>.And(Filters<Desktop>.Equals("SystemID", systemID), Filters<Desktop>.Equals("Alias", alias)), null, null));
		}

		internal static async Task<Desktop> GetDesktopByAliasAsync(string systemID, string alias, CancellationToken cancellationToken = default, bool force = false)
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

			return desktop ?? Utility.UpdateDesktop(await Desktop.GetAsync<Desktop>(Filters<Desktop>.And(Filters<Desktop>.Equals("SystemID", systemID), Filters<Desktop>.Equals("Alias", alias)), null, null, cancellationToken).ConfigureAwait(false));
		}

		internal static IFilterBy<Desktop> GetDesktopsFilter(string systemID, string parentID)
			=> Filters<Desktop>.And(Filters<Desktop>.Equals("SystemID", systemID), string.IsNullOrWhiteSpace(parentID) ? Filters<Desktop>.IsNull("ParentID") : Filters<Desktop>.Equals("ParentID", parentID));

		internal static string GetDesktopsCacheKey(string systemID, string parentID)
			=> $"desktops:{systemID.ToLower().Trim()}{(string.IsNullOrWhiteSpace(parentID) ? "" : $":{parentID.ToLower().Trim()}")}";

		internal static List<Desktop> GetDesktopsByParentID(string systemID, string parentID)
			=> string.IsNullOrWhiteSpace(systemID)
				? new List<Desktop>()
				: Desktop.Find(Utility.GetDesktopsFilter(systemID, parentID), Sorts<Desktop>.Ascending("OrderIndex"), 0, 1, Utility.GetDesktopsCacheKey(systemID, parentID));

		internal static Task<List<Desktop>> GetDesktopsByParentIDAsync(string systemID, string parentID, CancellationToken cancellationToken = default)
			=> string.IsNullOrWhiteSpace(systemID)
				? Task.FromResult(new List<Desktop>())
				: Desktop.FindAsync(Utility.GetDesktopsFilter(systemID, parentID), Sorts<Desktop>.Ascending("OrderIndex"), 0, 1, Utility.GetDesktopsCacheKey(systemID, parentID), cancellationToken);
		#endregion

	}

	//  --------------------------------------------------------------------------------------------

	[Serializable, Repository(ID = "00000000000000000000000000000001", Title = "Portals", Description = "Managing core information of portals and related", Directory = "Portals")]
	public abstract class Repository<T> : RepositoryBase<T> where T : class
	{
		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public override string ServiceName => ServiceBase.ServiceComponent.ServiceName;
	}
}
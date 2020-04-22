#region Related components
using System;
using System.Linq;
using System.Diagnostics;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using MongoDB.Bson.Serialization.Attributes;
using net.vieapps.Components.Security;
using net.vieapps.Components.Repository;
using net.vieapps.Components.Utility;
#endregion

namespace net.vieapps.Services.Portals
{
	[Serializable, BsonIgnoreExtraElements, DebuggerDisplay("ID = {ID}, Title = {Title}")]
	[Entity(CollectionName = "Sites", TableName = "T_Portals_Sites", CacheClass = typeof(Utility), CacheName = "Cache", Searchable = true)]
	public sealed class Site : Repository<Site>, IPortalObject
	{
		public Site() : base() { }

		[JsonConverter(typeof(StringEnumConverter)), BsonRepresentation(MongoDB.Bson.BsonType.String), Sortable(IndexName = "Management"), FormControl(Label = "{{portals.sites.controls.[name]}}", PlaceHolder = "{{portals.sites.controls.[name].placeholder}}", Description = "{{portals.sites.controls.[name].description}}")]
		public ApprovalStatus Status { get; set; } = ApprovalStatus.Pending;

		[Property(MaxLength = 250, NotNull = true, NotEmpty = true), Sortable(IndexName = "Title"), Searchable, FormControl(Label = "{{portals.sites.controls.[name]}}", PlaceHolder = "{{portals.sites.controls.[name].placeholder}}", Description = "{{portals.sites.controls.[name].description}}")]
		public override string Title { get; set; } = "";

		[FormControl(Label = "{{portals.sites.controls.[name]}}", PlaceHolder = "{{portals.sites.controls.[name].placeholder}}", Description = "{{portals.sites.controls.[name].description}}")]
		public string Description { get; set; }

		[Property(MaxLength = 100), Sortable(UniqueIndexName ="Domains"), Searchable, FormControl(Label = "{{portals.sites.controls.[name]}}", PlaceHolder = "{{portals.sites.controls.[name].placeholder}}", Description = "{{portals.sites.controls.[name].description}}")]
		public string PrimaryDomain { get; set; } = "company.com";

		[Property(MaxLength = 20), Sortable(UniqueIndexName = "Domains"), FormControl(Label = "{{portals.sites.controls.[name]}}", PlaceHolder = "{{portals.sites.controls.[name].placeholder}}", Description = "{{portals.sites.controls.[name].description}}")]
		public string SubDomain { get; set; } = "*";

		[Property(MaxLength = 1000), FormControl(Label = "{{portals.sites.controls.[name]}}", PlaceHolder = "{{portals.sites.controls.[name].placeholder}}", Description = "{{portals.sites.controls.[name].description}}")]
		public string OtherDomains { get; set; }

		[Property(MaxLength = 5), FormControl(Label = "{{portals.sites.controls.[name]}}", PlaceHolder = "{{portals.sites.controls.[name].placeholder}}", Description = "{{portals.sites.controls.[name].description}}")]
		public string Language { get; set; } = "vi-VN";

		[Property(MaxLength = 100), FormControl(Label = "{{portals.sites.controls.[name]}}", PlaceHolder = "{{portals.sites.controls.[name].placeholder}}", Description = "{{portals.sites.controls.[name].description}}")]
		public string Theme { get; set; }

		[Property(MaxLength = 32), FormControl(Label = "{{portals.sites.controls.[name]}}", PlaceHolder = "{{portals.sites.controls.[name].placeholder}}", Description = "{{portals.sites.controls.[name].description}}")]
		public string HomeDesktopID { get; set; }

		[Property(MaxLength = 32), FormControl(Label = "{{portals.sites.controls.[name]}}", PlaceHolder = "{{portals.sites.controls.[name].placeholder}}", Description = "{{portals.sites.controls.[name].description}}")]
		public string SearchDesktopID { get; set; }

		[Property(IsCLOB = true), FormControl(Excluded = true), XmlIgnore]
		public string OtherSettings { get; set; }

		[Sortable(IndexName = "Audits"), FormControl(Hidden = true)]
		public DateTime Created { get; set; } = DateTime.Now;

		[Sortable(IndexName = "Audits"), FormControl(Hidden = true)]
		public string CreatedID { get; set; } = "";

		[Sortable(IndexName = "Audits"), FormControl(Hidden = true)]
		public DateTime LastModified { get; set; } = DateTime.Now;

		[Sortable(IndexName = "Audits"), FormControl(Hidden = true)]
		public string LastModifiedID { get; set; } = "";

		[Property(MaxLength = 32, NotNull = true, NotEmpty = true), Sortable(IndexName = "Management"), FormControl(Hidden = true)]
		public override string SystemID { get; set; }

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public override string RepositoryID { get; set; }

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public override string EntityID { get; set; }

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public override Privileges OriginalPrivileges { get; set; }

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public override Privileges WorkingPrivileges => this.Organization?.WorkingPrivileges;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public string OrganizationID => this.SystemID;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public new IPortalObject Parent => this.Organization;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public Organization Organization => (this.OrganizationID ?? "").GetOrganizationByID();

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public Desktop HomeDesktop => (this.HomeDesktopID ?? "").GetDesktopByID() ?? this.Organization?.HomeDesktop;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public Desktop SearchDesktop => (this.SearchDesktopID ?? "").GetDesktopByID() ?? this.Organization?.SearchDesktop;
	}

	internal static class SiteExtensions
	{
		internal static ConcurrentDictionary<string, Site> Sites { get; } = new ConcurrentDictionary<string, Site>(StringComparer.OrdinalIgnoreCase);

		internal static ConcurrentDictionary<string, Site> SitesByDomain { get; } = new ConcurrentDictionary<string, Site>(StringComparer.OrdinalIgnoreCase);

		internal static Site Set(this Site site, bool clear = false)
		{
			if (site != null)
			{
				if (clear)
					site.Remove();
				SiteExtensions.Sites[site.ID] = site;
				SiteExtensions.SitesByDomain[$"{site.SubDomain}.{site.PrimaryDomain}"] = site;
				if (!string.IsNullOrWhiteSpace(site.OtherDomains))
					site.OtherDomains.Replace(";", ",").ToList().Where(domain => !string.IsNullOrWhiteSpace(domain)).ForEach(domain =>
					{
						if (SiteExtensions.SitesByDomain.TryAdd(domain, site))
							SiteExtensions.SitesByDomain.TryAdd($"*.{domain}", site);
					});
			}
			return site;
		}

		internal static Site Remove(this Site site)
			=> (site?.ID ?? "").RemoveSite();

		internal static Site RemoveSite(this string id)
		{
			if (!string.IsNullOrWhiteSpace(id) && SiteExtensions.Sites.TryGetValue(id, out var site) && site != null)
			{
				SiteExtensions.Sites.Remove(site.ID);
				SiteExtensions.SitesByDomain.Remove($"{site.SubDomain}.{site.PrimaryDomain}");
				if (!string.IsNullOrWhiteSpace(site.OtherDomains))
					site.OtherDomains.Replace(";", ",").ToList().Where(domain => !string.IsNullOrWhiteSpace(domain)).ForEach(domain =>
					{
						if (SiteExtensions.SitesByDomain.Remove(domain))
							SiteExtensions.SitesByDomain.Remove($"*.{domain}");
					});
				return site;
			}
			return null;
		}

		internal static ConcurrentDictionary<string, Site> GetSites(List<Site> sites = null)
		{
			sites = sites ?? Site.Find(null, Sorts<Site>.Ascending("PrimaryDomain").ThenByAscending("SubDomain"), 0, 1, null);
			SiteExtensions.Sites.Clear();
			SiteExtensions.SitesByDomain.Clear();
			sites.ForEach(site => site.Set());
			return SiteExtensions.Sites;
		}

		internal static async Task<ConcurrentDictionary<string, Site>> GetSitesAsync(CancellationToken cancellationToken = default)
			=> SiteExtensions.GetSites(await Site.FindAsync(null, Sorts<Site>.Ascending("PrimaryDomain").ThenByAscending("SubDomain"), 0, 1, null, cancellationToken).ConfigureAwait(false));

		internal static Site GetSiteByID(this string id, bool force = false, bool fetchRepository = true)
			=> !force && !string.IsNullOrWhiteSpace(id) && SiteExtensions.Sites.ContainsKey(id)
				? SiteExtensions.Sites[id]
				: fetchRepository && !string.IsNullOrWhiteSpace(id)
					? Site.Get<Site>(id)?.Set()
					: null;

		internal static async Task<Site> GetSiteByIDAsync(string id, CancellationToken cancellationToken = default, bool force = false)
		{
			var site = (id ?? "").GetSiteByID(force, false) ?? await Site.GetAsync<Site>(id, cancellationToken).ConfigureAwait(false);
			return site?.Set();
		}

		internal static Site GetSiteByDomain(this string domain)
		{
			if (string.IsNullOrWhiteSpace(domain))
				return null;

			var host = domain;
			var site = SiteExtensions.SitesByDomain.ContainsKey(host) || SiteExtensions.SitesByDomain.ContainsKey($"*.{host}")
				? SiteExtensions.SitesByDomain.ContainsKey(host)
					? SiteExtensions.SitesByDomain[host]
					: SiteExtensions.SitesByDomain[$"*.{host}"]
				: null;

			if (site == null)
			{
				var dotOffset = host.IndexOf(".");
				if (dotOffset < 0)
					SiteExtensions.SitesByDomain.TryGetValue($"*.{host}", out site);
				else
					while (site == null && dotOffset > 0)
					{
						site = SiteExtensions.SitesByDomain.ContainsKey(host) || SiteExtensions.SitesByDomain.ContainsKey($"*.{host}")
							? SiteExtensions.SitesByDomain.ContainsKey(host)
								? SiteExtensions.SitesByDomain[host]
								: SiteExtensions.SitesByDomain[$"*.{host}"]
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
	}
}
#region Related components
using System;
using System.Linq;
using System.Diagnostics;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Serialization;
using System.Dynamic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
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

		[Property(MaxLength = 250, NotNull = true, NotEmpty = true), Sortable(IndexName = "Title"), Searchable]
		[FormControl(Segment = "basic", Label = "{{portals.sites.controls.[name].label}}", PlaceHolder = "{{portals.sites.controls.[name].placeholder}}", Description = "{{portals.sites.controls.[name].description}}")]
		public override string Title { get; set; }

		[Searchable]
		[FormControl(Segment = "basic", ControlType = "TextArea", Label = "{{portals.sites.controls.[name].label}}", PlaceHolder = "{{portals.sites.controls.[name].placeholder}}", Description = "{{portals.sites.controls.[name].description}}")]
		public string Description { get; set; }

		[JsonConverter(typeof(StringEnumConverter)), BsonRepresentation(MongoDB.Bson.BsonType.String)]
		[Sortable(IndexName = "Management")]
		[FormControl(Segment = "basic", Label = "{{portals.sites.controls.[name].label}}", PlaceHolder = "{{portals.sites.controls.[name].placeholder}}", Description = "{{portals.sites.controls.[name].description}}")]
		public ApprovalStatus Status { get; set; } = ApprovalStatus.Pending;

		[Property(MaxLength = 100), Sortable(UniqueIndexName = "Domains"), Searchable]
		[FormControl(Segment = "basic", Label = "{{portals.sites.controls.[name].label}}", PlaceHolder = "{{portals.sites.controls.[name].placeholder}}", Description = "{{portals.sites.controls.[name].description}}")]
		public string PrimaryDomain { get; set; } = "company.com";

		[Property(MaxLength = 20), Sortable(UniqueIndexName = "Domains")]
		[FormControl(Segment = "basic", Label = "{{portals.sites.controls.[name].label}}", PlaceHolder = "{{portals.sites.controls.[name].placeholder}}", Description = "{{portals.sites.controls.[name].description}}")]
		public string SubDomain { get; set; } = "*";

		[Property(MaxLength = 500), Sortable(IndexName = "OtherDomains"), Searchable]
		[FormControl(Segment = "basic", Label = "{{portals.sites.controls.[name].label}}", PlaceHolder = "{{portals.sites.controls.[name].placeholder}}", Description = "{{portals.sites.controls.[name].description}}")]
		public string OtherDomains { get; set; }

		[Ignore, BsonIgnore]
		[FormControl(Segment = "basic", Label = "{{portals.sites.controls.[name].label}}", PlaceHolder = "{{portals.sites.controls.[name].placeholder}}", Description = "{{portals.sites.controls.[name].description}}")]
		public bool AlwaysUseHTTPs { get; set; } = false;

		[Property(MaxLength = 5)]
		[FormControl(Segment = "display", ControlType = "Select", Label = "{{portals.sites.controls.[name].label}}", PlaceHolder = "{{portals.sites.controls.[name].placeholder}}", Description = "{{portals.sites.controls.[name].description}}")]
		public string Language { get; set; } = "vi-VN";

		[Property(MaxLength = 100)]
		[FormControl(Segment = "display", Label = "{{portals.sites.controls.[name].label}}", PlaceHolder = "{{portals.sites.controls.[name].placeholder}}", Description = "{{portals.sites.controls.[name].description}}")]
		public string Theme { get; set; }

		[Property(MaxLength = 32)]
		[FormControl(Segment = "display", ControlType = "Lookup", Label = "{{portals.sites.controls.[name].label}}", PlaceHolder = "{{portals.sites.controls.[name].placeholder}}", Description = "{{portals.sites.controls.[name].description}}")]
		public string HomeDesktopID { get; set; }

		[Property(MaxLength = 32)]
		[FormControl(Segment = "display", ControlType = "Lookup", Label = "{{portals.sites.controls.[name].label}}", PlaceHolder = "{{portals.sites.controls.[name].placeholder}}", Description = "{{portals.sites.controls.[name].description}}")]
		public string SearchDesktopID { get; set; }

		[Ignore, BsonIgnore]
		[FormControl(Segment = "display", Label = "{{portals.sites.controls.[name].label}}", PlaceHolder = "{{portals.sites.controls.[name].placeholder}}", Description = "{{portals.sites.controls.[name].description}}")]
		public Settings.UI UISettings { get; set; }

		[Ignore, BsonIgnore]
		[FormControl(Segment = "display", ControlType = "Lookup", Label = "{{portals.sites.controls.[name].label}}", PlaceHolder = "{{portals.sites.controls.[name].placeholder}}", Description = "{{portals.sites.controls.[name].description}}")]
		public string IconURI { get; set; }

		[Ignore, BsonIgnore]
		[FormControl(Segment = "display", ControlType = "Lookup", Label = "{{portals.sites.controls.[name].label}}", PlaceHolder = "{{portals.sites.controls.[name].placeholder}}", Description = "{{portals.sites.controls.[name].description}}")]
		public string CoverURI { get; set; }

		[Ignore, BsonIgnore]
		[FormControl(Segment = "display", ControlType = "TextArea", Label = "{{portals.sites.controls.[name].label}}", PlaceHolder = "{{portals.sites.controls.[name].placeholder}}", Description = "{{portals.sites.controls.[name].description}}")]
		public string MetaTags { get; set; }

		[Ignore, BsonIgnore]
		[FormControl(Segment = "display", ControlType = "TextArea", Label = "{{portals.sites.controls.[name].label}}", PlaceHolder = "{{portals.sites.controls.[name].placeholder}}", Description = "{{portals.sites.controls.[name].description}}")]
		public string Scripts { get; set; }

		[Ignore, BsonIgnore]
		[FormControl(Segment = "seo", Label = "{{portals.sites.controls.[name].label}}", PlaceHolder = "{{portals.sites.controls.[name].placeholder}}", Description = "{{portals.sites.controls.[name].description}}")]
		public bool RedirectToNoneWWW { get; set; } = true;

		[Ignore, BsonIgnore]
		[FormControl(Segment = "seo", Label = "{{portals.sites.controls.[name].label}}", PlaceHolder = "{{portals.sites.controls.[name].placeholder}}", Description = "{{portals.sites.controls.[name].description}}")]
		public Settings.SEOInfo SEOInfo { get; set; }

		[NonSerialized]
		JObject _json;

		string _extras;

		[Property(IsCLOB = true)]
		[JsonIgnore, XmlIgnore]
		[FormControl(Excluded = true)]
		public string Extras
		{
			get => this._extras;
			set
			{
				this._extras = value;
				this._json = this._json ?? JObject.Parse(string.IsNullOrWhiteSpace(this._extras) ? "{}" : this._extras);
				this.NotifyPropertyChanged();
			}
		}

		[Sortable(IndexName = "Audits")]
		[FormControl(Hidden = true)]
		public DateTime Created { get; set; }

		[Sortable(IndexName = "Audits")]
		[FormControl(Hidden = true)]
		public string CreatedID { get; set; }

		[Sortable(IndexName = "Audits")]
		[FormControl(Hidden = true)]
		public DateTime LastModified { get; set; }

		[Sortable(IndexName = "Audits")]
		[FormControl(Hidden = true)]
		public string LastModifiedID { get; set; }

		[Property(MaxLength = 32, NotNull = true, NotEmpty = true), Sortable(IndexName = "Management")]
		[FormControl(Hidden = true)]
		public override string SystemID { get; set; }

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public override string RepositoryID { get; set; }

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public override string RepositoryEntityID { get; set; }

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public override Privileges OriginalPrivileges { get; set; }

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public override Privileges WorkingPrivileges => this.Organization?.WorkingPrivileges;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public string OrganizationID => this.SystemID;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public Organization Organization => (this.OrganizationID ?? "").GetOrganizationByID();

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public override RepositoryBase Parent => this.Organization;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		IPortalObject IPortalObject.Parent => this.Organization;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public Desktop HomeDesktop => (this.HomeDesktopID ?? "").GetDesktopByID() ?? this.Organization?.HomeDesktop;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public Desktop SearchDesktop => (this.SearchDesktopID ?? "").GetDesktopByID() ?? this.Organization?.SearchDesktop;

		public override JObject ToJson(bool addTypeOfExtendedProperties = false, Action<JObject> onPreCompleted = null)
			=> base.ToJson(addTypeOfExtendedProperties, json =>
			{
				json.Remove("Privileges");
				json.Remove("OriginalPrivileges");
				onPreCompleted?.Invoke(json);
			});

		internal void NormalizeExtras()
		{
			this.UISettings?.Normalize();
			this.UISettings = this.UISettings != null && string.IsNullOrWhiteSpace(this.UISettings.Padding) && string.IsNullOrWhiteSpace(this.UISettings.Margin) && string.IsNullOrWhiteSpace(this.UISettings.Width) && string.IsNullOrWhiteSpace(this.UISettings.Height) && string.IsNullOrWhiteSpace(this.UISettings.Color) && string.IsNullOrWhiteSpace(this.UISettings.BackgroundColor) && string.IsNullOrWhiteSpace(this.UISettings.BackgroundImageURI) && string.IsNullOrWhiteSpace(this.UISettings.BackgroundImageRepeat) && string.IsNullOrWhiteSpace(this.UISettings.BackgroundImagePosition) && string.IsNullOrWhiteSpace(this.UISettings.BackgroundImageSize) && string.IsNullOrWhiteSpace(this.UISettings.Css) && string.IsNullOrWhiteSpace(this.UISettings.Style) ? null : this.UISettings;
			this.IconURI = string.IsNullOrWhiteSpace(this.IconURI) ? null : this.IconURI.Trim();
			this.CoverURI = string.IsNullOrWhiteSpace(this.CoverURI) ? null : this.CoverURI.Trim();
			this.MetaTags = string.IsNullOrWhiteSpace(this.MetaTags) ? null : this.MetaTags.Trim();
			this.Scripts = string.IsNullOrWhiteSpace(this.Scripts) ? null : this.Scripts.Trim();
			this.SEOInfo?.Normalize();
			this.SEOInfo = this.SEOInfo != null && string.IsNullOrWhiteSpace(this.SEOInfo.Title) && string.IsNullOrWhiteSpace(this.SEOInfo.Description) && string.IsNullOrWhiteSpace(this.SEOInfo.Keywords) ? null : this.SEOInfo;
			this._json = this._json ?? JObject.Parse(string.IsNullOrWhiteSpace(this.Extras) ? "{}" : this.Extras);
			SiteExtensions.ExtraProperties.ForEach(name => this._json[name] = this.GetProperty(name)?.ToJson());
			this._extras = this._json.ToString(Formatting.None);
		}

		public override void ProcessPropertyChanged(string name)
		{
			if (name.IsEquals("Extras"))
			{
				this._json = this._json ?? JObject.Parse(string.IsNullOrWhiteSpace(this.Extras) ? "{}" : this.Extras);
				this.AlwaysUseHTTPs = this._json["AlwaysUseHTTPs"] != null ? this._json["AlwaysUseHTTPs"].FromJson<bool>() : false;
				this.UISettings = this._json["UISettings"]?.FromJson<Settings.UI>();
				this.IconURI = this._json["IconURI"]?.FromJson<string>();
				this.CoverURI = this._json["CoverURI"]?.FromJson<string>();
				this.MetaTags = this._json["MetaTags"]?.FromJson<string>();
				this.Scripts = this._json["Scripts"]?.FromJson<string>();
				this.RedirectToNoneWWW = this._json["RedirectToNoneWWW"] != null ? this._json["RedirectToNoneWWW"].FromJson<bool>() : true;
				this.SEOInfo = this._json["SEOInfo"]?.FromJson<Settings.SEOInfo>();
			}
			else if (SiteExtensions.ExtraProperties.Contains(name))
			{
				this._json = this._json ?? JObject.Parse(string.IsNullOrWhiteSpace(this.Extras) ? "{}" : this.Extras);
				this._json[name] = this.GetProperty(name)?.ToJson();
			}
		}
	}

	public static class SiteExtensions
	{
		public static ConcurrentDictionary<string, Site> Sites { get; } = new ConcurrentDictionary<string, Site>(StringComparer.OrdinalIgnoreCase);

		public static ConcurrentDictionary<string, Site> SitesByDomain { get; } = new ConcurrentDictionary<string, Site>(StringComparer.OrdinalIgnoreCase);

		public static HashSet<string> ExtraProperties { get; } = "AlwaysUseHTTPs,UISettings,IconURI,CoverURI,MetaTags,Scripts,RedirectToNoneWWW,SEOInfo".ToHashSet();

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

		public static Site Set(this Site site, bool clear = false, bool updateCache = false)
		{
			if (site != null)
			{
				if (clear)
					site.Remove();

				SiteExtensions.Sites[site.ID] = site;
				SiteExtensions.SitesByDomain[$"{site.SubDomain}.{site.PrimaryDomain}"] = site;
				Utility.NotRecognizedAliases.Remove($"Site:{(site.SubDomain.Equals("*") ? "" : $"{site.SubDomain}.")}{site.PrimaryDomain}");

				if (!string.IsNullOrWhiteSpace(site.OtherDomains))
					site.OtherDomains.ToList(";").Where(domain => !string.IsNullOrWhiteSpace(domain)).ForEach(domain =>
					{
						if (SiteExtensions.SitesByDomain.TryAdd(domain, site))
						{
							SiteExtensions.SitesByDomain.TryAdd($"*.{domain}", site);
							Utility.NotRecognizedAliases.Remove($"Site:{domain}");
						}
					});

				if (updateCache)
					Utility.Cache.Set(site);
			}
			return site;
		}

		public static async Task<Site> SetAsync(this Site site, bool clear = false, bool updateCache = false, CancellationToken cancellationToken = default)
		{
			site?.Set(clear);
			await (updateCache && site != null ? Utility.Cache.SetAsync(site, cancellationToken) : Task.CompletedTask).ConfigureAwait(false);
			return site;
		}

		public static Site Remove(this Site site)
			=> (site?.ID ?? "").RemoveSite();

		public static Site RemoveSite(this string id)
		{
			if (!string.IsNullOrWhiteSpace(id) && SiteExtensions.Sites.TryGetValue(id, out var site) && site != null)
			{
				SiteExtensions.Sites.Remove(site.ID);
				SiteExtensions.SitesByDomain.Remove($"{site.SubDomain}.{site.PrimaryDomain}");
				if (!string.IsNullOrWhiteSpace(site.OtherDomains))
					site.OtherDomains.ToList(";").Where(domain => !string.IsNullOrWhiteSpace(domain)).ForEach(domain =>
					{
						if (SiteExtensions.SitesByDomain.Remove(domain))
							SiteExtensions.SitesByDomain.Remove($"*.{domain}");
					});
				return site;
			}
			return null;
		}

		public static Site GetSiteByID(this string id, bool force = false, bool fetchRepository = true)
			=> !force && !string.IsNullOrWhiteSpace(id) && SiteExtensions.Sites.ContainsKey(id)
				? SiteExtensions.Sites[id]
				: fetchRepository && !string.IsNullOrWhiteSpace(id)
					? Site.Get<Site>(id)?.Set()
					: null;

		public static async Task<Site> GetSiteByIDAsync(this string id, CancellationToken cancellationToken = default, bool force = false)
			=> (id ?? "").GetSiteByID(force, false) ?? (await Site.GetAsync<Site>(id, cancellationToken).ConfigureAwait(false))?.Set();

		public static Tuple<string, string> GetSiteDomains(this string domain)
		{
			var info = domain.ToArray(".");
			return new Tuple<string, string>(info.Skip(1).Join("."), info.First());
		}

		public static Site GetSiteByDomain(this string domain, string defaultSiteIDWhenNotFound = null, bool fetchRepository = true)
		{
			if (string.IsNullOrWhiteSpace(domain) || Utility.NotRecognizedAliases.Contains($"Site:{domain}"))
				return (defaultSiteIDWhenNotFound ?? "").GetSiteByID(false, false);

			domain = domain.StartsWith("*.") ? domain.Right(domain.Length - 2) : domain;
			if (!SiteExtensions.SitesByDomain.TryGetValue(domain, out var site))
				SiteExtensions.SitesByDomain.TryGetValue($"*.{domain}", out site);

			if (site == null)
			{
				var name = domain;
				var dotOffset = name.IndexOf(".");
				if (dotOffset < 0)
					SiteExtensions.SitesByDomain.TryGetValue($"*.{name}", out site);
				else
					while (site == null && dotOffset > 0)
					{
						if (!SiteExtensions.SitesByDomain.TryGetValue(name, out site))
							SiteExtensions.SitesByDomain.TryGetValue($"*.{name}", out site);

						if (site == null)
						{
							name = name.Right(name.Length - dotOffset - 1);
							dotOffset = name.IndexOf(".");
						}
					}
			}

			if (site == null && fetchRepository)
			{
				var domains = domain.GetSiteDomains();
				var filter = Filters<Site>.Or(
					Filters<Site>.And(Filters<Site>.Equals("SubDomain", "*"), Filters<Site>.Equals("PrimaryDomain", domains.Item1)),
					Filters<Site>.And(Filters<Site>.Equals("SubDomain", domains.Item2), Filters<Site>.Equals("PrimaryDomain", domains.Item1)),
					Filters<Site>.And(Filters<Site>.Equals("SubDomain", "*"), Filters<Site>.Equals("PrimaryDomain", domain)),
					Filters<Site>.Contains("OtherDomains", domain)
				);
				site = Site.Get<Site>(filter, null, null)?.Set();
				if (site == null)
					Utility.NotRecognizedAliases.Add($"Site:{domain}");
			}

			return site ?? (defaultSiteIDWhenNotFound ?? "").GetSiteByID(false, false);
		}

		public static async Task<Site> GetSiteByDomainAsync(this string domain, string defaultSiteIDWhenNotFound = null, CancellationToken cancellationToken = default)
		{
			var site = (domain ?? "").GetSiteByDomain(defaultSiteIDWhenNotFound, false);
			if (site == null)
			{
				var domains = domain.GetSiteDomains();
				var filter = Filters<Site>.Or(
					Filters<Site>.And(Filters<Site>.Equals("SubDomain", "*"), Filters<Site>.Equals("PrimaryDomain", domains.Item1)),
					Filters<Site>.And(Filters<Site>.Equals("SubDomain", domains.Item2), Filters<Site>.Equals("PrimaryDomain", domains.Item1)),
					Filters<Site>.And(Filters<Site>.Equals("SubDomain", "*"), Filters<Site>.Equals("PrimaryDomain", domain)),
					Filters<Site>.Contains("OtherDomains", domain)
				);
				site = (await Site.GetAsync<Site>(filter, null, null, cancellationToken).ConfigureAwait(false))?.Set();
				if (site == null)
					Utility.NotRecognizedAliases.Add($"Site:{domain}");
			}
			return site ?? (defaultSiteIDWhenNotFound ?? "").GetSiteByID(false, false);
		}
	}
}
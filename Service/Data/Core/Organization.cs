#region Related components
using System;
using System.Linq;
using System.Dynamic;
using System.Diagnostics;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Serialization;
using MsgPack.Serialization;
using MongoDB.Bson.Serialization.Attributes;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Converters;
using net.vieapps.Components.Repository;
using net.vieapps.Components.Security;
using net.vieapps.Components.Utility;
using net.vieapps.Services.Portals.Settings;
#endregion

namespace net.vieapps.Services.Portals
{
	[BsonIgnoreExtraElements, DebuggerDisplay("ID = {ID}, Title = {Title}")]
	[Entity(CollectionName = "Organizations", TableName = "T_Portals_Organizations", CacheClass = typeof(Utility), CacheName = "Cache", Searchable = true)]
	public sealed class Organization : Repository<Organization>, IPortalObject
	{
		public Organization() : base()
			=> this.OriginalPrivileges = new(true);

		[Property(MaxLength = 250, NotNull = true, NotEmpty = true)]
		[Sortable(IndexName = "Title")]
		[Searchable]
		[FormControl(Segment = "basic", Label = "{{portals.organizations.controls.[name].label}}", PlaceHolder = "{{portals.organizations.controls.[name].placeholder}}", Description = "{{portals.organizations.controls.[name].description}}")]
		public override string Title { get; set; }

		[Property(MaxLength = 250)]
		[Searchable]
		[FormControl(Segment = "basic", ControlType = "TextArea", Label = "{{portals.organizations.controls.[name].label}}", PlaceHolder = "{{portals.organizations.controls.[name].placeholder}}", Description = "{{portals.organizations.controls.[name].description}}")]
		public string Description { get; set; }

		[Property(MaxLength = 32)]
		[Sortable(IndexName = "Management")]
		[FormControl(Segment = "basic", ControlType = "Lookup", Label = "{{portals.organizations.controls.[name].label}}", PlaceHolder = "{{portals.organizations.controls.[name].placeholder}}", Description = "{{portals.organizations.controls.[name].description}}")]
		public string OwnerID { get; set; }

		[JsonConverter(typeof(StringEnumConverter)), BsonRepresentation(MongoDB.Bson.BsonType.String)]
		[Sortable(IndexName = "Management")]
		[FormControl(Segment = "basic", Label = "{{portals.organizations.controls.[name].label}}", PlaceHolder = "{{portals.organizations.controls.[name].placeholder}}", Description = "{{portals.organizations.controls.[name].description}}")]
		public ApprovalStatus Status { get; set; } = ApprovalStatus.Pending;

		[Property(MaxLength = 100, NotNull = true, NotEmpty = true)]
		[Sortable(IndexName = "Management", UniqueIndexName = "Alias")]
		[FormControl(Segment = "basic", Label = "{{portals.organizations.controls.[name].label}}", PlaceHolder = "{{portals.organizations.controls.[name].placeholder}}", Description = "{{portals.organizations.controls.[name].description}}")]
		public string Alias { get; set; } = "";

		[Property(MaxLength = 10, NotNull = true, NotEmpty = true)]
		[Sortable(IndexName = "Management")]
		[FormControl(Segment = "basic", Label = "{{portals.organizations.controls.[name].label}}", PlaceHolder = "{{portals.organizations.controls.[name].placeholder}}", Description = "{{portals.organizations.controls.[name].description}}")]
		public string ExpiredDate { get; set; } = "-";

		[Sortable(IndexName = "Management")]
		[FormControl(Segment = "basic", Label = "{{portals.organizations.controls.[name].label}}", PlaceHolder = "{{portals.organizations.controls.[name].placeholder}}", Description = "{{portals.organizations.controls.[name].description}}")]
		public long FilesQuotes { get; set; } = 0;

		[Sortable(IndexName = "Management")]
		[FormControl(Segment = "basic", Label = "{{portals.organizations.controls.[name].label}}", PlaceHolder = "{{portals.organizations.controls.[name].placeholder}}", Description = "{{portals.organizations.controls.[name].description}}")]
		public bool Required2FA { get; set; } = false;

		[Sortable(IndexName = "Management")]
		[FormControl(Segment = "basic", Label = "{{portals.organizations.controls.[name].label}}", PlaceHolder = "{{portals.organizations.controls.[name].placeholder}}", Description = "{{portals.organizations.controls.[name].description}}")]
		public bool TrackDownloadFiles { get; set; } = false;

		[Property(MaxLength = 100)]
		[FormControl(Segment = "basic", ControlType = "Select", Label = "{{portals.organizations.controls.[name].label}}", PlaceHolder = "{{portals.organizations.controls.[name].placeholder}}", Description = "{{portals.organizations.controls.[name].description}}")]
		public string Theme { get; set; }

		[Property(MaxLength = 32)]
		[FormControl(Segment = "basic", ControlType = "Lookup", Label = "{{portals.organizations.controls.[name].label}}", PlaceHolder = "{{portals.organizations.controls.[name].placeholder}}", Description = "{{portals.organizations.controls.[name].description}}")]
		public string HomeDesktopID { get; set; }

		[Property(MaxLength = 32)]
		[FormControl(Segment = "basic", ControlType = "Lookup", Label = "{{portals.organizations.controls.[name].label}}", PlaceHolder = "{{portals.organizations.controls.[name].placeholder}}", Description = "{{portals.organizations.controls.[name].description}}")]
		public string SearchDesktopID { get; set; }

		JObject _json;
		string _extras;

		[JsonIgnore, XmlIgnore]
		[Property(IsCLOB = true)]
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

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public override string SystemID { get; set; }

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public override string RepositoryID { get; set; }

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public override string RepositoryEntityID { get; set; }

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore, MessagePackIgnore]
		public string OrganizationID => this.ID;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore, MessagePackIgnore]
		public override RepositoryBase Parent => null;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore, MessagePackIgnore]
		IPortalObject IPortalObject.Parent => null;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore, MessagePackIgnore]
		public override Privileges WorkingPrivileges => this.OriginalPrivileges ?? new(true);

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore, MessagePackIgnore]
		public Desktop DefaultDesktop => this.HomeDesktop ?? DesktopProcessor.Desktops.Values.Where(desktop => desktop.SystemID.IsEquals(this.ID)).FirstOrDefault();

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore, MessagePackIgnore]
		public Desktop HomeDesktop => (this.HomeDesktopID ?? "").GetDesktopByID();

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore, MessagePackIgnore]
		public Desktop SearchDesktop => (this.SearchDesktopID ?? "").GetDesktopByID();

		[Ignore, BsonIgnore, XmlIgnore, MessagePackIgnore]
		public Settings.Notifications Notifications { get; set; } = new();

		[Ignore, BsonIgnore, XmlIgnore, MessagePackIgnore]
		public Dictionary<string, Dictionary<string, Settings.Instruction>> Instructions { get; set; } = [];

		[Ignore, BsonIgnore, XmlIgnore, MessagePackIgnore]
		public List<string> Socials { get; set; } = new();

		[Ignore, BsonIgnore, XmlIgnore, MessagePackIgnore]
		public Dictionary<string, string> Trackings { get; set; } = [];

		[Ignore, BsonIgnore, XmlIgnore, MessagePackIgnore]
		public string MetaTags { get; set; }

		[Ignore, BsonIgnore, XmlIgnore, MessagePackIgnore]
		public string ScriptLibraries { get; set; }

		[Ignore, BsonIgnore, XmlIgnore, MessagePackIgnore]
		public string Scripts { get; set; }

		[Ignore, BsonIgnore, XmlIgnore, MessagePackIgnore]
		public bool AlwaysUseHtmlSuffix { get; set; } = true;

		[Ignore, BsonIgnore, XmlIgnore, MessagePackIgnore]
		public Settings.RefreshURLs RefreshURLs { get; set; } = new();

		[Ignore, BsonIgnore, XmlIgnore, MessagePackIgnore]
		public Settings.RedirectURLs RedirectURLs { get; set; } = new();

		[Ignore, BsonIgnore, XmlIgnore, MessagePackIgnore]
		public Settings.Email EmailSettings { get; set; } = new();

		[Ignore, BsonIgnore, XmlIgnore, MessagePackIgnore]
		public Settings.WebHookSetting WebHookSettings { get; set; } = new();

		[Ignore, BsonIgnore, XmlIgnore, MessagePackIgnore]
		public List<Settings.HttpIndicator> HttpIndicators { get; set; }

		[Ignore, BsonIgnore, XmlIgnore, MessagePackIgnore]
		public string FakeFilesHttpURI { get; set; }

		[Ignore, BsonIgnore, XmlIgnore, MessagePackIgnore]
		public string FakePortalsHttpURI { get; set; }

		[Ignore, BsonIgnore, XmlIgnore, MessagePackIgnore]
		public string CDNProvider { get; set; } = "Cloudflare";

		[Ignore, BsonIgnore, XmlIgnore, MessagePackIgnore]
		public string CDNZoneID { get; set; }

		[Ignore, BsonIgnore, XmlIgnore, MessagePackIgnore]
		public string CDNApiToken { get; set; }

		[Ignore, BsonIgnore, XmlIgnore, MessagePackIgnore]
		public List<Settings.ExamineURLs> ExamineURLs { get; set; }

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore, MessagePackIgnore]
		public McpSettings McpSettings { get; set; }

		internal List<string> _siteIDs = null;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public List<string> SiteIDs
		{
			get => this._siteIDs;
			set => this._siteIDs = value;
		}

		public string GetURL(bool useRelativeURL, string siteURL, string suffix)
			=> useRelativeURL ? $"~/~{this.Alias}" : (siteURL ?? $"{this.FakePortalsHttpURI ?? Utility.PortalsHttpURI}/~{this.Alias}") + (suffix ?? (this.AlwaysUseHtmlSuffix ? "/index.html" : "/"));
	
		public string GetURL(bool useRelativeURL, string suffix)
			=> this.GetURL(useRelativeURL, null as string, suffix);

		public string GetURL(bool useRelativeURL = true, Site site = null, string suffix = null)
			=> this.GetURL(useRelativeURL, site?.GetURL(), suffix);

		internal List<Site> FindSites(List<Site> sites = null, bool notifyPropertyChanged = true)
		{
			if (this._siteIDs == null)
			{
				sites = sites ?? (this.ID ?? "").FindSites();
				this._siteIDs = sites.Where(site => site != null).Select(site => site.ID).ToList();
				if (notifyPropertyChanged)
					this.NotifyPropertyChanged("Sites");
				return sites.Where(site => site != null).OrderBy(site => site.PrimaryDomain).ThenBy(site => site.SubDomain).ThenBy(site => site.Title).ToList();
			}
			return this._siteIDs.Select(siteID => siteID.GetSiteByID()).Where(site => site != null).OrderBy(site => site.PrimaryDomain).ThenBy(site => site.SubDomain).ThenBy(site => site.Title).ToList();
		}

		internal async Task<List<Site>> FindSitesAsync(CancellationToken cancellationToken = default, bool notifyPropertyChanged = true)
			=> this._siteIDs == null
				? this.FindSites(await (this.ID ?? "").FindSitesAsync(cancellationToken).ConfigureAwait(false), notifyPropertyChanged)
				: this._siteIDs.Select(siteID => siteID.GetSiteByID()).Where(site => site != null).OrderBy(site => site.PrimaryDomain).ThenBy(site => site.SubDomain).ThenBy(site => site.Title).ToList();

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore, MessagePackIgnore]
		public List<Site> Sites => this.FindSites();

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore, MessagePackIgnore]
		public Site DefaultSite => this.Sites?.FirstOrDefault(site => site.IsDefault) ?? this.Sites?.FirstOrDefault(site => "*".Equals(site.SubDomain)) ?? this.Sites?.FirstOrDefault();

		internal List<string> _moduleIDs;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public List<string> ModuleIDs
		{
			get => this._moduleIDs;
			set => this._moduleIDs = value;
		}

		internal List<Module> FindModules(List<Module> modules = null, bool notifyPropertyChanged = true)
		{
			if (this._moduleIDs == null)
			{
				modules = modules ?? (this.ID ?? "").FindModules();
				this._moduleIDs = modules.Select(module => module.ID).ToList();
				if (notifyPropertyChanged)
					this.NotifyPropertyChanged("Modules");
				return modules;
			}
			return this._moduleIDs?.Select(id => id.GetModuleByID()).Where(module => module != null).ToList();
		}

		internal async Task<List<Module>> FindModulesAsync(CancellationToken cancellationToken = default, bool notifyPropertyChanged = true)
			=> this._moduleIDs == null
				? this.FindModules(await (this.ID ?? "").FindModulesAsync(null, cancellationToken).ConfigureAwait(false), notifyPropertyChanged)
				: this._moduleIDs.Select(id => id.GetModuleByID()).Where(module => module != null).ToList();

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore, MessagePackIgnore]
		public List<Module> Modules => this.FindModules();

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore, MessagePackIgnore]
		public List<ContentType> ContentTypes => this.Modules.Select(module => module.ContentTypes).SelectMany(contentTypes => contentTypes).ToList();

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore, MessagePackIgnore]
		public List<ContentType> ContentTypesOfCategory => this.Modules.Select(module => module.ContentTypesOfCategory).SelectMany(contentTypes => contentTypes).ToList();

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore, MessagePackIgnore]
		public List<ContentType> ContentTypesOfContent => this.Modules.Select(module => module.ContentTypesOfContent).SelectMany(contentTypes => contentTypes).ToList();

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore, MessagePackIgnore]
		public List<ContentType> ContentTypesOfItem => this.Modules.Select(module => module.ContentTypesOfItem).SelectMany(contentTypes => contentTypes).ToList();

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore, MessagePackIgnore]
		public List<ContentType> ContentTypesOfLink => this.Modules.Select(module => module.ContentTypesOfLink).SelectMany(contentTypes => contentTypes).ToList();

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore, MessagePackIgnore]
		public List<ContentType> ContentTypesOfForm => this.Modules.Select(module => module.ContentTypesOfForm).SelectMany(contentTypes => contentTypes).ToList();

		public override JObject ToJson(bool addTypeOfExtendedProperties, Action<JObject> onCompleted = null)
			=> this.ToJson(false, addTypeOfExtendedProperties, onCompleted);

		public JObject ToJson(bool addModules, bool addTypeOfExtendedProperties, Action<JObject> onCompleted = null)
			=> base.ToJson(addTypeOfExtendedProperties, json =>
			{
				json["McpSettings"] = this.McpSettings?.ToJson().ToString(Formatting.Indented);
				json.Remove("OriginalPrivileges");
				if (addModules)
					json["Modules"] = this.Modules.ToJArray(module => module?.ToJson(true, addTypeOfExtendedProperties));
				onCompleted?.Invoke(json);
			});

		internal void NormalizeExtras()
		{
			this.Notifications = this.Notifications?.Normalize();
			this.Instructions = this.Instructions?.Select(kvp => KeyValuePair.Create(kvp.Key, kvp.Value)).Select(kvp =>
			{
				var instructions = kvp.Value?.Select(pair => KeyValuePair.Create(pair.Key, pair.Value?.Normalize())).Where(pair => pair.Value != null).ToDictionary();
				return KeyValuePair.Create(kvp.Key, instructions);
			}).Where(kvp => kvp.Value != null).ToDictionary();
			this.Instructions = this.Instructions != null && this.Instructions.Count > 0 ? this.Instructions : null;
			this.Socials = this.Socials != null && this.Socials.Count > 0 ? this.Socials : null;
			this.Trackings = (this.Trackings ?? []).Where(kvp => !string.IsNullOrWhiteSpace(kvp.Value)).ToDictionary();
			this.Trackings = this.Trackings.Any() ? this.Trackings : null;
			this.MetaTags = string.IsNullOrWhiteSpace(this.MetaTags) ? null : this.MetaTags.Trim();
			this.ScriptLibraries = string.IsNullOrWhiteSpace(this.ScriptLibraries) ? null : this.ScriptLibraries.Trim();
			this.Scripts = string.IsNullOrWhiteSpace(this.Scripts) ? null : this.Scripts.Trim();
			this.RefreshURLs = this.RefreshURLs?.Normalize();
			this.RedirectURLs = this.RedirectURLs?.Normalize();
			this.EmailSettings = this.EmailSettings?.Normalize();
			this.WebHookSettings = this.WebHookSettings?.Normalize();
			this.HttpIndicators = this.HttpIndicators?.Select(indicator => indicator.Normalize()).Where(indicator => indicator != null).ToList();
			this.HttpIndicators = this.HttpIndicators != null && this.HttpIndicators.Count > 0 ? this.HttpIndicators : null;
			try
			{
				var uri = new Uri(this.FakeFilesHttpURI);
				this.FakeFilesHttpURI = uri.AbsoluteUri;
				while (this.FakeFilesHttpURI.EndsWith('/') || this.FakeFilesHttpURI.EndsWith('.'))
					this.FakeFilesHttpURI = this.FakeFilesHttpURI.Left(this.FakeFilesHttpURI.Length - 1);
			}
			catch
			{
				this.FakeFilesHttpURI = null;
			}
			try
			{
				var uri = new Uri(this.FakePortalsHttpURI);
				this.FakePortalsHttpURI = uri.AbsoluteUri;
				while (this.FakePortalsHttpURI.EndsWith('/') || this.FakePortalsHttpURI.EndsWith('.'))
					this.FakePortalsHttpURI = this.FakePortalsHttpURI.Left(this.FakePortalsHttpURI.Length - 1);
			}
			catch
			{
				this.FakePortalsHttpURI = null;
			}
			if (!this.GotCDN())
				this.CDNZoneID = this.CDNApiToken = null;
			this.ExamineURLs = this.ExamineURLs?.Select(examineURL => examineURL.Normalize(_ =>
			{
				var defaultURL = $"{Utility.PortalsHttpURI}/~{this.Alias}";
				var rootURL = this.FakePortalsHttpURI != null ? $"{this.FakePortalsHttpURI}/~{this.Alias}" : null;
				var siteURL = this.DefaultSite?.GetURL();
				var siteHost = this.DefaultSite?.Host;
				var siteDomain = this.DefaultSite?.PrimaryDomain;
				examineURL.URLs = examineURL.URLs.Where(url => url == "*" || url.StartsWith('/') || url.IsStartsWith("https://") || url.IsStartsWith("http://") || url.IsStartsWith("s:/") || url.IsStartsWith("s:https://") || url.IsStartsWith("s:http://") || url.IsStartsWith("c:/") || url.IsStartsWith("c:https://") || url.IsStartsWith("c:http://") || url.IsStartsWith("e:/") || url.IsStartsWith("e:https://") || url.IsStartsWith("e:http://"))
					.Select(url => url.IsStartsWith("e:/") ? url.Right(url.Length - 2) : url)
					.Select(url => (this.AlwaysUseHtmlSuffix && url.IsEndsWith(".html") ? url.Left(url.Length - 5) : url).Trim())
					.Select(url => url.IsStartsWith(defaultURL) || url.IsStartsWith($"s:{defaultURL}") || url.IsStartsWith($"c:{defaultURL}")
						? url.Replace(StringComparison.OrdinalIgnoreCase, defaultURL, "")
						: rootURL != null && (url.IsStartsWith(rootURL) || url.IsStartsWith($"s:{rootURL}") || url.IsStartsWith($"c:{rootURL}"))
							? url.Replace(StringComparison.OrdinalIgnoreCase, rootURL, "")
							: siteURL != null && (url.IsStartsWith(siteURL) || url.IsStartsWith($"s:{siteURL}") || url.IsStartsWith($"c:{siteURL}"))
								? url.Replace(StringComparison.OrdinalIgnoreCase, siteURL, "")
								: url)
					.Select(url => url.Replace(StringComparison.OrdinalIgnoreCase, $"http://{siteHost}", "").Replace(StringComparison.OrdinalIgnoreCase, $"http://{siteDomain}", ""))
					.Select(url => url.Replace(StringComparison.OrdinalIgnoreCase, $"https://{siteHost}", "").Replace(StringComparison.OrdinalIgnoreCase, $"https://{siteDomain}", ""))
					.Distinct(StringComparer.OrdinalIgnoreCase)
					.ToList();
			})).Where(examineURL => examineURL != null).ToList();
			this.ExamineURLs = this.ExamineURLs != null && this.ExamineURLs.Count > 0 ? this.ExamineURLs : null;
			this.McpSettings = this.McpSettings?.Normalize();
			this._json = this._json ?? JObject.Parse(string.IsNullOrWhiteSpace(this.Extras) ? "{}" : this.Extras);
			OrganizationProcessor.ExtraProperties.ForEach(name => this._json[name] = this.GetProperty(name)?.ToJson());
			this._extras = this._json.ToString(Formatting.None);
			this.PrepareRedirectAddresses();
		}

		public override void ProcessPropertyChanged(string name)
		{
			if (name.IsEquals("Extras"))
			{
				this._json = this._json ?? JObject.Parse(string.IsNullOrWhiteSpace(this.Extras) ? "{}" : this.Extras);
				this.Notifications = this._json["Notifications"]?.As<Settings.Notifications>();
				this.Instructions = Settings.Instruction.Parse(this._json["Instructions"]?.ToExpandoObject());
				this.Socials = this._json["Socials"]?.As<List<string>>();
				this.Trackings = this._json["Trackings"]?.As<Dictionary<string, string>>();
				this.MetaTags = this._json["MetaTags"]?.As<string>();
				this.ScriptLibraries = this._json["ScriptLibraries"]?.As<string>();
				this.Scripts = this._json["Scripts"]?.As<string>();
				this.AlwaysUseHtmlSuffix = this._json["AlwaysUseHtmlSuffix"] != null && this._json["AlwaysUseHtmlSuffix"].As<bool>();
				this.RefreshURLs = (this._json["RefreshURLs"] ?? this._json["RefreshUrls"])?.As<Settings.RefreshURLs>();
				this.RedirectURLs = (this._json["RedirectURLs"] ?? this._json["RedirectUrls"])?.As<Settings.RedirectURLs>();
				this.EmailSettings = this._json["EmailSettings"]?.As<Settings.Email>();
				this.WebHookSettings = this._json["WebHookSettings"]?.As<Settings.WebHookSetting>();
				this.HttpIndicators = this._json["HttpIndicators"]?.As<List<Settings.HttpIndicator>>();
				this.FakeFilesHttpURI = this._json["FakeFilesHttpURI"]?.As<string>();
				this.FakePortalsHttpURI = this._json["FakePortalsHttpURI"]?.As<string>();
				this.CDNProvider = this._json["CDNProvider"]?.As<string>() ?? "Cloudflare";
				this.CDNZoneID = this._json["CDNZoneID"]?.As<string>() ?? this._json["CloudFlareZoneID"]?.As<string>();
				this.CDNApiToken = this._json["CDNApiToken"]?.As<string>() ?? this._json["CloudFlareApiToken"]?.As<string>();
				this.ExamineURLs = (this._json["ExamineURLs"] as JArray)?.Select(examineURLs => examineURLs as JObject).Select(examineURLs => examineURLs.As<Settings.ExamineURLs>()).Where(examineURLs => examineURLs != null).ToList();
				this.McpSettings = this._json["McpSettings"]?.As<Settings.McpSettings>();
				this.PrepareRedirectAddresses();
			}
			else if (OrganizationProcessor.ExtraProperties.Contains(name))
			{
				this._json = this._json ?? JObject.Parse(string.IsNullOrWhiteSpace(this.Extras) ? "{}" : this.Extras);
				this._json[name] = this.GetProperty(name)?.ToJson();
				if (name.IsEquals("RedirectURLs"))
					this.PrepareRedirectAddresses();
			}
			else if ((name.IsEquals("Modules") || name.IsEquals("Sites")) && !string.IsNullOrWhiteSpace(this.ID) && !string.IsNullOrWhiteSpace(this.Title) && !string.IsNullOrWhiteSpace(this.Theme))
			{
				new CommunicateMessage(Utility.ServiceName)
				{
					Type = $"{this.GetObjectName()}#Update",
					Data = this.ToJson(false, false),
					ExcludedNodeID = Utility.NodeID
				}.Send();
				this.Set(false, true);
			}
		}

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore, MessagePackIgnore]
		List<(string SourceURL, string DestinationURL, string Code)> RedirectAddresses { get; set; }

		void PrepareRedirectAddresses()
			=> this.RedirectAddresses = this.RedirectURLs?.Addresses?.Select(address =>
			{
				var info = address.ToArray('|');
				return info.Length > 1 ? (SourceURL: info[0], DestinationURL: info[1], Code: info.Length > 2 ? Int32.TryParse(info[2], out var code) ? code.ToString() : "302" : "302") : (SourceURL: null, DestinationURL: null, Code: null);
			}).Where(address => address.SourceURL != null && address.DestinationURL != null).ToList() ?? [];

		internal string GetRedirectURL(string requestedURL, out int redirectCode)
		{
			redirectCode = (int)System.Net.HttpStatusCode.Redirect;
			if (!string.IsNullOrWhiteSpace(requestedURL))
			{
				var redirectURL = this.RedirectAddresses.FindIndex(address => requestedURL.IsStartsWith(address.SourceURL)) > -1 ? this.RedirectAddresses.First(address => requestedURL.IsStartsWith(address.SourceURL)).DestinationURL : null;
				if (redirectURL != null)
				{
					redirectCode = this.RedirectAddresses.First(address => requestedURL.IsStartsWith(address.SourceURL)).Code.As<int>();
					return redirectURL;
				}
				else
				{
					var regexAddresses = this.RedirectAddresses.Where(address => address.SourceURL.IsStartsWith("@regex")).ToList() ?? [];
					var regexIndex = 0;
					while (regexIndex < regexAddresses.Count)
					{
						var address = regexAddresses[regexIndex];
						var regex = address.SourceURL.IsStartsWith("@regex(") && address.SourceURL.IsEndsWith(")")
							? address.SourceURL.Left(address.SourceURL.Length - 1).Replace(StringComparison.OrdinalIgnoreCase, "@regex(", "")
							: address.SourceURL.Replace(StringComparison.OrdinalIgnoreCase, "@regex:", "");
						var match = new Regex(regex, RegexOptions.IgnoreCase).Match(requestedURL);
						if (match.Success)
						{
							var isRegEx = address.DestinationURL.IsStartsWith("@regex");
							redirectURL = isRegEx
								? address.DestinationURL.IsStartsWith("@regex(") && address.DestinationURL.IsEndsWith(")")
									? address.DestinationURL.Left(address.DestinationURL.Length - 1).Replace(StringComparison.OrdinalIgnoreCase, "@regex(", "")
									: address.DestinationURL.Replace(StringComparison.OrdinalIgnoreCase, "@regex:", "")
								: address.DestinationURL;
							if (isRegEx)
							{
								var matchIndex = 1;
								while (matchIndex < match.Groups.Count)
								{
									redirectURL = redirectURL.Replace($"${matchIndex}", match.Groups[matchIndex].Value);
									matchIndex++;
								}
							}
							redirectCode = address.Code.As<int>();
							return redirectURL;
						}
						regexIndex++;
					}
				}
			}
			return null;
		}

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore, MessagePackIgnore]
		public bool IsHasSocialLibraries => this.Socials != null && this.Socials.Count > 0;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore, MessagePackIgnore]
		public bool IsHasTrackingLibraries => this.Trackings != null && this.Trackings.Count > 0;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore, MessagePackIgnore]
		public bool IsHasJavascriptLibraries => this.IsHasSocialLibraries || this.IsHasTrackingLibraries || !string.IsNullOrWhiteSpace(this.ScriptLibraries);

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore, MessagePackIgnore]
		public string JavascriptLibraries
		{
			get
			{
				var scripts = "";
				if (this.IsHasSocialLibraries)
				{
					if (this.Socials.IndexOf("Facebook") > -1)
						scripts += $"<script src=\"https://connect.facebook.net/en_US/sdk.js\" async defer></script>";
					if (this.Socials.IndexOf("Twitter") > -1)
						scripts += "<script src=\"https://platform.twitter.com/widgets.js\" async defer></script>";
				}
				if (this.IsHasTrackingLibraries)
				{
					if (this.Trackings.TryGetValue("GoogleAnalytics", out var googleAnalytics) && !string.IsNullOrWhiteSpace(googleAnalytics))
						scripts += "<script src=\"https://www.googletagmanager.com/gtag/js?id=" + googleAnalytics.ToArray(";", true).First() + "\" async defer></script>";
					if (this.Trackings.TryGetValue("FacebookPixel", out var facebookPixels) && !string.IsNullOrWhiteSpace(facebookPixels))
						scripts += $"<script src=\"https://connect.facebook.net/en_US/fbevents.js\" async defer></script>";
				}
				return scripts + (string.IsNullOrWhiteSpace(this.ScriptLibraries) ? "" : this.ScriptLibraries);
			}
		}

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore, MessagePackIgnore]
		public bool IsHasJavascripts => this.IsHasTrackingLibraries || !string.IsNullOrWhiteSpace(this.Scripts);

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore, MessagePackIgnore]
		public string Javascripts
		{
			get
			{
				var scripts = "";
				if (this.IsHasTrackingLibraries)
				{
					if (this.Trackings.TryGetValue("GoogleAnalytics", out var googleAnalytics) && !string.IsNullOrWhiteSpace(googleAnalytics))
					{
						scripts += @"(function () {
							window.dataLayer = window.dataLayer || [];
							window.gtag = function () {
								dataLayer.push(arguments);
							};
							gtag(""js"", new Date());
						})();".Replace("\r\n\t\t\t\t\t\t\t", "\r\n") + "\r\n";
						googleAnalytics.ToArray(";", true).ForEach((googleAnalyticsID, index) => scripts += "gtag(\"config\", \"" + googleAnalyticsID + "\", { \"transport_type\": !!navigator.sendBeacon ? \"beacon\" : \"xhr\"" + (index == 0 ? "" : ", \"groups\": \"agency\"") + " });\r\n");
					}
					if (this.Trackings.TryGetValue("FacebookPixel", out var facebookPixels) && !string.IsNullOrWhiteSpace(facebookPixels))
					{
						scripts += @"(function () {
							var func = window.fbq = function () {
								if (func.callMethod) {
									func.callMethod.apply(func, arguments);
								}
								else {
									func.queue.push(arguments);
								}
							};
							window._fbq = func;
							func.push = func;
							func.loaded = true;
							func.version = '2.0';
							func.queue = [];
						})();".Replace("\r\n\t\t\t\t\t\t\t", "\r\n") + "\r\n";
						facebookPixels.ToArray(";", true).ForEach(facebookPixelID => scripts += "fbq(\"init\", \"" + facebookPixelID + "\");\r\n");
						scripts += "fbq(\"track\", \"PageView\");\r\n";
					}
				}
				return scripts + (string.IsNullOrWhiteSpace(this.Scripts) ? "" : this.Scripts);
			}
		}

		internal Organization ReUpdate(ExpandoObject data = null)
		{
			this._workingPrivileges = null;
			this._siteIDs = null;
			this._moduleIDs = null;
			if (data != null)
				this.Update(data);
			return this;
		}

		internal static async Task<List<Organization>> FindAllAsync(bool findSites, CancellationToken cancellationToken)
		{
			if (findSites)
				await SiteProcessor.FindSitesAsync(null, null, false, cancellationToken).ConfigureAwait(false);
			return await Organization.FindAsync(null, Sorts<Organization>.Ascending("Title"), 0, 1, null, cancellationToken).ConfigureAwait(false) ?? [];
		}

	}
}
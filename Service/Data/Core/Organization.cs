﻿#region Related components
using System;
using System.Linq;
using System.Diagnostics;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Serialization;
using System.Text.RegularExpressions;
using MsgPack.Serialization;
using MongoDB.Bson.Serialization.Attributes;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Converters;
using net.vieapps.Components.Utility;
using net.vieapps.Components.Repository;
using net.vieapps.Components.Security;
#endregion

namespace net.vieapps.Services.Portals
{
	[BsonIgnoreExtraElements, DebuggerDisplay("ID = {ID}, Title = {Title}")]
	[Entity(CollectionName = "Organizations", TableName = "T_Portals_Organizations", CacheClass = typeof(Utility), CacheName = "Cache", Searchable = true)]
	public sealed class Organization : Repository<Organization>, IPortalObject
	{
		public Organization() : base()
			=> this.OriginalPrivileges = new Privileges(true);

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

		[MessagePackIgnore]
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
		public override Privileges WorkingPrivileges => this.OriginalPrivileges ?? new Privileges(true);

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore, MessagePackIgnore]
		public Desktop DefaultDesktop => this.HomeDesktop ?? DesktopProcessor.Desktops.Values.Where(desktop => desktop.SystemID.IsEquals(this.ID)).FirstOrDefault();

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore, MessagePackIgnore]
		public Desktop HomeDesktop => (this.HomeDesktopID ?? "").GetDesktopByID();

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore, MessagePackIgnore]
		public Desktop SearchDesktop => (this.SearchDesktopID ?? "").GetDesktopByID();

		[Ignore, BsonIgnore, XmlIgnore]
		public Settings.Notifications Notifications { get; set; } = new Settings.Notifications();

		[Ignore, BsonIgnore, XmlIgnore]
		public Dictionary<string, Dictionary<string, Settings.Instruction>> Instructions { get; set; } = new Dictionary<string, Dictionary<string, Settings.Instruction>>();

		[Ignore, BsonIgnore, XmlIgnore]
		public List<string> Socials { get; set; } = new List<string>();

		[Ignore, BsonIgnore, XmlIgnore]
		public Dictionary<string, string> Trackings { get; set; } = new Dictionary<string, string>();

		[Ignore, BsonIgnore, XmlIgnore]
		public string MetaTags { get; set; }

		[Ignore, BsonIgnore, XmlIgnore]
		public string ScriptLibraries { get; set; }

		[Ignore, BsonIgnore, XmlIgnore]
		public string Scripts { get; set; }

		[Ignore, BsonIgnore, XmlIgnore]
		public bool AlwaysUseHtmlSuffix { get; set; } = true;

		[Ignore, BsonIgnore, XmlIgnore]
		public Settings.RefreshUrls RefreshUrls { get; set; } = new Settings.RefreshUrls();

		[Ignore, BsonIgnore, XmlIgnore]
		public Settings.RedirectUrls RedirectUrls { get; set; } = new Settings.RedirectUrls();

		[Ignore, BsonIgnore, XmlIgnore]
		public Settings.Email EmailSettings { get; set; } = new Settings.Email();

		[Ignore, BsonIgnore, XmlIgnore]
		public Settings.WebHookSetting WebHookSettings { get; set; } = new Settings.WebHookSetting();

		[Ignore, BsonIgnore, XmlIgnore]
		public List<Settings.HttpIndicator> HttpIndicators { get; set; }

		[Ignore, BsonIgnore, XmlIgnore]
		public string FakeFilesHttpURI { get; set; }

		[Ignore, BsonIgnore, XmlIgnore]
		public string FakePortalsHttpURI { get; set; }

		internal List<string> _siteIDs = null;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public List<string> SiteIDs
		{
			get => this._siteIDs;
			set => this._siteIDs = value;
		}

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public string URL => $"{Utility.PortalsHttpURI}/~{this.Alias}";

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
				: this._siteIDs.Select(siteID => siteID.GetSiteByID()).OrderBy(site => site.PrimaryDomain).ThenBy(site => site.SubDomain).ThenBy(site => site.Title).ToList();

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore, MessagePackIgnore]
		public List<Site> Sites => this.FindSites();

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore, MessagePackIgnore]
		public Site DefaultSite => this.Sites?.FirstOrDefault(site => "*".Equals(site.SubDomain)) ?? this.Sites?.FirstOrDefault();

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
				: this._moduleIDs.Select(id => id.GetModuleByID()).ToList();

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore, MessagePackIgnore]
		public List<Module> Modules => this.FindModules();

		public override JObject ToJson(bool addTypeOfExtendedProperties, Action<JObject> onCompleted = null)
			=> this.ToJson(false, addTypeOfExtendedProperties, onCompleted);

		public JObject ToJson(bool addModules, bool addTypeOfExtendedProperties, Action<JObject> onCompleted = null)
			=> base.ToJson(addTypeOfExtendedProperties, json =>
			{
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
			this.Instructions = this.Instructions != null && this.Instructions.Any() ? this.Instructions : null;
			this.Socials = this.Socials != null && this.Socials.Any() ? this.Socials : null;
			this.Trackings = (this.Trackings ?? new Dictionary<string, string>()).Where(kvp => !string.IsNullOrWhiteSpace(kvp.Value)).ToDictionary();
			this.Trackings = this.Trackings.Any() ? this.Trackings : null;
			this.MetaTags = string.IsNullOrWhiteSpace(this.MetaTags) ? null : this.MetaTags.Trim();
			this.ScriptLibraries = string.IsNullOrWhiteSpace(this.ScriptLibraries) ? null : this.ScriptLibraries.Trim();
			this.Scripts = string.IsNullOrWhiteSpace(this.Scripts) ? null : this.Scripts.Trim();
			this.RefreshUrls = this.RefreshUrls?.Normalize();
			this.RedirectUrls = this.RedirectUrls?.Normalize();
			this.EmailSettings = this.EmailSettings?.Normalize();
			this.WebHookSettings = this.WebHookSettings?.Normalize();
			this.HttpIndicators = this.HttpIndicators?.Select(indicator => indicator.Normalize()).Where(indicator => indicator != null).ToList();
			this.HttpIndicators = this.HttpIndicators != null && this.HttpIndicators.Any() ? this.HttpIndicators : null;
			try
			{
				var uri = new Uri(this.FakeFilesHttpURI);
				this.FakeFilesHttpURI = uri.AbsoluteUri;
				while (this.FakeFilesHttpURI.EndsWith("/") || this.FakeFilesHttpURI.EndsWith("."))
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
				while (this.FakePortalsHttpURI.EndsWith("/") || this.FakePortalsHttpURI.EndsWith("."))
					this.FakePortalsHttpURI = this.FakePortalsHttpURI.Left(this.FakePortalsHttpURI.Length - 1);
			}
			catch
			{
				this.FakePortalsHttpURI = null;
			}
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
				this.RefreshUrls = this._json["RefreshUrls"]?.As<Settings.RefreshUrls>();
				this.RedirectUrls = this._json["RedirectUrls"]?.As<Settings.RedirectUrls>();
				this.EmailSettings = this._json["EmailSettings"]?.As<Settings.Email>();
				this.WebHookSettings = this._json["WebHookSettings"]?.As<Settings.WebHookSetting>();
				this.HttpIndicators = this._json["HttpIndicators"]?.As<List<Settings.HttpIndicator>>();
				this.FakeFilesHttpURI = this._json["FakeFilesHttpURI"]?.As<string>();
				this.FakePortalsHttpURI = this._json["FakePortalsHttpURI"]?.As<string>();
				this.PrepareRedirectAddresses();
			}
			else if (OrganizationProcessor.ExtraProperties.Contains(name))
			{
				this._json = this._json ?? JObject.Parse(string.IsNullOrWhiteSpace(this.Extras) ? "{}" : this.Extras);
				this._json[name] = this.GetProperty(name)?.ToJson();
				if (name.IsEquals("RedirectUrls"))
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

		List<Tuple<string, string, string>> _redirectAddresses;

		void PrepareRedirectAddresses()
			=> this._redirectAddresses = this.RedirectUrls?.Addresses?.Select(address =>
			{
				var addresses = address.ToArray('|');
				return addresses.Length > 1 ? new Tuple<string, string, string>(addresses[0], addresses[1], addresses.Length > 2 ? Int32.TryParse(addresses[2], out var code) ? code.ToString() : "302" : "302") : null;
			}).Where(addresses => addresses != null).ToList();

		internal string GetRedirectURL(string requestedURL, out int redirectCode)
		{
			redirectCode = 302;
			if (!string.IsNullOrWhiteSpace(requestedURL))
			{
				var url = this._redirectAddresses?.FirstOrDefault(addresses => requestedURL.IsStartsWith(addresses.Item1))?.Item2;
				if (url != null)
					redirectCode = this._redirectAddresses.FirstOrDefault(addresses => requestedURL.IsStartsWith(addresses.Item1)).Item3.As<int>();
				else
				{
					var regexAddresses = this._redirectAddresses?.Where(addresses => addresses.Item1.IsStartsWith("@regex")).ToList() ?? new List<Tuple<string, string, string>>();
					var regexIndex = 0;
					while (regexIndex < regexAddresses.Count)
					{
						var addresses = regexAddresses[regexIndex];
						var regex = addresses.Item1.IsStartsWith("@regex(") && addresses.Item1.IsEndsWith(")")
							? addresses.Item1.Left(addresses.Item1.Length - 1).Replace(StringComparison.OrdinalIgnoreCase, "@regex(", "")
							: addresses.Item1.Replace(StringComparison.OrdinalIgnoreCase, "@regex:", "");
						var match = new Regex(regex, RegexOptions.IgnoreCase).Match(requestedURL);
						if (match.Success)
						{
							var isRegEx = addresses.Item2.IsStartsWith("@regex");
							var redirectURL = isRegEx
								? addresses.Item2.IsStartsWith("@regex(") && addresses.Item2.IsEndsWith(")")
									? addresses.Item2.Left(addresses.Item2.Length - 1).Replace(StringComparison.OrdinalIgnoreCase, "@regex(", "")
									: addresses.Item2.Replace(StringComparison.OrdinalIgnoreCase, "@regex:", "")
								:addresses.Item2;
							if (isRegEx)
							{
								var matchIndex = 1;
								while (matchIndex < match.Groups.Count)
								{
									redirectURL = redirectURL.Replace($"${matchIndex}", match.Groups[matchIndex].Value);
									matchIndex++;
								}
							}
							redirectCode = addresses.Item3.As<int>();
							return redirectURL;
						}
						regexIndex++;
					}
				}
			}
			return null;
		}

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore, MessagePackIgnore]
		public bool IsHasJavascriptLibraries => (this.Socials != null && this.Socials.Any()) || (this.Trackings != null && this.Trackings.Any()) || !string.IsNullOrWhiteSpace(this.ScriptLibraries);

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore, MessagePackIgnore]
		public string JavascriptLibraries
		{
			get
			{
				var scripts = "";
				if (this.Socials != null && this.Socials.Any())
				{
					if (this.Socials.IndexOf("Facebook") > -1)
						scripts += $"<script src=\"https://connect.facebook.net/en_US/sdk.js\" async defer></script>";
					if (this.Socials.IndexOf("Twitter") > -1)
						scripts += "<script src=\"https://platform.twitter.com/widgets.js\" async defer></script>";
				}
				if (this.Trackings != null && this.Trackings.Any())
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
		public bool IsHasJavascripts => (this.Trackings != null && this.Trackings.Any()) || !string.IsNullOrWhiteSpace(this.Scripts);

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore, MessagePackIgnore]
		public string Javascripts
		{
			get
			{
				var scripts = "";
				if (this.Trackings != null && this.Trackings.Any())
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

	}
}
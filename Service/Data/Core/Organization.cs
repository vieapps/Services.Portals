#region Related components
using System;
using System.Linq;
using System.Diagnostics;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Serialization;
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
		public string Theme { get; set; } = "default";

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

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
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
			return this._moduleIDs?.Select(id => id.GetModuleByID()).ToList();
		}

		internal async Task<List<Module>> FindModulesAsync(CancellationToken cancellationToken = default, bool notifyPropertyChanged = true)
			=> this._moduleIDs == null
				? this.FindModules(await (this.ID ?? "").FindModulesAsync(null, cancellationToken).ConfigureAwait(false), notifyPropertyChanged)
				: this._moduleIDs.Select(id => id.GetModuleByID()).ToList();

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore, MessagePackIgnore]
		public List<Module> Modules => this.FindModules();

		public override JObject ToJson(bool addTypeOfExtendedProperties, Action<JObject> onPreCompleted = null)
			=> this.ToJson(false, addTypeOfExtendedProperties, onPreCompleted);

		public JObject ToJson(bool addModules, bool addTypeOfExtendedProperties, Action<JObject> onPreCompleted = null)
			=> base.ToJson(addTypeOfExtendedProperties, json =>
			{
				json.Remove("OriginalPrivileges");
				if (addModules)
					json["Modules"] = this.Modules.ToJArray(module => module?.ToJson(true, addTypeOfExtendedProperties));
				onPreCompleted?.Invoke(json);
			});

		internal void NormalizeExtras()
		{
			this.Notifications?.Normalize();
			this.Notifications = this.Notifications != null && this.Notifications.Events == null && this.Notifications.Methods == null && this.Notifications.Emails == null && this.Notifications.EmailsByApprovalStatus == null && this.Notifications.EmailsWhenPublish == null && this.Notifications.WebHooks == null ? null : this.Notifications;
			this.Instructions = this.Instructions ?? new Dictionary<string, Dictionary<string, Settings.Instruction>>();
			this.Instructions.Keys.ToList().ForEach(key =>
			{
				var instructions = this.Instructions[key];
				instructions.Values.ForEach(instruction => instruction.Normalize());
				instructions.Keys.ToList().ForEach(ikey => instructions[ikey] = string.IsNullOrWhiteSpace(instructions[ikey].Subject) && string.IsNullOrWhiteSpace(instructions[ikey].Body) ? null : instructions[ikey]);
				instructions = instructions.Where(kvp => kvp.Value != null).ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
				this.Instructions[key] = instructions.Count < 1 ? null : instructions;
			});
			this.Instructions = this.Instructions.Where(kvp => kvp.Value != null).ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
			this.Instructions = this.Instructions.Count < 1 ? null : this.Instructions;
			this.Socials = this.Socials != null && this.Socials.Count < 1 ? null : this.Socials;
			this.Trackings = (this.Trackings ?? new Dictionary<string, string>()).Where(kvp => !string.IsNullOrWhiteSpace(kvp.Value)).ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
			this.Trackings = this.Trackings.Count < 1 ? null : this.Trackings;
			this.MetaTags = string.IsNullOrWhiteSpace(this.MetaTags) ? null : this.MetaTags.Trim();
			this.ScriptLibraries = string.IsNullOrWhiteSpace(this.ScriptLibraries) ? null : this.ScriptLibraries.Trim();
			this.Scripts = string.IsNullOrWhiteSpace(this.Scripts) ? null : this.Scripts.Trim();
			this.RefreshUrls?.Normalize();
			this.RefreshUrls = this.RefreshUrls != null && this.RefreshUrls.Addresses == null ? null : this.RefreshUrls;
			this.RedirectUrls?.Normalize();
			this.RedirectUrls = this.RedirectUrls != null && this.RedirectUrls.Addresses == null && !this.RedirectUrls.AllHttp404 ? null : this.RedirectUrls;
			this.EmailSettings?.Normalize();
			this.EmailSettings = this.EmailSettings != null && this.EmailSettings.Sender == null && this.EmailSettings.Signature == null && this.EmailSettings.Smtp == null ? null : this.EmailSettings;
			this.HttpIndicators?.ForEach(indicator => indicator?.Normalize());
			this.HttpIndicators = this.HttpIndicators?.Where(indicator => indicator != null && !string.IsNullOrWhiteSpace(indicator.Name) && !string.IsNullOrWhiteSpace(indicator.Content)).ToList();
			this.HttpIndicators = this.HttpIndicators == null || this.HttpIndicators.Count < 1 ? null : this.HttpIndicators;
			this.FakeFilesHttpURI = string.IsNullOrWhiteSpace(this.FakeFilesHttpURI) ? null : this.FakeFilesHttpURI.Trim();
			this.FakePortalsHttpURI = string.IsNullOrWhiteSpace(this.FakePortalsHttpURI) ? null : this.FakePortalsHttpURI.Trim();
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
				this.Notifications = this._json["Notifications"]?.FromJson<Settings.Notifications>();
				this.Instructions = Settings.Instruction.Parse(this._json["Instructions"]?.ToExpandoObject());
				this.Socials = this._json["Socials"]?.FromJson<List<string>>();
				this.Trackings = this._json["Trackings"]?.FromJson<Dictionary<string, string>>();
				this.MetaTags = this._json["MetaTags"]?.FromJson<string>();
				this.ScriptLibraries = this._json["ScriptLibraries"]?.FromJson<string>();
				this.Scripts = this._json["Scripts"]?.FromJson<string>();
				this.AlwaysUseHtmlSuffix = this._json["AlwaysUseHtmlSuffix"] != null && this._json["AlwaysUseHtmlSuffix"].FromJson<bool>();
				this.RefreshUrls = this._json["RefreshUrls"]?.FromJson<Settings.RefreshUrls>();
				this.RedirectUrls = this._json["RedirectUrls"]?.FromJson<Settings.RedirectUrls>();
				this.EmailSettings = this._json["EmailSettings"]?.FromJson<Settings.Email>();
				this.HttpIndicators = this._json["HttpIndicators"]?.FromJson<List<Settings.HttpIndicator>>();
				this.FakeFilesHttpURI = this._json["FakeFilesHttpURI"]?.FromJson<string>();
				this.FakePortalsHttpURI = this._json["FakePortalsHttpURI"]?.FromJson<string>();
				this.PrepareRedirectAddresses();
			}
			else if (OrganizationProcessor.ExtraProperties.Contains(name))
			{
				this._json = this._json ?? JObject.Parse(string.IsNullOrWhiteSpace(this.Extras) ? "{}" : this.Extras);
				this._json[name] = this.GetProperty(name)?.ToJson();
				if (name.IsEquals("RedirectUrls"))
					this.PrepareRedirectAddresses();
			}
			else if ((name.IsEquals("Modules") || name.IsEquals("Sites")) && !string.IsNullOrWhiteSpace(this.ID) && !string.IsNullOrWhiteSpace(this.Title))
				Task.WhenAll(
					this.SetAsync(false, true),
					Utility.RTUService.SendInterCommunicateMessageAsync(new CommunicateMessage(ServiceBase.ServiceComponent.ServiceName)
					{
						Type = $"{this.GetObjectName()}#Update",
						Data = this.ToJson(false, false),
						ExcludedNodeID = Utility.NodeID
					})
				).Run();
		}

		List<Tuple<string, string>> _redirectAddresses;

		void PrepareRedirectAddresses()
			=> this._redirectAddresses = this.RedirectUrls?.Addresses?.Select(address =>
			{
				var addresses = address.ToArray('|');
				return addresses.Length > 1 ? new Tuple<string, string>(addresses[0], addresses[1]) : null;
			}).Where(addresses => addresses != null).ToList();

		internal string GetRedirectURL(string requestedURL)
			=> string.IsNullOrWhiteSpace(requestedURL) ? null : this._redirectAddresses?.FirstOrDefault(addresses => requestedURL.IsStartsWith(addresses.Item1))?.Item2;
	}
}
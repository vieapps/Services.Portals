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
using net.vieapps.Components.Utility;
using net.vieapps.Components.Repository;
using net.vieapps.Components.Security;
#endregion

namespace net.vieapps.Services.Portals
{
	[Serializable, BsonIgnoreExtraElements]
	[DebuggerDisplay("ID = {ID}, Title = {Title}")]
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

		[NonSerialized]
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

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		IPortalObject IPortalObject.Parent => null;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public override Privileges WorkingPrivileges => this.OriginalPrivileges ?? new Privileges(true);

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public List<Site> Sites => SiteProcessor.Sites.Values.Where(site => site.SystemID.IsEquals(this.ID)).OrderBy(site => site.PrimaryDomain).ThenBy(site => site.SubDomain).ToList();

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public Site DefaultSite => this.Sites.FirstOrDefault();

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public Desktop DefaultDesktop => DesktopProcessor.Desktops.Values.Where(desktop => desktop.SystemID.IsEquals(this.ID)).FirstOrDefault();

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public Desktop HomeDesktop => (this.HomeDesktopID ?? "").GetDesktopByID() ?? this.DefaultDesktop;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public Desktop SearchDesktop => (this.SearchDesktopID ?? "").GetDesktopByID() ?? this.DefaultDesktop;

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
		public string Scripts { get; set; }

		[Ignore, BsonIgnore, XmlIgnore]
		public bool AlwaysUseHtmlSuffix { get; set; } = true;

		[Ignore, BsonIgnore, XmlIgnore]
		public Settings.RefreshUrls RefreshUrls { get; set; } = new Settings.RefreshUrls();

		[Ignore, BsonIgnore, XmlIgnore]
		public Settings.RedirectUrls RedirectUrls { get; set; } = new Settings.RedirectUrls();

		[Ignore, BsonIgnore, XmlIgnore]
		public Settings.Email EmailSettings { get; set; } = new Settings.Email();

		internal List<string> _moduleIDs;

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

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
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
			this.Notifications = this.Notifications != null && this.Notifications.Events == null && this.Notifications.Methods == null && this.Notifications.Emails == null && this.Notifications.WebHooks == null ? null : this.Notifications;
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
			this.Scripts = string.IsNullOrWhiteSpace(this.Scripts) ? null : this.Scripts.Trim();
			this.RefreshUrls?.Normalize();
			this.RefreshUrls = this.RefreshUrls != null && this.RefreshUrls.Addresses == null ? null : this.RefreshUrls;
			this.RedirectUrls?.Normalize();
			this.RedirectUrls = this.RedirectUrls != null && this.RedirectUrls.Addresses == null ? null : this.RedirectUrls;
			this.EmailSettings?.Normalize();
			this.EmailSettings = this.EmailSettings != null && this.EmailSettings.Sender == null && this.EmailSettings.Signature == null && this.EmailSettings.Smtp == null ? null : this.EmailSettings;
			this._json = this._json ?? JObject.Parse(string.IsNullOrWhiteSpace(this.Extras) ? "{}" : this.Extras);
			OrganizationProcessor.ExtraProperties.ForEach(name => this._json[name] = this.GetProperty(name)?.ToJson());
			this._extras = this._json.ToString(Formatting.None);
		}

		public override void ProcessPropertyChanged(string name)
		{
			if (name.IsEquals("Extras"))
			{
				this._json = this._json ?? JObject.Parse(string.IsNullOrWhiteSpace(this.Extras) ? "{}" : this.Extras);
				this.Notifications = this._json["Notifications"]?.FromJson<Settings.Notifications>();
				this.Instructions = this._json["Instructions"]?.ToExpandoObject().GetOrganizationInstructions();
				this.Socials = this._json["Socials"]?.FromJson<List<string>>();
				this.Trackings = this._json["Trackings"]?.FromJson<Dictionary<string, string>>();
				this.MetaTags = this._json["MetaTags"]?.FromJson<string>();
				this.Scripts = this._json["Scripts"]?.FromJson<string>();
				this.AlwaysUseHtmlSuffix = this._json["AlwaysUseHtmlSuffix"] != null && this._json["AlwaysUseHtmlSuffix"].FromJson<bool>();
				this.RefreshUrls = this._json["RefreshUrls"]?.FromJson<Settings.RefreshUrls>();
				this.RedirectUrls = this._json["RedirectUrls"]?.FromJson<Settings.RedirectUrls>();
				this.EmailSettings = this._json["EmailSettings"]?.FromJson<Settings.Email>();
			}
			else if (OrganizationProcessor.ExtraProperties.Contains(name))
			{
				this._json = this._json ?? JObject.Parse(string.IsNullOrWhiteSpace(this.Extras) ? "{}" : this.Extras);
				this._json[name] = this.GetProperty(name)?.ToJson();
			}
			else if (name.IsEquals("Modules"))
				this.Set(true);
		}
	}
}
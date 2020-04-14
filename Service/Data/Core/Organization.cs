#region Related components
using System;
using System.Linq;
using System.Diagnostics;
using System.Collections.Generic;
using System.ComponentModel;
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
	[Serializable, BsonIgnoreExtraElements, DebuggerDisplay("ID = {ID}, Title = {Title}")]
	[Entity(CollectionName = "Organizations", TableName = "T_Portals_Organizations", CacheClass = typeof(Utility), CacheName = "Cache", Searchable = true)]
	public sealed class Organization : Repository<Organization>, IPortalObject
	{
		public Organization() : base()
			=> this.OriginalPrivileges = new Privileges(true);

		[Property(MaxLength = 250, NotNull = true, NotEmpty = true), Sortable(IndexName = "Title"), Searchable, FormControl(Label = "{{portals.organizations.controls.[name].label}}", PlaceHolder = "{{portals.organizations.controls.[name].placeholder}}", Description = "{{portals.organizations.controls.[name].description}}")]
		public override string Title { get; set; } = "";

		[Property(MaxLength = 250), FormControl(Label = "{{portals.organizations.controls.[name].label}}", PlaceHolder = "{{portals.organizations.controls.[name].placeholder}}", Description = "{{portals.organizations.controls.[name].description}}")]
		public string Description { get; set; }

		[Property(MaxLength = 32), Sortable(IndexName = "Management"), FormControl(Hidden = true, Label = "{{portals.organizations.controls.[name].label}}", PlaceHolder = "{{portals.organizations.controls.[name].placeholder}}", Description = "{{portals.organizations.controls.[name].description}}")]
		public string OwnerID { get; set; }

		[JsonConverter(typeof(StringEnumConverter)), BsonRepresentation(MongoDB.Bson.BsonType.String), Sortable(IndexName = "Management"), FormControl(Label = "{{portals.organizations.controls.[name].label}}", PlaceHolder = "{{portals.organizations.controls.[name].placeholder}}", Description = "{{portals.organizations.controls.[name].description}}")]
		public ApprovalStatus Status { get; set; } = ApprovalStatus.Pending;

		[Property(MaxLength = 100, NotNull = true, NotEmpty = true), Sortable(IndexName = "Management", UniqueIndexName = "Alias"), FormControl(Label = "{{portals.organizations.controls.[name].label}}", PlaceHolder = "{{portals.organizations.controls.[name].placeholder}}", Description = "{{portals.organizations.controls.[name].description}}")]
		public string Alias { get; set; } = "";

		[Property(MaxLength = 10, NotNull = true, NotEmpty = true), Sortable(IndexName = "Management"), FormControl(Label = "{{portals.organizations.controls.[name].label}}", PlaceHolder = "{{portals.organizations.controls.[name].placeholder}}", Description = "{{portals.organizations.controls.[name].description}}")]
		public string ExpiredDate { get; set; } = "-";

		[Sortable(IndexName = "Management"), FormControl(Label = "{{portals.organizations.controls.[name].label}}", PlaceHolder = "{{portals.organizations.controls.[name].placeholder}}", Description = "{{portals.organizations.controls.[name].description}}")]
		public long FilesQuotes { get; set; } = 0;

		[Sortable(IndexName = "Management"), FormControl(Label = "{{portals.organizations.controls.[name].label}}", PlaceHolder = "{{portals.organizations.controls.[name].placeholder}}", Description = "{{portals.organizations.controls.[name].description}}")]
		public bool Required2FA { get; set; } = false;

		[Sortable(IndexName = "Management"), FormControl(Label = "{{portals.organizations.controls.[name].label}}", PlaceHolder = "{{portals.organizations.controls.[name].placeholder}}", Description = "{{portals.organizations.controls.[name].description}}")]
		public bool TrackDownloadFiles { get; set; } = false;

		[Property(MaxLength = 100), FormControl(Label = "{{portals.organizations.controls.[name].label}}", PlaceHolder = "{{portals.organizations.controls.[name].placeholder}}", Description = "{{portals.organizations.controls.[name].description}}")]
		public string Theme { get; set; } = "default";

		[Property(MaxLength = 32), FormControl(Label = "{{portals.organizations.controls.[name].label}}", PlaceHolder = "{{portals.organizations.controls.[name].placeholder}}", Description = "{{portals.organizations.controls.[name].description}}")]
		public string HomeDesktopID { get; set; }

		[Property(MaxLength = 32), FormControl(Label = "{{portals.organizations.controls.[name].label}}", PlaceHolder = "{{portals.organizations.controls.[name].placeholder}}", Description = "{{portals.organizations.controls.[name].description}}")]
		public string SearchDesktopID { get; set; }

		[NonSerialized]
		JObject _settings;

		string _otherSettings;

		[Property(IsCLOB = true), FormControl(Excluded = true), JsonIgnore, XmlIgnore]
		public string OtherSettings
		{
			get => this._otherSettings;
			set
			{
				this._otherSettings = value;
				this._settings = this._settings ?? JObject.Parse(string.IsNullOrWhiteSpace(this._otherSettings) ? "{}" : this._otherSettings);
				this.NotifyPropertyChanged();
			}
		}

		[Sortable(IndexName = "Audits"), FormControl(Hidden = true, Label = "{{portals.organizations.controls.[name].label}}", PlaceHolder = "{{portals.organizations.controls.[name].placeholder}}", Description = "{{portals.organizations.controls.[name].description}}")]
		public DateTime Created { get; set; } = DateTime.Now;

		[Sortable(IndexName = "Audits"), FormControl(Hidden = true, Label = "{{portals.organizations.controls.[name].label}}", PlaceHolder = "{{portals.organizations.controls.[name].placeholder}}", Description = "{{portals.organizations.controls.[name].description}}")]
		public string CreatedID { get; set; } = "";

		[Sortable(IndexName = "Audits"), FormControl(Hidden = true, Label = "{{portals.organizations.controls.[name].label}}", PlaceHolder = "{{portals.organizations.controls.[name].placeholder}}", Description = "{{portals.organizations.controls.[name].description}}")]
		public DateTime LastModified { get; set; } = DateTime.Now;

		[Sortable(IndexName = "Audits"), FormControl(Hidden = true, Label = "{{portals.organizations.controls.[name].label}}", PlaceHolder = "{{portals.organizations.controls.[name].placeholder}}", Description = "{{portals.organizations.controls.[name].description}}")]
		public string LastModifiedID { get; set; } = "";

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public override string SystemID { get; set; }

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public override string RepositoryID { get; set; }

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public override string EntityID { get; set; }

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public string OrganizationID => this.ID;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public new IPortalObject Parent => null;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public override Privileges WorkingPrivileges => this.OriginalPrivileges ?? new Privileges(true);

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public List<Site> Sites => Utility.Sites.Values.Where(site => site.SystemID.IsEquals(this.ID)).OrderBy(site => site.PrimaryDomain).ThenBy(site => site.SubDomain).ToList();

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public Site DefaultSite => this.Sites.FirstOrDefault();

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public Desktop DefaultDesktop => Utility.Desktops.Values.Where(desktop => desktop.SystemID.IsEquals(this.ID)).FirstOrDefault();

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public Desktop HomeDesktop => Utility.GetDesktopByID(this.HomeDesktopID) ?? this.DefaultDesktop;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public Desktop SearchDesktop => Utility.GetDesktopByID(this.SearchDesktopID) ?? this.DefaultDesktop;

		[Ignore, BsonIgnore]
		public Settings.Notifications Notifications { get; set; } = new Settings.Notifications();

		[Ignore, BsonIgnore]
		public Dictionary<string, Dictionary<string, Settings.Instruction>> Instructions { get; set; } = new Dictionary<string, Dictionary<string, Settings.Instruction>>();

		[Ignore, BsonIgnore]
		public List<string> Socials { get; set; } = new List<string>();

		[Ignore, BsonIgnore]
		public Dictionary<string, string> Trackings { get; set; } = new Dictionary<string, string>();

		[Ignore, BsonIgnore]
		public string MetaTags { get; set; }

		[Ignore, BsonIgnore]
		public string Scripts { get; set; }

		[Ignore, BsonIgnore]
		public Settings.RefreshUrls RefreshUrls { get; set; } = new Settings.RefreshUrls();

		[Ignore, BsonIgnore]
		public Settings.RedirectUrls RedirectUrls { get; set; } = new Settings.RedirectUrls();

		[Ignore, BsonIgnore]
		public Settings.Email EmailSettings { get; set; } = new Settings.Email();

		internal static List<string> SettingProperties { get; } = "Notifications,Instructions,Socials,Trackings,MetaTags,Scripts,RefreshUrls,RedirectUrls,EmailSettings".ToList();

		internal static Dictionary<string, Dictionary<string, Settings.Instruction>> GetInstructions(ExpandoObject rawInstructions)
		{
			var instructions = new Dictionary<string, Dictionary<string, Settings.Instruction>>();
			rawInstructions?.ForEach(rawInstruction =>
			{
				var instructionsByLanguage = new Dictionary<string, Settings.Instruction>();
				(rawInstruction.Value as ExpandoObject)?.ForEach(kvp =>
				{
					var instructionData = kvp.Value as ExpandoObject;
					instructionsByLanguage[kvp.Key] = new Settings.Instruction { Subject = instructionData.Get<string>("Subject"), Body = instructionData.Get<string>("Body") };
				});
				instructions[rawInstruction.Key] = instructionsByLanguage;
			});
			return instructions;
		}

		public override void ProcessPropertyChanged(string name)
		{
			if (name.IsEquals("OtherSettings"))
			{
				this._settings = this._settings ?? JObject.Parse(string.IsNullOrWhiteSpace(this.OtherSettings) ? "{}" : this.OtherSettings);
				this.Notifications = this._settings["Notifications"]?.FromJson<Settings.Notifications>() ?? new Settings.Notifications();
				this.Instructions = Organization.GetInstructions(this._settings["Instructions"]?.ToExpandoObject());
				this.Socials = this._settings["Socials"]?.FromJson<List<string>>() ?? new List<string>();
				this.Trackings = this._settings["Trackings"]?.FromJson<Dictionary<string, string>>() ?? new Dictionary<string, string>();
				this.MetaTags = this._settings["MetaTags"]?.FromJson<string>();
				this.Scripts = this._settings["Scripts"]?.FromJson<string>();
				this.RefreshUrls = this._settings["RefreshUrls"]?.FromJson<Settings.RefreshUrls>() ?? new Settings.RefreshUrls();
				this.RedirectUrls = this._settings["RedirectUrls"]?.FromJson<Settings.RedirectUrls>() ?? new Settings.RedirectUrls();
				this.EmailSettings = this._settings["EmailSettings"]?.FromJson<Settings.Email>() ?? new Settings.Email();
			}
			else if (Organization.SettingProperties.ToHashSet().Contains(name))
			{
				this._settings = this._settings ?? JObject.Parse(string.IsNullOrWhiteSpace(this.OtherSettings) ? "{}" : this.OtherSettings);
				this._settings[name] = this.GetProperty(name)?.ToJson();
			}
		}

		internal void NormalizeSettings()
		{
			this.Notifications.Emails.Normalize();
			this.Notifications.WebHooks.Normalize();
			this.RefreshUrls.Normalize();
			this.RefreshUrls.Normalize();
			this.EmailSettings.Normalize();
			this._settings = this._settings ?? new JObject();
			Organization.SettingProperties.ForEach(name => this._settings[name] = this.GetProperty(name)?.ToJson());
			this._otherSettings = this._settings.ToString(Formatting.None);
		}

	}
}
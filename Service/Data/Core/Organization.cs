#region Related components
using System;
using System.Linq;
using System.Diagnostics;
using System.Collections.Generic;
using System.Collections.Concurrent;
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

		[Property(MaxLength = 250, NotNull = true, NotEmpty = true)]
		[Sortable(IndexName = "Title"), Searchable]
		[FormControl(Label = "{{portals.organizations.controls.[name].label}}", PlaceHolder = "{{portals.organizations.controls.[name].placeholder}}", Description = "{{portals.organizations.controls.[name].description}}")]
		public override string Title { get; set; } = "";

		[Property(MaxLength = 250)]
		[FormControl(Label = "{{portals.organizations.controls.[name].label}}", PlaceHolder = "{{portals.organizations.controls.[name].placeholder}}", Description = "{{portals.organizations.controls.[name].description}}")]
		public string Description { get; set; }

		[Property(MaxLength = 32)]
		[Sortable(IndexName = "Management")]
		[FormControl(ControlType = "Lookup", Label = "{{portals.organizations.controls.[name].label}}", PlaceHolder = "{{portals.organizations.controls.[name].placeholder}}", Description = "{{portals.organizations.controls.[name].description}}")]
		public string OwnerID { get; set; }

		[JsonConverter(typeof(StringEnumConverter)), BsonRepresentation(MongoDB.Bson.BsonType.String)]
		[Sortable(IndexName = "Management")]
		[FormControl(Label = "{{portals.organizations.controls.[name].label}}", PlaceHolder = "{{portals.organizations.controls.[name].placeholder}}", Description = "{{portals.organizations.controls.[name].description}}")]
		public ApprovalStatus Status { get; set; } = ApprovalStatus.Pending;

		[Property(MaxLength = 100, NotNull = true, NotEmpty = true)]
		[Sortable(IndexName = "Management", UniqueIndexName = "Alias")]
		[FormControl(Label = "{{portals.organizations.controls.[name].label}}", PlaceHolder = "{{portals.organizations.controls.[name].placeholder}}", Description = "{{portals.organizations.controls.[name].description}}")]
		public string Alias { get; set; } = "";

		[Property(MaxLength = 10, NotNull = true, NotEmpty = true)]
		[Sortable(IndexName = "Management")]
		[FormControl(Label = "{{portals.organizations.controls.[name].label}}", PlaceHolder = "{{portals.organizations.controls.[name].placeholder}}", Description = "{{portals.organizations.controls.[name].description}}")]
		public string ExpiredDate { get; set; } = "-";

		[Sortable(IndexName = "Management")]
		[FormControl(Label = "{{portals.organizations.controls.[name].label}}", PlaceHolder = "{{portals.organizations.controls.[name].placeholder}}", Description = "{{portals.organizations.controls.[name].description}}")]
		public long FilesQuotes { get; set; } = 0;

		[Sortable(IndexName = "Management")]
		[FormControl(Label = "{{portals.organizations.controls.[name].label}}", PlaceHolder = "{{portals.organizations.controls.[name].placeholder}}", Description = "{{portals.organizations.controls.[name].description}}")]
		public bool Required2FA { get; set; } = false;

		[Sortable(IndexName = "Management")]
		[FormControl(Label = "{{portals.organizations.controls.[name].label}}", PlaceHolder = "{{portals.organizations.controls.[name].placeholder}}", Description = "{{portals.organizations.controls.[name].description}}")]
		public bool TrackDownloadFiles { get; set; } = false;

		[Property(MaxLength = 100)]
		[FormControl(Label = "{{portals.organizations.controls.[name].label}}", PlaceHolder = "{{portals.organizations.controls.[name].placeholder}}", Description = "{{portals.organizations.controls.[name].description}}")]
		public string Theme { get; set; } = "default";

		[Property(MaxLength = 32)]
		[FormControl(Label = "{{portals.organizations.controls.[name].label}}", PlaceHolder = "{{portals.organizations.controls.[name].placeholder}}", Description = "{{portals.organizations.controls.[name].description}}")]
		public string HomeDesktopID { get; set; }

		[Property(MaxLength = 32)]
		[FormControl(Label = "{{portals.organizations.controls.[name].label}}", PlaceHolder = "{{portals.organizations.controls.[name].placeholder}}", Description = "{{portals.organizations.controls.[name].description}}")]
		public string SearchDesktopID { get; set; }

		[NonSerialized]
		JObject _settings;

		string _otherSettings;

		[Property(IsCLOB = true)]
		[JsonIgnore, XmlIgnore]
		[FormControl(Excluded = true)]
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

		[Sortable(IndexName = "Audits")]
		[FormControl(Hidden = true)]
		public DateTime Created { get; set; } = DateTime.Now;

		[Sortable(IndexName = "Audits")]
		[FormControl(Hidden = true)]
		public string CreatedID { get; set; } = "";

		[Sortable(IndexName = "Audits")]
		[FormControl(Hidden = true)]
		public DateTime LastModified { get; set; } = DateTime.Now;

		[Sortable(IndexName = "Audits")]
		[FormControl(Hidden = true)]
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
		public List<Site> Sites => SiteExtensions.Sites.Values.Where(site => site.SystemID.IsEquals(this.ID)).OrderBy(site => site.PrimaryDomain).ThenBy(site => site.SubDomain).ToList();

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public Site DefaultSite => this.Sites.FirstOrDefault();

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public Desktop DefaultDesktop => DesktopExtensions.Desktops.Values.Where(desktop => desktop.SystemID.IsEquals(this.ID)).FirstOrDefault();

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public Desktop HomeDesktop => (this.HomeDesktopID ?? "").GetDesktopByID() ?? this.DefaultDesktop;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public Desktop SearchDesktop => (this.SearchDesktopID ?? "").GetDesktopByID() ?? this.DefaultDesktop;

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

		internal void NormalizeSettings()
		{
			this.Notifications.Emails.Normalize();
			this.Notifications.WebHooks.Normalize();
			this.RefreshUrls.Normalize();
			this.RefreshUrls.Normalize();
			this.EmailSettings.Normalize();
			this._settings = this._settings ?? JObject.Parse(string.IsNullOrWhiteSpace(this.OtherSettings) ? "{}" : this.OtherSettings);
			OrganizationExtensions.SettingProperties.ForEach(name => this._settings[name] = this.GetProperty(name)?.ToJson());
			this._otherSettings = this._settings.ToString(Formatting.None);
		}

		public override void ProcessPropertyChanged(string name)
		{
			if (name.IsEquals("OtherSettings"))
			{
				this._settings = this._settings ?? JObject.Parse(string.IsNullOrWhiteSpace(this.OtherSettings) ? "{}" : this.OtherSettings);
				this.Notifications = this._settings["Notifications"]?.FromJson<Settings.Notifications>() ?? new Settings.Notifications();
				this.Instructions = this._settings["Instructions"]?.ToExpandoObject().GetInstructions();
				this.Socials = this._settings["Socials"]?.FromJson<List<string>>() ?? new List<string>();
				this.Trackings = this._settings["Trackings"]?.FromJson<Dictionary<string, string>>() ?? new Dictionary<string, string>();
				this.MetaTags = this._settings["MetaTags"]?.FromJson<string>();
				this.Scripts = this._settings["Scripts"]?.FromJson<string>();
				this.RefreshUrls = this._settings["RefreshUrls"]?.FromJson<Settings.RefreshUrls>() ?? new Settings.RefreshUrls();
				this.RedirectUrls = this._settings["RedirectUrls"]?.FromJson<Settings.RedirectUrls>() ?? new Settings.RedirectUrls();
				this.EmailSettings = this._settings["EmailSettings"]?.FromJson<Settings.Email>() ?? new Settings.Email();
			}
			else if (OrganizationExtensions.SettingProperties.ToHashSet().Contains(name))
			{
				this._settings = this._settings ?? JObject.Parse(string.IsNullOrWhiteSpace(this.OtherSettings) ? "{}" : this.OtherSettings);
				this._settings[name] = this.GetProperty(name)?.ToJson();
			}
		}
	}

	internal static class OrganizationExtensions
	{
		internal static ConcurrentDictionary<string, Organization> Organizations { get; } = new ConcurrentDictionary<string, Organization>(StringComparer.OrdinalIgnoreCase);

		internal static ConcurrentDictionary<string, Organization> OrganizationsByAlias { get; } = new ConcurrentDictionary<string, Organization>(StringComparer.OrdinalIgnoreCase);

		internal static List<string> SettingProperties { get; } = "Notifications,Instructions,Socials,Trackings,MetaTags,Scripts,RefreshUrls,RedirectUrls,EmailSettings".ToList();

		internal static Dictionary<string, Dictionary<string, Settings.Instruction>> GetInstructions(this ExpandoObject rawInstructions)
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

		internal static Organization CreateOrganizationInstance(this ExpandoObject requestBody, string excluded = null, Action<Organization> onCompleted = null)
			=> requestBody.Copy<Organization>(excluded?.ToHashSet(), organization =>
			{
				organization.Instructions = requestBody.Get<ExpandoObject>("Instructions").GetInstructions();
				organization.NormalizeSettings();
				onCompleted?.Invoke(organization);
			});

		internal static Organization UpdateOrganizationInstance(this Organization organization, ExpandoObject requestBody, string excluded = null, Action<Organization> onCompleted = null)
		{
			organization.CopyFrom(requestBody, excluded?.ToHashSet());
			organization.Instructions = requestBody.Get<ExpandoObject>("Instructions").GetInstructions();
			organization.NormalizeSettings();
			onCompleted?.Invoke(organization);
			return organization;
		}

		internal static Organization Set(this Organization organization, bool clear = false, bool updateCache = false)
		{
			if (organization != null)
			{
				if (clear && OrganizationExtensions.Organizations.TryGetValue(organization.ID, out var old) && old != null)
					OrganizationExtensions.OrganizationsByAlias.Remove(old.Alias);
				OrganizationExtensions.Organizations[organization.ID] = organization;
				OrganizationExtensions.OrganizationsByAlias[organization.Alias] = organization;
				if (updateCache)
					Utility.Cache.Set(organization);
			}
			return organization;
		}

		internal static Organization Remove(this Organization organization)
			=> (organization?.ID ?? "").RemoveOrganization();

		internal static Organization RemoveOrganization(this string id)
		{
			if (string.IsNullOrWhiteSpace(id))
				return null;
			if (OrganizationExtensions.Organizations.TryRemove(id, out var organization) && organization != null)
				OrganizationExtensions.OrganizationsByAlias.Remove(organization.Alias);
			return organization;
		}

		internal static async Task<Organization> SetAsync(this Organization organization, bool clear = false, bool updateCache = false, CancellationToken cancellationToken = default)
		{
			organization?.Set(clear);
			await (updateCache && organization != null ? Utility.Cache.SetAsync(organization, cancellationToken) : Task.CompletedTask).ConfigureAwait(false);
			return organization;
		}

		internal static Organization GetOrganizationByID(this string id, bool force = false, bool fetchRepository = true)
			=> !force && !string.IsNullOrWhiteSpace(id) && OrganizationExtensions.Organizations.ContainsKey(id)
				? OrganizationExtensions.Organizations[id]
				: fetchRepository && !string.IsNullOrWhiteSpace(id)
					? Organization.Get<Organization>(id)?.Set()
					: null;

		internal static async Task<Organization> GetOrganizationByIDAsync(this string id, CancellationToken cancellationToken = default, bool force = false)
		{
			var organization = id.GetOrganizationByID(force, false) ?? await Organization.GetAsync<Organization>(id, cancellationToken).ConfigureAwait(false);
			return organization != null ? await organization.SetAsync(false, false, cancellationToken).ConfigureAwait(false) : null;
		}

		internal static Organization GetOrganizationByAlias(this string alias, bool force = false, bool fetchRepository = true)
		{
			var organization = !force && !string.IsNullOrWhiteSpace(alias) && OrganizationExtensions.OrganizationsByAlias.ContainsKey(alias)
				? OrganizationExtensions.OrganizationsByAlias[alias]
				: null;

			if (organization == null && !force && !string.IsNullOrWhiteSpace(alias))
			{
				organization = OrganizationExtensions.Organizations.Values.FirstOrDefault(org => org.Alias.IsEquals(alias));
				organization?.Set();
			}

			return organization ?? (fetchRepository && !string.IsNullOrWhiteSpace(alias) ? Organization.Get<Organization>(Filters<Organization>.Equals("Alias", alias), null, null)?.Set() : null);
		}

		internal static async Task<Organization> GetOrganizationByAliasAsync(this string alias, CancellationToken cancellationToken = default, bool force = false)
		{
			var organization = string.IsNullOrWhiteSpace(alias)
				? null
				: alias.GetOrganizationByAlias(force, false) ?? await Organization.GetAsync<Organization>(Filters<Organization>.Equals("Alias", alias), null, null, cancellationToken).ConfigureAwait(false);
			return organization != null ? await organization.SetAsync(false, false, cancellationToken).ConfigureAwait(false) : null;
		}
	}
}
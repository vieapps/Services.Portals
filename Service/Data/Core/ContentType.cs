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
using net.vieapps.Components.Security;
using net.vieapps.Components.Repository;
#endregion

namespace net.vieapps.Services.Portals
{
	[Serializable, BsonIgnoreExtraElements]
	[DebuggerDisplay("ID = {ID}, Title = {Title}")]
	[Entity(CollectionName = "ContentTypes", TableName = "T_Portals_ContentTypes", CacheClass = typeof(Utility), CacheName = "Cache", Searchable = true)]
	public sealed class ContentType : Repository<ContentType>, IPortalContentType, IBusinessRepositoryEntity
	{
		public ContentType() : base() { }

		[Property(MaxLength = 32, NotNull = true, NotEmpty = true)]
		[Sortable(IndexName = "Management")]
		[FormControl(Segment = "basic", ControlType = "Select", Label = "{{portals.contenttypes.controls.[name].label}}", PlaceHolder = "{{portals.contenttypes.controls.[name].placeholder}}", Description = "{{portals.contenttypes.controls.[name].description}}")]
		public override string RepositoryID { get; set; }

		[Property(MaxLength = 32, NotNull = true, NotEmpty = true)]
		[Sortable(IndexName = "Management")]
		[FormControl(Segment = "basic", ControlType = "Select", Label = "{{portals.contenttypes.controls.[name].label}}", PlaceHolder = "{{portals.contenttypes.controls.[name].placeholder}}", Description = "{{portals.contenttypes.controls.[name].description}}")]
		public string ContentTypeDefinitionID { get; set; }

		[Property(MaxLength = 250, NotNull = true, NotEmpty = true)]
		[Sortable(IndexName = "Title")]
		[Searchable]
		[FormControl(Segment = "basic", Label = "{{portals.contenttypes.controls.[name].label}}", PlaceHolder = "{{portals.contenttypes.controls.[name].placeholder}}", Description = "{{portals.contenttypes.controls.[name].description}}")]
		public override string Title { get; set; }

		[Searchable]
		[FormControl(Segment = "basic", ControlType = "TextArea", Label = "{{portals.contenttypes.controls.[name].label}}", PlaceHolder = "{{portals.contenttypes.controls.[name].placeholder}}", Description = "{{portals.contenttypes.controls.[name].description}}")]
		public string Description { get; set; }

		[Property(MaxLength = 32)]
		[FormControl(Segment = "basic", ControlType = "Lookup", Label = "{{portals.contenttypes.controls.[name].label}}", PlaceHolder = "{{portals.contenttypes.controls.[name].placeholder}}", Description = "{{portals.contenttypes.controls.[name].description}}")]
		public string DesktopID { get; set; }

		[Sortable(IndexName = "Management")]
		[FormControl(Segment = "basic", Label = "{{portals.contenttypes.controls.[name].label}}", PlaceHolder = "{{portals.contenttypes.controls.[name].placeholder}}", Description = "{{portals.contenttypes.controls.[name].description}}")]
		public bool CreateNewVersionWhenUpdated { get; set; } = true;

		[Sortable(IndexName = "Management")]
		[FormControl(Segment = "basic", Label = "{{portals.contenttypes.controls.[name].label}}", PlaceHolder = "{{portals.contenttypes.controls.[name].placeholder}}", Description = "{{portals.contenttypes.controls.[name].description}}")]
		public bool AllowComments { get; set; } = false;

		[Sortable(IndexName = "Management")]
		[FormControl(Segment = "basic", Label = "{{portals.contenttypes.controls.[name].label}}", PlaceHolder = "{{portals.contenttypes.controls.[name].placeholder}}", Description = "{{portals.contenttypes.controls.[name].description}}")]
		public bool UseSocialNetworkComments { get; set; } = false;

		[JsonConverter(typeof(StringEnumConverter)), BsonRepresentation(MongoDB.Bson.BsonType.String)]
		[FormControl(Segment = "basic", Label = "{{portals.contenttypes.controls.[name].label}}", PlaceHolder = "{{portals.contenttypes.controls.[name].placeholder}}", Description = "{{portals.contenttypes.controls.[name].description}}")]
		public ApprovalStatus DefaultCommentStatus { get; set; } = ApprovalStatus.Pending;

		[Ignore, BsonIgnore, XmlIgnore]
		public Settings.Notifications Notifications { get; set; } = new Settings.Notifications();

		[Ignore, BsonIgnore, XmlIgnore]
		public Dictionary<string, string> Trackings { get; set; } = new Dictionary<string, string>();

		[Ignore, BsonIgnore, XmlIgnore]
		public Settings.Email EmailSettings { get; set; } = new Settings.Email();

		[AsJson]
		[FormControl(Excluded = true)]
		public List<ExtendedPropertyDefinition> ExtendedPropertyDefinitions { get; set; }

		[AsJson]
		[FormControl(Excluded = true)]
		public List<ExtendedControlDefinition> ExtendedControlDefinitions { get; set; }

		[AsJson]
		[FormControl(Excluded = true)]
		public List<StandardControlDefinition> StandardControlDefinitions { get; set; }

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

		[Property(MaxLength = 32, NotNull = true, NotEmpty = true)]
		[Sortable(IndexName = "Management")]
		[FormControl(Hidden = true)]
		public override string SystemID { get; set; }

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public override string RepositoryEntityID { get; set; }

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public string OrganizationID => this.SystemID;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public Organization Organization => (this.OrganizationID ?? "").GetOrganizationByID();

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		IPortalObject IPortalContentType.Organization => this.Organization;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public string ModuleID => this.RepositoryID;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public Module Module => (this.ModuleID ?? "").GetModuleByID();

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		IPortalModule IPortalContentType.Module => this.Module;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		IBusinessRepository IBusinessRepositoryEntity.BusinessRepository => this.Module;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public override RepositoryBase Parent => this.Module;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		IPortalObject IPortalObject.Parent => this.Module;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public ContentTypeDefinition ContentTypeDefinition => Utility.ContentTypeDefinitions.TryGetValue(this.ContentTypeDefinitionID, out var definition) ? definition : null;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public string EntityDefinitionTypeName => this.ContentTypeDefinition?.EntityDefinitionTypeName;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public EntityDefinition EntityDefinition => this.ContentTypeDefinition?.EntityDefinition ?? RepositoryMediator.GetEntityDefinition(this.EntityDefinitionTypeName);

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public Desktop Desktop => (this.DesktopID ?? "").GetDesktopByID() ?? this.Module?.Desktop;

		internal void NormalizeExtras()
		{
			this.Notifications?.Normalize();
			this.Notifications = this.Notifications != null && this.Notifications.Events == null && this.Notifications.Methods == null && this.Notifications.Emails == null && this.Notifications.WebHooks == null ? null : this.Notifications;
			this.Trackings = (this.Trackings ?? new Dictionary<string, string>()).Where(kvp => !string.IsNullOrWhiteSpace(kvp.Value)).ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
			this.Trackings = this.Trackings.Count < 1 ? null : this.Trackings;
			this.EmailSettings?.Normalize();
			this.EmailSettings = this.EmailSettings != null && this.EmailSettings.Sender == null && this.EmailSettings.Signature == null && this.EmailSettings.Smtp == null ? null : this.EmailSettings;
			this._json = this._json ?? JObject.Parse(string.IsNullOrWhiteSpace(this.Extras) ? "{}" : this.Extras);
			ModuleProcessor.ExtraProperties.ForEach(name => this._json[name] = this.GetProperty(name)?.ToJson());
			this._extras = this._json.ToString(Formatting.None);
		}

		public override void ProcessPropertyChanged(string name)
		{
			if (name.IsEquals("Extras"))
			{
				this._json = this._json ?? JObject.Parse(string.IsNullOrWhiteSpace(this.Extras) ? "{}" : this.Extras);
				this.Notifications = this._json["Notifications"]?.FromJson<Settings.Notifications>();
				this.Trackings = this._json["Trackings"]?.FromJson<Dictionary<string, string>>();
				this.EmailSettings = this._json["EmailSettings"]?.FromJson<Settings.Email>();
			}
			else if (ModuleProcessor.ExtraProperties.Contains(name))
			{
				this._json = this._json ?? JObject.Parse(string.IsNullOrWhiteSpace(this.Extras) ? "{}" : this.Extras);
				this._json[name] = this.GetProperty(name)?.ToJson();
			}
		}
	}
}
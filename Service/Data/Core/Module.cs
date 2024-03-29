﻿#region Related components
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
using net.vieapps.Components.Utility;
using net.vieapps.Components.Security;
using net.vieapps.Components.Repository;
#endregion

namespace net.vieapps.Services.Portals
{
	[BsonIgnoreExtraElements, DebuggerDisplay("ID = {ID}, Title = {Title}")]
	[Entity(CollectionName = "Modules", TableName = "T_Portals_Modules", CacheClass = typeof(Utility), CacheName = "Cache", Searchable = true)]
	public sealed class Module : Repository<Module>, IPortalModule, IBusinessRepository
	{
		public Module() : base() { }

		[Property(MaxLength = 32, NotNull = true, NotEmpty = true)]
		[Sortable(IndexName = "Management")]
		[FormControl(Segment = "basic", ControlType = "Select", Label = "{{portals.modules.controls.[name].label}}", PlaceHolder = "{{portals.modules.controls.[name].placeholder}}", Description = "{{portals.modules.controls.[name].description}}")]
		public string ModuleDefinitionID { get; set; }

		[Property(MaxLength = 250, NotNull = true, NotEmpty = true)]
		[Sortable(IndexName = "Title")]
		[Searchable]
		[FormControl(Segment = "basic", Label = "{{portals.modules.controls.[name].label}}", PlaceHolder = "{{portals.modules.controls.[name].placeholder}}", Description = "{{portals.modules.controls.[name].description}}")]
		public override string Title { get; set; }

		[Searchable]
		[FormControl(Segment = "basic", ControlType = "TextArea", Label = "{{portals.modules.controls.[name].label}}", PlaceHolder = "{{portals.modules.controls.[name].placeholder}}", Description = "{{portals.modules.controls.[name].description}}")]
		public string Description { get; set; }

		[Property(MaxLength = 32)]
		[FormControl(Segment = "basic", ControlType = "Lookup", Label = "{{portals.modules.controls.[name].label}}", PlaceHolder = "{{portals.modules.controls.[name].placeholder}}", Description = "{{portals.modules.controls.[name].description}}")]
		public string DesktopID { get; set; }

		[Ignore, BsonIgnore, XmlIgnore]
		public Settings.Notifications Notifications { get; set; } = new Settings.Notifications();

		[Ignore, BsonIgnore, XmlIgnore]
		public Dictionary<string, string> Trackings { get; set; } = new Dictionary<string, string>();

		[Ignore, BsonIgnore, XmlIgnore]
		public Settings.Email EmailSettings { get; set; } = new Settings.Email();

		[MessagePackIgnore]
		JObject _json;

		string _exras;

		[JsonIgnore, XmlIgnore]
		[Property(IsCLOB = true)]
		[FormControl(Excluded = true)]
		public string Extras
		{
			get => this._exras;
			set
			{
				this._exras = value;
				this._json = this._json ?? JObject.Parse(string.IsNullOrWhiteSpace(this._exras) ? "{}" : this._exras);
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
		public override string RepositoryID { get; set; }

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public override string RepositoryEntityID { get; set; }

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore, MessagePackIgnore]
		public string OrganizationID => this.SystemID;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore, MessagePackIgnore]
		public Organization Organization => (this.OrganizationID ?? "").GetOrganizationByID();

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore, MessagePackIgnore]
		IPortalObject IPortalModule.Organization => this.Organization;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore, MessagePackIgnore]
		public override RepositoryBase Parent => this.Organization;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore, MessagePackIgnore]
		IPortalObject IPortalObject.Parent => this.Organization;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore, MessagePackIgnore]
		public ModuleDefinition ModuleDefinition => Utility.ModuleDefinitions.TryGetValue(this.ModuleDefinitionID, out var moduleDefinition) ? moduleDefinition : null;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public string RepositoryDefinitionTypeName => this.ModuleDefinition?.RepositoryDefinitionTypeName;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore, MessagePackIgnore]
		public RepositoryDefinition RepositoryDefinition
		{
			get
			{
				var moduleDefinition = this.ModuleDefinition;
				return moduleDefinition?.RepositoryDefinition ?? (moduleDefinition != null && !string.IsNullOrWhiteSpace(moduleDefinition?.RepositoryDefinitionTypeName) ? RepositoryMediator.GetRepositoryDefinition(moduleDefinition?.RepositoryDefinitionTypeName) : null);
			}
		}

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore, MessagePackIgnore]
		public Desktop Desktop => (this.DesktopID ?? "").GetDesktopByID() ?? this.Organization?.DefaultDesktop;

		internal List<string> _contentTypeIDs;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public List<string> ContentTypeIDs
		{
			get => this._contentTypeIDs;
			set => this._contentTypeIDs = value;
		}

		internal List<ContentType> FindContentTypes(List<ContentType> contentTypes = null, bool notifyPropertyChanged = true)
		{
			if (this._contentTypeIDs == null || this._contentTypeIDs.Count < 1)
			{
				contentTypes = contentTypes ?? (this.SystemID ?? "").FindContentTypes(this.ID);
				this._contentTypeIDs = contentTypes.Select(contentType => contentType.ID).ToList();
				if (notifyPropertyChanged)
					this.NotifyPropertyChanged("ContentTypes");
				return contentTypes;
			}
			return this._contentTypeIDs.Select(id => id.GetContentTypeByID()).ToList();
		}

		internal async Task<List<ContentType>> FindContentTypesAsync(CancellationToken cancellationToken = default, bool notifyPropertyChanged = true)
			=> this._contentTypeIDs == null
				? this.FindContentTypes(await (this.SystemID ?? "").FindContentTypesAsync(this.ID, null, cancellationToken).ConfigureAwait(false), notifyPropertyChanged)
				: this._contentTypeIDs.Select(id => id.GetContentTypeByID()).ToList();

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore, MessagePackIgnore]
		public List<ContentType> ContentTypes => this.FindContentTypes();

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore, MessagePackIgnore]
		List<IPortalContentType> IPortalModule.ContentTypes => this.ContentTypes.Select(contentType => contentType as IPortalContentType).ToList();

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore, MessagePackIgnore]
		public List<IBusinessRepositoryEntity> BusinessRepositoryEntities => this.ContentTypes.Select(contentType => contentType as IBusinessRepositoryEntity).ToList();

		public override JObject ToJson(bool addTypeOfExtendedProperties = false, Action<JObject> onCompleted = null)
			=> this.ToJson(false, addTypeOfExtendedProperties, onCompleted);

		public JObject ToJson(bool addContentTypes, bool addTypeOfExtendedProperties, Action<JObject> onCompleted = null)
			=> base.ToJson(addTypeOfExtendedProperties, json =>
			{
				if (addContentTypes)
					json["ContentTypes"] = this.ContentTypes?.Select(contentType => contentType?.ToJson()).Where(contentType => contentType != null).ToJArray();
				onCompleted?.Invoke(json);
			});

		internal void NormalizeExtras()
		{
			this.Notifications = this.Notifications?.Normalize();
			this.Trackings = (this.Trackings ?? new Dictionary<string, string>()).Where(kvp => !string.IsNullOrWhiteSpace(kvp.Value)).ToDictionary();
			this.Trackings = this.Trackings.Any() ? this.Trackings : null;
			this.EmailSettings = this.EmailSettings?.Normalize();
			this._json = this._json ?? JObject.Parse(string.IsNullOrWhiteSpace(this.Extras) ? "{}" : this.Extras);
			ModuleProcessor.ExtraProperties.ForEach(name => this._json[name] = this.GetProperty(name)?.ToJson());
			this._exras = this._json.ToString(Formatting.None);
		}

		public override void ProcessPropertyChanged(string name)
		{
			if (name.IsEquals("Extras"))
			{
				this._json = this._json ?? JObject.Parse(string.IsNullOrWhiteSpace(this.Extras) ? "{}" : this.Extras);
				this.Notifications = this._json["Notifications"]?.As<Settings.Notifications>();
				this.Trackings = this._json["Trackings"]?.As<Dictionary<string, string>>();
				this.EmailSettings = this._json["EmailSettings"]?.As<Settings.Email>();
			}
			else if (ModuleProcessor.ExtraProperties.Contains(name))
			{
				this._json = this._json ?? JObject.Parse(string.IsNullOrWhiteSpace(this.Extras) ? "{}" : this.Extras);
				this._json[name] = this.GetProperty(name)?.ToJson();
			}
			else if (name.IsEquals("ContentTypes") && !string.IsNullOrWhiteSpace(this.ID) && !string.IsNullOrWhiteSpace(this.Title))
			{
				new CommunicateMessage(this.ServiceName)
				{
					Type = $"{this.GetObjectName()}#Update",
					Data = this.ToJson(false, false),
					ExcludedNodeID = Utility.NodeID
				}.Send();
				this.Set(true);
			}
		}
	}
}
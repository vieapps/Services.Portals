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

		[Ignore, BsonIgnore]
		public Settings.Notifications Notifications { get; set; } = new Settings.Notifications();

		[Ignore, BsonIgnore]
		public Dictionary<string, string> Trackings { get; set; } = new Dictionary<string, string>();

		[Ignore, BsonIgnore]
		public Settings.Email EmailSettings { get; set; } = new Settings.Email();

		[AsJson]
		[FormControl(Excluded = true)]
		public List<ExtendedPropertyDefinition> ExtendedPropertyDefinitions { get; set; }

		[AsJson]
		[FormControl(Excluded = true)]
		public ExtendedUIDefinition ExtendedUIDefinition { get; set; }

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
			ModuleExtensions.ExtraProperties.ForEach(name => this._json[name] = this.GetProperty(name)?.ToJson());
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
			else if (ModuleExtensions.ExtraProperties.Contains(name))
			{
				this._json = this._json ?? JObject.Parse(string.IsNullOrWhiteSpace(this.Extras) ? "{}" : this.Extras);
				this._json[name] = this.GetProperty(name)?.ToJson();
			}
		}
	}

	internal static class ContentTypeExtensions
	{
		internal static ConcurrentDictionary<string, ContentType> ContentTypes { get; } = new ConcurrentDictionary<string, ContentType>(StringComparer.OrdinalIgnoreCase);

		internal static ContentType CreateContentTypeInstance(this ExpandoObject requestBody, string excluded = null, Action<ContentType> onCompleted = null)
			=> requestBody.Copy<ContentType>(excluded?.ToHashSet(), contentType =>
			{
				contentType.OriginalPrivileges = contentType.OriginalPrivileges?.Normalize();
				contentType.TrimAll();
				onCompleted?.Invoke(contentType);
			});

		internal static ContentType UpdateContentTypeInstance(this ContentType contentType, ExpandoObject requestBody, string excluded = null, Action<ContentType> onCompleted = null)
		{
			contentType.CopyFrom(requestBody, excluded?.ToHashSet());
			contentType.OriginalPrivileges = contentType.OriginalPrivileges?.Normalize();
			contentType.TrimAll();
			onCompleted?.Invoke(contentType);
			return contentType;
		}

		internal static ContentType Set(this ContentType contentType, bool updateCache = false)
		{
			if (contentType != null)
			{
				ContentTypeExtensions.ContentTypes[contentType.ID] = contentType;
				contentType.EntityDefinition.Register(contentType);
				if (updateCache)
					Utility.Cache.Set(contentType);
			}
			return contentType;
		}

		internal static async Task<ContentType> SetAsync(this ContentType contentType, bool updateCache = false, CancellationToken cancellationToken = default)
		{
			contentType?.Set();
			await (updateCache && contentType != null ? Utility.Cache.SetAsync(contentType, cancellationToken) : Task.CompletedTask).ConfigureAwait(false);
			return contentType;
		}

		internal static ContentType Remove(this ContentType contentType)
			=> (contentType?.ID ?? "").RemoveContentType();

		internal static ContentType RemoveContentType(this string id)
		{
			if (!string.IsNullOrWhiteSpace(id) && ContentTypeExtensions.ContentTypes.TryRemove(id, out var contentType) && contentType != null)
			{
				contentType.EntityDefinition.Unregister(contentType);
				return contentType;
			}
			return null;
		}

		internal static ContentType GetContentTypeByID(this string id, bool force = false, bool fetchRepository = true)
			=> !force && !string.IsNullOrWhiteSpace(id) && ContentTypeExtensions.ContentTypes.ContainsKey(id)
				? ContentTypeExtensions.ContentTypes[id]
				: fetchRepository && !string.IsNullOrWhiteSpace(id)
					? ContentType.Get<ContentType>(id).Set()
					: null;

		internal static async Task<ContentType> GetContentTypeByIDAsync(this string id, CancellationToken cancellationToken = default, bool force = false)
			=> (id ?? "").GetContentTypeByID(force, false) ?? (await ContentType.GetAsync<ContentType>(id, cancellationToken).ConfigureAwait(false))?.Set();

		internal static IFilterBy<ContentType> GetContentTypesFilter(this string systemID, string repositoryID = null, string definitionID = null)
		{
			var filter = Filters<ContentType>.And(Filters<ContentType>.Equals("SystemID", systemID));
			if (!string.IsNullOrWhiteSpace(repositoryID))
				filter.Add(Filters<ContentType>.Equals("RepositoryID", repositoryID));
			if (!string.IsNullOrWhiteSpace(definitionID))
				filter.Add(Filters<ContentType>.Equals("DefinitionID", definitionID));
			return filter;
		}

		internal static List<ContentType> GetContentTypes(this string systemID, string repositoryID = null, string definitionID = null, bool updateCache = true)
		{
			if (string.IsNullOrWhiteSpace(systemID))
				return new List<ContentType>();
			var filter = systemID.GetContentTypesFilter(repositoryID, definitionID);
			var sort = Sorts<ContentType>.Ascending("Title");
			var contentTypes = ContentType.Find(filter, sort, 0, 1, Extensions.GetCacheKey(filter, sort, 0, 1));
			contentTypes.ForEach(contentType => contentType.Set(updateCache));
			return contentTypes;
		}

		internal static async Task<List<ContentType>> GetContentTypesAsync(this string systemID, string repositoryID = null, string definitionID = null, CancellationToken cancellationToken = default, bool updateCache = true)
		{
			if (string.IsNullOrWhiteSpace(systemID))
				return new List<ContentType>();
			var filter = systemID.GetContentTypesFilter(repositoryID, definitionID);
			var sort = Sorts<ContentType>.Ascending("Title");
			var contentTypes = await ContentType.FindAsync(filter, sort, 0, 1, Extensions.GetCacheKey(filter, sort, 0, 1), cancellationToken).ConfigureAwait(false);
			await contentTypes.ForEachAsync((contentType, token) => contentType.SetAsync(updateCache, token), cancellationToken).ConfigureAwait(false);
			return contentTypes;
		}
	}
}
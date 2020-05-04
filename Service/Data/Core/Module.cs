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

		[Ignore, BsonIgnore]
		public Settings.Notifications Notifications { get; set; } = new Settings.Notifications();

		[Ignore, BsonIgnore]
		public Dictionary<string, string> Trackings { get; set; } = new Dictionary<string, string>();

		[Ignore, BsonIgnore]
		public Settings.Email EmailSettings { get; set; } = new Settings.Email();

		[NonSerialized]
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

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public string OrganizationID => this.SystemID;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public Organization Organization => (this.OrganizationID ?? "").GetOrganizationByID();

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		IPortalObject IPortalModule.Organization => this.Organization;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public override RepositoryBase Parent => this.Organization;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		IPortalObject IPortalObject.Parent => this.Organization;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public ModuleDefinition ModuleDefinition => Utility.ModuleDefinitions.TryGetValue(this.ModuleDefinitionID, out var definition) ? definition : null;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public string RepositoryDefinitionTypeName => this.ModuleDefinition?.RepositoryDefinitionTypeName;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public RepositoryDefinition RepositoryDefinition => this.ModuleDefinition?.RepositoryDefinition ?? RepositoryMediator.GetRepositoryDefinition(this.RepositoryDefinitionTypeName);

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public Desktop Desktop => (this.DesktopID ?? "").GetDesktopByID() ?? this.Organization?.HomeDesktop;

		internal List<string> _contentTypeIDs;

		internal List<ContentType> GetContentsType(List<ContentType> contentTypes = null, bool notifyPropertyChanged = true)
		{
			if (this._contentTypeIDs == null)
			{
				contentTypes = contentTypes ?? this.SystemID.GetContentTypes(this.ID);
				this._contentTypeIDs = contentTypes.Select(contentType => contentType.ID).ToList();
				if (notifyPropertyChanged)
					this.NotifyPropertyChanged("ContentTypes");
				return contentTypes;
			}
			return this._contentTypeIDs.Select(id => id.GetContentTypeByID()).ToList();
		}

		internal async Task<List<ContentType>> GetContentTypesAsync(CancellationToken cancellationToken = default, bool notifyPropertyChanged = true)
			=> this._contentTypeIDs == null
				? this.GetContentsType(await this.SystemID.GetContentTypesAsync(this.ID, null, cancellationToken).ConfigureAwait(false), notifyPropertyChanged)
				: this._contentTypeIDs.Select(id => id.GetContentTypeByID()).ToList();

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public List<ContentType> ContentTypes => this.GetContentsType();

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		List<IPortalContentType> IPortalModule.ContentTypes => this.ContentTypes.Select(contentType => contentType as IPortalContentType).ToList();

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public List<IBusinessRepositoryEntity> BusinessRepositoryEntities => this.ContentTypes.Select(contentType => contentType as IBusinessRepositoryEntity).ToList();

		public override JObject ToJson(bool addTypeOfExtendedProperties = false, Action<JObject> onPreCompleted = null)
			=> this.ToJson(false, addTypeOfExtendedProperties, onPreCompleted);

		public JObject ToJson(bool addContentTypes, bool addTypeOfExtendedProperties, Action<JObject> onPreCompleted = null)
			=> base.ToJson(addTypeOfExtendedProperties, json =>
			{
				if (addContentTypes)
					json["ContentTypes"] = this.ContentTypes?.Select(contentType => contentType?.ToJson()).Where(contentType => contentType != null).ToJArray();
				onPreCompleted?.Invoke(json);
			});

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
			this._exras = this._json.ToString(Formatting.None);
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
			else if (name.IsEquals("ContentTypes"))
				this.Set(true);
		}
	}

	internal static class ModuleExtensions
	{
		internal static ConcurrentDictionary<string, Module> Modules { get; } = new ConcurrentDictionary<string, Module>(StringComparer.OrdinalIgnoreCase);

		public static HashSet<string> ExtraProperties { get; } = "Notifications,Trackings,EmailSettings".ToHashSet();

		internal static Module CreateModuleInstance(this ExpandoObject requestBody, string excluded = null, Action<Module> onCompleted = null)
			=> requestBody.Copy<Module>(excluded?.ToHashSet(), module =>
			{
				module.OriginalPrivileges = module.OriginalPrivileges?.Normalize();
				module.TrimAll();
				onCompleted?.Invoke(module);
			});

		internal static Module UpdateModuleInstance(this Module module, ExpandoObject requestBody, string excluded = null, Action<Module> onCompleted = null)
		{
			module.CopyFrom(requestBody, excluded?.ToHashSet());
			module.OriginalPrivileges = module.OriginalPrivileges?.Normalize();
			module.TrimAll();
			onCompleted?.Invoke(module);
			return module;
		}

		internal static Module Set(this Module module, bool updateCache = false)
		{
			if (module != null)
			{
				ModuleExtensions.Modules[module.ID] = module;
				module.RepositoryDefinition.Register(module);
				if (updateCache)
					Utility.Cache.Set(module);
			}
			return module;
		}

		internal static async Task<Module> SetAsync(this Module module, bool updateCache = false, CancellationToken cancellationToken = default)
		{
			module?.Set();
			await (updateCache && module != null ? Utility.Cache.SetAsync(module, cancellationToken) : Task.CompletedTask).ConfigureAwait(false);
			return module;
		}

		internal static Module Remove(this Module module)
			=> (module?.ID ?? "").RemoveModule();

		internal static Module RemoveModule(this string id)
		{
			if (!string.IsNullOrWhiteSpace(id) && ModuleExtensions.Modules.TryRemove(id, out var module) && module != null)
			{
				module.RepositoryDefinition.Unregister(module);
				return module;
			}
			return null;
		}

		internal static Module GetModuleByID(this string id, bool force = false, bool fetchRepository = true)
			=> !force && !string.IsNullOrWhiteSpace(id) && ModuleExtensions.Modules.ContainsKey(id)
				? ModuleExtensions.Modules[id]
				: fetchRepository && !string.IsNullOrWhiteSpace(id)
					? Module.Get<Module>(id)?.Set()
					: null;

		internal static async Task<Module> GetModuleByIDAsync(this string id, CancellationToken cancellationToken = default, bool force = false)
			=> (id ?? "").GetModuleByID(force, false) ?? (await Module.GetAsync<Module>(id, cancellationToken).ConfigureAwait(false))?.Set();

		internal static IFilterBy<Module> GetModulesFilter(this string systemID, string definitionID = null)
		{
			var filter = Filters<Module>.And(Filters<Module>.Equals("SystemID", systemID));
			if (!string.IsNullOrWhiteSpace(definitionID))
				filter.Add(Filters<Module>.Equals("DefinitionID", definitionID));
			return filter;
		}

		internal static List<Module> GetModules(this string systemID, string definitionID = null, bool updateCache = true)
		{
			if (string.IsNullOrWhiteSpace(systemID))
				return new List<Module>();
			var filter = systemID.GetModulesFilter(definitionID);
			var sort = Sorts<Module>.Ascending("Title");
			var modules = Module.Find(filter, sort, 0, 1, Extensions.GetCacheKey(filter, sort, 0, 1));
			modules.ForEach(module => module.Set(updateCache));
			return modules;
		}

		internal static async Task<List<Module>> GetModulesAsync(this string systemID, string definitionID = null, CancellationToken cancellationToken = default, bool updateCache = true)
		{
			if (string.IsNullOrWhiteSpace(systemID))
				return new List<Module>();
			var filter = systemID.GetModulesFilter(definitionID);
			var sort = Sorts<Module>.Ascending("Title");
			var modules = await Module.FindAsync(filter, sort, 0, 1, Extensions.GetCacheKey(filter, sort, 0, 1), cancellationToken).ConfigureAwait(false);
			await modules.ForEachAsync((module, token) => module.SetAsync(updateCache, token), cancellationToken).ConfigureAwait(false);
			return modules;
		}
	}
}
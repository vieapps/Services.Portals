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
	[Serializable, BsonIgnoreExtraElements, DebuggerDisplay("ID = {ID}, Title = {Title}")]
	[Entity(CollectionName = "Modules", TableName = "T_Portals_Modules", CacheClass = typeof(Utility), CacheName = "Cache", Searchable = true)]
	public sealed class Module : Repository<Module>, IPortalModule
	{
		public Module() : base() { }

		[Property(MaxLength = 250, NotNull = true, NotEmpty = true), Sortable(IndexName = "Title"), Searchable]
		[FormControl(Segment = "basic", Label = "{{portals.modules.controls.[name].label}}", PlaceHolder = "{{portals.modules.controls.[name].placeholder}}", Description = "{{portals.modules.controls.[name].description}}")]
		public override string Title { get; set; } = "";

		[Searchable]
		[FormControl(Segment = "basic", ControlType = "TextArea", Label = "{{portals.modules.controls.[name].label}}", PlaceHolder = "{{portals.modules.controls.[name].placeholder}}", Description = "{{portals.modules.controls.[name].description}}")]
		public string Description { get; set; }

		[Property(MaxLength = 32)]
		[FormControl(Segment = "basic", ControlType = "Lookup", Label = "{{portals.modules.controls.[name].label}}", PlaceHolder = "{{portals.modules.controls.[name].placeholder}}", Description = "{{portals.modules.controls.[name].description}}")]
		public string DesktopID { get; set; }

		[NonSerialized]
		JObject _json;

		string _exras;

		[Property(IsCLOB = true)]
		[JsonIgnore, XmlIgnore]
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

		[Property(MaxLength = 32, NotNull = true, NotEmpty = true), Sortable(IndexName = "Management")]
		[FormControl(Hidden = true)]
		public override string SystemID { get; set; }

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public override string RepositoryID { get; set; }

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public override string EntityID { get; set; }

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public string OrganizationID => this.SystemID;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public new IPortalObject Parent => this.Organization;

		[JsonIgnore, XmlIgnore]
		public string DefinitionType { get; set; }

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public RepositoryDefinition Definition => RepositoryMediator.GetRepositoryDefinition(AssemblyLoader.GetType(this.DefinitionType), true);

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public Organization Organization => (this.OrganizationID ?? "").GetOrganizationByID();

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public Desktop Desktop => (this.DesktopID ?? "").GetDesktopByID() ?? this.Organization?.HomeDesktop;

		[Ignore, BsonIgnore]
		public Settings.Notifications Notifications { get; set; } = new Settings.Notifications();

		[Ignore, BsonIgnore]
		public Dictionary<string, string> Trackings { get; set; } = new Dictionary<string, string>();

		[Ignore, BsonIgnore]
		public Settings.Email EmailSettings { get; set; } = new Settings.Email();

		internal void NormalizeExtras()
		{
			this.Notifications.Emails.Normalize();
			this.Notifications.WebHooks.Normalize();
			this.EmailSettings.Normalize();
			this._json = this._json ?? JObject.Parse(string.IsNullOrWhiteSpace(this.Extras) ? "{}" : this.Extras);
			ModuleExtensions.ExtraProperties.ForEach(name => this._json[name] = this.GetProperty(name)?.ToJson());
			this._exras = this._json.ToString(Formatting.None);
		}

		public override void ProcessPropertyChanged(string name)
		{
			if (name.IsEquals("Extras"))
			{
				this._json = this._json ?? JObject.Parse(string.IsNullOrWhiteSpace(this.Extras) ? "{}" : this.Extras);
				this.Notifications = this._json["Notifications"]?.FromJson<Settings.Notifications>() ?? new Settings.Notifications();
				this.Trackings = this._json["Trackings"]?.FromJson<Dictionary<string, string>>() ?? new Dictionary<string, string>();
				this.EmailSettings = this._json["EmailSettings"]?.FromJson<Settings.Email>() ?? new Settings.Email();
			}
			else if (ModuleExtensions.ExtraProperties.Contains(name))
			{
				this._json = this._json ?? JObject.Parse(string.IsNullOrWhiteSpace(this.Extras) ? "{}" : this.Extras);
				this._json[name] = this.GetProperty(name)?.ToJson();
			}
		}
	}

	internal static class ModuleExtensions
	{
		internal static ConcurrentDictionary<string, Module> Modules { get; } = new ConcurrentDictionary<string, Module>(StringComparer.OrdinalIgnoreCase);

		public static HashSet<string> ExtraProperties { get; } = "Notifications,Trackings,EmailSettings".ToHashSet();

		internal static Module CreateModuleInstance(this ExpandoObject requestBody, string excluded = null, Action<Module> onCompleted = null)
			=> requestBody.Copy<Module>(excluded?.ToHashSet(), onCompleted);

		internal static Module UpdateModuleInstance(this Module module, ExpandoObject requestBody, string excluded = null, Action<Module> onCompleted = null)
		{
			module.CopyFrom(requestBody, excluded?.ToHashSet(), onCompleted);
			return module;
		}

		internal static Module Set(this Module module, bool updateCache = false)
		{
			if (module != null)
			{
				ModuleExtensions.Modules[module.ID] = module;
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
			=> !string.IsNullOrWhiteSpace(id) && ModuleExtensions.Modules.TryRemove(id, out var module) ? module : null;

		internal static Module GetModuleByID(this string id, bool force = false, bool fetchRepository = true)
			=> !force && !string.IsNullOrWhiteSpace(id) && ModuleExtensions.Modules.ContainsKey(id)
				? ModuleExtensions.Modules[id]
				: fetchRepository && !string.IsNullOrWhiteSpace(id)
					? Module.Get<Module>(id)?.Set()
					: null;

		internal static async Task<Module> GetModuleByIDAsync(this string id, CancellationToken cancellationToken = default, bool force = false)
			=> (id ?? "").GetModuleByID(force, false) ?? (await Module.GetAsync<Module>(id, cancellationToken).ConfigureAwait(false))?.Set();
	}
}
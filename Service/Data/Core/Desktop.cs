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
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using MongoDB.Bson.Serialization.Attributes;
using net.vieapps.Components.Security;
using net.vieapps.Components.Repository;
using net.vieapps.Components.Utility;
#endregion

namespace net.vieapps.Services.Portals
{
	[Serializable, BsonIgnoreExtraElements, DebuggerDisplay("ID = {ID}, Title = {Title}")]
	[Entity(CollectionName = "Desktops", TableName = "T_Portals_Desktops", CacheClass = typeof(Utility), CacheName = "Cache", Searchable = true)]
	public sealed class Desktop : Repository<Desktop>, INestedObject
	{
		public Desktop() : base() { }

		[Property(MaxLength = 32)]
		[Sortable(IndexName = "Management")]
		[FormControl(Segment = "basic", ControlType = "Lookup", Multiple = false, Label = "{{portals.desktops.controls.[name].label}}", PlaceHolder = "{{portals.desktops.controls.[name].placeholder}}", Description = "{{portals.desktops.controls.[name].description}}")]
		public string ParentID { get; set; }

		[Property(MaxLength = 250, NotNull = true, NotEmpty = true)]
		[Sortable(IndexName = "Title"), Searchable]
		[FormControl(Segment = "basic", Label = "{{portals.desktops.controls.[name].label}}", PlaceHolder = "{{portals.desktops.controls.[name].placeholder}}", Description = "{{portals.desktops.controls.[name].description}}")]
		public override string Title { get; set; } = "";

		[Property(MaxLength = 100, NotNull = true, NotEmpty = true)]
		[Sortable(UniqueIndexName = "Alias"), Searchable]
		[FormControl(Segment = "basic", Label = "{{portals.desktops.controls.[name].label}}", PlaceHolder = "{{portals.desktops.controls.[name].placeholder}}", Description = "{{portals.desktops.controls.[name].description}}")]
		public string Alias { get; set; } = "";

		[Property(MaxLength = 250)]
		[Sortable(IndexName = "Aliases"), Searchable]
		[FormControl(Segment = "basic", Label = "{{portals.desktops.controls.[name].label}}", PlaceHolder = "{{portals.desktops.controls.[name].placeholder}}", Description = "{{portals.desktops.controls.[name].description}}")]
		public string Aliases { get; set; }

		[Property(MaxLength = 5)]
		[FormControl(Segment = "display", ControlType = "Select", Label = "{{portals.desktops.controls.[name].label}}", PlaceHolder = "{{portals.desktops.controls.[name].placeholder}}", Description = "{{portals.desktops.controls.[name].description}}")]
		public string Language { get; set; }

		[Property(MaxLength = 100)]
		[FormControl(Segment = "display", ControlType = "Select", Label = "{{portals.desktops.controls.[name].label}}", PlaceHolder = "{{portals.desktops.controls.[name].placeholder}}", Description = "{{portals.desktops.controls.[name].description}}")]
		public string Theme { get; set; }

		[Property(IsCLOB = true)]
		[FormControl(Segment = "display", ControlType = "TextArea", Label = "{{portals.desktops.controls.[name].label}}", PlaceHolder = "{{portals.desktops.controls.[name].placeholder}}", Description = "{{portals.desktops.controls.[name].description}}")]
		public string Template { get; set; }

		[Ignore, BsonIgnore]
		[FormControl(Segment = "display", Label = "{{portals.desktops.controls.[name].label}}", PlaceHolder = "{{portals.desktops.controls.[name].placeholder}}", Description = "{{portals.desktops.controls.[name].description}}")]
		public Settings.UI UISettings { get; set; }

		[Ignore, BsonIgnore]
		[FormControl(Segment = "display", ControlType = "Lookup", Label = "{{portals.desktops.controls.[name].label}}", PlaceHolder = "{{portals.desktops.controls.[name].placeholder}}", Description = "{{portals.desktops.controls.[name].description}}")]
		public string IconURI { get; set; }

		[Ignore, BsonIgnore]
		[FormControl(Segment = "display", ControlType = "TextArea", Label = "{{portals.desktops.controls.[name].label}}", PlaceHolder = "{{portals.desktops.controls.[name].placeholder}}", Description = "{{portals.desktops.controls.[name].description}}")]
		public string MetaTags { get; set; }

		[Ignore, BsonIgnore]
		[FormControl(Segment = "display", ControlType = "TextArea", Label = "{{portals.desktops.controls.[name].label}}", PlaceHolder = "{{portals.desktops.controls.[name].placeholder}}", Description = "{{portals.desktops.controls.[name].description}}")]
		public string Scripts { get; set; }

		[Property(MaxLength = 32)]
		[FormControl(Segment = "seo", ControlType = "Select", Label = "{{portals.desktops.controls.[name].label}}", PlaceHolder = "{{portals.desktops.controls.[name].placeholder}}", Description = "{{portals.desktops.controls.[name].description}}")]
		public string MainPortletID { get; set; }

		[Ignore, BsonIgnore]
		[FormControl(Segment = "seo", Label = "{{portals.desktops.controls.[name].label}}", PlaceHolder = "{{portals.desktops.controls.[name].placeholder}}", Description = "{{portals.desktops.controls.[name].description}}")]
		public Settings.SEO SEOSettings { get; set; }

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

		[Property(MaxLength = 32, NotNull = true, NotEmpty = true)]
		[Sortable(IndexName = "Management", UniqueIndexName = "Alias")]
		[FormControl(Hidden = true)]
		public override string SystemID { get; set; }

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public int OrderIndex { get; set; }

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public override string RepositoryID { get; set; }

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public override string EntityID { get; set; }

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public string OrganizationID => this.SystemID;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public Organization Organization => (this.OrganizationID ?? "").GetOrganizationByID();

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public Desktop ParentDesktop => (this.ParentID ?? "").GetDesktopByID();

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public new IPortalObject Parent => this.ParentDesktop ?? this.Organization as IPortalObject;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		INestedObject INestedObject.Parent => this.ParentDesktop;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public string FullTitle
		{
			get
			{
				var parent = this.ParentDesktop;
				return (parent == null ? "" : $"{parent.FullTitle} > ") + this.Title;
			}
		}

		internal List<string> _childrenIDs;

		internal List<Desktop> GetChildren(bool notifyPropertyChanged = true, List<Desktop> desktops = null)
		{
			if (this._childrenIDs == null)
			{
				desktops = desktops ?? this.SystemID.GetDesktopsByParentID(this.ID);
				this._childrenIDs = desktops.Select(desktop => desktop.ID).ToList();
				if (notifyPropertyChanged)
					this.NotifyPropertyChanged("ChildrenIDs");
				return desktops;
			}
			return this._childrenIDs.Select(id => id.GetDesktopByID()).ToList();
		}

		internal async Task<List<Desktop>> GetChildrenAsync(CancellationToken cancellationToken = default, bool notifyPropertyChanged = true)
			=> this._childrenIDs == null
				? this.GetChildren(notifyPropertyChanged, await this.SystemID.GetDesktopsByParentIDAsync(this.ID, cancellationToken).ConfigureAwait(false))
				: this._childrenIDs.Select(id => id.GetDesktopByID()).ToList();

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public List<Desktop> Children => this.GetChildren();

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		List<INestedObject> INestedObject.Children => this.Children.Select(desktop => desktop as INestedObject).ToList();

		public override JObject ToJson(bool addTypeOfExtendedProperties = false, Action<JObject> onPreCompleted = null)
			=> this.ToJson(false, addTypeOfExtendedProperties, onPreCompleted);

		public JObject ToJson(bool addChildren, bool addTypeOfExtendedProperties, Action<JObject> onPreCompleted = null)
			=> base.ToJson(addTypeOfExtendedProperties, json =>
			{
				if (addChildren && json != null)
					json["Children"] = (this.GetChildren() ?? new List<Desktop>()).Select(desktop => desktop?.ToJson(true, false)).Where(desktop => desktop != null).ToJArray();
				onPreCompleted?.Invoke(json);
			});

		internal void NormalizeSettings()
		{
			this._settings = this._settings ?? JObject.Parse(string.IsNullOrWhiteSpace(this.OtherSettings) ? "{}" : this.OtherSettings);
			DesktopExtensions.SettingProperties.ForEach(name => this._settings[name] = this.GetProperty(name)?.ToJson());
			this._otherSettings = this._settings.ToString(Formatting.None);
		}

		public override void ProcessPropertyChanged(string name)
		{
			if (name.IsEquals("ChildrenIDs"))
				Utility.Cache.Set(this);
			else if (name.IsEquals("OtherSettings"))
			{
				this._settings = this._settings ?? JObject.Parse(string.IsNullOrWhiteSpace(this.OtherSettings) ? "{}" : this.OtherSettings);
				this.UISettings = this._settings["UISettings"]?.FromJson<Settings.UI>() ?? new Settings.UI();
				this.IconURI = this._settings["IconURI"]?.FromJson<string>();
				this.MetaTags = this._settings["MetaTags"]?.FromJson<string>();
				this.Scripts = this._settings["Scripts"]?.FromJson<string>();
				this.SEOSettings = this._settings["SEOSettings"]?.FromJson<Settings.SEO>() ?? new Settings.SEO();
			}
			else if (DesktopExtensions.SettingProperties.ToHashSet().Contains(name))
			{
				this._settings = this._settings ?? JObject.Parse(string.IsNullOrWhiteSpace(this.OtherSettings) ? "{}" : this.OtherSettings);
				this._settings[name] = this.GetProperty(name)?.ToJson();
			}
		}
	}

	internal static class DesktopExtensions
	{
		internal static ConcurrentDictionary<string, Desktop> Desktops { get; } = new ConcurrentDictionary<string, Desktop>(StringComparer.OrdinalIgnoreCase);

		internal static ConcurrentDictionary<string, Desktop> DesktopsByAlias { get; } = new ConcurrentDictionary<string, Desktop>(StringComparer.OrdinalIgnoreCase);

		internal static List<string> SettingProperties { get; } = "UISettings,IconURI,MetaTags,Scripts,SEOSettings".ToList();

		internal static Desktop CreateDesktopInstance(this ExpandoObject requestBody, string excluded = null, Action<Desktop> onCompleted = null)
			=> requestBody.Copy<Desktop>(excluded?.ToHashSet(), desktop =>
			{
				desktop.Alias = string.IsNullOrWhiteSpace(desktop.Alias) ? desktop.Title.NormalizeAlias() : desktop.Alias.NormalizeAlias();
				desktop.Aliases = string.IsNullOrWhiteSpace(desktop.Aliases) ? null : desktop.Aliases.ToList(";", true, true).Select(alias => alias.NormalizeAlias()).Where(alias => !alias.IsEquals(desktop.Alias)).Join(";");
				desktop.SEOSettings = desktop.SEOSettings ?? new Settings.SEO();
				"TitleMode,DescriptionMode,KeywordsMode".ToList().ForEach(name =>
				{
					var value = requestBody.Get<string>($"SEOSettings.{name}");
					desktop.SEOSettings.SetAttributeValue(name, !string.IsNullOrWhiteSpace(value) && value.TryToEnum(out Settings.SEOMode mode) ? mode as object : null);
				});
				onCompleted?.Invoke(desktop);
			});

		internal static Desktop UpdateDesktopInstance(this Desktop desktop, ExpandoObject requestBody, string excluded = null, Action<Desktop> onCompleted = null)
		{
			desktop.CopyFrom(requestBody, excluded?.ToHashSet());
			desktop.Alias = string.IsNullOrWhiteSpace(desktop.Alias) ? desktop.Title.NormalizeAlias() : desktop.Alias.NormalizeAlias();
			desktop.Aliases = string.IsNullOrWhiteSpace(desktop.Aliases) ? null : desktop.Aliases.ToList(";", true, true).Select(alias => alias.NormalizeAlias()).Where(alias => !alias.IsEquals(desktop.Alias)).Join(";");
			desktop.SEOSettings = desktop.SEOSettings ?? new Settings.SEO();
			"TitleMode,DescriptionMode,KeywordsMode".ToList().ForEach(name =>
			{
				var value = requestBody.Get<string>($"SEOSettings.{name}");
				desktop.SEOSettings.SetAttributeValue(name, !string.IsNullOrWhiteSpace(value) && value.TryToEnum(out Settings.SEOMode mode) ? mode as object : null);
			});
			onCompleted?.Invoke(desktop);
			return desktop;
		}

		internal static Desktop Set(this Desktop desktop, bool clear = false, bool updateCache = false)
		{
			if (desktop != null)
			{
				if (clear)
				{
					var current = desktop.Remove();
					if (current != null && !current.ParentID.IsEquals(desktop.ParentID))
					{
						if (current.ParentDesktop != null)
							current.ParentDesktop._childrenIDs = null;
						if (desktop.ParentDesktop != null)
							desktop.ParentDesktop._childrenIDs = null;
					}
				}
				DesktopExtensions.Desktops[desktop.ID] = desktop;
				DesktopExtensions.DesktopsByAlias[$"{desktop.SystemID}:{desktop.Alias}"] = desktop;
				if (!string.IsNullOrWhiteSpace(desktop.Aliases))
					desktop.Aliases.Replace(",", ";").ToList(";").ForEach(alias => DesktopExtensions.DesktopsByAlias.TryAdd($"{desktop.SystemID}:{alias}", desktop));
				if (updateCache)
					Utility.Cache.Set(desktop);
			}
			return desktop;
		}

		internal static async Task<Desktop> SetAsync(this Desktop desktop, bool clear = false, bool updateCache = false, CancellationToken cancellationToken = default)
		{
			desktop?.Set(clear);
			await (updateCache && desktop != null ? Utility.Cache.SetAsync(desktop, cancellationToken) : Task.CompletedTask).ConfigureAwait(false);
			return desktop;
		}

		internal static Desktop Remove(this Desktop desktop)
			=> (desktop?.ID ?? "").RemoveDesktop();

		internal static Desktop RemoveDesktop(this string id)
		{
			if (!string.IsNullOrWhiteSpace(id) && DesktopExtensions.Desktops.TryRemove(id, out var desktop) && desktop != null)
			{
				DesktopExtensions.Desktops.Remove(desktop.ID);
				DesktopExtensions.DesktopsByAlias.Remove($"{desktop.SystemID}:{desktop.Alias}");
				if (!string.IsNullOrWhiteSpace(desktop.Aliases))
					desktop.Aliases.Replace(",", ";").ToList(";").ForEach(alias => DesktopExtensions.DesktopsByAlias.Remove($"{desktop.SystemID}:{alias}"));
				return desktop;
			}
			return null;
		}

		internal static Desktop GetDesktopByID(this string id, bool force = false, bool fetchRepository = true)
			=> !force && !string.IsNullOrWhiteSpace(id) && DesktopExtensions.Desktops.ContainsKey(id)
				? DesktopExtensions.Desktops[id]
				: fetchRepository && !string.IsNullOrWhiteSpace(id)
					? Desktop.Get<Desktop>(id)?.Set()
					: null;

		internal static async Task<Desktop> GetDesktopByIDAsync(this string id, CancellationToken cancellationToken = default, bool force = false)
			=> (id ?? "").GetDesktopByID(force, false) ?? (await Desktop.GetAsync<Desktop>(id, cancellationToken).ConfigureAwait(false))?.Set();

		internal static Desktop GetDesktopByAlias(this string systemID, string alias, bool force = false, bool fetchRepository = true)
		{
			if (string.IsNullOrWhiteSpace(systemID) || string.IsNullOrWhiteSpace(alias))
				return null;

			var desktop = !force && DesktopExtensions.DesktopsByAlias.ContainsKey($"{systemID}:{alias}")
				? DesktopExtensions.DesktopsByAlias[$"{systemID}:{alias}"]
				: null;

			return desktop ?? (fetchRepository && !string.IsNullOrWhiteSpace(alias) ? Desktop.Get<Desktop>(Filters<Desktop>.And(Filters<Desktop>.Equals("SystemID", systemID), Filters<Desktop>.Equals("Alias", alias)), null, null)?.Set() : null);
		}

		internal static async Task<Desktop> GetDesktopByAliasAsync(this string systemID, string alias, CancellationToken cancellationToken = default, bool force = false)
			=> (systemID ?? "").GetDesktopByAlias(alias, force, false) ?? (!string.IsNullOrWhiteSpace(alias) ? (await Desktop.GetAsync<Desktop>(Filters<Desktop>.And(Filters<Desktop>.Equals("SystemID", systemID), Filters<Desktop>.Equals("Alias", alias)), null, null, cancellationToken).ConfigureAwait(false))?.Set() : null);

		internal static IFilterBy<Desktop> GetDesktopsFilter(this string systemID, string parentID)
			=> Filters<Desktop>.And(Filters<Desktop>.Equals("SystemID", systemID), string.IsNullOrWhiteSpace(parentID) ? Filters<Desktop>.IsNull("ParentID") : Filters<Desktop>.Equals("ParentID", parentID));

		internal static List<Desktop> GetDesktopsByParentID(this string systemID, string parentID, bool updateCache = true)
		{
			if (string.IsNullOrWhiteSpace(systemID))
				return new List<Desktop>();
			var filter = systemID.GetDesktopsFilter(parentID);
			var sort = Sorts<Desktop>.Ascending("Title");
			var desktops = Desktop.Find(filter, sort, 0, 1, Extensions.GetCacheKey(filter, sort, 0, 1));
			desktops.ForEach(desktop => desktop.Set(false, updateCache));
			return desktops;
		}

		internal static async Task<List<Desktop>> GetDesktopsByParentIDAsync(this string systemID, string parentID, CancellationToken cancellationToken = default, bool updateCache = true)
		{
			if (string.IsNullOrWhiteSpace(systemID))
				return new List<Desktop>();
			var filter = systemID.GetDesktopsFilter(parentID);
			var sort = Sorts<Desktop>.Ascending("Title");
			var desktops = await Desktop.FindAsync(filter, sort, 0, 1, Extensions.GetCacheKey(filter, sort, 0, 1), cancellationToken).ConfigureAwait(false);
			await desktops.ForEachAsync((desktop, token) => desktop.SetAsync(false, updateCache, token), cancellationToken).ConfigureAwait(false);
			return desktops;
		}
	}
}
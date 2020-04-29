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
		public override string Title { get; set; }

		[Property(MaxLength = 100, NotNull = true, NotEmpty = true)]
		[Sortable(UniqueIndexName = "Alias"), Searchable]
		[FormControl(Segment = "basic", Label = "{{portals.desktops.controls.[name].label}}", PlaceHolder = "{{portals.desktops.controls.[name].placeholder}}", Description = "{{portals.desktops.controls.[name].description}}")]
		public string Alias { get; set; }

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
		[FormControl(Segment = "display", ControlType = "Lookup", Label = "{{portals.desktops.controls.[name].label}}", PlaceHolder = "{{portals.desktops.controls.[name].placeholder}}", Description = "{{portals.desktops.controls.[name].description}}")]
		public string CoverURI { get; set; }

		[Ignore, BsonIgnore]
		[FormControl(Segment = "display", ControlType = "TextArea", Label = "{{portals.desktops.controls.[name].label}}", PlaceHolder = "{{portals.desktops.controls.[name].placeholder}}", Description = "{{portals.desktops.controls.[name].description}}")]
		public string MetaTags { get; set; }

		[Ignore, BsonIgnore]
		[FormControl(Segment = "display", ControlType = "TextArea", Label = "{{portals.desktops.controls.[name].label}}", PlaceHolder = "{{portals.desktops.controls.[name].placeholder}}", Description = "{{portals.desktops.controls.[name].description}}")]
		public string Scripts { get; set; }

		[Ignore, BsonIgnore]
		[FormControl(Segment = "seo", ControlType = "Select", Label = "{{portals.desktops.controls.[name].label}}", PlaceHolder = "{{portals.desktops.controls.[name].placeholder}}", Description = "{{portals.desktops.controls.[name].description}}")]
		public string MainPortletID { get; set; }

		[Ignore, BsonIgnore]
		[FormControl(Segment = "seo", Label = "{{portals.desktops.controls.[name].label}}", PlaceHolder = "{{portals.desktops.controls.[name].placeholder}}", Description = "{{portals.desktops.controls.[name].description}}")]
		public Settings.SEO SEOSettings { get; set; }

		[NonSerialized]
		JObject _json;

		string _extras;

		[Property(IsCLOB = true)]
		[JsonIgnore, XmlIgnore]
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
		[Sortable(IndexName = "Management", UniqueIndexName = "Alias")]
		[FormControl(Hidden = true)]
		public override string SystemID { get; set; }

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public override string RepositoryID { get; set; }

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public override string RepositoryEntityID { get; set; }

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public override Privileges OriginalPrivileges { get; set; }

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public override Privileges WorkingPrivileges => this.Organization?.WorkingPrivileges;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public int OrderIndex { get; set; }

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public string OrganizationID => this.SystemID;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public Organization Organization => (this.OrganizationID ?? "").GetOrganizationByID();

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public Desktop ParentDesktop => (this.ParentID ?? "").GetDesktopByID();

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public override RepositoryBase Parent => this.ParentDesktop ?? this.Organization as RepositoryBase;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		IPortalObject IPortalObject.Parent => this.ParentDesktop ?? this.Organization as IPortalObject;

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
				desktops = desktops ?? this.SystemID.GetDesktops(this.ID);
				this._childrenIDs = desktops.Select(desktop => desktop.ID).ToList();
				if (notifyPropertyChanged)
					this.NotifyPropertyChanged("ChildrenIDs");
				return desktops;
			}
			return this._childrenIDs.Select(id => id.GetDesktopByID()).ToList();
		}

		internal async Task<List<Desktop>> GetChildrenAsync(CancellationToken cancellationToken = default, bool notifyPropertyChanged = true)
			=> this._childrenIDs == null
				? this.GetChildren(notifyPropertyChanged, await this.SystemID.GetDesktopsAsync(this.ID, cancellationToken).ConfigureAwait(false))
				: this._childrenIDs.Select(id => id.GetDesktopByID()).ToList();

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public List<Desktop> Children => this.GetChildren();

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		List<INestedObject> INestedObject.Children => this.Children?.Select(desktop => desktop as INestedObject).ToList();

		public override JObject ToJson(bool addTypeOfExtendedProperties = false, Action<JObject> onPreCompleted = null)
			=> this.ToJson(false, addTypeOfExtendedProperties, onPreCompleted);

		public JObject ToJson(bool addChildren, bool addTypeOfExtendedProperties, Action<JObject> onPreCompleted = null)
			=> base.ToJson(addTypeOfExtendedProperties, json =>
			{
				json.Remove("Privileges");
				json.Remove("OriginalPrivileges");
				if (addChildren)
					json["Children"] = this.Children?.Select(desktop => desktop?.ToJson(true, false)).Where(desktop => desktop != null).ToJArray();
				onPreCompleted?.Invoke(json);
			});

		internal void NormalizeExtras()
		{
			this.UISettings?.Normalize();
			this.UISettings = this.UISettings != null && string.IsNullOrWhiteSpace(this.UISettings.Padding) && string.IsNullOrWhiteSpace(this.UISettings.Margin) && string.IsNullOrWhiteSpace(this.UISettings.Width) && string.IsNullOrWhiteSpace(this.UISettings.Height) && string.IsNullOrWhiteSpace(this.UISettings.Color) && string.IsNullOrWhiteSpace(this.UISettings.BackgroundColor) && string.IsNullOrWhiteSpace(this.UISettings.BackgroundImageURI) && string.IsNullOrWhiteSpace(this.UISettings.BackgroundImageRepeat) && string.IsNullOrWhiteSpace(this.UISettings.BackgroundImagePosition) && string.IsNullOrWhiteSpace(this.UISettings.BackgroundImageSize) && string.IsNullOrWhiteSpace(this.UISettings.Css) && string.IsNullOrWhiteSpace(this.UISettings.Style) ? null : this.UISettings;
			this.IconURI = string.IsNullOrWhiteSpace(this.IconURI) ? null : this.IconURI.Trim();
			this.CoverURI = string.IsNullOrWhiteSpace(this.CoverURI) ? null : this.CoverURI.Trim();
			this.MetaTags = string.IsNullOrWhiteSpace(this.MetaTags) ? null : this.MetaTags.Trim();
			this.Scripts = string.IsNullOrWhiteSpace(this.Scripts) ? null : this.Scripts.Trim();
			this.MainPortletID = string.IsNullOrWhiteSpace(this.MainPortletID) ? null : this.MainPortletID.Trim();
			this.SEOSettings?.Normalize();
			this.SEOSettings = this.SEOSettings != null && this.SEOSettings.SEOInfo == null && this.SEOSettings.TitleMode == null && this.SEOSettings.DescriptionMode == null && this.SEOSettings.KeywordsMode == null ? null : this.SEOSettings;
			this._json = this._json ?? JObject.Parse(string.IsNullOrWhiteSpace(this.Extras) ? "{}" : this.Extras);
			DesktopExtensions.ExtraProperties.ForEach(name => this._json[name] = this.GetProperty(name)?.ToJson());
			this._extras = this._json.ToString(Formatting.None);
		}

		public override void ProcessPropertyChanged(string name)
		{
			if (name.IsEquals("Extras"))
			{
				this._json = this._json ?? JObject.Parse(string.IsNullOrWhiteSpace(this.Extras) ? "{}" : this.Extras);
				this.UISettings = this._json["UISettings"]?.FromJson<Settings.UI>();
				this.IconURI = this._json["IconURI"]?.FromJson<string>();
				this.CoverURI = this._json["CoverURI"]?.FromJson<string>();
				this.MetaTags = this._json["MetaTags"]?.FromJson<string>();
				this.Scripts = this._json["Scripts"]?.FromJson<string>();
				this.MainPortletID = this._json["MainPortletID"]?.FromJson<string>();
				this.SEOSettings = this._json["SEOSettings"]?.FromJson<Settings.SEO>();
			}
			else if (DesktopExtensions.ExtraProperties.Contains(name))
			{
				this._json = this._json ?? JObject.Parse(string.IsNullOrWhiteSpace(this.Extras) ? "{}" : this.Extras);
				this._json[name] = this.GetProperty(name)?.ToJson();
			}
			else if (name.IsEquals("ChildrenIDs"))
				Utility.Cache.Set(this);
		}
	}

	public static class DesktopExtensions
	{
		public static ConcurrentDictionary<string, Desktop> Desktops { get; } = new ConcurrentDictionary<string, Desktop>(StringComparer.OrdinalIgnoreCase);

		public static ConcurrentDictionary<string, Desktop> DesktopsByAlias { get; } = new ConcurrentDictionary<string, Desktop>(StringComparer.OrdinalIgnoreCase);

		public static HashSet<string> ExtraProperties { get; } = "UISettings,IconURI,CoverURI,MetaTags,Scripts,MainPortletID,SEOSettings".ToHashSet();

		public static HashSet<string> ExcludedAliases { get; } = (UtilityService.GetAppSetting("Portals:ExcludedAliases", "") + ",Files,Downloads,Thumbnails,ThumbnailBigs,ThumbnailBigPngs").ToLower().ToHashSet();

		public static Desktop CreateDesktopInstance(this ExpandoObject requestBody, string excluded = null, Action<Desktop> onCompleted = null)
			=> requestBody.Copy<Desktop>(excluded?.ToHashSet(), desktop =>
			{
				desktop.Alias = string.IsNullOrWhiteSpace(desktop.Alias) ? desktop.Title.NormalizeAlias() : desktop.Alias.NormalizeAlias();
				desktop.Aliases = string.IsNullOrWhiteSpace(desktop.Aliases) ? null : desktop.Aliases.Replace(",", ";").ToList(";", true, true).Select(alias => alias.NormalizeAlias()).Where(alias => !DesktopExtensions.ExcludedAliases.Contains(alias) && !alias.IsEquals(desktop.Alias)).Join(";");
				desktop.SEOSettings = desktop.SEOSettings ?? new Settings.SEO();
				"TitleMode,DescriptionMode,KeywordsMode".ToList().ForEach(name =>
				{
					var value = requestBody.Get<string>($"SEOSettings.{name}");
					desktop.SEOSettings.SetAttributeValue(name, !string.IsNullOrWhiteSpace(value) && value.TryToEnum(out Settings.SEOMode mode) ? mode as object : null);
				});
				desktop.SEOSettings = desktop.SEOSettings != null && desktop.SEOSettings.SEOInfo == null && desktop.SEOSettings.TitleMode == null && desktop.SEOSettings.DescriptionMode == null && desktop.SEOSettings.KeywordsMode == null ? null : desktop.SEOSettings;
				desktop.TrimAll();
				onCompleted?.Invoke(desktop);
			});

		public static Desktop UpdateDesktopInstance(this Desktop desktop, ExpandoObject requestBody, string excluded = null, Action<Desktop> onCompleted = null)
		{
			desktop.CopyFrom(requestBody, excluded?.ToHashSet());
			desktop.Alias = string.IsNullOrWhiteSpace(desktop.Alias) ? desktop.Title.NormalizeAlias() : desktop.Alias.NormalizeAlias();
			desktop.Aliases = string.IsNullOrWhiteSpace(desktop.Aliases) ? null : desktop.Aliases.Replace(",", ";").ToList(";", true, true).Select(alias => alias.NormalizeAlias()).Where(alias => !DesktopExtensions.ExcludedAliases.Contains(alias) && !alias.IsEquals(desktop.Alias)).Join(";");
			desktop.SEOSettings = desktop.SEOSettings ?? new Settings.SEO();
			"TitleMode,DescriptionMode,KeywordsMode".ToList().ForEach(name =>
			{
				var value = requestBody.Get<string>($"SEOSettings.{name}");
				desktop.SEOSettings.SetAttributeValue(name, !string.IsNullOrWhiteSpace(value) && value.TryToEnum(out Settings.SEOMode mode) ? mode as object : null);
			});
			desktop.SEOSettings = desktop.SEOSettings != null && desktop.SEOSettings.SEOInfo == null && desktop.SEOSettings.TitleMode == null && desktop.SEOSettings.DescriptionMode == null && desktop.SEOSettings.KeywordsMode == null ? null : desktop.SEOSettings;
			desktop.TrimAll();
			onCompleted?.Invoke(desktop);
			return desktop;
		}

		public static Desktop Set(this Desktop desktop, bool clear = false, bool updateCache = false)
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
				Utility.NotRecognizedAliases.Remove($"Desktop:{desktop.SystemID}:{desktop.Alias}");

				if (!string.IsNullOrWhiteSpace(desktop.Aliases))
					desktop.Aliases.ToList(";").ForEach(alias =>
					{
						if (DesktopExtensions.DesktopsByAlias.TryAdd($"{desktop.SystemID}:{alias}", desktop))
							Utility.NotRecognizedAliases.Remove($"Desktop:{desktop.SystemID}:{alias}");
					});

				if (updateCache)
					Utility.Cache.Set(desktop);
			}
			return desktop;
		}

		public static async Task<Desktop> SetAsync(this Desktop desktop, bool clear = false, bool updateCache = false, CancellationToken cancellationToken = default)
		{
			desktop?.Set(clear);
			await (updateCache && desktop != null ? Utility.Cache.SetAsync(desktop, cancellationToken) : Task.CompletedTask).ConfigureAwait(false);
			return desktop;
		}

		public static Desktop Remove(this Desktop desktop)
			=> (desktop?.ID ?? "").RemoveDesktop();

		public static Desktop RemoveDesktop(this string id)
		{
			if (!string.IsNullOrWhiteSpace(id) && DesktopExtensions.Desktops.TryRemove(id, out var desktop) && desktop != null)
			{
				DesktopExtensions.Desktops.Remove(desktop.ID);
				DesktopExtensions.DesktopsByAlias.Remove($"{desktop.SystemID}:{desktop.Alias}");
				if (!string.IsNullOrWhiteSpace(desktop.Aliases))
					desktop.Aliases.ToList(";").ForEach(alias => DesktopExtensions.DesktopsByAlias.Remove($"{desktop.SystemID}:{alias}"));
				return desktop;
			}
			return null;
		}

		public static Desktop GetDesktopByID(this string id, bool force = false, bool fetchRepository = true)
			=> !force && !string.IsNullOrWhiteSpace(id) && DesktopExtensions.Desktops.ContainsKey(id)
				? DesktopExtensions.Desktops[id]
				: fetchRepository && !string.IsNullOrWhiteSpace(id)
					? Desktop.Get<Desktop>(id)?.Set()
					: null;

		public static async Task<Desktop> GetDesktopByIDAsync(this string id, CancellationToken cancellationToken = default, bool force = false)
			=> (id ?? "").GetDesktopByID(force, false) ?? (await Desktop.GetAsync<Desktop>(id, cancellationToken).ConfigureAwait(false))?.Set();

		public static Desktop GetDesktopByAlias(this string systemID, string alias, bool force = false, bool fetchRepository = true)
		{
			if (string.IsNullOrWhiteSpace(systemID) || string.IsNullOrWhiteSpace(alias) || Utility.NotRecognizedAliases.Contains($"Desktop:{systemID}:{alias}"))
				return null;

			var desktop = !force && DesktopExtensions.DesktopsByAlias.ContainsKey($"{systemID}:{alias}")
				? DesktopExtensions.DesktopsByAlias[$"{systemID}:{alias}"]
				: null;

			if (desktop == null && fetchRepository)
			{
				desktop = Desktop.Get<Desktop>(Filters<Desktop>.And(Filters<Desktop>.Equals("SystemID", systemID), Filters<Desktop>.Equals("Alias", alias)), null, null)?.Set();
				if (desktop == null)
					Utility.NotRecognizedAliases.Add($"Desktop:{systemID}:{alias}");
			}

			return desktop;
		}

		public static async Task<Desktop> GetDesktopByAliasAsync(this string systemID, string alias, CancellationToken cancellationToken = default, bool force = false)
		{
			if (string.IsNullOrWhiteSpace(systemID) || string.IsNullOrWhiteSpace(alias) || Utility.NotRecognizedAliases.Contains($"Desktop:{systemID}:{alias}"))
				return null;

			var desktop = systemID.GetDesktopByAlias(alias, force, false) ?? (await Desktop.GetAsync<Desktop>(Filters<Desktop>.And(Filters<Desktop>.Equals("SystemID", systemID), Filters<Desktop>.Equals("Alias", alias)), null, null, cancellationToken).ConfigureAwait(false))?.Set();
			if (desktop == null)
				Utility.NotRecognizedAliases.Add($"Desktop:{systemID}:{alias}");
			return desktop;
		}

		public static IFilterBy<Desktop> GetDesktopsFilter(this string systemID, string parentID)
			=> Filters<Desktop>.And(Filters<Desktop>.Equals("SystemID", systemID), string.IsNullOrWhiteSpace(parentID) ? Filters<Desktop>.IsNull("ParentID") : Filters<Desktop>.Equals("ParentID", parentID));

		public static List<Desktop> GetDesktops(this string systemID, string parentID, bool updateCache = true)
		{
			if (string.IsNullOrWhiteSpace(systemID))
				return new List<Desktop>();
			var filter = systemID.GetDesktopsFilter(parentID);
			var sort = Sorts<Desktop>.Ascending("Title");
			var desktops = Desktop.Find(filter, sort, 0, 1, Extensions.GetCacheKey(filter, sort, 0, 1));
			desktops.ForEach(desktop => desktop.Set(false, updateCache));
			return desktops;
		}

		public static async Task<List<Desktop>> GetDesktopsAsync(this string systemID, string parentID, CancellationToken cancellationToken = default, bool updateCache = true)
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
#region Related components
using System;
using System.Linq;
using System.Diagnostics;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Serialization;
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
		public Organization Organization => Utility.GetOrganizationByID(this.OrganizationID);

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public Desktop ParentDesktop => Utility.GetDesktopByID(this.ParentID);

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
				desktops = desktops ?? Utility.GetDesktopsByParentID(this.SystemID, this.ID);
				this._childrenIDs = desktops.Select(desktop => desktop.ID).ToList();
				if (notifyPropertyChanged)
					this.NotifyPropertyChanged("ChildrenIDs");
				return desktops;
			}
			return this._childrenIDs.Select(id => Utility.GetDesktopByID(id)).ToList();
		}

		internal async Task<List<Desktop>> GetChildrenAsync(CancellationToken cancellationToken = default, bool notifyPropertyChanged = true)
			=> this._childrenIDs == null
				? this.GetChildren(notifyPropertyChanged, await Utility.GetDesktopsByParentIDAsync(this.SystemID, this.ID, cancellationToken).ConfigureAwait(false))
				: this._childrenIDs.Select(id => Utility.GetDesktopByID(id)).ToList();

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
					json["Children"] = (this.GetChildren() ?? new List<Desktop>()).ToJArray(desktop => desktop?.ToJson(true, false));
				onPreCompleted?.Invoke(json);
			});

		internal static List<string> SettingProperties { get; } = "UISettings,IconURI,MetaTags,Scripts,SEOSettings".ToList();

		internal void NormalizeSettings()
		{
			this._settings = this._settings ?? JObject.Parse(string.IsNullOrWhiteSpace(this.OtherSettings) ? "{}" : this.OtherSettings);
			Desktop.SettingProperties.ForEach(name => this._settings[name] = this.GetProperty(name)?.ToJson());
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
			else if (Desktop.SettingProperties.ToHashSet().Contains(name))
			{
				this._settings = this._settings ?? JObject.Parse(string.IsNullOrWhiteSpace(this.OtherSettings) ? "{}" : this.OtherSettings);
				this._settings[name] = this.GetProperty(name)?.ToJson();
			}
		}
	}
}
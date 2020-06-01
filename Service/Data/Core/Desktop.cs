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
	[Serializable, BsonIgnoreExtraElements]
	[DebuggerDisplay("ID = {ID}, Title = {Title}")]
	[Entity(CollectionName = "Desktops", TableName = "T_Portals_Desktops", CacheClass = typeof(Utility), CacheName = "Cache", Searchable = true)]
	public sealed class Desktop : Repository<Desktop>, INestedObject
	{
		public Desktop() : base() { }

		[Property(MaxLength = 32)]
		[Sortable(IndexName = "Management")]
		[FormControl(Segment = "basic", ControlType = "Lookup", Multiple = false, Label = "{{portals.desktops.controls.[name].label}}", PlaceHolder = "{{portals.desktops.controls.[name].placeholder}}", Description = "{{portals.desktops.controls.[name].description}}")]
		public string ParentID { get; set; }

		[Property(MaxLength = 250, NotNull = true, NotEmpty = true)]
		[Sortable(IndexName = "Title")]
		[Searchable]
		[FormControl(Segment = "basic", Label = "{{portals.desktops.controls.[name].label}}", PlaceHolder = "{{portals.desktops.controls.[name].placeholder}}", Description = "{{portals.desktops.controls.[name].description}}")]
		public override string Title { get; set; }

		[Property(MaxLength = 100, NotNull = true, NotEmpty = true)]
		[Sortable(UniqueIndexName = "Alias")]
		[Searchable]
		[FormControl(Segment = "basic", Label = "{{portals.desktops.controls.[name].label}}", PlaceHolder = "{{portals.desktops.controls.[name].placeholder}}", Description = "{{portals.desktops.controls.[name].description}}")]
		public string Alias { get; set; }

		[Property(MaxLength = 250)]
		[Sortable(IndexName = "Aliases")]
		[Searchable]
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

		[Ignore, BsonIgnore, XmlIgnore]
		[FormControl(Segment = "seo", Label = "{{portals.desktops.controls.[name].label}}", PlaceHolder = "{{portals.desktops.controls.[name].placeholder}}", Description = "{{portals.desktops.controls.[name].description}}")]
		public Settings.SEO SEOSettings { get; set; }

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
		public string WorkingTheme => this.Theme ?? this.Organization?.DefaultSite?.Theme ?? this.Organization?.Theme ?? "default";

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

		internal List<Desktop> FindChildren(bool notifyPropertyChanged = true, List<Desktop> desktops = null)
		{
			if (this._childrenIDs == null)
			{
				desktops = desktops ?? (this.SystemID ?? "").FindDesktops(this.ID);
				this._childrenIDs = desktops.Select(desktop => desktop.ID).ToList();
				if (notifyPropertyChanged)
					this.NotifyPropertyChanged("ChildrenIDs");
				return desktops;
			}
			return this._childrenIDs.Select(id => id.GetDesktopByID()).ToList();
		}

		internal async Task<List<Desktop>> FindChildrenAsync(CancellationToken cancellationToken = default, bool notifyPropertyChanged = true)
			=> this._childrenIDs == null
				? this.FindChildren(notifyPropertyChanged, await (this.SystemID ?? "").FindDesktopsAsync(this.ID, cancellationToken).ConfigureAwait(false))
				: this._childrenIDs.Select(id => id.GetDesktopByID()).ToList();

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public List<Desktop> Children => this.FindChildren();

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		List<INestedObject> INestedObject.Children => this.Children?.Select(desktop => desktop as INestedObject).ToList();

		internal List<Portlet> _portlets;

		internal List<Portlet> FindPortlets(bool notifyPropertyChanged = true, List<Portlet> portlets = null)
		{
			this._portlets = portlets ?? (this.ID ?? "").FindPortlets();
			if (notifyPropertyChanged)
				this.NotifyPropertyChanged("Portlets");
			return this._portlets;
		}

		internal async Task<List<Portlet>> FindPortletsAsync(CancellationToken cancellationToken = default, bool notifyPropertyChanged = true)
			=> this._portlets ?? this.FindPortlets(notifyPropertyChanged, await (this.ID ?? "").FindPortletsAsync(cancellationToken).ConfigureAwait(false));

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public List<Portlet> Portlets => this._portlets ?? (this._portlets = this.FindPortlets());

		public override JObject ToJson(bool addTypeOfExtendedProperties = false, Action<JObject> onCompleted = null)
			=> this.ToJson(false, addTypeOfExtendedProperties, onCompleted);

		public JObject ToJson(bool addChildrenAndPortlets, bool addTypeOfExtendedProperties, Action<JObject> onCompleted = null)
			=> base.ToJson(addTypeOfExtendedProperties, json =>
			{
				json.Remove("Privileges");
				json.Remove("OriginalPrivileges");
				if (addChildrenAndPortlets)
				{
					json["Children"] = this.Children?.Where(desktop => desktop != null).Select(desktop => desktop.ToJson(true, false)).ToJArray();
					json["Portlets"] = this.Portlets?.Where(portlet => portlet != null).OrderBy(portlet => portlet.Zone).ThenBy(portlet => portlet.OrderIndex).Select(portlet => portlet.ToJson()).ToJArray();
				}
				onCompleted?.Invoke(json);
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
			DesktopProcessor.ExtraProperties.ForEach(name => this._json[name] = this.GetProperty(name)?.ToJson());
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
			else if (DesktopProcessor.ExtraProperties.Contains(name))
			{
				this._json = this._json ?? JObject.Parse(string.IsNullOrWhiteSpace(this.Extras) ? "{}" : this.Extras);
				this._json[name] = this.GetProperty(name)?.ToJson();
			}
			else if (name.IsEquals("ChildrenIDs") || name.IsEquals("Portlets"))
				Utility.Cache.Set(this);
		}

		public async Task<string> GetTemplateAsync(CancellationToken cancellationToken = default)
			=> !string.IsNullOrWhiteSpace(this.Template)
				? this.Template
				: await Utility.GetTemplateAsync("desktop.xml", this.WorkingTheme, null, null, cancellationToken).ConfigureAwait(false) ?? await Utility.GetTemplateAsync("desktop.xml", null, null, null, cancellationToken).ConfigureAwait(false);
	}
}
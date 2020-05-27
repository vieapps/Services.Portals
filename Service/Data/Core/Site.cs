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
using net.vieapps.Components.Security;
using net.vieapps.Components.Repository;
using net.vieapps.Components.Utility;
#endregion

namespace net.vieapps.Services.Portals
{
	[Serializable, BsonIgnoreExtraElements]
	[DebuggerDisplay("ID = {ID}, Title = {Title}")]
	[Entity(CollectionName = "Sites", TableName = "T_Portals_Sites", CacheClass = typeof(Utility), CacheName = "Cache", Searchable = true)]
	public sealed class Site : Repository<Site>, IPortalObject
	{
		public Site() : base() { }

		[Property(MaxLength = 250, NotNull = true, NotEmpty = true)]
		[Sortable(IndexName = "Title")]
		[Searchable]
		[FormControl(Segment = "basic", Label = "{{portals.sites.controls.[name].label}}", PlaceHolder = "{{portals.sites.controls.[name].placeholder}}", Description = "{{portals.sites.controls.[name].description}}")]
		public override string Title { get; set; }

		[Searchable]
		[FormControl(Segment = "basic", ControlType = "TextArea", Label = "{{portals.sites.controls.[name].label}}", PlaceHolder = "{{portals.sites.controls.[name].placeholder}}", Description = "{{portals.sites.controls.[name].description}}")]
		public string Description { get; set; }

		[JsonConverter(typeof(StringEnumConverter)), BsonRepresentation(MongoDB.Bson.BsonType.String)]
		[Sortable(IndexName = "Management")]
		[FormControl(Segment = "basic", Label = "{{portals.sites.controls.[name].label}}", PlaceHolder = "{{portals.sites.controls.[name].placeholder}}", Description = "{{portals.sites.controls.[name].description}}")]
		public ApprovalStatus Status { get; set; } = ApprovalStatus.Pending;

		[Property(MaxLength = 100, NotNull = true, NotEmpty = true)]
		[Sortable(UniqueIndexName = "Domains")]
		[Searchable]
		[FormControl(Segment = "basic", Label = "{{portals.sites.controls.[name].label}}", PlaceHolder = "{{portals.sites.controls.[name].placeholder}}", Description = "{{portals.sites.controls.[name].description}}")]
		public string PrimaryDomain { get; set; } = "company.com";

		[Property(MaxLength = 50, NotNull = true, NotEmpty = true)]
		[Sortable(UniqueIndexName = "Domains")]
		[FormControl(Segment = "basic", Label = "{{portals.sites.controls.[name].label}}", PlaceHolder = "{{portals.sites.controls.[name].placeholder}}", Description = "{{portals.sites.controls.[name].description}}")]
		public string SubDomain { get; set; } = "*";

		[Property(MaxLength = 1000)]
		[Sortable(IndexName = "OtherDomains")]
		[Searchable]
		[FormControl(Segment = "basic", Label = "{{portals.sites.controls.[name].label}}", PlaceHolder = "{{portals.sites.controls.[name].placeholder}}", Description = "{{portals.sites.controls.[name].description}}")]
		public string OtherDomains { get; set; }

		[Ignore, BsonIgnore]
		[FormControl(Segment = "basic", Label = "{{portals.sites.controls.[name].label}}", PlaceHolder = "{{portals.sites.controls.[name].placeholder}}", Description = "{{portals.sites.controls.[name].description}}")]
		public bool AlwaysUseHTTPs { get; set; } = false;

		[Property(MaxLength = 5)]
		[FormControl(Segment = "display", ControlType = "Select", Label = "{{portals.sites.controls.[name].label}}", PlaceHolder = "{{portals.sites.controls.[name].placeholder}}", Description = "{{portals.sites.controls.[name].description}}")]
		public string Language { get; set; } = "vi-VN";

		[Property(MaxLength = 100)]
		[FormControl(Segment = "display", ControlType = "Select", Label = "{{portals.sites.controls.[name].label}}", PlaceHolder = "{{portals.sites.controls.[name].placeholder}}", Description = "{{portals.sites.controls.[name].description}}")]
		public string Theme { get; set; }

		[Property(MaxLength = 32)]
		[FormControl(Segment = "display", ControlType = "Lookup", Label = "{{portals.sites.controls.[name].label}}", PlaceHolder = "{{portals.sites.controls.[name].placeholder}}", Description = "{{portals.sites.controls.[name].description}}")]
		public string HomeDesktopID { get; set; }

		[Property(MaxLength = 32)]
		[FormControl(Segment = "display", ControlType = "Lookup", Label = "{{portals.sites.controls.[name].label}}", PlaceHolder = "{{portals.sites.controls.[name].placeholder}}", Description = "{{portals.sites.controls.[name].description}}")]
		public string SearchDesktopID { get; set; }

		[Ignore, BsonIgnore]
		[FormControl(Segment = "display", Label = "{{portals.sites.controls.[name].label}}", PlaceHolder = "{{portals.sites.controls.[name].placeholder}}", Description = "{{portals.sites.controls.[name].description}}")]
		public Settings.UI UISettings { get; set; }

		[Ignore, BsonIgnore]
		[FormControl(Segment = "display", ControlType = "Lookup", Label = "{{portals.sites.controls.[name].label}}", PlaceHolder = "{{portals.sites.controls.[name].placeholder}}", Description = "{{portals.sites.controls.[name].description}}")]
		public string IconURI { get; set; }

		[Ignore, BsonIgnore]
		[FormControl(Segment = "display", ControlType = "Lookup", Label = "{{portals.sites.controls.[name].label}}", PlaceHolder = "{{portals.sites.controls.[name].placeholder}}", Description = "{{portals.sites.controls.[name].description}}")]
		public string CoverURI { get; set; }

		[Ignore, BsonIgnore]
		[FormControl(Segment = "display", ControlType = "TextArea", Label = "{{portals.sites.controls.[name].label}}", PlaceHolder = "{{portals.sites.controls.[name].placeholder}}", Description = "{{portals.sites.controls.[name].description}}")]
		public string MetaTags { get; set; }

		[Ignore, BsonIgnore]
		[FormControl(Segment = "display", ControlType = "TextArea", Label = "{{portals.sites.controls.[name].label}}", PlaceHolder = "{{portals.sites.controls.[name].placeholder}}", Description = "{{portals.sites.controls.[name].description}}")]
		public string Scripts { get; set; }

		[Ignore, BsonIgnore]
		[FormControl(Segment = "seo", Label = "{{portals.sites.controls.[name].label}}", PlaceHolder = "{{portals.sites.controls.[name].placeholder}}", Description = "{{portals.sites.controls.[name].description}}")]
		public bool RedirectToNoneWWW { get; set; } = true;

		[Ignore, BsonIgnore]
		[FormControl(Segment = "seo", Label = "{{portals.sites.controls.[name].label}}", PlaceHolder = "{{portals.sites.controls.[name].placeholder}}", Description = "{{portals.sites.controls.[name].description}}")]
		public Settings.SEOInfo SEOInfo { get; set; }

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
		public override string RepositoryID { get; set; }

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public override string RepositoryEntityID { get; set; }

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public override Privileges OriginalPrivileges { get; set; }

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public override Privileges WorkingPrivileges => this.Organization?.WorkingPrivileges;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public string OrganizationID => this.SystemID;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public Organization Organization => (this.OrganizationID ?? "").GetOrganizationByID();

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public override RepositoryBase Parent => this.Organization;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		IPortalObject IPortalObject.Parent => this.Organization;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public Desktop HomeDesktop => (this.HomeDesktopID ?? "").GetDesktopByID() ?? this.Organization?.HomeDesktop;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public Desktop SearchDesktop => (this.SearchDesktopID ?? "").GetDesktopByID() ?? this.Organization?.SearchDesktop;

		public override JObject ToJson(bool addTypeOfExtendedProperties = false, Action<JObject> onPreCompleted = null)
			=> base.ToJson(addTypeOfExtendedProperties, json =>
			{
				json.Remove("Privileges");
				json.Remove("OriginalPrivileges");
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
			this.SEOInfo?.Normalize();
			this.SEOInfo = this.SEOInfo != null && string.IsNullOrWhiteSpace(this.SEOInfo.Title) && string.IsNullOrWhiteSpace(this.SEOInfo.Description) && string.IsNullOrWhiteSpace(this.SEOInfo.Keywords) ? null : this.SEOInfo;
			this._json = this._json ?? JObject.Parse(string.IsNullOrWhiteSpace(this.Extras) ? "{}" : this.Extras);
			SiteProcessor.ExtraProperties.ForEach(name => this._json[name] = this.GetProperty(name)?.ToJson());
			this._extras = this._json.ToString(Formatting.None);
		}

		public override void ProcessPropertyChanged(string name)
		{
			if (name.IsEquals("Extras"))
			{
				this._json = this._json ?? JObject.Parse(string.IsNullOrWhiteSpace(this.Extras) ? "{}" : this.Extras);
				this.AlwaysUseHTTPs = this._json["AlwaysUseHTTPs"] != null ? this._json["AlwaysUseHTTPs"].FromJson<bool>() : false;
				this.UISettings = this._json["UISettings"]?.FromJson<Settings.UI>();
				this.IconURI = this._json["IconURI"]?.FromJson<string>();
				this.CoverURI = this._json["CoverURI"]?.FromJson<string>();
				this.MetaTags = this._json["MetaTags"]?.FromJson<string>();
				this.Scripts = this._json["Scripts"]?.FromJson<string>();
				this.RedirectToNoneWWW = this._json["RedirectToNoneWWW"] != null ? this._json["RedirectToNoneWWW"].FromJson<bool>() : true;
				this.SEOInfo = this._json["SEOInfo"]?.FromJson<Settings.SEOInfo>();
			}
			else if (SiteProcessor.ExtraProperties.Contains(name))
			{
				this._json = this._json ?? JObject.Parse(string.IsNullOrWhiteSpace(this.Extras) ? "{}" : this.Extras);
				this._json[name] = this.GetProperty(name)?.ToJson();
			}
		}
	}
}
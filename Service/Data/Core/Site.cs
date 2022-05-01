#region Related components
using System;
using System.Diagnostics;
using System.Xml.Serialization;
using MsgPack.Serialization;
using MongoDB.Bson.Serialization.Attributes;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Converters;
using net.vieapps.Components.Security;
using net.vieapps.Components.Repository;
using net.vieapps.Components.Utility;
#endregion

namespace net.vieapps.Services.Portals
{
	[BsonIgnoreExtraElements, DebuggerDisplay("ID = {ID}, Title = {Title}")]
	[Entity(CollectionName = "Sites", TableName = "T_Portals_Sites", CacheClass = typeof(Utility), CacheName = "Cache", Searchable = true)]
	public sealed class Site : Repository<Site>, IPortalObject
	{
		public Site() : base() { }

		[Sortable(IndexName = "Title"), Searchable]
		[Property(MaxLength = 250, NotNull = true, NotEmpty = true)]
		[FormControl(Segment = "basic", Label = "{{portals.sites.controls.[name].label}}", PlaceHolder = "{{portals.sites.controls.[name].placeholder}}", Description = "{{portals.sites.controls.[name].description}}")]
		public override string Title { get; set; }

		[Searchable]
		[FormControl(Segment = "basic", ControlType = "TextArea", Label = "{{portals.sites.controls.[name].label}}", PlaceHolder = "{{portals.sites.controls.[name].placeholder}}", Description = "{{portals.sites.controls.[name].description}}")]
		public string Description { get; set; }

		[JsonConverter(typeof(StringEnumConverter)), BsonRepresentation(MongoDB.Bson.BsonType.String)]
		[Sortable(IndexName = "Management")]
		[FormControl(Segment = "basic", Label = "{{portals.sites.controls.[name].label}}", PlaceHolder = "{{portals.sites.controls.[name].placeholder}}", Description = "{{portals.sites.controls.[name].description}}")]
		public ApprovalStatus Status { get; set; } = ApprovalStatus.Pending;

		[Sortable(UniqueIndexName = "Domains"), Searchable]
		[Property(MaxLength = 100, NotNull = true, NotEmpty = true)]
		[FormControl(Segment = "basic", Label = "{{portals.sites.controls.[name].label}}", PlaceHolder = "{{portals.sites.controls.[name].placeholder}}", Description = "{{portals.sites.controls.[name].description}}")]
		public string PrimaryDomain { get; set; } = "company.com";

		[Sortable(UniqueIndexName = "Domains")]
		[Property(MaxLength = 50, NotNull = true, NotEmpty = true)]
		[FormControl(Segment = "basic", Label = "{{portals.sites.controls.[name].label}}", PlaceHolder = "{{portals.sites.controls.[name].placeholder}}", Description = "{{portals.sites.controls.[name].description}}")]
		public string SubDomain { get; set; } = "*";

		[Sortable(IndexName = "OtherDomains"), Searchable]
		[Property(MaxLength = 1000)]
		[FormControl(Segment = "basic", Label = "{{portals.sites.controls.[name].label}}", PlaceHolder = "{{portals.sites.controls.[name].placeholder}}", Description = "{{portals.sites.controls.[name].description}}")]
		public string OtherDomains { get; set; }

		[Ignore, BsonIgnore]
		[FormControl(Segment = "basic", Label = "{{portals.sites.controls.[name].label}}", PlaceHolder = "{{portals.sites.controls.[name].placeholder}}", Description = "{{portals.sites.controls.[name].description}}")]
		public bool AlwaysUseHTTPs { get; set; } = false;

		[Ignore, BsonIgnore]
		[FormControl(Segment = "basic", Label = "{{portals.sites.controls.[name].label}}", PlaceHolder = "{{portals.sites.controls.[name].placeholder}}", Description = "{{portals.sites.controls.[name].description}}")]
		public bool AlwaysReturnHTTPs { get; set; } = false;

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
		public string Stylesheets { get; set; }

		[FormControl(Segment = "display", ControlType = "TextArea", Label = "{{portals.sites.controls.[name].label}}", PlaceHolder = "{{portals.sites.controls.[name].placeholder}}", Description = "{{portals.sites.controls.[name].description}}")]
		public string ScriptLibraries { get; set; }

		[Ignore, BsonIgnore]
		[FormControl(Segment = "display", ControlType = "TextArea", Label = "{{portals.sites.controls.[name].label}}", PlaceHolder = "{{portals.sites.controls.[name].placeholder}}", Description = "{{portals.sites.controls.[name].description}}")]
		public string Scripts { get; set; }

		[Ignore, BsonIgnore]
		[FormControl(Segment = "seo", Label = "{{portals.sites.controls.[name].label}}", PlaceHolder = "{{portals.sites.controls.[name].placeholder}}", Description = "{{portals.sites.controls.[name].description}}")]
		public bool RedirectToNoneWWW { get; set; } = true;

		[Ignore, BsonIgnore]
		[FormControl(Segment = "seo", Label = "{{portals.sites.controls.[name].label}}", PlaceHolder = "{{portals.sites.controls.[name].placeholder}}", Description = "{{portals.sites.controls.[name].description}}")]
		public bool UseInlineStylesheets { get; set; } = false;

		[Ignore, BsonIgnore]
		[FormControl(Segment = "seo", Label = "{{portals.sites.controls.[name].label}}", PlaceHolder = "{{portals.sites.controls.[name].placeholder}}", Description = "{{portals.sites.controls.[name].description}}")]
		public bool UseInlineScripts { get; set; } = false;

		[Ignore, BsonIgnore]
		[FormControl(Segment = "seo", Label = "{{portals.sites.controls.[name].label}}", PlaceHolder = "{{portals.sites.controls.[name].placeholder}}", Description = "{{portals.sites.controls.[name].description}}")]
		public Settings.SEOInfo SEOInfo { get; set; }

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

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore, MessagePackIgnore]
		public override Privileges OriginalPrivileges { get; set; }

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore, MessagePackIgnore]
		public override Privileges WorkingPrivileges => this.Organization?.WorkingPrivileges;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public string OrganizationID => this.SystemID;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore, MessagePackIgnore]
		public Organization Organization => (this.OrganizationID ?? "").GetOrganizationByID();

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore, MessagePackIgnore]
		public override RepositoryBase Parent => this.Organization;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore, MessagePackIgnore]
		IPortalObject IPortalObject.Parent => this.Organization;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore, MessagePackIgnore]
		public Desktop HomeDesktop => (this.HomeDesktopID ?? "").GetDesktopByID() ?? this.Organization?.HomeDesktop;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore, MessagePackIgnore]
		public Desktop SearchDesktop => (this.SearchDesktopID ?? "").GetDesktopByID() ?? this.Organization?.SearchDesktop;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore, MessagePackIgnore]
		public string Host => $"{this.SubDomain}.{this.PrimaryDomain}".Replace("*.", "www.").Replace("www.www.", "www.").Replace("*", "");

		public string GetURL(string host = null, string requestedURL = null)
			=> $"http{(this.AlwaysUseHTTPs || this.AlwaysReturnHTTPs || (requestedURL ?? "").IsStartsWith("https://") ? "s" : "")}://{host ?? this.Host}";

		public override JObject ToJson(bool addTypeOfExtendedProperties = false, Action<JObject> onCompleted = null)
			=> base.ToJson(addTypeOfExtendedProperties, json =>
			{
				json.Remove("Privileges");
				json.Remove("OriginalPrivileges");
				onCompleted?.Invoke(json);
			});

		internal void NormalizeExtras()
		{
			this.UISettings?.Normalize(uiSettings => uiSettings.BackgroundImageURI = uiSettings.BackgroundImageURI?.Replace(StringComparison.OrdinalIgnoreCase, $"{this.Organization.FakeFilesHttpURI ?? Utility.FilesHttpURI}/", "~~/"));
			this.UISettings = this.UISettings != null && string.IsNullOrWhiteSpace(this.UISettings.Padding) && string.IsNullOrWhiteSpace(this.UISettings.Margin) && string.IsNullOrWhiteSpace(this.UISettings.Width) && string.IsNullOrWhiteSpace(this.UISettings.Height) && string.IsNullOrWhiteSpace(this.UISettings.Color) && string.IsNullOrWhiteSpace(this.UISettings.BackgroundColor) && string.IsNullOrWhiteSpace(this.UISettings.BackgroundImageURI) && string.IsNullOrWhiteSpace(this.UISettings.BackgroundImageRepeat) && string.IsNullOrWhiteSpace(this.UISettings.BackgroundImagePosition) && string.IsNullOrWhiteSpace(this.UISettings.BackgroundImageSize) && string.IsNullOrWhiteSpace(this.UISettings.Css) && string.IsNullOrWhiteSpace(this.UISettings.Style) ? null : this.UISettings;
			this.IconURI = string.IsNullOrWhiteSpace(this.IconURI) ? null : this.IconURI.Trim().Replace(StringComparison.OrdinalIgnoreCase, $"{this.Organization.FakeFilesHttpURI ?? Utility.FilesHttpURI}/", "~~/");
			this.CoverURI = string.IsNullOrWhiteSpace(this.CoverURI) ? null : this.CoverURI.Trim().Replace(StringComparison.OrdinalIgnoreCase, $"{this.Organization.FakeFilesHttpURI ?? Utility.FilesHttpURI}/", "~~/");
			this.MetaTags = string.IsNullOrWhiteSpace(this.MetaTags) ? null : this.MetaTags.Trim();
			this.Stylesheets = string.IsNullOrWhiteSpace(this.Stylesheets) ? null : this.Stylesheets.Trim();
			this.ScriptLibraries = string.IsNullOrWhiteSpace(this.ScriptLibraries) ? null : this.ScriptLibraries.Trim();
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
				this.AlwaysUseHTTPs = this._json["AlwaysUseHTTPs"] != null && this._json["AlwaysUseHTTPs"].As<bool>();
				this.AlwaysReturnHTTPs = this._json["AlwaysReturnHTTPs"] != null && this._json["AlwaysReturnHTTPs"].As<bool>();
				this.UISettings = this._json["UISettings"]?.As<Settings.UI>();
				this.IconURI = this._json["IconURI"]?.As<string>();
				this.CoverURI = this._json["CoverURI"]?.As<string>();
				this.MetaTags = this._json["MetaTags"]?.As<string>();
				this.Stylesheets = this._json["Stylesheets"]?.As<string>();
				this.ScriptLibraries = this._json["ScriptLibraries"]?.As<string>();
				this.Scripts = this._json["Scripts"]?.As<string>();
				this.RedirectToNoneWWW = this._json["RedirectToNoneWWW"] != null && this._json["RedirectToNoneWWW"].As<bool>();
				this.UseInlineStylesheets = this._json["UseInlineStylesheets"] != null && this._json["UseInlineStylesheets"].As<bool>();
				this.UseInlineScripts = this._json["UseInlineScripts"] != null && this._json["UseInlineScripts"].As<bool>();
				this.SEOInfo = this._json["SEOInfo"]?.As<Settings.SEOInfo>();
			}
			else if (SiteProcessor.ExtraProperties.Contains(name))
			{
				this._json = this._json ?? JObject.Parse(string.IsNullOrWhiteSpace(this.Extras) ? "{}" : this.Extras);
				this._json[name] = this.GetProperty(name)?.ToJson();
			}
		}
	}
}
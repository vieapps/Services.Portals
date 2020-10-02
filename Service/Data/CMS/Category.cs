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
	[Entity(CollectionName = "CMS_Categories", TableName = "T_Portals_CMS_Categories", CacheClass = typeof(Utility), CacheName = "Cache", Searchable = true, ID = "B0000000000000000000000000000001", Title = "Category", Description = "Categorizing the CMS contents", ObjectNamePrefix = "CMS.", Portlets = false)]
	public sealed class Category : Repository<Category>, IBusinessObject, INestedObject, IAliasEntity
	{
		public Category() : base() { }

		[Property(MaxLength = 32)]
		[Sortable(IndexName = "Management")]
		[FormControl(Segment = "basic", ControlType = "Lookup", Multiple = false, Label = "{{portals.cms.categories.controls.[name].label}}", PlaceHolder = "{{portals.cms.categories.controls.[name].placeholder}}", Description = "{{portals.cms.categories.controls.[name].description}}")]
		public string ParentID { get; set; }

		[Property(MaxLength = 250, NotNull = true, NotEmpty = true)]
		[Sortable(IndexName = "Title")]
		[Searchable]
		[FormControl(Segment = "basic", Label = "{{portals.cms.categories.controls.[name].label}}", PlaceHolder = "{{portals.cms.categories.controls.[name].placeholder}}", Description = "{{portals.cms.categories.controls.[name].description}}")]
		public override string Title { get; set; }

		[Property(MaxLength = 100, NotNull = true, NotEmpty = true)]
		[Alias]
		[Searchable]
		[FormControl(Segment = "basic", Label = "{{portals.cms.categories.controls.[name].label}}", PlaceHolder = "{{portals.cms.categories.controls.[name].placeholder}}", Description = "{{portals.cms.categories.controls.[name].description}}")]
		public string Alias { get; set; }

		[Searchable]
		[FormControl(Segment = "basic", ControlType = "TextArea", Label = "{{portals.cms.categories.controls.[name].label}}", PlaceHolder = "{{portals.cms.categories.controls.[name].placeholder}}", Description = "{{portals.cms.categories.controls.[name].description}}")]
		public string Description { get; set; }

		[Property(MaxLength = 32)]
		[FormControl(Segment = "basic", ControlType = "Lookup", Label = "{{portals.cms.categories.controls.[name].label}}", PlaceHolder = "{{portals.cms.categories.controls.[name].placeholder}}", Description = "{{portals.cms.categories.controls.[name].description}}")]
		public string DesktopID { get; set; }

		[JsonConverter(typeof(StringEnumConverter)), BsonRepresentation(MongoDB.Bson.BsonType.String)]
		[FormControl(Segment = "basic", Label = "{{portals.cms.categories.controls.[name].label}}", PlaceHolder = "{{portals.cms.categories.controls.[name].placeholder}}", Description = "{{portals.cms.categories.controls.[name].description}}")]
		public OpenBy OpenBy { get; set; } = OpenBy.DesktopWithAlias;

		[Property(MaxLength = 1000)]
		[FormControl(Segment = "basic", Label = "{{portals.cms.categories.controls.[name].label}}", PlaceHolder = "{{portals.cms.categories.controls.[name].placeholder}}", Description = "{{portals.cms.categories.controls.[name].description}}")]
		public string SpecifiedURI { get; set; }

		[Sortable(IndexName = "Management")]
		[FormControl(Segment = "basic", ReadOnly = true, Label = "{{portals.cms.categories.controls.[name].label}}", PlaceHolder = "{{portals.cms.categories.controls.[name].placeholder}}", Description = "{{portals.cms.categories.controls.[name].description}}")]
		public int OrderIndex { get; set; } = 0;

		[Ignore, BsonIgnore, XmlIgnore]
		public Settings.Notifications Notifications { get; set; } = new Settings.Notifications();

		[Ignore, BsonIgnore, XmlIgnore]
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

		[Property(MaxLength = 32, NotNull = true, NotEmpty = true)]
		[Sortable(IndexName = "Management")]
		[FormControl(Hidden = true)]
		public override string RepositoryID { get; set; }

		[Property(MaxLength = 32, NotNull = true, NotEmpty = true)]
		[Sortable(IndexName = "Management")]
		[FormControl(Hidden = true)]
		public override string RepositoryEntityID { get; set; }

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public ApprovalStatus Status => ApprovalStatus.Published;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public string OrganizationID => this.SystemID;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public Organization Organization => (this.OrganizationID ?? "").GetOrganizationByID();

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		IPortalObject IBusinessObject.Organization => this.Organization;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public string ModuleID => this.RepositoryID;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public Module Module => (this.ModuleID ?? "").GetModuleByID();

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		IPortalModule IBusinessObject.Module => this.Module;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public string ContentTypeID => this.RepositoryEntityID;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public ContentType ContentType => (this.ContentTypeID ?? "").GetContentTypeByID();

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		IPortalContentType IBusinessObject.ContentType => this.ContentType;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public Category ParentCategory => CategoryProcessor.GetCategoryByID(this.ParentID);

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public override RepositoryBase Parent => this.ParentCategory ?? this.Module as RepositoryBase;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		IBusinessEntity IBusinessEntity.Parent => this.ParentCategory;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		IPortalObject IPortalObject.Parent => this.ParentCategory ?? this.Module as IPortalObject;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		INestedObject INestedObject.Parent => this.ParentCategory;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public string FullTitle
		{
			get
			{
				var parent = this.ParentCategory;
				return (parent == null ? "" : $"{parent.FullTitle} > ") + this.Title;
			}
		}

		internal List<string> _childrenIDs;

		internal List<Category> FindChildren(bool notifyPropertyChanged = true, List<Category> categories = null)
		{
			if (this._childrenIDs == null)
			{
				categories = categories ?? (this.SystemID ?? "").FindCategories(this.RepositoryID, this.RepositoryEntityID, this.ID);
				this._childrenIDs = categories.Select(category => category.ID).ToList();
				if (notifyPropertyChanged)
					this.NotifyPropertyChanged("ChildrenIDs");
				return categories;
			}
			return this._childrenIDs.Select(id => id.GetCategoryByID()).ToList();
		}

		internal async Task<List<Category>> FindChildrenAsync(CancellationToken cancellationToken = default, bool notifyPropertyChanged = true)
			=> this._childrenIDs == null
				? this.FindChildren(notifyPropertyChanged, await (this.SystemID ?? "").FindCategoriesAsync(this.RepositoryID, this.RepositoryEntityID, this.ID, cancellationToken).ConfigureAwait(false))
				: this._childrenIDs.Select(id => id.GetCategoryByID()).ToList();

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public List<Category> Children => this.FindChildren();

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		List<INestedObject> INestedObject.Children => this.Children?.Select(category => category as INestedObject).ToList();

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public Desktop Desktop => (this.DesktopID ?? "").GetDesktopByID();

		public override JObject ToJson(bool addTypeOfExtendedProperties = false, Action<JObject> onCompleted = null)
			=> this.ToJson(false, addTypeOfExtendedProperties, onCompleted);

		public JObject ToJson(bool addChildren, bool addTypeOfExtendedProperties, Action<JObject> onCompleted = null, Action<JObject> onChildrenCompleted = null, int level = 1, int maxLevel = 0)
			=> base.ToJson(addTypeOfExtendedProperties, json =>
			{
				if (addChildren && (maxLevel < 1 || level < maxLevel))
					json["Children"] = this.Children?.Where(category => category != null).OrderBy(category => category.OrderIndex).Select(category => category.ToJson(addChildren, addTypeOfExtendedProperties, onChildrenCompleted, onChildrenCompleted, level + 1, maxLevel)).ToJArray();
				onCompleted?.Invoke(json);
			});

		internal void NormalizeExtras()
		{
			this.Notifications?.Normalize();
			this.Notifications = this.Notifications != null && this.Notifications.Events == null && this.Notifications.Methods == null && this.Notifications.Emails == null && this.Notifications.EmailsByApprovalStatus == null && this.Notifications.EmailsWhenPublish == null && this.Notifications.WebHooks == null ? null : this.Notifications;
			this.EmailSettings?.Normalize();
			this.EmailSettings = this.EmailSettings != null && this.EmailSettings.Sender == null && this.EmailSettings.Signature == null && this.EmailSettings.Smtp == null ? null : this.EmailSettings;
			this._json = this._json ?? JObject.Parse(string.IsNullOrWhiteSpace(this.Extras) ? "{}" : this.Extras);
			CategoryProcessor.ExtraProperties.ForEach(name => this._json[name] = this.GetProperty(name)?.ToJson());
			this._exras = this._json.ToString(Formatting.None);
		}

		public override void ProcessPropertyChanged(string name)
		{
			if (name.IsEquals("Extras"))
			{
				this._json = this._json ?? JObject.Parse(string.IsNullOrWhiteSpace(this.Extras) ? "{}" : this.Extras);
				this.Notifications = this._json["Notifications"]?.FromJson<Settings.Notifications>();
				this.EmailSettings = this._json["EmailSettings"]?.FromJson<Settings.Email>();
			}
			else if (CategoryProcessor.ExtraProperties.Contains(name))
			{
				this._json = this._json ?? JObject.Parse(string.IsNullOrWhiteSpace(this.Extras) ? "{}" : this.Extras);
				this._json[name] = this.GetProperty(name)?.ToJson();
			}
			else if (name.IsEquals("ChildrenIDs"))
				Utility.Cache.SetAsync(this).Run();
		}

		public string GetURL(string desktop = null, bool addPageNumberHolder = false)
		{
			var alwaysUseHtmlSuffix = this.Organization != null && this.Organization.AlwaysUseHtmlSuffix;
			var url = this.OpenBy.Equals(OpenBy.DesktopOnly)
				? $"~/{this.Desktop?.Alias ?? desktop ?? this.Module?.Desktop?.Alias ?? "-default"}{(alwaysUseHtmlSuffix ? ".html" : "")}".ToLower()
				: this.OpenBy.Equals(OpenBy.SpecifiedURI)
					? this.SpecifiedURI ?? "~/"
					: $"~/{this.Desktop?.Alias ?? desktop ?? this.Module?.Desktop?.Alias ?? "-default"}/{this.Alias}" + (addPageNumberHolder ? "/{{pageNumber}}" : "") + $"{(alwaysUseHtmlSuffix ? ".html" : "")}".ToLower();
			if (url.StartsWith("~/") && url.IsEndsWith("/default.aspx") && alwaysUseHtmlSuffix)
				url = url.Replace(StringComparison.OrdinalIgnoreCase, "/default.aspx", ".html");
			return url.Equals("~.html") ? "~/index.html" : url;
		}

		public IAliasEntity GetByAlias(string repositoryEntityID, string alias, string parentIdentity = null)
			=> (repositoryEntityID ?? "").GetCategoryByAlias(alias);

		public async Task<IAliasEntity> GetByAliasAsync(string repositoryEntityID, string alias, string parentIdentity = null, CancellationToken cancellationToken = default)
			=> await (repositoryEntityID ?? "").GetCategoryByAliasAsync(alias, cancellationToken).ConfigureAwait(false);
	}

	[Serializable]
	public enum OpenBy
	{
		DesktopWithAlias,
		DesktopOnly,
		SpecifiedURI
	}
}
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
	[Entity(CollectionName = "CMS_Categories", TableName = "T_Portals_CMS_Categories", CacheClass = typeof(Utility), CacheName = "Cache", Searchable = true, ObjectName = "Category", ID = "B0000000000000000000000000000001", Title = "Category", Description = "Categorizing the CMS contents", MultipleIntances = false, Extendable = false, Indexable = false)]
	public sealed class Category : Repository<Category>, IBusinessObject, INestedObject, IAliasEntity
	{
		public Category() : base() { }

		[Property(MaxLength = 32)]
		[Sortable(IndexName = "Management")]
		[FormControl(Segment = "basic", ControlType = "Lookup", Multiple = false, Label = "{{portals.cms.category.controls.[name].label}}", PlaceHolder = "{{portals.cms.category.controls.[name].placeholder}}", Description = "{{portals.cms.category.controls.[name].description}}")]
		public string ParentID { get; set; }

		[Sortable(IndexName = "Management")]
		[FormControl(Hidden = true)]
		public int OrderIndex { get; set; } = 0;

		[Property(MaxLength = 250, NotNull = true, NotEmpty = true)]
		[Sortable(IndexName = "Title")]
		[Searchable]
		[FormControl(Segment = "basic", Label = "{{portals.cms.category.controls.[name].label}}", PlaceHolder = "{{portals.cms.category.controls.[name].placeholder}}", Description = "{{portals.cms.category.controls.[name].description}}")]
		public override string Title { get; set; }

		[Property(MaxLength = 100, NotNull = true, NotEmpty = true)]
		[Alias]
		[Searchable]
		[FormControl(Segment = "basic", Label = "{{portals.cms.category.controls.[name].label}}", PlaceHolder = "{{portals.cms.category.controls.[name].placeholder}}", Description = "{{portals.cms.category.controls.[name].description}}")]
		public string Alias { get; set; }

		[Searchable]
		[FormControl(Segment = "basic", ControlType = "TextArea", Label = "{{portals.cms.category.controls.[name].label}}", PlaceHolder = "{{portals.cms.category.controls.[name].placeholder}}", Description = "{{portals.cms.category.controls.[name].description}}")]
		public string Description { get; set; }

		[Property(MaxLength = 32)]
		[FormControl(Segment = "basic", ControlType = "Lookup", Label = "{{portals.cms.category.controls.[name].label}}", PlaceHolder = "{{portals.cms.category.controls.[name].placeholder}}", Description = "{{portals.cms.category.controls.[name].description}}")]
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
		public Category ParentCategory => string.IsNullOrWhiteSpace(this.ParentID) ? null : Category.Get<Category>(this.ParentID);

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

		internal List<Category> GetChildren(bool notifyPropertyChanged = true, List<Category> categories = null)
		{
			if (this._childrenIDs == null)
			{
				categories = categories ?? this.SystemID.GetCategories(this.RepositoryID, this.RepositoryEntityID, this.ID);
				this._childrenIDs = categories.Select(category => category.ID).ToList();
				if (notifyPropertyChanged)
					this.NotifyPropertyChanged("ChildrenIDs");
				return categories;
			}
			return this._childrenIDs.Select(id => id.GetCategoryByID()).ToList();
		}

		internal async Task<List<Category>> GetChildrenAsync(CancellationToken cancellationToken = default, bool notifyPropertyChanged = true)
			=> this._childrenIDs == null
				? this.GetChildren(notifyPropertyChanged, await this.SystemID.GetCategoriesAsync(this.RepositoryID, this.RepositoryEntityID, this.ID, cancellationToken).ConfigureAwait(false))
				: this._childrenIDs.Select(id => id.GetCategoryByID()).ToList();

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public List<Category> Children => this.GetChildren();

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		List<INestedObject> INestedObject.Children => this.Children?.Select(category => category as INestedObject).ToList();

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public Desktop Desktop => (this.DesktopID ?? "").GetDesktopByID() ?? this.ParentCategory?.Desktop ?? this.Module?.Desktop;

		public override JObject ToJson(bool addTypeOfExtendedProperties = false, Action<JObject> onPreCompleted = null)
			=> this.ToJson(false, addTypeOfExtendedProperties, onPreCompleted);

		public JObject ToJson(bool addChildren, bool addTypeOfExtendedProperties, Action<JObject> onPreCompleted = null)
			=> base.ToJson(addTypeOfExtendedProperties, json =>
			{
				if (addChildren)
					json["Children"] = this.Children?.Select(category => category?.ToJson(true, false)).Where(category => category != null).ToJArray();
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
			else if (name.IsEquals("ChildrenIDs"))
				Utility.Cache.Set(this);
		}

		public IBusinessEntity GetByAlias(string repositoryEntityID, string alias, string parentIdentity = null)
			=> (repositoryEntityID ?? "").GetCategoryByAlias(alias);

		public async Task<IBusinessEntity> GetByAliasAsync(string repositoryEntityID, string alias, string parentIdentity = null, CancellationToken cancellationToken = default)
			=> await (repositoryEntityID ?? "").GetCategoryByAliasAsync(alias, cancellationToken).ConfigureAwait(false);
	}

	internal static class CategoryExtensions
	{
		internal static ConcurrentDictionary<string, Category> Categories { get; } = new ConcurrentDictionary<string, Category>(StringComparer.OrdinalIgnoreCase);

		internal static ConcurrentDictionary<string, Category> CategoriesByAlias { get; } = new ConcurrentDictionary<string, Category>(StringComparer.OrdinalIgnoreCase);

		internal static Category CreateCategoryInstance(this ExpandoObject requestBody, string excluded = null, Action<Category> onCompleted = null)
			=> requestBody.Copy<Category>(excluded?.ToHashSet(), category =>
			{
				category.OriginalPrivileges = category.OriginalPrivileges?.Normalize();
				category.TrimAll();
				onCompleted?.Invoke(category);
			});

		internal static Category UpdateCategoryInstance(this Category category, ExpandoObject requestBody, string excluded = null, Action<Category> onCompleted = null)
		{
			category.CopyFrom(requestBody, excluded?.ToHashSet());
			category.OriginalPrivileges = category.OriginalPrivileges?.Normalize();
			category.TrimAll();
			onCompleted?.Invoke(category);
			return category;
		}

		internal static Category Set(this Category category, bool updateCache = false)
		{
			if (category != null)
			{
				CategoryExtensions.Categories[category.ID] = category;
				CategoryExtensions.CategoriesByAlias[$"{category.RepositoryEntityID}:{category.Alias}"] = category;
				if (updateCache)
					Utility.Cache.Set(category);
			}
			return category;
		}

		internal static async Task<Category> SetAsync(this Category category, bool updateCache = false, CancellationToken cancellationToken = default)
		{
			category?.Set();
			await (updateCache && category != null ? Utility.Cache.SetAsync(category, cancellationToken) : Task.CompletedTask).ConfigureAwait(false);
			return category;
		}

		internal static Category Remove(this Category category)
			=> (category?.ID ?? "").RemoveCategory();

		internal static Category RemoveCategory(this string id)
		{
			if (!string.IsNullOrWhiteSpace(id) && CategoryExtensions.Categories.TryRemove(id, out var category) && category != null)
			{
				CategoryExtensions.CategoriesByAlias.Remove($"{category.RepositoryEntityID}:{category.Alias}");
				return category;
			}
			return null;
		}

		internal static Category GetCategoryByID(this string id, bool force = false, bool fetchRepository = true)
			=> !force && !string.IsNullOrWhiteSpace(id) && CategoryExtensions.Categories.ContainsKey(id)
				? CategoryExtensions.Categories[id]
				: fetchRepository && !string.IsNullOrWhiteSpace(id)
					? Category.Get<Category>(id)?.Set()
					: null;

		internal static Category GetCategoryByAlias(this string repositoryEntityID, string alias, bool fetchRepository = true)
		{
			if (string.IsNullOrWhiteSpace(alias))
				return null;

			var category = CategoryExtensions.CategoriesByAlias.ContainsKey($"{repositoryEntityID}:{alias}")
				? CategoryExtensions.CategoriesByAlias[$"{repositoryEntityID}:{alias}"]
				: null;

			if (category == null && fetchRepository)
				category = Category.Get(Filters<Category>.And(Filters<Category>.Equals("RepositoryEntityID", repositoryEntityID), Filters<Category>.Equals("Alias", alias)), null, repositoryEntityID)?.Set();

			return category;
		}

		internal static async Task<Category> GetCategoryByAliasAsync(this string repositoryEntityID, string alias, CancellationToken cancellationToken = default)
			=> (repositoryEntityID ?? "").GetCategoryByAlias(alias, false) ?? (await Category.GetAsync(Filters<Category>.And(Filters<Category>.Equals("RepositoryEntityID", repositoryEntityID), Filters<Category>.Equals("Alias", alias)), null, repositoryEntityID, cancellationToken).ConfigureAwait(false))?.Set();

		internal static async Task<Category> GetCategoryByIDAsync(this string id, CancellationToken cancellationToken = default, bool force = false)
			=> (id ?? "").GetCategoryByID(force, false) ?? (await Category.GetAsync<Category>(id, cancellationToken).ConfigureAwait(false))?.Set();

		internal static IFilterBy<Category> GetCategoriesFilter(this string systemID, string repositoryID = null, string repositoryEntityID = null, string parentID = null)
		{
			var filter = Filters<Category>.And(Filters<Category>.Equals("SystemID", systemID));
			if (!string.IsNullOrWhiteSpace(repositoryID))
				filter.Add(Filters<Category>.Equals("RepositoryID", repositoryID));
			if (!string.IsNullOrWhiteSpace(repositoryEntityID))
				filter.Add(Filters<Category>.Equals("RepositoryEntityID", repositoryEntityID));
			filter.Add(string.IsNullOrWhiteSpace(parentID) ? Filters<Category>.IsNull("ParentID") : Filters<Category>.Equals("ParentID", parentID));
			return filter;
		}

		internal static List<Category> GetCategories(this string systemID, string repositoryID = null, string repositoryEntityID = null, string parentID = null, bool updateCache = true)
		{
			if (string.IsNullOrWhiteSpace(systemID))
				return new List<Category>();
			var filter = systemID.GetCategoriesFilter(repositoryID, repositoryEntityID, parentID);
			var sort = Sorts<Category>.Ascending("OrderIndex").ThenByAscending("Title");
			var categories = Category.Find(filter, sort, 0, 1, Extensions.GetCacheKey(filter, sort, 0, 1));
			categories.ForEach(category => category.Set(updateCache));
			return categories;
		}

		internal static async Task<List<Category>> GetCategoriesAsync(this string systemID, string repositoryID = null, string repositoryEntityID = null, string parentID = null, CancellationToken cancellationToken = default, bool updateCache = true)
		{
			if (string.IsNullOrWhiteSpace(systemID))
				return new List<Category>();
			var filter = systemID.GetCategoriesFilter(repositoryID, repositoryEntityID, parentID);
			var sort = Sorts<Category>.Ascending("OrderIndex").ThenByAscending("Title");
			var categories = await Category.FindAsync(filter, sort, 0, 1, Extensions.GetCacheKey(filter, sort, 0, 1), cancellationToken).ConfigureAwait(false);
			await categories.ForEachAsync((category, token) => category.SetAsync(updateCache, token), cancellationToken).ConfigureAwait(false);
			return categories;
		}
	}
}
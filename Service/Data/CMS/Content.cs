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
	[Entity(CollectionName = "CMS_Contents", TableName = "T_Portals_CMS_Contents", CacheClass = typeof(Utility), CacheName = "Cache", Searchable = true, ID = "B0000000000000000000000000000002", Title = "Content", Description = "Complex content in CMS module (article/news)", ObjectNamePrefix = "CMS.", MultipleIntances = true, Indexable = true, Extendable = true)]
	public sealed class Content : Repository<Content>, IBusinessObject, IAliasEntity
	{
		public Content() : base() { }

		[JsonConverter(typeof(StringEnumConverter)), BsonRepresentation(MongoDB.Bson.BsonType.String)]
		[Sortable(IndexName = "Management")]
		[FormControl(Segment = "management", Label = "{{portals.cms.contents.controls.[name].label}}", PlaceHolder = "{{portals.cms.contents.controls.[name].placeholder}}", Description = "{{portals.cms.contents.controls.[name].description}}")]
		public ApprovalStatus Status { get; set; } = ApprovalStatus.Pending;

		[Property(MaxLength = 32, NotNull = true, NotEmpty = true)]
		[Sortable(IndexName = "Management")]
		[ParentMapping(Type = typeof(Category))]
		[FormControl(Segment = "management", ControlType = "Lookup", Multiple = false, LookupObjectIsNested = true, Label = "{{portals.cms.contents.controls.[name].label}}", PlaceHolder = "{{portals.cms.contents.controls.[name].placeholder}}", Description = "{{portals.cms.contents.controls.[name].description}}")]
		public string CategoryID { get; set; }

		[MultipleParentMappings(TableName = "T_Portals_CMS_Contents_Categories", LinkColumn = "ContentID", MapColumn = "CategoryID")]
		[FormControl(Segment = "management", ControlType = "Lookup", Multiple = true, LookupObjectIsNested = true, Label = "{{portals.cms.contents.controls.[name].label}}", PlaceHolder = "{{portals.cms.contents.controls.[name].placeholder}}", Description = "{{portals.cms.contents.controls.[name].description}}")]
		public List<string> OtherCategories { get; set; }

		[Property(MaxLength = 250, NotNull = true, NotEmpty = true)]
		[Alias(Properties = "CategoryID")]
		[Searchable]
		[FormControl(Segment = "management", Label = "{{portals.cms.contents.controls.[name].label}}", PlaceHolder = "{{portals.cms.contents.controls.[name].placeholder}}", Description = "{{portals.cms.contents.controls.[name].description}}")]
		public string Alias { get; set; }

		[Property(MaxLength = 10)]
		[Sortable(IndexName = "Times")]
		[FormControl(Segment = "management", ControlType = "DatePicker", DatePickerWithTimes = false, DataType = "date", Label = "{{portals.cms.contents.controls.[name].label}}", PlaceHolder = "{{portals.cms.contents.controls.[name].placeholder}}", Description = "{{portals.cms.contents.controls.[name].description}}")]
		public string StartDate { get; set; } = DateTime.Now.ToDTString(false, false);

		[Property(MaxLength = 10)]
		[Sortable(IndexName = "Times")]
		[FormControl(Segment = "management", ControlType = "DatePicker", DatePickerWithTimes = false, Label = "{{portals.cms.contents.controls.[name].label}}", PlaceHolder = "{{portals.cms.contents.controls.[name].placeholder}}", Description = "{{portals.cms.contents.controls.[name].description}}")]
		public string EndDate { get; set; }

		[Sortable(IndexName = "Times")]
		[FormControl(Segment = "management", ControlType = "DatePicker", DatePickerWithTimes = true, Label = "{{portals.cms.contents.controls.[name].label}}", PlaceHolder = "{{portals.cms.contents.controls.[name].placeholder}}", Description = "{{portals.cms.contents.controls.[name].description}}")]
		public DateTime? PublishedTime { get; set; }

		[Property(MaxLength = 250)]
		[Sortable(IndexName = "Tags")]
		[Searchable]
		[FormControl(Segment = "management", Label = "{{portals.cms.contents.controls.[name].label}}", PlaceHolder = "{{portals.cms.contents.controls.[name].placeholder}}", Description = "{{portals.cms.contents.controls.[name].description}}")]
		public string Tags { get; set; }

		[Sortable(IndexName = "Management")]
		[FormControl(Segment = "management", Label = "{{portals.cms.contents.controls.[name].label}}", PlaceHolder = "{{portals.cms.contents.controls.[name].placeholder}}", Description = "{{portals.cms.contents.controls.[name].description}}")]
		public bool AllowComments { get; set; } = false;

		[Property(MaxLength = 250, NotNull = true, NotEmpty = true)]
		[Sortable(IndexName = "Title")]
		[Searchable]
		[FormControl(Segment = "basic", Label = "{{portals.cms.contents.controls.[name].label}}", PlaceHolder = "{{portals.cms.contents.controls.[name].placeholder}}", Description = "{{portals.cms.contents.controls.[name].description}}")]
		public override string Title { get; set; }

		[Property(MaxLength = 250)]
		[Sortable(IndexName = "SubTitle")]
		[Searchable]
		[FormControl(Segment = "basic", Label = "{{portals.cms.contents.controls.[name].label}}", PlaceHolder = "{{portals.cms.contents.controls.[name].placeholder}}", Description = "{{portals.cms.contents.controls.[name].description}}")]
		public string SubTitle { get; set; }

		[Property(MaxLength = 250)]
		[Sortable(IndexName = "Author")]
		[Searchable]
		[FormControl(Segment = "basic", Label = "{{portals.cms.contents.controls.[name].label}}", PlaceHolder = "{{portals.cms.contents.controls.[name].placeholder}}", Description = "{{portals.cms.contents.controls.[name].description}}")]
		public string Author { get; set; }

		[Property(MaxLength = 250)]
		[Sortable(IndexName = "Source")]
		[Searchable]
		[FormControl(Segment = "basic", Label = "{{portals.cms.contents.controls.[name].label}}", PlaceHolder = "{{portals.cms.contents.controls.[name].placeholder}}", Description = "{{portals.cms.contents.controls.[name].description}}")]
		public string Source { get; set; }

		[Property(MaxLength = 1000)]
		[FormControl(Segment = "basic", DataType = "url", Label = "{{portals.cms.contents.controls.[name].label}}", PlaceHolder = "{{portals.cms.contents.controls.[name].placeholder}}", Description = "{{portals.cms.contents.controls.[name].description}}")]
		public string SourceURL { get; set; }

		[Searchable]
		[FormControl(Segment = "basic", ControlType = "TextArea", Label = "{{portals.cms.contents.controls.[name].label}}", PlaceHolder = "{{portals.cms.contents.controls.[name].placeholder}}", Description = "{{portals.cms.contents.controls.[name].description}}")]
		public string Summary { get; set; }

		[Searchable]
		[Property(IsCLOB = true)]
		[FormControl(Segment = "basic", ControlType = "TextEditor", Label = "{{portals.cms.contents.controls.[name].label}}", PlaceHolder = "{{portals.cms.contents.controls.[name].placeholder}}", Description = "{{portals.cms.contents.controls.[name].description}}")]
		public string Details { get; set; }

		[ChildrenMappings(TableName = "T_Portals_CMS_Contents_Relateds", LinkColumn = "FromID", MapColumn = "ToID", Type = typeof(Content))]
		[FormControl(Segment = "related", ControlType = "Lookup", Multiple = true, Label = "{{portals.cms.contents.controls.[name].label}}", PlaceHolder = "{{portals.cms.contents.controls.[name].placeholder}}", Description = "{{portals.cms.contents.controls.[name].description}}")]
		public List<string> Relateds { get; set; }

		[AsJson]
		[FormControl(Segment = "related", AsArray = true, Label = "{{portals.cms.contents.controls.[name].label}}", PlaceHolder = "{{portals.cms.contents.controls.[name].placeholder}}", Description = "{{portals.cms.contents.controls.[name].description}}")]
		public List<ExternalRelated> ExternalRelateds { get; set; }

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
		public Category Category => (this.CategoryID ?? "").GetCategoryByID();

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public override RepositoryBase Parent => this.Category;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		IBusinessEntity IBusinessEntity.Parent => this.Category;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		IPortalObject IPortalObject.Parent => this.Category;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public Desktop Desktop => this.Category?.Desktop;

		public IAliasEntity GetByAlias(string repositoryEntityID, string alias, string parentIdentity = null)
			=> Content.GetContentByAlias(repositoryEntityID, alias, parentIdentity);

		public async Task<IAliasEntity> GetByAliasAsync(string repositoryEntityID, string alias, string parentIdentity = null, CancellationToken cancellationToken = default)
			=> await Content.GetContentByAliasAsync(repositoryEntityID, alias, parentIdentity, cancellationToken).ConfigureAwait(false);

		internal static Content GetContentByAlias(ContentType contentType, string alias, string parentIdentity)
		{
			// check
			if (contentType == null || string.IsNullOrWhiteSpace(alias) || string.IsNullOrWhiteSpace(parentIdentity))
				return null;

			// get category
			var category = parentIdentity.IsValidUUID()
				? parentIdentity.GetCategoryByID()
				: (contentType.GetParent()?.RepositoryEntityID ?? "").GetCategoryByAlias(parentIdentity.NormalizeAlias());
			if (category == null)
				return null;

			// get by identity (using cache)
			var cacheKey = $"e:{contentType.ID}#c:{category.ID}#a:{alias.NormalizeAlias()}".GetCacheKey<Content>();
			var id = Utility.Cache.Get<string>(cacheKey);
			if (!string.IsNullOrWhiteSpace(id) && id.IsValidUUID())
				return Content.Get<Content>(id);

			// get by alias
			var content = Content.Get<Content>(contentType.GetContentByAliasFilter(category, alias), null, contentType.ID);
			if (content != null)
				Utility.Cache.Set(cacheKey, content.ID);
			return content;
		}

		internal static async Task<Content> GetContentByAliasAsync(ContentType contentType, string alias, string parentIdentity, CancellationToken cancellationToken = default)
		{
			// check
			if (contentType == null || string.IsNullOrWhiteSpace(alias) || string.IsNullOrWhiteSpace(parentIdentity))
				return null;

			// get category
			var category = parentIdentity.IsValidUUID()
				? await parentIdentity.GetCategoryByIDAsync(cancellationToken).ConfigureAwait(false)
				: await (contentType.GetParent()?.RepositoryEntityID ?? "").GetCategoryByAliasAsync(parentIdentity.NormalizeAlias(), cancellationToken).ConfigureAwait(false);
			if (category == null)
				return null;

			// get by identity (using cache)
			var cacheKey = $"e:{contentType.ID}#c:{category.ID}#a:{alias.NormalizeAlias()}".GetCacheKey<Content>();
			var id = await Utility.Cache.GetAsync<string>(cacheKey, cancellationToken).ConfigureAwait(false);
			if (!string.IsNullOrWhiteSpace(id) && id.IsValidUUID())
				return await Content.GetAsync<Content>(id, cancellationToken).ConfigureAwait(false);

			// get by alias
			var content = await Content.GetAsync<Content>(contentType.GetContentByAliasFilter(category, alias), null, contentType.ID, cancellationToken).ConfigureAwait(false);
			if (content != null)
				await Utility.Cache.SetAsync(cacheKey, content.ID, cancellationToken).ConfigureAwait(false);
			return content;
		}

		internal static Content GetContentByAlias(string repositoryEntityID, string alias, string parentIdentity)
			=> string.IsNullOrWhiteSpace(repositoryEntityID) || string.IsNullOrWhiteSpace(alias) || string.IsNullOrWhiteSpace(parentIdentity)
				? null
				: Content.GetContentByAlias(repositoryEntityID.GetContentTypeByID(), alias, parentIdentity);

		internal static async Task<Content> GetContentByAliasAsync(string repositoryEntityID, string alias, string parentIdentity, CancellationToken cancellationToken = default)
			=> string.IsNullOrWhiteSpace(repositoryEntityID) || string.IsNullOrWhiteSpace(alias) || string.IsNullOrWhiteSpace(parentIdentity)
				? null
				: await Content.GetContentByAliasAsync(await repositoryEntityID.GetContentTypeByIDAsync(cancellationToken).ConfigureAwait(false), alias, parentIdentity, cancellationToken).ConfigureAwait(false);
	}

	[Serializable]
	public sealed class ExternalRelated
	{
		public ExternalRelated() { }

		[FormControl(MaxLength = 250)]
		public string Title { get; set; }

		[FormControl(ControlType = "TextArea", MaxLength = 4000)]
		public string Summary { get; set; }

		[FormControl(DataType = "url", MaxLength = 1000)]
		public string URL { get; set; }
	}
}
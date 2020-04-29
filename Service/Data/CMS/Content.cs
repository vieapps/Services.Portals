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
	[Entity(CollectionName = "CMS_Contents", TableName = "T_Portals_CMS_Contents", CacheClass = typeof(Utility), CacheName = "Cache", Searchable = true,
		ObjectName = "Content", ID = "B0000000000000000000000000000002", Title = "Content", Description = "A detail of content in CMS module (article/news)", MultipleIntances = true, Extendable = true, Indexable = true, AliasProperty = "Alias",
		ParentType = typeof(Category), ParentAssociatedProperty = "CategoryID", MultipleParentAssociates = true, MultipleParentAssociatesProperty = "OtherCategories", MultipleParentAssociatesTable = "T_Portals_CMS_Contents_Categories", MultipleParentAssociatesMapColumn = "CategoryID", MultipleParentAssociatesLinkColumn = "ContentID")]
	public sealed class Content : Repository<Content>, IBusinessObject
	{
		public Content() : base() { }

		[JsonConverter(typeof(StringEnumConverter)), BsonRepresentation(MongoDB.Bson.BsonType.String)]
		[Sortable(IndexName = "Management")]
		[FormControl(Segment = "management", Label = "{{portals.cms.content.controls.[name].label}}", PlaceHolder = "{{portals.cms.content.controls.[name].placeholder}}", Description = "{{portals.cms.content.controls.[name].description}}")]
		public ApprovalStatus Status { get; set; } = ApprovalStatus.Pending;

		[Property(MaxLength = 32)]
		[Sortable(IndexName = "Management", UniqueIndexName = "Alias")]
		[FormControl(Segment = "management", ControlType = "Lookup", Multiple = false, Label = "{{portals.cms.content.controls.[name].label}}", PlaceHolder = "{{portals.cms.content.controls.[name].placeholder}}", Description = "{{portals.cms.content.controls.[name].description}}")]
		public string CategoryID { get; set; }

		[FormControl(Segment = "management", ControlType = "Lookup", Multiple = true, Label = "{{portals.cms.content.controls.[name].label}}", PlaceHolder = "{{portals.cms.content.controls.[name].placeholder}}", Description = "{{portals.cms.content.controls.[name].description}}")]
		public List<string> OtherCategories { get; set; }

		[Property(MaxLength = 10)]
		[Sortable(IndexName = "Times")]
		[FormControl(Segment = "management", Label = "{{portals.cms.content.controls.[name].label}}", PlaceHolder = "{{portals.cms.content.controls.[name].placeholder}}", Description = "{{portals.cms.content.controls.[name].description}}")]
		public string StartDate { get; set; } = DateTime.Now.ToString("yyyy/MM/dd");

		[Property(MaxLength = 10)]
		[Sortable(IndexName = "Times")]
		[FormControl(Segment = "management", Label = "{{portals.cms.content.controls.[name].label}}", PlaceHolder = "{{portals.cms.content.controls.[name].placeholder}}", Description = "{{portals.cms.content.controls.[name].description}}")]
		public string EndDate { get; set; } = "-";

		[Sortable(IndexName = "Times")]
		[FormControl(Segment = "management", Label = "{{portals.cms.content.controls.[name].label}}", PlaceHolder = "{{portals.cms.content.controls.[name].placeholder}}", Description = "{{portals.cms.content.controls.[name].description}}")]
		public DateTime PublishedTime { get; set; } = DateTime.Now;

		[Property(MaxLength = 250)]
		[Sortable(IndexName = "Tags"), Searchable]
		[FormControl(Segment = "management", Label = "{{portals.cms.content.controls.[name].label}}", PlaceHolder = "{{portals.cms.content.controls.[name].placeholder}}", Description = "{{portals.cms.content.controls.[name].description}}")]
		public string Tags { get; set; }

		[Property(MaxLength = 250, NotNull = true, NotEmpty = true)]
		[Sortable(IndexName = "Title"), Searchable]
		[FormControl(Segment = "basic", Label = "{{portals.cms.content.controls.[name].label}}", PlaceHolder = "{{portals.cms.content.controls.[name].placeholder}}", Description = "{{portals.cms.content.controls.[name].description}}")]
		public override string Title { get; set; }

		[Property(MaxLength = 250)]
		[Sortable(IndexName = "SubTitle"), Searchable]
		[FormControl(Segment = "basic", Label = "{{portals.cms.content.controls.[name].label}}", PlaceHolder = "{{portals.cms.content.controls.[name].placeholder}}", Description = "{{portals.cms.content.controls.[name].description}}")]
		public string SubTitle { get; set; }

		[Property(MaxLength = 250)]
		[Sortable(IndexName = "Author"), Searchable]
		[FormControl(Segment = "basic", Label = "{{portals.cms.content.controls.[name].label}}", PlaceHolder = "{{portals.cms.content.controls.[name].placeholder}}", Description = "{{portals.cms.content.controls.[name].description}}")]
		public string Author { get; set; }

		[Property(MaxLength = 250)]
		[Sortable(IndexName = "Source"), Searchable]
		[FormControl(Segment = "basic", Label = "{{portals.cms.content.controls.[name].label}}", PlaceHolder = "{{portals.cms.content.controls.[name].placeholder}}", Description = "{{portals.cms.content.controls.[name].description}}")]
		public string Source { get; set; }

		[Property(MaxLength = 1000)]
		[FormControl(Segment = "basic", Label = "{{portals.cms.content.controls.[name].label}}", PlaceHolder = "{{portals.cms.content.controls.[name].placeholder}}", Description = "{{portals.cms.content.controls.[name].description}}")]
		public string SourceURL { get; set; }

		[Property(MaxLength = 250, NotNull = true, NotEmpty = true)]
		[Sortable(UniqueIndexName = "Alias"), Searchable]
		[FormControl(Segment = "basic", Label = "{{portals.cms.content.controls.[name].label}}", PlaceHolder = "{{portals.cms.content.controls.[name].placeholder}}", Description = "{{portals.cms.content.controls.[name].description}}")]
		public string Alias { get; set; }

		[FormControl(Segment = "basic", ControlType = "TextArea", Label = "{{portals.cms.content.controls.[name].label}}", PlaceHolder = "{{portals.cms.content.controls.[name].placeholder}}", Description = "{{portals.cms.content.controls.[name].description}}")]
		public string Summary { get; set; }

		[Property(IsCLOB = true)]
		[FormControl(Segment = "basic", ControlType = "TextEditor", Label = "{{portals.cms.content.controls.[name].label}}", PlaceHolder = "{{portals.cms.content.controls.[name].placeholder}}", Description = "{{portals.cms.content.controls.[name].description}}")]
		public string Details { get; set; }

		[AsSingleMapping]
		[FormControl(Segment = "related", ControlType = "Lookup", Multiple = true, Label = "{{portals.cms.content.controls.[name].label}}", PlaceHolder = "{{portals.cms.content.controls.[name].placeholder}}", Description = "{{portals.cms.content.controls.[name].description}}")]
		public List<string> RelatedContents { get; set; }

		[AsJson]
		[FormControl(Segment = "related", Label = "{{portals.cms.content.controls.[name].label}}", PlaceHolder = "{{portals.cms.content.controls.[name].placeholder}}", Description = "{{portals.cms.content.controls.[name].description}}")]
		public List<string> Externals { get; set; }

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
		[Sortable(IndexName = "Management", UniqueIndexName = "Alias")]
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
	}
}
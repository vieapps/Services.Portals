#region Related components
using System;
using System.Diagnostics;
using System.Collections.Generic;
using MsgPack.Serialization;
using MongoDB.Bson.Serialization.Attributes;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Converters;
using net.vieapps.Components.Security;
using net.vieapps.Components.Repository;
#endregion

namespace net.vieapps.Services.Portals
{
	[BsonIgnoreExtraElements, DebuggerDisplay("ID = {ID}, Title = {Title}")]
	[Entity(CollectionName = "CMS_Crawlers", TableName = "T_Portals_CMS_Crawlers", CacheClass = typeof(Utility), CacheName = "Cache", Searchable = true)]
	public class Crawler : Repository<Crawler>, Crawlers.ICrawlerInfo
	{
		public Crawler() { }

		[JsonConverter(typeof(StringEnumConverter)), BsonRepresentation(MongoDB.Bson.BsonType.String)]
		[FormControl(Segment = "basic", Label = "{{portals.cms.crawlers.controls.[name].label}}", PlaceHolder = "{{portals.cms.crawlers.controls.[name].placeholder}}", Description = "{{portals.cms.crawlers.controls.[name].description}}")]
		public ApprovalStatus Status { get; set; } = ApprovalStatus.Draft;

		[Property(MaxLength = 250, NotNull = true, NotEmpty = true)]
		[Sortable(IndexName = "Title")]
		[Searchable]
		[FormControl(Segment = "basic", Label = "{{portals.cms.crawlers.controls.[name].label}}", PlaceHolder = "{{portals.cms.crawlers.controls.[name].placeholder}}", Description = "{{portals.cms.crawlers.controls.[name].description}}")]
		public override string Title { get; set; }

		[Searchable]
		[FormControl(ControlType = "TextArea", Label = "{{portals.cms.crawlers.controls.[name].label}}", PlaceHolder = "{{portals.cms.crawlers.controls.[name].placeholder}}", Description = "{{portals.cms.crawlers.controls.[name].description}}")]
		public string Description { get; set; }

		[JsonConverter(typeof(StringEnumConverter)), BsonRepresentation(MongoDB.Bson.BsonType.String)]
		[FormControl(Segment = "basic", Label = "{{portals.cms.crawlers.controls.[name].label}}", PlaceHolder = "{{portals.cms.crawlers.controls.[name].placeholder}}", Description = "{{portals.cms.crawlers.controls.[name].description}}")]
		public Crawlers.Type Type { get; set; } = Crawlers.Type.Custom;

		[Property(MaxLength = 250, NotNull = true, NotEmpty = true)]
		[FormControl(Segment = "basic", DataType = "url", Label = "{{portals.cms.crawlers.controls.[name].label}}", PlaceHolder = "{{portals.cms.crawlers.controls.[name].placeholder}}", Description = "{{portals.cms.crawlers.controls.[name].description}}")]
		public string URL { get; set; }

		[Property(MaxLength = 32, NotNull = true, NotEmpty = true)]
		[Sortable(IndexName = "Management")]
		[FormControl(Hidden = true)]
		public override string SystemID { get; set; }

		[Property(MaxLength = 32, NotNull = true, NotEmpty = true)]
		[Sortable(IndexName = "Management")]
		[FormControl(Segment = "basic", ControlType = "Select", Label = "{{portals.cms.crawlers.controls.[name].label}}", PlaceHolder = "{{portals.cms.crawlers.controls.[name].placeholder}}", Description = "{{portals.cms.crawlers.controls.[name].description}}")]
		public override string RepositoryID { get; set; }

		[Property(MaxLength = 32, NotNull = true, NotEmpty = true)]
		[Sortable(IndexName = "Management")]
		[FormControl(Segment = "basic", ControlType = "Select", Label = "{{portals.cms.crawlers.controls.[name].label}}", PlaceHolder = "{{portals.cms.crawlers.controls.[name].placeholder}}", Description = "{{portals.cms.crawlers.controls.[name].description}}")]
		public override string RepositoryEntityID { get; set; }

		[FormControl(Segment = "basic", Label = "{{portals.cms.crawlers.controls.[name].label}}", PlaceHolder = "{{portals.cms.crawlers.controls.[name].placeholder}}", Description = "{{portals.cms.crawlers.controls.[name].description}}")]
		public bool SetAuthor { get; set; } = false;

		[FormControl(Segment = "basic", Label = "{{portals.cms.crawlers.controls.[name].label}}", PlaceHolder = "{{portals.cms.crawlers.controls.[name].placeholder}}", Description = "{{portals.cms.crawlers.controls.[name].description}}")]
		public bool SetSource { get; set; } = false;

		[Property(MaxLength = 250)]
		[FormControl(Segment = "basic", Label = "{{portals.cms.crawlers.controls.[name].label}}", PlaceHolder = "{{portals.cms.crawlers.controls.[name].placeholder}}", Description = "{{portals.cms.crawlers.controls.[name].description}}")]
		public string NormalizingAdapter { get; set; }

		[JsonConverter(typeof(StringEnumConverter)), BsonRepresentation(MongoDB.Bson.BsonType.String)]
		[FormControl(Segment = "basic", Label = "{{portals.cms.crawlers.controls.[name].label}}", PlaceHolder = "{{portals.cms.crawlers.controls.[name].placeholder}}", Description = "{{portals.cms.crawlers.controls.[name].description}}")]
		public ApprovalStatus DefaultStatus { get; set; } = ApprovalStatus.Published;

		[FormControl(Segment = "basic", Required = true, Label = "{{portals.cms.crawlers.controls.[name].label}}", PlaceHolder = "{{portals.cms.crawlers.controls.[name].placeholder}}", Description = "{{portals.cms.crawlers.controls.[name].description}}")]
		public int MaxPages { get; set; } = -1;

		[FormControl(Segment = "basic", Required = true, Label = "{{portals.cms.crawlers.controls.[name].label}}", PlaceHolder = "{{portals.cms.crawlers.controls.[name].placeholder}}", Description = "{{portals.cms.crawlers.controls.[name].description}}")]
		public int Interval { get; set; } = 300;

		[Sortable(IndexName = "Audits")]
		[FormControl(ReadOnly = true, Label = "{{portals.cms.crawlers.controls.[name].label}}", PlaceHolder = "{{portals.cms.crawlers.controls.[name].placeholder}}", Description = "{{portals.cms.crawlers.controls.[name].description}}")]
		public DateTime LastActivity { get; set; }

		[AsJson]
		[FormControl(Segment = "categories", ControlType = "Lookup", Multiple = true, LookupObjectIsNested = false, Label = "{{portals.cms.crawlers.controls.[name].label}}", PlaceHolder = "{{portals.cms.crawlers.controls.[name].placeholder}}", Description = "{{portals.cms.crawlers.controls.[name].description}}")]
		public List<string> SelectedCategories { get; set; }

		[AsJson]
		[FormControl(Segment = "categories", ControlType = "Lookup", Multiple = false, LookupObjectIsNested = true, Label = "{{portals.cms.crawlers.controls.[name].label}}", PlaceHolder = "{{portals.cms.crawlers.controls.[name].placeholder}}", Description = "{{portals.cms.crawlers.controls.[name].description}}")]
		public List<string> CategoryMappings { get; set; }

		[Property(MaxLength = 32, NotNull = true, NotEmpty = true)]
		[FormControl(Segment = "categories", ControlType = "Lookup", Multiple = false, LookupObjectIsNested = true, Label = "{{portals.cms.crawlers.controls.[name].label}}", PlaceHolder = "{{portals.cms.crawlers.controls.[name].placeholder}}", Description = "{{portals.cms.crawlers.controls.[name].description}}")]
		public string DefaultCategoryID { get; set; }

		string _options;

		[Property(IsCLOB = true)]
		[FormControl(Segment = "options", Label = "{{portals.cms.crawlers.controls.[name].label}}", PlaceHolder = "{{portals.cms.crawlers.controls.[name].placeholder}}", Description = "{{portals.cms.crawlers.controls.[name].description}}")]
		public string Options
		{
			get => this._options;
			set
			{
				this._settings = JObject.Parse(string.IsNullOrWhiteSpace(value) ? "{}" : value);
				this._options = this._settings.ToString();
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

		[MessagePackIgnore]
		JObject _settings;

		[JsonIgnore, BsonIgnore, Ignore, MessagePackIgnore]
		[FormControl(Excluded = true)]
		public JObject Settings => this._settings;

		[JsonIgnore, BsonIgnore, Ignore, MessagePackIgnore]
		[FormControl(Excluded = true)]
		public List<Crawlers.Category> Categories { get; set; }

		[Ignore, JsonIgnore, BsonIgnore, MessagePackIgnore]
		public string OrganizationID => this.SystemID;

		[Ignore, JsonIgnore, BsonIgnore, MessagePackIgnore]
		public Organization Organization => (this.OrganizationID ?? "").GetOrganizationByID();

		[Ignore, JsonIgnore, BsonIgnore, MessagePackIgnore]
		public string ModuleID => this.RepositoryID;

		[Ignore, JsonIgnore, BsonIgnore, MessagePackIgnore]
		public Module Module => (this.ModuleID ?? "").GetModuleByID();

		[Ignore, JsonIgnore, BsonIgnore, MessagePackIgnore]
		public string ContentTypeID => this.RepositoryEntityID;

		[Ignore, JsonIgnore, BsonIgnore, MessagePackIgnore]
		public ContentType ContentType => (this.ContentTypeID ?? "").GetContentTypeByID();

		[Ignore, JsonIgnore, BsonIgnore, MessagePackIgnore]
		public string WebID { get; set; }

		[Ignore, JsonIgnore, BsonIgnore, MessagePackIgnore]
		public string WebURL { get; set; }
	}
}

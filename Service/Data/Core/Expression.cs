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
using net.vieapps.Services.Portals.Exceptions;
#endregion

namespace net.vieapps.Services.Portals
{
	[Serializable, BsonIgnoreExtraElements]
	[DebuggerDisplay("ID = {ID}, Title = {Title}")]
	[Entity(CollectionName = "Expressions", TableName = "T_Portals_Expressions", CacheClass = typeof(Utility), CacheName = "Cache", Searchable = true)]
	public sealed class Expression : Repository<Expression>, IPortalExpression
	{
		public Expression() : base() { }

		[Property(MaxLength = 250, NotNull = true, NotEmpty = true)]
		[Sortable(IndexName = "Title")]
		[Searchable]
		[FormControl(Segment = "basic", Label = "{{portals.expressions.controls.[name].label}}", PlaceHolder = "{{portals.expressions.controls.[name].placeholder}}", Description = "{{portals.expressions.controls.[name].description}}")]
		public override string Title { get; set; }

		[FormControl(Segment = "basic", ControlType = "TextArea", Label = "{{portals.expressions.controls.[name].label}}", PlaceHolder = "{{portals.expressions.controls.[name].placeholder}}", Description = "{{portals.expressions.controls.[name].description}}")]
		public string Description { get; set; }

		[Property(MaxLength = 32, NotNull = true, NotEmpty = true)]
		[Sortable(IndexName = "Management")]
		[FormControl(Segment = "basic", ControlType = "Select", Label = "{{portals.expressions.controls.[name].label}}", PlaceHolder = "{{portals.expressions.controls.[name].placeholder}}", Description = "{{portals.expressions.controls.[name].description}}")]
		public override string RepositoryID { get; set; }

		[Property(MaxLength = 32)]
		[Sortable(IndexName = "Management")]
		[FormControl(Segment = "basic", ControlType = "Select", Label = "{{portals.expressions.controls.[name].label}}", PlaceHolder = "{{portals.expressions.controls.[name].placeholder}}", Description = "{{portals.expressions.controls.[name].description}}")]
		public string ContentTypeDefinitionID { get; set; }

		[Property(MaxLength = 32)]
		[Sortable(IndexName = "Management")]
		[FormControl(Segment = "basic", ControlType = "Select", Label = "{{portals.expressions.controls.[name].label}}", PlaceHolder = "{{portals.expressions.controls.[name].placeholder}}", Description = "{{portals.expressions.controls.[name].description}}")]
		public override string RepositoryEntityID { get; set; }

		[AsJson]
		[FormControl(Hidden = true)]
		public FilterBy FilterBy { get; set; }

		[AsJson]
		[FormControl(Hidden = true)]
		public List<SortBy> SortBys { get; set; }

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
		public string OrganizationID => this.SystemID;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public Organization Organization => (this.OrganizationID ?? "").GetOrganizationByID();

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public string ModuleID => this.RepositoryID;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public Module Module => (this.ModuleID ?? "").GetModuleByID();

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public override Privileges OriginalPrivileges { get; set; }

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public override Privileges WorkingPrivileges => this.Organization?.WorkingPrivileges;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public override RepositoryBase Parent => this.Organization;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		IPortalObject IPortalObject.Parent => this.Organization;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public string ContentTypeID => this.RepositoryEntityID;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public ContentType ContentType => (this.ContentTypeID ?? "").GetContentTypeByID();

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public ContentTypeDefinition ContentTypeDefinition => this.ContentType?.ContentTypeDefinition ?? (!string.IsNullOrWhiteSpace(this.ContentTypeDefinitionID) && Utility.ContentTypeDefinitions.TryGetValue(this.ContentTypeDefinitionID, out var definition) ? definition : null);

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		IFilterBy IPortalExpression.FilterBy => this.FilterBy;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public SortBy SortBy => this.SortBys?.FirstOrDefault();

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		ISortBy IPortalExpression.SortBy => this.SortBy;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		List<ISortBy> IPortalExpression.SortBys => this.SortBys?.Select(sortBy => sortBy as ISortBy).ToList();

		internal void Normalize()
		{
		}
	}
}
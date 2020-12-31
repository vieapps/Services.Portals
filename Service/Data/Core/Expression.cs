#region Related components
using System;
using System.Linq;
using System.Diagnostics;
using System.Dynamic;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Serialization;
using MsgPack.Serialization;
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
	[BsonIgnoreExtraElements, DebuggerDisplay("ID = {ID}, Title = {Title}")]
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

		[JsonIgnore, XmlIgnore]
		[Property(IsCLOB = true)]
		[FormControl(Excluded = true)]
		public string FilterBy { get; set; }

		[Ignore, BsonIgnore, MessagePackIgnore]
		[FormControl(Excluded = true)]
		public FilterBys Filter { get; set; }

		[JsonIgnore, XmlIgnore]
		[Property(IsCLOB = true)]
		[FormControl(Excluded = true)]
		public string SortBy { get; set; }

		[Ignore, BsonIgnore, MessagePackIgnore]
		[FormControl(Excluded = true)]
		public List<SortBy> Sorts { get; set; }

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

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore, MessagePackIgnore]
		IFilterBy IPortalExpression.Filter => this.Filter;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public SortBy Sort => this.Sorts?.FirstOrDefault();

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore, MessagePackIgnore]
		ISortBy IPortalExpression.Sort => this.Sort;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore, MessagePackIgnore]
		List<ISortBy> IPortalExpression.Sorts => this.Sorts?.Select(sort => sort as ISortBy).ToList();

		internal void Normalize(JObject filterBy = null, JArray sortBy = null)
		{
			this.Filter = filterBy != null && filterBy.Get("Operator") != null
				? new FilterBys(filterBy)
				: new FilterBys(GroupOperator.And, new List<IFilterBy> { new FilterBy("SystemID", CompareOperator.Equals, this.Organization.ID) });
			this.FilterBy = this.Filter.ToJson().ToString(Formatting.None);
			this.Sorts = sortBy?.Select(sort => sort["Attribute"] != null ? new SortBy(sort as JObject) : null).Where(sort => sort != null).ToList();
			this.Sorts = this.Sorts != null && this.Sorts.Count > 0
				? this.Sorts
				: new[]
				{
					(this.ContentTypeDefinition?.EntityDefinition?.Type ?? AssemblyLoader.GetType(this.ContentTypeDefinition.EntityDefinitionTypeName))?.CreateInstance() is INestedObject
						? new SortBy("OrderIndex", SortMode.Ascending)
						: new SortBy("Created", SortMode.Descending)
				}.ToList();
			this.SortBy = this.Sorts.Select(sort => sort.ToJson()).ToJArray().ToString(Formatting.None);
		}

		public override void ProcessPropertyChanged(string name)
		{
			if ("FilterBy".IsEquals(name))
				this.Filter = string.IsNullOrWhiteSpace(this.FilterBy) ? null : new FilterBys(JObject.Parse(this.FilterBy));
			else if ("Filter".IsEquals(name))
				this.FilterBy = this.Filter?.ToJson().ToString(Formatting.None);
			else if ("SortBy".IsEquals(name))
				this.Sorts = string.IsNullOrWhiteSpace(this.SortBy) ? null : JArray.Parse(this.SortBy).Select(sort => new SortBy(sort  as JObject)).ToList();
			else if ("Sorts".IsEquals(name))
				this.SortBy = this.Sorts?.Select(sort => sort.ToJson()).ToJArray().ToString(Formatting.None);
		}

		public override JObject ToJson(bool addTypeOfExtendedProperties = false, Action<JObject> onCompleted = null)
			=> base.ToJson(addTypeOfExtendedProperties, json =>
			{
				if (this.Filter == null || this.Sorts == null)
					this.Prepare(true, json);
				onCompleted?.Invoke(json);
			});

		internal Expression Prepare(bool set = true, JObject json = null)
		{
			this.Filter = this.Filter ?? new FilterBys(JObject.Parse(this.FilterBy));
			this.Sorts = this.Sorts ?? JArray.Parse(this.SortBy).Select(sort => sort as JObject).Select(sort => new SortBy(sort)).ToList();
			if (set)
				this.Set();
			if (json != null)
			{
				json["Filter"] = this.Filter.ToJson();
				json["Sorts"] = this.Sorts.Select(sort => sort.ToJson()).ToJArray();
			}
			return this;
		}
	}
}
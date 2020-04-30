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
	[Entity(CollectionName = "CMS_Items", TableName = "T_Portals_CMS_Items", CacheClass = typeof(Utility), CacheName = "Cache", Searchable = true, ObjectName = "Link", ID = "B0000000000000000000000000000003", Title = "Item", Description = "Simple content in the CMS module", MultipleIntances = true, Indexable = true, Extendable = true, ExtendedPropertiesBefore = "Created")]
	public sealed class Item : Repository<Item>, IBusinessObject, IAliasEntity
	{
		public Item() : base() { }

		[JsonConverter(typeof(StringEnumConverter)), BsonRepresentation(MongoDB.Bson.BsonType.String)]
		[Sortable(IndexName = "Management")]
		[FormControl(Segment = "basic", Label = "{{portals.cms.item.controls.[name].label}}", PlaceHolder = "{{portals.cms.item.controls.[name].placeholder}}", Description = "{{portals.cms.item.controls.[name].description}}")]
		public ApprovalStatus Status { get; set; } = ApprovalStatus.Published;

		[Property(MaxLength = 250, NotNull = true, NotEmpty = true)]
		[Sortable(IndexName = "Title")]
		[Searchable]
		[FormControl(Segment = "basic", Label = "{{portals.cms.item.controls.[name].label}}", PlaceHolder = "{{portals.cms.item.controls.[name].placeholder}}", Description = "{{portals.cms.item.controls.[name].description}}")]
		public override string Title { get; set; }

		[Property(MaxLength = 250, NotNull = true, NotEmpty = true)]
		[Alias]
		[Searchable]
		[FormControl(Hidden = true)]
		public string Alias { get; set; }

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
		public override RepositoryBase Parent => this.ContentType;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		IBusinessEntity IBusinessEntity.Parent => null;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		IPortalObject IPortalObject.Parent => this.ContentType;

		public IBusinessEntity GetByAlias(string repositoryEntityID, string alias, string parentIdentity = null)
			=> Item.GetItemByAlias(repositoryEntityID, alias);

		public async Task<IBusinessEntity> GetByAliasAsync(string repositoryEntityID, string alias, string parentIdentity = null, CancellationToken cancellationToken = default)
			=> await Item.GetItemByAliasAsync(repositoryEntityID, alias, cancellationToken).ConfigureAwait(false);

		internal static Item GetItemByAlias(string repositoryEntityID, string alias)
			=> !string.IsNullOrWhiteSpace(repositoryEntityID) && !string.IsNullOrWhiteSpace(alias)
				? Item.Get(Filters<Item>.And(Filters<Item>.Equals("RepositoryEntityID", repositoryEntityID), Filters<Item>.Equals("Alias", alias)), null, repositoryEntityID)
				: null;

		internal static async Task<Item> GetItemByAliasAsync(string repositoryEntityID, string alias, CancellationToken cancellationToken = default)
			=> !string.IsNullOrWhiteSpace(repositoryEntityID) && !string.IsNullOrWhiteSpace(alias)
				? await Item.GetAsync(Filters<Item>.And(Filters<Item>.Equals("RepositoryEntityID", repositoryEntityID), Filters<Item>.Equals("Alias", alias)), null, repositoryEntityID, cancellationToken).ConfigureAwait(false)
				: null;
	}
}
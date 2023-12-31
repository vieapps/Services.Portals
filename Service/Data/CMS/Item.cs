#region Related components
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Serialization;
using MsgPack.Serialization;
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
	[BsonIgnoreExtraElements, DebuggerDisplay("ID = {ID}, Title = {Title}")]
	[Entity(CollectionName = "CMS_Items", TableName = "T_Portals_CMS_Items", CacheClass = typeof(Utility), CacheName = "Cache", Searchable = true, ID = "B0000000000000000000000000000003", Title = "Item", Description = "Simple content in the CMS module", ObjectNamePrefix = "CMS.", MultipleIntances = true, Indexable = true, Extendable = true)]
	public sealed class Item : Repository<Item>, IBusinessObject, IAliasEntity
	{
		public Item() : base() { }

		[Property(MaxLength = 250, NotNull = true, NotEmpty = true)]
		[Sortable(IndexName = "Title")]
		[Searchable]
		[FormControl(Segment = "basic", Label = "{{portals.cms.items.controls.[name].label}}", PlaceHolder = "{{portals.cms.items.controls.[name].placeholder}}", Description = "{{portals.cms.items.controls.[name].description}}")]
		public override string Title { get; set; }

		[Searchable]
		[FormControl(Segment = "basic", ControlType = "TextArea", Label = "{{portals.cms.items.controls.[name].label}}", PlaceHolder = "{{portals.cms.items.controls.[name].placeholder}}", Description = "{{portals.cms.items.controls.[name].description}}")]
		public string Summary { get; set; }

		[Property(MaxLength = 250)]
		[Sortable(IndexName = "Tags")]
		[Searchable]
		[FormControl(Segment = "basic", Label = "{{portals.cms.items.controls.[name].label}}", PlaceHolder = "{{portals.cms.items.controls.[name].placeholder}}", Description = "{{portals.cms.items.controls.[name].description}}")]
		public string Tags { get; set; }

		[JsonConverter(typeof(StringEnumConverter)), BsonRepresentation(MongoDB.Bson.BsonType.String)]
		[Sortable(IndexName = "Management")]
		[FormControl(Segment = "basic", Label = "{{portals.cms.items.controls.[name].label}}", PlaceHolder = "{{portals.cms.items.controls.[name].placeholder}}", Description = "{{portals.cms.items.controls.[name].description}}")]
		public ApprovalStatus Status { get; set; } = ApprovalStatus.Published;

		[Property(MaxLength = 250, NotNull = true, NotEmpty = true)]
		[Alias, Searchable]
		[FormControl(Segment = "basic", Label = "{{portals.cms.items.controls.[name].label}}", PlaceHolder = "{{portals.cms.items.controls.[name].placeholder}}", Description = "{{portals.cms.items.controls.[name].description}}")]
		public string Alias { get; set; }

		[Sortable(IndexName = "Management")]
		[FormControl(Segment = "basic", Label = "{{portals.cms.items.controls.[name].label}}", PlaceHolder = "{{portals.cms.items.controls.[name].placeholder}}", Description = "{{portals.cms.items.controls.[name].description}}")]
		public bool AllowComments { get; set; } = false;

		[FormControl(Segment = "basic", Label = "{{portals.cms.items.controls.[name].label}}", PlaceHolder = "{{portals.cms.items.controls.[name].placeholder}}", Description = "{{portals.cms.items.controls.[name].description}}")]
		public string InlineScripts { get; set; }

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

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore, MessagePackIgnore]
		public string OrganizationID => this.SystemID;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore, MessagePackIgnore]
		public Organization Organization => (this.OrganizationID ?? "").GetOrganizationByID();

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore, MessagePackIgnore]
		IPortalObject IBusinessObject.Organization => this.Organization;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore, MessagePackIgnore]
		public string ModuleID => this.RepositoryID;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore, MessagePackIgnore]
		public Module Module => (this.ModuleID ?? "").GetModuleByID();

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore, MessagePackIgnore]
		IPortalModule IBusinessObject.Module => this.Module;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore, MessagePackIgnore]
		public string ContentTypeID => this.RepositoryEntityID;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore, MessagePackIgnore]
		public ContentType ContentType => (this.ContentTypeID ?? "").GetContentTypeByID();

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore, MessagePackIgnore]
		IPortalContentType IBusinessObject.ContentType => this.ContentType;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore, MessagePackIgnore]
		public override RepositoryBase Parent => this.ContentType;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore, MessagePackIgnore]
		IBusinessEntity IBusinessEntity.Parent => null;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore, MessagePackIgnore]
		IPortalObject IPortalObject.Parent => this.ContentType;

		public override JObject ToJson(bool addTypeOfExtendedProperties, Action<JObject> onCompleted = null)
			=> base.ToJson(addTypeOfExtendedProperties, json =>
			{
				if (!string.IsNullOrWhiteSpace(this.ContentType?.SubTitleFormula) && json.Get<string>("SubTitle") == null)
					json["SubTitle"] = this.ContentType.SubTitleFormula.StartsWith("@")
						? this.ContentType.SubTitleFormula.Evaluate(json.ToExpandoObject())?.ToString()
						: this.ContentType.SubTitleFormula.Format(this.ContentType.SubTitleFormula.PrepareDoubleBracesParameters(json.ToExpandoObject()));
				onCompleted?.Invoke(json);
			});

		public string GetURL(string desktop = null, bool addPageNumberHolder = false, string parentIdentity = null)
			=> $"~/{this.ContentType?.Desktop?.Alias ?? desktop ?? "-default"}/{parentIdentity ?? this.ContentType?.Title?.GetANSIUri() ?? "-"}/{this.Alias}{(addPageNumberHolder ? "/{{pageNumber}}" : "")}{(this.Organization != null && this.Organization.AlwaysUseHtmlSuffix ? ".html" : "")}";

		public IAliasEntity GetByAlias(string repositoryEntityID, string alias, string parentIdentity = null)
			=> Item.GetItemByAlias(repositoryEntityID, alias);

		public async Task<IAliasEntity> GetByAliasAsync(string repositoryEntityID, string alias, string parentIdentity = null, CancellationToken cancellationToken = default)
			=> await Item.GetItemByAliasAsync(repositoryEntityID, alias, cancellationToken).ConfigureAwait(false);

		internal static Item GetItemByAlias(ContentType contentType, string alias)
		{
			if (contentType == null || string.IsNullOrWhiteSpace(alias))
				return null;

			var cacheKey = contentType.ID.GetCacheKeyOfAliasedItem(alias);
			var id = Utility.Cache.Get<string>(cacheKey);
			if (!string.IsNullOrWhiteSpace(id) && id.IsValidUUID())
				return Item.Get<Item>(id);

			var item = Item.Get(Filters<Item>.And(Filters<Item>.Equals("RepositoryEntityID", contentType.ID), Filters<Item>.Equals("Alias", alias.NormalizeAlias())), null, contentType.ID);
			if (item != null)
				Task.WhenAll
				(
					Utility.Cache.SetAsync(cacheKey, item.ID, Utility.CancellationToken),
					Utility.Cache.AddSetMemberAsync(contentType.GetSetCacheKey(), cacheKey, Utility.CancellationToken)
				).Run();
			return item;
		}

		internal static Item GetItemByAlias(string repositoryEntityID, string alias)
			=> string.IsNullOrWhiteSpace(repositoryEntityID) || string.IsNullOrWhiteSpace(alias)
				? null
				: Item.GetItemByAlias(repositoryEntityID.GetContentTypeByID(), alias);

		internal static async Task<Item> GetItemByAliasAsync(ContentType contentType, string alias, CancellationToken cancellationToken = default)
		{
			if (contentType == null || string.IsNullOrWhiteSpace(alias))
				return null;

			var cacheKey = contentType.ID.GetCacheKeyOfAliasedItem(alias);
			var id = await Utility.Cache.GetAsync<string>(cacheKey, cancellationToken).ConfigureAwait(false);
			if (!string.IsNullOrWhiteSpace(id) && id.IsValidUUID())
				return await Item.GetAsync<Item>(id, cancellationToken).ConfigureAwait(false);

			var item = await Item.GetAsync(Filters<Item>.And(Filters<Item>.Equals("RepositoryEntityID", contentType.ID), Filters<Item>.Equals("Alias", alias.NormalizeAlias())), null, contentType.ID, cancellationToken).ConfigureAwait(false);
			if (item != null)
				Task.WhenAll
				(
					Utility.Cache.SetAsync(cacheKey, item.ID, Utility.CancellationToken),
					Utility.Cache.AddSetMemberAsync(contentType.GetSetCacheKey(), cacheKey, Utility.CancellationToken)
				).Run();
			return item;
		}

		internal static Task<Item> GetItemByAliasAsync(string repositoryEntityID, string alias, CancellationToken cancellationToken = default)
			=> string.IsNullOrWhiteSpace(repositoryEntityID) || string.IsNullOrWhiteSpace(alias)
				? Task.FromResult<Item>(null)
				: Item.GetItemByAliasAsync(repositoryEntityID.GetContentTypeByID(), alias, cancellationToken);
	}
}
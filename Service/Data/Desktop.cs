#region Related components
using System;
using System.Linq;
using System.Diagnostics;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Serialization;

using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

using net.vieapps.Components.Security;
using net.vieapps.Components.Repository;
#endregion

namespace net.vieapps.Services.Portals
{
	[Serializable, BsonIgnoreExtraElements, DebuggerDisplay("ID = {ID}, Title = {Title}")]
	[Entity(CollectionName = "Desktops", TableName = "T_Portals_Desktops", CacheClass = typeof(Utility), CacheName = "Cache", Searchable = true)]
	public class Desktop : Repository<Desktop>, IBusinessEntity, IPortalObject, INestedObject
	{
		public Desktop() : base()
			=> this.ID = "";

		#region Properties
		[Property(MaxLength = 32, NotNull = true, NotEmpty = true), Sortable(IndexName = "Management", UniqueIndexName = "Alias"), FormControl(Hidden = true)]
		public override string SystemID { get; set; }

		[Property(MaxLength = 32), Sortable(IndexName = "Management"), FormControl(Hidden = true)]
		public string ParentID { get; set; }

		[Sortable(IndexName = "Management"), FormControl(Hidden = true)]
		public int OrderIndex { get; set; } = 0;

		[Property(MaxLength = 250, NotNull = true, NotEmpty = true), Sortable(IndexName = "Title"), Searchable, FormControl(Label = "{{portals.desktops.controls.[name]}}")]
		public override string Title { get; set; } = "";

		[Property(MaxLength = 100, NotNull = true, NotEmpty = true), Sortable(UniqueIndexName = "Alias"), Searchable, FormControl(Label = "{{portals.desktops.controls.[name]}}")]
		public string Alias { get; set; } = "";

		[Property(MaxLength = 250), FormControl(Label = "{{portals.desktops.controls.[name]}}")]
		public string Aliases { get; set; }

		[Property(MaxLength = 100), Sortable(UniqueIndexName ="Domains"), Searchable, FormControl(Label = "{{portals.desktops.controls.[name]}}")]
		public string PrimaryDomain { get; set; } = "company.com";

		[Property(MaxLength = 5), FormControl(Label = "{{portals.desktops.controls.[name]}}")]
		public string Language { get; set; }

		[Property(MaxLength = 100), FormControl(Label = "{{portals.desktops.controls.[name]}}")]
		public string Theme { get; set; }

		[XmlIgnore, Property(IsCLOB = true), FormControl(Excluded = true)]
		public string Templates { get; set; }

		[Property(MaxLength = 32), FormControl(Label = "{{portals.desktops.controls.[name]}}")]
		public string MainPortletID { get; set; }

		[XmlIgnore, Property(IsCLOB = true), FormControl(Excluded = true)]
		public string OtherSettings { get; set; }

		[Sortable(IndexName = "Audits"), FormControl(Hidden = true)]
		public DateTime Created { get; set; } = DateTime.Now;

		[Sortable(IndexName = "Audits"), FormControl(Hidden = true)]
		public string CreatedID { get; set; } = "";

		[Sortable(IndexName = "Audits"), FormControl(Hidden = true)]
		public DateTime LastModified { get; set; } = DateTime.Now;

		[Sortable(IndexName = "Audits"), FormControl(Hidden = true)]
		public string LastModifiedID { get; set; } = "";
		#endregion

		#region Other properties of IBusinessEntity, IPortalObject & INestedObject
		[JsonIgnore, XmlIgnore, BsonIgnore, Ignore]
		IBusinessEntity IBusinessEntity.Parent => this.ParentDesktop ?? this.Organization as IBusinessEntity;

		[JsonIgnore, XmlIgnore, BsonIgnore, Ignore]
		public override string RepositoryID { get; set; }

		[JsonIgnore, XmlIgnore, BsonIgnore, Ignore]
		public override string EntityID { get; set; }

		[JsonIgnore, XmlIgnore, BsonIgnore, Ignore]
		public string OrganizationID => this.SystemID;

		[JsonIgnore, XmlIgnore, BsonIgnore, Ignore]
		public string ModuleID => this.RepositoryID;

		[JsonIgnore, XmlIgnore, BsonIgnore, Ignore]
		public string ContentTypeID => this.EntityID;

		[JsonIgnore, XmlIgnore, BsonIgnore, Ignore]
		IPortalObject IPortalObject.Parent => this.ParentDesktop ?? this.Organization as IPortalObject;

		[JsonIgnore, XmlIgnore, BsonIgnore, Ignore]
		public string FullTitle => this.Title;

		[JsonIgnore, XmlIgnore, BsonIgnore, Ignore]
		INestedObject INestedObject.Parent => this.ParentDesktop;

		public List<INestedObject> Children => Desktop.FindByParentID(this.SystemID, this.ID).Select(desktop => desktop as INestedObject).ToList();
		#endregion

		[JsonIgnore, XmlIgnore, BsonIgnore, Ignore]
		public Organization Organization => Organization.GetByID(this.OrganizationID);

		[JsonIgnore, XmlIgnore, BsonIgnore, Ignore]
		public Desktop ParentDesktop => Desktop.GetByID(this.ParentID);

		public static Desktop GetByID(string id)
			=> Desktop.Get<Desktop>(id);

		public static Task<Desktop> GetByIDAsync(string id, CancellationToken cancellationToken = default)
			=> Desktop.GetAsync<Desktop>(id, cancellationToken);

		public static Desktop GetByAlias(string systemID, string alias)
			=> string.IsNullOrWhiteSpace(alias)
				? null
				: Desktop.Get(Filters<Desktop>.And(Filters<Desktop>.Equals("SystemID", systemID), Filters<Desktop>.Equals("Alias", alias)), null, null);

		public static Task<Desktop> GetByAliasAsync(string systemID, string alias, CancellationToken cancellationToken = default)
			=> string.IsNullOrWhiteSpace(alias)
				? Task.FromResult<Desktop>(null)
				: Desktop.GetAsync(Filters<Desktop>.And(Filters<Desktop>.Equals("SystemID", systemID), Filters<Desktop>.Equals("Alias", alias)), null, null, cancellationToken);

		public static List<Desktop> FindByParentID(string systemID, string parentID)
			=> string.IsNullOrWhiteSpace(parentID)
				? new List<Desktop>()
				: Desktop.Find(Filters<Desktop>.And(Filters<Desktop>.Equals("SystemID", systemID), string.IsNullOrWhiteSpace(parentID) ? Filters<Desktop>.IsNull("ParentID") : Filters<Desktop>.Equals("ParentID", parentID)), Sorts<Desktop>.Ascending("OrderIndex"), 0, 1, $"desktops:{systemID.ToLower().Trim()}{(string.IsNullOrWhiteSpace(parentID) ? "" : $":{parentID.ToLower().Trim()}")}");

		public static Task<List<Desktop>> FindByParentIDAsync(string systemID, string parentID, CancellationToken cancellationToken = default)
			=> string.IsNullOrWhiteSpace(parentID)
				? Task.FromResult(new List<Desktop>())
				: Desktop.FindAsync(Filters<Desktop>.And(Filters<Desktop>.Equals("SystemID", systemID), string.IsNullOrWhiteSpace(parentID) ? Filters<Desktop>.IsNull("ParentID") : Filters<Desktop>.Equals("ParentID", parentID)), Sorts<Desktop>.Ascending("OrderIndex"), 0, 1, $"desktops:{systemID.ToLower().Trim()}{(string.IsNullOrWhiteSpace(parentID) ? "" : $":{parentID.ToLower().Trim()}")}", cancellationToken);
	}
}
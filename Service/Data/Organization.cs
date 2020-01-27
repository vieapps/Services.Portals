#region Related components
using System;
using System.Linq;
using System.Diagnostics;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Serialization;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Converters;

using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

using net.vieapps.Components.Utility;
using net.vieapps.Components.Repository;
using net.vieapps.Components.Security;
#endregion

namespace net.vieapps.Services.Portals
{
	[Serializable, BsonIgnoreExtraElements, DebuggerDisplay("ID = {ID}, Title = {Title}")]
	[Entity(CollectionName = "Organizations", TableName = "T_Portals_Organizations", CacheClass = typeof(Utility), CacheName = "Cache", Searchable = true)]
	public sealed class Organization : Repository<Organization>, IBusinessEntity, IPortalObject
	{
		public Organization() : base()
			=> this.ID = "";

		#region Properties
		[Property(MaxLength = 32), Sortable(IndexName = "Management"), FormControl(Excluded = true)]
		public string OwnerID { get; set; }

		[JsonConverter(typeof(StringEnumConverter)), BsonRepresentation(BsonType.String), Sortable(IndexName = "Management"), FormControl(Label = "{{portals.organizations.controls.[name]}}")]
		public ApprovalStatus Status { get; set; } = ApprovalStatus.Pending;

		[Property(MaxLength = 100, NotNull = true, NotEmpty = true), Sortable(IndexName = "Management", UniqueIndexName = "Alias"), FormControl(Label = "{{portals.organizations.controls.[name]}}")]
		public string Alias { get; set; } = "";

		[Property(MaxLength = 10, NotNull = true, NotEmpty = true), Sortable(IndexName = "Management"), FormControl(Label = "{{portals.organizations.controls.[name]}}")]
		public string ExpiredDate { get; set; } = "-";

		[Property(MaxLength = 250, NotNull = true, NotEmpty = true), Sortable(IndexName = "Title"), Searchable, FormControl(Label = "{{portals.organizations.controls.[name]}}")]
		public override string Title { get; set; } = "";

		[FormControl(Label = "{{portals.organizations.controls.[name]}}")]
		public string Description { get; set; }

		[Property(MaxLength = 100), FormControl(Label = "{{portals.organizations.controls.[name]}}")]
		public string Theme { get; set; } = "default";

		[Property(MaxLength = 32), FormControl(Label = "{{portals.organizations.controls.[name]}}")]
		public string HomeDesktopID { get; set; }

		[Property(MaxLength = 32), FormControl(Label = "{{portals.organizations.controls.[name]}}")]
		public string SearchDesktopID { get; set; }

		[Sortable(IndexName = "Management"), FormControl(Label = "{{portals.organizations.controls.[name]}}")]
		public long FilesQuotes { get; set; } = 0;

		[Sortable(IndexName = "Management"), FormControl(Label = "{{portals.organizations.controls.[name]}}")]
		public bool Required2FA { get; set; } = false;

		[Sortable(IndexName = "Management"), FormControl(Label = "{{portals.organizations.controls.[name]}}")]
		public bool TrackDownloadFiles { get; set; } = false;

		[XmlIgnore, Property(IsCLOB = true), FormControl(Excluded = true)]
		public string OtherSettings { get; set; }

		[Property(MaxLength = 100), Sortable(IndexName = "Refers"), FormControl(Excluded = true)]
		public string ReferSection { get; set; } = "";

		[Property(MaxLength = 65), Sortable(IndexName = "Refers"), FormControl(Excluded = true)]
		public string ReferIDs { get; set; } = "";

		[JsonIgnore, XmlIgnore, AsJson, FormControl(Excluded = true)]
		public override Privileges OriginalPrivileges { get; set; } = new Privileges(true);

		[Sortable(IndexName = "Audits"), FormControl(Hidden = true)]
		public DateTime Created { get; set; } = DateTime.Now;

		[Sortable(IndexName = "Audits"), FormControl(Hidden = true)]
		public string CreatedID { get; set; } = "";

		[Sortable(IndexName = "Audits"), FormControl(Hidden = true)]
		public DateTime LastModified { get; set; } = DateTime.Now;

		[Sortable(IndexName = "Audits"), FormControl(Hidden = true)]
		public string LastModifiedID { get; set; } = "";
		#endregion

		#region Other properties of IBusinessEntity & IPortalObject
		[JsonIgnore, XmlIgnore, BsonIgnore, Ignore]
		IBusinessEntity IBusinessEntity.Parent => null;

		[JsonIgnore, XmlIgnore, BsonIgnore, Ignore]
		public override string SystemID { get; set; }

		[JsonIgnore, XmlIgnore, BsonIgnore, Ignore]
		public override string RepositoryID { get; set; }

		[JsonIgnore, XmlIgnore, BsonIgnore, Ignore]
		public override string EntityID { get; set; }

		[JsonIgnore, XmlIgnore, BsonIgnore, Ignore]
		public override Privileges WorkingPrivileges => this.OriginalPrivileges ?? new Privileges(true);

		[JsonIgnore, XmlIgnore, BsonIgnore, Ignore]
		public string OrganizationID => this.ID;

		[JsonIgnore, XmlIgnore, BsonIgnore, Ignore]
		IPortalObject IPortalObject.Parent => null;

		[JsonIgnore, XmlIgnore, BsonIgnore, Ignore]
		public string ModuleID => this.RepositoryID;

		[JsonIgnore, XmlIgnore, BsonIgnore, Ignore]
		public string ContentTypeID => this.EntityID;
		#endregion

		[JsonIgnore, XmlIgnore, BsonIgnore, Ignore]
		public Desktop HomeDesktop => Desktop.GetByID(this.HomeDesktopID);

		[JsonIgnore, XmlIgnore, BsonIgnore, Ignore]
		public Desktop SearchDesktop => Desktop.GetByID(this.SearchDesktopID);

		public static Organization GetByID(string id)
			=> Organization.Get<Organization>(id);

		public static Task<Organization> GetByIDAsync(string id, CancellationToken cancellationToken = default)
			=> Organization.GetAsync<Organization>(id, cancellationToken);

		public static Organization GetByAlias(string alias)
			=> string.IsNullOrWhiteSpace(alias)
				? null
				: Organization.Get(Filters<Organization>.Equals("Alias", alias), null, null);

		public static Task<Organization> GetByAliasAsync(string alias, CancellationToken cancellationToken = default)
			=> string.IsNullOrWhiteSpace(alias)
				? Task.FromResult<Organization>(null)
				: Organization.GetAsync(Filters<Organization>.Equals("Alias", alias), null, null, cancellationToken);
	}
}
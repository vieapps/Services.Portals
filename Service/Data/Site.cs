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
	[Entity(CollectionName = "Sites", TableName = "T_Portals_Sites", CacheClass = typeof(Utility), CacheName = "Cache", Searchable = true)]
	public class Site : Repository<Site>, IBusinessEntity, IPortalObject
	{
		public Site() : base()
			=> this.ID = "";

		#region Properties
		[Property(MaxLength = 32, NotNull = true, NotEmpty = true), Sortable(IndexName = "Management"), FormControl(Hidden = true)]
		public override string SystemID { get; set; }

		[JsonConverter(typeof(StringEnumConverter)), BsonRepresentation(BsonType.String), Sortable(IndexName = "Management"), FormControl(Label = "{{portals.sites.controls.[name]}}")]
		public ApprovalStatus Status { get; set; } = ApprovalStatus.Pending;

		[Property(MaxLength = 250, NotNull = true, NotEmpty = true), Sortable(IndexName = "Title"), Searchable, FormControl(Label = "{{portals.sites.controls.[name]}}")]
		public override string Title { get; set; } = "";

		[FormControl(Label = "{{portals.sites.controls.[name]}}")]
		public string Description { get; set; }

		[Property(MaxLength = 100), Sortable(UniqueIndexName ="Domains"), Searchable, FormControl(Label = "{{portals.sites.controls.[name]}}")]
		public string PrimaryDomain { get; set; } = "company.com";

		[Property(MaxLength = 20), Sortable(UniqueIndexName = "Domains"), FormControl(Label = "{{portals.sites.controls.[name]}}")]
		public string SubDomain { get; set; } = "*";

		[Property(MaxLength = 1000), FormControl(Label = "{{portals.sites.controls.[name]}}")]
		public string OtherDomains { get; set; }

		[Property(MaxLength = 5), FormControl(Label = "{{portals.sites.controls.[name]}}")]
		public string Language { get; set; } = "vi-VN";

		[Property(MaxLength = 100), FormControl(Label = "{{portals.sites.controls.[name]}}")]
		public string Theme { get; set; }

		[Property(MaxLength = 32), FormControl(Label = "{{portals.sites.controls.[name]}}")]
		public string HomeDesktopID { get; set; }

		[Property(MaxLength = 32), FormControl(Label = "{{portals.sites.controls.[name]}}")]
		public string SearchDesktopID { get; set; }

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

		#region Other properties of IBusinessEntity & IPortalObject
		[JsonIgnore, XmlIgnore, BsonIgnore, Ignore]
		IBusinessEntity IBusinessEntity.Parent => this.Organization;

		[JsonIgnore, XmlIgnore, BsonIgnore, Ignore]
		public override string RepositoryID { get; set; }

		[JsonIgnore, XmlIgnore, BsonIgnore, Ignore]
		public override string EntityID { get; set; }

		[JsonIgnore, XmlIgnore, BsonIgnore, Ignore]
		public override Privileges OriginalPrivileges { get; set; }

		[JsonIgnore, XmlIgnore, BsonIgnore, Ignore]
		public override Privileges WorkingPrivileges => this.Organization?.WorkingPrivileges;

		[JsonIgnore, XmlIgnore, BsonIgnore, Ignore]
		public string OrganizationID => this.SystemID;

		[JsonIgnore, XmlIgnore, BsonIgnore, Ignore]
		public string ModuleID => this.RepositoryID;

		[JsonIgnore, XmlIgnore, BsonIgnore, Ignore]
		public string ContentTypeID => this.EntityID;

		[JsonIgnore, XmlIgnore, BsonIgnore, Ignore]
		IPortalObject IPortalObject.Parent => this.Organization;
		#endregion

		[JsonIgnore, XmlIgnore, BsonIgnore, Ignore]
		public Desktop HomeDesktop => Desktop.GetByID(this.HomeDesktopID) ?? this.Organization?.HomeDesktop;

		[JsonIgnore, XmlIgnore, BsonIgnore, Ignore]
		public Desktop SearchDesktop => Desktop.GetByID(this.SearchDesktopID) ?? this.Organization?.SearchDesktop;

		[JsonIgnore, XmlIgnore, BsonIgnore, Ignore]
		public Organization Organization => Organization.GetByID(this.OrganizationID);

		public static Site GetByID(string id)
			=> Site.Get<Site>(id);

		public static Task<Site> GetByIDAsync(string id, CancellationToken cancellationToken = default)
			=> Site.GetAsync<Site>(id, cancellationToken);
	}
}
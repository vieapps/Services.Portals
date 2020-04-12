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
	public sealed class Site : Repository<Site>, IPortalObject
	{
		public Site() : base()
			=> this.ID = "";

		[JsonConverter(typeof(StringEnumConverter)), BsonRepresentation(BsonType.String), Sortable(IndexName = "Management"), FormControl(Label = "{{portals.sites.controls.[name]}}", PlaceHolder = "{{portals.sites.controls.[name].placeholder}}", Description = "{{portals.sites.controls.[name].description}}")]
		public ApprovalStatus Status { get; set; } = ApprovalStatus.Pending;

		[Property(MaxLength = 250, NotNull = true, NotEmpty = true), Sortable(IndexName = "Title"), Searchable, FormControl(Label = "{{portals.sites.controls.[name]}}", PlaceHolder = "{{portals.sites.controls.[name].placeholder}}", Description = "{{portals.sites.controls.[name].description}}")]
		public override string Title { get; set; } = "";

		[FormControl(Label = "{{portals.sites.controls.[name]}}", PlaceHolder = "{{portals.sites.controls.[name].placeholder}}", Description = "{{portals.sites.controls.[name].description}}")]
		public string Description { get; set; }

		[Property(MaxLength = 100), Sortable(UniqueIndexName ="Domains"), Searchable, FormControl(Label = "{{portals.sites.controls.[name]}}", PlaceHolder = "{{portals.sites.controls.[name].placeholder}}", Description = "{{portals.sites.controls.[name].description}}")]
		public string PrimaryDomain { get; set; } = "company.com";

		[Property(MaxLength = 20), Sortable(UniqueIndexName = "Domains"), FormControl(Label = "{{portals.sites.controls.[name]}}", PlaceHolder = "{{portals.sites.controls.[name].placeholder}}", Description = "{{portals.sites.controls.[name].description}}")]
		public string SubDomain { get; set; } = "*";

		[Property(MaxLength = 1000), FormControl(Label = "{{portals.sites.controls.[name]}}", PlaceHolder = "{{portals.sites.controls.[name].placeholder}}", Description = "{{portals.sites.controls.[name].description}}")]
		public string OtherDomains { get; set; }

		[Property(MaxLength = 5), FormControl(Label = "{{portals.sites.controls.[name]}}", PlaceHolder = "{{portals.sites.controls.[name].placeholder}}", Description = "{{portals.sites.controls.[name].description}}")]
		public string Language { get; set; } = "vi-VN";

		[Property(MaxLength = 100), FormControl(Label = "{{portals.sites.controls.[name]}}", PlaceHolder = "{{portals.sites.controls.[name].placeholder}}", Description = "{{portals.sites.controls.[name].description}}")]
		public string Theme { get; set; }

		[Property(MaxLength = 32), FormControl(Label = "{{portals.sites.controls.[name]}}", PlaceHolder = "{{portals.sites.controls.[name].placeholder}}", Description = "{{portals.sites.controls.[name].description}}")]
		public string HomeDesktopID { get; set; }

		[Property(MaxLength = 32), FormControl(Label = "{{portals.sites.controls.[name]}}", PlaceHolder = "{{portals.sites.controls.[name].placeholder}}", Description = "{{portals.sites.controls.[name].description}}")]
		public string SearchDesktopID { get; set; }

		[Property(IsCLOB = true), FormControl(Excluded = true), XmlIgnore]
		public string OtherSettings { get; set; }

		[Sortable(IndexName = "Audits"), FormControl(Hidden = true)]
		public DateTime Created { get; set; } = DateTime.Now;

		[Sortable(IndexName = "Audits"), FormControl(Hidden = true)]
		public string CreatedID { get; set; } = "";

		[Sortable(IndexName = "Audits"), FormControl(Hidden = true)]
		public DateTime LastModified { get; set; } = DateTime.Now;

		[Sortable(IndexName = "Audits"), FormControl(Hidden = true)]
		public string LastModifiedID { get; set; } = "";

		[Property(MaxLength = 32, NotNull = true, NotEmpty = true), Sortable(IndexName = "Management"), FormControl(Hidden = true)]
		public override string SystemID { get; set; }

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public override string RepositoryID { get; set; }

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public override string EntityID { get; set; }

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public override Privileges OriginalPrivileges { get; set; }

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public override Privileges WorkingPrivileges => this.Organization?.WorkingPrivileges;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public string OrganizationID => this.SystemID;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public new IPortalObject Parent => this.Organization;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public Organization Organization => Utility.GetOrganizationByID(this.OrganizationID);

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public Desktop HomeDesktop => Utility.GetDesktopByID(this.HomeDesktopID) ?? this.Organization?.HomeDesktop;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public Desktop SearchDesktop => Utility.GetDesktopByID(this.SearchDesktopID) ?? this.Organization?.SearchDesktop;
	}
}
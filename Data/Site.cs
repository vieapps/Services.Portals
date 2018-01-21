#region Related components
using System;
using System.Diagnostics;

using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

using net.vieapps.Components.Security;
using net.vieapps.Components.Repository;
#endregion

namespace net.vieapps.Services.Systems
{
	[Serializable, BsonIgnoreExtraElements, DebuggerDisplay("ID = {ID}, Title = {Title}")]
	[Entity(CollectionName = "Sites", TableName = "T_Core_Sites", CacheClass = typeof(Utility), CacheName = "Cache", Searchable = true,
	Title = "Site", Description = "Information of a site in an organization", ID = "10000000000000000000000000000002")]
	public class Site : Repository<Site>, IBusinessEntity
	{
		public Site() : base()
		{
			this.ID = "";
			this.Status = ApprovalStatus.Pending;
			this.SystemID = "";
			this.PrimaryDomain = "company.com";
			this.SubDomain = "*";
			this.Title = "";
			this.SkinName = "";
			this.HomeDesktopID = "";
			this.SearchDesktopID = "";
			this.DisplaySettings = null;
			this.MessageSettings = null;
			this.InstructionSettings = null;
			this.SEOSettings = null;
			this.Created = DateTime.Now;
			this.CreatedID = "";
			this.LastModified = DateTime.Now;
			this.LastModifiedID = "";
		}

		#region IBusinessEntity properties
		[JsonIgnore, BsonIgnore, Ignore]
		public override string RepositoryID { get; set; }

		[JsonIgnore, BsonIgnore, Ignore]
		public override string EntityID { get; set; }

		[JsonIgnore, BsonIgnore, Ignore]
		public override Privileges OriginalPrivileges { get; set; }
		#endregion

		#region Properties
		[JsonConverter(typeof(StringEnumConverter)), BsonRepresentation(BsonType.String), Sortable(IndexName = "Management")]
		public ApprovalStatus Status { get; set; }

		[Property(MaxLength = 250), Sortable(IndexName = "Title"), Searchable]
		public override string Title { get; set; }

		[Sortable(IndexName = "Management")]
		public override string SystemID { get; set; }

		[Property(MaxLength = 100), Sortable(UniqueIndexName ="Domains"), Searchable]
		public string PrimaryDomain { get; set; }

		[Property(MaxLength = 20), Sortable(UniqueIndexName = "Domains")]
		public string SubDomain { get; set; }

		public string OtherDomains { get; set; }

		[Property(MaxLength = 100), Sortable(IndexName = "Management")]
		public string SkinName { get; set; }

		public string HomeDesktopID { get; set; }

		public string SearchDesktopID { get; set; }

		[Property(IsCLOB = true), AsJson]
		public string DisplaySettings { get; set; }

		[Property(IsCLOB = true), AsJson]
		public string MessageSettings { get; set; }

		[Property(IsCLOB = true), AsJson]
		public string InstructionSettings { get; set; }

		[Property(IsCLOB = true), AsJson]
		public string SEOSettings { get; set; }

		[Sortable(IndexName = "Management")]
		public DateTime Created { get; set; }

		[Sortable(IndexName = "Management")]
		public string CreatedID { get; set; }

		[Sortable(IndexName = "Management")]
		public DateTime LastModified { get; set; }

		[Sortable(IndexName = "Management")]
		public string LastModifiedID { get; set; }

		[JsonIgnore, BsonIgnore, Ignore]
		public override IBusinessEntity Parent
		{
			get
			{
				return string.IsNullOrWhiteSpace(this.SystemID)
					? null
					: Organization.Get<Organization>(this.SystemID);
			}
		}
		#endregion

	}
}
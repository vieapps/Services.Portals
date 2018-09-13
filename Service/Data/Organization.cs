#region Related components
using System;
using System.Linq;
using System.Diagnostics;
using System.Collections.Generic;
using System.Threading.Tasks;

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
	[Entity(CollectionName = "Organizations", TableName = "T_Core_Organizations", CacheClass = typeof(Utility), CacheName = "Cache", Searchable = true, 
	Title = "Organization", Description = "Information of an organization", ID = "10000000000000000000000000000001")]
	public class Organization : Repository<Organization>, IBusinessEntity
	{
		public Organization() : base()
		{
			this.ID = "";
			this.OwnerID = "";
			this.Status = ApprovalStatus.Pending;
			this.Alias = "";
			this.ExpiredDate = "-";
			this.Title = "";
			this.SkinName = "";
			this.HomeDesktopID = "";
			this.SearchDesktopID = "";
			this.FilesDomain = "";
			this.FilesQuotes = 0;
			this.RequiredOTP = false;
			this.TrackDownloadFiles = false;
			this.ReferSection = "";
			this.ReferIDs = "";
			this.SearchServer = 0;
			this.OriginalPrivileges = new Privileges(true);
			this.MessageSettings = null;
			this.InstructionSettings = null;
			this.Created = DateTime.Now;
			this.CreatedID = "";
			this.LastModified = DateTime.Now;
			this.LastModifiedID = "";
		}

		#region Properties
		[Sortable(IndexName = "Management")]
		public string OwnerID { get; set; }

		[JsonConverter(typeof(StringEnumConverter)), BsonRepresentation(BsonType.String), Sortable(IndexName = "Management")]
		public ApprovalStatus Status { get; set; }

		[Property(MaxLength = 100, NotNull = true, NotEmpty = true), Sortable(IndexName = "Management", UniqueIndexName = "Alias")]
		public string Alias { get; set; }

		[Property(MaxLength = 10), Sortable(IndexName = "Management")]
		public string ExpiredDate { get; set; }

		[Property(MaxLength = 250, NotNull = true, NotEmpty = true), Sortable(IndexName = "Title"), Searchable]
		public override string Title { get; set; }

		[Property(MaxLength = 100)]
		public string SkinName { get; set; }

		public string HomeDesktopID { get; set; }

		public string SearchDesktopID { get; set; }

		public string FilesDomain { get; set; }

		[Sortable(IndexName = "Management")]
		public int FilesQuotes { get; set; }

		[Sortable(IndexName = "Management")]
		public bool RequiredOTP { get; set; }

		[Sortable(IndexName = "Management")]
		public bool TrackDownloadFiles { get; set; }

		[Property(MaxLength = 100), Sortable(IndexName = "Refers")]
		public string ReferSection { get; set; }

		[Property(MaxLength = 65), Sortable(IndexName = "Refers")]
		public string ReferIDs { get; set; }

		public int SearchServer { get; set; }

		[AsJson]
		public override Privileges OriginalPrivileges { get; set; }

		[Property(IsCLOB = true), AsJson]
		public string MessageSettings { get; set; }

		[Property(IsCLOB = true), AsJson]
		public string InstructionSettings { get; set; }

		[Sortable(IndexName = "Statistics")]
		public DateTime Created { get; set; }

		[Sortable(IndexName = "Statistics")]
		public string CreatedID { get; set; }

		[Sortable(IndexName = "Statistics")]
		public DateTime LastModified { get; set; }

		[Sortable(IndexName = "Statistics")]
		public string LastModifiedID { get; set; }
		#endregion

		#region IBusinessEntity properties
		[JsonIgnore, BsonIgnore, Ignore]
		public override string SystemID { get; set; }

		[JsonIgnore, BsonIgnore, Ignore]
		public override string RepositoryID { get; set; }

		[JsonIgnore, BsonIgnore, Ignore]
		public override string EntityID { get; set; }
		#endregion

		internal static Task<Organization> GetByAliasAsync(string alias)
		{
			return string.IsNullOrWhiteSpace(alias)
				? null
				: Organization.GetAsync(Filters<Organization>.Equals("Alias", alias));
		}

	}
}
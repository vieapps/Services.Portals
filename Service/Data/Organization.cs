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
	[Entity(CollectionName = "Organizations", TableName = "T_Portals_Organizations", CacheClass = typeof(Utility), CacheName = "Cache", Searchable = true, 
	Title = "Organization", Description = "Information of an organization", ID = "10000000000000000000000000000001")]
	public class Organization : Repository<Organization>, IBusinessEntity
	{
		public Organization() : base() { }

		#region Properties
		[Sortable(IndexName = "Management"), FormControl(Label = "{{portals.organizations.controls.[name]}}")]
		public string OwnerID { get; set; } = "";

		[JsonConverter(typeof(StringEnumConverter)), BsonRepresentation(BsonType.String), Sortable(IndexName = "Management"), FormControl(Label = "{{portals.organizations.controls.[name]}}")]
		public ApprovalStatus Status { get; set; } = ApprovalStatus.Pending;

		[Property(MaxLength = 100, NotNull = true, NotEmpty = true), Sortable(IndexName = "Management", UniqueIndexName = "Alias"), FormControl(Label = "{{portals.organizations.controls.[name]}}")]
		public string Alias { get; set; } = "";

		[Property(MaxLength = 10), Sortable(IndexName = "Management"), FormControl(Label = "{{portals.organizations.controls.[name]}}")]
		public string ExpiredDate { get; set; } = "-";

		[Property(MaxLength = 250, NotNull = true, NotEmpty = true), Sortable(IndexName = "Title"), Searchable, FormControl(Label = "{{portals.organizations.controls.[name]}}")]
		public override string Title { get; set; } = "";

		[Property(MaxLength = 100), FormControl(Label = "{{portals.organizations.controls.[name]}}")]
		public string Theme { get; set; } = "default";

		[FormControl(Label = "{{portals.organizations.controls.[name]}}")]
		public string HomeDesktopID { get; set; } = "";

		[FormControl(Label = "{{portals.organizations.controls.[name]}}")]
		public string SearchDesktopID { get; set; } = "";

		[Property(MaxLength = 100), FormControl(Label = "{{portals.organizations.controls.[name]}}")]
		public string FilesDomain { get; set; } = "";

		[Sortable(IndexName = "Management"), FormControl(Label = "{{portals.organizations.controls.[name]}}")]
		public int FilesQuotes { get; set; } = 0;

		[Sortable(IndexName = "Management"), FormControl(Label = "{{portals.organizations.controls.[name]}}")]
		public bool Required2FA { get; set; } = false;

		[Sortable(IndexName = "Management"), FormControl(Label = "{{portals.organizations.controls.[name]}}")]
		public bool TrackDownloadFiles { get; set; } = false;

		[Property(MaxLength = 100), Sortable(IndexName = "Refers"), FormControl(Label = "{{portals.organizations.controls.[name]}}")]
		public string ReferSection { get; set; } = "";

		[Property(MaxLength = 65), Sortable(IndexName = "Refers"), FormControl(Label = "{{portals.organizations.controls.[name]}}")]
		public string ReferIDs { get; set; } = "";

		[FormControl(Label = "{{portals.organizations.controls.[name]}}")]
		public int SearchServer { get; set; } = 0;

		[AsJson, FormControl(Label = "{{portals.organizations.controls.[name]}}")]
		public override Privileges OriginalPrivileges { get; set; } = new Privileges(true);

		[Property(IsCLOB = true), AsJson]
		public string MessageSettings { get; set; }

		[Property(IsCLOB = true), AsJson]
		public string InstructionSettings { get; set; }

		[Sortable(IndexName = "Statistics"), FormControl(Label = "{{portals.organizations.controls.[name]}}")]
		public DateTime Created { get; set; } = DateTime.Now;

		[Sortable(IndexName = "Statistics"), FormControl(Label = "{{portals.organizations.controls.[name]}}")]
		public string CreatedID { get; set; } = "";

		[Sortable(IndexName = "Statistics"), FormControl(Label = "{{portals.organizations.controls.[name]}}")]
		public DateTime LastModified { get; set; } = DateTime.Now;

		[Sortable(IndexName = "Statistics"), FormControl(Label = "{{portals.organizations.controls.[name]}}")]
		public string LastModifiedID { get; set; } = "";
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
			=> string.IsNullOrWhiteSpace(alias) ? null : Organization.GetAsync(Filters<Organization>.Equals("Alias", alias));
	}
}
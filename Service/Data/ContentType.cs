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
	[Entity(CollectionName = "ContentTypes", TableName = "T_Portals_ContentTypes", CacheClass = typeof(Utility), CacheName = "Cache", Searchable = true)]
	public class ContentType : Repository<ContentType>, IRepositoryEntity, IBusinessEntity, IPortalObject
	{
		public ContentType() : base()
			=> this.ID = "";

		#region Properties
		[Property(MaxLength = 32, NotNull = true, NotEmpty = true), Sortable(IndexName = "Management"), FormControl(Hidden = true)]
		public override string SystemID { get; set; }

		[Property(MaxLength = 32, NotNull = true, NotEmpty = true), Sortable(IndexName = "Management"), FormControl(Hidden = true)]
		public override string RepositoryID { get; set; }

		[Property(MaxLength = 250, NotNull = true, NotEmpty = true), Sortable(IndexName = "Title"), Searchable, FormControl(Label = "{{portals.contenttypes.controls.[name]}}")]
		public override string Title { get; set; } = "";

		[FormControl(Label = "{{portals.contenttypes.controls.[name]}}")]
		public string Description { get; set; }

		[Property(MaxLength = 32), FormControl(Label = "{{portals.contenttypes.controls.[name]}}")]
		public string DesktopID { get; set; }

		[Sortable(IndexName = "Management"), FormControl(Label = "{{portals.contenttypes.controls.[name]}}")]
		public bool CreateNewVersionWhenUpdated { get; set; } = true;

		[Sortable(IndexName = "Management"), FormControl(Label = "{{portals.contenttypes.controls.[name]}}")]
		public bool AllowComments { get; set; } = false;

		[Sortable(IndexName = "Management"), FormControl(Label = "{{portals.contenttypes.controls.[name]}}")]
		public bool UseSocialNetworkComments { get; set; } = false;

		[JsonConverter(typeof(StringEnumConverter)), BsonRepresentation(BsonType.String), FormControl(Label = "{{portals.contenttypes.controls.[name]}}")]
		public ApprovalStatus DefaultCommentStatus { get; set; } = ApprovalStatus.Pending;

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

		#region Other properties of IRepositoryEntity, IBusinessEntity & IPortalObject
		[JsonIgnore, XmlIgnore, BsonIgnore, Ignore]
		IBusinessEntity IBusinessEntity.Parent => this.Module;

		[JsonIgnore, XmlIgnore, BsonIgnore, Ignore]
		public override string EntityID { get; set; }

		[JsonIgnore, XmlIgnore, AsString, FormControl(Excluded = true)]
		public List<ExtendedPropertyDefinition> ExtendedPropertyDefinitions { get; }

		[JsonIgnore, XmlIgnore, AsString, FormControl(Excluded = true)]
		public ExtendedUIDefinition ExtendedUIDefinition { get; }

		[JsonIgnore, XmlIgnore, BsonIgnore, Ignore]
		public EntityDefinition Definition => RepositoryMediator.GetEntityDefinition<ContentType>();

		[JsonIgnore, XmlIgnore, BsonIgnore, Ignore]
		public string OrganizationID => this.SystemID;

		[JsonIgnore, XmlIgnore, BsonIgnore, Ignore]
		public string ModuleID => this.RepositoryID;

		[JsonIgnore, XmlIgnore, BsonIgnore, Ignore]
		public string ContentTypeID => this.EntityID;

		[JsonIgnore, XmlIgnore, BsonIgnore, Ignore]
		IPortalObject IPortalObject.Parent => this.Module;
		#endregion

		[JsonIgnore, XmlIgnore, BsonIgnore, Ignore]
		public Desktop Desktop => Desktop.GetByID(this.DesktopID) ?? this.Module?.Desktop;

		[JsonIgnore, XmlIgnore, BsonIgnore, Ignore]
		public Module Module => Module.GetByID(this.ModuleID);

		[JsonIgnore, XmlIgnore, BsonIgnore, Ignore]
		public Organization Organization => Organization.GetByID(this.OrganizationID);

		public static ContentType GetByID(string id)
			=> ContentType.Get<ContentType>(id);

		public static Task<ContentType> GetByIDAsync(string id, CancellationToken cancellationToken = default)
			=> ContentType.GetAsync<ContentType>(id, cancellationToken);
	}
}
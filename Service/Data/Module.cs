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
	[Entity(CollectionName = "Modules", TableName = "T_Portals_Modules", CacheClass = typeof(Utility), CacheName = "Cache", Searchable = true)]
	public class Module : Repository<Module>, IRepository, IBusinessEntity, IPortalObject
	{
		public Module() : base()
			=> this.ID = "";

		#region Properties
		[Property(MaxLength = 32, NotNull = true, NotEmpty = true), Sortable(IndexName = "Management"), FormControl(Hidden = true)]
		public override string SystemID { get; set; }

		[Property(MaxLength = 250, NotNull = true, NotEmpty = true), Sortable(IndexName = "Title"), Searchable, FormControl(Label = "{{portals.modules.controls.[name]}}")]
		public override string Title { get; set; } = "";

		[FormControl(Label = "{{portals.modules.controls.[name]}}")]
		public string Description { get; set; }

		[Property(MaxLength = 32), FormControl(Label = "{{portals.modules.controls.[name]}}")]
		public string DesktopID { get; set; }

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

		#region Other properties of IRepository, IBusinessEntity & IPortalObject
		[JsonIgnore, XmlIgnore, BsonIgnore, Ignore]
		IBusinessEntity IBusinessEntity.Parent => this.Organization;

		[JsonIgnore, XmlIgnore, BsonIgnore, Ignore]
		public override string RepositoryID { get; set; }

		[JsonIgnore, XmlIgnore, BsonIgnore, Ignore]
		public override string EntityID { get; set; }

		[JsonIgnore, XmlIgnore, BsonIgnore, Ignore]
		public RepositoryDefinition Definition => RepositoryMediator.GetEntityDefinition<Module>().RepositoryDefinition;

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
		public Desktop Desktop => Desktop.GetByID(this.DesktopID) ?? this.Organization?.HomeDesktop;

		[JsonIgnore, XmlIgnore, BsonIgnore, Ignore]
		public Organization Organization => Organization.GetByID(this.OrganizationID);

		public static Module GetByID(string id)
			=> Module.Get<Module>(id);

		public static Task<Module> GetByIDAsync(string id, CancellationToken cancellationToken = default)
			=> Module.GetAsync<Module>(id, cancellationToken);
	}
}
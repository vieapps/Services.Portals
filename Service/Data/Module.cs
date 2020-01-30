﻿#region Related components
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

using net.vieapps.Components.Utility;
using net.vieapps.Components.Security;
using net.vieapps.Components.Repository;
#endregion

namespace net.vieapps.Services.Portals
{
	[Serializable, BsonIgnoreExtraElements, DebuggerDisplay("ID = {ID}, Title = {Title}")]
	[Entity(CollectionName = "Modules", TableName = "T_Portals_Modules", CacheClass = typeof(Utility), CacheName = "Cache", Searchable = true)]
	public sealed class Module : Repository<Module>, IPortalModule
	{
		public Module() : base()
			=> this.ID = "";

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

		[Property(MaxLength = 32, NotNull = true, NotEmpty = true), Sortable(IndexName = "Management"), FormControl(Hidden = true)]
		public override string SystemID { get; set; }

		[JsonIgnore, XmlIgnore, BsonIgnore, Ignore]
		public override string RepositoryID { get; set; }

		[JsonIgnore, XmlIgnore, BsonIgnore, Ignore]
		public override string EntityID { get; set; }

		[JsonIgnore, XmlIgnore, BsonIgnore, Ignore]
		public string OrganizationID => this.SystemID;

		[JsonIgnore, XmlIgnore, BsonIgnore, Ignore]
		IPortalObject IPortalObject.Parent => this.Organization;

		[XmlIgnore]
		public string DefinitionType { get; set; }

		[JsonIgnore, XmlIgnore, BsonIgnore, Ignore]
		public RepositoryDefinition Definition => RepositoryMediator.GetRepositoryDefinition(AssemblyLoader.GetType(this.DefinitionType), true);

		[JsonIgnore, XmlIgnore, BsonIgnore, Ignore]
		public Organization Organization => Utility.GetOrganizationByID(this.OrganizationID);

		[JsonIgnore, XmlIgnore, BsonIgnore, Ignore]
		public Desktop Desktop => Utility.GetDesktopByID(this.DesktopID) ?? this.Organization?.HomeDesktop;
	}
}
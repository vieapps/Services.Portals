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
	public sealed class Desktop : Repository<Desktop>, INestedObject
	{
		public Desktop() : base()
			=> this.ID = "";

		[Property(MaxLength = 250, NotNull = true, NotEmpty = true), Sortable(IndexName = "Title"), Searchable, FormControl(Label = "{{portals.desktops.controls.[name]}}")]
		public override string Title { get; set; } = "";

		[Property(MaxLength = 100, NotNull = true, NotEmpty = true), Sortable(UniqueIndexName = "Alias"), Searchable, FormControl(Label = "{{portals.desktops.controls.[name]}}")]
		public string Alias { get; set; } = "";

		[Property(MaxLength = 250), FormControl(Label = "{{portals.desktops.controls.[name]}}")]
		public string Aliases { get; set; }

		[Property(MaxLength = 5), FormControl(Label = "{{portals.desktops.controls.[name]}}")]
		public string Language { get; set; }

		[Property(MaxLength = 100), FormControl(Label = "{{portals.desktops.controls.[name]}}")]
		public string Theme { get; set; }

		[Property(IsCLOB = true), FormControl(Excluded = true), XmlIgnore]
		public string Templates { get; set; }

		[Property(MaxLength = 32), FormControl(Label = "{{portals.desktops.controls.[name]}}")]
		public string MainPortletID { get; set; }

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

		[Property(MaxLength = 32), Sortable(IndexName = "Management"), FormControl(Hidden = true)]
		public string ParentID { get; set; }

		[Sortable(IndexName = "Management"), FormControl(Hidden = true)]
		public int OrderIndex { get; set; } = 0;

		[Property(MaxLength = 32, NotNull = true, NotEmpty = true), Sortable(IndexName = "Management", UniqueIndexName = "Alias"), FormControl(Hidden = true)]
		public override string SystemID { get; set; }

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public override string RepositoryID { get; set; }

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public override string EntityID { get; set; }

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public string OrganizationID => this.SystemID;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		IPortalObject IPortalObject.Parent => this.ParentDesktop ?? this.Organization as IPortalObject;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public string FullTitle
		{
			get
			{
				var parent = this.ParentDesktop;
				return (parent == null ? "" : $"{parent.FullTitle} > ") + this.Title;
			}
		}

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		INestedObject INestedObject.Parent => this.ParentDesktop;

		public List<INestedObject> Children => this.GetChildren().Select(desktop => desktop as INestedObject).ToList();

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public Organization Organization => Utility.GetOrganizationByID(this.OrganizationID);

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public Desktop ParentDesktop => Utility.GetDesktopByID(this.ParentID);

		internal List<string> ChildrenIDs { get; set; }

		public List<Desktop> GetChildren()
		{
			if (this.ChildrenIDs == null)
			{
				var desktops = Utility.GetDesktopsByParentID(this.SystemID, this.ID);
				this.ChildrenIDs = desktops.Select(desktop => desktop.ID).ToList();
				return desktops;
			}
			return this.ChildrenIDs.Select(id => Utility.GetDesktopByID(id)).ToList();
		}
	}
}
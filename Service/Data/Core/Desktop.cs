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
using MongoDB.Bson.Serialization.Attributes;
using Newtonsoft.Json.Linq;
using net.vieapps.Components.Security;
using net.vieapps.Components.Repository;
using net.vieapps.Components.Utility;
#endregion

namespace net.vieapps.Services.Portals
{
	[Serializable, BsonIgnoreExtraElements, DebuggerDisplay("ID = {ID}, Title = {Title}")]
	[Entity(CollectionName = "Desktops", TableName = "T_Portals_Desktops", CacheClass = typeof(Utility), CacheName = "Cache", Searchable = true)]
	public sealed class Desktop : Repository<Desktop>, INestedObject
	{
		public Desktop() : base() { }

		[Property(MaxLength = 250, NotNull = true, NotEmpty = true), Sortable(IndexName = "Title"), Searchable]
		[FormControl(Label = "{{portals.desktops.controls.[name]}}", PlaceHolder = "{{portals.desktops.controls.[name].placeholder}}", Description = "{{portals.desktops.controls.[name].description}}")]
		public override string Title { get; set; } = "";

		[Property(MaxLength = 100, NotNull = true, NotEmpty = true), Sortable(UniqueIndexName = "Alias"), Searchable]
		[FormControl(Label = "{{portals.desktops.controls.[name]}}", PlaceHolder = "{{portals.desktops.controls.[name].placeholder}}", Description = "{{portals.desktops.controls.[name].description}}")]
		public string Alias { get; set; } = "";

		[Property(MaxLength = 250)]
		[FormControl(Label = "{{portals.desktops.controls.[name]}}", PlaceHolder = "{{portals.desktops.controls.[name].placeholder}}", Description = "{{portals.desktops.controls.[name].description}}")]
		public string Aliases { get; set; }

		[Property(MaxLength = 5)]
		[FormControl(Label = "{{portals.desktops.controls.[name]}}", PlaceHolder = "{{portals.desktops.controls.[name].placeholder}}", Description = "{{portals.desktops.controls.[name].description}}")]
		public string Language { get; set; }

		[Property(MaxLength = 100)]
		[FormControl(Label = "{{portals.desktops.controls.[name]}}", PlaceHolder = "{{portals.desktops.controls.[name].placeholder}}", Description = "{{portals.desktops.controls.[name].description}}")]
		public string Theme { get; set; }

		[Property(IsCLOB = true), XmlIgnore]
		[FormControl(Excluded = true)]
		public string Templates { get; set; }

		[Property(MaxLength = 32)]
		[FormControl(Label = "{{portals.desktops.controls.[name]}}", PlaceHolder = "{{portals.desktops.controls.[name].placeholder}}", Description = "{{portals.desktops.controls.[name].description}}")]
		public string MainPortletID { get; set; }

		[Property(IsCLOB = true), XmlIgnore]
		[FormControl(Excluded = true)]
		public string OtherSettings { get; set; }

		[Sortable(IndexName = "Audits")]
		[FormControl(Hidden = true, Label = "{{portals.desktops.controls.[name].label}}", PlaceHolder = "{{portals.desktops.controls.[name].placeholder}}", Description = "{{portals.desktops.controls.[name].description}}")]
		public DateTime Created { get; set; } = DateTime.Now;

		[Sortable(IndexName = "Audits")]
		[FormControl(Hidden = true, Label = "{{portals.desktops.controls.[name].label}}", PlaceHolder = "{{portals.desktops.controls.[name].placeholder}}", Description = "{{portals.desktops.controls.[name].description}}")]
		public string CreatedID { get; set; } = "";

		[Sortable(IndexName = "Audits")]
		[FormControl(Hidden = true, Label = "{{portals.desktops.controls.[name].label}}", PlaceHolder = "{{portals.desktops.controls.[name].placeholder}}", Description = "{{portals.desktops.controls.[name].description}}")]
		public DateTime LastModified { get; set; } = DateTime.Now;

		[Sortable(IndexName = "Audits")]
		[FormControl(Hidden = true, Label = "{{portals.desktops.controls.[name].label}}", PlaceHolder = "{{portals.desktops.controls.[name].placeholder}}", Description = "{{portals.desktops.controls.[name].description}}")]
		public string LastModifiedID { get; set; } = "";

		[Property(MaxLength = 32), Sortable(IndexName = "Management")]
		[FormControl(Hidden = true)]
		public string ParentID { get; set; }

		[Sortable(IndexName = "Management")]
		[FormControl(Hidden = true)]
		public int OrderIndex { get; set; } = 0;

		[Property(MaxLength = 32, NotNull = true, NotEmpty = true), Sortable(IndexName = "Management", UniqueIndexName = "Alias")]
		[FormControl(Hidden = true)]
		public override string SystemID { get; set; }

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public override string RepositoryID { get; set; }

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public override string EntityID { get; set; }

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public string OrganizationID => this.SystemID;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public Organization Organization => Utility.GetOrganizationByID(this.OrganizationID);

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public Desktop ParentDesktop => Utility.GetDesktopByID(this.ParentID);

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public new IPortalObject Parent => this.ParentDesktop ?? this.Organization as IPortalObject;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		INestedObject INestedObject.Parent => this.ParentDesktop;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public string FullTitle
		{
			get
			{
				var parent = this.ParentDesktop;
				return (parent == null ? "" : $"{parent.FullTitle} > ") + this.Title;
			}
		}

		internal List<string> _childrenIDs;

		internal List<Desktop> GetChildren(bool notifyPropertyChanged = true)
		{
			if (this._childrenIDs == null)
			{
				var desktops = Utility.GetDesktopsByParentID(this.SystemID, this.ID);
				this._childrenIDs = desktops.Select(desktop => desktop.ID).ToList();
				if (notifyPropertyChanged)
					this.NotifyPropertyChanged("ChildrenIDs");
				return desktops;
			}
			return this._childrenIDs.Select(id => Utility.GetDesktopByID(id)).ToList();
		}

		internal async Task<List<Desktop>> GetChildrenAsync(CancellationToken cancellationToken = default, bool notifyPropertyChanged = true)
		{
			if (this._childrenIDs == null)
			{
				var desktops = await Utility.GetDesktopsByParentIDAsync(this.SystemID, this.ID, cancellationToken).ConfigureAwait(false);
				this._childrenIDs = desktops.Select(desktop => desktop.ID).ToList();
				if (notifyPropertyChanged)
					this.NotifyPropertyChanged("ChildrenIDs");
				return desktops;
			}
			return this._childrenIDs.Select(id => Utility.GetDesktopByID(id)).ToList();
		}

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public List<Desktop> Children => this.GetChildren();

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		List<INestedObject> INestedObject.Children => this.Children.Select(desktop => desktop as INestedObject).ToList();

		public override JObject ToJson(bool addTypeOfExtendedProperties = false, Action<JObject> onPreCompleted = null)
			=> this.ToJson(false, addTypeOfExtendedProperties, onPreCompleted);

		public JObject ToJson(bool addChildren, bool addTypeOfExtendedProperties, Action<JObject> onPreCompleted = null)
			=> base.ToJson(addTypeOfExtendedProperties, json =>
			{
				if (addChildren && json != null)
					json["Children"] = (this.GetChildren() ?? new List<Desktop>()).ToJArray(desktop => desktop?.ToJson(true, false));
				onPreCompleted?.Invoke(json);
			});

		public override void ProcessPropertyChanged(string name)
		{
			if (name.IsEquals("ChildrenIDs"))
				Utility.Cache.Set(this);
		}
	}
}
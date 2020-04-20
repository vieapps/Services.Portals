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
using net.vieapps.Components.Security;
using net.vieapps.Components.Repository;
using net.vieapps.Components.Utility;
using Newtonsoft.Json.Linq;
#endregion

namespace net.vieapps.Services.Portals
{
	[Serializable, BsonIgnoreExtraElements, DebuggerDisplay("ID = {ID}, Title = {Title}")]
	[Entity(CollectionName = "Roles", TableName = "T_Portals_Roles", CacheClass = typeof(Utility), CacheName = "Cache", Searchable = true)]
	public sealed class Role : Repository<Role>, INestedObject
	{
		public Role() : base() { }

		[Property(MaxLength = 250, NotNull = true, NotEmpty = true), Sortable(IndexName = "Title"), Searchable]
		[FormControl(Label = "{{portals.roles.controls.[name].label}}", PlaceHolder = "{{portals.roles.controls.[name].placeholder}}", Description = "{{portals.roles.controls.[name].description}}")]
		public override string Title { get; set; } = "";

		[FormControl(Label = "{{portals.roles.controls.[name].label}}", PlaceHolder = "{{portals.roles.controls.[name].placeholder}}", Description = "{{portals.roles.controls.[name].description}}")]
		public string Description { get; set; }

		[Property(MaxLength = 32), Sortable(IndexName = "Management")]
		[FormControl(Label = "{{portals.roles.controls.[name].label}}", PlaceHolder = "{{portals.roles.controls.[name].placeholder}}", Description = "{{portals.roles.controls.[name].description}}")]
		public string ParentID { get; set; }

		[AsMapping, Sortable(IndexName = "Members")]
		[FormControl(Label = "{{portals.roles.controls.[name].label}}", PlaceHolder = "{{portals.roles.controls.[name].placeholder}}", Description = "{{portals.roles.controls.[name].description}}")]
		public List<string> UserIDs { get; set; } = new List<string>();

		[Sortable(IndexName = "Audits")]
		[FormControl(Hidden = true, Label = "{{portals.roles.controls.[name].label}}", PlaceHolder = "{{portals.roles.controls.[name].placeholder}}", Description = "{{portals.roles.controls.[name].description}}")]
		public DateTime Created { get; set; } = DateTime.Now;

		[Sortable(IndexName = "Audits")]
		[FormControl(Hidden = true, Label = "{{portals.roles.controls.[name].label}}", PlaceHolder = "{{portals.roles.controls.[name].placeholder}}", Description = "{{portals.roles.controls.[name].description}}")]
		public string CreatedID { get; set; } = "";

		[Sortable(IndexName = "Audits")]
		[FormControl(Hidden = true, Label = "{{portals.roles.controls.[name].label}}", PlaceHolder = "{{portals.roles.controls.[name].placeholder}}", Description = "{{portals.roles.controls.[name].description}}")]
		public DateTime LastModified { get; set; } = DateTime.Now;

		[Sortable(IndexName = "Audits")]
		[FormControl(Hidden = true, Label = "{{portals.roles.controls.[name].label}}", PlaceHolder = "{{portals.roles.controls.[name].placeholder}}", Description = "{{portals.roles.controls.[name].description}}")]
		public string LastModifiedID { get; set; } = "";

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public int OrderIndex { get; set; } = 0;

		[Property(MaxLength = 32, NotNull = true, NotEmpty = true), Sortable(IndexName = "Management")]
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
		public Role ParentRole => Utility.GetRoleByID(this.ParentID);

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public new IPortalObject Parent => this.ParentRole ?? this.Organization as IPortalObject;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		INestedObject INestedObject.Parent => this.ParentRole;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public string FullTitle
		{
			get
			{
				var parent = this.ParentRole;
				return (parent == null ? "" : $"{parent.FullTitle} > ") + this.Title;
			}
		}

		internal List<string> _childrenIDs;

		internal List<Role> GetChildren(bool notifyPropertyChanged = true)
		{
			if (this._childrenIDs == null)
			{
				var roles = Utility.GetRolesByParentID(this.SystemID, this.ID);
				this._childrenIDs = roles.Select(role => role.ID).ToList();
				if (notifyPropertyChanged)
					this.NotifyPropertyChanged("ChildrenIDs");
				return roles;
			}
			return this._childrenIDs.Select(id => Utility.GetRoleByID(id)).ToList();
		}

		internal async Task<List<Role>> GetChildrenAsync(CancellationToken cancellationToken = default, bool notifyPropertyChanged = true)
		{
			if (this._childrenIDs == null)
			{
				var roles = await Utility.GetRolesByParentIDAsync(this.SystemID, this.ID, cancellationToken).ConfigureAwait(false);
				this._childrenIDs = roles.Select(role => role.ID).ToList();
				if (notifyPropertyChanged)
					this.NotifyPropertyChanged("ChildrenIDs");
				return roles;
			}
			return this._childrenIDs.Select(id => Utility.GetRoleByID(id)).ToList();
		}

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public List<Role> Children => this.GetChildren();

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		List<INestedObject> INestedObject.Children => this.Children.Select(role => role as INestedObject).ToList();

		public override JObject ToJson(bool addTypeOfExtendedProperties = false, Action<JObject> onPreCompleted = null)
			=> this.ToJson(false, addTypeOfExtendedProperties, onPreCompleted);

		public JObject ToJson(bool addChildren, bool addTypeOfExtendedProperties, Action<JObject> onPreCompleted = null)
			=> base.ToJson(addTypeOfExtendedProperties, json =>
			{
				if (addChildren && json != null)
					json["Children"] = (this.GetChildren() ?? new List<Role>()).ToJArray(role => role?.ToJson(true, false));
				onPreCompleted?.Invoke(json);
			});

		public override void ProcessPropertyChanged(string name)
		{
			if (name.IsEquals("ChildrenIDs"))
				Utility.Cache.Set(this);
		}
	}
}
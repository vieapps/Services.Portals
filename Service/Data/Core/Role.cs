#region Related components
using System;
using System.Linq;
using System.Diagnostics;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Serialization;
using MsgPack.Serialization;
using Newtonsoft.Json;
using MongoDB.Bson.Serialization.Attributes;
using net.vieapps.Components.Security;
using net.vieapps.Components.Repository;
using net.vieapps.Components.Utility;
using Newtonsoft.Json.Linq;
#endregion

namespace net.vieapps.Services.Portals
{
	[BsonIgnoreExtraElements, DebuggerDisplay("ID = {ID}, Title = {Title}")]
	[Entity(CollectionName = "Roles", TableName = "T_Portals_Roles", CacheClass = typeof(Utility), CacheName = "Cache", Searchable = true)]
	public sealed class Role : Repository<Role>, INestedObject
	{
		public Role() : base() { }

		[Property(MaxLength = 32)]
		[Sortable(IndexName = "Management")]
		[FormControl(ControlType = "Lookup", Label = "{{portals.roles.controls.[name].label}}", PlaceHolder = "{{portals.roles.controls.[name].placeholder}}", Description = "{{portals.roles.controls.[name].description}}")]
		public string ParentID { get; set; }

		[Property(MaxLength = 250, NotNull = true, NotEmpty = true)]
		[Sortable(IndexName = "Title")]
		[Searchable]
		[FormControl(Label = "{{portals.roles.controls.[name].label}}", PlaceHolder = "{{portals.roles.controls.[name].placeholder}}", Description = "{{portals.roles.controls.[name].description}}")]
		public override string Title { get; set; }

		[Searchable]
		[FormControl(Label = "{{portals.roles.controls.[name].label}}", PlaceHolder = "{{portals.roles.controls.[name].placeholder}}", Description = "{{portals.roles.controls.[name].description}}")]
		public string Description { get; set; }

		[ChildrenMappings(TableName = "T_Portals_Roles_Users", LinkColumn = "RoleID", MapColumn = "UserID")]
		[FormControl(Label = "{{portals.roles.controls.[name].label}}", PlaceHolder = "{{portals.roles.controls.[name].placeholder}}", Description = "{{portals.roles.controls.[name].description}}")]
		public List<string> UserIDs { get; set; } = new List<string>();

		[Sortable(IndexName = "Audits")]
		[FormControl(Hidden = true)]
		public DateTime Created { get; set; }

		[Sortable(IndexName = "Audits")]
		[FormControl(Hidden = true)]
		public string CreatedID { get; set; }

		[Sortable(IndexName = "Audits")]
		[FormControl(Hidden = true)]
		public DateTime LastModified { get; set; }

		[Sortable(IndexName = "Audits")]
		[FormControl(Hidden = true)]
		public string LastModifiedID { get; set; }

		[Property(MaxLength = 32, NotNull = true, NotEmpty = true)]
		[Sortable(IndexName = "Management")]
		[FormControl(Hidden = true)]
		public override string SystemID { get; set; }

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public override string RepositoryID { get; set; }

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public override string RepositoryEntityID { get; set; }

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public override Privileges OriginalPrivileges { get; set; }

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public override Privileges WorkingPrivileges => this.Organization?.WorkingPrivileges;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public int OrderIndex { get; set; }

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore, MessagePackIgnore]
		public string OrganizationID => this.SystemID;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore, MessagePackIgnore]
		public Organization Organization => (this.OrganizationID ?? "").GetOrganizationByID();

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore, MessagePackIgnore]
		public Role ParentRole => (this.ParentID ?? "").GetRoleByID();

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore, MessagePackIgnore]
		public override RepositoryBase Parent => this.ParentRole ?? this.Organization as RepositoryBase;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore, MessagePackIgnore]
		IPortalObject IPortalObject.Parent => this.ParentRole ?? this.Organization as IPortalObject;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore, MessagePackIgnore]
		INestedObject INestedObject.Parent => this.ParentRole;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore, MessagePackIgnore]
		public string FullTitle
		{
			get
			{
				var parent = this.ParentRole;
				return (parent == null ? "" : $"{parent.FullTitle} > ") + this.Title;
			}
		}

		internal List<string> _childrenIDs;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public List<string> ChildrenIDs
		{
			get => this._childrenIDs;
			set => this._childrenIDs = value;
		}

		internal List<Role> FindChildren(bool notifyPropertyChanged = true, List < Role> roles = null)
		{
			if (this._childrenIDs == null)
			{
				roles = roles ?? (this.SystemID ?? "").FindRoles(this.ID);
				this._childrenIDs = roles.Select(role => role.ID).ToList();
				if (notifyPropertyChanged)
					this.NotifyPropertyChanged("Childrens");
				return roles;
			}
			return this._childrenIDs.Select(id => id.GetRoleByID()).ToList();
		}

		internal async Task<List<Role>> FindChildrenAsync(CancellationToken cancellationToken = default, bool notifyPropertyChanged = true)
			=> this._childrenIDs == null
				? this.FindChildren(notifyPropertyChanged, await(this.SystemID ?? "").FindRolesAsync(this.ID, cancellationToken).ConfigureAwait(false))
				: this._childrenIDs.Select(id => id.GetRoleByID()).ToList();

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore, MessagePackIgnore]
		public List<Role> Children => this.FindChildren();

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore, MessagePackIgnore]
		List<INestedObject> INestedObject.Children => this.Children?.Select(role => role as INestedObject).ToList();

		public override JObject ToJson(bool addTypeOfExtendedProperties = false, Action<JObject> onCompleted = null)
			=> this.ToJson(false, addTypeOfExtendedProperties, onCompleted);

		public JObject ToJson(bool addChildren, bool addTypeOfExtendedProperties, Action<JObject> onCompleted = null, Action<JObject> onChildrenCompleted = null)
			=> base.ToJson(addTypeOfExtendedProperties, json =>
			{
				json.Remove("Privileges");
				json.Remove("OriginalPrivileges");
				if (addChildren)
					json["Children"] = this.Children?.Where(role => role != null).Select(role => role?.ToJson(addChildren, addTypeOfExtendedProperties, onChildrenCompleted)).ToJArray();
				onCompleted?.Invoke(json);
			});

		public override void ProcessPropertyChanged(string name)
		{
			if (name.IsEquals("Childrens") && !string.IsNullOrWhiteSpace(this.ID) && !string.IsNullOrWhiteSpace(this.Title))
			{
				new CommunicateMessage(ServiceBase.ServiceComponent.ServiceName)
				{
					Type = $"{this.GetObjectName()}#Update",
					Data = this.ToJson(false, false),
					ExcludedNodeID = Utility.NodeID
				}.Send();
				this.Set(true);
			}
		}
	}
}
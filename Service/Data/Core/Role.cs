#region Related components
using System;
using System.Linq;
using System.Diagnostics;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Serialization;
using System.Dynamic;
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

		[Property(MaxLength = 32)]
		[Sortable(IndexName = "Management")]
		[FormControl(ControlType = "Lookup", Label = "{{portals.roles.controls.[name].label}}", PlaceHolder = "{{portals.roles.controls.[name].placeholder}}", Description = "{{portals.roles.controls.[name].description}}")]
		public string ParentID { get; set; }

		[Property(MaxLength = 250, NotNull = true, NotEmpty = true)]
		[Sortable(IndexName = "Title"), Searchable]
		[FormControl(Label = "{{portals.roles.controls.[name].label}}", PlaceHolder = "{{portals.roles.controls.[name].placeholder}}", Description = "{{portals.roles.controls.[name].description}}")]
		public override string Title { get; set; }

		[Searchable]
		[FormControl(Label = "{{portals.roles.controls.[name].label}}", PlaceHolder = "{{portals.roles.controls.[name].placeholder}}", Description = "{{portals.roles.controls.[name].description}}")]
		public string Description { get; set; }

		[AsMapping]
		[Sortable(IndexName = "Members")]
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
		public override string EntityID { get; set; }

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public override Privileges OriginalPrivileges { get; set; }

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public override Privileges WorkingPrivileges => this.Organization?.WorkingPrivileges;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public int OrderIndex { get; set; }

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public string OrganizationID => this.SystemID;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public Organization Organization => (this.OrganizationID ?? "").GetOrganizationByID();

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public Role ParentRole => (this.ParentID ?? "").GetRoleByID();

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public override RepositoryBase Parent => this.ParentRole ?? this.Organization as RepositoryBase;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		IPortalObject IPortalObject.Parent => this.ParentRole ?? this.Organization as IPortalObject;

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

		internal List<Role> GetChildren(List<Role> roles = null)
		{
			if (this._childrenIDs == null)
			{
				roles = roles ?? this.SystemID.GetRoles(this.ID);
				this._childrenIDs = roles.Select(role => role.ID).ToList();
				this.NotifyPropertyChanged("ChildrenIDs");
				return roles;
			}
			return this._childrenIDs.Select(id => id.GetRoleByID()).ToList();
		}

		internal async Task<List<Role>> GetChildrenAsync(CancellationToken cancellationToken = default)
			=> this._childrenIDs == null
				? this.GetChildren(await this.SystemID.GetRolesAsync(this.ID, cancellationToken).ConfigureAwait(false))
				: this._childrenIDs.Select(id => id.GetRoleByID()).ToList();

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public List<Role> Children => this.GetChildren();

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		List<INestedObject> INestedObject.Children => this.Children?.Select(role => role as INestedObject).ToList();

		public override JObject ToJson(bool addTypeOfExtendedProperties = false, Action<JObject> onPreCompleted = null)
			=> this.ToJson(false, addTypeOfExtendedProperties, onPreCompleted);

		public JObject ToJson(bool addChildren, bool addTypeOfExtendedProperties, Action<JObject> onPreCompleted = null)
			=> base.ToJson(addTypeOfExtendedProperties, json =>
			{
				json.Remove("Privileges");
				json.Remove("OriginalPrivileges");
				if (addChildren)
					json["Children"] = this.Children?.Select(role => role?.ToJson(true, false)).Where(role => role != null).ToJArray();
				onPreCompleted?.Invoke(json);
			});

		public override void ProcessPropertyChanged(string name)
		{
			if (name.IsEquals("ChildrenIDs"))
				Utility.Cache.Set(this);
		}
	}

	internal static class RoleExtensions
	{
		internal static ConcurrentDictionary<string, Role> Roles { get; } = new ConcurrentDictionary<string, Role>(StringComparer.OrdinalIgnoreCase);

		internal static Role CreateRoleInstance(this ExpandoObject requestBody, string excluded = null, Action<Role> onCompleted = null)
			=> requestBody.Copy(excluded?.ToHashSet(), onCompleted);

		internal static Role UpdateRoleInstance(this Role role, ExpandoObject requestBody, string excluded = null, Action<Role> onCompleted = null)
		{
			role.CopyFrom(requestBody, excluded?.ToHashSet(), onCompleted);
			return role;
		}

		internal static Role Set(this Role role, bool updateCache = false)
		{
			if (role != null)
			{
				RoleExtensions.Roles[role.ID] = role;
				if (updateCache)
					Utility.Cache.Set(role);
			}
			return role;
		}

		internal static async Task<Role> SetAsync(this Role role, bool updateCache = false, CancellationToken cancellationToken = default)
		{
			role?.Set();
			await (updateCache && role != null ? Utility.Cache.SetAsync(role, cancellationToken) : Task.CompletedTask).ConfigureAwait(false);
			return role;
		}

		internal static Role Remove(this Role role)
			=> (role?.ID ?? "").RemoveRole();

		internal static Role RemoveRole(this string id)
			=> !string.IsNullOrWhiteSpace(id) && RoleExtensions.Roles.TryRemove(id, out var role) ? role : null;

		internal static Role GetRoleByID(this string id, bool force = false, bool fetchRepository = true)
			=> !force && !string.IsNullOrWhiteSpace(id) && RoleExtensions.Roles.ContainsKey(id)
				? RoleExtensions.Roles[id]
				: fetchRepository && !string.IsNullOrWhiteSpace(id)
					? Role.Get<Role>(id)?.Set()
					: null;

		internal static async Task<Role> GetRoleByIDAsync(this string id, CancellationToken cancellationToken = default, bool force = false)
			=> (id ?? "").GetRoleByID(force, false) ?? (await Role.GetAsync<Role>(id, cancellationToken).ConfigureAwait(false))?.Set();

		internal static IFilterBy<Role> GetRolesFilter(this string systemID, string parentID)
			=> Filters<Role>.And(Filters<Role>.Equals("SystemID", systemID), string.IsNullOrWhiteSpace(parentID) ? Filters<Role>.IsNull("ParentID") : Filters<Role>.Equals("ParentID", parentID));

		internal static List<Role> GetRoles(this string systemID, string parentID, bool updateCache = true)
		{
			if (string.IsNullOrWhiteSpace(systemID))
				return new List<Role>();
			var filter = systemID.GetRolesFilter(parentID);
			var sort = Sorts<Role>.Ascending("Title");
			var roles = Role.Find(filter, sort, 0, 1, Extensions.GetCacheKey(filter, sort, 0, 1));
			roles.ForEach(role => role.Set(updateCache));
			return roles;
		}

		internal static async Task<List<Role>> GetRolesAsync(this string systemID, string parentID, CancellationToken cancellationToken = default, bool updateCache = true)
		{
			if (string.IsNullOrWhiteSpace(systemID))
				return new List<Role>();
			var filter = systemID.GetRolesFilter(parentID);
			var sort = Sorts<Role>.Ascending("Title");
			var roles = await Role.FindAsync(filter, sort, 0, 1, Extensions.GetCacheKey(filter, sort, 0, 1), cancellationToken).ConfigureAwait(false);
			await roles.ForEachAsync((role, token) => role.SetAsync(updateCache, token), cancellationToken).ConfigureAwait(false);
			return roles;
		}
	}
}
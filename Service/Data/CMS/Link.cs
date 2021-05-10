#region Related components
using System;
using System.Linq;
using System.Diagnostics;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Serialization;
using MsgPack.Serialization;
using MongoDB.Bson.Serialization.Attributes;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Converters;
using net.vieapps.Components.Utility;
using net.vieapps.Components.Security;
using net.vieapps.Components.Repository;
#endregion

namespace net.vieapps.Services.Portals
{
	[BsonIgnoreExtraElements, DebuggerDisplay("ID = {ID}, Title = {Title}")]
	[Entity(CollectionName = "CMS_Links", TableName = "T_Portals_CMS_Links", CacheClass = typeof(Utility), CacheName = "Cache", Searchable = true, ID = "B0000000000000000000000000000004", Title = "Link", Description = "Linking content in the CMS module (menu/banners/links)", ObjectNamePrefix = "CMS.", MultipleIntances = true, Extendable = true)]
	public sealed class Link : Repository<Link>, IBusinessObject, INestedObject
	{
		public Link() : base() { }

		[Property(MaxLength = 32)]
		[Sortable(IndexName = "Management")]
		[FormControl(Segment = "basic", ControlType = "Lookup", Multiple = false, Label = "{{portals.cms.links.controls.[name].label}}", PlaceHolder = "{{portals.cms.links.controls.[name].placeholder}}", Description = "{{portals.cms.links.controls.[name].description}}")]
		public string ParentID { get; set; }

		[Property(MaxLength = 250, NotNull = true, NotEmpty = true)]
		[Sortable(IndexName = "Title")]
		[Searchable]
		[FormControl(Segment = "basic", Label = "{{portals.cms.links.controls.[name].label}}", PlaceHolder = "{{portals.cms.links.controls.[name].placeholder}}", Description = "{{portals.cms.links.controls.[name].description}}")]
		public override string Title { get; set; }

		[Searchable]
		[FormControl(Segment = "basic", ControlType = "TextArea", Label = "{{portals.cms.links.controls.[name].label}}", PlaceHolder = "{{portals.cms.links.controls.[name].placeholder}}", Description = "{{portals.cms.links.controls.[name].description}}")]
		public string Summary { get; set; }

		[Property(MaxLength = 1000, NotNull = true, NotEmpty = true)]
		[FormControl(Segment = "basic", Label = "{{portals.cms.links.controls.[name].label}}", PlaceHolder = "{{portals.cms.links.controls.[name].placeholder}}", Description = "{{portals.cms.links.controls.[name].description}}")]
		public string URL { get; set; }

		[Property(MaxLength = 50)]
		[FormControl(Segment = "basic", Label = "{{portals.cms.links.controls.[name].label}}", PlaceHolder = "{{portals.cms.links.controls.[name].placeholder}}", Description = "{{portals.cms.links.controls.[name].description}}")]
		public string Target { get; set; }

		[JsonConverter(typeof(StringEnumConverter)), BsonRepresentation(MongoDB.Bson.BsonType.String)]
		[FormControl(Segment = "basic", Label = "{{portals.cms.links.controls.[name].label}}", PlaceHolder = "{{portals.cms.links.controls.[name].placeholder}}", Description = "{{portals.cms.links.controls.[name].description}}")]
		public ChildrenMode ChildrenMode { get; set; } = ChildrenMode.Normal;

		[Property(MaxLength = 32)]
		[FormControl(Segment = "basic", ControlType = "Select", Label = "{{portals.cms.links.controls.[name].label}}", PlaceHolder = "{{portals.cms.links.controls.[name].placeholder}}", Description = "{{portals.cms.links.controls.[name].description}}")]
		public string LookupRepositoryID { get; set; }

		[Property(MaxLength = 32)]
		[FormControl(Segment = "basic", ControlType = "Select", Label = "{{portals.cms.links.controls.[name].label}}", PlaceHolder = "{{portals.cms.links.controls.[name].placeholder}}", Description = "{{portals.cms.links.controls.[name].description}}")]
		public string LookupRepositoryEntityID { get; set; }

		[Property(MaxLength = 32)]
		[FormControl(Segment = "basic", ControlType = "Lookup", Label = "{{portals.cms.links.controls.[name].label}}", PlaceHolder = "{{portals.cms.links.controls.[name].placeholder}}", Description = "{{portals.cms.links.controls.[name].description}}")]
		public string LookupRepositoryObjectID { get; set; }

		[JsonConverter(typeof(StringEnumConverter)), BsonRepresentation(MongoDB.Bson.BsonType.String)]
		[Sortable(IndexName = "Management")]
		[FormControl(Segment = "basic", Label = "{{portals.cms.links.controls.[name].label}}", PlaceHolder = "{{portals.cms.links.controls.[name].placeholder}}", Description = "{{portals.cms.links.controls.[name].description}}")]
		public ApprovalStatus Status { get; set; } = ApprovalStatus.Published;

		[Sortable(IndexName = "Management")]
		[FormControl(Segment = "basic", ReadOnly = true, Label = "{{portals.cms.links.controls.[name].label}}", PlaceHolder = "{{portals.cms.links.controls.[name].placeholder}}", Description = "{{portals.cms.links.controls.[name].description}}")]
		public int OrderIndex { get; set; } = 0;

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

		[Property(MaxLength = 32, NotNull = true, NotEmpty = true)]
		[Sortable(IndexName = "Management")]
		[FormControl(Hidden = true)]
		public override string RepositoryID { get; set; }

		[Property(MaxLength = 32, NotNull = true, NotEmpty = true)]
		[Sortable(IndexName = "Management")]
		[FormControl(Hidden = true)]
		public override string RepositoryEntityID { get; set; }

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public string OrganizationID => this.SystemID;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore, MessagePackIgnore]
		public Organization Organization => (this.OrganizationID ?? "").GetOrganizationByID();

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore, MessagePackIgnore]
		IPortalObject IBusinessObject.Organization => this.Organization;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public string ModuleID => this.RepositoryID;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore, MessagePackIgnore]
		public Module Module => (this.ModuleID ?? "").GetModuleByID();

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore, MessagePackIgnore]
		IPortalModule IBusinessObject.Module => this.Module;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public string ContentTypeID => this.RepositoryEntityID;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore, MessagePackIgnore]
		public ContentType ContentType => (this.ContentTypeID ?? "").GetContentTypeByID();

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore, MessagePackIgnore]
		IPortalContentType IBusinessObject.ContentType => this.ContentType;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore, MessagePackIgnore]
		public Link ParentLink => string.IsNullOrWhiteSpace(this.ParentID) ? null : Link.Get<Link>(this.ParentID);

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore, MessagePackIgnore]
		public override RepositoryBase Parent => this.ParentLink ?? this.ContentType as RepositoryBase;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore, MessagePackIgnore]
		IBusinessEntity IBusinessEntity.Parent => this.ParentLink;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore, MessagePackIgnore]
		IPortalObject IPortalObject.Parent => this.ParentLink ?? this.ContentType as IPortalObject;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore, MessagePackIgnore]
		INestedObject INestedObject.Parent => this.ParentLink;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public string FullTitle
		{
			get
			{
				var parent = this.ParentLink;
				return (parent == null ? "" : $"{parent.FullTitle} > ") + this.Title;
			}
		}

		internal List<Link> _children;

		internal List<string> _childrenIDs;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public List<string> ChildrenIDs
		{
			get => this._childrenIDs;
			set => this._childrenIDs = value;
		}

		internal List<Link> FindChildren(bool notifyPropertyChanged = true, List<Link> links = null)
		{
			if (this.ChildrenMode.Equals(ChildrenMode.Normal))
			{
				if (this._childrenIDs == null)
				{
					this._children = links ?? (this.SystemID ?? "").FindLinks(this.RepositoryID, this.RepositoryEntityID, this.ID);
					this._childrenIDs = this._children?.Where(link => link != null).Select(link => link.ID).ToList() ?? new List<string>();
					Utility.Cache.AddSetMembers(this.ContentType.ObjectCacheKeys, this._children?.Where(link => link != null).Select(link => link.GetCacheKey()));
					if (notifyPropertyChanged)
						this.NotifyPropertyChanged("Childrens");
				}
				return this._children ?? (this._children = this._childrenIDs?.Select(id => Link.Get<Link>(id)).Where(link => link != null).ToList() ?? new List<Link>());
			}
			else
				return this._children ?? new List<Link>();
		}

		internal async Task<List<Link>> FindChildrenAsync(CancellationToken cancellationToken = default, bool notifyPropertyChanged = true)
			=> this.ChildrenMode.Equals(ChildrenMode.Normal)
				? this._childrenIDs == null
					? this.FindChildren(notifyPropertyChanged, await (this.SystemID ?? "").FindLinksAsync(this.RepositoryID, this.RepositoryEntityID, this.ID, cancellationToken).ConfigureAwait(false))
					: this._children ?? (this._children = this._childrenIDs?.Select(id => Link.Get<Link>(id)).Where(link => link != null).ToList() ?? new List<Link>())
				: this._children ?? new List<Link>();

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore, MessagePackIgnore]
		public List<Link> Children => this.FindChildren();

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore, MessagePackIgnore]
		List<INestedObject> INestedObject.Children => this.Children?.Select(link => link as INestedObject).ToList();

		public override void ProcessPropertyChanged(string name)
		{
			if (name.IsEquals("Childrens") && !string.IsNullOrWhiteSpace(this.ID) && !string.IsNullOrWhiteSpace(this.Title))
				Utility.Cache.Set(this);
		}

		public override JObject ToJson(bool addTypeOfExtendedProperties = false, Action<JObject> onCompleted = null)
			=> this.ToJson(false, addTypeOfExtendedProperties, onCompleted);

		public JObject ToJson(bool addChildren, bool addTypeOfExtendedProperties, Action<JObject> onCompleted = null, Action<JObject> onChildrenCompleted = null, int level = 1, int maxLevel = 0)
			=> base.ToJson(addTypeOfExtendedProperties, json =>
			{
				if (addChildren && (maxLevel < 1 || level < maxLevel))
					json["Children"] = this.Children?.Where(link => link != null).OrderBy(link => link.OrderIndex).Select(link => link.ToJson(addChildren, addTypeOfExtendedProperties, onChildrenCompleted, onChildrenCompleted, level + 1, maxLevel)).ToJArray();
				onCompleted?.Invoke(json);
			});

		public string GetURL(string desktop = null, bool addPageNumberHolder = false, string parentIdentity = null)
		{
			var url = this.URL ?? "#";
			if (url.StartsWith("~/") && url.IsEndsWith("/default.aspx") && this.Organization != null && this.Organization.AlwaysUseHtmlSuffix)
				url = url.Replace(StringComparison.OrdinalIgnoreCase, "/default.aspx", ".html");
			return url.Equals("~.html") ? "~/index.html" : url;
		}
	}

	public enum ChildrenMode
	{
		Normal,
		Lookup
	}
}
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
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Converters;
using MongoDB.Bson.Serialization.Attributes;
using net.vieapps.Components.Utility;
using net.vieapps.Components.Security;
using net.vieapps.Components.Repository;
#endregion

namespace net.vieapps.Services.Portals
{
	[Serializable, BsonIgnoreExtraElements]
	[DebuggerDisplay("ID = {ID}, Title = {Title}")]
	[Entity(CollectionName = "CMS_Links", TableName = "T_Portals_CMS_Links", CacheClass = typeof(Utility), CacheName = "Cache", Searchable = true, ID = "B0000000000000000000000000000004", Title = "Link", Description = "Linking content in the CMS module (menu/banners/links)", ObjectNamePrefix = "CMS.", MultipleIntances = true, Extendable = true)]
	public sealed class Link : Repository<Link>, IBusinessObject, INestedObject
	{
		public Link() : base() { }

		[JsonConverter(typeof(StringEnumConverter)), BsonRepresentation(MongoDB.Bson.BsonType.String)]
		[Sortable(IndexName = "Management")]
		[FormControl(Segment = "basic", Label = "{{portals.cms.links.controls.[name].label}}", PlaceHolder = "{{portals.cms.links.controls.[name].placeholder}}", Description = "{{portals.cms.links.controls.[name].description}}")]
		public ApprovalStatus Status { get; set; } = ApprovalStatus.Published;

		[Property(MaxLength = 32)]
		[Sortable(IndexName = "Management")]
		[FormControl(Segment = "basic", ControlType = "Lookup", Multiple = false, Label = "{{portals.cms.links.controls.[name].label}}", PlaceHolder = "{{portals.cms.links.controls.[name].placeholder}}", Description = "{{portals.cms.links.controls.[name].description}}")]
		public string ParentID { get; set; }

		[Sortable(IndexName = "Management")]
		[FormControl(Hidden = true)]
		public int OrderIndex { get; set; } = 0;

		[Property(MaxLength = 250, NotNull = true, NotEmpty = true)]
		[Sortable(IndexName = "Title")]
		[Searchable]
		[FormControl(Segment = "basic", Label = "{{portals.cms.links.controls.[name].label}}", PlaceHolder = "{{portals.cms.links.controls.[name].placeholder}}", Description = "{{portals.cms.links.controls.[name].description}}")]
		public override string Title { get; set; }

		[Searchable]
		[FormControl(Segment = "basic", ControlType = "TextArea", Label = "{{portals.cms.links.controls.[name].label}}", PlaceHolder = "{{portals.cms.links.controls.[name].placeholder}}", Description = "{{portals.cms.links.controls.[name].description}}")]
		public string Summary { get; set; }

		[FormControl(Segment = "basic", Label = "{{portals.cms.links.controls.[name].label}}", PlaceHolder = "{{portals.cms.links.controls.[name].placeholder}}", Description = "{{portals.cms.links.controls.[name].description}}")]
		public string URL { get; set; }

		[Property(MaxLength = 50)]
		[FormControl(Segment = "basic", Label = "{{portals.cms.links.controls.[name].label}}", PlaceHolder = "{{portals.cms.links.controls.[name].placeholder}}", Description = "{{portals.cms.links.controls.[name].description}}")]
		public string Target { get; set; }

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

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public Organization Organization => (this.OrganizationID ?? "").GetOrganizationByID();

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		IPortalObject IBusinessObject.Organization => this.Organization;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public string ModuleID => this.RepositoryID;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public Module Module => (this.ModuleID ?? "").GetModuleByID();

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		IPortalModule IBusinessObject.Module => this.Module;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public string ContentTypeID => this.RepositoryEntityID;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public ContentType ContentType => (this.ContentTypeID ?? "").GetContentTypeByID();

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		IPortalContentType IBusinessObject.ContentType => this.ContentType;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public Link ParentLink => string.IsNullOrWhiteSpace(this.ParentID) ? null : Link.Get<Link>(this.ParentID);

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public override RepositoryBase Parent => this.ParentLink ?? this.ContentType as RepositoryBase;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		IBusinessEntity IBusinessEntity.Parent => this.ParentLink;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		IPortalObject IPortalObject.Parent => this.ParentLink ?? this.ContentType as IPortalObject;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
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

		[NonSerialized]
		internal List<Link> _children;

		internal List<string> _childrenIDs;

		internal List<Link> FindChildren(bool notifyPropertyChanged = true, List<Link> links = null)
		{
			if (this._childrenIDs == null)
			{
				if (links == null)
				{
					var filter = Filters<Link>.And(Filters<Link>.Equals("ParentID", this.ID));
					var sort = Sorts<Link>.Ascending("OrderIndex").ThenByAscending("Title");
					this._children = links = Link.Find(filter, sort, 0, 1, Extensions.GetCacheKey(filter, sort, 0, 1));
				}
				else
					this._children = links;

				this._childrenIDs = links.Select(link => link.ID).ToList();
				if (notifyPropertyChanged)
					this.NotifyPropertyChanged("ChildrenIDs");
			}

			this._children = this._children ?? this._childrenIDs.Select(id => Link.Get<Link>(id)).ToList();
			return this._children;
		}

		internal async Task<List<Link>> FindChildrenAsync(CancellationToken cancellationToken = default, bool notifyPropertyChanged = true)
		{
			if (this._childrenIDs == null)
			{
				var filter = Filters<Link>.And(Filters<Link>.Equals("ParentID", this.ID));
				var sort = Sorts<Link>.Ascending("OrderIndex").ThenByAscending("Title");
				return this.FindChildren(notifyPropertyChanged, await Link.FindAsync(filter, sort, 0, 1, Extensions.GetCacheKey(filter, sort, 0, 1), cancellationToken).ConfigureAwait(false));
			}
			return this.FindChildren();
		}

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public List<Link> Children => this.FindChildren();

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		List<INestedObject> INestedObject.Children => this.Children?.Select(link => link as INestedObject).ToList();

		public override JObject ToJson(bool addTypeOfExtendedProperties = false, Action<JObject> onPreCompleted = null)
			=> this.ToJson(false, addTypeOfExtendedProperties, onPreCompleted);

		public JObject ToJson(bool addChildren, bool addTypeOfExtendedProperties, Action<JObject> onPreCompleted = null)
			=> base.ToJson(addTypeOfExtendedProperties, json =>
			{
				if (addChildren)
					json["Children"] = this.Children?.Select(link => link?.ToJson(true, false)).Where(link => link != null).ToJArray();
				onPreCompleted?.Invoke(json);
			});
	}
}
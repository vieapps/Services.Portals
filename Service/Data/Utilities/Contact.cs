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
	[BsonIgnoreExtraElements, DebuggerDisplay("ID = {ID}, Name = {Name}, Title = {Title}")]
	[Entity(CollectionName = "CMS_Contacts", TableName = "T_Portals_CMS_Contacts", CacheClass = typeof(Utility), CacheName = "Cache", Searchable = true, ID = "B0000000000000000000000000000005", Title = "Contact", Description = "Contact information", ObjectNamePrefix = "Utils.", MultipleIntances = true, Extendable = true)]
	public sealed class Contact : Repository<Contact>, IBusinessObject
	{
		public Contact() : base() { }

		[Property(MaxLength = 250, NotNull = true, NotEmpty = true)]
		[Sortable(IndexName = "Name")]
		[Searchable]
		[FormControl(Label = "{{portals.utilities.contacts.controls.[name].label}}", PlaceHolder = "{{portals.utilities.contacts.controls.[name].placeholder}}", Description = "{{portals.utilities.contacts.controls.[name].description}}")]
		public string Name { get; set; }

		[Property(MaxLength = 250)]
		[Sortable(IndexName = "Email")]
		[Searchable]
		[FormControl(Label = "{{portals.utilities.contacts.controls.[name].label}}", PlaceHolder = "{{portals.utilities.contacts.controls.[name].placeholder}}", Description = "{{portals.utilities.contacts.controls.[name].description}}")]
		public string Email { get; set; }

		[Property(MaxLength = 250)]
		[Sortable(IndexName = "Phone")]
		[Searchable]
		[FormControl(Label = "{{portals.utilities.contacts.controls.[name].label}}", PlaceHolder = "{{portals.utilities.contacts.controls.[name].placeholder}}", Description = "{{portals.utilities.contacts.controls.[name].description}}")]
		public string Phone { get; set; }

		[Property(MaxLength = 250)]
		[Sortable(IndexName = "Address")]
		[Searchable]
		[FormControl(Label = "{{portals.utilities.contacts.controls.[name].label}}", PlaceHolder = "{{portals.utilities.contacts.controls.[name].placeholder}}", Description = "{{portals.utilities.contacts.controls.[name].description}}")]
		public string Address { get; set; }

		[FormControl(Label = "{{portals.utilities.contacts.controls.[name].label}}", PlaceHolder = "{{portals.utilities.contacts.controls.[name].placeholder}}", Description = "{{portals.utilities.contacts.controls.[name].description}}")]
		public string Notes { get; set; }

		[Property(MaxLength = 32)]
		[Sortable(IndexName = "Audits")]
		[FormControl(Hidden = true)]
		public string UserID { get; set; }

		[Property(MaxLength = 250, NotNull = true, NotEmpty = true)]
		[Sortable(IndexName = "Title")]
		[Searchable]
		[FormControl(Label = "{{portals.utilities.contacts.controls.[name].label}}", PlaceHolder = "{{portals.utilities.contacts.controls.[name].placeholder}}", Description = "{{portals.utilities.contacts.controls.[name].description}}")]
		public override string Title { get; set; }

		[Searchable]
		[Property(IsCLOB = true)]
		[FormControl(Label = "{{portals.utilities.contacts.controls.[name].label}}", PlaceHolder = "{{portals.utilities.contacts.controls.[name].placeholder}}", Description = "{{portals.utilities.contacts.controls.[name].description}}")]
		public string Details { get; set; }

		[JsonConverter(typeof(StringEnumConverter)), BsonRepresentation(MongoDB.Bson.BsonType.String)]
		[Sortable(IndexName = "Management")]
		[FormControl(Label = "{{portals.utilities.contacts.controls.[name].label}}", PlaceHolder = "{{portals.utilities.contacts.controls.[name].placeholder}}", Description = "{{portals.utilities.contacts.controls.[name].description}}")]
		public ApprovalStatus Status { get; set; } = ApprovalStatus.Published;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public bool AllowComments { get; } = false;

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
		public override RepositoryBase Parent => this.ContentType;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		IBusinessEntity IBusinessEntity.Parent => null;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		IPortalObject IPortalObject.Parent => this.ContentType;

		public string GetURL(string desktop = null, bool addPageNumberHolder = false, string parentIdentity = null)
			=> $"~/{this.ContentType?.Desktop?.Alias ?? desktop ?? "-default"}/{parentIdentity ?? this.ContentType?.Title?.GetANSIUri() ?? "-"}/{this.ID}{(addPageNumberHolder ? "/{{pageNumber}}" : "")}{(this.Organization != null && this.Organization.AlwaysUseHtmlSuffix ? ".html" : "")}";
	}
}
﻿#region Related components
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
	[BsonIgnoreExtraElements, DebuggerDisplay("ID = {ID}, Title = {Title}")]
	[Entity(CollectionName = "CMS_Forms", TableName = "T_Portals_CMS_Forms", CacheClass = typeof(Utility), CacheName = "Cache", Searchable = true, ID = "B0000000000000000000000000000005", Title = "Form", Description = "Form of a requesting information", ObjectNamePrefix = "CMS.", MultipleIntances = true, Extendable = true)]
	public sealed class Form : Repository<Form>, IBusinessObject
	{
		public Form() : base() { }

		[Searchable]
		[Property(MaxLength = 250, NotNull = true, NotEmpty = true)]
		[Sortable(IndexName = "Name")]
		[FormControl(Label = "{{portals.cms.forms.controls.[name].label}}", PlaceHolder = "{{portals.cms.forms.controls.[name].placeholder}}", Description = "{{portals.cms.forms.controls.[name].description}}")]
		public string Name { get; set; }

		[Searchable]
		[Property(MaxLength = 250, NotNull = true, NotEmpty = true)]
		[Sortable(IndexName = "Phone")]
		[FormControl(DataType = "tel", Label = "{{portals.cms.forms.controls.[name].label}}", PlaceHolder = "{{portals.cms.forms.controls.[name].placeholder}}", Description = "{{portals.cms.forms.controls.[name].description}}")]
		public string Phone { get; set; }

		[Searchable]
		[Property(MaxLength = 250)]
		[Sortable(IndexName = "Email")]
		[FormControl(DataType = "email", Label = "{{portals.cms.forms.controls.[name].label}}", PlaceHolder = "{{portals.cms.forms.controls.[name].placeholder}}", Description = "{{portals.cms.forms.controls.[name].description}}")]
		public string Email { get; set; }

		[Searchable]
		[Property(MaxLength = 250)]
		[Sortable(IndexName = "Address")]
		[FormControl(Label = "{{portals.cms.forms.controls.[name].label}}", PlaceHolder = "{{portals.cms.forms.controls.[name].placeholder}}", Description = "{{portals.cms.forms.controls.[name].description}}")]
		public string Address { get; set; }

		[Searchable]
		[Property(MaxLength = 50)]
		[Sortable(IndexName = "Address")]
		[FormControl(Label = "{{portals.cms.forms.controls.[name].label}}", PlaceHolder = "{{portals.cms.forms.controls.[name].placeholder}}", Description = "{{portals.cms.forms.controls.[name].description}}")]
		public string County { get; set; }

		[Searchable]
		[Property(MaxLength = 50)]
		[Sortable(IndexName = "Address")]
		[FormControl(Label = "{{portals.cms.forms.controls.[name].label}}", PlaceHolder = "{{portals.cms.forms.controls.[name].placeholder}}", Description = "{{portals.cms.forms.controls.[name].description}}")]
		public string Province { get; set; }

		[Property(MaxLength = 50)]
		[Sortable(IndexName = "Address")]
		[FormControl(Label = "{{portals.cms.forms.controls.[name].label}}", PlaceHolder = "{{portals.cms.forms.controls.[name].placeholder}}", Description = "{{portals.cms.forms.controls.[name].description}}")]
		public string Postal { get; set; }

		[Property(MaxLength = 2)]
		[Sortable(IndexName = "Address")]
		[FormControl(Label = "{{portals.cms.forms.controls.[name].label}}", PlaceHolder = "{{portals.cms.forms.controls.[name].placeholder}}", Description = "{{portals.cms.forms.controls.[name].description}}")]
		public string Country { get; set; }

		[FormControl(ControlType = "TextArea", MaxLength = 4000, Label = "{{portals.cms.forms.controls.[name].label}}", PlaceHolder = "{{portals.cms.forms.controls.[name].placeholder}}", Description = "{{portals.cms.forms.controls.[name].description}}")]
		public string Notes { get; set; }

		[Searchable]
		[Sortable(IndexName = "Title")]
		[Property(MaxLength = 250, NotNull = true, NotEmpty = true)]
		[FormControl(Label = "{{portals.cms.forms.controls.[name].label}}", PlaceHolder = "{{portals.cms.forms.controls.[name].placeholder}}", Description = "{{portals.cms.forms.controls.[name].description}}")]
		public override string Title { get; set; }

		[Searchable]
		[Property(IsCLOB = true)]
		[FormControl(ControlType = "TextArea", Label = "{{portals.cms.forms.controls.[name].label}}", PlaceHolder = "{{portals.cms.forms.controls.[name].placeholder}}", Description = "{{portals.cms.forms.controls.[name].description}}")]
		public string Details { get; set; }

		[Property(MaxLength = 32)]
		[Sortable(IndexName = "Management")]
		[FormControl(Hidden = true, Label = "{{portals.cms.forms.controls.[name].label}}", PlaceHolder = "{{portals.cms.forms.controls.[name].placeholder}}", Description = "{{portals.cms.forms.controls.[name].description}}")]
		public string IPAddress { get; set; }

		[AsJson]
		[FormControl(Hidden = true, ControlType = "Select", Label = "{{portals.cms.forms.controls.[name].label}}", PlaceHolder = "{{portals.cms.forms.controls.[name].placeholder}}", Description = "{{portals.cms.forms.controls.[name].description}}")]
		public Dictionary<string, string> Profiles { get; set; }

		[JsonConverter(typeof(StringEnumConverter)), BsonRepresentation(MongoDB.Bson.BsonType.String)]
		[Sortable(IndexName = "Management")]
		[FormControl(Hidden = true, Label = "{{portals.cms.forms.controls.[name].label}}", PlaceHolder = "{{portals.cms.forms.controls.[name].placeholder}}", Description = "{{portals.cms.forms.controls.[name].description}}")]
		public ApprovalStatus Status { get; set; } = ApprovalStatus.Pending;

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

		public override JObject ToJson(bool addTypeOfExtendedProperties, Action<JObject> onCompleted = null)
			=> base.ToJson(addTypeOfExtendedProperties, json =>
			{
				if (!string.IsNullOrWhiteSpace(this.ContentType?.SubTitleFormula) && json.Get<string>("SubTitle") == null)
					json["SubTitle"] = this.ContentType.SubTitleFormula.Evaluate(json.ToExpandoObject())?.ToString();
				onCompleted?.Invoke(json);
			});

		public string GetURL(string desktop = null, bool addPageNumberHolder = false, string parentIdentity = null)
			=> null;
	}
}
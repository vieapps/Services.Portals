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
using Newtonsoft.Json.Linq;
using MongoDB.Bson.Serialization.Attributes;
using net.vieapps.Components.Security;
using net.vieapps.Components.Repository;
using net.vieapps.Components.Utility;
using net.vieapps.Services.Portals.Exceptions;
#endregion

namespace net.vieapps.Services.Portals
{
	[Serializable, BsonIgnoreExtraElements]
	[DebuggerDisplay("ID = {ID}, Title = {Title}")]
	[Entity(CollectionName = "Portlets", TableName = "T_Portals_Portlets", CacheClass = typeof(Utility), CacheName = "Cache")]
	public sealed class Portlet : Repository<Portlet>
	{
		public Portlet() : base() { }

		[Property(MaxLength = 250, NotNull = true, NotEmpty = true)]
		[FormControl(Segment = "basic", Label = "{{portals.portlets.controls.[name].label}}", PlaceHolder = "{{portals.portlets.controls.[name].placeholder}}", Description = "{{portals.portlets.controls.[name].description}}")]
		public override string Title { get; set; }

		[Property(MaxLength = 10)]
		[FormControl(Segment = "basic", ControlType = "Select", Label = "{{portals.portlets.controls.[name].label}}", PlaceHolder = "{{portals.portlets.controls.[name].placeholder}}", Description = "{{portals.portlets.controls.[name].description}}")]
		public string MainAction { get; set; }

		[Property(MaxLength = 10)]
		[FormControl(Segment = "basic", ControlType = "Select", Label = "{{portals.portlets.controls.[name].label}}", PlaceHolder = "{{portals.portlets.controls.[name].placeholder}}", Description = "{{portals.portlets.controls.[name].description}}")]
		public string SubAction { get; set; }

		[Property(MaxLength = 32)]
		[Sortable(IndexName = "Management")]
		[FormControl(Hidden = true)]
		public string DesktopID { get; set; }

		[Property(MaxLength = 100)]
		[Sortable(IndexName = "Management")]
		[FormControl(Hidden = true)]
		public string Zone { get; set; }

		[Sortable(IndexName = "Management")]
		[FormControl(Hidden = true)]
		public int OrderIndex { get; set; } = 0;

		[Property(MaxLength = 32)]
		[Sortable(IndexName = "Management")]
		[FormControl(Hidden = true)]
		public string ContentTypeID { get; set; }

		[Property(MaxLength = 32)]
		[Sortable(IndexName = "Management")]
		[FormControl(Hidden = true)]
		public string OriginalPortletID { get; set; }

		[AsJson]
		[FormControl(Segment = "basic", Label = "{{portals.portlets.controls.[name].label}}", PlaceHolder = "{{portals.portlets.controls.[name].placeholder}}", Description = "{{portals.portlets.controls.[name].description}}")]
		public Portlets.CommonSettings CommonSettings { get; set; }

		[AsJson]
		[FormControl(Segment = "list", Label = "{{portals.portlets.controls.[name].label}}", PlaceHolder = "{{portals.portlets.controls.[name].placeholder}}", Description = "{{portals.portlets.controls.[name].description}}")]
		public Portlets.ListSettings ListSettings { get; set; }

		[AsJson]
		[FormControl(Segment = "view", Label = "{{portals.portlets.controls.[name].label}}", PlaceHolder = "{{portals.portlets.controls.[name].placeholder}}", Description = "{{portals.portlets.controls.[name].description}}")]
		public Portlets.ViewSettings ViewSettings { get; set; }

		[AsJson]
		[FormControl(Segment = "other", Label = "{{portals.portlets.controls.[name].label}}", PlaceHolder = "{{portals.portlets.controls.[name].placeholder}}", Description = "{{portals.portlets.controls.[name].description}}")]
		public Portlets.BreadcrumbSettings BreadcrumbSettings { get; set; }

		[AsJson]
		[FormControl(Segment = "other", Label = "{{portals.portlets.controls.[name].label}}", PlaceHolder = "{{portals.portlets.controls.[name].placeholder}}", Description = "{{portals.portlets.controls.[name].description}}")]
		public Portlets.PaginationSettings PaginationSettings { get; set; }

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
		public override Privileges WorkingPrivileges => this.Desktop?.WorkingPrivileges;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public string OrganizationID => this.SystemID;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public Organization Organization => (this.OrganizationID ?? "").GetOrganizationByID();

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public ContentType ContentType => (this.ContentTypeID ?? "").GetContentTypeByID();

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public Desktop Desktop => (this.DesktopID ?? "").GetDesktopByID();

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public Portlet OriginalPortlet => Portlet.Get<Portlet>(this.OriginalPortletID);

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public Desktop OriginalDesktop => this.OriginalPortlet?.Desktop;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public override RepositoryBase Parent => this.Desktop;

		internal void Normalize()
		{
			if (this.ContentType == null)
				this.MainAction = this.SubAction = null;
			else
			{
				this.MainAction = (this.MainAction ?? "List").Trim();
				this.SubAction = string.IsNullOrWhiteSpace(this.SubAction) || this.MainAction.IsEquals(this.SubAction) ? null : this.SubAction.Trim();
			}
			this.CommonSettings?.Normalize(settings => (settings.Template ?? "").ValidateTemplate(false));
			this.ListSettings?.Normalize(settings =>
			{
				try
				{
					(settings.Template ?? "").GetXDocument();
				}
				catch (Exception ex)
				{
					throw new TemplateIsInvalidException($"List XSLT is invalid => {ex.Message}", ex);
				}
			});
			this.ViewSettings?.Normalize(settings =>
			{
				try
				{
					(settings.Template ?? "").GetXDocument();
				}
				catch (Exception ex)
				{
					throw new TemplateIsInvalidException($"View XSLT is invalid => {ex.Message}", ex);
				}
			});
			this.BreadcrumbSettings?.Normalize(settings =>
			{
				try
				{
					(settings.Template ?? "").GetXDocument();
				}
				catch (Exception ex)
				{
					throw new TemplateIsInvalidException($"Breadcrumb XSLT is invalid => {ex.Message}", ex);
				}
			});
			this.PaginationSettings?.Normalize(settings =>
			{
				try
				{
					(settings.Template ?? "").GetXDocument();
				}
				catch (Exception ex)
				{
					throw new TemplateIsInvalidException($"Pagination XSLT is invalid => {ex.Message}", ex);
				}
			});
		}
	}
}
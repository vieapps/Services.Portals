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
		[FormControl(Segment = "common", Label = "{{portals.portlets.controls.[name].label}}", PlaceHolder = "{{portals.portlets.controls.[name].placeholder}}", Description = "{{portals.portlets.controls.[name].description}}")]
		public override string Title { get; set; }

		[Property(MaxLength = 10)]
		[FormControl(Segment = "common", ControlType = "Select", SelectValues = "List,View", Label = "{{portals.portlets.controls.[name].label}}", PlaceHolder = "{{portals.portlets.controls.[name].placeholder}}", Description = "{{portals.portlets.controls.[name].description}}")]
		public string Action { get; set; }

		[Property(MaxLength = 10)]
		[FormControl(Segment = "common", ControlType = "Select", SelectValues = "List,View", Label = "{{portals.portlets.controls.[name].label}}", PlaceHolder = "{{portals.portlets.controls.[name].placeholder}}", Description = "{{portals.portlets.controls.[name].description}}")]
		public string AlternativeAction { get; set; }

		[Property(MaxLength = 32)]
		[Sortable(IndexName = "Management")]
		[FormControl(Segment = "common", Label = "{{portals.portlets.controls.[name].label}}", PlaceHolder = "{{portals.portlets.controls.[name].placeholder}}", Description = "{{portals.portlets.controls.[name].description}}")]
		public string DesktopID { get; set; }

		[Property(MaxLength = 100)]
		[Sortable(IndexName = "Management")]
		[FormControl(Segment = "common", ControlType = "Select", Label = "{{portals.portlets.controls.[name].label}}", PlaceHolder = "{{portals.portlets.controls.[name].placeholder}}", Description = "{{portals.portlets.controls.[name].description}}")]
		public string Zone { get; set; }

		[Sortable(IndexName = "Management")]
		[FormControl(Hidden = true)]
		public int OrderIndex { get; set; } = 0;

		[Property(MaxLength = 32)]
		[Sortable(IndexName = "Management")]
		[FormControl(Segment = "common", ControlType = "Select", Label = "{{portals.portlets.controls.[name].label}}", PlaceHolder = "{{portals.portlets.controls.[name].placeholder}}", Description = "{{portals.portlets.controls.[name].description}}")]
		public override string RepositoryEntityID { get; set; }

		[Property(MaxLength = 32)]
		[Sortable(IndexName = "Management")]
		[FormControl(Segment = "common", Label = "{{portals.portlets.controls.[name].label}}", PlaceHolder = "{{portals.portlets.controls.[name].placeholder}}", Description = "{{portals.portlets.controls.[name].description}}")]
		public string OriginalPortletID { get; set; }

		[AsJson]
		[FormControl(Segment = "common", Label = "{{portals.portlets.controls.[name].label}}", PlaceHolder = "{{portals.portlets.controls.[name].placeholder}}", Description = "{{portals.portlets.controls.[name].description}}")]
		public Portlets.CommonSettings CommonSettings { get; set; }

		[AsJson]
		[FormControl(Segment = "list", Label = "{{portals.portlets.controls.[name].label}}", PlaceHolder = "{{portals.portlets.controls.[name].placeholder}}", Description = "{{portals.portlets.controls.[name].description}}")]
		public Portlets.ListSettings ListSettings { get; set; }

		[AsJson]
		[FormControl(Segment = "view", Label = "{{portals.portlets.controls.[name].label}}", PlaceHolder = "{{portals.portlets.controls.[name].placeholder}}", Description = "{{portals.portlets.controls.[name].description}}")]
		public Portlets.ViewSettings ViewSettings { get; set; }

		[AsJson]
		[FormControl(Segment = "other", Label = "{{portals.portlets.controls.[name].label}}", PlaceHolder = "{{portals.portlets.controls.[name].placeholder}}", Description = "{{portals.portlets.controls.[name].description}}")]
		public Portlets.PaginationSettings PaginationSettings { get; set; }

		[AsJson]
		[FormControl(Segment = "other", Label = "{{portals.portlets.controls.[name].label}}", PlaceHolder = "{{portals.portlets.controls.[name].placeholder}}", Description = "{{portals.portlets.controls.[name].description}}")]
		public Portlets.BreadcrumbSettings BreadcrumbSettings { get; set; }

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
		public override Privileges OriginalPrivileges { get; set; }

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public override Privileges WorkingPrivileges => this.Desktop?.WorkingPrivileges;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public override RepositoryBase Parent => this.Desktop;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public string OrganizationID => this.SystemID;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public Organization Organization => (this.OrganizationID ?? "").GetOrganizationByID();

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public string ModuleID => this.ContentType?.ModuleID;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public Module Module => this.ContentType?.Module;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public string ContentTypeID => this.RepositoryEntityID;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public ContentType ContentType => (this.ContentTypeID ?? "").GetContentTypeByID();

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public ContentTypeDefinition ContentTypeDefinition => this.ContentType?.ContentTypeDefinition;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public Desktop Desktop => (this.DesktopID ?? "").GetDesktopByID();

		internal Portlet _originalPortlet;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public Portlet OriginalPortlet => string.IsNullOrWhiteSpace(this.OriginalPortletID) ? this : this._originalPortlet ?? (this._originalPortlet = Portlet.Get<Portlet>(this.OriginalPortletID));

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public Desktop OriginalDesktop => this.OriginalPortlet?.Desktop;

		internal void Normalize()
		{
			if (this.ContentType != null && string.IsNullOrWhiteSpace(this.OriginalPortletID))
			{
				this.Action = (this.Action ?? "List").Trim();
				this.AlternativeAction = string.IsNullOrWhiteSpace(this.AlternativeAction) || this.Action.IsEquals(this.AlternativeAction) ? null : this.AlternativeAction.Trim();
			}
			else
				this.Action = this.AlternativeAction = null;

			this.CommonSettings?.Normalize(settings => (settings.Template ?? "").ValidateTemplate(false));

			this.ListSettings?.Normalize(settings =>
			{
				try
				{
					(settings.Template ?? "").GetXslCompiledTransform();
				}
				catch (Exception ex)
				{
					if (ex is TemplateIsInvalidException)
						ex = ex.InnerException;
					throw new TemplateIsInvalidException($"List XSLT is invalid => {ex.Message}", ex);
				}
				if (!string.IsNullOrWhiteSpace(settings.Options))
					try
					{
						JObject.Parse(settings.Options);
					}
					catch (Exception ex)
					{
						throw new OptionsAreInvalidException($"List options are invalid => {ex.Message}", ex);
					}
			});

			this.ViewSettings?.Normalize(settings =>
			{
				try
				{
					(settings.Template ?? "").GetXslCompiledTransform();
				}
				catch (Exception ex)
				{
					if (ex is TemplateIsInvalidException)
						ex = ex.InnerException;
					throw new TemplateIsInvalidException($"View XSLT is invalid => {ex.Message}", ex);
				}
				if (!string.IsNullOrWhiteSpace(settings.Options))
					try
					{
						JObject.Parse(settings.Options);
					}
					catch (Exception ex)
					{
						throw new OptionsAreInvalidException($"View options are invalid => {ex.Message}", ex);
					}
			});

			this.PaginationSettings?.Normalize(settings =>
			{
				try
				{
					(settings.Template ?? "").GetXslCompiledTransform();
				}
				catch (Exception ex)
				{
					if (ex is TemplateIsInvalidException)
						ex = ex.InnerException;
					throw new TemplateIsInvalidException($"Pagination XSLT is invalid => {ex.Message}", ex);
				}
			});

			this.BreadcrumbSettings?.Normalize(settings =>
			{
				try
				{
					(settings.Template ?? "").GetXslCompiledTransform();
				}
				catch (Exception ex)
				{
					if (ex is TemplateIsInvalidException)
						ex = ex.InnerException;
					throw new TemplateIsInvalidException($"Breadcrumb XSLT is invalid => {ex.Message}", ex);
				}
			});
		}

		public override JObject ToJson(bool addTypeOfExtendedProperties, Action<JObject> onCompleted = null)
			=> base.ToJson(addTypeOfExtendedProperties, json =>
			{
				json.Remove("Privileges");
				json.Remove("OriginalPrivileges");
				onCompleted?.Invoke(json);
			});

		public async Task<string> GetTemplateAsync(CancellationToken cancellationToken = default)
			=> string.IsNullOrWhiteSpace(this.CommonSettings?.Template)
				? await Utility.GetTemplateAsync("portlet.xml", this.Desktop?.WorkingTheme, null, null, cancellationToken).ConfigureAwait(false)
				: this.CommonSettings.Template;
	}
}
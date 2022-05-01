#region Related components
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Serialization;
using System.Collections.Generic;
using MsgPack.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using MongoDB.Bson.Serialization.Attributes;
using net.vieapps.Components.Security;
using net.vieapps.Components.Repository;
using net.vieapps.Components.Utility;
using net.vieapps.Services.Portals.Exceptions;
#endregion

namespace net.vieapps.Services.Portals
{
	[BsonIgnoreExtraElements, DebuggerDisplay("ID = {ID}, Title = {Title}")]
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
		[Sortable(IndexName = "Desktop")]
		[FormControl(Segment = "common", Label = "{{portals.portlets.controls.[name].label}}", PlaceHolder = "{{portals.portlets.controls.[name].placeholder}}", Description = "{{portals.portlets.controls.[name].description}}")]
		public string DesktopID { get; set; }

		[Property(MaxLength = 100)]
		[Sortable(IndexName = "Desktop")]
		[FormControl(Segment = "common", ControlType = "Select", Label = "{{portals.portlets.controls.[name].label}}", PlaceHolder = "{{portals.portlets.controls.[name].placeholder}}", Description = "{{portals.portlets.controls.[name].description}}")]
		public string Zone { get; set; }

		[Sortable(IndexName = "Desktop")]
		[FormControl(Hidden = true)]
		public int OrderIndex { get; set; } = 0;

		[Property(MaxLength = 32)]
		[Sortable(IndexName = "Management")]
		[FormControl(Segment = "common", ControlType = "Select", Label = "{{portals.portlets.controls.[name].label}}", PlaceHolder = "{{portals.portlets.controls.[name].placeholder}}", Description = "{{portals.portlets.controls.[name].description}}")]
		public override string RepositoryEntityID { get; set; }

		[Property(MaxLength = 32)]
		[Sortable(IndexName = "Desktop")]
		[FormControl(Segment = "common", Label = "{{portals.portlets.controls.[name].label}}", PlaceHolder = "{{portals.portlets.controls.[name].placeholder}}", Description = "{{portals.portlets.controls.[name].description}}")]
		public string OriginalPortletID { get; set; }

		[AsJson]
		[FormControl(Segment = "common", Label = "{{portals.portlets.controls.[name].label}}", PlaceHolder = "{{portals.portlets.controls.[name].placeholder}}", Description = "{{portals.portlets.controls.[name].description}}")]
		public Portlets.CommonSettings CommonSettings { get; set; }

		[Property(MaxLength = 32)]
		[Sortable(IndexName = "Management")]
		[FormControl(Segment = "list", ControlType = "Lookup", Label = "{{portals.portlets.controls.[name].label}}", PlaceHolder = "{{portals.portlets.controls.[name].placeholder}}", Description = "{{portals.portlets.controls.[name].description}}")]
		public string ExpressionID { get; set; }

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

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore, MessagePackIgnore]
		public override Privileges WorkingPrivileges => this.Desktop?.WorkingPrivileges;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore, MessagePackIgnore]
		public override RepositoryBase Parent => this.Desktop;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public string OrganizationID => this.SystemID;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore, MessagePackIgnore]
		public Organization Organization => (this.OrganizationID ?? "").GetOrganizationByID();

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public string ModuleID => this.ContentType?.ModuleID;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore, MessagePackIgnore]
		public Module Module => this.ContentType?.Module;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public string ContentTypeID => this.RepositoryEntityID;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore, MessagePackIgnore]
		public ContentType ContentType => (this.ContentTypeID ?? "").GetContentTypeByID();

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore, MessagePackIgnore]
		public ContentTypeDefinition ContentTypeDefinition => this.ContentType?.ContentTypeDefinition;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore, MessagePackIgnore]
		public Desktop Desktop => (this.DesktopID ?? "").GetDesktopByID();

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore, MessagePackIgnore]
		public Portlet OriginalPortlet => this.GetOriginalPortlet();

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore, MessagePackIgnore]
		public Desktop OriginalDesktop => this.OriginalPortlet?.Desktop;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore, MessagePackIgnore]
		public List<Portlet> MappingPortlets => this.GetMappingPortlets();

		internal Portlet _originalPortlet;

		internal List<Portlet> _mappingPortlets;

		internal Portlet GetOriginalPortlet()
			=> string.IsNullOrWhiteSpace(this.OriginalPortletID)
				? this
				: this._originalPortlet ?? (this._originalPortlet = Portlet.Get<Portlet>(this.OriginalPortletID));

		internal async Task<Portlet> GetOriginalPortletAsync(CancellationToken cancellationToken = default)
			=> string.IsNullOrWhiteSpace(this.OriginalPortletID)
				? this
				: this._originalPortlet ?? (this._originalPortlet = await Portlet.GetAsync<Portlet>(this.OriginalPortletID, cancellationToken).ConfigureAwait(false));

		internal List<Portlet> GetMappingPortlets()
			=> this._mappingPortlets ?? (this._mappingPortlets = Portlet.Find<Portlet>(Filters<Portlet>.Equals("OriginalPortletID", this.OriginalPortlet.ID), Sorts<Portlet>.Ascending("DesktopID").ThenByAscending("Zone").ThenByAscending("OrderIndex"), 0, 1, null, false));

		internal async Task<List<Portlet>> GetMappingPortletsAsync(CancellationToken cancellationToken = default)
			=> this._mappingPortlets ?? (this._mappingPortlets = await Portlet.FindAsync<Portlet>(Filters<Portlet>.Equals("OriginalPortletID", this.OriginalPortlet.ID), Sorts<Portlet>.Ascending("DesktopID").ThenByAscending("Zone").ThenByAscending("OrderIndex"), 0, 1, null, false, null, 0, cancellationToken).ConfigureAwait(false));

		internal void Normalize()
		{
			if (this.ContentType != null && string.IsNullOrWhiteSpace(this.OriginalPortletID))
			{
				this.Action = (this.Action ?? "List").Trim();
				this.AlternativeAction = string.IsNullOrWhiteSpace(this.AlternativeAction) || this.Action.IsEquals(this.AlternativeAction) ? null : this.AlternativeAction.Trim();
			}
			else
				this.Action = this.AlternativeAction = null;

			this.CommonSettings?.Normalize(settings =>
			{
				(settings.Template ?? "").ValidateTemplate();
				settings.IconURI = settings.IconURI?.Replace(StringComparison.OrdinalIgnoreCase, $"{this.Organization.FakeFilesHttpURI ?? Utility.FilesHttpURI}/", "~~/");
				if (settings.TitleUISettings != null)
					settings.TitleUISettings.BackgroundImageURI = settings.TitleUISettings.BackgroundImageURI?.Replace(StringComparison.OrdinalIgnoreCase, $"{this.Organization.FakeFilesHttpURI ?? Utility.FilesHttpURI}/", "~~/");
				if (settings.ContentUISettings != null)
					settings.ContentUISettings.BackgroundImageURI = settings.ContentUISettings.BackgroundImageURI?.Replace(StringComparison.OrdinalIgnoreCase, $"{this.Organization.FakeFilesHttpURI ?? Utility.FilesHttpURI}/", "~~/");
			});

			this.ExpressionID = string.IsNullOrWhiteSpace(this.ExpressionID) ? null : this.ExpressionID;
			this.ListSettings?.Normalize(settings =>
			{
				if (!string.IsNullOrWhiteSpace(settings.Template))
					try
					{
						settings.Template.GetXDocument().ToString().GetXslCompiledTransform();
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
				if (!string.IsNullOrWhiteSpace(settings.Template))
					try
					{
						settings.Template.GetXDocument().ToString().GetXslCompiledTransform();
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
				if (!string.IsNullOrWhiteSpace(settings.Template))
					try
					{
						var template = @"
						<?xml version=""1.0"" encoding=""utf-8"" ?>
						<xsl:stylesheet version=""1.0"" xmlns:xsl=""http://www.w3.org/1999/XSL/Transform"" xmlns:msxsl=""urn:schemas-microsoft-com:xslt"" xmlns:func=""urn:schemas-vieapps-net:xslt"">
						<xsl:template match=""/"">" + settings.Template + @"
						</xsl:template>
						</xsl:stylesheet>";
						template.Trim().Replace("\t", "").GetXDocument().ToString().GetXslCompiledTransform();
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
				if (!string.IsNullOrWhiteSpace(settings.Template))
					try
					{
						var template = @"
						<?xml version=""1.0"" encoding=""utf-8"" ?>
						<xsl:stylesheet version=""1.0"" xmlns:xsl=""http://www.w3.org/1999/XSL/Transform"" xmlns:msxsl=""urn:schemas-microsoft-com:xslt"" xmlns:func=""urn:schemas-vieapps-net:xslt"">
						<xsl:template match=""/"">" + settings.Template + @"
						</xsl:template>
						</xsl:stylesheet>";
						template.Trim().Replace("\t", "").GetXDocument().ToString().GetXslCompiledTransform();
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
			=> !string.IsNullOrWhiteSpace(this.CommonSettings?.Template)
				? this.CommonSettings.Template
				: await Utility.GetTemplateAsync("portlet.xml", this.Desktop?.WorkingTheme, null, null, cancellationToken).ConfigureAwait(false) ?? await Utility.GetTemplateAsync("portlet.xml", null, null, null, cancellationToken).ConfigureAwait(false);
	}
}
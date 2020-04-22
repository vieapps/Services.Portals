﻿#region Related components
using System;
using System.Linq;
using System.Diagnostics;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using MongoDB.Bson.Serialization.Attributes;
using net.vieapps.Components.Utility;
using net.vieapps.Components.Security;
using net.vieapps.Components.Repository;
#endregion

namespace net.vieapps.Services.Portals
{
	[Serializable, BsonIgnoreExtraElements, DebuggerDisplay("ID = {ID}, Title = {Title}")]
	[Entity(CollectionName = "ContentTypes", TableName = "T_Portals_ContentTypes", CacheClass = typeof(Utility), CacheName = "Cache", Searchable = true)]
	public sealed class ContentType : Repository<ContentType>, IPortalContentType
	{
		public ContentType() : base()
			=> this.ID = "";

		[Property(MaxLength = 250, NotNull = true, NotEmpty = true), Sortable(IndexName = "Title"), Searchable, FormControl(Label = "{{portals.contenttypes.controls.[name]}}", PlaceHolder = "{{portals.contenttypes.controls.[name].placeholder}}", Description = "{{portals.contenttypes.controls.[name].description}}")]
		public override string Title { get; set; } = "";

		[FormControl(Label = "{{portals.contenttypes.controls.[name]}}", PlaceHolder = "{{portals.contenttypes.controls.[name].placeholder}}", Description = "{{portals.contenttypes.controls.[name].description}}")]
		public string Description { get; set; }

		[Property(MaxLength = 32), FormControl(Label = "{{portals.contenttypes.controls.[name]}}", PlaceHolder = "{{portals.contenttypes.controls.[name].placeholder}}", Description = "{{portals.contenttypes.controls.[name].description}}")]
		public string DesktopID { get; set; }

		[Sortable(IndexName = "Management"), FormControl(Label = "{{portals.contenttypes.controls.[name]}}", PlaceHolder = "{{portals.contenttypes.controls.[name].placeholder}}", Description = "{{portals.contenttypes.controls.[name].description}}")]
		public bool CreateNewVersionWhenUpdated { get; set; } = true;

		[Sortable(IndexName = "Management"), FormControl(Label = "{{portals.contenttypes.controls.[name]}}", PlaceHolder = "{{portals.contenttypes.controls.[name].placeholder}}", Description = "{{portals.contenttypes.controls.[name].description}}")]
		public bool AllowComments { get; set; } = false;

		[Sortable(IndexName = "Management"), FormControl(Label = "{{portals.contenttypes.controls.[name]}}", PlaceHolder = "{{portals.contenttypes.controls.[name].placeholder}}", Description = "{{portals.contenttypes.controls.[name].description}}")]
		public bool UseSocialNetworkComments { get; set; } = false;

		[JsonConverter(typeof(StringEnumConverter)), BsonRepresentation(MongoDB.Bson.BsonType.String), FormControl(Label = "{{portals.contenttypes.controls.[name]}}", PlaceHolder = "{{portals.contenttypes.controls.[name].placeholder}}", Description = "{{portals.contenttypes.controls.[name].description}}")]
		public ApprovalStatus DefaultCommentStatus { get; set; } = ApprovalStatus.Pending;

		[Property(IsCLOB = true), FormControl(Excluded = true), XmlIgnore]
		public string OtherSettings { get; set; }

		[Sortable(IndexName = "Audits"), FormControl(Hidden = true)]
		public DateTime Created { get; set; } = DateTime.Now;

		[Sortable(IndexName = "Audits"), FormControl(Hidden = true)]
		public string CreatedID { get; set; } = "";

		[Sortable(IndexName = "Audits"), FormControl(Hidden = true)]
		public DateTime LastModified { get; set; } = DateTime.Now;

		[Sortable(IndexName = "Audits"), FormControl(Hidden = true)]
		public string LastModifiedID { get; set; } = "";

		[Property(MaxLength = 32, NotNull = true, NotEmpty = true), Sortable(IndexName = "Management"), FormControl(Hidden = true)]
		public override string SystemID { get; set; }

		[Property(MaxLength = 32, NotNull = true, NotEmpty = true), Sortable(IndexName = "Management"), FormControl(Hidden = true)]
		public override string RepositoryID { get; set; }

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public override string EntityID { get; set; }

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public new IPortalObject Parent => this.Module;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public string OrganizationID => this.SystemID;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public string ModuleID => this.RepositoryID;

		[AsJson, FormControl(Excluded = true), JsonIgnore, XmlIgnore]
		public List<ExtendedPropertyDefinition> ExtendedPropertyDefinitions { get; }

		[AsJson, FormControl(Excluded = true), JsonIgnore, XmlIgnore]
		public ExtendedUIDefinition ExtendedUIDefinition { get; }

		[XmlIgnore]
		public string DefinitionType { get; set; }

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public EntityDefinition Definition => RepositoryMediator.GetEntityDefinition(AssemblyLoader.GetType(this.DefinitionType), true);

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public Organization Organization => (this.OrganizationID ?? "").GetOrganizationByID();

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public Module Module => (this.ModuleID ?? "").GetModuleByID();

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public Desktop Desktop => (this.DesktopID ?? "").GetDesktopByID() ?? this.Module?.Desktop;
	}

	internal static class ContentTypeExtensions
	{
		internal static ConcurrentDictionary<string, ContentType> ContentTypes { get; } = new ConcurrentDictionary<string, ContentType>(StringComparer.OrdinalIgnoreCase);

		internal static ContentType UpdateContentType(this ContentType contentType)
		{
			if (contentType != null)
				ContentTypeExtensions.ContentTypes[contentType.ID] = contentType;
			return contentType;
		}

		internal static ContentType GetContentTypeByID(this string id, bool force = false, bool fetchRepository = true)
			=> !force && !string.IsNullOrWhiteSpace(id) && ContentTypeExtensions.ContentTypes.ContainsKey(id)
				? ContentTypeExtensions.ContentTypes[id]
				: fetchRepository && !string.IsNullOrWhiteSpace(id)
					? ContentType.Get<ContentType>(id).UpdateContentType()
					: null;

		internal static async Task<ContentType> GetContentTypeByIDAsync(string id, CancellationToken cancellationToken = default, bool force = false)
		{
			var contentType = (id ?? "").GetContentTypeByID(force, false) ?? await ContentType.GetAsync<ContentType>(id, cancellationToken).ConfigureAwait(false);
			return contentType?.UpdateContentType();
		}
	}
}
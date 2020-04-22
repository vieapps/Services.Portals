#region Related components
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
	[Entity(CollectionName = "Modules", TableName = "T_Portals_Modules", CacheClass = typeof(Utility), CacheName = "Cache", Searchable = true)]
	public sealed class Module : Repository<Module>, IPortalModule
	{
		public Module() : base()
			=> this.ID = "";

		[Property(MaxLength = 250, NotNull = true, NotEmpty = true), Sortable(IndexName = "Title"), Searchable, FormControl(Label = "{{portals.modules.controls.[name]}}", PlaceHolder = "{{portals.modules.controls.[name].placeholder}}", Description = "{{portals.modules.controls.[name].description}}")]
		public override string Title { get; set; } = "";

		[FormControl(Label = "{{portals.modules.controls.[name]}}", PlaceHolder = "{{portals.modules.controls.[name].placeholder}}", Description = "{{portals.modules.controls.[name].description}}")]
		public string Description { get; set; }

		[Property(MaxLength = 32), FormControl(Label = "{{portals.modules.controls.[name]}}", PlaceHolder = "{{portals.modules.controls.[name].placeholder}}", Description = "{{portals.modules.controls.[name].description}}")]
		public string DesktopID { get; set; }

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

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public override string RepositoryID { get; set; }

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public override string EntityID { get; set; }

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public string OrganizationID => this.SystemID;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public new IPortalObject Parent => this.Organization;

		[XmlIgnore]
		public string DefinitionType { get; set; }

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public RepositoryDefinition Definition => RepositoryMediator.GetRepositoryDefinition(AssemblyLoader.GetType(this.DefinitionType), true);

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public Organization Organization => (this.OrganizationID ?? "").GetOrganizationByID();

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public Desktop Desktop => (this.DesktopID ?? "").GetDesktopByID() ?? this.Organization?.HomeDesktop;
	}

	internal static class ModuleExtensions
	{
		internal static ConcurrentDictionary<string, Module> Modules { get; } = new ConcurrentDictionary<string, Module>(StringComparer.OrdinalIgnoreCase);

		internal static Module UpdateModule(this Module module)
		{
			if (module != null)
				ModuleExtensions.Modules[module.ID] = module;
			return module;
		}

		internal static Module GetModuleByID(this string id, bool force = false, bool fetchRepository = true)
			=> !force && !string.IsNullOrWhiteSpace(id) && ModuleExtensions.Modules.ContainsKey(id)
				? ModuleExtensions.Modules[id]
				: fetchRepository && !string.IsNullOrWhiteSpace(id)
					? Module.Get<Module>(id)?.UpdateModule()
					: null;

		internal static async Task<Module> GetModuleByIDAsync(this string id, CancellationToken cancellationToken = default, bool force = false)
		{
			var module = (id ?? "").GetModuleByID(force, false) ?? await Module.GetAsync<Module>(id, cancellationToken).ConfigureAwait(false);
			return module?.UpdateModule();
		}
	}
}
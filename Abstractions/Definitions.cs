using System;
using System.Linq;
using System.Collections.Generic;
using net.vieapps.Components.Repository;
using net.vieapps.Components.Utility;

namespace net.vieapps.Services.Portals
{
	/// <summary>
	/// Presents a definition of a portal module
	/// </summary>
	[Serializable]
	public class ModuleDefinition
	{
		public ModuleDefinition() : this(null) { }

		public ModuleDefinition(RepositoryDefinition definition, string serviceName = null)
		{
			this.ID = definition?.ID;
			this.Title = definition?.Title;
			this.Description = definition?.Description;
			this.Icon = definition?.Icon;
			this.Directory = definition?.Directory ?? serviceName ?? ServiceBase.ServiceComponent.ServiceName;
			this.ServiceName = serviceName ?? ServiceBase.ServiceComponent.ServiceName;
			this.ContentTypeDefinitions = definition == null
				? new List<ContentTypeDefinition>()
				: definition.EntityDefinitions.Where(entityDefinition => !string.IsNullOrWhiteSpace(entityDefinition.ID)).Select(entityDefinition => new ContentTypeDefinition(entityDefinition)).ToList();
		}

		/// <summary>
		/// Gets or Sets the identity
		/// </summary>
		public string ID { get; set; }

		/// <summary>
		/// Gets or Sets the title
		/// </summary>
		public string Title { get; set; }

		/// <summary>
		/// Gets or Sets the description
		/// </summary>
		public string Description { get; set; }

		/// <summary>
		/// Gets or sets the name of the icon for working with user interfaces
		/// </summary>
		public string Icon { get; set; }

		/// <summary>
		/// Gets or sets the name of the directory that contains all files for working with user interfaces
		/// </summary>
		public string Directory { get; set; }

		/// <summary>
		/// Gets or sets the name of the service that responsibilty to process the request
		/// </summary>
		public string ServiceName { get; set; }

		/// <summary>
		/// Gets or sets the collection of content-type definitions of the module
		/// </summary>
		public List<ContentTypeDefinition> ContentTypeDefinitions { get; set; }
	}

	/// <summary>
	/// Presents a definition of a portal content-type
	/// </summary>
	[Serializable]
	public class ContentTypeDefinition
	{
		public ContentTypeDefinition() : this(null) { }

		public ContentTypeDefinition(EntityDefinition definition, string objectName = null, string parentObjectName = null)
		{
			this.ID = definition?.ID;
			this.Title = definition?.Title;
			this.Description = definition?.Description;
			this.Icon = definition?.Icon;
			this.MultipleIntances = definition == null ? false : definition.MultipleIntances;
			this.Extendable = definition == null ? false : definition.Extendable;
			this.Indexable = definition == null ? false : definition.Indexable;
			this.ParentAssociatedProperty = definition?.ParentAssociatedProperty;
			this.AliasProperty = definition?.AliasProperty;
			this.ObjectName = objectName ?? definition?.Type.GetTypeName(true);
			this.ParentObjectName = parentObjectName ?? definition?.ParentType?.GetTypeName(true);
		}

		/// <summary>
		/// Gets or Sets the identity
		/// </summary>
		public string ID { get; set; }

		/// <summary>
		/// Gets or Sets the title
		/// </summary>
		public string Title { get; set; }

		/// <summary>
		/// Gets or Sets the description
		/// </summary>
		public string Description { get; set; }

		/// <summary>
		/// Gets or sets the name of the icon for working with user interfaces
		/// </summary>
		public string Icon { get; set; }

		/// <summary>
		/// Gets or Sets the state that allow to use multiple instances
		/// </summary>
		public bool MultipleIntances { get; set; }

		/// <summary>
		/// Gets or Sets the state that allow to extent properties
		/// </summary>
		public bool Extendable { get; set; }

		/// <summary>
		/// Gets or sets the state that specifies this entity is able to index with global search module
		/// </summary>
		public bool Indexable { get; set; }

		/// <summary>
		/// Gets or Sets the name of the property that use to associate with parent object
		/// </summary>
		public string ParentAssociatedProperty { get; set; }

		/// <summary>
		/// Gets or Sets the name of the property to use as alias
		/// </summary>
		public string AliasProperty { get; set; }

		/// <summary>
		/// Gets or sets the name of the service's object that responsibilty to process the request
		/// </summary>
		public string ObjectName { get; set; }

		/// <summary>
		/// Gets or Sets the name of the service's object that marked as parent object
		/// </summary>
		public string ParentObjectName { get; set; }
	}
}
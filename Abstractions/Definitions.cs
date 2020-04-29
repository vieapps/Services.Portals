using System;
using System.Linq;
using System.Collections.Generic;
using System.Xml.Serialization;
using Newtonsoft.Json;
using net.vieapps.Components.Repository;
using net.vieapps.Components.Utility;

namespace net.vieapps.Services.Portals
{
	/// <summary>
	/// Presents a module definition in a portal/system
	/// </summary>
	public class ModuleDefinition
	{
		public ModuleDefinition() : this(null) { }

		public ModuleDefinition(RepositoryDefinition definition)
		{
			if (definition != null)
			{
				this.RepositoryDefinition = definition;
				this.RepositoryDefinitionTypeName = definition.Type.GetTypeName();
				this.ID = definition.ID;
				this.Title = definition.Title ?? definition.Type.GetTypeName(true);
				this.Description = definition.Description;
				this.Icon = definition.Icon;
				this.Directory = definition.Directory ?? definition.Type.GetTypeName(true);
				this.ServiceName = definition.ServiceName;
				this.ContentTypeDefinitions = definition.EntityDefinitions.Where(entityDefinition => !string.IsNullOrWhiteSpace(entityDefinition.ID)).Select(entityDefinition => new ContentTypeDefinition(entityDefinition, this)).ToList();
			}
		}

		/// <summary>
		/// Gets or Sets the identity (means the value of RepositoryAttribute.ID)
		/// </summary>
		public string ID { get; set; }

		/// <summary>
		/// Gets or Sets the title (means the value of RepositoryAttribute.Title)
		/// </summary>
		public string Title { get; set; }

		/// <summary>
		/// Gets or Sets the description (means the value of RepositoryAttribute.Description)
		/// </summary>
		public string Description { get; set; }

		/// <summary>
		/// Gets or Sets the name of the icon for working with user interfaces (means the value of RepositoryAttribute.Icon)
		/// </summary>
		public string Icon { get; set; }

		/// <summary>
		/// Gets or Sets the name of the directory that contains the files for working with user interfaces (means the value of RepositoryAttribute.Directory)
		/// </summary>
		public string Directory { get; set; }

		/// <summary>
		/// Gets or Sets the name of the service that associates with this module definition
		/// </summary>
		public string ServiceName { get; set; }

		/// <summary>
		/// Gets or Sets the type name of the repository definition
		/// </summary>
		public string RepositoryDefinitionTypeName { get; set; }

		/// <summary>
		/// Gets or Sets the repository definition
		/// </summary>
		[JsonIgnore, XmlIgnore]
		public RepositoryDefinition RepositoryDefinition { get; set; }

		/// <summary>
		/// Gets or Sets the collection of content-type definitions
		/// </summary>
		public List<ContentTypeDefinition> ContentTypeDefinitions { get; set; }
	}

	//  ------------------------------------------------------------------------

	/// <summary>
	/// Presents a definition of a portal content-type
	/// </summary>
	public class ContentTypeDefinition
	{
		public ContentTypeDefinition() : this(null) { }

		public ContentTypeDefinition(EntityDefinition definition, ModuleDefinition moduleDefinition = null)
		{
			if (definition != null)
			{
				this.EntityDefinition = definition;
				this.EntityDefinitionTypeName = definition.Type.GetTypeName();
				this.ModuleDefinition = moduleDefinition;
				this.ID = definition.ID;
				this.Title = definition.Title ?? definition.Type.GetTypeName(true);
				this.Description = definition.Description;
				this.Icon = definition.Icon;
				this.MultipleIntances = definition.MultipleIntances;
				this.Extendable = definition.Extendable;
				this.Indexable = definition.Indexable;
				this.AliasProperty = definition.AliasProperty;
				this.ParentAssociatedProperty = definition.ParentAssociatedProperty;
				this.ParentObjectName = definition.ParentType?.GetTypeName(true);
				this.ObjectName = definition.ObjectName;
			}
		}

		/// <summary>
		/// Gets or Sets the identity (means the value of EntityAttribute.ID)
		/// </summary>
		public string ID { get; set; }

		/// <summary>
		/// Gets or Sets the title (means the value of EntityAttribute.Title)
		/// </summary>
		public string Title { get; set; }

		/// <summary>
		/// Gets or Sets the description (means the value of EntityAttribute.Description)
		/// </summary>
		public string Description { get; set; }

		/// <summary>
		/// Gets or sets the name of the icon for working with user interfaces (means the value of EntityAttribute.Icon)
		/// </summary>
		public string Icon { get; set; }

		/// <summary>
		/// Gets or Sets the state that allow to use multiple instances (means the value of EntityAttribute.MultipleIntances)
		/// </summary>
		public bool MultipleIntances { get; set; }

		/// <summary>
		/// Gets or Sets the state that allow to extend the run-time entities by extended properties (means the value of EntityAttribute.Extendable)
		/// </summary>
		public bool Extendable { get; set; }

		/// <summary>
		/// Gets or sets the state that specifies the data of run-time entities are able to index with global search module (means the value of EntityAttribute.Indexable)
		/// </summary>
		public bool Indexable { get; set; }

		/// <summary>
		/// Gets or Sets the name of the property to use as alias (means the value of EntityAttribute.AliasProperty)
		/// </summary>
		public string AliasProperty { get; set; }

		/// <summary>
		/// Gets or Sets the name of the property that use to associate with the parent object (means the value of EntityAttribute.ParentAssociatedProperty)
		/// </summary>
		public string ParentAssociatedProperty { get; set; }

		/// <summary>
		/// Gets or Sets the name of the service's object that associates as parent object (means the type-name value of EntityAttribute.ParentType)
		/// </summary>
		public string ParentObjectName { get; set; }

		/// <summary>
		/// Gets or sets the name of the service's object that associates with this content-type definition
		/// </summary>
		public string ObjectName { get; set; }

		/// <summary>
		/// Gets or Sets the type name of the entity definition
		/// </summary>
		public string EntityDefinitionTypeName { get; set; }

		/// <summary>
		/// Gets or Sets the entity definition
		/// </summary>
		[JsonIgnore, XmlIgnore]
		public EntityDefinition EntityDefinition { get; set; }

		/// <summary>
		/// Gets or Sets the module definition of this content-type definition
		/// </summary>
		[JsonIgnore, XmlIgnore]
		public ModuleDefinition ModuleDefinition { get; set; }
	}
}
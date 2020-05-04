#region Related components
using System;
using System.Linq;
using System.Collections.Generic;
using System.Xml.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using net.vieapps.Components.Repository;
using net.vieapps.Components.Utility;
#endregion

namespace net.vieapps.Services.Portals
{
	/// <summary>
	/// Presents a module definition in a portal/system
	/// </summary>
	[Serializable]
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
	[Serializable]
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
				this.Indexable = definition.Indexable;
				this.Extendable = definition.Extendable;
				this.ObjectName = definition.ObjectName;
				this.ParentObjectName = definition.ParentType?.GetTypeName(true);
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
		/// Gets or sets the state that specifies the data of run-time entities are able to index with global search module (means the value of EntityAttribute.Indexable)
		/// </summary>
		public bool Indexable { get; set; }

		/// <summary>
		/// Gets or Sets the state that allow to extend the run-time entities by extended properties (means the value of EntityAttribute.Extendable)
		/// </summary>
		public bool Extendable { get; set; }

		/// <summary>
		/// Gets or sets the name of the service's object that associates with this content-type definition
		/// </summary>
		public string ObjectName { get; set; }

		/// <summary>
		/// Gets or Sets the name of the service's object that associates as parent object (means the type-name value of EntityAttribute.ParentType)
		/// </summary>
		public string ParentObjectName { get; set; }

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

	//  ------------------------------------------------------------------------

	/// <summary>
	/// Presents an UI definition for working with an extended property of a repository entity in a respository 
	/// </summary>
	[Serializable]
	public sealed class ExtendedUIControlDefinition
	{
		public ExtendedUIControlDefinition() : this(null) { }

		public ExtendedUIControlDefinition(JObject json)
			=> this.CopyFrom(json ?? new JObject());

		public override string ToString()
			=> this.ToJson().ToString(Formatting.None);

		#region Properties
		/// <summary>
		/// Gets or sets the name
		/// </summary>
		public string Name { get; set; }

		/// <summary>
		/// Gets or sets the excluded state
		/// </summary>
		public bool Excluded { get; set; } = false;

		/// <summary>
		/// Gets or sets the hidden state
		/// </summary>
		public bool Hidden { get; set; } = false;

		/// <summary>
		/// Gets or sets the state that mark this property is hidden in the view or not
		/// </summary>
		public bool? HiddenInView { get; set; }

		/// <summary>
		/// Gets or sets the require state
		/// </summary>
		public bool Required { get; set; } = false;

		/// <summary>
		/// Gets or sets the label - use doube braces to specified code of a language resource - ex: {{common.buttons.ok}}
		/// </summary>
		public string Label { get; set; }

		/// <summary>
		/// Gets or sets the place-holder - use doube braces to specified code of a language resource - ex: {{common.buttons.ok}}
		/// </summary>
		public string PlaceHolder { get; set; }

		/// <summary>
		/// Gets or sets the description - use doube braces to specified code of a language resource - ex: {{common.buttons.ok}}
		/// </summary>
		public string Description { get; set; }

		/// <summary>
		/// Gets or sets the RegEx pattern for data validation
		/// </summary>
		public bool ValidatePattern { get; set; }

		/// <summary>
		/// Gets or sets the name of a standard property that the UI control of extended property will place before
		/// </summary>
		public string PlaceBefore { get; set; }

		/// <summary>
		/// Gets or sets the disable state
		/// </summary>
		public bool? Disabled { get; set; }

		/// <summary>
		/// Gets or sets the read-only state
		/// </summary>
		public bool? ReadOnly { get; set; }

		/// <summary>
		/// Gets or sets the auto-focus state
		/// </summary>
		public bool? AutoFocus { get; set; }

		/// <summary>
		/// Gets or sets the min value
		/// </summary>
		public double? MinValue { get; set; }

		/// <summary>
		/// Gets or sets the max value
		/// </summary>
		public double? MaxValue { get; set; }

		/// <summary>
		/// Gets or sets the min-length
		/// </summary>
		public int? MinLength { get; set; }

		/// <summary>
		/// Gets or sets the max-length
		/// </summary>
		public int? MaxLength { get; set; }

		/// <summary>
		/// Gets or sets the width
		/// </summary>
		public string Width { get; set; }

		/// <summary>
		/// Gets or sets the height
		/// </summary>
		public string Height { get; set; }

		/// <summary>
		/// Gets or sets the state to act as text/html editor
		/// </summary>
		public bool? AsTextEditor { get; set; }

		/// <summary>
		/// Gets or sets the date-picker with times
		/// </summary>
		public bool? DatePickerWithTimes { get; set; }

		/// <summary>
		/// Gets or sets the multiple of select/lookup control
		/// </summary>
		public bool? Multiple { get; set; }

		/// <summary>
		/// Gets or sets the values of select control (JSON string)
		/// </summary>
		public string SelectValues { get; set; }

		/// <summary>
		/// Gets or sets the 'as-boxes' of select control
		/// </summary>
		public bool? SelectAsBoxes { get; set; }

		/// <summary>
		/// Gets or sets the interface mode of select control (alert, popover, actionsheet)
		/// </summary>
		public string SelectInterface { get; set; }

		/// <summary>
		/// Gets or sets the mode for looking-up (Address, User or Business Object)
		/// </summary>
		public string LookupMode { get; set; }

		/// <summary>
		/// Gets or sets the identity of the business repository for looking-up
		/// </summary>
		public string LookupRepositoryID { get; set; }

		/// <summary>
		/// Gets or sets the identity of the business entity for looking-up
		/// </summary>
		public string LookupRepositoryEntityID { get; set; }

		/// <summary>
		/// Gets or sets the name of business entity's property for displaying while looking-up
		/// </summary>
		public string LookupProperty { get; set; }
		#endregion
	}

	//  ------------------------------------------------------------------------

	/// <summary>
	/// Presents a definition for working with the extended properties of a repository entity in a respository 
	/// </summary>
	[Serializable]
	public sealed class ExtendedUIDefinition
	{
		public ExtendedUIDefinition() : this(null) { }

		public ExtendedUIDefinition(JObject json)
			=> this.CopyFrom(json ?? new JObject());

		public override string ToString()
			=> this.ToJson().ToString(Formatting.None);

		public List<ExtendedUIControlDefinition> Controls { get; set; }

		public List<StandardUIDefinition> Specials { get; set; }

		public string ListXslt { get; set; }

		public string ViewXslt { get; set; }
	}

	//  ------------------------------------------------------------------------

	/// <summary>
	/// Presents a definition for working with a standard property of a repository entity in a respository 
	/// </summary>
	[Serializable]
	public sealed class StandardUIDefinition
	{
		public StandardUIDefinition() : this(null) { }

		public StandardUIDefinition(JObject json)
			=> this.CopyFrom(json ?? new JObject());

		public override string ToString()
			=> this.ToJson().ToString(Formatting.None);

		public string Name { get; set; }

		public string Title { get; set; }

		public string PlaceHolder { get; set; }

		public string Description { get; set; }

		public bool Hidden { get; set; } = false;

		public bool? HiddenInView { get; set; }

		public string Formula { get; set; }
	}
}
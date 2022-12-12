#region Related components
using System.Linq;
using System.Dynamic;
using System.Diagnostics;
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
	[DebuggerDisplay("ID = {ID}, Title = {Title}")]
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
				this.ObjectDefinitions = definition.EntityDefinitions.Where(entityDefinition => string.IsNullOrWhiteSpace(entityDefinition.ID)).Select(entityDefinition => new ContentTypeDefinition(entityDefinition, this)).ToList();
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

		/// <summary>
		/// Gets or Sets the collection of other object definitions
		/// </summary>
		public List<ContentTypeDefinition> ObjectDefinitions { get; set; }
	}

	//  ------------------------------------------------------------------------

	/// <summary>
	/// Presents a definition of a portal content-type
	/// </summary>
	[DebuggerDisplay("ID = {ID}, Title = {Title}")]
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
				this.Title = definition.Title ?? (string.IsNullOrWhiteSpace(definition.ID) ? null : definition.Type.GetTypeName(true));
				this.Description = definition.Description;
				this.Icon = definition.Icon;
				this.MultipleIntances = definition.MultipleIntances;
				this.Indexable = definition.Indexable;
				this.Extendable = definition.Extendable;
				this.ObjectName = definition.ObjectName;
				this.ObjectNamePrefix = definition.ObjectNamePrefix;
				this.ObjectNameSuffix = definition.ObjectNameSuffix;
				this.ParentObjectName = definition.ParentType?.GetTypeName(true);
				this.NestedObject = typeof(INestedObject).IsAssignableFrom(definition.Type);
				this.Portlets = definition.Portlets;
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
		/// Gets or Sets the name of the icon for working with user interfaces (means the value of EntityAttribute.Icon)
		/// </summary>
		public string Icon { get; set; }

		/// <summary>
		/// Gets or Sets the state that allow to use multiple instances (means the value of EntityAttribute.MultipleIntances)
		/// </summary>
		public bool MultipleIntances { get; set; }

		/// <summary>
		/// Gets or Sets the state that specifies the data of run-time entities are able to index with global search module (means the value of EntityAttribute.Indexable)
		/// </summary>
		public bool Indexable { get; set; }

		/// <summary>
		/// Gets or Sets the state that allow to extend the run-time entities by extended properties (means the value of EntityAttribute.Extendable)
		/// </summary>
		public bool Extendable { get; set; }

		/// <summary>
		/// Gets or Sets the name of the service's object that associates with this content-type definition
		/// </summary>
		public string ObjectName { get; set; }

		/// <summary>
		/// Gets or Sets the name prefix of the service's object that associates with this content-type definition
		/// </summary>
		public string ObjectNamePrefix { get; set; }

		/// <summary>
		/// Gets or Sets the name suffix of the service's object that associates with this content-type definition
		/// </summary>
		public string ObjectNameSuffix { get; set; }

		/// <summary>
		/// Gets or Sets the name of the service's object that associates as parent object (means the type-name value of EntityAttribute.ParentType)
		/// </summary>
		public string ParentObjectName { get; set; }

		/// <summary>
		/// Gets or Sets the nested state
		/// </summary>
		public bool NestedObject { get; set; } = false;

		/// <summary>
		/// Gets or Sets the portlets state
		/// </summary>
		public bool Portlets { get; set; } = false;

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
	/// Presents the definition of a control for working with an extended property of a repository entity in a respository 
	/// </summary>
	[DebuggerDisplay("Name = {Name}")]
	public sealed class ExtendedControlDefinition
	{
		public ExtendedControlDefinition() { }

		public ExtendedControlDefinition(JObject data)
			=> this.CopyFrom(data ?? new JObject());

		public ExtendedControlDefinition(ExpandoObject data)
			=> this.CopyFrom(data ?? new ExpandoObject());

		public override string ToString()
			=> this.ToJson().ToString(Formatting.None);

		/// <summary>
		/// Gets or Sets the name
		/// </summary>
		public string Name { get; set; }

		/// <summary>
		/// Gets or Sets the label - use doube braces to specified code of a language resource - ex: {{common.buttons.ok}}
		/// </summary>
		public string Label { get; set; }

		/// <summary>
		/// Gets or Sets the place-holder - use doube braces to specified code of a language resource - ex: {{common.buttons.ok}}
		/// </summary>
		public string PlaceHolder { get; set; }

		/// <summary>
		/// Gets or Sets the description - use doube braces to specified code of a language resource - ex: {{common.buttons.ok}}
		/// </summary>
		public string Description { get; set; }

		/// <summary>
		/// Gets or Sets the hidden state
		/// </summary>
		public bool? Hidden { get; set; }

		/// <summary>
		/// Gets or Sets the formula/expression for computing value when the control is hidden
		/// </summary>
		public string Formula { get; set; }

		/// <summary>
		/// Gets or Sets the state that mark this property is hidden in the view or not
		/// </summary>
		public bool? HiddenInView { get; set; }

		/// <summary>
		/// Gets or Sets the data type of control (ex: text, url, tel, ... follow the HTML5 input controls)
		/// </summary>
		public string DataType { get; set; }

		/// <summary>
		/// Gets or Sets the require state
		/// </summary>
		public bool? Required { get; set; }

		/// <summary>
		/// Gets or Sets the disable state
		/// </summary>
		public bool? Disabled { get; set; }

		/// <summary>
		/// Gets or Sets the read-only state
		/// </summary>
		public bool? ReadOnly { get; set; }

		/// <summary>
		/// Gets or Sets the auto-focus state
		/// </summary>
		public bool? AutoFocus { get; set; }

		/// <summary>
		/// Gets or Sets the min value
		/// </summary>
		public string MinValue { get; set; }

		/// <summary>
		/// Gets or Sets the max value
		/// </summary>
		public string MaxValue { get; set; }

		/// <summary>
		/// Gets or Sets the min-length
		/// </summary>
		public int? MinLength { get; set; }

		/// <summary>
		/// Gets or Sets the max-length
		/// </summary>
		public int? MaxLength { get; set; }

		/// <summary>
		/// Gets or Sets the CSS classes
		/// </summary>
		public string Css { get; set; }

		/// <summary>
		/// Gets or Sets the width
		/// </summary>
		public string Width { get; set; }

		/// <summary>
		/// Gets or Sets the height
		/// </summary>
		public string Height { get; set; }

		/// <summary>
		/// Gets or Sets the rows (text-area)
		/// </summary>
		public int? Rows { get; set; }

		/// <summary>
		/// Gets or Sets the state to act as text/html editor
		/// </summary>
		public bool? AsTextEditor { get; set; }

		/// <summary>
		/// Gets or Sets the date-picker with times
		/// </summary>
		public bool? DatePickerWithTimes { get; set; }

		/// <summary>
		/// Gets or Sets the multiple of select/lookup control
		/// </summary>
		public bool? Multiple { get; set; }

		/// <summary>
		/// Gets or Sets the values of select control, values are separated by comma (,)
		/// </summary>
		public string SelectValues { get; set; }

		/// <summary>
		/// Gets or Sets the 'as-boxes' of select control
		/// </summary>
		public bool? SelectAsBoxes { get; set; }

		/// <summary>
		/// Gets or Sets the interface mode of select control (alert, popover, actionsheet)
		/// </summary>
		public string SelectInterface { get; set; }

		/// <summary>
		/// Gets or Sets the mode for looking-up (Address, User or Business Object)
		/// </summary>
		public string LookupType { get; set; }

		/// <summary>
		/// Gets or Sets the identity of the business repository for looking-up
		/// </summary>
		public string LookupRepositoryID { get; set; }

		/// <summary>
		/// Gets or Sets the identity of the business entity for looking-up
		/// </summary>
		public string LookupRepositoryEntityID { get; set; }

		/// <summary>
		/// Gets or Sets the RegEx pattern for data validation
		/// </summary>
		public string ValidatePattern { get; set; }

		/// <summary>
		/// Gets or Sets the name of a standard property that the control of extended property will place before
		/// </summary>
		public string PlaceBefore { get; set; }
	}

	//  ------------------------------------------------------------------------

	/// <summary>
	/// Presents the definition of a control for working with a standard property of a repository entity in a respository 
	/// </summary>
	[DebuggerDisplay("Name = {Name}")]
	public sealed class StandardControlDefinition
	{
		public StandardControlDefinition() { }

		public StandardControlDefinition(JObject data)
			=> this.CopyFrom(data ?? new JObject());

		public StandardControlDefinition(ExpandoObject data)
			=> this.CopyFrom(data ?? new ExpandoObject());

		public override string ToString()
			=> this.ToJson().ToString(Formatting.None);

		/// <summary>
		/// Gets or Sets the name
		/// </summary>
		public string Name { get; set; }

		/// <summary>
		/// Gets or Sets the label - use doube braces to specified code of a language resource - ex: {{common.buttons.ok}}
		/// </summary>
		public string Label { get; set; }

		/// <summary>
		/// Gets or Sets the place-holder - use doube braces to specified code of a language resource - ex: {{common.buttons.ok}}
		/// </summary>
		public string PlaceHolder { get; set; }

		/// <summary>
		/// Gets or Sets the description - use doube braces to specified code of a language resource - ex: {{common.buttons.ok}}
		/// </summary>
		public string Description { get; set; }

		/// <summary>
		/// Gets or Sets the CSS classes
		/// </summary>
		public string Css { get; set; }

		/// <summary>
		/// Gets or Sets the state that mark this property is required or not
		/// </summary>
		public bool? Required { get; set; }

		/// <summary>
		/// Gets or Sets the hidden state
		/// </summary>
		public bool Hidden { get; set; } = false;

		/// <summary>
		/// Gets or Sets the state that mark this property is hidden in the view or not
		/// </summary>
		public bool? HiddenInView { get; set; }

		/// <summary>
		/// Gets or Sets the default value
		/// </summary>
		public string DefaultValue { get; set; }

		/// <summary>
		/// Gets or Sets the formula/expression for computing value when the control is hidden
		/// </summary>
		public string Formula { get; set; }
	}
}
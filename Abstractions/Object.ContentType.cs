#region Related components
using System;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using net.vieapps.Components.Repository;
using net.vieapps.Components.Security;
using net.vieapps.Components.Utility;
#endregion

namespace net.vieapps.Services.Portals
{
	/// <summary>
	/// Presents a business content-type of a portal
	/// </summary>
	public interface IPortalContentType : IPortalObject
	{
		/// <summary>
		/// Gets the organization that this object is belong to
		/// </summary>
		IPortalObject Organization { get; }

		/// <summary>
		/// Gets the identity of the business module that this object is belong to
		/// </summary>
		string ModuleID { get; }

		/// <summary>
		/// Gets the business module that this object is belong to
		/// </summary>
		IPortalModule Module { get; }

		/// <summary>
		/// Gets the identity of the content-type definition
		/// </summary>
		string ContentTypeDefinitionID { get; }

		/// <summary>
		/// Gets the module definition
		/// </summary>
		ContentTypeDefinition ContentTypeDefinition { get; }

		/// <summary>
		/// Gets the type name of the entity definition
		/// </summary>
		string EntityDefinitionTypeName { get; }

		/// <summary>
		/// Gets the collection of extended properties
		/// </summary>
		List<ExtendedPropertyDefinition> ExtendedPropertyDefinitions { get; }

		/// <summary>
		/// Gets the collection of extended controls
		/// </summary>
		List<ExtendedControlDefinition> ExtendedControlDefinitions { get; }

		/// <summary>
		/// Gets the collection of standard controls
		/// </summary>
		List<StandardControlDefinition> StandardControlDefinitions { get; }

		/// <summary>
		/// Gets the formula for computing the  sub-title
		/// </summary>
		string SubTitleFormula { get; }
	}

	public static partial class ObjectExtensions
	{
		/// <summary>
		/// Generates form controls
		/// </summary>
		/// <param name="contentType"></param>
		/// <param name="forViewing"></param>
		/// <param name="getContentTypeByID"></param>
		/// <param name="onCompleted"></param>
		/// <returns></returns>
		public static JToken GenerateFormControls(this IPortalContentType contentType, bool forViewing, Func<string, IPortalContentType> getContentTypeByID, Action<JToken> onCompleted = null)
		{
			// generate standard controls
			var controls = (RepositoryMediator.GenerateFormControls(contentType?.ContentTypeDefinition?.EntityDefinition?.Type) as JArray).Select(control => control as JObject).ToList();

			// update standard controls
			contentType?.StandardControlDefinitions?.Where(definition => !string.IsNullOrWhiteSpace(definition.Name)).ForEach(definition =>
			{
				var control = controls.FirstOrDefault(ctrl => definition.Name.IsEquals(ctrl.Get<string>("Name")));
				if (control != null)
				{
					if (!forViewing)
						control["Required"] = definition.Required != null && definition.Required.Value;

					control["Hidden"] = forViewing
						? definition.HiddenInView != null && definition.HiddenInView.Value
						: definition.Hidden;

					var options = control.Get("Options", new JObject());

					if (!string.IsNullOrWhiteSpace(definition.Label))
						options["Label"] = definition.Label;

					if (!string.IsNullOrWhiteSpace(definition.Description))
						options["Description"] = definition.Description;

					if (!string.IsNullOrWhiteSpace(definition.PlaceHolder))
						options["PlaceHolder"] = definition.PlaceHolder;

					if (!string.IsNullOrWhiteSpace(definition.DefaultValue))
						options["DefaultValue"] = definition.DefaultValue;

					control["Options"] = options;
				}
			});

			// generate extended controls
			contentType?.ExtendedControlDefinitions?.ForEach(definition =>
			{
				var control = definition.GenerateFormControl(contentType.ExtendedPropertyDefinitions.Find(def => def.Name.IsEquals(definition.Name)).Mode, contentType.ExtendedPropertyDefinitions?.FirstOrDefault(def => def.Name.Equals(definition.Name))?.DefaultValue, getContentTypeByID);

				if (forViewing && definition.HiddenInView != null && definition.HiddenInView.Value)
					control["Hidden"] = true;

				var index = !string.IsNullOrWhiteSpace(definition.PlaceBefore) ? controls.FindIndex(ctrl => definition.PlaceBefore.IsEquals(ctrl.Get<string>("Name"))) : -1;
				if (index > -1)
				{
					control["Segment"] = controls[index].Get<string>("Segment");
					controls.Insert(index, control);
				}
				else
					controls.Add(control);
			});

			// update order index
			controls.ForEach((control, order) => control["Order"] = order);
			var formControls = controls.ToJArray();
			onCompleted?.Invoke(formControls);
			return formControls;
		}

		static JObject GenerateFormControl(this ExtendedControlDefinition definition, ExtendedPropertyMode mode, string defaultValue, Func<string, IPortalContentType> getContentTypeByID)
		{
			var controlType = mode.Equals(ExtendedPropertyMode.LargeText) || (definition.AsTextEditor != null && definition.AsTextEditor.Value)
				? mode.Equals(ExtendedPropertyMode.LargeText) && definition.AsTextEditor != null && !definition.AsTextEditor.Value ? "TextArea" : "TextEditor"
				: mode.Equals(ExtendedPropertyMode.Select)
					? "Select"
					: mode.Equals(ExtendedPropertyMode.Lookup)
						? "Lookup"
						: mode.Equals(ExtendedPropertyMode.DateTime)
							? "DatePicker"
							: mode.Equals(ExtendedPropertyMode.YesNo)
								? "YesNo"
								: mode.Equals(ExtendedPropertyMode.MediumText) ? "TextArea" : "TextBox";

			var hidden = definition.Hidden != null && definition.Hidden.Value;
			var options = new JObject();
			if (!hidden)
			{
				options["Label"] = definition.Label;
				options["PlaceHolder"] = definition.PlaceHolder;
				options["Description"] = definition.Description;

				var dataType = !string.IsNullOrWhiteSpace(definition.DataType)
					? definition.DataType
					: "Lookup".IsEquals(controlType) && !string.IsNullOrWhiteSpace(definition.LookupType)
						? definition.LookupType
						: "DatePicker".IsEquals(controlType)
							? "date"
							: mode.Equals(ExtendedPropertyMode.IntegralNumber) || mode.Equals(ExtendedPropertyMode.FloatingPointNumber)
								? "number"
								: null;

				if (!string.IsNullOrWhiteSpace(dataType))
					options["Type"] = dataType;

				if (!string.IsNullOrWhiteSpace(defaultValue))
					options["DefaultValue"] = defaultValue;

				if (definition.Disabled != null && definition.Disabled.Value)
					options["Disabled"] = true;

				if (definition.ReadOnly != null && definition.ReadOnly.Value)
					options["ReadOnly"] = true;

				if (definition.AutoFocus != null && definition.AutoFocus.Value)
					options["AutoFocus"] = true;

				if (!string.IsNullOrWhiteSpace(definition.ValidatePattern))
					options["ValidatePattern"] = definition.ValidatePattern;

				if (!string.IsNullOrWhiteSpace(definition.Width))
					options["Width"] = definition.Width;

				if (!string.IsNullOrWhiteSpace(definition.Height))
					options["Height"] = definition.Height;

				if (definition.Rows != null && definition.Rows.Value > 0)
					options["Rows"] = definition.Rows.Value;

				if (!string.IsNullOrWhiteSpace(definition.MinValue))
					try
					{
						if (mode.Equals(ExtendedPropertyMode.IntegralNumber))
							options["MinValue"] = definition.MinValue.CastAs<long>();
						else if (mode.Equals(ExtendedPropertyMode.FloatingPointNumber))
							options["MinValue"] = definition.MinValue.CastAs<decimal>();
						else
							options["MinValue"] = definition.MinValue;
					}
					catch { }

				if (!string.IsNullOrWhiteSpace(definition.MaxValue))
					try
					{
						if (mode.Equals(ExtendedPropertyMode.IntegralNumber))
							options["MaxValue"] = definition.MaxValue.CastAs<long>();
						else if (mode.Equals(ExtendedPropertyMode.FloatingPointNumber))
							options["MaxValue"] = definition.MaxValue.CastAs<decimal>();
						else
							options["MaxValue"] = definition.MaxValue;
					}
					catch { }

				if (definition.MinLength != null && definition.MinLength.Value > 0)
					options["MinLength"] = definition.MinLength.Value;

				if (definition.MaxLength != null && definition.MaxLength.Value > 0)
					options["MaxLength"] = definition.MaxLength.Value;

				if ("DatePicker".IsEquals(controlType))
					options["DatePickerOptions"] = new JObject
					{
						{ "AllowTimes", definition.DatePickerWithTimes != null && definition.DatePickerWithTimes.Value }
					};

				if ("Select".IsEquals(controlType))
					options["SelectOptions"] = new JObject
					{
						{ "Values", definition.SelectValues },
						{ "Multiple", definition.Multiple != null && definition.Multiple.Value },
						{ "AsBoxes", definition.SelectAsBoxes != null && definition.SelectAsBoxes.Value },
						{ "Interface", definition.SelectInterface ?? "alert" }
					};

				if ("Lookup".IsEquals(controlType))
				{
					var contentType = !string.IsNullOrWhiteSpace(definition.LookupRepositoryEntityID) && getContentTypeByID != null ? getContentTypeByID(definition.LookupRepositoryEntityID) : null;
					options["LookupOptions"] = new JObject
					{
						{ "Multiple", definition.Multiple != null && definition.Multiple.Value },
						{ "AsModal", !"Address".IsEquals(definition.LookupType) },
						{ "AsCompleter", "Address".IsEquals(definition.LookupType) },
						{ "ModalOptions", new JObject
							{
								{ "Component", null },
								{ "ComponentProps", new JObject
									{
										{ "organizationID", contentType?.OrganizationID },
										{ "moduleID", contentType?.ModuleID },
										{ "contentTypeID", contentType?.ID },
										{ "objectName", $"{(string.IsNullOrWhiteSpace(contentType?.ContentTypeDefinition.ObjectNamePrefix) ? "" : contentType?.ContentTypeDefinition.ObjectNamePrefix)}{contentType?.ContentTypeDefinition.ObjectName}{(string.IsNullOrWhiteSpace(contentType?.ContentTypeDefinition.ObjectNameSuffix) ? "" : contentType?.ContentTypeDefinition.ObjectNameSuffix)}" },
										{ "nested", contentType?.ContentTypeDefinition.NestedObject },
										{ "multiple", definition.Multiple != null && definition.Multiple.Value }
									}
								}
							}
						}
					};
				}
			}

			return new JObject
			{
				{ "Name", definition.Name },
				{ "Type", controlType },
				{ "Hidden", hidden },
				{ "Required", definition.Required != null && definition.Required.Value },
				{ "Extras", new JObject() },
				{ "Options", options }
			};
		}
	}
}
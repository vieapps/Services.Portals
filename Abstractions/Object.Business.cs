#region Related components
using System;
using System.Linq;
using System.Dynamic;
using System.Collections.Generic;
using net.vieapps.Components.Repository;
using net.vieapps.Components.Security;
using net.vieapps.Components.Utility;
#endregion

namespace net.vieapps.Services.Portals
{
	/// <summary>
	/// Presents a business object of a portal
	/// </summary>
	public interface IBusinessObject : IPortalObject, IBusinessEntity
	{
		/// <summary>
		/// Gets the approval status
		/// </summary>
		ApprovalStatus Status { get; }

		/// <summary>
		/// Gets the organization that this object is belong to
		/// </summary>
		IPortalObject Organization { get; }

		/// <summary>
		/// Gets the identity of a business module that the object is belong to
		/// </summary>
		string ModuleID { get; }

		/// <summary>
		/// Gets the business module that this object is belong to
		/// </summary>
		IPortalModule Module { get; }

		/// <summary>
		/// Gets the identity of a business content-type that the object is belong to
		/// </summary>
		string ContentTypeID { get; }

		/// <summary>
		/// Gets the business content-type that this object is belong to
		/// </summary>
		IPortalContentType ContentType { get; }

		/// <summary>
		/// Gets the public URL
		/// </summary>
		/// <param name="desktop">The string that presents the alias of a desktop</param>
		/// <param name="addPageNumberHolder">true to add the page-number holder ({{pageNumber}})</param>
		/// <param name="parentIdentity">The string that presents the alias of the current requesting parent</param>
		/// <returns></returns>
		string GetURL(string desktop = null, bool addPageNumberHolder = false, string parentIdentity = null);
	}

	public static partial class ObjectExtensions
	{
		/// <summary>
		/// Computes all formulating properties
		/// </summary>
		/// <param name="object"></param>
		/// <param name="requestInfo"></param>
		/// <param name="onCompleted"></param>
		public static T Compute<T>(this T @object, RequestInfo requestInfo = null, System.Action onCompleted = null) where T : IBusinessObject
		{
			// check content type
			var contentType = @object?.ContentType;
			if (contentType == null)
			{
				onCompleted?.Invoke();
				return @object;
			}

			// prepare parameters
			var objectExpando = (@object as IBusinessEntity).ToExpandoObject();
			var requestExpando = requestInfo?.AsExpandoObject;
			var @params = new Dictionary<string, ExpandoObject>
			{
				["ContentType"] = contentType.ToExpandoObject(),
				["Module"] = @object.Module?.ToExpandoObject(),
				["Organization"] = @object.Organization?.ToExpandoObject()
			}.ToExpandoObject();

			// compute standard controls
			var attributes = @object.GetPublicAttributes(attribute => !attribute.IsStatic);
			contentType.StandardControlDefinitions?.Where(definition => !string.IsNullOrWhiteSpace(definition.DefaultValue) || !string.IsNullOrWhiteSpace(definition.Formula)).ForEach(definition =>
			{
				var attribute = attributes.FirstOrDefault(attr => attr.Name == definition.Name);
				if (attribute != null)
				{
					var value = @object.GetAttributeValue(attribute);
					if (value == null)
					{
						value = !string.IsNullOrWhiteSpace(definition.Formula) ? definition.Formula.Evaluate(objectExpando, requestExpando, @params) : null;
						@object.SetAttributeValue(attribute, value ?? definition.DefaultValue, true);
					}
				}
			});

			// compute extended controls
			if (@object.ExtendedProperties != null && @object.ExtendedProperties.Any())
				contentType.ExtendedControlDefinitions?.ForEach(definition =>
				{
					if (!@object.ExtendedProperties.TryGetValue(definition.Name, out var value) || value == null)
					{
						var propertyDefinition = contentType.ExtendedPropertyDefinitions?.FirstOrDefault(def => def.Name == definition.Name);
						value = !string.IsNullOrWhiteSpace(definition.Formula) ? definition.Formula.Evaluate(objectExpando, requestExpando, @params) : null;
						if (value == null)
							value = !string.IsNullOrWhiteSpace(propertyDefinition.DefaultValueFormula)
								? propertyDefinition.DefaultValueFormula.Evaluate(objectExpando, requestExpando, @params)
								: propertyDefinition.GetDefaultValue();
						@object.ExtendedProperties[definition.Name] = value?.CastAs(propertyDefinition.Type);
					}
				});

			// complete
			onCompleted?.Invoke();
			return @object;
		}

		/// <summary>
		/// Validates the objects' attributes
		/// </summary>
		/// <param name="object"></param>
		/// <param name="onValidate"></param>
		public static T Validate<T>(this T @object, Action<string, object> onValidate = null) where T : IBusinessObject
		{
			var entityDefinition = @object.ContentType?.ContentTypeDefinition?.EntityDefinition;
			var standardAttributes = @object.ContentType?.StandardControlDefinitions ?? new List<StandardControlDefinition>();
			var extendedAttributes = @object.ContentType?.ExtendedControlDefinitions ?? new List<ExtendedControlDefinition>();
			var allAttributes = entityDefinition?.Attributes?.Where(attribute => !attribute.IsIgnored() && attribute.CanRead && attribute.CanWrite).ToList() ?? new List<AttributeInfo>();
			var notEmptyAttributes = allAttributes.Where(attribute => attribute.NotEmpty != null && attribute.NotEmpty.Value).Select(attribute => attribute.Name).ToList();
			var requiredAttributes = standardAttributes.Where(definition => definition.Required != null && definition.Required.Value).Select(definition => definition.Name).ToList();
			requiredAttributes = new[] { entityDefinition?.PrimaryKeyInfo.Name }
				.Concat(notEmptyAttributes)
				.Concat(allAttributes.Where(attribute => attribute.NotNull || requiredAttributes.FirstOrDefault(name => name == attribute.Name) != null).Select(attribute => attribute.Name))
				.Concat(extendedAttributes.Where(definition => definition.Required != null && definition.Required.Value).Select(definition => definition.Name))
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.ToList();
			requiredAttributes.ForEach(name =>
			{
				var value = extendedAttributes.FirstOrDefault(attribute => attribute.Name == name) != null
					? @object.ExtendedProperties != null && @object.ExtendedProperties.ContainsKey(name) ? @object.ExtendedProperties[name] : null
					: @object.GetAttributeValue(name);
				if (value == null)
					throw new InformationRequiredException($"{name} is required");
				else if (value is string @string && string.IsNullOrWhiteSpace(@string) && notEmptyAttributes.IndexOf(name) > -1)
					throw new InformationRequiredException($"{name} is empty");
				onValidate?.Invoke(name, value);
			});
			return @object;
		}
	}
}
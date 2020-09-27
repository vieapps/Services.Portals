#region Related components
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using System.Xml.XPath;
using System.Xml.Xsl;
using System.Dynamic;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using System.Globalization;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using WampSharp.V2.Core.Contracts;
using net.vieapps.Components.Utility;
using net.vieapps.Components.Caching;
using net.vieapps.Components.Repository;
using net.vieapps.Services.Portals.Exceptions;
using net.vieapps.Components.Security;
#endregion

namespace net.vieapps.Services.Portals
{
	public static partial class Utility
	{
		/// <summary>
		/// Gets the pre-defined template
		/// </summary>
		/// <param name="filename"></param>
		/// <param name="theme"></param>
		/// <param name="mainDirectory"></param>
		/// <param name="subDirectory"></param>
		/// <returns></returns>
		public static async Task<string> GetTemplateAsync(string filename, string theme = null, string mainDirectory = null, string subDirectory = null, CancellationToken cancellationToken = default)
		{
			var filePath = Path.Combine(Utility.DataFilesDirectory, "themes", (theme ?? "default").Trim().ToLower(), "templates");
			if (!string.IsNullOrWhiteSpace(mainDirectory))
				filePath = Path.Combine(filePath, mainDirectory.Trim().ToLower());
			if (!string.IsNullOrWhiteSpace(subDirectory))
				filePath = Path.Combine(filePath, subDirectory.Trim().ToLower());
			filePath = Path.Combine(filePath, filename.Trim().ToLower());
			return File.Exists(filePath) ? await UtilityService.ReadTextFileAsync(filePath, null, cancellationToken).ConfigureAwait(false) : null;
		}

		/// <summary>
		/// Gets the XDocument of XML/XHTML
		/// </summary>
		/// <param name="xdoc"></param>
		public static XDocument GetXDocument(this string xdoc)
			=> XDocument.Parse(xdoc ?? "");

		/// <summary>
		/// Gets the defined zones from XHTML template
		/// </summary>
		/// <param name="xdocTemplate"></param>
		/// <returns></returns>
		public static IEnumerable<XElement> GetZones(this XDocument xdocTemplate)
			=> xdocTemplate?.XPathSelectElements("//*/*[@zone-id]");

		/// <summary>
		/// Gets the attribute that contains the identity of the zone (attriubte that named 'zone-id')
		/// </summary>
		/// <param name="zone"></param>
		/// <returns></returns>
		public static XAttribute GetZoneIDAttribute(this XElement zone)
			=> zone?.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName.IsEquals("zone-id"));

		/// <summary>
		/// Gets a zone by identity
		/// </summary>
		/// <param name="zones"></param>
		/// <param name="id"></param>
		/// <returns></returns>
		public static XElement GetZone(this IEnumerable<XElement> zones, string id)
			=> string.IsNullOrWhiteSpace(id)
				? null
				: zones?.FirstOrDefault(zone => id.IsEquals(zone.GetZoneIDAttribute()?.Value));

		/// <summary>
		/// Gets the names of the defined zones 
		/// </summary>
		/// <param name="zones"></param>
		/// <returns></returns>
		public static IEnumerable<string> GetZoneNames(this IEnumerable<XElement> zones)
			=> zones?.Select(zone => zone.GetZoneIDAttribute()).Select(attribute => attribute.Value);

		/// <summary>
		/// Gets the names of the defined zones from XHTML template
		/// </summary>
		/// <param name="xdocTemplate"></param>
		/// <returns></returns>
		public static IEnumerable<string> GetZoneNames(this XDocument xdocTemplate)
			=> xdocTemplate.GetZones().GetZoneNames();

		/// <summary>
		/// Validates the XHTML template
		/// </summary>
		/// <param name="xdocTemplate"></param>
		/// <param name="requiredZoneIDs"></param>
		public static void ValidateTemplate(this string xdocTemplate, IEnumerable<string> requiredZoneIDs = null)
		{
			if (!string.IsNullOrWhiteSpace(xdocTemplate))
				try
				{
					var template = xdocTemplate.GetXDocument();
					var zones = template.GetZones();

					if (zones.Count() < 1)
						throw new TemplateIsInvalidException("The template got no zone (means no any tag with 'zone-id' attribute)");

					var zoneIDs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
					zones.ForEach(zone =>
					{
						var zoneID = zone.GetZoneIDAttribute()?.Value;
						if (string.IsNullOrWhiteSpace(zoneID))
							throw new TemplateIsInvalidException($"The tag ({zone.Name.LocalName}) got 'zone-id' attribute but has no value");
						else if (zoneIDs.Contains(zoneID))
							throw new TemplateIsInvalidException($"The identity ({zoneID}) of the zone tag ({zone.Name.LocalName}) was used by another zone");
						zoneIDs.Add(zoneID);
					});

					(requiredZoneIDs ?? new[] { "Content" }).ForEach(zoneID =>
					{
						if (!zoneIDs.Contains(zoneID))
							throw new TemplateIsInvalidException($"The template is required a zone that identified as '{zoneID}' but not found");
					});
				}
				catch (Exception ex)
				{
					throw new TemplateIsInvalidException(ex);
				}
		}

		/// <summary>
		/// Validates the meta-tags/scripts
		/// </summary>
		/// <param name="xhtmlTemplate"></param>
		/// <param name="mustHaveAtLeastOneZone"></param>
		/// <param name="requiredZoneIDs"></param>
		public static void ValidateMetaTagsOrScripts(this string code, bool asScritps = false)
		{
			if (!string.IsNullOrWhiteSpace(code))
				try
				{
					$"<vieapps>{code}</vieapps>".GetXDocument();
				}
				catch (Exception ex)
				{
					throw asScritps ? new ScriptsAreInvalidException(ex) as AppException : new MetaTagsAreInvalidException(ex);
				}
		}

		/// <summary>
		/// Gets the compiled XSL stylesheet
		/// </summary>
		/// <param name="xslt">The string that presents the XSL stylesheet</param>
		/// <param name="enableDocumentFunctionAndInlineScript">true to enable document() function and inline script (like C#) of the XSL stylesheet</param>
		/// <param name="stylesheetResolver">Used to resolve any stylesheets referenced in XSLT import and include elements (if this is null, external resources are not resolved)</param>
		/// <returns></returns>
		public static XslCompiledTransform GetXslCompiledTransform(this string xslt, bool enableDocumentFunctionAndInlineScript = false, XmlResolver stylesheetResolver = null)
		{
			if (!string.IsNullOrWhiteSpace(xslt))
				try
				{
					using (var stream = UtilityService.CreateMemoryStream(xslt.ToBytes()))
					{
						using (var reader = XmlReader.Create(stream))
						{
							var xslTransform = new XslCompiledTransform(Utility.Logger.IsEnabled(LogLevel.Debug));
							xslTransform.Load(reader, enableDocumentFunctionAndInlineScript ? new XsltSettings(true, true) : null, stylesheetResolver ?? new XmlUrlResolver());
							return xslTransform;
						}
					}
				}
				catch (Exception ex)
				{
					if (ex.Message.IsContains("XSLT compile error"))
						throw new XslTemplateIsNotCompiledException(ex);
					else
						throw new XslTemplateIsInvalidException(ex);
				}
			return null;
		}

		/// <summary>
		/// Gets the compiled XSL stylesheet
		/// </summary>
		/// <param name="xslt">The XML document that presents the XSL stylesheet</param>
		/// <param name="enableDocumentFunctionAndInlineScript">true to enable document() function and inline script (like C#) of the XSL stylesheet</param>
		/// <param name="stylesheetResolver">Used to resolve any stylesheets referenced in XSLT import and include elements (if this is null, external resources are not resolved)</param>
		/// <returns></returns>
		public static XslCompiledTransform GetXslCompiledTransform(this XmlDocument xslt, bool enableDocumentFunctionAndInlineScript = false, XmlResolver stylesheetResolver = null)
			=> xslt?.ToString().GetXslCompiledTransform(enableDocumentFunctionAndInlineScript, stylesheetResolver);

		/// <summary>
		/// Gets the compiled XSL stylesheet
		/// </summary>
		/// <param name="xslt">The XML document that presents the XSL stylesheet</param>
		/// <param name="enableDocumentFunctionAndInlineScript">true to enable document() function and inline script (like C#) of the XSL stylesheet</param>
		/// <param name="stylesheetResolver">Used to resolve any stylesheets referenced in XSLT import and include elements (if this is null, external resources are not resolved)</param>
		/// <returns></returns>
		public static XslCompiledTransform GetXslCompiledTransform(this XDocument xslt, bool enableDocumentFunctionAndInlineScript = false, XmlResolver stylesheetResolver = null)
			=> xslt?.ToString().GetXslCompiledTransform(enableDocumentFunctionAndInlineScript, stylesheetResolver);

		/// <summary>
		/// Transforms this XML by the specified XSL stylesheet
		/// </summary>
		/// <param name="xml">The XML document to transform</param>
		/// <param name="xslt">The XSL stylesheet used to transform the XML document</param>
		/// <param name="xsltArguments">The arguments for transforming data</param>
		/// <param name="stylesheetResolver">Used to resolve any stylesheets referenced in XSLT import and include elements (if this is null, external resources are not resolved)</param>
		/// <returns></returns>
		public static string Transform(this XmlDocument xml, XslCompiledTransform xslt, XsltArgumentList xsltArguments = null, XmlResolver stylesheetResolver = null)
		{
			var results = "";
			if (xml != null && xslt != null)
				try
				{
					using (var stringWriter = new StringWriter())
					{
						using (var xmlWriter = XmlWriter.Create(stringWriter, xslt.OutputSettings))
						{
							xsltArguments = xsltArguments ?? new XsltArgumentList();
							xsltArguments.AddExtensionObject("urn:schemas-vieapps-net:xslt", new XslTransfromExtensions());
							xslt.Transform(xml, xsltArguments, xmlWriter, stylesheetResolver ?? new XmlUrlResolver());
						}
						results = stringWriter.ToString();
					}

					results = results.Replace(StringComparison.OrdinalIgnoreCase, "&#xD;", "").Replace(StringComparison.OrdinalIgnoreCase, "&#xA;", "");
					results = results.Replace(StringComparison.OrdinalIgnoreCase, "&#x9;", "").Replace(StringComparison.OrdinalIgnoreCase, "\t", "");
					results = results.Replace(StringComparison.OrdinalIgnoreCase, "<?xml version=\"1.0\" encoding=\"utf-16\"?>", "");
					results = results.Replace(StringComparison.OrdinalIgnoreCase, "<?xml version=\"1.0\" encoding=\"utf-8\"?>", "");

					var start = results.PositionOf("xmlns:");
					while (start > 0)
					{
						var end = results.PositionOf("\"", start);
						end = results.PositionOf("\"", end + 1);
						results = results.Remove(start, end - start + 1);
						start = results.PositionOf("xmlns:");
					}
				}
				catch (Exception ex)
				{
					if (ex.Message.IsContains("Execution of the 'document()'") || ex.Message.IsContains("Execution of scripts"))
						throw new XslTemplateExecutionIsProhibitedException(ex);
					else
						throw ex;
				}
			return results;
		}

		/// <summary>
		/// Transforms this XML by the specified XSL stylesheet
		/// </summary>
		/// <param name="xml">The XML document to transform</param>
		/// <param name="xslt">The XSL stylesheet used to transform the XML document</param>
		/// <param name="xsltArguments">The arguments for transforming data</param>
		/// <param name="stylesheetResolver">Used to resolve any stylesheets referenced in XSLT import and include elements (if this is null, external resources are not resolved)</param>
		/// <returns></returns>
		public static string Transform(this XDocument xml, XslCompiledTransform xslt, XsltArgumentList xsltArguments = null, XmlResolver stylesheetResolver = null)
			=> xml?.ToXmlDocument().Transform(xslt, xsltArguments, stylesheetResolver);

		/// <summary>
		/// Transforms this XML by the specified XSL stylesheet
		/// </summary>
		/// <param name="xml">The XML document to transform</param>
		/// <param name="xmlStylesheet">The XSL stylesheet used to transform the XML document</param>
		/// <param name="enableDocumentFunctionAndInlineScript">true to enable document() function and inline script (like C#) of the XSL stylesheet</param>
		/// <param name="xsltArguments">The arguments for transforming data</param>
		/// <param name="stylesheetResolver">Used to resolve any stylesheets referenced in XSLT import and include elements (if this is null, external resources are not resolved)</param>
		/// <returns></returns>
		public static string Transform(this XmlDocument xml, string xslt, bool enableDocumentFunctionAndInlineScript = false, XsltArgumentList xsltArguments = null, XmlResolver stylesheetResolver = null)
			=> xml?.Transform((xslt ?? "").GetXslCompiledTransform(enableDocumentFunctionAndInlineScript), xsltArguments, stylesheetResolver);

		/// <summary>
		/// Transforms this XML by the specified XSL stylesheet
		/// </summary>
		/// <param name="xml">The XML document to transform</param>
		/// <param name="xmlStylesheet">The XSL stylesheet used to transform the XML document</param>
		/// <param name="enableDocumentFunctionAndInlineScript">true to enable document() function and inline script (like C#) of the XSL stylesheet</param>
		/// <param name="xsltArguments">The arguments for transforming data</param>
		/// <param name="stylesheetResolver">Used to resolve any stylesheets referenced in XSLT import and include elements (if this is null, external resources are not resolved)</param>
		/// <returns></returns>
		public static string Transform(this XDocument xml, string xslt, bool enableDocumentFunctionAndInlineScript = false, XsltArgumentList xsltArguments = null, XmlResolver stylesheetResolver = null)
			=> xml?.ToXmlDocument().Transform(xslt, enableDocumentFunctionAndInlineScript, xsltArguments, stylesheetResolver);

		static Regex InvalidXmlCharacters { get; }  = new Regex("[\x00-\x08\x0B\x0C\x0E-\x1F]", RegexOptions.Compiled);

		/// <summary>
		/// Cleans invalid characters that not allowed in XML
		/// </summary>
		/// <param name="xml"></param>
		/// <returns></returns>
		public static XElement CleanInvalidCharacters(this XElement xml)
		{
			if (xml.HasElements)
				xml.Elements().ForEach(element => element.CleanInvalidCharacters());
			else
				xml.Value = Utility.InvalidXmlCharacters.Replace(xml.Value ?? "", string.Empty);
			return xml;
		}
	}

	//  --------------------------------------------------------------------------------------------

	public class XslTransfromExtensions
	{
		public XPathNodeIterator SelectNodeSet(XPathNodeIterator node, string xPath)
			=> node != null && node.Count == 1 && node.MoveNext() && node.Current != null
				? node.Current.Select(xPath)
				: null;

		public string ToString(string value, string format, string cultureCode)
			=> DateTime.TryParse(value, out var datetime)
				? datetime.ToString(format ?? "dd/MM/yyyy", CultureInfo.GetCultureInfo(cultureCode ?? "vi-VN"))
				: Decimal.TryParse(value, out var @decimal)
					? @decimal.ToString(format ?? "###,###,###,###,###,##0.###", CultureInfo.GetCultureInfo(cultureCode ?? "vi-VN"))
					: value;

		public string ToDateTimeQuarter(string value, string format, string cultureCode)
			=> DateTime.TryParse(value, out var datetime)
				? datetime.GetTimeQuarter().ToString(format ?? "dd/MM/yyyy hh:mm tt", CultureInfo.GetCultureInfo(cultureCode ?? "vi-VN"))
				: value;
	}
}
#region Related components
using System;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using System.Xml.XPath;
using System.Xml.Xsl;
using System.Dynamic;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.StaticFiles;
using Newtonsoft.Json.Linq;
using net.vieapps.Components.Utility;
using net.vieapps.Components.Caching;
using net.vieapps.Components.Repository;
using net.vieapps.Services.Portals.Exceptions;
#endregion

namespace net.vieapps.Services.Portals
{
	public static partial class Utility
	{
		/// <summary>
		/// Gets the cache storage
		/// </summary>
		public static Cache Cache { get; } = new Cache("VIEApps-Services-Portals", UtilityService.GetAppSetting("Cache:ExpirationTime", "30").CastAs<int>(), false, UtilityService.GetAppSetting("Cache:Provider"), Logger.GetLoggerFactory());

		/// <summary>
		/// Gets the collection of module definition
		/// </summary>
		public static ConcurrentDictionary<string, ModuleDefinition> ModuleDefinitions { get; } = new ConcurrentDictionary<string, ModuleDefinition>();

		/// <summary>
		/// Gets the collection of content-type definition
		/// </summary>
		public static ConcurrentDictionary<string, ContentTypeDefinition> ContentTypeDefinitions { get; } = new ConcurrentDictionary<string, ContentTypeDefinition>();

		/// <summary>
		/// Gets the collection of not recognized aliases
		/// </summary>
		public static HashSet<string> NotRecognizedAliases { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		/// <summary>
		/// Gets the URI of the public APIS
		/// </summary>
		public static string APIsHttpURI { get; internal set; }

		/// <summary>
		/// Gets the URI of the Files HTTP service
		/// </summary>
		public static string FilesHttpURI { get; internal set; }

		/// <summary>
		/// Gets the URI of the Portals HTTP service
		/// </summary>
		public static string PortalsHttpURI { get; internal set; }

		/// <summary>
		/// Gets the URI of the Passports HTTP service
		/// </summary>
		public static string PassportsHttpURI { get; internal set; }

		/// <summary>
		/// Gets the default site
		/// </summary>
		public static Site DefaultSite { get; internal set; }

		/// <summary>
		/// Gets the path to the directory that contains all data files of portals (css, images, scripts, templates)
		/// </summary>
		public static string DataFilesDirectory { get; internal set; }

		/// <summary>
		/// Normalizes an alias
		/// </summary>
		/// <param name="alias"></param>
		/// <param name="allowMinusSymbols"></param>
		/// <returns></returns>
		public static string NormalizeAlias(this string alias, bool allowMinusSymbols = true)
			=> allowMinusSymbols ? alias.GetANSIUri() : alias.GetANSIUri().Replace("-", "").Replace("_", "");

		/// <summary>
		/// Normalizes a domain name
		/// </summary>
		/// <param name="domain"></param>
		/// <returns></returns>
		public static string NormalizeDomain(this string domain)
			=> domain.ToArray(".", true).Select(name => name.Equals("*") || name.Equals("~") ? name : name.GetANSIUri(true, false, true)).Where(name => !string.IsNullOrWhiteSpace(name)).Join(".");

		/// <summary>
		/// Gets the parent content-type of this content-type
		/// </summary>
		/// <param name="contentType"></param>
		/// <returns></returns>
		public static ContentType GetParent(this ContentType contentType)
		{
			var parentDefinition = RepositoryMediator.GetEntityDefinition(contentType?.EntityDefinition?.ParentType);
			return parentDefinition != null
				? contentType?.Module?.ContentTypes?.FirstOrDefault(type => type.ContentTypeDefinitionID.Equals(parentDefinition.ID))
				: null;
		}

		/// <summary>
		/// Gets the object name for working with real-time update messages
		/// </summary>
		/// <param name="definition"></param>
		/// <returns></returns>
		public static string GetObjectName(this EntityDefinition definition)
			=> $"{(string.IsNullOrWhiteSpace(definition.ObjectNamePrefix) ? "" : definition.ObjectNamePrefix)}{definition.ObjectName}{(string.IsNullOrWhiteSpace(definition.ObjectNameSuffix) ? "" : definition.ObjectNameSuffix)}";

		/// <summary>
		/// Gets the object name for working with real-time update messages
		/// </summary>
		/// <typeparam name="T"></typeparam>
		/// <param name="object"></param>
		/// <returns></returns>
		public static string GetObjectName(this RepositoryBase @object)
			=> (@object != null ? RepositoryMediator.GetEntityDefinition(@object.GetType()) : null)?.GetObjectName() ?? @object?.GetType().GetTypeName(true);

		/// <summary>
		/// Gets the XDocument of XML/XHTML
		/// </summary>
		/// <param name="xhtml"></param>
		public static XDocument GetXDocument(this string xhtml)
			=> XDocument.Parse(xhtml ?? "");

		/// <summary>
		/// Gets the defined zones from XHTML template
		/// </summary>
		/// <param name="xhtmlTemplate"></param>
		/// <returns></returns>
		public static IEnumerable<XElement> GetZones(this XDocument xhtmlTemplate)
			=> xhtmlTemplate?.XPathSelectElements("//*/*[@zone-id]");

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
		/// <param name="xhtmlTemplate"></param>
		/// <returns></returns>
		public static IEnumerable<string> GetZoneNames(this XDocument xhtmlTemplate)
			=> xhtmlTemplate.GetZones().GetZoneNames();

		/// <summary>
		/// Validates the XHTML template
		/// </summary>
		/// <param name="xhtmlTemplate"></param>
		/// <param name="requiredZoneIDs"></param>
		public static void ValidateTemplate(this string xhtmlTemplate, IEnumerable<string> requiredZoneIDs = null)
		{
			if (!string.IsNullOrWhiteSpace(xhtmlTemplate))
				try
				{
					var template = xhtmlTemplate.GetXDocument();
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
							throw new TemplateIsInvalidException($"The template is required a zone that identitied as '{zoneID}' but not found");
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
		/// Gets the compiled XSLT
		/// </summary>
		/// <param name="xslt"></param>
		/// <param name="enableScript"></param>
		/// <returns></returns>
		public static XslCompiledTransform GetXslCompiledTransform(this string xslt, bool enableScript = true)
		{
			if (!string.IsNullOrWhiteSpace(xslt))
				try
				{
					var xml = xslt.GetXDocument();
					using (var stream = UtilityService.CreateMemoryStream(xml.ToString().ToBytes()))
					{
						using (var reader = new XmlTextReader(stream))
						{
							var xslTransform = new XslCompiledTransform();
							xslTransform.Load(reader, enableScript ? new XsltSettings { EnableScript = true } : null, null);
							return xslTransform;
						}
					}
				}
				catch (Exception ex)
				{
					throw new TemplateIsInvalidException($"The XSLT template is invalid => {ex.Message}", ex);
				}
			return null;
		}

		/// <summary>
		/// Transforms this XML by the specified stylesheet to XHTML/XML string
		/// </summary>
		/// <param name="xml"></param>
		/// <param name="xslt"></param>
		/// <returns></returns>
		public static string Transfrom(this XmlDocument xml, XslCompiledTransform xslt)
		{
			if (xslt != null)
				using (var writer = new StringWriter())
				{
					xslt.Transform(xml, null, writer);
					var results = writer.ToString().Replace(StringComparison.OrdinalIgnoreCase, "&#xD;", "").Replace(StringComparison.OrdinalIgnoreCase, "&#xA;", "").Replace(StringComparison.OrdinalIgnoreCase, "\t", "").Replace(StringComparison.OrdinalIgnoreCase, "&#x9;", "").Replace(StringComparison.OrdinalIgnoreCase, "<?xml version=\"1.0\" encoding=\"utf-16\"?>", "");
					var start = results.PositionOf("xmlns:");
					while (start > 0)
					{
						var end = results.PositionOf("\"", start);
						end = results.PositionOf("\"", end + 1);
						results = results.Remove(start, end - start + 1);
						start = results.PositionOf("xmlns:");
					}
					return results;
				}
			return "";
		}

		/// <summary>
		/// Transforms this XML by the specified stylesheet to XHTML/XML string
		/// </summary>
		/// <param name="xml"></param>
		/// <param name="xmlStylesheet">The stylesheet for transfroming</param>
		/// <param name="enableScript">true to enable inline script (like C#) while processing</param>
		/// <returns></returns>
		public static string Transfrom(this XmlDocument xml, string xslt, bool enableScript = true)
			=> xml.Transfrom((xslt ?? "").GetXslCompiledTransform(enableScript));

		/// <summary>
		/// Transforms this XML by the specified stylesheet to XHTML/XML string
		/// </summary>
		/// <param name="xml"></param>
		/// <param name="xslt"></param>
		/// <returns></returns>
		public static string Transfrom(this XDocument xml, XslCompiledTransform xslt)
			=> xml.ToXmlDocument().Transfrom(xslt);

		/// <summary>
		/// Transforms this XML by the specified stylesheet to XHTML/XML string
		/// </summary>
		/// <param name="xml"></param>
		/// <param name="xmlStylesheet">The stylesheet for transfroming</param>
		/// <param name="enableScript">true to enable inline script (like C#) while processing</param>
		/// <returns></returns>
		public static string Transfrom(this XDocument xml, string xslt, bool enableScript = true)
			=> xml.ToXmlDocument().Transfrom(xslt, enableScript);

		/// <summary>
		/// Gets the pre-defined template
		/// </summary>
		/// <param name="filename"></param>
		/// <param name="mainDirectory"></param>
		/// <param name="subDirectory"></param>
		/// <param name="theme"></param>
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

		static FileExtensionContentTypeProvider MimeTypeProvider { get; } = new FileExtensionContentTypeProvider();

		/// <summary>
		/// Gets the MIME type of a file
		/// </summary>
		/// <param name="filename"></param>
		/// <returns></returns>
		public static string GetMimeType(this string filename)
			=> Utility.MimeTypeProvider.TryGetContentType(filename, out var mimeType)
				? mimeType
				: "application/octet-stream";

		/// <summary>
		/// Gets the MIME type of a file
		/// </summary>
		/// <param name="fileInfo"></param>
		/// <returns></returns>
		public static string GetMimeType(this FileInfo fileInfo)
			=> fileInfo?.Name?.GetMimeType();

		/// <summary>
		/// Generates the pagination
		/// </summary>
		/// <param name="totalRecords"></param>
		/// <param name="totalPages"></param>
		/// <param name="pageSize"></param>
		/// <param name="pageNumber"></param>
		/// <param name="urlPattern"></param>
		/// <returns></returns>
		public static JObject GeneratePagination(long totalRecords, int totalPages, int pageSize, int pageNumber, string urlPattern)
		{
			var pages = new List<JObject>(totalPages);

			if (totalPages > 1)
				for (var page = 1; page <= totalPages; page++)
					pages.Add(new JObject
					{
						{ "Text", $"{page}" },
						{ "URL", urlPattern.Replace("{{pageNumber}}", $"{page}") }
					});
			else
				pages = null;

			return new JObject
			{
				{ "TotalRecords", totalRecords },
				{ "TotalPages", totalPages },
				{ "PageSize", pageSize },
				{ "PageNumber", pageNumber },
				{ "URLPattern", urlPattern },
				{ "Pages", pages?.ToJArray() }
			};
		}
	}

	//  --------------------------------------------------------------------------------------------

	[Serializable]
	[Repository(ServiceName = "Portals", ID = "A0000000000000000000000000000001", Title = "CMS", Description = "Services of the Portals CMS module", Directory = "CMS", ExtendedPropertiesTableName = "T_Portals_Extended_Properties")]
	public abstract class Repository<T> : RepositoryBase<T> where T : class { }
}
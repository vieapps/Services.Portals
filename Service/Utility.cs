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
		/// Gets the cache storage
		/// </summary>
		public static Cache Cache { get; } = new Cache("VIEApps-Services-Portals", UtilityService.GetAppSetting("Cache:ExpirationTime", "30").CastAs<int>(), false, UtilityService.GetAppSetting("Cache:Provider"), Components.Utility.Logger.GetLoggerFactory());

		/// <summary>
		/// Gets the real-time updater (RTU) service
		/// </summary>
		public static IRTUService RTUService { get; internal set; }

		/// <summary>
		/// Gets the messaging service
		/// </summary>
		public static IMessagingService MessagingService { get; internal set; }

		/// <summary>
		/// Gets the logging service
		/// </summary>
		public static ILoggingService LoggingService { get; internal set; }

		/// <summary>
		/// Gets the local logger
		/// </summary>
		public static ILogger Logger { get; internal set; }

		/// <summary>
		/// Gets the key for encrypting/decrypting data with AES
		/// </summary>
		public static string EncryptionKey { get; internal set; }

		/// <summary>
		/// Gets the key for validating data
		/// </summary>
		public static string ValidationKey { get; internal set; }

		/// <summary>
		/// Gets the key for sending notifications
		/// </summary>
		public static string NotificationsKey { get; internal set; }

		/// <summary>
		/// Gets the identity of the current node
		/// </summary>
		public static string NodeID => ServiceBase.ServiceComponent.NodeID;

		/// <summary>
		/// Gets the collection of module definition
		/// </summary>
		public static ConcurrentDictionary<string, ModuleDefinition> ModuleDefinitions { get; } = new ConcurrentDictionary<string, ModuleDefinition>();

		/// <summary>
		/// Gets the collection of content-type definition
		/// </summary>
		public static ConcurrentDictionary<string, ContentTypeDefinition> ContentTypeDefinitions { get; } = new ConcurrentDictionary<string, ContentTypeDefinition>();

		/// <summary>
		/// Gets the collection of content-type definition
		/// </summary>
		internal static ConcurrentDictionary<string, Cache> DesktopHtmlCaches { get; } = new ConcurrentDictionary<string, Cache>();

		/// <summary>
		/// Gets the collection of not recognized aliases
		/// </summary>
		public static HashSet<string> NotRecognizedAliases { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		/// <summary>
		/// Gets the collection of OEmbed providers
		/// </summary>
		public static List<Tuple<string, List<Regex>, Tuple<Regex, int, string>>> OEmbedProviders { get; } = new List<Tuple<string, List<Regex>, Tuple<Regex, int, string>>>();

		/// <summary>
		/// Gets the URI of the public APIS
		/// </summary>
		public static string APIsHttpURI { get; internal set; }

		/// <summary>
		/// Gets the URI of the Files HTTP service
		/// </summary>
		public static string FilesHttpURI { get; internal set; }

		/// <summary>
		/// Gets the URI of the Passports HTTP service
		/// </summary>
		public static string PassportsHttpURI { get; internal set; }

		/// <summary>
		/// Gets the URI of the Portals HTTP service
		/// </summary>
		public static string PortalsHttpURI { get; internal set; }

		/// <summary>
		/// Gets the URI of the CMS Portals app
		/// </summary>
		public static string CmsPortalsHttpURI { get; internal set; }

		/// <summary>
		/// Gets the default site
		/// </summary>
		public static Site DefaultSite { get; internal set; }

		/// <summary>
		/// Gets the path to the directory that contains all data files of portals (css, images, scripts, templates)
		/// </summary>
		public static string DataFilesDirectory { get; internal set; }

		/// <summary>
		/// Gets the collection of language resources (i18n)
		/// </summary>
		public static Dictionary<string, ExpandoObject> Languages { get; } = new Dictionary<string, ExpandoObject>();

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
			=> (@object != null ? RepositoryMediator.GetEntityDefinition(@object.GetType()) : null)?.GetObjectName() ?? @object?.GetTypeName(true);

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
		/// Cleans invalid XML characters
		/// </summary>
		/// <param name="string"></param>
		/// <returns></returns>
		public static string CleanInvalidXmlCharacters(this string @string)
			=> Regex.Replace(@string, "[\x00-\x08\x0B\x0C\x0E-\x1F]", string.Empty, RegexOptions.Compiled);

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
#if DEBUG
							var xslTransform = new XslCompiledTransform(true);
#else
							var xslTransform = new XslCompiledTransform();
#endif
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
		/// Normalizes a pagination URL
		/// </summary>
		/// <param name="url"></param>
		/// <returns></returns>
		public static string NormalizePaginationURL(this string url)
		{
			url = url.Replace("/1.html", ".html");
			return url.EndsWith("/1") ? url.Left(url.Length - 2) : url;
		}

		/// <summary>
		/// Generates the pagination
		/// </summary>
		/// <param name="totalRecords"></param>
		/// <param name="totalPages"></param>
		/// <param name="pageSize"></param>
		/// <param name="pageNumber"></param>
		/// <param name="urlPattern"></param>
		/// <returns></returns>
		public static JObject GeneratePagination(long totalRecords, int totalPages, int pageSize, int pageNumber, string urlPattern, bool showPageLinks = true, int numberOfPageLinks = 0)
		{
			var pages = new List<JObject>(totalPages);
			if (totalPages > 1 && !string.IsNullOrWhiteSpace(urlPattern))
			{
				if (!showPageLinks || numberOfPageLinks < 1 || totalPages <= numberOfPageLinks)
					for (var page = 1; page <= totalPages; page++)
						pages.Add(new JObject
						{
							{ "Text", $"{page}" },
							{ "URL", urlPattern.Replace(StringComparison.OrdinalIgnoreCase, "{{pageNumber}}", $"{page}").NormalizePaginationURL() }
						});
				else
				{
					var numberOfLinks = (numberOfPageLinks - 4) / 2;
					if (numberOfLinks < 1)
						numberOfLinks = 1;
					var start = pageNumber - numberOfLinks;
					var end = pageNumber + numberOfLinks;
					while (start < 2)
					{
						start++;
						end++;
					}
					if (end >= totalPages - 1)
					{
						end = totalPages - 1;
						while (end - start < numberOfLinks)
							start--;
						if (start < 2)
							start = 2;
					}
					pages.Add(new JObject
					{
						{ "Text", "1" },
						{ "URL", urlPattern.Replace(StringComparison.OrdinalIgnoreCase, "{{pageNumber}}", "1").NormalizePaginationURL() }
					});
					if (start - 1 > 1)
						pages.Add(new JObject
						{
							{ "Text", start - 1 > 2 ? "..." : $"{start - 1}" },
							{ "URL", urlPattern.Replace(StringComparison.OrdinalIgnoreCase, "{{pageNumber}}", $"{start - 1}") }
						});
					for (var page = start; page <= end; page++)
						pages.Add(new JObject
						{
							{ "Text", $"{page}" },
							{ "URL", urlPattern.Replace(StringComparison.OrdinalIgnoreCase, "{{pageNumber}}", $"{page}") }
						});
					if (end + 1 < totalPages)
						pages.Add(new JObject
						{
							{ "Text", end + 1 < totalPages ? "..." : $"{end + 1}" },
							{ "URL", urlPattern.Replace(StringComparison.OrdinalIgnoreCase, "{{pageNumber}}", $"{end + 1}") }
						});
					pages.Add(new JObject
					{
						{ "Text", $"{totalPages}" },
						{ "URL", urlPattern.Replace(StringComparison.OrdinalIgnoreCase, "{{pageNumber}}", $"{totalPages}") }
					});
				}
			}
			else
				pages = null;

			return new JObject
			{
				{ "TotalRecords", totalRecords },
				{ "TotalPages", totalPages },
				{ "PageSize", pageSize },
				{ "PageNumber", pageNumber },
				{ "URLPattern", urlPattern },
				{ "Pages", pages == null ? null : new JObject { { "Page", pages.ToJArray() } } }
			};
		}

		internal static JArray GetThumbnails(this JToken thumbnails, string objectID)
			=> thumbnails != null
				? thumbnails is JArray thumbnailsAsJArray
					? thumbnailsAsJArray
					: (thumbnails[$"@{@objectID}"] ?? thumbnails[objectID]) as JArray
				: null;

		internal static string GetThumbnailURL(this JToken thumbnails, string objectID)
			=> thumbnails?.GetThumbnails(objectID)?.FirstOrDefault()?.Get<JObject>("URIs")?.Get<string>("Direct");

		/// <summary>
		/// Normalizes the HTML contents
		/// </summary>
		/// <param name="html"></param>
		/// <returns></returns>
		public static string NormalizeHTML(this string html)
		{
			if (string.IsNullOrWhiteSpace(html))
				return null;

			// paragraphs of CKEditor5
			html = html.Replace("<p style=\"margin-left:0px;\"", "<p");

			// normalize all 'oembed' tags
			var start = html.PositionOf("<oembed");
			while (start > -1)
			{
				var end = start < 0 ? -1 : html.PositionOf("</oembed>", start);
				if (end > -1)
				{
					end += 9;
					var media = html.Substring(start, 9 + end - start);
					var urlStart = media.IndexOf("url=") + 5;
					var urlEnd = media.IndexOf("\"", urlStart + 1);
					var url = media.Substring(urlStart, urlEnd - urlStart);
					var oembedProvider = Utility.OEmbedProviders.FirstOrDefault(provider => provider.Item2.Any(regex => regex.Match(url).Success));
					if (oembedProvider != null)
					{
						var regex = oembedProvider.Item3.Item1;
						var position = oembedProvider.Item3.Item2;
						var xhtml = oembedProvider.Item3.Item3;
						var match = regex.Match(url);
						media = xhtml.Format(new Dictionary<string, object> { ["id"] = match.Success && match.Length > position ? match.Groups[position].Value : null });
					}
					else
					{
						var tag = url.IsEndsWith(".mp3") ? "audio" : "video";
						var height = url.IsEndsWith(".mp3") ? "32" : "315";
						media = ($"<{tag} width=\"560\" height=\"{height}\" controls autoplay muted>" + "<source src=\"{{url}}\"/>" + $"</{tag}>").Format(new Dictionary<string, object> { ["url"] = url });
					}
					html = html.Substring(0, start) + media + html.Substring(end);
				}
				start = html.PositionOf("<oembed", start + 1);
			}

			return html.HtmlDecode();
		}

		/// <summary>
		/// Normalized HTMLs (means CLOB and LargeText attributes) of this object
		/// </summary>
		/// <param name="object"></param>
		/// <param name="onCompleted"></param>
		/// <returns></returns>
		public static IBusinessObject NormalizeHTMLs(this IBusinessObject @object, Action<IBusinessObject> onCompleted = null)
		{
			// get entity definition
			var definition = RepositoryMediator.GetEntityDefinition(@object?.GetType());

			// normalize
			if (definition != null)
			{
				// standard properties
				definition.Attributes.Where(attribute => attribute.IsCLOB != null && attribute.IsCLOB.Value).ForEach(attribute =>
				{
					var value = @object.GetAttributeValue<string>(attribute.Name);
					if (!string.IsNullOrWhiteSpace(value))
						@object.SetAttributeValue(attribute.Name, value.HtmlDecode());
				});

				// extended properties
				if (@object.ExtendedProperties != null && definition.BusinessRepositoryEntities.TryGetValue(@object.RepositoryEntityID, out var repositiryEntity))
					repositiryEntity?.ExtendedPropertyDefinitions?.Where(propertyDefinition => propertyDefinition.Mode.Equals(ExtendedPropertyMode.LargeText)).ForEach(propertyDefinition =>
					{
						if (@object.ExtendedProperties.TryGetValue(propertyDefinition.Name, out var value) && value is string @string && !string.IsNullOrWhiteSpace(@string))
							@object.ExtendedProperties[propertyDefinition.Name] = @string.HtmlDecode();
					});
			}

			// return the object
			onCompleted?.Invoke(@object);
			return @object;
		}

		/// <summary>
		/// Normalized HTMLs (means CLOB and LargeText attributes) of this xml
		/// </summary>
		/// <param name="xml"></param>
		/// <param name="object"></param>
		/// <param name="onCompleted"></param>
		/// <returns></returns>
		public static XElement NormalizeHTMLs(this XElement xml, IBusinessObject @object, Action<XElement> onCompleted = null)
		{
			// get entity definition
			var definition = RepositoryMediator.GetEntityDefinition(@object?.GetType());

			// normalize
			if (definition != null)
			{
				// standard properties
				definition.Attributes.Where(attribute => attribute.IsCLOB != null && attribute.IsCLOB.Value).ForEach(attribute =>
				{
					var elment = xml.Element(attribute.Name);
					if (elment != null && !string.IsNullOrWhiteSpace(elment.Value))
						elment.Value = elment.Value.NormalizeHTML();
				});

				// extended properties
				if (@object.ExtendedProperties != null && definition.BusinessRepositoryEntities.TryGetValue(@object.RepositoryEntityID, out var repositiryEntity))
					repositiryEntity?.ExtendedPropertyDefinitions?.Where(propertyDefinition => propertyDefinition.Mode.Equals(ExtendedPropertyMode.LargeText)).ForEach(propertyDefinition =>
					{
						var elment = xml.Element(propertyDefinition.Name);
						if (elment != null && !string.IsNullOrWhiteSpace(elment.Value))
							elment.Value = elment.Value.NormalizeHTML();
					});
			}

			// return the xml
			onCompleted?.Invoke(xml);
			return xml;
		}

		/// <summary>
		/// Normalizes breaks (BR) of HTML code
		/// </summary>
		/// <param name="html"></param>
		/// <param name="noBreakBetweenTags"></param>
		/// <returns></returns>
		public static string NormalizeHTMLBreaks(this string html, bool noBreakBetweenTags = true)
		{
			html = html.Replace("\t", "").Replace("\r", "").Replace("\n", "<br/>");
			return noBreakBetweenTags ? html.Replace("><br/><", "><") : html;
		}

		/// <summary>
		/// Gets the state that determines the URI is belong to Portals HTTP service or not
		/// </summary>
		/// <param name="baseURI"></param>
		/// <returns></returns>
		public static bool IsAPIsHttpURI(this Uri baseURI)
			=> baseURI != null && (baseURI.AbsoluteUri.IsStartsWith(Utility.APIsHttpURI) || baseURI.AbsoluteUri.Replace("http://", "https://").IsStartsWith(Utility.APIsHttpURI) || baseURI.AbsoluteUri.Replace("https://", "http://").IsStartsWith(Utility.APIsHttpURI));

		/// <summary>
		/// Gets the state that determines the URI is belong to Portals HTTP service or not
		/// </summary>
		/// <param name="baseURI"></param>
		/// <returns></returns>
		public static bool IsPortalsHttpURI(this Uri baseURI)
			=> baseURI != null && (baseURI.AbsoluteUri.IsStartsWith(Utility.PortalsHttpURI) || baseURI.AbsoluteUri.Replace("http://", "https://").IsStartsWith(Utility.PortalsHttpURI) || baseURI.AbsoluteUri.Replace("https://", "http://").IsStartsWith(Utility.PortalsHttpURI));

		/// <summary>
		/// Gets the root URL for working with an organizations' resources
		/// </summary>
		/// <param name="baseURI"></param>
		/// <param name="systemIdentity"></param>
		/// <param name="useShortURLs"></param>
		/// <returns></returns>
		public static string GetRootURL(this Uri baseURI, string systemIdentity, bool useShortURLs = false)
			=> useShortURLs
				? baseURI.IsPortalsHttpURI() ? "./" : "/"
				: baseURI.IsPortalsHttpURI() ? $"{Utility.PortalsHttpURI}/~{systemIdentity}/" : $"{baseURI.Scheme}://{baseURI.Host}/";

		/// <summary>
		/// Normalizes all URLs of a HTML content
		/// </summary>
		/// <param name="html"></param>
		/// <param name="rootURL"></param>
		/// <param name="forDisplaying"></param>
		/// <returns></returns>
		public static string NormalizeURLs(this string html, string rootURL, bool forDisplaying = true)
			=> forDisplaying
				? html?.Replace("~~~/", rootURL).Replace("~~/", $"{Utility.FilesHttpURI}/").Replace("~/", rootURL)
				: html?.Replace(StringComparison.OrdinalIgnoreCase, $"{Utility.FilesHttpURI}/", "~~/").Replace(StringComparison.OrdinalIgnoreCase, rootURL, "~/");

		/// <summary>
		/// Normalizes all URLs of a HTML content
		/// </summary>
		/// <param name="html"></param>
		/// <param name="requestURI"></param>
		/// <param name="systemIdentity"></param>
		/// <param name="useShortURLs"></param>
		/// <param name="forDisplaying"></param>
		/// <returns></returns>
		public static string NormalizeURLs(this string html, Uri requestURI, string systemIdentity, bool useShortURLs = true, bool forDisplaying = true)
		{
			if (string.IsNullOrWhiteSpace(html))
				return html;

			html = forDisplaying
				? html.Replace("~/_", Utility.PortalsHttpURI + "/_")
				: html.Replace(Utility.PortalsHttpURI + "/_", "~/_");

			html = html.NormalizeURLs(forDisplaying ? requestURI.GetRootURL(systemIdentity, useShortURLs) : requestURI.GetRootURL(systemIdentity), forDisplaying);

			if (forDisplaying && useShortURLs && requestURI.IsPortalsHttpURI())
				html = html.Insert(html.PositionOf(">", html.PositionOf("<head")) + 1, $"<base href=\"{Utility.PortalsHttpURI}/~{systemIdentity}/\"/>");

			return html;
		}

		/// <summary>
		/// Normalizes all URLs of a HTML content
		/// </summary>
		/// <param name="organization"></param>
		/// <param name="html"></param>
		/// <param name="forDisplaying"></param>
		/// <returns></returns>
		public static string NormalizeURLs(this Organization organization, string html, bool forDisplaying = true)
		{
			if (string.IsNullOrWhiteSpace(html) || organization == null)
				return html;

			if (forDisplaying)
				return html.Replace("~/_", Utility.PortalsHttpURI + "/_").NormalizeURLs(new Uri(Utility.PortalsHttpURI).GetRootURL(organization.Alias, false));

			html = html.Replace(Utility.PortalsHttpURI + "/_", "~/_");

			var domains = new List<string>();
			organization.Sites.ForEach(site =>
			{
				domains.Add($"{site.SubDomain}.{site.PrimaryDomain}".Replace("*.", "www.").Replace("www.www.", "www."));
				domains.Add($"{site.SubDomain}.{site.PrimaryDomain}".Replace("*.", ""));
				site.OtherDomains?.ToList(";").ForEach(domain =>
				{
					domains.Add(domain);
					if (domain.IsStartsWith("www."))
						domains.Add(domain.Replace("www.", ""));
				});
			});

			new[] { new Uri(Utility.PortalsHttpURI).GetRootURL(organization.Alias, false) }
				.Concat(domains.Select(domain => $"http://{domain}/"))
				.Concat(domains.Select(domain => $"https://{domain}/"))
				.ForEach(rootURL => html = html.NormalizeURLs(rootURL, false));

			return html;
		}

		internal static Cache GetCacheOfDesktopHTML(this Organization organization)
		{
			if (!Utility.DesktopHtmlCaches.TryGetValue(organization.ID, out var cache))
			{
				cache = new Cache($"VIEApps-Services-Portals-Desktops-{organization.ID}", organization.RefreshUrls != null && organization.RefreshUrls.Interval > 0 ? organization.RefreshUrls.Interval - 2 : Utility.Cache.ExpirationTime / 2, true, Components.Utility.Logger.GetLoggerFactory());
				Utility.DesktopHtmlCaches[organization.ID] = cache;
			}
			return cache;
		}

		/// <summary>
		/// Gets the cache key prefix for working with collection of objects
		/// </summary>
		/// <param name="requestJson"></param>
		/// <returns></returns>
		public static string GetCacheKeyPrefix(this JToken requestJson)
		{
			var expressionID = requestJson?.Get<JObject>("Expression")?.Get<string>("ID");
			if (!string.IsNullOrWhiteSpace(expressionID))
			{
				var parentIdentity = requestJson.Get("IsAutoPageNumber", false) ? requestJson.Get<string>("ParentIdentity") : null;
				return $"#exp:{expressionID}{(string.IsNullOrWhiteSpace(parentIdentity) ? "" : $"~{parentIdentity}")}";
			}
			return null;
		}

		/// <summary>
		/// Gets the cache key prefix for working with collection of objects
		/// </summary>
		/// <param name="expression"></param>
		/// <param name="parentIdentity"></param>
		/// <returns></returns>
		public static string GetCacheKeyPrefix(this Expression expression, string parentIdentity = null)
			=> $"#exp:{expression.ID}{(string.IsNullOrWhiteSpace(parentIdentity) ? "" : $"~{parentIdentity.Trim().ToLower()}")}";

		/// <summary>
		/// Sets cache of page-size (to clear related cached further)
		/// </summary>
		/// <typeparam name="T"></typeparam>
		/// <param name="filter"></param>
		/// <param name="sort"></param>
		/// <param name="cacheKeyPrefix"></param>
		/// <param name="pageSize"></param>
		/// <param name="cancellationToken"></param>
		/// <returns></returns>
		public static async Task<string> SetCacheOfPageSizeAsync<T>(IFilterBy<T> filter, SortBy<T> sort, string cacheKeyPrefix, int pageSize, CancellationToken cancellationToken = default) where T : class
		{
			var cacheKey = $"{(string.IsNullOrWhiteSpace(cacheKeyPrefix) ? Extensions.GetCacheKey(filter, sort) : Extensions.GetCacheKey<T>(cacheKeyPrefix))}:size";
			await Utility.Cache.SetAsync(cacheKey, pageSize, cancellationToken).ConfigureAwait(false);
			return cacheKey;
		}

		/// <summary>
		/// Sends the request to clear related cached of collecion of objects of a content type
		/// </summary>
		/// <param name="rtuService"></param>
		/// <param name="contentTypeID"></param>
		/// <param name="keyPrefix"></param>
		/// <param name="parentIdentities"></param>
		/// <param name="cancellationToken"></param>
		/// <returns></returns>
		public static Task SendClearCacheRequestAsync(this IRTUService rtuService, string contentTypeID, string keyPrefix, IEnumerable<string> parentIdentities = null, CancellationToken cancellationToken = default)
			=> rtuService == null || string.IsNullOrWhiteSpace(contentTypeID) || string.IsNullOrWhiteSpace(keyPrefix)
				? Task.CompletedTask
				: rtuService.SendInterCommunicateMessageAsync(new CommunicateMessage("CMS.Portals")
				{
					Type = "Cache#Clear",
					Data = new JObject
					{
						{ "ContentTypeID", contentTypeID },
						{ "KeyPrefix", keyPrefix },
						{ "ParentIdentities", parentIdentities?.Where(parentIdentity => !string.IsNullOrWhiteSpace(parentIdentity)).Distinct(StringComparer.OrdinalIgnoreCase).ToJArray() }
					}
				}, cancellationToken);

		/// <summary>
		/// Sends the request to clear related cached of collecion of objects of a content type
		/// </summary>
		/// <param name="rtuService"></param>
		/// <param name="contentTypeID"></param>
		/// <param name="keyPrefix"></param>
		/// <param name="cancellationToken"></param>
		/// <returns></returns>
		public static Task SendClearCacheRequestAsync(this IRTUService rtuService, string contentTypeID, string keyPrefix, CancellationToken cancellationToken = default)
			=> rtuService.SendClearCacheRequestAsync(contentTypeID, keyPrefix, null, cancellationToken);

		/// <summary>
		/// Clears the related cached of collecion of objects of a content type
		/// </summary>
		/// <param name="contentType"></param>
		/// <param name="cacheKeyPrefix"></param>
		/// <param name="parentIdentities"></param>
		/// <param name="cancellationToken"></param>
		/// <returns></returns>
		public static async Task ClearCacheAsync(this CommunicateMessage message, CancellationToken cancellationToken = default)
		{
			var contentType = await message.Data.Get("ContentTypeID", "").GetContentTypeByIDAsync(cancellationToken).ConfigureAwait(false);
			var keyPrefix = message.Data.Get<string>("KeyPrefix");
			if (contentType == null || string.IsNullOrWhiteSpace(keyPrefix))
				return;

			var parentIdentities = message.Data.Get<JArray>("ParentIdentities")?.Select(identity => identity as JValue).Where(identity => identity != null && identity.Value != null).Select(identity => identity.Value.ToString()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
			var expressions = ExpressionProcessor.Expressions.Values.Where(expression => contentType.ID.IsEquals(expression.RepositoryEntityID)).ToList();

			await expressions.ForEachAsync(async (expression, ctoken1) =>
			{
				var simpleKey = $"{keyPrefix}{expression.GetCacheKeyPrefix()}";
				var simplePageSize = await Utility.Cache.ExistsAsync($"{simpleKey}:size", cancellationToken).ConfigureAwait(false)
					? await Utility.Cache.GetAsync<int>($"{simpleKey}:size", cancellationToken).ConfigureAwait(false)
					: 7;
				var simpleKeys = Extensions.GetRelatedCacheKeys(simpleKey, simplePageSize);
				await Task.WhenAll
				(
					Utility.Cache.RemoveAsync(simpleKeys, null, cancellationToken),
					parentIdentities == null ? Task.CompletedTask : parentIdentities.ForEachAsync(async (parentIdentity, ctoken2) =>
					{
						var complexKey = $"{keyPrefix}{expression.GetCacheKeyPrefix(parentIdentity)}";
						var complexPageSize = await Utility.Cache.ExistsAsync($"{complexKey}:size", cancellationToken).ConfigureAwait(false)
							? await Utility.Cache.GetAsync<int>($"{complexKey}:size", cancellationToken).ConfigureAwait(false)
							: 7;
						var complexKeys = Extensions.GetRelatedCacheKeys(complexKey, complexPageSize);
						await Utility.Cache.RemoveAsync(complexKeys, null, cancellationToken).ConfigureAwait(false);
					}, cancellationToken)
				).ConfigureAwait(false);
			}, cancellationToken).ConfigureAwait(false);
		}

		/// <summary>
		/// Gets the time with quater
		/// </summary>
		/// <param name="time"></param>
		/// <returns></returns>
		public static DateTime GetTimeQuarter(this DateTime time)
			=> DateTime.Parse($"{time:yyyy/MM/dd HH:}{(time.Minute > 44 ? "45" : time.Minute > 29 ? "30" : time.Minute > 24 ? "15" : "00")}:00");

		/// <summary>
		/// Runs this task and forget its
		/// </summary>
		/// <param name="task"></param>
		/// <param name="logger"></param>
		internal static void Run(this Task task, ILogger logger = null)
			=> Task.Run(async () =>
			{
				try
				{
					await task.ConfigureAwait(false);
				}
				catch (Exception ex)
				{
					logger = logger ?? Utility.Logger;
					if (logger != null)
						logger.LogError($"Error occurred while running a forgetable task => {ex.Message}", ex);
				}
			}).ConfigureAwait(false);

		/// <summary>
		/// Generate a form control of an extended property
		/// </summary>
		/// <param name="definition"></param>
		/// <param name="mode"></param>
		/// <returns></returns>
		public static JObject GenerateFormControl(this ExtendedControlDefinition definition, ExtendedPropertyMode mode)
		{
			var controlType = mode.Equals(ExtendedPropertyMode.LargeText) || (definition.AsTextEditor != null && definition.AsTextEditor.Value)
				? "TextEditor"
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

				if (definition.Disabled != null && definition.Disabled.Value)
					options["Disabled"] = true;

				if (definition.ReadOnly != null && definition.ReadOnly.Value)
					options["ReadOnly"] = true;

				if (definition.AutoFocus != null && definition.AutoFocus.Value)
					options["AutoFocus"] = true;

				if (!string.IsNullOrWhiteSpace(definition.ValidatePattern))
					options["ValidatePattern"] = definition.ValidatePattern;

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
					var contentType = string.IsNullOrWhiteSpace(definition.LookupRepositoryEntityID) ? null : definition.LookupRepositoryEntityID.GetContentTypeByID();
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
										{ "objectName", contentType?.GetObjectName() },
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

		/// <summary>
		/// Gets the identities of users (for working with notifications)
		/// </summary>
		/// <param name="privileges"></param>
		/// <param name="privilegeRole"></param>
		/// <param name="cancellationToken"></param>
		/// <returns></returns>
		public static async Task<List<string>> GetUserIDsAsync(this Privileges privileges, PrivilegeRole privilegeRole, CancellationToken cancellationToken = default)
		{
			if (privileges == null)
				return new List<string>();

			List<Role> roles;
			switch (privilegeRole)
			{
				case PrivilegeRole.Administrator:
					roles = privileges.AdministrativeRoles != null && privileges.AdministrativeRoles.Count > 0
						? await Role.FindAsync(Filters<Role>.Or(privileges.AdministrativeRoles.Select(roleID => Filters<Role>.Equals("ID", roleID))), null, 0, 1, null, cancellationToken).ConfigureAwait(false)
						: new List<Role>();
					return roles.SelectMany(role => role.UserIDs ?? new List<string>()).Concat(privileges.AdministrativeUsers ?? new HashSet<string>()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

				case PrivilegeRole.Moderator:
					roles = privileges.ModerateRoles != null && privileges.ModerateRoles.Count > 0
						? await Role.FindAsync(Filters<Role>.Or(privileges.ModerateRoles.Select(roleID => Filters<Role>.Equals("ID", roleID))), null, 0, 1, null, cancellationToken).ConfigureAwait(false)
						: new List<Role>();
					return roles.SelectMany(role => role.UserIDs ?? new List<string>()).Concat(privileges.ModerateUsers ?? new HashSet<string>()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

				case PrivilegeRole.Editor:
					roles = privileges.EditableRoles != null && privileges.EditableRoles.Count > 0
						? await Role.FindAsync(Filters<Role>.Or(privileges.EditableRoles.Select(roleID => Filters<Role>.Equals("ID", roleID))), null, 0, 1, null, cancellationToken).ConfigureAwait(false)
						: new List<Role>();
					return roles.SelectMany(role => role.UserIDs ?? new List<string>()).Concat(privileges.EditableUsers ?? new HashSet<string>()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

				case PrivilegeRole.Contributor:
					roles = privileges.ContributiveRoles != null && privileges.ContributiveRoles.Count > 0
						? await Role.FindAsync(Filters<Role>.Or(privileges.ContributiveRoles.Select(roleID => Filters<Role>.Equals("ID", roleID))), null, 0, 1, null, cancellationToken).ConfigureAwait(false)
						: new List<Role>();
					return roles.SelectMany(role => role.UserIDs ?? new List<string>()).Concat(privileges.ContributiveUsers ?? new HashSet<string>()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

				case PrivilegeRole.Viewer:
					roles = privileges.ViewableRoles != null && privileges.ViewableRoles.Count > 0
						? await Role.FindAsync(Filters<Role>.Or(privileges.ViewableRoles.Select(roleID => Filters<Role>.Equals("ID", roleID))), null, 0, 1, null, cancellationToken).ConfigureAwait(false)
						: new List<Role>();
					return roles.SelectMany(role => role.UserIDs ?? new List<string>()).Concat(privileges.ViewableUsers ?? new HashSet<string>()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

				case PrivilegeRole.Downloader:
					roles = privileges.DownloadableRoles != null && privileges.DownloadableRoles.Count > 0
						? await Role.FindAsync(Filters<Role>.Or(privileges.DownloadableRoles.Select(roleID => Filters<Role>.Equals("ID", roleID))), null, 0, 1, null, cancellationToken).ConfigureAwait(false)
						: new List<Role>();
					return roles.SelectMany(role => role.UserIDs ?? new List<string>()).Concat(privileges.DownloadableUsers ?? new HashSet<string>()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

				default:
					return new List<string>();
			}
		}

		static Task<JToken> GetUserProfilesAsync(this RequestInfo requestInfo, IEnumerable<string> userIDs, CancellationToken cancellationToken = default)
			=> Router.CallServiceAsync(new RequestInfo(requestInfo)
			{
				ServiceName = "Users",
				ObjectName = "Profile",
				Verb = "GET",
				Query = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
				{
					{ "object-identity", "fetch" },
					{ "x-request", new JObject { { "IDs", userIDs.ToJArray() } }.ToString(Newtonsoft.Json.Formatting.None).Url64Encode() }
				},
				Extra = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
				{
					{ "x-notifications-key", Utility.NotificationsKey }
				}
			}, cancellationToken);

		static async Task WriteNotificationErrorAsync(this RequestInfo requestInfo, Exception exception, CancellationToken cancellationToken = default, string message = null)
		{
			message = message ?? "Error occurred while sending a notification when an object was changed";
			Utility.Logger.LogError(message, exception);
			var logs = new List<string>
			{
				message
			};
			if (exception is WampException wampException)
			{
				var details = wampException.GetDetails();
				logs.Add($"> Message: {details.Item2}");
				logs.Add($"> Type: {details.Item3}");
			}
			else
			{
				logs.Add($"> Message: {exception.Message}");
				logs.Add($"> Type: {exception.GetType()}");
			}
			await Utility.LoggingService.WriteLogsAsync(requestInfo.CorrelationID, requestInfo.Session.DeveloperID, requestInfo.Session.AppID, ServiceBase.ServiceComponent.ServiceName, "Notifications", logs, exception.GetStack(), cancellationToken).ConfigureAwait(false);
		}

		static string GetWebURL(this string url, string siteURL)
			=> url.Replace("~/", siteURL);

		static string GetAppURL(this string url)
			=> $"{Utility.CmsPortalsHttpURI}/home?redirect={url.Url64Encode()}";

		/// <summary>
		/// Sends a notification when changed
		/// </summary>
		/// <param name="object"></param>
		/// <param name="event"></param>
		/// <param name="notificationSettings"></param>
		/// <param name="previousStatus"></param>
		/// <param name="status"></param>
		/// <param name="requestInfo"></param>
		/// <param name="cancellationToken"></param>
		/// <returns></returns>
		public static async Task SendNotificationAsync(this IPortalObject @object, string @event, Settings.Notifications notificationSettings, ApprovalStatus previousStatus, ApprovalStatus status, RequestInfo requestInfo, CancellationToken cancellationToken = default)
		{
			requestInfo = requestInfo ?? new RequestInfo();
			try
			{
				await requestInfo.SendNotificationAsync(@object, @event, notificationSettings, previousStatus, status, cancellationToken).ConfigureAwait(false);
			}
			catch (Exception exception)
			{
				await requestInfo.WriteNotificationErrorAsync(exception, cancellationToken).ConfigureAwait(false);
			}
		}

		static async Task SendNotificationAsync(this RequestInfo requestInfo, IPortalObject @object, string @event, Settings.Notifications notificationSettings, ApprovalStatus previousStatus, ApprovalStatus status, CancellationToken cancellationToken)
		{
			// check
			if (@object == null)
				return;

			// prepare settings
			var events = notificationSettings?.Events;
			var methods = notificationSettings?.Methods;
			var emails = notificationSettings?.Emails;
			var emailsByApprovalStatus = notificationSettings?.EmailsByApprovalStatus;
			var emailsWhenPublish = notificationSettings?.EmailsWhenPublish;
			var webHooks = notificationSettings?.WebHooks;
			var category = @object is Content ? (@object as Content).Category : null;
			var emailSettings = category != null ? category.EmailSettings : @object is Organization ? (@object as Organization).EmailSettings : null;

			var parent = @object.Parent;
			while (parent != null)
			{
				Settings.Notifications parentNotificationSettings = null;
				if (parent is Category parentAsCategory)
				{
					parentNotificationSettings = parentAsCategory.Notifications;
					emailSettings = emailSettings ?? parentAsCategory.EmailSettings;
				}
				else if (parent is ContentType parentAsContentType)
				{
					parentNotificationSettings = parentAsContentType.Notifications;
					emailSettings = emailSettings ?? parentAsContentType.EmailSettings;
				}
				else if (parent is Module parentAsModule)
				{
					parentNotificationSettings = parentAsModule.Notifications;
					emailSettings = emailSettings ?? parentAsModule.EmailSettings;
				}
				else if (parent is Organization parentAsOrganization)
				{
					parentNotificationSettings = parentAsOrganization.Notifications;
					emailSettings = emailSettings ?? parentAsOrganization.EmailSettings;
				}

				events = events != null && events.Count > 0 ? events : parentNotificationSettings?.Events;
				methods = methods != null && methods.Count > 0 ? methods : parentNotificationSettings?.Methods;
				emails = emails ?? parentNotificationSettings?.Emails;
				emailsByApprovalStatus = emailsByApprovalStatus ?? parentNotificationSettings?.EmailsByApprovalStatus;
				emailsWhenPublish = emailsWhenPublish ?? parentNotificationSettings?.EmailsWhenPublish;
				webHooks = webHooks ?? parentNotificationSettings?.WebHooks;
				parent = parent.Parent;
			}

			// stop if has no event
			if (events == null || events.Count < 1 || events.FirstOrDefault(e => e.IsEquals(@event)) == null)
				return;

			// prepare parameters
			var sender = (await requestInfo.GetUserProfilesAsync(new[] { requestInfo.Session.User.ID }, cancellationToken).ConfigureAwait(false) as JArray)?.FirstOrDefault();
			var businessObject = @object is IBusinessObject ? @object as IBusinessObject : null;
			var serviceName = ServiceBase.ServiceComponent.ServiceName;
			var objectName = (businessObject as RepositoryBase)?.GetObjectName() ?? @object.GetTypeName(true);
			var contentType = businessObject?.ContentType as ContentType;
			var organization = await (@object.OrganizationID ?? "").GetOrganizationByIDAsync(cancellationToken).ConfigureAwait(false);
			var alwaysUseHtmlSuffix = organization == null || organization.AlwaysUseHtmlSuffix;
			if (organization != null && organization._siteIDs == null)
				await organization.FindSitesAsync(cancellationToken).ConfigureAwait(false);
			var site = organization?.Sites.FirstOrDefault();
			var siteDomain = $"{site?.SubDomain}.{site?.PrimaryDomain}".Replace("*.", "www.").Replace("www.www.", "www.");
			var siteURL = $"http{(site != null && site.AlwaysUseHTTPs ? "s" : "")}://{siteDomain}/";

			// prepare the recipients and notification settings
			var recipientIDs = new List<string>();
			var sendWebHookNotifications = methods?.FirstOrDefault(method => method.IsEquals("WebHook")) != null && webHooks != null && webHooks.EndpointURLs != null && webHooks.EndpointURLs.Count > 0;
			var sendEmailNotifications = methods?.FirstOrDefault(method => method.IsEquals("Email")) != null;
			var emailNotifications = new Settings.EmailNotifications();

			switch (status)
			{
				case ApprovalStatus.Draft:
					recipientIDs = new[] { @object.CreatedID }.ToList();
					emailNotifications = emailsByApprovalStatus != null && emailsByApprovalStatus.ContainsKey($"{status}")
						? emailsByApprovalStatus[$"{status}"]
						: emails;
					break;

				case ApprovalStatus.Pending:
					recipientIDs = await @object.WorkingPrivileges.GetUserIDsAsync(PrivilegeRole.Editor, cancellationToken).ConfigureAwait(false);
					if (recipientIDs.Count < 1)
						recipientIDs = await @object.WorkingPrivileges.GetUserIDsAsync(PrivilegeRole.Moderator, cancellationToken).ConfigureAwait(false);
					if (recipientIDs.Count < 1)
						recipientIDs = await @object.WorkingPrivileges.GetUserIDsAsync(PrivilegeRole.Administrator, cancellationToken).ConfigureAwait(false);
					if (recipientIDs.Count < 1)
						recipientIDs = new[] { organization.OwnerID }.ToList();
					emailNotifications = emailsByApprovalStatus != null && emailsByApprovalStatus.ContainsKey($"{status}")
						? emailsByApprovalStatus[$"{status}"]
						: emails;
					break;

				case ApprovalStatus.Rejected:
					recipientIDs = new[] { @object.CreatedID }.ToList();
					emailNotifications = emailsByApprovalStatus != null && emailsByApprovalStatus.ContainsKey($"{status}")
						? emailsByApprovalStatus[$"{status}"]
						: emails;
					break;

				case ApprovalStatus.Approved:
					recipientIDs = await @object.WorkingPrivileges.GetUserIDsAsync(PrivilegeRole.Moderator, cancellationToken).ConfigureAwait(false);
					if (recipientIDs.Count < 1)
						recipientIDs = await @object.WorkingPrivileges.GetUserIDsAsync(PrivilegeRole.Administrator, cancellationToken).ConfigureAwait(false);
					if (recipientIDs.Count < 1)
						recipientIDs = new[] { organization.OwnerID }.ToList();
					emailNotifications = emailsByApprovalStatus != null && emailsByApprovalStatus.ContainsKey($"{status}")
						? emailsByApprovalStatus[$"{status}"]
						: emails;
					break;

				case ApprovalStatus.Published:
					recipientIDs = (await @object.WorkingPrivileges.GetUserIDsAsync(PrivilegeRole.Moderator, cancellationToken).ConfigureAwait(false))
						.Concat(await @object.WorkingPrivileges.GetUserIDsAsync(PrivilegeRole.Administrator, cancellationToken).ConfigureAwait(false))
						.Concat(new[] { @object.CreatedID }).ToList();
					if (recipientIDs.Count < 1)
						recipientIDs = new[] { organization.OwnerID }.ToList();
					emailNotifications = emailsByApprovalStatus != null && emailsByApprovalStatus.ContainsKey($"{status}")
						? emailsByApprovalStatus[$"{status}"]
						: emails;
					break;

				case ApprovalStatus.Archieved:
					recipientIDs = (await @object.WorkingPrivileges.GetUserIDsAsync(PrivilegeRole.Moderator, cancellationToken).ConfigureAwait(false))
						.Concat(await @object.WorkingPrivileges.GetUserIDsAsync(PrivilegeRole.Administrator, cancellationToken).ConfigureAwait(false))
						.Concat(new[] { @object.CreatedID, @object.LastModifiedID }).ToList();
					if (recipientIDs.Count < 1)
						recipientIDs = new[] { organization.OwnerID }.ToList();
					emailNotifications = emailsByApprovalStatus != null && emailsByApprovalStatus.ContainsKey($"{status}")
						? emailsByApprovalStatus[$"{status}"]
						: emails;
					break;
			}

			// prepare recipients
			recipientIDs = recipientIDs.Except(new[] { requestInfo.Session.User.ID }).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
			var recipients = await requestInfo.GetUserProfilesAsync(recipientIDs, cancellationToken).ConfigureAwait(false) as JArray;

			//  send app notifications
			try
			{
				var baseMessage = new BaseMessage
				{
					Type = $"Portals#Notification#{@event}",
					Data = new JObject
					{
						{ "Sender", new JObject
							{
								{ "ID", requestInfo.Session.User.ID },
								{ "Name", sender?.Get<string>("Name") ?? "Unknown" }
							}
						},
						{ "Action", @event },
						{ "Info", new JObject
							{
								{ "ServiceName", serviceName },
								{ "ObjectName", objectName },
								{ "SystemID", @object.OrganizationID },
								{ "ObjectID", @object.ID },
								{ "ObjectTitle", @object.Title },
								{ "Status", $"{status}" },
								{ "PreviousStatus", $"{previousStatus}" },
								{ "Time", DateTime.Now },
							}
						}
					}
				};
				await recipients.Select(recipient => recipient.Get<JArray>("Sessions"))
					.SelectMany(sessions => sessions.Select(session => session.Get<string>("DeviceID")))
					.ForEachAsync((deviceID, _) => Utility.RTUService.SendUpdateMessageAsync(new UpdateMessage(baseMessage) { DeviceID = deviceID }, cancellationToken), cancellationToken).ConfigureAwait(false);
			}
			catch (Exception exception)
			{
				await requestInfo.WriteNotificationErrorAsync(exception, cancellationToken, "Error occurred while sending app notifications").ConfigureAwait(false);
			}

			// send email notifications
			if (sendEmailNotifications || emailsWhenPublish != null)
			{
				var appURL = $"/portals/initializer?x-request={("{" + $"\"SystemID\":\"{organization?.ID}\",\"Action\":\"Open\",\"ObjectName\":\"{objectName}\",\"ObjectID\":\"{@object.ID}\"" + "}").Url64Encode()}".GetAppURL();
				var normalizedHTMLs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
				var definition = RepositoryMediator.GetEntityDefinition(@object.GetType());
				if (definition != null)
				{
					definition.Attributes.Where(attribute => attribute.IsCLOB != null && attribute.IsCLOB.Value).ForEach(attribute =>
					{
						var value = @object.GetAttributeValue<string>(attribute);
						normalizedHTMLs[attribute.Name] = value?.NormalizeHTML().NormalizeURLs(siteURL);
					});
					if (businessObject?.ExtendedProperties != null && definition.BusinessRepositoryEntities.TryGetValue(businessObject.RepositoryEntityID, out var repositiryEntity))
						repositiryEntity?.ExtendedPropertyDefinitions?.Where(propertyDefinition => propertyDefinition.Mode.Equals(ExtendedPropertyMode.LargeText)).ForEach(propertyDefinition =>
						{
							if (businessObject.ExtendedProperties.TryGetValue(propertyDefinition.Name, out var value))
								normalizedHTMLs[propertyDefinition.Name] = (value as string)?.NormalizeHTML().NormalizeURLs(siteURL);
						});
				}

				var @params = new Dictionary<string, ExpandoObject>(StringComparer.OrdinalIgnoreCase)
				{
					["Organization"] = organization?.ToJson(false, false, json =>
					{
						OrganizationProcessor.ExtraProperties.ForEach(name => json.Remove(name));
						json.Remove("Privileges");
						json["AlwaysUseHtmlSuffix"] = alwaysUseHtmlSuffix;
					}).ToExpandoObject(),
					["Site"] = site?.ToJson(json =>
					{
						SiteProcessor.ExtraProperties.ForEach(name => json.Remove(name));
						json.Remove("Privileges");
						json["Domain"] = siteDomain;
						json["URL"] = siteURL;
					}).ToExpandoObject(),
					["ContentTypeDefinition"] = contentType?.ContentTypeDefinition?.ToJson().ToExpandoObject(),
					["ModuleDefinition"] = contentType?.ContentTypeDefinition?.ModuleDefinition?.ToJson(json =>
					{
						(json as JObject).Remove("ContentTypeDefinitions");
						(json as JObject).Remove("ObjectDefinitions");
					}).ToExpandoObject(),
					["Module"] = contentType?.Module?.ToJson(false, false, json =>
					{
						ModuleProcessor.ExtraProperties.ForEach(name => json.Remove(name));
						json.Remove("Privileges");
					}).ToExpandoObject(),
					["ContentType"] = contentType?.ToJson(false, json =>
					{
						ModuleProcessor.ExtraProperties.ForEach(name => json.Remove(name));
						json.Remove("Privileges");
					}).ToExpandoObject(),
					["ParentContentType"] = contentType?.GetParent()?.ToJson(false, json =>
					{
						ModuleProcessor.ExtraProperties.ForEach(name => json.Remove(name));
						json.Remove("Privileges");
					}).ToExpandoObject(),
					["URLs"] = new JObject
					{
						{ "Public", $"{businessObject?.GetURL() ?? $"~/index"}{(alwaysUseHtmlSuffix ? ".html" : "")}".GetWebURL(siteURL) },
						{ "Portal", $"{businessObject?.GetURL() ?? $"~/index"}{(alwaysUseHtmlSuffix ? ".html" : "")}".GetWebURL($"{Utility.PortalsHttpURI}/~{organization?.Alias}/") },
						{ "Private", appURL },
						{ "Review", appURL }
					}.ToExpandoObject(),
					["HTMLs"] = normalizedHTMLs.ToExpandoObject(),
					["Sender"] = new JObject
					{
						{ "ID", requestInfo.Session.User.ID },
						{ "Name", sender?.Get<string>("Name") ?? "Unknown" },
						{ "Email", sender?.Get<string>("Email") },
						{ "URL", $"/users/profiles/{(sender?.Get<string>("Name") ?? "Unknown").GetANSIUri()}?x-request={("{\"ID\":\"" + requestInfo.Session.User.ID + "\"}").Url64Encode()}".GetAppURL() },
						{ "Location", await requestInfo.GetLocationAsync(cancellationToken).ConfigureAwait(false) },
						{ "IP", requestInfo.Session.IP },
						{ "AppName", requestInfo.Session.AppName },
						{ "AppPlatform", requestInfo.Session.AppPlatform }
					}.ToExpandoObject()
				};

				// update CMS Category
				if (category != null)
					@params["Category"] = category.ToJson(false, false, json =>
					{
						CategoryProcessor.ExtraProperties.ForEach(name => json.Remove(name));
						json.Remove("Privileges");
						json["URL"] = $"{category.GetURL()}{(alwaysUseHtmlSuffix ? ".html" : "")}".GetWebURL(siteURL);
					}).ToExpandoObject();

				// normalize parameters for evaluating
				var languages = Utility.Languages[requestInfo.CultureCode ?? "vi-VN"];
				var requestExpando = requestInfo.ToExpandoObject(expando => expando.Set("Body", requestInfo.BodyAsExpandoObject));
				var objectExpando = @object.ToExpandoObject();
				var paramsExpando = new Dictionary<string, object>(@params.ToDictionary(kvp => kvp.Key, kvp => kvp.Value as object), StringComparer.OrdinalIgnoreCase)
				{
					{ "Event", @event },
					{ "Event-i18n", languages?.Get<string>($"events.{@event}") ?? @event },
					{ "ObjectName", objectName },
					{ "ObjectType", @object.GetTypeName() },
					{ "Status", $"{status}" },
					{ "Status-i18n", languages?.Get<string>($"status.approval.{status}") ?? $"{status}" },
					{ "PreviousStatus", $"{previousStatus}" },
					{ "PreviousStatus-i18n", languages?.Get<string>($"status.approval.{previousStatus}") ?? $"{previousStatus}" },
					{ "Signature", emailSettings?.Signature?.NormalizeHTMLBreaks() },
					{ "EmailSignature", emailSettings?.Signature?.NormalizeHTMLBreaks() }
				}.ToExpandoObject();

				// send email message
				if (sendEmailNotifications)
					try
					{
						var addresses = recipients.Select(recipient => recipient.Get<string>("Email")).Where(email => !string.IsNullOrWhiteSpace(email)).Join(";");
						var subject = emailNotifications?.Subject ?? "[{{@params(Organization.Alias)}}] - \"{{@current(Title)}}\" was {{@toLower(@params(Event))}}d";
						var body = emailNotifications?.Body ?? @"
						Hi,
						The content that titled as ""<b>{{@current(Title)}}</b>"" ({{@params(ObjectName)}} on <a href=""{{@params(Site.URL)}}"">{{@params(Site.Title)}}</a>) was {{@toLower(@params(Event))}}d by {{@params(Sender.Name)}}.
						You can reach that content by one of these URLs below:
						<ul>
							<li>Public website: <a href=""{{@params(URLs.Public)}}"">{{@params(URLs.Public)}}</a></li>
							<li>CMS portals: <a href=""{{@params(URLs.Portal)}}"">{{@params(URLs.Portal)}}</a></li>
							<li>CMS apps: <a href=""{{@params(URLs.Private)}}"">{{@params(URLs.Private)}}</a></li>
						</ul>
						{{@params(EmailSignature)}}
						";
						var parameters = $"{subject}\r\n{body}"
							.GetDoubleBracesTokens()
							.Select(token => token.Item2)
							.Distinct(StringComparer.OrdinalIgnoreCase)
							.ToDictionary(token => token, token =>
							{
								return token.StartsWith("@[") && token.EndsWith("]")
									? Extensions.JsEvaluate(token.GetJsExpression(requestExpando, objectExpando, paramsExpando))
									: token.StartsWith("@")
										? token.Evaluate(new Tuple<ExpandoObject, ExpandoObject, ExpandoObject>(requestExpando, objectExpando, paramsExpando))
										: token;
							});
						var message = new EmailMessage
						{
							From = emailSettings?.Sender,
							To = addresses + (string.IsNullOrWhiteSpace(emailNotifications.ToAddresses) ? "" : $";{emailNotifications.ToAddresses}"),
							Cc = emailNotifications?.CcAddresses,
							Bcc = emailNotifications?.BccAddresses,
							Subject = subject.NormalizeHTMLBreaks().Format(parameters),
							Body = body.NormalizeHTMLBreaks().Format(parameters),
							SmtpServer = emailSettings?.Smtp?.Host,
							SmtpServerPort = emailSettings?.Smtp != null ? emailSettings.Smtp.Port : 0,
							SmtpServerEnableSsl = emailSettings?.Smtp != null && emailSettings.Smtp.EnableSsl,
							SmtpUsername = emailSettings?.Smtp?.User,
							SmtpPassword = emailSettings?.Smtp?.UserPassword,
							CorrelationID = requestInfo.CorrelationID
						};
						await Utility.MessagingService.SendEmailAsync(message, cancellationToken).ConfigureAwait(false);
						var log = "Add an email notification into queue successful" + "\r\n" +
							$"- ID: {message.ID}" + "\r\n" +
							$"- Object: {@object.Title} [{@object.GetType()}#{@object.ID}]" + "\r\n" +
							$"- Event: {@event}" + "\r\n" +
							$"- Status: {status} (previous: {previousStatus})" + "\r\n" +
							$"- Sender: {sender?.Get<string>("Name")} ({sender?.Get<string>("Email")})" + "\r\n" +
							$"- To: {message.To}" + (!string.IsNullOrWhiteSpace(message.Cc) ? $" / {message.Cc}" : "") + (!string.IsNullOrWhiteSpace(message.Bcc) ? $" / {message.Bcc}" : "") + "\r\n" +
							$"- Subject: {message.Subject}";
						await Utility.LoggingService.WriteLogAsync(requestInfo.CorrelationID, requestInfo.Session.DeveloperID, requestInfo.Session.AppID, serviceName, "Notifications", log, null, cancellationToken).ConfigureAwait(false);
						if (Utility.Logger.IsEnabled(LogLevel.Debug))
							Utility.Logger.LogDebug($"Add an email notification into queue successful\r\n{message.ToJson()}");
					}
					catch (Exception exception)
					{
						await requestInfo.WriteNotificationErrorAsync(exception, cancellationToken, "Error occurred while adding an email notification into queue").ConfigureAwait(false);
					}

				// send special email message (when publish)
				if (!@event.IsEquals("Delete") && businessObject != null && status.Equals(ApprovalStatus.Published) && !status.Equals(previousStatus) && emailsWhenPublish != null)
					try
					{
						var subject = emailsWhenPublish?.Subject ?? "[{{@params(Organization.Alias)}}] - \"{{@current(Title)}}\" was published";
						var body = emailsWhenPublish?.Body ?? @"
						Hi,
						The content that titled as ""<b>{{@current(Title)}}</b>"" ({{@params(ObjectName)}} on <a href=""{{@params(Site.URL)}}"">{{@params(Site.Title)}}</a>) was published by {{@params(Sender.Name)}}.
						You can reach that content by one of these URLs below:
						<ul>
							<li>Public website: <a href=""{{@params(URLs.Public)}}"">{{@params(URLs.Public)}}</a></li>
							<li>CMS portals: <a href=""{{@params(URLs.Portal)}}"">{{@params(URLs.Portal)}}</a></li>
							<li>CMS apps: <a href=""{{@params(URLs.Private)}}"">{{@params(URLs.Private)}}</a></li>
						</ul>
						{{@params(EmailSignature)}}
						";
						var parameters = $"{subject}\r\n{body}"
							.GetDoubleBracesTokens()
							.Select(token => token.Item2)
							.Distinct(StringComparer.OrdinalIgnoreCase)
							.ToDictionary(token => token, token =>
							{
								return token.StartsWith("@[") && token.EndsWith("]")
									? Extensions.JsEvaluate(token.GetJsExpression(requestExpando, objectExpando, paramsExpando))
									: token.StartsWith("@")
										? token.Evaluate(new Tuple<ExpandoObject, ExpandoObject, ExpandoObject>(requestExpando, objectExpando, paramsExpando))
										: token;
							});
						var message = new EmailMessage
						{
							From = emailSettings?.Sender,
							To = emailsWhenPublish?.ToAddresses,
							Cc = emailsWhenPublish?.CcAddresses,
							Bcc = emailsWhenPublish?.BccAddresses,
							Subject = subject.NormalizeHTMLBreaks().Format(parameters),
							Body = body.NormalizeHTMLBreaks().Format(parameters),
							SmtpServer = emailSettings?.Smtp?.Host,
							SmtpServerPort = emailSettings?.Smtp != null ? emailSettings.Smtp.Port : 0,
							SmtpServerEnableSsl = emailSettings?.Smtp != null && emailSettings.Smtp.EnableSsl,
							SmtpUsername = emailSettings?.Smtp?.User,
							SmtpPassword = emailSettings?.Smtp?.UserPassword,
							CorrelationID = requestInfo.CorrelationID
						};
						await Utility.MessagingService.SendEmailAsync(message, cancellationToken).ConfigureAwait(false);
						var log = "Add an email notification (notify when publish) into queue successful" + "\r\n" +
							$"- ID: {message.ID}" + "\r\n" +
							$"- Object: {@object.Title} [{@object.GetType()}#{@object.ID}]" + "\r\n" +
							$"- Event: {@event}" + "\r\n" +
							$"- Status: {status} (previous: {previousStatus})" + "\r\n" +
							$"- Sender: {sender?.Get<string>("Name")} ({sender?.Get<string>("Email")})" + "\r\n" +
							$"- To: {message.To}" + (!string.IsNullOrWhiteSpace(message.Cc) ? $" / {message.Cc}" : "") + (!string.IsNullOrWhiteSpace(message.Bcc) ? $" / {message.Bcc}" : "") + "\r\n" +
							$"- Subject: {message.Subject}";
						await Utility.LoggingService.WriteLogAsync(requestInfo.CorrelationID, requestInfo.Session.DeveloperID, requestInfo.Session.AppID, serviceName, "Notifications", log, null, cancellationToken).ConfigureAwait(false);
						if (Utility.Logger.IsEnabled(LogLevel.Debug))
							Utility.Logger.LogDebug($"Add an email notification (notify when publish) into queue successful\r\n{message.ToJson()}");
					}
					catch (Exception exception)
					{
						await requestInfo.WriteNotificationErrorAsync(exception, cancellationToken, "Error occurred while adding an email notification (notify when publish) into queue").ConfigureAwait(false);
					}
			}

			// send web-hook notifications
			if (sendWebHookNotifications)
				try
				{
					var signAlgorithm = webHooks.SignAlgorithm ?? "SHA256";
					var signKey = webHooks.SignKey ?? requestInfo.Session.AppID;
					var query = webHooks.AdditionalQuery?.ToExpandoObject().ToDictionary(kvp => kvp.Key, kvp => kvp.Value as string) ?? new Dictionary<string, string>();
					var header = webHooks.AdditionalHeader?.ToExpandoObject().ToDictionary(kvp => kvp.Key, kvp => kvp.Value as string) ?? new Dictionary<string, string>();
					header = new Dictionary<string, string>(header, StringComparer.OrdinalIgnoreCase)
					{
						{ "Event", @event },
						{ "ObjectName", objectName },
						{ "ObjectType", @object.GetTypeName() },
						{ "Status", $"{status}" },
						{ "PreviousStatus", $"{previousStatus}" },
						{ "DeveloperID", requestInfo.Session.DeveloperID },
						{ "AppID", requestInfo.Session.AppID },
						{ "SiteID", site?.ID },
						{ "SiteTitle", site?.Title },
						{ "SiteDomain", siteDomain },
						{ "SiteURL", siteURL },
						{ "OrganizationID", organization?.ID },
						{ "OrganizationTitle", organization?.Title },
						{ "ModuleID", contentType?.Module?.ID },
						{ "ModuleTitle", contentType?.Module?.Title },
						{ "ContentTypeID", contentType?.ID },
						{ "ContentTypeTitle", contentType?.Title }
					};

					var message = new WebHookMessage
					{
						EndpointURL = webHooks.EndpointURLs[0],
						Body = @object.ToJson(json =>
						{
							if (webHooks.GenerateIdentity)
							{
								var id = json.Get<string>("ID");
								if (!string.IsNullOrWhiteSpace(id))
									json["ID"] = id.GenerateUUID();
							}
						}).ToString(Newtonsoft.Json.Formatting.None),
						Query = query,
						Header = header,
						CorrelationID = requestInfo.CorrelationID
					}.Normalize(signAlgorithm, signKey, webHooks.SignatureName, webHooks.SignatureAsHex, webHooks.SignatureInQuery);

					await webHooks.EndpointURLs.ForEachAsync(async (endpointURL, _) =>
					{
						message.ID = UtilityService.NewUUID;
						message.EndpointURL = endpointURL;
						await Utility.MessagingService.SendWebHookAsync(message, cancellationToken).ConfigureAwait(false);
						var log = "Add a web-hook notification into queue successful" + "\r\n" +
							$"- ID: {message.ID}" + "\r\n" +
							$"- Object: {@object.Title} [{@object.GetType()}#{@object.ID}]" + "\r\n" +
							$"- Event: {@event}" + "\r\n" +
							$"- Status: {status} (previous: {previousStatus})" + "\r\n" +
							$"- Endpoint URL: {message.EndpointURL}";
						await Utility.LoggingService.WriteLogAsync(requestInfo.CorrelationID, requestInfo.Session.DeveloperID, requestInfo.Session.AppID, serviceName, "Notifications", log, null, cancellationToken).ConfigureAwait(false);
						if (Utility.Logger.IsEnabled(LogLevel.Debug))
							Utility.Logger.LogDebug($"Add a web-hook notification into queue successful\r\n{message.ToJson(json => { json["Query"] = message.Query.ToJObject(); json["Header"] = message.Header.ToJObject(); })}");
					}, cancellationToken).ConfigureAwait(false);
				}
				catch (Exception exception)
				{
					await requestInfo.WriteNotificationErrorAsync(exception, cancellationToken, "Error occurred while adding a web-hook notification into queue").ConfigureAwait(false);
				}
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

	//  --------------------------------------------------------------------------------------------

	[Serializable]
	[Repository(ServiceName = "Portals", ID = "A0000000000000000000000000000001", Title = "CMS", Description = "Services of the CMS Portals", Directory = "CMS", ExtendedPropertiesTableName = "T_Portals_Extended_Properties")]
	public abstract class Repository<T> : RepositoryBase<T> where T : class { }
}
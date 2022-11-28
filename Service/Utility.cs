#region Related components
using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using System.Dynamic;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using WampSharp.V2.Core.Contracts;
using net.vieapps.Components.Utility;
using net.vieapps.Components.Repository;
using net.vieapps.Components.Security;
#endregion

namespace net.vieapps.Services.Portals
{
	public static partial class Utility
	{
		/// <summary>
		/// Gets the messaging service
		/// </summary>
		public static IMessagingService MessagingService { get; internal set; }

		/// <summary>
		/// Gets the local logger
		/// </summary>
		public static ILogger Logger { get; internal set; }

		internal static ConcurrentQueue<Tuple<Tuple<DateTime, string, string, string, string, string, string>, List<string>, string>> Logs { get; } = new ConcurrentQueue<Tuple<Tuple<DateTime, string, string, string, string, string, string>, List<string>, string>>();

		internal static bool IsDebugLogEnabled => Utility.Logger != null && Utility.Logger.IsEnabled(LogLevel.Debug);

		internal static bool IsCacheLogEnabled => Utility.IsDebugLogEnabled || "true".IsEquals(UtilityService.GetAppSetting("Logs:Portals:Caches"));

		internal static bool IsWriteDesktopLogs(this RequestInfo requestInfo)
			=> Utility.IsDebugLogEnabled || (requestInfo != null && requestInfo.GetParameter("x-logs") != null) || "true".IsEquals(UtilityService.GetAppSetting("Logs:Portals:Desktops", "false"));

		internal static bool IsWriteMessageLogs(this RequestInfo requestInfo)
			=> Utility.IsDebugLogEnabled || (requestInfo != null && requestInfo.GetParameter("x-logs") != null) || "true".IsEquals(UtilityService.GetAppSetting("Logs:Portals:Messages", "false"));

		internal static bool Preload => "true".IsEquals(UtilityService.GetAppSetting("Portals:Preload", "true"));

		internal static bool RunProcessorInParallelsMode => "Parallels".IsEquals(UtilityService.GetAppSetting("Portals:Processor", "Parallels"));

		internal static CancellationToken CancellationToken => ServiceBase.ServiceComponent.CancellationToken;

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
		/// Gets the name of the service
		/// </summary>
		public static string ServiceName => ServiceBase.ServiceComponent.ServiceName;

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
		public static string DataFilesDirectory => UtilityService.GetAppSetting("Path:Portals");

		/// <summary>
		/// Gets the path to the directory that contains all temporary files
		/// </summary>
		public static string TempFilesDirectory => UtilityService.GetAppSetting("Path:Temp");

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
			=> domain.ToArray(".", true).Select(name => name.Equals("*") ? name : name.GetANSIUri(true, false, true)).Where(name => !string.IsNullOrWhiteSpace(name)).Join(".");

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
		/// Gets the children content-type of this content-type
		/// </summary>
		/// <param name="contentType"></param>
		/// <returns></returns>
		public static List<ContentType> GetChildren(this ContentType contentType)
		{
			var entityDefinition = contentType?.EntityDefinition;
			return entityDefinition != null
				? contentType?.Module?.ContentTypes?.Where(cntType => entityDefinition.ID.Equals(RepositoryMediator.GetEntityDefinition(cntType?.EntityDefinition?.ParentType)?.ID)).ToList()
				: null;
		}

		/// <summary>
		/// Gets the entity object name for working with real-time update messages
		/// </summary>
		/// <param name="definition"></param>
		/// <returns></returns>
		public static string GetObjectName(this ContentTypeDefinition definition)
			=> definition.EntityDefinition?.GetObjectName();

		static FileExtensionContentTypeProvider MimeTypeProvider { get; } = new FileExtensionContentTypeProvider();

		/// <summary>
		/// Gets the MIME type of a file
		/// </summary>
		/// <param name="filename"></param>
		/// <returns></returns>
		public static string GetMimeType(this string filename)
			=> Utility.MimeTypeProvider.TryGetContentType(filename, out var mimeType) && !string.IsNullOrWhiteSpace(mimeType) ? mimeType : "application/octet-stream; charset=utf-8";

		/// <summary>
		/// Gets the MIME type of a file
		/// </summary>
		/// <param name="fileInfo"></param>
		/// <returns></returns>
		public static string GetMimeType(this FileInfo fileInfo)
			=> fileInfo?.Name?.GetMimeType();

		/// <summary>
		/// Gets a pagination URL
		/// </summary>
		/// <param name="urlPattern"></param>
		/// <param name="pageNumber"></param>
		/// <param name="query"></param>
		/// <returns></returns>
		public static string GetPaginationURL(this string urlPattern, int pageNumber, string query = null)
		{
			var url = (urlPattern ?? "").Format(new Dictionary<string, object> { ["pageNumber"] = pageNumber }).Replace("/1.html", ".html");
			return (url.EndsWith("/1") ? url.Left(url.Length - 2) : url) + (string.IsNullOrWhiteSpace(query) ? "" : (url.IndexOf("?") > 0 ? "&" : "?") + query);
		}

		/// <summary>
		/// Generates the pagination
		/// </summary>
		/// <param name="totalRecords"></param>
		/// <param name="totalPages"></param>
		/// <param name="pageSize"></param>
		/// <param name="pageNumber"></param>
		/// <param name="urlPattern"></param>
		/// <param name="showPageLinks"></param>
		/// <param name="numberOfPageLinks"></param>
		/// <returns></returns>
		public static JObject GeneratePagination(long totalRecords, int totalPages, int pageSize, int pageNumber, string urlPattern, bool showPageLinks, int numberOfPageLinks, string query = null)
		{
			var pages = new List<JObject>(totalPages);
			if (totalPages > 1 && !string.IsNullOrWhiteSpace(urlPattern))
			{
				if (!showPageLinks || numberOfPageLinks < 1 || totalPages <= numberOfPageLinks)
					for (var page = 1; page <= totalPages; page++)
						pages.Add(new JObject
						{
							{ "Text", $"{page}" },
							{ "URL", urlPattern.GetPaginationURL(page, query) }
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
						{ "URL", urlPattern.GetPaginationURL(1, query) }
					});
					if (start - 1 > 1)
						pages.Add(new JObject
						{
							{ "Text", start - 1 > 2 ? "..." : $"{start - 1}" },
							{ "URL", urlPattern.GetPaginationURL(start - 1, query) }
						});
					for (var page = start; page <= end; page++)
						pages.Add(new JObject
						{
							{ "Text", $"{page}" },
							{ "URL", urlPattern.GetPaginationURL(page, query) }
						});
					if (end + 1 < totalPages)
						pages.Add(new JObject
						{
							{ "Text", end + 1 < totalPages ? "..." : $"{end + 1}" },
							{ "URL", urlPattern.GetPaginationURL(end + 1, query) }
						});
					pages.Add(new JObject
					{
						{ "Text", $"{totalPages}" },
						{ "URL", urlPattern.GetPaginationURL(totalPages, query) }
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
				{ "URLPattern", string.IsNullOrWhiteSpace(urlPattern) ? null : urlPattern + (string.IsNullOrWhiteSpace(query) ? "" : (urlPattern.IndexOf("?") > 0 ? "&" : "?") + query) },
				{ "Pages", pages == null ? null : new JObject { ["Page"] = pages.ToJArray() } }
			};
		}

		internal static string GetThumbnailURL(this string url, bool isPng, bool isBig, int width, int height)
		{
			if (isPng || isBig || width > 0 || height > 0)
			{
				var uri = new Uri(url);
				var segments = uri.AbsolutePath.ToList("/").Skip(2).ToList();
				url = $"{uri.Scheme}://{uri.Host}/"
					+ (isPng && isBig ? "thumbnailbigpngs" : isPng ? "thumbnailpngs" : isBig ? "thumbnailbigs" : "thumbnails")
					+ $"/{segments[0]}/{segments[1]}/{(width > 0 ? $"{width}" : "0")}/{(width > 0 ? $"{height}" : "0")}/{segments.Skip(4).Join("/")}";
				if (isPng && !url.IsEndsWith(".png"))
					url = url.Left(url.Length - 4) + ".png";
			}
			return url;
		}

		internal static JArray GetThumbnails(this JToken thumbnails, string objectID, bool isPng = false, bool isBig = false, int width = 0, int height = 0)
		{
			var thumbnailImages = thumbnails != null
				? thumbnails is JArray thumbnailsAsJArray
					? thumbnailsAsJArray
					: thumbnails[objectID] as JArray
				: null;
			thumbnailImages?.ForEach(thumbnail =>
			{
				var uri = thumbnail.Get<string>("URI");
				if (!string.IsNullOrWhiteSpace(uri))
					thumbnail["URI"] = uri.GetThumbnailURL(isPng, isBig, width, height);
				var uris = thumbnail.Get<JObject>("URIs");
				uri = uris?.Get<string>("Direct");
				if (!string.IsNullOrWhiteSpace(uri))
					uris["Direct"] = uri.GetThumbnailURL(isPng, isBig, width, height);
			});
			return thumbnailImages;
		}

		internal static string GetThumbnailURL(this JToken thumbnails, string objectID, bool isPng = false, bool isBig = false, int width = 0, int height = 0)
			=> thumbnails?.GetThumbnails(objectID, isPng, isBig, width, height)?.FirstOrDefault()?.Get<JObject>("URIs")?.Get<string>("Direct");

		internal static JArray GetAttachments(this JToken attachments, string objectID)
			=> attachments != null
				? attachments is JArray attachmentsAsJArray
					? attachmentsAsJArray
					: attachments[objectID] as JArray
				: null;

		internal static string GetWebpImageURL(this string url, bool useTransparentAsPng = false)
		{
			if (!string.IsNullOrWhiteSpace(url) && !url.IsEndsWith(".webp") && !url.IsContains("image=webp") && (url.IsStartsWith("~~/") || url.IsStartsWith(Utility.FilesHttpURI)))
			{
				var segments = new Uri(url.Replace("~~/", $"{Utility.FilesHttpURI}/")).AbsolutePath.ToList("/").Skip(1).ToList();
				var handler = segments[0].IsStartsWith("thumbnail") ? segments[0].ToLower() : "webp.image";
				if (segments[0].IsStartsWith("thumbnail"))
					handler = handler.IsEndsWith("pngs")
						? handler.Replace("pngs", "webps")
						: handler.IsEndsWith("bigs")
							? handler.Replace("bigs", "bigwebps")
							: "thumbnailwebps";
				url = (url.IsStartsWith("~~/") ? "~~" : Utility.FilesHttpURI) + $"/{handler}/" + (segments[0].IsStartsWith("thumbnail") ? segments.Skip(1).Join("/") : $"{segments[1]}/{segments.Skip(3).Join("/")}.webp");
				if (segments[0].IsStartsWith("thumbnail") && (url.IsEndsWith(".png") || url.IsEndsWith(".jpg")))
					url = (segments[2].Equals("0") ? url.Left(url.Length - 4) : url) + ".webp";
				url += useTransparentAsPng ? (url.IndexOf("?") > 0 ? "&" : "?") + "transparent=." : "";
			}
			return url;
		}

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
						var isAudio = url.IsEndsWith(".mp3") || url.IsEndsWith(".m4a");
						var tag = isAudio ? "audio" : "video";
						var height = isAudio ? "32" : "315";
						media = ($"<{tag} width=\"560\" height=\"{height}\" controls autoplay muted>" + "<source src=\"{{url}}\"/>" + $"</{tag}>").Format(new Dictionary<string, object> { ["url"] = url });
					}
					html = html.Substring(0, start) + media + html.Substring(end);
				}
				start = html.PositionOf("<oembed", start + 1);
			}

			// normalize all 'img' tags with WebP images
			start = html.PositionOf("<img");
			while (start > -1)
			{
				var offset = 1;
				var end = start < 0 ? -1 : html.PositionOf(">", start);
				if (end > -1)
				{
					end += 1;
					var image = html.Substring(start, end - start);

					var heightStart = image.PositionOf("style=");
					heightStart = heightStart > 0 ? image.PositionOf("height", heightStart + 1) : -1;
					if (heightStart > 0)
					{
						var heightEnd = image.IndexOf(";", heightStart + 1);
						heightEnd = heightEnd > 0 ? heightEnd : image.IndexOf("\"", heightStart + 1);
						heightEnd = heightEnd > 0 ? heightEnd : image.IndexOf("'", heightStart + 1);
						image = image.Remove(heightStart, heightEnd + 1 - heightStart);
					}

					heightStart = image.PositionOf("height=");
					if (heightStart > 0)
					{
						var heightEnd = image.IndexOf("\"", heightStart + 8);
						heightEnd = heightEnd > 0 ? heightEnd : image.IndexOf("'", heightStart + 8);
						image = image.Remove(heightStart, heightEnd + 2 - heightStart);
					}

					var urlStart = image.IndexOf("src=") + 5;
					var urlEnd = image.IndexOf("\"", urlStart + 1);
					if (urlEnd < 0)
						urlEnd = image.IndexOf("'", urlStart + 1);

					if (urlEnd > 0)
					{
						var url = image.Substring(urlStart, urlEnd - urlStart);
						var webpURL = url.IsContains("image=svg") ? url : url.GetWebpImageURL();
						if (!url.IsEquals(webpURL))
							image = $"<picture><source srcset=\"{webpURL}\"/>{image}</picture>";
					}

					html = html.Substring(0, start) + image + html.Substring(end);
					offset = image.Length;
				}
				start = html.PositionOf("<img", start + offset);
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
		/// <param name="baseHost"></param>
		/// <returns></returns>
		public static string GetRootURL(this Uri baseURI, string systemIdentity, bool useShortURLs = false, string baseHost = null)
			=> useShortURLs
				? baseURI.IsPortalsHttpURI() ? "" : "/"
				: baseURI.IsPortalsHttpURI() ? $"{Utility.PortalsHttpURI}/~{systemIdentity}/" : $"{baseURI.Scheme}://{baseHost ?? baseURI.Host}/";

		/// <summary>
		/// Normalizes all URLs of a HTML content
		/// </summary>
		/// <param name="html"></param>
		/// <param name="rootURL"></param>
		/// <param name="forDisplaying"></param>
		/// <param name="filesHttpURI"></param>
		/// <param name="portalsHttpURI"></param>
		/// <returns></returns>
		public static string NormalizeURLs(this string html, string rootURL, bool forDisplaying = true, string filesHttpURI = null, string portalsHttpURI = null)
			=> forDisplaying
				? html?.Replace("~~~/", rootURL).Replace("~~/", $"{filesHttpURI ?? Utility.FilesHttpURI}/").Replace("~#/", $"{portalsHttpURI ?? Utility.PortalsHttpURI}/").Replace("~/", rootURL)
				: html?.Replace(StringComparison.OrdinalIgnoreCase, $"{Utility.FilesHttpURI}/", "~~/").Replace(StringComparison.OrdinalIgnoreCase, rootURL, "~/");

		/// <summary>
		/// Normalizes all URIs of attachments (files or thumbnails)
		/// </summary>
		/// <param name="attachments"></param>
		/// <param name="filesHttpURI"></param>
		/// <returns></returns>
		public static JToken NormalizeURIs(this JToken attachments, string filesHttpURI)
		{
			if (attachments != null && !string.IsNullOrWhiteSpace(filesHttpURI))
				foreach (JObject attachment in attachments)
					if (attachment != null)
					{
						var uris = attachment.Get<JObject>("URIs");
						if (uris != null)
						{
							uris["Direct"] = uris.Get<string>("Direct")?.Replace(Utility.FilesHttpURI, filesHttpURI);
							uris["Download"] = uris.Get<string>("Download")?.Replace(Utility.FilesHttpURI, filesHttpURI);
						}
						var uri = attachment.Get<string>("URI")?.Replace(Utility.FilesHttpURI, filesHttpURI);
						if (!string.IsNullOrWhiteSpace(uri))
							attachment["URI"] = uri;
					}
			return attachments;
		}

		/// <summary>
		/// Normalizes all URLs of a HTML content
		/// </summary>
		/// <param name="html"></param>
		/// <param name="requestURI"></param>
		/// <param name="systemIdentity"></param>
		/// <param name="useShortURLs"></param>
		/// <param name="forDisplaying"></param>
		/// <param name="filesHttpURI"></param>
		/// <param name="portalsHttpURI"></param>
		/// <param name="baseHost"></param>
		/// <returns></returns>
		public static string NormalizeURLs(this string html, Uri requestURI, string systemIdentity, bool useShortURLs = true, bool forDisplaying = true, string filesHttpURI = null, string portalsHttpURI = null, string baseHost = null)
		{
			if (string.IsNullOrWhiteSpace(html))
				return html;

			html = forDisplaying
				? html.Replace("~/_", $"{portalsHttpURI ?? Utility.PortalsHttpURI}/_")
				: html.Replace($"{Utility.PortalsHttpURI}/_", "~/_");

			html = html.NormalizeURLs(forDisplaying ? requestURI.GetRootURL(systemIdentity, useShortURLs, baseHost) : requestURI.GetRootURL(systemIdentity, useShortURLs, baseHost), forDisplaying, filesHttpURI, portalsHttpURI);

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
		/// <param name="rootURL"></param>
		/// <returns></returns>
		public static string NormalizeURLs(this Organization organization, string html, bool forDisplaying = true, string rootURL = null)
		{
			if (string.IsNullOrWhiteSpace(html) || organization == null)
				return html;

			rootURL = rootURL ?? new Uri(Utility.PortalsHttpURI).GetRootURL(organization.Alias, false);
			if (forDisplaying)
				return html.NormalizeURLs(rootURL, true, string.IsNullOrWhiteSpace(organization.FakeFilesHttpURI) ? null : organization.FakeFilesHttpURI, string.IsNullOrWhiteSpace(organization.FakePortalsHttpURI) ? null : organization.FakePortalsHttpURI);

			var domains = new List<string>();
			(organization.Sites ?? new List<Site>()).ForEach(site =>
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

			html = html.Replace($"{Utility.PortalsHttpURI}/_", "~/_");
			new[] { rootURL, string.IsNullOrWhiteSpace(organization.FakePortalsHttpURI) ? null : new Uri(organization.FakePortalsHttpURI).GetRootURL(organization.Alias, false) }
				.Concat(domains.Select(domain => $"http://{domain}/"))
				.Concat(domains.Select(domain => $"https://{domain}/"))
				.Where(url => !string.IsNullOrWhiteSpace(url))
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.ForEach(url => html = html.NormalizeURLs(url, false));

			return html;
		}

		/// <summary>
		/// Normalizes all URLs for displaying
		/// </summary>
		/// <param name="string"></param>
		/// <param name="portalsHttpURI"></param>
		/// <param name="filesHttpURI"></param>
		/// <returns></returns>
		public static string NormalizeURLs(this string @string, string portalsHttpURI, string filesHttpURI)
			=> @string.Replace("~#/", $"{portalsHttpURI}/").Replace("~~~/", $"{portalsHttpURI}/").Replace("~~/", $"{filesHttpURI}/");

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
					roles = privileges.AdministrativeRoles != null && privileges.AdministrativeRoles.Any()
						? await Role.FindAsync(Filters<Role>.Or(privileges.AdministrativeRoles.Select(roleID => Filters<Role>.Equals("ID", roleID))), null, 0, 1, null, cancellationToken).ConfigureAwait(false)
						: new List<Role>();
					return roles.SelectMany(role => role.UserIDs ?? new List<string>()).Concat(privileges.AdministrativeUsers ?? new HashSet<string>()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

				case PrivilegeRole.Moderator:
					roles = privileges.ModerateRoles != null && privileges.ModerateRoles.Any()
						? await Role.FindAsync(Filters<Role>.Or(privileges.ModerateRoles.Select(roleID => Filters<Role>.Equals("ID", roleID))), null, 0, 1, null, cancellationToken).ConfigureAwait(false)
						: new List<Role>();
					return roles.SelectMany(role => role.UserIDs ?? new List<string>()).Concat(privileges.ModerateUsers ?? new HashSet<string>()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

				case PrivilegeRole.Editor:
					roles = privileges.EditableRoles != null && privileges.EditableRoles.Any()
						? await Role.FindAsync(Filters<Role>.Or(privileges.EditableRoles.Select(roleID => Filters<Role>.Equals("ID", roleID))), null, 0, 1, null, cancellationToken).ConfigureAwait(false)
						: new List<Role>();
					return roles.SelectMany(role => role.UserIDs ?? new List<string>()).Concat(privileges.EditableUsers ?? new HashSet<string>()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

				case PrivilegeRole.Contributor:
					roles = privileges.ContributiveRoles != null && privileges.ContributiveRoles.Any()
						? await Role.FindAsync(Filters<Role>.Or(privileges.ContributiveRoles.Select(roleID => Filters<Role>.Equals("ID", roleID))), null, 0, 1, null, cancellationToken).ConfigureAwait(false)
						: new List<Role>();
					return roles.SelectMany(role => role.UserIDs ?? new List<string>()).Concat(privileges.ContributiveUsers ?? new HashSet<string>()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

				case PrivilegeRole.Viewer:
					roles = privileges.ViewableRoles != null && privileges.ViewableRoles.Any()
						? await Role.FindAsync(Filters<Role>.Or(privileges.ViewableRoles.Select(roleID => Filters<Role>.Equals("ID", roleID))), null, 0, 1, null, cancellationToken).ConfigureAwait(false)
						: new List<Role>();
					return roles.SelectMany(role => role.UserIDs ?? new List<string>()).Concat(privileges.ViewableUsers ?? new HashSet<string>()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

				case PrivilegeRole.Downloader:
					roles = privileges.DownloadableRoles != null && privileges.DownloadableRoles.Any()
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

		internal static Task WriteLogsAsync(string developerID, string appID, string objectName, List<string> logs, Exception exception = null, string correlationID = null, string additional = null)
		{
			// prepare
			correlationID = correlationID ?? UtilityService.NewUUID;
			var wampException = exception != null && exception is WampException
				? (exception as WampException).GetDetails()
				: null;

			logs = logs ?? new List<string>();
			if (wampException != null)
			{
				logs.Add($"> Message: {wampException.Item2}");
				logs.Add($"> Type: {wampException.Item3}");
			}
			else if (exception != null)
			{
				logs.Add($"> Message: {exception.Message}");
				logs.Add($"> Type: {exception.GetTypeName(true)}");
			}

			if (!string.IsNullOrWhiteSpace(additional))
				logs.Add(additional);

			var stack = wampException != null
				? $"{wampException.Item3}: {wampException.Item2}\r\n{wampException.Item4}"
				: exception?.GetStack();

			// update queue & write to centerlized logs
			Utility.Logs.Enqueue(new Tuple<Tuple<DateTime, string, string, string, string, string, string>, List<string>, string>(new Tuple<DateTime, string, string, string, string, string, string>(DateTime.Now, correlationID, developerID, appID, ServiceBase.ServiceComponent.NodeID, Utility.ServiceName, objectName), logs, stack));
			return Utility.Logs.WriteLogsAsync(Utility.CancellationToken, Utility.Logger);
		}

		internal static Task WriteErrorAsync(this RequestInfo requestInfo, Exception exception, string message = null, string objectName = null, string additionnal = null)
		{
			message = message ?? "Error occurred while sending a notification when an object was changed";
			Utility.Logger.LogError(message, exception);
			return Utility.WriteLogsAsync(requestInfo.Session.DeveloperID, requestInfo.Session.AppID, objectName ?? requestInfo.ObjectName ?? "Notifications", new List<string> { message }, exception, requestInfo.CorrelationID, additionnal);
		}

		internal static Task WriteErrorAsync(Exception exception, string message = null, string objectName = null, string correlationID = null)
			=> Utility.WriteLogsAsync(null, null, objectName, string.IsNullOrWhiteSpace(message) ? null : new List<string> { message }, exception, correlationID);

		internal static Task WriteLogAsync(this RequestInfo requestInfo, string log, string objectName = null)
			=> Utility.WriteLogsAsync(requestInfo.Session.DeveloperID, requestInfo.Session.AppID, objectName ?? requestInfo.ObjectName ?? "Notifications", new List<string> { log }, null, requestInfo.CorrelationID);

		internal static Task WriteLogAsync(string correlationID, string log, string objectName = null)
			=> Utility.WriteLogsAsync(null, null, objectName, new List<string> { log }, null, correlationID);

		internal static string MinifyJs(this string data)
			=> Minifier.MinifyJs(data);

		internal static string MinifyCss(this string data)
			=> Minifier.MinifyCss(data);

		internal static string MinifyHtml(this string data)
		{
			var html = data;
			try
			{
				html = UtilityService.RemoveWhitespaces(data.Replace(" ", " "));
			}
			catch { }
			return html;
		}

		internal static bool IsAdministrator(this IUser user, Privileges privileges, Privileges parentPrivileges, Organization organization)
			=> user.ID.IsEquals(organization?.OwnerID) || user.IsAdministrator(privileges, parentPrivileges ?? organization?.WorkingPrivileges);

		internal static bool IsModerator(this IUser user, Privileges privileges, Privileges parentPrivileges, Organization organization)
			=> user.ID.IsEquals(organization?.OwnerID) || user.IsModerator(privileges, parentPrivileges ?? organization?.WorkingPrivileges);

		internal static bool IsEditor(this IUser user, Privileges privileges, Privileges parentPrivileges, Organization organization)
			=> user.ID.IsEquals(organization?.OwnerID) || user.IsEditor(privileges, parentPrivileges ?? organization?.WorkingPrivileges);

		internal static bool IsContributor(this IUser user, Privileges privileges, Privileges parentPrivileges, Organization organization)
			=> user.ID.IsEquals(organization?.OwnerID) || user.IsContributor(privileges, parentPrivileges ?? organization?.WorkingPrivileges);

		internal static bool IsViewer(this IUser user, Privileges privileges, Privileges parentPrivileges, Organization organization)
			=> user.ID.IsEquals(organization?.OwnerID) || user.IsViewer(privileges, parentPrivileges ?? organization?.WorkingPrivileges);

		internal static bool IsDownloader(this IUser user, Privileges privileges, Privileges parentPrivileges, Organization organization)
			=> user.ID.IsEquals(organization?.OwnerID) || user.IsDownloader(privileges, parentPrivileges ?? organization?.WorkingPrivileges);
	}

	//  --------------------------------------------------------------------------------------------

	[Repository(ServiceName = "Portals", ID = "A0000000000000000000000000000001", Title = "CMS", Description = "Services of the CMS Portals", Directory = "cms", ExtendedPropertiesTableName = "T_Portals_Extended_Properties")]
	public abstract class Repository<T> : RepositoryBase<T> where T : class { }

	[EventHandlers]
	public class FindObjectVersions : IPostUpdateHandler
	{
		public void OnPostUpdate<T>(RepositoryContext context, T @object, HashSet<string> changed, bool isRollback) where T : class
			=> (@object as RepositoryBase).FindVersionsAsync().Run();

		public Task OnPostUpdateAsync<T>(RepositoryContext context, T @object, HashSet<string> changed, bool isRollback, CancellationToken cancellationToken) where T : class
			=> (@object as RepositoryBase).FindVersionsAsync(cancellationToken);
	}

}
#region Related components
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Dynamic;
using System.Xml.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.Processing;
using WampSharp.V2.Core.Contracts;
using net.vieapps.Components.Repository;
using net.vieapps.Components.Security;
using net.vieapps.Components.Utility;
using net.vieapps.Services.Portals.Settings;
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

		internal static ConcurrentQueue<((DateTime Time, string CorrelationID, string DeveloperID, string AppID, string NodeID, string ServiceName, string ObjectName) Info, List<string> Logs, string Stack)> Logs { get; } = new ConcurrentQueue<((DateTime Time, string CorrelationID, string DeveloperID, string AppID, string NodeID, string ServiceName, string ObjectName) Info, List<string> Logs, string Stack)>();

		internal static bool IsDebugLogEnabled => Utility.Logger != null && Utility.Logger.IsEnabled(LogLevel.Debug);

		internal static bool IsWriteDebugLogs(this RequestInfo requestInfo, string component = null) => Utility.IsDebugLogEnabled || (requestInfo != null && requestInfo.ContainsKey("x-logs")) || (component != null && "true".IsEquals(UtilityService.GetAppSetting($"Logs:Portals:{component}")));

		internal static bool IsWriteCacheLogs(this RequestInfo requestInfo) => Utility.IsWriteDebugLogs(requestInfo, "Caches") || (requestInfo != null && requestInfo.ContainsKey("x-cache-logs"));

		internal static bool IsCacheLogEnabled => Utility.IsWriteCacheLogs(null);

		internal static bool IsPurgeCacheLogEnabled => Utility.IsCacheLogEnabled || "true".IsEquals(UtilityService.GetAppSetting("Cache:Portals:PurgeLogs", "false"));

		internal static bool IsWriteDesktopLogs(this RequestInfo requestInfo) => Utility.IsWriteDebugLogs(requestInfo, "Desktops");

		internal static bool IsDesktopLogEnabled => Utility.IsWriteDesktopLogs(null);

		internal static bool IsWriteMessageLogs(this RequestInfo requestInfo) => Utility.IsWriteDebugLogs(requestInfo, "Messages");

		internal static bool IsMessageLogEnabled => Utility.IsWriteMessageLogs(null);

		internal static bool IsForceCache(this RequestInfo requestInfo) => requestInfo.ContainsKey("x-force-cache") || requestInfo.ContainsKey("x-no-cache");

		internal static bool AllowInlineImages { get; } = "true".IsEquals(UtilityService.GetAppSetting("Portals:InlineImages:Allow", "true"));

		internal static bool UploadInlineImages	{ get; } = "upload".IsEquals(UtilityService.GetAppSetting("Portals:InlineImages:Mode", "Upload"));

		internal static bool Preload { get; } = "true".IsEquals(UtilityService.GetAppSetting("Portals:Preload", "true"));

		internal static bool RunProcessorInParallelsMode { get; } = "Parallels".IsEquals(UtilityService.GetAppSetting("Portals:Processor", "Parallels"));

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
		/// Gets the key for validating/signing a JSON Web Token
		/// </summary>
		public static string JWTKey { get; internal set; }

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
		public static ConcurrentHashSet<AliasKey> NotRecognizedAliases { get; } = new ConcurrentHashSet<AliasKey>();

		/// <summary>
		/// Gets the collection of OEmbed providers
		/// </summary>
		public static List<(string Name, List<Regex> Schemes, (Regex Expression, int Position, string Html) Pattern)> OEmbedProviders { get; } = new();

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
		/// Gets the URI of the Portals WebSocket HTTP service
		/// </summary>
		public static string PortalsWebSocketURI { get; internal set; }

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
		public static string DataFilesDirectory { get; } = UtilityService.GetAppSetting("Path:Portals");

		/// <summary>
		/// Gets the path to the directory that contains all temporary files
		/// </summary>
		public static string TempFilesDirectory { get; } = UtilityService.GetAppSetting("Path:Temp");

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
		{
			alias = alias.Replace(StringComparison.OrdinalIgnoreCase, ".html", "").Replace(StringComparison.OrdinalIgnoreCase, ".aspx", "").Replace(StringComparison.OrdinalIgnoreCase, ".php", "");
			return allowMinusSymbols
				? alias.GetANSIUri()
				: alias.GetANSIUri().Replace("-", "").Replace("_", "");
		}

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

		/// <summary>
		/// Gets a business object
		/// </summary>
		/// <typeparam name="T"></typeparam>
		/// <param name="objectID"></param>
		/// <param name="entityInfo"></param>
		/// <param name="cancellationToken"></param>
		/// <returns></returns>
		public static async Task<T> GetBusinessObjectAsync<T>(this string objectID, string entityInfo = null, CancellationToken cancellationToken = default) where T : class
		{
			if (!string.IsNullOrWhiteSpace(entityInfo) && entityInfo.IsValidUUID())
				await entityInfo.GetContentTypeByIDAsync(cancellationToken).ConfigureAwait(false);
			return await RepositoryMediator.GetAsync(entityInfo, objectID, cancellationToken).ConfigureAwait(false) as T;
		}

		static Microsoft.AspNetCore.StaticFiles.FileExtensionContentTypeProvider MimeTypeProvider { get; } = new();

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

		internal static string GetThumbnailURL(this string url, int width, int height, bool isPng)
		{
			if (width > 0 || height > 0 || isPng)
			{
				var uri = new Uri(url);
				var segments = uri.AbsolutePath.ToList("/").Skip(2).ToList();
				url = $"{uri.Scheme}://{uri.Host}/" + (isPng ? "thumbnailpngs" : "thumbnails") + $"/{segments[0]}/{segments[1]}/{(width > 0 ? $"{width}" : "0")}/{(width > 0 ? $"{height}" : "0")}/{segments.Skip(4).Join("/")}";
				if (isPng && !url.IsEndsWith(".png"))
					url = url.Left(url.Length - 4) + ".png";
			}
			return url;
		}

		internal static JArray GetThumbnails(this JToken thumbnails, string objectID, int width = 0, int height = 0, bool isPng = false)
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
					thumbnail["URI"] = uri.GetThumbnailURL(width, height, isPng);
				var uris = thumbnail.Get<JObject>("URIs");
				uri = uris?.Get<string>("Direct");
				if (!string.IsNullOrWhiteSpace(uri))
					uris["Direct"] = uri.GetThumbnailURL(width, height, isPng);
			});
			return thumbnailImages;
		}

		internal static string GetThumbnailURL(this JToken thumbnails, string objectID, int width = 0, int height = 0, bool isPng = false)
			=> thumbnails?.GetThumbnails(objectID, width, height, isPng)?.FirstOrDefault()?.Get<JObject>("URIs")?.Get<string>("Direct");

		internal static XElement UpdateThumbnail(this XElement element, string thumbnailURL, bool transparency)
		{
			var originalURL = string.IsNullOrWhiteSpace(thumbnailURL) ? element?.Value ?? "" : thumbnailURL;
			var alternativeURL = originalURL.GetWebpImageURL(null, transparency);
			if (element != null)
			{
				element.Value = string.IsNullOrWhiteSpace(alternativeURL) ? originalURL : alternativeURL;
				var attribute = element.Attribute("Original");
				if (attribute != null)
					attribute.Value = originalURL;
				else
					element.Add(new XAttribute("Original", originalURL));
				attribute = element.Attribute("Alternative");
				if (attribute != null)
					attribute.Value = alternativeURL;
				else
					element.Add(new XAttribute("Alternative", alternativeURL));
			}
			return element;
		}

		internal static XElement GetThumbnail(this string thumbnailURL, bool transparency = false, string tag = "ThumbnailURL")
			=> new XElement(string.IsNullOrWhiteSpace(tag) ? "ThumbnailURL" : tag, thumbnailURL).UpdateThumbnail(thumbnailURL, transparency);

		internal static XElement AddThumbnail(this XElement element, string thumbnailURL, bool transparency = false, string tag = "ThumbnailURL")
		{
			element?.Add(thumbnailURL?.GetThumbnail(transparency, tag));
			return element;
		}

		internal static JArray GetAttachments(this JToken attachments, string objectID)
			=> attachments != null
				? attachments is JArray attachmentsAsJArray
					? attachmentsAsJArray
					: attachments[objectID] as JArray
				: null;

		internal static string GetWebpImageURL(this string url, string filesHttpURI = null, bool transparency = false)
		{
			if (!string.IsNullOrWhiteSpace(url) && (url.IsStartsWith("~~/") || url.IsStartsWith(filesHttpURI ?? Utility.FilesHttpURI) || url.IsStartsWith(Utility.FilesHttpURI)))
			{
				var segments = new Uri(url.Replace("~~/", $"{filesHttpURI ?? Utility.FilesHttpURI}/")).AbsolutePath.ToList("/").Skip(1).ToList();
				var mime = FilesHttpMIMEs.Any(info => info.Handler.IsEquals(segments[0])) ? FilesHttpMIMEs.First(info => info.Handler.IsEquals(segments[0])) : (null, null);
				if (mime.Handler != null && mime.MIMEType != null)
				{
					segments[0] = "files";
					segments.Insert(2, mime.MIMEType);
				}
				var handler = segments[0].IsStartsWith("thumbnail") ? segments[0].ToLower() : "images";
				handler = segments[0].IsStartsWith("thumbnail") ? handler.IsEndsWith("pngs") ? handler.Replace("pngs", "webps") : "thumbnailwebps" : handler;
				url = (url.IsStartsWith("~~/") ? "~~" : filesHttpURI ?? Utility.FilesHttpURI) + $"/{handler}/";
				url += segments[0].IsStartsWith("thumbnail")
					? segments.Skip(1).Join("/")
					: $"{segments[1]}/{(segments.Count > 3 && segments[3].Length > 33 && segments[3].Left(32).IsValidUUID() ? $"{segments[3].Left(32)}/{segments[3].Right(segments[3].Length - 33)}.webp" : $"{segments.Skip(3).Join("/")}.webp")}";
				if (segments[0].IsStartsWith("thumbnail") && (url.IsEndsWith(".png") || url.IsEndsWith(".jpg")))
					url = (segments[2].Equals("0") ? url.Left(url.Length - 4) : url) + ".webp";
				url = url.IsEndsWith(".webp.webp") ? url.Left(url.Length - 5) : url;
				url += transparency ? (url.IndexOf("?") > 0 ? "&" : "?") + "transparent" : "";
			}
			return url;
		}

		/// <summary>
		/// Normalizes the HTML contents
		/// </summary>
		/// <param name="html"></param>
		/// <returns></returns>
		public static string NormalizeHTML(this string html, string filesHttpURI)
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
					var providerIndex = Utility.OEmbedProviders.FindIndex(provider => provider.Schemes.Any(regex => regex.Match(url).Success));
					if (providerIndex	> -1)
					{
						var oembedProvider = Utility.OEmbedProviders[providerIndex];
						var position = oembedProvider.Pattern.Position;
						var match = oembedProvider.Pattern.Expression.Match(url);
						media = oembedProvider.Pattern.Html.Format(new Dictionary<string, object> { ["id"] = match.Success && match.Length > position ? match.Groups[position].Value : null });
					}
					else
					{
						var isAudio = url.IsEndsWith(".mp3") || url.IsEndsWith(".m4a") || url.IsEndsWith(".wav");
						var tag = isAudio ? "audio" : "video";
						var height = isAudio ? "32" : "315";
						media = ($"<{tag} width=\"560\" height=\"{height}\" controls autoplay muted>" + "<source src=\"{{url}}\"/>" + $"</{tag}>").Format(new Dictionary<string, object> { ["url"] = url });
					}
					html = html.Substring(0, start) + media + html.Substring(end);
				}
				start = html.PositionOf("<oembed", start + 1);
			}

			// normalize IMG tags
			start = html.PositionOf("<img");
			while (start > -1)
			{
				var offset = 1;
				var end = html.PositionOf(">", start);
				if (end > start)
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

					image = image.PositionOf("decoding=") < 0 ? image.Replace(StringComparison.OrdinalIgnoreCase, "<img", "<img decoding=\"async\"") : image;
					image = image.PositionOf("loading=") < 0 ? image.Replace(StringComparison.OrdinalIgnoreCase, "<img", "<img loading=\"lazy\"") : image;
					image = image.Replace(";  alt=", ";\" alt=");
					image = image.EndsWith("/>") ? image : image.Replace(">", "/>");

					var urlStart = image.IndexOf("src=") + 5;
					var urlEnd = image.IndexOf("\"", urlStart + 1);
					if (urlEnd < 0)
						urlEnd = image.IndexOf("'", urlStart + 1);

					if (urlEnd > 0)
					{
						var imageStart = image.Substring(0, urlStart);
						var imageEnd = image.Substring(urlEnd);
						var url = image.Substring(urlStart, urlEnd - urlStart);

						// inline image
						if (url.IsStartsWith("data:image/"))
							image = Utility.AllowInlineImages ? image : $"{imageStart}~~/thumbnails/no-image.png{imageEnd}";

						// use WebP image
						else
						{
							url = url.IsStartsWith("/files/") ? $"~~{url}" : url;
							var webpURL = url.IsContains("image=svg") ? url : url.GetWebpImageURL(filesHttpURI);
							image = url.IsEquals(webpURL) ? image : url.IsContains(".webp") ? imageStart + webpURL + imageEnd : $"<picture><source srcset=\"{webpURL}\"/>{imageStart + url + imageEnd}</picture>";
						}
					}

					html = html.Substring(0, start) + image + html.Substring(end);
					offset = image.Length;
				}
				start = html.PositionOf("<img", start + offset);
			}

			// normalize inline popup image
			start = html.PositionOf("<figure class=\"image\"><a class=\"inline popup");
			while (start > -1)
			{
				var offset = 1;
				start = html.PositionOf("<a", start);
				var end = html.PositionOf(">", start);
				if (end > start)
				{
					end += 1;
					var anchor = html.Substring(start, end - start);

					var urlStart = anchor.PositionOf("href=") + 6;
					var urlEnd = anchor.IndexOf("\"", urlStart + 1);
					if (urlEnd < 0)
						urlEnd = anchor.IndexOf("'", urlStart + 1);

					if (urlEnd > 0)
					{
						var anchorStart = anchor.Substring(0, urlStart);
						var anchorEnd = anchor.Substring(urlEnd);
						var url = anchor.Substring(urlStart, urlEnd - urlStart);
						url = url.IsStartsWith("/files/") ? $"~~{url}" : url;
						var webpURL = url.IsContains("image=") && !url.IsContains("image=svg") ? url.GetWebpImageURL(filesHttpURI) : url;
						anchor = url.IsEquals(webpURL) ? anchor : anchorStart + webpURL + anchorEnd;
					}

					html = html.Substring(0, start) + anchor + html.Substring(end);
					offset = anchor.Length;
				}
				start = html.PositionOf("<figure class=\"image\"><a class=\"inline popup", start + offset);
			}

			// normalize tag of special handlers (Files HTTP)
			FilesHttpTags.ForEach(tag =>
			{
				var startOfTag = html.PositionOf($"<{tag.Name}");
				while (startOfTag > -1)
				{
					startOfTag = tag.SubName != null ? html.PositionOf($"<{tag.SubName}", startOfTag + 1) : startOfTag;
					var offset = 1;
					var endOfTag = html.PositionOf(">", startOfTag);
					if (endOfTag > startOfTag)
					{
						endOfTag += 1;
						var htmlTag = html.Substring(startOfTag, endOfTag - startOfTag);
						var startOfURL = htmlTag.PositionOf($"{tag.Attribute}=");
						if (startOfURL > 0)
						{
							startOfURL += tag.Attribute.Length + 1;
							var endOfURL = htmlTag.IndexOf("\"", startOfURL + 1);
							endOfURL = endOfURL < 0 ? htmlTag.IndexOf("'", startOfURL + 1) : endOfURL;
							if (endOfURL > 0)
							{
								var url = htmlTag.Substring(startOfURL, endOfURL - startOfURL);
								var matched = FilesHttpMIMEs.Any(mime => url.IsContains("/files/") && url.IsContains($"/{mime.MIMEType}/"))
									? FilesHttpMIMEs.First(mime => url.IsContains("/files/") && url.IsContains($"/{mime.MIMEType}/"))
									: (null, null);
								if (matched.Handler != null && matched.MIMEType != null)
								{
									htmlTag = htmlTag.Replace(StringComparison.OrdinalIgnoreCase, "/files/", $"/{matched.Handler}/").Replace(StringComparison.OrdinalIgnoreCase, $"/{matched.MIMEType}/", "/");
									html = html.Substring(0, startOfTag) + htmlTag + html.Substring(endOfTag);
									offset = htmlTag.Length;
								}
							}
						}
					}
					startOfTag = html.PositionOf($"<{tag.Name}", startOfTag + offset);
				}
			});

			return html.HtmlDecode();
		}

		static IEnumerable<(string Name, string SubName, string Attribute)> FilesHttpTags { get; } =
		[
			("a", null, "href"),
			("img", null, "src"),
			("audio", "source", "src"),
			("video", "source", "src")
		];

		static IEnumerable<(string Handler, string MIMEType)> FilesHttpMIMEs { get; } =
		[
			("pngs", "image=png"),
			("jpgs", "image=jpeg"),
			("jpegs", "image=jpeg"),
			("mp3s", "audio=mp3"),
			("m4as", "audio=m4a"),
			("mp4s", "video=mp4"),
			("pdfs", "application=pdf"),
			("docs", "application=msword"),
			("docxs", "application=vnd.openxmlformats-officedocument.wordprocessingml.document")
		];

		static string NormalizeHTML(this string html, IBusinessObject @object)
		{
			var organization = @object.Organization as Organization;
			html = html.Replace(StringComparison.OrdinalIgnoreCase, $"{organization?.FakeFilesHttpURI ?? Utility.FilesHttpURI}/", "~~/");
			html = html.Replace(StringComparison.OrdinalIgnoreCase, $"{organization?.FakePortalsHttpURI ?? Utility.PortalsHttpURI}/", "~#/");
			html = html.Replace(StringComparison.OrdinalIgnoreCase, "~/File.ashx/", $"~~/files/{organization?.ID.ToLower()}/");
			html = html.Replace(StringComparison.OrdinalIgnoreCase, "\"/File.ashx/", $"\"~~/files/{organization?.ID.ToLower()}/");
			html = html.Replace(StringComparison.OrdinalIgnoreCase, "~/Image.ashx/", $"~~/files/{organization?.ID.ToLower()}/");
			html = html.Replace(StringComparison.OrdinalIgnoreCase, "\"/Image.ashx/", $"\"~~/files/{organization?.ID.ToLower()}/");
			html = html.Replace(StringComparison.OrdinalIgnoreCase, "~/Files/image=", $"~~/files/{organization?.ID.ToLower()}/image=");
			html = html.Replace(StringComparison.OrdinalIgnoreCase, "src=\"/files/", $"src=\"~~/files/");
			html = html.Replace(StringComparison.OrdinalIgnoreCase, "src=\"~/files/", $"src=\"~~/files/");
			html = html.Replace(StringComparison.OrdinalIgnoreCase, "~~~/files/", $"~~/files/");
			return html.NormalizeHTML(organization?.FakeFilesHttpURI);
		}

		static string NormalizeHTML(this string html, out Dictionary<string, (string Identifier, string Filename)> inlineImages)
		{
			inlineImages = new Dictionary<string, (string Identifier, string Filename)>();
			var start = html.PositionOf("<img");

			while (start > -1)
			{
				var offset = 1;
				var end = start < 0 ? -1 : html.PositionOf(">", start);

				if (end > -1)
				{
					end += 1;
					var image = html.Substring(start, end - start);
					image = image.EndsWith("/>") ? image : image.Replace(">", "/>");

					var urlStart = image.IndexOf("src=") + 5;
					var urlEnd = image.IndexOf("\"", urlStart + 1);
					if (urlEnd < 0)
						urlEnd = image.IndexOf("'", urlStart + 1);

					if (urlEnd > 0)
					{
						var imageStart = image.Substring(0, urlStart);
						var imageEnd = image.Substring(urlEnd);
						var url = image.Substring(urlStart, urlEnd - urlStart);
						if (url.IsStartsWith("data:image/"))
							try
							{
								if (!Utility.AllowInlineImages)
									url = "~~/thumbnails/no-image.png";
								else if (Utility.UploadInlineImages)
								{
									var data = url.ToArray();
									var contentType = data.First().ToArray(";").First().ToArray(":").Last();
									var identifier = UtilityService.NewUUID;
									var filename = $"img{inlineImages.Count + 1}-{DateTime.Now:HHmmssfff}-{identifier.Left(4)}.{contentType.ToArray("/").Last()}";
									File.WriteAllBytes(Path.Combine(Utility.TempFilesDirectory, filename), data.Last().Base64ToBytes());
									url = $"~~/files/[system-id]/{contentType.Replace("/", "=")}/{identifier}/{filename}";
									inlineImages.Add(url, (identifier, filename));
								}
								image = imageStart + url + imageEnd;
								html = html.Substring(0, start) + image + html.Substring(end);
							}
							catch { }
					}

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
		public static IBusinessObject NormalizeHTMLs(this IBusinessObject @object, out Dictionary<string, (string Identifier, string Filename)> inlineImages, Action<IBusinessObject> onCompleted = null)
		{
			// get entity definition
			var images = new Dictionary<string, (string Identifier, string Filename)>();
			var definition = RepositoryMediator.GetEntityDefinition(@object.GetType());

			// normalize
			if (definition != null)
			{
				// standard properties
				definition.Attributes.Where(attribute => attribute.IsCLOB != null && attribute.IsCLOB.Value).ForEach(attribute =>
				{
					var value = @object.GetAttributeValue<string>(attribute.Name);
					if (!string.IsNullOrWhiteSpace(value))
					{
						@object.SetAttributeValue(attribute.Name, value.NormalizeHTML(out var img));
						img.ForEach(kvp => images.Add(kvp.Key, kvp.Value));
					}
				});

				// extended properties
				if (@object.ExtendedProperties != null && definition.BusinessRepositoryEntities.TryGetValue(@object.RepositoryEntityID, out var repositiryEntity))
					repositiryEntity?.ExtendedPropertyDefinitions?.Where(propertyDefinition => propertyDefinition.Mode.Equals(ExtendedPropertyMode.LargeText)).ForEach(propertyDefinition =>
					{
						if (@object.ExtendedProperties.TryGetValue(propertyDefinition.Name, out var value) && value is string @string && !string.IsNullOrWhiteSpace(@string))
						{
							@object.ExtendedProperties[propertyDefinition.Name] = @string.NormalizeHTML(out var img);
							img.ForEach(kvp => images.Add(kvp.Key, kvp.Value));
						}
					});
			}

			// return the object
			inlineImages = images;
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
					var element = xml.Element(attribute.Name);
					if (!string.IsNullOrWhiteSpace(element?.Value))
						element.Value = element.Value.NormalizeHTML(@object);
				});

				// extended properties
				if (@object.ExtendedProperties != null && definition.BusinessRepositoryEntities.TryGetValue(@object.RepositoryEntityID, out var repositiryEntity))
					repositiryEntity?.ExtendedPropertyDefinitions?.Where(propertyDefinition => propertyDefinition.Mode.Equals(ExtendedPropertyMode.LargeText)).ForEach(propertyDefinition =>
					{
						var element = xml.Element(propertyDefinition.Name);
						if (!string.IsNullOrWhiteSpace(element?.Value))
							element.Value = element.Value.NormalizeHTML(@object);
					});
			}

			// return the xml
			onCompleted?.Invoke(xml);
			return xml;
		}

		internal static async Task<IBusinessObject> UploadInlineImagesAsync(this RequestInfo requestInfo, Dictionary<string, (string Identifier, string Filename)> inlineImages, IBusinessObject @object, CancellationToken cancellationToken)
		{
			// upload the images
			var isDebugLogEnabled = Utility.IsDebugLogEnabled || requestInfo.ContainsKey("x-logs");
			await inlineImages.ForEachAsync(async kvp =>
			{
				try
				{
					var fileInfo = new FileInfo(Path.Combine(Utility.TempFilesDirectory, kvp.Value.Filename));
					var contentType = fileInfo.GetMimeType();
					contentType = string.IsNullOrWhiteSpace(contentType) ? $"image/{(fileInfo.Extension.IsEquals(".jpg") ? "jpeg" : fileInfo.Extension.Right(fileInfo.Extension.Length - 1))}" : contentType;
					await fileInfo.UploadAsync($"{Utility.FilesHttpURI}/files{(isDebugLogEnabled ? "?x-logs=true" : "")}", new Dictionary<string, string>
					{
						["x-attachment-id"] = kvp.Value.Identifier,
						["x-attachment-content-type"] = contentType,
						["x-service-name"] = Utility.ServiceName.ToLower(),
						["x-object-name"] = @object.GetTypeName(true).ToLower(),
						["x-system-id"] = @object.SystemID,
						["x-entity"] = @object.RepositoryEntityID,
						["x-object-id"] = (@object as IPortalObject).ID,
						["x-object-title"] = (@object as IPortalObject).Title,
						["x-receive-mode"] = "file",
						["x-app-name"] = "NGX-Uploader",
						["x-correlation-id"] = requestInfo.CorrelationID,
						["x-temp-token"] = requestInfo.Session.User.GetAuthenticateToken(Utility.EncryptionKey, Utility.JWTKey)
					}, cancellationToken).ConfigureAwait(false);
					if (isDebugLogEnabled)
						await requestInfo.WriteLogAsync($"Upload an inline image successful [{@object.SystemID}/{kvp.Value.Identifier}-{kvp.Value.Filename}]", "Images").ConfigureAwait(false);
				}
				catch (Exception ex)
				{
					await requestInfo.WriteErrorAsync(ex, $"Error occurred while uploading an inline image [{@object.SystemID}/{kvp.Value.Identifier}-{kvp.Value.Filename}] => {ex.Message}", "Images").ConfigureAwait(false);
				}
			}, true, false).ConfigureAwait(false);

			// update the object
			var inlineURLs = inlineImages.Select(kvp => (Original: kvp.Key, Final: kvp.Key.Replace(StringComparison.OrdinalIgnoreCase, kvp.Key, kvp.Key.Replace("[system-id]", @object.SystemID)))).ToList();
			var definition = RepositoryMediator.GetEntityDefinition(@object.GetType());
			if (definition != null)
			{
				definition.Attributes.Where(attribute => attribute.IsCLOB != null && attribute.IsCLOB.Value).ForEach(attribute =>
				{
					var value = @object.GetAttributeValue<string>(attribute.Name);
					if (!string.IsNullOrWhiteSpace(value))
					{
						inlineURLs.ForEach(url => value = value.Replace(StringComparison.OrdinalIgnoreCase, url.Original, url.Final));
						@object.SetAttributeValue(attribute.Name, value);
					}
				});
				if (@object.ExtendedProperties != null && definition.BusinessRepositoryEntities.TryGetValue(@object.RepositoryEntityID, out var repositiryEntity))
					repositiryEntity?.ExtendedPropertyDefinitions?.Where(propertyDefinition => propertyDefinition.Mode.Equals(ExtendedPropertyMode.LargeText)).ForEach(propertyDefinition =>
					{
						if (@object.ExtendedProperties.TryGetValue(propertyDefinition.Name, out var value) && value is string @string && !string.IsNullOrWhiteSpace(@string))
						{
							inlineURLs.ForEach(url => @string = @string.Replace(StringComparison.OrdinalIgnoreCase, url.Original, url.Final));
							@object.ExtendedProperties[propertyDefinition.Name] = @string;
						}
					});
			}

			// return the object
			return @object;
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
			(organization.Sites ?? []).ForEach(site =>
			{
				domains.AddRange([site.Host, $"{site.SubDomain}.{site.PrimaryDomain}".Replace("*.", "")]);
				site.OtherDomains?.ToList(";").ForEach(domain =>
				{
					domains.Add(domain);
					if (domain.IsStartsWith("www."))
						domains.Add(domain.Replace("www.", ""));
				});
			});

			html = html.Replace($"{organization.FakePortalsHttpURI ?? Utility.PortalsHttpURI}/_", "~/_").Replace($"{Utility.PortalsHttpURI}/_", "~/_");
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
				return [];

			List<Role> roles;
			switch (privilegeRole)
			{
				case PrivilegeRole.Administrator:
					roles = privileges.AdministrativeRoles != null && privileges.AdministrativeRoles.Any()
						? await Role.FindAsync(Filters<Role>.Or(privileges.AdministrativeRoles.Select(roleID => Filters<Role>.Equals("ID", roleID))), null, 0, 1, null, cancellationToken).ConfigureAwait(false)
						: [];
					return roles.SelectMany(role => role.UserIDs ?? []).Concat(privileges.AdministrativeUsers ?? []).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

				case PrivilegeRole.Moderator:
					roles = privileges.ModerateRoles != null && privileges.ModerateRoles.Any()
						? await Role.FindAsync(Filters<Role>.Or(privileges.ModerateRoles.Select(roleID => Filters<Role>.Equals("ID", roleID))), null, 0, 1, null, cancellationToken).ConfigureAwait(false)
						: [];
					return roles.SelectMany(role => role.UserIDs ?? []).Concat(privileges.ModerateUsers ?? []).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

				case PrivilegeRole.Editor:
					roles = privileges.EditableRoles != null && privileges.EditableRoles.Any()
						? await Role.FindAsync(Filters<Role>.Or(privileges.EditableRoles.Select(roleID => Filters<Role>.Equals("ID", roleID))), null, 0, 1, null, cancellationToken).ConfigureAwait(false)
						: [];
					return roles.SelectMany(role => role.UserIDs ?? []).Concat(privileges.EditableUsers ?? []).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

				case PrivilegeRole.Contributor:
					roles = privileges.ContributiveRoles != null && privileges.ContributiveRoles.Any()
						? await Role.FindAsync(Filters<Role>.Or(privileges.ContributiveRoles.Select(roleID => Filters<Role>.Equals("ID", roleID))), null, 0, 1, null, cancellationToken).ConfigureAwait(false)
						: [];
					return roles.SelectMany(role => role.UserIDs ?? []).Concat(privileges.ContributiveUsers ?? []).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

				case PrivilegeRole.Viewer:
					roles = privileges.ViewableRoles != null && privileges.ViewableRoles.Any()
						? await Role.FindAsync(Filters<Role>.Or(privileges.ViewableRoles.Select(roleID => Filters<Role>.Equals("ID", roleID))), null, 0, 1, null, cancellationToken).ConfigureAwait(false)
						: [];
					return roles.SelectMany(role => role.UserIDs ?? []).Concat(privileges.ViewableUsers ?? []).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

				case PrivilegeRole.Downloader:
					roles = privileges.DownloadableRoles != null && privileges.DownloadableRoles.Any()
						? await Role.FindAsync(Filters<Role>.Or(privileges.DownloadableRoles.Select(roleID => Filters<Role>.Equals("ID", roleID))), null, 0, 1, null, cancellationToken).ConfigureAwait(false)
						: [];
					return roles.SelectMany(role => role.UserIDs ?? []).Concat(privileges.DownloadableUsers ?? []).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

				default:
					return [];
			}
		}

		internal static async Task<List<string>> GetRecipientsAsync(this IPortalObject @object, ApprovalStatus status, Organization organization = null, CancellationToken cancellationToken = default, IEnumerable<string> excluded = null)
		{
			organization = organization ?? await(@object.OrganizationID ?? "").GetOrganizationByIDAsync(cancellationToken).ConfigureAwait(false);
			var recipientIDs = new List<string>();
			switch (status)
			{
				case ApprovalStatus.Draft:
				case ApprovalStatus.Rejected:
					recipientIDs = @object is Form
						? await @object.WorkingPrivileges.GetUserIDsAsync(PrivilegeRole.Editor, cancellationToken).ConfigureAwait(false)
						: [@object.CreatedID];
					break;

				case ApprovalStatus.Pending:
					recipientIDs = await @object.WorkingPrivileges.GetUserIDsAsync(PrivilegeRole.Editor, cancellationToken).ConfigureAwait(false);
					if (!recipientIDs.Any())
						recipientIDs = await @object.WorkingPrivileges.GetUserIDsAsync(PrivilegeRole.Moderator, cancellationToken).ConfigureAwait(false);
					if (!recipientIDs.Any())
						recipientIDs = await @object.WorkingPrivileges.GetUserIDsAsync(PrivilegeRole.Administrator, cancellationToken).ConfigureAwait(false);
					if (!recipientIDs.Any())
						recipientIDs = [organization.OwnerID];
					break;

				case ApprovalStatus.Approved:
					recipientIDs = await @object.WorkingPrivileges.GetUserIDsAsync(PrivilegeRole.Moderator, cancellationToken).ConfigureAwait(false);
					if (!recipientIDs.Any())
						recipientIDs = await @object.WorkingPrivileges.GetUserIDsAsync(PrivilegeRole.Administrator, cancellationToken).ConfigureAwait(false);
					if (!recipientIDs.Any())
						recipientIDs = [organization.OwnerID];
					break;

				case ApprovalStatus.Published:
					recipientIDs = (await @object.WorkingPrivileges.GetUserIDsAsync(PrivilegeRole.Moderator, cancellationToken).ConfigureAwait(false))
						.Concat(await @object.WorkingPrivileges.GetUserIDsAsync(PrivilegeRole.Administrator, cancellationToken).ConfigureAwait(false))
						.Concat([@object.CreatedID]).ToList();
					if (!recipientIDs.Any())
						recipientIDs = [organization.OwnerID];
					break;

				case ApprovalStatus.Archieved:
					recipientIDs = (await @object.WorkingPrivileges.GetUserIDsAsync(PrivilegeRole.Moderator, cancellationToken).ConfigureAwait(false))
						.Concat(await @object.WorkingPrivileges.GetUserIDsAsync(PrivilegeRole.Administrator, cancellationToken).ConfigureAwait(false))
						.Concat([@object.CreatedID, @object.LastModifiedID]).ToList();
					if (!recipientIDs.Any())
						recipientIDs = [organization.OwnerID];
					break;
			}
			return (excluded != null && excluded.Any() ? recipientIDs.Except(excluded) : recipientIDs).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
		}


		internal static Task WriteLogsAsync(string developerID, string appID, string objectName, List<string> logs, Exception exception = null, string correlationID = null, string additional = null)
		{
			// prepare
			correlationID = correlationID ?? UtilityService.NewUUID;
			var wampDetails = exception != null && exception is WampException wampException
				? wampException.GetDetails()
				: (0, null, null, null, null, null);

			logs = logs ?? new List<string>();
			if (wampDetails.Code > 0)
			{
				logs.Add($"> Message: {wampDetails.Message}");
				logs.Add($"> Type: {wampDetails.Type}");
			}
			else if (exception != null)
			{
				logs.Add($"> Message: {exception.Message}");
				logs.Add($"> Type: {exception.GetTypeName(true)}");
			}

			if (!string.IsNullOrWhiteSpace(additional))
				logs.Add(additional);

			var stack = wampDetails.Code > 0
				? $"{wampDetails.Type}: {wampDetails.Message}\r\n{wampDetails.Stack}"
				: exception?.GetStack();

			// update queue & write to centerlized logs
			Utility.Logs.Enqueue(((DateTime.Now, correlationID, developerID, appID, ServiceBase.ServiceComponent.NodeID, Utility.ServiceName, objectName), logs, stack));
			return Utility.Logs.WriteLogsAsync(Utility.Logger);
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

		internal static string RemoveURITrail(this string uri, string trail = "/")
		{
			uri ??= "";
			trail = string.IsNullOrWhiteSpace(trail) ? trail = "/" : trail;
			while (uri.EndsWith(trail))
				uri = uri.Left(uri.Length - trail.Length);
			return uri;
		}

		internal static bool IsAdministrator(this IUser user, Privileges privileges, Privileges parentPrivileges, Organization organization, bool checkAsOwner = true)
			=> (checkAsOwner && user.ID.IsEquals(organization?.OwnerID)) || user.IsAdministrator(privileges, parentPrivileges ?? organization?.WorkingPrivileges);

		internal static bool IsModerator(this IUser user, Privileges privileges, Privileges parentPrivileges, Organization organization, bool checkAsOwner = true)
			=> (checkAsOwner && user.ID.IsEquals(organization?.OwnerID)) || user.IsModerator(privileges, parentPrivileges ?? organization?.WorkingPrivileges);

		internal static bool IsEditor(this IUser user, Privileges privileges, Privileges parentPrivileges, Organization organization, bool checkAsOwner = true)
			=> (checkAsOwner && user.ID.IsEquals(organization?.OwnerID)) || user.IsEditor(privileges, parentPrivileges ?? organization?.WorkingPrivileges);

		internal static bool IsContributor(this IUser user, Privileges privileges, Privileges parentPrivileges, Organization organization, bool checkAsOwner = true)
			=> (checkAsOwner && user.ID.IsEquals(organization?.OwnerID)) || user.IsContributor(privileges, parentPrivileges ?? organization?.WorkingPrivileges);

		internal static bool IsViewer(this IUser user, Privileges privileges, Privileges parentPrivileges, Organization organization, bool checkAsOwner = true)
			=> (checkAsOwner && user.ID.IsEquals(organization?.OwnerID)) || user.IsViewer(privileges, parentPrivileges ?? organization?.WorkingPrivileges);

		internal static bool IsDownloader(this IUser user, Privileges privileges, Privileges parentPrivileges, Organization organization, bool checkAsOwner = true)
			=> (checkAsOwner && user.ID.IsEquals(organization?.OwnerID)) || user.IsDownloader(privileges, parentPrivileges ?? organization?.WorkingPrivileges);

		internal static List<string> IsIn(this Privileges privileges, IUser user, string mode)
		{
			var roles = mode == "Download" || mode == "View"
				? (privileges.DownloadableRoles ?? []).Concat(privileges.ViewableUsers ?? []).Concat(privileges.ContributiveRoles ?? []).Concat(privileges.EditableRoles ?? []).Concat(privileges.ModerateRoles ?? []).Concat(privileges.AdministrativeRoles ?? [])
				: mode == "Contribute"
					? (privileges.ContributiveRoles ?? []).Concat(privileges.EditableRoles ?? []).Concat(privileges.ModerateRoles ?? []).Concat(privileges.AdministrativeRoles ?? [])
					: mode == "Edit"
						? (privileges.EditableRoles ?? []).Concat(privileges.ModerateRoles ?? []).Concat(privileges.AdministrativeRoles ?? [])
						: mode == "Moderate"
							? (privileges.ModerateRoles ?? []).Concat(privileges.AdministrativeRoles ?? [])
							: privileges.AdministrativeRoles ?? [];
			var users = mode == "Download" || mode == "View"
				? (privileges.DownloadableUsers ?? []).Concat(privileges.ViewableUsers ?? []).Concat(privileges.ContributiveUsers ?? []).Concat(privileges.EditableUsers ?? []).Concat(privileges.ModerateUsers ?? []).Concat(privileges.AdministrativeUsers ?? [])
				: mode == "Contribute"
					? (privileges.ContributiveUsers ?? []).Concat(privileges.EditableUsers ?? []).Concat(privileges.ModerateUsers ?? []).Concat(privileges.AdministrativeUsers ?? [])
					: mode == "Edit"
						? (privileges.EditableUsers ?? []).Concat(privileges.ModerateUsers ?? []).Concat(privileges.AdministrativeUsers ?? [])
						: mode == "Moderate"
							? (privileges.ModerateUsers ?? []).Concat(privileges.AdministrativeUsers ?? [])
							: privileges.AdministrativeUsers ?? [];
			return roles.Intersect(user?.Roles ?? []).Concat(user != null ? users.Intersect([user.ID]) : []).ToList();
		}

		internal static JObject UpdateVersions(this JObject json, List<VersionContent> versions, Action<JObject> onCompleted = null)
		{
			if (versions != null)
			{
				json["Versions"] = versions.Select(version => version.ToJson(jtoken => (jtoken as JObject).Remove("Data"))).ToJArray();
				json["TotalVersions"] = versions.Count;
			}
			onCompleted?.Invoke(json);
			return json;
		}

		internal static IFilterBy<T> GetFilterBy<T>(this Expression expression) where T : class
			=> expression.JSONs.Filter?.ToFilter<T>();

		internal static SortBy<T> GetSortBy<T>(this Expression expression) where T : class
			=> expression.JSONs.Sort?.ToSort<T>();

		public static WebHookMessage ToWebHookMessage(this RequestInfo requestInfo, WebHookSetting settings, string organizationID, bool doValidation = true, Action<WebHookMessage> onCompleted = null)
			=> requestInfo.ToWebHookMessage(settings.SecretToken, settings.SecretTokenName, settings.SignAlgorithm, settings.SignKey ?? requestInfo?.GetAppID() ?? requestInfo?.GetDeveloperID() ?? organizationID, settings.SignKeyIsHex, settings.SignatureName, settings.SignatureAsHex, settings.SignaturePrefix, settings.SignatureSuffix, settings.SignWithTimestamp, settings.SignWithTimestampName, settings.SignWithTimestampConnect, settings.QueryAsJson?.ToDictionary<string>(), settings.HeaderAsJson?.ToDictionary<string>(), settings.EncryptionKey?.HexToBytes(), settings.EncryptionIV?.HexToBytes(), doValidation, onCompleted);

		public static WebHookMessage Normalize(this WebHookMessage message, string secretToken, string secretTokenName, WebHook settings, RequestInfo requestInfo, string organizationID, bool signatureInQuery = false)
			=> message.Normalize(secretToken, secretTokenName, settings.SignAlgorithm, settings.SignKey ?? requestInfo?.GetAppID() ?? requestInfo?.GetDeveloperID() ?? organizationID, settings.SignKeyIsHex, settings.SignatureName, settings.SignatureAsHex, signatureInQuery, settings.SignaturePrefix, settings.SignatureSuffix, settings.SignWithTimestamp, settings.SignWithTimestampName, settings.SignWithTimestampConnect, settings.QueryAsJson?.ToDictionary<string>(), settings.HeaderAsJson?.ToDictionary<string>(), settings.EncryptionKey?.HexToBytes(), settings.EncryptionIV?.HexToBytes());

		public static WebHookMessage Normalize(this WebHookMessage message, WebHookNotification settings, RequestInfo requestInfo, string organizationID)
			=> message.Normalize(null, null, settings, requestInfo, organizationID, settings.SignatureInQuery);

		public static WebHookMessage Normalize(this WebHookMessage message, WebHookSetting settings, RequestInfo requestInfo, string organizationID)
			=> message.Normalize(settings.SecretToken, settings.SecretTokenName, settings, requestInfo, organizationID);

		static Utility()
		{
			SixLabors.ImageSharp.Configuration.Default.MemoryAllocator = MemoryAllocator.Create(new MemoryAllocatorOptions
			{
				MaximumPoolSizeMegabytes = 128
			});
		}

		static SixLabors.ImageSharp.Formats.IImageEncoder WebpEncoder { get; } = new SixLabors.ImageSharp.Formats.Webp.WebpEncoder();

		static SixLabors.ImageSharp.Formats.IImageEncoder WebpAdvancedEncoder { get; } = new SixLabors.ImageSharp.Formats.Webp.WebpEncoder
		{
			FileFormat = SixLabors.ImageSharp.Formats.Webp.WebpFileFormatType.Lossy,
			Method = Enum.TryParse<SixLabors.ImageSharp.Formats.Webp.WebpEncodingMethod>(UtilityService.GetAppSetting("Portals:WebP:Method", "Default"), out var method)
				? method
				: SixLabors.ImageSharp.Formats.Webp.WebpEncodingMethod.Default,
			Quality = 70,
			NearLosslessQuality = 60
		};

		internal static async Task<byte[]> ToWebPAsync(this byte[] data, bool isPNG, CancellationToken cancellationToken)
		{
			using var webpStream = UtilityService.CreateMemoryStream();
			using var imageStream = data.ToMemoryStream();
			using var imageObject = await SixLabors.ImageSharp.Image.LoadAsync(imageStream, cancellationToken).ConfigureAwait(false);
			imageObject.Metadata.ExifProfile = null;
			imageObject.Metadata.IccProfile = null;
			imageObject.Metadata.XmpProfile = null;
			imageObject.Metadata.IptcProfile = null;
			if (isPNG)
				await imageObject.SaveAsync(webpStream, WebpEncoder, cancellationToken).ConfigureAwait(false);
			else
			{
				imageObject.Mutate(op => op.AutoOrient());
				if (imageObject.PixelType.BitsPerPixel != 24)
				{
					using var rgbImage = imageObject.CloneAs<SixLabors.ImageSharp.PixelFormats.Rgb24>();
					await rgbImage.SaveAsync(webpStream, WebpAdvancedEncoder, cancellationToken).ConfigureAwait(false);
				}
			}
			return webpStream.ToBytes();
		}

		internal static IEnumerable<string> GetPaginatingURLs(this string url, int totalPages, string suffix = "")
			=> url.IsContains("/{{pageNumber}}")
				? Enumerable.Range(1, totalPages > 0 ? totalPages : Utility.RefreshMaxPage).Select(pageNumber => url.Replace("/{{pageNumber}}", pageNumber > 1 ? $"/{pageNumber}{suffix}" : suffix, StringComparison.OrdinalIgnoreCase))
				: new[] { url };

		internal static string GetURLPath(this Uri uri)
			=> $"{uri.Scheme}://{uri.Host}{uri.AbsolutePath}";

		internal static async Task ProcessWebHookTriggerAsync(this RequestInfo requestInfo, string url, string body, Func<string, Task> trackAsync = null)
		{
			using var httpResponseMessage = await new Uri(url).SendHttpRequestAsync(string.IsNullOrWhiteSpace(body) ? "GET" : "POST", new Dictionary<string, string>
			{
				["Content-Type"] = "application/json",
				["X-Original-Correlation-ID"] = requestInfo.CorrelationID,
				["X-WebHook-Trigger-SHA256-Hmac"] = (string.IsNullOrWhiteSpace(body) ? "None" : body).GetHMACSHA256Hash(Utility.ValidationKey).ToHex(),
				["X-WebHook-Trigger-SHA256-Hash"] = (string.IsNullOrWhiteSpace(body) ? "None" : body).GetSHA256Hash().ToHex()
			}, body, 300, Utility.CancellationToken).ConfigureAwait(false);
			await (trackAsync == null ? Task.CompletedTask : trackAsync($"Send to trigger URL successful [{url}] => {await httpResponseMessage.ReadAsStringAsync(Utility.CancellationToken).ConfigureAwait(false)}")).ConfigureAwait(false);
		}

		internal static JObject ToCursor(this JObject json, Action<JObject> onCompleted = null)
		{
			var pagination = json.Get<JObject>("Pagination");
			var objects = json.Get<JArray>("Objects");
			var (_, totalPages, _, pageNumber) = pagination.GetPagination();
			var cursor = new JObject
			{
				["Objects"] = objects
			};
			if (objects != null && objects.Count > 0 && totalPages > 0 && totalPages > pageNumber)
				cursor["Cursor"] = new JObject
				{
					["FilterBy"] = json.Get<JObject>("FilterBy"),
					["SortBy"] = json.Get<JObject>("SortBy"),
					["Pagination"] = pagination
				}.ToString(Newtonsoft.Json.Formatting.None).ToBase64Url();
			onCompleted?.Invoke(cursor);
			return cursor;
		}

		public static AliasKey GetOrganiztionAliasKey(this string alias)
			=> new AliasKey(AliasTypes.Organization, null, alias);

		public static AliasKey GetSiteAliasKey(this string domain)
			=> new AliasKey(AliasTypes.Site, null, domain);

		public static AliasKey GetDesktopAliasKey(this string systemID, string alias)
			=> new AliasKey(AliasTypes.Desktop, systemID, alias);

		public static AliasKey GetCategoryAliasKey(this string repositoryEntityID, string alias)
			=> new AliasKey(AliasTypes.Category, repositoryEntityID, alias);
	}

	//  --------------------------------------------------------------------------------------------

	/// <summary>
	/// Presents the alias key
	/// </summary>
	public readonly struct AliasKey : IEquatable<AliasKey>
	{
		public readonly byte Type;
		public readonly string Scope;
		public readonly string Value;
		readonly int _hash;

		public AliasKey(byte type, string scope, string value)
		{
			this.Type = type;
			this.Scope = scope;
			this.Value = value;
			unchecked
			{
				int hash = type;
				if (scope != null)
					hash = (hash * 397) ^ StringComparer.OrdinalIgnoreCase.GetHashCode(scope);
				if (value != null)
					hash = (hash * 397) ^ StringComparer.OrdinalIgnoreCase.GetHashCode(value);
				this._hash = hash;
			}
		}

		public bool Equals(AliasKey other)
			=> this._hash != other._hash || this.Type != other.Type
				? false
				: string.Equals(this.Scope, other.Scope, StringComparison.OrdinalIgnoreCase) && string.Equals(this.Value, other.Value, StringComparison.OrdinalIgnoreCase);

		public override bool Equals(object obj)
			=> obj is AliasKey other && this.Equals(other);

		public override int GetHashCode()
			=> this._hash;

		public override string ToString()
			=> this.Scope == null ? $"{Type}:{Value}"  : $"{Type}:{Scope}:{Value}";
	}

	/// <summary>
	/// Presents type of an alias
	/// </summary>
	public static class AliasTypes
	{
		public const byte Organization = 1;
		public const byte Site = 2;
		public const byte Desktop = 3;
		public const byte Category = 4;
	}

	//  --------------------------------------------------------------------------------------------

	[Repository(ServiceName = "Portals", ID = "A0000000000000000000000000000001", Title = "CMS", Description = "Services of the CMS Portals", Directory = "cms", ExtendedPropertiesTableName = "T_Portals_Extended_Properties")]
	public abstract class Repository<T> : RepositoryBase<T> where T : class { }

}
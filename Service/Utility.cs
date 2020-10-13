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
		/// Gets the path to the directory that contains all temporary files
		/// </summary>
		public static string TempFilesDirectory { get; internal set; }

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
		/// Gets the object name for working with real-time update messages
		/// </summary>
		/// <param name="definition"></param>
		/// <returns></returns>
		public static string GetObjectName(this ContentTypeDefinition definition)
			=> $"{(string.IsNullOrWhiteSpace(definition.ObjectNamePrefix) ? "" : definition.ObjectNamePrefix)}{definition.ObjectName}{(string.IsNullOrWhiteSpace(definition.ObjectNameSuffix) ? "" : definition.ObjectNameSuffix)}";

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
		/// Gets a pagination URL
		/// </summary>
		/// <param name="urlPattern"></param>
		/// <param name="pageNumber"></param>
		/// <returns></returns>
		public static string GetPaginationURL(this string urlPattern, int pageNumber)
		{
			var url = (urlPattern ?? "").Format(new Dictionary<string, object> { ["pageNumber"] = pageNumber }).Replace("/1.html", ".html");
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
							{ "URL", urlPattern.GetPaginationURL(page) }
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
						{ "URL", urlPattern.GetPaginationURL(1) }
					});
					if (start - 1 > 1)
						pages.Add(new JObject
						{
							{ "Text", start - 1 > 2 ? "..." : $"{start - 1}" },
							{ "URL", urlPattern.GetPaginationURL(start - 1) }
						});
					for (var page = start; page <= end; page++)
						pages.Add(new JObject
						{
							{ "Text", $"{page}" },
							{ "URL", urlPattern.GetPaginationURL(page) }
						});
					if (end + 1 < totalPages)
						pages.Add(new JObject
						{
							{ "Text", end + 1 < totalPages ? "..." : $"{end + 1}" },
							{ "URL", urlPattern.GetPaginationURL(end + 1) }
						});
					pages.Add(new JObject
					{
						{ "Text", $"{totalPages}" },
						{ "URL", urlPattern.GetPaginationURL(totalPages) }
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

		internal static Task WriteErrorAsync(this RequestInfo requestInfo, Exception exception, CancellationToken cancellationToken = default, string message = null, string objectName = null, string additionnal = null)
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
			if (!string.IsNullOrWhiteSpace(additionnal))
				logs.Add(additionnal);
			return Utility.LoggingService.WriteLogsAsync(requestInfo.CorrelationID, requestInfo.Session.DeveloperID, requestInfo.Session.AppID, ServiceBase.ServiceComponent.ServiceName, objectName ?? "Notifications", logs, exception.GetStack(), cancellationToken);
		}

		internal static Task WriteLogAsync(this RequestInfo requestInfo, string log, CancellationToken cancellationToken = default, string objectName = null)
			=> Utility.LoggingService.WriteLogAsync(requestInfo.CorrelationID, requestInfo.Session.DeveloperID, requestInfo.Session.AppID, ServiceBase.ServiceComponent.ServiceName, objectName ?? "Notifications", log, null, cancellationToken);

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

		internal static string MinifyJs(this string data)
			=> new Minifier().MinifyJs(data);

		internal static string MinifyCss(this string data)
			=> new Minifier().MinifyCss(data);

		internal static string MinifyHtml(this string data)
			=> UtilityService.RemoveWhitespaces(data).Replace("\r", "").Replace("\n\t", "").Replace("\t", "");
	}

	//  --------------------------------------------------------------------------------------------

	[Serializable]
	[Repository(ServiceName = "Portals", ID = "A0000000000000000000000000000001", Title = "CMS", Description = "Services of the CMS Portals", Directory = "CMS", ExtendedPropertiesTableName = "T_Portals_Extended_Properties")]
	public abstract class Repository<T> : RepositoryBase<T> where T : class { }
}
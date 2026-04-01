#region Related components
using System;
using System.Linq;
using System.Diagnostics;
using System.Collections.Generic;
using System.Reactive.Concurrency;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using net.vieapps.Components.Caching;
using net.vieapps.Components.Repository;
using net.vieapps.Components.Security;
using net.vieapps.Components.Utility;
#endregion

namespace net.vieapps.Services.Portals
{
	public static partial class Utility
	{
		public static Cache Cache { get; } = Cache.CreateInstance("VIEApps-Services-Portals", Components.Utility.Logger.GetLoggerFactory(), "true".IsEquals(UtilityService.GetAppSetting("Portals:Cache:L1")));

		public static bool IsCacheDisabled { get; internal set; } = "true".IsEquals(UtilityService.GetAppSetting("Portals:Cache:Disabled"));

		internal static string CloudFlareZoneID { get; set; } = UtilityService.GetAppSetting("Portals:CloudFlare:ZoneID");

		internal static string CloudFlareApiToken { get; set; } = UtilityService.GetAppSetting("Portals:CloudFlare:ApiToken");

		internal static bool CloudFlareForAll { get; set; } = !string.IsNullOrWhiteSpace(CloudFlareZoneID) && !string.IsNullOrWhiteSpace(CloudFlareApiToken) && "true".IsEquals(UtilityService.GetAppSetting("Portals:CloudFlare:All"));

		public static bool CloudFlarePurgeEverythingOnObject { get; internal set; } = "true".IsEquals(UtilityService.GetAppSetting("Portals:CloudFlare:PurgeEverythingOnObject"));

		internal static string RefresherURL { get; set; } = UtilityService.GetAppSetting("Portals:Refresh:ReferURL", "https://vieapps.net/~url.refresher");

		internal static int RefreshTimeout { get; set; } = Int32.TryParse(UtilityService.GetAppSetting("Portals:Refresh:Timeout"), out var value) && value > 0 ? value : 15;

		internal static int RefreshMaxPage { get; set; } = Int32.TryParse(UtilityService.GetAppSetting("Portals:Refresh:MaxPage"), out var value) && value > 0 ? value : 5;

		internal static int RefreshMaxPageOnMonday { get; set; } = Int32.TryParse(UtilityService.GetAppSetting("Portals:Refresh:MaxPage:Monday"), out var value) && value > 0 ? value : 20;

		internal static DateTime RefreshMinTime => DateTime.Now.AddDays(0 - (Int32.TryParse(UtilityService.GetAppSetting("Portals:Refresh:MaxDay"), out var value) && value > 0 ?  value : 15));

		internal static DateTime RefreshMinTimeOnMonday => DateTime.Now.AddDays(0 - (Int32.TryParse(UtilityService.GetAppSetting("Portals:Refresh:MaxDay:Monday"), out var value) && value > 0 ? value : 90));

		internal static Dictionary<string, string> RefresherHeaders => new()
		{
			["AllowAutoRedirect"] = "true",
			["Referer"] = Utility.RefresherURL,
			["User-Agent"] = $"{UtilityService.DesktopUserAgent} NGX-Refresher/{typeof(DesktopProcessor).Assembly.GetVersion(false)}"
		};

		/// <summary>
		/// Gets the key for storing a set of keys that belong to an organization
		/// </summary>
		/// <param name="organization"></param>
		/// <param name="suffix"></param>
		/// <returns></returns>
		public static string GetSetCacheKey(this Organization organization, string suffix)
			=> $"Set:{organization.ID}:{suffix}";

		/// <summary>
		/// Gets the key for storing a set of keys that related to a content-type
		/// </summary>
		/// <param name="contentType"></param>
		/// <param name="suffix"></param>
		/// <returns></returns>
		public static string GetSetCacheKey(this ContentType contentType, string suffix = null)
			=> contentType.Organization.GetSetCacheKey($"ContentType:{contentType.ID}{suffix}");

		/// <summary>
		/// Gets the key for storing a set of keys that related to a desktop
		/// </summary>
		/// <param name="desktop"></param>
		/// <returns></returns>
		public static string GetSetCacheKey(this Desktop desktop)
			=> desktop.Organization.GetSetCacheKey($"Desktop:{desktop.ID}");

		/// <summary>
		/// Gets the key for storing a set of keys that related to a category
		/// </summary>
		/// <param name="desktop"></param>
		/// <returns></returns>
		public static string GetSetCacheKey(this Category category, string suffix = null)
			=> category.Organization.GetSetCacheKey($"Category:{category.ID}{(string.IsNullOrWhiteSpace(suffix) ? "" : $":{suffix}")}");

		/// <summary>
		/// Gets the set of keys that used to store HTML cache of this desktop
		/// </summary>
		/// <param name="desktop"></param>
		/// <param name="cancellationToken"></param>
		/// <param name="staticIncluded"></param>
		/// <returns></returns>
		public static async Task<List<string>> GetSetCacheKeysAsync(this Desktop desktop, CancellationToken cancellationToken = default, bool includeStaticResources = false)
			=> (includeStaticResources ? [$"css#d_{desktop.ID}", $"css#d_{desktop.ID}:time", $"js#d_{desktop.ID}", $"js#d_{desktop.ID}:time"] : Array.Empty<string>())
				.Concat(await Utility.Cache.GetSetMembersAsync(desktop.GetSetCacheKey(), cancellationToken).ConfigureAwait(false) ?? [])
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.ToList();

		/// <summary>
		/// Gets the set of keys that used to store HTML cache of this collection of desktops
		/// </summary>
		/// <param name="desktops"></param>
		/// <param name="cancellationToken"></param>
		/// <param name="staticIncluded"></param>
		/// <returns></returns>
		public static async Task<List<string>> GetSetCacheKeysAsync(this IEnumerable<Desktop> desktops, CancellationToken cancellationToken = default, bool includeStaticResources = false)
		{
			var keys = new List<string>();
			if (desktops != null)
				await desktops.Where(desktop => desktop != null).ForEachAsync(async desktop => keys = keys.Concat(await desktop.GetSetCacheKeysAsync(cancellationToken, includeStaticResources).ConfigureAwait(false) ?? []).ToList(), true, false).ConfigureAwait(false);
			return keys.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
		}

		/// <summary>
		/// Gets the set of keys that used to store HTML cache that related to this expression
		/// </summary>
		/// <param name="expression"></param>
		/// <param name="cancellationToken"></param>
		/// <returns></returns>
		public static async Task<List<string>> GetSetCacheKeysAsync(this Expression expression, CancellationToken cancellationToken = default)
		{
			var keys = new List<string>();
			var filter = Filters<Portlet>.And(Filters<Portlet>.Equals("ExpressionID", expression.ID), Filters<Portlet>.IsNull("OriginalPortletID"));
			var sort = Sorts<Portlet>.Ascending("DesktopID").ThenByAscending("Zone").ThenByAscending("OrderIndex");
			var portlets = await Portlet.FindAsync(filter, sort, cancellationToken).ConfigureAwait(false);
			await portlets.Where(portlet => portlet != null).ForEachAsync(async portlet =>
			{
				var dekstops = await portlet.GetDesktopsAsync(cancellationToken).ConfigureAwait(false);
				keys = keys.Concat(await dekstops.GetSetCacheKeysAsync(cancellationToken).ConfigureAwait(false) ?? []).ToList();
			}, true, false).ConfigureAwait(false);
			return keys.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
		}

		/// <summary>
		/// Gets the set of keys that used to store HTML cache that related to this content-type
		/// </summary>
		/// <param name="contentType"></param>
		/// <param name="cancellationToken"></param>
		/// <returns></returns>
		public static async Task<List<string>> GetSetCacheKeysAsync(this ContentType contentType, CancellationToken cancellationToken = default)
		{
			var keys = new List<string>();
			var filter = Filters<Portlet>.And(Filters<Portlet>.Equals("RepositoryEntityID", contentType.ID), Filters<Portlet>.IsNull("OriginalPortletID"));
			var sort = Sorts<Portlet>.Ascending("DesktopID").ThenByAscending("Zone").ThenByAscending("OrderIndex");
			var portlets = await Portlet.FindAsync(filter, sort, cancellationToken).ConfigureAwait(false);
			await portlets.Where(portlet => portlet != null).ForEachAsync(async portlet =>
			{
				var dekstops = await portlet.GetDesktopsAsync(cancellationToken).ConfigureAwait(false);
				keys = keys.Concat(await dekstops.GetSetCacheKeysAsync(cancellationToken).ConfigureAwait(false) ?? []).ToList();
			}, true, false).ConfigureAwait(false);
			return keys.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
		}

		/// <summary>
		/// Gets the set of keys that used to store HTML cache that related to this site
		/// </summary>
		/// <param name="site"></param>
		/// <param name="cancellationToken"></param>
		/// <returns></returns>
		public static async Task<List<string>> GetSetCacheKeysAsync(this Site site, CancellationToken cancellationToken = default)
		{
			var theme = site.WorkingTheme ?? "defaut";
			return new[] { "css#defaut", "css#defaut:time", "js#defaut", "js#defaut:time", $"css#{theme}", $"css#{theme}:time", $"js#{theme}", $"js#{theme}:time", $"css#s_{site.ID}", $"css#s_{site.ID}:time", $"js#s_{site.ID}", $"js#s_{site.ID}:time" }
				.Concat(await Utility.Cache.GetSetMembersAsync($"statics:{theme}", cancellationToken).ConfigureAwait(false) ?? [])
				.Concat(site.Organization != null ? await site.Organization.GetSetCacheKeysAsync(cancellationToken).ConfigureAwait(false) : [])
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.ToList();
		}

		/// <summary>
		/// Gets the set of keys that used to store HTML cache that related to this organization
		/// </summary>
		/// <param name="organization"></param>
		/// <param name="cancellationToken"></param>
		/// <returns></returns>
		public static async Task<List<string>> GetSetCacheKeysAsync(this Organization organization, CancellationToken cancellationToken = default)
		{
			var theme = organization.Theme ?? "defaut";
			return new[] { "css#defaut", "css#defaut:time", "js#defaut", "js#defaut:time", $"css#{theme}", $"css#{theme}:time", $"js#{theme}", $"js#{theme}:time", $"js#o_{organization.ID}", $"js#o_{organization.ID}:time" }
				.Concat(await Utility.Cache.GetSetMembersAsync($"statics:{theme}", cancellationToken).ConfigureAwait(false) ?? [])
				.Concat(await Utility.Cache.GetSetMembersAsync("statics", cancellationToken).ConfigureAwait(false) ?? [])
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.ToList();
		}

		/// <summary>
		/// Gets the key for storing HTML code of a desktop that specified by alias and requested URL
		/// </summary>
		/// <param name="desktop"></param>
		/// <param name="requestURI"></param>
		/// <param name="site"></param>
		/// <returns></returns>
		public static string GetDesktopCacheKey(this Desktop desktop, Uri requestURI, Site site = null)
		{
			var organization = desktop.Organization;
			var path = desktop.Alias.IsEquals("-default") || desktop.ID.IsEquals((site?.HomeDesktop ?? organization.HomeDesktop)?.ID) ? "-default" : null;
			if (path == null)
			{
				path = requestURI.AbsolutePath.ToLower();
				while (path.EndsWith("/") || path.EndsWith("."))
					path = path.Left(path.Length - 1).Trim();
				path = path.IsStartsWith($"/~{organization.Alias}") ? path.Right(path.Length - organization.Alias.Length - 2) : path;
				path = path.IsEndsWith("/default.aspx") ? path.Left(path.Length - 13) : path;
				path = path.IsEndsWith(".html") || path.IsEndsWith(".aspx") ? path.Left(path.Length - 5) : path.IsEndsWith(".php") ? path.Left(path.Length - 4) : path;
				if (path.Equals("") || path.Equals("/") || path.Equals("/index") || path.Equals("/default"))
					path = "-default";
				else
				{
					path = $"/{desktop.Alias}/{path.ToArray("/", true).Skip(1).Join("/")}";
					while (path.EndsWith('/'))
						path = path.Left(path.Length - 1);
				}
			}
			return organization.ID + (site == null || string.IsNullOrWhiteSpace(site.ID) || site.ID.IsEquals(organization.DefaultSite?.ID) ? "" : ":" + site.ID) + ":" + path.GenerateUUID();
		}

		/// <summary>
		/// Gets the key for storing HTML code of a desktop that specified by alias and requested URL
		/// </summary>
		/// <param name="desktop"></param>B
		/// <param name="requestURL"></param>
		/// <param name="site"></param>
		/// <returns></returns>
		public static string GetDesktopCacheKey(this Desktop desktop, string requestURL, Site site = null)
			=> desktop.GetDesktopCacheKey(new Uri(requestURL.IsStartsWith("http://") || requestURL.IsStartsWith("https://") ? requestURL : "https://site.vieapps.net/" + (requestURL.Equals("#") ? "" : requestURL.Replace("~/", ""))), site);

		/// <summary>
		/// Gets all the keys for storing HTML code of a desktop that specified by alias and requested URL
		/// </summary>
		/// <param name="desktop"></param>
		/// <param name="requestURL"></param>
		/// <param name="site"></param>
		/// <returns></returns>
		public static List<string> GetDesktopCacheKeys(this Desktop desktop, string requestURL, Site site = null)
		{
			var cacheKey = desktop.GetDesktopCacheKey(requestURL, site);
			return [cacheKey, $"{cacheKey}:time", $"{cacheKey}:expiration"];
		}

		/// <summary>
		/// Gets all the keys for storing HTML code of home desktops (of all sites)
		/// </summary>
		/// <param name="organization"></param>
		/// <returns></returns>
		internal static List<string> GetDesktopCacheKeys(this Organization organization)
		{
			var cacheKeys = new[]
			{
				organization.HomeDesktop?.GetDesktopCacheKey($"{organization.URL}/{organization.HomeDesktop?.Alias}"),
				$"{organization.ID}:{organization.HomeDesktop?.Alias.GenerateUUID()}"
			}.ToList();
			if (organization.Sites != null && organization.Sites.Count > 1)
				cacheKeys = cacheKeys.Concat(organization.Sites.Select(site => site.HomeDesktop?.GetDesktopCacheKey($"{organization.URL}/{site.HomeDesktop?.Alias}", site))).ToList();
			cacheKeys = cacheKeys.Where(cacheKey => cacheKey != null).ToList();
			return cacheKeys.Concat(cacheKeys.Select(cacheKey => new[] { $"{cacheKey}:time", $"{cacheKey}:expiration" }).SelectMany(keys => keys)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
		}

		/// <summary>
		/// Sets cache of page-size (to clear related cached further)
		/// </summary>
		/// <typeparam name="T"></typeparam>
		/// <param name="filter"></param>
		/// <param name="sort"></param>
		/// <param name="pageSize"></param>
		/// <param name="cancellationToken"></param>
		/// <returns></returns>
		public static string SetCacheOfPageSize<T>(IFilterBy<T> filter, SortBy<T> sort, int pageSize) where T : class
		{
			var cacheKey = $"{Extensions.GetCacheKey(filter, sort)}:size";
			Utility.Cache.SetAsync(cacheKey, pageSize, Utility.CancellationToken).Execute();
			return cacheKey;
		}

		/// <summary>
		/// Sets cache of page-size (to clear related cached further)
		/// </summary>
		/// <typeparam name="T"></typeparam>
		/// <param name="filter"></param>
		/// <param name="sort"></param>
		/// <param name="pageSize"></param>
		/// <param name="cancellationToken"></param>
		/// <returns></returns>
		public static async Task<string> SetCacheOfPageSizeAsync<T>(IFilterBy<T> filter, SortBy<T> sort, int pageSize, CancellationToken cancellationToken = default) where T : class
		{
			var cacheKey = $"{Extensions.GetCacheKey(filter, sort)}:size";
			await Utility.Cache.SetAsync(cacheKey, pageSize, cancellationToken).ConfigureAwait(false);
			return cacheKey;
		}

		/// <summary>
		/// Purges cache at CloudFlare CDN by URLs
		/// </summary>
		/// <param name="urls"></param>
		/// <param name="cloudflareZoneID"></param>
		/// <param name="cloudflareApiToken"></param>
		/// <param name="correlationID"></param>
		/// <param name="writeLogs"></param>
		/// <param name="cancellationToken"></param>
		/// <returns></returns>
		public static async Task PurgeCloudFlareCacheAsync(this IEnumerable<string> urls, string cloudflareZoneID, string cloudflareApiToken, string correlationID, bool writeLogs, CancellationToken cancellationToken)
		{
			var uri = new Uri($"https://api.cloudflare.com/client/v4/zones/{cloudflareZoneID}/purge_cache");
			var headers = new Dictionary<string, string>
			{
				["Content-Type"] = "application/json",
				["Authorization"] = $"Bearer {cloudflareApiToken}"
			};
			var body = new JObject
			{
				["purge_everything"] = true
			};

			async Task purgeCloudFlareCacheAsync(JObject request)
			{
				try
				{
					using var _ = await uri.SendHttpRequestAsync("POST", headers, request.ToString(Newtonsoft.Json.Formatting.None), Utility.RefreshTimeout, cancellationToken).ConfigureAwait(false);
				}
				catch (Exception ex)
				{
					await Utility.WriteLogAsync(correlationID, $"Error occurred while purging CloudFlare cache => {(ex is RemoteServerException rse ? $"{rse.Message} (Code: {rse.StatusCode}){(string.IsNullOrWhiteSpace(rse.Body) ? "" : $"\r\nBody: {rse.Body}")}" : $"{ex.Message}")} [{ex.GetType()}]", "Caches").ConfigureAwait(false);
				}
			}

			var count = urls.Count();
			if (count > 0)
			{
				var pageSize = 25;
				var pageNumber = 0;
				var totalPages = Extensions.GetTotalPages(count, pageSize);
				while (pageNumber < totalPages)
				{
					body = new JObject
					{
						["files"] = urls.Skip(pageNumber * pageSize).Take(pageSize).ToJArray()
					};
					await purgeCloudFlareCacheAsync(body).ConfigureAwait(false);
					pageNumber++;
				}
				if (writeLogs || Utility.IsPurgeCacheLogEnabled)
					await Utility.WriteLogAsync(correlationID, $"Purge CloudFlare cache successful [{count:###,##0}]\r\nURLs:\r\n- {urls.Join("\r\n- ")}", "Caches").ConfigureAwait(false);
			}
			else
			{
				await purgeCloudFlareCacheAsync(body).ConfigureAwait(false);
				if (writeLogs || Utility.IsPurgeCacheLogEnabled)
					await Utility.WriteLogAsync(correlationID, "Purge CloudFlare cache successful [EVERYTHING]", "Caches").ConfigureAwait(false);
			}
		}

		/// <summary>
		/// Purges cache of this organization at CloudFlare CDN by URLs
		/// </summary>
		/// <param name="organization"></param>
		/// <param name="urls"></param>
		/// <param name="correlationID"></param>
		/// <param name="writeLogs"></param>
		/// <param name="cancellationToken"></param>
		/// <returns></returns>
		public static async Task PurgeCloudFlareCacheAsync(this Organization organization, IEnumerable<string> urls, string correlationID, bool writeLogs, CancellationToken cancellationToken, Action<IEnumerable<string>> onCompleted = null)
		{
			var orgGotCloudFlare = !string.IsNullOrWhiteSpace(organization.CloudFlareZoneID) && !string.IsNullOrWhiteSpace(organization.CloudFlareApiToken);
			var gotCloudFlare = orgGotCloudFlare || Utility.CloudFlareForAll;
			var purgeURLs = (urls ?? []).Where(url => !string.IsNullOrWhiteSpace(url))
				.Select(url => url.GetPaginatingURLs(Utility.RefreshMaxPage, organization.AlwaysUseHtmlSuffix ? ".html" : ""))
				.SelectMany(url => url)
				.Distinct(StringComparer.OrdinalIgnoreCase);
			var systemURLs = purgeURLs.Where(url => url.IsStartsWith(Utility.PortalsHttpURI)).ToList();
			var orgURLs = purgeURLs.Except(systemURLs)
				.Select(url => new[] { url, gotCloudFlare && url.IsContains("//www.") ? url.Replace("//www.", "//") : null })
				.SelectMany(url => url)
				.Where(url => !string.IsNullOrWhiteSpace(url))
				.ToList();
			if (writeLogs || Utility.IsPurgeCacheLogEnabled)
				Utility.WriteLogAsync(correlationID, $"Prepare to purge CloudFlare cache of '{organization.Title}'\r\nOrganization URLs:\r\n- {(orgURLs.Count == 0 ? "None" : orgURLs.Join("\r\n- "))}\r\nSystem URLs:\r\n- {(systemURLs.Count == 0 ? "None" : systemURLs.Join("\r\n- "))}", "Caches").Execute();
			await Task.WhenAll
			(
				gotCloudFlare
					? orgURLs.PurgeCloudFlareCacheAsync(orgGotCloudFlare ? organization.CloudFlareZoneID : Utility.CloudFlareZoneID, orgGotCloudFlare ? organization.CloudFlareApiToken : Utility.CloudFlareApiToken, correlationID, writeLogs, cancellationToken)
					: Task.CompletedTask,
				systemURLs.Count > 0 && !string.IsNullOrWhiteSpace(Utility.CloudFlareZoneID) && !string.IsNullOrWhiteSpace(Utility.CloudFlareApiToken)
					? systemURLs.PurgeCloudFlareCacheAsync(Utility.CloudFlareZoneID, Utility.CloudFlareApiToken, correlationID, writeLogs, cancellationToken)
					: Task.CompletedTask
			).ConfigureAwait(false);
			onCompleted?.Invoke(urls);
		}

		/// <summary>
		/// Purges cache of this object at CloudFlare CDN by URLs
		/// </summary>
		/// <param name="object"></param>
		/// <param name="correlationID"></param>
		/// <param name="writeLogs"></param>
		/// <param name="cancellationToken"></param>
		/// <returns></returns>
		public static async Task PurgeCloudFlareCacheAsync(this IBusinessObject @object, bool doRefresh, string correlationID, bool writeLogs, CancellationToken cancellationToken, Action<IBusinessObject> onCompleted = null)
		{
			if (@object.Organization is Organization organization)
			{
				var urls = new[] { organization.URL }.ToList();

				if (@object is Category category)
				{
					urls.Add(category.GetURL(true));
					var parentCategory = category?.ParentCategory;
					while (parentCategory != null)
					{
						urls.Add(parentCategory.GetURL());
						parentCategory = parentCategory.ParentCategory;
					}
				}

				else if (@object is Content content && content.Status.Equals(ApprovalStatus.Published))
				{
					urls.AddRange([content.GetURL(), content.Category?.GetURL(false)]);
					var categories = new[] { content.Category }.ToList();
					var parentCategory = content.Category?.ParentCategory;
					while (parentCategory != null)
					{
						urls.Add(parentCategory.GetURL());
						categories.Add(parentCategory);
						parentCategory = parentCategory.ParentCategory;
					}
					categories.ForEach(category => urls.Add(category?.GetURL(true)));
					(content.OtherCategories ?? []).Select(id => id.GetCategoryByID()).ForEach(category => urls.Add(category?.GetURL(true)));
				}

				else if (@object is Item item && item.Status.Equals(ApprovalStatus.Published))
					urls.AddRange([item.GetURL(), item.ContentType?.GetURL(null, true)]);

				else if (@object is Link link)
					urls.Add(link.URL);

				var (linkURLs, _, _) = await organization.GetRefreshingURLsAsync(true, null, false, false, null).ConfigureAwait(false);
				urls = urls.Concat(linkURLs).Where(url => !string.IsNullOrWhiteSpace(url) && url.StartsWith("~/")).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

				var siteURL = (organization.DefaultSite?.GetURL() ?? organization.URL.Replace("~/", Utility.PortalsHttpURI)) + "/";
				await organization.PurgeCloudFlareCacheAsync(Utility.CloudFlarePurgeEverythingOnObject ? [] : urls.Select(url => url.Replace("~/", siteURL)), correlationID, writeLogs, cancellationToken).ConfigureAwait(false);

				if (doRefresh)
					await organization.RefreshWebPagesAsync(urls, false, true, correlationID, "Refresh when purge CloudFlare cache", writeLogs, cancellationToken).ConfigureAwait(false);
			}
			onCompleted?.Invoke(@object);
		}

		/// <summary>
		/// Refreshs URLs of this organization
		/// </summary>
		/// <param name="organization"></param>
		/// <param name="urls"></param>
		/// <param name="force"></param>
		/// <param name="delayByIndex"></param>
		/// <param name="correlationID"></param>
		/// <param name="message"></param>
		/// <param name="writeLogs"></param>
		/// <param name="cancellationToken"></param>
		/// <returns></returns>
		public static async Task RefreshWebPagesAsync(this Organization organization, IEnumerable<string> urls, bool force, bool delayByIndex, string correlationID, string message, bool writeLogs, CancellationToken cancellationToken)
		{
			var headers = new Dictionary<string, string>
			{
				["x-requester"] = "vieapps-ngx-portals",
				["x-original-correlation-id"] = correlationID
			};
			if (force)
				headers["x-force-cache"] = "1";
			else
				headers["x-sliding-cache"] = "1";
			var gotCloudFlare = !string.IsNullOrWhiteSpace(organization.CloudFlareZoneID) && !string.IsNullOrWhiteSpace(organization.CloudFlareApiToken);
			var rootURL = (gotCloudFlare ? (organization.DefaultSite?.GetURL() ?? organization.URL) : organization.URL) + "/";
			var refreshURLs = (urls ?? [])
				.Where(url => !string.IsNullOrWhiteSpace(url))
				.Select(url => url.GetPaginatingURLs(Utility.RefreshMaxPage, organization.AlwaysUseHtmlSuffix ? ".html" : ""))
				.SelectMany(url => url)
				.Select(url =>
				{
					var fullURL = url.Replace("~/", rootURL);
					return new[] { fullURL, gotCloudFlare && fullURL.IsContains("//www.") ? fullURL.Replace("//www.", "//") : null };
				})
				.SelectMany(url => url)
				.Where(url => !string.IsNullOrWhiteSpace(url) && (url.IsStartsWith("https://") || url.IsStartsWith("http://")))
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.ToList();
			await Task.WhenAll
			(
				writeLogs
				 ? Utility.WriteLogAsync(correlationID, $"{message ?? $"Refresh URLs of '{organization.Title}' [ID: {organization.ID}]"}\r\nURLs:\r\n- {refreshURLs.Join("\r\n- ")}", "Caches")
				 : Task.CompletedTask,
				refreshURLs.ForEachAsync((url, index, cancellationtoken) => url.RefreshWebPageAsync(headers, delayByIndex ? index : 0, correlationID, force, cancellationtoken), cancellationToken, true, !force && Utility.RunProcessorInParallelsMode)
			).ConfigureAwait(false);
		}

		/// <summary>
		/// Refreshs URLs of this organization
		/// </summary>
		/// <param name="organization"></param>
		/// <param name="urls"></param>
		/// <param name="correlationID"></param>
		/// <param name="message"></param>
		/// <param name="force"></param>
		/// <param name="cancellationToken"></param>
		/// <returns></returns>
		public static Task RefreshWebPagesAsync(this Organization organization, IEnumerable<string> urls, bool force, string correlationID, string message, CancellationToken cancellationToken)
			=> organization.RefreshWebPagesAsync(urls, force, false, correlationID, message, false, cancellationToken);

		/// <summary>
		/// Refreshs a web-page by specified URL
		/// </summary>
		/// <param name="url"></param>
		/// <param name="headers"></param>
		/// <param name="delay"></param>
		/// <param name="correlationID"></param>
		/// <param name="writeLogs"></param>
		/// <param name="cancellationToken"></param>
		/// <returns></returns>
		public static async Task RefreshWebPageAsync(this string url, Dictionary<string, string> headers, int delay, string correlationID, bool writeLogs, CancellationToken cancellationToken)
		{
			if (string.IsNullOrWhiteSpace(url) || (!url.IsStartsWith("https://") && !url.IsStartsWith("http://")))
				return;

			writeLogs = writeLogs || Utility.IsCacheLogEnabled;
			correlationID = correlationID ?? UtilityService.NewUUID;
			headers = new Dictionary<string, string>(headers ?? [], StringComparer.OrdinalIgnoreCase);
			Utility.RefresherHeaders.ForEach(kvp => headers[kvp.Key] = kvp.Value);

			async Task refreshWebPageAsync(Uri uri, bool handleException)
			{
				try
				{
					var stopwatch = Stopwatch.StartNew();
					await uri.FetchHttpAsync(headers, Utility.RefreshTimeout, cancellationToken).ConfigureAwait(false);
					if (writeLogs)
						await Utility.WriteLogAsync(correlationID, $"Refresh successful => {uri.AbsoluteUri}\r\nExecution times: {stopwatch.GetElapsedTimes()}", "Caches").ConfigureAwait(false);
				}
				catch (TaskCanceledException) { }
				catch (OperationCanceledException) { }
				catch (ConnectionTimeoutException) { }
				catch (ServiceOperationException) { }
				catch (ServiceNotFoundException) { }
				catch (RemoteServerMovedException ex)
				{
					if (handleException)
						await Utility.WriteLogAsync(correlationID, $"Server was moved while refreshing => {ex.URI}", "Caches").ConfigureAwait(false);
					else
						throw;
				}
				catch (RemoteServerException ex)
				{
					if (handleException)
					{
						if (ex.Code != 404 && ex.Code != 502 && ex.Code != 503 && ex.Code != 522 && !ex.Message.IsContains("No such host is known"))
							await Utility.WriteLogAsync(correlationID, $"Error occurred while refreshing ({uri.AbsoluteUri}) => {ex.Message} [Code: {ex.StatusCode}]{(string.IsNullOrWhiteSpace(ex.Body) ? "" : $"\r\nBody: {ex.Body}")}", "Caches").ConfigureAwait(false);
					}
					else
						throw;
				}
				catch (Exception ex)
				{
					if (handleException)
					{
						if (!ex.Message.IsContains("No such host is known"))
							await Utility.WriteLogAsync(correlationID, $"Error occurred while refreshing ({uri.AbsoluteUri}) => {ex.Message} [{ex.GetType()}]", "Caches").ConfigureAwait(false);
					}
					else
						throw;
				}
			}

			try
			{
				if (delay > 0)
					await Task.Delay(delay * 1000, cancellationToken).ConfigureAwait(false);
				await refreshWebPageAsync(new Uri(url), false).ConfigureAwait(false);
			}
			catch (Exception ex)
			{
				if (ex is RemoteServerMovedException rsme && rsme.InnerException is not ServiceOperationException && rsme.InnerException is not ServiceNotFoundException)
					await refreshWebPageAsync(rsme.URI, true).ConfigureAwait(false);
				else if (!ex.Message.IsContains("No such host is known"))
					await Utility.WriteLogAsync(correlationID, $"Error occurred while refreshing ({url}) => {ex.Message} [{ex.GetType()}]", "Caches").ConfigureAwait(false);
			}
		}

		/// <summary>
		/// Refreshs a web-page by specified URL
		/// </summary>
		/// <param name="url"></param>
		/// <param name="delay"></param>
		/// <param name="correlationID"></param>
		/// <param name="writeLogs"></param>
		/// <param name="cancellationToken"></param>
		/// <returns></returns>
		public static Task RefreshWebPageAsync(this string url, int delay, string correlationID, bool writeLogs, CancellationToken cancellationToken)
			=> url.RefreshWebPageAsync(null, delay, correlationID, writeLogs, cancellationToken);
	}
}
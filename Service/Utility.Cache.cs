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

		internal static string CDNZoneID { get; set; } = UtilityService.GetAppSetting("Portals:CDN:ZoneID") ?? UtilityService.GetAppSetting("Portals:CloudFlare:ZoneID");

		internal static string CDNApiToken { get; set; } = UtilityService.GetAppSetting("Portals:CDN:ApiToken") ?? UtilityService.GetAppSetting("Portals:CloudFlare:ApiToken");

		internal static bool CDNForAll { get; set; } = !string.IsNullOrWhiteSpace(CDNZoneID) && !string.IsNullOrWhiteSpace(CDNApiToken) && "true".IsEquals(UtilityService.GetAppSetting("Portals:CDN:All"));

		public static bool CDNPurgeEverythingOnObject { get; internal set; } = "true".IsEquals(UtilityService.GetAppSetting("Portals:CDN:Everything"));

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
		public static string GetSetCacheKey(this Organization organization, string suffix)
			=> $"Set:{organization.ID}:{suffix}";

		/// <summary>
		/// Gets the key for storing a set of keys that related to a content-type
		/// </summary>
		public static string GetSetCacheKey(this ContentType contentType, string suffix = null)
			=> contentType.Organization.GetSetCacheKey($"ContentType:{contentType.ID}{(string.IsNullOrWhiteSpace(suffix) ? "" : $":{suffix}")}");

		/// <summary>
		/// Gets the key for storing a set of keys that related to a desktop
		/// </summary>
		/// <param name="desktop"></param>
		/// <returns></returns>
		public static string GetSetCacheKey(this Desktop desktop, string suffix = null)
			=> desktop.Organization.GetSetCacheKey($"Desktop:{desktop.ID}{(string.IsNullOrWhiteSpace(suffix) ? "" : $":{suffix}")}");

		/// <summary>
		/// Gets the key for storing a set of keys that related to a category
		/// </summary>
		/// <param name="desktop"></param>
		/// <returns></returns>
		public static string GetSetCacheKey(this Category category, string suffix = null)
			=> category.Organization.GetSetCacheKey($"Category:{category.ID}{(string.IsNullOrWhiteSpace(suffix) ? "" : $":{suffix}")}");

		/// <summary>
		/// Gets the set of keys that used to store HTML cache that related to this organization
		/// </summary>
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
		/// Gets the set of keys that used to store HTML cache that related to this site
		/// </summary>
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
		/// Gets the set of keys that used to store HTML cache of this desktop
		/// </summary>
		public static async Task<List<string>> GetSetCacheKeysAsync(this Desktop desktop, CancellationToken cancellationToken = default, bool includeStaticResources = false)
			=> (includeStaticResources ? [$"css#d_{desktop.ID}", $"css#d_{desktop.ID}:time", $"js#d_{desktop.ID}", $"js#d_{desktop.ID}:time"] : Array.Empty<string>())
				.Concat(await Utility.Cache.GetSetMembersAsync(desktop.GetSetCacheKey(), cancellationToken).ConfigureAwait(false) ?? [])
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.ToList();

		static string GetPath(this Uri requestURI, string organizationAlias, string desktopAlias)
		{
			var path = requestURI.AbsolutePath.ToLower();
			while (path.EndsWith("/") || path.EndsWith("."))
				path = path.Left(path.Length - 1).Trim();
			path = path.IsStartsWith($"/~{organizationAlias}")
				? path.Right(path.Length - organizationAlias.Length - 2)
				: path;
			path = path.IsEndsWith("/default.aspx")
				? path.Left(path.Length - 13)
				: path;
			path = path.IsEndsWith(".html") || path.IsEndsWith(".aspx")
				? path.Left(path.Length - 5)
				: path.IsEndsWith(".php")
					? path.Left(path.Length - 4)
					: path;
			if (path.Equals("") || path.Equals("/") || path.Equals("/index") || path.Equals("/default"))
				path = "-default";
			else
			{
				path = $"/{desktopAlias}/{path.ToArray("/", true).Skip(1).Join("/")}";
				while (path.EndsWith('/'))
					path = path.Left(path.Length - 1);
			}
			return path;
		}

		/// <summary>
		/// Gets the key for storing HTML code of a desktop that specified by alias and requested URL
		/// </summary>
		public static string GetDesktopCacheKey(this Desktop desktop, Uri requestURI, Site site = null)
		{
			var organization = desktop.Organization;
			var path = desktop.Alias.IsEquals("-default") || desktop.ID.IsEquals((site?.HomeDesktop ?? organization.HomeDesktop)?.ID)
				? "-default"
				: requestURI.GetPath(organization.Alias, desktop.Alias);
			return organization.ID + (site == null || string.IsNullOrWhiteSpace(site.ID) || site.ID.IsEquals(organization.DefaultSite?.ID) ? "" : ":" + site.ID) + ":" + path.GenerateUUID();
		}

		/// <summary>
		/// Gets the key for storing HTML code of a desktop that specified by alias and requested URL
		/// </summary>
		public static string GetDesktopCacheKey(this Desktop desktop, string requestURL, Site site = null)
			=> desktop.GetDesktopCacheKey(new Uri(requestURL.IsStartsWith("http://") || requestURL.IsStartsWith("https://") ? requestURL : "https://site.vieapps.net/" + (requestURL.Equals("#") ? "" : requestURL.Replace("~/", ""))), site);

		/// <summary>
		/// Gets all the keys for storing HTML code of home desktops (of all sites)
		/// </summary>
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
		public static string GetCacheKeyOfPageSize<T>(IFilterBy<T> filter, SortBy<T> sort) where T : class
			=> $"{Extensions.GetCacheKey(filter, sort)}:size";

		/// <summary>
		/// Sets cache of page-size (to clear related cached further)
		/// </summary>
		public static string SetCacheOfPageSize<T>(IFilterBy<T> filter, SortBy<T> sort, int pageSize) where T : class
		{
			var cacheKey = Utility.GetCacheKeyOfPageSize(filter, sort);
			Utility.Cache.SetAsync(cacheKey, pageSize, Utility.CancellationToken).Execute();
			return cacheKey;
		}

		/// <summary>
		/// Purges cache at CDN (Cloudlare) by specified URLs
		/// </summary>
		/// <returns></returns>
		public static async Task PurgeCloudFlareCacheAsync(this IEnumerable<string> urls, string cloudflareZoneID, string cloudflareApiToken, string correlationID, bool writeLogs, CancellationToken cancellationToken)
		{
			if (string.IsNullOrWhiteSpace(cloudflareZoneID) || string.IsNullOrWhiteSpace(cloudflareApiToken))
				return;

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

			writeLogs = writeLogs || Utility.IsPurgeCacheLogEnabled;
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
				if (writeLogs)
					await Utility.WriteLogAsync(correlationID, $"Purge CloudFlare cache successful [{count:###,##0}]\r\nURLs:\r\n- {urls.Join("\r\n- ")}", "Caches").ConfigureAwait(false);
			}
			else
			{
				await purgeCloudFlareCacheAsync(body).ConfigureAwait(false);
				if (writeLogs)
					await Utility.WriteLogAsync(correlationID, "Purge CloudFlare cache successful [EVERYTHING]", "Caches").ConfigureAwait(false);
			}
		}

		/// <summary>
		/// Purges cache of this organization at CDN by specified URLs
		/// </summary>
		public static async Task PurgeCDNCacheAsync(this Organization organization, IEnumerable<string> urls, bool doRefresh, string correlationID, bool writeLogs, CancellationToken cancellationToken, Action<IEnumerable<string>> onCompleted = null)
		{
			var gotCDN = organization.GotCDN(true);
			var purgeURLs = (urls ?? []).Where(url => !string.IsNullOrWhiteSpace(url) && (url.IsStartsWith("https://") || url.IsStartsWith("http://")))
				.Select(url => url.GetPaginatingURLs(Utility.RefreshMaxPage, organization.AlwaysUseHtmlSuffix ? ".html" : ""))
				.SelectMany(url => url)
				.Distinct(StringComparer.OrdinalIgnoreCase);
			var systemURLs = purgeURLs.Where(url => url.IsStartsWith(Utility.PortalsHttpURI)).ToList();
			var orgURLs = purgeURLs.Except(systemURLs)
				.Select(url => new[] { url, gotCDN && url.IsContains("//www.") ? url.Replace("//www.", "//") : null })
				.SelectMany(url => url)
				.ToList();

			writeLogs = writeLogs || Utility.IsPurgeCacheLogEnabled;
			if (writeLogs)
				Utility.WriteLogAsync(correlationID, $"Prepare to purge CDN cache of '{organization.Title}'\r\nOrganization URLs:\r\n- {(orgURLs.Count == 0 ? "None" : orgURLs.Join("\r\n- "))}\r\nSystem URLs:\r\n- {(systemURLs.Count == 0 ? "None" : systemURLs.Join("\r\n- "))}", "Caches").Execute();

			await Task.WhenAll
			(
				gotCDN
					? orgURLs.PurgeCloudFlareCacheAsync(organization.GetCDNZoneID(), organization.GetCDNApiToken(), correlationID, writeLogs, cancellationToken)
					: Task.CompletedTask,
				systemURLs.Count > 0 && !string.IsNullOrWhiteSpace(Utility.CDNZoneID) && !string.IsNullOrWhiteSpace(Utility.CDNApiToken)
					? systemURLs.PurgeCloudFlareCacheAsync(Utility.CDNZoneID, Utility.CDNApiToken, correlationID, writeLogs, cancellationToken)
					: Task.CompletedTask
			).ConfigureAwait(false);

			if (doRefresh)
			{
				if (gotCDN)
					await Task.Delay(3456, cancellationToken).ConfigureAwait(false);
				await organization.RefreshWebPagesAsync(orgURLs, false, true, correlationID, "Refresh when purge CDN cache", writeLogs, cancellationToken).ConfigureAwait(false);
			}

			onCompleted?.Invoke(urls);
		}

		/// <summary>
		/// Purges cache of this organization at CDN by specified URLs
		/// </summary>
		public static Task PurgeCDNCacheAsync(this Organization organization, IEnumerable<string> urls, string correlationID, bool writeLogs, CancellationToken cancellationToken, Action<IEnumerable<string>> onCompleted = null)
		{
			var siteURL = (organization.DefaultSite?.GetURL() ?? organization.URL) + "/";
			return organization.PurgeCDNCacheAsync(urls?.Select(url => url?.Replace("~/", siteURL)), false, correlationID, writeLogs, cancellationToken, onCompleted);
		}

		/// <summary>
		/// Purges cache of this object at CDN by specified URLs
		/// </summary>
		public static async Task PurgeCDNCacheAsync(this IBusinessObject @object, bool doRefresh, string correlationID, bool writeLogs, CancellationToken cancellationToken, Action<IBusinessObject> onCompleted = null)
		{
			writeLogs = writeLogs || Utility.IsPurgeCacheLogEnabled;
			if (@object.Organization is Organization organization)
			{
				var urls = new[] { "~/" + (organization.AlwaysUseHtmlSuffix ? "index.html" : ""), "~/rss", "~/rss.xml", "~/rss.json" }.ToList();
				(organization.Sites ?? []).Where(site => (site.Status == ApprovalStatus.Approved || site.Status == ApprovalStatus.Published) && site.ID != organization.DefaultSite?.ID)
					.ForEach(site => urls.Add(site.GetURL() + (organization.AlwaysUseHtmlSuffix ? "/index.html" : "")));

				if (@object is Category category && category.Status == ApprovalStatus.Published)
				{
					urls.Add(category.GetURL(true));
					var parentCategory = category?.ParentCategory;
					while (parentCategory != null)
					{
						urls.Add(parentCategory.GetURL(true));
						parentCategory = parentCategory.ParentCategory;
					}
				}

				else if (@object is Content content && content.Status == ApprovalStatus.Published)
				{
					var categories = new[] { content.Category }.ToList();
					var parentCategory = content.Category?.ParentCategory;
					while (parentCategory != null)
					{
						categories.Add(parentCategory);
						parentCategory = parentCategory.ParentCategory;
					}
					categories.AddRange((content.OtherCategories ?? []).Select(id => id.GetCategoryByID()).Where(category => category != null && category.Status == ApprovalStatus.Published));

					urls.Add(content.GetURL());
					urls.AddRange(categories.Select(category => category?.GetURL(true)));

					await categories.ForEachAsync(async (category, cancellationtoken) =>
					{
						var (contents, _) = await content.ContentType.FindContentsAsync(category.ID, -1, 20, 1, cancellationtoken).ConfigureAwait(false);
						urls.AddRange(contents.Select(contentObj => contentObj.Status == ApprovalStatus.Published ? contentObj.GetURL() : null));
					}, cancellationToken, true, false).ConfigureAwait(false);
				}

				else if (@object is Item item && item.Status == ApprovalStatus.Published)
				{
					urls.AddRange([item.GetURL(), item.ContentType.GetURL(true)]);
					var items = await item.ContentType.FindItemsAsync(20, 1, cancellationToken).ConfigureAwait(false);
					urls.AddRange(items.Select(itemObj => itemObj.Status == ApprovalStatus.Published ? itemObj.GetURL() : null));
				}

				else if (@object is Link link && link.Status == ApprovalStatus.Published)
				{
					urls.Add(link.URL);
					var parentLink = link.ParentLink;
					while (parentLink != null)
					{
						urls.Add(parentLink.URL);
						parentLink = parentLink.ParentLink;
					}
				}

				var (linkURLs, _, _, _) = await organization.GetRefreshingURLsAsync(true, false, false, false, 0, 0, null, null, null, correlationID, cancellationToken).ConfigureAwait(false);
				var siteURL = (organization.DefaultSite?.GetURL() ?? organization.URL) + "/";
				urls = urls.Concat(linkURLs)
					.Where(url => !string.IsNullOrWhiteSpace(url) && (url.StartsWith("~/") || url.IsStartsWith("https://") || url.IsStartsWith("http://")))
					.Select(url => url.Replace("~/", siteURL))
					.Distinct(StringComparer.OrdinalIgnoreCase)
					.ToList();

				var cachePaths = new List<string>();
				var cacheURLs = urls.Select(url => url.GetPaginatingURLs(Utility.RefreshMaxPage, organization.AlwaysUseHtmlSuffix ? ".html" : "")).SelectMany(url => url).ToList();
				cacheURLs.ForEach(url =>
				{
					var uri = new Uri(url);
					var segments = uri.AbsolutePath.ToList("/", true).ToList();
					if (segments.Count > 0 && segments[0].IsEquals("~" + organization.Alias))
						segments = segments.Skip(1).ToList();
					var desktopAlias = segments.Count > 0 ? segments[0] : null;
					if (desktopAlias != null)
						desktopAlias = desktopAlias.IsEndsWith(".html") || desktopAlias.IsEndsWith(".aspx")
							? desktopAlias.Left(desktopAlias.Length - 5)
							: desktopAlias.IsEndsWith(".php")
								? desktopAlias.Left(desktopAlias.Length - 4)
								: desktopAlias;
					cachePaths.Add(desktopAlias == null || desktopAlias.IsEquals(organization.HomeDesktop?.Alias) ? "-default" : uri.GetPath(organization.Alias, desktopAlias));
				});
				var cacheKeys = cachePaths.Distinct(StringComparer.OrdinalIgnoreCase).Select(path => organization.ID + ":" + path.GenerateUUID()).ToList();
				await Utility.Cache.RemoveAsync(cacheKeys, cancellationToken).ConfigureAwait(false);
				if (writeLogs)
					await Utility.WriteLogAsync(correlationID, $"Remove cache of HTMLs before purging CDN cache\r\nURLs [{cacheURLs.Count:###,##0}]:\r\n- {cacheURLs.Join("\r\n- ")}\r\nPaths [{cachePaths.Count:###,##0}]: {cachePaths.Join(", ")}\r\nKeys [{cacheKeys.Count:###,##0}]: {cacheKeys.Join(", ")}", "Caches").ConfigureAwait(false);

				if (Utility.CDNPurgeEverythingOnObject)
				{
					await organization.PurgeCDNCacheAsync([], false, correlationID, writeLogs, cancellationToken).ConfigureAwait(false);
					if (doRefresh)
					{
						await Task.Delay(3456, cancellationToken).ConfigureAwait(false);
						await organization.RefreshWebPagesAsync(urls, false, true, correlationID, "Refresh when purge CDN cache", writeLogs, cancellationToken).ConfigureAwait(false);
					}
				}
				else
					await organization.PurgeCDNCacheAsync(urls, doRefresh, correlationID, writeLogs, cancellationToken).ConfigureAwait(false);
			}
			onCompleted?.Invoke(@object);
		}

		internal static bool GotCDN(this Organization organization, bool checkCDNForAll = false)
			=> (organization != null && !string.IsNullOrWhiteSpace(organization.CDNZoneID) && !string.IsNullOrWhiteSpace(organization.CDNApiToken)) || (checkCDNForAll && Utility.CDNForAll);

		internal static string GetCDNZoneID(this Organization organization)
			=> organization != null && organization.GotCDN()
				? organization.CDNZoneID
				: Utility.CDNZoneID;

		internal static string GetCDNApiToken(this Organization organization)
			=> organization != null && organization.GotCDN()
				? organization.CDNApiToken
				: Utility.CDNApiToken;

		/// <summary>
		/// Refreshs URLs of this organization
		/// </summary>
		public static async Task RefreshWebPagesAsync(this Organization organization, IEnumerable<string> urls, bool force, bool delayByIndex, string correlationID, string message, bool writeLogs, CancellationToken cancellationToken)
		{
			writeLogs = writeLogs || Utility.IsCacheLogEnabled;
			var headers = new Dictionary<string, string>
			{
				["x-requester"] = "vieapps-ngx-portals",
				["x-original-correlation-id"] = correlationID
			};
			if (force)
				headers["x-force-cache"] = "1";
			else
				headers["x-sliding-cache"] = "1";
			if (writeLogs)
				headers["x-header-logs"] = "1";
			var gotCDN = organization.GotCDN(true);
			var rootURL = (gotCDN ? (organization.DefaultSite?.GetURL() ?? organization.URL) : organization.URL) + "/";
			var refreshURLs = (urls ?? [])
				.Where(url => !string.IsNullOrWhiteSpace(url))
				.Select(url => url.GetPaginatingURLs(Utility.RefreshMaxPage, organization.AlwaysUseHtmlSuffix ? ".html" : ""))
				.SelectMany(url => url)
				.Select(url =>
				{
					var fullURL = url.Replace("~/", rootURL);
					return new[] { fullURL, gotCDN && fullURL.IsContains("//www.") ? fullURL.Replace("//www.", "//") : null };
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
		public static Task RefreshWebPagesAsync(this Organization organization, IEnumerable<string> urls, bool force, string correlationID, string message, CancellationToken cancellationToken)
			=> organization.RefreshWebPagesAsync(urls, force, false, correlationID, message, false, cancellationToken);

		/// <summary>
		/// Refreshs a web-page by specified URL
		/// </summary>
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
						if (ex.Code != 404 && ex.Code != 502 && ex.Code != 503 && ex.Code != 522)
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
			catch (RemoteServerException ex)
			{
				if (ex.Code != 404 && ex.Code != 502 && ex.Code != 503 && ex.Code != 522)
					await Utility.WriteLogAsync(correlationID, $"Error occurred while refreshing ({url}) => {ex.Message} [Code: {ex.StatusCode}]{(string.IsNullOrWhiteSpace(ex.Body) ? "" : $"\r\nBody: {ex.Body}")}", "Caches").ConfigureAwait(false);
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
		public static Task RefreshWebPageAsync(this string url, int delay, string correlationID, bool writeLogs, CancellationToken cancellationToken)
			=> url.RefreshWebPageAsync(null, delay, correlationID, writeLogs, cancellationToken);

		internal static async Task<(IEnumerable<string> DataCacheKeys, IEnumerable<(string SetCacheKey, IEnumerable<string> SetCacheKeys)> HtmlCacheKeys)> GetCacheKeysAsync(this Organization organization, bool getDataCacheKeys, bool getHtmlCacheKeys, CancellationToken cancellationToken)
		{
			IEnumerable<string> dataCacheKeys = new List<string>();
			if (getDataCacheKeys)
				dataCacheKeys = Extensions.GetRelatedCacheKeys(Filters<Organization>.And(), Sorts<Organization>.Ascending("Title"))
					.Concat(Extensions.GetRelatedCacheKeys(Filters<Organization>.And(Filters<Organization>.Equals("OwnerID", organization.OwnerID)), Sorts<Organization>.Ascending("Title")))
					.ToList();

			var htmlCacheKeys = new List<(string SetCacheKey, IEnumerable<string> SetCacheKeys)>();
			if (getHtmlCacheKeys)
				htmlCacheKeys.Add(("Global", organization.GetDesktopCacheKeys().Concat(await organization.GetSetCacheKeysAsync(cancellationToken).ConfigureAwait(false))));

			return (dataCacheKeys, htmlCacheKeys);
		}

		internal static async Task<(IEnumerable<string> WorkingCacheKeys, IEnumerable<(string SetCacheKey, IEnumerable<string> SetCacheKeys)> ObjectCacheKeys, IEnumerable<(string SetCacheKey, IEnumerable<string> SetCacheKeys)> DataCacheKeys, IEnumerable<(string SetCacheKey, IEnumerable<string> SetCacheKeys)> HtmlCacheKeys)> GetCacheKeysAsync(this Module module, bool getObjectCacheKeys, bool getDataCacheKeys, bool getHtmlCacheKeys, CancellationToken cancellationToken)
		{
			var sort = Sorts<Module>.Ascending("Title");
			var workingCacheKeys = Extensions.GetRelatedCacheKeys(Filters<Module>.And(), sort)
				.Concat(Extensions.GetRelatedCacheKeys(ModuleProcessor.GetModulesFilter(module.SystemID), sort))
				.Concat(Extensions.GetRelatedCacheKeys(ModuleProcessor.GetModulesFilter(module.SystemID, module.ModuleDefinitionID), sort))
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.ToList();
			var objectCacheKeys = new List<(string SetCacheKey, IEnumerable<string> SetCacheKeys)>();
			var dataCacheKeys = new List<(string SetCacheKey, IEnumerable<string> SetCacheKeys)>();
			var htmlCacheKeys = new List<(string SetCacheKey, IEnumerable<string> SetCacheKeys)>();

			await module.ContentTypes.ForEachAsync(async (contentType, cancellationtoken) =>
			{
				var info = await contentType.GetCacheKeysAsync(getObjectCacheKeys, getDataCacheKeys, getHtmlCacheKeys, cancellationtoken).ConfigureAwait(false);
				workingCacheKeys.AddRange(info.WorkingCacheKeys);
				objectCacheKeys.Add((contentType.ObjectCacheKeys, info.ObjectCacheKeys));
				dataCacheKeys.Add((contentType.GetSetCacheKey(), info.DataCacheKeys));
				htmlCacheKeys.AddRange(info.HtmlCacheKeys);
			}, cancellationToken, true, false).ConfigureAwait(false);

			return (workingCacheKeys, objectCacheKeys, dataCacheKeys, htmlCacheKeys);
		}

		internal static async Task<(IEnumerable<string> WorkingCacheKeys, IEnumerable<string> ObjectCacheKeys, IEnumerable<string> DataCacheKeys, IEnumerable<(string SetCacheKey, IEnumerable<string> SetCacheKeys)> HtmlCacheKeys)> GetCacheKeysAsync(this ContentType contentType, bool getObjectCacheKeys, bool getDataCacheKeys, bool getHtmlCacheKeys, CancellationToken cancellationToken)
		{
			var sort = Sorts<ContentType>.Ascending("Title");
			var workingCacheKeys = Extensions.GetRelatedCacheKeys(Filters<ContentType>.And(), sort)
				.Concat(Extensions.GetRelatedCacheKeys(ContentTypeProcessor.GetContentTypesFilter(contentType.SystemID), sort))
				.Concat(Extensions.GetRelatedCacheKeys(ContentTypeProcessor.GetContentTypesFilter(contentType.SystemID, contentType.RepositoryID, contentType.ContentTypeDefinitionID), sort))
				.Concat(Extensions.GetRelatedCacheKeys(ContentTypeProcessor.GetContentTypesFilter(contentType.SystemID, contentType.RepositoryID, null), sort))
				.Concat(Extensions.GetRelatedCacheKeys(ContentTypeProcessor.GetContentTypesFilter(contentType.SystemID, null, contentType.ContentTypeDefinitionID), sort))
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.ToList();

			var objectCacheKeys = (getObjectCacheKeys
				? await Utility.Cache.GetSetMembersAsync(contentType.ObjectCacheKeys, cancellationToken).ConfigureAwait(false) ?? []
				: []).ToList();

			var dataCacheKeys = (getDataCacheKeys
				? await Utility.Cache.GetSetMembersAsync(contentType.GetSetCacheKey(), cancellationToken).ConfigureAwait(false) ?? []
				: []).ToList();

			var htmlCacheKeys = new List<(string SetCacheKey, IEnumerable<string> SetCacheKeys)>();
			if (getHtmlCacheKeys)
			{
				htmlCacheKeys.Add(("Global", contentType.Organization.GetDesktopCacheKeys()));
				var desktops = await contentType.FindDesktopsAsync(false, cancellationToken).ConfigureAwait(false);
				await desktops.ForEachAsync(async (desktop, cancellationtoken) =>
				{
					var cacheKey = desktop.GetSetCacheKey();
					var cacheKeys = await Utility.Cache.GetSetMembersAsync(cacheKey, cancellationtoken).ConfigureAwait(false) ?? [];
					if (cacheKeys.Any())
						htmlCacheKeys.Add((cacheKey, cacheKeys));
				}, cancellationToken, true, false).ConfigureAwait(false);
			}

			return (workingCacheKeys, objectCacheKeys, dataCacheKeys, htmlCacheKeys);
		}

		internal static async Task<(IEnumerable<string> DataCacheKeys, IEnumerable<(string SetCacheKey, IEnumerable<string> SetCacheKeys)> HtmlCacheKeys)> GetCacheKeysAsync(this Expression expression, bool getDataCacheKeys, bool getHtmlCacheKeys, CancellationToken cancellationToken)
		{
			IEnumerable<string> dataCacheKeys = new List<string>();
			if (getDataCacheKeys)
			{
				var sort = Sorts<Expression>.Ascending("Title");
				dataCacheKeys = Extensions.GetRelatedCacheKeys(Filters<Expression>.And(), sort)
					.Concat(Extensions.GetRelatedCacheKeys(expression.SystemID.GetExpressionsFilter(null), sort))
					.Concat(Extensions.GetRelatedCacheKeys(expression.SystemID.GetExpressionsFilter(expression.RepositoryID), sort))
					.Concat(Extensions.GetRelatedCacheKeys(expression.SystemID.GetExpressionsFilter(expression.RepositoryID, expression.RepositoryEntityID, expression.ContentTypeDefinitionID), sort))
					.Concat(Extensions.GetRelatedCacheKeys(expression.SystemID.GetExpressionsFilter(expression.RepositoryID, expression.RepositoryEntityID, null), sort))
					.Concat(Extensions.GetRelatedCacheKeys(expression.SystemID.GetExpressionsFilter(expression.RepositoryID, null, expression.ContentTypeDefinitionID), sort))
					.ToList();
				if (!string.IsNullOrWhiteSpace(expression.RepositoryEntityID) && !string.IsNullOrWhiteSpace(expression.ContentTypeDefinitionID))
				{
					var filter = Filters<Expression>.Or
					(
						Filters<Expression>.Equals("ContentTypeDefinitionID", expression.ContentTypeDefinitionID),
						Filters<Expression>.Equals("RepositoryEntityID", expression.RepositoryEntityID)
					);
					dataCacheKeys = dataCacheKeys.Concat(Extensions.GetRelatedCacheKeys(filter, sort)).Concat(Extensions.GetRelatedCacheKeys(Filters<Expression>.And(Filters<Expression>.Equals("RepositoryID", expression.RepositoryID), filter), sort));
					if (expression.ContentType != null)
						dataCacheKeys = dataCacheKeys.Concat(await Utility.Cache.GetSetMembersAsync(expression.ContentType.GetSetCacheKey(), cancellationToken).ConfigureAwait(false));
				}
			}

			var htmlCacheKeys = new List<(string SetCacheKey, IEnumerable<string> SetCacheKeys)>();
			if (getHtmlCacheKeys)
			{
				htmlCacheKeys.Add(("Global", expression.Organization.GetDesktopCacheKeys()));
				var desktops = await expression.FindDesktopsAsync(cancellationToken).ConfigureAwait(false);
				await desktops.ForEachAsync(async (desktop, cancellationtoken) =>
				{
					var cacheKey = desktop.GetSetCacheKey();
					var cacheKeys = await Utility.Cache.GetSetMembersAsync(cacheKey, cancellationtoken).ConfigureAwait(false) ?? [];
					if (cacheKeys.Any())
						htmlCacheKeys.Add((cacheKey, cacheKeys));
				}, cancellationToken, true, false).ConfigureAwait(false);
			}

			return (dataCacheKeys, htmlCacheKeys);
		}

		internal static async Task<IEnumerable<string>> GetCacheKeys(this Role role, string oldParentID)
		{
			var sort = Sorts<Role>.Ascending("Title");
			IEnumerable<string> cacheKeys = Extensions.GetRelatedCacheKeys(RoleProcessor.GetRolesFilter(role.SystemID), sort);
			if (!string.IsNullOrWhiteSpace(role.ParentID) && role.ParentID.IsValidUUID())
				cacheKeys = cacheKeys.Concat(Extensions.GetRelatedCacheKeys(RoleProcessor.GetRolesFilter(role.SystemID, role.ParentID), sort));
			if (!string.IsNullOrWhiteSpace(oldParentID) && oldParentID.IsValidUUID())
				cacheKeys = cacheKeys.Concat(Extensions.GetRelatedCacheKeys(RoleProcessor.GetRolesFilter(role.SystemID, oldParentID), sort));
			return cacheKeys.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
		}

		internal static async Task<(IEnumerable<string> DataCacheKeys, IEnumerable<(string SetCacheKey, IEnumerable<string> SetCacheKeys)> HtmlCacheKeys)> GetCacheKeysAsync(this Site site, bool getDataCacheKeys, bool getHtmlCacheKeys, CancellationToken cancellationToken)
		{
			IEnumerable<string> dataCacheKeys = new List<string>();
			if (getDataCacheKeys)
			{
				var sort = Sorts<Site>.Ascending("PrimaryDomain").ThenByAscending("SubDomain").ThenByAscending("Title");
				dataCacheKeys = Extensions.GetRelatedCacheKeys(Filters<Site>.And(), Sorts<Site>.Ascending("Title"))
					.Concat(Extensions.GetRelatedCacheKeys(Filters<Site>.And(), sort))
					.Concat(Extensions.GetRelatedCacheKeys(Filters<Site>.And(Filters<Site>.Equals("SystemID", site.SystemID)), sort))
					.Concat(Extensions.GetRelatedCacheKeys(Filters<Site>.And(Filters<Site>.Equals("SystemID", site.SystemID)), Sorts<Site>.Ascending("Title")))
					.ToList();
			}

			var htmlCacheKeys = new List<(string SetCacheKey, IEnumerable<string> SetCacheKeys)>();
			if (getHtmlCacheKeys)
				htmlCacheKeys.Add(("Global", await site.GetSetCacheKeysAsync(cancellationToken).ConfigureAwait(false)));

			return (dataCacheKeys, htmlCacheKeys);
		}

		internal static async Task<(IEnumerable<string> DataCacheKeys, IEnumerable<(string SetCacheKey, IEnumerable<string> SetCacheKeys)> HtmlCacheKeys)> GetCacheKeysAsync(this Desktop desktop, string oldParentID, bool getDataCacheKeys, bool getHtmlCacheKeys, CancellationToken cancellationToken)
		{
			IEnumerable<string> dataCacheKeys = new List<string>();
			if (getDataCacheKeys)
			{
				var sort = Sorts<Desktop>.Ascending("Title");
				dataCacheKeys = Extensions.GetRelatedCacheKeys(DesktopProcessor.GetDesktopsFilter(desktop.SystemID, null), sort);
				if (!string.IsNullOrWhiteSpace(desktop.ParentID) && desktop.ParentID.IsValidUUID())
					dataCacheKeys = dataCacheKeys.Concat(Extensions.GetRelatedCacheKeys(DesktopProcessor.GetDesktopsFilter(desktop.SystemID, desktop.ParentID), sort));
				if (!string.IsNullOrWhiteSpace(oldParentID) && oldParentID.IsValidUUID())
					dataCacheKeys = dataCacheKeys.Concat(Extensions.GetRelatedCacheKeys(DesktopProcessor.GetDesktopsFilter(desktop.SystemID, oldParentID), sort));
			}

			var htmlCacheKeys = new List<(string SetCacheKey, IEnumerable<string> SetCacheKeys)>();
			if (getHtmlCacheKeys)
			{
				var cacheKey = desktop.GetDesktopCacheKey($"{desktop.Organization.URL}/{desktop.Alias}");
				var cacheKeys = new[] { cacheKey, $"{cacheKey}:time", $"{cacheKey}:expiration" }.Concat(await desktop.GetSetCacheKeysAsync(cancellationToken, true).ConfigureAwait(false));
				htmlCacheKeys.Add((desktop.GetSetCacheKey(), cacheKeys));
			}

			return (dataCacheKeys, htmlCacheKeys);
		}

		internal static async Task<(IEnumerable<string> DataCacheKeys, IEnumerable<(string SetCacheKey, IEnumerable<string> SetCacheKeys)> HtmlCacheKeys)> GetCacheKeysAsync(this Portlet portlet, bool getDataCacheKeys, bool getHtmlCacheKeys, bool getAllHtmlCacheKeys, CancellationToken cancellationToken)
		{
			IEnumerable<string> dataCacheKeys = new List<string>();
			if (getDataCacheKeys)
			{
				dataCacheKeys = Extensions.GetRelatedCacheKeys(Filters<Portlet>.And(Filters<Portlet>.Equals("DesktopID", portlet.DesktopID)), Sorts<Portlet>.Ascending("Zone").ThenByAscending("OrderIndex"));
				if (string.IsNullOrWhiteSpace(portlet.OriginalPortletID))
					dataCacheKeys = dataCacheKeys.Concat(Extensions.GetRelatedCacheKeys(Filters<Portlet>.Equals("OriginalPortletID", portlet.ID), Sorts<Portlet>.Ascending("DesktopID").ThenByAscending("Zone").ThenByAscending("OrderIndex")));
			}

			var htmlCacheKeys = new List<(string SetCacheKey, IEnumerable<string> SetCacheKeys)>();
			if (getHtmlCacheKeys)
			{
				if (getAllHtmlCacheKeys)
				{
					var dekstops = await portlet.GetDesktopsAsync(cancellationToken).ConfigureAwait(false);
					await dekstops.ForEachAsync(async (desktop, cancellationtoken) =>
					{
						var (_, desktopHtmlCacheKeys) = await desktop.GetCacheKeysAsync(null, false, true, cancellationtoken).ConfigureAwait(false);
						htmlCacheKeys.AddRange(desktopHtmlCacheKeys);
					}, cancellationToken, true, false).ConfigureAwait(false);
				}
				else
				{
					var (_, desktopHtmlCacheKeys) = await portlet.Desktop.GetCacheKeysAsync(null, false, true, cancellationToken).ConfigureAwait(false);
					htmlCacheKeys.AddRange(desktopHtmlCacheKeys);
				}
			}

			return (dataCacheKeys, htmlCacheKeys);
		}

		internal static async Task<(IEnumerable<(string SetCacheKey, IEnumerable<string> SetCacheKeys)> DataCacheKeys, IEnumerable<(string SetCacheKey, IEnumerable<string> SetCacheKeys)> HtmlCacheKeys)> GetCacheKeysAsync(this Category category, bool getDataCacheKeys, bool getHtmlCacheKeys, CancellationToken cancellationToken)
		{
			var childrenContentTypes = category.ContentType.GetChildren() ?? [];
			var linkContentTypes = new HashSet<ContentType>();

			var dataCacheKeys = new List<(string SetCacheKey, IEnumerable<string> SetCacheKeys)>();
			if (getDataCacheKeys)
			{
				var sort = Sorts<Category>.Ascending("OrderIndex").ThenByAscending("Title");
				var cacheKey = category.GetSetCacheKey();
				IEnumerable<string> cacheKeys = await Utility.Cache.GetSetMembersAsync(cacheKey, cancellationToken).ConfigureAwait(false) ?? [];
				cacheKeys = cacheKeys.Concat([Extensions.GetCacheKey(CategoryProcessor.GetCategoriesFilter(category.SystemID, category.RepositoryID, category.RepositoryEntityID, category.ID), sort, 0, 1)]);
				if (!string.IsNullOrWhiteSpace(category.ParentID))
					cacheKeys = cacheKeys.Concat([Extensions.GetCacheKey(CategoryProcessor.GetCategoriesFilter(category.SystemID, category.RepositoryID, category.RepositoryEntityID, category.ParentID), sort, 0, 1)]);
				dataCacheKeys.Add((cacheKey, cacheKeys));
				
				cacheKey = category.ContentType.GetSetCacheKey();
				cacheKeys = await Utility.Cache.GetSetMembersAsync(cacheKey, cancellationToken).ConfigureAwait(false) ?? [];
				if (cacheKeys.Any())
					dataCacheKeys.Add((cacheKey, cacheKeys));

				await childrenContentTypes.ForEachAsync(async (contentType, cancellationtoken) =>
				{
					cacheKey = contentType.GetSetCacheKey();
					cacheKeys = await Utility.Cache.GetSetMembersAsync(cacheKey, cancellationtoken).ConfigureAwait(false) ?? [];
					if (cacheKeys.Any())
						dataCacheKeys.Add((cacheKey, cacheKeys));
				}, cancellationToken, true, false).ConfigureAwait(false);

				IEnumerable<string> linkCacheKeys = new List<string>();
				var links = await category.FindLinksAsync(cancellationToken).ConfigureAwait(false) ?? [];
				await links.ForEachAsync(async (link, cancellationtoken) =>
				{
					linkCacheKeys = linkCacheKeys.Concat(Extensions.GetRelatedCacheKeys(link.GetCacheKey()));
					if (link.ContentType != null && linkContentTypes.Add(link.ContentType))
					{
						cacheKey = link.ContentType.GetSetCacheKey();
						cacheKeys = await Utility.Cache.GetSetMembersAsync(cacheKey, cancellationtoken).ConfigureAwait(false) ?? [];
						if (cacheKeys.Any())
							dataCacheKeys.Add((cacheKey, cacheKeys));
					}
				}, cancellationToken, true, false).ConfigureAwait(false);

				if (linkCacheKeys.Any())
					dataCacheKeys[0] = (dataCacheKeys[0].SetCacheKey, dataCacheKeys[0].SetCacheKeys.Concat(linkCacheKeys));
			}

			var htmlCacheKeys = new List<(string SetCacheKey, IEnumerable<string> SetCacheKeys)>();
			if (getHtmlCacheKeys)
			{
				htmlCacheKeys.Add(("Global", category.Organization.GetDesktopCacheKeys()));

				var cacheKey = category.GetSetCacheKey("HTMLs");
				var cacheKeys = await Utility.Cache.GetSetMembersAsync(cacheKey, cancellationToken).ConfigureAwait(false) ?? [];
				if (cacheKeys.Any())
					htmlCacheKeys.Add((cacheKey, cacheKeys));

				cacheKey = category.Desktop?.GetSetCacheKey();
				if (cacheKey != null)
				{
					cacheKeys = await Utility.Cache.GetSetMembersAsync(cacheKey, cancellationToken).ConfigureAwait(false) ?? [];
					if (cacheKeys.Any())
						htmlCacheKeys.Add((cacheKey, cacheKeys));
				}

				var desktops = category.FindDesktops();
				await desktops.ForEachAsync(async (desktop, cancellationtoken) =>
				{
					var cacheKey = desktop.GetSetCacheKey();
					var cacheKeys = await Utility.Cache.GetSetMembersAsync(cacheKey, cancellationtoken).ConfigureAwait(false) ?? [];
					if (cacheKeys.Any())
						htmlCacheKeys.Add((cacheKey, cacheKeys));
				}, cancellationToken, true, false).ConfigureAwait(false);

				if (linkContentTypes.Count < 1)
				{
					var links = await category.FindLinksAsync(cancellationToken).ConfigureAwait(false) ?? [];
					links.ForEach(link => linkContentTypes.Add(link.ContentType));
				}
				await linkContentTypes.ForEachAsync(async (contentType, cancellationtoken) =>
				{
					var (_, _, _, contentTypeHtmlCacheKeys) = await contentType.GetCacheKeysAsync(false, false, true, cancellationtoken).ConfigureAwait(false);
					htmlCacheKeys.AddRange(contentTypeHtmlCacheKeys);
				}, cancellationToken, true, false).ConfigureAwait(false);
			}

			return (dataCacheKeys, htmlCacheKeys);
		}

		internal static async Task<(IEnumerable<(string SetCacheKey, IEnumerable<string> SetCacheKeys)> DataCacheKeys, IEnumerable<(string SetCacheKey, IEnumerable<string> SetCacheKeys)> HtmlCacheKeys)> GetCacheKeysAsync(this IBusinessObject @object, bool getDataCacheKeys, bool getHtmlCacheKeys, CancellationToken cancellationToken)
		{
			if (@object is Category category)
				return await category.GetCacheKeysAsync(getDataCacheKeys, getHtmlCacheKeys, cancellationToken).ConfigureAwait(false);

			var dataCacheKeys = new List<(string SetCacheKey, IEnumerable<string> SetCacheKeys)>();
			var htmlCacheKeys = new List<(string SetCacheKey, IEnumerable<string> SetCacheKeys)>();

			if (@object.ContentType is ContentType contentType)
			{
				if (getDataCacheKeys)
				{
					var cacheKey = contentType.GetSetCacheKey();
					var cacheKeys = Extensions.GetRelatedCacheKeys(@object.GetCacheKey()).Concat(await Utility.Cache.GetSetMembersAsync(cacheKey, cancellationToken).ConfigureAwait(false) ?? []);

					if (@object is Content content)
						cacheKeys = cacheKeys.Concat([content.GetCacheKeyOfAlias()]);
					else if (@object is Item item)
						cacheKeys = cacheKeys.Concat([item.GetCacheKeyOfAlias()]);
					else if (@object is Link link)
					{
						var sort = Sorts<Link>.Ascending("OrderIndex").ThenByAscending("Title");
						cacheKeys = cacheKeys.Concat([Extensions.GetCacheKey(LinkProcessor.GetLinksFilter(link.SystemID, link.RepositoryID, link.RepositoryEntityID, link.ID), sort, 0, 1)]);
						if (!string.IsNullOrWhiteSpace(link.ParentID))
							cacheKeys = cacheKeys.Concat([Extensions.GetCacheKey(LinkProcessor.GetLinksFilter(link.SystemID, link.RepositoryID, link.RepositoryEntityID, link.ParentID), sort, 0, 1)]);
					}

					dataCacheKeys.Add((cacheKey, cacheKeys));
				}

				if (getHtmlCacheKeys)
				{
					var (_, _, _, contentTypeHtmlCacheKeys) = await contentType.GetCacheKeysAsync(false, false, true, cancellationToken).ConfigureAwait(false);
					htmlCacheKeys.AddRange(contentTypeHtmlCacheKeys);

					var desktops = await @object.FindDesktopsAsync(cancellationToken).ConfigureAwait(false);
					await desktops.ForEachAsync(async (desktop, cancellationtoken) =>
					{
						var cacheKey = desktop.GetSetCacheKey();
						var cacheKeys = await Utility.Cache.GetSetMembersAsync(cacheKey, cancellationtoken).ConfigureAwait(false) ?? [];
						if (cacheKeys.Any())
							htmlCacheKeys.Add((cacheKey, cacheKeys));
					}, cancellationToken, true, false).ConfigureAwait(false);
				}

				if (@object is Content contentObj && (getDataCacheKeys || getHtmlCacheKeys))
				{
					var (categoryDataCacheKeys, categoryHtmlCacheKeys) = await contentObj.Category.GetCacheKeysAsync(getDataCacheKeys, getHtmlCacheKeys, cancellationToken).ConfigureAwait(false);
					if (getDataCacheKeys)
						dataCacheKeys.AddRange(categoryDataCacheKeys);
					if (getHtmlCacheKeys)
						htmlCacheKeys.AddRange(categoryHtmlCacheKeys);
				}
			}

			return (dataCacheKeys, htmlCacheKeys);
		}

		internal static async Task<List<Desktop>> FindDesktopsAsync(this ContentType contentType, bool byPortlets, CancellationToken cancellationToken)
		{
			if (byPortlets)
			{
				var allDesktops = new Dictionary<string, Desktop>();
				var portlets = await contentType.FindPortletsAsync(cancellationToken).ConfigureAwait(false) ?? [];
				await portlets.Where(portlet => portlet != null).ForEachAsync(async (portlet, cancellationtoken) =>
				{
					var desktops = await portlet.GetDesktopsAsync(cancellationtoken).ConfigureAwait(false) ?? [];
					desktops.Where(desktop => desktop != null).ForEach(desktop => allDesktops.TryAdd(desktop.ID, desktop));
				}, cancellationToken, true, false).ConfigureAwait(false);
				return allDesktops.Values.ToList();
			}
			else
			{
				var desktops = new List<Desktop>();
				await new[] { contentType.DesktopID, contentType.Module?.DesktopID }
					.Where(desktopID => desktopID != null)
					.Distinct(StringComparer.OrdinalIgnoreCase)
					.ForEachAsync(async (desktopID, cancellationtoken) => desktops.Add(await desktopID.GetDesktopByIDAsync(cancellationtoken).ConfigureAwait(false)), cancellationToken, true, false).ConfigureAwait(false);
				if ("B0000000000000000000000000000001" == contentType.ContentTypeDefinitionID)
				{
					var (categories, _) = await contentType.FindCategoriesAsync(cancellationToken).ConfigureAwait(false);
					categories.ForEach(category =>
					{
						var categoryDesktops = category.FindDesktops();
						desktops.AddRange(categoryDesktops.Where(categoryDesktop => desktops.FirstOrDefault(desktop => desktop?.ID == categoryDesktop.ID) == null));
					});
				}
				return desktops.Where(desktop => desktop != null).ToList();
			}
		}

		internal static List<Desktop> FindDesktops(this Category category)
		{
			var desktops = new List<Desktop>();
			var desktop = category.Desktop;
			if (desktop != null)
				desktops.Add(desktop);
			var parentCategory = category.ParentCategory;
			while (parentCategory != null)
			{
				desktop = parentCategory.Desktop;
				if (desktop != null && desktops.FirstOrDefault(desktopObj => desktopObj.ID == desktop.ID) == null)
					desktops.Add(desktop);
				parentCategory = parentCategory.ParentCategory;
			}
			return desktops;
		}

		internal static async Task<List<Desktop>> FindDesktopsAsync(this IBusinessObject @object, CancellationToken cancellationToken)
		{
			var desktops = new List<Desktop>();
			var others = new List<Desktop>();
			if (@object is Category category)
			{
				desktops = category.FindDesktops();
				others = await category.ContentType.FindDesktopsAsync(false, cancellationToken).ConfigureAwait(false);
			}
			else if (@object is Content content)
			{
				desktops = content.Category?.FindDesktops() ?? [];
				(content.OtherCategories ?? []).Select(id => id.GetCategoryByID())
					.Where(category => category != null)
					.ForEach(category =>
					{
						var categoryDesktops = category.FindDesktops();
						desktops.AddRange(categoryDesktops.Where(categoryDesktop => desktops.FirstOrDefault(desktop => desktop.ID == categoryDesktop.ID) == null));
					});
				others = await content.ContentType.FindDesktopsAsync(false, cancellationToken).ConfigureAwait(false);
			}
			else if (@object is Item item)
				others = await item.ContentType.FindDesktopsAsync(false, cancellationToken).ConfigureAwait(false);
			return desktops.Where(desktop => desktop != null).Concat(others.Where(desktop => desktop != null && desktops.FirstOrDefault(desktopObj => desktopObj?.ID == desktop.ID) == null)).ToList();
		}

		internal static async Task<List<Desktop>> FindDesktopsAsync(this Expression expression, CancellationToken cancellationToken)
		{
			var allDesktops = new Dictionary<string, Desktop>();
			var portlets = await expression.FindPortletsAsync(cancellationToken).ConfigureAwait(false) ?? [];
			await portlets.Where(portlet => portlet != null).ForEachAsync(async (portlet, cancellationtoken) =>
			{
				var desktops = await portlet.GetDesktopsAsync(cancellationtoken).ConfigureAwait(false) ?? [];
				desktops.Where(desktop => desktop != null).ForEach(desktop => allDesktops.TryAdd(desktop.ID, desktop));
			}, cancellationToken, true, false).ConfigureAwait(false);
			return allDesktops.Values.ToList();
		}

		internal static Task<List<Portlet>> FindPortletsAsync(this ContentType contentType, CancellationToken cancellationToken)
			=> Portlet.FindAsync(Filters<Portlet>.And(Filters<Portlet>.Equals("RepositoryEntityID", contentType.ID), Filters<Portlet>.IsNull("OriginalPortletID")), Sorts<Portlet>.Ascending("DesktopID").ThenByAscending("Zone").ThenByAscending("OrderIndex"), cancellationToken);

		internal static Task<List<Portlet>> FindPortletsAsync(this Expression expression, CancellationToken cancellationToken)
			=> Portlet.FindAsync(Filters<Portlet>.And(Filters<Portlet>.Equals("ExpressionID", expression.ID), Filters<Portlet>.IsNull("OriginalPortletID")), Sorts<Portlet>.Ascending("DesktopID").ThenByAscending("Zone").ThenByAscending("OrderIndex"), cancellationToken);

		internal static async Task<(List<Category> Objects, string CacheKeyOfObjects)> FindCategoriesAsync(this ContentType contentType, CancellationToken cancellationToken)
		{
			var filter = CategoryProcessor.GetCategoriesFilter(contentType.SystemID, contentType.RepositoryID, contentType.ID);
			var sort = Sorts<Category>.Ascending("OrderIndex").ThenByAscending("Title");
			var cacheKeyOfObjects = Extensions.GetCacheKey(filter, sort, 0, 1);
			var objects = await Category.FindAsync(filter, sort, 0, 1, contentType.ID, true, cacheKeyOfObjects, 0, cancellationToken).ConfigureAwait(false);
			return (objects, cacheKeyOfObjects);
		}

		internal static Task<(long TotalRecords, string CacheKeyOfTotalObjects)> CountContentsAsync(this ContentType contentType, string categoryID, CancellationToken cancellationToken)
			=> ContentProcessor.CountAsync(null, ContentProcessor.GetContentsFilter(contentType.SystemID, contentType.RepositoryID, contentType.ID, categoryID), Sorts<Content>.Descending("StartDate").ThenByDescending("PublishedTime"), contentType.ID, 0, cancellationToken);

		internal static async Task<(List<Content> Objects, string CacheKeyOfObjects)> FindContentsAsync(this ContentType contentType, string categoryID, long totalRecords, int pageSize, int pageNumber, CancellationToken cancellationToken)
		{
			var filter = ContentProcessor.GetContentsFilter(contentType.SystemID, contentType.RepositoryID, contentType.ID, categoryID);
			var sort = Sorts<Content>.Descending("StartDate").ThenByDescending("PublishedTime");
			var results = await ContentProcessor.SearchAsync(null, filter, sort, pageSize, pageNumber, contentType.ID, totalRecords, false, 0, 0, 0, cancellationToken).ConfigureAwait(false);
			return (results.Objects, Extensions.GetCacheKey(filter, sort, pageSize, pageNumber));
		}

		internal static async Task<(List<Link> Objects, string CacheKeyOfObjects)> FindLinksAsync(this ContentType contentType, CancellationToken cancellationToken)
		{
			var filter = LinkProcessor.GetLinksFilter(contentType.SystemID, contentType.RepositoryID, contentType.ID);
			var sort = Sorts<Link>.Ascending("OrderIndex").ThenByAscending("Title");
			var cacheKeyOfObjects = Extensions.GetCacheKey(filter, sort, 0, 1);
			var objects = await Link.FindAsync(filter, sort, 0, 1, contentType.ID, true, cacheKeyOfObjects, 0, cancellationToken).ConfigureAwait(false);
			return (objects, cacheKeyOfObjects);
		}

		internal static async Task<List<Item>> FindItemsAsync(this ContentType contentType, int pageSize, int pageNumber, CancellationToken cancellationToken)
		{
			var filter = ItemProcessor.GetItemsFilter(contentType.SystemID, contentType.RepositoryID, contentType.ID);
			var sort = Sorts<Item>.Descending("Created").ThenByAscending("Title");
			var cacheKeyOfObjects = Extensions.GetCacheKey(filter, sort, pageSize, pageNumber);
			var objects = await Item.FindAsync(filter, sort, pageSize, pageNumber, contentType.ID, true, cacheKeyOfObjects, 0, cancellationToken).ConfigureAwait(false);
			await Task.WhenAll
			(
				Utility.Cache.AddSetMembersAsync(contentType.ObjectCacheKeys, objects.Select(@object => @object.GetCacheKey()), cancellationToken),
				Utility.Cache.AddSetMembersAsync(contentType.GetSetCacheKey(), objects.Select(@object => @object.GetCacheKeyOfAlias()).Concat([cacheKeyOfObjects, Extensions.GetCacheKeyOfTotalObjects(filter, sort), Utility.GetCacheKeyOfPageSize(filter, sort)]), cancellationToken)
			).ConfigureAwait(false);
			return objects;
		}

		internal static Task<List<Link>> FindLinksAsync(this Category category, CancellationToken cancellationToken)
		{
			var filter = Filters<Link>.And
			(
				Filters<Link>.Equals("SystemID", category.SystemID),
				Filters<Link>.Equals("LookupRepositoryID", category.RepositoryID)
			);
			var sort = Sorts<Link>.Ascending("ParentID").ThenByAscending("OrderIndex");
			return Link.FindAsync(filter, sort, 0, 1, null, cancellationToken);
		}

		internal static async Task<(List<string> LinkURLs, List<string> CategoryURLs, List<string> ContentURLs, List<string> ItemURLs)> GetRefreshingURLsAsync(
			this Organization organization,
			bool getLinks,
			bool getCategories,
			bool getContents,
			bool getItems,
			int maxCategoryPageNumber,
			int maxContentPageNumber,
			DateTime? minPublishedTime,
			IEnumerable<Link> links,
			IEnumerable<Category> categories,
			string correlationID,
			CancellationToken cancellationToken)
		{
			var linkURLs = new List<string>();
			var categoryURLs = new List<string>();
			var contentURLs = new List<string>();
			var itemURLs = new List<string>();
			var organizationURL = organization.URL;
			correlationID ??= UtilityService.NewUUID;

			async Task getLinkURLsAsync(Link link)
			{
				await Utility.Cache.AddSetMemberAsync(link.ContentType.ObjectCacheKeys, link.GetCacheKey(), cancellationToken).ConfigureAwait(false);

				var url = link.GetURL();
				if (url.IsStartsWith("~/") || url.IsStartsWith(organizationURL))
					linkURLs.Add(url);

				var children = await link.FindChildrenAsync(cancellationToken).ConfigureAwait(false) ?? [];
				if (children.Count > 0)
					await children.Where(child => child.Status == ApprovalStatus.Published).ForEachAsync(child => getLinkURLsAsync(child)).ConfigureAwait(false);
			}

			async Task getContentURLsAsync(Category category, IEnumerable<ContentType> contentTypes)
			{
				await Utility.Cache.AddSetMemberAsync(category.ContentType.ObjectCacheKeys, category.GetCacheKey(), cancellationToken).ConfigureAwait(false);

				var categoryURL = category.GetURL(true);
				if (categoryURL.IsStartsWith("~/") || categoryURL.IsStartsWith(organizationURL))
				{
					if (getCategories)
						categoryURLs.AddRange(categoryURL.GetPaginatingURLs(maxCategoryPageNumber));

					if (getContents)
						await contentTypes.ForEachAsync(async (contentType, cancellationtoken) =>
						{
							var (totalRecords, cacheKeyOfTotalObjects) = await contentType.CountContentsAsync(category.ID, cancellationtoken).ConfigureAwait(false);
							await Utility.Cache.AddSetMemberAsync(contentType.GetSetCacheKey(), cacheKeyOfTotalObjects, cancellationtoken).ConfigureAwait(false);
							if (totalRecords > 0)
							{
								var pageNumber = 0;
								var pageSize = 20;
								var totalPages = (totalRecords, pageSize).GetTotalPages();
								while (pageNumber < totalPages && (maxContentPageNumber > 0 ? pageNumber < maxContentPageNumber : true))
								{
									pageNumber++;
									var (contents, cacheKeyOfObjects) = await contentType.FindContentsAsync(category.ID, totalRecords, pageSize, pageNumber, cancellationtoken).ConfigureAwait(false);
									var cacheKeys = contents.Select(content => content.GetCacheKeyOfAlias()).Concat([cacheKeyOfObjects]);
									await Task.WhenAll
									(
										Utility.Cache.AddSetMembersAsync(contentType.GetSetCacheKey(), cacheKeys, cancellationtoken),
										Utility.Cache.AddSetMembersAsync(category.GetSetCacheKey(), cacheKeys, cancellationtoken)
									).ConfigureAwait(false);
									contents = contents.Where(content => content.Status == ApprovalStatus.Published).ToList();
									if (contents.Count > 0)
									{
										contentURLs.AddRange(contents.Where(content => minPublishedTime != null ? minPublishedTime.Value > content.PublishedTime.Value : true).Select(content => content.GetURL()));
										if (minPublishedTime != null && minPublishedTime.Value > contents.Last().PublishedTime.Value)
											break;
									}
								}
							}
						}, cancellationToken, true, false).ConfigureAwait(false);
				}

				var children = await category.FindChildrenAsync(cancellationToken).ConfigureAwait(false) ?? [];
				if (children.Count > 0)
					await children.Where(child => child.Status == ApprovalStatus.Published).ForEachAsync(child => getContentURLsAsync(child, contentTypes), true, false).ConfigureAwait(false);
			}

			if (getLinks)
			{
				if (links != null)
					try
					{
						await links.Where(link => link.Status == ApprovalStatus.Published).ForEachAsync(link => getLinkURLsAsync(link)).ConfigureAwait(false);
					}
					catch (TaskCanceledException) { }
					catch (OperationCanceledException) { }
					catch (Exception ex)
					{
						await Utility.WriteLogAsync(correlationID, $"Error occurred while preparing URL of links => {ex.Message}\r\nStack: {ex.StackTrace}", "Caches").ConfigureAwait(false);
					}
				else
					await organization.ContentTypesOfLink.ForEachAsync(async (contentType, cancellationtoken) =>
					{
						try
						{
							var (links, cacheKeyOfObjects) = await contentType.FindLinksAsync(cancellationtoken).ConfigureAwait(false);
							await Task.WhenAll
							(
								Utility.Cache.AddSetMembersAsync(contentType.ObjectCacheKeys, links.Where(link => link.Status != ApprovalStatus.Published).Select(link => link.GetCacheKey()), cancellationtoken),
								Utility.Cache.AddSetMemberAsync(contentType.GetSetCacheKey(), cacheKeyOfObjects, cancellationtoken)
							).ConfigureAwait(false);
							await links.Where(link => link.Status == ApprovalStatus.Published).ForEachAsync(link => getLinkURLsAsync(link)).ConfigureAwait(false);
						}
						catch (TaskCanceledException) { }
						catch (OperationCanceledException) { }
						catch (Exception ex)
						{
							await Utility.WriteLogAsync(correlationID, $"Error occurred while preparing URL of links => {ex.Message}\r\nStack: {ex.StackTrace}", "Caches").ConfigureAwait(false);
						}
					}, cancellationToken, true, false).ConfigureAwait(false);
			}

			if (getCategories || getContents)
			{
				if (categories != null)
					try
					{
						await categories.Where(category => category.Status == ApprovalStatus.Published).ForEachAsync(category => getContentURLsAsync(category, organization.ContentTypesOfContent), true, false).ConfigureAwait(false);
					}
					catch (TaskCanceledException) { }
					catch (OperationCanceledException) { }
					catch (Exception ex)
					{
						await Utility.WriteLogAsync(correlationID, $"Error occurred while preparing URL of categories/contents => {ex.Message}\r\nStack: {ex.StackTrace}", "Caches").ConfigureAwait(false);
					}
				else
					await organization.ContentTypesOfCategory.ForEachAsync(async (contentType, cancellationtoken) =>
					{
						try
						{
							var (categories, cacheKeyOfObjects) = await contentType.FindCategoriesAsync(cancellationtoken).ConfigureAwait(false);
							await Task.WhenAll
							(
								Utility.Cache.AddSetMembersAsync(contentType.ObjectCacheKeys, categories.Where(category => category.Status == ApprovalStatus.Published).Select(category => categories.GetCacheKey()), cancellationtoken),
								Utility.Cache.AddSetMemberAsync(contentType.GetSetCacheKey(), cacheKeyOfObjects, cancellationtoken)
							).ConfigureAwait(false);
							await categories.ForEachAsync(async (category, canceltoken) =>
							{
								var cat = (category?.ID ?? "").GetCategoryByID(false, false);
								if (cat != null)
									await cat.FindChildrenAsync(canceltoken).ConfigureAwait(false);
								else if (category?.Set() != null)
								{
									await category.FindChildrenAsync(canceltoken).ConfigureAwait(false);
									new CommunicateMessage(Utility.ServiceName)
									{
										Type = $"{category.GetObjectName()}#Update",
										Data = category.ToJson(true, false),
										ExcludedNodeID = Utility.NodeID
									}.Send();
								}
							}, cancellationtoken, true, false).ConfigureAwait(false);
							await categories.Select(category => category.ID.GetCategoryByID(false, false))
								.Where(category => category != null && category.Status == ApprovalStatus.Published)
								.ForEachAsync(category => getContentURLsAsync(category, organization.ContentTypesOfContent), true, false).ConfigureAwait(false);
						}
						catch (TaskCanceledException) { }
						catch (OperationCanceledException) { }
						catch (Exception ex)
						{
							await Utility.WriteLogAsync(correlationID, $"Error occurred while preparing URL of categories/contents => {ex.Message}\r\nStack: {ex.StackTrace}", "Caches").ConfigureAwait(false);
						}
					}, cancellationToken, true, false).ConfigureAwait(false);
			}

			if (getItems)
				await organization.ContentTypesOfItem.ForEachAsync(async (contentType, cancellationtoken) =>
				{
					try
					{
						var pageSize = 20;
						var pageNumber = 1;
						var maxPage = (maxContentPageNumber > 0 ? maxContentPageNumber : Utility.RefreshMaxPage) / 2;
						while (pageNumber <= maxPage)
						{
							var items = await contentType.FindItemsAsync(pageSize, pageNumber, cancellationtoken).ConfigureAwait(false);
							itemURLs.AddRange(items.Where(item => item.Status == ApprovalStatus.Published).Select(item => item.GetURL()));
							pageNumber++;
						}
					}
					catch (TaskCanceledException) { }
					catch (OperationCanceledException) { }
					catch (Exception ex)
					{
						await Utility.WriteLogAsync(correlationID, $"Error occurred while preparing URL of items => {ex.Message}\r\nStack: {ex.StackTrace}", "Caches").ConfigureAwait(false);
					}
				}, cancellationToken, true, false).ConfigureAwait(false);

			return (linkURLs, categoryURLs, contentURLs, itemURLs.Distinct(StringComparer.OrdinalIgnoreCase).ToList());
		}

		internal static void RemoveCache(this RequestInfo requestInfo, IEnumerable<string> cacheKeys)
		{
			if (requestInfo.IsForceCache())
				Utility.Cache.RemoveAsync(cacheKeys, Utility.CancellationToken).Execute();
		}
	}
}
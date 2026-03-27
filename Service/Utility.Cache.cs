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
using net.vieapps.Components.Utility;
#endregion

namespace net.vieapps.Services.Portals
{
	public static partial class Utility
	{
		public static Cache Cache { get; } = Cache.CreateInstance("VIEApps-Services-Portals", Components.Utility.Logger.GetLoggerFactory(), "true".IsEquals(UtilityService.GetAppSetting("Portals:Cache:L1")));

		public static bool IsCacheDisabled { get; internal set; } = "true".IsEquals(UtilityService.GetAppSetting("Portals:Cache:Disabled"));

		internal static string CloudFlareZoneID { get; } = UtilityService.GetAppSetting("Portals:CloudFlare:ZoneID");

		internal static string CloudFlareApiToken { get; } = UtilityService.GetAppSetting("Portals:CloudFlare:ApiToken");

		internal static bool CloudFlareForAll { get; } = "true".IsEquals(UtilityService.GetAppSetting("Portals:CloudFlare:All"));

		internal static string RefresherURL { get; } = UtilityService.GetAppSetting("Portals:Refresh:ReferURL", "https://vieapps.net/~url.refresher");

		internal static int RefreshTimeout { get; } = Int32.TryParse(UtilityService.GetAppSetting("Portals:Refresh:Timeout"), out var value) && value > 0 ? value : 30;

		internal static int RefreshMaxPage { get; } = Int32.TryParse(UtilityService.GetAppSetting("Portals:Refresh:MaxPage"), out var value) && value > 0 ? value : 30;

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
		public static async Task<string> SetCacheOfPageSizeAsync<T>(IFilterBy<T> filter, SortBy<T> sort, int pageSize, CancellationToken cancellationToken = default) where T : class
		{
			var cacheKey = $"{Extensions.GetCacheKey(filter, sort)}:size";
			await Utility.Cache.SetAsync(cacheKey, pageSize, cancellationToken).ConfigureAwait(false);
			return cacheKey;
		}

		internal static async Task PurgeCloudFlareCacheAsync(this IEnumerable<string> urls, string cloudflareZoneID, string cloudflareApiToken, string correlationID, CancellationToken cancellationToken, bool writeLogs = false)
		{
			var cloudflareURI = new Uri($"https://api.cloudflare.com/client/v4/zones/{cloudflareZoneID}/purge_cache");
			var cloudflareHeaders = new Dictionary<string, string>
			{
				["Content-Type"] = "application/json",
				["Authorization"] = $"Bearer {cloudflareApiToken}"
			};
			var cloudflareBody = new JObject
			{
				["purge_everything"] = true
			};

			async Task purgeAsync(JObject body)
			{
				try
				{
					using var _ = await cloudflareURI.SendHttpRequestAsync("POST", cloudflareHeaders, body.ToString(Newtonsoft.Json.Formatting.None), Utility.RefreshTimeout, cancellationToken).ConfigureAwait(false);
				}
				catch (Exception ex)
				{
					await Utility.WriteLogAsync(correlationID, $"Error occurred while purging CloudFlare cache => {(ex is RemoteServerException rex ? $"{rex.Message} (Code: {rex.StatusCode}){(string.IsNullOrWhiteSpace(rex.Body) ? "" : $"\r\nBody: {rex.Body}")}" : $"{ex.Message}")} [{ex.GetType()}]", "Caches").ConfigureAwait(false);
				}
			}

			if (urls.Count() > 0)
			{
				var cloudflareURLs = urls.Select(url => new[] { url, url.IsStartsWith("http://www.") || url.IsStartsWith("https://www.") ? url.Replace("//www.", "//") : url.Replace("//", "//www.") }).SelectMany(url => url).ToList();
				var pageNumber = 0;
				var pageSize = 25;
				var totalPages = Extensions.GetTotalPages(cloudflareURLs.Count, pageSize);
				while (pageNumber < totalPages)
				{
					cloudflareBody = new JObject
					{
						["files"] = cloudflareURLs.Skip(pageNumber * pageSize).Take(pageSize).ToJArray()
					};
					await purgeAsync(cloudflareBody).ConfigureAwait(false);
					pageNumber++;
				}
				if (Utility.IsPurgeCacheLogEnabled || writeLogs)
					await Utility.WriteLogAsync(correlationID, $"Purge CloudFlare cache successful\r\nURLs:\r\n- {urls.Join("\r\n- ")}", "Caches").ConfigureAwait(false);
			}
			else
			{
				await purgeAsync(cloudflareBody).ConfigureAwait(false);
				if (Utility.IsPurgeCacheLogEnabled || writeLogs)
					await Utility.WriteLogAsync(correlationID, "Purge CloudFlare cache successful [EVERYTHING]", "Caches").ConfigureAwait(false);
			}
		}

		internal static Task PurgeCloudFlareCacheAsync(this Organization organization, IEnumerable<string> urls, string correlationID, bool writeLogs, CancellationToken cancellationToken)
		{
			var suffix = organization.AlwaysUseHtmlSuffix ? ".html" : "";
			var purgeURLs = (urls ?? [])
				.Where(url => !string.IsNullOrWhiteSpace(url))
				.Select(url => url.IsContains("/{{pageNumber}}")
					? Enumerable.Range(1, Utility.RefreshMaxPage).Select(pageNumber => url.Replace("/{{pageNumber}}", pageNumber > 1 ? $"/{pageNumber}{suffix}" : suffix, StringComparison.OrdinalIgnoreCase))
					: new[] { url }
				)
				.SelectMany(url => url);
			var systemURLs = purgeURLs.Where(url => (url.IsContains("/_js/") || url.IsContains("/_css/") || url.IsContains("/_themes/")) && url.IsStartsWith(Utility.PortalsHttpURI)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
			var orgURLs = purgeURLs.Except(systemURLs).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
			return Task.WhenAll
			(
				!string.IsNullOrWhiteSpace(organization.CloudFlareZoneID) && !string.IsNullOrWhiteSpace(organization.CloudFlareApiToken)
					? orgURLs.PurgeCloudFlareCacheAsync(organization.CloudFlareZoneID, organization.CloudFlareApiToken, correlationID, cancellationToken, writeLogs)
					: Task.CompletedTask,
				systemURLs.Count > 0 && !string.IsNullOrWhiteSpace(Utility.CloudFlareZoneID) && !string.IsNullOrWhiteSpace(Utility.CloudFlareApiToken)
					? systemURLs.PurgeCloudFlareCacheAsync(Utility.CloudFlareZoneID, Utility.CloudFlareApiToken, correlationID, cancellationToken, writeLogs)
					: Task.CompletedTask
			);
		}

		internal static async Task PurgeCloudFlareCacheAsync(this IBusinessObject @object, string correlationID = null, bool writeLogs = false, CancellationToken cancellationToken = default)
		{
			if (@object.Organization is Organization organization)
			{
				var urls = await @object.GetURLsAsync(cancellationToken).ConfigureAwait(false);
				await organization.PurgeCloudFlareCacheAsync(urls, correlationID, writeLogs, cancellationToken).ConfigureAwait(false);
			}
		}

		internal static Task<IEnumerable<string>> GetURLsAsync(this IBusinessObject @object, CancellationToken cancellationToken = default)
		{
			/*
			if (@object.Organization is not Organization organization)
				return Task.FromResult<IEnumerable<string>>(Array.Empty<string>());

			var siteURL = $"{organization.DefaultSite?.GetURL()}/";
			var urls = new List<string> { @object.GetURL().Replace("~/", siteURL) };
			if (@object is Content content && content.Category != null)
				urls.Add(content.Category.GetURL(null, true).Replace("~/", siteURL));

			return Task.FromResult<IEnumerable<string>>(urls);
			*/
			return Task.FromResult<IEnumerable<string>>([]);
		}

		internal static async Task RefreshWebPagesAsync(this Organization organization, IEnumerable<string> urls, string correlationID, string log, bool force, bool writeLogs, CancellationToken cancellationToken)
		{
			var rootURL = organization.URL;
			var suffix = organization.AlwaysUseHtmlSuffix ? ".html" : "";
			var refreshURLs = (urls ?? [])
				.Where(url => !string.IsNullOrWhiteSpace(url))
				.Select(url => url.IsContains("/{{pageNumber}}")
					? Enumerable.Range(1, Utility.RefreshMaxPage).Select(pageNumber => url.Replace("/{{pageNumber}}", pageNumber > 1 ? $"/{pageNumber}{suffix}" : suffix, StringComparison.OrdinalIgnoreCase))
					: new[] { url }
				)
				.SelectMany(url => url)
				.Select(url => url.Replace("~/", rootURL + "/"))
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.Select(url => $"{url}{(force ? url.IsContains("x-force-cache") ? "" : $"{(url.IsContains("?") ? "&" : "?")}x-force-cache" : "")}")
				.ToList();
			await Task.WhenAll
			(
				writeLogs
				 ? Utility.WriteLogAsync(correlationID, $"{log ?? $"Refresh URLs of '{organization.Title}' [ID: {organization.ID}]"}\r\nURLs:\r\n- {refreshURLs.Join("\r\n- ")}", "Caches")
				 : Task.CompletedTask,
				refreshURLs.ForEachAsync((url, cancellationtoken) => url.RefreshWebPageAsync(correlationID, cancellationtoken), cancellationToken, true, false)
			).ConfigureAwait(false);
		}

		internal static Task RefreshWebPagesAsync(this Organization organization, IEnumerable<string> urls, string correlationID = null, string log = null, bool force = false, CancellationToken cancellationToken = default)
			=> organization.RefreshWebPagesAsync(urls, correlationID, log, force, false, cancellationToken);

		internal static async Task RefreshWebPageAsync(this string url, int delay, string correlationID = null, CancellationToken cancellationToken = default)
		{
			var stopwatch = Stopwatch.StartNew();
			var writeLogs = Utility.IsCacheLogEnabled;
			correlationID = correlationID ?? UtilityService.NewUUID;
			try
			{
				if (delay > 0)
					await Task.Delay(delay * 1000, cancellationToken).ConfigureAwait(false);
				await new Uri(url).FetchHttpAsync(Utility.RefresherHeaders, Utility.RefreshTimeout, cancellationToken).ConfigureAwait(false);
				stopwatch.Stop();
				if (writeLogs)
					await Utility.WriteLogAsync(correlationID, $"Refresh an url successful => {url}\r\nExecution times: {stopwatch.GetElapsedTimes()}", "Caches").ConfigureAwait(false);
			}
			catch (RemoteServerMovedException ex)
			{
				if (ex.InnerException is not ServiceOperationException && ex.InnerException is not ServiceNotFoundException)
					try
					{
						await ex.URI.FetchHttpAsync(Utility.RefresherHeaders, Utility.RefreshTimeout, cancellationToken).ConfigureAwait(false);
						stopwatch.Stop();
						if (writeLogs)
							await Utility.WriteLogAsync(correlationID, $"Refresh an url successful => {ex.URI}\r\nExecution times: {stopwatch.GetElapsedTimes()}", "Caches").ConfigureAwait(false);
					}
					catch (ConnectionTimeoutException) { }
					catch (Exception exception)
					{
						await Utility.WriteLogAsync(correlationID, $"Error occurred while refreshing an url ({ex.URI}) => {exception.Message} [{exception.GetType()}]", "Caches").ConfigureAwait(false);
					}
				else
					await Utility.WriteLogAsync(correlationID, $"Error occurred while refreshing an url ({url}) => {(ex.InnerException is RemoteServerException rex ? $"{rex.Message} (Code: {rex.StatusCode}){(string.IsNullOrWhiteSpace(rex.Body) ? "" : $"\r\nBody: {rex.Body}")}" : $"{ex.Message}")} [{ex.GetType()}]", "Caches").ConfigureAwait(false);
			}
			catch (TaskCanceledException) { }
			catch (OperationCanceledException) { }
			catch (ConnectionTimeoutException) { }
			catch (ServiceOperationException) { }
			catch (ServiceNotFoundException) { }
			catch (Exception ex)
			{
				await Utility.WriteLogAsync(correlationID, $"Error occurred while refreshing an url ({url}) => {(ex is RemoteServerException rex ? $"{rex.Message} (Code: {rex.StatusCode}){(string.IsNullOrWhiteSpace(rex.Body) ? "" : $"\r\nBody: {rex.Body}")}" : $"{ex.Message}")} [{ex.GetType()}]", "Caches").ConfigureAwait(false);
			}
		}

		internal static Task RefreshWebPageAsync(this string url, string correlationID = null, CancellationToken cancellationToken = default)
			=> (url ?? "").RefreshWebPageAsync(0, correlationID, cancellationToken);
	}
}
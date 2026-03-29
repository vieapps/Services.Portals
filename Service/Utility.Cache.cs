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

		internal static int RefreshTimeout { get; } = Int32.TryParse(UtilityService.GetAppSetting("Portals:Refresh:Timeout"), out var value) && value > 0 ? value : 15;

		internal static int RefreshMaxPage { get; } = Int32.TryParse(UtilityService.GetAppSetting("Portals:Refresh:MaxPage"), out var value) && value > 0 ? value : 20;

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

			async Task purgeCloudFlareCacheAsync(JObject body)
			{
				try
				{
					using var _ = await cloudflareURI.SendHttpRequestAsync("POST", cloudflareHeaders, body.ToString(Newtonsoft.Json.Formatting.None), Utility.RefreshTimeout, cancellationToken).ConfigureAwait(false);
				}
				catch (Exception ex)
				{
					await Utility.WriteLogAsync(correlationID, $"Error occurred while purging CloudFlare cache => {(ex is RemoteServerException rse ? $"{rse.Message} (Code: {rse.StatusCode}){(string.IsNullOrWhiteSpace(rse.Body) ? "" : $"\r\nBody: {rse.Body}")}" : $"{ex.Message}")} [{ex.GetType()}]", "Caches").ConfigureAwait(false);
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
					await purgeCloudFlareCacheAsync(cloudflareBody).ConfigureAwait(false);
					pageNumber++;
				}
				if (writeLogs || Utility.IsPurgeCacheLogEnabled)
					await Utility.WriteLogAsync(correlationID, $"Purge CloudFlare cache successful\r\nURLs:\r\n- {urls.Join("\r\n- ")}", "Caches").ConfigureAwait(false);
			}
			else
			{
				await purgeCloudFlareCacheAsync(cloudflareBody).ConfigureAwait(false);
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
		public static Task PurgeCloudFlareCacheAsync(this Organization organization, IEnumerable<string> urls, string correlationID, bool writeLogs, CancellationToken cancellationToken)
		{
			var purgeURLs = (urls ?? []).Where(url => !string.IsNullOrWhiteSpace(url)).Select(url => url.GetPaginatingURLs(Utility.RefreshMaxPage, organization.AlwaysUseHtmlSuffix ? ".html" : "")).SelectMany(url => url);
			var resourceURLs = purgeURLs.Where(url => url.IsContains("/_js/") || url.IsContains("/_css/") || url.IsContains("/_themes/") || url.IsContains("/_assets/")).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
			var systemURLs = purgeURLs.Where(url => url.IsStartsWith(Utility.PortalsHttpURI)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
			var orgURLs = purgeURLs.Except(systemURLs).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
			systemURLs = systemURLs.Concat(resourceURLs.Where(url => url.IsStartsWith(Utility.PortalsHttpURI))).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
			if (!string.IsNullOrWhiteSpace(organization.CloudFlareZoneID) && !string.IsNullOrWhiteSpace(organization.CloudFlareApiToken))
				orgURLs = orgURLs.Concat(resourceURLs.Where(url => !url.IsStartsWith(Utility.PortalsHttpURI))).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
			if (Utility.IsPurgeCacheLogEnabled || writeLogs)
				Utility.WriteLogAsync(correlationID, $"Prepare to purge CloudFlare cache of '{organization.Title}'\r\nOrganization URLs:\r\n- {(orgURLs.Count == 0 ? "None" : orgURLs.Join("\r\n- "))}\r\nSystem URLs:\r\n- {(systemURLs.Count == 0 ? "None" : systemURLs.Join("\r\n- "))}", "Caches").Execute();
			return Task.WhenAll
			(
				!string.IsNullOrWhiteSpace(organization.CloudFlareZoneID) && !string.IsNullOrWhiteSpace(organization.CloudFlareApiToken)
					? orgURLs.PurgeCloudFlareCacheAsync(organization.CloudFlareZoneID, organization.CloudFlareApiToken, correlationID, writeLogs, cancellationToken)
					: Task.CompletedTask,
				systemURLs.Count > 0 && !string.IsNullOrWhiteSpace(Utility.CloudFlareZoneID) && !string.IsNullOrWhiteSpace(Utility.CloudFlareApiToken)
					? systemURLs.PurgeCloudFlareCacheAsync(Utility.CloudFlareZoneID, Utility.CloudFlareApiToken, correlationID, writeLogs, cancellationToken)
					: Task.CompletedTask
			);
		}

		/// <summary>
		/// Purges cache of this object at CloudFlare CDN by URLs
		/// </summary>
		/// <param name="object"></param>
		/// <param name="correlationID"></param>
		/// <param name="writeLogs"></param>
		/// <param name="cancellationToken"></param>
		/// <returns></returns>
		public static async Task PurgeCloudFlareCacheAsync(this IBusinessObject @object, string correlationID = null, bool writeLogs = false, CancellationToken cancellationToken = default)
		{
			if (@object.Organization is Organization organization)
				await organization.PurgeCloudFlareCacheAsync([], correlationID, writeLogs, cancellationToken).ConfigureAwait(false);
		}

		/// <summary>
		/// Refreshs URLs of this organization
		/// </summary>
		/// <param name="organization"></param>
		/// <param name="urls"></param>
		/// <param name="correlationID"></param>
		/// <param name="log"></param>
		/// <param name="force"></param>
		/// <param name="writeLogs"></param>
		/// <param name="cancellationToken"></param>
		/// <returns></returns>
		public static async Task RefreshWebPagesAsync(this Organization organization, IEnumerable<string> urls, string correlationID, string message, bool force, bool writeLogs, CancellationToken cancellationToken)
		{
			var query = (force ? "x-force-cache&" : "") + "x-original-correlation-id=" + correlationID;
			var rootURL = (string.IsNullOrWhiteSpace(organization.CloudFlareZoneID) || string.IsNullOrWhiteSpace(organization.CloudFlareApiToken)
				? organization.URL
				: (organization.DefaultSite?.GetURL() ?? organization.URL)) + "/";
			var refreshURLs = (urls ?? [])
				.Where(url => !string.IsNullOrWhiteSpace(url))
				.Select(url => url.GetPaginatingURLs(Utility.RefreshMaxPage, organization.AlwaysUseHtmlSuffix ? ".html" : ""))
				.SelectMany(url => url)
				.Select(url => url.Replace("~/", rootURL) + (url.IsContains("?") ? "&" : "?") + query)
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.ToList();
			await Task.WhenAll
			(
				writeLogs
				 ? Utility.WriteLogAsync(correlationID, $"{message ?? $"Refresh URLs of '{organization.Title}' [ID: {organization.ID}]"}\r\nURLs:\r\n- {refreshURLs.Join("\r\n- ")}", "Caches")
				 : Task.CompletedTask,
				refreshURLs.ForEachAsync((url, cancellationtoken) => url.RefreshWebPageAsync(0, correlationID, force, cancellationtoken), cancellationToken, true, !force && Utility.RunProcessorInParallelsMode)
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
		public static Task RefreshWebPagesAsync(this Organization organization, IEnumerable<string> urls, string correlationID, string message, bool force, CancellationToken cancellationToken)
			=> organization.RefreshWebPagesAsync(urls, correlationID, message, force, false, cancellationToken);

		/// <summary>
		/// Refreshs a web-page by specified URL
		/// </summary>
		/// <param name="url"></param>
		/// <param name="delay"></param>
		/// <param name="correlationID"></param>
		/// <param name="writeLogs"></param>
		/// <param name="cancellationToken"></param>
		/// <returns></returns>
		public static async Task RefreshWebPageAsync(this string url, int delay, string correlationID, bool writeLogs, CancellationToken cancellationToken)
		{
			writeLogs = writeLogs || Utility.IsCacheLogEnabled;
			correlationID = correlationID ?? UtilityService.NewUUID;

			async Task refreshWebPageAsync(Uri uri, bool handleException)
			{
				try
				{
					var stopwatch = Stopwatch.StartNew();
					await uri.FetchHttpAsync(Utility.RefresherHeaders, Utility.RefreshTimeout, cancellationToken).ConfigureAwait(false);
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
						if (ex.Code != 522)
							await Utility.WriteLogAsync(correlationID, $"Error occurred while refreshing ({url}) => {ex.Message} [Code: {ex.StatusCode}]{(string.IsNullOrWhiteSpace(ex.Body) ? "" : $"\r\nBody: {ex.Body}")}", "Caches").ConfigureAwait(false);
					}
					else
						throw;
				}
				catch (Exception ex)
				{
					if (handleException)
						await Utility.WriteLogAsync(correlationID, $"Error occurred while refreshing ({url}) => {ex.Message} [{ex.GetType()}]", "Caches").ConfigureAwait(false);
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
				else
					await Utility.WriteLogAsync(correlationID, $"Error occurred while refreshing ({url}) => {ex.Message} [{ex.GetType()}]", "Caches").ConfigureAwait(false);
			}
		}
	}
}
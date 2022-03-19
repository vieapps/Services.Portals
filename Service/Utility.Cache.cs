#region Related components
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using net.vieapps.Components.Caching;
using net.vieapps.Components.Repository;
using net.vieapps.Components.Utility;
#endregion

namespace net.vieapps.Services.Portals
{
	public static partial class Utility
	{
		/// <summary>
		/// Gets the cache storage
		/// </summary>
		public static Cache Cache { get; } = new Cache("VIEApps-Services-Portals", Components.Utility.Logger.GetLoggerFactory());

		internal static bool IsDebugEnabled { get; } = Utility.Logger != null && Utility.Logger.IsEnabled(Microsoft.Extensions.Logging.LogLevel.Debug);

		internal static bool WriteCacheLogs { get; } = Utility.IsDebugEnabled || "true".IsEquals(UtilityService.GetAppSetting("Logs:Portals:Caches"));

		/// <summary>
		/// Gets the refer URL of the refresher
		/// </summary>
		internal static string RefresherURL { get; } = UtilityService.GetAppSetting("Portals:RefresherURL", "https://portals.vieapps.net/~url.refresher");

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
		/// Gets the set of keys that used to store HTML cache of this desktop
		/// </summary>
		/// <param name="desktop"></param>
		/// <param name="cancellationToken"></param>
		/// <param name="staticIncluded"></param>
		/// <returns></returns>
		public static async Task<List<string>> GetSetCacheKeysAsync(this Desktop desktop, CancellationToken cancellationToken = default, bool staticIncluded = false)
			=> (staticIncluded ? new[] { $"css#d_{desktop.ID}", $"css#d_{desktop.ID}:time", $"js#d_{desktop.ID}", $"js#d_{desktop.ID}:time" } : Array.Empty<string>())
				.Concat(desktop != null ? await Utility.Cache.GetSetMembersAsync(desktop.GetSetCacheKey(), cancellationToken).ConfigureAwait(false) : new HashSet<string>())
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.ToList();

		/// <summary>
		/// Gets the set of keys that used to store HTML cache of this collection of desktops
		/// </summary>
		/// <param name="desktops"></param>
		/// <param name="cancellationToken"></param>
		/// <param name="staticIncluded"></param>
		/// <returns></returns>
		public static async Task<List<string>> GetSetCacheKeysAsync(this IEnumerable<Desktop> desktops, CancellationToken cancellationToken = default, bool staticIncluded = false)
		{
			var keys = new List<string>();
			if (desktops != null)
				await desktops.Where(desktop => desktop != null).ForEachAsync(async desktop => keys = keys.Concat(await desktop.GetSetCacheKeysAsync(cancellationToken, staticIncluded).ConfigureAwait(false) ?? new List<string>()).ToList(), true, false).ConfigureAwait(false);
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
			var portlets = await Portlet.FindAsync<Portlet>(filter, sort, 0, 1, null, false, null, 0, cancellationToken).ConfigureAwait(false);
			await portlets.Where(portlet => portlet != null).ForEachAsync(async portlet =>
			{
				var dekstops = await portlet.GetDesktopsAsync(cancellationToken).ConfigureAwait(false);
				keys = keys.Concat(await dekstops.GetSetCacheKeysAsync(cancellationToken).ConfigureAwait(false) ?? new List<string>()).ToList();
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
			var portlets = await Portlet.FindAsync<Portlet>(filter, sort, 0, 1, null, false, null, 0, cancellationToken).ConfigureAwait(false);
			await portlets.Where(portlet => portlet != null).ForEachAsync(async portlet =>
			{
				var dekstops = await portlet.GetDesktopsAsync(cancellationToken).ConfigureAwait(false);
				keys = keys.Concat(await dekstops.GetSetCacheKeysAsync(cancellationToken).ConfigureAwait(false) ?? new List<string>()).ToList();
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
			var theme = site.Theme ?? site.Organization?.Theme ?? "defaut";
			return new[] { "css#defaut", "css#defaut:time", "js#defaut", "js#defaut:time", $"css#{theme}", $"css#{theme}:time", $"js#{theme}", $"js#{theme}:time" }
				.Concat(new[] { $"css#s_{site.ID}", $"css#s_{site.ID}:time", $"js#s_{site.ID}", $"js#s_{site.ID}:time" })
				.Concat(await Utility.Cache.GetSetMembersAsync($"statics:{theme}", cancellationToken).ConfigureAwait(false) ?? new HashSet<string>())
				.Concat(site.Organization != null ? await site.Organization.GetSetCacheKeysAsync(cancellationToken).ConfigureAwait(false) : new List<string>())
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
			return new[] { "css#defaut", "css#defaut:time", "js#defaut", "js#defaut:time", $"css#{theme}", $"css#{theme}:time", $"js#{theme}", $"js#{theme}:time" }
				.Concat(new[] { $"js#o_{organization.ID}", $"js#o_{organization.ID}:time" })
				.Concat(await Utility.Cache.GetSetMembersAsync($"statics:{theme}", cancellationToken).ConfigureAwait(false) ?? new HashSet<string>())
				.Concat(await Utility.Cache.GetSetMembersAsync("statics", cancellationToken).ConfigureAwait(false) ?? new HashSet<string>())
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.ToList();
		}

		/// <summary>
		/// Gets the key for storing HTML code of a desktop that specified by alias and requested URL
		/// </summary>
		/// <param name="organization"></param>
		/// <param name="desktopAlias"></param>
		/// <param name="requestURI"></param>
		/// <returns></returns>
		public static string GetDesktopCacheKey(this Organization organization, string desktopAlias, Uri requestURI)
		{
			var path = requestURI.AbsolutePath.ToLower();
			while (path.EndsWith("/") || path.EndsWith("."))
				path = path.Left(path.Length - 1).Trim();
			path = path.IsStartsWith($"/~{organization.Alias}") ? path.Right(path.Length - organization.Alias.Length - 2) : path;
			path = path.IsEndsWith(".html") || path.IsEndsWith(".aspx") ? path.Left(path.Length - 5) : path.IsEndsWith(".php") ? path.Left(path.Length - 4) : path;
			path = path.Equals("") || path.Equals("/") || path.Equals("/index") || path.Equals("/default") ? desktopAlias : path;
			return $"{organization.ID}:" + (desktopAlias.IsEquals("-default") || desktopAlias.IsEquals(organization.HomeDesktop?.Alias) ? "-default" : path).GenerateUUID();
		}

		/// <summary>
		/// Gets the key for storing HTML code of a desktop that specified by alias and requested URL
		/// </summary>
		/// <param name="desktop"></param>
		/// <param name="requestURI"></param>
		/// <returns></returns>
		public static string GetDesktopCacheKey(this Desktop desktop, Uri requestURI)
			=> desktop.Organization.GetDesktopCacheKey(desktop.Alias, requestURI);

		/// <summary>
		/// Gets the key for storing HTML code of a desktop that specified by alias and requested URL
		/// </summary>
		/// <param name="desktop"></param>
		/// <param name="requestURL"></param>
		/// <returns></returns>
		public static string GetDesktopCacheKey(this Desktop desktop, string requestURL)
			=> desktop.GetDesktopCacheKey(new Uri(requestURL.IsStartsWith("http://") || requestURL.IsStartsWith("https://") ? requestURL : "https://site.vieapps.net/" + (requestURL.Equals("#") ? "" : requestURL.Replace("~/", ""))));

		/// <summary>
		/// Gets all the keys for storing HTML code of a desktop that specified by alias and requested URL
		/// </summary>
		/// <param name="organization"></param>
		/// <param name="desktopAlias"></param>
		/// <param name="requestURI"></param>
		/// <returns></returns>
		public static List<string> GetDesktopCacheKeys(this Organization organization, string desktopAlias, Uri requestURI)
		{
			var cacheKey = organization.GetDesktopCacheKey(desktopAlias, requestURI);
			return new[] { cacheKey }.Concat(new[] { $"{cacheKey}:time", $"{cacheKey}:expiration" }).ToList();
		}

		/// <summary>
		/// Gets all the keys for storing HTML code of a desktop that specified by alias and requested URL
		/// </summary>
		/// <param name="desktop"></param>
		/// <param name="requestURI"></param>
		/// <returns></returns>
		public static List<string> GetDesktopCacheKeys(this Desktop desktop, Uri requestURI)
			=> desktop.Organization.GetDesktopCacheKeys(desktop.Alias, requestURI);

		/// <summary>
		/// Gets all the keys for storing HTML code of a desktop that specified by alias and requested URL
		/// </summary>
		/// <param name="desktop"></param>
		/// <param name="requestURL"></param>
		/// <returns></returns>
		public static List<string> GetDesktopCacheKeys(this Desktop desktop, string requestURL)
			=> desktop.GetDesktopCacheKeys(new Uri(requestURL.IsStartsWith("http://") || requestURL.IsStartsWith("https://") ? requestURL : "https://site.vieapps.net/" + (requestURL.Equals("#") ? "" : requestURL.Replace("~/", ""))));

		/// <summary>
		/// Gets all the keys for storing HTML code of all home desktops
		/// </summary>
		/// <param name="organization"></param>
		/// <returns></returns>
		internal static List<string> GetDesktopCacheKey(this Organization organization)
		{
			var cacheKeys = new List<string>
			{
				organization.HomeDesktop?.GetDesktopCacheKey($"{Utility.PortalsHttpURI}/~{organization.Alias}/{organization.HomeDesktop?.Alias}"),
				$"{organization.ID}:{organization.HomeDesktop?.Alias.GenerateUUID()}"
			};
			if (organization.Sites != null && organization.Sites.Count > 0)
				cacheKeys = cacheKeys.Concat(organization.Sites.Select(site => site.HomeDesktop?.GetDesktopCacheKey($"{Utility.PortalsHttpURI}/~{organization.Alias}/{site.HomeDesktop?.Alias}"))).ToList();
			cacheKeys = cacheKeys.Where(cacheKey => cacheKey != null).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
			return cacheKeys.Concat(cacheKeys.Select(cacheKey => new[] { $"{cacheKey}:time", $"{cacheKey}:expiration" }).SelectMany(keys => keys)).ToList();
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
		public static Task SetCacheOfPageSizeAsync<T>(IFilterBy<T> filter, SortBy<T> sort, int pageSize, CancellationToken cancellationToken = default) where T : class
			=> Utility.Cache.SetAsync($"{Extensions.GetCacheKey(filter, sort)}:size", pageSize, cancellationToken);

		/// <summary>
		/// Refreshs a web page
		/// </summary>
		/// <param name="url"></param>
		/// <param name="delay"></param>
		/// <param name="correlationID"></param>
		/// <param name="message"></param>
		/// <returns></returns>
		internal static async Task RefreshWebPageAsync(this string url, int delay = 0, string correlationID = null, string message = null)
		{
			try
			{
				if (delay > 0)
					await Task.Delay(delay * 1000).ConfigureAwait(false);
				await new Uri(url).FetchHttpAsync(new Dictionary<string, string> { ["Referer"] = Utility.RefresherURL, ["User-Agent"] = $"{UtilityService.DesktopUserAgent} NGX-Refresher/{typeof(DesktopProcessor).Assembly.GetVersion(false)}" }, 13, Utility.CancellationToken).ConfigureAwait(false);
				if (Utility.WriteCacheLogs)
					await Utility.WriteLogAsync(correlationID ?? UtilityService.NewUUID, $"{message ?? "Refresh an url successful"} => {url}", Utility.CancellationToken, "Caches").ConfigureAwait(false);
			}
			catch (Exception ex)
			{
				await Utility.WriteLogAsync(correlationID ?? UtilityService.NewUUID, $"Error occurred while refreshing an url ({url}) => {ex.Message} [{ex.GetType()}]", Utility.CancellationToken, "Caches").ConfigureAwait(false);
			}
		}
	}
}
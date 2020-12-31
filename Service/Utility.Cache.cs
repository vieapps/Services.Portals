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
		public static Cache Cache { get; } = new Cache("VIEApps-Services-Portals", UtilityService.GetAppSetting("Cache:ExpirationTime", "30").CastAs<int>(), false, UtilityService.GetAppSetting("Cache:Provider"), Components.Utility.Logger.GetLoggerFactory());

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
		/// <returns></returns>
		public static string GetSetCacheKey(this ContentType contentType)
			=> contentType.Organization.GetSetCacheKey($"ContentType:{contentType.ID}");

		/// <summary>
		/// Gets the key for storing a set of keys that related to an expression
		/// </summary>
		/// <param name="expression"></param>
		/// <returns></returns>
		public static string GetSetCacheKey(this Expression expression)
			=> expression.Organization.GetSetCacheKey($"Expression:{expression.ID}");

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
		/// <param name="category"></param>
		/// <returns></returns>
		public static string GetSetCacheKey(this Category category)
			=> category.Organization.GetSetCacheKey($"Category:{category.ID}");

		/// <summary>
		/// Gets the key for storing HTML code of a desktop that specified by alias and requested URL
		/// </summary>
		/// <param name="organization"></param>
		/// <param name="desktopAlias"></param>
		/// <param name="requestURI"></param>
		/// <returns></returns>
		public static string GetDesktopCacheKey(this Organization organization, string desktopAlias, Uri requestURI)
		{
			var path = requestURI.AbsolutePath;
			path = path.IsStartsWith($"/~{organization.Alias}") ? path.Right(path.Length - organization.Alias.Length - 2) : path;
			path = (path.IsEndsWith(".html") || path.IsEndsWith(".aspx") ? path.Left(path.Length - 5) : path).ToLower();
			path = path.Equals("") || path.Equals("/") || path.Equals("/index") || path.Equals("/default") ? desktopAlias : path;
			return $"{organization.ID}:" + ("-default".IsEquals(desktopAlias) ? desktopAlias : path).GenerateUUID();
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

		internal static List<string> GetDesktopCacheKey(this Organization organization)
		{
			var cacheKeys = new List<string>
			{
				organization.HomeDesktop?.GetDesktopCacheKey("https://site.vieapps.net/")
			};
			if (organization.Sites != null && organization.Sites.Count > 0)
				cacheKeys = cacheKeys.Concat(organization.Sites.Select(site => site.HomeDesktop?.GetDesktopCacheKey("https://site.vieapps.net/"))).ToList();
			cacheKeys = cacheKeys.Where(cacheKey => cacheKey != null).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
			return cacheKeys.Concat(cacheKeys.Select(cacheKey => $"{cacheKey}:time")).ToList();
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

		internal static async Task RefreshWebPageAsync(this string url, int delay = 0, string correlationID = null)
		{
			try
			{
				if (delay > 0)
					await Task.Delay(delay * 1000).ConfigureAwait(false);
				await UtilityService.GetWebPageAsync(url, "https://portals.vieapps.net/~refresher").ConfigureAwait(false);
				if (Utility.Logger.IsEnabled(Microsoft.Extensions.Logging.LogLevel.Debug))
					await Utility.WriteLogAsync(correlationID ?? UtilityService.NewUUID, $"Refresh an url successful [{url}]", CancellationToken.None, "Caches").ConfigureAwait(false);
			}
			catch (Exception ex)
			{
				await Utility.WriteLogAsync(correlationID ?? UtilityService.NewUUID, $"Error occurred while refreshing an url [{url}] => {ex.Message} [{ex.GetType()}]", CancellationToken.None, "Caches").ConfigureAwait(false);
			}
		}
	}
}
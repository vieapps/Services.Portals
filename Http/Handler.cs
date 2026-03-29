#region Related components
using System;
using System.IO;
using System.Net;
using System.Linq;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Concurrent;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using WampSharp.V2.Core.Contracts;
using net.vieapps.Components.Caching;
using net.vieapps.Components.Security;
using net.vieapps.Components.Utility;
#endregion

namespace net.vieapps.Services.Portals
{
	public class Handler
	{
		public Handler(RequestDelegate _) { }

		public async Task Invoke(HttpContext context)
		{
			if (!context.Request.Method.IsEquals("OPTIONS"))
			{
				await this.ProcessRequestAsync(context).ConfigureAwait(false);
				if (!context.WebSockets.IsWebSocketRequest && Global.IsVisitLogEnabled)
					await context.WriteVisitFinishingLogAsync().ConfigureAwait(false);
			}
		}

		#region Properties
		static HashSet<string> Validators { get; } = "_validator,validator.aspx".ToHashSet();

		static HashSet<string> Initializers { get; } = "_initializer,_activate,initializer.aspx,activate.aspx,initializer.html,activate.html,initializer.php,activate.php".ToHashSet();

		static HashSet<string> LogIns { get; } = "_login,login.aspx,signin.aspx,login.html,signin.html,login.php,signin.php".ToHashSet();

		static HashSet<string> LogOuts { get; } = "_logout,logout.aspx,signout.aspx,logout.html,signout.html,logout.php,signout.php".ToHashSet();

		static HashSet<string> CmsPortals { get; } = "_confirm,_unsubscribe,_image,_visit,_admin,_cms,_edit,_update,admin.aspx,cms.aspx,admin.html,cms.html,admin.php,cms.php".ToHashSet();

		static HashSet<string> Feeds { get; } = "feed,feed.xml,feed.json,atom,atom.xml,atom.json,rss,rss.xml,rss.json".ToHashSet();

		internal static bool UseShortURLs { get; } = "true".IsEquals(UtilityService.GetAppSetting("Portals:UseShortURLs", "true"));

		static string LoadBalancerHealthCheckURL { get; } = UtilityService.GetAppSetting("LoadBalancer:HealthCheckURL", "/load-balancer-health-check");

		internal static List<string> ExcludedHeaders { get; } = UtilityService.GetAppSetting("ExcludedHeaders", "connection,accept,accept-encoding,accept-language,cache-control,cookie,host,content-type,content-length,user-agent,upgrade-insecure-requests,priority,purpose,pragma,ms-aspnetcore-token,x-forwarded-for,x-forwarded-proto,x-forwarded-port,x-original-for,x-original-proto,x-original-remote-endpoint,x-original-port,cdn-loop").ToList();

		internal static Cache Cache { get; set; }

		internal static string MonitorLogPath { get; set; }

		internal static IDisposable CacheUpdater { get; set; }

		internal static IDisposable CacheCommunicator { get; set; }

		internal static bool AllowCache { get; } = "true".IsEquals(UtilityService.GetAppSetting("Portals:Cache:Allow", "true"));

		internal static int CacheMaxAge { get; set; }

		internal static bool TrackSessions { get; set; } = "true".IsEquals(UtilityService.GetAppSetting("Sessions:Track", "true"));

		internal static bool TrackPortalStatistics { get; set; } = Handler.TrackSessions || "true".IsEquals(UtilityService.GetAppSetting("Sessions:Track:Portals", "true"));

		internal static bool TrackAPIStatistics { get; set; } = Handler.TrackSessions && "true".IsEquals(UtilityService.GetAppSetting("Sessions:Track:APIs", "false"));

		internal static string CrossOrigin { get; } = "true".IsEquals(UtilityService.GetAppSetting("Portals:Desktops:Resources:CrossOrigin")) ? "use-credentials" : "anonymous";

		internal static string RefresherURL { get; } = UtilityService.GetAppSetting("Portals:RefresherURL", "https://vieapps.net/~url.refresher");

		internal static int ExpiresAfter { get; } = Int32.TryParse(UtilityService.GetAppSetting("Authenticator:TokenExpiresAfter", "0"), out var expiresAfter) && expiresAfter > -1 ? expiresAfter : 0;

		internal static List<string> LegacyParameters { get; } = UtilityService.GetAppSetting("Portals:LegacyParameters", "desktop;catName;contId;page").ToList(";");

		internal static List<string> PreventingPaths { get; } = UtilityService.GetAppSetting("Portals:PreventingPaths", "").ToList(";");

		internal static string PortalsHttpURI { get; } = UtilityService.GetAppSetting("HttpUri:Portals", "https://portals.vieapps.net");

		internal static string PortalsHttpHost { get; } = new Uri(Handler.PortalsHttpURI).Host;

		internal static string PortalsWebSocketURI	{ get; } = UtilityService.GetAppSetting("HttpUri:WebSockets", Handler.PortalsHttpURI);

		internal static string CMSPortalsHttpURI	{ get; } = UtilityService.GetAppSetting("HttpUri:CMSPortals", "https://cms.vieapps.net");

		internal static string FilesHttpURI { get; } = UtilityService.GetAppSetting("HttpUri:Files", "https://fs.vieapps.net");
		#endregion

		Task ProcessRequestAsync(HttpContext context)
		{
			// WebSocket
			if (context.WebSockets.IsWebSocketRequest)
				return Task.WhenAll
				(
					Global.IsVisitLogEnabled ? context.WriteLogsAsync(Global.Logger, "APIs", $"Wrap a WebSocket connection successful\r\n- Endpoint: {context.GetRemoteIPAddress()}:{context.Connection.RemotePort}\r\n- URI: {context.GetRequestUri()}{(Global.IsDebugLogEnabled ? $"\r\n- Headers:\r\n\t{context.Request.Headers.Select(kvp => $"{kvp.Key}: {kvp.Value}").Join("\r\n\t")}" : "")}") : Task.CompletedTask,
					APIsHandler.WebSocket.WrapAsync(context)
				);

			// load balancer
			if (context.Request.Path.Value.IsEquals(Handler.LoadBalancerHealthCheckURL))
				return context.WriteAsync("OK", "text/plain", null, 0, "private, no-cache, no-store", TimeSpan.Zero, null, Global.CancellationToken);

			// HTTP
			var requestURI = context.GetRequestUri();
			var requestSegments = requestURI.GetRequestPathSegments();
			var requestPath = requestSegments.First().ToLower();
			return requestPath.IsEquals("favicon.ico") && requestURI.Host.IsEquals(Handler.PortalsHttpHost)
				? context.ProcessFavouritesIconFileRequestAsync()
				: Global.StaticSegments.Contains(requestPath)
					? context.ProcessStaticFileRequestAsync()
					: ".well-known".IsEquals(requestPath)
						? context.ProcessAPIsRequestAsync(requestSegments)
						: this.ProcessPortalRequestAsync(context);
		}

		async Task ProcessPortalRequestAsync(HttpContext context)
		{
			// first-step: process L1-cache
			var stopwatch = Stopwatch.StartNew();
			var requestURI = context.GetRequestUri();
			var requestMethod = (context.Request.Method ?? "GET").ToUpper();

			var isDebugLogEnabled = Global.IsDebugLogEnabled || context.ContainsKey("x-logs") || context.ContainsKey("x-cache-logs");
			if (isDebugLogEnabled || Global.IsVisitLogEnabled)
				await context.WriteLogsAsync("Http.Process.Requests", $"Start process a request of CMS Portals [{requestMethod} {requestURI}]").ConfigureAwait(false);

			var isForceCacheRequested = context.ContainsKey("x-force-cache") || context.ContainsKey("x-no-cache") || context.ContainsKey("x-bypass-cache");
			if (requestMethod == "GET" && await context.ProcessL1CacheAsync(isForceCacheRequested, stopwatch).ConfigureAwait(false))
				return;

			// gathering the requesting information
			var correlationID = context.GetCorrelationID();
			var stepwatch = Stopwatch.StartNew();
			var session = context.GetSession();
			var requestSegments = new List<string>();

			var isMobile = string.IsNullOrWhiteSpace(session.AppPlatform) || session.AppPlatform.IsContains("Desktop") ? "false" : "true";
			var osInfo = (session.AppAgent ?? "").GetOSInfo();

			var systemIdentity = string.Empty;
			var specialRequest = string.Empty;
			var legacyRequest = string.Empty;

			var query = context.Request.QueryString.ToDictionary(queryString =>
			{
				if (queryString.TryGetValue("x-params", out var xparams))
				{
					queryString.Remove("x-params");
					try
					{
						(xparams.Url64Decode().ToJSON() as JObject).ForEach(kvp => queryString[kvp.Key] = (kvp.Value as JValue).Value?.ToString());
					}
					catch { }
				}

				var pathSegments = context.GetRequestPathSegments().Where(segment => !segment.IsEquals("desktop.aspx") && !segment.IsEquals("default.aspx") && !segment.IsEquals("index.aspx") && !segment.IsEquals("index.php")).ToArray();
				var firstPathSegment = pathSegments.Length > 0 ? pathSegments[0].ToLower() : "";
				requestSegments = pathSegments.Skip(0).ToList();

				// special parameters (like indicators (robots.txt/ads.txt/favicon.ico) or system/organization identity)
				if (!string.IsNullOrWhiteSpace(firstPathSegment))
				{
					// system/oranization identity
					if (firstPathSegment.StartsWith("~"))
					{
						requestSegments = pathSegments.Skip(1).ToList();
						systemIdentity = firstPathSegment.Right(firstPathSegment.Length - 1).Replace(StringComparison.OrdinalIgnoreCase, ".html", "").Replace(StringComparison.OrdinalIgnoreCase, ".aspx", "").Replace(StringComparison.OrdinalIgnoreCase, ".php", "").GetANSIUri(true, false);
						queryString["x-system"] = systemIdentity;
					}

					// special requests (_initializer, _validator, _login, _logout, _feed, _cms, _admin) or special resources (_assets, _css, _fonts, _images, _js)
					else if (firstPathSegment.StartsWith("_"))
					{
						// special requests
						if (Handler.Initializers.Contains(firstPathSegment))
							specialRequest = "initializer";

						else if (Handler.Validators.Contains(firstPathSegment))
							specialRequest = "validator";

						else if (Handler.LogIns.Contains(firstPathSegment))
							specialRequest = "login";

						else if (Handler.LogOuts.Contains(firstPathSegment))
							specialRequest = "logout";

						else if (Handler.Feeds.Contains(firstPathSegment.Right(firstPathSegment.Length - 1)))
							specialRequest = "feed";

						else if (Handler.CmsPortals.Contains(firstPathSegment))
						{
							specialRequest = "cms";
							queryString["x-resource"] = "cms";
							queryString["x-cms-path"] = requestSegments.Skip(1).Join("/");
							queryString["x-cms-mode"] = firstPathSegment.IsStartsWith("_image")
								? "Tracking"
								: firstPathSegment.IsStartsWith("_confirm")
									? "Confirm"
									: firstPathSegment.IsStartsWith("_unsubscribe")
										? "Unsubscribe"
										: firstPathSegment.IsStartsWith("_visit")
											? "Visit"
											: "Redirect";
						}

						// special resources
						else
						{
							systemIdentity = "~resources";
							queryString["x-resource"] = firstPathSegment.Right(firstPathSegment.Length - 1).GetANSIUri(true, true);
							queryString["x-path"] = pathSegments.Skip(1).Join("/");
						}

						// no info
						requestSegments = new();
					}
				}

				// normalize info of requests
				if (requestSegments.Count > 0 && specialRequest == "")
				{
					var firstRequestSegment = requestSegments.First().ToLower();

					// special requests
					if (Handler.Initializers.Contains(firstRequestSegment))
					{
						specialRequest = "initializer";
						requestSegments = new();
					}

					else if (Handler.Validators.Contains(firstRequestSegment))
					{
						specialRequest = "validator";
						requestSegments = new();
					}

					else if (Handler.LogIns.Contains(firstRequestSegment))
					{
						specialRequest = "login";
						requestSegments = new();
					}

					else if (Handler.LogOuts.Contains(firstRequestSegment))
					{
						specialRequest = "logout";
						requestSegments = new();
					}

					else if (Handler.Feeds.Contains(firstRequestSegment))
					{
						specialRequest = "feed";
						requestSegments = new();
					}

					else if (Handler.CmsPortals.Contains(firstRequestSegment))
					{
						specialRequest = "cms";
						queryString["x-resource"] = "cms";
						queryString["x-cms-path"] = requestSegments.Skip(1).Join("/");
						queryString["x-cms-mode"] = firstPathSegment.IsStartsWith("_image")
							? "Tracking"
							: firstPathSegment.IsStartsWith("_confirm")
								? "Confirm"
								: firstPathSegment.IsStartsWith("_unsubscribe")
									? "Unsubscribe"
									: firstPathSegment.IsStartsWith("_visit")
										? "Visit"
										: "Redirect";
						requestSegments = new();
					}

					// indicators
					else if (firstPathSegment.IsEndsWith(".txt") || firstPathSegment.IsEndsWith(".xml") || firstPathSegment.IsEndsWith(".json") || firstPathSegment.IsEquals("favicon.ico"))
					{
						systemIdentity = "~indicators";
						queryString["x-indicator"] = firstPathSegment;
						requestSegments = new();
					}

					// request of legacy systems
					else if (firstRequestSegment.IsEndsWith(".ashx"))
						legacyRequest = requestSegments.Join("/");

					else if (firstRequestSegment.IsEndsWith(".aspx"))
						legacyRequest = firstRequestSegment.IsStartsWith("Download") || firstRequestSegment.IsStartsWith("File") || firstRequestSegment.IsStartsWith("Image") || firstRequestSegment.IsStartsWith("Thumbnail") ? requestSegments.Join("/") : null;

					// parameters of desktop and contents
					if (string.IsNullOrWhiteSpace(specialRequest) && string.IsNullOrWhiteSpace(legacyRequest))
					{
						var value = firstRequestSegment.Replace(StringComparison.OrdinalIgnoreCase, ".html", "").Replace(StringComparison.OrdinalIgnoreCase, ".aspx", "").Replace(StringComparison.OrdinalIgnoreCase, ".php", "");
						value = value.Equals("") || value.StartsWith("-") || value.IsEquals("default") || value.IsEquals("index") || value.IsNumeric() ? "default" : value.GetANSIUri();
						queryString["x-desktop"] = (value.Equals("default") ? "-" : "") + value;

						value = requestSegments.Count > 1 && !string.IsNullOrWhiteSpace(requestSegments[1]) ? requestSegments[1].Replace(StringComparison.OrdinalIgnoreCase, ".html", "").Replace(StringComparison.OrdinalIgnoreCase, ".aspx", "").Replace(StringComparison.OrdinalIgnoreCase, ".php", "") : null;
						queryString["x-parent"] = string.IsNullOrWhiteSpace(value) ? null : value.GetANSIUri();

						if (requestSegments.Count > 2 && !string.IsNullOrWhiteSpace(requestSegments[2]))
						{
							value = requestSegments[2].Replace(StringComparison.OrdinalIgnoreCase, ".html", "").Replace(StringComparison.OrdinalIgnoreCase, ".aspx", "").Replace(StringComparison.OrdinalIgnoreCase, ".php", "");
							if (value.IsNumeric())
								queryString["x-page"] = value;
							else
								queryString["x-content"] = value.GetANSIUri();

							if (requestSegments.Count > 3 && !string.IsNullOrWhiteSpace(requestSegments[3]))
							{
								value = requestSegments[3].Replace(StringComparison.OrdinalIgnoreCase, ".html", "").Replace(StringComparison.OrdinalIgnoreCase, ".aspx", "").Replace(StringComparison.OrdinalIgnoreCase, ".php", "");
								if (value.IsNumeric())
									queryString["x-page"] = value;
							}
						}
					}
				}
				else if (!systemIdentity.IsEquals("~indicators") && !systemIdentity.IsEquals("~resources"))
					queryString["x-desktop"] = "-default";

				// legacy parameters
				Handler.LegacyParameters.ForEach(key => queryString.Remove(key));
			});

			// validate request
			var isHarmfulRequest = false;
			Exception harmfulException = null;
			if ((!requestMethod.IsEquals("GET") && !specialRequest.IsEquals("login")) || requestSegments.Count > 5 || requestURI.AbsolutePath.IsEndsWith(".php"))
			{
				isHarmfulRequest = true;
				if (requestSegments.Count > 5 || requestURI.AbsolutePath.IsEndsWith(".php"))
					harmfulException = new InvalidRequestException("Bad request");
			}
			else
			{
				var firstSegment = requestSegments.FirstOrDefault()?.ToLower();
				if (!string.IsNullOrWhiteSpace(firstSegment) && Handler.PreventingPaths.Count > 0)
					foreach (var path in Handler.PreventingPaths)
					{
						isHarmfulRequest = path.IsStartsWith("s:")
							? firstSegment.IsStartsWith(path.Replace("s:", "", StringComparison.OrdinalIgnoreCase))
							: path.IsStartsWith("e:")
								? firstSegment.IsEndsWith(path.Replace("e:", "", StringComparison.OrdinalIgnoreCase))
								: path.IsStartsWith("c:")
									? firstSegment.IsContains(path.Replace("c:", "", StringComparison.OrdinalIgnoreCase))
									: firstSegment.IsEquals(path);
						if (isHarmfulRequest)
						{
							harmfulException = new InformationNotFoundException();
							break;
						}
					}
			}
			if (isHarmfulRequest)
			{
				context.ShowError(context.MonitorHarmfulRequest(context.GetRemoteIPAddress().ToString(), Global.NodeID, Global.ServiceName, harmfulException));
				return;
			}

			// prepare headers
			var headers = context.Request.Headers.ToDictionary(header =>
			{
				Handler.ExcludedHeaders.ForEach(name => header.Remove(name));
				header.Keys.Where(name => name.IsStartsWith("cf-") || name.IsStartsWith("sec-")).ToList().ForEach(name => header.Remove(name));
				header["x-host"] = context.GetParameter("Host") ?? requestURI.Host;
				header["x-url"] = "https".IsEquals(context.GetHeaderParameter("x-forwarded-proto") ?? context.GetHeaderParameter("x-original-proto")) && !"https".IsEquals(requestURI.Scheme)
					? requestURI.AbsoluteUri.Replace(StringComparison.OrdinalIgnoreCase, $"{requestURI.Scheme}://", "https://")
					: requestURI.AbsoluteUri;
				header["x-use-short-urls"] = Handler.UseShortURLs.ToString().ToLower();
				header["x-environment-is-mobile"] = isMobile;
				header["x-environment-os-info"] = osInfo;
				header["x-requester"] = context.TryGetParameter("x-requester", out var requester) ? requester : "vieapps-ngx-portals";
			});

			if (specialRequest.IsEquals("feed"))
			{
				if (requestURI.AbsolutePath.IsEndsWith(".json"))
					headers["x-feed-json"] = "true";
				var categoryAlias = context.GetRequestPathSegments().Last();
				categoryAlias = categoryAlias.Replace(StringComparison.OrdinalIgnoreCase, ".xml", "").Replace(StringComparison.OrdinalIgnoreCase, ".json", "").Replace(StringComparison.OrdinalIgnoreCase, ".html", "").Replace(StringComparison.OrdinalIgnoreCase, ".aspx", "").Replace(StringComparison.OrdinalIgnoreCase, ".php", "");
				categoryAlias = categoryAlias.Replace(StringComparison.OrdinalIgnoreCase, "feed", "").Replace(StringComparison.OrdinalIgnoreCase, "atom", "").Replace(StringComparison.OrdinalIgnoreCase, "rss", "");
				if (!string.IsNullOrWhiteSpace(categoryAlias))
					headers["x-feed-category"] = categoryAlias;
			}

			// prepare extra info
			var extra = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			if (query.Remove("x-request-extra", out var extraInfo) && !string.IsNullOrWhiteSpace(extraInfo))
				try
				{
					extra = extraInfo.Url64Decode().ToExpandoObject().ToDictionary(kvp => kvp.Key, kvp => kvp.Value?.ToString(), StringComparer.OrdinalIgnoreCase);
				}
				catch { }

			// process the request
			JObject systemIdentityJson = null;
			using var cts = CancellationTokenSource.CreateLinkedTokenSource(Global.CancellationToken, context.RequestAborted);
			var requestInfo = new RequestInfo(session, "Portals", "Identify.System", "GET", query, headers, null, extra, correlationID);

			var alwaysUseHTTPs = false;
			var alwaysReturnHTTPs = false;
			var redirectToNoneWWW = false;
			stepwatch.Restart();

			if (string.IsNullOrWhiteSpace(specialRequest))
				try
				{
					// call the Portals service to identify the system
					if (!"~resources".IsEquals(systemIdentity))
					{
						systemIdentityJson = await context.IdentifySystemAsync(requestInfo, cts.Token).ConfigureAwait(false);
						requestInfo.Query["x-system"] = systemIdentityJson.Get<string>("Alias");
						alwaysUseHTTPs = systemIdentityJson.Get("AlwaysUseHTTPs", false);
						alwaysReturnHTTPs = systemIdentityJson.Get("AlwaysReturnHTTPs", false);
						redirectToNoneWWW = systemIdentityJson.Get("RedirectToNoneWWW", false);
						context.UpdateServerTiming("ngxIdentify", stepwatch.ElapsedMilliseconds);
					}

					// request of legacy system (files and medias)
					if (!string.IsNullOrWhiteSpace(legacyRequest))
					{
						requestSegments = legacyRequest.ToArray("/").ToList();
						if (!requestSegments.Any())
							requestSegments.Add("");

						var legacyHandler = requestSegments.FirstOrDefault();
						if (legacyHandler.IsEquals("Download.ashx") || legacyHandler.IsEquals("Download.aspx"))
							requestSegments[0] = "downloads";
						else
						{
							requestSegments[0] = legacyHandler.IsEquals("File.ashx") || legacyHandler.IsEquals("File.aspx") || legacyHandler.IsEquals("Image.ashx") || legacyHandler.IsEquals("Image.aspx")
								? "files"
								: legacyHandler.Replace(StringComparison.OrdinalIgnoreCase, ".ashx", "").Replace(StringComparison.OrdinalIgnoreCase, ".aspx", "").ToLower() + "s";
							if (requestSegments.Count > 0 && !requestSegments[1].IsValidUUID())
								requestSegments.Insert(1, systemIdentityJson?.Get<string>("ID"));
						}

						if (legacyHandler.IsEquals("files") && requestSegments.Count > 3 && requestSegments[3].Contains('-') && requestSegments[3].Length > 32)
						{
							var id = requestSegments[3].Left(32);
							var filename = requestSegments[3].Right(requestSegments[3].Length - 33);
							requestSegments[3] = id;
							requestSegments.Insert(4, filename);
							requestSegments = requestSegments.Take(5).ToList();
						}

						else if (legacyHandler.IsStartsWith("thumbnail") && requestSegments.Count > 5 && requestSegments[5].Contains('-') && requestSegments[5].Length > 32)
						{
							var id = requestSegments[5].Left(32);
							var filename = requestSegments[5].Right(requestSegments[5].Length - 33);
							requestSegments[5] = id;
							requestSegments.Insert(6, filename);
							requestSegments = requestSegments.Take(7).ToList();
						}

						if (!string.IsNullOrWhiteSpace(legacyHandler))
						{
							var filesHttpURI = this.RemoveURITrail(systemIdentityJson?.Get<string>("FilesHttpURI") ?? Handler.FilesHttpURI);
							context.SetResponseHeaders((int)HttpStatusCode.MovedPermanently, new Dictionary<string, string>
							{
								["Location"] = $"{filesHttpURI}/{requestSegments.Join("/")}",
								["X-Node"] = Global.NodeID,
								["X-Correlation-ID"] = correlationID,
								["X-Redirector"] = "VIEApps NGX HTTP CMS Portals"
							});
							return;
						}
					}
					else
					{
						var redirectURL = systemIdentityJson?.Get<string>("RedirectTo");
						if (!string.IsNullOrWhiteSpace(redirectURL))
						{
							context.SetResponseHeaders((int)HttpStatusCode.Redirect, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
							{
								["Location"] = redirectURL,
								["X-Node"] = Global.NodeID,
								["X-Correlation-ID"] = correlationID,
								["X-Redirector"] = "VIEApps NGX HTTP CMS Portals"
							});
							if (isDebugLogEnabled || Global.IsVisitLogEnabled)
								await context.WriteLogsAsync("Http.Process.Requests", $"Redirect for matching with the settings [{requestURI} => {redirectURL}]").ConfigureAwait(false);
							return;
						}
					}

					// requester is NGX-Refresher
					var isRefresher = Handler.RefresherURL.IsEquals(context.GetReferUrl());

					// session state
					if (!isRefresher && !"~resources".IsEquals(systemIdentity) && !"~indicators".IsEquals(systemIdentity))
						requestInfo.SendSessionState(systemIdentityJson, Global.ServiceName + ".HTTP", $"{requestMethod} {requestURI.AbsoluteUri}", Handler.TrackPortalStatistics);

					// examinations
					var examinations = isRefresher || "~resources".IsEquals(systemIdentity) || "~indicators".IsEquals(systemIdentity) || (context.TryGetParameter("x-requester", out var requester) && requester.IsStartsWith("vieapps-ngx")) ? null : systemIdentityJson?.Get<JArray>("CacheExaminations")?.Select(exam => exam as JObject).Where(exam => exam != null).Select(exam => exam?.Copy<Settings.ExamineURLs>()).Where(exam => exam != null).ToList();
					if (examinations != null && examinations.Count > 0)
					{
						var path = requestURI.AbsolutePath.ToLower();
						var pathWithoutExtention = path.Replace("/default.aspx", "").Replace(".aspx", "").Replace(".php", "").Replace(".html", "");
						var examination = examinations.FirstOrDefault(exam => exam.Start <= DateTime.Now && exam.End >= DateTime.Now && ((exam.URLs.Any(url => url.IsStartsWith("s:/") ? path.IsStartsWith(url.Right(url.Length - 2)) : url.IsStartsWith("c:/") ? path.IsContains(url.Right(url.Length - 2)) : path.IsEndsWith(url)) || exam.URLs.Any(url => url.IsStartsWith("s:/") ? pathWithoutExtention.IsStartsWith(url.Right(url.Length - 2)) : url.IsStartsWith("c:/") ? pathWithoutExtention.IsContains(url.Right(url.Length - 2)) : pathWithoutExtention.IsEndsWith(url)) || exam.URLs.Any(url => url == "*"))));
						if (examination != null)
						{
							var seconds = UtilityService.GetRandomNumber(examination.WaitSecondsMin, examination.WaitSecondsMax);
							await context.WriteLogsAsync("Http.Process.Examinations", $"Do the examination in {seconds} seconds => {requestURI}{("Continue".IsEquals(examination.ResponseMode) ? "" : $" [{examination.ResponseCode}: {examination.ResponseType} - {examination.ResponseMessage}]")}").ConfigureAwait(false);
							await Task.Delay(seconds * 1000, cts.Token).ConfigureAwait(false);
							if (!"Continue".IsEquals(examination.ResponseMode))
							{
								context.ShowError(examination.ResponseCode, examination.ResponseMessage, examination.ResponseType, correlationID);
								return;
							}
						}
					}

					// process with cache
					if (Handler.AllowCache && !isForceCacheRequested && !context.IsAuthenticated())
					{
						var cacheKey = "";
						var eTag = "";
						var contentType = "text/html";
						var expires = DateTime.Now.AddMinutes(Handler.CacheMaxAge);
						var baseURL = "";
						var rootURL = "/";
						var filesHttpURI = this.RemoveURITrail(systemIdentityJson?.Get<string>("FilesHttpURI") ?? Handler.FilesHttpURI);
						var portalsHttpURI = this.RemoveURITrail(systemIdentityJson?.Get<string>("PortalsHttpURI") ?? Handler.PortalsHttpURI);

						if ("~resources".IsEquals(systemIdentity))
						{
							expires = DateTime.Now.AddDays(366);
							string identity = null;
							var isThemeResource = false;
							var path = requestInfo.GetParameter("x-path");
							var type = requestInfo.GetParameter("x-resource");

							if (type.IsStartsWith("theme"))
							{
								isThemeResource = true;
								var paths = path.ToList("/", true, true);
								type = paths != null && paths.Count > 1
									? paths[1].IsStartsWith("css")
										? "css"
										: paths[1].IsStartsWith("js") || paths[1].IsStartsWith("javascript") || paths[1].IsStartsWith("script")
											? "js"
											: paths[1].IsStartsWith("img") || paths[1].IsStartsWith("image") ? "images" : paths[1].IsStartsWith("font") ? "fonts" : ""
									: "";
								if (!type.IsEquals("images") && !type.IsEquals("fonts"))
									identity = paths.Count > 0 ? paths[0] : null;
							}

							else if (type.IsStartsWith("css") || type.IsStartsWith("js") || type.IsStartsWith("javascript") || type.IsStartsWith("script"))
							{
								type = type.IsStartsWith("css") ? "css" : "js";
								identity = path.Replace(StringComparison.OrdinalIgnoreCase, $".{type}", "").ToLower().Trim();
							}

							else if (type.IsEquals("assets"))
								type = type.IsStartsWith("img") || type.IsStartsWith("image")
									? "images"
									: type.IsStartsWith("font")
										? "fonts"
										: path.ToList(".").Last();

							if (type.IsEquals("css"))
								contentType = "text/css";
							else if (type.IsEquals("xml"))
								contentType = "text/xml";
							else if (type.IsEquals("js"))
								contentType = "application/javascript";
							else if (type.IsEquals("json"))
								contentType = "application/json";
							else if (type.IsEquals("fonts"))
								contentType = $"font/{path.ToList(".").Last()}";
							else if (type.IsEquals("images"))
							{
								contentType = path.ToList(".").Last();
								contentType = $"image/{(contentType.IsEquals("svg") ? "svg+xml" : contentType.IsEquals("jpg") ? "jpeg" : contentType)}";
							}

							cacheKey = (type.IsEquals("css") || type.IsEquals("js")) && (isThemeResource || (identity != null && identity.Length == 34 && identity.Right(32).IsValidUUID()))
								? $"{type}#{identity}"
								: requestURI.AbsolutePath.ToLower().GenerateUUID();
							eTag = $"vieapps#{cacheKey.GenerateUUID()}";
						}

						else if (!"~indicators".IsEquals(systemIdentity))
						{
							systemIdentityJson ??= await context.IdentifySystemAsync(requestInfo, cts.Token).ConfigureAwait(false);

							var siteURI = $"//{systemIdentityJson.Get<string>("SiteHost")}";
							var organizationAlias = systemIdentityJson.Get<string>("Alias");
							var homeDesktopAlias = systemIdentityJson.Get<string>("HomeDesktopAlias");
							var homeDesktopAliases = systemIdentityJson.Get<string>("HomeDesktopAliases");

							var desktopAlias = query["x-desktop"].ToLower();
							var path = homeDesktopAlias.IsEquals(desktopAlias) || homeDesktopAliases.IsContains(desktopAlias) || "-default".IsEquals(desktopAlias) ? "-default" : null;
							if (path == null)
							{
								path = requestURI.AbsolutePath.ToLower();
								while (path.EndsWith("/") || path.EndsWith("."))
									path = path.Left(path.Length - 1).Trim();
								if (path.IsStartsWith($"/~{organizationAlias}"))
								{
									path = path.Right(path.Length - organizationAlias.Length - 2);
									baseURL = $"{(portalsHttpURI.IsEndsWith(siteURI) ? portalsHttpURI : Handler.PortalsHttpURI)}/~{organizationAlias}/";
									rootURL = "";
								}
								path = path.IsEndsWith("/default.aspx") ? path.Left(path.Length - 13) : path;
								path = path.IsEndsWith(".html") || path.IsEndsWith(".aspx") ? path.Left(path.Length - 5) : path.IsEndsWith(".php") ? path.Left(path.Length - 4) : path;
								path = path.Equals("") || path.Equals("/") || path.Equals("/index") || path.Equals("/default") ? "-default" : path;
							}
							else if (portalsHttpURI.IsEndsWith(siteURI) || Handler.PortalsHttpURI.IsEndsWith(siteURI))
							{
								baseURL = $"{(portalsHttpURI.IsEndsWith(siteURI) ? portalsHttpURI : Handler.PortalsHttpURI)}/~{organizationAlias}/";
								rootURL = "";
							}

							cacheKey = systemIdentityJson.Get<string>("CacheKeyPrefix") + ":" + path.GenerateUUID();
							eTag = $"vieapps#{cacheKey.GenerateUUID()}";

							alwaysUseHTTPs = systemIdentityJson.Get("AlwaysUseHTTPs", false);
							alwaysReturnHTTPs = systemIdentityJson.Get("AlwaysReturnHTTPs", false);
							redirectToNoneWWW = systemIdentityJson.Get("RedirectToNoneWWW", false);
						}

						stepwatch.Restart();
						if (!string.IsNullOrWhiteSpace(cacheKey))
						{
							var isHtml = contentType.IsStartsWith("text/html");
							var isBase64 = contentType.IsStartsWith("font/") || contentType.IsStartsWith("image/");

							// redirect (HTTPS or None-WWW)
							if (isHtml && ((alwaysUseHTTPs && !requestURI.Scheme.IsEquals("https")) || (redirectToNoneWWW && requestURI.Host.IsStartsWith("www."))))
							{
								var redirectURL = $"{(alwaysUseHTTPs || alwaysReturnHTTPs ? "https" : requestURI.Scheme)}://{(redirectToNoneWWW && requestURI.Host.IsStartsWith("www.") ? requestURI.Host.Replace("www.", "") : requestURI.Host)}{requestURI.PathAndQuery}{requestURI.Fragment}";
								context.SetResponseHeaders((int)HttpStatusCode.Redirect, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
								{
									["Location"] = redirectURL,
									["X-Node"] = Global.NodeID,
									["X-Correlation-ID"] = correlationID,
									["X-Redirector"] = "VIEApps NGX HTTP CMS Portals"
								});
								if (isDebugLogEnabled || Global.IsVisitLogEnabled)
									await context.WriteLogsAsync("Http.Process.Requests", $"Redirect for matching with the settings [{requestURI} => {redirectURL}]").ConfigureAwait(false);
								return;
							}

							// process cache							
							if (isDebugLogEnabled || Global.IsVisitLogEnabled)
								await context.WriteLogsAsync("Http.Process.Requests", $"Attempt to process the CMS Portals service cache => {requestURI} ({cacheKey})").ConfigureAwait(false);

							headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
							{
								["Content-Type"] = contentType + (isBase64 ? "" : "; charset=utf-8"),
								["ETag"] = eTag,
								["Cache-Control"] = isHtml ? context.GetHttpCacheControl(Handler.CacheMaxAge * 60) : context.GetHttpCacheControl(),
								["X-Cache"] = "HTTP-200",
								["X-Node"] = Global.NodeID,
								["X-Correlation-ID"] = correlationID
							};

							var allowOrigin = "*";
							if (!isHtml && !isBase64 && Handler.CrossOrigin.IsEquals("use-credentials"))
							{
								headers["Referrer-Policy"] = "no-referrer-when-downgrade";
								headers["Access-Control-Allow-Credentials"] = "true";
								var origin = requestInfo.GetHeaderParameter("Origin") ?? requestInfo.GetHeaderParameter("Referer");
								if (!string.IsNullOrWhiteSpace(origin))
								{
									var originURI = new Uri(origin);
									allowOrigin = $"{originURI.Scheme}://{originURI.Host}";
								}
							}
							headers["Access-Control-Allow-Origin"] = allowOrigin;

							// last modified
							var modifiedSince = context.GetHeaderParameter("If-Modified-Since") ?? context.GetHeaderParameter("If-Unmodified-Since");
							var lastModified = modifiedSince != null ? await Handler.Cache.GetAsync<string>($"{cacheKey}:time", cts.Token).ConfigureAwait(false) : null;
							var noneMatch = lastModified != null ? context.GetHeaderParameter("If-None-Match") : null;
							if (lastModified != null && eTag.IsEquals(noneMatch) && modifiedSince.FromHttpDateTime() >= lastModified.FromHttpDateTime())
							{
								headers["Last-Modified"] = lastModified;
								headers["X-Cache"] = "HTTP-304";
								context.UpdateServerTiming("ngxCache", stepwatch.ElapsedMilliseconds);
								context.SetResponseHeaders((int)HttpStatusCode.NotModified, headers);

								if (examinations == null || !examinations.Any(exam => exam.Start >= DateTime.Now && exam.End <= DateTime.Now))
									context.SetL1Cache(alwaysUseHTTPs, alwaysReturnHTTPs, portalsHttpURI, filesHttpURI, headers, cacheKey);

								if (isDebugLogEnabled || Global.IsVisitLogEnabled)
									await context.WriteLogsAsync("Http.Process.Requests", $"Process the CMS Portals service cache was done => NOT MODIFIED ({eTag}/{lastModified}) - Execution times: {stepwatch.GetElapsedTimes()} of {stopwatch.GetElapsedTimes()}").ConfigureAwait(false);
								return;
							}

							// cached data
							stepwatch.Restart();
							var cached = await Handler.Cache.GetAsync<string>(cacheKey, cts.Token).ConfigureAwait(false);

							if (!string.IsNullOrWhiteSpace(cached))
							{
								var maxAge = Handler.CacheMaxAge * 60;
								var expiresAt = !isBase64 && isHtml ? await Handler.Cache.GetAsync<string>($"{cacheKey}:expiration", cts.Token).ConfigureAwait(false) : null;
								if (expiresAt != null && DateTime.TryParse(expiresAt, out var expiresAtTime))
								{
									expires = expiresAtTime;
									maxAge = (expires - DateTime.UtcNow).TotalSeconds.As<int>();
								}
								lastModified ??= await Handler.Cache.GetAsync<string>($"{cacheKey}:time", cts.Token).ConfigureAwait(false) ?? DateTime.Now.ToHttpString();

								if (context.ContainsKey("x-sliding-cache"))
								{
									var items = new Dictionary<string, string>
									{
										[cacheKey] = cached,
										[$"{cacheKey}:time"] = lastModified
									};
									if (expiresAt != null && DateTime.TryParse(expiresAt, out expiresAtTime))
									{
										items[$"{cacheKey}:expiration"] = expiresAtTime.AddMinutes(Handler.CacheMaxAge).ToIsoString(true);
										Handler.Cache.SetAsync(items, null, expiresAtTime.AddMinutes(Handler.CacheMaxAge), Global.CancellationToken).Execute();
									}
									else
										Handler.Cache.SetAsync(items, null, 0, Global.CancellationToken).Execute();
								}

								headers["Last-Modified"] = lastModified;
								headers["Expires"] = expires.ToHttpString();
								headers["Cache-Control"] = isHtml ? context.GetHttpCacheControl(maxAge) : context.GetHttpCacheControl();
								
								var isCacheLogEnabled = !isBase64 && (isDebugLogEnabled || context.ContainsKey("x-cache-logs"));
								if (isCacheLogEnabled)
									await context.WriteLogsAsync("Http.Process.Requests", $"CMS Portals service cache was found ({cacheKey})\r\n\r\nRaw cache:\r\n{cached}").ConfigureAwait(false);

								cached = isBase64 ? cached : cached.Replace("~#/", $"{portalsHttpURI}/").Replace("~~~/", $"{portalsHttpURI}/").Replace("~~/", $"{filesHttpURI}/").Replace("~/", rootURL);
								cached = !isBase64 && isHtml ? context.NormalizeHtml(cached, alwaysUseHTTPs, alwaysReturnHTTPs, baseURL) : cached;
								var body = isBase64 ? cached.Base64ToBytes() : cached.ToBytes();

								context.UpdateServerTiming("ngxCache", stepwatch.ElapsedMilliseconds);
								context.SetResponseHeaders((int)HttpStatusCode.OK, headers);
								await context.WriteAsync(body, cts.Token).ConfigureAwait(false);

								if (examinations == null || !examinations.Any(exam => exam.Start >= DateTime.Now && exam.End <= DateTime.Now))
									context.SetL1Cache(alwaysUseHTTPs, alwaysReturnHTTPs, portalsHttpURI, filesHttpURI, headers, cacheKey);

								stepwatch.Stop();
								if (isDebugLogEnabled || Global.IsVisitLogEnabled)
									await context.WriteLogsAsync("Http.Process.Requests", $"Process the CMS Portals service cache was done => FOUND ({cacheKey}) - Execution times: {stepwatch.GetElapsedTimes()} of {stopwatch.GetElapsedTimes()}{(isCacheLogEnabled ? $"\r\n\r\nNormalized cache:\r\n{cached}" : "")}").ConfigureAwait(false);
								return;
							}
						}
					}

					// call CMS Portals service to process the request
					stepwatch.Restart();
					try
					{
						requestInfo = new RequestInfo(requestInfo) { ObjectName = "Process.Http.Request" };
						if (isDebugLogEnabled)
							await context.WriteLogsAsync("Http.Process.Requests", $"Call the service to process the request\r\n- App: {session.AppName} [{session.AppPlatform} @ {session.AppAgent}]\r\n- Request: {requestInfo.ToString(Formatting.Indented)}").ConfigureAwait(false);

						var response = (await context.CallServiceAsync(requestInfo, cts.Token, Global.Logger, "Http.Process.Requests").ConfigureAwait(false)).ToExpandoObject();

						var statusCode = response.Get("StatusCode", (int)HttpStatusCode.OK);

						var responseBody = response.Get<string>("Body");
						var body = responseBody != null ? responseBody.Base64ToBytes().Decompress(response.Get("BodyEncoding", "zstd")) : null;

						headers = response.Get("Headers", new Dictionary<string, string>());
						if (headers.TryGetValue("X-Node", out var nodeID))
							headers["X-Service-Node"] = nodeID;

						var isHtml = headers.TryGetValue("Content-Type", out var contentType) && contentType.IsStartsWith("text/html");
						if (!headers.TryGetValue("Cache-Control", out var cacheControl))
							cacheControl = isHtml ? context.GetHttpCacheControl(Handler.CacheMaxAge * 60) : context.GetHttpCacheControl();
						if (isForceCacheRequested || isRefresher || context.IsAuthenticated())
							cacheControl = context.GetHttpCacheControl(true);
						
						headers = new Dictionary<string, string>(headers, StringComparer.OrdinalIgnoreCase)
						{
							["Cache-Control"] = cacheControl,
							["X-Correlation-ID"] = correlationID,
							["X-Node"]  = Global.NodeID
						};

						if (headers.TryGetValue("Server-Timing", out var serverTiming))
							context.UpdateServerTiming(serverTiming, () => headers.Remove("Server-Timing"));
						context.UpdateServerTiming("ngxServe", stepwatch.ElapsedMilliseconds);

						context.SetResponseHeaders(statusCode, headers);
						if (body != null)
							await context.WriteAsync(body, cts.Token).ConfigureAwait(false);

						if (Handler.Cache.UseL1Cache && !context.IsAuthenticated() && (examinations == null || !examinations.Any(exam => exam.Start >= DateTime.Now && exam.End <= DateTime.Now)))
						{
							var filesHttpURI = this.RemoveURITrail(systemIdentityJson.Get<string>("FilesHttpURI") ?? Handler.FilesHttpURI);
							var portalsHttpURI = this.RemoveURITrail(systemIdentityJson.Get<string>("PortalsHttpURI") ?? Handler.PortalsHttpURI);
							var siteURI = $"//{systemIdentityJson.Get<string>("SiteHost")}";
							var organizationAlias = systemIdentityJson.Get<string>("Alias");
							var homeDesktopAlias = systemIdentityJson.Get<string>("HomeDesktopAlias");
							var homeDesktopAliases = systemIdentityJson.Get<string>("HomeDesktopAliases");
							var desktopAlias = query.TryGetValue("x-desktop", out var xdesktopAlias) ? xdesktopAlias.ToLower() : null;
							var path = homeDesktopAlias.IsEquals(desktopAlias) || homeDesktopAliases.IsContains(desktopAlias) || "-default".IsEquals(desktopAlias) ? "-default" : null;
							if (path == null)
							{
								path = requestURI.AbsolutePath.ToLower();
								while (path.EndsWith("/") || path.EndsWith("."))
									path = path.Left(path.Length - 1).Trim();
								if (path.IsStartsWith($"/~{organizationAlias}"))
									path = path.Right(path.Length - organizationAlias.Length - 2);
								path = path.IsEndsWith("/default.aspx") ? path.Left(path.Length - 13) : path;
								path = path.IsEndsWith(".html") || path.IsEndsWith(".aspx") ? path.Left(path.Length - 5) : path.IsEndsWith(".php") ? path.Left(path.Length - 4) : path;
								path = path.Equals("") || path.Equals("/") || path.Equals("/index") || path.Equals("/default") ? "-default" : path;
							}
							var cacheKey = systemIdentityJson.Get<string>("CacheKeyPrefix") + ":" + path.GenerateUUID();
							context.SetL1Cache(alwaysUseHTTPs, alwaysReturnHTTPs, portalsHttpURI, filesHttpURI, headers, cacheKey);
						}

						stepwatch.Stop();
						if (isDebugLogEnabled)
							await context.WriteLogsAsync("Http.Process.Requests", $"Call the service to process the request was successed - Execution times: {stepwatch.GetElapsedTimes()}\r\n- App: {session.AppName} [{session.AppPlatform} @ {session.AppAgent}]\r\n- Response: {response.ToJson()}").ConfigureAwait(false);
					}
					catch (Exception)
					{
						if ("~indicators".IsEquals(systemIdentity) && query.TryGetValue("x-indicator", out var indicator) && "favicon.ico".IsEquals(indicator))
							await context.ProcessFavouritesIconFileRequestAsync().ConfigureAwait(false);
						else
							throw;
					}
				}
				catch (TaskCanceledException) { }
				catch (OperationCanceledException) { }
				catch (Exception ex)
				{
					var statusCode = (int)HttpStatusCode.InternalServerError;
					if (ex is WampException wampException)
					{
						var wampDetails = wampException.GetDetails(requestInfo);
						statusCode = wampDetails.Type == "SiteNotRecognizedException"
							? (int)HttpStatusCode.NotFound
							: wampDetails.Type == "SiteFrozenException" ? 530 : wampDetails.Code;
						context.ShowError(statusCode, wampDetails.Message, wampDetails.Type, correlationID, wampDetails.Stack + "\r\n\t" + ex.StackTrace, isDebugLogEnabled);
					}
					else
					{
						var type = ex.GetTypeName(true);
						statusCode = type == "SiteNotRecognizedException"
							? (int)HttpStatusCode.NotFound
							: type == "SiteFrozenException" ? 530 : ex.GetHttpStatusCode();
						context.ShowError(statusCode, ex.Message, type, correlationID, ex, isDebugLogEnabled);
					}
					await context.WriteLogsAsync("Http.Process.Requests", $"Error occurred ({statusCode}) => {context.Request.Method} {requestURI}", ex, Global.ServiceName, LogLevel.Error).ConfigureAwait(false);
				}
				finally
				{
					context.StoreSession(session);
				}
			else
			{
				var isUserInteract = context.Request.Path.Value.IsEndsWith(".aspx") || context.Request.Path.Value.IsEndsWith(".html") || context.Request.Path.Value.IsEndsWith(".php");
				try
				{
					switch (specialRequest)
					{
						case "initializer":
							if (context.Request.Path.Value.IsEndsWith(".aspx") || context.Request.Path.Value.IsEndsWith(".html") || context.Request.Path.Value.IsEndsWith(".php"))
								systemIdentityJson ??= await context.IdentifySystemAsync(requestInfo, cts.Token).ConfigureAwait(false);
							await this.ProcessInitializerRequestAsync(context, systemIdentityJson).ConfigureAwait(false);
							break;

						case "validator":
							await this.ProcessValidatorRequestAsync(context).ConfigureAwait(false);
							break;

						case "login":
							if (!context.Request.Method.IsEquals("GET") || context.Request.Path.Value.IsEndsWith(".aspx") || context.Request.Path.Value.IsEndsWith(".html") || context.Request.Path.Value.IsEndsWith(".php"))
								systemIdentityJson ??= await context.IdentifySystemAsync(requestInfo, cts.Token).ConfigureAwait(false);
							await this.ProcessLogInRequestAsync(context, systemIdentityJson, isUserInteract).ConfigureAwait(false);
							break;

						case "logout":
							if (context.Request.Path.Value.IsEndsWith(".aspx") || context.Request.Path.Value.IsEndsWith(".html") || context.Request.Path.Value.IsEndsWith(".php"))
								systemIdentityJson ??= await context.IdentifySystemAsync(requestInfo, Global.CancellationToken).ConfigureAwait(false);
							await this.ProcessLogOutRequestAsync(context, systemIdentityJson, isUserInteract).ConfigureAwait(false);
							break;

						case "cms":
							try
							{
								systemIdentityJson ??= await context.IdentifySystemAsync(requestInfo, Global.CancellationToken).ConfigureAwait(false);
								await this.ProcessCmsPortalsRequestAsync(context, systemIdentityJson?.Get<string>("ID"), systemIdentityJson?.Get<string>("ObjectID"), systemIdentityJson?.Get<string>("RepositoryEntityID") ?? systemIdentityJson?.Get<string>("ObjectName"), systemIdentityJson?.Get<string>("Location"), systemIdentityJson?.Get<string>("TrackingContentType"), systemIdentityJson?.Get<string>("TrackingBody"), systemIdentityJson?.Get<string>("TrackingBodyEncoding"), systemIdentityJson?.Get<string>("TrackingCacheControl")).ConfigureAwait(false);
							}
							catch (TaskCanceledException) { }
							catch (OperationCanceledException) { }
							catch (Exception ex)
							{
								if (ex is WampException wampException)
								{
									var wampDetails = wampException.GetDetails(requestInfo);
									context.ShowError(wampDetails.Code, wampDetails.Message, wampDetails.Type, correlationID, wampDetails.Stack + "\r\n\t" + ex.StackTrace, isDebugLogEnabled);
								}
								else
									context.ShowError(ex, isDebugLogEnabled);
								await context.WriteLogsAsync("Http.Process.Requests", $"Error occurred while processing with CMS Portals => {ex.Message}", ex).ConfigureAwait(false);
							}
							break;

						case "feed":
							try
							{
								stepwatch.Restart();
								systemIdentityJson ??= await context.IdentifySystemAsync(requestInfo, cts.Token).ConfigureAwait(false);

								requestInfo = new RequestInfo(requestInfo) { ObjectName = "Generate.Feed" };
								requestInfo.Query["x-system"] = systemIdentityJson.Get<string>("Alias");
								if (isDebugLogEnabled)
									await context.WriteLogsAsync("Http.Process.Requests", $"Call the service to generate feeds\r\n- App: {session.AppName} [{session.AppPlatform} @ {session.AppAgent}]\r\n- Request: {requestInfo.ToString(Formatting.Indented)}").ConfigureAwait(false);

								var response = (await context.CallServiceAsync(requestInfo, cts.Token, Global.Logger, "Http.Process.Requests").ConfigureAwait(false)).ToExpandoObject();

								var responseBody = response.Get<string>("Body");
								var body = responseBody != null ? responseBody.Base64ToBytes().Decompress(response.Get("BodyEncoding", "zstd")) : null;

								headers = response.Get("Headers", new Dictionary<string, string>());
								if (headers.TryGetValue("X-Node", out var nodeID))
									headers["X-Service-Node"] = nodeID;
								headers = new Dictionary<string, string>(headers, StringComparer.OrdinalIgnoreCase)
								{
									["Server-Timing"] = $"ngxPrepare;dur={stepwatch.ElapsedMilliseconds}",
									["X-Correlation-ID"] = correlationID,
									["X-Node"] = Global.NodeID
								};

								context.SetResponseHeaders(response.Get("StatusCode", (int)HttpStatusCode.OK), headers);
								if (body != null)
									await context.WriteAsync(body, cts.Token).ConfigureAwait(false);

								requestInfo.SendSessionState(systemIdentityJson, Global.ServiceName + $".HTTP", $"{requestMethod} {requestURI.AbsoluteUri}", Handler.TrackPortalStatistics);
							}
							catch (TaskCanceledException) { }
							catch (OperationCanceledException) { }
							catch (Exception ex)
							{
								if (ex is WampException wampException)
								{
									var wampDetails = wampException.GetDetails(requestInfo);
									context.ShowError(wampDetails.Code, wampDetails.Message, wampDetails.Type, correlationID, wampDetails.Stack + "\r\n\t" + ex.StackTrace, isDebugLogEnabled);
								}
								else
									context.ShowError(ex, isDebugLogEnabled);
								await context.WriteLogsAsync("Http.Process.Requests", $"Error occurred while processing feeds => {ex.Message}", ex).ConfigureAwait(false);
							}
							break;

						default:
							throw new InvalidRequestException();
					}
				}
				catch (Exception ex)
				{
					if (isUserInteract)
						context.ShowError(ex, true);
					else
						context.WriteError(Global.Logger, ex);
				}
			}

			stopwatch.Stop();
			if (isDebugLogEnabled || Global.IsVisitLogEnabled)
				await context.WriteLogsAsync("Http.Process.Requests", $"Done process a request of CMS Portals - Execution times: {stopwatch.GetElapsedTimes()}").ConfigureAwait(false);
		}

		async Task ProcessInitializerRequestAsync(HttpContext context, JObject systemIdentityJson)
		{
			var correlationID = context.GetCorrelationID();
			var session = context.GetSession();

			// activate new password
			if (context.Request.Path.Value.IsEndsWith(".aspx"))
				try
				{
					using var cts = CancellationTokenSource.CreateLinkedTokenSource(Global.CancellationToken, context.RequestAborted);
					var mode = context.Request.Query["mode"];
					var error = "undefined";
					try
					{
						await context.CallServiceAsync(new RequestInfo(session, "Users", "Activate")
						{
							Query = context.Request.QueryString.ToDictionary(query =>
							{
								query.Remove("service-name");
								query.Remove("object-name");
								query.Remove("object-identity");
							}),
							CorrelationID = correlationID
						}, cts.Token, Global.Logger, "Http.Process.Requests").ConfigureAwait(false);

						await Task.WhenAll
						(
							Global.Cache.RemoveAsync($"Attempt#{context.Connection.RemoteIpAddress}", cts.Token),
							Global.IsDebugLogEnabled ? context.WriteLogsAsync(Global.Logger, "Http.Process.Requests", $"Successfully activate {context.Request.QueryString.ToDictionary().ToJson()}") : Task.CompletedTask
						).ConfigureAwait(false);
					}
					catch (Exception ex)
					{
						await context.WriteLogsAsync(Global.Logger, "Http.Process.Requests", $"Error occurred while activating => {ex.Message}", ex).ConfigureAwait(false);
						await context.WaitOnAttemptedAsync().ConfigureAwait(false);
						var code = ex.GetHttpStatusCode();
						var message = ex.Message;
						var type = ex.GetTypeName(true);
						var stack = ex.StackTrace;
						if (ex is WampException wampException)
						{
							var details = wampException.GetDetails();
							code = details.Item1;
							message = details.Item2;
							type = details.Item3;
							stack = details.Item4;
						}
						error = new JObject
						{
							{ "Code", code },
							{ "Message", message },
							{ "Type", type },
							{ "Stack", stack },
							{ "CorrelationID", correlationID }
						}.ToString(Formatting.None);
					}

					// response
					var scripts = @"<script>
					window.__activate = window.__activate || function(){};
					__activate(" + $"\"{mode}\", {error}" + @");
					</script>";
					await context.WriteAsync(this.GetSpecialHtml(context, systemIdentityJson, "Activate").Replace("[[placeholder]]", scripts.Replace("\t\t\t\t\t", "")), "text/html", null, 0, "private, no-store, no-cache", TimeSpan.Zero, correlationID, cts.Token).ConfigureAwait(false);
				}
				catch (Exception ex)
				{
					await context.WriteLogsAsync(Global.Logger, "Http.Process.Requests", $"Error occurred while activating => {ex.Message}", ex).ConfigureAwait(false);
					var code = ex.GetHttpStatusCode();
					var message = ex.Message;
					var type = ex.GetTypeName(true);
					if (ex is WampException wampException)
					{
						var details = wampException.GetDetails();
						code = details.Code;
						message = details.Message;
						type = details.Type;
					}
					context.ShowError(code, message, type, correlationID, ex, Global.IsDebugLogEnabled);
				}

			// redirect with authenticate token
			else
			{
				if (session.User.IsAuthenticated && !context.IsAuthenticated())
				{
					var userPrincipal = new UserPrincipal(new UserIdentity(session.User.ID, session.SessionID, CookieAuthenticationDefaults.AuthenticationScheme));
					await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, userPrincipal, new AuthenticationProperties { IsPersistent = false }).ConfigureAwait(false);
				}
				else if (!session.User.IsAuthenticated && context.IsAuthenticated())
					await context.SignOutAsync().ConfigureAwait(false);

				var url = context.GetQueryParameter("ReturnURL");
				if (string.IsNullOrWhiteSpace(url))
				{
					var pathSegment = context.GetRequestPathSegments().First();
					url = pathSegment.StartsWith("~") ? $"/{pathSegment}" : "/";
				}
				else
					try
					{
						url = url.Url64Decode();
					}
					catch { }
				context.Redirect(url);
			}
		}

		async Task ProcessValidatorRequestAsync(HttpContext context)
		{
			var correlationID = context.GetCorrelationID();
			try
			{
				var callbackFunction = context.GetQueryParameter("x-callback");
				if (string.IsNullOrWhiteSpace(callbackFunction))
					callbackFunction = null;
				else
					try
					{
						callbackFunction = callbackFunction.Url64Decode();
					}
					catch { }

				var scripts = "/* nothing */";
				var session = context.GetSession();
				if (session.User.IsAuthenticated && !context.IsAuthenticated())
				{
					var userPrincipal = new UserPrincipal(new UserIdentity(session.User.ID, session.SessionID, CookieAuthenticationDefaults.AuthenticationScheme));
					await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, userPrincipal, new AuthenticationProperties { IsPersistent = false }).ConfigureAwait(false);
					scripts = $"{callbackFunction ?? "console.log"}({session.GetSessionJson(payload => payload["did"] = session.DeviceID).ToString(Formatting.None)})";
				}
				else if (!session.User.IsAuthenticated && context.IsAuthenticated())
				{
					await context.SignOutAsync().ConfigureAwait(false);
					scripts = $"{callbackFunction ?? "console.log"}({session.GetSessionJson(payload => payload["did"] = session.DeviceID).ToString(Formatting.None)})";
				}

				using var cts = CancellationTokenSource.CreateLinkedTokenSource(Global.CancellationToken, context.RequestAborted);
				await context.WriteAsync(scripts, "application/javascript", null, 0, "private, no-store, no-cache", TimeSpan.Zero, correlationID, cts.Token).ConfigureAwait(false);
			}
			catch (OperationCanceledException) { }
			catch (Exception ex)
			{
				await Task.WhenAll
				(
					context.WriteAsync($"console.error('Error occurred while validating => {ex.Message.Replace("'", @"\'")}')", "application/javascript", null, 0, "private, no-store, no-cache", TimeSpan.Zero, correlationID, Global.CancellationToken),
					context.WriteLogsAsync(Global.Logger, "Http.Process.Requests", $"Error occurred while validating => {ex.Message}", ex)
				).ConfigureAwait(false);
			}
		}

		async Task<JToken> ProcessSessionRequestAsync(HttpContext context, Session session)
		{
			var body = session.GetSessionBody().ToString(Formatting.None);
			var response = await context.CallServiceAsync(new RequestInfo(session, "Users", "Session", "POST")
			{
				Body = body,
				Extra = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
				{
					{ "Signature", body.GetHMACSHA256(Global.ValidationKey) }
				},
				CorrelationID = context.GetCorrelationID()
			}, context.RequestAborted, Global.Logger, "Authentications").ConfigureAwait(false);
			context.StoreSession(session);
			return response;
		}

		async Task ProcessLogInRequestAsync(HttpContext context, JObject systemIdentityJson, bool isUserInteract)
		{
			using var cts = CancellationTokenSource.CreateLinkedTokenSource(Global.CancellationToken, context.RequestAborted);
			var correlationID = context.GetCorrelationID();
			var headers = new Dictionary<string, string>
			{
				["Cache-Control"] = context.GetHttpCacheControl(true),
				["X-Node"] = Global.NodeID,
				["X-Correlation-ID"] = correlationID
			};

			async Task registerAsync()
			{
				try
				{
					var session = context.GetSession();
					session.DeviceID = string.IsNullOrWhiteSpace(session.DeviceID) ? $"{UtilityService.NewUUID}@vieapps-ngx" : session.DeviceID;
					session.SessionID = session.User.SessionID = !string.IsNullOrWhiteSpace(session.User.SessionID)
						? session.User.SessionID
						: !string.IsNullOrWhiteSpace(session.SessionID)
							? session.SessionID
							: UtilityService.NewUUID;

					var response = await this.ProcessSessionRequestAsync(context, session).ConfigureAwait(false);
					context.SendSessionState("Users", "POST /session", true, Handler.TrackAPIStatistics);

					await Task.WhenAll
					(
						context.WriteAsync(session.GetSessionJson(), headers, cts.Token),
						Global.IsDebugLogEnabled ? context.WriteLogsAsync(Global.Logger, "Authentications", $"Successfully register a new session {response}") : Task.CompletedTask
					).ConfigureAwait(false);
				}
				catch (Exception ex)
				{
					context.WriteError(Global.Logger, ex, null, $"Error occurred while registering a new session => {ex.Message}", true, "Authentications");
				}
			}

			async Task showAsync()
			{
				var scripts = @"<script>
				window.__prepare = window.__prepare || function(){};
				__prepare();
				</script>";
				await context.WriteAsync(this.GetSpecialHtml(context, systemIdentityJson).Replace("[[placeholder]]", scripts.Replace("\t\t\t\t", "")), "text/html", null, 0, "private, no-store, no-cache", TimeSpan.Zero, correlationID, cts.Token).ConfigureAwait(false);
			}

			void validate(Session session)
			{
				if (context.TryGetQueryParameter("x-session-id", out var sessionID))
					try
					{
						sessionID = sessionID.Url64Decode();
					}
					catch { }

				if (context.TryGetQueryParameter("x-device-id", out var deviceID))
					try
					{
						deviceID = deviceID.Url64Decode();
					}
					catch { }

				if (session == null || !session.GetEncryptedID().IsEquals(sessionID) || !session.DeviceID.IsEquals(deviceID))
					throw new InvalidSessionException("Session is invalid (The session is not issued by the system)");
			}

			async Task<JToken> signInAsync(Session session, JToken json, string state = null)
			{
				// update session
				session.User = json.Copy<User>();
				session.SessionID = session.User.SessionID = UtilityService.NewUUID;
				session.IP = context.Connection.RemoteIpAddress.ToString();

				// perform sign-in
				var userPrincipal = new UserPrincipal(new UserIdentity(session.User.ID, session.SessionID, session.User.Roles, session.User.Privileges, CookieAuthenticationDefaults.AuthenticationScheme));
				await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, userPrincipal, new AuthenticationProperties { IsPersistent = false }).ConfigureAwait(false);

				await this.ProcessSessionRequestAsync(context, session).ConfigureAwait(false);
				context.SendSessionState("Users", $"PUT /session{state}", true, Handler.TrackAPIStatistics);
				await context.WriteLogsAsync("Authentications", $"User sign-in successful\r\nIdentity: {context.User.Identity.Name}]\r\nSession Info: {session.ToJson()}").ConfigureAwait(false);

				// return the json of the signed-in session
				return session.GetSessionJson();
			}

			async Task loginAsync()
			{
				try
				{
					// prepare
					var session = context.GetSession();
					validate(session);

					var request = await context.ReadJsonAsync(cts.Token).ConfigureAwait(false);
					var account = Global.RSA.Decrypt(request.Get("Account", "")).Trim().ToLower();
					var password = Global.RSA.Decrypt(request.Get("Password", ""));
					if (string.IsNullOrWhiteSpace(account) || string.IsNullOrWhiteSpace(password))
						throw new WrongAccountException();

					// call service to login
					var body = new JObject
					{
						{ "Account", account.Encrypt(Global.EncryptionKey) },
						{ "Password", password.Encrypt(Global.EncryptionKey) },
					}.ToString(Formatting.None);

					var response = await context.CallServiceAsync(new RequestInfo(session, "Users", "Session", "PUT")
					{
						Body = body,
						Extra = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
						{
							{ "Signature", body.GetHMACSHA256(Global.ValidationKey) }
						},
						CorrelationID = correlationID
					}, cts.Token, Global.Logger, "Authentications").ConfigureAwait(false);

					// check to see the account is two-factor authenticaion required 
					var require2FA = response.Get("Require2FA", false);

					if (require2FA)
						response = new JObject
						{
							{ "ID", response.Get<string>("ID") },
							{ "Require2FA", true },
							{ "Providers", response["Providers"] as JArray }
						};

					else
						response = await signInAsync(session, response).ConfigureAwait(false);

					// response
					await Task.WhenAll
					(
						Global.Cache.RemoveAsync($"Attempt#{context.Connection.RemoteIpAddress}", cts.Token),
						context.WriteAsync(response, headers, cts.Token),
						Global.IsDebugLogEnabled ? context.WriteLogsAsync(Global.Logger, "Authentications", $"Successfully log a session in {response}") : Task.CompletedTask
					).ConfigureAwait(false);
				}
				catch (Exception ex)
				{
					await context.WaitOnAttemptedAsync().ConfigureAwait(false);
					context.WriteError(Global.Logger, ex);
				}
			}

			async Task loginOTPAsync()
			{
				try
				{
					// prepare
					var session = context.GetSession();
					validate(session);

					var request = await context.ReadJsonAsync(cts.Token).ConfigureAwait(false);
					var id = request.Get<string>("ID");
					var otp = request.Get<string>("OTP");
					var info = request.Get<string>("Info");

					if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(otp) || string.IsNullOrWhiteSpace(info))
						throw new InvalidTokenException("OTP is invalid (empty)");

					try
					{
						id = Global.RSA.Decrypt(id);
						otp = Global.RSA.Decrypt(otp);
						info = Global.RSA.Decrypt(info);
					}
					catch (Exception ex)
					{
						throw new InvalidTokenException("OTP is invalid (cannot decrypt)", ex);
					}

					// call service to validate
					var body = new JObject
					{
						{ "ID", id.Encrypt(Global.EncryptionKey) },
						{ "OTP", otp.Encrypt(Global.EncryptionKey) },
						{ "Info", info.Encrypt(Global.EncryptionKey) }
					}.ToString(Formatting.None);

					var response = await signInAsync(session, await context.CallServiceAsync(new RequestInfo(session, "Users", "OTP", "POST")
					{
						Body = body,
						CorrelationID = correlationID
					}, cts.Token, Global.Logger, "Authentications").ConfigureAwait(false), "/otp").ConfigureAwait(false);

					// response
					await Task.WhenAll
					(
						Global.Cache.RemoveAsync($"Attempt#{context.Connection.RemoteIpAddress}", cts.Token),
						context.WriteAsync(response, headers, cts.Token),
						Global.IsDebugLogEnabled ? context.WriteLogsAsync(Global.Logger, "Authentications", $"Successfully log a session in with OTP {response}") : Task.CompletedTask
					).ConfigureAwait(false);
				}
				catch (Exception ex)
				{
					await context.WaitOnAttemptedAsync().ConfigureAwait(false);
					context.WriteError(Global.Logger, ex);
				}
			}

			async Task resetAsync()
			{
				try
				{
					// prepare
					var request = await context.ReadJsonAsync(cts.Token).ConfigureAwait(false);
					var account = Global.RSA.Decrypt(request.Get("Account", "")).Trim().ToLower();
					var password = Global.RSA.Decrypt(request.Get("Password", ""));
					if (string.IsNullOrWhiteSpace(account) || string.IsNullOrWhiteSpace(password))
						throw new InformationInvalidException();

					var language = context.GetParameter("language") ?? "vi-VN";
					var requestURI = context.GetRequestUri();
					var pathSegment = requestURI.GetRequestPathSegments().First();
					var renewURI = $"{requestURI.Scheme}://{context.GetParameter("X-SRP-Host") ?? requestURI.Host}/{(pathSegment.StartsWith("~") ? $"{pathSegment}/" : "")}initializer.aspx?" + "code={{code}}&mode={{mode}}" + $"&language={language}";

					// call service to reset password
					var session = context.GetSession();
					var response = await context.CallServiceAsync(new RequestInfo(session, "Users", "Account", "PUT")
					{
						Query = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
						{
							{ "object-identity", "Reset" },
							{ "related-service", "Portals" },
							{ "language", language },
							{ "organization", systemIdentityJson.Get<string>("Alias") }
						},
						Extra = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
						{
							{ "Account", account.Encrypt(Global.EncryptionKey) },
							{ "Password", password.Encrypt(Global.EncryptionKey) },
							{ "Uri", renewURI.Encrypt(Global.EncryptionKey) }
						},
						CorrelationID = correlationID
					}, cts.Token, Global.Logger, "Authentications").ConfigureAwait(false);

					// response
					context.SendSessionState("Users", "PATCH /account", true, Handler.TrackAPIStatistics);
					await Task.WhenAll
					(
						Global.Cache.RemoveAsync($"Attempt#{context.Connection.RemoteIpAddress}", cts.Token),
						context.WriteAsync(response, headers, cts.Token),
						Global.IsDebugLogEnabled ? context.WriteLogsAsync(Global.Logger, "Authentications", $"Successfully send a renew password request {response}") : Task.CompletedTask
					).ConfigureAwait(false);
				}
				catch (Exception ex)
				{
					await context.WaitOnAttemptedAsync().ConfigureAwait(false);
					context.WriteError(Global.Logger, ex);
				}
			}

			try
			{
				switch (context.Request.Method)
				{
					// log a session in or others
					case "POST":
						switch ((context.GetQueryParameter("x-mode") ?? "login").ToUpper())
						{
							// log a session in with OTP
							case "OTP":
								await loginOTPAsync().ConfigureAwait(false);
								break;

							// forgot password
							case "RESET":
							case "RENEW":
							case "FORGOT":
								await resetAsync().ConfigureAwait(false);
								break;

							// log a session in
							default:
								await loginAsync().ConfigureAwait(false);
								break;
						}
						break;

					// log a session in with OTP
					case "PUT":
						await loginOTPAsync().ConfigureAwait(false);
						break;

					// forgot password
					case "PATCH":
						await resetAsync().ConfigureAwait(false);
						break;

					// register a session in or open login form
					default:
						await (isUserInteract ? showAsync() : registerAsync()).ConfigureAwait(false);
						break;
				}
			}
			catch (Exception ex)
			{
				if (isUserInteract)
				{
					await context.WriteLogsAsync("Http.Process.Requests", $"Error occurred while logging in => {ex.Message}", ex).ConfigureAwait(false);
					var code = ex.GetHttpStatusCode();
					var message = ex.Message;
					var type = ex.GetTypeName(true);
					if (ex is WampException wampException)
					{
						var details = wampException.GetDetails();
						code = details.Code;
						message = details.Message;
						type = details.Type;
					}
					context.ShowError(code, message, type, correlationID, ex, Global.IsDebugLogEnabled);
				}
				else
					context.WriteError(Global.Logger, ex);
			}
		}

		async Task ProcessLogOutRequestAsync(HttpContext context, JObject systemIdentityJson, bool isUserInteract)
		{
			var correlationID = context.GetCorrelationID();
			var headers = new Dictionary<string, string>
			{
				["Cache-Control"] = context.GetHttpCacheControl(true),
				["X-Node"] = Global.NodeID,
				["X-Correlation-ID"] = correlationID
			};
			try
			{
				// get session
				var session = context.GetSession();

				// call service to delete the session
				using var cts = CancellationTokenSource.CreateLinkedTokenSource(Global.CancellationToken, context.RequestAborted);
				var response = await context.CallServiceAsync(new RequestInfo(session, "Users", "Session", "DELETE")
				{
					Header = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
					{
						{ "x-app-token", $"x-session-temp-token-{correlationID}" }
					},
					Extra = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
					{
						{ "Signature", $"x-session-temp-token-{correlationID}".GetHMACSHA256(Global.ValidationKey) }
					},
					CorrelationID = correlationID
				}, cts.Token, Global.Logger, "Authentications").ConfigureAwait(false);

				// perform log out
				await context.SignOutAsync().ConfigureAwait(false);
				await context.WriteLogsAsync("Authentications", $"User sign-out successful\r\nIdentity: {context.User.Identity.Name}]\r\nSession Info: {session.ToJson()}").ConfigureAwait(false);

				// response
				if (isUserInteract)
				{
					context.StoreSession(session);
					context.SendSessionState("Users", "DELETE /session", false, Handler.TrackAPIStatistics);
					var scripts = @"<script>
					window.__logout = window.__logout || function(){};
					__logout(true);
					</script>";
					await Task.WhenAll
					(
						context.WriteAsync(this.GetSpecialHtml(context, systemIdentityJson, "Log out").Replace("[[placeholder]]", scripts.Replace("\t\t\t\t\t", "")), "text/html", null, 0, "private, no-store, no-cache", TimeSpan.Zero, correlationID, cts.Token),
						Global.IsDebugLogEnabled ? context.WriteLogsAsync(Global.Logger, "Authentications", $"Successfully log a session out (direct) {response}") : Task.CompletedTask
					).ConfigureAwait(false);
				}
				else
				{
					session.User = new User("", "", [SystemRole.All.ToString()], new())
					{
						SessionID = session.SessionID = UtilityService.NewUUID
					};
					response = await this.ProcessSessionRequestAsync(context, session).ConfigureAwait(false);
					context.SendSessionState("Users", "POST /session", true, Handler.TrackAPIStatistics);

					await Task.WhenAll
					(
						context.WriteAsync(session.GetSessionJson(), headers, cts.Token),
						Global.IsDebugLogEnabled ? context.WriteLogsAsync(Global.Logger, "Authentications", $"Successfully log a session out {response}") : Task.CompletedTask
					).ConfigureAwait(false);
				}
			}
			catch (Exception ex)
			{
				if (isUserInteract)
				{
					await context.WriteLogsAsync(Global.Logger, "Authentications", $"Error occurred while logging out => {ex.Message}", ex).ConfigureAwait(false);
					var code = ex.GetHttpStatusCode();
					var message = ex.Message;
					var type = ex.GetTypeName(true);
					if (ex is WampException wampException)
					{
						var details = wampException.GetDetails();
						code = details.Code;
						message = details.Message;
						type = details.Type;
					}
					context.ShowError(code, message, type, correlationID, ex, Global.IsDebugLogEnabled);
				}
				else
					context.WriteError(Global.Logger, ex);
			}
		}

		async Task ProcessCmsPortalsRequestAsync(HttpContext context, string systemID, string objectID, string objectNameOrContentTypeID, string location, string trackingContentType, string trackingBody, string trackingBodyEncoding, string trackingCacheControl)
		{
			var headers = new Dictionary<string, string>
			{
				["Cache-Control"] = trackingCacheControl ?? "private, no-store, no-cache",
				["X-Node"] = Global.NodeID,
				["X-Correlation-ID"] = context.GetCorrelationID()
			};
			if (string.IsNullOrWhiteSpace(trackingBody))
			{
				var request = new JObject
				{
					["SystemID"] = systemID,
					["ObjectID"] = objectID
				};
				request[!string.IsNullOrWhiteSpace(objectNameOrContentTypeID) && objectNameOrContentTypeID.IsValidUUID() ? "RepositoryEntityID" : "ObjectName"] = objectNameOrContentTypeID;
				location ??= $"{this.RemoveURITrail(Handler.CMSPortalsHttpURI)}/home?redirect={$"/portals/initializer?x-request={request.ToString(Formatting.None).Url64Encode()}".Url64Encode()}&r={UtilityService.GetRandomNumber()}";
				context.SetResponseHeaders((int)HttpStatusCode.Redirect, new Dictionary<string, string>(headers, StringComparer.OrdinalIgnoreCase)
				{
					["Location"] = location
				});
				if (Global.IsDebugLogEnabled || context.Request.Query.ContainsKey("x-logs"))
					await context.WriteLogsAsync("Http.Process.Requests", $"Redirect to a location (of CMS Portals) successful => {location}").ConfigureAwait(false);
			}
			else
			{
				headers = new Dictionary<string, string>(headers, StringComparer.OrdinalIgnoreCase)
				{
					["Content-Type"] = trackingContentType ?? "image/webp"
				};
				if ("application/javascript".IsEquals(trackingContentType))
				{
					var origin = context.GetOriginUri() ?? context.GetReferUri();
					headers["Access-Control-Allow-Origin"] = origin != null ? $"{origin.Scheme}://{origin.Host}" : "*";
					headers["Access-Control-Allow-Credentials"] = "true";
				}
				if (!string.IsNullOrWhiteSpace(trackingCacheControl))
				{
					headers["ETag"] = $"vieapps#{context.GetRequestUri().AbsoluteUri.GenerateUUID()}";
					headers["Expires"] = DateTime.Now.AddDays(366).ToHttpString();
					headers["Last-Modified"] = DateTime.Now.ToHttpString();
				}
				context.SetResponseHeaders((int)HttpStatusCode.OK, headers);
				using var cts = CancellationTokenSource.CreateLinkedTokenSource(Global.CancellationToken, context.RequestAborted);
				await context.WriteAsync(trackingBody.Base64ToBytes().Decompress(trackingBodyEncoding ?? "zstd"), cts.Token).ConfigureAwait(false);
				if (Global.IsDebugLogEnabled || context.Request.Query.ContainsKey("x-logs"))
					await context.WriteLogsAsync("Http.Process.Requests", $"Process the tracking request successful => {context.GetRequestUrl()}").ConfigureAwait(false);
			}
		}

		string GetSpecialHtml(HttpContext context, JObject systemIdentityJson, string title = "Log in")
		{
			var organizationID = systemIdentityJson.Get<string>("ID");
			var organizationAlias = systemIdentityJson.Get<string>("Alias");

			var portalsHttpURI = this.RemoveURITrail(systemIdentityJson.Get("PortalsHttpURI", Handler.PortalsHttpURI));
			var portalsWebSocketURI = this.RemoveURITrail(systemIdentityJson.Get("PortalsWebSocketURI", Handler.PortalsWebSocketURI).Replace("http://", "ws://").Replace("https://", "wss://"));
			var filesHttpURI = this.RemoveURITrail(systemIdentityJson.Get("FilesHttpURI", Handler.FilesHttpURI));

			var rootURL = context.GetRequestPathSegments().First().StartsWith("~") ? "" : "/";
			var language = context.GetQueryParameter("language") ?? systemIdentityJson.Get("Language", "en-US");

			var session = context.GetSession();
			var isMobile = string.IsNullOrWhiteSpace(session.AppPlatform) || session.AppPlatform.IsContains("Desktop") ? "false" : "true";
			var osInfo = (session.AppAgent ?? "").GetOSInfo();

			var version = DateTime.Now.GetTimeQuarter().ToUnixTimestamp().ToString();
			var scripts = "<script>__vieapps={ids:{" + $"system:\"{organizationID}\"" + "},URLs:{root:" + $"\"{rootURL}\",portals:\"{portalsHttpURI}\",websockets:\"{portalsWebSocketURI}\",files:\"{filesHttpURI}\"" + "}" + $",language:\"{language}\",isMobile:{isMobile},osInfo:\"{osInfo}\",correlationID:\"{context.GetCorrelationID()}\"" + "};</script>"
				+ $"<script src=\"{UtilityService.GetAppSetting("Portals:Desktops:Resources:JQuery", "https://cdnjs.cloudflare.com/ajax/libs/jquery/3.7.1/jquery.min.js")}\"></script>"
				+ $"<script src=\"{UtilityService.GetAppSetting("Portals:Desktops:Resources:CryptoJs", "https://cdnjs.cloudflare.com/ajax/libs/crypto-js/4.2.0/crypto-js.min.js")}\"></script>"
				+ $"<script src=\"{portalsHttpURI}/_assets/rsa.js?v={version}\"></script>"
				+ $"<script src=\"{portalsHttpURI}/_assets/default.js?v={version}\"></script>"
				+ $"<script src=\"{portalsHttpURI}/_themes/default/js/all.js?v={version}\"></script>"
				+ $"<script src=\"{portalsHttpURI}/_js/o_{organizationID}.js?v={version}\"></script>";

			return @$"<!DOCTYPE html>
				<html xmlns=""http://www.w3.org/1999/xhtml"">
				<head>{(rootURL.Equals("/") ? "" : $"\r\n<base href=\"{portalsHttpURI}/~{organizationAlias}/\"/>")}
				<title>{title.GetCapitalizedFirstLetter()} ({organizationAlias.ToUpper()})</title>
				<meta name=""viewport"" content=""width=device-width, initial-scale=1""/>
				<link rel=""stylesheet"" href=""{portalsHttpURI}/_assets/default.css?v={version}""/>
				<link rel=""stylesheet"" href=""{portalsHttpURI}/_themes/default/css/all.css?v={version}""/>
				</head>
				<body>
				{scripts}
				[[placeholder]]
				</body>
				</html>".Replace("\t\t\t\t\t", "");
		}

		string RemoveURITrail(string uri, string trail = "/")
		{
			uri ??= "";
			trail = string.IsNullOrWhiteSpace(trail) ? trail = "/" : trail;
			while (uri.EndsWith(trail))
				uri = uri.Left(uri.Length - trail.Length);
			return uri;
		}

		internal static void Connect(int waitingTimes = 6789)
		{
			Global.Logger.LogDebug($"Attempting to connect to API Gateway Router [{(Router.GotBackupRouter() ? "Primary: " : "")}{new Uri(Router.GetRouterStrInfo()).GetResolvedURI()}{(Router.GotBackupRouter() ? $" - Backup: {new Uri(Router.GetRouterStrInfo(true)).GetResolvedURI()}" : "")}]");
			Global.Connect
			(
				// incoming - on connection established
				(sender, arguments) =>
				{
					Global.PrimaryInterCommunicateMessageUpdater?.Dispose();
					Global.PrimaryInterCommunicateMessageUpdater = Router.IncomingChannel.Subscribe<CommunicateMessage>
					(
						"messages.services.portals",
						message => Global.NodeID.IsEquals(message.ExcludedNodeID) ? Task.CompletedTask : Handler.ProcessInterCommunicateMessageAsync(message),
						exception => Global.WriteLogsAsync(Global.Logger, "Http.Process.Requests", exception.Message, exception)
					);
					Global.SecondaryInterCommunicateMessageUpdater?.Dispose();
					Global.SecondaryInterCommunicateMessageUpdater = Router.IncomingChannel.Subscribe<CommunicateMessage>
					(
						"messages.services.apigateway",
						message => Global.NodeID.IsEquals(message.ExcludedNodeID) ? Task.CompletedTask : Handler.ProcessGatewayCommunicateMessageAsync(message),
						exception => Global.WriteLogsAsync(Global.Logger, "Http.Process.Requests", exception.Message, exception)
					);
					if (!Router.GotBackupRouter())
					{
						if (Handler.Cache.UseL1Cache)
						{
							Handler.CacheUpdater?.Dispose();
							Handler.CacheUpdater = Router.IncomingChannel.Subscribe<CommunicateMessage>("messages.services.portals.http.l1cache", message => message.SetL1Cache());
						}
						Handler.CacheCommunicator?.Dispose();
						Handler.CacheCommunicator = Router.IncomingChannel.AssignProcessL1CacheRequest(Handler.Cache, Global.ServiceName);
						Handler.Cache.AssignSendL1CacheRequest(Global.ServiceName, Global.NodeID);
					}
				},
				// outgoing - on connection established
				async (sender, arguments) =>
				{
					await Global.RegisterServiceAsync().ConfigureAwait(false);
					try
					{
						while (Router.IncomingChannel == null)
							await Task.Delay(UtilityService.GetRandomNumber(13, 123), Global.CancellationToken).ConfigureAwait(false);

						new CommunicateMessage(Global.ServiceName)
						{
							Type = "BlackIPs#Sync",
							ExcludedNodeID = Global.NodeID
						}.Send();

						new CommunicateMessage(Global.ServiceName)
						{
							Type = "HarmfulIPs#Sync",
							ExcludedNodeID = Global.NodeID
						}.Send();

						new CommunicateMessage("APIGateway")
						{
							Type = "McpServer#RequestInfo"
						}.Send();
						new CommunicateMessage("APIGateway")
						{
							Type = "McpServer#SyncSession"
						}.Send();
					}
					catch { }
				},
				// backup - on connection established
				(sender, arguments) =>
				{
					if (Handler.Cache.UseL1Cache)
					{
						Handler.CacheUpdater?.Dispose();
						Handler.CacheUpdater = Router.BackupChannel.Subscribe<CommunicateMessage>("messages.services.portals.http.l1cache", message => message.SetL1Cache());
					}
					Handler.CacheCommunicator?.Dispose();
					Handler.CacheCommunicator = Router.BackupChannel.AssignProcessL1CacheRequest(Handler.Cache, Global.ServiceName);
					Handler.Cache.AssignSendL1CacheRequest(Global.ServiceName, Global.NodeID, true);
				},
				waitingTimes
			);
		}

		internal static void Disconnect()
		{
			Handler.CacheCommunicator?.Dispose();
			Handler.CacheCommunicator = null;
			Handler.CacheUpdater?.Dispose();
			Handler.CacheUpdater = null;
			Global.UnregisterService();
			Global.Disconnect();
		}

		internal static Task ProcessInterCommunicateMessageAsync(CommunicateMessage message)
		{
			if (message.Type.IsEquals("BlackIPs#Update") || message.Type.IsEquals("BlackIPs#Remove"))
				message.UpdateBlackIPs(message.Type.IsEquals("BlackIPs#Remove"));
			else if (message.Type.IsEquals("BlackIPs#Sync"))
				message.SyncBlackIPs(Global.ServiceName, Global.NodeID);
			else if (message.Type.IsEquals("BlackIPs#Reset"))
				message.ResetBlackIPs();
			else if (message.Type.IsEquals("HarmfulIPs#Update") || message.Type.IsEquals("HarmfulIPs#Remove"))
				message.UpdateHarmfulIPs(message.Type.IsEquals("HarmfulIPs#Remove"));
			else if (message.Type.IsEquals("HarmfulIPs#Sync"))
				message.SyncHarmfulIPs(Global.ServiceName, Global.NodeID);
			else if (message.Type.IsEquals("HarmfulIPs#Pause"))
				RequestExtensions.AutoBlockHarmfulRequest = false;
			else if (message.Type.IsEquals("HarmfulIPs#Resume"))
				RequestExtensions.AutoBlockHarmfulRequest = true;
			else if (message.Type.IsEquals("Sessions#Track#Disable"))
				Handler.TrackSessions = false;
			else if (message.Type.IsEquals("Sessions#Track#Enable"))
				Handler.TrackSessions = true;
			return Task.CompletedTask;
		}

		internal static async Task ProcessGatewayCommunicateMessageAsync(CommunicateMessage message)
		{
			if (message.Type.IsEquals("Service#RequestInfo"))
				await Global.SendServiceInfoAsync().ConfigureAwait(false);

			else if (message.Type.IsStartsWith("McpServer#"))
				await message.ProcessGatewayMessageAsync().ConfigureAwait(false);

			else if (message.Type.IsEquals("Monitor#Enable") || message.Type.IsEquals("Monitor#Start"))
			{
				var logPath = UtilityService.GetAppSetting("Path:Logs");
				if (!string.IsNullOrWhiteSpace(logPath) && Directory.Exists(logPath))
				{
					Global.Monitor = true;
					Handler.StartMonitor(logPath);
				}
			}

			else if (message.Type.IsEquals("Monitor#Disable") || message.Type.IsEquals("Monitor#Stop"))
			{
				Handler.StopMonitor();
				if (message.Type.IsEquals("Monitor#Disable"))
					Global.Monitor = false;
			}
		}

		internal static void StartMonitor(string logPath)
		{
			ThreadPool.GetMaxThreads(out var maxWorker, out var maxIO);
			ThreadPool.GetMinThreads(out var minWorker, out var minIO);
			Global.Logger.LogInformation($"ThreadPool:\r\n\t- Max: {maxWorker:###,##0} / {maxIO:###,##0}\r\n\t- Min: {minWorker:###,##0} / {minIO:###,##0}");

			if (Global.Monitor && !string.IsNullOrWhiteSpace(logPath))
			{
				Handler.MonitorLogPath = Path.Combine(logPath, $"{Global.ServiceName.ToLower()}.http.{Environment.ProcessId}");
				Global.Logger.LogInformation($"Start to monitor threadpool/cache - Log path => {Handler.MonitorLogPath}");

				if (!Int32.TryParse(UtilityService.GetAppSetting("Portals:Monitor:Cache:Interval"), out var interval) || interval < 0)
					interval = 15000;
				if (!Int32.TryParse(UtilityService.GetAppSetting("Portals:Monitor:Cache:Warn"), out var warnQS) || warnQS < 0)
					warnQS = 1000;
				if (!Int32.TryParse(UtilityService.GetAppSetting("Portals:Monitor:Cache:Critical"), out var criticalQS) || criticalQS < 0)
					criticalQS = 5000;

				Global.Cache.StartMonitor(
					(msg, details) => Handler.OnMonitor("HTTP", msg, details),
					(msg, _, ex) => Handler.OnMonitor("HTTP", msg, ("", 0, 0, 0), ex),
					(msg, _) => Handler.OnMonitor("HTTP", msg, ("", 0, 0, 0)),
					(msg, _, ex) => Handler.OnMonitor("HTTP", msg, ("", 0, 0, 0), ex),
					interval, warnQS, criticalQS, Global.CancellationToken);

				Handler.Cache.StartMonitor(
					(msg, details) => Handler.OnMonitor("Service", msg, details),
					(msg, _, ex) => Handler.OnMonitor("Service", msg, ("", 0, 0, 0), ex),
					(msg, _) => Handler.OnMonitor("Service", msg, ("", 0, 0, 0)),
					(msg, _, ex) => Handler.OnMonitor("Service", msg, ("", 0, 0, 0), ex),
					interval, warnQS, criticalQS, Global.CancellationToken);
			}
		}

		internal static void StopMonitor()
		{
			try
			{
				Global.Cache.StopMonitor();
				Handler.Cache.StopMonitor();
			}
			catch { }
		}

		internal static void OnMonitor(string prefix, string message, (string Level, long Total, long Interactive, long PingMiliseconds) details, Exception ex = null)
		{
			ThreadPool.GetAvailableThreads(out var workers, out var io);
			var now = DateTime.Now;
			var pid = Environment.ProcessId.ToString();
			var logs = "PID: " + pid + " @ " + now.ToString("HH:mm:ss") + " -----\r\n";
			if (string.IsNullOrWhiteSpace(details.Level))
				logs += message;
			else
				logs += "Available threads - Workers: " + workers.ToString("###,##0") + " / Async IO: " + io.ToString("###,##0")	+ "\r\n" + prefix + " Caching: " + message;
			if (ex != null)
				logs += "\r\n" + ex.Message + " [" + ex.GetTypeName(true) + "]\r\nStack: " + ex.StackTrace;
			logs += "\r\n\r\n";
			if (!Global.CancellationTokenSource.IsCancellationRequested)
				File.AppendAllTextAsync(Handler.MonitorLogPath + "-" + now.ToString("yyyyMMddHH") + "-monitor.txt", logs, Global.CancellationToken).Execute();
		}
	}

	public class Starter
	{
		readonly RequestDelegate NextAsync;
		readonly string AllowMethods;

		public Starter(RequestDelegate next, string allowMethods = null)
		{
			this.NextAsync = next;
			this.AllowMethods = allowMethods;
		}

		public async Task Invoke(HttpContext context)
		{
			// black IPs
			if (context.IsBlackIP())
			{
				context.SetResponseHeaders((int)HttpStatusCode.Forbidden);
				return;
			}

			// HTTP request
			if (!context.WebSockets.IsWebSocketRequest)
			{
				// CORS options
				context.Response.Headers.AccessControlAllowOrigin = "*";
				if (context.Request.Method.IsEquals("OPTIONS"))
				{
					var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
					{
						["X-Node"] = Global.NodeID,
						["Access-Control-Allow-Methods"] = this.AllowMethods ?? "GET,POST"
					};
					if (context.Request.Headers.TryGetValue("Access-Control-Request-Headers", out var requestHeaders))
						headers["Access-Control-Allow-Headers"] = requestHeaders;
					context.SetResponseHeaders((int)HttpStatusCode.OK, headers);
				}

				// visit logs
				else
				{
					context.SetItem("PipelineStopwatch", Stopwatch.StartNew());
					if (Global.IsVisitLogEnabled)
						await context.WriteVisitStartingLogAsync().ConfigureAwait(false);
				}
			}

			// next step
			await this.NextAsync(context).ConfigureAwait(false);
		}
	}

	public class L1CacheInfo
	{
		public L1CacheInfo(bool alwaysUseHTTPs, bool alwaysReturnHTTPs, string portalsHttpURI, string filesHttpURI, Dictionary<string, string> headers, string bodyCacheKey)
		{
			this.AlwaysUseHTTPs = alwaysUseHTTPs;
			this.AlwaysReturnHTTPs = alwaysReturnHTTPs;
			this.PortalsURL = portalsHttpURI;
			this.FilesURL = filesHttpURI;
			this.Headers = headers;
			this.BodyCacheKey = bodyCacheKey;
			this.Headers["X-Node"] = Global.NodeID;
		}
		public L1CacheInfo(JToken json)
		{
			this.CopyFrom(json, ["Headers"], info => info.Headers = new Dictionary<string, string>(json?.Get<JObject>("Headers")?.ToDictionary<string>() ?? new(), StringComparer.OrdinalIgnoreCase));
			this.Headers["X-Node"] = Global.NodeID;
		}
		public bool AlwaysUseHTTPs { get; set; }
		public bool AlwaysReturnHTTPs { get; set; }
		public string PortalsURL { get; set; }
		public string FilesURL { get; set; }
		public string BodyCacheKey { get; set; }
		public Dictionary<string, string> Headers { get; set; }
	}

	internal static class HandlerExtentions
	{
		public static bool IsL1CacheAvailable(this HttpContext context, string portalsHttpURI = null)
		{
			var url = context.GetRequestUrl();
			var start = url.IndexOf("/~");
			if (start < 0 && (url.IsStartsWith(Handler.PortalsHttpURI) || url.IsStartsWith(portalsHttpURI)))
				return false;
			var end = start > 0 ? url.IndexOf('/', start + 1) : -1;
			var alias = start < 0 ? null : end > start ? url.Substring(start + 2, end - start - 3) : url.Substring(start + 2);
			return string.IsNullOrWhiteSpace(alias);
		}

		public static async Task<bool> ProcessL1CacheAsync(this HttpContext context, bool isForceCacheRequested, Stopwatch stopwatch)
		{
			var stepwatch = Stopwatch.StartNew();
			var isDebugLogEnabled = Global.IsDebugLogEnabled || context.ContainsKey("x-logs") || context.ContainsKey("x-cache-logs");
			var url = context.GetRequestUrl();

			if (!Handler.Cache.UseL1Cache || isForceCacheRequested || !context.IsL1CacheAvailable())
			{
				if (isDebugLogEnabled)
					await context.WriteLogsAsync("Http.Process.Requests", $"Stop process L1-Cache [{!Handler.Cache.UseL1Cache}/{isForceCacheRequested}] => {url}").ConfigureAwait(false);
				return false;
			}

			var gotWWW = url.IndexOf("//www.") > 0;
			var originIsRequired = Handler.CrossOrigin.IsEquals("use-credentials");

			var info = Handler.Cache.GetL1CacheItem<L1CacheInfo>(context.GetL1CacheKey());
			if (info == null && originIsRequired && gotWWW)
				info = Handler.Cache.GetL1CacheItem<L1CacheInfo>(context.GetL1CacheKey(false));

			if (info == null)
			{
				if (isDebugLogEnabled)
					await context.WriteLogsAsync("Http.Process.Requests", $"Stop process L1-Cache (no info) [{context.GetL1CacheKey()} => {url}]").ConfigureAwait(false);
				return false;
			}

			info.Headers.TryGetValue("ETag", out var eTag);
			info.Headers.TryGetValue("Content-Type", out var contentType);
			info.Headers.TryGetValue("Last-Modified", out var lastModified);

			if (eTag == null || contentType == null || lastModified == null)
			{
				if (isDebugLogEnabled)
					await context.WriteLogsAsync("Http.Process.Requests", $"Stop process L1-Cache (no required info) [{context.GetL1CacheKey()} => {eTag}/{contentType}/{lastModified}]").ConfigureAwait(false);
				return context.RemoveL1Cache(info.BodyCacheKey);
			}

			if (info.Headers.TryGetValue("Expires", out var expiresAt) && !string.IsNullOrWhiteSpace(expiresAt) && expiresAt.FromHttpDateTime() < DateTime.UtcNow)
			{
				if (isDebugLogEnabled)
					await context.WriteLogsAsync("Http.Process.Requests", $"Stop process L1-Cache (expired) [{context.GetL1CacheKey()} => {expiresAt.FromHttpDateTime().ToIsoString()}]").ConfigureAwait(false);
				return context.RemoveL1Cache(info.BodyCacheKey);
			}

			var allowOrigin = "*";
			var isHtml = contentType.IsStartsWith("text/html");
			var isBase64 = contentType.IsStartsWith("image/") || contentType.IsStartsWith("font/");
			if (originIsRequired && !isHtml && !isBase64)
			{
				var origin = context.GetHeaderParameter("Origin") ?? context.GetHeaderParameter("Referer");
				if (!string.IsNullOrWhiteSpace(origin))
				{
					var originURI = new Uri(origin);
					allowOrigin = $"{originURI.Scheme}://{originURI.Host}";
				}
			}

			info.Headers["X-Cache"] = "L1-HTTP-200";
			info.Headers["Access-Control-Allow-Origin"] = allowOrigin;

			var statusCode = (int)HttpStatusCode.OK;
			byte[] body = null;
			var gotBody = true;

			var modifiedSince = context.GetHeaderParameter("If-Modified-Since") ?? context.GetHeaderParameter("If-Unmodified-Since");
			if (modifiedSince != null && modifiedSince.FromHttpDateTime() >= lastModified.FromHttpDateTime() && eTag.IsEquals(context.GetHeaderParameter("If-None-Match")))
			{
				info.Headers["X-Cache"] = "L1-HTTP-304";
				statusCode = (int)HttpStatusCode.NotModified;
				gotBody = false;
			}
			context.UpdateServerTiming("ngxPrepare", stepwatch.ElapsedMilliseconds);

			if (gotBody)
			{
				stepwatch.Restart();
				var cached = originIsRequired
					? gotWWW ? Handler.Cache.GetL1CacheItem(info.BodyCacheKey + ":WWW") : Handler.Cache.GetL1CacheItem(info.BodyCacheKey)
					: Handler.Cache.GetL1CacheItem(info.BodyCacheKey);
				if (cached == null && originIsRequired && gotWWW)
					cached = Handler.Cache.GetL1CacheItem(info.BodyCacheKey);

				if (cached == null)
				{
					if (isDebugLogEnabled)
						await context.WriteLogsAsync("Http.Process.Requests", $"Stop process L1-Cache (no body) [{context.GetL1CacheKey()} => {context.GetRequestUrl()}]").ConfigureAwait(false);
					return context.RemoveL1Cache(info.BodyCacheKey);
				}

				if (cached is string cachedBody)
				{
					if (isBase64)
						body = cachedBody.Base64ToBytes();
					else
					{
						cachedBody = cachedBody.Replace("~#/", info.PortalsURL + "/").Replace("~~~/", info.PortalsURL + "/").Replace("~~/", info.FilesURL + "/").Replace("~/", "/");
						cachedBody = context.NormalizeHtml(cachedBody, info.AlwaysUseHTTPs, info.AlwaysReturnHTTPs);
						body = cachedBody.ToBytes();
					}
					Handler.Cache.SetL1CacheItem(info.BodyCacheKey + (originIsRequired && gotWWW ? ":WWW" : ""), body);
					if (isDebugLogEnabled)
						await context.WriteLogsAsync("Http.Process.Requests", $"Update L1-Cache (bytes) successful ({info.BodyCacheKey} -> {body.Length}) [{context.GetL1CacheKey()} => {context.GetRequestUrl()}]").ConfigureAwait(false);
				}
				else
					body = cached.As<byte[]>();

				context.UpdateServerTiming("ngxFetch", stepwatch.ElapsedMilliseconds);
			}

			info.Headers["X-Correlation-ID"] = context.GetCorrelationID();
			context.UpdateServerTiming("ngxServe", stopwatch.ElapsedMilliseconds);
			context.SetResponseHeaders(statusCode, info.Headers);
			if (body != null)
				try
				{
					await context.WriteAsync(body, context.RequestAborted).ConfigureAwait(false);
				}
				catch (TaskCanceledException) { }
				catch (OperationCanceledException) { }
				catch (Exception ex)
				{
					await context.WriteLogsAsync("Http.Process.Requests", $"Error occurred while processing L1-Cache => {ex.Message}", ex).ConfigureAwait(false);
				}

			stopwatch.Stop();
			Task.WhenAll
			(
				context.SendSessionStateAsync(true, Handler.TrackPortalStatistics),
				context.WriteLogsAsync("Http.Process.Requests", $"Process L1-Cache was done {(isDebugLogEnabled ? $" [{context.GetL1CacheKey()} => {context.GetRequestUrl()}]\r\nInfo: {info.ToJson()}" : "")} - Execution times: {stopwatch.GetElapsedTimes()}")
			).Execute();
			return true;
		}

		public static void SetL1Cache(this HttpContext context, bool alwaysUseHTTPs, bool alwaysReturnHTTPs, string portalsHttpURI, string filesHttpURI, Dictionary<string, string> headers, string bodyCacheKey)
		{
			var isDebugLogEnabled = Global.IsDebugLogEnabled || context.ContainsKey("x-logs") || context.ContainsKey("x-cache-logs");
			if (Handler.Cache.UseL1Cache && context.IsL1CacheAvailable(portalsHttpURI))
			{
				var key = context.GetL1CacheKey();
				var info = new L1CacheInfo(alwaysUseHTTPs, alwaysReturnHTTPs, portalsHttpURI, filesHttpURI, headers, bodyCacheKey);
				new CommunicateMessage(Global.ServiceName + ".HTTP.L1Cache")
				{
					ExcludedNodeID = Global.NodeID,
					Type = key,
					Data = info.ToJson()
				}.Send(Router.GotBackupRouter());
				Handler.Cache.SetL1CacheItem(key, info);
				if (isDebugLogEnabled)
					context.WriteLogsAsync("Http.Process.Requests", $"Update L1-Cache successful [{key} => {context.GetRequestUrl()}]\r\nInfo: {info.ToJson()}").Execute();
			}
			else if (isDebugLogEnabled)
				context.WriteLogsAsync("Http.Process.Requests", $"Bypass update L1-Cache [{context.GetL1CacheKey()} => {context.GetRequestUrl()}]").Execute();
		}

		public static void SetL1Cache(this CommunicateMessage message)
		{
			if (!Global.NodeID.IsEquals(message.ExcludedNodeID))
			{
				TimeSpan? validFor = null;
				var key = message.Type;
				var pos = key.IndexOf('#');
				if (pos > 0)
				{
					if (Int32.TryParse(key.Right(key.Length - pos), out var minutes) && minutes > 0)
						validFor = TimeSpan.FromMinutes(minutes);
					key = key.Left(pos);
				}
				if (key.IsValidUUID())
					Handler.Cache.SetL1CacheItem(key, new L1CacheInfo(message.Data), validFor);
				else
					Handler.Cache.SetL1CacheItem(key, message.Data, validFor);
			}
		}

		public static bool RemoveL1Cache(this HttpContext context, string bodyCacheKey)
		{
			var originIsRequired = Handler.CrossOrigin.IsEquals("use-credentials");
			Handler.Cache.RemoveL1CacheItem(context.GetL1CacheKey());
			if (originIsRequired)
				Handler.Cache.RemoveL1CacheItem(context.GetL1CacheKey(false));

			if (bodyCacheKey != null)
			{
				Handler.Cache.RemoveL1CacheItem(bodyCacheKey);
				if (originIsRequired)
					Handler.Cache.RemoveL1CacheItem(bodyCacheKey + ":WWW");
			}
			
			return false;
		}

		public static string GetL1CacheKey(this HttpContext context, bool noneWWW = true)
		{
			var url = context.GetRequestUrl(true);
			url = noneWWW ? url.Replace("//www.", "//") : url;
			url += url.EndsWith('/') ? "index.html" : "";
			return url.GenerateUUID();
		}

		public static void SendSessionState(this RequestInfo requestInfo, JObject systemIdentityJson, string serviceName, string serviceURI, bool trackStatistics)
		{
			if (Handler.TrackSessions)
				requestInfo.SendSessionState(systemIdentityJson, message =>
				{
					message.Data["Crawler"] = requestInfo.IsCrawlerbot();
					var serviceInfo = message.Data.Get<JObject>("Service");
					if (!string.IsNullOrWhiteSpace(serviceName))
						serviceInfo["Name"] = serviceName.ToLower();
					if (!string.IsNullOrWhiteSpace(serviceURI))
						serviceInfo["URI"] = serviceURI;
				}, trackStatistics);
			else if (trackStatistics)
				requestInfo.TrackStatistics();
		}

		public static void SendSessionState(this HttpContext context, string serviceName, string serviceURI, string serviceSystemID, bool online, bool trackStatistics)
		{
			if (Handler.TrackSessions)
				context.GetSession().SendSessionState(serviceName, serviceURI, serviceSystemID, online, trackStatistics, false, message => message.Data["Crawler"] = context.IsCrawlerbot(), null, context.GetCorrelationID());
			else if (trackStatistics)
				context.GetSession().TrackStatistics(context.GetCorrelationID());
		}

		public static void SendSessionState(this HttpContext context, string serviceName, string serviceURI, bool online, bool trackStatistics)
			=> context.SendSessionState(serviceName, serviceURI, null, online, trackStatistics);

		public static async Task SendSessionStateAsync(this HttpContext context, bool online, bool trackStatistics)
		{
			var serviceSystemID = string.Empty;
			var requestURI = context.GetRequestUri();
			var requestURL = requestURI.AbsoluteUri;
			if (Handler.TrackSessions && !requestURL.IsContains("/_css/") && !requestURL.IsContains("/_js/") && !requestURL.IsContains("/_themes/") && !requestURL.IsContains("/_assets/"))
			{
				var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
				{
					["x-url"] = requestURL,
					["x-host"] = context.GetParameter("Host") ?? requestURI.Host,
					["x-requester"] = context.GetParameter("x-requester") ?? "vieapps-ngx-portals"
				};
				var requestInfo = new RequestInfo(context.GetSession(), "Portals", "Identify.System", "GET", null, headers, null, null, context.GetCorrelationID());
				var systemIdentityJson = await context.IdentifySystemAsync(requestInfo, Global.CancellationToken).ConfigureAwait(false);
				serviceSystemID = systemIdentityJson?.Get<string>("ID");
			}
			context.SendSessionState(Global.ServiceName + ".HTTP", $"{context.Request.Method} {requestURL}", serviceSystemID, online, trackStatistics);
		}

		public static string NormalizeHtml(this HttpContext context, string html, bool alwaysUseHTTPs, bool alwaysReturnHTTPs, string baseURL = null)
		{
			var requestURI = context.GetRequestUri();
			var session = context.GetSession();
			var isMobile = string.IsNullOrWhiteSpace(session.AppPlatform) || session.AppPlatform.IsContains("Desktop") ? "false" : "true";
			var osInfo = (session.AppAgent ?? "").GetOSInfo();
			var osPlatform = osInfo.GetANSIUri();
			var osMode = "true".IsEquals(isMobile) ? "mobile-os" : "desktop-os";
			var correlationID = context.GetCorrelationID();
			var hostUUID = requestURI.Host.GenerateUUID();
			var timestamp = DateTime.Now.ToUnixTimestamp();

			html = html.Format(new Dictionary<string, object>
			{
				["isMobile"] = isMobile,
				["is-mobile"] = isMobile,
				["osInfo"] = osInfo,
				["os-info"] = osInfo,
				["osPlatform"] = osPlatform,
				["os-platform"] = osPlatform,
				["osMode"] = osMode,
				["os-mode"] = osMode,
				["device-id"] = session.DeviceID,
				["device-id-base64url"] = session.DeviceID.Url64Encode(),
				["correlationID"] = correlationID,
				["correlation-id"] = correlationID,
				["timestamp"] = timestamp,
				["time-stamp"] = timestamp,
				["host-md5"] = hostUUID,
				["host-uuid"] = hostUUID
			});

			if (!string.IsNullOrWhiteSpace(baseURL))
				html = html.Insert(html.PositionOf(">", html.PositionOf("<head")) + 1, $"<base href=\"{baseURL}\"/>");

			html = html.Replace(StringComparison.OrdinalIgnoreCase, $" src=\"http://", " src=\"//");
			html = html.Replace(StringComparison.OrdinalIgnoreCase, $" src=\"https://", " src=\"//");
			html = html.Replace(StringComparison.OrdinalIgnoreCase, $" srcset=\"http://", " srcset=\"//");
			html = html.Replace(StringComparison.OrdinalIgnoreCase, $" srcset=\"https://", " srcset=\"//");
			html = html.Replace(StringComparison.OrdinalIgnoreCase, $" href=\"http://", " href=\"//");
			html = html.Replace(StringComparison.OrdinalIgnoreCase, $" href=\"https://", " href=\"//");
			html = html.Replace(StringComparison.OrdinalIgnoreCase, $"url(http://", "url(//");
			html = html.Replace(StringComparison.OrdinalIgnoreCase, $"url(https://", "url(//");
			html = html.Replace(StringComparison.OrdinalIgnoreCase, "<link rel=\"canonical\" href=\"//", $"<link rel=\"canonical\" href=\"{(alwaysUseHTTPs || alwaysReturnHTTPs ? "https" : requestURI.Scheme)}://");
			html = html.Replace(StringComparison.OrdinalIgnoreCase, "<link rel=\"prev\" href=\"/", $"<link rel=\"prev\" href=\"{(alwaysUseHTTPs || alwaysReturnHTTPs ? "https" : requestURI.Scheme)}://{requestURI.Host}/");
			html = html.Replace(StringComparison.OrdinalIgnoreCase, "<link rel=\"next\" href=\"/", $"<link rel=\"next\" href=\"{(alwaysUseHTTPs || alwaysReturnHTTPs ? "https" : requestURI.Scheme)}://{requestURI.Host}/");

			return html;
		}

		public static async Task<JObject> IdentifySystemAsync(this HttpContext context, RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			var requestURI = context.GetRequestUri();
			var requestHost = requestURI.Host.Replace("www.", "");
			var useL1Cache = Handler.Cache.UseL1Cache && context.IsL1CacheAvailable();
			var identifyJson = useL1Cache ? Handler.Cache.GetL1CacheItem<JObject>(requestHost) : null;
			if (identifyJson == null)
			{
				identifyJson = await context.CallServiceAsync(requestInfo, cancellationToken, Global.Logger, "Http.Process.Requests").ConfigureAwait(false) as JObject;
				if (identifyJson != null && useL1Cache)
				{
					var updateL1Cache = true;
					var examinations = identifyJson.Get<JArray>("CacheExaminations")?.Select(examination => examination as JObject)
						.Select(examination => examination?.Copy<Settings.ExamineURLs>())
						.Where(examination => examination != null)
						.ToList();
					if (examinations != null && examinations.Count > 0)
					{
						var path = requestURI.AbsolutePath.ToLower();
						var pathWithoutExtention = path.Replace("/default.aspx", "").Replace(".aspx", "").Replace(".php", "").Replace(".html", "");
						updateL1Cache = examinations.FirstOrDefault(exam => exam.Start <= DateTime.Now && exam.End >= DateTime.Now && ((exam.URLs.Any(url => url.IsStartsWith("s:/") ? path.IsStartsWith(url.Right(url.Length - 2)) : url.IsStartsWith("c:/") ? path.IsContains(url.Right(url.Length - 2)) : path.IsEndsWith(url)) || exam.URLs.Any(url => url.IsStartsWith("s:/") ? pathWithoutExtention.IsStartsWith(url.Right(url.Length - 2)) : url.IsStartsWith("c:/") ? pathWithoutExtention.IsContains(url.Right(url.Length - 2)) : pathWithoutExtention.IsEndsWith(url)) || exam.URLs.Any(url => url == "*")))) == null;
					}
					if (updateL1Cache)
					{
						Handler.Cache.SetL1CacheItem(requestHost, identifyJson, TimeSpan.FromMinutes(3));
						new CommunicateMessage(Global.ServiceName + ".HTTP.L1Cache")
						{
							ExcludedNodeID = Global.NodeID,
							Type = requestHost + "#3",
							Data = identifyJson
						}.Send(Router.GotBackupRouter());
						if (Global.IsDebugLogEnabled || context.ContainsKey("x-logs"))
							await context.WriteLogsAsync("Http.Process.Requests", $"Update identify info to L1-Cache successful\r\n- Request: {requestInfo.ToJson()}\r\n- Response: {identifyJson}").ConfigureAwait(false);
					}
				}
			}
			return identifyJson;
		}

		public static Task WriteAsync(this HttpContext context, JToken json, Dictionary<string, string> headers, CancellationToken cancellationToken)
			=> context.WriteAsync(json.ToString(Newtonsoft.Json.Formatting.None), "application/json", new Dictionary<string, string>(headers ?? []) { ["Cache-Control"] = context.GetHttpCacheControl(true) }, cancellationToken);

		public static Task WriteAsync(this HttpContext context, JToken json, CancellationToken cancellationToken)
			=> context.WriteAsync(json, null, cancellationToken);
	}
}
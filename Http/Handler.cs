#region Related components
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
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

		#region Properties
		static HashSet<string> Validators { get; } = "_validator,validator.aspx".ToHashSet();

		static HashSet<string> Initializers { get; } = "_initializer,_activate,initializer.aspx,activate.aspx,initializer.html,activate.html,initializer.php,activate.php".ToHashSet();

		static HashSet<string> LogIns { get; } = "_login,login.aspx,signin.aspx,login.html,signin.html,login.php,signin.php".ToHashSet();

		static HashSet<string> LogOuts { get; } = "_logout,logout.aspx,signout.aspx,logout.html,signout.html,logout.php,signout.php".ToHashSet();

		static HashSet<string> CmsPortals { get; } = "_confirm,_unsubscribe,_image,_visit,_admin,_cms,_edit,_update,admin.aspx,cms.aspx,admin.html,cms.html,admin.php,cms.php".ToHashSet();

		static HashSet<string> Feeds { get; } = "feed,feed.xml,feed.json,atom,atom.xml,atom.json,rss,rss.xml,rss.json".ToHashSet();

		static bool UseShortURLs { get; } = "true".IsEquals(UtilityService.GetAppSetting("Portals:UseShortURLs", "true"));

		static string LoadBalancerHealthCheckURL { get; } = UtilityService.GetAppSetting("LoadBalancer:HealthCheckURL", "/load-balancer-health-check");

		internal static List<string> ExcludedHeaders { get; } = UtilityService.GetAppSetting("ExcludedHeaders", "connection,accept,accept-encoding,accept-language,cache-control,cookie,host,content-type,content-length,user-agent,upgrade-insecure-requests,priority,purpose,pragma,ms-aspnetcore-token,x-forwarded-for,x-forwarded-proto,x-forwarded-port,x-original-for,x-original-proto,x-original-remote-endpoint,x-original-port,cdn-loop").ToList();

		internal static Cache Cache { get; set; }

		internal static IDisposable CacheUpdater { get; set; }

		internal static IDisposable CacheCommunicator { get; set; }

		static bool AllowCache { get; } = "true".IsEquals(UtilityService.GetAppSetting("Portals:Cache:Allow", "true"));

		internal static bool TrackSessions { get; set; } = "true".IsEquals(UtilityService.GetAppSetting("Sessions:Track", "true"));

		internal static bool TrackPortalSessions { get; set; } = Handler.TrackSessions && "true".IsEquals(UtilityService.GetAppSetting("Sessions:Track:Portals", "true"));

		internal static bool TrackAPISessions { get; set; } = Handler.TrackSessions && "true".IsEquals(UtilityService.GetAppSetting("Sessions:Track:APIs", "false"));

		static string CrossOrigin { get; } = "true".IsEquals(UtilityService.GetAppSetting("Portals:Desktops:Resources:CrossOrigin")) ? "use-credentials" : "anonymous";

		internal static string RefresherURL { get; } = UtilityService.GetAppSetting("Portals:RefresherURL", "https://vieapps.net/~url.refresher");

		internal static int ExpiresAfter { get; } = Int32.TryParse(UtilityService.GetAppSetting("Authenticator:TokenExpiresAfter", "0"), out var expiresAfter) && expiresAfter > -1 ? expiresAfter : 0;

		internal static List<string> LegacyParameters { get; } = UtilityService.GetAppSetting("Portals:LegacyParameters", "desktop,catName,contId,page").ToList();

		static string PortalsHttpURI { get; } = UtilityService.GetAppSetting("HttpUri:Portals", "https://portals.vieapps.net");

		static string PortalsHttpHost { get; } = new Uri(Handler.PortalsHttpURI).Host;

		static string PortalsWebSocketURI	{ get; } = UtilityService.GetAppSetting("HttpUri:WebSockets", Handler.PortalsHttpURI);

		static string CMSPortalsHttpURI	{ get; } = UtilityService.GetAppSetting("HttpUri:CMSPortals", "https://cms.vieapps.net");

		static string FilesHttpURI { get; } = UtilityService.GetAppSetting("HttpUri:Files", "https://fs.vieapps.net");
		#endregion

		public Task Invoke(HttpContext context)
		{
			// request of WebSocket
			if (context.WebSockets.IsWebSocketRequest)
				return Task.WhenAll
				(
					Global.IsVisitLogEnabled ? context.WriteLogsAsync(Global.Logger, "APIs", $"Wrap a WebSocket connection successful\r\n- Endpoint: {context.GetRemoteIPAddress()}:{context.Connection.RemotePort}\r\n- URI: {context.GetRequestUri()}{(Global.IsDebugLogEnabled ? $"\r\n- Headers:\r\n\t{context.Request.Headers.Select(kvp => $"{kvp.Key}: {kvp.Value}").Join("\r\n\t")}" : "")}") : Task.CompletedTask,
					APIsHandler.WebSocket.WrapAsync(context)
				);

			// CORS: allow origin
			context.Response.Headers.AccessControlAllowOrigin = "*";

			// CORS: options
			if (context.Request.Method.IsEquals("OPTIONS"))
			{
				var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
				{
					["Access-Control-Allow-Methods"] = "HEAD,GET,POST,PUT,PATCH"
				};
				if (context.Request.Headers.TryGetValue("Access-Control-Request-Headers", out var requestHeaders))
					headers["Access-Control-Allow-Headers"] = requestHeaders;
				context.SetResponseHeaders((int)HttpStatusCode.OK, headers);
				return Task.CompletedTask;
			}

			// health check
			if (context.Request.Path.Value.IsEquals(Handler.LoadBalancerHealthCheckURL))
				return context.WriteAsync("OK", "text/plain", null, 0, null, TimeSpan.Zero, null, Global.CancellationToken);

			// requests of the service
			if (context.IsBlackIP(context.GetRemoteIPAddress()))
			{
				context.SetResponseHeaders((int)HttpStatusCode.Forbidden);
				return Task.CompletedTask;
			}
			return this.ProcessHttpRequestAsync(context);
		}

		async Task ProcessHttpRequestAsync(HttpContext context)
		{
			// prepare
			context.SetItem("PipelineStopwatch", Stopwatch.StartNew());
			var requestURI = context.GetRequestUri();
			var requestSegments = requestURI.GetRequestPathSegments();
			var requestPath = requestSegments.First().ToLower();

			if (Global.IsVisitLogEnabled)
				await context.WriteVisitStartingLogAsync().ConfigureAwait(false);

			// request to favicon.ico file
			if (requestPath.IsEquals("favicon.ico") && requestURI.Host.IsEquals(Handler.PortalsHttpHost))
				await context.ProcessFavouritesIconFileRequestAsync().ConfigureAwait(false);

			// request to static segments
			else if (Global.StaticSegments.Contains(requestPath))
				await context.ProcessStaticFileRequestAsync().ConfigureAwait(false);

			// request to APIs/MCP discovery
			else if (".well-known".IsEquals(requestPath))
			{
				if (requestSegments.Length > 1 && requestSegments[1].IsEquals("mcp.json"))
				{

				}
				else
					await context.ProcessAPIsRequestAsync(requestSegments).ConfigureAwait(false);
			}

			// request to portal desktops/resources
			else
				await this.ProcessPortalRequestAsync(context).ConfigureAwait(false);

			if (Global.IsVisitLogEnabled)
				await context.WriteVisitFinishingLogAsync().ConfigureAwait(false);
		}

		async Task ProcessPortalRequestAsync(HttpContext context)
		{
			// prepare
			var stopwatch = Stopwatch.StartNew();
			var correlationID = context.GetCorrelationID();
			var isDebugLogEnabled = Global.IsDebugLogEnabled || context.ContainsKey("x-logs") || context.ContainsKey("x-cache-logs");

			var stepwatch = Stopwatch.StartNew();
			var session = context.Session.Get<Session>("Session") ?? context.GetSession();
			session.NormalizeSession(context);

			var requestURI = context.GetRequestUri();
			var requestMethod = (context.Request.Method ?? "GET").ToUpper();
			if (isDebugLogEnabled || Global.IsVisitLogEnabled)
				await context.WriteLogsAsync("Http.Process.Requests", $"Start process a request of CMS Portals [{requestMethod}: {requestURI}]").ConfigureAwait(false);

			// process L1-Cache first
			if (await this.ProcessL1CacheAsync(context, stopwatch).ConfigureAwait(false))
				return;

			// gathering the requesting information
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
				var requestSegments = pathSegments.Skip(0).ToArray();

				// special parameters (like spider indicator (robots.txt)/ads indicator (ads.txt) or system/organization identity)
				if (!string.IsNullOrWhiteSpace(firstPathSegment))
				{
					// system/oranization identity
					if (firstPathSegment.StartsWith("~"))
					{
						requestSegments = pathSegments.Skip(1).ToArray();
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
						requestSegments = Array.Empty<string>();
					}
				}

				// normalize info of requests
				if (requestSegments.Length > 0 && specialRequest == "")
				{
					var firstRequestSegment = requestSegments.First().ToLower();

					// special requests
					if (Handler.Initializers.Contains(firstRequestSegment))
					{
						specialRequest = "initializer";
						requestSegments = Array.Empty<string>();
					}

					else if (Handler.Validators.Contains(firstRequestSegment))
					{
						specialRequest = "validator";
						requestSegments = Array.Empty<string>();
					}

					else if (Handler.LogIns.Contains(firstRequestSegment))
					{
						specialRequest = "login";
						requestSegments = Array.Empty<string>();
					}

					else if (Handler.LogOuts.Contains(firstRequestSegment))
					{
						specialRequest = "logout";
						requestSegments = Array.Empty<string>();
					}

					else if (Handler.Feeds.Contains(firstRequestSegment))
					{
						specialRequest = "feed";
						requestSegments = Array.Empty<string>();
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
						requestSegments = Array.Empty<string>();
					}

					// indicators
					else if (firstPathSegment.IsEndsWith(".txt") || firstPathSegment.IsEndsWith(".xml") || firstPathSegment.IsEndsWith(".json") || firstPathSegment.IsEquals("favicon.ico"))
					{
						systemIdentity = "~indicators";
						queryString["x-indicator"] = firstPathSegment;
						requestSegments = Array.Empty<string>();
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

						value = requestSegments.Length > 1 && !string.IsNullOrWhiteSpace(requestSegments[1]) ? requestSegments[1].Replace(StringComparison.OrdinalIgnoreCase, ".html", "").Replace(StringComparison.OrdinalIgnoreCase, ".aspx", "").Replace(StringComparison.OrdinalIgnoreCase, ".php", "") : null;
						queryString["x-parent"] = string.IsNullOrWhiteSpace(value) ? null : value.GetANSIUri();

						if (requestSegments.Length > 2 && !string.IsNullOrWhiteSpace(requestSegments[2]))
						{
							value = requestSegments[2].Replace(StringComparison.OrdinalIgnoreCase, ".html", "").Replace(StringComparison.OrdinalIgnoreCase, ".aspx", "").Replace(StringComparison.OrdinalIgnoreCase, ".php", "");
							if (value.IsNumeric())
								queryString["x-page"] = value;
							else
								queryString["x-content"] = value.GetANSIUri();

							if (requestSegments.Length > 3 && !string.IsNullOrWhiteSpace(requestSegments[3]))
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

			// check request method (HTTP Verb)
			if (!requestMethod.IsEquals("GET") && !specialRequest.IsEquals("login"))
				throw context.MonitorHarmfulRequest(context.GetRemoteIPAddress().ToString(), Global.NodeID, Global.ServiceName);

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
			var requestInfo = new RequestInfo(session, "Portals", "Identify.System", "GET", query, headers, null, extra, correlationID);
			if ("".Equals(systemIdentity))
				await context.WriteLogsAsync("Http.Process.Requests", $"Identify the request [Prev step: {stepwatch.GetElapsedTimes()}]{(isDebugLogEnabled ? $"\r\n- App: {session.AppName} [{session.AppPlatform} @ {session.AppAgent}]\r\n- Request: {requestInfo.ToString(Formatting.Indented)}" : "")}").ConfigureAwait(false);

			session.UpdateSessionCookie(context, true);
			stepwatch.Restart();

			JObject systemIdentityJson = null;
			var alwaysUseHTTPs = false;
			var alwaysReturnHTTPs = false;
			var redirectToNoneWWW = false;

			if (string.IsNullOrWhiteSpace(specialRequest))
				try
				{
					// call the Portals service to identify the system
					using var cts = CancellationTokenSource.CreateLinkedTokenSource(Global.CancellationToken, context.RequestAborted);
					if (!"~resources".IsEquals(systemIdentity))
					{
						systemIdentityJson = await this.IdentifySystemAsync(context, requestInfo, cts.Token).ConfigureAwait(false);
						requestInfo.Query["x-system"] = systemIdentityJson.Get<string>("Alias");
						alwaysUseHTTPs = systemIdentityJson.Get("AlwaysUseHTTPs", false);
						alwaysReturnHTTPs = systemIdentityJson.Get("AlwaysReturnHTTPs", false);
						redirectToNoneWWW = systemIdentityJson.Get("RedirectToNoneWWW", false);
						if (stepwatch.Elapsed.TotalMilliseconds > 50)
							await context.WriteLogsAsync("Http.Process.Requests", $"The identify process was completed in {stepwatch.GetElapsedTimes()} [{systemIdentity}]").ConfigureAwait(false);
					}

					// request of legacy system (files and medias)
					if (!string.IsNullOrWhiteSpace(legacyRequest))
					{
						var requestSegments = legacyRequest.ToArray("/").ToList();
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
								await context.WriteLogsAsync("Http.Process.Requests", $"Redirect for matching with the settings\r\n{requestURI} => {redirectURL}").ConfigureAwait(false);
							return;
						}
					}

					// requester is NGX-Refresher
					var isRefresher = Handler.RefresherURL.IsEquals(context.GetReferUrl());

					// session state
					if (!isRefresher && !"~resources".IsEquals(systemIdentity) && !"~indicators".IsEquals(systemIdentity))
						requestInfo.SendSessionState(systemIdentityJson, $"{Global.ServiceName}.HTTP", $"{requestMethod} {requestURI.AbsoluteUri}", Handler.TrackPortalSessions);

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
					var processCache = requestInfo.ContainsKey("x-force-cache") || requestInfo.ContainsKey("x-no-cache") || requestInfo.ContainsKey("x-bypass-cache")
						? false
						: Handler.AllowCache;
					if (processCache && !isRefresher)
					{
						var cacheKey = "";
						var eTag = "";
						var contentType = "text/html";
						var expires = DateTime.Now.AddMinutes(13);
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
							var logstep = "".Equals(systemIdentity) && systemIdentityJson == null;
							systemIdentityJson ??= await this.IdentifySystemAsync(context, requestInfo, cts.Token).ConfigureAwait(false);

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

							if (logstep)
								await context.WriteLogsAsync("Http.Process.Requests", $"The identify process was completed in {stepwatch.GetElapsedTimes()}").ConfigureAwait(false);
						}

						if ("".Equals(systemIdentity))
							await context.WriteLogsAsync("Http.Process.Requests", $"The request was identified [Prev step: {stepwatch.GetElapsedTimes()}]").ConfigureAwait(false);
						stepwatch.Restart();

						if (!string.IsNullOrWhiteSpace(cacheKey))
						{
							// redirect (HTTPS or None-WWW)
							if (contentType.IsEquals("text/html") && ((alwaysUseHTTPs && !requestURI.Scheme.IsEquals("https")) || (redirectToNoneWWW && requestURI.Host.IsStartsWith("www."))))
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
									await context.WriteLogsAsync("Http.Process.Requests", $"Redirect for matching with the settings\r\n{requestURI} => {redirectURL}").ConfigureAwait(false);
								return;
							}

							// process cache							
							if (isDebugLogEnabled || Global.IsVisitLogEnabled)
								await context.WriteLogsAsync("Http.Process.Requests", $"Attempt to process the CMS Portals service cache => {requestURI} ({cacheKey})").ConfigureAwait(false);

							headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
							{
								["Content-Type"] = $"{contentType}; charset=utf-8",
								["ETag"] = eTag,
								["Cache-Control"] = "public",
								["X-Cache"] = "HTTP-200",
								["X-Node"] = Global.NodeID,
								["X-Correlation-ID"] = correlationID
							};

							var allowOrigin = "*";
							if (!contentType.IsStartsWith("text/html") && !contentType.IsStartsWith("font/") && !contentType.IsStartsWith("image/") && Handler.CrossOrigin.IsEquals("use-credentials"))
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
								context.SetResponseHeaders((int)HttpStatusCode.NotModified, headers);

								if (examinations == null || !examinations.Any(exam => exam.Start >= DateTime.Now && exam.End <= DateTime.Now))
									this.SetL1Cache(context, alwaysUseHTTPs, alwaysReturnHTTPs, baseURL, rootURL, portalsHttpURI, filesHttpURI, headers, cacheKey);

								if (isDebugLogEnabled || Global.IsVisitLogEnabled)
									await context.WriteLogsAsync("Http.Process.Requests", $"Process the CMS Portals service cache was done => NOT MODIFIED ({eTag}/{lastModified}) - Execution times: {stepwatch.GetElapsedTimes()} of {stopwatch.GetElapsedTimes()}").ConfigureAwait(false);
								return;
							}

							// cached data
							stepwatch.Restart();
							var cached = await Handler.Cache.GetAsync<string>(cacheKey, cts.Token).ConfigureAwait(false);

							if (!string.IsNullOrWhiteSpace(cached))
							{
								var isBase64 = contentType.IsStartsWith("image/") || contentType.IsStartsWith("font/");
								var isHtml = !isBase64 && contentType.IsEquals("text/html");
								var expiresAt = isHtml ? await Handler.Cache.GetAsync<string>($"{cacheKey}:expiration", cts.Token).ConfigureAwait(false) : null;
								lastModified = lastModified ?? await Handler.Cache.GetAsync<string>($"{cacheKey}:time", cts.Token).ConfigureAwait(false) ?? DateTime.Now.ToHttpString();

								if (context.ContainsKey("x-sliding-cache"))
								{
									var items = new Dictionary<string, string>
									{
										[cacheKey] = cached,
										[$"{cacheKey}:time"] = lastModified
									};
									if (expiresAt != null && DateTime.TryParse(expiresAt, out var expiresAtTime))
									{
										items[$"{cacheKey}:expiration"] = expiresAtTime.AddMinutes(13).ToDTString();
										Handler.Cache.SetAsync(items, null, expiresAtTime.AddMinutes(13), Global.CancellationToken).Execute();
									}
									else
										Handler.Cache.SetAsync(items, null, 0, Global.CancellationToken).Execute();
								}

								var isCacheLogEnabled = !isBase64 && (isDebugLogEnabled || context.ContainsKey("x-cache-logs"));
								if (isCacheLogEnabled)
									await context.WriteLogsAsync("Http.Process.Requests", $"CMS Portals service cache was found ({cacheKey})\r\n\r\nRaw cache:\r\n{cached}").ConfigureAwait(false);

								headers["Last-Modified"] = lastModified;
								headers["Expires"] = (string.IsNullOrWhiteSpace(expiresAt) || !DateTime.TryParse(expiresAt, out var expirationTime) ? expires : expirationTime).ToHttpString();
								context.SetResponseHeaders((int)HttpStatusCode.OK, headers);

								cached = isBase64 ? cached : cached.Replace("~#/", $"{portalsHttpURI}/").Replace("~~~/", $"{portalsHttpURI}/").Replace("~~/", $"{filesHttpURI}/").Replace("~/", rootURL);
								cached = isHtml ? context.NormalizeHtml(cached, alwaysUseHTTPs, alwaysReturnHTTPs, baseURL) : cached;
								await context.WriteAsync(isBase64 ? cached.Base64ToBytes() : cached.ToBytes(), cts.Token).ConfigureAwait(false);

								if (examinations == null || !examinations.Any(exam => exam.Start >= DateTime.Now && exam.End <= DateTime.Now))
									this.SetL1Cache(context, alwaysUseHTTPs, alwaysReturnHTTPs, baseURL, rootURL, portalsHttpURI, filesHttpURI, headers, cacheKey);

								stepwatch.Stop();
								if (isDebugLogEnabled || Global.IsVisitLogEnabled)
									await context.WriteLogsAsync("Http.Process.Requests", $"Process the CMS Portals service cache was done => FOUND ({cacheKey}) - Execution times: {stepwatch.GetElapsedTimes()} of {stopwatch.GetElapsedTimes()}{(isCacheLogEnabled ? $"\r\n\r\nNormalized cache:\r\n{cached}" : "")}").ConfigureAwait(false);
								return;
							}
						}
					}

					// call CMS Portals service to process the request
					stepwatch.Restart();
					Handler.Cache.RemoveL1CacheItem(requestURI.GetUrl().GenerateUUID());

					try
					{
						requestInfo = new RequestInfo(requestInfo) { ObjectName = "Process.Http.Request" };
						if (isDebugLogEnabled)
							await context.WriteLogsAsync("Http.Process.Requests", $"Call the service to process the request\r\n- App: {session.AppName} [{session.AppPlatform} @ {session.AppAgent}]\r\n- Request: {requestInfo.ToString(Formatting.Indented)}").ConfigureAwait(false);
						var response = (await context.CallServiceAsync(requestInfo, cts.Token, Global.Logger, "Http.Process.Requests").ConfigureAwait(false)).ToExpandoObject();
						
						headers = response.Get("Headers", new Dictionary<string, string>());
						if (headers.TryGetValue("X-Node", out var nodeID))
							headers["X-Service-Node"] = nodeID;
						headers = new Dictionary<string, string>(headers, StringComparer.OrdinalIgnoreCase)
						{
							["X-Correlation-ID"] = correlationID,
							["X-Node"]  = Global.NodeID
						};
						context.SetResponseHeaders(response.Get("StatusCode", (int)HttpStatusCode.OK), headers);

						var body = response.Get<string>("Body");
						if (body != null)
							await context.WriteAsync(body.Base64ToBytes().Decompress(response.Get("BodyEncoding", "zstd")), cts.Token).ConfigureAwait(false);

						if (examinations == null || !examinations.Any(exam => exam.Start >= DateTime.Now && exam.End <= DateTime.Now))
						{
							var baseURL = "";
							var rootURL = "/";
							var filesHttpURI = this.RemoveURITrail(systemIdentityJson.Get<string>("FilesHttpURI") ?? Handler.FilesHttpURI);
							var portalsHttpURI = this.RemoveURITrail(systemIdentityJson.Get<string>("PortalsHttpURI") ?? Handler.PortalsHttpURI);
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
							this.SetL1Cache(context, alwaysUseHTTPs, alwaysReturnHTTPs, baseURL, rootURL, portalsHttpURI, filesHttpURI, headers, systemIdentityJson.Get<string>("CacheKeyPrefix") + ":" + path.GenerateUUID());
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

			else
				switch (specialRequest)
				{
					case "initializer":
						if (context.Request.Path.Value.IsEndsWith(".aspx") || context.Request.Path.Value.IsEndsWith(".html") || context.Request.Path.Value.IsEndsWith(".php"))
							systemIdentityJson ??= await this.IdentifySystemAsync(context, requestInfo, Global.CancellationToken).ConfigureAwait(false);
						await this.ProcessInitializerRequestAsync(context, systemIdentityJson).ConfigureAwait(false);
						break;

					case "validator":
						await this.ProcessValidatorRequestAsync(context).ConfigureAwait(false);
						break;

					case "login":
						if (!context.Request.Method.IsEquals("GET") || context.Request.Path.Value.IsEndsWith(".aspx") || context.Request.Path.Value.IsEndsWith(".html") || context.Request.Path.Value.IsEndsWith(".php"))
							systemIdentityJson ??= await this.IdentifySystemAsync(context, requestInfo, Global.CancellationToken).ConfigureAwait(false);
						await this.ProcessLogInRequestAsync(context, systemIdentityJson).ConfigureAwait(false);
						break;

					case "logout":
						if (context.Request.Path.Value.IsEndsWith(".aspx") || context.Request.Path.Value.IsEndsWith(".html") || context.Request.Path.Value.IsEndsWith(".php"))
							systemIdentityJson ??= await this.IdentifySystemAsync(context, requestInfo, Global.CancellationToken).ConfigureAwait(false);
						await this.ProcessLogOutRequestAsync(context, systemIdentityJson).ConfigureAwait(false);
						break;

					case "cms":
						try
						{
							systemIdentityJson ??= await this.IdentifySystemAsync(context, requestInfo, Global.CancellationToken).ConfigureAwait(false);
							await this.ProcessCmsPortalsRequestAsync(context, systemIdentityJson?.Get<string>("ID"), systemIdentityJson?.Get<string>("ObjectID"), systemIdentityJson?.Get<string>("RepositoryEntityID") ?? systemIdentityJson?.Get<string>("ObjectName"), systemIdentityJson?.Get<string>("Location"), systemIdentityJson?.Get<string>("TrackingContentType"), systemIdentityJson?.Get<string>("TrackingBody"), systemIdentityJson?.Get<string>("TrackingBodyEncoding"), systemIdentityJson?.Get<string>("TrackingCacheControl")).ConfigureAwait(false);
						}
						catch (OperationCanceledException) { }
						catch (Exception ex)
						{
							if (ex is WampException wampException)
							{
								var wampDetails = wampException.GetDetails(requestInfo);
								context.ShowError(wampDetails.Code, wampDetails.Message, wampDetails.Type, correlationID, wampDetails.Stack + "\r\n\t" + ex.StackTrace, isDebugLogEnabled);
							}
							else
								context.ShowError(ex.GetHttpStatusCode(), ex.Message, ex.GetTypeName(true), correlationID, ex, isDebugLogEnabled);
							await context.WriteLogsAsync("Http.Process.Requests", $"Error occurred while processing with CMS Portals => {ex.Message}", ex).ConfigureAwait(false);
						}
						break;

					case "feed":
						try
						{
							using var cts = CancellationTokenSource.CreateLinkedTokenSource(Global.CancellationToken, context.RequestAborted);
							systemIdentityJson ??= await this.IdentifySystemAsync(context, requestInfo, cts.Token).ConfigureAwait(false);
							requestInfo = new RequestInfo(requestInfo) { ObjectName = "Generate.Feed" };
							requestInfo.Query["x-system"] = systemIdentityJson.Get<string>("Alias");

							requestInfo.SendSessionState(systemIdentityJson, $"{Global.ServiceName}.HTTP", $"{requestMethod} {requestURI.AbsoluteUri}", Handler.TrackPortalSessions);
							if (isDebugLogEnabled)
								await context.WriteLogsAsync("Http.Process.Requests", $"Call the service to generate feeds\r\n- App: {session.AppName} [{session.AppPlatform} @ {session.AppAgent}]\r\n- Request: {requestInfo.ToString(Formatting.Indented)}").ConfigureAwait(false);

							var response = (await context.CallServiceAsync(requestInfo, cts.Token, Global.Logger, "Http.Process.Requests").ConfigureAwait(false)).ToExpandoObject();

							headers = response.Get("Headers", new Dictionary<string, string>());
							if (headers.TryGetValue("X-Node", out var nodeID))
								headers["X-Service-Node"] = nodeID;
							headers = new Dictionary<string, string>(headers, StringComparer.OrdinalIgnoreCase)
							{
								["X-Correlation-ID"] = correlationID,
								["X-Node"] = Global.NodeID
							};
							context.SetResponseHeaders(response.Get("StatusCode", (int)HttpStatusCode.OK), headers);

							var body = response.Get<string>("Body");							
							if (body != null)
								await context.WriteAsync(body.Base64ToBytes().Decompress(response.Get("BodyEncoding", "zstd")), cts.Token).ConfigureAwait(false);
						}
						catch (OperationCanceledException) { }
						catch (Exception ex)
						{
							if (ex is WampException wampException)
							{
								var wampDetails = wampException.GetDetails(requestInfo);
								context.ShowError(wampDetails.Code, wampDetails.Message, wampDetails.Type, correlationID, wampDetails.Stack + "\r\n\t" + ex.StackTrace, isDebugLogEnabled);
							}
							else
								context.ShowError(ex.GetHttpStatusCode(), ex.Message, ex.GetTypeName(true), correlationID, ex, isDebugLogEnabled);
							await context.WriteLogsAsync("Http.Process.Requests", $"Error occurred while processing feeds => {ex.Message}", ex).ConfigureAwait(false);
						}
						break;

					default:
						var invalidException = new InvalidRequestException();
						context.ShowError(invalidException.GetHttpStatusCode(), invalidException.Message, invalidException.GetType().GetTypeName(true), correlationID, invalidException, isDebugLogEnabled);
						break;
				}

			stopwatch.Stop();
			if (isDebugLogEnabled || Global.IsVisitLogEnabled)
				await context.WriteLogsAsync("Http.Process.Requests", $"Done process a request of CMS Portals - Execution times: {stopwatch.GetElapsedTimes()}").ConfigureAwait(false);
		}

		async Task<bool> ProcessL1CacheAsync(HttpContext context, Stopwatch stopwatch)
		{
			if (!Handler.Cache.UseL1Cache || context.ContainsKey("x-force-cache") || context.ContainsKey("x-no-cache") || context.ContainsKey("x-bypass-cache") || context.ContainsKey("x-sliding-cache"))
				return false;

			var requestURI = context.GetRequestUri();
			var key = requestURI.GetUrl().GenerateUUID();
			var meta = Handler.Cache.GetL1CacheItem<JObject>(key);

			if (meta == null)
				return false;

			var statusCode = (int)HttpStatusCode.OK;
			var headers = new Dictionary<string, string>(meta.Get<JObject>("Headers").ToDictionary<string>(), StringComparer.OrdinalIgnoreCase)
			{
				["X-Cache"] = "L1-HTTP-200",
				["X-Node"] = Global.NodeID,
				["X-Correlation-ID"] = context.GetCorrelationID()
			};

			headers.TryGetValue("Expires", out var expiresAt);
			if (!string.IsNullOrWhiteSpace(expiresAt) && DateTime.TryParse(expiresAt, out var expirationTime) && expirationTime < DateTime.Now)
			{
				Handler.Cache.RemoveL1CacheItem(key);
				return false;
			}

			headers.TryGetValue("ETag", out var eTag);
			headers.TryGetValue("Content-Type", out var contentType);
			headers.TryGetValue("Last-Modified", out var lastModified);
			if (eTag == null || contentType == null || lastModified == null)
			{
				Handler.Cache.RemoveL1CacheItem(key);
				return false;
			}

			var gotBody = true;
			var modifiedSince = context.GetHeaderParameter("If-Modified-Since") ?? context.GetHeaderParameter("If-Unmodified-Since");
			if (modifiedSince != null && modifiedSince.FromHttpDateTime() >= lastModified.FromHttpDateTime() && eTag.IsEquals(context.GetHeaderParameter("If-None-Match")))
			{
				statusCode = (int)HttpStatusCode.NotModified;
				headers["X-Cache"] = "L1-HTTP-304";
				gotBody = false;
			}

			var body = gotBody ? Handler.Cache.GetL1CacheItem<string>(meta.Get<string>("BodyCacheKey")) : null;
			if (gotBody && body == null)
			{
				Handler.Cache.RemoveL1CacheItem(key);
				return false;
			}

			if (!contentType.IsStartsWith("text/html") && !contentType.IsStartsWith("font/") && !contentType.IsStartsWith("image/") && Handler.CrossOrigin.IsEquals("use-credentials"))
			{
				headers.TryGetValue("Access-Control-Allow-Origin", out var allowOrigin);
				var origin = context.GetHeaderParameter("Origin") ?? context.GetHeaderParameter("Referer");
				if (!string.IsNullOrWhiteSpace(origin))
				{
					var originURI = new Uri(origin);
					allowOrigin = $"{originURI.Scheme}://{originURI.Host}";
				}
				headers["Access-Control-Allow-Origin"] = allowOrigin ?? "*";
			}

			context.SetResponseHeaders(statusCode, headers);
			if (body != null)
			{
				var isBase64 = contentType.IsStartsWith("image/") || contentType.IsStartsWith("font/");
				var isHtml = !isBase64 && contentType.IsStartsWith("text/html");
				if (!isBase64)
				{
					body = body.Replace("~#/", $"{meta.Get<string>("PortalsURL")}/").Replace("~~~/", $"{meta.Get<string>("PortalsURL")}/").Replace("~~/", $"{meta.Get<string>("FilesURL")}/").Replace("~/", meta.Get<string>("RootURL"));
					body = context.NormalizeHtml(body, meta.Get<bool>("AlwaysUseHTTPs"), meta.Get<bool>("AlwaysReturnHTTPs"), isHtml ? meta.Get<string>("BaseURL") : null);
				}
				using var cts = CancellationTokenSource.CreateLinkedTokenSource(Global.CancellationToken, context.RequestAborted);
				await context.WriteAsync(isBase64 ? body.Base64ToBytes() : body.ToBytes(), cts.Token).ConfigureAwait(false);
			}

			if (Handler.TrackSessions)
				context.GetSession().SendSessionState(Global.ServiceName.ToLower(), $"GET {requestURI}");
			else
				context.GetSession().TrackStatistics(context.GetCorrelationID());

			stopwatch.Stop();
			await context.WriteLogsAsync("Http.Process.Requests", $"Process the L1-Cache of CMS Portals HTTP service was done - Execution times: {stopwatch.GetElapsedTimes()}").ConfigureAwait(false);
			return true;
		}

		void SetL1Cache(HttpContext context, bool alwaysUseHTTPs, bool alwaysReturnHTTPs, string baseURL, string rootURL, string portalsHttpURI, string filesHttpURI, Dictionary<string, string> headers, string cacheKey)
		{
			if (!Handler.Cache.UseL1Cache)
				return;

			var id = context.GetRequestUri().GetUrl().GenerateUUID();
			var data = new JObject
			{
				["AlwaysUseHTTPs"] = alwaysUseHTTPs,
				["AlwaysReturnHTTPs"] = alwaysReturnHTTPs,
				["BaseURL"] = baseURL,
				["RootURL"] = rootURL,
				["PortalsURL"] = portalsHttpURI,
				["FilesURL"] = filesHttpURI,
				["Headers"] = headers.ToJObject(),
				["BodyCacheKey"] = cacheKey
			};

			Handler.Cache.SetL1CacheItem(id, data);
			new CommunicateMessage($"{Global.ServiceName}.HTTP.L1Cache")
			{
				ExcludedNodeID = Global.NodeID,
				Type = id,
				Data = data
			}.Send(Router.GotBackupRouter());
		}

		async Task<JObject> IdentifySystemAsync(HttpContext context, RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			var requestURI = context.GetRequestUri();
			var requestHost = requestURI.Host.Replace("www.", "");
			var identifyJson = Handler.Cache.UseL1Cache
				? Handler.Cache.GetL1CacheItem<JObject>(requestHost)
				: null;

			if (identifyJson == null)
			{
				identifyJson = await context.CallServiceAsync(requestInfo, cancellationToken, Global.Logger, "Http.Process.Requests").ConfigureAwait(false) as JObject;
				if (identifyJson != null && Handler.Cache.UseL1Cache)
				{
					var examinations = identifyJson.Get<JArray>("CacheExaminations")?.Select(examination => examination as JObject)					
						.Select(examination => examination?.Copy<Settings.ExamineURLs>())
						.Where(examination => examination != null)
						.ToList();
					if (examinations != null && examinations.Count > 0)
					{
						var path = requestURI.AbsolutePath.ToLower();
						var pathWithoutExtention = path.Replace("/default.aspx", "").Replace(".aspx", "").Replace(".php", "").Replace(".html", "");
						var examination = examinations.FirstOrDefault(exam => exam.Start <= DateTime.Now && exam.End >= DateTime.Now && ((exam.URLs.Any(url => url.IsStartsWith("s:/") ? path.IsStartsWith(url.Right(url.Length - 2)) : url.IsStartsWith("c:/") ? path.IsContains(url.Right(url.Length - 2)) : path.IsEndsWith(url)) || exam.URLs.Any(url => url.IsStartsWith("s:/") ? pathWithoutExtention.IsStartsWith(url.Right(url.Length - 2)) : url.IsStartsWith("c:/") ? pathWithoutExtention.IsContains(url.Right(url.Length - 2)) : pathWithoutExtention.IsEndsWith(url)) || exam.URLs.Any(url => url == "*"))));
						if (examination == null)
						{
							Handler.Cache.SetL1CacheItem(requestHost, identifyJson, TimeSpan.FromMinutes(3));
							new CommunicateMessage($"{Global.ServiceName}.HTTP.L1Cache")
							{
								ExcludedNodeID = Global.NodeID,
								Type = $"{requestHost}#3",
								Data = identifyJson
							}.Send(Router.GotBackupRouter());
						}
					}
				}
			}

			return identifyJson;
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

		async Task ProcessLogInRequestAsync(HttpContext context, JObject systemIdentityJson)
		{
			var correlationID = context.GetCorrelationID();
			var isUserInteract = context.Request.Path.Value.IsEndsWith(".aspx") || context.Request.Path.Value.IsEndsWith(".html") || context.Request.Path.Value.IsEndsWith(".php");

			async Task registerAsync()
			{
				try
				{
					var session = context.Session.Get<Session>("Session") ?? context.GetSession();
					session.DeviceID = string.IsNullOrWhiteSpace(session.DeviceID) ? $"{UtilityService.NewUUID}@vieapps-ngx" : session.DeviceID;
					session.SessionID = session.User.SessionID = !string.IsNullOrWhiteSpace(session.User.SessionID)
						? session.User.SessionID
						: !string.IsNullOrWhiteSpace(session.SessionID)
							? session.SessionID
							: UtilityService.NewUUID;
					context.Session.Add("Session", session);
					context.SetSession(session);

					if (Handler.TrackSessions)
						session.SendSessionState("Users", "POST /session", null, true, Handler.TrackAPISessions, false);

					using var cts = CancellationTokenSource.CreateLinkedTokenSource(Global.CancellationToken, context.RequestAborted);
					var body = session.GetSessionBody().ToString(Formatting.None);
					var response = await context.CallServiceAsync(new RequestInfo(session, "Users", "Session", "POST")
					{
						Body = body,
						Extra = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
						{
							{ "Signature", body.GetHMACSHA256(Global.ValidationKey) }
						},
						CorrelationID = correlationID
					}, cts.Token, Global.Logger, "Http.Process.Requests").ConfigureAwait(false);
					await Task.WhenAll
					(
						context.WriteAsync(session.GetSessionJson(), Formatting.Indented, correlationID, cts.Token),
						Global.IsDebugLogEnabled ? context.WriteLogsAsync(Global.Logger, "Http.Process.Requests", $"Successfully register a new session {response}") : Task.CompletedTask
					).ConfigureAwait(false);
				}
				catch (Exception ex)
				{
					context.WriteError(Global.Logger, ex, null, $"Error occurred while registering a new session => {ex.Message}", true, "Http.Process.Requests");
				}
			}

			async Task showAsync()
			{
				var scripts = @"<script>
				window.__prepare = window.__prepare || function(){};
				__prepare();
				</script>";
				using var cts = CancellationTokenSource.CreateLinkedTokenSource(Global.CancellationToken, context.RequestAborted);
				await context.WriteAsync(this.GetSpecialHtml(context, systemIdentityJson).Replace("[[placeholder]]", scripts.Replace("\t\t\t\t", "")), "text/html", null, 0, "private, no-store, no-cache", TimeSpan.Zero, correlationID, cts.Token).ConfigureAwait(false);
			}

			async Task loginAsync()
			{
				try
				{
					// prepare
					var session = context.Session.Get<Session>("Session");
					if (session == null || !session.GetEncryptedID().IsEquals(context.Request.Query["x-session-id"]) || !session.DeviceID.Url64Encode().IsEquals(context.Request.Query["x-device-id"]))
						throw new InvalidSessionException("Session is invalid (The session is not issued by the system)");

					if (Handler.TrackSessions)
						session.SendSessionState("Users", "PUT /session", null, true, Handler.TrackAPISessions, false);

					var request = (await context.ReadTextAsync(Global.CancellationToken).ConfigureAwait(false)).ToExpandoObject();
					var account = Global.RSA.Decrypt(request.Get("Account", "")).Trim().ToLower();
					var password = Global.RSA.Decrypt(request.Get("Password", ""));
					if (string.IsNullOrWhiteSpace(account) || string.IsNullOrWhiteSpace(password))
						throw new WrongAccountException();

					// call service to login
					using var cts = CancellationTokenSource.CreateLinkedTokenSource(Global.CancellationToken, context.RequestAborted);
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
					}, cts.Token, Global.Logger, "Http.Process.Requests").ConfigureAwait(false);

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
					{
						// update session
						session.User = response.Copy<User>();
						session.SessionID = session.User.SessionID = UtilityService.NewUUID;
						session.IP = $"{context.Connection.RemoteIpAddress}";

						body = session.GetSessionBody().ToString(Formatting.None);
						await context.CallServiceAsync(new RequestInfo(session, "Users", "Session", "POST")
						{
							Body = body,
							Extra = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
							{
								{ "Signature", body.GetHMACSHA256(Global.ValidationKey) }
							},
							CorrelationID = correlationID
						}, cts.Token, Global.Logger, "Http.Process.Requests").ConfigureAwait(false);

						// update authenticate ticket
						var userPrincipal = new UserPrincipal(new UserIdentity(session.User.ID, session.SessionID, CookieAuthenticationDefaults.AuthenticationScheme));
						await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, userPrincipal, new AuthenticationProperties { IsPersistent = false }).ConfigureAwait(false);
						context.Session.Add("Session", session);

						response = session.GetSessionJson(payload => payload["did"] = session.DeviceID);
					}

					// response
					await Task.WhenAll
					(
						Global.Cache.RemoveAsync($"Attempt#{context.Connection.RemoteIpAddress}", cts.Token),
						context.WriteAsync(response, Formatting.Indented, correlationID, cts.Token),
						Global.IsDebugLogEnabled ? context.WriteLogsAsync(Global.Logger, "Http.Process.Requests", $"Successfully log a session in {response}") : Task.CompletedTask
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
					var session = context.Session.Get<Session>("Session");
					if (session == null || !session.GetEncryptedID().IsEquals(context.Request.Query["x-session-id"]) || !session.DeviceID.Url64Encode().IsEquals(context.Request.Query["x-device-id"]))
						throw new InvalidSessionException("Session is invalid (The session is not issued by the system)");

					if (Handler.TrackSessions)
						session.SendSessionState("Users", "PUT /session/otp", null, true, Handler.TrackAPISessions, false);

					var request = (await context.ReadTextAsync(Global.CancellationToken).ConfigureAwait(false)).ToExpandoObject();
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
					using var cts = CancellationTokenSource.CreateLinkedTokenSource(Global.CancellationToken, context.RequestAborted);
					var body = new JObject
					{
						{ "ID", id.Encrypt(Global.EncryptionKey) },
						{ "OTP", otp.Encrypt(Global.EncryptionKey) },
						{ "Info", info.Encrypt(Global.EncryptionKey) }
					}.ToString(Formatting.None);
					var response = await context.CallServiceAsync(new RequestInfo(session, "Users", "OTP", "POST")
					{
						Body = body,
						CorrelationID = correlationID
					}, cts.Token, Global.Logger, "Http.Process.Requests").ConfigureAwait(false);

					// update session
					session.User = response.Copy<User>();
					session.SessionID = session.User.SessionID = UtilityService.NewUUID;
					session.IP = $"{context.Connection.RemoteIpAddress}";
					session.Verified = true;

					body = session.GetSessionBody().ToString(Formatting.None);
					await context.CallServiceAsync(new RequestInfo(session, "Users", "Session", "POST")
					{
						Body = body,
						Extra = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
						{
							{ "Signature", body.GetHMACSHA256(Global.ValidationKey) }
						},
						CorrelationID = correlationID
					}, cts.Token, Global.Logger, "Http.Process.Requests").ConfigureAwait(false);

					// update authenticate ticket
					var userPrincipal = new UserPrincipal(new UserIdentity(session.User.ID, session.SessionID, CookieAuthenticationDefaults.AuthenticationScheme));
					await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, userPrincipal, new AuthenticationProperties { IsPersistent = false }).ConfigureAwait(false);
					context.Session.Add("Session", session);

					// response
					await Task.WhenAll
					(
						Global.Cache.RemoveAsync($"Attempt#{context.Connection.RemoteIpAddress}", cts.Token),
						context.WriteAsync(session.GetSessionJson(payload => payload["did"] = session.DeviceID), Formatting.Indented, correlationID, cts.Token),
						Global.IsDebugLogEnabled ? context.WriteLogsAsync(Global.Logger, "Http.Process.Requests", $"Successfully log a session in with OTP {response}") : Task.CompletedTask
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
					var session = context.GetSession();
					var request = (await context.ReadTextAsync(Global.CancellationToken).ConfigureAwait(false)).ToExpandoObject();
					var account = Global.RSA.Decrypt(request.Get("Account", "")).Trim().ToLower();
					var password = Global.RSA.Decrypt(request.Get("Password", ""));
					if (string.IsNullOrWhiteSpace(account) || string.IsNullOrWhiteSpace(password))
						throw new InformationInvalidException();

					if (Handler.TrackSessions)
						session.SendSessionState("Users", "PATCH /account", null, true, Handler.TrackAPISessions, false);

					var language = context.GetParameter("language") ?? "vi-VN";
					var requestURI = context.GetRequestUri();
					var pathSegment = requestURI.GetRequestPathSegments().First();
					var renewURI = $"{requestURI.Scheme}://{context.GetParameter("X-SRP-Host") ?? requestURI.Host}/{(pathSegment.StartsWith("~") ? $"{pathSegment}/" : "")}initializer.aspx?" + "code={{code}}&mode={{mode}}" + $"&language={language}";

					// call service to reset password
					using var cts = CancellationTokenSource.CreateLinkedTokenSource(Global.CancellationToken, context.RequestAborted);
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
					}, cts.Token, Global.Logger, "Http.Process.Requests").ConfigureAwait(false);

					// response
					await Task.WhenAll
					(
						Global.Cache.RemoveAsync($"Attempt#{context.Connection.RemoteIpAddress}", cts.Token),
						context.WriteAsync(response, Formatting.Indented, correlationID, cts.Token),
						Global.IsDebugLogEnabled ? context.WriteLogsAsync(Global.Logger, "Http.Process.Requests", $"Successfully send a renew password request {response}") : Task.CompletedTask
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

		async Task ProcessLogOutRequestAsync(HttpContext context, JObject systemIdentityJson)
		{
			var correlationID = context.GetCorrelationID();
			var isUserInteract = context.Request.Path.Value.IsEndsWith(".aspx") || context.Request.Path.Value.IsEndsWith(".html") || context.Request.Path.Value.IsEndsWith(".php");
			try
			{
				// get session
				var session = context.GetSession();
				if (Handler.TrackSessions)
					session.SendSessionState("Users", "DELETE /session", null, false, Handler.TrackAPISessions, false);

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
				}, cts.Token, Global.Logger, "Http.Process.Requests").ConfigureAwait(false);

				// perform log out
				await context.SignOutAsync().ConfigureAwait(false);

				// response
				if (isUserInteract)
				{
					var scripts = @"<script>
					window.__logout = window.__logout || function(){};
					__logout(true);
					</script>";
					await Task.WhenAll
					(
						context.WriteAsync(this.GetSpecialHtml(context, systemIdentityJson, "Log out").Replace("[[placeholder]]", scripts.Replace("\t\t\t\t\t", "")), "text/html", null, 0, "private, no-store, no-cache", TimeSpan.Zero, correlationID, cts.Token),
						Global.IsDebugLogEnabled ? context.WriteLogsAsync(Global.Logger, "Http.Process.Requests", $"Successfully log a session out (direct) {response}") : Task.CompletedTask
					).ConfigureAwait(false);
				}
				else
				{
					session.User = new User("", "", [SystemRole.All.ToString()], new())
					{
						SessionID = session.SessionID = UtilityService.NewUUID
					};
					var body = session.GetSessionBody().ToString(Formatting.None);
					response = await context.CallServiceAsync(new RequestInfo(session, "Users", "Session", "POST")
					{
						Body = body,
						Extra = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
						{
							{ "Signature", body.GetHMACSHA256(Global.ValidationKey) }
						},
						CorrelationID = correlationID
					}, cts.Token, Global.Logger, "Http.Process.Requests").ConfigureAwait(false);

					context.Session.Add("Session", session);
					if (Handler.TrackSessions)
						session.SendSessionState("Users", "POST /session", null, true, Handler.TrackAPISessions, false);

					await Task.WhenAll
					(
						context.WriteAsync(session.GetSessionJson(payload => payload["did"] = session.DeviceID), Formatting.Indented, correlationID, cts.Token),
						Global.IsDebugLogEnabled ? context.WriteLogsAsync(Global.Logger, "Http.Process.Requests", $"Successfully log a session out {response}") : Task.CompletedTask
					).ConfigureAwait(false);
				}
			}
			catch (Exception ex)
			{
				if (isUserInteract)
				{
					await context.WriteLogsAsync(Global.Logger, "Http.Process.Requests", $"Error occurred while logging out => {ex.Message}", ex).ConfigureAwait(false);
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
						message => message.Type.IsEquals("Service#RequestInfo") ? Global.SendServiceInfoAsync() : Task.CompletedTask,
						exception => Global.WriteLogsAsync(Global.Logger, "Http.Process.Requests", exception.Message, exception)
					);
					if (!Router.GotBackupRouter())
					{
						if (Handler.Cache.UseL1Cache)
						{
							Handler.CacheUpdater?.Dispose();
							Handler.CacheUpdater = Router.IncomingChannel.Subscribe<CommunicateMessage>
							(
								"messages.services.portals.http.l1cache",
								message =>
								{
									if (!Global.NodeID.IsEquals(message.ExcludedNodeID))
									{
										var key = message.Type;
										TimeSpan? validFor = null;
										if (key.IndexOf('#') > 0)
										{
											if (Int32.TryParse(key.Right(key.Length - key.IndexOf('#')), out var minutes) && minutes > 0)
												validFor = TimeSpan.FromMinutes(minutes);
											key = key.Left(key.IndexOf('#'));
										}
										Handler.Cache.SetL1CacheItem(key, message.Data as JObject, validFor);
									}
								}
							);
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
					}
					catch { }
				},
				// backup - on connection established
				(sender, arguments) =>
				{
					if (Handler.Cache.UseL1Cache)
					{
						Handler.CacheUpdater?.Dispose();
						Handler.CacheUpdater = Router.BackupChannel.Subscribe<CommunicateMessage>
						(
							"messages.services.portals.http.l1cache",
							message => Handler.Cache.SetL1CacheItem(message.Type, message.Data as JObject)
						);
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
	}

	internal static class HandlerExtentions
	{
		static NetCrawlerDetect.CrawlerDetect CrawlerDetector { get; } = new NetCrawlerDetect.CrawlerDetect();

		public static void SendSessionState(this RequestInfo requestInfo, JObject systemIdentityJson, string serviceName, string serviceURI, bool trackStatistics)
		{
			if (Handler.TrackSessions)
				requestInfo.SendSessionState(systemIdentityJson, message =>
				{
					message.Data["Crawler"] = CrawlerDetector.IsCrawler(requestInfo.Session.AppAgent) || "Generic OS".IsEquals(requestInfo.Session.AppAgent.GetOSInfo());
					var serviceInfo = message.Data.Get<JObject>("Service");
					if (!string.IsNullOrWhiteSpace(serviceName))
						serviceInfo["Name"] = serviceName.ToLower();
					if (!string.IsNullOrWhiteSpace(serviceURI))
						serviceInfo["URI"] = serviceURI;
				}, trackStatistics);
			else
				requestInfo.TrackStatistics();
		}

		public static Session NormalizeSession(this Session session, HttpContext context = null)
		{
			var appName = context?.GetParameter("x-app-name");
			try
			{
				session.AppName = (appName ?? session.AppName).Url64Decode();
			}
			catch
			{
				session.AppName = appName ?? session.AppName;
			}

			var appPlatform = context?.GetParameter("x-app-platform");
			try
			{
				session.AppPlatform = (appPlatform ?? session.AppPlatform).Url64Decode();
			}
			catch
			{
				session.AppPlatform = appPlatform ?? session.AppPlatform;
			}

			var deviceID = context?.GetParameter("x-device-id");
			try
			{
				session.DeviceID = (deviceID ?? session.DeviceID).Url64Decode();
			}
			catch
			{
				session.DeviceID = deviceID ?? session.DeviceID;
			}

			if (context != null)
			{
				var cookie = context.Request?.Cookies[".VIEApps-SessionP"];
				if (!string.IsNullOrWhiteSpace(cookie) && (string.IsNullOrWhiteSpace(session.SessionID) || string.IsNullOrWhiteSpace(session.DeviceID)))
					try
					{
						var info = cookie.Decrypt(Global.EncryptionKey, true).ToList("|");
						if (string.IsNullOrWhiteSpace(session.SessionID) && info.Count > 0)
							session.SessionID = session.User.SessionID = info[0];
						if (string.IsNullOrWhiteSpace(session.DeviceID) && info.Count > 1)
							session.DeviceID = info[1];
					}
					catch { }
				else
					session.UpdateSessionCookie(context);
			}

			return session;
		}

		public static Session UpdateSessionCookie(this Session session, HttpContext context, bool check = false)
		{
			if (context != null && !string.IsNullOrWhiteSpace(session.SessionID) && !string.IsNullOrWhiteSpace(session.DeviceID))
				try
				{
					var update = true;
					if (check)
					{
						update = string.IsNullOrWhiteSpace(session.SessionID) || string.IsNullOrWhiteSpace(session.DeviceID);
						var cookie = update ? null : context.Request?.Cookies[".VIEApps-SessionP"];
						if (!string.IsNullOrWhiteSpace(cookie))
							try
							{
								var info = cookie.Decrypt(Global.EncryptionKey, true).ToList("|");
								update = (info.Count > 0 && !session.SessionID.IsEquals(info[0])) || (info.Count > 1 && !session.DeviceID.IsEquals(info[1]));
							}
							catch { }
					}
					if (update)
						context.Response?.Cookies?.Append(".VIEApps-SessionP", $"{session.SessionID}|{session.DeviceID}".Encrypt(Global.EncryptionKey, true), new CookieOptions { Expires = DateTime.Now.AddDays(366) });
				}
				catch { }
			return session;
		}

		public static string NormalizeHtml(this HttpContext context, string html, bool alwaysUseHTTPs, bool alwaysReturnHTTPs, string baseURL)
		{
			var requestURI = context.GetRequestUri();
			var correlationID = context.GetCorrelationID();
			var session = context.GetSession();
			var isMobile = string.IsNullOrWhiteSpace(session.AppPlatform) || session.AppPlatform.IsContains("Desktop") ? "false" : "true";
			var osInfo = (session.AppAgent ?? "").GetOSInfo();
			var osPlatform = osInfo.GetANSIUri();
			var osMode = "true".IsEquals(isMobile) ? "mobile-os" : "desktop-os";

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
				["timestamp"] = DateTime.Now.ToUnixTimestamp(),
				["time-stamp"] = DateTime.Now.ToUnixTimestamp(),
				["host-md5"] = requestURI.Host.GenerateUUID(),
				["host-uuid"] = requestURI.Host.GenerateUUID()
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
	}

}
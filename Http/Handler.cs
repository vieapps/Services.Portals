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
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using WampSharp.V2.Core.Contracts;
using net.vieapps.Components.Caching;
using net.vieapps.Components.Security;
using net.vieapps.Components.WebSockets;
using net.vieapps.Components.Utility;
#endregion

namespace net.vieapps.Services.Portals
{
	public class Handler
	{
		public Handler(RequestDelegate _) { }

		#region Properties
		static HashSet<string> Initializers { get; } = "_initializer,initializer.aspx,initializer.ashx".ToHashSet();

		static HashSet<string> Validators { get; } = "_validator,validator.aspx,validator.ashx".ToHashSet();

		static HashSet<string> LogIns { get; } = "_login,_signin,_signup,_register,_admin,_users,_cms,login.aspx,login.ashx,signin.aspx,signin.ashx,signup.aspx,signup.ashx,register.aspx,register.ashx,admin.aspx,admin.ashx,users.aspx,users.ashx,cms.aspx,cms.ashx".ToHashSet();

		static HashSet<string> LogOuts { get; } = "_logout,_signout,logout.aspx,logout.ashx,signout.aspx,signout.ashx".ToHashSet();

		static bool UseShortURLs => "true".IsEquals(UtilityService.GetAppSetting("Portals:UseShortURLs", "true"));

		static string LoadBalancingHealthCheckUrl { get; } = UtilityService.GetAppSetting("HealthCheckUrl", "/load-balancing-health-check");

		internal static Components.WebSockets.WebSocket WebSocket { get; private set; }

		internal static string NodeName => Extensions.GetUniqueName(Global.ServiceName + ".http");

		internal static bool RedirectToPassportOnUnauthorized => "true".IsEquals(UtilityService.GetAppSetting("Portals:RedirectToPassportOnUnauthorized", "true"));

		public static List<string> ExcludedHeaders { get; } = UtilityService.GetAppSetting("ExcludedHeaders", "connection,accept,accept-encoding,accept-language,cache-control,cookie,host,content-type,content-length,user-agent,upgrade-insecure-requests,purpose,ms-aspnetcore-token,x-forwarded-for,x-forwarded-proto,x-forwarded-port,x-original-for,x-original-proto,x-original-remote-endpoint,x-original-port,cdn-loop").ToList();

		internal static Cache Cache { get; } = new Cache("VIEApps-Services-Portals", Cache.Configuration.ExpirationTime, Cache.Configuration.Provider, Logger.GetLoggerFactory());

		internal static string RefresherRefererURL => "https://portals.vieapps.net/~url.refresher";

		internal static int RequestTimeout { get; } = Int32.TryParse(UtilityService.GetAppSetting("Portals:RequestTimeout", "13"), out var timeout) && timeout > 0 ? timeout : 13;

		static Task ProcessInterCommunicateMessageAsync(CommunicateMessage message)
			=> Task.CompletedTask;
		#endregion

		public async Task Invoke(HttpContext context)
		{
			// request of WebSocket
			if (context.WebSockets.IsWebSocketRequest)
				await Task.WhenAll
				(
					Global.IsVisitLogEnabled ? context.WriteLogsAsync(Global.Logger, "Http.Visits", $"Wrap a WebSocket connection successful\r\n- Endpoint: {context.Connection.RemoteIpAddress}:{context.Connection.RemotePort}\r\n- URI: {context.GetRequestUri()}{(Global.IsDebugLogEnabled ? $"\r\n- Headers:\r\n\t{context.Request.Headers.Select(kvp => $"{kvp.Key}: {kvp.Value}").Join("\r\n\t")}" : "")}") : Task.CompletedTask,
					Handler.WebSocket.WrapAsync(context)
				).ConfigureAwait(false);

			// request of HTTP
			else
			{
				// CORS: allow origin
				context.Response.Headers["Access-Control-Allow-Origin"] = "*";

				// CORS: options
				if (context.Request.Method.IsEquals("OPTIONS"))
				{
					var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
					{
						["Access-Control-Allow-Methods"] = "HEAD,GET,POST"
					};
					if (context.Request.Headers.TryGetValue("Access-Control-Request-Headers", out var requestHeaders))
						headers["Access-Control-Allow-Headers"] = requestHeaders;
					context.SetResponseHeaders((int)HttpStatusCode.OK, headers);
					await context.FlushAsync(Global.CancellationToken).ConfigureAwait(false);
				}

				// load balancing health check
				else if (context.Request.Path.Value.IsEquals(Handler.LoadBalancingHealthCheckUrl))
					await context.WriteAsync("OK", "text/plain", null, 0, null, TimeSpan.Zero, null, Global.CancellationToken).ConfigureAwait(false);

				// process portals' requests
				else
					await this.ProcessRequestAsync(context).ConfigureAwait(false);
			}
		}

		internal async Task ProcessRequestAsync(HttpContext context)
		{
			// prepare
			context.SetItem("PipelineStopwatch", Stopwatch.StartNew());
			var requestPath = context.GetRequestUri().GetRequestPathSegments(true).First();

			if (Global.IsVisitLogEnabled)
				await context.WriteVisitStartingLogAsync().ConfigureAwait(false);

			// request to favicon.ico file
			if (requestPath.Equals("favicon.ico"))
				await context.ProcessFavouritesIconFileRequestAsync().ConfigureAwait(false);

			// request to static segments
			else if (Global.StaticSegments.Contains(requestPath))
				await context.ProcessStaticFileRequestAsync().ConfigureAwait(false);

			// request to portal desktops/resources
			else
				await this.ProcessPortalRequestAsync(context).ConfigureAwait(false);

			if (Global.IsVisitLogEnabled)
				await context.WriteVisitFinishingLogAsync().ConfigureAwait(false);
		}

		async Task ProcessPortalRequestAsync(HttpContext context)
		{
			// prepare session information
			var correlationID = context.GetCorrelationID();
			var session = context.GetSession();
			var requestURI = context.GetRequestUri();
			var isMobile = string.IsNullOrWhiteSpace(session.AppPlatform) || session.AppPlatform.IsContains("Desktop") ? "false" : "true";
			var osInfo = (session.AppAgent ?? "").GetOSInfo();

			// get authenticate token
			var authenticateToken = context.GetParameter("x-app-token") ?? context.GetParameter("x-passport-token");

			// normalize the Bearer token
			if (string.IsNullOrWhiteSpace(authenticateToken))
			{
				authenticateToken = context.GetHeaderParameter("authorization");
				authenticateToken = authenticateToken != null && authenticateToken.IsStartsWith("Bearer") ? authenticateToken.ToArray(" ").Last() : null;
			}

			// got authenticate token => update the session
			if (!string.IsNullOrWhiteSpace(authenticateToken))
				try
				{
					// authenticate (token is expired after 15 minutes)
					await context.UpdateWithAuthenticateTokenAsync(session, authenticateToken, 90, null, null, null, Global.Logger, "Http.Authentication", correlationID).ConfigureAwait(false);
					if (Global.IsDebugLogEnabled)
						await context.WriteLogsAsync(Global.Logger, "Http.Authentication", $"Successfully authenticate an user with token {session.ToJson().ToString(Formatting.Indented)}").ConfigureAwait(false);

					// perform sign-in (to create authenticate ticket cookie) when the authenticate token its came from passport service
					if (context.GetParameter("x-passport-token") != null)
					{
						await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new UserPrincipal(session.User), new AuthenticationProperties { IsPersistent = false }).ConfigureAwait(false);
						if (Global.IsDebugLogEnabled)
							await context.WriteLogsAsync(Global.Logger, "Http.Authentication", $"Successfully create the authenticate ticket cookie for an user ({session.User.ID})").ConfigureAwait(false);
					}

					// just assign user information
					else
						context.User = new UserPrincipal(session.User);
				}
				catch (Exception ex)
				{
					await context.WriteLogsAsync(Global.Logger, "Http.Authentication", $"Failure authenticate a token => {ex.Message}", ex, Global.ServiceName, LogLevel.Error).ConfigureAwait(false);
				}

			// no authenticate token => update user of the session if already signed-in
			else if (context.IsAuthenticated())
				session.User = context.GetUser();

			// update session
			session.User.SessionID = string.IsNullOrWhiteSpace(session.User.SessionID) ? UtilityService.NewUUID : session.User.SessionID;
			session.SessionID = session.User.SessionID;

			var appName = context.GetParameter("x-app-name");
			if (!string.IsNullOrWhiteSpace(appName))
				session.AppName = appName;

			var appPlatform = context.GetParameter("x-app-platform");
			if (!string.IsNullOrWhiteSpace(appPlatform))
				session.AppPlatform = appPlatform;

			var deviceID = context.GetParameter("x-device-id");
			if (!string.IsNullOrWhiteSpace(deviceID))
				session.DeviceID = deviceID;

			// prepare the requesting information
			var systemIdentity = string.Empty;
			var specialRequest = string.Empty;
			var legacyRequest = string.Empty;
			var queryString = context.Request.QueryString.ToDictionary(query =>
			{
				var pathSegments = context.GetRequestPathSegments().Where(segment => !segment.IsEquals("desktop.aspx") && !segment.IsEquals("default.aspx") && !segment.IsEquals("index.aspx") && !segment.IsEquals("index.php")).ToArray();
				var requestSegments = pathSegments;

				// special parameters (like spider indicator (robots.txt)/ads indicator (ads.txt) or system/organization identity)
				if (pathSegments.Length > 0 && !string.IsNullOrWhiteSpace(pathSegments[0]))
				{
					// system/oranization identity or service
					if (pathSegments[0].StartsWith("~"))
					{
						// specifict service
						if (requestSegments[0].IsEquals("~apis.service") || requestSegments[0].IsEquals("~apis.gateway"))
							specialRequest = "service";
						else
						{
							systemIdentity = pathSegments[0].Right(pathSegments[0].Length - 1).Replace(StringComparison.OrdinalIgnoreCase, ".html", "").GetANSIUri(true, false);
							query["x-system"] = systemIdentity;
						}
						requestSegments = pathSegments.Skip(1).ToArray();
						if (requestSegments.Length > 0 && specialRequest.IsEquals("service"))
						{
							query["service-name"] = requestSegments.Length > 0 && !string.IsNullOrWhiteSpace(requestSegments[0]) ? requestSegments[0].GetANSIUri(true, true) : "";
							query["object-name"] = requestSegments.Length > 1 && !string.IsNullOrWhiteSpace(requestSegments[1]) ? requestSegments[1].GetANSIUri(true, true) : "";
							query["object-identity"] = requestSegments.Length > 2 && !string.IsNullOrWhiteSpace(requestSegments[2]) ? requestSegments[2].GetANSIUri() : "";
						}
					}

					// special requests (_initializer, _validator, _login, _logout) or special resources (_assets, _css, _fonts, _images, _js)
					else if (pathSegments[0].StartsWith("_"))
					{
						// special requests
						if (Handler.Initializers.Contains(pathSegments[0].ToLower()))
							specialRequest = "initializer";

						else if (Handler.Validators.Contains(pathSegments[0].ToLower()))
							specialRequest = "validator";

						else if (Handler.LogIns.Contains(pathSegments[0].ToLower()))
							specialRequest = "login";

						else if (Handler.LogOuts.Contains(pathSegments[0].ToLower()))
							specialRequest = "logout";

						// special resources
						else
						{
							systemIdentity = "~resources";
							query["x-resource"] = pathSegments[0].Right(pathSegments[0].Length - 1).GetANSIUri(true, true);
							query["x-path"] = pathSegments.Skip(1).Join("/");
						}

						// no info
						requestSegments = Array.Empty<string>();
					}

					// HTTP indicator
					else if (pathSegments[0].IsEndsWith(".txt"))
					{
						systemIdentity = "~indicators";
						query["x-indicator"] = pathSegments[0].Replace(StringComparison.OrdinalIgnoreCase, ".txt", "").ToLower();
						requestSegments = Array.Empty<string>();
					}
				}

				// normalize info of requests
				if (requestSegments.Length > 0 && specialRequest.IsEquals(""))
				{
					// special requests
					if (Handler.Initializers.Contains(requestSegments[0].ToLower()))
					{
						specialRequest = "initializer";
						requestSegments = Array.Empty<string>();
					}

					else if (Handler.Validators.Contains(requestSegments[0].ToLower()))
					{
						specialRequest = "validator";
						requestSegments = Array.Empty<string>();
					}

					else if (Handler.LogIns.Contains(requestSegments[0].ToLower()))
					{
						specialRequest = "login";
						requestSegments = Array.Empty<string>();
					}

					else if (Handler.LogOuts.Contains(requestSegments[0].ToLower()))
					{
						specialRequest = "logout";
						requestSegments = Array.Empty<string>();
					}

					// request of legacy systems
					else if (requestSegments[0].IsEndsWith(".ashx") || requestSegments[0].IsEndsWith(".aspx"))
						legacyRequest = requestSegments.Join("/");

					// parameters of desktop and contents
					else
					{
						var value = requestSegments[0].Replace(StringComparison.OrdinalIgnoreCase, ".html", "");
						value = value.Equals("") || value.StartsWith("-") || value.IsEquals("default") || value.IsEquals("index") || value.IsNumeric() ? "default" : value.GetANSIUri();
						query["x-desktop"] = (value.Equals("default") ? "-" : "") + value;

						value = requestSegments.Length > 1 && !string.IsNullOrWhiteSpace(requestSegments[1]) ? requestSegments[1].Replace(".html", "") : null;
						query["x-parent"] = string.IsNullOrWhiteSpace(value) ? null : value.GetANSIUri();

						if (requestSegments.Length > 2 && !string.IsNullOrWhiteSpace(requestSegments[2]))
						{
							value = requestSegments[2].Replace(StringComparison.OrdinalIgnoreCase, ".html", "");
							if (value.IsNumeric())
								query["x-page"] = value;
							else
								query["x-content"] = value.GetANSIUri();

							if (requestSegments.Length > 3 && !string.IsNullOrWhiteSpace(requestSegments[3]))
							{
								value = requestSegments[3].Replace(StringComparison.OrdinalIgnoreCase, ".html", "");
								if (value.IsNumeric())
									query["x-page"] = value;
							}
						}
					}
				}
				else if (!systemIdentity.IsEquals("~indicators") && !systemIdentity.IsEquals("~resources") && !specialRequest.IsEquals("service"))
					query["x-desktop"] = "-default";
			});

			// validate HTTP Verb
			var httpVerb = (context.Request.Method ?? "GET").ToUpper();
			if (httpVerb.IsEquals("POST") && !specialRequest.IsEquals("service"))
				throw new MethodNotAllowedException(httpVerb);

			var headers = context.Request.Headers.ToDictionary(dictionary =>
			{
				Handler.ExcludedHeaders.ForEach(name => dictionary.Remove(name));
				dictionary.Keys.Where(name => name.IsStartsWith("cf-") || name.IsStartsWith("sec-")).ToList().ForEach(name => dictionary.Remove(name));
				dictionary["x-host"] = context.GetParameter("Host");
				dictionary["x-url"] = "https".IsEquals(context.GetHeaderParameter("x-forwarded-proto") ?? context.GetHeaderParameter("x-original-proto")) && !"https".IsEquals(requestURI.Scheme)
					? requestURI.AbsoluteUri.Replace(StringComparison.OrdinalIgnoreCase, $"{requestURI.Scheme}://", "https://")
					: requestURI.AbsoluteUri;
				dictionary["x-use-short-urls"] = Handler.UseShortURLs.ToString().ToLower();
				dictionary["x-environment-is-mobile"] = isMobile;
				dictionary["x-environment-os-info"] = osInfo;
			});

			var extra = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			if (queryString.Remove("x-request-extra", out var extraInfo) && !string.IsNullOrWhiteSpace(extraInfo))
				try
				{
					extra = extraInfo.Url64Decode().ToExpandoObject().ToDictionary(kvp => kvp.Key, kvp => kvp.Value?.ToString(), StringComparer.OrdinalIgnoreCase);
				}
				catch { }

			// process the request
			var requestInfo = new RequestInfo(session, "Portals", "Identify.System", "GET", queryString, headers, null, extra, correlationID);
			JObject systemIdentityJson = null;
			if (string.IsNullOrWhiteSpace(specialRequest))
				try
				{
					using var ctsrc = new CancellationTokenSource(TimeSpan.FromSeconds(Handler.RequestTimeout));
					using var cts = CancellationTokenSource.CreateLinkedTokenSource(Global.CancellationToken, ctsrc.Token, context.RequestAborted);

					// call the Portals service to identify the system
					if (string.IsNullOrWhiteSpace(systemIdentity) || "~indicators".IsEquals(systemIdentity))
					{
						systemIdentityJson = systemIdentityJson ?? await context.CallServiceAsync(requestInfo, cts.Token, Global.Logger, "Http.Process.Requests").ConfigureAwait(false) as JObject;
						requestInfo.Query["x-system"] = systemIdentityJson.Get<string>("Alias");
					}

					// request of portal desktops/resources
					if (string.IsNullOrWhiteSpace(legacyRequest))
					{
						// working with cache
						if (!Handler.RefresherRefererURL.IsEquals(context.GetReferUrl()) && requestInfo.GetParameter("noCache") == null && requestInfo.GetParameter("forceCache") == null)
						{
							var cacheKey = "";
							var eTag = "";
							var contentType = "text/html";
							var expires = DateTime.Now.AddMinutes(13);
							var baseURL = "";
							var rootURL = "/";
							var filesHttpURI = UtilityService.GetAppSetting("HttpUri:Files");
							var portalsHttpURI = UtilityService.GetAppSetting("HttpUri:Portals");
							var alwaysUseHTTPs = false;
							var redirectToNoneWWW = false;

							if (systemIdentity.IsEquals("~resources"))
							{
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
										: type.IsStartsWith("font") ? "fonts" : "";

								cacheKey = requestURI.ToString().ToLower();
								cacheKey = isThemeResource && (type.IsEquals("css") || type.IsEquals("js")) && !(identity.Length == 34 && identity.Right(32).IsValidUUID())
									? $"{type}#{identity}"
									: $"v#{(cacheKey.IndexOf("?") > 0 ? cacheKey.Left(cacheKey.IndexOf("?")) : cacheKey).GenerateUUID()}";
								eTag = cacheKey;

								if (type.IsEquals("css"))
									contentType = "text/css";
								else if (type.IsEquals("js"))
									contentType = "application/javascript";
								else if (type.IsEquals("fonts"))
									contentType = $"font/{path.ToList(".").Last()}";
								else if (type.IsEquals("images"))
								{
									contentType = path.ToList(".").Last();
									contentType = $"image/{(contentType.IsEquals("svg") ? "svg+xml" : contentType.IsEquals("jpg") || contentType.IsEquals("jpeg") ? "jpeg" : contentType)}";
								}
								expires = DateTime.Now.AddDays(366);
							}

							else if (!"~indicators".IsEquals(systemIdentity))
							{
								systemIdentityJson = systemIdentityJson ?? await context.CallServiceAsync(requestInfo, cts.Token, Global.Logger, "Http.Process.Requests").ConfigureAwait(false) as JObject;
								var organizationID = systemIdentityJson.Get<string>("ID");
								var organizationAlias = systemIdentityJson.Get<string>("Alias");

								filesHttpURI = systemIdentityJson.Get<string>("FilesHttpURI");
								filesHttpURI += filesHttpURI.EndsWith("/") ? "" : "/";
								portalsHttpURI = systemIdentityJson.Get<string>("PortalsHttpURI");
								portalsHttpURI += portalsHttpURI.EndsWith("/") ? "" : "/";

								alwaysUseHTTPs = systemIdentityJson.Get<bool>("AlwaysUseHTTPs");
								redirectToNoneWWW = systemIdentityJson.Get<bool>("RedirectToNoneWWW");

								var path = requestURI.AbsolutePath;
								path = (path.IsEndsWith(".html") || path.IsEndsWith(".aspx") ? path.Left(path.Length - 5) : path).ToLower();

								if (path.IsStartsWith($"/~{organizationAlias}"))
								{
									path = path.Right(path.Length - organizationAlias.Length - 2);
									baseURL = $"{portalsHttpURI}~{organizationAlias}/";
									rootURL = "./";
								}

								var desktopAlias = queryString["x-desktop"].ToLower();
								path = path.Equals("") || path.Equals("/") || path.Equals("/index") || path.Equals("/default") ? desktopAlias : path;

								cacheKey = $"{organizationID}:" + ("-default".IsEquals(desktopAlias) ? desktopAlias : path).GenerateUUID();
								eTag = $"v#{cacheKey}";
							}

							if (!string.IsNullOrWhiteSpace(cacheKey))
							{
								// redirect
								if (contentType.IsEquals("text/html") && ((alwaysUseHTTPs && !requestURI.Scheme.IsEquals("https")) || (redirectToNoneWWW && requestURI.Host.IsStartsWith("www."))))
								{
									context.SetResponseHeaders((int)HttpStatusCode.Redirect, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
									{
										{ "Location", $"{(alwaysUseHTTPs ? "https" : requestURI.Scheme)}://{(redirectToNoneWWW && requestURI.Host.IsStartsWith("www.") ? requestURI.Host.Replace("www.", "") : requestURI.Host)}{requestURI.PathAndQuery}{requestURI.Fragment}" }
									});
									return;
								}

								// process cache
								if (Global.IsDebugLogEnabled || Global.IsVisitLogEnabled)
									await context.WriteLogsAsync(Global.Logger, "Http.Visits", $"Attempt to process the CMS Portals service cache ({requestURI})").ConfigureAwait(false);

								// last modified
								var modifiedSince = context.GetHeaderParameter("If-Modified-Since") ?? context.GetHeaderParameter("If-Unmodified-Since");
								var lastModified = modifiedSince != null ? await Handler.Cache.GetAsync<string>($"{cacheKey}:time", cts.Token).ConfigureAwait(false) : null;
								if (modifiedSince != null)
								{
									var noneMatch = context.GetHeaderParameter("If-None-Match");
									if (eTag.IsEquals(noneMatch) && lastModified != null && modifiedSince.FromHttpDateTime() >= lastModified.FromHttpDateTime())
									{
										context.SetResponseHeaders((int)HttpStatusCode.NotModified, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
										{
											{ "X-Cache", "http-time" },
											{ "X-Correlation-ID", correlationID },
											{ "Content-Type", $"{contentType}; charset=utf-8" },
											{ "ETag", eTag },
											{ "Last-Modified", lastModified }
										});

										if (Global.IsDebugLogEnabled || Global.IsVisitLogEnabled)
											await context.WriteLogsAsync(Global.Logger, "Http.Visits", $"Process the CMS Portals service cache was done => not modified ({eTag} - {lastModified})").ConfigureAwait(false);
										return;
									}
								}

								// cached data
								var cached = await Handler.Cache.GetAsync<string>(cacheKey, cts.Token).ConfigureAwait(false);
								if (!string.IsNullOrWhiteSpace(cached))
								{
									lastModified = lastModified ?? await Handler.Cache.GetAsync<string>($"{cacheKey}:time", cts.Token).ConfigureAwait(false) ?? DateTime.Now.ToHttpString();
									context.SetResponseHeaders((int)HttpStatusCode.OK, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
									{
										{ "X-Cache", "http-cache" },
										{ "X-Correlation-ID", correlationID },
										{ "Content-Type", $"{contentType}; charset=utf-8" },
										{ "ETag", eTag },
										{ "Last-Modified", lastModified },
										{ "Expires", expires.ToHttpString() },
										{ "Cache-Control", "public" }
									});

									if (contentType.IsEquals("text/html"))
									{
										var osPlatform = osInfo.GetANSIUri();
										var osMode = "true".IsEquals(isMobile) ? "mobile-os" : "desktop-os";
										cached = cached.Format(new Dictionary<string, object>
										{
											["isMobile"] = isMobile,
											["is-mobile"] = isMobile,
											["osInfo"] = osInfo,
											["os-info"] = osInfo,
											["osPlatform"] = osPlatform,
											["os-platform"] = osPlatform,
											["osMode"] = osMode,
											["os-mode"] = osMode,
											["correlationID"] = correlationID,
											["correlation-id"] = correlationID
										}).Replace("~#/", portalsHttpURI).Replace("~~~/", portalsHttpURI).Replace("~~/", filesHttpURI).Replace("~/", rootURL);
										if (!string.IsNullOrWhiteSpace(baseURL))
											cached = cached.Insert(cached.PositionOf(">", cached.PositionOf("<head")) + 1, $"<base href=\"{baseURL}\"/>");
									}

									await context.WriteAsync(cached.ToBytes(), cts.Token).ConfigureAwait(false);
									if (Global.IsDebugLogEnabled || Global.IsVisitLogEnabled)
										await context.WriteLogsAsync(Global.Logger, "Http.Visits", $"Process the CMS Portals service cache was done => found ({cacheKey})").ConfigureAwait(false);
									return;
								}
							}
						}

						// call Portals service to process the request
						requestInfo = new RequestInfo(requestInfo) { ObjectName = "Process.Http.Request" };
						var response = (await context.CallServiceAsync(requestInfo, cts.Token, Global.Logger, "Http.Process.Requests").ConfigureAwait(false)).ToExpandoObject();
						context.SetResponseHeaders(response.Get("StatusCode", (int)HttpStatusCode.OK), response.Get("Headers", new Dictionary<string, string>()));
						var body = response.Get<string>("Body");
						if (body != null)
							await context.WriteAsync(response.Get("BodyAsPlainText", false) ? body.ToBytes() : body.Base64ToBytes().Decompress(response.Get("BodyEncoding", "deflate")), cts.Token).ConfigureAwait(false);
					}

					// request of legacy system (files and medias)
					else
					{
						systemIdentityJson = systemIdentityJson ?? await context.CallServiceAsync(requestInfo, cts.Token, Global.Logger, "Http.Process.Requests").ConfigureAwait(false) as JObject;
						var requestSegments = legacyRequest.ToArray("/").ToList();
						if (!requestSegments.Any())
							requestSegments.Add("");
						var legacyHandler = requestSegments.FirstOrDefault() ?? "";

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

						if (legacyHandler.IsEquals("files") && requestSegments.Count > 3 && requestSegments[3].Contains("-") && requestSegments[3].Length > 32)
						{
							var id = requestSegments[3].Left(32);
							var filename = requestSegments[3].Right(requestSegments[3].Length - 33);
							requestSegments[3] = id;
							requestSegments.Insert(4, filename);
							requestSegments = requestSegments.Take(5).ToList();
						}

						else if (legacyHandler.IsStartsWith("thumbnail") && requestSegments.Count > 5 && requestSegments[5].Contains("-") && requestSegments[5].Length > 32)
						{
							var id = requestSegments[5].Left(32);
							var filename = requestSegments[5].Right(requestSegments[5].Length - 33);
							requestSegments[5] = id;
							requestSegments.Insert(6, filename);
							requestSegments = requestSegments.Take(7).ToList();
						}

						var filesHttpURI = systemIdentityJson?.Get<string>("FilesHttpURI") ?? UtilityService.GetAppSetting("HttpUri:Files", "https://fs.vieapps.net");
						while (filesHttpURI.IsEndsWith("/"))
							filesHttpURI = filesHttpURI.Left(filesHttpURI.Length - 1).Trim();

						context.SetResponseHeaders((int)HttpStatusCode.MovedPermanently, new Dictionary<string, string>
						{
							["Location"] = $"{filesHttpURI}/{requestSegments.Join("/")}"
						});
					}
				}
				catch (OperationCanceledException) { }
				catch (Exception ex)
				{
					var statusCode = ex.GetHttpStatusCode();
					var query = context.ParseQuery();
					if (ex is AccessDeniedException && !context.IsAuthenticated() && Handler.RedirectToPassportOnUnauthorized && !query.ContainsKey("x-app-token") && !query.ContainsKey("x-passport-token"))
					{
						await context.WriteLogsAsync("Http.Process.Requests", $"Access denied ({statusCode}) => {context.Request.Method} {requestURI}", ex, Global.ServiceName, LogLevel.Error).ConfigureAwait(false);
						context.Redirect(context.GetPassportSessionAuthenticatorUrl());
					}
					else
					{
						if (ex is WampException wampException)
						{
							var wampDetails = wampException.GetDetails(requestInfo);
							statusCode = wampDetails.Item1;
							context.ShowHttpError(statusCode: statusCode, message: wampDetails.Item2, type: wampDetails.Item3, correlationID: correlationID, stack: wampDetails.Item4 + "\r\n\t" + ex.StackTrace, showStack: Global.IsDebugLogEnabled);
						}
						else
							context.ShowHttpError(statusCode: statusCode, message: ex.Message, type: ex.GetTypeName(true), correlationID: correlationID, ex: ex, showStack: Global.IsDebugLogEnabled);
						await context.WriteLogsAsync("Http.Process.Requests", $"Error occurred ({statusCode}) => {context.Request.Method} {requestURI}", ex, Global.ServiceName, LogLevel.Error).ConfigureAwait(false);
					}
				}

			else
				switch (specialRequest)
				{
					case "initializer":
						await this.ProcessInitializerRequestAsync(context).ConfigureAwait(false);
						break;

					case "validator":
						await this.ProcessValidatorRequestAsync(context).ConfigureAwait(false);
						break;

					case "login":
						using (var cts = CancellationTokenSource.CreateLinkedTokenSource(Global.CancellationToken, context.RequestAborted))
						{
							try
							{
								systemIdentityJson = systemIdentityJson ?? await context.CallServiceAsync(requestInfo, cts.Token, Global.Logger, "Http.Process.Requests").ConfigureAwait(false) as JObject;
								await this.ProcessLogInRequestAsync(context, systemIdentityJson?.Get<string>("ID")).ConfigureAwait(false);
							}
							catch (Exception ex)
							{
								var statusCode = ex.GetHttpStatusCode();
								if (ex is WampException wampException)
								{
									var wampDetails = wampException.GetDetails(requestInfo);
									statusCode = wampDetails.Item1;
									context.ShowHttpError(statusCode: statusCode, message: wampDetails.Item2, type: wampDetails.Item3, correlationID: correlationID, stack: wampDetails.Item4 + "\r\n\t" + ex.StackTrace, showStack: Global.IsDebugLogEnabled);
								}
								else
									context.ShowHttpError(statusCode: statusCode, message: ex.Message, type: ex.GetTypeName(true), correlationID: correlationID, ex: ex, showStack: Global.IsDebugLogEnabled);
								await context.WriteLogsAsync("Http.Authentication", $"Error occurred while logging in => {ex.Message}", ex).ConfigureAwait(false);
							}
						}
						break;

					case "logout":
						await this.ProcessLogOutRequestAsync(context).ConfigureAwait(false);
						break;

					case "service":
						using (var cts = CancellationTokenSource.CreateLinkedTokenSource(Global.CancellationToken, context.RequestAborted))
						{
							requestInfo = new RequestInfo(requestInfo) { ServiceName = requestInfo.Query["service-name"], ObjectName = requestInfo.Query["object-name"], Verb = httpVerb };
							try
							{
								await context.WriteAsync(await context.CallServiceAsync(requestInfo, cts.Token, Global.Logger, "Http.Services").ConfigureAwait(false), cts.Token).ConfigureAwait(false);
							}
							catch (Exception ex)
							{
								await context.WriteLogsAsync("Http.Services", $"Error occurred while calling a service => {ex.Message}", ex).ConfigureAwait(false);
								context.WriteError(Global.Logger, ex, requestInfo);
							}
						}
						break;

					default:
						var invalidException = new InvalidRequestException();
						context.ShowHttpError(invalidException.GetHttpStatusCode(), invalidException.Message, invalidException.GetType().GetTypeName(true), correlationID, invalidException, Global.IsDebugLogEnabled);
						break;
				}
		}

		internal static void InitializeWebSocket()
		{
			Handler.WebSocket = new Components.WebSockets.WebSocket(Logger.GetLoggerFactory(), Global.CancellationToken)
			{
				KeepAliveInterval = TimeSpan.FromSeconds(Int32.TryParse(UtilityService.GetAppSetting("Proxy:KeepAliveInterval", "45"), out var interval) ? interval : 45),
				OnError = async (websocket, exception) => await Global.WriteLogsAsync(Global.Logger, "Http.WebSockets", $"Got an error while processing => {exception.Message} ({websocket?.ID} {websocket?.RemoteEndPoint})", exception).ConfigureAwait(false),
				OnMessageReceived = async (websocket, result, data) => await (websocket == null ? Task.CompletedTask : Handler.ProcessWebSocketRequestAsync(websocket, result, data)).ConfigureAwait(false),
			};
		}

		static async Task ProcessWebSocketRequestAsync(ManagedWebSocket websocket, WebSocketReceiveResult result, byte[] data)
		{
			// prepare
			var correlationID = UtilityService.NewUUID;
			var requestMsg = result.MessageType.Equals(WebSocketMessageType.Text) ? data.GetString() : null;
			if (string.IsNullOrWhiteSpace(requestMsg))
				return;

			var requestObj = requestMsg.ToExpandoObject();
			var requestID = requestObj.Get<string>("ID");
			var serviceName = requestObj.Get("ServiceName", "");
			var objectName = requestObj.Get("ObjectName", "");
			var verb = requestObj.Get("Verb", "GET").ToUpper();
			var header = new Dictionary<string, string>(requestObj.Get("Header", new Dictionary<string, string>()), StringComparer.OrdinalIgnoreCase);
			var query = new Dictionary<string, string>(requestObj.Get("Query", new Dictionary<string, string>()), StringComparer.OrdinalIgnoreCase);

			// register session
			var session = websocket.Get<Session>("Session") ?? Global.CurrentHttpContext.GetSession();
			if (serviceName.IsEquals("APIs") && objectName.IsEquals("Session") && verb.IsEquals("REG"))
			{
				session.DeviceID = header.ContainsKey("x-device-id") ? header["x-device-id"] : UtilityService.NewUUID + "@web";
				websocket.Set("Session", session);
			}

			// call a service of APIs
			else
				try
				{
					var response = new JObject
					{
						{ "ID", requestID },
						{ "Type", $"{serviceName.GetCapitalizedFirstLetter()}#{objectName.GetCapitalizedFirstLetter()}#{verb.GetCapitalizedFirstLetter()}" },
						{ "Data", await Global.CallServiceAsync(new RequestInfo(session, serviceName, objectName, verb, query, header, null, null, correlationID), Global.CancellationToken).ConfigureAwait(false) }
					};
					await websocket.SendAsync(response, Global.CancellationToken).ConfigureAwait(false);
				}
				catch (Exception ex)
				{
					try
					{
						var response = new JObject
						{
							{ "ID", requestID },
							{ "Type", "Error" },
							{ "Error", new JObject
								{
									{ "Type", ex.GetTypeName(true) },
									{ "Message", ex.Message },
									{ "Stack", ex.StackTrace }
								}
							}
						};
						await websocket.SendAsync(response, Global.CancellationToken).ConfigureAwait(false);
					}
					catch (Exception e)
					{
						Global.Logger.LogError($"Cannot send an error to client via WebSocket => {e.Message}", e);
					}
				}
		}

		async Task ProcessInitializerRequestAsync(HttpContext context)
		{
			var redirectUrl = "";
			try
			{
				redirectUrl = context.GetQueryParameter("r").ToBase64(false, true).Decrypt(Global.EncryptionKey).ToArray('|').Last();
				if (string.IsNullOrWhiteSpace(redirectUrl))
					redirectUrl = context.GetReferUrl();
				redirectUrl += (redirectUrl.IndexOf("?") < 0 ? "?" : "&") + "r=";

				var userID = context.GetQueryParameter("u").ToBase64(false, true).Decrypt(Global.EncryptionKey).ToArray('|').Last();
				var isAuthenticated = context.GetQueryParameter("s").ToBase64(false, true).Decrypt(Global.EncryptionKey).ToArray('|').Last().CastAs<bool>();

				if (context.User.Identity.IsAuthenticated ? !isAuthenticated || !userID.Equals(context.User.Identity.Name) : isAuthenticated)
				{
					var session = context.GetSession();
					redirectUrl += $"&x-passport-token={session.GetAuthenticateToken(payload => payload["dev"] = session.DeviceID)}";
				}
			}
			catch (Exception ex)
			{
				await context.WriteLogsAsync("Http.Authentication", $"Error occurred while initializing => {ex.Message}", ex).ConfigureAwait(false);
			}

			context.Redirect(redirectUrl);
			await context.FlushAsync(Global.CancellationToken).ConfigureAwait(false);
		}

		async Task ProcessValidatorRequestAsync(HttpContext context)
		{
			try
			{
				var userID = context.GetQueryParameter("u").ToBase64(false, true).Decrypt(Global.EncryptionKey).ToArray('|').Last();
				var isAuthenticated = context.GetQueryParameter("s").ToBase64(false, true).Decrypt(Global.EncryptionKey).ToArray('|').Last().CastAs<bool>();

				var scripts = "/*still authenticated*/";
				var needProcess = context.User.Identity.IsAuthenticated
					? !isAuthenticated || !userID.Equals(context.User.Identity.Name)
					: isAuthenticated;

				if (needProcess)
				{
					var session = context.GetSession();
					var token = session.GetAuthenticateToken(payload => payload["dev"] = session.DeviceID);

					var callbackFunction = context.GetQueryParameter("c").ToBase64(false, true).Decrypt(Global.EncryptionKey).ToArray('|').Last();
					if (string.IsNullOrWhiteSpace(callbackFunction))
					{
						var redirectUrl = context.GetReferUrl();
						redirectUrl += (redirectUrl.IndexOf("?") < 0 ? "?" : "&") + $"x-passport-token={token}";
						scripts = $"location.href=\"{redirectUrl}\"";
					}
					else
						scripts = $"{callbackFunction}({JSONWebToken.DecodeAsJson(token, Global.JWTKey).ToString(Formatting.None)})";

					await context.WriteAsync(scripts, "application/javascript", context.GetCorrelationID(), Global.CancellationToken).ConfigureAwait(false);
				}
			}
			catch (Exception ex)
			{
				await Task.WhenAll(
					context.WriteAsync($"console.error('Error occurred while validating => {ex.Message.Replace("'", @"\'")}')", "application/javascript", context.GetCorrelationID(), Global.CancellationToken),
					context.WriteLogsAsync("Http.Authentication", $"Error occurred while validating => {ex.Message}", ex)
				).ConfigureAwait(false);
			}
		}

		async Task ProcessLogInRequestAsync(HttpContext context, string systemID)
		{
			var url = UtilityService.GetAppSetting("HttpUri:CMSPortals", "https://cms.vieapps.net");
			while (url.EndsWith("/"))
				url = url.Left(url.Length - 1);
			url += "/home?redirect=" + $"/portals/initializer?x-request={("{\"SystemID\":\"" + systemID + "\"}").Url64Encode()}".Url64Encode();
			context.Redirect(url);
			await context.FlushAsync(Global.CancellationToken).ConfigureAwait(false);
		}

		async Task ProcessLogOutRequestAsync(HttpContext context)
		{
			try
			{
				// sign-out
				var correlationID = context.GetCorrelationID();
				var session = context.GetSession();
				await context.CallServiceAsync(new RequestInfo(session, "Users", "Session", "DELETE")
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
				}, Global.CancellationToken).ConfigureAwait(false);

				await context.SignOutAsync().ConfigureAwait(false);
				context.User = new UserPrincipal();
				context.Session.Clear();

				// register new session
				session.User = new User("", "", new List<string> { SystemRole.All.ToString() }, new List<Privilege>())
				{
					SessionID = session.SessionID = UtilityService.NewUUID
				};

				var body = new JObject
				{
					{ "ID", session.SessionID },
					{ "IssuedAt", DateTime.Now },
					{ "RenewedAt", DateTime.Now },
					{ "ExpiredAt", DateTime.Now.AddDays(90) },
					{ "UserID", session.User.ID },
					{ "AccessToken", session.User.GetAccessToken(Global.ECCKey) },
					{ "IP", session.IP },
					{ "DeviceID", session.DeviceID },
					{ "AppInfo", session.AppName + " @ " + session.AppPlatform },
					{ "OSInfo", $"{session.AppAgent.GetOSInfo()} [{session.AppAgent}]" },
					{ "Verified", false },
					{ "Online", true }
				}.ToString(Formatting.None);

				await context.CallServiceAsync(new RequestInfo(session, "Users", "Session", "POST")
				{
					Body = body,
					Extra = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
					{
						{ "Signature", body.GetHMACSHA256(Global.ValidationKey) }
					},
					CorrelationID = correlationID
				}, Global.CancellationToken).ConfigureAwait(false);

				// prepare url for redirecting
				var token = session.GetAuthenticateToken(payload => payload["dev"] = session.DeviceID);
				var redirectUrl = context.GetQueryParameter("ReturnUrl");
				if (string.IsNullOrWhiteSpace(redirectUrl))
					redirectUrl = context.GetReferUrl();
				redirectUrl += (redirectUrl.IndexOf("?") < 0 ? "?" : "&") + $"x-passport-token={token}";
				context.Redirect(redirectUrl);
				await context.FlushAsync(Global.CancellationToken).ConfigureAwait(false);
			}
			catch (Exception ex)
			{
				await context.WriteLogsAsync("Http.Authentication", $"Error occurred while logging out => {ex.Message}", ex).ConfigureAwait(false);
				context.ShowHttpError(ex.GetHttpStatusCode(), ex.Message, ex.GetType().GetTypeName(true), context.GetCorrelationID(), ex, Global.IsDebugLogEnabled);
			}
		}

		#region Connect/Disconnect with API Gateway Router
		internal static void Connect(int waitingTimes = 6789)
		{
			Global.Logger.LogDebug($"Attempting to connect to API Gateway Router [{new Uri(Router.GetRouterStrInfo()).GetResolvedURI()}]");
			Global.Connect(
				async (sender, arguments) =>
				{
					Global.Logger.LogDebug($"Incoming channel to API Gateway Router is established - Session ID: {arguments.SessionId}");
					await Router.IncomingChannel.UpdateAsync(Router.IncomingChannelSessionID, Global.ServiceName, $"Incoming ({Global.ServiceName} HTTP service)").ConfigureAwait(false);
					Global.PrimaryInterCommunicateMessageUpdater?.Dispose();
					Global.PrimaryInterCommunicateMessageUpdater = Router.IncomingChannel?.RealmProxy.Services
						.GetSubject<CommunicateMessage>("messages.services.portals")
						.Subscribe(
							async message =>
							{
								var correlationID = UtilityService.NewUUID;
								try
								{
									await Handler.ProcessInterCommunicateMessageAsync(message).ConfigureAwait(false);
									if (Global.IsDebugResultsEnabled)
										await Global.WriteLogsAsync(Global.Logger, "RTU",
											$"Successfully process an inter-communicate message" + "\r\n" +
											$"- Type: {message?.Type}" + "\r\n" +
											$"- Message: {message?.Data?.ToString(Global.IsDebugLogEnabled ? Formatting.Indented : Formatting.None)}"
										, null, Global.ServiceName, LogLevel.Information, correlationID).ConfigureAwait(false);
								}
								catch (Exception ex)
								{
									await Global.WriteLogsAsync(Global.Logger, "RTU", $"{ex.Message} => {message?.ToJson().ToString(Global.IsDebugLogEnabled ? Formatting.Indented : Formatting.None)}", ex, Global.ServiceName, LogLevel.Error, correlationID).ConfigureAwait(false);
								}
							},
							async exception => await Global.WriteLogsAsync(Global.Logger, "RTU", $"{exception.Message}", exception).ConfigureAwait(false)
						);
					Global.SecondaryInterCommunicateMessageUpdater?.Dispose();
					Global.SecondaryInterCommunicateMessageUpdater = Router.IncomingChannel?.RealmProxy.Services
						.GetSubject<CommunicateMessage>("messages.services.apigateway")
						.Subscribe(
							async message =>
							{
								if (message.Type.IsEquals("Service#RequestInfo"))
								{
									var correlationID = UtilityService.NewUUID;
									try
									{
										await Global.SendServiceInfoAsync().ConfigureAwait(false);
										if (Global.IsDebugResultsEnabled)
											await Global.WriteLogsAsync(Global.Logger, "RTU",
												$"Successfully process an inter-communicate message" + "\r\n" +
												$"- Type: {message?.Type}" + "\r\n" +
												$"- Message: {message?.Data?.ToString(Global.IsDebugLogEnabled ? Formatting.Indented : Formatting.None)}"
											, null, Global.ServiceName, LogLevel.Information, correlationID).ConfigureAwait(false);
									}
									catch (Exception ex)
									{
										await Global.WriteLogsAsync(Global.Logger, "RTU", $"{ex.Message} => {message?.ToJson().ToString(Global.IsDebugLogEnabled ? Formatting.Indented : Formatting.None)}", ex, Global.ServiceName, LogLevel.Error, correlationID).ConfigureAwait(false);
									}
								}
							},
							async exception => await Global.WriteLogsAsync(Global.Logger, "RTU", $"{exception.Message}", exception).ConfigureAwait(false)
						);
				},
				async (sender, arguments) =>
				{
					Global.Logger.LogDebug($"Outgoing channel to API Gateway Router is established - Session ID: {arguments.SessionId}");
					await Router.OutgoingChannel.UpdateAsync(Router.OutgoingChannelSessionID, Global.ServiceName, $"Outgoing ({Global.ServiceName} HTTP service)").ConfigureAwait(false);
					await Global.RegisterServiceAsync().ConfigureAwait(false);
				},
				waitingTimes
			);
		}

		internal static void Disconnect(int waitingTimes = 1234)
		{
			Global.UnregisterService(null, waitingTimes);
			Global.PrimaryInterCommunicateMessageUpdater?.Dispose();
			Global.SecondaryInterCommunicateMessageUpdater?.Dispose();
			Global.Disconnect();
		}
		#endregion

	}
}
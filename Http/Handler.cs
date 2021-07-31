﻿#region Related components
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
		static HashSet<string> Initializers { get; } = "_initializer,initializer.aspx".ToHashSet();

		static HashSet<string> Validators { get; } = "_validator,validator.aspx".ToHashSet();

		static HashSet<string> LogIns { get; } = "_login,login.aspx,signin.aspx".ToHashSet();

		static HashSet<string> LogOuts { get; } = "_logout,logout.aspx,signout.aspx".ToHashSet();

		static HashSet<string> CmsPortals { get; } = "_admin,admin.aspx,admin.html,_cms,cms.aspx,cms.html".ToHashSet();

		static bool UseShortURLs => "true".IsEquals(UtilityService.GetAppSetting("Portals:UseShortURLs", "true"));

		static string LoadBalancingHealthCheckUrl { get; } = UtilityService.GetAppSetting("HealthCheckUrl", "/load-balancing-health-check");

		internal static Components.WebSockets.WebSocket WebSocket { get; private set; }

		internal static string NodeName => Extensions.GetUniqueName(Global.ServiceName + ".http");

		public static List<string> ExcludedHeaders { get; } = UtilityService.GetAppSetting("ExcludedHeaders", "connection,accept,accept-encoding,accept-language,cache-control,cookie,host,content-type,content-length,user-agent,upgrade-insecure-requests,purpose,ms-aspnetcore-token,x-forwarded-for,x-forwarded-proto,x-forwarded-port,x-original-for,x-original-proto,x-original-remote-endpoint,x-original-port,cdn-loop").ToList();

		internal static Cache Cache { get; } = new Cache("VIEApps-Services-Portals", Cache.Configuration.ExpirationTime, Cache.Configuration.Provider, Logger.GetLoggerFactory());

		internal static string RefresherRefererURL => "https://portals.vieapps.net/~url.refresher";

		internal static int ExpiresAfter { get; } = Int32.TryParse(UtilityService.GetAppSetting("Portals:ExpiresAfter", "0"), out var expiresAfter) && expiresAfter > -1 ? expiresAfter : 0;

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
						["Access-Control-Allow-Methods"] = "HEAD,GET,POST,PUT,PATCH"
					};
					if (context.Request.Headers.TryGetValue("Access-Control-Request-Headers", out var requestHeaders))
						headers["Access-Control-Allow-Headers"] = requestHeaders;
					context.SetResponseHeaders((int)HttpStatusCode.OK, headers);
				}

				// load balancing health check
				else if (context.Request.Path.Value.IsEquals(Handler.LoadBalancingHealthCheckUrl))
					await context.WriteAsync("OK", "text/plain", null, 0, null, TimeSpan.Zero).ConfigureAwait(false);

				// process portals' requests
				else
					await this.ProcessHttpRequestAsync(context).ConfigureAwait(false);
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
			var requestMsg = result.MessageType.Equals(WebSocketMessageType.Text) ? data.GetString() : null;
			if (string.IsNullOrWhiteSpace(requestMsg))
				return;

			var correlationID = UtilityService.NewUUID;
			var stopwatch = Stopwatch.StartNew();
			var requestObj = requestMsg.ToExpandoObject();
			var requestID = requestObj.Get<string>("ID");

			var serviceName = requestObj.Get("ServiceName", "");
			var objectName = requestObj.Get("ObjectName", "");
			var verb = requestObj.Get("Verb", "GET").ToUpper();
			var query = new Dictionary<string, string>(requestObj.Get("Query", new Dictionary<string, string>()), StringComparer.OrdinalIgnoreCase);
			var header = new Dictionary<string, string>(requestObj.Get("Header", new Dictionary<string, string>()), StringComparer.OrdinalIgnoreCase);
			var body = requestObj.Get("Body")?.ToExpandoObject();
			var extra = new Dictionary<string, string>(requestObj.Get("Extra", new Dictionary<string, string>()), StringComparer.OrdinalIgnoreCase);
			query.TryGetValue("object-identity", out var objectIdentity);

			var session = websocket.Get<Session>("Session") ?? Global.GetSession();

			try
			{
				// visit logs
				if (Global.IsVisitLogEnabled)
					await Global.WriteLogsAsync(Global.Logger, "Http.Visits",
						$"Request starting {verb} " + $"/{serviceName}{(string.IsNullOrWhiteSpace(objectName) ? "" : $"/{objectName}")}{(string.IsNullOrWhiteSpace(objectIdentity) ? "" : $"/{objectIdentity}")}".ToLower() + (query.TryGetValue("x-request", out var xrequest) ? $"?x-request={xrequest}" : "") + " HTTPWS/1.1" + " \r\n" +
						$"- App: {session.AppName ?? "Unknown"} @ {session.AppPlatform ?? "Unknown"} [{session.AppAgent ?? "Unknown"}]" + " \r\n" +
						$"- WebSocket: {websocket.ID} @ {websocket.RemoteEndPoint}"
					, null, Global.ServiceName, LogLevel.Information, correlationID).ConfigureAwait(false);

				// register/authenticate a session
				if (serviceName.IsEquals("Session") && (verb.IsEquals("REG") || verb.IsEquals("AUTH")))
				{
					session.AppName = header.TryGetValue("x-app-name", out var appName) && !string.IsNullOrWhiteSpace(appName) ? appName : session.AppName;
					session.AppPlatform = header.TryGetValue("x-app-platform", out var appPlatform) && !string.IsNullOrWhiteSpace(appPlatform) ? appPlatform : session.AppPlatform;

					if (verb.IsEquals("REG"))
					{
						session.DeviceID = header.TryGetValue("x-device-id", out var deviceID) && !string.IsNullOrWhiteSpace(deviceID) ? deviceID : string.IsNullOrWhiteSpace(session.DeviceID) ? $"{UtilityService.NewUUID}@web" : session.DeviceID;
						websocket.Set("Status", "Registered");
					}

					else
					{
						var appToken = body?.Get<string>("x-app-token") ?? "";
						await Global.UpdateWithAuthenticateTokenAsync(session, appToken, Handler.ExpiresAfter, null, null, null, Global.Logger, "Http.Authentication", correlationID).ConfigureAwait(false);
						if (!string.IsNullOrWhiteSpace(session.User.ID) && !await session.IsSessionExistAsync(Global.Logger, "Http.Authentication", correlationID).ConfigureAwait(false))
							throw new InvalidSessionException("Session is invalid (The session is not issued by the system)");

						var encryptionKey = session.GetEncryptionKey(Global.EncryptionKey);
						var encryptionIV = session.GetEncryptionIV(Global.EncryptionKey);
						if (!header.TryGetValue("x-session-id", out var sessionID) || !sessionID.Decrypt(encryptionKey, encryptionIV).Equals(session.GetEncryptedID())
							|| !header.TryGetValue("x-device-id", out var deviceID) || !deviceID.Decrypt(encryptionKey, encryptionIV).Equals(session.DeviceID))
							throw new InvalidSessionException("Session is invalid (The session is not issued by the system)");

						session.AppName = body?.Get<string>("x-app-name") ?? session.AppName;
						session.AppPlatform = body?.Get<string>("x-app-platform") ?? session.AppPlatform;
						websocket.Set("Status", "Authenticated");
						websocket.Set("Token", JSONWebToken.DecodeAsJson(appToken, Global.JWTKey));
					}

					websocket.Set("Session", session);
					await websocket.PrepareConnectionInfoAsync(correlationID, session, Global.CancellationToken, Global.Logger).ConfigureAwait(false);
					if (Global.IsDebugLogEnabled)
						await Global.WriteLogsAsync(Global.Logger, "Http.Authentication", $"Successfully {(verb.IsEquals("REG") ? "register" : "authenticate")} a WebSocket connection\r\n{websocket.GetConnectionInfo(session)}\r\n- Status: {websocket.Get<string>("Status")}", null, Global.ServiceName, LogLevel.Information, correlationID).ConfigureAwait(false);
				}

				// call a service of APIs
				else
				{
					var requestInfo = new RequestInfo(session, serviceName, objectName, verb, query, header, body?.ToJson().ToString(Formatting.None), extra, correlationID);
					var response = new JObject
					{
						{ "ID", requestID },
						{ "Type", $"{serviceName.GetCapitalizedFirstLetter()}#{objectName.GetCapitalizedFirstLetter()}#{verb.GetCapitalizedFirstLetter()}" },
						{ "Data", await Global.CallServiceAsync(requestInfo, Global.CancellationToken, Global.Logger, "Http.Process.Requests").ConfigureAwait(false) }
					};
					await websocket.SendAsync(response, Global.CancellationToken).ConfigureAwait(false);
				}
			}
			catch (Exception ex)
			{
				try
				{
					var code = ex.GetHttpStatusCode();
					var message = ex.Message;
					var type = ex.GetTypeName(true);
					var stacks = ex.GetStacks();
					if (ex is WampException wampException)
					{
						var wampDetails = wampException.GetDetails();
						code = wampDetails.Item1;
						message = wampDetails.Item2;
						type = wampDetails.Item3;
						stacks = new JArray { wampDetails.Item4 };
						var inner = wampDetails.Item6;
						while (inner != null)
						{
							stacks.Add($"{inner.Get<string>("Message")} [{inner.Get<string>("Type")}] {inner.Get<string>("StackTrace")}");
							inner = inner.Get<JObject>("InnerException");
						}
					}
					var response = new JObject
					{
						{ "ID", requestID },
						{ "Type", "Error" },
						{ "Data", new JObject
							{
								{ "Code", code },
								{ "Message", message },
								{ "Type", type },
								{ "StackTrace", stacks },
								{ "CorrelationID", correlationID }
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
			finally
			{
				stopwatch.Stop();
				if (Global.IsVisitLogEnabled)
					await Global.WriteLogsAsync(Global.Logger, "Http.Visits", $"Request finished in {stopwatch.GetElapsedTimes()}", null, Global.ServiceName, LogLevel.Information, correlationID).ConfigureAwait(false);
			}
		}

		internal async Task ProcessHttpRequestAsync(HttpContext context)
		{
			// prepare
			context.SetItem("PipelineStopwatch", Stopwatch.StartNew());
			var requestPath = context.GetRequestPathSegments(true).First();

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
			// prepare
			var correlationID = context.GetCorrelationID();

			var session = context.Session.Get<Session>("Session") ?? context.GetSession();

			var appName = context.GetParameter("x-app-name");
			if (!string.IsNullOrWhiteSpace(appName))
				try
				{
					session.AppName = appName.Url64Decode();
				}
				catch
				{
					session.AppName = appName;
				}

			var appPlatform = context.GetParameter("x-app-platform");
			if (!string.IsNullOrWhiteSpace(appPlatform))
				try
				{
					session.AppPlatform = appPlatform.Url64Decode();
				}
				catch
				{
					session.AppPlatform = appPlatform;
				}

			var deviceID = context.GetParameter("x-device-id");
			if (!string.IsNullOrWhiteSpace(deviceID))
				try
				{
					session.DeviceID = deviceID.Url64Decode();
				}
				catch
				{
					session.DeviceID = deviceID;
				}

			// update user of the session if already signed-in
			if (context.IsAuthenticated())
			{
				if (string.IsNullOrWhiteSpace(session.User.ID) && string.IsNullOrWhiteSpace(session.User.SessionID))
				{
					session.User = context.GetUser();
					session.SessionID = session.User.SessionID = !string.IsNullOrWhiteSpace(session.SessionID)
						? session.SessionID
						: UtilityService.NewUUID;
				}
				else
				{
					session.SessionID = session.User.SessionID = !string.IsNullOrWhiteSpace(session.User.SessionID)
						? session.User.SessionID
						: !string.IsNullOrWhiteSpace(session.SessionID)
							? session.SessionID
							: UtilityService.NewUUID;
					context.User = new UserPrincipal(session.User);
				}

				if (Global.IsDebugLogEnabled)
					await context.WriteLogsAsync(Global.Logger, "Http.Authentication", $"Successfully update an user with authenticate ticket {session.ToJson()}").ConfigureAwait(false);
			}

			// update with authenticate token
			else
			{
				// prepare token
				var authenticateToken = context.GetParameter("x-app-token");
				if (string.IsNullOrWhiteSpace(authenticateToken))
				{
					authenticateToken = context.GetHeaderParameter("authorization");
					authenticateToken = authenticateToken != null && authenticateToken.IsStartsWith("Bearer") ? authenticateToken.ToArray(" ").Last() : null;
				}

				// authenticate the session
				if (!string.IsNullOrWhiteSpace(authenticateToken))
					try
					{
						// authenticate
						await context.UpdateWithAuthenticateTokenAsync(session, authenticateToken, Handler.ExpiresAfter, null, null, null, Global.Logger, "Http.Authentication", correlationID).ConfigureAwait(false);
						if (Global.IsDebugLogEnabled)
							await context.WriteLogsAsync(Global.Logger, "Http.Authentication", $"Successfully authenticate an user with authenticate token {session.ToJson().ToString(Formatting.Indented)}").ConfigureAwait(false);

						// assign user information
						context.User = new UserPrincipal(session.User);
					}
					catch (Exception ex)
					{
						await context.WriteLogsAsync(Global.Logger, "Http.Authentication", $"Failure authenticate an user with authenticate token => {ex.Message}", ex, Global.ServiceName, LogLevel.Error).ConfigureAwait(false);
					}

				// update identities
				else
					session.SessionID = session.User.SessionID = !string.IsNullOrWhiteSpace(session.User.SessionID)
						? session.User.SessionID
						: !string.IsNullOrWhiteSpace(session.SessionID)
							? session.SessionID
							: UtilityService.NewUUID;
			}

			// update session into storages
			if (context.Session.ContainsKey("Session"))
				context.Session.Add("Session", session);
			context.SetSession(session);

			// prepare the requesting information
			var requestURI = context.GetRequestUri();
			var isMobile = string.IsNullOrWhiteSpace(session.AppPlatform) || session.AppPlatform.IsContains("Desktop") ? "false" : "true";
			var osInfo = (session.AppAgent ?? "").GetOSInfo();

			var systemIdentity = string.Empty;
			var specialRequest = string.Empty;
			var legacyRequest = string.Empty;

			var queryString = context.Request.QueryString.ToDictionary(query =>
			{
				var pathSegments = context.GetRequestPathSegments().Where(segment => !segment.IsEquals("desktop.aspx") && !segment.IsEquals("default.aspx") && !segment.IsEquals("index.aspx") && !segment.IsEquals("index.php")).ToArray();
				var requestSegments = pathSegments;
				var firstPathSegment = pathSegments.Length > 0 ? pathSegments[0].ToLower() : "";

				// special parameters (like spider indicator (robots.txt)/ads indicator (ads.txt) or system/organization identity)
				if (!string.IsNullOrWhiteSpace(firstPathSegment))
				{
					// system/oranization identity or service
					if (firstPathSegment.StartsWith("~"))
					{
						// specifict service
						if (requestSegments[0].IsEquals("~apis.service") || requestSegments[0].IsEquals("~apis.gateway"))
							specialRequest = "service";
						else
						{
							systemIdentity = firstPathSegment.Right(firstPathSegment.Length - 1).Replace(StringComparison.OrdinalIgnoreCase, ".html", "").GetANSIUri(true, false);
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

						// special resources
						else
						{
							systemIdentity = "~resources";
							query["x-resource"] = firstPathSegment.Right(firstPathSegment.Length - 1).GetANSIUri(true, true);
							query["x-path"] = pathSegments.Skip(1).Join("/");
						}

						// no info
						requestSegments = Array.Empty<string>();
					}

					// HTTP indicator
					else if (firstPathSegment.IsEndsWith(".txt"))
					{
						systemIdentity = "~indicators";
						query["x-indicator"] = firstPathSegment.Replace(StringComparison.OrdinalIgnoreCase, ".txt", "").ToLower();
						requestSegments = Array.Empty<string>();
					}
				}

				// normalize info of requests
				if (requestSegments.Length > 0 && specialRequest.IsEquals(""))
				{
					var firstRequestSegment = requestSegments[0].ToLower();

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

					else if (Handler.CmsPortals.Contains(firstRequestSegment))
					{
						specialRequest = "cms";
						requestSegments = Array.Empty<string>();
					}

					// request of legacy systems
					else if (firstRequestSegment.IsEndsWith(".ashx") || firstRequestSegment.IsEndsWith(".aspx"))
						legacyRequest = requestSegments.Join("/");

					// parameters of desktop and contents
					else
					{
						var value = firstRequestSegment.Replace(StringComparison.OrdinalIgnoreCase, ".html", "");
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
			if (!httpVerb.IsEquals("GET") && !specialRequest.IsEquals("login") && !specialRequest.IsEquals("service"))
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
					using var cts = CancellationTokenSource.CreateLinkedTokenSource(Global.CancellationToken, context.RequestAborted);

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
								// redirect (always HTTPS or None WWW)
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
											{ "X-Cache", "HTTP" },
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
										{ "X-Cache", "HTTP" },
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
							await context.WriteAsync(response.Get("BodyAsPlainText", false) ? body.ToBytes() : body.Base64ToBytes().Decompress(response.Get("BodyEncoding", "gzip")), cts.Token).ConfigureAwait(false);
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

			else
				switch (specialRequest)
				{
					case "initializer":
						if (context.Request.Path.Value.IsEndsWith(".aspx"))
							systemIdentityJson = systemIdentityJson ?? await context.CallServiceAsync(requestInfo, Global.CancellationToken, Global.Logger, "Http.Authentication").ConfigureAwait(false) as JObject;
						await this.ProcessInitializerRequestAsync(context, systemIdentityJson).ConfigureAwait(false);
						break;

					case "validator":
						await this.ProcessValidatorRequestAsync(context).ConfigureAwait(false);
						break;

					case "login":
						if (context.Request.Path.Value.IsEndsWith(".aspx") || !context.Request.Method.IsEquals("GET"))
							systemIdentityJson = systemIdentityJson ?? await context.CallServiceAsync(requestInfo, Global.CancellationToken, Global.Logger, "Http.Authentication").ConfigureAwait(false) as JObject;
						await this.ProcessLogInRequestAsync(context, systemIdentityJson).ConfigureAwait(false);
						break;

					case "logout":
						if (context.Request.Path.Value.IsEndsWith(".aspx"))
							systemIdentityJson = systemIdentityJson ?? await context.CallServiceAsync(requestInfo, Global.CancellationToken, Global.Logger, "Http.Authentication").ConfigureAwait(false) as JObject;
						await this.ProcessLogOutRequestAsync(context, systemIdentityJson).ConfigureAwait(false);
						break;

					case "cms":
						try
						{
							systemIdentityJson = systemIdentityJson ?? await context.CallServiceAsync(requestInfo, Global.CancellationToken, Global.Logger, "Http.Process.Requests").ConfigureAwait(false) as JObject;
							await this.ProcessCmsPortalsRequestAsync(context, systemIdentityJson?.Get<string>("ID")).ConfigureAwait(false);
						}
						catch (Exception ex)
						{
							if (ex is WampException wampException)
							{
								var wampDetails = wampException.GetDetails(requestInfo);
								context.ShowHttpError(wampDetails.Item1, wampDetails.Item2, wampDetails.Item3, correlationID, wampDetails.Item4 + "\r\n\t" + ex.StackTrace, Global.IsDebugLogEnabled);
							}
							else
								context.ShowHttpError(ex.GetHttpStatusCode(), ex.Message, ex.GetTypeName(true), correlationID, ex, Global.IsDebugLogEnabled);
							await context.WriteLogsAsync("Http.Process.Requests", $"Error occurred while redirecting to CMS Portals => {ex.Message}", ex).ConfigureAwait(false);
						}
						break;

					case "service":
						requestInfo = new RequestInfo(requestInfo)
						{
							ServiceName = requestInfo.Query["service-name"],
							ObjectName = requestInfo.Query["object-name"],
							Verb = httpVerb
						};
						try
						{
							using var cts = CancellationTokenSource.CreateLinkedTokenSource(Global.CancellationToken, context.RequestAborted);
							var response = await context.CallServiceAsync(requestInfo, cts.Token, Global.Logger, "Http.Services").ConfigureAwait(false);
							await Task.WhenAll
							(
								context.WriteAsync(response, cts.Token),
								Global.IsDebugLogEnabled ? context.WriteLogsAsync(Global.Logger, "Http.Services", $"Successfully process request of a service {response}") : Task.CompletedTask
							).ConfigureAwait(false);
						}
						catch (Exception ex)
						{
							context.WriteError(Global.Logger, ex, requestInfo, $"Error occurred while calling a service => {ex.Message}", true, "Http.Services");
						}
						break;

					default:
						var invalidException = new InvalidRequestException();
						context.ShowHttpError(invalidException.GetHttpStatusCode(), invalidException.Message, invalidException.GetType().GetTypeName(true), correlationID, invalidException, Global.IsDebugLogEnabled);
						break;
				}
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
						}, cts.Token, Global.Logger, "Http.Authentication").ConfigureAwait(false);

						await Task.WhenAll
						(
							Global.Cache.RemoveAsync($"Attempt#{context.Connection.RemoteIpAddress}", cts.Token),
							Global.IsDebugLogEnabled ? context.WriteLogsAsync(Global.Logger, "Http.Authentication", $"Successfully activate {context.Request.QueryString.ToDictionary().ToJson()}") : Task.CompletedTask
						).ConfigureAwait(false);
					}
					catch (Exception ex)
					{
						await context.WriteLogsAsync("Http.Authentication", $"Error occurred while activating => {ex.Message}", ex).ConfigureAwait(false);
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
					$(window).on('load', function() {
						__vieapps.session.events.in = __redirect;
						__vieapps.session.events.close = __redirect;
						__vieapps.session.activate(" + $"\"{mode}\", {error}" + @");
					}); 
					</script>";
					await context.WriteAsync(this.GetSpecialHtml(context, systemIdentityJson, "Activate").Replace("[[placeholder]]", scripts.Replace("\t\t\t\t\t", "")), "text/html", null, 0, "private, no-store, no-cache", TimeSpan.Zero, correlationID, cts.Token).ConfigureAwait(false);
				}
				catch (Exception ex)
				{
					await context.WriteLogsAsync("Http.Authentication", $"Error occurred while activating => {ex.Message}", ex).ConfigureAwait(false);
					var code = ex.GetHttpStatusCode();
					var message = ex.Message;
					var type = ex.GetTypeName(true);
					if (ex is WampException wampException)
					{
						var details = wampException.GetDetails();
						code = details.Item1;
						message = details.Item2;
						type = details.Item3;
					}
					context.ShowHttpError(code, message, type, correlationID, ex, Global.IsDebugLogEnabled);
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
				context.Redirect(this.GetURL(context));
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
					scripts = $"{callbackFunction ?? "console.warn"}({session.GetSessionJson(payload => payload["did"] = session.DeviceID).ToString(Formatting.None)})";
				}
				else if (!session.User.IsAuthenticated && context.IsAuthenticated())
				{
					await context.SignOutAsync().ConfigureAwait(false);
					scripts = $"{callbackFunction ?? "console.warn"}({session.GetSessionJson(payload => payload["did"] = session.DeviceID).ToString(Formatting.None)})";
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
					context.WriteLogsAsync("Http.Authentication", $"Error occurred while validating => {ex.Message}", ex)
				).ConfigureAwait(false);
			}
		}

		async Task ProcessLogInRequestAsync(HttpContext context, JObject systemIdentityJson)
		{
			var correlationID = context.GetCorrelationID();

			async Task registerAsync()
			{
				try
				{
					var session = context.Session.Get<Session>("Session") ?? context.GetSession();
					session.DeviceID = string.IsNullOrWhiteSpace(session.DeviceID) ? $"{UtilityService.NewUUID}@web" : session.DeviceID;
					session.SessionID = session.User.SessionID = !string.IsNullOrWhiteSpace(session.User.SessionID)
						? session.User.SessionID
						: !string.IsNullOrWhiteSpace(session.SessionID)
							? session.SessionID
							: UtilityService.NewUUID;
					context.Session.Add("Session", session);
					context.SetSession(session);
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
					}, cts.Token, Global.Logger, "Http.Authentication").ConfigureAwait(false);
					await Task.WhenAll
					(
						context.WriteAsync(session.GetSessionJson(payload => payload["did"] = session.DeviceID), Formatting.Indented, correlationID, cts.Token),
						Global.IsDebugLogEnabled ? context.WriteLogsAsync(Global.Logger, "Http.Authentication", $"Successfully register a new session {response}") : Task.CompletedTask
					).ConfigureAwait(false);
				}
				catch (Exception ex)
				{
					context.WriteError(Global.Logger, ex, null, $"Error occurred while registering a new session => {ex.Message}", true, "Http.Authentication");
				}
			}

			async Task showAsync()
			{
				var scripts = @"<script>
				$(window).on('load', function() {
					__vieapps.session.register(function() {
						if (__vieapps.session.logged) {
							__redirect();
						}
						else {
							__vieapps.session.events.in = __redirect;
							__vieapps.session.events.close = __redirect;
							__vieapps.session.open();
						}
					});
				}); 
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
					}, cts.Token, Global.Logger, "Http.Authentication").ConfigureAwait(false);

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
						}, cts.Token, Global.Logger, "Http.Authentication").ConfigureAwait(false);

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
						Global.IsDebugLogEnabled ? context.WriteLogsAsync(Global.Logger, "Http.Authentication", $"Successfully log a session in {response}") : Task.CompletedTask
					).ConfigureAwait(false);
				}
				catch (Exception ex)
				{
					await context.WaitOnAttemptedAsync().ConfigureAwait(false);
					context.WriteError(Global.Logger, ex);
				}
			}

			async Task loginOtpAsync()
			{
				try
				{
					// prepare
					var session = context.Session.Get<Session>("Session");
					if (session == null || !session.GetEncryptedID().IsEquals(context.Request.Query["x-session-id"]) || !session.DeviceID.Url64Encode().IsEquals(context.Request.Query["x-device-id"]))
						throw new InvalidSessionException("Session is invalid (The session is not issued by the system)");

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
					}, cts.Token, Global.Logger, "Http.Authentication").ConfigureAwait(false);

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
					}, cts.Token, Global.Logger, "Http.Authentication").ConfigureAwait(false);

					// update authenticate ticket
					var userPrincipal = new UserPrincipal(new UserIdentity(session.User.ID, session.SessionID, CookieAuthenticationDefaults.AuthenticationScheme));
					await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, userPrincipal, new AuthenticationProperties { IsPersistent = false }).ConfigureAwait(false);
					context.Session.Add("Session", session);

					// response
					await Task.WhenAll
					(
						Global.Cache.RemoveAsync($"Attempt#{context.Connection.RemoteIpAddress}", cts.Token),
						context.WriteAsync(session.GetSessionJson(payload => payload["did"] = session.DeviceID), Formatting.Indented, correlationID, cts.Token),
						Global.IsDebugLogEnabled ? context.WriteLogsAsync(Global.Logger, "Http.Authentication", $"Successfully log a session in with OTP {response}") : Task.CompletedTask
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
					}, cts.Token, Global.Logger, "Http.Authentication").ConfigureAwait(false);

					// response
					await Task.WhenAll
					(
						Global.Cache.RemoveAsync($"Attempt#{context.Connection.RemoteIpAddress}", cts.Token),
						context.WriteAsync(response, Formatting.Indented, correlationID, cts.Token),
						Global.IsDebugLogEnabled ? context.WriteLogsAsync(Global.Logger, "Http.Authentication", $"Successfully send a renew password request {response}") : Task.CompletedTask
					).ConfigureAwait(false);
				}
				catch (Exception ex)
				{
					await context.WaitOnAttemptedAsync().ConfigureAwait(false);
					context.WriteError(Global.Logger, ex);
				}
			}

			switch (context.Request.Method)
			{
				// log a session in or others
				case "POST":
					switch ((context.GetQueryParameter("x-mode") ?? "login").ToUpper())
					{
						// log a session in with OTP
						case "OTP":
							await loginOtpAsync().ConfigureAwait(false);
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
					await loginOtpAsync().ConfigureAwait(false);
					break;

				// forgot password
				case "PATCH":
					await resetAsync().ConfigureAwait(false);
					break;

				// register a session in or open login form
				default:
					await (context.Request.Path.Value.IsEndsWith(".aspx") ? showAsync() : registerAsync()).ConfigureAwait(false);
					break;
			}
		}

		async Task ProcessLogOutRequestAsync(HttpContext context, JObject systemIdentityJson)
		{
			var correlationID = context.GetCorrelationID();
			try
			{
				// call service to delete the session
				var session = context.GetSession();
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
				}, cts.Token, Global.Logger, "Http.Authentication").ConfigureAwait(false);

				// perform log out
				await context.SignOutAsync().ConfigureAwait(false);

				// response
				if (context.Request.Path.Value.IsEndsWith(".aspx"))
				{
					var scripts = @"<script>
					$(window).on('load', function() {
						__vieapps.session.unregister();
						__redirect();
					}); 
					</script>";
					await Task.WhenAll
					(
						context.WriteAsync(this.GetSpecialHtml(context, systemIdentityJson, "Log out").Replace("[[placeholder]]", scripts.Replace("\t\t\t\t\t", "")), "text/html", null, 0, "private, no-store, no-cache", TimeSpan.Zero, correlationID, cts.Token),
						Global.IsDebugLogEnabled ? context.WriteLogsAsync(Global.Logger, "Http.Authentication", $"Successfully log a session out (direct) {response}") : Task.CompletedTask
					).ConfigureAwait(false);
				}
				else
				{
					session.User = new User("", "", new List<string> { SystemRole.All.ToString() }, new List<Privilege>())
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
					}, cts.Token, Global.Logger, "Http.Authentication").ConfigureAwait(false);
					context.Session.Add("Session", session);
					await Task.WhenAll
					(
						context.WriteAsync(session.GetSessionJson(payload => payload["did"] = session.DeviceID), Formatting.Indented, correlationID, cts.Token),
						Global.IsDebugLogEnabled ? context.WriteLogsAsync(Global.Logger, "Http.Authentication", $"Successfully log a session out {response}") : Task.CompletedTask
					).ConfigureAwait(false);
				}
			}
			catch (Exception ex)
			{
				if (context.Request.Path.Value.IsEndsWith(".aspx"))
				{
					await context.WriteLogsAsync("Http.Authentication", $"Error occurred while logging out => {ex.Message}", ex).ConfigureAwait(false);
					var code = ex.GetHttpStatusCode();
					var message = ex.Message;
					var type = ex.GetTypeName(true);
					if (ex is WampException wampException)
					{
						var details = wampException.GetDetails();
						code = details.Item1;
						message = details.Item2;
						type = details.Item3;
					}
					context.ShowHttpError(code, message, type, correlationID, ex, Global.IsDebugLogEnabled);
				}
				else
					context.WriteError(Global.Logger, ex);
			}
		}

		async Task ProcessCmsPortalsRequestAsync(HttpContext context, string systemID)
		{
			var url = UtilityService.GetAppSetting("HttpUri:CMSPortals", "https://cms.vieapps.net");
			while (url.EndsWith("/"))
				url = url.Left(url.Length - 1);
			url += "/home?redirect=" + $"/portals/initializer?x-request={("{\"SystemID\":\"" + systemID + "\"}").Url64Encode()}".Url64Encode();
			context.Redirect(url);
			await context.FlushAsync(Global.CancellationToken).ConfigureAwait(false);
		}

		#region Connect/Disconnect with API Gateway Router
		internal static void Connect(int waitingTimes = 6789)
		{
			Global.Logger.LogDebug($"Attempting to connect to API Gateway Router [{new Uri(Router.GetRouterStrInfo()).GetResolvedURI()}]");
			Global.Connect(
				(sender, arguments) =>
				{
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
				async (sender, arguments) => await Global.RegisterServiceAsync().ConfigureAwait(false),
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

		string GetURL(HttpContext context, string name = null)
		{
			var url = context.GetQueryParameter(name ?? "ReturnURL");
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
			return url;
		}

		string GetSpecialHtml(HttpContext context, JObject systemIdentityJson, string title = "Log in")
		{
			var organizationID = systemIdentityJson.Get<string>("ID");
			var organizationAlias = systemIdentityJson.Get<string>("Alias");
			var portalsHttpURI = systemIdentityJson.Get<string>("PortalsHttpURI");
			while (portalsHttpURI.EndsWith("/"))
				portalsHttpURI = portalsHttpURI.Left(portalsHttpURI.Length - 1);

			var rootURL = context.GetRequestPathSegments().First().StartsWith("~") ? "./" : "/";
			var language = context.GetQueryParameter("language") ?? "en-US";

			var session = context.GetSession();
			var isMobile = string.IsNullOrWhiteSpace(session.AppPlatform) || session.AppPlatform.IsContains("Desktop") ? "false" : "true";
			var osInfo = (session.AppAgent ?? "").GetOSInfo();

			var version = DateTime.Now.GetTimeQuarter().ToUnixTimestamp().ToString();
			var scripts = "<script>__vieapps={ids:{" + $"system:\"{organizationID}\"" + "},URLs:{root:" + $"\"{rootURL}\",portals:\"{portalsHttpURI}\"" + "}" + $",language:\"{language}\",isMobile:{isMobile},osInfo:\"{osInfo}\",correlationID:\"{context.GetCorrelationID()}\"" + "};</script>"
				+ $"<script src=\"{UtilityService.GetAppSetting("Portals:Desktops:Resources:JQuery", "https://cdnjs.cloudflare.com/ajax/libs/jquery/3.6.0/jquery.min.js")}\"></script>"
				+ $"<script src=\"{UtilityService.GetAppSetting("Portals:Desktops:Resources:CryptoJs", "https://cdnjs.cloudflare.com/ajax/libs/crypto-js/4.0.0/crypto-js.min.js")}\"></script>"
				+ $"<script src=\"{portalsHttpURI}/_assets/rsa.js?v={version}\"></script>"
				+ $"<script src=\"{portalsHttpURI}/_assets/default.js?v={version}\"></script>"
				+ $"<script src=\"{portalsHttpURI}/_themes/default/js/all.js?v={version}\"></script>"
				+ $"<script src=\"{portalsHttpURI}/_js/o_{organizationID}.js?v={version}\"></script>"
				+ @"
					<script>
					function __redirect() {
						location.href = '" + this.GetURL(context) + @"';
					}
					</script>";

			return @$"<!DOCTYPE html>
					<html xmlns=""http://www.w3.org/1999/xhtml"">
					<head>{(rootURL.Equals("/") ? "" : $"<base href=\"{portalsHttpURI}/~{organizationAlias}/\"/>")}
					<title>{title} ({organizationAlias})</title>
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
	}
}
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
		static HashSet<string> Validators { get; } = "_validator,validator.aspx".ToHashSet();

		static HashSet<string> Initializers { get; } = "_initializer,_activate,initializer.aspx,activate.aspx,initializer.html,activate.html,initializer.php,activate.php".ToHashSet();

		static HashSet<string> LogIns { get; } = "_login,login.aspx,signin.aspx,login.html,signin.html,login.php,signin.php".ToHashSet();

		static HashSet<string> LogOuts { get; } = "_logout,logout.aspx,signout.aspx,logout.html,signout.html,logout.php,signout.php".ToHashSet();

		static HashSet<string> CmsPortals { get; } = "_admin,_cms,admin.aspx,cms.aspx,admin.html,cms.html,admin.php,cms.php".ToHashSet();

		static HashSet<string> Feeds { get; } = "feed,feed.xml,feed.json,atom,atom.xml,atom.json,rss,rss.xml,rss.json".ToHashSet();

		static bool UseShortURLs { get; } = "true".IsEquals(UtilityService.GetAppSetting("Portals:UseShortURLs", "true"));

		static string LoadBalancingHealthCheckUrl { get; } = UtilityService.GetAppSetting("HealthCheckUrl", "/load-balancing-health-check");

		internal static Components.WebSockets.WebSocket WebSocket { get; private set; }

		internal static string NodeName => Extensions.GetUniqueName(Global.ServiceName + ".http");

		public static List<string> ExcludedHeaders { get; } = UtilityService.GetAppSetting("ExcludedHeaders", "connection,accept,accept-encoding,accept-language,cache-control,cookie,host,content-type,content-length,user-agent,upgrade-insecure-requests,purpose,ms-aspnetcore-token,x-forwarded-for,x-forwarded-proto,x-forwarded-port,x-original-for,x-original-proto,x-original-remote-endpoint,x-original-port,cdn-loop").ToList();

		internal static Cache Cache { get; } = new Cache(UtilityService.GetAppSetting("Portals:Cache:Name", "VIEApps-Services-Portals"), Cache.Configuration.ExpirationTime, Cache.Configuration.Provider, Logger.GetLoggerFactory());

		static bool AllowCache { get; } = "true".IsEquals(UtilityService.GetAppSetting("Portals:Cache:Allow", "true"));

		internal static string RefresherURL { get; } = UtilityService.GetAppSetting("Portals:RefresherURL", "https://vieapps.net/~url.refresher");

		internal static int ExpiresAfter { get; } = Int32.TryParse(UtilityService.GetAppSetting("Portals:ExpiresAfter", "0"), out var expiresAfter) && expiresAfter > -1 ? expiresAfter : 0;

		public static List<string> LegacyParameters { get; } = UtilityService.GetAppSetting("Portals:LegacyParameters", "desktop,catName,contId,page").ToList();

		public static HashSet<string> BlackIPs { get; } = UtilityService.GetAppSetting("Portals:BlackIPs", "").ToHashSet();

		static Task ProcessInterCommunicateMessageAsync(CommunicateMessage message)
			=> Task.CompletedTask;
		#endregion

		public async Task Invoke(HttpContext context)
		{
			// request of WebSocket
			if (context.WebSockets.IsWebSocketRequest)
				await Task.WhenAll
				(
					Global.IsVisitLogEnabled ? context.WriteLogsAsync(Global.Logger, "Http.Visits", $"Wrap a WebSocket connection successful\r\n- Endpoint: {context.GetRemoteIPAddress()}:{context.Connection.RemotePort}\r\n- URI: {context.GetRequestUri()}{(Global.IsDebugLogEnabled ? $"\r\n- Headers:\r\n\t{context.Request.Headers.Select(kvp => $"{kvp.Key}: {kvp.Value}").Join("\r\n\t")}" : "")}") : Task.CompletedTask,
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
				{
					if (Handler.BlackIPs.Contains($"{context.GetRemoteIPAddress()}"))
						context.SetResponseHeaders((int)HttpStatusCode.Forbidden);
					else
						await this.ProcessHttpRequestAsync(context).ConfigureAwait(false);
				}
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

		static void NormalizeSession(Session session, string appName = null, string appPlatform = null, string deviceID = null)
		{
			try
			{
				session.AppName = (appName ?? session.AppName).Url64Decode();
			}
			catch
			{
				session.AppName = appName ?? session.AppName;
			}
			try
			{
				session.AppPlatform = (appPlatform ?? session.AppPlatform).Url64Decode();
			}
			catch
			{
				session.AppPlatform = appPlatform ?? session.AppPlatform;
			}
			try
			{
				session.DeviceID = (deviceID ?? session.DeviceID).Url64Decode();
			}
			catch
			{
				session.DeviceID = deviceID ?? session.DeviceID;
			}
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

			var serviceName = requestObj.Get("ServiceName", "").GetANSIUri(true, true);
			var objectName = requestObj.Get("ObjectName", "").GetANSIUri(true, true);
			var verb = requestObj.Get("Verb", "GET").ToUpper();
			var query = new Dictionary<string, string>(requestObj.Get("Query", new Dictionary<string, string>()), StringComparer.OrdinalIgnoreCase);
			var header = new Dictionary<string, string>(requestObj.Get("Header", new Dictionary<string, string>()), StringComparer.OrdinalIgnoreCase);
			var body = requestObj.Get("Body")?.ToExpandoObject();
			var extra = new Dictionary<string, string>(requestObj.Get("Extra", new Dictionary<string, string>()), StringComparer.OrdinalIgnoreCase);
			query.TryGetValue("object-identity", out var objectIdentity);

			// session
			var session = websocket.Get<Session>("Session") ?? Global.GetSession();
			Handler.NormalizeSession(session);

			// procsess
			try
			{
				// visit logs
				if (Global.IsVisitLogEnabled)
					await Global.WriteLogsAsync(Global.Logger, "Http.Visits",
						$"Request starting {verb} " + $"/{serviceName.ToLower()}{(string.IsNullOrWhiteSpace(objectName) ? "" : $"/{objectName.ToLower()}")}{(string.IsNullOrWhiteSpace(objectIdentity) ? "" : $"/{objectIdentity}")}".ToLower() + (query.TryGetValue("x-request", out var xrequest) ? $"?x-request={xrequest}" : "") + " HTTPWS/1.1" + " \r\n" +
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
						await Global.UpdateWithAuthenticateTokenAsync(session, appToken, Handler.ExpiresAfter, null, null, null, Global.Logger, "Http.Authentications", correlationID).ConfigureAwait(false);
						if (!string.IsNullOrWhiteSpace(session.User.ID) && !await session.IsSessionExistAsync(Global.Logger, "Http.Authentications", correlationID).ConfigureAwait(false))
							throw new InvalidSessionException("Session is invalid (The session is not issued by the system)");

						var encryptionKey = session.GetEncryptionKey(Global.EncryptionKey);
						var encryptionIV = session.GetEncryptionIV(Global.EncryptionKey);

						if (!header.TryGetValue("x-session-id", out var sessionID) || !sessionID.Decrypt(encryptionKey, encryptionIV).Equals(session.GetEncryptedID()))
						{
							if (Global.IsDebugLogEnabled)
								await Global.WriteLogsAsync(Global.Logger, "Http.Authentications", $"The session identity is invalid [{session.GetEncryptedID()} != {(sessionID ?? "").Decrypt(encryptionKey, encryptionIV)}]", null, Global.ServiceName, LogLevel.Error, correlationID).ConfigureAwait(false);
							throw new InvalidSessionException("Session is invalid (The session is not issued by the system)");
						}

						if (!header.TryGetValue("x-device-id", out var deviceID) || !deviceID.Decrypt(encryptionKey, encryptionIV).Equals(session.DeviceID))
						{
							if (Global.IsDebugLogEnabled)
								await Global.WriteLogsAsync(Global.Logger, "Http.Authentications", $"The device identity is invalid [{session.DeviceID} != {(deviceID ?? "").Decrypt(encryptionKey, encryptionIV)}]", null, Global.ServiceName, LogLevel.Error, correlationID).ConfigureAwait(false);
							throw new InvalidSessionException("Session is invalid (The session is not issued by the system)");
						}

						session.AppName = body?.Get<string>("x-app-name") ?? session.AppName;
						session.AppPlatform = body?.Get<string>("x-app-platform") ?? session.AppPlatform;
						websocket.Set("Status", "Authenticated");
						websocket.Set("Token", JSONWebToken.DecodeAsJson(appToken, Global.JWTKey));
					}

					websocket.Set("Session", session);
					await websocket.PrepareConnectionInfoAsync(correlationID, session, Global.CancellationToken, Global.Logger).ConfigureAwait(false);
					if (Global.IsDebugLogEnabled)
						await Global.WriteLogsAsync(Global.Logger, "Http.Authentications", $"Successfully {(verb.IsEquals("REG") ? "register" : "authenticate")} a WebSocket connection\r\n{websocket.GetConnectionInfo(session)}\r\n- Status: {websocket.Get<string>("Status")}", null, Global.ServiceName, LogLevel.Information, correlationID).ConfigureAwait(false);
				}

				// call a service of APIs
				else
				{
					var requestInfo = new RequestInfo(session, serviceName, objectName, verb, query, header, body?.ToJson().ToString(Formatting.None), extra, correlationID);
					if ("discovery".IsEquals(requestInfo.ServiceName) && "definitions".IsEquals(requestInfo.ObjectName))
					{
						requestInfo.ServiceName = requestInfo.Query["service-name"] = requestInfo.Query["x-service-name"].GetANSIUri(true, true).GetCapitalizedFirstLetter();
						requestInfo.Query["object-identity"] = requestInfo.Query["x-object-name"];
						requestInfo.Query["mode"] = requestInfo.Query.TryGetValue("x-object-identity", out var mode) ? mode : "";
						requestInfo.Verb = "GET";
					}
					var response = new JObject
					{
						{ "Type", $"{requestInfo.ServiceName}#{requestInfo.ObjectName}#{verb.GetCapitalizedFirstLetter()}" },
						{ "Data", await Global.CallServiceAsync(requestInfo, Global.CancellationToken, Global.Logger, "Http.Process.Requests").ConfigureAwait(false) }
					};
					if (!string.IsNullOrWhiteSpace(requestID))
						response["ID"] = requestID;
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
								{ "Message", message },
								{ "Type", type },
								{ "Verb", verb },
								{ "Code", code },
								{ "StackTrace", stacks },
								{ "CorrelationID", correlationID }
							}
						}
					};
					await websocket.SendAsync(response, Global.CancellationToken).ConfigureAwait(false);
					if (ex is InvalidSessionException)
						await websocket.CloseAsync(WebSocketCloseStatus.PolicyViolation, ex.Message, Global.CancellationToken).ConfigureAwait(false);
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

		async Task ProcessHttpRequestAsync(HttpContext context)
		{
			// prepare
			context.SetItem("PipelineStopwatch", Stopwatch.StartNew());
			var requestPath = context.GetRequestPathSegments(true).First();

			if (Global.IsVisitLogEnabled)
				await context.WriteVisitStartingLogAsync(context.GetParameter("x-logs") != null).ConfigureAwait(false);

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
			var isDebugLogEnabled = Global.IsDebugLogEnabled || context.GetParameter("x-logs") != null;

			var session = context.Session.Get<Session>("Session") ?? context.GetSession();
			Handler.NormalizeSession(session, context.GetParameter("x-app-name"), context.GetParameter("x-app-platform"), context.GetParameter("x-device-id"));

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

				if (isDebugLogEnabled)
					await context.WriteLogsAsync(Global.Logger, "Http.Authentications", $"Successfully update an user with authenticate ticket {session.ToJson()}").ConfigureAwait(false);
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
						await context.UpdateWithAuthenticateTokenAsync(session, authenticateToken, Handler.ExpiresAfter, null, null, null, Global.Logger, "Http.Authentications", correlationID).ConfigureAwait(false);
						if (isDebugLogEnabled)
							await context.WriteLogsAsync(Global.Logger, "Http.Authentications", $"Successfully authenticate an user with authenticate token {session.ToJson().ToString(Formatting.Indented)}").ConfigureAwait(false);

						// assign user information
						context.User = new UserPrincipal(session.User);
					}
					catch (Exception ex)
					{
						await context.WriteLogsAsync(Global.Logger, "Http.Authentications", $"Failure authenticate an user with authenticate token => {ex.Message}", ex, Global.ServiceName, LogLevel.Error).ConfigureAwait(false);
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
				var requestSegments = pathSegments.Select(path => path).ToArray();
				var firstPathSegment = pathSegments.Length > 0 ? pathSegments[0].ToLower() : "";

				// special parameters (like spider indicator (robots.txt)/ads indicator (ads.txt) or system/organization identity)
				if (!string.IsNullOrWhiteSpace(firstPathSegment))
				{
					// system/oranization identity or service
					if (firstPathSegment.StartsWith("~"))
					{
						// a specified service
						if (requestSegments[0].IsStartsWith("~apis"))
							specialRequest = "service";

						else if (Handler.Feeds.Contains(firstPathSegment.Right(firstPathSegment.Length - 1)))
							specialRequest = "feed";

						// a specified system
						else
						{
							systemIdentity = firstPathSegment.Right(firstPathSegment.Length - 1).Replace(StringComparison.OrdinalIgnoreCase, ".html", "").Replace(StringComparison.OrdinalIgnoreCase, ".aspx", "").Replace(StringComparison.OrdinalIgnoreCase, ".php", "").GetANSIUri(true, false);
							query["x-system"] = systemIdentity;
						}

						requestSegments = pathSegments.Skip(1).ToArray();
						if (specialRequest.IsEquals("service"))
						{
							query["service-name"] = requestSegments.Length > 0 && !string.IsNullOrWhiteSpace(requestSegments[0]) ? requestSegments[0].GetANSIUri(true, true) : "unknown";
							query["object-name"] = requestSegments.Length > 1 && !string.IsNullOrWhiteSpace(requestSegments[1]) ? requestSegments[1].GetANSIUri(true, true) : "";
							query["object-identity"] = requestSegments.Length > 2 && !string.IsNullOrWhiteSpace(requestSegments[2]) ? requestSegments[2].GetANSIUri() : "";
						}
					}

					// special requests (_initializer, _validator, _login, _logout, _feed) or special resources (_assets, _css, _fonts, _images, _js)
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
				}

				// normalize info of requests
				if (requestSegments.Length > 0 && specialRequest.IsEquals(""))
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

					else if (Handler.CmsPortals.Contains(firstRequestSegment))
					{
						specialRequest = "cms";
						requestSegments = Array.Empty<string>();
					}

					else if (Handler.Feeds.Contains(firstRequestSegment))
					{
						specialRequest = "feed";
						requestSegments = Array.Empty<string>();
					}

					// indicators
					else if (firstPathSegment.IsEndsWith(".txt") || firstPathSegment.IsEndsWith(".xml") || firstPathSegment.IsEndsWith(".json"))
					{
						systemIdentity = "~indicators";
						query["x-indicator"] = firstPathSegment;
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
						query["x-desktop"] = (value.Equals("default") ? "-" : "") + value;

						value = requestSegments.Length > 1 && !string.IsNullOrWhiteSpace(requestSegments[1]) ? requestSegments[1].Replace(StringComparison.OrdinalIgnoreCase, ".html", "").Replace(StringComparison.OrdinalIgnoreCase, ".aspx", "").Replace(StringComparison.OrdinalIgnoreCase, ".php", "") : null;
						query["x-parent"] = string.IsNullOrWhiteSpace(value) ? null : value.GetANSIUri();

						if (requestSegments.Length > 2 && !string.IsNullOrWhiteSpace(requestSegments[2]))
						{
							value = requestSegments[2].Replace(StringComparison.OrdinalIgnoreCase, ".html", "").Replace(StringComparison.OrdinalIgnoreCase, ".aspx", "").Replace(StringComparison.OrdinalIgnoreCase, ".php", "");
							if (value.IsNumeric())
								query["x-page"] = value;
							else
								query["x-content"] = value.GetANSIUri();

							if (requestSegments.Length > 3 && !string.IsNullOrWhiteSpace(requestSegments[3]))
							{
								value = requestSegments[3].Replace(StringComparison.OrdinalIgnoreCase, ".html", "").Replace(StringComparison.OrdinalIgnoreCase, ".aspx", "").Replace(StringComparison.OrdinalIgnoreCase, ".php", "");
								if (value.IsNumeric())
									query["x-page"] = value;
							}
						}
					}
				}
				else if (!systemIdentity.IsEquals("~indicators") && !systemIdentity.IsEquals("~resources") && !specialRequest.IsEquals("service"))
					query["x-desktop"] = "-default";

				// legacy parameters
				Handler.LegacyParameters.ForEach(key => query.Remove(key));
			});

			// validate HTTP Verb
			var httpVerb = (context.Request.Method ?? "GET").ToUpper();
			if (!httpVerb.IsEquals("GET") && !specialRequest.IsEquals("login") && !specialRequest.IsEquals("service"))
				throw new MethodNotAllowedException(httpVerb);

			// prepare headers
			var headers = context.Request.Headers.ToDictionary(dictionary =>
			{
				Handler.ExcludedHeaders.ForEach(name => dictionary.Remove(name));
				dictionary.Keys.Where(name => name.IsStartsWith("cf-") || name.IsStartsWith("sec-")).ToList().ForEach(name => dictionary.Remove(name));
				dictionary["x-host"] = context.GetParameter("Host") ?? requestURI.Host;
				dictionary["x-url"] = "https".IsEquals(context.GetHeaderParameter("x-forwarded-proto") ?? context.GetHeaderParameter("x-original-proto")) && !"https".IsEquals(requestURI.Scheme)
					? requestURI.AbsoluteUri.Replace(StringComparison.OrdinalIgnoreCase, $"{requestURI.Scheme}://", "https://")
					: requestURI.AbsoluteUri;
				dictionary["x-use-short-urls"] = Handler.UseShortURLs.ToString().ToLower();
				dictionary["x-environment-is-mobile"] = isMobile;
				dictionary["x-environment-os-info"] = osInfo;
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
			if (queryString.Remove("x-request-extra", out var extraInfo) && !string.IsNullOrWhiteSpace(extraInfo))
				try
				{
					extra = extraInfo.Url64Decode().ToExpandoObject().ToDictionary(kvp => kvp.Key, kvp => kvp.Value?.ToString(), StringComparer.OrdinalIgnoreCase);
				}
				catch { }

			// process the request
			var requestInfo = new RequestInfo(session, "Portals", "Identify.System", "GET", queryString, headers, null, extra, correlationID);
			if (isDebugLogEnabled)
				await Global.WriteLogsAsync(Global.Logger, "Http.Visits",
					$"Process a request of CMS Portals {httpVerb} {requestURI}" + " \r\n" +
					$"- App: {session.AppName ?? "Unknown"} @ {session.AppPlatform ?? "Unknown"} [{session.AppAgent ?? "Unknown"}]" + " \r\n" +
					$"- Request Info: {requestInfo.ToString(Formatting.Indented)}"
				, null, Global.ServiceName, LogLevel.Information, correlationID).ConfigureAwait(false);

			JObject systemIdentityJson = null;
			if (string.IsNullOrWhiteSpace(specialRequest))
				try
				{
					// call the Portals service to identify the system
					using var cts = CancellationTokenSource.CreateLinkedTokenSource(Global.CancellationToken, context.RequestAborted);
					if (!"~resources".IsEquals(systemIdentity))
					{
						systemIdentityJson = await context.CallServiceAsync(requestInfo, cts.Token, Global.Logger, "Http.Process.Requests").ConfigureAwait(false) as JObject;
						requestInfo.Query["x-system"] = systemIdentityJson?.Get<string>("Alias");
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
							var filesHttpURI = systemIdentityJson?.Get<string>("FilesHttpURI") ?? UtilityService.GetAppSetting("HttpUri:Files", "https://fs.vieapps.net");
							while (filesHttpURI.EndsWith("/"))
								filesHttpURI = filesHttpURI.Left(filesHttpURI.Length - 1).Trim();
							context.SetResponseHeaders((int)HttpStatusCode.MovedPermanently, new Dictionary<string, string>
							{
								["Location"] = $"{filesHttpURI}/{requestSegments.Join("/")}"
							});
							return;
						}
					}

					// working with cache (of portal desktops/resources)
					if (Handler.AllowCache && !Handler.RefresherURL.IsEquals(context.GetReferUrl()) && requestInfo.GetParameter("x-no-cache") == null && requestInfo.GetParameter("x-force-cache") == null)
					{
						var cacheKey = "";
						var eTag = "";
						var contentType = "text/html";
						var expires = DateTime.Now.AddMinutes(13);
						var baseURL = "";
						var rootURL = "/";
						var alwaysUseHTTPs = false;
						var alwaysReturnHTTPs = false;
						var redirectToNoneWWW = false;
						var filesHttpURI = systemIdentityJson?.Get<string>("FilesHttpURI") ?? UtilityService.GetAppSetting("HttpUri:Files", "https://fs.vieapps.net");
						while (filesHttpURI.EndsWith("/"))
							filesHttpURI = filesHttpURI.Left(filesHttpURI.Length - 1).Trim();
						var portalsHttpURI = systemIdentityJson?.Get<string>("PortalsHttpURI") ?? UtilityService.GetAppSetting("HttpUri:Portals", "https://portals.vieapps.net");
						while (portalsHttpURI.EndsWith("/"))
							portalsHttpURI = portalsHttpURI.Left(portalsHttpURI.Length - 1).Trim();

						if ("~resources".IsEquals(systemIdentity))
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
								contentType = $"image/{(contentType.IsEquals("svg") ? "svg+xml" : contentType.IsEquals("jpg") || contentType.IsEquals("jpeg") ? "jpeg" : contentType)}";
							}

							eTag = cacheKey = (type.IsEquals("css") || type.IsEquals("js")) && (isThemeResource || (identity != null && identity.Length == 34 && identity.Right(32).IsValidUUID()))
								? $"{type}#{identity}"
								: $"v#{requestURI.AbsolutePath.ToLower().GenerateUUID()}";
							expires = DateTime.Now.AddDays(366);
						}

						else if (!"~indicators".IsEquals(systemIdentity))
						{
							systemIdentityJson = systemIdentityJson ?? await context.CallServiceAsync(requestInfo, cts.Token, Global.Logger, "Http.Process.Requests").ConfigureAwait(false) as JObject;
							var organizationID = systemIdentityJson.Get<string>("ID");
							var organizationAlias = systemIdentityJson.Get<string>("Alias");
							var homeDesktopAlias = systemIdentityJson.Get<string>("HomeDesktopAlias");

							alwaysUseHTTPs = systemIdentityJson.Get("AlwaysUseHTTPs", false);
							alwaysReturnHTTPs = systemIdentityJson.Get("AlwaysReturnHTTPs", false);
							redirectToNoneWWW = systemIdentityJson.Get("RedirectToNoneWWW", false);

							var desktopAlias = queryString["x-desktop"].ToLower();
							var path = requestURI.AbsolutePath.ToLower();
							while (path.EndsWith("/") || path.EndsWith("."))
								path = path.Left(path.Length - 1).Trim();
							if (path.IsStartsWith($"/~{organizationAlias}"))
							{
								path = path.Right(path.Length - organizationAlias.Length - 2);
								baseURL = $"{portalsHttpURI}/~{organizationAlias}/";
								rootURL = "";
							}
							path = path.IsEndsWith(".html") || path.IsEndsWith(".aspx") ? path.Left(path.Length - 5) : path.IsEndsWith(".php") ? path.Left(path.Length - 4) : path;
							path = path.Equals("") || path.Equals("/") || path.Equals("/index") || path.Equals("/default") ? desktopAlias : path;

							cacheKey = $"{organizationID}:{(desktopAlias.IsEquals("-default") || desktopAlias.IsEquals(homeDesktopAlias) ? "-default" : path).GenerateUUID()}";
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
							var watch = Stopwatch.StartNew();
							if (isDebugLogEnabled || Global.IsVisitLogEnabled)
								await context.WriteLogsAsync(Global.Logger, "Http.Visits", $"Attempt to process the CMS Portals service cache => {requestURI} ({cacheKey})").ConfigureAwait(false);

							// last modified
							var modifiedSince = context.GetHeaderParameter("If-Modified-Since") ?? context.GetHeaderParameter("If-Unmodified-Since");
							var lastModified = modifiedSince != null ? await Handler.Cache.GetAsync<string>($"{cacheKey}:time", cts.Token).ConfigureAwait(false) : null;
							var noneMatch = lastModified != null ? context.GetHeaderParameter("If-None-Match") : null;
							if (lastModified != null && eTag.IsEquals(noneMatch) && modifiedSince.FromHttpDateTime() >= lastModified.FromHttpDateTime())
							{
								context.SetResponseHeaders((int)HttpStatusCode.NotModified, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
								{
									{ "X-Cache", "HTTP-304" },
									{ "X-Correlation-ID", correlationID },
									{ "Content-Type", $"{contentType}; charset=utf-8" },
									{ "ETag", eTag },
									{ "Last-Modified", lastModified }
								});

								if (isDebugLogEnabled || Global.IsVisitLogEnabled)
									await context.WriteLogsAsync(Global.Logger, "Http.Visits", $"Process the CMS Portals service cache was done => NOT MODIFIED ({eTag} - {lastModified}) - Excution times: {watch.GetElapsedTimes()}").ConfigureAwait(false);

								return;
							}

							// cached data
							watch.Restart();
							var cached = await Handler.Cache.GetAsync<string>(cacheKey, cts.Token).ConfigureAwait(false);

							if (!string.IsNullOrWhiteSpace(cached))
							{
								lastModified = lastModified ?? await Handler.Cache.GetAsync<string>($"{cacheKey}:time", cts.Token).ConfigureAwait(false) ?? DateTime.Now.ToHttpString();
								var expiresAt = contentType.IsEquals("text/html") ? await Handler.Cache.GetAsync<string>($"{cacheKey}:expiration", cts.Token).ConfigureAwait(false) : null;
								context.SetResponseHeaders((int)HttpStatusCode.OK, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
								{
									{ "X-Cache", "HTTP-200" },
									{ "X-Correlation-ID", correlationID },
									{ "Content-Type", $"{contentType}; charset=utf-8" },
									{ "ETag", eTag },
									{ "Last-Modified", lastModified },
									{ "Expires", (string.IsNullOrWhiteSpace(expiresAt) || !DateTime.TryParse(expiresAt, out var expirationTime) ? expires : expirationTime).ToHttpString() },
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
									});
									if (!string.IsNullOrWhiteSpace(baseURL))
										cached = cached.Insert(cached.PositionOf(">", cached.PositionOf("<head")) + 1, $"<base href=\"{baseURL}\"/>");
									cached = cached.Replace(StringComparison.OrdinalIgnoreCase, $" src=\"{(alwaysUseHTTPs || alwaysReturnHTTPs ? "https" : requestURI.Scheme)}://", " src=\"//");
									cached = cached.Replace(StringComparison.OrdinalIgnoreCase, $" srcset=\"{(alwaysUseHTTPs || alwaysReturnHTTPs ? "https" : requestURI.Scheme)}://", " srcset=\"//");
									cached = cached.Replace(StringComparison.OrdinalIgnoreCase, $" href=\"{(alwaysUseHTTPs || alwaysReturnHTTPs ? "https" : requestURI.Scheme)}://", " href=\"//");
									cached = cached.Replace(StringComparison.OrdinalIgnoreCase, $"url({(alwaysUseHTTPs || alwaysReturnHTTPs ? "https" : requestURI.Scheme)}://", "url(//");
									cached = cached.Replace(StringComparison.OrdinalIgnoreCase, "<link rel=\"canonical\" href=\"//", $"<link rel=\"canonical\" href=\"{(alwaysUseHTTPs || alwaysReturnHTTPs ? "https" : requestURI.Scheme)}://");
								}

								await context.WriteAsync(contentType.IsStartsWith("image/") || contentType.IsStartsWith("font/") ? cached.Base64ToBytes() : cached.Replace("~#/", $"{portalsHttpURI}/").Replace("~~~/", $"{portalsHttpURI}/").Replace("~~/", $"{filesHttpURI}/").Replace("~/", rootURL).ToBytes(), cts.Token).ConfigureAwait(false);
								if (isDebugLogEnabled || Global.IsVisitLogEnabled)
									await context.WriteLogsAsync(Global.Logger, "Http.Visits", $"Process the CMS Portals service cache was done => FOUND ({cacheKey}) - Excution times: {watch.GetElapsedTimes()}").ConfigureAwait(false);

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
				catch (OperationCanceledException) { }
				catch (Exception ex)
				{
					var statusCode = (int)HttpStatusCode.InternalServerError;
					if (ex is WampException wampException)
					{
						var wampDetails = wampException.GetDetails(requestInfo);
						statusCode = wampDetails.Item3 == "SiteNotRecognizedException" ? (int)HttpStatusCode.NotFound : wampDetails.Item1;
						context.ShowError(statusCode, wampDetails.Item2, wampDetails.Item3, correlationID, wampDetails.Item4 + "\r\n\t" + ex.StackTrace, isDebugLogEnabled);
					}
					else
					{
						var type = ex.GetTypeName(true);
						statusCode = type == "SiteNotRecognizedException" ? (int)HttpStatusCode.NotFound : ex.GetHttpStatusCode();
						context.ShowError(statusCode, ex.Message, type, correlationID, ex, isDebugLogEnabled);
					}
					await context.WriteLogsAsync("Http.Process.Requests", $"Error occurred ({statusCode}) => {context.Request.Method} {requestURI}", ex, Global.ServiceName, LogLevel.Error).ConfigureAwait(false);
				}

			else
				switch (specialRequest)
				{
					case "initializer":
						if (context.Request.Path.Value.IsEndsWith(".aspx") || context.Request.Path.Value.IsEndsWith(".html") || context.Request.Path.Value.IsEndsWith(".php"))
							systemIdentityJson = systemIdentityJson ?? await context.CallServiceAsync(requestInfo, Global.CancellationToken, Global.Logger, "Http.Authentications").ConfigureAwait(false) as JObject;
						await this.ProcessInitializerRequestAsync(context, systemIdentityJson).ConfigureAwait(false);
						break;

					case "validator":
						await this.ProcessValidatorRequestAsync(context).ConfigureAwait(false);
						break;

					case "login":
						if (!context.Request.Method.IsEquals("GET") || context.Request.Path.Value.IsEndsWith(".aspx") || context.Request.Path.Value.IsEndsWith(".html") || context.Request.Path.Value.IsEndsWith(".php"))
							systemIdentityJson = systemIdentityJson ?? await context.CallServiceAsync(requestInfo, Global.CancellationToken, Global.Logger, "Http.Authentications").ConfigureAwait(false) as JObject;
						await this.ProcessLogInRequestAsync(context, systemIdentityJson).ConfigureAwait(false);
						break;

					case "logout":
						if (context.Request.Path.Value.IsEndsWith(".aspx") || context.Request.Path.Value.IsEndsWith(".html") || context.Request.Path.Value.IsEndsWith(".php"))
							systemIdentityJson = systemIdentityJson ?? await context.CallServiceAsync(requestInfo, Global.CancellationToken, Global.Logger, "Http.Authentications").ConfigureAwait(false) as JObject;
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
								context.ShowError(wampDetails.Item1, wampDetails.Item2, wampDetails.Item3, correlationID, wampDetails.Item4 + "\r\n\t" + ex.StackTrace, isDebugLogEnabled);
							}
							else
								context.ShowError(ex.GetHttpStatusCode(), ex.Message, ex.GetTypeName(true), correlationID, ex, isDebugLogEnabled);
							await context.WriteLogsAsync("Http.Process.Requests", $"Error occurred while redirecting to CMS Portals => {ex.Message}", ex).ConfigureAwait(false);
						}
						break;

					case "feed":
						try
						{
							using var cts = CancellationTokenSource.CreateLinkedTokenSource(Global.CancellationToken, context.RequestAborted);
							systemIdentityJson = systemIdentityJson ?? await context.CallServiceAsync(requestInfo, cts.Token, Global.Logger, "Http.Process.Requests").ConfigureAwait(false) as JObject;
							requestInfo = new RequestInfo(requestInfo) { ObjectName = "Generate.Feed" };
							requestInfo.Query["x-system"] = systemIdentityJson.Get<string>("Alias");
							var response = (await context.CallServiceAsync(requestInfo, cts.Token, Global.Logger, "Http.Process.Requests").ConfigureAwait(false)).ToExpandoObject();
							context.SetResponseHeaders(response.Get("StatusCode", (int)HttpStatusCode.OK), response.Get("Headers", new Dictionary<string, string>()));
							var body = response.Get<string>("Body");
							if (body != null)
								await context.WriteAsync(response.Get("BodyAsPlainText", false) ? body.ToBytes() : body.Base64ToBytes().Decompress(response.Get("BodyEncoding", "gzip")), cts.Token).ConfigureAwait(false);
						}
						catch (Exception ex)
						{
							if (ex is WampException wampException)
							{
								var wampDetails = wampException.GetDetails(requestInfo);
								context.ShowError(wampDetails.Item1, wampDetails.Item2, wampDetails.Item3, correlationID, wampDetails.Item4 + "\r\n\t" + ex.StackTrace, isDebugLogEnabled);
							}
							else
								context.ShowError(ex.GetHttpStatusCode(), ex.Message, ex.GetTypeName(true), correlationID, ex, isDebugLogEnabled);
							await context.WriteLogsAsync("Http.Process.Requests", $"Error occurred while processing feeds => {ex.Message}", ex).ConfigureAwait(false);
						}
						break;

					case "service":
						requestInfo = new RequestInfo(requestInfo)
						{
							ServiceName = requestInfo.Query["service-name"],
							ObjectName = requestInfo.Query["object-name"],
							Verb = httpVerb
						};
						if ("discovery".IsEquals(requestInfo.ServiceName) && "definitions".IsEquals(requestInfo.ObjectName))
						{
							requestInfo.ServiceName = requestInfo.Query["service-name"] = requestInfo.Query["x-service-name"];
							requestInfo.Query["object-identity"] = requestInfo.Query["x-object-name"];
							requestInfo.Query["mode"] = requestInfo.Query.TryGetValue("x-object-identity", out var mode) ? mode : "";
							requestInfo.Verb = "GET";
						}
						try
						{
							using var cts = CancellationTokenSource.CreateLinkedTokenSource(Global.CancellationToken, context.RequestAborted);
							var response = await context.CallServiceAsync(requestInfo, cts.Token, Global.Logger, "Http.Services").ConfigureAwait(false);
							await Task.WhenAll
							(
								context.WriteAsync(response, cts.Token),
								isDebugLogEnabled ? context.WriteLogsAsync(Global.Logger, "Http.Services", $"Successfully process request of a service {response}") : Task.CompletedTask
							).ConfigureAwait(false);
						}
						catch (Exception ex)
						{
							context.WriteError(Global.Logger, ex, requestInfo, $"Error occurred while calling a service => {ex.Message}", true, "Http.Services");
						}
						break;

					default:
						var invalidException = new InvalidRequestException();
						context.ShowError(invalidException.GetHttpStatusCode(), invalidException.Message, invalidException.GetType().GetTypeName(true), correlationID, invalidException, isDebugLogEnabled);
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
						}, cts.Token, Global.Logger, "Http.Authentications").ConfigureAwait(false);

						await Task.WhenAll
						(
							Global.Cache.RemoveAsync($"Attempt#{context.Connection.RemoteIpAddress}", cts.Token),
							Global.IsDebugLogEnabled ? context.WriteLogsAsync(Global.Logger, "Http.Authentications", $"Successfully activate {context.Request.QueryString.ToDictionary().ToJson()}") : Task.CompletedTask
						).ConfigureAwait(false);
					}
					catch (Exception ex)
					{
						await context.WriteLogsAsync("Http.Authentications", $"Error occurred while activating => {ex.Message}", ex).ConfigureAwait(false);
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
					await context.WriteLogsAsync("Http.Authentications", $"Error occurred while activating => {ex.Message}", ex).ConfigureAwait(false);
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
					context.WriteLogsAsync("Http.Authentications", $"Error occurred while validating => {ex.Message}", ex)
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
					}, cts.Token, Global.Logger, "Http.Authentications").ConfigureAwait(false);
					await Task.WhenAll
					(
							context.WriteAsync(session.GetSessionJson(payload => payload["did"] = session.DeviceID), Formatting.Indented, correlationID, cts.Token),
							Global.IsDebugLogEnabled ? context.WriteLogsAsync(Global.Logger, "Http.Authentications", $"Successfully register a new session {response}") : Task.CompletedTask
					).ConfigureAwait(false);
				}
				catch (Exception ex)
				{
					context.WriteError(Global.Logger, ex, null, $"Error occurred while registering a new session => {ex.Message}", true, "Http.Authentications");
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
					}, cts.Token, Global.Logger, "Http.Authentications").ConfigureAwait(false);

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
						}, cts.Token, Global.Logger, "Http.Authentications").ConfigureAwait(false);

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
							Global.IsDebugLogEnabled ? context.WriteLogsAsync(Global.Logger, "Http.Authentications", $"Successfully log a session in {response}") : Task.CompletedTask
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
					}, cts.Token, Global.Logger, "Http.Authentications").ConfigureAwait(false);

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
					}, cts.Token, Global.Logger, "Http.Authentications").ConfigureAwait(false);

					// update authenticate ticket
					var userPrincipal = new UserPrincipal(new UserIdentity(session.User.ID, session.SessionID, CookieAuthenticationDefaults.AuthenticationScheme));
					await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, userPrincipal, new AuthenticationProperties { IsPersistent = false }).ConfigureAwait(false);
					context.Session.Add("Session", session);

					// response
					await Task.WhenAll
					(
							Global.Cache.RemoveAsync($"Attempt#{context.Connection.RemoteIpAddress}", cts.Token),
							context.WriteAsync(session.GetSessionJson(payload => payload["did"] = session.DeviceID), Formatting.Indented, correlationID, cts.Token),
							Global.IsDebugLogEnabled ? context.WriteLogsAsync(Global.Logger, "Http.Authentications", $"Successfully log a session in with OTP {response}") : Task.CompletedTask
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
					}, cts.Token, Global.Logger, "Http.Authentications").ConfigureAwait(false);

					// response
					await Task.WhenAll
					(
							Global.Cache.RemoveAsync($"Attempt#{context.Connection.RemoteIpAddress}", cts.Token),
							context.WriteAsync(response, Formatting.Indented, correlationID, cts.Token),
							Global.IsDebugLogEnabled ? context.WriteLogsAsync(Global.Logger, "Http.Authentications", $"Successfully send a renew password request {response}") : Task.CompletedTask
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
						await (isUserInteract ? showAsync() : registerAsync()).ConfigureAwait(false);
						break;
				}
			}
			catch (Exception ex)
			{
				if (isUserInteract)
				{
					await context.WriteLogsAsync("Http.Authentications", $"Error occurred while logging in => {ex.Message}", ex).ConfigureAwait(false);
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
				}, cts.Token, Global.Logger, "Http.Authentications").ConfigureAwait(false);

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
						Global.IsDebugLogEnabled ? context.WriteLogsAsync(Global.Logger, "Http.Authentications", $"Successfully log a session out (direct) {response}") : Task.CompletedTask
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
					}, cts.Token, Global.Logger, "Http.Authentications").ConfigureAwait(false);
					context.Session.Add("Session", session);
					await Task.WhenAll
					(
						context.WriteAsync(session.GetSessionJson(payload => payload["did"] = session.DeviceID), Formatting.Indented, correlationID, cts.Token),
						Global.IsDebugLogEnabled ? context.WriteLogsAsync(Global.Logger, "Http.Authentications", $"Successfully log a session out {response}") : Task.CompletedTask
					).ConfigureAwait(false);
				}
			}
			catch (Exception ex)
			{
				if (isUserInteract)
				{
					await context.WriteLogsAsync("Http.Authentications", $"Error occurred while logging out => {ex.Message}", ex).ConfigureAwait(false);
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
					context.ShowError(code, message, type, correlationID, ex, Global.IsDebugLogEnabled);
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

		internal static void Connect(int waitingTimes = 6789)
		{
			Global.Logger.LogDebug($"Attempting to connect to API Gateway Router [{new Uri(Router.GetRouterStrInfo()).GetResolvedURI()}]");
			Global.Connect(
				(sender, arguments) =>
				{
					Global.PrimaryInterCommunicateMessageUpdater?.Dispose();
					Global.PrimaryInterCommunicateMessageUpdater = Router.IncomingChannel?.RealmProxy.Services
						.GetSubject<CommunicateMessage>("messages.services.portals")
						.Subscribe
						(
							async message =>
							{
								var correlationID = UtilityService.NewUUID;
								try
								{
									await Handler.ProcessInterCommunicateMessageAsync(message).ConfigureAwait(false);
									if (Global.IsDebugResultsEnabled)
										await Global.WriteLogsAsync(Global.Logger, "Http.Updates",
											$"Successfully process an inter-communicate message" + "\r\n" +
											$"- Type: {message?.Type}" + "\r\n" +
											$"- Message: {message?.Data?.ToString(Global.IsDebugLogEnabled ? Formatting.Indented : Formatting.None)}"
										, null, Global.ServiceName, LogLevel.Information, correlationID).ConfigureAwait(false);
								}
								catch (Exception ex)
								{
									await Global.WriteLogsAsync(Global.Logger, "Http.Updates", $"{ex.Message} => {message?.ToJson().ToString(Global.IsDebugLogEnabled ? Formatting.Indented : Formatting.None)}", ex, Global.ServiceName, LogLevel.Error, correlationID).ConfigureAwait(false);
								}
							},
							async exception => await Global.WriteLogsAsync(Global.Logger, "Http.Updates", exception.Message, exception).ConfigureAwait(false)
						);
					Global.SecondaryInterCommunicateMessageUpdater?.Dispose();
					Global.SecondaryInterCommunicateMessageUpdater = Router.IncomingChannel?.RealmProxy.Services
						.GetSubject<CommunicateMessage>("messages.services.apigateway")
						.Subscribe
						(
							async message =>
							{
								if (message.Type.IsEquals("Service#RequestInfo"))
								{
									var correlationID = UtilityService.NewUUID;
									try
									{
										await Global.SendServiceInfoAsync().ConfigureAwait(false);
										if (Global.IsDebugResultsEnabled)
											await Global.WriteLogsAsync(Global.Logger, "Http.Updates",
												$"Successfully process an inter-communicate message" + "\r\n" +
												$"- Type: {message?.Type}" + "\r\n" +
												$"- Message: {message?.Data?.ToString(Global.IsDebugLogEnabled ? Formatting.Indented : Formatting.None)}"
											, null, Global.ServiceName, LogLevel.Information, correlationID).ConfigureAwait(false);
									}
									catch (Exception ex)
									{
										await Global.WriteLogsAsync(Global.Logger, "Http.Updates", $"{ex.Message} => {message?.ToJson().ToString(Global.IsDebugLogEnabled ? Formatting.Indented : Formatting.None)}", ex, Global.ServiceName, LogLevel.Error, correlationID).ConfigureAwait(false);
									}
								}
							},
							async exception => await Global.WriteLogsAsync(Global.Logger, "Http.Updates", exception.Message, exception).ConfigureAwait(false)
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

		string GetSpecialHtml(HttpContext context, JObject systemIdentityJson, string title = "Log in")
		{
			var organizationID = systemIdentityJson.Get<string>("ID");
			var organizationAlias = systemIdentityJson.Get<string>("Alias");

			var portalsHttpURI = systemIdentityJson.Get<string>("PortalsHttpURI") ?? UtilityService.GetAppSetting("HttpUri:Portals", "https://portals.vieapps.net");
			while (portalsHttpURI.EndsWith("/"))
				portalsHttpURI = portalsHttpURI.Left(portalsHttpURI.Length - 1);

			var filesHttpURI = systemIdentityJson.Get<string>("FilesHttpURI") ?? UtilityService.GetAppSetting("HttpUri:Files", "https://fs.vieapps.net");
			while (filesHttpURI.EndsWith("/"))
				filesHttpURI = filesHttpURI.Left(filesHttpURI.Length - 1);

			var rootURL = context.GetRequestPathSegments().First().StartsWith("~") ? "" : "/";
			var language = context.GetQueryParameter("language") ?? systemIdentityJson.Get<string>("Language") ?? "en-US";

			var session = context.GetSession();
			var isMobile = string.IsNullOrWhiteSpace(session.AppPlatform) || session.AppPlatform.IsContains("Desktop") ? "false" : "true";
			var osInfo = (session.AppAgent ?? "").GetOSInfo();

			var version = DateTime.Now.GetTimeQuarter().ToUnixTimestamp().ToString();
			var scripts = "<script>__vieapps={ids:{" + $"system:\"{organizationID}\"" + "},URLs:{root:" + $"\"{rootURL}\",portals:\"{portalsHttpURI}\",files:\"{filesHttpURI}\"" + "}" + $",language:\"{language}\",isMobile:{isMobile},osInfo:\"{osInfo}\",correlationID:\"{context.GetCorrelationID()}\"" + "};</script>"
				+ $"<script src=\"{UtilityService.GetAppSetting("Portals:Desktops:Resources:JQuery", "https://cdnjs.cloudflare.com/ajax/libs/jquery/3.6.0/jquery.min.js")}\"></script>"
				+ $"<script src=\"{UtilityService.GetAppSetting("Portals:Desktops:Resources:CryptoJs", "https://cdnjs.cloudflare.com/ajax/libs/crypto-js/4.1.1/crypto-js.min.js")}\"></script>"
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
	}
}
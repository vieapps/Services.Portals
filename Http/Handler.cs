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

		#region Static properties
		static HashSet<string> Initializers { get; } = "_initializer,initializer.aspx,initializer.ashx".ToHashSet();

		static HashSet<string> Validators { get; } = "_validator,validator.aspx,validator.ashx".ToHashSet();

		static HashSet<string> LogIns { get; } = "_login,_signin,_signup,_register,_admin,_users,_cms,login.aspx,login.ashx,signin.aspx,signin.ashx,signup.aspx,signup.ashx,register.aspx,register.ashx,admin.aspx,admin.ashx,users.aspx,users.ashx,cms.aspx,cms.ashx".ToHashSet();

		static HashSet<string> LogOuts { get; } = "_logout,_signout,logout.aspx,logout.ashx,signout.aspx,signout.ashx".ToHashSet();

		static bool UseShortURLs => "true".IsEquals(UtilityService.GetAppSetting("Portals:UseShortURLs", "true"));

		static string LoadBalancingHealthCheckUrl { get; } = UtilityService.GetAppSetting("HealthCheckUrl", "/load-balancing-health-check");

		internal static Components.WebSockets.WebSocket WebSocket { get; private set; }
		#endregion

		RequestDelegate Next { get; }

		public Handler(RequestDelegate next) => this.Next = next;

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
					await context.FlushAsync(Global.CancellationTokenSource.Token).ConfigureAwait(false);
				}

				// load balancing health check
				else if (context.Request.Path.Value.IsEquals(Handler.LoadBalancingHealthCheckUrl))
					await context.WriteAsync("OK", "text/plain", null, 0, null, TimeSpan.Zero, null, Global.CancellationTokenSource.Token).ConfigureAwait(false);

				// process portals' requests and invoke next middle ware
				else
				{
					await this.ProcessRequestAsync(context).ConfigureAwait(false);
					try
					{
						await this.Next.Invoke(context).ConfigureAwait(false);
					}
					catch (InvalidOperationException) { }
					catch (Exception ex)
					{
						Global.Logger.LogCritical($"Error occurred while invoking the next middleware => {ex.Message}", ex);
					}
				}
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
			var header = context.Request.Headers.ToDictionary();
			var session = context.GetSession();

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
					await context.UpdateWithAuthenticateTokenAsync(session, authenticateToken, 90, null, null, null, Global.Logger, "Http.Authentication", context.GetCorrelationID()).ConfigureAwait(false);
					if (Global.IsDebugLogEnabled)
						await context.WriteLogsAsync(Global.Logger, "Http.Authentication", $"Successfully authenticate an user with token {session.ToJson().ToString(Formatting.Indented)}");

					// perform sign-in (to create authenticate ticket cookie) when the authenticate token its came from passport service
					if (context.GetParameter("x-passport-token") != null)
					{
						await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new UserPrincipal(session.User), new AuthenticationProperties { IsPersistent = false }).ConfigureAwait(false);
						if (Global.IsDebugLogEnabled)
							await context.WriteLogsAsync(Global.Logger, "Http.Authentication", $"Successfully create the authenticate ticket cookie for an user ({session.User.ID})");
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
			if (string.IsNullOrWhiteSpace(session.User.SessionID))
				session.SessionID = session.User.SessionID = UtilityService.NewUUID;
			else
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
				var pathSegments = context.GetRequestPathSegments().Where(segment => !segment.IsEquals("desktop.aspx") && !segment.IsEquals("default.aspx")).ToArray();
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

			var extra = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			if (queryString.Remove("x-request-extra", out var extraInfo) && !string.IsNullOrWhiteSpace(extraInfo))
				try
				{
					extra = extraInfo.Url64Decode().ToExpandoObject().ToDictionary(kvp => kvp.Key, kvp => kvp.Value?.ToString(), StringComparer.OrdinalIgnoreCase);
				}
				catch { }

			var requestURI = context.GetRequestUri();
			var headers = context.Request.Headers.ToDictionary(dictionary =>
			{
				Handler.ExcludedHeaders.ForEach(name => dictionary.Remove(name));
				dictionary.Keys.Where(name => name.IsStartsWith("cf-") || name.IsStartsWith("sec-")).ToList().ForEach(name => dictionary.Remove(name));
				dictionary["x-host"] = context.GetParameter("Host");
				dictionary["x-url"] = "https".IsEquals(context.GetHeaderParameter("x-forwarded-proto") ?? context.GetHeaderParameter("x-original-proto")) && !"https".IsEquals(requestURI.Scheme)
					? requestURI.AbsoluteUri.Replace(StringComparison.OrdinalIgnoreCase, $"{requestURI.Scheme}://", "https://")
					: requestURI.AbsoluteUri;
				dictionary["x-use-short-urls"] = Handler.UseShortURLs.ToString().ToLower();
				dictionary["x-environment-is-mobile"] = string.IsNullOrWhiteSpace(session.AppPlatform) || session.AppPlatform.IsContains("Desktop") ? "false" : "true";
				dictionary["x-environment-os-info"] = (session.AppAgent ?? "").GetOSInfo();
			});

			// process the request
			JObject systemIdentityJson = null;
			if (string.IsNullOrWhiteSpace(specialRequest))
				try
				{
					if (!context.Request.Method.IsEquals("GET"))
						throw new MethodNotAllowedException(context.Request.Method);

					using (var cts = CancellationTokenSource.CreateLinkedTokenSource(Global.CancellationTokenSource.Token, context.RequestAborted))
					{
						// call the Portals service to identify the system
						if (string.IsNullOrWhiteSpace(systemIdentity) || "~indicators".IsEquals(systemIdentity))
						{
							systemIdentityJson = systemIdentityJson ?? await context.CallServiceAsync(new RequestInfo(session, "Portals", "Identify.System", "GET", queryString, headers, null, extra, context.GetCorrelationID()), cts.Token, Global.Logger, "Http.Process.Requests").ConfigureAwait(false) as JObject;
							queryString["x-system"] = systemIdentityJson?.Get<string>("Alias");
						}

						// request of portal desktops/resources
						if (string.IsNullOrWhiteSpace(legacyRequest))
						{
							// call Portals service to process the request
							var response = (await context.CallServiceAsync(new RequestInfo(session, "Portals", "Process.Http.Request", "GET", queryString, headers, null, extra, context.GetCorrelationID()), cts.Token, Global.Logger, "Http.Process.Requests").ConfigureAwait(false)).ToExpandoObject();

							// write headers
							context.SetResponseHeaders(response.Get("StatusCode", (int)HttpStatusCode.OK), response.Get("Headers", new Dictionary<string, string>()));

							// write body
							var body = response.Get<string>("Body");
							if (body != null)
								await context.WriteAsync(response.Get("BodyAsPlainText", false) ? body.ToBytes() : body.Base64ToBytes().Decompress(response.Get("BodyEncoding", "deflate")), cts.Token).ConfigureAwait(false);
						}

						// request of legacy system (files and medias)
						else
						{
							systemIdentityJson = systemIdentityJson ?? await context.CallServiceAsync(new RequestInfo(session, "Portals", "Identify.System", "GET", queryString, headers, null, extra, context.GetCorrelationID()), cts.Token, Global.Logger, "Http.Process.Requests").ConfigureAwait(false) as JObject;
							var requestSegments = legacyRequest.ToArray("/").ToList();

							if (requestSegments[0].IsEquals("Download.ashx"))
								requestSegments[0] = "downloads";
							else
							{
								requestSegments[0] = requestSegments[0].IsEquals("File.ashx") || requestSegments[0].IsEquals("Image.ashx")
									? "files"
									: requestSegments[0].Replace(StringComparison.OrdinalIgnoreCase, ".ashx", "").ToLower() + "s";
								if (!requestSegments[1].IsValidUUID())
									requestSegments.Insert(1, systemIdentityJson?.Get<string>("ID"));
							}

							if (requestSegments[0].IsEquals("files") && requestSegments[3].Contains("-") && requestSegments[3].Length > 32)
							{
								var id = requestSegments[3].Left(32);
								var filename = requestSegments[3].Right(requestSegments[3].Length - 33);
								requestSegments[3] = id;
								requestSegments.Insert(4, filename);
								requestSegments = requestSegments.Take(5).ToList();
							}

							else if (requestSegments[0].IsStartsWith("thumbnail") && requestSegments[5].Contains("-") && requestSegments[5].Length > 32)
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
							}); ;
						}

						// flush the response stream as final step
						await context.FlushAsync(cts.Token).ConfigureAwait(false);
					}
				}
				catch (OperationCanceledException) { }
				catch (Exception ex)
				{
					await context.WriteLogsAsync("Http.Process.Requests", $"Error occurred => {context.Request.Method} {requestURI}", ex, Global.ServiceName, LogLevel.Error).ConfigureAwait(false);
					var query = context.ParseQuery();
					if (ex is AccessDeniedException && !context.IsAuthenticated() && Handler.RedirectToPassportOnUnauthorized && !query.ContainsKey("x-app-token") && !query.ContainsKey("x-passport-token"))
						context.Redirect(context.GetPassportSessionAuthenticatorUrl());
					else
					{
						if (ex is WampException)
						{
							var wampException = (ex as WampException).GetDetails();
							context.ShowHttpError(statusCode: wampException.Item1, message: wampException.Item2, type: wampException.Item3, correlationID: context.GetCorrelationID(), stack: wampException.Item4 + "\r\n\t" + ex.StackTrace, showStack: Global.IsDebugLogEnabled);
						}
						else
							context.ShowHttpError(statusCode: ex.GetHttpStatusCode(), message: ex.Message, type: ex.GetTypeName(true), correlationID: context.GetCorrelationID(), ex: ex, showStack: Global.IsDebugLogEnabled);
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
						using (var cts = CancellationTokenSource.CreateLinkedTokenSource(Global.CancellationTokenSource.Token, context.RequestAborted))
						{
							try
							{
								systemIdentityJson = systemIdentityJson ?? await context.CallServiceAsync(new RequestInfo(session, "Portals", "Identify.System", "GET", queryString, headers, null, extra, context.GetCorrelationID()), cts.Token, Global.Logger, "Http.Process.Requests").ConfigureAwait(false) as JObject;
								await this.ProcessLogInRequestAsync(context, systemIdentityJson?.Get<string>("ID")).ConfigureAwait(false);
							}
							catch (Exception ex)
							{
								await context.WriteLogsAsync("Http.Authentication", $"Error occurred while logging in => {ex.Message}", ex).ConfigureAwait(false);
								context.ShowHttpError(ex.GetHttpStatusCode(), ex.Message, ex.GetType().GetTypeName(true), context.GetCorrelationID(), ex, Global.IsDebugLogEnabled);
							}
						}
						break;

					case "logout":
						await this.ProcessLogOutRequestAsync(context).ConfigureAwait(false);
						break;

					case "service":
						using (var cts = CancellationTokenSource.CreateLinkedTokenSource(Global.CancellationTokenSource.Token, context.RequestAborted))
						{
							var requestInfo = new RequestInfo(session, queryString["service-name"], queryString["object-name"], "GET", queryString, headers, null, extra, context.GetCorrelationID());
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
						context.ShowHttpError(invalidException.GetHttpStatusCode(), invalidException.Message, invalidException.GetType().GetTypeName(true), context.GetCorrelationID(), invalidException, Global.IsDebugLogEnabled);
						break;
				}
		}

		internal static void InitializeWebSocket()
		{
			Handler.WebSocket = new Components.WebSockets.WebSocket(Logger.GetLoggerFactory(), Global.CancellationTokenSource.Token)
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
			await context.FlushAsync(Global.CancellationTokenSource.Token).ConfigureAwait(false);
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

					await context.WriteAsync(scripts, "application/javascript", context.GetCorrelationID(), Global.CancellationTokenSource.Token).ConfigureAwait(false);
				}
			}
			catch (Exception ex)
			{
				await Task.WhenAll(
					context.WriteAsync($"console.error('Error occurred while validating => {ex.Message.Replace("'", @"\'")}')", "application/javascript", context.GetCorrelationID(), Global.CancellationTokenSource.Token),
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
			await context.FlushAsync(Global.CancellationTokenSource.Token).ConfigureAwait(false);
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
				}, Global.CancellationTokenSource.Token).ConfigureAwait(false);

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
				}, Global.CancellationTokenSource.Token).ConfigureAwait(false);

				// prepare url for redirecting
				var token = session.GetAuthenticateToken(payload => payload["dev"] = session.DeviceID);
				var redirectUrl = context.GetQueryParameter("ReturnUrl");
				if (string.IsNullOrWhiteSpace(redirectUrl))
					redirectUrl = context.GetReferUrl();
				redirectUrl += (redirectUrl.IndexOf("?") < 0 ? "?" : "&") + $"x-passport-token={token}";
				context.Redirect(redirectUrl);
				await context.FlushAsync(Global.CancellationTokenSource.Token).ConfigureAwait(false);
			}
			catch (Exception ex)
			{
				await context.WriteLogsAsync("Http.Authentication", $"Error occurred while logging out => {ex.Message}", ex).ConfigureAwait(false);
				context.ShowHttpError(ex.GetHttpStatusCode(), ex.Message, ex.GetType().GetTypeName(true), context.GetCorrelationID(), ex, Global.IsDebugLogEnabled);
			}
		}

		#region Static properties and working with API Gateway Router
		internal static string NodeName => Extensions.GetUniqueName(Global.ServiceName + ".http");

		internal static bool RedirectToPassportOnUnauthorized => "true".IsEquals(UtilityService.GetAppSetting("Portals:RedirectToPassportOnUnauthorized", "true"));

		public static List<string> ExcludedHeaders { get; } = UtilityService.GetAppSetting("ExcludedHeaders", "connection,accept,accept-encoding,accept-language,cache-control,cookie,host,content-type,content-length,user-agent,upgrade-insecure-requests,purpose,ms-aspnetcore-token,x-forwarded-for,x-forwarded-proto,x-forwarded-port,x-original-for,x-original-proto,x-original-remote-endpoint,x-original-port,cdn-loop").ToList();

		internal static void Connect(int waitingTimes = 6789)
		{
			Global.Logger.LogDebug($"Attempting to connect to API Gateway Router [{new Uri(Router.GetRouterStrInfo()).GetResolvedURI()}]");
			Global.Connect(
				(sender, arguments) =>
				{
					Global.Logger.LogDebug($"Incoming channel to API Gateway Router is established - Session ID: {arguments.SessionId}");
					Task.Run(() => Router.IncomingChannel.UpdateAsync(Router.IncomingChannelSessionID, Global.ServiceName, $"Incoming ({Global.ServiceName} HTTP service)")).ConfigureAwait(false);
					Global.PrimaryInterCommunicateMessageUpdater?.Dispose();
					Global.PrimaryInterCommunicateMessageUpdater = Router.IncomingChannel.RealmProxy.Services
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
					Global.SecondaryInterCommunicateMessageUpdater = Router.IncomingChannel.RealmProxy.Services
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
				(sender, arguments) =>
				{
					Global.Logger.LogDebug($"Outgoing channel to API Gateway Router is established - Session ID: {arguments.SessionId}");
					Task.Run(async () =>
					{
						await Router.OutgoingChannel.UpdateAsync(Router.OutgoingChannelSessionID, Global.ServiceName, $"Outgoing ({Global.ServiceName} HTTP service)").ConfigureAwait(false);
						try
						{
							await Task.WhenAll(
								Global.InitializeLoggingServiceAsync(),
								Global.InitializeRTUServiceAsync()
							).ConfigureAwait(false);
							Global.Logger.LogInformation("Helper services are succesfully initialized");
						}
						catch (Exception ex)
						{
							Global.Logger.LogError($"Error occurred while initializing helper services: {ex.Message}", ex);
						}
					})
					.ContinueWith(async task => await Global.RegisterServiceAsync().ConfigureAwait(false), TaskContinuationOptions.OnlyOnRanToCompletion)
					.ConfigureAwait(false);
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

		static Task ProcessInterCommunicateMessageAsync(CommunicateMessage message)
			=> Task.CompletedTask;
		#endregion

	}
}
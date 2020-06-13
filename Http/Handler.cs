#region Related components
using System;
using System.Net;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
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
		HashSet<string> SpecialRequests { get; } = "_initializer,_validator,_logout,_signout".ToHashSet();

		string _alwaysUseSecureConnections = null, _useRelativeURLs = null;

		bool AlwaysUseSecureConnections
		{
			get
			{
				this._alwaysUseSecureConnections = this._alwaysUseSecureConnections ?? UtilityService.GetAppSetting("AlwaysUseSecureConnections", "false");
				 return "true".IsEquals(this._alwaysUseSecureConnections);
			}
		}

		bool UseRelativeURLs
		{
			get
			{
				this._useRelativeURLs = this._useRelativeURLs ?? UtilityService.GetAppSetting("Portals:UseRelativeURLs", "true");
				return "true".IsEquals(this._useRelativeURLs);
			}
		}

		string LoadBalancingHealthCheckUrl { get; } = UtilityService.GetAppSetting("HealthCheckUrl", "/load-balancing-health-check");

		RequestDelegate Next { get; }

		public Handler(RequestDelegate next) => this.Next = next;

		public async Task Invoke(HttpContext context)
		{
			// CORS: allow origin
			context.Response.Headers["Access-Control-Allow-Origin"] = "*";

			// CORS: options
			if (context.Request.Method.IsEquals("OPTIONS"))
			{
				var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
				{
					["Access-Control-Allow-Methods"] = "HEAD,GET"
				};
				if (context.Request.Headers.TryGetValue("Access-Control-Request-Headers", out var requestHeaders))
					headers["Access-Control-Allow-Headers"] = requestHeaders;
				context.SetResponseHeaders((int)HttpStatusCode.OK, headers);
				await context.FlushAsync(Global.CancellationTokenSource.Token).ConfigureAwait(false);
			}

			// load balancing health check
			else if (context.Request.Path.Value.IsEquals(this.LoadBalancingHealthCheckUrl))
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

		internal async Task ProcessRequestAsync(HttpContext context)
		{
			// prepare
			context.SetItem("PipelineStopwatch", Stopwatch.StartNew());
			var requestUri = context.GetRequestUri();
			var requestPath = requestUri.GetRequestPathSegments(true).First();

			if (Global.IsVisitLogEnabled)
				await context.WriteVisitStartingLogAsync().ConfigureAwait(false);

			// request to favicon.ico file
			if (requestPath.Equals("favicon.ico"))
				await context.ProcessFavouritesIconFileRequestAsync().ConfigureAwait(false);

			// request to static segments
			else if (Global.StaticSegments.Contains(requestPath))
				await context.ProcessStaticFileRequestAsync().ConfigureAwait(false);

			else if (this.AlwaysUseSecureConnections && !requestUri.Scheme.IsEquals("https"))
				context.Redirect($"https://{requestUri.Host}{requestUri.PathAndQuery}");

			// request of special sections (initializer, validator, log-out)
			else if (this.SpecialRequests.Contains(requestPath))
			{
				context.Items["PipelineStopwatch"] = Stopwatch.StartNew();
				context.Response.Headers["Access-Control-Allow-Origin"] = "*";
				switch (requestPath)
				{
					case "_initializer":
						await this.ProcessInitializerRequestAsync(context).ConfigureAwait(false);
						break;

					case "_validator":
						await this.ProcessValidatorRequestAsync(context).ConfigureAwait(false);
						break;

					default:
						await this.ProcessLogOutRequestAsync(context).ConfigureAwait(false);
						break;
				}
			}

			// request of portal
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
			var systemIdentity = "";
			var queryString = context.Request.QueryString.ToDictionary(query =>
			{
				var pathSegments = context.GetRequestPathSegments();
				var requestSegments = pathSegments;

				// special parameters, like spider indicator (robots.txt)/ads indicator (ads.txt) or system/organization identity
				if (pathSegments.Length > 0 && !string.IsNullOrWhiteSpace(pathSegments[0]))
				{
					// indicator
					if (pathSegments[0].IsEndsWith(".txt"))
					{
						systemIdentity = "~indicators";
						query["x-indicator"] = pathSegments[0].Replace(".txt", "").ToLower();
						requestSegments = new string[] { };
					}

					// special resources (_assets, _images, _css, _js)
					else if (pathSegments[0].StartsWith("_"))
					{
						systemIdentity = "~resources";
						query["x-resource"] = pathSegments[0].Right(pathSegments[0].Length - 1).GetANSIUri(true, true);
						query["x-path"] = pathSegments.Skip(1).Join("/");
						requestSegments = new string[] { };
					}

					// system/oranization identity
					else if (pathSegments[0].StartsWith("~"))
					{
						systemIdentity = pathSegments[0].Right(pathSegments[0].Length - 1).Replace(".html", "").GetANSIUri(true, false);
						query["x-system"] = systemIdentity;
						requestSegments = pathSegments.Skip(1).ToArray();
					}
				}

				// parameters of desktop and contents
				if (requestSegments.Length > 0)
				{
					var value = requestSegments[0].Replace(".html", "");
					value = value.Equals("") || value.StartsWith("-") || value.IsEquals("default") || value.IsEquals("index") || value.IsNumeric() ? "default" : value.GetANSIUri();
					query["x-desktop"] = (value.Equals("default") ? "-" : "") + value;

					value = requestSegments.Length > 1 && !string.IsNullOrWhiteSpace(requestSegments[1]) ? requestSegments[1].Replace(".html", "") : null;
					query["x-parent"] = string.IsNullOrWhiteSpace(value) ? null : value.GetANSIUri();

					if (requestSegments.Length > 2 && !string.IsNullOrWhiteSpace(requestSegments[2]))
					{
						value = requestSegments[2].Replace(".html", "");
						if (value.IsNumeric())
							query["x-page"] = value;
						else
							query["x-content"] = value.GetANSIUri();

						if (requestSegments.Length > 3 && !string.IsNullOrWhiteSpace(requestSegments[3]))
						{
							value = requestSegments[3].Replace(".html", "");
							if (value.IsNumeric())
								query["x-page"] = value;
						}
					}
				}
				else if (!systemIdentity.IsEquals("~indicators") && !systemIdentity.IsEquals("~resources"))
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
				dictionary["x-host"] = context.GetParameter("Host");
				dictionary["x-url"] = "https".IsEquals(context.GetHeaderParameter("x-forwarded-proto") ?? context.GetHeaderParameter("x-original-proto")) && !"https".IsEquals(requestURI.Scheme)
					? requestURI.AbsoluteUri.Replace($"{requestURI.Scheme}://", "https://")
					: requestURI.AbsoluteUri;
				dictionary["x-relative-urls"] = this.UseRelativeURLs.ToString().ToLower();
			});

			// process the request
			try
			{
				if (!context.Request.Method.IsEquals("GET"))
					throw new MethodNotAllowedException(context.Request.Method);

				using (var cts = CancellationTokenSource.CreateLinkedTokenSource(Global.CancellationTokenSource.Token, context.RequestAborted))
				{
					// call Portals service to identify the system
					if (string.IsNullOrWhiteSpace(systemIdentity))
					{
						var info = await context.CallServiceAsync(new RequestInfo(session, "Portals", "Identify.System", "GET", queryString, headers, null, extra, context.GetCorrelationID()), cts.Token).ConfigureAwait(false) as JObject;
						queryString["x-system"] = info?.Get<string>("ID");
					}

					// call Portals service to process the request
					var response = (await context.CallServiceAsync(new RequestInfo(session, "Portals", "Process.Http.Request", "GET", queryString, headers, null, extra, context.GetCorrelationID()), cts.Token).ConfigureAwait(false)).ToExpandoObject();

					// write headers
					context.SetResponseHeaders(response.Get("StatusCode", 200), response.Get("Headers", new Dictionary<string, string>()));

					// write body
					var body = response.Get<string>("Body");
					if (body != null)
						await context.WriteAsync(response.Get("BodyAsPlainText", false) ? body.ToBytes() : body.Base64ToBytes().Decompress(response.Get("BodyEncoding", "deflate")), cts.Token).ConfigureAwait(false);

					// flush the response stream as final step
					await context.FlushAsync(cts.Token).ConfigureAwait(false);
				}
			}
			catch (OperationCanceledException) { }
			catch (Exception ex)
			{
				await context.WriteLogsAsync("Portals", $"Error occurred => {context.Request.Method} {requestURI}", ex, Global.ServiceName, LogLevel.Error).ConfigureAwait(false);
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
				await context.WriteLogsAsync("Passport", $"Error occurred while initializing: {ex.Message}", ex).ConfigureAwait(false);
			}

			context.Redirect(redirectUrl);
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
					context.WriteAsync($"console.error('Error occurred while validating: {ex.Message.Replace("'", @"\'")}')", "application/javascript", context.GetCorrelationID(), Global.CancellationTokenSource.Token),
					context.WriteLogsAsync("Passport", $"Error occurred while validating: {ex.Message}", ex)
				).ConfigureAwait(false);
			}
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
			}
			catch (Exception ex)
			{
				await context.WriteLogsAsync("Passport", $"Error occurred while signing-out: {ex.Message}", ex).ConfigureAwait(false);
				context.ShowHttpError(ex.GetHttpStatusCode(), ex.Message, ex.GetType().GetTypeName(true), context.GetCorrelationID(), ex, Global.IsDebugLogEnabled);
			}
		}

		#region Static properties and working with Router
		static string _RedirectToPassportOnUnauthorized = null;

		internal static string NodeName => Extensions.GetUniqueName(Global.ServiceName + ".http");

		internal static bool RedirectToPassportOnUnauthorized
			=> "true".IsEquals(Handler._RedirectToPassportOnUnauthorized ?? (Handler._RedirectToPassportOnUnauthorized = UtilityService.GetAppSetting("Portals:RedirectToPassportOnUnauthorized", "true")));

		public static List<string> ExcludedHeaders { get; } = UtilityService.GetAppSetting("ExcludedHeaders", "connection,accept,accept-encoding,accept-language,cache-control,cookie,host,content-type,content-length,user-agent,upgrade-insecure-requests,purpose,ms-aspnetcore-token,x-forwarded-for,x-forwarded-proto,x-forwarded-port,x-original-for,x-original-proto,x-original-remote-endpoint,x-original-port,cdn-loop,cf-ipcountry,cf-ray,cf-visitor,cf-connecting-ip,sec-fetch-site,sec-fetch-mode,sec-fetch-dest,sec-fetch-user").ToList();

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
											$"- Message: {message?.Data?.ToString(Global.IsDebugLogEnabled ? Newtonsoft.Json.Formatting.Indented : Newtonsoft.Json.Formatting.None)}"
										, null, Global.ServiceName, LogLevel.Information, correlationID).ConfigureAwait(false);
								}
								catch (Exception ex)
								{
									await Global.WriteLogsAsync(Global.Logger, "RTU", $"{ex.Message} => {message?.ToJson().ToString(Global.IsDebugLogEnabled ? Newtonsoft.Json.Formatting.Indented : Newtonsoft.Json.Formatting.None)}", ex, Global.ServiceName, LogLevel.Error, correlationID).ConfigureAwait(false);
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
												$"- Message: {message?.Data?.ToString(Global.IsDebugLogEnabled ? Newtonsoft.Json.Formatting.Indented : Newtonsoft.Json.Formatting.None)}"
											, null, Global.ServiceName, LogLevel.Information, correlationID).ConfigureAwait(false);
									}
									catch (Exception ex)
									{
										await Global.WriteLogsAsync(Global.Logger, "RTU", $"{ex.Message} => {message?.ToJson().ToString(Global.IsDebugLogEnabled ? Newtonsoft.Json.Formatting.Indented : Newtonsoft.Json.Formatting.None)}", ex, Global.ServiceName, LogLevel.Error, correlationID).ConfigureAwait(false);
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
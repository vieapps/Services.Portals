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
using net.vieapps.Components.WebSockets;
#endregion

namespace net.vieapps.Services.Portals
{
	public class APIsHandler
	{
		public APIsHandler(RequestDelegate _) { }

		public Task Invoke(HttpContext context)
		{
			// request of WebSocket
			if (context.WebSockets.IsWebSocketRequest)
				return Task.WhenAll
				(
					Global.IsVisitLogEnabled ? context.WriteLogsAsync(Global.Logger, "APIs", $"Wrap a WebSocket connection successful\r\n- Endpoint: {context.GetRemoteIPAddress()}:{context.Connection.RemotePort}\r\n- URI: {context.GetRequestUri()}{(Global.IsDebugLogEnabled || context.ContainsKey("x-logs") ? $"\r\n- Headers:\r\n\t{context.Request.Headers.Select(kvp => $"{kvp.Key}: {kvp.Value}").Join("\r\n\t")}" : "")}") : Task.CompletedTask,
					APIsHandler.WebSocket.WrapAsync(context)
				);

			// CORS: allow origin
			context.Response.Headers.AccessControlAllowOrigin = "*";

			// CORS: options
			if (context.Request.Method.IsEquals("OPTIONS"))
			{
				var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
				{
					["Access-Control-Allow-Methods"] = "HEAD,GET,POST,PUT,PATCH,DELETE"
				};
				if (context.Request.Headers.TryGetValue("Access-Control-Request-Headers", out var requestHeaders))
					headers["Access-Control-Allow-Headers"] = requestHeaders;
				context.SetResponseHeaders((int)HttpStatusCode.OK, headers);
				return Task.CompletedTask;
			}

			// requests of the APIs
			if (context.IsBlackIP(context.GetRemoteIPAddress()))
			{
				context.SetResponseHeaders((int)HttpStatusCode.Forbidden);
				return Task.CompletedTask;
			}

			context.SetItem("PipelineStopwatch", Stopwatch.StartNew());
			return context.ProcessAPIsRequestAsync();
		}

		internal static Components.WebSockets.WebSocket WebSocket { get; } = new(Logger.GetLoggerFactory(), Global.CancellationToken)
		{
			KeepAliveInterval = TimeSpan.FromSeconds(Int32.TryParse(UtilityService.GetAppSetting("Proxy:KeepAliveInterval", "45"), out var interval) ? interval : 45),
			OnError = (websocket, exception) => Global.WriteLogsAsync(Global.Logger, "APIs", $"Got an error while processing => {exception.Message} ({websocket?.ID} {websocket?.RemoteEndPoint})", exception).Execute(),
			OnConnectionEstablished = websocket => (websocket == null ? Task.CompletedTask : websocket.PrepareAPIsAsync()).Execute(),
			OnConnectionBroken = websocket => (websocket == null ? Task.CompletedTask : websocket.DisconnectAPIsAsync()).Execute(),
			OnMessageReceived = (websocket, result, data) => (websocket == null ? Task.CompletedTask : websocket.ProcessAPIsRequestAsync(result, data)).Execute()
		};
	}

	internal static class APIsHandlerExtensions
	{
		public static async Task ProcessAPIsRequestAsync(this HttpContext context, string[] requestSegments = null)
		{
			var writeVisitLog = Global.IsVisitLogEnabled && requestSegments == null;
			if (writeVisitLog)
				await context.WriteVisitStartingLogAsync().ConfigureAwait(false);

			using var cts = CancellationTokenSource.CreateLinkedTokenSource(Global.CancellationToken, context.RequestAborted);
			var isDebugLogEnabled = Global.IsDebugLogEnabled || context.ContainsKey("x-logs");
			requestSegments ??= context.GetRequestPathSegments().Skip(1).ToArray();

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

				var serviceName = requestSegments.Length > 0 && !string.IsNullOrWhiteSpace(requestSegments[0])
					? requestSegments[0].GetANSIUri(false, true)
					: "unknown";
				var objectName = requestSegments.Length > 1
					? requestSegments[1].GetANSIUri(false, true)
					: "";
				var objectIdentity = requestSegments.Length > 2
					? requestSegments[2].GetANSIUri(false, true)
					: "";

				queryString["service-name"] = ".well-known".IsEquals(serviceName) ? "discovery" : serviceName;
				queryString["object-name"] = objectName;
				queryString["object-identity"] = objectIdentity;

				if (requestSegments.Length > 3 && !objectIdentity.IsValidUUID())
					queryString["object-extra-identity"] = requestSegments[3].GetANSIUri(false, true);
			});

			var headers = context.Request.Headers.ToDictionary(header =>
			{
				Handler.ExcludedHeaders.ForEach(name => header.Remove(name));
				header.Keys.Where(name => name.IsStartsWith("cf-") || name.IsStartsWith("sec-")).ToList().ForEach(name => header.Remove(name));
				header["x-requester"] = context.TryGetParameter("x-requester", out var requester) ? requester : "vieapps-ngx-portals";
			});

			string body = null;
			if (context.Request.Method.IsEquals("POST") || context.Request.Method.IsEquals("PUT") || context.Request.Method.IsEquals("PATCH"))
				try
				{
					body = await context.ReadTextAsync(cts.Token).ConfigureAwait(false);
				}
				catch (Exception ex)
				{
					await context.WriteLogsAsync(Global.Logger, "APIs", $"Error occurred while parsing body of the request => {ex.Message}", ex).ConfigureAwait(false);
				}
			else if (context.Request.Method.IsEquals("GET") && query.Remove("x-body", out var encodedBody))
				try
				{
					body = encodedBody.Url64Decode();
				}
				catch (Exception ex)
				{
					await context.WriteLogsAsync(Global.Logger, "APIs", $"Error occurred while parsing body of the 'x-body' parameter => {ex.Message}", ex).ConfigureAwait(false);
				}

			var extra = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			if (query.Remove("x-request-extra", out var extraInfo) && !string.IsNullOrWhiteSpace(extraInfo))
				try
				{
					extra = extraInfo.Url64Decode().ToExpandoObject().ToDictionary(kvp => kvp.Key, kvp => kvp.Value?.ToString(), StringComparer.OrdinalIgnoreCase);
				}
				catch (Exception ex)
				{
					await context.WriteLogsAsync(Global.Logger, "APIs", $"Error occurred while parsing body of the 'x-request-extra' parameter => {ex.Message}", ex).ConfigureAwait(false);
				}

			var requestInfo = new RequestInfo(context.GetSession(), query["service-name"], query["object-name"], context.Request.Method, query, headers, body, extra, context.GetCorrelationID());
			if ("discovery".IsEquals(requestInfo.ServiceName) && "definitions".IsEquals(requestInfo.ObjectName))
			{
				requestInfo.ServiceName = requestInfo.Query["service-name"] = requestInfo.Query["x-service-name"].GetANSIUri(true, true).GetCapitalizedFirstLetter();
				requestInfo.Query["object-identity"] = requestInfo.Query["x-object-name"];
				requestInfo.Query["mode"] = requestInfo.Query.TryGetValue("x-object-identity", out var mode) ? mode : "";
				requestInfo.Verb = "GET";
			}

			if (Handler.TrackSessions)
				requestInfo.SendSessionState(Handler.TrackAPISessions);
			else
				requestInfo.TrackStatistics();

			try
			{
				var response = await context.CallServiceAsync(requestInfo, cts.Token, Global.Logger, "APIs").ConfigureAwait(false);
				headers = response.Get("Headers", new Dictionary<string, string>());
				if (headers.TryGetValue("X-Node", out var nodeID))
					headers["X-Service-Node"] = nodeID;
				headers = new Dictionary<string, string>(headers, StringComparer.OrdinalIgnoreCase)
				{
					["X-Correlation-ID"] = context.GetCorrelationID(),
					["X-Node"] = Global.NodeID
				};
				await Task.WhenAll
				(
					context.WriteAsync(response, headers, cts.Token),
					isDebugLogEnabled ? context.WriteLogsAsync("APIs", $"Successfully process request of a service {response}") : Task.CompletedTask
				).ConfigureAwait(false);
			}
			catch (OperationCanceledException) { }
			catch (Exception ex)
			{
				context.WriteError(Global.Logger, ex, requestInfo, $"Error occurred while calling a service => {ex.Message}", true, "APIs");
			}

			if (writeVisitLog)
				await context.WriteVisitFinishingLogAsync().ConfigureAwait(false);
		}

		public static Task PrepareAPIsAsync(this ManagedWebSocket websocket)
		{
			var session = websocket.Get<Session>("Session");
			if (session == null)
			{
				var context = Global.CurrentHttpContext;
				websocket.Set("Session", session = context?.GetSession());
				session?.SendSessionState("Users", "CONNECT /session", false, Handler.TrackAPISessions);
				if (context != null && context.ContainsKey("x-logs"))
					return Global.WriteLogsAsync(Global.Logger, "APIs", $"A websocket connection was established {websocket.RemoteEndPoint}\r\nSession:{session.ToJson()}");
			}
			return Task.CompletedTask;
		}

		public static Task DisconnectAPIsAsync(this ManagedWebSocket websocket)
		{
			if (websocket.Remove("Session", out Session session) && session != null && Handler.TrackSessions)
				session.SendSessionState("Users", "DISCONNECT /session", false, Handler.TrackAPISessions);
			return Task.CompletedTask;
		}

		public static async Task ProcessAPIsRequestAsync(this ManagedWebSocket websocket, WebSocketReceiveResult result, byte[] data)
		{
			// receive continuous messages
			object requestMsg;
			if (!result.EndOfMessage)
			{
				websocket.Extra["Message"] = websocket.Extra.TryGetValue("Message", out requestMsg) ? (requestMsg as byte[]).Concat(data) : data;
				return;
			}

			// last message or single small message
			var stopwatch = Stopwatch.StartNew();
			var correlationID = UtilityService.NewUUID;

			if (websocket.Extra.TryGetValue("Message", out requestMsg))
			{
				requestMsg = (requestMsg as byte[]).Concat(data);
				websocket.Extra.Remove("Message");
			}
			else
				requestMsg = data;

			// prepare the request
			JToken requestJson = null;
			try
			{
				requestJson = (requestMsg as byte[])?.GetString()?.ToJSON();
			}
			catch (Exception ex)
			{
				await Global.WriteLogsAsync(Global.Logger, "APIs", $"Invalid message => {ex.Message}", ex, Global.ServiceName, LogLevel.Error, correlationID).ConfigureAwait(false);
				return;
			}

			var requestID = requestJson.Get<string>("ID");
			var serviceName = requestJson.Get("ServiceName", "").GetANSIUri(true, true);
			var objectName = requestJson.Get("ObjectName", "").GetANSIUri(true, true);
			var verb = requestJson.Get("Verb", "GET").ToUpper();
			var query = new Dictionary<string, string>(requestJson.Get<JObject>("Query")?.ToDictionary<string>() ?? [], StringComparer.OrdinalIgnoreCase);
			var header = new Dictionary<string, string>(requestJson.Get<JObject>("Header")?.ToDictionary<string>() ?? [], StringComparer.OrdinalIgnoreCase);
			var body = requestJson.Get("Body", new JObject());
			var extra = new Dictionary<string, string>(requestJson.Get<JObject>("Extra")?.ToDictionary<string>() ?? [], StringComparer.OrdinalIgnoreCase);
			query.TryGetValue("object-identity", out var objectIdentity);

			var isDebugLogEnabled = Global.IsDebugLogEnabled || header.ContainsKey("x-logs") || query.ContainsKey("x-logs");
			var session = websocket.Get<Session>("Session") ?? Global.GetSession();

			// visit logs
			if (Global.IsVisitLogEnabled || isDebugLogEnabled)
				await Global.WriteLogsAsync(Global.Logger, "Http.Visits",
					$"Request starting {verb} " + $"/~apis/{serviceName.ToLower()}{(string.IsNullOrWhiteSpace(objectName) ? "" : $"/{objectName.ToLower()}")}{(string.IsNullOrWhiteSpace(objectIdentity) ? "" : $"/{objectIdentity}")}".ToLower() + (query.TryGetValue("x-request", out var xrequest) ? $"?x-request={xrequest}" : "") + " HTTPWS/1.1" + " \r\n" +
					$"- App: {session.AppName ?? "Unknown"} @ {session.AppPlatform ?? "Unknown"} [{session.AppAgent ?? "Unknown"}]" + " \r\n" +
					$"- WebSocket: {websocket.ID} @ {websocket.RemoteEndPoint}"
				, null, Global.ServiceName, LogLevel.Information, correlationID).ConfigureAwait(false);

			// process the request
			try
			{
				// register/authenticate a session
				if (serviceName.IsEquals("Session") && (verb.IsEquals("REG") || verb.IsEquals("AUTH")))
				{
					session.AppName = header.TryGetValue("x-app-name", out var appName) && !string.IsNullOrWhiteSpace(appName) ? appName : session.AppName;
					session.AppPlatform = header.TryGetValue("x-app-platform", out var appPlatform) && !string.IsNullOrWhiteSpace(appPlatform) ? appPlatform : session.AppPlatform;

					if (verb.IsEquals("REG"))
					{
						session.DeviceID = header.TryGetValue("x-device-id", out var deviceID) && !string.IsNullOrWhiteSpace(deviceID) ? deviceID : string.IsNullOrWhiteSpace(session.DeviceID) ? $"{UtilityService.NewUUID}@vieapps-ngx" : session.DeviceID;
						websocket.Set("Status", "Registered");
						if (Handler.TrackSessions)
							session.SendSessionState("Users", "REG /session", true, Handler.TrackAPISessions);
					}

					else
					{
						var appToken = body?.Get<string>("x-app-token") ?? "";
						await Global.UpdateWithAuthenticateTokenAsync(session, appToken, Handler.ExpiresAfter, null, null, null, Global.Logger, "Authentications", correlationID).ConfigureAwait(false);
						if (!string.IsNullOrWhiteSpace(session.User.ID) && !await session.IsSessionExistAsync(Global.Logger, "Authentications", correlationID).ConfigureAwait(false))
							throw new InvalidSessionException("Session is invalid (The session is not issued by the system)");

						var encryptionKey = session.GetEncryptionKey(Global.EncryptionKey);
						var encryptionIV = session.GetEncryptionIV(Global.EncryptionKey);

						if (!header.TryGetValue("x-session-id", out var sessionID) || !sessionID.Decrypt(encryptionKey, encryptionIV).Equals(session.GetEncryptedID()))
						{
							if (Global.IsDebugLogEnabled)
								await Global.WriteLogsAsync(Global.Logger, "Authentications", $"The session identity is invalid [{session.GetEncryptedID()} != {(sessionID ?? "").Decrypt(encryptionKey, encryptionIV)}]", null, Global.ServiceName, LogLevel.Error, correlationID).ConfigureAwait(false);
							throw new InvalidSessionException("Session is invalid (The session is not issued by the system)");
						}

						if (!header.TryGetValue("x-device-id", out var deviceID) || !deviceID.Decrypt(encryptionKey, encryptionIV).Equals(session.DeviceID))
						{
							if (Global.IsDebugLogEnabled)
								await Global.WriteLogsAsync(Global.Logger, "Authentications", $"The device identity is invalid [{session.DeviceID} != {(deviceID ?? "").Decrypt(encryptionKey, encryptionIV)}]", null, Global.ServiceName, LogLevel.Error, correlationID).ConfigureAwait(false);
							throw new InvalidSessionException("Session is invalid (The session is not issued by the system)");
						}

						session.AppName = body?.Get<string>("x-app-name") ?? session.AppName;
						session.AppPlatform = body?.Get<string>("x-app-platform") ?? session.AppPlatform;
						websocket.Set("Status", "Authenticated");
						websocket.Set("Token", JSONWebToken.DecodeAsJson(appToken, Global.JWTKey));
					}

					websocket.Set("Session", session);
					await websocket.PrepareConnectionInfoAsync(correlationID, session, Global.CancellationToken, Global.Logger).ConfigureAwait(false);
					if (Handler.TrackSessions)
						session.SendSessionState("Users", "AUTH /session", true, Handler.TrackAPISessions);
					if (Global.IsDebugLogEnabled)
						await Global.WriteLogsAsync(Global.Logger, "Authentications", $"Successfully {(verb.IsEquals("REG") ? "register" : "authenticate")} a WebSocket connection\r\n{websocket.GetConnectionInfo(session)}\r\n- Status: {websocket.Get<string>("Status")}", null, Global.ServiceName, LogLevel.Information, correlationID).ConfigureAwait(false);
				}

				// send communicate message
				else if ("CommunicateMessage".IsEquals(requestJson.Get<string>("Type")))
				{
					var messages = new List<CommunicateMessage>();
					if (serviceName.IsEquals("Files") && objectName.IsEquals("PrepareCache"))
					{
						serviceName = body?.Get<string>("service-name");
						var systemID = body?.Get<string>("system-id");
						var objectID = body?.Get<string>("object-id");
						body?.Get<JArray>("attachments")?.ForEach(attachment => messages.Add(new CommunicateMessage("Files")
						{
							Type = "PrepareCache",
							Data = new JObject
							{
								{ "ServiceName", serviceName },
								{ "SystemID", systemID },
								{ "ObjectID", objectID },
								{ "ID", attachment.Get<string>("id") },
								{ "Filename", attachment.Get<string>("filename") },
								{ "ContentType", attachment.Get<string>("content-type") },
								{ "X-Type", "Attachment" },
								{ "X-Logs", isDebugLogEnabled },
								{ "X-Correlation-ID", correlationID }
							}
						}));
					}
					else
						messages.Add(new CommunicateMessage(serviceName)
						{
							Type = requestJson.Get<string>("MessageType"),
							Data = body
						});

					messages.ForEach(message => message.Send());

					var response = new JObject
					{
						["Type"] = "CommunicateMessage",
						["Data"] = new JObject
						{
							["Status"] = "Success",
							["CorrelationID"] = correlationID
						}
					};
					if (!string.IsNullOrWhiteSpace(requestID))
						response["ID"] = requestID;

					await websocket.SendAsync(response, Global.CancellationToken).ConfigureAwait(false);
					if (isDebugLogEnabled)
						await Global.WriteLogsAsync(Global.Logger, "APIs", $"Send communnicate messages successful\r\n{messages.Select(message => message.ToJson().ToString()).Join("\r\n")}", null, Global.ServiceName, LogLevel.Information, correlationID).ConfigureAwait(false);
				}

				// call a service
				else
				{
					var requestInfo = new RequestInfo(session, serviceName, objectName, verb, query, header, body?.ToString(Formatting.None), extra, correlationID);
					if ("discovery".IsEquals(requestInfo.ServiceName) && "definitions".IsEquals(requestInfo.ObjectName))
					{
						requestInfo.ServiceName = requestInfo.Query["service-name"] = requestInfo.Query["x-service-name"].GetANSIUri(true, true).GetCapitalizedFirstLetter();
						requestInfo.Query["object-identity"] = requestInfo.Query["x-object-name"];
						requestInfo.Query["mode"] = requestInfo.Query.TryGetValue("x-object-identity", out var mode) ? mode : "";
						requestInfo.Verb = "GET";
					}

					if (Handler.TrackSessions)
						requestInfo.SendSessionState(Handler.TrackAPISessions);
					else
						requestInfo.TrackStatistics();

					var response = new JObject
					{
						["Type"] = $"{requestInfo.ServiceName}#{requestInfo.ObjectName}#{verb.GetCapitalizedFirstLetter()}",
						["Data"] = await Global.CallServiceAsync(requestInfo, Global.CancellationToken, Global.Logger, "Http.Process.Requests").ConfigureAwait(false)
					};
					if (!string.IsNullOrWhiteSpace(requestID))
						response["ID"] = requestID;

					await websocket.SendAsync(response, Global.CancellationToken).ConfigureAwait(false);
					if (isDebugLogEnabled)
						await Global.WriteLogsAsync(Global.Logger, objectName, $"Process a request successful\r\nRequest: {requestInfo.ToString()}\r\nResponse: {response}", null, serviceName, LogLevel.Information, correlationID).ConfigureAwait(false);
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
						code = wampDetails.Code;
						message = wampDetails.Message;
						type = wampDetails.Type;
						stacks = new JArray { wampDetails.Stack };
						var inner = wampDetails.InnerJSON;
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
				catch (Exception exception)
				{
					await Global.WriteLogsAsync(Global.Logger, "APIs", $"Cannot send an error to client via WebSocket => {exception.Message}", exception, Global.ServiceName, LogLevel.Error, correlationID).ConfigureAwait(false);
				}
			}
			finally
			{
				stopwatch.Stop();
				if (Global.IsVisitLogEnabled || isDebugLogEnabled)
					await Global.WriteLogsAsync(Global.Logger, "Http.Visits", $"Request finished in {stopwatch.GetElapsedTimes()}", null, Global.ServiceName, LogLevel.Information, correlationID).ConfigureAwait(false);
			}
		}
	}
}
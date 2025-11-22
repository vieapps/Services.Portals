#region Related components
using System;
using System.Net;
using System.Linq;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Concurrent;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using WampSharp.V2.Core.Contracts;
using net.vieapps.Components.Utility;
using net.vieapps.Components.Security;
using static net.vieapps.Services.Portals.McpHandler;
#endregion

namespace net.vieapps.Services.Portals
{
	public class McpHandler
	{
		public McpHandler(RequestDelegate _) { }

		public async Task Invoke(HttpContext context)
		{
			if (!context.Request.Method.IsEquals("OPTIONS"))
			{
				await context.ProcessMcpRequestAsync().ConfigureAwait(false);
				if (Global.IsVisitLogEnabled)
					await context.WriteVisitFinishingLogAsync().ConfigureAwait(false);
			}
		}

		internal static ConcurrentDictionary<string, Settings.McpSettings> Settings { get; } = new ConcurrentDictionary<string, Settings.McpSettings>(StringComparer.OrdinalIgnoreCase);

		internal static ConcurrentDictionary<string, (string SessionID, string IP, long LastActivity)> Sessions { get; } = new ConcurrentDictionary<string, (string SessionID, string IP, long LastActivity)>(StringComparer.OrdinalIgnoreCase);

		internal static List<string> SupportedProtocols { get; } = new() { "2025-06-18" };

		internal static void SyncSessionInfo() => McpHandlerExtensions.SyncSessionInfo();

		#region Exceptions
		public class MalformedMcpRequestException : AppException
		{
			public MalformedMcpRequestException() : base("Bad request") { }
			public MalformedMcpRequestException(string message) : base(message) { }
			public MalformedMcpRequestException(string message, Exception innerException) : base(message, innerException) { }
		}

		public class InvalidMcpProtocolException : AppException
		{
			public InvalidMcpProtocolException()	: base("Invalid protocol") { }
			public InvalidMcpProtocolException(string message) : base(message) { }
			public InvalidMcpProtocolException(string message, Exception innerException) : base(message, innerException) { }
		}

		public class InvalidMcpSessionException : AppException
		{
			public InvalidMcpSessionException() : base("Invalid session") { }
			public InvalidMcpSessionException(string message) : base(message) { }
			public InvalidMcpSessionException(string message, Exception innerException) : base(message, innerException) { }
		}

		public class InvalidMcpRequestException : AppException
		{
			public InvalidMcpRequestException() : base("Invalid request") { }
			public InvalidMcpRequestException(string message) : base(message) { }
			public InvalidMcpRequestException(string message, Exception innerException) : base(message, innerException) { }
		}

		public class InvalidMcpBodyException : AppException
		{
			public InvalidMcpBodyException() : base("Invalid body") { }
			public InvalidMcpBodyException(string message) : base(message) { }
			public InvalidMcpBodyException(string message, Exception innerException) : base(message, innerException) { }
		}

		public class InvalidMcpMethodException : AppException
		{
			public InvalidMcpMethodException() : base("Method not found") { }
			public InvalidMcpMethodException(string message) : base(message) { }
			public InvalidMcpMethodException(string message, Exception innerException) : base(message, innerException) { }
		}

		public class InvalidMcpParamsException : AppException
		{
			public InvalidMcpParamsException() : base("Invalid params") { }
			public InvalidMcpParamsException(string message) : base(message) { }
			public InvalidMcpParamsException(string message, Exception innerException) : base(message, innerException) { }
		}
		#endregion

	}

	internal static class McpHandlerExtensions
	{
		public static async Task ProcessMcpRequestAsync(this HttpContext context)
		{
			if (!context.Request.Method.IsEquals("POST"))
			{
				await context.ShowErrorAsync(new MethodNotAllowedException()).ConfigureAwait(false);
				return;
			}

			// prepare
			var stopwatch = Stopwatch.StartNew();
			using var cts = CancellationTokenSource.CreateLinkedTokenSource(Global.CancellationToken, context.RequestAborted);

			var isDebugLogEnabled = Global.IsDebugLogEnabled || context.ContainsKey("x-logs");
			var headers = context.Request.Headers.ToDictionary(header =>
			{
				header["x-host"] = context.GetRequestUri().Host;
				header["x-brief"] = "1";
			});

			if (!headers.TryGetValue("MCP-Protocol-Version", out var mcpProtocolVersion) || McpHandler.SupportedProtocols.FirstOrDefault(version => version == mcpProtocolVersion) == null)
			{
				await context.ShowErrorAsync(new InvalidMcpProtocolException($"Protocol version ({mcpProtocolVersion}) is invalid - supported version(s): {McpHandler.SupportedProtocols.Join(", ")}")).ConfigureAwait(false);
				return;
			}

			// identify the system
			Settings.McpSettings mcpSettings;
			var requestInfo = new RequestInfo(context.GetSession(), "Portals", "Identify.System", "GET", context.Request.QueryString.ToDictionary(), headers, null, null, context.GetCorrelationID());
			try
			{
				var identifyJson = await context.IdentifySystemAsync(requestInfo, cts.Token).ConfigureAwait(false);
				var systemID = identifyJson.Get<string>("ID");
				var alias = identifyJson.Get<string>("Alias");
				await context.WriteLogsAsync("MCP", $"Process request [{systemID}/{alias}]").ConfigureAwait(false);

				if (!McpHandler.Settings.TryGetValue(systemID, out mcpSettings))
					throw new ServiceNotFoundException("Unavailable");

				if (string.IsNullOrWhiteSpace(mcpSettings.Name))
					mcpSettings.Name = $"{alias}-mcp";
			}
			catch (Exception ex)
			{
				await context.ShowErrorAsync(ex).ConfigureAwait(false);
				return;
			}

			// validate the JSON-RPC message
			JObject mcpRequest = null;
			string mcpRequestID = null, mcpMethod = null;
			var mcpInitializeStage = false;

			try
			{
				try
				{
					var msg = await context.ReadTextAsync(cts.Token).ConfigureAwait(false);
					mcpRequest = msg.ToJson() as JObject;
					if (mcpRequest == null)
						throw new InvalidMcpBodyException();
				}
				catch (InvalidMcpBodyException)
				{
					throw;
				}
				catch (Exception ex)
				{
					throw new MalformedMcpRequestException("Malformed", ex);
				}

				var jsonrpc = mcpRequest.Get<string>("jsonrpc");
				if (string.IsNullOrWhiteSpace(jsonrpc) || jsonrpc != "2.0")
					throw new InvalidMcpRequestException();

				mcpMethod = mcpRequest.Get<string>("method");
				if (string.IsNullOrWhiteSpace(mcpMethod))
					throw new InvalidMcpMethodException();

				mcpInitializeStage = mcpMethod.IsEquals("initialize");
				mcpRequestID = mcpRequest.Get<string>("id");
				if (string.IsNullOrWhiteSpace(mcpRequestID) && !mcpMethod.IsStartsWith("notifications/"))
					throw new InvalidMcpRequestException();
			}
			catch (Exception ex)
			{
				await context.ShowErrorAsync(ex, mcpRequestID).ConfigureAwait(false);
				return;
			}

			headers.TryGetValue("MCP-Session-ID", out var mcpSessionID);
			if (!mcpInitializeStage && (string.IsNullOrWhiteSpace(mcpSessionID) || !McpHandler.Sessions.ContainsKey(mcpSessionID)))
			{
				await context.ShowErrorAsync(new InvalidMcpSessionException($"Invalid session ({(string.IsNullOrWhiteSpace(mcpSessionID) ? "no identity" : "not found")})"), mcpRequestID).ConfigureAwait(false);
				return;
			}

			// track
			if (Handler.TrackSessions)
				requestInfo.SendSessionState(Handler.TrackAPISessions);
			else
				requestInfo.TrackStatistics();

			// process the request
			try
			{
				if (mcpInitializeStage)
					await context.ProcessInitializeRequestAsync(mcpSettings, mcpRequest, cts.Token).ConfigureAwait(false);

				else if (mcpMethod.IsEquals("tools/list"))
					await context.ProcessToolListRequestAsync(mcpSettings, mcpRequest, cts.Token).ConfigureAwait(false);

				else if (mcpMethod.IsEquals("tools/call"))
					await context.ProcessToolCallRequestAsync(mcpSettings, mcpRequest, cts.Token).ConfigureAwait(false);

				else if (mcpMethod.IsEquals("resources/templates/list"))
					await context.ProcessResourceTemplateListRequestAsync(mcpSettings, mcpRequest, cts.Token).ConfigureAwait(false);

				else if (mcpMethod.IsEquals("resources/list"))
					await context.ProcessResourceListRequestAsync(mcpSettings, mcpRequest, cts.Token).ConfigureAwait(false);

				else if (mcpMethod.IsEquals("resources/read"))
					await context.ProcessResourceReadRequestAsync(mcpSettings, mcpRequest, cts.Token).ConfigureAwait(false);

				else if (mcpMethod.IsStartsWith("notifications/"))
					context.SetResponseHeaders((int)HttpStatusCode.Accepted);

				else
					throw new NotImplementedException();

				if (!mcpInitializeStage)
					context.GetSession().SendSessionInfo(mcpSessionID);
			}
			catch (OperationCanceledException) { }
			catch (Exception ex)
			{
				await context.ShowErrorAsync(ex, mcpRequestID).ConfigureAwait(false);
			}

			await context.WriteLogsAsync("MCP", $"Request is completed - Execution times: {stopwatch.GetElapsedTimes()}").ConfigureAwait(false);
		}

		static Task ProcessInitializeRequestAsync(this HttpContext context, Settings.McpSettings mcpSettings, JObject mcpRequest, CancellationToken cancellationToken)
		{
			var protocolVersion = context.GetParameter("MCP-Protocol-Version");
			var sessionID = UtilityService.NewUUID;

			var result = new JObject
			{
				["protocolVersion"] = protocolVersion,
				["serverInfo"] = new JObject
				{
					["name"] = mcpSettings.Name,
					["version"] = typeof(McpHandler).Assembly.GetVersion(false)
				},
				["capabilities"] = new JObject
				{
					["tools"] = new JObject	{	["listChanged"] = true },
					["resources"] = new JObject { ["listChanged"] = true }
				}
			};

			var additional = string.IsNullOrWhiteSpace(mcpSettings.Instructions)
				? null
				: new JObject
				{
					["instructions"] = mcpSettings.Instructions
				};

			context.GetSession().SendSessionInfo(sessionID);

			return context.ShowResultAsync(mcpRequest.Get<string>("id"), result, additional, protocolVersion, sessionID, cancellationToken);
		}

		static Task ProcessToolListRequestAsync(this HttpContext context, Settings.McpSettings mcpSettings, JObject mcpRequest, CancellationToken cancellationToken)
		{
			var tools = new JArray();
			mcpSettings.Resources.ForEach(mcpResource => mcpResource.Tools.ForEach(mcpTool => tools.Add(new JObject
			{
				["name"] = $"{mcpResource.Name}.{mcpTool.Name}",
				["title"] = mcpTool.Title,
				["description"] = mcpTool.Description,
				["inputSchema"] = mcpTool.Schema
			})));
			return context.ShowResultAsync(mcpRequest.Get<string>("id"), new JObject
			{
				["tools"] = tools,
				["nextCursor"] = null
			}, cancellationToken);
		}

		static async Task ProcessToolCallRequestAsync(this HttpContext context, Settings.McpSettings mcpSettings, JObject mcpRequest, CancellationToken cancellationToken)
		{
			var mcpParams = mcpRequest.Get<JObject>("params");
			var name = mcpParams?.Get<string>("name")?.ToArray(".");
			if (name == null || name.Length != 2)
				throw new InvalidMcpParamsException();

			var mcpResource = mcpSettings.Resources.FirstOrDefault(resource => resource.Name == name.First());
			if (mcpResource == null)
				throw new InvalidMcpMethodException();

			var mcpTool = mcpResource.Tools.FirstOrDefault(tool => tool.Name == name.Last());
			if (mcpTool == null)
				throw new InvalidMcpMethodException();

			var requestInfo = new RequestInfo
			(
				context.GetSession(),
				mcpResource.ServiceName,
				mcpResource.Name,
				"tools/call",
				context.Request.QueryString.ToDictionary(query => query["object-identity"] = mcpTool.Name),
				context.Request.Headers.ToDictionary(header =>
				{
					header["x-system-id"] = mcpSettings.SystemID;
					header["x-requester"] = "vieapps-ngx-portals";
				}),
				mcpParams?.Get<JObject>("arguments")?.ToString(Formatting.None),
				null,
				context.GetCorrelationID()
			);
			
			try
			{
				var result = await requestInfo.ProcessRequestAsync(cancellationToken).ConfigureAwait(false);
				await context.ShowResultAsync(mcpRequest.Get<string>("id"), new JObject
				{
					["isError"] = false,
					["content"] = new JArray(new JObject
					{
						["type"] = "text",
						["text"] = result == null ? null : $"```json\n{result.ToString(Formatting.None) ?? "null"}\n```"
					})
				}, cancellationToken).ConfigureAwait(false);
			}
			catch (Exception ex)
			{
				await context.ShowErrorAsync(mcpRequest.Get<string>("id"), ex, cancellationToken).ConfigureAwait(false);
			}
		}

		static Task ProcessResourceTemplateListRequestAsync(this HttpContext context, Settings.McpSettings mcpSettings, JObject mcpRequest, CancellationToken cancellationToken)
		{
			var resourceTemplates = new JArray();
			mcpSettings.Resources.ForEach(mcpResource => resourceTemplates.Add(new JObject
			{
				["uriTemplate"] = $"{mcpResource.ServiceName}://{mcpResource.Name}/" + "{id}",
				["name"] = mcpResource.Name,
				["title"] = mcpResource.Title,
				["description"] = mcpResource.Description,
				["mimeType"] = "application/json",
				["annotations"] = new JObject
				{
					["category"] = mcpResource.Name
				}
			}));
			return context.ShowResultAsync(mcpRequest.Get<string>("id"), new JObject
			{
				["resourceTemplates"] = resourceTemplates,
				["nextCursor"] = null
			}, cancellationToken);
		}

		static async Task ProcessResourceListRequestAsync(this HttpContext context, Settings.McpSettings mcpSettings, JObject mcpRequest, CancellationToken cancellationToken)
		{
			var session = context.GetSession();
			var query = context.Request.QueryString.ToDictionary();
			var headers = context.Request.Headers.ToDictionary(header =>
			{
				header["x-system-id"] = mcpSettings.SystemID;
				header["x-requester"] = "vieapps-ngx-portals";
			});
			var correlationID = context.GetCorrelationID();
			try
			{
				var resources = new JArray();
				await mcpSettings.Resources.ForEachAsync(async mcpResource =>
				{
					var requestInfo = new RequestInfo
					(
						session,
						mcpResource.ServiceName,
						mcpResource.Name,
						"resources/list",
						query,
						headers,
						null,
						null,
						correlationID
					);
					var result = await requestInfo.ProcessRequestAsync(cancellationToken).ConfigureAwait(false);
					(result as JArray ?? result?.Get<JArray>("items"))?.Select(resource => resource as JObject).ToList().ForEach(resource =>
					{
						var id = resource.Get<string>("ID") ?? resource.Get<string>("Id") ?? resource.Get<string>("id");
						var uri = $"{mcpResource.ServiceName}://{mcpResource.Name}/{id}";
						var name = $"{mcpResource.Name}/{id}";
						var title = resource.Get<string>("Title") ?? resource.Get<string>("title");
						var description = resource.Get<string>("Description") ?? resource.Get<string>("description") ?? resource.Get<string>("Summary") ?? resource.Get<string>("summary");
						var lastModified = resource.Get<string>("LastModified") ?? resource.Get<string>("lastModified");
						resources.Add(new JObject
						{
							["uri"] = uri,
							["name"] = name,
							["title"] = title,
							["description"] = description,
							["mimeType"] = "application/json",
							["annotations"] = new JObject
							{
								["audience"] = new JArray("assistant"),
								["priority"] = 0.85,
								["lastModified"] = DateTime.TryParse(lastModified, out var time) ? time.ToIsoString() : null,
								["category"] = mcpResource.Name
							}
						});
					});
				}, true, false).ConfigureAwait(false);
				await context.ShowResultAsync(mcpRequest.Get<string>("id"), new JObject
				{
					["resources"] = resources,
					["nextCursor"] = null
				}, cancellationToken).ConfigureAwait(false);
			}
			catch (Exception ex)
			{
				await context.ShowErrorAsync(mcpRequest.Get<string>("id"), ex, cancellationToken).ConfigureAwait(false);
			}
		}

		static async Task ProcessResourceReadRequestAsync(this HttpContext context, Settings.McpSettings mcpSettings, JObject mcpRequest, CancellationToken cancellationToken)
		{
			var uri = mcpRequest.Get<JObject>("params")?.Get<string>("uri");
			if (string.IsNullOrWhiteSpace(uri))
				throw new InvalidMcpParamsException();

			var uriParts = uri.ToArray("/");
			var objectIdentity = uriParts.Last();
			var name = uriParts[uriParts.Length - 2];

			var mcpResource = mcpSettings.Resources.FirstOrDefault(resource => resource.Name == name);
			if (mcpResource == null)
				throw new InvalidMcpParamsException();

			var requestInfo = new RequestInfo
			(
				context.GetSession(),
				mcpResource.ServiceName,
				mcpResource.Name,
				"resources/read",
				context.Request.QueryString.ToDictionary(query => query["object-identity"] = objectIdentity),
				context.Request.Headers.ToDictionary(header =>
				{
					header["x-system-id"] = mcpSettings.SystemID;
					header["x-requester"] = "vieapps-ngx-portals";
				}),
				null,
				null,
				context.GetCorrelationID()
			);

			try
			{
				var result = await requestInfo.ProcessRequestAsync(cancellationToken).ConfigureAwait(false);
				await context.ShowResultAsync(mcpRequest.Get<string>("id"), new JObject
				{
					["contents"] = new JArray(new JObject
					{
						["uri"] = uri,
						["type"] = "application/json",
						["text"] = result == null ? null : $"```json\n{result.ToString(Formatting.None) ?? "null"}\n```"
					})
				}, cancellationToken).ConfigureAwait(false);
			}
			catch (Exception ex)
			{
				await context.ShowErrorAsync(mcpRequest.Get<string>("id"), ex, cancellationToken).ConfigureAwait(false);
			}
		}

		public static async Task GatheringInfoAsync(this CommunicateMessage message)
		{
			var serviceName = message.Data.Get<string>("ServiceName");
			var systemID = message.Data.Get<string>("SystemID");
			var correlationID = UtilityService.NewUUID;
			try
			{
				var requestInfo = new RequestInfo(Global.GetSession(), serviceName, "", "capabilities", null, new Dictionary<string, string>
				{
					["x-system-id"] = systemID,
					["x-requester"] = "vieapps-ngx-portals"
				}, null, null, correlationID);
				var response = await requestInfo.ProcessRequestAsync(Global.CancellationToken).ConfigureAwait(false);
				response.As<Settings.McpSettings>(true, (mcpSettings, _) => mcpSettings.SystemID = systemID).UpdateInfo(serviceName, systemID, correlationID);
				await Global.WriteLogsAsync("MCP", $"Success gathering info [{serviceName}/{systemID}]", null, Global.ServiceName, LogLevel.Information, correlationID).ConfigureAwait(false);
			}
			catch (Exception ex)
			{
				await Global.WriteLogsAsync("MCP", $"Cannot gathering info [{serviceName}/{systemID}] => {ex.Message}", ex, Global.ServiceName, LogLevel.Error, correlationID).ConfigureAwait(false);
			}
		}

		public static void UpdateInfo(this Settings.McpSettings settings, string serviceName, string systemID, string correlationID = null)
		{
			if (string.IsNullOrWhiteSpace(systemID) || !systemID.IsEquals(settings.SystemID) || settings.Resources == null || settings.Resources.Count < 1)
				return;

			if (!McpHandler.Settings.TryGetValue(systemID, out var mcpSettings))
			{
				mcpSettings = new Settings.McpSettings();
				McpHandler.Settings[systemID] = mcpSettings;
			}

			mcpSettings.SystemID = systemID;
			mcpSettings.Instructions = settings.Instructions;
			mcpSettings.Resources ??= new();
			settings.Resources.ForEach(resource =>
			{
				var mcpResource = mcpSettings.Resources.FirstOrDefault(res => res.Name == resource.Name);
				if (mcpResource == null)
				{
					mcpResource = new();
					mcpSettings.Resources.Add(mcpResource);
				}
				mcpResource.CopyFrom(resource);
				mcpResource.ServiceName = serviceName.ToLower();
			});

			Global.WriteLogs("MCP", $"Success update info [{serviceName}/{systemID}]", null, Global.ServiceName, LogLevel.Information, correlationID ?? UtilityService.NewUUID);
		}

		public static void UpdateSessionInfo(this CommunicateMessage message)
			=> McpHandler.Sessions[message.Data.Get<string>("ID")] = (message.Data.Get<string>("SessionID"), message.Data.Get<string>("IP"), message.Data.Get<long>("LastActivity"));

		public static void SendSessionInfo(this Session session, string sessionID)
		{
			McpHandler.Sessions[sessionID] = (session.SessionID, session.IP, DateTime.Now.ToUnixTimestamp());
			new CommunicateMessage("APIGateway")
			{
				ExcludedNodeID = Global.NodeID,
				Type = "McpServer#SessionInfo",
				Data = new JObject
				{
					["ID"] = sessionID,
					["SessionID"] = session.SessionID,
					["IP"] = session.IP,
					["LastActivity"] = DateTime.Now.ToUnixTimestamp()
				}
			}.Send();
		}

		public static void SyncSessionInfo()
			=> McpHandler.Sessions.ForEach(kvp => new CommunicateMessage("APIGateway")
			{
				ExcludedNodeID = Global.NodeID,
				Type = "McpServer#SessionInfo",
				Data = new JObject
				{
					["ID"] = kvp.Key,
					["SessionID"] = kvp.Value.SessionID,
					["IP"] = kvp.Value.IP,
					["LastActivity"] = kvp.Value.LastActivity
				}
			}.Send());

		static async Task ShowErrorAsync(this HttpContext context, Exception exception, string id = null, string message = null)
		{
			var statusCode = exception is MalformedMcpRequestException || exception is MethodNotAllowedException
				? (int)HttpStatusCode.BadRequest
				: (int)HttpStatusCode.OK;

			var errorCode = -32603;
			if (exception is InvalidMcpRequestException)
				errorCode = -32600;
			else if (exception is InvalidMcpBodyException)
				errorCode = -32700;
			else if (exception is InvalidMcpMethodException)
				errorCode = -32601;
			else if (exception is InvalidMcpParamsException)
				errorCode = -32602;
			else if (exception is ServiceNotFoundException)
				errorCode = -32003;

			message ??= exception is WampException wampException
				? wampException.GetDetails().Message
				: exception.Message;

			var body = new JObject
			{
				["jsonrpc"] = "2.0",
				["id"] = id,
				["error"] = new JObject
				{
					["code"] = errorCode,
					["message"] = message
				}
			};

			var headers = new Dictionary<string, string>
			{
				["X-Node"] = Global.NodeID,
				["X-Correlation-ID"] = context.GetCorrelationID()
			};
			if (context.ContainsKey("MCP-Session-ID"))
				headers["MCP-Session-ID"] = context.GetParameter("MCP-Session-ID");

			try
			{
				await Task.WhenAll
				(
					statusCode != (int)HttpStatusCode.OK ? Task.CompletedTask : context.WriteAsync(body, headers, Global.CancellationToken),
					context.WriteLogsAsync("MCP", message, exception, Global.ServiceName, LogLevel.Error)
				).ConfigureAwait(false);
				if (statusCode != (int)HttpStatusCode.OK)
					context.WriteError(statusCode, body, headers);
			}
			catch { }
		}

		static async Task ShowErrorAsync(this HttpContext context, string id, Exception exception, CancellationToken cancellationToken)
		{
			var code = exception.GetHttpStatusCode();
			var message = exception.Message ?? "Unknown error";
			var type = exception.GetTypeName(true) ?? "UnknownException";
			if (exception is WampException wampException)
			{
				var details = wampException.GetDetails();
				code = details.Code;
				message = details.Message;
				type = details.Type;
			}
			await Task.WhenAll
			(
				context.ShowResultAsync(id, new JObject
				{
					["isError"] = true,
					["content"] = new JArray(new JObject
					{
						["type"] = "text",
						["text"] = $"```json\n{new JObject
						{
							["code"] = code,
							["message"] = message,
							["type"] = type.IndexOf('+') > 0 ? type.Right(type.Length - type.IndexOf('+')) : type,
							["correlationID"] = context.GetCorrelationID()
						}.ToString(Formatting.None)}\n```"
					})
				}, cancellationToken),
				context.WriteLogsAsync("MCP", message, exception, Global.ServiceName, LogLevel.Error)
			).ConfigureAwait(false);
		}

		static Task ShowResultAsync(this HttpContext context, string id, JObject result, CancellationToken cancellationToken)
			=> context.ShowResultAsync(id, result, null, null, null, cancellationToken);

		static Task ShowResultAsync(this HttpContext context, string id, JObject result, JObject additional, string protocolVersion, string sessionID, CancellationToken cancellationToken)
		{
			var response = new JObject
			{
				["jsonrpc"] = "2.0",
				["id"] = id,
				["result"] = result
			};
			additional?.ForEach(kvp => response[kvp.Key] = kvp.Value);
			var headers = new Dictionary<string, string>
			{
				["MCP-Protocol-Version"] = protocolVersion ?? context.GetParameter("MCP-Protocol-Version"),
				["MCP-Session-ID"] = sessionID ?? context.GetParameter("MCP-Session-ID"),
				["X-Node"] = Global.NodeID,
				["X-Correlation-ID"] = context.GetCorrelationID()
			};
			return Task.WhenAll
			(
				context.WriteAsync(response, headers, cancellationToken),
				Global.IsDebugLogEnabled || context.ContainsKey("x-logs") ? context.WriteLogsAsync("MCP", $"Response: {response}") : Task.CompletedTask
			);
		}

		static Task<JToken> ProcessRequestAsync(this RequestInfo requestInfo, CancellationToken cancellationToken)
			=> Router.GetService(requestInfo.ServiceName).ProcessMcpRequestAsync(requestInfo, cancellationToken);
	}
}
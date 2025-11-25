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
				try
				{
					await context.ProcessMcpRequestAsync().ConfigureAwait(false);
				}
				catch (Exception ex)
				{
					await context.ShowErrorAsync(ex).ConfigureAwait(false);
				}
				if (Global.IsVisitLogEnabled)
					await context.WriteVisitFinishingLogAsync().ConfigureAwait(false);
			}
		}

		internal static void SyncSessionInfo()
			=> McpHandlerExtensions.SyncSessionInfo();

		#region Properties
		internal static ConcurrentDictionary<string, Settings.McpSettings> Settings { get; } = new ConcurrentDictionary<string, Settings.McpSettings>(StringComparer.OrdinalIgnoreCase);

		internal static ConcurrentDictionary<string, (string SessionID, string IP, long LastActivity)> Sessions { get; } = new ConcurrentDictionary<string, (string SessionID, string IP, long LastActivity)>(StringComparer.OrdinalIgnoreCase);

		internal static List<string> SupportedProtocols { get; } = new() { "2025-06-18", "2025-03-26" };
		#endregion

		#region Exceptions
		public class MalformedMcpRequestException : AppException
		{
			public MalformedMcpRequestException() : base("Parse error: malformed JSON") { }
			public MalformedMcpRequestException(string message) : base(message) { }
			public MalformedMcpRequestException(Exception innerException) : base("Parse error: malformed JSON", innerException) { }
			public MalformedMcpRequestException(string message, Exception innerException) : base(message, innerException) { }
		}

		public class InvalidMcpProtocolException : AppException
		{
			public InvalidMcpProtocolException()	: base("Unsupported or invalid MCP protocol version") { }
			public InvalidMcpProtocolException(string message) : base(message) { }
			public InvalidMcpProtocolException(string message, Exception innerException) : base(message, innerException) { }
		}

		public class InvalidMcpSessionException : AppException
		{
			public InvalidMcpSessionException() : base("Invalid or expired session") { }
			public InvalidMcpSessionException(string message) : base(message) { }
			public InvalidMcpSessionException(string message, Exception innerException) : base(message, innerException) { }
		}

		public class InvalidMcpRequestException : AppException
		{
			public InvalidMcpRequestException() : base("Invalid JSON-RPC request") { }
			public InvalidMcpRequestException(string message) : base(message) { }
			public InvalidMcpRequestException(string message, Exception innerException) : base(message, innerException) { }
		}

		public class InvalidMcpBodyException : AppException
		{
			public InvalidMcpBodyException() : base("Invalid JSON-RPC request body") { }
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

		public class InvalidMcpCursorException : AppException
		{
			public InvalidMcpCursorException() : base("Invalid cursor parameter") { }
			public InvalidMcpCursorException(string message) : base(message) { }
			public InvalidMcpCursorException(Exception innerException) : base("Invalid cursor", innerException) { }
			public InvalidMcpCursorException(string message, Exception innerException) : base(message, innerException) { }
		}
		#endregion

	}

	internal static class McpHandlerExtensions
	{
		public static async Task ProcessMcpRequestAsync(this HttpContext context)
		{
			// check method
			if (!context.Request.Method.IsEquals("POST"))
				throw new MethodNotAllowedException();

			// identify the system
			var stopwatch = Stopwatch.StartNew();
			using var cts = CancellationTokenSource.CreateLinkedTokenSource(Global.CancellationToken, context.RequestAborted);

			var isDebugLogEnabled = Global.IsDebugLogEnabled || context.ContainsKey("x-logs");
			var session = context.GetSession();
			var headers = context.Request.Headers.ToDictionary(header =>
			{
				header["x-host"] = context.GetRequestUri().Host;
				header["x-brief"] = "1";
			});

			var requestInfo = new RequestInfo(session, "Portals", "Identify.System", "GET", context.Request.QueryString.ToDictionary(), headers, null, null, context.GetCorrelationID());
			var identifyJson = await context.IdentifySystemAsync(requestInfo, cts.Token).ConfigureAwait(false);
			var systemID = identifyJson.Get<string>("ID");
			var alias = identifyJson.Get<string>("Alias");

			// get MCP server settings
			Settings.McpSettings mcpSettings;
			if (!McpHandler.Settings.TryGetValue(systemID, out mcpSettings))
			{
				await session.GatheringInfoAsync(Global.ServiceName, systemID, context.GetCorrelationID()).ConfigureAwait(false);
				McpHandler.Settings.TryGetValue(systemID, out mcpSettings);
			}

			if (mcpSettings == null)
				throw new ServiceNotFoundException("Unavailable");

			if (!mcpSettings.AllowAnonymous && !context.IsAuthenticated())
				throw new UnauthorizedException("Unauthorized: missing or invalid credentials");

			// prepare
			JObject mcpRequest = null;
			try
			{
				var body = await context.ReadTextAsync(cts.Token).ConfigureAwait(false);
				mcpRequest = body.ToJson() as JObject;
				if (mcpRequest == null)
					throw new InvalidMcpBodyException();
			}
			catch (Exception ex)
			{
				throw ex is InvalidMcpBodyException ? ex : new MalformedMcpRequestException(ex);
			}

			var jsonrpc = mcpRequest.Get<string>("jsonrpc");
			if (string.IsNullOrWhiteSpace(jsonrpc) || jsonrpc != "2.0")
				throw new InvalidMcpRequestException("Invalid JSON-RPC version");

			var mcpMethod = mcpRequest.Get<string>("method");
			if (string.IsNullOrWhiteSpace(mcpMethod))
				throw new InvalidMcpMethodException("Invalid JSON-RPC method");

			var mcpRequestID = mcpRequest.Get<string>("id");
			if (string.IsNullOrWhiteSpace(mcpRequestID) && !mcpMethod.IsStartsWith("notifications/"))
				throw new InvalidMcpRequestException("Invalid JSON-RPC identifier");

			string mcpSessionID = null;
			var mcpInitializeStage = mcpMethod.IsEquals("initialize");

			if (mcpInitializeStage)
			{
				if (!headers.TryGetValue("MCP-Protocol-Version", out var mcpProtocolVersion))
					mcpProtocolVersion = mcpRequest.Get<JObject>("params")?.Get<string>("protocolVersion") ?? mcpRequest.Get<string>("protocolVersion");
				if (string.IsNullOrWhiteSpace(mcpProtocolVersion) || McpHandler.SupportedProtocols.FirstOrDefault(version => version == mcpProtocolVersion) == null)
					throw new InvalidMcpProtocolException($"Unsupported or invalid MCP protocol version ({mcpProtocolVersion ?? "null"}) - supported version(s): {McpHandler.SupportedProtocols.Join(", ")}");
			}
			else if (!headers.TryGetValue("MCP-Session-ID", out mcpSessionID) || !McpHandler.Sessions.ContainsKey(mcpSessionID))
				throw new InvalidMcpSessionException($"Invalid or expired session ({(string.IsNullOrWhiteSpace(mcpSessionID) ? "no identity" : "not found")})");

			// process the request
			await context.WriteLogsAsync("MCP", $"Start process request [{systemID}/{alias}]{(isDebugLogEnabled ? $"\r\nRequest JSON-RPC: {mcpRequest}" : "")}").ConfigureAwait(false);
			if (Handler.TrackSessions)
				requestInfo.SendSessionState(Handler.TrackAPISessions);
			else
				requestInfo.TrackStatistics();

			try
			{
				if (mcpInitializeStage)
				{
					if (string.IsNullOrWhiteSpace(mcpSettings.Name))
						mcpSettings.Name = $"{alias}-mcp";
					await context.ProcessInitializeRequestAsync(mcpSettings, mcpRequest, cts.Token).ConfigureAwait(false);
				}

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
					session.SendSessionInfo(mcpSessionID);
			}
			catch (OperationCanceledException) { }
			catch (Exception ex)
			{
				await context.ShowErrorAsync(ex, mcpRequestID).ConfigureAwait(false);
			}

			await context.WriteLogsAsync("MCP", $"End process request - Execution times: {stopwatch.GetElapsedTimes()}").ConfigureAwait(false);
		}

		static Task ProcessInitializeRequestAsync(this HttpContext context, Settings.McpSettings mcpSettings, JObject mcpRequest, CancellationToken cancellationToken)
		{
			var protocolVersion = context.GetParameter("MCP-Protocol-Version") ?? mcpRequest.Get<JObject>("params")?.Get<string>("protocolVersion") ?? mcpRequest.Get<string>("protocolVersion");
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
			if (!string.IsNullOrWhiteSpace(mcpSettings.Instructions))
				result["instructions"] = mcpSettings.Instructions;

			context.GetSession().SendSessionInfo(sessionID);
			return context.ShowResultAsync(mcpRequest.Get<string>("id"), result, sessionID, cancellationToken);
		}

		static Task ProcessToolListRequestAsync(this HttpContext context, Settings.McpSettings mcpSettings, JObject mcpRequest, CancellationToken cancellationToken)
		{
			var tools = new JArray();
			mcpSettings.Resources.ForEach(mcpResource => mcpResource.Tools.ForEach(mcpTool =>
			{
				var tool = new JObject
				{
					["name"] = $"{mcpResource.Name}.{mcpTool.Name}",
					["title"] = mcpTool.Title,
					["description"] = mcpTool.Description
				};
				if (mcpTool.InputSchema != null)
					tool["inputSchema"] = mcpTool.InputSchema;
				if (mcpTool.OutputSchema != null)
					tool["outputSchema"] = mcpTool.OutputSchema;
				tools.Add(tool);
			}));
			var result = new JObject
			{
				["tools"] = tools,
				["nextCursor"] = null
			};
			return context.ShowResultAsync(mcpRequest.Get<string>("id"), result, cancellationToken);
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

			var mcpBody = mcpParams?.Get<JObject>("arguments");
			if (mcpBody == null)
				throw new InvalidMcpBodyException();

			var cursor = mcpBody.Get<string>("cursor");
			if (!string.IsNullOrWhiteSpace(cursor))
				try
				{
					var cursorJson = cursor.ToBase64(false, true).Decrypt(Global.EncryptionKey).ToJson() as JObject;
					cursor = cursorJson.Get<string>("cursor");
					if (string.IsNullOrWhiteSpace(cursor))
						throw new InvalidMcpCursorException();
					var actualSignature = cursorJson.Get<string>("signature");
					if (string.IsNullOrWhiteSpace(actualSignature))
						throw new InvalidMcpCursorException();
					var computeSignature = cursor.GetHMAC(Global.ValidationKey);
					if (computeSignature != actualSignature)
						throw new InvalidMcpCursorException();
					mcpBody["cursor"] = cursor;
				}
				catch (Exception ex)
				{
					throw ex is InvalidMcpCursorException ? ex : new InvalidMcpCursorException(ex);
				}

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
				mcpBody.ToString(Formatting.None),
				null,
				context.GetCorrelationID()
			);
			
			try
			{
				var response = await requestInfo.ProcessRequestAsync(cancellationToken).ConfigureAwait(false);
				cursor = response?.Get<string>("nextCursor");
				if (!string.IsNullOrWhiteSpace(cursor))
					response["nextCursor"] = new JObject
					{
						["cursor"] = cursor,
						["signature"] = cursor.GetHMAC(Global.ValidationKey)
					}.ToString(Formatting.None).Encrypt(Global.EncryptionKey).ToBase64Url(true);
				var result = new JObject
				{
					["isError"] = false,
					["content"] = new JArray(new JObject
					{
						["type"] = "text",
						["text"] = response?.ToString(Formatting.None)
					}),
					["structuredContent"] = response
				};
				await context.ShowResultAsync(mcpRequest.Get<string>("id"), result, cancellationToken).ConfigureAwait(false);
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
			var result = new JObject
			{
				["resourceTemplates"] = resourceTemplates,
				["nextCursor"] = null
			};
			return context.ShowResultAsync(mcpRequest.Get<string>("id"), result, cancellationToken);
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
			
			var mcpParams = mcpRequest.Get<JObject>("params");
			var cursor = mcpParams?.Get<string>("cursor");
			var cursors = new JObject();
			if (!string.IsNullOrWhiteSpace(cursor))
				try
				{
					var jsonCursors = cursor.ToBase64(false, true).Decrypt(Global.EncryptionKey).ToJson() as JObject;
					foreach(var mcpResource in mcpSettings.Resources)
					{
						var mcpCursor = jsonCursors.Get<JObject>(mcpResource.Name);
						if (mcpCursor == null)
							continue;
						var cursorStr = mcpCursor.Get<string>("cursor");
						if (string.IsNullOrWhiteSpace(cursorStr))
							throw new InvalidMcpCursorException();
						var actualSignature = mcpCursor.Get<string>("signature");
						if (string.IsNullOrWhiteSpace(actualSignature))
							throw new InvalidMcpCursorException();
						var computeSignature = cursorStr.GetHMAC(Global.ValidationKey);
						if (computeSignature != actualSignature)
							throw new InvalidMcpCursorException();
						cursors[mcpResource.Name] = cursorStr;
					}
				}
				catch (Exception ex)
				{
					throw ex is InvalidMcpCursorException ? ex : new InvalidMcpCursorException(ex);
				}

			try
			{
				var resources = new JArray();
				var nextCursors = new JObject();

				var mcpResources = mcpSettings.Resources.Where(mcpResource => string.IsNullOrWhiteSpace(cursor) || cursors.Get<string>(mcpResource.Name) != null).ToList();
				await mcpResources.ForEachAsync(async mcpResource =>
				{
					var requestInfo = new RequestInfo
					(
						session,
						mcpResource.ServiceName,
						mcpResource.Name,
						"resources/list",
						query,
						headers,
						string.IsNullOrWhiteSpace(cursor) ? null : new JObject { ["cursor"] = cursors.Get<string>(mcpResource.Name) }.ToString(Formatting.None),
						null,
						correlationID
					);
					var response = await requestInfo.ProcessRequestAsync(cancellationToken).ConfigureAwait(false);
					(response as JArray ?? response?.Get<JArray>("items"))?.Select(resource => resource as JObject).ToList().ForEach(resource =>
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
								["priority"] = string.IsNullOrWhiteSpace(cursor) ? 0.85 : 0.75,
								["lastModified"] = DateTime.TryParse(lastModified, out var time) ? time.ToIsoString() : null,
								["category"] = mcpResource.Name
							}
						});
					});
					var nextCursor = response?.Get<string>("nextCursor");
					if (!string.IsNullOrWhiteSpace(nextCursor))
						nextCursors[mcpResource.Name] = new JObject
						{
							["cursor"] = nextCursor,
							["signature"] = nextCursor.GetHMAC(Global.ValidationKey)
						};
				}, true, false).ConfigureAwait(false);

				var result = new JObject
				{
					["resources"] = resources,
					["nextCursor"] = nextCursors.Count < 1 ? null : nextCursors.ToString(Formatting.None).Encrypt(Global.EncryptionKey).ToBase64Url(true)
				};
				await context.ShowResultAsync(mcpRequest.Get<string>("id"), result, cancellationToken).ConfigureAwait(false);
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
				var response = await requestInfo.ProcessRequestAsync(cancellationToken).ConfigureAwait(false);
				var result = new JObject
				{
					["contents"] = new JArray(new JObject
					{
						["uri"] = uri,
						["mimeType"] = "application/json",
						["text"] = response?.ToString(Formatting.None)
					})
				};
				await context.ShowResultAsync(mcpRequest.Get<string>("id"), result, cancellationToken).ConfigureAwait(false);
			}
			catch (Exception ex)
			{
				await context.ShowErrorAsync(mcpRequest.Get<string>("id"), ex, cancellationToken).ConfigureAwait(false);
			}
		}

		public static Task GatheringInfoAsync(this CommunicateMessage message)
		{
			var serviceName = message.Data.Get<string>("ServiceName");
			var systemID = message.Data.Get<string>("SystemID");
			return GatheringInfoAsync(Global.GetSession(), serviceName, systemID);
		}

		public static async Task GatheringInfoAsync(this Session session, string serviceName, string systemID, string correlationID = null)
		{
			session ??= Global.GetSession();
			correlationID ??= Global.GetCorrelationID() ?? UtilityService.NewUUID;
			try
			{
				var requestInfo = new RequestInfo(session, serviceName, "", "capabilities", null, new Dictionary<string, string>
				{
					["x-system-id"] = systemID,
					["x-requester"] = "vieapps-ngx-portals"
				}, null, null, correlationID);
				var response = await requestInfo.ProcessRequestAsync(Global.CancellationToken).ConfigureAwait(false);
				response.As<Settings.McpSettings>(true, (mcpSettings, _) => mcpSettings.SystemID = systemID).UpdateInfo(serviceName, systemID, correlationID);
				await Global.WriteLogsAsync("MCP", $"Success gathering info [{serviceName.ToLower()}/{systemID}]", null, Global.ServiceName, LogLevel.Information, correlationID).ConfigureAwait(false);
			}
			catch (Exception ex)
			{
				await Global.WriteLogsAsync("MCP", $"Cannot gathering info [{serviceName.ToLower()}/{systemID}] => {ex.Message}", ex, Global.ServiceName, LogLevel.Error, correlationID).ConfigureAwait(false);
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
			mcpSettings.AllowAnonymous = settings.AllowAnonymous;
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

		public static (int Code, string Message, string Type, string Stack) GetErrorDetails(this Exception exception)
		{
			var code = exception.GetHttpStatusCode();
			var message = exception.Message ?? "Unknown error";
			var type = exception.GetTypeName(true) ?? "UnknownException";
			var stack = exception.StackTrace;
			if (exception is WampException wampException)
			{
				var details = wampException.GetDetails();
				code = details.Code;
				message = details.Message;
				type = details.Type;
				stack = details.Stack;
			}
			if (type == "AccessDeniedException")
				message = "Access denied: insufficient permissions";
			return (code, message, type.IndexOf('+') > 0 ? type.Right(type.Length - type.IndexOf('+') - 1) : type, stack);
		}

		public static async Task ShowErrorAsync(this HttpContext context, Exception exception, string id = null)
		{
			var code = -32603;
			if (exception is InvalidMcpProtocolException || exception is MalformedMcpRequestException || exception is InvalidMcpRequestException)
				code = -32600;
			else if (exception is InvalidMcpMethodException || exception is NotImplementedException)
				code = -32601;
			else if (exception is InvalidMcpBodyException || exception is InvalidMcpParamsException)
				code = -32602;
			else if (exception is UnauthorizedException)
				code = -32001;
			else if (exception is InvalidMcpSessionException)
				code = -32002;
			else if (exception is AccessDeniedException)
				code = -32003;
			else if (exception is ServiceNotFoundException)
				code = -32004;
			else if (exception is MethodNotAllowedException)
				code = -32005;
			else if (exception is InvalidMcpCursorException)
				code = -32010;

			var (httpStatus, message, type, stack) = exception.GetErrorDetails();

			var error = new JObject
			{
				["code"] = code,
				["message"] = message,
				["data"] = new JObject
				{
					["httpStatus"] = exception is MethodNotAllowedException || exception is MalformedMcpRequestException || exception is InvalidMcpCursorException ? (int)HttpStatusCode.BadRequest : httpStatus,
					["type"] = type,
					["stack"] = stack,
					["correlationID"] = context.GetCorrelationID()
				}
			};

			using var cts = CancellationTokenSource.CreateLinkedTokenSource(Global.CancellationToken, context.RequestAborted);
			await Task.WhenAll
			(
				context.ShowJsonRpcAsync(id, "error", error, null, cts.Token),
				context.WriteLogsAsync("MCP", message, exception, Global.ServiceName, LogLevel.Error)
			).ConfigureAwait(false);
		}

		public static Task ShowErrorAsync(this HttpContext context, string id, Exception exception, CancellationToken cancellationToken)
		{
			var (code, message, type, stack) = exception.GetErrorDetails();
			var result = new JObject
			{
				["isError"] = true,
				["content"] = new JArray(new JObject
				{
					["type"] = "text",
					["text"] = message
				}),
				["structuredContent"] = new JObject
				{
					["code"] = code,
					["message"] = message,
					["type"] = type,
					["stack"] = stack,
					["correlationID"] = context.GetCorrelationID()
				}
			};
			return Task.WhenAll
			(
				context.ShowResultAsync(id, result, cancellationToken),
				context.WriteLogsAsync("MCP", message, exception, Global.ServiceName, LogLevel.Error)
			);
		}

		public static Task ShowResultAsync(this HttpContext context, string id, JObject result, CancellationToken cancellationToken)
			=> context.ShowResultAsync(id, result, null, cancellationToken);

		public static Task ShowResultAsync(this HttpContext context, string id, JObject result, string sessionID, CancellationToken cancellationToken)
			=> context.ShowJsonRpcAsync(id, "result", result, sessionID, cancellationToken);

		public static Task ShowJsonRpcAsync(this HttpContext context, string id, string name, JObject json, string sessionID, CancellationToken cancellationToken)
		{
			var response = new JObject
			{
				["jsonrpc"] = "2.0",
				["id"] = Int64.TryParse(id, out var idAsNumber) ? idAsNumber : id,
				[name] = json
			};
			var headers = new Dictionary<string, string>
			{
				["MCP-Session-ID"] = sessionID ?? context.GetParameter("MCP-Session-ID"),
				["X-Node"] = Global.NodeID,
				["X-Correlation-ID"] = context.GetCorrelationID()
			};
			return Task.WhenAll
			(
				context.WriteAsync(response, headers, cancellationToken),
				Global.IsDebugLogEnabled || context.ContainsKey("x-logs") ? context.WriteLogsAsync("MCP", $"Response JSON-RPC: {response}") : Task.CompletedTask
			);
		}

		static Task<JToken> ProcessRequestAsync(this RequestInfo requestInfo, CancellationToken cancellationToken)
			=> Router.GetService(requestInfo.ServiceName).ProcessMcpRequestAsync(requestInfo, cancellationToken);
	}
}
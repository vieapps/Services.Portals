#region Related components
using System;
using System.Net;
using System.Linq;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using WampSharp.V2.Core.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using net.vieapps.Components.Utility;
using static net.vieapps.Services.Portals.McpHandler;
#endregion

namespace net.vieapps.Services.Portals
{
	public class McpHandler
	{
		public McpHandler(RequestDelegate _) { }

		public async Task Invoke(HttpContext context)
		{
			await this.ProcessRequestAsync(context).ConfigureAwait(false);
			if (!context.Request.Method.IsEquals("OPTIONS") && !context.WebSockets.IsWebSocketRequest && Global.IsVisitLogEnabled)
				await context.WriteVisitFinishingLogAsync().ConfigureAwait(false);
		}

		Task ProcessRequestAsync(HttpContext context)
			=> context.Request.Method.IsEquals("OPTIONS")
				? Task.CompletedTask
				: context.ProcessMcpRequestAsync();

		internal static ConcurrentDictionary<string, Settings.McpSettings> Settings { get; } = new();

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
			if (context.Request.Method.IsEquals("GET"))
			{
				await context.ShowErrorAsync(new MethodNotAllowedException()).ConfigureAwait(false);
				return;
			}

			// prepare
			using var cts = CancellationTokenSource.CreateLinkedTokenSource(Global.CancellationToken, context.RequestAborted);

			var isDebugLogEnabled = Global.IsDebugLogEnabled || context.ContainsKey("x-logs");
			var headers = context.Request.Headers.ToDictionary(header =>
			{
				header["x-host"] = context.GetRequestUri().Host;
				header["x-brief"] = "1";
			});

			if (!headers.TryGetValue("MCP-Protocol-Version", out var mcpProtocolVersion))
			{
				await context.ShowErrorAsync(new InvalidMcpProtocolException()).ConfigureAwait(false);
				return;
			}

			// identify the system
			Settings.McpSettings mcpSettings;
			try
			{
				var requestInfo = new RequestInfo(context.GetSession(), "Portals", "Identify.System", "GET", context.Request.QueryString.ToDictionary(), headers, null, null, context.GetCorrelationID());
				var identifyJson = await context.IdentifySystemAsync(requestInfo, cts.Token).ConfigureAwait(false);
				if (!McpHandler.Settings.TryGetValue(identifyJson.Get<string>("ID"), out mcpSettings))
					throw new ServiceNotFoundException("Service is unavailable");

				if (string.IsNullOrWhiteSpace(mcpSettings.Name))
					mcpSettings.Name = $"{identifyJson.Get<string>("Alias")}-mcp";

				if (Handler.TrackSessions)
					requestInfo.SendSessionState(Handler.TrackAPISessions);
				else
					requestInfo.TrackStatistics();
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

			headers.TryGetValue("Mcp-Session-Id", out var mcpSessionID);
			if (!mcpInitializeStage && string.IsNullOrWhiteSpace(mcpSessionID))
			{
				await context.ShowErrorAsync(new InvalidMcpSessionException(), mcpRequestID).ConfigureAwait(false);
				return;
			}

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

				else if (!mcpMethod.IsStartsWith("notifications/"))
					throw new NotImplementedException();
			}
			catch (OperationCanceledException) { }
			catch (Exception ex)
			{
				await context.ShowErrorAsync(ex, mcpRequestID).ConfigureAwait(false);
			}
		}

		static Task ProcessInitializeRequestAsync(this HttpContext context, Settings.McpSettings mcpSettings, JObject mcpRequest, CancellationToken cancellationToken)
		{
			var sessionID = UtilityService.NewUUID;
			var result = new JObject
			{
				["protocolVersion"] = context.Request.Headers["MCP-Protocol-Version"].ToString(),
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
			return context.ShowResultAsync(mcpRequest.Get<string>("id"), result, additional, sessionID, cancellationToken);
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
			var name = mcpRequest.Get<JObject>("params")?.Get<string>("name");
			if (string.IsNullOrWhiteSpace(name))
				throw new InvalidMcpParamsException();

			var resource = mcpSettings.Resources.FirstOrDefault(mcpResource => mcpResource.Name == name.ToArray(".").First());
			if (resource == null)
				throw new InvalidMcpMethodException();

			name = name.ToArray(".").Last();
			var tool = resource.Tools.FirstOrDefault(mcpTool => mcpTool.Name == name);
			if (tool == null)
				throw new InvalidMcpMethodException();

			var requestInfo = new RequestInfo
			(
				context.GetSession(),
				resource.ServiceName,
				resource.Name,
				tool.Name,
				context.Request.QueryString.ToDictionary(),
				context.Request.Headers.ToDictionary(header => header["x-system-id"] = mcpSettings.SystemID),
				mcpRequest.Get<JObject>("params")?.Get<JObject>("arguments")?.ToString(Formatting.None),
				null,
				Global.GetCorrelationID()
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
						["text"] = $"```json\n{result.ToString(Formatting.None)}\n```"
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
				["uriTemplate"] = $"{mcpResource.ServiceName.ToLower()}://{mcpResource.Name}" + "{id}",
				["name"] = mcpResource.Name,
				["title"] = mcpResource.Title,
				["description"] = mcpResource.Description,
				["mimeType"] = "application/json"
			}));
			return context.ShowResultAsync(mcpRequest.Get<string>("id"), new JObject
			{
				["resourceTemplates"] = resourceTemplates,
				["nextCursor"] = null
			}, cancellationToken);
		}

		static async Task ProcessResourceListRequestAsync(this HttpContext context, Settings.McpSettings mcpSettings, JObject mcpRequest, CancellationToken cancellationToken)
		{
			var query = context.Request.QueryString.ToDictionary();
			var headers = context.Request.Headers.ToDictionary(header => header["x-system-id"] = mcpSettings.SystemID);
			var correlationID = Global.GetCorrelationID();
			try
			{
				var resources = new JArray();
				await mcpSettings.Resources.ForEachAsync(async mcpResource =>
				{
					var requestInfo = new RequestInfo
					(
						context.GetSession(),
						mcpResource.ServiceName,
						mcpResource.Name,
						"resources/list",
						query,
						headers,
						null,
						null,
						correlationID
					);
					var result = await requestInfo.ProcessRequestAsync(cancellationToken).ConfigureAwait(false) as JArray;
					result.Select(resource => resource as JObject).ToList().ForEach(resource =>
					{
						var id = resource.Get<string>("ID") ?? resource.Get<string>("Id") ?? resource.Get<string>("id");
						var uri = $"{mcpResource.ServiceName.ToLower()}://{mcpResource.Name}/{id}";
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
								["priority"] = 0.8,
								["lastModified"] = lastModified
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

			var resource = mcpSettings.Resources.FirstOrDefault(mcpResource => mcpResource.Name == name);
			if (resource == null)
				throw new InvalidMcpParamsException();

			var requestInfo = new RequestInfo
			(
				context.GetSession(),
				resource.ServiceName,
				resource.Name,
				"resources/read",
				context.Request.QueryString.ToDictionary(query => query["object-identity"] = objectIdentity),
				context.Request.Headers.ToDictionary(header => header["x-system-id"] = mcpSettings.SystemID),
				null,
				null,
				Global.GetCorrelationID()
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
						["text"] = $"```json\n{result.ToString(Formatting.None)}\n```"
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
				var session = Global.GetSession();
				var query = new Dictionary<string, string>
				{
					["object-identity"] = systemID
				};
				var header = new Dictionary<string, string>
				{
					["x-requester"] = "vieapps-ngx-portals"
				};
				var requestInfo = new RequestInfo(session, serviceName, "", "capabilities", query, header, null, null, correlationID);
				var settings = (await requestInfo.ProcessRequestAsync(Global.CancellationToken).ConfigureAwait(false)).As<Settings.McpSettings>();
				settings.UpdateInfo(serviceName, systemID);
			}
			catch (Exception ex)
			{
				await Global.WriteLogsAsync("MCP", $"Cannot gathering info [{serviceName}/{systemID}] => {ex.Message}", ex, Global.ServiceName, LogLevel.Error, correlationID).ConfigureAwait(false);
			}
		}

		public static void UpdateInfo(this Settings.McpSettings settings, string serviceName, string systemID, string correlationID = null)
		{
			correlationID ??= UtilityService.NewUUID;
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
				mcpResource.ServiceName = serviceName;
			});

			Global.WriteLogs("MCP", $"Success gathering info [{serviceName}/{systemID}]", null, Global.ServiceName, LogLevel.Information, correlationID);
		}

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

			message ??= exception.Message;
			if (exception is WampException wampException)
				message = wampException.GetDetails().Message;

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
			if (context.Request.Headers.ContainsKey("Mcp-Session-Id"))
				headers["Mcp-Session-Id"] = context.Request.Headers["Mcp-Session-Id"].ToString();

			try
			{
				using var cts = CancellationTokenSource.CreateLinkedTokenSource(Global.CancellationToken, context.RequestAborted);
				await Task.WhenAll
				(
					statusCode != (int)HttpStatusCode.OK ? Task.CompletedTask : context.WriteAsync(body, headers, cts.Token),
					context.WriteLogsAsync("MCP", message ?? exception.Message, exception, Global.ServiceName, LogLevel.Error)
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
							["type"] = type,
							["correlationID"] = context.GetCorrelationID()
						}.ToString(Formatting.None)}\n```"
					})
				}, cancellationToken),
				context.WriteLogsAsync("MCP", exception.Message, exception, Global.ServiceName, LogLevel.Error)
			).ConfigureAwait(false);
		}

		static Task ShowResultAsync(this HttpContext context, string id, JObject result, CancellationToken cancellationToken)
			=> context.ShowResultAsync(id, result, null, null, cancellationToken);

		static Task ShowResultAsync(this HttpContext context, string id, JObject result, JObject additional, string sessionID, CancellationToken cancellationToken)
		{
			var response = new JObject
			{
				["jsonrpc"] = "2.0",
				["id"] = id,
				["result"] = result
			};
			additional?.ForEach(kvp => response[kvp.Key] = kvp.Value);
			return context.WriteAsync(response,	new Dictionary<string, string> { ["Mcp-Session-Id"] = sessionID ?? context.Request.Headers["Mcp-Session-Id"].ToString() }, cancellationToken);
		}

		static Task<JToken> ProcessRequestAsync(this RequestInfo requestInfo, CancellationToken cancellationToken)
			=> Router.GetService(requestInfo.ServiceName).ProcessMcpRequestAsync(requestInfo, cancellationToken);
	}
}
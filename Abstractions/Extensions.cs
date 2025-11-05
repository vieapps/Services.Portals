#region Related components
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Concurrent;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json.Linq;
using net.vieapps.Components.Utility;
#endregion

namespace net.vieapps.Services.Portals
{
	public static class ServiceExtensions
	{
		static ConcurrentDictionary<string, ICmsPortalsService> Services { get; } = new ConcurrentDictionary<string, ICmsPortalsService>(StringComparer.OrdinalIgnoreCase);

		/// <summary>
		/// Gets a service of CMS Portals that specified by a name
		/// </summary>
		/// <param name="name">The string that presents the name of a service in CMS Portals</param>
		/// <returns></returns>
		public static ICmsPortalsService GetService(string name)
		{
			if (string.IsNullOrWhiteSpace(name))
				throw new ServiceNotFoundException("The service name is null or empty");

			if (!Services.TryGetValue(name, out var service))
			{
				service = Router.OutgoingChannel.RealmProxy.Services.GetCalleeProxy<ICmsPortalsService>(ProxyInterceptor.Create(name));
				Services.TryAdd(name, service);
			}

			return service ?? throw new ServiceNotFoundException($"The service \"{name.ToLower()}\" is not found");
		}

		/// <summary>
		/// Gets a service of CMS Portals that specified by a name
		/// </summary>
		/// <param name="contentTypeDefinition"></param>
		/// <returns></returns>
		public static ICmsPortalsService GetService(this ContentTypeDefinition contentTypeDefinition)
			=> GetService(contentTypeDefinition.ModuleDefinition.ServiceName);

		/// <summary>
		/// Gets a service of CMS Portals that specified by a name
		/// </summary>
		/// <param name="contentType"></param>
		/// <returns></returns>
		public static ICmsPortalsService GetService(this IPortalContentType contentType)
			=> contentType.ContentTypeDefinition.GetService();
	}

	public static class RequestExtensions
	{
		static string GetIP(this HttpContext context)
			=> new Dictionary<string, string>(context.Request.Headers.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToString()), StringComparer.OrdinalIgnoreCase).TryGetValue("X-Forwarded-For", out var ip) && !string.IsNullOrWhiteSpace(ip)
				? ip
				: context.Connection.RemoteIpAddress?.ToString();

		/// <summary>
		/// Gets the collection of IP addresses that labeled as black (need to be blocked)
		/// </summary>
		public static ConcurrentDictionary<string, DateTime> BlackIPs { get; } = new(UtilityService.GetAppSetting("Portals:BlackIPs", "").ToList(";", true).Select(ip => new KeyValuePair<string, DateTime>(ip, DateTime.Now)));

		/// <summary>
		/// Gets the state that determines the IP address is black or not
		/// </summary>
		/// <param name="context"></param>
		/// <param name="ip"></param>
		/// <returns></returns>
		public static bool IsBlackIP(this HttpContext context, string ip = null)
			=> BlackIPs.ContainsKey(ip ?? context.GetIP());

		/// <summary>
		/// Gets the state that determines the IP address is black or not
		/// </summary>
		/// <param name="context"></param>
		/// <param name="ip"></param>
		/// <returns></returns>
		public static bool IsBlackIP(this HttpContext context, System.Net.IPAddress ip)
			=> ip != null && context.IsBlackIP(ip.ToString());

		/// <summary>
		/// Gets the state that determines the IP address is black or not
		/// </summary>
		/// <param name="requestInfo"></param>
		/// <returns></returns>
		public static bool IsBlackIP(this RequestInfo requestInfo)
			=> BlackIPs.ContainsKey(requestInfo.Session.IP);

		/// <summary>
		/// Updates the collection of black IPs
		/// </summary>
		/// <param name="message"></param>
		/// <returns></returns>
		public static ConcurrentDictionary<string, DateTime> UpdateBlackIPs(this CommunicateMessage message, bool beRemoved)
		{
			if (message?.Data is JArray msg)
				msg.ToList<string>().Select(ips => ips.ToList(";", true)).SelectMany(ips => ips).Where(ip => !string.IsNullOrWhiteSpace(ip)).ForEach(ip =>
				{
					if (beRemoved)
						BlackIPs.Remove(ip);
					else
						BlackIPs[ip] = DateTime.Now;
				});
			return BlackIPs;
		}

		/// <summary>
		/// Sends message to sync the collection of black IPs
		/// </summary>
		/// <param name="message"></param>
		/// <returns></returns>
		public static ConcurrentDictionary<string, DateTime> SyncBlackIPs(this CommunicateMessage message, string serviceName, string excludedNodeID)
		{
			if (message?.Data is JArray msg)
				msg.ToList<string>().Select(ips => ips.ToList(";", true)).SelectMany(ips => ips).Where(ip => !string.IsNullOrWhiteSpace(ip)).ForEach(ip => BlackIPs[ip] = DateTime.Now);
			new CommunicateMessage(serviceName)
			{
				Type = "BlackIPs#Update",
				ExcludedNodeID = excludedNodeID,
				Data = BlackIPs.Select(kvp => kvp.Key).ToJArray()
			}.Send();
			return BlackIPs;
		}

		/// <summary>
		/// Resets the collection of black IPs
		/// </summary>
		/// <param name="message"></param>
		/// <returns></returns>
		public static ConcurrentDictionary<string, DateTime> ResetBlackIPs(this CommunicateMessage message, string serviceName = null, string excludedNodeID = null)
		{
			BlackIPs.Clear();
			if (message?.Data is JArray msg)
				msg.ToList<string>().Select(ips => ips.ToList(";", true)).SelectMany(ips => ips).Where(ip => !string.IsNullOrWhiteSpace(ip)).ForEach(ip => BlackIPs[ip] = DateTime.Now);
			if (serviceName != null && excludedNodeID != null)
				new CommunicateMessage(serviceName)
				{
					Type = "BlackIPs#Reset",
					ExcludedNodeID = excludedNodeID
				}.Send();
			return BlackIPs;
		}

		/// <summary>
		/// Gets the collection of IP addresses that labeled as harmful (need to be monitored)
		/// </summary>
		public static ConcurrentDictionary<string, (int Counter, DateTime LastAccess)> HarmfulIPs { get; } = [];

		/// <summary>
		/// Gets the state that determines to block harmful requests automatically
		/// </summary>
		public static bool AutoBlockHarmfulRequest { get; set; } = "true".IsEquals(UtilityService.GetAppSetting("Portals:HarmfulRequests:AutoBlock", "true"));

		/// <summary>
		/// Gets the limits that determines to block harmful requests automatically
		/// </summary>
		public static int AutoBlockHarmfulRequestLimits { get; set; } = Int32.TryParse(UtilityService.GetAppSetting("Portals:HarmfulRequests:AutoBlock:Limits", "100"), out var limits) ? limits : 100;

		/// <summary>
		/// Gets the collection of IP addresses that excluded from harmful requests
		/// </summary>
		public static List<string> ExcludedHarmfulRequestIPs { get; } = UtilityService.GetAppSetting("Portals:HarmfulRequests:ExcludedIPs", "").ToList(";", true);

		static Exception MonitorHarmfulRequest(string ip, string nodeID, string method, string serviceName, Exception exception = null)
		{
			if (ExcludedHarmfulRequestIPs.Any(excluedIP => ip.IsStartsWith(excluedIP)))
				return exception ?? new MethodNotAllowedException(method);

			var counter = 1;
			if (HarmfulIPs.TryGetValue(ip, out var info))
				counter = info.Counter + 1;

			if (AutoBlockHarmfulRequest && counter > AutoBlockHarmfulRequestLimits && BlackIPs.TryAdd(ip, DateTime.Now))
			{
				new CommunicateMessage(serviceName)
				{
					Type = "BlackIPs#Update",
					ExcludedNodeID = nodeID,
					Data = new JArray(ip)
				}.Send();
				new CommunicateMessage(serviceName)
				{
					Type = "HarmfulIPs#Remove",
					ExcludedNodeID = nodeID,
					Data = new JArray(ip)
				}.Send();
				HarmfulIPs.Remove(ip);
			}
			else
			{
				HarmfulIPs[ip] = (counter, DateTime.Now);
				new CommunicateMessage(serviceName)
				{
					Type = "HarmfulIPs#Update",
					ExcludedNodeID = nodeID,
					Data = new JArray(new JObject
					{
						["IP"] = ip,
						["Counter"] = counter,
						["LastAccess"] = DateTime.Now
					})
				}.Send();
			}

			return exception ?? new MethodNotAllowedException(method);
		}

		/// <summary>
		/// Monitors a harmful request
		/// </summary>
		/// <param name="context"></param>
		/// <param name="ip"></param>
		/// <param name="nodeID"></param>
		/// <param name="serviceName"></param>
		/// <param name="exception"></param>
		/// <returns></returns>
		public static Exception MonitorHarmfulRequest(this HttpContext context, string ip, string nodeID, string serviceName, Exception exception = null)
			=> MonitorHarmfulRequest(ip ?? context.GetIP(), nodeID, context.Request.Method, serviceName, exception);

		/// <summary>
		/// Monitors a harmful request
		/// </summary>
		/// <param name="requestInfo"></param>
		/// <param name="nodeID"></param>
		/// <param name="serviceName"></param>
		/// <param name="exception"></param>
		/// <returns></returns>
		public static Exception MonitorHarmfulRequest(this RequestInfo requestInfo, string nodeID, string serviceName, Exception exception = null)
			=> MonitorHarmfulRequest(requestInfo.Session.IP, nodeID, requestInfo.Verb, serviceName ?? requestInfo.ServiceName, exception);

		/// <summary>
		/// Updates the collection of harmful IPs
		/// </summary>
		/// <param name="message"></param>
		/// <returns></returns>
		public static ConcurrentDictionary<string, (int Counter, DateTime LastAccess)> UpdateHarmfulIPs(this CommunicateMessage message, bool beRemoved = false)
		{
			if (message?.Data is JArray msg)
			{
				if (beRemoved)
					msg.ToList<string>().Select(ips => ips.ToList(";", true)).SelectMany(ips => ips).Where(ip => !string.IsNullOrWhiteSpace(ip)).ForEach(ip => HarmfulIPs.Remove(ip));
				else
					msg.Select(data => data as JObject).ForEach(data =>
					{
						var ip = data.Get<string>("IP");
						var counter = data.Get("Counter", 1);
						var lastAccess = data.Get("LastAccess", DateTime.Now);
						HarmfulIPs[ip] = HarmfulIPs.TryGetValue(ip, out var info) ? (counter > info.Counter ? counter : info.Counter, lastAccess) : (counter, lastAccess);
					});
			}
			return HarmfulIPs;
		}

		/// <summary>
		/// Sends message to sync the collection of harmful IPs
		/// </summary>
		/// <param name="message"></param>
		/// <param name="serviceName"></param>
		/// <param name="excludedNodeID"></param>
		/// <returns></returns>
		public static ConcurrentDictionary<string, (int Counter, DateTime LastAccess)> SyncHarmfulIPs(this CommunicateMessage message, string serviceName, string excludedNodeID)
		{
			message.UpdateHarmfulIPs();
			new CommunicateMessage(serviceName)
			{
				Type = "HarmfulIPs#Update",
				ExcludedNodeID = excludedNodeID,
				Data = HarmfulIPs.Select(kvp => new JObject
				{
					["IP"] = kvp.Key,
					["Counter"] = kvp.Value.Counter,
					["LastAccess"] = kvp.Value.LastAccess
				}).ToJArray()
			}.Send();
			return HarmfulIPs;
		}

		/// <summary>
		/// Refreshs the collection of black/harmful IPs
		/// </summary>
		/// <param name="message"></param>
		/// <returns></returns>
		public static (ConcurrentDictionary<string, DateTime> BlackIPs, ConcurrentDictionary<string, (int Counter, DateTime LastAccess)> HarmfulIPs) RefreshIPs(this CommunicateMessage message)
		{
			var lastAccess = DateTime.Now.AddMinutes(-45);

			var ipAddresses = BlackIPs.Where(kvp => kvp.Value < lastAccess).Select(kvp => kvp.Key).ToList();
			if (ipAddresses.Count > 0)
			{
				ipAddresses.ForEach(ip => BlackIPs.Remove(ip));
				new CommunicateMessage(message.ServiceName)
				{
					Type = "BlackIPs#Remove",
					ExcludedNodeID = message.ExcludedNodeID,
					Data = ipAddresses.ToJArray()
				}.Send();
			}

			ipAddresses = [.. HarmfulIPs.Where(kvp => kvp.Value.LastAccess < lastAccess).Select(kvp => kvp.Key)];
			if (ipAddresses.Count > 0)
			{
				ipAddresses.ForEach(ip => HarmfulIPs.Remove(ip));
				new CommunicateMessage(message.ServiceName)
				{
					Type = "HarmfulIPs#Remove",
					ExcludedNodeID = message.ExcludedNodeID,
					Data = ipAddresses.ToJArray()
				}.Send();
			}

			Task.Delay(UtilityService.GetRandomNumber(1234, 2345)).Execute(true);
			message.SyncBlackIPs(message.ServiceName, message.ExcludedNodeID);
			message.SyncHarmfulIPs(message.ServiceName, message.ExcludedNodeID);

			return (BlackIPs, HarmfulIPs);
		}

		/// <summary>
		/// Fetch the IPs
		/// </summary>
		/// <param name="requestInfo"></param>
		/// <returns></returns>
		public static JObject FetchIPs(this RequestInfo requestInfo)
			=> "FETCH".IsEquals(requestInfo.Verb)
			? new JObject
			{
				["BlackIPs"] = BlackIPs.OrderBy(kvp => kvp.Key).Select(kvp => kvp.Key).ToJArray(),
				["HarmfulIPs"] = HarmfulIPs.OrderBy(kvp => kvp.Key).Select(kvp => $"{kvp.Key}({kvp.Value.Counter})").ToJArray()
			}
			: null;
	}
}
#region Related components
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Diagnostics;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using net.vieapps.Components.Caching;
using net.vieapps.Components.Utility;
#endregion

namespace net.vieapps.Services.Portals
{
	public class Startup(IConfiguration configuration)
	{
		public static void Main(string[] args)
			=> WebApplication.CreateBuilder(args).Run
			(
				args,
				configuration => new Startup(configuration),
				(startup, services) => startup.ConfigureServices(services),
				(startup, app) => startup.Configure(app, app.Lifetime, app.Environment)
			);

		public IConfiguration Configuration { get; } = configuration;

		public LogLevel LogLevel => this.Configuration.GetAppSetting("Logging/LogLevel/Default", UtilityService.GetAppSetting("Logs:Level", "Information")).TryToEnum(out LogLevel logLevel) ? logLevel : LogLevel.Information;

		public void ConfigureServices(IServiceCollection services)
		{
			// mandatory services
			services
				.AddHttpContextAccessor()
				.AddResponseCompression(options => Global.PrepareResponseCompression(options))
				.AddLogging(builder => builder.SetMinimumLevel(this.LogLevel))
				.AddCache(options => this.Configuration.GetSection("Cache").Bind(options))
				.AddSession(options => Global.PrepareSessionOptions(options, 30))
				.Configure<FormOptions>(options => Global.PrepareFormOptions(options))
				.Configure<CookiePolicyOptions>(options => Global.PrepareCookiePolicyOptions(options));

			// authentication
			services
				.AddAuthentication(options => Global.PrepareAuthenticationOptions(options, _ => options.RequireAuthenticatedSignIn = false))
				.AddCookie(options => Global.PrepareCookieAuthenticationOptions(options, 45));

			// config authentication with proxy/load balancer
			if (Global.UseIISIntegration)
				services.Configure<IISOptions>(options => options.ForwardClientCertificate = false);

			else
			{
				var certificateHeader = "true".IsEquals(UtilityService.GetAppSetting("Proxy:UseAzure"))
					? "X-ARR-ClientCert"
					: UtilityService.GetAppSetting("Proxy:X-Forwarded-Certificate");
				if (!string.IsNullOrWhiteSpace(certificateHeader))
					services.AddCertificateForwarding(options => options.CertificateHeader = certificateHeader);
			}

			// data protection (encrypt/decrypt authenticate ticket cookies & sync across load balancers)
			services.AddDataProtection().PrepareDataProtection("VIEApps-NGX-Portals");

			// config options of IIS server (for working with InProcess hosting model)
			if (Global.UseIISInProcess)
				services.Configure<IISServerOptions>(options => Global.PrepareIISServerOptions(options, _ => options.MaxRequestBodySize = 1024 * 1024 * Global.MaxRequestBodySize));
		}

		public void Configure(IApplicationBuilder appBuilder, IHostApplicationLifetime appLifetime, IWebHostEnvironment environment)
		{
			// settings
			var stopwatch = Stopwatch.StartNew();
			Console.OutputEncoding = Encoding.UTF8;
			Global.ServiceName = "Portals";

			var loggerFactory = appBuilder.ApplicationServices.GetService<ILoggerFactory>();
			var logPath = UtilityService.GetAppSetting("Path:Logs");
			if (!string.IsNullOrWhiteSpace(logPath) && Directory.Exists(logPath))
				loggerFactory.AddFile(logPath, $"{Global.ServiceName}.http.{Environment.ProcessId}");
			else
				logPath = null;

			Logger.AssignLoggerFactory(loggerFactory);
			Global.Logger = loggerFactory.CreateLogger<Startup>();

			Global.Logger.LogInformation($"The {Global.ServiceName} HTTP service is starting");
			Global.Logger.LogInformation($"Version: {typeof(Startup).Assembly.GetVersion()}");
#if DEBUG
			Global.Logger.LogInformation($"Working mode: DEBUG ({(environment.IsDevelopment() ? "Development" : "Production")})");
#else
			Global.Logger.LogInformation($"Working mode: RELEASE ({(environment.IsDevelopment() ? "Development" : "Production")})");
#endif
			Global.Logger.LogInformation($"Service URIs:\r\n\t- Round robin: services.{Global.ServiceName.ToLower()}.http\r\n\t- Single (unique): services.{Extensions.GetUniqueName($"{Global.ServiceName}.http")}");
			Global.Logger.LogInformation($"Environment:\r\n\t{Extensions.GetRuntimeEnvironment()}\r\n\t- Node ID: {Extensions.GetNodeID()}");

			Global.CreateRSA();
			Global.ServiceProvider = appBuilder.ApplicationServices;
			Global.RootPath = environment.ContentRootPath;

			JsonConvert.DefaultSettings = () => new JsonSerializerSettings
			{
				Formatting = Formatting.None,
				ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
				DateTimeZoneHandling = DateTimeZoneHandling.Local
			};

			// prepare outgoing proxy
			var proxy = UtilityService.GetAppSetting("Proxy:Host");
			if (!string.IsNullOrWhiteSpace(proxy))
				try
				{
					UtilityService.AssignWebProxy(proxy, UtilityService.GetAppSetting("Proxy:Port").CastAs<int>(), UtilityService.GetAppSetting("Proxy:User"), UtilityService.GetAppSetting("Proxy:UserPassword"), UtilityService.GetAppSetting("Proxy:Bypass")?.ToArray(";"));
				}
				catch (Exception ex)
				{
					Global.Logger.LogError($"Error occurred while assigning web-proxy => {ex.Message}", ex);
				}

			// setup WebSocket
			Handler.InitializeWebSocket();

			// setup the middleware
			appBuilder
				.UseForwardedHeaders(Global.GetForwardedHeadersOptions())
				.UseStatusCodeHandler()
				.UseResponseCompression()
				.UseCache()
				.UseSession()
				.UseCertificateForwarding()
				.UseCookiePolicy()
				.UseAuthentication()
				.UseWebSockets(new WebSocketOptions
				{
					KeepAliveInterval = Handler.WebSocket.KeepAliveInterval
				})
				.UseMiddleware<Authenticator>()
				.UseMiddleware<Handler>();

			// caching of centerlized services
			Handler.Cache = Cache.CreateInstance("VIEApps-Services-Portals", loggerFactory);

			// connect to API Gateway
			Handler.Connect();

			// on started
			appLifetime.ApplicationStarted.Register(() =>
			{
				Global.Logger.LogInformation($"API Gateway Router: {new Uri(Router.GetRouterStrInfo()).GetResolvedURI()}");
				Global.Logger.LogInformation($"API Gateway HTTP service: {UtilityService.GetAppSetting("HttpUri:APIs", "None")}");
				Global.Logger.LogInformation($"Files HTTP service: {UtilityService.GetAppSetting("HttpUri:Files", "None")}");
				Global.Logger.LogInformation($"Portals HTTP service: {UtilityService.GetAppSetting("HttpUri:Portals", "None")}");
				Global.Logger.LogInformation($"CMSPortals HTTP service: {UtilityService.GetAppSetting("HttpUri:CMSPortals", "None")}");
				Global.Logger.LogInformation($"Root (base) directory: {Global.RootPath}");
				Global.Logger.LogInformation($"Temporary directory: {UtilityService.GetAppSetting("Path:Temp", "None")}");
				Global.Logger.LogInformation($"Status files directory: {UtilityService.GetAppSetting("Path:Status", "None")}");
				Global.Logger.LogInformation($"Static files directory: {UtilityService.GetAppSetting("Path:Statics", "None")}");
				Global.Logger.LogInformation($"Static segments: {Global.StaticSegments.ToString(", ")}");
				Global.Logger.LogInformation($"Logging level: {this.LogLevel} - Rolling log files is {(string.IsNullOrWhiteSpace(logPath) ? "disabled" : $"enabled => {logPath}")}");
				Global.Logger.LogInformation($"Show debugs: {Global.IsDebugLogEnabled} - Show results: {Global.IsDebugResultsEnabled} - Show stacks: {Global.IsDebugStacksEnabled}");
				Global.Logger.LogInformation($"Request limit: {UtilityService.GetAppSetting("Limits:Body", "10")} MB");

				stopwatch.Stop();
				Global.Logger.LogInformation($"The {Global.ServiceName} HTTP service was started - PID: {Environment.ProcessId} - Execution times: {stopwatch.GetElapsedTimes()}");
				Global.Logger = loggerFactory.CreateLogger<Handler>();
			});

			// on stopping
			appLifetime.ApplicationStopping.Register(() =>
			{
				Global.Logger = loggerFactory.CreateLogger<Startup>();
				Global.RSA.Dispose();
				Handler.Disconnect();
			});

			// on stopped
			appLifetime.ApplicationStopped.Register(() =>
			{
				Global.Logger.LogInformation($"The {Global.ServiceName} HTTP service was stopped");
			});

			// don't terminate the process immediately, wait for the main thread to exit gracefully
			Console.CancelKeyPress += (sender, args) =>
			{
				appLifetime.StopApplication();
				args.Cancel = true;
			};
		}
	}
}
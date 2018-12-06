using System.Linq;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using net.vieapps.Components.Utility;

namespace net.vieapps.Services.Users
{
	public static class Program
	{
		public static void Main(string[] args)
		{
			WebHost.CreateDefaultBuilder(args)
				.CaptureStartupErrors(true)
				.UseStartup<Startup>()
				.UseKestrel(options => options.AddServerHeader = false)
				.UseUrls(args.FirstOrDefault(a => a.IsStartsWith("/listenuri:"))?.Replace("/listenuri:", "") ?? UtilityService.GetAppSetting("HttpUri:Listen", "http://0.0.0.0:8026").Trim())
				.Build()
				.Run();
		}
	}
}
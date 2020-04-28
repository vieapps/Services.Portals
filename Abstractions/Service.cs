using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using WampSharp.V2.Rpc;
using Newtonsoft.Json.Linq;
using net.vieapps.Components.Repository;
namespace net.vieapps.Services.Portals
{
	/// <summary>
	/// Presents a service of CMS Portals
	/// </summary>
	public interface ICmsPortalsService : IService, IUniqueService, IServiceComponent
	{
		/// <summary>
		/// Registers the service with API Gateway
		/// </summary>
		/// <param name="args">The arguments for registering</param>
		/// <param name="onSuccess">The action to run when the service was registered successful</param>
		/// <param name="onError">The action to run when got any error</param>
		/// <returns></returns>
		Task RegisterServiceAsync(IEnumerable<string> args, Action<ServiceBase> onSuccess = null, Action<Exception> onError = null);

		/// <summary>
		/// Unregisters the service with API Gateway
		/// </summary>
		/// <param name="args">The arguments for unregistering</param>
		/// <param name="available">true to mark the service still available</param>
		/// <param name="onSuccess">The action to run when the service was unregistered successful</param>
		/// <param name="onError">The action to run when got any error</param>
		Task UnregisterServiceAsync(IEnumerable<string> args, bool available = true, Action<ServiceBase> onSuccess = null, Action<Exception> onError = null);

		/// <summary>
		/// Gets the definition
		/// </summary>
		/// <returns></returns>
		[WampProcedure("cms.portals.{0}.definitions")]
		ModuleDefinition GetDefinition();
	}

}
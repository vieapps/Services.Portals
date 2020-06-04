using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using WampSharp.V2.Rpc;
using Newtonsoft.Json.Linq;
using net.vieapps.Components.Repository;
using net.vieapps.Components.Security;

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
		Task RegisterServiceAsync(IEnumerable<string> args, Action<IService> onSuccess = null, Action<Exception> onError = null);

		/// <summary>
		/// Unregisters the service with API Gateway
		/// </summary>
		/// <param name="args">The arguments for unregistering</param>
		/// <param name="available">true to mark the service still available</param>
		/// <param name="onSuccess">The action to run when the service was unregistered successful</param>
		/// <param name="onError">The action to run when got any error</param>
		Task UnregisterServiceAsync(IEnumerable<string> args, bool available = true, Action<IService> onSuccess = null, Action<Exception> onError = null);

		/// <summary>
		/// Gets the definition for working with CMS Portals
		/// </summary>
		/// <returns></returns>
		[WampProcedure("cms.portals.get.definition.{0}")]
		ModuleDefinition GetDefinition();

		/// <summary>
		/// Generates the content for working with CMS Portals
		/// </summary>
		/// <param name="requestInfo">The requesting information</param>
		/// <param name="cancellationToken">The cancellation token</param>
		/// <returns></returns>
		[WampProcedure("cms.portals.generate.content.{0}")]
		Task<JObject> GenerateAsync(RequestInfo requestInfo, CancellationToken cancellationToken = default);

		/// <summary>
		/// Generates the menu for working with CMS Portals
		/// </summary>
		/// <param name="requestInfo">The requesting information</param>
		/// <param name="cancellationToken">The cancellation token</param>
		/// <returns></returns>
		[WampProcedure("cms.portals.generate.menu.{0}")]
		Task<JArray> GenerateMenuAsync(RequestInfo requestInfo, CancellationToken cancellationToken = default);
	}

}
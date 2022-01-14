#region Related components
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using WampSharp.V2.Rpc;
using Newtonsoft.Json.Linq;
using net.vieapps.Components.Utility;
#endregion

namespace net.vieapps.Services.Portals
{
	/// <summary>
	/// Presents a service of CMS Portals
	/// </summary>
	public interface ICmsPortalsService : IService, IUniqueService, IServiceComponent
	{
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

	public static class CmsPortalsServiceExtensions
	{
		internal static ConcurrentDictionary<string, ICmsPortalsService> Services { get; } = new ConcurrentDictionary<string, ICmsPortalsService>(StringComparer.OrdinalIgnoreCase);

		/// <summary>
		/// Gets a service of CMS Portals that specified by a name
		/// </summary>
		/// <param name="name">The string that presents the name of a service in CMS Portals</param>
		/// <returns></returns>
		public static ICmsPortalsService GetService(string name)
		{
			if (string.IsNullOrWhiteSpace(name))
				throw new ServiceNotFoundException("The service name is null or empty");

			if (!CmsPortalsServiceExtensions.Services.TryGetValue(name, out var service))
			{
				service = Router.OutgoingChannel.RealmProxy.Services.GetCalleeProxy<ICmsPortalsService>(ProxyInterceptor.Create(name));
				CmsPortalsServiceExtensions.Services.TryAdd(name, service);
			}

			return service ?? throw new ServiceNotFoundException($"The service \"{name.ToLower()}\" is not found");
		}

		/// <summary>
		/// Gets a service of CMS Portals that specified by a name
		/// </summary>
		/// <param name="contentTypeDefinition"></param>
		/// <returns></returns>
		public static ICmsPortalsService GetService(this ContentTypeDefinition contentTypeDefinition)
			=> CmsPortalsServiceExtensions.GetService(contentTypeDefinition.ModuleDefinition.ServiceName);

		/// <summary>
		/// Gets a service of CMS Portals that specified by a name
		/// </summary>
		/// <param name="contentType"></param>
		/// <returns></returns>
		public static ICmsPortalsService GetService(this IPortalContentType contentType)
			=> contentType.ContentTypeDefinition.GetService();

		/// <summary>
		/// Registers the communicator to the commnunicating subject of CMS Portals service (URI: messages.services.cms.portals)
		/// </summary>
		/// <param name="onNext"></param>
		/// <param name="onError"></param>
		/// <returns></returns>
		public static IDisposable RegisterServiceCommunicator(Action<CommunicateMessage> onNext, Action<Exception> onError)
			=> Router.IncomingChannel.RealmProxy.Services.GetSubject<CommunicateMessage>("messages.services.cms.portals").Subscribe(message => onNext?.Invoke(message), exception => onError?.Invoke(exception));
	}
}
using System;
using System.Collections.Concurrent;
using net.vieapps.Components.Utility;

namespace net.vieapps.Services.Portals
{
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
				CmsPortalsServiceExtensions.Services.Add(name, service);
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
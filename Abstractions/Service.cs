using System.Threading;
using System.Threading.Tasks;
using WampSharp.V2.Rpc;
using Newtonsoft.Json.Linq;

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

}
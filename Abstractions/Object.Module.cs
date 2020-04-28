using System.Collections.Generic;
namespace net.vieapps.Services.Portals
{
	/// <summary>
	/// Presents a business module of a portal
	/// </summary>
	public interface IPortalModule : IPortalObject
	{
		/// <summary>
		/// Gets the organization that this object is belong to
		/// </summary>
		IPortalObject Organization { get; }

		/// <summary>
		/// Gets the identity of the module definition
		/// </summary>
		string ModuleDefinitionID { get; }

		/// <summary>
		/// Gets the module definition
		/// </summary>
		ModuleDefinition ModuleDefinition { get; }

		/// <summary>
		/// Gets the type name of the repository definition
		/// </summary>
		string RepositoryDefinitionTypeName { get; }

		/// <summary>
		/// Gets the collection of business content-types
		/// </summary>
		List<IPortalContentType> ContentTypes { get; }
	}
}
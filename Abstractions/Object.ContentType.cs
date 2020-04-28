namespace net.vieapps.Services.Portals
{
	/// <summary>
	/// Presents a business content-type of a portal
	/// </summary>
	public interface IPortalContentType : IPortalObject
	{
		/// <summary>
		/// Gets the organization that this object is belong to
		/// </summary>
		IPortalObject Organization { get; }

		/// <summary>
		/// Gets the identity of the business module that this object is belong to
		/// </summary>
		string ModuleID { get; }

		/// <summary>
		/// Gets the business module that this object is belong to
		/// </summary>
		IPortalModule Module { get; }

		/// <summary>
		/// Gets the identity of the content-type definition
		/// </summary>
		string ContentTypeDefinitionID { get; }

		/// <summary>
		/// Gets the module definition
		/// </summary>
		ContentTypeDefinition ContentTypeDefinition { get; }

		/// <summary>
		/// Gets the type name of the entity definition
		/// </summary>
		string EntityDefinitionTypeName { get; }
	}
}
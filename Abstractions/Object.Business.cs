using net.vieapps.Components.Repository;
namespace net.vieapps.Services.Portals
{
	/// <summary>
	/// Presents a business object of a portal
	/// </summary>
	public interface IBusinessObject : IPortalObject, IBusinessEntity
	{
		/// <summary>
		/// Gets the organization that this object is belong to
		/// </summary>
		IPortalObject Organization { get; }

		/// <summary>
		/// Gets the identity of a business module that the object is belong to
		/// </summary>
		string ModuleID { get; }

		/// <summary>
		/// Gets the business module that this object is belong to
		/// </summary>
		IPortalModule Module { get; }

		/// <summary>
		/// Gets the identity of a business content-type that the object is belong to
		/// </summary>
		string ContentTypeID { get; }

		/// <summary>
		/// Gets the business content-type that this object is belong to
		/// </summary>
		IPortalContentType ContentType { get; }
	}
}
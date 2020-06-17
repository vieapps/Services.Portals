using net.vieapps.Components.Repository;
using net.vieapps.Components.Security;
namespace net.vieapps.Services.Portals
{
	/// <summary>
	/// Presents a business object of a portal
	/// </summary>
	public interface IBusinessObject : IPortalObject, IBusinessEntity
	{
		/// <summary>
		/// Gets the approval status
		/// </summary>
		ApprovalStatus Status { get; }

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

		/// <summary>
		/// Gets the public URL
		/// </summary>
		/// <param name="desktop">The string that presents the alias of a desktop</param>
		/// <param name="addPageNumberHolder">true to add the page-number holder ({{pageNumber}})</param>
		/// <returns></returns>
		string GetURL(string desktop = null, bool addPageNumberHolder = false);
	}
}
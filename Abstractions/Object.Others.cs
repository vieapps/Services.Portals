using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using net.vieapps.Components.Security;
using net.vieapps.Components.Repository;
namespace net.vieapps.Services.Portals
{
	/// <summary>
	/// Presents a business module of a portal
	/// </summary>
	public interface IPortalModule : IPortalObject, IRepository
	{
		/// <summary>
		/// Gets the type name of the module definition (means the type name of a repository)
		/// </summary>
		string DefinitionType { get; }
	}

	//  --------------------------------------------------------------------------------------------

	/// <summary>
	/// Presents a business content-type of a portal
	/// </summary>
	public interface IPortalContentType : IPortalObject, IRepositoryEntity
	{
		/// <summary>
		/// Gets the type name of the content-type definition (means the type name of a repository entity)
		/// </summary>
		string DefinitionType { get; }

		/// <summary>
		/// Gets the identity of a module that this object is belong to
		/// </summary>
		string ModuleID { get; }
	}

	//  -----------------------------------------------------------

	/// <summary>
	/// Presents a business object of a portal
	/// </summary>
	public interface IBusinessObject : IPortalObject, IBusinessEntity
	{
		/// <summary>
		/// Gets the identity of a business module that the object is belong to
		/// </summary>
		string ModuleID { get; }

		/// <summary>
		/// Gets the identity of a business content-type that the object is belong to
		/// </summary>
		string ContentTypeID { get; }
	}

	//  -----------------------------------------------------------

	/// <summary>
	/// Presents a nested object (means an object that can contain itself) of a portal
	/// </summary>
	public interface INestedObject : IPortalObject
	{
		/// <summary>
		/// Gets or sets the identity of parent object
		/// </summary>
		string ParentID { get; set; }

		/// <summary>
		/// Gets or sets order index
		/// </summary>
		int OrderIndex { get; set; }

		/// <summary>
		/// Gets the full title for displaying
		/// </summary>
		string FullTitle { get; }

		/// <summary>
		/// Gets the parent object
		/// </summary>
		new INestedObject Parent { get; }

		/// <summary>
		/// Gets the collection of child objects
		/// </summary>
		List<INestedObject> Children { get; }
	}
}
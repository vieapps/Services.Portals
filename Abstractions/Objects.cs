using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using net.vieapps.Components.Security;
namespace net.vieapps.Services.Portals
{
	/// <summary>
	/// Presents a business object of a portal
	/// </summary>
	public interface IPortalObject
	{
		/// <summary>
		/// Gets the identity
		/// </summary>
		string ID { get; }

		/// <summary>
		/// Gets the title
		/// </summary>
		string Title { get; }

		/// <summary>
		/// Gets the identity of an organization that the object is belong to
		/// </summary>
		string OrganizationID { get; }

		/// <summary>
		/// Gets the identity of a business module that the object is belong to
		/// </summary>
		string ModuleID { get; }

		/// <summary>
		/// Gets the identity of a business content-type that the object is belong to
		/// </summary>
		string ContentTypeID { get; }

		/// <summary>
		/// Gets or sets the collection of extended properties
		/// </summary>
		Dictionary<string, object> ExtendedProperties { get; set; }

		/// <summary>
		/// Gets the portal object that marks as parent of this object
		/// </summary>
		IPortalObject Parent { get; }

		/// <summary>
		/// Gets or sets the original privileges (means original working permissions) of this object
		/// </summary>
		Privileges OriginalPrivileges { get; set; }

		/// <summary>
		/// Gets the actual privileges (mean the combined privileges) of this object
		/// </summary>
		Privileges WorkingPrivileges { get; }

		/// <summary>
		/// Gets the time when object is created
		/// </summary>
		DateTime Created { get; }

		/// <summary>
		/// Gets the identity of an user who creates this object at the first-time
		/// </summary>
		string CreatedID { get; }

		/// <summary>
		/// Gets the time when object is modified at the last-time
		/// </summary>
		DateTime LastModified { get; }

		/// <summary>
		/// Gets the identity of an user who modifies this object at the last-time
		/// </summary>
		string LastModifiedID { get; }
	}

	//  -----------------------------------------------------------

	/// <summary>
	/// Presents a nested object of a portal
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
		/// Gets the full title for displaying.
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
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using net.vieapps.Components.Security;
using net.vieapps.Components.Repository;
namespace net.vieapps.Services.Portals
{
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
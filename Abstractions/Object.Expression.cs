using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using net.vieapps.Components.Repository;
namespace net.vieapps.Services.Portals
{
	/// <summary>
	/// Presents an expression for filtering portal objects
	/// </summary>
	public interface IPortalExpression : IPortalObject
	{
		/// <summary>
		/// Gets the identity of a module that this object is related to
		/// </summary>
		string ModuleID { get; }

		/// <summary>
		/// Gets the type name of the content-type definition what the object is related to
		/// </summary>
		string ContentTypeDefinition { get; }

		/// <summary>
		/// Gets the identity of a business content-type that the object is related to
		/// </summary>
		string ContentTypeID { get; }

		/// <summary>
		/// Gets the filter expression
		/// </summary>
		IFilterBy FilterBy { get; }

		/// <summary>
		/// Gets the sort expression
		/// </summary>
		ISortBy SortBy { get; }
	}
}
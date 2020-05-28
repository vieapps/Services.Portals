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
		/// Gets the identity of a content-type definition that the object is related to
		/// </summary>
		string ContentTypeDefinitionID { get; }

		/// <summary>
		/// Gets the identity of a business content-type that the object is related to
		/// </summary>
		string ContentTypeID { get; }

		/// <summary>
		/// Gets the filter expression
		/// </summary>
		IFilterBy FilterBy { get; }

		/// <summary>
		/// Gets the default sort expression
		/// </summary>
		ISortBy SortBy { get; }

		/// <summary>
		/// Gets the collection of sort expressions
		/// </summary>
		List<ISortBy> SortBys { get; }
	}
}
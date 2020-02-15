#region Related components
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using net.vieapps.Components.Utility;
using net.vieapps.Components.Repository;
#endregion

namespace net.vieapps.Services.Portals
{
	[EventHandlers]
	public class EventHandlers: IPostCreateHandler, IPostUpdateHandler, IPostDeleteHandler
	{
		public void OnPostCreate<T>(RepositoryContext context, T @object, bool isRestore) where T : class
		{
			if (@object is Organization)
			{

			}
		}

		public Task OnPostCreateAsync<T>(RepositoryContext context, T @object, bool isRestore, CancellationToken cancellationToken) where T : class
		{
			if (@object is Organization)
			{

			}
			return Task.CompletedTask;
		}

		public void OnPostUpdate<T>(RepositoryContext context, T @object, HashSet<string> changed, bool isRollback) where T : class
		{
			if (@object is Organization)
			{

			}
		}

		public Task OnPostUpdateAsync<T>(RepositoryContext context, T @object, HashSet<string> changed, bool isRollback, CancellationToken cancellationToken) where T : class
		{
			if (@object is Organization)
			{

			}
			return Task.CompletedTask;
		}

		public void OnPostDelete<T>(RepositoryContext context, T @object) where T : class
		{
			if (@object is Organization)
			{

			}
		}

		public Task OnPostDeleteAsync<T>(RepositoryContext context, T @object, CancellationToken cancellationToken) where T : class
		{
			if (@object is Organization)
			{
				
			}
			return Task.CompletedTask;
		}
	}
}
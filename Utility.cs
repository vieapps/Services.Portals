#region Related components
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;

using Newtonsoft.Json.Linq;

using net.vieapps.Components.Utility;
using net.vieapps.Components.Caching;
using net.vieapps.Components.Repository;
#endregion

namespace net.vieapps.Services.Systems
{
	public static class Utility
	{

		#region Caching mechanism
		static int _CacheTime = 0;

		internal static int CacheExpirationTime
		{
			get
			{
				if (Utility._CacheTime < 1)
					try
					{
						Utility._CacheTime = UtilityService.GetAppSetting("Cache:ExpirationTime", "30").CastAs<int>();
					}
					catch
					{
						Utility._CacheTime = 30;
					}
				return Utility._CacheTime;
			}
		}

		static Cache _Cache = new Cache("VIEApps-Services-Systems", Utility.CacheExpirationTime, UtilityService.GetAppSetting("Cache:Provider"));

		public static Cache Cache { get { return Utility._Cache; } }
		#endregion

		static string _FilesHttpUri = null;

		static string FilesHttpUri
		{
			get
			{
				if (string.IsNullOrWhiteSpace(Utility._FilesHttpUri))
					Utility._FilesHttpUri = UtilityService.GetAppSetting("HttpUri:Files", "https://afs.vieapps.net");
				while (Utility._FilesHttpUri.EndsWith("/"))
					Utility._FilesHttpUri = Utility._FilesHttpUri.Left(Utility._FilesHttpUri.Length - 1);
				return Utility._FilesHttpUri;
			}
		}

	}

	//  --------------------------------------------------------------------------------------------

	[Serializable]
	[Repository(Title = "Systems", Description = "Information for managing the business system", ID = "00000000000000000000000000000001")]
	public abstract class Repository<T> : RepositoryBase<T> where T : class { }
}
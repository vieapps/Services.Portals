﻿#region Related components
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
						Utility._CacheTime = UtilityService.GetAppSetting("CacheExpirationTime", "30").CastAs<int>();
					}
					catch
					{
						Utility._CacheTime = 30;
					}
				return Utility._CacheTime;
			}
		}

		static Cache _Cache = new Cache("VIEApps-Services-Systems", Utility.CacheExpirationTime, UtilityService.GetAppSetting("CacheProvider"));

		public static Cache Cache { get { return Utility._Cache; } }
		#endregion

		static string _HttpFilesUri = null;

		static string HttpFilesUri
		{
			get
			{
				if (string.IsNullOrWhiteSpace(Utility._HttpFilesUri))
					Utility._HttpFilesUri = UtilityService.GetAppSetting("HttpFilesUri", "https://afs.vieapps.net");
				while (Utility._HttpFilesUri.EndsWith("/"))
					Utility._HttpFilesUri = Utility._HttpFilesUri.Left(Utility._HttpFilesUri.Length - 1);
				return Utility._HttpFilesUri;
			}
		}

	}

	//  --------------------------------------------------------------------------------------------

	[Serializable]
	[Repository(Title = "Systems", Description = "Information for managing the business system", ID = "00000000000000000000000000000001")]
	public abstract class Repository<T> : RepositoryBase<T> where T : class { }
}
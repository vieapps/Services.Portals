#region Related components
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Xml.Serialization;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using MongoDB.Bson.Serialization.Attributes;

using net.vieapps.Components.Utility;
using net.vieapps.Components.Caching;
using net.vieapps.Components.Repository;
#endregion

namespace net.vieapps.Services.Systems
{
	public static class Utility
	{
		public static Cache Cache { get; } = new Cache("VIEApps-Services-Systems", UtilityService.GetAppSetting("Cache:ExpirationTime", "30").CastAs<int>(), false, UtilityService.GetAppSetting("Cache:Provider"), Logger.GetLoggerFactory());

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
	public abstract class Repository<T> : RepositoryBase<T> where T : class
	{
		/// <summary>
		/// Gets the name of the service that associates with this repository
		/// </summary>
		[JsonIgnore, XmlIgnore, BsonIgnore, Ignore]
		public override string ServiceName
		{
			get { return "Systems"; }
		}
	}
}
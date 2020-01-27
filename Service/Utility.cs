#region Related components
using System;
using System.Xml.Serialization;

using Newtonsoft.Json;
using MongoDB.Bson.Serialization.Attributes;

using net.vieapps.Components.Utility;
using net.vieapps.Components.Caching;
using net.vieapps.Components.Repository;
#endregion

namespace net.vieapps.Services.Portals
{
	public static partial class Utility
	{
		public static Cache Cache { get; } = new Cache("VIEApps-Services-Portals", UtilityService.GetAppSetting("Cache:ExpirationTime", "30").CastAs<int>(), false, UtilityService.GetAppSetting("Cache:Provider"), Logger.GetLoggerFactory());

		static string _FilesHttpUri = null;

		public static string FilesHttpUri
		{
			get
			{
				if (string.IsNullOrWhiteSpace(Utility._FilesHttpUri))
				{
					Utility._FilesHttpUri = UtilityService.GetAppSetting("HttpUri:Files", "https://fs.vieapps.net");
					while (Utility._FilesHttpUri.EndsWith("/"))
						Utility._FilesHttpUri = Utility._FilesHttpUri.Left(Utility._FilesHttpUri.Length - 1);
				}
				return Utility._FilesHttpUri;
			}
		}
	}

	//  --------------------------------------------------------------------------------------------

	[Serializable, Repository(ID = "00000000000000000000000000000001", Title = "Portals", Description = "Managing core information of portals and related")]
	public abstract class Repository<T> : RepositoryBase<T> where T : class
	{
		[JsonIgnore, XmlIgnore, BsonIgnore, Ignore]
		public override string ServiceName => ServiceBase.ServiceComponent.ServiceName;
	}
}
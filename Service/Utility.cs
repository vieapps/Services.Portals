#region Related components
using System;
using System.Linq;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
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
		/// <summary>
		/// Gets the cache storage
		/// </summary>
		public static Cache Cache { get; } = new Cache("VIEApps-Services-Portals", UtilityService.GetAppSetting("Cache:ExpirationTime", "30").CastAs<int>(), false, UtilityService.GetAppSetting("Cache:Provider"), Logger.GetLoggerFactory());

		static string _APIsHttpUri, _FilesHttpUri, _PortalsHttpUri, _PassportsHttpUri;

		/// <summary>
		/// Gets the URI of the public APIS
		/// </summary>
		public static string APIsHttpUri
		{
			get
			{
				if (string.IsNullOrWhiteSpace(Utility._APIsHttpUri))
				{
					Utility._APIsHttpUri = UtilityService.GetAppSetting("HttpUri:APIs", "https://apis.vieapps.net");
					while (Utility._APIsHttpUri.EndsWith("/"))
						Utility._APIsHttpUri = Utility._APIsHttpUri.Left(Utility._APIsHttpUri.Length - 1);
				}
				return Utility._APIsHttpUri;
			}
		}

		/// <summary>
		/// Gets the URI of the Files HTTP service
		/// </summary>
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

		/// <summary>
		/// Gets the URI of the Portals HTTP service
		/// </summary>
		public static string PortalsHttpUri
		{
			get
			{
				if (string.IsNullOrWhiteSpace(Utility._PortalsHttpUri))
				{
					Utility._PortalsHttpUri = UtilityService.GetAppSetting("HttpUri:Portals", "https://portals.vieapps.net");
					while (Utility._PortalsHttpUri.EndsWith("/"))
						Utility._PortalsHttpUri = Utility._PortalsHttpUri.Left(Utility._PortalsHttpUri.Length - 1);
				}
				return Utility._PortalsHttpUri;
			}
		}

		/// <summary>
		/// Gets the URI of the Passports HTTP service
		/// </summary>
		public static string PassportsHttpUri
		{
			get
			{
				if (string.IsNullOrWhiteSpace(Utility._PassportsHttpUri))
				{
					Utility._PassportsHttpUri = UtilityService.GetAppSetting("HttpUri:Passports", "https://id.vieapps.net");
					while (Utility._PassportsHttpUri.EndsWith("/"))
						Utility._PassportsHttpUri = Utility._PassportsHttpUri.Left(Utility._PassportsHttpUri.Length - 1);
				}
				return Utility._PassportsHttpUri;
			}
		}

		public static string NormalizeAlias(this string alias, bool allowMinusSymbols = true)
		{
			alias = alias.GetANSIUri();
			while (alias.StartsWith("-") || alias.StartsWith("_"))
				alias = alias.Right(alias.Length - 1);
			while (alias.EndsWith("-") || alias.EndsWith("_"))
				alias = alias.Left(alias.Length - 1);
			return allowMinusSymbols ? alias : alias.Replace("-", "");
		}
	}

	//  --------------------------------------------------------------------------------------------

	[Serializable, Repository(ID = "00000000000000000000000000000001", Title = "Portals", Description = "Managing core information of portals and related services", Directory = "Portals")]
	public abstract class Repository<T> : RepositoryBase<T> where T : class
	{
		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public override string ServiceName => ServiceBase.ServiceComponent.ServiceName;
	}
}
#region Related components
using System;
using System.Linq;
using System.Diagnostics;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Serialization;

using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

using net.vieapps.Components.Utility;
using net.vieapps.Components.Security;
using net.vieapps.Components.Repository;
#endregion

namespace net.vieapps.Services.Portals
{
	/// <summary>
	/// Presents information for working with SEO (search engine optimization)
	/// </summary>
	[Serializable, BsonIgnoreExtraElements]
	public class SEOInfo
	{
		public SEOInfo() { }
		public string Title { get; set; }
		public string Description { get; set; }
		public string Keywords { get; set; }
	}
}
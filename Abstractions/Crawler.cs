#region Related components
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using net.vieapps.Components.Utility;
using net.vieapps.Components.Security;
#endregion

namespace net.vieapps.Services.Portals.Crawlers
{
	public interface ICrawler
	{
		/// <summary>
		/// Gets the information of this crawler
		/// </summary>
		ICrawlerInfo CrawlerInfo { get; }

		/// <summary>
		/// Fetchs the collection of categories
		/// </summary>
		/// <param name="cancellationToken"></param>
		/// <returns></returns>
		Task<List<Category>> FetchCategoriesAsync(CancellationToken cancellationToken = default);

		/// <summary>
		/// Fetch the collection of contents
		/// </summary>
		/// <param name="url"></param>
		/// <param name="normalizeAsync"></param>
		/// <param name="cancellationToken"></param>
		/// <returns></returns>
		Task<List<Content>> FetchContentsAsync(string url, Func<Content, CancellationToken, Task> normalizeAsync = null, CancellationToken cancellationToken = default);

		/// <summary>
		/// Fetchs a content
		/// </summary>
		/// <param name="url"></param>
		/// <param name="normalizeAsync"></param>
		/// <param name="cancellationToken"></param>
		/// <returns></returns>
		Task<Content> FetchContentAsync(string url, Func<Content, CancellationToken, Task> normalizeAsync = null, CancellationToken cancellationToken = default);

		/// <summary>
		/// Gets a content from raw data
		/// </summary>
		/// <param name="data"></param>
		/// <param name="normalizeAsync"></param>
		/// <param name="cancellationToken"></param>
		/// <returns></returns>
		Task<Content> GetContentAsync(JObject data, Func<Content, CancellationToken, Task> normalizeAsync = null, CancellationToken cancellationToken = default);
	}

	public interface ICrawlerAdapter
	{
		/// <summary>
		/// Gets the information of this crawler
		/// </summary>
		ICrawlerInfo CrawlerInfo { get; set; }

		/// <summary>
		/// Normalizes information of a content
		/// </summary>
		/// <param name="content"></param>
		/// <param name="cancellationToken"></param>
		/// <returns></returns>
		Task NormalizeAsync(Content content, CancellationToken cancellationToken = default);
	}

	public interface ICrawlerInfo
	{
		string Title { get; set; }
		string Description { get; set; }
		Type Type { get; set; }
		string URL { get; set; }
		string SystemID { get; set; }
		string RepositoryID { get; set; }
		string RepositoryEntityID { get; set; }
		bool SetAuthor { get; set; }
		bool SetSource { get; set; }
		string NormalizingAdapter { get; set; }
		ApprovalStatus DefaultStatus { get; set; }
		int MaxPages { get; set; }
		int Interval { get; set; }
		DateTime LastActivity { get; set; }
		List<string> SelectedCategories { get; set; }
		string DefaultCategoryID { get; set; }
		List<string> CategoryMappings { get; set; }
		string Options { get; set; }
		DateTime Created { get; set; }
		string CreatedID { get; set; }
		DateTime LastModified { get; set; }
		string LastModifiedID { get; set; }
		List<Category> Categories { get; set; }
	}

	/// <summary>
	/// Presents type of a crawler
	/// </summary>
	public enum Type
	{
		/// <summary>
		/// Presents a WordPress site with JSON
		/// </summary>
		WordPress,

		/// <summary>
		/// Presents a Blogger (Blogspot) site with JSON
		/// </summary>
		Blogger,

		/// <summary>
		/// Presents a Wix site with RSS
		/// </summary>
		Wix,

		/// <summary>
		/// Presents a normalize site with custom rules
		/// </summary>
		Custom
	}

	public class Category
	{
		public Category(string id = null, string title = null, string url = null)
		{
			this.ID = id;
			this.Title = title;
			this.URL = url;
		}
		public string ID { get; set; }
		public string Title { get; set; }
		public string URL { get; set; }
	}

	public class Content
	{
		public string Title { get; set; }
		public string Summary { get; set; }
		public string Details { get; set; }
		public string Author { get; set; }
		public string Source { get; set; }
		public string SourceURL { get; set; }
		public string Tags { get; set; }
		public ApprovalStatus Status { get; set; }
		public string Alias { get; set; }
		public string CategoryID { get; set; }
		public List<string> OtherCategories { get; set; }
		public string StartDate { get; set; }
		public string EndDate { get; set; }
		public DateTime PublishedTime { get; set; }
		public DateTime LastModified { get; set; }
		public string SourceURI { get; set; }
		public string ThumbnailURL { get; set; }
		public string ID { get; set; }
		public string SystemID { get; set; }
		public string RepositoryID { get; set; }
		public string RepositoryEntityID { get; set; }
		public static int PageSize { get; } = 20;
		public void Normalize(ICrawlerInfo crawlerInfo, bool normalizeCategories = false)
		{
			this.ID = string.IsNullOrWhiteSpace(this.ID) ? this.SourceURI?.GenerateUUID() ?? UtilityService.NewUUID : this.ID;
			this.Title = this.Title.HtmlDecode();
			this.Summary = (this.Summary ?? "").HtmlDecode().RemoveTags();
			this.StartDate = this.StartDate ?? this.PublishedTime.ToDTString(false, false);
			this.Alias = $"{this.StartDate}-{this.Title}".GetANSIUri();
			this.Status = crawlerInfo.DefaultStatus;
			this.SystemID = crawlerInfo.SystemID;
			this.RepositoryID = crawlerInfo.RepositoryID;
			this.RepositoryEntityID = crawlerInfo.RepositoryEntityID;
			if (normalizeCategories)
			{
				this.CategoryID = !string.IsNullOrWhiteSpace(this.CategoryID) ? crawlerInfo.CategoryMappings?.FirstOrDefault(catID => catID.IsStartsWith(this.CategoryID)).ToList(":").Last() ?? crawlerInfo.DefaultCategoryID : crawlerInfo.DefaultCategoryID;
				this.OtherCategories = this.OtherCategories?.Select(categoryID => crawlerInfo.CategoryMappings?.FirstOrDefault(catID => catID.IsStartsWith(categoryID)).ToList(":").Last()).Where(catID => !string.IsNullOrWhiteSpace(catID)).ToList();
			}
		}
	}

	public class Expression
	{
		public string Attribute { get; set; }
		public string Operator { get; set; }
		public string Value { get; set; }
		public string Replacement { get; set; }
	}
}
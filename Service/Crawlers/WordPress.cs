#region Related components
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using net.vieapps.Components.Utility;
#endregion

namespace net.vieapps.Services.Portals.Crawlers
{
	public class WordPress : ICrawler
	{
		ICrawlerInfo ICrawler.CrawlerInfo => this.CrawlerInfo;

		public Crawler CrawlerInfo { get; set; }

		/// <summary>
		/// Initializes the crawler
		/// </summary>
		/// <param name="crawlerInfo"></param>
		public WordPress(ICrawlerInfo crawlerInfo)
			=> this.CrawlerInfo = crawlerInfo as Crawler;

		public async Task<List<Category>> FetchCategoriesAsync(CancellationToken cancellationToken = default)
		{
			var json = JArray.Parse(await this.CrawlerInfo.FetchAsync($"{this.CrawlerInfo.URL}/wp-json/wp/v2/categories", cancellationToken).ConfigureAwait(false));
			return json.Select(category => category as JObject).Select(category =>
			{
				var id = category.Get<long>("id").As<string>();
				var title = category.Get<string>("name");
				var url = category.Get<JObject>("_links")?.Get<JArray>("wp:post_type")?.FirstOrDefault()?.Get<string>("href");
				return new Category(id, title, url);
			}).OrderBy(category => category.Title).ToList();
		}

		public async Task<List<Content>> FetchContentsAsync(string url, Func<Content, CancellationToken, Task> normalizeAsync = null,  CancellationToken cancellationToken = default)
		{
			try
			{
				url = url ?? $"{this.CrawlerInfo.URL}/wp-json/wp/v2/posts?per_page={Content.PageSize}";
				var json = JArray.Parse(await this.CrawlerInfo.FetchAsync(url, cancellationToken).ConfigureAwait(false));
				var contents = new List<Content>();
				await json.Select(data => data as JObject).ForEachAsync(async data =>
				{
					contents.Add(await this.GetContentAsync(data, normalizeAsync, cancellationToken).ConfigureAwait(false));
				}).ConfigureAwait(false);
				return contents.OrderByDescending(content => content.LastModified).ThenByDescending(content => content.PublishedTime).ToList();
			}
			catch
			{
				return new List<Content>();
			}
		}

		public async Task<Content> FetchContentAsync(string url, Func<Content, CancellationToken, Task> normalizeAsync = null, CancellationToken cancellationToken = default)
		{
			var data = JObject.Parse(await this.CrawlerInfo.FetchAsync(url, cancellationToken).ConfigureAwait(false));
			return await this.GetContentAsync(data, normalizeAsync, cancellationToken).ConfigureAwait(false);
		}

		public async Task<Content> GetContentAsync(JObject data, Func<Content, CancellationToken, Task> normalizeAsync = null, CancellationToken cancellationToken = default)
		{
			var content = new Content
			{
				Title = data.Get<JObject>("title")?.Get<string>("rendered")?.HtmlDecode(),
				Summary = data.Get<JObject>("excerpt")?.Get<string>("rendered")?.HtmlDecode(),
				Details = data.Get<JObject>("content")?.Get<string>("rendered")?.HtmlDecode(),
				ID = data.Get<JObject>("guid")?.Get<string>("rendered")?.GenerateUUID(),
				PublishedTime = DateTime.Parse(data.Get<string>("date")),
				LastModified = DateTime.Parse(data.Get<string>("modified"))
			};

			if (data["featured_media"] != null)
			{
				var media = JObject.Parse(await this.CrawlerInfo.FetchAsync($"{this.CrawlerInfo.URL}/wp-json/wp/v2/media/{data.Get<long>("featured_media")}", cancellationToken).ConfigureAwait(false));
				content.ThumbnailURL = media?.Get<JObject>("guid")?.Get<string>("rendered");
			}
			else
				content.ThumbnailURL = content.Details.FindTags().FirstOrDefault(tag => tag.Name.IsEquals("img"))?.Attributes.FirstOrDefault(attribute => attribute.Name.IsEquals("src"))?.Value;

			var categories = data.Get<JArray>("categories")?.Select(cat => cat as JValue).Select(cat => cat.Value.As<string>()).ToList();
			content.CategoryID = this.CrawlerInfo.Categories?.FirstOrDefault(cat => cat.ID == categories?.FirstOrDefault())?.ID;
			content.OtherCategories = categories?.Where(catID => catID != content.CategoryID && this.CrawlerInfo.Categories?.FirstOrDefault(cat => cat.ID == catID) != null).ToList();

			var tags = data.Get<JArray>("tags")?.Select(tag => tag as JValue).Select(tag => tag.Value.As<string>()).ToList();
			if (tags != null && tags.Any())
			{
				content.Tags = "";
				await tags.ForEachAsync(async id =>
				{
					var tag = JObject.Parse(await this.CrawlerInfo.FetchAsync($"{this.CrawlerInfo.URL}/wp-json/wp/v2/tags/{id}", cancellationToken).ConfigureAwait(false));
					content.Tags += (content.Tags != "" ? "," : "") + tag.Get<string>("name");
				}).ConfigureAwait(false);
			}

			if (this.CrawlerInfo.SetAuthor)
			{
				var author = JObject.Parse(await this.CrawlerInfo.FetchAsync($"{this.CrawlerInfo.URL}/wp-json/wp/v2/users/{data.Get<long>("author")}", cancellationToken).ConfigureAwait(false));
				content.Author = author?.Get<string>("name");
			}

			if (this.CrawlerInfo.SetSource)
			{
				content.Source = this.CrawlerInfo.Title;
				content.SourceURL = data.Get<string>("link");
			}
			content.SourceURI = data.Get<JObject>("_links")?.Get<JArray>("self")?.FirstOrDefault()?.Get<string>("href");

			if (normalizeAsync != null)
				try
				{
					await normalizeAsync(content, cancellationToken).ConfigureAwait(false);
				}
				catch { }

			return content;
		}
	}
}
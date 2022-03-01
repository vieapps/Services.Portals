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
	public class Blogger : ICrawler
	{
		ICrawlerInfo ICrawler.CrawlerInfo => this.CrawlerInfo;

		public Crawler CrawlerInfo { get; set; }

		public Blogger(ICrawlerInfo crawlerInfo)
			=> this.CrawlerInfo = crawlerInfo as Crawler;

		void GetInfo(JObject json)
		{
			this.CrawlerInfo.WebID = json.Get<JObject>("feed").Get<JObject>("id").Get<string>("$t").ToList("-").Last();
			this.CrawlerInfo.WebURL = "https://www.blogger.com/feeds/" + $"{this.CrawlerInfo.WebID}/posts/default?alt=json&max-results={Content.PageSize}&start-index=1";
		}

		public async Task<List<Category>> FetchCategoriesAsync(CancellationToken cancellationToken = default)
		{
			var json = JObject.Parse(await new Uri($"{this.CrawlerInfo.URL}/feeds/posts/summary?max-results=1&alt=json").FetchHttpAsync(UtilityService.DesktopUserAgent, this.CrawlerInfo.URL, cancellationToken).ConfigureAwait(false));
			if (string.IsNullOrWhiteSpace(this.CrawlerInfo.WebURL))
				this.GetInfo(json);
			return json.Get<JObject>("feed").Get<JArray>("category").Select(category => category as JObject).Select(category =>
			{
				var title = category.Get<string>("term");
				return new Category(title.GenerateUUID(), title, null);
			}).OrderBy(category => category.Title).ToList();
		}

		public async Task<List<Content>> FetchContentsAsync(string url, Func<Content, CancellationToken, Task> normalizeAsync = null, CancellationToken cancellationToken = default)
		{
			try
			{
				url = url ?? this.CrawlerInfo.WebURL;
				var contents = new List<Content>();
				var json = JObject.Parse(await new Uri(url).FetchHttpAsync(UtilityService.DesktopUserAgent, this.CrawlerInfo.URL, cancellationToken).ConfigureAwait(false));
				var feed = json.Get<JObject>("feed");
				await feed.Get<JArray>("entry").Select(data => data as JObject).ForEachAsync(async data =>
				{
					contents.Add(await this.GetContentAsync(data, normalizeAsync, cancellationToken).ConfigureAwait(false));
				}).ConfigureAwait(false);
				this.CrawlerInfo.WebURL = null;
				foreach (JObject link in feed.Get<JArray>("link"))
					if ("next" == link.Get<string>("rel"))
					{
						this.CrawlerInfo.WebURL = link.Get<string>("href");
						break;
					}
				return contents.OrderByDescending(content => content.LastModified).ThenByDescending(content => content.PublishedTime).ToList();
			}
			catch
			{
				return new List<Content>();
			}
		}

		public async Task<Content> FetchContentAsync(string url, Func<Content, CancellationToken, Task> normalizeAsync = null, CancellationToken cancellationToken = default)
		{
			var data = JObject.Parse(await new Uri(url).FetchHttpAsync(UtilityService.DesktopUserAgent, this.CrawlerInfo.URL, cancellationToken).ConfigureAwait(false));
			return await this.GetContentAsync(data.Get<JObject>("entry"), normalizeAsync, cancellationToken).ConfigureAwait(false);
		}

		public async Task<Content> GetContentAsync(JObject data, Func<Content, CancellationToken, Task> normalizeAsync = null, CancellationToken cancellationToken = default)
		{
			var content = new Content
			{
				Title = data.Get<JObject>("title")?.Get<string>("$t")?.HtmlDecode(),
				Summary = data.Get<JObject>("summary")?.Get<string>("$t")?.HtmlDecode(),
				Details = data.Get<JObject>("content")?.Get<string>("$t")?.HtmlDecode(),
				ID = data.Get<JObject>("id")?.Get<string>("$t")?.GenerateUUID(),
				PublishedTime = DateTime.Parse(data.Get<JObject>("published").Get<string>("$t")),
				LastModified = DateTime.Parse(data.Get<JObject>("updated").Get<string>("$t"))
			};

			content.ThumbnailURL = data.Get<JObject>("media$thumbnail")?.Get<string>("url");
			if (string.IsNullOrWhiteSpace(content.ThumbnailURL))
				content.ThumbnailURL = content.Details.FindTags().FirstOrDefault(tag => tag.Name.IsEquals("img"))?.Attributes.FirstOrDefault(attribute => attribute.Name.IsEquals("src"))?.Value;
			else
			{
				var start = content.ThumbnailURL.IndexOf("/s72-");
				var end = content.ThumbnailURL.IndexOf("/", start + 1) + 1;
				content.ThumbnailURL = content.ThumbnailURL.Substring(0, start) + "/s1920/" + content.ThumbnailURL.Substring(end, content.ThumbnailURL.Length - end);
			}

			var tags = data.Get<JArray>("category")?.Select(cat => cat as JObject).Select(cat => cat.Get<string>("term")).ToList();
			content.Tags = tags?.ToString(",");

			var categories = tags?.Select(tag => tag.GenerateUUID()).ToList();
			content.CategoryID = this.CrawlerInfo.Categories?.FirstOrDefault(cat => cat.ID == categories?.FirstOrDefault())?.ID;
			content.OtherCategories = categories?.Where(catID => catID != content.CategoryID && this.CrawlerInfo.Categories?.FirstOrDefault(cat => cat.ID == catID) != null).ToList();

			if (this.CrawlerInfo.SetAuthor)
				content.Author = data.Get<JObject>("author")?.Get<JObject>("name")?.Get<string>("$t");

			var links = data.Get<JArray>("link");
			if (this.CrawlerInfo.SetSource)
			{
				content.Source = this.CrawlerInfo.Title;
				foreach (JObject link in links)
					if ("alternate" == link.Get<string>("rel"))
					{
						content.SourceURL = link.Get<string>("href");
						break;
					}
			}
			foreach (JObject link in links)
				if ("self" == link.Get<string>("rel"))
				{
					content.SourceURI = $"{link.Get<string>("href")}?alt=json";
					break;
				}

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
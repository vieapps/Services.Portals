#region Related components
using System;
using System.Linq;
using System.Dynamic;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Collections.Generic;
using System.Collections.Concurrent;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using net.vieapps.Components.Repository;
using net.vieapps.Components.Security;
using net.vieapps.Components.Utility;
using net.vieapps.Services.Portals.Exceptions;
using net.vieapps.Services.Portals.Settings;
#endregion

namespace net.vieapps.Services.Portals
{
	public static class OrganizationProcessor
	{
		internal static ConcurrentDictionary<string, Organization> Organizations { get; } = new ConcurrentDictionary<string, Organization>(StringComparer.OrdinalIgnoreCase);

		internal static ConcurrentDictionary<AliasKey, Organization> OrganizationsByAlias { get; } = new ConcurrentDictionary<AliasKey, Organization>();

		internal static HashSet<string> ExcludedAliases { get; } = (UtilityService.GetAppSetting("Portals:ExcludedAliases", "") + ",APIs,CMS,CRM,MCP,Portals,Dashboard,Dashboards,Temp,Feed,Feeds,Atom,Rss").ToLower().ToHashSet();

		internal static HashSet<string> ExtraProperties { get; } = "Notifications,Instructions,Socials,Trackings,MetaTags,ScriptLibraries,Scripts,AlwaysUseHtmlSuffix,RefreshURLs,RedirectURLs,ExamineURLs,EmailSettings,WebHookSettings,HttpIndicators,FakeFilesHttpURI,FakePortalsHttpURI,CloudFlareZoneID,CloudFlareApiToken,McpSettings".ToHashSet();

		internal static List<string> MustUpdatedProperties { get; } = "HomeDesktopID,SearchDesktopID,MetaTags,Stylesheets,ScriptLibraries,Scripts,FakeFilesHttpURI,FakePortalsHttpURI,CloudFlareZoneID,CloudFlareApiToken".ToList();

		static Organization Normalize(this Organization organization, ExpandoObject data, Action<Organization> onCompleted = null)
		{
			organization.Instructions = Settings.Instruction.Parse(data.Get<ExpandoObject>("Instructions"));
			organization.Alias = organization.Alias?.NormalizeAlias(false);
			organization.Theme = string.IsNullOrWhiteSpace(organization.Theme) ? "default" : organization.Theme;
			try
			{
				organization.ExamineURLs = data.Get<List<ExpandoObject>>("ExamineURLs")?.Select(examineURLs => examineURLs.Copy<ExamineURLs>()).Where(examineURLs => examineURLs != null).ToList();
			}
			catch
			{
				organization.ExamineURLs = null;
			}
			onCompleted?.Invoke(organization);
			return organization;
		}

		public static Organization CreateOrganization(this ExpandoObject data, string excluded = null, Action<Organization> onCompleted = null)
			=> Organization.CreateInstance(data, excluded, null).Normalize(data, onCompleted);

		public static Organization Update(this Organization organization, ExpandoObject data, string excluded = null, Action<Organization> onCompleted = null)
			=> organization.Fill(data, excluded, null).Normalize(data, onCompleted);

		internal static Organization Set(this Organization organization, bool clear = false, bool updateCache = false, string oldAlias = null)
		{
			if (organization == null)
				return null;

			var id = organization.ID;
			var title = organization.Title;

			if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(title))
				return organization;

			if (clear)
				organization.Remove();

			if (updateCache)
				Utility.Cache.SetAsync(organization).Execute();

			var alias = organization.Alias;
			OrganizationProcessor.Organizations[id] = organization;

			if (!string.IsNullOrEmpty(alias))
			{
				var key = alias.GetOrganiztionAliasKey();
				OrganizationProcessor.OrganizationsByAlias[key] = organization;
				Utility.NotRecognizedAliases.Remove(key);
			}

			if (!string.IsNullOrEmpty(oldAlias) && !oldAlias.IsEquals(alias))
			{
				var oldKey = oldAlias.GetOrganiztionAliasKey();
				OrganizationProcessor.OrganizationsByAlias.Remove(oldKey);
				Utility.NotRecognizedAliases.Remove(oldKey);
			}

			return organization;
		}

		internal static async Task<Organization> SetAsync(this Organization organization, bool clear = false, bool updateCache = false, CancellationToken cancellationToken = default, string oldAlias = null)
		{
			organization?.Set(clear, false, oldAlias);
			if (updateCache && organization != null)
			{
				var id = organization.ID;
				var title = organization.Title;
				if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(title))
					await Utility.Cache.SetAsync(organization, cancellationToken).ConfigureAwait(false);
			}
			return organization;
		}

		internal static Organization Remove(this Organization organization)
			=> (organization?.ID ?? "").RemoveOrganization();

		internal static Organization RemoveOrganization(this string id)
		{
			if (string.IsNullOrWhiteSpace(id) || !OrganizationProcessor.Organizations.TryRemove(id, out var organization) || organization == null)
				return null;

			var key = organization.Alias.GetOrganiztionAliasKey();
			OrganizationProcessor.OrganizationsByAlias.Remove(key);
			Utility.NotRecognizedAliases.Remove(key);

			return organization;
		}

		internal static async Task<Organization> ReloadAsync(this Organization organization, CancellationToken cancellationToken, bool set = true, bool updateCache = false, string oldAlias = null)
		{
			organization._siteIDs = organization._moduleIDs = null;
			await Task.WhenAll
			(
				organization.FindSitesAsync(cancellationToken, false),
				organization.FindModulesAsync(cancellationToken, false)
			).ConfigureAwait(false);
			return set ? organization.Set(true, updateCache, oldAlias) : organization;
		}

		internal static async Task<Organization> RefreshAsync(this Organization organization, CancellationToken cancellationToken, bool reloadContentTypes = true, bool updateCache = true, bool sendCommunicatingMessage = true, bool sendUpdatingMessage = false)
		{
			// reload organization
			await Utility.Cache.RemoveAsync(organization, cancellationToken).ConfigureAwait(false);
			organization = await organization.Remove().ID.GetOrganizationByIDAsync(cancellationToken, true).ConfigureAwait(false);

			// reload sites & modules
			if (organization._siteIDs == null || organization._moduleIDs == null)
				await organization.ReloadAsync(cancellationToken, false).ConfigureAwait(false);

			// reload content-types
			var modules = (organization.Modules ?? []).Where(module => module != null).ToList();
			if (reloadContentTypes)
				modules.ForEach(module => module._contentTypeIDs = null);
			await modules.ForEachAsync(module => module._contentTypeIDs == null ? module.FindContentTypesAsync(cancellationToken) : Task.CompletedTask, true, false).ConfigureAwait(false);

			// update cache
			await organization.SetAsync(false, updateCache, cancellationToken).ConfigureAwait(false);

			// send messages
			var json = sendCommunicatingMessage || sendUpdatingMessage ? organization.ToJson() : null;
			if (sendCommunicatingMessage)
				new CommunicateMessage(Utility.ServiceName)
				{
					Type = $"{organization.GetObjectName()}#Update",
					Data = json,
					ExcludedNodeID = Utility.NodeID
				}.Send();
			if (sendUpdatingMessage)
				new UpdateMessage
				{
					Type = $"{Utility.ServiceName}#{organization.GetObjectName()}#Update",
					Data = json,
					DeviceID = "*"
				}.Send();

			return organization;
		}

		internal static async Task<(List<string> LinkURLs, List<string> CategoryURLs, List<string> ContentURLs)> GetRefreshingURLsAsync(
			this Organization organization, 
			bool getLinks, 
			IEnumerable<Link> links, 
			bool getCategories, 
			bool getContents, 
			IEnumerable<Category> categories, 
			int maxCategoryPageNumber = 0, 
			int maxContentPageNumber = 0, 
			DateTime? minPublishedTime = null, 
			Func<Link, Task> onProcessLinkAsync = null, 
			Func<Category, Task> onProcessCategoryAsync = null, 
			Func<Category, ContentType, int, int, Task> onProcessContentsAsync = null, 
			string correlationID = null)
		{
			var linkURLs = new List<string>();
			var categoryURLs = new List<string>();
			var contentURLs = new List<string>();
			var organizationURL = organization.URL;
			correlationID ??= UtilityService.NewUUID;

			async Task getLinkURLsAsync(Link link)
			{
				await Task.WhenAll
				(
					onProcessLinkAsync == null ? Task.CompletedTask : onProcessLinkAsync(link),
					Utility.Cache.AddSetMemberAsync(link.ContentType.ObjectCacheKeys, link.GetCacheKey(), Utility.CancellationToken)
				).ConfigureAwait(false);

				var url = link.GetURL();
				if (url.IsStartsWith("~/") || url.IsStartsWith(organizationURL))
					linkURLs.Add(url);

				var children = await link.FindChildrenAsync(Utility.CancellationToken).ConfigureAwait(false) ?? [];
				if (children.Count > 0)
					await children.Where(child => child.Status == ApprovalStatus.Published).ForEachAsync(child => getLinkURLsAsync(child)).ConfigureAwait(false);
			}

			async Task getContentURLsAsync(Category category, IEnumerable<ContentType> contentTypes)
			{
				await Task.WhenAll
				(
					onProcessCategoryAsync == null ? Task.CompletedTask : onProcessCategoryAsync(category),
					Utility.Cache.AddSetMemberAsync(category.ContentType.ObjectCacheKeys, category.GetCacheKey(), Utility.CancellationToken)
				).ConfigureAwait(false);

				var categoryURL = category.GetURL(true);
				if (categoryURL.IsStartsWith("~/") || categoryURL.IsStartsWith(organizationURL))
				{
					if (getCategories)
						categoryURLs.Add(categoryURL.Replace("/{{pageNumber}}", "", StringComparison.OrdinalIgnoreCase));

					if (categoryURL.IsContains("/{{pageNumber}}"))
					{
						if (getCategories)
							categoryURLs = categoryURLs.Concat(categoryURL.GetPaginatingURLs(maxCategoryPageNumber).Skip(1)).ToList();

						if (getContents)
							await contentTypes.ForEachAsync(async contentType =>
							{
								var filter = ContentProcessor.GetContentsFilter(contentType.SystemID, contentType.RepositoryID, contentType.ID, category.ID);
								var sort = Sorts<Content>.Descending("StartDate").ThenByDescending("PublishedTime");

								var (totalRecords, cacheKeyOfTotal) = await ContentProcessor.CountAsync(filter, sort, contentType.ID, Utility.CancellationToken).ConfigureAwait(false);
								await Utility.Cache.AddSetMemberAsync(contentType.GetSetCacheKey(), cacheKeyOfTotal, Utility.CancellationToken).ConfigureAwait(false);
								
								if (totalRecords > 0)
								{
									var pageNumber = 0;
									var pageSize = 20;
									var totalPages = (totalRecords, pageSize).GetTotalPages();

									while (pageNumber < totalPages && (maxContentPageNumber > 0 ? pageNumber < maxContentPageNumber : true))
									{
										pageNumber++;
										var (contents, _, _) = await ContentProcessor.SearchAsync(filter, sort, pageSize, pageNumber, contentType.ID, totalRecords, Utility.CancellationToken).ConfigureAwait(false);

										await Task.WhenAll
										(
											onProcessContentsAsync == null ? Task.CompletedTask : onProcessContentsAsync(category, contentType, totalPages, pageNumber),
											Utility.Cache.AddSetMembersAsync(contentType.GetSetCacheKey(), contents.Select(content => new[] { content.GetCacheKey(), content.GetCacheKeyOfAliasedContent() }).SelectMany(keys => keys).Concat([Extensions.GetCacheKey(filter, sort, pageSize, pageNumber)]), Utility.CancellationToken)
										).ConfigureAwait(false);

										contents = contents.Where(content => content.Status == ApprovalStatus.Published).ToList();
										if (contents.Count > 0)
										{
											contentURLs.AddRange(contents.Where(content => minPublishedTime != null ? minPublishedTime.Value > content.PublishedTime.Value : true).Select(content => content.GetURL()));
											if (minPublishedTime != null && minPublishedTime.Value > contents.Last().PublishedTime.Value)
												break;
										}
									}
								}
							}, true, false).ConfigureAwait(false);
					}
				}

				var children = await category.FindChildrenAsync(Utility.CancellationToken).ConfigureAwait(false) ?? [];
				if (children.Count > 0)
					await children.Where(child => child.Status == ApprovalStatus.Published).ForEachAsync(child => getContentURLsAsync(child, contentTypes), true, false).ConfigureAwait(false);
			}

			if (getLinks)
			{
				if (links != null)
					try
					{
						await links.Where(link => link.Status == ApprovalStatus.Published).ForEachAsync(link => getLinkURLsAsync(link)).ConfigureAwait(false);
					}
					catch (Exception ex)
					{
						await Utility.WriteLogAsync(correlationID, $"Error occurred while preparing URL of links => {ex.Message}\r\nStack: {ex.StackTrace}", "Caches").ConfigureAwait(false);
					}
				else
					await organization.ContentTypesOfLink.ForEachAsync(async contentType =>
					{
						try
						{
							var filter = LinkProcessor.GetLinksFilter(contentType.SystemID, contentType.RepositoryID, contentType.ID);
							var sort = Sorts<Link>.Ascending("OrderIndex").ThenByAscending("Title");
							links = await Link.FindAsync(filter, sort, 0, 1, contentType.ID, true, Extensions.GetCacheKey(filter, sort, 0, 1), 0, Utility.CancellationToken).ConfigureAwait(false);
							await links.Where(link => link.Status == ApprovalStatus.Published).ForEachAsync(link => getLinkURLsAsync(link)).ConfigureAwait(false);
						}
						catch (Exception ex)
						{
							await Utility.WriteLogAsync(correlationID, $"Error occurred while preparing URL of links => {ex.Message}\r\nStack: {ex.StackTrace}", "Caches").ConfigureAwait(false);
						}
					}).ConfigureAwait(false);
			}

			if (getCategories || getContents)
			{
				if (categories != null)
					try
					{
						await categories.Where(category => category.Status == ApprovalStatus.Published).ForEachAsync(category => getContentURLsAsync(category, organization.ContentTypesOfContent), true, false).ConfigureAwait(false);
					}
					catch (Exception ex)
					{
						await Utility.WriteLogAsync(correlationID, $"Error occurred while preparing URL of categories/contents => {ex.Message}\r\nStack: {ex.StackTrace}", "Caches").ConfigureAwait(false);
					}
				else
					await organization.ContentTypesOfCategory.ForEachAsync(async contentType =>
					{
						try
						{
							var filter = CategoryProcessor.GetCategoriesFilter(contentType.SystemID, contentType.RepositoryID, contentType.ID);
							var sort = Sorts<Category>.Ascending("OrderIndex").ThenByAscending("Title");
							categories = await Category.FindAsync(filter, sort, 0, 1, contentType.ID, true, Extensions.GetCacheKey(filter, sort, 0, 1), 0, Utility.CancellationToken).ConfigureAwait(false);
							await categories.ForEachAsync(async category =>
							{
								var cat = (category?.ID ?? "").GetCategoryByID(false, false);
								if (cat != null)
									await cat.FindChildrenAsync(Utility.CancellationToken).ConfigureAwait(false);
								else if (category?.Set() != null)
								{
									await category.FindChildrenAsync(Utility.CancellationToken).ConfigureAwait(false);
									new CommunicateMessage(Utility.ServiceName)
									{
										Type = $"{category.GetObjectName()}#Update",
										Data = category.ToJson(true, false),
										ExcludedNodeID = Utility.NodeID
									}.Send();
								}
							}, true, false).ConfigureAwait(false);
							await categories.Select(category => category.ID.GetCategoryByID(false, false))
								.Where(category => category != null && category.Status == ApprovalStatus.Published)
								.ForEachAsync(category => getContentURLsAsync(category, organization.ContentTypesOfContent), true, false).ConfigureAwait(false);
						}
						catch (Exception ex)
						{
							await Utility.WriteLogAsync(correlationID, $"Error occurred while preparing URL of categories/contents => {ex.Message}\r\nStack: {ex.StackTrace}", "Caches").ConfigureAwait(false);
						}
					}).ConfigureAwait(false);
			}

			return (linkURLs, categoryURLs, contentURLs);
		}

		internal static async Task<List<string>> GetRefreshingURLsAsync(this Organization organization, IEnumerable<string> addresses, bool onlyDetailsOfCategories = false)
		{
			var refreshingURLs = onlyDetailsOfCategories ? [] : new[] { "~/rss" }.ToList();
			var links = new List<Link>();
			var categories = new List<Category>();
			var suffix = organization.AlwaysUseHtmlSuffix ? ".html" : "";

			addresses ??= organization.RefreshURLs?.Addresses ?? [];
			await addresses.Select(address => address.Replace("\r", "").ToArray("\n"))
				.SelectMany(address => address)
				.Where(address => onlyDetailsOfCategories ? address.IsStartsWith("@category:") || address.IsStartsWith("@category(") : true)
				.ForEachAsync(async address =>
				{
					if (address.IsStartsWith("@desktop:") || address.IsStartsWith("@desktop("))
					{
						var id = address.Replace(StringComparison.OrdinalIgnoreCase, "@desktop:", "").Replace(StringComparison.OrdinalIgnoreCase, "@desktop(", "").Replace(")", "").Trim();
						var desktop = await id.GetDesktopByIDAsync(Utility.CancellationToken).ConfigureAwait(false);
						if (desktop != null)
							refreshingURLs.Add($"~/{desktop.Alias}{(desktop.Organization != null && desktop.Organization.AlwaysUseHtmlSuffix ? ".html" : "")}");
					}
					else if (address.IsStartsWith("@link:") || address.IsStartsWith("@link("))
					{
						var id = address.Replace(StringComparison.OrdinalIgnoreCase, "@link:", "").Replace(StringComparison.OrdinalIgnoreCase, "@link(", "").Replace(")", "").Trim();
						links.Add(await Link.GetAsync(id, Utility.CancellationToken).ConfigureAwait(false));
					}
					else if (address.IsStartsWith("@category:") || address.IsStartsWith("@category("))
					{
						var id = address.Replace(StringComparison.OrdinalIgnoreCase, "@category:", "").Replace(StringComparison.OrdinalIgnoreCase, "@category(", "").Replace(")", "").Trim();
						categories.Add(await id.GetCategoryByIDAsync(Utility.CancellationToken).ConfigureAwait(false));
					}
					else
					{
						if (address.IsContains("/{{pageNumber}}"))
							refreshingURLs.AddRange(address.GetPaginatingURLs(Utility.RefreshMaxPage));
						else
							refreshingURLs.Add(address);
					}
				}, true, false).ConfigureAwait(false);

			links = links.Where(link => link != null && link.ID.IsValidUUID()).ToList();
			categories = categories.Where(category => category != null && category.ID.IsValidUUID()).ToList();
			var (linkURLs, categoryURLs, contentURLs) = await organization.GetRefreshingURLsAsync(true, links, true, true, categories, Utility.RefreshMaxPage, 2).ConfigureAwait(false);
			return refreshingURLs.Concat(linkURLs).Concat(categoryURLs).Concat(contentURLs)
				.Where(url => url != null).Select(url => url.IsEquals("~/default.aspx") || url.IsEquals("~/index.html") ? "~/" : url)
				.Where(url => url != "~/").Distinct(StringComparer.OrdinalIgnoreCase).ToList();
		}

		internal static Task<List<string>> GetRefreshingURLsAsync(this Organization organization, bool onlyDetailsOfCategories = false)
			=> organization.GetRefreshingURLsAsync(null, onlyDetailsOfCategories);

		internal static IEnumerable<string> GetRefreshingURLs(this Organization organization)
		{
			var orgURLs = new[] { "~/", organization.HomeDesktop?.GetURL(), organization.SearchDesktop?.GetURL() };
			var siteURLs = (organization.Sites ?? []).Where(site => !site.ID.IsEquals(organization.DefaultSite?.ID) && (site.Status == ApprovalStatus.Published || site.Status == ApprovalStatus.Approved))
				.Select(site => new[] { $"{site.GetURL()}/{(organization.AlwaysUseHtmlSuffix ? "index.html" : "")}", site.HomeDesktop?.GetURL(), site.SearchDesktop?.GetURL() })
				.SelectMany(url => url);
			return orgURLs.Concat(siteURLs).Where(url => url != null).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
		}

		internal static async Task<List<SchedulingTask>> GetRefreshingTasksAsync(this Organization organization, bool others = true, List<string> otherURLs = null)
		{
			var refreshURLs = organization.GetRefreshingURLs().ToList();
			var schedulingTasks = new[] { new SchedulingTask(3)
			{
				ID = $"{organization.ID}:URLs:Home".GenerateUUID(),
				SystemID = organization.ID,
				Title = "Refresh home URLs",
				SchedulingType = SchedulingType.Refresh,
				Data = refreshURLs.ToJArray().ToString(Formatting.None),
				Persistance = false
			}}.ToList();
			if (others)
			{
				refreshURLs = otherURLs ?? await organization.GetRefreshingURLsAsync().ConfigureAwait(false) ?? [];
				if (refreshURLs.Count > 0)
					schedulingTasks.Add(new SchedulingTask(organization.RefreshURLs != null && organization.RefreshURLs.Interval > 0 ? organization.RefreshURLs.Interval : 30)
					{
						ID = $"{organization.ID}:URLs:Other".GenerateUUID(),
						SystemID = organization.ID,
						Title = "Refresh all pre-defined URLs",
						SchedulingType = SchedulingType.Refresh,
						Data = refreshURLs.ToJArray().ToString(Formatting.None),
						Persistance = false
					});
			}
			return schedulingTasks;
		}

		internal static async Task SendRefreshingTasksAsync(this Organization organization, bool isDeleted = false, bool sendOtherURLs = true)
		{
			var sendDeleteMessage = (string type) => new SchedulingTask
			{
				ID = $"{organization.ID}:URLs:{type}".GenerateUUID(),
				SystemID = organization.ID,
				Persistance = false
			}.SendMessages("Delete");

			if (isDeleted)
				new[] { "Home", "Other" }.ForEach(type => sendDeleteMessage(type));

			else
			{
				var otherURLs = await organization.GetRefreshingURLsAsync().ConfigureAwait(false);
				if (sendOtherURLs && !otherURLs.Any())
					sendDeleteMessage("Other");
				var otherTasks = await organization.GetRefreshingTasksAsync(sendOtherURLs, otherURLs).ConfigureAwait(false);
				otherTasks.ForEach(schedulingTask => schedulingTask.SendMessages());
			}
		}

		internal static async Task<List<SchedulingTask>> GetSchedulingTasksAsync(this Organization organization, CancellationToken cancellationToken, bool reload = true, bool sendUpdatingMessages = true)
		{
			if (organization.Status != ApprovalStatus.Approved && organization.Status != ApprovalStatus.Published)
				return new List<SchedulingTask>();

			var schedulingTasks = reload ? null : SchedulingTaskProcessor.SchedulingTasks.Where(kvp => organization.ID.IsEquals(kvp.Value.OrganizationID)).Select(kvp => kvp.Value).OrderBy(schedulingTask => schedulingTask.Time).ToList();
			if (reload || schedulingTasks.Count < 1)
			{
				var filter = Filters<SchedulingTask>.And(Filters<SchedulingTask>.Equals("SystemID", organization.ID));
				await Utility.Cache.RemoveAsync(Extensions.GetRelatedCacheKeys(filter, Sorts<SchedulingTask>.Ascending("Time")), cancellationToken).ConfigureAwait(false);
				schedulingTasks = await organization.GetRefreshingTasksAsync().ConfigureAwait(false);
				schedulingTasks = schedulingTasks.Concat(await SchedulingTaskProcessor.SearchAsync(filter, cancellationToken).ConfigureAwait(false) ?? []).OrderBy(schedulingTask => schedulingTask.Time).ToList();
				schedulingTasks.ForEach(schedulingTask => SchedulingTaskProcessor.SchedulingTasks[schedulingTask.ID] = schedulingTask);
			}

			if (sendUpdatingMessages)
				schedulingTasks.ForEach(schedulingTask => schedulingTask.SendMessages("Update", null, Utility.NodeID));

			return schedulingTasks;
		}

		public static Organization GetOrganizationByID(this string id, bool force = false, bool fetchRepository = true)
		{
			if (string.IsNullOrWhiteSpace(id))
				return null;

			if (!force && OrganizationProcessor.Organizations.TryGetValue(id, out var organization) && organization != null)
			{
				var roles = organization.OriginalPrivileges?.AdministrativeRoles;
				if (roles != null && roles.Count > 0)
					return organization;
			}

			return fetchRepository ? Organization.Get(id, !Utility.IsCacheDisabled)?.Set() : null;
		}

		public static async Task<Organization> GetOrganizationByIDAsync(this string id, CancellationToken cancellationToken = default, bool force = false)
		{
			var organization = (id ?? "").GetOrganizationByID(force, false);
			var roles = organization?.OriginalPrivileges?.AdministrativeRoles;
			if (roles == null || roles.Count < 1)
				organization = (await Organization.GetAsync(id, !Utility.IsCacheDisabled, cancellationToken).ConfigureAwait(false))?.Set();
			return organization;
		}

		public static Organization GetOrganizationByAlias(this string alias, bool fetchRepository = true)
		{
			if (string.IsNullOrWhiteSpace(alias))
				return null;

			var key = alias.GetOrganiztionAliasKey();
			if (Utility.NotRecognizedAliases.Contains(key))
				return null;

			if (!OrganizationProcessor.OrganizationsByAlias.TryGetValue(key, out var organization) || organization == null)
			{
				if (!fetchRepository)
					return null;

				organization = Organization.Get(Filters<Organization>.Equals("Alias", alias), null, null)?.Set();

				if (organization == null)
				{
					Utility.NotRecognizedAliases.Add(key);
					return null;
				}

				new CommunicateMessage(Utility.ServiceName)
				{
					Type = organization.GetObjectName() + "#Update",
					Data = organization.ToJson(),
					ExcludedNodeID = Utility.NodeID
				}.Send();
			}

			var roles = organization.OriginalPrivileges?.AdministrativeRoles;
			if (roles == null || roles.Count < 1)
				organization = Organization.Get(organization.ID, !Utility.IsCacheDisabled)?.Set();

			return organization;
		}

		public static async Task<Organization> GetOrganizationByAliasAsync(this string alias, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(alias))
				return null;

			var key = alias.GetOrganiztionAliasKey();
			if (Utility.NotRecognizedAliases.Contains(key))
				return null;

			var organization = alias.GetOrganizationByAlias(false);
			if (organization == null)
			{
				organization = (await Organization.GetAsync(Filters<Organization>.Equals("Alias", alias), null, null, cancellationToken).ConfigureAwait(false))?.Set();

				if (organization == null)
				{
					Utility.NotRecognizedAliases.Add(key);
					return null;
				}

				new CommunicateMessage(Utility.ServiceName)
				{
					Type = organization.GetObjectName() + "#Update",
					Data = organization.ToJson(),
					ExcludedNodeID = Utility.NodeID
				}.Send();
			}

			var roles = organization.OriginalPrivileges?.AdministrativeRoles;
			if (roles == null || roles.Count < 1)
				organization = (await Organization.GetAsync(organization.ID, !Utility.IsCacheDisabled, cancellationToken).ConfigureAwait(false))?.Set();

			return organization;
		}

		internal static async Task ProcessInterCommunicateMessageOfOrganizationAsync(this CommunicateMessage message, CancellationToken cancellationToken = default)
		{
			if (message.Type.IsEndsWith("#Create") || message.Type.IsEndsWith("#Update"))
			{
				var oldAlias = message.Type.IsEndsWith("#Update") ? message.Data.Get("ID", "").GetOrganizationByID(false, false)?.Alias : null;
				await message.Data.ToExpandoObject().CreateOrganization().ReloadAsync(cancellationToken, true, false, oldAlias).ConfigureAwait(false);
			}
			else if (message.Type.IsEndsWith("#Delete"))
				message.Data.ToExpandoObject().CreateOrganization().Remove();
		}

		internal static async Task ClearRelatedCacheAsync(this Organization organization, CancellationToken cancellationToken, string correlationID = null, bool clearDataCache = true, bool clearHtmlCache = true, bool doRefresh = true)
		{
			// data cache keys
			var dataCacheKeys = clearDataCache
				? Extensions.GetRelatedCacheKeys(Filters<Organization>.And(), Sorts<Organization>.Ascending("Title"))
					.Concat(Extensions.GetRelatedCacheKeys(Filters<Organization>.And(Filters<Organization>.Equals("OwnerID", organization.OwnerID)), Sorts<Organization>.Ascending("Title")))
					.ToList()
				: [];

			// html cache keys (desktop HTMLs and related resources)
			var htmlCacheKeys = (clearHtmlCache ? organization.GetDesktopCacheKeys() : []).Concat(await organization.GetSetCacheKeysAsync(cancellationToken).ConfigureAwait(false)).ToList();

			// remove related cache & refresh
			await Task.WhenAll
			(
				Utility.Cache.RemoveAsync(htmlCacheKeys.Concat(dataCacheKeys).Distinct(StringComparer.OrdinalIgnoreCase).ToList(), cancellationToken),
				Utility.IsCacheLogEnabled
					? Utility.WriteLogAsync(correlationID, $"Clear related cache of an organization [{organization.Title} - ID: {organization.ID}]\r\n- {dataCacheKeys.Distinct(StringComparer.OrdinalIgnoreCase).Count()} data-keys => {dataCacheKeys.Distinct(StringComparer.OrdinalIgnoreCase).Join(", ")}\r\n- {htmlCacheKeys.Distinct(StringComparer.OrdinalIgnoreCase).Count()} html-keys => {htmlCacheKeys.Distinct(StringComparer.OrdinalIgnoreCase).Join(", ")}", "Caches")
					: Task.CompletedTask
			).ConfigureAwait(false);
			if (doRefresh && (organization.ExamineURLs == null || organization.ExamineURLs.Count < 1))
				await organization.RefreshWebPagesAsync([organization.URL, $"{organization.URL}/favicon.ico", $"{organization.FakePortalsHttpURI ?? Utility.PortalsHttpURI}/_js/o_{organization.ID}.js", $"{Utility.PortalsHttpURI}/_js/o_{organization.ID}.js"], true, correlationID, $"Refresh when clear related cache of an organization [{organization.Title} - ID: {organization.ID}]", cancellationToken).ConfigureAwait(false);
		}

		internal static async Task ClearCacheAsync(this Organization organization, CancellationToken cancellationToken, string correlationID = null, bool clearObjectsCache = true, bool clearRelatedDataCache = true, bool clearRelatedHtmlCache = true, bool doRefresh = true)
		{
			// clear cache of home desktop (html)
			var tasks = new List<Task>
			{
				organization.ClearRelatedCacheAsync(cancellationToken, correlationID, clearRelatedDataCache, clearRelatedHtmlCache, false)
			};

			// clear cache of objects
			if (clearObjectsCache)
			{
				// clear cache of expressions
				var expressions = await Expression.FindAsync(Filters<Expression>.And(Filters<Expression>.Equals("SystemID", organization.ID)), null, 0, 1, null, cancellationToken).ConfigureAwait(false);
				tasks.Append(expressions.Select(expression => expression.ClearCacheAsync(cancellationToken, correlationID, clearRelatedDataCache, clearRelatedHtmlCache, doRefresh)));

				// clear cache of roles
				var roles = await Role.FindAsync(Filters<Role>.And(Filters<Role>.Equals("SystemID", organization.ID)), null, 0, 1, null, cancellationToken).ConfigureAwait(false);
				tasks.Append(roles.Select(role => role.ClearCacheAsync(cancellationToken, correlationID, clearRelatedDataCache)));

				// clear cache of modules, content-types and business objects
				tasks.Append(organization.Modules.Select(module => module.ClearCacheAsync(cancellationToken, correlationID, clearObjectsCache, clearRelatedDataCache, clearRelatedHtmlCache, doRefresh)));

				// clear cache of desktops
				var desktops = await Desktop.FindAsync(Filters<Desktop>.And(Filters<Desktop>.Equals("SystemID", organization.ID)), null, 0, 1, null, cancellationToken).ConfigureAwait(false);
				tasks.Append(desktops.Select(desktop => desktop.ClearCacheAsync(cancellationToken, correlationID, clearRelatedDataCache, clearRelatedHtmlCache, false, doRefresh)));

				// clear cache of sites
				tasks.Append(organization.Sites.Select(site => site.ClearCacheAsync(cancellationToken, correlationID, clearRelatedDataCache, clearRelatedHtmlCache, doRefresh)));
			}

			// clear cache of the organization
			tasks.Append(new[]
			{
				Utility.Cache.RemoveAsync(organization.Remove(), cancellationToken),
				Utility.IsCacheLogEnabled ? Utility.WriteLogAsync(correlationID, $"Clear cache of an organization [{organization.Title} - ID: {organization.ID}]", "Caches") : Task.CompletedTask,
				new CommunicateMessage(Utility.ServiceName)
				{
					Type = $"{organization.GetObjectName()}#Delete",
					Data = organization.ToJson(),
					ExcludedNodeID = Utility.NodeID
				}.SendAsync()
			});

			await Task.WhenAll(tasks).ConfigureAwait(false);

			// re-load organization & sites/modules/content-types
			organization = await organization.ID.GetOrganizationByIDAsync(cancellationToken).ConfigureAwait(false);
			await Task.WhenAll
			(
				organization.FindSitesAsync(cancellationToken, false),
				organization.FindModulesAsync(cancellationToken, false)
			).ConfigureAwait(false);
			await organization.Modules.ForEachAsync(async module =>
			{
				await module.FindContentTypesAsync(cancellationToken, false).ConfigureAwait(false);
				await Task.WhenAll
				(
					Utility.IsCacheLogEnabled ? Utility.WriteLogAsync(correlationID, $"The organization was reloaded when all cache were clean\r\n{module.ToJson()}", "Caches") : Task.CompletedTask,
					Utility.IsCacheLogEnabled ? Utility.WriteLogAsync(correlationID, $"The content-types were reloaded when all cache were clean\r\n{module.ContentTypes.Select(contentType => contentType.ToJson().ToString(Formatting.Indented)).Join("\r\n")}", "Caches") : Task.CompletedTask
				).ConfigureAwait(false);
			}, true, false).ConfigureAwait(false);
			await Task.WhenAll
			(
				organization.SetAsync(false, true, cancellationToken),
				Task.WhenAll(organization.Sites.Select(site => site.SetAsync(false, true, cancellationToken))),
				Task.WhenAll(organization.Modules.Select(module => module.SetAsync(true, cancellationToken))),
				Task.WhenAll(organization.Modules.Select(module => Task.WhenAll(module.ContentTypes.Select(contentType => contentType.SetAsync(true, cancellationToken)))))
			).ConfigureAwait(false);

			// re-load and refresh the home desktop
			var homedesktop = await Desktop.GetAsync(organization.HomeDesktopID, cancellationToken).ConfigureAwait(false);
			await Task.WhenAll
			(
				homedesktop.FindChildrenAsync(cancellationToken, false),
				homedesktop.FindPortletsAsync(cancellationToken, false)
			).ConfigureAwait(false);
			await homedesktop.SetAsync(false, true, cancellationToken).ConfigureAwait(false);

			if (doRefresh && (organization.ExamineURLs == null || organization.ExamineURLs.Count < 1))
				await organization.RefreshWebPagesAsync([organization.URL], true, correlationID, $"Refresh the home desktop when clear related cache of an organization [{organization.Title} - ID: {organization.ID}]", cancellationToken).ConfigureAwait(false);
		}

		internal static async Task<JObject> SearchOrganizationsAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, CancellationToken cancellationToken = default)
		{
			// check permissions
			var asFetch = requestInfo.TryGetParameter("x-fetch", out var asxFetch) && ("vieapps-ngx".IsEquals(asxFetch) || "ngx-apps".IsEquals(asxFetch));
			if (!isSystemAdministrator && !asFetch)
				throw new AccessDeniedException();

			// prepare
			var request = requestInfo.GetRequestExpando();

			var query = request.Get<string>("FilterBy.Query");

			var filter = request.Get<ExpandoObject>("FilterBy")?.ToFilterBy<Organization>() ?? Filters<Organization>.And();
			var sort = string.IsNullOrWhiteSpace(query) ? request.Get<ExpandoObject>("SortBy")?.ToSortBy<Organization>() ?? Sorts<Organization>.Ascending("Title") : null;

			var (totalRecords, totalPages, pageSize, pageNumber) = request.Get<ExpandoObject>("Pagination")?.GetPagination() ?? (-1, 0, 20, 1);

			// process cache
			var cacheKey = Extensions.GetCacheKeyOfObjectsJson(filter, sort, pageSize, pageNumber);
			var json = string.IsNullOrWhiteSpace(query) && !asFetch && !Utility.IsCacheDisabled ? await Utility.Cache.GetAsync<string>(cacheKey, cancellationToken).ConfigureAwait(false) : null;
			if (!string.IsNullOrWhiteSpace(json))
				return JObject.Parse(json);

			// prepare pagination
			if (totalRecords < 0)
				totalRecords = string.IsNullOrWhiteSpace(query)
					? await Organization.CountAsync(filter, !Utility.IsCacheDisabled, Extensions.GetCacheKeyOfTotalObjects(filter, sort), cancellationToken).ConfigureAwait(false)
					: await Organization.CountAsync(query, filter, cancellationToken).ConfigureAwait(false);

			totalPages = (totalRecords, pageSize).GetTotalPages();
			if (totalPages > 0 && pageNumber > totalPages)
				pageNumber = totalPages;

			// search
			var objects = totalRecords > 0
				? string.IsNullOrWhiteSpace(query)
					? await Organization.FindAsync(filter, sort, pageSize, pageNumber, !Utility.IsCacheDisabled, Extensions.GetCacheKey(filter, sort, pageSize, pageNumber), cancellationToken).ConfigureAwait(false)
					: await Organization.SearchAsync(query, filter, null, pageSize, pageNumber, cancellationToken).ConfigureAwait(false)
				: [];

			// build result
			var response = new JObject
			{
				{ "FilterBy", filter.ToClientJson(query) },
				{ "SortBy", sort?.ToClientJson() },
				{ "Pagination", (totalRecords, totalPages, pageSize, pageNumber).GetPagination() },
				{ "Objects", asFetch
					? objects.Where(@object => @object != null).Select(@object => new JObject
					{
						{ "ID", @object.ID },
						{ "Alias", @object.Alias },
						{ "Title", @object.Title }
					}).ToJArray()
					: objects.Where(@object => @object != null).ToList().ToJsonArray()
				}
			};

			// update cache
			if (string.IsNullOrWhiteSpace(query) && !asFetch)
				Utility.Cache.SetAsync(cacheKey, response.ToString(Formatting.None), Utility.CancellationToken).Execute();

			// response
			return response;
		}

		internal static async Task<JObject> CreateOrganizationAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, CancellationToken cancellationToken = default)
		{
			// check permission
			var isCreatedByOtherService = requestInfo.Extra != null && requestInfo.Extra.TryGetValue("x-create", out var xcreate) && xcreate.IsEquals(requestInfo.Session.SessionID.Encrypt());
			if (!isSystemAdministrator && !isCreatedByOtherService)
				throw new AccessDeniedException();

			// check the exising the the alias
			var request = requestInfo.GetBodyExpando();
			var alias = request.Get<string>("Alias");
			if (!string.IsNullOrWhiteSpace(alias))
			{
				if (OrganizationProcessor.ExcludedAliases.Contains(alias.NormalizeAlias(false)))
					throw new AliasIsExistedException($"The alias ({alias.NormalizeAlias(false)}) is used by another organization");

				var existing = await alias.NormalizeAlias(false).GetOrganizationByAliasAsync(cancellationToken).ConfigureAwait(false);
				if (existing != null)
					throw new AliasIsExistedException($"The alias ({alias.NormalizeAlias(false)}) is used by another organization");
			}

			// gathering information
			var organization = request.CreateOrganization("Status,Instructions,Privileges,OriginalPrivileges,Created,CreatedID,LastModified,LastModifiedID,McpSettings", obj =>
			{
				obj.ID = string.IsNullOrWhiteSpace(obj.ID) || !obj.ID.IsValidUUID() ? UtilityService.NewUUID : obj.ID;
				obj.Alias = string.IsNullOrWhiteSpace(obj.Alias) ? $"{obj.Title}{UtilityService.GetRandomNumber()}".NormalizeAlias(false) : obj.Alias;
				obj.OwnerID = string.IsNullOrWhiteSpace(obj.OwnerID) || !obj.OwnerID.IsValidUUID() ? requestInfo.Session.User.ID : obj.OwnerID;
				obj.Status = isSystemAdministrator
					? request.Get("Status", "Pending").TryToEnum(out ApprovalStatus statusByAdmin) ? statusByAdmin : ApprovalStatus.Pending
					: isCreatedByOtherService
						? requestInfo.Extra.TryGetValue("x-status", out var xstatus) && xstatus.TryToEnum(out ApprovalStatus statusByOtherService) ? statusByOtherService : ApprovalStatus.Pending
						: ApprovalStatus.Pending;
				obj.OriginalPrivileges = (isSystemAdministrator ? request.Get<Privileges>("OriginalPrivileges") : null) ?? new Privileges(true);
				obj.Created = obj.LastModified = DateTime.Now;
				obj.CreatedID = obj.LastModifiedID = requestInfo.Session.User.ID;
				try
				{
					obj.McpSettings = request.Get<string>("McpSettings")?.ToJson().As<McpSettings>(true);
				}
				catch { }
				obj.NormalizeExtras();
			});
			organization.Notifications?.WebHooks?.Validate(requestInfo, organization);
			organization.WebHookSettings?.Validate(requestInfo, organization);

			// create new
			await Organization.CreateAsync(organization, cancellationToken).ConfigureAwait(false);

			// update cache
			await organization.Set().ClearRelatedCacheAsync(cancellationToken, requestInfo.CorrelationID).ConfigureAwait(false);

			// send update messages
			var response = organization.ToJson();
			var objectName = organization.GetObjectName();
			new UpdateMessage
			{
				Type = $"{requestInfo.ServiceName}#{objectName}#Create",
				Data = response,
				DeviceID = "*"
			}.Send();
			new CommunicateMessage(requestInfo.ServiceName)
			{
				Type = $"{objectName}#Create",
				Data = response,
				ExcludedNodeID = Utility.NodeID
			}.Send();

			// send notification & update scheduling tasks
			await Task.WhenAll
			(
				organization.SendNotificationAsync("Create", organization.Notifications, ApprovalStatus.Draft, organization.Status, requestInfo, cancellationToken),
				organization.SendRefreshingTasksAsync()
			).ConfigureAwait(false);

			// tell HTTP servers to update MCP settings
			if (organization.McpSettings != null)
				new CommunicateMessage("APIGateway")
				{
					Type = "McpServer#UpdateInfo",
					Data = organization.McpSettings.ToJSON(json =>
					{
						json["ServiceName"] = Utility.ServiceName;
						json["SystemID"] = organization.ID;
					})
				}.Send();

			// response
			return response;
		}

		internal static async Task<JObject> GetOrganizationAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, CancellationToken cancellationToken = default)
		{
			// get the organization
			var isForceCache = requestInfo.IsForceCache();
			var identity = requestInfo.GetObjectIdentity(true, true) ?? "";
			var organization = await (identity.IsValidUUID() ? identity.GetOrganizationByIDAsync(cancellationToken, isForceCache) : identity.GetOrganizationByAliasAsync(cancellationToken)).ConfigureAwait(false) ?? throw new InformationNotFoundException();

			// check permission
			var gotRights = isSystemAdministrator || requestInfo.Session.User.IsViewer(null, null, organization);
			if (!gotRights)
				throw new AccessDeniedException();

			if (!identity.IsValidUUID() || requestInfo.ContainsKey("x-brief"))
				return new JObject
				{
					{ "ID", organization.ID },
					{ "Title", organization.Title },
					{ "Alias", organization.Alias }
				};

			// refresh (clear cache and reload) or get sites & modules/content-types
			var isRefresh = requestInfo.Session.User.IsAuthenticated && (isForceCache || "refresh".IsEquals(requestInfo.GetObjectIdentity()) || organization._siteIDs == null || organization._moduleIDs == null);
			organization = isRefresh
				? await organization.RefreshAsync(cancellationToken).ConfigureAwait(false)
				: organization;

			// response
			var response = organization.ToJson(true, false).UpdateVersions(await organization.FindVersionsAsync(cancellationToken, false).ConfigureAwait(false));
			if (requestInfo.ContainsKey("x-sites"))
				response["Sites"] = organization.Sites.ToJsonArray();

			new UpdateMessage
			{
				Type = $"{requestInfo.ServiceName}#{organization.GetObjectName()}#Update",
				Data = response,
				DeviceID = "*",
				ExcludedDeviceID = isRefresh ? "" : requestInfo.Session.DeviceID
			}.Send();

			if (isRefresh)
				await organization.GetSchedulingTasksAsync(cancellationToken).ConfigureAwait(false);

			else
			{
				var filter = Filters<Role>.And(Filters<Role>.Equals("SystemID", organization.ID), Filters<Role>.IsNull("ParentID"));
				var sort = Sorts<Role>.Ascending("Title");
				(await Role.FindAsync(filter, sort, 20, 1, !Utility.IsCacheDisabled, Extensions.GetCacheKey(filter, sort, 20, 1), cancellationToken).ConfigureAwait(false) ?? []).ForEach(role => new UpdateMessage
				{
					Type = $"{requestInfo.ServiceName}#{role.GetObjectName()}#Update",
					Data = role.ToJson(true, false),
					DeviceID = "*"
				}.Send());
				if (!requestInfo.Session.User.IsModerator(null, null, organization))
					new UpdateMessage
					{
						Type = $"{requestInfo.ServiceName}#{organization.DefaultSite?.GetObjectName() ?? "Site"}#Update",
						Data = organization.DefaultSite?.ToJson(),
						DeviceID = "*"
					}.Send();
			}

			if (requestInfo.IsWriteDebugLogs())
				await requestInfo.WriteLogAsync($"An organization was fetched => {organization.Title} [{isSystemAdministrator} / {isRefresh || isForceCache}]\r\n- JSON: {response}").ConfigureAwait(false);

			return response;
		}

		internal static async Task<JObject> UpdateAsync(this Organization organization, RequestInfo requestInfo, ApprovalStatus oldStatus, CancellationToken cancellationToken, bool clearObjectsCache = false, string oldAlias = null, string @event = null)
		{
			// update
			await Organization.UpdateAsync(organization, requestInfo.Session.User.ID, cancellationToken).ConfigureAwait(false);

			// clear cache
			await organization.ClearCacheAsync(cancellationToken, requestInfo.CorrelationID, clearObjectsCache, true, false, false).ConfigureAwait(false);

			// send update messages
			await organization.SetAsync(!organization.Alias.IsEquals(oldAlias), true, cancellationToken, oldAlias).ConfigureAwait(false);
			var versions = await organization.FindVersionsAsync(cancellationToken, false).ConfigureAwait(false);
			var response = organization.ToJson();
			var objectName = organization.GetObjectName();
			new UpdateMessage
			{
				Type = $"{requestInfo.ServiceName}#{objectName}#Update",
				Data = response.UpdateVersions(versions),
				DeviceID = "*"
			}.Send();
			new CommunicateMessage(requestInfo.ServiceName)
			{
				Type = $"{objectName}#Update",
				Data = response,
				ExcludedNodeID = Utility.NodeID
			}.Send();

			// send notification & update scheduling tasks
			await Task.WhenAll
			(
				organization.SendNotificationAsync(@event ?? "Update", organization.Notifications, oldStatus, organization.Status, requestInfo, cancellationToken),
				organization.SendRefreshingTasksAsync()
			).ConfigureAwait(false);

			// tell HTTP servers to update MCP settings
			if (organization.McpSettings != null)
				new CommunicateMessage("APIGateway")
				{
					Type = "McpServer#UpdateInfo",
					Data = organization.McpSettings.ToJSON(json =>
					{
						json["ServiceName"] = Utility.ServiceName;
						json["SystemID"] = organization.ID;
					})
				}.Send();

			// response
			return response;
		}

		internal static async Task<JObject> UpdateOrganizationAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, CancellationToken cancellationToken = default)
		{
			// get the organization
			var organization = await (requestInfo.GetObjectIdentity() ?? "").GetOrganizationByIDAsync(cancellationToken).ConfigureAwait(false);
			if (organization == null)
				throw new InformationNotFoundException();

			// check permission
			var gotRights = isSystemAdministrator || requestInfo.Session.User.IsAdministrator(null, null, organization);
			if (!gotRights)
				throw new AccessDeniedException();

			// check the exising the the alias
			var request = requestInfo.GetBodyExpando();
			var oldAlias = organization.Alias;
			var oldStatus = organization.Status;
			var alias = request.Get<string>("Alias");

			if (!string.IsNullOrWhiteSpace(alias) && !oldAlias.IsEquals(alias))
			{
				if (OrganizationProcessor.ExcludedAliases.Contains(alias.NormalizeAlias(false)))
					throw new AliasIsExistedException($"The alias ({alias.NormalizeAlias(false)}) is used by another organization");

				var existing = await alias.NormalizeAlias(false).GetOrganizationByAliasAsync(cancellationToken).ConfigureAwait(false);
				if (existing != null && !existing.ID.IsEquals(organization.ID))
					throw new AliasIsExistedException($"The alias ({alias.NormalizeAlias(false)}) is used by another organization");
			}

			// gathering information
			var privileges = organization.OriginalPrivileges?.Copy();
			organization.Update(request, "ID,OwnerID,HomeDesktopID,SearchDesktopID,Status,Instructions,Privileges,Created,CreatedID,LastModified,LastModifiedID,McpSettings", _ =>
			{
				OrganizationProcessor.MustUpdatedProperties.ForEach(name => organization.SetProperty(name, request.Get(name)));
				organization.OwnerID = isSystemAdministrator ? request.Get("OwnerID", organization.OwnerID) : organization.OwnerID;
				organization.Status = isSystemAdministrator ? request.Get("Status", organization.Status.ToString()).ToEnum<ApprovalStatus>() : organization.Status;
				organization.Alias = string.IsNullOrWhiteSpace(organization.Alias) ? oldAlias : organization.Alias;
				organization.LastModified = DateTime.Now;
				organization.LastModifiedID = requestInfo.Session.User.ID;
				organization.OriginalPrivileges = organization.OriginalPrivileges ?? new Privileges(true);
				try
				{
					organization.McpSettings = request.Get<string>("McpSettings")?.ToJson().As<McpSettings>(true);
				}
				catch { }
				organization.NormalizeExtras();
			}).Remove();
			organization.Notifications?.WebHooks?.Validate(requestInfo, organization);
			organization.WebHookSettings?.Validate(requestInfo, organization);

			// update
			var privilegesWereChanged = !organization.OriginalPrivileges.IsEquals(privileges);
			var response = await organization.UpdateAsync(requestInfo, oldStatus, cancellationToken, privilegesWereChanged, oldAlias).ConfigureAwait(false);

			// broadcast update when the privileges were changed
			// ...

			return response;
		}

		internal static async Task<JObject> DeleteOrganizationAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, Func<RequestInfo, CancellationToken, Task> serviceCaller = null, Action<RequestInfo, string, Exception> onServiceCallerGotError = null, CancellationToken cancellationToken = default)
		{
			// prepare
			var organization = await (requestInfo.GetObjectIdentity() ?? "").GetOrganizationByIDAsync(cancellationToken).ConfigureAwait(false);
			if (organization == null)
				throw new InformationNotFoundException();

			// check permission
			if (!isSystemAdministrator && !UtilityService.GetAppSetting("Portals:Phrase", "know the ways").IsEquals(requestInfo.GetParameter("x-phrase")))
				throw new AccessDeniedException();

			// delete
			organization.DeleteAsync(requestInfo, serviceCaller, onServiceCallerGotError, Utility.CancellationToken).Execute(ex => Utility.WriteErrorAsync(ex, $"Error occurred while deleting an organization => {ex.Message}", "Trash", requestInfo.CorrelationID), 1234);

			// response
			return organization.ToJson();
		}

		internal static async Task DeleteAsync(this Organization organization, RequestInfo requestInfo, Func<RequestInfo, CancellationToken, Task> serviceCaller, Action<RequestInfo, string, Exception> onServiceCallerGotError, CancellationToken cancellationToken)
		{
			// prepare
			var stopwatch = Stopwatch.StartNew();
			var updateCache = "true".IsEquals(requestInfo.GetParameter("x-update-cache"));
			var sendUpdatingMessages = !"false".IsEquals(requestInfo.GetParameter("x-send-updating-messages"));
			await requestInfo.WriteLogAsync($"Prepare to move an organization ({organization.Title}) into trash", "Trash").ConfigureAwait(false);

			// delete all content-types & all belong contents
			var contentTypes = await organization.FetchAsync<ContentType>(cancellationToken).ConfigureAwait(false);
			while (contentTypes.Count > 0)
			{
				await contentTypes.ForEachAsync(contentType => contentType.DeleteAsync(requestInfo, true, updateCache, sendUpdatingMessages, cancellationToken), true, false).ConfigureAwait(false);
				contentTypes = await organization.FetchAsync<ContentType>(cancellationToken).ConfigureAwait(false);
			}

			// delete all modules
			var modules = await organization.FetchAsync<Module>(cancellationToken).ConfigureAwait(false);
			while (modules.Count > 0)
			{
				await modules.ForEachAsync(module => module.DeleteAsync(requestInfo, updateCache, sendUpdatingMessages, cancellationToken), true, false).ConfigureAwait(false);
				modules = await organization.FetchAsync<Module>(cancellationToken).ConfigureAwait(false);
			}

			// delete all expressions
			var expressions = await organization.FetchAsync<Expression>(cancellationToken).ConfigureAwait(false);
			while (expressions.Count > 0)
			{
				await expressions.ForEachAsync(expression => expression.DeleteAsync(requestInfo, updateCache, sendUpdatingMessages, cancellationToken), true, false).ConfigureAwait(false);
				expressions = await organization.FetchAsync<Expression>(cancellationToken).ConfigureAwait(false);
			}

			// delete all desktops
			IFilterBy filter = Filters<Desktop>.And(organization.GetFilter<Desktop>(), Filters<Desktop>.IsNull("ParentID"));
			var desktops = await organization.FetchAsync(cancellationToken, filter as IFilterBy<Desktop>).ConfigureAwait(false);
			while (desktops.Count > 0)
			{
				await desktops.ForEachAsync(desktop => desktop.DeleteAsync(requestInfo, true, updateCache, sendUpdatingMessages, cancellationToken), true, false).ConfigureAwait(false);
				desktops = await organization.FetchAsync(cancellationToken, filter as IFilterBy<Desktop>).ConfigureAwait(false);
			}

			// delete all sites
			var sites = await organization.FetchAsync<Site>(cancellationToken).ConfigureAwait(false);
			while (sites.Count > 0)
			{
				await sites.ForEachAsync(site => site.DeleteAsync(requestInfo, updateCache, sendUpdatingMessages, cancellationToken), true, false).ConfigureAwait(false);
				sites = await organization.FetchAsync<Site>(cancellationToken).ConfigureAwait(false);
			}

			// delete all tasks
			var schedulingTasks = await organization.FetchAsync<SchedulingTask>(cancellationToken).ConfigureAwait(false);
			while (schedulingTasks.Count > 0)
			{
				await schedulingTasks.ForEachAsync(schedulingTask => schedulingTask.DeleteAsync(requestInfo, updateCache, sendUpdatingMessages, cancellationToken), true, false).ConfigureAwait(false);
				schedulingTasks = await organization.FetchAsync<SchedulingTask>(cancellationToken).ConfigureAwait(false);
			}

			// delete all roles
			filter = Filters<Role>.And(organization.GetFilter<Role>(), Filters<Role>.IsNull("ParentID"));
			var roles = await organization.FetchAsync(cancellationToken, filter as IFilterBy<Role>).ConfigureAwait(false);
			while (roles.Count > 0)
			{
				await roles.ForEachAsync(role => role.DeleteAsync(requestInfo, serviceCaller, onServiceCallerGotError, true, updateCache, sendUpdatingMessages, cancellationToken), true, false).ConfigureAwait(false);
				roles = await organization.FetchAsync(cancellationToken, filter as IFilterBy<Role>).ConfigureAwait(false);
			}

			// delete organization
			await Organization.DeleteAsync(organization.ID, requestInfo.Session.User.ID, cancellationToken).ConfigureAwait(false);
			await organization.SendNotificationAsync("Delete", organization.Notifications, organization.Status, organization.Status, requestInfo, cancellationToken).ConfigureAwait(false);

			var cacheKeys = new[] {
				organization.GetCacheKey(),
				Extensions.GetCacheKeyOfTotalObjects(Filters<Organization>.And(), Sorts<Organization>.Ascending("Title"))
			}.ToList();
			for (var page = 1; page < 100; page++)
				cacheKeys.AddRange(Extensions.GetCacheKey(Filters<Organization>.And(), Sorts<Organization>.Ascending("Title"), 20, page), Extensions.GetCacheKeyOfObjectsJson(Filters<Organization>.And(), Sorts<Organization>.Ascending("Title"), 20, page));
			await Utility.Cache.RemoveAsync(cacheKeys, cancellationToken).ConfigureAwait(false);
			organization.Remove();

			if (sendUpdatingMessages)
			{
				var objectName = organization.GetObjectName();
				var messageData = organization.ToJson();
				new UpdateMessage
				{
					Type = $"{requestInfo.ServiceName}#{objectName}#Delete",
					Data = messageData,
					DeviceID = "*"
				}.Send();
				new CommunicateMessage(requestInfo.ServiceName)
				{
					Type = $"{objectName}#Delete",
					Data = messageData,
					ExcludedNodeID = Utility.NodeID
				}.Send();
			}
			stopwatch.Stop();
			await requestInfo.WriteLogAsync($"The organization ({organization.Title}) has been moved into trash - Execution times: {stopwatch.GetElapsedTimes()}", "Trash").ConfigureAwait(false);
		}

		static IFilterBy<T> GetFilter<T>(this Organization organization) where T : class
			=> Filters<T>.Equals("SystemID", organization.ID);

		static Task<List<T>> FetchAsync<T>(this Organization organization, CancellationToken cancellationToken, IFilterBy<T> filter = null) where T : class
			=> RepositoryMediator.FindAsync("", filter ?? organization.GetFilter<T>(), null, 100, 1, null, false, null, 0, cancellationToken);

		internal static async Task<JObject> SyncOrganizationAsync(this RequestInfo requestInfo, CancellationToken cancellationToken, bool sendNotifications = false, bool dontCreateNewVersion = false)
		{
			var @event = requestInfo.GetParameter("event") ?? requestInfo.GetParameter("x-original-event");
			if (string.IsNullOrWhiteSpace(@event) || !@event.IsEquals("Delete"))
				@event = "Update";

			var data = requestInfo.GetBodyExpando();
			var organization = await data.Get<string>("ID").GetOrganizationByIDAsync(cancellationToken).ConfigureAwait(false);
			var oldStatus = organization != null ? organization.Status : ApprovalStatus.Pending;

			if (!@event.IsEquals("Delete"))
			{
				if (organization == null)
				{
					organization = Organization.CreateInstance(data);
					organization.NormalizeExtras();
					organization.Extras = data.Get<string>("Extras") ?? organization.Extras;
					await Organization.CreateAsync(organization, cancellationToken).ConfigureAwait(false);
				}
				else
				{
					organization.Fill(data);
					organization.NormalizeExtras();
					organization.Extras = data.Get<string>("Extras") ?? organization.Extras;
					await Organization.UpdateAsync(organization, dontCreateNewVersion, cancellationToken).ConfigureAwait(false);
				}
			}
			else if (organization != null)
				await Organization.DeleteAsync(organization.ID, organization.LastModifiedID, cancellationToken).ConfigureAwait(false);

			// stop if has no info
			if (organization == null)
				return new JObject();

			// clear related cache
			if (requestInfo.GetHeaderParameter("x-converter") == null || @event.IsEquals("Delete"))
				await organization.ClearCacheAsync(cancellationToken, requestInfo.CorrelationID).ConfigureAwait(false);
			else
				await organization.ClearRelatedCacheAsync(cancellationToken, requestInfo.CorrelationID).ConfigureAwait(false);

			// send notifications & update scheduling tasks
			await Task.WhenAll
			(
				sendNotifications ? organization.SendNotificationAsync(@event, organization.Notifications, oldStatus, organization.Status, requestInfo, cancellationToken) : Task.CompletedTask,
				organization.SendRefreshingTasksAsync(@event.IsEquals("Delete"))
			).ConfigureAwait(false);

			// send update messages
			var json = @event.IsEquals("Delete")
				? organization.Remove().ToJson()
				: organization.Set().ToJson();
			var objectName = organization.GetObjectName();
			new UpdateMessage
			{
				Type = $"{requestInfo.ServiceName}#{objectName}#{@event}",
				Data = json,
				DeviceID = "*"
			}.Send();
			new CommunicateMessage(requestInfo.ServiceName)
			{
				Type = $"{objectName}#{@event}",
				Data = json,
				ExcludedNodeID = Utility.NodeID
			}.Send();
			return json;
		}

		internal static async Task<JObject> RollbackOrganizationAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, CancellationToken cancellationToken = default)
		{
			// get the organization
			var organization = await (requestInfo.GetObjectIdentity() ?? "").GetOrganizationByIDAsync(cancellationToken).ConfigureAwait(false);
			if (organization == null)
				throw new InformationNotFoundException();

			// check permission
			var gotRights = isSystemAdministrator || requestInfo.Session.User.IsAdministrator(null, null, organization);
			if (!gotRights)
				throw new AccessDeniedException();

			// rollback
			var oldStatus = organization.Status;
			var oldAlias = organization.Alias;
			organization = await RepositoryMediator.RollbackAsync<Organization>(requestInfo.GetParameter("x-version-id") ?? "", requestInfo.Session.User.ID, cancellationToken).ConfigureAwait(false);
			
			await Task.WhenAll
			(
				organization.ClearRelatedCacheAsync(cancellationToken, requestInfo.CorrelationID),
				organization.SendNotificationAsync("Rollback", organization.Notifications, oldStatus, organization.Status, requestInfo, cancellationToken),
				organization.SendRefreshingTasksAsync()
			).ConfigureAwait(false);

			// send update messages
			var versions = await organization.FindVersionsAsync(cancellationToken, false).ConfigureAwait(false);
			var response = organization.Set(true, true, oldAlias).ToJson(true, false);
			var objectName = organization.GetObjectName();
			new UpdateMessage
			{
				Type = $"{requestInfo.ServiceName}#{objectName}#Update",
				Data = response.UpdateVersions(versions),
				DeviceID = "*"
			}.Send();
			new CommunicateMessage(requestInfo.ServiceName)
			{
				Type = $"{objectName}#Update",
				Data = response,
				ExcludedNodeID = Utility.NodeID
			}.Send();
			return response;
		}

		internal static async Task<JObject> RebuildCacheAsync(this RequestInfo requestInfo)
		{
			var organizations = (await Organization.FindAllAsync(false, Utility.CancellationToken).ConfigureAwait(false)).Where(organization => organization.Status == ApprovalStatus.Approved || organization.Status == ApprovalStatus.Published).ToList();
			await Utility.WriteLogAsync(requestInfo.CorrelationID, $"Start to rebuild cache of all organizations ({organizations.Count()})", "Caches").ConfigureAwait(false);
			organizations.ForEach(organization => Router.GetService(Utility.ServiceName).ProcessRequestAsync(new RequestInfo(requestInfo.Session, Utility.ServiceName, "Cache")
			{
				Header = new Dictionary<string, string>(requestInfo.Header)
				{
					["x-rebuild"] = organization.ID,
					["x-max-page"] = Int32.TryParse(requestInfo.GetParameter("x-max-page"), out var maxPage) && maxPage > 0 ? maxPage.ToString() : null,
					["x-min-time"] = DateTime.TryParse(requestInfo.GetParameter("x-min-time"), out var minTime) ? minTime.ToIsoString() : null
				},
				CorrelationID = requestInfo.CorrelationID
			}).Execute());
			return new JObject { ["CorrelationID"] = requestInfo.CorrelationID };
		}

		internal static Task<JObject> RebuildCacheAsync(this RequestInfo requestInfo, Organization organization, CancellationToken cancellationToken)
		{
			if (organization != null)
			{
				if (!Int32.TryParse(requestInfo.GetParameter("x-done"), out var done) || done < 0)
					done = 0;
				if (!Int32.TryParse(requestInfo.GetParameter("x-max-page"), out var maxPage) || maxPage < 0)
					maxPage = Utility.RefreshMaxPage;
				if (!DateTime.TryParse(requestInfo.GetParameter("x-min-time"), out var minTime))
					minTime = Utility.RefreshMinTime;
				organization.RebuildCacheAsync(done, maxPage, minTime, requestInfo.CorrelationID, requestInfo.ContainsKey("x-logs"), cancellationToken).Execute();
			}
			return Task.FromResult(new JObject { ["CorrelationID"] = requestInfo.CorrelationID });
		}

		internal static async Task RebuildCacheAsync(this Organization organization, int done, int maxPage, DateTime minTime, string correlationID, bool writeLogs, CancellationToken cancellationToken)
		{
			if (organization == null || (organization.Status != ApprovalStatus.Approved && organization.Status != ApprovalStatus.Published))
				return;

			await Task.Delay(UtilityService.GetRandomNumber(456, 789), cancellationToken).ConfigureAwait(false);
			var stopwatch = Stopwatch.StartNew();

			var refreshingURLs = organization.GetRefreshingURLs().ToList();

			void sendStatus(string state)
				=> new CommunicateMessage($"{Utility.ServiceName}.cache.rebuild")
				{
					Type = organization.ID,
					Data = new JObject
					{
						["Title"] = organization.Title,
						["Node"] = ServiceBase.ServiceComponent.NodeID,
						["Time"] = DateTime.Now.ToUnixTimestamp(),
						["State"] = state,
						["Total"] = refreshingURLs.Count,
						["Done"] = done
					}
				}.Send();

			sendStatus("Started");
			await Utility.WriteLogAsync(correlationID, $"Start to rebuild caches of '{organization.Title}'\r\n- Number of Links' content-types: {organization.ContentTypesOfLink.Count}\r\n- Number of Categorys' content-types: {organization.ContentTypesOfCategory.Count}\r\n- Number of Contents' content-types: {organization.ContentTypesOfContent.Count}", "Caches").ConfigureAwait(false);

			var (linkURLs, categoryURLs, contentURLs) = await organization.GetRefreshingURLsAsync(
				true, null, true, true, null, maxPage, 0, minTime,
				link => writeLogs ? Utility.WriteLogAsync(correlationID, $"Get URLs of link '{link.FullTitle}' [{organization.Title}]", "Caches") : Task.CompletedTask,
				category => writeLogs ? Utility.WriteLogAsync(correlationID, $"Get URLs of category '{category.FullTitle}' [{organization.Title}]", "Caches") : Task.CompletedTask,
				(category, contentType, totalPages, pageNumber) => writeLogs ? Utility.WriteLogAsync(correlationID, $"Get content URLs of '{category.FullTitle}' [{contentType.Title} @ {organization.Title}] - Total pages: {totalPages:###,##0}", "Caches") : Task.CompletedTask,
				correlationID
			).ConfigureAwait(false);

			var domains = new HashSet<string>((organization.Sites ?? []).Select(site => site.Host));
			var rootURL = (string.IsNullOrWhiteSpace(organization.CloudFlareZoneID) || string.IsNullOrWhiteSpace(organization.CloudFlareApiToken)
				? organization.URL
				: (organization.DefaultSite?.GetURL() ?? organization.URL)) + "/";
			var headers = new Dictionary<string, string>
			{
				["x-force-cache"] = "1",
				["x-no-purge"] = "1",
				["x-requester"] = "vieapps-ngx-portals",
				["x-original-correlation-id"] = correlationID
			};
			refreshingURLs = refreshingURLs.Concat(linkURLs).Concat(categoryURLs).Concat(contentURLs)
				.Where(url => url.IsStartsWith("~/") || domains.Contains(new Uri(url).Host))
				.Select(url => url.Replace("~/", rootURL))
				.ToList();
			await Utility.WriteLogAsync(correlationID, $"{refreshingURLs.Count:###,###,##0} caching URLs of '{organization.Title}' were prepared to rebuild cache\r\n- Link URLs: {linkURLs.Count:###,###,##0}\r\n- Category URLs: {categoryURLs.Count:###,###,##0}\r\n- Content URLs: {contentURLs.Count:###,###,##0}", "Caches").ConfigureAwait(false);
			sendStatus("Prepared");

			while (!cancellationToken.IsCancellationRequested)
			{
				if (cancellationToken.IsCancellationRequested)
				{
					if (writeLogs)
						await Utility.WriteLogAsync(correlationID, $"Got signal to cancel the rebuild process [{organization.Title}]", "Caches").ConfigureAwait(false);
					break;
				}

				var urls = refreshingURLs.Skip(done).Take(10).ToList();
				if (urls.Count < 1)
					break;

				await urls.ForEachAsync((url, index, cancellationtoken) => url.RefreshWebPageAsync(headers, index / 2, correlationID, false, cancellationtoken), cancellationToken, true, false).ConfigureAwait(false);

				if (!cancellationToken.IsCancellationRequested)
				{
					done += urls.Count;
					if (done % 50 == 0)
						sendStatus("Processing");
				}

				if ((writeLogs && done % 50 == 0) || (done % 100 == 0))
					await Utility.WriteLogAsync(correlationID, $"{done:###,###,##0}/{refreshingURLs.Count:###,###,##0} caching URLs of '{organization.Title}' were refreshen", "Caches").ConfigureAwait(false);
			}

			stopwatch.Stop();
			await Utility.WriteLogAsync(correlationID, $"Complete rebuild {done:###,###,##0} caches of '{organization.Title}' - Execution times: {stopwatch.GetElapsedTimes()}", "Caches").ConfigureAwait(false);
			sendStatus(cancellationToken.IsCancellationRequested ? "Canceled" : "Completed");
		}
	}
}
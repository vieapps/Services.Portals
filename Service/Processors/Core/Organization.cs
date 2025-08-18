#region Related components
using System;
using System.Linq;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Dynamic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using net.vieapps.Components.Utility;
using net.vieapps.Components.Repository;
using net.vieapps.Components.Security;
using net.vieapps.Services.Portals.Exceptions;
using net.vieapps.Services.Portals.Settings;
#endregion

namespace net.vieapps.Services.Portals
{
	public static class OrganizationProcessor
	{
		internal static ConcurrentDictionary<string, Organization> Organizations { get; } = new ConcurrentDictionary<string, Organization>(StringComparer.OrdinalIgnoreCase);

		internal static ConcurrentDictionary<string, Organization> OrganizationsByAlias { get; } = new ConcurrentDictionary<string, Organization>(StringComparer.OrdinalIgnoreCase);

		internal static HashSet<string> ExcludedAliases { get; } = (UtilityService.GetAppSetting("Portals:ExcludedAliases", "") + ",APIs,Portals,CMS,CRM,Dashboard,Dashboards,Temp,Feed,Feeds,Atom,Rss").ToLower().ToHashSet();

		internal static HashSet<string> ExtraProperties { get; } = "Notifications,Instructions,Socials,Trackings,MetaTags,ScriptLibraries,Scripts,AlwaysUseHtmlSuffix,RefreshURLs,RedirectURLs,ExamineURLs,EmailSettings,WebHookSettings,HttpIndicators,FakeFilesHttpURI,FakePortalsHttpURI".ToHashSet();

		internal static List<string> MustUpdatedProperties { get; } = "HomeDesktopID,SearchDesktopID,MetaTags,Stylesheets,ScriptLibraries,Scripts,FakeFilesHttpURI,FakePortalsHttpURI".ToList();

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
			if (organization != null && !string.IsNullOrWhiteSpace(organization.ID) && !string.IsNullOrWhiteSpace(organization.Title))
			{
				if (clear)
					organization.Remove();

				if (updateCache)
					Utility.Cache.SetAsync(organization).Run();

				OrganizationProcessor.Organizations[organization.ID] = organization;
				OrganizationProcessor.OrganizationsByAlias[organization.Alias] = organization;
				Utility.NotRecognizedAliases.TryRemove($"Organization:{organization.Alias}");
				if (!string.IsNullOrWhiteSpace(oldAlias) && !oldAlias.IsEquals(organization.Alias) && OrganizationProcessor.OrganizationsByAlias.Remove(oldAlias))
					Utility.NotRecognizedAliases.TryRemove($"Organization:{oldAlias}");
			}
			return organization;
		}

		internal static async Task<Organization> SetAsync(this Organization organization, bool clear = false, bool updateCache = false, CancellationToken cancellationToken = default, string oldAlias = null)
		{
			organization?.Set(clear, false, oldAlias);
			await (updateCache && organization != null && !string.IsNullOrWhiteSpace(organization.ID) && !string.IsNullOrWhiteSpace(organization.Title) ? Utility.Cache.SetAsync(organization, cancellationToken) : Task.CompletedTask).ConfigureAwait(false);
			return organization;
		}

		internal static Organization Remove(this Organization organization)
			=> (organization?.ID ?? "").RemoveOrganization();

		internal static Organization RemoveOrganization(this string id)
		{
			if (string.IsNullOrWhiteSpace(id) || !OrganizationProcessor.Organizations.TryRemove(id, out var organization) || organization == null)
				return null;

			OrganizationProcessor.OrganizationsByAlias.Remove(organization.Alias);
			Utility.NotRecognizedAliases.TryRemove($"Organization:{organization.Alias}");

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
			await modules.ForEachAsync(async module => await (module._contentTypeIDs == null ? module.FindContentTypesAsync(cancellationToken) : Task.CompletedTask).ConfigureAwait(false), true, false).ConfigureAwait(false);

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

		internal static List<string> GetRefreshingURLs(this Organization organization, IEnumerable<string> addresses, bool onlyDetailsOfCategories = false)
			=> (addresses ?? organization.RefreshURLs?.Addresses ?? []).Select(address =>
			{
				var urls = onlyDetailsOfCategories ? [] : new[] { "~/rss" }.ToList();
				address.Replace("\r", "").ToArray("\n")
				.Where(url => onlyDetailsOfCategories ? url.IsStartsWith("@category:") || url.IsStartsWith("@category(") : true)
				.ForEach(url =>
				{
					if (url.IsStartsWith("@desktop:") || url.IsStartsWith("@desktop("))
					{
						var desktops = new[] { url.Replace(StringComparison.OrdinalIgnoreCase, "@desktop:", "").Replace(StringComparison.OrdinalIgnoreCase, "@desktop(", "").Replace(")", "").Trim().GetDesktopByID() }.ToList();
						desktops.Concat(desktops.FirstOrDefault()?.Children ?? []).Where(desktop => desktop != null).ToList().ForEach(desktop => urls.Add($"~/{desktop.Alias}{(desktop.Organization != null && desktop.Organization.AlwaysUseHtmlSuffix ? ".html" : "")}"));
					}
					else if (url.IsStartsWith("@link:") || url.IsStartsWith("@link("))
					{
						var links = new[] { Link.Get<Link>(url.Replace(StringComparison.OrdinalIgnoreCase, "@link:", "").Replace(StringComparison.OrdinalIgnoreCase, "@link(", "").Replace(")", "").Trim()) }.ToList();
						links.Concat(links.FirstOrDefault()?.Children ?? []).Where(link => link != null).ToList().ForEach(link => urls.Add(link.GetURL()));
					}
					else if (url.IsStartsWith("@category:") || url.IsStartsWith("@category("))
					{
						var categories = new[] { url.Replace(StringComparison.OrdinalIgnoreCase, "@category:", "").Replace(StringComparison.OrdinalIgnoreCase, "@category(", "").Replace(")", "").Trim().GetCategoryByID() }.ToList();
						categories = categories.Concat(categories.FirstOrDefault()?.Children ?? []).Where(category => category != null).ToList();
						var contentTypes = onlyDetailsOfCategories ? categories.FirstOrDefault()?.Module?.ContentTypesOfContent ?? [] : [];
						categories.ForEach(category =>
						{
							if (onlyDetailsOfCategories)
								contentTypes.ForEach(contentType =>
								{
									var filter = Filters<Content>.And
									(
										Filters<Content>.Equals("SystemID", contentType.SystemID),
										Filters<Content>.Equals("RepositoryID", contentType.RepositoryID),
										Filters<Content>.Equals("RepositoryEntityID", contentType.ID),
										Filters<Content>.Equals("CategoryID", category.ID)
									);
									var sort = Sorts<Content>.Descending("StartDate").ThenByDescending("PublishedTime");
									var contents = Content.Find(filter, sort, 20, 1, contentType.ID, true, Extensions.GetCacheKey(filter, sort, 20, 1), 0) ?? [];
									urls.AddRange(contents.Where(content => content.Status.Equals(ApprovalStatus.Published)).Select(content => content.GetURL()));
								});
							else
							{
								url = category.GetURL(null, true);
								if (url.IsContains("/{{pageNumber}}"))
									for (var page = 1; page <= 10; page++)
										urls.Add(url.Replace(StringComparison.OrdinalIgnoreCase, "/{{pageNumber}}", page > 1 ? $"/{page}" : ""));
								else
									urls.Add(url);
							}
						});
					}
					else if (url.IsContains("/{{pageNumber}}"))
						for (var page = 1; page <= 10; page++)
							urls.Add(url.Replace(StringComparison.OrdinalIgnoreCase, "/{{pageNumber}}", page > 1 ? $"/{page}" : ""));
					else
						urls.Add(url);
				});
				return urls;
			})
			.SelectMany(urls => urls)
			.Where(url => !string.IsNullOrWhiteSpace(url) && !url.IsEquals("#"))
			.Select(url => url.IsEquals("~/default.aspx") || url.IsEquals("~/index.html") ? "~/" : url)
			.Where(url => !url.IsEquals("~/"))
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToList();

		internal static List<string> GetRefreshingURLs(this Organization organization, bool onlyDetailsOfCategories = false)
			=> organization.GetRefreshingURLs(null, onlyDetailsOfCategories);

		internal static List<SchedulingTask> GetRefreshingTasks(this Organization organization, bool others = true)
		{
			var refreshURLs = new[] { "~/" }.Concat((organization.Sites ?? []).Where(site => !site.ID.IsEquals(organization.DefaultSite?.ID)).Select(site => $"{site.GetURL()}/{(organization.AlwaysUseHtmlSuffix ? "index.html" : "")}")).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

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
				refreshURLs = organization.GetRefreshingURLs();
				if (refreshURLs.Count > 0)
					schedulingTasks.Add(new SchedulingTask(organization.RefreshURLs.Interval > 0 ? organization.RefreshURLs.Interval : 7)
					{
						ID = $"{organization.ID}:URLs:Other".GenerateUUID(),
						SystemID = organization.ID,
						Title = "Refresh all pre-defined URLs",
						SchedulingType = SchedulingType.Refresh,
						Data = refreshURLs.ToJArray().ToString(Formatting.None),
						Persistance = false
					});

				schedulingTasks.Add(new SchedulingTask(12, RecurringType.Hours, DateTime.Parse($"{DateTime.Now.AddDays(DateTime.Now.Hour < 13 ? 0 : 1):yyyy/MM/dd} {(DateTime.Now.Hour < 13 ? 13 : 1):00}:{UtilityService.GetRandomNumber(0, 30):00}:00"))
				{
					ID = $"{organization.ID}:URLs:Force".GenerateUUID(),
					SystemID = organization.ID,
					Title = "Force refresh all pre-defined URLs",
					SchedulingType = SchedulingType.Refresh,
					Data = (schedulingTasks.First().DataAsJson as JArray).Select(value => value as JValue).Select(value => value.ToString()).Concat(refreshURLs).Distinct(StringComparer.OrdinalIgnoreCase).Select(url => $"{url}{(url.IndexOf("?") > 0 ? "&" : "?")}x-force-cache").ToJArray().ToString(Formatting.None),
					Persistance = false
				});
			}

			return schedulingTasks;
		}

		internal static void SendRefreshingTasks(this Organization organization, bool isDeleted = false, bool sendOtherURLs = true)
		{
			var sendDeleteMessage = (string type) => new SchedulingTask
			{
				ID = $"{organization.ID}:URLs:{type}".GenerateUUID(),
				SystemID = organization.ID,
				Persistance = false
			}.SendMessages("Delete");

			if (isDeleted)
				new[] { "Home", "Other", "Force" }.ForEach(type => sendDeleteMessage(type));

			else
			{
				if (sendOtherURLs && !organization.GetRefreshingURLs().Any())
					sendDeleteMessage("Other");
				organization.GetRefreshingTasks(sendOtherURLs).ForEach(schedulingTask => schedulingTask.SendMessages());
			}
		}

		internal static async Task<List<SchedulingTask>> GetSchedulingTasksAsync(this Organization organization, CancellationToken cancellationToken, bool reload = true)
		{
			var schedulingTasks = reload ? null : SchedulingTaskProcessor.SchedulingTasks.Where(kvp => organization.ID.IsEquals(kvp.Value.OrganizationID)).Select(kvp => kvp.Value).OrderBy(schedulingTask => schedulingTask.Time).ToList();
			if (reload || schedulingTasks.Count < 1)
			{
				var filter = Filters<SchedulingTask>.And(Filters<SchedulingTask>.Equals("SystemID", organization.ID));
				await Utility.Cache.RemoveAsync(Extensions.GetRelatedCacheKeys(filter, Sorts<SchedulingTask>.Ascending("Time")), cancellationToken).ConfigureAwait(false);
				schedulingTasks = organization.GetRefreshingTasks().Concat(await SchedulingTaskProcessor.SearchAsync(filter, cancellationToken).ConfigureAwait(false) ?? []).OrderBy(schedulingTask => schedulingTask.Time).ToList();
				schedulingTasks.ForEach(schedulingTask => SchedulingTaskProcessor.SchedulingTasks[schedulingTask.ID] = schedulingTask);
			}
			return schedulingTasks;
		}

		public static Organization GetOrganizationByID(this string id, bool force = false, bool fetchRepository = true)
		{
			var organization = !force && !string.IsNullOrWhiteSpace(id) && OrganizationProcessor.Organizations.ContainsKey(id)
				? OrganizationProcessor.Organizations[id]
				: null;
			if (organization == null || organization.OriginalPrivileges == null || organization.OriginalPrivileges.AdministrativeRoles == null || organization.OriginalPrivileges.AdministrativeRoles.Count < 1)
				organization = Organization.Get<Organization>(id)?.Set();
			return organization;
		}

		public static async Task<Organization> GetOrganizationByIDAsync(this string id, CancellationToken cancellationToken = default, bool force = false)
		{
			var organization = (id ?? "").GetOrganizationByID(force, false);
			if (organization == null || organization.OriginalPrivileges == null || organization.OriginalPrivileges.AdministrativeRoles == null || organization.OriginalPrivileges.AdministrativeRoles.Count < 1)
				organization = (await Organization.GetAsync<Organization>(id, cancellationToken).ConfigureAwait(false))?.Set();
			return organization;
		}

		public static Organization GetOrganizationByAlias(this string alias, bool fetchRepository = true)
		{
			if (string.IsNullOrWhiteSpace(alias) || Utility.NotRecognizedAliases.Contains($"Organization:{alias}"))
				return null;

			if ((!OrganizationProcessor.OrganizationsByAlias.TryGetValue(alias, out var organization) || organization == null) && fetchRepository)
			{
				organization = Organization.Get<Organization>(Filters<Organization>.Equals("Alias", alias), null, null)?.Set();
				if (organization == null)
					Utility.NotRecognizedAliases.Add($"Organization:{alias}");
				else
					new CommunicateMessage(Utility.ServiceName)
					{
						Type = $"{organization.GetObjectName()}#Update",
						Data = organization.ToJson(),
						ExcludedNodeID = Utility.NodeID
					}.Send();
			}

			if (organization != null && (organization.OriginalPrivileges == null || organization.OriginalPrivileges.AdministrativeRoles == null || organization.OriginalPrivileges.AdministrativeRoles.Count < 1))
				organization = Organization.Get<Organization>(organization.ID)?.Set();
			return organization;
		}

		public static async Task<Organization> GetOrganizationByAliasAsync(this string alias, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(alias) || Utility.NotRecognizedAliases.Contains($"Organization:{alias}"))
				return null;

			var organization = alias.GetOrganizationByAlias(false) ?? (await Organization.GetAsync<Organization>(Filters<Organization>.Equals("Alias", alias), null, null, cancellationToken).ConfigureAwait(false))?.Set();
			if (organization == null)
				Utility.NotRecognizedAliases.Add($"Organization:{alias}");
			else
				new CommunicateMessage(Utility.ServiceName)
				{
					Type = $"{organization.GetObjectName()}#Update",
					Data = organization.ToJson(),
					ExcludedNodeID = Utility.NodeID
				}.Send();

			if (organization != null && (organization.OriginalPrivileges == null || organization.OriginalPrivileges.AdministrativeRoles == null || organization.OriginalPrivileges.AdministrativeRoles.Count < 1))
				organization = (await Organization.GetAsync<Organization>(organization.ID, cancellationToken).ConfigureAwait(false))?.Set();
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

			// clear related cache
			await Task.WhenAll
			(
				Utility.Cache.RemoveAsync(htmlCacheKeys.Concat(dataCacheKeys).Distinct(StringComparer.OrdinalIgnoreCase).ToList(), cancellationToken),
				Utility.IsCacheLogEnabled ? Utility.WriteLogAsync(correlationID, $"Clear related cache of an organization [{organization.Title} - ID: {organization.ID}]\r\n- {dataCacheKeys.Distinct(StringComparer.OrdinalIgnoreCase).Count()} messageData keys => {dataCacheKeys.Distinct(StringComparer.OrdinalIgnoreCase).Join(", ")}\r\n- {htmlCacheKeys.Distinct(StringComparer.OrdinalIgnoreCase).Count()} html keys => {htmlCacheKeys.Distinct(StringComparer.OrdinalIgnoreCase).Join(", ")}", "Caches") : Task.CompletedTask,
				doRefresh && (organization.ExamineURLs == null || organization.ExamineURLs.Count < 1) ? Task.WhenAll(
					$"{organization.URL}?x-force-cache".RefreshWebPageAsync(1, correlationID, $"Refresh home desktop when related cache of an organization was clean [{organization.Title} - ID: {organization.ID}]"),
					$"{organization.FakePortalsHttpURI ?? Utility.PortalsHttpURI}/_js/o_{organization.ID}.js?x-force-cache".RefreshWebPageAsync(1, correlationID, $"Refresh organization JS when related cache of an organization was clean [{organization.Title} - ID: {organization.ID}]")
				) : Task.CompletedTask
			).ConfigureAwait(false);
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
				tasks = tasks.Concat(expressions.Select(expression => expression.ClearCacheAsync(cancellationToken, correlationID, clearRelatedDataCache, clearRelatedHtmlCache, doRefresh))).ToList();

				// clear cache of roles
				var roles = await Role.FindAsync(Filters<Role>.And(Filters<Role>.Equals("SystemID", organization.ID)), null, 0, 1, null, cancellationToken).ConfigureAwait(false);
				tasks = tasks.Concat(roles.Select(role => role.ClearCacheAsync(cancellationToken, correlationID, clearRelatedDataCache))).ToList();

				// clear cache of modules, content-types and business objects
				tasks = tasks.Concat(organization.Modules.Select(module => module.ClearCacheAsync(cancellationToken, correlationID, clearObjectsCache, clearRelatedDataCache, clearRelatedHtmlCache, doRefresh))).ToList();

				// clear cache of desktops
				var desktops = await Desktop.FindAsync(Filters<Desktop>.And(Filters<Desktop>.Equals("SystemID", organization.ID)), null, 0, 1, null, cancellationToken).ConfigureAwait(false);
				tasks = tasks.Concat(desktops.Select(desktop => desktop.ClearCacheAsync(cancellationToken, correlationID, clearRelatedDataCache, clearRelatedHtmlCache, false, doRefresh))).ToList();

				// clear cache of sites
				tasks = tasks.Concat(organization.Sites.Select(site => site.ClearCacheAsync(cancellationToken, correlationID, clearRelatedDataCache, clearRelatedHtmlCache, doRefresh))).ToList();
			}

			// clear cache of the organization
			tasks = tasks.Concat(new[]
			{
				Utility.Cache.RemoveAsync(organization.Remove(), cancellationToken),
				Utility.IsCacheLogEnabled ? Utility.WriteLogAsync(correlationID, $"Clear cache of an organization [{organization.Title} - ID: {organization.ID}]", "Caches") : Task.CompletedTask,
				new CommunicateMessage(Utility.ServiceName)
				{
					Type = $"{organization.GetObjectName()}#Delete",
					Data = organization.ToJson(),
					ExcludedNodeID = Utility.NodeID
				}.SendAsync()
			}).ToList();

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
			var homedesktop = await Desktop.GetAsync<Desktop>(organization.HomeDesktopID, cancellationToken).ConfigureAwait(false);
			await Task.WhenAll
			(
				homedesktop.FindChildrenAsync(cancellationToken, false),
				homedesktop.FindPortletsAsync(cancellationToken, false)
			).ConfigureAwait(false);
			await homedesktop.SetAsync(false, true, cancellationToken).ConfigureAwait(false);

			if (doRefresh && (organization.ExamineURLs == null || organization.ExamineURLs.Count < 1))
				await Task.WhenAll
				(
					$"{organization.URL}/".RefreshWebPageAsync(correlationID, $"Refresh the home desktop when all cache of an organization were clean [{organization.Title} - ID: {organization.ID}]"),
					Utility.IsCacheLogEnabled ? Utility.WriteLogAsync(correlationID, $"The organization was reloaded when all cache were clean\r\n{organization.ToJson()}", "Caches") : Task.CompletedTask
				).ConfigureAwait(false);
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
			var json = string.IsNullOrWhiteSpace(query) && !asFetch ? await Utility.Cache.GetAsync<string>(cacheKey, cancellationToken).ConfigureAwait(false) : null;
			if (!string.IsNullOrWhiteSpace(json))
				return JObject.Parse(json);

			// prepare pagination
			if (totalRecords < 0)
				totalRecords = string.IsNullOrWhiteSpace(query)
					? await Organization.CountAsync(filter, Extensions.GetCacheKeyOfTotalObjects(filter, sort), cancellationToken).ConfigureAwait(false)
					: await Organization.CountAsync(query, filter, cancellationToken).ConfigureAwait(false);

			totalPages = (totalRecords, pageSize).GetTotalPages();
			if (totalPages > 0 && pageNumber > totalPages)
				pageNumber = totalPages;

			// search
			var objects = totalRecords > 0
				? string.IsNullOrWhiteSpace(query)
					? await Organization.FindAsync(filter, sort, pageSize, pageNumber, Extensions.GetCacheKey(filter, sort, pageSize, pageNumber), cancellationToken).ConfigureAwait(false)
					: await Organization.SearchAsync(query, filter, null, pageSize, pageNumber, cancellationToken).ConfigureAwait(false)
				: [];

			// build result
			var response = new JObject
			{
				{ "FilterBy", filter.ToClientJson(query) },
				{ "SortBy", sort?.ToClientJson() },
				{ "Pagination", (totalRecords, totalPages, pageSize, pageNumber).GetPagination() },
				{ "Objects", asFetch
					? objects.Select(@object => new JObject
					{
						{ "ID", @object.ID },
						{ "Alias", @object.Alias },
						{ "Title", @object.Title }
					}).ToJArray()
					: objects.ToJsonArray()
				}
			};

			// update cache
			if (string.IsNullOrWhiteSpace(query) && !asFetch)
				Utility.Cache.SetAsync(cacheKey, response.ToString(Formatting.None), Utility.CancellationToken).Run();

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
			var organization = request.CreateOrganization("Status,Instructions,Privileges,OriginalPrivileges,Created,CreatedID,LastModified,LastModifiedID", obj =>
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

			// send notification
			await organization.SendNotificationAsync("Create", organization.Notifications, ApprovalStatus.Draft, organization.Status, requestInfo, cancellationToken).ConfigureAwait(false);

			// update scheduling tasks
			organization.SendRefreshingTasks();

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

			if (!identity.IsValidUUID())
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
				(await organization.GetSchedulingTasksAsync(cancellationToken).ConfigureAwait(false) ?? []).ForEach(schedulingTask => schedulingTask.SendMessages("Update", null, Utility.NodeID));

			else
			{
				var filter = Filters<Role>.And(Filters<Role>.Equals("SystemID", organization.ID), Filters<Role>.IsNull("ParentID"));
				var sort = Sorts<Role>.Ascending("Title");
				(await Role.FindAsync(filter, sort, 20, 1, Extensions.GetCacheKey(filter, sort, 20, 1), cancellationToken).ConfigureAwait(false) ?? []).ForEach(role => new UpdateMessage
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

			// send notification
			await organization.SendNotificationAsync(@event ?? "Update", organization.Notifications, oldStatus, organization.Status, requestInfo, cancellationToken).ConfigureAwait(false);

			// update scheduling tasks
			organization.SendRefreshingTasks();

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
			organization.Update(request, "ID,OwnerID,HomeDesktopID,SearchDesktopID,Status,Instructions,Privileges,Created,CreatedID,LastModified,LastModifiedID", _ =>
			{
				OrganizationProcessor.MustUpdatedProperties.ForEach(name => organization.SetProperty(name, request.Get(name)));
				organization.OwnerID = isSystemAdministrator ? request.Get("OwnerID", organization.OwnerID) : organization.OwnerID;
				organization.Status = isSystemAdministrator ? request.Get("Status", organization.Status.ToString()).ToEnum<ApprovalStatus>() : organization.Status;
				organization.Alias = string.IsNullOrWhiteSpace(organization.Alias) ? oldAlias : organization.Alias;
				organization.OriginalPrivileges = organization.OriginalPrivileges ?? new Privileges(true);
				organization.LastModified = DateTime.Now;
				organization.LastModifiedID = requestInfo.Session.User.ID;
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
			organization.DeleteAsync(requestInfo, serviceCaller, onServiceCallerGotError, Utility.CancellationToken).Run(false, ex => Utility.WriteErrorAsync(ex, $"Error occurred while deleting an organization => {ex.Message}", "Trash", requestInfo.CorrelationID), 1234);

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
			await Organization.DeleteAsync<Organization>(organization.ID, requestInfo.Session.User.ID, cancellationToken).ConfigureAwait(false);
			await organization.SendNotificationAsync("Delete", organization.Notifications, organization.Status, organization.Status, requestInfo, cancellationToken).ConfigureAwait(false);
			await Task.WhenAll
			(
				Utility.Cache.RemoveAsync(organization.GetCacheKey(), cancellationToken),
				Utility.Cache.RemoveAsync(Extensions.GetCacheKey(Filters<Organization>.And(), Sorts<Organization>.Ascending("Title"), 20, 1), cancellationToken),
				Utility.Cache.RemoveAsync(Extensions.GetCacheKeyOfObjectsJson(Filters<Organization>.And(), Sorts<Organization>.Ascending("Title"), 20, 1), cancellationToken)
			).ConfigureAwait(false);
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
				await Organization.DeleteAsync<Organization>(organization.ID, organization.LastModifiedID, cancellationToken).ConfigureAwait(false);

			// stop if has no info
			if (organization == null)
				return new JObject();

			// clear related cache
			if (requestInfo.GetHeaderParameter("x-converter") == null || @event.IsEquals("Delete"))
				await organization.ClearCacheAsync(cancellationToken, requestInfo.CorrelationID).ConfigureAwait(false);
			else
				await organization.ClearRelatedCacheAsync(cancellationToken, requestInfo.CorrelationID).ConfigureAwait(false);

			// send notifications
			if (sendNotifications)
				await organization.SendNotificationAsync(@event, organization.Notifications, oldStatus, organization.Status, requestInfo, cancellationToken).ConfigureAwait(false);

			// update scheduling tasks
			organization.SendRefreshingTasks(@event.IsEquals("Delete"));

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
				organization.SendNotificationAsync("Rollback", organization.Notifications, oldStatus, organization.Status, requestInfo, cancellationToken)
			).ConfigureAwait(false);
			organization.SendRefreshingTasks();

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
	}
}
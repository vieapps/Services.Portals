#region Related components
using System;
using System.IO;
using System.Net;
using System.Linq;
using System.Data;
using System.Xml.Linq;
using System.Diagnostics;
using System.Dynamic;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using WampSharp.V2.Core.Contracts;
using net.vieapps.Components.Utility;
using net.vieapps.Components.Security;
using net.vieapps.Components.Repository;
using net.vieapps.Components.Caching;
using net.vieapps.Services.Portals.Exceptions;
#endregion

namespace net.vieapps.Services.Portals
{
	public class ServiceComponent : ServiceBase, ICmsPortalsService
	{

		#region Definitions
		IDisposable ServiceCommunicator { get; set; }

		IAsyncDisposable ServiceInstance { get; set; }

		DateTime TimeOfRefreshOrganizations { get; set; } = DateTime.Now;

		public override string ServiceName => "Portals";

		public ModuleDefinition GetDefinition()
			=> new ModuleDefinition(RepositoryMediator.GetEntityDefinition<Organization>().RepositoryDefinition);

		void UpdateDefinition(ModuleDefinition moduleDefinition)
		{
			if (moduleDefinition != null && !string.IsNullOrWhiteSpace(moduleDefinition.ID) && !Utility.ModuleDefinitions.ContainsKey(moduleDefinition.ID))
			{
				Utility.ModuleDefinitions[moduleDefinition.ID] = moduleDefinition;
				moduleDefinition.ContentTypeDefinitions.ForEach(contentTypeDefinition =>
				{
					contentTypeDefinition.ModuleDefinition = moduleDefinition;
					Utility.ContentTypeDefinitions[contentTypeDefinition.ID] = contentTypeDefinition;
				});
			}
		}
		#endregion

		#region Register/Start
		public override Task RegisterServiceAsync(IEnumerable<string> args, Action<IService> onSuccess = null, Action<Exception> onError = null)
			=> base.RegisterServiceAsync
			(
				args,
				async _ =>
				{
					this.ServiceInstance = await Router.IncomingChannel.RealmProxy.Services.RegisterCallee<ICmsPortalsService>(() => this, RegistrationInterceptor.Create(this.ServiceName)).ConfigureAwait(false);
					this.ServiceCommunicator?.Dispose();
					this.ServiceCommunicator = CmsPortalsServiceExtensions.RegisterServiceCommunicator
					(
						async message => await this.ProcessCommunicateMessageAsync(message).ConfigureAwait(false),
						exception => this.Logger?.LogError($"Error occurred while fetching an communicate message of CMS Portals => {exception.Message}", this.State == ServiceState.Connected ? exception : null)
					);
					this.Logger?.LogDebug($"Successfully{(this.State == ServiceState.Disconnected ? " re-" : " ")}register the service with CMS Portals");
					onSuccess?.Invoke(this);
				},
				onError
			);

		public override Task UnregisterServiceAsync(IEnumerable<string> args, bool available = true, Action<IService> onSuccess = null, Action<Exception> onError = null)
			=> base.UnregisterServiceAsync
			(
				args,
				available,
				async _ =>
				{
					try
					{
						await this.ServiceInstance.DisposeAsync().ConfigureAwait(false);
					}
					catch { }
					finally
					{
						this.ServiceInstance = null;
					}
					this.ServiceCommunicator?.Dispose();
					this.ServiceCommunicator = null;
					this.Logger?.LogDebug($"Successfully unregister the service with CMS Portals");
					onSuccess?.Invoke(this);
				},
				onError
			);

		public override Task StartAsync(string[] args = null, bool initializeRepository = true, Action<IService> next = null)
			=> base.StartAsync(args, initializeRepository, _ =>
			{
				this.UpdateDefinition(this.GetDefinition());
				this.Logger?.LogDebug($"Portals' data files directory: {Utility.DataFilesDirectory ?? "None"}");

				Utility.APIsHttpURI = this.GetHttpURI("APIs", "https://apis.vieapps.net");
				while (Utility.APIsHttpURI.EndsWith("/"))
					Utility.APIsHttpURI = Utility.APIsHttpURI.Left(Utility.APIsHttpURI.Length - 1);

				Utility.FilesHttpURI = this.GetHttpURI("Files", "https://fs.vieapps.net");
				while (Utility.FilesHttpURI.EndsWith("/"))
					Utility.FilesHttpURI = Utility.FilesHttpURI.Left(Utility.FilesHttpURI.Length - 1);

				Utility.PortalsHttpURI = this.GetHttpURI("Portals", "https://portals.vieapps.net");
				while (Utility.PortalsHttpURI.EndsWith("/"))
					Utility.PortalsHttpURI = Utility.PortalsHttpURI.Left(Utility.PortalsHttpURI.Length - 1);

				Utility.CmsPortalsHttpURI = this.GetHttpURI("CMSPortals", "https://cms.vieapps.net");
				while (Utility.CmsPortalsHttpURI.EndsWith("/"))
					Utility.CmsPortalsHttpURI = Utility.CmsPortalsHttpURI.Left(Utility.CmsPortalsHttpURI.Length - 1);

				Utility.MessagingService = this.MessagingService;
				Utility.Logger = this.Logger;

				Utility.EncryptionKey = this.EncryptionKey;
				Utility.ValidationKey = this.ValidationKey;
				Utility.NotificationsKey = UtilityService.GetAppSetting("Keys:Notifications");

				Utility.NotRecognizedAliases.Add($"Site:{new Uri(Utility.PortalsHttpURI).Host}");
				Task.Run(async () =>
				{
					Utility.DefaultSite = await UtilityService.GetAppSetting("Portals:Default:SiteID", "").GetSiteByIDAsync().ConfigureAwait(false);
					this.Logger?.LogDebug($"The default site: {(Utility.DefaultSite != null ? $"{Utility.DefaultSite.Title} [{Utility.DefaultSite.ID}]" : "None")}");
				}).ConfigureAwait(false);

				Task.Run(async () =>
				{
					// wait for a few times
					await Task.Delay(UtilityService.GetRandomNumber(678, 789), this.CancellationToken).ConfigureAwait(false);

					// get OEmbed providers
					try
					{
						await this.GetOEmbedProvidersAsync(this.CancellationToken).ConfigureAwait(false);
					}
					catch { }

					// prepare multi-languges
					try
					{
						await this.PrepareLanguagesAsync(this.CancellationToken).ConfigureAwait(false);
					}
					catch { }

					// gathering definitions
					try
					{
						await this.SendInterCommunicateMessageAsync(new CommunicateMessage("CMS.Portals")
						{
							Type = "Definition#RequestInfo"
						}, this.CancellationToken).ConfigureAwait(false);
					}
					catch (Exception ex)
					{
						await this.WriteLogsAsync(UtilityService.NewUUID, $"Error occurred while sending a request for gathering definitions => {ex.Message}", ex, this.ServiceName, "CMS.Portals", LogLevel.Error).ConfigureAwait(false);
					}

					// warm-up the Files HTTP service
					if (!string.IsNullOrWhiteSpace(Utility.FilesHttpURI))
						try
						{
							await UtilityService.FetchHttpAsync(Utility.FilesHttpURI).ConfigureAwait(false);
						}
						catch { }
				}).ConfigureAwait(false);

				// refine thumbnail images
				if (args?.FirstOrDefault(arg => arg.IsEquals("/refine-thumbnails")) != null)
					Task.Run(async () => await this.RefineThumbnailImagesAsync().ConfigureAwait(false)).ConfigureAwait(false);

				// send info & reload resources
				this.StartTimer(() => this.SendDefinitionInfo(), 12 * 60 * 60);

				this.StartTimer(async () =>
				{
					var organizations = await OrganizationProcessor.ReloadOrganizationsAsync(this.CancellationToken).ConfigureAwait(false);
					await this.WriteLogsAsync(UtilityService.NewUUID, $"All organizations have been re-loaded - Total: {organizations.Count}", null, this.ServiceName, "Caches").ConfigureAwait(false);
					await Task.Delay(UtilityService.GetRandomNumber(1234, 3456), this.CancellationToken).ConfigureAwait(false);
					if (this.TimeOfRefreshOrganizations.Hour < DateTime.Now.Hour)
					{
						new CommunicateMessage(this.ServiceName)
						{
							Type = "Organization#Refresh"
						}.Send();
						organizations.ForEach(organization => new UpdateMessage
						{
							Type = $"{this.ServiceName}#Organization#Update",
							Data = organization.ToJson(),
							DeviceID = "*"
						}.Send());
					}
				}, 61 * 60);

				this.StartTimer(async () =>
				{
					var sites = await SiteProcessor.ReloadSitesAsync(this.CancellationToken).ConfigureAwait(false);
					await this.WriteLogsAsync(UtilityService.NewUUID, $"All sites have been re-loaded - Total: {sites.Count}", null, this.ServiceName, "Caches").ConfigureAwait(false);
				}, 65 * 60);

				this.StartTimer(async () => await this.GetOEmbedProvidersAsync(this.CancellationToken).ConfigureAwait(false), 15 * 60);
				this.StartTimer(async () => await this.PrepareLanguagesAsync(this.CancellationToken).ConfigureAwait(false), 15 * 60);

				// scheduling tasks
				Task.Run(async () =>
				{
					await Task.Delay(UtilityService.GetRandomNumber(2345, 3456), this.CancellationToken).ConfigureAwait(false);
					await OrganizationProcessor.ReloadOrganizationsAsync(this.CancellationToken, true).ConfigureAwait(false);
					this.StartTimer(async () =>
					{
						var correlationID = UtilityService.NewUUID;
						try
						{
							await SchedulingTaskProcessor.RunSchedulingTasksAsync(correlationID).ConfigureAwait(false);
						}
						catch (Exception ex)
						{
							await this.WriteLogsAsync(correlationID, $"Error occurred while running the scheduling tasks => {ex.Message} [{ex.GetType()}]", ex, this.ServiceName, "Task", LogLevel.Error).ConfigureAwait(false);
						}
					}, 13);
				}).ConfigureAwait(false);

				// invoke next action
				next?.Invoke(this);
			});
		#endregion

		#region Authorizations
		protected override bool IsAdministrator(IUser user, RepositoryBase @object)
			=> @object is IPortalObject portalObject
				? user != null && user.IsAdministrator(portalObject.WorkingPrivileges, portalObject.Parent?.WorkingPrivileges, (portalObject.OrganizationID ?? "").GetOrganizationByID())
				: base.IsAdministrator(user, @object);

		protected override async Task<bool> IsAdministratorAsync(IUser user, string objectName, RepositoryBase @object, string correlationID = null, CancellationToken cancellationToken = default)
			=> @object is IPortalObject portalObject
				? (user != null && user.IsAdministrator(portalObject.WorkingPrivileges, portalObject.Parent?.WorkingPrivileges, await (portalObject.OrganizationID ?? "").GetOrganizationByIDAsync(cancellationToken).ConfigureAwait(false))) || await this.IsSystemAdministratorAsync(user, correlationID, cancellationToken).ConfigureAwait(false)
				: await base.IsAdministratorAsync(user, objectName, @object, correlationID, cancellationToken).ConfigureAwait(false);

		protected override bool IsModerator(IUser user, RepositoryBase @object)
			=> @object is IPortalObject portalObject
				? user != null && user.IsModerator(portalObject.WorkingPrivileges, portalObject.Parent?.WorkingPrivileges, (portalObject.OrganizationID ?? "").GetOrganizationByID())
				: base.IsModerator(user, @object);

		protected override async Task<bool> IsModeratorAsync(IUser user, string objectName, RepositoryBase @object, string correlationID = null, CancellationToken cancellationToken = default)
			=> @object is IPortalObject portalObject
				? (user != null && user.IsModerator(portalObject.WorkingPrivileges, portalObject.Parent?.WorkingPrivileges, await (portalObject.OrganizationID ?? "").GetOrganizationByIDAsync(cancellationToken).ConfigureAwait(false))) || await this.IsAdministratorAsync(user, objectName, @object, correlationID, cancellationToken).ConfigureAwait(false)
				: await base.IsModeratorAsync(user, objectName, @object, correlationID, cancellationToken).ConfigureAwait(false);

		protected override bool IsEditor(IUser user, RepositoryBase @object)
			=> @object is IPortalObject portalObject
				? user != null && user.IsEditor(portalObject.WorkingPrivileges, portalObject.Parent?.WorkingPrivileges, (portalObject.OrganizationID ?? "").GetOrganizationByID())
				: base.IsEditor(user, @object);

		protected override async Task<bool> IsEditorAsync(IUser user, string objectName, RepositoryBase @object, string correlationID = null, CancellationToken cancellationToken = default)
			=> @object is IPortalObject portalObject
				? (user != null && user.IsEditor(portalObject.WorkingPrivileges, portalObject.Parent?.WorkingPrivileges, await (portalObject.OrganizationID ?? "").GetOrganizationByIDAsync(cancellationToken).ConfigureAwait(false))) || await this.IsModeratorAsync(user, objectName, @object, correlationID, cancellationToken).ConfigureAwait(false)
				: await base.IsEditorAsync(user, objectName, @object, correlationID, cancellationToken).ConfigureAwait(false);

		protected override bool IsContributor(IUser user, RepositoryBase @object)
			=> @object is IPortalObject portalObject
				? user != null && user.IsContributor(portalObject.WorkingPrivileges, portalObject.Parent?.WorkingPrivileges, (portalObject.OrganizationID ?? "").GetOrganizationByID())
				: base.IsContributor(user, @object);

		protected override async Task<bool> IsContributorAsync(IUser user, string objectName, RepositoryBase @object, string correlationID = null, CancellationToken cancellationToken = default)
			=> @object is IPortalObject portalObject
				? (user != null && user.IsContributor(portalObject.WorkingPrivileges, portalObject.Parent?.WorkingPrivileges, await (portalObject.OrganizationID ?? "").GetOrganizationByIDAsync(cancellationToken).ConfigureAwait(false))) || await this.IsEditorAsync(user, objectName, @object, correlationID, cancellationToken).ConfigureAwait(false)
				: await base.IsContributorAsync(user, objectName, @object, correlationID, cancellationToken).ConfigureAwait(false);

		protected override bool IsViewer(IUser user, RepositoryBase @object)
			=> @object is IPortalObject portalObject
				? user != null && user.IsViewer(portalObject.WorkingPrivileges, portalObject.Parent?.WorkingPrivileges, (portalObject.OrganizationID ?? "").GetOrganizationByID())
				: base.IsViewer(user, @object);

		protected override async Task<bool> IsViewerAsync(IUser user, string objectName, RepositoryBase @object, string correlationID = null, CancellationToken cancellationToken = default)
			=> @object is IPortalObject portalObject
				? (user != null && user.IsViewer(portalObject.WorkingPrivileges, portalObject.Parent?.WorkingPrivileges, await (portalObject.OrganizationID ?? "").GetOrganizationByIDAsync(cancellationToken).ConfigureAwait(false))) || await this.IsContributorAsync(user, objectName, @object, correlationID, cancellationToken).ConfigureAwait(false)
				: await base.IsViewerAsync(user, objectName, @object, correlationID, cancellationToken).ConfigureAwait(false);

		protected override bool IsDownloader(IUser user, RepositoryBase @object)
			=> @object is IPortalObject portalObject
				? user != null && user.IsDownloader(portalObject.WorkingPrivileges, portalObject.Parent?.WorkingPrivileges, (portalObject.OrganizationID ?? "").GetOrganizationByID())
				: base.IsDownloader(user, @object);

		protected override async Task<bool> IsDownloaderAsync(IUser user, string objectName, RepositoryBase @object, string correlationID = null, CancellationToken cancellationToken = default)
			=> @object is IPortalObject portalObject
				? (user != null && user.IsDownloader(portalObject.WorkingPrivileges, portalObject.Parent?.WorkingPrivileges, await (portalObject.OrganizationID ?? "").GetOrganizationByIDAsync(cancellationToken).ConfigureAwait(false))) || await this.IsViewerAsync(user, objectName, @object, correlationID, cancellationToken).ConfigureAwait(false)
				: await base.IsDownloaderAsync(user, objectName, @object, correlationID, cancellationToken).ConfigureAwait(false);

		public override async Task<bool> CanManageAsync(IUser user, string objectName, RepositoryBase @object, CancellationToken cancellationToken = default)
			=> @object is IPortalObject portalObject
				? (user != null && user.IsAdministrator(portalObject.WorkingPrivileges, portalObject.Parent?.WorkingPrivileges, await (portalObject.OrganizationID ?? "").GetOrganizationByIDAsync(cancellationToken).ConfigureAwait(false))) || await this.IsSystemAdministratorAsync(user, null, cancellationToken).ConfigureAwait(false)
				: await base.CanManageAsync(user, objectName, @object, cancellationToken).ConfigureAwait(false);

		public override async Task<bool> CanModerateAsync(IUser user, string objectName, RepositoryBase @object, CancellationToken cancellationToken = default)
			=> @object is IPortalObject portalObject
				? (user != null && user.IsModerator(portalObject.WorkingPrivileges, portalObject.Parent?.WorkingPrivileges, await (portalObject.OrganizationID ?? "").GetOrganizationByIDAsync(cancellationToken).ConfigureAwait(false))) || await this.IsSystemAdministratorAsync(user, null, cancellationToken).ConfigureAwait(false)
				: await base.CanModerateAsync(user, objectName, @object, cancellationToken).ConfigureAwait(false);

		public override async Task<bool> CanEditAsync(IUser user, string objectName, RepositoryBase @object, CancellationToken cancellationToken = default)
			=> @object is IPortalObject portalObject
				? (user != null && user.IsEditor(portalObject.WorkingPrivileges, portalObject.Parent?.WorkingPrivileges, await (portalObject.OrganizationID ?? "").GetOrganizationByIDAsync(cancellationToken).ConfigureAwait(false))) || await this.IsSystemAdministratorAsync(user, null, cancellationToken).ConfigureAwait(false)
				: await base.CanEditAsync(user, objectName, @object, cancellationToken).ConfigureAwait(false);

		public override async Task<bool> CanContributeAsync(IUser user, string objectName, RepositoryBase @object, CancellationToken cancellationToken = default)
			=> @object is IPortalObject portalObject
				? (user != null && user.IsContributor(portalObject.WorkingPrivileges, portalObject.Parent?.WorkingPrivileges, await (portalObject.OrganizationID ?? "").GetOrganizationByIDAsync(cancellationToken).ConfigureAwait(false))) || await this.IsSystemAdministratorAsync(user, null, cancellationToken).ConfigureAwait(false)
				: await base.CanContributeAsync(user, objectName, @object, cancellationToken).ConfigureAwait(false);

		public override async Task<bool> CanContributeAsync(User user, string objectName, string systemID, string entityInfo, string objectID, CancellationToken cancellationToken = default)
		{
			var canContribute = await this.IsSystemAdministratorAsync(user, null, cancellationToken).ConfigureAwait(false);
			if (!canContribute)
			{
				if (!string.IsNullOrWhiteSpace(systemID) && !string.IsNullOrWhiteSpace(entityInfo) && !string.IsNullOrWhiteSpace(objectID))
					canContribute = await this.CanContributeAsync(user, objectName, await this.GetBusinessObjectAsync(entityInfo, objectID, cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
				else if (user != null)
				{
					var contentType = RepositoryMediator.GetBusinessRepositoryEntity(entityInfo) as ContentType;
					var organization = contentType?.Organization ?? await (systemID ?? "").GetOrganizationByIDAsync(cancellationToken).ConfigureAwait(false);
					canContribute = user.IsContributor(contentType?.WorkingPrivileges, contentType?.Parent?.WorkingPrivileges, organization);
				}
			}
			return canContribute;
		}

		public override async Task<bool> CanViewAsync(IUser user, string objectName, RepositoryBase @object, CancellationToken cancellationToken = default)
			=> @object is IPortalObject portalObject
				? (user != null && user.IsViewer(portalObject.WorkingPrivileges, portalObject.Parent?.WorkingPrivileges, await (portalObject.OrganizationID ?? "").GetOrganizationByIDAsync(cancellationToken).ConfigureAwait(false))) || await this.IsSystemAdministratorAsync(user, null, cancellationToken).ConfigureAwait(false)
				: await base.CanViewAsync(user, objectName, @object, cancellationToken).ConfigureAwait(false);

		public override async Task<bool> CanDownloadAsync(IUser user, string objectName, RepositoryBase @object, CancellationToken cancellationToken = default)
			=> @object is IPortalObject portalObject
				? (user != null && user.IsDownloader(portalObject.WorkingPrivileges, portalObject.Parent?.WorkingPrivileges, await (portalObject.OrganizationID ?? "").GetOrganizationByIDAsync(cancellationToken).ConfigureAwait(false))) || await this.IsSystemAdministratorAsync(user, null, cancellationToken).ConfigureAwait(false)
				: await base.CanDownloadAsync(user, objectName, @object, cancellationToken).ConfigureAwait(false);
		#endregion

		public override async Task<JToken> ProcessRequestAsync(RequestInfo requestInfo, CancellationToken cancellationToken = default)
		{
			var stopwatch = Stopwatch.StartNew();
			await this.WriteLogsAsync(requestInfo, $"Begin request ({requestInfo.Verb} {requestInfo.GetURI()})").ConfigureAwait(false);
			try
			{
				JToken json = null;
				using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, this.CancellationToken);
				switch (requestInfo.ObjectName.ToLower())
				{

					#region process the request of Portals objects
					case "organization":
					case "core.organization":
						json = await this.ProcessOrganizationAsync(requestInfo, cts.Token).ConfigureAwait(false);
						break;

					case "role":
					case "core.role":
						json = await this.ProcessRoleAsync(requestInfo, cts.Token).ConfigureAwait(false);
						break;

					case "site":
					case "core.site":
						json = await this.ProcessSiteAsync(requestInfo, cts.Token).ConfigureAwait(false);
						break;

					case "desktop":
					case "core.desktop":
						json = await this.ProcessDesktopAsync(requestInfo, cts.Token).ConfigureAwait(false);
						break;

					case "portlet":
					case "core.portlet":
						json = await this.ProcessPortletAsync(requestInfo, cts.Token).ConfigureAwait(false);
						break;

					case "module":
					case "core.module":
						json = await this.ProcessModuleAsync(requestInfo, cts.Token).ConfigureAwait(false);
						break;

					case "contenttype":
					case "content.type":
					case "content-type":
					case "core.contenttype":
					case "core.content.type":
						json = await this.ProcessContentTypeAsync(requestInfo, cts.Token).ConfigureAwait(false);
						break;

					case "expression":
					case "core.expression":
						json = await this.ProcessExpressionAsync(requestInfo, cts.Token).ConfigureAwait(false);
						break;

					case "task":
					case "schedulingtask":
					case "scheduling.task":
					case "scheduling-task":
					case "core.task":
					case "core.schedulingtask":
					case "core.scheduling.task":
					case "core.scheduling-task":
						json = await this.ProcessSchedulingTaskAsync(requestInfo, cts.Token).ConfigureAwait(false);
						break;
					#endregion

					#region process the request of CMS objects
					case "category":
					case "cms.category":
						json = await this.ProcessCategoryAsync(requestInfo, cts.Token).ConfigureAwait(false);
						break;

					case "content":
					case "cms.content":
						json = await this.ProcessContentAsync(requestInfo, cts.Token).ConfigureAwait(false);
						break;

					case "item":
					case "cms.item":
						json = await this.ProcessItemAsync(requestInfo, cts.Token).ConfigureAwait(false);
						break;

					case "link":
					case "cms.link":
						json = await this.ProcessLinkAsync(requestInfo, cts.Token).ConfigureAwait(false);
						break;

					case "form":
					case "cms.form":
						json = await this.ProcessFormAsync(requestInfo, cts.Token).ConfigureAwait(false);
						break;

					case "crawler":
					case "crawlers":
					case "cms.crawler":
					case "cms.crawlers":
						json = await this.ProcessCrawlerAsync(requestInfo, cts.Token).ConfigureAwait(false);
						break;
					#endregion

					#region process request of Portals HTTP service
					case "identify.system":
						json = await this.IdentifySystemAsync(requestInfo, cts.Token).ConfigureAwait(false);
						break;

					case "generate.feed":
						json = await this.GenerateFeedAsync(requestInfo, cts.Token).ConfigureAwait(false);
						break;

					case "process.http.request":
						json = await this.ProcessHttpRequestAsync(requestInfo, cts.Token).ConfigureAwait(false);
						break;
					#endregion

					#region process the request of definitions, instructions, files, profiles and all known others
					case "definitions":
						switch (requestInfo.GetObjectIdentity())
						{
							case "moduledefinitions":
							case "module.definitions":
							case "module-definitions":
								json = Utility.ModuleDefinitions.Values.OrderBy(definition => definition.Title).ToJArray();
								break;

							case "social":
							case "socials":
								json = UtilityService.GetAppSetting("Portals:Socials", "Facebook,Twitter").ToArray().ToJArray();
								break;

							case "tracking":
							case "trackings":
								json = UtilityService.GetAppSetting("Portals:Trackings", "GoogleAnalytics,FacebookPixel").ToArray().ToJArray();
								break;

							case "theme":
							case "themes":
								json = await this.GetThemesAsync(cts.Token).ConfigureAwait(false);
								break;

							case "template":
								json = await this.ProcessTemplateAsync(requestInfo, cts.Token).ConfigureAwait(false);
								break;

							case "organization":
							case "core.organization":
								json = this.GenerateFormControls<Organization>();
								break;

							case "site":
							case "core.site":
								json = this.GenerateFormControls<Site>();
								break;

							case "role":
							case "core.role":
								json = this.GenerateFormControls<Role>();
								break;

							case "desktop":
							case "core.desktop":
								json = this.GenerateFormControls<Desktop>();
								break;

							case "portlet":
							case "core.portlet":
								json = this.GenerateFormControls<Portlet>();
								break;

							case "module":
							case "core.module":
								json = this.GenerateFormControls<Module>();
								break;

							case "contenttype":
							case "content.type":
							case "content-type":
							case "core.contenttype":
							case "core.content.type":
								json = this.GenerateFormControls<ContentType>();
								break;

							case "expression":
							case "core.expression":
								json = this.GenerateFormControls<Expression>();
								break;

							case "task":
							case "schedulingtask":
							case "scheduling.task":
							case "scheduling-task":
							case "core.task":
							case "core.schedulingtask":
							case "core.scheduling.task":
							case "core.scheduling-task":
								json = this.GenerateFormControls<SchedulingTask>();
								break;

							case "crawler":
							case "cms.crawler":
								json = this.GenerateFormControls<Crawler>();
								break;

							case "category":
							case "cms.category":
							case "content":
							case "cms.content":
							case "item":
							case "cms.item":
							case "link":
							case "cms.link":
							case "form":
							case "cms.form":
								json = (requestInfo.GetParameter("x-content-type-id") ?? "").GetContentTypeByID().GenerateFormControls(requestInfo.GetParameter("x-view-controls") != null, id => (id ?? "").GetContentTypeByID()) ?? new JArray();
								break;

							default:
								throw new InvalidRequestException($"The request is invalid [({requestInfo.Verb}): {requestInfo.GetURI()}]");
						}
						break;

					case "instructions":
						var mode = requestInfo.Extra != null && requestInfo.Extra.ContainsKey("mode") ? requestInfo.Extra["mode"].GetCapitalizedFirstLetter() : null;
						var organization = mode != null ? await (requestInfo.GetParameter("x-system-id") ?? requestInfo.GetParameter("active-id") ?? "").GetOrganizationByIDAsync(cts.Token).ConfigureAwait(false) : null;
						json = new JObject
							{
								{ "Message", organization != null && organization.Instructions != null && organization.Instructions.TryGetValue(mode, out var instruction) ? instruction?.ToJson() : null },
								{ "Email", organization?.EmailSettings?.ToJson() },
							};
						break;

					case "files":
					case "attachments":
						json = await this.ProcessAttachmentFileAsync(requestInfo, cts.Token).ConfigureAwait(false);
						break;

					case "profile":
						break;

					case "excel":
						json = await this.DoExcelActionAsync(requestInfo, cts.Token).ConfigureAwait(false);
						break;

					case "cache":
					case "caches":
						json = await this.ClearCacheAsync(requestInfo, cts.Token).ConfigureAwait(false);
						break;

					case "approve":
					case "approval":
						json = await this.ApproveAsync(requestInfo, cts.Token).ConfigureAwait(false);
						break;

					case "move":
						json = await this.MoveAsync(requestInfo, cts.Token).ConfigureAwait(false);
						break;

					default:
						throw new InvalidRequestException($"The request is invalid [({requestInfo.Verb}): {requestInfo.GetURI()}]");
						#endregion

				}
				stopwatch.Stop();
				await Task.WhenAll
				(
					this.WriteLogsAsync(requestInfo, $"Success response - Execution times: {stopwatch.GetElapsedTimes()}"),
					this.IsDebugResultsEnabled ? this.WriteLogsAsync(requestInfo, $"- Request: {requestInfo.ToString(this.JsonFormat)}" + "\r\n" + $"- Response: {json?.ToString(this.JsonFormat)}") : Task.CompletedTask
				).ConfigureAwait(false);
				return json;
			}
			catch (Exception ex)
			{
				throw this.GetRuntimeException(requestInfo, ex, stopwatch);
			}
		}

		#region Get static data (themes, language resources, providers  of OEmbed media, ...)
		async Task<JArray> GetThemesAsync(CancellationToken cancellationToken)
		{
			var themes = new JArray();
			if (string.IsNullOrWhiteSpace(Utility.DataFilesDirectory))
				themes.Add(new JObject
				{
					{ "name", "default" },
					{ "description", "The theme with default styles and coloring codes" },
					{ "author", "System" }
				});
			else if (Directory.Exists(Path.Combine(Utility.DataFilesDirectory, "themes")))
				await Directory.GetDirectories(Path.Combine(Utility.DataFilesDirectory, "themes")).ForEachAsync(async directory =>
				{
					var name = Path.GetFileName(directory).ToLower();
					var packageInfo = new JObject
					{
						{ "name", name },
						{ "description", name.IsEquals("default") ? "The theme with default styles and coloring codes" : "" },
						{ "author", "System" }
					};
					var fileInfo = new FileInfo(Path.Combine(directory, "package.json"));
					if (fileInfo.Exists)
						try
						{
							packageInfo = JObject.Parse(await fileInfo.ReadAsTextAsync(cancellationToken).ConfigureAwait(false));
						}
						catch { }
					themes.Add(packageInfo);
				}, true, false).ConfigureAwait(false);
			return themes;
		}

		async Task PrepareLanguagesAsync(CancellationToken cancellationToken)
		{
			var correlationID = UtilityService.NewUUID;
			Utility.Languages.Clear();
			await UtilityService.GetAppSetting("Portals:Languages", "vi-VN|en-US").ToList("|", true).ForEachAsync(async language => await new[] { "common", "notifications", "portals", "portals.cms", "users" }.ForEachAsync(async module =>
			{
				if (!Utility.Languages.TryGetValue(language, out var languages))
				{
					languages = new ExpandoObject();
					Utility.Languages[language] = languages;
				}
				var url = $"{Utility.APIsHttpURI}/statics/i18n/{module}/{language}.json";
				try
				{
					languages.Merge(JObject.Parse(await new Uri(url).FetchHttpAsync(cancellationToken).ConfigureAwait(false)).ToExpandoObject());
				}
				catch (Exception ex)
				{
					await this.WriteLogsAsync(correlationID, $"Error occurred while gathering i18n language resource [{url}] => {ex.Message}", ex, this.ServiceName, "CMS.Portals", LogLevel.Error).ConfigureAwait(false);
				}
			}, true, false).ConfigureAwait(false), true, false).ConfigureAwait(false);
			if (this.IsDebugResultsEnabled)
				await this.WriteLogsAsync(correlationID, $"Gathering i18n language resources successful => {Utility.Languages.Select(kvp => kvp.Key).Join(" - ")}", null, this.ServiceName, "CMS.Portals", LogLevel.Debug).ConfigureAwait(false);
		}

		async Task GetOEmbedProvidersAsync(CancellationToken cancellationToken)
		{
			var correlationID = UtilityService.NewUUID;
			try
			{
				var providers = JArray.Parse(await new Uri($"{Utility.APIsHttpURI}/statics/oembed.providers.json").FetchHttpAsync(cancellationToken).ConfigureAwait(false));
				Utility.OEmbedProviders.Clear();
				providers.Select(provider => provider as JObject).ForEach(provider =>
				{
					var name = provider.Get<string>("name");
					var schemes = provider.Get<JArray>("schemes").Select(scheme => new Regex($"{(scheme as JValue).Value}", RegexOptions.IgnoreCase)).ToList();
					var patternJson = provider.Get<JObject>("pattern");
					var expression = new Regex(patternJson.Get<string>("expression"), RegexOptions.IgnoreCase);
					var position = patternJson.Get<int>("position");
					var html = patternJson.Get<string>("html");
					Utility.OEmbedProviders.Add(new Tuple<string, List<Regex>, Tuple<Regex, int, string>>(name, schemes, new Tuple<Regex, int, string>(expression, position, html)));
				});
				if (this.IsDebugResultsEnabled)
					await this.WriteLogsAsync(correlationID, $"Gathering OEmbed providers successful => {Utility.OEmbedProviders.Select(info => info.Item1).Join(" - ")}", null, this.ServiceName, "CMS.Portals", LogLevel.Debug).ConfigureAwait(false);
			}
			catch (Exception ex)
			{
				await this.WriteLogsAsync(correlationID, $"Error occurred while gathering OEmbed providers => {ex.Message}", ex, this.ServiceName, "CMS.Portals", LogLevel.Error).ConfigureAwait(false);
			}
		}
		#endregion

		#region Process Core Portals objects
		async Task<JObject> ProcessOrganizationAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			var isSystemAdministrator = await this.IsSystemAdministratorAsync(requestInfo, cancellationToken).ConfigureAwait(false);
			switch (requestInfo.Verb)
			{
				case "GET":
					return "search".IsEquals(requestInfo.GetObjectIdentity())
						? await requestInfo.SearchOrganizationsAsync(isSystemAdministrator, cancellationToken).ConfigureAwait(false)
						: await requestInfo.GetOrganizationAsync(isSystemAdministrator, cancellationToken).ConfigureAwait(false);

				case "POST":
					return await requestInfo.CreateOrganizationAsync(isSystemAdministrator, cancellationToken).ConfigureAwait(false);

				case "PUT":
					return await requestInfo.UpdateOrganizationAsync(isSystemAdministrator, cancellationToken).ConfigureAwait(false);

				case "DELETE":
					return await requestInfo.DeleteOrganizationAsync(isSystemAdministrator, cancellationToken).ConfigureAwait(false);

				default:
					throw new MethodNotAllowedException(requestInfo.Verb);
			}
		}

		async Task<JObject> ProcessSiteAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			var isSystemAdministrator = await this.IsSystemAdministratorAsync(requestInfo, cancellationToken).ConfigureAwait(false);
			switch (requestInfo.Verb)
			{
				case "GET":
					return "search".IsEquals(requestInfo.GetObjectIdentity())
						? await requestInfo.SearchSitesAsync(isSystemAdministrator, cancellationToken).ConfigureAwait(false)
						: await requestInfo.GetSiteAsync(isSystemAdministrator, cancellationToken).ConfigureAwait(false);

				case "POST":
					return await requestInfo.CreateSiteAsync(isSystemAdministrator, cancellationToken).ConfigureAwait(false);

				case "PUT":
					return await requestInfo.UpdateSiteAsync(isSystemAdministrator, cancellationToken).ConfigureAwait(false);

				case "DELETE":
					return await requestInfo.DeleteSiteAsync(isSystemAdministrator, cancellationToken).ConfigureAwait(false);

				default:
					throw new MethodNotAllowedException(requestInfo.Verb);
			}
		}

		async Task<JObject> ProcessRoleAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			var isSystemAdministrator = await this.IsSystemAdministratorAsync(requestInfo, cancellationToken).ConfigureAwait(false);
			switch (requestInfo.Verb)
			{
				case "GET":
					return "search".IsEquals(requestInfo.GetObjectIdentity())
						? await requestInfo.SearchRolesAsync(isSystemAdministrator, cancellationToken).ConfigureAwait(false)
						: await requestInfo.GetRoleAsync(isSystemAdministrator, cancellationToken).ConfigureAwait(false);

				case "POST":
					return await requestInfo.CreateRoleAsync(isSystemAdministrator, (request, token) => this.CallServiceAsync(request, token), (request, msg, ex) => this.WriteLogs(request, msg, ex, LogLevel.Error), cancellationToken).ConfigureAwait(false);

				case "PUT":
					return await requestInfo.UpdateRoleAsync(isSystemAdministrator, (request, token) => this.CallServiceAsync(request, token), (request, msg, ex) => this.WriteLogs(request, msg, ex, LogLevel.Error), cancellationToken).ConfigureAwait(false);

				case "DELETE":
					return await requestInfo.DeleteRoleAsync(isSystemAdministrator, (request, token) => this.CallServiceAsync(request, token), (request, msg, ex) => this.WriteLogs(request, msg, ex, LogLevel.Error), cancellationToken).ConfigureAwait(false);

				default:
					throw new MethodNotAllowedException(requestInfo.Verb);
			}
		}

		async Task<JObject> ProcessDesktopAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			var isSystemAdministrator = await this.IsSystemAdministratorAsync(requestInfo, cancellationToken).ConfigureAwait(false);
			switch (requestInfo.Verb)
			{
				case "GET":
					return "search".IsEquals(requestInfo.GetObjectIdentity())
						? await requestInfo.SearchDesktopsAsync(isSystemAdministrator, cancellationToken).ConfigureAwait(false)
						: await requestInfo.GetDesktopAsync(isSystemAdministrator, cancellationToken).ConfigureAwait(false);

				case "POST":
					return await requestInfo.CreateDesktopAsync(isSystemAdministrator, cancellationToken).ConfigureAwait(false);

				case "PUT":
					return "order-index".IsEquals(requestInfo.GetHeaderParameter("x-update"))
						? await requestInfo.UpdateDesktopPortletsAsync(isSystemAdministrator, cancellationToken).ConfigureAwait(false)
						: await requestInfo.UpdateDesktopAsync(isSystemAdministrator, cancellationToken).ConfigureAwait(false);

				case "DELETE":
					return await requestInfo.DeleteDesktopAsync(isSystemAdministrator, cancellationToken).ConfigureAwait(false);

				default:
					throw new MethodNotAllowedException(requestInfo.Verb);
			}
		}

		async Task<JObject> ProcessPortletAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			var isSystemAdministrator = await this.IsSystemAdministratorAsync(requestInfo, cancellationToken).ConfigureAwait(false);
			switch (requestInfo.Verb)
			{
				case "GET":
					return "search".IsEquals(requestInfo.GetObjectIdentity())
						? await requestInfo.SearchPortletsAsync(isSystemAdministrator, cancellationToken).ConfigureAwait(false)
						: await requestInfo.GetPortletAsync(isSystemAdministrator, cancellationToken).ConfigureAwait(false);

				case "POST":
					return await requestInfo.CreatePortletAsync(isSystemAdministrator, cancellationToken).ConfigureAwait(false);

				case "PUT":
					return await requestInfo.UpdatePortletAsync(isSystemAdministrator, cancellationToken).ConfigureAwait(false);

				case "DELETE":
					return await requestInfo.DeletePortletAsync(isSystemAdministrator, cancellationToken).ConfigureAwait(false);

				default:
					throw new MethodNotAllowedException(requestInfo.Verb);
			}
		}

		async Task<JObject> ProcessModuleAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			var isSystemAdministrator = await this.IsSystemAdministratorAsync(requestInfo, cancellationToken).ConfigureAwait(false);
			switch (requestInfo.Verb)
			{
				case "GET":
					return "search".IsEquals(requestInfo.GetObjectIdentity())
						? await requestInfo.SearchModulesAsync(isSystemAdministrator, cancellationToken).ConfigureAwait(false)
						: await requestInfo.GetModuleAsync(isSystemAdministrator, cancellationToken).ConfigureAwait(false);

				case "POST":
					return await requestInfo.CreateModuleAsync(isSystemAdministrator, cancellationToken).ConfigureAwait(false);

				case "PUT":
					return await requestInfo.UpdateModuleAsync(isSystemAdministrator, cancellationToken).ConfigureAwait(false);

				case "DELETE":
					return await requestInfo.DeleteModuleAsync(isSystemAdministrator, cancellationToken).ConfigureAwait(false);

				default:
					throw new MethodNotAllowedException(requestInfo.Verb);
			}
		}

		async Task<JObject> ProcessContentTypeAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			var isSystemAdministrator = await this.IsSystemAdministratorAsync(requestInfo, cancellationToken).ConfigureAwait(false);
			switch (requestInfo.Verb)
			{
				case "GET":
					return "search".IsEquals(requestInfo.GetObjectIdentity())
						? await requestInfo.SearchContentTypesAsync(isSystemAdministrator, cancellationToken).ConfigureAwait(false)
						: await requestInfo.GetContentTypeAsync(isSystemAdministrator, cancellationToken).ConfigureAwait(false);

				case "POST":
					return await requestInfo.CreateContentTypeAsync(isSystemAdministrator, cancellationToken).ConfigureAwait(false);

				case "PUT":
					return await requestInfo.UpdateContentTypeAsync(isSystemAdministrator, cancellationToken).ConfigureAwait(false);

				case "DELETE":
					return await requestInfo.DeleteContentTypeAsync(isSystemAdministrator, cancellationToken).ConfigureAwait(false);

				default:
					throw new MethodNotAllowedException(requestInfo.Verb);
			}
		}

		async Task<JObject> ProcessExpressionAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			var isSystemAdministrator = await this.IsSystemAdministratorAsync(requestInfo, cancellationToken).ConfigureAwait(false);
			switch (requestInfo.Verb)
			{
				case "GET":
					return "search".IsEquals(requestInfo.GetObjectIdentity())
						? await requestInfo.SearchExpressionsAsync(isSystemAdministrator, cancellationToken).ConfigureAwait(false)
						: await requestInfo.GetExpressionAsync(isSystemAdministrator, cancellationToken).ConfigureAwait(false);

				case "POST":
					return await requestInfo.CreateExpressionAsync(isSystemAdministrator, cancellationToken).ConfigureAwait(false);

				case "PUT":
					return await requestInfo.UpdateExpressionAsync(isSystemAdministrator, cancellationToken).ConfigureAwait(false);

				case "DELETE":
					return await requestInfo.DeleteExpressionAsync(isSystemAdministrator, cancellationToken).ConfigureAwait(false);

				default:
					throw new MethodNotAllowedException(requestInfo.Verb);
			}
		}

		async Task<JToken> ProcessSchedulingTaskAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			var isSystemAdministrator = await this.IsSystemAdministratorAsync(requestInfo, cancellationToken).ConfigureAwait(false);
			switch (requestInfo.Verb)
			{
				case "GET":
					switch (requestInfo.GetObjectIdentity())
					{
						case "fetch":
							return await requestInfo.FetchSchedulingTaskAsync(cancellationToken).ConfigureAwait(false);

						case "search":
							return await requestInfo.SearchSchedulingTasksAsync(isSystemAdministrator, cancellationToken).ConfigureAwait(false);

						case "run":
							return await requestInfo.RunSchedulingTaskAsync(isSystemAdministrator, cancellationToken).ConfigureAwait(false);

						default:
							return await requestInfo.GetSchedulingTaskAsync(isSystemAdministrator, cancellationToken).ConfigureAwait(false);
					}

				case "POST":
					return await requestInfo.CreateSchedulingTaskAsync(isSystemAdministrator, cancellationToken).ConfigureAwait(false);

				case "PUT":
					return await requestInfo.UpdateSchedulingTaskAsync(isSystemAdministrator, cancellationToken).ConfigureAwait(false);

				case "DELETE":
					return await requestInfo.DeleteSchedulingTaskAsync(isSystemAdministrator, cancellationToken).ConfigureAwait(false);

				default:
					throw new MethodNotAllowedException(requestInfo.Verb);
			}
		}
		#endregion

		#region Process CMS Portals object
		async Task<JToken> ProcessCategoryAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			var isSystemAdministrator = await this.IsSystemAdministratorAsync(requestInfo, cancellationToken).ConfigureAwait(false);
			switch (requestInfo.Verb)
			{
				case "GET":
					return "search".IsEquals(requestInfo.GetObjectIdentity())
						? await requestInfo.SearchCategoriesAsync(isSystemAdministrator, cancellationToken).ConfigureAwait(false)
						: await requestInfo.GetCategoryAsync(isSystemAdministrator, cancellationToken).ConfigureAwait(false);

				case "POST":
					return await requestInfo.CreateCategoryAsync(isSystemAdministrator, cancellationToken).ConfigureAwait(false);

				case "PUT":
					return "order-index".IsEquals(requestInfo.GetHeaderParameter("x-update"))
						? await requestInfo.UpdateCategoriesAsync(isSystemAdministrator, cancellationToken).ConfigureAwait(false)
						: await requestInfo.UpdateCategoryAsync(isSystemAdministrator, cancellationToken).ConfigureAwait(false);

				case "DELETE":
					return await requestInfo.DeleteCategoryAsync(isSystemAdministrator, cancellationToken).ConfigureAwait(false);

				default:
					throw new MethodNotAllowedException(requestInfo.Verb);
			}
		}

		async Task<JToken> ProcessContentAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			var isSystemAdministrator = await this.IsSystemAdministratorAsync(requestInfo, cancellationToken).ConfigureAwait(false);
			switch (requestInfo.Verb)
			{
				case "GET":
					return "search".IsEquals(requestInfo.GetObjectIdentity())
						? await requestInfo.SearchContentsAsync(isSystemAdministrator, cancellationToken).ConfigureAwait(false)
						: await requestInfo.GetContentAsync(isSystemAdministrator, cancellationToken).ConfigureAwait(false);

				case "POST":
					return await requestInfo.CreateContentAsync(isSystemAdministrator, cancellationToken).ConfigureAwait(false);

				case "PUT":
					return await requestInfo.UpdateContentAsync(isSystemAdministrator, cancellationToken).ConfigureAwait(false);

				case "DELETE":
					return await requestInfo.DeleteContentAsync(isSystemAdministrator, cancellationToken).ConfigureAwait(false);

				default:
					throw new MethodNotAllowedException(requestInfo.Verb);
			}
		}

		async Task<JToken> ProcessItemAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			var isSystemAdministrator = await this.IsSystemAdministratorAsync(requestInfo, cancellationToken).ConfigureAwait(false);
			switch (requestInfo.Verb)
			{
				case "GET":
					return "search".IsEquals(requestInfo.GetObjectIdentity())
						? await requestInfo.SearchItemsAsync(isSystemAdministrator, cancellationToken).ConfigureAwait(false)
						: await requestInfo.GetItemAsync(isSystemAdministrator, cancellationToken).ConfigureAwait(false);

				case "POST":
					return await requestInfo.CreateItemAsync(isSystemAdministrator, cancellationToken).ConfigureAwait(false);

				case "PUT":
					return await requestInfo.UpdateItemAsync(isSystemAdministrator, cancellationToken).ConfigureAwait(false);

				case "DELETE":
					return await requestInfo.DeleteItemAsync(isSystemAdministrator, cancellationToken).ConfigureAwait(false);

				default:
					throw new MethodNotAllowedException(requestInfo.Verb);
			}
		}

		async Task<JToken> ProcessLinkAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			var isSystemAdministrator = await this.IsSystemAdministratorAsync(requestInfo, cancellationToken).ConfigureAwait(false);
			switch (requestInfo.Verb)
			{
				case "GET":
					return "search".IsEquals(requestInfo.GetObjectIdentity())
						? await requestInfo.SearchLinksAsync(isSystemAdministrator, cancellationToken).ConfigureAwait(false)
						: await requestInfo.GetLinkAsync(isSystemAdministrator, cancellationToken).ConfigureAwait(false);

				case "POST":
					return await requestInfo.CreateLinkAsync(isSystemAdministrator, cancellationToken).ConfigureAwait(false);

				case "PUT":
					return "order-index".IsEquals(requestInfo.GetHeaderParameter("x-update"))
						? await requestInfo.UpdateLinksAsync(isSystemAdministrator, cancellationToken).ConfigureAwait(false)
						: await requestInfo.UpdateLinkAsync(isSystemAdministrator, cancellationToken).ConfigureAwait(false);

				case "DELETE":
					return await requestInfo.DeleteLinkAsync(isSystemAdministrator, cancellationToken).ConfigureAwait(false);

				default:
					throw new MethodNotAllowedException(requestInfo.Verb);
			}
		}

		async Task<JToken> ProcessFormAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			var isSystemAdministrator = await this.IsSystemAdministratorAsync(requestInfo, cancellationToken).ConfigureAwait(false);
			switch (requestInfo.Verb)
			{
				case "GET":
					return "search".IsEquals(requestInfo.GetObjectIdentity())
						? await requestInfo.SearchFormsAsync(isSystemAdministrator, cancellationToken).ConfigureAwait(false)
						: await requestInfo.GetFormAsync(isSystemAdministrator, cancellationToken).ConfigureAwait(false);

				case "POST":
					return await requestInfo.CreateFormAsync(isSystemAdministrator, cancellationToken).ConfigureAwait(false);

				case "PUT":
					return await requestInfo.UpdateFormAsync(isSystemAdministrator, cancellationToken).ConfigureAwait(false);

				case "DELETE":
					return await requestInfo.DeleteFormAsync(isSystemAdministrator, cancellationToken).ConfigureAwait(false);

				default:
					throw new MethodNotAllowedException(requestInfo.Verb);
			}
		}

		async Task<JToken> ProcessCrawlerAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			var isSystemAdministrator = await this.IsSystemAdministratorAsync(requestInfo, cancellationToken).ConfigureAwait(false);
			switch (requestInfo.Verb)
			{
				case "GET":
					return "search".IsEquals(requestInfo.GetObjectIdentity())
						? await requestInfo.SearchCrawlersAsync(isSystemAdministrator, cancellationToken).ConfigureAwait(false)
						: await requestInfo.GetCrawlerAsync(isSystemAdministrator, cancellationToken).ConfigureAwait(false);

				case "POST":
					return "test".IsEquals(requestInfo.GetObjectIdentity())
						? await requestInfo.TestCrawlerAsync(cancellationToken).ConfigureAwait(false)
						: "categories".IsEquals(requestInfo.GetObjectIdentity())
							? await requestInfo.FetchCrawlerCategoriesAsync(cancellationToken).ConfigureAwait(false) as JToken
							: await requestInfo.CreateCrawlerAsync(isSystemAdministrator, cancellationToken).ConfigureAwait(false);

				case "PUT":
					return await requestInfo.UpdateCrawlerAsync(isSystemAdministrator, cancellationToken).ConfigureAwait(false);

				case "DELETE":
					return await requestInfo.DeleteCrawlerAsync(isSystemAdministrator, cancellationToken).ConfigureAwait(false);

				default:
					throw new MethodNotAllowedException(requestInfo.Verb);
			}
		}
		#endregion

		#region Process attachment files and desktops' templates
		Task<JToken> ProcessAttachmentFileAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			var systemID = requestInfo.GetParameter("SystemID") ?? requestInfo.GetParameter("x-system-id");
			var entityInfo = requestInfo.GetParameter("RepositoryEntityID") ?? requestInfo.GetParameter("x-entity");
			var objectID = requestInfo.GetParameter("ObjectID") ?? requestInfo.GetParameter("x-object-id");
			var objectTitle = requestInfo.GetParameter("ObjectTitle") ?? requestInfo.GetParameter("x-object-title");

			if (requestInfo.Verb.IsEquals("PATCH"))
				return this.MarkFilesAsOfficialAsync(requestInfo, systemID, entityInfo, objectID, objectTitle, cancellationToken);

			else if (requestInfo.Verb.IsEquals("GET"))
				switch ((requestInfo.GetObjectIdentity() ?? "").ToLower())
				{
					case "thumbnail":
					case "thumbnails":
						return this.GetThumbnailsAsync(requestInfo, objectID, objectTitle, cancellationToken);

					case "attachment":
					case "attachments":
						return this.GetAttachmentsAsync(requestInfo, objectID, objectTitle, cancellationToken);

					default:
						return this.GetFilesAsync(requestInfo, objectID, objectTitle, cancellationToken);
				}
			else
				return Task.FromException<JToken>(new MethodNotAllowedException(requestInfo.Verb));
		}

		async Task<JToken> ProcessTemplateAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			var request = requestInfo.GetRequestExpando();
			if ("Zones".IsEquals(request.Get<string>("Mode")))
			{
				var desktop = await request.Get("DesktopID", "").GetDesktopByIDAsync(cancellationToken).ConfigureAwait(false);
				return desktop != null
					? (await desktop.GetTemplateAsync(cancellationToken).ConfigureAwait(false)).GetXDocument().GetZoneNames().ToJArray()
					: new JArray();
			}

			var filename = request.Get<string>("Name");
			var theme = request.Get<string>("Theme");
			var mainDirectory = request.Get<string>("MainDirectory");
			var subDirectory = request.Get<string>("SubDirectory");
			var template = await Utility.GetTemplateAsync(filename, theme, mainDirectory, subDirectory, cancellationToken).ConfigureAwait(false);
			if (string.IsNullOrWhiteSpace(template) && !"default".IsEquals(theme))
				template = await Utility.GetTemplateAsync(filename, null, mainDirectory, subDirectory, cancellationToken).ConfigureAwait(false);

			return new JObject
			{
				{ "Template", template }
			};
		}
		#endregion

		async Task<JToken> IdentifySystemAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			var identity = requestInfo.GetParameter("x-system");
			var host = requestInfo.GetParameter("x-host");

			var organization = await (identity ?? "").GetOrganizationByAliasAsync(cancellationToken).ConfigureAwait(false);
			var site = await (host ?? "").GetSiteByDomainAsync(cancellationToken).ConfigureAwait(false);

			if (site == null)
			{
				site = organization?.DefaultSite;
				if (site == null && !string.IsNullOrWhiteSpace(host) && !Utility.NotRecognizedAliases.Contains($"Site:{host}"))
					site = Utility.DefaultSite;
			}
			else
				site = site.Prepare(host, false);

			organization = organization ?? site?.Organization;
			if (organization != null && requestInfo.GetParameter("x-force-refresh") != null)
				await organization.RefreshAsync(cancellationToken).ConfigureAwait(false);

			return organization != null
				? new JObject
				{
					{ "ID", organization.ID },
					{ "Alias", organization.Alias },
					{ "HomeDesktopAlias", (site?.HomeDesktop ?? organization.HomeDesktop ?? organization.DefaultDesktop)?.Alias ?? "-default" },
					{ "FilesHttpURI", this.GetFilesHttpURI(organization) },
					{ "PortalsHttpURI", this.GetPortalsHttpURI(organization) },
					{ "CmsPortalsHttpURI", Utility.CmsPortalsHttpURI },
					{ "AlwaysUseHtmlSuffix", organization.AlwaysUseHtmlSuffix },
					{ "AlwaysUseHTTPs", site != null && site.AlwaysUseHTTPs },
					{ "AlwaysReturnHTTPs", site != null && site.AlwaysReturnHTTPs },
					{ "RedirectToNoneWWW", site != null && site.RedirectToNoneWWW },
					{ "Language", requestInfo.GetParameter("Language") ?? site?.Language ?? "en-US" }
				}
				: throw new SiteNotRecognizedException($"The requested site is not recognized ({(string.IsNullOrWhiteSpace(host) ? "unknown" : host)})");
		}

		Task<JToken> ProcessHttpRequestAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
			=> requestInfo.Query.ContainsKey("x-indicator")
				? this.ProcessHttpIndicatorRequestAsync(requestInfo, cancellationToken)
				: requestInfo.Query.ContainsKey("x-resource")
					? this.ProcessHttpResourceRequestAsync(requestInfo, cancellationToken)
					: this.ProcessHttpDesktopRequestAsync(requestInfo, cancellationToken);

		#region Process resource requests of Portals HTTP service
		HashSet<string> DontCacheThemes => UtilityService.GetAppSetting("Portals:Desktops:Resources:DontCacheThemes", "").Trim().ToLower().ToHashSet();

		HashSet<string> DontMinifyJsThemes => (UtilityService.GetAppSetting("Portals:Desktops:Resources:DontMinifyJsThemes") ?? UtilityService.GetAppSetting("Portals:Desktops:Resources:DontMinifyThemes", "")).Trim().ToLower().ToHashSet();

		HashSet<string> DontMinifyCssThemes => (UtilityService.GetAppSetting("Portals:Desktops:Resources:DontMinifyCssThemes") ?? UtilityService.GetAppSetting("Portals:Desktops:Resources:DontMinifyThemes", "")).Trim().ToLower().ToHashSet();

		bool CacheDesktopResources => "true".IsEquals(UtilityService.GetAppSetting("Portals:Desktops:Resources:Cache", "true"));

		bool CacheDesktopHtmls => "true".IsEquals(UtilityService.GetAppSetting("Portals:Desktops:Htmls:Cache", "true"));

		bool AllowSrcResourceFiles => "true".IsEquals(UtilityService.GetAppSetting("Portals:Desktops:Resources:AllowSrcFiles", "true"));

		bool AllowPreconnect => "true".IsEquals(UtilityService.GetAppSetting("Portals:Desktops:Resources:AllowPreconnect", "true"));

		bool RemoveDesktopHtmlWhitespaces => "true".IsEquals(UtilityService.GetAppSetting("Portals:Desktops:Htmls:RemoveWhitespaces", "true"));

		string BodyEncoding => UtilityService.GetAppSetting("Portals:Desktops:Body:Encoding", "br");

		string GetPortalsHttpURI(IPortalObject @object = null)
		{
			var httpURI = @object is Organization organization
				? organization?.FakePortalsHttpURI
				: (@object?.OrganizationID ?? "").GetOrganizationByID()?.FakePortalsHttpURI;
			httpURI = string.IsNullOrWhiteSpace(httpURI)
				? Utility.PortalsHttpURI ?? this.GetHttpURI("Portals", "https://portals.vieapps.net")
				: httpURI;
			while (httpURI.EndsWith("/"))
				httpURI = httpURI.Left(httpURI.Length - 1);
			return string.IsNullOrWhiteSpace(httpURI)
				? Utility.PortalsHttpURI ?? this.GetHttpURI("Portals", "https://portals.vieapps.net")
				: httpURI;
		}

		string GetFilesHttpURI(IPortalObject @object = null)
		{
			var httpURI = @object is Organization organization
				? organization?.FakeFilesHttpURI
				: (@object?.OrganizationID ?? "").GetOrganizationByID()?.FakeFilesHttpURI;
			httpURI = string.IsNullOrWhiteSpace(httpURI)
				? Utility.FilesHttpURI ?? this.GetHttpURI("Files", "https://fs.vieapps.net")
				: httpURI;
			while (httpURI.EndsWith("/"))
				httpURI = httpURI.Left(httpURI.Length - 1);
			return string.IsNullOrWhiteSpace(httpURI)
				? Utility.FilesHttpURI ?? this.GetHttpURI("Files", "https://fs.vieapps.net")
				: httpURI;
		}

		async Task<JToken> ProcessHttpIndicatorRequestAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			await this.WriteLogsAsync(requestInfo.CorrelationID, $"Process HTTP indicator => {requestInfo.GetHeaderParameter("x-url")}", null, this.ServiceName, "Process.Http.Request").ConfigureAwait(false);

			var organization = await (requestInfo.GetParameter("x-system") ?? "").GetOrganizationByAliasAsync(cancellationToken).ConfigureAwait(false);
			if (organization == null)
				throw new InformationNotFoundException();

			var name = $"{requestInfo.Query["x-indicator"]}.txt";
			var indicator = organization.HttpIndicators?.FirstOrDefault(httpIndicator => httpIndicator.Name.IsEquals(name));
			return indicator != null
				? new JObject
				{
					{ "StatusCode", (int)HttpStatusCode.OK },
					{ "Headers", new Dictionary<string, string>
						{
							{ "Content-Type", "text/plain; charset=utf-8" },
							{ "X-Correlation-ID", requestInfo.CorrelationID }
						}.ToJson()
					},
					{ "Body", indicator.Content.Compress(this.BodyEncoding) },
					{ "BodyEncoding", this.BodyEncoding }
				}
				: throw new InformationNotFoundException();
		}

		async Task<JToken> ProcessHttpResourceRequestAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			// prepare
			var uri = new Uri(requestInfo.GetParameter("x-url"));
			await this.WriteLogsAsync(requestInfo.CorrelationID, $"Process HTTP resource => {uri}", null, this.ServiceName, "Process.Http.Request").ConfigureAwait(false);

			// get the type of the resource
			var type = requestInfo.Query.Get("x-resource", "assets");

			// permanent link
			if (type.IsEquals("permanentlink") || type.IsEquals("permanently") || type.IsEquals("permanent"))
			{
				// prepare
				if (!requestInfo.Query.TryGetValue("x-path", out var info) || string.IsNullOrWhiteSpace(info))
					throw new InvalidRequestException();

				var link = info.Replace(StringComparison.OrdinalIgnoreCase, ".html", "").Replace(StringComparison.OrdinalIgnoreCase, ".aspx", "").Replace(StringComparison.OrdinalIgnoreCase, ".php", "").ToArray("/");
				var contentType = link.Length > 1 ? await link[link.Length - 2].GetContentTypeByIDAsync(cancellationToken).ConfigureAwait(false) : null;
				if (contentType == null)
					throw new InvalidRequestException();

				var @object = await RepositoryMediator.GetAsync(contentType.ID, link[link.Length - 1], cancellationToken).ConfigureAwait(false);
				var url = @object != null
					? @object is IBusinessObject businessObject
						? businessObject.GetURL()
						: throw new InvalidRequestException()
					: throw new InformationNotFoundException();

				if (string.IsNullOrWhiteSpace(url) || !(businessObject.Organization is Organization organization))
					throw new InvalidRequestException();

				// response
				return new JObject
				{
					{ "StatusCode", (int)HttpStatusCode.Redirect },
					{ "Headers", new JObject { ["Location"] = url.NormalizeURLs(uri, organization.Alias, false, true, null, null, requestInfo.GetHeaderParameter("x-srp-host")) } }
				};
			}

			// prepare required info
			string identity = null;
			var isThemeResource = false;
			var filePath = requestInfo.GetQueryParameter("x-path") ?? "";
			var filePaths = filePath.ToList("/", true, true);

			if (type.IsStartsWith("theme"))
			{
				isThemeResource = true;
				type = filePaths.Count > 1
					? filePaths[1].IsStartsWith("css")
						? "css"
						: filePaths[1].IsStartsWith("js") || filePaths[1].IsStartsWith("javascript") || filePaths[1].IsStartsWith("script")
							? "js"
							: filePaths[1].IsStartsWith("img") || filePaths[1].IsStartsWith("image") ? "images" : filePaths[1].IsStartsWith("font") ? "fonts" : ""
					: "";
				if (!type.IsEquals("images") && !type.IsEquals("fonts"))
					identity = filePaths.Count > 0 ? filePaths[0] : null;
			}

			else if (type.IsStartsWith("css") || type.IsStartsWith("js") || type.IsStartsWith("javascript") || type.IsStartsWith("script"))
			{
				type = type.IsStartsWith("css") ? "css" : "js";
				identity = filePaths[0].Replace(StringComparison.OrdinalIgnoreCase, $".min.{type}", "").Replace(StringComparison.OrdinalIgnoreCase, $".{type}", "").ToLower().Trim();
			}

			else
				type = type.IsStartsWith("img") || type.IsStartsWith("image")
					? "images"
					: type.IsStartsWith("font") ? "fonts" : type;

			// fake URIs
			string filesHttpURI = null, portalsHttpURI = null;

			// special headers
			var forceCacheRequested = requestInfo.GetParameter("x-force-cache") != null;
			var noneMatch = requestInfo.GetHeaderParameter("If-None-Match");
			var modifiedSince = requestInfo.GetHeaderParameter("If-Modified-Since") ?? requestInfo.GetHeaderParameter("If-Unmodified-Since");
			var eTag = (type.IsEquals("css") || type.IsEquals("js")) && (isThemeResource || (identity != null && identity.Length == 34 && identity.Right(32).IsValidUUID()))
				? $"{type}#{identity}"
				: $"v#{uri.AbsolutePath.ToLower().GenerateUUID()}";

			// check special headers to reduce traffict
			var lastModified = this.CacheDesktopResources && !forceCacheRequested ? await Utility.Cache.GetAsync<string>($"{eTag}:time", cancellationToken).ConfigureAwait(false) : null;
			if (this.CacheDesktopResources && lastModified == null && (type.IsEquals("css") || type.IsEquals("js")))
			{
				if (identity != null && identity.Length == 34 && identity.Right(32).IsValidUUID())
				{
					if (identity.IsStartsWith("o_"))
					{
						var organization = await identity.Right(32).GetOrganizationByIDAsync(cancellationToken).ConfigureAwait(false);
						filesHttpURI = this.GetFilesHttpURI(organization);
						portalsHttpURI = this.GetPortalsHttpURI(organization);
						lastModified = organization?.LastModified.ToHttpString();
					}
					else if (identity.IsStartsWith("s_"))
					{
						var site = await identity.Right(32).GetSiteByIDAsync(cancellationToken).ConfigureAwait(false);
						filesHttpURI = this.GetFilesHttpURI(site);
						portalsHttpURI = this.GetPortalsHttpURI(site);
						lastModified = site?.LastModified.ToHttpString();
					}
					else if (identity.IsStartsWith("d_"))
					{
						var desktop = await identity.Right(32).GetDesktopByIDAsync(cancellationToken).ConfigureAwait(false);
						filesHttpURI = this.GetFilesHttpURI(desktop);
						portalsHttpURI = this.GetPortalsHttpURI(desktop);
						lastModified = desktop?.LastModified.ToHttpString();
					}
				}
				else if (isThemeResource)
					lastModified = this.GetThemeResourcesLastModified(identity, type).ToHttpString();

				if (lastModified != null)
					await Task.WhenAll
					(
						Utility.Cache.SetAsync($"{eTag}:time", lastModified, cancellationToken),
						Utility.Cache.AddSetMemberAsync("statics" + (isThemeResource ? $":{identity}" : ""), $"{eTag}:time", cancellationToken)
					).ConfigureAwait(false);
			}

			if (this.CacheDesktopResources && eTag.IsEquals(noneMatch) && modifiedSince != null && lastModified != null && modifiedSince.FromHttpDateTime() >= lastModified.FromHttpDateTime())
				return new JObject
				{
					{ "StatusCode", (int)HttpStatusCode.NotModified },
					{ "Headers", new Dictionary<string, string>
						{
							{ "X-Cache", "SVC-304" },
							{ "X-Correlation-ID", requestInfo.CorrelationID },
							{ "ETag", eTag },
							{ "Last-Modified", lastModified }
						}.ToJson()
					}
				};

			// get cached resources
			var resources = this.CacheDesktopResources && !forceCacheRequested ? await Utility.Cache.GetAsync<string>(eTag, cancellationToken).ConfigureAwait(false) : null;
			if (resources != null)
			{
				var contentType = "application/octet-stream";
				if (type.IsEquals("css"))
					contentType = "text/css";
				else if (type.IsEquals("js"))
					contentType = "application/javascript";
				else if (type.IsEquals("assets"))
					switch (filePath.ToList(".").Last())
					{
						case "js":
							contentType = "application/javascript";
							break;
						case "json":
							contentType = "application/json";
							break;
						case "css":
							contentType = "text/css";
							break;
						case "htm":
						case "html":
						case "xhtml":
							contentType = "text/html";
							break;
						case "xml":
							contentType = "text/xml";
							break;
					}
				else if (type.IsEquals("fonts"))
					contentType = $"font/{filePath.ToList(".").Last()}";
				else if (type.IsEquals("images"))
				{
					contentType = filePath.ToList(".").Last();
					contentType = $"image/{(contentType.IsEquals("svg") ? "svg+xml" : contentType.IsEquals("jpg") || contentType.IsEquals("jpeg") ? "jpeg" : contentType)}";
				}
				return new JObject
				{
					{ "StatusCode", (int)HttpStatusCode.OK },
					{ "Headers", new Dictionary<string, string>
						{
							{ "X-Cache", "SVC-200" },
							{ "X-Correlation-ID", requestInfo.CorrelationID },
							{ "Content-Type", $"{contentType}; charset=utf-8" },
							{ "ETag", eTag },
							{ "Last-Modified", lastModified },
							{ "Expires", DateTime.Now.AddDays(366).ToHttpString() },
							{ "Cache-Control", "public" }
						}.ToJson()
					},
					{ "Body", resources.Compress(this.BodyEncoding) },
					{ "BodyEncoding", this.BodyEncoding }
				};
			}

			// static files in 'assets' directory or image/font files of a theme
			if (type.IsEquals("assets") || type.IsEquals("images") || type.IsEquals("fonts"))
			{
				// prepare
				if (string.IsNullOrWhiteSpace(filePath))
					throw new InformationNotFoundException();

				var fileInfo = new FileInfo(Path.Combine(Utility.DataFilesDirectory, type.IsEquals("assets") ? type : "themes", filePath));
				if (!fileInfo.Exists)
					throw new InformationNotFoundException(filePath);

				var data = filePath.IsEndsWith(".css")
					? this.MinifyCss(await fileInfo.ReadAsTextAsync(cancellationToken).ConfigureAwait(false)).NormalizeURLs(portalsHttpURI ?? this.GetPortalsHttpURI(), filesHttpURI ?? this.GetFilesHttpURI()).ToBytes()
					: filePath.IsEndsWith(".js")
						? this.MinifyJs(await fileInfo.ReadAsTextAsync(cancellationToken).ConfigureAwait(false)).NormalizeURLs(portalsHttpURI ?? this.GetPortalsHttpURI(), filesHttpURI ?? this.GetFilesHttpURI()).ToBytes()
						: await fileInfo.ReadAsBinaryAsync(cancellationToken).ConfigureAwait(false);

				// response
				lastModified = fileInfo.LastWriteTime.ToHttpString();
				var contentType = fileInfo.GetMimeType();
				var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
				{
					{ "Content-Type", $"{contentType}; charset=utf-8" },
					{ "X-Cache", "None" },
					{ "X-Correlation-ID", requestInfo.CorrelationID }
				};

				if (this.CacheDesktopResources)
				{
					headers = new Dictionary<string, string>(headers, StringComparer.OrdinalIgnoreCase)
					{
						{ "ETag", eTag },
						{ "Last-Modified", lastModified },
						{ "Expires", DateTime.Now.AddDays(366).ToHttpString() },
						{ "Cache-Control", "public" }
					};
					resources = contentType.IsStartsWith("image/") || contentType.IsStartsWith("font/") ? data.ToBase64() : data.GetString();
					await Task.WhenAll
					(
						Utility.Cache.SetAsFragmentsAsync(eTag, resources, cancellationToken),
						Utility.Cache.SetAsync($"{eTag}:time", lastModified, cancellationToken),
						Utility.Cache.AddSetMembersAsync("statics" + (isThemeResource ? $":{identity}" : ""), new[] { eTag, $"{eTag}:time" }, cancellationToken)
					).ConfigureAwait(false);
				}

				return new JObject
				{
					{ "StatusCode", (int)HttpStatusCode.OK },
					{ "Headers", headers.ToJson() },
					{ "Body", data.Compress(this.BodyEncoding).ToBase64() },
					{ "BodyEncoding", this.BodyEncoding }
				};
			}

			// css stylesheets
			if (type.IsEquals("css"))
			{
				// prepare
				if (string.IsNullOrWhiteSpace(identity))
					throw new InvalidRequestException($"The request is invalid [({requestInfo.Verb}): {requestInfo.GetURI()}]");

				if (identity.Length == 34 && identity.Right(32).IsValidUUID())
				{
					if (identity.IsStartsWith("s_"))
					{
						var site = await identity.Right(32).GetSiteByIDAsync(cancellationToken).ConfigureAwait(false);
						filesHttpURI = this.GetFilesHttpURI(site);
						portalsHttpURI = this.GetPortalsHttpURI(site);
						resources = site != null
							? (this.IsDebugLogEnabled ? $"/* css of the '{site.Title}' site */\r\n" : "") + (string.IsNullOrWhiteSpace(site.Stylesheets) ? "" : this.MinifyCss(site.Stylesheets, filePath.IsEndsWith($".min.{type}") ? null : site.WorkingTheme))
							: $"/* css of the site ({identity}) is not found */";
					}
					else if (identity.IsStartsWith("d_"))
					{
						var desktop = await identity.Right(32).GetDesktopByIDAsync(cancellationToken).ConfigureAwait(false);
						filesHttpURI = this.GetFilesHttpURI(desktop);
						portalsHttpURI = this.GetPortalsHttpURI(desktop);
						resources = desktop != null
							? (this.IsDebugLogEnabled ? $"/* css of the '{desktop.Title}' desktop */\r\n" : "") + (string.IsNullOrWhiteSpace(desktop.Stylesheets) ? "" : this.MinifyCss(desktop.Stylesheets, filePath.IsEndsWith($".min.{type}") ? null : desktop.WorkingTheme))
							: $"/* css of the desktop ({identity}) is not found */";
					}
					else
						resources = $"/* css ({identity}) is not found */";
				}
				else
				{
					if (type.IsEquals(requestInfo.Query.Get("x-resource")))
					{
						var fileInfo = new FileInfo(Path.Combine(Utility.DataFilesDirectory, "themes", filePaths.First(), type, filePaths.Skip(type.IsEquals(filePaths[1]) ? 2 : 1).Join("/").Replace(StringComparison.OrdinalIgnoreCase, $".min.{type}", $".{type}") + (filePaths.Last().IsEndsWith($".{type}") ? "" : $".{type}")));
						if (fileInfo.Exists)
						{
							resources = this.MinifyCss(await fileInfo.ReadAsTextAsync(cancellationToken).ConfigureAwait(false), filePath.IsEndsWith($".min.{type}") ? null : filePaths.First());
							lastModified = fileInfo.LastWriteTime.ToHttpString();
						}
						else
						{
							resources = $"/* css ({filePath}) is not found */";
							lastModified = DateTime.Now.GetTimeQuarter().ToHttpString();
						}
					}
					else
					{
						resources = await this.GetThemeResourcesAsync(identity, "css", cancellationToken).ConfigureAwait(false);
						lastModified = this.GetThemeResourcesLastModified(identity, type).ToHttpString();
					}
				}

				// response
				resources = resources.NormalizeURLs(portalsHttpURI ?? this.GetPortalsHttpURI(), filesHttpURI ?? this.GetFilesHttpURI());
				var headers = new Dictionary<string, string>
				{
					{ "Content-Type", "text/css; charset=utf-8" },
					{ "X-Cache", "None" },
					{ "X-Correlation-ID", requestInfo.CorrelationID }
				};

				if (this.CacheDesktopResources && ((identity.Length == 34 && identity.Right(32).IsValidUUID()) || !this.DontCacheThemes.Contains(identity)))
				{
					headers = new Dictionary<string, string>(headers, StringComparer.OrdinalIgnoreCase)
					{
						{ "ETag", eTag },
						{ "Last-Modified", lastModified },
						{ "Expires", DateTime.Now.AddDays(366).ToHttpString() },
						{ "Cache-Control", "public" }
					};
					await Task.WhenAll
					(
						Utility.Cache.SetAsync(eTag, resources, cancellationToken),
						Utility.Cache.SetAsync($"{eTag}:time", lastModified, cancellationToken),
						Utility.Cache.AddSetMembersAsync("statics" + (isThemeResource ? $":{identity}" : ""), new[] { eTag, $"{eTag}:time" }, cancellationToken)
					).ConfigureAwait(false);
				}

				return new JObject
				{
					{ "StatusCode", (int)HttpStatusCode.OK },
					{ "Headers", headers.ToJson() },
					{ "Body", resources.Compress(this.BodyEncoding) },
					{ "BodyEncoding", this.BodyEncoding }
				};
			}

			// javascripts
			if (type.IsEquals("js"))
			{
				// prepare
				if (string.IsNullOrWhiteSpace(identity))
					throw new InvalidRequestException($"The request is invalid [({requestInfo.Verb}): {requestInfo.GetURI()}]");

				if (identity.Length == 34 && identity.Right(32).IsValidUUID())
				{
					if (identity.IsStartsWith("o_"))
					{
						var organization = await identity.Right(32).GetOrganizationByIDAsync(cancellationToken).ConfigureAwait(false);
						filesHttpURI = this.GetFilesHttpURI(organization);
						portalsHttpURI = this.GetPortalsHttpURI(organization);
						lastModified = lastModified ?? organization?.LastModified.ToHttpString();
						resources = organization != null
							? (this.IsDebugLogEnabled ? $"/* scripts of the '{organization.Title}' organization */\r\n" : "") + this.MinifyJs(organization.Javascripts, filePath.IsEndsWith($".min.{type}") ? null : organization.Theme)
							: $"/* scripts of the organization ({identity.Right(32)}) is not found */";
					}
					else if (identity.IsStartsWith("s_"))
					{
						var site = await identity.Right(32).GetSiteByIDAsync(cancellationToken).ConfigureAwait(false);
						filesHttpURI = this.GetFilesHttpURI(site);
						portalsHttpURI = this.GetPortalsHttpURI(site);
						lastModified = lastModified ?? site?.LastModified.ToHttpString();
						resources = site != null
							? (this.IsDebugLogEnabled ? $"/* scripts of the '{site.Title}' site */\r\n" : "") + (string.IsNullOrWhiteSpace(site.Scripts) ? "" : this.MinifyJs(site.Scripts, filePath.IsEndsWith($".min.{type}") ? null : site.WorkingTheme))
							: $"/* scripts of the site ({identity.Right(32)}) is not found */";
					}
					else if (identity.IsStartsWith("d_"))
					{
						var desktop = await identity.Right(32).GetDesktopByIDAsync(cancellationToken).ConfigureAwait(false);
						filesHttpURI = this.GetFilesHttpURI(desktop);
						portalsHttpURI = this.GetPortalsHttpURI(desktop);
						lastModified = lastModified ?? desktop?.LastModified.ToHttpString();
						resources = desktop != null
							? (this.IsDebugLogEnabled ? $"/* scripts of the '{desktop.Title}' desktop */\r\n" : "") + (string.IsNullOrWhiteSpace(desktop.Scripts) ? "" : this.MinifyJs(desktop.Scripts, filePath.IsEndsWith($".min.{type}") ? null : desktop.WorkingTheme))
							: $"/* scripts of the desktop ({identity.Right(32)}) is not found */";
					}
					else
						resources = $"/* scripts ({identity}) is not found */";
				}
				else
				{
					if (type.IsEquals(requestInfo.Query.Get("x-resource")))
					{
						var fileInfo = new FileInfo(Path.Combine(Utility.DataFilesDirectory, "themes", filePaths.First(), type, filePaths.Skip(type.IsEquals(filePaths[1]) ? 2 : 1).Join("/").Replace(StringComparison.OrdinalIgnoreCase, $".min.{type}", $".{type}") + (filePaths.Last().IsEndsWith($".{type}") ? "" : $".{type}")));
						if (fileInfo.Exists)
						{
							resources = this.MinifyJs(await fileInfo.ReadAsTextAsync(cancellationToken).ConfigureAwait(false), filePath.IsEndsWith($".min.{type}") ? null : filePaths.First());
							lastModified = fileInfo.LastWriteTime.ToHttpString();
						}
						else
						{
							resources = $"/* scripts ({filePath}) is not found */";
							lastModified = DateTime.Now.GetTimeQuarter().ToHttpString();
						}
					}
					else
					{
						resources = await this.GetThemeResourcesAsync(identity, "js", cancellationToken).ConfigureAwait(false);
						lastModified = this.GetThemeResourcesLastModified(identity, type).ToHttpString();
					}
				}

				// response
				resources = resources.NormalizeURLs(portalsHttpURI ?? this.GetPortalsHttpURI(), filesHttpURI ?? this.GetFilesHttpURI());
				var headers = new Dictionary<string, string>
				{
					{ "Content-Type", "application/javascript; charset=utf-8" },
					{ "X-Cache", "None" },
					{ "X-Correlation-ID", requestInfo.CorrelationID }
				};

				if (this.CacheDesktopResources && ((identity.Length == 34 && identity.Right(32).IsValidUUID()) || !this.DontCacheThemes.Contains(identity)))
				{
					headers = new Dictionary<string, string>(headers, StringComparer.OrdinalIgnoreCase)
					{
						{ "ETag", eTag },
						{ "Last-Modified", lastModified },
						{ "Expires", DateTime.Now.AddDays(366).ToHttpString() },
						{ "Cache-Control", "public" }
					};
					await Task.WhenAll
					(
						Utility.Cache.SetAsync(eTag, resources, cancellationToken),
						Utility.Cache.SetAsync($"{eTag}:time", lastModified, cancellationToken),
						Utility.Cache.AddSetMembersAsync("statics" + (isThemeResource ? $":{identity}" : ""), new[] { eTag, $"{eTag}:time" }, cancellationToken)
					).ConfigureAwait(false);
				}

				return new JObject
				{
					{ "StatusCode", (int)HttpStatusCode.OK },
					{ "Headers", headers.ToJson() },
					{ "Body", resources.Compress(this.BodyEncoding) },
					{ "BodyEncoding", this.BodyEncoding }
				};
			}

			// unknown
			throw new InformationNotFoundException($"The requested resource is not found [{requestInfo.GetURI()}]");
		}

		async Task<string> GetThemeResourcesAsync(string theme, string type, CancellationToken cancellationToken)
		{
			var isJavascript = type.IsEquals("js");
			var resources = this.IsDebugLogEnabled ? $"/* {(isJavascript ? "scripts" : "stylesheets")} of the '{theme}' theme */\r\n" : "";
			var directory = new DirectoryInfo(Path.Combine(Utility.DataFilesDirectory, "themes", theme, type));
			if (directory.Exists)
				await directory.GetFiles($"*.{type}").OrderBy(fileInfo => fileInfo.Name).ForEachAsync(async fileInfo =>
				{
					var resource = await fileInfo.ReadAsTextAsync(cancellationToken).ConfigureAwait(false);
					resources += (isJavascript ? ";" : "")
						+ (this.IsDebugLogEnabled ? $"\r\n/* {fileInfo.FullName} */\r\n" : "")
						+ (isJavascript ? this.MinifyJs(resource, theme) : this.MinifyCss(resource, theme))
						+ "\r\n";
				}, true, false).ConfigureAwait(false);
			return resources;
		}

		DateTime GetThemeResourcesLastModified(string theme, string type)
		{
			var lastModified = DateTimeService.CheckingDateTime;
			var directory = new DirectoryInfo(Path.Combine(Utility.DataFilesDirectory, "themes", theme, type));
			if (directory.Exists)
			{
				var files = directory.GetFiles($"*.{type}");
				lastModified = files.Any() ? files.OrderByDescending(fileInfo => fileInfo.LastWriteTime).First().LastWriteTime : directory.LastWriteTime;
			}
			return lastModified;
		}

		string MinifyJs(string resource, string theme = null)
			=> !string.IsNullOrWhiteSpace(theme) && this.DontMinifyJsThemes.Contains(theme) ? resource : resource.MinifyJs();

		string MinifyCss(string resource, string theme = null)
			=> !string.IsNullOrWhiteSpace(theme) && this.DontMinifyCssThemes.Contains(theme) ? resource : resource.MinifyCss();
		#endregion

		#region Process desktop requests of Portals HTTP service
		async Task<JToken> ProcessHttpDesktopRequestAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			// prepare required information
			var identity = requestInfo.GetParameter("x-system");
			if (string.IsNullOrWhiteSpace(identity))
				throw new InvalidRequestException($"The request is invalid [({requestInfo.Verb}): {requestInfo.GetURI()}]");
			var stopwatch = Stopwatch.StartNew();
			var organization = await (identity.IsValidUUID() ? identity.GetOrganizationByIDAsync(cancellationToken) : identity.GetOrganizationByAliasAsync(cancellationToken)).ConfigureAwait(false);
			if (organization == null || string.IsNullOrWhiteSpace(organization.ID))
				throw new InvalidRequestException($"The request is invalid [({requestInfo.Verb}): {requestInfo.GetURI()}]");

			// prepare sites and desktops (at the first-time only)
			if (SiteProcessor.Sites.IsEmpty)
			{
				var sites = await organization.ID.ReloadSitesAsync(cancellationToken).ConfigureAwait(false);
				organization._siteIDs = sites.Select(website => website.ID).ToList();
				await organization.SetAsync(false, true, cancellationToken).ConfigureAwait(false);
			}

			if (DesktopProcessor.Desktops.IsEmpty || !DesktopProcessor.Desktops.Any(kvp => kvp.Value.SystemID == organization.ID))
			{
				var filter = Filters<Desktop>.And(Filters<Desktop>.Equals("SystemID", organization.ID), Filters<Desktop>.IsNull("ParentID"));
				var sort = Sorts<Desktop>.Ascending("Title");
				var desktops = await Desktop.FindAsync(filter, sort, 0, 1, Extensions.GetCacheKey(filter, sort, 0, 1), cancellationToken).ConfigureAwait(false);
				await desktops.ForEachAsync(async webdesktop => await webdesktop.SetAsync(false, true, cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
			}

			// get site
			var host = requestInfo.GetParameter("x-host");
			var site = await (host ?? "").GetSiteByDomainAsync(cancellationToken).ConfigureAwait(false);

			if (site == null)
			{
				if (!string.IsNullOrWhiteSpace(Utility.CmsPortalsHttpURI) && new Uri(Utility.CmsPortalsHttpURI).Host.IsEquals(host) && (organization._siteIDs == null || !organization._siteIDs.Any()))
				{
					organization._siteIDs = null;
					await organization.FindSitesAsync(cancellationToken).ConfigureAwait(false);
					site = organization.DefaultSite ?? Utility.DefaultSite;
				}
				else
				{
					site = organization.DefaultSite;
					if (site == null && !Utility.NotRecognizedAliases.Contains($"Site:{host}"))
					{
						if (string.IsNullOrWhiteSpace(organization.FakePortalsHttpURI))
							site = Utility.DefaultSite;
						else
						{
							Utility.NotRecognizedAliases.Add($"Site:{new Uri(organization.FakePortalsHttpURI).Host}");
							if (!Utility.NotRecognizedAliases.Contains($"Site:{host}"))
								site = Utility.DefaultSite;
						}
					}
				}
			}

			// normalize & check site
			site = site != null && !organization.ID.IsEquals(site.OrganizationID) ? organization.DefaultSite : site;
			if (site?.Prepare(host, false) == null)
				throw new SiteNotRecognizedException($"The requested site is not recognized ({host ?? "unknown"}){(this.IsDebugLogEnabled ? $" because the organization ({organization.Title}) has no site [{organization.Sites?.Count}]" : "")}");

			// get desktop and prepare the redirecting url
			var useShortURLs = "true".IsEquals(requestInfo.GetParameter("x-use-short-urls"));
			var requestURI = new Uri(requestInfo.GetParameter("x-url") ?? requestInfo.GetParameter("x-uri"));
			var requestURL = requestURI.AbsoluteUri;
			var redirectURL = "";

			var alias = requestInfo.GetParameter("x-desktop");
			var desktop = "-default".IsEquals(alias)
				? site.HomeDesktop ?? organization.DefaultDesktop
				: await organization.ID.GetDesktopByAliasAsync(alias, cancellationToken).ConfigureAwait(false);

			// prepare redirect URL when the desktop is not found
			if (desktop == null)
			{
				redirectURL = organization.GetRedirectURL(requestURI.AbsoluteUri) ?? organization.GetRedirectURL($"~{requestURI.PathAndQuery}".Replace($"/~{organization.Alias}/", "/"));
				if (string.IsNullOrWhiteSpace(redirectURL) && organization.RedirectUrls != null && organization.RedirectUrls.AllHttp404)
					redirectURL = organization.GetRedirectURL("*") ?? "~/index";

				if (string.IsNullOrWhiteSpace(redirectURL))
					throw new DesktopNotFoundException($"The requested desktop ({alias ?? "unknown"}) is not found");

				redirectURL += (organization.AlwaysUseHtmlSuffix && !redirectURL.IsEndsWith(".html") && !redirectURL.IsEndsWith(".aspx") && !redirectURL.IsEndsWith(".php") ? ".html" : "") + $"{requestURI.Query}{requestURI.Fragment}";
			}

			// normalize the redirectinng url
			if (site.AlwaysUseHTTPs || site.RedirectToNoneWWW)
			{
				if (string.IsNullOrWhiteSpace(redirectURL))
				{
					var redirectHost = requestInfo.GetHeaderParameter("x-srp-host") ?? requestURI.Host;
					redirectURL = (site.AlwaysUseHTTPs ? "https" : requestURI.Scheme) + "://" + (site.RedirectToNoneWWW ? redirectHost.Replace("www.", "") : redirectHost) + $"{requestURI.PathAndQuery}{requestURI.Fragment}";
				}
				else
				{
					if (site.AlwaysUseHTTPs)
						redirectURL = redirectURL.Replace("http://", "https://");
					if (site.RedirectToNoneWWW)
					{
						while (redirectURL.IsContains("://www."))
							redirectURL = redirectURL.Replace("://www.", "://");
					}
				}
			}

			// do redirect
			var writeDesktopLogs = requestInfo.IsWriteDesktopLogs();
			if (!string.IsNullOrWhiteSpace(redirectURL) && !requestURL.Equals(redirectURL))
			{
				if (writeDesktopLogs)
				{
					stopwatch.Stop();
					await this.WriteLogsAsync(requestInfo.CorrelationID, $"Redirect for matching with the settings - Execution times: {stopwatch.GetElapsedTimes()}\r\n{requestURL} => {redirectURL}", null, this.ServiceName, "Process.Http.Request").ConfigureAwait(false);
				}
				return new JObject
				{
					{ "StatusCode", (int)HttpStatusCode.Redirect },
					{ "Headers", new JObject { ["Location"] = redirectURL.NormalizeURLs(requestURI, organization.Alias, false, true, null, null, requestInfo.GetHeaderParameter("x-srp-host")) } }
				};
			}

			// start process
			var desktopInfo = $"the '{desktop.Title}' desktop [Alias: {desktop.Alias} - ID: {desktop.ID}]";
			await this.WriteLogsAsync(requestInfo.CorrelationID, $"Start to process {desktopInfo} of '{site.Title} [{organization.Title}]' => {requestURL}", null, this.ServiceName, "Process.Http.Request").ConfigureAwait(false);
			JObject response = null;

			// prepare the caching
			var cacheKey = desktop.GetDesktopCacheKey(requestURI);
			var cacheKeyOfLastModified = $"{cacheKey}:time";
			var cacheKeyOfExpiration = $"{cacheKey}:expiration";
			var processCache = this.CacheDesktopHtmls && requestInfo.GetParameter("x-no-cache") == null && requestInfo.GetParameter("x-force-cache") == null;

			// check "If-Modified-Since" request to reduce traffict
			var eTag = $"v#{cacheKey}";
			var noneMatch = processCache ? requestInfo.GetHeaderParameter("If-None-Match") : null;
			var modifiedSince = processCache ? requestInfo.GetHeaderParameter("If-Modified-Since") ?? requestInfo.GetHeaderParameter("If-Unmodified-Since") : null;
			var headers = new Dictionary<string, string>
			{
				{ "Content-Type", "text/html; charset=utf-8" },
				{ "X-Correlation-ID", requestInfo.CorrelationID }
			};

			string lastModified = null;
			if (processCache && modifiedSince != null && eTag.IsEquals(noneMatch))
			{
				lastModified = await Utility.Cache.GetAsync<string>(cacheKeyOfLastModified, cancellationToken).ConfigureAwait(false);
				if (!string.IsNullOrWhiteSpace(lastModified) && modifiedSince.FromHttpDateTime() >= lastModified.FromHttpDateTime())
				{
					headers = new Dictionary<string, string>(headers)
					{
						{ "X-Cache", "SVC-304" },
						{ "ETag", eTag },
						{ "Last-Modified", lastModified },
						{ "Cache-Control", "public" }
					};
					response = new JObject
					{
						{ "StatusCode", (int)HttpStatusCode.NotModified },
						{ "Headers", headers.ToJson() }
					};
					if (writeDesktopLogs)
					{
						stopwatch.Stop();
						await this.WriteLogsAsync(requestInfo.CorrelationID, $"By-pass the process of {desktopInfo} => Got 'If-Modified-Since'/'If-None-Match' request headers - ETag: {eTag} - Timestamp: {lastModified} - Execution times: {stopwatch.GetElapsedTimes()}", null, this.ServiceName, "Process.Http.Request").ConfigureAwait(false);
					}
					return response;
				}
			}

			// environment info
			var isMobile = $"{"true".IsEquals(requestInfo.GetHeaderParameter("x-environment-is-mobile"))}".ToLower();
			var osInfo = requestInfo.GetHeaderParameter("x-environment-os-info") ?? "Generic OS";

			// get cache of HTML
			var html = processCache
				? await Utility.Cache.GetAsync<string>(cacheKey, cancellationToken).ConfigureAwait(false)
				: null;

			// normalize the cache of HTML when got request from the refresher
			if (!string.IsNullOrWhiteSpace(html) && Utility.RefresherURL.IsEquals(requestInfo.GetHeaderParameter("Referer")))
			{
				// got specified expiration time => clear to refresh
				var expiresAt = await Utility.Cache.GetAsync<string>(cacheKeyOfExpiration, cancellationToken).ConfigureAwait(false);
				if (!string.IsNullOrWhiteSpace(expiresAt))
				{
					await Utility.Cache.RemoveAsync(new[] { cacheKey, cacheKeyOfLastModified }.Concat(new[] { DateTime.TryParse(expiresAt, out var expirationTime) ? "" : cacheKeyOfExpiration }).Where(key => !string.IsNullOrWhiteSpace(key)), cancellationToken).ConfigureAwait(false);
					html = null;
				}

				// no expiration => re-update cache
				else
				{
					lastModified = lastModified ?? await Utility.Cache.GetAsync<string>(cacheKeyOfLastModified, cancellationToken).ConfigureAwait(false) ?? DateTime.Now.ToHttpString();
					await Task.WhenAll
					(
						Utility.Cache.SetAsync(cacheKey, html, cancellationToken),
						Utility.Cache.SetAsync(cacheKeyOfLastModified, lastModified, cancellationToken)
					).ConfigureAwait(false);
				}
			}

			// response as cache of HTML
			if (!string.IsNullOrWhiteSpace(html))
			{
				html = this.NormalizeDesktopHtml(html, requestURI, useShortURLs, organization, site, desktop, isMobile, osInfo, requestInfo.CorrelationID);
				lastModified = lastModified ?? await Utility.Cache.GetAsync<string>(cacheKeyOfLastModified, cancellationToken).ConfigureAwait(false);
				if (string.IsNullOrWhiteSpace(lastModified))
				{
					lastModified = DateTime.Now.ToHttpString();
					await Utility.Cache.SetAsync(cacheKeyOfLastModified, lastModified, cancellationToken).ConfigureAwait(false);
				}
				var expiresAt = await Utility.Cache.GetAsync<string>(cacheKeyOfExpiration, cancellationToken).ConfigureAwait(false);
				expiresAt = !string.IsNullOrWhiteSpace(expiresAt) && DateTime.TryParse(expiresAt, out var expirationTime) ? expirationTime.ToHttpString() : DateTime.Now.AddMinutes(13).ToHttpString();
				headers = new Dictionary<string, string>(headers)
				{
					{ "X-Cache", "SVC-200" },
					{ "ETag", eTag },
					{ "Last-Modified", lastModified },
					{ "Expires", expiresAt },
					{ "Cache-Control", "public" }
				};
				response = new JObject
				{
					{ "StatusCode", (int)HttpStatusCode.OK },
					{ "Headers", headers.ToJson() },
					{ "Body", html.Compress(this.BodyEncoding) },
					{ "BodyEncoding", this.BodyEncoding }
				};
				if (writeDesktopLogs)
				{
					stopwatch.Stop();
					await this.WriteLogsAsync(requestInfo.CorrelationID, $"By-pass the process of {desktopInfo} => Got cached of XHTML - Key: {cacheKey} - Execution times: {stopwatch.GetElapsedTimes()}", null, this.ServiceName, "Process.Http.Request").ConfigureAwait(false);
				}
				return response;
			}

			// process the request
			try
			{
				// prepare portlets
				if (writeDesktopLogs)
					await this.WriteLogsAsync(requestInfo.CorrelationID, $"Start to prepare portlets of {desktopInfo}", null, this.ServiceName, "Process.Http.Request").ConfigureAwait(false);

				var stepwatch = Stopwatch.StartNew();
				if (desktop._portlets == null)
				{
					await desktop.FindPortletsAsync(cancellationToken, false).ConfigureAwait(false);
					await desktop.SetAsync(false, true, cancellationToken).ConfigureAwait(false);
					if (writeDesktopLogs)
					{
						stepwatch.Stop();
						await this.WriteLogsAsync(requestInfo.CorrelationID, $"Complete load portlets of {desktopInfo} - Execution times: {stepwatch.GetElapsedTimes()}", null, this.ServiceName, "Process.Http.Request").ConfigureAwait(false);
					}
				}

				stepwatch.Restart();
				await this.WriteLogsAsync(requestInfo.CorrelationID, $"Start to prepare data of {desktop.Portlets?.Count} portlet(s) of {desktopInfo} => {desktop.Portlets?.Select(p => p.Title).Join(", ")}", null, this.ServiceName, "Process.Http.Request").ConfigureAwait(false);

				var organizationJson = organization.ToJson(false, false, json =>
				{
					OrganizationProcessor.ExtraProperties.ForEach(name => json.Remove(name));
					json.Remove("Privileges");
					json.Remove("OriginalPrivileges");
					json["Description"] = organization.Description?.NormalizeHTMLBreaks();
					json["AlwaysUseHtmlSuffix"] = organization.AlwaysUseHtmlSuffix;
				});

				var siteJson = site.ToJson(json =>
				{
					SiteProcessor.ExtraProperties.ForEach(name => json.Remove(name));
					json.Remove("Privileges");
					json.Remove("OriginalPrivileges");
					json["Description"] = site.Description?.NormalizeHTMLBreaks();
					json["Domain"] = site.Host;
					json["Host"] = host;
				});

				var desktopsJson = new JObject
				{
					{ "Current", desktop.Alias },
					{ "Default", organization.DefaultDesktop?.Alias },
					{ "Home", site.HomeDesktop?.Alias },
					{ "Search", site.SearchDesktop?.Alias }
				};

				var parentIdentity = requestInfo.GetQueryParameter("x-parent");
				var contentIdentity = requestInfo.GetQueryParameter("x-content");
				var pageNumber = requestInfo.GetQueryParameter("x-page");
				var fileSuffixName = $"_p[{(string.IsNullOrWhiteSpace(parentIdentity) ? "none" : parentIdentity.Left(32) + (parentIdentity.Length > 32 ? "---" : "")).GetANSIUri()}]_c[{(string.IsNullOrWhiteSpace(contentIdentity) ? "none" : contentIdentity.Left(32) + (contentIdentity.Length > 32 ? "---" : "")).GetANSIUri()}]";

				async Task<JObject> generateAsync(ContentType portletContentType, JObject requestJson)
				{
					var data = await portletContentType.GetService().GenerateAsync(new RequestInfo(requestInfo)
					{
						ServiceName = portletContentType.ContentTypeDefinition.ModuleDefinition.ServiceName,
						ObjectName = portletContentType.ContentTypeDefinition.ObjectName,
						Body = requestJson.ToString(Newtonsoft.Json.Formatting.None),
						Header = new Dictionary<string, string>(requestInfo.Header, StringComparer.OrdinalIgnoreCase)
						{
							["x-origin"] = $"Portlet: {requestJson.Get<string>("Title")} [ID: {requestJson.Get<string>("ID")} - Action: {requestJson.Get<string>("Action")}]"
						}
					}, cancellationToken).ConfigureAwait(false);
					if (writeDesktopLogs)
					{
						var portletTitle = requestJson.Get<string>("Title");
						var portletID = requestJson.Get<string>("ID");
						await (requestJson?.ToString(Newtonsoft.Json.Formatting.Indented) ?? "NULL").ToBytes().SaveAsTextAsync(Path.Combine(Utility.TempFilesDirectory, $"{$"{portletTitle}_{portletID}".GetANSIUri()}{fileSuffixName}_request.json"), cancellationToken).ConfigureAwait(false);
					}
					return data;
				}

				var language = desktop.WorkingLanguage ?? site.Language ?? "en-US";
				var portletData = new ConcurrentDictionary<string, JObject>(StringComparer.OrdinalIgnoreCase);
				await (desktop.Portlets ?? new List<Portlet>()).Where(portlet => portlet != null).ForEachAsync(async portlet =>
				{
					var data = await this.PreparePortletAsync(portlet, requestInfo, organizationJson, siteJson, desktopsJson, language, parentIdentity, contentIdentity, pageNumber, generateAsync, writeDesktopLogs, cancellationToken).ConfigureAwait(false);
					if (data != null)
						portletData[portlet.ID] = data;
					if (writeDesktopLogs)
						await (data?.ToString(Newtonsoft.Json.Formatting.Indented) ?? "NULL").ToBytes().SaveAsTextAsync(Path.Combine(Utility.TempFilesDirectory, $"{$"{portlet.Title}_{portlet.ID}".GetANSIUri()}{fileSuffixName}_response.json"), cancellationToken).ConfigureAwait(false);
				}, true, Utility.RunProcessorInParallelsMode).ConfigureAwait(false);
				stepwatch.Stop();
				await this.WriteLogsAsync(requestInfo.CorrelationID, $"Complete prepare portlets' data of {desktopInfo} - Execution times: {stepwatch.GetElapsedTimes()}", null, this.ServiceName, "Process.Http.Request").ConfigureAwait(false);

				// generate HTML of portlets
				stepwatch.Restart();
				if (writeDesktopLogs)
					await this.WriteLogsAsync(requestInfo.CorrelationID, $"Start to generate HTML of {desktopInfo}", null, this.ServiceName, "Process.Http.Request").ConfigureAwait(false);

				var portletHtmls = new ConcurrentDictionary<string, Tuple<string, bool, string>>(StringComparer.OrdinalIgnoreCase);
				var generatePortletsTask = desktop.Portlets.Where(portlet => portlet != null).ForEachAsync(async portlet =>
				{
					try
					{
						var action = !string.IsNullOrWhiteSpace(parentIdentity) && !string.IsNullOrWhiteSpace(contentIdentity) ? portlet.OriginalPortlet.AlternativeAction : portlet.OriginalPortlet.Action;
						var isList = string.IsNullOrWhiteSpace(action) || "List".IsEquals(action);
						portletHtmls[portlet.ID] = await this.GeneratePortletAsync(requestInfo, portlet, isList, portletData.TryGetValue(portlet.ID, out var data) ? data : null, siteJson, desktopsJson, organization.AlwaysUseHtmlSuffix, language, cancellationToken, writeDesktopLogs, fileSuffixName).ConfigureAwait(false);
					}
					catch (Exception ex)
					{
						portletHtmls[portlet.ID] = new Tuple<string, bool, string>(this.GenerateErrorHtml($"Unexpected error => {ex.Message}", ex.StackTrace, requestInfo.CorrelationID, portlet.ID), true, null);
					}
				}, true, Utility.RunProcessorInParallelsMode);

				// generate desktop
				string title = "", metaTags = "", body = "", stylesheets = "", scripts = "", additionalStylesheets = "", additionalScriptLibraries = "", additionalScripts = "", jqueryScripts = "";
				var gotErrorOnGenerateDesktop = false;
				var mainPortlet = string.IsNullOrWhiteSpace(desktop.MainPortletID) || !portletData.TryGetValue(desktop.MainPortletID, out var value) ? null : value;
				try
				{
					var desktopData = await this.GenerateDesktopAsync(desktop, organization, site, host, mainPortlet, parentIdentity, contentIdentity, writeDesktopLogs, requestInfo.CorrelationID, cancellationToken).ConfigureAwait(false);
					title = desktopData.Item1;
					metaTags = (this.AllowPreconnect ? $"<link rel=\"preconnect\" href=\"{this.GetPortalsHttpURI(organization).Substring(6)}\"/>" + $"<link rel=\"preconnect\" href=\"{this.GetFilesHttpURI(organization).Substring(6)}\"/>" + "<link rel=\"preconnect\" href=\"//fonts.googleapis.com\"/><link rel=\"preconnect\" href=\"//fonts.gstatic.com\"/><link rel=\"preconnect\" href=\"//www.googletagmanager.com\"/><link rel=\"preconnect\" href=\"//unpkg.com\"/><link rel=\"preconnect\" href=\"//cdnjs.cloudflare.com\"/>" : "") + desktopData.Item2;
					body = desktopData.Item3;
					stylesheets = desktopData.Item4;
					scripts = desktopData.Item5;
				}
				catch (Exception ex)
				{
					body = this.GenerateErrorHtml($"Unexpected error => {ex.Message}", ex.StackTrace, requestInfo.CorrelationID, desktop.ID, "Desktop ID");
					gotErrorOnGenerateDesktop = true;
				}

				// prepare HTML of portlets
				await generatePortletsTask.ConfigureAwait(false);
				if (!gotErrorOnGenerateDesktop)
					portletHtmls.Where(kvp => !kvp.Value.Item2).Select(kvp => kvp.Key).ToList().ForEach(portletID =>
					{
						var portletDataInfo = portletHtmls[portletID];
						var portletHtml = portletDataInfo.Item1;
						var portletCacheExpiration = portletDataInfo.Item3;

						// prepare all STYLE tags
						try
						{
							var start = portletHtml.PositionOf("<style");
							while (start > -1)
							{
								var end = portletHtml.PositionOf("</style>", start);
								var portletStylesheet = portletHtml.Substring(start, end - start);
								portletHtml = portletHtml.Remove(start, end - start + 8);

								start = portletStylesheet.PositionOf("<style");
								end = portletStylesheet.PositionOf(">", start);
								additionalStylesheets += this.MinifyCss(portletStylesheet.Remove(start, end - start + 1));

								start = portletHtml.PositionOf("<style");
							}
						}
						catch { }

						// prepare all SCRIPT tags
						try
						{
							var start = portletHtml.PositionOf("<script");
							while (start > -1)
							{
								var end = portletHtml.PositionOf("</script>", start);
								var portletScript = portletHtml.Substring(start, end - start);
								portletHtml = portletHtml.Remove(start, end - start + 9);

								if (portletScript.PositionOf("src=") > 0)
									additionalScriptLibraries += portletScript.Trim() + "</script>";
								else
								{
									portletScript = this.MinifyJs(portletScript.Substring(portletScript.IndexOf(">") + 1) + ";").Replace(";;", ";");
									if ((portletScript.StartsWith("$(function(){") && portletScript.EndsWith("});")) || (portletScript.StartsWith("$(()=>") && portletScript.EndsWith(");")))
										jqueryScripts += portletScript.StartsWith("$(function(){") && portletScript.EndsWith("});")
											? portletScript.Substring(13, portletScript.Length - 16) + ";"
											: portletScript.StartsWith("$(()=>{") && portletScript.EndsWith("});")
												? portletScript.Substring(7, portletScript.Length - 10) + ";"
												: portletScript.Substring(6, portletScript.Length - 8) + ";";
									else
										additionalScripts += portletScript;
								}

								start = portletHtml.PositionOf("<script");
							}
						}
						catch { }

						portletHtmls[portletID] = new Tuple<string, bool, string>(portletHtml, false, portletCacheExpiration);
					});

				// prepare all SCRIPT tags of body
				try
				{
					var start = body.PositionOf("<script");
					while (start > -1)
					{
						var end = body.PositionOf("</script>", start);
						var bodyScript = body.Substring(start, end - start);
						body = body.Remove(start, end - start + 9);

						if (bodyScript.PositionOf("src=") > 0)
							additionalScriptLibraries += bodyScript.Trim() + "</script>";
						else
						{
							bodyScript = this.MinifyJs(bodyScript.Substring(bodyScript.IndexOf(">") + 1) + ";").Replace(";;", ";");
							if ((bodyScript.StartsWith("$(function(){") && bodyScript.EndsWith("});")) || (bodyScript.StartsWith("$(()=>") && bodyScript.EndsWith(");")))
								jqueryScripts += bodyScript.StartsWith("$(function(){") && bodyScript.EndsWith("});")
									? bodyScript.Substring(13, bodyScript.Length - 16) + ";"
									: bodyScript.StartsWith("$(()=>{") && bodyScript.EndsWith("});")
										? bodyScript.Substring(7, bodyScript.Length - 10) + ";"
										: bodyScript.Substring(6, bodyScript.Length - 8) + ";";
							else
								additionalScripts += bodyScript;
						}

						start = body.PositionOf("<script");
					}
				}
				catch { }

				// final stylesheets
				stylesheets += string.IsNullOrWhiteSpace(additionalStylesheets) ? "" : $"<style>{additionalStylesheets}</style>";

				// final scripts
				additionalScripts += string.IsNullOrWhiteSpace(jqueryScripts) ? "" : "$(()=>{" + jqueryScripts.Replace(";;", ";") + "});";
				scripts = "<script>__vieapps={ids:{" + (mainPortlet?.Get<string>("IDs") ?? $"system:\"{organization.ID}\",service:\"{this.ServiceName.ToLower()}\"") + $",parent:\"{parentIdentity}\",content:\"{contentIdentity}\"" + "},URLs:{root:\"~/\",portals:\"" + (organization.FakePortalsHttpURI ?? Utility.PortalsHttpURI) + "\",files:\"" + (organization.FakeFilesHttpURI ?? Utility.FilesHttpURI) + "\"},desktops:{home:{{homeDesktop}},search:{{searchDesktop}},current:{" + $"alias:\"{desktop.Alias}\",id:\"{desktop.ID}\"" + "}},language:{{language}},isMobile:{{isMobile}},osInfo:\"{{osInfo}}\",correlationID:\"{{correlationID}}\"};</script>"
					+ scripts
					+ additionalScriptLibraries
					+ (string.IsNullOrWhiteSpace(additionalScripts) ? "" : $"<script>{additionalScripts}</script>");

				// prepare HTML of all zones
				var zoneHtmls = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
				(desktop.Portlets ?? new List<Portlet>()).Where(portlet => portlet != null).OrderBy(portlet => portlet.Zone).ThenBy(portlet => portlet.OrderIndex).ForEach(portlet =>
				{
					if (!zoneHtmls.TryGetValue(portlet.Zone, out var htmls))
					{
						htmls = new List<string>();
						zoneHtmls[portlet.Zone] = htmls;
					}
					htmls.Add(portletHtmls[portlet.ID].Item1);
				});
				zoneHtmls.ForEach(kvp => body = body.Replace(StringComparison.OrdinalIgnoreCase, "{{" + kvp.Key + "-holder}}", kvp.Value.Join("\r\n")));

				// generate html
				html = "<!DOCTYPE html><html xmlns=\"http://www.w3.org/1999/xhtml\" lang=\"" + language.Left(2) + "\"><head></head><body></body></html>";

				var style = desktop.UISettings?.GetStyle() ?? "";
				if (string.IsNullOrWhiteSpace(style))
					style = site.UISettings?.GetStyle() ?? "";

				var css = desktop.UISettings?.Css ?? "";
				if (!string.IsNullOrWhiteSpace(site.UISettings?.Css))
					css += (css != "" ? " " : "") + site.UISettings.Css;

				if (!string.IsNullOrWhiteSpace(css) || !string.IsNullOrWhiteSpace(style))
				{
					var bodyStyle = "";
					if (!string.IsNullOrWhiteSpace(css))
						bodyStyle += $" class=\"{css.Replace("&", "&amp;").Replace("\"", "&quot;").Replace("<", "&lt;").Replace(">", "&gt;")}\"";
					if (!string.IsNullOrWhiteSpace(style))
						bodyStyle += $" style=\"{style.Replace("&", "&amp;").Replace("\"", "&quot;").Replace("<", "&lt;").Replace(">", "&gt;")}\"";
					html = html.Insert(html.IndexOf("></body>"), bodyStyle);
				}

				html = html.Insert(html.IndexOf("</head>"), $"<title>{title}</title><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"/>" + metaTags + stylesheets);
				html = html.Insert(html.IndexOf("</body>"), body + scripts);

				// minify
				html = html.Replace(StringComparison.OrdinalIgnoreCase, $"{Utility.FilesHttpURI}/", "~~/").Replace(StringComparison.OrdinalIgnoreCase, $"{Utility.PortalsHttpURI}/", "~#/").Trim();
				html = this.RemoveDesktopHtmlWhitespaces ? html.MinifyHtml() : html;

				// prepare caching
				if (requestInfo.GetParameter("x-force-cache") != null)
					await Utility.Cache.RemoveAsync(new[] { cacheKey, cacheKeyOfLastModified, cacheKeyOfExpiration }, cancellationToken).ConfigureAwait(false);

				if (processCache && !gotErrorOnGenerateDesktop && !portletHtmls.Values.Any(data => data.Item2))
				{
					var expirationTime = 0;
					DateTime? expiresAt = null;
					portletHtmls.Values.Where(data => data.Item3 != null).ForEach(data =>
					{
						if (Int32.TryParse(data.Item3, out var minutes) && minutes > 0)
						{
							if (expirationTime < minutes)
								expirationTime = minutes;
						}
						else if (DateTime.TryParse(data.Item3, out var time))
							expiresAt = expiresAt == null || expiresAt < time
								? time
								: expiresAt;
					});
					lastModified = DateTime.Now.ToHttpString();
					headers = new Dictionary<string, string>(headers, StringComparer.OrdinalIgnoreCase)
					{
						{ "ETag", eTag },
						{ "Last-Modified", lastModified },
						{ "Expires", expiresAt != null ? expiresAt.Value.ToHttpString() : DateTime.Now.AddMinutes(13).ToHttpString() },
						{ "X-Cache", "None" },
						{ "Cache-Control", "public" }
					};
					if (expiresAt != null)
						await Task.WhenAll
						(
							Utility.Cache.SetAsync(cacheKey, this.NormalizeDesktopHtml(html, organization, site, desktop), expiresAt.Value, cancellationToken),
							Utility.Cache.SetAsync(cacheKeyOfLastModified, lastModified, expiresAt.Value, cancellationToken),
							Utility.Cache.SetAsync(cacheKeyOfExpiration, expiresAt.Value.ToDTString(), expiresAt.Value, cancellationToken)
						).ConfigureAwait(false);
					else
						await Task.WhenAll
						(
							Utility.Cache.SetAsync(cacheKey, this.NormalizeDesktopHtml(html, organization, site, desktop), expirationTime, cancellationToken),
							Utility.Cache.SetAsync(cacheKeyOfLastModified, lastModified, expirationTime, cancellationToken),
							expirationTime > 0
								? Utility.Cache.SetAsync(cacheKeyOfExpiration, DateTime.Now.AddMinutes(expirationTime).ToDTString(), expirationTime, cancellationToken)
								: Utility.Cache.RemoveAsync(cacheKeyOfExpiration, cancellationToken)
						).ConfigureAwait(false);
					await Utility.Cache.AddSetMembersAsync(desktop.GetSetCacheKey(), new[] { cacheKey, cacheKeyOfLastModified, cacheKeyOfExpiration }, cancellationToken).ConfigureAwait(false);
				}

				// normalize
				html = this.NormalizeDesktopHtml(html, requestURI, useShortURLs, organization, site, desktop, isMobile, osInfo, requestInfo.CorrelationID);

				stepwatch.Stop();
				await this.WriteLogsAsync(requestInfo.CorrelationID, $"HTML code of {desktopInfo} has been generated - Execution times: {stepwatch.GetElapsedTimes()}", null, this.ServiceName, "Process.Http.Request").ConfigureAwait(false);
				if (writeDesktopLogs)
					await this.WriteLogsAsync(requestInfo.CorrelationID, $"HTML code of {desktopInfo} has been generated & normalized:\r\n{html}", null, this.ServiceName, "Process.Http.Request").ConfigureAwait(false);
			}
			catch (Exception ex)
			{
				await this.WriteLogsAsync(requestInfo.CorrelationID, $"Unexpected error occurred while processing {desktopInfo}", ex, this.ServiceName, "Process.Http.Request").ConfigureAwait(false);
				html = "<!DOCTYPE html>\r\n"
					+ "<html xmlns=\"http://www.w3.org/1999/xhtml\">\r\n"
					+ "<head><title>Error: " + ex.Message.Replace("&", "&amp;").Replace("\"", "&quot;").Replace("<", "&lt;").Replace(">", "&gt;") + "</title><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"/></head>\r\n"
					+ "<body>" + this.GenerateErrorHtml($"Unexpected error => {ex.Message}", ex.StackTrace, requestInfo.CorrelationID, desktop.ID, "Desktop ID") + "</body>\r\n"
					+ "</html>";
			}

			// response
			response = new JObject
			{
				{ "StatusCode", (int)HttpStatusCode.OK },
				{ "Headers", headers.ToJson() },
				{ "Body", writeDesktopLogs ? html : html.Compress(this.BodyEncoding) },
				{ "BodyEncoding", this.BodyEncoding },
				{ "BodyAsPlainText", writeDesktopLogs }
			};
			stopwatch.Stop();
			await this.WriteLogsAsync(requestInfo.CorrelationID, $"Complete process of {desktopInfo} - Execution times: {stopwatch.GetElapsedTimes()}", null, this.ServiceName, "Process.Http.Request").ConfigureAwait(false);
			return response;
		}

		async Task<JObject> PreparePortletAsync(Portlet theportlet, RequestInfo requestInfo, JObject organizationJson, JObject siteJson, JObject desktopsJson, string language, string parentIdentity, string contentIdentity, string pageNumber, Func<ContentType, JObject, Task<JObject>> generateAsync, bool writeLogs, CancellationToken cancellationToken)
		{
			// get original portlet
			var stopwatch = Stopwatch.StartNew();
			var portlet = theportlet.OriginalPortlet;
			if (portlet == null)
				return this.GenerateErrorJson(new InformationNotFoundException("The original portlet was not found"), requestInfo, writeLogs);

			var portletInfo = writeLogs
				? $"the '{theportlet.Title}' portlet [ID: {theportlet.ID}{(string.IsNullOrWhiteSpace(theportlet.OriginalPortletID) ? "" : $" - alias of '{portlet.Title}' (ID: {portlet.ID})")}]"
				: null;
			if (writeLogs)
				await this.WriteLogsAsync(requestInfo.CorrelationID, $"Start to prepare data of {portletInfo}", null, this.ServiceName, "Process.Http.Request").ConfigureAwait(false);

			// get content-type
			var contentType = await (portlet.RepositoryEntityID ?? "").GetContentTypeByIDAsync(cancellationToken).ConfigureAwait(false);
			var parentContentType = contentType?.GetParent();

			// no content-type => then by-pass on static porlet
			if (contentType == null)
			{
				stopwatch.Stop();
				if (writeLogs)
					await this.WriteLogsAsync(requestInfo.CorrelationID, $"By-pass the preparing process of {portletInfo} => Static content - Execution times: {stopwatch.GetElapsedTimes()}", null, this.ServiceName, "Process.Http.Request").ConfigureAwait(false);
				return null;
			}

			// prepare
			var module = await (contentType.RepositoryID ?? "").GetModuleByIDAsync(cancellationToken).ConfigureAwait(false) ?? contentType.Module;
			parentIdentity = parentIdentity ?? requestInfo.GetQueryParameter("x-parent");
			contentIdentity = contentIdentity ?? requestInfo.GetQueryParameter("x-content");
			pageNumber = pageNumber ?? requestInfo.GetQueryParameter("x-page");

			var action = !string.IsNullOrWhiteSpace(parentIdentity) && !string.IsNullOrWhiteSpace(contentIdentity) ? portlet.AlternativeAction : portlet.Action;
			var isList = string.IsNullOrWhiteSpace(action) || "List".IsEquals(action);

			var expression = isList && !string.IsNullOrWhiteSpace(portlet.ExpressionID) ? await portlet.ExpressionID.GetExpressionByIDAsync(cancellationToken).ConfigureAwait(false) : null;
			if (expression != null && (expression.Filter == null || expression.Filter.Children == null || !expression.Filter.Children.Any()))
				expression = await portlet.ExpressionID.GetExpressionByIDAsync(cancellationToken, true).ConfigureAwait(false);

			var optionsJson = isList ? JObject.Parse(portlet.ListSettings?.Options ?? "{}") : JObject.Parse(portlet.ViewSettings?.Options ?? "{}");
			optionsJson["ShowBreadcrumbs"] = isList ? portlet.ListSettings != null && portlet.ListSettings.ShowBreadcrumbs : portlet.ViewSettings != null && portlet.ViewSettings.ShowBreadcrumbs;
			optionsJson["ShowPagination"] = isList ? portlet.ListSettings != null && portlet.ListSettings.ShowPagination : portlet.ViewSettings != null && portlet.ViewSettings.ShowPagination;
			var desktop = await optionsJson.Get("DesktopID", "").GetDesktopByIDAsync(cancellationToken).ConfigureAwait(false);

			if (writeLogs)
				await this.WriteLogsAsync(requestInfo.CorrelationID, $"Determine the action/expression for generating content of {portletInfo} - Action: {(isList ? "List" : "View")} - Expression: {portlet.ExpressionID ?? "N/A"} (Title: {expression?.Title ?? "None"}{(expression != null ? $" / Filter: {expression.Filter != null} / Sort: {expression.Sort != null}" : "")}) - Specified desktop: {(desktop != null ? $"{desktop.Title} [ID: {desktop.ID}]" : "(None)")}", null, this.ServiceName, "Process.Http.Request").ConfigureAwait(false);

			// prepare the JSON that contains the requesting information for generating content
			var requestJson = new JObject
			{
				{ "ID", portlet.ID },
				{ "Title", portlet.Title },
				{ "Action", isList ? "List" : "View" },
				{ "ParentIdentity", parentIdentity },
				{ "ContentIdentity", contentIdentity },
				{ "Expression", new JObject
					{
						{ "ID", expression?.ID },
						{ "FilterBy", expression?.Filter?.ToJson() },
						{ "SortBy", expression?.Sort?.ToJson() },
					}
				},
				{ "IsAutoPageNumber", isList && portlet.ListSettings != null && portlet.ListSettings.AutoPageNumber },
				{ "Pagination", new JObject
					{
						{ "PageSize", isList && portlet.ListSettings != null ? portlet.ListSettings.PageSize : 0 },
						{ "PageNumber", isList && portlet.ListSettings != null ? portlet.ListSettings.AutoPageNumber ? (pageNumber ?? "1").CastAs<int>() : 1 : (pageNumber ?? "1").CastAs<int>() },
						{ "ShowPageLinks", portlet.PaginationSettings != null && portlet.PaginationSettings.ShowPageLinks },
						{ "NumberOfPageLinks", portlet.PaginationSettings != null ? portlet.PaginationSettings.NumberOfPageLinks : 7 }
					}
				},
				{ "Options", optionsJson },
				{ "Language", language ?? "vi-VN" },
				{ "Desktops", new JObject
					{
						{ "Specified", desktop?.Alias },
						{ "ContentType", contentType.Desktop?.Alias },
						{ "Module", contentType.Module?.Desktop?.Alias },
						{ "Current", desktopsJson["Current"] },
						{ "Default", desktopsJson["Default"] },
						{ "Home", desktopsJson["Home"] },
						{ "Search", desktopsJson["Search"] }
					}
				},
				{ "Site", siteJson },
				{ "ContentTypeDefinition", contentType.ContentTypeDefinition?.ToJson() },
				{ "ModuleDefinition", contentType.ContentTypeDefinition?.ModuleDefinition?.ToJson(json =>
					{
						(json as JObject).Remove("ContentTypeDefinitions");
						(json as JObject).Remove("ObjectDefinitions");
					})
				},
				{ "Organization", organizationJson },
				{ "Module", contentType.Module?.ToJson(json =>
					{
						ModuleProcessor.ExtraProperties.ForEach(name => json.Remove(name));
						json.Remove("Privileges");
						json.Remove("OriginalPrivileges");
						json["Description"] = contentType.Module.Description?.NormalizeHTMLBreaks();
					})
				},
				{ "ContentType", contentType.ToJson(json =>
					{
						ModuleProcessor.ExtraProperties.ForEach(name => json.Remove(name));
						json.Remove("Privileges");
						json.Remove("OriginalPrivileges");
						json["Description"] = contentType.Description?.NormalizeHTMLBreaks();
					})
				},
				{ "ParentContentType", parentContentType?.ToJson(json =>
					{
						ModuleProcessor.ExtraProperties.ForEach(name => json.Remove(name));
						json.Remove("Privileges");
						json.Remove("OriginalPrivileges");
						json["Description"] = parentContentType.Description?.NormalizeHTMLBreaks();
					})
				}
			};

			// call the service for generating content of the portlet
			JObject responseJson = null;
			Exception exception = null;
			var serviceURI = $"GET /{module.ModuleDefinition?.ServiceName?.ToLower()}/{contentType.ContentTypeDefinition?.ObjectName.ToLower()}";
			try
			{
				if (writeLogs)
					await this.WriteLogsAsync(requestInfo.CorrelationID, $"Call the service ({serviceURI}) to prepare data of {portletInfo}\r\n- Request:\r\n{requestJson}", null, this.ServiceName, "Process.Http.Request").ConfigureAwait(false);
				responseJson = await generateAsync(contentType, requestJson).ConfigureAwait(false);
			}
			catch (Exception ex)
			{
				exception = ex;
				responseJson = this.GenerateErrorJson(ex, requestInfo, writeLogs, $"Error occurred while calling a service [{serviceURI}]");
			}

			stopwatch.Stop();
			if (exception != null)
				await this.WriteLogsAsync(requestInfo.CorrelationID, $"Error occurred while preparing data of {portletInfo} - Execution times: {stopwatch.GetElapsedTimes()}\r\n- Request:\r\n{requestJson}\r\n- Error:\r\n{responseJson}", exception, this.ServiceName, "Process.Http.Request", LogLevel.Error).ConfigureAwait(false);
			else if (writeLogs)
				await this.WriteLogsAsync(requestInfo.CorrelationID, $"Data of {portletInfo} has been prepared - Execution times: {stopwatch.GetElapsedTimes()}\r\n- Response:\r\n{responseJson}", null, this.ServiceName, "Process.Http.Request").ConfigureAwait(false);

			return responseJson;
		}

		async Task<Tuple<string, bool, string>> GeneratePortletAsync(RequestInfo requestInfo, Portlet theportlet, bool isList, JObject data, JObject siteJson, JObject desktopsJson, bool alwaysUseHtmlSuffix, string language, CancellationToken cancellationToken, bool writeLogs, string fileSuffixName)
		{
			// get original first
			var stopwatch = Stopwatch.StartNew();
			var portlet = theportlet.OriginalPortlet;
			var portletInfo = writeLogs
				? $"the '{theportlet.Title}' portlet [ID: {theportlet.ID}{(string.IsNullOrWhiteSpace(theportlet.OriginalPortletID) ? "" : $" - alias of '{portlet.Title}' (ID: {portlet.ID})")}]"
				: null;
			if (writeLogs)
				await this.WriteLogsAsync(requestInfo.CorrelationID, $"Start to generate HTML code of {portletInfo}", null, this.ServiceName, "Process.Http.Request").ConfigureAwait(false);

			// prepare container and zones
			var portletContainer = (await portlet.GetTemplateAsync(cancellationToken).ConfigureAwait(false)).GetXDocument();
			var portletZones = portletContainer.GetZones().ToList();

			// check the zone of content
			var contentZone = portletZones.GetZone("Content");
			if (contentZone == null)
				throw new TemplateIsInvalidException("The required zone ('Content') is not found");

			string style, css, title;

			// prepare context-menu zone
			var menuZone = portletZones?.GetZone("ContextMenu");
			if (menuZone != null)
			{
				menuZone.Remove();
				portletZones.Remove(menuZone);
			}

			// prepare title zone
			var titleZone = portletZones.GetZone("Title");
			if (portlet.CommonSettings.HideTitle)
			{
				if (titleZone != null)
				{
					titleZone.Remove();
					portletZones.Remove(titleZone);
				}
			}
			else if (titleZone != null)
			{
				titleZone.GetZoneIDAttribute().Remove();

				style = portlet.CommonSettings?.TitleUISettings?.GetStyle() ?? "";
				if (!string.IsNullOrWhiteSpace(style))
				{
					var attribute = titleZone.Attributes().FirstOrDefault(attr => attr.Name.LocalName.IsEquals("style"));
					if (attribute == null)
						titleZone.Add(new XAttribute("style", style));
					else
						attribute.Value = $"{attribute.Value.Trim()}{(attribute.Value.Trim().EndsWith(";") ? "" : ";")}{style}";
				}

				css = portlet.CommonSettings?.TitleUISettings?.Css ?? "";
				if (!string.IsNullOrWhiteSpace(css))
				{
					var attribute = titleZone.Attributes().FirstOrDefault(attr => attr.Name.LocalName.IsEquals("class"));
					if (attribute == null)
						titleZone.Add(new XAttribute("class", css));
					else
						attribute.Value = $"{attribute.Value.Trim()} {css}";
				}

				title = "";
				if (!string.IsNullOrWhiteSpace(portlet.CommonSettings.IconURI))
					title += $"<picture><source srcset=\"{portlet.CommonSettings.IconURI.GetWebpImageURL(portlet.CommonSettings.IconURI.IsEndsWith(".png"))}\"/><img alt=\"\" src=\"{portlet.CommonSettings.IconURI}\"/></picture>";
				title += string.IsNullOrWhiteSpace(portlet.CommonSettings.TitleURL)
					? $"<span>{portlet.Title.Replace("\"", "&quot;").Replace("<", "&lt;").Replace(">", "&gt;")}</span>"
					: $"<span><a href=\"{portlet.CommonSettings.TitleURL}\">{portlet.Title.Replace("\"", "&quot;").Replace("<", "&lt;").Replace(">", "&gt;")}</a></span>";
				titleZone.Add(XElement.Parse($"<div>{title}</div>"));
			}

			// prepare content zone
			contentZone.GetZoneIDAttribute().Remove();

			style = portlet.CommonSettings?.ContentUISettings?.GetStyle() ?? "";
			if (!string.IsNullOrWhiteSpace(style))
			{
				var attribute = contentZone.Attributes().FirstOrDefault(attr => attr.Name.LocalName.IsEquals("style"));
				if (attribute == null)
					contentZone.Add(new XAttribute("style", style));
				else
					contentZone.Value = $"{attribute.Value.Trim()}{(attribute.Value.Trim().EndsWith(";") ? "" : ";")}{style}";
			}

			css = portlet.CommonSettings?.ContentUISettings?.Css ?? "";
			if (!string.IsNullOrWhiteSpace(css))
			{
				var attribute = contentZone.Attributes().FirstOrDefault(attr => attr.Name.LocalName.IsEquals("class"));
				if (attribute == null)
					contentZone.Add(new XAttribute("class", css));
				else
					attribute.Value = $"{attribute.Value.Trim()} {css}";
			}

			var html = "";
			var objectType = "";
			var gotError = false;
			var cacheExpiration = data != null && Int32.TryParse(data["CacheExpiration"]?.ToString(), out var expiration) && expiration > 0 ? expiration : 0;
			var cacheExpirationTime = data != null && DateTime.TryParse(data["CacheExpiration"]?.ToString(), out var expirationTime) ? expirationTime as DateTime? : null;
			var contentType = data != null ? await (portlet.RepositoryEntityID ?? "").GetContentTypeByIDAsync(cancellationToken).ConfigureAwait(false) : null;

			if (contentType != null)
			{
				objectType = contentType.ContentTypeDefinition?.GetObjectName() ?? "";
				var xslFilename = "";
				var xslTemplate = "";

				var errorMessage = data.Get<string>("Error");
				var errorStack = string.Empty;
				var errorType = string.Empty;

				var content = "";
				XDocument xml = null;

				if (string.IsNullOrWhiteSpace(errorMessage))
					try
					{
						// raw HTML
						if (data["RawHTML"] != null && data.Get<bool>("RawHTML"))
							content = (data["Data"] as JValue)?.Value?.ToString();

						// XML to transform
						else
						{
							// check data of XML
							if (!(data["Data"] is JValue xmlJson) || xmlJson.Value == null)
								throw new InformationRequiredException("The response JSON must have the element named 'Data' that contains XML code for transforming via a node that named 'Data'");

							// prepare XSLT
							var mainDirectory = contentType.ContentTypeDefinition?.ModuleDefinition?.Directory?.ToLower();
							var subDirectory = contentType.ContentTypeDefinition?.ObjectName?.ToLower();
							xslTemplate = isList ? portlet.ListSettings.Template : portlet.ViewSettings.Template;

							if (string.IsNullOrWhiteSpace(xslTemplate))
							{
								xslFilename = data.Get<string>("XslFilename");
								if (string.IsNullOrWhiteSpace(xslFilename))
									xslFilename = isList ? "list.xsl" : "view.xsl";
								xslTemplate = await Utility.GetTemplateAsync(xslFilename, portlet.Desktop?.WorkingTheme, mainDirectory, subDirectory, cancellationToken).ConfigureAwait(false);

								if (string.IsNullOrWhiteSpace(xslTemplate))
									xslTemplate = await Utility.GetTemplateAsync(xslFilename, "default", mainDirectory, subDirectory, cancellationToken).ConfigureAwait(false);

								if (string.IsNullOrWhiteSpace(xslTemplate) && !xslFilename.IsEquals("list.xsl"))
								{
									xslTemplate = await Utility.GetTemplateAsync("list.xsl", portlet.Desktop?.WorkingTheme, mainDirectory, subDirectory, cancellationToken).ConfigureAwait(false);
									if (string.IsNullOrWhiteSpace(xslTemplate))
										xslTemplate = await Utility.GetTemplateAsync("list.xsl", "default", mainDirectory, subDirectory, cancellationToken).ConfigureAwait(false);
								}
							}

							if (string.IsNullOrWhiteSpace(xslTemplate))
								throw new TemplateIsInvalidException($"XSL template is invalid [/themes/{portlet.Desktop?.WorkingTheme ?? "default"}/templates/{mainDirectory ?? "-"}/{subDirectory ?? "-"}/{xslFilename}]");

							var showBreadcrumbs = isList ? portlet.ListSettings.ShowBreadcrumbs : portlet.ViewSettings.ShowBreadcrumbs;
							if (xslTemplate.IsContains("{{breadcrumb-holder}}"))
							{
								if (showBreadcrumbs)
								{
									var xslBreadcrumb = portlet.BreadcrumbSettings.Template;
									if (string.IsNullOrWhiteSpace(xslBreadcrumb))
									{
										xslBreadcrumb = await Utility.GetTemplateAsync("breadcrumb.xml", portlet.Desktop?.WorkingTheme, null, null, cancellationToken).ConfigureAwait(false);
										if (string.IsNullOrWhiteSpace(xslBreadcrumb))
											xslBreadcrumb = await Utility.GetTemplateAsync("breadcrumb.xml", "default", null, null, cancellationToken).ConfigureAwait(false);
									}
									xslTemplate = xslTemplate.Replace(StringComparison.OrdinalIgnoreCase, "{{breadcrumb-holder}}", xslBreadcrumb ?? "");
								}
								else
									xslTemplate = xslTemplate.Replace(StringComparison.OrdinalIgnoreCase, "{{breadcrumb-holder}}", "");
							}

							var showPagination = isList ? portlet.ListSettings.ShowPagination : portlet.ViewSettings.ShowPagination;
							if (xslTemplate.IsContains("{{pagination-holder}}"))
							{
								if (showPagination)
								{
									var xslPagination = portlet.PaginationSettings.Template;
									if (string.IsNullOrWhiteSpace(xslPagination))
									{
										xslPagination = await Utility.GetTemplateAsync("pagination.xml", portlet.Desktop?.WorkingTheme, null, null, cancellationToken).ConfigureAwait(false);
										if (string.IsNullOrWhiteSpace(xslPagination))
											xslPagination = await Utility.GetTemplateAsync("pagination.xml", "default", null, null, cancellationToken).ConfigureAwait(false);
									}
									xslTemplate = xslTemplate.Replace(StringComparison.OrdinalIgnoreCase, "{{pagination-holder}}", xslPagination ?? "");
								}
								else
									xslTemplate = xslTemplate.Replace(StringComparison.OrdinalIgnoreCase, "{{pagination-holder}}", "");
							}

							// prepare XML
							var dataXml = xmlJson.Value.ToString().ToXml(element => element.Descendants().Attributes().Where(attribute => attribute.IsNamespaceDeclaration).Remove());

							var metaXml = new JObject
							{
								{ "Language", language ?? "vi-VN" },
								{ "Portlet", new JObject
									{
										{ "ID", portlet.ID },
										{ "Title", portlet.Title },
										{ "URL", portlet.CommonSettings?.TitleURL ?? "" },
										{ "Zone", portlet.Zone },
										{ "OrderIndex", portlet.OrderIndex }
									}
								},
								{ "Desktops", new JObject
									{
										{ "ContentType", contentType.Desktop?.Alias },
										{ "Module", contentType.Module?.Desktop?.Alias },
										{ "Current", desktopsJson["Current"] },
										{ "Default", desktopsJson["Default"] },
										{ "Home", desktopsJson["Home"] },
										{ "Search", desktopsJson["Search"] }
									}
								},
								{ "Site", siteJson },
								{ "ContentType", contentType.ToJson(json =>
									{
										json["Description"] = contentType.Description?.Replace("\r", "").Replace("\n", "<br/>");
										ModuleProcessor.ExtraProperties.ForEach(name => json.Remove(name));
										json.Remove("Privileges");
										json.Remove("OriginalPrivileges");
										json.Remove("ExtendedPropertyDefinitions");
										json.Remove("ExtendedControlDefinitions");
										json.Remove("StandardControlDefinitions");
										if (contentType.ExtendedPropertyDefinitions != null)
										{
											json["ExtendedPropertyDefinitions"] = new JObject
											{
												{ "ExtendedPropertyDefinition", contentType.ExtendedPropertyDefinitions.Select(definition => definition.ToJson()).ToJArray() }
											};
											json["ExtendedControlDefinitions"] = new JObject
											{
												{ "ExtendedControlDefinition", contentType.ExtendedControlDefinitions.Select(definition => definition.ToJson()).ToJArray() }
											};
										}
										if (contentType.StandardControlDefinitions != null)
											json["StandardControlDefinitions"] = new JObject
											{
												{ "StandardControlDefinition", contentType.StandardControlDefinitions.Select(definition => definition.ToJson()).ToJArray() }
											};
									})
								}
							}.ToXml("Meta").CleanInvalidCharacters();

							var optionsJson = isList ? JObject.Parse(portlet.ListSettings.Options ?? "{}") : JObject.Parse(portlet.ViewSettings.Options ?? "{}");
							optionsJson["ShowBreadcrumbs"] = showBreadcrumbs;
							optionsJson["ShowPagination"] = showPagination;
							var optionsXml = optionsJson.ToXml("Options").CleanInvalidCharacters();

							JObject breadcrumbsJson = null;
							if (showBreadcrumbs)
							{
								var breadcrumbs = (data.Get<JArray>("Breadcrumbs") ?? new JArray()).Select(node => node as JObject).ToList();
								if (portlet.BreadcrumbSettings.NumberOfNodes > 0 && portlet.BreadcrumbSettings.NumberOfNodes < breadcrumbs.Count)
									breadcrumbs = breadcrumbs.Skip(breadcrumbs.Count - portlet.BreadcrumbSettings.NumberOfNodes).ToList();

								if (portlet.BreadcrumbSettings.ShowContentTypeLink)
								{
									if (!string.IsNullOrWhiteSpace(portlet.BreadcrumbSettings.ContentTypeAdditionalLabel) && !string.IsNullOrWhiteSpace(portlet.BreadcrumbSettings.ContentTypeAdditionalURL))
										breadcrumbs.Insert(0, new JObject
										{
											{ "Text", portlet.BreadcrumbSettings.ContentTypeAdditionalLabel },
											{ "URL", portlet.BreadcrumbSettings.ContentTypeAdditionalURL }
										});
									breadcrumbs.Insert(0, new JObject
									{
										{ "Text", !string.IsNullOrWhiteSpace(portlet.BreadcrumbSettings.ContentTypeLabel) ? portlet.BreadcrumbSettings.ContentTypeLabel : contentType.Title },
										{ "URL", !string.IsNullOrWhiteSpace(portlet.BreadcrumbSettings.ContentTypeURL) ? portlet.BreadcrumbSettings.ContentTypeURL : $"~/{contentType.Desktop?.Alias}" + (contentType.GetParent() != null ? $"{(alwaysUseHtmlSuffix ? ".html" : "")}" : $"/{contentType.Title.GetANSIUri()}{(alwaysUseHtmlSuffix ? ".html" : "")}") }
									});
								}

								if (portlet.BreadcrumbSettings.ShowModuleLink)
								{
									if (!string.IsNullOrWhiteSpace(portlet.BreadcrumbSettings.ModuleAdditionalLabel) && !string.IsNullOrWhiteSpace(portlet.BreadcrumbSettings.ModuleAdditionalURL))
										breadcrumbs.Insert(0, new JObject
										{
											{ "Text", portlet.BreadcrumbSettings.ModuleAdditionalLabel },
											{ "URL", portlet.BreadcrumbSettings.ModuleAdditionalURL }
										});
									breadcrumbs.Insert(0, new JObject
									{
										{ "Text", !string.IsNullOrWhiteSpace(portlet.BreadcrumbSettings.ModuleLabel) ? portlet.BreadcrumbSettings.ModuleLabel : contentType.Module?.Title },
										{ "URL", !string.IsNullOrWhiteSpace(portlet.BreadcrumbSettings.ModuleURL) ? portlet.BreadcrumbSettings.ModuleURL : $"~/{contentType.Module?.Desktop?.Alias}" + (contentType.GetParent() != null ? $"{(alwaysUseHtmlSuffix ? ".html" : "")}" : $"/{contentType.Module?.Title.GetANSIUri()}{(alwaysUseHtmlSuffix ? ".html" : "")}") }
									});
								}

								if (!string.IsNullOrWhiteSpace(portlet.BreadcrumbSettings.HomeAdditionalLabel) && !string.IsNullOrWhiteSpace(portlet.BreadcrumbSettings.HomeAdditionalURL))
									breadcrumbs.Insert(0, new JObject
									{
										{ "Text", portlet.BreadcrumbSettings.HomeAdditionalLabel },
										{ "URL", portlet.BreadcrumbSettings.HomeAdditionalURL }
									});

								breadcrumbs.Insert(0, new JObject
								{
									{ "Text", !string.IsNullOrWhiteSpace(portlet.BreadcrumbSettings.HomeLabel) ? portlet.BreadcrumbSettings.HomeLabel : "Home" },
									{ "URL", !string.IsNullOrWhiteSpace(portlet.BreadcrumbSettings.HomeURL) ? portlet.BreadcrumbSettings.HomeURL : $"~/index{(alwaysUseHtmlSuffix ? ".html" : "")}" }
								});

								breadcrumbsJson = new JObject
								{
									{ "SeparatedLabel", string.IsNullOrWhiteSpace(portlet.BreadcrumbSettings.SeparatedLabel) ? ">" : portlet.BreadcrumbSettings.SeparatedLabel },
									{ "Nodes", new JObject
										{
											{ "Node", breadcrumbs.ToJArray() }
										}
									}
								};
							}

							var paginationJson = showPagination ? data.Get<JObject>("Pagination") ?? new JObject() : null;

							xml = new XDocument(new XElement("VIEApps", metaXml, dataXml, optionsXml));

							if (breadcrumbsJson != null)
								xml.Root.Add(breadcrumbsJson.ToXml("Breadcrumbs").CleanInvalidCharacters());

							if (paginationJson != null && paginationJson.Get<JObject>("Pages") != null)
								xml.Root.Add(paginationJson.ToXml("Pagination", paginationXml =>
								{
									paginationXml.Element("URLPattern").Remove();
									paginationXml.Element("Pages").Add(new XAttribute("Label", !string.IsNullOrWhiteSpace(portlet.PaginationSettings.CurrentPageLabel) ? portlet.PaginationSettings.CurrentPageLabel : "Current"));
									paginationXml.Add(new XElement("ShowPageLinks", portlet.PaginationSettings.ShowPageLinks));
									var totalPages = paginationJson.Get<int>("TotalPages");
									if (totalPages > 1)
									{
										var urlPattern = paginationJson.Get<string>("URLPattern");
										var currentPage = paginationJson.Get<int>("PageNumber");
										if (currentPage > 1)
										{
											var text = string.IsNullOrWhiteSpace(portlet.PaginationSettings.PreviousPageLabel)
												? "Previous"
												: portlet.PaginationSettings.PreviousPageLabel;
											var url = urlPattern.GetPaginationURL(currentPage - 1);
											paginationXml.Add(new XElement("PreviousPage", new XElement("Text", text), new XElement("URL", url)));
										}
										if (currentPage < totalPages)
										{
											var text = string.IsNullOrWhiteSpace(portlet.PaginationSettings.NextPageLabel)
												? "Next"
												: portlet.PaginationSettings.NextPageLabel;
											var url = urlPattern.GetPaginationURL(currentPage + 1);
											paginationXml.Add(new XElement("NextPage", new XElement("Text", text), new XElement("URL", url)));
										}
									}
								}).CleanInvalidCharacters());

							var filterBy = data.Get<JObject>("FilterBy");
							if (filterBy != null)
								xml.Root.Add(filterBy.ToXml("FilterBy").CleanInvalidCharacters());

							var sortBy = data.Get<JObject>("SortBy");
							if (sortBy != null)
								xml.Root.Add(sortBy.ToXml("SortBy").CleanInvalidCharacters());

							// transform
							content = xml.Transform(xslTemplate, optionsJson.Get("EnableDocumentFunctionAndInlineScripts", false));
							if (writeLogs)
							{
								var filename = $"{$"{theportlet.Title}_{theportlet.ID}".GetANSIUri()}{fileSuffixName}";
								await Task.WhenAll
								(
									this.WriteLogsAsync(requestInfo.CorrelationID, $"HTML of {portletInfo} has been transformed\r\n- XML:\r\n{xml}\r\n- XSL:\r\n{xslTemplate}\r\n- XHTML:\r\n{content}", null, this.ServiceName, "Process.Http.Request"),
									xml.ToString().ToBytes().SaveAsTextAsync(Path.Combine(Utility.TempFilesDirectory, $"{filename}.xml"), cancellationToken),
									xslTemplate.ToBytes().SaveAsTextAsync(Path.Combine(Utility.TempFilesDirectory, $"{filename}.xsl"), cancellationToken)
								).ConfigureAwait(false);
							}
						}
					}
					catch (Exception ex)
					{
						gotError = true;

						errorMessage = ex is XslTemplateIsInvalidException || ex is XslTemplateExecutionIsProhibitedException || ex is XslTemplateIsNotCompiledException
							? ex.Message
							: ex.Message.IsContains("An error occurred while loading document ''")
								? $"Transform error => The document('') will fail if the stylesheet is only in memory and was not loaded from a file. Please change to use extension objects."
								: $"Transform error => {(ex.Message.IsContains("See InnerException") && ex.InnerException != null ? ex.InnerException.Message : ex.Message)}";
						errorStack = $"\r\n => {ex.Message} [{ex.GetTypeName()}]\r\n{ex.StackTrace}";

						var inner = ex.InnerException;
						while (inner != null)
						{
							errorStack += $"\r\n\r\n ==> {inner.Message} [{inner.GetTypeName()}]\r\n{inner.StackTrace}";
							inner = inner.InnerException;
						}

						try
						{
							await this.WriteLogsAsync(requestInfo.CorrelationID,
								$"Error occurred while transforming HTML of {portletInfo} => {ex.Message}" +
								$"\r\n- XML:\r\n{xml}\r\n- XSL:\r\n{xslTemplate}{(string.IsNullOrWhiteSpace(isList ? portlet.ListSettings.Template : portlet.ViewSettings.Template) ? $"\r\n- XSL file: {portlet.Desktop?.WorkingTheme ?? "default"}/templates/{contentType.ContentTypeDefinition?.ModuleDefinition?.Directory?.ToLower() ?? "-"}/{contentType.ContentTypeDefinition?.ObjectName?.ToLower() ?? "-"}/{xslFilename}" : "")}"
							, ex, this.ServiceName, "Process.Http.Request", LogLevel.Error).ConfigureAwait(false);
						}
						catch (Exception e)
						{
							await this.WriteLogsAsync(requestInfo.CorrelationID, $"Error occurred while transforming HTML of {portletInfo} => {e.Message}", e, this.ServiceName, "Process.Http.Request", LogLevel.Error).ConfigureAwait(false);
						}
					}
				else
				{
					gotError = true;
					errorStack = data.Get<string>("Stack");
					errorType = data.Get<string>("Type");
				}

				if (!string.IsNullOrWhiteSpace(errorMessage))
					content = this.GenerateErrorHtml(errorMessage, errorStack, requestInfo.CorrelationID, portlet.ID, null, errorType);

				contentZone.Value = "{{content-holder}}";
				html = portletContainer.ToString().Replace(StringComparison.OrdinalIgnoreCase, "{{content-holder}}", content);
			}
			else
			{
				html = portletContainer.ToString();
				var tokens = "id,name,title,action,object,object-type,object-name,ansi-title,title-ansi,portlet-title,portlet-url".ToList();
				tokens.ForEach(token => html = html.Replace(StringComparison.OrdinalIgnoreCase, "{{" + token + "}}", $"[[{token}]]"));
				html = html.Format(html.PrepareDoubleBracesParameters(portlet, requestInfo, new JObject { ["Site"] = siteJson, ["Desktop"] = desktopsJson, ["Language"] = language }.ToExpandoObject()));
				tokens.ForEach(token => html = html.Replace(StringComparison.OrdinalIgnoreCase, $"[[{token}]]", "{{" + token + "}}"));
			}

			title = portlet.Title.GetANSIUri();
			html = html.Format(new Dictionary<string, object>
			{
				["id"] = portlet.ID,
				["name"] = title,
				["title"] = title,
				["action"] = isList ? "list" : "view",
				["object"] = objectType,
				["object-type"] = objectType,
				["object-name"] = objectType,
				["ansi-title"] = title,
				["title-ansi"] = title,
				["portlet-title"] = portlet.Title,
				["portlet-url"] = portlet.CommonSettings?.TitleURL ?? ""
			});

			stopwatch.Stop();
			if (writeLogs)
				await this.WriteLogsAsync(requestInfo.CorrelationID, $"HTML code of {portletInfo} has been generated - Execution times: {stopwatch.GetElapsedTimes()}", null, this.ServiceName, "Process.Http.Request").ConfigureAwait(false);

			return new Tuple<string, bool, string>(html, gotError, cacheExpiration > 0 ? cacheExpiration.ToString() : cacheExpirationTime?.ToDTString());
		}

		async Task<Tuple<string, string, string, string, string>> GenerateDesktopAsync(Desktop desktop, Organization organization, Site site, string siteHost, JObject mainPortletData, string parentIdentity, string contentIdentity, bool writeLogs = false, string correlationID = null, CancellationToken cancellationToken = default)
		{
			var desktopInfo = $"the '{desktop.Title}' desktop [Alias: {desktop.Alias} - ID: {desktop.ID}]";

			var coverURI = mainPortletData?.Get<string>("CoverURI");
			var metaInfo = mainPortletData?.Get<JArray>("MetaTags");
			var seoInfo = mainPortletData?.Get<JObject>("SEOInfo");

			var title = "";
			var titleOfPortlet = seoInfo?.Get<string>("Title") ?? "";
			var titleOfDesktop = desktop.SEOSettings?.SEOInfo?.Title ?? desktop.Title;
			var titleOfSite = site.SEOInfo?.Title ?? site.Title;
			var mode = desktop.SEOSettings?.TitleMode;
			if (mode == null)
			{
				var parentDesktop = desktop.ParentDesktop;
				while (mode == null && parentDesktop != null)
				{
					mode = parentDesktop.SEOSettings?.TitleMode;
					parentDesktop = parentDesktop?.ParentDesktop;
				}
				mode = mode ?? Settings.SEOMode.PortletAndDesktopAndSite;
			}
			switch (mode.Value)
			{
				case Settings.SEOMode.SiteAndDesktopAndPortlet:
					title = titleOfSite;
					if (!string.IsNullOrWhiteSpace(titleOfDesktop))
						title += (title != "" ? " :: " : "") + titleOfDesktop;
					if (!string.IsNullOrWhiteSpace(titleOfPortlet))
						title += (title != "" ? " :: " : "") + titleOfPortlet;
					break;
				case Settings.SEOMode.PortletAndDesktop:
					title = titleOfPortlet;
					if (!string.IsNullOrWhiteSpace(titleOfDesktop))
						title += (title != "" ? " :: " : "") + titleOfDesktop;
					break;
				case Settings.SEOMode.DesktopAndPortlet:
					title = titleOfDesktop;
					if (!string.IsNullOrWhiteSpace(titleOfPortlet))
						title += (title != "" ? " :: " : "") + titleOfPortlet;
					break;
				case Settings.SEOMode.PortletAndSite:
					title = titleOfPortlet;
					if (!string.IsNullOrWhiteSpace(titleOfSite))
						title += (title != "" ? " :: " : "") + titleOfSite;
					break;
				case Settings.SEOMode.SiteAndPortlet:
					title = titleOfSite;
					if (!string.IsNullOrWhiteSpace(titleOfPortlet))
						title += (title != "" ? " :: " : "") + titleOfPortlet;
					break;
				case Settings.SEOMode.Portlet:
					title = titleOfPortlet;
					break;
				case Settings.SEOMode.Desktop:
					title = titleOfDesktop;
					break;
			}
			if (string.IsNullOrWhiteSpace(title))
			{
				title = titleOfPortlet;
				if (!string.IsNullOrWhiteSpace(titleOfDesktop))
					title += (title != "" ? " :: " : "") + titleOfDesktop;
				if (!string.IsNullOrWhiteSpace(titleOfSite))
					title += (title != "" ? " :: " : "") + titleOfSite;
			}
			title = title.Replace("&", "&amp;").Replace("\"", "&quot;").Replace("<", "&lt;").Replace(">", "&gt;");

			var description = "";
			var descriptionOfPortlet = seoInfo?.Get<string>("Description") ?? "";
			var descriptionOfDesktop = desktop.SEOSettings?.SEOInfo?.Description ?? "";
			var descriptionOfSite = site.SEOInfo?.Description ?? "";
			mode = desktop.SEOSettings?.DescriptionMode;
			if (mode == null)
			{
				var parentDesktop = desktop.ParentDesktop;
				while (mode == null && parentDesktop != null)
				{
					mode = parentDesktop.SEOSettings?.DescriptionMode;
					parentDesktop = parentDesktop.ParentDesktop;
				}
				mode = mode ?? Settings.SEOMode.PortletAndDesktopAndSite;
			}
			switch (mode.Value)
			{
				case Settings.SEOMode.SiteAndDesktopAndPortlet:
					description = descriptionOfSite;
					if (!string.IsNullOrWhiteSpace(descriptionOfDesktop))
						description += (description != "" ? ", " : "") + descriptionOfDesktop;
					if (!string.IsNullOrWhiteSpace(descriptionOfPortlet))
						description += (description != "" ? ", " : "") + descriptionOfPortlet;
					break;
				case Settings.SEOMode.PortletAndDesktop:
					description = descriptionOfPortlet;
					if (!string.IsNullOrWhiteSpace(descriptionOfDesktop))
						description += (description != "" ? ", " : "") + descriptionOfDesktop;
					break;
				case Settings.SEOMode.DesktopAndPortlet:
					description = descriptionOfDesktop;
					if (!string.IsNullOrWhiteSpace(descriptionOfPortlet))
						description += (description != "" ? ", " : "") + descriptionOfPortlet;
					break;
				case Settings.SEOMode.PortletAndSite:
					description = descriptionOfPortlet;
					if (!string.IsNullOrWhiteSpace(descriptionOfSite))
						description += (description != "" ? ", " : "") + descriptionOfSite;
					break;
				case Settings.SEOMode.SiteAndPortlet:
					description = descriptionOfSite;
					if (!string.IsNullOrWhiteSpace(descriptionOfPortlet))
						description += (description != "" ? ", " : "") + descriptionOfPortlet;
					break;
				case Settings.SEOMode.Portlet:
					description = descriptionOfPortlet;
					break;
				case Settings.SEOMode.Desktop:
					description = descriptionOfDesktop;
					break;
			}
			if (string.IsNullOrWhiteSpace(description))
			{
				description = descriptionOfPortlet;
				if (!string.IsNullOrWhiteSpace(descriptionOfDesktop))
					description += (description != "" ? ", " : "") + descriptionOfDesktop;
				if (!string.IsNullOrWhiteSpace(descriptionOfSite))
					description += (description != "" ? ", " : "") + descriptionOfSite;
			}
			description = description.Replace("\t", "").Replace("\r", "").Replace("\n", " ");

			var keywords = "";
			var keywordsOfPortlet = seoInfo?.Get<string>("Keywords") ?? "";
			var keywordsOfDesktop = desktop.SEOSettings?.SEOInfo?.Keywords ?? "";
			var keywordsOfSite = site.SEOInfo?.Keywords ?? "";
			mode = desktop.SEOSettings?.KeywordsMode;
			if (mode == null)
			{
				var parentDesktop = desktop.ParentDesktop;
				while (mode == null && parentDesktop != null)
				{
					mode = parentDesktop.SEOSettings?.KeywordsMode;
					parentDesktop = parentDesktop?.ParentDesktop;
				}
				mode = mode ?? Settings.SEOMode.PortletAndDesktopAndSite;
			}
			switch (mode.Value)
			{
				case Settings.SEOMode.SiteAndDesktopAndPortlet:
					keywords = keywordsOfSite;
					if (!string.IsNullOrWhiteSpace(keywordsOfDesktop))
						keywords += (keywords != "" ? ", " : "") + keywordsOfDesktop;
					if (!string.IsNullOrWhiteSpace(keywordsOfPortlet))
						keywords += (keywords != "" ? ", " : "") + keywordsOfPortlet;
					break;
				case Settings.SEOMode.PortletAndDesktop:
					keywords = keywordsOfPortlet;
					if (!string.IsNullOrWhiteSpace(keywordsOfDesktop))
						keywords += (keywords != "" ? ", " : "") + keywordsOfDesktop;
					break;
				case Settings.SEOMode.DesktopAndPortlet:
					keywords = keywordsOfDesktop;
					if (!string.IsNullOrWhiteSpace(keywordsOfPortlet))
						keywords += (keywords != "" ? ", " : "") + keywordsOfPortlet;
					break;
				case Settings.SEOMode.PortletAndSite:
					keywords = keywordsOfPortlet;
					if (!string.IsNullOrWhiteSpace(keywordsOfSite))
						keywords += (keywords != "" ? ", " : "") + keywordsOfSite;
					break;
				case Settings.SEOMode.SiteAndPortlet:
					keywords = keywordsOfSite;
					if (!string.IsNullOrWhiteSpace(keywordsOfPortlet))
						keywords += (keywords != "" ? ", " : "") + keywordsOfPortlet;
					break;
				case Settings.SEOMode.Portlet:
					keywords = keywordsOfPortlet;
					break;
				case Settings.SEOMode.Desktop:
					keywords = keywordsOfDesktop;
					break;
			}
			if (string.IsNullOrWhiteSpace(keywords))
			{
				keywords = keywordsOfPortlet;
				if (!string.IsNullOrWhiteSpace(keywordsOfDesktop))
					keywords += (keywords != "" ? ", " : "") + keywordsOfDesktop;
				if (!string.IsNullOrWhiteSpace(keywordsOfSite))
					keywords += (keywords != "" ? ", " : "") + keywordsOfSite;
			}
			keywords = keywords.Replace("\t", "").Replace("\r", "").Replace("\n", " ");

			// start meta tags with information for SEO and social networks
			var metaTags = "";

			if (!string.IsNullOrWhiteSpace(description))
			{
				description = description.Replace("&", "&amp;").Replace("\"", "&quot;").Replace("<", "&lt;").Replace(">", "&gt;");
				metaTags += $"<meta name=\"description\" content=\"{description}\"/>";
				metaTags += $"<meta name=\"twitter:description\" property=\"og:description\" content=\"{description}\"/>";
			}

			metaTags += string.IsNullOrWhiteSpace(keywords) ? "" : $"<meta name=\"keywords\" content=\"{keywords.Replace("\"", "&quot;").Replace("<", "&lt;").Replace(">", "&gt;")}\"/>";
			metaTags += string.IsNullOrWhiteSpace(desktop.IconURI) ? "" : $"<link rel=\"icon\" type=\"image/{(desktop.IconURI.IsEndsWith(".ico") ? "x-icon" : desktop.IconURI.IsEndsWith(".png") ? "png" : "jpeg")}\" href=\"{desktop.IconURI}\"/><link rel=\"shortcut icon\" type=\"image/{(desktop.IconURI.IsEndsWith(".ico") ? "x-icon" : desktop.IconURI.IsEndsWith(".png") ? "png" : "jpeg")}\" href=\"{desktop.IconURI}\"/>";
			metaTags += string.IsNullOrWhiteSpace(site.IconURI) ? "" : $"<link rel=\"icon\" type=\"image/{(site.IconURI.IsEndsWith(".ico") ? "x-icon" : site.IconURI.IsEndsWith(".png") ? "png" : "jpeg")}\" href=\"{site.IconURI}\"/><link rel=\"shortcut icon\" type=\"image/{(site.IconURI.IsEndsWith(".ico") ? "x-icon" : site.IconURI.IsEndsWith(".png") ? "png" : "jpeg")}\" href=\"{site.IconURI}\"/>";

			// social network meta tags
			metaTags += $"<meta name=\"twitter:title\" property=\"og:title\" content=\"{title}\"/>";
			metaTags += $"<meta property=\"og:locale\" content=\"{(desktop.WorkingLanguage ?? site.Language ?? "en-US").Replace("-", "_")}\"/>";
			metaTags += string.IsNullOrWhiteSpace(coverURI) ? "" : $"<meta name=\"twitter:image\" property=\"og:image\" content=\"{coverURI}\"/>";
			metaTags += string.IsNullOrWhiteSpace(desktop.CoverURI) ? "" : $"<meta name=\"twitter:image\" property=\"og:image\" content=\"{desktop.CoverURI}\"/>";
			metaTags += string.IsNullOrWhiteSpace(site.CoverURI) ? "" : $"<meta name=\"twitter:image\" property=\"og:image\" content=\"{site.CoverURI}\"/>";

			// addtional meta tags of main portlet
			metaInfo?.Select(meta => (meta as JValue)?.Value?.ToString()).Where(meta => !string.IsNullOrWhiteSpace(meta)).ForEach(meta => metaTags += meta);

			// add meta tags of the organization/site/desktop
			metaTags += string.IsNullOrWhiteSpace(organization.MetaTags) ? "" : organization.MetaTags;
			metaTags += string.IsNullOrWhiteSpace(site.MetaTags) ? "" : site.MetaTags;
			metaTags += string.IsNullOrWhiteSpace(desktop.MetaTags) ? "" : desktop.MetaTags;

			// the required stylesheet libraries
			var version = organization.LastModified.AddDays(-13).GetTimeQuarter().ToUnixTimestamp();
			var stylesheets = site.UseInlineStylesheets
				? this.MinifyCss(await new FileInfo(Path.Combine(Utility.DataFilesDirectory, "assets", "default.css")).ReadAsTextAsync(cancellationToken).ConfigureAwait(false)) + await this.GetThemeResourcesAsync("default", "css", cancellationToken).ConfigureAwait(false)
				: $"<link rel=\"stylesheet\" href=\"~#/_assets/default.css?v={version}\"/><link rel=\"stylesheet\" href=\"~#/_themes/default/css/all.css?v={version}\"/>";

			// add the stylesheet of the organization theme
			var organizationTheme = organization.Theme ?? "default";
			if (!"default".IsEquals(organizationTheme))
				stylesheets += site.UseInlineStylesheets
					? await this.GetThemeResourcesAsync(organizationTheme, "css", cancellationToken).ConfigureAwait(false)
					: $"<link rel=\"stylesheet\" href=\"~#/_themes/{organizationTheme}/css/all.css?v={version}\"/>";

			// add the stylesheet of the site theme
			var siteTheme = site.WorkingTheme;
			if (!"default".IsEquals(siteTheme) && !organizationTheme.IsEquals(siteTheme))
				stylesheets += site.UseInlineStylesheets
					? await this.GetThemeResourcesAsync(siteTheme, "css", cancellationToken).ConfigureAwait(false)
					: $"<link rel=\"stylesheet\" href=\"~#/_themes/{siteTheme}/css/all.css?v={version}\"/>";

			// add the stylesheet of the desktop theme
			var desktopTheme = desktop.WorkingTheme;
			if (!"default".IsEquals(desktopTheme) && !organizationTheme.IsEquals(desktopTheme) && !siteTheme.IsEquals(desktopTheme))
				stylesheets += site.UseInlineStylesheets
					? await this.GetThemeResourcesAsync(desktopTheme, "css", cancellationToken).ConfigureAwait(false)
					: $"<link rel=\"stylesheet\" href=\"~#/_themes/{desktopTheme}/css/all.css?v={version}\"/>";

			// add the stylesheet of the site
			if (!string.IsNullOrWhiteSpace(site.Stylesheets))
				stylesheets += site.UseInlineStylesheets
					? this.MinifyCss(site.Stylesheets, siteTheme).Replace(StringComparison.OrdinalIgnoreCase, $"{Utility.FilesHttpURI}/", "~~/").Replace(StringComparison.OrdinalIgnoreCase, $"{Utility.PortalsHttpURI}/", "~#/")
					: $"<link rel=\"stylesheet\" href=\"~#/_css/s_{site.ID}.css?v={site.LastModified.ToUnixTimestamp()}\"/>";

			// add the stylesheet of the desktop
			if (!string.IsNullOrWhiteSpace(desktop.Stylesheets))
				stylesheets += site.UseInlineStylesheets
					? this.MinifyCss(desktop.Stylesheets, desktopTheme).Replace(StringComparison.OrdinalIgnoreCase, $"{Utility.FilesHttpURI}/", "~~/").Replace(StringComparison.OrdinalIgnoreCase, $"{Utility.PortalsHttpURI}/", "~#/")
					: $"<link rel=\"stylesheet\" href=\"~#/_css/d_{desktop.ID}.css?v={desktop.LastModified.ToUnixTimestamp()}\"/>";

			if (site.UseInlineStylesheets)
			{
				var imports = "";
				var start = stylesheets.PositionOf("@import url(");
				while (start > -1)
				{
					var end = stylesheets.PositionOf(")", start + 1);
					end += stylesheets.Length > end && stylesheets[end + 1] == ';' ? 2 : 1;
					imports += stylesheets.Substring(start, end - start);
					stylesheets = stylesheets.Remove(start, end - start);
					start = stylesheets.PositionOf("@import url(");
				}
				stylesheets = $"<style>{imports + stylesheets}</style>";
			}

			// add default scripts
			var scripts = "<script src=\"" + UtilityService.GetAppSetting("Portals:Desktops:Resources:JQuery", "https://cdnjs.cloudflare.com/ajax/libs/jquery/3.6.1/jquery.min.js") + "\"></script>"
				+ "<script src=\"" + UtilityService.GetAppSetting("Portals:Desktops:Resources:CryptoJs", "https://cdnjs.cloudflare.com/ajax/libs/crypto-js/4.1.1/crypto-js.min.js") + "\"></script>"
				+ (site.UseInlineScripts ? "<script>" + this.MinifyJs(await new FileInfo(Path.Combine(Utility.DataFilesDirectory, "assets", "rsa.js")).ReadAsTextAsync(cancellationToken).ConfigureAwait(false) + "\r\n" + await new FileInfo(Path.Combine(Utility.DataFilesDirectory, "assets", "default.js")).ReadAsTextAsync(cancellationToken).ConfigureAwait(false)) : $"<script src=\"~#/_assets/rsa.js?v={version}\"></script><script src=\"~#/_assets/default.js?v={version}\"></script>");

			// add scripts of the default theme
			var directory = new DirectoryInfo(Path.Combine(Utility.DataFilesDirectory, "themes", "default", "js"));
			if (directory.Exists && this.AllowSrcResourceFiles)
			{
				scripts += site.UseInlineScripts ? "</script>" : "";
				await directory.GetFiles("*.src").OrderBy(fileInfo => fileInfo.Name).ForEachAsync(async fileInfo => scripts += await fileInfo.ReadAsTextAsync(cancellationToken).ConfigureAwait(false) + "\r\n", true, false).ConfigureAwait(false);
				scripts += site.UseInlineScripts ? "<script>" : "";
			}

			scripts += site.UseInlineScripts
				? await this.GetThemeResourcesAsync("default", "js", cancellationToken).ConfigureAwait(false)
				: $"<script src=\"~#/_themes/default/js/all.js?v={version}\"></script>";

			// add scripts of the organization theme
			if (!"default".IsEquals(organizationTheme))
			{
				directory = new DirectoryInfo(Path.Combine(Utility.DataFilesDirectory, "themes", organizationTheme, "js"));
				if (directory.Exists && this.AllowSrcResourceFiles)
				{
					scripts += site.UseInlineScripts ? "</script>" : "";
					await directory.GetFiles("*.src").OrderBy(fileInfo => fileInfo.Name).ForEachAsync(async fileInfo => scripts += await fileInfo.ReadAsTextAsync(cancellationToken).ConfigureAwait(false) + "\r\n", true, false).ConfigureAwait(false);
					scripts += site.UseInlineScripts ? "<script>" : "";
				}
				scripts += site.UseInlineScripts
					? await this.GetThemeResourcesAsync(organizationTheme, "js", cancellationToken).ConfigureAwait(false)
					: $"<script src=\"~#/_themes/{organizationTheme}/js/all.js?v={version}\"></script>";
			}

			// add scripts of the site theme
			if (!"default".IsEquals(siteTheme) && !organizationTheme.IsEquals(siteTheme))
			{
				directory = new DirectoryInfo(Path.Combine(Utility.DataFilesDirectory, "themes", siteTheme, "js"));
				if (directory.Exists && this.AllowSrcResourceFiles)
				{
					scripts += site.UseInlineScripts ? "</script>" : "";
					await directory.GetFiles("*.src").OrderBy(fileInfo => fileInfo.Name).ForEachAsync(async fileInfo => scripts += await fileInfo.ReadAsTextAsync(cancellationToken).ConfigureAwait(false) + "\r\n", true, false).ConfigureAwait(false);
					scripts += site.UseInlineScripts ? "<script>" : "";
				}
				scripts += site.UseInlineScripts
					? await this.GetThemeResourcesAsync(siteTheme, "js", cancellationToken).ConfigureAwait(false)
					: $"<script src=\"~#/_themes/{siteTheme}/js/all.js?v={version}\"></script>";
			}

			// add scripts of the desktop theme
			if (!"default".IsEquals(desktopTheme) && !organizationTheme.IsEquals(desktopTheme) && !siteTheme.IsEquals(desktopTheme))
			{
				directory = new DirectoryInfo(Path.Combine(Utility.DataFilesDirectory, "themes", desktopTheme, "js"));
				if (directory.Exists && this.AllowSrcResourceFiles)
				{
					scripts += site.UseInlineScripts ? "</script>" : "";
					await directory.GetFiles("*.src").OrderBy(fileInfo => fileInfo.Name).ForEachAsync(async fileInfo => scripts += await fileInfo.ReadAsTextAsync(cancellationToken).ConfigureAwait(false) + "\r\n", true, false).ConfigureAwait(false);
					scripts += site.UseInlineScripts ? "<script>" : "";
				}
				scripts += site.UseInlineScripts
					? await this.GetThemeResourcesAsync(desktopTheme, "js", cancellationToken).ConfigureAwait(false)
					: $"<script src=\"~#/_themes/{desktopTheme}/js/all.js?v={version}\"></script>";
			}

			// add the scripts of the organization
			if (organization.IsHasJavascriptLibraries)
				scripts += site.UseInlineScripts
					? $"</script>{organization.JavascriptLibraries}<script>"
					: organization.JavascriptLibraries;

			if (organization.IsHasJavascripts)
				scripts += site.UseInlineScripts
					? this.MinifyJs(organization.Javascripts, organizationTheme).Replace(StringComparison.OrdinalIgnoreCase, $"{Utility.FilesHttpURI}/", "~~/").Replace(StringComparison.OrdinalIgnoreCase, $"{Utility.PortalsHttpURI}/", "~#/")
					: $"<script src=\"~#/_js/o_{organization.ID}.js?v={organization.LastModified.ToUnixTimestamp()}\"></script>";

			// add the scripts of the site
			if (!string.IsNullOrWhiteSpace(site.ScriptLibraries))
				scripts += site.UseInlineScripts
					? $"</script>{site.ScriptLibraries}<script>"
					: site.ScriptLibraries;

			if (!string.IsNullOrWhiteSpace(site.Scripts))
				scripts += site.UseInlineScripts
					? this.MinifyJs(site.Scripts, siteTheme).Replace(StringComparison.OrdinalIgnoreCase, $"{Utility.FilesHttpURI}/", "~~/").Replace(StringComparison.OrdinalIgnoreCase, $"{Utility.PortalsHttpURI}/", "~#/")
					: $"<script src=\"~#/_js/s_{site.ID}.js?v={site.LastModified.ToUnixTimestamp()}\"></script>";

			// add the scripts of the desktop
			if (!string.IsNullOrWhiteSpace(desktop.ScriptLibraries))
				scripts += site.UseInlineScripts
					? $"</script>{desktop.ScriptLibraries}<script>"
					: desktop.ScriptLibraries;

			if (!string.IsNullOrWhiteSpace(desktop.Scripts))
				scripts += site.UseInlineScripts
					? this.MinifyJs(desktop.Scripts, desktopTheme).Replace(StringComparison.OrdinalIgnoreCase, $"{Utility.FilesHttpURI}/", "~~/").Replace(StringComparison.OrdinalIgnoreCase, $"{Utility.PortalsHttpURI}/", "~#/")
					: $"<script src=\"~#/_js/d_{desktop.ID}.js?v={desktop.LastModified.ToUnixTimestamp()}\"></script>";

			scripts += site.UseInlineScripts ? "</script>" : "";

			// prepare desktop zones
			var desktopContainer = (await desktop.GetTemplateAsync(cancellationToken).ConfigureAwait(false)).GetXDocument();
			var desktopZones = desktopContainer.GetZones().ToList();
			if (writeLogs)
				await this.WriteLogsAsync(correlationID, $"Prepare the zone(s) of {desktopInfo} => {desktopZones.GetZoneNames().Join(", ")}", null, this.ServiceName, "Process.Http.Request").ConfigureAwait(false);

			var removedZones = new List<XElement>();
			var desktopZonesGotPortlet = desktop.Portlets.Select(portlet => portlet.Zone).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
			desktopZones.ForEach(zone =>
			{
				var idAttribute = zone.GetZoneIDAttribute();
				if (desktopZonesGotPortlet.IndexOf(idAttribute.Value) < 0)
				{
					// get parent  element
					var parent = zone.Parent;

					// remove this empty zone
					removedZones.Add(zone);
					zone.Remove();

					// add css class '.empty' to parent element
					if (!parent.HasElements)
					{
						parent.Value = " ";
						var cssAttribute = parent.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName.IsEquals("class"));
						if (cssAttribute == null)
							parent.Add(new XAttribute("class", "empty"));
						else if (!cssAttribute.Value.IsContains("empty"))
							cssAttribute.Value = $"{cssAttribute.Value.Trim()} empty";
					}
				}
				else
				{
					zone.Value = "{{" + idAttribute.Value + "-holder}}";
					idAttribute.Remove();
				}
			});

			removedZones.ForEach(zone => desktopZones.Remove(zone));
			if (writeLogs)
				await this.WriteLogsAsync(correlationID, $"Remove empty zone(s) of {desktopInfo} => {removedZones.GetZoneNames().Join(", ")}", null, this.ServiceName, "Process.Http.Request").ConfigureAwait(false);

			// add css class '.full' to a zone that the parent only got this zone
			desktopZones.Where(zone => zone.Parent.Elements().Count() == 1).Where(zone => zone.Parent.Attribute("class") == null || !zone.Parent.Attribute("class").Value.IsContains("fixed")).ForEach(zone =>
			{
				var cssAttribute = zone.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName.IsEquals("class"));
				if (cssAttribute == null)
					zone.Add(new XAttribute("class", "full"));
				else
					cssAttribute.Value = $"{cssAttribute.Value.Trim()} full";
			});

			// prepare main portlet for generating
			string mainPortletType = "", mainPortletAction = "", mainPortletTitle = "", mainPortletURL = "";
			string mainPortletParentTitle = "", mainPortletParentURL = "", mainPortletParentRootTitle = "", mainPortletParentRootURL = "", mainPortletContentTitle = "", mainPortletContentURL = "";
			var mainPortlet = mainPortletData != null ? desktop.Portlets.Find(portlet => portlet.ID == desktop.MainPortletID) : null;
			if (mainPortlet != null)
			{
				var contentType = await (mainPortlet.RepositoryEntityID ?? "").GetContentTypeByIDAsync(cancellationToken).ConfigureAwait(false);
				mainPortletType = contentType?.ContentTypeDefinition?.GetObjectName() ?? "";
				var action = !string.IsNullOrWhiteSpace(parentIdentity) && !string.IsNullOrWhiteSpace(contentIdentity) ? mainPortlet.OriginalPortlet.AlternativeAction : mainPortlet.OriginalPortlet.Action;
				mainPortletAction = string.IsNullOrWhiteSpace(action) || "List".IsEquals(action) ? "List" : "View";
				mainPortletTitle = mainPortlet.Title;
				mainPortletURL = mainPortlet.CommonSettings?.TitleURL ?? "";
				var xdoc = mainPortletData.Get<JValue>("Data")?.ToString()?.ToXml();
				var xnode = xdoc?.Element("Parent");
				mainPortletParentTitle = xnode?.Element("Title")?.Value ?? "";
				mainPortletParentURL = xnode?.Element("URL")?.Value ?? "";
				xnode = xnode?.Element("Root");
				mainPortletParentRootTitle = xnode?.Value ?? "";
				mainPortletParentRootURL = xnode?.Attribute("URL")?.Value ?? "";
				xnode = xdoc?.Element("Content");
				mainPortletContentTitle = xnode?.Element("Title")?.Value ?? "";
				mainPortletContentURL = xnode?.Element("URL")?.Value ?? "";
			}

			// get the desktop body
			var language = desktop.WorkingLanguage ?? site.Language ?? "vi-VN";
			var body = desktopContainer.ToString(SaveOptions.DisableFormatting).Format(new Dictionary<string, object>
			{
				["theme"] = desktopTheme,
				["skin"] = desktopTheme,
				["organization"] = organization.Alias,
				["organization-alias"] = organization.Alias,
				["organization-title"] = organization.Title,
				["site"] = site.Title,
				["site-title"] = site.Title,
				["site-host"] = siteHost,
				["site-domain"] = site.Host,
				["home-title"] = language.IsEquals("vi-VN") ? "Trang chủ" : "Home",
				["home-url"] = (language.IsEquals("vi-VN") ? "~/vi" : "~/en") + (organization.AlwaysUseHtmlSuffix ? ".html" : ""),
				["alias"] = desktop.Alias,
				["desktop"] = desktop.Alias,
				["desktop-alias"] = desktop.Alias,
				["desktop-title"] = desktop.Title,
				["desktop-url"] = $"~/{desktop.Alias}" + (organization.AlwaysUseHtmlSuffix ? ".html" : ""),
				["parent-identity"] = parentIdentity ?? "",
				["content-identity"] = contentIdentity ?? "",
				["main-portlet-type"] = mainPortletType.ToLower().Replace(".", "-"),
				["main-portlet-action"] = mainPortletAction.ToLower(),
				["main-portlet-title"] = mainPortletTitle,
				["main-portlet-url"] = mainPortletURL,
				["main-portlet-parent-title"] = mainPortletParentTitle,
				["main-portlet-parent-url"] = mainPortletParentURL,
				["main-portlet-parent-root-title"] = mainPortletParentRootTitle,
				["main-portlet-parent-root-url"] = mainPortletParentRootURL,
				["main-portlet-content-title"] = mainPortletContentTitle,
				["main-portlet-content-url"] = mainPortletContentURL
			});

			return new Tuple<string, string, string, string, string>(title, metaTags, body, stylesheets, scripts);
		}

		string NormalizeDesktopHtml(string html, Organization organization, Site site, Desktop desktop)
		{
			var homeDesktop = site.HomeDesktop != null ? $"\"{site.HomeDesktop.Alias}{(organization.AlwaysUseHtmlSuffix ? ".html" : "")}\"" : "undefined";
			var searchDesktop = site.SearchDesktop != null ? $"\"{site.SearchDesktop.Alias}{(organization.AlwaysUseHtmlSuffix ? ".html" : "")}\"" : "undefined";
			var language = "\"" + (desktop.WorkingLanguage ?? site.Language ?? "vi-VN") + "\"";
			return html.Format(new Dictionary<string, object>
			{
				["home"] = homeDesktop,
				["homedesktop"] = homeDesktop,
				["home-desktop"] = homeDesktop,
				["search"] = searchDesktop,
				["searchdesktop"] = searchDesktop,
				["search-desktop"] = searchDesktop,
				["organization-alias"] = organization.Alias,
				["desktop-alias"] = desktop.Alias,
				["language"] = language,
				["culture"] = language,
				["locale"] = language.Replace("-", "_")
			});
		}

		string NormalizeDesktopHtml(string html, Uri requestURI, bool useShortURLs, Organization organization, Site site, Desktop desktop, string isMobile, string osInfo, string correlationID)
			=> this.NormalizeDesktopHtml(html, organization, site, desktop).Format(new Dictionary<string, object>
			{
				["isMobile"] = isMobile,
				["is-mobile"] = isMobile,
				["osInfo"] = osInfo,
				["os-info"] = osInfo,
				["osPlatform"] = osInfo.GetANSIUri(),
				["os-platform"] = osInfo.GetANSIUri(),
				["osMode"] = "true".IsEquals(isMobile) ? "mobile-os" : "desktop-os",
				["os-mode"] = "true".IsEquals(isMobile) ? "mobile-os" : "desktop-os",
				["correlationID"] = correlationID,
				["correlation-id"] = correlationID
			}).NormalizeURLs(requestURI, organization.Alias, useShortURLs, true, string.IsNullOrWhiteSpace(organization.FakeFilesHttpURI) ? null : organization.FakeFilesHttpURI, string.IsNullOrWhiteSpace(organization.FakePortalsHttpURI) ? null : organization.FakePortalsHttpURI);

		JObject GenerateErrorJson(Exception exception, RequestInfo requestInfo, bool addErrorStack, string errorMessage = null)
		{
			var json = new JObject
			{
				{ "Code", (int)HttpStatusCode.InternalServerError },
				{ "Error", string.IsNullOrWhiteSpace(errorMessage) ? exception.Message : $"{errorMessage} => {exception.Message}" },
				{ "Type", exception.GetTypeName(true) }
			};
			if (exception is WampException wampException)
			{
				var details = wampException.GetDetails(requestInfo);
				json["Code"] = details.Item1;
				json["Error"] = details.Item3.Equals("AccessDeniedException") ? details.Item2 : string.IsNullOrWhiteSpace(errorMessage) ? details.Item2 : $"{errorMessage} => {details.Item2}";
				json["Type"] = details.Item3;
				if (addErrorStack)
					json["Stack"] = details.Item4;
			}
			else if (addErrorStack)
				json["Stack"] = exception.StackTrace;
			json["CorrelationID"] = requestInfo.CorrelationID;
			return json;
		}

		string GenerateErrorHtml(string errorMessage, string errorStack, string correlationID, string objectID, string objectIDLabel = null, string errorType = null)
			=> "<div>"
				+ $"<div style=\"color:red\">{errorMessage.Replace("\"", "&quot;").Replace("<", "&lt;").Replace(">", "&gt;")}</div>"
				+ $"<div style=\"font-size:80%\">Correlation ID: {correlationID} - {objectIDLabel ?? "Portlet ID"}: {objectID}</div>"
				+ (this.IsDebugLogEnabled
					? $"<div style=\"font-size:80%\">{errorStack?.Replace("\"", "&quot;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\r\n", "<br/>")}</div>"
					: $"<!-- {errorStack?.Replace("\"", "&quot;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\r\n", "<br/>")} -->")
				+ ("AccessDeniedException".IsEquals(errorType) ? $"<div style=\"padding:30px 0\">Please <a href=\"javascript:__login()\">click here</a> to login and try again</div>" : "")
				+ "</div>";
		#endregion

		#region Generate data for working with CMS Portals
		public async Task<JObject> GenerateAsync(RequestInfo requestInfo, CancellationToken cancellationToken = default)
		{
			var stopwatch = Stopwatch.StartNew();
			try
			{
				JObject json = null;
				var isSystemAdministrator = await this.IsSystemAdministratorAsync(requestInfo, cancellationToken).ConfigureAwait(false);
				switch (requestInfo.ObjectName.ToLower().Trim())
				{
					case "category":
					case "cms.category":
						json = await CategoryProcessor.GenerateAsync(requestInfo, isSystemAdministrator, cancellationToken).ConfigureAwait(false);
						break;

					case "content":
					case "cms.content":
						json = await ContentProcessor.GenerateAsync(requestInfo, isSystemAdministrator, cancellationToken).ConfigureAwait(false);
						break;

					case "item":
					case "cms.item":
						json = await ItemProcessor.GenerateAsync(requestInfo, isSystemAdministrator, cancellationToken).ConfigureAwait(false);
						break;

					case "link":
					case "cms.link":
						json = await LinkProcessor.GenerateAsync(requestInfo, isSystemAdministrator, cancellationToken).ConfigureAwait(false);
						break;

					case "form":
					case "cms.form":
						json = FormProcessor.Generate(requestInfo);
						break;

					default:
						throw new InvalidRequestException();
				}
				stopwatch.Stop();
				if (requestInfo.IsWriteDesktopLogs())
					await this.WriteLogsAsync(requestInfo, $"Data of a CMS Portals object [{requestInfo.ObjectName}] was generated/prepared - Execution times: {stopwatch.GetElapsedTimes()}\r\n- Request: {requestInfo.ToString(this.JsonFormat)}" + "\r\n" + $"- Response: {json?.ToString(this.JsonFormat)}").ConfigureAwait(false);
				return json;
			}
			catch (Exception ex)
			{
				throw this.GetRuntimeException(requestInfo, ex);
			}
		}

		public async Task<JArray> GenerateMenuAsync(RequestInfo requestInfo, CancellationToken cancellationToken = default)
		{
			var stopwatch = Stopwatch.StartNew();

			// get the module
			var repositoryID = requestInfo.GetParameter("x-menu-repository-id");
			var module = await (repositoryID ?? "").GetModuleByIDAsync(cancellationToken).ConfigureAwait(false);
			if (module == null)
				throw new InformationNotFoundException($"The module of the requested menu is not found [ID: {repositoryID}]");

			// get the content-type
			var repositoryEntityID = requestInfo.GetParameter("x-menu-repository-entity-id");
			var contentType = await (repositoryEntityID ?? "").GetContentTypeByIDAsync(cancellationToken).ConfigureAwait(false);
			if (contentType == null)
				throw new InformationNotFoundException($"The content-type of the requested menu is not found [ID: {repositoryEntityID}]");

			// get the object
			var repositoryObjectID = requestInfo.GetParameter("x-menu-repository-object-id");
			var @object = await this.GetBusinessObjectAsync(repositoryEntityID, repositoryObjectID, cancellationToken).ConfigureAwait(false);
			if (@object == null)
				throw new InformationNotFoundException($"The requested menu is not found [Content-Type ID: {contentType.ID} - Menu ID: {repositoryObjectID}]");
			if (!(@object is INestedObject))
				throw new InformationInvalidException($"The requested menu is invalid (its not nested object) [Content-Type ID: {contentType.ID} - Menu ID: {repositoryObjectID}]");

			// check permission
			var gotRights = await this.IsSystemAdministratorAsync(requestInfo, cancellationToken).ConfigureAwait(false) || await this.CanModerateAsync(requestInfo, "Organization", cancellationToken).ConfigureAwait(false) || requestInfo.Session.User.IsViewer(@object.WorkingPrivileges);
			if (!gotRights)
			{
				var organization = @object is IPortalObject
					? await ((@object as IPortalObject).OrganizationID ?? "").GetOrganizationByIDAsync(cancellationToken).ConfigureAwait(false)
					: module.Organization;
				gotRights = requestInfo.Session.User.ID.IsEquals(organization?.OwnerID);
			}
			if (!gotRights)
				return null;

			// check the children
			var children = (@object as INestedObject).Children;
			if (children == null || !children.Any())
				return null;

			// get thumbnails
			var options = requestInfo.BodyAsJson.Get("Options", new JObject()).ToExpandoObject();
			requestInfo.Header["x-thumbnails-as-attachments"] = "true";
			var thumbnails = children.Count == 1
				? await requestInfo.GetThumbnailsAsync(children[0].ID, children[0].Title.Url64Encode(), this.ValidationKey, cancellationToken).ConfigureAwait(false)
				: await requestInfo.GetThumbnailsAsync(children.Select(child => child.ID).Join(","), children.ToJObject("ID", child => new JValue(child.Title.Url64Encode())).ToString(Newtonsoft.Json.Formatting.None), this.ValidationKey, cancellationToken).ConfigureAwait(false);

			// generate and return the menu
			var pngThumbnails = options.Get("ThumbnailsAsPng", options.Get("ThumbnailAsPng", options.Get("ShowPngThumbnails", options.Get("ShowAsPngThumbnails", false))));
			var bigThumbnails = options.Get("ThumbnailsAsBig", options.Get("ThumbnailAsBig", options.Get("ShowBigThumbnails", options.Get("ShowAsBigThumbnails", false))));
			var thumbnailsWidth = options.Get("ThumbnailsWidth", options.Get("ThumbnailWidth", 0));
			var thumbnailsHeight = options.Get("ThumbnailsHeight", options.Get("ThumbnailHeight", 0));

			if (!Int32.TryParse(requestInfo.GetParameter("x-menu-level") ?? "1", out var level))
				level = 1;
			if (!Int32.TryParse(requestInfo.GetParameter("x-menu-max-level") ?? "1", out var maxLevel))
				maxLevel = 0;

			var menu = new JArray();
			Exception exception = null;
			await children.Where(child => child != null).OrderBy(child => child.OrderIndex).ForEachAsync(async child =>
			{
				if (exception == null)
					try
					{
						if (child is Category category)
							menu.Add(await requestInfo.GenerateMenuAsync(category, thumbnails?.GetThumbnailURL(child.ID, pngThumbnails, bigThumbnails, thumbnailsWidth, thumbnailsHeight), level, maxLevel, pngThumbnails, bigThumbnails, thumbnailsWidth, thumbnailsHeight, cancellationToken).ConfigureAwait(false));
						else if (child is Link link)
							menu.Add(await requestInfo.GenerateMenuAsync(link, thumbnails?.GetThumbnailURL(child.ID, pngThumbnails, bigThumbnails, thumbnailsWidth, thumbnailsHeight), level, maxLevel, pngThumbnails, bigThumbnails, thumbnailsWidth, thumbnailsHeight, cancellationToken).ConfigureAwait(false));
					}
					catch (Exception ex)
					{
						exception = ex;
					}
			}, true, false).ConfigureAwait(false);

			if (exception != null)
				throw requestInfo.GetRuntimeException(exception, null, async (msg, ex) => await requestInfo.WriteErrorAsync(ex, $"Error occurred while generating a child menu => {msg} : {@object.ToJson()}", "Links").ConfigureAwait(false));

			stopwatch.Stop();
			if (requestInfo.IsWriteDesktopLogs())
				await this.WriteLogsAsync(requestInfo, $"Data of a CMS Portals object [{@object.GetObjectName()} => {@object.Title}] was generated/prepared as menu - Execution times: {stopwatch.GetElapsedTimes()}\r\n- Request: {requestInfo.ToString(this.JsonFormat)}" + "\r\n" + $"- Response: {menu?.ToString(this.JsonFormat)}").ConfigureAwait(false);

			return menu;
		}
		#endregion

		#region Export/Import objects (Working with Excel files)
		async Task<JToken> DoExcelActionAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			var requestJson = requestInfo.GetRequestJson();
			var objectName = requestJson.Get("ObjectName", "").Trim();

			var contentType = await (requestJson.Get<string>("RepositoryEntityID") ?? requestJson.Get<string>("ContentTypeID") ?? "").GetContentTypeByIDAsync(cancellationToken).ConfigureAwait(false);
			var module = contentType?.Module ?? await (requestJson.Get<string>("RepositoryID") ?? requestJson.Get<string>("ModuleID") ?? "").GetModuleByIDAsync(cancellationToken).ConfigureAwait(false);
			var organization = contentType?.Organization ?? module?.Organization ?? await (requestJson.Get<string>("SystemID") ?? requestJson.Get<string>("OrganizationID") ?? "").GetOrganizationByIDAsync(cancellationToken).ConfigureAwait(false);

			var gotRights = await this.IsSystemAdministratorAsync(requestInfo, cancellationToken).ConfigureAwait(false);
			if (!gotRights)
				switch (objectName.ToLower())
				{
					case "role":
					case "core.role":
					case "site":
					case "core.site":
					case "desktop":
					case "core.desktop":
					case "portlet":
					case "core.portlet":
					case "module":
					case "core.module":
					case "contenttype":
					case "content.type":
					case "content-type":
					case "core.contenttype":
					case "core.content.type":
					case "expression":
					case "core.expression":
					case "task":
					case "schedulingtask":
					case "scheduling.task":
					case "scheduling-task":
					case "core.task":
					case "core.schedulingtask":
					case "core.scheduling.task":
					case "core.scheduling-task":
						gotRights = requestInfo.Session.User.IsAdministrator(null, null, organization);
						break;

					case "category":
					case "cms.category":
						gotRights = requestInfo.Session.User.IsModerator(module?.WorkingPrivileges, null, organization);
						break;

					case "content":
					case "cms.content":
					case "item":
					case "cms.item":
					case "link":
					case "cms.link":
					case "form":
					case "cms.form":
						gotRights = requestInfo.Session.User.IsEditor(contentType?.WorkingPrivileges, contentType?.Module?.WorkingPrivileges, organization);
						break;
				}
			if (!gotRights)
				throw new AccessDeniedException();

			var processID = requestInfo.CorrelationID ?? UtilityService.NewUUID;
			var deviceID = requestInfo.Session.DeviceID;

			if ("export".IsEquals(requestInfo.GetObjectIdentity()))
			{
				var filterBy = requestJson.Get<JObject>("FilterBy");
				var sortBy = requestJson.Get<JObject>("SortBy");
				var pagination = requestJson.Get("Pagination", new JObject());
				var pageSize = pagination.Get("PageSize", 20);
				var pageNumber = pagination.Get("PageNumber", 1);
				var maxPages = pagination.Get("MaxPages", 0);
				switch (objectName.ToLower())
				{
					case "organization":
					case "core.organization":
						this.Export(processID, deviceID, contentType?.ID, this.GetFilter<Organization>(filterBy), sortBy?.ToSortBy<Organization>(), pageSize, pageNumber, maxPages);
						break;

					case "role":
					case "core.role":
						this.Export(processID, deviceID, contentType?.ID,
							this.GetFilter(filterBy, filter =>
							{
								if (filter.GetValue("SystemID") == null)
									filter.Add(Filters<Role>.Equals("SystemID", organization.ID));
							},
							Filters<Role>.Equals("SystemID", organization.ID)),
							sortBy?.ToSortBy<Role>(), pageSize, pageNumber, maxPages);
						break;

					case "site":
					case "core.site":
						this.Export(processID, deviceID, contentType?.ID,
							this.GetFilter(filterBy, filter =>
							{
								if (filter.GetValue("SystemID") == null)
									filter.Add(Filters<Site>.Equals("SystemID", organization.ID));
							},
							Filters<Site>.Equals("SystemID", organization.ID)),
							sortBy?.ToSortBy<Site>(), pageSize, pageNumber, maxPages);
						break;

					case "desktop":
					case "core.desktop":
						this.Export(processID, deviceID, contentType?.ID,
							this.GetFilter(filterBy, filter =>
							{
								if (filter.GetValue("SystemID") == null)
									filter.Add(Filters<Desktop>.Equals("SystemID", organization.ID));
							},
							Filters<Desktop>.Equals("SystemID", organization.ID)),
							sortBy?.ToSortBy<Desktop>(), pageSize, pageNumber, maxPages);
						break;

					case "portlet":
					case "core.portlet":
						this.Export(processID, deviceID, contentType?.ID,
							this.GetFilter(filterBy, async filter =>
							{
								if (filter.GetValue("SystemID") == null)
									filter.Add(Filters<Portlet>.Equals("SystemID", organization.ID));
								if (filter.GetValue("DesktopID") == null)
								{
									var desktop = await (requestJson.Get<string>("DesktopID") ?? "").GetDesktopByIDAsync(cancellationToken).ConfigureAwait(false);
									if (desktop != null)
										filter.Add(Filters<Portlet>.Equals("DesktopID", desktop.ID));
								}
							},
							Filters<Portlet>.Equals("SystemID", organization.ID)),
							sortBy?.ToSortBy<Portlet>(), pageSize, pageNumber, maxPages);
						break;

					case "module":
					case "core.module":
						this.Export(processID, deviceID, contentType?.ID,
							this.GetFilter(filterBy, filter =>
							{
								if (filter.GetValue("SystemID") == null)
									filter.Add(Filters<Module>.Equals("SystemID", organization.ID));
							},
							Filters<Module>.Equals("SystemID", organization.ID)),
							sortBy?.ToSortBy<Module>(), pageSize, pageNumber, maxPages);
						break;

					case "contenttype":
					case "content.type":
					case "content-type":
					case "core.contenttype":
					case "core.content.type":
						this.Export(processID, deviceID, contentType?.ID,
							this.GetFilter(filterBy, filter =>
							{
								if (filter.GetValue("SystemID") == null)
									filter.Add(Filters<ContentType>.Equals("SystemID", organization.ID));
							},
							Filters<ContentType>.Equals("SystemID", organization.ID)),
							sortBy?.ToSortBy<ContentType>(), pageSize, pageNumber, maxPages);
						break;

					case "expression":
					case "core.expression":
						this.Export(processID, deviceID, contentType?.ID,
							this.GetFilter(filterBy, filter =>
							{
								if (filter.GetValue("SystemID") == null)
									filter.Add(Filters<Expression>.Equals("SystemID", organization.ID));
							},
							Filters<Expression>.Equals("SystemID", organization.ID)),
							sortBy?.ToSortBy<Expression>(), pageSize, pageNumber, maxPages);
						break;

					case "task":
					case "schedulingtask":
					case "scheduling.task":
					case "scheduling-task":
					case "core.task":
					case "core.schedulingtask":
					case "core.scheduling.task":
					case "core.scheduling-task":
						this.Export(processID, deviceID, contentType?.ID,
							this.GetFilter(filterBy, filter =>
							{
								if (filter.GetValue("SystemID") == null)
									filter.Add(Filters<SchedulingTask>.Equals("SystemID", organization.ID));
							},
							Filters<SchedulingTask>.Equals("SystemID", organization.ID)),
							sortBy?.ToSortBy<SchedulingTask>(), pageSize, pageNumber, maxPages);
						break;

					case "category":
					case "cms.category":
						this.Export(processID, deviceID, contentType?.ID,
							this.GetFilter(filterBy, filter =>
							{
								if (filter.GetValue("SystemID") == null)
									filter.Add(Filters<Category>.Equals("SystemID", organization.ID));
								if (filter.GetValue("RepositoryID") == null && module != null)
									filter.Add(Filters<Category>.Equals("RepositoryID", module.ID));
								if (filter.GetValue("RepositoryEntityID") == null && contentType != null)
									filter.Add(Filters<Category>.Equals("RepositoryEntityID", contentType.ID));
							},
							Filters<Category>.Equals("SystemID", organization.ID)),
							sortBy?.ToSortBy<Category>(), pageSize, pageNumber, maxPages);
						break;

					case "content":
					case "cms.content":
						this.Export(processID, deviceID, contentType?.ID,
							this.GetFilter(filterBy, filter =>
							{
								if (filter.GetValue("SystemID") == null)
									filter.Add(Filters<Content>.Equals("SystemID", organization.ID));
								if (filter.GetValue("RepositoryID") == null && module != null)
									filter.Add(Filters<Content>.Equals("RepositoryID", module.ID));
								if (filter.GetValue("RepositoryEntityID") == null && contentType != null)
									filter.Add(Filters<Content>.Equals("RepositoryEntityID", contentType.ID));
							},
							Filters<Content>.Equals("SystemID", organization.ID)),
							sortBy?.ToSortBy<Content>(), pageSize, pageNumber, maxPages);
						break;

					case "item":
					case "cms.item":
						this.Export(processID, deviceID, contentType?.ID,
							this.GetFilter(filterBy, filter =>
							{
								if (filter.GetValue("SystemID") == null)
									filter.Add(Filters<Item>.Equals("SystemID", organization.ID));
								if (filter.GetValue("RepositoryID") == null && module != null)
									filter.Add(Filters<Item>.Equals("RepositoryID", module.ID));
								if (filter.GetValue("RepositoryEntityID") == null && contentType != null)
									filter.Add(Filters<Item>.Equals("RepositoryEntityID", contentType.ID));
							},
							Filters<Item>.Equals("SystemID", organization.ID)),
							sortBy?.ToSortBy<Item>(), pageSize, pageNumber, maxPages);
						break;

					case "link":
					case "cms.link":
						this.Export(processID, deviceID, contentType?.ID,
							this.GetFilter(filterBy, filter =>
							{
								if (filter.GetValue("SystemID") == null)
									filter.Add(Filters<Link>.Equals("SystemID", organization.ID));
								if (filter.GetValue("RepositoryID") == null && module != null)
									filter.Add(Filters<Link>.Equals("RepositoryID", module.ID));
								if (filter.GetValue("RepositoryEntityID") == null && contentType != null)
									filter.Add(Filters<Link>.Equals("RepositoryEntityID", contentType.ID));
							},
							Filters<Link>.Equals("SystemID", organization.ID)),
							sortBy?.ToSortBy<Link>(), pageSize, pageNumber, maxPages);
						break;

					case "form":
					case "cms.form":
						this.Export(processID, deviceID, contentType?.ID,
							this.GetFilter(filterBy, filter =>
							{
								if (filter.GetValue("SystemID") == null)
									filter.Add(Filters<Form>.Equals("SystemID", organization.ID));
								if (filter.GetValue("RepositoryID") == null && module != null)
									filter.Add(Filters<Form>.Equals("RepositoryID", module.ID));
								if (filter.GetValue("RepositoryEntityID") == null && contentType != null)
									filter.Add(Filters<Form>.Equals("RepositoryEntityID", contentType.ID));
							},
							Filters<Form>.Equals("SystemID", organization.ID)),
							sortBy?.ToSortBy<Form>(), pageSize, pageNumber, maxPages);
						break;
				}
			}
			else if ("import".IsEquals(requestInfo.GetObjectIdentity()))
			{
				requestInfo.Header["x-filename"] = requestJson.Get<string>("Filename") ?? requestJson.Get<string>("x-filename");
				requestInfo.Header["x-node"] = requestJson.Get<string>("NodeID") ?? requestJson.Get<string>("x-node");
				var filename = await requestInfo.DownloadTemporaryFileAsync(cancellationToken).ConfigureAwait(false);
				var userID = requestInfo.Session.User.ID;
				var regenerateID = requestInfo.GetParameter("x-regenerate-id") != null;
				switch (objectName.ToLower())
				{
					case "organization":
					case "core.organization":
						this.Import<Organization>(processID, deviceID, userID, filename, contentType?.ID, regenerateID, objects => objects.ForEach(@object =>
						{
							@object.Set();
							new UpdateMessage
							{
								Type = $"{requestInfo.ServiceName}#{objectName}#Update",
								Data = @object.ToJson(),
								DeviceID = "*"
							}.Send();
							new CommunicateMessage(requestInfo.ServiceName)
							{
								Type = $"{objectName}#Update",
								Data = @object.ToJson(),
								ExcludedNodeID = Utility.NodeID
							}.Send();
						}));
						break;

					case "role":
					case "core.role":
						this.Import<Role>(processID, deviceID, userID, filename, contentType?.ID, regenerateID, objects => objects.ForEach(@object =>
						{
							@object.Set();
							new UpdateMessage
							{
								Type = $"{requestInfo.ServiceName}#{objectName}#Update",
								Data = @object.ToJson(),
								DeviceID = "*"
							}.Send();
							new CommunicateMessage(requestInfo.ServiceName)
							{
								Type = $"{objectName}#Update",
								Data = @object.ToJson(),
								ExcludedNodeID = Utility.NodeID
							}.Send();
						}));
						break;

					case "site":
					case "core.site":
						this.Import<Site>(processID, deviceID, userID, filename, contentType?.ID, regenerateID, objects => objects.ForEach(@object =>
						{
							@object.Set();
							new UpdateMessage
							{
								Type = $"{requestInfo.ServiceName}#{objectName}#Update",
								Data = @object.ToJson(),
								DeviceID = "*"
							}.Send();
							new CommunicateMessage(requestInfo.ServiceName)
							{
								Type = $"{objectName}#Update",
								Data = @object.ToJson(),
								ExcludedNodeID = Utility.NodeID
							}.Send();
						}));
						break;

					case "desktop":
					case "core.desktop":
						this.Import<Desktop>(processID, deviceID, userID, filename, contentType?.ID, regenerateID, objects => objects.ForEach(@object =>
						{
							@object.Set();
							new UpdateMessage
							{
								Type = $"{requestInfo.ServiceName}#{objectName}#Update",
								Data = @object.ToJson(),
								DeviceID = "*"
							}.Send();
							new CommunicateMessage(requestInfo.ServiceName)
							{
								Type = $"{objectName}#Update",
								Data = @object.ToJson(),
								ExcludedNodeID = Utility.NodeID
							}.Send();
						}));
						break;

					case "portlet":
					case "core.portlet":
						this.Import<Portlet>(processID, deviceID, userID, filename, contentType?.ID);
						break;

					case "module":
					case "core.module":
						this.Import<Module>(processID, deviceID, userID, filename, contentType?.ID, regenerateID, objects => objects.ForEach(@object =>
						{
							@object.Set();
							new UpdateMessage
							{
								Type = $"{requestInfo.ServiceName}#{objectName}#Update",
								Data = @object.ToJson(),
								DeviceID = "*"
							}.Send();
							new CommunicateMessage(requestInfo.ServiceName)
							{
								Type = $"{objectName}#Update",
								Data = @object.ToJson(),
								ExcludedNodeID = Utility.NodeID
							}.Send();
						}));
						break;

					case "contenttype":
					case "content.type":
					case "content-type":
					case "core.contenttype":
					case "core.content.type":
						this.Import<ContentType>(processID, deviceID, userID, filename, contentType?.ID, regenerateID, objects => objects.ForEach(@object =>
						{
							@object.Set();
							new UpdateMessage
							{
								Type = $"{requestInfo.ServiceName}#{objectName}#Update",
								Data = @object.ToJson(),
								DeviceID = "*"
							}.Send();
							new CommunicateMessage(requestInfo.ServiceName)
							{
								Type = $"{objectName}#Update",
								Data = @object.ToJson(),
								ExcludedNodeID = Utility.NodeID
							}.Send();
						}));
						break;

					case "expression":
					case "core.expression":
						this.Import<Expression>(processID, deviceID, userID, filename, contentType?.ID, regenerateID, objects => objects.ForEach(@object =>
						{
							@object.Set();
							new UpdateMessage
							{
								Type = $"{requestInfo.ServiceName}#{objectName}#Update",
								Data = @object.ToJson(),
								DeviceID = "*"
							}.Send();
							new CommunicateMessage(requestInfo.ServiceName)
							{
								Type = $"{objectName}#Update",
								Data = @object.ToJson(),
								ExcludedNodeID = Utility.NodeID
							}.Send();
						}));
						break;

					case "task":
					case "schedulingtask":
					case "scheduling.task":
					case "scheduling-task":
					case "core.task":
					case "core.schedulingtask":
					case "core.scheduling.task":
					case "core.scheduling-task":
						this.Import<SchedulingTask>(processID, deviceID, userID, filename, contentType?.ID, regenerateID, objects => objects.ForEach(@object =>
						{
							@object.Set();
							new UpdateMessage
							{
								Type = $"{requestInfo.ServiceName}#{objectName}#Update",
								Data = @object.ToJson(),
								DeviceID = "*"
							}.Send();
							new CommunicateMessage(requestInfo.ServiceName)
							{
								Type = $"{objectName}#Update",
								Data = @object.ToJson(),
								ExcludedNodeID = Utility.NodeID
							}.Send();
						}));
						break;

					case "category":
					case "cms.category":
						this.Import<Category>(processID, deviceID, userID, filename, contentType?.ID, regenerateID, async objects => await objects.ForEachAsync(async @object =>
						{
							@object.Set();
							await @object.ClearRelatedCacheAsync(this.CancellationToken).ConfigureAwait(false);
							new UpdateMessage
							{
								Type = $"{requestInfo.ServiceName}#{objectName}#Update",
								Data = @object.ToJson(),
								DeviceID = "*"
							}.Send();
							new CommunicateMessage(requestInfo.ServiceName)
							{
								Type = $"{objectName}#Update",
								Data = @object.ToJson(),
								ExcludedNodeID = Utility.NodeID
							}.Send();
						}).ConfigureAwait(false));
						break;

					case "content":
					case "cms.content":
						this.Import<Content>(processID, deviceID, userID, filename, contentType?.ID, regenerateID, async objects => await objects.ForEachAsync(async @object => await @object.ClearRelatedCacheAsync(this.CancellationToken).ConfigureAwait(false)).ConfigureAwait(false));
						break;

					case "item":
					case "cms.item":
						this.Import<Item>(processID, deviceID, userID, filename, contentType?.ID, regenerateID, async objects => await objects.ForEachAsync(async @object => await @object.ClearRelatedCacheAsync(this.CancellationToken).ConfigureAwait(false)).ConfigureAwait(false));
						break;

					case "link":
					case "cms.link":
						this.Import<Link>(processID, deviceID, userID, filename, contentType?.ID, regenerateID, async objects => await objects.ForEachAsync(async @object => await @object.ClearRelatedCacheAsync(this.CancellationToken).ConfigureAwait(false)).ConfigureAwait(false));
						break;

					case "form":
					case "cms.form":
						this.Import<Form>(processID, deviceID, userID, filename, contentType?.ID, regenerateID, async objects => await objects.ForEachAsync(async @object => await @object.ClearRelatedCacheAsync(this.CancellationToken).ConfigureAwait(false)).ConfigureAwait(false));
						break;
				}
			}
			else
				throw new InvalidRequestException($"The request is invalid [({requestInfo.Verb}): {requestInfo.GetURI()}]");

			return new JObject
			{
				{ "ProcessID", processID },
				{ "Status", "Processing" },
				{ "Percentage", "0%" }
			};
		}

		IFilterBy<T> GetFilter<T>(JObject filterBy, Action<FilterBys<T>> onCompleted = null, IFilterBy<T> @default = null) where T : class
		{
			if (filterBy?.ToFilterBy<T>() is FilterBys<T> filter)
			{
				onCompleted?.Invoke(filter);
				return filter;
			}
			return @default;
		}

		void Export<T>(string processID, string deviceID, string repositoryEntityID, IFilterBy<T> filter, SortBy<T> sort, int pageSize, int pageNumber, int maxPages, int totalPages = 0, Action<DataSet> onCompleted = null) where T : class
			=> Task.Run(async () =>
			{
				try
				{
					var stopwatch = Stopwatch.StartNew();
					if (this.IsDebugLogEnabled)
						await this.WriteLogsAsync(processID, $"Start to export data to Excel - Object: {typeof(T).GetTypeName(true)} - Filter: {filter?.ToJson().ToString(Newtonsoft.Json.Formatting.None) ?? "N/A"} - Sort: {sort?.ToJson().ToString(Newtonsoft.Json.Formatting.None) ?? "N/A"}", null, this.ServiceName, "Excel").ConfigureAwait(false);

					long totalRecords = 0;
					if (totalPages < 1)
					{
						totalRecords = await RepositoryMediator.CountAsync(null, filter, repositoryEntityID, false, null, 0, this.CancellationToken).ConfigureAwait(false);
						totalPages = totalRecords < 1 ? 0 : new Tuple<long, int>(totalRecords, pageSize).GetTotalPages();
					}

					var dataSet = totalPages < 1
						? ExcelService.ToDataSet<T>(null, repositoryEntityID)
						: null;

					var exceptions = new List<Exception>();
					while (pageNumber <= totalPages && (maxPages == 0 || pageNumber <= maxPages))
					{
						new UpdateMessage
						{
							Type = "Portals#Excel#Export",
							DeviceID = deviceID,
							Data = new JObject
							{
								{ "ProcessID", processID },
								{ "Status", "Processing" },
								{ "Percentage", $"{pageNumber * 100/totalPages:#0.0}%" }
							}
						}.Send();

						try
						{
							var objects = pageNumber <= totalPages && (maxPages == 0 || pageNumber <= maxPages)
								? await RepositoryMediator.FindAsync(null, filter, sort, pageSize, pageNumber, repositoryEntityID, false, null, 0, this.CancellationToken).ConfigureAwait(false)
								: new List<T>();
							if (pageNumber < 2)
								dataSet = objects.ToDataSet(repositoryEntityID);
							else
								dataSet.Tables[0].UpdateDataTable(objects, repositoryEntityID);
						}
						catch (Exception ex)
						{
							exceptions.Add(new RepositoryOperationException($"Error occurred while preparing objects to export to Excel => {ex.GetTypeName(true)}: {ex.Message}", ex));
							await this.WriteLogsAsync(processID, $"Error occurred while preparing objects to export to Excel => {ex.GetTypeName(true)}: {ex.Message}", ex, this.ServiceName, "Excel").ConfigureAwait(false);
						}
						pageNumber++;
					}

					var filename = $"{processID}-{typeof(T).GetTypeName(true)}.xlsx";
					if (dataSet != null)
					{
						using (var stream = dataSet.SaveAsExcel())
							await stream.SaveAsBinaryAsync(Path.Combine(this.GetPath("Temp", Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data-files", "temp")), filename), this.CancellationToken).ConfigureAwait(false);
						onCompleted?.Invoke(dataSet);
					}

					new UpdateMessage
					{
						Type = "Portals#Excel#Export",
						DeviceID = deviceID,
						Data = new JObject
						{
							{ "ProcessID", processID },
							{ "Status", "Done" },
							{ "Percentage", "100%" },
							{ "Filename", filename },
							{ "NodeID", $"{this.ServiceName.Trim().ToLower()}.{this.NodeID}" },
							{
								"Exceptions",
								exceptions.Select(exception => new JObject
								{
									{ "Type", exception.GetType().ToString() },
									{ "Message", exception.Message },
									{ "Stack", exception.StackTrace }
								}).ToJArray()
							}
						}
					}.Send();

					stopwatch.Stop();
					if (this.IsDebugLogEnabled)
						await this.WriteLogsAsync(processID, $"Export objects to Excel was completed - Total: {totalRecords:###,###,##0} - Execution times: {stopwatch.GetElapsedTimes()}", null, this.ServiceName, "Excel").ConfigureAwait(false);
				}
				catch (Exception ex)
				{
					var code = 500;
					var type = ex.GetTypeName(true);
					var message = ex.Message;
					var stack = ex.StackTrace;
					if (ex is WampException wampException)
					{
						var wampDetails = wampException.GetDetails();
						code = wampDetails.Item1;
						type = wampDetails.Item2;
						message = wampDetails.Item3;
						stack = wampDetails.Item4;
					}
					new UpdateMessage
					{
						Type = "Portals#Excel#Export",
						DeviceID = deviceID,
						Data = new JObject
						{
							{ "ProcessID", processID },
							{ "Status", "Error" },
							{
								"Error", new JObject
								{
									{ "Code", code },
									{ "Type", type },
									{ "Message", message },
									{ "Stack", stack }
								}
							}
						}
					}.Send();
					await this.WriteLogsAsync(processID, $"Error occurred while exporting objects to Excel => {message}", ex, this.ServiceName, "Excel").ConfigureAwait(false);
				}
			}, this.CancellationToken).ConfigureAwait(false);

		void Import<T>(string processID, string deviceID, string userID, string filename, string repositoryEntityID, bool regenerateID = false, Action<IEnumerable<T>> onCompleted = null) where T : class
			=> Task.Run(async () =>
			{
				try
				{
					var stopwatch = Stopwatch.StartNew();
					if (this.IsDebugLogEnabled)
						await this.WriteLogsAsync(processID, $"Start to import objects from Excel - Object: {typeof(T).GetTypeName(true)} - Data file: {filename}", null, this.ServiceName, "Excel").ConfigureAwait(false);

					// read the Excel file
					var dataSet = ExcelService.ReadExcelAsDataSet(Path.Combine(this.GetPath("Temp", Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data-files", "temp")), filename));
					var objects = dataSet.ToObjects<T>(repositoryEntityID);
					var contentType = !string.IsNullOrWhiteSpace(repositoryEntityID) && repositoryEntityID.IsValidUUID()
						? await repositoryEntityID.GetContentTypeByIDAsync(this.CancellationToken).ConfigureAwait(false)
						: null;
					var objectName = contentType?.ContentTypeDefinition?.GetObjectName();

					// do import
					var totalRecords = objects.Count();
					var counter = 0;
					var exceptions = new List<Exception>();
					await objects.ForEachAsync(async @object =>
					{
						var @event = "Update";
						try
						{
							// prepare
							var bizObject = @object is IBusinessEntity ? @object as IBusinessEntity : null;
							var aliasObject = @object is IAliasEntity ? @object as IAliasEntity : null;

							if (bizObject != null)
							{
								bizObject.ID = string.IsNullOrWhiteSpace(bizObject.ID) ? UtilityService.NewUUID : bizObject.ID;
								bizObject.LastModified = DateTime.Now;
								bizObject.LastModifiedID = userID;

								if (contentType != null)
								{
									bizObject.SystemID = string.IsNullOrWhiteSpace(bizObject.SystemID) ? contentType.SystemID : bizObject.SystemID;
									bizObject.RepositoryID = string.IsNullOrWhiteSpace(bizObject.RepositoryID) ? contentType.RepositoryID : bizObject.RepositoryID;
									bizObject.RepositoryEntityID = string.IsNullOrWhiteSpace(bizObject.RepositoryEntityID) ? contentType.ID : bizObject.RepositoryEntityID;
								}
							}

							// re-generate the identity
							if (regenerateID)
								new[] { "ID", "Id" }.ForEach(name =>
								{
									if (@object.GetAttributeValue(name) is string objectID)
										@object.SetAttributeValue(name, objectID.GenerateUUID());
								});

							// update database
							var existed = await RepositoryMediator.GetAsync<T>(null, @object.GetEntityID(), this.CancellationToken).ConfigureAwait(false);
							if (existed != null)
							{
								@object.GetPublicAttributes(attribute => !attribute.IsStatic && attribute.CanRead && attribute.CanWrite && @object.GetAttributeValue(attribute) == null).ForEach(attribute => @object.SetAttributeValue(attribute, existed.GetAttributeValue(attribute)));
								try
								{
									await RepositoryMediator.UpdateAsync(null, @object, false, userID, this.CancellationToken).ConfigureAwait(false);
								}
								catch (Exception ex)
								{
									if (ex is RepositoryOperationException && ex.InnerException != null && ex.InnerException is InformationExistedException && ex.InnerException.Message.IsContains("A key was existed") && aliasObject != null)
									{
										aliasObject.Alias = $"{aliasObject.Alias}-{aliasObject.ID}".NormalizeAlias();
										await RepositoryMediator.UpdateAsync(null, @object, false, userID, this.CancellationToken).ConfigureAwait(false);
									}
									else
										throw;
								}
							}

							else
							{
								@event = "Create";
								if (bizObject != null)
								{
									bizObject.Created = bizObject.LastModified = DateTime.Now;
									bizObject.CreatedID = bizObject.LastModifiedID = userID;
								}

								if (aliasObject != null && string.IsNullOrWhiteSpace(aliasObject.Alias))
									aliasObject.Alias = (aliasObject.Title ?? aliasObject.ID).NormalizeAlias();

								try
								{
									await RepositoryMediator.CreateAsync(null, @object, this.CancellationToken).ConfigureAwait(false);
								}
								catch (Exception ex)
								{
									if (ex is RepositoryOperationException && ex.InnerException != null && ex.InnerException is InformationExistedException && ex.InnerException.Message.IsContains("A key was existed") && aliasObject != null)
									{
										aliasObject.Alias = $"{aliasObject.Alias}-{aliasObject.ID}".NormalizeAlias();
										await RepositoryMediator.CreateAsync(null, @object, this.CancellationToken).ConfigureAwait(false);
									}
									else
										throw;
								}
							}

							// send update message
							objectName = objectName ?? (@object as RepositoryBase)?.GetObjectName();
							new UpdateMessage
							{
								Type = $"{ServiceBase.ServiceComponent.ServiceName}#{objectName}#Update",
								DeviceID = "*",
								Data = (@object as RepositoryBase)?.ToJson()
							}.Send();

							// clear related cache
							if (@object is Category category)
								category.Set().ClearRelatedCacheAsync(this.CancellationToken, processID).Run();
							else if (@object is Content content)
								content.ClearRelatedCacheAsync(this.CancellationToken, processID).Run();
							else if (@object is Item item)
								item.ClearRelatedCacheAsync(this.CancellationToken, processID).Run();
							else if (@object is Link link)
								link.ClearRelatedCacheAsync(this.CancellationToken, processID).Run();
							else if (@object is Form form)
								form.ClearRelatedCacheAsync(this.CancellationToken, processID).Run();
						}
						catch (Exception ex)
						{
							ex = ex is RepositoryOperationException ? ex.InnerException : ex;
							exceptions.Add(new RepositoryOperationException($"Error ({@event}) {@object.GetType()}#{@object.GetEntityID()}: [{@object.GetAttributeValue("Title")}] => {ex.GetTypeName(true)}: {ex.Message}", ex));
							await this.WriteLogsAsync(processID, $"Error occurred while importing ({@event}) an object [{@object.GetType()}#{@object.GetEntityID()} => {@object.GetAttributeValue("Title")}] => {ex.GetTypeName(true)}: {ex.Message}", ex, this.ServiceName, "Excel").ConfigureAwait(false);
						}

						counter++;
						new UpdateMessage
						{
							Type = "Portals#Excel#Import",
							DeviceID = deviceID,
							Data = new JObject
							{
								{ "ProcessID", processID },
								{ "Status", "Processing" },
								{ "Percentage", $"{counter * 100/totalRecords:#0.0}%" }
							}
						}.Send();
					}, true, false).ConfigureAwait(false);

					// final
					onCompleted?.Invoke(objects);
					new UpdateMessage
					{
						Type = "Portals#Excel#Import",
						DeviceID = deviceID,
						Data = new JObject
						{
							{ "ProcessID", processID },
							{ "Status", "Done" },
							{ "Percentage", "100%" },
							{
								"Exceptions",
								exceptions.Select(exception => new JObject
								{
									{ "Type", exception.GetType().ToString() },
									{ "Message", exception.Message },
									{ "Stack", exception.StackTrace }
								}).ToJArray()
							}
						}
					}.Send();

					stopwatch.Stop();
					if (this.IsDebugLogEnabled)
						await this.WriteLogsAsync(processID, $"Import objects from Excel was completed - Total: {totalRecords:###,###,##0} - Execution times: {stopwatch.GetElapsedTimes()}", null, this.ServiceName, "Excel").ConfigureAwait(false);
				}
				catch (Exception ex)
				{
					var code = 500;
					var type = ex.GetTypeName(true);
					var message = ex.Message;
					var stack = ex.StackTrace;
					if (ex is WampException wampException)
					{
						var wampDetails = wampException.GetDetails();
						code = wampDetails.Item1;
						type = wampDetails.Item2;
						message = wampDetails.Item3;
						stack = wampDetails.Item4;
					}
					new UpdateMessage
					{
						Type = "Portals#Excel#Import",
						DeviceID = deviceID,
						Data = new JObject
						{
							{ "ProcessID", processID },
							{ "Status", "Error" },
							{
								"Error", new JObject
								{
									{ "Code", code },
									{ "Type", type },
									{ "Message", message },
									{ "Stack", stack }
								}
							}
						}
					}.Send();
					await this.WriteLogsAsync(processID, $"Error occurred while importing objects from Excel => {message}", ex, this.ServiceName, "Excel").ConfigureAwait(false);
				}
			}, this.CancellationToken).ConfigureAwait(false);
		#endregion

		#region Sync objects (via sync requests or web-hook messages)
		public override async Task<JToken> SyncAsync(RequestInfo requestInfo, CancellationToken cancellationToken = default)
		{
			var stopwatch = Stopwatch.StartNew();
			await this.WriteLogsAsync(requestInfo, $"Start sync ({requestInfo.Verb} {requestInfo.GetURI()})").ConfigureAwait(false);
			using (var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, this.CancellationToken))
				try
				{
					// validate & sync
					var json = await base.SyncAsync(requestInfo, cts.Token).ConfigureAwait(false);
					json = await this.SyncObjectAsync(requestInfo, cts.Token, requestInfo.GetHeaderParameter("x-converter") == null).ConfigureAwait(false);

					// write logs
					stopwatch.Stop();
					await this.WriteLogsAsync(requestInfo, $"Sync success - Execution times: {stopwatch.GetElapsedTimes()}").ConfigureAwait(false);
					if (this.IsDebugResultsEnabled)
						this.WriteLogs(requestInfo, $"- Request: {requestInfo.ToString(this.JsonFormat)}" + "\r\n" + $"- Response: {json?.ToString(this.JsonFormat)}");

					// return info
					return json;
				}
				catch (Exception ex)
				{
					throw this.GetRuntimeException(requestInfo, ex, stopwatch);
				}
		}

		protected override Task SendSyncRequestAsync(RequestInfo requestInfo, CancellationToken cancellationToken = default)
			=> base.SendSyncRequestAsync(requestInfo, cancellationToken);

		public override async Task ProcessWebHookMessageAsync(RequestInfo requestInfo, CancellationToken cancellationToken = default)
		{
			var stopwatch = Stopwatch.StartNew();
			if (this.IsDebugLogEnabled)
				await this.WriteLogsAsync(requestInfo, $"Start process a web-hook message").ConfigureAwait(false);

			using (var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, this.CancellationToken))
				try
				{
					// prepare
					var organization = await (requestInfo.GetHeaderParameter("SystemID") ?? "").GetOrganizationByIDAsync(cts.Token).ConfigureAwait(false);
					if (organization == null)
						throw new InformationInvalidException("Invalid (no system)");

					var settings = organization.WebHookSettings ?? new Settings.WebHook();
					var signAlgorithm = settings.SignAlgorithm;
					var signKey = settings.SignKey ?? requestInfo.Session.AppID ?? organization.ID;
					var signatureName = settings.SignatureName;
					var signatureAsHex = settings.SignatureAsHex;
					var signatureInQuery = settings.SignatureInQuery;
					var query = new Dictionary<string, string>();
					if (!string.IsNullOrWhiteSpace(settings.AdditionalQuery))
						(settings.AdditionalQuery.ToJson() as JObject).ForEach(kvp => query[kvp.Key] = kvp.Value.ToString());
					var header = new Dictionary<string, string>();
					if (!string.IsNullOrWhiteSpace(settings.AdditionalHeader))
						(settings.AdditionalHeader.ToJson() as JObject).ForEach(kvp => header[kvp.Key] = kvp.Value.ToString());

					// validate
					requestInfo.ValidateWebHookMessage(signAlgorithm, signKey, signatureName, signatureAsHex, signatureInQuery, query, header);

					// sync object
					await this.SyncObjectAsync(requestInfo, cts.Token, false).ConfigureAwait(false);
					stopwatch.Stop();
					if (this.IsDebugLogEnabled)
						await this.WriteLogsAsync(requestInfo, $"Process a web-hook message successful - Execution times: {stopwatch.GetElapsedTimes()}" + (this.IsDebugResultsEnabled ? $"\r\nMessage: {requestInfo.ToString(this.JsonFormat)}" : "")).ConfigureAwait(false);
				}
				catch (Exception ex)
				{
					throw this.GetRuntimeException(requestInfo, ex, stopwatch, "Error occurred while processing a web-hook message");
				}
		}

		Task<JObject> SyncObjectAsync(RequestInfo requestInfo, CancellationToken cancellationToken, bool sendNotifications)
		{
			switch (requestInfo.ObjectName.ToLower())
			{
				case "organization":
				case "core.organization":
					return requestInfo.SyncOrganizationAsync(cancellationToken, sendNotifications);

				case "role":
				case "core.role":
					return requestInfo.SyncRoleAsync(cancellationToken, sendNotifications);

				case "module":
				case "core.module":
					return requestInfo.SyncModuleAsync(cancellationToken, sendNotifications);

				case "contenttype":
				case "content.type":
				case "core.contenttype":
				case "core.content.type":
					return requestInfo.SyncContentTypeAsync(cancellationToken, sendNotifications);

				case "expression":
				case "core.expression":
					return requestInfo.SyncExpressionAsync(cancellationToken, sendNotifications);

				case "site":
				case "core.site":
					return requestInfo.SyncSiteAsync(cancellationToken, sendNotifications);

				case "desktop":
				case "core.desktop":
					return requestInfo.SyncDesktopAsync(cancellationToken, sendNotifications);

				case "portlet":
				case "core.portlet":
					return requestInfo.SyncPortletAsync(cancellationToken);

				case "task":
				case "schedulingtask":
				case "scheduling.task":
				case "scheduling-task":
				case "core.task":
				case "core.schedulingtask":
				case "core.scheduling.task":
				case "core.scheduling-task":
					return requestInfo.SyncSchedulingTaskAsync(cancellationToken);

				case "category":
				case "cms.category":
					return requestInfo.SyncCategoryAsync(cancellationToken, sendNotifications);

				case "content":
				case "cms.content":
					return requestInfo.SyncContentAsync(cancellationToken, sendNotifications);

				case "item":
				case "cms.item":
					return requestInfo.SyncItemAsync(cancellationToken, sendNotifications);

				case "link":
				case "cms.link":
					return requestInfo.SyncLinkAsync(cancellationToken, sendNotifications);

				case "form":
				case "cms.form":
					return requestInfo.SyncFormAsync(cancellationToken, sendNotifications);

				case "crawler":
				case "crawlers":
				case "cms.crawler":
				case "cms.crawlers":
					return requestInfo.SyncCrawlerAsync(cancellationToken, sendNotifications);

				default:
					return Task.FromException<JObject>(new InvalidRequestException($"The request is invalid ({requestInfo.Verb} {requestInfo.GetURI()})"));
			}
		}
		#endregion

		#region Process communicate message of Portals service
		protected override async Task ProcessInterCommunicateMessageAsync(CommunicateMessage message, CancellationToken cancellationToken = default)
		{
			// check
			if (message?.Type == null || message?.Data == null)
				return;

			var stopwatch = Stopwatch.StartNew();

			// messages of an organization
			if (message.Type.IsStartsWith("Organization#"))
			{
				if (message.Type.IsEquals("Organization#Refresh"))
					this.TimeOfRefreshOrganizations = DateTime.Now;
				else
					await message.ProcessInterCommunicateMessageOfOrganizationAsync(cancellationToken).ConfigureAwait(false);
			}

			// messages of a site
			else if (message.Type.IsStartsWith("Site#"))
				await message.ProcessInterCommunicateMessageOfSiteAsync(cancellationToken).ConfigureAwait(false);

			// messages a role
			else if (message.Type.IsStartsWith("Role#"))
				await message.ProcessInterCommunicateMessageOfRoleAsync(cancellationToken).ConfigureAwait(false);

			// messages of a desktop
			else if (message.Type.IsStartsWith("Desktop#"))
				await message.ProcessInterCommunicateMessageOfDesktopAsync(cancellationToken).ConfigureAwait(false);

			// messages of a portlet
			else if (message.Type.IsStartsWith("Portlet#"))
				await message.ProcessInterCommunicateMessageOfPortletAsync(cancellationToken).ConfigureAwait(false);

			// messages a module
			else if (message.Type.IsStartsWith("Module#"))
				await message.ProcessInterCommunicateMessageOfModuleAsync(cancellationToken).ConfigureAwait(false);

			// messages a content-type
			else if (message.Type.IsStartsWith("ContentType#"))
				await message.ProcessInterCommunicateMessageOfContentTypeAsync(cancellationToken).ConfigureAwait(false);

			// messages an expression
			else if (message.Type.IsStartsWith("Expression#"))
				await message.ProcessInterCommunicateMessageOfExpressionAsync(cancellationToken).ConfigureAwait(false);

			// messages a scheduling task
			else if (message.Type.IsStartsWith("SchedulingTask#"))
				await message.ProcessInterCommunicateMessageOfSchedulingTaskAsync(cancellationToken).ConfigureAwait(false);

			// messages of a CMS category
			else if (message.Type.IsStartsWith("Category#") || message.Type.IsStartsWith("CMS.Category#"))
				await message.ProcessInterCommunicateMessageOfCategoryAsync(cancellationToken).ConfigureAwait(false);

			// messages of a CMS crawler
			else if (message.Type.IsStartsWith("Crawler#") || message.Type.IsStartsWith("CMS.Crawler#"))
				await message.ProcessInterCommunicateMessageOfCrawlerAsync(cancellationToken).ConfigureAwait(false);

			stopwatch.Stop();
			if (Utility.IsWriteMessageLogs(null))
				await Utility.WriteLogAsync(UtilityService.NewUUID, $"Process an inter-communicate message successful - Execution times: {stopwatch.GetElapsedTimes()}\r\n{message?.ToJson()}", "Updates").ConfigureAwait(false);
		}
		#endregion

		#region Process communicate message of CMS Portals service
		async Task ProcessCommunicateMessageAsync(CommunicateMessage message, CancellationToken cancellationToken = default)
		{
			var correlationID = UtilityService.NewUUID;
			try
			{
				if (message.Type.IsEquals("Definition#RequestInfo"))
					this.SendDefinitionInfo();

				else if (message.Type.IsEquals("Definition#Info"))
				{
					var moduleDefinition = message.Data?.ToExpandoObject()?.Copy<ModuleDefinition>();
					if (this.IsDebugLogEnabled)
						await this.WriteLogsAsync(correlationID, $"Got an update of a module definition\r\n{message.Data}", null, this.ServiceName, "Updates").ConfigureAwait(false);
					this.UpdateDefinition(message.Data?.ToExpandoObject()?.Copy<ModuleDefinition>());
				}
			}
			catch (Exception ex)
			{
				await this.WriteLogsAsync(correlationID, $"Error occurred while processing an inter-communicate message => {ex.Message}", ex, this.ServiceName, "Updates").ConfigureAwait(false);
			}
		}

		void SendDefinitionInfo()
			=> new CommunicateMessage("CMS.Portals")
			{
				Type = "Definition#Info",
				Data = this.GetDefinition().ToJson()
			}.Send();
		#endregion

		#region Clear cache of a core object
		async Task<JToken> ClearCacheAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			// validate
			if (!requestInfo.Verb.IsEquals("GET"))
				throw new InvalidRequestException($"The request is invalid [({requestInfo.Verb}): {requestInfo.GetURI()}]");

			var identity = requestInfo.GetObjectIdentity(true, true) ?? "";
			if (string.IsNullOrWhiteSpace(identity))
				throw new InvalidRequestException($"The request is invalid [({requestInfo.Verb}): {requestInfo.GetURI()}]");

			// check permissions
			var isSystemAdministrator = await this.IsSystemAdministratorAsync(requestInfo, cancellationToken).ConfigureAwait(false);
			var gotRights = false;
			Organization organization = null;
			Module module = null;
			ContentType contentType = null;
			Site site = null;
			Desktop desktop = null;
			Expression expression = null;
			switch (requestInfo.GetObjectIdentity().ToLower())
			{
				case "organization":
				case "core.organization":
					organization = await identity.GetOrganizationByIDAsync(cancellationToken).ConfigureAwait(false);
					gotRights = isSystemAdministrator || requestInfo.Session.User.IsAdministrator(null, null, organization);
					break;

				case "module":
				case "core.module":
					module = await identity.GetModuleByIDAsync(cancellationToken).ConfigureAwait(false);
					gotRights = isSystemAdministrator || requestInfo.Session.User.IsAdministrator(module?.WorkingPrivileges, null, module?.Organization);
					break;

				case "contenttype":
				case "content.type":
				case "core.contenttype":
				case "core.content.type":
					contentType = await identity.GetContentTypeByIDAsync(cancellationToken).ConfigureAwait(false);
					gotRights = isSystemAdministrator || requestInfo.Session.User.IsAdministrator(contentType?.WorkingPrivileges, contentType?.Module?.WorkingPrivileges, contentType?.Organization);
					break;

				case "site":
				case "core.site":
					site = await identity.GetSiteByIDAsync(cancellationToken).ConfigureAwait(false);
					gotRights = isSystemAdministrator || requestInfo.Session.User.IsAdministrator(null, null, site?.Organization);
					break;

				case "desktop":
				case "core.desktop":
					desktop = await identity.GetDesktopByIDAsync(cancellationToken).ConfigureAwait(false);
					gotRights = isSystemAdministrator || requestInfo.Session.User.IsAdministrator(null, null, desktop?.Organization);
					break;

				case "expression":
				case "core.expression":
					expression = await identity.GetExpressionByIDAsync(cancellationToken).ConfigureAwait(false);
					gotRights = isSystemAdministrator || requestInfo.Session.User.IsAdministrator(null, null, expression?.Organization);
					break;
			}

			if (!gotRights)
				throw new AccessDeniedException();

			// clear related cache
			var stopwatch = Stopwatch.StartNew();
			if (Utility.IsCacheLogEnabled)
				await Utility.WriteLogAsync(requestInfo.CorrelationID, $"Clear all cache{(organization != null ? " of the whole organization" : "")} [{requestInfo.GetURI()}]", "Caches").ConfigureAwait(false);

			if (organization != null)
				await organization.ClearCacheAsync(cancellationToken, requestInfo.CorrelationID, true, true, true, false).ConfigureAwait(false);

			else if (module != null)
			{
				await module.ClearCacheAsync(cancellationToken, requestInfo.CorrelationID, true, true, true, false).ConfigureAwait(false);
				module = await Module.GetAsync<Module>(module.ID, cancellationToken).ConfigureAwait(false);
				await module.FindContentTypesAsync(cancellationToken, false).ConfigureAwait(false);
				await module.SetAsync(true, cancellationToken).ConfigureAwait(false);
			}

			else if (contentType != null)
			{
				await contentType.ClearCacheAsync(cancellationToken, requestInfo.CorrelationID, true, true, true, false).ConfigureAwait(false);
				contentType = await ContentType.GetAsync<ContentType>(contentType.ID, cancellationToken).ConfigureAwait(false);
				await contentType.SetAsync(true, cancellationToken).ConfigureAwait(false);
			}

			else if (site != null)
			{
				await site.ClearCacheAsync(cancellationToken, requestInfo.CorrelationID, true, true, false).ConfigureAwait(false);
				site = await Site.GetAsync<Site>(site.ID, cancellationToken).ConfigureAwait(false);
				desktop = await Desktop.GetAsync<Desktop>(site.HomeDesktopID ?? site.Organization?.HomeDesktopID, cancellationToken).ConfigureAwait(false);
				if (desktop != null)
					await Task.WhenAll
					(
						desktop.FindChildrenAsync(cancellationToken, false),
						desktop.FindPortletsAsync(cancellationToken, false)
					).ConfigureAwait(false);
				await Task.WhenAll
				(
					site.SetAsync(false, true, cancellationToken),
					desktop != null ? desktop.SetAsync(false, true, cancellationToken) : Task.CompletedTask
				).ConfigureAwait(false);
				if (desktop != null)
					await $"{Utility.PortalsHttpURI}/~{site.Organization?.Alias}/{desktop.Alias}".RefreshWebPageAsync(0, requestInfo.CorrelationID, $"Refresh home desktop when related cache of a site was clean [{site.Title} - ID: {site.ID}]");
			}

			else if (desktop != null)
			{
				await desktop.ClearCacheAsync(cancellationToken, requestInfo.CorrelationID, true, true, true).ConfigureAwait(false);
				desktop = await Desktop.GetAsync<Desktop>(desktop.ID, cancellationToken).ConfigureAwait(false);
				await Task.WhenAll
				(
					desktop.FindChildrenAsync(cancellationToken, false),
					desktop.FindPortletsAsync(cancellationToken, false)
				).ConfigureAwait(false);
				await desktop.SetAsync(false, true, cancellationToken).ConfigureAwait(false);
			}

			else if (expression != null)
			{
				await Utility.Cache.RemoveAsync(expression.Remove(), cancellationToken).ConfigureAwait(false);
				new CommunicateMessage(requestInfo.ServiceName)
				{
					Type = $"{expression.GetObjectName()}#Delete",
					Data = expression.ToJson(),
					ExcludedNodeID = Utility.NodeID
				}.Send();
				await expression.ID.GetExpressionByIDAsync(cancellationToken, true).ConfigureAwait(false);
			}

			stopwatch.Stop();
			if (Utility.IsCacheLogEnabled)
				await Utility.WriteLogAsync(requestInfo.CorrelationID, $"Clear related cache successful - Execution times: {stopwatch.GetElapsedTimes()}", "Caches").ConfigureAwait(false);

			return new JObject();
		}
		#endregion

		#region Approval an object (organization/site/cms content)
		async Task<JToken> ApproveAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			// prepare
			if (!requestInfo.Verb.IsEquals("GET"))
				throw new InvalidRequestException($"The request is invalid [({requestInfo.Verb}): {requestInfo.GetURI()}]");

			if (!Enum.TryParse<ApprovalStatus>(requestInfo.GetParameter("Status") ?? requestInfo.GetParameter("x-status"), out var approvalStatus))
				throw new InvalidRequestException($"The request is invalid [({requestInfo.Verb}): {requestInfo.GetURI()}]");

			var entityInfo = requestInfo.GetParameter("RepositoryEntityID") ?? requestInfo.GetParameter("x-entity");
			if (!(await this.GetBusinessObjectAsync(entityInfo, requestInfo.GetObjectIdentity(true), cancellationToken).ConfigureAwait(false) is IPortalObject @object))
				throw new InvalidRequestException($"The request is invalid [({requestInfo.Verb}): {requestInfo.GetURI()}]");

			var organization = @object is Organization
				? @object as Organization
				: await (@object.OrganizationID ?? "").GetOrganizationByIDAsync(cancellationToken).ConfigureAwait(false);
			if (organization == null)
				throw new InvalidRequestException($"The request is invalid [({requestInfo.Verb}): {requestInfo.GetURI()}]");

			var site = @object is Site
				? @object as Site
				: null;

			var bizObject = @object is IBusinessObject
				? @object as IBusinessObject
				: null;

			var update = false;
			var oldStatus = ApprovalStatus.Draft;
			var gotRights = await this.IsSystemAdministratorAsync(requestInfo, cancellationToken).ConfigureAwait(false);

			switch (approvalStatus)
			{
				case ApprovalStatus.Draft:
				case ApprovalStatus.Pending:
					if (bizObject != null)
					{
						oldStatus = bizObject.Status;
						update = !approvalStatus.Equals(bizObject.Status);
						if (!gotRights)
							gotRights = (int)bizObject.Status < (int)ApprovalStatus.Approved
								? requestInfo.Session.User.ID.IsEquals(@object.CreatedID)
								: requestInfo.Session.User.IsEditor(@object.WorkingPrivileges, bizObject.ContentType?.WorkingPrivileges, bizObject.Organization as Organization);
					}
					else if (site != null)
					{
						oldStatus = site.Status;
						update = !approvalStatus.Equals(site.Status);
					}
					else if (@object is Organization)
					{
						oldStatus = organization.Status;
						update = !approvalStatus.Equals(organization.Status);
					}
					break;

				case ApprovalStatus.Rejected:
				case ApprovalStatus.Approved:
					if (bizObject != null)
					{
						oldStatus = bizObject.Status;
						update = !approvalStatus.Equals(bizObject.Status);
						if (!gotRights)
							gotRights = (int)bizObject.Status < (int)ApprovalStatus.Approved
								? requestInfo.Session.User.IsEditor(@object.WorkingPrivileges, bizObject.ContentType?.WorkingPrivileges, bizObject.Organization as Organization)
								: requestInfo.Session.User.IsModerator(@object.WorkingPrivileges, bizObject.ContentType?.WorkingPrivileges, bizObject.Organization as Organization);
					}
					else if (site != null)
					{
						oldStatus = site.Status;
						update = !approvalStatus.Equals(site.Status);
					}
					else if (@object is Organization)
					{
						oldStatus = organization.Status;
						update = !approvalStatus.Equals(organization.Status);
					}
					break;

				case ApprovalStatus.Published:
				case ApprovalStatus.Archieved:
					if (bizObject != null)
					{
						oldStatus = bizObject.Status;
						update = !approvalStatus.Equals(bizObject.Status);
						if (!gotRights)
							gotRights = requestInfo.Session.User.IsModerator(@object.WorkingPrivileges, bizObject.ContentType?.WorkingPrivileges, bizObject.Organization as Organization);
					}
					else if (site != null)
					{
						oldStatus = site.Status;
						update = !approvalStatus.Equals(site.Status);
					}
					else if (@object is Organization)
					{
						oldStatus = organization.Status;
						update = !approvalStatus.Equals(organization.Status);
					}
					break;
			}

			if (!gotRights)
				throw new AccessDeniedException();

			var json = @object.ToJson();
			if (!update)
				return json;

			// do the approval process
			if (@object is Organization)
			{
				organization.Status = approvalStatus;
				organization.LastModified = DateTime.Now;
				organization.LastModifiedID = requestInfo.Session.User.ID;
				json = await organization.UpdateAsync(requestInfo, oldStatus, cancellationToken).ConfigureAwait(false);
			}

			else if (site != null)
			{
				site.Status = approvalStatus;
				site.LastModified = DateTime.Now;
				site.LastModifiedID = requestInfo.Session.User.ID;
				json = await site.UpdateAsync(requestInfo, oldStatus, cancellationToken).ConfigureAwait(false);
			}

			else if (@object is Content content)
			{
				content.Status = approvalStatus;
				content.LastModified = DateTime.Now;
				content.LastModifiedID = requestInfo.Session.User.ID;
				json = await content.UpdateAsync(requestInfo, oldStatus, cancellationToken).ConfigureAwait(false);
			}

			else if (@object is Item item)
			{
				item.Status = approvalStatus;
				item.LastModified = DateTime.Now;
				item.LastModifiedID = requestInfo.Session.User.ID;
				json = await item.UpdateAsync(requestInfo, oldStatus, cancellationToken).ConfigureAwait(false);
			}

			else if (@object is Link link)
			{
				link.Status = approvalStatus;
				link.LastModified = DateTime.Now;
				link.LastModifiedID = requestInfo.Session.User.ID;
				json = await link.UpdateAsync(requestInfo, oldStatus, null, cancellationToken).ConfigureAwait(false);
			}

			else if (@object is Form form)
			{
				form.Status = approvalStatus;
				form.LastModified = DateTime.Now;
				form.LastModifiedID = requestInfo.Session.User.ID;
				json = await form.UpdateAsync(requestInfo, oldStatus, cancellationToken).ConfigureAwait(false);
			}

			return json;
		}
		#endregion

		#region Move (update management information)
		async Task<JToken> MoveAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			// prepare
			if (!await this.IsSystemAdministratorAsync(requestInfo, cancellationToken).ConfigureAwait(false))
				throw new AccessDeniedException();

			if (!requestInfo.Verb.IsEquals("GET"))
				throw new InvalidRequestException($"The request is invalid [({requestInfo.Verb}): {requestInfo.GetURI()}]");

			var objectName = requestInfo.GetObjectIdentity();
			var objectID = requestInfo.GetObjectIdentity(true, true);
			var @object = "ContentType".IsEquals(objectName)
				? await (objectID ?? "").GetContentTypeByIDAsync(cancellationToken).ConfigureAwait(false)
				: "Category".IsEquals(objectName)
					? await (objectID ?? "").GetCategoryByIDAsync(cancellationToken).ConfigureAwait(false) as IPortalObject
					: null;
			if (@object == null)
				throw new InvalidRequestException($"The request is invalid [({requestInfo.Verb}): {requestInfo.GetURI()}]");

			// move content-type and/or all belong items/links to oher module
			if (@object is ContentType contentType)
			{
				var module = await (requestInfo.GetParameter("x-module-id") ?? "").GetModuleByIDAsync(cancellationToken).ConfigureAwait(false);
				if (module == null)
					throw new InvalidRequestException($"The request is invalid [({requestInfo.Verb}): {requestInfo.GetURI()}]");

				objectName = contentType.ContentTypeDefinition.GetObjectName();
				if ("CMS.Item".IsEquals(objectName))
				{
					// get all objects
					var objects = await Item.FindAsync(Filters<Item>.Equals("RepositoryEntityID", contentType.ID), null, 0, 1, null, cancellationToken).ConfigureAwait(false);

					// update objects
					await objects.ForEachAsync(async item =>
					{
						item.RepositoryID = module.ID;
						item.LastModified = DateTime.Now;
						item.LastModifiedID = requestInfo.Session.User.ID;
						await Item.UpdateAsync(item, false, cancellationToken).ConfigureAwait(false);
						new UpdateMessage
						{
							Type = $"{requestInfo.ServiceName}#{objectName}#Update",
							Data = item.ToJson(),
							DeviceID = "*"
						}.Send();
					}, true, false).ConfigureAwait(false);

					// update content-type
					contentType.RepositoryID = module.ID;
					await contentType.UpdateAsync(requestInfo, cancellationToken).ConfigureAwait(false);
				}
				else if ("CMS.Link".IsEquals(objectName))
				{
					// get all objects
					var objects = await Link.FindAsync(Filters<Link>.Equals("RepositoryEntityID", contentType.ID), null, 0, 1, null, cancellationToken).ConfigureAwait(false);

					// update objects
					await objects.ForEachAsync(async link =>
					{
						link.RepositoryID = module.ID;
						link.LastModified = DateTime.Now;
						link.LastModifiedID = requestInfo.Session.User.ID;
						await Link.UpdateAsync(link, false, cancellationToken).ConfigureAwait(false);
						new UpdateMessage
						{
							Type = $"{requestInfo.ServiceName}#{objectName}#Update",
							Data = link.ToJson(),
							DeviceID = "*"
						}.Send();
					}, true, false).ConfigureAwait(false);

					// update content-type
					contentType.RepositoryID = module.ID;
					await contentType.UpdateAsync(requestInfo, cancellationToken).ConfigureAwait(false);
				}
			}

			// move all belong contents to other category
			else if (@object is Category category)
			{
				var cntType = await (requestInfo.GetParameter("x-content-type-id") ?? "").GetContentTypeByIDAsync(cancellationToken).ConfigureAwait(false);
				var destination = await (requestInfo.GetParameter("x-category-id") ?? "").GetCategoryByIDAsync(cancellationToken).ConfigureAwait(false);
				if (cntType == null || destination == null || category.ID.Equals(destination.ID) || !category.RepositoryEntityID.Equals(destination.RepositoryEntityID) || !category.RepositoryID.Equals(destination.RepositoryID))
					throw new InvalidRequestException($"The request is invalid [({requestInfo.Verb}): {requestInfo.GetURI()}]");

				// get all objects
				var objects = await Content.FindAsync(Filters<Content>.Equals("CategoryID", category.ID), null, 0, 1, null, cancellationToken).ConfigureAwait(false);

				// update objects
				await objects.ForEachAsync(async content =>
				{
					content.CategoryID = destination.ID;
					content.LastModified = DateTime.Now;
					content.LastModifiedID = requestInfo.Session.User.ID;
					await Content.UpdateAsync(content, false, cancellationToken).ConfigureAwait(false);
					new UpdateMessage
					{
						Type = $"{requestInfo.ServiceName}#{objectName}#Update",
						Data = content.ToJson(),
						DeviceID = "*"
					}.Send();
				}, true, false).ConfigureAwait(false);

				// clear related cache
				await cntType.ClearRelatedCacheAsync(cancellationToken, requestInfo.CorrelationID, true, false).ConfigureAwait(false);
			}

			return new JObject();
		}
		#endregion

		#region Generate feeds
		/// <summary>
		/// Generate feeds
		/// </summary>
		/// <param name="requestInfo"></param>
		/// <param name="cancellationToken"></param>
		/// <returns></returns>
		async Task<JToken> GenerateFeedAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			var asJson = "true".IsEquals(requestInfo.GetParameter("x-feed-json")) || "json".IsEquals(requestInfo.GetQueryParameter("alt"));
			try
			{
				// prepare required information
				var identity = requestInfo.GetParameter("x-system");
				if (string.IsNullOrWhiteSpace(identity))
					throw new InvalidRequestException($"The request is invalid [({requestInfo.Verb}): {requestInfo.GetURI()}]");
				var organization = await (identity.IsValidUUID() ? identity.GetOrganizationByIDAsync(cancellationToken) : identity.GetOrganizationByAliasAsync(cancellationToken)).ConfigureAwait(false);
				if (organization == null)
					throw new InvalidRequestException($"The request is invalid [({requestInfo.Verb}): {requestInfo.GetURI()}]");

				// get site by domain
				var url = requestInfo.GetParameter("x-url") ?? requestInfo.GetParameter("x-uri");
				var host = requestInfo.GetParameter("x-host");
				var site = await (host ?? "").GetSiteByDomainAsync(cancellationToken).ConfigureAwait(false) ?? organization.DefaultSite ?? Utility.DefaultSite;
				if (site == null)
					throw new SiteNotRecognizedException($"The requested site is not recognized ({host ?? "unknown"}){(this.IsDebugLogEnabled ? $" because the organization ({organization.Title}) has no site [{organization.Sites?.Count}]" : "")}");

				// do redirect
				if (site.AlwaysUseHTTPs || site.RedirectToNoneWWW)
				{
					var requestURI = new Uri(url);
					var redirectHost = requestInfo.GetHeaderParameter("x-srp-host") ?? requestURI.Host;
					var redirectURL = (site.AlwaysUseHTTPs ? "https" : requestURI.Scheme) + "://" + (site.RedirectToNoneWWW ? redirectHost.Replace("www.", "") : redirectHost) + $"{requestURI.PathAndQuery}{requestURI.Fragment}";
					if (!string.IsNullOrWhiteSpace(redirectURL) && !redirectURL.Equals(requestURI.AbsoluteUri))
						return new JObject
						{
							{ "StatusCode", site.AlwaysUseHTTPs && !requestURI.Scheme.IsEquals("https") ? (int)HttpStatusCode.Redirect : (int)HttpStatusCode.MovedPermanently },
							{ "Headers", new JObject { ["Location"] = redirectURL.NormalizeURLs(requestURI, organization.Alias, false, true, null, null, requestInfo.GetHeaderParameter("x-srp-host")) } }
						};
				}

				var contentTypes = organization.Modules.Select(module => module.ContentTypes.Where(cntType => cntType.ContentTypeDefinitionID == "B0000000000000000000000000000002")).SelectMany(cntType => cntType).ToList();
				Category category = null;
				var categoryAlias = requestInfo.GetParameter("x-feed-category")?.NormalizeAlias();
				if (!string.IsNullOrWhiteSpace(categoryAlias))
					for (var index = 0; index < contentTypes.Count; index++)
					{
						var categoryContentTypes = organization.Modules.Select(module => module.ContentTypes.Where(cntType => cntType.ContentTypeDefinitionID == "B0000000000000000000000000000001")).SelectMany(cntType => cntType).ToList();
						var contentType = categoryContentTypes.FirstOrDefault(cntType => cntType.RepositoryID == contentTypes[index].RepositoryID);
						category = string.IsNullOrWhiteSpace(contentType?.ID) ? null : await contentType.ID.GetCategoryByAliasAsync(categoryAlias, cancellationToken).ConfigureAwait(false);
						if (category != null)
						{
							contentTypes = new List<ContentType> { contentTypes[index] };
							break;
						}
					}

				// search for contents
				var contents = new List<Content>();
				var thumbnails = new JObject();
				await contentTypes.ForEachAsync(async contentType =>
				{
					var filter = Filters<Content>.And
					(
						Filters<Content>.Equals("SystemID", organization.ID),
						Filters<Content>.Equals("RepositoryID", contentType.ModuleID),
						Filters<Content>.Equals("RepositoryEntityID", contentType.ID),
						Filters<Content>.LessThanOrEquals("StartDate", "@today"),
						Filters<Content>.Or(Filters<Content>.IsNull("EndDate"), Filters<Content>.GreaterOrEquals("EndDate", "@today")),
						Filters<Content>.Equals("Status", ApprovalStatus.Published.ToString())
					);
					if (category != null)
						filter.Add(Filters<Content>.Equals("CategoryID", category.ID));
					var sort = Sorts<Content>.Descending("StartDate").ThenByDescending("PublishedTime");
					var results = await requestInfo.SearchAsync(null, filter, sort, 20, 1, contentType.ID, -1, cancellationToken, true, false, 0, 0, 60).ConfigureAwait(false);
					results.Item1.Where(@object => contents.Find(obj => obj.ID == @object.ID) == null).ForEach(@object => contents.Add(@object));
					(results.Item4 as JObject)?.ForEach(kvp => thumbnails[kvp.Key] = kvp.Value);
				}, true, false).ConfigureAwait(false);
				contents = contents.OrderByDescending(content => content.StartDate).ThenByDescending(content => content.PublishedTime).Take(20).ToList();

				// generate feed
				var useAlias = (url ?? "").IsContains($"/~{organization.Alias}/");
				if (!useAlias)
					host = requestInfo.GetHeaderParameter("x-srp-host") ?? host ?? site.Host;
				var href = site.GetURL(host, url);
				var baseHref = useAlias ? $"/~{organization.Alias}/" : "/";
				var lastModified = contents.Any()
					? contents.OrderByDescending(content => content.LastModified).First().LastModified
					: site.LastModified;

				var feed = new XElement
				(
					"feed",
					new XElement("id", $"tag:{host},{lastModified:yyyy-MM-dd}:site/{site.ID}{(category != null ? $"/category/{category.ID}" : "")}"),
					new XElement("updated", lastModified.ToIsoString()),
					new XElement("title", (category != null ? $"{category.Title} :: " : "") + site.Title),
					new XElement("link", new XAttribute("rel", "alternate"), new XAttribute("type", "text/html"), new XAttribute("href", $"{href}{category?.GetURL().Replace("~/", baseHref) ?? baseHref}"))
				);

				await contents.ForEachAsync(async content =>
				{
					var entry = new XElement
					(
						"entry",
						new XElement("id", $"tag:{host},{content.LastModified:yyyy-MM-dd}:content/{content.ID}"),
						new XElement("published", content.PublishedTime.Value.ToIsoString()),
						new XElement("updated", content.LastModified.ToIsoString()),
						new XElement("title", content.Title),
						new XElement("subtitle", content.SubTitle ?? ""),
						new XElement("summary", $"{content.Summary?.RemoveTags().Replace("\t", "").Replace("\r", "").Replace("\n", " ") ?? ""}", new XAttribute("type", "text")),
						new XElement("author", new XElement("name", content.Author ?? "N/A")),
						new XElement("link", new XAttribute("rel", "alternate"), new XAttribute("type", "text/html"), new XAttribute("href", $"{href}{content.GetURL().Replace("~/", baseHref)}"))
					);
					feed.Add(entry);

					var media = string.IsNullOrWhiteSpace(organization.FakeFilesHttpURI)
						? thumbnails.GetThumbnailURL(content.ID)
						: thumbnails.GetThumbnailURL(content.ID)?.Replace(Utility.FilesHttpURI, organization.FakeFilesHttpURI);
					if (!string.IsNullOrWhiteSpace(media))
						entry.Add(new XElement("media", new XAttribute("url", media)));

					if (content.Category != null)
						entry.Add(new XElement
						(
							"category",
							new XAttribute("term", content.Category.ID),
							new XAttribute("label", content.Category.Title),
							new XAttribute("scheme", $"{href}{content.Category.GetURL().Replace("~/", baseHref)}")
						));

					var categories = new List<Category>();
					if (content.OtherCategories != null && content.OtherCategories.Any())
						await content.OtherCategories.ForEachAsync(async id => categories.Add(await id.GetCategoryByIDAsync(cancellationToken).ConfigureAwait(false)), true, false).ConfigureAwait(false);
					categories.Where(cat => cat != null).ForEach(cat => entry.Add(new XElement
					(
						"category",
						new XAttribute("label", cat.Title),
						new XAttribute("term", cat.ID),
						new XAttribute("scheme", $"{href}{cat.GetURL().Replace("~/", baseHref)}")
					)));
				}, true, false).ConfigureAwait(false);

				var body = asJson
					? feed.ToJson(json => json.Get<JObject>("feed").Get<JArray>("entry").Where(entry => entry["category"] is JObject).ForEach(entry =>
						{
							var id = entry.Get<string>("id").ToList("/").Last();
							var primaryCategory = contents.FirstOrDefault(obj => obj.ID == id)?.Category;
							if (primaryCategory != null)
								entry["category"] = new[] { new JObject { ["@label"] = primaryCategory.Title, ["@term"] = primaryCategory.ID, ["@scheme"] = $"{href}{primaryCategory.GetURL().Replace("~/", baseHref)}" } }.ToJArray();
						})).Get<JObject>("feed").ToString(Newtonsoft.Json.Formatting.None)
					: null;
				if (body == null)
				{
					body = $"{new XDeclaration("1.0", "utf-8", "yes")}\r\n{feed.CleanInvalidCharacters()}".RemoveWhitespaces();
					body = body.Insert(body.IndexOf("<link"), $"{new XElement("link", new XAttribute("rel", "self"), new XAttribute("type", "application/atom+xml"), new XAttribute("href", url ?? $"{href}{baseHref}feed"))}");
					body = body.Replace("<feed", $"<feed xmlns=\"http://www.w3.org/2005/Atom\" xmlns:media=\"http://search.yahoo.com/mrss/\"");
					body = body.Replace("<media", "<media:thumbnail").Replace("></media>", "/>").Replace("></link>", "/>").Replace("></category>", "/>");
					body = body.Replace("></summary>", "/>").Replace("></subtitle>", "/>").Replace(" />", "/>").Replace("<summary type=\"text\"/>", "").Replace("<subtitle/>", "");
				}

				// response
				return new JObject
				{
					{ "StatusCode", (int)HttpStatusCode.OK },
					{ "Headers", new JObject { [ "Content-Type"] = $"application/{(asJson ? "json" : "atom+xml")}; charset=utf-8", ["X-Correlation-ID"] = requestInfo.CorrelationID } },
					{ "Body", body.Compress(this.BodyEncoding) },
					{ "BodyEncoding", this.BodyEncoding }
				};
			}
			catch (Exception ex)
			{
				await requestInfo.WriteErrorAsync(ex, $"Error occurred while generating a feed => {ex.Message}").ConfigureAwait(false);
				var body = asJson ? new JObject { ["error"] = ex.Message }.ToString() : new XElement("error", ex.Message).ToString();
				return new JObject
				{
					{ "StatusCode", (int)HttpStatusCode.InternalServerError },
					{ "Headers", new JObject { [ "Content-Type"] = $"application/{(asJson ? "json" : "atom+xml")}; charset=utf-8", ["X-Correlation-ID"] = requestInfo.CorrelationID } },
					{ "Body", body.Compress(this.BodyEncoding) },
					{ "BodyEncoding", this.BodyEncoding }
				};
			}
		}
		#endregion

		#region Refine thumbnail images
		async Task RefineThumbnailImagesAsync()
		{
			var correlationID = UtilityService.NewUUID;
			try
			{
				var stopwatch = Stopwatch.StartNew();
				var sort = Sorts<Content>.Ascending("Created");
				var totalRecords = await Content.CountAsync(null, "", this.CancellationToken).ConfigureAwait(false);
				var pageSize = 100;
				var pageNumber = 1;
				var totalPages = new Tuple<long, int>(totalRecords, pageSize).GetTotalPages();

				await this.WriteLogsAsync(correlationID, $"Start to refine thumbnail image of {totalRecords:###,###,###,##0} CMS contents", null, this.ServiceName, "Thumbnails").ConfigureAwait(false);
				while (pageNumber <= totalPages)
				{
					var objects = await Content.FindAsync(null, sort, pageSize, pageNumber, null, this.CancellationToken).ConfigureAwait(false);
					objects.ForEach(@object => new CommunicateMessage("Files")
					{
						Type = "Thumbnail#Refine",
						Data = new JObject
						{
							{ "ServiceName", this.ServiceName },
							{ "ObjectName", "Content" },
							{ "SystemID", @object.SystemID },
							{ "EntityInfo", @object.RepositoryEntityID },
							{ "ObjectID", @object.ID },
							{ "Filename", $"{@object.ID}.jpg" },
							{ "Size", 0 },
							{ "ContentType", "image/jpeg" },
							{ "IsTemporary", false },
							{ "IsShared", false },
							{ "IsTracked", false },
							{ "IsThumbnail", true },
							{ "Title", "" },
							{ "Description", "" },
							{ "LastModified", @object.LastModified },
							{ "LastModifiedID", @object.LastModifiedID }
						}
					}.Send());
					pageNumber++;
				}
				stopwatch.Stop();
				await this.WriteLogsAsync(correlationID, $"Complete to refine thumbnail image of {totalRecords:###,###,###,##0} CMS contents - Execution times: {stopwatch.GetElapsedTimes()}", null, this.ServiceName, "Thumbnails").ConfigureAwait(false);
			}
			catch (Exception ex)
			{
				await this.WriteLogsAsync(correlationID, $"Error occurred while refining thumbnail images => {ex.Message}", ex, this.ServiceName, "Thumbnails").ConfigureAwait(false);
			}
		}
		#endregion

	}
}
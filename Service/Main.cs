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

		public override string ServiceName => "Portals";

		public ModuleDefinition GetDefinition()
			=> new ModuleDefinition(RepositoryMediator.GetEntityDefinition<Organization>().RepositoryDefinition);

		void UpdateDefinition(ModuleDefinition moduleDefinition, string correlationID = null)
		{
			if (moduleDefinition != null && !string.IsNullOrWhiteSpace(moduleDefinition.ID) && !Utility.ModuleDefinitions.ContainsKey(moduleDefinition.ID))
			{
				Utility.ModuleDefinitions[moduleDefinition.ID] = moduleDefinition;
				moduleDefinition.ContentTypeDefinitions.ForEach(contentTypeDefinition =>
				{
					contentTypeDefinition.ModuleDefinition = moduleDefinition;
					Utility.ContentTypeDefinitions[contentTypeDefinition.ID] = contentTypeDefinition;
				});
				if (this.IsDebugLogEnabled)
					this.WriteLogsAsync(correlationID ?? UtilityService.NewUUID, $"Update the module definition into the collection of definitions\r\n{moduleDefinition.ToJson()}", null, this.ServiceName, "CMS.Portals").Run();
			}
		}
		#endregion

		#region Register/Start
		public override Task RegisterServiceAsync(IEnumerable<string> args, Action<IService> onSuccess = null, Action<Exception> onError = null)
			=> base.RegisterServiceAsync(
				args,
				async _ =>
				{
					this.ServiceInstance = await Router.IncomingChannel.RealmProxy.Services.RegisterCallee<ICmsPortalsService>(() => this, RegistrationInterceptor.Create(this.ServiceName)).ConfigureAwait(false);
					this.ServiceCommunicator?.Dispose();
					this.ServiceCommunicator = CmsPortalsServiceExtensions.RegisterServiceCommunicator(
						async message => await this.ProcessCommunicateMessageAsync(message).ConfigureAwait(false),
						exception => this.Logger?.LogError($"Error occurred while fetching an communicate message of CMS Portals => {exception.Message}", this.State == ServiceState.Connected ? exception : null)
					);
					this.Logger?.LogDebug($"Successfully{(this.State == ServiceState.Disconnected ? " re-" : " ")}register the service with CMS Portals");
					onSuccess?.Invoke(this);
				},
				onError
			);

		public override Task UnregisterServiceAsync(IEnumerable<string> args, bool available = true, Action<IService> onSuccess = null, Action<Exception> onError = null)
			=> base.UnregisterServiceAsync(
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

				Utility.APIsHttpURI = this.GetHttpURI("APIs", "https://apis.vieapps.net");
				while (Utility.APIsHttpURI.EndsWith("/"))
					Utility.APIsHttpURI = Utility.APIsHttpURI.Left(Utility.APIsHttpURI.Length - 1);

				Utility.FilesHttpURI = this.GetHttpURI("Files", "https://fs.vieapps.net");
				while (Utility.FilesHttpURI.EndsWith("/"))
					Utility.FilesHttpURI = Utility.FilesHttpURI.Left(Utility.FilesHttpURI.Length - 1);

				Utility.PassportsHttpURI = this.GetHttpURI("Passports", "https://id.vieapps.net");
				while (Utility.PassportsHttpURI.EndsWith("/"))
					Utility.PassportsHttpURI = Utility.PassportsHttpURI.Left(Utility.PassportsHttpURI.Length - 1);

				Utility.PortalsHttpURI = this.GetHttpURI("Portals", "https://portals.vieapps.net");
				while (Utility.PortalsHttpURI.EndsWith("/"))
					Utility.PortalsHttpURI = Utility.PortalsHttpURI.Left(Utility.PortalsHttpURI.Length - 1);

				Utility.CmsPortalsHttpURI = this.GetHttpURI("CMSPortals", "https://cms.vieapps.net");
				while (Utility.CmsPortalsHttpURI.EndsWith("/"))
					Utility.CmsPortalsHttpURI = Utility.CmsPortalsHttpURI.Left(Utility.CmsPortalsHttpURI.Length - 1);

				Utility.RTUService = this.RTUService;
				Utility.MessagingService = this.MessagingService;
				Utility.LoggingService = this.LoggingService;
				Utility.Logger = this.Logger;

				Utility.EncryptionKey = this.EncryptionKey;
				Utility.ValidationKey = this.ValidationKey;
				Utility.NotificationsKey = UtilityService.GetAppSetting("Keys:Notifications");

				Utility.DefaultSite = UtilityService.GetAppSetting("Portals:Default:SiteID", "").GetSiteByID();
				Utility.NotRecognizedAliases.Add($"Site:{new Uri(Utility.PortalsHttpURI).Host}");
				Utility.DataFilesDirectory = UtilityService.GetAppSetting("Path:Portals");
				Utility.TempFilesDirectory = UtilityService.GetAppSetting("Path:Temp");

				this.StartTimer(async () => await this.SendDefinitionInfoAsync(this.CancellationTokenSource.Token).ConfigureAwait(false), 15 * 60);
				this.StartTimer(async () => await this.GetOEmbedProvidersAsync(this.CancellationTokenSource.Token).ConfigureAwait(false), 5 * 60);
				this.StartTimer(async () => await this.PrepareLanguagesAsync(this.CancellationTokenSource.Token).ConfigureAwait(false), 5 * 60);

				this.Logger?.LogDebug($"The default site: {(Utility.DefaultSite != null ? $"{Utility.DefaultSite.Title} [{Utility.DefaultSite.ID}]" : "None")}");
				this.Logger?.LogDebug($"Portals' data files directory: {Utility.DataFilesDirectory ?? "None"}");

				Task.Run(async () =>
				{
					// wait for a few times
					await Task.Delay(UtilityService.GetRandomNumber(678, 789), this.CancellationTokenSource.Token).ConfigureAwait(false);

					// get OEmbed providers
					await this.GetOEmbedProvidersAsync(this.CancellationTokenSource.Token).ConfigureAwait(false);

					// prepare multi-languges
					await this.PrepareLanguagesAsync(this.CancellationTokenSource.Token).ConfigureAwait(false);

					// gathering definitions
					try
					{
						await this.SendInterCommunicateMessageAsync(new CommunicateMessage("CMS.Portals")
						{
							Type = "Definition#RequestInfo"
						}, this.CancellationTokenSource.Token).ConfigureAwait(false);
					}
					catch (Exception ex)
					{
						await this.WriteLogsAsync(UtilityService.NewUUID, $"Error occurred while sending a request for gathering definitions => {ex.Message}", ex, this.ServiceName, "CMS.Portals", LogLevel.Error).ConfigureAwait(false);
					}

					// warm-up the Files HTTP service
					if (!string.IsNullOrWhiteSpace(Utility.FilesHttpURI))
						try
						{
							await UtilityService.GetWebPageAsync(Utility.FilesHttpURI).ConfigureAwait(false);
						}
						catch { }
				}).ConfigureAwait(false);

				// start the refresh timers
				Task.Run(async () =>
				{
					await Task.Delay(UtilityService.GetRandomNumber(2345, 3456), this.CancellationTokenSource.Token).ConfigureAwait(false);
					try
					{
						var organizations = await Organization.FindAsync(null, Sorts<Organization>.Ascending("Title"), 0, 1).ConfigureAwait(false);
						organizations.ForEach(organization => this.StartRefreshTimer(organization.Set()));
					}
					catch { }
				}).ConfigureAwait(false);

				// refine thumbnail images
				if (args?.FirstOrDefault(arg => arg.IsEquals("/refine-thumbnails")) != null)
					this.RefineThumbnailImagesAsync().Run();

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
				? (user != null && user.IsAdministrator(portalObject.WorkingPrivileges, portalObject.Parent?.WorkingPrivileges, await (portalObject.OrganizationID ?? "").GetOrganizationByIDAsync(cancellationToken).ConfigureAwait(false), correlationID)) || await this.IsSystemAdministratorAsync(user, correlationID, cancellationToken).ConfigureAwait(false)
				: await base.IsAdministratorAsync(user, objectName, @object, correlationID, cancellationToken).ConfigureAwait(false);

		protected override bool IsModerator(IUser user, RepositoryBase @object)
			=> @object is IPortalObject portalObject
				? user != null && user.IsModerator(portalObject.WorkingPrivileges, portalObject.Parent?.WorkingPrivileges, (portalObject.OrganizationID ?? "").GetOrganizationByID())
				: base.IsModerator(user, @object);

		protected override async Task<bool> IsModeratorAsync(IUser user, string objectName, RepositoryBase @object, string correlationID = null, CancellationToken cancellationToken = default)
			=> @object is IPortalObject portalObject
				? (user != null && user.IsModerator(portalObject.WorkingPrivileges, portalObject.Parent?.WorkingPrivileges, await (portalObject.OrganizationID ?? "").GetOrganizationByIDAsync(cancellationToken).ConfigureAwait(false), correlationID)) || await this.IsAdministratorAsync(user, objectName, @object, correlationID, cancellationToken).ConfigureAwait(false)
				: await base.IsModeratorAsync(user, objectName, @object, correlationID, cancellationToken).ConfigureAwait(false);

		protected override bool IsEditor(IUser user, RepositoryBase @object)
			=> @object is IPortalObject portalObject
				? user != null && user.IsEditor(portalObject.WorkingPrivileges, portalObject.Parent?.WorkingPrivileges, (portalObject.OrganizationID ?? "").GetOrganizationByID())
				: base.IsEditor(user, @object);

		protected override async Task<bool> IsEditorAsync(IUser user, string objectName, RepositoryBase @object, string correlationID = null, CancellationToken cancellationToken = default)
			=> @object is IPortalObject portalObject
				? (user != null && user.IsEditor(portalObject.WorkingPrivileges, portalObject.Parent?.WorkingPrivileges, await (portalObject.OrganizationID ?? "").GetOrganizationByIDAsync(cancellationToken).ConfigureAwait(false), correlationID)) || await this.IsModeratorAsync(user, objectName, @object, correlationID, cancellationToken).ConfigureAwait(false)
				: await base.IsEditorAsync(user, objectName, @object, correlationID, cancellationToken).ConfigureAwait(false);

		protected override bool IsContributor(IUser user, RepositoryBase @object)
			=> @object is IPortalObject portalObject
				? user != null && user.IsContributor(portalObject.WorkingPrivileges, portalObject.Parent?.WorkingPrivileges, (portalObject.OrganizationID ?? "").GetOrganizationByID())
				: base.IsContributor(user, @object);

		protected override async Task<bool> IsContributorAsync(IUser user, string objectName, RepositoryBase @object, string correlationID = null, CancellationToken cancellationToken = default)
			=> @object is IPortalObject portalObject
				? (user != null && user.IsContributor(portalObject.WorkingPrivileges, portalObject.Parent?.WorkingPrivileges, await (portalObject.OrganizationID ?? "").GetOrganizationByIDAsync(cancellationToken).ConfigureAwait(false), correlationID)) || await this.IsEditorAsync(user, objectName, @object, correlationID, cancellationToken).ConfigureAwait(false)
				: await base.IsContributorAsync(user, objectName, @object, correlationID, cancellationToken).ConfigureAwait(false);

		protected override bool IsViewer(IUser user, RepositoryBase @object)
			=> @object is IPortalObject portalObject
				? user != null && user.IsViewer(portalObject.WorkingPrivileges, portalObject.Parent?.WorkingPrivileges, (portalObject.OrganizationID ?? "").GetOrganizationByID())
				: base.IsViewer(user, @object);

		protected override async Task<bool> IsViewerAsync(IUser user, string objectName, RepositoryBase @object, string correlationID = null, CancellationToken cancellationToken = default)
			=> @object is IPortalObject portalObject
				? (user != null && user.IsViewer(portalObject.WorkingPrivileges, portalObject.Parent?.WorkingPrivileges, await (portalObject.OrganizationID ?? "").GetOrganizationByIDAsync(cancellationToken).ConfigureAwait(false), correlationID)) || await this.IsContributorAsync(user, objectName, @object, correlationID, cancellationToken).ConfigureAwait(false)
				: await base.IsViewerAsync(user, objectName, @object, correlationID, cancellationToken).ConfigureAwait(false);

		protected override bool IsDownloader(IUser user, RepositoryBase @object)
			=> @object is IPortalObject portalObject
				? user != null && user.IsDownloader(portalObject.WorkingPrivileges, portalObject.Parent?.WorkingPrivileges, (portalObject.OrganizationID ?? "").GetOrganizationByID())
				: base.IsDownloader(user, @object);

		protected override async Task<bool> IsDownloaderAsync(IUser user, string objectName, RepositoryBase @object, string correlationID = null, CancellationToken cancellationToken = default)
			=> @object is IPortalObject portalObject
				? (user != null && user.IsDownloader(portalObject.WorkingPrivileges, portalObject.Parent?.WorkingPrivileges, await (portalObject.OrganizationID ?? "").GetOrganizationByIDAsync(cancellationToken).ConfigureAwait(false), correlationID)) || await this.IsViewerAsync(user, objectName, @object, correlationID, cancellationToken).ConfigureAwait(false)
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
			this.WriteLogs(requestInfo, $"Begin request ({requestInfo.Verb} {requestInfo.GetURI()})");
			try
			{
				string mode;
				JToken json = null;
				Organization organization;
				switch (requestInfo.ObjectName.ToLower())
				{

					#region process the request of Portals objects
					case "organization":
					case "core.organization":
						json = await this.ProcessOrganizationAsync(requestInfo, cancellationToken).ConfigureAwait(false);
						break;

					case "role":
					case "core.role":
						json = await this.ProcessRoleAsync(requestInfo, cancellationToken).ConfigureAwait(false);
						break;

					case "site":
					case "core.site":
						json = await this.ProcessSiteAsync(requestInfo, cancellationToken).ConfigureAwait(false);
						break;

					case "desktop":
					case "core.desktop":
						json = await this.ProcessDesktopAsync(requestInfo, cancellationToken).ConfigureAwait(false);
						break;

					case "portlet":
					case "core.portlet":
						json = await this.ProcessPortletAsync(requestInfo, cancellationToken).ConfigureAwait(false);
						break;

					case "module":
					case "core.module":
						json = await this.ProcessModuleAsync(requestInfo, cancellationToken).ConfigureAwait(false);
						break;

					case "contenttype":
					case "content.type":
					case "content-type":
					case "core.contenttype":
					case "core.content.type":
						json = await this.ProcessContentTypeAsync(requestInfo, cancellationToken).ConfigureAwait(false);
						break;

					case "expression":
					case "core.expression":
						json = await this.ProcessExpressionAsync(requestInfo, cancellationToken).ConfigureAwait(false);
						break;
					#endregion

					#region process the request of CMS objects
					case "category":
					case "cms.category":
						json = await this.ProcessCategoryAsync(requestInfo, cancellationToken).ConfigureAwait(false);
						break;

					case "content":
					case "cms.content":
						json = await this.ProcessContentAsync(requestInfo, cancellationToken).ConfigureAwait(false);
						break;

					case "item":
					case "cms.item":
						json = await this.ProcessItemAsync(requestInfo, cancellationToken).ConfigureAwait(false);
						break;

					case "link":
					case "cms.link":
						json = await this.ProcessLinkAsync(requestInfo, cancellationToken).ConfigureAwait(false);
						break;
					#endregion

					#region process request of Portals HTTP service
					case "identify.system":
						json = await this.IdentifySystemAsync(requestInfo, cancellationToken).ConfigureAwait(false);
						break;

					case "process.http.request":
						json = await this.ProcessHttpRequestAsync(requestInfo, cancellationToken).ConfigureAwait(false);
						break;
					#endregion

					#region process the request of definitions, instructions, files, profiles and all known others
					case "definitions":
						mode = requestInfo.GetQueryParameter("mode");
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
								json = await this.GetThemesAsync(cancellationToken).ConfigureAwait(false);
								break;

							case "template":
								json = await this.ProcessTemplateAsync(requestInfo, cancellationToken).ConfigureAwait(false);
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

							case "category":
							case "cms.category":
								json = this.GenerateFormControls<Category>(requestInfo.GetParameter("x-content-type-id"));
								break;

							case "content":
							case "cms.content":
								json = this.GenerateFormControls<Content>(requestInfo.GetParameter("x-content-type-id"));
								break;

							case "item":
							case "cms.item":
								json = this.GenerateFormControls<Item>(requestInfo.GetParameter("x-content-type-id"));
								break;

							case "link":
							case "cms.link":
								json = this.GenerateFormControls<Link>(requestInfo.GetParameter("x-content-type-id"));
								break;

							case "contact":
							case "utils.contact":
								json = this.GenerateFormControls<Contact>(requestInfo.GetParameter("x-content-type-id"));
								break;

							default:
								throw new InvalidRequestException($"The request is invalid [({requestInfo.Verb}): {requestInfo.GetURI()}]");
						}
						break;

					case "instructions":
						mode = requestInfo.Extra != null && requestInfo.Extra.ContainsKey("mode") ? requestInfo.Extra["mode"].GetCapitalizedFirstLetter() : null;
						organization = mode != null ? await (requestInfo.GetParameter("x-system-id") ?? requestInfo.GetParameter("active-id") ?? "").GetOrganizationByIDAsync(cancellationToken).ConfigureAwait(false) : null;
						json = new JObject
						{
							{ "Message", organization != null && organization.Instructions != null && organization.Instructions.ContainsKey(mode) ? organization.Instructions[mode]?.ToJson() : null },
							{ "Email", organization?.EmailSettings?.ToJson() },
						};
						break;

					case "files":
					case "attachments":
						json = await this.ProcessAttachmentFileAsync(requestInfo, cancellationToken).ConfigureAwait(false);
						break;

					case "profile":
						break;

					case "excel":
						json = await this.DoExcelActionAsync(requestInfo, cancellationToken).ConfigureAwait(false);
						break;

					case "cache":
					case "caches":
						json = await this.ClearCacheAsync(requestInfo, cancellationToken).ConfigureAwait(false);
						break;

					case "approve":
					case "approval":
						json = await this.ApproveAsync(requestInfo, cancellationToken).ConfigureAwait(false);
						break;

					case "move":
						json = await this.MoveAsync(requestInfo, cancellationToken).ConfigureAwait(false);
						break;

					default:
						throw new InvalidRequestException($"The request is invalid [({requestInfo.Verb}): {requestInfo.GetURI()}]");
						#endregion

				}
				stopwatch.Stop();
				this.WriteLogs(requestInfo, $"Success response - Execution times: {stopwatch.GetElapsedTimes()}");
				if (this.IsDebugResultsEnabled)
					this.WriteLogs(requestInfo, $"- Request: {requestInfo.ToString(this.JsonFormat)}" + "\r\n" + $"- Response: {json?.ToString(this.JsonFormat)}");
				return json;
			}
			catch (Exception ex)
			{
				throw this.GetRuntimeException(requestInfo, ex, stopwatch);
			}
		}

		#region Generate form controls
		JToken GenerateFormControls<T>(string contentTypeID) where T : class
		{
			// get content type
			var contentType = (contentTypeID ?? "").GetContentTypeByID();
			if (contentType == null || contentType.ExtendedPropertyDefinitions == null || contentType.ExtendedPropertyDefinitions.Count < 1)
				return this.GenerateFormControls<T>();

			// generate standard controls
			var controls = (this.GenerateFormControls<T>() as JArray).Select(control => control as JObject).ToList();

			// generate extended controls
			contentType.ExtendedControlDefinitions.ForEach(definition =>
			{
				var control = this.GenerateFormControl(definition, contentType.ExtendedPropertyDefinitions.Find(def => def.Name.IsEquals(definition.Name)).Mode);
				var index = !string.IsNullOrWhiteSpace(definition.PlaceBefore) ? controls.FindIndex(ctrl => definition.PlaceBefore.IsEquals(ctrl.Get<string>("Name"))) : -1;
				if (index > -1)
				{
					control["Segment"] = controls[index].Get<string>("Segment");
					controls.Insert(index, control);
				}
				else
					controls.Add(control);
			});

			// update standard controls
			// ...

			// update order-index and return the controls
			controls.ForEach((control, order) => control["Order"] = order);
			return controls.ToJArray();
		}

		JObject GenerateFormControl(ExtendedControlDefinition definition, ExtendedPropertyMode mode)
		{
			var controlType = mode.Equals(ExtendedPropertyMode.LargeText) || (definition.AsTextEditor != null && definition.AsTextEditor.Value)
				? mode.Equals(ExtendedPropertyMode.LargeText) && definition.AsTextEditor != null && !definition.AsTextEditor.Value ? "TextArea" : "TextEditor"
				: mode.Equals(ExtendedPropertyMode.Select)
					? "Select"
					: mode.Equals(ExtendedPropertyMode.Lookup)
						? "Lookup"
						: mode.Equals(ExtendedPropertyMode.DateTime)
							? "DatePicker"
							: mode.Equals(ExtendedPropertyMode.YesNo)
								? "YesNo"
								: mode.Equals(ExtendedPropertyMode.MediumText) ? "TextArea" : "TextBox";

			var hidden = definition.Hidden != null && definition.Hidden.Value;
			var options = new JObject();
			if (!hidden)
			{
				options["Label"] = definition.Label;
				options["PlaceHolder"] = definition.PlaceHolder;
				options["Description"] = definition.Description;

				var dataType = !string.IsNullOrWhiteSpace(definition.DataType)
					? definition.DataType
					: "Lookup".IsEquals(controlType) && !string.IsNullOrWhiteSpace(definition.LookupType)
						? definition.LookupType
						: "DatePicker".IsEquals(controlType)
							? "date"
							: mode.Equals(ExtendedPropertyMode.IntegralNumber) || mode.Equals(ExtendedPropertyMode.FloatingPointNumber)
								? "number"
								: null;

				if (!string.IsNullOrWhiteSpace(dataType))
					options["Type"] = dataType;

				if (definition.Disabled != null && definition.Disabled.Value)
					options["Disabled"] = true;

				if (definition.ReadOnly != null && definition.ReadOnly.Value)
					options["ReadOnly"] = true;

				if (definition.AutoFocus != null && definition.AutoFocus.Value)
					options["AutoFocus"] = true;

				if (!string.IsNullOrWhiteSpace(definition.ValidatePattern))
					options["ValidatePattern"] = definition.ValidatePattern;

				if (!string.IsNullOrWhiteSpace(definition.Width))
					options["Width"] = definition.Width;

				if (!string.IsNullOrWhiteSpace(definition.Height))
					options["Height"] = definition.Height;

				if (definition.Rows != null && definition.Rows.Value > 0)
					options["Rows"] = definition.Rows.Value;

				if (!string.IsNullOrWhiteSpace(definition.MinValue))
					try
					{
						if (mode.Equals(ExtendedPropertyMode.IntegralNumber))
							options["MinValue"] = definition.MinValue.CastAs<long>();
						else if (mode.Equals(ExtendedPropertyMode.FloatingPointNumber))
							options["MinValue"] = definition.MinValue.CastAs<decimal>();
						else
							options["MinValue"] = definition.MinValue;
					}
					catch { }

				if (!string.IsNullOrWhiteSpace(definition.MaxValue))
					try
					{
						if (mode.Equals(ExtendedPropertyMode.IntegralNumber))
							options["MaxValue"] = definition.MaxValue.CastAs<long>();
						else if (mode.Equals(ExtendedPropertyMode.FloatingPointNumber))
							options["MaxValue"] = definition.MaxValue.CastAs<decimal>();
						else
							options["MaxValue"] = definition.MaxValue;
					}
					catch { }

				if (definition.MinLength != null && definition.MinLength.Value > 0)
					options["MinLength"] = definition.MinLength.Value;

				if (definition.MaxLength != null && definition.MaxLength.Value > 0)
					options["MaxLength"] = definition.MaxLength.Value;

				if ("DatePicker".IsEquals(controlType))
					options["DatePickerOptions"] = new JObject
					{
						{ "AllowTimes", definition.DatePickerWithTimes != null && definition.DatePickerWithTimes.Value }
					};

				if ("Select".IsEquals(controlType))
					options["SelectOptions"] = new JObject
					{
						{ "Values", definition.SelectValues },
						{ "Multiple", definition.Multiple != null && definition.Multiple.Value },
						{ "AsBoxes", definition.SelectAsBoxes != null && definition.SelectAsBoxes.Value },
						{ "Interface", definition.SelectInterface ?? "alert" }
					};

				if ("Lookup".IsEquals(controlType))
				{
					var contentType = string.IsNullOrWhiteSpace(definition.LookupRepositoryEntityID) ? null : definition.LookupRepositoryEntityID.GetContentTypeByID();
					options["LookupOptions"] = new JObject
					{
						{ "Multiple", definition.Multiple != null && definition.Multiple.Value },
						{ "AsModal", !"Address".IsEquals(definition.LookupType) },
						{ "AsCompleter", "Address".IsEquals(definition.LookupType) },
						{ "ModalOptions", new JObject
							{
								{ "Component", null },
								{ "ComponentProps", new JObject
									{
										{ "organizationID", contentType?.OrganizationID },
										{ "moduleID", contentType?.ModuleID },
										{ "contentTypeID", contentType?.ID },
										{ "objectName", contentType?.ContentTypeDefinition.GetObjectName() },
										{ "nested", contentType?.ContentTypeDefinition.NestedObject },
										{ "multiple", definition.Multiple != null && definition.Multiple.Value }
									}
								}
							}
						}
					};
				}
			}

			return new JObject
			{
				{ "Name", definition.Name },
				{ "Type", controlType },
				{ "Hidden", hidden },
				{ "Required", definition.Required != null && definition.Required.Value },
				{ "Extras", new JObject() },
				{ "Options", options }
			};
		}
		#endregion

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
				await Directory.GetDirectories(Path.Combine(Utility.DataFilesDirectory, "themes")).ForEachAsync(async (directory, _) =>
				{
					var name = Path.GetFileName(directory).ToLower();
					var packageInfo = new JObject
					{
						{ "name", name },
						{ "description", name.IsEquals("default") ? "The theme with default styles and coloring codes" : "" },
						{ "author", "System" }
					};
					var filename = Path.Combine(directory, "package.json");
					if (File.Exists(filename))
						try
						{
							packageInfo = JObject.Parse(await UtilityService.ReadTextFileAsync(filename, null, cancellationToken).ConfigureAwait(false));
						}
						catch { }
					themes.Add(packageInfo);
				}, cancellationToken, true, false).ConfigureAwait(false);
			return themes;
		}

		async Task PrepareLanguagesAsync(CancellationToken cancellationToken = default)
		{
			var correlationID = UtilityService.NewUUID;
			Utility.Languages.Clear();
			await UtilityService.GetAppSetting("Portals:Languages", "vi-VN|en-US").ToList("|", true).ForEachAsync(async (language, _) => await new[] { "common", "notifications", "portals", "portals.cms", "users" }.ForEachAsync(async (module, __) =>
			{
				if (!Utility.Languages.TryGetValue(language, out var languages))
				{
					languages = new ExpandoObject();
					Utility.Languages[language] = languages;
				}
				try
				{
					languages.Merge(JObject.Parse(await UtilityService.FetchWebResourceAsync($"{Utility.APIsHttpURI}/statics/i18n/{module}/{language}.json", cancellationToken).ConfigureAwait(false)).ToExpandoObject());
				}
				catch (Exception ex)
				{
					await this.WriteLogsAsync(correlationID, $"Error occurred while gathering i18n language resources => {ex.Message}", ex, this.ServiceName, "CMS.Portals", LogLevel.Error).ConfigureAwait(false);
				}
			}, cancellationToken, true, false).ConfigureAwait(false), cancellationToken, true, false).ConfigureAwait(false);
			if (this.IsDebugResultsEnabled)
				await this.WriteLogsAsync(correlationID, $"Gathering i18n language resources successful => {Utility.Languages.Select(kvp => kvp.Key).Join(" - ")}", null, this.ServiceName, "CMS.Portals", LogLevel.Debug).ConfigureAwait(false);
		}

		async Task GetOEmbedProvidersAsync(CancellationToken cancellationToken = default)
		{
			var correlationID = UtilityService.NewUUID;
			try
			{
				var providers = JArray.Parse(await UtilityService.FetchWebResourceAsync($"{Utility.APIsHttpURI}/statics/oembed.providers.json", cancellationToken).ConfigureAwait(false));
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
		#endregion

		#region Process CMS Portals object
		async Task<JObject> ProcessCategoryAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
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

		async Task<JObject> ProcessContentAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
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

		async Task<JObject> ProcessItemAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
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

		async Task<JObject> ProcessLinkAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
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
		#endregion

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

		async Task<JToken> ProcessTemplateAsync(RequestInfo requestInfo, CancellationToken cancellationToken = default)
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

		async Task<JToken> IdentifySystemAsync(RequestInfo requestInfo, CancellationToken cancellationToken = default)
		{
			string host;
			var organization = await (requestInfo.GetParameter("x-system") ?? "").GetOrganizationByAliasAsync(cancellationToken).ConfigureAwait(false);
			if (organization == null)
				organization = (requestInfo.Header.TryGetValue("x-host", out host) && !string.IsNullOrWhiteSpace(host) ? await host.GetSiteByDomainAsync(null, cancellationToken).ConfigureAwait(false) : Utility.DefaultSite)?.Organization;

			return organization != null
				? new JObject
				{
					{ "ID", organization.ID },
					{ "Alias", organization.Alias }
				}
				: throw new SiteNotRecognizedException($"The requested site is not recognized ({(requestInfo.Header.TryGetValue("x-host", out host) && !string.IsNullOrWhiteSpace(host) ? "unknown" : host)})");
		}

		Task<JToken> ProcessHttpRequestAsync(RequestInfo requestInfo, CancellationToken cancellationToken = default)
			=> requestInfo.Query.ContainsKey("x-indicator")
				? this.ProcessHttpIndicatorRequestAsync(requestInfo, cancellationToken)
				: requestInfo.Query.ContainsKey("x-resource")
					? this.ProcessHttpResourceRequestAsync(requestInfo, cancellationToken)
					: this.ProcessHttpDesktopRequestAsync(requestInfo, cancellationToken);

		#region Process resource  requests of Portals HTTP service
		bool WriteDesktopLogs => this.Logger.IsEnabled(LogLevel.Trace) || "true".IsEquals(UtilityService.GetAppSetting("Logs:Portals:Desktops", "false"));

		HashSet<string> ExcludedThemes => UtilityService.GetAppSetting("Portals:Desktops:Resources:ExcludedThemes", "").Trim().ToLower().ToHashSet();

		HashSet<string> DontMinifyThemes => UtilityService.GetAppSetting("Portals:Desktops:Resources:DontMinifyThemes", "").Trim().ToLower().ToHashSet();

		bool CacheDesktopResources => "true".IsEquals(UtilityService.GetAppSetting("Portals:Desktops:Resources:Cache", "true"));

		bool AllowSrcResourceFiles => "true".IsEquals(UtilityService.GetAppSetting("Portals:Desktops:Resources:AllowSrcFiles", "true"));

		bool CacheDesktopHtmls => "true".IsEquals(UtilityService.GetAppSetting("Portals:Desktops:Htmls:Cache", "true"));

		bool RemoveDesktopHtmlWhitespaces => "true".IsEquals(UtilityService.GetAppSetting("Portals:Desktops:Htmls:RemoveWhitespaces", "true"));

#if NETSTANDARD2_0
		string BodyEncoding => UtilityService.GetAppSetting("Portals:Desktops:Body:Encoding", "gzip");
#else
		string BodyEncoding => UtilityService.GetAppSetting("Portals:Desktops:Body:Encoding", "br");
#endif

		async Task<JToken> ProcessHttpIndicatorRequestAsync(RequestInfo requestInfo, CancellationToken cancellationToken = default)
		{
			this.WriteLogsAsync(requestInfo.CorrelationID, $"Process HTTP indicator => {requestInfo.GetHeaderParameter("x-url")}", null, this.ServiceName, "Process.Http.Request").Run();

			var organization = await (requestInfo.GetParameter("x-system") ?? "").GetOrganizationByAliasAsync(cancellationToken).ConfigureAwait(false);
			if (organization == null)
				throw new InformationNotFoundException();

			var name = $"{requestInfo.Query["x-indicator"]}.txt";
			var indicator = organization.HttpIndicators?.FirstOrDefault(httpIndicator => httpIndicator.Name.IsEquals(name));
			return indicator != null
				? new JObject
				{
					{ "StatusCode", (int)HttpStatusCode.OK },
					{ "Headers", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
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

		async Task<JToken> ProcessHttpResourceRequestAsync(RequestInfo requestInfo, CancellationToken cancellationToken = default)
		{
			this.WriteLogsAsync(requestInfo.CorrelationID, $"Process HTTP resource => {requestInfo.GetHeaderParameter("x-url")}", null, this.ServiceName, "Process.Http.Request").Run();

			// get the type of the resource
			var type = requestInfo.Query["x-resource"];

			// special headers
			var noneMatch = requestInfo.GetHeaderParameter("If-None-Match");
			var modifiedSince = requestInfo.GetHeaderParameter("If-Modified-Since") ?? requestInfo.GetHeaderParameter("If-Unmodified-Since");

			// static files in 'assets' directory or image/font files of a theme
			if (type.IsEquals("assets") || type.IsEquals("images") || type.IsEquals("fonts"))
			{
				// prepare
				if (!requestInfo.Query.TryGetValue("x-path", out var filePath) || string.IsNullOrWhiteSpace(filePath))
					throw new InformationNotFoundException();

				// check special headers to reduce traffict
				var eTag = $"{type.ToLower()}#{filePath.ToLower().GenerateUUID()}";
				var lastModified = this.CacheDesktopResources ? await Utility.Cache.GetAsync<string>($"{eTag}:time", cancellationToken).ConfigureAwait(false) : null;
				if (this.CacheDesktopResources && eTag.IsEquals(noneMatch) && modifiedSince != null && lastModified != null && modifiedSince.FromHttpDateTime() >= lastModified.FromHttpDateTime())
					return new JObject
					{
						{ "StatusCode", (int)HttpStatusCode.NotModified },
						{
							"Headers",
							new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
							{
								{ "X-Correlation-ID", requestInfo.CorrelationID },
								{ "ETag", eTag },
								{ "Last-Modified", lastModified }
							}.ToJson()
						}
					};

				// prepare resource file
				var fileInfo = new FileInfo(Path.Combine(Utility.DataFilesDirectory, type, filePath));
				if (!type.IsEquals("assets"))
				{
					var pathSegments = filePath.ToArray('/');
					filePath = pathSegments.Skip(1).Join(Path.DirectorySeparatorChar.ToString());
					fileInfo = new FileInfo(Path.Combine(Utility.DataFilesDirectory, "themes", pathSegments[0], type, filePath));
				}
				if (!fileInfo.Exists)
					throw new InformationNotFoundException(filePath);


				// response
				lastModified = fileInfo.LastWriteTime.ToHttpString();
				var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
				{
					{ "Content-Type", $"{fileInfo.GetMimeType()}; charset=utf-8" },
					{ "X-Correlation-ID", requestInfo.CorrelationID }
				};

				if (this.CacheDesktopResources)
				{
					Task.WhenAll
					(
						Utility.Cache.SetAsync($"{eTag}:time", lastModified, this.CancellationToken),
						Utility.Cache.AddSetMemberAsync("Statics", $"{eTag}:time", this.CancellationToken)
					).Run();
					headers = new Dictionary<string, string>(headers, StringComparer.OrdinalIgnoreCase)
					{
						{ "ETag", eTag },
						{ "Last-Modified", lastModified },
						{ "Expires", DateTime.Now.AddDays(366).ToHttpString() },
						{ "Cache-Control", "public" }
					};
				}

				return new JObject
				{
					{ "StatusCode", (int)HttpStatusCode.OK },
					{ "Headers", headers.ToJson() },
					{
						"Body",
						(filePath.IsEndsWith(".css")
							? (await UtilityService.ReadTextFileAsync(fileInfo, null, cancellationToken).ConfigureAwait(false)).Replace("~~/", $"{Utility.FilesHttpURI}/").Replace("~#/", $"{Utility.PortalsHttpURI}/").MinifyCss().ToBytes()
							: filePath.IsEndsWith(".js")
								? (await UtilityService.ReadTextFileAsync(fileInfo, null, cancellationToken).ConfigureAwait(false)).Replace("~~/", $"{Utility.FilesHttpURI}/").Replace("~#/", $"{Utility.PortalsHttpURI}/").MinifyJs().ToBytes()
								: await UtilityService.ReadBinaryFileAsync(fileInfo, cancellationToken).ConfigureAwait(false)
						).Compress(this.BodyEncoding).ToBase64()
					},
					{ "BodyEncoding", this.BodyEncoding }
				};
			}

			// css stylesheets
			if (type.IsEquals("css"))
			{
				// verify
				if (!requestInfo.Query.TryGetValue("x-path", out var identity) || string.IsNullOrWhiteSpace(identity))
					throw new InvalidRequestException($"The request is invalid [({requestInfo.Verb}): {requestInfo.GetURI()}]");

				// prepare
				identity = identity.Replace(StringComparison.OrdinalIgnoreCase, ".css", "").ToLower().Trim();
				var eTag = $"css#{identity.GenerateUUID()}";
				var cacheKey = $"css#{identity}";
				var lastModified = this.CacheDesktopResources ? await Utility.Cache.GetAsync<string>($"{cacheKey}:time", cancellationToken).ConfigureAwait(false) : null;
				var headers = new Dictionary<string, string>
				{
					{ "Content-Type", "text/css; charset=utf-8" },
					{ "X-Correlation-ID", requestInfo.CorrelationID }
				};

				// get last-modified and response as HTTP 304 to reduce traffict
				if (this.CacheDesktopResources)
				{
					if (lastModified == null)
					{
						if (identity.Length == 34 && identity.Right(32).IsValidUUID())
						{
							if (identity.Left(1).IsEquals("s"))
							{
								var site = await identity.Right(32).GetSiteByIDAsync(cancellationToken).ConfigureAwait(false);
								if (site != null)
									lastModified = site.LastModified.ToHttpString();
							}
							else if (identity.Left(1).IsEquals("d"))
							{
								var desktop = await identity.Right(32).GetDesktopByIDAsync(cancellationToken).ConfigureAwait(false);
								if (desktop != null)
									lastModified = desktop.LastModified.ToHttpString();
							}
						}
						else
							lastModified = this.GetThemeResourcesLastModified(identity, "css").ToHttpString();
						
						Task.WhenAll
						(
							Utility.Cache.SetAsync($"{cacheKey}:time", lastModified, this.CancellationToken),
							Utility.Cache.AddSetMemberAsync("Statics", $"{cacheKey}:time", this.CancellationToken)
						).Run();
					}

					if (eTag.IsEquals(noneMatch) && modifiedSince != null && lastModified != null && modifiedSince.FromHttpDateTime() >= lastModified.FromHttpDateTime())
						return new JObject
						{
							{ "StatusCode", (int)HttpStatusCode.NotModified },
							{
								"Headers",
								new Dictionary<string, string>(headers, StringComparer.OrdinalIgnoreCase)
								{
									{ "ETag", eTag },
									{ "Last-Modified", lastModified }
								}.ToJson()
							}
						};
				}

				// get resources
				var resources = this.CacheDesktopResources && !this.ExcludedThemes.Contains(identity) && !(identity.Length == 34 && identity.Right(32).IsValidUUID())
					? await Utility.Cache.GetAsync<string>(cacheKey, cancellationToken).ConfigureAwait(false)
					: null;

				if (resources == null)
				{
					if (identity.Length == 34 && identity.Right(32).IsValidUUID())
					{
						if (identity.Left(1).IsEquals("s"))
						{
							var site = await identity.Right(32).GetSiteByIDAsync(cancellationToken).ConfigureAwait(false);
							resources = site != null
								? (this.IsDebugLogEnabled ? $"/* css of the '{site.Title}' site */\r\n" : "") + (string.IsNullOrWhiteSpace(site.Stylesheets) ? "" : site.Stylesheets.Replace("~~/", $"{Utility.FilesHttpURI}/").Replace("~#/", $"{Utility.PortalsHttpURI}/").MinifyCss())
								: $"/* the requested site ({identity}) is not found */";
						}
						else if (identity.Left(1).IsEquals("d"))
						{
							var desktop = await identity.Right(32).GetDesktopByIDAsync(cancellationToken).ConfigureAwait(false);
							resources = desktop != null
								? (this.IsDebugLogEnabled ? $"/* css of the '{desktop.Title}' desktop */\r\n" : "") + (string.IsNullOrWhiteSpace(desktop.Stylesheets) ? "" : desktop.Stylesheets.Replace("~~/", $"{Utility.FilesHttpURI}/").Replace("~#/", $"{Utility.PortalsHttpURI}/").MinifyCss())
								: $"/* the requested desktop ({identity}) is not found */";
						}
						else
							resources = $"/* the requested resource ({identity}) is not found */";
					}
					else
						resources = await this.GetThemeResourcesAsync(identity, "css", cancellationToken).ConfigureAwait(false);
				}

				if (this.CacheDesktopResources)
				{
					if ((identity.Length == 34 && identity.Right(32).IsValidUUID()) || !this.ExcludedThemes.Contains(identity))
						headers = new Dictionary<string, string>(headers, StringComparer.OrdinalIgnoreCase)
						{
							{ "ETag", eTag },
							{ "Last-Modified", lastModified },
							{ "Expires", DateTime.Now.AddDays(366).ToHttpString() },
							{ "Cache-Control", "public" }
						};
					if (!(identity.Length == 34 && identity.Right(32).IsValidUUID()) && !this.ExcludedThemes.Contains(identity))
						Task.WhenAll
						(
							Utility.Cache.SetAsync(cacheKey, resources, this.CancellationToken),
							Utility.Cache.SetAsync($"{cacheKey}:time", lastModified, this.CancellationToken),
							Utility.Cache.AddSetMembersAsync("Statics", new[] { cacheKey, $"{cacheKey}:time" }, this.CancellationToken)
						).Run();
				}

				// response
				return new JObject
				{
					{ "StatusCode", (int)HttpStatusCode.OK },
					{ "Headers", headers.ToJson() },
					{ "Body", resources.Compress(this.BodyEncoding) },
					{ "BodyEncoding", this.BodyEncoding }
				};
			}

			// scripts
			if (type.IsEquals("js") || type.IsEquals("javascript") || type.IsEquals("script") || type.IsEquals("scripts"))
			{
				// prepare
				if (!requestInfo.Query.TryGetValue("x-path", out var identity) || string.IsNullOrWhiteSpace(identity))
					throw new InvalidRequestException($"The request is invalid [({requestInfo.Verb}): {requestInfo.GetURI()}]");

				identity = identity.Replace(StringComparison.OrdinalIgnoreCase, ".js", "").ToLower().Trim();
				var eTag = $"js#{identity.GenerateUUID()}";
				var cacheKey = $"js#{identity}";
				var lastModified = this.CacheDesktopResources ? await Utility.Cache.GetAsync<string>($"{cacheKey}:time", cancellationToken).ConfigureAwait(false) : null;
				var headers = new Dictionary<string, string>
				{
					{ "Content-Type", "application/javascript; charset=utf-8" },
					{ "X-Correlation-ID", requestInfo.CorrelationID }
				};

				// get last-modified and response as HTTP 304 to reduce traffict
				if (this.CacheDesktopResources)
				{
					if (lastModified == null)
					{
						if (identity.Length == 34 && identity.Right(32).IsValidUUID())
						{
							if (identity.Left(1).IsEquals("s"))
							{
								var site = await identity.Right(32).GetSiteByIDAsync(cancellationToken).ConfigureAwait(false);
								if (site != null)
									lastModified = site.LastModified.ToHttpString();
							}
							else if (identity.Left(1).IsEquals("d"))
							{
								var desktop = await identity.Right(32).GetDesktopByIDAsync(cancellationToken).ConfigureAwait(false);
								if (desktop != null)
									lastModified = desktop.LastModified.ToHttpString();
							}
						}
						else
							lastModified = this.GetThemeResourcesLastModified(identity, "js").ToHttpString();

						Task.WhenAll
						(
							Utility.Cache.SetAsync($"{cacheKey}:time", lastModified, this.CancellationToken),
							Utility.Cache.AddSetMemberAsync("Statics", $"{cacheKey}:time", this.CancellationToken)
						).Run();
					}

					if (eTag.IsEquals(noneMatch) && modifiedSince != null && lastModified != null && modifiedSince.FromHttpDateTime() >= lastModified.FromHttpDateTime())
						return new JObject
						{
							{ "StatusCode", (int)HttpStatusCode.NotModified },
							{
								"Headers",
								new Dictionary<string, string>(headers, StringComparer.OrdinalIgnoreCase)
								{
									{ "ETag", eTag },
									{ "Last-Modified", lastModified }
								}.ToJson()
							}
						};
				}

				// get resources
				var resources = this.CacheDesktopResources && !this.ExcludedThemes.Contains(identity) && !(identity.Length == 34 && identity.Right(32).IsValidUUID())
					? await Utility.Cache.GetAsync<string>(cacheKey, cancellationToken).ConfigureAwait(false)
					: null;

				if (resources == null)
				{
					if (identity.Length == 34 && identity.Right(32).IsValidUUID())
					{
						if (identity.Left(1).IsEquals("o"))
						{
							var organization = await identity.Right(32).GetOrganizationByIDAsync(cancellationToken).ConfigureAwait(false);
							resources = organization != null
								? (this.IsDebugLogEnabled ? $"/* scripts of the '{organization.Title}' organization */\r\n" : "") + (string.IsNullOrWhiteSpace(organization.Scripts) ? "" : organization.Scripts.Replace("~~/", $"{Utility.FilesHttpURI}/").Replace("~#/", $"{Utility.PortalsHttpURI}/").MinifyJs())
								: $"/* the requested organization ({identity.Right(32)}) is not found */";
						}
						else if (identity.Left(1).IsEquals("s"))
						{
							var site = await identity.Right(32).GetSiteByIDAsync(cancellationToken).ConfigureAwait(false);
							resources = site != null
								? (this.IsDebugLogEnabled ? $"/* scripts of the '{site.Title}' site */\r\n" : "") + (string.IsNullOrWhiteSpace(site.Scripts) ? "" : site.Scripts.Replace("~~/", $"{Utility.FilesHttpURI}/").Replace("~#/", $"{Utility.PortalsHttpURI}/").MinifyJs())
								: $"/* the requested site ({identity.Right(32)}) is not found */";
						}
						else if (identity.Left(1).IsEquals("d"))
						{
							var desktop = await identity.Right(32).GetDesktopByIDAsync(cancellationToken).ConfigureAwait(false);
							resources = desktop != null
								? (this.IsDebugLogEnabled ? $"/* scripts of the '{desktop.Title}' desktop */\r\n" : "") + (string.IsNullOrWhiteSpace(desktop.Scripts) ? "" : desktop.Scripts.Replace("~~/", $"{Utility.FilesHttpURI}/").Replace("~#/", $"{Utility.PortalsHttpURI}/").MinifyJs())
								: $"/* the requested desktop ({identity.Right(32)}) is not found */";
						}
						else
							resources = $"/* the requested resource ({identity}) is not found */";
					}
					else
						resources = await this.GetThemeResourcesAsync(identity, "js", cancellationToken).ConfigureAwait(false);
				}

				if (this.CacheDesktopResources)
				{
					if ((identity.Length == 34 && identity.Right(32).IsValidUUID()) || !this.ExcludedThemes.Contains(identity))
						headers = new Dictionary<string, string>(headers, StringComparer.OrdinalIgnoreCase)
						{
							{ "ETag", eTag },
							{ "Last-Modified", lastModified },
							{ "Expires", DateTime.Now.AddDays(366).ToHttpString() },
							{ "Cache-Control", "public" }
						};
					if (!(identity.Length == 34 && identity.Right(32).IsValidUUID()) && !this.ExcludedThemes.Contains(identity))
						Task.WhenAll
						(
							Utility.Cache.SetAsync(cacheKey, resources, this.CancellationToken),
							Utility.Cache.SetAsync($"{cacheKey}:time", lastModified, this.CancellationToken),
							Utility.Cache.AddSetMembersAsync("Statics", new[] { cacheKey, $"{cacheKey}:time" }, this.CancellationToken)
						).Run();
				}

				// response
				return new JObject
				{
					{ "StatusCode", (int)HttpStatusCode.OK },
					{ "Headers", headers.ToJson() },
					{ "Body", resources.Compress(this.BodyEncoding) },
					{ "BodyEncoding", this.BodyEncoding }
				};
			}

			// permanent link
			if (type.IsEquals("permanentlink") || type.IsEquals("permanently") || type.IsEquals("permanent"))
			{
				// prepare
				if (!requestInfo.Query.TryGetValue("x-path", out var info) || string.IsNullOrWhiteSpace(info))
					throw new InvalidRequestException();

				var link = info.Replace(".html", "").ToArray("/");
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
					{ "Headers", new JObject
						{
							{ "Location", url.NormalizeURLs(new Uri(requestInfo.GetParameter("x-url")), organization.Alias, false) }
						}
					}
				};
			}

			// unknown
			throw new InformationNotFoundException($"The requested resource is not found [({requestInfo.Verb}): {requestInfo.GetURI()}]");
		}

		async Task<string> GetThemeResourcesAsync(string theme, string type, CancellationToken cancellationToken, string filesHttpURI = null, string portalsHttpURI = null)
		{
			var isJs = type.IsEquals("js");
			var resources = this.IsDebugLogEnabled ? $"/* {(isJs ? "scripts" : "stylesheets")} of the '{theme}' theme */\r\n" : "";
			var directory = new DirectoryInfo(Path.Combine(Utility.DataFilesDirectory, "themes", theme, type));
			if (directory.Exists)
				await directory.GetFiles($"*.{type}").OrderBy(fileInfo => fileInfo.Name).ForEachAsync(async (fileInfo, _) =>
				{
					var resource = (await UtilityService.ReadTextFileAsync(fileInfo, null, cancellationToken).ConfigureAwait(false)).Replace("~~/", $"{filesHttpURI ?? Utility.FilesHttpURI}/").Replace("~#/", $"{portalsHttpURI ?? Utility.PortalsHttpURI}/");
					resources += (isJs ? ";" : "") + (this.IsDebugLogEnabled ? $"\r\n/* {fileInfo.FullName} */\r\n" : "")
						+ (this.DontMinifyThemes.Contains(theme) ? resource : isJs ? resource.MinifyJs() : resource.MinifyCss()) + "\r\n";
				}, cancellationToken, true, false).ConfigureAwait(false);
			return resources;
		}

		DateTime GetThemeResourcesLastModified(string theme, string type)
		{
			var lastModified = DateTimeService.CheckingDateTime;
			var directory = new DirectoryInfo(Path.Combine(Utility.DataFilesDirectory, "themes", theme, type));
			if (directory.Exists)
			{
				var files = directory.GetFiles($"*.{type}");
				if (files.Length < 1)
					lastModified = directory.LastWriteTime;
				else
					files.OrderBy(fileInfo => fileInfo.Name).ForEach(fileInfo =>
					{
						if (fileInfo.LastWriteTime > lastModified)
							lastModified = fileInfo.LastWriteTime;
					});
			}
			return lastModified;
		}
		#endregion

		#region Process desktop requests of Portals HTTP service
		async Task<JToken> ProcessHttpDesktopRequestAsync(RequestInfo requestInfo, CancellationToken cancellationToken = default)
		{
			// prepare required information
			var organizationIdentity = requestInfo.GetParameter("x-system");
			if (string.IsNullOrWhiteSpace(organizationIdentity))
				throw new InvalidRequestException($"The request is invalid [({requestInfo.Verb}): {requestInfo.GetURI()}]");
			var stopwatch = Stopwatch.StartNew();
			var organization = await (organizationIdentity.IsValidUUID() ? organizationIdentity.GetOrganizationByIDAsync(cancellationToken) : organizationIdentity.GetOrganizationByAliasAsync(cancellationToken)).ConfigureAwait(false);
			if (organization == null)
				throw new InvalidRequestException($"The request is invalid [({requestInfo.Verb}): {requestInfo.GetURI()}]");

			// prepare sites and desktops (at the first-time only)
			if (SiteProcessor.Sites.IsEmpty)
			{
				var filter = Filters<Site>.And(Filters<Site>.Equals("SystemID", organization.ID));
				var sort = Sorts<Site>.Ascending("Title");
				var sites = await Site.FindAsync(filter, sort, 0, 1, Extensions.GetCacheKey(filter, sort, 0, 1), cancellationToken).ConfigureAwait(false);
				sites.ForEach(website => website.Set(false, true));
				organization._siteIDs = sites.Select(website => website.ID).ToList();
				organization.Set(false, true);
			}

			if (DesktopProcessor.Desktops.IsEmpty || !DesktopProcessor.Desktops.Any(kvp => kvp.Value.SystemID == organization.ID))
			{
				var filter = Filters<Desktop>.And(Filters<Desktop>.Equals("SystemID", organization.ID), Filters<Desktop>.IsNull("ParentID"));
				var sort = Sorts<Desktop>.Ascending("Title");
				var desktops = await Desktop.FindAsync(filter, sort, 0, 1, Extensions.GetCacheKey(filter, sort, 0, 1), cancellationToken).ConfigureAwait(false);
				desktops.ForEach(webdesktop => webdesktop.Set(false, true));
			}

			// get site by domain
			var host = requestInfo.GetParameter("x-host");
			var site = await (host ?? "").GetSiteByDomainAsync(Utility.DefaultSite?.ID, cancellationToken).ConfigureAwait(false);

			// get default site if not found
			if (site == null)
			{
				if (host.Equals(new Uri(Utility.CmsPortalsHttpURI).Host) && (organization._siteIDs == null || organization._siteIDs.Count < 1))
				{
					organization._siteIDs = null;
					site = (await organization.FindSitesAsync(cancellationToken).ConfigureAwait(false)).FirstOrDefault();
				}
				else
					site = organization.Sites?.FirstOrDefault();
			}

			// stop if no site is found
			if (site == null)
				throw new SiteNotRecognizedException($"The requested site is not recognized ({host ?? "unknown"}){(this.IsDebugLogEnabled ? $" because the organization ({ organization.Title }) has no site [{organization.Sites?.Count}]" : "")}");

			// get desktop and prepare the redirecting url
			var writeDesktopLogs = this.WriteDesktopLogs || requestInfo.GetParameter("x-logs") != null || requestInfo.GetParameter("x-desktop-logs") != null;
			var useShortURLs = "true".IsEquals(requestInfo.GetParameter("x-use-short-urls"));
			var requestURI = new Uri(requestInfo.GetParameter("x-url") ?? requestInfo.GetParameter("x-uri"));
			var requestURL = requestURI.AbsoluteUri;
			var redirectURL = "";
			var redirectCode = 0;

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

				redirectURL += organization.AlwaysUseHtmlSuffix && !redirectURL.IsEndsWith(".html") && !redirectURL.IsEndsWith(".aspx") ? ".html" : "";
				redirectCode = (int)HttpStatusCode.MovedPermanently;
			}

			// normalize the redirect url
			if (site.AlwaysUseHTTPs || site.RedirectToNoneWWW)
			{
				if (string.IsNullOrWhiteSpace(redirectURL))
				{
					redirectURL = (site.AlwaysUseHTTPs && !requestURI.Scheme.IsEquals("https") ? "https" : requestURI.Scheme) + "://" + (site.RedirectToNoneWWW ? requestURI.Host.Replace("www.", "") : requestURI.Host) + requestURI.PathAndQuery;
					redirectCode = redirectCode > 0 ? redirectCode : site.AlwaysUseHTTPs && !requestURI.Scheme.IsEquals("https") ? (int)HttpStatusCode.Redirect : (int)HttpStatusCode.MovedPermanently;
				}
				else
				{
					if (site.AlwaysUseHTTPs)
					{
						redirectURL = redirectURL.Replace("http://", "https://");
						redirectCode = redirectCode > 0 ? redirectCode : (int)HttpStatusCode.Redirect;
					}
					if (site.RedirectToNoneWWW)
					{
						redirectURL = redirectURL.Replace("://www.", "://");
						redirectCode = redirectCode > 0 ? redirectCode : (int)HttpStatusCode.MovedPermanently;
					}
				}
			}

			// do redirect
			JObject response = null;
			if (!string.IsNullOrWhiteSpace(redirectURL) && !requestURL.Equals(redirectURL))
			{
				response = new JObject
				{
					{ "StatusCode", redirectCode > 0 ? redirectCode : (int)HttpStatusCode.Redirect },
					{ "Headers", new JObject
						{
							{ "Location", redirectURL.NormalizeURLs(requestURI, organization.Alias, false) }
						}
					}
				};
				stopwatch.Stop();
				if (writeDesktopLogs)
					this.WriteLogsAsync(requestInfo.CorrelationID, $"Redirect for matching with the settings - Execution times: {stopwatch.GetElapsedTimes()}\r\n{requestURL} => {redirectURL} [{redirectCode}]", null, this.ServiceName, "Process.Http.Request").Run();
				return response;
			}

			// start process
			var desktopInfo = $"the '{desktop.Title}' desktop [Alias: {desktop.Alias} - ID: {desktop.ID}]";
			this.WriteLogsAsync(requestInfo.CorrelationID, $"Start to process {desktopInfo} => {requestURL}", null, this.ServiceName, "Process.Http.Request").Run();

			// prepare the caching
			var processCache = this.CacheDesktopHtmls && requestInfo.GetParameter("noCache") == null;
			var forceCache = requestInfo.GetParameter("forceCache") != null;
			var cacheKey = desktop.GetDesktopCacheKey(requestURI);
			var cacheKeyOfLastModified = $"{cacheKey}:time";
			var cacheKeyOfExpiration = $"{cacheKey}:expiration";

			// check "If-Modified-Since" request to reduce traffict
			var eTag = $"desktop#{cacheKey}";
			var noneMatch = processCache && !forceCache ? requestInfo.GetHeaderParameter("If-None-Match") : null;
			var modifiedSince = processCache && !forceCache ? requestInfo.GetHeaderParameter("If-Modified-Since") ?? requestInfo.GetHeaderParameter("If-Unmodified-Since") : null;
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
						{ "ETag", eTag },
						{ "Last-Modified", lastModified },
						{ "Cache-Control", "public" }
					};
					response = new JObject
					{
						{ "StatusCode", (int)HttpStatusCode.NotModified },
						{ "Headers", headers.ToJson() }
					};
					stopwatch.Stop();
					this.WriteLogsAsync(requestInfo.CorrelationID, $"By-pass the process of {desktopInfo} => Got 'If-Modified-Since'/'If-None-Match' request headers - ETag: {eTag} - Timestamp: {lastModified} - Execution times: {stopwatch.GetElapsedTimes()}", null, this.ServiceName, "Process.Http.Request").Run();
					return response;
				}
			}

			// environment info
			var isMobile = "true".IsEquals(requestInfo.GetHeaderParameter("x-environment-is-mobile")) ? "true" : "false";
			var osInfo = requestInfo.GetHeaderParameter("x-environment-os-info") ?? "Generic OS";

			// get cache of HTML
			var html = processCache && !forceCache ? await Utility.Cache.GetAsync<string>(cacheKey, cancellationToken).ConfigureAwait(false) : null;

			// normalize the cache of HTML when got request from the refresher
			if (!string.IsNullOrWhiteSpace(html) && Utility.RefresherRefererURL.IsEquals(requestInfo.GetHeaderParameter("Referer")))
			{
				// got specified expiration time => clear to refresh
				if (await Utility.Cache.ExistsAsync(cacheKeyOfExpiration, cancellationToken).ConfigureAwait(false))
				{
					await Utility.Cache.RemoveAsync(new[] { cacheKey, cacheKeyOfLastModified, cacheKeyOfExpiration }, cancellationToken).ConfigureAwait(false);
					html = null;
				}

				// no expiration => re-update cache
				else
				{
					lastModified = lastModified ?? await Utility.Cache.GetAsync<string>(cacheKeyOfLastModified, cancellationToken).ConfigureAwait(false) ?? DateTime.Now.ToHttpString();
					Task.WhenAll
					(
						Utility.Cache.SetAsync(cacheKey, html, this.CancellationToken),
						Utility.Cache.SetAsync(cacheKeyOfLastModified, lastModified, this.CancellationToken)
					).Run();
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
					Utility.Cache.SetAsync(cacheKeyOfLastModified, lastModified, this.CancellationToken).Run();
				}
				headers = new Dictionary<string, string>(headers)
				{
					{ "ETag", eTag },
					{ "Last-Modified", lastModified },
					{ "Expires", DateTime.Now.AddMinutes(13).ToHttpString() },
					{ "Cache-Control", "public" }
				};
				response = new JObject
				{
					{ "StatusCode", (int)HttpStatusCode.OK },
					{ "Headers", headers.ToJson() },
					{ "Body", html.Compress(this.BodyEncoding) },
					{ "BodyEncoding", this.BodyEncoding }
				};
				stopwatch.Stop();
				this.WriteLogsAsync(requestInfo.CorrelationID, $"By-pass the process of {desktopInfo} => Got cached of XHTML - Key: {cacheKey} - Execution times: {stopwatch.GetElapsedTimes()}", null, this.ServiceName, "Process.Http.Request").Run();
				return response;
			}

			// process the request
			try
			{
				// prepare portlets
				if (writeDesktopLogs)
					this.WriteLogsAsync(requestInfo.CorrelationID, $"Start to prepare portlets of {desktopInfo}", null, this.ServiceName, "Process.Http.Request").Run();

				var stepwatch = Stopwatch.StartNew();
				if (desktop._portlets == null)
				{
					await desktop.FindPortletsAsync(cancellationToken, false).ConfigureAwait(false);
					await desktop.SetAsync(false, true, cancellationToken).ConfigureAwait(false);
					if (writeDesktopLogs)
					{
						stepwatch.Stop();
						this.WriteLogsAsync(requestInfo.CorrelationID, $"Complete load portlets of {desktopInfo} - Execution times: {stepwatch.GetElapsedTimes()}", null, this.ServiceName, "Process.Http.Request").Run();
					}
				}

				stepwatch.Restart();
				this.WriteLogsAsync(requestInfo.CorrelationID, $"Start to prepare data of {desktop.Portlets.Count} portlet(s) of {desktopInfo} => {desktop.Portlets.Select(p => p.Title).Join(", ")}", null, this.ServiceName, "Process.Http.Request").Run();

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
					json["Domain"] = $"{site.SubDomain}.{site.PrimaryDomain}".Replace("*.", "www.").Replace("www.www.", "www.");
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
						UtilityService.WriteTextFileAsync(Path.Combine(Utility.TempFilesDirectory, $"{$"{portletTitle}_{portletID}".GetANSIUri()}{fileSuffixName}_request.json"), requestJson?.ToString(Newtonsoft.Json.Formatting.Indented) ?? "NULL", false, null, this.CancellationToken).Run();
					}
					return data;
				}

				var language = desktop.WorkingLanguage ?? site.Language ?? "vi-VN";
				var portletData = new ConcurrentDictionary<string, JObject>(StringComparer.OrdinalIgnoreCase);
				await desktop.Portlets.Where(portlet => portlet != null).ForEachAsync(async portlet =>
				{
					var data = await this.PreparePortletAsync(portlet, requestInfo, organizationJson, siteJson, desktopsJson, language, parentIdentity, contentIdentity, pageNumber, generateAsync, writeDesktopLogs, requestInfo.CorrelationID, cancellationToken).ConfigureAwait(false);
					if (data != null)
						portletData[portlet.ID] = data;
					if (writeDesktopLogs)
						UtilityService.WriteTextFileAsync(Path.Combine(Utility.TempFilesDirectory, $"{$"{portlet.Title}_{portlet.ID}".GetANSIUri()}{fileSuffixName}_response.json"), data?.ToString(Newtonsoft.Json.Formatting.Indented) ?? "NULL", false, null, this.CancellationToken).Run();
				}, true, Utility.RunProcessorInParallelsMode).ConfigureAwait(false);
				stepwatch.Stop();
				this.WriteLogsAsync(requestInfo.CorrelationID, $"Complete prepare portlets' data of {desktopInfo} - Execution times: {stepwatch.GetElapsedTimes()}", null, this.ServiceName, "Process.Http.Request").Run();

				// generate HTML of portlets
				stepwatch.Restart();
				if (writeDesktopLogs)
					this.WriteLogsAsync(requestInfo.CorrelationID, $"Start to generate HTML of {desktopInfo}", null, this.ServiceName, "Process.Http.Request").Run();

				var portletHtmls = new ConcurrentDictionary<string, Tuple<string, bool, int>>(StringComparer.OrdinalIgnoreCase);
				var generatePortletsTask = desktop.Portlets.Where(portlet => portlet != null).ForEachAsync(async portlet =>
				{
					try
					{
						var action = !string.IsNullOrWhiteSpace(parentIdentity) && !string.IsNullOrWhiteSpace(contentIdentity) ? portlet.OriginalPortlet.AlternativeAction : portlet.OriginalPortlet.Action;
						var isList = string.IsNullOrWhiteSpace(action) || "List".IsEquals(action);
						portletHtmls[portlet.ID] = await this.GeneratePortletAsync(portlet, isList, portletData.TryGetValue(portlet.ID, out var data) ? data : null, siteJson, desktopsJson, organization.AlwaysUseHtmlSuffix, requestInfo.CorrelationID, cancellationToken, writeDesktopLogs, fileSuffixName).ConfigureAwait(false);
					}
					catch (Exception ex)
					{
						portletHtmls[portlet.ID] = new Tuple<string, bool, int>(this.GenerateErrorHtml($"Unexpected error => {ex.Message}", ex.StackTrace, requestInfo.CorrelationID, portlet.ID), true, 0);
					}
				}, true, Utility.RunProcessorInParallelsMode);

				// generate desktop
				string title = "", metaTags = "", body = "", stylesheets = "", scripts = "";
				var gotErrorOnGenerateDesktop = false;
				var mainPortlet = !string.IsNullOrWhiteSpace(desktop.MainPortletID) && portletData.ContainsKey(desktop.MainPortletID) ? portletData[desktop.MainPortletID] : null;
				try
				{
					var desktopData = await this.GenerateDesktopAsync(desktop, organization, site, mainPortlet, parentIdentity, contentIdentity, writeDesktopLogs, requestInfo.CorrelationID, cancellationToken).ConfigureAwait(false);
					title = desktopData.Item1;
					metaTags = desktopData.Item2;
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
				{
					var portletScripts = "";
					var portletStylesheets = "";

					portletHtmls.Where(kvp => !kvp.Value.Item2).Select(kvp => kvp.Key).ToList().ForEach(portletID =>
					{
						var portletDataInfo = portletHtmls[portletID];
						var portletHtml = portletDataInfo.Item1;
						var portletGotError = portletDataInfo.Item2;
						var portletCacheExpiration = portletDataInfo.Item3;
						var portletScript = "";
						var portletStylesheet = "";

						try
						{
							// prepare all SCRIPT tags
							var start = portletHtml.PositionOf("<script");
							while (start > -1)
							{
								var end = portletHtml.PositionOf("</script>", start);
								portletScript += portletHtml.Substring(start, end - start);
								portletHtml = portletHtml.Remove(start, end - start + 9);

								start = portletScript.PositionOf("<script");
								end = portletScript.PositionOf(">", start);
								portletScript = portletScript.Remove(start, end - start + 1);

								start = portletHtml.PositionOf("<script");
							}

							// prepare all STYLE tags
							start = portletHtml.PositionOf("<style");
							while (start > -1)
							{
								var end = portletHtml.PositionOf("</style>", start);
								portletStylesheet += portletHtml.Substring(start, end - start);
								portletHtml = portletHtml.Remove(start, end - start + 8);

								start = portletStylesheet.PositionOf("<style");
								end = portletStylesheet.PositionOf(">", start);
								portletStylesheet = portletStylesheet.Remove(start, end - start + 1);

								start = portletHtml.PositionOf("<style");
							}
						}
						catch { }

						portletHtmls[portletID] = new Tuple<string, bool, int>(portletHtml, portletGotError, portletCacheExpiration);
						portletScripts += portletScript;
						portletStylesheets += portletStylesheet;
					});
					scripts += string.IsNullOrWhiteSpace(portletScripts) ? "" : $"<script>{portletScripts.MinifyJs()}</script>";
					stylesheets += string.IsNullOrWhiteSpace(portletStylesheets) ? "" : $"<style>{portletStylesheets.MinifyCss()}</style>";
				}

				// prepare all SCRIPT tags of body
				try
				{
					var bodyScript = "";
					var start = body.PositionOf("<script");
					while (start > -1)
					{
						var end = body.PositionOf("</script>", start);
						bodyScript += body.Substring(start, end - start);
						body = body.Remove(start, end - start + 9);

						start = bodyScript.PositionOf("<script");
						end = bodyScript.PositionOf(">", start);
						bodyScript = bodyScript.Remove(start, end - start + 1);

						start = body.PositionOf("<script");
					}
					scripts += string.IsNullOrWhiteSpace(bodyScript) ? "" : $"<script>{bodyScript.MinifyJs()}</script>";
				}
				catch { }

				// all scripts
				scripts = "<script>var __vieapps={ids:{" + (mainPortlet?.Get<string>("IDs") ?? $"system:\"{organization.ID}\",service:\"{this.ServiceName.ToLower()}\"") + $",parent:\"{parentIdentity}\",content:\"{contentIdentity}\"" + "},URLs:{root:\"~/\",portals:\"" + (organization.FakePortalsHttpURI ?? Utility.PortalsHttpURI) + "\"},desktops:{home:{{homeDesktop}},search:{{searchDesktop}}},language:{{language}},isMobile:{{isMobile}},osInfo:\"{{osInfo}}\",correlationID:\"{{correlationID}}\"};</script>" + scripts;

				// prepare HTML of all zones
				var zoneHtmls = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
				desktop.Portlets.OrderBy(portlet => portlet.Zone).ThenBy(portlet => portlet.OrderIndex).ForEach(portlet =>
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
				if (processCache && !portletHtmls.Values.Any(data => data.Item2))
				{
					lastModified = DateTime.Now.ToHttpString();
					headers = new Dictionary<string, string>(headers, StringComparer.OrdinalIgnoreCase)
					{
						{ "ETag", eTag },
						{ "Last-Modified", lastModified },
						{ "Expires", DateTime.Now.AddMinutes(13).ToHttpString() },
						{ "Cache-Control", "public" }
					};
					var cacheExpiration = 0;
					portletHtmls.Values.Where(data => data.Item3 > 0).ForEach(data =>
					{
						if (cacheExpiration < data.Item3)
							cacheExpiration = data.Item3;
					});
					Task.WhenAll
					(
						Utility.Cache.SetAsync(cacheKey, html, cacheExpiration, this.CancellationToken),
						Utility.Cache.SetAsync(cacheKeyOfLastModified, lastModified, cacheExpiration, this.CancellationToken),
						cacheExpiration > 0 ? Utility.Cache.SetAsync(cacheKeyOfExpiration, cacheExpiration, cacheExpiration, this.CancellationToken) : Utility.Cache.RemoveAsync(cacheKeyOfExpiration, this.CancellationToken),
						Utility.Cache.AddSetMembersAsync(desktop.GetSetCacheKey(), new[] { cacheKey, cacheKeyOfLastModified, cacheKeyOfExpiration }, this.CancellationToken)
					).Run();
				}

				// normalize
				html = this.NormalizeDesktopHtml(html, requestURI, useShortURLs, organization, site, desktop, isMobile, osInfo, requestInfo.CorrelationID);

				stepwatch.Stop();
				this.WriteLogsAsync(requestInfo.CorrelationID, $"HTML code of {desktopInfo} has been generated - Execution times: {stepwatch.GetElapsedTimes()}", null, this.ServiceName, "Process.Http.Request").Run();
				if (writeDesktopLogs)
					this.WriteLogsAsync(requestInfo.CorrelationID, $"HTML code of {desktopInfo} has been generated & normalized:\r\n{html}", null, this.ServiceName, "Process.Http.Request").Run();
			}
			catch (Exception ex)
			{
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
			this.WriteLogsAsync(requestInfo.CorrelationID, $"Complete process of {desktopInfo} - Execution times: {stopwatch.GetElapsedTimes()}", null, this.ServiceName, "Process.Http.Request").Run();
			return response;
		}

		async Task<JObject> PreparePortletAsync(Portlet theportlet, RequestInfo requestInfo, JObject organizationJson, JObject siteJson, JObject desktopsJson, string language, string parentIdentity, string contentIdentity, string pageNumber, Func<ContentType, JObject, Task<JObject>> generateAsync, bool writeLogs = false, string correlationID = null, CancellationToken cancellationToken = default)
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
				this.WriteLogsAsync(correlationID, $"Start to prepare data of {portletInfo}", null, this.ServiceName, "Process.Http.Request").Run();

			// get content-type
			var contentType = await (portlet.RepositoryEntityID ?? "").GetContentTypeByIDAsync(cancellationToken).ConfigureAwait(false);
			var parentContentType = contentType?.GetParent();

			// no content-type => then by-pass on static porlet
			if (contentType == null)
			{
				stopwatch.Stop();
				if (writeLogs)
					this.WriteLogsAsync(correlationID, $"By-pass the preparing process of {portletInfo} => Static content - Execution times: {stopwatch.GetElapsedTimes()}", null, this.ServiceName, "Process.Http.Request").Run();
				return null;
			}

			// prepare
			var module = await (contentType.RepositoryID ?? "").GetModuleByIDAsync(cancellationToken).ConfigureAwait(false) ?? contentType.Module;
			parentIdentity = parentIdentity ?? requestInfo.GetQueryParameter("x-parent");
			contentIdentity = contentIdentity ?? requestInfo.GetQueryParameter("x-content");
			pageNumber = pageNumber ?? requestInfo.GetQueryParameter("x-page");

			var action = !string.IsNullOrWhiteSpace(parentIdentity) && !string.IsNullOrWhiteSpace(contentIdentity) ? portlet.AlternativeAction : portlet.Action;
			var isList = string.IsNullOrWhiteSpace(action) || "List".IsEquals(action);

			var expresion = isList && !string.IsNullOrWhiteSpace(portlet.ExpressionID) ? await portlet.ExpressionID.GetExpressionByIDAsync(cancellationToken).ConfigureAwait(false) : null;
			var optionsJson = isList ? JObject.Parse(portlet.ListSettings?.Options ?? "{}") : JObject.Parse(portlet.ViewSettings?.Options ?? "{}");
			optionsJson["ShowBreadcrumbs"] = isList ? portlet.ListSettings != null && portlet.ListSettings.ShowBreadcrumbs : portlet.ViewSettings != null && portlet.ViewSettings.ShowBreadcrumbs;
			optionsJson["ShowPagination"] = isList ? portlet.ListSettings != null && portlet.ListSettings.ShowPagination : portlet.ViewSettings != null && portlet.ViewSettings.ShowPagination;
			var desktop = await optionsJson.Get("DesktopID", "").GetDesktopByIDAsync(cancellationToken).ConfigureAwait(false);

			if (writeLogs)
				this.WriteLogsAsync(correlationID, $"Determine the action/expression for generating content of {portletInfo} - Action: {(isList ? "List" : "View")} - Expression: {portlet.ExpressionID ?? "N/A"} (Title: {expresion?.Title ?? "None"}{(expresion != null ? $" / Filter: {expresion.Filter != null} / Sort: {expresion.Sort != null}" : "")}) - Specified desktop: {(desktop != null ? $"{desktop.Title} [ID: {desktop.ID}]" : "(None)")}", null, this.ServiceName, "Process.Http.Request").Run();

			// prepare the JSON that contains the requesting information for generating content
			var requestJson = new JObject
			{
				{ "ID", theportlet.ID },
				{ "Title", theportlet.Title },
				{ "Action", isList ? "List" : "View" },
				{ "ParentIdentity", parentIdentity },
				{ "ContentIdentity", contentIdentity },
				{ "Expression", new JObject
					{
						{ "ID", expresion?.ID },
						{ "FilterBy", expresion?.Filter?.ToJson() },
						{ "SortBy", expresion?.Sort?.ToJson() },
					}
				},
				{ "Pagination", new JObject
					{
						{ "PageSize", isList && portlet.ListSettings != null ? portlet.ListSettings.PageSize : 0 },
						{ "PageNumber", isList && portlet.ListSettings != null ? portlet.ListSettings.AutoPageNumber ? (pageNumber ?? "1").CastAs<int>() : 1 : (pageNumber ?? "1").CastAs<int>() },
						{ "ShowPageLinks", portlet.PaginationSettings != null && portlet.PaginationSettings.ShowPageLinks },
						{ "NumberOfPageLinks", portlet.PaginationSettings != null ? portlet.PaginationSettings.NumberOfPageLinks : 7 }
					}
				},
				{ "IsAutoPageNumber", isList && portlet.ListSettings != null && portlet.ListSettings.AutoPageNumber },
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
					this.WriteLogsAsync(correlationID, $"Call the service ({serviceURI}) to prepare data of {portletInfo}\r\n- Request:\r\n{requestJson}", null, this.ServiceName, "Process.Http.Request").Run();
				responseJson = await generateAsync(contentType, requestJson).ConfigureAwait(false);
			}
			catch (Exception ex)
			{
				exception = ex;
				responseJson = this.GenerateErrorJson(ex, requestInfo, writeLogs, $"Error occurred while calling a service [{serviceURI}]");
			}

			stopwatch.Stop();
			if (exception != null)
				this.WriteLogsAsync(correlationID, $"Error occurred while preparing data of {portletInfo} - Execution times: {stopwatch.GetElapsedTimes()}\r\n- Request:\r\n{requestJson}\r\n- Error:\r\n{responseJson}", exception, this.ServiceName, "Process.Http.Request", LogLevel.Error).Run();
			else if (writeLogs)
				this.WriteLogsAsync(correlationID, $"Data of {portletInfo} has been prepared - Execution times: {stopwatch.GetElapsedTimes()}\r\n- Response:\r\n{responseJson}", null, this.ServiceName, "Process.Http.Request").Run();

			return responseJson;
		}

		async Task<Tuple<string, bool, int>> GeneratePortletAsync(Portlet theportlet, bool isList, JObject data, JObject siteJson, JObject desktopsJson, bool alwaysUseHtmlSuffix = true, string correlationID = null, CancellationToken cancellationToken = default, bool writeLogs = false, string fileSuffixName = null)
		{
			// get original first
			var stopwatch = Stopwatch.StartNew();
			var portlet = theportlet.OriginalPortlet;
			var portletInfo = writeLogs
				? $"the '{theportlet.Title}' portlet [ID: {theportlet.ID}{(string.IsNullOrWhiteSpace(theportlet.OriginalPortletID) ? "" : $" - alias of '{portlet.Title}' (ID: {portlet.ID})")}]"
				: null;
			if (writeLogs)
				this.WriteLogsAsync(correlationID, $"Start to generate HTML code of {portletInfo}", null, this.ServiceName, "Process.Http.Request").Run();

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
			var contentType = data != null ? await (portlet.RepositoryEntityID ?? "").GetContentTypeByIDAsync(cancellationToken).ConfigureAwait(false) : null;

			if (contentType != null)
			{
				objectType = contentType.ContentTypeDefinition?.GetObjectName() ?? "";
				var xslFilename = "";
				var xslTemplate = "";

				var errorMessage = data.Get<string>("Error");
				var errorStack = "";

				var content = "";
				XDocument xml = null;

				if (string.IsNullOrWhiteSpace(errorMessage))
					try
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
							{ "Portlet", new JObject
								{
									{ "ID", portlet.ID },
									{ "Title", portlet.Title },
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
						content = xml.Transform(xslTemplate, optionsJson.Get<bool>("EnableDocumentFunctionAndInlineScripts", false));
						if (writeLogs)
						{
							var filename = $"{$"{theportlet.Title}_{theportlet.ID}".GetANSIUri()}{fileSuffixName}";
							Task.WhenAll
							(
								this.WriteLogsAsync(correlationID, $"HTML of {portletInfo} has been transformed\r\n- XML:\r\n{xml}\r\n- XSL:\r\n{xslTemplate}\r\n- XHTML:\r\n{content}", null, this.ServiceName, "Process.Http.Request"),
								UtilityService.WriteTextFileAsync(Path.Combine(Utility.TempFilesDirectory, $"{filename}.xml"), xml.ToString(), false, null, this.CancellationToken),
								UtilityService.WriteTextFileAsync(Path.Combine(Utility.TempFilesDirectory, $"{filename}.xsl"), xslTemplate, false, null, this.CancellationToken)
							).Run();
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
							await this.WriteLogsAsync(correlationID,
								$"Error occurred while transforming HTML of {portletInfo} => {ex.Message}" +
								$"\r\n- XML:\r\n{xml}\r\n- XSL:\r\n{xslTemplate}{(string.IsNullOrWhiteSpace(isList ? portlet.ListSettings.Template : portlet.ViewSettings.Template) ? $"\r\n- XSL file: {portlet.Desktop?.WorkingTheme ?? "default"}/templates/{contentType.ContentTypeDefinition?.ModuleDefinition?.Directory?.ToLower() ?? "-"}/{contentType.ContentTypeDefinition?.ObjectName?.ToLower() ?? "-"}/{xslFilename}" : "")}"
							, ex, this.ServiceName, "Process.Http.Request", LogLevel.Error).ConfigureAwait(false);
						}
						catch (Exception e)
						{
							this.WriteLogsAsync(correlationID, $"Error occurred while transforming HTML of {portletInfo} => {e.Message}", e, this.ServiceName, "Process.Http.Request", LogLevel.Error).Run();
						}
					}
				else
				{
					gotError = true;
					errorStack = data.Get<string>("Stack");
				}

				if (!string.IsNullOrWhiteSpace(errorMessage))
					content = this.GenerateErrorHtml(errorMessage, errorStack, correlationID, portlet.ID);

				contentZone.Value = "{{content-holder}}";
				html = portletContainer.ToString().Replace(StringComparison.OrdinalIgnoreCase, "{{content-holder}}", content);
			}
			else
				html = portletContainer.ToString();

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
				["title-ansi"] = title
			});

			stopwatch.Stop();
			if (writeLogs)
				this.WriteLogsAsync(correlationID, $"HTML code of {portletInfo} has been generated - Execution times: {stopwatch.GetElapsedTimes()}", null, this.ServiceName, "Process.Http.Request").Run();

			return new Tuple<string, bool, int>(html, gotError, cacheExpiration);
		}

		async Task<Tuple<string, string, string, string, string>> GenerateDesktopAsync(Desktop desktop, Organization organization, Site site, JObject mainPortlet, string parentIdentity, string contentIdentity, bool writeLogs = false, string correlationID = null, CancellationToken cancellationToken = default)
		{
			var desktopInfo = $"the '{desktop.Title}' desktop [Alias: {desktop.Alias} - ID: {desktop.ID}]";

			var coverURI = mainPortlet?.Get<string>("CoverURI");
			var metaInfo = mainPortlet?.Get<JArray>("MetaTags");
			var seoInfo = mainPortlet?.Get<JObject>("SEOInfo");

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

			// start meta tags with information for SEO and social networks
			var metaTags = "";

			if (!string.IsNullOrWhiteSpace(description))
			{
				description = description.Replace("&", "&amp;").Replace("\"", "&quot;").Replace("<", "&lt;").Replace(">", "&gt;");
				metaTags += $"<meta name=\"description\" property=\"og:description\" content=\"{description}\"/>";
			}

			if (!string.IsNullOrWhiteSpace(keywords))
				metaTags += $"<meta name=\"keywords\" content=\"{keywords.Replace("\"", "&quot;").Replace("<", "&lt;").Replace(">", "&gt;")}\"/>";

			metaTags += $"<meta name=\"twitter:title\" property=\"og:title\" content=\"{title}\"/>";
			if (!string.IsNullOrWhiteSpace(description))
				metaTags += $"<meta name=\"twitter:description\" content=\"{description}\"/>";

			if (!string.IsNullOrWhiteSpace(coverURI))
				metaTags += $"<meta name=\"twitter:image\" property=\"og:image\" content=\"{coverURI}\"/>";
			if (!string.IsNullOrWhiteSpace(desktop.CoverURI))
				metaTags += $"<meta name=\"twitter:image\" property=\"og:image\" content=\"{desktop.CoverURI}\"/>";
			if (!string.IsNullOrWhiteSpace(site.CoverURI))
				metaTags += $"<meta name=\"twitter:image\" property=\"og:image\" content=\"{site.CoverURI}\"/>";

			if (!string.IsNullOrWhiteSpace(desktop.IconURI))
				metaTags += $"<link rel=\"icon\" type=\"image/{(desktop.IconURI.IsEndsWith(".ico") ? "x-icon" : desktop.IconURI.IsEndsWith(".png") ? "png" : "jpeg")}\" href=\"{desktop.IconURI}\"/>"
					+ $"<link rel=\"shortcut icon\" type=\"image/{(desktop.IconURI.IsEndsWith(".ico") ? "x-icon" : desktop.IconURI.IsEndsWith(".png") ? "png" : "jpeg")}\" href=\"{desktop.IconURI}\"/>";
			if (!string.IsNullOrWhiteSpace(site.IconURI))
				metaTags += $"<link rel=\"icon\" type=\"image/{(site.IconURI.IsEndsWith(".ico") ? "x-icon" : site.IconURI.IsEndsWith(".png") ? "png" : "jpeg")}\" href=\"{site.IconURI}\"/>"
					+ $"<link rel=\"shortcut icon\" type=\"image/{(site.IconURI.IsEndsWith(".ico") ? "x-icon" : site.IconURI.IsEndsWith(".png") ? "png" : "jpeg")}\" href=\"{site.IconURI}\"/>";

			// add addtional meta tags of main portlet
			metaInfo?.Select(meta => (meta as JValue)?.Value?.ToString()).Where(meta => !string.IsNullOrWhiteSpace(meta)).ForEach(meta => metaTags += meta);

			// add meta tags of the organization
			if (!string.IsNullOrWhiteSpace(organization.MetaTags))
				metaTags += organization.MetaTags;

			// add meta tags of the site
			if (!string.IsNullOrWhiteSpace(site.MetaTags))
				metaTags += site.MetaTags;

			// add meta tags of the desktop
			if (!string.IsNullOrWhiteSpace(desktop.MetaTags))
				metaTags += desktop.MetaTags;

			// add the required stylesheet libraries
			var stylesheets = site.UseInlineStylesheets
				? (await UtilityService.ReadTextFileAsync(Path.Combine(Utility.DataFilesDirectory, "assets", "default.css"), null, cancellationToken).ConfigureAwait(false)).MinifyCss() + await this.GetThemeResourcesAsync("default", "css", cancellationToken).ConfigureAwait(false)
				: $"<link rel=\"stylesheet\" href=\"~#/_assets/default.css?v={DateTime.Now.GetTimeQuarter().ToUnixTimestamp()}\"/><link rel=\"stylesheet\" href=\"~#/_css/default.css?v={DateTime.Now.GetTimeQuarter().ToUnixTimestamp()}\"/>";

			// add the stylesheet of the organization theme
			var organizationTheme = organization.Theme ?? "default";
			if (!"default".IsEquals(organizationTheme))
				stylesheets += site.UseInlineStylesheets
					? await this.GetThemeResourcesAsync(organizationTheme, "css", cancellationToken).ConfigureAwait(false)
					: $"<link rel=\"stylesheet\" href=\"~#/_css/{organizationTheme}.css?v={DateTime.Now.GetTimeQuarter().ToUnixTimestamp()}\"/>";

			// add the stylesheet of the site theme
			var siteTheme = site.Theme ?? "default";
			if (!"default".IsEquals(siteTheme) && organizationTheme.IsEquals(siteTheme))
				stylesheets += site.UseInlineStylesheets
					? await this.GetThemeResourcesAsync(siteTheme, "css", cancellationToken).ConfigureAwait(false)
					: $"<link rel=\"stylesheet\" href=\"~#/_css/{siteTheme}.css?v={DateTime.Now.GetTimeQuarter().ToUnixTimestamp()}\"/>";

			// add the stylesheet of the desktop theme
			var desktopTheme = desktop.WorkingTheme ?? "default";
			if (!"default".IsEquals(desktopTheme) && organizationTheme.IsEquals(desktopTheme) && siteTheme.IsEquals(desktopTheme))
				stylesheets += site.UseInlineStylesheets
					? await this.GetThemeResourcesAsync(desktopTheme, "css", cancellationToken).ConfigureAwait(false)
					: $"<link rel=\"stylesheet\" href=\"~#/_css/{desktopTheme}.css?v={DateTime.Now.GetTimeQuarter().ToUnixTimestamp()}\"/>";

			// add the stylesheet of the site
			if (!string.IsNullOrWhiteSpace(site.Stylesheets))
				stylesheets += site.UseInlineStylesheets
					? site.Stylesheets.MinifyCss()
					: $"<link rel=\"stylesheet\" href=\"~#/_css/s_{site.ID}.css?v={site.LastModified.ToUnixTimestamp()}\"/>";

			// add the stylesheet of the desktop
			if (!string.IsNullOrWhiteSpace(desktop.Stylesheets))
				stylesheets += site.UseInlineStylesheets
					? desktop.Stylesheets.MinifyCss()
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
			var scripts = "<script src=\"" + UtilityService.GetAppSetting("Portals:Desktops:Resources:JQuery", "https://cdnjs.cloudflare.com/ajax/libs/jquery/3.5.1/jquery.min.js") + "\"></script>"
				+ "<script src=\"" + UtilityService.GetAppSetting("Portals:Desktops:Resources:CryptoJs", "https://cdnjs.cloudflare.com/ajax/libs/crypto-js/4.0.0/crypto-js.min.js") + "\"></script>"
				+ (site.UseInlineScripts ? "<script>" + (await UtilityService.ReadTextFileAsync(Path.Combine(Utility.DataFilesDirectory, "assets", "default.js"), null, cancellationToken).ConfigureAwait(false)).MinifyJs() : $"<script src=\"~#/_assets/default.js?v={DateTime.Now.GetTimeQuarter().ToUnixTimestamp()}\"></script>");

			// add scripts of the default theme
			var directory = new DirectoryInfo(Path.Combine(Utility.DataFilesDirectory, "themes", "default", "js"));
			if (directory.Exists && this.AllowSrcResourceFiles)
			{
				scripts += site.UseInlineScripts ? "</script>" : "";
				await directory.GetFiles("*.src").OrderBy(fileInfo => fileInfo.Name).ForEachAsync(async (fileInfo, _) =>
				{
					scripts += await UtilityService.ReadTextFileAsync(fileInfo, null, cancellationToken).ConfigureAwait(false) + "\r\n";
				}, cancellationToken, true, false).ConfigureAwait(false);
				scripts += site.UseInlineScripts ? "<script>" : "";
			}

			scripts += site.UseInlineScripts
					? await this.GetThemeResourcesAsync("default", "js", cancellationToken).ConfigureAwait(false)
					: $"<script src=\"~#/_js/default.js?v={DateTime.Now.GetTimeQuarter().ToUnixTimestamp()}\"></script>";

			// add scripts of the organization theme
			if (!"default".IsEquals(organizationTheme))
			{
				directory = new DirectoryInfo(Path.Combine(Utility.DataFilesDirectory, "themes", organizationTheme, "js"));
				if (directory.Exists && this.AllowSrcResourceFiles)
				{
					scripts += site.UseInlineScripts ? "</script>" : "";
					await directory.GetFiles("*.src").OrderBy(fileInfo => fileInfo.Name).ForEachAsync(async (fileInfo, _) =>
					{
						scripts += await UtilityService.ReadTextFileAsync(fileInfo, null, cancellationToken).ConfigureAwait(false) + "\r\n";
					}, cancellationToken, true, false).ConfigureAwait(false);
					scripts += site.UseInlineScripts ? "<script>" : "";
				}
				scripts += site.UseInlineScripts
					? await this.GetThemeResourcesAsync(organizationTheme, "js", cancellationToken).ConfigureAwait(false)
					: $"<script src=\"~#/_js/{organizationTheme}.js?v={DateTime.Now.GetTimeQuarter().ToUnixTimestamp()}\"></script>";
			}

			// add scripts of the site theme
			if (!"default".IsEquals(siteTheme) && organizationTheme.IsEquals(siteTheme))
			{
				directory = new DirectoryInfo(Path.Combine(Utility.DataFilesDirectory, "themes", siteTheme, "js"));
				if (directory.Exists && this.AllowSrcResourceFiles)
				{
					scripts += site.UseInlineScripts ? "</script>" : "";
					await directory.GetFiles("*.src").OrderBy(fileInfo => fileInfo.Name).ForEachAsync(async (fileInfo, _) =>
					{
						scripts += await UtilityService.ReadTextFileAsync(fileInfo, null, cancellationToken).ConfigureAwait(false) + "\r\n";
					}, cancellationToken, true, false).ConfigureAwait(false);
					scripts += site.UseInlineScripts ? "<script>" : "";
				}
				scripts += site.UseInlineScripts
					? await this.GetThemeResourcesAsync(siteTheme, "js", cancellationToken).ConfigureAwait(false)
					: $"<script src=\"~#/_js/{siteTheme}.js?v={DateTime.Now.GetTimeQuarter().ToUnixTimestamp()}\"></script>";
			}

			// add scripts of the desktop theme
			if (!"default".IsEquals(desktopTheme) && organizationTheme.IsEquals(desktopTheme) && siteTheme.IsEquals(desktopTheme))
			{
				directory = new DirectoryInfo(Path.Combine(Utility.DataFilesDirectory, "themes", desktopTheme, "js"));
				if (directory.Exists && this.AllowSrcResourceFiles)
				{
					scripts += site.UseInlineScripts ? "</script>" : "";
					await directory.GetFiles("*.src").OrderBy(fileInfo => fileInfo.Name).ForEachAsync(async (fileInfo, _) =>
					{
						scripts += await UtilityService.ReadTextFileAsync(fileInfo, null, cancellationToken).ConfigureAwait(false) + "\r\n";
					}, cancellationToken, true, false).ConfigureAwait(false);
					scripts += site.UseInlineScripts ? "<script>" : "";
				}
				scripts += site.UseInlineScripts
					? await this.GetThemeResourcesAsync(desktopTheme, "js", cancellationToken).ConfigureAwait(false)
					: $"<script src=\"~#/_js/{desktopTheme}.js?v={DateTime.Now.GetTimeQuarter().ToUnixTimestamp()}\"></script>";
			}

			// add the scripts of the organization
			if (!string.IsNullOrWhiteSpace(organization.ScriptLibraries))
				scripts += site.UseInlineScripts
					? $"</script>{organization.ScriptLibraries}<script>"
					: organization.ScriptLibraries;

			if (!string.IsNullOrWhiteSpace(organization.Scripts))
				scripts += site.UseInlineScripts
					? organization.Scripts.MinifyJs()
					: $"<script src=\"~#/_js/o_{organization.ID}.js?v={organization.LastModified.ToUnixTimestamp()}\"></script>";

			// add the scripts of the site
			if (!string.IsNullOrWhiteSpace(site.ScriptLibraries))
				scripts += site.UseInlineScripts
					? $"</script>{site.ScriptLibraries}<script>"
					: site.ScriptLibraries;

			if (!string.IsNullOrWhiteSpace(site.Scripts))
				scripts += site.UseInlineScripts
					? site.Scripts.MinifyJs()
					: $"<script src=\"~#/_js/s_{site.ID}.js?v={site.LastModified.ToUnixTimestamp()}\"></script>";

			// add the scripts of the desktop
			if (!string.IsNullOrWhiteSpace(desktop.ScriptLibraries))
				scripts += site.UseInlineScripts
					? $"</script>{desktop.ScriptLibraries}<script>"
					: desktop.ScriptLibraries;

			if (!string.IsNullOrWhiteSpace(desktop.Scripts))
				scripts += site.UseInlineScripts
					? desktop.Scripts.MinifyJs()
					: $"<script src=\"~#/_js/d_{desktop.ID}.js?v={desktop.LastModified.ToUnixTimestamp()}\"></script>";

			scripts += site.UseInlineScripts ? "</script>" : "";

			// prepare desktop zones
			var desktopContainer = (await desktop.GetTemplateAsync(cancellationToken).ConfigureAwait(false)).GetXDocument();
			var desktopZones = desktopContainer.GetZones().ToList();
			if (writeLogs)
				this.WriteLogsAsync(correlationID, $"Prepare the zone(s) of {desktopInfo} => {desktopZones.GetZoneNames().Join(", ")}", null, this.ServiceName, "Process.Http.Request").Run();

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
				this.WriteLogsAsync(correlationID, $"Remove empty zone(s) of {desktopInfo} => {removedZones.GetZoneNames().Join(", ")}", null, this.ServiceName, "Process.Http.Request").Run();

			// add css class '.full' to a zone that the parent only got this zone
			desktopZones.Where(zone => zone.Parent.Elements().Count() == 1).Where(zone => zone.Parent.Attribute("class") == null || !zone.Parent.Attribute("class").Value.IsContains("fixed")).ForEach(zone =>
			{
				var cssAttribute = zone.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName.IsEquals("class"));
				if (cssAttribute == null)
					zone.Add(new XAttribute("class", "full"));
				else
					cssAttribute.Value = $"{cssAttribute.Value.Trim()} full";
			});

			// prepare main-portlet for generating CSS classes of the desktop body
			var mainPortletType = "";
			var mainPortletAction = "";
			var theMainPortlet = mainPortlet != null ? desktop.Portlets.Find(p => p.ID == desktop.MainPortletID) : null;
			if (theMainPortlet != null)
			{
				var contentType = await (theMainPortlet.RepositoryEntityID ?? "").GetContentTypeByIDAsync(cancellationToken).ConfigureAwait(false);
				mainPortletType = contentType?.ContentTypeDefinition?.GetObjectName() ?? "";
				var action = !string.IsNullOrWhiteSpace(parentIdentity) && !string.IsNullOrWhiteSpace(contentIdentity) ? theMainPortlet.OriginalPortlet.AlternativeAction : theMainPortlet.OriginalPortlet.Action;
				mainPortletAction = string.IsNullOrWhiteSpace(action) || "List".IsEquals(action) ? "List" : "View";
			}

			// get the desktop body
			var body = desktopContainer.ToString(SaveOptions.DisableFormatting).Format(new Dictionary<string, object>
			{
				["theme"] = desktopTheme,
				["skin"] = desktopTheme,
				["organization"] = organization.Alias,
				["organization-alias"] = organization.Alias,
				["desktop"] = desktop.Alias,
				["desktop-alias"] = desktop.Alias,
				["alias"] = desktop.Alias,
				["main-portlet-type"] = mainPortletType.ToLower().Replace(".", "-"),
				["main-portlet-action"] = mainPortletAction.ToLower(),
				["parent-identity"] = parentIdentity ?? "",
				["content-identity"] = contentIdentity ?? ""
			});

			return new Tuple<string, string, string, string, string>(title, metaTags, body, stylesheets, scripts);
		}

		string NormalizeDesktopHtml(string html, Uri requestURI, bool useShortURLs, Organization organization, Site site, Desktop desktop, string isMobile, string osInfo, string correlationID)
		{
			var homeDesktop = site.HomeDesktop != null ? $"\"{site.HomeDesktop.Alias}{(organization.AlwaysUseHtmlSuffix ? ".html" : "")}\"" : "undefined";
			var searchDesktop = site.SearchDesktop != null ? $"\"{site.SearchDesktop.Alias}{(organization.AlwaysUseHtmlSuffix ? ".html" : "")}\"" : "undefined";
			var language = "\"" + (desktop.WorkingLanguage ?? site.Language ?? "vi-VN") + "\"";
			var osMode = "true".IsEquals(isMobile) ? "mobile-os" : "desktop-os";
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
				["isMobile"] = isMobile,
				["is-mobile"] = isMobile,
				["osInfo"] = osInfo,
				["os-info"] = osInfo,
				["osPlatform"] = osInfo.GetANSIUri(),
				["os-platform"] = osInfo.GetANSIUri(),
				["osMode"] = osMode,
				["os-mode"] = osMode,
				["correlationID"] = correlationID,
				["correlation-id"] = correlationID
			}).NormalizeURLs(requestURI, organization.Alias, useShortURLs, true, string.IsNullOrWhiteSpace(organization.FakeFilesHttpURI) ? null : organization.FakeFilesHttpURI, string.IsNullOrWhiteSpace(organization.FakePortalsHttpURI) ? null : organization.FakePortalsHttpURI);
		}

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
				json["Error"] = string.IsNullOrWhiteSpace(errorMessage) ? details.Item2 : $"{errorMessage} => {details.Item2}";
				json["Type"] = details.Item3;
				if (addErrorStack)
					json["Stack"] = details.Item4;
			}
			else if (addErrorStack)
				json["Stack"] = exception.StackTrace;
			json["CorrelationID"] = requestInfo.CorrelationID;
			return json;
		}

		string GenerateErrorHtml(string errorMessage, string errorStack, string correlationID, string objectID, string objectIDLabel = null)
			=> "<div>"
				+ $"<div style=\"color:red\">{errorMessage.Replace("\"", "&quot;").Replace("<", "&lt;").Replace(">", "&gt;")}</div>"
				+ $"<div style=\"font-size:80%\">Correlation ID: {correlationID} - {objectIDLabel ?? "Portlet ID"}: {objectID}</div>"
				+ (this.IsDebugLogEnabled
					? $"<div style=\"font-size:80%\">{errorStack?.Replace("\"", "&quot;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\r\n", "<br/>")}</div>"
					: $"<!-- {errorStack?.Replace("\"", "&quot;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\r\n", "<br/>")} -->")
				+ "</div>";
		#endregion

		#region Generate data for working with CMS Portals
		public async Task<JObject> GenerateAsync(RequestInfo requestInfo, CancellationToken cancellationToken = default)
		{
			try
			{
				var isSystemAdministrator = await this.IsSystemAdministratorAsync(requestInfo, cancellationToken).ConfigureAwait(false);
				switch (requestInfo.ObjectName.ToLower().Trim())
				{
					case "category":
					case "cms.category":
						return await CategoryProcessor.GenerateAsync(requestInfo, isSystemAdministrator, cancellationToken).ConfigureAwait(false);

					case "content":
					case "cms.content":
						return await ContentProcessor.GenerateAsync(requestInfo, isSystemAdministrator, cancellationToken).ConfigureAwait(false);

					case "item":
					case "cms.item":
						return await ItemProcessor.GenerateAsync(requestInfo, isSystemAdministrator, cancellationToken).ConfigureAwait(false);

					case "link":
					case "cms.link":
						return await LinkProcessor.GenerateAsync(requestInfo, isSystemAdministrator, cancellationToken).ConfigureAwait(false);

					default:
						throw new InvalidRequestException();
				}
			}
			catch (Exception ex)
			{
				throw this.GetRuntimeException(requestInfo, ex);
			}
		}

		public async Task<JArray> GenerateMenuAsync(RequestInfo requestInfo, CancellationToken cancellationToken = default)
		{
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
			var gotRights = await this.IsSystemAdministratorAsync(requestInfo).ConfigureAwait(false) || await this.CanModerateAsync(requestInfo, "Organization", cancellationToken).ConfigureAwait(false) || requestInfo.Session.User.IsViewer(@object.WorkingPrivileges);
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
			if (children == null || children.Count < 1)
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

			Exception exception = null;
			var menu = new JArray();
			await children.Where(child => child != null).OrderBy(child => child.OrderIndex).ForEachAsync(async (child, _) =>
			{
				if (exception != null)
					return;

				if (child is Category category)
					menu.Add(await requestInfo.GenerateMenuAsync(category, thumbnails?.GetThumbnailURL(child.ID, pngThumbnails, bigThumbnails, thumbnailsWidth, thumbnailsHeight), level, maxLevel, pngThumbnails, bigThumbnails, thumbnailsWidth, thumbnailsHeight, cancellationToken).ConfigureAwait(false));

				else if (child is Link link)
					menu.Add(await requestInfo.GenerateMenuAsync(link, thumbnails?.GetThumbnailURL(child.ID, pngThumbnails, bigThumbnails, thumbnailsWidth, thumbnailsHeight), level, maxLevel, pngThumbnails, bigThumbnails, thumbnailsWidth, thumbnailsHeight, cancellationToken).ConfigureAwait(false));
			}, cancellationToken, true, false).ConfigureAwait(false);

			if (exception != null)
				throw requestInfo.GetRuntimeException(exception, null, (msg, ex) => requestInfo.WriteErrorAsync(ex, cancellationToken, $"Error occurred while generating a child menu => {msg} : {@object.ToJson()}", "Links").Run());

			return menu;
		}
		#endregion

		#region Export/Import objects (Working with Excel files)
		async Task<JToken> DoExcelActionAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			var requestJson = requestInfo.GetRequestJson();
			var objectName = requestJson.Get("ObjectName", "").Trim();

			var contentType = await(requestJson.Get<string>("RepositoryEntityID") ?? requestJson.Get<string>("ContentTypeID") ?? "").GetContentTypeByIDAsync(cancellationToken).ConfigureAwait(false);
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
						gotRights = requestInfo.Session.User.IsAdministrator(null, null, organization, requestInfo.CorrelationID);
						break;

					case "category":
					case "cms.category":
						gotRights = requestInfo.Session.User.IsModerator(module?.WorkingPrivileges, null, organization, requestInfo.CorrelationID);
						break;

					case "content":
					case "cms.content":
					case "item":
					case "cms.item":
					case "link":
					case "cms.link":
					case "contact":
					case "cms.contact":
					case "utils.contact":
					case "utilities.contact":
						gotRights = requestInfo.Session.User.IsEditor(contentType?.WorkingPrivileges, contentType?.Module?.WorkingPrivileges, organization, requestInfo.CorrelationID);
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

					case "contact":
					case "cms.contact":
					case "utils.contact":
					case "utilities.contact":
						this.Export(processID, deviceID, contentType?.ID,
							this.GetFilter(filterBy, filter =>
							{
								if (filter.GetValue("SystemID") == null)
									filter.Add(Filters<Contact>.Equals("SystemID", organization.ID));
								if (filter.GetValue("RepositoryID") == null && module != null)
									filter.Add(Filters<Contact>.Equals("RepositoryID", module.ID));
								if (filter.GetValue("RepositoryEntityID") == null && contentType != null)
									filter.Add(Filters<Contact>.Equals("RepositoryEntityID", contentType.ID));
							},
							Filters<Contact>.Equals("SystemID", organization.ID)),
							sortBy?.ToSortBy<Contact>(), pageSize, pageNumber, maxPages);
						break;
				}
			}
			else if ("import".IsEquals(requestInfo.GetObjectIdentity()))
			{
				requestInfo.Header["x-filename"] = requestJson.Get<string>("Filename") ?? requestJson.Get<string>("x-filename");
				requestInfo.Header["x-node"] = requestJson.Get<string>("NodeID") ?? requestJson.Get<string>("x-node");
				var filename = await requestInfo.DownloadTemporaryFileAsync(cancellationToken).ConfigureAwait(false);
				var userID = requestInfo.Session.User.ID;
				switch (objectName.ToLower())
				{
					case "organization":
					case "core.organization":
						this.Import<Organization>(processID, deviceID, userID, filename, contentType?.ID, objects => objects.ForEach(@object => Task.WhenAll(
							@object.SetAsync(),
							Utility.RTUService.SendUpdateMessageAsync(new UpdateMessage
							{
								Type = $"{requestInfo.ServiceName}#{objectName}#Update",
								Data = @object.ToJson(),
								DeviceID = "*"
							}, this.CancellationTokenSource.Token),
							Utility.RTUService.SendInterCommunicateMessageAsync(new CommunicateMessage(requestInfo.ServiceName)
							{
								Type = $"{objectName}#Update",
								Data = @object.ToJson(),
								ExcludedNodeID = Utility.NodeID
							}, this.CancellationTokenSource.Token)
						).Run()));
						break;

					case "role":
					case "core.role":
						this.Import<Role>(processID, deviceID, userID, filename, contentType?.ID, objects => objects.ForEach(@object => Task.WhenAll(
							@object.SetAsync(),
							Utility.RTUService.SendUpdateMessageAsync(new UpdateMessage
							{
								Type = $"{requestInfo.ServiceName}#{objectName}#Update",
								Data = @object.ToJson(),
								DeviceID = "*"
							}, this.CancellationTokenSource.Token),
							Utility.RTUService.SendInterCommunicateMessageAsync(new CommunicateMessage(requestInfo.ServiceName)
							{
								Type = $"{objectName}#Update",
								Data = @object.ToJson(),
								ExcludedNodeID = Utility.NodeID
							}, this.CancellationTokenSource.Token)
						).Run()));
						break;

					case "site":
					case "core.site":
						this.Import<Site>(processID, deviceID, userID, filename, contentType?.ID, objects => objects.ForEach(@object => Task.WhenAll(
							@object.SetAsync(),
							Utility.RTUService.SendUpdateMessageAsync(new UpdateMessage
							{
								Type = $"{requestInfo.ServiceName}#{objectName}#Update",
								Data = @object.ToJson(),
								DeviceID = "*"
							}, this.CancellationTokenSource.Token),
							Utility.RTUService.SendInterCommunicateMessageAsync(new CommunicateMessage(requestInfo.ServiceName)
							{
								Type = $"{objectName}#Update",
								Data = @object.ToJson(),
								ExcludedNodeID = Utility.NodeID
							}, this.CancellationTokenSource.Token)
						).Run()));
						break;

					case "desktop":
					case "core.desktop":
						this.Import<Desktop>(processID, deviceID, userID, filename, contentType?.ID, objects => objects.ForEach(@object => Task.WhenAll(
							@object.SetAsync(),
							Utility.RTUService.SendUpdateMessageAsync(new UpdateMessage
							{
								Type = $"{requestInfo.ServiceName}#{objectName}#Update",
								Data = @object.ToJson(),
								DeviceID = "*"
							}, this.CancellationTokenSource.Token),
							Utility.RTUService.SendInterCommunicateMessageAsync(new CommunicateMessage(requestInfo.ServiceName)
							{
								Type = $"{objectName}#Update",
								Data = @object.ToJson(),
								ExcludedNodeID = Utility.NodeID
							}, this.CancellationTokenSource.Token)
						).Run()));
						break;

					case "portlet":
					case "core.portlet":
						this.Import<Portlet>(processID, deviceID, userID, filename, contentType?.ID);
						break;

					case "module":
					case "core.module":
						this.Import<Module>(processID, deviceID, userID, filename, contentType?.ID, objects => objects.ForEach(@object => Task.WhenAll(
							@object.SetAsync(),
							Utility.RTUService.SendUpdateMessageAsync(new UpdateMessage
							{
								Type = $"{requestInfo.ServiceName}#{objectName}#Update",
								Data = @object.ToJson(),
								DeviceID = "*"
							}, this.CancellationTokenSource.Token),
							Utility.RTUService.SendInterCommunicateMessageAsync(new CommunicateMessage(requestInfo.ServiceName)
							{
								Type = $"{objectName}#Update",
								Data = @object.ToJson(),
								ExcludedNodeID = Utility.NodeID
							}, this.CancellationTokenSource.Token)
						).Run()));
						break;

					case "contenttype":
					case "content.type":
					case "content-type":
					case "core.contenttype":
					case "core.content.type":
						this.Import<ContentType>(processID, deviceID, userID, filename, contentType?.ID, objects => objects.ForEach(@object => Task.WhenAll(
							@object.SetAsync(),
							Utility.RTUService.SendUpdateMessageAsync(new UpdateMessage
							{
								Type = $"{requestInfo.ServiceName}#{objectName}#Update",
								Data = @object.ToJson(),
								DeviceID = "*"
							}, this.CancellationTokenSource.Token),
							Utility.RTUService.SendInterCommunicateMessageAsync(new CommunicateMessage(requestInfo.ServiceName)
							{
								Type = $"{objectName}#Update",
								Data = @object.ToJson(),
								ExcludedNodeID = Utility.NodeID
							}, this.CancellationTokenSource.Token)
						).Run()));
						break;

					case "expression":
					case "core.expression":
						this.Import<Expression>(processID, deviceID, userID, filename, contentType?.ID, objects => objects.ForEach(@object => Task.WhenAll(
							@object.SetAsync(),
							Utility.RTUService.SendUpdateMessageAsync(new UpdateMessage
							{
								Type = $"{requestInfo.ServiceName}#{objectName}#Update",
								Data = @object.ToJson(),
								DeviceID = "*"
							}, this.CancellationTokenSource.Token),
							Utility.RTUService.SendInterCommunicateMessageAsync(new CommunicateMessage(requestInfo.ServiceName)
							{
								Type = $"{objectName}#Update",
								Data = @object.ToJson(),
								ExcludedNodeID = Utility.NodeID
							}, this.CancellationTokenSource.Token)
						).Run()));
						break;

					case "category":
					case "cms.category":
						this.Import<Category>(processID, deviceID, userID, filename, contentType?.ID, objects => objects.ForEach(@object => Task.WhenAll(
							@object.SetAsync(false, false, this.CancellationTokenSource.Token),
							@object.ClearRelatedCacheAsync(this.CancellationTokenSource.Token),
							Utility.RTUService.SendUpdateMessageAsync(new UpdateMessage
							{
								Type = $"{requestInfo.ServiceName}#{objectName}#Update",
								Data = @object.ToJson(),
								DeviceID = "*"
							}, this.CancellationTokenSource.Token),
							Utility.RTUService.SendInterCommunicateMessageAsync(new CommunicateMessage(requestInfo.ServiceName)
							{
								Type = $"{objectName}#Update",
								Data = @object.ToJson(),
								ExcludedNodeID = Utility.NodeID
							}, this.CancellationTokenSource.Token)
						).Run()));
						break;

					case "content":
					case "cms.content":
						this.Import<Content>(processID, deviceID, userID, filename, contentType?.ID, objects => objects.ForEach(@object => @object.ClearRelatedCacheAsync(this.CancellationTokenSource.Token).Run()));
						break;

					case "item":
					case "cms.item":
						this.Import<Item>(processID, deviceID, userID, filename, contentType?.ID, objects => objects.ForEach(@object => @object.ClearRelatedCacheAsync(this.CancellationTokenSource.Token).Run()));
						break;

					case "link":
					case "cms.link":
						this.Import<Link>(processID, deviceID, userID, filename, contentType?.ID, objects => objects.ForEach(@object => @object.ClearRelatedCacheAsync(this.CancellationTokenSource.Token).Run()));
						break;

					case "contact":
					case "cms.contact":
					case "utils.contact":
					case "utilities.contact":
						this.Import<Contact>(processID, deviceID, userID, filename, contentType?.ID);
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
						totalRecords = await RepositoryMediator.CountAsync(null, filter, repositoryEntityID, false, null, 0, this.CancellationTokenSource.Token).ConfigureAwait(false);
						totalPages = totalRecords < 1 ? 0 : new Tuple<long, int>(totalRecords, pageSize).GetTotalPages();
					}

					var dataSet = totalPages < 1
						? ExcelService.ToDataSet<T>(null, repositoryEntityID)
						: null;

					var exceptions = new List<Exception>();
					while (pageNumber <= totalPages && (maxPages == 0 || pageNumber <= maxPages))
					{
						await this.SendUpdateMessageAsync(new UpdateMessage
						{
							Type = "Portals#Excel#Export",
							DeviceID = deviceID,
							Data = new JObject
							{
								{ "ProcessID", processID },
								{ "Status", "Processing" },
								{ "Percentage", $"{pageNumber * 100/totalPages:#0.0}%" }
							}
						}, this.CancellationTokenSource.Token).ConfigureAwait(false);

						try
						{
							var objects = pageNumber <= totalPages && (maxPages == 0 || pageNumber <= maxPages)
								? await RepositoryMediator.FindAsync(null, filter, sort, pageSize, pageNumber, repositoryEntityID, false, null, 0, this.CancellationTokenSource.Token).ConfigureAwait(false)
								: new List<T>();
							if (pageNumber < 2)
								dataSet = objects.ToDataSet(repositoryEntityID);
							else
								dataSet.Tables[0].UpdateDataTable(objects, repositoryEntityID);
						}
						catch (Exception ex)
						{
							exceptions.Add(new RepositoryOperationException($"Error occurred while preparing objects to export to Excel => {ex.GetTypeName(true)}: {ex.Message}", ex));
							this.WriteLogsAsync(processID, $"Error occurred while preparing objects to export to Excel => {ex.GetTypeName(true)}: {ex.Message}", ex, this.ServiceName, "Excel").Run();
						}
						pageNumber++;
					}

					var filename = $"{processID}-{typeof(T).GetTypeName(true)}.xlsx";
					if (dataSet != null)
					{
						using (var stream = dataSet.SaveAsExcel())
						{
							await UtilityService.WriteBinaryFileAsync(Path.Combine(this.GetPath("Temp", Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data-files", "temp")), filename), stream, false, this.CancellationTokenSource.Token).ConfigureAwait(false);
						}
						onCompleted?.Invoke(dataSet);
					}

					await this.SendUpdateMessageAsync(new UpdateMessage
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
					}, this.CancellationTokenSource.Token).ConfigureAwait(false);

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
					await this.SendUpdateMessageAsync(new UpdateMessage
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
					}, this.CancellationTokenSource.Token).ConfigureAwait(false);
					await this.WriteLogsAsync(processID, $"Error occurred while exporting objects to Excel => {message}", ex, this.ServiceName, "Excel").ConfigureAwait(false);
				}
			}).ConfigureAwait(false);

		void Import<T>(string processID, string deviceID, string userID, string filename, string repositoryEntityID, Action<IEnumerable<T>> onCompleted = null) where T : class
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
						? await repositoryEntityID.GetContentTypeByIDAsync(this.CancellationTokenSource.Token).ConfigureAwait(false)
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
								bizObject.ID = string.IsNullOrWhiteSpace(bizObject.ID)
									? UtilityService.NewUUID
									: bizObject.ID;
								bizObject.LastModified = DateTime.Now;
								bizObject.LastModifiedID = userID;

								if (contentType != null)
								{
									bizObject.SystemID = string.IsNullOrWhiteSpace(bizObject.SystemID)
										? contentType.SystemID
										: bizObject.SystemID;
									bizObject.RepositoryID = string.IsNullOrWhiteSpace(bizObject.RepositoryID)
										? contentType.RepositoryID
										: bizObject.RepositoryID;
									bizObject.RepositoryEntityID = string.IsNullOrWhiteSpace(bizObject.RepositoryEntityID)
										? contentType.ID
										: bizObject.RepositoryEntityID;
								}
							}

							// update database
							var existed = await RepositoryMediator.GetAsync<T>(null, @object.GetEntityID(), this.CancellationTokenSource.Token).ConfigureAwait(false);
							if (existed != null)
							{
								@object.GetPublicAttributes().Where(attribute => @object.GetAttributeValue(attribute) == null).ForEach(attribute => @object.SetAttributeValue(attribute, existed.GetAttributeValue(attribute)));
								try
								{
									await RepositoryMediator.UpdateAsync(null, @object, false, userID, this.CancellationTokenSource.Token).ConfigureAwait(false);
								}
								catch (Exception ex)
								{
									if (ex is RepositoryOperationException && ex.InnerException != null && ex.InnerException is InformationExistedException && ex.InnerException.Message.IsContains("A key was existed") && aliasObject != null)
									{
										aliasObject.Alias = $"{aliasObject.Alias}-{aliasObject.ID}".NormalizeAlias();
										await RepositoryMediator.UpdateAsync(null, @object, false, userID, this.CancellationTokenSource.Token).ConfigureAwait(false);
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
									await RepositoryMediator.CreateAsync(null, @object, this.CancellationTokenSource.Token).ConfigureAwait(false);
								}
								catch (Exception ex)
								{
									if (ex is RepositoryOperationException && ex.InnerException != null && ex.InnerException is InformationExistedException && ex.InnerException.Message.IsContains("A key was existed") && aliasObject != null)
									{
										aliasObject.Alias = $"{aliasObject.Alias}-{aliasObject.ID}".NormalizeAlias();
										await RepositoryMediator.CreateAsync(null, @object, this.CancellationTokenSource.Token).ConfigureAwait(false);
									}
									else
										throw;
								}
							}

							// send update message
							objectName = objectName ?? (@object as RepositoryBase)?.GetObjectName();
							Utility.RTUService.SendUpdateMessageAsync(new UpdateMessage
							{
								Type = $"{ServiceBase.ServiceComponent.ServiceName}#{objectName}#Update",
								DeviceID = "*",
								Data = (@object as RepositoryBase)?.ToJson()
							}, this.CancellationToken).Run();

							// clear related cache
							if (@object is Category category)
								category.Set().ClearRelatedCacheAsync(this.CancellationToken, processID).Run();
							else if (@object is Content content)
								content.ClearRelatedCacheAsync(this.CancellationToken, processID).Run();
							else if (@object is Item item)
								item.ClearRelatedCacheAsync(this.CancellationToken, processID).Run();
							else if (@object is Link link)
								link.ClearRelatedCacheAsync(this.CancellationToken, processID).Run();
						}
						catch (Exception ex)
						{
							ex = ex is RepositoryOperationException ? ex.InnerException : ex;
							exceptions.Add(new RepositoryOperationException($"Error ({@event}) {@object.GetType()}#{@object.GetEntityID()}: [{@object.GetAttributeValue("Title")}] => {ex.GetTypeName(true)}: {ex.Message}", ex));
							this.WriteLogsAsync(processID, $"Error occurred while importing ({@event}) an object [{@object.GetType()}#{@object.GetEntityID()} => {@object.GetAttributeValue("Title")}] => {ex.GetTypeName(true)}: {ex.Message}", ex, this.ServiceName, "Excel").Run();
						}

						counter++;
						await this.SendUpdateMessageAsync(new UpdateMessage
						{
							Type = "Portals#Excel#Import",
							DeviceID = deviceID,
							Data = new JObject
							{
								{ "ProcessID", processID },
								{ "Status", "Processing" },
								{ "Percentage", $"{counter * 100/totalRecords:#0.0}%" }
							}
						}, this.CancellationTokenSource.Token).ConfigureAwait(false);
					}, true, false).ConfigureAwait(false);

					// final
					onCompleted?.Invoke(objects);
					await this.SendUpdateMessageAsync(new UpdateMessage
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
					}, this.CancellationTokenSource.Token).ConfigureAwait(false);

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
					await this.SendUpdateMessageAsync(new UpdateMessage
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
					}, this.CancellationTokenSource.Token).ConfigureAwait(false);
					await this.WriteLogsAsync(processID, $"Error occurred while importing objects from Excel => {message}", ex, this.ServiceName, "Excel").ConfigureAwait(false);
				}
			}).ConfigureAwait(false);
		#endregion

		#region Sync objects
		public override async Task<JToken> SyncAsync(RequestInfo requestInfo, CancellationToken cancellationToken = default)
		{
			var stopwatch = Stopwatch.StartNew();
			this.WriteLogs(requestInfo, $"Start sync ({requestInfo.Verb} {requestInfo.GetURI()})");
			using (var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, this.CancellationTokenSource.Token))
				try
				{
					// validate
					var json = await base.SyncAsync(requestInfo, cancellationToken).ConfigureAwait(false);

					// sync
					switch (requestInfo.ObjectName.ToLower())
					{
						case "organization":
						case "core.organization":
							json = await requestInfo.SyncOrganizationAsync(cts.Token).ConfigureAwait(false);
							break;

						case "role":
						case "core.role":
							json = await requestInfo.SyncRoleAsync(cts.Token).ConfigureAwait(false);
							break;

						case "module":
						case "core.module":
							json = await requestInfo.SyncModuleAsync(cts.Token).ConfigureAwait(false);
							break;

						case "contenttype":
						case "content.type":
						case "core.contenttype":
						case "core.content.type":
							json = await requestInfo.SyncContentTypeAsync(cts.Token).ConfigureAwait(false);
							break;

						case "expression":
						case "core.expression":
							json = await requestInfo.SyncExpressionAsync(cts.Token).ConfigureAwait(false);
							break;

						case "site":
						case "core.site":
							json = await requestInfo.SyncSiteAsync(cts.Token).ConfigureAwait(false);
							break;

						case "desktop":
						case "core.desktop":
							json = await requestInfo.SyncDesktopAsync(cts.Token).ConfigureAwait(false);
							break;

						case "portlet":
						case "core.portlet":
							json = await requestInfo.SyncPortletAsync(cts.Token).ConfigureAwait(false);
							break;

						case "category":
						case "cms.category":
							json = await requestInfo.SyncCategoryAsync(cts.Token).ConfigureAwait(false);
							break;

						case "content":
						case "cms.content":
							json = await requestInfo.SyncContentAsync(cts.Token).ConfigureAwait(false);
							break;

						case "item":
						case "cms.item":
							json = await requestInfo.SyncItemAsync(cts.Token).ConfigureAwait(false);
							break;

						case "link":
						case "cms.link":
							json = await requestInfo.SyncLinkAsync(cts.Token).ConfigureAwait(false);
							break;

						default:
							throw new InvalidRequestException($"The request for synchronizing is invalid ({requestInfo.Verb} {requestInfo.GetURI()})");
					}

					stopwatch.Stop();
					this.WriteLogs(requestInfo, $"Sync success - Execution times: {stopwatch.GetElapsedTimes()}");
					if (this.IsDebugResultsEnabled)
						this.WriteLogs(requestInfo, $"- Request: {requestInfo.ToString(this.JsonFormat)}" + "\r\n" + $"- Response: {json?.ToString(this.JsonFormat)}");
					return json;
				}
				catch (Exception ex)
				{
					throw this.GetRuntimeException(requestInfo, ex, stopwatch);
				}
		}

		protected override Task SendSyncRequestAsync(RequestInfo requestInfo, CancellationToken cancellationToken = default)
			=> base.SendSyncRequestAsync(requestInfo, cancellationToken);
		#endregion

		#region Process communicate message of Portals service
		protected override async Task ProcessInterCommunicateMessageAsync(CommunicateMessage message, CancellationToken cancellationToken = default)
		{
			// check
			if (message?.Type == null || message?.Data == null)
				return;

			var stopwatch = Stopwatch.StartNew();

			// messages of a refresh timer for an organization
			if (message.Type.IsStartsWith("RefreshTimer#"))
			{
				var organization = message.Data.ToExpandoObject().CreateOrganizationInstance();
				if (message.Type.IsEndsWith("#Start"))
					this.StartRefreshTimer(organization);
				else
					this.RestartRefreshTimer(organization, !message.Type.IsEndsWith("#Stop"));
			}

			// messages of an organization
			else if (message.Type.IsStartsWith("Organization#"))
				await message.ProcessInterCommunicateMessageOfOrganizationAsync(cancellationToken).ConfigureAwait(false);

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

			// messages a expression
			else if (message.Type.IsStartsWith("Expression#"))
				await message.ProcessInterCommunicateMessageOfExpressionAsync(cancellationToken).ConfigureAwait(false);

			// messages of a CMS category
			else if (message.Type.IsStartsWith("Category#") || message.Type.IsStartsWith("CMS.Category#"))
				await message.ProcessInterCommunicateMessageOfCategoryAsync(cancellationToken).ConfigureAwait(false);

			stopwatch.Stop();
			if (Utility.WriteMessageLogs)
				await Utility.WriteLogAsync(UtilityService.NewUUID, $"Process an inter-communicate message successful - Execution times: {stopwatch.GetElapsedTimes()}\r\n{message?.ToJson()}", cancellationToken, "Messages").ConfigureAwait(false);
		}
		#endregion

		#region Process communicate message of CMS Portals service
		async Task ProcessCommunicateMessageAsync(CommunicateMessage message, CancellationToken cancellationToken = default)
		{
			var correlationID = UtilityService.NewUUID;
			try
			{
				if (message.Type.IsEquals("Definition#RequestInfo"))
					await this.SendDefinitionInfoAsync(cancellationToken).ConfigureAwait(false);

				else if (message.Type.IsEquals("Definition#Info"))
				{
					var moduleDefinition = message.Data?.ToExpandoObject()?.Copy<ModuleDefinition>();
					if (this.IsDebugLogEnabled)
						await this.WriteLogsAsync(correlationID, $"Got an update of a module definition\r\n{message.Data}", null, this.ServiceName, "CMS.Portals").ConfigureAwait(false);
					this.UpdateDefinition(message.Data?.ToExpandoObject()?.Copy<ModuleDefinition>(), correlationID);
				}
			}
			catch (Exception ex)
			{
				await this.WriteLogsAsync(correlationID, $"Error occurred while processing an inter-communicate message => {ex.Message}", ex, this.ServiceName, "CMS.Portals").ConfigureAwait(false);
			}
		}

		Task SendDefinitionInfoAsync(CancellationToken cancellationToken = default)
			=> this.SendInterCommunicateMessageAsync(new CommunicateMessage("CMS.Portals")
			{
				Type = "Definition#Info",
				Data = this.GetDefinition().ToJson()
			}, cancellationToken);
		#endregion

		#region Caching (timers for refreshing desktop URLs & clear cache)
		ConcurrentDictionary<string, IDisposable> RefreshTimers { get; } = new ConcurrentDictionary<string, IDisposable>();

		void StartRefreshTimer(Organization organization, bool refreshHomeDesktops = true)
		{
			// check existing
			if (organization == null || this.RefreshTimers.ContainsKey(organization.ID))
				return;

			// refresh home desktops each 3 minutes
			if (refreshHomeDesktops)
			{
				var homeURLs = new[] { $"{Utility.PortalsHttpURI}/~{organization.Alias}" }.ToList();
				var sites = organization.Sites ?? new List<Site>();
				if (sites.Count > 1)
					homeURLs = homeURLs.Concat(sites.Select(site =>
					{
						var domain = $"{site.SubDomain}.{site.PrimaryDomain}".Replace("*.", "www.").Replace("www.www.", "www.");
						if (site.RedirectToNoneWWW && domain.IsStartsWith("www."))
							domain = domain.Right(domain.Length - 4);
						return (site.AlwaysUseHTTPs ? "https:" : "http:") + $"//{domain}";
					}))
					.Distinct(StringComparer.OrdinalIgnoreCase)
					.ToList();

				this.StartTimer(async () => await homeURLs.ForEachAsync(url => url.RefreshWebPageAsync(), true, Utility.RunProcessorInParallelsMode).ConfigureAwait(false), 3 * 60);
				if (this.IsDebugLogEnabled)
					this.WriteLogsAsync(UtilityService.NewUUID, $"The timer to refresh the home desktops of '{organization.Title}' [{organization.ID}] was started - Interval: 3 minutes\r\nURLs:\r\n\t{homeURLs.Join("\r\n\t")}", null, this.ServiceName, "Caches").Run();
			}

			// refresh the specified addresses
			if (organization.RefreshUrls != null)
			{
				var refreshUrls = organization.RefreshUrls.Addresses.Select(address =>
				{
					var urls = new List<string>();
					var addresses = address.Replace("~/", $"{Utility.PortalsHttpURI}/~{organization.Alias}/").Replace("\r", "").ToArray("\n");
					addresses.ForEach(url =>
					{
						if (url.IsContains("/{{pageNumber}}"))
							for (var page = 1; page <= 10; page++)
								urls.Add(url.Replace(StringComparison.OrdinalIgnoreCase, "/{{pageNumber}}", page > 1 ? $"/{page}" : ""));
						else
							urls.Add(url);
					});
					return urls;
				})
				.SelectMany(urls => urls)
				.Where(url => !string.IsNullOrWhiteSpace(url))
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.ToList();

				if (refreshUrls.Count > 0)
				{
					this.RefreshTimers[organization.ID] = this.StartTimer(async () => await refreshUrls.ForEachAsync(url => url.RefreshWebPageAsync(), true, Utility.RunProcessorInParallelsMode).ConfigureAwait(false), (organization.RefreshUrls.Interval > 0 ? organization.RefreshUrls.Interval : 7) * 60);
					if (this.IsDebugLogEnabled)
						this.WriteLogsAsync(UtilityService.NewUUID, $"The timer to the specified addresses of '{organization.Title}' [{organization.ID}] was started - Interval: {(organization.RefreshUrls.Interval > 0 ? organization.RefreshUrls.Interval : 7)} minutes\r\nURLs:\r\n\t{refreshUrls.Join("\r\n\t")}", null, this.ServiceName, "Caches").Run();
				}
			}
		}

		void RestartRefreshTimer(Organization organization, bool restart = true)
		{
			if (organization == null)
				return;

			if (this.RefreshTimers.TryRemove(organization.ID, out var timer))
			{
				this.StopTimer(timer);
				if (this.IsDebugLogEnabled)
					this.WriteLogsAsync(UtilityService.NewUUID, $"The timer to the specified addresses of '{organization.Title}' [{organization.ID}] was stopped", null, this.ServiceName, "Caches").Run();

				if (restart)
					this.StartRefreshTimer(organization, false);
			}
			else
				this.StartRefreshTimer(organization);
		}

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
			switch (requestInfo.GetObjectIdentity().ToLower())
			{
				case "organization":
					organization = await identity.GetOrganizationByIDAsync(cancellationToken).ConfigureAwait(false);
					gotRights = isSystemAdministrator || requestInfo.Session.User.IsAdministrator(null, null, organization, requestInfo.CorrelationID);
					break;

				case "module":
					module = await identity.GetModuleByIDAsync(cancellationToken).ConfigureAwait(false);
					gotRights = isSystemAdministrator || requestInfo.Session.User.IsAdministrator(module?.WorkingPrivileges, null, module?.Organization, requestInfo.CorrelationID);
					break;

				case "contenttype":
				case "content.type":
					contentType = await identity.GetContentTypeByIDAsync(cancellationToken).ConfigureAwait(false);
					gotRights = isSystemAdministrator || requestInfo.Session.User.IsAdministrator(contentType?.WorkingPrivileges, contentType?.Module?.WorkingPrivileges, contentType?.Organization, requestInfo.CorrelationID);
					break;

				case "site":
					site = await identity.GetSiteByIDAsync(cancellationToken).ConfigureAwait(false);
					gotRights = isSystemAdministrator || requestInfo.Session.User.IsAdministrator(null, null, site?.Organization, requestInfo.CorrelationID);
					break;

				case "desktop":
					desktop = await identity.GetDesktopByIDAsync(cancellationToken).ConfigureAwait(false);
					gotRights = isSystemAdministrator || requestInfo.Session.User.IsAdministrator(null, null, desktop?.Organization, requestInfo.CorrelationID);
					break;
			}

			if (!gotRights)
				throw new AccessDeniedException();

			// clear related cache
			var stopwatch = Stopwatch.StartNew();
			if (Utility.WriteCacheLogs)
				await Utility.WriteLogAsync(requestInfo.CorrelationID, $"Clear all cache{(organization != null ? " of the whole organization" : "")} [{requestInfo.GetURI()}]", cancellationToken, "Caches").ConfigureAwait(false);

			if (organization != null)
			{
				await organization.ClearCacheAsync(cancellationToken, requestInfo.CorrelationID, true, true, true, false).ConfigureAwait(false);
				var theme = organization.Theme ?? "defaut";
				await Task.WhenAll
				(
					Utility.Cache.RemoveAsync(await Utility.Cache.GetSetMembersAsync("Statics", this.CancellationToken).ConfigureAwait(false), this.CancellationToken),
					Utility.Cache.RemoveAsync(new[] { "css#defaut", "css#defaut:time", "js#defaut", "js#defaut:time", $"css#{theme}", $"css#{theme}:time", $"js#{theme}", $"js#{theme}:time" }.Distinct(StringComparer.OrdinalIgnoreCase), this.CancellationToken)
				).ConfigureAwait(false);
			}

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
				var siteTheme = site.Theme ?? site.Organization.Theme ?? "defaut";
				var organizationTheme = site.Organization.Theme ?? "defaut";
				await Task.WhenAll
				(
					Utility.Cache.RemoveAsync(await Utility.Cache.GetSetMembersAsync("Statics", this.CancellationToken).ConfigureAwait(false), this.CancellationToken),
					Utility.Cache.RemoveAsync(new[] { "css#defaut", "css#defaut:time", "js#defaut", "js#defaut:time", $"css#{siteTheme}", $"css#{siteTheme}:time", $"js#{siteTheme}", $"js#{siteTheme}:time", $"css#{organizationTheme}", $"css#{organizationTheme}:time", $"js#{organizationTheme}", $"js#{organizationTheme}:time" }.Distinct(StringComparer.OrdinalIgnoreCase), this.CancellationToken)
				).ConfigureAwait(false);
				site = await Site.GetAsync<Site>(site.ID, cancellationToken).ConfigureAwait(false);
				desktop = await Desktop.GetAsync<Desktop>(site.HomeDesktopID ?? site.Organization?.HomeDesktopID, cancellationToken).ConfigureAwait(false);
				await Task.WhenAll
				(
					desktop.FindChildrenAsync(cancellationToken, false),
					desktop.FindPortletsAsync(cancellationToken, false)
				).ConfigureAwait(false);
				await Task.WhenAll
				(
					site.SetAsync(false, true, cancellationToken),
					desktop.SetAsync(false, true, cancellationToken)
				).ConfigureAwait(false);
				await $"{Utility.PortalsHttpURI}/~{site.Organization?.Alias}/{desktop.Alias}".RefreshWebPageAsync(0, requestInfo.CorrelationID, $"Refresh home desktop when related cache of a site was clean [{site.Title} - ID: {site.ID}]");
			}

			else if (desktop != null)
			{
				await desktop.ClearCacheAsync(cancellationToken, requestInfo.CorrelationID, true, true, false).ConfigureAwait(false);
				desktop = await Desktop.GetAsync<Desktop>(desktop.ID, cancellationToken).ConfigureAwait(false);
				await Task.WhenAll
				(
					desktop.FindChildrenAsync(cancellationToken, false),
					desktop.FindPortletsAsync(cancellationToken, false)
				).ConfigureAwait(false);
				await desktop.SetAsync(false, true, cancellationToken).ConfigureAwait(false);
			}

			stopwatch.Stop();
			if (Utility.WriteCacheLogs)
				await Utility.WriteLogAsync(requestInfo.CorrelationID, $"Clear related cache successful - Execution times: {stopwatch.GetElapsedTimes()}", cancellationToken, "Caches").ConfigureAwait(false);

			return new JObject();
		}
		#endregion

		#region Approval
		async Task<JToken> ApproveAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			// prepare
			if (!requestInfo.Verb.IsEquals("GET"))
				throw new InvalidRequestException($"The request is invalid [({requestInfo.Verb}): {requestInfo.GetURI()}]");

			if (!Enum.TryParse<ApprovalStatus>(requestInfo.GetParameter("Status") ?? requestInfo.GetParameter("x-status"), out var approvalStatus))
				throw new InvalidRequestException($"The request is invalid [({requestInfo.Verb}): {requestInfo.GetURI()}]");

			var entityInfo = requestInfo.GetParameter("RepositoryEntityID") ?? requestInfo.GetParameter("x-entity");
			var @object = await this.GetBusinessObjectAsync(entityInfo, requestInfo.GetObjectIdentity(true), cancellationToken).ConfigureAwait(false) as IPortalObject;
			if (@object == null)
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
								: requestInfo.Session.User.IsEditor(@object.WorkingPrivileges, bizObject.ContentType?.WorkingPrivileges, bizObject.Organization as Organization, requestInfo.CorrelationID);
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
								? requestInfo.Session.User.IsEditor(@object.WorkingPrivileges, bizObject.ContentType?.WorkingPrivileges, bizObject.Organization as Organization, requestInfo.CorrelationID)
								: requestInfo.Session.User.IsModerator(@object.WorkingPrivileges, bizObject.ContentType?.WorkingPrivileges, bizObject.Organization as Organization, requestInfo.CorrelationID);
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
							gotRights = requestInfo.Session.User.IsModerator(@object.WorkingPrivileges, bizObject.ContentType?.WorkingPrivileges, bizObject.Organization as Organization, requestInfo.CorrelationID);
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

			return json;
		}
		#endregion

		#region Move
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
						await Utility.RTUService.SendUpdateMessageAsync(new UpdateMessage
						{
							Type = $"{requestInfo.ServiceName}#{objectName}#Update",
							Data = item.ToJson(),
							DeviceID = "*"
						}, cancellationToken).ConfigureAwait(false);
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
						await Utility.RTUService.SendUpdateMessageAsync(new UpdateMessage
						{
							Type = $"{requestInfo.ServiceName}#{objectName}#Update",
							Data = link.ToJson(),
							DeviceID = "*"
						}, cancellationToken).ConfigureAwait(false);
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
					await Utility.RTUService.SendUpdateMessageAsync(new UpdateMessage
					{
						Type = $"{requestInfo.ServiceName}#{objectName}#Update",
						Data = content.ToJson(),
						DeviceID = "*"
					}, cancellationToken).ConfigureAwait(false);
				}, true, false).ConfigureAwait(false);

				// clear related cache
				await cntType.ClearRelatedCacheAsync(cancellationToken, requestInfo.CorrelationID, true, false).ConfigureAwait(false);
			}

			return new JObject();
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

				this.WriteLogsAsync(correlationID, $"Start to refine thumbnail image of {totalRecords:###,###,###,##0} CMS contents", null, this.ServiceName, "Thumbnails").Run();
				while (pageNumber <= totalPages)
				{
					var objects = await Content.FindAsync(null, sort, pageSize, pageNumber, null, this.CancellationToken).ConfigureAwait(false);
					await objects.ForEachAsync(async @object => await this.SendInterCommunicateMessageAsync(new CommunicateMessage("Files")
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
					}, this.CancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
					pageNumber++;
				}
				stopwatch.Stop();
				this.WriteLogsAsync(correlationID, $"Complete to refine thumbnail image of {totalRecords:###,###,###,##0} CMS contents - Execution times: {stopwatch.GetElapsedTimes()}", null, this.ServiceName, "Thumbnails").Run();
			}
			catch (Exception ex)
			{
				await this.WriteLogsAsync(correlationID, $"Error occurred while refining thumbnail images => {ex.Message}", ex, this.ServiceName, "Thumbnails").ConfigureAwait(false);
			}
		}
		#endregion

	}
}
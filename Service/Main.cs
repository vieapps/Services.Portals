#region Related components
using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Dynamic;
using System.ComponentModel;
using System.Xml;
using System.Xml.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using WampSharp.V2.Core.Contracts;
using net.vieapps.Components.Utility;
using net.vieapps.Components.Security;
using net.vieapps.Components.Repository;
using net.vieapps.Services.Portals.Exceptions;
#endregion

namespace net.vieapps.Services.Portals
{
	public class ServiceComponent : ServiceBase, ICmsPortalsService
	{

		#region Definitions
		IAsyncDisposable ServiceInstance { get; set; }

		IDisposable ServiceCommunicator { get; set; }

		public override string ServiceName => "Portals";

		public ModuleDefinition GetDefinition()
			=> new ModuleDefinition(RepositoryMediator.GetEntityDefinition<Organization>().RepositoryDefinition);
		#endregion

		#region Start/Stop the service
		public override Task RegisterServiceAsync(IEnumerable<string> args, Action<IService> onSuccess = null, Action<Exception> onError = null)
			=> base.RegisterServiceAsync(
				args,
				async _ =>
				{
					onSuccess?.Invoke(this);
					this.ServiceInstance = await Router.IncomingChannel.RealmProxy.Services.RegisterCallee<ICmsPortalsService>(() => this, RegistrationInterceptor.Create(this.ServiceName)).ConfigureAwait(false);
					this.ServiceCommunicator?.Dispose();
					this.ServiceCommunicator = CmsPortalsServiceExtensions.RegisterServiceCommunicator(
						async message => await this.ProcessCommunicateMessageAsync(message).ConfigureAwait(false),
						exception => this.Logger?.LogError($"Error occurred while fetching an communicate message of CMS Portals => {exception.Message}", this.State == ServiceState.Connected ? exception : null)
					);
					this.Logger?.LogDebug($"Successfully{(this.State == ServiceState.Disconnected ? " re-" : " ")}register the service with CMS Portals");
				},
				onError
			);

		public override Task UnregisterServiceAsync(IEnumerable<string> args, bool available = true, Action<IService> onSuccess = null, Action<Exception> onError = null)
			=> base.UnregisterServiceAsync(
				args,
				available,
				async _ =>
				{
					onSuccess?.Invoke(this);
					if (this.ServiceInstance != null)
						try
						{
							await this.ServiceInstance.DisposeAsync().ConfigureAwait(false);
						}
						catch { }
					this.ServiceInstance = null;
					this.ServiceCommunicator?.Dispose();
					this.ServiceCommunicator = null;
					this.Logger?.LogDebug($"Successfully unregister the service with CMS Portals");
				},
				onError
			);

		public override Task StartAsync(string[] args = null, bool initializeRepository = true, Action<IService> next = null)
			=> base.StartAsync(args, initializeRepository, _ =>
			{
				Utility.APIsHttpURI = this.GetHttpURI("APIs", "https://apis.vieapps.net");
				while (Utility.APIsHttpURI.EndsWith("/"))
					Utility.APIsHttpURI = Utility.APIsHttpURI.Left(Utility.APIsHttpURI.Length - 1);

				Utility.FilesHttpURI = this.GetHttpURI("Files", "https://fs.vieapps.net");
				while (Utility.FilesHttpURI.EndsWith("/"))
					Utility.FilesHttpURI = Utility.FilesHttpURI.Left(Utility.FilesHttpURI.Length - 1);

				Utility.PortalsHttpURI = this.GetHttpURI("Portals", "https://portals.vieapps.net");
				while (Utility.PortalsHttpURI.EndsWith("/"))
					Utility.PortalsHttpURI = Utility.PortalsHttpURI.Left(Utility.PortalsHttpURI.Length - 1);

				Utility.PassportsHttpURI = this.GetHttpURI("Passports", "https://id.vieapps.net");
				while (Utility.PassportsHttpURI.EndsWith("/"))
					Utility.PassportsHttpURI = Utility.PassportsHttpURI.Left(Utility.PassportsHttpURI.Length - 1);

				Utility.DefaultSite = UtilityService.GetAppSetting("Portals:DefaultSiteID", "").GetSiteByID();
				Utility.DataFilesDirectory = UtilityService.GetAppSetting("Path:Portals");
				this.StartTimer(() => this.SendDefinitionInfoAsync(this.CancellationTokenSource.Token), 15 * 60);

				this.Logger?.LogDebug($"The default site: {(Utility.DefaultSite != null ? $"{Utility.DefaultSite.Title} [{Utility.DefaultSite.ID}]" : "None")}");
				this.Logger?.LogDebug($"Portals' files directory: {Utility.DataFilesDirectory ?? "None"}");

				Task.Run(async () =>
				{
					try
					{
						await Task.Delay(UtilityService.GetRandomNumber(678, 789), this.CancellationTokenSource.Token).ConfigureAwait(false);
						await this.SendInterCommunicateMessageAsync(new CommunicateMessage("CMS.Portals")
						{
							Type = "Definition#RequestInfo"
						}, this.CancellationTokenSource.Token).ConfigureAwait(false);
					}
					catch (Exception ex)
					{
						await this.WriteLogsAsync(UtilityService.NewUUID, $"Error occurred while sending a request for gathering module definitions => {ex.Message}", ex, this.ServiceName, "CMS", Microsoft.Extensions.Logging.LogLevel.Error).ConfigureAwait(false);
					}
				});
				next?.Invoke(this);
			});
		#endregion

		public override async Task<JToken> ProcessRequestAsync(RequestInfo requestInfo, CancellationToken cancellationToken = default)
		{
			var stopwatch = Stopwatch.StartNew();
			this.WriteLogs(requestInfo, $"Begin request ({requestInfo.Verb} {requestInfo.GetURI()})");
			try
			{
				JToken json = null;
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

					#region process request of Portls HTTP service
					case "identify.system":
						json = await this.IdentifySystemAsync(requestInfo, cancellationToken).ConfigureAwait(false);
						break;

					case "process.http.request":
						json = await this.ProcessHttpRequestAsync(requestInfo, cancellationToken).ConfigureAwait(false);
						break;
					#endregion

					#region process the request of definitions, files, profiles and all known others
					case "definitions":
						var mode = requestInfo.GetQueryParameter("mode");
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
								json = this.GenerateFormControls<Category>();
								break;

							case "content":
							case "cms.content":
								json = this.GenerateFormControls<Content>();
								break;

							case "link":
							case "cms.link":
								json = this.GenerateFormControls<Link>();
								break;

							case "item":
							case "cms.item":
								json = this.GenerateFormControls<Item>();
								break;

							case "contact":
							case "utils.contact":
								json = this.GenerateFormControls<Contact>();
								break;

							default:
								throw new InvalidRequestException($"The request is invalid [({requestInfo.Verb}): {requestInfo.GetURI()}]");
						}
						break;

					case "files":
					case "attachments":
						json = await this.ProcessAttachmentFileAsync(requestInfo, cancellationToken).ConfigureAwait(false);
						break;

					case "profile":
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

		#region Get themes
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
				await Directory.GetDirectories(Path.Combine(Utility.DataFilesDirectory, "themes")).ForEachAsync(async (directory, token) =>
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
							packageInfo = JObject.Parse(await UtilityService.ReadTextFileAsync(filename).ConfigureAwait(false));
						}
						catch { }
					themes.Add(packageInfo);
				}, cancellationToken, true, false).ConfigureAwait(false);
			return themes;
		}
		#endregion

		#region Process core portal objects
		async Task<JObject> ProcessOrganizationAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			var isSystemAdministrator = await this.IsSystemAdministratorAsync(requestInfo).ConfigureAwait(false) || await this.IsAuthorizedAsync(requestInfo, "Organization", Components.Security.Action.Approve, cancellationToken).ConfigureAwait(false);
			switch (requestInfo.Verb)
			{
				case "GET":
					return "search".IsEquals(requestInfo.GetObjectIdentity())
						? await requestInfo.SearchOrganizationsAsync(isSystemAdministrator, cancellationToken).ConfigureAwait(false)
						: await requestInfo.GetOrganizationAsync(isSystemAdministrator, cancellationToken).ConfigureAwait(false);

				case "POST":
					return await requestInfo.CreateOrganizationAsync(isSystemAdministrator, this.NodeID, this.RTUService, cancellationToken).ConfigureAwait(false);

				case "PUT":
					return await requestInfo.UpdateOrganizationAsync(isSystemAdministrator, this.NodeID, this.RTUService, cancellationToken).ConfigureAwait(false);

				case "DELETE":
					return await requestInfo.DeleteOrganizationAsync(isSystemAdministrator, this.NodeID, this.RTUService, cancellationToken).ConfigureAwait(false);

				default:
					throw new MethodNotAllowedException(requestInfo.Verb);
			}
		}

		async Task<JObject> ProcessSiteAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			var isSystemAdministrator = await this.IsSystemAdministratorAsync(requestInfo).ConfigureAwait(false) || await this.IsAuthorizedAsync(requestInfo, "Organization", Components.Security.Action.Approve, cancellationToken).ConfigureAwait(false);
			switch (requestInfo.Verb)
			{
				case "GET":
					return "search".IsEquals(requestInfo.GetObjectIdentity())
						? await requestInfo.SearchSitesAsync(isSystemAdministrator, cancellationToken).ConfigureAwait(false)
						: await requestInfo.GetSiteAsync(isSystemAdministrator, this.RTUService, cancellationToken).ConfigureAwait(false);

				case "POST":
					return await requestInfo.CreateSiteAsync(isSystemAdministrator, this.NodeID, this.RTUService, cancellationToken).ConfigureAwait(false);

				case "PUT":
					return await requestInfo.UpdateSiteAsync(isSystemAdministrator, this.NodeID, this.RTUService, cancellationToken).ConfigureAwait(false);

				case "DELETE":
					return await requestInfo.DeleteSiteAsync(isSystemAdministrator, this.NodeID, this.RTUService, cancellationToken).ConfigureAwait(false);

				default:
					throw new MethodNotAllowedException(requestInfo.Verb);
			}
		}

		async Task<JObject> ProcessRoleAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			var isSystemAdministrator = await this.IsSystemAdministratorAsync(requestInfo).ConfigureAwait(false) || await this.IsAuthorizedAsync(requestInfo, "Organization", Components.Security.Action.Approve, cancellationToken).ConfigureAwait(false);
			switch (requestInfo.Verb)
			{
				case "GET":
					return "search".IsEquals(requestInfo.GetObjectIdentity())
						? await requestInfo.SearchRolesAsync(isSystemAdministrator, cancellationToken).ConfigureAwait(false)
						: await requestInfo.GetRoleAsync(isSystemAdministrator, this.RTUService, cancellationToken).ConfigureAwait(false);

				case "POST":
					return await requestInfo.CreateRoleAsync(isSystemAdministrator, this.EncryptionKey, (request, token) => this.CallServiceAsync(request, token), (request, msg, ex) => this.WriteLogs(request, msg, ex, Microsoft.Extensions.Logging.LogLevel.Error), this.NodeID, this.RTUService, cancellationToken).ConfigureAwait(false);

				case "PUT":
					return await requestInfo.UpdateRoleAsync(isSystemAdministrator, this.EncryptionKey, (request, token) => this.CallServiceAsync(request, token), (request, msg, ex) => this.WriteLogs(request, msg, ex, Microsoft.Extensions.Logging.LogLevel.Error), this.NodeID, this.RTUService, cancellationToken).ConfigureAwait(false);

				case "DELETE":
					return await requestInfo.DeleteRoleAsync(isSystemAdministrator, this.EncryptionKey, (request, token) => this.CallServiceAsync(request, token), (request, msg, ex) => this.WriteLogs(request, msg, ex, Microsoft.Extensions.Logging.LogLevel.Error), this.NodeID, this.RTUService, cancellationToken).ConfigureAwait(false);

				default:
					throw new MethodNotAllowedException(requestInfo.Verb);
			}
		}

		async Task<JObject> ProcessDesktopAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			var isSystemAdministrator = await this.IsSystemAdministratorAsync(requestInfo).ConfigureAwait(false) || await this.IsAuthorizedAsync(requestInfo, "Organization", Components.Security.Action.Approve, cancellationToken).ConfigureAwait(false);
			switch (requestInfo.Verb)
			{
				case "GET":
					return "search".IsEquals(requestInfo.GetObjectIdentity())
						? await requestInfo.SearchDesktopsAsync(isSystemAdministrator, cancellationToken).ConfigureAwait(false)
						: await requestInfo.GetDesktopAsync(isSystemAdministrator, this.RTUService, cancellationToken).ConfigureAwait(false);

				case "POST":
					return await requestInfo.CreateDesktopAsync(isSystemAdministrator, this.NodeID, this.RTUService, cancellationToken).ConfigureAwait(false);

				case "PUT":
					return await requestInfo.UpdateDesktopAsync(isSystemAdministrator, this.NodeID, this.RTUService, cancellationToken).ConfigureAwait(false);

				case "DELETE":
					return await requestInfo.DeleteDesktopAsync(isSystemAdministrator, this.NodeID, this.RTUService, cancellationToken).ConfigureAwait(false);

				default:
					throw new MethodNotAllowedException(requestInfo.Verb);
			}
		}

		async Task<JObject> ProcessPortletAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			var isSystemAdministrator = await this.IsSystemAdministratorAsync(requestInfo).ConfigureAwait(false) || await this.IsAuthorizedAsync(requestInfo, "Organization", Components.Security.Action.Approve, cancellationToken).ConfigureAwait(false);
			switch (requestInfo.Verb)
			{
				case "GET":
					return "search".IsEquals(requestInfo.GetObjectIdentity())
						? await requestInfo.SearchPortletsAsync(isSystemAdministrator, cancellationToken).ConfigureAwait(false)
						: await requestInfo.GetPortletAsync(isSystemAdministrator, this.RTUService, cancellationToken).ConfigureAwait(false);

				case "POST":
					return await requestInfo.CreatePortletAsync(isSystemAdministrator, this.RTUService, cancellationToken).ConfigureAwait(false);

				case "PUT":
					return await requestInfo.UpdatePortletAsync(isSystemAdministrator, this.RTUService, cancellationToken).ConfigureAwait(false);

				case "DELETE":
					return await requestInfo.DeletePortletAsync(isSystemAdministrator, this.RTUService, cancellationToken).ConfigureAwait(false);

				default:
					throw new MethodNotAllowedException(requestInfo.Verb);
			}
		}

		async Task<JObject> ProcessModuleAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			var isSystemAdministrator = await this.IsSystemAdministratorAsync(requestInfo).ConfigureAwait(false) || await this.IsAuthorizedAsync(requestInfo, "Organization", Components.Security.Action.Approve, cancellationToken).ConfigureAwait(false);
			switch (requestInfo.Verb)
			{
				case "GET":
					return "search".IsEquals(requestInfo.GetObjectIdentity())
						? await requestInfo.SearchModulesAsync(isSystemAdministrator, cancellationToken).ConfigureAwait(false)
						: await requestInfo.GetModuleAsync(isSystemAdministrator, this.RTUService, cancellationToken).ConfigureAwait(false);

				case "POST":
					return await requestInfo.CreateModuleAsync(isSystemAdministrator, this.NodeID, this.RTUService, cancellationToken).ConfigureAwait(false);

				case "PUT":
					return await requestInfo.UpdateModuleAsync(isSystemAdministrator, this.NodeID, this.RTUService, cancellationToken).ConfigureAwait(false);

				case "DELETE":
					return await requestInfo.DeleteModuleAsync(isSystemAdministrator, this.NodeID, this.RTUService, cancellationToken).ConfigureAwait(false);

				default:
					throw new MethodNotAllowedException(requestInfo.Verb);
			}
		}

		async Task<JObject> ProcessContentTypeAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			var isSystemAdministrator = await this.IsSystemAdministratorAsync(requestInfo).ConfigureAwait(false) || await this.IsAuthorizedAsync(requestInfo, "Organization", Components.Security.Action.Approve, cancellationToken).ConfigureAwait(false);
			switch (requestInfo.Verb)
			{
				case "GET":
					return "search".IsEquals(requestInfo.GetObjectIdentity())
						? await requestInfo.SearchContentTypesAsync(isSystemAdministrator, cancellationToken).ConfigureAwait(false)
						: await requestInfo.GetContentTypeAsync(isSystemAdministrator, this.RTUService, cancellationToken).ConfigureAwait(false);

				case "POST":
					return await requestInfo.CreateContentTypeAsync(isSystemAdministrator, this.NodeID, this.RTUService, cancellationToken).ConfigureAwait(false);

				case "PUT":
					return await requestInfo.UpdateContentTypeAsync(isSystemAdministrator, this.NodeID, this.RTUService, cancellationToken).ConfigureAwait(false);

				case "DELETE":
					return await requestInfo.DeleteContentTypeAsync(isSystemAdministrator, this.NodeID, this.RTUService, cancellationToken).ConfigureAwait(false);

				default:
					throw new MethodNotAllowedException(requestInfo.Verb);
			}
		}

		async Task<JObject> ProcessExpressionAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			var isSystemAdministrator = await this.IsSystemAdministratorAsync(requestInfo).ConfigureAwait(false) || await this.IsAuthorizedAsync(requestInfo, "Organization", Components.Security.Action.Approve, cancellationToken).ConfigureAwait(false);
			switch (requestInfo.Verb)
			{
				case "GET":
					return "search".IsEquals(requestInfo.GetObjectIdentity())
						? await requestInfo.SearchExpressionsAsync(isSystemAdministrator, cancellationToken).ConfigureAwait(false)
						: await requestInfo.GetExpressionAsync(isSystemAdministrator, this.RTUService, cancellationToken).ConfigureAwait(false);

				case "POST":
					return await requestInfo.CreateExpressionAsync(isSystemAdministrator, this.NodeID, this.RTUService, cancellationToken).ConfigureAwait(false);

				case "PUT":
					return await requestInfo.UpdateExpressionAsync(isSystemAdministrator, this.NodeID, this.RTUService, cancellationToken).ConfigureAwait(false);

				case "DELETE":
					return await requestInfo.DeleteExpressionAsync(isSystemAdministrator, this.NodeID, this.RTUService, cancellationToken).ConfigureAwait(false);

				default:
					throw new MethodNotAllowedException(requestInfo.Verb);
			}
		}
		#endregion

		#region Process CMS object
		async Task<JObject> ProcessCategoryAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			var isSystemAdministrator = await this.IsSystemAdministratorAsync(requestInfo).ConfigureAwait(false) || await this.IsAuthorizedAsync(requestInfo, "Organization", Components.Security.Action.Approve, cancellationToken).ConfigureAwait(false);
			switch (requestInfo.Verb)
			{
				case "GET":
					return "search".IsEquals(requestInfo.GetObjectIdentity())
						? await requestInfo.SearchCategorysAsync(isSystemAdministrator, cancellationToken).ConfigureAwait(false)
						: await requestInfo.GetCategoryAsync(isSystemAdministrator, this.RTUService, cancellationToken).ConfigureAwait(false);

				case "POST":
					return await requestInfo.CreateCategoryAsync(isSystemAdministrator, this.NodeID, this.RTUService, cancellationToken).ConfigureAwait(false);

				case "PUT":
					return await requestInfo.UpdateCategoryAsync(isSystemAdministrator, this.NodeID, this.RTUService, cancellationToken).ConfigureAwait(false);

				case "DELETE":
					return await requestInfo.DeleteCategoryAsync(isSystemAdministrator, this.NodeID, this.RTUService, cancellationToken, this.ValidationKey).ConfigureAwait(false);

				default:
					throw new MethodNotAllowedException(requestInfo.Verb);
			}
		}

		async Task<JObject> ProcessContentAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			var isSystemAdministrator = await this.IsSystemAdministratorAsync(requestInfo).ConfigureAwait(false) || await this.IsAuthorizedAsync(requestInfo, "Organization", Components.Security.Action.Approve, cancellationToken).ConfigureAwait(false);
			switch (requestInfo.Verb)
			{
				case "GET":
					return "search".IsEquals(requestInfo.GetObjectIdentity())
						? await requestInfo.SearchContentsAsync(isSystemAdministrator, this.ValidationKey, cancellationToken).ConfigureAwait(false)
						: await requestInfo.GetContentAsync(isSystemAdministrator, this.RTUService, this.ValidationKey, cancellationToken).ConfigureAwait(false);

				case "POST":
					return await requestInfo.CreateContentAsync(isSystemAdministrator, this.RTUService, this.ValidationKey, cancellationToken).ConfigureAwait(false);

				case "PUT":
					return await requestInfo.UpdateContentAsync(isSystemAdministrator, this.RTUService, this.ValidationKey, cancellationToken).ConfigureAwait(false);

				case "DELETE":
					return await requestInfo.DeleteContentAsync(isSystemAdministrator, this.RTUService, this.ValidationKey, cancellationToken).ConfigureAwait(false);

				default:
					throw new MethodNotAllowedException(requestInfo.Verb);
			}
		}

		async Task<JObject> ProcessItemAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			var isSystemAdministrator = await this.IsSystemAdministratorAsync(requestInfo).ConfigureAwait(false) || await this.IsAuthorizedAsync(requestInfo, "Organization", Components.Security.Action.Approve, cancellationToken).ConfigureAwait(false);
			switch (requestInfo.Verb)
			{
				case "GET":
					return "search".IsEquals(requestInfo.GetObjectIdentity())
						? await requestInfo.SearchItemsAsync(isSystemAdministrator, this.ValidationKey, cancellationToken).ConfigureAwait(false)
						: await requestInfo.GetItemAsync(isSystemAdministrator, this.RTUService, this.ValidationKey, cancellationToken).ConfigureAwait(false);

				case "POST":
					return await requestInfo.CreateItemAsync(isSystemAdministrator, this.RTUService, this.ValidationKey, cancellationToken).ConfigureAwait(false);

				case "PUT":
					return await requestInfo.UpdateItemAsync(isSystemAdministrator, this.RTUService, this.ValidationKey, cancellationToken).ConfigureAwait(false);

				case "DELETE":
					return await requestInfo.DeleteItemAsync(isSystemAdministrator, this.RTUService, this.ValidationKey, cancellationToken).ConfigureAwait(false);

				default:
					throw new MethodNotAllowedException(requestInfo.Verb);
			}
		}

		async Task<JObject> ProcessLinkAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			var isSystemAdministrator = await this.IsSystemAdministratorAsync(requestInfo).ConfigureAwait(false) || await this.IsAuthorizedAsync(requestInfo, "Organization", Components.Security.Action.Approve, cancellationToken).ConfigureAwait(false);
			switch (requestInfo.Verb)
			{
				case "GET":
					return "search".IsEquals(requestInfo.GetObjectIdentity())
						? await requestInfo.SearchLinksAsync(isSystemAdministrator, cancellationToken).ConfigureAwait(false)
						: await requestInfo.GetLinkAsync(isSystemAdministrator, this.RTUService, cancellationToken).ConfigureAwait(false);

				case "POST":
					return await requestInfo.CreateLinkAsync(isSystemAdministrator, this.NodeID, this.RTUService, cancellationToken).ConfigureAwait(false);

				case "PUT":
					return await requestInfo.UpdateLinkAsync(isSystemAdministrator, this.NodeID, this.RTUService, cancellationToken).ConfigureAwait(false);

				case "DELETE":
					return await requestInfo.DeleteLinkAsync(isSystemAdministrator, this.NodeID, this.RTUService, cancellationToken).ConfigureAwait(false);

				default:
					throw new MethodNotAllowedException(requestInfo.Verb);
			}
		}
		#endregion

		async Task<JToken> ProcessAttachmentFileAsync(RequestInfo requestInfo, CancellationToken cancellationToken)
		{
			var systemID = requestInfo.GetParameter("SystemID") ?? requestInfo.GetParameter("x-system-id");
			var entityInfo = requestInfo.GetParameter("RepositoryEntityID") ?? requestInfo.GetParameter("x-entity");
			var objectID = requestInfo.GetParameter("ObjectID") ?? requestInfo.GetParameter("x-object-id");
			var objectTitle = requestInfo.GetParameter("ObjectTitle") ?? requestInfo.GetParameter("x-object-title");

			if (requestInfo.Verb.IsEquals("PATCH"))
				return await this.MarkFilesAsOfficialAsync(requestInfo, systemID, entityInfo, objectID, objectTitle, cancellationToken).ConfigureAwait(false);

			else if (requestInfo.Verb.IsEquals("GET"))
				switch ((requestInfo.GetObjectIdentity() ?? "").ToLower())
				{
					case "thumbnail":
					case "thumbnails":
						return await this.GetThumbnailsAsync(requestInfo, objectID, objectTitle, cancellationToken).ConfigureAwait(false);

					case "attachment":
					case "attachments":
						return await this.GetAttachmentsAsync(requestInfo, objectID, objectTitle, cancellationToken).ConfigureAwait(false);

					default:
						return await this.GetFilesAsync(requestInfo, objectID, objectTitle, cancellationToken).ConfigureAwait(false);
				}
			else
				throw new MethodNotAllowedException(requestInfo.Verb);
		}

		async Task<JToken> ProcessTemplateAsync(RequestInfo requestInfo, CancellationToken cancellationToken = default)
		{
			var request = requestInfo.GetRequestExpando();
			if ("Zones".IsEquals(request.Get<string>("Mode")))
			{
				var desktop = await request.Get("DesktopID", "").GetDesktopByIDAsync(cancellationToken).ConfigureAwait(false);
				return desktop != null
					? (await desktop.GetTemplateAsync(cancellationToken).ConfigureAwait(false)).GetXDocument().GetZoneNames().ToJArray()
					: null;
			}
			return new JObject
			{
				{ "Template", await Utility.GetTemplateAsync(request.Get<string>("Name"), null, request.Get<string>("MainDirectory"), request.Get<string>("SubDirectory"), cancellationToken).ConfigureAwait(false) }
			};
		}

		async Task<JToken> IdentifySystemAsync(RequestInfo requestInfo, CancellationToken cancellationToken = default)
		{
			var site = requestInfo.Header.TryGetValue("Host", out var host) && !string.IsNullOrWhiteSpace(host)
				? await host.GetSiteByDomainAsync(null, cancellationToken).ConfigureAwait(false)
				: Utility.DefaultSite;

			if (site == null || site.Organization == null)
				throw new InformationNotFoundException($"The requested site is not recognized ({(string.IsNullOrWhiteSpace(host) ? "unknown" : host)})");

			return new JObject
			{
				{ "ID", site.Organization.Alias }
			};
		}

		Task<JToken> ProcessHttpRequestAsync(RequestInfo requestInfo, CancellationToken cancellationToken = default)
			=> requestInfo.Query.ContainsKey("x-indicator")
				? this.ProcessHttpIndicatorsRequestAsync(requestInfo, cancellationToken)
				: requestInfo.Query.ContainsKey("x-resource")
					? this.ProcessHttpResourcesRequestAsync(requestInfo, cancellationToken)
					: this.ProcessHttpDesktopsRequestAsync(requestInfo, cancellationToken);

		#region Process requests of Portals HTTP service
		Task<JToken> ProcessHttpIndicatorsRequestAsync(RequestInfo requestInfo, CancellationToken cancellationToken = default)
		{
			throw new InvalidRequestException($"The request is invalid [({requestInfo.Verb}): {requestInfo.GetURI()}]");
		}

		async Task<JToken> ProcessHttpResourcesRequestAsync(RequestInfo requestInfo, CancellationToken cancellationToken = default)
		{
			var resourceType = requestInfo.Query["x-resource"];

			// static files in 'assets' directory or image files of a theme
			if (resourceType.IsEquals("assets") || resourceType.IsEquals("images"))
			{
				// prepare
				if (!requestInfo.Query.TryGetValue("x-path", out var filePath) || string.IsNullOrWhiteSpace(filePath))
					throw new InformationNotFoundException();
				var fileInfo = new FileInfo(Path.Combine(Utility.DataFilesDirectory, resourceType.IsEquals("images") ? "themes" : "assets", filePath));
				if (!fileInfo.Exists)
					throw new InformationNotFoundException(filePath);

				var eTag = $"{resourceType.ToLower()}#{filePath.ToLower().GenerateUUID()}";
				var headers = new Dictionary<string, string>
				{
					{ "Content-Type", $"{fileInfo.GetMimeType()}; charset=utf-8" },
					{ "ETag", eTag },
					{ "Last-Modified", fileInfo.LastWriteTime.ToHttpString() },
					{ "Cache-Control", "public" },
					{ "X-Correlation-ID", requestInfo.CorrelationID }
				};

				// response
				var ifModifiedSince = requestInfo.GetParameter("If-Modified-Since");
				return eTag.IsEquals(requestInfo.GetParameter("If-None-Match")) && ifModifiedSince != null && ifModifiedSince.FromHttpDateTime() >= fileInfo.LastWriteTime      // check "If-Modified-Since" request to reduce traffict
					? new JObject
					{
						{ "StatusCode", 304 },
						{ "Headers", headers.ToJson() }
					}
					: new JObject
					{
						{ "StatusCode", 200 },
						{ "Headers", headers.ToJson() },
						{ "Body", (await UtilityService.ReadBinaryFileAsync(fileInfo, cancellationToken).ConfigureAwait(false)).ToBase64() }
					};
			}

			// css of a theme
			if (resourceType.IsEquals("css"))
			{
				// prepare
				if (!requestInfo.Query.TryGetValue("x-path", out var theme) || string.IsNullOrWhiteSpace(theme))
					throw new InvalidRequestException($"The request is invalid [({requestInfo.Verb}): {requestInfo.GetURI()}]");

				theme = theme.Replace(".css", "").ToLower().Trim();
				var directory = new DirectoryInfo(Path.Combine(Utility.DataFilesDirectory, "themes", theme, "css"));
				if (!directory.Exists)
					throw new InformationNotFoundException(theme);

				var body = this.IsDebugLogEnabled ? $"/* css files of '{theme}' theme */\n\n" : "";
				var lastModified = DateTimeService.CheckingDateTime;
				var files = directory.GetFiles("*.css");
				if (files.Length < 1)
					lastModified = directory.LastWriteTime;
				else
					await files.ForEachAsync(async (fileInfo, token) =>
					{
						if (this.IsDebugLogEnabled)
							body += $"/* {fileInfo.FullName} */\n\n";
						body += await UtilityService.ReadTextFileAsync(fileInfo, null, cancellationToken).ConfigureAwait(false) + "\n";
						if (fileInfo.LastWriteTime > lastModified)
							lastModified = fileInfo.LastWriteTime;
					}, cancellationToken).ConfigureAwait(false);

				var eTag = $"css#{theme.GenerateUUID()}";
				var headers = new Dictionary<string, string>
				{
					{ "Content-Type", "text/css; charset=utf-8" },
					{ "ETag", eTag },
					{ "Last-Modified", lastModified.ToHttpString() },
					{ "Cache-Control", "public" },
					{ "X-Correlation-ID", requestInfo.CorrelationID }
				};

				// response
				var ifModifiedSince = requestInfo.GetParameter("If-Modified-Since");
				return eTag.IsEquals(requestInfo.GetParameter("If-None-Match")) && ifModifiedSince != null && ifModifiedSince.FromHttpDateTime() >= lastModified      // check "If-Modified-Since" request to reduce traffict
					? new JObject
					{
						{ "StatusCode", 304 },
						{ "Headers", headers.ToJson() }
					}
					: new JObject
					{
						{ "StatusCode", 200 },
						{ "Headers", headers.ToJson() },
						{ "Body", body.ToBytes().ToBase64() }
					};
			}

			// script  of a theme
			if (resourceType.IsEquals("js") || resourceType.IsEquals("javascript") || resourceType.IsEquals("script") || resourceType.IsEquals("script"))
			{
				// prepare
				if (!requestInfo.Query.TryGetValue("x-path", out var theme) || string.IsNullOrWhiteSpace(theme))
					throw new InvalidRequestException($"The request is invalid [({requestInfo.Verb}): {requestInfo.GetURI()}]");

				theme = theme.Replace(".js", "").ToLower().Trim();
				var directory = new DirectoryInfo(Path.Combine(Utility.DataFilesDirectory, "themes", theme, "js"));
				if (!directory.Exists)
					throw new InformationNotFoundException(theme);

				var body = this.IsDebugLogEnabled ? $"/* script files of '{theme}' theme */\n\n" : "";
				var lastModified = DateTimeService.CheckingDateTime;
				var files = directory.GetFiles("*.js");
				if (files.Length < 1)
					lastModified = directory.LastWriteTime;
				else
					await files.ForEachAsync(async (fileInfo, token) =>
					{
						if (this.IsDebugLogEnabled)
							body += $"/* {fileInfo.FullName} */\n\n";
						body += await UtilityService.ReadTextFileAsync(fileInfo, null, cancellationToken).ConfigureAwait(false) + "\n";
						if (fileInfo.LastWriteTime > lastModified)
							lastModified = fileInfo.LastWriteTime;
					}, cancellationToken).ConfigureAwait(false);

				var eTag = $"script#{theme.GenerateUUID()}";
				var headers = new Dictionary<string, string>
				{
					{ "Content-Type", "application/javascript; charset=utf-8" },
					{ "ETag", eTag },
					{ "Last-Modified", lastModified.ToHttpString() },
					{ "Cache-Control", "public" },
					{ "X-Correlation-ID", requestInfo.CorrelationID }
				};

				// response
				var ifModifiedSince = requestInfo.GetParameter("If-Modified-Since");
				return eTag.IsEquals(requestInfo.GetParameter("If-None-Match")) && ifModifiedSince != null && ifModifiedSince.FromHttpDateTime() >= lastModified      // check "If-Modified-Since" request to reduce traffict
					? new JObject
					{
						{ "StatusCode", 304 },
						{ "Headers", headers.ToJson() }
					}
					: new JObject
					{
						{ "StatusCode", 200 },
						{ "Headers", headers.ToJson() },
						{ "Body", body.ToBytes().ToBase64() }
					};
			}

			// unknown
			throw new InvalidRequestException($"The request is invalid [({requestInfo.Verb}): {requestInfo.GetURI()}]");
		}

		async Task<JToken> ProcessHttpDesktopsRequestAsync(RequestInfo requestInfo, CancellationToken cancellationToken = default)
		{
			// prepare required information
			var organization = await (requestInfo.GetParameter("x-system") ?? "").GetOrganizationByAliasAsync(cancellationToken).ConfigureAwait(false);
			if (organization == null)
				throw new InvalidRequestException($"The request is invalid [({requestInfo.Verb}): {requestInfo.GetURI()}]");

			if (SiteProcessor.Sites.Count < 1)
			{
				var filter = Filters<Site>.And();
				var sort = Sorts<Site>.Ascending("Title");
				var sites = await Site.FindAsync(filter, sort, 0, 1, Extensions.GetCacheKey(filter, sort, 0, 1), cancellationToken).ConfigureAwait(false);
				await sites.ForEachAsync(async (website, token) => await website.SetAsync(false, true, token), cancellationToken).ConfigureAwait(false);
			}

			if (DesktopProcessor.Desktops.Count < 1 || DesktopProcessor.Desktops.Count(kvp => kvp.Value.SystemID == organization.ID) < 1)
			{
				var filter = Filters<Desktop>.Equals("SystemID", organization.ID);
				var sort = Sorts<Desktop>.Ascending("Title");
				var desktops = await Desktop.FindAsync(filter, sort, 0, 1, Extensions.GetCacheKey(filter, sort, 0, 1), cancellationToken).ConfigureAwait(false);
				await desktops.ForEachAsync(async (des, token) => await des.SetAsync(false, true, token), cancellationToken).ConfigureAwait(false);
			}

			var site = await (requestInfo.GetParameter("Host") ?? "").GetSiteByDomainAsync(null, cancellationToken).ConfigureAwait(false) ?? organization.DefaultSite;
			if (site == null)
				throw new InvalidRequestException($"The request is invalid [({requestInfo.Verb}): {requestInfo.GetURI()}]");

			var desktop = "-default".IsEquals(requestInfo.GetParameter("x-desktop"))
				? organization.DefaultDesktop
				: await organization.ID.GetDesktopByAliasAsync(requestInfo.GetParameter("x-desktop"), cancellationToken).ConfigureAwait(false);
			if (desktop == null)
				throw new InvalidRequestException($"The request is invalid [({requestInfo.Verb}): {requestInfo.GetURI()}]");

			// prepare the key for working with caching
			var key = requestInfo.Query.Select(kvp => $"{kvp.Key}={kvp.Value}").Join("&").GenerateUUID();
			var htmlCacheKey = $"Desktop:Html:{key}";
			var timeCacheKey = $"Desktop:Time:{key}";
			var headers = new Dictionary<string, string>
			{
				{ "Content-Type", "text/html; charset=utf-8" },
				{ "X-Correlation-ID", requestInfo.CorrelationID }
			};

			// check "If-Modified-Since" request to reduce traffict
			var eTag = $"desktop#{key}";
			var lastModified = "";
			var ifModifiedSince = requestInfo.GetParameter("If-Modified-Since");
			if (eTag.IsEquals(requestInfo.GetParameter("If-None-Match")) && ifModifiedSince != null)
			{
				lastModified = await Utility.Cache.GetAsync<string>(timeCacheKey, cancellationToken).ConfigureAwait(false);
				if (!string.IsNullOrWhiteSpace(lastModified) && ifModifiedSince.FromHttpDateTime() >= lastModified.FromHttpDateTime())
				{
					headers = new Dictionary<string, string>(headers)
					{
						{ "ETag", eTag },
						{ "Last-Modified", lastModified },
						{ "Cache-Control", "public" }
					};
					return new JObject
					{
						{ "StatusCode", 304 },
						{ "Headers", headers.ToJson() }
					};
				}
			}

			// response as cached HTML
			var html = this.IsDebugLogEnabled ? null : await Utility.Cache.GetAsync<string>(htmlCacheKey, cancellationToken).ConfigureAwait(false);
			if (!string.IsNullOrWhiteSpace(html))
			{
				lastModified = string.IsNullOrWhiteSpace(lastModified) ? await Utility.Cache.GetAsync<string>(timeCacheKey, cancellationToken).ConfigureAwait(false) : lastModified;
				if (string.IsNullOrWhiteSpace(lastModified))
					lastModified = DateTime.Now.ToHttpString();
				headers = new Dictionary<string, string>(headers)
				{
					{ "ETag", eTag },
					{ "Last-Modified", lastModified },
					{ "Cache-Control", "public" }
				};
				return new JObject
				{
					{ "StatusCode", 200 },
					{ "Headers", headers.ToJson() },
					{ "Body", html.ToBytes().ToBase64() }
				};
			}

			// prepare to generate portlets
			if (desktop._portlets == null)
				await desktop.FindPortletsAsync(cancellationToken).ConfigureAwait(false);

			// prepare required information
			var queryJson = requestInfo.Query.Where(kvp => !kvp.Key.IsEquals("x-system") && !kvp.Key.IsEquals("x-desktop") && !kvp.Key.IsEquals("x-parent") && !kvp.Key.IsEquals("x-content") && !kvp.Key.IsEquals("x-page") && !kvp.Key.IsEquals("x-step")).ToDictionary(kvp => kvp.Key, kvp => kvp.Value).ToJson();

			var organizationJson = organization.ToJson(false, false, json =>
			{
				json.Remove("Privileges");
				OrganizationProcessor.ExtraProperties.ForEach(name => json.Remove(name));
				json["DefaultDesktop"] = organization.DefaultDesktop?.Alias;
			});

			var siteJson = site.ToJson(false, json =>
			{
				json.Remove("Privileges");
				SiteProcessor.ExtraProperties.ForEach(name => json.Remove(name));
			});

			// write logs
			var writeSteps = requestInfo.GetParameter("x-step") != null;
			if (writeSteps)
				await this.WriteLogsAsync(requestInfo.CorrelationID, $"Start to generate portlets' data of '{desktop.Alias}' [ID: {desktop.ID}] desktop", null, this.ServiceName, "Steps").ConfigureAwait(false);

			// generate data of portlets
			var gotError = false;
			var portletJSONs = new ConcurrentDictionary<string, JObject>(StringComparer.OrdinalIgnoreCase);
			await desktop.Portlets.ForEachAsync(async (theportlet, token) =>
			{
				// get original portlet
				var portlet = theportlet.OriginalPortlet;
				if (writeSteps)
					await this.WriteLogsAsync(requestInfo.CorrelationID, $"Start to generate portlet data of '{theportlet.Title}' [ID: {theportlet.ID}] {(string.IsNullOrWhiteSpace(theportlet.OriginalPortletID) ? $" (alias of '{portlet.Title}' [ID: {portlet.ID}])" : "")}", null, this.ServiceName, "Steps").ConfigureAwait(false);

				// get content-type
				var contentType = await (portlet.RepositoryEntityID ?? "").GetContentTypeByIDAsync(token).ConfigureAwait(false);
				var parentContentType = contentType?.GetParent();

				// no content-type => then by-pass on static porlet
				if (contentType == null)
				{
					if (writeSteps)
						await this.WriteLogsAsync(requestInfo.CorrelationID, $"By-pass static content portlet", null, this.ServiceName, "Steps").ConfigureAwait(false);
					return;
				}

				// prepare action
				var action = requestInfo.GetQueryParameter("x-parent") != null && requestInfo.GetQueryParameter("x-content") != null
					? portlet.AlternativeAction
					: portlet.Action;

				// prepare expression (for filtering and sorting)
				var generateList = !"View".IsEquals(action);
				var expresion = generateList && !string.IsNullOrWhiteSpace(portlet.ListSettings.ExpressionID)
					? await portlet.ListSettings.ExpressionID.GetExpressionByIDAsync(token).ConfigureAwait(false)
					: null;

				// prepare the JSON that contains the requesting information for generating data
				var requestJson = new JObject
				{
					{ "ID", theportlet.ID },
					{ "Action", action },
					{ "ParentIdentity", requestInfo.GetQueryParameter("x-parent") },
					{ "ContentIdentity", requestInfo.GetQueryParameter("x-content") },
					{ "PageSize", generateList ? portlet.ListSettings.PageSize : 0 },
					{ "PageNumber", generateList ? portlet.ListSettings.AutoPageNumber ? (requestInfo.GetQueryParameter("x-page") ?? "1").CastAs<int>() : 1 : (requestInfo.GetQueryParameter("x-page") ?? "1").CastAs<int>() },
					{ "Options", generateList ? JObject.Parse(portlet.ListSettings.Options ?? "{}") : JObject.Parse(portlet.ViewSettings.Options ?? "{}") },
					{ "Desktop", desktop.Alias },
					{ "Site", siteJson },
					{ "ContentTypeDefinition", contentType.ContentTypeDefinition?.ToJson() },
					{ "ModuleDefinition", contentType.ContentTypeDefinition?.ModuleDefinition?.ToJson(json =>
						{
							(json as JObject).Remove("ContentTypeDefinitions");
							(json as JObject).Remove("ObjectDefinitions");
						})
					},
					{ "Organization", organizationJson },
					{ "Module", contentType.Module?.ToJson(false, json =>
						{
							json.Remove("Privileges");
							ModuleProcessor.ExtraProperties.ForEach(name => json.Remove(name));
							json["Desktop"] = contentType.Module?.Desktop?.Alias;
						})
					},
					{ "ContentType", contentType.ToJson(false, json =>
						{
							json.Remove("Privileges");
							ModuleProcessor.ExtraProperties.ForEach(name => json.Remove(name));
							json["Desktop"] = contentType.Desktop?.Alias;
						})
					},
					{ "ParentContentType", parentContentType?.ToJson(false, json =>
						{
							json.Remove("Privileges");
							ModuleProcessor.ExtraProperties.ForEach(name => json.Remove(name));
							json["Desktop"] = parentContentType.Desktop?.Alias;
						})
					}
				};

				if (generateList)
				{
					requestJson["FilterBy"] = expresion?.Filter?.ToJson();
					requestJson["SortBy"] = expresion?.Sort?.ToJson();
				}

				// generate data
				try
				{
					var responseJson = await contentType.GetService().GenerateAsync(new RequestInfo(requestInfo)
					{
						ServiceName = contentType.ContentTypeDefinition.ModuleDefinition.ServiceName,
						ObjectName = contentType.ContentTypeDefinition.ObjectName,
						Body = requestJson.ToString(Newtonsoft.Json.Formatting.None)
					}, cancellationToken).ConfigureAwait(false);

					portletJSONs[theportlet.ID] = responseJson;

					if (writeSteps)
						await this.WriteLogsAsync(requestInfo.CorrelationID, $"Data of portlet has been generated\r\n- Request: {requestJson}\r\n- Response: {responseJson}", null, this.ServiceName, "Steps").ConfigureAwait(false);
				}
				catch (Exception ex)
				{
					var errorJson = new JObject
					{
						{ "Error", ex.Message },
						{ "Code", 500 },
						{ "Type", ex.GetTypeName(true) },
						{ "CorrelationID", requestInfo.CorrelationID }
					};
					if (ex is WampException)
					{
						var wampException = (ex as WampException).GetDetails();
						errorJson["Error"] = wampException.Item2;
						errorJson["Code"] = wampException.Item1;
						errorJson["Type"] = wampException.Item3;
						if (this.IsDebugLogEnabled)
							errorJson["Stack"] = wampException.Item4;
					}
					else if (this.IsDebugLogEnabled)
						errorJson["Stack"] = ex.StackTrace;
					portletJSONs[portlet.ID] = errorJson;
					gotError = true;
					await this.WriteLogsAsync(requestInfo.CorrelationID, $"Error occurred while generating data of portlet\r\n- Request: {requestJson}\r\n- Error: {errorJson}", ex, this.ServiceName, "Steps").ConfigureAwait(false);
				}
			}, cancellationToken).ConfigureAwait(false);

			// generate HTML of portlets
			var portletHTMLs = new Dictionary<string, List<string>>();
			await desktop.Portlets.OrderBy(portlet => portlet.Zone).ThenBy(portlet => portlet.OrderIndex).ForEachAsync(async (theportlet, index, token) =>
			{
				// get original first
				var portlet = theportlet.OriginalPortlet;

				// prepare container and zones
				var portletContainer = (await portlet.GetTemplateAsync(token).ConfigureAwait(false)).GetXDocument();
				var portletZones = portletContainer.GetZones().ToList();

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

					var style = portlet.CommonSettings?.TitleUISettings?.GetStyle() ?? "";
					if (!string.IsNullOrWhiteSpace(style))
					{
						var attribute = titleZone.Attributes().FirstOrDefault(attr => attr.Name.LocalName.IsEquals("style"));
						if (attribute == null)
							titleZone.Add(new XAttribute("style", style));
						else
							attribute.Value = (attribute.Value + " " + style).Trim();
					}

					var css = portlet.CommonSettings?.TitleUISettings?.Css ?? "";
					if (!string.IsNullOrWhiteSpace(css))
					{
						var attribute = titleZone.Attributes().FirstOrDefault(attr => attr.Name.LocalName.IsEquals("class"));
						if (attribute == null)
							titleZone.Add(new XAttribute("class", css));
						else
							attribute.Value = (attribute.Value + " " + css).Trim();
					}

					var portletTitle = "";
					if (!string.IsNullOrWhiteSpace(portlet.CommonSettings.IconURI))
						portletTitle += $"<img src=\"{portlet.CommonSettings.IconURI}\"/>";
					portletTitle += string.IsNullOrWhiteSpace(portlet.CommonSettings.TitleURL)
						? $"<span>{portlet.Title.Replace("\"", "&quot;").Replace("<", "&lt;").Replace(">", "&gt;")}</span>"
						: $"<span><a href=\"{portlet.CommonSettings.TitleURL}\">{portlet.Title.Replace("\"", "&quot;").Replace("<", "&lt;").Replace(">", "&gt;")}</a></span>";
					titleZone.Add(XElement.Parse($"<div>{portletTitle}</div>"));
				}

				// prepare content zone
				var contentZone = portletZones.GetZone("Content");
				if (contentZone != null)
				{
					contentZone.GetZoneIDAttribute().Remove();

					var style = portlet.CommonSettings?.ContentUISettings?.GetStyle() ?? "";
					if (!string.IsNullOrWhiteSpace(style))
					{
						var attribute = contentZone.Attributes().FirstOrDefault(attr => attr.Name.LocalName.IsEquals("style"));
						if (attribute == null)
							contentZone.Add(new XAttribute("style", style));
						else
							contentZone.Value = (attribute.Value + " " + style).Trim();
					}

					var css = portlet.CommonSettings?.ContentUISettings?.Css ?? "";
					if (!string.IsNullOrWhiteSpace(css))
					{
						var attribute = contentZone.Attributes().FirstOrDefault(attr => attr.Name.LocalName.IsEquals("class"));
						if (attribute == null)
							contentZone.Add(new XAttribute("class", css));
						else
							attribute.Value = (attribute.Value + " " + css).Trim();
					}

					var portletHTML = "";
					var contentType = await (portlet.RepositoryEntityID ?? "").GetContentTypeByIDAsync(token).ConfigureAwait(false);
					if (contentType != null && portletJSONs.TryGetValue(theportlet.ID, out var json))
					{
						var errorMessage = "";
						var errorStack = "";
						var portletContent = "";
						var xslFilename = "";
						var xslTemplate = "";
						var isList = !"View".IsEquals(requestInfo.GetQueryParameter("x-parent") != null && requestInfo.GetQueryParameter("x-content") != null ? portlet.AlternativeAction : portlet.Action);

						if (json["Error"] == null)
							try
							{
								// prepare XSLT
								xslTemplate = isList ? portlet.ListSettings.Template : portlet.ViewSettings.Template;
								if (string.IsNullOrWhiteSpace(xslTemplate))
								{
									xslFilename = (json["TemplateFilename"] as JValue)?.Value?.ToString();
									if (string.IsNullOrWhiteSpace(xslFilename))
										xslFilename = isList ? "list.xsl" : "view.xsl";
									xslTemplate = await Utility.GetTemplateAsync(xslFilename, portlet.Desktop?.WorkingTheme, contentType.ContentTypeDefinition?.ModuleDefinition?.Directory?.ToLower(), contentType.ContentTypeDefinition?.ObjectName?.ToLower(), token).ConfigureAwait(false);
									if (string.IsNullOrWhiteSpace(xslTemplate))
										xslTemplate = await Utility.GetTemplateAsync(xslFilename, "default", contentType.ContentTypeDefinition?.ModuleDefinition?.Directory?.ToLower(), contentType.ContentTypeDefinition?.ObjectName?.ToLower(), token).ConfigureAwait(false);
								}

								if (string.IsNullOrWhiteSpace(xslTemplate))
									throw new TemplateIsInvalidException($"XSLT is invalid [/themes/{portlet.Desktop?.WorkingTheme ?? "default"}/templates/{contentType.ContentTypeDefinition?.ModuleDefinition?.Directory?.ToLower() ?? "-"}/{contentType.ContentTypeDefinition?.ObjectName?.ToLower() ?? "-"}/{xslFilename}]");

								if (xslTemplate.PositionOf("{{pagination-holder}}") > 0)
								{
									var xslPagination = portlet.PaginationSettings.Template;
									if (string.IsNullOrWhiteSpace(xslPagination))
									{
										xslPagination = await Utility.GetTemplateAsync("pagination.xml", portlet.Desktop?.WorkingTheme, null, null, token).ConfigureAwait(false);
										if (string.IsNullOrWhiteSpace(xslPagination))
											xslPagination = await Utility.GetTemplateAsync("pagination.xml", "default", null, null, token).ConfigureAwait(false);
									}
									xslTemplate = xslTemplate.Replace(StringComparison.OrdinalIgnoreCase, "{{pagination-holder}}", xslPagination);
								}

								if (xslTemplate.PositionOf("{{breadcrumb-holder}}") > 0)
								{
									var xslBreadcrumb = portlet.BreadcrumbSettings.Template;
									if (string.IsNullOrWhiteSpace(xslBreadcrumb))
									{
										xslBreadcrumb = await Utility.GetTemplateAsync("breadcrumb.xml", portlet.Desktop?.WorkingTheme, null, null, token).ConfigureAwait(false);
										if (string.IsNullOrWhiteSpace(xslBreadcrumb))
											xslBreadcrumb = await Utility.GetTemplateAsync("breadcrumb.xml", "default", null, null, token).ConfigureAwait(false);
									}
									xslTemplate = xslTemplate.Replace(StringComparison.OrdinalIgnoreCase, "{{breadcrumb-holder}}", xslBreadcrumb);
								}

								// prepare XML
								if (!(json["Data"] is JValue xmlJson) || xmlJson.Value == null)
									throw new InformationRequiredException("The response JSON must have the element named 'Data' that contains XML for transforming via a node that named 'Data'");

								var breadcrumb = (json["Breadcrumb"] ?? new JArray()).Select(node => node as JObject).ToList();
								if (portlet.BreadcrumbSettings.ShowContentTypeLink)
								{
									if (!string.IsNullOrWhiteSpace(portlet.BreadcrumbSettings.ContentTypeAdditionalLabel) && !string.IsNullOrWhiteSpace(portlet.BreadcrumbSettings.ContentTypeAdditionalURL))
										breadcrumb.Insert(0, new JObject
										{
											{ "Text", portlet.BreadcrumbSettings.ContentTypeAdditionalLabel },
											{ "URL", portlet.BreadcrumbSettings.ContentTypeAdditionalURL }
										});
									breadcrumb.Insert(0, new JObject
									{
										{ "Text", !string.IsNullOrWhiteSpace(portlet.BreadcrumbSettings.ContentTypeLabel) ? portlet.BreadcrumbSettings.ContentTypeLabel : contentType.Title },
										{ "URL", !string.IsNullOrWhiteSpace(portlet.BreadcrumbSettings.ContentTypeURL) ? portlet.BreadcrumbSettings.ContentTypeURL : $"~/{contentType.Desktop?.Alias}" + (contentType.GetParent() != null ? "" : $"/{contentType.Title.GetANSIUri()}") }
									});
								}

								if (portlet.BreadcrumbSettings.ShowModuleLink)
								{
									if (!string.IsNullOrWhiteSpace(portlet.BreadcrumbSettings.ModuleAdditionalLabel) && !string.IsNullOrWhiteSpace(portlet.BreadcrumbSettings.ModuleAdditionalURL))
										breadcrumb.Insert(0, new JObject
										{
											{ "Text", portlet.BreadcrumbSettings.ModuleAdditionalLabel },
											{ "URL", portlet.BreadcrumbSettings.ModuleAdditionalURL }
										});
									breadcrumb.Insert(0, new JObject
									{
										{ "Text", !string.IsNullOrWhiteSpace(portlet.BreadcrumbSettings.ModuleLabel) ? portlet.BreadcrumbSettings.ModuleLabel : contentType.Module?.Title },
										{ "URL", !string.IsNullOrWhiteSpace(portlet.BreadcrumbSettings.ModuleURL) ? portlet.BreadcrumbSettings.ModuleURL : $"~/{contentType.Module?.Desktop?.Alias}" + (contentType.GetParent() != null ? "" : $"/{contentType.Module?.Title.GetANSIUri()}") }
									});
								}

								if (!string.IsNullOrWhiteSpace(portlet.BreadcrumbSettings.HomeAdditionalLabel) && !string.IsNullOrWhiteSpace(portlet.BreadcrumbSettings.HomeAdditionalURL))
									breadcrumb.Insert(0, new JObject
									{
										{ "Text", portlet.BreadcrumbSettings.HomeAdditionalLabel },
										{ "URL", portlet.BreadcrumbSettings.HomeAdditionalURL }
									});

								breadcrumb.Insert(0, new JObject
								{
									{ "Text", !string.IsNullOrWhiteSpace(portlet.BreadcrumbSettings.HomeLabel) ? portlet.BreadcrumbSettings.HomeLabel : "Home" },
									{ "URL", !string.IsNullOrWhiteSpace(portlet.BreadcrumbSettings.HomeURL) ? portlet.BreadcrumbSettings.HomeURL : $"~/index" }
								});

								var breadcrumbJson = new JObject
								{
									{ "Nodes", breadcrumb.ToJArray() }
								};

								var dataXml = XDocument.Parse(xmlJson.Value.ToString());
								var optionsXml = JsonConvert.DeserializeXNode((isList ? JObject.Parse(portlet.ListSettings.Options ?? "{}") : JObject.Parse(portlet.ViewSettings.Options ?? "{}")).ToString(), "Options");
								var breadcrumbXml = JsonConvert.DeserializeXNode(breadcrumbJson.ToString(), "Breadcrumb");
								breadcrumbXml.Root.Add(new XAttribute("SeperatedLabel", string.IsNullOrWhiteSpace(portlet.BreadcrumbSettings.SeperatedLabel) ? ">" : portlet.BreadcrumbSettings.SeperatedLabel));
								var paginationJson = json["Pagination"] ?? new JObject();
								var paginationXml = JsonConvert.DeserializeXNode(paginationJson.ToString(), "Pagination");

								// transform
								var xml = new XDocument(new XElement("VIEApps", dataXml.Root, optionsXml.Root, breadcrumbXml.Root, paginationXml.Root));
								portletContent = xml.Transfrom(xslTemplate);

								if (writeSteps)
									await this.WriteLogsAsync(requestInfo.CorrelationID, $"Data of portlet has been transformed\r\n- XML: {xml}\r\n- XSLT: {xslTemplate}\r\n- XHTML: {portletContent}", null, this.ServiceName, "Steps").ConfigureAwait(false);
							}
							catch (Exception ex)
							{
								errorMessage = $"Transform error => {ex.Message}";
								errorStack = ex.StackTrace;
								await this.WriteLogsAsync(requestInfo.CorrelationID, $"Error occurred while transforming XML/XSLT/XHTML of portlet => {ex.Message}", ex, this.ServiceName, "Steps").ConfigureAwait(false);
								if (!(ex is TemplateIsInvalidException) && !(ex is InformationRequiredException))
									await this.WriteLogsAsync(requestInfo.CorrelationID, $"- XML: {json["XML"]}\r\n- XSLT: {xslTemplate}{(string.IsNullOrWhiteSpace(isList ? portlet.ListSettings.Template : portlet.ViewSettings.Template) ? $"\r\n- XSL file: {portlet.Desktop?.WorkingTheme ?? "default"}/templates/{contentType.ContentTypeDefinition?.ModuleDefinition?.Directory?.ToLower() ?? "-"}/{contentType.ContentTypeDefinition?.ObjectName?.ToLower() ?? "-"}/{xslFilename}" : "")}", null, this.ServiceName, "Steps").ConfigureAwait(false);
							}
						else
						{
							errorMessage = (json["Error"] as JValue)?.Value?.ToString();
							errorStack = (json["Stack"] as JValue)?.Value?.ToString() ?? "";
						}

						if (!string.IsNullOrWhiteSpace(errorMessage))
							portletContent = "<div>"
								+ $"<div style=\"color:red\">{errorMessage.Replace("\"", "&quot;").Replace("<", "&lt;").Replace(">", "&gt;")}</div>"
								+ $"<div style=\"font-size:80%\">CorrelationID: {requestInfo.CorrelationID} - PortletID: {portlet.ID}</div>"
								+ (this.IsDebugLogEnabled ? $"<div style=\"font-size:80%\">Stack: {errorStack.Replace("\"", "&quot;").Replace("<", "&lt;").Replace(">", "&gt;")}</div>" : $"<!-- {errorStack.Replace("\"", "&quot;").Replace("<", "&lt;").Replace(">", "&gt;")} -->")
								+ "</div>";

						contentZone.Value = "{{content-holder}}";
						portletHTML = portletContainer.ToString();
						portletHTML = portletHTML.Replace(StringComparison.OrdinalIgnoreCase, "{{content-holder}}", portletContent);
					}
					else
						portletHTML = portletContainer.ToString();

					if (!portletHTMLs.TryGetValue(portlet.Zone, out var htmls))
					{
						htmls = new List<string>();
						portletHTMLs[portlet.Zone] = htmls;
					}
					htmls.Add(portletHTML.Replace(StringComparison.OrdinalIgnoreCase, "{{name}}", portlet.Title.GetANSIUri()));
				}
			}, cancellationToken, true, false).ConfigureAwait(false);

			// prepare meta tags
			var metaTags = $"<link rel=\"stylesheet\" href=\"https://cdnjs.cloudflare.com/ajax/libs/twitter-bootstrap/4.5.0/css/bootstrap.min.css\"/>"
				+ $"<link rel=\"stylesheet\" href=\"{Utility.PortalsHttpURI}/_css/default.css\"/>";
			if (!"default".IsEquals(desktop.WorkingTheme ?? "default"))
				metaTags += $"<link rel=\"stylesheet\" href=\"{Utility.PortalsHttpURI}/_css/{desktop.WorkingTheme ?? "default"}.css\"/>";

			var mainPortlet = !string.IsNullOrWhiteSpace(desktop.MainPortletID) && portletJSONs.ContainsKey(desktop.MainPortletID) ? portletJSONs[desktop.MainPortletID] : null;
			var seoInfo = mainPortlet["SEOInfo"];

			var title = "";
			var titleOfPortlet = (seoInfo == null ? null : (seoInfo["Title"] as JValue)?.Value?.ToString()) ?? "";
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
			var descriptionOfPortlet = (seoInfo == null ? null : (seoInfo["Description"] as JValue)?.Value?.ToString()) ?? "";
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
			var keywordsOfPortlet = (seoInfo == null ? null : (seoInfo["Keywords"] as JValue)?.Value?.ToString()) ?? "";
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

			var meta = "";

			if (!string.IsNullOrWhiteSpace(description))
			{
				description = description.Replace("&", "&amp;").Replace("\"", "&quot;").Replace("<", "&lt;").Replace(">", "&gt;");
				meta += $"<meta name=\"description\" property=\"og:description\" itemprop=\"description\" content=\"{description}\"/>";
			}

			if (!string.IsNullOrWhiteSpace(keywords))
				meta += $"<meta name=\"keywords\" content=\"{keywords.Replace("\"", "&quot;").Replace("<", "&lt;").Replace(">", "&gt;")}\"/>";

			meta += $"<meta name=\"twitter:title\" property=\"og:title\" itemprop=\"headline\" content=\"{title}\"/>";
			if (!string.IsNullOrWhiteSpace(description))
				meta = $"<meta name=\"twitter:description\" content=\"{description}\"/>";

			var coverURI = mainPortlet.Value<string>("CoverURI");
			if (!string.IsNullOrWhiteSpace(coverURI))
				meta += $"<meta name=\"twitter:image\" property=\"og:image\" itemprop=\"thumbnailUrl\" content=\"{coverURI}\"/>";
			if (!string.IsNullOrWhiteSpace(desktop.CoverURI))
				meta += $"<meta name=\"twitter:image\" property=\"og:image\" itemprop=\"thumbnailUrl\" content=\"{desktop.CoverURI}\"/>";
			if (!string.IsNullOrWhiteSpace(site.CoverURI))
				meta += $"<meta name=\"twitter:image\" property=\"og:image\" itemprop=\"thumbnailUrl\" content=\"{site.CoverURI}\"/>";

			if (!string.IsNullOrWhiteSpace(desktop.IconURI))
				meta += $"<link rel=\"icon\" type=\"image/{(desktop.IconURI.IsEndsWith(".icon") ? "x-icon" : desktop.IconURI.IsEndsWith(".png") ? "png" : "jpeg")}\" href=\"{desktop.IconURI}\"/>"
					+ $"<link rel=\"shortcut icon\" type=\"image/{(desktop.IconURI.IsEndsWith(".icon") ? "x-icon" : desktop.IconURI.IsEndsWith(".png") ? "png" : "jpeg")}\" href=\"{desktop.IconURI}\"/>";
			if (!string.IsNullOrWhiteSpace(site.IconURI))
				meta += $"<link rel=\"icon\" type=\"image/{(site.IconURI.IsEndsWith(".icon") ? "x-icon" : site.IconURI.IsEndsWith(".png") ? "png" : "jpeg")}\" href=\"{site.IconURI}\"/>"
					+ $"<link rel=\"shortcut icon\" type=\"image/{(site.IconURI.IsEndsWith(".icon") ? "x-icon" : site.IconURI.IsEndsWith(".png") ? "png" : "jpeg")}\" href=\"{site.IconURI}\"/>";

			metaTags = meta + metaTags;
			if (!string.IsNullOrWhiteSpace(site.MetaTags))
				metaTags += site.MetaTags;
			if (!string.IsNullOrWhiteSpace(desktop.MetaTags))
				metaTags += desktop.MetaTags;
			metaTags += $"<script src=\"https://ajax.googleapis.com/ajax/libs/jquery/3.5.1/jquery.min.js\"></script><script>var $j=$</script>";

			title = $"<title>{title}</title>";

			// prepare scripts
			var scripts = $"<script src=\"{Utility.PortalsHttpURI}/_js/default.js\"></script>";
			if (!"default".IsEquals(desktop.WorkingTheme ?? "default"))
				scripts += $"<script src=\"{Utility.PortalsHttpURI}/_js/{desktop.WorkingTheme ?? "default"}.js\"></script>";
			if (!string.IsNullOrWhiteSpace(site.Scripts))
				scripts += site.Scripts;
			if (!string.IsNullOrWhiteSpace(desktop.Scripts))
				scripts += desktop.Scripts;

			// prepare desktop body
			var desktopContainer = (await desktop.GetTemplateAsync(cancellationToken).ConfigureAwait(false)).GetXDocument();
			var desktopZones = desktopContainer.GetZones().ToList();

			if (writeSteps)
				await this.WriteLogsAsync(requestInfo.CorrelationID, $"Start to generate desktop - Defined zone(s): {desktopZones.GetZoneNames().Join(", ")}", null, this.ServiceName, "Steps").ConfigureAwait(false);

			var desktopZonesGotPortlet = desktop.Portlets.Select(portlet => portlet.Zone).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
			var removedZones = new List<XElement>();
			desktopZones.ForEach(zone =>
			{
				var idAttribute = zone.GetZoneIDAttribute();
				if (desktopZonesGotPortlet.IndexOf(idAttribute.Value) < 0)
				{
					removedZones.Add(zone);
					zone.Remove();
				}
				else
				{
					zone.Value = "{{" + zone.GetZoneIDAttribute().Value + "-holder}}";
					idAttribute.Remove();
				}
			});

			if (writeSteps)
				await this.WriteLogsAsync(requestInfo.CorrelationID, $"- Empty zone(s): {removedZones?.GetZoneNames().Join(", ")}", null, this.ServiceName, "Steps").ConfigureAwait(false);

			removedZones.ForEach(zone => desktopZones.Remove(zone));

			var emptyElements = desktopContainer.Root.Elements().Where(element => element.IsEmpty).ToList();
			emptyElements.ForEach(element => element.Remove());

			var body = desktopContainer.ToString();
			body = body.Replace(StringComparison.OrdinalIgnoreCase, "{{organization}}", organization.Alias);
			body = body.Replace(StringComparison.OrdinalIgnoreCase, "{{alias}}", desktop.Alias);
			body = body.Replace(StringComparison.OrdinalIgnoreCase, "{{theme}}", desktop.WorkingTheme ?? "default");
			portletHTMLs.ForEach(kvp => body = body.Replace(StringComparison.OrdinalIgnoreCase, "{{" + kvp.Key + "-holder}}", kvp.Value.Join("\r\n")));

			// generate html
			html = "<!DOCTYPE html>\r\n"
				+ "<html xmlns=\"http://www.w3.org/1999/xhtml\">\r\n"
				+ "<head><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"/></head>\r\n"
				+ "<body></body>\r\n"
				+ "</html>";

			var position = html.IndexOf("<head");
			position = html.IndexOf(">", position) + 1;
			html = html.Insert(position, title);

			position = html.IndexOf("</head>");
			html = html.Insert(position, metaTags);

			position = html.IndexOf("<body");
			position = html.IndexOf(">", position) + 1;
			html = html.Insert(position, body);

			position = html.IndexOf("</body>");
			html = html.Insert(position, scripts);

			// final => body's css and style
			var bodyCssStyle = desktop.UISettings?.GetStyle() ?? "";
			if (string.IsNullOrWhiteSpace(bodyCssStyle))
				bodyCssStyle = site.UISettings?.GetStyle() ?? "";

			var bodyCssClass = desktop.UISettings?.Css ?? "";
			if (!string.IsNullOrWhiteSpace(site.UISettings?.Css))
				bodyCssClass += (bodyCssClass != "" ? " " : "") + site.UISettings.Css;
			if (!string.IsNullOrWhiteSpace(bodyCssClass) || !string.IsNullOrWhiteSpace(bodyCssStyle))
			{
				var bodyStyle = "";
				if (!string.IsNullOrWhiteSpace(bodyCssClass))
					bodyStyle += $" class=\"{bodyCssClass}\"";
				if (!string.IsNullOrWhiteSpace(bodyCssStyle))
					bodyStyle += $" style=\"{bodyCssStyle}\"";
				position = html.IndexOf("<body");
				position = html.IndexOf(">", position);
				html = html.Insert(position, bodyStyle);
			}

			if (writeSteps)
				await this.WriteLogsAsync(requestInfo.CorrelationID, $"Complete to generate desktop => {html}", null, this.ServiceName, "Steps").ConfigureAwait(false);

			// prepare headers & caching
			if (!gotError && !this.IsDebugLogEnabled)
			{
				html = UtilityService.RemoveWhitespaces(html);
				lastModified = DateTime.Now.ToHttpString();
				await Task.WhenAll(
					Utility.Cache.SetAsync(timeCacheKey, lastModified, organization.RefreshUrls.Interval > 0 ? organization.RefreshUrls.Interval - 1 : Utility.Cache.ExpirationTime / 2, cancellationToken),
					Utility.Cache.SetAsync(htmlCacheKey, html, organization.RefreshUrls.Interval > 0 ? organization.RefreshUrls.Interval - 2 : Utility.Cache.ExpirationTime / 2, cancellationToken)
				).ConfigureAwait(false);
				headers = new Dictionary<string, string>(headers)
				{
					{ "ETag", eTag },
					{ "Last-Modified", lastModified },
					{ "Cache-Control", "public" }
				};
			}

			// response
			return new JObject
			{
				{ "StatusCode", 200 },
				{ "Headers", headers.ToJson() },
				{ "Body", this.IsDebugLogEnabled ? html : html.ToBytes().ToBase64() },
				{ "BodyAsPlainText", this.IsDebugLogEnabled.ToString() }
			};
		}
		#endregion

		#region Generate data for working with CMS Portals
		public async Task<JObject> GenerateAsync(RequestInfo requestInfo, CancellationToken cancellationToken = default)
		{
			try
			{
				switch (requestInfo.ObjectName.ToLower().Trim())
				{
					case "content":
						return await ContentProcessor.GenerateAsync(requestInfo, cancellationToken, this.ValidationKey).ConfigureAwait(false);

					case "item":
						return await ItemProcessor.GenerateAsync(requestInfo, cancellationToken).ConfigureAwait(false);

					case "link":
						return await LinkProcessor.GenerateAsync(requestInfo, cancellationToken).ConfigureAwait(false);

					default:
						throw new InvalidRequestException();
				}
			}
			catch (Exception ex)
			{
				throw this.GetRuntimeException(requestInfo, ex);
			}
		}
		#endregion

		#region Process communicate message of Portals service
		protected override async Task ProcessInterCommunicateMessageAsync(CommunicateMessage message, CancellationToken cancellationToken = default)
		{
			// prepare
			if (message?.Type == null || message?.Data == null)
				return;

			var correlationID = UtilityService.NewUUID;
			if (this.IsDebugLogEnabled)
				this.WriteLogs(correlationID, $"Process an inter-communicate message\r\n{message?.ToJson()}");

			// messages of an organization
			if (message.Type.StartsWith("Organization#"))
				await message.ProcessInterCommunicateMessageOfOrganizationAsync(cancellationToken).ConfigureAwait(false);

			// messages of a site
			else if (message.Type.StartsWith("Site#"))
				await message.ProcessInterCommunicateMessageOfSiteAsync(cancellationToken).ConfigureAwait(false);

			// messages a role
			else if (message.Type.StartsWith("Role#"))
				await message.ProcessInterCommunicateMessageOfRoleAsync(cancellationToken).ConfigureAwait(false);

			// messages of a desktop
			else if (message.Type.StartsWith("Desktop#"))
				await message.ProcessInterCommunicateMessageOfDesktopAsync(cancellationToken).ConfigureAwait(false);

			// messages a module
			else if (message.Type.StartsWith("Module#"))
				await message.ProcessInterCommunicateMessageOfModuleAsync(cancellationToken).ConfigureAwait(false);

			// messages a content-type
			else if (message.Type.StartsWith("ContentType#"))
				await message.ProcessInterCommunicateMessageOfContentTypeAsync(cancellationToken).ConfigureAwait(false);

			// messages of a CMS category
			else if (message.Type.StartsWith("Category#") || message.Type.StartsWith("CMS.Category#"))
				await message.ProcessInterCommunicateMessageOfCategoryAsync(cancellationToken).ConfigureAwait(false);
		}
		#endregion

		#region Process communicate message of CMS Portals service
		async Task ProcessCommunicateMessageAsync(CommunicateMessage message, CancellationToken cancellationToken = default)
		{
			var correlationID = UtilityService.NewUUID;

			if (message.Type.IsEquals("Definition#RequestInfo"))
				await this.SendDefinitionInfoAsync(cancellationToken).ConfigureAwait(false);

			else if (message.Type.IsEquals("Definition#Info"))
			{
				var definition = message.Data?.ToExpandoObject()?.Copy<ModuleDefinition>();
				if (this.IsDebugLogEnabled)
					await this.WriteLogsAsync(correlationID, $"Got an update of a module definition\r\n{message.Data}", null, this.ServiceName, "CMS.Portals").ConfigureAwait(false);

				if (definition != null && !string.IsNullOrWhiteSpace(definition.ID) && !Utility.ModuleDefinitions.ContainsKey(definition.ID))
				{
					Utility.ModuleDefinitions[definition.ID] = definition;
					definition.ContentTypeDefinitions.ForEach(contentTypeDefinition =>
					{
						contentTypeDefinition.ModuleDefinition = definition;
						Utility.ContentTypeDefinitions[contentTypeDefinition.ID] = contentTypeDefinition;
					});
					if (this.IsDebugLogEnabled)
						await this.WriteLogsAsync(correlationID, $"Update the module definition into the collection of definitions\r\n{definition.ToJson()}", null, this.ServiceName, "CMS").ConfigureAwait(false);
				}
			}
		}

		Task SendDefinitionInfoAsync(CancellationToken cancellationToken = default)
			=> this.SendInterCommunicateMessageAsync(new CommunicateMessage("CMS.Portals")
			{
				Type = "Definition#Info",
				Data = this.GetDefinition().ToJson()
			}, cancellationToken);
		#endregion

	}
}
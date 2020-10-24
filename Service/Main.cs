﻿#region Related components
using System;
using System.IO;
using System.Net;
using System.Text;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using System.Diagnostics;
using System.Dynamic;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using WampSharp.V2.Core.Contracts;
using Microsoft.Extensions.Logging;
using net.vieapps.Components.Utility;
using net.vieapps.Components.Security;
using net.vieapps.Components.Repository;
using net.vieapps.Components.Caching;
using net.vieapps.Services.Portals.Exceptions;
using System.Security.Cryptography;
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

				next?.Invoke(this);
			});
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

					#region process request of Portls HTTP service
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

							case "link":
							case "cms.link":
								json = this.GenerateFormControls<Link>(requestInfo.GetParameter("x-content-type-id"));
								break;

							case "item":
							case "cms.item":
								json = this.GenerateFormControls<Item>(requestInfo.GetParameter("x-content-type-id"));
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
				? "TextEditor"
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
										{ "objectName", contentType?.GetObjectName() },
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
			try
			{
				Utility.Languages.Clear();
				await UtilityService.GetAppSetting("Portals:Languages", "vi-VN|en-US").ToList("|", true).ForEachAsync(async (language, _) => await new[] { "common", "portals", "portals.cms", "users" }.ForEachAsync(async (module, __) =>
				{
					if (!Utility.Languages.TryGetValue(language, out var languages))
					{
						languages = new ExpandoObject();
						Utility.Languages[language] = languages;
					}
					languages.Merge(JObject.Parse(await UtilityService.FetchWebResourceAsync($"{Utility.APIsHttpURI}/statics/i18n/{module}/{language}.json", cancellationToken).ConfigureAwait(false)).ToExpandoObject());
				}, cancellationToken, true, false).ConfigureAwait(false), cancellationToken, true, false).ConfigureAwait(false);
				if (this.IsDebugResultsEnabled)
					await this.WriteLogsAsync(correlationID, $"Gathering i18n language resources successful => {Utility.Languages.Select(kvp => kvp.Key).Join(" - ")}", null, this.ServiceName, "CMS.Portals", LogLevel.Debug).ConfigureAwait(false);
			}
			catch (Exception ex)
			{
				await this.WriteLogsAsync(correlationID, $"Error occurred while gathering i18n language resources => {ex.Message}", ex, this.ServiceName, "CMS.Portals", LogLevel.Error).ConfigureAwait(false);
			}
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
			var isSystemAdministrator = await this.IsSystemAdministratorAsync(requestInfo).ConfigureAwait(false) || await this.IsAuthorizedAsync(requestInfo, "Organization", Components.Security.Action.Approve, cancellationToken).ConfigureAwait(false);
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
			var isSystemAdministrator = await this.IsSystemAdministratorAsync(requestInfo).ConfigureAwait(false) || await this.IsAuthorizedAsync(requestInfo, "Organization", Components.Security.Action.Approve, cancellationToken).ConfigureAwait(false);
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
			var isSystemAdministrator = await this.IsSystemAdministratorAsync(requestInfo).ConfigureAwait(false) || await this.IsAuthorizedAsync(requestInfo, "Organization", Components.Security.Action.Approve, cancellationToken).ConfigureAwait(false);
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
			var isSystemAdministrator = await this.IsSystemAdministratorAsync(requestInfo).ConfigureAwait(false) || await this.IsAuthorizedAsync(requestInfo, "Organization", Components.Security.Action.Approve, cancellationToken).ConfigureAwait(false);
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
			var isSystemAdministrator = await this.IsSystemAdministratorAsync(requestInfo).ConfigureAwait(false) || await this.IsAuthorizedAsync(requestInfo, "Organization", Components.Security.Action.Approve, cancellationToken).ConfigureAwait(false);
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
			var isSystemAdministrator = await this.IsSystemAdministratorAsync(requestInfo).ConfigureAwait(false) || await this.IsAuthorizedAsync(requestInfo, "Organization", Components.Security.Action.Approve, cancellationToken).ConfigureAwait(false);
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
			var isSystemAdministrator = await this.IsSystemAdministratorAsync(requestInfo).ConfigureAwait(false) || await this.IsAuthorizedAsync(requestInfo, "Organization", Components.Security.Action.Approve, cancellationToken).ConfigureAwait(false);
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
			var isSystemAdministrator = await this.IsSystemAdministratorAsync(requestInfo).ConfigureAwait(false) || await this.IsAuthorizedAsync(requestInfo, "Organization", Components.Security.Action.Approve, cancellationToken).ConfigureAwait(false);
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
			var isSystemAdministrator = await this.IsSystemAdministratorAsync(requestInfo).ConfigureAwait(false) || await this.IsAuthorizedAsync(requestInfo, "Organization", Components.Security.Action.Approve, cancellationToken).ConfigureAwait(false);
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
			var isSystemAdministrator = await this.IsSystemAdministratorAsync(requestInfo).ConfigureAwait(false) || await this.IsAuthorizedAsync(requestInfo, "Organization", Components.Security.Action.Approve, cancellationToken).ConfigureAwait(false);
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
			var isSystemAdministrator = await this.IsSystemAdministratorAsync(requestInfo).ConfigureAwait(false) || await this.IsAuthorizedAsync(requestInfo, "Organization", Components.Security.Action.Approve, cancellationToken).ConfigureAwait(false);
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
			var isSystemAdministrator = await this.IsSystemAdministratorAsync(requestInfo).ConfigureAwait(false) || await this.IsAuthorizedAsync(requestInfo, "Organization", Components.Security.Action.Approve, cancellationToken).ConfigureAwait(false);
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

			return new JObject { { "Template", template } };
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
		bool WriteDesktopLogs => this.IsDebugLogEnabled || "true".IsEquals(UtilityService.GetAppSetting("Logs:Portals:Desktops", "false"));

		HashSet<string> ExcludedThemes => UtilityService.GetAppSetting("Portals:Desktops:ExcludedThemes", "").Trim().ToLower().ToHashSet();

		bool CacheDesktopResources => "true".IsEquals(UtilityService.GetAppSetting("Portals:Desktops:Resources:Cache", "false"));

		bool AllowSrcResourceFiles => "true".IsEquals(UtilityService.GetAppSetting("Portals:Desktops:Resources:AllowSrcFiles", "true"));

		bool CacheDesktopHtmls => "true".IsEquals(UtilityService.GetAppSetting("Portals:Desktops:Htmls:Cache", "false"));

		bool RemoveDesktopHtmlWhitespaces => "true".IsEquals(UtilityService.GetAppSetting("Portals:Desktops:Htmls:RemoveWhitespaces", "true"));

#if NETSTANDARD2_0
		string BodyEncoding => UtilityService.GetAppSetting("Portals:Desktops:Body:Encoding", "gzip");
#else
		string BodyEncoding => UtilityService.GetAppSetting("Portals:Desktops:Body:Encoding", "br");
#endif

		async Task<JToken> ProcessHttpIndicatorRequestAsync(RequestInfo requestInfo, CancellationToken cancellationToken = default)
		{
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
			// get the type of the resource
			var type = requestInfo.Query["x-resource"];

			// static files in 'assets' directory or image files of a theme
			if (type.IsEquals("assets") || type.IsEquals("images"))
			{
				// prepare
				if (!requestInfo.Query.TryGetValue("x-path", out var filePath) || string.IsNullOrWhiteSpace(filePath))
					throw new InformationNotFoundException();
				var fileInfo = new FileInfo(Path.Combine(Utility.DataFilesDirectory, type.IsEquals("images") ? "themes" : "assets", filePath));
				if (!fileInfo.Exists)
					throw new InformationNotFoundException(filePath);

				var eTag = $"{type.ToLower()}#{filePath.ToLower().GenerateUUID()}";
				var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
				{
					{ "Content-Type", $"{fileInfo.GetMimeType()}; charset=utf-8" },
					{ "X-Correlation-ID", requestInfo.CorrelationID }
				};
				if (this.CacheDesktopResources)
					headers = new Dictionary<string, string>(headers, StringComparer.OrdinalIgnoreCase)
					{
						{ "ETag", eTag },
						{ "Last-Modified", fileInfo.LastWriteTime.ToHttpString() },
						{ "Cache-Control", "public" }
					};

				// response
				var ifModifiedSince = this.CacheDesktopResources ? requestInfo.GetHeaderParameter("If-Modified-Since") : null;
				return this.CacheDesktopResources && eTag.IsEquals(requestInfo.GetHeaderParameter("If-None-Match")) && ifModifiedSince != null && ifModifiedSince.FromHttpDateTime() >= fileInfo.LastWriteTime
					? new JObject
					{
						{ "StatusCode", (int)HttpStatusCode.NotModified },
						{ "Headers", headers.ToJson() }
					}
					: new JObject
					{
						{ "StatusCode", (int)HttpStatusCode.OK },
						{ "Headers", headers.ToJson() },
						{
							"Body",
							(filePath.IsEndsWith(".css")
								? (await UtilityService.ReadTextFileAsync(fileInfo, null, cancellationToken).ConfigureAwait(false)).MinifyCss().ToBytes()
								: filePath.IsEndsWith(".js")
									? (await UtilityService.ReadTextFileAsync(fileInfo, null, cancellationToken).ConfigureAwait(false)).MinifyJs().ToBytes()
									: await UtilityService.ReadBinaryFileAsync(fileInfo, cancellationToken).ConfigureAwait(false)
							).Compress(this.BodyEncoding).ToBase64()
						},
						{ "BodyEncoding", this.BodyEncoding }
					};
			}

			// css stylesheets
			if (type.IsEquals("css"))
			{
				// prepare
				if (!requestInfo.Query.TryGetValue("x-path", out var identity) || string.IsNullOrWhiteSpace(identity))
					throw new InvalidRequestException($"The request is invalid [({requestInfo.Verb}): {requestInfo.GetURI()}]");

				var body = "";
				var lastModified = DateTimeService.CheckingDateTime;
				identity = identity.Replace(".css", "").ToLower().Trim();

				// stylesheets of a site/desktop
				if (identity.Length == 34 && identity.Right(32).IsValidUUID())
				{
					if (identity.Left(1).IsEquals("s"))
					{
						var site = await identity.Right(32).GetSiteByIDAsync(cancellationToken).ConfigureAwait(false);
						if (site != null)
						{
							body = this.IsDebugLogEnabled ? $"/* css of the '{site.Title}' site */\r\n" : "";
							lastModified = site.LastModified;
							body += string.IsNullOrWhiteSpace(site.Stylesheets) ? "" : site.Stylesheets.Replace("~~/", $"{Utility.FilesHttpURI}/").MinifyCss();
						}
						else
							body = $"/* the requested site ({identity}) is not found */";
					}
					else if (identity.Left(1).IsEquals("d"))
					{
						var desktop = await identity.Right(32).GetDesktopByIDAsync(cancellationToken).ConfigureAwait(false);
						if (desktop != null)
						{
							body = this.IsDebugLogEnabled ? $"/* css of the '{desktop.Title}' desktop */\r\n" : "";
							lastModified = desktop.LastModified;
							body += string.IsNullOrWhiteSpace(desktop.Stylesheets) ? "" : desktop.Stylesheets.Replace("~~/", $"{Utility.FilesHttpURI}/").MinifyCss();
						}
						else
							body = $"/* the requested desktop ({identity}) is not found */";
					}
					else
						body = $"/* the requested resource ({identity}) is not found */";
				}

				// stylesheets of a theme
				else
				{
					body = this.IsDebugLogEnabled ? $"/* css of the '{identity}' theme */\r\n" : "";
					var directory = new DirectoryInfo(Path.Combine(Utility.DataFilesDirectory, "themes", identity, "css"));
					if (directory.Exists)
					{
						var files = directory.GetFiles("*.css");
						if (files.Length < 1)
							lastModified = directory.LastWriteTime;
						else
							await files.OrderBy(fileInfo => fileInfo.Name).ForEachAsync(async (fileInfo, token) =>
							{
								body += (this.IsDebugLogEnabled ? $"\r\n/* {fileInfo.FullName} */\r\n" : "") + (await UtilityService.ReadTextFileAsync(fileInfo, null, cancellationToken).ConfigureAwait(false)).Replace("~~/", $"{Utility.FilesHttpURI}/").MinifyCss() + "\r\n";
								if (fileInfo.LastWriteTime > lastModified)
									lastModified = fileInfo.LastWriteTime;
							}, cancellationToken, true, false).ConfigureAwait(false);
					}
				}

				var eTag = $"css#{(identity.IsValidUUID() ? identity : identity.GenerateUUID())}";
				var headers = new Dictionary<string, string>
				{
					{ "Content-Type", "text/css; charset=utf-8" },
					{ "X-Correlation-ID", requestInfo.CorrelationID }
				};
				if (this.CacheDesktopResources && !this.ExcludedThemes.Contains(identity))
					headers = new Dictionary<string, string>(headers, StringComparer.OrdinalIgnoreCase)
					{
						{ "ETag", eTag },
						{ "Last-Modified", lastModified.ToHttpString() },
						{ "Cache-Control", "public" }
					};

				// response
				var ifModifiedSince = this.CacheDesktopResources ? requestInfo.GetHeaderParameter("If-Modified-Since") : null;
				return this.CacheDesktopResources && eTag.IsEquals(requestInfo.GetHeaderParameter("If-None-Match")) && ifModifiedSince != null && ifModifiedSince.FromHttpDateTime() >= lastModified
					? new JObject
					{
						{ "StatusCode", (int)HttpStatusCode.NotModified },
						{ "Headers", headers.ToJson() }
					}
					: new JObject
					{
						{ "StatusCode", (int)HttpStatusCode.OK },
						{ "Headers", headers.ToJson() },
						{ "Body", body.Compress(this.BodyEncoding) },
						{ "BodyEncoding", this.BodyEncoding }
					};
			}

			// scripts
			if (type.IsEquals("js") || type.IsEquals("javascript") || type.IsEquals("script") || type.IsEquals("scripts"))
			{
				// prepare
				if (!requestInfo.Query.TryGetValue("x-path", out var identity) || string.IsNullOrWhiteSpace(identity))
					throw new InvalidRequestException($"The request is invalid [({requestInfo.Verb}): {requestInfo.GetURI()}]");

				var body = "";
				var lastModified = DateTimeService.CheckingDateTime;
				identity = identity.Replace(".js", "").ToLower().Trim();

				// scripts of organization/site/desktop
				if (identity.Length == 34 && identity.Right(32).IsValidUUID())
				{
					if (identity.Left(1).IsEquals("o"))
					{
						var organization = await identity.Right(32).GetOrganizationByIDAsync(cancellationToken).ConfigureAwait(false);
						if (organization != null)
						{
							body = this.IsDebugLogEnabled ? $"/* scripts of the '{organization.Title}' organization */\r\n" : "";
							lastModified = organization.LastModified;
							body += string.IsNullOrWhiteSpace(organization.Scripts) ? "" : organization.Scripts.Replace("~~/", $"{Utility.FilesHttpURI}/").MinifyJs();
						}
						else
							body = $"/* the requested organization ({identity.Right(32)}) is not found */";
					}
					else if (identity.Left(1).IsEquals("s"))
					{
						var site = await identity.Right(32).GetSiteByIDAsync(cancellationToken).ConfigureAwait(false);
						if (site != null)
						{
							body = this.IsDebugLogEnabled ? $"/* scripts of the '{site.Title}' site */\r\n" : "";
							lastModified = site.LastModified;
							body += string.IsNullOrWhiteSpace(site.Scripts) ? "" : site.Scripts.Replace("~~/", $"{Utility.FilesHttpURI}/").MinifyJs();
						}
						else
							body = $"/* the requested site ({identity.Right(32)}) is not found */";
					}
					else if (identity.Left(1).IsEquals("d"))
					{
						var desktop = await identity.Right(32).GetDesktopByIDAsync(cancellationToken).ConfigureAwait(false);
						if (desktop != null)
						{
							body = this.IsDebugLogEnabled ? $"/* scripts of the '{desktop.Title}' desktop */\r\n" : "";
							lastModified = desktop.LastModified;
							body += string.IsNullOrWhiteSpace(desktop.Scripts) ? "" : desktop.Scripts.Replace("~~/", $"{Utility.FilesHttpURI}/").MinifyJs();
						}
						else
							body = $"/* the requested desktop ({identity.Right(32)}) is not found */";
					}
					else
						body = $"/* the requested resource ({identity}) is not found */";
				}

				// scripts of a theme
				else
				{
					body = this.IsDebugLogEnabled ? $"/* scripts of the '{identity}' theme */\r\n" : "";
					var directory = new DirectoryInfo(Path.Combine(Utility.DataFilesDirectory, "themes", identity, "js"));
					if (directory.Exists)
					{
						var files = directory.GetFiles("*.js");
						if (files.Length < 1)
							lastModified = directory.LastWriteTime;
						else
							await files.OrderBy(fileInfo => fileInfo.Name).ForEachAsync(async (fileInfo, token) =>
							{
								body += (this.IsDebugLogEnabled ? $"\r\n/* {fileInfo.FullName} */\r\n" : "") + (await UtilityService.ReadTextFileAsync(fileInfo, null, cancellationToken).ConfigureAwait(false)).MinifyJs() + "\r\n";
								if (fileInfo.LastWriteTime > lastModified)
									lastModified = fileInfo.LastWriteTime;
							}, cancellationToken, true, false).ConfigureAwait(false);
					}
				}

				var eTag = $"script#{identity.GenerateUUID()}";
				var headers = new Dictionary<string, string>
				{
					{ "Content-Type", "application/javascript; charset=utf-8" },
					{ "X-Correlation-ID", requestInfo.CorrelationID }
				};
				if (this.CacheDesktopResources && !this.ExcludedThemes.Contains(identity))
					headers = new Dictionary<string, string>(headers, StringComparer.OrdinalIgnoreCase)
					{
						{ "ETag", eTag },
						{ "Last-Modified", lastModified.ToHttpString() },
						{ "Cache-Control", "public" }
					};

				// response
				var ifModifiedSince = this.CacheDesktopResources ? requestInfo.GetHeaderParameter("If-Modified-Since") : null;
				return this.CacheDesktopResources && eTag.IsEquals(requestInfo.GetHeaderParameter("If-None-Match")) && ifModifiedSince != null && ifModifiedSince.FromHttpDateTime() >= lastModified
					? new JObject
					{
						{ "StatusCode", (int)HttpStatusCode.NotModified },
						{ "Headers", headers.ToJson() }
					}
					: new JObject
					{
						{ "StatusCode", (int)HttpStatusCode.OK },
						{ "Headers", headers.ToJson() },
						{ "Body", body.Compress(this.BodyEncoding) },
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
			if (SiteProcessor.Sites.Count < 1)
			{
				var filter = Filters<Site>.And(Filters<Site>.Equals("SystemID", organization.ID));
				var sort = Sorts<Site>.Ascending("Title");
				var sites = await Site.FindAsync(filter, sort, 0, 1, Extensions.GetCacheKey(filter, sort, 0, 1), cancellationToken).ConfigureAwait(false);
				await sites.ForEachAsync(async (website, token) => await website.SetAsync(false, true, token), cancellationToken).ConfigureAwait(false);
				organization._siteIDs = sites.Select(website => website.ID).ToList();
				await organization.SetAsync(false, true, cancellationToken).ConfigureAwait(false);
			}

			if (DesktopProcessor.Desktops.Count < 1 || DesktopProcessor.Desktops.Count(kvp => kvp.Value.SystemID == organization.ID) < 1)
			{
				var filter = Filters<Desktop>.And(Filters<Desktop>.Equals("SystemID", organization.ID), Filters<Desktop>.IsNull("ParentID"));
				var sort = Sorts<Desktop>.Ascending("Title");
				var desktops = await Desktop.FindAsync(filter, sort, 0, 1, Extensions.GetCacheKey(filter, sort, 0, 1), cancellationToken).ConfigureAwait(false);
				await desktops.ForEachAsync(async (webdesktop, token) => await webdesktop.SetAsync(false, true, token), cancellationToken).ConfigureAwait(false);
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

				redirectURL += organization.AlwaysUseHtmlSuffix && !redirectURL.IsEndsWith(".html") ? ".html" : "";
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
					await this.WriteLogsAsync(requestInfo.CorrelationID, $"Redirect for matching with the settings - Execution times: {stopwatch.GetElapsedTimes()}\r\n{requestURL} => {redirectURL} [{redirectCode}]", null, this.ServiceName, "Process.Http.Request").ConfigureAwait(false);
				return response;
			}

			// start process
			var desktopInfo = $"the '{desktop.Title}' desktop [Alias: {desktop.Alias} - ID: {desktop.ID}]";
			await this.WriteLogsAsync(requestInfo.CorrelationID, $"Start to process {desktopInfo}", null, this.ServiceName, "Process.Http.Request").ConfigureAwait(false);

			// prepare the caching
			var theme = desktop.WorkingTheme ?? "default";
			var key = $"{desktop.ID}{("-default".IsEquals(alias) ? "" : $":{(requestURL.IndexOf("?") > 0 ? requestURL.Left(requestURL.IndexOf("?")) : requestURL).GenerateUUID()}")}";
			var htmlCacheKey = $"{key}:html";
			var timeCacheKey = $"{key}:time";
			var headers = new Dictionary<string, string>
			{
				{ "Content-Type", "text/html; charset=utf-8" },
				{ "X-Correlation-ID", requestInfo.CorrelationID }
			};

			// check "If-Modified-Since" request to reduce traffict
			var cache = this.CacheDesktopHtmls ? organization.GetCacheOfDesktopHTML() : null;
			var eTag = $"desktop#{key}";
			var lastModified = "";
			var ifModifiedSince = this.CacheDesktopHtmls ? requestInfo.GetHeaderParameter("If-Modified-Since") : null;
			if (this.CacheDesktopHtmls && eTag.IsEquals(requestInfo.GetHeaderParameter("If-None-Match")) && ifModifiedSince != null)
			{
				lastModified = await cache.GetAsync<string>(timeCacheKey, cancellationToken).ConfigureAwait(false);
				if (!string.IsNullOrWhiteSpace(lastModified) && ifModifiedSince.FromHttpDateTime() >= lastModified.FromHttpDateTime())
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
					await this.WriteLogsAsync(requestInfo.CorrelationID, $"By-pass the process of {desktopInfo} => Got 'If-Modified-Since'/'If-None-Match' request headers - ETag: {eTag} - Timestamp: {lastModified} - Execution times: {stopwatch.GetElapsedTimes()}", null, this.ServiceName, "Process.Http.Request").ConfigureAwait(false);
					return response;
				}
			}

			// environment info
			var isMobile = "true".IsEquals(requestInfo.GetHeaderParameter("x-environment-is-mobile")) ? "true" : "false";
			var osInfo = requestInfo.GetHeaderParameter("x-environment-os-info") ?? "Generic OS";

			// response as cached HTML
			var html = cache != null ? await cache.GetAsync<string>(htmlCacheKey, cancellationToken).ConfigureAwait(false) : null;
			if (!string.IsNullOrWhiteSpace(html))
			{
				html = this.NormalizeDesktopHtml(html, requestURI, useShortURLs, organization, site, desktop, isMobile, osInfo);
				lastModified = string.IsNullOrWhiteSpace(lastModified) ? await cache.GetAsync<string>(timeCacheKey, cancellationToken).ConfigureAwait(false) : lastModified;
				if (string.IsNullOrWhiteSpace(lastModified))
					lastModified = DateTime.Now.ToHttpString();
				headers = new Dictionary<string, string>(headers)
				{
					{ "ETag", eTag },
					{ "Last-Modified", lastModified },
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
				await this.WriteLogsAsync(requestInfo.CorrelationID, $"By-pass the process of {desktopInfo} => Got cached of XHTML - Key: {htmlCacheKey} - ETag: {eTag} - Timestamp: {lastModified} - Execution times: {stopwatch.GetElapsedTimes()}", null, this.ServiceName, "Process.Http.Request").ConfigureAwait(false);
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

				if (writeDesktopLogs)
				{
					stepwatch.Restart();
					await this.WriteLogsAsync(requestInfo.CorrelationID, $"Start to prepare data of {desktop.Portlets.Count} portlet(s) of {desktopInfo} => {desktop.Portlets.Select(p => p.Title).Join(", ")}", null, this.ServiceName, "Process.Http.Request").ConfigureAwait(false);
				}

				var organizationJson = organization.ToJson(false, false, json =>
				{
					OrganizationProcessor.ExtraProperties.ForEach(name => json.Remove(name));
					json.Remove("Privileges");
					json["Description"] = organization.Description?.NormalizeHTMLBreaks();
					json["AlwaysUseHtmlSuffix"] = organization.AlwaysUseHtmlSuffix;
				});

				var siteJson = site.ToJson(json =>
				{
					SiteProcessor.ExtraProperties.ForEach(name => json.Remove(name));
					json.Remove("Privileges");
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
				var fileSurfname = $"_p[{(string.IsNullOrWhiteSpace(parentIdentity) ? "none" : parentIdentity.Left(32) + (parentIdentity.Length > 32 ? "---" : "")).GetANSIUri()}]_c[{(string.IsNullOrWhiteSpace(contentIdentity) ? "none" : contentIdentity.Left(32) + (contentIdentity.Length > 32 ? "---" : "")).GetANSIUri()}]";

				async Task<JObject> generatorAsync(ContentType portletContentType, JObject requestJson)
				{
					var data = await this.PreparePortletAsync(requestInfo, portletContentType, requestJson, cancellationToken).ConfigureAwait(false);
					if (writeDesktopLogs)
					{
						var portletTitle = requestJson.Get<string>("Title");
						var portletID = requestJson.Get<string>("ID");
						await UtilityService.WriteTextFileAsync(Path.Combine(Utility.TempFilesDirectory, $"{$"{portletTitle}_{portletID}".GetANSIUri()}{fileSurfname}_request.json"), requestJson?.ToString(Newtonsoft.Json.Formatting.Indented) ?? "NULL", false, null, cancellationToken).ConfigureAwait(false);
					}
					return data;
				}

				var portletData = new ConcurrentDictionary<string, JObject>(StringComparer.OrdinalIgnoreCase);
				await desktop.Portlets.ForEachAsync(async (portlet, token) =>
				{
					JObject data;
					try
					{
						data = await this.PreparePortletAsync(portlet, requestInfo, organizationJson, siteJson, desktopsJson, desktop.Language ?? site.Language, parentIdentity, contentIdentity, pageNumber, generatorAsync, writeDesktopLogs, requestInfo.CorrelationID, token).ConfigureAwait(false);
					}
					catch (Exception ex)
					{
						if (ex.Message.IsContains("services.unknown"))
							try
							{
								data = await this.PreparePortletAsync(portlet, requestInfo, organizationJson, siteJson, desktopsJson, desktop.Language ?? site.Language, parentIdentity, contentIdentity, pageNumber, generatorAsync, writeDesktopLogs, requestInfo.CorrelationID, token).ConfigureAwait(false);
							}
							catch (Exception exc)
							{
								data = this.GenerateErrorJson(exc, requestInfo.CorrelationID, writeDesktopLogs, $"Unexpected error => {exc.Message}");
							}
						else
							data = this.GenerateErrorJson(ex, requestInfo.CorrelationID, writeDesktopLogs, $"Unexpected error => {ex.Message}");
					}

					if (data != null)
						portletData[portlet.ID] = data;

					if (writeDesktopLogs)
						await UtilityService.WriteTextFileAsync(Path.Combine(Utility.TempFilesDirectory, $"{$"{portlet.Title}_{portlet.ID}".GetANSIUri()}{fileSurfname}_response.json"), data?.ToString(Newtonsoft.Json.Formatting.Indented) ?? "NULL", false, null, token).ConfigureAwait(false);

				}, cancellationToken).ConfigureAwait(false);

				// generate HTML of portlets
				if (writeDesktopLogs)
				{
					stepwatch.Stop();
					await this.WriteLogsAsync(requestInfo.CorrelationID, $"Complete prepare portlets' data of {desktopInfo} - Execution times: {stepwatch.GetElapsedTimes()}", null, this.ServiceName, "Process.Http.Request").ConfigureAwait(false);
					stepwatch.Restart();
					await this.WriteLogsAsync(requestInfo.CorrelationID, $"Start to generate HTML of {desktopInfo}", null, this.ServiceName, "Process.Http.Request").ConfigureAwait(false);
				}

				var portletHtmls = new ConcurrentDictionary<string, Tuple<string, bool>>(StringComparer.OrdinalIgnoreCase);
				var generatePortletsTask = desktop.Portlets.ForEachAsync(async (portlet, token) =>
				{
					try
					{
						var action = !string.IsNullOrWhiteSpace(parentIdentity) && !string.IsNullOrWhiteSpace(contentIdentity) ? portlet.OriginalPortlet.AlternativeAction : portlet.OriginalPortlet.Action;
						var isList = string.IsNullOrWhiteSpace(action) || "List".IsEquals(action);
						portletHtmls[portlet.ID] = await this.GeneratePortletAsync(portlet, isList, portletData.TryGetValue(portlet.ID, out var data) ? data : null, siteJson, desktopsJson, organization.AlwaysUseHtmlSuffix, requestInfo.CorrelationID, token, writeDesktopLogs, fileSurfname).ConfigureAwait(false);
					}
					catch (Exception ex)
					{
						portletHtmls[portlet.ID] = new Tuple<string, bool>(this.GenerateErrorHtml($"Unexpected error => {ex.Message}", ex.StackTrace, requestInfo.CorrelationID, portlet.ID), true);
					}
				}, cancellationToken);

				// generate desktop
				string title = "", metaTags = "", scripts = "", body = "";
				try
				{
					var desktopData = await this.GenerateDesktopAsync(desktop, organization, site, string.IsNullOrWhiteSpace(desktop.MainPortletID) || !portletData.TryGetValue(desktop.MainPortletID, out var mainPortlet) ? null : mainPortlet, parentIdentity, contentIdentity, writeDesktopLogs, requestInfo.CorrelationID, cancellationToken).ConfigureAwait(false);
					title = desktopData.Item1;
					metaTags = desktopData.Item2;
					scripts = desktopData.Item3;
					body = desktopData.Item4;
				}
				catch (Exception ex)
				{
					body = this.GenerateErrorHtml($"Unexpected error => {ex.Message}", ex.StackTrace, requestInfo.CorrelationID, desktop.ID, "Desktop ID");
				}

				// prepare HTML of portlets
				await generatePortletsTask.ConfigureAwait(false);
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
				html = "<!DOCTYPE html><html xmlns=\"http://www.w3.org/1999/xhtml\"><head></head><body></body></html>";

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

				html = html.Insert(html.IndexOf("</head>"), $"<title>{title}</title><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"/>");
				html = html.Insert(html.IndexOf("</head>"), metaTags + "<script src=\"" + this.GetHttpURI("jQuery", "https://cdnjs.cloudflare.com/ajax/libs/jquery/3.5.1/jquery.min.js") + "\"></script><script>var $j=$;var __vieapps={rootURL:\"~/\",desktops:{home:{{homeDesktop}},search:{{searchDesktop}}},language:{{language}},isMobile:{{isMobile}},osInfo:\"{{osInfo}}\"};</script>");
				html = html.Insert(html.IndexOf("</body>"), body + scripts);

				if (writeDesktopLogs)
				{
					stepwatch.Stop();
					await this.WriteLogsAsync(requestInfo.CorrelationID, $"HTML code of {desktopInfo} has been generated - Execution times: {stepwatch.GetElapsedTimes()}\r\n- HTML:\r\n{html}", null, this.ServiceName, "Process.Http.Request").ConfigureAwait(false);
				}

				// minify
				html = this.RemoveDesktopHtmlWhitespaces ? html.MinifyHtml() : html.Trim();

				// prepare caching
				if (this.CacheDesktopHtmls && !portletHtmls.Values.Any(data => data.Item2))
				{
					lastModified = DateTime.Now.ToHttpString();
					await Task.WhenAll(
						cache.SetAsync(timeCacheKey, lastModified, cancellationToken),
						cache.SetAsync(htmlCacheKey, html, cancellationToken)
					).ConfigureAwait(false);
					headers = new Dictionary<string, string>(headers, StringComparer.OrdinalIgnoreCase)
					{
						{ "ETag", eTag },
						{ "Last-Modified", lastModified },
						{ "Cache-Control", "public" }
					};
				}

				// normalize
				html = this.NormalizeDesktopHtml(html, requestURI, useShortURLs, organization, site, desktop, isMobile, osInfo);
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
			await this.WriteLogsAsync(requestInfo.CorrelationID, $"Complete process of {desktopInfo} - Execution times: {stopwatch.GetElapsedTimes()}", null, this.ServiceName, "Process.Http.Request").ConfigureAwait(false);
			return response;
		}

		Task<JObject> PreparePortletAsync(RequestInfo requestInfo, ContentType contentType, JObject requestJson, CancellationToken cancellationToken)
			=> contentType.GetService().GenerateAsync(new RequestInfo(requestInfo)
			{
				ServiceName = contentType.ContentTypeDefinition.ModuleDefinition.ServiceName,
				ObjectName = contentType.ContentTypeDefinition.ObjectName,
				Body = requestJson.ToString(Newtonsoft.Json.Formatting.None),
				Header = new Dictionary<string, string>(requestInfo.Header, StringComparer.OrdinalIgnoreCase)
				{
					["x-origin"] = $"Portlet: {requestJson.Get<string>("Title")} [ID: {requestJson.Get<string>("ID")} - Action: {requestJson.Get<string>("Action")}]"
				}
			}, cancellationToken);

		async Task<JObject> PreparePortletAsync(Portlet theportlet, RequestInfo requestInfo, JObject organizationJson, JObject siteJson, JObject desktopsJson, string language, string parentIdentity, string contentIdentity, string pageNumber, Func<ContentType, JObject, Task<JObject>> generatorAsync, bool writeLogs = false, string correlationID = null, CancellationToken cancellationToken = default)
		{
			// get original portlet
			var stopwatch = Stopwatch.StartNew();
			var portlet = theportlet.OriginalPortlet;
			var portletInfo = writeLogs
				? $"the '{theportlet.Title}' portlet [ID: {theportlet.ID}{(string.IsNullOrWhiteSpace(theportlet.OriginalPortletID) ? "" : $" - alias of '{portlet.Title}' (ID: {portlet.ID})")}]"
				: null;
			if (writeLogs)
				await this.WriteLogsAsync(correlationID, $"Start to prepare data of {portletInfo}", null, this.ServiceName, "Process.Http.Request").ConfigureAwait(false);

			// get content-type
			var contentType = await (portlet.RepositoryEntityID ?? "").GetContentTypeByIDAsync(cancellationToken).ConfigureAwait(false);
			var parentContentType = contentType?.GetParent();

			// no content-type => then by-pass on static porlet
			if (contentType == null)
			{
				stopwatch.Stop();
				if (writeLogs)
					await this.WriteLogsAsync(correlationID, $"By-pass the preparing process of {portletInfo} => Static content - Execution times: {stopwatch.GetElapsedTimes()}", null, this.ServiceName, "Process.Http.Request").ConfigureAwait(false);
				return null;
			}

			// prepare
			parentIdentity = parentIdentity ?? requestInfo.GetQueryParameter("x-parent");
			contentIdentity = contentIdentity ?? requestInfo.GetQueryParameter("x-content");
			pageNumber = pageNumber ?? requestInfo.GetQueryParameter("x-page");

			var action = !string.IsNullOrWhiteSpace(parentIdentity) && !string.IsNullOrWhiteSpace(contentIdentity) ? portlet.AlternativeAction : portlet.Action;
			var isList = string.IsNullOrWhiteSpace(action) || "List".IsEquals(action);

			var expresion = isList && !string.IsNullOrWhiteSpace(portlet.ExpressionID) ? await portlet.ExpressionID.GetExpressionByIDAsync(cancellationToken).ConfigureAwait(false) : null;
			var optionsJson = isList ? JObject.Parse(portlet.ListSettings?.Options ?? "{}") : JObject.Parse(portlet.ViewSettings?.Options ?? "{}");
			var desktop = await optionsJson.Get<string>("DesktopID", "").GetDesktopByIDAsync(cancellationToken).ConfigureAwait(false);

			if (writeLogs)
				await this.WriteLogsAsync(correlationID, $"Determine the action/expression for generating content of {portletInfo} - Action: {(isList ? "List" : "View")} - Expression: {portlet.ExpressionID ?? "N/A"} (Title: {expresion?.Title ?? "None"}{(expresion != null ? $" / Filter: {expresion.Filter != null} / Sort: {expresion.Sort != null}" : "")}) - Specified desktop: {(desktop != null ? $"{desktop.Title} [ID: {desktop.ID}]" : "(None)")}", null, this.ServiceName, "Process.Http.Request").ConfigureAwait(false);

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
						json["Description"] = contentType.Module.Description?.NormalizeHTMLBreaks();
					})
				},
				{ "ContentType", contentType.ToJson(json =>
					{
						ModuleProcessor.ExtraProperties.ForEach(name => json.Remove(name));
						json.Remove("Privileges");
						json["Description"] = contentType.Description?.NormalizeHTMLBreaks();
					})
				},
				{ "ParentContentType", parentContentType?.ToJson(json =>
					{
						ModuleProcessor.ExtraProperties.ForEach(name => json.Remove(name));
						json.Remove("Privileges");
						json["Description"] = parentContentType.Description?.NormalizeHTMLBreaks();
					})
				}
			};

			// call the service for generating content of the portlet
			JObject responseJson = null;
			Exception exception = null;
			var serviceURI = $"GET /{contentType.ContentTypeDefinition.ModuleDefinition.ServiceName.ToLower()}/{contentType.ContentTypeDefinition.ObjectName.ToLower()}";
			try
			{
				if (writeLogs)
					await this.WriteLogsAsync(correlationID, $"Call the service ({serviceURI}) to prepare data of {portletInfo}\r\n- Request:\r\n{requestJson}", null, this.ServiceName, "Process.Http.Request").ConfigureAwait(false);
				responseJson = await generatorAsync(contentType, requestJson).ConfigureAwait(false);
			}
			catch (Exception ex)
			{
				exception = ex;
				responseJson = this.GenerateErrorJson(ex, correlationID, writeLogs, $"Error occurred while calling a service [{serviceURI}]");
			}

			stopwatch.Stop();
			if (exception != null)
				await this.WriteLogsAsync(correlationID, $"Error occurred while preparing data of {portletInfo} - Execution times: {stopwatch.GetElapsedTimes()}\r\n- Request:\r\n{requestJson}\r\n- Error:\r\n{responseJson}", exception, this.ServiceName, "Process.Http.Request", LogLevel.Error).ConfigureAwait(false);
			else if (writeLogs)
				await this.WriteLogsAsync(correlationID, $"Data of {portletInfo} has been prepared - Execution times: {stopwatch.GetElapsedTimes()}\r\n- Response:\r\n{responseJson}", null, this.ServiceName, "Process.Http.Request").ConfigureAwait(false);

			return responseJson;
		}

		async Task<Tuple<string, bool>> GeneratePortletAsync(Portlet theportlet, bool isList, JObject data, JObject siteJson, JObject desktopsJson, bool alwaysUseHtmlSuffix = true, string correlationID = null, CancellationToken cancellationToken = default, bool writeLogs = false, string fileSurfname = null)
		{
			// get original first
			var stopwatch = Stopwatch.StartNew();
			var portlet = theportlet.OriginalPortlet;
			var portletInfo = writeLogs
				? $"the '{theportlet.Title}' portlet [ID: {theportlet.ID}{(string.IsNullOrWhiteSpace(theportlet.OriginalPortletID) ? "" : $" - alias of '{portlet.Title}' (ID: {portlet.ID})")}]"
				: null;
			if (writeLogs)
				await this.WriteLogsAsync(correlationID, $"Start to generate HTML code of {portletInfo}", null, this.ServiceName, "Process.Http.Request").ConfigureAwait(false);

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
					title += $"<img src=\"{portlet.CommonSettings.IconURI}\"/>";
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
							var filename = $"{$"{portlet.Title}_{portlet.ID}".GetANSIUri()}{fileSurfname}";
							await Task.WhenAll
							(
								this.WriteLogsAsync(correlationID, $"HTML of {portletInfo} has been transformed\r\n- XML:\r\n{xml}\r\n- XSL:\r\n{xslTemplate}\r\n- XHTML:\r\n{content}", null, this.ServiceName, "Process.Http.Request"),
								UtilityService.WriteTextFileAsync(Path.Combine(Utility.TempFilesDirectory, $"{filename}.xml"), xml.ToString(), false, null, cancellationToken),
								UtilityService.WriteTextFileAsync(Path.Combine(Utility.TempFilesDirectory, $"{filename}.xsl"), xslTemplate, false, null, cancellationToken)
							).ConfigureAwait(false);
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
							await this.WriteLogsAsync(correlationID, $"Error occurred while transforming HTML of {portletInfo} => {e.Message}", e, this.ServiceName, "Process.Http.Request", LogLevel.Error).ConfigureAwait(false);
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
				await this.WriteLogsAsync(correlationID, $"HTML code of {portletInfo} has been generated - Execution times: {stopwatch.GetElapsedTimes()}", null, this.ServiceName, "Process.Http.Request").ConfigureAwait(false);

			return new Tuple<string, bool>(html, gotError);
		}

		async Task<Tuple<string, string, string, string>> GenerateDesktopAsync(Desktop desktop, Organization organization, Site site, JObject mainPortlet, string parentIdentity, string contentIdentity, bool writeLogs = false, string correlationID = null, CancellationToken cancellationToken = default)
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
				metaTags += $"<meta name=\"description\" property=\"og:description\" itemprop=\"description\" content=\"{description}\"/>";
			}

			if (!string.IsNullOrWhiteSpace(keywords))
				metaTags += $"<meta name=\"keywords\" content=\"{keywords.Replace("\"", "&quot;").Replace("<", "&lt;").Replace(">", "&gt;")}\"/>";

			metaTags += $"<meta name=\"twitter:title\" property=\"og:title\" itemprop=\"headline\" content=\"{title}\"/>";
			if (!string.IsNullOrWhiteSpace(description))
				metaTags += $"<meta name=\"twitter:description\" content=\"{description}\"/>";

			if (!string.IsNullOrWhiteSpace(coverURI))
				metaTags += $"<meta name=\"twitter:image\" property=\"og:image\" itemprop=\"thumbnailUrl\" content=\"{coverURI}\"/>";
			if (!string.IsNullOrWhiteSpace(desktop.CoverURI))
				metaTags += $"<meta name=\"twitter:image\" property=\"og:image\" itemprop=\"thumbnailUrl\" content=\"{desktop.CoverURI}\"/>";
			if (!string.IsNullOrWhiteSpace(site.CoverURI))
				metaTags += $"<meta name=\"twitter:image\" property=\"og:image\" itemprop=\"thumbnailUrl\" content=\"{site.CoverURI}\"/>";

			if (!string.IsNullOrWhiteSpace(desktop.IconURI))
				metaTags += $"<link rel=\"icon\" type=\"image/{(desktop.IconURI.IsEndsWith(".ico") ? "x-icon" : desktop.IconURI.IsEndsWith(".png") ? "png" : "jpeg")}\" href=\"{desktop.IconURI}\"/>"
					+ $"<link rel=\"shortcut icon\" type=\"image/{(desktop.IconURI.IsEndsWith(".ico") ? "x-icon" : desktop.IconURI.IsEndsWith(".png") ? "png" : "jpeg")}\" href=\"{desktop.IconURI}\"/>";
			if (!string.IsNullOrWhiteSpace(site.IconURI))
				metaTags += $"<link rel=\"icon\" type=\"image/{(site.IconURI.IsEndsWith(".ico") ? "x-icon" : site.IconURI.IsEndsWith(".png") ? "png" : "jpeg")}\" href=\"{site.IconURI}\"/>"
					+ $"<link rel=\"shortcut icon\" type=\"image/{(site.IconURI.IsEndsWith(".ico") ? "x-icon" : site.IconURI.IsEndsWith(".png") ? "png" : "jpeg")}\" href=\"{site.IconURI}\"/>";

			// add addtional meta tags of main portlet
			metaInfo?.Select(meta => (meta as JValue).Value.ToString()).Where(meta => !string.IsNullOrWhiteSpace(meta)).ForEach(meta => metaTags += meta);

			// add the required stylesheet libraries
			metaTags += $"<link rel=\"stylesheet\" href=\"{Utility.PortalsHttpURI}/_assets/default.css\"/>"
				+ $"<link rel=\"stylesheet\" href=\"{Utility.PortalsHttpURI}/_css/default.css\"/>";

			// add the stylesheet of the organization theme
			var organizationTheme = organization.Theme ?? "default";
			if (!"default".IsEquals(organizationTheme))
				metaTags += $"<link rel=\"stylesheet\" href=\"{Utility.PortalsHttpURI}/_css/{organizationTheme}.css\"/>";

			// add the stylesheet of the site theme
			var siteTheme = site.Theme ?? "default";
			if (!"default".IsEquals(siteTheme) && organizationTheme.IsEquals(siteTheme))
				metaTags += $"<link rel=\"stylesheet\" href=\"{Utility.PortalsHttpURI}/_css/{siteTheme}.css\"/>";

			// add the stylesheet of the desktop theme
			var desktopTheme = desktop.WorkingTheme ?? "default";
			if (!"default".IsEquals(desktopTheme) && organizationTheme.IsEquals(desktopTheme) && siteTheme.IsEquals(desktopTheme))
				metaTags += $"<link rel=\"stylesheet\" href=\"{Utility.PortalsHttpURI}/_css/{desktopTheme}.css\"/>";

			// add the stylesheet of the site
			if (!string.IsNullOrWhiteSpace(site.Stylesheets))
				metaTags += $"<link rel=\"stylesheet\" href=\"{Utility.PortalsHttpURI}/_css/s_{site.ID}.css\"/>";

			// add the stylesheet of the desktop
			if (!string.IsNullOrWhiteSpace(desktop.Stylesheets))
				metaTags += $"<link rel=\"stylesheet\" href=\"{Utility.PortalsHttpURI}/_css/d_{desktop.ID}.css\"/>";

			// add meta tags of the organization
			if (!string.IsNullOrWhiteSpace(organization.MetaTags))
				metaTags += organization.MetaTags;

			// add meta tags of the site
			if (!string.IsNullOrWhiteSpace(site.MetaTags))
				metaTags += site.MetaTags;

			// add meta tags of the desktop
			if (!string.IsNullOrWhiteSpace(desktop.MetaTags))
				metaTags += desktop.MetaTags;

			// add default scripts
			var scripts = $"<script src=\"{Utility.PortalsHttpURI}/_assets/default.js\"></script>";

			// add scripts of the default theme
			var directory = new DirectoryInfo(Path.Combine(Utility.DataFilesDirectory, "themes", "default", "js"));
			if (directory.Exists && this.AllowSrcResourceFiles)
				await directory.GetFiles("*.src").OrderBy(fileInfo => fileInfo.Name).ForEachAsync(async (fileInfo, _) =>
				{
					scripts += await UtilityService.ReadTextFileAsync(fileInfo, null, cancellationToken).ConfigureAwait(false) + "\r\n";
				}, cancellationToken, true, false).ConfigureAwait(false);
			scripts += $"<script src=\"{Utility.PortalsHttpURI}/_js/default.js\"></script>";

			// add scripts of the organization theme
			if (!"default".IsEquals(organizationTheme))
			{
				directory = new DirectoryInfo(Path.Combine(Utility.DataFilesDirectory, "themes", organizationTheme, "js"));
				if (directory.Exists && this.AllowSrcResourceFiles)
					await directory.GetFiles("*.src").OrderBy(fileInfo => fileInfo.Name).ForEachAsync(async (fileInfo, _) =>
					{
						scripts += await UtilityService.ReadTextFileAsync(fileInfo, null, cancellationToken).ConfigureAwait(false) + "\r\n";
					}, cancellationToken, true, false).ConfigureAwait(false);
				scripts += $"<script src=\"{Utility.PortalsHttpURI}/_js/{organizationTheme}.js\"></script>";
			}

			// add scripts of the site theme
			if (!"default".IsEquals(siteTheme) && organizationTheme.IsEquals(siteTheme))
			{
				directory = new DirectoryInfo(Path.Combine(Utility.DataFilesDirectory, "themes", siteTheme, "js"));
				if (directory.Exists && this.AllowSrcResourceFiles)
					await directory.GetFiles("*.src").OrderBy(fileInfo => fileInfo.Name).ForEachAsync(async (fileInfo, _) =>
					{
						scripts += await UtilityService.ReadTextFileAsync(fileInfo, null, cancellationToken).ConfigureAwait(false) + "\r\n";
					}, cancellationToken, true, false).ConfigureAwait(false);
				scripts += $"<script src=\"{Utility.PortalsHttpURI}/_js/{siteTheme}.js\"></script>";
			}

			// add scripts of the desktop theme
			if (!"default".IsEquals(desktopTheme) && organizationTheme.IsEquals(desktopTheme) && siteTheme.IsEquals(desktopTheme))
			{
				directory = new DirectoryInfo(Path.Combine(Utility.DataFilesDirectory, "themes", desktopTheme, "js"));
				if (directory.Exists && this.AllowSrcResourceFiles)
					await directory.GetFiles("*.src").OrderBy(fileInfo => fileInfo.Name).ForEachAsync(async (fileInfo, _) =>
					{
						scripts += await UtilityService.ReadTextFileAsync(fileInfo, null, cancellationToken).ConfigureAwait(false) + "\r\n";
					}, cancellationToken, true, false).ConfigureAwait(false);
				scripts += $"<script src=\"{Utility.PortalsHttpURI}/_js/{desktopTheme}.js\"></script>";
			}

			// add the scripts of the organization
			if (!string.IsNullOrWhiteSpace(organization.ScriptLibraries))
				scripts += organization.ScriptLibraries;

			if (!string.IsNullOrWhiteSpace(organization.Scripts))
				scripts += $"<script src=\"{Utility.PortalsHttpURI}/_js/o_{organization.ID}.js\"></script>";

			// add the scripts of the site
			if (!string.IsNullOrWhiteSpace(site.ScriptLibraries))
				scripts += site.ScriptLibraries;

			if (!string.IsNullOrWhiteSpace(site.Scripts))
				scripts += $"<script src=\"{Utility.PortalsHttpURI}/_js/s_{site.ID}.js\"></script>";

			// add the scripts of the desktop
			if (!string.IsNullOrWhiteSpace(desktop.ScriptLibraries))
				scripts += desktop.ScriptLibraries;

			if (!string.IsNullOrWhiteSpace(desktop.Scripts))
				scripts += $"<script src=\"{Utility.PortalsHttpURI}/_js/d_{desktop.ID}.js\"></script>";

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
				["main-portlet-action"] = mainPortletAction.ToLower()
			});

			return new Tuple<string, string, string, string>(title, metaTags, scripts, body);
		}

		string NormalizeDesktopHtml(string html, Uri requestURI, bool useShortURLs, Organization organization, Site site, Desktop desktop, string isMobile, string osInfo)
		{
			var homeDesktop = site.HomeDesktop != null ? $"\"{site.HomeDesktop.Alias}{(organization.AlwaysUseHtmlSuffix ? ".html" : "")}\"" : "undefined";
			var searchDesktop = site.SearchDesktop != null ? $"\"{site.SearchDesktop.Alias}{(organization.AlwaysUseHtmlSuffix ? ".html" : "")}\"" : "undefined";
			var language = "\"" + (desktop.Language ?? site.Language ?? "vi-VN") + "\"";
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
				["os-mode"] = osMode
			}).NormalizeURLs(requestURI, organization.Alias, useShortURLs);
		}

		JObject GenerateErrorJson(Exception exception, string correlationID, bool addErrorStack, string errorMessage = null)
		{
			var json = new JObject
			{
				{ "Code", (int)HttpStatusCode.InternalServerError },
				{ "Error", string.IsNullOrWhiteSpace(errorMessage) ? exception.Message : $"{errorMessage} => {exception.Message}" },
				{ "Type", exception.GetTypeName(true) }
			};
			if (exception is WampException wampException)
			{
				var details = wampException.GetDetails();
				json["Code"] = details.Item1;
				json["Error"] = string.IsNullOrWhiteSpace(errorMessage) ? details.Item2 : $"{errorMessage} => {details.Item2}";
				json["Type"] = details.Item3;
				if (addErrorStack)
					json["Stack"] = details.Item4;
			}
			else if (addErrorStack)
				json["Stack"] = exception.StackTrace;
			json["CorrelationID"] = correlationID;
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
				var isSystemAdministrator = await this.IsSystemAdministratorAsync(requestInfo).ConfigureAwait(false) || await this.IsAuthorizedAsync(requestInfo, "Organization", Components.Security.Action.Approve, cancellationToken).ConfigureAwait(false);
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
			// prepare
			var repositoryID = requestInfo.GetParameter("x-menu-repository-id");
			var repositoryEntityID = requestInfo.GetParameter("x-menu-repository-entity-id");
			var repositoryObjectID = requestInfo.GetParameter("x-menu-repository-object-id");
			if (!Int32.TryParse(requestInfo.GetParameter("x-menu-level") ?? "1", out var level))
				level = 1;
			if (!Int32.TryParse(requestInfo.GetParameter("x-menu-max-level") ?? "1", out var maxLevel))
				maxLevel = 0;

			// get the object
			var @object = await this.GetBusinessObjectAsync(repositoryEntityID, repositoryObjectID, cancellationToken).ConfigureAwait(false);
			if (@object == null)
				throw new InformationNotFoundException($"The requested menu is not found [Content-Type ID: {repositoryEntityID} - Menu ID: {repositoryObjectID}]");
			if (!(@object is INestedObject))
				throw new InformationInvalidException($"The requested menu is invalid (its not nested object) [Content-Type ID: {repositoryEntityID} - Menu ID: {repositoryObjectID}]");

			// check permission
			var gotRights = await this.IsSystemAdministratorAsync(requestInfo).ConfigureAwait(false) || await this.IsAuthorizedAsync(requestInfo, "Organization", Components.Security.Action.Approve, cancellationToken).ConfigureAwait(false) || requestInfo.Session.User.IsViewer(@object.WorkingPrivileges);
			if (!gotRights)
			{
				var organization = @object is IPortalObject
					? await ((@object as IPortalObject).OrganizationID ?? "").GetOrganizationByIDAsync(cancellationToken).ConfigureAwait(false)
					: (await (repositoryID ?? "").GetModuleByIDAsync(cancellationToken).ConfigureAwait(false))?.Organization;
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
			requestInfo.Header["x-thumbnails-as-png"] = $"{options.Get("ThumbnailsAsPng", options.Get("ThumbnailAsPng", options.Get("ShowPngThumbnails", options.Get("ShowAsPngThumbnails", false))))}".ToLower();
			requestInfo.Header["x-thumbnails-as-big"] = $"{options.Get("ThumbnailsAsBig", options.Get("ThumbnailAsBig", options.Get("ShowBigThumbnails", options.Get("ShowAsBigThumbnails", false))))}".ToLower();
			requestInfo.Header["x-thumbnails-width"] = $"{options.Get("ThumbnailsWidth", options.Get("ThumbnailWidth", 0))}";
			requestInfo.Header["x-thumbnails-height"] = $"{options.Get("ThumbnailsHeight", options.Get("ThumbnailHeight", 0))}";
			var thumbnails = children.Count == 1
				? await requestInfo.GetThumbnailsAsync(children[0].ID, children[0].Title.Url64Encode(), this.ValidationKey, cancellationToken).ConfigureAwait(false)
				: await requestInfo.GetThumbnailsAsync(children.Select(child => child.ID).Join(","), children.ToJObject("ID", child => new JValue(child.Title.Url64Encode())).ToString(Newtonsoft.Json.Formatting.None), this.ValidationKey, cancellationToken).ConfigureAwait(false);

			// generate and return the menu
			var menu = new JArray();
			await children.Where(child => child != null).OrderBy(child => child.OrderIndex).ForEachAsync(async (child, token) =>
			{
				if (child is Category category)
					menu.Add(await requestInfo.GenerateMenuAsync(category, thumbnails?.GetThumbnailURL(child.ID), level, maxLevel, token).ConfigureAwait(false));
				else if (child is Link link)
					menu.Add(await requestInfo.GenerateMenuAsync(link, thumbnails?.GetThumbnailURL(child.ID), level, maxLevel, token).ConfigureAwait(false));
			}, cancellationToken, true, false).ConfigureAwait(false);
			return menu;
		}
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
			// prepare
			if (message?.Type == null || message?.Data == null)
				return;

			var correlationID = UtilityService.NewUUID;
			if (this.IsDebugLogEnabled)
				this.WriteLogs(correlationID, $"Process an inter-communicate message\r\n{message?.ToJson()}");

			// messages of an organization
			if (message.Type.IsStartsWith("Organization#"))
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

			// messages of a CMS category
			else if (message.Type.IsStartsWith("Category#") || message.Type.IsStartsWith("CMS.Category#"))
				await message.ProcessInterCommunicateMessageOfCategoryAsync(cancellationToken).ConfigureAwait(false);
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
							await this.WriteLogsAsync(correlationID, $"Update the module definition into the collection of definitions\r\n{definition.ToJson()}", null, this.ServiceName, "CMS.Portals").ConfigureAwait(false);
					}
				}

				else if (message.Type.IsEquals("Cache#Clear"))
				{
					message.ClearCacheAsync(cancellationToken).Run(this.Logger);
					if (this.IsDebugLogEnabled)
						await this.WriteLogsAsync(correlationID, $"Clear the cached collection of objects of a content type successful\r\n{message.Data}", null, this.ServiceName, "CMS.Portals").ConfigureAwait(false);
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

	}
}
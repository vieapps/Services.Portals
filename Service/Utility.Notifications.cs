#region Related components
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using net.vieapps.Components.Caching;
using net.vieapps.Components.Repository;
using net.vieapps.Components.Security;
using net.vieapps.Components.Utility;
using net.vieapps.Services.Portals.Settings;
#endregion

namespace net.vieapps.Services.Portals
{
	public static partial class Utility
	{
		internal static string GetWebURL(this string url, string siteURL)
			=> url.Replace("~/", siteURL);

		internal static string GetAppURL(this string url)
			=> $"{Utility.PortalsCMSAppURI}/home?redirect={url.Url64Encode()}";

		/// <summary>
		/// Sends a notification when object was changed
		/// </summary>
		/// <param name="object"></param>
		/// <param name="event"></param>
		/// <param name="notificationSettings"></param>
		/// <param name="previousStatus"></param>
		/// <param name="status"></param>
		/// <param name="requestInfo"></param>
		/// <returns></returns>
		public static void SendNotification(this IPortalObject @object, string @event, Settings.Notifications notificationSettings, ApprovalStatus previousStatus, ApprovalStatus status, RequestInfo requestInfo = null)
			=> @object.SendNotificationAsync(@event, notificationSettings, previousStatus, status, requestInfo, Utility.CancellationToken).Execute();

		/// <summary>
		/// Sends a notification when object was changed
		/// </summary>
		/// <param name="object"></param>
		/// <param name="event"></param>
		/// <param name="notificationSettings"></param>
		/// <param name="previousStatus"></param>
		/// <param name="status"></param>
		/// <param name="requestInfo"></param>
		/// <param name="cancellationToken"></param>
		/// <returns></returns>
		public static async Task SendNotificationAsync(this IPortalObject @object, string @event, Settings.Notifications notificationSettings, ApprovalStatus previousStatus, ApprovalStatus status, RequestInfo requestInfo = null, CancellationToken cancellationToken = default)
		{
			requestInfo = requestInfo ?? new RequestInfo
			{
				ServiceName = Utility.ServiceName,
				ObjectName = @object.GetTypeName(true),
				Verb = @event
			};
			try
			{
				await requestInfo.SendNotificationAsync(@object, @event, notificationSettings, previousStatus, status, cancellationToken).ConfigureAwait(false);
			}
			catch (Exception exception)
			{
				await requestInfo.WriteErrorAsync(exception).ConfigureAwait(false);
			}
		}

		/// <summary>
		/// Sends a notification when object was changed
		/// </summary>
		/// <param name="requestInfo"></param>
		/// <param name="object"></param>
		/// <param name="event"></param>
		/// <param name="notificationSettings"></param>
		/// <param name="previousStatus"></param>
		/// <param name="status"></param>
		/// <param name="cancellationToken"></param>
		/// <param name="sendAppNotifications"></param>
		/// <param name="sendEmailNotifications"></param>
		/// <param name="sendWebHookNotifications"></param>
		/// <returns></returns>
		public static async Task SendNotificationAsync(this RequestInfo requestInfo, IPortalObject @object, string @event, Settings.Notifications notificationSettings, ApprovalStatus previousStatus, ApprovalStatus status, CancellationToken cancellationToken = default, bool sendAppNotifications = true, bool sendEmailNotifications = true, bool sendWebHookNotifications = true)
		{
			// check
			if (@object == null)
				return;

			// prepare settings
			var events = notificationSettings?.Events;
			var methods = notificationSettings?.Methods;
			var emails = notificationSettings?.Emails;
			var emailsByApprovalStatus = notificationSettings?.EmailsByApprovalStatus;
			var emailsWhenPublish = notificationSettings?.EmailsWhenPublish;
			var webhooks = notificationSettings?.WebHooks;
			var category = @object is Content content ? content.Category : null;
			var emailSettings = category != null ? category.EmailSettings : @object is Organization org ? org.EmailSettings : null;

			var parent = @object.Parent;
			while (parent != null)
			{
				Settings.Notifications parentNotificationSettings = null;
				if (parent is Category parentAsCategory)
				{
					parentNotificationSettings = parentAsCategory.Notifications;
					emailSettings = emailSettings ?? parentAsCategory.EmailSettings;
				}
				else if (parent is ContentType parentAsContentType)
				{
					parentNotificationSettings = parentAsContentType.Notifications;
					emailSettings = emailSettings ?? parentAsContentType.EmailSettings;
				}
				else if (parent is Module parentAsModule)
				{
					parentNotificationSettings = parentAsModule.Notifications;
					emailSettings = emailSettings ?? parentAsModule.EmailSettings;
				}
				else if (parent is Organization parentAsOrganization)
				{
					parentNotificationSettings = parentAsOrganization.Notifications;
					emailSettings = emailSettings ?? parentAsOrganization.EmailSettings;
				}

				events = events != null && events.Any() ? events : parentNotificationSettings?.Events;
				methods = methods != null && methods.Any() ? methods : parentNotificationSettings?.Methods;
				emails = emails ?? parentNotificationSettings?.Emails;
				emailsByApprovalStatus = emailsByApprovalStatus ?? parentNotificationSettings?.EmailsByApprovalStatus;
				emailsWhenPublish = emailsWhenPublish ?? parentNotificationSettings?.EmailsWhenPublish;
				webhooks = webhooks ?? parentNotificationSettings?.WebHooks;
				parent = parent.Parent;
			}

			var writeDebugLogs = requestInfo.IsWriteMessageLogs();
			var searchingEvent = "Create|Update|Delete".IsContains(@event) ? @event : "Update";
			var businessObject = @object is IBusinessObject bizObject ? bizObject : null;
			var contentType = businessObject?.ContentType as ContentType;
			var gotEvent = events != null && events.Any() && events.FirstOrDefault(evt => evt.IsEquals(searchingEvent)) != null;
			var gotContentTypeWebHooks = contentType?.WebHookNotifications != null && contentType.WebHookNotifications.Where(webhookNotification => webhookNotification != null).Any();

			// stop if has no event or web-hook
			if (!gotEvent && !gotContentTypeWebHooks)
			{
				if (writeDebugLogs)
					await requestInfo.WriteLogAsync($"Stop to send notification because no suitable event/method was found ({@object?.Title} [{@object?.GetType()}#{@object?.ID}]) => {@event} ({events?.Join(", ")} / {methods?.Join(", ")})", "Notifications").ConfigureAwait(false);
				return;
			}

			// prepare parameters
			var serviceName = requestInfo.ServiceName;
			var objectName = (businessObject as RepositoryBase)?.GetObjectName() ?? @object.GetTypeName(true);
			var organization = contentType?.Organization ?? await (@object.OrganizationID ?? "").GetOrganizationByIDAsync(cancellationToken).ConfigureAwait(false);
			if (organization != null && organization._siteIDs == null)
				await organization.FindSitesAsync(cancellationToken).ConfigureAwait(false);
			var site = organization?.DefaultSite;
			var siteDomain = site?.Host;
			var siteURL = $"{site?.GetURL()}/";

			// prepare notification settings			
			sendEmailNotifications = sendEmailNotifications && gotEvent && methods?.FirstOrDefault(method => method.IsEquals("Email")) != null;
			sendWebHookNotifications = sendWebHookNotifications && gotEvent && methods?.FirstOrDefault(method => method.IsEquals("WebHook")) != null && webhooks != null && webhooks.EndpointURLs != null && webhooks.EndpointURLs.Any();
			var emailNotifications = sendEmailNotifications && emailsByApprovalStatus != null && emailsByApprovalStatus.TryGetValue($"{status}", out var emailNotificationsByApprovalStatus) ? emailNotificationsByApprovalStatus : emails;
			if (writeDebugLogs)
				await requestInfo.WriteLogAsync($"Prepare to send notification ({@object?.Title} [{@object?.GetType()}#{@object?.ID}]) => App: {sendAppNotifications} - Email: {sendEmailNotifications || emailsWhenPublish != null} - WebHook: {sendWebHookNotifications || gotContentTypeWebHooks}", "Notifications").ConfigureAwait(false);

			// prepare sender & recipients
			var sender = @object is Form form
				? new JObject
					{
						{ "ID", "" },
						{ "Name", form.Name },
						{ "Email", form.Email }
					}
				: (await requestInfo.GetUserProfilesAsync([requestInfo.Session.User.ID], false, cancellationToken).ConfigureAwait(false) as JArray)?.FirstOrDefault();
			var recipientIDs = await @object.GetRecipientsAsync(status, organization, cancellationToken, [requestInfo.Session.User.ID]).ConfigureAwait(false);

			// send app notifications
			if (sendAppNotifications && recipientIDs.Any())
				try
				{
					var info = new JObject
					{
						{ "SystemID", @object.OrganizationID },
						{ "RepositoryID", businessObject?.RepositoryID },
						{ "RepositoryEntityID", businessObject?.RepositoryEntityID },
						{ "ObjectID", @object.ID },
						{ "Title", @object.Title },
						{ "Action", @event },
						{ "Status", $"{status}" },
						{ "PreviousStatus", $"{previousStatus}" }
					};
					await requestInfo.SendNotificationAsync(sender?.Get<string>("ID") ?? "", sender?.Get<string>("Name") ?? "Unknown", recipientIDs, info, cancellationToken).ConfigureAwait(false);
					if (writeDebugLogs)
						await requestInfo.WriteLogAsync($"Send app notifications successful\r\n{info}", "Notifications").ConfigureAwait(false);
				}
				catch (Exception exception)
				{
					await requestInfo.WriteErrorAsync(exception, $"Error occurred while sending app notifications ({@object?.Title} [{@object?.GetType()}#{@object?.ID}])", "Notifications").ConfigureAwait(false);
				}

			// send email notifications
			if (sendEmailNotifications || emailsWhenPublish != null)
			{
				var appURL = $"/portals/initializer?x-request={("{" + $"\"SystemID\":\"{organization?.ID}\",\"ObjectName\":\"{objectName}\",\"ObjectID\":\"{@object.ID}\"" + "}").Url64Encode()}".GetAppURL();
				var objectURL = businessObject?.GetURL() ?? $"~/index";
				var normalizedHTMLs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
				var definition = RepositoryMediator.GetEntityDefinition(@object.GetType());
				if (definition != null)
				{
					definition.Attributes.Where(attribute => attribute.IsCLOB != null && attribute.IsCLOB.Value).ForEach(attribute =>
					{
						var value = @object.GetAttributeValue<string>(attribute);
						normalizedHTMLs[attribute.Name] = value?.NormalizeHTML(organization?.FakeFilesHttpURI).NormalizeURLs(siteURL);
					});
					if (businessObject?.ExtendedProperties != null && definition.BusinessRepositoryEntities.TryGetValue(businessObject.RepositoryEntityID, out var repositiryEntity))
						repositiryEntity?.ExtendedPropertyDefinitions?.Where(propertyDefinition => propertyDefinition.Mode.Equals(ExtendedPropertyMode.LargeText)).ForEach(propertyDefinition =>
						{
							if (businessObject.ExtendedProperties.TryGetValue(propertyDefinition.Name, out var value))
								normalizedHTMLs[propertyDefinition.Name] = (value as string)?.NormalizeHTML(organization?.FakeFilesHttpURI).NormalizeURLs(siteURL);
						});
				}

				var @params = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
				{
					["Organization"] = organization?.ToJson(false, false, json =>
					{
						OrganizationProcessor.ExtraProperties.Concat(["Privileges"]).ForEach(name => json.Remove(name));
						json["AlwaysUseHtmlSuffix"] = organization.AlwaysUseHtmlSuffix;
					}),
					["Site"] = site?.ToJson(json =>
					{
						SiteProcessor.ExtraProperties.Concat(["Privileges"]).ForEach(name => json.Remove(name));
						json["Domain"] = siteDomain;
						json["URL"] = siteURL;
					}),
					["ContentTypeDefinition"] = contentType?.ContentTypeDefinition?.ToJson(),
					["ModuleDefinition"] = contentType?.ContentTypeDefinition?.ModuleDefinition?.ToJson(json =>
					{
						(json as JObject).Remove("ContentTypeDefinitions");
						(json as JObject).Remove("ObjectDefinitions");
					}),
					["Module"] = contentType?.Module?.ToJson(false, false, json => ModuleProcessor.ExtraProperties.Concat(["Privileges", "OriginalPrivileges"]).ForEach(name => json.Remove(name))),
					["ContentType"] = contentType?.ToJson(false, json => ContentTypeProcessor.ExtraProperties.Concat(["Privileges", "OriginalPrivileges", "ExtendedPropertyDefinitions", "ExtendedControlDefinitions", "StandardControlDefinitions"]).ForEach(name => json.Remove(name))),
					["ParentContentType"] = contentType?.GetParent()?.ToJson(false, json => ContentTypeProcessor.ExtraProperties.Concat(["Privileges", "OriginalPrivileges", "ExtendedPropertyDefinitions", "ExtendedControlDefinitions", "StandardControlDefinitions"]).ForEach(name => json.Remove(name))),
					["URLs"] = new JObject
					{
						{ "Public", objectURL.GetWebURL(siteURL) },
						{ "Portal", objectURL.GetWebURL($"{Utility.PortalsHttpURI}/~{organization?.Alias}/") },
						{ "Private", appURL },
						{ "Review", appURL }
					},
					["HTMLs"] = normalizedHTMLs,
					["Sender"] = new JObject
					{
						{ "ID", requestInfo.Session.User?.ID ?? "" },
						{ "Name", sender?.Get<string>("Name") ?? "Unknown" },
						{ "Email", sender?.Get<string>("Email") },
						{ "URL", $"/users/profiles/{(sender?.Get<string>("Name") ?? "Unknown").GetANSIUri()}?x-request={("{\"ID\":\"" + (requestInfo.Session.User?.ID ?? "") + "\"}").Url64Encode()}".GetAppURL() },
						{ "Location", await requestInfo.GetLocationAsync(cancellationToken).ConfigureAwait(false) },
						{ "IP", requestInfo.Session.IP },
						{ "AppName", requestInfo.Session.AppName },
						{ "AppPlatform", requestInfo.Session.AppPlatform }
					}
				};

				// add information of the CMS Category
				if (category != null)
					@params["Category"] = category.ToJson(false, false, json =>
					{
						CategoryProcessor.ExtraProperties.Concat(["Privileges", "OriginalPrivileges"]).ForEach(name => json.Remove(name));
						json["URL"] = category.GetURL().GetWebURL(siteURL);
					});

				// normalize parameters for evaluating
				var language = requestInfo.CultureCode ?? "vi-VN";
				Utility.Languages.TryGetValue(language, out var languages);
				@params = new Dictionary<string, object>(@params, StringComparer.OrdinalIgnoreCase)
				{
					{ "Event", @event },
					{ "Event-i18n", languages?.Get<string>($"events.{@event}") ?? @event },
					{ "ObjectName", objectName },
					{ "ObjectType", @object.GetTypeName() },
					{ "Status", $"{status}" },
					{ "Status-i18n", languages?.Get<string>($"status.approval.{status}") ?? $"{status}" },
					{ "PreviousStatus", $"{previousStatus}" },
					{ "PreviousStatus-i18n", languages?.Get<string>($"status.approval.{previousStatus}") ?? $"{previousStatus}" },
					{ "Signature", emailSettings?.Signature?.NormalizeHTMLBreaks() },
					{ "EmailSignature", emailSettings?.Signature?.NormalizeHTMLBreaks() }
				};

				var objectAsExpandoObject = (@object as RepositoryBase).ToJson(json => json.Remove("Privileges")).ToExpandoObject();
				var requestInfoAsExpandoObject = requestInfo.AsExpandoObject;
				var paramsAsExpandoObject = @params.ToExpandoObject();

				JObject instructions = null;
				if (sendEmailNotifications || (!@event.IsEquals("Delete") && businessObject != null && status.Equals(ApprovalStatus.Published) && !status.Equals(previousStatus) && emailsWhenPublish != null))
					try
					{
						instructions = JObject.Parse(await new Uri($"{Utility.APIsHttpURI}/statics/instructions/portals/{language}.json").FetchHttpAsync(cancellationToken).ConfigureAwait(false))?.Get<JObject>("notifications");
					}
					catch (Exception exception)
					{
						await requestInfo.WriteErrorAsync(exception, "Error occurred while fetching email instructions", "Emails").ConfigureAwait(false);
					}

				// send a normal email message (when the status was changed)
				if (sendEmailNotifications)
					try
					{
						var subject = emailNotifications?.Subject ?? instructions?.Get<JObject>("emailByApprovalStatus")?.Get<JObject>($"{status}")?.Get<string>("subject") ?? instructions?.Get<JObject>("email")?.Get<string>("subject");
						if (string.IsNullOrWhiteSpace(subject))
							subject = "[{{@params(Organization.Alias)}}]: \"{{@current(Title)}}\" was {{@toLower(@params(Event))}}d";

						var body = emailNotifications?.Body ?? instructions?.Get<JObject>("emailByApprovalStatus")?.Get<JObject>($"{status}")?.Get<string>("body") ?? instructions?.Get<JObject>("email")?.Get<string>("body");
						if (string.IsNullOrWhiteSpace(body))
							body = @"Hi,
							The content that titled as ""<b>{{@current(Title)}}</b>"" ({{@params(ObjectName)}} on <a href=""{{@params(Site.URL)}}"">{{@params(Site.Title)}}</a>) was {{@toLower(@params(Event))}}d by {{@params(Sender.Name)}}.
							You can reach that content by one of these URLs below:
							<ul>
								<li>Public website: <a href=""{{@params(URLs.Public)}}"">{{@params(URLs.Public)}}</a></li>
								<li>CMS portals: <a href=""{{@params(URLs.Portal)}}"">{{@params(URLs.Portal)}}</a></li>
								<li>CMS apps: <a href=""{{@params(URLs.Private)}}"">{{@params(URLs.Private)}}</a></li>
							</ul>
							{{@params(EmailSignature)}}";

						var recipients = recipientIDs.Any() ? await requestInfo.GetUserProfilesAsync(recipientIDs, false, cancellationToken).ConfigureAwait(false) as JArray ?? new() : new();
						var parameters = $"{subject}\r\n{body}".PrepareDoubleBracesParameters(objectAsExpandoObject, requestInfoAsExpandoObject, paramsAsExpandoObject);
						var message = new EmailMessage
						{
							From = emailSettings?.Sender,
							To = recipients.Select(recipient => recipient?.Get<string>("Email")).Where(email => !string.IsNullOrWhiteSpace(email)).Distinct(StringComparer.OrdinalIgnoreCase).Join(";") + (string.IsNullOrWhiteSpace(emailNotifications.ToAddresses) ? "" : $";{emailNotifications.ToAddresses}"),
							Cc = emailNotifications?.CcAddresses,
							Bcc = emailNotifications?.BccAddresses,
							Subject = subject.NormalizeHTMLBreaks().Format(parameters),
							Body = body.NormalizeHTMLBreaks().Format(parameters),
							SmtpServer = emailSettings?.Smtp?.Host,
							SmtpServerPort = emailSettings?.Smtp != null ? emailSettings.Smtp.Port : 0,
							SmtpServerEnableSsl = emailSettings?.Smtp != null && emailSettings.Smtp.EnableSsl,
							SmtpUsername = emailSettings?.Smtp?.User,
							SmtpPassword = emailSettings?.Smtp?.UserPassword,
							CorrelationID = requestInfo.CorrelationID
						};
						await Utility.MessagingService.SendEmailAsync(message, cancellationToken).ConfigureAwait(false);
						var log = "Add an email notification into queue successful" + "\r\n" +
							$"- ID: {message.ID}" + "\r\n" +
							$"- Object: {@object.Title} [{@object.GetType()}#{@object.ID}]" + "\r\n" +
							$"- Event: {@event}" + "\r\n" +
							$"- Status: {status} (previous: {previousStatus})" + "\r\n" +
							$"- Sender: {sender?.Get<string>("Name")} ({sender?.Get<string>("Email")})" + "\r\n" +
							$"- To: {message.To}" + (!string.IsNullOrWhiteSpace(message.Cc) ? $" / {message.Cc}" : "") + (!string.IsNullOrWhiteSpace(message.Bcc) ? $" / {message.Bcc}" : "") + "\r\n" +
							$"- Subject: {message.Subject}";
						if (writeDebugLogs)
							log += $"\r\n- Message: {message.ToJson()}";
						await requestInfo.WriteLogAsync(log, "Emails").ConfigureAwait(false);
					}
					catch (Exception exception)
					{
						await requestInfo.WriteErrorAsync(exception, $"Error occurred while adding an email notification into queue ({@object?.Title} [{@object?.GetType()}#{@object?.ID}])", "Emails").ConfigureAwait(false);
					}

				// send a special email message (when publish)
				if (!@event.IsEquals("Delete") && businessObject != null && status.Equals(ApprovalStatus.Published) && !status.Equals(previousStatus) && emailsWhenPublish != null)
					try
					{
						var subject = emailsWhenPublish?.Subject ?? instructions?.Get<JObject>("emailsWhenPublish")?.Get<string>("subject");
						if (string.IsNullOrWhiteSpace(subject))
							subject = "[{{@params(Organization.Alias)}}]: \"{{@current(Title)}}\" was published";

						var body = emailsWhenPublish?.Body ?? instructions?.Get<JObject>("emailsWhenPublish")?.Get<string>("body");
						if (string.IsNullOrWhiteSpace(body))
							body = @"Hi,
							The content that titled as ""<b>{{@current(Title)}}</b>"" ({{@params(ObjectName)}} on <a href=""{{@params(Site.URL)}}"">{{@params(Site.Title)}}</a>) was published by {{@params(Sender.Name)}}.
							You can reach that content by one of these URLs below:
							<ul>
								<li>Public website: <a href=""{{@params(URLs.Public)}}"">{{@params(URLs.Public)}}</a></li>
								<li>CMS portals: <a href=""{{@params(URLs.Portal)}}"">{{@params(URLs.Portal)}}</a></li>
								<li>CMS apps: <a href=""{{@params(URLs.Private)}}"">{{@params(URLs.Private)}}</a></li>
							</ul>
							{{@params(EmailSignature)}}";

						var parameters = $"{subject}\r\n{body}".PrepareDoubleBracesParameters(objectAsExpandoObject, requestInfoAsExpandoObject, paramsAsExpandoObject);
						var message = new EmailMessage
						{
							From = emailSettings?.Sender,
							To = emailsWhenPublish?.ToAddresses,
							Cc = emailsWhenPublish?.CcAddresses,
							Bcc = emailsWhenPublish?.BccAddresses,
							Subject = subject.NormalizeHTMLBreaks().Format(parameters),
							Body = body.NormalizeHTMLBreaks().Format(parameters),
							SmtpServer = emailSettings?.Smtp?.Host,
							SmtpServerPort = emailSettings?.Smtp != null ? emailSettings.Smtp.Port : 0,
							SmtpServerEnableSsl = emailSettings?.Smtp != null && emailSettings.Smtp.EnableSsl,
							SmtpUsername = emailSettings?.Smtp?.User,
							SmtpPassword = emailSettings?.Smtp?.UserPassword,
							CorrelationID = requestInfo.CorrelationID
						};
						await Utility.MessagingService.SendEmailAsync(message, cancellationToken).ConfigureAwait(false);
						var log = "Add an email notification (notify when publish) into queue successful" + "\r\n" +
							$"- ID: {message.ID}" + "\r\n" +
							$"- Object: {@object.Title} [{@object.GetType()}#{@object.ID}]" + "\r\n" +
							$"- Event: {@event}" + "\r\n" +
							$"- Status: {status} (previous: {previousStatus})" + "\r\n" +
							$"- Sender: {sender?.Get<string>("Name")} ({sender?.Get<string>("Email")})" + "\r\n" +
							$"- To: {message.To}" + (!string.IsNullOrWhiteSpace(message.Cc) ? $" / {message.Cc}" : "") + (!string.IsNullOrWhiteSpace(message.Bcc) ? $" / {message.Bcc}" : "") + "\r\n" +
							$"- Subject: {message.Subject}";
						if (writeDebugLogs)
							log += $"\r\n- Message: {message.ToJson()}";
						await requestInfo.WriteLogAsync(log, "Emails").ConfigureAwait(false);
					}
					catch (Exception exception)
					{
						await requestInfo.WriteErrorAsync(exception, $"Error occurred while adding an email notification (notify when publish) into queue ({@object?.Title} [{@object?.GetType()}#{@object?.ID}])", "Emails").ConfigureAwait(false);
					}
			}

			// send web-hook notifications
			var webhookNotifications = (contentType?.WebHookNotifications ?? []).Concat(sendWebHookNotifications ? [webhooks] : Array.Empty<Settings.WebHookNotification>()).Where(webhookNotification => webhookNotification != null).ToList();
			if (webhookNotifications.Any())
			{
				var header = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
				{
					{ "Event", @event },
					{ "Origin", Utility.APIsHttpURI },
					{ "X-Original-Event", @event },
					{ "X-Original-Status", $"{status}" },
					{ "X-Original-Previous-Status", $"{previousStatus}" },
					{ "X-Original-Developer-ID", requestInfo.Session.DeveloperID },
					{ "X-Original-App-ID", requestInfo.Session.AppID },
					{ "X-Original-Correlation-ID", requestInfo.CorrelationID },
					{ "X-Original-Service-Name", serviceName },
					{ "X-Original-Object-Name", objectName },
					{ "X-Original-Object-Type", @object.GetTypeName() },
					{ "X-Original-Object-ID", @object.ID },
					{ "X-Original-Object-Title", @object.Title },
					{ "X-Original-Organization-ID", organization?.ID },
					{ "X-Original-Organization-Title", organization?.Title },
					{ "X-Original-Site-ID", site?.ID },
					{ "X-Original-Site-Title", site?.Title },
					{ "X-Original-Site-Domain", siteDomain },
					{ "X-Original-Site-URL", siteURL }
				};
				if (contentType != null)
				{
					if (contentType.Module != null)
					{
						header["X-Original-Module-ID"] = contentType.Module.ID;
						header["X-Original-Module-Title"] = contentType.Module.Title;
					}
					header["X-Original-Content-Type-ID"] = contentType.ID;
					header["X-Original-Content-Type-Title"] = contentType.Title;
				}
				if (category != null)
				{
					header["X-Original-Category-ID"] = category.ID;
					header["X-Original-Category-Title"] = category.Title;
				}
				var bodyJson = (@object as RepositoryBase).ToJson(json => json.Remove("Privileges"));
				var requestInfoJson = requestInfo.AsJson;
				var paramsJson = new JObject
				{
					["Organization"] = organization?.ToJson(false, false, json => OrganizationProcessor.ExtraProperties.Concat(["Privileges", "OriginalPrivileges"]).ForEach(name => json.Remove(name))),
					["Module"] = contentType?.Module?.ToJson(json => ModuleProcessor.ExtraProperties.Concat(["Privileges", "OriginalPrivileges"]).ForEach(name => json.Remove(name))),
					["ContentType"] = contentType?.ToJson(json => ContentTypeProcessor.ExtraProperties.Concat(["Privileges", "OriginalPrivileges", "ExtendedPropertyDefinitions", "ExtendedControlDefinitions", "StandardControlDefinitions"]).ForEach(name => json.Remove(name))),
					["Category"] = category?.ToJson(json => CategoryProcessor.ExtraProperties.Concat(["Privileges", "OriginalPrivileges"]).ForEach(name => json.Remove(name)))
				};
				await webhookNotifications.ForEachAsync(async webhookNotification =>
				{
					try
					{
						bodyJson["ID"] = webhookNotification.GenerateIdentity ? @object.ID.GenerateUUID() : @object.ID;
						var body = "";
						try
						{
							body = string.IsNullOrWhiteSpace(webhookNotification.PrepareBodyScript)
								? bodyJson.ToString(Newtonsoft.Json.Formatting.None)
								: webhookNotification.PrepareBodyScript.JsEvaluate(bodyJson, requestInfoJson, paramsJson, Utility.JsFunctions, Utility.JsEmbedObjects, null, webhookNotification.PrepareBodyScriptTimeout)?.ToString() ?? bodyJson.ToString(Newtonsoft.Json.Formatting.None);
						}
						catch (Exception ex)
						{
							await requestInfo.WriteErrorAsync(ex, $"WebHook JS error => {ex.Message}\r\n\r\nSource Code:\r\n{webhookNotification.PrepareBodyScript}\r\n\r\nObject:\r\n{bodyJson}\r\n\r\nRequest:\r\n{requestInfoJson}\r\n\r\nParams:\r\n{paramsJson}", "WebHooks").ConfigureAwait(false);
							throw;
						}
						var doubleBracesTokens = body.GetDoubleBracesTokens();
						if (doubleBracesTokens.Any())
							body = body.Format(doubleBracesTokens.PrepareDoubleBracesParameters(bodyJson.ToExpandoObject(), requestInfoJson.ToExpandoObject(), paramsJson.ToExpandoObject()));

						var message = new WebHookMessage
						{
							EndpointURL = webhookNotification.EndpointURLs.First(),
							Header = webhookNotification.HeaderAsJson?.ToDictionary<string>(),
							Body = body,
							CorrelationID = requestInfo.CorrelationID
						}.Normalize(webhookNotification, requestInfo, @object.OrganizationID);

						await webhookNotification.EndpointURLs.ForEachAsync(async endpointURL =>
						{
							message.ID = message.Header["X-Original-Message-ID"] = UtilityService.NewUUID;
							message.EndpointURL = endpointURL;
							var sendAsCall = endpointURL.IsStartsWith($"{Utility.APIsHttpURI}/webhooks/{Utility.ServiceName}") && !endpointURL.IsContains("~forwarder") && !endpointURL.IsContains("?x-as-forwarder") && !endpointURL.IsContains("&x-as-forwarder");
							var log = (sendAsCall ? "Call the service to process a web-hook message" : "Add a web-hook notification into queue successful") + "\r\n" +
								$"- ID: {message.ID}" + "\r\n" +
								$"- Object: {@object.Title} [{@object.GetType()}#{@object.ID}]" + "\r\n" +
								$"- Event: {@event}" + "\r\n" +
								$"- Status: {status} (previous: {previousStatus})" + "\r\n" +
								$"- Endpoint URL: {message.EndpointURL}" + (writeDebugLogs ? $"\r\n- Message: {message.ToJson()}" : "");
							await Task.WhenAll
							(
								sendAsCall ? Task.CompletedTask : Utility.MessagingService.SendWebHookAsync(message, cancellationToken),
								requestInfo.WriteLogAsync(log, "WebHooks")
							).ConfigureAwait(false);
							if (sendAsCall)
								message.SendAsCallServiceAsync(Utility.CancellationToken).Execute(ex => requestInfo.WriteErrorAsync(ex, $"Error occurred while calling the service to process a web-hook notification message [{endpointURL}]", "WebHooks"));
						}, true, false).ConfigureAwait(false);
					}
					catch (Exception exception)
					{
						await requestInfo.WriteErrorAsync(exception, $"Error occurred while processing a web-hook notification ({@object?.Title} [{@object?.GetType()}#{@object?.ID}] - {webhookNotification?.EndpointURLs?.Join(" :: ") ?? "NULL"})", "WebHooks").ConfigureAwait(false);
					}
				}, true, false).ConfigureAwait(false);
			}
		}

		public static async Task<JToken> SendNotificationAsync(this RequestInfo requestInfo, IPortalObject @object, string @event, bool sendAppNotifications, bool sendEmailNotifications, bool sendWebHookNotifications, CancellationToken cancellationToken)
		{
			if (string.IsNullOrWhiteSpace(requestInfo.GetHeaderParameter("x-app-token")) && requestInfo.TryGetHeaderParameter("authorization", out var authenticateToken))
			{
				requestInfo.Header.Remove("authorization");
				try
				{
					var isBasicToken = authenticateToken.IsStartsWith("Basic");
					authenticateToken = isBasicToken || authenticateToken.IsStartsWith("Bearer") || authenticateToken.IsStartsWith("JWT") ? authenticateToken.ToArray(" ").Last() : null;
					if (authenticateToken != null)
					{
						var authorizeToken = await new RequestInfo(requestInfo.Session, "Users", "Token", "GET")
						{
							Query = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
							Header = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
							{
								["x-authorization-token"] = authenticateToken,
								["x-authorization-mode"] = isBasicToken ? "Basic" : "Bearer",
								["x-authorization-signature"] = authenticateToken.GetHMACSHA256(Utility.ValidationKey)
							},
							CorrelationID = requestInfo.CorrelationID
						}.CallServiceAsync(cancellationToken).ConfigureAwait(false);
						requestInfo.Header["x-app-token"] = authorizeToken.Get<string>("Token");
						requestInfo.Session.Fill(authorizeToken.Get<JObject>("Session"));
						if (requestInfo.ContainsKey("x-logs"))
							await requestInfo.WriteLogAsync($"The request was authorized (for sending notifications)\r\nToken: {authenticateToken} => {requestInfo.Header["x-app-token"]}", "Authentications").ConfigureAwait(false);
					}
				}
				catch (Exception ex)
				{
					await requestInfo.WriteErrorAsync(ex, $"Error occurred while authorizing (for sending notifications) => {ex.Message}", "Authentications").ConfigureAwait(false);
				}
			}

			ContentType contentType = null;
			ApprovalStatus status = ApprovalStatus.Published;
			@object ??= await requestInfo.GetObjectIdentity(true).GetBusinessObjectAsync<IBusinessObject>(requestInfo.GetParameter("RepositoryEntityID") ?? requestInfo.GetParameter("x-entity"), cancellationToken).ConfigureAwait(false) ?? throw new InvalidRequestException($"The request is invalid [({requestInfo.Verb}): {requestInfo.GetURI()}#404]");

			var businessObject = @object as IBusinessObject;
			if (businessObject != null)
			{
				contentType = businessObject.ContentType as ContentType;
				status = businessObject.Status;
			}

			var organization = await (@object.OrganizationID ?? "").GetOrganizationByIDAsync(cancellationToken).ConfigureAwait(false) ?? throw new InvalidRequestException($"The request is invalid [({requestInfo.Verb}): {requestInfo.GetURI()}#401]");

			var gotRights = await requestInfo.IsSystemAdministratorAsync(cancellationToken).ConfigureAwait(false);
			if (!gotRights)
				gotRights = requestInfo.Session.User.IsEditor(@object?.WorkingPrivileges, contentType?.WorkingPrivileges, organization);
			if (!gotRights)
				throw new AccessDeniedException();

			if (@object is Content content)
				await requestInfo.SendNotificationAsync(@object, @event, content.Category?.Notifications, status, status, cancellationToken, sendAppNotifications, sendEmailNotifications, sendWebHookNotifications).ConfigureAwait(false);
			else if (businessObject != null)
				await requestInfo.SendNotificationAsync(@object, @event, contentType?.Notifications, status, status, cancellationToken, sendAppNotifications, sendEmailNotifications, sendWebHookNotifications).ConfigureAwait(false);
			else
				await requestInfo.SendNotificationAsync(@object, @event, organization.Notifications, status, status, cancellationToken, sendAppNotifications, sendEmailNotifications, sendWebHookNotifications).ConfigureAwait(false);

			var response = new JObject();
			if (requestInfo.ContainsKey("x-recipients"))
			{
				var recipientIDs = await @object.GetRecipientsAsync(status, null, cancellationToken, new[] { requestInfo.Session.User.ID }).ConfigureAwait(false);
				response = new JObject
				{
					{ "ID", @object.ID },
					{ "Title", @object.Title },
					{ "Type", @object.GetTypeName(true) },
					{ "RecipientIDs", recipientIDs.Join(",") }
				};
			}
			return response;
		}

		public static async Task<JToken> SendNotificationAsync(this RequestInfo requestInfo, string objectIdentity, string @event, bool sendAppNotifications, bool sendEmailNotifications, bool sendWebHookNotifications, CancellationToken cancellationToken)
		{
			var @object = await (objectIdentity ?? requestInfo.GetObjectIdentity(true)).GetBusinessObjectAsync<IBusinessObject>(requestInfo.GetParameter("RepositoryEntityID") ?? requestInfo.GetParameter("x-entity"), cancellationToken).ConfigureAwait(false) ?? throw new InvalidRequestException($"The request is invalid [({requestInfo.Verb}): {requestInfo.GetURI()}#404]");
			return await requestInfo.SendNotificationAsync(@object, @event ?? "Update", sendAppNotifications, sendEmailNotifications, sendWebHookNotifications, cancellationToken).ConfigureAwait(false);
		}

		public static Task<JToken> SendNotificationAsync(this RequestInfo requestInfo, CancellationToken cancellationToken = default)
			=> requestInfo.SendNotificationAsync(requestInfo.GetObjectIdentity(true), "Update", "true".IsEquals(requestInfo.GetParameter("x-send-app-notifications")), "true".IsEquals(requestInfo.GetParameter("x-send-email-notifications")), "true".IsEquals(requestInfo.GetParameter("x-send-webhook-notifications")), cancellationToken);

		internal static string JsFunctions => @"
		var __sendNotification = function(requestInfo, object, event, sendAppNotifications, sendEmailNotifications, sendWebHookNotifications) {
			__sf_SendNotification(typeof requestInfo === 'string' ? requestInfo : JSON.stringify(requestInfo), typeof object === 'string' ? object : JSON.stringify(object), typeof event === 'string' ? event : 'Update', typeof sendAppNotifications === 'boolean' && true === sendAppNotifications, typeof sendEmailNotifications === 'boolean' && true === sendEmailNotifications, typeof sendWebHookNotifications === 'boolean' && true === sendWebHookNotifications);
		};
		".Replace("\t", "");

		internal static Dictionary<string, object> JsEmbedObjects => new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
		{
			["__sf_SendNotification"] = Utility.Func_SendNotification
		};

		static Action<string, string, string, bool, bool, bool> Func_SendNotification => (requestinfo, objectinfo, @event, sendAppNotifications, sendEmailNotifications, sendWebHookNotifications) =>
		{
			var correlationID = UtilityService.NewUUID;
			try
			{
				var requestJson = (requestinfo ?? "{}").ToJson();
				var objectJson = (objectinfo ?? "{}").ToJson();
				var requestInfo = new RequestInfo().CopyFrom(requestJson, null, request =>
				{
					request.Query = new Dictionary<string, string>(request.Query ?? [], StringComparer.OrdinalIgnoreCase);
					request.Header = new Dictionary<string, string>(request.Header ?? [], StringComparer.OrdinalIgnoreCase)
					{
						["RepositoryEntityID"] = objectJson.Get<string>("RepositoryEntityID")
					};
					request.CorrelationID ??= UtilityService.NewUUID;
				});
				correlationID = requestInfo.CorrelationID;
				requestInfo.SendNotificationAsync(objectJson.Get<string>("ID"), @event, sendAppNotifications, sendEmailNotifications, sendWebHookNotifications, Utility.CancellationToken).Execute(ex => Utility.WriteLogsAsync(null, null, "WebHooks", new List<string> { $"Error occurred while sending a notification => {ex.Message}" }, ex, correlationID));
			}
			catch (Exception ex)
			{
				Utility.WriteLogsAsync(null, null, "WebHooks", new List<string> { $"Error occurred while sending a notification => {ex.Message}" }, ex, correlationID).Execute();
			}
		};
	}
}
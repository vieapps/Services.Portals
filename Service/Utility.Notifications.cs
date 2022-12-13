#region Related components
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using net.vieapps.Components.Security;
using net.vieapps.Components.Caching;
using net.vieapps.Components.Repository;
using net.vieapps.Components.Utility;
#endregion

namespace net.vieapps.Services.Portals
{
	public static partial class Utility
	{
		internal static string GetWebURL(this string url, string siteURL)
			=> url.Replace("~/", siteURL);

		internal static string GetAppURL(this string url)
			=> $"{Utility.CmsPortalsHttpURI}/home?redirect={url.Url64Encode()}";

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
		/// <returns></returns>
		public static async Task SendNotificationAsync(this RequestInfo requestInfo, IPortalObject @object, string @event, Settings.Notifications notificationSettings, ApprovalStatus previousStatus, ApprovalStatus status, CancellationToken cancellationToken = default)
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
			var category = @object is Content ? (@object as Content).Category : null;
			var emailSettings = category != null ? category.EmailSettings : @object is Organization ? (@object as Organization).EmailSettings : null;

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

			var businessObject = @object is IBusinessObject ? @object as IBusinessObject : null;
			var contentType = businessObject?.ContentType as ContentType;

			// stop if has no event or web-hook
			var gotEvent = events != null && events.Any() && events.FirstOrDefault(e => e.IsEquals(@event)) == null;
			var gotContentTypeWebHooks = contentType?.WebHookNotifications != null && contentType.WebHookNotifications.Where(webhookNotification => webhookNotification != null).Any();
			if (!gotEvent && !gotContentTypeWebHooks)
				return;

			// prepare parameters
			var writeDebugLogs = requestInfo.IsWriteMessageLogs();
			var sender = (await requestInfo.GetUserProfilesAsync(new[] { requestInfo.Session.User.ID }, cancellationToken).ConfigureAwait(false) as JArray)?.FirstOrDefault();
			var serviceName = requestInfo.ServiceName;
			var objectName = (businessObject as RepositoryBase)?.GetObjectName() ?? @object.GetTypeName(true);
			var organization = contentType?.Organization ?? await (@object.OrganizationID ?? "").GetOrganizationByIDAsync(cancellationToken).ConfigureAwait(false);
			var alwaysUseHtmlSuffix = organization == null || organization.AlwaysUseHtmlSuffix;
			if (organization != null && organization._siteIDs == null)
				await organization.FindSitesAsync(cancellationToken).ConfigureAwait(false);
			var site = organization?.DefaultSite;
			var siteDomain = site?.Host;
			var siteURL = $"{site?.GetURL()}/";

			// prepare notification settings
			var sendEmailNotifications = gotEvent && methods?.FirstOrDefault(method => method.IsEquals("Email")) != null;
			var emailNotifications = sendEmailNotifications && emailsByApprovalStatus != null && emailsByApprovalStatus.TryGetValue($"{status}", out var emailNotificationsByApprovalStatus) ? emailNotificationsByApprovalStatus : emails;
			var sendWebHookNotifications = gotEvent && methods?.FirstOrDefault(method => method.IsEquals("WebHook")) != null && webhooks != null && webhooks.EndpointURLs != null && webhooks.EndpointURLs.Any();
			if (writeDebugLogs)
				await requestInfo.WriteLogAsync($"Prepare to send notification ({@object?.Title} [{@object?.GetType()}#{@object?.ID}]) => App: True - Email: {sendEmailNotifications || emailsWhenPublish != null} - WebHook: {sendWebHookNotifications || gotContentTypeWebHooks}", "Notifications").ConfigureAwait(false);

			// prepare recipients
			var recipientIDs = new List<string>();
			if (sendEmailNotifications)
				switch (status)
				{
					case ApprovalStatus.Draft:
					case ApprovalStatus.Rejected:
						recipientIDs = @object is Form
							? await @object.WorkingPrivileges.GetUserIDsAsync(PrivilegeRole.Editor, cancellationToken).ConfigureAwait(false)
							: new[] { @object.CreatedID }.ToList();
						break;

					case ApprovalStatus.Pending:
						recipientIDs = await @object.WorkingPrivileges.GetUserIDsAsync(PrivilegeRole.Editor, cancellationToken).ConfigureAwait(false);
						if (!recipientIDs.Any())
							recipientIDs = await @object.WorkingPrivileges.GetUserIDsAsync(PrivilegeRole.Moderator, cancellationToken).ConfigureAwait(false);
						if (!recipientIDs.Any())
							recipientIDs = await @object.WorkingPrivileges.GetUserIDsAsync(PrivilegeRole.Administrator, cancellationToken).ConfigureAwait(false);
						if (!recipientIDs.Any())
							recipientIDs = new[] { organization.OwnerID }.ToList();
						break;

					case ApprovalStatus.Approved:
						recipientIDs = await @object.WorkingPrivileges.GetUserIDsAsync(PrivilegeRole.Moderator, cancellationToken).ConfigureAwait(false);
						if (!recipientIDs.Any())
							recipientIDs = await @object.WorkingPrivileges.GetUserIDsAsync(PrivilegeRole.Administrator, cancellationToken).ConfigureAwait(false);
						if (!recipientIDs.Any())
							recipientIDs = new[] { organization.OwnerID }.ToList();
						break;

					case ApprovalStatus.Published:
						recipientIDs = (await @object.WorkingPrivileges.GetUserIDsAsync(PrivilegeRole.Moderator, cancellationToken).ConfigureAwait(false))
							.Concat(await @object.WorkingPrivileges.GetUserIDsAsync(PrivilegeRole.Administrator, cancellationToken).ConfigureAwait(false))
							.Concat(new[] { @object.CreatedID }).ToList();
						if (!recipientIDs.Any())
							recipientIDs = new[] { organization.OwnerID }.ToList();
						break;

					case ApprovalStatus.Archieved:
						recipientIDs = (await @object.WorkingPrivileges.GetUserIDsAsync(PrivilegeRole.Moderator, cancellationToken).ConfigureAwait(false))
							.Concat(await @object.WorkingPrivileges.GetUserIDsAsync(PrivilegeRole.Administrator, cancellationToken).ConfigureAwait(false))
							.Concat(new[] { @object.CreatedID, @object.LastModifiedID }).ToList();
						if (!recipientIDs.Any())
							recipientIDs = new[] { organization.OwnerID }.ToList();
						break;
				}

			recipientIDs = recipientIDs.Except(new[] { requestInfo.Session.User.ID }).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
			var recipients = recipientIDs.Any() ? await requestInfo.GetUserProfilesAsync(recipientIDs, cancellationToken).ConfigureAwait(false) as JArray : new JArray();

			//  send app notifications
			try
			{
				var baseMessage = new BaseMessage
				{
					Type = $"Portals#Notification#{@event}",
					Data = new JObject
					{
						{ "Action", @event },
						{ "Sender", new JObject
							{
								{ "ID", requestInfo.Session.User.ID },
								{ "Name", sender?.Get<string>("Name") ?? "Unknown" }
							}
						},
						{ "Info", new JObject
							{
								{ "ServiceName", serviceName },
								{ "ObjectName", objectName },
								{ "SystemID", @object.OrganizationID },
								{ "ObjectID", @object.ID },
								{ "ObjectTitle", @object.Title },
								{ "Status", $"{status}" },
								{ "PreviousStatus", $"{previousStatus}" },
								{ "Time", DateTime.Now },
							}
						}
					}
				};
				recipients.Select(recipient => recipient.Get<JArray>("Sessions"))
					.Where(sessions => sessions != null)
					.SelectMany(sessions => sessions.Select(session => session.Get<string>("DeviceID")))
					.ForEach(deviceID => new UpdateMessage(baseMessage) { DeviceID = deviceID }.Send());
				if (writeDebugLogs)
					await requestInfo.WriteLogAsync($"Send app notifications successful\r\n{baseMessage.ToJson()}", "Notifications").ConfigureAwait(false);
			}
			catch (Exception exception)
			{
				await requestInfo.WriteErrorAsync(exception, $"Error occurred while sending app notifications ({@object?.Title} [{@object?.GetType()}#{@object?.ID}])", "Notifications").ConfigureAwait(false);
			}

			// send email notifications
			if (sendEmailNotifications || emailsWhenPublish != null)
			{
				var appURL = $"/portals/initializer?x-request={("{" + $"\"SystemID\":\"{organization?.ID}\",\"ObjectName\":\"{objectName}\",\"ObjectID\":\"{@object.ID}\"" + "}").Url64Encode()}".GetAppURL();
				var normalizedHTMLs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
				var definition = RepositoryMediator.GetEntityDefinition(@object.GetType());
				if (definition != null)
				{
					definition.Attributes.Where(attribute => attribute.IsCLOB != null && attribute.IsCLOB.Value).ForEach(attribute =>
					{
						var value = @object.GetAttributeValue<string>(attribute);
						normalizedHTMLs[attribute.Name] = value?.NormalizeHTML().NormalizeURLs(siteURL);
					});
					if (businessObject?.ExtendedProperties != null && definition.BusinessRepositoryEntities.TryGetValue(businessObject.RepositoryEntityID, out var repositiryEntity))
						repositiryEntity?.ExtendedPropertyDefinitions?.Where(propertyDefinition => propertyDefinition.Mode.Equals(ExtendedPropertyMode.LargeText)).ForEach(propertyDefinition =>
						{
							if (businessObject.ExtendedProperties.TryGetValue(propertyDefinition.Name, out var value))
								normalizedHTMLs[propertyDefinition.Name] = (value as string)?.NormalizeHTML().NormalizeURLs(siteURL);
						});
				}

				var @params = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
				{
					["Organization"] = organization?.ToJson(false, false, json =>
					{
						OrganizationProcessor.ExtraProperties.Concat(new[] { "Privileges" }).ForEach(name => json.Remove(name));
						json["AlwaysUseHtmlSuffix"] = alwaysUseHtmlSuffix;
					}),
					["Site"] = site?.ToJson(json =>
					{
						SiteProcessor.ExtraProperties.Concat(new[] { "Privileges" }).ForEach(name => json.Remove(name));
						json["Domain"] = siteDomain;
						json["URL"] = siteURL;
					}),
					["ContentTypeDefinition"] = contentType?.ContentTypeDefinition?.ToJson(),
					["ModuleDefinition"] = contentType?.ContentTypeDefinition?.ModuleDefinition?.ToJson(json =>
					{
						(json as JObject).Remove("ContentTypeDefinitions");
						(json as JObject).Remove("ObjectDefinitions");
					}),
					["Module"] = contentType?.Module?.ToJson(false, false, json => new[] { "Privileges", "OriginalPrivileges" }.Concat(ModuleProcessor.ExtraProperties).ForEach(name => json.Remove(name))),
					["ContentType"] = contentType?.ToJson(false, json => new[] { "Privileges", "OriginalPrivileges", "ExtendedPropertyDefinitions", "ExtendedControlDefinitions", "StandardControlDefinitions" }.Concat(ContentTypeProcessor.ExtraProperties).ForEach(name => json.Remove(name))),
					["ParentContentType"] = contentType?.GetParent()?.ToJson(false, json => new[] { "Privileges", "OriginalPrivileges", "ExtendedPropertyDefinitions", "ExtendedControlDefinitions", "StandardControlDefinitions" }.Concat(ContentTypeProcessor.ExtraProperties).ForEach(name => json.Remove(name))),
					["URLs"] = new JObject
					{
						{ "Public", $"{businessObject?.GetURL() ?? $"~/index"}{(alwaysUseHtmlSuffix ? ".html" : "")}".GetWebURL(siteURL) },
						{ "Portal", $"{businessObject?.GetURL() ?? $"~/index"}{(alwaysUseHtmlSuffix ? ".html" : "")}".GetWebURL($"{Utility.PortalsHttpURI}/~{organization?.Alias}/") },
						{ "Private", appURL },
						{ "Review", appURL }
					},
					["HTMLs"] = normalizedHTMLs,
					["Sender"] = new JObject
					{
						{ "ID", requestInfo.Session.User.ID },
						{ "Name", sender?.Get<string>("Name") ?? "Unknown" },
						{ "Email", sender?.Get<string>("Email") },
						{ "URL", $"/users/profiles/{(sender?.Get<string>("Name") ?? "Unknown").GetANSIUri()}?x-request={("{\"ID\":\"" + requestInfo.Session.User.ID + "\"}").Url64Encode()}".GetAppURL() },
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
						new[] { "Privileges", "OriginalPrivileges" }.Concat(CategoryProcessor.ExtraProperties).ForEach(name => json.Remove(name));
						json["URL"] = $"{category.GetURL()}{(alwaysUseHtmlSuffix ? ".html" : "")}".GetWebURL(siteURL);
					}).ToExpandoObject();

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

				var objectAsExpandoObject = @object.ToExpandoObject();
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
						await requestInfo.WriteErrorAsync(exception, "Error occurred while fetching instructions", "Notifications").ConfigureAwait(false);
					}

				// send a normal email message (when the status was changed)
				if (sendEmailNotifications)
					try
					{
						var subject = emailNotifications?.Subject ?? instructions?.Get<JObject>("emailByApprovalStatus")?.Get<JObject>($"{status}")?.Get<string>("subject") ?? instructions?.Get<JObject>("email")?.Get<string>("subject");
						if (string.IsNullOrWhiteSpace(subject))
							subject = "[{{@params(Organization.Alias)}}] - \"{{@current(Title)}}\" was {{@toLower(@params(Event))}}d";

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

						var parameters = $"{subject}\r\n{body}".PrepareDoubleBracesParameters(objectAsExpandoObject, requestInfoAsExpandoObject, paramsAsExpandoObject);
						var message = new EmailMessage
						{
							From = emailSettings?.Sender,
							To = recipients.Select(recipient => recipient.Get<string>("Email")).Where(email => !string.IsNullOrWhiteSpace(email)).Join(";") + (string.IsNullOrWhiteSpace(emailNotifications.ToAddresses) ? "" : $";{emailNotifications.ToAddresses}"),
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
						await requestInfo.WriteLogAsync(log, "Notifications").ConfigureAwait(false);
					}
					catch (Exception exception)
					{
						await requestInfo.WriteErrorAsync(exception, $"Error occurred while adding an email notification into queue ({@object?.Title} [{@object?.GetType()}#{@object?.ID}])", "Notifications").ConfigureAwait(false);
					}

				// send a special email message (when publish)
				if (!@event.IsEquals("Delete") && businessObject != null && status.Equals(ApprovalStatus.Published) && !status.Equals(previousStatus) && emailsWhenPublish != null)
					try
					{
						var subject = emailsWhenPublish?.Subject ?? instructions?.Get<JObject>("emailsWhenPublish")?.Get<string>("subject");
						if (string.IsNullOrWhiteSpace(subject))
							subject = "[{{@params(Organization.Alias)}}] - \"{{@current(Title)}}\" was published";

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
						await requestInfo.WriteLogAsync(log, "Notifications").ConfigureAwait(false);
					}
					catch (Exception exception)
					{
						await requestInfo.WriteErrorAsync(exception, $"Error occurred while adding an email notification (notify when publish) into queue ({@object?.Title} [{@object?.GetType()}#{@object?.ID}])", "Notifications").ConfigureAwait(false);
					}
			}

			// send web-hook notifications
			var webhookNotifications = (contentType?.WebHookNotifications ?? new List<Settings.WebHookNotification>()).Concat(sendWebHookNotifications ? new[] { webhooks } : Array.Empty<Settings.WebHookNotification>()).Where(webhookNotification => webhookNotification != null).ToList();
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
					["Organization"] = organization?.ToJson(false, false, json => new[] { "Privileges", "OriginalPrivileges" }.Concat(OrganizationProcessor.ExtraProperties).ForEach(name => json.Remove(name))),
					["Module"] = contentType?.Module?.ToJson(json => new[] { "Privileges", "OriginalPrivileges" }.Concat(ModuleProcessor.ExtraProperties).ForEach(name => json.Remove(name))),
					["ContentType"] = contentType?.ToJson(json => new[] { "Privileges", "OriginalPrivileges", "ExtendedPropertyDefinitions", "ExtendedControlDefinitions", "StandardControlDefinitions" }.Concat(ContentTypeProcessor.ExtraProperties).ForEach(name => json.Remove(name))),
					["Category"] = category?.ToJson(json => new[] { "Privileges", "OriginalPrivileges" }.Concat(CategoryProcessor.ExtraProperties).ForEach(name => json.Remove(name)))
				};
				await webhookNotifications.ForEachAsync(async webhookNotification =>
				{
					try
					{
						bodyJson["ID"] = webhookNotification.GenerateIdentity ? @object.ID.GenerateUUID() : @object.ID;
						var body = string.IsNullOrWhiteSpace(webhookNotification.PrepareBodyScript)
							? bodyJson.ToString(Newtonsoft.Json.Formatting.None)
							: webhookNotification.PrepareBodyScript.JsEvaluate(bodyJson, requestInfoJson, paramsJson)?.ToString() ?? bodyJson.ToString(Newtonsoft.Json.Formatting.None);
						var doubleBracesTokens = body.GetDoubleBracesTokens();
						if (doubleBracesTokens.Any())
							body = body.Format(doubleBracesTokens.PrepareDoubleBracesParameters(bodyJson.ToExpandoObject(), requestInfoJson.ToExpandoObject(), paramsJson.ToExpandoObject()));
						var message = new WebHookMessage
						{
							EndpointURL = webhookNotification.EndpointURLs.First(),
							Header = webhookNotification.HeaderAsJson?.ToDictionary<string>(),
							Body = body,
							CorrelationID = requestInfo.CorrelationID
						}.Normalize(webhookNotification.SignAlgorithm, webhookNotification.SignKey ?? requestInfo.Session.AppID ?? @object.OrganizationID, webhookNotification.SignKeyIsHex, webhookNotification.SignatureName, webhookNotification.SignatureAsHex, webhookNotification.SignatureInQuery, webhookNotification.QueryAsJson?.ToDictionary<string>(), header, webhookNotification.EncryptionKey?.HexToBytes(), webhookNotification.EncryptionIV?.HexToBytes());
						await webhookNotification.EndpointURLs.ForEachAsync(async endpointURL =>
						{
							message.ID = message.Header["X-Original-Message-ID"] = UtilityService.NewUUID;
							message.EndpointURL = endpointURL;
							await Utility.MessagingService.SendWebHookAsync(message, cancellationToken).ConfigureAwait(false);
							var log = "Add a web-hook notification into queue successful" + "\r\n" +
								$"- ID: {message.ID}" + "\r\n" +
								$"- Object: {@object.Title} [{@object.GetType()}#{@object.ID}]" + "\r\n" +
								$"- Event: {@event}" + "\r\n" +
								$"- Status: {status} (previous: {previousStatus})" + "\r\n" +
								$"- Endpoint URL: {message.EndpointURL}" + (writeDebugLogs ? $"\r\n- Message: {message.ToJson()}" : "");
							await requestInfo.WriteLogAsync(log, "Notifications").ConfigureAwait(false);
						}, true, false).ConfigureAwait(false);
					}
					catch (Exception exception)
					{
						await requestInfo.WriteErrorAsync(exception, $"Error occurred while adding a web-hook notification into queue ({@object?.Title} [{@object?.GetType()}#{@object?.ID}] - {webhookNotification?.EndpointURLs?.Join(" :: ") ?? "NULL"})", "Notifications").ConfigureAwait(false);
					}
				}, true, false).ConfigureAwait(false);
			}
		}
	}
}
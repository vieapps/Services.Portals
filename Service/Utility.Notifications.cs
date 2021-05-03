#region Related components
using System;
using System.Linq;
using System.Dynamic;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
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
		/// Sends a notification when changed
		/// </summary>
		/// <param name="object"></param>
		/// <param name="event"></param>
		/// <param name="notificationSettings"></param>
		/// <param name="previousStatus"></param>
		/// <param name="status"></param>
		/// <param name="requestInfo"></param>
		/// <param name="cancellationToken"></param>
		/// <returns></returns>
		public static async Task SendNotificationAsync(this IPortalObject @object, string @event, Settings.Notifications notificationSettings, ApprovalStatus previousStatus, ApprovalStatus status, RequestInfo requestInfo, CancellationToken cancellationToken = default)
		{
			requestInfo = requestInfo ?? new RequestInfo();
			try
			{
				await requestInfo.SendNotificationAsync(@object, @event, notificationSettings, previousStatus, status, cancellationToken).ConfigureAwait(false);
			}
			catch (Exception exception)
			{
				await requestInfo.WriteErrorAsync(exception, cancellationToken).ConfigureAwait(false);
			}
		}

		static async Task SendNotificationAsync(this RequestInfo requestInfo, IPortalObject @object, string @event, Settings.Notifications notificationSettings, ApprovalStatus previousStatus, ApprovalStatus status, CancellationToken cancellationToken)
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
			var webHooks = notificationSettings?.WebHooks;
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

				events = events != null && events.Count > 0 ? events : parentNotificationSettings?.Events;
				methods = methods != null && methods.Count > 0 ? methods : parentNotificationSettings?.Methods;
				emails = emails ?? parentNotificationSettings?.Emails;
				emailsByApprovalStatus = emailsByApprovalStatus ?? parentNotificationSettings?.EmailsByApprovalStatus;
				emailsWhenPublish = emailsWhenPublish ?? parentNotificationSettings?.EmailsWhenPublish;
				webHooks = webHooks ?? parentNotificationSettings?.WebHooks;
				parent = parent.Parent;
			}

			// stop if has no event
			if (events == null || events.Count < 1 || events.FirstOrDefault(e => e.IsEquals(@event)) == null)
				return;

			// prepare parameters
			var sender = (await requestInfo.GetUserProfilesAsync(new[] { requestInfo.Session.User.ID }, cancellationToken).ConfigureAwait(false) as JArray)?.FirstOrDefault();
			var businessObject = @object is IBusinessObject ? @object as IBusinessObject : null;
			var serviceName = ServiceBase.ServiceComponent.ServiceName;
			var objectName = (businessObject as RepositoryBase)?.GetObjectName() ?? @object.GetTypeName(true);
			var contentType = businessObject?.ContentType as ContentType;
			var organization = await (@object.OrganizationID ?? "").GetOrganizationByIDAsync(cancellationToken).ConfigureAwait(false);
			var alwaysUseHtmlSuffix = organization == null || organization.AlwaysUseHtmlSuffix;
			if (organization != null && organization._siteIDs == null)
				await organization.FindSitesAsync(cancellationToken).ConfigureAwait(false);
			var site = organization?.Sites.FirstOrDefault();
			var siteDomain = $"{site?.SubDomain}.{site?.PrimaryDomain}".Replace("*.", "www.").Replace("www.www.", "www.");
			var siteURL = $"http{(site != null && site.AlwaysUseHTTPs ? "s" : "")}://{siteDomain}/";

			// prepare the recipients and notification settings
			var recipientIDs = new List<string>();
			var sendWebHookNotifications = methods?.FirstOrDefault(method => method.IsEquals("WebHook")) != null && webHooks != null && webHooks.EndpointURLs != null && webHooks.EndpointURLs.Count > 0;
			var sendEmailNotifications = methods?.FirstOrDefault(method => method.IsEquals("Email")) != null;
			var emailNotifications = new Settings.EmailNotifications();

			switch (status)
			{
				case ApprovalStatus.Draft:
					recipientIDs = new[] { @object.CreatedID }.ToList();
					emailNotifications = emailsByApprovalStatus != null && emailsByApprovalStatus.ContainsKey($"{status}")
						? emailsByApprovalStatus[$"{status}"]
						: emails;
					break;

				case ApprovalStatus.Pending:
					recipientIDs = await @object.WorkingPrivileges.GetUserIDsAsync(PrivilegeRole.Editor, cancellationToken).ConfigureAwait(false);
					if (recipientIDs.Count < 1)
						recipientIDs = await @object.WorkingPrivileges.GetUserIDsAsync(PrivilegeRole.Moderator, cancellationToken).ConfigureAwait(false);
					if (recipientIDs.Count < 1)
						recipientIDs = await @object.WorkingPrivileges.GetUserIDsAsync(PrivilegeRole.Administrator, cancellationToken).ConfigureAwait(false);
					if (recipientIDs.Count < 1)
						recipientIDs = new[] { organization.OwnerID }.ToList();
					emailNotifications = emailsByApprovalStatus != null && emailsByApprovalStatus.ContainsKey($"{status}")
						? emailsByApprovalStatus[$"{status}"]
						: emails;
					break;

				case ApprovalStatus.Rejected:
					recipientIDs = new[] { @object.CreatedID }.ToList();
					emailNotifications = emailsByApprovalStatus != null && emailsByApprovalStatus.ContainsKey($"{status}")
						? emailsByApprovalStatus[$"{status}"]
						: emails;
					break;

				case ApprovalStatus.Approved:
					recipientIDs = await @object.WorkingPrivileges.GetUserIDsAsync(PrivilegeRole.Moderator, cancellationToken).ConfigureAwait(false);
					if (recipientIDs.Count < 1)
						recipientIDs = await @object.WorkingPrivileges.GetUserIDsAsync(PrivilegeRole.Administrator, cancellationToken).ConfigureAwait(false);
					if (recipientIDs.Count < 1)
						recipientIDs = new[] { organization.OwnerID }.ToList();
					emailNotifications = emailsByApprovalStatus != null && emailsByApprovalStatus.ContainsKey($"{status}")
						? emailsByApprovalStatus[$"{status}"]
						: emails;
					break;

				case ApprovalStatus.Published:
					recipientIDs = (await @object.WorkingPrivileges.GetUserIDsAsync(PrivilegeRole.Moderator, cancellationToken).ConfigureAwait(false))
						.Concat(await @object.WorkingPrivileges.GetUserIDsAsync(PrivilegeRole.Administrator, cancellationToken).ConfigureAwait(false))
						.Concat(new[] { @object.CreatedID }).ToList();
					if (recipientIDs.Count < 1)
						recipientIDs = new[] { organization.OwnerID }.ToList();
					emailNotifications = emailsByApprovalStatus != null && emailsByApprovalStatus.ContainsKey($"{status}")
						? emailsByApprovalStatus[$"{status}"]
						: emails;
					break;

				case ApprovalStatus.Archieved:
					recipientIDs = (await @object.WorkingPrivileges.GetUserIDsAsync(PrivilegeRole.Moderator, cancellationToken).ConfigureAwait(false))
						.Concat(await @object.WorkingPrivileges.GetUserIDsAsync(PrivilegeRole.Administrator, cancellationToken).ConfigureAwait(false))
						.Concat(new[] { @object.CreatedID, @object.LastModifiedID }).ToList();
					if (recipientIDs.Count < 1)
						recipientIDs = new[] { organization.OwnerID }.ToList();
					emailNotifications = emailsByApprovalStatus != null && emailsByApprovalStatus.ContainsKey($"{status}")
						? emailsByApprovalStatus[$"{status}"]
						: emails;
					break;
			}

			// prepare recipients
			recipientIDs = recipientIDs.Except(new[] { requestInfo.Session.User.ID }).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
			var recipients = await requestInfo.GetUserProfilesAsync(recipientIDs, cancellationToken).ConfigureAwait(false) as JArray;

			//  send app notifications
			try
			{
				var baseMessage = new BaseMessage
				{
					Type = $"Portals#Notification#{@event}",
					Data = new JObject
					{
						{ "Sender", new JObject
							{
								{ "ID", requestInfo.Session.User.ID },
								{ "Name", sender?.Get<string>("Name") ?? "Unknown" }
							}
						},
						{ "Action", @event },
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
				await recipients.Select(recipient => recipient.Get<JArray>("Sessions"))
					.Where(sessions => sessions != null)
					.SelectMany(sessions => sessions.Select(session => session.Get<string>("DeviceID")))
					.ForEachAsync(deviceID => new UpdateMessage(baseMessage) { DeviceID = deviceID }.SendAsync()).ConfigureAwait(false);
				if (Utility.Logger.IsEnabled(LogLevel.Debug))
					await requestInfo.WriteLogAsync($"Send app notifications successful\r\n{baseMessage.ToJson()}", cancellationToken).ConfigureAwait(false);
			}
			catch (Exception exception)
			{
				await requestInfo.WriteErrorAsync(exception, cancellationToken, "Error occurred while sending app notifications").ConfigureAwait(false);
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

				var @params = new Dictionary<string, ExpandoObject>(StringComparer.OrdinalIgnoreCase)
				{
					["Organization"] = organization?.ToJson(false, false, json =>
					{
						OrganizationProcessor.ExtraProperties.ForEach(name => json.Remove(name));
						json.Remove("Privileges");
						json["AlwaysUseHtmlSuffix"] = alwaysUseHtmlSuffix;
					}).ToExpandoObject(),
					["Site"] = site?.ToJson(json =>
					{
						SiteProcessor.ExtraProperties.ForEach(name => json.Remove(name));
						json.Remove("Privileges");
						json["Domain"] = siteDomain;
						json["URL"] = siteURL;
					}).ToExpandoObject(),
					["ContentTypeDefinition"] = contentType?.ContentTypeDefinition?.ToJson().ToExpandoObject(),
					["ModuleDefinition"] = contentType?.ContentTypeDefinition?.ModuleDefinition?.ToJson(json =>
					{
						(json as JObject).Remove("ContentTypeDefinitions");
						(json as JObject).Remove("ObjectDefinitions");
					}).ToExpandoObject(),
					["Module"] = contentType?.Module?.ToJson(false, false, json =>
					{
						ModuleProcessor.ExtraProperties.ForEach(name => json.Remove(name));
						json.Remove("Privileges");
					}).ToExpandoObject(),
					["ContentType"] = contentType?.ToJson(false, json =>
					{
						ModuleProcessor.ExtraProperties.ForEach(name => json.Remove(name));
						json.Remove("Privileges");
					}).ToExpandoObject(),
					["ParentContentType"] = contentType?.GetParent()?.ToJson(false, json =>
					{
						ModuleProcessor.ExtraProperties.ForEach(name => json.Remove(name));
						json.Remove("Privileges");
					}).ToExpandoObject(),
					["URLs"] = new JObject
					{
						{ "Public", $"{businessObject?.GetURL() ?? $"~/index"}{(alwaysUseHtmlSuffix ? ".html" : "")}".GetWebURL(siteURL) },
						{ "Portal", $"{businessObject?.GetURL() ?? $"~/index"}{(alwaysUseHtmlSuffix ? ".html" : "")}".GetWebURL($"{Utility.PortalsHttpURI}/~{organization?.Alias}/") },
						{ "Private", appURL },
						{ "Review", appURL }
					}.ToExpandoObject(),
					["HTMLs"] = normalizedHTMLs.ToExpandoObject(),
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
					}.ToExpandoObject()
				};

				// add information of the CMS Category
				if (category != null)
					@params["Category"] = category.ToJson(false, false, json =>
					{
						CategoryProcessor.ExtraProperties.ForEach(name => json.Remove(name));
						json.Remove("Privileges");
						json["URL"] = $"{category.GetURL()}{(alwaysUseHtmlSuffix ? ".html" : "")}".GetWebURL(siteURL);
					}).ToExpandoObject();

				// normalize parameters for evaluating
				var language = requestInfo.CultureCode ?? "vi-VN";
				var languages = Utility.Languages.ContainsKey(language) ? Utility.Languages[language] : null;
				var requestExpando = requestInfo.ToExpandoObject(requestInfoAsExpandoObject =>
				{
					requestInfoAsExpandoObject.Set("Body", requestInfo.BodyAsExpandoObject);
					requestInfoAsExpandoObject.Get<ExpandoObject>("Header")?.Remove("x-app-token");
				});
				var objectExpando = @object.ToExpandoObject();
				var paramsExpando = new Dictionary<string, object>(@params.ToDictionary(kvp => kvp.Key, kvp => kvp.Value as object), StringComparer.OrdinalIgnoreCase)
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
				}.ToExpandoObject();

				JObject instructions = null;
				if (sendEmailNotifications || (!@event.IsEquals("Delete") && businessObject != null && status.Equals(ApprovalStatus.Published) && !status.Equals(previousStatus) && emailsWhenPublish != null))
					try
					{
						instructions = JObject.Parse(await UtilityService.FetchWebResourceAsync($"{Utility.APIsHttpURI}/statics/instructions/portals/{language}.json", cancellationToken).ConfigureAwait(false))?.Get<JObject>("notifications");
					}
					catch (Exception exception)
					{
						await requestInfo.WriteErrorAsync(exception, cancellationToken, "Error occurred while fetching instructions").ConfigureAwait(false);
					}

				// send email message
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
						var parameters = $"{subject}\r\n{body}"
							.GetDoubleBracesTokens()
							.Select(token => token.Item2)
							.Distinct(StringComparer.OrdinalIgnoreCase)
							.ToDictionary(token => token, token =>
							{
								return token.StartsWith("@[") && token.EndsWith("]")
									? Extensions.JsEvaluate(token.GetJsExpression(requestExpando, objectExpando, paramsExpando))
									: token.StartsWith("@")
										? token.Evaluate(new Tuple<ExpandoObject, ExpandoObject, ExpandoObject>(requestExpando, objectExpando, paramsExpando))
										: token;
							});
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
						if (Utility.Logger.IsEnabled(LogLevel.Debug))
							log += $"\r\n- Message: {message.ToJson()}";
						await requestInfo.WriteLogAsync(log, cancellationToken, "Notifications").ConfigureAwait(false);
					}
					catch (Exception exception)
					{
						await requestInfo.WriteErrorAsync(exception, cancellationToken, "Error occurred while adding an email notification into queue").ConfigureAwait(false);
					}

				// send special email message (when publish)
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
						var parameters = $"{subject}\r\n{body}"
							.GetDoubleBracesTokens()
							.Select(token => token.Item2)
							.Distinct(StringComparer.OrdinalIgnoreCase)
							.ToDictionary(token => token, token =>
							{
								return token.StartsWith("@[") && token.EndsWith("]")
									? Extensions.JsEvaluate(token.GetJsExpression(requestExpando, objectExpando, paramsExpando))
									: token.StartsWith("@")
										? token.Evaluate(new Tuple<ExpandoObject, ExpandoObject, ExpandoObject>(requestExpando, objectExpando, paramsExpando))
										: token;
							});
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
						if (Utility.Logger.IsEnabled(LogLevel.Debug))
							log += $"\r\n- Message: {message.ToJson()}";
						await requestInfo.WriteLogAsync(log, cancellationToken, "Notifications").ConfigureAwait(false);
					}
					catch (Exception exception)
					{
						await requestInfo.WriteErrorAsync(exception, cancellationToken, "Error occurred while adding an email notification (notify when publish) into queue").ConfigureAwait(false);
					}
			}

			// send web-hook notifications
			if (sendWebHookNotifications)
				try
				{
					var signAlgorithm = webHooks.SignAlgorithm ?? "SHA256";
					var signKey = webHooks.SignKey ?? requestInfo.Session.AppID ?? @object.OrganizationID;
					var query = webHooks.AdditionalQuery?.ToExpandoObject().ToDictionary(kvp => kvp.Key, kvp => kvp.Value as string) ?? new Dictionary<string, string>();
					var header = webHooks.AdditionalHeader?.ToExpandoObject().ToDictionary(kvp => kvp.Key, kvp => kvp.Value as string) ?? new Dictionary<string, string>();
					header = new Dictionary<string, string>(header, StringComparer.OrdinalIgnoreCase)
					{
						{ "Event", @event },
						{ "SystemID", organization?.ID },
						{ "ServiceName", ServiceBase.ServiceComponent.ServiceName },
						{ "ObjectName", objectName },
						{ "ObjectType", @object.GetTypeName() },
						{ "Status", $"{status}" },
						{ "PreviousStatus", $"{previousStatus}" },
						{ "DeveloperID", requestInfo.Session.DeveloperID },
						{ "AppID", requestInfo.Session.AppID },
						{ "SiteID", site?.ID },
						{ "SiteTitle", site?.Title },
						{ "SiteDomain", siteDomain },
						{ "SiteURL", siteURL },
						{ "OrganizationID", organization?.ID },
						{ "OrganizationTitle", organization?.Title },
						{ "ModuleID", contentType?.Module?.ID },
						{ "ModuleTitle", contentType?.Module?.Title },
						{ "ContentTypeID", contentType?.ID },
						{ "ContentTypeTitle", contentType?.Title }
					};

					var message = new WebHookMessage
					{
						EndpointURL = webHooks.EndpointURLs[0],
						Body = @object.ToJson(json =>
						{
							if (webHooks.GenerateIdentity)
							{
								var id = json.Get<string>("ID");
								if (!string.IsNullOrWhiteSpace(id))
									json["ID"] = id.GenerateUUID();
							}
						}).ToString(Newtonsoft.Json.Formatting.None),
						Query = query,
						Header = header,
						CorrelationID = requestInfo.CorrelationID
					}.Normalize(signAlgorithm, signKey, webHooks.SignatureName, webHooks.SignatureAsHex, webHooks.SignatureInQuery);

					await webHooks.EndpointURLs.ForEachAsync(async endpointURL =>
					{
						message.ID = UtilityService.NewUUID;
						message.EndpointURL = endpointURL;
						await Utility.MessagingService.SendWebHookAsync(message, cancellationToken).ConfigureAwait(false);
						var log = "Add a web-hook notification into queue successful" + "\r\n" +
							$"- ID: {message.ID}" + "\r\n" +
							$"- Object: {@object.Title} [{@object.GetType()}#{@object.ID}]" + "\r\n" +
							$"- Event: {@event}" + "\r\n" +
							$"- Status: {status} (previous: {previousStatus})" + "\r\n" +
							$"- Endpoint URL: {message.EndpointURL}";
						if (Utility.Logger.IsEnabled(LogLevel.Debug))
							log += $"\r\n- Message: {message.ToJson()}";
						await requestInfo.WriteLogAsync(log, cancellationToken, "Notifications").ConfigureAwait(false);
					}).ConfigureAwait(false);
				}
				catch (Exception exception)
				{
					await requestInfo.WriteErrorAsync(exception, cancellationToken, "Error occurred while adding a web-hook notification into queue").ConfigureAwait(false);
				}
		}
	}
}
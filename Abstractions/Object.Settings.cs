#region Related components
using System;
using System.Net;
using System.Linq;
using System.Dynamic;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Converters;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using net.vieapps.Components.Utility;
using net.vieapps.Components.Repository;
#endregion

namespace net.vieapps.Services.Portals.Settings
{
	public class EmailNotifications
	{
		public EmailNotifications() { }

		public string ToAddresses { get; set; }

		public string CcAddresses { get; set; }

		public string BccAddresses { get; set; }

		public string Subject { get; set; }

		public string Body { get; set; }

		public void Normalize()
		{
			this.ToAddresses = string.IsNullOrWhiteSpace(this.ToAddresses) ? null : this.ToAddresses.ToList(";", true).Where(emailAddress => emailAddress.GetMailAddress() != null).Join(";");
			this.CcAddresses = string.IsNullOrWhiteSpace(this.CcAddresses) ? null : this.CcAddresses.ToList(";", true).Where(emailAddress => emailAddress.GetMailAddress() != null).Join(";");
			this.BccAddresses = string.IsNullOrWhiteSpace(this.BccAddresses) ? null : this.BccAddresses.ToList(";", true).Where(emailAddress => emailAddress.GetMailAddress() != null).Join(";");
			this.Subject = string.IsNullOrWhiteSpace(this.Subject) ? null : this.Subject.Trim();
			this.Body = string.IsNullOrWhiteSpace(this.Body) ? null : this.Body.Trim();
		}
	}

	// ---------------------------------------------------------------

	public class WebHookNotifications : WebHook
	{
		public WebHookNotifications() : base() { }

		public List<string> EndpointURLs { get; set; } = new List<string>();

		public bool GenerateIdentity { get; set; } = false;

		public override void Normalize()
		{
			this.EndpointURLs = (this.EndpointURLs ?? new List<string>())
				.Where(url => !string.IsNullOrWhiteSpace(url))
				.Select(url => url.Replace("\t", "").Replace("\r", "").ToList("\n"))
				.SelectMany(urls => urls)
				.Where(url => !string.IsNullOrWhiteSpace(url) && (url.IsStartsWith("http://") || url.IsStartsWith("https://")))
				.ToList();
			this.EndpointURLs = this.EndpointURLs.Count < 1 ? null : this.EndpointURLs;
			base.Normalize();
		}
	}

	// ---------------------------------------------------------------

	public class Notifications
	{
		public Notifications() { }

		public List<string> Events { get; set; } = new List<string>();

		public List<string> Methods { get; set; } = new List<string>();

		public EmailNotifications Emails { get; set; } = new EmailNotifications();

		public Dictionary<string, EmailNotifications> EmailsByApprovalStatus { get; set; } = new Dictionary<string, EmailNotifications>();

		public EmailNotifications EmailsWhenPublish { get; set; } = new EmailNotifications();

		public WebHookNotifications WebHooks { get; set; } = new WebHookNotifications();

		public void Normalize()
		{
			this.Events = this.Events == null || this.Events.Count < 1 ? null : this.Events;
			this.Methods = this.Methods == null || this.Methods.Count < 1 ? null : this.Methods;
			this.Emails?.Normalize();
			this.Emails = this.Emails != null && string.IsNullOrWhiteSpace(this.Emails.ToAddresses) && string.IsNullOrWhiteSpace(this.Emails.CcAddresses) && string.IsNullOrWhiteSpace(this.Emails.BccAddresses) && string.IsNullOrWhiteSpace(this.Emails.Subject) && string.IsNullOrWhiteSpace(this.Emails.Body) ? null : this.Emails;
			this.EmailsByApprovalStatus = this.EmailsByApprovalStatus ?? new Dictionary<string, EmailNotifications>();
			this.EmailsByApprovalStatus.Keys.ToList().ForEach(key =>
			{
				var notification = this.EmailsByApprovalStatus[key];
				notification?.Normalize();
				this.EmailsByApprovalStatus[key] = notification != null && string.IsNullOrWhiteSpace(notification.ToAddresses) && string.IsNullOrWhiteSpace(notification.CcAddresses) && string.IsNullOrWhiteSpace(notification.BccAddresses) && string.IsNullOrWhiteSpace(notification.Subject) && string.IsNullOrWhiteSpace(notification.Body) ? null : notification;
			});
			this.EmailsByApprovalStatus = this.EmailsByApprovalStatus.Where(kvp => kvp.Value != null).ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
			this.EmailsByApprovalStatus = this.EmailsByApprovalStatus.Count < 1 ? null : this.EmailsByApprovalStatus;
			this.EmailsWhenPublish?.Normalize();
			this.EmailsWhenPublish = this.EmailsWhenPublish != null && string.IsNullOrWhiteSpace(this.EmailsWhenPublish.ToAddresses) && string.IsNullOrWhiteSpace(this.EmailsWhenPublish.CcAddresses) && string.IsNullOrWhiteSpace(this.EmailsWhenPublish.BccAddresses) && string.IsNullOrWhiteSpace(this.EmailsWhenPublish.Subject) && string.IsNullOrWhiteSpace(this.EmailsWhenPublish.Body) ? null : this.EmailsWhenPublish;
			this.WebHooks?.Normalize();
			this.WebHooks = this.WebHooks != null && this.WebHooks.EndpointURLs == null ? null : this.WebHooks;
		}
	}

	// ---------------------------------------------------------------

	public class Instruction
	{
		public Instruction() { }

		public string Subject { get; set; }

		[FormControl(ControlType = "TextArea")]
		public string Body { get; set; }

		public void Normalize()
		{
			this.Subject = string.IsNullOrWhiteSpace(this.Subject) ? null : this.Subject.Trim();
			this.Body = string.IsNullOrWhiteSpace(this.Body) ? null : this.Body.Trim();
		}

		public static Dictionary<string, Dictionary<string, Instruction>> Parse(ExpandoObject rawInstructions)
		{
			var parsedInstructions = new Dictionary<string, Dictionary<string, Instruction>>();
			rawInstructions?.ForEach(rawInstruction =>
			{
				var instructionsByLanguage = new Dictionary<string, Instruction>();
				(rawInstruction.Value as ExpandoObject)?.ForEach(kvp =>
				{
					var instructionData = kvp.Value as ExpandoObject;
					instructionsByLanguage[kvp.Key] = new Instruction { Subject = instructionData.Get<string>("Subject"), Body = instructionData.Get<string>("Body") };
				});
				parsedInstructions[rawInstruction.Key] = instructionsByLanguage;
			});
			return parsedInstructions;
		}
	}

	// ---------------------------------------------------------------

	public class RefreshUrls
	{
		public RefreshUrls() { }

		public List<string> Addresses { get; set; } = new List<string>();

		public int Interval { get; set; } = 15;

		public void Normalize()
		{
			this.Addresses = (this.Addresses ?? new List<string>())
				.Where(address => !string.IsNullOrWhiteSpace(address))
				.Select(address => address.Trim().Replace("\r", "").ToArray("\n"))
				.SelectMany(addresses => addresses)
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.ToList();
			this.Addresses = this.Addresses.Count < 1 ? null : this.Addresses;
			this.Interval = this.Interval < 1 ? 15 : this.Interval;
		}
	}

	// ---------------------------------------------------------------

	public class RedirectUrls
	{
		public RedirectUrls() { }

		public List<string> Addresses { get; set; } = new List<string>();

		public bool AllHttp404 { get; set; } = false;

		public void Normalize()
		{
			this.Addresses = (this.Addresses ?? new List<string>())
				.Where(address => !string.IsNullOrWhiteSpace(address))
				.Select(address => address.Trim().Replace("\r", "").ToArray("\n"))
				.SelectMany(addresses => addresses)
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.ToList();
			this.Addresses = this.Addresses.Count < 1 ? null : this.Addresses;
		}
	}

	// ---------------------------------------------------------------

	public class Smtp
	{
		public Smtp() { }

		public string Host { get; set; }

		public int Port { get; set; } = 25;

		public bool EnableSsl { get; set; } = false;

		public string User { get; set; }

		public string UserPassword { get; set; }

		public void Normalize()
		{
			this.Host = string.IsNullOrWhiteSpace(this.Host) ? null : this.Host.Trim();
			this.Port = this.Port > IPEndPoint.MinPort && this.Port < IPEndPoint.MaxPort ? this.Port : 25;
			this.User = string.IsNullOrWhiteSpace(this.User) ? null : this.User.Trim();
			this.UserPassword = string.IsNullOrWhiteSpace(this.UserPassword) ? null : this.UserPassword.Trim();
		}
	}

	public class Email
	{
		public Email() { }

		public string Sender { get; set; }

		public string Signature { get; set; }

		public Smtp Smtp { get; set; } = new Smtp();

		public void Normalize()
		{
			this.Sender = string.IsNullOrWhiteSpace(this.Sender) || this.Sender.GetMailAddress() == null ? null : this.Sender.Trim();
			this.Signature = string.IsNullOrWhiteSpace(this.Signature) ? null : this.Signature.Trim();
			this.Smtp?.Normalize();
			this.Smtp = string.IsNullOrWhiteSpace(this.Smtp?.Host) ? null : this.Smtp;
		}
	}

	public class WebHook
	{
		public WebHook() { }

		public string SignAlgorithm { get; set; } = "SHA256";

		public string SignKey { get; set; }

		public string SignatureName { get; set; }

		public bool SignatureAsHex { get; set; } = true;

		public bool SignatureInQuery { get; set; } = false;

		[FormControl(ControlType = "TextArea")]
		public string AdditionalQuery { get; set; }

		[FormControl(ControlType = "TextArea")]
		public string AdditionalHeader { get; set; }

		public virtual void Normalize()
		{
			if (string.IsNullOrWhiteSpace(this.AdditionalQuery))
				this.AdditionalQuery = null;
			else
				try
				{
					this.AdditionalQuery = JObject.Parse(this.AdditionalQuery) != null
						? this.AdditionalQuery = this.AdditionalQuery.Trim()
						: null;
				}
				catch
				{
					this.AdditionalQuery = null;
				}
			if (string.IsNullOrWhiteSpace(this.AdditionalHeader))
				this.AdditionalHeader = null;
			else
				try
				{
					this.AdditionalHeader = JObject.Parse(this.AdditionalHeader) != null
						? this.AdditionalHeader = this.AdditionalHeader.Trim()
						: null;
				}
				catch
				{
					this.AdditionalHeader = null;
				}
		}
	}

	// ---------------------------------------------------------------

	public sealed class HttpIndicator
	{
		public HttpIndicator() { }

		[FormControl(MaxLength = 100)]
		public string Name { get; set; }

		[FormControl(ControlType = "TextArea", MaxLength = 4000)]
		public string Content { get; set; }

		public void Normalize()
		{
			this.Name = string.IsNullOrWhiteSpace(this.Name) ? null : this.Name.Trim();
			this.Content = string.IsNullOrWhiteSpace(this.Content) ? null : this.Content.Trim();
		}
	}

	// ---------------------------------------------------------------

	public class UI
	{
		public UI() { }

		public string Padding { get; set; }

		public string Margin { get; set; }

		public string Width { get; set; }

		public string Height { get; set; }

		public string Color { get; set; }

		public string BackgroundColor { get; set; }

		[FormControl(ControlType = "Lookup")]
		public string BackgroundImageURI { get; set; }

		[FormControl(ControlType = "Select", SelectInterface = "popover", SelectValues = "repeat#;repeat-x#;repeat-y#;no-repeat#;initial#;inherit")]
		public string BackgroundImageRepeat { get; set; }

		public string BackgroundImagePosition { get; set; }

		[FormControl(ControlType = "Select", SelectInterface = "popover", SelectValues = "auto#;length#;cover#;contain#;initial#;inherit")]
		public string BackgroundImageSize { get; set; }

		public string Css { get; set; }

		public string Style { get; set; }

		public void Normalize(Action<UI> onCompleted = null)
		{
			this.Padding = string.IsNullOrWhiteSpace(this.Padding) ? null : this.Padding.Trim();
			this.Margin = string.IsNullOrWhiteSpace(this.Margin) ? null : this.Margin.Trim();
			this.Width = string.IsNullOrWhiteSpace(this.Width) ? null : this.Width.Trim();
			this.Height = string.IsNullOrWhiteSpace(this.Height) ? null : this.Height.Trim();
			this.Color = string.IsNullOrWhiteSpace(this.Color) ? null : this.Color.Trim();
			this.BackgroundColor = string.IsNullOrWhiteSpace(this.BackgroundColor) ? null : this.BackgroundColor.Trim();
			this.BackgroundImageURI = string.IsNullOrWhiteSpace(this.BackgroundImageURI) ? null : this.BackgroundImageURI.Trim();
			this.BackgroundImageRepeat = string.IsNullOrWhiteSpace(this.BackgroundImageRepeat) ? null : this.BackgroundImageRepeat.Trim();
			this.BackgroundImagePosition = string.IsNullOrWhiteSpace(this.BackgroundImagePosition) ? null : this.BackgroundImagePosition.Trim();
			this.BackgroundImageSize = string.IsNullOrWhiteSpace(this.BackgroundImageSize) ? null : this.BackgroundImageSize.Trim();
			this.Css = string.IsNullOrWhiteSpace(this.Css) ? null : this.Css.Trim();
			this.Style = string.IsNullOrWhiteSpace(this.Style) ? null : this.Style.Trim();
			onCompleted?.Invoke(this);
		}

		public string GetStyle()
		{
			var style = "";
			if (!string.IsNullOrWhiteSpace(this.Padding))
				style += $"padding:{this.Padding};";
			if (!string.IsNullOrWhiteSpace(this.Margin))
				style += $"margin:{this.Margin};";
			if (!string.IsNullOrWhiteSpace(this.Width))
				style += $"width:{this.Width};";
			if (!string.IsNullOrWhiteSpace(this.Height))
				style += $"height:{this.Height};";
			if (!string.IsNullOrWhiteSpace(this.Color))
				style += $"color:{this.Color};";
			if (!string.IsNullOrWhiteSpace(this.BackgroundColor))
				style += $"background-color:{this.BackgroundColor};";
			if (!string.IsNullOrWhiteSpace(this.BackgroundImageURI))
			{
				style += $"background-image:url({this.BackgroundImageURI});";
				if (!string.IsNullOrWhiteSpace(this.BackgroundImageRepeat))
					style += $"background-repeat:{this.BackgroundImageRepeat};";
				if (!string.IsNullOrWhiteSpace(this.BackgroundImagePosition))
					style += $"background-position:{this.BackgroundImagePosition};";
				if (!string.IsNullOrWhiteSpace(this.BackgroundImageSize))
					style += $"background-size:{this.BackgroundImageSize};";
			}
			if (!string.IsNullOrWhiteSpace(this.Style))
				style += this.Style;
			return style;
		}
	}

	// ---------------------------------------------------------------

	public enum SEOMode
	{
		PortletAndDesktopAndSite,
		SiteAndDesktopAndPortlet,
		PortletAndDesktop,
		DesktopAndPortlet,
		PortletAndSite,
		SiteAndPortlet,
		Portlet,
		Desktop
	}

	// ---------------------------------------------------------------

	public class SEOInfo
	{
		public SEOInfo() { }

		public string Title { get; set; }

		[FormControl(ControlType = "TextArea")]
		public string Description { get; set; }

		[FormControl(ControlType = "TextArea")]
		public string Keywords { get; set; }

		public void Normalize()
		{
			this.Title = string.IsNullOrWhiteSpace(this.Title) ? null : this.Title.Trim();
			this.Description = string.IsNullOrWhiteSpace(this.Description) ? null : this.Description.Trim();
			this.Keywords = string.IsNullOrWhiteSpace(this.Keywords) ? null : this.Keywords.Trim();
		}
	}

	// ---------------------------------------------------------------

	public class SEO
	{
		public SEO() { }

		public SEOInfo SEOInfo { get; set; } = new SEOInfo();

		[JsonConverter(typeof(StringEnumConverter)), BsonRepresentation(BsonType.String)]
		[FormControl(ControlType = "Select", SelectInterface = "popover", SelectValues = "PortletAndDesktopAndSite#;SiteAndDesktopAndPortlet#;PortletAndDesktop#;DesktopAndPortlet#;PortletAndSite#;SiteAndPortlet#;DesktopAndSite#;SiteAndDesktop#;Portlet#;Desktop")]
		public SEOMode? TitleMode { get; set; }

		[JsonConverter(typeof(StringEnumConverter)), BsonRepresentation(BsonType.String)]
		[FormControl(ControlType = "Select", SelectInterface = "popover", SelectValues = "PortletAndDesktopAndSite#;SiteAndDesktopAndPortlet#;PortletAndDesktop#;DesktopAndPortlet#;PortletAndSite#;SiteAndPortlet#;DesktopAndSite#;SiteAndDesktop#;Portlet#;Desktop")]
		public SEOMode? DescriptionMode { get; set; }

		[JsonConverter(typeof(StringEnumConverter)), BsonRepresentation(BsonType.String)]
		[FormControl(ControlType = "Select", SelectInterface = "popover", SelectValues = "PortletAndDesktopAndSite#;SiteAndDesktopAndPortlet#;PortletAndDesktop#;DesktopAndPortlet#;PortletAndSite#;SiteAndPortlet#;DesktopAndSite#;SiteAndDesktop#;Portlet#;Desktop")]
		public SEOMode? KeywordsMode { get; set; }

		public void Normalize()
		{
			this.SEOInfo?.Normalize();
			this.SEOInfo = this.SEOInfo != null && string.IsNullOrWhiteSpace(this.SEOInfo.Title) && string.IsNullOrWhiteSpace(this.SEOInfo.Description) && string.IsNullOrWhiteSpace(this.SEOInfo.Keywords) ? null : this.SEOInfo;
		}
	}
}
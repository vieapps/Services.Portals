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
	public class EmailNotification
	{
		public EmailNotification() { }

		public string ToAddresses { get; set; }

		public string CcAddresses { get; set; }

		public string BccAddresses { get; set; }

		public string Subject { get; set; }

		public string Body { get; set; }

		public EmailNotification Normalize()
		{
			this.ToAddresses = string.IsNullOrWhiteSpace(this.ToAddresses) ? null : this.ToAddresses.ToList(";", true).Where(emailAddress => emailAddress.GetMailAddress() != null).Join(";");
			this.CcAddresses = string.IsNullOrWhiteSpace(this.CcAddresses) ? null : this.CcAddresses.ToList(";", true).Where(emailAddress => emailAddress.GetMailAddress() != null).Join(";");
			this.BccAddresses = string.IsNullOrWhiteSpace(this.BccAddresses) ? null : this.BccAddresses.ToList(";", true).Where(emailAddress => emailAddress.GetMailAddress() != null).Join(";");
			this.Subject = string.IsNullOrWhiteSpace(this.Subject) ? null : this.Subject.Trim();
			this.Body = string.IsNullOrWhiteSpace(this.Body) ? null : this.Body.Trim();
			return string.IsNullOrWhiteSpace(this.ToAddresses) && string.IsNullOrWhiteSpace(this.CcAddresses) && string.IsNullOrWhiteSpace(this.BccAddresses) && string.IsNullOrWhiteSpace(this.Subject) && string.IsNullOrWhiteSpace(this.Body) ? null : this;
		}
	}

	// ---------------------------------------------------------------

	public class WebHookNotification : WebHook
	{
		public WebHookNotification() : base() { }

		public List<string> EndpointURLs { get; set; } = new List<string>();

		public bool SignatureInQuery { get; set; } = false;

		public override WebHookNotification Normalize(Action onCompleted = null)
		{
			base.Normalize();
			this.EndpointURLs = (this.EndpointURLs ?? new List<string>())
				.Where(url => !string.IsNullOrWhiteSpace(url))
				.Select(url => url.Replace("\t", "").Replace("\r", "").ToList("\n"))
				.SelectMany(urls => urls)
				.Where(url => !string.IsNullOrWhiteSpace(url) && (url.IsStartsWith("http://") || url.IsStartsWith("https://")))
				.ToList();
			this.EndpointURLs = this.EndpointURLs.Any() ? this.EndpointURLs : null;
			onCompleted?.Invoke();
			return this.EndpointURLs != null ? this : null;
		}
	}

	// ---------------------------------------------------------------

	public class WebHookSetting : WebHook
	{
		public WebHookSetting() : base() { }

		public string SecretToken { get; set; }

		public override WebHookSetting Normalize(Action onCompleted = null)
		{
			base.Normalize();
			this.SecretToken = string.IsNullOrWhiteSpace(this.SecretToken) ? null : this.SecretToken.Trim();
			onCompleted?.Invoke();
			return this;
		}
	}

	// ---------------------------------------------------------------

	public class Notifications
	{
		public Notifications() { }

		public List<string> Events { get; set; } = new List<string>();

		public List<string> Methods { get; set; } = new List<string>();

		public EmailNotification Emails { get; set; } = new EmailNotification();

		public Dictionary<string, EmailNotification> EmailsByApprovalStatus { get; set; } = new Dictionary<string, EmailNotification>();

		public EmailNotification EmailsWhenPublish { get; set; } = new EmailNotification();

		public WebHookNotification WebHooks { get; set; } = new WebHookNotification();

		public Notifications Normalize()
		{
			this.Events = this.Events != null && this.Events.Any() ? this.Events : null;
			this.Methods = this.Methods != null && this.Methods.Any() ? this.Methods : null;
			this.Emails = this.Emails?.Normalize();
			this.EmailsByApprovalStatus = this.EmailsByApprovalStatus?.Select(kvp => KeyValuePair.Create(kvp.Key, kvp.Value?.Normalize())).Where(kvp => kvp.Value != null).ToDictionary();
			this.EmailsByApprovalStatus = this.EmailsByApprovalStatus != null && this.EmailsByApprovalStatus.Any() ? this.EmailsByApprovalStatus : null;
			this.EmailsWhenPublish = this.EmailsWhenPublish?.Normalize();
			this.WebHooks = this.WebHooks?.Normalize();
			return this.Events == null && this.Methods == null && this.Emails == null && this.EmailsByApprovalStatus == null && this.EmailsWhenPublish == null && this.WebHooks == null ? null : this;
		}
	}

	// ---------------------------------------------------------------

	public class Instruction
	{
		public Instruction() { }

		public string Subject { get; set; }

		[FormControl(ControlType = "TextArea")]
		public string Body { get; set; }

		public Instruction Normalize()
		{
			this.Subject = string.IsNullOrWhiteSpace(this.Subject) ? null : this.Subject.Trim();
			this.Body = string.IsNullOrWhiteSpace(this.Body) ? null : this.Body.Trim();
			return string.IsNullOrWhiteSpace(this.Subject) && string.IsNullOrWhiteSpace(this.Body) ? null : this;
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

		public RefreshUrls Normalize()
		{
			this.Addresses = (this.Addresses ?? new List<string>())
				.Where(address => !string.IsNullOrWhiteSpace(address))
				.Select(address => address.Trim().Replace("\r", "").ToArray("\n"))
				.SelectMany(addresses => addresses)
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.ToList();
			this.Addresses = this.Addresses.Any() ? this.Addresses : null;
			this.Interval = this.Interval < 1 ? 15 : this.Interval;
			return this.Addresses != null ? this : null;
		}
	}

	// ---------------------------------------------------------------

	public class RedirectUrls
	{
		public RedirectUrls() { }

		public List<string> Addresses { get; set; } = new List<string>();

		public bool AllHttp404 { get; set; } = false;

		public RedirectUrls Normalize()
		{
			this.Addresses = (this.Addresses ?? new List<string>())
				.Where(address => !string.IsNullOrWhiteSpace(address))
				.Select(address => address.Trim().Replace("\r", "").ToArray("\n"))
				.SelectMany(addresses => addresses)
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.ToList();
			this.Addresses = this.Addresses.Any() ? this.Addresses : null;
			return this.Addresses != null ? this : null;
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

		public Smtp Normalize()
		{
			this.Host = string.IsNullOrWhiteSpace(this.Host) ? null : this.Host.Trim();
			this.Port = this.Port > IPEndPoint.MinPort && this.Port < IPEndPoint.MaxPort ? this.Port : 25;
			this.User = string.IsNullOrWhiteSpace(this.User) ? null : this.User.Trim();
			this.UserPassword = string.IsNullOrWhiteSpace(this.UserPassword) ? null : this.UserPassword.Trim();
			return string.IsNullOrWhiteSpace(this.Host) ? null : this;
		}
	}

	public class Email
	{
		public Email() { }

		public string Sender { get; set; }

		public string Signature { get; set; }

		public Smtp Smtp { get; set; } = new Smtp();

		public Email Normalize()
		{
			this.Sender = string.IsNullOrWhiteSpace(this.Sender) || this.Sender.GetMailAddress() == null ? null : this.Sender.Trim();
			this.Signature = string.IsNullOrWhiteSpace(this.Signature) ? null : this.Signature.Trim();
			this.Smtp = this.Smtp?.Normalize();
			return this.Sender == null && this.Signature == null && this.Smtp == null ? null : this;
		}
	}

	public class WebHook : WebHookInfo
	{
		public WebHook() : base() { }

		public bool GenerateIdentity { get; set; } = false;

		public virtual WebHook Normalize(Action onCompleted = null)
		{
			this.SignAlgorithm = string.IsNullOrWhiteSpace(this.SignAlgorithm) ? null : this.SignAlgorithm.Trim();
			this.SignKey = string.IsNullOrWhiteSpace(this.SignKey) ? null : this.SignKey.Trim();
			this.SignatureName = string.IsNullOrWhiteSpace(this.SignatureName) ? null : this.SignatureName.Trim();
			this.Query = string.IsNullOrWhiteSpace(this.Query) ? null : this.Query.Trim();
			this.Header = string.IsNullOrWhiteSpace(this.Header) ? null : this.Header.Trim();
			this.EncryptionKey = string.IsNullOrWhiteSpace(this.EncryptionKey) ? null : this.EncryptionKey.Trim();
			this.EncryptionIV = string.IsNullOrWhiteSpace(this.EncryptionIV) ? null : this.EncryptionIV.Trim();
			if (string.IsNullOrWhiteSpace(this.EncryptionKey) || string.IsNullOrWhiteSpace(this.EncryptionIV))
				this.EncryptionKey = this.EncryptionIV = null;
			this.PrepareBodyScript = string.IsNullOrWhiteSpace(this.PrepareBodyScript) ? null : this.PrepareBodyScript.Trim();
			onCompleted?.Invoke();
			return this;
		}

		public void Validate(JObject bodyJson, JObject requestInfoJson, JObject paramsJson)
		{
			if (this.SignKeyIsHex && !string.IsNullOrWhiteSpace(this.SignKey))
				try
				{
					this.SignKey = this.SignKey.HexToBytes().ToHex();
				}
				catch (Exception ex)
				{
					throw new InformationInvalidException($"SignKey => {ex.Message}", ex);
				}

			if (!string.IsNullOrWhiteSpace(this.Query))
				try
				{
					this.Query = this.QueryAsJson != null ? this.Query.Trim() : null;
				}
				catch (Exception ex)
				{
					throw new InformationInvalidException($"Query => {ex.Message}", ex);
				}

			if (!string.IsNullOrWhiteSpace(this.Header))
				try
				{
					this.Header = this.HeaderAsJson != null ? this.Header.Trim() : null;
				}
				catch (Exception ex)
				{
					throw new InformationInvalidException($"Header => {ex.Message}", ex);
				}

			if (!string.IsNullOrWhiteSpace(this.EncryptionKey) || !string.IsNullOrWhiteSpace(this.EncryptionIV))
				try
				{
					this.EncryptionKey = this.EncryptionKey.HexToBytes().ToHex();
				}
				catch (Exception ex)
				{
					throw new InformationInvalidException($"EncryptionKey => {ex.Message}", ex);
				}

			if (!string.IsNullOrWhiteSpace(this.EncryptionIV))
				try
				{
					this.EncryptionIV = this.EncryptionIV.HexToBytes().ToHex();
				}
				catch (Exception ex)
				{
					throw new InformationInvalidException($"EncryptionIV => {ex.Message}", ex);
				}

			if (!string.IsNullOrWhiteSpace(this.PrepareBodyScript))
				try
				{
					this.PrepareBodyScript.JsEvaluate(bodyJson, requestInfoJson, paramsJson);
				}
				catch (Exception ex)
				{
					throw new InformationInvalidException($"Script => {ex.Message}", ex);
				}
		}

		public void Validate(RequestInfo requestInfo, IPortalObject organization, IPortalModule module = null, IPortalContentType contentType = null, IBusinessObject @object = null)
			=> this.Validate(
				@object?.ToJson(null) as JObject ?? new JObject
				{
					["ID"] = "ID",
					["Title"] = "Title",
					["Body"] = new JObject { ["ID"] = "ID", ["Title"] = "Title" }
				},
				requestInfo?.AsJson as JObject ?? new JObject
				{
					["Session"] = new JObject(),
					["Verb"] = "GET",
					["Query"] = new JObject(),
					["Header"] = new JObject(),
					["Body"] = new JObject { ["ID"] = "ID", ["Title"] = "Title" }
				},
				new JObject
				{
					["Organization"] = organization?.ToJson(null),
					["Module"] = module?.ToJson(null),
					["ContentType"] = contentType?.ToJson(null)
				}
			);

		public void Validate(RequestInfo requestInfo, IPortalContentType contentType)
			=> this.Validate(requestInfo, contentType?.Organization, contentType?.Module, contentType);

		public void Validate(RequestInfo requestInfo, IBusinessObject @object)
			=> this.Validate(requestInfo, @object?.Organization, @object?.Module, @object?.ContentType, @object);
	}

	// ---------------------------------------------------------------

	public sealed class HttpIndicator
	{
		public HttpIndicator() { }

		[FormControl(MaxLength = 100)]
		public string Name { get; set; }

		[FormControl(ControlType = "TextArea")]
		public string Content { get; set; }

		public HttpIndicator Normalize()
		{
			this.Name = string.IsNullOrWhiteSpace(this.Name) ? null : this.Name.Trim();
			this.Content = string.IsNullOrWhiteSpace(this.Content) ? null : this.Content.Trim();
			return string.IsNullOrWhiteSpace(this.Name) && string.IsNullOrWhiteSpace(this.Content) ? null : this;
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

		public UI Normalize(Action<UI> onCompleted = null)
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
			return string.IsNullOrWhiteSpace(this.Padding) && string.IsNullOrWhiteSpace(this.Margin) && string.IsNullOrWhiteSpace(this.Width) && string.IsNullOrWhiteSpace(this.Height) && string.IsNullOrWhiteSpace(this.Color) && string.IsNullOrWhiteSpace(this.BackgroundColor) && string.IsNullOrWhiteSpace(this.BackgroundImageURI) && string.IsNullOrWhiteSpace(this.BackgroundImageRepeat) && string.IsNullOrWhiteSpace(this.BackgroundImagePosition) && string.IsNullOrWhiteSpace(this.BackgroundImageSize) && string.IsNullOrWhiteSpace(this.Css) && string.IsNullOrWhiteSpace(this.Style) ? null : this;
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

		public SEOInfo Normalize()
		{
			this.Title = string.IsNullOrWhiteSpace(this.Title) ? null : this.Title.Trim();
			this.Description = string.IsNullOrWhiteSpace(this.Description) ? null : this.Description.Trim();
			this.Keywords = string.IsNullOrWhiteSpace(this.Keywords) ? null : this.Keywords.Trim();
			return string.IsNullOrWhiteSpace(this.Title) && string.IsNullOrWhiteSpace(this.Description) && string.IsNullOrWhiteSpace(this.Keywords) ? null : this;
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

		public SEO Normalize()
		{
			this.SEOInfo = this.SEOInfo?.Normalize();
			return this.SEOInfo == null && this.TitleMode == null && this.DescriptionMode == null && this.KeywordsMode == null ? null : this;
		}
	}
}
using System;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using net.vieapps.Components.Utility;
namespace net.vieapps.Services.Portals.Settings
{
	[Serializable]
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
		}
	}

	[Serializable]
	public class WebHookNotifications
	{
		public WebHookNotifications() { }
		public List<string> EndpointURLs { get; set; } = new List<string>();
		public string SignAlgorithm { get; set; } = "SHA256";
		public string SignKey { get; set; }
		public string SignatureName { get; set; }
		public bool SignatureAsHex { get; set; } = true;
		public bool SignatureInQuery { get; set; } = false;
		public string AdditionalQuery { get; set; }
		public string AdditionalHeader { get; set; }
		public void Normalize()
		{
			this.EndpointURLs = (this.EndpointURLs ?? new List<string>()).Where(url => !string.IsNullOrWhiteSpace(url) && (url.IsStartsWith("http://") || url.IsStartsWith("https://"))).ToList();
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

	[Serializable]
	public class Notifications
	{
		public Notifications() { }
		public List<string> Events { get; set; } = new List<string>();
		public List<string> Methods { get; set; } = new List<string>();
		public EmailNotifications Emails { get; set; } = new EmailNotifications();
		public WebHookNotifications WebHooks { get; set; } = new WebHookNotifications();
	}

	[Serializable]
	public class Instruction
	{
		public Instruction() { }
		public string Subject { get; set; }
		public string Body { get; set; }
	}

	[Serializable]
	public class RefreshUrls
	{
		public RefreshUrls() { }
		public List<string> Addresses { get; set; } = new List<string>();
		public int Interval { get; set; } = 15;
		public void Normalize()
		{
			this.Addresses = (this.Addresses ?? new List<string>()).Where(url => !string.IsNullOrWhiteSpace(url)).ToList();
			this.Interval = this.Interval < 1 ? 15 : this.Interval;
		}
	}

	[Serializable]
	public class RedirectUrls
	{
		public RedirectUrls() { }
		public List<string> Addresses { get; set; } = new List<string>();
		public bool AllHttp404 { get; set; } = false;
		public void Normalize()
			=> this.Addresses = (this.Addresses ?? new List<string>()).Where(url => !string.IsNullOrWhiteSpace(url)).ToList();
	}

	[Serializable]
	public class Smtp
	{
		public Smtp() { }
		public string Host { get; set; }
		public int Port { get; set; } = 25;
		public bool EnableSsl { get; set; } = false;
		public string User { get; set; }
		public string UserPassword { get; set; }
	}

	[Serializable]
	public class Email
	{
		public Email() { }
		public string Sender { get; set; }
		public string Signature { get; set; }
		public Smtp Smtp { get; set; } = new Smtp();
		public void Normalize()
		{
			this.Sender = string.IsNullOrWhiteSpace(this.Sender) || this.Sender.GetMailAddress() == null ? null : this.Sender.Trim();
			this.Signature = this.Signature?.Trim();
		}
	}

}
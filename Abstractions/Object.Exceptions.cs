using System;
using System.Runtime.Serialization;
using net.vieapps.Components.Utility;

namespace net.vieapps.Services.Portals.Exceptions
{
	[Serializable]
	public class AliasIsExistedException : AppException
	{
		public AliasIsExistedException() : this("The alias was used by other") { }

		public AliasIsExistedException(string message) : base(message) { }

		public AliasIsExistedException(string message, Exception innerException) : base(message, innerException) { }
	}

	[Serializable]
	public class TemplateIsInvalidException : AppException
	{
		public TemplateIsInvalidException() : this("The XHTML template is invalid") { }

		public TemplateIsInvalidException(Exception innerException) : base($"The XHTML template is invalid => {innerException.Message}", innerException) { }

		public TemplateIsInvalidException(string message) : base(message) { }

		public TemplateIsInvalidException(string message, Exception innerException) : base(message, innerException) { }
	}

	[Serializable]
	public class XslTemplateIsInvalidException : TemplateIsInvalidException
	{
		public XslTemplateIsInvalidException() : this("The XSL template is invalid") { }

		public XslTemplateIsInvalidException(Exception innerException) : base($"The XSL template is invalid => {innerException.Message}", innerException) { }

		public XslTemplateIsInvalidException(string message) : base(message) { }

		public XslTemplateIsInvalidException(string message, Exception innerException) : base(message, innerException) { }
	}

	[Serializable]
	public class XslTemplateExecutionIsProhibitedException : AppException
	{
		public XslTemplateExecutionIsProhibitedException() : this("Execution of the 'document()' function and inline script was prohibited. Please set the 'EnableDocumentFunctionAndInlineScripts' of the options to true to allow its.") { }

		public XslTemplateExecutionIsProhibitedException(Exception innerException) : base("Execution of the 'document()' function and inline script was prohibited. Please set the 'EnableDocumentFunctionAndInlineScripts' of the options to true to allow its.", innerException) { }

		public XslTemplateExecutionIsProhibitedException(string message) : base(message) { }

		public XslTemplateExecutionIsProhibitedException(string message, Exception innerException) : base(message, innerException) { }
	}

	[Serializable]
	public class XslTemplateIsNotCompiledException : AppException
	{
		public XslTemplateIsNotCompiledException() : this("Please use the extension objects (via arguments) while transforming, because the XSLT engine requires a pre-compiled .DLL file to execute XSL's inline scripts (that is impossible at the run-time)") { }

		public XslTemplateIsNotCompiledException(Exception innerException) : base("Please use the extension objects (via arguments) while transforming, because the XSLT engine requires a pre-compiled .DLL file to execute XSL's inline scripts (that is impossible at the run-time)", innerException) { }

		public XslTemplateIsNotCompiledException(string message) : base(message) { }

		public XslTemplateIsNotCompiledException(string message, Exception innerException) : base(message, innerException) { }
	}

	[Serializable]
	public class MetaTagsAreInvalidException : AppException
	{
		public MetaTagsAreInvalidException() : this("The meta-tags are invalid") { }

		public MetaTagsAreInvalidException(Exception innerException) : base($"The meta-tags are invalid => {innerException.Message}", innerException) { }

		public MetaTagsAreInvalidException(string message) : base(message) { }

		public MetaTagsAreInvalidException(string message, Exception innerException) : base(message, innerException) { }
	}

	[Serializable]
	public class OptionsAreInvalidException : AppException
	{
		public OptionsAreInvalidException() : this("The options are invalid") { }

		public OptionsAreInvalidException(Exception innerException) : base($"The options are invalid => {innerException.Message}", innerException) { }

		public OptionsAreInvalidException(string message) : base(message) { }

		public OptionsAreInvalidException(string message, Exception innerException) : base(message, innerException) { }
	}

	[Serializable]
	public class SiteNotRecognizedException : AppException
	{
		public SiteNotRecognizedException() : this("The requested site is not recognized") { }

		public SiteNotRecognizedException(Exception innerException) : base($"The requested site is not recognized => {innerException.Message}", innerException) { }

		public SiteNotRecognizedException(string message) : base(message) { }

		public SiteNotRecognizedException(string message, Exception innerException) : base(message, innerException) { }
	}

	[Serializable]
	public class DesktopNotFoundException : AppException
	{
		public DesktopNotFoundException() : this("The requested desktop is not found") { }

		public DesktopNotFoundException(Exception innerException) : base($"The requested desktop is not found => {innerException.Message}", innerException) { }

		public DesktopNotFoundException(string message) : base(message) { }

		public DesktopNotFoundException(string message, Exception innerException) : base(message, innerException) { }
	}
}
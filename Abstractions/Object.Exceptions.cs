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

		public AliasIsExistedException(SerializationInfo info, StreamingContext context) : base(info, context) { }
	}

	[Serializable]
	public class TemplateIsInvalidException : AppException
	{
		public TemplateIsInvalidException() : this("The XHTML template is invalid") { }

		public TemplateIsInvalidException(Exception innerException) : base($"The XHTML template is invalid => {innerException.Message}", innerException) { }

		public TemplateIsInvalidException(string message) : base(message) { }

		public TemplateIsInvalidException(string message, Exception innerException) : base(message, innerException) { }

		public TemplateIsInvalidException(SerializationInfo info, StreamingContext context) : base(info, context) { }
	}

	[Serializable]
	public class MetaTagsAreInvalidException : AppException
	{
		public MetaTagsAreInvalidException() : this("The meta-tags are invalid") { }

		public MetaTagsAreInvalidException(Exception innerException) : base($"The meta-tags are invalid => {innerException.Message}", innerException) { }

		public MetaTagsAreInvalidException(string message) : base(message) { }

		public MetaTagsAreInvalidException(string message, Exception innerException) : base(message, innerException) { }

		public MetaTagsAreInvalidException(SerializationInfo info, StreamingContext context) : base(info, context) { }
	}

	[Serializable]
	public class ScriptsAreInvalidException : AppException
	{
		public ScriptsAreInvalidException() : this("The scripts are invalid") { }

		public ScriptsAreInvalidException(Exception innerException) : base($"The scripts are invalid => {innerException.Message}", innerException) { }

		public ScriptsAreInvalidException(string message) : base(message) { }

		public ScriptsAreInvalidException(string message, Exception innerException) : base(message, innerException) { }

		public ScriptsAreInvalidException(SerializationInfo info, StreamingContext context) : base(info, context) { }
	}

	[Serializable]
	public class OptionsAreInvalidException : AppException
	{
		public OptionsAreInvalidException() : this("The options are invalid") { }

		public OptionsAreInvalidException(Exception innerException) : base($"The options are invalid => {innerException.Message}", innerException) { }

		public OptionsAreInvalidException(string message) : base(message) { }

		public OptionsAreInvalidException(string message, Exception innerException) : base(message, innerException) { }

		public OptionsAreInvalidException(SerializationInfo info, StreamingContext context) : base(info, context) { }
	}

	[Serializable]
	public class DesktopNotFounndException : AppException
	{
		public DesktopNotFounndException() : this("The requested desktop is not found") { }

		public DesktopNotFounndException(Exception innerException) : base($"The requested desktop is not found => {innerException.Message}", innerException) { }

		public DesktopNotFounndException(string message) : base(message) { }

		public DesktopNotFounndException(string message, Exception innerException) : base(message, innerException) { }

		public DesktopNotFounndException(SerializationInfo info, StreamingContext context) : base(info, context) { }
	}
}
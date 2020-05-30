using System;
using net.vieapps.Components.Repository;
using net.vieapps.Components.Utility;

namespace net.vieapps.Services.Portals.Portlets
{
	[Serializable]
	public class CommonSettings
	{
		public CommonSettings() { }

		[FormControl(ControlType = "TextArea")]
		public string Template { get; set; }

		public bool HideTitle { get; set; } = false;

		[FormControl(DataType = "url")]
		public string TitleURL { get; set; }

		[FormControl(ControlType = "Lookup")]
		public string IconURI { get; set; }

		public Settings.UI TitleUISettings { get; set; }

		public Settings.UI ContentUISettings { get; set; }

		public void Normalize(Action<CommonSettings> onCompleted = null)
		{
			this.Template = string.IsNullOrWhiteSpace(this.Template) ? null : this.Template.Trim();
			this.TitleURL = string.IsNullOrWhiteSpace(this.TitleURL) ? null : this.TitleURL.Trim();
			this.IconURI = string.IsNullOrWhiteSpace(this.IconURI) ? null : this.IconURI.Trim();
			this.TitleUISettings?.Normalize();
			this.TitleUISettings = this.TitleUISettings != null && string.IsNullOrWhiteSpace(this.TitleUISettings.Padding) && string.IsNullOrWhiteSpace(this.TitleUISettings.Margin) && string.IsNullOrWhiteSpace(this.TitleUISettings.Width) && string.IsNullOrWhiteSpace(this.TitleUISettings.Height) && string.IsNullOrWhiteSpace(this.TitleUISettings.Color) && string.IsNullOrWhiteSpace(this.TitleUISettings.BackgroundColor) && string.IsNullOrWhiteSpace(this.TitleUISettings.BackgroundImageURI) && string.IsNullOrWhiteSpace(this.TitleUISettings.BackgroundImageRepeat) && string.IsNullOrWhiteSpace(this.TitleUISettings.BackgroundImagePosition) && string.IsNullOrWhiteSpace(this.TitleUISettings.BackgroundImageSize) && string.IsNullOrWhiteSpace(this.TitleUISettings.Css) && string.IsNullOrWhiteSpace(this.TitleUISettings.Style) ? null : this.TitleUISettings;
			this.ContentUISettings?.Normalize();
			this.ContentUISettings = this.ContentUISettings != null && string.IsNullOrWhiteSpace(this.ContentUISettings.Padding) && string.IsNullOrWhiteSpace(this.ContentUISettings.Margin) && string.IsNullOrWhiteSpace(this.ContentUISettings.Width) && string.IsNullOrWhiteSpace(this.ContentUISettings.Height) && string.IsNullOrWhiteSpace(this.ContentUISettings.Color) && string.IsNullOrWhiteSpace(this.ContentUISettings.BackgroundColor) && string.IsNullOrWhiteSpace(this.ContentUISettings.BackgroundImageURI) && string.IsNullOrWhiteSpace(this.ContentUISettings.BackgroundImageRepeat) && string.IsNullOrWhiteSpace(this.ContentUISettings.BackgroundImagePosition) && string.IsNullOrWhiteSpace(this.ContentUISettings.BackgroundImageSize) && string.IsNullOrWhiteSpace(this.ContentUISettings.Css) && string.IsNullOrWhiteSpace(this.ContentUISettings.Style) ? null : this.ContentUISettings;
			onCompleted?.Invoke(this);
		}
	}

	[Serializable]
	public class ListSettings
	{
		public ListSettings() { }

		[FormControl(ControlType = "TextArea")]
		public string Template { get; set; }

		[FormControl(ControlType = "Lookup")]
		public string ExpressionID { get; set; }

		public int PageSize { get; set; } = 7;

		public bool AutoPageNumber { get; set; } = true;

		[FormControl(ControlType = "TextArea")]
		public string Options { get; set; }

		public bool ShowBreadcrumbs { get; set; } = true;

		public bool ShowPagination { get; set; } = true;

		public void Normalize(Action<ListSettings> onCompleted = null)
		{
			this.Template = string.IsNullOrWhiteSpace(this.Template) ? null : this.Template.Trim();
			this.ExpressionID = string.IsNullOrWhiteSpace(this.ExpressionID) ? null : this.ExpressionID;
			this.Options = string.IsNullOrWhiteSpace(this.Options) ? null : this.Options.Trim();
			onCompleted?.Invoke(this);
		}
	}

	[Serializable]
	public class ViewSettings
	{
		public ViewSettings() { }

		[FormControl(ControlType = "TextArea")]
		public string Template { get; set; }

		[FormControl(ControlType = "TextArea")]
		public string Options { get; set; }

		public bool ShowBreadcrumbs { get; set; } = true;

		public bool ShowPagination { get; set; } = true;

		public void Normalize(Action<ViewSettings> onCompleted = null)
		{
			this.Template = string.IsNullOrWhiteSpace(this.Template) ? null : this.Template.Trim();
			this.Options = string.IsNullOrWhiteSpace(this.Options) ? null : this.Options.Trim();
			onCompleted?.Invoke(this);
		}
	}

	[Serializable]
	public class PaginationSettings
	{
		public PaginationSettings() { }

		[FormControl(ControlType = "TextArea")]
		public string Template { get; set; }

		public string PreviousPageLabel { get; set; } = "Previous";

		public string NextPageLabel { get; set; } = "Next";

		public string CurrentPageLabel { get; set; } = "Page:";

		public bool ShowPageLinks { get; set; } = true;

		public void Normalize(Action<PaginationSettings> onCompleted = null)
		{
			this.Template = string.IsNullOrWhiteSpace(this.Template) ? null : this.Template.Trim();
			this.PreviousPageLabel = string.IsNullOrWhiteSpace(this.PreviousPageLabel) ? null : this.PreviousPageLabel.Trim();
			this.NextPageLabel = string.IsNullOrWhiteSpace(this.NextPageLabel) ? null : this.NextPageLabel.Trim();
			this.CurrentPageLabel = string.IsNullOrWhiteSpace(this.CurrentPageLabel) ? null : this.CurrentPageLabel.Trim();
			onCompleted?.Invoke(this);
		}
	}

	[Serializable]
	public class BreadcrumbSettings
	{
		public BreadcrumbSettings() { }

		[FormControl(ControlType = "TextArea")]
		public string Template { get; set; }

		public string SeperatedLabel { get; set; } = ">";

		public string HomeLabel { get; set; } = "Home";

		[FormControl(DataType = "url")]
		public string HomeURL { get; set; }

		public string HomeAdditionalLabel { get; set; }

		[FormControl(DataType = "url")]
		public string HomeAdditionalURL { get; set; }

		public bool ShowModuleLink { get; set; } = false;

		public string ModuleLabel { get; set; }

		[FormControl(DataType = "url")]
		public string ModuleURL { get; set; }

		public string ModuleAdditionalLabel { get; set; }

		[FormControl(DataType = "url")]
		public string ModuleAdditionalURL { get; set; }

		public bool ShowContentTypeLink { get; set; } = false;

		public string ContentTypeLabel { get; set; }

		[FormControl(DataType = "url")]
		public string ContentTypeURL { get; set; }

		public string ContentTypeAdditionalLabel { get; set; }

		[FormControl(DataType = "url")]
		public string ContentTypeAdditionalURL { get; set; }

		public void Normalize(Action<BreadcrumbSettings> onCompleted = null)
		{
			this.Template = string.IsNullOrWhiteSpace(this.Template) ? null : this.Template.Trim();
			this.SeperatedLabel = string.IsNullOrWhiteSpace(this.SeperatedLabel) ? ">" : this.SeperatedLabel.Trim();
			this.HomeLabel = string.IsNullOrWhiteSpace(this.HomeLabel) ? null : this.HomeLabel.Trim();
			this.HomeURL = string.IsNullOrWhiteSpace(this.HomeURL) ? null : this.HomeURL.Trim();
			this.HomeAdditionalLabel = string.IsNullOrWhiteSpace(this.HomeAdditionalLabel) ? null : this.HomeAdditionalLabel.Trim();
			this.HomeAdditionalURL = string.IsNullOrWhiteSpace(this.HomeAdditionalURL) ? null : this.HomeAdditionalURL.Trim();
			this.ModuleLabel = string.IsNullOrWhiteSpace(this.ModuleLabel) ? null : this.ModuleLabel.Trim();
			this.ModuleURL = string.IsNullOrWhiteSpace(this.ModuleURL) ? null : this.ModuleURL.Trim();
			this.ModuleAdditionalLabel = string.IsNullOrWhiteSpace(this.ModuleAdditionalLabel) ? null : this.ModuleAdditionalLabel.Trim();
			this.ModuleAdditionalURL = string.IsNullOrWhiteSpace(this.ModuleAdditionalURL) ? null : this.ModuleAdditionalURL.Trim();
			this.ContentTypeLabel = string.IsNullOrWhiteSpace(this.ContentTypeLabel) ? null : this.ContentTypeLabel.Trim();
			this.ContentTypeURL = string.IsNullOrWhiteSpace(this.ContentTypeURL) ? null : this.ContentTypeURL.Trim();
			this.ContentTypeAdditionalLabel = string.IsNullOrWhiteSpace(this.ContentTypeAdditionalLabel) ? null : this.ContentTypeAdditionalLabel.Trim();
			this.ContentTypeAdditionalURL = string.IsNullOrWhiteSpace(this.ContentTypeAdditionalURL) ? null : this.ContentTypeAdditionalURL.Trim();
			onCompleted?.Invoke(this);
		}
	}
}
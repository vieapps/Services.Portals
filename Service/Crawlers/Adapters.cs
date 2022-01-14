#region Related components
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using net.vieapps.Components.Utility;
#endregion

namespace net.vieapps.Services.Portals.Crawlers
{
	public class NormalizingAdapter : ICrawlerAdapter
	{
		ICrawlerInfo ICrawlerAdapter.CrawlerInfo
		{
			get => this.CrawlerInfo;
			set => this.CrawlerInfo = value as Crawler;
		}

		public NormalizingAdapter(ICrawlerInfo crawlerInfo = null)
		{
			this.CrawlerInfo = crawlerInfo as Crawler;
			var settings = this.CrawlerInfo?.Settings ?? new JObject();

			settings.Get("RemoveTags", new JObject()).ForEach(kvp =>
			{
				var predicates = new List<Expression>();
				kvp.Value.Get<JArray>("Predicates")?.Select(options => options as JObject).ForEach(options =>
				{
					var predicate = this.GetExpression(options.Get<string>("Operator"), options.Get<string>("Value"), options.Get<string>("Attribute"));
					if (predicate != null && !string.IsNullOrWhiteSpace(predicate.Attribute))
						predicates.Add(predicate);
				});
				if (predicates.Any())
					this.RemoveTags.Add(kvp.Key.ToLower().Trim(), new Tuple<string, List<Expression>>(kvp.Value.Get<string>("Operator"), predicates));
			});

			var removeTagAttributes = settings.Get("RemoveTagAttributes", new JObject());
			if (removeTagAttributes["Disabled"] != null && removeTagAttributes.Get<bool>("Disabled"))
				this.DisableRemoveTagAttributes = true;

			(this.DisableRemoveTagAttributes ? new JObject() : removeTagAttributes.Get("Tags", new JObject())).ForEach(tag =>
			{
				var tagAttributes = new Dictionary<string, Tuple<string, List<Expression>, List<Expression>>>();
				(tag.Value as JObject)?.ForEach(kvp =>
				{
					var predicates = new List<Expression>();
					kvp.Value.Get<JArray>("Predicates")?.Select(options => options as JObject).ForEach(options =>
					{
						var predicate = this.GetExpression(options.Get<string>("Operator"), options.Get<string>("Value"));
						if (predicate != null)
							predicates.Add(predicate);
					});
					if (predicates.Any())
					{
						var actions = new List<Expression>();
						kvp.Value.Get<JArray>("Actions")?.Select(options => options as JObject).ForEach(options =>
						{
							var action = this.GetExpression(options.Get<string>("Operator"), options.Get<string>("Value"), options.Get<string>("Attribute"), options.Get<string>("Replacement"), op => op == "replace" || op == "replaces" || op == "update" || op == "updates" ? "Replaces" : op == "set" || op == "sets" ? "Sets" : null);
							if (action != null)
								actions.Add(action);
						});
						tagAttributes.Add(kvp.Key, new Tuple<string, List<Expression>, List<Expression>>(kvp.Value.Get<string>("Operator"), predicates, actions));
					}
				});
				if (tagAttributes.Any())
					this.RemoveTagAttributes.Add(tag.Key, tagAttributes);
			});

			if (settings["RemoveWhitespaces"] != null && !settings.Get<bool>("RemoveWhitespaces"))
				this.RemoveWhitespaces = false;

			if (settings["NormalizeHeadings"] != null && settings.Get<bool>("NormalizeHeadings"))
				this.NormalizeHeadings = true;

			if (settings["NormalizeSummary"] != null && settings.Get<bool>("NormalizeSummary"))
				this.NormalizeSummary = true;

			settings.Get("Updates", new JObject()).ForEach(kvp =>
			{
				var predicates = new List<Expression>();
				kvp.Value.Get<JArray>("Predicates")?.Select(options => options as JObject).ForEach(options =>
				{
					var predicate = this.GetExpression(options.Get<string>("Operator"), options.Get<string>("Value"));
					if (predicate != null)
						predicates.Add(predicate);
				});
				var actions = new List<Expression>();
				kvp.Value.Get<JArray>("Actions")?.Select(options => options as JObject).ForEach(options =>
				{
					var action = this.GetExpression(options.Get<string>("Operator"), options.Get<string>("Value"), options.Get<string>("Attribute"), options.Get<string>("Replacement"), op => op == "replace" || op == "replaces" || op == "update" || op == "updates" ? "Replaces" : op == "set" || op == "sets" ? "Sets" : null);
					if (action != null)
						actions.Add(action);
				});
				if (predicates.Any() && actions.Any())
					this.Updates.Add(kvp.Key, new Tuple<string, List<Expression>, List<Expression>>(kvp.Value.Get<string>("Operator"), predicates, actions));
			});
		}

		protected Crawler CrawlerInfo { get; set; }

		protected Dictionary<string, Tuple<string, List<Expression>>> RemoveTags { get; set; } = new Dictionary<string, Tuple<string, List<Expression>>>();

		protected bool DisableRemoveTagAttributes { get; set; } = false;

		protected Dictionary<string, Dictionary<string, Tuple<string, List<Expression>, List<Expression>>>> RemoveTagAttributes { get; set; } = new Dictionary<string, Dictionary<string, Tuple<string, List<Expression>, List<Expression>>>>();

		protected bool RemoveWhitespaces { get; set; } = true;

		protected bool NormalizeHeadings { get; set; } = false;

		protected bool NormalizeSummary { get; set; } = false;

		protected Dictionary<string, Tuple<string, List<Expression>, List<Expression>>> Updates { get; set; } = new Dictionary<string, Tuple<string, List<Expression>, List<Expression>>>();

		protected bool IsTagRemovable(XTag tag)
		{
			var name = tag.Name.ToLower();
			if (this.RemoveTags.TryGetValue(name, out var info))
			{
				var isOr = "Or".Equals(info.Item1);
				foreach (var predicate in info.Item2)
				{
					var attribute = tag.Attributes.FirstOrDefault(attr => attr.Name.ToLower() == predicate.Attribute.ToLower());
					var beRemoved = attribute != null;
					if (beRemoved)
					{
						switch (predicate.Operator)
						{
							case "CastAsBoolean":
								beRemoved = Boolean.TryParse(predicate.Value, out var castAs) && castAs;
								break;

							case "Contains":
								beRemoved = attribute.Value.IsContains(predicate.Value);
								break;

							case "Equals":
								beRemoved = attribute.Value.IsEquals(predicate.Value);
								break;

							case "NotEquals":
								beRemoved = !attribute.Value.IsEquals(predicate.Value);
								break;

							case "StartsWith":
								beRemoved = attribute.Value.IsStartsWith(predicate.Value);
								break;

							case "EndsWith":
								beRemoved = attribute.Value.IsEndsWith(predicate.Value);
								break;
						}
						if (beRemoved && isOr)
							return true;
						if (!beRemoved && !isOr)
							return false;
					}
				}
			}
			return false;
		}

		bool Being(string value, string @operator, List<Expression> predicates)
		{
			if (value == null)
				return false;

			var being = false;
			var isOr = "Or".Equals(@operator);
			foreach (var predicate in predicates ?? new List<Expression>())
			{
				switch (predicate.Operator)
				{
					case "CastAsBoolean":
						being = Boolean.TryParse(predicate.Value, out var castAs) && castAs;
						break;

					case "Contains":
						being = value.IsContains(predicate.Value);
						break;

					case "Equals":
						being = value.IsEquals(predicate.Value);
						break;

					case "NotEquals":
						being = !value.IsEquals(predicate.Value);
						break;

					case "StartsWith":
						being = value.IsStartsWith(predicate.Value);
						break;

					case "EndsWith":
						being = value.IsEndsWith(predicate.Value);
						break;
				}

				if (being && isOr)
					return true;

				if (!being && !isOr)
					return false;
			}

			return being;
		}

		protected bool IsTagAttributeRemovable(XTag tag, XTagAttribute tagAttribute)
			=> this.RemoveTagAttributes.TryGetValue(tag.Name.ToLower(), out var tagInfo) && tagInfo.TryGetValue(tagAttribute.Name.ToLower(), out var attributeInfo)
				? this.UpdateTagAttribute(tag, tagAttribute, attributeInfo.Item3, this.Being(tagAttribute.Value, attributeInfo.Item1, attributeInfo.Item2))
				: this.UpdateTagAttribute(tag, tagAttribute, null, true);

		bool UpdateTagAttribute(XTag tag, XTagAttribute tagAttribute, List<Expression> actions, bool being)
		{
			XTagAttribute attribute;
			foreach (var action in actions ?? new List<Expression>())
				switch (action.Operator)
				{
					case "Replaces":
						var pattern = action.Value != null && action.Value.StartsWith("@") ? action.Value.Evaluate(null, null, null)?.As<string>() : action.Value;
						if (!string.IsNullOrWhiteSpace(pattern))
						{
							var replacement = action.Replacement != null && action.Replacement.StartsWith("@") ? action.Replacement.Evaluate(null, null, null)?.As<string>() : action.Replacement;
							attribute = string.IsNullOrWhiteSpace(action.Attribute) ? tagAttribute : tag.Attributes.FirstOrDefault(attr => attr.Name.IsEquals(action.Attribute));
							if (attribute == null)
							{
								attribute = new XTagAttribute
								{
									Name = action.Attribute ?? tagAttribute.Name
								};
								tag.Attributes.Add(attribute);
							}
							attribute.Value = attribute?.Value.Replace(StringComparison.OrdinalIgnoreCase, pattern, replacement);
						}
						break;

					case "Sets":
						attribute = string.IsNullOrWhiteSpace(action.Attribute) ? tagAttribute : tag.Attributes.FirstOrDefault(attr => attr.Name.IsEquals(action.Attribute));
						if (attribute == null)
						{
							attribute = new XTagAttribute
							{
								Name = action.Attribute ?? tagAttribute.Name
							};
							tag.Attributes.Add(attribute);
						}
						attribute.Value = action.Value != null && action.Value.StartsWith("@") ? action.Value.Evaluate(null, null, null)?.As<string>() : action.Value;
						break;
				}
			return being;
		}

		protected bool IsUpdatable(Content content, string name)
			=> this.Updates.TryGetValue(name, out var info) && this.Being(content.GetAttributeValue(name).As<string>(), info.Item1, info.Item2);

		protected Content Update(Content content)
		{
			content.Normalize(this.CrawlerInfo, true);
			var @params = this.CrawlerInfo?.ToExpandoObject();
			this.Updates?.Keys.Where(name => this.IsUpdatable(content, name)).ForEach(name =>
			{
				foreach (var action in this.Updates[name].Item3)
					switch (action.Operator)
					{
						case "Replaces":
							var value = content.GetAttributeValue(action.Attribute ?? name)?.As<string>();
							if (!string.IsNullOrWhiteSpace(value))
							{
								var pattern = action.Value != null && action.Value.StartsWith("@") ? action.Value.Evaluate(content.ToExpandoObject(), null, @params)?.As<string>() : action.Value;
								if (!string.IsNullOrWhiteSpace(pattern))
								{
									var replacement = action.Replacement != null && action.Replacement.StartsWith("@") ? action.Replacement.Evaluate(content.ToExpandoObject(), null, @params)?.As<string>() : action.Replacement;
									content.SetAttributeValue(action.Attribute ?? name, value.Replace(StringComparison.OrdinalIgnoreCase, pattern, replacement));
								}
							}
							break;

						case "Sets":
							content.SetAttributeValue(action.Attribute ?? name, action.Value != null && action.Value.StartsWith("@") ? action.Value.Evaluate(content.ToExpandoObject(), null, @params)?.As<string>() : action.Value);
							break;
					}
			});
			return content;
		}

		protected Expression GetExpression(string @operator, string value, string attribute = null, string replacement = null, Func<string, string> normalizeOperator = null)
		{
			@operator = @operator?.ToLower().Trim();
			if (!string.IsNullOrWhiteSpace(@operator))
				@operator = normalizeOperator != null
					? normalizeOperator(@operator)
					: @operator == "contain" || @operator == "contains" || @operator == "iscontains"
						? "Contains"
						: @operator == "equal" || @operator == "equals" || @operator == "isequals"
							? "Equals"
							: @operator == "notequal" || @operator == "notequals"
								? "NotEquals"
								: @operator == "startwith" || @operator == "startswith" || @operator == "isstartswith"
									? "StartsWith"
									: @operator == "endwith" || @operator == "endswith" || @operator == "isendswith"
										? "EndsWith"
										: @operator == "castasboolean" || @operator == "castasbool"
											? "CastAsBoolean"
											: null;
			return !string.IsNullOrWhiteSpace(@operator) && value != null
				? new Expression
				{
					Attribute = attribute,
					Operator = @operator,
					Value = value,
					Replacement = replacement
				}
				: null;
		}

		public virtual Task NormalizeAsync(Content content, CancellationToken cancellationToken = default)
		{
			content.Details = (content.Details ?? "").NormalizeNoneCloseTags();

			if (this.RemoveTags != null && this.RemoveTags.Any())
				content.Details = content.Details.RemoveTags(this.RemoveTags.Keys.ToList(), true, this.IsTagRemovable);

			if (!this.DisableRemoveTagAttributes)
				content.Details = content.Details.RemoveTagAttributes(UtilityService.RemoveTagAttributesPredicate, (tag, tagAttribute) => this.RemoveTagAttributes == null || !this.RemoveTagAttributes.Any() || this.IsTagAttributeRemovable(tag, tagAttribute));

			if (this.RemoveWhitespaces)
				content.Details = content.Details.RemoveWhitespaces().Replace("<a></a>", "").Replace("<span></span>", "").Replace("<p></p>", "").Replace("<div></div>", "");

			if (this.NormalizeHeadings)
			{
				content.Details = content.Details.Replace(StringComparison.OrdinalIgnoreCase, "<h4", "<h5").Replace(StringComparison.OrdinalIgnoreCase, "</h4>", "</h5>");
				content.Details = content.Details.Replace(StringComparison.OrdinalIgnoreCase, "<h3", "<h4").Replace(StringComparison.OrdinalIgnoreCase, "</h3>", "</h4>");
				content.Details = content.Details.Replace(StringComparison.OrdinalIgnoreCase, "<h2", "<h3").Replace(StringComparison.OrdinalIgnoreCase, "</h2>", "</h3>");
				content.Details = content.Details.Replace(StringComparison.OrdinalIgnoreCase, "<h1", "<h2").Replace(StringComparison.OrdinalIgnoreCase, "</h1>", "</h2>");
			}

			if (!string.IsNullOrWhiteSpace(content.Details))
			{
				var tags = content.Details.FindTags();
				var firstTag = tags.FirstOrDefault(tag => tag.IsOpen && tag.RelevantIndex < 0);
				if (firstTag != null)
					content.Details += $"</{firstTag.Name}>";
				if (string.IsNullOrWhiteSpace(content.Summary) || this.NormalizeSummary)
				{
					var summary = tags.FirstOrDefault(info => info.Name.IsEquals("p"))?.Inner;
					if (string.IsNullOrWhiteSpace(summary))
					{
						var firstBRIndex = tags.FindIndex(info => info.Name.IsEquals("br"));
						if (firstBRIndex > 0)
							summary = tags[firstBRIndex - 1].Inner;
					}
					content.Summary = (summary ?? tags.FirstOrDefault(info => info.Name.IsEquals("div"))?.Inner ?? content.Summary ?? content.Details)?.Substring(0, 250).RemoveTags();
				}
			}

			this.Update(content);

			return Task.CompletedTask;
		}
	}
}
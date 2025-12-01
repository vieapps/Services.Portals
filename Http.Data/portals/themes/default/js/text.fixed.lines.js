$(window).on("load", () => $("[data-fixed-lines]").each(function () {
	var element = $(this);
	var options = $.extend({ fixedLines: 0 }, element.data());
	if (!!options.fixedLines) {
		element.bind("customResize", event => {
			event.stopPropagation();
			var fontSize = parseInt(element.css("font-size"));
			var lineHeight = parseInt(element.css("line-height"));
			var height = fontSize * (lineHeight / fontSize) * options.fixedLines;
			element.css({
				"display": "block",
				"display": "-webkit-box",
				"height": height + options.fixedLines,
				"-webkit-line-clamp": String(options.fixedLines),
				"-webkit-box-orient": "vertical",
				"overflow": "hidden",
				"text-overflow": "ellipsis"
			});
		}).trigger("customResize");
		$(window).on("resize", () => element.trigger("customResize"));
	}
}));
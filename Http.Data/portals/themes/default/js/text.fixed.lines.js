__vieapps.fixedLines = function (selector, opts) {
	$(selector).each(function () {
		var element = $(this);
		var options = $.extend({ fixedLines: 0 }, opts, element.data());
		if (options.fixedLines) {
			element.bind("customResize", function (event) {
				event.stopPropagation();
				var fontSize = parseInt(element.css("font-size"));
				var lineHeight = parseInt(element.css("line-height"));
				var height = fontSize * (lineHeight / fontSize) * options.fixedLines;
				element.css({
					"display": "block",
					"display": "-webkit-box",
					"height": height,
					"-webkit-line-clamp": String(options.fixedLines),
					"-webkit-box-orient": "vertical",
					"overflow": "hidden",
					"text-overflow": "ellipsis"
				});
			}).trigger("customResize");
			$(window).on("resize", function () {
				element.trigger("customResize");
			});
		}
	});
};

$(window).on("load", function () {
	__vieapps.fixedLines("[data-fixed-lines]");
});
__vieapps.wow = { instance: undefined, callbacks: [] };
if (typeof WOW !== "undefined") {
	__vieapps.wow.instance = new WOW({
		callback: function (node) {
			__vieapps.wow.callbacks.filter(callback => typeof callback === "function").forEach(callback => callback(node));
		}
	});
	__vieapps.wow.instance.init();
}
__vieapps.wowCallbacks = __vieapps.wow.callbacks;

__vieapps.slideAnimates = ["fadeOut", "fadeOutUp", "fadeOutDown", "fadeOutLeft", "fadeOutRight", "lightSpeedOutRight", "lightSpeedOutLeft", "flipOutX", "flipOutY", "rotateOut", "rotateOutDownLeft", "rotateOutDownRight", "rotateOutUpLeft", "rotateOutUpRight", "rollOut", "zoomOut", "zoomOutDown", "zoomOutLeft", "zoomOutRight", "zoomOutUp", "slideOut", "slideOutDown", "slideOutLeft", "slideOutRight", "slideOutUp"];

__vieapps.prepareResponsiveImages = function() {
	var screenWidth = $(window).width();
	var selectors = ["data-src-mode", "data-bg-mode"];
	selectors = screenWidth < 768
		? selectors.map(selector => selector.replace("-mode", "-mobile"))
		: screenWidth < 1024
			? selectors.map(selector => selector.replace("-mode", "-tablet"))
			: selectors.map(selector => selector.replace("-mode", "-desktop"));
	selectors.forEach(selector => {
		$(`[${selector}]`).each(function () {
			var ctrl = $(this), url = ctrl.attr(selector);
			if (!!url) {
				if (selector.indexOf("-src-") > 0) {
					ctrl.attr("src", url);
				}
				else {
					ctrl.css("background-image", `url(${url})`);
				}
			}
		});
	});
};

__vieapps.prepareLazyImage = function(element) {
	var image = $(element), src = image.data("lazySrc");
	if (!!src) {
		image.attr("src", src);
	}
};

$(function () {
	$("a.inline.popup").each(function () {
		var anchor = $(this);
		var href = anchor[0].href.indexOf("?") > 0
			? anchor[0].href.substr(0, anchor[0].href.indexOf("?"))
			: anchor[0].href;
		if (href.endsWith(".jpg") || href.endsWith(".jpeg") || href.endsWith(".png") || href.endsWith(".webp") || href.endsWith(".bmp") || href.endsWith(".gif") || href.endsWith(".tif")) {
			__vieapps.utils.addDataAttribute(anchor, "fancybox", "gallery");
			anchor.addClass("image");
			var caption = anchor.parent().children("figcaption");
			if (caption && caption.length) {
				__vieapps.utils.addDataAttribute(anchor, "caption", caption.text());
			}
		}
		else if (!href.startsWith("javascript:")) {
			__vieapps.utils.addDataAttribute(anchor, "type", "iframe");
			anchor.addClass("iframe");
		}
	});
	if (typeof $.fancybox !== "undefined") {
		["a.inline.popup.image", "a.inline.popup.iframe"].forEach(selector => $(selector).fancybox());
	}

	$("a.smooth.scroll").on("click tap", function (event) {
		event.preventDefault();
		var target = $($(this).attr("href"));
		if (target.length) {
			$("html,body").stop().animate({ scrollTop: target.offset().top - 50 }, 567);
		}
	});

	__vieapps.prepareResponsiveImages();
});

$(window).on("load", function () {
	$("body").addClass("loaded");
	$("img[data-lazy-src]").each(function () {
		__vieapps.prepareLazyImage(this);
	});
});

$(window).on("resize", function () {
	__vieapps.prepareResponsiveImages();
});

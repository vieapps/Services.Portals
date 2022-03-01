__vieapps.countToNumber = function (element, selector, opts) {
	element = $(element);
	var options = $.extend(opts, element.data());
	var toNumber = (options.countNumber || options.countToNumber || options.counterNumber);
	toNumber = toNumber !== undefined && +toNumber > 0 ? +toNumber : 0;
	var duration = (options.countDuration || options.countToDuration || options.counterDuration);
	duration = duration !== undefined && +duration > 0 ? +duration : 3000;
	if (selector !== undefined && selector !== "") {
		var elements = element.children(selector);
		if (elements.length) {
			element = $(elements[0]);
		}
	}
	$({ count: 0 }).animate({ count: toNumber }, {
		duration: duration,
		easing: "linear",
		step: function () {
			element.text(Math.floor(this.count).toLocaleString(__vieapps.language));
		},
		complete: function () {
			element.text(this.count.toLocaleString(__vieapps.language));
		}
	});
};
__vieapps.counter = __vieapps.countToNumber;

$(function () {
	["[data-count-number]", "[data-count-to-number]"].forEach(selector => $(selector).each(function () {
		$(this).addClass("wow");
	}));
	if ($("[data-count-number]").length || $("[data-count-to-number]").length) {
		__vieapps.wow.callbacks.push(__vieapps.countToNumber);
	}
});

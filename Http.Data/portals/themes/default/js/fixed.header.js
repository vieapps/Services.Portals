__vieapps.scrollPosition = 0;
__vieapps.scrollDirection = "";
__vieapps.headerSelector = undefined;

$(function () {
	if (typeof __vieapps.headerSelector === "string") {
		var header = $(__vieapps.headerSelector);
		if (header.length) {
			$(header).on("mouseover", function () {
				header.addClass("hovered");
			});
			$(header).on("mouseout", function () {
				header.removeClass("hovered");
			});
		}
	}
});

$(window).on("scroll", function () {
	var position = $(window).scrollTop();
	__vieapps.scrollDirection = position > __vieapps.scrollPosition ? "down" : "up";
	__vieapps.scrollPosition = position;
	var header = $(__vieapps.headerSelector || ".default.desktop > .header");
	if (header.length) {
		if (__vieapps.scrollDirection == "down" || __vieapps.scrollPosition < 1) {
			header.removeClass("fixed");
		}
		else {
			header.addClass("fixed");
		}
	}
});

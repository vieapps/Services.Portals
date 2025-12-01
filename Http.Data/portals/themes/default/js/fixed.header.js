__vieapps.scroll = { 
	position: 0,
	direction: "down",
	fn: -1,
	selectors: !!__vieapps.scroll && !!__vieapps.scroll.selectors ? __vieapps.scroll.selectors : {
		global: "body",
		header: ".desktop .header"
	},
	onLoaded: !!__vieapps.scroll && typeof __vieapps.scroll.onLoaded === "function" ? __vieapps.scroll.onLoaded : undefined,
	onScroll: !!__vieapps.scroll && typeof __vieapps.scroll.onScroll === "function" ? __vieapps.scroll.onScroll : undefined
};

$(window).on("load", () => {
	$(__vieapps.scroll.selectors.global || "body").addClass("loaded");
	if (typeof __vieapps.scroll.onLoaded === "function") {
		__vieapps.scroll.onLoaded();
	}
	if (!!__vieapps.scroll.selectors.header) {
		$(__vieapps.scroll.selectors.header).on("mouseover", event => $(event.currentTarget).addClass("hovered"));
		$(__vieapps.scroll.selectors.header).on("mouseout", event => $(event.currentTarget).removeClass("hovered"));
	}
});

$(window).on("scroll", () => {
	var position = $(window).scrollTop();
	var direction = __vieapps.scroll.direction;
	__vieapps.scroll.direction = position > __vieapps.scroll.position ? "down" : "up";
	__vieapps.scroll.position = position;
	if (direction !== __vieapps.scroll.direction) {
		clearTimeout(__vieapps.scroll.fn);
		__vieapps.scroll.fn = setTimeout(() => {
			$(__vieapps.scroll.selectors.global || "body").removeClass("up down").addClass(__vieapps.scroll.direction);
			if (!!__vieapps.scroll.selectors.header) {
				if (__vieapps.scroll.direction === "down" || __vieapps.scroll.position < 1) {
					$(__vieapps.scroll.selectors.header).removeClass("fixed");
				}
				else {
					$(__vieapps.scroll.selectors.header).addClass("fixed");
				}
			}
		}, 13);
	}
	if (typeof __vieapps.scroll.onScroll === "function") {
		__vieapps.scroll.onScroll();
	}
});

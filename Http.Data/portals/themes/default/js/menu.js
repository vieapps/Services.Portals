__vieapps.width = function (elements) {
	var width = 0;
	(elements || []).forEach(element => {
		var $element = $(element);
		width += $element.width() + parseInt($element.css("padding-left")) + parseInt($element.css("padding-right"));
	});
	return width;
};

__vieapps.menu = {
	auto: true,
	timeoutID: -1,
	width: 1140,
	position: function (ul) {
		var menu = !!ul && ul.length ? $(ul) : undefined;
		if (!!menu && menu.position().left === 0) {
			var width = __vieapps.width(menu.children("li").toArray());
			var parent = menu.parent();
			var left = parent.position().left;
			var min = 0 - left;
			var max = __vieapps.menu.width - width - left;
			left = (parent.width() - width) / 2;
			menu.css({ left: left < min ? min : left > max ? max : left, width: width });
		}
	},
	assign: function (selector) {
		__vieapps.menu.width = $(selector).width();
		if ($(window).width() >= 1200) {
			var ul = $(selector + " > li.selected > ul");
			if ((!ul || !ul.length) && __vieapps.menu.auto) {
				var li = $(selector + " > li:first-child");
				if (li && li.length) {
					ul = $(li).children("ul");
					if (ul && ul.length) {
						$(li).addClass("selected");
					}
				}
			}
			__vieapps.menu.position(ul);
		}
		$(selector + " > li").each(function () {
			var li = $(this);
			li.on("mouseout", function () {
				$(selector + " > li.selected.inactive").each(function () {
					var item = $(this);
					__vieapps.menu.timeoutID = setTimeout(function () {
						item.removeClass("inactive");
					}, 300);
				});
			});
			li.on("mouseover", function () {
				var item = $(this);
				if (item.hasClass("selected")) {
					item.removeClass("inactive");
				}
				else {
					clearTimeout(__vieapps.menu.timeoutID);
					$(selector + " > li.selected").each(function () {
						$(this).addClass("inactive");
					});
				}
				__vieapps.menu.position(item.children("ul"));
			});
		});
	}
};
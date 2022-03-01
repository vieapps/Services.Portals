__vieapps.protector = {
	onkey: function (event) {
		if (event.ctrlKey && (event.which == 65 || event.which == 66 || event.which == 67 || event.which == 70 || event.which == 73 || event.which == 80 || event.which == 83 || event.which == 85 || event.which == 86)) {
			event.preventDefault();
			return false;
		}
		return true;
	},
	enable: function () {
		$(document).on("contextmenu dragstart selectstart onmousedown", function (event) {
			event.preventDefault();
			return false;
		});
		$(document).on("keydown", function (event) {
			if (event.keyCode == 123 || event.keyCode == 18) {
				return false;
			}
		});
		$(document).on("keypress", function (event) {
			return __vieapps.protector.onkey(event);
		});
		$(window).on("keydown", function (event) {
			return __vieapps.protector.onkey(event);
		});
	}
};

__vieapps.googlemaps = {
	// identity of DIV element that use to presents the mash-up map
	canvasID: "maps",

	// the map instance
	map: undefined,

	// the current position
	current: {
		marker: undefined,
		box: undefined,
		address: undefined
	},

	// information of all markers
	markers: [],

	// directions
	directions: {
		service: undefined,
		renderer: undefined,
		results: undefined,
		markers: []
	},

	// near-by
	nearby: {
		nearest: undefined,
		markers: []
	},

	// settings
	settings: {
		// path to icon of a marker
		icon: undefined,

		// css class name of info box
		css: undefined,

		// min-width of info box
		minWidth: undefined,

		// zoom
		defaultZoomLevel: 13,
		onclickZoomLevel: 15,
		zoomOnClick: true,

		// auto, greedy, cooperative
		gestureHandling: "auto",

		// the current position
		current: {
			label: __vieapps.language == "vi-VN" ? "Ví trí của bạn" : "Your location",
			latitude: 21.0195594,
			longitude: 105.8209749,
			get: false
		}
	},

	// initialize
	init: function (options, onInitialized, onLocated) {
		// prepare options/settings
		if (options) {
			if (options.canvasID) {
				this.canvasID = options.canvasID;
			}
			if (options.markerIcon) {
				this.settings.icon = options.markerIcon;
			}
			if (options.boxCss) {
				this.settings.css = options.boxCss;
			}
			if (options.minWidth) {
				this.settings.minWidth = options.minWidth;
			}
			if (options.zoomLevel) {
				this.settings.defaultZoomLevel = options.zoomLevel;
			}
			if (options.gestureHandling) {
				this.settings.gestureHandling = options.gestureHandling;
			}
			if (options.position) {
				if (options.position.label && options.position.label != "") {
					this.settings.current.label = options.position.label;
				}
				if (typeof options.position.latitude !== "undefined" && typeof options.position.longitude !== "undefined") {
					this.settings.current.latitude = +options.position.latitude;
					this.settings.current.longitude = +options.position.longitude;
				}
				else if (!options.position.latitude && !options.position.longitude) {
					this.settings.current.latitude = undefined;
					this.settings.current.longitude = undefined;
				}
				if (typeof options.position.get !== "undefined") {
					this.settings.current.get = !!options.position.get;
				}
			}
		}

		// get element to use as canvas of the maps
		var canvas = typeof this.canvasID === "string" && this.canvasID !== ""
			? document.getElementById(this.canvasID)
			: undefined;

		if (!canvas) {
			console.error("The canvas element for displaying the map is not found");
			return;
		}

		// initialize Google Maps
		this.map = new google.maps.Map(canvas, {
			zoom: this.settings.defaultZoomLevel,
			gestureHandling: this.settings.gestureHandling,
			mapTypeId: google.maps.MapTypeId.ROADMAP
		});
		this.initialize(onInitialized, onLocated);
	},

	initialize: function (onInitialized, onLocated) {
		// initialize the services
		this.directions.service = new google.maps.DirectionsService();
		this.directions.renderer = new google.maps.DirectionsRenderer({ map: this.map, suppressMarkers: true });

		// create temporary current position and callback when initialized
		this.createCurrent();
		this.gotoCurrent(onInitialized);

		// locate current real position
		if (this.settings.current.get) {
			this.locateCurrent(
				function (position) {
					__vieapps.googlemaps.getAddress(position, function (data) {
						if (data) {
							var address = data.address_components;
							__vieapps.googlemaps.current.address = {
								full: data.formatted_address,
								county: address[address.length - 3].long_name,
								province: address[address.length - 2].long_name,
								country: address[address.length - 1].long_name
							};
						}
						__vieapps.googlemaps.createCurrent(position, undefined, data ? data.formatted_address : undefined, onLocated);
					});
				},
				function () {
					if (!__vieapps.googlemaps.current.marker && !__vieapps.googlemaps.current.box) {
						__vieapps.googlemaps.createCurrent(undefined, undefined, undefined, onLocated);
					}
					else if (typeof onLocated === "function") {
						onLocated(__vieapps.googlemaps.current);
					}
				}
			);
		}
		else if (this.settings.current.latitude && this.settings.current.longitude) {
			this.createCurrent(undefined, undefined, undefined, onLocated);
		}
	},

	// create a new marker by specified position/location
	createMarkerByPosition: function (position, id, title, content, icon, showBox, moveTo, onClick, callback) {
		var info = {
			id: typeof id === "string" && id !== "" ? id : undefined,
			marker: this.createMarker(position, title, icon),
			box: this.createBox(title, typeof id === "string" && id !== "" ? content.replace(/\{\{id\}\}/g, id) : content)
		};
		google.maps.event.addListener(info.marker, "click", function () {
			__vieapps.googlemaps.hideBoxes(false, __vieapps.googlemaps.directions.markers.length ? false : true);
			__vieapps.googlemaps.setCenter(info.marker.position, __vieapps.googlemaps.settings.zoomOnClick && __vieapps.googlemaps.settings.onclickZoomLevel > __vieapps.googlemaps.map.zoom ? __vieapps.googlemaps.settings.onclickZoomLevel : undefined);
			info.box.open(__vieapps.googlemaps.map, info.marker);
			if (typeof onClick === "function") {
				onClick(info.marker, info.box);
			}
		});
		this.markers.push(info);
		if (showBox) {
			info.box.open(this.map, info.marker);
		}
		if (moveTo) {
			this.setCenter(info.marker.position);
		}
		return typeof callback === "function"
			? callback(info)
			: info;
	},

	// create a new marker by address (using Geocoder to process position)
	createMarkerByAddress: function (address, id, title, content, icon, showBox, moveTo, onClick, callback) {
		this.getPosition(address, function (position) {
			if (position) {
				__vieapps.googlemaps.createMarkerByPosition(position, id, title, content, icon, showBox, moveTo, onClick, callback);
			}
			else {
				console.warn("No suitable location was found => ", address);
				if (typeof callback === "function") {
					callback(undefined);
				}
			}
		});
	},

	// location & address
	createPosition: function (latitude, longitude, callback) {
		var position = new google.maps.LatLng(latitude, longitude);
		return typeof callback === "function"
			? callback(position)
			: position;
	},

	preparePosition(position, callback) {
		position = position
			? position instanceof google.maps.LatLng
				? position
				: this.createPosition(position.latitude || position.Latitude, position.longitude || position.Longitude)
			: this.current && this.current.marker
				? this.current.marker.position
				: this.createPosition(this.settings.current.latitude, this.settings.current.longitude);
		return typeof callback === "function"
			? callback(position)
			: position;
	},

	getPosition: function (address, callback) {
		if (typeof address === "string" && address !== "" && typeof callback === "function") {
			new google.maps.Geocoder().geocode({ address: address }, function (results, status) {
				var position = status == google.maps.GeocoderStatus.OK
					? __vieapps.googlemaps.createPosition(results[0].geometry.location.lat(), results[0].geometry.location.lng())
					: undefined;
				callback(position);
			});
		}
	},

	getAddress: function (position, callback) {
		if (typeof position === "object" && position !== null && typeof callback === "function") {
			new google.maps.Geocoder().geocode({ location: position }, function (results, status) {
				var address = status == google.maps.GeocoderStatus.OK
					? results[0]
					: undefined;
				callback(address);
			});
		}
	},

	// move the center of the map to a specified position
	setCenter: function (position, zoom) {
		if (position) {
			this.map.setCenter(position);
			if (zoom) {
				this.map.setZoom(zoom);
			}
		}
	},

	// current position
	locateCurrent: function (onSuccess, onError, timeout) {
		if (navigator.geolocation) {
			navigator.geolocation.getCurrentPosition(
				function (geoPosition) {
					if (typeof onSuccess === "function") {
						onSuccess(__vieapps.googlemaps.createPosition(geoPosition && geoPosition.coords ? geoPosition.coords.latitude : __vieapps.googlemaps.settings.current.latitude, geoPosition && geoPosition.coords ? geoPosition.coords.longitude : __vieapps.googlemaps.settings.current.longitude));
					}
				},
				function (error) {
					console.error("Unable to locate the current position", error);
					if (typeof onError === "function") {
						onError();
					}
				},
				{
					timeout: timeout || 13000
				}
			);
		}
		else {
			console.error("Cannot locate the current position");
			if (typeof onError === "function") {
				onError();
			}
		}
	},

	createCurrent: function (position, title, content, callback) {
		var addListener = !this.current.box && !this.current.marker;
		if (this.current.box) {
			this.current.box.close();
			this.current.box.setContent(this.getBoxContent(title || this.settings.current.label, content));
		}
		else {
			this.current.box = this.createBox(title || this.settings.current.label, content);
		}
		if (this.current.marker) {
			this.current.marker.setPosition(position || this.getCurrent());
		}
		else {
			this.current.marker = this.createMarker(position || this.getCurrent(), title);
		}
		if (addListener) {
			google.maps.event.addListener(this.current.marker, "click", function () {
				__vieapps.googlemaps.hideBoxes(false, __vieapps.googlemaps.directions.markers.length ? false : true);
				__vieapps.googlemaps.setCenter(__vieapps.googlemaps.current.marker.position, __vieapps.googlemaps.settings.zoomOnClick && __vieapps.googlemaps.settings.onclickZoomLevel > __vieapps.googlemaps.map.zoom ? __vieapps.googlemaps.settings.onclickZoomLevel : undefined);
				__vieapps.googlemaps.current.box.open(__vieapps.googlemaps.map, __vieapps.googlemaps.current.marker);
			});
		}
		if (position) {
			this.current.box.open(this.map, this.current.marker);
		}
		if (typeof callback === "function") {
			callback(this.current);
		}
	},

	getCurrent: function (callback) {
		var position = this.current && this.current.marker
			? this.current.marker.position
			: this.createPosition(this.settings.current.latitude, this.settings.current.longitude);
		return typeof callback === "function"
			? callback(position)
			: position;
	},

	gotoCurrent: function (callback) {
		this.setCenter(this.getCurrent(), this.settings.defaultZoomLevel);
		if (this.current && this.current.marker) {
			this.current.marker.setVisible(true);
		}
		if (typeof callback === "function") {
			callback();
		}
	},

	// directions between two locations/positions
	getDirections: function (start, end, travelMode, onSuccess, onFailure) {
		this.directions.service.route({ origin: start, destination: end, travelMode: travelMode ? travelMode : google.maps.TravelMode.DRIVING }, function (response, status) {
			if (status == google.maps.DirectionsStatus.OK) {
				if (typeof onSuccess === "function") {
					onSuccess(response);
				}
			}
			else if (typeof onFailure === "function") {
				onFailure();
			}
		});
	},

	showDirections: function (start, end, travelMode, callback) {
		this.getDirections(start, end, travelMode,
			function (response) {
				__vieapps.googlemaps.directions.results = response.routes[0].legs[0];
				__vieapps.googlemaps.directions.renderer.setDirections(response);
				__vieapps.googlemaps.displayDirections();
				if (typeof callback === "function") {
					callback();
				}
			},
			function () {
				console.warn("No suitable route was found");
			}
		);
	},

	displayDirections: function () {
		var icon = "https://chart.googleapis.com/chart?chst=d_map_xpin_icon&chld=pin_star|car-dealer|FFFFFF|014B92";
		this.hideBoxes(true, true);
		this.directions.markers = [];
		this.directions.results.steps.forEach((step, index) => {
			var position = index == 0
				? this.directions.results.start_location
				: step.start_location;
			var content = `
				<div>
					${(__vieapps.language == "vi-VN" ? "Điểm rẽ kế tiếp" : "Next turn")}: <b>${step.distance.text} - ${step.duration.text}</b>
				</div>
				<div>
					${step.instructions}
				</div>`;
			content = index == 0
				? `<div>
						<div>
							${(__vieapps.language == "vi-VN" ? "Quãng đường dự kiến" : "Expected distance")}: <b>${this.directions.results.distance.text}</b>
							-
							${(__vieapps.language == "vi-VN" ? "Thời gian dự kiến" : "Intend time")}: <b>${this.directions.results.duration.text}</b>
						</div>` + content + `</div>`
				: `<div>` + content + `</div>`;
			var marker = this.createMarker(position, undefined, icon);
			var box = this.createBox(undefined, content);
			google.maps.event.addListener(marker, "click", function () {
				__vieapps.googlemaps.hideBoxes(false, true, true);
				__vieapps.googlemaps.setCenter(marker.position, __vieapps.googlemaps.settings.zoomOnClick && __vieapps.googlemaps.settings.onclickZoomLevel > __vieapps.googlemaps.map.zoom ? __vieapps.googlemaps.settings.onclickZoomLevel : undefined);
				box.open(__vieapps.googlemaps.map, marker);
			});
			this.directions.markers.push({ marker: marker, box: box });
		});
		this.directions.markers[0].box.open(this.map, this.directions.markers[0].marker);
	},

	// near-by
	deg2rad: function (deg) {
		return deg * Math.PI / 180;
	},

	getDistance: function (from, to) {
		var latitude = this.deg2rad(to.latitude - from.latitude);
		var longitude = this.deg2rad(to.longitude - from.longitude);
		var distance = Math.sin(latitude / 2) * Math.sin(latitude / 2) + Math.cos(this.deg2rad(from.latitude)) * Math.cos(this.deg2rad(to.latitude)) * Math.sin(longitude / 2) * Math.sin(longitude / 2);
		return Math.atan2(Math.sqrt(distance), Math.sqrt(1 - distance)) * 2 * 6372.8;
	},

	getNearby: function (position, minDistance, callback) {
		this.nearby.nearest = undefined;
		this.nearby.markers = [];
		var from = {
			latitude: position.lat(),
			longitude: position.lng()
		};
		var nearest = 1000000000;
		this.markers.forEach(marker => {
			var to = {
				latitude: marker.marker.position.lat(),
				longitude: marker.marker.position.lng()
			};
			var distance = this.getDistance(from, to);
			if (distance <= minDistance) {
				this.nearby.markers.push(marker);
				if (nearest > distance) {
					nearest = distance;
					this.nearby.nearest = marker;
				}
			}
		});
		if (typeof callback === "function") {
			callback(this.nearby);
		}
	},

	showNearby: function (position, minDistance, callback) {
		this.hideBoxes(false, true);
		this.current.marker.setVisible(true);
		this.toggleMarkers(minDistance < 0);
		if (minDistance > 0) {
			this.getNearby(position, minDistance);
			this.nearby.markers.forEach(marker => this.showMarker(marker));
		}
		if (typeof callback === "function") {
			callback(this.nearby);
		}
	},

	// box
	getBoxContent: function (title, content) {
		return `
			<div${(this.settings.css ? ` class="${this.settings.css}"` : "") + (this.settings.minWidth ? ` style="min-width:${this.settings.minWidth}"` : "")}>
				${(title ? `<div class="title">${title}</div>` : "")}
				${(content && content !== "" ? `<div class="content">${content}</div>` : "")}
			</div>`;
	},

	createBox: function (title, content, callback) {
		var box = new google.maps.InfoWindow({
			content: this.getBoxContent(title, content)
		});
		return typeof callback === "function"
			? callback(box)
			: box;
	},

	showBox: function (info, hideAll) {
		if (info && info.box && info.marker) {
			if (hideAll) {
				this.hideBoxes();
			}
			this.setCenter(info.marker.position);
			info.box.open(this.map, info.marker);
		}
	},

	hideBox: function (info) {
		if (info && info.box) {
			info.box.close();
		}
	},

	hideBoxes: function (hideCurrent, hideDirections, visible) {
		this.markers.forEach(info => this.hideMarker(info, true));
		if (!!hideCurrent && this.current) {
			this.hideMarker(this.current);
		}
		if (!!hideDirections && this.directions.markers.length) {
			this.directions.markers.forEach(marker => this.hideMarker(marker, visible));
		}
	},

	// marker
	createMarker: function (position, title, icon, callback) {
		var marker = new google.maps.Marker({
			map: this.map,
			position: position,
			title: title,
			icon: icon || this.settings.icon
		});
		return typeof callback === "function"
			? callback(marker)
			: marker;
	},

	showMarker: function (info) {
		info.marker.setVisible(true);
	},

	hideMarker: function (info, visible) {
		if (info.marker) {
			info.marker.setVisible(visible !== undefined ? visible : false);
		}
		if (info.box) {
			info.box.close();
		}
	},

	toggleMarker: function (info, state) {
		if (state) {
			this.showMarker(info);
		}
		else {
			this.hideMarker(info);
		}
	},

	toggleMarkers: function (state) {
		this.markers.forEach(marker => this.toggleMarker(marker, state));
	},

	// URLs
	getMapURL(position) {
		position = this.preparePosition(position);
		return "https://www.google.com/maps/@?api=1&map_action=map&zoom=" + this.settings.zoomOnClick + "&center=" + position.lat() + "," + position.lng();
	},

	getDirectionURL(destination, origin) {
		destination = this.preparePosition(destination);
		return "https://www.google.com/maps/dir/?api=1&travelmode=driving" + (origin ? "&origin=" + origin.lat() + "," + origin.lng() : "") + "&destination=" + destination.lat() + "," + destination.lng();
	}
};
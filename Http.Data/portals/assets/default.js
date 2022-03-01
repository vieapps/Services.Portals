/**
 * --------------------------------------
 * URLs
*/
__vieapps.URLs.get = function (url) {
	url += url.indexOf("?") > 0 ? url.endsWith("&") ? "" : "&" : "?";
	if (!!__vieapps.session.id && !!__vieapps.session.token && !!__vieapps.crypto._jwt) {
		url += `x-session-id=${__vieapps.session.id}&x-app-token=${__vieapps.crypto.jwtEncode(__vieapps.session.token, __vieapps.crypto._jwt, true)}&`;
	}
	url += `x-device-id=${__vieapps.utils.getDeviceID(true)}&x-app-name=${__vieapps.crypto.base64urlEncode(__vieapps.session.app.name)}&x-app-platform=${__vieapps.crypto.base64urlEncode(__vieapps.session.app.platform)}&language=${__vieapps.language}`;
	return url;
};
__vieapps.URLs.getSite = function (url, params) {
	url = url.startsWith("/") ? url.substr(1) : url;
	var base = $("base");
	return (!!base && !!base.length ? base.attr("href") : this.root) + (!!params ? this.get(url) : url);
};
__vieapps.URLs.getPortals = function (url, params) {
	url = url.startsWith("/") ? url : "/" + url;
	return this.portals + (!!params ? this.get(url) : url);
};
__vieapps.URLs.getAPIs = function (serviceName, objectName, query) {
	query = query || {};
	var objectIdentity = query["object-identity"];
	var url = `/~apis/${serviceName}/${objectName}${(!!objectIdentity ? `/${objectIdentity}` : "")}?`;
	Object.keys(query).filter(key => key != "service-name" && key != "object-name" && key != "object-identity" && key != "x-app-token" && key != "x-session-id" && key != "x-device-id").forEach(key => url += `${key}=${encodeURIComponent(query[key])}&`);
	return this.getPortals(url, serviceName !== "discovery" && objectName !== "definitions");
};

/**
 * --------------------------------------
 * sessions
*/
__vieapps.session = {
	id: undefined,
	token: undefined,
	deviceID: undefined,
	app: {
		name: "NGX Websites",
		platform: (!!__vieapps.isMobile ? __vieapps.osInfo : "Desktop") + " PWA"
	},
	oAuths: undefined,
	events: {
		init: undefined,
		in: undefined,
		out: undefined,
		forgot: undefined,
		close: undefined
	},
	state: {
		registered: false,
		logged: false,
		post: false
	},
	update: function (data, callback, verb) {
		var keys = data.keys || data.Keys;
		if (keys) {
			__vieapps.crypto.init({
				jwt: keys.JWT,
				aes: {
					key: keys.AES.Key,
					iv: keys.AES.IV
				},
				rsa: {
					encryptionExponent: keys.RSA.EncryptionExponent,
					decryptionExponent: keys.RSA.DecryptionExponent,
					exponent: keys.RSA.Exponent,
					modulus: keys.RSA.Modulus
				}
			});
		}
		this.id = data.id || data.ID;
		this.token = data.token || (data.Token ? __vieapps.crypto.jwtDecode(data.Token, __vieapps.crypto._jwt) : undefined);
		if (data.DeviceID && this.deviceID !== data.DeviceID) {
			this.deviceID = data.DeviceID;
			localStorage.setItem("vieapps:DeviceID", __vieapps.session.deviceID);
		}
		this.state.registered = !!this.id && !!this.token && !!__vieapps.crypto._aes.key && !!__vieapps.crypto._aes.iv && !!__vieapps.crypto._jwt;
		this.state.logged = this.state.registered && !!this.token.uid && this.token.uid !== "";
		__vieapps.ws.authenticate(verb || (this.state.registered ? "AUTH" : "REG"));
		sessionStorage.setItem("vieapps:Session", __vieapps.crypto.stringify({
			id: this.id,
			token: this.token,
			keys: keys
		}));
		if (typeof callback === "function") {
			callback(this);
		}
	},
	register: function (callback, verb) {
		if (!this.state.registered) {
			try {
				this.update(JSON.parse(sessionStorage.getItem("vieapps:Session") || "{}"), undefined, verb);
			}
			catch (error) {
				console.error("Error occurred while loading a session from the storage", error);
				this.state.registered = false;
			}
		}
		if (!this.state.registered) {
			__vieapps.utils.ajax(
				__vieapps.URLs.getSite("_login", true),
				data => __vieapps.session.update(data, callback, verb),
				error => console.error("Error occurred while registering a session", error)
			);
		}
		else if (typeof callback === "function") {
			callback(this);
		}
	},
	unregister: function (callback) {
		sessionStorage.removeItem("vieapps:Session");
		this.id = undefined;
		this.token = undefined;
		this.state.registered = false;
		this.state.logged = false;
		if (typeof callback === "function") {
			callback(this);
		}
	},
	open: function (mode, callback) {
		this.register();
		var prefix = mode || "login";
		if (!__vieapps.utils.existed(`${prefix}-form`)) {
			var title = "forgot" !== mode
				? "otp" === mode
					? __vieapps.languages.get("users.login.otp.title")
					: __vieapps.languages.get("users.login.login.title")
				: __vieapps.languages.get("users.login.reset.title");
			var account = __vieapps.languages.get("users.login.login.controls.Account.label").toLowerCase();
			var password = "otp" === mode
				? __vieapps.languages.get("users.login.otp.controls.OTP.label").toLowerCase()
				: __vieapps.languages.get("users.login.login.controls.Password").toLowerCase();
			var button = "forgot" !== mode
				? "otp" === mode
					? __vieapps.languages.get("users.login.otp.button")
					: __vieapps.languages.get("users.login.login.buttons.login")
				: __vieapps.languages.get("users.login.reset.renew");
			var hint = "forgot" !== mode
				? "otp" === mode
					? __vieapps.languages.get("users.login.hints.otp")
					: __vieapps.languages.get("users.login.login.buttons.forgot")
				: __vieapps.languages.get("users.login.hints.reset");
			var html = `<!-- Bootsnipp Elegant Login Page -->
			<div id="${prefix}-form" class="vieapps-auth active">
				<div class="d-flex justify-content-center">
					<div class="card">
						<div class="card-header">
							<h3>${title}</h3>
							<div class="d-flex justify-content-end icon">
								<span class="close"><i class="fas fa-times"></i></span>
							</div>
						</div>
						<div class="card-body">
							<div class="d-none message">
								<span></span>
							</div>
							<div class="input-group account">
								<div class="input-group-prepend">
									<span class="input-group-text"><i class="fas fa-user"></i></span>
								</div>
								<input id="${prefix}-account" type="text" class="form-control" maxlength="250" placeholder="${account}"/>
							</div>
							<div class="input-group password">
								<div class="input-group-prepend">
									<span class="input-group-text"><i class="fas fa-key"></i></span>
								</div>
								<input id="${prefix}-password" type="${"otp" === mode ? "number" : "password"}" class="form-control" maxlength="${"otp" === mode ? 12 : 250}" placeholder="${password}"/>
							</div>`;
			if ("forgot" === mode) {
				var confirmPassword = __vieapps.languages.get("users.register.controls.ConfirmPassword").toLowerCase();
				html += `
							<div class="input-group retype-password">
								<div class="input-group-prepend">
									<span class="input-group-text"><i class="fas fa-key"></i></span>
								</div>
								<input id="forgot-retype-password" type="password" class="form-control" maxlength="250" placeholder="${confirmPassword}"/>
							</div>`;
			}
			else if ("otp" === mode) {
				html += `<input id="otp-id" type="hidden"/><input id="otp-info" type="hidden"/>`;
			}
			html += `
							<div class="form-group">
								<button class="btn float-end float-right ${prefix}">${button}</button>
								<div class="spinner-border text-warning float-end float-right d-none spinner" role="status">
									<span class="sr-only">Loading...</span>
								</div>
							</div>
						</div>
						<div class="card-footer d-flex justify-content-center links">
							${"forgot" === mode || "otp" === mode ? `<span>${hint}</span>` : `<a href="#" class="forgot">${hint}</a>`}
						</div>
						`;
			if ("forgot" !== mode && "otp" !== mode && Array.isArray(this.oAuths) && !!this.oAuths.length) {
				var oauths = __vieapps.languages.get("users.login.login.controls.OAuths");
				html += `
						<div class="card-footer oauth">
							<div class="d-flex justify-content-center">
								<h4>${oauths}</h4>
							</div>
							<div class="d-flex justify-content-center">
						`;
				this.oAuths.forEach(provider => html += `<a href="#" data-provider="${provider}" title="${provider}"><i class="fab fa-${provider}"></i></a>`);
				html += `</div></div>`;
			}
			html += `
					</div>
				</div>
			</div>
			`;
			$("body").append(html);
			$(`#${prefix}-form .close`).on("click tap", function () {
				__vieapps.session.close(mode);
			});
			if ("forgot" === mode) {
				$("#forgot-form .forgot").on("click tap", function () {
					__vieapps.session.forgot();
				});
			}
			else if ("otp" === mode) {
				$("#otp-form .otp").on("click tap", function () {
					__vieapps.session.otp();
				});
			}
			else {
				$("#login-form .login").on("click tap", function () {
					__vieapps.session.login();
				});
				$("#login-form .forgot").on("click tap", function (event) {
					event.preventDefault();
					__vieapps.session.close("login", true);
					__vieapps.session.open("forgot");
				});
				$("#login-form .oauth > a").on("click tap", function (event) {
					event.preventDefault();
					__vieapps.session.oauth($(this).data("provider"));
				});
			}
		}
		else {
			$(`#${prefix}-form`).toggleClass("active");
			$(`#${prefix}-form .${prefix}`).removeClass("d-none");
			$(`#${prefix}-form .spinner`).addClass("d-none");
			$(`#${prefix}-form .message`).addClass("d-none");
			$(`#${prefix}-form .account`).removeClass("d-none");
			$(`#${prefix}-form .password`).removeClass("d-none");
			$(`#${prefix}-form .retype-password`).removeClass("d-none");
			$(`#${prefix}-form .links > span`).removeClass("d-none");
		}
		if ("otp" === mode) {
			$("#otp-account").prop("disabled", true);
			$("#otp-password").focus();
		}
		else {
			$(`#${prefix}-account`).prop("disabled", false);
			$(`#${prefix}-account`).focus();
		}
		if (typeof callback === "function") {
			callback(__vieapps.session);
		}
	},
	close: function (mode, dontFireEvent) {
		$(`#${mode || "login"}-form`).toggleClass("active");
		if (!!!dontFireEvent && !!this.events && typeof this.events.close === "function") {
			this.events.close();
		}
	},
	login: function () {
		var account = $("#login-account").val();
		var password = $("#login-password").val();
		if (typeof account !== "string" || account.trim() === "" || account.length > 250 || typeof password !== "string" || password.trim() === "" || password.length > 250) {
			$("#login-account").focus();
			return;
		}
		$("#login-form .login").toggleClass("d-none");
		$("#login-form .spinner").toggleClass("d-none");
		__vieapps.utils.ajax(
			__vieapps.URLs.getSite("_login", true),
			data => {
				if (data.Require2FA) {
					__vieapps.session.close("login", true);
					__vieapps.session.open("otp", () => {
						$("#otp-id").val(data.ID);
						$("#otp-info").val(data.Providers[0].Info);
						$("#otp-account").val(account);
						console.log("The session was logged in with static password, so its require 2FA (OTP)", data.Providers);
					});
				}
				else {
					__vieapps.session.update(data);
					__vieapps.session.close("login", __vieapps.session.events && typeof __vieapps.session.events.in === "function");
					console.log("The session was logged in (" + __vieapps.session.token.uid + ")");
					if (!!__vieapps.session.events && typeof __vieapps.session.events.in === "function") {
						__vieapps.session.events.in(__vieapps.session);
					}
				}
			},
			error => {
				var details = error.responseJSON || JSON.parse(error.responseText || "{'Message':'Unknown error'}");
				console.error("Error occurred while logging in", details);
				if ("InvalidSessionException" === details.Type) {
					__vieapps.session.unregister();
					__vieapps.session.register();
				}
				$("#login-form .login").toggleClass("d-none");
				$("#login-form .spinner").toggleClass("d-none");
				$("#login-form .message").removeClass("d-none");
				$("#login-form .message > span").html(details.Message);
				$("#login-form .message > span").addClass("text-danger");
				$("#login-account").focus();
			},
			"POST",
			__vieapps.crypto.stringify({
				Account: __vieapps.crypto.rsaEncrypt(account.trim()),
				Password: __vieapps.crypto.rsaEncrypt(password)
			})
		);
	},
	otp: function () {
		var otp = $("#otp-password").val();
		if (typeof otp === "undefined" || otp.trim() == "" || otp.length > 12) {
			$("#otp-password").focus();
			return;
		}
		$("#otp-form .otp").toggleClass("d-none");
		$("#otp-form .spinner").toggleClass("d-none");
		var id = $("#otp-id").val();
		var info = $("#otp-info").val();
		__vieapps.utils.ajax(
			__vieapps.URLs.getSite(`_login${this.state.post ? "?x-mode=otp" : ""}`, true),
			data => {
				__vieapps.session.update(data);
				__vieapps.session.close("otp", __vieapps.session.events && typeof __vieapps.session.events.in === "function");
				console.log("The OTP session was logged in (" + __vieapps.session.token.uid + ")");
				if (!!__vieapps.session.events && typeof __vieapps.session.events.in === "function") {
					__vieapps.session.events.in(__vieapps.session);
				}
			},
			error => {
				var details = error.responseJSON || JSON.parse(error.responseText || "{'Message':'Unknown error'}");
				console.error("Error occurred while logging in with OTP", details);
				if ("InvalidSessionException" === details.Type) {
					__vieapps.session.unregister();
					__vieapps.session.close("otp", true);
					__vieapps.session.open("login", () => {
						$("#login-form .message").removeClass("d-none");
						$("#login-form .message > span").html(details.Message);
						$("#login-form .message > span").addClass("text-danger");
					});
				}
				else {
					$("#otp-form .otp").toggleClass("d-none");
					$("#otp-form .spinner").toggleClass("d-none");
					$("#otp-form .message").removeClass("d-none");
					$("#otp-form .message > span").html(details.Message);
					$("#otp-form .message > span").addClass("text-danger");
					$("#otp-password").focus();
				}
			},
			this.state.post ? "POST" : "PUT",
			__vieapps.crypto.stringify({
				ID: __vieapps.crypto.rsaEncrypt(id),
				OTP: __vieapps.crypto.rsaEncrypt(otp),
				Info: __vieapps.crypto.rsaEncrypt(info)
			})
		);
	},
	oauth: function (provider) {
		console.warn("oauth", provider);
	},
	forgot: function () {
		var account = $("#forgot-account").val();
		var password = $("#forgot-password").val();
		var retypePassword = $("#forgot-retype-password").val();
		if (typeof account !== "string" || account.trim() === "" || account.length > 250 || typeof password !== "string" || password.trim() === "" || password.length > 250) {
			$("#forgot-account").focus();
			return;
		}
		else if (password !== retypePassword) {
			$("#forgot-form .message").removeClass("d-none");
			$("#forgot-form .message > span").html(__vieapps.utils.template.format(__vieapps.languages.get("users.login.reset.messages.password"), { account: account }));
			$("#forgot-form .message > span").addClass("text-danger");
			$("#forgot-retype-password").focus();
			return;
		}
		$("#forgot-form .forgot").toggleClass("d-none");
		$("#forgot-form .spinner").toggleClass("d-none");
		$("#forgot-form .message").addClass("d-none");
		__vieapps.utils.ajax(
			__vieapps.URLs.getSite(`_login${this.state.post ? "?x-mode=forgot" : ""}`, true),
			() => {
				console.log("The renew password request has been sent");
				$("#forgot-form .account").addClass("d-none");
				$("#forgot-form .password").addClass("d-none");
				$("#forgot-form .retype-password").addClass("d-none");
				$("#forgot-form .spinner").addClass("d-none");
				$("#forgot-form .links > span").addClass("d-none");
				$("#forgot-form .message").removeClass("d-none");
				$("#forgot-form .message > span").html(__vieapps.utils.template.format(__vieapps.languages.get("users.login.reset.messages.account"), { account: account.trim() }));
				$("#forgot-form .message > span").removeClass("text-danger");
				if (!!__vieapps.session.events && typeof __vieapps.session.events.forgot === "function") {
					__vieapps.session.events.forgot(__vieapps.session);
				}
			},
			error => {
				var details = error.responseJSON || JSON.parse(error.responseText || "{'Message':'Unknown error'}");
				console.error("Error occurred while renewing password", details);
				if ("InvalidSessionException" === details.Type) {
					__vieapps.session.unregister();
					__vieapps.session.register();
				}
				$("#forgot-form .forgot").toggleClass("d-none");
				$("#forgot-form .spinner").toggleClass("d-none");
				$("#forgot-form .message").removeClass("d-none");
				$("#forgot-form .message > span").html(details.Message);
				$("#forgot-form .message > span").addClass("text-danger");
				$("#forgot-account").focus();
			},
			this.state.post ? "POST" : "PATCH",
			__vieapps.crypto.stringify({
				Account: __vieapps.crypto.rsaEncrypt(account.trim()),
				Password: __vieapps.crypto.rsaEncrypt(password)
			})
		);
	},
	activate: function (mode, error) {
		this.unregister();
		this.open("login", () => {
			var message = error && error.Code
				? `${error.Code}: ${error.Message}`
				: "account" === mode
					? __vieapps.languages.get("users.activate.messages.success.account")
					: "email" === mode
						? __vieapps.languages.get("users.activate.messages.success.email")
						: __vieapps.languages.get("users.activate.messages.success.password");
			$("#login-form .message").removeClass("d-none");
			$("#login-form .message > span").html(message);
			if (error && error.Code) {
				$("#login-form .message > span").addClass("text-danger");
			}
			else {
				$("#login-form .message > span").removeClass("text-danger");
			}
		});
	},
	logout: function () {
		__vieapps.utils.ajax(
			__vieapps.URLs.getSite("_logout", true),
			data => {
				__vieapps.session.update(data);
				console.log("The session was logged out");
				if (!!__vieapps.session.events && typeof __vieapps.session.events.out === "function") {
					__vieapps.session.events.out(__vieapps.session);
				}
			},
			error => console.error("Error occurred while logging out", error)
		);
	},
	prepare: function () {
		__vieapps.utils.ajax(
			__vieapps.URLs.getAPIs("discovery", "definitions", { "x-service-name": "users", "x-object-name": "oauths" }),
			data => {
				__vieapps.session.oAuths = data.filter(oauth => oauth !== "");
				sessionStorage.setItem("vieapps:OAuths", __vieapps.crypto.stringify(__vieapps.session.oAuths));
			}
		);
	},
	init: function (callback) {
		if (!!__vieapps.session.events && typeof __vieapps.session.events.init === "function") {
			__vieapps.session.events.init();
		}
		var oAuths = sessionStorage.getItem("vieapps:OAuths");
		if (!!oAuths) {
			this.oAuths = JSON.parse(oAuths);
			setTimeout(() => __vieapps.session.prepare(), 12345);
		}
		else {
			this.prepare();
		}
		var session = JSON.parse(sessionStorage.getItem("vieapps:Session") || "{}");
		if (!!session.id && !!session.token && !!session.keys) {
			if (!!!__vieapps.session.id && !!!__vieapps.session.token) {
				this.register(function () {
					console.log("The session was " + (__vieapps.session.token.uid !== "" ? "authenticated" : "registered"));
					if (typeof callback === "function") {
						callback(this);
					}
				});
			}
			else {
				this.state.registered = true;
				this.state.logged = !!this.token.uid && this.token.uid !== "";
				console.log("The session was " + (this.state.logged ? "authenticated & logged" : "registered"));
				if (typeof callback === "function") {
					callback(this);
				}
			}
		}
		else if (typeof callback === "function") {
			callback(this);
		}
	}
};
setTimeout(() => __vieapps.session.init(), 123);
var __redirect = function (url) {
	__vieapps.utils.redirect(typeof url === "string" && url !== "" ? url : __vieapps.URLs.root);
};
var __prepare = function (dontOpen) {
	__vieapps.session.events.in = __redirect;
	__vieapps.session.events.close = __redirect;
	if (!!!dontOpen) {
		__vieapps.session.register(() => {
			if (__vieapps.session.state.logged) {
				__redirect();
			}
			else {
				__vieapps.session.open("login");
			}
		});
	}
};
var __activate = function (mode, error) {
	__prepare(true);
	__vieapps.session.activate(mode, error);
};
var __login = function (callback, url) {
	__vieapps.session.events.in = function () {
		__redirect(url);
	};
	__vieapps.session.open("login", () => {
		if (typeof callback === "function") {
			callback();
		}
	});
};
var __logout = function (param) {
	__vieapps.session.events.out = typeof param === "function" ? param : undefined;
	if (typeof param === "boolean" && !!param) {
		__vieapps.session.unregister(() => __redirect());
	}
	else {
		__vieapps.session.logout();
	}
};

/**
 * --------------------------------------
 * cryptography
*/
__vieapps.crypto = {
	_aes: {
		key: undefined,
		iv: undefined
	},
	_rsa: new RSA(),
	_jwt: undefined,
	stringify: function (object, replacer) {
		return JSON.stringify(
			object || {},
			(key, value) => typeof replacer === "function"
				? replacer(key, value)
				: typeof value === "undefined"
					? null
					: value instanceof Set || value instanceof Map
						? Array.from(value.entries())
						: value
		);
	},
	md5: function (text) {
		return CryptoJS.MD5(text).toString();
	},
	sign: function (text, key, asBase64) {
		var signature = CryptoJS.enc.Base64.stringify(CryptoJS.HmacSHA256(text, key));
		return !!!asBase64 ? this.toBase64Url(signature) : signature;
	},
	toBase64Url: function (base64) {
		return base64.replace(/=+$/, "").replace(/\+/g, "-").replace(/\//g, "_");
	},
	toBase64: function (base64url) {
		var base64 = base64url.replace(/\-/g, "+").replace(/\_/g, "/");
		switch (base64.length % 4) {
			case 0:
				break;
			case 2:
				base64 += "==";
				break;
			case 3:
				base64 += "=";
				break;
			default:
				throw new Error("Base64-url string is not well-form");
		}
		return base64;
	},
	base64Encode: function (text) {
		return CryptoJS.enc.Base64.stringify(CryptoJS.enc.Utf8.parse(text));
	},
	base64Decode: function (base64) {
		return CryptoJS.enc.Utf8.stringify(CryptoJS.enc.Base64.parse(base64));
	},
	base64urlEncode: function (text) {
		return this.toBase64Url(this.base64Encode(text));
	},
	base64urlDecode: function (base64url) {
		return this.base64Decode(this.toBase64(base64url));
	},
	jsonEncode: function (json) {
		return this.base64urlEncode(this.stringify(json || {}));
	},
	jsonDecode: function (text) {
		return JSON.parse(this.base64urlDecode(text));
	},
	jwtEncode: function (jwt, key, updateIssuedAt) {
		if (!!updateIssuedAt) {
			jwt.iat = Math.round(+new Date() / 1000);
		}
		var encoded = `${this.jsonEncode({ typ: "JWT", alg: "HS256" })}.${this.jsonEncode(jwt)}`;
		return `${encoded}.${this.sign(encoded, key || this._jwt)}`;
	},
	jwtDecode: function (jwt, key, dontVerify) {
		var elements = jwt.split(".");
		var token = this.jsonDecode(elements[1]);
		if (!!dontVerify || (elements.length > 2 && this.sign(`${elements[0]}.${elements[1]}`, key || this._jwt) === elements[2]))
			return token;
		return undefined;
	},
	aesEncrypt: function (text, key, iv) {
		return CryptoJS.AES.encrypt(text, key || this._aes.key, { iv: iv || this._aes.iv }).toString();
	},
	aesDecrypt: function (text, key, iv) {
		return CryptoJS.AES.decrypt(text, key || this._aes.key, { iv: iv || this._aes.iv }).toString(CryptoJS.enc.Utf8);
	},
	rsaEncrypt: function (text) {
		return this._rsa.encrypt(text);
	},
	rsaDecrypt: function (text) {
		return this._rsa.decrypt(text);
	},
	init: function (keys) {
		if (keys.aes) {
			this._aes.key = CryptoJS.enc.Hex.parse(keys.aes.key);
			this._aes.iv = CryptoJS.enc.Hex.parse(keys.aes.iv);
		}
		if (keys.rsa) {
			this._rsa.init(keys.rsa.encryptionExponent || keys.rsa.exponent, keys.rsa.decryptionExponent || keys.rsa.exponent, keys.rsa.modulus);
		}
		if (keys.jwt) {
			this._jwt = keys.aes ? this.aesDecrypt(keys.jwt) : keys.jwt;
		}
	}
};

/**
 * --------------------------------------
 * utility
*/
__vieapps.utils = {
	existed: function (id) {
		if (typeof id === "string" && id.trim() !== "") {
			var existed = $("#" + id);
			return existed && existed.length;
		}
		return false;
	},
	importCss: function (url, id, attributes) {
		if (!this.existed(id) && typeof url === "string" && url.trim() !== "") {
			var element = "<link rel=\"stylesheet\" href=\"" + url + "\""
				+ (typeof id === "string" && id.trim() !== "" ? " id=\"" + id + "\"" : "")
				+ (typeof attributes === "string" && attributes.trim() !== "" ? attributes : "")
				+ "/>";
			$("head").append(element);
		}
	},
	importJs: function (url, id, attributes) {
		if (!this.existed(id) && url != undefined && url.trim() !== "") {
			var element = "<scri" + "pt src=\"" + url + "\""
				+ (typeof id === "string" && id.trim() !== "" ? " id=\"" + id + "\"" : "")
				+ (typeof attributes === "string" && attributes.trim() !== "" ? attributes : "")
				+ "></scri" + "pt>";
			$("body").append(element);
		}
	},
	getQueryParameter: function (name, url) {
		var regex = new RegExp("[?&]" + (name || "x-search-query") + "(=([^&#]*)|&|#|$)");
		var results = regex.exec(url || location.href);
		return results && results.length > 2 && results[2]
			? decodeURIComponent(results[2].replace(/\+/g, " "))
			: undefined;
	},
	onKey: function (event, control, callback) {
		if (event && event.keyCode && event.keyCode === 13 && typeof callback === "function") {
			event.preventDefault();
			if (control && control.value && control.value !== "") {
				callback(control.value);
			}
		}
	},
	addDataAttribute: function (element, key, value, callback) {
		$(element).attr("data-" + key.toLowerCase(), value);
		if (typeof callback === "function") {
			callback(element);
		}
	},
	openWindow: function (url, target, width, height, options) {
		if (url != undefined && url !== null && url !== "" && url !== "#") {
			window.open(url, target || "_blank", `width=${width},height=${height},${options || "left=100,top=100,location=no,status=no,resizeable=yes,toolbar=no"}`);
		}
	},
	redirect: function (param) {
		var url = typeof param === "string" && param.trim() !== "" ? param.trim() : location.href;
		if (typeof param === "boolean" && !!param) {
			var pos = url.indexOf("#");
			param = `${url.indexOf("?") > 0 ? "&" : "?"}x-rnd=${Math.random()}`;
			url = pos > 0
				? url.substr(0, pos) + param + url.substr(pos)
				: url + param;
		}
		location.href = url;
	},
	getDeviceID: function (toBase64Url) {
		if (!!__vieapps.session) {
			if (!!!__vieapps.session.deviceID) {
				__vieapps.session.deviceID = localStorage.getItem("vieapps:DeviceID");
				if (!!!__vieapps.session.deviceID) {
					__vieapps.session.deviceID = `${__vieapps.crypto.md5(navigator.userAgent + Math.random())}@portals`;
					localStorage.setItem("vieapps:DeviceID", __vieapps.session.deviceID);
				}
			}
			return !!toBase64Url ? __vieapps.crypto.base64urlEncode(__vieapps.session.deviceID) : __vieapps.session.deviceID;
		}
		else {
			var deviceID = `${__vieapps.crypto.md5(navigator.userAgent + Math.random())}@portals`;
			return !!toBase64Url ? __vieapps.crypto.base64urlEncode(deviceID) : deviceID;
		}
	},
	ajax: function (url, onSuccess, onError, method, data, contentType) {
		$.ajax(url, {
			crossDomain: true,
			method: method || "GET",
			data: data ? typeof data === "string" ? data : __vieapps.crypto.stringify(data) : "",
			contentType: contentType || "application/json",
			success: function (data, textStatus, jqXHR) {
				if (typeof onSuccess === "function") {
					onSuccess(data, textStatus, jqXHR);
				}
			},
			error: function (jqXHR, textStatus, errorThrown) {
				if (typeof onError === "function") {
					onError(jqXHR, textStatus, errorThrown);
				}
			}
		});
	},
	fetchCountries: function (callback) {
		this.ajax(__vieapps.URLs.getPortals(`/statics/geo/countries.json?v=${Math.random()}`), function (data) {
			__vieapps.utils.countries = data.countries || [];
			__vieapps.countries = __vieapps.utils.countries;
			localStorage.setItem("vieapps:Countries", __vieapps.crypto.stringify(__vieapps.utils.countries));
			if (typeof callback === "function") {
				callback(__vieapps.utils.countries);
			}
		});
	},
	fetchProvinces: function (code, callback) {
		code = code || __vieapps.language.substr(3);
		this.ajax(__vieapps.URLs.getPortals(`/statics/geo/provinces/${code}.json?v=${Math.random()}`), function (data) {
			__vieapps.utils.provinces = __vieapps.utils.provinces || {};
			__vieapps.utils.provinces[code] = data.provinces;
			__vieapps.provinces = __vieapps.utils.provinces;
			localStorage.setItem("vieapps:Provinces", __vieapps.crypto.stringify(__vieapps.utils.provinces));
			if (typeof callback === "function") {
				callback(__vieapps.utils.provinces[code]);
			}
		});
	},
	toURI: function (input) {
		if (typeof input !== "string" || input.trim() === "") {
			return "";
		}
		var result = input.trim();
		result = result.replace(/\s/g, "-").replace(/\&/g, "").replace(/\?/g, "");
		result = result.replace(/\+/g, "").replace(/\//g, "-").replace(/\'/g, "");
		result = result.replace(/\\/g, "-").replace(/\=/g, "").replace(/\,/g, "").replace(/\./g, "-");
		result = result.replace(/\(/g, "").replace(/\)/g, "").replace(/\#/g, "").replace(/\%/g, "");
		result = result.replace(/\`/g, "").replace(/\!/g, "").replace(/\@/g, "").replace(/\$/g, "");
		result = result.replace(/\>/g, "").replace(/\</g, "").replace(/\{/g, "").replace(/\}/g, "");
		result = result.replace(/\[/g, "").replace(/\]/g, "").replace(/\*/g, "").replace(/\^/g, "");
		result = result.replace(/\:/g, "").replace(/\;/g, "").replace(/\|/g, "").replace(/\"/g, "");
		result = result.replace(/\_\-\_/g, "-").replace(/\-\_\-/g, "-").replace(/\-\-\-/g, "-").replace(/\-\-/g, "-");
		return result.toLowerCase();
	},
	toANSI: function (input, asURI) {
		if (typeof input !== "string" || input.trim() === "") {
			return "";
		}
		var result = input.trim();
		// a A
		result = result.replace(/\u00E1/g, "a");
		result = result.replace(/\u00C1/g, "A");
		result = result.replace(/\u00E0/g, "a");
		result = result.replace(/\u00C0/g, "A");
		result = result.replace(/\u1EA3/g, "a");
		result = result.replace(/\u1EA2/g, "A");
		result = result.replace(/\u00E3/g, "a");
		result = result.replace(/\u00C3/g, "A");
		result = result.replace(/\u1EA1/g, "a");
		result = result.replace(/\u1EA0/g, "A");
		result = result.replace(/\u0103/g, "a");
		result = result.replace(/\u0102/g, "A");
		result = result.replace(/\u1EAF/g, "a");
		result = result.replace(/\u1EAE/g, "A");
		result = result.replace(/\u1EB1/g, "a");
		result = result.replace(/\u1EB0/g, "A");
		result = result.replace(/\u1EB3/g, "a");
		result = result.replace(/\u1EB2/g, "A");
		result = result.replace(/\u1EB5/g, "a");
		result = result.replace(/\u1EB4/g, "A");
		result = result.replace(/\u1EB7/g, "a");
		result = result.replace(/\u1EB6/g, "A");
		result = result.replace(/\u00E2/g, "a");
		result = result.replace(/\u00C2/g, "A");
		result = result.replace(/\u1EA5/g, "a");
		result = result.replace(/\u1EA4/g, "A");
		result = result.replace(/\u1EA7/g, "a");
		result = result.replace(/\u1EA6/g, "A");
		result = result.replace(/\u1EA9/g, "a");
		result = result.replace(/\u1EA8/g, "A");
		result = result.replace(/\u1EAB/g, "a");
		result = result.replace(/\u1EAA/g, "A");
		result = result.replace(/\u1EAD/g, "a");
		result = result.replace(/\u1EAC/g, "A");
		// e E
		result = result.replace(/\u00E9/g, "e");
		result = result.replace(/\u00C9/g, "E");
		result = result.replace(/\u00E8/g, "e");
		result = result.replace(/\u00C8/g, "E");
		result = result.replace(/\u1EBB/g, "e");
		result = result.replace(/\u1EBA/g, "E");
		result = result.replace(/\u1EBD/g, "e");
		result = result.replace(/\u1EBC/g, "E");
		result = result.replace(/\u1EB9/g, "e");
		result = result.replace(/\u1EB8/g, "E");
		result = result.replace(/\u00EA/g, "e");
		result = result.replace(/\u00CA/g, "E");
		result = result.replace(/\u1EBF/g, "e");
		result = result.replace(/\u1EBE/g, "E");
		result = result.replace(/\u1EC1/g, "e");
		result = result.replace(/\u1EC0/g, "E");
		result = result.replace(/\u1EC3/g, "e");
		result = result.replace(/\u1EC2/g, "E");
		result = result.replace(/\u1EC5/g, "e");
		result = result.replace(/\u1EC4/g, "E");
		result = result.replace(/\u1EC7/g, "e");
		result = result.replace(/\u1EC6/g, "E");
		// i I
		result = result.replace(/\u00ED/g, "i");
		result = result.replace(/\u00CD/g, "I");
		result = result.replace(/\u00EC/g, "i");
		result = result.replace(/\u00CC/g, "I");
		result = result.replace(/\u1EC9/g, "i");
		result = result.replace(/\u1EC8/g, "I");
		result = result.replace(/\u0129/g, "i");
		result = result.replace(/\u0128/g, "I");
		result = result.replace(/\u1ECB/g, "i");
		result = result.replace(/\u1ECA/g, "I");
		// o O
		result = result.replace(/\u00F3/g, "o");
		result = result.replace(/\u00D3/g, "O");
		result = result.replace(/\u00F2/g, "o");
		result = result.replace(/\u00D2/g, "O");
		result = result.replace(/\u1ECF/g, "o");
		result = result.replace(/\u1ECE/g, "O");
		result = result.replace(/\u00F5/g, "o");
		result = result.replace(/\u00D5/g, "O");
		result = result.replace(/\u1ECD/g, "o");
		result = result.replace(/\u1ECC/g, "O");
		result = result.replace(/\u01A1/g, "o");
		result = result.replace(/\u01A0/g, "O");
		result = result.replace(/\u1EDB/g, "o");
		result = result.replace(/\u1EDA/g, "O");
		result = result.replace(/\u1EDD/g, "o");
		result = result.replace(/\u1EDC/g, "O");
		result = result.replace(/\u1EDF/g, "o");
		result = result.replace(/\u1EDE/g, "O");
		result = result.replace(/\u1EE1/g, "o");
		result = result.replace(/\u1EE0/g, "O");
		result = result.replace(/\u1EE3/g, "o");
		result = result.replace(/\u1EE2/g, "O");
		result = result.replace(/\u00F4/g, "o");
		result = result.replace(/\u00D4/g, "O");
		result = result.replace(/\u1ED1/g, "o");
		result = result.replace(/\u1ED0/g, "O");
		result = result.replace(/\u1ED3/g, "o");
		result = result.replace(/\u1ED2/g, "O");
		result = result.replace(/\u1ED5/g, "o");
		result = result.replace(/\u1ED4/g, "O");
		result = result.replace(/\u1ED7/g, "o");
		result = result.replace(/\u1ED6/g, "O");
		result = result.replace(/\u1ED9/g, "o");
		result = result.replace(/\u1ED8/g, "O");
		// u U
		result = result.replace(/\u00FA/g, "u");
		result = result.replace(/\u00DA/g, "U");
		result = result.replace(/\u00F9/g, "u");
		result = result.replace(/\u00D9/g, "U");
		result = result.replace(/\u1EE7/g, "u");
		result = result.replace(/\u1EE6/g, "U");
		result = result.replace(/\u0169/g, "u");
		result = result.replace(/\u0168/g, "U");
		result = result.replace(/\u1EE5/g, "u");
		result = result.replace(/\u1EE4/g, "U");
		result = result.replace(/\u01B0/g, "u");
		result = result.replace(/\u01AF/g, "U");
		result = result.replace(/\u1EE9/g, "u");
		result = result.replace(/\u1EE8/g, "U");
		result = result.replace(/\u1EEB/g, "u");
		result = result.replace(/\u1EEA/g, "U");
		result = result.replace(/\u1EED/g, "u");
		result = result.replace(/\u1EEC/g, "U");
		result = result.replace(/\u1EEF/g, "u");
		result = result.replace(/\u1EEE/g, "U");
		result = result.replace(/\u1EF1/g, "u");
		result = result.replace(/\u1EF0/g, "U");
		// y Y
		result = result.replace(/\u00FD/g, "y");
		result = result.replace(/\u00DD/g, "Y");
		result = result.replace(/\u1EF3/g, "y");
		result = result.replace(/\u1EF2/g, "Y");
		result = result.replace(/\u1EF7/g, "y");
		result = result.replace(/\u1EF6/g, "Y");
		result = result.replace(/\u1EF9/g, "y");
		result = result.replace(/\u1EF8/g, "Y");
		result = result.replace(/\u1EF5/g, "y");
		result = result.replace(/\u1EF4/g, "Y");
		// d D
		result = result.replace(/\u00D0/g, "D");
		result = result.replace(/\u0110/g, "D");
		result = result.replace(/\u0111/g, "d");
		// spaces
		result = result.replace(/\s\s+/g, " ");
		return !!asURI ? this.toURI(result) : result.trim();
	},
	toRegExp: function (regex) {
		var flags = regex.replace(/.*\/([gimy]*)$/, "$1");
		var pattern = regex.replace(new RegExp("^/(.*?)/" + flags + "$"), "$1");
		return new RegExp(pattern, flags);
	},
	time: {
		diff: function (start, end, unit) {
			unit = 1000 * 60 * (typeof unit !== "undefined" && +unit > 0 ? +unit : 1);	// 1: minutes, 60: hours, 60 * 24: days, 30 * 60 * 24: months, 12 * 30 * 60 * 24: years
			var startTime = (start ? new Date(start) : new Date()).getTime();
			var endTime = (end ? new Date(end) : new Date()).getTime();
			return parseInt((endTime - startTime) / unit);
		},
		getFriendly: function (time) {
			var hour = `0${time.getHours()}`;
			var minute = `0${time.getMinutes()}`;
			return `${hour.substr(hour.length - 2)}:${minute.substr(minute.length - 2)} - ${time.toLocaleDateString(__vieapps.language)}`;
		}
	},
	template: {
		parse: function (template) {
			return (template.match(/{{([^{}]*)}}/g) || []).map(param => ({
				token: param,
				name: param.match(/[\w\.]+/)[0]
			}));
		},
		format: function (template, params) {
			var tokenParams = this.parse(template);
			Object.keys(params).forEach(key => {
				var value = (params[key] || "").toString();
				tokenParams.filter(param => param.name === key).forEach(param => template = template.replace(__vieapps.utils.toRegExp(`/${param.token}/g`), value));
			});
			return template;
		}
	}
};
setTimeout(() => {
	if (!__vieapps.utils.dontFetchCountries) {
		if (!__vieapps.utils.countries || !__vieapps.utils.countries.length) {
			__vieapps.utils.fetchCountries();
		}
		else {
			setTimeout(() => __vieapps.utils.fetchCountries(), 7890);
		}
	}
	if (!__vieapps.utils.dontFetchProvinces) {
		var code = __vieapps.language.substr(3);
		if (!__vieapps.utils.provinces || !__vieapps.utils.provinces[code] || !__vieapps.utils.provinces[code].length) {
			__vieapps.utils.fetchProvinces(code);
		}
		else {
			setTimeout(() => __vieapps.utils.fetchProvinces(code), 7890);
		}
		if (code !== "VN" && (!__vieapps.utils.provinces || !__vieapps.utils.provinces["VN"] || !__vieapps.utils.provinces["VN"].length)) {
			__vieapps.utils.fetchProvinces("VN");
		}
	}
}, 3456);
try {
	__vieapps.utils.countries = JSON.parse(localStorage.getItem("vieapps:Countries") || "[]");
	__vieapps.utils.provinces = JSON.parse(localStorage.getItem("vieapps:Provinces") || "{}");
}
catch { }
__vieapps.countries = __vieapps.utils.countries;
__vieapps.provinces = __vieapps.utils.provinces;

/**
 * --------------------------------------
 * social sharings
*/
__vieapps.shares = {
	facebook: function (event) {
		if (!!event) {
			event.preventDefault();
		}
		var url = $("meta[property='og:url']").attr("content") || location.href;
		url += (url.indexOf("?") > 0 ? "&" : "?") + "utm_source=Facebook&utm_medium=WebPortals&utm_campaign=Shares";
		__vieapps.utils.openWindow(`https://www.facebook.com/sharer.php?u=${encodeURIComponent(url)}`, "_blank", 626, 436);
	},
	twitter: function (event) {
		if (!!event) {
			event.preventDefault();
		}
		var url = $("meta[name='twitter:url']").attr("content") || $("meta[property='og:url']").attr("content") || location.href;
		url += (url.indexOf("?") > 0 ? "&" : "?") + "utm_source=Twitter&utm_medium=WebPortals&utm_campaign=Shares";
		__vieapps.utils.openWindow(`https://twitter.com/share?url=${encodeURIComponent(url)}`, "_blank", 626, 436);
	},
	linkedin: function (event) {
		if (!!event) {
			event.preventDefault();
		}
		var url = $("meta[name='twitter:url']").attr("content") || $("meta[property='og:url']").attr("content") || location.href;
		url += (url.indexOf("?") > 0 ? "&" : "?") + "utm_source=LinkedIn&utm_medium=WebPortals&utm_campaign=Shares";
		__vieapps.utils.openWindow(`https://www.linkedin.com/shareArticle?mini=true&url=${encodeURIComponent(url)}`, "_blank", 626, 436);
	},
	pinterest: function (event) {
		if (!!event) {
			event.preventDefault();
		}
		var url = $("meta[name='twitter:url']").attr("content") || $("meta[property='og:url']").attr("content") || location.href;
		url += (url.indexOf("?") > 0 ? "&" : "?") + "utm_source=Pinterest&utm_medium=WebPortals&utm_campaign=Shares";
		var description = $("meta[name='twitter:title']").attr("content") || $("meta[property='og:title']").attr("content") || document.title;
		var media = $("meta[name='twitter:image']").attr("content") || $("meta[property='og:image']").attr("content") || "";
		__vieapps.utils.openWindow(`https://www.pinterest.com/pin/create/button/?url=${encodeURIComponent(url)}&description=${encodeURIComponent(description)}&media=${encodeURIComponent(media)}`, "_blank", 626, 436);
	}
};

/**
 * --------------------------------------
 * websocket
*/
__vieapps.ws = {
	instance: undefined,
	disabled: false,
	counter: 0,
	successHandlers: {},
	errorHandlers: {},
	schedulingHandlers: [],
	refreshingHandlers: [],
	onMessage: undefined,
	init: function () {
		var session = JSON.parse(sessionStorage.getItem("vieapps:Session") || "{}");
		if (!!session.id && !!session.token && !!session.keys && !!!__vieapps.session.id && !!!__vieapps.session.token) {
			__vieapps.crypto.init({
				jwt: session.keys.JWT,
				aes: {
					key: session.keys.AES.Key,
					iv: session.keys.AES.IV
				},
				rsa: {
					encryptionExponent: session.keys.RSA.EncryptionExponent,
					decryptionExponent: session.keys.RSA.DecryptionExponent,
					exponent: session.keys.RSA.Exponent,
					modulus: session.keys.RSA.Modulus
				}
			});
			__vieapps.session.id = session.id;
			__vieapps.session.token = session.token;
		}
		this.instance = new WebSocket(__vieapps.URLs.get(`${__vieapps.URLs.portals.replace("http://", "ws://").replace("https://", "wss://")}/v`));
		this.instance.onopen = () => {
			console.log("The websocket was opened");
			__vieapps.ws.authenticate();
		};
		this.instance.onclose = () => {
			console.log("The websocket was closed");
			__vieapps.ws.instance = undefined;
		};
		this.instance.onmessage = event => {
			try {
				var json = JSON.parse(event.data || "{}");
				var message = {
					ID: json.ID,
					Type: json.Type || "",
					Data: json.Data || json.Error || {}
				};
				var gotID = typeof message.ID === "string" && message.ID.trim() !== "";
				var errorCallback = gotID ? __vieapps.ws.errorHandlers[message.ID] : undefined;
				var successCallback = gotID ? __vieapps.ws.successHandlers[message.ID] : undefined;
				if (gotID) {
					delete __vieapps.ws.errorHandlers[message.ID];
					delete __vieapps.ws.successHandlers[message.ID];
				}
				if (message.Type === "Error") {
					var error = message.Data || {};
					if (error.Type === "InvalidSessionException") {
						console.error("Invalid session", error);
						__vieapps.session.state.registered = false;
						__vieapps.session.state.logged = false;
						__vieapps.session.register(data => console.warn("The session was re-registered", data), "REG");
					}
					else if (typeof errorCallback === "function") {
						errorCallback(error);
					}
					else {
						console.error("Error occurred while processing", error);
					}
				}
				else if (typeof successCallback === "function") {
					successCallback(message.Data);
				}
				else if (message.Type.endsWith("#Scheduler")) {
					__vieapps.ws.schedulingHandlers.filter(callback => typeof callback === "function").forEach(callback => callback(message.Data));
				}
				else if (message.Type.endsWith("#Refresh")) {
					__vieapps.ws.refreshingHandlers.filter(callback => typeof callback === "function").forEach(callback => callback(message.Data));
				}
			}
			catch (error) {
				console.error("Error occurred while processing a WebSocket message", error instanceof SyntaxError ? "" : error);
			}
			if (typeof __vieapps.ws.onMessage === "function") {
				__vieapps.ws.onMessage(event);
			}
		};
	},
	authenticate: function (verb) {
		var isAuthenticated = !!__vieapps.session.id && !!__vieapps.session.token && !!__vieapps.crypto._jwt;
		var request = {
			ServiceName: "Session",
			Verb: verb || (isAuthenticated ? "AUTH" : "REG"),
			Header: {
				"x-device-id": __vieapps.utils.getDeviceID(),
				"x-app-name": __vieapps.session.app.name,
				"x-app-platform": __vieapps.session.app.platform
			}
		};
		if (isAuthenticated) {
			request.Header["x-device-id"] = __vieapps.crypto.aesEncrypt(__vieapps.utils.getDeviceID());
			request.Header["x-session-id"] = __vieapps.crypto.aesEncrypt(__vieapps.session.id);
			request.Body = {
				"x-app-token": __vieapps.crypto.jwtEncode(__vieapps.session.token, __vieapps.crypto._jwt, true),
				"x-app-name": __vieapps.session.app.name,
				"x-app-platform": __vieapps.session.app.platform
			};
		}
		if (!!this.instance && this.instance.readyState == 1) {
			this.instance.send(__vieapps.crypto.stringify(request));
			console.log("The websocket was " + (isAuthenticated ? "authenticated" : "registered"));
		}
	}
};
if (typeof WebSocket !== "undefined" && !__vieapps.ws.disabled) {
	__vieapps.ws.init();
}
$(window).on("load", function () {
	if (!!__vieapps.ws.instance && __vieapps.ws.instance.readyState === 1 && __vieapps.ws.disabled) {
		__vieapps.ws.instance.close();
		console.log("The websocket was disabled");
	}
});

/**
 * --------------------------------------
 * working with APIs
*/
__vieapps.apis = {
	call: function (request, onSuccess, onError, useXHR) {
		request = request || {};
		if (!!useXHR || !!!__vieapps.ws.instance || __vieapps.ws.disabled || __vieapps.ws.instance.readyState !== 1) {
			__vieapps.utils.ajax(
				__vieapps.URLs.getAPIs(request.ServiceName, request.ObjectName, request.Query),
				onSuccess,
				onError,
				request.Verb || "GET",
				request.Body
			);
		}
		else {
			__vieapps.ws.counter++;
			var id = `cmd-${__vieapps.ws.counter}`;
			if (typeof onSuccess === "function") {
				request["ID"] = id;
				__vieapps.ws.successHandlers[id] = onSuccess;
			}
			if (typeof onError === "function") {
				request["ID"] = id;
				__vieapps.ws.errorHandlers[id] = onError;
			}
			__vieapps.ws.instance.send(__vieapps.crypto.stringify(request));
		}
	},
	fetch: function (serviceName, objectName, objectIdentity, header, query, onSuccess, onError, useXHR) {
		var request = {
			ServiceName: serviceName,
			ObjectName: objectName,
			Header: header || {},
			Query: query || {}
		};
		request.Query["object-identity"] = objectIdentity;
		this.call(request, onSuccess, onError, useXHR);
	},
	fetchDefinition: function (query, callback) {
		this.call({ ServiceName: "discovery", ObjectName: "definitions", Query: query }, callback, undefined, true);
	},

};

/**
 * --------------------------------------
 * paginator
*/
__vieapps.paginator = {
	template: {
		container: "<section class=\"{{css}}\">{{pages}}<section>{{previous}}{{next}}</section></section>",
		previous: "{{anchor}}",
		next: "{{anchor}}",
		pages: {
			container: "<div><ul>{{pages}}</ul></div>",
			page: "<li>{{anchor}}</li>",
			current: "<li><label>{{page}}</label></li>"
		}
	},
	css: {
		container: "cms pagination links",
		previous: "previous",
		next: "next"
	},
	previous: {
		label: __vieapps.language == "vi-VN" ? "Trang trước" : "Previous page",
		icon: "<i class=\"fa fa-arrow-circle-left\"></i>"
	},
	next: {
		label: __vieapps.language == "vi-VN" ? "Trang sau" : "Next page",
		icon: "<i class=\"fa fa-arrow-circle-right\"></i>"
	},
	handlers: {},
	go: function (name, page) {
		var handler = this.handlers[name];
		if (typeof handler === "function") {
			handler(page);
		}
	},
	show: function (selector, name, pageNumber, pageSize, totalRecords, showPages, maxPages, hidePreviousNext) {
		var totalPages = Math.floor(totalRecords / pageSize);
		if (totalRecords - (totalPages * pageSize) > 0)
			totalPages += 1;
		if (totalPages > 1) {
			var previous = `
				<a class="${this.css.previous}" href="#" onclick="__vieapps.paginator.go('${name}', ${pageNumber - 1});return false;">
					${(this.previous && this.previous.icon ? "<span>" + this.previous.icon + "</span>" : "")}
					${(this.previous && this.previous.label ? "<span>" + this.previous.label + "</span>" : "")}
				</a>`;
			if (this.template.previous && this.template.previous != "") {
				previous = __vieapps.utils.template.format(this.template.previous, { anchor: previous });
			}
			var next = `
				<a class="${this.css.next}" href="#" onclick="__vieapps.paginator.go('${name}', ${pageNumber + 1});return false;">
					${(this.next && this.next.label ? "<span>" + this.next.label + "</span>" : "")}
					${(this.next && this.next.icon ? "<span>" + this.next.icon + "</span>" : "")}
				</a>`;
			if (this.template.next && this.template.next != "") {
				next = __vieapps.utils.template.format(this.template.next, { anchor: next });
			}
			var pages = "";
			if (!!showPages) {
				maxPages = maxPages != undefined && ++maxPages < totalPages ? +maxPages : 0;
				var numberOfPages = Math.floor((maxPages - 4) / 2);
				if (numberOfPages < 1)
					numberOfPages = 1;
				var start = pageNumber - numberOfPages;
				var end = pageNumber + numberOfPages;
				while (start < 2) {
					start++;
					end++;
				}
				if (end >= totalPages - 1) {
					end = totalPages - 1;
					while (end - start < numberOfPages)
						start--;
					if (start < 2)
						start = 2;
				}
				var current = this.template.pages.current && this.template.pages.current != "" ? this.template.pages.current : undefined;
				var anchor = `<a href="#" onclick="__vieapps.paginator.go('${name}', 1);return false;">1</a>`;
				pages += __vieapps.utils.template.format(pageNumber == 1 && current ? current : this.template.pages.page, { page: pageNumber, anchor: anchor });
				if (start - 1 > 1) {
					anchor = `<a href="#" onclick="__vieapps.paginator.go('${name}', ${start - 1});return false;">${(start - 1 > 2 ? "..." : start - 1)}</a>`;
					pages += __vieapps.utils.template.format(pageNumber == start - 1 && current ? current : this.template.pages.page, { page: start - 1, anchor: anchor });
				}
				for (var page = start; page <= end; page++) {
					anchor = `<a href="#" onclick="__vieapps.paginator.go('${name}', ${page});return false;">${page}</a>`;
					pages += __vieapps.utils.template.format(pageNumber == page && current ? current : this.template.pages.page, { page: page, anchor: anchor });
				}
				if (end + 1 < totalPages) {
					anchor = `<a href="#" onclick="__vieapps.paginator.go('${name}', ${end + 1});return false;">${(end + 1 < totalPages ? "..." : end + 1)}</a>`;
					pages += __vieapps.utils.template.format(pageNumber == end + 1 && current ? current : this.template.pages.page, { page: end + 1, anchor: anchor });
				}
				anchor = `<a href="#" onclick="__vieapps.paginator.go('${name}', ${totalPages});return false;">${totalPages}</a>`;
				pages += __vieapps.utils.template.format(pageNumber == totalPages && current ? current : this.template.pages.page, { page: totalPages, anchor: anchor });
				pages = this.template.pages.container && this.template.pages.container != ""
					? __vieapps.utils.template.format(this.template.pages.container, { pages: pages })
					: pages;
			}
			if (!!hidePreviousNext) {
				previous = next = "";
			}
			$(selector).html(__vieapps.utils.template.format(this.template.container, {
				css: this.css.container,
				previous: pageNumber > 1 ? previous : "",
				next: pageNumber < totalPages ? next : "",
				pages: pages
			}));
		}
		else {
			this.hide(selector);
		}
	},
	hide: function (selector) {
		$(selector).html("");
	}
};

/**
 * --------------------------------------
 * search
*/
__vieapps.searcher = {
	data: {
		serviceName: "",
		objectName: "",
		systemID: "",
		repositoryID: "",
		repositoryEntityID: "",
		status: "Published",
		query: "",
		scope: "",
		sortBy: { LastModified: "Descending" },
		pagination: {
			TotalRecords: -1,
			TotalPages: 0,
			PageSize: 20,
			PageNumber: 0
		},
		indicators: {
			time: `<i class="far fa-clock"></i>`,
			author: `<i class="far fa-user"></i>`
		}
	},
	getFilterBy: function () {
		var filterBy = {
			Query: undefined,
			And: []
		};
		if (typeof this.data.query === "string" && this.data.query.trim() !== "") {
			filterBy.Query = this.data.query;
		}
		if (typeof this.data.systemID === "string" && this.data.systemID.trim() !== "") {
			filterBy.And.push({
				SystemID: {
					Equals: this.data.systemID
				}
			});
		}
		if (typeof this.data.repositoryID === "string" && this.data.repositoryID.trim() !== "") {
			filterBy.And.push({
				RepositoryID: {
					Equals: this.data.repositoryID
				}
			});
		}
		if (typeof this.data.repositoryEntityID === "string" && this.data.repositoryEntityID.trim() !== "") {
			filterBy.And.push({
				RepositoryEntityID: {
					Equals: this.data.repositoryEntityID
				}
			});
		}
		filterBy.And.push({
			Status: {
				Equals: typeof this.data.status === "string" && this.data.status.trim() !== "" ? this.data.status : "Published"
			}
		});
		return filterBy;
	},
	getRequest: function () {
		return {
			ServiceName: typeof this.data.serviceName === "string" && this.data.serviceName.trim() !== "" ? this.data.serviceName : "portals",
			ObjectName: typeof this.data.objectName === "string" && this.data.objectName.trim() !== "" ? this.data.objectName : "cms.content",
			Verb: "GET",
			Header: {},
			Query: {
				"object-identity": "search",
				"x-request": __vieapps.crypto.jsonEncode({
					FilterBy: this.getFilterBy(),
					SortBy: this.data.sortBy || { LastModified: "Descending" },
					Pagination: this.data.pagination || { TotalRecords: -1, TotalPages: 0, PageSize: 20, PageNumber: 0 }
				}),
				"ShowURLs": "x"
			}
		};
	},
	search: function (callback) {
		__vieapps.apis.call(this.getRequest(), function (data) {
			if (typeof callback === "function") {
				callback(data)
			}
		});
	},
	show: function (selector, items) {
		var html = "";
		(items || []).forEach(item => {
			var url = (item.URL || `~/_permanentlink/${item.RepositoryEntityID}/${item.ID}`).replace("~/", __vieapps.URLs.root);
			var time = new Date(item.PublishedTime || item.Created);
			html += `
				<li class="no thumbnail">
					<h3>
						<a href="${url}">${item.Title}</a>
					</h3>
					<div>
						<span>${this.data.indicators.time} ${time.toLocaleDateString(__vieapps.language)}</span>
						${(item.Author ? `<label>|</label><span>${this.data.indicators.author} ${item.Author}</span>` : "")}
						<span class="summary d-block">${item.Summary || ""}</span>
					</div>
				</li>`;
		});
		$(selector).html(`<ul class="cms list">${html}</ul>`);
	},
	hide: function (selector) {
		$(selector).html(`<div class="loading"></div>`);
	},
	perform: function (resultsSelector, paginationSelector, callback) {
		__vieapps.paginator.hide(paginationSelector);
		this.hide(resultsSelector);
		this.search(function (data) {
			__vieapps.searcher.show(resultsSelector, data.Objects);
			__vieapps.searcher.data.pagination = data.Pagination;
			__vieapps.paginator.handlers["search"] = __vieapps.paginator.handlers["search"] || function (pageNumber) {
				__vieapps.searcher.data.pagination.PageNumber = pageNumber;
				__vieapps.searcher.perform(resultsSelector, paginationSelector, callback);
			};
			__vieapps.paginator.show(paginationSelector, "search", data.Pagination.PageNumber, data.Pagination.PageSize, data.Pagination.TotalRecords);
			if (typeof callback === "function") {
				callback(data)
			}
		});
	},
	open: function (query, mode) {
		if (typeof query === "string" && query.trim() !== "") {
			location.href = __vieapps.URLs.root + (__vieapps.desktops.search || __vieapps.desktops.home || "-default") + "?x-search-query=" + encodeURIComponent(query) + (typeof mode === "string" && mode !== "" ? "&x-search-mode=" + encodeURIComponent(mode) : "") + "&x-language=" + __vieapps.language;
		}
	},
	redirect: function (query, mode) {
		this.open(query, mode);
	}
};

/**
 * --------------------------------------
 * contact/order forms
*/
__vieapps.forms = {
	request: {
		id: undefined,
		repository: undefined,
		system: undefined,
		form: undefined,
		options: undefined
	},
	data: {},
	captcha: {
		Registered: undefined,
		Code: undefined
	},
	addresses: [],
	config: [],
	css: {
		control: "",
		button: "btn-primary"
	},
	resources: {
		form: {
			message: undefined,
			button: undefined,
		},
		captcha: {
			label: undefined,
			button: `<i class="fas fa-sync-alt"></i>`
		}
	},
	processors: {
		prepared: undefined,
		shown: undefined,
		success: undefined,
		error: undefined
	},
	definitions: {},
	fetchDefinition: function (contentTypeID, callback) {
		if (!!!this.definitions[contentTypeID]) {
			__vieapps.apis.fetchDefinition({ "x-service-name": "portals", "x-object-name": "cms.form", "x-content-type-id": contentTypeID }, data => {
				__vieapps.forms.definitions[contentTypeID] = data;
				if (typeof callback === "function") {
					callback();
				}
			});
		}
		else if (typeof callback === "function") {
			callback();
		}
	},
	autoComplete: function (input, items, onClick) {
		var currentFocus;
		input.addEventListener("input", () => {
			currentFocus = -1;
			closeAllLists();
			var value = __vieapps.utils.toANSI(input.value).toLowerCase();
			if (!value) {
				onClick();
				return false;
			}
			var container = document.getElementById("auto-complete-list");
			if (!!!container) {
				container = document.createElement("DIV");
				container.setAttribute("id", "auto-complete-list");
				container.setAttribute("class", "items");
				input.parentNode.appendChild(container);
			}
			container.innerHTML = "";
			items.forEach((item, index) => {
				if (item.ansiTitle.indexOf(value) > -1) {
					var element = document.createElement("DIV");
					element.innerHTML = `${item.title}<input type="hidden" value="${index}"/>`;
					element.addEventListener("click", () => {
						var position = +element.getElementsByTagName("input")[0].value;
						input.value = items[position].title;
						input.focus();
						onClick(items[position]);
						closeAllLists();
						$(input).removeClass("is-invalid");
					});
					container.appendChild(element);
				}
			});
		});
		input.addEventListener("keydown", event => {
			var element = document.getElementById("auto-complete-list");
			if (!!element) {
				element = element.getElementsByTagName("DIV");
			}
			if (event.keyCode == 40) {
				currentFocus++;
				addActive(element);
			}
			else if (event.keyCode == 38) {
				currentFocus--;
				addActive(element);
			}
			else if (event.keyCode == 13) {
				event.preventDefault();
				if (currentFocus > -1 && element) {
					element[currentFocus].click();
				}
			}
		});
		function addActive (element) {
			if (!element) {
				return false;
			}
			removeActive(element);
			if (currentFocus >= element.length) {
				currentFocus = 0;
			}
			if (currentFocus < 0) {
				currentFocus = (element.length - 1);
			}
			element[currentFocus].classList.add("active");
		}
		function removeActive (element) {
			for (var index = 0; index < element.length; index++) {
				element[index].classList.remove("active");
			}
		}
		function closeAllLists () {
			var element = document.getElementById("auto-complete-list");
			if (!!element) {
				element.parentNode.removeChild(element);
			}
		}
		document.addEventListener("click", () => closeAllLists());
	},
	track: function (action) {
		if (!!this.request.options && !!this.request.options.Tracking && !!this.request.options.Tracking.Category) {
			var data = {
				"event_category": this.request.options.Tracking.Category
			};
			if (!!this.request.options.Tracking.Label) {
				data["event_label"] = this.request.options.Tracking.Label;
			}
			if (this.request.options.Tracking.Value !== undefined) {
				data["value"] = this.request.options.Tracking.Value;
			}
			gtag("event", action, data);
		}
	},
	create: function () {
		var container = $(`#${this.request.form}`);
		if (container.length) {
			container[0].outerHTML = `<form autocomplete="off" novalidate><div id="vieapps-form-${this.request.form}" class="vieapps-form row"></div></form>`;
		}
	},
	show: function () {
		this.create();
		this.data = {};
		this.config = this.definitions[this.request.id].filter(ctrl => !ctrl.Hidden);
		this.config.forEach(ctrl => {
			this.data[ctrl.Name] = undefined;
			if (!!ctrl.Options.Label && ctrl.Options.Label.startsWith("{{") && ctrl.Options.Label.endsWith("}}")) {
				ctrl.Options.Label = __vieapps.languages.get(ctrl.Options.Label.substr(2, ctrl.Options.Label.length - 4).trim());
			}
			if (!!ctrl.Options.Description && ctrl.Options.Description.startsWith("{{") && ctrl.Options.Description.endsWith("}}")) {
				ctrl.Options.Description = __vieapps.languages.get(ctrl.Options.Description.substr(2, ctrl.Options.Description.length - 4).trim());
			}
			if (!!ctrl.Options.PlaceHolder && ctrl.Options.PlaceHolder.startsWith("{{") && ctrl.Options.PlaceHolder.endsWith("}}")) {
				ctrl.Options.PlaceHolder = __vieapps.languages.get(ctrl.Options.PlaceHolder.substr(2, ctrl.Options.PlaceHolder.length - 4).trim());
			}
			if (ctrl.Type === "TextArea" || ctrl.Type === "TextEditor") {
				ctrl.Options.Rows = 7;
			}
		});
		var control = this.config.find(ctrl => ctrl.Name === "Address");
		if (!!control) {
			this.addresses = [];
			var country = this.data["Country"] = __vieapps.utils.country || "VN";
			var provinces = __vieapps.utils.provinces[country];
			if (!!provinces && Array.isArray(provinces)) {
				provinces.forEach(province => province.counties.forEach(county => this.addresses.push({
					title: `${county.title}, ${province.title}, ${country}`,
					ansiTitle: __vieapps.utils.toANSI(`${county.title}, ${province.title}, ${country}`).toLowerCase(),
					county: county.title,
					province: province.title,
					country: country
				})));
			}
			if (!!this.addresses.length) {
				this.config.insert({
					Name: "Addresses",
					Type: "Lookup",
					Options: {
						Type: "Address",
						PlaceHolder: __vieapps.languages.get("users.register.controls.Address.placeholder")
					}
				}, control.Order + 1);
				this.config.removeAt(this.config.findIndex(ctrl => ctrl.Name === "County"));
				this.config.removeAt(this.config.findIndex(ctrl => ctrl.Name === "Province"));
				this.config.removeAt(this.config.findIndex(ctrl => ctrl.Name === "Country"));
			}
			else {
				this.config.filter(ctrl => ctrl.Name === "Province" || ctrl.Name === "Country").forEach(ctrl => ctrl.Options.Css = "col-4");
			}
		}
		control = this.config.find(ctrl => ctrl.Name === "Notes");
		if (!!control) {
			control.Options.Rows = 3;
		}
		if (!!this.processors && typeof this.processors.prepared === "function") {
			this.processors.prepared();		
		}
		var id = `vieapps-form-${this.request.form}`;
		var form = $(`#${id}`);
		var controlCss = !!this.css && !!this.css.control ? " " + this.css.control : "";
		this.config.forEach(ctrl => {
			var placeholder = !!ctrl.Options.PlaceHolder ? ` placeholder="${ctrl.Options.PlaceHolder}"` : "";
			var required = !!ctrl.Required ? " required" : "";
			var maxlength = !!ctrl.Options.MaxLength ? ` maxlength="${ctrl.Options.MaxLength}"` : "";
			var css = controlCss + (!!ctrl.Options.Css ? " " + ctrl.Options.Css : "");
			var isAddresses = ctrl.Type === "Lookup" && ctrl.Options.Type === "Address";
			var isContainer = !isAddresses && (ctrl.Type !== "Select" || (ctrl.Type === "Select" && !ctrl.Options.SelectOptions.AsBoxes));
			var html = isAddresses
				? `<div class="col-12${css} auto-complete">`
				: isContainer
					? `<div class="${ctrl.Name === "Phone" || ctrl.Name === "Postal" || ctrl.Options.Type === "number" ? "col-12 col-md-4" : ctrl.Name === "Email" ? "col-12 col-md-8" : "col-12"}${css}">${!!ctrl.Options.Label ? `<label for="${id}-${ctrl.Name}" class="form-label">${ctrl.Options.Label}</label>` : ""}`
					: "";
			if (isAddresses) {
				html += `<input type="text" class="form-control" name="${ctrl.Name}" id="${id}-${ctrl.Name}"${placeholder}/>`;
			}
			else if (ctrl.Type === "TextArea" || ctrl.Type === "TextEditor") {
				var rows = !!ctrl.Options.Rows && +ctrl.Options.Rows > 0 ? +ctrl.Options.Rows : 2;
				html += `<textarea class="form-control" name="${ctrl.Name}" id="${id}-${ctrl.Name}" rows="${rows}"${placeholder}${maxlength}${required}>${ctrl.Options.DefaultValue || ""}</textarea>`;
			}
			else if (ctrl.Type === "Select") {
				var multiple = !!ctrl.Options.Multiple ? " multiple" : "";
				var values = (ctrl.Options.SelectOptions.Values || "");
				var options = (values.indexOf("#;") > 0 ? values.split("#;") : values.indexOf("#") > 0 ? values.split("#") : values.split(";")).map(option => option.split("|"));
				var defaultValue = ctrl.Options.DefaultValue || options.first().first();
				if (!!ctrl.Options.SelectOptions.AsBoxes) {
					html += !!ctrl.Options.Label
						? `<div class="col-12${css} boxes"><label for="${id}-${ctrl.Name}" class="form-label">${ctrl.Options.Label}</label></div>`
						: "";
					options.forEach((option, index) => {
						var value = option.first();
						var text = option.last();
						html += `<div class="form-check form-check-inline${css}">`
							+ `<input class="form-check-input ${ctrl.Name}" type="${!!ctrl.Options.Multiple ? "checkbox" : "radio"}" name="${ctrl.Name}" id="${id}-${ctrl.Name}-${index}" value="${value}"${value === defaultValue ? " checked" : ""}>`
							+ `<label for="${id}-${ctrl.Name}-${index}" class="form-check-label">${text}</label>`
							+ `</div>`
					});
					html += `<div class="col-12 boxes"></div>`;
				}
				else {
					html += `<select class="form-select" name="${ctrl.Name}" id="${id}-${ctrl.Name}"${placeholder}${multiple}${required}/>`
						+ options.map(option => `<option value="${option.first()}"${option.first() === defaultValue ? " selected" : ""}>${option.last() || option.first()}</option>`)
						+ `</select>`;
				}
			}
			else {
				var defaultValue = !!ctrl.Options.DefaultValue ? ` value="${ctrl.Options.DefaultValue}"` : "";
				var max = !!ctrl.Options.MaxValue ? ` max="${ctrl.Options.MaxValue}"` : "";
				var min = !!ctrl.Options.MinValue ? ` min="${ctrl.Options.MinValue}"` : "";
				html += `<input type="${ctrl.Type === "DatePicker" ? !!ctrl.Options.DatePickerOptions.AllowTimes ? "datetime-local" : "date" : ctrl.Options.Type}" class="form-control" name="${ctrl.Name}" id="${id}-${ctrl.Name}"${defaultValue}${placeholder}${maxlength}${min}${max}${required}/>`;
			}
			html += isContainer ? "</div>" : "";
			form.append(html);
			if (ctrl.Type === "Select") {
				var values = (ctrl.Options.SelectOptions.Values || "");
				var options = (values.indexOf("#;") > 0 ? values.split("#;") : values.indexOf("#") > 0 ? values.split("#") : values.split(";")).map(option => option.split("|"));
				this.data[ctrl.Name] = ctrl.Options.DefaultValue || options.first().first();
				if (!!ctrl.Options.SelectOptions.AsBoxes) {
					options.forEach((_, index) => {
						if (!!ctrl.Options.Multiple) {
							$(`#${id}-${ctrl.Name}-${index}`).on("click tap", function () {
								var values = "";
								var name = $(this).attr("name");
								$(`vieapps-form-${__vieapps.forms.request.form} .${name}`).each(function() {
									var ctl = $(this);
									if (ctl.prop("checked")) {
										values += (values !== "" ? "#;" : "") + ctl.val();
									}
								});
								__vieapps.forms.data[name] = values;
							});
						}
						else {
							$(`#${id}-${ctrl.Name}-${index}`).on("click tap", function () {
								var ctl = $(this);
								if (ctl.prop("checked")) {
									__vieapps.forms.data[ctl.attr("name")] = ctl.val();
								}
							});
						}
					});
				}
			}
			else {
				$(`#${id}-${ctrl.Name}`).on("change", function () {
					$(this).removeClass("is-invalid");
				});
			}
		});
		control = this.config.find(ctrl => ctrl.Name === "Addresses");
		var addressesCtrl = !!control ? document.getElementById(`${id}-${control.Name}`) : undefined;
		if (!!addressesCtrl) {
			this.autoComplete(addressesCtrl, this.addresses, data => {
				this.data["County"] = !!!data ? undefined : data.county;
				this.data["Province"] = !!!data ? undefined : data.province;
				this.data["Country"] = !!!data ? undefined : data.country;
			});
		}
		if (!!!this.resources.form.message) {
			this.resources.form.message = __vieapps.languages.get("portals.cms.forms.messages.success");
		}
		if (!!!this.resources.form.button) {
			this.resources.form.button = __vieapps.languages.get("common.buttons.submit");
		}
		if (!!!this.resources.captcha.label) {
			this.resources.captcha.label = __vieapps.languages.get("users.login.reset.controls.Captcha.label");
		}
		if (!!!this.resources.captcha.button) {
			this.resources.captcha.button = `<i class="fas fa-sync-alt"></i>`;
		}
		controlCss = !!this.css && !!this.css.button ? " " + this.css.button : "";
		var buttons = {
			submit: !!this.request.options && !!this.request.options.Buttons && !!this.request.options.Buttons.Submit ? this.request.options.Buttons.Submit : this.resources.form.button,
			captcha: !!this.request.options && !!this.request.options.Buttons && !!this.request.options.Buttons.Captcha ? this.request.options.Buttons.Captcha : this.resources.captcha.button
		};
		var footer = __vieapps.session.state.logged
			? `<div class="col-12">`
				+ `<button id="${id}-submit" class="btn${controlCss} submit float-end float-right" disabled>${buttons.submit}</button>`
				+ `</div>`
			: `<div class="col-12 input-group">`
				+ `<label for="${id}-code" class="input-group-text">${this.resources.captcha.label}</label>`
				+ `<img id="${id}-image" class="input-group-text"/>`
				+ `<button id="${id}-refresh" class="btn btn-secondary" disabled>${buttons.captcha}</button>`
				+ `<input id="${id}-code" type="text" class="captcha form-control" maxlength="9" required/>`
				+ `<button id="${id}-submit" class="btn${controlCss} submit" disabled>${buttons.submit}</button>`
				+ `</div>`;
		form.append(footer);
		this.refresh(() => {
			var captcha = $(`#${id}-refresh`);
			captcha.prop("disabled", false);
			captcha.on("click tap", function (event) {
				event.preventDefault();
				__vieapps.forms.refresh(() => $(`#${id}-code`).focus());
			});
			$(`#${id}-code`).on("change", function () {
				$(this).removeClass("is-invalid");
			});
			var submit = $(`#${id}-submit`);
			submit.prop("disabled", false);
			submit.on("click tap", function (event) {
				event.preventDefault();
				__vieapps.forms.submit();
			});
			this.track("Open");
			if (!!this.processors && typeof this.processors.shown === "function") {
				this.processors.shown();
			}
		});
	},
	success: function (message, callback) {
		this.create();
		var id = `vieapps-form-${this.request.form}`;
		var control = $(`#${id}`);
		control.html(`<div class="alert alert-success col-12" role="alert"><span class="close float-end float-right"><i class="fas fa-times"></i></span><h4 class="alert-heading">${message}</h4></div>`);
		$(`#${id} .alert .close`).on("click tap", function (event) {
			event.preventDefault();
			$(`#vieapps-form-${__vieapps.forms.request.form}`).html("");
			__vieapps.forms.show();
		});
		$("html,body").stop().animate({ scrollTop: control.offset().top - 100 }, 456);
		if (typeof callback === "function") {
			callback();
		}
	},
	refresh: function (callback) {
		this.captcha.Code = undefined;
		__vieapps.apis.call(
			{ ServiceName: "users", ObjectName: "captcha", Query: { register: __vieapps.session.id } },
			data => {
				this.captcha.Registered = data.Code;
				$(`#vieapps-form-${this.request.form}-code`).val(undefined);
				$(`#vieapps-form-${this.request.form}-image`).attr("src", data.Uri);
				if (typeof callback === "function") {
					callback();
				}
			},
			error => {
				console.error("Error while refreshing", error);
				if (typeof callback === "function") {
					callback();
				}
			}
		);
	},
	submit: function () {
		var id = `vieapps-form-${this.request.form}`;
		var controls = this.config.filter(ctrl => !!ctrl.Required).map(ctrl => $(`#${id}-${ctrl.Name}`)).filter(ctrl => !!!ctrl.val());
		var addCtrl = this.config.find(ctrl => ctrl.Name === "Address");
		var addrCtrl = $(`#${id}-Addresses`);
		if (!!controls.length) {
			controls.forEach(ctrl => ctrl.addClass("is-invalid"));
			if (!!addCtrl && !!addCtrl.Required && ["County", "Province", "Country"].some(name => this.data[name] === undefined) && !!addrCtrl.length) {
				addrCtrl.addClass("is-invalid");
			}
			controls.first().focus();
			return;
		}
		if (!!addCtrl && !!addCtrl.Required && ["County", "Province", "Country"].some(name => this.data[name] === undefined) && !!addrCtrl.length) {
			addrCtrl.addClass("is-invalid");
			addrCtrl.focus();
			return;
		}
		var captcha = $(`#${id}-code`);
		if (!__vieapps.session.state.logged) {
			this.captcha.Code = captcha.val();
			if (!this.captcha.Code || this.captcha.Code.trim() === "") {
				captcha.addClass("is-invalid");
				captcha.focus();
				return;
			}
		}
		Object.keys(this.data).forEach(name => {
			var ctrl = $(`#${id}-${name}`);
			if (!!ctrl.length) {
				this.data[name] = ctrl.val();
			}
		});
		this.data["SystemID"] = this.request.system;
		this.data["RepositoryID"] = this.request.repository;
		this.data["RepositoryEntityID"] = this.request.id;
		if (!__vieapps.session.state.logged) {
			this.data["Captcha"] = this.captcha;
		}
		var submit = $(`#${id}-submit`);
		submit.prop("disabled", true);
		__vieapps.apis.call(
			{
				ServiceName: "portals",
				ObjectName: "cms.form",
				Verb: "POST",
				Body: this.data
			},
			() => this.success(!!this.request.options && !!this.request.options.Message ? this.request.options.Message : this.resources.form.message, () => {
				this.track("Success");
				if (!!this.processors && typeof this.processors.success === "function") {
					this.processors.success();
				}
			}),
			error => {
				console.error("Error while sending", error);
				var message = !!error.Message
					? error.Message
					: !!error.Data && !!error.Data.Message
						? error.Data.Message
						: !!error.responseJSON && !!error.responseJSON.Data && !!error.responseJSON.Data.Message ? error.responseJSON.Data.Message : error.toString();
				this.refresh(() => {
					var control = $(`#${id}-message`);
					if (!!control.length) {
						control.html(message);
					}
					else {
						$(`<div id="${id}-message" class="alert alert-danger col-12" role="alert">${message}</div>`).insertBefore($(`#${id}`).children().first());
						control = $(`#${id}-message`);
					}
					if (message.indexOf("Captcha") > -1) {
						captcha.addClass("is-invalid");
						captcha.focus();
					}
					else {
						var names = ["Name", "Phone", "Email", "Address", "Title"];
						if (names.some(name => message.indexOf(name) > -1 && $(`#${id}-${name}`).length)) {
							control = $(`#${id}-${names.first(name => message.indexOf(name) > -1 && $(`#${id}-${name}`).length)}`);
							control.addClass("is-invalid");
							control.focus();
						}
						else {
							var addCtrl = this.config.find(ctrl => ctrl.Name === "Address");
							var addrCtrl = $(`#${id}-Addresses`);
							names = ["County", "Province", "Country"];
							if (!!addCtrl && !!addCtrl.Required && names.some(name => this.data[name] === undefined) && !!addrCtrl.length) {
								addrCtrl.addClass("is-invalid");
								addrCtrl.focus();
							}
							else {
								$("html,body").stop().animate({ scrollTop: control.offset().top - 100 }, 456);
							}
						}
					}
					submit.prop("disabled", false);
					this.track("Error");
					if (!!this.processors && typeof this.processors.error === "function") {
						this.processors.error();
					}
				});
			}
		);
	}
};
$(window).on("load", function () {
	window.gtag = window.gtag || function () { };
	window.fbq = window.fbq || function () { };
	if (!!__vieapps.forms.request && !!__vieapps.forms.request.id && !!__vieapps.forms.request.form) {
		__vieapps.forms.fetchDefinition(__vieapps.forms.request.id, () => __vieapps.forms.show());
	}
});

/**
 * --------------------------------------
 * i18n languages
*/
__vieapps.languages = {
	fetch: function (language) {
		["common", "users", "portals", "portals.cms"].forEach(service => {
			__vieapps.utils.ajax(__vieapps.URLs.getPortals(`/statics/i18n/${service}/${language}.json?v=${Math.random()}`), data => {
				var languages = __vieapps.languages[language] || {};
				Object.keys(data).forEach(key => {
					var resource = data[key];
					if (typeof resource === "object") {
						var resources = languages[key] || {};
						Object.keys(resource).forEach(name => resources[name] = resource[name]);
						languages[key] = resources;
					}
					else {
						languages[key] = resource;
					}
				});
				__vieapps.languages[language] = languages;
				sessionStorage.setItem(`vieapps:${language}`, __vieapps.crypto.stringify(languages));
			});
		});
	},
	get: function (id, i18n) {
		id = Array.isArray(id) ? id : typeof id === "string" && id !== "" ? id.trim().split(".") : [];
		i18n = i18n || this[__vieapps.language];
		i18n = typeof id.first() === "string" ? i18n[id.first()] : undefined;
		return !!i18n
			? typeof i18n === "string"
				? i18n
				: typeof i18n === "object"
					? this.get(id.restOf(id.length - 1), i18n)
					: undefined
			: undefined;
	}
};
["vi-VN", "en-US"].forEach(language => {
	var languages = sessionStorage.getItem(`vieapps:${language}`);
	if (!!languages) {
		__vieapps.languages[language] = JSON.parse(languages);
		setTimeout(() => __vieapps.languages.fetch(language), 12345);
	}
	else {
		__vieapps.languages.fetch(language);
	}
});

/**
 * --------------------------------------
 * Array extensions
*/
Array.prototype.insert = function (value, index) {
	if (index != undefined && index > -1 && index < this.length) {
		this.splice(index, 0, value);
	}
	else {
		this.push(value);
	}
	return this;
};
Array.prototype.update = function (value, index) {
	if (index != undefined && index > -1 && index < this.length) {
		this[index] = value;
	}
	else {
		this.push(value);
	}
	return this;
};
Array.prototype.merge = function (values, distinct, findIndex) {
	if (values !== undefined && Array.isArray(values)) {
		values.forEach(value => {
			if (!!!distinct) {
				this.push(value);
			}
			else if (typeof findIndex === "function" ? findIndex(value, this) < 0 : this.indexOf(value) < 0) {
				this.push(value);
			}
		});
	}
	return this;
};
Array.prototype.clear = function (start, amount) {
	this.splice(start !== undefined && start > 0 ? start : 0, amount !== undefined && amount > 0 ? amount : this.length);
	return this;
};
Array.prototype.removeAt = function (index) {
	if (index != undefined && index > -1 && index < this.length) {
		this.splice(index, 1);
	}
	return this;
};
Array.prototype.remove = function (value, findIndex) {
	return this.removeAt(typeof findIndex === "function" ? findIndex(value, this) : this.indexOf(value));
};
Array.prototype.removeAll = function () {
	return this.clear();
};
Array.prototype.move = function (from, to) {
	if (from != undefined && to != undefined && from !== to && from > -1 && from < this.length && to > -1 && to < this.length) {
		var values = this.splice(from, 1);
		if (values != undefined && values.length > 0) {
			this.insert(values[0], to);
		}
	}
	return this;
};
Array.prototype.take = function (amount, skip) {
	var values = skip != undefined && skip > 0 && skip < this.length
		? this.slice(skip)
		: this;
	return amount != undefined && amount > 0 && amount < values.length
		? values.slice(0, amount)
		: values;
};
Array.prototype.restOf = function (amount) {
	return amount !== undefined && amount > 0 && amount < this.length
		? this.take(amount, this.length - amount)
		: this;
};
Array.prototype.distinct = function (comparer) {
	return typeof comparer === "function"
		? this.filter((value, index, array) => comparer(value, index, array))
		: this.filter((value, index, array) => array.indexOf(value) === index);
};
Array.prototype.except = function (other, comparer) {
	return typeof comparer === "function"
		? this.filter(value => comparer(value, other))
		: this.filter(value => other.indexOf(value) < 0);
};
Array.prototype.intersect = function (other, comparer) {
	return typeof comparer === "function"
		? this.filter(value => comparer(value, other))
		: this.filter(value => other.indexOf(value) > -1);
};
Array.prototype.compareFn = function (sorts) {
	var compareFn = (a, b) => a == b ? 0 : a < b ? -1 : 1;
	var sortBy = sorts.map(sort => ({
		name: sort.name,
		compare: (a, b) => (sort.reverse ? -1 : 1) * (typeof sort.transformer === "function" ? compareFn(sort.transformer(a), sort.transformer(b)) : compareFn(a, b))
	}));
	return (a, b) => {
		var result = 0;
		for (var index = 0; index < sortBy.length; index++) {
			var name = sortBy[index].name;
			result = sortBy[index].compare(a[name], b[name]);
			if (result != 0) {
				break;
			}
		}
		return result;
	};
};
Array.prototype.orderBy = function (sorts) {
	return this.sort(sorts != undefined && sorts.length ? this.compareFn(sorts) : undefined);
};
Array.prototype.sortBy = function (...sorts) {
	return this.orderBy(sorts != undefined && sorts.length
		? sorts.filter(sort => sort != undefined && sort != null).map(sort => typeof sort === "string"
			? {
				name: sort,
				reverse: false,
				transformer: undefined
			}
			: {
				name: sort.name,
				reverse: sort.reverse,
				transformer: sort.transformer
			})
		: undefined
	);
};
Array.prototype.first = function (predicate) {
	return typeof predicate === "function" ? this.find(predicate) : this.slice(0, 1).pop();
};
Array.prototype.firstOrDefault = function (predicate) {
	return this.first(predicate) || this.first();
};
Array.prototype.last = function (predicate) {
	return (typeof predicate === "function" ? this.filter(predicate) : this).slice(-1).pop();
};
Array.prototype.lastOrDefault = function (predicate) {
	return this.last(predicate) || this.last();
};
Array.prototype.previousLast = function (predicate) {
	const array = (typeof predicate === "function" ? this.filter(predicate) : this).slice(-2);
	return array.length > 1 ? array.first() : undefined;
};

/**
 * --------------------------------------
 * Set extentions
*/
Set.prototype.contains = function (value) {
	return this.has(value);
};
Set.prototype.merge = function (values) {
	if (values !== undefined && values instanceof Set) {
		values.forEach(value => this.add(value));
	}
	return this;
};
Set.prototype.concat = function (values) {
	return this.merge(values);
};
Set.prototype.filter = function (predicate) {
	if (this.size > 0 && typeof predicate !== "function") {
		var set = new Set();
		this.forEach(value => {
			if (predicate(value)) {
				set.add(value);
			}
		});
		return set;
	}
	return this;
};
Set.prototype.find = function (predicate) {
	var set = this.filter(predicate);
	for (var value of set.values()) {
		return value;
	}
	return undefined;
};
Set.prototype.first = function (predicate) {
	return this.find(predicate);
};
Set.prototype.firstOrDefault = function (predicate) {
	return this.find(predicate) || this.find();
};
Set.prototype.except = function (other) {
	return other !== undefined && other instanceof Set ? this.filter(value => !other.has(value)) : this;
};
Set.prototype.intersect = function (other) {
	return other !== undefined && other instanceof Set ? this.filter(value => other.has(value)) : new Set();
};
Set.prototype.toArray = function (predicate) {
	return Array.from(this.filter(predicate).values());
};

/**
 * --------------------------------------
 * Map extentions
*/
Map.prototype.contains = function (key) {
	return this.has(key);
};
Map.prototype.merge = function (values) {
	if (values !== undefined && values instanceof Map) {
		values.forEach((value, key) => this.set(key, value));
	}
	return this;
};
Map.prototype.concat = function (values) {
	return this.merge(values);
};
Map.prototype.filter = function (predicate) {
	if (this.size > 0 && typeof predicate !== "function") {
		var map = new Map();
		this.forEach((value, key) => {
			if (predicate(value)) {
				map.set(key, value);
			}
		});
		return map;
	}
	return this;
};
Map.prototype.find = function (predicate) {
	var map = this.filter(predicate);
	for (var value of map.values()) {
		return value;
	}
	return undefined;
};
Map.prototype.first = function (predicate) {
	return this.find(predicate);
};
Map.prototype.firstOrDefault = function (predicate) {
	return this.find(predicate) || this.find();
};
Map.prototype.except = function (other) {
	if (other !== undefined && other instanceof Map) {
		var map = new Map();
		this.forEach((value, key) => {
			if (!other.has(key)) {
				map.set(key, value);
			}
		});
		return map;
	}
	return this;
};
Map.prototype.intersect = function (other) {
	if (other !== undefined && other instanceof Map) {
		var map = new Map();
		this.forEach((value, key) => {
			if (other.has(key)) {
				map.set(key, value);
			}
		});
		return map;
	}
	return this;
};
Map.prototype.toArray = function (predicate) {
	return Array.from(this.filter(predicate).values());
};

/**
 * --------------------------------------
 * Backwark compatible
*/
function __getQueryParameter(name, url) {
	return __vieapps.utils.getQueryParameter(name, url);
}
function __onkey(event, control, callback) {
	return __vieapps.utils.onKey(event, control, callback);
}
function __open(url, target, width, height, options) {
	return __vieapps.utils.openWindow(url, target, width, height, options);
}
function __facebook() {
	__vieapps.shares.facebook();
}
function __twitter() {
	__vieapps.shares.twitter();
}
function __md5(text) {
	return __vieapps.crypto.md5(text);
}
function __toBase64Url(text) {
	return __vieapps.crypto.base64urlEncode(text);
}
function __jsonToBase64Url(json) {
	return __vieapps.crypto.jsonEncode(json);
}
function __decodeBase64Url(text) {
	return __vieapps.crypto.base64urlDecode(text);
}
function __timeDiff(start, end, unit) {
	return __vieapps.utils.time.diff(start, end, unit);
}
function __timeFriendly(time) {
	return __vieapps.utils.time.getFriendly(time);
}
function __toRegExp(regex) {
	return __vieapps.utils.toRegExp(regex);
}
function __parse(template) {
	return __vieapps.utils.template.parse(template);
}
function __format(template, params) {
	return __vieapps.utils.template.format(template, params);
}
function __getDeviceID() {
	return __vieapps.utils.getDeviceID();
}
function __fetchProvinces(callback) {
	__vieapps.utils.fetchProvinces(callback);
}
function __callAPIs(request, onSuccess, onError, useXHR) {
	__vieapps.apis.call(request, onSuccess, onError, useXHR);
}
function __fetchAPIs(serviceName, objectName, objectIdentity, header, query, onSuccess, onError, useXHR) {
	__vieapps.apis.fetch(serviceName, objectName, objectIdentity, header, query, onSuccess, onError, useXHR);
}
function __search(query, mode) {
	__vieapps.searcher.open(query, mode);
}
function __onsearch(event, control, callback) {
	__onkey(event, control, callback || __search);
}
function ___openImage(url, width, height) {
	if ($.fancybox) {
		$.fancybox.open([{ src: url }], { loop: false });
	}
	else {
		__vieapps.utils.openWindow(url, "_blank", width, height);
	}
}

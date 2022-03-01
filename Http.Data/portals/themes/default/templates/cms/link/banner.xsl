<?xml version="1.0" encoding="utf-8" ?>
<xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform" xmlns:msxsl="urn:schemas-microsoft-com:xslt" xmlns:func="urn:schemas-vieapps-net:xslt">
	<xsl:output method="xml" indent="no"/>

	<xsl:template match="/">
		<xsl:choose>
			<xsl:when test="/VIEApps/Options/AsSlides = 'true' or /VIEApps/Options/BannerAsSlides = 'true' or /VIEApps/Options/DisplayAsSlides = 'true'">
				<xsl:call-template name="DisplayAsSlides"/>
			</xsl:when>
			<xsl:when test="/VIEApps/Options/AsCarousel = 'true' or /VIEApps/Options/BannerAsCarousel = 'true' or /VIEApps/Options/DisplayAsCarousel = 'true'">
				<xsl:call-template name="DisplayAsCarousel"/>
			</xsl:when>
			<xsl:otherwise>
				<xsl:call-template name="DisplayAsLinks"/>
			</xsl:otherwise>
		</xsl:choose>
	</xsl:template>

	<!-- display banner as slides -->
	<xsl:template name="DisplayAsSlides">

		<!-- variables -->
		<xsl:variable name="ID">slides-<xsl:value-of select="/VIEApps/Meta/Portlet/ID"/></xsl:variable>
		<xsl:variable name="Alternative">
			<xsl:choose>
				<xsl:when test="/VIEApps/Options/Alternative = 'true' or /VIEApps/Options/IsAlternative = 'true' or /VIEApps/Options/AsAlternative = 'true' or /VIEApps/Options/AsAlternativeSlides = 'true'">true</xsl:when>
				<xsl:otherwise>false</xsl:otherwise>
			</xsl:choose>
		</xsl:variable>
		<xsl:variable name="ShowTitle">
			<xsl:value-of select="/VIEApps/Options/ShowTitle"/>
		</xsl:variable>
		<xsl:variable name="ShowSummary">
			<xsl:value-of select="/VIEApps/Options/ShowSummary"/>
		</xsl:variable>
		<xsl:variable name="ShowDetailLabel">
			<xsl:value-of select="/VIEApps/Options/ShowDetailLabel"/>
		</xsl:variable>
		<xsl:variable name="DetailLabel">
			<xsl:value-of select="/VIEApps/Options/DetailLabel"/>
		</xsl:variable>
		<xsl:variable name="ShowCaret">
			<xsl:value-of select="/VIEApps/Options/ShowCaret"/>
		</xsl:variable>
		<xsl:variable name="ShowBorders">
			<xsl:choose>
				<xsl:when test="/VIEApps/Options/ShowBorders = 'true' or /VIEApps/Options/ShowTextBorders = 'true' or /VIEApps/Options/TextBorders = 'true'">true</xsl:when>
				<xsl:otherwise>false</xsl:otherwise>
			</xsl:choose>
		</xsl:variable>
		<xsl:variable name="ShowFooter">
			<xsl:choose>
				<xsl:when test="$Alternative = 'true'">
					<xsl:choose>
						<xsl:when test="/VIEApps/Options/ShowFooter != ''">
							<xsl:value-of select="/VIEApps/Options/ShowFooter"/>
						</xsl:when>
						<xsl:otherwise>true</xsl:otherwise>
					</xsl:choose>
				</xsl:when>
				<xsl:otherwise>false</xsl:otherwise>
			</xsl:choose>
		</xsl:variable>
		<xsl:variable name="Loop">
			<xsl:choose>
				<xsl:when test="/VIEApps/Options/Loop != ''">
					<xsl:value-of select="/VIEApps/Options/Loop"/>
				</xsl:when>
				<xsl:otherwise>true</xsl:otherwise>
			</xsl:choose>
		</xsl:variable>
		<xsl:variable name="AutoPlay">
			<xsl:choose>
				<xsl:when test="/VIEApps/Options/AutoPlay != ''">
					<xsl:value-of select="/VIEApps/Options/AutoPlay"/>
				</xsl:when>
				<xsl:otherwise>true</xsl:otherwise>
			</xsl:choose>
		</xsl:variable>
		<xsl:variable name="AutoPlayTimeout">
			<xsl:choose>
				<xsl:when test="/VIEApps/Options/AutoPlayTimeout != ''">
					<xsl:value-of select="/VIEApps/Options/AutoPlayTimeout"/>
				</xsl:when>
				<xsl:otherwise>
					<xsl:choose>
						<xsl:when test="$Alternative = 'true'">7000</xsl:when>
						<xsl:otherwise>5000</xsl:otherwise>
					</xsl:choose>
				</xsl:otherwise>
			</xsl:choose>
		</xsl:variable>
		<xsl:variable name="AutoPlayHoverPause">
			<xsl:choose>
				<xsl:when test="/VIEApps/Options/AutoPlayHoverPause != ''">
					<xsl:value-of select="/VIEApps/Options/AutoPlayHoverPause"/>
				</xsl:when>
				<xsl:otherwise>false</xsl:otherwise>
			</xsl:choose>
		</xsl:variable>
		<xsl:variable name="AutoHeight">
			<xsl:choose>
				<xsl:when test="/VIEApps/Options/AutoHeight != ''">
					<xsl:value-of select="/VIEApps/Options/AutoHeight"/>
				</xsl:when>
				<xsl:otherwise>false</xsl:otherwise>
			</xsl:choose>
		</xsl:variable>
		<xsl:variable name="SmartSpeed">
			<xsl:choose>
				<xsl:when test="/VIEApps/Options/SmartSpeed != ''">
					<xsl:value-of select="/VIEApps/Options/SmartSpeed"/>
				</xsl:when>
				<xsl:otherwise>1500</xsl:otherwise>
			</xsl:choose>
		</xsl:variable>
		<xsl:variable name="Nav">
			<xsl:choose>
				<xsl:when test="$Alternative = 'true'">false</xsl:when>
				<xsl:otherwise>
					<xsl:choose>
						<xsl:when test="/VIEApps/Options/Nav != ''">
							<xsl:value-of select="/VIEApps/Options/Nav"/>
						</xsl:when>
						<xsl:otherwise>false</xsl:otherwise>
					</xsl:choose>
				</xsl:otherwise>
			</xsl:choose>
		</xsl:variable>
		<xsl:variable name="NavText">
			<xsl:choose>
				<xsl:when test="$Alternative = 'true' or $Nav = 'false'">undefined</xsl:when>
				<xsl:otherwise>
					<xsl:choose>
						<xsl:when test="/VIEApps/Options/NavText != ''">
							<xsl:value-of select="/VIEApps/Options/NavText"/>
						</xsl:when>
						<xsl:otherwise>['&lt;span&gt;&lt;i class=&quot;fas fa-chevron-left&quot; aria-hidden=&quot;true&quot;&gt;&lt;/i&gt;&lt;/span&gt;','&lt;span&gt;&lt;i class=&quot;fas fa-chevron-right&quot; aria-hidden=&quot;true&quot;&gt;&lt;/i&gt;&lt;/span&gt;']</xsl:otherwise>
					</xsl:choose>
				</xsl:otherwise>
			</xsl:choose>
		</xsl:variable>
		<xsl:variable name="Dots">
			<xsl:choose>
				<xsl:when test="$Alternative = 'true'">false</xsl:when>
				<xsl:otherwise>
					<xsl:choose>
						<xsl:when test="/VIEApps/Options/Dots != ''">
							<xsl:value-of select="/VIEApps/Options/Dots"/>
						</xsl:when>
						<xsl:otherwise>true</xsl:otherwise>
					</xsl:choose>
				</xsl:otherwise>
			</xsl:choose>
		</xsl:variable>
		<xsl:variable name="Animate">
			<xsl:choose>
				<xsl:when test="/VIEApps/Options/Animate != ''">
					'<xsl:value-of select="/VIEApps/Options/Animate"/>'
				</xsl:when>
				<xsl:otherwise>undefined</xsl:otherwise>
			</xsl:choose>
		</xsl:variable>
		<xsl:variable name="SlidesCss">
			<xsl:choose>
				<xsl:when test="$Alternative = 'true'">banner slides alternative</xsl:when>
				<xsl:otherwise>
					<xsl:choose>
						<xsl:when test="$ShowBorders = 'true' and $AutoHeight = 'true'">banner slides borders auto</xsl:when>
						<xsl:when test="$ShowBorders = 'true'">banner slides borders</xsl:when>
						<xsl:when test="$AutoHeight = 'true'">banner slides auto</xsl:when>
						<xsl:otherwise>banner slides</xsl:otherwise>
					</xsl:choose>
				</xsl:otherwise>
			</xsl:choose>
		</xsl:variable>

		<!-- display -->
		<div class="{$SlidesCss}">
			<div id="{$ID}" class="owl-carousel owl-theme">
				<xsl:for-each select="/VIEApps/Data/Link">
					<xsl:choose>
						<xsl:when test="$AutoHeight = 'true'">
							<div class="item">
								<xsl:call-template name="DisplaySlideContent">
									<xsl:with-param name="ShowTitle" select="$ShowTitle"/>
									<xsl:with-param name="ShowSummary" select="$ShowSummary"/>
									<xsl:with-param name="ShowDetailLabel" select="$ShowDetailLabel"/>
									<xsl:with-param name="DetailLabel" select="$DetailLabel"/>
									<xsl:with-param name="ShowCaret" select="$ShowCaret"/>
								</xsl:call-template>
								<picture>
									<source srcset="{./ThumbnailURL/@Alternative}"/>
									<img alt="{./Title}" src="{./ThumbnailURL}"/>
								</picture>
							</div>
						</xsl:when>
						<xsl:otherwise>
							<div class="item" style="background-image:url({./ThumbnailURL})" data-index="{position() - 1}">
								<xsl:if test="./MobileImageURL != ''">
									<xsl:attribute name="data-bg-mobile">
										<xsl:value-of select="./MobileImageURL"/>
									</xsl:attribute>
								</xsl:if>
								<xsl:if test="./TabletImageURL != ''">
									<xsl:attribute name="data-bg-tablet">
										<xsl:value-of select="./TabletImageURL"/>
									</xsl:attribute>
								</xsl:if>
								<xsl:if test="./DesktopImageURL != ''">
									<xsl:attribute name="data-bg-desktop">
										<xsl:value-of select="./DesktopImageURL"/>
									</xsl:attribute>
								</xsl:if>
								<xsl:choose>
									<xsl:when test="$Alternative = 'true'">
										<div>
											<xsl:attribute name="class">
												<xsl:choose>
													<xsl:when test="position() = 1">content wow animate__fadeInUp</xsl:when>
													<xsl:otherwise>content</xsl:otherwise>
												</xsl:choose>
											</xsl:attribute>
											<div class="container">
												<div class="row">
													<xsl:call-template name="DisplaySlideContent">
														<xsl:with-param name="ShowTitle" select="$ShowTitle"/>
														<xsl:with-param name="ShowSummary" select="$ShowSummary"/>
														<xsl:with-param name="ShowDetailLabel" select="$ShowDetailLabel"/>
														<xsl:with-param name="DetailLabel" select="$DetailLabel"/>
														<xsl:with-param name="ShowCaret" select="$ShowCaret"/>
														<xsl:with-param name="CssClass">col-10</xsl:with-param>
													</xsl:call-template>
												</div>
											</div>
										</div>
									</xsl:when>
									<xsl:otherwise>
										<xsl:call-template name="DisplaySlideContent">
											<xsl:with-param name="ShowTitle" select="$ShowTitle"/>
											<xsl:with-param name="ShowSummary" select="$ShowSummary"/>
											<xsl:with-param name="ShowDetailLabel" select="$ShowDetailLabel"/>
											<xsl:with-param name="DetailLabel" select="$DetailLabel"/>
											<xsl:with-param name="ShowCaret" select="$ShowCaret"/>
										</xsl:call-template>
									</xsl:otherwise>
								</xsl:choose>
							</div>
						</xsl:otherwise>
					</xsl:choose>
				</xsl:for-each>
			</div>
			<xsl:if test="$Alternative = 'true' and $ShowFooter = 'true' and count(/VIEApps/Data/Link) &gt; 1">
				<div id="{$ID}-footer" class="footer wow animate__fadeIn" data-wow-duration="2s">
					<div class="container">
						<div class="row">
							<div class="col-10">
								<ul>
									<xsl:for-each select="/VIEApps/Data/Link">
										<li data-index="{position() - 1}">
											<xsl:if test="position() = 1">
												<xsl:attribute name="class">active</xsl:attribute>
											</xsl:if>
											<span>
												<small>&#xa0;</small>
											</span>
											<h3>
												<xsl:value-of select="./Title"/>
											</h3>
										</li>
									</xsl:for-each>
								</ul>
							</div>
						</div>
					</div>
				</div>
				<style>
					@media (min-width: 768px) {
						.banner.slides .footer ul li {
							width: calc(100% / <xsl:value-of select="count(/VIEApps/Data/Link)"/>);
						}
					}
					body.loaded .banner.slides.alternative .footer ul li.active span small {
						width: 100%;
						transition: width <xsl:value-of select="$AutoPlayTimeout - 500"/>ms;
						transition-delay: 500ms;
					}
				</style>
			</xsl:if>
			<script>
				$(function () {
					var animate = <xsl:value-of select="$Animate"/>;
					animate = !!!animate || animate == 'undefined' || animate == 'null'
						? undefined
						: __vieapps.slideAnimates.indexOf(animate) == -1
							? animate = __vieapps.slideAnimates[Math.floor(Math.random() * __vieapps.slideAnimates.length)]
							: animate;
					animate = !!animate ? 'animate__' + animate : undefined;
					var slides = $('#<xsl:value-of select="$ID"/>');
					slides.owlCarousel({
						loop: <xsl:value-of select="$Loop"/>,
						autoplay: <xsl:value-of select="$AutoPlay"/>,
						autoplayTimeout: <xsl:value-of select="$AutoPlayTimeout"/>,
						autoplayHoverPause: <xsl:value-of select="$AutoPlayHoverPause"/>,
						autoHeight: <xsl:value-of select="$AutoHeight"/>,
						smartSpeed: <xsl:value-of select="$SmartSpeed"/>,
						nav: <xsl:value-of select="$Nav"/>,
						navText: <xsl:value-of select="$NavText" disable-output-escaping="yes"/>,
						dots: <xsl:value-of select="$Dots"/>,
						animateOut: animate,
						items: 1
					});
					<xsl:if test="$Alternative = 'true'">
						var list = $('#<xsl:value-of select="$ID"/>-footer ul');
						list.find('li').on('click tap', function () {
							slides.trigger('to.owl.carousel', [$(this).attr('data-index')]);
						});
						slides.on('changed.owl.carousel', function (event) {
							setTimeout(function () {
								var index = $(event.currentTarget).find('.owl-item.active .item').attr('data-index');
								list.find('li').removeClass('active');
								list.find('li').eq(index).addClass('active');
							}, 123);
						});
					</xsl:if>
				});
			</script>
		</div>
	</xsl:template>

	<xsl:template name="DisplaySlideContent">
		<xsl:param name="ShowTitle"/>
		<xsl:param name="ShowSummary"/>
		<xsl:param name="ShowDetailLabel"/>
		<xsl:param name="DetailLabel"/>
		<xsl:param name="ShowCaret"/>
		<xsl:param name="CssClass"/>
		<xsl:choose>
			<xsl:when test="($ShowTitle = 'true' and ./Title != '') or ($ShowSummary = 'true' and ./Summary != '') or ($ShowDetailLabel = 'true' and $DetailLabel != '')">
				<xsl:variable name="Css">
					<xsl:choose>
						<xsl:when test="$CssClass != ''">
							<xsl:value-of select="$CssClass"/>
						</xsl:when>
						<xsl:otherwise>content</xsl:otherwise>
					</xsl:choose>
				</xsl:variable>
				<div class="{$Css}">
					<xsl:if test="$ShowTitle = 'true' and ./Title != ''">
						<h2>
							<xsl:choose>
								<xsl:when test="./URL != '#'">
									<a href="{./URL}">
										<xsl:value-of select="./Title"/>
									</a>
								</xsl:when>
								<xsl:otherwise>
									<xsl:value-of select="./Title"/>
								</xsl:otherwise>
							</xsl:choose>
						</h2>
					</xsl:if>
					<xsl:if test="$ShowSummary = 'true' and ./Summary != ''">
						<p>
							<xsl:value-of select="./Summary" disable-output-escaping="yes"/>
						</p>
					</xsl:if>
					<xsl:if test="$ShowDetailLabel = 'true' and $DetailLabel != ''">
						<span>
							<xsl:if test="$ShowCaret = 'true'">
								<i class="fas fa-caret-right">&#xa0;</i>
							</xsl:if>
							<xsl:choose>
								<xsl:when test="./URL != '#'">
									<a href="{./URL}">
										<xsl:value-of select="$DetailLabel"/>
									</a>
								</xsl:when>
								<xsl:otherwise>
									<xsl:value-of select="$DetailLabel"/>
								</xsl:otherwise>
							</xsl:choose>
						</span>
					</xsl:if>
				</div>
			</xsl:when>
			<xsl:otherwise>&#xa0;</xsl:otherwise>
		</xsl:choose>
	</xsl:template>

	<!-- display banner as carousel -->
	<xsl:template name="DisplayAsCarousel">

		<!-- variables -->
		<xsl:variable name="ID">carousel-<xsl:value-of select="/VIEApps/Meta/Portlet/ID"/></xsl:variable>
		<xsl:variable name="Loop">
			<xsl:choose>
				<xsl:when test="/VIEApps/Options/Loop != ''">
					<xsl:value-of select="/VIEApps/Options/Loop"/>
				</xsl:when>
				<xsl:otherwise>true</xsl:otherwise>
			</xsl:choose>
		</xsl:variable>
		<xsl:variable name="AutoPlay">
			<xsl:choose>
				<xsl:when test="/VIEApps/Options/AutoPlay != ''">
					<xsl:value-of select="/VIEApps/Options/AutoPlay"/>
				</xsl:when>
				<xsl:otherwise>true</xsl:otherwise>
			</xsl:choose>
		</xsl:variable>
		<xsl:variable name="AutoPlayTimeout">
			<xsl:choose>
				<xsl:when test="/VIEApps/Options/AutoPlayTimeout != ''">
					<xsl:value-of select="/VIEApps/Options/AutoPlayTimeout"/>
				</xsl:when>
				<xsl:otherwise>3000</xsl:otherwise>
			</xsl:choose>
		</xsl:variable>
		<xsl:variable name="AutoPlayHoverPause">
			<xsl:choose>
				<xsl:when test="/VIEApps/Options/AutoPlayHoverPause != ''">
					<xsl:value-of select="/VIEApps/Options/AutoPlayHoverPause"/>
				</xsl:when>
				<xsl:otherwise>false</xsl:otherwise>
			</xsl:choose>
		</xsl:variable>
		<xsl:variable name="AutoHeight">
			<xsl:choose>
				<xsl:when test="/VIEApps/Options/AutoHeight != ''">
					<xsl:value-of select="/VIEApps/Options/AutoHeight"/>
				</xsl:when>
				<xsl:otherwise>false</xsl:otherwise>
			</xsl:choose>
		</xsl:variable>
		<xsl:variable name="AutoWidth">
			<xsl:choose>
				<xsl:when test="/VIEApps/Options/AutoWidth != ''">
					<xsl:value-of select="/VIEApps/Options/AutoWidth"/>
				</xsl:when>
				<xsl:otherwise>false</xsl:otherwise>
			</xsl:choose>
		</xsl:variable>
		<xsl:variable name="Margin">
			<xsl:choose>
				<xsl:when test="/VIEApps/Options/Margin != ''">
					<xsl:value-of select="/VIEApps/Options/Margin"/>
				</xsl:when>
				<xsl:otherwise>15</xsl:otherwise>
			</xsl:choose>
		</xsl:variable>
		<xsl:variable name="Nav">
			<xsl:choose>
				<xsl:when test="/VIEApps/Options/Nav != ''">
					<xsl:value-of select="/VIEApps/Options/Nav"/>
				</xsl:when>
				<xsl:otherwise>true</xsl:otherwise>
			</xsl:choose>
		</xsl:variable>
		<xsl:variable name="NavText">
			<xsl:choose>
				<xsl:when test="/VIEApps/Options/NavText != ''">
					<xsl:value-of select="/VIEApps/Options/NavText"/>
				</xsl:when>
				<xsl:otherwise>['&lt;span&gt;&lt;i class=&quot;fas fa-chevron-left&quot; aria-hidden=&quot;true&quot;&gt;&lt;/i&gt;&lt;/span&gt;','&lt;span&gt;&lt;i class=&quot;fas fa-chevron-right&quot; aria-hidden=&quot;true&quot;&gt;&lt;/i&gt;&lt;/span&gt;']</xsl:otherwise>
			</xsl:choose>
		</xsl:variable>
		<xsl:variable name="Dots">
			<xsl:choose>
				<xsl:when test="/VIEApps/Options/Dots != ''">
					<xsl:value-of select="/VIEApps/Options/Dots"/>
				</xsl:when>
				<xsl:otherwise>false</xsl:otherwise>
			</xsl:choose>
		</xsl:variable>
		<xsl:variable name="Responsive0">
			<xsl:choose>
				<xsl:when test="/VIEApps/Options/Responsive0 != ''">
					<xsl:value-of select="/VIEApps/Options/Responsive0"/>
				</xsl:when>
				<xsl:otherwise>2</xsl:otherwise>
			</xsl:choose>
		</xsl:variable>
		<xsl:variable name="Responsive480">
			<xsl:choose>
				<xsl:when test="/VIEApps/Options/Responsive480 != ''">
					<xsl:value-of select="/VIEApps/Options/Responsive480"/>
				</xsl:when>
				<xsl:otherwise>3</xsl:otherwise>
			</xsl:choose>
		</xsl:variable>
		<xsl:variable name="Responsive768">
			<xsl:choose>
				<xsl:when test="/VIEApps/Options/Responsive768 != ''">
					<xsl:value-of select="/VIEApps/Options/Responsive768"/>
				</xsl:when>
				<xsl:otherwise>4</xsl:otherwise>
			</xsl:choose>
		</xsl:variable>
		<xsl:variable name="Responsive1024">
			<xsl:choose>
				<xsl:when test="/VIEApps/Options/Responsive1024 != ''">
					<xsl:value-of select="/VIEApps/Options/Responsive1024"/>
				</xsl:when>
				<xsl:otherwise>5</xsl:otherwise>
			</xsl:choose>
		</xsl:variable>
		<xsl:variable name="Responsive1200">
			<xsl:choose>
				<xsl:when test="/VIEApps/Options/Responsive1200 != ''">
					<xsl:value-of select="/VIEApps/Options/Responsive1200"/>
				</xsl:when>
				<xsl:otherwise>6</xsl:otherwise>
			</xsl:choose>
		</xsl:variable>

		<!-- display -->
		<div class="banner carousel">
			<div class="owl-carousel owl-theme">
				<xsl:attribute name="id">
					<xsl:value-of select="$ID"/>
				</xsl:attribute>
				<xsl:for-each select="/VIEApps/Data/Link">
					<div class="item">
						<a href="{./URL}">
							<xsl:if test="./Target != ''">
								<xsl:attribute name="target">
									<xsl:value-of select="./Target"/>
								</xsl:attribute>
							</xsl:if>
							<picture>
								<source srcset="{./ThumbnailURL/@Alternative}"/>
								<img alt="" src="{./ThumbnailURL}"/>
							</picture>
						</a>
					</div>
				</xsl:for-each>
			</div>
			<script>
			$(function() {
				$('#<xsl:value-of select="$ID"/>').owlCarousel({
					loop: <xsl:value-of select="$Loop"/>,
					autoplay: <xsl:value-of select="$AutoPlay"/>,
					autoplayTimeout: <xsl:value-of select="$AutoPlayTimeout"/>,
					autoplayHoverPause: <xsl:value-of select="$AutoPlayHoverPause"/>,
					autoHeight: <xsl:value-of select="$AutoHeight"/>,
					autoWidth: <xsl:value-of select="$AutoWidth"/>,
					margin: <xsl:value-of select="$Margin"/>,
					nav: <xsl:value-of select="$Nav"/>,
					navText: <xsl:value-of select="$NavText" disable-output-escaping="yes"/>,
					dots: <xsl:value-of select="$Dots"/>,
	 				responsiveClass: true,
					responsive: {
						0: { items: <xsl:value-of select="$Responsive0"/> },
						480: { items: <xsl:value-of select="$Responsive480"/> },
						768: { items: <xsl:value-of select="$Responsive768"/> },
						1024: { items: <xsl:value-of select="$Responsive1024"/> },
						1200: { items: <xsl:value-of select="$Responsive1200"/> }
					}
				});
			});
			</script>
		</div>

	</xsl:template>

	<!-- display banner as links of image/text -->
	<xsl:template name="DisplayAsLinks">

		<!-- variables -->
		<xsl:variable name="DisplayAsGrid">
			<xsl:value-of select="/VIEApps/Options/DisplayAsGrid"/>
		</xsl:variable>
		<xsl:variable name="GridColumns">
			<xsl:choose>
				<xsl:when test="/VIEApps/Options/GridColumns = 'three'">three</xsl:when>
				<xsl:otherwise>two</xsl:otherwise>
			</xsl:choose>
		</xsl:variable>
		<xsl:variable name="ShowThumbnail">
			<xsl:choose>
				<xsl:when test="/VIEApps/Options/ShowThumbnail = 'true' or /VIEApps/Options/ShowThumbnails = 'true'">true</xsl:when>
				<xsl:otherwise>false</xsl:otherwise>
			</xsl:choose>
		</xsl:variable>
		<xsl:variable name="ShowTitle">
			<xsl:value-of select="/VIEApps/Options/ShowTitle"/>
		</xsl:variable>

		<!-- display -->
		<ul>
			<xsl:attribute name="class">
				<xsl:choose>
					<xsl:when test="$DisplayAsGrid = 'true'">
						cms list grid <xsl:value-of select="$GridColumns"/> columns row banner
					</xsl:when>
					<xsl:otherwise>
						cms list banner
					</xsl:otherwise>
				</xsl:choose>
			</xsl:attribute>
			<xsl:for-each select="/VIEApps/Data/Link">
				<li>
					<xsl:if test="$DisplayAsGrid = 'true'">
						<xsl:attribute name="class">
							<xsl:choose>
								<xsl:when test="$GridColumns = 'three'">
									col-4
								</xsl:when>
								<xsl:otherwise>
									col-6
								</xsl:otherwise>
							</xsl:choose>
						</xsl:attribute>
					</xsl:if>
					<xsl:choose>
						<xsl:when test="$ShowThumbnail = 'true' and ./ThumbnailURL != ''">
							<figure>
								<a href="{./URL}">
									<xsl:if test="./Target != ''">
										<xsl:attribute name="target">
											<xsl:value-of select="./Target"/>
										</xsl:attribute>
									</xsl:if>
									<picture>
										<source srcset="{./ThumbnailURL/@Alternative}"/>
										<img alt="" src="{./ThumbnailURL}"/>
									</picture>
								</a>
							</figure>
						</xsl:when>
						<xsl:otherwise>
							<a href="{./URL}">
								<xsl:if test="./Target != ''">
									<xsl:attribute name="target">
										<xsl:value-of select="./Target"/>
									</xsl:attribute>
								</xsl:if>
								<span>
									<xsl:value-of select="./Title"/>
								</span>
							</a>
						</xsl:otherwise>
					</xsl:choose>
				</li>
			</xsl:for-each>
		</ul>

	</xsl:template>

</xsl:stylesheet>
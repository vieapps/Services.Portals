<?xml version="1.0" encoding="utf-8" ?>
<xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform" xmlns:msxsl="urn:schemas-microsoft-com:xslt" xmlns:func="urn:schemas-vieapps-net:xslt">
	<xsl:output method="xml" indent="no"/>

	<xsl:template match="/">

		<!-- variables -->
		<xsl:variable name="LastModifiedLabel">
			<xsl:value-of select="/VIEApps/Options/LastModifiedLabel"/>
		</xsl:variable>
		<xsl:variable name="ShowSocialShares">
			<xsl:value-of select="/VIEApps/Options/ShowSocialShares"/>
		</xsl:variable>
		<xsl:variable name="ShowTags">
			<xsl:value-of select="/VIEApps/Options/ShowTags"/>
		</xsl:variable>
		<xsl:variable name="ShowOthers">
			<xsl:value-of select="/VIEApps/Options/ShowOthers"/>
		</xsl:variable>
		<xsl:variable name="OthersLabel">
			<xsl:value-of select="/VIEApps/Options/OthersLabel"/>
		</xsl:variable>

		<!-- breadcrumbs -->
		{{breadcrumb-holder}}

		<!-- details of the content -->
		<div class="cms view item">
			<h2>
				<xsl:value-of select="/VIEApps/Data/Item/Title"/>
			</h2>
			<section>
				<xsl:value-of select="/VIEApps/Data/Item/Summary" disable-output-escaping="yes"/>
			</section>
			<div>
				<section>
					<xsl:if test="$ShowSocialShares != 'false'">
						<span class="shares facebook">
							<a href="#">
								<i class="fab fa-facebook">&#xa0;</i>
								Facebook
							</a>
						</span>
						<span class="shares twitter">
							<a href="#">
								<i class="fab fa-twitter">&#xa0;</i>
								Twitter
							</a>
						</span>
						<script>
							$(function() {
								$('.shares.facebook').on('click tap', function() {
									__facebook();
									return false;
								});
								$('.shares.twitter').on('click tap', function() {
									__twitter();
									return false;
								});
							});
						</script>
					</xsl:if>
					<span>
						<xsl:if test="$LastModifiedLabel != ''">
							<span>
								<xsl:value-of select="$LastModifiedLabel"/>
							</span>
						</xsl:if>
						<label>
							<xsl:value-of select="/VIEApps/Data/Item/LastModified/@Short"/>
						</label>
					</span>
				</section>
			</div>
			<xsl:if test="$ShowTags = 'true' and count(/VIEApps/Data/Item/Tags/Tag) &gt; 0">
				<div class="tags">
					<label>
						<i class="fas fa-tags">&#xa0;</i>
					</label>
					<xsl:for-each select="/VIEApps/Data/Item/Tags/Tag">
						<a>
							<xsl:value-of select="."/>
						</a>
					</xsl:for-each>
					<script>
						<xsl:text disable-output-escaping="yes">
						$(function() {
							$('.tags > a').on('click tap', function() {
								__search($(this).text(), 'tags');
							});
						});
						</xsl:text>
					</script>
				</div>
			</xsl:if>
		</div>

		<!-- pagination -->
		{{pagination-holder}}

		<!-- others -->
		<xsl:if test="$ShowOthers = 'true' and count(/VIEApps/Data/Others/Item) &gt; 0">
			<div class="cms content others">
				<xsl:if test="$OthersLabel != ''">
					<label>
						<xsl:value-of select="$OthersLabel"/>
					</label>
				</xsl:if>
				<ul class="cms list grid row">
					<xsl:for-each select="/VIEApps/Data/Others/Item">
						<li class="col-6">
							<xsl:if test="./ThumbnailURL != ''">
								<figure>
									<a href="{./URL}">
										<picture>
											<source srcset="{./ThumbnailURL/@Alternative}"/>
											<img alt="" src="{./ThumbnailURL}"/>
										</picture>
									</a>
								</figure>
							</xsl:if>
							<h3>
								<a href="{./URL}">
									<xsl:value-of select="./Title"/>
								</a>
							</h3>
						</li>
					</xsl:for-each>
				</ul>
			</div>
		</xsl:if>

	</xsl:template>
	
</xsl:stylesheet>
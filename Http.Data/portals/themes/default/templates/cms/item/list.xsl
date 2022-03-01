<?xml version="1.0" encoding="utf-8" ?>
<xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform" xmlns:msxsl="urn:schemas-microsoft-com:xslt" xmlns:func="urn:schemas-vieapps-net:xslt">
	<xsl:output method="xml" indent="no"/>

	<xsl:template match="/">

		<!-- variables -->
		<xsl:variable name="DisplayAsGrid">
			<xsl:value-of select="/VIEApps/Options/DisplayAsGrid"/>
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
		<xsl:variable name="ShowSummary">
			<xsl:value-of select="/VIEApps/Options/ShowSummary"/>
		</xsl:variable>
		<xsl:variable name="ShowDetailLabel">
			<xsl:value-of select="/VIEApps/Options/ShowDetailLabel"/>
		</xsl:variable>
		<xsl:variable name="DetailLabel">
			<xsl:value-of select="/VIEApps/Options/DetailLabel"/>
		</xsl:variable>
		<xsl:variable name="Target">
			<xsl:value-of select="/VIEApps/Options/Target"/>
		</xsl:variable>
		<xsl:variable name="ListCss">
			<xsl:choose>
				<xsl:when test="$DisplayAsGrid = 'true'">
					cms list grid two columns row item
				</xsl:when>
				<xsl:otherwise>
					cms list item
				</xsl:otherwise>
			</xsl:choose>
		</xsl:variable>
		<xsl:variable name="ItemCss">
			<xsl:choose>
				<xsl:when test="$DisplayAsGrid = 'true' and $ShowThumbnail != 'true'">
					col-6 no thumbnail
				</xsl:when>
				<xsl:when test="$DisplayAsGrid = 'true'">
					col-6
				</xsl:when>
				<xsl:when test="$ShowThumbnail != 'true'">
					no thumbnail
				</xsl:when>
			</xsl:choose>
		</xsl:variable>

		<!-- breadcrumbs -->
		{{breadcrumb-holder}}

		<!-- list of contents -->
		<ul class="{$ListCss}">
			<xsl:for-each select="/VIEApps/Data/Item">
				<li>
					<xsl:if test="$ItemCss != ''">
						<xsl:attribute name="class">
							<xsl:value-of select="$ItemCss"/>
						</xsl:attribute>
					</xsl:if>
					<xsl:if test="$ShowThumbnail = 'true' and ./ThumbnailURL != ''">
						<figure>
							<a href="{./URL}">
								<xsl:if test="$DisplayAsGrid = 'true' and $ShowSummary = 'true'">
									<xsl:attribute name="title">
										<xsl:value-of select="./Summary"/>
									</xsl:attribute>
								</xsl:if>
								<xsl:if test="$Target != ''">
									<xsl:attribute name="target">
										<xsl:value-of select="$Target"/>
									</xsl:attribute>
								</xsl:if>
								<picture>
									<source srcset="{./ThumbnailURL/@Alternative}"/>
									<img alt="" src="{./ThumbnailURL}"/>
								</picture>
							</a>
						</figure>
					</xsl:if>
					<xsl:if test="$ShowTitle != 'false'">
						<h2>
							<a href="{./URL}">
								<xsl:if test="$DisplayAsGrid = 'true' and $ShowSummary = 'true'">
									<xsl:attribute name="title">
										<xsl:value-of select="./Summary"/>
									</xsl:attribute>
								</xsl:if>
								<xsl:if test="$Target != ''">
									<xsl:attribute name="target">
										<xsl:value-of select="$Target"/>
									</xsl:attribute>
								</xsl:if>
								<xsl:value-of select="./Title"/>
							</a>
						</h2>
					</xsl:if>
					<xsl:if test="$ShowSummary = 'true'">
						<span>
							<xsl:value-of select="./Summary" disable-output-escaping="yes"/>
						</span>
					</xsl:if>
					<xsl:if test="$ShowDetailLabel = 'true' and $DetailLabel != ''">
						<section>
							<a href="{./URL}">
								<xsl:if test="$Target != ''">
									<xsl:attribute name="target">
										<xsl:value-of select="$Target"/>
									</xsl:attribute>
								</xsl:if>
								<xsl:value-of select="$DetailLabel"/>
							</a>
						</section>
					</xsl:if>
				</li>
			</xsl:for-each>
		</ul>

		<!-- pagination -->
		{{pagination-holder}}

	</xsl:template>
	
</xsl:stylesheet>
<?xml version="1.0" encoding="utf-8" ?>
<xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform" xmlns:msxsl="urn:schemas-microsoft-com:xslt" xmlns:func="urn:schemas-vieapps-net:xslt">
	<xsl:output method="xml" indent="no"/>

	<xsl:template match="/">

		<!-- variables -->
		<xsl:variable name="DisplayAsGrid">
			<xsl:choose>
				<xsl:when test="/VIEApps/Options/DisplayAsGrid != ''">
					<xsl:value-of select="/VIEApps/Options/DisplayAsGrid"/>
				</xsl:when>
				<xsl:when test="/VIEApps/Options/AsGrid != ''">
					<xsl:value-of select="/VIEApps/Options/AsGrid"/>
				</xsl:when>
			</xsl:choose>
		</xsl:variable>
		<xsl:variable name="IsSidebar">
			<xsl:choose>
				<xsl:when test="/VIEApps/Meta/Portlet/Zone = 'Sidebar'">true</xsl:when>
				<xsl:otherwise>false</xsl:otherwise>
			</xsl:choose>
		</xsl:variable>
		<xsl:variable name="Target">
			<xsl:value-of select="/VIEApps/Options/Target"/>
		</xsl:variable>
		<xsl:variable name="ShowSubTitle">
			<xsl:value-of select="/VIEApps/Options/ShowSubTitle"/>
		</xsl:variable>
		<xsl:variable name="ShowTitle">
			<xsl:value-of select="/VIEApps/Options/ShowTitle"/>
		</xsl:variable>
		<xsl:variable name="TitleAfterThumbnail">
			<xsl:choose>
				<xsl:when test="/VIEApps/Options/TitleAfterThumbnail = 'true' or $DisplayAsGrid = 'true' or $IsSidebar = 'true'">true</xsl:when>
				<xsl:otherwise>false</xsl:otherwise>
			</xsl:choose>
		</xsl:variable>
		<xsl:variable name="TitleFixedLines">
			<xsl:value-of select="/VIEApps/Options/TitleFixedLines"/>
		</xsl:variable>
		<xsl:variable name="ShowThumbnail">
			<xsl:choose>
				<xsl:when test="/VIEApps/Options/ShowThumbnail = 'true' or /VIEApps/Options/ShowThumbnails = 'true'">true</xsl:when>
				<xsl:otherwise>false</xsl:otherwise>
			</xsl:choose>
		</xsl:variable>
		<xsl:variable name="ShowThumbnailAsBackgroundImage">
			<xsl:choose>
				<xsl:when test="$ShowThumbnail = 'true' and (/VIEApps/Options/ShowThumbnailAsBackgroundImage = 'true' or /VIEApps/Options/ShowThumbnailsAsBackgroundImage = 'true')">true</xsl:when>
				<xsl:otherwise>false</xsl:otherwise>
			</xsl:choose>
		</xsl:variable>
		<xsl:variable name="ThumbnailBackgroundImageHeight">
			<xsl:value-of select="/VIEApps/Options/ThumbnailBackgroundImageHeight"/>
		</xsl:variable>
		<xsl:variable name="UseDefaultThumbnailWhenHasNoImage">
			<xsl:value-of select="/VIEApps/Options/UseDefaultThumbnailWhenHasNoImage"/>
		</xsl:variable>
		<xsl:variable name="DefaultThumbnailURL">
			<xsl:choose>
				<xsl:when test="/VIEApps/Options/DefaultThumbnailURL != ''">
					<xsl:value-of select="/VIEApps/Options/DefaultThumbnailURL"/>
				</xsl:when>
				<xsl:otherwise>~~/thumbnailpngs/no-image.png</xsl:otherwise>
			</xsl:choose>
		</xsl:variable>
		<xsl:variable name="ShowCategory">
			<xsl:value-of select="/VIEApps/Options/ShowCategory"/>
		</xsl:variable>
		<xsl:variable name="ShowPublishedTime">
			<xsl:value-of select="/VIEApps/Options/ShowPublishedTime"/>
		</xsl:variable>
		<xsl:variable name="ShowAuthor">
			<xsl:value-of select="/VIEApps/Options/ShowAuthor"/>
		</xsl:variable>
		<xsl:variable name="ShowSource">
			<xsl:value-of select="/VIEApps/Options/ShowSource"/>
		</xsl:variable>
		<xsl:variable name="ShowSummary">
			<xsl:value-of select="/VIEApps/Options/ShowSummary"/>
		</xsl:variable>
		<xsl:variable name="SummaryFixedLines">
			<xsl:value-of select="/VIEApps/Options/SummaryFixedLines"/>
		</xsl:variable>
		<xsl:variable name="ShowDetailLabel">
			<xsl:value-of select="/VIEApps/Options/ShowDetailLabel"/>
		</xsl:variable>
		<xsl:variable name="DetailLabel">
			<xsl:value-of select="/VIEApps/Options/DetailLabel"/>
		</xsl:variable>
		<xsl:variable name="ShowMoreLabel">
			<xsl:value-of select="/VIEApps/Options/ShowMoreLabel"/>
		</xsl:variable>
		<xsl:variable name="MoreLabel">
			<xsl:value-of select="/VIEApps/Options/MoreLabel"/>
		</xsl:variable>
		<xsl:variable name="MoreTarget">
			<xsl:value-of select="/VIEApps/Options/MoreTarget"/>
		</xsl:variable>
		<xsl:variable name="MoreURL">
			<xsl:choose>
				<xsl:when test="/VIEApps/Data/Parent/URL != ''">
					<xsl:value-of select="/VIEApps/Data/Parent/URL"/>
				</xsl:when>
				<xsl:when test="/VIEApps/Options/MoreURL != ''">
					<xsl:value-of select="/VIEApps/Options/MoreURL"/>
				</xsl:when>
			</xsl:choose>
		</xsl:variable>
		<xsl:variable name="ShowFirstAsBig">
			<xsl:value-of select="/VIEApps/Options/ShowFirstAsBig"/>
		</xsl:variable>
		<xsl:variable name="Columns">
			<xsl:choose>
				<xsl:when test="$DisplayAsGrid = 'true'">
					<xsl:choose>
						<xsl:when test="/VIEApps/Options/Columns = '3' or /VIEApps/Options/Columns = 'three'">three</xsl:when>
						<xsl:otherwise>two</xsl:otherwise>
					</xsl:choose>
				</xsl:when>
				<xsl:otherwise>one</xsl:otherwise>
			</xsl:choose>
		</xsl:variable>
		<xsl:variable name="ListCss">
			<xsl:choose>
				<xsl:when test="$DisplayAsGrid = 'true'">
					cms list grid <xsl:value-of select="$Columns"/> columns row content
				</xsl:when>
				<xsl:otherwise>
					cms list content
				</xsl:otherwise>
			</xsl:choose>
		</xsl:variable>
		<xsl:variable name="ItemCss">
			<xsl:choose>
				<xsl:when test="$IsSidebar = 'true'">
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
				</xsl:when>
				<xsl:otherwise>
					<xsl:choose>
						<xsl:when test="$DisplayAsGrid = 'true' and $ShowThumbnail != 'true'">
							<xsl:choose>
								<xsl:when test="$Columns = 'three'">col-12 col-sm-4 no thumbnail</xsl:when>
								<xsl:otherwise>col-12 col-sm-6 no thumbnail</xsl:otherwise>
							</xsl:choose>
						</xsl:when>
						<xsl:when test="$DisplayAsGrid = 'true'">
							<xsl:choose>
								<xsl:when test="$Columns = 'three'">col-12 col-sm-4</xsl:when>
								<xsl:otherwise>col-12 col-sm-6</xsl:otherwise>
							</xsl:choose>
						</xsl:when>
						<xsl:when test="$ShowThumbnail != 'true'">
							no thumbnail
						</xsl:when>
					</xsl:choose>
				</xsl:otherwise>
			</xsl:choose>
		</xsl:variable>

		<!-- breadcrumbs -->
		{{breadcrumb-holder}}

		<!-- category as title -->
		<xsl:variable name="ShowCategoryAsTitle">
			<xsl:value-of select="/VIEApps/Options/ShowCategoryAsTitle"/>
		</xsl:variable>
		<xsl:variable name="CategoryTitle">
			<xsl:choose>
				<xsl:when test="/VIEApps/Options/CategoryTitle != ''">
					<xsl:value-of select="/VIEApps/Options/CategoryTitle"/>
				</xsl:when>
				<xsl:when test="/VIEApps/Data/Parent/Title != ''">
					<xsl:value-of select="/VIEApps/Data/Parent/Title"/>
				</xsl:when>
				<xsl:otherwise>
					<xsl:value-of select="/VIEApps/Meta/Portlet/Title"/>
				</xsl:otherwise>
			</xsl:choose>
		</xsl:variable>
		<xsl:variable name="ShowCategoryThumbnail">
			<xsl:value-of select="/VIEApps/Options/ShowCategoryThumbnail"/>
		</xsl:variable>
		<xsl:variable name="CategoryThumbnailURL">
			<xsl:choose>
				<xsl:when test="/VIEApps/Options/CategoryThumbnailURL != ''">
					<xsl:value-of select="/VIEApps/Options/CategoryThumbnailURL"/>
				</xsl:when>
				<xsl:when test="/VIEApps/Data/Parent/ThumbnailURL != ''">
					<xsl:value-of select="/VIEApps/Data/Parent/ThumbnailURL"/>
				</xsl:when>
			</xsl:choose>
		</xsl:variable>
		<xsl:variable name="CategoryThumbnailWidth">
			<xsl:value-of select="/VIEApps/Options/CategoryThumbnailWidth"/>
		</xsl:variable>
		<xsl:variable name="CategoryThumbnailHeight">
			<xsl:value-of select="/VIEApps/Options/CategoryThumbnailHeight"/>
		</xsl:variable>
		<xsl:variable name="ShowCategoryDescription">
			<xsl:value-of select="/VIEApps/Options/ShowCategoryDescription"/>
		</xsl:variable>
		<xsl:variable name="CategoryDescription">
			<xsl:choose>
				<xsl:when test="/VIEApps/Options/CategoryDescription != ''">
					<xsl:value-of select="/VIEApps/Options/CategoryDescription"/>
				</xsl:when>
				<xsl:when test="/VIEApps/Data/Parent/Description != ''">
					<xsl:value-of select="/VIEApps/Data/Parent/Description"/>
				</xsl:when>
			</xsl:choose>
		</xsl:variable>
		<xsl:variable name="CategoryURL">
			<xsl:choose>
				<xsl:when test="/VIEApps/Options/CategoryURL != ''">
					<xsl:value-of select="/VIEApps/Options/CategoryURL"/>
				</xsl:when>
				<xsl:otherwise>
					<xsl:value-of select="$MoreURL"/>
				</xsl:otherwise>
			</xsl:choose>
		</xsl:variable>
		<xsl:if test="$ShowCategoryAsTitle = 'true' and $CategoryTitle != ''">
			<div class="cms list title">
				<h2>
					<xsl:choose>
						<xsl:when test="$CategoryURL != ''">
							<a href="{$CategoryURL}">
								<xsl:value-of select="$CategoryTitle"/>
							</a>
						</xsl:when>
						<xsl:otherwise>
							<xsl:value-of select="$CategoryTitle"/>
						</xsl:otherwise>
					</xsl:choose>
					<xsl:if test="$MoreLabel != '' and $MoreURL != ''">
						<span class="d-none d-md-block">
							<a href="{$MoreURL}">
								<xsl:if test="$MoreTarget != ''">
									<xsl:attribute name="target">
										<xsl:value-of select="$MoreTarget"/>
									</xsl:attribute>
								</xsl:if>
								<xsl:value-of select="$MoreLabel" disable-output-escaping="yes"/>
							</a>
						</span>
					</xsl:if>
				</h2>
				<xsl:if test="$ShowCategoryThumbnail = 'true' and $CategoryThumbnailURL != ''">
					<a href="{$MoreURL}">
						<xsl:if test="$MoreTarget != ''">
							<xsl:attribute name="target">
								<xsl:value-of select="$MoreTarget"/>
							</xsl:attribute>
						</xsl:if>
						<figure>
							<xsl:choose>
								<xsl:when test="$CategoryThumbnailWidth != '' and $CategoryThumbnailHeight != ''">
									<xsl:attribute name="style">width:<xsl:value-of select="$CategoryThumbnailWidth"/>;height:<xsl:value-of select="$CategoryThumbnailHeight"/></xsl:attribute>
								</xsl:when>
								<xsl:when test="$CategoryThumbnailWidth != ''">
									<xsl:attribute name="style">width:<xsl:value-of select="$CategoryThumbnailWidth"/></xsl:attribute>
								</xsl:when>
								<xsl:when test="$CategoryThumbnailHeight != ''">
									<xsl:attribute name="style">height:<xsl:value-of select="$CategoryThumbnailHeight"/></xsl:attribute>
								</xsl:when>
							</xsl:choose>
							<span style="background-image:url({$CategoryThumbnailURL})">&#xa0;</span>
						</figure>
					</a>
				</xsl:if>
				<xsl:if test="$ShowCategoryDescription = 'true' and $CategoryDescription != ''">
					<label>
						<xsl:value-of select="$CategoryDescription" disable-output-escaping="yes"/>
					</label>
				</xsl:if>
			</div>
		</xsl:if>

		<!-- items -->
		<ul class="{$ListCss}">
			<xsl:for-each select="/VIEApps/Data/Content">
				<xsl:call-template name="Item">
					<xsl:with-param name="ItemCss">
						<xsl:choose>
							<xsl:when test="$DisplayAsGrid = 'true' and $ShowThumbnail = 'true' and $ShowFirstAsBig = 'true' and position() = 1">col-12 big</xsl:when>
							<xsl:otherwise><xsl:value-of select="$ItemCss"/></xsl:otherwise>
						</xsl:choose>
					</xsl:with-param>
					<xsl:with-param name="DisplayAsGrid">
						<xsl:value-of select="$DisplayAsGrid"/>
					</xsl:with-param>
					<xsl:with-param name="IsSidebar">
						<xsl:value-of select="$IsSidebar"/>
					</xsl:with-param>
					<xsl:with-param name="IsFirst">
						<xsl:value-of select="position() = 1"/>
					</xsl:with-param>
					<xsl:with-param name="ShowFirstAsBig">
						<xsl:value-of select="$ShowFirstAsBig"/>
					</xsl:with-param>
					<xsl:with-param name="Target">
						<xsl:value-of select="$Target"/>
					</xsl:with-param>
					<xsl:with-param name="ShowSubTitle">
						<xsl:value-of select="$ShowSubTitle"/>
					</xsl:with-param>
					<xsl:with-param name="ShowTitle">
						<xsl:value-of select="$ShowTitle"/>
					</xsl:with-param>
					<xsl:with-param name="TitleAfterThumbnail">
						<xsl:value-of select="$TitleAfterThumbnail"/>
					</xsl:with-param>
					<xsl:with-param name="TitleFixedLines">
						<xsl:value-of select="$TitleFixedLines"/>
					</xsl:with-param>
					<xsl:with-param name="ShowThumbnail">
						<xsl:value-of select="$ShowThumbnail"/>
					</xsl:with-param>
					<xsl:with-param name="UseDefaultThumbnailWhenHasNoImage">
						<xsl:value-of select="$UseDefaultThumbnailWhenHasNoImage"/>
					</xsl:with-param>
					<xsl:with-param name="DefaultThumbnailURL">
						<xsl:value-of select="$DefaultThumbnailURL"/>
					</xsl:with-param>
					<xsl:with-param name="ShowThumbnailAsBackgroundImage">
						<xsl:value-of select="$ShowThumbnailAsBackgroundImage"/>
					</xsl:with-param>
					<xsl:with-param name="ThumbnailBackgroundImageHeight">
						<xsl:value-of select="$ThumbnailBackgroundImageHeight"/>
					</xsl:with-param>
					<xsl:with-param name="ShowCategory">
						<xsl:value-of select="$ShowCategory"/>
					</xsl:with-param>
					<xsl:with-param name="ShowPublishedTime">
						<xsl:value-of select="$ShowPublishedTime"/>
					</xsl:with-param>
					<xsl:with-param name="ShowAuthor">
						<xsl:value-of select="$ShowAuthor"/>
					</xsl:with-param>
					<xsl:with-param name="ShowSource">
						<xsl:value-of select="$ShowSource"/>
					</xsl:with-param>
					<xsl:with-param name="ShowSummary">
						<xsl:value-of select="$ShowSummary"/>
					</xsl:with-param>
					<xsl:with-param name="SummaryFixedLines">
						<xsl:value-of select="$SummaryFixedLines"/>
					</xsl:with-param>
					<xsl:with-param name="ShowDetailLabel">
						<xsl:value-of select="$ShowDetailLabel"/>
					</xsl:with-param>
					<xsl:with-param name="DetailLabel">
						<xsl:value-of select="$DetailLabel"/>
					</xsl:with-param>
				</xsl:call-template>
			</xsl:for-each>
		</ul>

		<!-- see more -->
		<xsl:if test="$ShowMoreLabel = 'true' and $MoreLabel != '' and $MoreURL != ''">
			<section class="cms list more d-md-none">
				<a href="{$MoreURL}">
					<xsl:if test="$MoreTarget != ''">
						<xsl:attribute name="target">
							<xsl:value-of select="$MoreTarget"/>
						</xsl:attribute>
					</xsl:if>
					<xsl:value-of select="$MoreLabel" disable-output-escaping="yes"/>
				</a>
			</section>
		</xsl:if>

		<!-- pagination -->
		{{pagination-holder}}

	</xsl:template>

	<!-- item -->	
	<xsl:template name="Item">
		<xsl:param name="ItemCss"/>
		<xsl:param name="DisplayAsGrid"/>
		<xsl:param name="IsSidebar"/>
		<xsl:param name="IsFirst"/>
		<xsl:param name="ShowFirstAsBig"/>
		<xsl:param name="Target"/>
		<xsl:param name="ShowSubTitle"/>
		<xsl:param name="ShowTitle"/>
		<xsl:param name="TitleAfterThumbnail"/>
		<xsl:param name="TitleFixedLines"/>
		<xsl:param name="ShowThumbnail"/>
		<xsl:param name="UseDefaultThumbnailWhenHasNoImage"/>
		<xsl:param name="DefaultThumbnailURL"/>
		<xsl:param name="ShowThumbnailAsBackgroundImage"/>
		<xsl:param name="ThumbnailBackgroundImageHeight"/>
		<xsl:param name="ShowCategory"/>
		<xsl:param name="ShowPublishedTime"/>
		<xsl:param name="ShowAuthor"/>
		<xsl:param name="ShowSource"/>
		<xsl:param name="ShowSummary"/>
		<xsl:param name="SummaryFixedLines"/>
		<xsl:param name="ShowDetailLabel"/>
		<xsl:param name="DetailLabel"/>
		<xsl:variable name="FixedLines">
			<xsl:if test="$TitleFixedLines != ''">
				<xsl:choose>
					<xsl:when test="$DisplayAsGrid != 'true'">
						<xsl:value-of select="$TitleFixedLines"/>
					</xsl:when>
					<xsl:otherwise>
						<xsl:if test="$ShowFirstAsBig != 'true' or ($ShowFirstAsBig = 'true' and $IsFirst != 'true')">
							<xsl:value-of select="$TitleFixedLines"/>
						</xsl:if>
					</xsl:otherwise>
				</xsl:choose>
			</xsl:if>
		</xsl:variable>
		<xsl:variable name="ThumbnailURL">
			<xsl:choose>
				<xsl:when test="./ThumbnailURL != ''">
					<xsl:value-of select="./ThumbnailURL"/>
				</xsl:when>
				<xsl:when test="./ThumbnailURL = '' and $UseDefaultThumbnailWhenHasNoImage = 'true' and $DefaultThumbnailURL != ''">
					<xsl:value-of select="$DefaultThumbnailURL"/>
				</xsl:when>
			</xsl:choose>
		</xsl:variable>
		<li>
			<xsl:if test="$ItemCss != ''">
				<xsl:attribute name="class">
					<xsl:value-of select="$ItemCss"/>
				</xsl:attribute>
			</xsl:if>
			<xsl:if test="$ShowSubTitle = 'true' and ./SubTitle != ''">
				<label>
					<xsl:value-of select="./SubTitle"/>
				</label>
			</xsl:if>
			<xsl:if test="$ShowTitle != 'false' and $TitleAfterThumbnail = 'false'">
				<h3>
					<a href="{./URL}">
						<xsl:if test="$FixedLines != ''">
							<xsl:attribute name="data-fixed-lines">
								<xsl:value-of select="$FixedLines"/>
							</xsl:attribute>
						</xsl:if>
						<xsl:if test="$Target != ''">
							<xsl:attribute name="target">
								<xsl:value-of select="$Target"/>
							</xsl:attribute>
						</xsl:if>
						<xsl:value-of select="./Title"/>
					</a>
				</h3>
			</xsl:if>
			<xsl:if test="$ShowThumbnail = 'true' and $ThumbnailURL != ''">
				<a href="{./URL}">
					<xsl:if test="$Target != ''">
						<xsl:attribute name="target">
							<xsl:value-of select="$Target"/>
						</xsl:attribute>
					</xsl:if>
					<figure>
						<xsl:choose>
							<xsl:when test="$ShowThumbnailAsBackgroundImage = 'true'">
								<xsl:attribute name="class">
									<xsl:choose>
										<xsl:when test="$IsSidebar != 'true' and $ShowFirstAsBig = 'true' and $IsFirst = 'true'">background thumbnail wow animate__animated animate__fadeIn</xsl:when>
										<xsl:otherwise>background thumbnail</xsl:otherwise>
									</xsl:choose>
								</xsl:attribute>
								<xsl:if test="$ThumbnailBackgroundImageHeight != '' and $ShowFirstAsBig != 'true' and $IsFirst != 'true'">
									<xsl:attribute name="style">height:<xsl:value-of select="$ThumbnailBackgroundImageHeight"/></xsl:attribute>
								</xsl:if>
								<span style="background-image:url({$ThumbnailURL})">&#xa0;</span>
							</xsl:when>
							<xsl:otherwise>
								<xsl:if test="$IsSidebar != 'true' and $ShowFirstAsBig = 'true' and $IsFirst = 'true'">
									<xsl:attribute name="class">wow animate__animated animate__fadeIn</xsl:attribute>
								</xsl:if>
								<picture>
									<xsl:if test="./ThumbnailURL/@Alternative != ''">
										<source srcset="{./ThumbnailURL/@Alternative}"/>
									</xsl:if>
									<img alt="" src="{$ThumbnailURL}"/>
								</picture>
							</xsl:otherwise>
						</xsl:choose>
					</figure>
				</a>
			</xsl:if>
			<xsl:if test="$ShowTitle != 'false' and $TitleAfterThumbnail = 'true'">
				<h3>
					<a href="{./URL}">
						<xsl:if test="$FixedLines != ''">
							<xsl:attribute name="data-fixed-lines">
								<xsl:value-of select="$FixedLines"/>
							</xsl:attribute>
						</xsl:if>
						<xsl:if test="$Target != ''">
							<xsl:attribute name="target">
								<xsl:value-of select="$Target"/>
							</xsl:attribute>
						</xsl:if>
						<xsl:value-of select="./Title"/>
					</a>
				</h3>
			</xsl:if>
			<xsl:if test="$ShowCategory = 'true' or $ShowPublishedTime = 'true' or ($ShowAuthor = 'true' and ./Author != '') or ($ShowSource = 'true' and ./Source != '' and ./Source != ./Author)">
				<div>
					<xsl:if test="$ShowCategory = 'true'">
						<span>
							<a href="{./Category/@URL}">
								<xsl:if test="$Target != ''">
									<xsl:attribute name="target">
										<xsl:value-of select="$Target"/>
									</xsl:attribute>
								</xsl:if>
								<xsl:value-of select="./Category"/>
							</a>
						</span>
					</xsl:if>
					<xsl:if test="$ShowPublishedTime = 'true'">
						<i class="far fa-clock">&#xa0;</i>
						<xsl:choose>
							<xsl:when test="$IsSidebar = 'true'">
								<xsl:value-of select="./PublishedTime/@DateOnly"/>
							</xsl:when>
							<xsl:otherwise>
								<xsl:value-of select="./PublishedTime/@Short"/>
							</xsl:otherwise>
						</xsl:choose>
					</xsl:if>
					<xsl:if test="$ShowAuthor = 'true' and ./Author != ''">
						<xsl:if test="$ShowPublishedTime = 'true'">
							<label>-</label>
						</xsl:if>
						<i class="far fa-user">&#xa0;</i>
						<xsl:value-of select="./Author"/>
					</xsl:if>
					<xsl:if test="$ShowSource = 'true' and ./Source != '' and ./Source != ./Author">
						<xsl:if test="$ShowPublishedTime = 'true' or ($ShowAuthor = 'true' and ./Author != '')">
							<label>-</label>
						</xsl:if>
						<xsl:value-of select="./Source"/>
					</xsl:if>
				</div>
			</xsl:if>
			<xsl:if test="$ShowSummary = 'true' and ./Summary != ''">
				<span>
					<xsl:if test="$SummaryFixedLines != '' and ($ShowFirstAsBig != 'true' or ($ShowFirstAsBig = 'true' and $IsFirst != 'true'))">
						<xsl:attribute name="data-fixed-lines">
							<xsl:value-of select="$SummaryFixedLines"/>
						</xsl:attribute>
					</xsl:if>
					<xsl:value-of select="./Summary" disable-output-escaping="yes"/>
				</span>
			</xsl:if>
			<xsl:if test="$ShowDetailLabel = 'true' and $DetailLabel != ''">
				<section class="text-right">
					<a href="{./URL}">
						<xsl:if test="$Target != ''">
							<xsl:attribute name="target">
								<xsl:value-of select="$Target"/>
							</xsl:attribute>
						</xsl:if>
						<xsl:value-of select="$DetailLabel" disable-output-escaping="yes"/>
					</a>
				</section>
			</xsl:if>
		</li>
	</xsl:template>

</xsl:stylesheet>
<?xml version="1.0" encoding="utf-8" ?>
<xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform" xmlns:msxsl="urn:schemas-microsoft-com:xslt" xmlns:func="urn:schemas-vieapps-net:xslt">
	<xsl:output method="xml" indent="no"/>

	<!-- the entry -->
	<xsl:template match="/">

		<xsl:choose>

			<!-- mega menu -->
			<xsl:when test="/VIEApps/Options/IsMegaMenu = 'true' or /VIEApps/Options/AsMegaMenu = 'true'">
				<xsl:variable name="Columns">
					<xsl:value-of select="/VIEApps/Options/Columns"/>
				</xsl:variable>
				<div>
					<xsl:attribute name="class">
						<xsl:choose>
							<xsl:when test="$Columns = '4' or $Columns = 'four'">navigator mega four columns</xsl:when>
							<xsl:when test="$Columns = '3' or $Columns = 'three'">navigator mega three columns</xsl:when>
							<xsl:otherwise>navigator mega two columns</xsl:otherwise>
						</xsl:choose>
					</xsl:attribute>
					<ul>
						<xsl:for-each select="/VIEApps/Data/Menu">
							<xsl:call-template name="MenuItem"/>
						</xsl:for-each>
					</ul>
				</div>
			</xsl:when>

			<!-- normal menu -->
			<xsl:otherwise>

				<xsl:variable name="DontUseMMenuJs">
					<xsl:value-of select="/VIEApps/Options/DontUseMMenuJs"/>
				</xsl:variable>

				<!-- MMenuJS -->
				<xsl:if test="$DontUseMMenuJs != 'true'">
					<xsl:variable name="MMenuJsID">mmenujs-<xsl:value-of select="/VIEApps/Meta/Portlet/ID"/></xsl:variable>
					<xsl:variable name="MMenuJsElementID">#<xsl:value-of select="$MMenuJsID"/></xsl:variable>
					<style>
						<xsl:value-of select="$MMenuJsElementID"/>:not(.mm-menu_offcanvas){display:none}
					</style>
					<xsl:call-template name="Menu">
						<xsl:with-param name="UseMMenuJs">true</xsl:with-param>
						<xsl:with-param name="MMenuJsID">
							<xsl:value-of select="$MMenuJsID"/>
						</xsl:with-param>
						<xsl:with-param name="MMenuJsElementID">
							<xsl:value-of select="$MMenuJsElementID"/>
						</xsl:with-param>
					</xsl:call-template>
				</xsl:if>

				<!-- HTML menu -->
				<xsl:call-template name="Menu">
					<xsl:with-param name="UseMMenuJs">false</xsl:with-param>
				</xsl:call-template>

			</xsl:otherwise>

		</xsl:choose>

	</xsl:template>

	<!-- the normal menu -->
	<xsl:template name="Menu">

		<!-- parameters & variables -->
		<xsl:param name="UseMMenuJs"/>
		<xsl:param name="MMenuJsID"/>
		<xsl:param name="MMenuJsElementID"/>
		<xsl:variable name="ShowSearchBox">
			<xsl:choose>
				<xsl:when test="/VIEApps/Options/ShowSearchBox = 'true' or /VIEApps/Options/SearchBox = 'true'">true</xsl:when>
				<xsl:otherwise>false</xsl:otherwise>
			</xsl:choose>
		</xsl:variable>
		<xsl:variable name="ShowPreviousLinks">
			<xsl:value-of select="count(/VIEApps/Options/PreviousLinks/PreviousLink) &gt; 0"/>
		</xsl:variable>
		<xsl:variable name="ShowNextLinks">
			<xsl:value-of select="count(/VIEApps/Options/NextLinks/NextLink) &gt; 0"/>
		</xsl:variable>

		<!-- previous links -->
		<xsl:if test="$ShowPreviousLinks = 'true' and $UseMMenuJs != 'true'">
			<div class="navigator previous links">
				<xsl:variable name="SearchBoxAtEnd">
					<xsl:choose>
						<xsl:when test="/VIEApps/Options/SearchBoxAtEnd = 'true'">true</xsl:when>
						<xsl:otherwise>false</xsl:otherwise>
					</xsl:choose>
				</xsl:variable>
				<xsl:if test="$ShowSearchBox = 'true' and $SearchBoxAtEnd != 'true'">
					<span class="search box start">
						<span>
							<input type="text" maxlength="150" onkeyup="__onsearch(event, this)" placeholder="{/VIEApps/Options/SearchPlaceHolder}" />
							<span class="fa fa-search">&#xa0;</span>
						</span>
					</span>
				</xsl:if>
				<xsl:for-each select="/VIEApps/Options/PreviousLinks/PreviousLink">
					<xsl:call-template name="MenuLink" />
				</xsl:for-each>
				<xsl:if test="$ShowSearchBox = 'true' and $SearchBoxAtEnd = 'true'">
					<span class="search box end">
						<span>
							<input type="text" maxlength="150" onkeyup="__onsearch(event, this)" placeholder="{/VIEApps/Options/SearchPlaceHolder}" />
							<span class="fa fa-search">&#xa0;</span>
						</span>
					</span>
				</xsl:if>
			</div>
		</xsl:if>

		<!-- menu -->
		<nav>

			<xsl:if test="$UseMMenuJs = 'true'">
				<xsl:attribute name="id"><xsl:value-of select="$MMenuJsID"/></xsl:attribute>
			</xsl:if>
			<xsl:attribute name="class">
				<xsl:choose>
					<xsl:when test="$UseMMenuJs = 'true'">navigator mm-menu</xsl:when>
					<xsl:otherwise>
						<xsl:choose>
							<xsl:when test="$ShowPreviousLinks = 'true'">navigator previous links</xsl:when>
							<xsl:when test="$ShowNextLinks = 'true'">navigator next links</xsl:when>
							<xsl:otherwise>navigator</xsl:otherwise>
						</xsl:choose>
					</xsl:otherwise>
				</xsl:choose>
			</xsl:attribute>

			<ul>

				<!-- search box with MMenuJs -->
				<xsl:if test="$ShowSearchBox = 'true' and $UseMMenuJs = 'true'">
					<li class="search box">
						<span>
							<span>
								<input type="text" maxlength="150" onkeyup="__onsearch(event, this)" placeholder="{/VIEApps/Options/SearchPlaceHolder}" />
								<span class="fa fa-search">&#xa0;</span>
							</span>
						</span>
					</li>
				</xsl:if>

				<!-- menu items -->
				<xsl:for-each select="/VIEApps/Data/Menu">
					<xsl:call-template name="MenuItem" />
				</xsl:for-each>

				<!-- search box with pure HTML menu -->
				<xsl:if test="$ShowSearchBox = 'true' and $UseMMenuJs != 'true' and $ShowPreviousLinks != 'true'">
					<li class="search box">
						<span>
							<input type="text" maxlength="150" onkeyup="__onsearch(event, this)" placeholder="{/VIEApps/Options/SearchPlaceHolder}" />
							<span class="fa fa-search">&#xa0;</span>
						</span>
					</li>
				</xsl:if>

			</ul>
		</nav>

		<!-- next links -->
		<xsl:if test="$ShowNextLinks = 'true' and $UseMMenuJs != 'true'">
			<div class="navigator next links">
				<xsl:for-each select="/VIEApps/Options/NextLinks/NextLink">
					<xsl:call-template name="MenuLink" />
				</xsl:for-each>
			</div>
		</xsl:if>

		<!-- toggle button to show MMenuJs -->
		<xsl:if test="$UseMMenuJs = 'true'">
			<a href="{$MMenuJsElementID}" class="navigator toggle">
				<button type="button">
					<i class="fas fa-bars">&#xa0;</i>
				</button>
			</a>
		</xsl:if>

		<!-- scripts of MMenuJs -->
		<xsl:if test="$UseMMenuJs = 'true'">
			<xsl:variable name="MMenuJsExtensions">
				<xsl:choose>
					<xsl:when test="/VIEApps/Options/MMenuJsExtensions != ''">
						<xsl:value-of select="/VIEApps/Options/MMenuJsExtensions"/>
					</xsl:when>
					<xsl:otherwise>
						['theme-dark','position-right','position-front','pagedim-white']
					</xsl:otherwise>
				</xsl:choose>
			</xsl:variable>
			<xsl:variable name="MMenuJsTitle">
				<xsl:choose>
					<xsl:when test="/VIEApps/Options/Title != ''">
						<xsl:value-of select="/VIEApps/Options/Title"/>
					</xsl:when>
					<xsl:otherwise>
						<xsl:value-of select="/VIEApps/Meta/Site/Title"/>
					</xsl:otherwise>
				</xsl:choose>
			</xsl:variable>
			<script>
				$(function(){
					$('<xsl:value-of select="$MMenuJsElementID"/>').mmenu({pageScroll:true,extensions:<xsl:value-of select="$MMenuJsExtensions"/>,navbar:{title:'<xsl:value-of select="$MMenuJsTitle"/>'}});
				});
			</script>
		</xsl:if>

	</xsl:template>

	<!-- menu item -->
	<xsl:template name="MenuItem">
		<xsl:variable name="MenuItemCss">
			<xsl:value-of select="/VIEApps/Options/MenuItemCss"/>
		</xsl:variable>
		<li>
			<xsl:choose>
				<xsl:when test="count(./SubMenu/Menu) &gt; 0 and ./Selected = 'true'">
					<xsl:attribute name="class">has children selected</xsl:attribute>				
				</xsl:when>
				<xsl:when test="count(./SubMenu/Menu)">
					<xsl:attribute name="class">has children</xsl:attribute>				
				</xsl:when>
				<xsl:when test="./Selected = 'true'">
					<xsl:attribute name="class">selected</xsl:attribute>				
				</xsl:when>
			</xsl:choose>
			<a href="{./URL}">
				<xsl:if test="$MenuItemCss != ''">
					<xsl:attribute name="class">
						<xsl:value-of select="$MenuItemCss"/>
					</xsl:attribute>
				</xsl:if>
				<xsl:if test="./Target != ''">
					<xsl:attribute name="target">
						<xsl:value-of select="./Target"/>
					</xsl:attribute>
				</xsl:if>
				<xsl:if test="/VIEApps/Options/ShowDescription = 'true' and ./Description != ''">
					<xsl:attribute name="title">
						<xsl:value-of select="./Description"/>
					</xsl:attribute>
				</xsl:if>
				<xsl:if test="/VIEApps/Options/ShowImage = 'true' and ./Image != ''">
					<figure>
						<img alt="" src="{./Image}"/>
					</figure>
				</xsl:if>
				<xsl:value-of select="Title"/>
			</a>
			<xsl:if test="count(./SubMenu/Menu) &gt; 0">
				<label>&#xa0;</label>
				<ul>
					<xsl:for-each select="./SubMenu/Menu">
						<xsl:call-template name="MenuItem" />
					</xsl:for-each>
				</ul>
			</xsl:if>
		</li>
	</xsl:template>

	<!-- additional link -->	
	<xsl:template name="MenuLink">
		<span>
			<xsl:choose>
				<xsl:when test="./URL = '#' or ./URL = ''">
					<xsl:value-of select="./Text" disable-output-escaping="yes"/>
				</xsl:when>
				<xsl:otherwise>
					<a href="{./URL}">
						<xsl:if test="./Target != ''">
							<xsl:attribute name="target">
								<xsl:value-of select="./Target"/>
							</xsl:attribute>
						</xsl:if>
						<xsl:value-of select="./Text" disable-output-escaping="yes"/>
					</a>
				</xsl:otherwise>
			</xsl:choose>
		</span>
	</xsl:template>

</xsl:stylesheet>
<?xml version="1.0" encoding="utf-8" ?>
<xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform" xmlns:msxsl="urn:schemas-microsoft-com:xslt" xmlns:func="urn:schemas-vieapps-net:xslt">
	<xsl:output method="xml" indent="no"/>

	<xsl:template match="/">

		<!-- variables -->
		<xsl:variable name="ShowTitle">
			<xsl:value-of select="/VIEApps/Options/ShowTitle"/>
		</xsl:variable>
		<xsl:variable name="Title">
			<xsl:value-of select="/VIEApps/Data/Content/Title"/>
		</xsl:variable>
		<xsl:variable name="ShowSubTitle">
			<xsl:value-of select="/VIEApps/Options/ShowSubTitle"/>
		</xsl:variable>
		<xsl:variable name="SubTitle">
			<xsl:value-of select="/VIEApps/Data/Content/SubTitle"/>
		</xsl:variable>
		<xsl:variable name="ShowThumbnail">
			<xsl:value-of select="/VIEApps/Options/ShowThumbnail"/>
		</xsl:variable>
		<xsl:variable name="ThumbnailURL">
			<xsl:value-of select="/VIEApps/Data/Content/Thumbnails/Thumbnail[1]"/>
		</xsl:variable>
		<xsl:variable name="ThumbnailURLAlternative">
			<xsl:value-of select="/VIEApps/Data/Content/Thumbnails/Thumbnail[1]/@Alternative"/>
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
		<xsl:variable name="ShowAuthor">
			<xsl:value-of select="/VIEApps/Options/ShowAuthor"/>
		</xsl:variable>
		<xsl:variable name="Author">
			<xsl:value-of select="/VIEApps/Data/Content/Author"/>
		</xsl:variable>
		<xsl:variable name="AuthorTitle">
			<xsl:value-of select="/VIEApps/Data/Content/AuthorTitle"/>
		</xsl:variable>
		<xsl:variable name="ShowPublishedTime">
			<xsl:value-of select="/VIEApps/Options/ShowPublishedTime"/>
		</xsl:variable>
		<xsl:variable name="PublishedTime">
			<xsl:value-of select="/VIEApps/Data/Content/PublishedTime/@Full"/>
		</xsl:variable>
		<xsl:variable name="ShowSummary">
			<xsl:value-of select="/VIEApps/Options/ShowSummary"/>
		</xsl:variable>
		<xsl:variable name="Summary">
			<xsl:value-of select="/VIEApps/Data/Content/Summary"/>
		</xsl:variable>
		<xsl:variable name="ShowDetails">
			<xsl:value-of select="/VIEApps/Options/ShowDetails"/>
		</xsl:variable>
		<xsl:variable name="Details">
			<xsl:value-of select="/VIEApps/Data/Content/Details"/>
		</xsl:variable>
		<xsl:variable name="ShowSource">
			<xsl:value-of select="/VIEApps/Options/ShowSource"/>
		</xsl:variable>
		<xsl:variable name="Source">
			<xsl:value-of select="/VIEApps/Data/Content/Source"/>
		</xsl:variable>
		<xsl:variable name="SourceURL">
			<xsl:value-of select="/VIEApps/Data/Content/SourceURL"/>
		</xsl:variable>
		<xsl:variable name="SourceLabel">
			<xsl:value-of select="/VIEApps/Options/SourceLabel"/>
		</xsl:variable>
		<xsl:variable name="ShowSocialShares">
			<xsl:value-of select="/VIEApps/Options/ShowSocialShares"/>
		</xsl:variable>
		<xsl:variable name="ShowLastModified">
			<xsl:value-of select="/VIEApps/Options/ShowLastModified"/>
		</xsl:variable>
		<xsl:variable name="LastModifiedLabel">
			<xsl:value-of select="/VIEApps/Options/LastModifiedLabel"/>
		</xsl:variable>
		<xsl:variable name="LastModified">
			<xsl:value-of select="/VIEApps/Data/Content/LastModified/@Short"/>
		</xsl:variable>
		<xsl:variable name="ShowTags">
			<xsl:value-of select="/VIEApps/Options/ShowTags"/>
		</xsl:variable>
		<xsl:variable name="TagsLabel">
			<xsl:value-of select="/VIEApps/Options/TagsLabel"/>
		</xsl:variable>
		<xsl:variable name="ShowRelateds">
			<xsl:value-of select="/VIEApps/Options/ShowRelateds"/>
		</xsl:variable>
		<xsl:variable name="RelatedsLabel">
			<xsl:value-of select="/VIEApps/Options/RelatedsLabel"/>
		</xsl:variable>
		<xsl:variable name="ShowRelatedsCategory">
			<xsl:value-of select="/VIEApps/Options/ShowRelatedsCategory"/>
		</xsl:variable>
		<xsl:variable name="ShowRelatedsPublishedTime">
			<xsl:value-of select="/VIEApps/Options/ShowRelatedsPublishedTime"/>
		</xsl:variable>
		<xsl:variable name="ShowRelatedsAuthor">
			<xsl:value-of select="/VIEApps/Options/ShowRelatedsAuthor"/>
		</xsl:variable>
		<xsl:variable name="ShowRelatedsSummary">
			<xsl:value-of select="/VIEApps/Options/ShowRelatedsSummary"/>
		</xsl:variable>
		<xsl:variable name="RelatedsSummaryFixedLines">
			<xsl:choose>
				<xsl:when test="/VIEApps/Options/RelatedsSummaryFixedLines != ''">
					<xsl:value-of select="/VIEApps/Options/RelatedsSummaryFixedLines"/>
				</xsl:when>
				<xsl:otherwise>1</xsl:otherwise>
			</xsl:choose>
		</xsl:variable>
		<xsl:variable name="MaxRelateds">
			<xsl:choose>
				<xsl:when test="/VIEApps/Options/MaxRelateds != ''">
					<xsl:value-of select="/VIEApps/Options/MaxRelateds"/>
				</xsl:when>
				<xsl:otherwise>0</xsl:otherwise>
			</xsl:choose>
		</xsl:variable>
		<xsl:variable name="AllRelatedsLabel">
			<xsl:value-of select="/VIEApps/Options/AllRelatedsLabel"/>
		</xsl:variable>
		<xsl:variable name="ExternalRelatedsLabel">
			<xsl:value-of select="/VIEApps/Options/ExternalRelatedsLabel"/>
		</xsl:variable>
		<xsl:variable name="ShowExternalRelatedsSummary">
			<xsl:value-of select="/VIEApps/Options/ShowExternalRelatedsSummary"/>
		</xsl:variable>
		<xsl:variable name="ExternalRelatedsSummaryFixedLines">
			<xsl:choose>
				<xsl:when test="/VIEApps/Options/ExternalRelatedsSummaryFixedLines != ''">
					<xsl:value-of select="/VIEApps/Options/ExternalRelatedsSummaryFixedLines"/>
				</xsl:when>
				<xsl:otherwise>2</xsl:otherwise>
			</xsl:choose>
		</xsl:variable>
		<xsl:variable name="MaxExternalRelateds">
			<xsl:choose>
				<xsl:when test="/VIEApps/Options/MaxExternalRelateds != ''">
					<xsl:value-of select="/VIEApps/Options/MaxExternalRelateds"/>
				</xsl:when>
				<xsl:otherwise>0</xsl:otherwise>
			</xsl:choose>
		</xsl:variable>
		<xsl:variable name="AllExternalRelatedsLabel">
			<xsl:value-of select="/VIEApps/Options/AllExternalRelatedsLabel"/>
		</xsl:variable>
		<xsl:variable name="ShowOthers">
			<xsl:value-of select="/VIEApps/Options/ShowOthers"/>
		</xsl:variable>
		<xsl:variable name="OthersLabel">
			<xsl:value-of select="/VIEApps/Options/OthersLabel"/>
		</xsl:variable>
		<xsl:variable name="ShowOthersCategory">
			<xsl:value-of select="/VIEApps/Options/ShowOthersCategory"/>
		</xsl:variable>
		<xsl:variable name="ShowOthersPublishedTime">
			<xsl:value-of select="/VIEApps/Options/ShowOthersPublishedTime"/>
		</xsl:variable>
		<xsl:variable name="ShowOthersAuthor">
			<xsl:value-of select="/VIEApps/Options/ShowOthersAuthor"/>
		</xsl:variable>
		<xsl:variable name="ShowOthersSummary">
			<xsl:value-of select="/VIEApps/Options/ShowOthersSummary"/>
		</xsl:variable>
		<xsl:variable name="OthersSummaryFixedLines">
			<xsl:choose>
				<xsl:when test="/VIEApps/Options/OthersSummaryFixedLines != ''">
					<xsl:value-of select="/VIEApps/Options/OthersSummaryFixedLines"/>
				</xsl:when>
				<xsl:otherwise>1</xsl:otherwise>
			</xsl:choose>			
		</xsl:variable>

		<!-- breadcrumbs -->
		{{breadcrumb-holder}}

		<!-- details of the content -->
		<div class="cms view content">
			<xsl:if test="$ShowSubTitle != 'false' and $SubTitle != ''">
				<label>
					<xsl:value-of select="$SubTitle"/>
				</label>
			</xsl:if>
			<xsl:if test="$ShowTitle != 'false'">
				<h2>
					<xsl:value-of select="$Title"/>
				</h2>
			</xsl:if>
			<xsl:if test="$ShowThumbnail = 'true' and $ThumbnailURL != ''">
				<figure>
					<xsl:choose>
						<xsl:when test="$ShowThumbnailAsBackgroundImage = 'true'">
							<xsl:attribute name="class">animate__animated animate__fadeIn background thumbnail</xsl:attribute>
							<xsl:if test="$ThumbnailBackgroundImageHeight != ''">
								<xsl:attribute name="style">height:<xsl:value-of select="$ThumbnailBackgroundImageHeight"/></xsl:attribute>
							</xsl:if>
							<span style="background-image:url({$ThumbnailURL})">&#xa0;</span>
						</xsl:when>
						<xsl:otherwise>
							<xsl:attribute name="class">animate__animated animate__fadeIn</xsl:attribute>
							<picture>
								<source srcset="{$ThumbnailURLAlternative}"/>
								<img alt="" src="{$ThumbnailURL}"/>
							</picture>
						</xsl:otherwise>
					</xsl:choose>
				</figure>
			</xsl:if>
			<xsl:if test="($ShowAuthor != 'false' and $Author != '') or $ShowPublishedTime != 'false'">
				<div class="meta">
					<xsl:if test="$ShowAuthor != 'false' and $Author != ''">
						<span>
							<label>
								<xsl:value-of select="$Author"/>
							</label>
							<xsl:if test="$AuthorTitle != ''">
								<span>
									<xsl:value-of select="$AuthorTitle"/>
								</span>
							</xsl:if>
						</span>
					</xsl:if>
					<xsl:if test="$ShowPublishedTime != 'false'">
						<section>
							<span>
								<xsl:value-of select="$PublishedTime"/>
								<i style="display:none" class="edit icon float-end fas fa-edit">&#xa0;</i>
							</span>
						</section>
					</xsl:if>
				</div>
			</xsl:if>
			<xsl:if test="$ShowSummary = 'true' and $Summary != ''">
				<span class="summary">
					<xsl:value-of select="$Summary" disable-output-escaping="yes"/>
				</span>
			</xsl:if>
			<xsl:if test="$ShowDetails != 'false' and $Details != ''">
				<section>
					<xsl:value-of select="$Details" disable-output-escaping="yes"/>
				</section>
			</xsl:if>
			<xsl:if test="($ShowSource != 'false' and $Source != '') or $ShowSocialShares != 'false' or $ShowLastModified = 'true'">
				<div class="info">
					<xsl:if test="$ShowSource != 'false' and $Source != ''">
						<div>
							<xsl:if test="$SourceLabel != ''">
								<span>
									<xsl:value-of select="$SourceLabel"/>
								</span>
							</xsl:if>
							<span>
								<xsl:if test="$SourceURL != ''">
									<a href="{$SourceURL}" target="_blank" rel="nofollow">
										<xsl:value-of select="$Source"/>
									</a>
								</xsl:if>
								<xsl:if test="$SourceURL = ''">
									<label>
										<xsl:value-of select="$Source"/>
									</label>
								</xsl:if>
							</span>						
						</div>
					</xsl:if>
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
							<span class="shares linkedin">
								<a href="#">
									<i class="fab fa-linkedin">&#xa0;</i>
									LinkedIn
								</a>
							</span>
							<span class="shares pinterest">
								<a href="#">
									<i class="fab fa-pinterest">&#xa0;</i>
									Pinterest
								</a>
							</span>
							<script>
								<xsl:text disable-output-escaping="yes">
								$(function () {
									$(".shares.facebook").on("click tap", function (event) {
										__vieapps.shares.facebook(event);
									});
									$(".shares.twitter").on("click tap", function (event) {
										__vieapps.shares.twitter(event);
									});
									$(".shares.linkedin").on("click tap", function (event) {
										__vieapps.shares.linkedin(event);
									});
									$(".shares.pinterest").on("click tap", function (event) {
										__vieapps.shares.pinterest(event);
									});
								});
								</xsl:text>
							</script>
						</xsl:if>
						<xsl:if test="$ShowLastModified = 'true'">
							<span>
								<xsl:if test="$LastModifiedLabel != ''">
									<span>
										<xsl:value-of select="$LastModifiedLabel"/>
									</span>
								</xsl:if>
								<label>
									<xsl:value-of select="$LastModified"/>
								</label>
							</span>
						</xsl:if>
					</section>
				</div>
			</xsl:if>
			<xsl:if test="$ShowTags = 'true' and count(/VIEApps/Data/Content/Tags/Tag) &gt; 0">
				<div class="tags">
					<label>
						<i class="fas fa-tags">&#xa0;</i>
						<xsl:value-of select="$TagsLabel"/>
					</label>
					<xsl:for-each select="/VIEApps/Data/Content/Tags/Tag">
						<a href="#">
							<xsl:value-of select="."/>
						</a>
					</xsl:for-each>
					<script>
						<xsl:text disable-output-escaping="yes">
						$(function () {
							$(".tags > a").on("click tap", function (event) {
								event.preventDefault();
								__search(`"${$(this).text()}"`, "tags");
							});
						});
						</xsl:text>
					</script>
				</div>
			</xsl:if>
		</div>

		<!-- pagination -->
		{{pagination-holder}}

		<!-- relateds -->
		<xsl:if test="$ShowRelateds = 'true' and count(/VIEApps/Data/Relateds/Content) &gt; 0">
			<div class="cms view internal relateds">
				<xsl:if test="$RelatedsLabel != ''">
					<div class="cms list title relateds">
						<h3>
							<xsl:value-of select="$RelatedsLabel"/>
						</h3>
					</div>
				</xsl:if>
				<ul class="cms list">
					<xsl:for-each select="/VIEApps/Data/Relateds/Content">
						<li>
							<xsl:attribute name="class">
								<xsl:choose>
									<xsl:when test="$MaxRelateds &gt; 0 and position() &gt; $MaxRelateds">no thumbnail d-none</xsl:when>
									<xsl:otherwise>no thumbnail</xsl:otherwise>
								</xsl:choose>
							</xsl:attribute>
							<h3>
								<a href="{./URL}">
									<xsl:value-of select="./Title"/>
								</a>
							</h3>
							<xsl:if test="$ShowRelatedsCategory = 'true' or $ShowRelatedsPublishedTime = 'true' or $ShowRelatedsAuthor = 'true' or $ShowRelatedsSummary = 'true'">
								<div>
									<xsl:if test="$ShowRelatedsCategory = 'true'">
										<span>
											<a href="{./Category/@URL}">
												<xsl:value-of select="./Category"/>
											</a>
										</span>
									</xsl:if>
									<xsl:if test="$ShowRelatedsPublishedTime = 'true'">
										<xsl:if test="$ShowRelatedsCategory = 'true'">
											<label>&#xa0;|&#xa0;</label>
										</xsl:if>
										<xsl:value-of select="./PublishedTime/@DateOnly"/>
									</xsl:if>
									<xsl:if test="$ShowRelatedsAuthor = 'true' and ./Author != ''">
										<xsl:if test="$ShowRelatedsCategory = 'true' or $ShowRelatedsPublishedTime = 'true'">
											<label>&#xa0;|&#xa0;</label>
										</xsl:if>
										<xsl:value-of select="./Author"/>
									</xsl:if>
									<xsl:if test="$ShowRelatedsSummary = 'true'">
										<span class="summary">
											<xsl:if test="$RelatedsSummaryFixedLines != ''">
												<xsl:attribute name="data-fixed-lines">
													<xsl:value-of select="$RelatedsSummaryFixedLines"/>
												</xsl:attribute>
											</xsl:if>
											<xsl:value-of select="./Summary" disable-output-escaping="yes"/>
										</span>
									</xsl:if>
								</div>
							</xsl:if>
						</li>
					</xsl:for-each>
					<xsl:if test="$AllRelatedsLabel != '' and $MaxRelateds &gt; 0 and count(/VIEApps/Data/Relateds/Content) &gt; $MaxRelateds">
						<li class="all">
							<a href="#">
								<xsl:value-of select="$AllRelatedsLabel" disable-output-escaping="yes"/>
							</a>
							<script>
								<xsl:text disable-output-escaping="yes">
								$(function () {
									$(".cms.view.internal.relateds .cms.list .all a").on("click tap", function (event) {
										event.preventDefault();
										$(".cms.view.internal.relateds .cms.list li.d-none").each(function () {
											$(this).removeClass("d-none");
										});
										$(this).parent().addClass("d-none");
									});
								});
								</xsl:text>
							</script>
						</li>
					</xsl:if>
				</ul>
			</div>
		</xsl:if>

		<xsl:if test="$ShowRelateds = 'true' and count(/VIEApps/Data/ExternalRelateds/ExternalRelated) &gt; 0">
			<div class="cms view external relateds">
				<xsl:if test="$ExternalRelatedsLabel != ''">
					<div class="cms list title external relateds">
						<h3>
							<xsl:value-of select="$ExternalRelatedsLabel"/>
						</h3>
					</div>
				</xsl:if>
				<ul class="cms list">
					<xsl:for-each select="/VIEApps/Data/ExternalRelateds/ExternalRelated">
						<li>
							<xsl:attribute name="class">
								<xsl:choose>
									<xsl:when test="$MaxExternalRelateds &gt; 0 and position() &gt; $MaxExternalRelateds">no thumbnail d-none</xsl:when>
									<xsl:otherwise>no thumbnail</xsl:otherwise>
								</xsl:choose>
							</xsl:attribute>
							<h3>
								<a href="{./URL}" target="_blank" rel="nofollow">
									<xsl:value-of select="./Title"/>
								</a>
							</h3>
							<xsl:if test="$ShowExternalRelatedsSummary = 'true' and ./Summary !=''">
								<div>
									<span class="summary">
										<xsl:if test="$ExternalRelatedsSummaryFixedLines != ''">
											<xsl:attribute name="data-fixed-lines">
												<xsl:value-of select="$ExternalRelatedsSummaryFixedLines"/>
											</xsl:attribute>
										</xsl:if>
										<xsl:value-of select="./Summary" disable-output-escaping="yes"/>
									</span>
								</div>
							</xsl:if>
						</li>
					</xsl:for-each>
					<xsl:if test="$AllExternalRelatedsLabel != '' and $MaxExternalRelateds &gt; 0 and count(/VIEApps/Data/ExternalRelateds/ExternalRelated) &gt; $MaxExternalRelateds">
						<li class="all">
							<a href="#">
								<xsl:value-of select="$AllExternalRelatedsLabel" disable-output-escaping="yes"/>
							</a>
							<script>
								<xsl:text disable-output-escaping="yes">
								$(function () {
									$(".cms.view.external.relateds .cms.list .all a").on("click tap", function (event) {
										event.preventDefault();
										$(".cms.view.external.relateds .cms.list li.d-none").each(function () {
											$(this).removeClass("d-none");
										});
										$(this).parent().addClass("d-none");
									});
								});
								</xsl:text>
							</script>
						</li>
					</xsl:if>
				</ul>
			</div>
		</xsl:if>

		<!-- others -->
		<xsl:if test="$ShowOthers = 'true' and count(/VIEApps/Data/Others/Content) &gt; 0">
			<div class="cms view others">
				<xsl:if test="$OthersLabel != ''">
					<div class="cms list title others">
						<h3>
							<xsl:value-of select="$OthersLabel"/>
						</h3>
					</div>
				</xsl:if>
				<ul class="cms list">
					<xsl:for-each select="/VIEApps/Data/Others/Content">
						<li class="no thumbnail">
							<h3>
								<a href="{./URL}">
									<xsl:value-of select="./Title"/>
								</a>
							</h3>
							<xsl:if test="$ShowOthersCategory = 'true' or $ShowOthersPublishedTime = 'true' or $ShowOthersAuthor = 'true' or $ShowOthersCategory = 'true'">
								<div>
									<xsl:if test="$ShowOthersCategory = 'true'">
										<span>
											<a href="{./Category/@URL}">
												<xsl:value-of select="./Category"/>
											</a>
										</span>
									</xsl:if>
									<xsl:if test="$ShowOthersPublishedTime = 'true'">
										<xsl:if test="$ShowOthersCategory = 'true'">
											<label>&#xa0;|&#xa0;</label>
										</xsl:if>
										<xsl:value-of select="./PublishedTime/@DateOnly"/>
									</xsl:if>
									<xsl:if test="$ShowOthersAuthor = 'true' and ./Author != ''">
										<xsl:if test="$ShowOthersCategory = 'true' or $ShowOthersPublishedTime = 'true'">
											<label>&#xa0;|&#xa0;</label>
										</xsl:if>
										<xsl:value-of select="./Author"/>
									</xsl:if>
									<xsl:if test="$ShowOthersSummary = 'true'">
										<span class="summary">
											<xsl:if test="$OthersSummaryFixedLines != ''">
												<xsl:attribute name="data-fixed-lines">
													<xsl:value-of select="$OthersSummaryFixedLines"/>
												</xsl:attribute>
											</xsl:if>
											<xsl:value-of select="./Summary" disable-output-escaping="yes"/>
										</span>
									</xsl:if>
								</div>
							</xsl:if>
						</li>
					</xsl:for-each>
				</ul>
			</div>
		</xsl:if>

	</xsl:template>
	
</xsl:stylesheet>
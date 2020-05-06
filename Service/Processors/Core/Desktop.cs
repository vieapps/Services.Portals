#region Related components
using System;
using System.Linq;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using System.Dynamic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using net.vieapps.Components.Security;
using net.vieapps.Components.Repository;
using net.vieapps.Components.Utility;
#endregion

namespace net.vieapps.Services.Portals
{
	public static class DesktopProcessor
	{
		internal static ConcurrentDictionary<string, Desktop> Desktops { get; } = new ConcurrentDictionary<string, Desktop>(StringComparer.OrdinalIgnoreCase);

		internal static ConcurrentDictionary<string, Desktop> DesktopsByAlias { get; } = new ConcurrentDictionary<string, Desktop>(StringComparer.OrdinalIgnoreCase);

		internal static HashSet<string> ExtraProperties { get; } = "UISettings,IconURI,CoverURI,MetaTags,Scripts,MainPortletID,SEOSettings".ToHashSet();

		internal static HashSet<string> ExcludedAliases { get; } = (UtilityService.GetAppSetting("Portals:ExcludedAliases", "") + ",Files,Downloads,Thumbnails,ThumbnailBigs,ThumbnailBigPngs").ToLower().ToHashSet();

		public static Desktop CreateDesktopInstance(this ExpandoObject requestBody, string excluded = null, Action<Desktop> onCompleted = null)
			=> requestBody.Copy<Desktop>(excluded?.ToHashSet(), desktop =>
			{
				desktop.Alias = string.IsNullOrWhiteSpace(desktop.Alias) ? desktop.Title.NormalizeAlias() : desktop.Alias.NormalizeAlias();
				desktop.Aliases = string.IsNullOrWhiteSpace(desktop.Aliases) ? null : desktop.Aliases.Replace(",", ";").ToList(";", true, true).Select(alias => alias.NormalizeAlias()).Where(alias => !DesktopProcessor.ExcludedAliases.Contains(alias) && !alias.IsEquals(desktop.Alias)).Join(";");
				desktop.SEOSettings = desktop.SEOSettings ?? new Settings.SEO();
				"TitleMode,DescriptionMode,KeywordsMode".ToList().ForEach(name =>
				{
					var value = requestBody.Get<string>($"SEOSettings.{name}");
					desktop.SEOSettings.SetAttributeValue(name, !string.IsNullOrWhiteSpace(value) && value.TryToEnum(out Settings.SEOMode mode) ? mode as object : null);
				});
				desktop.SEOSettings = desktop.SEOSettings != null && desktop.SEOSettings.SEOInfo == null && desktop.SEOSettings.TitleMode == null && desktop.SEOSettings.DescriptionMode == null && desktop.SEOSettings.KeywordsMode == null ? null : desktop.SEOSettings;
				desktop.TrimAll();
				onCompleted?.Invoke(desktop);
			});

		public static Desktop UpdateDesktopInstance(this Desktop desktop, ExpandoObject requestBody, string excluded = null, Action<Desktop> onCompleted = null)
		{
			desktop.CopyFrom(requestBody, excluded?.ToHashSet());
			desktop.Alias = string.IsNullOrWhiteSpace(desktop.Alias) ? desktop.Title.NormalizeAlias() : desktop.Alias.NormalizeAlias();
			desktop.Aliases = string.IsNullOrWhiteSpace(desktop.Aliases) ? null : desktop.Aliases.Replace(",", ";").ToList(";", true, true).Select(alias => alias.NormalizeAlias()).Where(alias => !DesktopProcessor.ExcludedAliases.Contains(alias) && !alias.IsEquals(desktop.Alias)).Join(";");
			desktop.SEOSettings = desktop.SEOSettings ?? new Settings.SEO();
			"TitleMode,DescriptionMode,KeywordsMode".ToList().ForEach(name =>
			{
				var value = requestBody.Get<string>($"SEOSettings.{name}");
				desktop.SEOSettings.SetAttributeValue(name, !string.IsNullOrWhiteSpace(value) && value.TryToEnum(out Settings.SEOMode mode) ? mode as object : null);
			});
			desktop.SEOSettings = desktop.SEOSettings != null && desktop.SEOSettings.SEOInfo == null && desktop.SEOSettings.TitleMode == null && desktop.SEOSettings.DescriptionMode == null && desktop.SEOSettings.KeywordsMode == null ? null : desktop.SEOSettings;
			desktop.TrimAll();
			onCompleted?.Invoke(desktop);
			return desktop;
		}

		internal static Desktop Set(this Desktop desktop, bool clear = false, bool updateCache = false)
		{
			if (desktop != null)
			{
				if (clear)
				{
					var current = desktop.Remove();
					if (current != null && !current.ParentID.IsEquals(desktop.ParentID))
					{
						if (current.ParentDesktop != null)
							current.ParentDesktop._childrenIDs = null;
						if (desktop.ParentDesktop != null)
							desktop.ParentDesktop._childrenIDs = null;
					}
				}

				DesktopProcessor.Desktops[desktop.ID] = desktop;
				DesktopProcessor.DesktopsByAlias[$"{desktop.SystemID}:{desktop.Alias}"] = desktop;
				Utility.NotRecognizedAliases.Remove($"Desktop:{desktop.SystemID}:{desktop.Alias}");

				if (!string.IsNullOrWhiteSpace(desktop.Aliases))
					desktop.Aliases.ToList(";").ForEach(alias =>
					{
						if (DesktopProcessor.DesktopsByAlias.TryAdd($"{desktop.SystemID}:{alias}", desktop))
							Utility.NotRecognizedAliases.Remove($"Desktop:{desktop.SystemID}:{alias}");
					});

				if (updateCache)
					Utility.Cache.Set(desktop);
			}
			return desktop;
		}

		internal static async Task<Desktop> SetAsync(this Desktop desktop, bool clear = false, bool updateCache = false, CancellationToken cancellationToken = default)
		{
			desktop?.Set(clear);
			await (updateCache && desktop != null ? Utility.Cache.SetAsync(desktop, cancellationToken) : Task.CompletedTask).ConfigureAwait(false);
			return desktop;
		}

		internal static Desktop Remove(this Desktop desktop)
			=> (desktop?.ID ?? "").RemoveDesktop();

		internal static Desktop RemoveDesktop(this string id)
		{
			if (!string.IsNullOrWhiteSpace(id) && DesktopProcessor.Desktops.TryRemove(id, out var desktop) && desktop != null)
			{
				DesktopProcessor.Desktops.Remove(desktop.ID);
				DesktopProcessor.DesktopsByAlias.Remove($"{desktop.SystemID}:{desktop.Alias}");
				if (!string.IsNullOrWhiteSpace(desktop.Aliases))
					desktop.Aliases.ToList(";").ForEach(alias => DesktopProcessor.DesktopsByAlias.Remove($"{desktop.SystemID}:{alias}"));
				return desktop;
			}
			return null;
		}

		public static Desktop GetDesktopByID(this string id, bool force = false, bool fetchRepository = true)
			=> !force && !string.IsNullOrWhiteSpace(id) && DesktopProcessor.Desktops.ContainsKey(id)
				? DesktopProcessor.Desktops[id]
				: fetchRepository && !string.IsNullOrWhiteSpace(id)
					? Desktop.Get<Desktop>(id)?.Set()
					: null;

		public static async Task<Desktop> GetDesktopByIDAsync(this string id, CancellationToken cancellationToken = default, bool force = false)
			=> (id ?? "").GetDesktopByID(force, false) ?? (await Desktop.GetAsync<Desktop>(id, cancellationToken).ConfigureAwait(false))?.Set();

		public static Desktop GetDesktopByAlias(this string systemID, string alias, bool force = false, bool fetchRepository = true)
		{
			if (string.IsNullOrWhiteSpace(systemID) || string.IsNullOrWhiteSpace(alias) || Utility.NotRecognizedAliases.Contains($"Desktop:{systemID}:{alias}"))
				return null;

			var desktop = !force && DesktopProcessor.DesktopsByAlias.ContainsKey($"{systemID}:{alias}")
				? DesktopProcessor.DesktopsByAlias[$"{systemID}:{alias}"]
				: null;

			if (desktop == null && fetchRepository)
			{
				desktop = Desktop.Get<Desktop>(Filters<Desktop>.And(Filters<Desktop>.Equals("SystemID", systemID), Filters<Desktop>.Equals("Alias", alias)), null, null)?.Set();
				if (desktop == null)
					Utility.NotRecognizedAliases.Add($"Desktop:{systemID}:{alias}");
			}

			return desktop;
		}

		public static async Task<Desktop> GetDesktopByAliasAsync(this string systemID, string alias, CancellationToken cancellationToken = default, bool force = false)
		{
			if (string.IsNullOrWhiteSpace(systemID) || string.IsNullOrWhiteSpace(alias) || Utility.NotRecognizedAliases.Contains($"Desktop:{systemID}:{alias}"))
				return null;

			var desktop = systemID.GetDesktopByAlias(alias, force, false) ?? (await Desktop.GetAsync<Desktop>(Filters<Desktop>.And(Filters<Desktop>.Equals("SystemID", systemID), Filters<Desktop>.Equals("Alias", alias)), null, null, cancellationToken).ConfigureAwait(false))?.Set();
			if (desktop == null)
				Utility.NotRecognizedAliases.Add($"Desktop:{systemID}:{alias}");
			return desktop;
		}

		public static IFilterBy<Desktop> GetDesktopsFilter(this string systemID, string parentID)
			=> Filters<Desktop>.And(Filters<Desktop>.Equals("SystemID", systemID), string.IsNullOrWhiteSpace(parentID) ? Filters<Desktop>.IsNull("ParentID") : Filters<Desktop>.Equals("ParentID", parentID));

		public static List<Desktop> FindDesktops(this string systemID, string parentID, bool updateCache = true)
		{
			if (string.IsNullOrWhiteSpace(systemID))
				return new List<Desktop>();
			var filter = systemID.GetDesktopsFilter(parentID);
			var sort = Sorts<Desktop>.Ascending("Title");
			var desktops = Desktop.Find(filter, sort, 0, 1, Extensions.GetCacheKey(filter, sort, 0, 1));
			desktops.ForEach(desktop => desktop.Set(false, updateCache));
			return desktops;
		}

		public static async Task<List<Desktop>> FindDesktopsAsync(this string systemID, string parentID, CancellationToken cancellationToken = default, bool updateCache = true)
		{
			if (string.IsNullOrWhiteSpace(systemID))
				return new List<Desktop>();
			var filter = systemID.GetDesktopsFilter(parentID);
			var sort = Sorts<Desktop>.Ascending("Title");
			var desktops = await Desktop.FindAsync(filter, sort, 0, 1, Extensions.GetCacheKey(filter, sort, 0, 1), cancellationToken).ConfigureAwait(false);
			await desktops.ForEachAsync((desktop, token) => desktop.SetAsync(false, updateCache, token), cancellationToken).ConfigureAwait(false);
			return desktops;
		}

		internal static async Task ProcessInterCommunicateMessageOfDesktopAsync(this CommunicateMessage message, CancellationToken cancellationToken = default)
		{
			if (message.Type.IsEndsWith("#Create"))
				await message.Data.ToExpandoObject().CreateDesktopInstance().SetAsync(true, false, cancellationToken).ConfigureAwait(false);

			else if (message.Type.IsEndsWith("#Update"))
			{
				var desktop = message.Data.Get("ID", "").GetDesktopByID(false, false);
				await (desktop == null ? message.Data.ToExpandoObject().CreateDesktopInstance() : desktop.UpdateDesktopInstance(message.Data.ToExpandoObject())).SetAsync(true, false, cancellationToken).ConfigureAwait(false);
			}

			else if (message.Type.IsEndsWith("#Delete"))
				message.Data.ToExpandoObject().CreateDesktopInstance().Remove();
		}

		static Task ClearRelatedCache(this Desktop desktop, string oldParentID = null, CancellationToken cancellationToken = default)
		{
			var tasks = new List<Task> { Utility.Cache.RemoveAsync(Extensions.GetRelatedCacheKeys(desktop.SystemID.GetDesktopsFilter(null), Sorts<Desktop>.Ascending("Title")), cancellationToken) };
			if (!string.IsNullOrWhiteSpace(desktop.ParentID) && desktop.ParentID.IsValidUUID())
				tasks.Add(Utility.Cache.RemoveAsync(Extensions.GetRelatedCacheKeys(desktop.SystemID.GetDesktopsFilter(desktop.ParentID), Sorts<Desktop>.Ascending("Title")), cancellationToken));
			if (!string.IsNullOrWhiteSpace(oldParentID) && oldParentID.IsValidUUID())
				tasks.Add(Utility.Cache.RemoveAsync(Extensions.GetRelatedCacheKeys(desktop.SystemID.GetDesktopsFilter(oldParentID), Sorts<Desktop>.Ascending("Title")), cancellationToken));
			return Task.WhenAll(tasks);
		}

		internal static async Task<JObject> SearchDesktopsAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, CancellationToken cancellationToken = default)
		{
			// prepare
			var request = requestInfo.GetRequestExpando();

			var query = request.Get<string>("FilterBy.Query");

			var filter = request.Get<ExpandoObject>("FilterBy", null)?.ToFilterBy<Desktop>() ?? Filters<Desktop>.And();
			if (filter is FilterBys<Desktop>)
			{
				if (!string.IsNullOrWhiteSpace(query))
				{
					var index = (filter as FilterBys<Desktop>).Children.FindIndex(exp => (exp as FilterBy<Desktop>).Attribute.IsEquals("ParentID"));
					if (index > -1)
						(filter as FilterBys<Desktop>).Children.RemoveAt(index);
				}
				else if ((filter as FilterBys<Desktop>).Children.FirstOrDefault(exp => (exp as FilterBy<Desktop>).Attribute.IsEquals("ParentID")) == null)
					(filter as FilterBys<Desktop>).Children.Add(Filters<Desktop>.IsNull("ParentID"));
			}

			var sort = string.IsNullOrWhiteSpace(query) ? request.Get<ExpandoObject>("SortBy")?.ToSortBy<Desktop>() ?? Sorts<Desktop>.Ascending("Title") : null;

			var pagination = request.Get<ExpandoObject>("Pagination")?.GetPagination() ?? new Tuple<long, int, int, int>(-1, 0, 20, 1);
			var pageSize = pagination.Item3;
			var pageNumber = pagination.Item4;

			// get organization
			var organizationID = filter is FilterBys<Desktop>
				? ((filter as FilterBys<Desktop>).Children.FirstOrDefault(exp => (exp as FilterBy<Desktop>).Attribute.IsEquals("SystemID")) as FilterBy<Desktop>)?.Value as string
				: null;
			if (string.IsNullOrWhiteSpace(organizationID))
				organizationID = requestInfo.GetParameter("x-system") ?? requestInfo.GetParameter("SystemID");
			var organization = await (organizationID ?? "").GetOrganizationByIDAsync(cancellationToken).ConfigureAwait(false);
			if (organization == null)
				throw new InformationExistedException("The organization is invalid");

			// check permission
			var gotRights = isSystemAdministrator || requestInfo.Session.User.ID.IsEquals(organization.OwnerID) || requestInfo.Session.User.IsViewer(organization.WorkingPrivileges);
			if (!gotRights)
				throw new AccessDeniedException();

			// process cache
			var json = string.IsNullOrWhiteSpace(query) ? await Utility.Cache.GetAsync<string>(Extensions.GetCacheKeyOfObjectsJson(filter, sort, pageSize, pageNumber), cancellationToken).ConfigureAwait(false) : null;
			if (!string.IsNullOrWhiteSpace(json))
				return JObject.Parse(json);

			// prepare pagination
			var totalRecords = pagination.Item1 > -1 ? pagination.Item1 : -1;
			if (totalRecords < 0)
				totalRecords = string.IsNullOrWhiteSpace(query)
					? await Desktop.CountAsync(filter, Extensions.GetCacheKeyOfTotalObjects(filter, sort), cancellationToken).ConfigureAwait(false)
					: await Desktop.CountAsync(query, filter, cancellationToken).ConfigureAwait(false);

			var totalPages = new Tuple<long, int>(totalRecords, pageSize).GetTotalPages();
			if (totalPages > 0 && pageNumber > totalPages)
				pageNumber = totalPages;

			// search
			var objects = totalRecords > 0
				? string.IsNullOrWhiteSpace(query)
					? await Desktop.FindAsync(filter, sort, pageSize, pageNumber, Extensions.GetCacheKey(filter, sort, pageSize, pageNumber), cancellationToken).ConfigureAwait(false)
					: await Desktop.SearchAsync(query, filter, pageSize, pageNumber, cancellationToken).ConfigureAwait(false)
				: new List<Desktop>();

			// build response
			pagination = new Tuple<long, int, int, int>(totalRecords, totalPages, pageSize, pageNumber);
			var response = new JObject()
			{
				{ "FilterBy", filter.ToClientJson(query) },
				{ "SortBy", sort?.ToClientJson() },
				{ "Pagination", pagination.GetPagination() },
				{ "Objects", objects.ToJsonArray() }
			};

			// update cache
			if (string.IsNullOrWhiteSpace(query))
			{
#if DEBUG
				json = response.ToString(Formatting.Indented);
#else
				json = response.ToString(Formatting.Indented);
#endif
				await Utility.Cache.SetAsync(Extensions.GetCacheKeyOfObjectsJson(filter, sort, pageSize, pageNumber), json, Utility.Cache.ExpirationTime / 2).ConfigureAwait(false);
			}

			// response
			return response;
		}

		internal static async Task<JObject> CreateDesktopAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, string nodeID = null, IRTUService rtuService = null, CancellationToken cancellationToken = default)
		{
			// prepare
			var request = requestInfo.GetBodyExpando();
			var organizationID = request.Get<string>("SystemID") ?? requestInfo.GetParameter("x-system") ?? requestInfo.GetParameter("SystemID");
			var organization = await (organizationID ?? "").GetOrganizationByIDAsync(cancellationToken).ConfigureAwait(false);
			if (organization == null)
				throw new InformationInvalidException("The organization is invalid");

			// check permission
			var gotRights = isSystemAdministrator || requestInfo.Session.User.ID.IsEquals(organization.OwnerID) || requestInfo.Session.User.IsModerator(organization.WorkingPrivileges);
			if (!gotRights)
				throw new AccessDeniedException();

			// check alias
			var alias = request.Get<string>("Alias");
			if (!string.IsNullOrWhiteSpace(alias))
			{
				if (DesktopProcessor.ExcludedAliases.Contains(alias.NormalizeAlias()))
					throw new InformationExistedException($"The alias ({alias.NormalizeAlias()}) is used by another purpose");
				var existing = await organization.ID.GetDesktopByAliasAsync(alias.NormalizeAlias(), cancellationToken).ConfigureAwait(false);
				if (existing != null)
					throw new InformationExistedException($"The alias ({alias.NormalizeAlias()}) is used by another desktop");
			}

			// create new
			var desktop = request.CreateDesktopInstance("SystemID,Privileges,OriginalPrivileges,Created,CreatedID,LastModified,LastModifiedID", obj =>
			{
				obj.SystemID = organization.ID;
				obj.ParentID = obj.ParentDesktop != null ? obj.ParentID : null;
				obj.ID = string.IsNullOrWhiteSpace(obj.ID) || !obj.ID.IsValidUUID() ? UtilityService.NewUUID : obj.ID;
				obj.Created = obj.LastModified = DateTime.Now;
				obj.CreatedID = obj.LastModifiedID = requestInfo.Session.User.ID;
				obj.NormalizeExtras();
				obj._childrenIDs = new List<string>();
			});
			await Task.WhenAll(
				Desktop.CreateAsync(desktop, cancellationToken),
				desktop.ClearRelatedCache(null, cancellationToken),
				desktop.SetAsync(false, false, cancellationToken)
			).ConfigureAwait(false);

			var updateMessages = new List<UpdateMessage>();
			var communicateMessages = new List<CommunicateMessage>();
			var objectName = desktop.GetTypeName(true);

			// update parent
			if (desktop.ParentDesktop != null)
			{
				await desktop.ParentDesktop.GetChildrenAsync(cancellationToken, false).ConfigureAwait(false);
				desktop.ParentDesktop._childrenIDs.Add(desktop.ID);
				await desktop.ParentDesktop.SetAsync(false, true, cancellationToken).ConfigureAwait(false);

				var json = desktop.ParentDesktop.ToJson(true, false);
				updateMessages.Add(new UpdateMessage
				{
					Type = $"{requestInfo.ServiceName}#{objectName}#Create",
					Data = json,
					DeviceID = "*"
				});
				communicateMessages.Add(new CommunicateMessage(requestInfo.ServiceName)
				{
					Type = $"{objectName}#Create",
					Data = json,
					ExcludedNodeID = nodeID
				});
			}

			// message to update to all other connected clients
			var response = desktop.ToJson(true, false);
			if (desktop.ParentDesktop == null)
				updateMessages.Add(new UpdateMessage
				{
					Type = $"{requestInfo.ServiceName}#{objectName}#Update",
					DeviceID = "*",
					ExcludedDeviceID = requestInfo.Session.DeviceID,
					Data = response
				});

			// message to update to all service instances (on all other nodes)
			communicateMessages.Add(new CommunicateMessage(requestInfo.ServiceName)
			{
				Type = $"{objectName}#Update",
				Data = response,
				ExcludedNodeID = nodeID
			});

			// send messages and response
			await Task.WhenAll(
				updateMessages.ForEachAsync((message, token) => rtuService == null ? Task.CompletedTask : rtuService.SendUpdateMessageAsync(message, token), cancellationToken, true, false),
				communicateMessages.ForEachAsync((message, token) => rtuService == null ? Task.CompletedTask : rtuService.SendInterCommunicateMessageAsync(message, token), cancellationToken)
			).ConfigureAwait(false);
			return response;
		}

		internal static async Task<JObject> GetDesktopAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, IRTUService rtuService = null, CancellationToken cancellationToken = default)
		{
			// prepare
			var identity = requestInfo.GetObjectIdentity() ?? "";
			var desktop = await (identity.IsValidUUID() ? identity.GetDesktopByIDAsync(cancellationToken) : (requestInfo.GetParameter("x-system") ?? requestInfo.GetParameter("SystemID") ?? "").GetDesktopByAliasAsync(identity, cancellationToken)).ConfigureAwait(false);
			if (desktop == null)
				throw new InformationNotFoundException();
			else if (desktop.Organization == null)
				throw new InformationInvalidException("The organization is invalid");

			// check permission
			var gotRights = isSystemAdministrator || requestInfo.Session.User.ID.IsEquals(desktop.Organization.OwnerID) || requestInfo.Session.User.IsViewer(desktop.Organization.WorkingPrivileges);
			if (!gotRights)
				throw new AccessDeniedException();

			if (!identity.IsValidUUID())
				return new JObject
				{
					{ "ID", desktop.ID },
					{ "Title", desktop.Title },
					{ "Alias", desktop.Alias }
				};

			// prepare the response
			if (desktop._childrenIDs == null)
			{
				await desktop.GetChildrenAsync(cancellationToken, false).ConfigureAwait(false);
				await desktop.SetAsync(false, true, cancellationToken).ConfigureAwait(false);
			}

			// send update message and response
			var response = desktop.ToJson(true, false);
			await (rtuService == null ? Task.CompletedTask : rtuService.SendUpdateMessageAsync(new UpdateMessage
			{
				Type = $"{requestInfo.ServiceName}#{desktop.GetTypeName(true)}#Update",
				Data = response,
				DeviceID = "*",
				ExcludedDeviceID = requestInfo.Session.DeviceID
			}, cancellationToken)).ConfigureAwait(false);
			return response;
		}

		internal static async Task<JObject> UpdateDesktopAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, string nodeID = null, IRTUService rtuService = null, CancellationToken cancellationToken = default)
		{
			// prepare
			var desktop = await (requestInfo.GetObjectIdentity() ?? "").GetDesktopByIDAsync(cancellationToken).ConfigureAwait(false);
			if (desktop == null)
				throw new InformationNotFoundException();
			else if (desktop.Organization == null)
				throw new InformationInvalidException("The organization is invalid");

			// check permission
			var gotRights = isSystemAdministrator || requestInfo.Session.User.ID.IsEquals(desktop.Organization.OwnerID) || requestInfo.Session.User.IsModerator(desktop.Organization.WorkingPrivileges);
			if (!gotRights)
				throw new AccessDeniedException();

			// update
			var request = requestInfo.GetBodyExpando();
			var oldParentID = desktop.ParentID;

			var oldAlias = desktop.Alias;
			var alias = request.Get<string>("Alias");
			if (!string.IsNullOrWhiteSpace(alias))
			{
				if (DesktopProcessor.ExcludedAliases.Contains(alias.NormalizeAlias()))
					throw new InformationExistedException($"The alias ({alias.NormalizeAlias()}) is used by another purpose");
				var existing = await desktop.SystemID.GetDesktopByAliasAsync(alias.NormalizeAlias(), cancellationToken).ConfigureAwait(false);
				if (existing != null && !existing.ID.Equals(desktop.ID))
					throw new InformationExistedException($"The alias ({alias.NormalizeAlias()}) is used by another desktop");
			}

			desktop.UpdateDesktopInstance(request, "ID,SystemID,Privileges,OriginalPrivileges,Created,CreatedID,LastModified,LastModifiedID", async obj =>
			{
				obj.LastModified = DateTime.Now;
				obj.LastModifiedID = requestInfo.Session.User.ID;
				obj.NormalizeExtras();
				await obj.GetChildrenAsync(cancellationToken, false).ConfigureAwait(false);
			});
			await Task.WhenAll(
				Desktop.UpdateAsync(desktop, requestInfo.Session.User.ID, cancellationToken),
				desktop.SetAsync(false, false, cancellationToken)
			).ConfigureAwait(false);
			await desktop.ClearRelatedCache(oldParentID, cancellationToken).ConfigureAwait(false);

			var updateMessages = new List<UpdateMessage>();
			var communicateMessages = new List<CommunicateMessage>();
			var objectName = desktop.GetTypeName(true);

			// update parent
			if (desktop.ParentDesktop != null && !desktop.ParentID.IsEquals(oldParentID))
			{
				await desktop.ParentDesktop.GetChildrenAsync(cancellationToken, false).ConfigureAwait(false);
				desktop.ParentDesktop._childrenIDs.Add(desktop.ID);
				await desktop.ParentDesktop.SetAsync(false, true, cancellationToken).ConfigureAwait(false);

				var json = desktop.ParentDesktop.ToJson(true, false);
				updateMessages.Add(new UpdateMessage
				{
					Type = $"{requestInfo.ServiceName}#{objectName}#Update",
					Data = json,
					DeviceID = "*"
				});
				communicateMessages.Add(new CommunicateMessage(requestInfo.ServiceName)
				{
					Type = $"{objectName}#Update",
					Data = json,
					ExcludedNodeID = nodeID
				});
			}

			// update old parent
			if (!string.IsNullOrWhiteSpace(oldParentID) && !oldParentID.IsEquals(desktop.ParentID))
			{
				var parentDesktop = await oldParentID.GetDesktopByIDAsync(cancellationToken).ConfigureAwait(false);
				if (parentDesktop != null)
				{
					await parentDesktop.GetChildrenAsync(cancellationToken, false).ConfigureAwait(false);
					parentDesktop._childrenIDs.Remove(desktop.ID);
					await parentDesktop.SetAsync(false, true, cancellationToken).ConfigureAwait(false);

					var json = parentDesktop.ToJson(true, false);
					updateMessages.Add(new UpdateMessage
					{
						Type = $"{requestInfo.ServiceName}#{objectName}#Update",
						Data = json,
						DeviceID = "*"
					});
					communicateMessages.Add(new CommunicateMessage(requestInfo.ServiceName)
					{
						Type = $"{objectName}#Update",
						Data = json,
						ExcludedNodeID = nodeID
					});
				}
			}

			// message to update to all other connected clients
			var response = desktop.ToJson(true, false);
			if (desktop.ParentDesktop == null)
				updateMessages.Add(new UpdateMessage
				{
					Type = $"{requestInfo.ServiceName}#{objectName}#Update",
					Data = response,
					DeviceID = "*",
					ExcludedDeviceID = requestInfo.Session.DeviceID
				});

			// message to update to all service instances (on all other nodes)
			communicateMessages.Add(new CommunicateMessage(requestInfo.ServiceName)
			{
				Type = $"{objectName}#Update",
				Data = response,
				ExcludedNodeID = nodeID
			});

			// send messages and response
			await Task.WhenAll(
				updateMessages.ForEachAsync((message, token) => rtuService == null ? Task.CompletedTask : rtuService.SendUpdateMessageAsync(message, token), cancellationToken, true, false),
				communicateMessages.ForEachAsync((message, token) => rtuService == null ? Task.CompletedTask : rtuService.SendInterCommunicateMessageAsync(message, token), cancellationToken)
			).ConfigureAwait(false);
			return response;
		}

		internal static async Task<JObject> DeleteDesktopAsync(this RequestInfo requestInfo, bool isSystemAdministrator = false, string nodeID = null, IRTUService rtuService = null, CancellationToken cancellationToken = default)
		{
			// prepare
			var desktop = await (requestInfo.GetObjectIdentity() ?? "").GetDesktopByIDAsync(cancellationToken).ConfigureAwait(false);
			if (desktop == null)
				throw new InformationNotFoundException();
			else if (desktop.Organization == null)
				throw new InformationInvalidException("The organization is invalid");

			// check permission
			var gotRights = isSystemAdministrator || requestInfo.Session.User.ID.IsEquals(desktop.Organization.OwnerID) || requestInfo.Session.User.IsModerator(desktop.Organization.WorkingPrivileges);
			if (!gotRights)
				throw new AccessDeniedException();

			// delete
			var updateMessages = new List<UpdateMessage>();
			var communicateMessages = new List<CommunicateMessage>();
			var objectName = desktop.GetTypeName(true);
			var updateChildren = requestInfo.Header.TryGetValue("x-children", out var childrenMode) && "set-null".IsEquals(childrenMode);

			await (await desktop.GetChildrenAsync(cancellationToken, false).ConfigureAwait(false)).ForEachAsync(async (child, token) =>
			{
				// update children to root
				if (updateChildren)
				{
					child.ParentID = null;
					child.LastModified = DateTime.Now;
					child.LastModifiedID = requestInfo.Session.User.ID;

					await Task.WhenAll(
						Role.UpdateAsync(child, requestInfo.Session.User.ID, token),
						child.SetAsync(false, false, token)
					).ConfigureAwait(false);

					var json = child.ToJson(true, false);
					updateMessages.Add(new UpdateMessage
					{
						Type = $"{requestInfo.ServiceName}#{objectName}#Delete",
						Data = json,
						DeviceID = "*"
					});
					communicateMessages.Add(new CommunicateMessage(requestInfo.ServiceName)
					{
						Type = $"{objectName}#Delete",
						Data = json,
						ExcludedNodeID = nodeID
					});
				}

				// delete children
				else
				{
					var messages = await child.DeleteChildrenAsync(requestInfo.Session.User.ID, requestInfo.ServiceName, nodeID, token).ConfigureAwait(false);
					updateMessages = updateMessages.Concat(messages.Item1).ToList();
					communicateMessages = communicateMessages.Concat(messages.Item2).ToList();
				}
			}, cancellationToken, true, false).ConfigureAwait(false);

			await Desktop.DeleteAsync<Desktop>(desktop.ID, requestInfo.Session.User.ID, cancellationToken).ConfigureAwait(false);
			desktop.Remove();
			await desktop.ClearRelatedCache(null, cancellationToken).ConfigureAwait(false);

			// message to update to all other connected clients
			var response = desktop.ToJson();
			updateMessages.Add(new UpdateMessage
			{
				Type = $"{requestInfo.ServiceName}#{objectName}#Delete",
				Data = response,
				DeviceID = "*",
				ExcludedDeviceID = requestInfo.Session.DeviceID
			});

			// message to update to all service instances (on all other nodes)
			communicateMessages.Add(new CommunicateMessage(requestInfo.ServiceName)
			{
				Type = $"{objectName}#Delete",
				Data = response,
				ExcludedNodeID = nodeID
			});

			// send messages and response
			await Task.WhenAll(
				updateMessages.ForEachAsync((message, token) => rtuService == null ? Task.CompletedTask : rtuService.SendUpdateMessageAsync(message, token), cancellationToken, true, false),
				communicateMessages.ForEachAsync((message, token) => rtuService == null ? Task.CompletedTask : rtuService.SendInterCommunicateMessageAsync(message, token), cancellationToken)
			).ConfigureAwait(false);
			return response;
		}

		static async Task<Tuple<List<UpdateMessage>, List<CommunicateMessage>>> DeleteChildrenAsync(this Desktop desktop, string userID, string serviceName = null, string nodeID = null, CancellationToken cancellationToken = default)
		{
			var updateMessages = new List<UpdateMessage>();
			var communicateMessages = new List<CommunicateMessage>();
			var objectName = desktop.GetTypeName(true);

			var children = await desktop.GetChildrenAsync(cancellationToken, false).ConfigureAwait(false);
			await children.ForEachAsync(async (child, token) =>
			{
				var messages = await child.DeleteChildrenAsync(userID, serviceName, nodeID, token).ConfigureAwait(false);
				updateMessages = updateMessages.Concat(messages.Item1).ToList();
				communicateMessages = communicateMessages.Concat(messages.Item2).ToList();
			}, cancellationToken, true, false).ConfigureAwait(false);

			await Desktop.DeleteAsync<Desktop>(desktop.ID, userID, cancellationToken).ConfigureAwait(false);
			desktop.Remove();

			var json = desktop.ToJson();
			updateMessages.Add(new UpdateMessage
			{
				Type = $"{serviceName}#{objectName}#Delete",
				Data = json,
				DeviceID = "*"
			});
			communicateMessages.Add(new CommunicateMessage(serviceName)
			{
				Type = $"{objectName}#Delete",
				Data = json,
				ExcludedNodeID = nodeID
			});
			return new Tuple<List<UpdateMessage>, List<CommunicateMessage>>(updateMessages, communicateMessages);
		}
	}
}
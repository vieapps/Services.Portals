#region Related components
using System;
using System.Dynamic;
using System.Diagnostics;
using System.Xml.Serialization;
using MsgPack.Serialization;
using MongoDB.Bson.Serialization.Attributes;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Converters;
using net.vieapps.Components.Utility;
using net.vieapps.Components.Security;
using net.vieapps.Components.Repository;
#endregion

namespace net.vieapps.Services.Portals
{
	[BsonIgnoreExtraElements, DebuggerDisplay("ID = {ID}, Title = {Title}")]
	[Entity(CollectionName = "Tasks", TableName = "T_Portals_Tasks", CacheClass = typeof(Utility), CacheName = "Cache")]
	public sealed class SchedulingTask : Repository<SchedulingTask>, IPortalObject
	{
		public SchedulingTask() : this(0) { }

		public SchedulingTask(int recurringUnit, RecurringType recurringType = RecurringType.Minutes, DateTime? time = null) : base()
		{
			this.RecurringUnit = recurringUnit;
			this.RecurringType = recurringType;
			this.UpdateTime(time);
		}

		[Property(MaxLength = 250, NotNull = true, NotEmpty = true)]
		[Sortable(IndexName = "Title"), Searchable]
		[FormControl(Label = "{{portals.tasks.controls.[name].label}}", PlaceHolder = "{{portals.tasks.controls.[name].placeholder}}", Description = "{{portals.tasks.controls.[name].description}}")]
		public override string Title { get; set; }

		[Searchable]
		[FormControl(ControlType = "TextArea", Label = "{{portals.tasks.controls.[name].label}}", PlaceHolder = "{{portals.tasks.controls.[name].placeholder}}", Description = "{{portals.tasks.controls.[name].description}}")]
		public string Description { get; set; }

		[JsonConverter(typeof(StringEnumConverter)), BsonRepresentation(MongoDB.Bson.BsonType.String)]
		[Sortable(IndexName = "Management")]
		[FormControl(Label = "{{portals.tasks.controls.[name].label}}", PlaceHolder = "{{portals.tasks.controls.[name].placeholder}}", Description = "{{portals.tasks.controls.[name].description}}")]
		public Status Status { get; set; } = Status.Awaiting;

		[JsonConverter(typeof(StringEnumConverter)), BsonRepresentation(MongoDB.Bson.BsonType.String)]
		[Sortable(IndexName = "Management")]
		[FormControl(Label = "{{portals.tasks.controls.[name].label}}", PlaceHolder = "{{portals.tasks.controls.[name].placeholder}}", Description = "{{portals.tasks.controls.[name].description}}")]
		public SchedulingType SchedulingType { get; set; } = SchedulingType.Update;

		[JsonConverter(typeof(StringEnumConverter)), BsonRepresentation(MongoDB.Bson.BsonType.String)]
		[FormControl(Label = "{{portals.tasks.controls.[name].label}}", PlaceHolder = "{{portals.tasks.controls.[name].placeholder}}", Description = "{{portals.tasks.controls.[name].description}}")]
		public RecurringType RecurringType { get; set; } = RecurringType.Minutes;

		[FormControl(MinValue = "0", Label = "{{portals.tasks.controls.[name].label}}", PlaceHolder = "{{portals.tasks.controls.[name].placeholder}}", Description = "{{portals.tasks.controls.[name].description}}")]
		public int RecurringUnit { get; set; } = 0;

		[Sortable(IndexName = "Management")]
		[FormControl(DatePickerWithTimes = true, Label = "{{portals.tasks.controls.[name].label}}", PlaceHolder = "{{portals.tasks.controls.[name].placeholder}}", Description = "{{portals.tasks.controls.[name].description}}")]
		public DateTime Time { get; set; } = DateTime.Now;

		[Property(MaxLength = 250)]
		[FormControl(Label = "{{portals.tasks.controls.[name].label}}", PlaceHolder = "{{portals.tasks.controls.[name].placeholder}}", Description = "{{portals.tasks.controls.[name].description}}")]
		public string EntityInfo { get; set; }

		[Property(MaxLength = 32)]
		[Sortable(IndexName = "Management")]
		[FormControl(Label = "{{portals.tasks.controls.[name].label}}", PlaceHolder = "{{portals.tasks.controls.[name].placeholder}}", Description = "{{portals.tasks.controls.[name].description}}")]
		public string ObjectID { get; set; }

		[Property(MaxLength = 32)]
		[FormControl(Label = "{{portals.tasks.controls.[name].label}}", PlaceHolder = "{{portals.tasks.controls.[name].placeholder}}", Description = "{{portals.tasks.controls.[name].description}}")]
		public string UserID { get; set; }

		[Property(IsCLOB = true)]
		[FormControl(ControlType = "TextArea", Label = "{{portals.tasks.controls.[name].label}}", PlaceHolder = "{{portals.tasks.controls.[name].placeholder}}", Description = "{{portals.tasks.controls.[name].description}}")]
		public string Data { get; set; }

		[Sortable(IndexName = "Audits")]
		[FormControl(Hidden = true)]
		public DateTime Created { get; set; } = DateTime.Now;

		[Sortable(IndexName = "Audits")]
		[FormControl(Hidden = true)]
		public string CreatedID { get; set; }

		[Sortable(IndexName = "Audits")]
		[FormControl(Hidden = true)]
		public DateTime LastModified { get; set; } = DateTime.Now;

		[Sortable(IndexName = "Audits")]
		[FormControl(Hidden = true)]
		public string LastModifiedID { get; set; }

		[Property(MaxLength = 32, NotNull = true, NotEmpty = true)]
		[Sortable(IndexName = "Management")]
		[FormControl(Hidden = true)]
		public override string SystemID { get; set; }

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public override string RepositoryID { get; set; }

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public override string RepositoryEntityID { get; set; }

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore]
		public string OrganizationID => this.SystemID;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore, MessagePackIgnore]
		public Organization Organization => (this.OrganizationID ?? "").GetOrganizationByID();

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore, MessagePackIgnore]
		public override RepositoryBase Parent => this.Organization;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore, MessagePackIgnore]
		IPortalObject IPortalObject.Parent => this.Organization;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore, MessagePackIgnore]
		public override Privileges OriginalPrivileges { get; set; }

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore, MessagePackIgnore]
		public override Privileges WorkingPrivileges => this.Organization?.WorkingPrivileges;

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore, MessagePackIgnore]
		public JToken DataAsJson => this.Data?.ToJson() ?? new JObject();

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore, MessagePackIgnore]
		public ExpandoObject DataAsExpandoObject => this.Data?.ToExpandoObject() ?? new ExpandoObject();

		[Ignore, JsonIgnore, BsonIgnore, XmlIgnore, MessagePackIgnore]
		public bool Persistance { get; set; } = true;

		public override JObject ToJson(bool addTypeOfExtendedProperties = false, Action<JObject> onCompleted = null)
			=> base.ToJson(addTypeOfExtendedProperties, json =>
			{
				json["Persistance"] = this.Persistance;
				json["Time"] = this.Time.ToIsoString();
				onCompleted?.Invoke(json);
			});

		internal void UpdateTime(DateTime? time = null)
			=> this.Time = time != null && time.Value > DateTime.Now
				? time.Value
				: this.RecurringUnit < 1
					? this.Time
					: this.RecurringType.Equals(RecurringType.Years)
						? DateTime.Now.AddYears(this.RecurringUnit)
						: this.RecurringType.Equals(RecurringType.Months)
							? DateTime.Now.AddMonths(this.RecurringUnit)
							: this.RecurringType.Equals(RecurringType.Days)
								? DateTime.Now.AddDays(this.RecurringUnit)
								: this.RecurringType.Equals(RecurringType.Hours)
									? DateTime.Now.AddHours(this.RecurringUnit)
									: this.RecurringType.Equals(RecurringType.Minutes)
										? DateTime.Now.AddMinutes(this.RecurringUnit)
										: DateTime.Now.AddSeconds(this.RecurringUnit);
	}

	public enum SchedulingType
	{
		Update,
		Refresh,
		SendNotification,
		RunCrawler
	}

	public enum RecurringType
	{
		Seconds,
		Minutes,
		Hours,
		Days,
		Months,
		Years
	}

	public enum Status
	{
		Awaiting,
		Acquired,
		Running,
		Completed
	}
}
namespace Backend.Veteriner.Domain.Reminders;

public enum ReminderType
{
    Appointment = 0,
    Vaccination = 1
}

public enum ReminderSourceEntityType
{
    Appointment = 0,
    Vaccination = 1
}

public enum ReminderDispatchStatus
{
    Pending = 0,
    Enqueued = 1,
    Sent = 2,
    Failed = 3,
    Skipped = 4
}

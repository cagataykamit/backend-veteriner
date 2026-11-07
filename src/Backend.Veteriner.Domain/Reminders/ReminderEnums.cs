namespace Backend.Veteriner.Domain.Reminders;

public enum ReminderType
{
    Appointment = 0,
    Vaccination = 1,
    FollowUp = 2,
    Debt = 3,
    NoReturn = 4
}

public enum ReminderSourceEntityType
{
    Appointment = 0,
    Vaccination = 1,
    Treatment = 2,
    Charge = 3,
    Pet = 4
}

public enum ReminderDispatchStatus
{
    Pending = 0,
    Enqueued = 1,
    Sent = 2,
    Failed = 3,
    Skipped = 4
}

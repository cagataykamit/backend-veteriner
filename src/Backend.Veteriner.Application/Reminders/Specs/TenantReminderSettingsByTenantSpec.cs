using Ardalis.Specification;
using Backend.Veteriner.Domain.Reminders;

namespace Backend.Veteriner.Application.Reminders.Specs;

public sealed class TenantReminderSettingsByTenantSpec : Specification<TenantReminderSettings>
{
    public TenantReminderSettingsByTenantSpec(Guid tenantId)
    {
        Query.AsNoTracking();
        Query.Where(x => x.TenantId == tenantId);
    }
}

using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Reminders.Contracts.Dtos;
using Backend.Veteriner.Application.Reminders.Specs;
using Backend.Veteriner.Domain.Reminders;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Reminders.Queries.GetSettings;

public sealed class GetReminderSettingsQueryHandler
    : IRequestHandler<GetReminderSettingsQuery, Result<ReminderSettingsDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IReadRepository<TenantReminderSettings> _settingsRead;

    public GetReminderSettingsQueryHandler(
        ITenantContext tenantContext,
        IReadRepository<TenantReminderSettings> settingsRead)
    {
        _tenantContext = tenantContext;
        _settingsRead = settingsRead;
    }

    public async Task<Result<ReminderSettingsDto>> Handle(GetReminderSettingsQuery request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<ReminderSettingsDto>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        var row = await _settingsRead.FirstOrDefaultAsync(new TenantReminderSettingsByTenantSpec(tenantId), ct);
        if (row is null)
        {
            var defaults = TenantReminderSettings.CreateDefault(tenantId);
            return Result<ReminderSettingsDto>.Success(new ReminderSettingsDto(
                defaults.AppointmentRemindersEnabled,
                defaults.AppointmentReminderHoursBefore,
                defaults.VaccinationRemindersEnabled,
                defaults.VaccinationReminderDaysBefore,
                defaults.EmailChannelEnabled,
                null));
        }

        return Result<ReminderSettingsDto>.Success(new ReminderSettingsDto(
            row.AppointmentRemindersEnabled,
            row.AppointmentReminderHoursBefore,
            row.VaccinationRemindersEnabled,
            row.VaccinationReminderDaysBefore,
            row.EmailChannelEnabled,
            row.UpdatedAtUtc));
    }
}

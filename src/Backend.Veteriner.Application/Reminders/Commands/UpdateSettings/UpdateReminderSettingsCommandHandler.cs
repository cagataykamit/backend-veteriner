using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Reminders.Contracts.Dtos;
using Backend.Veteriner.Application.Reminders.Specs;
using Backend.Veteriner.Domain.Reminders;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Reminders.Commands.UpdateSettings;

public sealed class UpdateReminderSettingsCommandHandler
    : IRequestHandler<UpdateReminderSettingsCommand, Result<ReminderSettingsDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IReadRepository<TenantReminderSettings> _settingsRead;
    private readonly IRepository<TenantReminderSettings> _settingsWrite;

    public UpdateReminderSettingsCommandHandler(
        ITenantContext tenantContext,
        IReadRepository<TenantReminderSettings> settingsRead,
        IRepository<TenantReminderSettings> settingsWrite)
    {
        _tenantContext = tenantContext;
        _settingsRead = settingsRead;
        _settingsWrite = settingsWrite;
    }

    public async Task<Result<ReminderSettingsDto>> Handle(UpdateReminderSettingsCommand request, CancellationToken ct)
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
            row = TenantReminderSettings.CreateDefault(tenantId);
            var initial = row.Update(
                request.AppointmentRemindersEnabled,
                request.AppointmentReminderHoursBefore,
                request.VaccinationRemindersEnabled,
                request.VaccinationReminderDaysBefore,
                request.EmailChannelEnabled);
            if (!initial.IsSuccess)
                return Result<ReminderSettingsDto>.Failure(initial.Error);
            await _settingsWrite.AddAsync(row, ct);
        }
        else
        {
            var update = row.Update(
                request.AppointmentRemindersEnabled,
                request.AppointmentReminderHoursBefore,
                request.VaccinationRemindersEnabled,
                request.VaccinationReminderDaysBefore,
                request.EmailChannelEnabled);
            if (!update.IsSuccess)
                return Result<ReminderSettingsDto>.Failure(update.Error);
            await _settingsWrite.UpdateAsync(row, ct);
        }

        await _settingsWrite.SaveChangesAsync(ct);

        return Result<ReminderSettingsDto>.Success(new ReminderSettingsDto(
            row.AppointmentRemindersEnabled,
            row.AppointmentReminderHoursBefore,
            row.VaccinationRemindersEnabled,
            row.VaccinationReminderDaysBefore,
            row.EmailChannelEnabled,
            row.UpdatedAtUtc));
    }
}

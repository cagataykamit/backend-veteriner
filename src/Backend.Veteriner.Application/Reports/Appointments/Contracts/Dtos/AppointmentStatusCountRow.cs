using Backend.Veteriner.Domain.Appointments;

namespace Backend.Veteriner.Application.Reports.Appointments.Contracts.Dtos;

/// <summary>Rapor durum dağılımı için tek aggregate satırı (Scheduled/Completed/Cancelled).</summary>
public sealed record AppointmentStatusCountRow(AppointmentStatus Status, int Count);

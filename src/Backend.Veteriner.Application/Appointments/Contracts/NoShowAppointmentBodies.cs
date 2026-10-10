namespace Backend.Veteriner.Application.Appointments.Contracts;

/// <summary>POST /appointments/{id}/no-show gövdesi; gerekçe opsiyoneldir.</summary>
public sealed record MarkAppointmentNoShowBody(string? Reason = null);

/// <summary>POST /appointments/{id}/no-show/revert gövdesi; gerekçe zorunludur (5-500 karakter).</summary>
public sealed record RevertAppointmentNoShowBody(string? Reason = null);

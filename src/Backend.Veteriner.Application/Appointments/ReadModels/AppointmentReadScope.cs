namespace Backend.Veteriner.Application.Appointments.ReadModels;

/// <summary>
/// Doğrulanmış tenant/klinik kapsamı; query read-model reader'a handler tarafından aktarılır.
/// </summary>
public sealed record AppointmentReadScope(Guid TenantId, Guid ClinicId);

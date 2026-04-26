namespace Backend.Veteriner.Application.Dashboard.Contracts.Dtos;

public sealed record DashboardCapabilitiesDto(
    bool CanViewFinance,
    bool CanViewOperationalAlerts,
    bool IsOwner,
    bool IsAdmin,
    bool IsStaff,
    Guid? SelectedClinicId,
    bool HasClinicContext,
    bool IsTenantReadOnly);

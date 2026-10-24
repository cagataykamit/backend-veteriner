namespace Backend.Veteriner.Application.Tenants.Contracts.Dtos;

/// <summary>
/// Örnek Veteriner Kliniği yüklemesinin özeti (ADR-002): kliniğe yazılan kayıt sayıları. Frontend onboarding
/// sonunda "ne yüklendi" kartını bu alanlardan kurar.
/// </summary>
public sealed record ExampleClinicLoadSummaryDto(
    Guid TenantId,
    Guid ClinicId,
    int Clients,
    int Pets,
    int Appointments,
    int Examinations,
    int Vaccinations,
    int Treatments,
    int LabResults,
    int Charges,
    int Payments,
    int Products,
    int StockMovements);

namespace Backend.Veteriner.Application.Tenants.Contracts.Dtos;

/// <summary>
/// Kiracıda örnek klinik verisi var mı ve temizlik neyi silecek (EXAMPLE_CLINIC_API_CONTRACT.md §10.2). Panel onay
/// diyaloğunu bu sayılardan kurar.
/// </summary>
public sealed record ExampleClinicStatusDto(bool HasExampleData, ExampleClinicDataCountsDto Counts);

namespace Backend.Veteriner.Application.VaccineDefinitions.Contracts.Dtos;

public sealed record VaccineDefinitionDto(
    Guid Id,
    Guid? TenantId,
    Guid? SpeciesId,
    string Name,
    string Code,
    string? Description,
    int? DefaultNextDueDays,
    bool IsCore,
    bool IsActive);

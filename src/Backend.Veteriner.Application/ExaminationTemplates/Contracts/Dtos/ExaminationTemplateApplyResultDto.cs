using Backend.Veteriner.Application.Administrations.Contracts.Dtos;

namespace Backend.Veteriner.Application.ExaminationTemplates.Contracts.Dtos;

/// <summary>
/// Şablon uygulama sonucu. <see cref="RowVersion"/> muayenenin yeni sürümüdür (sonraki PUT bununla yapılır);
/// <see cref="UpdatedFields"/> metni değişen muayene alanları (<c>visitReason</c>, <c>findings</c>, <c>plan</c>).
/// <see cref="PackageChargeSuggestionId"/>: fiyatlı paketin tek "ücret bekliyor" kalemi; doluysa satırlar pakete dahildir (EXAM-007).
/// </summary>
public sealed record ExaminationTemplateApplyResultDto(
    Guid ExaminationId,
    string RowVersion,
    IReadOnlyList<string> UpdatedFields,
    IReadOnlyList<AdministrationDto> Administrations,
    IReadOnlyList<AppliedServiceLineDto> Services,
    Guid? PackageChargeSuggestionId = null);

/// <summary>
/// Uygulanan hizmet satırı. <see cref="ChargeSuggestionId"/> "ücret bekliyor" kalemidir; katalogda bedelsiz (0) hizmet kalem
/// doğurmaz ve <c>null</c> döner (ADR-018 karar 4). Fiyatlı pakette hizmet pakete dahildir (<see cref="IncludedInPackage"/>), kalem doğurmaz.
/// </summary>
public sealed record AppliedServiceLineDto(
    Guid ProductId,
    string ProductName,
    string Unit,
    decimal Quantity,
    Guid? ChargeSuggestionId,
    bool IncludedInPackage = false);

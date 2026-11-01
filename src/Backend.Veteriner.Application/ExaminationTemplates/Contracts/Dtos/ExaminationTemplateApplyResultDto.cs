using Backend.Veteriner.Application.Administrations.Contracts.Dtos;

namespace Backend.Veteriner.Application.ExaminationTemplates.Contracts.Dtos;

/// <summary>
/// Şablon uygulama sonucu. <see cref="RowVersion"/> muayenenin yeni sürümüdür (sonraki PUT bununla yapılır);
/// <see cref="UpdatedFields"/> metni değişen muayene alanları (<c>visitReason</c>, <c>findings</c>, <c>plan</c>);
/// <see cref="Text"/> aynı bilginin bölüm bazında ayrıntısı (arayüz başarı mesajı bundan kurulur).
/// <see cref="PendingChargeCount"/>: bu uygulamada doğan "ücret bekliyor" kalemi sayısı (paket kalemi, uygulama ve hizmet kalemleri).
/// <see cref="PackageChargeSuggestionId"/>: fiyatlı paketin tek "ücret bekliyor" kalemi; doluysa satırlar pakete dahildir (EXAM-007).
/// </summary>
public sealed record ExaminationTemplateApplyResultDto(
    Guid ExaminationId,
    string RowVersion,
    IReadOnlyList<string> UpdatedFields,
    IReadOnlyList<AdministrationDto> Administrations,
    IReadOnlyList<AppliedServiceLineDto> Services,
    Guid? PackageChargeSuggestionId,
    AppliedTemplateTextDto Text,
    int PendingChargeCount);

/// <summary>
/// Şablon metninin muayeneye ne kattığı. <see cref="VisitReason"/>: <c>NotInTemplate</c> (şablonda şikâyet yok), <c>Filled</c>
/// (muayenenin şikâyeti boştu, dolduruldu), <c>SkippedExisting</c> (şikâyet doluydu, şablon metni eklenmedi).
/// <see cref="FindingsLinesAdded"/> / <see cref="PlanLinesAdded"/>: alanın sonuna eklenen satır sayısı (muayenede zaten olan satırlar sayılmaz).
/// </summary>
public sealed record AppliedTemplateTextDto(string VisitReason, int FindingsLinesAdded, int PlanLinesAdded);

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

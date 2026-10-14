using Backend.Veteriner.Application.Clinics.KvkkFormTemplate;
using Backend.Veteriner.Application.Examinations;
using Backend.Veteriner.Domain.Clinics;

namespace Backend.Veteriner.Application.Clinics.Contracts.Dtos;

/// <summary>
/// Klinik KVKK açık rıza formu şablonu (ham metin; yer tutucular istemcide doldurulur).
/// <paramref name="IsDefault"/> true iken kayıt yoktur ve <paramref name="RowVersion"/> null'dır.
/// </summary>
public sealed record ClinicKvkkFormTemplateDto(
    Guid ClinicId,
    string Text,
    bool IsDefault,
    string? RowVersion,
    DateTime? UpdatedAtUtc,
    int MaxLength,
    IReadOnlyList<string> Placeholders)
{
    public static ClinicKvkkFormTemplateDto Default(Guid clinicId)
        => new(
            clinicId,
            ClinicKvkkFormTemplateDefaults.Text,
            IsDefault: true,
            RowVersion: null,
            UpdatedAtUtc: null,
            ClinicKvkkFormTemplate.MaxTextLength,
            ClinicKvkkFormTemplateDefaults.Placeholders);

    public static ClinicKvkkFormTemplateDto From(ClinicKvkkFormTemplate row)
        => new(
            row.ClinicId,
            row.Text,
            IsDefault: false,
            ExaminationRowVersion.Encode(row.RowVersion),
            row.UpdatedAtUtc ?? row.CreatedAtUtc,
            ClinicKvkkFormTemplate.MaxTextLength,
            ClinicKvkkFormTemplateDefaults.Placeholders);
}

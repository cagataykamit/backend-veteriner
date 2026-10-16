using Backend.Veteriner.Application.Clinics.KvkkFormTemplate;
using Backend.Veteriner.Application.Examinations;
using Backend.Veteriner.Domain.Clinics;

namespace Backend.Veteriner.Application.Clinics.Contracts.Dtos;

/// <summary>
/// Klinik KVKK şablonu: açık rıza formu veya aydınlatma metni (ham metin; yer tutucular istemcide doldurulur).
/// <paramref name="IsDefault"/> true iken o türde kayıt yoktur ve <paramref name="RowVersion"/> null'dır.
/// </summary>
/// <param name="TextVersion">Metin sürümü (ADR-032): varsayılan metin 0, kayıtlı metin 1'den başlar ve metin değiştikçe artar.</param>
/// <param name="TextHash">Ham metnin SHA-256 özeti (64 karakter hex).</param>
public sealed record ClinicKvkkFormTemplateDto(
    Guid ClinicId,
    string Text,
    bool IsDefault,
    string? RowVersion,
    DateTime? UpdatedAtUtc,
    int MaxLength,
    IReadOnlyList<string> Placeholders,
    int TextVersion,
    string TextHash)
{
    public static ClinicKvkkFormTemplateDto Default(Guid clinicId, ClinicKvkkFormKind kind)
    {
        var text = ClinicKvkkFormTemplateDefaults.TextFor(kind);
        return new(
            clinicId,
            text,
            IsDefault: true,
            RowVersion: null,
            UpdatedAtUtc: null,
            ClinicKvkkFormTemplate.MaxTextLength,
            ClinicKvkkFormTemplateDefaults.Placeholders,
            ClinicKvkkFormTemplate.DefaultTextVersion,
            ClinicKvkkFormTemplate.ComputeTextHash(text));
    }

    public static ClinicKvkkFormTemplateDto From(ClinicKvkkFormTemplate row)
        => new(
            row.ClinicId,
            row.Text,
            IsDefault: false,
            ExaminationRowVersion.Encode(row.RowVersion),
            row.UpdatedAtUtc ?? row.CreatedAtUtc,
            ClinicKvkkFormTemplate.MaxTextLength,
            ClinicKvkkFormTemplateDefaults.Placeholders,
            row.TextVersion,
            row.TextHash);
}

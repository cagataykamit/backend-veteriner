using Backend.Veteriner.Application.Examinations;
using Backend.Veteriner.Domain.Catalog;

namespace Backend.Veteriner.Application.ClinicalSnippets.Contracts.Dtos;

/// <summary>Klinik metin parçası; <c>shortcut</c> saklanan (küçültülmüş, <c>#</c>'siz) anahtardır.</summary>
public sealed record ClinicalSnippetDto(
    Guid Id,
    string Shortcut,
    string Title,
    string Body,
    bool IsActive,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc,
    string RowVersion)
{
    public static ClinicalSnippetDto From(ClinicalSnippet s)
        => new(s.Id, s.Shortcut, s.Title, s.Body, s.IsActive, s.CreatedAtUtc, s.UpdatedAtUtc, ExaminationRowVersion.Encode(s.RowVersion));
}

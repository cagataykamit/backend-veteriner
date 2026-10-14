using System.Text.Json.Serialization;
using Backend.Veteriner.Application.Examinations;
using Backend.Veteriner.Domain.Catalog;

namespace Backend.Veteriner.Application.ClinicalTerms.Contracts.Dtos;

/// <summary>Tanı/şikâyet sözlüğü kaydı. <c>IsSystem</c> true ise salt okunur sistem varsayılanıdır.</summary>
public sealed record ClinicalTermDto(
    Guid Id,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] ClinicalTermKind Kind,
    string Name,
    bool IsActive,
    bool IsSystem,
    string RowVersion)
{
    public static ClinicalTermDto From(ClinicalTerm term)
        => new(term.Id, term.Kind, term.Name, term.IsActive, term.IsSystem, ExaminationRowVersion.Encode(term.RowVersion));
}

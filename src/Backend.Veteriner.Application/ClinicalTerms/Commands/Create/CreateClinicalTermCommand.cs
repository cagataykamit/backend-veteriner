using System.Text.Json.Serialization;
using Backend.Veteriner.Application.ClinicalTerms.Contracts.Dtos;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Catalog;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.ClinicalTerms.Commands.Create;

/// <summary>Kliniğe özel sözlük kaydı ekler (aktif doğar).</summary>
public sealed record CreateClinicalTermCommand(
    [property: JsonConverter(typeof(JsonStringEnumConverter))] ClinicalTermKind Kind,
    string Name)
    : IRequest<Result<ClinicalTermDto>>, IAuditableRequest
{
    public string AuditAction => "ClinicalTerm.Create";
    public string? AuditTarget => $"Kind={Kind}";
}

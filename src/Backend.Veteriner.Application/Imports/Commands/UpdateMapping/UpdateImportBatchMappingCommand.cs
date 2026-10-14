using System.Text.Json.Serialization;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Imports.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Imports.Commands.UpdateMapping;

/// <summary>PUT /api/v1/imports/batches/{id}/mapping gövdesi; tam değiştirme.</summary>
public sealed record ImportMappingRequest(
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? Columns,
    IReadOnlyList<ImportValueMappingRequest>? ValueMappings,
    ImportMappingOptionsRequest? Options);

public sealed record ImportValueMappingRequest(string? Kind, string? Source, Guid? TargetId);

public sealed record ImportMappingOptionsRequest(string? OnDuplicateClient);

/// <summary>
/// Sütun ve değer eşlemesini kaydeder (<c>Mapped</c>). Audit payload'ı yalnız parti kimliğini taşır:
/// eşleme gövdesindeki kaynak değerler kişisel veri içerebilir (<see cref="Mapping"/> serileştirilmez).
/// </summary>
public sealed record UpdateImportBatchMappingCommand(Guid Id, [property: JsonIgnore] ImportMappingRequest Mapping)
    : IRequest<Result<ImportBatchDto>>, IAuditableRequest
{
    public string AuditAction => "ImportBatch.Mapping";
    public string? AuditTarget => $"ImportBatchId={Id}";
}

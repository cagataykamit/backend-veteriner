using System.Text.Json.Serialization;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Files.Contracts.Dtos;
using Backend.Veteriner.Domain.Files;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.Files.Commands.Upload;

/// <summary>
/// Sahip kayda tek dosya yükler (IMAGING_API_CONTRACT.md §5). <see cref="Content"/> okunabilir ve konumlanabilir olmalıdır
/// (çok parçalı form dosyası); denetim kaydına içerik değil yalnız ad ve boyut yazılır.
/// </summary>
public sealed record UploadStoredFileCommand(
    StoredFileOwnerType OwnerType,
    Guid OwnerId,
    string? FileName,
    long Length,
    [property: JsonIgnore] Stream Content)
    : IRequest<Result<StoredFileDto>>, IAuditableRequest
{
    public string AuditAction => OwnerType == StoredFileOwnerType.ImagingRecord
        ? "ImagingRecord.FileUpload"
        : "LabResult.FileUpload";

    public string? AuditTarget => $"{OwnerType}Id={OwnerId}";
}

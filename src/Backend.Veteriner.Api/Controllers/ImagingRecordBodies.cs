using System.Text.Json.Serialization;
using Backend.Veteriner.Domain.Imaging;

namespace Backend.Veteriner.Api.Controllers;

/// <summary>POST /api/v1/imaging-records gövdesi; <see cref="Modality"/> metin (ör. <c>XRay</c>).</summary>
public sealed class CreateImagingRecordBody
{
    public Guid ClinicId { get; init; }
    public Guid PetId { get; init; }
    public Guid? ExaminationId { get; init; }
    public DateTime PerformedAtUtc { get; init; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ImagingModality Modality { get; init; }

    public string? Description { get; init; }
    public string? ClinicalNote { get; init; }
}

/// <summary>PUT /api/v1/imaging-records/{id} gövdesi; rota kimliği esastır, <see cref="RowVersion"/> zorunlu.</summary>
public sealed class UpdateImagingRecordBody
{
    /// <summary>Opsiyonel; verilirse rota kimliğiyle aynı olmalı.</summary>
    public Guid? Id { get; init; }

    public Guid? ExaminationId { get; init; }
    public DateTime PerformedAtUtc { get; init; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ImagingModality Modality { get; init; }

    public string? Description { get; init; }
    public string? ClinicalNote { get; init; }

    /// <summary>GET ile alınan Base64 sürüm; zorunlu.</summary>
    public string? RowVersion { get; init; }
}

/// <summary>POST /api/v1/imaging-records/{id}/cancel gövdesi; gerekçe 5-500 karakter, rowVersion zorunlu.</summary>
public sealed class CancelImagingRecordBody
{
    public string? Reason { get; init; }
    public string? RowVersion { get; init; }
}

/// <summary>Dosya yükleme formu (<c>multipart/form-data</c>, tek alan <c>file</c>).</summary>
public sealed class StoredFileUploadForm
{
    public IFormFile? File { get; init; }
}

namespace Backend.Veteriner.Api.Controllers;

/// <summary>
/// POST /imports/batches form alanları (<c>multipart/form-data</c>). B1: <see cref="RequestId"/> zorunlu,
/// <see cref="Name"/> (≤ 100) ve <see cref="ClinicId"/> opsiyonel; dosya alanları B2'de eklenir.
/// </summary>
public sealed class CreateImportBatchForm
{
    public Guid? RequestId { get; init; }
    public string? Name { get; init; }
    public Guid? ClinicId { get; init; }
}

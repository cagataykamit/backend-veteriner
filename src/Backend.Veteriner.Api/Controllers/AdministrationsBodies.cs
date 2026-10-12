namespace Backend.Veteriner.Api.Controllers;

/// <summary>
/// POST /administrations gövdesi. Miktar, ürünün stok birimi cinsindendir (birim dönüşümü yoktur).
/// </summary>
public sealed class CreateAdministrationBody
{
    public Guid? ClinicId { get; init; }
    public Guid PetId { get; init; }
    public Guid? ExaminationId { get; init; }
    public Guid? TreatmentId { get; init; }
    public Guid ProductId { get; init; }
    public decimal Quantity { get; init; }
    public DateTime? AdministeredAtUtc { get; init; }
    public string? Notes { get; init; }
    public Guid? RequestId { get; init; }
}

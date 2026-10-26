namespace Backend.Veteriner.Application.Tenants.Contracts.Dtos;

/// <summary>
/// Örnek klinik temizliğinin kapsamı (EXAMPLE_CLINIC_API_CONTRACT.md §10): durum sorgusunda silinecek, temizlik
/// yanıtında silinen/pasife alınan kayıt sayıları. <see cref="OtherRecords"/> ayrı sayılmayan bağlı kayıtların toplamıdır.
/// </summary>
public sealed record ExampleClinicDataCountsDto(
    int Clients,
    int Pets,
    int Appointments,
    int Examinations,
    int Vaccinations,
    int Treatments,
    int LabResults,
    int Charges,
    int Payments,
    int Products,
    int ProductsDeactivated,
    int StockMovements,
    int OtherRecords)
{
    public static ExampleClinicDataCountsDto Empty { get; } = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
}

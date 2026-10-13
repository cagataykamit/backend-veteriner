namespace Backend.Veteriner.Application.Pets.Access;

/// <summary>
/// Hayvana bağlı klinik kayıt türü. Sayısal değer timeline sıralamasındaki sabit <c>typeOrder</c>'dır; mevcut değerler değişmez, yeni tür sona eklenir.
/// </summary>
public enum PetRecordKind
{
    Visit = 1,
    Examination = 2,
    Treatment = 3,
    Prescription = 4,
    LabResult = 5,
    Vaccination = 6,
    Hospitalization = 7,
    Appointment = 8,
    Payment = 9,
    Administration = 10
}

namespace Backend.Veteriner.Domain.Appointments;

/// <summary>
/// Randevunun klinik işlem / ziyaret türü (hayvan türü değildir).
/// Sayısal değerler stabil tutulmalıdır; JSON varsayılanında <c>int</c> olarak serileşir.
/// </summary>
public enum AppointmentType
{
    Examination = 0,
    Vaccination = 1,
    Checkup = 2,
    Surgery = 3,
    Grooming = 4,
    Consultation = 5,
    Other = 6
}

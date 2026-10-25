namespace Backend.Veteriner.Domain.Tenants;

/// <summary>
/// Deneme aktivasyonunda "anlamlı aksiyon" türleri (TRIAL-008): kiracı kullanıcısının kendi eliyle yaptığı ilk kayıt.
/// Komut eşlemesi <c>docs/TRIAL_ACTIVATION_METRICS_API_CONTRACT.md</c> §2.1'de; enum adı API'de string olarak döner.
/// </summary>
public enum ActivationSignalKind
{
    ClientCreated = 1,
    PetCreated = 2,
    AppointmentCreated = 3,
    ExaminationCreated = 4,
    VaccinationCreated = 5,
    ChargeCreated = 6,
    PaymentCreated = 7,
    SaleCreated = 8,
    ProductCreated = 9,
}

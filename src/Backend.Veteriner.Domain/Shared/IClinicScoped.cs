namespace Backend.Veteriner.Domain.Shared;

/// <summary>
/// Kliniğe bağlı kayıt. Klinik kapsam filtresi (seçili klinik veya kullanıcının erişebildiği klinikler) bu arayüz
/// üzerinden tek yerde uygulanır; filtre her varlık için ayrı yazılmaz.
/// </summary>
public interface IClinicScoped
{
    Guid ClinicId { get; }
}

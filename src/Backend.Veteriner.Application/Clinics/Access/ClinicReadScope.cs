namespace Backend.Veteriner.Application.Clinics.Access;

/// <summary>
/// Bir okuma sorgusu için kullanıcı/rol kombinasyonundan çözümlenen efektif klinik kapsamı.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><description>Her iki alan da <c>null</c> ise tenant-wide okuma (Admin / Owner, klinik filtresi yok).</description></item>
/// <item><description><see cref="SingleClinicId"/> dolu ise yalnızca o klinik filtrelenir.</description></item>
/// <item><description><see cref="AccessibleClinicIds"/> dolu (boş olabilir) ise <c>ClinicId IN (...)</c> filtresi uygulanır;
/// boş liste, ClinicAdmin'in atanmış kliniği olmadığını ifade eder ve sorgu sonucu boş kümeye düşürülmelidir.</description></item>
/// </list>
/// </remarks>
public sealed record ClinicReadScope(
    Guid? SingleClinicId,
    IReadOnlyCollection<Guid>? AccessibleClinicIds);

using Backend.Veteriner.Domain.Users;

namespace Backend.Veteriner.Domain.Clinics;

/// <summary>
/// Kullanıcının bir kliniğe atanmış erişimini temsil eder; /me/clinics ve select-clinic doğrulaması için kaynak.
/// </summary>
public sealed class UserClinic
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid UserId { get; private set; }
    public Guid ClinicId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    public User? User { get; private set; }
    public Clinic? Clinic { get; private set; }

    private UserClinic() { }

    public UserClinic(Guid userId, Guid clinicId)
    {
        if (userId == Guid.Empty)
            throw new ArgumentException("UserId geçersiz.", nameof(userId));
        if (clinicId == Guid.Empty)
            throw new ArgumentException("ClinicId geçersiz.", nameof(clinicId));

        UserId = userId;
        ClinicId = clinicId;
        CreatedAtUtc = DateTime.UtcNow;
    }
}

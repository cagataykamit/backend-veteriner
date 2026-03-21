using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Users.Events;

namespace Backend.Veteriner.Domain.Users;

/// <summary>
/// User aggregate root.
/// Kimlik, rol ve refresh token ya�am d�ng�s�n� y�netir.
/// </summary>
public class User : AggregateRoot
{
    private readonly List<UserRole> _roles = new();
    private readonly List<RefreshToken> _refreshTokens = new();

    public Guid Id { get; private set; } = Guid.NewGuid();

    public string Email { get; private set; } = default!;
    public string PasswordHash { get; private set; } = default!;

    public bool EmailConfirmed { get; private set; }

    /// <summary>
    /// Kullan?c?n?n olu�turulma zaman? (UTC).
    /// Admin listeleme, audit ve raporlama i�in kritik metadatad?r.
    /// </summary>
    public DateTime CreatedAtUtc { get; private set; } = DateTime.UtcNow;

    /// <summary>
    /// Kullan?c?n?n son g�ncellenme zaman? (UTC). Opsiyonel ama �nerilir.
    /// </summary>
    public DateTime? UpdatedAtUtc { get; private set; }

    public IReadOnlyCollection<UserRole> Roles => _roles.AsReadOnly();
    public IReadOnlyCollection<RefreshToken> RefreshTokens => _refreshTokens.AsReadOnly();

    private User() { } // EF i�in

    public User(string email, string passwordHash)
    {
        Email = email ?? throw new ArgumentNullException(nameof(email));
        PasswordHash = passwordHash ?? throw new ArgumentNullException(nameof(passwordHash));

        // Kurumsal standart: olu�turma an?n? domain'de sabitle
        CreatedAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = null;

        AddDomainEvent(new UserCreatedDomainEvent(Id, Email));
    }

    public void ConfirmEmail()
    {
        EmailConfirmed = true;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Role ekler. Domain kural ihlallerinde exception f?rlatmaz, Result d�ner.
    /// </summary>
    public Result AddRole(string roleName)
    {
        if (string.IsNullOrWhiteSpace(roleName))
            return Result.Failure(UserErrors.RoleNameEmpty);

        var normalized = roleName.Trim();

        if (_roles.Any(r => string.Equals(r.Name, normalized, StringComparison.OrdinalIgnoreCase)))
            return Result.Failure(UserErrors.RoleAlreadyExists);

        _roles.Add(new UserRole(normalized));
        UpdatedAtUtc = DateTime.UtcNow;

        return Result.Success();
    }

    public void AddRefreshToken(RefreshToken token)
    {
        _refreshTokens.Add(token);
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void RevokeRefreshToken(string tokenHash)
    {
        var token = _refreshTokens.FirstOrDefault(t => t.TokenHash == tokenHash);
        token?.Revoke();
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void UpdatePasswordHash(string newHash)
    {
        PasswordHash = newHash ?? throw new ArgumentNullException(nameof(newHash));
        UpdatedAtUtc = DateTime.UtcNow;
    }
}

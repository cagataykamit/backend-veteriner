using Backend.Veteriner.Domain.Users;

namespace Backend.Veteriner.Domain.Auth;

/// <summary>
/// Kullan�c� ile OperationClaim (rol) aras�ndaki ili�ki tablosu.
/// </summary>
public sealed class UserOperationClaim
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid UserId { get; private set; }
    public Guid OperationClaimId { get; private set; }

    // Navigation props (iste�e ba�l�)
    public User? User { get; private set; }
    public OperationClaim? OperationClaim { get; private set; }

    private UserOperationClaim() { } // EF Core i�in

    public UserOperationClaim(Guid userId, Guid operationClaimId)
    {
        UserId = userId;
        OperationClaimId = operationClaimId;
    }
}

namespace Backend.Veteriner.Application.Auth.Contracts.Dtos;

/// <summary>
/// Admin ekranları için detaylı ilişki çıktısı (kullanıcı email + rol adı).
/// </summary>
public sealed record UserOperationClaimDetailDto(
    Guid Id,
    Guid UserId,
    string UserEmail,
    Guid OperationClaimId,
    string OperationClaimName
);

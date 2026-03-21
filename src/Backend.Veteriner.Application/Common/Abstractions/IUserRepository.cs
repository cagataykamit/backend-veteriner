using Backend.Veteriner.Domain.Users;

namespace Backend.Veteriner.Application.Common.Abstractions;

/// <summary>
/// User aggregate write sözleşmesi.
/// Hem write hem read sözleşmesini kapsar.
/// </summary>
public interface IUserRepository : IRepository<User>, IUserReadRepository
{
}

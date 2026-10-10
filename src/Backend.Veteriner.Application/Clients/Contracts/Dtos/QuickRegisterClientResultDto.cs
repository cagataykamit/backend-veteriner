namespace Backend.Veteriner.Application.Clients.Contracts.Dtos;

/// <summary>POST /clients/quick-register yanıtı; geliş için <see cref="PetId"/> ile POST /visits çağrılır.</summary>
public sealed record QuickRegisterClientResultDto(Guid ClientId, Guid PetId);

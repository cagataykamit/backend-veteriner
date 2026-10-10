using Backend.Veteriner.Application.Clients.Commands.Create;
using Backend.Veteriner.Application.Clients.Contracts.Dtos;
using Backend.Veteriner.Application.Pets.Commands.Create;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Clients.Commands.QuickRegister;

/// <summary>
/// Mevcut müşteri ve hayvan oluşturma komutlarını sırayla çalıştırır (doğrulama, mükerrer kontrol, domain,
/// outbox/projeksiyon aynen). Atomiklik, komutun <see cref="Common.Abstractions.ITransactionalRollbackOnFailureRequest"/>
/// işaretiyle <c>TransactionBehavior</c> tarafından sağlanır: ikinci adım başarısız olursa birinci adım geri alınır.
/// </summary>
public sealed class QuickRegisterClientCommandHandler
    : IRequestHandler<QuickRegisterClientCommand, Result<QuickRegisterClientResultDto>>
{
    private readonly ISender _sender;

    public QuickRegisterClientCommandHandler(ISender sender) => _sender = sender;

    public async Task<Result<QuickRegisterClientResultDto>> Handle(
        QuickRegisterClientCommand request, CancellationToken ct)
    {
        var client = await _sender.Send(
            new CreateClientCommand(request.FullName, Phone: request.Phone), ct);
        if (!client.IsSuccess)
            return Result<QuickRegisterClientResultDto>.Failure(client.Error);

        var pet = await _sender.Send(
            new CreatePetCommand(
                client.Value!.Id,
                request.PetName,
                request.SpeciesId,
                request.Breed,
                request.BirthDate,
                request.BreedId,
                request.Gender,
                MicrochipNumber: request.MicrochipNumber,
                IsNeutered: request.IsNeutered),
            ct);
        if (!pet.IsSuccess)
            return Result<QuickRegisterClientResultDto>.Failure(pet.Error);

        return Result<QuickRegisterClientResultDto>.Success(
            new QuickRegisterClientResultDto(client.Value.Id, pet.Value));
    }
}

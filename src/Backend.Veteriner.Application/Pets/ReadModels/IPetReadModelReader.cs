using Backend.Veteriner.Application.Pets.Contracts.Dtos;

namespace Backend.Veteriner.Application.Pets.ReadModels;

public interface IPetReadModelReader
{
    Task<PetListReadResult> GetListAsync(
        PetListReadRequest request,
        CancellationToken cancellationToken = default);
}

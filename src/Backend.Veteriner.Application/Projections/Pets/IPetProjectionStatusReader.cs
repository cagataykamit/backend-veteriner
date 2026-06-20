namespace Backend.Veteriner.Application.Projections.Pets;

public interface IPetProjectionStatusReader
{
    Task<PetProjectionStatus> GetStatusAsync(CancellationToken cancellationToken = default);
}

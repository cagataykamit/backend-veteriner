namespace Backend.Veteriner.Application.Projections.Clients;

public interface IClientProjectionStatusReader
{
    Task<ClientProjectionStatus> GetStatusAsync(CancellationToken cancellationToken = default);
}

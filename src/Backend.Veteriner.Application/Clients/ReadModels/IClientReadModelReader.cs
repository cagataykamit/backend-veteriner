namespace Backend.Veteriner.Application.Clients.ReadModels;

public interface IClientReadModelReader
{
    Task<ClientListReadResult> GetListAsync(
        ClientListReadRequest request,
        CancellationToken cancellationToken = default);
}

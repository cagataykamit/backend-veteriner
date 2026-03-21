using Backend.Veteriner.Application.Clients.Contracts.Dtos;
using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Clients.Queries.GetById;

public sealed class GetClientByIdQueryHandler : IRequestHandler<GetClientByIdQuery, Result<ClientDetailDto>>
{
    private readonly IReadRepository<Client> _clients;

    public GetClientByIdQueryHandler(IReadRepository<Client> clients) => _clients = clients;

    public async Task<Result<ClientDetailDto>> Handle(GetClientByIdQuery request, CancellationToken ct)
    {
        var client = await _clients.FirstOrDefaultAsync(new ClientByIdSpec(request.TenantId, request.Id), ct);
        if (client is null)
            return Result<ClientDetailDto>.Failure("Clients.NotFound", "Müşteri bulunamadı.");

        var dto = new ClientDetailDto(client.Id, client.TenantId, client.FullName, client.Phone);
        return Result<ClientDetailDto>.Success(dto);
    }
}

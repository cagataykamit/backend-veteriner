using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Public.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Public.Commands.SignupAndAcceptInvite;

// Public davet akışı (anonim signup + accept) tenant abonelik durumundan bağımsızdır;
// guard çağıran tarafı (henüz tenant context yok) blokladığında akış kullanılamaz hale gelir.
public sealed record SignupAndAcceptTenantInviteCommand(string RawToken, string Password)
    : IRequest<Result<TenantInviteAcceptResultDto>>, IIgnoreTenantWriteSubscriptionGuard;

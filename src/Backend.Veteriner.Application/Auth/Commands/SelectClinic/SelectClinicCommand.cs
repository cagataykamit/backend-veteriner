using Backend.Veteriner.Application.Auth.Commands.Login;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Auth.Commands.SelectClinic;

public sealed record SelectClinicCommand(string RefreshToken, Guid ClinicId)
    : IRequest<Result<LoginResultDto>>, IIgnoreTenantWriteSubscriptionGuard;


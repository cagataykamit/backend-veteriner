using Backend.Veteriner.Application.Auth.Commands.Login;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Auth.Commands.Refresh;

/// <summary>
/// Access token yenileme. Kiracı yalnızca sunucudaki refresh kaydından alınır; istekte tenant alanı yoktur.
/// </summary>
/// <param name="RefreshToken">Ham refresh token (JSON alanı: <c>refreshToken</c>).</param>
public sealed record RefreshCommand(string RefreshToken)
    : IRequest<Result<LoginResultDto>>, IIgnoreTenantWriteSubscriptionGuard;

using Backend.Veteriner.Application.Public.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Public.Commands.OwnerSignup;

public sealed record PublicOwnerSignupCommand(
    string PlanCode,
    string TenantName,
    string ClinicName,
    string ClinicCity,
    string Email,
    string Password) : IRequest<Result<PublicOwnerSignupResultDto>>;

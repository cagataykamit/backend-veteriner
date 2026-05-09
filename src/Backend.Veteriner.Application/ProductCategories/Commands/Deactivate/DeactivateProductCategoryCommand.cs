using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.ProductCategories.Commands.Deactivate;

public sealed record DeactivateProductCategoryCommand(Guid Id) : IRequest<Result>;

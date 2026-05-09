using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.ProductCategories.Commands.Activate;

public sealed record ActivateProductCategoryCommand(Guid Id) : IRequest<Result>;

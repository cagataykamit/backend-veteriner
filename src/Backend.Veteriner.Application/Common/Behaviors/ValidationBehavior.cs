using FluentValidation;
using Mediator;

namespace Backend.Veteriner.Application.Common.Behaviors;

public sealed class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull, IMessage
{
    private readonly IEnumerable<IValidator<TRequest>> _validators;
    public ValidationBehavior(IEnumerable<IValidator<TRequest>> validators) => _validators = validators;

    public async ValueTask<TResponse> Handle(TRequest request, MessageHandlerDelegate<TRequest, TResponse> next, CancellationToken ct)
    {
        if (_validators.Any())
        {
            var context = new ValidationContext<TRequest>(request);
            var failures = (await Task.WhenAll(_validators.Select(v => v.ValidateAsync(context, ct))))
                           .SelectMany(r => r.Errors)
                           .Where(f => f is not null)
                           .ToList();

            if (failures.Count != 0)
                throw new ValidationException(failures);
        }

        return await next(request, ct);
    }
}

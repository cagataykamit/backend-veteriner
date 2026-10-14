using FluentValidation;

namespace Backend.Veteriner.Application.Imports.Commands.Create;

/// <summary>İstek biçimi; ad uzunluğu ve diğer parti kuralları domain'de (<c>ImportBatch.Create</c>).</summary>
public sealed class CreateImportBatchCommandValidator : AbstractValidator<CreateImportBatchCommand>
{
    public CreateImportBatchCommandValidator()
    {
        RuleFor(x => x.RequestId).NotEmpty();
        RuleFor(x => x.ClinicId).NotEqual(Guid.Empty).When(x => x.ClinicId.HasValue);
    }
}

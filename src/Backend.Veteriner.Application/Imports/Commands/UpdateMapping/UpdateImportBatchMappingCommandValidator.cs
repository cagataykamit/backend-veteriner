using Backend.Veteriner.Application.Imports.Mapping;
using FluentValidation;

namespace Backend.Veteriner.Application.Imports.Commands.UpdateMapping;

/// <summary>İstek biçimi; tablo/sütun/hedef denetimleri partiye bağlı olduğundan handler'dadır.</summary>
public sealed class UpdateImportBatchMappingCommandValidator : AbstractValidator<UpdateImportBatchMappingCommand>
{
    public const int MaxValueMappingSourceLength = 200;

    public UpdateImportBatchMappingCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Mapping).NotNull();
        RuleFor(x => x.Mapping.Options!.OnDuplicateClient)
            .Must(v => v is null || ImportOptionsDocument.AllowedDuplicateClientModes.Contains(v))
            .When(x => x.Mapping?.Options is not null)
            .WithMessage("options.onDuplicateClient Match, Skip veya Error olmalıdır.");
        RuleForEach(x => x.Mapping.ValueMappings)
            .ChildRules(v =>
            {
                v.RuleFor(m => m.Source)
                    .NotEmpty().MaximumLength(MaxValueMappingSourceLength)
                    .WithMessage("valueMappings[].source boş olamaz ve en çok 200 karakter olabilir.");
                v.RuleFor(m => m.Kind)
                    .Must(k => ImportValueKinds.TryParse(k, out _))
                    .WithMessage("valueMappings[].kind species, breed, vaccine veya color olmalıdır.");
            })
            .When(x => x.Mapping?.ValueMappings is not null);
    }
}

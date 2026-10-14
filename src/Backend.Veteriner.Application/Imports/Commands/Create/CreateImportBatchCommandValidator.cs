using Backend.Veteriner.Application.Imports.Reading;
using FluentValidation;

namespace Backend.Veteriner.Application.Imports.Commands.Create;

/// <summary>İstek biçimi; ad uzunluğu ve diğer parti kuralları domain'de (<c>ImportBatch.Create</c>), dosya kuralları okuyucuda.</summary>
public sealed class CreateImportBatchCommandValidator : AbstractValidator<CreateImportBatchCommand>
{
    public CreateImportBatchCommandValidator()
    {
        RuleFor(x => x.RequestId).NotEmpty();
        RuleFor(x => x.ClinicId).NotEqual(Guid.Empty).When(x => x.ClinicId.HasValue);
        RuleFor(x => x.Encoding)
            .Must(e => CsvTableReader.TryParseEncoding(e, out _))
            .WithMessage("encoding utf-8 veya windows-1254 olmalıdır.");
        RuleFor(x => x.Delimiter)
            .Must(d => CsvTableReader.TryParseDelimiter(d, out _))
            .WithMessage("delimiter ';', ',' veya sekme (tab) olmalıdır.");
    }
}

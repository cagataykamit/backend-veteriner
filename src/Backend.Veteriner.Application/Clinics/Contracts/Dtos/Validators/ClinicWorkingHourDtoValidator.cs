using FluentValidation;

namespace Backend.Veteriner.Application.Clinics.Contracts.Dtos.Validators;

public sealed class ClinicWorkingHourDtoValidator : AbstractValidator<ClinicWorkingHourDto>
{
    public ClinicWorkingHourDtoValidator()
    {
        RuleFor(x => x.DayOfWeek).IsInEnum();

        When(x => x.IsClosed, () =>
        {
            RuleFor(x => x.OpensAt).Must(v => v is null);
            RuleFor(x => x.ClosesAt).Must(v => v is null);
            RuleFor(x => x.BreakStartsAt).Must(v => v is null);
            RuleFor(x => x.BreakEndsAt).Must(v => v is null);
        });

        When(x => !x.IsClosed, () =>
        {
            RuleFor(x => x.OpensAt).NotNull();
            RuleFor(x => x.ClosesAt).NotNull();

            When(x => x.OpensAt is not null && x.ClosesAt is not null, () =>
            {
                RuleFor(x => x)
                    .Must(x => x.OpensAt!.Value < x.ClosesAt!.Value)
                    .WithMessage("OpensAt, ClosesAt'tan küçük olmalıdır.");
            });

            RuleFor(x => x)
                .Must(x =>
                    (x.BreakStartsAt is null && x.BreakEndsAt is null)
                    || (x.BreakStartsAt is not null && x.BreakEndsAt is not null))
                .WithMessage("Mola başlangıç ve bitiş birlikte verilmelidir veya ikisi de boş olmalıdır.");

            When(x => x.BreakStartsAt is not null && x.BreakEndsAt is not null && x.OpensAt is not null && x.ClosesAt is not null, () =>
            {
                RuleFor(x => x)
                    .Must(x => x.BreakStartsAt!.Value < x.BreakEndsAt!.Value)
                    .WithMessage("Mola başlangıç, mola bitişten küçük olmalıdır.");
                RuleFor(x => x)
                    .Must(x =>
                        x.BreakStartsAt!.Value >= x.OpensAt!.Value
                        && x.BreakEndsAt!.Value <= x.ClosesAt!.Value)
                    .WithMessage("Mola aralığı çalışma saatleri içinde olmalıdır.");
            });
        });
    }
}

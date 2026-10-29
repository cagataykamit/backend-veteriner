using FluentValidation;

namespace Backend.Veteriner.Application.OwnerPortal.Commands.UpdatePortalReminderPreferences;

public sealed class UpdatePortalReminderPreferencesCommandValidator : AbstractValidator<UpdatePortalReminderPreferencesCommand>
{
    public UpdatePortalReminderPreferencesCommandValidator()
    {
        RuleFor(x => x.RemindersOptedOut).NotNull();
    }
}

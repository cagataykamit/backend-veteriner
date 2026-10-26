using Backend.Veteriner.Application.Reminders.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.Reminders.Queries.GetSettings;

public sealed record GetReminderSettingsQuery : IRequest<Result<ReminderSettingsDto>>;

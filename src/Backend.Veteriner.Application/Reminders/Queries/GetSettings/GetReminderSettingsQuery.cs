using Backend.Veteriner.Application.Reminders.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Reminders.Queries.GetSettings;

public sealed record GetReminderSettingsQuery : IRequest<Result<ReminderSettingsDto>>;

using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.Reminders.Contracts.Dtos;
using Backend.Veteriner.Domain.Reminders;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Reminders.Queries.GetLogs;

public sealed record GetReminderDispatchLogsQuery(
    PageRequest PageRequest,
    ReminderType? ReminderType = null,
    ReminderDispatchStatus? Status = null,
    DateTime? FromUtc = null,
    DateTime? ToUtc = null)
    : IRequest<Result<PagedResult<ReminderDispatchLogItemDto>>>;

using System.Text.Json.Serialization;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Clients.Commands.Update;

/// <param name="PreferredContactChannel"><c>null</c>: dokunma (alanı bilmeyen istemci tercihi silmez); <c>None</c>: temizle.</param>
/// <param name="RemindersOptedOut"><c>null</c>: dokunma.</param>
public sealed record UpdateClientCommand(
    Guid Id,
    string FullName,
    string? Email = null,
    string? Phone = null,
    string? Address = null,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] ContactChannel? PreferredContactChannel = null,
    bool? RemindersOptedOut = null)
    : IRequest<Result>;

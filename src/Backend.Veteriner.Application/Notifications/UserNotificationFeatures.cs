using Ardalis.Specification;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Domain.Notifications;
using Backend.Veteriner.Domain.Shared;
using FluentValidation;
using Mediator;

namespace Backend.Veteriner.Application.Notifications;

public sealed record UserNotificationDto(
    Guid Id,
    UserNotificationType Type,
    string Title,
    string? Body,
    string? TargetType,
    Guid? TargetId,
    Guid? ClinicId,
    DateTime CreatedAtUtc,
    DateTime? ReadAtUtc);

public sealed record UserNotificationUnreadCountDto(int Count);

/// <summary>Oturumdaki kullanıcının bildirimleri (yeniden eskiye); tür ve yalnız okunmamış filtresi.</summary>
public sealed record GetMyNotificationsQuery(UserNotificationType? Type, bool UnreadOnly, int Page, int PageSize)
    : IRequest<Result<PagedResult<UserNotificationDto>>>;

/// <summary>Okunmamış rozeti.</summary>
public sealed record GetMyUnreadNotificationCountQuery : IRequest<Result<UserNotificationUnreadCountDto>>;

public sealed record MarkNotificationReadCommand(Guid Id) : IRequest<Result>;

/// <summary>Tümünü okundu işaretler; tür verilirse yalnız o tür.</summary>
public sealed record MarkAllNotificationsReadCommand(UserNotificationType? Type) : IRequest<Result>;

public sealed class GetMyNotificationsQueryValidator : AbstractValidator<GetMyNotificationsQuery>
{
    public GetMyNotificationsQueryValidator()
    {
        RuleFor(x => x.Type).IsInEnum().When(x => x.Type.HasValue);
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
    }
}

public sealed class UserNotificationsSpec : Specification<UserNotification>
{
    public UserNotificationsSpec(Guid tenantId, Guid userId, UserNotificationType? type, bool unreadOnly, int? page = null, int? pageSize = null)
    {
        Query.Where(x => x.TenantId == tenantId && x.UserId == userId);
        if (type is { } t)
            Query.Where(x => x.Type == t);
        if (unreadOnly)
            Query.Where(x => x.ReadAtUtc == null);
        Query.OrderByDescending(x => x.CreatedAtUtc);
        if (page is { } p && pageSize is { } size)
            Query.Skip((p - 1) * size).Take(size);
    }
}

/// <summary>Kullanıcının kendi bildirimi; başkasının bildirimi yokmuş gibi 404.</summary>
public sealed class UserNotificationByIdSpec : Specification<UserNotification>
{
    public UserNotificationByIdSpec(Guid tenantId, Guid userId, Guid id)
        => Query.Where(x => x.TenantId == tenantId && x.UserId == userId && x.Id == id);
}

internal static class NotificationUser
{
    public static Result<(Guid TenantId, Guid UserId)> Resolve(ITenantContext tenant, IClientContext client)
        => tenant.TenantId is { } tenantId && client.UserId is { } userId
            ? Result<(Guid, Guid)>.Success((tenantId, userId))
            : Result<(Guid, Guid)>.Failure("Auth.Unauthorized.UserContextMissing", "Kullanıcı bağlamı yok.");

    public static UserNotificationDto ToDto(UserNotification n)
        => new(n.Id, n.Type, n.Title, n.Body, n.TargetType, n.TargetId, n.ClinicId, n.CreatedAtUtc, n.ReadAtUtc);
}

public sealed class GetMyNotificationsQueryHandler : IRequestHandler<GetMyNotificationsQuery, Result<PagedResult<UserNotificationDto>>>
{
    private readonly ITenantContext _tenant;
    private readonly IClientContext _client;
    private readonly IReadRepository<UserNotification> _notifications;

    public GetMyNotificationsQueryHandler(ITenantContext tenant, IClientContext client, IReadRepository<UserNotification> notifications)
    {
        _tenant = tenant;
        _client = client;
        _notifications = notifications;
    }

    public async ValueTask<Result<PagedResult<UserNotificationDto>>> Handle(GetMyNotificationsQuery request, CancellationToken ct)
    {
        var user = NotificationUser.Resolve(_tenant, _client);
        if (!user.IsSuccess)
            return Result<PagedResult<UserNotificationDto>>.Failure(user.Error);
        var (tenantId, userId) = user.Value;

        var total = await _notifications.CountAsync(new UserNotificationsSpec(tenantId, userId, request.Type, request.UnreadOnly), ct);
        var rows = total == 0
            ? []
            : await _notifications.ListAsync(
                new UserNotificationsSpec(tenantId, userId, request.Type, request.UnreadOnly, request.Page, request.PageSize), ct);
        return Result<PagedResult<UserNotificationDto>>.Success(
            PagedResult<UserNotificationDto>.Create(rows.Select(NotificationUser.ToDto).ToList(), total, request.Page, request.PageSize));
    }
}

public sealed class GetMyUnreadNotificationCountQueryHandler
    : IRequestHandler<GetMyUnreadNotificationCountQuery, Result<UserNotificationUnreadCountDto>>
{
    private readonly ITenantContext _tenant;
    private readonly IClientContext _client;
    private readonly IReadRepository<UserNotification> _notifications;

    public GetMyUnreadNotificationCountQueryHandler(ITenantContext tenant, IClientContext client, IReadRepository<UserNotification> notifications)
    {
        _tenant = tenant;
        _client = client;
        _notifications = notifications;
    }

    public async ValueTask<Result<UserNotificationUnreadCountDto>> Handle(GetMyUnreadNotificationCountQuery request, CancellationToken ct)
    {
        var user = NotificationUser.Resolve(_tenant, _client);
        if (!user.IsSuccess)
            return Result<UserNotificationUnreadCountDto>.Failure(user.Error);

        var count = await _notifications.CountAsync(new UserNotificationsSpec(user.Value.TenantId, user.Value.UserId, null, unreadOnly: true), ct);
        return Result<UserNotificationUnreadCountDto>.Success(new UserNotificationUnreadCountDto(count));
    }
}

public sealed class MarkNotificationReadCommandHandler : IRequestHandler<MarkNotificationReadCommand, Result>
{
    private readonly ITenantContext _tenant;
    private readonly IClientContext _client;
    private readonly IRepository<UserNotification> _notifications;
    private readonly TimeProvider _timeProvider;

    public MarkNotificationReadCommandHandler(
        ITenantContext tenant,
        IClientContext client,
        IRepository<UserNotification> notifications,
        TimeProvider timeProvider)
    {
        _tenant = tenant;
        _client = client;
        _notifications = notifications;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result> Handle(MarkNotificationReadCommand request, CancellationToken ct)
    {
        var user = NotificationUser.Resolve(_tenant, _client);
        if (!user.IsSuccess)
            return Result.Failure(user.Error);

        var row = await _notifications.FirstOrDefaultAsync(new UserNotificationByIdSpec(user.Value.TenantId, user.Value.UserId, request.Id), ct);
        if (row is null)
            return Result.Failure("Notifications.NotFound", "Bildirim bulunamadı.");

        if (row.MarkRead(_timeProvider.GetUtcNow().UtcDateTime))
            await _notifications.SaveChangesAsync(ct);
        return Result.Success();
    }
}

public sealed class MarkAllNotificationsReadCommandHandler : IRequestHandler<MarkAllNotificationsReadCommand, Result>
{
    private const int Batch = 500;

    private readonly ITenantContext _tenant;
    private readonly IClientContext _client;
    private readonly IRepository<UserNotification> _notifications;
    private readonly TimeProvider _timeProvider;

    public MarkAllNotificationsReadCommandHandler(
        ITenantContext tenant,
        IClientContext client,
        IRepository<UserNotification> notifications,
        TimeProvider timeProvider)
    {
        _tenant = tenant;
        _client = client;
        _notifications = notifications;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result> Handle(MarkAllNotificationsReadCommand request, CancellationToken ct)
    {
        var user = NotificationUser.Resolve(_tenant, _client);
        if (!user.IsSuccess)
            return Result.Failure(user.Error);

        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        var rows = await _notifications.ListAsync(
            new UserNotificationsSpec(user.Value.TenantId, user.Value.UserId, request.Type, unreadOnly: true, page: 1, pageSize: Batch), ct);
        foreach (var row in rows)
            row.MarkRead(utcNow);
        if (rows.Count > 0)
            await _notifications.SaveChangesAsync(ct);
        return Result.Success();
    }
}

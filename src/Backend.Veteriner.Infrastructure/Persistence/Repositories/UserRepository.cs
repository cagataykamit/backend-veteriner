using System.Collections.Generic;
using System.Diagnostics;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.Users.Contracts.Dtos;
using Backend.Veteriner.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Backend.Veteriner.Infrastructure.Persistence.Repositories;

/// <summary>
/// User aggregate�i i�in repository implementasyonu.
/// 
/// Notlar:
/// - Projede Ardalis.Specification kullan�ld��� i�in generic repository (EfRepository) temel CRUD/spec i�lerini ta��r.
/// - Admin listeleme gibi paging+filter+projection senaryolar� i�in IQueryable gerekti�inden,
///   bu use-case �zel metot olarak IUserReadRepository �zerinde tan�mlan�r ve burada implement edilir.
/// </summary>
public sealed class UserRepository : EfRepository<User>, IUserRepository
{
    private readonly AppDbContext _db;
    private readonly ILogger<UserRepository> _logger;

    public UserRepository(AppDbContext db, ILogger<UserRepository> logger) : base(db)
    {
        _db = db;
        _logger = logger;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Yalnızca <c>Users</c> kolonları; owned <c>UserRoles</c> bu sorguya dahil edilmez.
    /// Yavaşlık görülüp EF.SlowSql düşmüyorsa süre çoğunlukla bağlantı havuzu / ilk açılış / ağdur;
    /// DbConnectionSlowOpenInterceptor logları ile birlikte değerlendirin.
    /// </remarks>
    public async Task<LoginUserLookupResult?> GetForLoginByEmailAsync(string email, CancellationToken ct = default)
    {
        var query = _db.Users
            .AsNoTracking()
            .Where(u => u.Email == email)
            .TagWith("login:user-lookup projection (Users only, no UserRoles)")
            .Select(u => new LoginUserLookupResult(
                u.Id,
                u.Email,
                u.EmailConfirmed,
                u.PasswordHash));

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            try
            {
                _logger.LogDebug("Login user lookup SQL preview: {Sql}", query.ToQueryString());
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Login user lookup: ToQueryString unavailable");
            }
        }

        var sw = Stopwatch.StartNew();
        var result = await query.FirstOrDefaultAsync(ct);
        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug(
                "Login user lookup completed: wall-clock {ElapsedMs}ms (connection lease + execute + reader + scalar materialization; single FirstOrDefaultAsync)",
                sw.ElapsedMilliseconds);
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetRoleNamesByUserIdAsync(Guid userId, CancellationToken ct = default)
    {
        return await _db.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .SelectMany(u => u.Roles)
            .Select(r => r.Name)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Admin kullan�c� listeleme i�in sayfal� sorgu.
    /// IReadRepository IQueryable sa�lamad��� i�in bu metot do�rudan DbContext �zerinden y�r�t�l�r.
    /// </summary>
    public async Task<PagedResult<AdminUserListItemDto>> GetAdminPagedAsync(PageRequest req, CancellationToken ct)
    {
        var page = Math.Max(1, req.Page);
        var pageSize = Math.Clamp(req.PageSize, 1, 200);

        // AsNoTracking: admin listeleme read-only, performans i�in
        var q = _db.Users
            .AsNoTracking()
            .Include(u => u.Roles)
            .AsQueryable();

        // Arama (email)
        if (!string.IsNullOrWhiteSpace(req.Search))
        {
            var s = req.Search.Trim();
            q = q.Where(u => u.Email.Contains(s));
        }

        // Toplam
        var total = await q.CountAsync(ct);

        // Sayfal� liste
        var items = await q
            .OrderBy(u => u.Email)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(u => new AdminUserListItemDto(
                u.Id,
                u.Email,
                u.EmailConfirmed,
                // User entity'nizde CreatedAtUtc yoksa hem DTO'dan hem buradan kald�r�n.
                u.CreatedAtUtc,
                u.Roles.Select(r => r.Name).ToList()
            ))
            .ToListAsync(ct);

        return PagedResult<AdminUserListItemDto>.Create(items, total, page, pageSize);
    }
}

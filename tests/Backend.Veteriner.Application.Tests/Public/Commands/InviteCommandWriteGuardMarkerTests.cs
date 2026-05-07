using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Public.Commands.AcceptInvite;
using Backend.Veteriner.Application.Public.Commands.SignupAndAcceptInvite;
using FluentAssertions;

namespace Backend.Veteriner.Application.Tests.Public.Commands;

/// <summary>
/// Faz 4B-1: davet kabul akışı (oturumlu accept ve anonim signup+accept) tenant subscription
/// write-guard'ından muaf olmalıdır. Aksi halde mevcut tenantı read-only/cancelled olan kullanıcı
/// başka bir tenanta davet kabul edemez ve anonim signup+accept akışı kilitlenebilir.
/// </summary>
public sealed class InviteCommandWriteGuardMarkerTests
{
    [Fact]
    public void AcceptTenantInviteCommand_Should_Implement_IgnoreTenantWriteSubscriptionGuard()
    {
        typeof(IIgnoreTenantWriteSubscriptionGuard)
            .IsAssignableFrom(typeof(AcceptTenantInviteCommand))
            .Should()
            .BeTrue("AcceptTenantInviteCommand subscription write-guard'a takılmamalıdır");
    }

    [Fact]
    public void SignupAndAcceptTenantInviteCommand_Should_Implement_IgnoreTenantWriteSubscriptionGuard()
    {
        typeof(IIgnoreTenantWriteSubscriptionGuard)
            .IsAssignableFrom(typeof(SignupAndAcceptTenantInviteCommand))
            .Should()
            .BeTrue("SignupAndAcceptTenantInviteCommand subscription write-guard'a takılmamalıdır");
    }
}

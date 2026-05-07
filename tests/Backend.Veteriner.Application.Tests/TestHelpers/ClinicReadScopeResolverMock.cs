using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Domain.Shared;
using Moq;

namespace Backend.Veteriner.Application.Tests.TestHelpers;

/// <summary>
/// <see cref="IClinicReadScopeResolver"/> için varsayılan davranışları sağlayan test yardımcısı.
/// </summary>
internal static class ClinicReadScopeResolverMock
{
    /// <summary>
    /// Varsayılan Admin/Owner davranışı: <c>requestClinicId</c> ne ise <see cref="ClinicReadScope.SingleClinicId"/>
    /// olarak geri döndürür (yokluğu da koruyup tenant-wide olmasını sağlar).
    /// Mevcut "ClinicNotInTenant" gibi testlerde bu varsayılan üzerine
    /// <see cref="SetupNotFound"/> / <see cref="SetupAccessDenied"/> ile geçersiz kılınabilir.
    /// </summary>
    public static Mock<IClinicReadScopeResolver> Default()
    {
        var mock = new Mock<IClinicReadScopeResolver>();
        mock
            .Setup(x => x.ResolveAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, Guid? cid, CancellationToken __) =>
                Result<ClinicReadScope>.Success(new ClinicReadScope(cid, null)));
        return mock;
    }

    /// <summary>
    /// ClinicAdmin senaryosu: erişilebilir klinik kümesi sabit kalır; <c>requestClinicId</c> bu kümede ise
    /// <see cref="ClinicReadScope.SingleClinicId"/>, değilse <c>Clinics.AccessDenied</c>; null ise <see cref="ClinicReadScope.AccessibleClinicIds"/> seti döner.
    /// </summary>
    public static Mock<IClinicReadScopeResolver> ForClinicAdmin(IReadOnlyCollection<Guid> accessibleClinicIds)
    {
        var mock = new Mock<IClinicReadScopeResolver>();
        mock
            .Setup(x => x.ResolveAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, Guid? cid, CancellationToken __) =>
            {
                if (cid.HasValue)
                {
                    if (!accessibleClinicIds.Contains(cid.Value))
                    {
                        return Result<ClinicReadScope>.Failure(
                            "Clinics.AccessDenied",
                            "Bu klinik için atanmış üyeliğiniz yok.");
                    }
                    return Result<ClinicReadScope>.Success(new ClinicReadScope(cid.Value, null));
                }
                return Result<ClinicReadScope>.Success(new ClinicReadScope(null, accessibleClinicIds));
            });
        return mock;
    }

    /// <summary>Tenant'a ait olmayan klinik için resolver Clinics.NotFound döndürmeli.</summary>
    public static void SetupNotFound(this Mock<IClinicReadScopeResolver> mock)
    {
        mock
            .Setup(x => x.ResolveAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ClinicReadScope>.Failure(
                "Clinics.NotFound",
                "Klinik bulunamadı veya kiracıya ait değil."));
    }

    /// <summary>Atanmamış klinik için ClinicAdmin senaryosunda Clinics.AccessDenied döndürmeli.</summary>
    public static void SetupAccessDenied(this Mock<IClinicReadScopeResolver> mock)
    {
        mock
            .Setup(x => x.ResolveAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ClinicReadScope>.Failure(
                "Clinics.AccessDenied",
                "Bu klinik için atanmış üyeliğiniz yok."));
    }
}

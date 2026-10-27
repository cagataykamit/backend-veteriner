using Backend.Veteriner.Domain.Products;
using AwesomeAssertions;

namespace Backend.Veteriner.Domain.Tests.Products;

public sealed class ProductServiceKindTests
{
    private static readonly DateTime Now = new(2026, 10, 27, 9, 0, 0, DateTimeKind.Utc);

    private static readonly Guid TenantId = Guid.NewGuid();

    [Theory]
    [InlineData(ProductKind.Service, false)]
    [InlineData(ProductKind.Other, true)]
    [InlineData(ProductKind.Medicine, true)]
    [InlineData(ProductKind.Vaccine, true)]
    public void IsStockTracked_Should_Be_False_Only_For_Service(ProductKind kind, bool expected)
        => new Product(TenantId, "Kalem", "Adet", 100m, "TRY", Now, kind: kind).IsStockTracked.Should().Be(expected);

    [Fact]
    public void Service_Should_Not_Be_Controlled()
    {
        var create = () => new Product(TenantId, "Muayene", "Adet", 500m, "TRY",
            Now,
            kind: ProductKind.Service, controlledClass: ControlledSubstanceClass.Narcotic);

        create.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Update_Should_Not_Make_Service_Controlled()
    {
        var service = new Product(TenantId, "Muayene", "Adet", 500m, "TRY", Now, kind: ProductKind.Service);

        var update = () => service.Update(null, "Muayene", "Adet", 600m, "TRY",
            Now,
            kind: ProductKind.Service, controlledClass: ControlledSubstanceClass.Narcotic);

        update.Should().Throw<ArgumentException>();
    }
}

using FluentAssertions;
using Microsoft.Extensions.Configuration;

namespace Backend.Veteriner.Application.Tests.Projections.Payments;

public sealed class PaymentProjectionOptionsTests
{
    private const string SectionName = "PaymentProjection";

    [Fact]
    public void ConfigurationBinding_Should_DefaultEnabledToFalse()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{SectionName}:Enabled"] = "false",
                [$"{SectionName}:ConsumerName"] = "payment-finance-v1",
                [$"{SectionName}:ClaimingEnabled"] = "false"
            })
            .Build();

        var section = config.GetSection(SectionName);

        section["Enabled"].Should().Be("false", "PaymentProjection:Enabled production default false olmalı");
        section["ClaimingEnabled"].Should().Be("false");
        section["ConsumerName"].Should().Be("payment-finance-v1");
    }
}

using FamilyTrustFund.Infrastructure.Payments;
using FluentAssertions;

namespace FamilyTrustFund.Tests.Payments;

public class PaystackPaymentProviderTests
{
    [Theory]
    [InlineData(100.00, 10000)]
    [InlineData(1, 100)]
    [InlineData(0, 0)]
    [InlineData(1000.50, 100050)]
    public void ToMinorUnits_converts_ngn_to_kobo(decimal ngn, long expectedKobo) =>
        PaystackPaymentProvider.ToMinorUnits(ngn).Should().Be(expectedKobo);

    [Fact]
    public void ToMinorUnits_rejects_negative_amount()
    {
        var act = () => PaystackPaymentProvider.ToMinorUnits(-1);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}

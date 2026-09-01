using FamilyTrustFund.Domain.Payments;
using FluentAssertions;

namespace FamilyTrustFund.Tests.Payments;

public class PaymentRecipientTests
{
    private static readonly Guid MemberId = Guid.NewGuid();

    [Fact]
    public void Create_sets_pending_unverified_state()
    {
        var r = PaymentRecipient.Create(MemberId, "Paystack", "058", "GTBank", "0123456789", "Ada Obi");

        r.MemberId.Should().Be(MemberId);
        r.Provider.Should().Be("Paystack");
        r.BankCode.Should().Be("058");
        r.BankName.Should().Be("GTBank");
        r.AccountNumber.Should().Be("0123456789");
        r.AccountName.Should().Be("Ada Obi");
        r.Status.Should().Be(PaymentRecipientStatus.Unverified);
        r.IsActive.Should().BeFalse();
        r.ProviderRecipientCode.Should().BeNull();
    }

    [Fact]
    public void Create_with_blank_bank_name_uses_bank_code()
    {
        var r = PaymentRecipient.Create(MemberId, "Paystack", "058", "  ", "0123456789", "Ada Obi");
        r.BankName.Should().Be("058");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("123")]
    public void Create_rejects_invalid_account_number(string accountNumber)
    {
        var act = () => PaymentRecipient.Create(MemberId, "Paystack", "058", "GTBank", accountNumber, "Ada Obi");
        act.Should().Throw<InvalidPaymentException>();
    }

    [Fact]
    public void Create_rejects_missing_account_name()
    {
        var act = () => PaymentRecipient.Create(MemberId, "Paystack", "058", "GTBank", "0123456789", "  ");
        act.Should().Throw<InvalidPaymentException>();
    }

    [Fact]
    public void VerifyAndActivate_sets_recipient_code_and_active_state()
    {
        var r = PaymentRecipient.Create(MemberId, "Paystack", "058", "GTBank", "0123456789", "Ada Obi");

        r.VerifyAndActivate("RCP_123");

        r.ProviderRecipientCode.Should().Be("RCP_123");
        r.Status.Should().Be(PaymentRecipientStatus.Active);
        r.IsActive.Should().BeTrue();
    }

    [Fact]
    public void VerifyAndActivate_rejects_empty_recipient_code()
    {
        var r = PaymentRecipient.Create(MemberId, "Paystack", "058", "GTBank", "0123456789", "Ada Obi");
        var act = () => r.VerifyAndActivate("  ");
        act.Should().Throw<InvalidPaymentException>();
    }

    [Fact]
    public void Disable_marks_recipient_inactive()
    {
        var r = PaymentRecipient.Create(MemberId, "Paystack", "058", "GTBank", "0123456789", "Ada Obi");
        r.VerifyAndActivate("RCP_123");

        r.Disable();

        r.Status.Should().Be(PaymentRecipientStatus.Disabled);
        r.IsActive.Should().BeFalse();
    }
}

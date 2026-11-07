using FamilyTrustFund.Domain.Auth;
using FluentAssertions;

namespace FamilyTrustFund.Tests.Auth;

public class NewAccountRoleTests
{
    [Fact]
    public void Member_signup_receives_the_member_role()
    {
        AppRoles.RolesForNewAccount(AppRoles.Member).Should().Equal(AppRoles.Member);
    }

    [Fact]
    public void Guarantor_signup_receives_the_guarantor_role()
    {
        AppRoles.RolesForNewAccount(AppRoles.Guarantor).Should().Equal(AppRoles.Guarantor);
    }

    [Fact]
    public void Every_new_account_receives_at_least_one_role()
    {
        foreach (var requested in AppRoles.All)
        {
            AppRoles.RolesForNewAccount(requested).Should().NotBeEmpty();
        }
    }
}

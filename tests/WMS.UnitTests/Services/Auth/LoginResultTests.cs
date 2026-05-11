using WMS.BLL.Services.Auth;
using WMS.Common.Auth;

namespace WMS.UnitTests.Services.Auth;

// P0 #4 — pin the record shape of LoginResult + its factory methods.
// Records are tiny but the in-flow forced password change flow leans
// on the `RequiresPasswordChange` flag being set ONLY by the
// `RequiresForcedPasswordChange` factory — a controller switching on
// `Success && RequiresPasswordChange` MUST distinguish that case from
// a normal `Succeeded` result.
public class LoginResultTests
{
    private static IReadOnlyList<UserTenantInfo> SampleTenants() =>
        new[]
        {
            new UserTenantInfo(
                TenantId: Guid.Parse("00000000-0000-0000-0000-000000000001"),
                TenantCode: "ACME",
                TenantName: "ACME Corp"),
        };

    [Fact]
    public void Failed_Sets_Success_False_And_Empty_Tenants()
    {
        var r = LoginResult.Failed("InvalidPassword");

        Assert.False(r.Success);
        Assert.Equal("InvalidPassword", r.FailureReason);
        Assert.Null(r.PreAuthToken);
        Assert.Empty(r.Tenants);
        Assert.False(r.RequiresPasswordChange);
    }

    [Fact]
    public void Succeeded_Sets_Success_True_And_Token_And_Tenants()
    {
        var tenants = SampleTenants();

        var r = LoginResult.Succeeded("the-token", tenants);

        Assert.True(r.Success);
        Assert.Null(r.FailureReason);
        Assert.Equal("the-token", r.PreAuthToken);
        Assert.Same(tenants, r.Tenants);
        Assert.False(r.RequiresPasswordChange);
    }

    [Fact]
    public void RequiresForcedPasswordChange_Sets_The_Flag_But_Is_Otherwise_Success()
    {
        var tenants = SampleTenants();

        var r = LoginResult.RequiresForcedPasswordChange("flagged-token", tenants);

        Assert.True(r.Success);
        Assert.Null(r.FailureReason);
        Assert.Equal("flagged-token", r.PreAuthToken);
        Assert.True(r.RequiresPasswordChange);
    }

    [Fact]
    public void Succeeded_And_RequiresForcedPasswordChange_Are_Distinguishable_By_Flag()
    {
        // Defensive — a controller that wants the new flow MUST branch
        // on RequiresPasswordChange, not just Success.
        var tenants = SampleTenants();
        var normal = LoginResult.Succeeded("t1", tenants);
        var forced = LoginResult.RequiresForcedPasswordChange("t2", tenants);

        Assert.Equal(normal.Success, forced.Success);
        Assert.NotEqual(normal.RequiresPasswordChange, forced.RequiresPasswordChange);
    }
}

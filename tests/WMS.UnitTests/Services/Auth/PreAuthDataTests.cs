using WMS.BLL.Services.Auth;

namespace WMS.UnitTests.Services.Auth;

// P0 #4 — pin the PreAuthData record shape. The
// `RequiresPasswordChange` flag is new (T1/T2); SQL projection in
// AuthService.ValidatePreAuthTokenAsync depends on positional binding
// matching the record's ctor order.
public class PreAuthDataTests
{
    [Fact]
    public void Constructor_Preserves_All_Fields()
    {
        var id = Guid.NewGuid();
        var expiresAt = DateTime.UtcNow.AddMinutes(5);

        var data = new PreAuthData(
            Id: id,
            UserEmail: "alice@example.com",
            ExpiresAt: expiresAt,
            IpAddress: "203.0.113.5",
            RequiresPasswordChange: true);

        Assert.Equal(id, data.Id);
        Assert.Equal("alice@example.com", data.UserEmail);
        Assert.Equal(expiresAt, data.ExpiresAt);
        Assert.Equal("203.0.113.5", data.IpAddress);
        Assert.True(data.RequiresPasswordChange);
    }

    [Fact]
    public void RequiresPasswordChange_DefaultsTo_False_NotImplicit()
    {
        // No default — every caller must supply it explicitly so the
        // SQL projection in ValidatePreAuthTokenAsync stays honest.
        var data = new PreAuthData(
            Guid.NewGuid(), "x@x.com", DateTime.UtcNow, IpAddress: null,
            RequiresPasswordChange: false);

        Assert.False(data.RequiresPasswordChange);
    }
}

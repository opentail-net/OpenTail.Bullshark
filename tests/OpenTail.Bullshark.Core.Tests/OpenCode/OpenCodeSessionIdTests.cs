using OpenTail.Bullshark.Core.OpenCode;

namespace OpenTail.Bullshark.Core.Tests.OpenCode;

/// <summary>Stage OC1a (OC-D8): the opencode session id shape, verified at OC0 as <c>ses_</c> + 12 lowercase hex + 14 base62 characters.</summary>
public sealed class OpenCodeSessionIdTests
{
    private const string Valid = "ses_0123456789abAbCdEfGhIjKlMn";

    [Fact]
    public void AnIdOfTheVerifiedShapeIsValid()
    {
        Assert.True(OpenCodeSessionId.IsValid(Valid));
        Assert.Equal(30, Valid.Length);
    }

    [Theory]
    [InlineData("ses_0123456789AB" + "AbCdEfGhIjKlMn")]
    [InlineData("ses_0123456789abAbCdEfGhIjKlM")]
    [InlineData("ses_0123456789abAbCdEfGhIjKlMnO")]
    [InlineData("ses_0123456789aAbCdEfGhIjKlMn")]
    [InlineData("ses_0123456789abAbCdEfGhIjKl")]
    [InlineData("ses_0123456789a")]
    [InlineData("-ses_0123456789abAbCdEfGhIjKlMn")]
    [InlineData("ses_0123456789abAbCdEfGhIjKl Mn")]
    [InlineData(" ses_0123456789abAbCdEfGhIjKlMn")]
    [InlineData("ses_0123456789abAbCdEfGhIjKlMn ")]
    [InlineData("ses_0123456789abAbCdEfGhIjKlM_")]
    [InlineData("ses-0123456789abAbCdEfGhIjKlMn")]
    [InlineData("3f2a9c10-1b2d-4e5f-8a9b-0c1d2e3f4a51")]
    [InlineData("ses_0123456789abAbCdEfGhIjKlMn\n")]
    [InlineData("ses_0123456789abAbCdEfGhIjKlMn\r\n")]
    public void AnIdOfAnyOtherShapeIsNotValid(string id)
    {
        Assert.False(OpenCodeSessionId.IsValid(id));
    }

    [Fact]
    public void NullAndEmptyAreNotValid()
    {
        Assert.False(OpenCodeSessionId.IsValid(null));
        Assert.False(OpenCodeSessionId.IsValid(string.Empty));
    }
}

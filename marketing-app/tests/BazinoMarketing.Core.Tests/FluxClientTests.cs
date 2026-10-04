using BazinoMarketing.Core.Tools;
using Xunit;

namespace BazinoMarketing.Core.Tests;

public class FluxClientTests
{
    [Theory]
    [InlineData("https://api.cloudflare.com/client/v4")]
    [InlineData("https://api.cloudflare.com/client/v4/")]
    [InlineData("https://api.cloudflare.com")]
    [InlineData("https://cloudflare.com/client/v4")]
    public void LooksLikeCloudflareApiRoot_Detects_The_Unusable_Default(string url) =>
        Assert.True(FluxClient.LooksLikeCloudflareApiRoot(url));

    [Theory]
    [InlineData("https://api.cloudflare.com/client/v4/accounts/abc/ai/models/search")]
    [InlineData("https://api.together.xyz/v1/models")]
    [InlineData("https://example.com/health")]
    [InlineData("")]
    [InlineData("not a url")]
    [InlineData(null)]
    public void LooksLikeCloudflareApiRoot_Rejects_Real_Test_Urls(string? url) =>
        Assert.False(FluxClient.LooksLikeCloudflareApiRoot(url));
}

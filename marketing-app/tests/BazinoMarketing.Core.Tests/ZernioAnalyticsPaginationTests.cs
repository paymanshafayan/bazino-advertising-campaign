using System.Text.Json.Nodes;
using BazinoMarketing.Core.Tools;
using Xunit;

namespace BazinoMarketing.Core.Tests;

public class ZernioAnalyticsPaginationTests
{
    [Fact]
    public void CombinesAllPostPagesAndPreservesFirstPageOverview()
    {
        var pages = new[]
        {
            JsonNode.Parse("{\"posts\":[{\"postId\":\"post-1\"}],\"overview\":{\"reach\":23},\"pagination\":{\"page\":1,\"limit\":1,\"total\":2}}")!.AsObject(),
            JsonNode.Parse("{\"posts\":[{\"postId\":\"post-2\"}],\"pagination\":{\"page\":2,\"limit\":1,\"total\":2}}")!.AsObject()
        };

        var result = ZernioClient.CombineInstagramPostAnalyticsPages(pages, 1, complete: true);

        Assert.True(result.Ok);
        Assert.Equal(2, result.Body!["posts"]!.AsArray().Count);
        Assert.Equal("23", result.Body["overview"]!["reach"]!.ToJsonString());
        Assert.Equal(2, result.Body["_bazinoCollection"]!["pagesFetched"]!.GetValue<int>());
        Assert.Equal(2, result.Body["_bazinoCollection"]!["postsCollected"]!.GetValue<int>());
        Assert.True(result.Body["_bazinoCollection"]!["complete"]!.GetValue<bool>());
    }

    [Fact]
    public void IncompletePageCollectionIsNeverReportedAsSuccessful()
    {
        var pages = new[]
        {
            JsonNode.Parse("{\"posts\":[{\"postId\":\"post-1\"}]}")!.AsObject()
        };

        var result = ZernioClient.CombineInstagramPostAnalyticsPages(pages, 100, complete: false);

        Assert.False(result.Ok);
        Assert.False(result.Body!["_bazinoCollection"]!["complete"]!.GetValue<bool>());
    }
}

using System.Net;
using System.Text.Json.Nodes;
using BazinoMarketing.Core.Publishing;
using BazinoMarketing.Core.Settings;
using Xunit;

namespace BazinoMarketing.Core.Tests;

public sealed class BazinoAffiliatePortalClientTests
{
    private const string MediaId = "3400011122233344444";
    private static readonly Uri Portal = new("https://bazino.test");

    [Fact]
    public void ProductionConfigurationRequiresOfficialPortalHostAndSafeToken()
    {
        Assert.Null(BazinoAffiliatePortalClient.ValidateConfiguration(new BazinoPortalSettings
        {
            BaseUrl = "https://bazino.pro"
        }, "safe_token"));
        Assert.NotNull(BazinoAffiliatePortalClient.ValidateConfiguration(new BazinoPortalSettings
        {
            BaseUrl = "https://attacker.example"
        }, "safe_token"));
        Assert.NotNull(BazinoAffiliatePortalClient.ValidateConfiguration(new BazinoPortalSettings
        {
            BaseUrl = "https://bazino.pro/other"
        }, "safe_token"));
        Assert.NotNull(BazinoAffiliatePortalClient.ValidateConfiguration(new BazinoPortalSettings
        {
            BaseUrl = "https://bazino.pro"
        }, "bad\r\ntoken"));
    }

    [Fact]
    public async Task ReportsOnlyRealAffiliateReelFieldsToExistingEndpoint()
    {
        var handler = new RecordingHandler((_, _) => Task.FromResult(JsonResponse(HttpStatusCode.OK,
            "{\"accepted\":true,\"status\":\"needs_review\",\"duplicate\":false}")));
        using var http = new HttpClient(handler);
        var result = await BazinoAffiliatePortalClient.ReportPublishedReelAsync(http, Portal, "baz_test_token",
            MediaId, new DateTimeOffset(2026, 10, 2, 12, 30, 0, TimeSpan.Zero), NoDelay);

        Assert.True(result.Success);
        Assert.Equal("registered", result.Status);
        Assert.Equal("needs_review", result.RegistryStatus);
        Assert.Single(handler.Requests);
        var request = handler.Requests[0];
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://bazino.test/api/integrations/instagram/published-media", request.Uri!.ToString());
        Assert.Equal("Bearer baz_test_token", request.Authorization);
        Assert.Equal("instagram:" + MediaId, request.IdempotencyKey);
        var body = JsonNode.Parse(request.Body)!;
        Assert.Equal(MediaId, body["media_id"]!.GetValue<string>());
        Assert.Equal("reel", body["media_type"]!.GetValue<string>());
        Assert.Equal("2026-10-02T12:30:00.0000000+00:00", body["published_at"]!.GetValue<string>());
        Assert.Null(body["campaign_id"]);
        Assert.Equal(3, body.AsObject().Count);
    }

    [Fact]
    public async Task Duplicate200IsSuccessAnd409IsReportedWithoutRetry()
    {
        var duplicateHandler = new RecordingHandler((_, _) => Task.FromResult(JsonResponse(HttpStatusCode.OK,
            "{\"accepted\":true,\"status\":\"approved\",\"duplicate\":true}")));
        using var duplicateClient = new HttpClient(duplicateHandler);
        var duplicate = await BazinoAffiliatePortalClient.ReportPublishedReelAsync(duplicateClient, Portal, "token",
            MediaId, DateTimeOffset.UtcNow, NoDelay);
        Assert.True(duplicate.Success);
        Assert.True(duplicate.Duplicate);
        Assert.Equal("duplicate", duplicate.Status);
        Assert.Single(duplicateHandler.Requests);

        var conflictHandler = new RecordingHandler((_, _) => Task.FromResult(JsonResponse(HttpStatusCode.Conflict, "{\"error\":\"CONFLICTING_MEDIA\"}")));
        using var conflictClient = new HttpClient(conflictHandler);
        var conflict = await BazinoAffiliatePortalClient.ReportPublishedReelAsync(conflictClient, Portal, "token",
            MediaId, DateTimeOffset.UtcNow, NoDelay);
        Assert.False(conflict.Success);
        Assert.Equal("conflict", conflict.Status);
        Assert.Single(conflictHandler.Requests);
    }

    [Fact]
    public async Task UnauthorizedStopsImmediatelyAndNeverEchoesToken()
    {
        const string secret = "baz_private_secret";
        var handler = new RecordingHandler((_, _) => Task.FromResult(JsonResponse(HttpStatusCode.Unauthorized, "{\"error\":\"unauthorized\"}")));
        using var http = new HttpClient(handler);
        var result = await BazinoAffiliatePortalClient.ReportPublishedReelAsync(http, Portal, secret,
            MediaId, DateTimeOffset.UtcNow, NoDelay);
        Assert.False(result.Success);
        Assert.Equal("unauthorized", result.Status);
        Assert.Equal(1, result.Attempts);
        Assert.Single(handler.Requests);
        Assert.DoesNotContain(secret, result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OtherHttpErrorsRetryThreeTimesWithSameIdempotencyKey()
    {
        var handler = new RecordingHandler((_, _) => Task.FromResult(JsonResponse(HttpStatusCode.ServiceUnavailable, "{\"error\":\"temporarily_unavailable\"}")));
        using var http = new HttpClient(handler);
        var delays = new List<TimeSpan>();
        var result = await BazinoAffiliatePortalClient.ReportPublishedReelAsync(http, Portal, "token",
            MediaId, DateTimeOffset.UtcNow, (duration, _) => { delays.Add(duration); return Task.CompletedTask; });
        Assert.False(result.Success);
        Assert.Equal(3, result.Attempts);
        Assert.Equal(3, handler.Requests.Count);
        Assert.Equal(2, delays.Count);
        Assert.All(handler.Requests, request => Assert.Equal("instagram:" + MediaId, request.IdempotencyKey));
    }

    [Fact]
    public async Task InvalidMediaIdsFailClosedBeforeNetworkRequest()
    {
        var handler = new RecordingHandler((_, _) => Task.FromResult(JsonResponse(HttpStatusCode.OK,
            "{\"accepted\":true,\"status\":\"approved\",\"duplicate\":false}")));
        using var http = new HttpClient(handler);
        var result = await BazinoAffiliatePortalClient.ReportPublishedReelAsync(http, Portal, "token",
            "https://instagram.com/reel/not-an-id", DateTimeOffset.UtcNow, NoDelay);
        Assert.False(result.Success);
        Assert.Equal("blocked", result.Status);
        Assert.Empty(handler.Requests);
    }

    private static Task NoDelay(TimeSpan _, CancellationToken __) => Task.CompletedTask;

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, string body) => new(status)
    {
        Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
    };

    private sealed class RecordingHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public List<CapturedRequest> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new CapturedRequest(request.Method, request.RequestUri, request.Headers.Authorization?.ToString(),
                request.Headers.TryGetValues("Idempotency-Key", out var values) ? values.Single() : null, body));
            return await respond(request, cancellationToken);
        }
    }

    private sealed record CapturedRequest(HttpMethod Method, Uri? Uri, string? Authorization, string? IdempotencyKey, string Body);
}

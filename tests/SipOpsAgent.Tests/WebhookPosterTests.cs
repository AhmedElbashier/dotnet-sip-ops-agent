using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using SipOpsAgent.Options;
using SipOpsAgent.Tests.TestSupport;
using SipOpsAgent.Webhooks;

namespace SipOpsAgent.Tests;

public class WebhookPosterTests
{
    [Fact]
    public void Sign_and_verify_round_trip()
    {
        const string secret = "test-secret";
        const string timestamp = "1700000000";
        const string body = """{"source":"sip-ops-agent"}""";
        var signature = WebhookPoster.Sign(secret, timestamp, body);
        Assert.Equal(64, signature.Length);
        Assert.True(WebhookPoster.Verify(secret, timestamp, body, signature));
        Assert.False(WebhookPoster.Verify(secret, timestamp, body, "0" + signature[1..]));
        Assert.False(WebhookPoster.Verify("other", timestamp, body, signature));
    }

    [Fact]
    public async Task Post_sends_hmac_headers_and_records_the_delivery()
    {
        const string secret = "test-secret";
        RecordingHandler? captured = null;
        captured = new RecordingHandler((request, body) =>
        {
            Assert.True(request.Headers.TryGetValues(WebhookPoster.TimestampHeader, out var ts));
            Assert.True(request.Headers.TryGetValues(WebhookPoster.SignatureHeader, out var sig));
            Assert.True(WebhookPoster.Verify(secret, ts.Single(), body, sig.Single()));
            Assert.Contains("\"title\":\"Trunk alert\"", body, StringComparison.Ordinal);
            return new HttpResponseMessage(HttpStatusCode.Accepted) { Content = new StringContent("""{"ok":true}""") };
        });

        var sink = new WebhookSink();
        var poster = new WebhookPoster(
            new HttpClient(captured) { BaseAddress = new Uri("http://webhook.test/") },
            Microsoft.Extensions.Options.Options.Create(new WebhookOptions { Url = "http://webhook.test/hooks/ops", HmacSecret = secret }),
            sink);

        var delivery = await poster.PostAsync("Trunk alert", "critical", "12 x 503", ["cdr-20261124-0001"]);
        Assert.Equal(202, delivery.StatusCode);
        Assert.False(delivery.DryRun);
        Assert.Null(delivery.Error);
        Assert.Single(sink.List());
        Assert.Equal(1, captured.Calls);
    }

    [Fact]
    public async Task Post_without_a_url_is_a_dry_run()
    {
        var sink = new WebhookSink();
        var poster = new WebhookPoster(
            new HttpClient(),
            Microsoft.Extensions.Options.Options.Create(new WebhookOptions { Url = "", HmacSecret = "x" }),
            sink);
        var delivery = await poster.PostAsync("Dry", "info", "nothing sent", []);
        Assert.True(delivery.DryRun);
        Assert.Null(delivery.StatusCode);
        Assert.Single(sink.List());
    }

    [Fact]
    public async Task Non_success_status_is_recorded_as_an_error()
    {
        var handler = new RecordingHandler((_, _) => new HttpResponseMessage(HttpStatusCode.BadGateway));
        var poster = new WebhookPoster(
            new HttpClient(handler),
            Microsoft.Extensions.Options.Options.Create(new WebhookOptions { Url = "http://webhook.test/hooks/ops" }),
            new WebhookSink());
        var delivery = await poster.PostAsync("Down", "warning", "sink 502", []);
        Assert.Equal(502, delivery.StatusCode);
        Assert.Equal("HTTP 502", delivery.Error);
    }

    [Theory]
    [InlineData("high", "critical")]
    [InlineData("WARN", "warning")]
    [InlineData("nope", "info")]
    public void Severity_is_normalized(string input, string expected) =>
        Assert.Equal(expected, WebhookPoster.NormalizeSeverity(input));
}

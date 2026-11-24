using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SipOpsAgent.Llm;
using SipOpsAgent.Options;

namespace SipOpsAgent.Webhooks;

public sealed class WebhookDelivery
{
    public required string Id { get; init; }
    public required DateTimeOffset GeneratedAt { get; init; }
    public required string Title { get; init; }
    public required string Severity { get; init; }
    public required string Summary { get; init; }
    public required IReadOnlyList<string> CallIds { get; init; }
    public string? Destination { get; init; }
    public int? StatusCode { get; init; }
    public bool DryRun { get; init; }
    public string? Error { get; init; }
    public string? Signature { get; init; }
    public string? Timestamp { get; init; }
}

public sealed class WebhookSink
{
    private readonly List<WebhookDelivery> _items = new();
    private readonly object _gate = new();

    public void Add(WebhookDelivery delivery)
    {
        lock (_gate)
        {
            _items.Insert(0, delivery);
        }
    }

    public IReadOnlyList<WebhookDelivery> List()
    {
        lock (_gate)
        {
            return _items.ToList();
        }
    }
}

public sealed class WebhookPoster
{
    public const string SignatureHeader = "X-Agent-Signature";
    public const string TimestampHeader = "X-Agent-Timestamp";

    private readonly HttpClient _http;
    private readonly WebhookOptions _options;
    private readonly WebhookSink _sink;

    public WebhookPoster(HttpClient http, IOptions<WebhookOptions> options, WebhookSink sink)
    {
        _http = http;
        _options = options.Value;
        _sink = sink;
        _http.Timeout = TimeSpan.FromSeconds(Math.Max(2, _options.TimeoutSeconds));
    }

    public async Task<WebhookDelivery> PostAsync(string title, string severity, string summary, IReadOnlyList<string> callIds, CancellationToken cancellationToken = default)
    {
        var generatedAt = DateTimeOffset.UtcNow;
        var id = $"wh-{generatedAt.ToUnixTimeMilliseconds()}-{Random.Shared.Next(1000, 9999)}";
        var payload = new
        {
            source = "sip-ops-agent",
            id,
            title,
            severity = NormalizeSeverity(severity),
            summary,
            callIds,
            generatedAt
        };
        var body = JsonSerializer.Serialize(payload, OpenAiCompatibleChatClient.JsonOptions);
        var timestamp = generatedAt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var signature = string.IsNullOrWhiteSpace(_options.HmacSecret)
            ? null
            : Sign(_options.HmacSecret, timestamp, body);

        if (string.IsNullOrWhiteSpace(_options.Url))
        {
            var dry = new WebhookDelivery
            {
                Id = id,
                GeneratedAt = generatedAt,
                Title = title,
                Severity = payload.severity,
                Summary = summary,
                CallIds = callIds,
                DryRun = true,
                Signature = signature,
                Timestamp = timestamp
            };
            _sink.Add(dry);
            return dry;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, _options.Url)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        request.Headers.TryAddWithoutValidation(TimestampHeader, timestamp);
        if (signature is not null)
        {
            request.Headers.TryAddWithoutValidation(SignatureHeader, signature);
        }

        try
        {
            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var delivery = new WebhookDelivery
            {
                Id = id,
                GeneratedAt = generatedAt,
                Title = title,
                Severity = payload.severity,
                Summary = summary,
                CallIds = callIds,
                Destination = _options.Url,
                StatusCode = (int)response.StatusCode,
                DryRun = false,
                Error = response.IsSuccessStatusCode ? null : $"HTTP {(int)response.StatusCode}",
                Signature = signature,
                Timestamp = timestamp
            };
            _sink.Add(delivery);
            return delivery;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            var failed = new WebhookDelivery
            {
                Id = id,
                GeneratedAt = generatedAt,
                Title = title,
                Severity = payload.severity,
                Summary = summary,
                CallIds = callIds,
                Destination = _options.Url,
                DryRun = false,
                Error = ex.Message,
                Signature = signature,
                Timestamp = timestamp
            };
            _sink.Add(failed);
            return failed;
        }
    }

    public static string Sign(string secret, string timestamp, string body)
    {
        var bytes = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"{timestamp}.{body}"));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static bool Verify(string secret, string timestamp, string body, string signature)
    {
        if (string.IsNullOrWhiteSpace(signature))
        {
            return false;
        }

        var expected = Encoding.UTF8.GetBytes(Sign(secret, timestamp, body));
        var actual = Encoding.UTF8.GetBytes(signature.Trim().ToLowerInvariant());
        return expected.Length == actual.Length && CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    internal static string NormalizeSeverity(string? severity) =>
        severity?.Trim().ToLowerInvariant() switch
        {
            "critical" or "error" or "high" => "critical",
            "warning" or "warn" or "medium" => "warning",
            _ => "info"
        };
}

using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace HelloDoctor.Api.Services;

public record IceServerDto(
    [property: JsonPropertyName("urls")] string[] Urls,
    [property: JsonPropertyName("username")] string? Username,
    [property: JsonPropertyName("credential")] string? Credential);

public record IceConfigDto(
    [property: JsonPropertyName("iceServers")] IceServerDto[] IceServers,
    [property: JsonPropertyName("iceTransportPolicy")] string IceTransportPolicy,
    [property: JsonPropertyName("hasTurn")] bool HasTurn,
    [property: JsonPropertyName("expiresAt")] string ExpiresAt);

// TURN kimlik bilgilerini istemciye çalışma anında verir.
//
// Eskiden bunlar VITE_TURN_* olarak derlenmiş pakete gömülüyordu: süresi
// dolduğunda dağıtılmış paket bozuluyor, üstelik kimlik bilgisi uygulamayı
// açan herkese görünüyordu. Artık API anahtarı sunucuda kalıyor ve istemci
// yalnızca süreli kimlik alıyor.
public class IceServerProvider
{
    private readonly IceOptions _options;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<IceServerProvider> _logger;

    private readonly SemaphoreSlim _lock = new(1, 1);
    private IceConfigDto? _cached;
    private DateTime _cachedUntil = DateTime.MinValue;

    public IceServerProvider(IOptions<IceOptions> options, IHttpClientFactory httpFactory,
        ILogger<IceServerProvider> logger)
    {
        _options = options.Value;
        _httpFactory = httpFactory;
        _logger = logger;
    }

    public async Task<IceConfigDto> GetAsync(CancellationToken ct = default)
    {
        if (_cached is not null && DateTime.UtcNow < _cachedUntil)
            return _cached;

        await _lock.WaitAsync(ct);
        try
        {
            // Beklerken başka bir istek tazelemiş olabilir.
            if (_cached is not null && DateTime.UtcNow < _cachedUntil)
                return _cached;

            var config = await BuildAsync(ct);
            _cached = config;
            _cachedUntil = DateTime.UtcNow.AddMinutes(_options.CacheMinutes);
            return config;
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<IceConfigDto> BuildAsync(CancellationToken ct)
    {
        var servers = new List<IceServerDto>();
        if (_options.StunUrls.Length > 0)
            servers.Add(new IceServerDto(_options.StunUrls, null, null));

        var turn = _options.UsesMetered
            ? await FetchMeteredAsync(ct)
            : StaticTurn();

        if (turn is not null) servers.Add(turn);
        else if (_options.UsesMetered)
            // Metered'a ulaşılamadı; STUN'la devam ediyoruz ama doğrudan
            // bağlanamayan kullanıcılar görüşemeyecek.
            _logger.LogWarning("TURN kimlik bilgisi alınamadı, yalnızca STUN sunuluyor.");

        var expiresAt = DateTime.UtcNow.AddMinutes(_options.CacheMinutes);
        return new IceConfigDto(
            [.. servers],
            _options.IceTransportPolicy == "relay" ? "relay" : "all",
            turn is not null,
            expiresAt.ToString("o"));
    }

    private IceServerDto? StaticTurn() =>
        _options.UsesStaticTurn
            ? new IceServerDto(_options.TurnUrls, _options.TurnUsername, _options.TurnCredential)
            : null;

    private async Task<IceServerDto?> FetchMeteredAsync(CancellationToken ct)
    {
        var url = $"https://{_options.MeteredSubdomain}.metered.live/api/v1/turn/credentials" +
                  $"?apiKey={Uri.EscapeDataString(_options.MeteredApiKey!)}";

        try
        {
            var client = _httpFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(10);

            using var res = await client.GetAsync(url, ct);
            if (!res.IsSuccessStatusCode)
            {
                _logger.LogWarning("Metered {Status} döndü.", (int)res.StatusCode);
                return null;
            }

            var entries = await res.Content.ReadFromJsonAsync<MeteredEntry[]>(cancellationToken: ct);
            var turn = entries?
                .Where(e => e.Urls is not null && (e.Urls.StartsWith("turn:") || e.Urls.StartsWith("turns:")))
                .ToArray();

            if (turn is null || turn.Length == 0) return null;

            // Tüm adresler aynı kimlik bilgisini taşır; ilkini kullanıyoruz.
            return new IceServerDto(
                turn.Select(t => t.Urls!).ToArray(), turn[0].Username, turn[0].Credential);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Metered kimlik bilgisi çekilemedi.");
            return null;
        }
    }

    private sealed record MeteredEntry(
        [property: JsonPropertyName("urls")] string? Urls,
        [property: JsonPropertyName("username")] string? Username,
        [property: JsonPropertyName("credential")] string? Credential);
}

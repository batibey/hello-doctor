using System.Collections.Concurrent;
using StackExchange.Redis;

namespace HelloDoctor.Api.Services;

// CallHub'ın süreç dışında tutulması gereken iki durumu var:
//
//   1. Varlık (presence): kullanıcı çevrimiçi mi? CallUser buna bakıp
//      ulaşılamayan hedefte hemen "çevrimdışı" diyebiliyor.
//   2. Eşleşme: kim kiminle görüşüyor? Sinyal mesajları yalnızca eşleşmiş
//      taraflar arasında iletiliyor.
//
// Bağlantı listesi burada YOK: onu SignalR kendisi tutuyor ve Clients.User()
// ile erişiliyor. Backplane devredeyse bu örnekler arasında da çalışır.
public interface ICallStateStore
{
    Task AddConnectionAsync(string userId, string connectionId);

    // Kullanıcının başka bağlantısı kalmadıysa true.
    Task<bool> RemoveConnectionAsync(string userId, string connectionId);

    Task<bool> IsOnlineAsync(string userId);

    // Hedefi atomik olarak sahiplenir. Dönen değer hedefin mevcut eşi:
    // callerId ise sahiplenme başarılı, başkasıysa hedef meşgul.
    Task<string> ClaimPeerAsync(string targetId, string callerId);

    // Arayanın kendi kaydını yazar. Bayat kayıt varsa üzerine yazılır, yoksa
    // tarayıcı çöktüğünde sonraki aramalar kalıcı olarak engellenirdi.
    Task SetPeerAsync(string userId, string peerId);

    Task<string?> GetPeerAsync(string userId);

    // Yalnızca beklenen değer eşleşiyorsa siler: araya yeni bir görüşme
    // girmişse eski taraftan gecikmeli gelen EndCall onu bozmaz.
    Task ClearPairAsync(string a, string b);
}

// Tek örnek için. Redis yapılandırılmadığında kullanılır — geliştirme ve
// tek sunuculu kurulumlar ek altyapı gerektirmesin.
public class InMemoryCallStateStore : ICallStateStore
{
    private static readonly ConcurrentDictionary<string, HashSet<string>> Connections = new();
    private static readonly ConcurrentDictionary<string, string> CallPeers = new();

    public Task AddConnectionAsync(string userId, string connectionId)
    {
        var set = Connections.GetOrAdd(userId, _ => new HashSet<string>());
        lock (set) set.Add(connectionId);
        return Task.CompletedTask;
    }

    public Task<bool> RemoveConnectionAsync(string userId, string connectionId)
    {
        if (!Connections.TryGetValue(userId, out var set)) return Task.FromResult(true);
        lock (set)
        {
            set.Remove(connectionId);
            return Task.FromResult(set.Count == 0);
        }
    }

    public Task<bool> IsOnlineAsync(string userId)
    {
        if (!Connections.TryGetValue(userId, out var set)) return Task.FromResult(false);
        lock (set) return Task.FromResult(set.Count > 0);
    }

    public Task<string> ClaimPeerAsync(string targetId, string callerId) =>
        Task.FromResult(CallPeers.GetOrAdd(targetId, callerId));

    public Task SetPeerAsync(string userId, string peerId)
    {
        CallPeers[userId] = peerId;
        return Task.CompletedTask;
    }

    public Task<string?> GetPeerAsync(string userId) =>
        Task.FromResult(CallPeers.TryGetValue(userId, out var peer) ? peer : null);

    public Task ClearPairAsync(string a, string b)
    {
        CallPeers.TryRemove(new KeyValuePair<string, string>(a, b));
        CallPeers.TryRemove(new KeyValuePair<string, string>(b, a));
        return Task.CompletedTask;
    }
}

// Birden fazla örnek için. SignalR backplane'i mesajları dağıtır; varlık ve
// eşleşme durumu da burada paylaşılır.
public class RedisCallStateStore : ICallStateStore
{
    private readonly IConnectionMultiplexer _redis;

    // Süreç çökerse SREM hiç çalışmaz ve kullanıcı sonsuza dek çevrimiçi
    // görünürdü. TTL her bağlantıda tazeleniyor.
    private static readonly TimeSpan PresenceTtl = TimeSpan.FromHours(12);
    private static readonly TimeSpan PairTtl = TimeSpan.FromHours(2);

    // Beklenen değer eşleşirse sil. Redis'te tek adımda karşılığı yok.
    private const string CompareDeleteScript =
        "if redis.call('GET', KEYS[1]) == ARGV[1] then return redis.call('DEL', KEYS[1]) else return 0 end";

    public RedisCallStateStore(IConnectionMultiplexer redis) => _redis = redis;

    private IDatabase Db => _redis.GetDatabase();
    private static RedisKey Presence(string userId) => $"hd:presence:{userId}";
    private static RedisKey Peer(string userId) => $"hd:callpeer:{userId}";

    public async Task AddConnectionAsync(string userId, string connectionId)
    {
        await Db.SetAddAsync(Presence(userId), connectionId);
        await Db.KeyExpireAsync(Presence(userId), PresenceTtl);
    }

    public async Task<bool> RemoveConnectionAsync(string userId, string connectionId)
    {
        await Db.SetRemoveAsync(Presence(userId), connectionId);
        return await Db.SetLengthAsync(Presence(userId)) == 0;
    }

    public async Task<bool> IsOnlineAsync(string userId) =>
        await Db.SetLengthAsync(Presence(userId)) > 0;

    public async Task<string> ClaimPeerAsync(string targetId, string callerId)
    {
        // SET NX: yalnızca anahtar yoksa yazar. GetOrAdd'in atomik karşılığı.
        if (await Db.StringSetAsync(Peer(targetId), callerId, PairTtl, When.NotExists))
            return callerId;

        return await Db.StringGetAsync(Peer(targetId)) is { HasValue: true } existing
            ? existing.ToString()
            : callerId;
    }

    public Task SetPeerAsync(string userId, string peerId) =>
        Db.StringSetAsync(Peer(userId), peerId, PairTtl);

    public async Task<string?> GetPeerAsync(string userId) =>
        await Db.StringGetAsync(Peer(userId)) is { HasValue: true } v ? v.ToString() : null;

    public async Task ClearPairAsync(string a, string b)
    {
        await Db.ScriptEvaluateAsync(CompareDeleteScript, [Peer(a)], [b]);
        await Db.ScriptEvaluateAsync(CompareDeleteScript, [Peer(b)], [a]);
    }
}

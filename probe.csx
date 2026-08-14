using System.Net;
using System.Net.Http;
using Launcher.Core.Download;
using Launcher.Core.Utils;

var handler = new Stub();
handler.Route("resources.download.minecraft.net/ab/abcdef", 500, []);
handler.Route("bmclapi2.bangbang93.com/ab/abcdef", 200, "12345"u8.ToArray());
var resolver = new ResolvingDlSourceMapper(new DefaultDlSourceMapper(), new BmclapiDlSourceMapper());
var svc = new DownloadService(new HttpClient(handler), resolver, new DownloadOptions {
    MaxSourceAttempts = 2, BackoffProvider = _ => TimeSpan.Zero,
}, Path.GetTempPath(), (_, _) => Task.FromResult(true));
var dest = Path.Combine(Path.GetTempPath(), $"probe-{Guid.NewGuid():N}.jar");
try {
    await svc.DownloadFileAsync("https://resources.download.minecraft.net/ab/abcdef", dest, null, 5, null, CancellationToken.None);
    Console.WriteLine("OK len=" + new FileInfo(dest).Length);
} catch (Exception ex) { Console.WriteLine("THROW: " + ex.GetType().Name + ": " + ex.Message); }
Console.WriteLine("REQUESTS: " + string.Join(" | ", handler.Requests));

class Stub : HttpMessageHandler
{
    public readonly List<string> Requests = [];
    readonly Dictionary<string, (int Status, byte[] Body)> _routes = [];
    public void Route(string k, int s, byte[] b) => _routes[k] = (s, b);
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
    {
        var key = $"{r.RequestUri!.Host}{r.RequestUri.AbsolutePath}";
        Requests.Add($"{r.Method} {key}");
        if (_routes.TryGetValue(key, out var rt))
            return Task.FromResult(rt.Status == 200
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(rt.Body) }
                : new HttpResponseMessage((HttpStatusCode)rt.Status));
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent("12345"u8.ToArray()) });
    }
}

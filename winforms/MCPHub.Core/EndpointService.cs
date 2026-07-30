using System.Diagnostics;

namespace MCPHub.Core;

public sealed class EndpointService(HttpClient client)
{
    public async Task<EndpointReachability> CheckAsync(string url, CancellationToken cancellationToken = default)
    {
        var watch = Stopwatch.StartNew();
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("ngrok-skip-browser-warning", "true");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            var status = (int)response.StatusCode;
            return new(status is >= 200 and < 500, status, watch.ElapsedMilliseconds, url, "Endpoint responded");
        }
        catch (Exception error) { return new(false, null, watch.ElapsedMilliseconds, url, error.Message); }
    }
}

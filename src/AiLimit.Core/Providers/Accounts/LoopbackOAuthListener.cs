using System.Net;

namespace AiLimit.Core.Providers.Accounts;

public sealed record LoopbackCallback(string Code, string State);

public sealed class OAuthCallbackException : Exception
{
    public OAuthCallbackException(string message) : base(message) { }
}

public interface ILoopbackOAuthListener : IDisposable
{
    string RedirectUri { get; }
    Task<LoopbackCallback> WaitForCallbackAsync(CancellationToken cancellationToken);
}

public sealed class LoopbackOAuthListener : ILoopbackOAuthListener
{
    private readonly HttpListener _http = new();
    private readonly int _port;

    public LoopbackOAuthListener()
    {
        _port = FreePort();
        RedirectUri = $"http://127.0.0.1:{_port}/oauth-callback";
        _http.Prefixes.Add($"http://127.0.0.1:{_port}/");
        _http.Start();
    }

    public string RedirectUri { get; }

    public async Task<LoopbackCallback> WaitForCallbackAsync(CancellationToken cancellationToken)
    {
        using var reg = cancellationToken.Register(() => { try { _http.Stop(); } catch { } });

        while (true)
        {
            HttpListenerContext ctx;
            try
            {
                ctx = await _http.GetContextAsync().ConfigureAwait(false);
            }
            catch (Exception ex) when (cancellationToken.IsCancellationRequested
                && ex is HttpListenerException or ObjectDisposedException or InvalidOperationException)
            {
                throw new OperationCanceledException(cancellationToken);
            }

            var q = ctx.Request.QueryString;
            var code = q["code"];
            var error = q["error"];

            if (string.IsNullOrEmpty(code) && string.IsNullOrEmpty(error))
            {
                // Ignore noise such as /favicon.ico; keep listening for the real redirect.
                ctx.Response.StatusCode = 204;
                try { ctx.Response.Close(); } catch { }
                continue;
            }

            const string html = "<html><body>You can close this tab and return to AiLimit.</body></html>";
            var buffer = System.Text.Encoding.UTF8.GetBytes(html);
            ctx.Response.ContentType = "text/html";
            try
            {
                await ctx.Response.OutputStream.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancellationToken);
            }
            ctx.Response.Close();

            if (!string.IsNullOrEmpty(error))
            {
                throw new OAuthCallbackException(error);
            }
            return new LoopbackCallback(code ?? "", q["state"] ?? "");
        }
    }

    private static int FreePort()
    {
        var l = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    public void Dispose() { try { _http.Close(); } catch { } }
}

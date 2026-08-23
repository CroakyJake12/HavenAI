using System.Diagnostics;
using System.Net;
using System.Text;
using ModelContextProtocol.Authentication;

namespace Haven.Infrastructure;

internal static class McpOAuthBrowserAuthorization
{
    public static async Task<AuthorizationResult?> AuthorizeAsync(AuthorizationCallbackContext context, CancellationToken cancellationToken)
    {
        if (!context.RedirectUri.IsLoopback || context.RedirectUri.Scheme != Uri.UriSchemeHttp)
            throw new InvalidOperationException("Haven MCP OAuth requires an HTTP loopback redirect URI.");
        using var listener = new HttpListener();
        listener.Prefixes.Add(Prefix(context.RedirectUri));
        listener.Start();
        Process.Start(new ProcessStartInfo(context.AuthorizationUri.AbsoluteUri) { UseShellExecute = true });
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));
        var callback = await listener.GetContextAsync().WaitAsync(timeout.Token).ConfigureAwait(false);
        var query = callback.Request.QueryString;
        var error = query["error"];
        await RespondAsync(callback.Response, string.IsNullOrWhiteSpace(error), cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(error)) throw new InvalidOperationException("MCP authorization was declined: " + (query["error_description"] ?? error));
        return new AuthorizationResult { Code = query["code"], State = query["state"], Iss = query["iss"] };
    }

    private static string Prefix(Uri redirectUri) => redirectUri.AbsoluteUri.EndsWith("/", StringComparison.Ordinal) ? redirectUri.AbsoluteUri : redirectUri.AbsoluteUri + "/";

    private static async Task RespondAsync(HttpListenerResponse response, bool success, CancellationToken cancellationToken)
    {
        response.StatusCode = 200;
        response.ContentType = "text/plain; charset=utf-8";
        var bytes = Encoding.UTF8.GetBytes(success ? "Connected to Haven. You can return to the app." : "Connection not completed. Return to Haven to review the error.");
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        response.Close();
    }
}

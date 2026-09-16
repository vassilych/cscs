using System.Security.Cryptography;
using System.Text;

namespace CscsMcp;

/// <summary>Who is calling, for the per-client rate limit.</summary>
public static class ClientAddress
{
    public static string Of(HttpContext context, bool trustForwardedHeaders)
    {
        if (trustForwardedHeaders)
        {
            var cloudflare = context.Request.Headers["CF-Connecting-IP"].ToString();
            if (!string.IsNullOrWhiteSpace(cloudflare))
            {
                return cloudflare.Trim();
            }
            var forwarded = context.Request.Headers["X-Forwarded-For"].ToString();
            if (!string.IsNullOrWhiteSpace(forwarded))
            {
                return forwarded.Split(',')[0].Trim();
            }
        }
        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }
}

/// <summary>
/// Optional access keys for /mcp. With "Cscs:ApiKeys" empty the playground is public -- the point of
/// it -- and the sandbox plus the rate limits are what protect the server. Listing keys turns it
/// into a private endpoint: a key is accepted as "Authorization: Bearer KEY" or "X-Api-Key: KEY".
/// </summary>
public sealed class ApiKeyMiddleware
{
    readonly RequestDelegate _next;
    readonly byte[][] _keys;

    public ApiKeyMiddleware(RequestDelegate next, IConfiguration configuration)
    {
        _next = next;
        _keys = (configuration.GetSection("Cscs:ApiKeys").Get<string[]>() ?? [])
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Select(k => Encoding.UTF8.GetBytes(k.Trim()))
            .ToArray();
    }

    public bool Enabled => _keys.Length > 0;

    public async Task InvokeAsync(HttpContext context)
    {
        if (!Enabled || !context.Request.Path.StartsWithSegments("/mcp"))
        {
            await _next(context);
            return;
        }

        var presented = context.Request.Headers["X-Api-Key"].ToString();
        var authorization = context.Request.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(presented) && authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            presented = authorization["Bearer ".Length..].Trim();
        }

        var bytes = Encoding.UTF8.GetBytes(presented);
        if (presented.Length == 0 || !_keys.Any(k => CryptographicOperations.FixedTimeEquals(k, bytes)))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync("A valid API key is required for this CSCS playground.");
            return;
        }
        await _next(context);
    }
}

using Npgsql;

namespace Tasklog.Api.Data;

/// <summary>
/// Hosting providers (Neon, Render, Heroku…) hand out URLs like
///   postgresql://user:pass@host/db?sslmode=require
/// but Npgsql wants "Host=…;Username=…". This accepts either form.
/// </summary>
public static class PostgresConnectionString
{
    public static string Normalize(string value)
    {
        value = value.Trim();
        if (!value.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) &&
            !value.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            return value; // already key=value
        }

        var uri = new Uri(value);
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.IsDefaultPort || uri.Port <= 0 ? 5432 : uri.Port,
            Database = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')),
        };

        var userInfo = uri.UserInfo.Split(':', 2);
        if (userInfo.Length > 0 && userInfo[0].Length > 0) builder.Username = Uri.UnescapeDataString(userInfo[0]);
        if (userInfo.Length > 1) builder.Password = Uri.UnescapeDataString(userInfo[1]);

        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = pair.Split('=', 2);
            if (kv.Length != 2) continue;

            var key = Uri.UnescapeDataString(kv[0]).ToLowerInvariant();
            var val = Uri.UnescapeDataString(kv[1]).ToLowerInvariant();

            // Only sslmode matters here; other URL options (channel_binding…)
            // are left to Npgsql's defaults, which work with Neon.
            if (key == "sslmode")
            {
                builder.SslMode = val switch
                {
                    "disable" => SslMode.Disable,
                    "allow" => SslMode.Allow,
                    "prefer" => SslMode.Prefer,
                    "verify-ca" => SslMode.VerifyCA,
                    "verify-full" => SslMode.VerifyFull,
                    _ => SslMode.Require
                };
            }
        }

        return builder.ConnectionString;
    }
}

using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Tasklog.Api.Features.Auth;

/// <summary>
/// Single-user login: one password, set through configuration
/// (environment variable <c>Auth__Password</c> in the cloud).
/// </summary>
public sealed class PasswordAuth
{
    public const string FingerprintClaim = "pwd";

    private readonly byte[] _passwordHash;

    public PasswordAuth(string password)
    {
        _passwordHash = SHA256.HashData(Encoding.UTF8.GetBytes(password));

        // Stored inside the (encrypted) login cookie. When the password is
        // changed, the fingerprint changes and every old login stops working.
        Fingerprint = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes("tasklog-session:" + password)))[..16];
    }

    public string Fingerprint { get; }

    /// <summary>
    /// Reads Auth:Password. Development falls back to "dev"; everywhere else a
    /// missing password stops the app from starting, and production requires
    /// at least 10 characters (the site is on the public internet).
    /// </summary>
    public static PasswordAuth FromConfiguration(IConfiguration config, IHostEnvironment env)
    {
        var password = config["Auth:Password"];

        if (string.IsNullOrWhiteSpace(password))
        {
            if (!env.IsDevelopment())
                throw new InvalidOperationException(
                    "Auth:Password is not set. Set the Auth__Password environment variable.");
            password = "dev";
        }

        if (env.IsProduction() && password.Length < 10)
            throw new InvalidOperationException("Auth:Password must be at least 10 characters in production.");

        return new PasswordAuth(password);
    }

    /// <summary>Constant-time comparison, so response time leaks nothing.</summary>
    public bool Verify(string? candidate) =>
        candidate is not null &&
        CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(candidate)), _passwordHash);
}

public sealed record LoginRequest(string? Password);

public sealed record MeResponse(bool Authenticated);

public static class AuthEndpoints
{
    public const string LoginRateLimit = "login";

    public static RouteGroupBuilder MapAuthEndpoints(this RouteGroupBuilder api)
    {
        var auth = api.MapGroup("/auth").WithTags("Auth");

        auth.MapPost("/login", async (LoginRequest req, PasswordAuth passwords, HttpContext http) =>
            {
                if (!passwords.Verify(req.Password))
                    return Results.Problem(title: "Şifrə yanlışdır.", statusCode: StatusCodes.Status401Unauthorized);

                var claims = new[]
                {
                    new Claim(ClaimTypes.Name, "owner"),
                    new Claim(PasswordAuth.FingerprintClaim, passwords.Fingerprint)
                };
                var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);

                await http.SignInAsync(
                    CookieAuthenticationDefaults.AuthenticationScheme,
                    new ClaimsPrincipal(identity),
                    new AuthenticationProperties { IsPersistent = true });

                return Results.Ok(new MeResponse(true));
            })
            .AllowAnonymous()
            .RequireRateLimiting(LoginRateLimit);

        auth.MapPost("/logout", async (HttpContext http) =>
            {
                await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                return Results.NoContent();
            })
            .AllowAnonymous();

        // 200 when logged in; the group's authorization turns anything else into 401.
        auth.MapGet("/me", () => Results.Ok(new MeResponse(true)));

        return api;
    }

    /// <summary>Rejects cookies issued before the password was changed.</summary>
    public static Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        var expected = context.HttpContext.RequestServices.GetRequiredService<PasswordAuth>().Fingerprint;
        if (context.Principal?.FindFirst(PasswordAuth.FingerprintClaim)?.Value == expected)
            return Task.CompletedTask;

        context.RejectPrincipal();
        return context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    }
}

using System.Security.Claims;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;

namespace Aspire.Hosting.DevJwt;

/// <summary>
/// Factory for creating and signing JWT tokens for development use.
/// </summary>
public static class JwtTokenFactory
{
    /// <summary>
    /// Creates a signed JWT token using the shared development signing key.
    /// </summary>
    /// <param name="signingKey">The Base64-encoded HMAC-SHA256 signing key.</param>
    /// <param name="issuer">The token issuer claim.</param>
    /// <param name="audience">The token audience claim.</param>
    /// <param name="subject">The subject (<c>sub</c>) claim value.</param>
    /// <param name="expiry">The token lifetime.</param>
    /// <param name="roles">Optional role claims to include.</param>
    /// <param name="scopes">Optional scope claims to include.</param>
    /// <param name="customClaims">Optional additional claims to include.</param>
    /// <returns>The signed JWT compact serialization string.</returns>
    public static string CreateToken(
        string signingKey,
        string issuer,
        string audience,
        string subject,
        TimeSpan expiry,
        IEnumerable<string>? roles = null,
        IEnumerable<string>? scopes = null,
        IDictionary<string, string>? customClaims = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(signingKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(issuer);
        ArgumentException.ThrowIfNullOrWhiteSpace(audience);
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);

        var keyBytes = Convert.FromBase64String(signingKey);
        var securityKey = new SymmetricSecurityKey(keyBytes);
        var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, subject),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };

        if (roles is not null)
        {
            foreach (var role in roles)
            {
                claims.Add(new Claim(ClaimTypes.Role, role));
            }
        }

        if (scopes is not null)
        {
            foreach (var scope in scopes)
            {
                claims.Add(new Claim("scope", scope));
            }
        }

        if (customClaims is not null)
        {
            foreach (var (type, value) in customClaims)
            {
                claims.Add(new Claim(type, value));
            }
        }

        var now = DateTime.UtcNow;
        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            notBefore: now,
            expires: now.Add(expiry),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>
    /// Generates a cryptographically random 256-bit signing key encoded as Base64.
    /// </summary>
    /// <returns>A Base64-encoded 32-byte random key.</returns>
    public static string GenerateSigningKey()
    {
        var key = new byte[32];
        System.Security.Cryptography.RandomNumberGenerator.Fill(key);
        return Convert.ToBase64String(key);
    }
}

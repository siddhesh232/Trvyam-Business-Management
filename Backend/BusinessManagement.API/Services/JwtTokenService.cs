using System.Security.Cryptography;
using System.Text;
using BusinessManagement.API.Configuration;
using BusinessManagement.Application.Abstractions;
using BusinessManagement.Domain.Entities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace BusinessManagement.API.Services;

public sealed class JwtTokenService(IOptions<JwtOptions> options, TimeProvider clock) : ITokenService
{
    public AccessToken CreateAccessToken(User user)
    {
        var o = options.Value;
        var now = clock.GetUtcNow().UtcDateTime;
        var expires = now.AddMinutes(o.AccessTokenMinutes);
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(o.SigningKey));

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = o.Issuer, Audience = o.Audience,
            IssuedAt = now, NotBefore = now, Expires = expires,
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256),
            Claims = new Dictionary<string, object>
            {
                ["sub"] = user.Id.ToString(),
                ["role"] = user.Role.ToString(),
                ["name"] = user.Name,
                ["jti"] = Guid.NewGuid().ToString("N")
            }
        };
        return new AccessToken(new JsonWebTokenHandler().CreateToken(descriptor), expires);
    }

    public RefreshTokenValue CreateRefreshToken()
    {
        var raw = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        var expires = clock.GetUtcNow().UtcDateTime.AddDays(options.Value.RefreshTokenDays);
        return new RefreshTokenValue(raw, HashRefreshToken(raw), expires);
    }

    public string HashRefreshToken(string rawToken) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));
}

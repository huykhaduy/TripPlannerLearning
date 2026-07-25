using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using TripPlanner.Application.Common.Interfaces;
using TripPlanner.Domain.Entities;

namespace TripPlanner.Infrastructure.Identity;

/// <summary>
/// Issues signed JWTs containing the user's id (sub) and email. The Web API
/// validates these tokens using the same signing key (see Program.cs).
/// </summary>
public class JwtTokenGenerator : IJwtTokenGenerator
{
    private const string PurposeClaimType = "purpose";
    private const string EmailVerificationPurpose = "email_verification";
    private static readonly TimeSpan EmailVerificationTtl = TimeSpan.FromHours(24);

    private readonly JwtSettings _settings;

    public JwtTokenGenerator(IOptions<JwtSettings> settings)
    {
        _settings = settings.Value;
    }

    /// <summary>
    /// A distinct audience for email-verification tokens. The Web API's
    /// JWT Bearer authentication (Program.cs) validates incoming tokens
    /// against the normal <see cref="JwtSettings.Audience"/> only, so a
    /// token issued for this audience is rejected by that pipeline outright
    /// — it can never be used as a Bearer credential against an
    /// [Authorize] endpoint, regardless of any claim-level check here.
    /// </summary>
    private string EmailVerificationAudience => $"{_settings.Audience}.email-verification";

    public (string Token, DateTimeOffset ExpiresAt) GenerateToken(User user)
    {
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(_settings.ExpiryMinutes);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };

        var token = new JwtSecurityToken(
            issuer: _settings.Issuer,
            audience: _settings.Audience,
            claims: claims,
            expires: expiresAt.UtcDateTime,
            signingCredentials: SigningCredentials());

        var encoded = new JwtSecurityTokenHandler().WriteToken(token);
        return (encoded, expiresAt);
    }

    public string GenerateEmailVerificationToken(User user)
    {
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(PurposeClaimType, EmailVerificationPurpose),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };

        var token = new JwtSecurityToken(
            issuer: _settings.Issuer,
            audience: EmailVerificationAudience,
            claims: claims,
            expires: DateTime.UtcNow.Add(EmailVerificationTtl),
            signingCredentials: SigningCredentials());

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public Guid? ValidateEmailVerificationToken(string token)
    {
        var parameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = _settings.Issuer,
            ValidateAudience = true,
            ValidAudience = EmailVerificationAudience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_settings.Key)),
            ClockSkew = TimeSpan.Zero,
        };

        // MapInboundClaims defaults to true, which silently rewrites short
        // claim names (e.g. "sub") to long legacy XML/SOAP URIs on the
        // resulting principal — FindFirst(JwtRegisteredClaimNames.Sub) would
        // never match anything unless this is turned off for this handler.
        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };

        ClaimsPrincipal principal;
        try
        {
            principal = handler.ValidateToken(token, parameters, out _);
        }
        catch (Exception)
        {
            // Expired, malformed, or badly signed — all read as "invalid link"
            // to the caller rather than a 500.
            return null;
        }

        if (principal.FindFirst(PurposeClaimType)?.Value != EmailVerificationPurpose)
        {
            return null; // e.g. someone tried to replay a normal access token here
        }

        return Guid.TryParse(principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var userId)
            ? userId
            : null;
    }

    private SigningCredentials SigningCredentials()
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_settings.Key));
        return new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
    }
}

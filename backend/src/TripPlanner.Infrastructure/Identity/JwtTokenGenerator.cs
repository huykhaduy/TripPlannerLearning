using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Logging;
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
    private readonly ILogger<JwtTokenGenerator> _logger;

    public JwtTokenGenerator(IOptions<JwtSettings> settings, ILogger<JwtTokenGenerator> logger)
    {
        _settings = settings.Value;
        _logger = logger;
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

        // Checked before ValidateToken, which throws ArgumentException — NOT a
        // SecurityTokenException — for input that isn't a JWT at all (null, blank, or
        // arbitrary text). The verify endpoint hands the raw request value straight
        // through, so that is ordinary input and must read as "invalid link".
        if (!handler.CanReadToken(token))
        {
            _logger.LogWarning("Rejected an email-verification token that is not a readable JWT.");
            return null;
        }

        ClaimsPrincipal principal;
        try
        {
            principal = handler.ValidateToken(token, parameters, out _);
        }
        catch (SecurityTokenException ex)
        {
            // Expired, badly signed, or wrong audience — all read as "invalid link"
            // to the caller rather than a 500.
            //
            // Narrow on purpose: catching every Exception would let a genuine bug
            // (misconfigured validation parameters, an unusable signing key) also
            // masquerade as a bad link, hiding it indefinitely.
            _logger.LogWarning(ex, "Rejected an email-verification token.");
            return null;
        }

        if (principal.FindFirst(PurposeClaimType)?.Value != EmailVerificationPurpose)
        {
            // The signature was valid but the token was issued for something else —
            // i.e. someone replayed an access token against the verify endpoint.
            // Worth its own line: a valid signature makes this more interesting than
            // an ordinary expired link.
            _logger.LogWarning("Rejected a validly-signed token with the wrong purpose claim.");
            return null;
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

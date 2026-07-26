using Microsoft.AspNetCore.Mvc;
using TripPlanner.Application.Features.Auth;
using TripPlanner.Application.Features.Auth.Dtos;

namespace TripPlanner.WebApi.Controllers;

/// <summary>
/// REFERENCE CONTROLLER (Feature 4: User Authentication) — fully implemented.
/// Notice how thin it is: it only binds the request, calls the use-case, and
/// returns a result. All rules and error handling live below the controller.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

    /// <summary>F4/US1 — register a new account and send a verification email. Does not log the user in (F4/US2).</summary>
    [HttpPost("register")]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<UserDto>> Register(RegisterRequest request, CancellationToken cancellationToken)
    {
        var response = await _authService.RegisterAsync(request, cancellationToken);
        return Ok(response);
    }

    /// <summary>F4/US3 — log in with email and password and return a JWT. Blocked (403) until the email is verified.</summary>
    [HttpPost("login")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        var response = await _authService.LoginAsync(request, cancellationToken);
        return Ok(response);
    }

    /// <summary>F4/US2 — verify the email behind a registration via the emailed link's token.</summary>
    [HttpPost("verify-email")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> VerifyEmail(VerifyEmailRequest request, CancellationToken cancellationToken)
    {
        await _authService.VerifyEmailAsync(request.Token, cancellationToken);
        return Ok();
    }

    /// <summary>F4/US2 — re-send the verification email for an unverified account, given its email. Anonymous, since a blocked (unverified) user has no session to authenticate with.</summary>
    [HttpPost("resend-verification")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ResendVerification(ResendVerificationRequest request, CancellationToken cancellationToken)
    {
        await _authService.ResendVerificationEmailAsync(request, cancellationToken);
        return Ok();
    }
}

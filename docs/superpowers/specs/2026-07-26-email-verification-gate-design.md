# Email Verification Gate — Design

**Date:** 2026-07-26
**Scope:** Change Feature 4 / US2 (email verification) from a soft nudge — login
always succeeds regardless of `IsEmailVerified`, and a dismissible banner
reminds the user afterward — to a hard gate: an unverified user's login
attempt fails outright, with a way to get a fresh verification link without
being signed in. Backend and frontend both change; no new domain entity or
migration is needed (`User.IsEmailVerified` already exists and already starts
`false`).

## Implementation note (found during build, not in the original design)

`AuthService.RegisterAsync` returned a full `AuthResponse` (a working JWT)
unconditionally, regardless of `IsEmailVerified`. This was missed during
design — the design only reasoned about `LoginAsync` — and meant a brand-new
account was fully logged in immediately on registration, bypassing the login
gate entirely for the most common path (a first-time user never hits `/login`
until they log out). Fixed by changing `RegisterAsync` to return `UserDto`
(no token) instead of `AuthResponse`; `RegisterPage.tsx` now shows a "check
your email" success state instead of persisting a session and navigating to
`/trips`. Caught by manually exercising the full flow in a browser rather than
by unit tests alone — the existing `AuthServiceTests` didn't cover "does
register log you in" as its own concern until this fix added the assertion.

## Current state (context)

`AuthService.LoginAsync` ([AuthService.cs:153-167](../../../backend/src/TripPlanner.Application/Features/Auth/AuthService.cs))
checks only email + password; it never reads `IsEmailVerified`. Once logged
in, `EmailVerificationBanner.tsx` renders a dismissible reminder (with its own
"resend" button, calling the *authenticated* `POST /api/auth/resend-verification`
which reads the user from the JWT) whenever `user.isEmailVerified` is false.
`ProtectedRoute.tsx` gates only on `isAuthenticated`, never on verification
status.

## Why this changes

The user wants unverified accounts blocked from actually using the app, not
just nudged. Since login itself will now fail for an unverified account, no
authenticated session can ever exist for an unverified user — which makes
today's authenticated resend endpoint and the post-login banner permanently
unreachable. Both are replaced/removed rather than left as dead code.

## Backend changes

### 1. `AuthService.LoginAsync` — the gate

After password verification succeeds (**never before** — preserves the
existing anti-enumeration property: a wrong password still gets the same
generic 401 whether or not the account is verified), check
`user.IsEmailVerified`. If `false`, throw:

```csharp
throw new ForbiddenException("Please verify your email before logging in.");
```

`ForbiddenException` already exists and is already mapped to HTTP 403 by
`ExceptionHandlingMiddleware` — no middleware change needed.

```csharp
var user = await _users.GetByEmailAsync(email, cancellationToken);

if (user is null || !_passwordHasher.Verify(request.Password, user.PasswordHash))
{
    throw new UnauthorizedException("Invalid email or password.");
}

if (!user.IsEmailVerified)
{
    throw new ForbiddenException("Please verify your email before logging in.");
}

return BuildAuthResponse(user);
```

### 2. Replace the authenticated resend endpoint with a public one

`ResendVerificationEmailAsync` currently takes no parameters and resolves the
user via `ICurrentUserService`. Replace its signature to take an email
instead, and drop `[Authorize]` from the controller action — same route,
different contract (this is a breaking change to that one endpoint, which is
fine: its only caller, `EmailVerificationBanner`, is being deleted in this
same change).

```csharp
// IAuthService.cs
Task ResendVerificationEmailAsync(ResendVerificationRequest request, CancellationToken cancellationToken = default);
```

```csharp
// Dtos/AuthDtos.cs
public record ResendVerificationRequest(string Email);
```

```csharp
// Validators/ResendVerificationRequestValidator.cs — same email shape check as RegisterRequestValidator
RuleFor(x => x.Email)
    .Cascade(CascadeMode.Stop)
    .NotEmpty().WithMessage("A valid email address is required.")
    .Must(email => email.Contains('@')).WithMessage("A valid email address is required.");
```

```csharp
// AuthService.cs
public async Task ResendVerificationEmailAsync(ResendVerificationRequest request, CancellationToken cancellationToken = default)
{
    await _resendValidator.ValidateAndThrowAppExceptionAsync(request, cancellationToken);

    var user = await _users.GetByEmailAsync(NormalizeEmail(request.Email), cancellationToken);

    // Always the same outcome from the caller's perspective whether the email
    // doesn't exist, is already verified, or is unverified — an anonymous
    // endpoint must not leak which emails are registered.
    if (user is null || user.IsEmailVerified)
    {
        return;
    }

    await SendVerificationEmailAsync(user, cancellationToken);
}
```

`SendVerificationEmailAsync` (private helper, already handles the
transient-SMTP-failure swallow) is unchanged.

### 3. Controller

```csharp
/// <summary>F4/US2 — re-send the verification email for an unverified account, given its email.</summary>
[HttpPost("resend-verification")]
[ProducesResponseType(StatusCodes.Status200OK)]
[ProducesResponseType(StatusCodes.Status400BadRequest)]
public async Task<IActionResult> ResendVerification(ResendVerificationRequest request, CancellationToken cancellationToken)
{
    await _authService.ResendVerificationEmailAsync(request, cancellationToken);
    return Ok();
}
```

(`[Authorize]` attribute removed; `ProducesResponseType(401)` removed since
this route no longer requires auth.)

### 4. Login's `ProducesResponseType`

Add `[ProducesResponseType(StatusCodes.Status403Forbidden)]` to the `Login`
action for accurate Swagger documentation.

## Frontend changes

### 1. `api/auth.ts`

```typescript
export async function resendVerificationEmail(email: string): Promise<void> {
  await apiClient.post('/auth/resend-verification', { email });
}
```

### 2. `LoginPage.tsx`

- Add state to track the "blocked, unverified" case distinctly from a generic
  login error, and a small resend sent/loading state (mirroring the pattern
  `EmailVerificationBanner` used before removal):
  ```typescript
  const [unverified, setUnverified] = useState(false);
  const [resending, setResending] = useState(false);
  const [resent, setResent] = useState(false);
  ```
- In `handleSubmit`'s catch block, branch on the HTTP status:
  ```typescript
  } catch (err) {
    if (axios.isAxiosError(err) && err.response?.status === 403) {
      setUnverified(true);
      setError(getErrorMessage(err, 'Please verify your email before logging in.'));
    } else {
      setUnverified(false);
      setError(getErrorMessage(err, 'Login failed.'));
    }
  }
  ```
- When `unverified` is true, render a "Resend verification email" button
  below the error message, calling `resendVerificationEmail(email)` (the
  email already typed into the form) and showing "Verification email
  sent — check your inbox." on success (same copy `EmailVerificationBanner`
  used).
- Reset `unverified`/`resent` at the top of `handleSubmit` alongside the
  existing `setError(null)`, so a retry starts clean.

### 3. Remove `EmailVerificationBanner`

- Delete `frontend/src/components/EmailVerificationBanner.tsx`.
- Remove its import and render from `App.tsx`.

### 4. `AuthContext.tsx`

- Remove `markEmailVerified` from `AuthContextValue` and its implementation —
  a user is (with rare exception, see Edge cases below) never in a logged-in
  session while unverified anymore, so there is no in-memory user object left
  to flip.

### 5. `VerifyEmailPage.tsx`

- Drop the `markEmailVerified` call and the `useAuth()` import (no longer
  needed).
- Change the success state's copy/link from "Go to My trips" → `/trips` to
  **"You can now log in."** → `/login`, since logging in is now the only
  productive next step for someone who just verified.

## Edge cases

- **Wrong password, any verification state** → unchanged generic 401 "Invalid
  email or password." (password check runs first).
- **Correct password + unverified** → new 403 + resend button.
- **Resend endpoint, three input states** (unknown email / already-verified /
  unverified-existing) → identical generic 200 response in all three cases;
  only the third actually sends an email.
- **US8 (login-then-resume-add-to-trip)** — `AddToTripButton`'s redirect-to-
  login-and-resume flow needs no changes: it only proceeds past `login()` on
  success, so an unverified user simply sees the new blocked state on
  `LoginPage` instead of resuming, exactly as intended (they must verify
  first regardless of what they were trying to do).
- **Already-registered unverified accounts** (e.g. from before this change
  shipped) need no migration — they simply can't log in until they verify,
  which is the desired new behavior.
- **Stale pre-deploy sessions**: a JWT issued to an unverified user *before*
  this change deploys remains valid until its normal expiry (JWTs aren't
  revoked). For up to that window, such a session could still exist — this is
  a narrow, self-resolving transitional edge case, not worth special-casing
  in a training capstone.
- **Anonymous resend endpoint abuse** (spamming an arbitrary inbox with
  verification emails): no rate-limiting exists anywhere in this codebase
  today, and this change doesn't add any — same accepted-gap treatment this
  project already gives other unaddressed concerns (e.g. the documented
  `SortOrder` concurrency gap in `CLAUDE.md`).

## Tests (`AuthServiceTests.cs`)

- `LoginAsync_WhenEmailNotVerified_ThrowsForbidden` (correct password, unverified user).
- `LoginAsync_WhenEmailVerified_Succeeds` (regression — existing verified-login test should already cover this; confirm it still passes).
- `LoginAsync_WrongPassword_ThrowsUnauthorized_RegardlessOfVerificationStatus` (parameterize over verified/unverified if not already covered).
- `ResendVerificationEmailAsync_UnknownEmail_DoesNotThrowAndSendsNothing`.
- `ResendVerificationEmailAsync_AlreadyVerified_SendsNothing`.
- `ResendVerificationEmailAsync_UnverifiedExisting_SendsEmail` (assert the email-sender mock was invoked).

No frontend automated tests exist in this project (per `CLAUDE.md`/`TECHNICAL_SPEC.md` — no test runner configured beyond `tsc`/ESLint), so frontend verification is manual: register a new account, confirm login is blocked with the resend button, resend, verify via the emailed-link flow, confirm login then succeeds.

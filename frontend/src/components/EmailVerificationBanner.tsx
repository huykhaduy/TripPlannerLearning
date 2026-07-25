import { useState } from 'react';
import axios from 'axios';
import { resendVerificationEmail } from '../api/auth';
import { useAuth } from '../auth/AuthContext';

/**
 * F4/US2 — a dismissible reminder for unverified accounts. Login isn't
 * blocked, so this is purely a nudge; dismissal is per-session (component
 * state) and reappears on the next reload/login rather than being persisted.
 */
export function EmailVerificationBanner() {
  const { user } = useAuth();
  const [dismissed, setDismissed] = useState(false);
  const [sending, setSending] = useState(false);
  const [sent, setSent] = useState(false);
  const [error, setError] = useState<string | null>(null);

  if (!user || user.isEmailVerified || dismissed) return null;

  async function handleResend() {
    setError(null);
    setSending(true);
    try {
      await resendVerificationEmail();
      setSent(true);
    } catch (err) {
      setError(
        axios.isAxiosError(err)
          ? (err.response?.data?.detail ?? 'Could not resend the email. Please try again.')
          : 'Could not resend the email. Please try again.',
      );
    } finally {
      setSending(false);
    }
  }

  return (
    <div className="border-b border-amber-200 bg-amber-50">
      <div className="mx-auto flex max-w-[1280px] flex-wrap items-center justify-between gap-3 px-4 py-2.5 text-sm text-amber-900 sm:px-12">
        <span>
          {sent ? 'Verification email sent — check your inbox.' : 'Please verify your email to activate your account.'}
          {error && <span className="ml-2 text-red-600">{error}</span>}
        </span>
        <div className="flex items-center gap-3">
          {!sent && (
            <button
              type="button"
              onClick={handleResend}
              disabled={sending}
              className="font-semibold text-amber-900 underline hover:text-amber-700 disabled:opacity-60"
            >
              {sending ? 'Sending…' : 'Resend email'}
            </button>
          )}
          <button
            type="button"
            onClick={() => setDismissed(true)}
            aria-label="Dismiss"
            className="text-amber-700 hover:text-amber-900"
          >
            ✕
          </button>
        </div>
      </div>
    </div>
  );
}

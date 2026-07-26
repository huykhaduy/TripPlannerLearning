import { useEffect, useState } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { getErrorMessage } from '../../api/client';
import { verifyEmail } from '../../api/auth';
import { Card } from '../../components/Card';

type Status = 'verifying' | 'success' | 'error';

/** F4/US2 — lands here from the link emailed by the backend; verifies the token in the URL. */
export function VerifyEmailPage() {
  const [searchParams] = useSearchParams();
  const token = searchParams.get('token');

  const [status, setStatus] = useState<Status>('verifying');
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!token) {
      setStatus('error');
      setError('This verification link is missing its token.');
      return;
    }

    let ignore = false;
    verifyEmail(token)
      .then(() => {
        if (ignore) return;
        setStatus('success');
      })
      .catch((err) => {
        if (ignore) return;
        setError(getErrorMessage(err, 'This verification link is invalid or has expired.'));
        setStatus('error');
      });

    return () => {
      ignore = true;
    };
  }, [token]);

  return (
    <div className="flex min-h-[65vh] items-center justify-center">
      <Card className="w-full max-w-sm text-center">
        {status === 'verifying' && <p className="text-sm text-slate-500">Verifying your email…</p>}

        {status === 'success' && (
          <>
            <h1 className="text-2xl font-semibold tracking-tight text-slate-900">Email verified</h1>
            <p className="mt-2 text-sm text-slate-500">Your account is now fully active.</p>
            <Link to="/login" className="mt-4 inline-block text-sm text-brand-600 hover:underline">
              You can now log in
            </Link>
          </>
        )}

        {status === 'error' && (
          <>
            <h1 className="text-2xl font-semibold tracking-tight text-slate-900">Verification failed</h1>
            <p className="mt-2 text-sm text-red-600">{error}</p>
            <Link to="/" className="mt-4 inline-block text-sm text-brand-600 hover:underline">
              ← Back to home
            </Link>
          </>
        )}
      </Card>
    </div>
  );
}

import { useEffect, useState } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import axios from 'axios';
import { verifyEmail } from '../../api/auth';
import { useAuth } from '../../auth/AuthContext';
import { Card } from '../../components/Card';

type Status = 'verifying' | 'success' | 'error';

/** F4/US2 — lands here from the link emailed by the backend; verifies the token in the URL. */
export function VerifyEmailPage() {
  const [searchParams] = useSearchParams();
  const token = searchParams.get('token');
  const { markEmailVerified } = useAuth();

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
        markEmailVerified();
        setStatus('success');
      })
      .catch((err) => {
        if (ignore) return;
        const message = axios.isAxiosError(err)
          ? (err.response?.data?.detail ?? 'This verification link is invalid or has expired.')
          : 'This verification link is invalid or has expired.';
        setError(message);
        setStatus('error');
      });

    return () => {
      ignore = true;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [token]);

  return (
    <div className="flex min-h-[65vh] items-center justify-center">
      <Card className="w-full max-w-sm text-center">
        {status === 'verifying' && <p className="text-sm text-slate-500">Verifying your email…</p>}

        {status === 'success' && (
          <>
            <h1 className="text-2xl font-semibold tracking-tight text-slate-900">Email verified</h1>
            <p className="mt-2 text-sm text-slate-500">Your account is now fully active.</p>
            <Link to="/trips" className="mt-4 inline-block text-sm text-brand-600 hover:underline">
              Go to My trips
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

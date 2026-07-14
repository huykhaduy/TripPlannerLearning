import { useState, type FormEvent } from 'react';
import { Link, Navigate, useLocation, useNavigate } from 'react-router-dom';
import axios from 'axios';
import { useAuth } from '../../auth/AuthContext';
import { Card } from '../../components/Card';
import { Field, fieldControlClass } from '../../components/Field';
import { Button } from '../../components/Button';

/** Feature 4 / US3 — log in with email and password. */
export function LoginPage() {
  const { login, isAuthenticated } = useAuth();
  const navigate = useNavigate();
  const location = useLocation();

  // US8: a page that redirected here (e.g. "Add to trip" while logged out)
  // says where to return to; a direct visit falls back to the planner.
  const from = (location.state as { from?: string } | null)?.from ?? '/trips';

  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);

  // Already signed in — the form makes no sense; go back (or to the planner).
  if (isAuthenticated) {
    return <Navigate to={from} replace state={location.state} />;
  }

  async function handleSubmit(event: FormEvent) {
    event.preventDefault();
    setError(null);
    setSubmitting(true);
    try {
      await login(email, password);
      // Forward the state so AddToTripButton can resume the pending add.
      navigate(from, { replace: true, state: location.state });
    } catch (err) {
      // The API returns a ProblemDetails body with a friendly "detail" message.
      const message = axios.isAxiosError(err)
        ? (err.response?.data?.detail ?? 'Login failed.')
        : 'Login failed.';
      setError(message);
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <div className="flex min-h-[65vh] items-center justify-center">
      <Card className="w-full max-w-sm">
        <h1 className="text-3xl font-semibold tracking-tight text-slate-900">Log in</h1>
        <form onSubmit={handleSubmit} className="mt-6 flex flex-col gap-4">
          <Field label="Email">
            <input
              type="email"
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              required
              className={fieldControlClass}
            />
          </Field>
          <Field label="Password">
            <input
              type="password"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              required
              className={fieldControlClass}
            />
          </Field>
          {error && <p className="text-sm text-red-600">{error}</p>}
          <Button type="submit" disabled={submitting}>
            {submitting ? 'Signing in…' : 'Log in'}
          </Button>
        </form>
        <p className="mt-4 text-sm text-slate-500">
          No account?{' '}
          <Link to="/register" className="text-brand-600 hover:underline">
            Sign up
          </Link>
        </p>
      </Card>
    </div>
  );
}

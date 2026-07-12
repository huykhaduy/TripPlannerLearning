import { useEffect, useState, type FormEvent } from 'react';
import { Link } from 'react-router-dom';
import axios from 'axios';
import { useAuth } from '../../auth/AuthContext';
import { createTrip, getMyTrips } from '../../api/trips';
import type { TripSummary } from '../../types';

function formatDates(startDate: string | null, endDate: string | null) {
  if (!startDate || !endDate) return 'No dates yet';
  return `${startDate} → ${endDate}`;
}

/** F3/US1 & US10 — the current user's trip list plus a create-trip form. */
export function TripsPage() {
  const { user, logout } = useAuth();

  const [trips, setTrips] = useState<TripSummary[] | null>(null);
  const [loadError, setLoadError] = useState<string | null>(null);

  const [name, setName] = useState('');
  const [creating, setCreating] = useState(false);
  const [createError, setCreateError] = useState<string | null>(null);

  useEffect(() => {
    getMyTrips()
      .then(setTrips)
      .catch(() => setLoadError('Could not load your trips. Please try again.'));
  }, []);

  async function handleCreate(event: FormEvent) {
    event.preventDefault();
    setCreateError(null);
    setCreating(true);
    try {
      const trip = await createTrip(name.trim());
      setTrips((current) => [...(current ?? []), trip]);
      setName('');
    } catch (err) {
      const message = axios.isAxiosError(err)
        ? (err.response?.data?.detail ?? 'Could not create the trip.')
        : 'Could not create the trip.';
      setCreateError(message);
    } finally {
      setCreating(false);
    }
  }

  return (
    <div className="rounded-xl border border-slate-200 bg-white p-6 shadow-sm sm:p-8">
      <header className="flex items-center justify-between">
        <h1 className="text-2xl font-bold">My trips</h1>
        <button
          onClick={logout}
          className="rounded-lg border border-slate-300 px-3 py-1.5 text-sm text-slate-600 hover:bg-slate-50"
        >
          Log out
        </button>
      </header>
      <p className="mt-1 text-sm text-slate-500">
        Signed in as <strong>{user?.email}</strong>.
      </p>

      <form onSubmit={handleCreate} className="mt-6 flex flex-col gap-4">
        <label className="flex flex-col gap-1.5 text-sm text-slate-500">
          New trip
          <input
            value={name}
            onChange={(e) => setName(e.target.value)}
            placeholder="e.g. Summer in Da Nang"
            required
            className="rounded-lg border border-slate-300 px-3 py-2 text-base text-slate-800 focus:border-blue-500 focus:outline-none focus:ring-2 focus:ring-blue-200"
          />
        </label>
        {createError && <p className="text-sm text-red-600">{createError}</p>}
        <button
          type="submit"
          disabled={creating || name.trim() === ''}
          className="self-start rounded-lg bg-blue-600 px-4 py-2 font-medium text-white hover:bg-blue-700 disabled:cursor-not-allowed disabled:opacity-60"
        >
          {creating ? 'Creating…' : 'Create trip'}
        </button>
      </form>

      <div className="mt-6">
        {loadError ? (
          <p className="text-sm text-red-600">{loadError}</p>
        ) : trips === null ? (
          <p className="text-sm text-slate-500">Loading your trips…</p>
        ) : trips.length === 0 ? (
          <p className="text-sm text-slate-500">No trips yet — create your first one above.</p>
        ) : (
          <ul className="flex flex-col gap-2">
            {trips.map((trip) => (
              <li key={trip.id}>
                <Link
                  to={`/trips/${trip.id}`}
                  className="flex flex-col gap-0.5 rounded-lg border border-slate-200 p-3 hover:border-blue-400 hover:bg-blue-50"
                >
                  <strong>{trip.name}</strong>
                  <span className="text-sm text-slate-500">
                    {formatDates(trip.startDate, trip.endDate)}
                  </span>
                  <span className="text-sm text-slate-500">
                    {trip.destinationCount} destination{trip.destinationCount === 1 ? '' : 's'}
                  </span>
                </Link>
              </li>
            ))}
          </ul>
        )}
      </div>
    </div>
  );
}

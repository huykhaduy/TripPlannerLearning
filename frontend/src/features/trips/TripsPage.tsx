import { useEffect, useState, type FormEvent } from 'react';
import { Link } from 'react-router-dom';
import axios from 'axios';
import { createTrip, getMyTrips } from '../../api/trips';
import { Card } from '../../components/Card';
import { Field, fieldControlClass } from '../../components/Field';
import { Button } from '../../components/Button';
import { EmptyState } from '../../components/EmptyState';
import type { TripSummary } from '../../types';

function formatDates(startDate: string | null, endDate: string | null) {
  if (!startDate || !endDate) return 'No dates yet';
  return `${startDate} → ${endDate}`;
}

/** F3/US1 & US10 — the current user's trip list plus a create-trip form. */
export function TripsPage() {
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
    <div className="flex flex-col gap-8">
      <h1 className="text-3xl font-semibold tracking-tight text-slate-900">My trips</h1>

      <form onSubmit={handleCreate} className="flex flex-wrap items-end gap-3">
        <div className="min-w-64 flex-1">
          <Field label="New trip">
            <input
              value={name}
              onChange={(e) => setName(e.target.value)}
              placeholder="e.g. Summer in Da Nang"
              required
              className={fieldControlClass}
            />
          </Field>
        </div>
        <Button type="submit" disabled={creating || name.trim() === ''}>
          {creating ? 'Creating…' : 'Create trip'}
        </Button>
      </form>
      {createError && <p className="text-sm text-red-600">{createError}</p>}

      {loadError ? (
        <p className="text-sm text-red-600">{loadError}</p>
      ) : trips === null ? (
        <p className="text-sm text-slate-500">Loading your trips…</p>
      ) : trips.length === 0 ? (
        <EmptyState icon="🗺️" message="No trips yet — create your first one above." />
      ) : (
        <div className="grid gap-5 sm:grid-cols-2 lg:grid-cols-3">
          {trips.map((trip) => (
            <Link key={trip.id} to={`/trips/${trip.id}`}>
              <Card
                padding="tight"
                className="relative flex h-full flex-col gap-1 overflow-hidden transition-colors hover:border-brand-300"
              >
                <span className="absolute inset-x-0 top-0 h-1.5 bg-brand-500" aria-hidden="true" />
                <strong className="text-slate-900">{trip.name}</strong>
                <span className="text-sm text-slate-500">{formatDates(trip.startDate, trip.endDate)}</span>
                <span className="text-sm text-slate-500">
                  {trip.destinationCount} destination{trip.destinationCount === 1 ? '' : 's'}
                </span>
              </Card>
            </Link>
          ))}
        </div>
      )}
    </div>
  );
}

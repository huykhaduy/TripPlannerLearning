import { useEffect, useState, type FormEvent } from 'react';
import { Link } from 'react-router-dom';
import { getErrorMessage } from '../../api/client';
import { createTrip, getMyTrips } from '../../api/trips';
import { Card } from '../../components/Card';
import { Field, fieldControlClass } from '../../components/Field';
import { Button } from '../../components/Button';
import { EmptyState } from '../../components/EmptyState';
import type { TripSummary } from '../../types';
import { TripThumbnail, formatDates } from './TripThumbnail';

// F3/US10 — a relative status pill computed purely from the trip's own
// dates vs. today; there's no backend field for this.
function getTripStatusLabel(startDate: string | null, endDate: string | null): string | null {
  if (!startDate || !endDate) return null;
  const now = new Date();
  const today = `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}-${String(now.getDate()).padStart(2, '0')}`;
  if (today > endDate) return 'Past trip';
  if (today >= startDate) return 'In progress';
  const days = Math.ceil((new Date(startDate).getTime() - new Date(today).getTime()) / 86_400_000);
  return days <= 1 ? 'Tomorrow' : `In ${days} days`;
}

/** Trip card header: the trip's cover photo, or the gradient fallback, plus a status pill. */
function TripCardHeader({ id, coverImageUrl, status }: { id: string; coverImageUrl: string | null; status: string | null }) {
  return (
    <div className="relative flex h-24 items-end overflow-hidden p-3">
      <TripThumbnail id={id} coverImageUrl={coverImageUrl} />
      {status && (
        <span className="absolute right-3 top-3 rounded-full bg-white/90 px-3 py-1 text-xs font-bold text-slate-900">
          {status}
        </span>
      )}
    </div>
  );
}

/** F3/US1 & US10 — the current user's trip list plus a create-trip form. */
export function TripsPage() {
  const [trips, setTrips] = useState<TripSummary[] | null>(null);
  const [loadError, setLoadError] = useState<string | null>(null);

  const [name, setName] = useState('');
  const [creating, setCreating] = useState(false);
  const [createError, setCreateError] = useState<string | null>(null);
  const [modalOpen, setModalOpen] = useState(false);

  useEffect(() => {
    getMyTrips()
      .then(setTrips)
      .catch(() => setLoadError('Could not load your trips. Please try again.'));
  }, []);

  function closeModal() {
    setModalOpen(false);
    setName('');
    setCreateError(null);
  }

  async function handleCreate(event: FormEvent) {
    event.preventDefault();
    setCreateError(null);
    setCreating(true);
    try {
      const trip = await createTrip(name.trim());
      setTrips((current) => [...(current ?? []), trip]);
      setName('');
      setModalOpen(false);
    } catch (err) {
      setCreateError(getErrorMessage(err, 'Could not create the trip.'));
    } finally {
      setCreating(false);
    }
  }

  return (
    <div className="flex flex-col gap-8">
      <div className="flex flex-wrap items-center justify-between gap-4">
        <div>
          <h1 className="font-headline text-3xl font-bold tracking-tight text-slate-900">My Trips</h1>
          <p className="text-slate-500">Manage and view all your upcoming journeys.</p>
        </div>
        <Button type="button" variant="action" onClick={() => setModalOpen(true)}>
          + Plan new trip
        </Button>
      </div>

      {modalOpen && (
        <div
          className="fixed inset-0 z-50 flex items-center justify-center bg-slate-900/40 p-4"
          onClick={closeModal}
        >
          <div
            className="w-full max-w-lg rounded-2xl bg-white shadow-2xl"
            onClick={(e) => e.stopPropagation()}
            role="dialog"
            aria-label="Plan a new trip"
          >
            <div className="flex items-center justify-between border-b border-[#E2E8F0] px-6 py-4">
              <h2 className="font-headline text-lg font-semibold text-slate-900">Plan new trip</h2>
              <button
                type="button"
                onClick={closeModal}
                className="text-slate-400 hover:text-slate-600"
                aria-label="Close"
              >
                ✕
              </button>
            </div>
            <form onSubmit={handleCreate} className="flex flex-col gap-4 p-6">
              <Field label="Trip name">
                <input
                  value={name}
                  onChange={(e) => setName(e.target.value)}
                  placeholder="e.g. Summer in Da Nang"
                  required
                  className={fieldControlClass}
                />
              </Field>
              {createError && <p className="text-sm text-red-600">{createError}</p>}
              <div className="flex justify-end gap-2 pt-2">
                <Button type="button" variant="secondary" onClick={closeModal}>
                  Cancel
                </Button>
                <Button type="submit" variant="action" disabled={creating || name.trim() === ''}>
                  {creating ? 'Creating…' : 'Create trip'}
                </Button>
              </div>
            </form>
          </div>
        </div>
      )}

      {loadError ? (
        <p className="text-sm text-red-600">{loadError}</p>
      ) : trips === null ? (
        <p className="text-sm text-slate-500">Loading your trips…</p>
      ) : trips.length === 0 ? (
        <EmptyState icon="🗺️" message="No trips yet — create your first one above." />
      ) : (
        <div className="grid gap-5 sm:grid-cols-2 xl:grid-cols-3">
          {trips.map((trip) => {
            const status = getTripStatusLabel(trip.startDate, trip.endDate);
            return (
              <Link key={trip.id} to={`/trips/${trip.id}`}>
                <Card padding="tight" className="flex h-full flex-col gap-0 overflow-hidden p-0">
                  <TripCardHeader id={trip.id} coverImageUrl={trip.coverImageUrl} status={status} />
                  <div className="flex flex-1 flex-col gap-1 p-4">
                    <strong className="font-headline text-slate-900">{trip.name}</strong>
                    <span className="text-sm text-slate-500">{formatDates(trip.startDate, trip.endDate)}</span>
                    <span className="mt-auto pt-3 text-sm font-semibold text-slate-500">
                      {trip.destinationCount} destination{trip.destinationCount === 1 ? '' : 's'}
                    </span>
                  </div>
                </Card>
              </Link>
            );
          })}
        </div>
      )}
    </div>
  );
}

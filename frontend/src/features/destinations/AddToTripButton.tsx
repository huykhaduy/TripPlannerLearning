import { useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import axios from 'axios';
import { useAuth } from '../../auth/AuthContext';
import { getMyTrips, getTrip, addDestination } from '../../api/trips';
import type { AttractionSummary, ItineraryDay, TripSummary } from '../../types';

/**
 * F3/US3 — add an attraction to one of the user's trips, optionally onto a
 * specific day (US4 drag-and-drop is out of scope, so a day picker stands in).
 * Logged-out users are sent to the login page instead (US8).
 */
export function AddToTripButton({ attraction }: { attraction: AttractionSummary }) {
  const { isAuthenticated } = useAuth();
  const navigate = useNavigate();

  const [open, setOpen] = useState(false);
  const [justAdded, setJustAdded] = useState(false);

  function handleClick() {
    if (!isAuthenticated) {
      navigate('/login'); // US8: prompt login when trying to add while logged out
      return;
    }
    setOpen(true);
  }

  return (
    <>
      <button
        type="button"
        onClick={handleClick}
        className="mt-1 self-start rounded-lg border border-blue-200 px-3 py-1.5 text-sm text-blue-600 hover:bg-blue-50"
      >
        {justAdded ? 'Added ✓' : 'Add to trip'}
      </button>
      {open && (
        <AddToTripDialog
          attraction={attraction}
          onClose={() => setOpen(false)}
          onAdded={() => {
            setOpen(false);
            setJustAdded(true);
            setTimeout(() => setJustAdded(false), 2500);
          }}
        />
      )}
    </>
  );
}

function AddToTripDialog({
  attraction,
  onClose,
  onAdded,
}: {
  attraction: AttractionSummary;
  onClose: () => void;
  onAdded: () => void;
}) {
  const [trips, setTrips] = useState<TripSummary[] | null>(null);
  const [tripId, setTripId] = useState('');
  const [days, setDays] = useState<ItineraryDay[] | null>(null);
  // '' = Saved Places (no day) — the backend takes itineraryDayId: null.
  const [dayId, setDayId] = useState('');
  const [adding, setAdding] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let ignore = false;
    getMyTrips()
      .then((result) => {
        if (!ignore) setTrips(result);
      })
      .catch(() => {
        if (!ignore) setError('Could not load your trips. Please try again.');
      });
    return () => {
      ignore = true;
    };
  }, []);

  // Load the selected trip's days so the user can schedule directly (US3 AC3).
  useEffect(() => {
    setDays(null);
    setDayId('');
    if (!tripId) return;

    let ignore = false;
    getTrip(tripId)
      .then((trip) => {
        if (!ignore) setDays(trip.days);
      })
      .catch(() => {
        if (!ignore) setError('Could not load the trip days. Please try again.');
      });
    return () => {
      ignore = true;
    };
  }, [tripId]);

  async function handleAdd() {
    setError(null);
    setAdding(true);
    try {
      await addDestination(tripId, attraction.providerId, dayId || null);
      onAdded();
    } catch (err) {
      const message = axios.isAxiosError(err)
        ? (err.response?.data?.detail ?? 'Could not add the destination.')
        : 'Could not add the destination.';
      setError(message);
    } finally {
      setAdding(false);
    }
  }

  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center bg-black/30 p-4"
      onClick={onClose}
    >
      <div
        className="w-full max-w-sm rounded-xl border border-slate-200 bg-white p-6 shadow-lg"
        onClick={(e) => e.stopPropagation()}
        role="dialog"
        aria-label={`Add ${attraction.name} to a trip`}
      >
        <h3 className="text-lg font-semibold">Add “{attraction.name}”</h3>

        {trips === null && !error && <p className="mt-3 text-sm text-slate-500">Loading your trips…</p>}

        {trips !== null && trips.length === 0 && (
          <p className="mt-3 text-sm text-slate-500">
            You have no trips yet — create one on the My trips page first.
          </p>
        )}

        {trips !== null && trips.length > 0 && (
          <div className="mt-3 flex flex-col gap-3">
            <label className="flex flex-col gap-1.5 text-sm text-slate-500">
              Trip
              <select
                value={tripId}
                onChange={(e) => setTripId(e.target.value)}
                className="rounded-lg border border-slate-300 px-3 py-2 text-base text-slate-800 focus:border-blue-500 focus:outline-none focus:ring-2 focus:ring-blue-200"
              >
                <option value="">Choose a trip…</option>
                {trips.map((trip) => (
                  <option key={trip.id} value={trip.id}>
                    {trip.name}
                  </option>
                ))}
              </select>
            </label>

            {tripId && (
              <label className="flex flex-col gap-1.5 text-sm text-slate-500">
                Day
                <select
                  value={dayId}
                  onChange={(e) => setDayId(e.target.value)}
                  className="rounded-lg border border-slate-300 px-3 py-2 text-base text-slate-800 focus:border-blue-500 focus:outline-none focus:ring-2 focus:ring-blue-200"
                >
                  <option value="">Saved Places (no day yet)</option>
                  {(days ?? []).map((day) => (
                    <option key={day.id} value={day.id}>
                      Day {day.dayNumber} — {day.date}
                    </option>
                  ))}
                </select>
              </label>
            )}
          </div>
        )}

        {error && <p className="mt-3 text-sm text-red-600">{error}</p>}

        <div className="mt-4 flex justify-end gap-2">
          <button
            type="button"
            onClick={onClose}
            className="rounded-lg border border-slate-300 px-3 py-1.5 text-sm text-slate-600 hover:bg-slate-50"
          >
            Cancel
          </button>
          <button
            type="button"
            onClick={handleAdd}
            disabled={!tripId || adding}
            className="rounded-lg bg-blue-600 px-4 py-1.5 text-sm font-medium text-white hover:bg-blue-700 disabled:cursor-not-allowed disabled:opacity-60"
          >
            {adding ? 'Adding…' : 'Add'}
          </button>
        </div>
      </div>
    </div>
  );
}

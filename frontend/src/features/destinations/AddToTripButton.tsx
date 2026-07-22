import { useEffect, useState } from 'react';
import { useLocation, useNavigate } from 'react-router-dom';
import axios from 'axios';
import { useAuth } from '../../auth/AuthContext';
import { getMyTrips, getTrip, addDestination } from '../../api/trips';
import { Button } from '../../components/Button';
import { Field, fieldControlClass } from '../../components/Field';
import type { AttractionSummary, ItineraryDay, TripSummary } from '../../types';

/**
 * F3/US3 — add an attraction to one of the user's trips, optionally onto a
 * specific day (US4 drag-and-drop is out of scope, so a day picker stands in).
 * Logged-out users are sent to the login page instead (US8).
 */
export function AddToTripButton({ attraction }: { attraction: AttractionSummary }) {
  const { isAuthenticated } = useAuth();
  const navigate = useNavigate();
  const location = useLocation();

  const [open, setOpen] = useState(false);
  const [justAdded, setJustAdded] = useState(false);

  // US8 resume: LoginPage forwards our state back here after sign-in. The
  // providerId check matters — the search page renders one button per
  // attraction, and only the one the user originally clicked should open.
  useEffect(() => {
    const state = location.state as { resumeAddId?: string } | null;
    if (isAuthenticated && state?.resumeAddId === attraction.providerId) {
      setOpen(true);
      // Clear the note so refresh/back doesn't re-open the dialog.
      navigate(location.pathname, { replace: true, state: null });
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [isAuthenticated, location.state]);

  function handleClick() {
    if (!isAuthenticated) {
      // US8: prompt login, remembering where we were and what the user was
      // trying to add so the flow can resume after sign-in.
      navigate('/login', {
        state: { from: location.pathname, resumeAddId: attraction.providerId },
      });
      return;
    }
    setOpen(true);
  }

  return (
    <>
      <Button type="button" variant="action" size="sm" onClick={handleClick} className="w-full">
        {justAdded ? 'Added ✓' : 'Add to trip'}
      </Button>
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
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-slate-900/40 p-4" onClick={onClose}>
      <div
        className="w-full max-w-sm rounded-2xl border border-slate-200 bg-white p-6 shadow-lg"
        onClick={(e) => e.stopPropagation()}
        role="dialog"
        aria-label={`Add ${attraction.name} to a trip`}
      >
        <h3 className="text-lg font-semibold text-slate-900">Add “{attraction.name}”</h3>

        {trips === null && !error && <p className="mt-3 text-sm text-slate-500">Loading your trips…</p>}

        {trips !== null && trips.length === 0 && (
          <p className="mt-3 text-sm text-slate-500">
            You have no trips yet — create one on the My trips page first.
          </p>
        )}

        {trips !== null && trips.length > 0 && (
          <div className="mt-4 flex flex-col gap-3">
            <Field label="Trip">
              <select value={tripId} onChange={(e) => setTripId(e.target.value)} className={fieldControlClass}>
                <option value="">Choose a trip…</option>
                {trips.map((trip) => (
                  <option key={trip.id} value={trip.id}>
                    {trip.name}
                  </option>
                ))}
              </select>
            </Field>

            {tripId && (
              <Field label="Day">
                <select value={dayId} onChange={(e) => setDayId(e.target.value)} className={fieldControlClass}>
                  <option value="">Saved Places (no day yet)</option>
                  {(days ?? []).map((day) => (
                    <option key={day.id} value={day.id}>
                      Day {day.dayNumber} — {day.date}
                    </option>
                  ))}
                </select>
              </Field>
            )}
          </div>
        )}

        {error && <p className="mt-3 text-sm text-red-600">{error}</p>}

        <div className="mt-5 flex justify-end gap-2">
          <Button type="button" variant="secondary" size="sm" onClick={onClose}>
            Cancel
          </Button>
          <Button type="button" size="sm" onClick={handleAdd} disabled={!tripId || adding}>
            {adding ? 'Adding…' : 'Add'}
          </Button>
        </div>
      </div>
    </div>
  );
}

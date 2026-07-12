import { useEffect, useState, type FormEvent } from 'react';
import { Link, useParams } from 'react-router-dom';
import axios from 'axios';
import { getTrip, removeDestination, updateItineraryItem, updateTrip } from '../../api/trips';
import type { TripDestination, TripDetail } from '../../types';

/**
 * F3/US4-US6 — one drop-enabled bucket (a day, or Saved Places when dayId is
 * null). Dropping on a row inserts at that row's position; dropping on the
 * surrounding area appends to the end.
 */
function DestinationList({
  dayId,
  destinations,
  emptyHint,
  onRemove,
  removingItemId,
  onDragStart,
  onDragEnd,
  onDrop,
}: {
  dayId: string | null;
  destinations: TripDestination[];
  emptyHint: string;
  onRemove: (itemId: string) => void;
  removingItemId: string | null;
  onDragStart: (itemId: string) => void;
  onDragEnd: () => void;
  onDrop: (targetDayId: string | null, position: number) => void;
}) {
  return (
    <div
      onDragOver={(e) => e.preventDefault()} // required, or the browser refuses the drop
      onDrop={() => onDrop(dayId, destinations.length)}
    >
      {destinations.length === 0 ? (
        <p className="mt-2 rounded-lg border border-dashed border-slate-300 px-3 py-2 text-sm text-slate-500">
          {emptyHint}
        </p>
      ) : (
        <ul className="mt-2 flex flex-col gap-2">
          {destinations.map((destination, index) => (
            <li
              key={destination.itemId}
              draggable
              onDragStart={() => onDragStart(destination.itemId)}
              onDragEnd={onDragEnd}
              onDragOver={(e) => e.preventDefault()}
              onDrop={(e) => {
                e.stopPropagation(); // this drop is ours — don't also append via the list handler
                onDrop(dayId, index);
              }}
              className="flex cursor-grab items-center justify-between gap-2 rounded-lg border border-slate-200 px-3 py-2 active:cursor-grabbing"
            >
              <span>{destination.name}</span>
              <button
                type="button"
                onClick={() => onRemove(destination.itemId)}
                disabled={removingItemId === destination.itemId}
                className="rounded-lg border border-red-200 px-3 py-1.5 text-sm text-red-600 hover:bg-red-50 disabled:cursor-not-allowed disabled:opacity-60"
              >
                {removingItemId === destination.itemId ? 'Removing…' : 'Remove'}
              </button>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}

/**
 * Optimistic mirror of the backend's move semantics (US4-US6): pull the item
 * out of every bucket, insert it at the requested position in the target, and
 * renumber both buckets densely. The server confirms the same state.
 */
function moveLocally(
  trip: TripDetail,
  itemId: string,
  targetDayId: string | null,
  position: number,
): TripDetail {
  const item = [...trip.savedPlaces, ...trip.days.flatMap((d) => d.destinations)].find(
    (d) => d.itemId === itemId,
  );
  if (!item) return trip;

  const without = (list: TripDestination[]) => list.filter((d) => d.itemId !== itemId);
  const insertInto = (list: TripDestination[]) => {
    const next = [...list];
    next.splice(Math.min(position, next.length), 0, item);
    return next.map((d, i) => ({ ...d, sortOrder: i }));
  };

  return {
    ...trip,
    days: trip.days.map((day) => {
      const rest = without(day.destinations);
      return { ...day, destinations: day.id === targetDayId ? insertInto(rest) : rest };
    }),
    savedPlaces: targetDayId === null ? insertInto(without(trip.savedPlaces)) : without(trip.savedPlaces),
  };
}

/** F3/US2, US7, US9 & US10 — day-by-day itinerary, Saved Places, edit name/dates. */
export function TripDetailPage() {
  const { tripId } = useParams<{ tripId: string }>();

  const [trip, setTrip] = useState<TripDetail | null>(null);
  const [loadError, setLoadError] = useState<string | null>(null);

  // Edit form state — kept as strings so they bind directly to the inputs.
  const [name, setName] = useState('');
  const [startDate, setStartDate] = useState('');
  const [endDate, setEndDate] = useState('');
  const [saving, setSaving] = useState(false);
  const [saveError, setSaveError] = useState<string | null>(null);

  const [removingItemId, setRemovingItemId] = useState<string | null>(null);
  const [removeError, setRemoveError] = useState<string | null>(null);

  const [dragItemId, setDragItemId] = useState<string | null>(null);
  const [moveError, setMoveError] = useState<string | null>(null);

  // Sync both the page and the edit form from a freshly fetched/saved trip.
  function applyTrip(fresh: TripDetail) {
    setTrip(fresh);
    setName(fresh.name);
    setStartDate(fresh.startDate ?? '');
    setEndDate(fresh.endDate ?? '');
  }

  useEffect(() => {
    if (!tripId) return;

    let ignore = false;
    setLoadError(null);
    getTrip(tripId)
      .then((fresh) => {
        if (!ignore) applyTrip(fresh);
      })
      .catch((err) => {
        if (ignore) return;
        const notFound = axios.isAxiosError(err) && err.response?.status === 404;
        setLoadError(notFound ? 'Trip not found.' : 'Could not load the trip. Please try again.');
      });
    return () => {
      ignore = true;
    };
  }, [tripId]);

  // F3/US2 AC5 — dates are yyyy-MM-dd strings (both from ItineraryDay.date and
  // <input type="date">), so plain string comparison sorts chronologically.
  function wouldRemoveScheduledItems(): boolean {
    if (!trip) return false;
    return trip.days.some((day) => {
      const staysInRange = startDate !== '' && endDate !== '' && day.date >= startDate && day.date <= endDate;
      return !staysInRange && day.destinations.length > 0;
    });
  }

  async function handleSave(event: FormEvent) {
    event.preventDefault();
    if (!tripId) return;

    if (wouldRemoveScheduledItems()) {
      const confirmed = window.confirm(
        'Changing the dates removes days outside the new range and moves their destinations back to Saved Places. Continue?',
      );
      if (!confirmed) return;
    }

    setSaveError(null);
    setSaving(true);
    try {
      // <input type="date"> uses '' for empty — the API wants null.
      const updated = await updateTrip(tripId, name.trim(), startDate || null, endDate || null);
      applyTrip(updated);
    } catch (err) {
      const message = axios.isAxiosError(err)
        ? (err.response?.data?.detail ?? 'Could not save the trip.')
        : 'Could not save the trip.';
      setSaveError(message);
    } finally {
      setSaving(false);
    }
  }

  // F3/US4-US6 with NFR4 (DnD ≤ 100 ms): apply the move to local state
  // immediately, let the API confirm in the background, roll back on error.
  async function handleDrop(targetDayId: string | null, position: number) {
    if (!tripId || !trip || !dragItemId) return;
    const itemId = dragItemId;
    setDragItemId(null);

    const snapshot = trip;
    setMoveError(null);
    setTrip(moveLocally(trip, itemId, targetDayId, position));
    try {
      await updateItineraryItem(tripId, itemId, targetDayId, position);
    } catch (err) {
      setTrip(snapshot); // roll back the optimistic move
      const message = axios.isAxiosError(err)
        ? (err.response?.data?.detail ?? 'Could not move the destination.')
        : 'Could not move the destination.';
      setMoveError(message);
    }
  }

  async function handleRemove(itemId: string) {
    if (!tripId) return;
    setRemoveError(null);
    setRemovingItemId(itemId);
    try {
      await removeDestination(tripId, itemId);
      setTrip(
        (current) =>
          current && {
            ...current,
            days: current.days.map((day) => ({
              ...day,
              destinations: day.destinations.filter((d) => d.itemId !== itemId),
            })),
            savedPlaces: current.savedPlaces.filter((d) => d.itemId !== itemId),
          },
      );
    } catch {
      setRemoveError('Could not remove the destination. Please try again.');
    } finally {
      setRemovingItemId(null);
    }
  }

  if (loadError) {
    return (
      <div className="rounded-xl border border-slate-200 bg-white p-6 shadow-sm sm:p-8">
        <p className="text-sm text-red-600">{loadError}</p>
        <Link to="/trips" className="mt-2 inline-block text-sm text-blue-600 hover:underline">
          ← Back to my trips
        </Link>
      </div>
    );
  }

  if (!trip) {
    return (
      <div className="rounded-xl border border-slate-200 bg-white p-6 shadow-sm sm:p-8">
        <p className="text-sm text-slate-500">Loading trip…</p>
      </div>
    );
  }

  return (
    <div className="rounded-xl border border-slate-200 bg-white p-6 shadow-sm sm:p-8">
      <Link to="/trips" className="text-sm text-blue-600 hover:underline">
        ← Back to my trips
      </Link>
      <h1 className="mt-2 text-2xl font-bold">{trip.name}</h1>

      <form onSubmit={handleSave} className="mt-4 flex flex-col gap-4">
        <label className="flex flex-col gap-1.5 text-sm text-slate-500">
          Name
          <input
            value={name}
            onChange={(e) => setName(e.target.value)}
            required
            className="rounded-lg border border-slate-300 px-3 py-2 text-base text-slate-800 focus:border-blue-500 focus:outline-none focus:ring-2 focus:ring-blue-200"
          />
        </label>
        <div className="flex gap-4">
          <label className="flex flex-1 flex-col gap-1.5 text-sm text-slate-500">
            Start date
            <input
              type="date"
              value={startDate}
              onChange={(e) => setStartDate(e.target.value)}
              className="rounded-lg border border-slate-300 px-3 py-2 text-base text-slate-800 focus:border-blue-500 focus:outline-none focus:ring-2 focus:ring-blue-200"
            />
          </label>
          <label className="flex flex-1 flex-col gap-1.5 text-sm text-slate-500">
            End date
            <input
              type="date"
              value={endDate}
              onChange={(e) => setEndDate(e.target.value)}
              className="rounded-lg border border-slate-300 px-3 py-2 text-base text-slate-800 focus:border-blue-500 focus:outline-none focus:ring-2 focus:ring-blue-200"
            />
          </label>
        </div>
        {saveError && <p className="text-sm text-red-600">{saveError}</p>}
        <button
          type="submit"
          disabled={saving || name.trim() === ''}
          className="self-start rounded-lg bg-blue-600 px-4 py-2 font-medium text-white hover:bg-blue-700 disabled:cursor-not-allowed disabled:opacity-60"
        >
          {saving ? 'Saving…' : 'Save changes'}
        </button>
      </form>

      {removeError && <p className="mt-4 text-sm text-red-600">{removeError}</p>}
      {moveError && <p className="mt-4 text-sm text-red-600">{moveError}</p>}

      {trip.days.length === 0 ? (
        <p className="mt-6 text-sm text-slate-500">
          Set the trip dates to generate a day-by-day itinerary.
        </p>
      ) : (
        trip.days.map((day) => (
          <section key={day.id} className="mt-6 border-t border-slate-200 pt-4">
            <h2 className="text-lg font-semibold">
              Day {day.dayNumber} <span className="font-normal text-slate-500">{day.date}</span>
            </h2>
            <DestinationList
              dayId={day.id}
              destinations={day.destinations}
              emptyHint="Nothing planned yet — drag a destination here."
              onRemove={handleRemove}
              removingItemId={removingItemId}
              onDragStart={setDragItemId}
              onDragEnd={() => setDragItemId(null)}
              onDrop={handleDrop}
            />
          </section>
        ))
      )}

      <section className="mt-6 border-t border-slate-200 pt-4">
        <h2 className="text-lg font-semibold">Saved Places</h2>
        <DestinationList
          dayId={null}
          destinations={trip.savedPlaces}
          emptyHint="No saved places — add destinations from the Discover page."
          onRemove={handleRemove}
          removingItemId={removingItemId}
          onDragStart={setDragItemId}
          onDragEnd={() => setDragItemId(null)}
          onDrop={handleDrop}
        />
      </section>
    </div>
  );
}

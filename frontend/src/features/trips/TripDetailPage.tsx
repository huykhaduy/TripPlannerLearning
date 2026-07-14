import { useEffect, useState, type FormEvent } from 'react';
import { Link, useParams } from 'react-router-dom';
import axios from 'axios';
import { getTrip, removeDestination, updateItineraryItem, updateTrip } from '../../api/trips';
import { Card } from '../../components/Card';
import { Field, fieldControlClass } from '../../components/Field';
import { Button } from '../../components/Button';
import type { TripDestination, TripDetail } from '../../types';

/** Small thumbnail with the same missing/broken-image fallback as the attraction cards. */
function DestinationThumbnail({ imageUrl, name }: { imageUrl: string | null; name: string }) {
  const [failed, setFailed] = useState(false);
  const showImage = imageUrl && !failed;
  return showImage ? (
    <img src={imageUrl} alt={name} onError={() => setFailed(true)} className="h-10 w-10 rounded-lg object-cover" />
  ) : (
    <div
      className="flex h-10 w-10 items-center justify-center rounded-lg bg-slate-100 text-base"
      aria-hidden="true"
    >
      🏛️
    </div>
  );
}

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
        <p className="mt-3 rounded-xl border-2 border-dashed border-slate-300 bg-slate-50 px-4 py-6 text-center text-sm text-slate-500">
          {emptyHint}
        </p>
      ) : (
        <ul className="mt-3 flex flex-col gap-2">
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
              className="flex cursor-grab items-center gap-3 rounded-xl border border-slate-200 bg-white px-3 py-2 active:cursor-grabbing"
            >
              <span className="select-none text-slate-400" aria-hidden="true">
                ⠿
              </span>
              <DestinationThumbnail imageUrl={destination.imageUrl} name={destination.name} />
              <span className="flex-1 text-slate-900">{destination.name}</span>
              <Button
                type="button"
                variant="danger"
                size="sm"
                onClick={() => onRemove(destination.itemId)}
                disabled={removingItemId === destination.itemId}
              >
                {removingItemId === destination.itemId ? 'Removing…' : 'Remove'}
              </Button>
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
      <Card className="mx-auto max-w-2xl">
        <p className="text-sm text-red-600">{loadError}</p>
        <Link to="/trips" className="mt-2 inline-block text-sm text-brand-600 hover:underline">
          ← Back to my trips
        </Link>
      </Card>
    );
  }

  if (!trip) {
    return (
      <Card className="mx-auto max-w-2xl">
        <p className="text-sm text-slate-500">Loading trip…</p>
      </Card>
    );
  }

  const hasDays = trip.days.length > 0;

  return (
    <div className="flex flex-col gap-8">
      <div>
        <Link to="/trips" className="text-sm text-brand-600 hover:underline">
          ← Back to my trips
        </Link>
        <h1 className="mt-2 text-3xl font-semibold tracking-tight text-slate-900">{trip.name}</h1>
      </div>

      <Card>
        <form onSubmit={handleSave} className="flex flex-col gap-4">
          <Field label="Name">
            <input value={name} onChange={(e) => setName(e.target.value)} required className={fieldControlClass} />
          </Field>
          <div className="flex gap-4">
            <div className="flex-1">
              <Field label="Start date">
                <input
                  type="date"
                  value={startDate}
                  onChange={(e) => setStartDate(e.target.value)}
                  className={fieldControlClass}
                />
              </Field>
            </div>
            <div className="flex-1">
              <Field label="End date">
                <input
                  type="date"
                  value={endDate}
                  onChange={(e) => setEndDate(e.target.value)}
                  className={fieldControlClass}
                />
              </Field>
            </div>
          </div>
          {saveError && <p className="text-sm text-red-600">{saveError}</p>}
          <Button type="submit" disabled={saving || name.trim() === ''} className="self-start">
            {saving ? 'Saving…' : 'Save changes'}
          </Button>
        </form>
      </Card>

      {removeError && <p className="text-sm text-red-600">{removeError}</p>}
      {moveError && <p className="text-sm text-red-600">{moveError}</p>}

      <div className={hasDays ? 'flex flex-col gap-8 lg:flex-row lg:items-start' : 'flex flex-col gap-8'}>
        <div className={hasDays ? 'flex flex-1 flex-col gap-6' : 'flex flex-col gap-6'}>
          {hasDays ? (
            trip.days.map((day) => (
              <section key={day.id}>
                <h2 className="text-lg font-semibold text-slate-900">
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
          ) : (
            <p className="text-sm text-slate-500">Set the trip dates to generate a day-by-day itinerary.</p>
          )}
        </div>

        <div className={hasDays ? 'lg:sticky lg:top-8 lg:w-80 lg:shrink-0' : ''}>
          <section>
            <h2 className="text-lg font-semibold text-slate-900">Saved Places</h2>
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
      </div>
    </div>
  );
}

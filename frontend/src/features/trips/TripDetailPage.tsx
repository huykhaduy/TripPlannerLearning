import { useEffect, useState, type FormEvent } from 'react';
import { Link, useParams } from 'react-router-dom';
import axios from 'axios';
import { getErrorMessage } from '../../api/client';
import { getTrip, removeDestination, updateItineraryItem, updateTrip } from '../../api/trips';
import { Card } from '../../components/Card';
import { Field, fieldControlClass } from '../../components/Field';
import { Button } from '../../components/Button';
import { Modal } from '../../components/Modal';
import type { TripDestination, TripDetail } from '../../types';
import { formatDates } from './TripThumbnail';

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
  isMatch,
}: {
  dayId: string | null;
  destinations: TripDestination[];
  emptyHint: string;
  onRemove: (itemId: string) => void;
  removingItemId: string | null;
  onDragStart: (itemId: string) => void;
  onDragEnd: () => void;
  onDrop: (targetDayId: string | null, position: number) => void;
  // Optional visual-only filter (the Saved Places search box). Rows are
  // hidden with CSS rather than removed from the array so index-based drop
  // positions stay correct regardless of what's currently filtered out.
  isMatch?: (destination: TripDestination) => boolean;
}) {
  const anyVisible = !isMatch || destinations.length === 0 || destinations.some(isMatch);

  return (
    <div
      onDragOver={(e) => e.preventDefault()} // required, or the browser refuses the drop
      onDrop={() => onDrop(dayId, destinations.length)}
    >
      {destinations.length === 0 ? (
        <p className="mt-3 rounded-xl border-2 border-dashed border-slate-300 bg-slate-50 px-4 py-6 text-center text-sm text-slate-500">
          {emptyHint}
        </p>
      ) : !anyVisible ? (
        <p className="mt-3 rounded-xl border-2 border-dashed border-slate-300 bg-slate-50 px-4 py-6 text-center text-sm text-slate-500">
          No matches.
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
              className={`group relative flex cursor-grab items-center gap-3 rounded-lg border border-[#E2E8F0] bg-white px-3 py-2 shadow-sm active:cursor-grabbing ${
                isMatch && !isMatch(destination) ? 'hidden' : ''
              }`}
            >
              <span className="select-none text-slate-400" aria-hidden="true">
                ⠿
              </span>
              <DestinationThumbnail imageUrl={destination.imageUrl} name={destination.name} />
              <span className="flex-1 truncate text-slate-900">{destination.name}</span>
              {/* Icon-only affordance matching the mockup's hover-reveal "×" —
                  distinct enough from the shared Button's variants that a
                  plain <button> reads better here than forcing a Button variant. */}
              <button
                type="button"
                onClick={() => onRemove(destination.itemId)}
                disabled={removingItemId === destination.itemId}
                aria-label={`Remove ${destination.name}`}
                className="rounded-full p-1 text-slate-300 opacity-0 transition-opacity hover:bg-red-50 hover:text-red-600 disabled:opacity-100 group-hover:opacity-100"
              >
                {removingItemId === destination.itemId ? '…' : '✕'}
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

  const [savedPlacesQuery, setSavedPlacesQuery] = useState('');
  const [editOpen, setEditOpen] = useState(false);

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

  function closeEditModal() {
    setEditOpen(false);
    if (trip) {
      setName(trip.name);
      setStartDate(trip.startDate ?? '');
      setEndDate(trip.endDate ?? '');
    }
    setSaveError(null);
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
      setEditOpen(false);
    } catch (err) {
      setSaveError(getErrorMessage(err, 'Could not save the trip.'));
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
      setMoveError(getErrorMessage(err, 'Could not move the destination.'));
    }
  }

  async function handleRemove(itemId: string) {
    if (!tripId || !trip) return;

    const destination = [...trip.savedPlaces, ...trip.days.flatMap((d) => d.destinations)].find(
      (d) => d.itemId === itemId,
    );
    const confirmed = window.confirm(
      `Remove ${destination?.name ?? 'this destination'} from the trip?`,
    );
    if (!confirmed) return;

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

  const savedPlacesSection = (
    <section className="flex h-full flex-col rounded-lg border border-[#E2E8F0] bg-white p-4">
      <h2 className="font-headline text-base font-semibold text-brand-600">Saved Places</h2>
      <p className="text-xs text-slate-500">Drag items into a day to schedule them.</p>
      <input
        type="search"
        value={savedPlacesQuery}
        onChange={(e) => setSavedPlacesQuery(e.target.value)}
        placeholder="Search saved…"
        aria-label="Search saved places"
        className="mt-3 rounded-lg border border-slate-300 bg-slate-50 px-3 py-2 text-sm focus:border-brand-600 focus:outline-none focus:ring-2 focus:ring-brand-600/10"
      />
      <div className="mt-3 flex-1 overflow-y-auto">
        <DestinationList
          dayId={null}
          destinations={trip.savedPlaces}
          emptyHint="No saved places — add destinations from the Explore page."
          onRemove={handleRemove}
          removingItemId={removingItemId}
          onDragStart={setDragItemId}
          onDragEnd={() => setDragItemId(null)}
          onDrop={handleDrop}
          isMatch={
            savedPlacesQuery.trim() === ''
              ? undefined
              : (d) => d.name.toLowerCase().includes(savedPlacesQuery.trim().toLowerCase())
          }
        />
      </div>
    </section>
  );

  return (
    <div className="flex flex-col gap-8">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <Link to="/trips" className="text-sm text-brand-600 hover:underline">
            ← Back to my trips
          </Link>
          <h1 className="font-headline mt-2 text-3xl font-bold tracking-tight text-slate-900">{trip.name}</h1>
          <p className="text-sm text-slate-500">{formatDates(trip.startDate, trip.endDate)}</p>
        </div>
        <div className="flex items-center gap-3">
          <span className="flex items-center gap-2 rounded-full bg-brand-50 px-3 py-1.5 text-xs font-semibold text-brand-600">
            <span aria-hidden="true">{saving ? '⏳' : '☁️'}</span>
            {saving ? 'Saving…' : 'All changes saved'}
          </span>
          <Button type="button" variant="secondary" size="sm" onClick={() => setEditOpen(true)}>
            Edit details
          </Button>
        </div>
      </div>

      {editOpen && (
        <Modal title="Edit trip details" onClose={closeEditModal}>
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
            <div className="flex justify-end gap-2">
              <Button type="button" variant="secondary" onClick={closeEditModal}>
                Cancel
              </Button>
              <Button type="submit" disabled={saving || name.trim() === ''}>
                {saving ? 'Saving…' : 'Save changes'}
              </Button>
            </div>
          </form>
        </Modal>
      )}

      {removeError && <p className="text-sm text-red-600">{removeError}</p>}
      {moveError && <p className="text-sm text-red-600">{moveError}</p>}

      {hasDays ? (
        <div className="flex flex-col gap-6 lg:flex-row lg:items-start">
          <div className="lg:sticky lg:top-8 lg:w-72 lg:shrink-0">{savedPlacesSection}</div>

          <div className="flex flex-1 gap-6 overflow-x-auto pb-4">
            {trip.days.map((day) => (
              <section key={day.id} className="flex w-72 shrink-0 flex-col gap-3 rounded-lg bg-[#F8FAFC] p-3">
                <h2 className="font-headline text-base font-semibold text-slate-900">
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
            ))}
          </div>
        </div>
      ) : (
        <>
          <p className="text-sm text-slate-500">Set the trip dates to generate a day-by-day itinerary.</p>
          {savedPlacesSection}
        </>
      )}
    </div>
  );
}

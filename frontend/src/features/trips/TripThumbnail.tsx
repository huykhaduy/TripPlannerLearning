import { useState } from 'react';

/** F3/US10 shared helper — used by both the My Trips cards and the add-to-trip picker. */
export function formatDates(startDate: string | null, endDate: string | null) {
  if (!startDate || !endDate) return 'No dates yet';
  return `${startDate} → ${endDate}`;
}

// Deterministic (hashed from the trip id), not random — so a card's header
// color is stable across reloads. Fallback for trips with no destination
// photo yet (a brand-new trip, or one whose destinations have no image).
const HEADER_GRADIENTS = [
  'from-brand-600 to-brand-400',
  'from-action-500 to-amber-300',
  'from-tertiary-500 to-emerald-300',
  'from-slate-700 to-slate-400',
];
function headerGradient(id: string): string {
  const hash = [...id].reduce((sum, ch) => sum + ch.charCodeAt(0), 0);
  return HEADER_GRADIENTS[hash % HEADER_GRADIENTS.length];
}

/**
 * A trip's cover photo, or the gradient fallback above. Fills its parent
 * (which must be positioned, e.g. `relative`, and sized) via `absolute inset-0`.
 */
export function TripThumbnail({ id, coverImageUrl }: { id: string; coverImageUrl: string | null }) {
  const [failed, setFailed] = useState(false);
  const showImage = coverImageUrl && !failed;

  if (!showImage) {
    return <div className={`absolute inset-0 bg-gradient-to-br ${headerGradient(id)}`} />;
  }

  return (
    <>
      <img
        src={coverImageUrl}
        alt=""
        onError={() => setFailed(true)}
        className="absolute inset-0 h-full w-full object-cover"
      />
      <div className="absolute inset-0 bg-gradient-to-t from-black/40 to-transparent" />
    </>
  );
}

import { Link } from 'react-router-dom';

// Cycled so the placeholder rows don't read as one solid block of identical bars.
const NAME_BAR_WIDTHS = ['w-3/4', 'w-1/2', 'w-2/3'];

/** One placeholder destination row: drag handle, thumbnail, name. */
function RowSkeleton({ widthIndex }: { widthIndex: number }) {
  return (
    <li className="flex items-center gap-3 rounded-lg border border-[#E2E8F0] bg-white px-3 py-2 shadow-sm">
      <div className="h-4 w-2 shrink-0 animate-pulse rounded bg-slate-200" />
      <div className="h-10 w-10 shrink-0 animate-pulse rounded-lg bg-slate-100" />
      <div className={`h-4 animate-pulse rounded bg-slate-200 ${NAME_BAR_WIDTHS[widthIndex % NAME_BAR_WIDTHS.length]}`} />
    </li>
  );
}

function RowsSkeleton({ count }: { count: number }) {
  return (
    <ul className="mt-3 flex flex-col gap-2">
      {Array.from({ length: count }, (_, i) => (
        <RowSkeleton key={i} widthIndex={i} />
      ))}
    </ul>
  );
}

/**
 * F3/US2 — placeholder for TripDetailPage while the trip loads.
 *
 * Two things here are deliberately real rather than placeheld: the back link
 * (it needs no trip data, and someone waiting on a slow request should still be
 * able to leave) and the Saved Places heading and hint, which are fixed chrome.
 *
 * It mirrors the *dated* layout — sidebar plus day columns. Whether a trip has
 * days isn't knowable until it arrives, and this is both the common case and the
 * taller one, so an undated trip collapses into a shorter view rather than
 * shoving content down the page.
 */
export function TripDetailSkeleton() {
  return (
    <section className="flex flex-col gap-8" aria-busy="true" aria-label="Loading trip">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <Link to="/trips" className="text-sm text-brand-600 hover:underline">
            ← Back to my trips
          </Link>
          {/* h-9 / h-5 are the line boxes of the text-3xl name and text-sm date
              range below, so the header keeps its height when they land. */}
          <div className="mt-2 h-9 w-64 animate-pulse rounded bg-slate-200" />
          <div className="h-5 w-44 animate-pulse rounded bg-slate-100" />
        </div>
        <div className="h-9 w-28 animate-pulse rounded-lg bg-slate-200" />
      </div>

      <div className="flex flex-col gap-6 lg:flex-row lg:items-start">
        <div className="lg:w-80 lg:shrink-0">
          <section className="flex h-full flex-col rounded-lg border border-[#E2E8F0] bg-white p-4">
            <h2 className="font-headline text-base font-semibold text-brand-600">Saved Places</h2>
            <p className="text-xs text-slate-500">Drag items into a day to schedule them.</p>
            {/* Carries the real search box's own padding/border classes rather
                than a guessed height, so it is exactly as tall as the input. */}
            <div className="mt-3 rounded-lg border border-slate-300 bg-slate-50 px-3 py-2">
              <div className="h-5 w-28 animate-pulse rounded bg-slate-200" />
            </div>
            <div className="mt-3 flex-1 overflow-y-auto">
              <RowsSkeleton count={3} />
            </div>
          </section>
        </div>

        <div className="flex flex-1 gap-6 overflow-x-auto pb-4">
          {/* A plain div, not the <section> the real day columns use: without
              their "Day N" heading these would be unnamed landmarks, and the
              surrounding "Loading trip" region already says what this is. */}
          {[0, 1].map((column) => (
            <div
              key={column}
              className="flex w-80 shrink-0 flex-col gap-3 rounded-lg border border-[#E2E8F0] bg-white p-4"
            >
              <div className="h-6 w-40 animate-pulse rounded bg-slate-200" />
              <div>
                <RowsSkeleton count={2} />
              </div>
            </div>
          ))}
        </div>
      </div>
    </section>
  );
}

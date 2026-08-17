/**
 * A single card-shaped placeholder shown while attractions are loading. Mirrors
 * AttractionCard's own shape (image, title, category pill, button) so the grid
 * doesn't reflow when the real cards arrive. Shared by AttractionsList and
 * NearbyAttractions.
 */
export function AttractionCardSkeleton() {
  return (
    <div className="flex h-full flex-col overflow-hidden rounded-lg border border-[#E2E8F0] bg-white">
      <div className="aspect-[4/3] w-full animate-pulse bg-slate-200" />
      <div className="flex flex-1 flex-col gap-2 p-4 pb-0">
        <div className="h-4 w-3/4 animate-pulse rounded bg-slate-200" />
        <div className="h-5 w-16 animate-pulse rounded-full bg-slate-200" />
      </div>
      <div className="p-4 pt-3">
        <div className="h-9 w-full animate-pulse rounded-md bg-slate-200" />
      </div>
    </div>
  );
}

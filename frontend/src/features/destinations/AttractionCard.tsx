import { useState } from 'react';
import { Link } from 'react-router-dom';
import { AddToTripButton } from './AddToTripButton';
import type { AttractionSummary } from '../../types';

/** F1/US3 & F2 — a single attraction/POI card: photo, name, category, rating, add-to-trip. */
export function AttractionCard({ attraction }: { attraction: AttractionSummary }) {
  const [imageFailed, setImageFailed] = useState(false);
  const showImage = attraction.imageUrl && !imageFailed;

  return (
    <div className="overflow-hidden rounded-lg border border-[#E2E8F0] bg-white transition-shadow hover:shadow-md">
      {/* F2/US1 — open the details view. Add-to-trip stays outside this link
          so a button never ends up nested inside an anchor. */}
      <Link to={`/destinations/${encodeURIComponent(attraction.providerId)}`}>
        {showImage ? (
          <img
            src={attraction.imageUrl!}
            alt={attraction.name}
            onError={() => setImageFailed(true)}
            className="aspect-[4/3] w-full object-cover"
          />
        ) : (
          <div className="flex aspect-[4/3] w-full items-center justify-center bg-slate-100 text-4xl">🏛️</div>
        )}
        <div className="flex flex-col gap-1 p-4 pb-0">
          <strong className="font-headline text-slate-900">{attraction.name}</strong>
          {attraction.category && <span className="text-sm capitalize text-slate-500">{attraction.category}</span>}
          {attraction.rating != null && (
            <span className="text-sm text-slate-700">⭐ {attraction.rating.toFixed(1)}</span>
          )}
        </div>
      </Link>
      <div className="p-4 pt-3">
        <AddToTripButton attraction={attraction} />
      </div>
    </div>
  );
}

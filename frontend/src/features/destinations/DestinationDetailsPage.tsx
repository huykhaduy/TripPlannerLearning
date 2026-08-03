import { useEffect, useState } from 'react';
import { useLocation, useNavigate, useParams } from 'react-router-dom';
import { getErrorStatus } from '../../api/client';
import { getDestinationDetails } from '../../api/destinations';
import { AddToTripButton } from './AddToTripButton';
import { Card } from '../../components/Card';
import { NearbyAttractions } from './NearbyAttractions';
import type { DestinationDetails } from '../../types';

// A small bounding box around the point (~1.1km) keeps the embedded pin at a
// readable street-level zoom without needing a map API key.
const MAP_BBOX_DELTA = 0.01;

function buildOsmEmbedUrl(latitude: number, longitude: number): string {
  const bbox = [longitude - MAP_BBOX_DELTA, latitude - MAP_BBOX_DELTA, longitude + MAP_BBOX_DELTA, latitude + MAP_BBOX_DELTA].join(',');
  return `https://www.openstreetmap.org/export/embed.html?bbox=${encodeURIComponent(bbox)}&layer=mapnik&marker=${latitude}%2C${longitude}`;
}

function buildOsmViewUrl(latitude: number, longitude: number): string {
  return `https://www.openstreetmap.org/?mlat=${latitude}&mlon=${longitude}#map=17/${latitude}/${longitude}`;
}

/**
 * F2/US2 — the details view's photo gallery. A broken image is dropped from
 * the carousel entirely (rather than shown as a broken-image icon); the
 * emoji placeholder only appears once every photo has failed or none exist.
 */
function PhotoCarousel({ images, name }: { images: string[]; name: string }) {
  const [items, setItems] = useState(images);
  const [index, setIndex] = useState(0);

  useEffect(() => {
    setItems(images);
    setIndex(0);
  }, [images]);

  function dropFailed(url: string) {
    setItems((prev) => {
      const next = prev.filter((u) => u !== url);
      setIndex((i) => Math.min(i, Math.max(next.length - 1, 0)));
      return next;
    });
  }

  if (items.length === 0) {
    return <div className="flex h-full w-full items-center justify-center bg-slate-100 text-6xl">🏛️</div>;
  }

  return (
    <>
      <img
        key={items[index]}
        src={items[index]}
        alt={items.length > 1 ? `${name} — photo ${index + 1} of ${items.length}` : name}
        onError={() => dropFailed(items[index])}
        className="h-full w-full object-cover"
      />
      {items.length > 1 && (
        <>
          <button
            type="button"
            aria-label="Previous photo"
            onClick={() => setIndex((i) => (i - 1 + items.length) % items.length)}
            className="absolute left-3 top-1/2 z-10 -translate-y-1/2 rounded-full bg-black/40 px-3 py-1.5 text-white hover:bg-black/60"
          >
            ‹
          </button>
          <button
            type="button"
            aria-label="Next photo"
            onClick={() => setIndex((i) => (i + 1) % items.length)}
            className="absolute right-3 top-1/2 z-10 -translate-y-1/2 rounded-full bg-black/40 px-3 py-1.5 text-white hover:bg-black/60"
          >
            ›
          </button>
          <div className="absolute top-3 left-1/2 z-10 flex -translate-x-1/2 gap-1.5">
            {items.map((url, i) => (
              <button
                key={url}
                type="button"
                aria-label={`Show photo ${i + 1} of ${items.length}`}
                onClick={() => setIndex(i)}
                className={`h-1.5 w-1.5 rounded-full ${i === index ? 'bg-white' : 'bg-white/40'}`}
              />
            ))}
          </div>
        </>
      )}
    </>
  );
}

/**
 * F2/US1, US2 & US4 — full destination details, opened from a card in the
 * search results. The view must still render with any optional field absent
 * (photo, address, website, opening hours) — see spec §11.3.
 */
export function DestinationDetailsPage() {
  const { providerId } = useParams<{ providerId: string }>();
  const navigate = useNavigate();
  // location.key === 'default' means this tab has no prior in-app history
  // (e.g. this page was loaded/refreshed directly) — navigate(-1) would be a
  // no-op then, so fall back to "/" instead of going nowhere.
  const location = useLocation();

  function backToSearch() {
    if (location.key === 'default') {
      navigate('/');
    } else {
      navigate(-1);
    }
  }

  const [details, setDetails] = useState<DestinationDetails | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!providerId) return;

    let ignore = false;
    setDetails(null);
    setError(null);
    getDestinationDetails(providerId)
      .then((result) => {
        if (!ignore) setDetails(result);
      })
      .catch((err) => {
        if (ignore) return;
        const notFound = getErrorStatus(err) === 404;
        setError(notFound ? 'Destination not found.' : 'Could not load this destination. Please try again.');
      });

    return () => {
      ignore = true;
    };
  }, [providerId]);

  if (error) {
    return (
      <Card className="mx-auto max-w-2xl">
        <p className="text-sm text-red-600">{error}</p>
        <button type="button" onClick={backToSearch} className="mt-2 inline-block text-sm text-brand-600 hover:underline">
          ← Back to search
        </button>
      </Card>
    );
  }

  if (!details) {
    return (
      <Card className="mx-auto max-w-2xl">
        <p className="text-sm text-slate-500">Loading destination…</p>
      </Card>
    );
  }

  // The backend always keeps imageUrl in sync as imageUrls[0] (or both empty),
  // so imageUrls alone is a complete gallery — no separate imageUrl fallback needed.
  const heroImages = details.imageUrls;

  const hasNearby = details.latitude != null && details.longitude != null;

  return (
    <div className="mx-auto max-w-5xl">
      <button type="button" onClick={backToSearch} className="text-sm text-brand-600 hover:underline">
        ← Back to search
      </button>

      <div className="relative mt-3 h-80 w-full overflow-hidden rounded-2xl sm:h-96">
        <PhotoCarousel images={heroImages} name={details.name} />
        <div className="pointer-events-none absolute inset-0 flex flex-col justify-end bg-gradient-to-t from-black/70 via-black/10 to-transparent p-6 text-white">
          <div className="pointer-events-auto flex flex-col gap-3 sm:flex-row sm:items-end sm:justify-between">
            <div>
              {details.category && (
                <span className="inline-block rounded-full bg-brand-600 px-3 py-1 text-xs font-semibold capitalize">
                  {details.category}
                </span>
              )}
              <h1 className="font-headline mt-2 text-3xl font-bold tracking-tight sm:text-4xl">{details.name}</h1>
            </div>
            <div className="w-full sm:w-auto">
              <AddToTripButton
                attraction={{
                  providerId: details.providerId,
                  name: details.name,
                  category: details.category,
                  imageUrl: details.imageUrl,
                  rating: null,
                }}
              />
            </div>
          </div>
        </div>
      </div>

      {details.description && <p className="mt-6 text-slate-700">{details.description}</p>}

      <Card padding="tight" className="mt-6">
        <h2 className="font-headline text-base font-semibold text-brand-600">Practical info</h2>
        <div className="mt-3 grid grid-cols-1 gap-6 lg:grid-cols-2">
          <dl className={`flex flex-col gap-3 text-sm ${hasNearby ? '' : 'lg:col-span-2'}`}>
            <div>
              <dt className="font-medium text-slate-500">Address</dt>
              <dd className="mt-0.5 text-slate-900">{details.address ?? 'Not available'}</dd>
            </div>
            <div>
              <dt className="font-medium text-slate-500">Opening hours</dt>
              <dd className="mt-0.5 text-slate-900">{details.openingHours ?? 'Opening hours not available'}</dd>
            </div>
            {details.website && (
              <div>
                <dt className="font-medium text-slate-500">Website</dt>
                <dd className="mt-0.5">
                  <a href={details.website} target="_blank" rel="noreferrer" className="text-brand-600 hover:underline">
                    {details.website}
                  </a>
                </dd>
              </div>
            )}
          </dl>

          {hasNearby && (
            <div>
              <div className="overflow-hidden rounded-lg border border-[#E2E8F0]">
                <iframe
                  title={`Map showing ${details.name}`}
                  src={buildOsmEmbedUrl(details.latitude!, details.longitude!)}
                  loading="lazy"
                  className="h-48 w-full border-0"
                />
              </div>
              <a
                href={buildOsmViewUrl(details.latitude!, details.longitude!)}
                target="_blank"
                rel="noreferrer"
                className="mt-1.5 inline-block text-xs text-brand-600 hover:underline"
              >
                View larger map
              </a>
            </div>
          )}
        </div>
      </Card>

      {hasNearby && (
        <NearbyAttractions
          latitude={details.latitude!}
          longitude={details.longitude!}
          excludeProviderId={details.providerId}
        />
      )}
    </div>
  );
}

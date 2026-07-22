import { useEffect, useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import axios from 'axios';
import { getDestinationDetails } from '../../api/destinations';
import { AddToTripButton } from './AddToTripButton';
import { Card } from '../../components/Card';
import { NearbyAttractions } from './NearbyAttractions';
import type { DestinationDetails } from '../../types';

/**
 * F2/US1, US2 & US4 — full destination details, opened from a card in the
 * search results. The view must still render with any optional field absent
 * (photo, address, website, opening hours) — see spec §11.3.
 */
export function DestinationDetailsPage() {
  const { providerId } = useParams<{ providerId: string }>();

  const [details, setDetails] = useState<DestinationDetails | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [imageFailed, setImageFailed] = useState(false);

  useEffect(() => {
    if (!providerId) return;

    let ignore = false;
    setDetails(null);
    setError(null);
    setImageFailed(false);
    getDestinationDetails(providerId)
      .then((result) => {
        if (!ignore) setDetails(result);
      })
      .catch((err) => {
        if (ignore) return;
        const notFound = axios.isAxiosError(err) && err.response?.status === 404;
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
        <Link to="/" className="mt-2 inline-block text-sm text-brand-600 hover:underline">
          ← Back to search
        </Link>
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

  const showImage = details.imageUrl && !imageFailed;

  return (
    <div className="mx-auto max-w-5xl">
      <Link to="/" className="text-sm text-brand-600 hover:underline">
        ← Back to search
      </Link>

      <div className="relative mt-3 h-80 w-full overflow-hidden rounded-2xl sm:h-96">
        {showImage ? (
          <img
            src={details.imageUrl!}
            alt={details.name}
            onError={() => setImageFailed(true)}
            className="h-full w-full object-cover"
          />
        ) : (
          <div className="flex h-full w-full items-center justify-center bg-slate-100 text-6xl">🏛️</div>
        )}
        <div className="absolute inset-0 flex flex-col justify-end bg-gradient-to-t from-black/70 via-black/10 to-transparent p-6 text-white">
          <div className="flex flex-col gap-3 sm:flex-row sm:items-end sm:justify-between">
            <div>
              {details.category && (
                <span className="inline-block rounded-full bg-brand-600 px-3 py-1 text-xs font-semibold">
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

      <div className="mt-6 grid grid-cols-1 gap-6 lg:grid-cols-3">
        <div className="lg:col-span-2">{details.description && <p className="text-slate-700">{details.description}</p>}</div>

        <Card padding="tight" className="h-fit">
          <h2 className="font-headline text-base font-semibold text-brand-600">Practical info</h2>
          <dl className="mt-3 flex flex-col gap-3 text-sm">
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
        </Card>
      </div>

      {details.latitude != null && details.longitude != null && (
        <div className="mt-8">
          <NearbyAttractions
            latitude={details.latitude}
            longitude={details.longitude}
            excludeProviderId={details.providerId}
          />
        </div>
      )}
    </div>
  );
}

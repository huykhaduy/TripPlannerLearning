import { useEffect, useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import axios from 'axios';
import { getDestinationDetails } from '../../api/destinations';
import { AddToTripButton } from './AddToTripButton';
import { Card } from '../../components/Card';
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
    <div className="mx-auto max-w-3xl">
      <Link to="/" className="text-sm text-brand-600 hover:underline">
        ← Back to search
      </Link>

      {showImage ? (
        <img
          src={details.imageUrl!}
          alt={details.name}
          onError={() => setImageFailed(true)}
          className="mt-3 h-80 w-full rounded-2xl object-cover"
        />
      ) : (
        <div className="mt-3 flex h-80 w-full items-center justify-center rounded-2xl bg-slate-100 text-6xl">
          🏛️
        </div>
      )}

      <div className="mt-6">
        <h1 className="text-3xl font-semibold tracking-tight text-slate-900">{details.name}</h1>
        {details.category && <p className="mt-1 text-sm text-slate-500">{details.category}</p>}
        {details.description && <p className="mt-4 text-slate-700">{details.description}</p>}

        <dl className="mt-6 flex flex-col gap-3 text-sm">
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

        <div className="mt-6 max-w-xs">
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
  );
}

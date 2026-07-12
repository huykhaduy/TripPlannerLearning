import { useEffect, useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import axios from 'axios';
import { getDestinationDetails } from '../../api/destinations';
import { AddToTripButton } from './AddToTripButton';
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
      <div className="rounded-xl border border-slate-200 bg-white p-6 shadow-sm sm:p-8">
        <p className="text-sm text-red-600">{error}</p>
        <Link to="/" className="mt-2 inline-block text-sm text-blue-600 hover:underline">
          ← Back to search
        </Link>
      </div>
    );
  }

  if (!details) {
    return (
      <div className="rounded-xl border border-slate-200 bg-white p-6 shadow-sm sm:p-8">
        <p className="text-sm text-slate-500">Loading destination…</p>
      </div>
    );
  }

  const showImage = details.imageUrl && !imageFailed;

  return (
    <div className="rounded-xl border border-slate-200 bg-white p-6 shadow-sm sm:p-8">
      <Link to="/" className="text-sm text-blue-600 hover:underline">
        ← Back to search
      </Link>

      {showImage ? (
        <img
          src={details.imageUrl!}
          alt={details.name}
          onError={() => setImageFailed(true)}
          className="mt-3 h-56 w-full rounded-lg object-cover"
        />
      ) : (
        <div className="mt-3 flex h-56 w-full items-center justify-center rounded-lg bg-slate-100 text-5xl">
          🏛️
        </div>
      )}

      <h1 className="mt-4 text-2xl font-bold">{details.name}</h1>
      {details.category && <p className="text-sm text-slate-500">{details.category}</p>}
      {details.description && <p className="mt-3">{details.description}</p>}

      <dl className="mt-4 flex flex-col gap-2 text-sm">
        <div>
          <dt className="inline font-medium text-slate-500">Address: </dt>
          <dd className="inline">{details.address ?? 'Not available'}</dd>
        </div>
        <div>
          <dt className="inline font-medium text-slate-500">Opening hours: </dt>
          <dd className="inline">{details.openingHours ?? 'Opening hours not available'}</dd>
        </div>
        {details.website && (
          <div>
            <dt className="inline font-medium text-slate-500">Website: </dt>
            <dd className="inline">
              <a
                href={details.website}
                target="_blank"
                rel="noreferrer"
                className="text-blue-600 hover:underline"
              >
                {details.website}
              </a>
            </dd>
          </div>
        )}
      </dl>

      <div className="mt-4">
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
  );
}

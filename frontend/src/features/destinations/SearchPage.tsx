import { Link } from 'react-router-dom';

/**
 * STUB — students implement destination search & details here (Features 1 & 2).
 *
 * This page is PUBLIC — users browse before logging in (F3/US8).
 * Suggested build order:
 *   1. Search input with autocomplete (GET /api/destinations/locations) — F1/US1-2.
 *   2. Recommended attractions list (GET /api/destinations/attractions) — F1/US3.
 *   3. Filters & sorting — F1/US4-5.
 *   4. Destination details view (GET /api/destinations/{providerId}) — F2.
 */
export function SearchPage() {
  return (
    <div className="card">
      <h1>Discover destinations</h1>
      <p className="todo">
        TODO: build search + recommended attractions here (Features 1 & 2). Call
        <code> GET /api/destinations/locations?query=… </code> to start.
      </p>
      <p>
        <Link to="/login">Log in</Link> to start planning a trip.
      </p>
    </div>
  );
}

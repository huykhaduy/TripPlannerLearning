import { useAuth } from '../../auth/AuthContext';

/**
 * STUB — students implement the Trip Planner UI here (Feature 3).
 *
 * Suggested build order:
 *   1. List the current user's trips (GET /api/trips).
 *   2. Create a trip (POST /api/trips) — US1.
 *   3. Open a trip and show day-by-day itinerary + Saved Places — US2, US10.
 *   4. Add / remove destinations — US3, US7.
 *   5. (Stretch) drag-and-drop scheduling & reordering — US4, US5, US6.
 *
 * The apiClient already attaches the JWT, so authenticated calls just work.
 */
export function TripsPage() {
  const { user, logout } = useAuth();

  return (
    <div className="card">
      <header className="row">
        <h1>My trips</h1>
        <button onClick={logout}>Log out</button>
      </header>
      <p>
        Signed in as <strong>{user?.email}</strong>.
      </p>
      <p className="todo">
        TODO: build the trip list and planner here (Feature 3). Call
        <code> GET /api/trips </code> to start.
      </p>
    </div>
  );
}

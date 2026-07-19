# Trip Planner Redesign (Stitch mockups) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.
>
> **Human note (this project):** the established working agreement for this
> repo is that the student writes the code themselves, one step at a time,
> with review after each step — see "Working agreement" under Global
> Constraints. If a human is driving this session interactively rather than
> an autonomous agent, present each task's steps and let them write the code;
> use this plan as the reviewer's checklist, not as something to execute for
> them.

**Goal:** Restyle the 4 trip-planning pages (Explore/Search, Destination
Details, My Trips, Trip Itinerary) plus the app shell to the Stitch mockups'
color/typography/layout system, per
[2026-07-19-trip-planner-redesign-design.md](../specs/2026-07-19-trip-planner-redesign-design.md),
without any backend change or new dependency.

**Architecture:** Repoint the existing Tailwind v4 `@theme` tokens in
`src/styles.css` to the new palette/fonts (everything referencing `brand-*`
picks this up for free), make small targeted edits to the 5 shared components,
then restyle each page in place. Two small pieces of genuinely new logic ride
along: a "Nearby experiences" section on the details page (reuses the
existing attractions endpoint) and a client-side Saved Places search filter.
Drag-and-drop, data fetching, and all API calls are untouched.

**Tech Stack:** React 19, TypeScript, Tailwind v4 (`@theme`, no config file),
react-router 7, axios. No test runner exists for the frontend — verification
is `npm run lint` (`tsc --noEmit`) plus manual checks in the browser preview,
consistent with the prior redesign pass's verification approach.

## Global Constraints

- No backend changes (no new endpoint, no new DTO field, no migration).
- No new npm dependencies (Google Fonts are loaded via a `<link>` tag, not a package).
- App name/logo stays **"✈️ Trip Planner"** — do not introduce "Voyager" anywhere in UI copy.
- Dark mode stays out of scope.
- No frontend test runner exists — each task's verification step is `npm run lint` + a manual check in the browser preview (per `mcp__Claude_Browser__*` tools), not an automated test.
- Every dropped/substituted mockup element listed in the design spec must actually be absent — no dead buttons wired to nothing.
- Working agreement: the student writes the code, one step at a time, with review after each step, in the task order below.

---

## Task 1: Design tokens (palette + fonts)

**Files:**
- Modify: `frontend/src/styles.css`
- Modify: `frontend/index.html`

**Interfaces:**
- Produces: repointed `--color-brand-{50..900}` (blue ramp), new
  `--color-action-{500,600}` (orange), `--color-tertiary-500` (teal), and
  `font-headline`/`font-body` Tailwind utility classes (from Tailwind v4's
  automatic `--font-*` → `font-*` mapping). Every later task's `bg-brand-*`,
  `text-brand-*`, `border-brand-*` usage — old and new — picks up the new
  hex values automatically; no other file needs to change color values it
  already expressed via `brand-*`.

- [ ] **Step 1: Replace the theme tokens**

Replace the full contents of `frontend/src/styles.css`:

```css
@import 'tailwindcss';

@theme {
  --color-brand-50: #eef2ff;
  --color-brand-100: #dbe1ff;
  --color-brand-200: #b5c4ff;
  --color-brand-300: #8fa6ff;
  --color-brand-400: #4a72e0;
  --color-brand-500: #1a56db;
  --color-brand-600: #003fb1;
  --color-brand-700: #003dab;
  --color-brand-800: #00174d;
  --color-brand-900: #00174d;

  --color-action-500: #fd761a;
  --color-action-600: #ea580c;

  --color-tertiary-500: #00544c;

  --font-headline: 'Hanken Grotesk', ui-sans-serif, system-ui, sans-serif;
  --font-body: 'Inter', ui-sans-serif, system-ui, sans-serif;
}

@layer base {
  html {
    color-scheme: light;
  }

  body {
    @apply antialiased;
    background-color: #f9f9ff;
    color: #151c27;
    font-family: var(--font-body);
  }

  h1,
  h2,
  h3 {
    font-family: var(--font-headline);
  }
}
```

- [ ] **Step 2: Load the Google Fonts**

In `frontend/index.html`, add font links inside `<head>` (title stays
`TripPlanner` — unchanged):

```html
<!doctype html>
<html lang="en">
  <head>
    <meta charset="UTF-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <link rel="preconnect" href="https://fonts.googleapis.com" />
    <link rel="preconnect" href="https://fonts.gstatic.com" crossorigin />
    <link
      href="https://fonts.googleapis.com/css2?family=Hanken+Grotesk:wght@600;700;800&family=Inter:wght@400;500;600;700&display=swap"
      rel="stylesheet"
    />
    <title>TripPlanner</title>
  </head>
  <body>
    <div id="root"></div>
    <script type="module" src="/src/main.tsx"></script>
  </body>
</html>
```

- [ ] **Step 3: Verify — lint**

Run: `cd frontend && npm run lint`
Expected: no errors (this is a CSS/HTML-only change; the gate should already pass).

- [ ] **Step 4: Verify — manual browser check**

Run: `npm run dev`, open the app in the browser preview.
- Confirm the page background is off-white/blue-tinted (`#f9f9ff`), not the old `slate-100` gray.
- Open any page with an `<h1>` (e.g. My Trips) and confirm the heading renders in a different typeface than body text (Hanken Grotesk vs Inter) — check via `javascript_tool`: `getComputedStyle(document.querySelector('h1')).fontFamily` should include `Hanken Grotesk`.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/styles.css frontend/index.html
git commit -m "style: repoint design tokens to the Stitch color/type system"
```

---

## Task 2: Shared components — action variant, flat cards, focus ring

**Files:**
- Modify: `frontend/src/components/Button.tsx`
- Modify: `frontend/src/components/Card.tsx`
- Modify: `frontend/src/components/Field.tsx`
- Modify: `frontend/src/features/destinations/AddToTripButton.tsx:50`

**Interfaces:**
- Consumes: `--color-action-500/600` from Task 1.
- Produces: `Button` gains `variant="action"` (orange — the one primary CTA
  per view), used by Tasks 4, 7, 8. `Card` is flat at rest with
  `hover:shadow-sm` and `rounded-lg` (was always-on `shadow-sm` +
  `rounded-2xl`) — every existing `<Card>` usage is unaffected structurally.
  `fieldControlClass` keeps its exported name/shape.

- [ ] **Step 1: Add the `action` button variant**

Replace `frontend/src/components/Button.tsx`:

```tsx
import type { ButtonHTMLAttributes } from 'react';

type ButtonVariant = 'primary' | 'action' | 'secondary' | 'outline' | 'danger';
type ButtonSize = 'md' | 'sm';

const VARIANT_CLASSES: Record<ButtonVariant, string> = {
  primary: 'bg-brand-600 text-white hover:bg-brand-700',
  // Orange — reserved for the one primary call-to-action per view (Add to
  // trip, Create trip, Plan new trip), per the Stitch design system.
  action: 'bg-action-500 text-white hover:bg-action-600',
  secondary: 'border border-slate-300 text-slate-700 hover:bg-slate-50',
  outline: 'border border-brand-200 text-brand-600 hover:bg-brand-50',
  danger: 'border border-red-200 text-red-600 hover:bg-red-50',
};

const SIZE_CLASSES: Record<ButtonSize, string> = {
  md: 'px-4 py-2',
  sm: 'px-3 py-1.5',
};

interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: ButtonVariant;
  size?: ButtonSize;
}

/**
 * Shared button styling. `variant` controls color, `size` controls padding;
 * type/onClick/disabled/children stay with the caller as plain <button> props.
 */
export function Button({ variant = 'primary', size = 'md', className = '', ...rest }: ButtonProps) {
  return (
    <button
      className={`rounded-lg text-sm font-medium transition-colors disabled:cursor-not-allowed disabled:opacity-60 ${VARIANT_CLASSES[variant]} ${SIZE_CLASSES[size]} ${className}`}
      {...rest}
    />
  );
}
```

- [ ] **Step 2: Flatten the card, use it as the CTA on the search results page**

Replace `frontend/src/components/Card.tsx`:

```tsx
import type { HTMLAttributes } from 'react';

type CardPadding = 'normal' | 'tight';

const PADDING_CLASSES: Record<CardPadding, string> = {
  normal: 'p-6 sm:p-8',
  tight: 'p-5',
};

interface CardProps extends HTMLAttributes<HTMLDivElement> {
  padding?: CardPadding;
}

/** The one card shell used for page content. Flat at rest, shadow only on hover (Stitch design system). */
export function Card({ padding = 'normal', className = '', ...rest }: CardProps) {
  return (
    <div
      className={`rounded-lg border border-[#E2E8F0] bg-white transition-shadow hover:shadow-sm ${PADDING_CLASSES[padding]} ${className}`}
      {...rest}
    />
  );
}
```

- [ ] **Step 3: Update the focus ring**

In `frontend/src/components/Field.tsx`, replace the `fieldControlClass` constant:

```tsx
export const fieldControlClass =
  'rounded-lg border border-slate-300 bg-white px-3 py-2 text-base text-slate-900 focus:border-brand-600 focus:outline-none focus:ring-2 focus:ring-brand-600/10';
```

- [ ] **Step 4: Make "Add to trip" the orange CTA**

In `frontend/src/features/destinations/AddToTripButton.tsx`, line 50, change:

```tsx
      <Button type="button" variant="outline" size="sm" onClick={handleClick} className="w-full">
```

to:

```tsx
      <Button type="button" variant="action" size="sm" onClick={handleClick} className="w-full">
```

- [ ] **Step 5: Verify — lint**

Run: `cd frontend && npm run lint`
Expected: no errors.

- [ ] **Step 6: Verify — manual browser check**

Run: `npm run dev`. On the Login page, confirm the card has a lighter border
and only shows a shadow on hover. On the Search page, search a city and
confirm "Add to trip" renders as a solid orange button.

- [ ] **Step 7: Commit**

```bash
git add frontend/src/components/Button.tsx frontend/src/components/Card.tsx frontend/src/components/Field.tsx frontend/src/features/destinations/AddToTripButton.tsx
git commit -m "style: add action button variant, flatten cards, tune focus ring"
```

---

## Task 3: App shell nav restyle

**Files:**
- Modify: `frontend/src/App.tsx`

**Interfaces:**
- Consumes: `Button` `variant="action"` (Task 2), `Avatar` (unchanged).
- Produces: no change to routes or `useAuth()` usage — later tasks don't depend on anything new here.

- [ ] **Step 1: Replace the app shell**

Replace the full contents of `frontend/src/App.tsx`:

```tsx
import { Navigate, Route, Routes, Link, NavLink } from 'react-router-dom';
import { useAuth } from './auth/AuthContext';
import { ProtectedRoute } from './auth/ProtectedRoute';
import { LoginPage } from './features/auth/LoginPage';
import { RegisterPage } from './features/auth/RegisterPage';
import { SearchPage } from './features/destinations/SearchPage';
import { DestinationDetailsPage } from './features/destinations/DestinationDetailsPage';
import { TripsPage } from './features/trips/TripsPage';
import { TripDetailPage } from './features/trips/TripDetailPage';
import { Avatar } from './components/Avatar';
import { Button } from './components/Button';

const navLinkClass = ({ isActive }: { isActive: boolean }) =>
  isActive
    ? 'border-b-2 border-brand-600 px-1 py-1 font-bold text-brand-600'
    : 'px-1 py-1 text-[#434654] hover:text-brand-600';

export default function App() {
  const { user, logout } = useAuth();

  return (
    <div className="min-h-screen bg-[#f9f9ff]">
      <nav className="sticky top-0 z-50 border-b border-[#E2E8F0] bg-white/95 backdrop-blur">
        <div className="mx-auto flex h-16 max-w-[1280px] flex-wrap items-center justify-between gap-3 px-4 sm:px-12">
          <div className="flex items-center gap-8">
            <Link to="/" className="font-headline text-xl font-bold text-brand-600">
              ✈️ Trip Planner
            </Link>
            <div className="hidden items-center gap-6 text-sm sm:flex">
              <NavLink to="/" className={navLinkClass} end>
                Explore
              </NavLink>
              {user && (
                <NavLink to="/trips" className={navLinkClass}>
                  My Trips
                </NavLink>
              )}
            </div>
          </div>
          <div className="flex flex-wrap items-center gap-4 text-sm">
            {user ? (
              <div className="flex items-center gap-2 rounded-full border border-[#E2E8F0] py-1 pl-1 pr-3">
                <Avatar label={user.displayName || user.email} />
                <span className="text-[#434654]">{user.displayName || user.email}</span>
                <button type="button" onClick={logout} className="text-[#434654] hover:text-brand-600">
                  Log out
                </button>
              </div>
            ) : (
              <>
                <NavLink to="/login" className={navLinkClass}>
                  Log in
                </NavLink>
                <Link to="/register">
                  <Button type="button" size="sm" variant="action">
                    Sign up
                  </Button>
                </Link>
              </>
            )}
          </div>
        </div>
      </nav>

      <main className="mx-auto max-w-[1280px] px-4 py-8 sm:px-12">
        <Routes>
          <Route path="/" element={<SearchPage />} />
          <Route path="/destinations/:providerId" element={<DestinationDetailsPage />} />
          <Route path="/login" element={<LoginPage />} />
          <Route path="/register" element={<RegisterPage />} />

          {/* Authenticated area (Feature 3). */}
          <Route element={<ProtectedRoute />}>
            <Route path="/trips" element={<TripsPage />} />
            <Route path="/trips/:tripId" element={<TripDetailPage />} />
          </Route>

          <Route path="*" element={<Navigate to="/" replace />} />
        </Routes>
      </main>
    </div>
  );
}
```

- [ ] **Step 2: Verify — lint**

Run: `cd frontend && npm run lint`
Expected: no errors.

- [ ] **Step 3: Verify — manual browser check**

Run: `npm run dev`. Confirm: logged out, nav shows "Explore" + "Log in" +
orange "Sign up"; log in, confirm "My Trips" appears and the avatar/name/"Log
out" pill shows on the right; click the active tab and confirm it gets a blue
underline.

- [ ] **Step 4: Commit**

```bash
git add frontend/src/App.tsx
git commit -m "style: restyle app shell nav to the Stitch layout"
```

---

## Task 4: Explore page — sidebar filters + hero restyle

**Files:**
- Create: `frontend/src/features/destinations/AttractionCard.tsx`
- Modify: `frontend/src/features/destinations/AttractionsList.tsx`
- Modify: `frontend/src/features/destinations/SearchPage.tsx`

**Interfaces:**
- Consumes: `AddToTripButton` (unchanged), `EmptyState`, `Button`, `fieldControlClass`.
- Produces: `AttractionCard` — a standalone component
  `{ attraction: AttractionSummary }` — consumed by Task 6's
  `NearbyAttractions`. No change to `AttractionsList`'s own props
  (`{ city: LocationSuggestion }`) or its internal filter/sort state — this
  task only restructures its JSX into a sidebar + grid layout.

- [ ] **Step 1: Extract `AttractionCard` into its own file**

Create `frontend/src/features/destinations/AttractionCard.tsx`:

```tsx
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
          {attraction.category && <span className="text-sm text-slate-500">{attraction.category}</span>}
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
```

- [ ] **Step 2: Restructure `AttractionsList` into a sidebar + grid layout**

In `frontend/src/features/destinations/AttractionsList.tsx`:

1. Remove the local `AttractionCard` function (lines 10-42) and add an import instead:

```tsx
import { useEffect, useMemo, useState } from 'react';
import { getAttractions } from '../../api/destinations';
import { AttractionCard } from './AttractionCard';
import { EmptyState } from '../../components/EmptyState';
import { fieldControlClass } from '../../components/Field';
import { Button } from '../../components/Button';
import type { AttractionSummary, LocationSuggestion } from '../../types';
```

(Drop the now-unused `Link` import — `AttractionCard` owns that now.)

2. Keep every hook/state/effect (`attractions`, `loading`, `error`,
   `categoryFilter`, `minRating`, `sortBy`, `categories`, `visible`,
   `hasActiveFilters`) exactly as-is. Only the `return` statement below the
   `if (attractions.length === 0)` guard changes. Replace it with:

```tsx
  return (
    <section className="flex flex-col gap-6 md:flex-row md:items-start">
      <aside className="w-full flex-shrink-0 rounded-lg border border-[#E2E8F0] bg-white p-4 md:w-64">
        <h3 className="font-headline text-base font-semibold text-brand-600">Filters</h3>

        <div className="mt-4">
          <p className="mb-2 text-xs font-semibold uppercase tracking-wide text-[#434654]">Category</p>
          <div className="flex flex-col gap-1">
            <button
              type="button"
              onClick={() => setCategoryFilter('')}
              className={`rounded-md px-2 py-1.5 text-left text-sm ${
                categoryFilter === '' ? 'bg-brand-50 font-semibold text-brand-600' : 'text-slate-700 hover:bg-slate-50'
              }`}
            >
              All destinations
            </button>
            {categories.map((category) => (
              <button
                key={category}
                type="button"
                onClick={() => setCategoryFilter(category)}
                className={`rounded-md px-2 py-1.5 text-left text-sm ${
                  categoryFilter === category
                    ? 'bg-brand-50 font-semibold text-brand-600'
                    : 'text-slate-700 hover:bg-slate-50'
                }`}
              >
                {category}
              </button>
            ))}
          </div>
        </div>

        <div className="mt-6">
          <p className="mb-2 text-xs font-semibold uppercase tracking-wide text-[#434654]">Minimum rating</p>
          <div className="flex flex-col gap-1">
            {(['', '3', '4'] as const).map((value) => (
              <button
                key={value || 'any'}
                type="button"
                onClick={() => setMinRating(value)}
                className={`rounded-md px-2 py-1.5 text-left text-sm ${
                  minRating === value ? 'bg-brand-50 font-semibold text-brand-600' : 'text-slate-700 hover:bg-slate-50'
                }`}
              >
                {value === '' ? 'Any rating' : `⭐ ${value}+ stars`}
              </button>
            ))}
          </div>
        </div>

        <label className="mt-6 flex flex-col gap-1.5 text-sm text-slate-500">
          Sort by
          <select
            value={sortBy}
            onChange={(e) => setSortBy(e.target.value as 'recommended' | 'rating')}
            className={fieldControlClass}
          >
            <option value="recommended">Recommended</option>
            <option value="rating">Highest rating</option>
          </select>
        </label>

        {hasActiveFilters && (
          <Button
            type="button"
            variant="secondary"
            size="sm"
            onClick={() => {
              setCategoryFilter('');
              setMinRating('');
            }}
            className="mt-4 w-full"
          >
            Clear filters
          </Button>
        )}
      </aside>

      <div className="flex-1">
        <h2 className="font-headline mb-4 text-lg font-semibold text-slate-900">Attractions near {city.name}</h2>
        {visible.length === 0 ? (
          <EmptyState message="No attractions match your filters." />
        ) : (
          <div className="grid gap-5 sm:grid-cols-2 xl:grid-cols-3">
            {visible.map((attraction) => (
              <AttractionCard key={attraction.providerId} attraction={attraction} />
            ))}
          </div>
        )}
      </div>
    </section>
  );
```

- [ ] **Step 3: Restyle the search hero**

In `frontend/src/features/destinations/SearchPage.tsx`, replace the hero `<div>`:

```tsx
      <div className="rounded-3xl bg-brand-600 px-6 py-14 text-center sm:py-20">
        <h1 className="font-headline text-4xl font-bold tracking-tight text-white sm:text-5xl">Where to next?</h1>
        <p className="mt-3 text-brand-100">Search a city to see its recommended attractions.</p>
        <div className="mx-auto mt-6 max-w-xl">
          <CitySearchInput onSelect={setSelectedCity} />
        </div>
      </div>
```

- [ ] **Step 4: Verify — lint**

Run: `cd frontend && npm run lint`
Expected: no errors (watch for the now-unused `Link` import in `AttractionsList.tsx` — it must be removed or `tsc` will flag it under most configs; if it doesn't, delete it anyway since it's dead).

- [ ] **Step 5: Verify — manual browser check**

Run: `npm run dev`. On the Explore page, search a city with results, confirm
a left "Filters" sidebar appears with category buttons and a rating list,
clicking one narrows the grid, "Clear filters" reappears/resets. Confirm the
hero renders with the new blue background and Hanken Grotesk heading.

- [ ] **Step 6: Commit**

```bash
git add frontend/src/features/destinations/AttractionCard.tsx frontend/src/features/destinations/AttractionsList.tsx frontend/src/features/destinations/SearchPage.tsx
git commit -m "style: move Explore filters into a sidebar, extract AttractionCard"
```

---

## Task 5: Destination Details page — hero/info restyle

**Files:**
- Modify: `frontend/src/features/destinations/DestinationDetailsPage.tsx`

**Interfaces:**
- Consumes: `Card`, `AddToTripButton` (unchanged).
- Produces: no new exports; Task 6 adds to this same file next.

- [ ] **Step 1: Replace the page**

Replace the full contents of `frontend/src/features/destinations/DestinationDetailsPage.tsx`:

```tsx
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
    </div>
  );
}
```

- [ ] **Step 2: Verify — lint**

Run: `cd frontend && npm run lint`
Expected: no errors.

- [ ] **Step 3: Verify — manual browser check**

Run: `npm run dev`. Open any destination's details page. Confirm the photo
renders full-bleed with a dark gradient overlay carrying the category pill,
title, and orange "Add to trip" button; confirm the practical-info card
(address/hours/website) renders in the right column; confirm a destination
with no photo/website/hours still renders cleanly (check the 🏛️ fallback and
"Not available"/"Opening hours not available" text).

- [ ] **Step 4: Commit**

```bash
git add frontend/src/features/destinations/DestinationDetailsPage.tsx
git commit -m "style: restyle destination details hero and practical-info card"
```

---

## Task 6: Destination Details page — Nearby experiences

**Files:**
- Create: `frontend/src/features/destinations/NearbyAttractions.tsx`
- Modify: `frontend/src/features/destinations/DestinationDetailsPage.tsx`

**Interfaces:**
- Consumes: `AttractionCard` (Task 4), `getAttractions(lat, lng, radiusKm)` from `frontend/src/api/destinations.ts` (unchanged, already used by `AttractionsList`).
- Produces: `NearbyAttractions` — `{ latitude: number; longitude: number; excludeProviderId: string }`, renders `null` when there's nothing to show (F2's "must still render with optional data missing" rule applies here too — the whole section just doesn't exist rather than showing an empty box).

- [ ] **Step 1: Create the component**

Create `frontend/src/features/destinations/NearbyAttractions.tsx`:

```tsx
import { useEffect, useState } from 'react';
import { getAttractions } from '../../api/destinations';
import { AttractionCard } from './AttractionCard';
import type { AttractionSummary } from '../../types';

const NEARBY_RADIUS_KM = 5;

/**
 * F2 — a compact "nearby experiences" strip on the details page. Reuses the
 * existing attractions endpoint centered on this destination's own
 * coordinates; hides itself entirely if there's nothing to show.
 */
export function NearbyAttractions({
  latitude,
  longitude,
  excludeProviderId,
}: {
  latitude: number;
  longitude: number;
  excludeProviderId: string;
}) {
  const [attractions, setAttractions] = useState<AttractionSummary[] | null>(null);

  useEffect(() => {
    let ignore = false;
    setAttractions(null);
    getAttractions(latitude, longitude, NEARBY_RADIUS_KM)
      .then((results) => {
        if (!ignore) setAttractions(results.filter((a) => a.providerId !== excludeProviderId));
      })
      .catch(() => {
        if (!ignore) setAttractions([]);
      });
    return () => {
      ignore = true;
    };
  }, [latitude, longitude, excludeProviderId]);

  if (!attractions || attractions.length === 0) return null;

  return (
    <section>
      <h2 className="font-headline mb-4 text-lg font-semibold text-slate-900">Nearby experiences</h2>
      <div className="grid gap-5 sm:grid-cols-2 lg:grid-cols-4">
        {attractions.slice(0, 4).map((attraction) => (
          <AttractionCard key={attraction.providerId} attraction={attraction} />
        ))}
      </div>
    </section>
  );
}
```

- [ ] **Step 2: Wire it into the details page**

In `frontend/src/features/destinations/DestinationDetailsPage.tsx`:

Add the import:

```tsx
import { NearbyAttractions } from './NearbyAttractions';
```

Add this block right after the closing `</div>` of the `grid grid-cols-1 gap-6 lg:grid-cols-3` block (i.e. as the last thing inside the outer `<div className="mx-auto max-w-5xl">`, after the `Practical info` `Card`'s enclosing grid):

```tsx
      {details.latitude != null && details.longitude != null && (
        <div className="mt-8">
          <NearbyAttractions
            latitude={details.latitude}
            longitude={details.longitude}
            excludeProviderId={details.providerId}
          />
        </div>
      )}
```

- [ ] **Step 3: Verify — lint**

Run: `cd frontend && npm run lint`
Expected: no errors.

- [ ] **Step 4: Verify — manual browser check**

Run: `npm run dev` + backend running. Open a destination with nearby POIs
within 5km and confirm a "Nearby experiences" section appears below the
practical info, excluding the destination itself; open one in a sparse area
and confirm the section is simply absent (no empty heading/box).

- [ ] **Step 5: Commit**

```bash
git add frontend/src/features/destinations/NearbyAttractions.tsx frontend/src/features/destinations/DestinationDetailsPage.tsx
git commit -m "feat: add Nearby experiences section to destination details"
```

---

## Task 7: My Trips page — card grid restyle

**Files:**
- Modify: `frontend/src/features/trips/TripsPage.tsx`

**Interfaces:**
- Produces: `getTripStatusLabel(startDate, endDate)` and `headerGradient(id)`
  — local pure helpers, not exported (nothing outside this file needs them).
  Task 8 builds directly on top of this file's next version.

- [ ] **Step 1: Replace the page**

Replace the full contents of `frontend/src/features/trips/TripsPage.tsx`:

```tsx
import { useEffect, useState, type FormEvent } from 'react';
import { Link } from 'react-router-dom';
import axios from 'axios';
import { createTrip, getMyTrips } from '../../api/trips';
import { Card } from '../../components/Card';
import { Field, fieldControlClass } from '../../components/Field';
import { Button } from '../../components/Button';
import { EmptyState } from '../../components/EmptyState';
import type { TripSummary } from '../../types';

function formatDates(startDate: string | null, endDate: string | null) {
  if (!startDate || !endDate) return 'No dates yet';
  return `${startDate} → ${endDate}`;
}

// F3/US10 — a relative status pill computed purely from the trip's own
// dates vs. today; there's no backend field for this.
function getTripStatusLabel(startDate: string | null, endDate: string | null): string | null {
  if (!startDate || !endDate) return null;
  const today = new Date().toISOString().slice(0, 10);
  if (today > endDate) return 'Past trip';
  if (today >= startDate) return 'In progress';
  const days = Math.ceil((new Date(startDate).getTime() - new Date(today).getTime()) / 86_400_000);
  return days <= 1 ? 'Tomorrow' : `In ${days} days`;
}

// Deterministic (hashed from the trip id), not random — so a card's header
// color is stable across reloads. No backend field backs this; it's purely
// decorative since there's no trip cover-photo feature.
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

/** F3/US1 & US10 — the current user's trip list plus a create-trip form. */
export function TripsPage() {
  const [trips, setTrips] = useState<TripSummary[] | null>(null);
  const [loadError, setLoadError] = useState<string | null>(null);

  const [name, setName] = useState('');
  const [creating, setCreating] = useState(false);
  const [createError, setCreateError] = useState<string | null>(null);

  useEffect(() => {
    getMyTrips()
      .then(setTrips)
      .catch(() => setLoadError('Could not load your trips. Please try again.'));
  }, []);

  async function handleCreate(event: FormEvent) {
    event.preventDefault();
    setCreateError(null);
    setCreating(true);
    try {
      const trip = await createTrip(name.trim());
      setTrips((current) => [...(current ?? []), trip]);
      setName('');
    } catch (err) {
      const message = axios.isAxiosError(err)
        ? (err.response?.data?.detail ?? 'Could not create the trip.')
        : 'Could not create the trip.';
      setCreateError(message);
    } finally {
      setCreating(false);
    }
  }

  return (
    <div className="flex flex-col gap-8">
      <div>
        <h1 className="font-headline text-3xl font-bold tracking-tight text-slate-900">My Trips</h1>
        <p className="text-slate-500">Manage and view all your upcoming journeys.</p>
      </div>

      <form onSubmit={handleCreate} className="flex flex-wrap items-end gap-3">
        <div className="min-w-64 flex-1">
          <Field label="New trip">
            <input
              value={name}
              onChange={(e) => setName(e.target.value)}
              placeholder="e.g. Summer in Da Nang"
              required
              className={fieldControlClass}
            />
          </Field>
        </div>
        <Button type="submit" variant="action" disabled={creating || name.trim() === ''}>
          {creating ? 'Creating…' : 'Create trip'}
        </Button>
      </form>
      {createError && <p className="text-sm text-red-600">{createError}</p>}

      {loadError ? (
        <p className="text-sm text-red-600">{loadError}</p>
      ) : trips === null ? (
        <p className="text-sm text-slate-500">Loading your trips…</p>
      ) : trips.length === 0 ? (
        <EmptyState icon="🗺️" message="No trips yet — create your first one above." />
      ) : (
        <div className="grid gap-5 sm:grid-cols-2 xl:grid-cols-3">
          {trips.map((trip) => {
            const status = getTripStatusLabel(trip.startDate, trip.endDate);
            return (
              <Link key={trip.id} to={`/trips/${trip.id}`}>
                <Card padding="tight" className="flex h-full flex-col gap-0 overflow-hidden p-0">
                  <div className={`relative flex h-24 items-end bg-gradient-to-br p-3 ${headerGradient(trip.id)}`}>
                    {status && (
                      <span className="absolute right-3 top-3 rounded-full bg-white/90 px-3 py-1 text-xs font-bold text-slate-900">
                        {status}
                      </span>
                    )}
                  </div>
                  <div className="flex flex-1 flex-col gap-1 p-4">
                    <strong className="font-headline text-slate-900">{trip.name}</strong>
                    <span className="text-sm text-slate-500">{formatDates(trip.startDate, trip.endDate)}</span>
                    <span className="mt-auto pt-3 text-sm font-semibold text-slate-500">
                      {trip.destinationCount} destination{trip.destinationCount === 1 ? '' : 's'}
                    </span>
                  </div>
                </Card>
              </Link>
            );
          })}
        </div>
      )}
    </div>
  );
}
```

- [ ] **Step 2: Verify — lint**

Run: `cd frontend && npm run lint`
Expected: no errors.

- [ ] **Step 3: Verify — manual browser check**

Run: `npm run dev`. On My Trips with ≥2 trips having different date ranges
(one past, one future), confirm each card shows a colored gradient header
with a status pill ("Past trip" / "In N days"), and a trip with no dates set
shows no pill. Confirm the destination count matches what's on the trip.

- [ ] **Step 4: Commit**

```bash
git add frontend/src/features/trips/TripsPage.tsx
git commit -m "style: restyle My Trips into a card grid with status pills"
```

---

## Task 8: My Trips page — create-trip modal

**Files:**
- Modify: `frontend/src/features/trips/TripsPage.tsx`

**Interfaces:**
- Consumes: the `handleCreate`/`name`/`creating`/`createError` state from Task 7 (unchanged logic, just relocated into a modal).

- [ ] **Step 1: Add modal state and trigger button**

In `frontend/src/features/trips/TripsPage.tsx`, add state next to the existing `createError` state:

```tsx
  const [modalOpen, setModalOpen] = useState(false);
```

Replace the header block:

```tsx
      <div>
        <h1 className="font-headline text-3xl font-bold tracking-tight text-slate-900">My Trips</h1>
        <p className="text-slate-500">Manage and view all your upcoming journeys.</p>
      </div>
```

with:

```tsx
      <div className="flex flex-wrap items-center justify-between gap-4">
        <div>
          <h1 className="font-headline text-3xl font-bold tracking-tight text-slate-900">My Trips</h1>
          <p className="text-slate-500">Manage and view all your upcoming journeys.</p>
        </div>
        <Button type="button" variant="action" onClick={() => setModalOpen(true)}>
          + Plan new trip
        </Button>
      </div>
```

- [ ] **Step 2: Move the create-trip form into a modal**

Remove the standalone `<form onSubmit={handleCreate} ...>...</form>` block
and the `{createError && ...}` line right after it (they move inside the
modal below). In their place — right after the header block, before the
trips-list conditional — add:

```tsx
      {modalOpen && (
        <div
          className="fixed inset-0 z-50 flex items-center justify-center bg-slate-900/40 p-4"
          onClick={() => setModalOpen(false)}
        >
          <div
            className="w-full max-w-lg rounded-2xl bg-white shadow-2xl"
            onClick={(e) => e.stopPropagation()}
            role="dialog"
            aria-label="Plan a new trip"
          >
            <div className="flex items-center justify-between border-b border-[#E2E8F0] px-6 py-4">
              <h2 className="font-headline text-lg font-semibold text-slate-900">Plan new trip</h2>
              <button
                type="button"
                onClick={() => setModalOpen(false)}
                className="text-slate-400 hover:text-slate-600"
                aria-label="Close"
              >
                ✕
              </button>
            </div>
            <form onSubmit={handleCreate} className="flex flex-col gap-4 p-6">
              <Field label="Trip name">
                <input
                  value={name}
                  onChange={(e) => setName(e.target.value)}
                  placeholder="e.g. Summer in Da Nang"
                  required
                  className={fieldControlClass}
                />
              </Field>
              {createError && <p className="text-sm text-red-600">{createError}</p>}
              <div className="flex justify-end gap-2 pt-2">
                <Button type="button" variant="secondary" onClick={() => setModalOpen(false)}>
                  Cancel
                </Button>
                <Button type="submit" variant="action" disabled={creating || name.trim() === ''}>
                  {creating ? 'Creating…' : 'Create trip'}
                </Button>
              </div>
            </form>
          </div>
        </div>
      )}
```

- [ ] **Step 3: Close the modal on success**

Update `handleCreate` to close the modal after a successful create:

```tsx
  async function handleCreate(event: FormEvent) {
    event.preventDefault();
    setCreateError(null);
    setCreating(true);
    try {
      const trip = await createTrip(name.trim());
      setTrips((current) => [...(current ?? []), trip]);
      setName('');
      setModalOpen(false);
    } catch (err) {
      const message = axios.isAxiosError(err)
        ? (err.response?.data?.detail ?? 'Could not create the trip.')
        : 'Could not create the trip.';
      setCreateError(message);
    } finally {
      setCreating(false);
    }
  }
```

- [ ] **Step 4: Verify — lint**

Run: `cd frontend && npm run lint`
Expected: no errors.

- [ ] **Step 5: Verify — manual browser check**

Run: `npm run dev`. Click "+ Plan new trip", confirm the modal opens;
click outside the modal panel, confirm it closes; reopen, submit a valid
name, confirm the modal closes and the new trip appears in the grid;
reopen and submit an empty name, confirm the submit button stays disabled.

- [ ] **Step 6: Commit**

```bash
git add frontend/src/features/trips/TripsPage.tsx
git commit -m "feat: convert My Trips create-trip form into a modal"
```

---

## Task 9: Trip Itinerary page — Saved Places sidebar + search filter

**Files:**
- Modify: `frontend/src/features/trips/TripDetailPage.tsx`

**Interfaces:**
- Produces: `DestinationList` gains an optional `isMatch?: (destination: TripDestination) => boolean` prop. Rows failing `isMatch` are hidden with CSS (`hidden` class), **not** removed from the array passed in — this keeps `index`/`destinations.length`-based drop-position math correct regardless of what's currently filtered. Task 10 continues editing this same function/file.

- [ ] **Step 1: Add the `isMatch` prop to `DestinationList`**

In `frontend/src/features/trips/TripDetailPage.tsx`, replace the
`DestinationList` function (originally lines 31-94):

```tsx
function DestinationList({
  dayId,
  destinations,
  emptyHint,
  onRemove,
  removingItemId,
  onDragStart,
  onDragEnd,
  onDrop,
  isMatch,
}: {
  dayId: string | null;
  destinations: TripDestination[];
  emptyHint: string;
  onRemove: (itemId: string) => void;
  removingItemId: string | null;
  onDragStart: (itemId: string) => void;
  onDragEnd: () => void;
  onDrop: (targetDayId: string | null, position: number) => void;
  // Optional visual-only filter (the Saved Places search box). Rows are
  // hidden with CSS rather than removed from the array so index-based drop
  // positions stay correct regardless of what's currently filtered out.
  isMatch?: (destination: TripDestination) => boolean;
}) {
  const anyVisible = !isMatch || destinations.length === 0 || destinations.some(isMatch);

  return (
    <div
      onDragOver={(e) => e.preventDefault()} // required, or the browser refuses the drop
      onDrop={() => onDrop(dayId, destinations.length)}
    >
      {destinations.length === 0 ? (
        <p className="mt-3 rounded-xl border-2 border-dashed border-slate-300 bg-slate-50 px-4 py-6 text-center text-sm text-slate-500">
          {emptyHint}
        </p>
      ) : !anyVisible ? (
        <p className="mt-3 rounded-xl border-2 border-dashed border-slate-300 bg-slate-50 px-4 py-6 text-center text-sm text-slate-500">
          No matches.
        </p>
      ) : (
        <ul className="mt-3 flex flex-col gap-2">
          {destinations.map((destination, index) => (
            <li
              key={destination.itemId}
              draggable
              onDragStart={() => onDragStart(destination.itemId)}
              onDragEnd={onDragEnd}
              onDragOver={(e) => e.preventDefault()}
              onDrop={(e) => {
                e.stopPropagation(); // this drop is ours — don't also append via the list handler
                onDrop(dayId, index);
              }}
              className={`flex cursor-grab items-center gap-3 rounded-lg border border-[#E2E8F0] bg-white px-3 py-2 active:cursor-grabbing ${
                isMatch && !isMatch(destination) ? 'hidden' : ''
              }`}
            >
              <span className="select-none text-slate-400" aria-hidden="true">
                ⠿
              </span>
              <DestinationThumbnail imageUrl={destination.imageUrl} name={destination.name} />
              <span className="flex-1 text-slate-900">{destination.name}</span>
              <Button
                type="button"
                variant="danger"
                size="sm"
                onClick={() => onRemove(destination.itemId)}
                disabled={removingItemId === destination.itemId}
              >
                {removingItemId === destination.itemId ? 'Removing…' : 'Remove'}
              </Button>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
```

- [ ] **Step 2: Add the search box state**

Add next to the other `useState` declarations in `TripDetailPage`:

```tsx
  const [savedPlacesQuery, setSavedPlacesQuery] = useState('');
```

- [ ] **Step 3: Restyle Saved Places into a sidebar panel with search**

Replace the `savedPlacesSection` definition:

```tsx
  const savedPlacesSection = (
    <section className="flex h-full flex-col rounded-lg border border-[#E2E8F0] bg-white p-4">
      <h2 className="font-headline text-base font-semibold text-brand-600">Saved Places</h2>
      <p className="text-xs text-slate-500">Drag items into a day to schedule them.</p>
      <input
        type="search"
        value={savedPlacesQuery}
        onChange={(e) => setSavedPlacesQuery(e.target.value)}
        placeholder="Search saved…"
        aria-label="Search saved places"
        className="mt-3 rounded-lg border border-slate-300 bg-slate-50 px-3 py-2 text-sm focus:border-brand-600 focus:outline-none focus:ring-2 focus:ring-brand-600/10"
      />
      <div className="mt-3 flex-1 overflow-y-auto">
        <DestinationList
          dayId={null}
          destinations={trip.savedPlaces}
          emptyHint="No saved places — add destinations from the Discover page."
          onRemove={handleRemove}
          removingItemId={removingItemId}
          onDragStart={setDragItemId}
          onDragEnd={() => setDragItemId(null)}
          onDrop={handleDrop}
          isMatch={
            savedPlacesQuery.trim() === ''
              ? undefined
              : (d) => d.name.toLowerCase().includes(savedPlacesQuery.trim().toLowerCase())
          }
        />
      </div>
    </section>
  );
```

- [ ] **Step 4: Verify — lint**

Run: `cd frontend && npm run lint`
Expected: no errors.

- [ ] **Step 5: Verify — manual browser check**

Run: `npm run dev` + backend running. Open a trip with ≥3 saved places.
Type a substring matching one item — confirm only it stays visible and the
rest disappear. Clear the box — confirm all reappear. With the filter
active, **drag the still-visible item into a day** and confirm it lands
correctly (this is the case that would break if filtering had removed items
from the array instead of just hiding them). Type a query matching nothing
and confirm "No matches." appears while the day-drop-zones are still
functional (drag a day item back to Saved Places while the search box has
unrelated text in it — it should still land in Saved Places, not error).

- [ ] **Step 6: Commit**

```bash
git add frontend/src/features/trips/TripDetailPage.tsx
git commit -m "feat: restyle Saved Places into a sidebar with a search filter"
```

---

## Task 10: Trip Itinerary page — day columns layout + row restyle + save status

**Files:**
- Modify: `frontend/src/features/trips/TripDetailPage.tsx`

**Interfaces:**
- Consumes: `DestinationList` with `isMatch` (Task 9).
- Produces: no new exports; this is the final task in the plan.

- [ ] **Step 1: Add a save-status label**

In the `TripDetailPage` function body, right after `const hasDays = trip.days.length > 0;`, add:

```tsx
  const isDirty =
    name !== trip.name || startDate !== (trip.startDate ?? '') || endDate !== (trip.endDate ?? '');
  const saveStatusLabel = saving ? 'Saving…' : saveError ? 'Error saving' : isDirty ? 'Unsaved changes' : 'All changes saved';
```

- [ ] **Step 2: Show the status next to the trip name**

Replace the page header block:

```tsx
      <div>
        <Link to="/trips" className="text-sm text-brand-600 hover:underline">
          ← Back to my trips
        </Link>
        <h1 className="mt-2 text-3xl font-semibold tracking-tight text-slate-900">{trip.name}</h1>
      </div>
```

with:

```tsx
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <Link to="/trips" className="text-sm text-brand-600 hover:underline">
            ← Back to my trips
          </Link>
          <h1 className="font-headline mt-2 text-3xl font-bold tracking-tight text-slate-900">{trip.name}</h1>
        </div>
        <span
          className={`flex items-center gap-2 rounded-full px-3 py-1.5 text-xs font-semibold ${
            saveError ? 'bg-red-50 text-red-600' : 'bg-brand-50 text-brand-600'
          }`}
        >
          <span aria-hidden="true">{saving ? '⏳' : '☁️'}</span>
          {saveStatusLabel}
        </span>
      </div>
```

- [ ] **Step 3: Switch to horizontal-scrolling day columns**

Replace the `{hasDays ? (...) : (...)}` block:

```tsx
      {hasDays ? (
        <div className="flex flex-col gap-6 lg:flex-row lg:items-start">
          <div className="lg:sticky lg:top-8 lg:w-72 lg:shrink-0">{savedPlacesSection}</div>

          <div className="flex flex-1 gap-6 overflow-x-auto pb-4">
            {trip.days.map((day) => (
              <section key={day.id} className="flex w-72 shrink-0 flex-col gap-3 rounded-lg bg-[#F8FAFC] p-3">
                <h2 className="font-headline text-base font-semibold text-slate-900">
                  Day {day.dayNumber} <span className="font-normal text-slate-500">{day.date}</span>
                </h2>
                <DestinationList
                  dayId={day.id}
                  destinations={day.destinations}
                  emptyHint="Nothing planned yet — drag a destination here."
                  onRemove={handleRemove}
                  removingItemId={removingItemId}
                  onDragStart={setDragItemId}
                  onDragEnd={() => setDragItemId(null)}
                  onDrop={handleDrop}
                />
              </section>
            ))}
          </div>
        </div>
      ) : (
        <>
          <p className="text-sm text-slate-500">Set the trip dates to generate a day-by-day itinerary.</p>
          {savedPlacesSection}
        </>
      )}
```

- [ ] **Step 4: Restyle the row — hover-reveal remove icon**

Inside `DestinationList`'s `<ul>` (the version from Task 9), replace the `<li>`:

```tsx
            <li
              key={destination.itemId}
              draggable
              onDragStart={() => onDragStart(destination.itemId)}
              onDragEnd={onDragEnd}
              onDragOver={(e) => e.preventDefault()}
              onDrop={(e) => {
                e.stopPropagation();
                onDrop(dayId, index);
              }}
              className={`group relative flex cursor-grab items-center gap-3 rounded-lg border border-[#E2E8F0] bg-white px-3 py-2 shadow-sm active:cursor-grabbing ${
                isMatch && !isMatch(destination) ? 'hidden' : ''
              }`}
            >
              <span className="select-none text-slate-400" aria-hidden="true">
                ⠿
              </span>
              <DestinationThumbnail imageUrl={destination.imageUrl} name={destination.name} />
              <span className="flex-1 truncate text-slate-900">{destination.name}</span>
              {/* Icon-only affordance matching the mockup's hover-reveal "×" —
                  distinct enough from the shared Button's variants that a
                  plain <button> reads better here than forcing a Button variant. */}
              <button
                type="button"
                onClick={() => onRemove(destination.itemId)}
                disabled={removingItemId === destination.itemId}
                aria-label={`Remove ${destination.name}`}
                className="rounded-full p-1 text-slate-300 opacity-0 transition-opacity hover:bg-red-50 hover:text-red-600 disabled:opacity-100 group-hover:opacity-100"
              >
                {removingItemId === destination.itemId ? '…' : '✕'}
              </button>
            </li>
```

- [ ] **Step 5: Verify — lint**

Run: `cd frontend && npm run lint`
Expected: no errors.

- [ ] **Step 6: Verify — manual browser check**

Run: `npm run dev` + backend running. Open a trip with ≥2 days and items in
both Saved Places and at least one day. Confirm: day columns sit side by
side and scroll horizontally if there are more than fit on screen; Saved
Places stays as a sticky left sidebar; hovering a destination row reveals a
"✕" that removes it (with the existing confirm dialog); dragging a saved
place into a day, reordering within a day, and moving between days all still
work exactly as before. Confirm the header pill reads "All changes saved" at
rest, "Unsaved changes" after editing the name/dates fields but before
saving, and "Saving…" while the save request is in flight.

- [ ] **Step 7: Commit**

```bash
git add frontend/src/features/trips/TripDetailPage.tsx
git commit -m "style: day-column itinerary layout, hover-remove rows, save status"
```

---

## Plan self-review notes

- **Spec coverage:** every kept/added/dropped/substituted item in
  [2026-07-19-trip-planner-redesign-design.md](../specs/2026-07-19-trip-planner-redesign-design.md)
  maps to a task above. Dropped items (AI promo, favorites, booking,
  carousel/gallery, bento cards, pricing, community tip, map, per-day menu,
  "Add Another Day", per-item category/rating tags, user photo) require no
  task — they simply never get built, and no task references them.
- **Correctness check performed during planning:** the Saved Places search
  filter (Task 9) could have silently broken drag-and-drop position math if
  it filtered the array before passing it to `DestinationList` — resolved by
  keeping the full array and hiding non-matching rows with CSS instead
  (`isMatch` prop), verified explicitly in Task 9 Step 5's manual check.
- **Type consistency:** `AttractionCard`'s prop shape
  (`{ attraction: AttractionSummary }`) is identical everywhere it's used
  (Task 4's `AttractionsList`, Task 6's `NearbyAttractions`).
  `DestinationList`'s `isMatch` prop is introduced once (Task 9) and consumed
  as-is by Task 10 without renaming.

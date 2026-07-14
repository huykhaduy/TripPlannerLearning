# Frontend Visual Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Restyle every existing frontend page (Discover, destination details, login, register, trips list, trip detail) plus the app header into a consistent "clean & modern minimal" look, with an indigo/violet accent, a wider layout, and login status visible in the header — with zero change to data flow, routing, validation, or drag-and-drop behavior.

**Architecture:** Introduce a small shared UI kit (`Button`, `Field`, `Card`, `EmptyState`, `Avatar`) in `src/components/`, plus indigo/violet design tokens added to `src/styles.css` via Tailwind v4's `@theme`. Every page is then restyled to use these primitives instead of its own copy-pasted Tailwind class strings. Business logic (state, effects, API calls, event handlers) in every file is copied verbatim — only JSX markup/className changes.

**Tech Stack:** React 19, Tailwind v4 (`@tailwindcss/vite`), react-router 7, axios, TypeScript 5 (strict, `noUnusedLocals`/`noUnusedParameters` on). No new npm dependencies.

## Global Constraints

- Visual-only change: no backend changes, no new features, no change to API calls, validation, routing, or the `TripDetailPage` drag-and-drop mechanics (handlers/state/logic must be copied verbatim).
- No new npm dependencies — Tailwind v4 + existing stack only.
- Dark mode is out of scope (light only).
- Header account UI is an always-visible inline pill (name/email + "Log out") — no dropdown/menu.
- No mobile hamburger nav (nav wraps at narrow widths instead).
- `npm run lint` (`tsc --noEmit`, strict mode) must pass after every task — this project has no other frontend test runner.
- Accent color is an indigo/violet `brand-*` Tailwind scale replacing `blue-*` everywhere.
- One card radius (`rounded-2xl`) and shadow used sparingly (photos/popovers/modals), not on every plain content box.
- Page shell width is `max-w-6xl` (up from `max-w-2xl`).

---

## File Structure

**Create:**
- `src/components/Button.tsx` — shared button (variant/size props)
- `src/components/Field.tsx` — label+control wrapper + shared `fieldControlClass`
- `src/components/Card.tsx` — shared card shell
- `src/components/EmptyState.tsx` — shared "nothing here" placeholder
- `src/components/Avatar.tsx` — initial-in-circle badge for the header pill

**Modify:**
- `src/styles.css` — add `brand-*` theme tokens, adjust body background
- `src/App.tsx` — wider shell, restyled nav, header account pill/sign-up button
- `src/features/auth/LoginPage.tsx`
- `src/features/auth/RegisterPage.tsx`
- `src/features/destinations/SearchPage.tsx`
- `src/features/destinations/CitySearchInput.tsx`
- `src/features/destinations/AttractionsList.tsx`
- `src/features/destinations/AddToTripButton.tsx`
- `src/features/destinations/DestinationDetailsPage.tsx`
- `src/features/trips/TripsPage.tsx`
- `src/features/trips/TripDetailPage.tsx`

No files are deleted. No changes to `src/api/*`, `src/auth/*`, `src/types.ts`, `src/hooks/*`, or any backend code.

---

### Task 1: Design tokens

**Files:**
- Modify: `frontend/src/styles.css`

**Interfaces:**
- Produces: Tailwind utility classes `bg-brand-50` … `bg-brand-900` (and the `text-brand-*`/`border-brand-*`/`ring-brand-*` equivalents Tailwind generates automatically from the same theme color), usable by every later task.

- [x] **Step 1: Replace the file contents**

Replace the entire contents of `frontend/src/styles.css` with:

```css
@import 'tailwindcss';

@theme {
  --color-brand-50: #f5f3ff;
  --color-brand-100: #ede9fe;
  --color-brand-200: #ddd6fe;
  --color-brand-300: #c4b5fd;
  --color-brand-400: #a78bfa;
  --color-brand-500: #8b5cf6;
  --color-brand-600: #6d5bd0;
  --color-brand-700: #5b48b8;
  --color-brand-800: #453679;
  --color-brand-900: #362a5e;
}

@layer base {
  body {
    @apply bg-slate-50 text-slate-900 antialiased;
  }
}
```

- [x] **Step 2: Verify the app still builds and the background is visibly lighter**

Run: `cd frontend && npm run lint`
Expected: exits 0, no TypeScript errors (this is a CSS-only change, so this mainly confirms nothing else broke).

Run: `npm run dev`, open the app in a browser.
Expected: page loads with no console errors; body background is a very light slate (`#f8fafc`), slightly lighter than before — no layout change yet since nothing consumes `brand-*` colors until later tasks.

- [ ] **Step 3: Commit**

```bash
git add frontend/src/styles.css
git commit -m "style: add indigo/violet brand color tokens"
```

---

### Task 2: Shared UI primitives

**Files:**
- Create: `frontend/src/components/Button.tsx`
- Create: `frontend/src/components/Field.tsx`
- Create: `frontend/src/components/Card.tsx`
- Create: `frontend/src/components/EmptyState.tsx`
- Create: `frontend/src/components/Avatar.tsx`

**Interfaces:**
- Consumes: `brand-*` color tokens from Task 1.
- Produces:
  - `Button({ variant?: 'primary' | 'secondary' | 'outline' | 'danger', size?: 'md' | 'sm', ...ButtonHTMLAttributes })`
  - `Field({ label: string, children: ReactNode, className?: string })` and `fieldControlClass: string`
  - `Card({ padding?: 'normal' | 'tight', ...HTMLAttributes<HTMLDivElement> })`
  - `EmptyState({ icon?: string, message: string })`
  - `Avatar({ label: string })`

These are presentational only — no state, no API calls. All later tasks import from these exact names.

- [x] **Step 1: Create `Button.tsx`**

```tsx
import type { ButtonHTMLAttributes } from 'react';

type ButtonVariant = 'primary' | 'secondary' | 'outline' | 'danger';
type ButtonSize = 'md' | 'sm';

const VARIANT_CLASSES: Record<ButtonVariant, string> = {
  primary: 'bg-brand-600 text-white hover:bg-brand-700',
  secondary: 'border border-slate-300 text-slate-700 hover:bg-slate-50',
  // Brand-tinted outline — for calls to action that need to stand out (e.g.
  // "Add to trip") without the visual weight of a filled primary button.
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

- [x] **Step 2: Create `Field.tsx`**

```tsx
import type { ReactNode } from 'react';

/** Shared input/select styling — apply to the actual <input>/<select> inside a Field. */
export const fieldControlClass =
  'rounded-lg border border-slate-300 px-3 py-2 text-base text-slate-900 focus:border-brand-500 focus:outline-none focus:ring-2 focus:ring-brand-200';

interface FieldProps {
  label: string;
  children: ReactNode;
  className?: string;
}

/** Label + control wrapper matching the shared form field look across the app. */
export function Field({ label, children, className = '' }: FieldProps) {
  return (
    <label className={`flex flex-col gap-1.5 text-sm text-slate-500 ${className}`}>
      {label}
      {children}
    </label>
  );
}
```

- [x] **Step 3: Create `Card.tsx`**

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

/** The one card shell used for page content, replacing the repeated border/shadow wrapper. */
export function Card({ padding = 'normal', className = '', ...rest }: CardProps) {
  return (
    <div
      className={`rounded-2xl border border-slate-200 bg-white ${PADDING_CLASSES[padding]} ${className}`}
      {...rest}
    />
  );
}
```

- [x] **Step 4: Create `EmptyState.tsx`**

```tsx
interface EmptyStateProps {
  icon?: string;
  message: string;
}

/** Centered placeholder for "nothing here yet" states (empty lists, no results). */
export function EmptyState({ icon = '🧭', message }: EmptyStateProps) {
  return (
    <div className="flex flex-col items-center gap-2 rounded-xl border border-dashed border-slate-300 px-4 py-8 text-center">
      <span className="text-3xl" aria-hidden="true">
        {icon}
      </span>
      <p className="text-sm text-slate-500">{message}</p>
    </div>
  );
}
```

- [x] **Step 5: Create `Avatar.tsx`**

```tsx
interface AvatarProps {
  label: string;
}

/** Small circular initial badge, e.g. for the header account pill. */
export function Avatar({ label }: AvatarProps) {
  const initial = label.trim().charAt(0).toUpperCase() || '?';
  return (
    <span className="flex h-7 w-7 items-center justify-center rounded-full bg-brand-100 text-sm font-semibold text-brand-700">
      {initial}
    </span>
  );
}
```

- [x] **Step 6: Verify**

Run: `cd frontend && npm run lint`
Expected: exits 0. (Nothing imports these yet, so this only confirms the 5 new files themselves type-check — each is self-contained with no unused imports.)

- [ ] **Step 7: Commit**

```bash
git add frontend/src/components/
git commit -m "feat: add shared Button, Field, Card, EmptyState, Avatar primitives"
```

---

### Task 3: App shell & header

**Files:**
- Modify: `frontend/src/App.tsx` (full file)

**Interfaces:**
- Consumes: `Button` and `Avatar` from Task 2; `useAuth()` (`user: User | null`, `logout: () => void`) — unchanged from `src/auth/AuthContext.tsx`.
- Produces: no new exports; this is the top-level shell every route renders inside.

- [x] **Step 1: Replace the file contents**

Replace the entire contents of `frontend/src/App.tsx` with:

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
  isActive ? 'font-medium text-brand-600' : 'text-slate-600 hover:text-brand-600';

export default function App() {
  const { user, logout } = useAuth();

  return (
    <div className="min-h-screen bg-slate-50">
      <nav className="flex flex-wrap items-center justify-between gap-3 border-b border-slate-200 bg-white px-6 py-4">
        <Link to="/" className="text-lg font-bold text-slate-900">
          ✈️ TripPlanner
        </Link>
        <div className="flex flex-wrap items-center gap-4 text-sm">
          <NavLink to="/" className={navLinkClass} end>
            Discover
          </NavLink>
          {user ? (
            <>
              <NavLink to="/trips" className={navLinkClass}>
                My trips
              </NavLink>
              <div className="flex items-center gap-2 rounded-full border border-slate-200 py-1 pl-1 pr-3">
                <Avatar label={user.displayName || user.email} />
                <span className="text-slate-700">{user.displayName || user.email}</span>
                <button type="button" onClick={logout} className="text-slate-500 hover:text-brand-600">
                  Log out
                </button>
              </div>
            </>
          ) : (
            <>
              <NavLink to="/login" className={navLinkClass}>
                Log in
              </NavLink>
              <Link to="/register">
                <Button type="button" size="sm">
                  Sign up
                </Button>
              </Link>
            </>
          )}
        </div>
      </nav>

      <main className="mx-auto max-w-6xl px-4 py-8 sm:px-6">
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

- [x] **Step 2: Verify**

Run: `cd frontend && npm run lint`
Expected: exits 0.

Run: `npm run dev`, open the app.
Expected:
- Logged out: header shows "Discover", "Log in", and a filled "Sign up" button; content area is visibly wider than before.
- Log in: header now shows "Discover", "My trips", and a pill with your initial avatar, name/email, and "Log out".
- Click "Log out": pill disappears, "Log in"/"Sign up" return.
- Shrink the browser window narrow: nav items wrap onto a second line instead of overlapping.

- [ ] **Step 3: Commit**

```bash
git add frontend/src/App.tsx
git commit -m "style: widen app shell and add header login-status pill"
```

---

### Task 4: Auth pages (Login + Register)

**Files:**
- Modify: `frontend/src/features/auth/LoginPage.tsx` (full file)
- Modify: `frontend/src/features/auth/RegisterPage.tsx` (full file)

**Interfaces:**
- Consumes: `Card`, `Field`, `fieldControlClass`, `Button` from Task 2.
- Produces: no change to exported component names/props; `useAuth()` usage (`login`, `register`, `isAuthenticated`) unchanged.

- [x] **Step 1: Replace `LoginPage.tsx`**

```tsx
import { useState, type FormEvent } from 'react';
import { Link, Navigate, useLocation, useNavigate } from 'react-router-dom';
import axios from 'axios';
import { useAuth } from '../../auth/AuthContext';
import { Card } from '../../components/Card';
import { Field, fieldControlClass } from '../../components/Field';
import { Button } from '../../components/Button';

/** Feature 4 / US3 — log in with email and password. */
export function LoginPage() {
  const { login, isAuthenticated } = useAuth();
  const navigate = useNavigate();
  const location = useLocation();

  // US8: a page that redirected here (e.g. "Add to trip" while logged out)
  // says where to return to; a direct visit falls back to the planner.
  const from = (location.state as { from?: string } | null)?.from ?? '/trips';

  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);

  // Already signed in — the form makes no sense; go back (or to the planner).
  if (isAuthenticated) {
    return <Navigate to={from} replace state={location.state} />;
  }

  async function handleSubmit(event: FormEvent) {
    event.preventDefault();
    setError(null);
    setSubmitting(true);
    try {
      await login(email, password);
      // Forward the state so AddToTripButton can resume the pending add.
      navigate(from, { replace: true, state: location.state });
    } catch (err) {
      const message = axios.isAxiosError(err)
        ? (err.response?.data?.detail ?? 'Login failed.')
        : 'Login failed.';
      setError(message);
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <Card className="mx-auto max-w-sm">
      <h1 className="text-3xl font-semibold tracking-tight text-slate-900">Log in</h1>
      <form onSubmit={handleSubmit} className="mt-6 flex flex-col gap-4">
        <Field label="Email">
          <input
            type="email"
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            required
            className={fieldControlClass}
          />
        </Field>
        <Field label="Password">
          <input
            type="password"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            required
            className={fieldControlClass}
          />
        </Field>
        {error && <p className="text-sm text-red-600">{error}</p>}
        <Button type="submit" disabled={submitting}>
          {submitting ? 'Signing in…' : 'Log in'}
        </Button>
      </form>
      <p className="mt-4 text-sm text-slate-500">
        No account?{' '}
        <Link to="/register" className="text-brand-600 hover:underline">
          Sign up
        </Link>
      </p>
    </Card>
  );
}
```

- [x] **Step 2: Replace `RegisterPage.tsx`**

```tsx
import { useState, type FormEvent } from 'react';
import { Link, Navigate, useNavigate } from 'react-router-dom';
import axios from 'axios';
import { useAuth } from '../../auth/AuthContext';
import { Card } from '../../components/Card';
import { Field, fieldControlClass } from '../../components/Field';
import { Button } from '../../components/Button';

/** Feature 4 / US1 — sign up with email and password. */
export function RegisterPage() {
  const { register, isAuthenticated } = useAuth();
  const navigate = useNavigate();

  const [displayName, setDisplayName] = useState('');
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);

  // Already signed in — the form makes no sense; go to the planner.
  if (isAuthenticated) {
    return <Navigate to="/trips" replace />;
  }

  async function handleSubmit(event: FormEvent) {
    event.preventDefault();
    setError(null);
    setSubmitting(true);
    try {
      await register(email, password, displayName || undefined);
      navigate('/trips');
    } catch (err) {
      const message = axios.isAxiosError(err)
        ? (err.response?.data?.detail ?? 'Registration failed.')
        : 'Registration failed.';
      setError(message);
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <Card className="mx-auto max-w-sm">
      <h1 className="text-3xl font-semibold tracking-tight text-slate-900">Create account</h1>
      <form onSubmit={handleSubmit} className="mt-6 flex flex-col gap-4">
        <Field label="Display name (optional)">
          <input
            value={displayName}
            onChange={(e) => setDisplayName(e.target.value)}
            className={fieldControlClass}
          />
        </Field>
        <Field label="Email">
          <input
            type="email"
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            required
            className={fieldControlClass}
          />
        </Field>
        <Field label="Password (min 8 characters)">
          <input
            type="password"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            minLength={8}
            required
            className={fieldControlClass}
          />
        </Field>
        {error && <p className="text-sm text-red-600">{error}</p>}
        <Button type="submit" disabled={submitting}>
          {submitting ? 'Creating…' : 'Sign up'}
        </Button>
      </form>
      <p className="mt-4 text-sm text-slate-500">
        Already have an account?{' '}
        <Link to="/login" className="text-brand-600 hover:underline">
          Log in
        </Link>
      </p>
    </Card>
  );
}
```

- [x] **Step 3: Verify**

Run: `cd frontend && npm run lint`
Expected: exits 0.

Run: `npm run dev`, visit `/login` and `/register`.
Expected: both render as a centered card with the new field/button styling; submitting invalid credentials still shows the red error message; successful login/register still redirects as before.

- [ ] **Step 4: Commit**

```bash
git add frontend/src/features/auth/LoginPage.tsx frontend/src/features/auth/RegisterPage.tsx
git commit -m "style: restyle login and register pages with shared primitives"
```

---

### Task 5: Discover page & city search

**Files:**
- Modify: `frontend/src/features/destinations/SearchPage.tsx` (full file)
- Modify: `frontend/src/features/destinations/CitySearchInput.tsx` (full file)

**Interfaces:**
- Consumes: no shared components from Task 2 (this page uses raw Tailwind for its hero layout, per the design spec's "prominent centered search bar" treatment).
- Produces: no change to exported component names/props (`SearchPage()`, `CitySearchInput({ onSelect })`).

- [x] **Step 1: Replace `SearchPage.tsx`**

```tsx
import { useState } from 'react';
import { Link } from 'react-router-dom';
import { useAuth } from '../../auth/AuthContext';
import { CitySearchInput } from './CitySearchInput';
import { AttractionsList } from './AttractionsList';
import type { LocationSuggestion } from '../../types';

/**
 * F1/US1-3 — public destination discovery page: city autocomplete +
 * recommended attractions. Filters/sorting (US4-5) and the details view
 * (Feature 2) come in later slices.
 */
export function SearchPage() {
  const { isAuthenticated } = useAuth();
  const [selectedCity, setSelectedCity] = useState<LocationSuggestion | null>(null);

  return (
    <div className="flex flex-col gap-8">
      <div className="text-center">
        <h1 className="text-4xl font-semibold tracking-tight text-slate-900">Where to next?</h1>
        <p className="mt-2 text-slate-500">Search a city to see its recommended attractions.</p>
        <div className="mx-auto mt-6 max-w-xl">
          <CitySearchInput onSelect={setSelectedCity} />
        </div>
      </div>

      {selectedCity && <AttractionsList city={selectedCity} />}

      <p className="text-center text-sm text-slate-500">
        {isAuthenticated ? (
          <>
            Ready to plan?{' '}
            <Link to="/trips" className="text-brand-600 hover:underline">
              Go to My trips
            </Link>
            .
          </>
        ) : (
          <>
            <Link to="/login" className="text-brand-600 hover:underline">
              Log in
            </Link>{' '}
            to start planning a trip.
          </>
        )}
      </p>
    </div>
  );
}
```

- [x] **Step 2: Replace `CitySearchInput.tsx`**

```tsx
import { useEffect, useState } from 'react';
import { searchLocations } from '../../api/destinations';
import { useDebounce } from '../../hooks/useDebounce';
import type { LocationSuggestion } from '../../types';

// Matches SearchLocationsRequestValidator.MinQueryLength on the backend —
// shorter queries would just get a 400 back.
const MIN_QUERY_LENGTH = 2;

function formatCity(city: LocationSuggestion): string {
  return city.country ? `${city.name}, ${city.country}` : city.name;
}

/** F1/US1-2 — debounced city autocomplete. Calls onSelect when a suggestion is picked. */
export function CitySearchInput({ onSelect }: { onSelect: (city: LocationSuggestion) => void }) {
  const [query, setQuery] = useState('');
  const [suggestions, setSuggestions] = useState<LocationSuggestion[]>([]);
  const [open, setOpen] = useState(false);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  // Selecting a suggestion writes its label into the input, which would
  // re-trigger the search below and reopen the dropdown — skip that query.
  const [pickedLabel, setPickedLabel] = useState<string | null>(null);

  const debouncedQuery = useDebounce(query, 300);

  useEffect(() => {
    const trimmed = debouncedQuery.trim();
    if (trimmed.length < MIN_QUERY_LENGTH || trimmed === pickedLabel) {
      setSuggestions([]);
      setOpen(false);
      setLoading(false);
      setError(null);
      return;
    }

    let ignore = false;
    setLoading(true);
    setError(null);
    searchLocations(trimmed)
      .then((results) => {
        if (ignore) return;
        setSuggestions(results);
        setOpen(true);
      })
      .catch(() => {
        if (ignore) return;
        setError('Could not load suggestions. Please try again.');
        setSuggestions([]);
        setOpen(false);
      })
      .finally(() => {
        if (!ignore) setLoading(false);
      });

    return () => {
      ignore = true;
    };
  }, [debouncedQuery, pickedLabel]);

  function handlePick(city: LocationSuggestion) {
    const label = formatCity(city);
    setQuery(label);
    setPickedLabel(label);
    setOpen(false);
    setSuggestions([]);
    onSelect(city);
  }

  return (
    <div className="relative">
      <input
        type="search"
        placeholder="Search for a city, e.g. Paris"
        value={query}
        onChange={(e) => setQuery(e.target.value)}
        aria-label="Search for a city"
        className="w-full rounded-full border border-slate-300 px-5 py-3 text-base text-slate-900 shadow-sm focus:border-brand-500 focus:outline-none focus:ring-2 focus:ring-brand-200"
      />
      {loading && <p className="mt-2 text-sm text-slate-500">Searching…</p>}
      {error && <p className="mt-2 text-sm text-red-600">{error}</p>}
      {open && !loading && suggestions.length === 0 && (
        <p className="mt-2 text-sm text-slate-500">No matching cities found.</p>
      )}
      {open && suggestions.length > 0 && (
        <ul className="absolute inset-x-0 top-full z-10 mt-2 overflow-hidden rounded-xl border border-slate-200 bg-white shadow-lg">
          {suggestions.map((city) => (
            <li key={`${city.latitude},${city.longitude}`}>
              <button
                type="button"
                onClick={() => handlePick(city)}
                className="block w-full px-4 py-2.5 text-left hover:bg-brand-50"
              >
                {formatCity(city)}
              </button>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
```

- [x] **Step 3: Verify**

Run: `cd frontend && npm run lint`
Expected: exits 0.

Run: `npm run dev`, visit `/`.
Expected: centered "Where to next?" heading, a large rounded search bar; typing "par" (with backend running) still shows a debounced dropdown with brand-colored hover states; picking "Paris" still loads attractions below.

- [ ] **Step 4: Commit**

```bash
git add frontend/src/features/destinations/SearchPage.tsx frontend/src/features/destinations/CitySearchInput.tsx
git commit -m "style: restyle discover page and city search as a hero search bar"
```

---

### Task 6: Attraction cards & add-to-trip

**Files:**
- Modify: `frontend/src/features/destinations/AttractionsList.tsx` (full file)
- Modify: `frontend/src/features/destinations/AddToTripButton.tsx` (full file)

**Interfaces:**
- Consumes: `EmptyState`, `Field`/`fieldControlClass`, `Button` from Task 2.
- Produces: no change to exported component names/props (`AttractionsList({ city })`, `AddToTripButton({ attraction })`).

- [x] **Step 1: Replace `AttractionsList.tsx`**

```tsx
import { useEffect, useMemo, useState } from 'react';
import { Link } from 'react-router-dom';
import { getAttractions } from '../../api/destinations';
import { AddToTripButton } from './AddToTripButton';
import { EmptyState } from '../../components/EmptyState';
import { fieldControlClass } from '../../components/Field';
import { Button } from '../../components/Button';
import type { AttractionSummary, LocationSuggestion } from '../../types';

function AttractionCard({ attraction }: { attraction: AttractionSummary }) {
  const [imageFailed, setImageFailed] = useState(false);
  const showImage = attraction.imageUrl && !imageFailed;

  return (
    <div className="overflow-hidden rounded-2xl border border-slate-200 bg-white shadow-sm">
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
          <strong className="text-slate-900">{attraction.name}</strong>
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

/** F1/US3-US5 — recommended attractions near the selected city, with filters and sort. */
export function AttractionsList({ city }: { city: LocationSuggestion }) {
  const [attractions, setAttractions] = useState<AttractionSummary[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  // F1/US4-US5 — filters and sort are frontend concerns over the ≤20 loaded
  // results (spec §11.2); no API parameters involved.
  const [categoryFilter, setCategoryFilter] = useState('');
  const [minRating, setMinRating] = useState(''); // '' = any; otherwise a number as string
  const [sortBy, setSortBy] = useState<'recommended' | 'rating'>('recommended');

  useEffect(() => {
    let ignore = false;
    setLoading(true);
    setError(null);
    // A new city means new results — stale filters would silently hide them.
    setCategoryFilter('');
    setMinRating('');
    setSortBy('recommended');
    getAttractions(city.latitude, city.longitude)
      .then((results) => {
        if (!ignore) setAttractions(results);
      })
      .catch(() => {
        if (!ignore) setError('Could not load attractions. Please try again.');
      })
      .finally(() => {
        if (!ignore) setLoading(false);
      });

    return () => {
      ignore = true;
    };
  }, [city]);

  // Filter options come from the data itself — only categories that exist.
  const categories = useMemo(
    () => [...new Set(attractions.flatMap((a) => (a.category ? [a.category] : [])))].sort(),
    [attractions],
  );

  const visible = useMemo(() => {
    const filtered = attractions.filter(
      (a) =>
        (categoryFilter === '' || a.category === categoryFilter) &&
        (minRating === '' || (a.rating != null && a.rating >= Number(minRating))),
    );
    // "Recommended" keeps the API's order; rating sort puts unrated last (US5
    // keeps the filters because it sorts the already-filtered list).
    return sortBy === 'rating'
      ? [...filtered].sort((a, b) => (b.rating ?? -1) - (a.rating ?? -1))
      : filtered;
  }, [attractions, categoryFilter, minRating, sortBy]);

  const hasActiveFilters = categoryFilter !== '' || minRating !== '';

  if (loading) return <p className="text-center text-sm text-slate-500">Loading attractions…</p>;
  if (error) return <p className="text-center text-sm text-red-600">{error}</p>;
  if (attractions.length === 0) {
    return <EmptyState message={`No attractions found near ${city.name}.`} />;
  }

  return (
    <section>
      <h2 className="mb-4 text-lg font-semibold text-slate-900">Attractions near {city.name}</h2>

      <div className="mb-6 flex flex-wrap items-center gap-3 text-sm">
        <label className="flex items-center gap-1.5 text-slate-500">
          Category
          <select value={categoryFilter} onChange={(e) => setCategoryFilter(e.target.value)} className={fieldControlClass}>
            <option value="">All</option>
            {categories.map((category) => (
              <option key={category} value={category}>
                {category}
              </option>
            ))}
          </select>
        </label>

        <label className="flex items-center gap-1.5 text-slate-500">
          Rating
          <select value={minRating} onChange={(e) => setMinRating(e.target.value)} className={fieldControlClass}>
            <option value="">Any</option>
            <option value="3">3+ stars</option>
            <option value="4">4+ stars</option>
          </select>
        </label>

        <label className="flex items-center gap-1.5 text-slate-500">
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
          >
            Clear filters
          </Button>
        )}
      </div>

      {visible.length === 0 ? (
        <EmptyState message="No attractions match your filters." />
      ) : (
        <div className="grid gap-5 sm:grid-cols-2 lg:grid-cols-3">
          {visible.map((attraction) => (
            <AttractionCard key={attraction.providerId} attraction={attraction} />
          ))}
        </div>
      )}
    </section>
  );
}
```

- [x] **Step 2: Replace `AddToTripButton.tsx`**

```tsx
import { useEffect, useState } from 'react';
import { useLocation, useNavigate } from 'react-router-dom';
import axios from 'axios';
import { useAuth } from '../../auth/AuthContext';
import { getMyTrips, getTrip, addDestination } from '../../api/trips';
import { Button } from '../../components/Button';
import { Field, fieldControlClass } from '../../components/Field';
import type { AttractionSummary, ItineraryDay, TripSummary } from '../../types';

/**
 * F3/US3 — add an attraction to one of the user's trips, optionally onto a
 * specific day (US4 drag-and-drop is out of scope, so a day picker stands in).
 * Logged-out users are sent to the login page instead (US8).
 */
export function AddToTripButton({ attraction }: { attraction: AttractionSummary }) {
  const { isAuthenticated } = useAuth();
  const navigate = useNavigate();
  const location = useLocation();

  const [open, setOpen] = useState(false);
  const [justAdded, setJustAdded] = useState(false);

  // US8 resume: LoginPage forwards our state back here after sign-in. The
  // providerId check matters — the search page renders one button per
  // attraction, and only the one the user originally clicked should open.
  useEffect(() => {
    const state = location.state as { resumeAddId?: string } | null;
    if (isAuthenticated && state?.resumeAddId === attraction.providerId) {
      setOpen(true);
      // Clear the note so refresh/back doesn't re-open the dialog.
      navigate(location.pathname, { replace: true, state: null });
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [isAuthenticated, location.state]);

  function handleClick() {
    if (!isAuthenticated) {
      // US8: prompt login, remembering where we were and what the user was
      // trying to add so the flow can resume after sign-in.
      navigate('/login', {
        state: { from: location.pathname, resumeAddId: attraction.providerId },
      });
      return;
    }
    setOpen(true);
  }

  return (
    <>
      <Button type="button" variant="outline" size="sm" onClick={handleClick} className="w-full">
        {justAdded ? 'Added ✓' : 'Add to trip'}
      </Button>
      {open && (
        <AddToTripDialog
          attraction={attraction}
          onClose={() => setOpen(false)}
          onAdded={() => {
            setOpen(false);
            setJustAdded(true);
            setTimeout(() => setJustAdded(false), 2500);
          }}
        />
      )}
    </>
  );
}

function AddToTripDialog({
  attraction,
  onClose,
  onAdded,
}: {
  attraction: AttractionSummary;
  onClose: () => void;
  onAdded: () => void;
}) {
  const [trips, setTrips] = useState<TripSummary[] | null>(null);
  const [tripId, setTripId] = useState('');
  const [days, setDays] = useState<ItineraryDay[] | null>(null);
  // '' = Saved Places (no day) — the backend takes itineraryDayId: null.
  const [dayId, setDayId] = useState('');
  const [adding, setAdding] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let ignore = false;
    getMyTrips()
      .then((result) => {
        if (!ignore) setTrips(result);
      })
      .catch(() => {
        if (!ignore) setError('Could not load your trips. Please try again.');
      });
    return () => {
      ignore = true;
    };
  }, []);

  // Load the selected trip's days so the user can schedule directly (US3 AC3).
  useEffect(() => {
    setDays(null);
    setDayId('');
    if (!tripId) return;

    let ignore = false;
    getTrip(tripId)
      .then((trip) => {
        if (!ignore) setDays(trip.days);
      })
      .catch(() => {
        if (!ignore) setError('Could not load the trip days. Please try again.');
      });
    return () => {
      ignore = true;
    };
  }, [tripId]);

  async function handleAdd() {
    setError(null);
    setAdding(true);
    try {
      await addDestination(tripId, attraction.providerId, dayId || null);
      onAdded();
    } catch (err) {
      const message = axios.isAxiosError(err)
        ? (err.response?.data?.detail ?? 'Could not add the destination.')
        : 'Could not add the destination.';
      setError(message);
    } finally {
      setAdding(false);
    }
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-slate-900/40 p-4" onClick={onClose}>
      <div
        className="w-full max-w-sm rounded-2xl border border-slate-200 bg-white p-6 shadow-lg"
        onClick={(e) => e.stopPropagation()}
        role="dialog"
        aria-label={`Add ${attraction.name} to a trip`}
      >
        <h3 className="text-lg font-semibold text-slate-900">Add “{attraction.name}”</h3>

        {trips === null && !error && <p className="mt-3 text-sm text-slate-500">Loading your trips…</p>}

        {trips !== null && trips.length === 0 && (
          <p className="mt-3 text-sm text-slate-500">
            You have no trips yet — create one on the My trips page first.
          </p>
        )}

        {trips !== null && trips.length > 0 && (
          <div className="mt-4 flex flex-col gap-3">
            <Field label="Trip">
              <select value={tripId} onChange={(e) => setTripId(e.target.value)} className={fieldControlClass}>
                <option value="">Choose a trip…</option>
                {trips.map((trip) => (
                  <option key={trip.id} value={trip.id}>
                    {trip.name}
                  </option>
                ))}
              </select>
            </Field>

            {tripId && (
              <Field label="Day">
                <select value={dayId} onChange={(e) => setDayId(e.target.value)} className={fieldControlClass}>
                  <option value="">Saved Places (no day yet)</option>
                  {(days ?? []).map((day) => (
                    <option key={day.id} value={day.id}>
                      Day {day.dayNumber} — {day.date}
                    </option>
                  ))}
                </select>
              </Field>
            )}
          </div>
        )}

        {error && <p className="mt-3 text-sm text-red-600">{error}</p>}

        <div className="mt-5 flex justify-end gap-2">
          <Button type="button" variant="secondary" size="sm" onClick={onClose}>
            Cancel
          </Button>
          <Button type="button" size="sm" onClick={handleAdd} disabled={!tripId || adding}>
            {adding ? 'Adding…' : 'Add'}
          </Button>
        </div>
      </div>
    </div>
  );
}
```

- [x] **Step 3: Verify**

Run: `cd frontend && npm run lint`
Expected: exits 0.

Run: `npm run dev`, search a city on `/`.
Expected: attraction cards show a large ~4:3 photo (or the 🏛️ placeholder), 3-column grid on a wide window; category/rating/sort filters still filter correctly; "Add to trip" opens the restyled modal, still lets you pick a trip/day and add, still shows "Added ✓" briefly after success; logged-out click still redirects to `/login` and resumes the dialog after signing in.

- [ ] **Step 4: Commit**

```bash
git add frontend/src/features/destinations/AttractionsList.tsx frontend/src/features/destinations/AddToTripButton.tsx
git commit -m "style: restyle attraction cards and add-to-trip dialog"
```

---

### Task 7: Destination details page

**Files:**
- Modify: `frontend/src/features/destinations/DestinationDetailsPage.tsx` (full file)

**Interfaces:**
- Consumes: `Card` from Task 2; `AddToTripButton` from Task 6 (already restyled).
- Produces: no change to exported component name/props (`DestinationDetailsPage()`).

- [x] **Step 1: Replace the file contents**

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
```

- [x] **Step 2: Verify**

Run: `cd frontend && npm run lint`
Expected: exits 0.

Run: `npm run dev`, click into a destination from the search results.
Expected: large full-width hero photo (or 🏛️ placeholder) at the top, name/category/description below, address/opening-hours/website as a clean stacked list (not cramped inline pairs), "Add to trip" button beneath. A bad/missing `providerId` still shows the "Destination not found." card with a working back link.

- [ ] **Step 3: Commit**

```bash
git add frontend/src/features/destinations/DestinationDetailsPage.tsx
git commit -m "style: restyle destination details page with a full-width hero"
```

---

### Task 8: Trips list page

**Files:**
- Modify: `frontend/src/features/trips/TripsPage.tsx` (full file)

**Interfaces:**
- Consumes: `Card`, `Field`/`fieldControlClass`, `Button`, `EmptyState` from Task 2.
- Produces: no change to exported component name/props (`TripsPage()`). No longer reads `logout`/`user` from `useAuth()` — that moved to the header in Task 3.

- [x] **Step 1: Replace the file contents**

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
      <h1 className="text-3xl font-semibold tracking-tight text-slate-900">My trips</h1>

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
        <Button type="submit" disabled={creating || name.trim() === ''}>
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
        <div className="grid gap-5 sm:grid-cols-2 lg:grid-cols-3">
          {trips.map((trip) => (
            <Link key={trip.id} to={`/trips/${trip.id}`}>
              <Card padding="tight" className="flex h-full flex-col gap-1 transition-colors hover:border-brand-300">
                <strong className="text-slate-900">{trip.name}</strong>
                <span className="text-sm text-slate-500">{formatDates(trip.startDate, trip.endDate)}</span>
                <span className="text-sm text-slate-500">
                  {trip.destinationCount} destination{trip.destinationCount === 1 ? '' : 's'}
                </span>
              </Card>
            </Link>
          ))}
        </div>
      )}
    </div>
  );
}
```

- [x] **Step 2: Verify**

Run: `cd frontend && npm run lint`
Expected: exits 0.

Run: `npm run dev`, log in, visit `/trips`.
Expected: no "Signed in as…" line (that's in the header now); a compact create-trip form; existing trips render as a card grid (2–3 columns) with name/dates/count, each linking to its detail page; creating a new trip still appends it to the grid without a page reload; zero trips shows the 🗺️ empty state.

- [ ] **Step 3: Commit**

```bash
git add frontend/src/features/trips/TripsPage.tsx
git commit -m "style: restyle trips list as a card grid"
```

---

### Task 9: Trip detail page (itinerary + Saved Places)

**Files:**
- Modify: `frontend/src/features/trips/TripDetailPage.tsx` (full file)

**Interfaces:**
- Consumes: `Card`, `Field`/`fieldControlClass`, `Button` from Task 2. `TripDestination`, `TripDetail` types unchanged from `src/types.ts`.
- Produces: no change to exported component name/props (`TripDetailPage()`). All state, effects, and handlers (`applyTrip`, `wouldRemoveScheduledItems`, `handleSave`, `handleDrop`, `handleRemove`, `moveLocally`) are unchanged — only `DestinationList`'s row markup changes, plus a new internal `DestinationThumbnail` helper.

- [x] **Step 1: Replace the file contents**

```tsx
import { useEffect, useState, type FormEvent } from 'react';
import { Link, useParams } from 'react-router-dom';
import axios from 'axios';
import { getTrip, removeDestination, updateItineraryItem, updateTrip } from '../../api/trips';
import { Card } from '../../components/Card';
import { Field, fieldControlClass } from '../../components/Field';
import { Button } from '../../components/Button';
import type { TripDestination, TripDetail } from '../../types';

/** Small thumbnail with the same missing/broken-image fallback as the attraction cards. */
function DestinationThumbnail({ imageUrl, name }: { imageUrl: string | null; name: string }) {
  const [failed, setFailed] = useState(false);
  const showImage = imageUrl && !failed;
  return showImage ? (
    <img src={imageUrl} alt={name} onError={() => setFailed(true)} className="h-10 w-10 rounded-lg object-cover" />
  ) : (
    <div
      className="flex h-10 w-10 items-center justify-center rounded-lg bg-slate-100 text-base"
      aria-hidden="true"
    >
      🏛️
    </div>
  );
}

/**
 * F3/US4-US6 — one drop-enabled bucket (a day, or Saved Places when dayId is
 * null). Dropping on a row inserts at that row's position; dropping on the
 * surrounding area appends to the end.
 */
function DestinationList({
  dayId,
  destinations,
  emptyHint,
  onRemove,
  removingItemId,
  onDragStart,
  onDragEnd,
  onDrop,
}: {
  dayId: string | null;
  destinations: TripDestination[];
  emptyHint: string;
  onRemove: (itemId: string) => void;
  removingItemId: string | null;
  onDragStart: (itemId: string) => void;
  onDragEnd: () => void;
  onDrop: (targetDayId: string | null, position: number) => void;
}) {
  return (
    <div
      onDragOver={(e) => e.preventDefault()} // required, or the browser refuses the drop
      onDrop={() => onDrop(dayId, destinations.length)}
    >
      {destinations.length === 0 ? (
        <p className="mt-3 rounded-xl border-2 border-dashed border-slate-300 bg-slate-50 px-4 py-6 text-center text-sm text-slate-500">
          {emptyHint}
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
              className="flex cursor-grab items-center gap-3 rounded-xl border border-slate-200 bg-white px-3 py-2 active:cursor-grabbing"
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

/**
 * Optimistic mirror of the backend's move semantics (US4-US6): pull the item
 * out of every bucket, insert it at the requested position in the target, and
 * renumber both buckets densely. The server confirms the same state.
 */
function moveLocally(
  trip: TripDetail,
  itemId: string,
  targetDayId: string | null,
  position: number,
): TripDetail {
  const item = [...trip.savedPlaces, ...trip.days.flatMap((d) => d.destinations)].find(
    (d) => d.itemId === itemId,
  );
  if (!item) return trip;

  const without = (list: TripDestination[]) => list.filter((d) => d.itemId !== itemId);
  const insertInto = (list: TripDestination[]) => {
    const next = [...list];
    next.splice(Math.min(position, next.length), 0, item);
    return next.map((d, i) => ({ ...d, sortOrder: i }));
  };

  return {
    ...trip,
    days: trip.days.map((day) => {
      const rest = without(day.destinations);
      return { ...day, destinations: day.id === targetDayId ? insertInto(rest) : rest };
    }),
    savedPlaces: targetDayId === null ? insertInto(without(trip.savedPlaces)) : without(trip.savedPlaces),
  };
}

/** F3/US2, US7, US9 & US10 — day-by-day itinerary, Saved Places, edit name/dates. */
export function TripDetailPage() {
  const { tripId } = useParams<{ tripId: string }>();

  const [trip, setTrip] = useState<TripDetail | null>(null);
  const [loadError, setLoadError] = useState<string | null>(null);

  // Edit form state — kept as strings so they bind directly to the inputs.
  const [name, setName] = useState('');
  const [startDate, setStartDate] = useState('');
  const [endDate, setEndDate] = useState('');
  const [saving, setSaving] = useState(false);
  const [saveError, setSaveError] = useState<string | null>(null);

  const [removingItemId, setRemovingItemId] = useState<string | null>(null);
  const [removeError, setRemoveError] = useState<string | null>(null);

  const [dragItemId, setDragItemId] = useState<string | null>(null);
  const [moveError, setMoveError] = useState<string | null>(null);

  // Sync both the page and the edit form from a freshly fetched/saved trip.
  function applyTrip(fresh: TripDetail) {
    setTrip(fresh);
    setName(fresh.name);
    setStartDate(fresh.startDate ?? '');
    setEndDate(fresh.endDate ?? '');
  }

  useEffect(() => {
    if (!tripId) return;

    let ignore = false;
    setLoadError(null);
    getTrip(tripId)
      .then((fresh) => {
        if (!ignore) applyTrip(fresh);
      })
      .catch((err) => {
        if (ignore) return;
        const notFound = axios.isAxiosError(err) && err.response?.status === 404;
        setLoadError(notFound ? 'Trip not found.' : 'Could not load the trip. Please try again.');
      });
    return () => {
      ignore = true;
    };
  }, [tripId]);

  // F3/US2 AC5 — dates are yyyy-MM-dd strings (both from ItineraryDay.date and
  // <input type="date">), so plain string comparison sorts chronologically.
  function wouldRemoveScheduledItems(): boolean {
    if (!trip) return false;
    return trip.days.some((day) => {
      const staysInRange = startDate !== '' && endDate !== '' && day.date >= startDate && day.date <= endDate;
      return !staysInRange && day.destinations.length > 0;
    });
  }

  async function handleSave(event: FormEvent) {
    event.preventDefault();
    if (!tripId) return;

    if (wouldRemoveScheduledItems()) {
      const confirmed = window.confirm(
        'Changing the dates removes days outside the new range and moves their destinations back to Saved Places. Continue?',
      );
      if (!confirmed) return;
    }

    setSaveError(null);
    setSaving(true);
    try {
      // <input type="date"> uses '' for empty — the API wants null.
      const updated = await updateTrip(tripId, name.trim(), startDate || null, endDate || null);
      applyTrip(updated);
    } catch (err) {
      const message = axios.isAxiosError(err)
        ? (err.response?.data?.detail ?? 'Could not save the trip.')
        : 'Could not save the trip.';
      setSaveError(message);
    } finally {
      setSaving(false);
    }
  }

  // F3/US4-US6 with NFR4 (DnD ≤ 100 ms): apply the move to local state
  // immediately, let the API confirm in the background, roll back on error.
  async function handleDrop(targetDayId: string | null, position: number) {
    if (!tripId || !trip || !dragItemId) return;
    const itemId = dragItemId;
    setDragItemId(null);

    const snapshot = trip;
    setMoveError(null);
    setTrip(moveLocally(trip, itemId, targetDayId, position));
    try {
      await updateItineraryItem(tripId, itemId, targetDayId, position);
    } catch (err) {
      setTrip(snapshot); // roll back the optimistic move
      const message = axios.isAxiosError(err)
        ? (err.response?.data?.detail ?? 'Could not move the destination.')
        : 'Could not move the destination.';
      setMoveError(message);
    }
  }

  async function handleRemove(itemId: string) {
    if (!tripId) return;
    setRemoveError(null);
    setRemovingItemId(itemId);
    try {
      await removeDestination(tripId, itemId);
      setTrip(
        (current) =>
          current && {
            ...current,
            days: current.days.map((day) => ({
              ...day,
              destinations: day.destinations.filter((d) => d.itemId !== itemId),
            })),
            savedPlaces: current.savedPlaces.filter((d) => d.itemId !== itemId),
          },
      );
    } catch {
      setRemoveError('Could not remove the destination. Please try again.');
    } finally {
      setRemovingItemId(null);
    }
  }

  if (loadError) {
    return (
      <Card className="mx-auto max-w-2xl">
        <p className="text-sm text-red-600">{loadError}</p>
        <Link to="/trips" className="mt-2 inline-block text-sm text-brand-600 hover:underline">
          ← Back to my trips
        </Link>
      </Card>
    );
  }

  if (!trip) {
    return (
      <Card className="mx-auto max-w-2xl">
        <p className="text-sm text-slate-500">Loading trip…</p>
      </Card>
    );
  }

  return (
    <div className="flex flex-col gap-8">
      <div>
        <Link to="/trips" className="text-sm text-brand-600 hover:underline">
          ← Back to my trips
        </Link>
        <h1 className="mt-2 text-3xl font-semibold tracking-tight text-slate-900">{trip.name}</h1>
      </div>

      <Card>
        <form onSubmit={handleSave} className="flex flex-col gap-4">
          <Field label="Name">
            <input value={name} onChange={(e) => setName(e.target.value)} required className={fieldControlClass} />
          </Field>
          <div className="flex gap-4">
            <div className="flex-1">
              <Field label="Start date">
                <input
                  type="date"
                  value={startDate}
                  onChange={(e) => setStartDate(e.target.value)}
                  className={fieldControlClass}
                />
              </Field>
            </div>
            <div className="flex-1">
              <Field label="End date">
                <input
                  type="date"
                  value={endDate}
                  onChange={(e) => setEndDate(e.target.value)}
                  className={fieldControlClass}
                />
              </Field>
            </div>
          </div>
          {saveError && <p className="text-sm text-red-600">{saveError}</p>}
          <Button type="submit" disabled={saving || name.trim() === ''} className="self-start">
            {saving ? 'Saving…' : 'Save changes'}
          </Button>
        </form>
      </Card>

      {removeError && <p className="text-sm text-red-600">{removeError}</p>}
      {moveError && <p className="text-sm text-red-600">{moveError}</p>}

      {trip.days.length === 0 ? (
        <p className="text-sm text-slate-500">Set the trip dates to generate a day-by-day itinerary.</p>
      ) : (
        <div className="flex flex-col gap-6">
          {trip.days.map((day) => (
            <section key={day.id}>
              <h2 className="text-lg font-semibold text-slate-900">
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
      )}

      <section>
        <h2 className="text-lg font-semibold text-slate-900">Saved Places</h2>
        <DestinationList
          dayId={null}
          destinations={trip.savedPlaces}
          emptyHint="No saved places — add destinations from the Discover page."
          onRemove={handleRemove}
          removingItemId={removingItemId}
          onDragStart={setDragItemId}
          onDragEnd={() => setDragItemId(null)}
          onDrop={handleDrop}
        />
      </section>
    </div>
  );
}
```

- [x] **Step 2: Verify**

Run: `cd frontend && npm run lint`
Expected: exits 0.

Run: `npm run dev`, open a trip with dated days and at least one saved place.
Expected:
- Each destination row shows a grip glyph, a thumbnail (image or 🏛️ placeholder), name, and a restyled red "Remove" button.
- Dragging a Saved Places row into a day still schedules it there (optimistic move, confirmed by the server); dragging between days still works; dragging back to Saved Places still works.
- An empty day/Saved Places bucket shows the dashed drop-target box.
- Changing dates outside the current range still prompts the "Changing the dates removes days…" confirm dialog before saving.
- "Remove" still removes the item from whichever bucket it was in.

- [ ] **Step 3: Commit**

```bash
git add frontend/src/features/trips/TripDetailPage.tsx
git commit -m "style: restyle trip itinerary rows with thumbnails, preserve drag-and-drop"
```

---

## Self-Review Notes

- **Spec coverage:** Section A (tokens/primitives) → Tasks 1–2. Section B (shell/header/account pill) → Task 3. Section C (Discover/attractions/details) → Tasks 5–7. Section D (trips list/detail/modal) → Tasks 8–9, 6. Section E (auth) → Task 4. All spec pages are covered; no spec section lacks a task.
- **Placeholder scan:** no TBD/TODO; every step has complete, runnable code.
- **Type consistency:** `Button`/`Field`/`fieldControlClass`/`Card`/`EmptyState`/`Avatar` are defined once in Task 2 with the exact names and prop shapes every later task imports — checked against each usage above (including the `Button` `outline` variant, which Task 2 defines and Task 6 is the first to use, for the "Add to trip" CTA).
- **Deviation from spec worth flagging to the user:** the spec's Button table listed `primary`/`secondary`/`danger`; implementation needed a 4th `outline` variant (brand-tinted border, used for "Add to trip") to avoid fighting Tailwind's class-order cascade by bolting brand colors onto the `secondary` variant via extra classNames. Noted here rather than silently expanding scope.

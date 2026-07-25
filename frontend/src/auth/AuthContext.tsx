import { createContext, useContext, useEffect, useMemo, useState, type ReactNode } from 'react';
import { AUTH_LOGOUT_EVENT, clearToken, storeToken } from '../api/client';
import * as authApi from '../api/auth';
import type { User } from '../types';

const USER_STORAGE_KEY = 'tripplanner.user';

interface AuthContextValue {
  user: User | null;
  isAuthenticated: boolean;
  login: (email: string, password: string) => Promise<void>;
  register: (email: string, password: string, displayName?: string) => Promise<void>;
  logout: () => void;
  // F4/US2 — reflects a just-completed verification immediately (e.g. the
  // user opened the link in the same browser tab they registered from)
  // without requiring them to log in again.
  markEmailVerified: () => void;
}

const AuthContext = createContext<AuthContextValue | undefined>(undefined);

/**
 * Holds the signed-in user in memory + localStorage so a page refresh keeps the
 * session (Feature 4 / US3: "Stay signed in after refreshing the page").
 */
export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<User | null>(() => {
    const raw = localStorage.getItem(USER_STORAGE_KEY);
    return raw ? (JSON.parse(raw) as User) : null;
  });

  function persistSession(token: string, nextUser: User) {
    storeToken(token);
    localStorage.setItem(USER_STORAGE_KEY, JSON.stringify(nextUser));
    setUser(nextUser);
  }

  function clearSession() {
    clearToken();
    localStorage.removeItem(USER_STORAGE_KEY);
    setUser(null);
  }

  // A 401 with a token attached means the server no longer honors this
  // session (expired/revoked) — sync isAuthenticated to that immediately,
  // otherwise it stays true forever and traps the user off the login page.
  useEffect(() => {
    window.addEventListener(AUTH_LOGOUT_EVENT, clearSession);
    return () => window.removeEventListener(AUTH_LOGOUT_EVENT, clearSession);
  }, []);

  const value = useMemo<AuthContextValue>(
    () => ({
      user,
      isAuthenticated: user !== null,
      async login(email, password) {
        const res = await authApi.login(email, password);
        persistSession(res.accessToken, res.user);
      },
      async register(email, password, displayName) {
        const res = await authApi.register(email, password, displayName);
        persistSession(res.accessToken, res.user);
      },
      logout: clearSession,
      markEmailVerified() {
        setUser((current) => {
          if (!current || current.isEmailVerified) return current;
          const next = { ...current, isEmailVerified: true };
          localStorage.setItem(USER_STORAGE_KEY, JSON.stringify(next));
          return next;
        });
      },
    }),
    [user],
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthContextValue {
  const ctx = useContext(AuthContext);
  if (!ctx) {
    throw new Error('useAuth must be used within an AuthProvider');
  }
  return ctx;
}

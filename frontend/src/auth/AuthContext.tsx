import { createContext, useContext, useMemo, useState, type ReactNode } from 'react';
import { clearToken, storeToken } from '../api/client';
import * as authApi from '../api/auth';
import type { User } from '../types';

const USER_STORAGE_KEY = 'tripplanner.user';

interface AuthContextValue {
  user: User | null;
  isAuthenticated: boolean;
  login: (email: string, password: string) => Promise<void>;
  register: (email: string, password: string, displayName?: string) => Promise<void>;
  logout: () => void;
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
      logout() {
        clearToken();
        localStorage.removeItem(USER_STORAGE_KEY);
        setUser(null);
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

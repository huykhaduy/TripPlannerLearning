import '@testing-library/jest-dom/vitest';
import { cleanup } from '@testing-library/react';
import { afterEach } from 'vitest';

// Both are explicit because `globals: false` means React Testing Library cannot
// register its own afterEach hook. localStorage is shared across tests in a file,
// and AuthContext reads it during render, so a leftover session would leak into
// the next test's initial state.
afterEach(() => {
  cleanup();
  localStorage.clear();
});

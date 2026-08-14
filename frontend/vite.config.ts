import { defineConfig } from 'vitest/config';
import react from '@vitejs/plugin-react';
import tailwindcss from '@tailwindcss/vite';

// https://vite.dev/config/
export default defineConfig({
  plugins: [react(), tailwindcss()],
  server: {
    port: 5173,
  },
  test: {
    // jsdom, not node: everything under test touches localStorage, window events,
    // or the DOM via React Testing Library.
    environment: 'jsdom',
    // Tests live in tests/, mirroring src/ one-for-one — the same split the
    // backend uses (backend/tests mirrors backend/src). Stated explicitly rather
    // than left to Vitest's default glob, which would also pick up a stray
    // *.test.ts anywhere under src/ and quietly reintroduce co-location.
    include: ['tests/**/*.test.{ts,tsx}'],
    setupFiles: ['./tests/setup.ts'],
    // Globals stay off — test files import describe/it/expect explicitly, so they
    // type-check under the existing tsconfig without needing a "types" entry.
    globals: false,
    css: false,
  },
});

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
    setupFiles: ['./src/test/setup.ts'],
    // Globals stay off — test files import describe/it/expect explicitly, so they
    // type-check under the existing tsconfig without needing a "types" entry.
    globals: false,
    css: false,
  },
});

import type { ReactNode } from 'react';

/** Shared input/select styling — apply to the actual <input>/<select> inside a Field. */
export const fieldControlClass =
  'rounded-lg border border-slate-300 bg-white px-3 py-2 text-base text-slate-900 focus:border-brand-500 focus:outline-none focus:ring-2 focus:ring-brand-200';

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

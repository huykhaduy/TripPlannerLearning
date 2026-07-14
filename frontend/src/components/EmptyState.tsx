interface EmptyStateProps {
  icon?: string;
  message: string;
}

/** Centered placeholder for "nothing here yet" states (empty lists, no results). */
export function EmptyState({ icon = '🧭', message }: EmptyStateProps) {
  return (
    <div className="flex flex-col items-center gap-3 rounded-xl border border-dashed border-slate-300 bg-white px-4 py-10 text-center">
      <span className="flex h-14 w-14 items-center justify-center rounded-full bg-brand-50 text-3xl" aria-hidden="true">
        {icon}
      </span>
      <p className="text-sm text-slate-500">{message}</p>
    </div>
  );
}

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

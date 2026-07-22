import type { ButtonHTMLAttributes } from 'react';

type ButtonVariant = 'primary' | 'action' | 'secondary' | 'outline' | 'danger';
type ButtonSize = 'md' | 'sm';

const VARIANT_CLASSES: Record<ButtonVariant, string> = {
  primary: 'bg-brand-600 text-white hover:bg-brand-700',
  // Reserved for the one primary call-to-action per view (Add to
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

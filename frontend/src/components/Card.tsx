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
      className={`rounded-2xl border border-slate-200 bg-white shadow-sm ${PADDING_CLASSES[padding]} ${className}`}
      {...rest}
    />
  );
}

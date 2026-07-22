import type { HTMLAttributes } from 'react';

type CardPadding = 'normal' | 'tight';

const PADDING_CLASSES: Record<CardPadding, string> = {
  normal: 'p-6 sm:p-8',
  tight: 'p-5',
};

interface CardProps extends HTMLAttributes<HTMLDivElement> {
  padding?: CardPadding;
}

/** The one card shell used for page content. Flat at rest, shadow only on hover (Stitch design system). */
export function Card({ padding = 'normal', className = '', ...rest }: CardProps) {
  return (
    <div
      className={`rounded-lg border border-[#E2E8F0] bg-white transition-shadow hover:shadow-sm ${PADDING_CLASSES[padding]} ${className}`}
      {...rest}
    />
  );
}

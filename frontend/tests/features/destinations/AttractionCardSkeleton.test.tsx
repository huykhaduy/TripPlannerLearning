import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { AttractionCardSkeleton } from '../../../src/features/destinations/AttractionCardSkeleton';

describe('AttractionCardSkeleton', () => {
  it('renders pulsing placeholder blocks', () => {
    const { container } = render(<AttractionCardSkeleton />);

    // The whole point of the component is the shimmer — a version that rendered
    // the right boxes with the animation dropped would look like a broken card.
    expect(container.querySelectorAll('.animate-pulse').length).toBeGreaterThan(0);
  });

  it('exposes nothing to assistive tech or the keyboard', () => {
    // It stands in for a card with a title and an "Add to trip" button. If any of
    // that leaked in as real text or a real control, users would read and tab
    // into placeholders. The aria-busy label lives on the surrounding grid.
    render(<AttractionCardSkeleton />);

    expect(screen.queryAllByRole('button')).toHaveLength(0);
    expect(screen.queryAllByRole('link')).toHaveLength(0);
    expect(screen.queryAllByRole('img')).toHaveLength(0);
  });
});

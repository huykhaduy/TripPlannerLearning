import { fireEvent, render } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { TripThumbnail, formatDates } from '../../../src/features/trips/TripThumbnail';

/**
 * The cover photo is decorative (alt=""), so it is deliberately absent from the
 * accessibility tree — getByRole('img') would never match it. Query by tag instead.
 */
function coverImage(container: HTMLElement): HTMLImageElement | null {
  return container.querySelector('img');
}

function gradientLayer(container: HTMLElement): HTMLElement | null {
  return container.querySelector('div[class*="bg-gradient-to-br"]');
}

describe('formatDates', () => {
  it('renders a complete range', () => {
    expect(formatDates('2026-03-10', '2026-03-12')).toBe('2026-03-10 → 2026-03-12');
  });

  it.each([
    ['both missing', null, null],
    ['no start', null, '2026-03-12'],
    ['no end', '2026-03-10', null],
  ])('says there are no dates yet when %s', (_label, start, end) => {
    // A half-open range is not a range — an undated trip is the normal starting
    // state, not an error.
    expect(formatDates(start, end)).toBe('No dates yet');
  });
});

describe('TripThumbnail', () => {
  it('shows the cover photo when the trip has one', () => {
    const { container } = render(
      <TripThumbnail id="trip-1" coverImageUrl="https://example.com/cover.jpg" />,
    );

    expect(coverImage(container)).toHaveAttribute('src', 'https://example.com/cover.jpg');
  });

  it('marks the photo as decorative', () => {
    const { container } = render(
      <TripThumbnail id="trip-1" coverImageUrl="https://example.com/cover.jpg" />,
    );

    // The trip name sits next to it in the DOM; announcing the image too would
    // just repeat that to a screen reader.
    expect(coverImage(container)).toHaveAttribute('alt', '');
  });

  it('falls back to a gradient when there is no photo', () => {
    const { container } = render(<TripThumbnail id="trip-1" coverImageUrl={null} />);

    expect(coverImage(container)).toBeNull();
    expect(gradientLayer(container)).toBeInTheDocument();
  });

  /** Provider photo URLs go stale; a broken-image icon would look like our bug. */
  it('falls back to the gradient when the photo fails to load', () => {
    const { container } = render(
      <TripThumbnail id="trip-1" coverImageUrl="https://example.com/gone.jpg" />,
    );

    fireEvent.error(coverImage(container)!);

    expect(coverImage(container)).toBeNull();
    expect(gradientLayer(container)).toBeInTheDocument();
  });

  describe('the gradient fallback', () => {
    /**
     * Hashed from the trip id rather than random, so a card keeps the same colour
     * across reloads. Asserted by comparing two renders instead of pinning a specific
     * gradient — that would test the hash arithmetic rather than the guarantee.
     */
    it('is stable for the same trip id', () => {
      // Six renders, not two: there are only four gradients, so a random
      // implementation would still match on a single re-render about a quarter of
      // the time. Six agreeing runs makes that essentially impossible.
      const classes = new Set<string>();
      for (let i = 0; i < 6; i++) {
        const { container, unmount } = render(<TripThumbnail id="trip-abc" coverImageUrl={null} />);
        classes.add(gradientLayer(container)!.className);
        unmount();
      }

      expect(classes.size).toBe(1);
    });

    it('varies across trips rather than always picking one colour', () => {
      // A constant would satisfy "stable" too, so prove the id actually matters.
      const seen = new Set<string>();
      for (const id of ['a', 'b', 'c', 'd', 'e', 'f', 'g', 'h']) {
        const { container, unmount } = render(<TripThumbnail id={id} coverImageUrl={null} />);
        seen.add(gradientLayer(container)!.className);
        unmount();
      }

      expect(seen.size).toBeGreaterThan(1);
    });

    it('handles an empty id without crashing', () => {
      // Defensive: the hash reduces over the characters, so '' sums to 0.
      const { container } = render(<TripThumbnail id="" coverImageUrl={null} />);

      expect(gradientLayer(container)).toBeInTheDocument();
    });
  });
});

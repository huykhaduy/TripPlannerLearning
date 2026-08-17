import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it } from 'vitest';
import { TripDetailSkeleton } from '../../../src/features/trips/TripDetailSkeleton';

/** Renders a Link, so it needs router context. */
function renderSkeleton() {
  return render(
    <MemoryRouter>
      <TripDetailSkeleton />
    </MemoryRouter>,
  );
}

describe('TripDetailSkeleton', () => {
  it('marks itself busy for assistive tech', () => {
    renderSkeleton();

    expect(screen.getByRole('region', { name: 'Loading trip' })).toHaveAttribute('aria-busy', 'true');
  });

  it('renders pulsing placeholder blocks', () => {
    const { container } = renderSkeleton();

    // Without the animation this is just a page of grey boxes, which reads as
    // broken rather than loading.
    expect(container.querySelectorAll('.animate-pulse').length).toBeGreaterThan(0);
  });

  it('keeps the back link real so a slow load is escapable', () => {
    renderSkeleton();

    expect(screen.getByRole('link', { name: '← Back to my trips' })).toHaveAttribute('href', '/trips');
  });

  it('renders the fixed Saved Places chrome for real', () => {
    // The heading and hint don't depend on trip data, so placeholding them would
    // hide information we already have.
    renderSkeleton();

    expect(screen.getByRole('heading', { name: 'Saved Places' })).toBeInTheDocument();
    expect(screen.getByText('Drag items into a day to schedule them.')).toBeInTheDocument();
  });

  it('offers nothing else to click or type into', () => {
    // Everything but the back link stands in for data that hasn't arrived — an
    // Edit button or search box here would be a control that cannot work yet.
    renderSkeleton();

    expect(screen.queryAllByRole('button')).toHaveLength(0);
    expect(screen.queryAllByRole('textbox')).toHaveLength(0);
    expect(screen.queryAllByRole('searchbox')).toHaveLength(0);
    expect(screen.getAllByRole('link')).toHaveLength(1);
  });
});

import { fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it, vi } from 'vitest';
import { AuthProvider } from '../../auth/AuthContext';
import { AttractionCard } from './AttractionCard';
import type { AttractionSummary } from '../../types';

vi.mock('../../api/auth', () => ({
  login: vi.fn(),
  register: vi.fn(),
  verifyEmail: vi.fn(),
  resendVerificationEmail: vi.fn(),
}));

// AttractionCard embeds AddToTripButton, which reads the user's trips when opened.
vi.mock('../../api/trips', () => ({
  getMyTrips: vi.fn(),
  getTrip: vi.fn(),
  createTrip: vi.fn(),
  updateTrip: vi.fn(),
  addDestination: vi.fn(),
  updateItineraryItem: vi.fn(),
  removeDestination: vi.fn(),
}));

function attraction(overrides: Partial<AttractionSummary> = {}): AttractionSummary {
  return {
    providerId: 'geo-123',
    name: 'Golden Bridge',
    category: 'landmark',
    imageUrl: null,
    rating: null,
    ...overrides,
  };
}

function renderCard(value = attraction()) {
  return render(
    <AuthProvider>
      <MemoryRouter>
        <AttractionCard attraction={value} />
      </MemoryRouter>
    </AuthProvider>,
  );
}

describe('AttractionCard', () => {
  it('links to the destination details page', () => {
    renderCard();

    expect(screen.getByRole('link', { name: /Golden Bridge/ })).toHaveAttribute(
      'href',
      '/destinations/geo-123',
    );
  });

  /** Geoapify place ids contain characters that would otherwise break the path. */
  it('encodes the provider id in the URL', () => {
    renderCard(attraction({ providerId: 'geo/with spaces&stuff' }));

    expect(screen.getByRole('link', { name: /Golden Bridge/ })).toHaveAttribute(
      'href',
      `/destinations/${encodeURIComponent('geo/with spaces&stuff')}`,
    );
  });

  it('shows the category when there is one', () => {
    renderCard();

    expect(screen.getByText('landmark')).toBeInTheDocument();
  });

  it('omits the category chip when the provider gave none', () => {
    renderCard(attraction({ category: null }));

    expect(screen.queryByText('landmark')).not.toBeInTheDocument();
    // The card still renders — a missing category is normal, not an error.
    expect(screen.getByText('Golden Bridge')).toBeInTheDocument();
  });

  it('shows a rating to one decimal place', () => {
    renderCard(attraction({ rating: 4.25 }));

    expect(screen.getByText('⭐ 4.3')).toBeInTheDocument();
  });

  /** Geoapify has no ratings at all, so null is the common case — not "0 stars". */
  it('omits the rating entirely when there is none', () => {
    renderCard(attraction({ rating: null }));

    expect(screen.queryByText(/⭐/)).not.toBeInTheDocument();
  });

  it('shows a zero rating rather than hiding it', () => {
    // != null, not truthiness — 0 is a real rating and must not vanish.
    renderCard(attraction({ rating: 0 }));

    expect(screen.getByText('⭐ 0.0')).toBeInTheDocument();
  });

  describe('the photo', () => {
    it('renders the image when one is supplied', () => {
      renderCard(attraction({ imageUrl: 'https://example.com/bridge.jpg' }));

      expect(screen.getByRole('img', { name: 'Golden Bridge' })).toHaveAttribute(
        'src',
        'https://example.com/bridge.jpg',
      );
    });

    it('falls back to the placeholder when there is no image', () => {
      renderCard();

      expect(screen.queryByRole('img')).not.toBeInTheDocument();
      expect(screen.getByText('🏛️')).toBeInTheDocument();
    });

    /**
     * Provider image URLs go stale — without this the card would show the browser's
     * broken-image icon, which looks like a bug in our page rather than a dead link.
     */
    it('swaps in the placeholder when the image fails to load', () => {
      renderCard(attraction({ imageUrl: 'https://example.com/gone.jpg' }));

      fireEvent.error(screen.getByRole('img', { name: 'Golden Bridge' }));

      expect(screen.queryByRole('img')).not.toBeInTheDocument();
      expect(screen.getByText('🏛️')).toBeInTheDocument();
    });
  });

  it('keeps the add-to-trip button outside the details link', () => {
    renderCard();

    // A <button> nested inside an <a> is invalid HTML and swallows the click.
    const link = screen.getByRole('link', { name: /Golden Bridge/ });
    const addButton = screen.getByRole('button', { name: 'Add to trip' });
    expect(link).not.toContainElement(addButton);
  });
});

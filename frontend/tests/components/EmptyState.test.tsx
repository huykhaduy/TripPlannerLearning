import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { EmptyState } from '../../src/components/EmptyState';

describe('EmptyState', () => {
  it('shows the message it is given', () => {
    render(<EmptyState message="No trips yet — plan your first one." />);

    expect(screen.getByText('No trips yet — plan your first one.')).toBeInTheDocument();
  });

  it('falls back to the compass icon', () => {
    render(<EmptyState message="Nothing here." />);

    expect(screen.getByText('🧭')).toBeInTheDocument();
  });

  it('uses a caller-supplied icon instead', () => {
    render(<EmptyState icon="🔍" message="No attractions matched." />);

    expect(screen.getByText('🔍')).toBeInTheDocument();
    expect(screen.queryByText('🧭')).not.toBeInTheDocument();
  });

  it('hides the icon from assistive tech', () => {
    // The emoji is decoration — the message already says what is going on, and a
    // screen reader announcing "compass" before it would just be noise.
    render(<EmptyState message="Nothing here." />);

    expect(screen.getByText('🧭')).toHaveAttribute('aria-hidden', 'true');
  });

  it('leaves the message itself readable to assistive tech', () => {
    // Pairing the absence check above with a positive one: a version that hid the
    // whole block from the accessibility tree would also pass the assertion alone.
    render(<EmptyState message="No trips yet." />);

    expect(screen.getByText('No trips yet.')).not.toHaveAttribute('aria-hidden');
  });
});

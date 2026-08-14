import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { Avatar } from '../../src/components/Avatar';

describe('Avatar', () => {
  it('shows the first letter of the label, uppercased', () => {
    render(<Avatar label="ada@example.com" />);

    expect(screen.getByText('A')).toBeInTheDocument();
  });

  it('leaves an already-uppercase initial alone', () => {
    render(<Avatar label="Ada Lovelace" />);

    expect(screen.getByText('A')).toBeInTheDocument();
  });

  it('ignores leading whitespace when picking the initial', () => {
    // App.tsx passes `user.displayName || user.email` straight through, and a
    // display name that came back with a stray space would otherwise render blank.
    render(<Avatar label="   Grace" />);

    expect(screen.getByText('G')).toBeInTheDocument();
  });

  it('falls back to ? for an empty label', () => {
    render(<Avatar label="" />);

    expect(screen.getByText('?')).toBeInTheDocument();
  });

  it('falls back to ? for a whitespace-only label', () => {
    render(<Avatar label="   " />);

    expect(screen.getByText('?')).toBeInTheDocument();
  });

  it('keeps a non-Latin initial intact', () => {
    // toUpperCase() on a script without case is a no-op, not a dropped character.
    render(<Avatar label="Đức" />);

    expect(screen.getByText('Đ')).toBeInTheDocument();
  });

  it('renders exactly one character', () => {
    const { container } = render(<Avatar label="Ada Lovelace" />);

    expect(container.textContent).toBe('A');
  });
});

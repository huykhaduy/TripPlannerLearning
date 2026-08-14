import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { Card } from '../../src/components/Card';

describe('Card', () => {
  it('renders its children', () => {
    render(<Card>Trip summary</Card>);

    expect(screen.getByText('Trip summary')).toBeInTheDocument();
  });

  it('defaults to the roomy page padding', () => {
    const { container } = render(<Card>content</Card>);

    expect(container.firstElementChild).toHaveClass('p-6', 'sm:p-8');
  });

  it('switches to tight padding on request', () => {
    const { container } = render(<Card padding="tight">content</Card>);

    const card = container.firstElementChild;
    expect(card).toHaveClass('p-5');
    expect(card).not.toHaveClass('p-6');
  });

  it('stays flat at rest and lifts only on hover', () => {
    // The Stitch design system calls for a flat card with a hover shadow; a
    // resting shadow class here would make every list look busy.
    const { container } = render(<Card>content</Card>);

    const card = container.firstElementChild;
    expect(card).toHaveClass('hover:shadow-sm');
    expect(card).not.toHaveClass('shadow-sm');
  });

  it('appends a caller className without dropping the shell classes', () => {
    const { container } = render(<Card className="flex gap-4">content</Card>);

    const card = container.firstElementChild;
    expect(card).toHaveClass('flex', 'gap-4');
    expect(card).toHaveClass('rounded-lg', 'bg-white');
  });

  it('forwards arbitrary div props such as onClick', async () => {
    // TripsPage wraps its rows in a clickable Card, so the spread has to reach
    // the underlying div rather than being swallowed by the wrapper.
    const onClick = vi.fn();
    render(<Card onClick={onClick} data-testid="row">Vietnam 2026</Card>);

    await userEvent.click(screen.getByTestId('row'));

    expect(onClick).toHaveBeenCalledTimes(1);
  });
});

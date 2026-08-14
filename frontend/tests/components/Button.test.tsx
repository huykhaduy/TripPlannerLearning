import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { Button } from '../../src/components/Button';

describe('Button', () => {
  it('renders its children inside a real button element', () => {
    render(<Button>Save</Button>);

    expect(screen.getByRole('button', { name: 'Save' })).toBeInTheDocument();
  });

  it('defaults to the primary variant at medium size', () => {
    render(<Button>Save</Button>);

    const button = screen.getByRole('button');
    expect(button).toHaveClass('bg-brand-600');
    expect(button).toHaveClass('px-4', 'py-2');
  });

  it('uses the action color for the one call-to-action per view', () => {
    // 'action' is reserved for Add to trip / Create trip / Plan new trip; if it
    // silently fell back to primary those would stop standing out.
    render(<Button variant="action">Add to trip</Button>);

    expect(screen.getByRole('button')).toHaveClass('bg-action-500');
  });

  it.each([
    ['secondary', 'border-slate-300'],
    ['outline', 'border-brand-200'],
    ['danger', 'border-red-200'],
  ] as const)('maps the %s variant to its own border', (variant, expected) => {
    render(<Button variant={variant}>Go</Button>);

    expect(screen.getByRole('button')).toHaveClass(expected);
  });

  it('tightens the padding at the small size', () => {
    render(<Button size="sm">Go</Button>);

    const button = screen.getByRole('button');
    expect(button).toHaveClass('px-3', 'py-1.5');
    expect(button).not.toHaveClass('px-4');
  });

  it('appends a caller className instead of replacing the variant classes', () => {
    render(<Button className="w-full">Go</Button>);

    const button = screen.getByRole('button');
    expect(button).toHaveClass('w-full');
    expect(button).toHaveClass('bg-brand-600');
  });

  it('passes type through so a button inside a form does not submit it by accident', () => {
    render(<Button type="button">Cancel</Button>);

    expect(screen.getByRole('button')).toHaveAttribute('type', 'button');
  });

  it('calls onClick when pressed', async () => {
    const onClick = vi.fn();
    render(<Button onClick={onClick}>Go</Button>);

    await userEvent.click(screen.getByRole('button'));

    expect(onClick).toHaveBeenCalledTimes(1);
  });

  it('does not fire onClick while disabled', async () => {
    // Pages disable this during in-flight requests; a click that still landed
    // would double-submit the create-trip and add-to-trip calls.
    const onClick = vi.fn();
    render(<Button disabled onClick={onClick}>Saving…</Button>);

    await userEvent.click(screen.getByRole('button'));

    expect(onClick).not.toHaveBeenCalled();
    expect(screen.getByRole('button')).toBeDisabled();
  });
});

import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { Modal } from '../../src/components/Modal';

describe('Modal', () => {
  it('renders as a labelled modal dialog', () => {
    render(
      <Modal title="Plan new trip" onClose={vi.fn()}>
        <p>body</p>
      </Modal>,
    );

    const dialog = screen.getByRole('dialog');
    expect(dialog).toHaveAttribute('aria-modal', 'true');
    expect(dialog).toHaveAccessibleName('Plan new trip');
    expect(screen.getByRole('heading', { name: 'Plan new trip' })).toBeInTheDocument();
    expect(screen.getByText('body')).toBeInTheDocument();
  });

  describe('closing', () => {
    it('closes on the header close button', async () => {
      const onClose = vi.fn();
      render(<Modal title="Edit trip details" onClose={onClose}>body</Modal>);

      await userEvent.click(screen.getByRole('button', { name: 'Close' }));

      expect(onClose).toHaveBeenCalledTimes(1);
    });

    it('closes on Escape', async () => {
      const onClose = vi.fn();
      render(<Modal title="Edit trip details" onClose={onClose}>body</Modal>);

      await userEvent.keyboard('{Escape}');

      expect(onClose).toHaveBeenCalledTimes(1);
    });

    it('closes on a click outside the panel', async () => {
      const onClose = vi.fn();
      const { container } = render(<Modal title="Edit trip details" onClose={onClose}>body</Modal>);

      await userEvent.click(container.firstElementChild as HTMLElement);

      expect(onClose).toHaveBeenCalledTimes(1);
    });

    it('does not close on a click inside the panel', async () => {
      // The panel stops propagation; without it, every click on a form field would
      // bubble to the backdrop and shut the modal mid-edit.
      const onClose = vi.fn();
      render(
        <Modal title="Edit trip details" onClose={onClose}>
          <input aria-label="Trip name" />
        </Modal>,
      );

      await userEvent.click(screen.getByLabelText('Trip name'));

      expect(onClose).not.toHaveBeenCalled();
      expect(screen.getByRole('dialog')).toBeInTheDocument();
    });

    it('stops listening for Escape once unmounted', async () => {
      // The listener is on `document`, so a leaked one would keep firing against a
      // closed modal — and, worse, reopen-then-Escape would call onClose twice.
      const onClose = vi.fn();
      const { unmount } = render(<Modal title="Edit trip details" onClose={onClose}>body</Modal>);

      unmount();
      await userEvent.keyboard('{Escape}');

      expect(onClose).not.toHaveBeenCalled();
    });
  });

  describe('focus management', () => {
    it('moves focus into the panel on open', () => {
      render(<Modal title="Plan new trip" onClose={vi.fn()}>body</Modal>);

      expect(screen.getByRole('dialog')).toHaveFocus();
    });

    it('restores focus to the trigger when it closes', async () => {
      render(<button type="button">Plan new trip</button>);
      const trigger = screen.getByRole('button', { name: 'Plan new trip' });
      trigger.focus();

      const { unmount } = render(<Modal title="Plan new trip" onClose={vi.fn()}>body</Modal>);
      expect(trigger).not.toHaveFocus();

      unmount();

      expect(trigger).toHaveFocus();
    });

    it('does not steal focus back from a field the user is typing in', async () => {
      // The mount effect depends on [] and reads onClose through a ref. All three
      // call sites recreate their onClose on every render, and the modal's own form
      // state lives in that same component — so depending on [onClose] here would
      // re-run this effect on every keystroke and yank focus out of the input.
      const { rerender } = render(
        <Modal title="Plan new trip" onClose={() => {}}>
          <input aria-label="Trip name" />
        </Modal>,
      );

      const input = screen.getByLabelText('Trip name');
      await userEvent.type(input, 'Viet');
      expect(input).toHaveFocus();

      rerender(
        <Modal title="Plan new trip" onClose={() => {}}>
          <input aria-label="Trip name" />
        </Modal>,
      );

      expect(input).toHaveFocus();
      expect(screen.getByRole('dialog')).not.toHaveFocus();
    });

    it('still calls the latest onClose after a re-render', async () => {
      // The flip side of the mount-only effect: the ref has to be kept current, or
      // Escape would invoke the onClose captured at open time and close over stale
      // form state.
      const stale = vi.fn();
      const fresh = vi.fn();
      const { rerender } = render(<Modal title="Plan new trip" onClose={stale}>body</Modal>);

      rerender(<Modal title="Plan new trip" onClose={fresh}>body</Modal>);
      await userEvent.keyboard('{Escape}');

      expect(fresh).toHaveBeenCalledTimes(1);
      expect(stale).not.toHaveBeenCalled();
    });
  });

  describe('width', () => {
    it('defaults to the standard panel width', () => {
      render(<Modal title="Plan new trip" onClose={vi.fn()}>body</Modal>);

      expect(screen.getByRole('dialog')).toHaveClass('max-w-lg');
    });

    it('takes a caller-supplied width', () => {
      render(<Modal title="Add to trip" onClose={vi.fn()} maxWidth="max-w-md">body</Modal>);

      const dialog = screen.getByRole('dialog');
      expect(dialog).toHaveClass('max-w-md');
      expect(dialog).not.toHaveClass('max-w-lg');
    });
  });
});

import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { Field, fieldControlClass } from '../../src/components/Field';

describe('Field', () => {
  it('associates the label with the control it wraps', async () => {
    // Field wraps rather than using htmlFor/id, so the association is implicit —
    // if that ever changed to a plain <div>, every getByLabelText in the auth and
    // trip page tests would start failing and this says why.
    render(
      <Field label="Email">
        <input type="email" />
      </Field>,
    );

    expect(screen.getByLabelText('Email')).toHaveAttribute('type', 'email');
  });

  it('focuses the control when the label is clicked', async () => {
    render(
      <Field label="Trip name">
        <input />
      </Field>,
    );

    await userEvent.click(screen.getByText('Trip name'));

    expect(screen.getByLabelText('Trip name')).toHaveFocus();
  });

  it('works for a select as well as an input', () => {
    render(
      <Field label="Day">
        <select>
          <option>Day 1</option>
        </select>
      </Field>,
    );

    expect(screen.getByLabelText('Day').tagName).toBe('SELECT');
  });

  it('appends a caller className to the wrapper', () => {
    const { container } = render(
      <Field label="Name" className="sm:col-span-2">
        <input />
      </Field>,
    );

    const label = container.querySelector('label');
    expect(label).toHaveClass('sm:col-span-2');
    expect(label).toHaveClass('flex', 'flex-col');
  });

  it('exports the shared control styling for the input itself to use', () => {
    // The wrapper styles the label; the control has to opt in, so the class string
    // is exported rather than applied here. Pin the focus ring, which is the part
    // callers would silently lose by hand-rolling their own border classes.
    expect(fieldControlClass).toContain('border-slate-300');
    expect(fieldControlClass).toContain('focus:border-brand-600');
    expect(fieldControlClass).toContain('focus:outline-none');
  });
});

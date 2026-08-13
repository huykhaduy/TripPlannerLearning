import { act, renderHook, waitFor } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { useDebounce } from '../../src/hooks/useDebounce';

/**
 * Real timers, matching CitySearchInput.test.tsx: the delays here fit inside
 * waitFor's 1 s default, and driving fake timers through React's act() buys
 * brittleness without buying speed at this scale.
 */
describe('useDebounce', () => {
  /** Renders the hook while recording every value it has emitted. */
  function renderRecording<T>(initial: T, delayMs: number) {
    const seen: T[] = [];
    const view = renderHook(
      ({ value }: { value: T }) => {
        const debounced = useDebounce(value, delayMs);
        seen.push(debounced);
        return debounced;
      },
      { initialProps: { value: initial } },
    );
    return { ...view, seen };
  }

  it('returns the initial value straight away', () => {
    // The first render must not be blank for `delayMs` — CitySearchInput seeds
    // this from its input value and would flash an empty query otherwise.
    const { result } = renderHook(() => useDebounce('hanoi', 50));

    expect(result.current).toBe('hanoi');
  });

  it('holds the previous value until the delay has passed', async () => {
    const { result, rerender } = renderHook(({ value }) => useDebounce(value, 50), {
      initialProps: { value: 'h' },
    });

    rerender({ value: 'ha' });
    expect(result.current).toBe('h');

    await waitFor(() => expect(result.current).toBe('ha'));
  });

  it('collapses a burst of changes into the last one', async () => {
    const { result, rerender, seen } = renderRecording('h', 50);

    // Each change restarts the timer, so only the final value should ever land.
    act(() => {
      rerender({ value: 'ha' });
      rerender({ value: 'han' });
      rerender({ value: 'hano' });
      rerender({ value: 'hanoi' });
    });

    await waitFor(() => expect(result.current).toBe('hanoi'));
    expect(seen).not.toContain('ha');
    expect(seen).not.toContain('han');
    expect(seen).not.toContain('hano');
  });

  it('never emits a value the caller has already moved away from', async () => {
    const { result, rerender, seen } = renderRecording('paris', 50);
    await waitFor(() => expect(result.current).toBe('paris'));

    act(() => {
      rerender({ value: 'rome' });
      rerender({ value: 'paris' });
    });

    // 'rome' existed only between two synchronous renders. Emitting it would send
    // CitySearchInput off to search a city the user never finished typing.
    await new Promise((resolve) => setTimeout(resolve, 120));
    expect(seen).not.toContain('rome');
    expect(result.current).toBe('paris');
  });

  it('honours a caller-supplied delay', async () => {
    const { result, rerender } = renderHook(({ value }) => useDebounce(value, 400), {
      initialProps: { value: 'a' },
    });

    rerender({ value: 'b' });
    await new Promise((resolve) => setTimeout(resolve, 120));
    expect(result.current).toBe('a');

    await waitFor(() => expect(result.current).toBe('b'), { timeout: 1000 });
  });

  it('debounces values of any type, not just strings', async () => {
    const { result, rerender } = renderHook(({ value }) => useDebounce(value, 50), {
      initialProps: { value: { lat: 1 } as { lat: number } },
    });

    rerender({ value: { lat: 2 } });

    await waitFor(() => expect(result.current).toEqual({ lat: 2 }));
  });

  it('clears its pending timer when the caller unmounts', () => {
    // Asserting on the setTimeout/clearTimeout pair rather than "nothing threw":
    // React makes a setState on an unmounted component a silent no-op, so a test
    // that only checked for an error could never fail if the cleanup were dropped.
    const setSpy = vi.spyOn(globalThis, 'setTimeout');
    const { unmount } = renderHook(() => useDebounce('a', 50));
    const created = setSpy.mock.results.map((r) => r.value);

    const clearSpy = vi.spyOn(globalThis, 'clearTimeout');
    unmount();
    const cleared = clearSpy.mock.calls.map((call) => call[0]);

    expect(created.length).toBeGreaterThan(0);
    expect(created.some((id) => cleared.includes(id))).toBe(true);

    setSpy.mockRestore();
    clearSpy.mockRestore();
  });
});

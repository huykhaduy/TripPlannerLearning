import { useEffect, useRef, type ReactNode } from 'react';

/**
 * Shared modal shell: backdrop (click-outside-to-close) + centered panel
 * with a title/close header and padded body. On mount: moves focus into the
 * panel and starts listening for Escape; on unmount: restores focus to
 * whatever was focused before the modal opened. No Tab-cycle focus trap —
 * Tab can still move focus to page content behind the backdrop.
 */
export function Modal({
  title,
  onClose,
  children,
  maxWidth = 'max-w-lg',
}: {
  title: string;
  onClose: () => void;
  children: ReactNode;
  maxWidth?: string;
}) {
  const panelRef = useRef<HTMLDivElement>(null);
  const previouslyFocused = useRef<HTMLElement | null>(null);

  // Read the latest onClose via a ref instead of depending on it directly.
  // All 3 call sites (TripsPage/TripDetailPage's closeModal/closeEditModal,
  // AddToTripButton's inline arrow) pass a plain function that's recreated on
  // every render of their owning component, and typing into that modal's own
  // fields lives in that same component's state — so depending on [onClose]
  // here would re-run this effect (stealing focus back to the panel, off
  // whatever the user just typed into) on every keystroke in the "Plan new
  // trip" and "Edit trip details" forms. Mount/unmount only, via [], keeps
  // the focus-management + Escape-listener setup a one-time effect while
  // still always calling the current onClose.
  const onCloseRef = useRef(onClose);
  onCloseRef.current = onClose;

  useEffect(() => {
    previouslyFocused.current = document.activeElement as HTMLElement | null;
    panelRef.current?.focus();

    function handleKeyDown(event: KeyboardEvent) {
      if (event.key === 'Escape') onCloseRef.current();
    }
    document.addEventListener('keydown', handleKeyDown);

    return () => {
      document.removeEventListener('keydown', handleKeyDown);
      previouslyFocused.current?.focus();
    };
  }, []);

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-slate-900/40 p-4" onClick={onClose}>
      <div
        ref={panelRef}
        tabIndex={-1}
        className={`max-h-[90vh] w-full overflow-y-auto ${maxWidth} rounded-2xl bg-white shadow-2xl`}
        onClick={(e) => e.stopPropagation()}
        role="dialog"
        aria-modal="true"
        aria-label={title}
      >
        <div className="flex items-center justify-between border-b border-[#E2E8F0] px-6 py-4">
          <h2 className="font-headline text-lg font-semibold text-slate-900">{title}</h2>
          <button type="button" onClick={onClose} className="text-slate-400 hover:text-slate-600" aria-label="Close">
            ✕
          </button>
        </div>
        <div className="p-6">{children}</div>
      </div>
    </div>
  );
}

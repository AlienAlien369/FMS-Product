import { useEffect, useRef, useState } from 'react';
import type { ReactNode } from 'react';
import { ChevronDown, ChevronLeft, ChevronRight, MoreVertical, X } from 'lucide-react';

/**
 * Shared responsive primitives — one mobile treatment for every page so the
 * app doesn't invent per-page behaviors.
 *
 * Conventions (standard breakpoints):
 *   <640px  mobile      <768px  (md)  tablet  ≥1024px  (lg)  desktop
 * Tables render stacked cards below md; modals become full-screen sheets;
 * filter chips scroll horizontally; touch targets stay ≥44px on touch.
 */

// ── ModalSheet ─────────────────────────────────────────────────────────────
// Desktop: centered dialog (max-width + 85vh cap). Mobile: full-screen sheet
// (100dvh) with the caller's header/footer pinned by the flex column — the
// scrollable body sits between them, and dvh keeps the footer clear of the
// mobile browser chrome / keyboard.
export function ModalSheet({ children, onClose, maxWidth = 'max-w-2xl', labelledBy }: {
  children: ReactNode; onClose: () => void; maxWidth?: string; labelledBy?: string;
}) {
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') onClose(); };
    document.addEventListener('keydown', onKey);
    document.body.style.overflow = 'hidden';
    return () => { document.removeEventListener('keydown', onKey); document.body.style.overflow = ''; };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);
  return (
    <div className="fixed inset-0 z-50 flex items-end sm:items-center justify-center sm:p-4" role="dialog" aria-modal="true" aria-labelledby={labelledBy}>
      <div className="fixed inset-0 bg-black/50" onClick={onClose} />
      <div className={`relative bg-white w-full sm:rounded-xl sm:shadow-2xl h-[100dvh] sm:h-auto sm:max-h-[85vh] flex flex-col overflow-hidden ${maxWidth}`}>
        {children}
      </div>
    </div>
  );
}

/** Consistent modal header — back-title + close. */
export function ModalHeader({ title, onClose, children }: { title: ReactNode; onClose: () => void; children?: ReactNode }) {
  return (
    <div className="flex items-center justify-between px-4 sm:px-6 py-3.5 border-b border-gray-200 shrink-0">
      <h2 className="text-lg font-semibold text-gray-900">{title}</h2>
      <div className="flex items-center gap-2">
        {children}
        <button onClick={onClose} aria-label="Close" className="p-2.5 hover:bg-gray-100 rounded-lg text-gray-500 min-w-[44px] min-h-[44px] flex items-center justify-center">
          <X className="w-5 h-5" />
        </button>
      </div>
    </div>
  );
}

/** Consistent modal footer — Cancel/Submit, pinned (reachable, not keyboard-obscured). */
export function ModalFooter({ children }: { children: ReactNode }) {
  return (
    <div className="flex justify-end gap-3 px-4 sm:px-6 py-3.5 border-t border-gray-200 shrink-0 pb-[max(1rem,env(safe-area-inset-bottom))]">
      {children}
    </div>
  );
}

/** Primary / secondary action buttons with touch-friendly sizing. */
export const BtnPrimary = 'min-h-[44px] px-4 sm:px-5 py-2 bg-blue-600 text-white text-sm font-medium rounded-lg hover:bg-blue-700 disabled:bg-blue-400 disabled:cursor-not-allowed';
export const BtnSecondary = 'min-h-[44px] px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-100 rounded-lg';
export const BtnDanger = 'min-h-[44px] px-4 py-2 bg-red-600 text-white text-sm font-medium rounded-lg hover:bg-red-700';

// ── Overflow menu (mobile row actions → single "…" button) ─────────────────
// Desktop pages keep their inline icon rows; this is for the stacked-card view.
// The menu is rendered `fixed` (not `absolute`): cards use overflow-hidden for
// their rounded corners, and an absolutely-positioned menu taller than the card
// would be clipped. Fixed positioning escapes the card's clip (containing block
// is the viewport, not the card), so a 4th action or a short card can never
// hide items. Position is computed from the trigger button at open time.
export function OverflowMenu({ items }: { items: { key: string; label: string; icon?: ReactNode; onClick: () => void; danger?: boolean }[] }) {
  const [open, setOpen] = useState(false);
  const [pos, setPos] = useState<{ top: number; left: number } | null>(null);
  const ref = useRef<HTMLDivElement>(null);
  const btnRef = useRef<HTMLButtonElement>(null);

  useEffect(() => {
    if (!open) return;
    const onDoc = (e: MouseEvent) => { if (ref.current && !ref.current.contains(e.target as Node)) setOpen(false); };
    const onMove = () => setOpen(false); // scroll/resize would misplace the fixed menu — close it
    document.addEventListener('mousedown', onDoc);
    window.addEventListener('scroll', onMove, true);
    window.addEventListener('resize', onMove);
    return () => {
      document.removeEventListener('mousedown', onDoc);
      window.removeEventListener('scroll', onMove, true);
      window.removeEventListener('resize', onMove);
    };
  }, [open]);

  const toggle = () => {
    setOpen(o => {
      const next = !o;
      if (next && btnRef.current) {
        const r = btnRef.current.getBoundingClientRect();
        const W = 176; // w-44
        const pad = 8;
        const left = Math.min(Math.max(r.right - W, pad), Math.max(pad, window.innerWidth - W - pad));
        setPos({ top: r.bottom + 4, left });
      }
      return next;
    });
  };

  return (
    <div className="relative" ref={ref}>
      <button ref={btnRef} onClick={toggle} aria-label="Row actions"
        className="p-2.5 rounded-lg hover:bg-gray-100 text-gray-500 min-w-[44px] min-h-[44px] flex items-center justify-center">
        <MoreVertical className="w-5 h-5" />
      </button>
      {open && pos && (
        <div className="fixed z-30 w-44 bg-white border border-gray-200 rounded-xl shadow-lg py-1" style={{ top: pos.top, left: pos.left }}>
          {items.map(i => (
            <button key={i.key} onClick={() => { setOpen(false); i.onClick(); }}
              className={`w-full flex items-center gap-2.5 px-4 py-3 text-sm text-left hover:bg-gray-50 min-h-[44px] ${i.danger ? 'text-red-600' : 'text-gray-700'}`}>
              {i.icon} {i.label}
            </button>
          ))}
        </div>
      )}
    </div>
  );
}

// ── SheetDropdown — bottom sheet below sm, anchored dropdown from sm up ──────
// One owner for the mobile/desktop panel treatment shared by the notification
// bell and the company-scope selector. These previously each hand-rolled this
// pattern — and each missed `sm:bottom-auto`, so the desktop dropdown had both
// top and bottom set and collapsed to ~2px. Below sm the panel is a fixed
// bottom sheet; from sm up it anchors under the trigger as a dropdown.
export function SheetDropdown({ open, onOpenChange, trigger, header, children, panelClass = '' }: {
  open: boolean;
  onOpenChange: (o: boolean) => void;
  trigger: ReactNode;
  header?: ReactNode;
  children: ReactNode;
  panelClass?: string;
}) {
  const ref = useRef<HTMLDivElement>(null);
  useEffect(() => {
    if (!open) return;
    const onDoc = (e: MouseEvent) => { if (ref.current && !ref.current.contains(e.target as Node)) onOpenChange(false); };
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') onOpenChange(false); };
    document.addEventListener('mousedown', onDoc);
    document.addEventListener('keydown', onKey);
    return () => { document.removeEventListener('mousedown', onDoc); document.removeEventListener('keydown', onKey); };
  }, [open, onOpenChange]);
  return (
    <div className="relative" ref={ref}>
      {trigger}
      {open && (
        <div className={`fixed sm:absolute inset-x-0 bottom-0 sm:bottom-auto sm:inset-x-auto sm:right-0 sm:top-full sm:mt-2 z-50 bg-white sm:rounded-xl sm:border sm:border-gray-200 sm:shadow-xl overflow-hidden flex flex-col sm:max-h-[75vh] rounded-t-2xl ${panelClass}`}>
          {header}
          {children}
        </div>
      )}
    </div>
  );
}

// ── ResponsiveCards — the mobile half of every data table ──────────────────
// Desktop tables stay untouched (wrapped in `hidden md:block`); below md this
// renders one stacked card per row: title + status chip + overflow actions on
// the first line, 2-3 deliberate primary fields, then an expandable "More
// details" section with every remaining field. The page supplies ALL of the
// render functions, so permission-gated actions stay gated in this view too.
export function ResponsiveCards<T>({ items, loading, empty = 'Nothing to show', keyOf, title, subtitle, statusChip, primary, details, actions, footer }: {
  items: T[];
  loading?: boolean;
  empty?: string;
  keyOf: (item: T) => string;
  title: (item: T) => ReactNode;
  subtitle?: (item: T) => ReactNode;
  statusChip?: (item: T) => ReactNode;
  primary?: (item: T) => { label: string; value: ReactNode }[];
  details?: (item: T) => ReactNode;
  actions?: (item: T) => { key: string; label: string; icon?: ReactNode; onClick: () => void; danger?: boolean }[];
  footer?: ReactNode;
}) {
  const [expanded, setExpanded] = useState<string | null>(null);
  return (
    <div className="md:hidden space-y-3">
      {loading ? (
        <div className="bg-white rounded-xl border border-gray-200 px-4 py-12 text-center text-sm text-gray-400">Loading…</div>
      ) : items.length === 0 ? (
        <div className="bg-white rounded-xl border border-gray-200 px-4 py-12 text-center text-sm text-gray-400">{empty}</div>
      ) : items.map(item => {
        const id = keyOf(item);
        const open = expanded === id;
        return (
          <div key={id} className="bg-white rounded-xl border border-gray-200 overflow-hidden">
            <div className="px-4 pt-3.5">
              <div className="flex items-start justify-between gap-2">
                <div className="min-w-0 flex-1">
                  <div className="text-[15px] font-semibold text-gray-900 leading-snug break-words">{title(item)}</div>
                  {subtitle && <div className="text-[13px] text-gray-500 mt-0.5">{subtitle(item)}</div>}
                </div>
                <div className="flex items-center gap-1 shrink-0">
                  {statusChip && statusChip(item)}
                  {actions && <OverflowMenu items={actions(item)} />}
                </div>
              </div>
              {primary && (
                <div className="mt-2.5 grid grid-cols-2 gap-x-3 gap-y-2">
                  {primary(item).map(f => (
                    <div key={f.label}>
                      <div className="text-[11px] text-gray-400 uppercase tracking-wide">{f.label}</div>
                      <div className="text-sm text-gray-800 truncate">{f.value}</div>
                    </div>
                  ))}
                </div>
              )}
            </div>
            {details && (
              <>
                <button onClick={() => setExpanded(open ? null : id)}
                  className="w-full flex items-center justify-center gap-1.5 mt-2.5 py-2.5 text-xs font-medium text-blue-600 hover:bg-blue-50 min-h-[44px]">
                  <ChevronDown className={`w-4 h-4 transition-transform ${open ? 'rotate-180' : ''}`} />
                  {open ? 'Less details' : 'More details'}
                </button>
                {open && <div className="px-4 pb-3 pt-1 border-t border-gray-100">{details(item)}</div>}
              </>
            )}
          </div>
        );
      })}
      {footer}
    </div>
  );
}

/** Detail-row label/value pair used inside expanded card sections. */
export function DetailField({ label, value }: { label: string; value?: ReactNode }) {
  return (
    <div>
      <div className="text-[11px] text-gray-400 uppercase tracking-wide">{label}</div>
      <div className="text-sm text-gray-800">{value || '—'}</div>
    </div>
  );
}

// ── FilterTabs — single horizontally-scrolling chip row (no wrapping) ───────
export function FilterTabs({ children, className = '' }: { children: ReactNode; className?: string }) {
  return (
    <div className={`flex gap-2 overflow-x-auto pb-1 -mx-1 px-1 sm:flex-wrap sm:overflow-visible sm:pb-0 ${className}`}>
      {children}
    </div>
  );
}

/** Compact prev/next pager for the mobile card view of a paged table. */
export function Pager({ page, totalPages, hasPrev, hasNext, onChange, label }: {
  page: number; totalPages: number; hasPrev: boolean; hasNext: boolean; onChange: (p: number) => void; label: string;
}) {
  return (
    <div className="flex items-center justify-between px-1 pt-1">
      <span className="text-xs text-gray-500">{label}</span>
      <div className="flex items-center gap-1">
        <button disabled={!hasPrev} onClick={() => onChange(page - 1)} aria-label="Previous page"
          className="p-2.5 rounded-lg border hover:bg-gray-50 disabled:opacity-40 min-w-[44px] min-h-[44px] flex items-center justify-center">
          <ChevronLeft className="w-4 h-4" />
        </button>
        <span className="text-xs text-gray-600 px-1">Page {page} of {totalPages}</span>
        <button disabled={!hasNext} onClick={() => onChange(page + 1)} aria-label="Next page"
          className="p-2.5 rounded-lg border hover:bg-gray-50 disabled:opacity-40 min-w-[44px] min-h-[44px] flex items-center justify-center">
          <ChevronRight className="w-4 h-4" />
        </button>
      </div>
    </div>
  );
}

/** Chip button for use inside FilterTabs (nowrap so chips stay single-line). */
export function FilterChip({ active, color, onClick, children }: { active: boolean; color?: string; onClick: () => void; children: ReactNode }) {
  return (
    <button onClick={onClick}
      className={`px-3 py-1.5 rounded-lg text-xs font-medium whitespace-nowrap transition-colors shrink-0 min-h-[36px] ${active ? (color ?? 'bg-blue-100 text-blue-700') : 'bg-gray-100 text-gray-600 hover:bg-gray-200'}`}>
      {children}
    </button>
  );
}
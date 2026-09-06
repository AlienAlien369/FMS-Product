// Single source of truth for severity rendering — the bell dropdown, the
// notifications page, and the alert/event catalogs all render through this
// chip so the labels and palettes can never drift apart again.
// Severity enum (backend): 0 = Info, 1 = Low, 2 = Medium, 3 = High, 4 = Critical.

export const SEVERITY_LABELS: Record<number, string> = { 0: 'Info', 1: 'Low', 2: 'Medium', 3: 'High', 4: 'Critical' };
export const SEVERITY_COLORS: Record<number, string> = {
  0: 'bg-gray-100 text-gray-600', 1: 'bg-blue-100 text-blue-700',
  2: 'bg-amber-100 text-amber-700', 3: 'bg-orange-100 text-orange-700', 4: 'bg-red-100 text-red-700',
};

interface SeverityChipProps {
  severity: number;
  /** 'sm' = bell/notification rows (compact), 'md' = catalog tables. */
  size?: 'sm' | 'md';
  /** Extra layout classes from the caller (e.g. mt-0.5 flex-shrink-0). */
  className?: string;
}

export default function SeverityChip({ severity, size = 'sm', className = '' }: SeverityChipProps) {
  const sizing = size === 'md' ? 'px-2.5 text-xs font-medium' : 'px-2 text-[10px] font-semibold';
  return (
    <span className={`inline-flex items-center py-0.5 rounded-full flex-shrink-0 ${sizing} ${SEVERITY_COLORS[severity] ?? 'bg-gray-100 text-gray-600'} ${className}`}>
      {SEVERITY_LABELS[severity] ?? '—'}
    </span>
  );
}
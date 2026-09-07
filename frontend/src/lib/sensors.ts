/** Canonical tyre positions in diagram order (2×2 + spare). */
export const TYRE_POSITIONS = [
  { position: 0, key: 'front_left', label: 'Front Left' },
  { position: 1, key: 'front_right', label: 'Front Right' },
  { position: 2, key: 'rear_left', label: 'Rear Left' },
  { position: 3, key: 'rear_right', label: 'Rear Right' },
  { position: 4, key: 'spare', label: 'Spare' },
] as const;

/** Status → tailwind classes for the tyre diagram (green/amber/red/gray). */
export const TYRE_STATUS_STYLE: Record<string, { ring: string; bg: string; text: string; label: string }> = {
  ok:           { ring: 'border-green-500',      bg: 'bg-green-100',      text: 'text-green-700',      label: 'OK' },
  warning:      { ring: 'border-amber-500',      bg: 'bg-amber-100',      text: 'text-amber-700',      label: 'Warning' },
  critical:     { ring: 'border-red-500',        bg: 'bg-red-100',        text: 'text-red-700',        label: 'Critical' },
  notSupported: { ring: 'border-gray-300',       bg: 'bg-gray-100',       text: 'text-gray-400',       label: 'Not supported' },
};

export const SPEED_STATUS_STYLE: Record<string, { chip: string; label: string }> = {
  ok:       { chip: 'bg-green-100 text-green-700',   label: 'Within policy' },
  over:     { chip: 'bg-red-100 text-red-700',       label: 'Over policy limit' },
  noPolicy: { chip: 'bg-gray-100 text-gray-600',     label: 'No speed policy configured' },
  noData:   { chip: 'bg-gray-100 text-gray-500',     label: 'No speed data yet' },
};
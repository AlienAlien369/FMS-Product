import { AlertTriangle, Camera, CheckCircle2, Clock, PenLine, ShieldCheck } from 'lucide-react';
import { podPhotoSrc } from '../lib/api';

/**
 * One proof-of-delivery record as the API projects it.
 * This component is deliberately auth-free and data-driven so the SAME viewer
 * serves the internal admin trip detail and the future external customer
 * tracking link — it renders whatever records it is given.
 */
export interface PodRecord {
  id: string;
  tripId: string;
  waypointId: string;
  waypointName: string;
  type: number;            // 0=signature 1=photo 2=otp_code
  typeName: string;
  signatureSvg?: string | null;
  imageUrl?: string | null;
  otpVerified: boolean;
  otpVerifiedAt?: string | null;
  verified: boolean;
  capturedBy: string;
  capturedAt: string;
  latitude?: number | null;
  longitude?: number | null;
  locationMismatch?: boolean | null;
  locationMismatchDetail?: string | null;
  notes?: string | null;
}

export const POD_TYPE_LABELS = ['Signature', 'Photo', 'OTP Code'];

/**
 * Render-side SVG sanitizer (defense in depth — capture is sanitized server-
 * side too, but stored data may predate that). Allowlist: svg root + drawing
 * primitives only, and only the geometry/stroke/fill attributes the signature
 * pad emits. Everything else — script, foreignObject, on* handlers, href — is
 * stripped so stored markup can never reach the DOM as active content.
 */
export function sanitizeSignatureSvg(svg: string): string {
  const allowedAttrs = new Set([
    'd', 'stroke', 'stroke-width', 'stroke-linecap', 'stroke-linejoin', 'stroke-miterlimit',
    'fill', 'fill-rule', 'fill-opacity', 'stroke-opacity', 'viewbox', 'xmlns', 'width', 'height', 'x', 'y',
  ]);
  const allowedTags = new Set(['svg', 'path', 'g', 'line', 'polyline', 'circle', 'rect']);

  return svg.replace(/<[^>]*>/g, raw => {
    const inner = raw.slice(1, -1).trim();
    if (!inner) return '';
    const closing = inner.startsWith('/');
    const selfClosing = !closing && inner.endsWith('/');
    const trimmed = selfClosing ? inner.slice(0, -1).trimEnd() : inner;
    const match = /^\/?(\S+)/.exec(trimmed);
    const name = match?.[1] ?? '';
    if (!allowedTags.has(name.toLowerCase())) return ''; // drop disallowed elements

    if (closing) return `</${name}>`;

    const attrText = trimmed.slice(name.length).trim();
    const parts: string[] = [];
    for (const attr of attrText.matchAll(/([\w:-]+)\s*=\s*("[^"]*"|'[^']*'|[^\s>]+)/g)) {
      const aName = attr[1];
      if (aName.toLowerCase().startsWith('on')) continue;         // on* handlers
      if (!allowedAttrs.has(aName.toLowerCase())) continue;       // href, style, class, id, …
      parts.push(`${aName}=${attr[2]}`);
    }
    return parts.length > 0 ? `<${name} ${parts.join(' ')}${selfClosing ? '/>' : '>'}` : `<${name}${selfClosing ? '/>' : '>'}`;
  });
}

/** Reusable evidence panel: renders POD records for one waypoint (or a trip). */
export default function PodEvidence({ records, compact = false }: { records: PodRecord[]; compact?: boolean }) {
  if (records.length === 0) {
    return <div className="text-xs text-gray-400 py-2">No proof of delivery captured yet.</div>;
  }

  return (
    <div className="space-y-2">
      {records.map(p => (
        <div key={p.id} className={`bg-white rounded-lg border p-3 ${p.locationMismatch ? 'border-amber-300' : 'border-gray-200'}`}>
          <div className="flex items-center justify-between gap-2 flex-wrap">
            <div className="flex items-center gap-2">
              {p.type === 0 ? <PenLine className="w-4 h-4 text-blue-600" />
                : p.type === 1 ? <Camera className="w-4 h-4 text-purple-600" />
                : <ShieldCheck className="w-4 h-4 text-green-600" />}
              <span className="text-sm font-medium text-gray-900">{POD_TYPE_LABELS[p.type] ?? p.typeName}</span>
              {p.verified ? (
                <span className="inline-flex items-center gap-1 px-1.5 py-0.5 rounded-full text-[10px] font-medium bg-green-100 text-green-700">
                  <CheckCircle2 className="w-3 h-3" /> Verified
                </span>
              ) : (
                <span className="inline-flex items-center gap-1 px-1.5 py-0.5 rounded-full text-[10px] font-medium bg-gray-100 text-gray-600">
                  <Clock className="w-3 h-3" /> Pending
                </span>
              )}
            </div>
            <span className="text-[10px] text-gray-400">{new Date(p.capturedAt).toLocaleString()}</span>
          </div>

          {/* Evidence body per type */}
          {p.type === 0 && p.signatureSvg && (
            <div className="mt-2 bg-gray-50 border border-gray-100 rounded-lg p-2 flex items-center justify-center">
              {/* Stored SVG is sanitized on render (allowlist) — stored markup
                  must never reach the DOM as active content. */}
              <div dangerouslySetInnerHTML={{ __html: sanitizeSignatureSvg(p.signatureSvg) }} className="max-h-28" />
            </div>
          )}
          {p.type === 1 && p.imageUrl && (
            <div className="mt-2 flex items-center justify-center">
              {/* Legacy records may hold a base64 data URL; new captures hold an
                  API-relative stored-file reference. podPhotoSrc renders both. */}
              <img src={podPhotoSrc(p.imageUrl)} alt="Delivery photo proof" className="max-h-40 rounded-lg border border-gray-200" />
            </div>
          )}
          {p.type === 2 && (
            <div className="mt-2 text-xs text-gray-600">
              {p.otpVerified
                ? <>Customer code verified {p.otpVerifiedAt ? `at ${new Date(p.otpVerifiedAt).toLocaleString()}` : ''}.</>
                : 'OTP issued — awaiting customer verification.'}
            </div>
          )}

          {!compact && (
            <div className="mt-2 flex flex-wrap gap-x-4 gap-y-0.5 text-[10px] text-gray-500">
              <span>by {p.capturedBy}</span>
              {p.latitude != null && p.longitude != null && (
                <span className="font-mono">{p.latitude.toFixed(5)}, {p.longitude.toFixed(5)}</span>
              )}
              {p.notes && <span>· {p.notes}</span>}
            </div>
          )}

          {/* Data-quality / fraud flag — informational, never blocking */}
          {p.locationMismatch && (
            <div className="mt-2 flex items-start gap-1.5 bg-amber-50 border border-amber-200 rounded-lg px-2.5 py-1.5 text-[11px] text-amber-800">
              <AlertTriangle className="w-3.5 h-3.5 shrink-0 mt-0.5" />
              <span>
                <strong>Location mismatch</strong> — {p.locationMismatchDetail ?? 'capture location does not match the expected delivery geofence.'}
              </span>
            </div>
          )}
        </div>
      ))}
    </div>
  );
}
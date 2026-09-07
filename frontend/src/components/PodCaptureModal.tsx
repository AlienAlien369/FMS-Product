import { useEffect, useRef, useState } from 'react';
import api from '../lib/api';
import { Camera, CheckCircle2, KeyRound, Loader2, PenLine, X } from 'lucide-react';
import type { PodRecord } from './PodEvidence';

/**
 * Proof-of-delivery capture modal (signature / photo / OTP).
 *
 * Signature → pointer strokes serialized to an SVG path (stored as SVG,
 * rendered inline by PodEvidence). Photo → file picked locally, uploaded as an
 * image reference (data URL in dev; blob-storage URL in production). OTP →
 * issue a code against the waypoint's customer contact, then verify it.
 *
 * Geolocation is attached when the browser grants it — the backend cross-checks
 * it against the waypoint's delivery geofence and flags mismatches as a
 * data-quality signal (never a hard block).
 */
export default function PodCaptureModal({ tripId, waypointId, waypointName, onClose, onCaptured }:
  { tripId: string; waypointId: string; waypointName: string; onClose: () => void; onCaptured: (r: PodRecord) => void }) {

  const [mode, setMode] = useState<0 | 1 | 2>(0); // 0=signature 1=photo 2=otp
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const [lat, setLat] = useState<number | null>(null);
  const [lng, setLng] = useState<number | null>(null);

  // ── Signature pad (stroke → SVG) ───────────────────────
  const [strokes, setStrokes] = useState<number[][][]>([]);
  const [current, setCurrent] = useState<number[][] | null>(null);
  const padRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    // Optional capture geolocation — the mismatch flag is a quality signal.
    if ('geolocation' in navigator) {
      navigator.geolocation.getCurrentPosition(
        p => { setLat(p.coords.latitude); setLng(p.coords.longitude); },
        () => { /* denied — capture without coordinates */ },
        { enableHighAccuracy: true, timeout: 5000 }
      );
    }
  }, []);

  const pt = (e: React.PointerEvent) => {
    const rect = padRef.current!.getBoundingClientRect();
    return [e.clientX - rect.left, e.clientY - rect.top];
  };
  const onDown = (e: React.PointerEvent) => {
    e.preventDefault();
    padRef.current?.setPointerCapture(e.pointerId);
    setCurrent([pt(e)]);
  };
  const onMove = (e: React.PointerEvent) => {
    if (!current) return;
    setCurrent([...current, pt(e)]);
  };
  const onUp = () => {
    if (current && current.length > 0) setStrokes(s => [...s, current]);
    setCurrent(null);
  };

  const svgPath = (pts: number[][]) =>
    pts.map((p, i) => `${i === 0 ? 'M' : 'L'} ${p[0].toFixed(1)} ${p[1].toFixed(1)}`).join(' ');
  const signatureSvg = () => {
    const W = 600, H = 240;
    const all = [...strokes, ...(current ? [current] : [])].filter(s => s.length > 1);
    return `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 ${W} ${H}" width="${W}" height="${H}">`
      + all.map(s => `<path d="${svgPath(s)}" stroke="#1f2937" stroke-width="2.5" stroke-linecap="round" fill="none"/>`).join('')
      + `</svg>`;
  };

  // ── Photo capture ──────────────────────────────────────
  const [photoDataUrl, setPhotoDataUrl] = useState('');
  const [photoName, setPhotoName] = useState('');
  const onFile = (f: File | undefined) => {
    if (!f) return;
    setError('');
    const reader = new FileReader();
    reader.onload = () => { setPhotoDataUrl(String(reader.result)); setPhotoName(f.name); };
    reader.readAsDataURL(f);
  };

  // ── OTP flow ───────────────────────────────────────────
  const [otpCode, setOtpCode] = useState('');
  const [issuedOtp, setIssuedOtp] = useState<{ id: string; code: string; channel: string | null } | null>(null);
  const [verifyCode, setVerifyCode] = useState('');

  const issueOtp = async () => {
    setBusy(true); setError('');
    try {
      const r = await api.post(`/trips/${tripId}/waypoints/${waypointId}/pod/otp`, {});
      setIssuedOtp({ id: r.data.data.record.id, code: r.data.data.otp, channel: r.data.data.customerChannel });
    } catch (err: any) { setError(err.response?.data?.message ?? 'Failed to issue OTP'); }
    setBusy(false);
  };

  const submit = async (type: 0 | 1 | 2) => {
    setBusy(true); setError('');
    const geo = lat != null && lng != null ? { latitude: lat, longitude: lng } : {};
    try {
      if (type === 0) {
        const svg = signatureSvg();
        if (!svg.includes('<path')) { setError('Draw a signature first.'); setBusy(false); return; }
        const r = await api.post(`/trips/${tripId}/waypoints/${waypointId}/pod/signature`, { signatureSvg: svg, ...geo });
        onCaptured(r.data.data);
      } else if (type === 1) {
        if (!photoDataUrl) { setError('Take or choose a photo first.'); setBusy(false); return; }
        const r = await api.post(`/trips/${tripId}/waypoints/${waypointId}/pod/photo`, { imageUrl: photoDataUrl, ...geo });
        onCaptured(r.data.data);
      } else {
        if (!issuedOtp) { setError('Issue an OTP to the customer first.'); setBusy(false); return; }
        if (!verifyCode.trim()) { setError('Enter the code the customer received.'); setBusy(false); return; }
        const r = await api.post(`/trips/${tripId}/waypoints/${waypointId}/pod/otp/verify`, { code: verifyCode.trim(), ...geo });
        onCaptured(r.data.data);
      }
    } catch (err: any) { setError(err.response?.data?.message ?? 'Capture failed'); }
    setBusy(false);
  };

  const tabs = [
    { v: 0 as const, label: 'Signature', icon: PenLine },
    { v: 1 as const, label: 'Photo', icon: Camera },
    { v: 2 as const, label: 'OTP Code', icon: KeyRound },
  ];

  return (
    <div className="fixed inset-0 bg-black/50 flex items-center justify-center z-[60]">
      <div className="bg-white rounded-xl w-full max-w-lg max-h-[90vh] overflow-y-auto">
        <div className="flex items-center justify-between p-5 border-b">
          <div>
            <h3 className="text-base font-semibold">Proof of Delivery — {waypointName}</h3>
            <p className="text-[11px] text-gray-400 mt-0.5">
              Captured at the point of delivery{lat != null && lng != null ? ` · ${lat.toFixed(4)}, ${lng.toFixed(4)}` : ' · location unavailable (no mismatch flag)'}
            </p>
          </div>
          <button onClick={onClose} className="p-2 rounded-lg hover:bg-gray-100"><X className="w-4 h-4" /></button>
        </div>

        <div className="p-5 space-y-4">
          {/* Mode tabs */}
          <div className="flex gap-1.5 bg-gray-100 rounded-lg p-1">
            {tabs.map(t => (
              <button key={t.v} onClick={() => { setMode(t.v); setError(''); }}
                className={`flex-1 flex items-center justify-center gap-1.5 px-3 py-1.5 rounded-md text-xs font-medium transition-colors ${mode === t.v ? 'bg-white text-blue-700 shadow-sm' : 'text-gray-500 hover:text-gray-700'}`}>
                <t.icon className="w-3.5 h-3.5" /> {t.label}
              </button>
            ))}
          </div>

          {/* Signature */}
          {mode === 0 && (
            <div>
              <div ref={padRef}
                onPointerDown={onDown} onPointerMove={onMove} onPointerUp={onUp} onPointerLeave={onUp}
                className="relative w-full h-56 bg-gray-50 border-2 border-dashed border-gray-300 rounded-lg touch-none select-none cursor-crosshair">
                <svg className="absolute inset-0 w-full h-full" viewBox="0 0 600 240" preserveAspectRatio="none">
                  {[...strokes, ...(current ? [current] : [])].filter(s => s.length > 1).map((s, i) => (
                    <path key={i} d={svgPath(s)} stroke="#1f2937" strokeWidth={3} strokeLinecap="round" fill="none" />
                  ))}
                </svg>
                {strokes.length === 0 && !current && (
                  <span className="absolute inset-0 flex items-center justify-center text-xs text-gray-400 pointer-events-none">Draw signature here</span>
                )}
              </div>
              <div className="flex justify-between mt-2">
                <button onClick={() => { setStrokes([]); setCurrent(null); }} className="text-xs text-gray-500 hover:text-gray-700">Clear</button>
                <span className="text-[10px] text-gray-400">Stored as SVG — rendered inline on the trip detail & customer link</span>
              </div>
            </div>
          )}

          {/* Photo */}
          {mode === 1 && (
            <div>
              <label className="flex flex-col items-center justify-center w-full h-56 bg-gray-50 border-2 border-dashed border-gray-300 rounded-lg cursor-pointer hover:bg-gray-100 transition-colors overflow-hidden">
                {photoDataUrl
                  ? <img src={photoDataUrl} alt="Delivery photo" className="w-full h-full object-cover" />
                  : <span className="text-xs text-gray-400 flex flex-col items-center gap-1.5">
                      <Camera className="w-8 h-8 text-gray-300" />
                      Tap to take / choose a delivery photo
                    </span>}
                <input type="file" accept="image/*" capture="environment" className="hidden" onChange={e => onFile(e.target.files?.[0])} />
              </label>
              {photoName && <p className="text-[10px] text-gray-400 mt-1">{photoName}</p>}
            </div>
          )}

          {/* OTP */}
          {mode === 2 && (
            <div className="space-y-3">
              {!issuedOtp ? (
                <div>
                  <p className="text-xs text-gray-600 mb-2">
                    Issue a one-time code to the waypoint's customer contact — they read it back at the door, and you verify it here.
                  </p>
                  <button onClick={issueOtp} disabled={busy}
                    className="w-full flex items-center justify-center gap-1.5 px-3 py-2 rounded-lg bg-blue-600 text-white text-sm font-medium hover:bg-blue-700 disabled:opacity-50">
                    {busy ? <Loader2 className="w-4 h-4 animate-spin" /> : <KeyRound className="w-4 h-4" />} Issue OTP
                  </button>
                </div>
              ) : (
                <div className="space-y-3">
                  <div className="bg-blue-50 border border-blue-200 rounded-lg p-3 text-center">
                    <p className="text-[10px] text-blue-600 uppercase tracking-wide font-medium">Customer code{issuedOtp.channel ? ` · sent to ${issuedOtp.channel}` : ''}</p>
                    <p className="text-2xl font-bold tracking-[0.3em] text-blue-900 font-mono mt-1">{issuedOtp.code}</p>
                    <p className="text-[10px] text-blue-500 mt-1">Dev-mode relay — production sends via the SMS/email gateway. Only the SHA-256 hash is stored.</p>
                  </div>
                  <div className="flex gap-2">
                    <input value={verifyCode} onChange={e => setVerifyCode(e.target.value)}
                      placeholder="Enter customer code" className="flex-1 px-3 py-2 border border-gray-300 rounded-lg text-sm focus:ring-2 focus:ring-blue-500" />
                    <button onClick={() => submit(2)} disabled={busy || !verifyCode.trim()}
                      className="px-4 py-2 rounded-lg bg-green-600 text-white text-sm font-medium hover:bg-green-700 disabled:opacity-50 flex items-center gap-1.5">
                      {busy ? <Loader2 className="w-4 h-4 animate-spin" /> : <CheckCircle2 className="w-4 h-4" />} Verify
                    </button>
                  </div>
                </div>
              )}
            </div>
          )}

          {error && <p className="text-xs text-red-600 bg-red-50 border border-red-100 rounded-lg px-3 py-2">{error}</p>}

          {/* Submit for signature/photo; OTP verifies inline */}
          {mode !== 2 && (
            <div className="flex justify-end gap-2">
              <button onClick={onClose} className="px-4 py-2 rounded-lg border border-gray-300 text-sm text-gray-600 hover:bg-gray-50">Cancel</button>
              <button onClick={() => submit(mode)} disabled={busy}
                className="px-4 py-2 rounded-lg bg-blue-600 text-white text-sm font-medium hover:bg-blue-700 disabled:opacity-50 flex items-center gap-1.5">
                {busy ? <Loader2 className="w-4 h-4 animate-spin" /> : <CheckCircle2 className="w-4 h-4" />} Capture {mode === 0 ? 'Signature' : 'Photo'}
              </button>
            </div>
          )}
          {mode === 2 && issuedOtp && (
            <p className="text-right text-[10px] text-gray-400">Verifying records the capture location & flags geofence mismatches.</p>
          )}
        </div>
      </div>
    </div>
  );
}
import { useRef, useState } from 'react';
import { Upload, FileText, CheckCircle2, XCircle, AlertTriangle, Loader2 } from 'lucide-react';
import api from '../lib/api';

interface Props {
  /** For cross-tenant Super Admins the target company chosen in the modal — required by the backend. */
  companyId?: string | null;
  isCrossTenant: boolean;
  onImported: () => void;
}

interface ImportResult {
  imported: number;
  failed: number;
  errors?: { row?: string | number; error?: string }[];
  total: number;
}

function detectFormat(content: string): 'geojson' | 'csv' {
  const t = content.trimStart();
  if (t.startsWith('{') || t.startsWith('[')) return 'geojson';
  return 'csv';
}

const CSV_HINT = 'name,latitude,longitude,radiusMeters[,description]';
const GEOJSON_HINT = 'FeatureCollection of Polygon features (or Point features with properties.radiusMeters)';

export default function GeofenceImportTab({ companyId, isCrossTenant, onImported }: Props) {
  const [content, setContent] = useState('');
  const [fileName, setFileName] = useState('');
  const [format, setFormat] = useState<'csv' | 'geojson'>('csv');
  const [busy, setBusy] = useState(false);
  const [result, setResult] = useState<ImportResult | null>(null);
  const [error, setError] = useState('');
  const fileRef = useRef<HTMLInputElement>(null);

  const loadFile = (file: File) => {
    const reader = new FileReader();
    reader.onload = () => {
      const text = String(reader.result ?? '');
      setContent(text);
      setFileName(file.name);
      setFormat(detectFormat(text));
      setResult(null);
      setError('');
    };
    reader.readAsText(file);
  };

  const runImport = async () => {
    if (!content.trim()) { setError('Paste content or choose a file first.'); return; }
    if (isCrossTenant && !companyId) { setError('Select the target company above before importing.'); return; }
    setBusy(true); setError(''); setResult(null);
    try {
      const res = await api.post('/geofences/import', {
        format,
        content,
        ...(isCrossTenant ? { companyId } : {}),
      });
      setResult(res.data.data ?? { imported: 0, failed: 0, total: 0 });
    } catch (err: any) {
      setError(err.response?.data?.message ?? 'Import failed. Check the content and try again.');
    }
    setBusy(false);
  };

  const previewLines = content.split('\n').slice(0, 6);

  return (
    <div className="space-y-3">
      <p className="text-xs text-gray-500">
        Upload radius circles as CSV <code className="bg-gray-100 px-1 rounded">name,lat,lng,radiusMeters</code> — or a{' '}
        <strong>GeoJSON FeatureCollection</strong> of <strong>Polygon</strong> features (circles as <strong>Point</strong>{' '}
        features with <code className="bg-gray-100 px-1 rounded">properties.radiusMeters</code>). Every row is validated
        individually; invalid rows are reported and never fail the batch.
      </p>

      <div className="flex items-center gap-2">
        <button type="button" onClick={() => fileRef.current?.click()}
          className="flex items-center gap-2 border border-gray-300 rounded-lg px-3 py-2 text-sm text-gray-700 hover:bg-gray-50">
          <Upload className="w-4 h-4" /> {fileName || 'Choose .csv / .geojson file'}
        </button>
        <input ref={fileRef} type="file" accept=".csv,.geojson,.json,.txt,text/csv,application/json" className="hidden"
          onChange={e => { const f = e.target.files?.[0]; if (f) loadFile(f); e.target.value = ''; }} />
        <select value={format} onChange={e => setFormat(e.target.value as 'csv' | 'geojson')} className="border border-gray-300 rounded-lg px-2 py-2 text-sm">
          <option value="csv">CSV</option>
          <option value="geojson">GeoJSON</option>
        </select>
      </div>

      <div>
        <label className="block text-xs font-medium text-gray-500 mb-1">Content</label>
        <textarea className="w-full px-3 py-2 border border-gray-300 rounded-lg text-xs font-mono focus:ring-2 focus:ring-blue-500"
          rows={6} placeholder={format === 'csv' ? CSV_HINT : GEOJSON_HINT}
          value={content} onChange={e => { setContent(e.target.value); setFormat(detectFormat(e.target.value)); setResult(null); }} />
        {content && (
          <p className="text-[11px] text-gray-400 mt-1">
            Detected format: <strong className="text-gray-600">{format.toUpperCase()}</strong>
            {previewLines.length > 6 && ` · showing first ${previewLines.length} of ${content.split('\n').length} lines`}
          </p>
        )}
      </div>

      {error && (
        <div className="flex items-start gap-2 bg-red-50 border border-red-200 text-red-700 px-3 py-2 rounded-lg text-xs">
          <AlertTriangle className="w-4 h-4 mt-0.5 shrink-0" /> <span>{error}</span>
        </div>
      )}

      {result && (
        <div className={`rounded-lg border px-3 py-2 text-xs ${result.failed > 0 ? 'border-amber-200 bg-amber-50' : 'border-green-200 bg-green-50'}`}>
          <div className="flex items-center gap-2 font-medium text-gray-800">
            {result.failed > 0 ? <AlertTriangle className="w-4 h-4 text-amber-500" /> : <CheckCircle2 className="w-4 h-4 text-green-600" />}
            Imported {result.imported} of {result.total} geofence{result.total === 1 ? '' : 's'}
            {result.failed > 0 && ` — ${result.failed} rejected`}
          </div>
          {result.failed > 0 && (
            <ul className="mt-2 space-y-1 max-h-32 overflow-y-auto">
              {(result.errors ?? []).map((e, i) => (
                <li key={i} className="flex items-start gap-1.5 text-gray-600">
                  <XCircle className="w-3.5 h-3.5 text-red-500 mt-0.5 shrink-0" />
                  <span><strong>{e.row ?? '?'}</strong>: {e.error}</span>
                </li>
              ))}
            </ul>
          )}
          {result.imported > 0 && (
            <button type="button" onClick={onImported} className="mt-2 text-blue-600 hover:underline font-medium">
              Refresh list to see imported geofences →
            </button>
          )}
        </div>
      )}

      <div className="flex justify-end gap-3 pt-1">
        {content && (
          <button type="button" onClick={() => { setContent(''); setFileName(''); setResult(null); setError(''); }}
            className="px-3 py-2 text-xs border border-gray-300 rounded-lg text-gray-600 hover:bg-gray-50">
            Clear
          </button>
        )}
        <button type="button" onClick={runImport} disabled={busy || !content.trim()}
          className="flex items-center gap-1.5 px-4 py-2 text-xs bg-blue-600 text-white rounded-lg hover:bg-blue-700 disabled:opacity-50">
          {busy ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> : <FileText className="w-3.5 h-3.5" />}
          {busy ? 'Importing…' : 'Import Geofences'}
        </button>
      </div>
    </div>
  );
}

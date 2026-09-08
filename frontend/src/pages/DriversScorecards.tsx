import { useState, useEffect, useCallback } from 'react';
import { Award, TrendingUp, Settings2, Shield, Clock, CheckCircle2, Gauge } from 'lucide-react';
import api from '../lib/api';
import { usePermissions } from '../hooks/usePermissions';

interface RankingRow {
  driverId: string;
  name: string;
  composite: number | null;
  safety: number | null;
  compliance: number | null;
  punctuality: number | null;
  behavior: number | null;
  insufficientData: boolean;
  tripCount: number;
}

interface WeightConfig {
  safety: number;
  compliance: number;
  punctuality: number;
  behavior: number;
  source: 'company' | 'platform';
}

const WINDOWS = [
  { key: '30d', label: 'Last 30 days' },
  { key: '90d', label: 'Last 90 days' },
  { key: 'all', label: 'All time' },
];

function ScoreBar({ value }: { value: number | null }) {
  if (value == null) return <span className="text-xs text-gray-400">Insufficient data</span>;
  const color = value >= 80 ? 'bg-green-500' : value >= 60 ? 'bg-yellow-500' : 'bg-red-500';
  return (
    <div className="flex items-center gap-2">
      <div className="w-20 h-2 bg-gray-200 rounded-full overflow-hidden">
        <div className={`h-full rounded-full ${color}`} style={{ width: `${value}%` }} />
      </div>
      <span className="text-xs font-medium text-gray-700">{value}</span>
    </div>
  );
}

function CategoryCell({ value, label }: { value: number | null; label: string }) {
  return (
    <div className="flex flex-col">
      <span className="text-[10px] uppercase tracking-wide text-gray-400">{label}</span>
      <span className={`text-sm font-medium ${value == null ? 'text-gray-400' : 'text-gray-800'}`}>
        {value == null ? '—' : value}
      </span>
    </div>
  );
}

export default function DriversScorecards() {
  const { can } = usePermissions();
  const canConfigure = can('driverscore.configure');
  const [window, setWindow] = useState('30d');
  const [rows, setRows] = useState<RankingRow[]>([]);
  const [loading, setLoading] = useState(true);
  const [weights, setWeights] = useState<WeightConfig | null>(null);
  const [editOpen, setEditOpen] = useState(false);
  const [draft, setDraft] = useState({ safety: 0.4, compliance: 0.25, punctuality: 0.2, behavior: 0.15 });
  const [saving, setSaving] = useState(false);
  const [notice, setNotice] = useState('');

  const fetchRanking = useCallback(async () => {
    setLoading(true);
    try {
      const res = await api.get(`/drivers/scorecards?window=${window}&limit=100`);
      setRows(res.data.data || []);
    } catch { setRows([]); }
    setLoading(false);
  }, [window]);

  const fetchWeights = useCallback(async () => {
    try {
      const res = await api.get('/drivers/scorecard-config');
      setWeights(res.data.data);
      setDraft({
        safety: res.data.data.safety,
        compliance: res.data.data.compliance,
        punctuality: res.data.data.punctuality,
        behavior: res.data.data.behavior,
      });
    } catch { /* permission-gated */ }
  }, []);

  useEffect(() => { fetchRanking(); }, [fetchRanking]);
  useEffect(() => { fetchWeights(); }, [fetchWeights]);

  const saveWeights = async () => {
    setSaving(true);
    setNotice('');
    try {
      const res = await api.put('/drivers/scorecard-config', {
        safetyWeight: draft.safety,
        complianceWeight: draft.compliance,
        punctualityWeight: draft.punctuality,
        behaviorWeight: draft.behavior,
      });
      setWeights(res.data.data);
      setEditOpen(false);
      setNotice('Weight config saved — existing periods keep their scores until recomputed.');
    } catch (e: any) {
      setNotice(e.response?.data?.message || 'Failed to save weights');
    }
    setSaving(false);
  };

  const num = (v: string, fb: number) => { const n = parseFloat(v); return Number.isFinite(n) ? n : fb; };

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-xl font-bold text-gray-900">Driver Scorecards</h1>
          <p className="text-sm text-gray-500">Composite safety, compliance, punctuality &amp; behavior scores from raw event + trip data.</p>
        </div>
        <div className="flex items-center gap-2">
          <div className="flex rounded-lg border border-gray-200 bg-white p-0.5">
            {WINDOWS.map(w => (
              <button key={w.key} onClick={() => setWindow(w.key)}
                className={`px-3 py-1.5 text-xs font-medium rounded-md transition-colors ${window === w.key ? 'bg-blue-600 text-white' : 'text-gray-600 hover:bg-gray-50'}`}>
                {w.label}
              </button>
            ))}
          </div>
          {canConfigure && (
            <button onClick={() => setEditOpen(true)}
              className="flex items-center gap-1.5 px-3 py-2 bg-white border border-gray-200 rounded-lg text-sm text-gray-700 hover:bg-gray-50">
              <Settings2 className="w-4 h-4" /> Weights
            </button>
          )}
        </div>
      </div>

      {weights && (
        <div className="flex flex-wrap items-center gap-3 text-xs text-gray-500 bg-white border border-gray-200 rounded-lg px-4 py-2.5">
          <Award className="w-4 h-4 text-blue-500" />
          <span>Composite = {weights.safety}×Safety + {weights.compliance}×Compliance + {weights.punctuality}×Punctuality + {weights.behavior}×Behavior</span>
          <span className={`px-2 py-0.5 rounded-full ${weights.source === 'company' ? 'bg-purple-100 text-purple-700' : 'bg-gray-100 text-gray-600'}`}>
            {weights.source === 'company' ? 'Company override' : 'Platform default'}
          </span>
        </div>
      )}

      {loading ? (
        <div className="text-sm text-gray-500 py-10 text-center">Loading scorecards…</div>
      ) : rows.length === 0 ? (
        <div className="bg-white border border-gray-200 rounded-xl p-10 text-center text-sm text-gray-500">
          No scorecards yet — scores materialize from driver behavior events and completed trips.
        </div>
      ) : (
        <div className="bg-white rounded-xl border border-gray-200 overflow-hidden">
          <table className="w-full text-sm">
            <thead>
              <tr className="bg-gray-50 text-left text-xs text-gray-500 uppercase tracking-wide">
                <th className="px-4 py-3 font-medium">#</th>
                <th className="px-4 py-3 font-medium">Driver</th>
                <th className="px-4 py-3 font-medium">Composite</th>
                <th className="px-4 py-3 font-medium">Safety</th>
                <th className="px-4 py-3 font-medium">Compliance</th>
                <th className="px-4 py-3 font-medium">Punctuality</th>
                <th className="px-4 py-3 font-medium">Behavior</th>
                <th className="px-4 py-3 font-medium">Trips</th>
              </tr>
            </thead>
            <tbody>
              {rows.map((r, i) => (
                <tr key={r.driverId} className="border-t border-gray-100 hover:bg-gray-50">
                  <td className="px-4 py-3 text-gray-500">{i + 1}</td>
                  <td className="px-4 py-3 font-medium text-gray-900">{r.name}</td>
                  <td className="px-4 py-3"><ScoreBar value={r.composite} /></td>
                  <td className="px-4 py-3"><CategoryCell value={r.safety} label="Safety" /></td>
                  <td className="px-4 py-3"><CategoryCell value={r.compliance} label="Compliance" /></td>
                  <td className="px-4 py-3"><CategoryCell value={r.punctuality} label="Punctuality" /></td>
                  <td className="px-4 py-3"><CategoryCell value={r.behavior} label="Behavior" /></td>
                  <td className="px-4 py-3 text-gray-600">{r.tripCount}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {/* Weight config editor */}
      {editOpen && (
        <div className="fixed inset-0 z-50 flex items-center justify-center p-4">
          <div className="fixed inset-0 bg-black/50" onClick={() => setEditOpen(false)} />
          <div className="relative bg-white rounded-xl shadow-2xl w-full max-w-md p-6">
            <h3 className="text-lg font-semibold text-gray-900 mb-1">Score Weight Configuration</h3>
            <p className="text-xs text-gray-500 mb-4">
              Category weights for the composite score. Existing periods are not rewritten —
              recompute a driver's scorecard to apply new weights.
            </p>
            <div className="space-y-3">
              {([
                { key: 'safety', label: 'Safety', icon: Shield },
                { key: 'compliance', label: 'Compliance', icon: CheckCircle2 },
                { key: 'punctuality', label: 'Punctuality', icon: Clock },
                { key: 'behavior', label: 'Behavior', icon: Gauge },
              ] as const).map(({ key, label, icon: Icon }) => (
                <label key={key} className="flex items-center justify-between gap-3">
                  <span className="flex items-center gap-2 text-sm text-gray-700"><Icon className="w-4 h-4 text-gray-400" /> {label}</span>
                  <input type="number" min={0} max={1} step={0.05} value={draft[key]}
                    onChange={e => setDraft(d => ({ ...d, [key]: num(e.target.value, d[key]) }))}
                    className="w-24 px-3 py-2 border border-gray-300 rounded-lg text-sm focus:ring-2 focus:ring-blue-500" />
                </label>
              ))}
            </div>
            {notice && <div className="mt-3 text-xs text-blue-600">{notice}</div>}
            <div className="mt-5 flex justify-end gap-2">
              <button onClick={() => setEditOpen(false)} className="px-4 py-2 text-sm text-gray-600 hover:bg-gray-100 rounded-lg">Cancel</button>
              <button onClick={saveWeights} disabled={saving}
                className="px-4 py-2 text-sm bg-blue-600 text-white rounded-lg hover:bg-blue-700 disabled:opacity-50">
                {saving ? 'Saving…' : 'Save Weights'}
              </button>
            </div>
          </div>
        </div>
      )}

      <div className="flex items-center gap-1.5 text-xs text-gray-400">
        <TrendingUp className="w-3.5 h-3.5" />
        Scores are materialized from raw events and trips — a driver with fewer than 3 completed trips in a window shows “Insufficient data”.
      </div>
    </div>
  );
}
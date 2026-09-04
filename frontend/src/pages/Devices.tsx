import { useEffect, useState, useCallback } from 'react';
import api, { type PagedResult, type Device, type DeviceVendor, type DeviceSim, type Company } from '../lib/api';
import { usePermissions } from '../hooks/usePermissions';
import { useAuth } from '../contexts/AuthContext';
import {
  Search, Plus, Edit, Trash2, ChevronLeft, ChevronRight, ChevronUp, ChevronDown,
  X, Radio, Cpu, CreditCard,
} from 'lucide-react';

const INPUT = 'w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:ring-2 focus:ring-blue-500';
const LABEL = 'block text-sm font-medium text-gray-700 mb-1';

const DEVICE_TYPE_OPTIONS = [
  { value: 0, label: 'GPS Tracker' }, { value: 1, label: 'Dashcam' }, { value: 2, label: 'ADAS' },
  { value: 3, label: 'Fuel Sensor' }, { value: 4, label: 'Temperature Sensor' }, { value: 5, label: 'Dual Camera' },
  { value: 99, label: 'Other' },
];
const IDENTITY_TYPE_OPTIONS = [
  { value: 0, label: 'IMEI' }, { value: 1, label: 'Serial' }, { value: 2, label: 'MAC' }, { value: 3, label: 'Phone Number' },
];
const STATUS_FILTERS = [
  { key: 'all', label: 'All' },
  { key: '0', label: 'Active', value: 0 },
  { key: '4', label: 'Awaiting Vendor', value: 4 },
  { key: '1', label: 'Inactive', value: 1 },
  { key: '2', label: 'Retired', value: 2 },
];
const STATUS_BADGE: Record<number, string> = {
  0: 'bg-green-100 text-green-700', 1: 'bg-gray-100 text-gray-700', 2: 'bg-gray-200 text-gray-600',
  3: 'bg-red-100 text-red-700', 4: 'bg-amber-100 text-amber-700',
};
const STATUS_LABEL: Record<number, string> = {
  0: 'Active', 1: 'Inactive', 2: 'Retired', 3: 'Lost', 4: 'Awaiting Vendor',
};

interface SimDraft { iccid: string; phoneNumber: string; carrier: string; isPrimary: boolean; }

export default function Devices() {
  const { can } = usePermissions();
  const { user } = useAuth();
  const isSuperAdmin = user?.roles?.includes('SuperAdmin');
  const canCreate = can('device.create');
  const canEdit = can('device.update');
  const canDelete = can('device.delete');

  const [data, setData] = useState<PagedResult<Device> | null>(null);
  const [vendors, setVendors] = useState<DeviceVendor[]>([]);
  const [companies, setCompanies] = useState<Company[]>([]);
  const [search, setSearch] = useState('');
  const [page, setPage] = useState(1);
  const [loading, setLoading] = useState(true);
  const [statusFilter, setStatusFilter] = useState('all');
  const [sortDesc, setSortDesc] = useState(true);
  const [modal, setModal] = useState<{ open: boolean; edit?: Device }>({ open: false });
  const [simModal, setSimModal] = useState<Device | null>(null);
  const [deleteConfirm, setDeleteConfirm] = useState<Device | null>(null);

  const fetchData = useCallback(async () => {
    setLoading(true);
    try {
      const params = new URLSearchParams({ page: String(page), pageSize: '10', search, sortBy: 'createdAt', sortDescending: String(sortDesc) });
      if (statusFilter !== 'all') params.set('status', statusFilter);
      const res = await api.get(`/devices?${params}`);
      setData(res.data.data);
    } catch (err) { console.error(err); }
    setLoading(false);
  }, [page, search, sortDesc, statusFilter]);

  const fetchVendors = useCallback(async () => {
    try { const res = await api.get('/devices/vendors'); setVendors(res.data.data || []); } catch { /* ignore */ }
  }, []);

  const fetchCompanies = useCallback(async () => {
    try {
      const res = await api.get('/admin/companies?pageSize=100');
      setCompanies((res.data.data?.items || []).filter((c: Company) => c.status === 0));
    } catch { /* ignore */ }
  }, []);

  useEffect(() => { fetchData(); }, [fetchData]);
  useEffect(() => { fetchVendors(); }, [fetchVendors]);
  useEffect(() => { if (isSuperAdmin) fetchCompanies(); }, [isSuperAdmin, fetchCompanies]);

  const handleDelete = async (d: Device) => {
    try { await api.delete(`/devices/${d.id}`); setDeleteConfirm(null); fetchData(); }
    catch (e: any) { alert(e.response?.data?.message || 'Failed to delete device'); }
  };

  const onSaved = () => { setModal({ open: false }); setSimModal(null); fetchData(); };

  return (
    <div className="space-y-4">
      {/* Header */}
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-xl font-semibold text-gray-900">Devices</h1>
          <p className="text-sm text-gray-500 mt-0.5">Tracking devices, SIMs and vehicle assignments</p>
        </div>
        {canCreate && (
          <button onClick={() => setModal({ open: true })}
            className="flex items-center gap-2 px-4 py-2 bg-blue-600 hover:bg-blue-700 text-white rounded-lg text-sm font-medium transition-colors">
            <Plus className="w-4 h-4" /> Add Device
          </button>
        )}
      </div>

      {/* Filters */}
      <div className="flex flex-wrap items-center gap-3">
        <div className="flex flex-wrap gap-2">
          {STATUS_FILTERS.map(f => (
            <button key={f.key} onClick={() => { setStatusFilter(f.key); setPage(1); }}
              className={`px-3 py-1.5 rounded-full text-xs font-medium transition-colors ${statusFilter === f.key ? 'bg-blue-600 text-white' : 'bg-white border border-gray-200 text-gray-600 hover:bg-gray-50'}`}>
              {f.label}
            </button>
          ))}
        </div>
        <div className="flex-1 min-w-[200px] relative">
          <Search className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-gray-400" />
          <input value={search} onChange={e => { setSearch(e.target.value); setPage(1); }}
            placeholder="Search by IMEI / serial / model..." className="w-full pl-9 pr-4 py-2 border border-gray-300 rounded-lg text-sm focus:ring-2 focus:ring-blue-500" />
        </div>
      </div>

      {/* Table */}
      <div className="bg-white rounded-xl border border-gray-200 overflow-hidden">
        <div className="overflow-x-auto">
          <table className="w-full text-sm">
            <thead>
              <tr className="border-b border-gray-200 bg-gray-50 text-left text-xs uppercase tracking-wide text-gray-500">
                <th className="px-4 py-3 cursor-pointer select-none" onClick={() => setSortDesc(d => !d)}>
                  <span className="inline-flex items-center gap-1">Identity {sortDesc ? <ChevronDown className="w-3 h-3" /> : <ChevronUp className="w-3 h-3" />}</span>
                </th>
                <th className="px-4 py-3">Type</th>
                <th className="px-4 py-3">Vendor</th>
                <th className="px-4 py-3">Model</th>
                <th className="px-4 py-3">SIMs</th>
                <th className="px-4 py-3">Assigned To</th>
                <th className="px-4 py-3">Status</th>
                <th className="px-4 py-3">Last Seen</th>
                <th className="px-4 py-3 text-right">Actions</th>
              </tr>
            </thead>
            <tbody>
              {loading ? (
                <tr><td colSpan={9} className="px-4 py-10 text-center text-gray-400">Loading...</td></tr>
              ) : !data || data.items.length === 0 ? (
                <tr><td colSpan={9} className="px-4 py-10 text-center text-gray-400">No devices found</td></tr>
              ) : data.items.map(d => (
                <tr key={d.id} className="border-b border-gray-100 hover:bg-gray-50">
                  <td className="px-4 py-3 font-medium text-gray-900">
                    <span className="inline-flex items-center gap-2"><Radio className="w-4 h-4 text-blue-500" /> {d.identityValue}</span>
                    <div className="text-xs text-gray-400">{d.identityType === 0 ? 'IMEI' : d.identityType === 1 ? 'Serial' : d.identityType === 2 ? 'MAC' : 'Phone'}</div>
                  </td>
                  <td className="px-4 py-3 text-gray-700">{d.deviceTypeOverride || ['GPS Tracker', 'Dashcam', 'ADAS', 'Fuel Sensor', 'Temperature Sensor', 'Dual Camera', '', 'Other'][d.deviceType] || 'Other'}</td>
                  <td className="px-4 py-3">
                    {d.vendorCode
                      ? <span className="inline-flex px-2 py-0.5 rounded-full text-xs font-medium bg-indigo-50 text-indigo-700">{d.vendorName || d.vendorCode}</span>
                      : <span className="text-xs text-gray-400">—</span>}
                  </td>
                  <td className="px-4 py-3 text-gray-700">{d.model || '—'}</td>
                  <td className="px-4 py-3">
                    <button onClick={() => canEdit && setSimModal(d)} disabled={!canEdit} className="inline-flex items-center gap-1 text-xs text-gray-600 hover:text-blue-600 disabled:hover:text-gray-600">
                      <CreditCard className="w-3.5 h-3.5" /> {d.sims.length}
                    </button>
                  </td>
                  <td className="px-4 py-3 text-gray-700">{d.currentVehicleRegistration || <span className="text-xs text-gray-400">Unassigned</span>}</td>
                  <td className="px-4 py-3"><span className={`inline-flex px-2 py-0.5 rounded-full text-xs font-medium ${STATUS_BADGE[d.status] || 'bg-gray-100 text-gray-700'}`}>{STATUS_LABEL[d.status] || d.status}</span></td>
                  <td className="px-4 py-3 text-gray-500">{d.lastSeenAt ? new Date(d.lastSeenAt).toLocaleString() : '—'}</td>
                  <td className="px-4 py-3">
                    <div className="flex items-center justify-end gap-1">
                      {canEdit && <button onClick={() => setModal({ open: true, edit: d })} className="p-1.5 hover:bg-gray-100 rounded-lg"><Edit className="w-4 h-4 text-gray-500" /></button>}
                      {canDelete && <button onClick={() => setDeleteConfirm(d)} className="p-1.5 hover:bg-gray-100 rounded-lg"><Trash2 className="w-4 h-4 text-red-500" /></button>}
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>

        {/* Pagination */}
        {data && data.totalPages > 1 && (
          <div className="flex items-center justify-between px-4 py-3 border-t border-gray-100">
            <span className="text-xs text-gray-500">Showing {data.items.length} of {data.totalCount}</span>
            <div className="flex items-center gap-2">
              <button disabled={page <= 1} onClick={() => setPage(p => p - 1)} className="p-1.5 border border-gray-200 rounded-lg disabled:opacity-40"><ChevronLeft className="w-4 h-4" /></button>
              <span className="text-sm text-gray-600">Page {data.page} / {data.totalPages}</span>
              <button disabled={page >= data.totalPages} onClick={() => setPage(p => p + 1)} className="p-1.5 border border-gray-200 rounded-lg disabled:opacity-40"><ChevronRight className="w-4 h-4" /></button>
            </div>
          </div>
        )}
      </div>

      {/* Create / Edit modal */}
      {modal.open && <DeviceModal
        edit={modal.edit} vendors={vendors} companies={companies} isSuperAdmin={isSuperAdmin}
        onClose={() => setModal({ open: false })} onSaved={onSaved} />}

      {/* SIMs modal */}
      {simModal && <SimsModal device={simModal} onClose={() => setSimModal(null)} onSaved={onSaved} />}

      {/* Delete confirm */}
      {deleteConfirm && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 p-4">
          <div className="bg-white rounded-xl shadow-xl w-full max-w-sm p-6">
            <h3 className="text-lg font-semibold text-gray-900 mb-2">Delete device</h3>
            <p className="text-sm text-gray-600 mb-4">
              Are you sure you want to delete <strong>{deleteConfirm.identityValue}</strong>? Its vehicle assignment will be ended.
            </p>
            <div className="flex justify-end gap-2">
              <button onClick={() => setDeleteConfirm(null)} className="px-4 py-2 border border-gray-200 rounded-lg text-sm">Cancel</button>
              <button onClick={() => handleDelete(deleteConfirm)} className="px-4 py-2 bg-red-600 hover:bg-red-700 text-white rounded-lg text-sm">Delete</button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}

// ── Device create/edit modal ─────────────────────────────
function DeviceModal({ edit, vendors, companies, isSuperAdmin, onClose, onSaved }:
  { edit?: Device; vendors: DeviceVendor[]; companies: Company[]; isSuperAdmin: boolean; onClose: () => void; onSaved: () => void }) {
  const [companyId, setCompanyId] = useState('');
  const [vendorCode, setVendorCode] = useState(edit?.vendorCode || '');
  const [deviceType, setDeviceType] = useState(edit?.deviceType ?? 0);
  const [identityType, setIdentityType] = useState(edit?.identityType ?? 0);
  const [identityValue, setIdentityValue] = useState(edit?.identityValue || '');
  const [model, setModel] = useState(edit?.model || '');
  const [firmware, setFirmware] = useState(edit?.firmwareVersion || '');
  const [status, setStatus] = useState(edit?.status ?? 0);
  const [sims, setSims] = useState<SimDraft[]>([]);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');

  const submit = async () => {
    setError('');
    if (!identityValue.trim()) { setError('Device identity (IMEI/serial) is required'); return; }
    if (!edit && !vendorCode) { setError('Select a vendor'); return; }
    if (!edit && isSuperAdmin && !companyId) { setError('Select the company this device belongs to'); return; }
    setSaving(true);
    try {
      if (edit) {
        await api.put(`/devices/${edit.id}`, { deviceType, model, firmwareVersion: firmware, status });
      } else {
        const payload: any = { vendorCode, deviceType, identityType, identityValue: identityValue.trim(), model, firmwareVersion: firmware };
        if (isSuperAdmin) payload.companyId = companyId;
        if (sims.length > 0) payload.sims = sims;
        await api.post('/devices', payload);
      }
      onSaved();
    } catch (e: any) { setError(e.response?.data?.message || 'Failed to save device'); }
    setSaving(false);
  };

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 p-4 overflow-y-auto">
      <div className="bg-white rounded-xl shadow-xl w-full max-w-lg my-8">
        <div className="flex items-center justify-between px-6 py-4 border-b border-gray-200">
          <h3 className="text-lg font-semibold text-gray-900 flex items-center gap-2"><Radio className="w-5 h-5 text-blue-600" /> {edit ? 'Edit Device' : 'Add Device'}</h3>
          <button onClick={onClose} className="p-1 hover:bg-gray-100 rounded-lg"><X className="w-5 h-5" /></button>
        </div>
        <div className="p-6 space-y-4">
          {error && <div className="p-3 bg-red-50 border border-red-200 rounded-lg text-red-700 text-sm">{error}</div>}

          {!edit && isSuperAdmin && (
            <div>
              <label className={LABEL}>Company *</label>
              <select value={companyId} onChange={e => setCompanyId(e.target.value)} className={INPUT}>
                <option value="">Select company...</option>
                {companies.map(c => <option key={c.id} value={c.id}>{c.name}</option>)}
                {companies.length === 0 && <option value="" disabled>No companies available</option>}
              </select>
            </div>
          )}

          {!edit && (
            <div>
              <label className={LABEL}>Vendor *</label>
              <select value={vendorCode} onChange={e => setVendorCode(e.target.value)} className={INPUT}>
                <option value="">Select vendor...</option>
                {vendors.map(v => <option key={v.id} value={v.code}>{v.name} ({v.code})</option>)}
                {vendors.length === 0 && <option value="" disabled>No active vendors</option>}
              </select>
            </div>
          )}
          {edit && (
            <div className="flex items-center gap-2 text-sm text-gray-500 bg-gray-50 border border-gray-200 rounded-lg px-3 py-2">
              <Cpu className="w-4 h-4" /> {edit.vendorName || edit.vendorCode || 'No vendor'} · {edit.identityValue}
            </div>
          )}

          <div className="grid grid-cols-2 gap-4">
            <div>
              <label className={LABEL}>Device Type</label>
              <select value={deviceType} onChange={e => setDeviceType(Number(e.target.value))} className={INPUT}>
                {DEVICE_TYPE_OPTIONS.map(o => <option key={o.value} value={o.value}>{o.label}</option>)}
              </select>
            </div>
            <div>
              <label className={LABEL}>Status</label>
              <select value={status} onChange={e => setStatus(Number(e.target.value))} className={INPUT}>
                {Object.entries(STATUS_LABEL).map(([v, l]) => <option key={v} value={v}>{l}</option>)}
              </select>
            </div>
          </div>

          {!edit && (
            <div className="grid grid-cols-2 gap-4">
              <div>
                <label className={LABEL}>Identity Type</label>
                <select value={identityType} onChange={e => setIdentityType(Number(e.target.value))} className={INPUT}>
                  {IDENTITY_TYPE_OPTIONS.map(o => <option key={o.value} value={o.value}>{o.label}</option>)}
                </select>
              </div>
              <div>
                <label className={LABEL}>Identity (IMEI / Serial / MAC) *</label>
                <input value={identityValue} onChange={e => setIdentityValue(e.target.value)} placeholder="e.g. 860123456789012" className={INPUT} />
              </div>
            </div>
          )}

          <div className="grid grid-cols-2 gap-4">
            <div>
              <label className={LABEL}>Model</label>
              <input value={model} onChange={e => setModel(e.target.value)} placeholder="e.g. SampleTracker-X1" className={INPUT} />
            </div>
            <div>
              <label className={LABEL}>Firmware</label>
              <input value={firmware} onChange={e => setFirmware(e.target.value)} placeholder="e.g. 1.4.2" className={INPUT} />
            </div>
          </div>

          {!edit && (
            <div>
              <div className="flex items-center justify-between mb-1">
                <label className="text-sm font-medium text-gray-700">SIM(s)</label>
                <button type="button" onClick={() => setSims(s => [...s, { iccid: '', phoneNumber: '', carrier: '', isPrimary: sims.length === 0 }])}
                  className="text-xs text-blue-600 hover:text-blue-700 font-medium">+ Add SIM</button>
              </div>
              {sims.map((s, i) => (
                <div key={i} className="flex items-end gap-2 mb-2">
                  <div className="flex-1">
                    <label className={LABEL}>ICCID</label>
                    <input value={s.iccid} onChange={e => setSims(x => x.map((v, j) => j === i ? { ...v, iccid: e.target.value } : v))} placeholder="SIM card number" className={INPUT} />
                  </div>
                  <div className="flex-1">
                    <label className={LABEL}>Carrier</label>
                    <input value={s.carrier} onChange={e => setSims(x => x.map((v, j) => j === i ? { ...v, carrier: e.target.value } : v))} placeholder="e.g. Airtel" className={INPUT} />
                  </div>
                  <label className="flex items-center gap-1.5 text-xs text-gray-600 pb-2 cursor-pointer">
                    <input type="checkbox" checked={s.isPrimary} onChange={e => setSims(x => x.map((v, j) => j === i ? { ...v, isPrimary: e.target.checked } : v))} />
                    Primary
                  </label>
                  {sims.length > 1 && (
                    <button type="button" onClick={() => setSims(x => x.filter((_, j) => j !== i))} className="p-2 text-gray-400 hover:text-red-500"><X className="w-4 h-4" /></button>
                  )}
                </div>
              ))}
            </div>
          )}

          <div className="flex justify-end gap-2 pt-2">
            <button onClick={onClose} className="px-4 py-2 border border-gray-200 rounded-lg text-sm">Cancel</button>
            <button onClick={submit} disabled={saving} className="px-4 py-2 bg-blue-600 hover:bg-blue-700 disabled:bg-blue-400 text-white rounded-lg text-sm">
              {saving ? 'Saving...' : edit ? 'Update' : 'Create'}
            </button>
          </div>
        </div>
      </div>
    </div>
  );
}

// ── SIM management modal ─────────────────────────────────
function SimsModal({ device, onClose, onSaved }: { device: Device; onClose: () => void; onSaved: () => void }) {
  const [iccid, setIccid] = useState('');
  const [carrier, setCarrier] = useState('');
  const [phone, setPhone] = useState('');
  const [isPrimary, setIsPrimary] = useState(device.sims.length === 0);
  const [error, setError] = useState('');

  const addSim = async () => {
    setError('');
    try {
      await api.post(`/devices/${device.id}/sims`, { iccid: iccid || null, carrier: carrier || null, phoneNumber: phone || null, isPrimary });
      onSaved();
    } catch (e: any) { setError(e.response?.data?.message || 'Failed to add SIM'); }
  };

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 p-4">
      <div className="bg-white rounded-xl shadow-xl w-full max-w-md">
        <div className="flex items-center justify-between px-6 py-4 border-b border-gray-200">
          <h3 className="text-lg font-semibold text-gray-900 flex items-center gap-2"><CreditCard className="w-5 h-5 text-blue-600" /> SIMs · {device.identityValue}</h3>
          <button onClick={onClose} className="p-1 hover:bg-gray-100 rounded-lg"><X className="w-5 h-5" /></button>
        </div>
        <div className="p-6 space-y-3">
          {device.sims.length === 0 && <div className="text-sm text-gray-400">No SIMs on this device yet.</div>}
          {device.sims.map(s => (
            <div key={s.id} className="flex items-center justify-between bg-gray-50 border border-gray-200 rounded-lg px-3 py-2">
              <div>
                <div className="text-sm font-medium text-gray-800 flex items-center gap-2">
                  {s.iccid || s.phoneNumber || 'SIM'} {s.isPrimary && <span className="px-1.5 py-0.5 bg-green-100 text-green-700 text-xs rounded-full">Primary</span>}
                </div>
                <div className="text-xs text-gray-500">{s.carrier || '—'}{s.phoneNumber ? ` · ${s.phoneNumber}` : ''}</div>
              </div>
              <span className={`text-xs px-2 py-0.5 rounded-full ${s.status === 0 ? 'bg-green-100 text-green-700' : 'bg-gray-100 text-gray-600'}`}>
                {s.status === 0 ? 'Active' : s.status === 1 ? 'Failover' : s.status === 2 ? 'Blocked' : 'Retired'}
              </span>
            </div>
          ))}
          <div className="border-t border-gray-100 pt-3 mt-3 space-y-2">
            <div className="grid grid-cols-2 gap-2">
              <input value={iccid} onChange={e => setIccid(e.target.value)} placeholder="ICCID" className={INPUT} />
              <input value={carrier} onChange={e => setCarrier(e.target.value)} placeholder="Carrier" className={INPUT} />
            </div>
            <input value={phone} onChange={e => setPhone(e.target.value)} placeholder="Phone number (MSISDN)" className={INPUT} />
            <label className="flex items-center gap-1.5 text-xs text-gray-600 cursor-pointer">
              <input type="checkbox" checked={isPrimary} onChange={e => setIsPrimary(e.target.checked)} /> Make this the primary SIM
            </label>
            {error && <div className="text-sm text-red-600">{error}</div>}
            <button onClick={addSim} className="w-full px-4 py-2 bg-blue-600 hover:bg-blue-700 text-white rounded-lg text-sm">Add SIM</button>
          </div>
        </div>
      </div>
    </div>
  );
}
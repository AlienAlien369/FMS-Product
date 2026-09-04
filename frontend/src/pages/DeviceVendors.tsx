import { useEffect, useState, useCallback } from 'react';
import api, { type DeviceVendor } from '../lib/api';
import { usePermissions } from '../hooks/usePermissions';
import { Plus, Edit, Trash2, X, Cpu, RefreshCw } from 'lucide-react';

const INPUT = 'w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:ring-2 focus:ring-blue-500';
const LABEL = 'block text-sm font-medium text-gray-700 mb-1';

const PROTOCOL_OPTIONS = [
  { value: 0, label: 'TCP Raw' }, { value: 1, label: 'UDP' },
  { value: 2, label: 'HTTP Webhook' }, { value: 3, label: 'MQTT' },
];
const PROTOCOL_LABEL: Record<number, string> = { 0: 'TCP Raw', 1: 'UDP', 2: 'HTTP Webhook', 3: 'MQTT' };
const STATUS_BADGE: Record<number, string> = { 0: 'bg-green-100 text-green-700', 1: 'bg-gray-100 text-gray-700' };
const STATUS_LABEL: Record<number, string> = { 0: 'Active', 1: 'Inactive' };

/** UI affordance only — the backend enforces this from the registered adapters. */
const ADAPTER_BACKED_CODES = new Set(['sample-json', 'pictor', 'itriangle']);

const slugify = (s: string) =>
  s.toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-+|-+$/g, '');

export default function DeviceVendors() {
  const { can, isSuperAdmin } = usePermissions();
  const canCreate = can('devicevendor.create');
  const canEdit = can('devicevendor.update');
  const canDelete = can('devicevendor.delete');

  const [vendors, setVendors] = useState<DeviceVendor[]>([]);
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState('');
  const [notice, setNotice] = useState('');
  const [modal, setModal] = useState<{ open: boolean; edit?: DeviceVendor }>({ open: false });
  const [deleteConfirm, setDeleteConfirm] = useState<DeviceVendor | null>(null);
  const [deleteError, setDeleteError] = useState('');

  const flash = (msg: string) => {
    setNotice(msg);
    window.setTimeout(() => setNotice(''), 5000);
  };

  const fetchData = useCallback(async () => {
    setLoading(true);
    setLoadError('');
    try {
      const res = await api.get('/admin/device-vendors');
      setVendors(res.data.data || []);
    } catch (e: any) {
      setLoadError(e.response?.data?.message || 'Failed to load vendors — you may not have permission (403).');
    }
    setLoading(false);
  }, []);

  useEffect(() => { fetchData(); }, [fetchData]);

  const handleDelete = async (v: DeviceVendor) => {
    setDeleteError('');
    try {
      await api.delete(`/admin/device-vendors/${v.id}`);
      setDeleteConfirm(null);
      flash(`Vendor "${v.name}" deleted`);
      fetchData();
    } catch (e: any) {
      setDeleteError(e.response?.data?.message || 'Failed to delete vendor — you may not have permission (403).');
    }
  };

  // SuperAdmin-only page; the route guard already redirects everyone else.
  if (!isSuperAdmin) return null;

  const protectedDelete = (v: DeviceVendor) =>
    ADAPTER_BACKED_CODES.has(v.code) || (v.deviceCount ?? 0) > 0;

  const deleteHint = (v: DeviceVendor) =>
    ADAPTER_BACKED_CODES.has(v.code)
      ? 'Ships with a registered ingestion adapter — deactivate instead of deleting.'
      : (v.deviceCount ?? 0) > 0
        ? `Has ${v.deviceCount} registered device(s) — remove those devices first.`
        : '';

  return (
    <div className="space-y-4">
      {/* Header */}
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-xl font-semibold text-gray-900">Device Vendors</h1>
          <p className="text-sm text-gray-500 mt-0.5">Vendor and adapter registry — which device vendors the platform can ingest from</p>
        </div>
        {canCreate && (
          <button onClick={() => setModal({ open: true })}
            className="flex items-center gap-2 px-4 py-2 bg-blue-600 hover:bg-blue-700 text-white rounded-lg text-sm font-medium transition-colors">
            <Plus className="w-4 h-4" /> Add Vendor
          </button>
        )}
      </div>

      {notice && <div className="p-3 bg-green-50 border border-green-200 rounded-lg text-sm text-green-700">{notice}</div>}
      {loadError && <div className="p-3 bg-red-50 border border-red-200 rounded-lg text-sm text-red-700">{loadError}</div>}

      {/* Vendors table */}
      <div className="bg-white rounded-xl border border-gray-200 overflow-hidden">
        <div className="overflow-x-auto">
          <table className="w-full text-sm">
            <thead>
              <tr className="border-b border-gray-200 bg-gray-50 text-left text-xs uppercase tracking-wide text-gray-500">
                <th className="px-4 py-3">Vendor</th>
                <th className="px-4 py-3">Protocol</th>
                <th className="px-4 py-3">Payload Format</th>
                <th className="px-4 py-3">Adapter</th>
                <th className="px-4 py-3 text-center">Devices</th>
                <th className="px-4 py-3">Status</th>
                <th className="px-4 py-3 text-right">Actions</th>
              </tr>
            </thead>
            <tbody>
              {loading ? (
                <tr><td colSpan={7} className="px-4 py-10 text-center text-gray-400">Loading...</td></tr>
              ) : vendors.length === 0 ? (
                <tr><td colSpan={7} className="px-4 py-10 text-center text-gray-400">No vendors yet — add your first device vendor.</td></tr>
              ) : vendors.map(v => (
                <tr key={v.id} className="border-b border-gray-100 hover:bg-gray-50">
                  <td className="px-4 py-3">
                    <div className="flex items-center gap-2 font-medium text-gray-900">
                      <Cpu className="w-4 h-4 text-blue-500 shrink-0" /> {v.name}
                    </div>
                    <div className="text-xs text-gray-400 font-mono">{v.code}</div>
                    {v.description && <div className="text-xs text-gray-500 mt-0.5 max-w-md">{v.description}</div>}
                  </td>
                  <td className="px-4 py-3 text-gray-700">{PROTOCOL_LABEL[v.protocolType] || v.protocolType}</td>
                  <td className="px-4 py-3"><code className="text-xs bg-gray-100 px-1.5 py-0.5 rounded">{v.payloadFormat || '—'}</code></td>
                  <td className="px-4 py-3 text-gray-500">{v.adapterVersion || '—'}</td>
                  <td className="px-4 py-3 text-center text-gray-700">{v.deviceCount ?? 0}</td>
                  <td className="px-4 py-3">
                    <button
                      disabled={!canEdit}
                      onClick={async () => {
                        try {
                          await api.put(`/admin/device-vendors/${v.id}`, { status: v.status === 0 ? 1 : 0 });
                          flash(v.status === 0 ? `Vendor "${v.name}" deactivated — hidden from the device form dropdown` : `Vendor "${v.name}" activated`);
                          fetchData();
                        } catch (e: any) {
                          flash(e.response?.data?.message || 'Failed to toggle vendor status');
                        }
                      }}
                      title={canEdit ? (v.status === 0 ? 'Deactivate (hides from the Add Device vendor dropdown)' : 'Activate') : undefined}
                      className={`inline-flex px-2 py-0.5 rounded-full text-xs font-medium transition-colors ${STATUS_BADGE[v.status] || 'bg-gray-100 text-gray-700'} ${canEdit ? 'hover:ring-2 hover:ring-blue-200 cursor-pointer' : 'cursor-default'}`}>
                      {v.status === 0 ? 'Active' : 'Inactive'} <RefreshCw className="w-3 h-3 ml-1" />
                    </button>
                  </td>
                  <td className="px-4 py-3">
                    <div className="flex items-center justify-end gap-1">
                      {canEdit && (
                        <button onClick={() => setModal({ open: true, edit: v })} className="p-1.5 hover:bg-gray-100 rounded-lg" title="Edit vendor">
                          <Edit className="w-4 h-4 text-gray-500" />
                        </button>
                      )}
                      {canDelete && (
                        <button
                          onClick={() => { setDeleteError(''); setDeleteConfirm(v); }}
                          disabled={protectedDelete(v)}
                          title={protectedDelete(v) ? deleteHint(v) : 'Delete vendor'}
                          className={`p-1.5 rounded-lg ${protectedDelete(v) ? 'opacity-40 cursor-not-allowed' : 'hover:bg-gray-100'}`}>
                          <Trash2 className="w-4 h-4 text-red-500" />
                        </button>
                      )}
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>

      {/* Create / edit modal */}
      {modal.open && <VendorModal edit={modal.edit} onClose={() => setModal({ open: false })} onSaved={() => { setModal({ open: false }); fetchData(); }} />}

      {/* Delete confirm */}
      {deleteConfirm && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 p-4">
          <div className="bg-white rounded-xl shadow-xl w-full max-w-sm p-6">
            <h3 className="text-lg font-semibold text-gray-900 mb-2">Delete vendor</h3>
            <p className="text-sm text-gray-600 mb-4">
              Are you sure you want to delete <strong>{deleteConfirm.name}</strong> ({deleteConfirm.code})?
            </p>
            {deleteError && <div className="p-3 bg-red-50 border border-red-200 rounded-lg text-sm text-red-700 mb-4">{deleteError}</div>}
            <div className="flex justify-end gap-2">
              <button onClick={() => { setDeleteConfirm(null); setDeleteError(''); }} className="px-4 py-2 border border-gray-200 rounded-lg text-sm">Cancel</button>
              <button onClick={() => handleDelete(deleteConfirm)} className="px-4 py-2 bg-red-600 hover:bg-red-700 text-white rounded-lg text-sm">Delete</button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}

// ── Vendor create/edit modal ─────────────────────────────
function VendorModal({ edit, onClose, onSaved }: {
  edit?: DeviceVendor; onClose: () => void; onSaved: () => void;
}) {
  const isEdit = !!edit;
  const [name, setName] = useState(edit?.name || '');
  const [code, setCode] = useState(edit?.code || '');
  const [codeTouched, setCodeTouched] = useState(isEdit);
  const [description, setDescription] = useState(edit?.description || '');
  const [adapterVersion, setAdapterVersion] = useState(edit?.adapterVersion || '1.0.0');
  const [protocolType, setProtocolType] = useState(edit?.protocolType ?? 2);
  const [payloadFormat, setPayloadFormat] = useState(edit?.payloadFormat || '');
  const [status, setStatus] = useState(edit?.status ?? 0);
  const [listenerConfig, setListenerConfig] = useState(edit?.listenerConfig || '');
  const [capabilities, setCapabilities] = useState(edit?.capabilities || '');
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');

  const onNameChange = (v: string) => {
    setName(v);
    if (!codeTouched) setCode(slugify(v));
  };

  const submit = async () => {
    setError('');
    if (!name.trim()) { setError('Vendor name is required'); return; }
    if (!isEdit && !/^[a-z0-9]+(-[a-z0-9]+)*$/.test(code.trim())) {
      setError('Vendor code must be lowercase kebab-case (letters, digits and dashes only)');
      return;
    }
    setSaving(true);
    try {
      const payload: any = {
        name: name.trim(),
        description: description || null,
        adapterVersion: adapterVersion || null,
        protocolType,
        payloadFormat: payloadFormat || null,
        status,
        listenerConfig: listenerConfig || null,
        capabilities: capabilities || null,
      };
      if (isEdit) {
        await api.put(`/admin/device-vendors/${edit!.id}`, payload);
      } else {
        await api.post('/admin/device-vendors', { ...payload, code: code.trim() });
      }
      onSaved();
    } catch (e: any) {
      setError(e.response?.data?.message || 'Failed to save vendor — you may not have permission (403).');
    }
    setSaving(false);
  };

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 p-4 overflow-y-auto">
      <div className="bg-white rounded-xl shadow-xl w-full max-w-lg my-8">
        <div className="flex items-center justify-between px-6 py-4 border-b border-gray-200">
          <h3 className="text-lg font-semibold text-gray-900 flex items-center gap-2"><Cpu className="w-5 h-5 text-blue-600" /> {isEdit ? 'Edit Vendor' : 'Add Vendor'}</h3>
          <button onClick={onClose} className="p-1 hover:bg-gray-100 rounded-lg"><X className="w-5 h-5" /></button>
        </div>
        <div className="p-6 space-y-4">
          {error && <div className="p-3 bg-red-50 border border-red-200 rounded-lg text-red-700 text-sm">{error}</div>}
          {isEdit && (
            <p className="text-xs text-amber-600 bg-amber-50 border border-amber-200 rounded-lg p-2">
              The vendor code is immutable — it anchors adapter lookup and existing device rows. You can rename the display name and edit every other field.
            </p>
          )}

          <div className="grid grid-cols-2 gap-4">
            <div>
              <label className={LABEL}>Name *</label>
              <input value={name} onChange={e => onNameChange(e.target.value)} placeholder="e.g. Streamax" className={INPUT} />
            </div>
            <div>
              <label className={LABEL}>Code {!isEdit && '* (auto from name)'}</label>
              <input value={code} disabled={isEdit} onChange={e => { setCodeTouched(true); setCode(e.target.value); }}
                placeholder="e.g. streamax" className={`${INPUT} ${isEdit ? 'bg-gray-50 text-gray-500' : ''} font-mono`} />
            </div>
          </div>

          <div>
            <label className={LABEL}>Description</label>
            <textarea value={description} onChange={e => setDescription(e.target.value)} rows={2}
              placeholder="What this vendor integrates (protocol notes, status of the integration…)"
              className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:ring-2 focus:ring-blue-500" />
          </div>

          <div className="grid grid-cols-2 gap-4">
            <div>
              <label className={LABEL}>Protocol</label>
              <select value={protocolType} onChange={e => setProtocolType(Number(e.target.value))} className={INPUT}>
                {PROTOCOL_OPTIONS.map(o => <option key={o.value} value={o.value}>{o.label}</option>)}
              </select>
            </div>
            <div>
              <label className={LABEL}>Payload Format</label>
              <input value={payloadFormat} onChange={e => setPayloadFormat(e.target.value)} placeholder="e.g. streamax-json-v1" className={INPUT} />
            </div>
          </div>

          <div className="grid grid-cols-2 gap-4">
            <div>
              <label className={LABEL}>Adapter Version</label>
              <input value={adapterVersion} onChange={e => setAdapterVersion(e.target.value)} placeholder="e.g. 1.0.0" className={INPUT} />
            </div>
            <div>
              <label className={LABEL}>Status</label>
              <select value={status} onChange={e => setStatus(Number(e.target.value))} className={INPUT}>
                <option value={0}>Active — usable by tenants</option>
                <option value={1}>Inactive — hidden from device forms</option>
              </select>
            </div>
          </div>

          <div>
            <label className={LABEL}>Listener Config (JSON)</label>
            <textarea value={listenerConfig} onChange={e => setListenerConfig(e.target.value)} rows={2}
              placeholder='e.g. {"path":"api/v1/ingest/streamax"}'
              className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:ring-2 focus:ring-blue-500 font-mono" />
          </div>
          <div>
            <label className={LABEL}>Capabilities (JSON array)</label>
            <textarea value={capabilities} onChange={e => setCapabilities(e.target.value)} rows={2}
              placeholder='e.g. ["gps","speed","ignition","fuel","driverId"]'
              className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:ring-2 focus:ring-blue-500 font-mono" />
          </div>

          <div className="flex justify-end gap-2 pt-2">
            <button onClick={onClose} className="px-4 py-2 border border-gray-200 rounded-lg text-sm">Cancel</button>
            <button onClick={submit} disabled={saving} className="px-4 py-2 bg-blue-600 hover:bg-blue-700 disabled:bg-blue-400 text-white rounded-lg text-sm">
              {saving ? 'Saving...' : isEdit ? 'Update' : 'Create'}
            </button>
          </div>
        </div>
      </div>
    </div>
  );
}

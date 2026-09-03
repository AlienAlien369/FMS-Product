import { useEffect, useRef, useState } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import api from '../lib/api';
import {
  Layers, Plus, Pencil, Trash2, GripVertical, X, ChevronDown, ChevronRight,
} from 'lucide-react';
import { usePermissions } from '../hooks/usePermissions';

// ── Types ─────────────────────────────────────────────────────────────────
export interface RegistryPage {
  id: string; key: string; label: string; planned: boolean; nav: boolean; route?: string;
  adminOnly: boolean; isCore: boolean; status: number; displayOrder: number; description?: string;
}
export interface ModuleRow {
  id: string; code: string; name: string; description?: string; icon?: string;
  isCore: boolean; status: number; displayOrder: number;
  pageCount: number; plannedPageCount: number;
  pages: RegistryPage[];
}

type ModuleModalState = { mode: 'create' } | { mode: 'edit'; module: ModuleRow } | null;
type PageModalState =
  | { mode: 'create'; moduleId: string; moduleName: string }
  | { mode: 'edit'; page: RegistryPage; moduleId: string; moduleName: string }
  | null;
type ConfirmState =
  | { type: 'module'; target: ModuleRow }
  | { type: 'page'; target: RegistryPage; moduleId: string }
  | null;

const fetchModules = async (): Promise<ModuleRow[]> => {
  const r = await api.get('/modules?pageSize=50');
  return r.data.data?.items || [];
};

const slugify = (s: string) =>
  s.toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-+|-+$/g, '');

// Canonical registry page keys (frontend mirror) — registry keys are locked.
const PageKeys = new Set([
  'dashboard', 'vehicle', 'driver', 'geofence', 'route', 'trip', 'alert', 'fuel',
  'maintenance', 'report', 'company', 'user', 'role', 'localization', 'settings',
  'document', 'subscription', 'client', 'notification', 'platform', 'package', 'module',
]);

// ── Main page ─────────────────────────────────────────────────────────────
export default function Modules() {
  const { isSuperAdmin } = usePermissions();
  const qc = useQueryClient();

  const [expanded, setExpanded] = useState<Set<string>>(new Set());
  const seededRef = useRef(false);
  const [moduleModal, setModuleModal] = useState<ModuleModalState>(null);
  const [pageModal, setPageModal] = useState<PageModalState>(null);
  const [confirmDelete, setConfirmDelete] = useState<ConfirmState>(null);
  const [cascade, setCascade] = useState(false);
  const [deleteError, setDeleteError] = useState('');
  const [notice, setNotice] = useState('');
  const noticeTimer = useRef<number | undefined>(undefined);

  // Drag & drop state (native HTML5 — no dnd library in the repo)
  const [dragModuleId, setDragModuleId] = useState<string | null>(null);
  const [overModuleId, setOverModuleId] = useState<string | null>(null);
  const [dragPage, setDragPage] = useState<{ moduleId: string; pageId: string } | null>(null);
  const [overPageId, setOverPageId] = useState<string | null>(null);

  const flash = (msg: string) => {
    setNotice(msg);
    window.clearTimeout(noticeTimer.current);
    noticeTimer.current = window.setTimeout(() => setNotice(''), 5000);
  };

  const {
    data: modules = [],
    isLoading,
    error: queryError,
    refetch,
  } = useQuery({
    queryKey: ['modules'],
    queryFn: fetchModules,
  });

  // Expand every module card on first load.
  useEffect(() => {
    if (!seededRef.current && modules.length) {
      seededRef.current = true;
      setExpanded(new Set(modules.map(m => m.id)));
    }
  }, [modules]);

  // Defensive: the route guard already redirects non-SuperAdmin (PermissionRoute
  // adminOnly); never render the management surface for anyone else.
  if (!isSuperAdmin) return null;

  const toggleExpanded = (id: string) => {
    setExpanded(prev => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id); else next.add(id);
      return next;
    });
  };

  // ── Mutations (optimistic reorder with rollback) ────────────────────────
  const reorderModules = useMutation({
    mutationFn: (ids: string[]) => api.put('/admin/modules/reorder', { moduleIds: ids }),
    onMutate: async (ids) => {
      await qc.cancelQueries({ queryKey: ['modules'] });
      const prev = qc.getQueryData<ModuleRow[]>(['modules']);
      qc.setQueryData<ModuleRow[]>(['modules'], old => {
        if (!old) return old;
        const byId = new Map(old.map(m => [m.id, m]));
        const ordered = ids.map(id => byId.get(id)).filter((m): m is ModuleRow => !!m);
        return [...ordered, ...old.filter(m => !ids.includes(m.id))];
      });
      return { prev };
    },
    onError: (_e, _ids, ctx) => {
      if (ctx?.prev) qc.setQueryData(['modules'], ctx.prev);
      flash('Reorder failed — changes reverted. You may not have permission (403).');
    },
  });

  const reorderPages = useMutation({
    mutationFn: ({ moduleId, pageIds }: { moduleId: string; pageIds: string[] }) =>
      api.put('/admin/pages/reorder', { moduleId, pageIds }),
    onMutate: async ({ moduleId, pageIds }) => {
      await qc.cancelQueries({ queryKey: ['modules'] });
      const prev = qc.getQueryData<ModuleRow[]>(['modules']);
      qc.setQueryData<ModuleRow[]>(['modules'], old => old?.map(m => {
        if (m.id !== moduleId) return m;
        const byId = new Map(m.pages.map(p => [p.id, p]));
        const ordered = pageIds.map(id => byId.get(id)).filter((p): p is RegistryPage => !!p);
        return { ...m, pages: [...ordered, ...m.pages.filter(p => !pageIds.includes(p.id))] };
      }));
      return { prev };
    },
    onError: (_e, _v, ctx) => {
      if (ctx?.prev) qc.setQueryData(['modules'], ctx.prev);
      flash('Reorder failed — changes reverted. You may not have permission (403).');
    },
  });

  const invalidateModules = () => qc.invalidateQueries({ queryKey: ['modules'] });

  // ── DnD handlers ─────────────────────────────────────────────────────────
  const dropModule = (targetId: string) => {
    const dragged = dragModuleId;
    setDragModuleId(null); setOverModuleId(null);
    if (!dragged || dragged === targetId || !modules.length) return;
    const ids = modules.map(m => m.id);
    const from = ids.indexOf(dragged);
    const to = ids.indexOf(targetId);
    if (from < 0 || to < 0) return;
    ids.splice(from, 1);
    ids.splice(to, 0, dragged);
    reorderModules.mutate(ids);
  };

  const dropPage = (moduleId: string, targetPageId: string) => {
    const dragged = dragPage;
    setDragPage(null); setOverPageId(null);
    if (!dragged || dragged.moduleId !== moduleId || dragged.pageId === targetPageId) return;
    const mod = modules.find(m => m.id === moduleId);
    if (!mod) return;
    const ids = mod.pages.map(p => p.id);
    const from = ids.indexOf(dragged.pageId);
    const to = ids.indexOf(targetPageId);
    if (from < 0 || to < 0) return;
    ids.splice(from, 1);
    ids.splice(to, 0, dragged.pageId);
    reorderPages.mutate({ moduleId, pageIds: ids });
  };

  // ── Delete actions ───────────────────────────────────────────────────────
  const doDeleteModule = async () => {
    if (!confirmDelete || confirmDelete.type !== 'module') return;
    try {
      await api.delete(`/admin/modules/${confirmDelete.target.id}?cascade=${cascade}`);
      setConfirmDelete(null); setCascade(false); setDeleteError('');
      await invalidateModules();
    } catch (e: any) {
      setDeleteError(e.response?.data?.message || 'You are not authorized to delete modules (403).');
    }
  };

  const doDeletePage = async () => {
    if (!confirmDelete || confirmDelete.type !== 'page') return;
    try {
      await api.delete(`/admin/pages/${confirmDelete.target.id}`);
      setConfirmDelete(null); setDeleteError('');
      await invalidateModules();
    } catch (e: any) {
      setDeleteError(e.response?.data?.message || 'You are not authorized to delete pages (403).');
    }
  };

  if (isLoading) {
    return <div className="flex items-center justify-center h-64"><div className="animate-spin rounded-full h-8 w-8 border-b-2 border-blue-600" /></div>;
  }

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <div>
          <h2 className="text-xl font-bold text-gray-900">Module &amp; Form Management</h2>
          <p className="text-sm text-gray-500 mt-0.5">
            Top-level modules group the product's pages/forms. A company can use a module only when its package includes it.
            Super Admin manages this registry; every write is enforced server-side (403 for other roles).
          </p>
        </div>
        <div className="flex items-center gap-3">
          <span className="text-sm text-gray-500">{modules.length} modules</span>
          <button onClick={() => setModuleModal({ mode: 'create' })}
            className="inline-flex items-center gap-1.5 px-3 py-2 bg-blue-600 text-white text-sm font-medium rounded-lg hover:bg-blue-700 transition-colors">
            <Plus className="w-4 h-4" /> Add Module
          </button>
        </div>
      </div>

      {queryError && (
        <div className="p-4 bg-red-50 border border-red-200 rounded-lg text-sm text-red-700 flex items-center justify-between">
          <span>Failed to load the module registry — the API may be unreachable or your session expired.</span>
          <button onClick={() => refetch()}
            className="px-3 py-1.5 bg-red-600 text-white text-xs font-medium rounded-lg hover:bg-red-700 transition-colors flex-shrink-0">
            Retry
          </button>
        </div>
      )}

      {notice && (
        <div className="p-3 bg-amber-50 border border-amber-200 rounded-lg text-sm text-amber-800">{notice}</div>
      )}

      {queryError ? null : (
      <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
        {modules.length === 0 ? (
          <div className="col-span-full text-center py-12 text-gray-400">No modules found</div>
        ) : (
          modules.map(m => {
            const isExpanded = expanded.has(m.id);
            return (
              <div
                key={m.id}
                onDragOver={e => { e.preventDefault(); setOverModuleId(m.id); }}
                onDragLeave={() => setOverModuleId(v => (v === m.id ? null : v))}
                onDrop={() => dropModule(m.id)}
                className={`bg-white rounded-xl border p-5 transition-shadow ${overModuleId === m.id ? 'border-blue-300 ring-2 ring-blue-100' : 'border-gray-200'}`}
              >
                {/* Header (click to expand/collapse) */}
                <div className="flex items-center gap-3 cursor-pointer select-none" onClick={() => toggleExpanded(m.id)}>
                  <button
                    draggable
                    onDragStart={e => { e.dataTransfer.setData('text/plain', m.id); e.dataTransfer.effectAllowed = 'move'; setDragModuleId(m.id); }}
                    onDragEnd={() => setDragModuleId(null)}
                    onClick={e => e.stopPropagation()}
                    title="Drag to reorder modules"
                    className="p-0.5 -ml-1 cursor-grab active:cursor-grabbing hover:bg-gray-100 rounded">
                    <GripVertical className="w-4 h-4 text-gray-300" />
                  </button>
                  {isExpanded ? <ChevronDown className="w-4 h-4 text-gray-400" /> : <ChevronRight className="w-4 h-4 text-gray-400" />}
                  <div className="w-10 h-10 bg-blue-50 rounded-lg flex items-center justify-center flex-shrink-0">
                    <Layers className="w-5 h-5 text-blue-600" />
                  </div>
                  <div className="flex-1 min-w-0">
                    <div className="flex items-center gap-2">
                      <span className="text-sm font-semibold text-gray-900">{m.name}</span>
                      <span className={`px-1.5 py-0.5 text-xs font-medium rounded ${m.isCore ? 'bg-purple-100 text-purple-700' : 'bg-gray-100 text-gray-600'}`}>
                        {m.isCore ? 'Core' : 'Custom'}
                      </span>
                      <span className={`px-1.5 py-0.5 text-xs font-medium rounded ${m.status === 0 ? 'bg-green-100 text-green-700' : 'bg-gray-100 text-gray-700'}`}>
                        {m.status === 0 ? 'Active' : 'Inactive'}
                      </span>
                    </div>
                    <p className="text-xs text-gray-500 mt-0.5 line-clamp-1">{m.description || m.code}</p>
                  </div>
                  <div className="text-right flex-shrink-0">
                    <div className="text-lg font-bold text-gray-900">{m.pageCount}</div>
                    <div className="text-xs text-gray-400">pages</div>
                  </div>
                </div>

                {/* Module actions */}
                <div className="flex items-center gap-2 mt-3">
                  <button onClick={() => setModuleModal({ mode: 'edit', module: m })}
                    className="inline-flex items-center gap-1 px-2.5 py-1.5 text-xs font-medium text-gray-600 hover:bg-gray-100 rounded-lg transition-colors">
                    <Pencil className="w-3.5 h-3.5" /> Edit
                  </button>
                  <button
                    onClick={() => { setDeleteError(''); setCascade(false); setConfirmDelete({ type: 'module', target: m }); }}
                    disabled={m.isCore}
                    title={m.isCore ? 'Core modules are system-protected and cannot be deleted' : 'Delete module'}
                    className="inline-flex items-center gap-1 px-2.5 py-1.5 text-xs font-medium text-red-600 hover:bg-red-50 rounded-lg transition-colors disabled:text-red-300 disabled:hover:bg-transparent disabled:cursor-not-allowed">
                    <Trash2 className="w-3.5 h-3.5" /> Delete
                  </button>
                  <button onClick={() => setPageModal({ mode: 'create', moduleId: m.id, moduleName: m.name })}
                    className="ml-auto inline-flex items-center gap-1 px-2.5 py-1.5 text-xs font-medium text-blue-600 hover:bg-blue-50 rounded-lg transition-colors">
                    <Plus className="w-3.5 h-3.5" /> Add Page
                  </button>
                </div>

                {/* Child pages/forms */}
                {isExpanded && (
                  <div className="mt-3 border-t border-gray-100 pt-3 space-y-1">
                    {m.pages.length === 0 ? (
                      <div className="text-xs text-gray-400 py-1">No pages yet — add one above.</div>
                    ) : (
                      m.pages.map(p => (
                        <div
                          key={p.id}
                          onDragOver={e => { e.preventDefault(); setOverPageId(p.id); }}
                          onDragLeave={() => setOverPageId(v => (v === p.id ? null : v))}
                          onDrop={() => dropPage(m.id, p.id)}
                          className={`flex items-center gap-2 px-2 py-1.5 rounded-md ${overPageId === p.id ? 'ring-1 ring-blue-300 bg-blue-50' : 'hover:bg-gray-50'}`}
                        >
                          <button
                            draggable
                            onDragStart={e => { e.dataTransfer.setData('text/plain', p.id); e.dataTransfer.effectAllowed = 'move'; setDragPage({ moduleId: m.id, pageId: p.id }); }}
                            onDragEnd={() => setDragPage(null)}
                            title="Drag to reorder pages"
                            className="p-0.5 -ml-1 cursor-grab active:cursor-grabbing hover:bg-gray-100 rounded">
                            <GripVertical className="w-3.5 h-3.5 text-gray-300" />
                          </button>
                          <span className="text-xs font-medium text-gray-700">{p.label}</span>
                          <span className="text-[10px] text-gray-400 font-mono truncate">
                            {p.key}{p.route ? ` · ${p.route}` : ''}
                          </span>
                          {p.isCore && <span className="px-1 py-0.5 rounded bg-purple-100 text-purple-700 text-[10px] font-medium flex-shrink-0">Core</span>}
                          {p.planned
                            ? <span className="px-1.5 py-0.5 rounded-full bg-amber-100 text-amber-700 text-[10px] font-medium flex-shrink-0">Planned</span>
                            : <span className={`px-1.5 py-0.5 rounded-full text-[10px] font-medium flex-shrink-0 ${p.status === 0 ? 'bg-green-100 text-green-700' : 'bg-gray-100 text-gray-700'}`}>
                                {p.status === 0 ? 'Active' : 'Inactive'}
                              </span>}
                          <div className="ml-auto flex gap-0.5">
                            <button onClick={() => setPageModal({ mode: 'edit', page: p, moduleId: m.id, moduleName: m.name })}
                              title="Edit page" className="p-1 hover:bg-gray-100 rounded"><Pencil className="w-3.5 h-3.5 text-gray-400" /></button>
                            <button
                              onClick={() => { setDeleteError(''); setConfirmDelete({ type: 'page', target: p, moduleId: m.id }); }}
                              disabled={p.isCore}
                              title={p.isCore ? 'Core pages are system-protected and cannot be deleted' : 'Delete page'}
                              className="p-1 hover:bg-red-50 rounded disabled:opacity-30 disabled:cursor-not-allowed">
                              <Trash2 className="w-3.5 h-3.5 text-gray-400" />
                            </button>
                          </div>
                        </div>
                      ))
                    )}
                  </div>
                )}
              </div>
            );
          }          ))}
      </div>
      )}

      {moduleModal && (
        <ModuleModal
          initial={moduleModal.mode === 'edit' ? moduleModal.module : null}
          onClose={() => setModuleModal(null)}
          onSaved={async () => { setModuleModal(null); await invalidateModules(); }}
        />
      )}

      {pageModal && (
        <PageModal
          moduleId={pageModal.moduleId}
          moduleName={pageModal.moduleName}
          initial={pageModal.mode === 'edit' ? pageModal.page : null}
          onClose={() => setPageModal(null)}
          onSaved={async () => { setPageModal(null); await invalidateModules(); }}
        />
      )}

      {confirmDelete && (
        <div className="fixed inset-0 z-50 flex items-center justify-center p-4">
          <div className="fixed inset-0 bg-black/50" onClick={() => setConfirmDelete(null)} />
          <div className="relative bg-white rounded-xl shadow-2xl w-full max-w-md p-6">
            <h3 className="text-lg font-semibold text-gray-900">
              {confirmDelete.type === 'module' ? `Delete module "${confirmDelete.target.name}"?` : `Delete page "${confirmDelete.target.label}"?`}
            </h3>

            {confirmDelete.type === 'module' ? (
              <div className="mt-3 space-y-2">
                <p className="text-sm text-gray-500">
                  {confirmDelete.target.pages.length > 0
                    ? `This will remove the module and its ${confirmDelete.target.pages.length} page(s):`
                    : 'This module has no pages. It will be removed from the registry.'}
                </p>
                {confirmDelete.target.pages.length > 0 && (
                  <ul className="text-xs text-gray-600 bg-gray-50 border border-gray-200 rounded-lg p-3 space-y-1 max-h-40 overflow-y-auto">
                    {confirmDelete.target.pages.map(p => (
                      <li key={p.id} className="flex items-center gap-1.5">
                        <span className="text-red-400">•</span> {p.label}
                        <span className="text-gray-400 font-mono">({p.key})</span>
                        {p.planned && <span className="px-1 rounded bg-amber-100 text-amber-700 text-[10px]">Planned</span>}
                      </li>
                    ))}
                  </ul>
                )}
                <p className="text-xs text-gray-400">Each page's 6 permissions (view/create/update/delete/export/import) and any role grants will also be removed.</p>
                {confirmDelete.target.pages.length > 0 && (
                  <label className="flex items-center gap-2 mt-1 text-sm text-gray-700">
                    <input type="checkbox" checked={cascade} onChange={e => setCascade(e.target.checked)} className="rounded" />
                    Yes, cascade delete all pages inside this module
                  </label>
                )}
              </div>
            ) : (
              <p className="text-sm text-gray-500 mt-2">
                This will remove the page and its 6 permissions (view/create/update/delete/export/import) from every role that references them.
              </p>
            )}

            {deleteError && <p className="text-sm text-red-600 mt-3">{deleteError}</p>}
            <div className="flex items-center justify-end gap-3 mt-5">
              <button onClick={() => setConfirmDelete(null)} className="px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-100 rounded-lg">Cancel</button>
              <button
                onClick={confirmDelete.type === 'module' ? doDeleteModule : doDeletePage}
                disabled={confirmDelete.type === 'module' && confirmDelete.target.isCore}
                className="px-4 py-2 bg-red-600 text-white text-sm font-medium rounded-lg hover:bg-red-700 disabled:bg-red-300 transition-colors">
                Delete
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}

// ── Module create/edit modal ──────────────────────────────────────────────
function ModuleModal({ initial, onClose, onSaved }: {
  initial: ModuleRow | null;
  onClose: () => void;
  onSaved: () => Promise<void> | void;
}) {
  const isEdit = !!initial;
  const isCore = !!initial?.isCore;
  const [name, setName] = useState(initial?.name || '');
  const [slug, setSlug] = useState(initial?.code || '');
  const [slugTouched, setSlugTouched] = useState(isEdit);
  const [description, setDescription] = useState(initial?.description || '');
  const [category, setCategory] = useState<'Core' | 'Custom'>(initial ? (initial.isCore ? 'Core' : 'Custom') : 'Custom');
  const [status, setStatus] = useState(initial?.status ?? 0);
  const [displayOrder, setDisplayOrder] = useState(initial?.displayOrder ?? 0);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');

  const onNameChange = (v: string) => {
    setName(v);
    if (!slugTouched) setSlug(slugify(v));
  };

  const handleSubmit = async () => {
    if (!name.trim()) { setError('Module name is required'); return; }
    if (!slug.trim()) { setError('Module slug is required'); return; }
    setSaving(true); setError('');
    try {
      if (isEdit) {
        await api.put(`/admin/modules/${initial!.id}`, {
          ...(slug !== initial!.code && !isCore ? { code: slug.trim() } : {}),
          name: name.trim(),
          description,
          status,
          displayOrder,
        });
      } else {
        await api.post('/admin/modules', {
          code: slug.trim(), name: name.trim(), description,
          isCore: category === 'Core', status, displayOrder,
        });
      }
      await onSaved();
    } catch (e: any) {
      setError(e.response?.data?.message || 'Failed to save module — you may not have permission (403).');
    }
    setSaving(false);
  };

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center p-4">
      <div className="fixed inset-0 bg-black/50" onClick={onClose} />
      <div className="relative bg-white rounded-xl shadow-2xl w-full max-w-lg">
        <div className="flex items-center justify-between px-6 py-4 border-b border-gray-200">
          <div className="flex items-center gap-2">
            <Layers className="w-5 h-5 text-blue-600" />
            <h2 className="text-lg font-semibold text-gray-900">{isEdit ? 'Edit Module' : 'Add Module'}</h2>
          </div>
          <button onClick={onClose} className="p-1 hover:bg-gray-100 rounded-lg"><X className="w-5 h-5 text-gray-500" /></button>
        </div>
        <div className="px-6 py-4 space-y-4">
          {error && <div className="p-3 bg-red-50 border border-red-200 rounded-lg text-sm text-red-700">{error}</div>}
          {isCore && (
            <p className="text-xs text-amber-600 bg-amber-50 border border-amber-200 rounded-lg p-2">
              Core module — name, slug, description and icon are system-protected; you can only toggle status and order. Core modules cannot be deleted.
            </p>
          )}
          <div className="grid grid-cols-2 gap-4">
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">Name *</label>
              <input value={name} onChange={e => onNameChange(e.target.value)} disabled={isEdit && isCore} placeholder="e.g. Fleet Operations"
                className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:ring-2 focus:ring-blue-500 focus:border-blue-500 disabled:bg-gray-50" />
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">Slug *</label>
              <input value={slug} onChange={e => { setSlug(slugify(e.target.value)); setSlugTouched(true); }} disabled={isEdit && isCore}
                placeholder="auto-generated from name"
                className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:ring-2 focus:ring-blue-500 focus:border-blue-500 disabled:bg-gray-50" />
            </div>
          </div>
          <p className="text-xs text-gray-400 -mt-2">Slug is lowercase kebab-case and unique across modules.</p>
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">Description</label>
            <textarea value={description} onChange={e => setDescription(e.target.value)} rows={2} placeholder="What this module groups" disabled={isEdit && isCore}
              className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:ring-2 focus:ring-blue-500 focus:border-blue-500 disabled:bg-gray-50 disabled:text-gray-400" />
          </div>
          <div className="grid grid-cols-3 gap-4">
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">Category</label>
              <select value={category} onChange={e => setCategory(e.target.value as 'Core' | 'Custom')} disabled={isEdit}
                title={isEdit ? 'Category is fixed at creation; Core modules are system-protected' : 'Category'}
                className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:ring-2 focus:ring-blue-500 focus:border-blue-500 disabled:bg-gray-50 disabled:text-gray-400">
                <option value="Custom">Custom</option>
                <option value="Core">Core</option>
              </select>
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">Status</label>
              <select value={status} onChange={e => setStatus(Number(e.target.value))}
                className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:ring-2 focus:ring-blue-500 focus:border-blue-500">
                <option value={0}>Active</option>
                <option value={1}>Inactive</option>
              </select>
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">Display Order</label>
              <input type="number" value={displayOrder} onChange={e => setDisplayOrder(Number(e.target.value))}
                className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:ring-2 focus:ring-blue-500 focus:border-blue-500" />
            </div>
          </div>
          <p className="text-xs text-gray-400 -mt-2">Tip: use drag handles on the list to reorder instead of typing Display Order.</p>
        </div>
        <div className="flex items-center justify-end gap-3 px-6 py-4 border-t border-gray-200">
          <button onClick={onClose} className="px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-100 rounded-lg">Cancel</button>
          <button onClick={handleSubmit} disabled={saving}
            className="px-5 py-2 bg-blue-600 text-white text-sm font-medium rounded-lg hover:bg-blue-700 disabled:bg-blue-400">
            {saving ? 'Saving...' : isEdit ? 'Update Module' : 'Create Module'}
          </button>
        </div>
      </div>
    </div>
  );
}

// ── Page/Form create/edit modal ───────────────────────────────────────────
function PageModal({ moduleId, moduleName, initial, onClose, onSaved }: {
  moduleId: string;
  moduleName: string;
  initial: RegistryPage | null;
  onClose: () => void;
  onSaved: () => Promise<void> | void;
}) {
  const isEdit = !!initial;
  const isCore = !!initial?.isCore;
  const isRegistryPage = !!initial && PageKeys.has(initial.key);
  const [name, setName] = useState(initial?.label || '');
  const [slug, setSlug] = useState(initial?.key || '');
  const [slugTouched, setSlugTouched] = useState(isEdit);
  const [route, setRoute] = useState(initial?.route || '');
  const [statusKind, setStatusKind] = useState<'active' | 'planned' | 'inactive'>(
    initial ? (initial.planned ? 'planned' : initial.status === 1 ? 'inactive' : 'active') : 'planned'
  );
  const [displayOrder, setDisplayOrder] = useState(initial?.displayOrder ?? 0);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');

  const planned = statusKind === 'planned';

  const onNameChange = (v: string) => {
    setName(v);
    if (!slugTouched) setSlug(slugify(v));
  };

  const handleSubmit = async () => {
    if (!name.trim()) { setError('Page name is required'); return; }
    if (!slug.trim()) { setError('Page slug is required'); return; }
    setSaving(true); setError('');
    try {
      // Planned pages grant no nav/route; Active pages with a route appear in nav.
      const payload = {
        name: name.trim(),
        route: planned ? null : route.trim() || null,
        nav: !planned && !!route.trim(),
        planned,
        status: statusKind === 'inactive' ? 1 : 0,
        displayOrder,
      };
      if (isEdit) {
        await api.put(`/admin/pages/${initial!.id}`, payload);
      } else {
        await api.post(`/admin/modules/${moduleId}/pages`, { ...payload, key: slug.trim() });
      }
      await onSaved();
    } catch (e: any) {
      setError(e.response?.data?.message || 'Failed to save page — you may not have permission (403).');
    }
    setSaving(false);
  };

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center p-4">
      <div className="fixed inset-0 bg-black/50" onClick={onClose} />
      <div className="relative bg-white rounded-xl shadow-2xl w-full max-w-lg">
        <div className="flex items-center justify-between px-6 py-4 border-b border-gray-200">
          <div className="flex items-center gap-2">
            <Layers className="w-5 h-5 text-blue-600" />
            <h2 className="text-lg font-semibold text-gray-900">{isEdit ? 'Edit Page/Form' : 'Add Page/Form'}</h2>
            <span className="text-xs text-gray-400 bg-gray-100 rounded-md px-2 py-0.5">in {moduleName}</span>
          </div>
          <button onClick={onClose} className="p-1 hover:bg-gray-100 rounded-lg"><X className="w-5 h-5 text-gray-500" /></button>
        </div>
        <div className="px-6 py-4 space-y-4">
          {error && <div className="p-3 bg-red-50 border border-red-200 rounded-lg text-sm text-red-700">{error}</div>}
          {isCore && (
            <p className="text-xs text-amber-600 bg-amber-50 border border-amber-200 rounded-lg p-2">
              Core page — name, slug, route and flags are system-protected; you can only toggle status and order.
            </p>
          )}
          {isEdit && isRegistryPage && !isCore && (
            <p className="text-xs text-amber-600 bg-amber-50 border border-amber-200 rounded-lg p-2">
              This page exists in the canonical registry — its slug is the permission identity and cannot be changed here. Rename the display name instead.
            </p>
          )}
          <div className="grid grid-cols-2 gap-4">
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">Name *</label>
              <input value={name} onChange={e => onNameChange(e.target.value)} disabled={isCore}
                className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:ring-2 focus:ring-blue-500 focus:border-blue-500 disabled:bg-gray-50" />
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">Slug *</label>
              <input value={slug} onChange={e => { setSlug(slugify(e.target.value)); setSlugTouched(true); }}
                disabled={isCore || isRegistryPage}
                placeholder="auto-generated from name"
                className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:ring-2 focus:ring-blue-500 focus:border-blue-500 disabled:bg-gray-50" />
            </div>
          </div>
          <p className="text-xs text-gray-400 -mt-2">Slug is lowercase kebab-case and globally unique — it becomes the permission prefix (e.g. <code className="bg-gray-100 px-1 rounded">{slug || 'page'}.view</code>).</p>
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">Route Path</label>
            <input value={route} onChange={e => setRoute(e.target.value)} placeholder="e.g. /trip-logs" disabled={planned || isCore}
              className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:ring-2 focus:ring-blue-500 focus:border-blue-500 disabled:bg-gray-50" />
            {planned && <p className="text-xs text-gray-400 mt-1">Planned pages grant no route or nav access until activated.</p>}
          </div>
          <div className="grid grid-cols-2 gap-4">
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">Status</label>
              <select value={statusKind} onChange={e => setStatusKind(e.target.value as 'active' | 'planned' | 'inactive')}
                className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:ring-2 focus:ring-blue-500 focus:border-blue-500">
                <option value="active">Active</option>
                <option value="planned">Planned</option>
                <option value="inactive">Inactive</option>
              </select>
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">Display Order</label>
              <input type="number" value={displayOrder} onChange={e => setDisplayOrder(Number(e.target.value))}
                className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:ring-2 focus:ring-blue-500 focus:border-blue-500" />
            </div>
          </div>
          <p className="text-xs text-gray-400 -mt-2">Tip: use drag handles on the list to reorder instead of typing Display Order.</p>
        </div>
        <div className="flex items-center justify-end gap-3 px-6 py-4 border-t border-gray-200">
          <button onClick={onClose} className="px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-100 rounded-lg">Cancel</button>
          <button onClick={handleSubmit} disabled={saving}
            className="px-5 py-2 bg-blue-600 text-white text-sm font-medium rounded-lg hover:bg-blue-700 disabled:bg-blue-400">
            {saving ? 'Saving...' : isEdit ? 'Update Page' : 'Create Page'}
          </button>
        </div>
      </div>
    </div>
  );
}
import { useEffect, useState } from 'react';
import api from '../lib/api';
import { Layers } from 'lucide-react';
import { usePermissions } from '../hooks/usePermissions';

interface RegistryPage { key: string; label: string; planned: boolean; nav: boolean; route?: string; }
interface ModuleRow {
  id: string; code: string; name: string; description?: string; icon?: string;
  isCore: boolean; status: number; displayOrder: number;
  pageCount: number; plannedPageCount: number;
  pages: RegistryPage[];
}

export default function Modules() {
  const { can } = usePermissions();
  const canView = can('module.view');
  const [modules, setModules] = useState<ModuleRow[]>([]);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    api.get('/modules?pageSize=50').then(r => {
      setModules(r.data.data?.items || []);
    }).catch(() => {}).finally(() => setLoading(false));
  }, []);

  if (!canView) return null;
  if (loading) return <div className="flex items-center justify-center h-64"><div className="animate-spin rounded-full h-8 w-8 border-b-2 border-blue-600" /></div>;

  const statusBadge = (s: number) => s === 0
    ? <span className="inline-flex px-2 py-0.5 rounded-full text-xs font-medium bg-green-100 text-green-700">Active</span>
    : <span className="inline-flex px-2 py-0.5 rounded-full text-xs font-medium bg-gray-100 text-gray-700">Inactive</span>;

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <div>
          <h2 className="text-xl font-bold text-gray-900">Module Management</h2>
          <p className="text-sm text-gray-500 mt-0.5">Top-level modules group the product's pages. A company can use a module only when its package includes it.</p>
        </div>
        <span className="text-sm text-gray-500">{modules.length} modules</span>
      </div>

      <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
        {modules.length === 0 ? (
          <div className="col-span-full text-center py-12 text-gray-400">No modules found</div>
        ) : (
          modules.map(m => {
            return (
              <div key={m.id} className="bg-white rounded-xl border border-gray-200 p-5">
                <div className="flex items-center gap-4 mb-3">
                  <div className="w-10 h-10 bg-blue-50 rounded-lg flex items-center justify-center flex-shrink-0">
                    <Layers className="w-5 h-5 text-blue-600" />
                  </div>
                  <div className="flex-1 min-w-0">
                    <div className="flex items-center gap-2">
                      <span className="text-sm font-semibold text-gray-900">{m.name}</span>
                      {m.isCore && <span className="px-1.5 py-0.5 bg-purple-100 text-purple-700 text-xs font-medium rounded">Core</span>}
                      {statusBadge(m.status)}
                    </div>
                    <p className="text-xs text-gray-500 mt-0.5 line-clamp-2">{m.description || m.code}</p>
                  </div>
                  <div className="text-right flex-shrink-0">
                    <div className="text-lg font-bold text-gray-900">{m.pageCount}</div>
                    <div className="text-xs text-gray-400">pages</div>
                  </div>
                </div>

                {/* Pages inside this module */}
                {m.pages?.length > 0 && (
                  <div className="flex flex-wrap gap-1.5 mt-2">
                    {m.pages.map(p => (
                      <span key={p.key} title={p.route ? `Route: ${p.route}` : 'No standalone page yet'}
                        className="inline-flex items-center gap-1 px-2 py-1 rounded-md text-xs font-medium bg-gray-50 border border-gray-200 text-gray-600">
                        {p.label}
                        {p.planned && <span className="px-1 py-0.5 rounded bg-amber-100 text-amber-700 text-[10px] font-medium">Planned</span>}
                      </span>
                    ))}
                  </div>
                )}
              </div>
            );
          })
        )}
      </div>

      {/* Legend for planned pages */}
      {modules.some(m => m.plannedPageCount > 0) && (
        <p className="text-xs text-gray-400">
          <span className="inline-flex px-1.5 py-0.5 rounded bg-amber-100 text-amber-700 text-[10px] font-medium mr-1">Planned</span>
          modules with a <em>Planned</em> badge are registered for future pages — they grant no nav or route today and can be excluded from packages.
        </p>
      )}
    </div>
  );
}

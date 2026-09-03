import { useEffect, useState } from 'react';
import api from '../lib/api';
import { Package, ChevronRight, ToggleLeft, ToggleRight } from 'lucide-react';
import { usePermissions } from '../hooks/usePermissions';

interface Module {
  id: string; code: string; name: string; description?: string; icon?: string;
  isCore: boolean; moduleVersion: string; featureCount: number;
  status: number; displayOrder: number;
}

interface Feature {
  id: string; code: string; name: string; description?: string;
  isEnabledByDefault: boolean; status: number; displayOrder: number;
}

export default function Modules() {
  const { can } = usePermissions();
  const canView = can('configuration.view');
  const canUpdate = can('configuration.update');
  const [modules, setModules] = useState<Module[]>([]);
  const [loading, setLoading] = useState(true);
  const [expanded, setExpanded] = useState<string | null>(null);
  const [features, setFeatures] = useState<Feature[]>([]);
  const [featuresLoading, setFeaturesLoading] = useState(false);

  useEffect(() => {
    api.get('/modules?pageSize=50').then(r => {
      setModules(r.data.data?.items || []);
    }).catch(() => {}).finally(() => setLoading(false));
  }, []);

  const toggleExpand = async (moduleId: string) => {
    if (expanded === moduleId) { setExpanded(null); return; }
    setExpanded(moduleId);
    setFeaturesLoading(true);
    try {
      const res = await api.get(`/modules/${moduleId}/features`);
      setFeatures(res.data.data || []);
    } catch { setFeatures([]); }
    setFeaturesLoading(false);
  };

  const statusBadge = (s: number) => s === 0
    ? <span className="inline-flex px-2 py-0.5 rounded-full text-xs font-medium bg-green-100 text-green-700">Active</span>
    : <span className="inline-flex px-2 py-0.5 rounded-full text-xs font-medium bg-gray-100 text-gray-700">Inactive</span>;

  if (loading) return <div className="flex items-center justify-center h-64"><div className="animate-spin rounded-full h-8 w-8 border-b-2 border-blue-600" /></div>;

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <h2 className="text-xl font-bold text-gray-900">Module Management</h2>
        <span className="text-sm text-gray-500">{modules.length} modules</span>
      </div>

      <div className="bg-white rounded-xl border border-gray-200 divide-y divide-gray-100">
        {modules.length === 0 ? (
          <div className="text-center py-12 text-gray-400">No modules found</div>
        ) : (
          modules.map(m => (
            <div key={m.id}>
              <div className="flex items-center gap-4 px-5 py-4 hover:bg-gray-50 transition-colors cursor-pointer"
                onClick={() => toggleExpand(m.id)}>
                <div className="w-10 h-10 bg-blue-50 rounded-lg flex items-center justify-center flex-shrink-0">
                  <Package className="w-5 h-5 text-blue-600" />
                </div>
                <div className="flex-1 min-w-0">
                  <div className="flex items-center gap-2">
                    <span className="text-sm font-semibold text-gray-900">{m.name}</span>
                    {m.isCore && <span className="px-1.5 py-0.5 bg-purple-100 text-purple-700 text-xs font-medium rounded">Core</span>}
                    {statusBadge(m.status)}
                  </div>
                  <p className="text-xs text-gray-500 mt-0.5 truncate">{m.description || m.code}</p>
                </div>
                <div className="text-right flex-shrink-0">
                  <div className="text-xs text-gray-500">v{m.moduleVersion}</div>
                  <div className="text-xs text-gray-400">{m.featureCount} features</div>
                </div>
                <ChevronRight className={`w-4 h-4 text-gray-400 transition-transform ${expanded === m.id ? 'rotate-90' : ''}`} />
              </div>

              {expanded === m.id && (
                <div className="bg-gray-50 border-t border-gray-100 px-5 py-3">
                  {featuresLoading ? (
                    <div className="text-sm text-gray-400 py-2">Loading features...</div>
                  ) : features.length === 0 ? (
                    <div className="text-sm text-gray-400 py-2">No features in this module</div>
                  ) : (
                    <div className="space-y-1">
                      <div className="text-xs font-medium text-gray-500 uppercase mb-2">Features</div>
                      {features.map(f => (
                        <div key={f.id} className="flex items-center gap-3 px-3 py-2 bg-white rounded-lg border border-gray-200">
                          <div className="flex-1">
                            <span className="text-sm font-medium text-gray-800">{f.name}</span>
                            <span className="text-xs text-gray-400 ml-2">{f.code}</span>
                          </div>
                          {f.isEnabledByDefault
                            ? <ToggleRight className="w-5 h-5 text-green-500 flex-shrink-0" />
                            : <ToggleLeft className="w-5 h-5 text-gray-400 flex-shrink-0" />
                          }
                        </div>
                      ))}
                    </div>
                  )}
                </div>
              )}
            </div>
          ))
        )}
      </div>
    </div>
  );
}

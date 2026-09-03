import { useEffect, useState } from 'react';
import api from '../lib/api';
import { Globe, DollarSign } from 'lucide-react';
import { usePermissions } from '../hooks/usePermissions';

interface Lang { id: string; code: string; name: string; nativeName: string; isRightToLeft: boolean; isDefault: boolean; status: number; displayOrder: number; }
interface Curr { id: string; code: string; name: string; symbol: string; decimalPlaces: number; isDefault: boolean; status: number; displayOrder: number; }
interface PagedData<T> { items: T[]; totalCount: number; page: number; pageSize: number; totalPages: number; }

export default function Localization() {
  const { can } = usePermissions();
  const canView = can('configuration.view');
  const [tab, setTab] = useState<'languages' | 'currencies'>('languages');
  const [langs, setLangs] = useState<PagedData<Lang> | null>(null);
  const [currencies, setCurrencies] = useState<PagedData<Curr> | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    setLoading(true);
    const fetch = tab === 'languages'
      ? api.get('/languages?pageSize=50').then(r => { setLangs(r.data.data); setLoading(false); })
      : api.get('/currencies?pageSize=50').then(r => { setCurrencies(r.data.data); setLoading(false); });
    fetch.catch(() => setLoading(false));
  }, [tab]);

  const statusBadge = (s: number) => s === 0
    ? <span className="inline-flex px-2 py-0.5 rounded-full text-xs font-medium bg-green-100 text-green-700">Active</span>
    : <span className="inline-flex px-2 py-0.5 rounded-full text-xs font-medium bg-gray-100 text-gray-700">Inactive</span>;

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <h2 className="text-xl font-bold text-gray-900">Localization</h2>
      </div>

      <div className="flex gap-1 bg-gray-100 p-1 rounded-lg w-fit">
        <button onClick={() => setTab('languages')}
          className={`flex items-center gap-2 px-4 py-2 rounded-md text-sm font-medium transition-colors ${tab === 'languages' ? 'bg-white shadow text-gray-900' : 'text-gray-600 hover:text-gray-900'}`}>
          <Globe className="w-4 h-4" /> Languages
        </button>
        <button onClick={() => setTab('currencies')}
          className={`flex items-center gap-2 px-4 py-2 rounded-md text-sm font-medium transition-colors ${tab === 'currencies' ? 'bg-white shadow text-gray-900' : 'text-gray-600 hover:text-gray-900'}`}>
          <DollarSign className="w-4 h-4" /> Currencies
        </button>
      </div>

      <div className="bg-white rounded-xl border border-gray-200 overflow-hidden">
        <div className="overflow-x-auto">
          {tab === 'languages' ? (
            <table className="w-full">
              <thead className="bg-gray-50 border-b border-gray-200">
                <tr>
                  <th className="text-left px-4 py-3 text-xs font-medium text-gray-500 uppercase">Code</th>
                  <th className="text-left px-4 py-3 text-xs font-medium text-gray-500 uppercase">Name</th>
                  <th className="text-left px-4 py-3 text-xs font-medium text-gray-500 uppercase">Native Name</th>
                  <th className="text-left px-4 py-3 text-xs font-medium text-gray-500 uppercase">RTL</th>
                  <th className="text-left px-4 py-3 text-xs font-medium text-gray-500 uppercase">Default</th>
                  <th className="text-left px-4 py-3 text-xs font-medium text-gray-500 uppercase">Status</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-gray-100">
                {loading ? (
                  <tr><td colSpan={6} className="text-center py-12 text-gray-400">Loading...</td></tr>
                ) : langs?.items?.length === 0 ? (
                  <tr><td colSpan={6} className="text-center py-12 text-gray-400">No languages found</td></tr>
                ) : (
                  langs?.items?.map(l => (
                    <tr key={l.id} className="hover:bg-gray-50 transition-colors">
                      <td className="px-4 py-3 text-sm font-mono font-medium text-gray-900">{l.code}</td>
                      <td className="px-4 py-3 text-sm text-gray-900">{l.name}</td>
                      <td className="px-4 py-3 text-sm text-gray-600">{l.nativeName}</td>
                      <td className="px-4 py-3 text-sm text-gray-600">{l.isRightToLeft ? 'Yes' : 'No'}</td>
                      <td className="px-4 py-3">
                        {l.isDefault && <span className="inline-flex px-2 py-0.5 rounded-full text-xs font-medium bg-blue-100 text-blue-700">Default</span>}
                      </td>
                      <td className="px-4 py-3">{statusBadge(l.status)}</td>
                    </tr>
                  ))
                )}
              </tbody>
            </table>
          ) : (
            <table className="w-full">
              <thead className="bg-gray-50 border-b border-gray-200">
                <tr>
                  <th className="text-left px-4 py-3 text-xs font-medium text-gray-500 uppercase">Code</th>
                  <th className="text-left px-4 py-3 text-xs font-medium text-gray-500 uppercase">Name</th>
                  <th className="text-left px-4 py-3 text-xs font-medium text-gray-500 uppercase">Symbol</th>
                  <th className="text-left px-4 py-3 text-xs font-medium text-gray-500 uppercase">Decimals</th>
                  <th className="text-left px-4 py-3 text-xs font-medium text-gray-500 uppercase">Default</th>
                  <th className="text-left px-4 py-3 text-xs font-medium text-gray-500 uppercase">Status</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-gray-100">
                {loading ? (
                  <tr><td colSpan={6} className="text-center py-12 text-gray-400">Loading...</td></tr>
                ) : currencies?.items?.length === 0 ? (
                  <tr><td colSpan={6} className="text-center py-12 text-gray-400">No currencies found</td></tr>
                ) : (
                  currencies?.items?.map(c => (
                    <tr key={c.id} className="hover:bg-gray-50 transition-colors">
                      <td className="px-4 py-3 text-sm font-mono font-medium text-gray-900">{c.code}</td>
                      <td className="px-4 py-3 text-sm text-gray-900">{c.name}</td>
                      <td className="px-4 py-3 text-sm text-gray-900 text-lg">{c.symbol}</td>
                      <td className="px-4 py-3 text-sm text-gray-600">{c.decimalPlaces}</td>
                      <td className="px-4 py-3">
                        {c.isDefault && <span className="inline-flex px-2 py-0.5 rounded-full text-xs font-medium bg-blue-100 text-blue-700">Default</span>}
                      </td>
                      <td className="px-4 py-3">{statusBadge(c.status)}</td>
                    </tr>
                  ))
                )}
              </tbody>
            </table>
          )}
        </div>
        {((tab === 'languages' && langs) || (tab === 'currencies' && currencies)) && (
          <div className="px-4 py-3 border-t border-gray-200">
            <span className="text-sm text-gray-500">
              Showing {tab === 'languages' ? langs!.items.length : currencies!.items.length} of{' '}
              {tab === 'languages' ? langs!.totalCount : currencies!.totalCount} {tab}
            </span>
          </div>
        )}
      </div>
    </div>
  );
}

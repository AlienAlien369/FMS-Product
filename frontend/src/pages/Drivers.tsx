import { useEffect, useState } from 'react';
import api from '../lib/api';
import type { Driver, PagedResult } from '../lib/api';
import { Search, Plus, Edit, Trash2, ChevronLeft, ChevronRight } from 'lucide-react';

export default function Drivers() {
  const [data, setData] = useState<PagedResult<Driver> | null>(null);
  const [search, setSearch] = useState('');
  const [page, setPage] = useState(1);
  const [loading, setLoading] = useState(true);

  const fetchDrivers = async () => {
    setLoading(true);
    try {
      const res = await api.get(`/drivers?page=${page}&pageSize=10&search=${search}`);
      setData(res.data.data);
    } catch (err) { console.error(err); }
    setLoading(false);
  };

  useEffect(() => { fetchDrivers(); }, [page, search]);

  const statusMap: Record<number, { label: string; color: string }> = {
    0: { label: 'Active', color: 'bg-green-100 text-green-700' },
    1: { label: 'Inactive', color: 'bg-gray-100 text-gray-700' },
    2: { label: 'On Trip', color: 'bg-blue-100 text-blue-700' },
    3: { label: 'Off Duty', color: 'bg-yellow-100 text-yellow-700' },
    4: { label: 'Suspended', color: 'bg-red-100 text-red-700' },
  };

  return (
    <div className="space-y-4">
      <div className="flex flex-col sm:flex-row items-start sm:items-center justify-between gap-3">
        <div className="relative flex-1 max-w-md">
          <Search className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-gray-400" />
          <input type="text" value={search} onChange={e => { setSearch(e.target.value); setPage(1); }}
            className="w-full pl-9 pr-4 py-2 border border-gray-300 rounded-lg text-sm focus:ring-2 focus:ring-blue-500"
            placeholder="Search drivers..." />
        </div>
        <button className="flex items-center gap-2 px-4 py-2 bg-blue-600 text-white rounded-lg text-sm hover:bg-blue-700">
          <Plus className="w-4 h-4" /> Add Driver
        </button>
      </div>

      <div className="bg-white rounded-xl border border-gray-200 overflow-hidden">
        <div className="overflow-x-auto">
          <table className="w-full">
            <thead className="bg-gray-50 border-b border-gray-200">
              <tr>
                <th className="text-left px-4 py-3 text-xs font-medium text-gray-500 uppercase">Name</th>
                <th className="text-left px-4 py-3 text-xs font-medium text-gray-500 uppercase">Employee ID</th>
                <th className="text-left px-4 py-3 text-xs font-medium text-gray-500 uppercase">Phone</th>
                <th className="text-left px-4 py-3 text-xs font-medium text-gray-500 uppercase">License</th>
                <th className="text-left px-4 py-3 text-xs font-medium text-gray-500 uppercase">Safety Score</th>
                <th className="text-left px-4 py-3 text-xs font-medium text-gray-500 uppercase">Status</th>
                <th className="text-left px-4 py-3 text-xs font-medium text-gray-500 uppercase">Actions</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-gray-100">
              {loading ? (
                <tr><td colSpan={7} className="text-center py-12 text-gray-400">Loading...</td></tr>
              ) : data?.items?.length === 0 ? (
                <tr><td colSpan={7} className="text-center py-12 text-gray-400">No drivers found</td></tr>
              ) : (
                data?.items?.map(d => (
                  <tr key={d.id} className="hover:bg-gray-50 transition-colors">
                    <td className="px-4 py-3 text-sm font-medium text-gray-900">{d.fullName}</td>
                    <td className="px-4 py-3 text-sm text-gray-600">{d.employeeId}</td>
                    <td className="px-4 py-3 text-sm text-gray-600">{d.phoneNumber || '—'}</td>
                    <td className="px-4 py-3 text-sm text-gray-600">{d.licenseNumber || '—'}</td>
                    <td className="px-4 py-3 text-sm text-gray-600">{d.safetyScore != null ? `${d.safetyScore}%` : '—'}</td>
                    <td className="px-4 py-3">
                      <span className={`inline-flex px-2 py-0.5 rounded-full text-xs font-medium ${statusMap[d.status]?.color}`}>
                        {statusMap[d.status]?.label}
                      </span>
                    </td>
                    <td className="px-4 py-3">
                      <div className="flex items-center gap-1">
                        <button className="p-1 hover:bg-gray-100 rounded"><Edit className="w-4 h-4 text-gray-500" /></button>
                        <button className="p-1 hover:bg-gray-100 rounded"><Trash2 className="w-4 h-4 text-red-500" /></button>
                      </div>
                    </td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>
        {data && (
          <div className="flex items-center justify-between px-4 py-3 border-t border-gray-200">
            <span className="text-sm text-gray-500">Showing {data.items.length} of {data.totalCount} drivers</span>
            <div className="flex items-center gap-2">
              <button disabled={!data.hasPrevious} onClick={() => setPage(p => p - 1)} className="p-1 rounded hover:bg-gray-100 disabled:opacity-30"><ChevronLeft className="w-4 h-4" /></button>
              <span className="text-sm text-gray-600">Page {data.page} of {data.totalPages}</span>
              <button disabled={!data.hasNext} onClick={() => setPage(p => p + 1)} className="p-1 rounded hover:bg-gray-100 disabled:opacity-30"><ChevronRight className="w-4 h-4" /></button>
            </div>
          </div>
        )}
      </div>
    </div>
  );
}

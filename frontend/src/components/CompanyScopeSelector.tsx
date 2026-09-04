import { useEffect, useRef, useState } from 'react';
import { Building2, Check, ChevronDown, Search, X } from 'lucide-react';
import { useCompanyScope } from '../contexts/CompanyScopeContext';
import type { CompanyScopeValue } from '../contexts/CompanyScopeContext';

/**
 * Header company-scope selector — rendered only for cross-tenant users
 * (SuperAdmin). Multi-select searchable dropdown with an "All Companies" option.
 * State lives in CompanyScopeContext (session-persisted); every API call carries
 * the selection as the X-Company-Scope header.
 */
export default function CompanyScopeSelector() {
  const { isCrossTenant, scope, companies, setScope, scopeLabel, companyName } = useCompanyScope();
  const [open, setOpen] = useState(false);
  const [query, setQuery] = useState('');
  const wrapRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!open) return;
    const onDoc = (e: MouseEvent) => {
      if (wrapRef.current && !wrapRef.current.contains(e.target as Node)) setOpen(false);
    };
    document.addEventListener('mousedown', onDoc);
    return () => document.removeEventListener('mousedown', onDoc);
  }, [open]);

  if (!isCrossTenant) return null;

  const selectedIds = Array.isArray(scope) ? new Set(scope) : new Set<string>();
  const isAll = scope === null || scope === 'ALL';
  const filtered = companies.filter(c => c.name.toLowerCase().includes(query.trim().toLowerCase()));

  const toggleAll = () => { setScope(isAll ? [] : 'ALL'); setQuery(''); };
  const toggleCompany = (id: string) => {
    if (isAll) {
      // Leaving "All Companies" → start from everything except the one just toggled off.
      setScope(companies.map(c => c.id).filter(cid => cid !== id));
    } else {
      const next = new Set(selectedIds);
      if (next.has(id)) next.delete(id); else next.add(id);
      setScope(next.size === companies.length ? 'ALL' : Array.from(next));
    }
    setQuery('');
  };
  const clearSelection = () => { setScope('ALL'); setQuery(''); };

  return (
    <div className="relative" ref={wrapRef}>
      <button
        onClick={() => setOpen(o => !o)}
        className="flex items-center gap-1.5 px-2.5 py-1.5 rounded-lg border border-gray-200 text-xs font-medium text-gray-700 hover:bg-gray-50 transition-colors max-w-48"
        title="View data for which companies?"
      >
        <Building2 className="w-3.5 h-3.5 text-gray-500" />
        <span className="truncate">{scopeLabel}</span>
        <ChevronDown className={`w-3.5 h-3.5 text-gray-400 transition-transform ${open ? 'rotate-180' : ''}`} />
      </button>

      {open && (
        <div className="absolute right-0 top-full mt-1 w-72 bg-white border border-gray-200 rounded-xl shadow-lg z-50 overflow-hidden">
          <div className="px-3 py-2 border-b border-gray-100 flex items-center justify-between">
            <span className="text-xs font-semibold text-gray-700">Company scope</span>
            {!isAll && scope && Array.isArray(scope) && scope.length > 0 && (
              <button onClick={clearSelection} className="text-[11px] text-blue-600 hover:underline flex items-center gap-0.5">
                <X className="w-3 h-3" /> Clear
              </button>
            )}
          </div>

          <div className="px-3 py-2 border-b border-gray-100">
            <label className="flex items-center gap-2 cursor-pointer">
              <input type="checkbox" checked={isAll} onChange={toggleAll} className="rounded border-gray-300 text-blue-600 focus:ring-blue-500" />
              <span className="text-sm font-medium text-gray-800">All Companies</span>
              {isAll && <Check className="w-3.5 h-3.5 text-blue-600 ml-auto" />}
            </label>
          </div>

          <div className="px-3 py-2 border-b border-gray-100">
            <div className="relative">
              <Search className="absolute left-2 top-1/2 -translate-y-1/2 w-3.5 h-3.5 text-gray-400" />
              <input
                value={query}
                onChange={e => setQuery(e.target.value)}
                placeholder="Search companies…"
                className="w-full pl-7 pr-2 py-1.5 text-xs bg-gray-50 border border-gray-200 rounded-md focus:outline-none focus:ring-1 focus:ring-blue-500"
              />
            </div>
          </div>

          <div className="max-h-56 overflow-y-auto py-1">
            {filtered.length === 0 && (
              <div className="px-4 py-6 text-center text-xs text-gray-400">No companies match</div>
            )}
            {filtered.map(c => (
              <label key={c.id} className="flex items-center gap-2 px-3 py-2 hover:bg-gray-50 cursor-pointer">
                <input
                  type="checkbox"
                  checked={isAll || selectedIds.has(c.id)}
                  disabled={isAll}
                  onChange={() => toggleCompany(c.id)}
                  className="rounded border-gray-300 text-blue-600 focus:ring-blue-500 disabled:opacity-40"
                />
                <span className="text-sm text-gray-700 truncate">{c.name}</span>
              </label>
            ))}
          </div>

          <div className="px-3 py-2 border-t border-gray-100 text-[11px] text-gray-400">
            {isAll
              ? 'Showing every company you can access.'
              : (Array.isArray(scope) && scope.length > 0)
                ? `Showing ${scope.length} of ${companies.length} company${scope.length === 1 ? '' : 'ies'} (${scope.map(id => companyName(id)).filter(Boolean).join(', ')}).`
                : 'Nothing selected — choose companies or pick All.'}
          </div>
        </div>
      )}
    </div>
  );
}

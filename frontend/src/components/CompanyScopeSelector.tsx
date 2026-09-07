import { useState } from 'react';
import { Building2, Check, ChevronDown, Search, X } from 'lucide-react';
import { useCompanyScope } from '../contexts/CompanyScopeContext';
import type { CompanyScopeValue } from '../contexts/CompanyScopeContext';
import { SheetDropdown } from './ui';

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
    <SheetDropdown
      open={open}
      onOpenChange={setOpen}
      panelClass="sm:w-72"
      trigger={
        <button
          onClick={() => setOpen(o => !o)}
          className="flex items-center gap-1.5 px-2.5 py-1.5 rounded-lg border border-gray-200 text-xs font-medium text-gray-700 hover:bg-gray-50 transition-colors max-w-48 min-h-[44px]"
          title="View data for which companies?"
        >
          <Building2 className="w-4 h-4 text-gray-500" />
          <span className="truncate hidden sm:inline">{scopeLabel}</span>
          <ChevronDown className={`w-3.5 h-3.5 text-gray-400 transition-transform ${open ? 'rotate-180' : ''} hidden sm:block`} />
        </button>
      }
      header={
        <>
          <div className="sm:hidden px-3 py-2 flex items-center justify-between border-b border-gray-100 shrink-0">
            <span className="text-xs font-semibold text-gray-700">Company scope</span>
            <button onClick={() => setOpen(false)} aria-label="Close" className="p-2.5 text-gray-500 min-w-[44px] min-h-[44px]"><X className="w-5 h-5" /></button>
          </div>
          <div className="hidden sm:flex px-3 py-2 border-b border-gray-100 items-center justify-between">
            <span className="text-xs font-semibold text-gray-700">Company scope</span>
            {!isAll && scope && Array.isArray(scope) && scope.length > 0 && (
              <button onClick={clearSelection} className="text-[11px] text-blue-600 hover:underline flex items-center gap-0.5">
                <X className="w-3 h-3" /> Clear
              </button>
            )}
          </div>
        </>
      }
    >
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

      <div className="max-h-56 sm:max-h-[40vh] overflow-y-auto py-1">
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

      <div className="px-3 py-2 border-t border-gray-100 text-[11px] text-gray-400 shrink-0">
        {isAll
          ? 'Showing every company you can access.'
          : (Array.isArray(scope) && scope.length > 0)
            ? `Showing ${scope.length} of ${companies.length} company${scope.length === 1 ? '' : 'ies'} (${scope.map(id => companyName(id)).filter(Boolean).join(', ')}).`
            : 'Nothing selected — choose companies or pick All.'}
      </div>
    </SheetDropdown>
  );
}

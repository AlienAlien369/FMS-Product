import { createContext, useContext, useEffect, useState, useCallback, useMemo } from 'react';
import type { ReactNode } from 'react';
import api from '../lib/api';
import { useAuth } from './AuthContext';

/**
 * Company scope for cross-company views. Stateless server-side: the selector
 * state lives ONLY in frontend app state + sessionStorage, and is sent as the
 * `X-Company-Scope` header on every API call. The backend never stores a
 * "current scope" — it intersects the header with the user's permitted set.
 *
 * Values:
 *  - null        → no header. Normal users see only their own company;
 *                  SuperAdmin (cross-tenant) sees ALL companies.
 *  - 'ALL'       → "every company I may access" (explicit All Companies).
 *  - string[]    → the specific company ids to view.
 */
export type CompanyScopeValue = 'ALL' | string[] | null;

const STORAGE_KEY = 'companyScope';

interface ScopeCompany { id: string; name: string; }

interface CompanyScopeContextType {
  scope: CompanyScopeValue;
  /** All companies the cross-tenant user may select (only fetched for SuperAdmin). */
  companies: ScopeCompany[];
  /** Bumped on every scope change — pages use it to refetch their data. */
  version: number;
  /** True when the user may act across companies (SuperAdmin today). */
  isCrossTenant: boolean;
  /** True when the active view may span more than one company (drives row labels). */
  isMultiCompany: boolean;
  setScope: (v: CompanyScopeValue) => void;
  resetScope: () => void;
  companyName: (id: string) => string | undefined;
  /** Human label for the selector button. */
  scopeLabel: string;
}

const CompanyScopeContext = createContext<CompanyScopeContextType>({} as CompanyScopeContextType);

export function CompanyScopeProvider({ children }: { children: ReactNode }) {
  const { user, isLoading } = useAuth();
  const isSuperAdmin = user?.roles?.includes('SuperAdmin') ?? false;
  const isCrossTenant = isSuperAdmin;
  // User identity for this login session ('' while auth is still booting so we
  // never touch sessionStorage before we know who is signed in).
  const userKey = isLoading ? '' : (user?.id ?? 'anon');

  // sessionStorage persistence mirrors the interceptor read path: the axios
  // interceptor reads sessionStorage directly, so no import cycle.
  const [scope, setScopeState] = useState<CompanyScopeValue>(null);
  const [version, setVersion] = useState(0);
  const [companies, setCompanies] = useState<ScopeCompany[]>([]);

  // Per login session: reset to default when the signed-in user changes; while
  // auth is still booting (userKey === '') do nothing — otherwise this effect
  // would wipe a saved scope before the real user's cross-tenant status is known.
  useEffect(() => {
    if (!userKey) return;
    if (!isCrossTenant) {
      // Normal users never send a scope header — wipe any stale value.
      sessionStorage.removeItem(STORAGE_KEY);
      setScopeState(null);
      setCompanies([]);
      return;
    }
    const saved = sessionStorage.getItem(STORAGE_KEY);
    if (saved === 'ALL') {
      setScopeState('ALL');
    } else if (saved) {
      try {
        const ids = JSON.parse(saved);
        if (Array.isArray(ids) && ids.length > 0) setScopeState(ids);
      } catch { /* corrupt — fall back to ALL */ }
    }
    setVersion(v => v + 1);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [userKey, isCrossTenant]);

  // Cross-tenant user: load the selectable company list once per session.
  useEffect(() => {
    if (!isCrossTenant || !userKey) return;
    let alive = true;
    (async () => {
      try {
        const res = await api.get('/admin/companies?pageSize=100');
        const items = res.data?.data?.items;
        if (!alive || !Array.isArray(items)) return;
        setCompanies(items
          .filter((c: any) => c.status === 0)
          .map((c: any) => ({ id: String(c.id), name: String(c.name || '—') })));
      } catch { /* selector falls back to empty list */ }
    })();
    return () => { alive = false; };
  }, [isCrossTenant, userKey]);

  const setScope = useCallback((v: CompanyScopeValue) => {
    if (!isCrossTenant) return; // never let a non-cross-tenant user widen scope
    setScopeState(v);
    setVersion(x => x + 1);
    if (v === null) sessionStorage.removeItem(STORAGE_KEY);
    else sessionStorage.setItem(STORAGE_KEY, v === 'ALL' ? 'ALL' : JSON.stringify(v));
  }, [isCrossTenant]);

  const resetScope = useCallback(() => setScope(null), [setScope]);

  const companyName = useCallback((id: string) => companies.find(c => c.id === id)?.name, [companies]);

  const isMultiCompany = useMemo(
    () => isCrossTenant && (scope === 'ALL' || (Array.isArray(scope) && scope.length > 1)),
    [isCrossTenant, scope]);

  const scopeLabel = useMemo(() => {
    if (!isCrossTenant) return 'My company';
    if (scope === null || scope === 'ALL') return 'All Companies';
    if (Array.isArray(scope)) {
      if (scope.length === 0) return 'Nothing selected';
      if (scope.length === 1) return companyName(scope[0]) ?? '1 company';
      return `${scope.length} companies`;
    }
    return 'My company';
  }, [isCrossTenant, scope, companyName]);

  return (
    <CompanyScopeContext.Provider value={{
      scope, companies, version, isCrossTenant, isMultiCompany, setScope, resetScope, companyName, scopeLabel,
    }}>
      {children}
    </CompanyScopeContext.Provider>
  );
}

export const useCompanyScope = () => useContext(CompanyScopeContext);

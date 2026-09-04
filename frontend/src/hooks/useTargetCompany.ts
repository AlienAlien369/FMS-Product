import { useEffect, useMemo, useState } from 'react';
import { useCompanyScope } from '../contexts/CompanyScopeContext';

/**
 * Resolves the single company a create/edit form targets, mirroring the
 * backend rule (TargetCompanyResolver):
 *
 *  - Non-cross-tenant users: no company field — the server always forces their
 *    own tenant; any client-supplied companyId is ignored there.
 *  - SuperAdmin (cross-tenant): an explicit target company is required. When
 *    the view scope selects exactly one company, the field defaults to it; with
 *    "All Companies" / multiple companies the choice is forced (no guess).
 *    The target company — not the view scope — decides where the record lands.
 */
export function useTargetCompany() {
  const { scope, companies, isCrossTenant } = useCompanyScope();
  const [picked, setPicked] = useState<string>('');

  // The single company selected in the view scope, if any.
  const singleScoped = useMemo(
    () => (Array.isArray(scope) && scope.length === 1 ? String(scope[0]) : null),
    [scope],
  );

  // Default the field to the single scoped company; never persist a stale pick
  // once the view moves to ALL/multiple (that would look like a guess).
  useEffect(() => {
    if (singleScoped) setPicked(singleScoped);
  }, [singleScoped]);

  const effective = isCrossTenant ? picked || singleScoped || '' : '';
  const needsPick = isCrossTenant && !effective;
  const companyName = (id: string) => companies.find(c => c.id === id)?.name;

  return {
    isCrossTenant,
    needsPick,
    targetCompanyId: effective || null,
    setTargetCompanyId: setPicked,
    companies,
    companyName,
  };
}
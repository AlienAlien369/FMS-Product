import { Building2 } from 'lucide-react';
import type { useTargetCompany } from '../hooks/useTargetCompany';

const LABEL = 'block text-sm font-medium text-gray-700 mb-1';
const INPUT = 'w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:ring-2 focus:ring-blue-500 focus:border-blue-500';

/**
 * Target-company picker for create forms. Rendered only for cross-tenant
 * (SuperAdmin) users — company users get no picker and the server forces their
 * own tenant. Defaults to the single company selected in the view scope;
 * required (no guess) when the scope spans multiple/all companies.
 *
 * The caller passes its own `useTargetCompany()` result so the field and the
 * form's submit handler share ONE hook instance (two instances would render
 * the field with a pick the submit logic never sees).
 */
export default function TargetCompanyField({ hook, error }: { hook: ReturnType<typeof useTargetCompany>; error?: string }) {
  const { isCrossTenant, needsPick, targetCompanyId, setTargetCompanyId, companies, companyName } = hook;
  if (!isCrossTenant) return null;

  return (
    <div>
      <label className={LABEL}>Company {needsPick && <span className="text-red-500">*</span>}</label>
      <select
        value={targetCompanyId || ''}
        onChange={e => setTargetCompanyId(e.target.value)}
        className={INPUT + (error && needsPick ? ' border-red-400' : '')}
      >
        <option value="">{needsPick ? 'Select target company...' : ''}</option>
        {companies.map(c => <option key={c.id} value={c.id}>{c.name}</option>)}
        {targetCompanyId && !companyName(targetCompanyId) && <option value={targetCompanyId}>Selected company</option>}
        {companies.length === 0 && <option value="" disabled>Loading companies...</option>}
      </select>
      {targetCompanyId && !needsPick && (
        <p className="mt-1 text-xs text-gray-500 flex items-center gap-1">
          <Building2 className="w-3 h-3" /> This record will be created in {companyName(targetCompanyId) ?? 'the selected company'}.
        </p>
      )}
      {needsPick && <p className="mt-1 text-xs text-amber-600">Select the company this record belongs to — the view scope spans multiple companies.</p>}
    </div>
  );
}
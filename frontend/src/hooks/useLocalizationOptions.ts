import { useEffect, useState } from 'react';
import api from '../lib/api';

export interface LocaleOption {
  code: string;
  label: string;
}

/**
 * Company-scoped localization rule: a company may only choose languages /
 * currencies that exist and are Active in the platform-wide master lists
 * (managed on the Localization page). This hook loads those two lists.
 */
export function useLocalizationOptions() {
  const [languages, setLanguages] = useState<LocaleOption[]>([]);
  const [currencies, setCurrencies] = useState<LocaleOption[]>([]);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    let alive = true;
    Promise.all([
      api.get('/languages?pageSize=100').catch(() => null),
      api.get('/currencies?pageSize=100').catch(() => null),
    ]).then(([langRes, currRes]) => {
      if (!alive) return;
      const activeLang = (langRes?.data?.data?.items || langRes?.data?.data || []);
      const activeCurr = (currRes?.data?.data?.items || currRes?.data?.data || []);
      setLanguages((activeLang as any[])
        .filter((l: any) => l.status === 0)
        .map((l: any) => ({ code: l.code, label: `${l.name}${l.nativeName ? ` (${l.nativeName})` : ''}` })));
      setCurrencies((activeCurr as any[])
        .filter((c: any) => c.status === 0)
        .map((c: any) => ({ code: c.code, label: `${c.code} — ${c.name}` })));
    }).finally(() => alive && setLoading(false));
    return () => { alive = false; };
  }, []);

  return { languages, currencies, loading };
}

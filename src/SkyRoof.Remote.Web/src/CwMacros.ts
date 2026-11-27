import { useEffect, useState } from 'react';

const key = 'skyroof.cw.macros.v1';
export function useCwMacros() {
  const [macros, setMacros] = useState<string[]>(() => {
    try {
      const saved: unknown = JSON.parse(localStorage.getItem(key) ?? 'null');
      if (Array.isArray(saved) && saved.length === 8 && saved.every(v => typeof v === 'string'))
        return saved;
    } catch { /* Invalid local storage entry. */ }
    return Array<string>(8).fill('');
  });
  useEffect(() => { localStorage.setItem(key, JSON.stringify(macros)); }, [macros]);
  return { macros, edit: (index: number, value: string) =>
    setMacros(old => old.map((m, i) => i === index ? value : m)) };
}

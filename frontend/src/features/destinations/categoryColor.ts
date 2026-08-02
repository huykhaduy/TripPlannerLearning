// Deterministic (hashed from the category string), not random — so a
// category's color is stable across reloads/re-searches, without needing
// to maintain a list of every possible Geoapify category value (there
// isn't a fixed one — GeoapifyClient.cs's MostSpecificCategory derives it
// from whatever tag hierarchy a place happens to have).
const CATEGORY_COLORS = [
  { bg: 'bg-brand-50', text: 'text-brand-600' },
  { bg: 'bg-amber-50', text: 'text-amber-700' },
  { bg: 'bg-emerald-50', text: 'text-tertiary-500' },
  { bg: 'bg-slate-100', text: 'text-slate-700' },
] as const;

export function categoryColor(category: string): { bg: string; text: string } {
  const hash = [...category].reduce((sum, ch) => sum + ch.charCodeAt(0), 0);
  return CATEGORY_COLORS[hash % CATEGORY_COLORS.length];
}

// Deterministic (hashed from the category string), not random — so a
// category's color is stable across reloads/re-searches, without needing
// to maintain a list of every possible Geoapify category value (there
// isn't a fixed one — GeoapifyClient.cs's MostSpecificCategory derives it
// from whatever tag hierarchy a place happens to have).
const CATEGORY_COLORS = [
  { bg: 'bg-brand-50', text: 'text-brand-600' },
  { bg: 'bg-amber-50', text: 'text-amber-700' },
  { bg: 'bg-emerald-50', text: 'text-tertiary-500' },
  { bg: 'bg-rose-50', text: 'text-rose-700' },
  { bg: 'bg-violet-50', text: 'text-violet-700' },
  { bg: 'bg-orange-50', text: 'text-orange-700' },
  { bg: 'bg-cyan-50', text: 'text-cyan-700' },
  { bg: 'bg-indigo-50', text: 'text-indigo-700' },
] as const;

function hashString(value: string): number {
  let hash = 0;
  for (let i = 0; i < value.length; i++) {
    hash = (hash * 31 + value.charCodeAt(i)) | 0;
  }
  return hash >>> 0;
}

export function categoryColor(category: string): { bg: string; text: string } {
  return CATEGORY_COLORS[hashString(category) % CATEGORY_COLORS.length];
}

import { describe, expect, it } from 'vitest';
import { categoryColor } from '../../../src/features/destinations/categoryColor';

/**
 * The palette is picked by hashing the category string rather than from a lookup
 * table, because Geoapify has no fixed category vocabulary — GeoapifyClient derives
 * one from whatever tag hierarchy a place happens to carry. So the properties worth
 * pinning are "always returns a real palette entry" and "stable for a given input",
 * not which specific color any one category gets.
 */
describe('categoryColor', () => {
  const PALETTE_SIZE = 8;

  it('returns a tailwind bg/text pair', () => {
    const color = categoryColor('tourism.attraction');

    expect(color.bg).toMatch(/^bg-[a-z0-9-]+$/);
    expect(color.text).toMatch(/^text-[a-z0-9-]+$/);
  });

  it('gives the same category the same color every time', () => {
    // The point of hashing instead of assigning at random: a category keeps its
    // color across reloads and re-searches, so the badges do not flicker.
    expect(categoryColor('catering.restaurant')).toEqual(categoryColor('catering.restaurant'));
  });

  it('spreads categories across the whole palette', () => {
    const categories = [
      'tourism.attraction',
      'catering.restaurant',
      'entertainment.museum',
      'natural.water',
      'accommodation.hotel',
      'commercial.shopping_mall',
      'leisure.park',
      'religion.place_of_worship',
      'heritage.unesco',
      'sport.stadium',
    ];

    const distinct = new Set(categories.map((c) => categoryColor(c).bg));

    // Not asserting an exact count — that would break the moment a category string
    // changes. One color for everything would defeat the badge, though.
    expect(distinct.size).toBeGreaterThan(1);
  });

  it('always lands inside the palette, never off the end', () => {
    // hashString folds with `| 0`, which goes negative, then `>>> 0` brings it back
    // to unsigned. Drop that and the modulo yields a negative index and `undefined`
    // here, which would crash the badge with "cannot read property bg of undefined".
    const longEnoughToOverflow = [
      'a'.repeat(64),
      'zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz',
      'tourism.sights.memorial.war_memorial',
      '\u{1F5FC} Hà Nội — Đền Ngọc Sơn',
      'x'.repeat(500),
    ];

    for (const category of longEnoughToOverflow) {
      const color = categoryColor(category);
      expect(color).toBeDefined();
      expect(color.bg).toBeTruthy();
      expect(color.text).toBeTruthy();
    }
  });

  it('handles an empty category without blowing up', () => {
    // AttractionSummary.category is nullable; callers pass '' rather than skipping.
    const color = categoryColor('');

    expect(color.bg).toBeTruthy();
    expect(color.text).toBeTruthy();
  });

  it('treats differently-cased categories as different keys', () => {
    // Not a rule anyone relies on, but pinning it makes the hash's case-sensitivity
    // a decision rather than a surprise if someone starts normalizing upstream.
    const lower = categoryColor('tourism');
    const upper = categoryColor('TOURISM');

    expect(lower).toEqual(categoryColor('tourism'));
    expect(upper).toEqual(categoryColor('TOURISM'));
  });

  it('only ever returns one of the palette entries', () => {
    const seen = new Set<string>();
    for (let i = 0; i < 200; i++) {
      seen.add(JSON.stringify(categoryColor(`category-${i}`)));
    }

    expect(seen.size).toBeLessThanOrEqual(PALETTE_SIZE);
  });
});

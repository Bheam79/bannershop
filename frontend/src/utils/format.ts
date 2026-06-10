/**
 * Shared formatting utilities used across multiple views.
 *
 * All functions are null-safe: passing null/undefined returns a dash (–/—)
 * rather than throwing or rendering "Invalid Date".
 */

/**
 * Formats a NOK amount as a Norwegian number followed by " kr".
 * Null/undefined returns '–'.
 *
 * @param n       The amount to format (null/undefined → '–').
 * @param decimals Maximum fraction digits (default 0 for whole-kroner display).
 *                 Pass 1 when displaying per-unit prices that may be fractional.
 *
 * @example formatNok(1495)       → "1 495 kr"
 * @example formatNok(163.3, 1)   → "163,3 kr"
 * @example formatNok(null)       → "–"
 */
export function formatNok(n: number | null | undefined, decimals = 0): string {
  if (n == null) return '–'
  return new Intl.NumberFormat('nb-NO', { maximumFractionDigits: decimals }).format(n) + ' kr'
}

/**
 * Formats an ISO date string as a short Norwegian date (day + abbreviated month + year).
 * Null/empty returns '—'.
 *
 * @example formatDate('2024-03-15') → "15. mar. 2024"
 */
export function formatDate(iso: string | null | undefined): string {
  if (!iso) return '—'
  return new Date(iso).toLocaleDateString('nb-NO', {
    day: '2-digit',
    month: 'short',
    year: 'numeric',
  })
}

/**
 * Formats an ISO date string as a long Norwegian date (day + full month + year).
 * Use this variant in detail/confirmation views where space allows.
 * Null/empty returns '—'.
 *
 * @example formatDateLong('2024-03-15') → "15. mars 2024"
 */
export function formatDateLong(iso: string | null | undefined): string {
  if (!iso) return '—'
  return new Date(iso).toLocaleDateString('nb-NO', {
    day: '2-digit',
    month: 'long',
    year: 'numeric',
  })
}

/**
 * Formats an ISO datetime string as a short Norwegian date + time.
 * Null/empty returns '—'.
 *
 * @example formatDateTime('2024-03-15T14:30:00Z') → "15. mar. 2024, 15:30"
 */
export function formatDateTime(iso: string | null | undefined): string {
  if (!iso) return '—'
  return new Date(iso).toLocaleString('nb-NO', {
    day: '2-digit',
    month: 'short',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  })
}

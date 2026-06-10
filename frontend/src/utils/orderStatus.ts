/**
 * Shared order-status and design-request-status maps used across multiple views.
 *
 * Two styling systems coexist in this codebase:
 *  - Account views use shorthand badge-* CSS class names (defined in scoped
 *    styles of account/*.vue components).
 *  - Admin views use inline Tailwind utility classes for their distinct dark
 *    admin theme.
 *
 * Each domain therefore ships both sets of class maps.
 */

// ── Order status ─────────────────────────────────────────────────────────────

/** Norwegian labels for banner-order statuses (shared across account + admin). */
export const ORDER_STATUS_LABELS: Record<string, string> = {
  Draft:            'Utkast',
  PendingPayment:   'Venter betaling',
  Paid:             'Betalt',
  InProduction:     'Under produksjon',
  ReadyToShip:      'Klar til frakt',
  Shipped:          'Sendt',
  Delivered:        'Levert',
  Cancelled:        'Kansellert',
  // AI-banner / ManualDesign workflow states
  DesignReady:      'Design klart',
  CustomerApproval: 'Venter kundeaproval',
}

/** Badge CSS classes for order statuses — used by account/*.vue (badge-* shorthand). */
export const ORDER_STATUS_CLASSES: Record<string, string> = {
  Draft:          'badge-draft',
  PendingPayment: 'badge-pending',
  Paid:           'badge-paid',
  InProduction:   'badge-paid',
  ReadyToShip:    'badge-ready',
  Shipped:        'badge-shipped',
  Delivered:      'badge-shipped',
  Cancelled:      'badge-cancelled',
}

/** Tailwind utility classes for order statuses — used by admin/*.vue. */
export const ORDER_STATUS_ADMIN_CLASSES: Record<string, string> = {
  Draft:            'bg-gray-100 text-gray-600',
  PendingPayment:   'bg-yellow-100 text-yellow-800',
  Paid:             'bg-blue-100 text-blue-800',
  InProduction:     'bg-indigo-100 text-indigo-800',
  ReadyToShip:      'bg-purple-100 text-purple-800',
  Shipped:          'bg-green-100 text-green-800',
  Delivered:        'bg-green-100 text-green-700',
  Cancelled:        'bg-red-100 text-red-700',
  // AI-banner / ManualDesign workflow states
  DesignReady:      'bg-cyan-100 text-cyan-800',
  CustomerApproval: 'bg-orange-100 text-orange-800',
}

export function orderStatusLabel(s: string): string {
  return ORDER_STATUS_LABELS[s] ?? s
}
export function orderStatusClass(s: string): string {
  return ORDER_STATUS_CLASSES[s] ?? 'badge-draft'
}
export function orderStatusAdminClass(s: string): string {
  return ORDER_STATUS_ADMIN_CLASSES[s] ?? 'bg-gray-100 text-gray-600'
}

// ── Design-request status ─────────────────────────────────────────────────────

/**
 * Norwegian labels for design-request statuses used in admin views.
 * Uses more operational phrasing (e.g. "Venter godkjenning", "Godkjent").
 */
export const DR_STATUS_LABELS: Record<string, string> = {
  Pending:           'Venter',
  InProgress:        'Under arbeid',
  AwaitingApproval:  'Venter godkjenning',
  Approved:          'Godkjent',
  RevisionRequested: 'Revisjon bedt',
  Revised:           'Revidert',
  Final:             'Levert',
  Failed:            'Feilet',
  Cancelled:         'Kansellert',
}

/**
 * Norwegian labels for design-request statuses used in customer-facing views.
 * Uses friendlier phrasing (e.g. "Til godkjenning", "Design klar").
 */
export const DR_STATUS_CUSTOMER_LABELS: Record<string, string> = {
  Pending:           'Venter',
  InProgress:        'Under arbeid',
  AwaitingApproval:  'Til godkjenning',
  Approved:          'Design klar',
  RevisionRequested: 'Revisjon',
  Revised:           'Revidert',
  Final:             'Levert',
  Failed:            'Feilet',
  Cancelled:         'Kansellert',
}

/** Badge CSS classes for design-request statuses — used by account/*.vue. */
export const DR_STATUS_CLASSES: Record<string, string> = {
  Pending:           'badge-pending',
  InProgress:        'badge-inprogress',
  AwaitingApproval:  'badge-awaiting',
  Approved:          'badge-approved',
  RevisionRequested: 'badge-revision',
  Revised:           'badge-revised',
  Final:             'badge-approved',
  Failed:            'badge-cancelled',
  Cancelled:         'badge-cancelled',
}

/** Tailwind utility classes for design-request statuses — used by admin/*.vue. */
export const DR_STATUS_ADMIN_CLASSES: Record<string, string> = {
  Pending:           'bg-yellow-100 text-yellow-800',
  InProgress:        'bg-blue-100 text-blue-800',
  AwaitingApproval:  'bg-purple-100 text-purple-800',
  Approved:          'bg-green-100 text-green-700',
  RevisionRequested: 'bg-orange-100 text-orange-800',
  Revised:           'bg-sky-100 text-sky-800',
  Final:             'bg-green-100 text-green-800',
  Failed:            'bg-red-100 text-red-700',
  Cancelled:         'bg-red-100 text-red-700',
}

export function drStatusLabel(s: string): string {
  return DR_STATUS_LABELS[s] ?? s
}
export function drStatusCustomerLabel(s: string): string {
  return DR_STATUS_CUSTOMER_LABELS[s] ?? s
}
export function drStatusClass(s: string): string {
  return DR_STATUS_CLASSES[s] ?? 'badge-draft'
}
export function drStatusAdminClass(s: string): string {
  return DR_STATUS_ADMIN_CLASSES[s] ?? 'bg-gray-100 text-gray-600'
}

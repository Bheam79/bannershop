/**
 * BANNERSH-285 — visitor traffic tracking.
 *
 * Call `setupAnalyticsTracker(router)` once from App.vue (or main.ts).
 * Each navigation records a page-view via POST /api/analytics/track.
 *
 * Session lifecycle:
 *   - Session ID is a UUID stored in sessionStorage (new tab / window = new session).
 *   - isNewSession=true only on the very first page-view of the session.
 *
 * Referrer:
 *   - On the first page-view of the session we send document.referrer (the external
 *     site that sent the visitor here).  Subsequent in-app navigations use null so
 *     we don't mis-classify them as referrals.
 */

import type { Router } from 'vue-router'
import { trackPageView } from '@/api/analytics'

const SESSION_KEY = 'bs_session_id'

function getOrCreateSessionId(): { sessionId: string; isNew: boolean } {
  const existing = sessionStorage.getItem(SESSION_KEY)
  if (existing) return { sessionId: existing, isNew: false }

  const id = crypto.randomUUID()
  sessionStorage.setItem(SESSION_KEY, id)
  return { sessionId: id, isNew: true }
}

export function setupAnalyticsTracker(router: Router): void {
  let firstNavigation = true

  router.afterEach((to) => {
    // Skip admin routes and API paths — we care about customer traffic
    if (to.path.startsWith('/admin')) return

    const { sessionId, isNew } = getOrCreateSessionId()

    // Capture document.referrer only on the very first navigation (external source).
    // On subsequent in-app navigations document.referrer is the previous page on this
    // site, which is not an external referral.
    const referrer = firstNavigation ? (document.referrer || null) : null
    firstNavigation = false

    trackPageView({
      sessionId,
      isNewSession: isNew,
      path: to.path,
      referrer,
    })
  })
}

import apiClient from './client'

// ── Public tracking ───────────────────────────────────────────────────────────

export interface TrackPageViewRequest {
  sessionId: string
  isNewSession: boolean
  path: string
  referrer: string | null
}

export function trackPageView(req: TrackPageViewRequest): void {
  // Fire-and-forget — never block navigation on tracking
  apiClient.post('/analytics/track', req).catch(() => {/* silently ignore */})
}

// ── Admin analytics ───────────────────────────────────────────────────────────

export interface SessionBucket {
  label: string
  bucketUtc: string
  pageViews: number
  uniqueSessions: number
  newSessions: number
}

export interface TrafficSummary {
  totalPageViews: number
  totalSessions: number
  newSessions: number
  buckets: SessionBucket[]
}

export interface ReferrerStat {
  source: string
  sessions: number
  pageViews: number
}

export interface ReferrerStats {
  sources: ReferrerStat[]
}

export interface AnalyticsFilter {
  fromUtc?: string
  toUtc?: string
  groupBy?: 'day' | 'hour'
}

export async function getAdminTraffic(filter: AnalyticsFilter = {}): Promise<TrafficSummary> {
  const res = await apiClient.get('/admin/analytics/traffic', { params: filter })
  return res.data
}

export async function getAdminReferrers(filter: Omit<AnalyticsFilter, 'groupBy'> = {}): Promise<ReferrerStats> {
  const res = await apiClient.get('/admin/analytics/referrers', { params: filter })
  return res.data
}

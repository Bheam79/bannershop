import apiClient from './client'

// ── DTOs ──────────────────────────────────────────────────────────────────────

export interface BannerTemplateItem {
  id: number
  category: string
  nameNb: string
  nameEn: string
  sortOrder: number
}

export interface CreateAiRequestPayload {
  templateId: number
  language: string // 'nb' | 'en'
  personName: string
  personAge?: number | null
  textContent: string
  themeDescription: string
  aspectRatio: string // '16:9' | '18:9'
  uploadedPhotoBannerDesignId?: number | null
}

/**
 * BANNERSH-67 free-first flow — response from POST /api/design-requests/ai.
 * No Stripe client secret; payment is collected at print-order time.
 */
export interface CreateAiDesignRequestResult {
  designRequestId: number
  /** true for anonymous callers — frontend shows a soft "create account" prompt */
  requiresAuth: boolean
  /** credits remaining after this generation (auth path only; 0 for anonymous) */
  creditsRemaining: number
}

/** Paywall options carried in a 402 response from POST /api/design-requests/ai. */
export interface PaywallOptions {
  creditPackPriceNok: number
  creditPackCount: number
  bannerOrderActivationFeeNok: number
  /** Number of AI credits granted when the user places a banner order. */
  bannerOrderCreditBonus: number
  manualDesignerUrl: string
  uploadOwnUrl: string
}

/** Full 402 paywall body from POST /api/design-requests/ai. */
export interface AiPaywallData {
  reason: string // 'ip_limit_reached' | 'insufficient_credits'
  creditsRemaining: number
  paywallOptions: PaywallOptions
}

/** 202 response from POST /api/design-requests/{id}/regenerate. */
export interface RegenerateAiResult {
  generationId: number
  creditsRemaining: number
}

/** 402 body from POST /api/design-requests/{id}/regenerate. */
export interface RegeneratePaywall402 {
  error: string
  creditsRemaining: number
  paywallMetadata: { reason: string }
}

/** Kept for /design-requests/manual (still Stripe-gated). */
export interface CreateDesignRequestResponse {
  designRequestId: number
  clientSecret: string
  /** Total charged to the customer (design fee + banner production cost). */
  totalNok: number
  /** Design fee portion (BANNERSH-104). 495 NOK on the manual flow today. */
  designPriceNok: number
  /** Physical-banner production cost portion (BANNERSH-104). May be 0 in degraded mode. */
  bannerPriceNok: number
}

export interface DesignRequestListItem {
  id: number
  bannerTemplateId: number
  mode: string
  status: string
  aspectRatio: string
  priceNok: number
  createdAt: string
  updatedAt: string

  // BANNERSH-83: enriched fields for the AI wizard "past banners" gallery.
  /** Currently active preview URL, or null if generation has not produced an image yet. */
  previewUrl: string | null
  personName: string
  themeDescription: string
}

/** One entry in the generation history of a design request (BANNERSH-84). */
export interface BannerGenerationHistoryItem {
  id: number
  status: string
  isActive: boolean
  createdAt: string
  completedAt: string | null
  /** Cropped/print-ready public URL for this attempt, or null if pipeline hasn't finished. */
  previewUrl: string | null
  /** Uncropped raw AI output URL. */
  rawUrl: string | null
}

export interface DesignRequestDetail {
  id: number
  userId: number | null // nullable since BANNERSH-67 (anonymous requests have no user)
  bannerTemplateId: number
  mode: string
  status: string
  language: string
  personName: string
  personAge: number | null
  textContent: string
  themeDescription: string
  aspectRatio: string
  revisionCount: number
  regenerationsRemaining: number
  priceNok: number
  stripePaymentIntentId: string | null
  previewUrl: string | null
  finalCroppedUrl: string | null
  finalBannerDesignId: number | null
  currentGenerationId: number | null
  lastError: string | null
  /** All AI generation attempts, oldest first. Always empty for Manual requests. (BANNERSH-84) */
  generationHistory: BannerGenerationHistoryItem[]
  createdAt: string
  updatedAt: string
}

// ── API calls ─────────────────────────────────────────────────────────────────

/** Fetch all available banner templates (celebration categories). Public endpoint. */
export async function fetchTemplates(): Promise<BannerTemplateItem[]> {
  const { data } = await apiClient.get<BannerTemplateItem[]>('/templates')
  return data
}

/**
 * Create an AI design request under the BANNERSH-67 free-first flow.
 *
 * The X-Request-Integrity header (bot-protection fingerprint) must be generated
 * by useRequestIntegrity.ts and passed as `integrityToken`.
 *
 * Throws an axios error with `response.status === 402` when the caller has hit
 * the paywall — the caller should extract `response.data` as `AiPaywallData`.
 */
export async function createAiRequest(
  req: CreateAiRequestPayload,
  integrityToken: string,
): Promise<CreateAiDesignRequestResult> {
  const { data } = await apiClient.post<CreateAiDesignRequestResult>(
    '/design-requests/ai',
    req,
    { headers: { 'X-Request-Integrity': integrityToken } },
  )
  return data
}

/** Get full detail for a design request (used for polling). Requires auth. */
export async function getDesignRequest(id: number): Promise<DesignRequestDetail> {
  const { data } = await apiClient.get<DesignRequestDetail>(`/design-requests/${id}`)
  return data
}

/** Customer approves the preview result. Returns the updated design request detail. */
export async function approveDesignRequest(id: number): Promise<DesignRequestDetail> {
  const { data } = await apiClient.post<DesignRequestDetail>(`/design-requests/${id}/approve`)
  return data
}

/**
 * Consume 1 AI credit and enqueue a new generation attempt with optionally updated inputs.
 * Returns 202 on success with updated credit balance.
 * Throws 402 with `RegeneratePaywall402` data when insufficient credits.
 *
 * The X-Request-Integrity header is attached for bot-protection.
 *
 * BANNERSH-84 extension: `personName`, `personAge`, and `uploadedPhotoBannerDesignId`
 * are now accepted alongside `textContent` / `themeDescription` so the account detail
 * view can edit the inputs in-place before regenerating. Pass `personAge: -1` to clear
 * an existing age, and `uploadedPhotoBannerDesignId: -1` to drop a previously-attached
 * portrait. Leave any field undefined/null to keep the current value.
 */
export async function regenerateDesignRequest(
  id: number,
  params: {
    textContent?: string
    themeDescription?: string
    personName?: string
    personAge?: number | null
    uploadedPhotoBannerDesignId?: number | null
  },
  integrityToken: string,
): Promise<RegenerateAiResult> {
  const { data } = await apiClient.post<RegenerateAiResult>(
    `/design-requests/${id}/regenerate`,
    params,
    { headers: { 'X-Request-Integrity': integrityToken } },
  )
  return data
}

/**
 * Switch the active generation to a previously-completed one (BANNERSH-84). Free —
 * does not consume a credit. Used by the design-request detail view's gallery.
 */
export async function activateGeneration(
  id: number,
  generationId: number,
): Promise<DesignRequestDetail> {
  const { data } = await apiClient.post<DesignRequestDetail>(
    `/design-requests/${id}/generations/${generationId}/activate`,
  )
  return data
}

/** List all design requests for the authenticated user. */
export async function listDesignRequests(): Promise<DesignRequestListItem[]> {
  const { data } = await apiClient.get<DesignRequestListItem[]>('/design-requests')
  return data
}

export interface CreateManualRequestPayload {
  templateId: number
  language: string // 'nb' | 'en'
  personName: string
  personAge?: number | null
  textContent: string
  themeDescription: string
  aspectRatio: string // '16:9' | '18:9'
  uploadedPhotoBannerDesignId?: number | null
}

/** Create a Manual design request (495 kr) and return a Stripe PaymentIntent client secret. */
export async function createManualRequest(
  req: CreateManualRequestPayload,
): Promise<CreateDesignRequestResponse> {
  const { data } = await apiClient.post<CreateDesignRequestResponse>(
    '/design-requests/manual',
    req,
  )
  return data
}

/** Customer requests one free revision (Manual only; only allowed when status=AwaitingApproval). */
export async function requestRevision(id: number, comment: string): Promise<DesignRequestDetail> {
  const { data } = await apiClient.post<DesignRequestDetail>(`/design-requests/${id}/revision`, {
    comment,
  })
  return data
}

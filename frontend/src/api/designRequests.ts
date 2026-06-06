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

export interface CreateDesignRequestResponse {
  designRequestId: number
  clientSecret: string
  totalNok: number
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
}

export interface DesignRequestDetail {
  id: number
  userId: number
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
  lastError: string | null
  createdAt: string
  updatedAt: string
}

// ── API calls ─────────────────────────────────────────────────────────────────

/** Fetch all available banner templates (celebration categories). Public endpoint. */
export async function fetchTemplates(): Promise<BannerTemplateItem[]> {
  const { data } = await apiClient.get<BannerTemplateItem[]>('/templates')
  return data
}

/** Create an AI design request and return a Stripe PaymentIntent client secret. */
export async function createAiRequest(
  req: CreateAiRequestPayload,
): Promise<CreateDesignRequestResponse> {
  const { data } = await apiClient.post<CreateDesignRequestResponse>(
    '/design-requests/ai',
    req,
  )
  return data
}

/** Get full detail for a design request (used for polling). */
export async function getDesignRequest(id: number): Promise<DesignRequestDetail> {
  const { data } = await apiClient.get<DesignRequestDetail>(`/design-requests/${id}`)
  return data
}

/** Customer approves the preview result. */
export async function approveDesignRequest(id: number): Promise<void> {
  await apiClient.post(`/design-requests/${id}/approve`)
}

/** Request a free re-generation (uses up one RegenerationsRemaining credit). */
export async function regenerateDesignRequest(id: number): Promise<void> {
  await apiClient.post(`/design-requests/${id}/regenerate`)
}

/** List all design requests for the authenticated user. */
export async function listDesignRequests(): Promise<DesignRequestListItem[]> {
  const { data } = await apiClient.get<DesignRequestListItem[]>('/design-requests')
  return data
}

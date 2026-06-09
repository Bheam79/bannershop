/**
 * API helpers for seeding test data and performing authenticated operations.
 * These call the BannerShop backend directly (bypassing the frontend).
 */

const API_URL = process.env.API_URL ?? 'http://localhost:5000'

export interface LoginResponse {
  accessToken: string
  refreshToken: string
  user: {
    id: number
    email: string
    name: string
    role: string
  }
}

/**
 * Pre-seed an IpAiUsage row for the given IP address (BANNERSH-114).
 *
 * Anonymous AI generation grants 1 free request per IP per rolling 30 days.
 * To deterministically test the "paywall on second attempt" scenario
 * (BANNERSH-79, scenario 3), Playwright calls this BEFORE the first browser
 * generation attempt so the IP is already in the "used" state.
 *
 * The endpoint is only registered in Development — see TestOnlyController and
 * the Program.cs guard that strips it from MVC discovery in all other
 * environments (it returns 404 in Production).
 */
export async function apiSeedIpAiUsage(ipAddress: string): Promise<void> {
  const res = await fetch(`${API_URL}/api/test/seed-ip-ai-usage`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ ipAddress }),
  })
  if (!res.ok) {
    const text = await res.text()
    throw new Error(`Seed IpAiUsage failed (${res.status}): ${text}`)
  }
}

/**
 * Log in via the API and return the auth response.
 */
export async function apiLogin(email: string, password: string): Promise<LoginResponse> {
  const res = await fetch(`${API_URL}/api/auth/login`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ email, password }),
  })
  if (!res.ok) {
    const text = await res.text()
    throw new Error(`Login failed (${res.status}): ${text}`)
  }
  return res.json() as Promise<LoginResponse>
}

/**
 * Register a new user via the API.
 */
export async function apiRegister(
  email: string,
  password: string,
  name: string,
  phone?: string,
): Promise<LoginResponse> {
  const res = await fetch(`${API_URL}/api/auth/register`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ email, password, name, phone: phone ?? null }),
  })
  if (!res.ok) {
    const text = await res.text()
    throw new Error(`Register failed (${res.status}): ${text}`)
  }
  return res.json() as Promise<LoginResponse>
}

/**
 * Create a test order via the API (requires auth token).
 * Returns the orderId so it can be used in tests.
 */
export async function apiCreateOrder(
  accessToken: string,
  opts: {
    bannerSizeId: number
    quantity?: number
    postalCode?: string
    city?: string
    deliveryType?: 'Standard' | 'Express'
    recipientName?: string
  },
): Promise<{ orderId: number; clientSecret: string; totalNok: number }> {
  // First, get the banner size price via shipping calc
  const shippingRes = await fetch(`${API_URL}/api/shipping/calculate`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      Authorization: `Bearer ${accessToken}`,
    },
    body: JSON.stringify({
      postalCode: opts.postalCode ?? '0150',
      city: opts.city ?? 'Oslo',
      bannerSizeId: opts.bannerSizeId,
      qty: opts.quantity ?? 1,
    }),
  })

  if (!shippingRes.ok) {
    // Non-fatal: continue without shipping data
    console.warn('Shipping calc failed, proceeding anyway')
  }

  const draftRes = await fetch(`${API_URL}/api/orders/draft`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      Authorization: `Bearer ${accessToken}`,
    },
    body: JSON.stringify({
      deliveryType: opts.deliveryType ?? 'Standard',
      shippingAddress: {
        line1: 'Testgaten 1',
        postalCode: opts.postalCode ?? '0150',
        city: opts.city ?? 'Oslo',
        country: 'NO',
      },
      items: [
        {
          bannerSizeId: opts.bannerSizeId,
          quantity: opts.quantity ?? 1,
        },
      ],
    }),
  })

  if (!draftRes.ok) {
    const text = await draftRes.text()
    throw new Error(`Create order draft failed (${draftRes.status}): ${text}`)
  }

  return draftRes.json() as Promise<{ orderId: number; clientSecret: string; totalNok: number }>
}

export interface BannerSizeInfo {
  id: number
  name: string
  widthCm: number | null
  heightCm: number
  isCustomWidth: boolean
  calculatedPrice: number | null
  fixedPrice: number | null
  isActive: boolean
}

/**
 * Fetch all public banner sizes from the API.
 */
export async function apiFetchSizes(): Promise<BannerSizeInfo[]> {
  const res = await fetch(`${API_URL}/api/sizes`)
  if (!res.ok) {
    throw new Error(`Fetch sizes failed (${res.status})`)
  }
  return res.json() as Promise<BannerSizeInfo[]>
}

/**
 * Update the production stage of an order item (admin).
 */
export async function apiUpdateProductionStage(
  adminToken: string,
  orderId: number,
  itemId: number,
  stage: string,
  notes?: string,
): Promise<void> {
  const res = await fetch(`${API_URL}/api/admin/orders/${orderId}/items/${itemId}/production`, {
    method: 'PUT',
    headers: {
      'Content-Type': 'application/json',
      Authorization: `Bearer ${adminToken}`,
    },
    body: JSON.stringify({ stage, notes: notes ?? null }),
  })
  if (!res.ok) {
    const text = await res.text()
    throw new Error(`Update production stage failed (${res.status}): ${text}`)
  }
}

/**
 * Update order status as admin (PUT /api/admin/orders/{id}/status).
 * Allowed transitions are gated server-side; see OrderService.AllowedTransitions.
 * Typical e2e flow to reach InProduction:
 *   apiUpdateOrderStatus(token, id, 'Paid')
 *   apiUpdateOrderStatus(token, id, 'InProduction')
 */
export async function apiUpdateOrderStatus(
  adminToken: string,
  orderId: number,
  status: string,
): Promise<void> {
  const res = await fetch(`${API_URL}/api/admin/orders/${orderId}/status`, {
    method: 'PUT',
    headers: {
      'Content-Type': 'application/json',
      Authorization: `Bearer ${adminToken}`,
    },
    body: JSON.stringify({ status }),
  })
  if (!res.ok) {
    const text = await res.text()
    throw new Error(`Update order status to ${status} failed (${res.status}): ${text}`)
  }
}

/**
 * Convenience: advance a freshly-created PendingPayment order through
 * Paid → InProduction so it is eligible for admin shipping operations.
 */
export async function apiAdvanceOrderToInProduction(
  adminToken: string,
  orderId: number,
): Promise<void> {
  await apiUpdateOrderStatus(adminToken, orderId, 'Paid')
  await apiUpdateOrderStatus(adminToken, orderId, 'InProduction')
}

/**
 * Set shipping info on an order (admin). This also flips status to "Shipped".
 */
export async function apiSetShipping(
  adminToken: string,
  orderId: number,
  tracking: {
    carrier: string
    trackingNumber: string
    trackingUrl?: string
    shippedAt?: string
    estimatedArrival?: string
  },
): Promise<void> {
  const res = await fetch(`${API_URL}/api/admin/orders/${orderId}/shipping`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      Authorization: `Bearer ${adminToken}`,
    },
    body: JSON.stringify(tracking),
  })
  if (!res.ok) {
    const text = await res.text()
    throw new Error(`Set shipping failed (${res.status}): ${text}`)
  }
}

// ─── Design-request types ──────────────────────────────────────────────────────

export interface TemplateInfo {
  id: number
  category: string
  nameNb: string
  nameEn: string
  sortOrder: number
}

/**
 * Customer-facing view of a single design request.
 * `stripePaymentIntentId` will start with 'pi_mock_' in development
 * (MockStripePaymentService is active when STRIPE_SECRET_KEY is unset).
 */
export interface DesignRequestInfo {
  id: number
  mode: string                     // 'Ai' | 'Manual'
  status: string                   // 'Pending' | 'InProgress' | 'AwaitingApproval' | 'Final' | 'Cancelled' | 'Failed'
  personName: string
  textContent: string
  themeDescription: string
  aspectRatio: string
  language: string
  personAge: number | null
  priceNok: number
  stripePaymentIntentId: string | null
  previewUrl: string | null
  finalCroppedUrl: string | null
  finalBannerDesignId: number | null
  revisionCount: number
  regenerationsRemaining: number
  customerApprovedAt: string | null
  designerNotes: string | null
  lastError: string | null
  revisions: Array<{
    id: number
    revisionNumber: number
    customerComment: string
    createdAt: string
  }>
  createdAt: string
  updatedAt: string
}

/** Customer-facing list item for GET /api/design-requests. */
export interface DesignRequestListInfo {
  id: number
  mode: string
  status: string
  aspectRatio: string
  priceNok: number
  bannerTemplateId: number
  createdAt: string
  updatedAt: string
}

/** Admin list item — includes customer name/email. */
export interface AdminDesignRequestListInfo {
  id: number
  mode: string
  status: string
  aspectRatio: string
  personName: string
  personAge: number | null
  customerName: string
  customerEmail: string
  priceNok: number
  bannerTemplateId: number
  revisionCount: number
  createdAt: string
  updatedAt: string
}

// ─── Design-request helper functions ──────────────────────────────────────────

/** Shared option shape for both AI and Manual design-request creation. */
interface DesignRequestOpts {
  templateId: number
  personName: string
  textContent: string
  themeDescription: string
  /** ISO 639-1 language code; defaults to 'nb'. */
  language?: string
  /** Banner aspect ratio; defaults to '16:9'. */
  aspectRatio?: string
  personAge?: number | null
}

/**
 * Fetch all public banner templates (GET /api/templates).
 */
export async function apiFetchTemplates(): Promise<TemplateInfo[]> {
  const res = await fetch(`${API_URL}/api/templates`)
  if (!res.ok) {
    const text = await res.text()
    throw new Error(`Fetch templates failed (${res.status}): ${text}`)
  }
  return res.json() as Promise<TemplateInfo[]>
}

/**
 * Create an AI design request (POST /api/design-requests/ai).
 *
 * BANNERSH-67 free-first flow: no Stripe PaymentIntent is created. The response
 * shape is `{ designRequestId, requiresAuth, creditsRemaining }`. The endpoint is
 * gated by the BotProtectionFilter, so a browser-like User-Agent and a non-empty
 * `X-Request-Integrity` header are mandatory (anonymous or authenticated).
 */
export async function apiCreateAiDesignRequest(
  accessToken: string | null,
  opts: DesignRequestOpts,
): Promise<{ designRequestId: number; requiresAuth: boolean; creditsRemaining: number }> {
  const headers: Record<string, string> = {
    'Content-Type': 'application/json',
    // The default `node`/`undici` UA is bot-flagged by BotProtectionFilter.
    'User-Agent': 'Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36',
    'X-Request-Integrity': 'e2e-test-integrity-token',
  }
  if (accessToken) headers.Authorization = `Bearer ${accessToken}`

  const res = await fetch(`${API_URL}/api/design-requests/ai`, {
    method: 'POST',
    headers,
    body: JSON.stringify({
      templateId: opts.templateId,
      personName: opts.personName,
      textContent: opts.textContent,
      themeDescription: opts.themeDescription,
      language: opts.language ?? 'nb',
      aspectRatio: opts.aspectRatio ?? '16:9',
      personAge: opts.personAge ?? null,
    }),
  })
  if (!res.ok) {
    const text = await res.text()
    throw new Error(`Create AI design request failed (${res.status}): ${text}`)
  }
  return res.json() as Promise<{ designRequestId: number; requiresAuth: boolean; creditsRemaining: number }>
}

/**
 * Create a manual design request (POST /api/design-requests/manual).
 * Returns { designRequestId, clientSecret, totalNok }.
 * In development the clientSecret starts with 'pi_mock_' (MockStripePaymentService).
 */
export async function apiCreateManualDesignRequest(
  accessToken: string,
  opts: DesignRequestOpts,
): Promise<{ designRequestId: number; clientSecret: string; totalNok: number }> {
  const res = await fetch(`${API_URL}/api/design-requests/manual`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      Authorization: `Bearer ${accessToken}`,
    },
    body: JSON.stringify({
      templateId: opts.templateId,
      personName: opts.personName,
      textContent: opts.textContent,
      themeDescription: opts.themeDescription,
      language: opts.language ?? 'nb',
      aspectRatio: opts.aspectRatio ?? '16:9',
      personAge: opts.personAge ?? null,
    }),
  })
  if (!res.ok) {
    const text = await res.text()
    throw new Error(`Create manual design request failed (${res.status}): ${text}`)
  }
  return res.json() as Promise<{ designRequestId: number; clientSecret: string; totalNok: number }>
}

/**
 * Get a single design request detail (GET /api/design-requests/{id}).
 * Admins can access any request; customers can only access their own.
 */
export async function apiGetDesignRequest(
  accessToken: string,
  id: number,
): Promise<DesignRequestInfo> {
  const res = await fetch(`${API_URL}/api/design-requests/${id}`, {
    headers: { Authorization: `Bearer ${accessToken}` },
  })
  if (!res.ok) {
    const text = await res.text()
    throw new Error(`Get design request ${id} failed (${res.status}): ${text}`)
  }
  return res.json() as Promise<DesignRequestInfo>
}

/**
 * List the caller's own design requests (GET /api/design-requests).
 */
export async function apiListDesignRequests(
  accessToken: string,
): Promise<DesignRequestListInfo[]> {
  const res = await fetch(`${API_URL}/api/design-requests`, {
    headers: { Authorization: `Bearer ${accessToken}` },
  })
  if (!res.ok) {
    const text = await res.text()
    throw new Error(`List design requests failed (${res.status}): ${text}`)
  }
  return res.json() as Promise<DesignRequestListInfo[]>
}

/**
 * ADMIN: update the status of a design request
 * (PUT /api/admin/design-requests/{id}/status).
 * Requires a JWT obtained via apiLogin(getAdminEmail(), getAdminPassword()).
 */
export async function apiAdminUpdateDesignRequestStatus(
  adminToken: string,
  id: number,
  status: string,
  notes?: string,
): Promise<void> {
  const res = await fetch(`${API_URL}/api/admin/design-requests/${id}/status`, {
    method: 'PUT',
    headers: {
      'Content-Type': 'application/json',
      Authorization: `Bearer ${adminToken}`,
    },
    body: JSON.stringify({ status, notes: notes ?? null }),
  })
  if (!res.ok) {
    const text = await res.text()
    throw new Error(`Admin update design-request status failed (${res.status}): ${text}`)
  }
}

/**
 * ADMIN: list all design requests with optional filters
 * (GET /api/admin/design-requests).
 * Returns { items, totalCount } (a PagedResult).
 * Requires a JWT obtained via apiLogin(getAdminEmail(), getAdminPassword()).
 */
export async function apiAdminListDesignRequests(
  adminToken: string,
  params?: { status?: string; mode?: string; search?: string; page?: number; pageSize?: number },
): Promise<{ items: AdminDesignRequestListInfo[]; totalCount: number; page: number; pageSize: number }> {
  const qs = new URLSearchParams()
  if (params?.status) qs.set('status', params.status)
  if (params?.mode) qs.set('mode', params.mode)
  if (params?.search) qs.set('search', params.search)
  if (params?.page !== undefined) qs.set('page', String(params.page))
  if (params?.pageSize !== undefined) qs.set('pageSize', String(params.pageSize))

  const url = `${API_URL}/api/admin/design-requests${qs.toString() ? `?${qs}` : ''}`
  const res = await fetch(url, {
    headers: { Authorization: `Bearer ${adminToken}` },
  })
  if (!res.ok) {
    const text = await res.text()
    throw new Error(`Admin list design requests failed (${res.status}): ${text}`)
  }
  return res.json() as Promise<{
    items: AdminDesignRequestListInfo[]
    totalCount: number
    page: number
    pageSize: number
  }>
}

/**
 * Customer approves a design request preview
 * (POST /api/design-requests/{id}/approve).
 */
export async function apiApproveDesignRequest(
  accessToken: string,
  id: number,
): Promise<void> {
  const res = await fetch(`${API_URL}/api/design-requests/${id}/approve`, {
    method: 'POST',
    headers: { Authorization: `Bearer ${accessToken}` },
  })
  if (!res.ok) {
    const text = await res.text()
    throw new Error(`Approve design request ${id} failed (${res.status}): ${text}`)
  }
}

/**
 * Customer requests a revision on a design request
 * (POST /api/design-requests/{id}/revision).
 */
export async function apiRequestRevision(
  accessToken: string,
  id: number,
  comment: string,
): Promise<void> {
  const res = await fetch(`${API_URL}/api/design-requests/${id}/revision`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      Authorization: `Bearer ${accessToken}`,
    },
    body: JSON.stringify({ comment }),
  })
  if (!res.ok) {
    const text = await res.text()
    throw new Error(`Request revision on design request ${id} failed (${res.status}): ${text}`)
  }
}

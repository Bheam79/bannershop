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

import apiClient from './client'
import type { BannerSize, ShippingEstimate } from '@/types'

// ─── Banner size endpoints ──────────────────────────────────────────────────

/** Fetches all active banner sizes with calculated prices (custom-width sizes use the optional width). */
export async function fetchSizes(customWidthCm?: number): Promise<BannerSize[]> {
  const params: Record<string, number> = {}
  if (customWidthCm) params.customWidthCm = customWidthCm
  const { data } = await apiClient.get<BannerSize[]>('/sizes', { params })
  return data
}

/** Fetches a single price for a banner size (optionally for a custom width).
 *  Pass noSurcharge=true to omit the custom-width surcharge — used when
 *  dimensions are derived automatically (e.g. the AI quality-picker) rather
 *  than explicitly requested as a custom size by the customer.
 */
export async function fetchPrice(
  sizeId: number,
  customWidthCm?: number,
  noSurcharge?: boolean,
): Promise<number> {
  const params: Record<string, number | boolean> = {}
  if (customWidthCm) params.customWidthCm = customWidthCm
  if (noSurcharge) params.noCustomSurcharge = true
  const { data } = await apiClient.get<{ priceNok: number }>(
    `/sizes/${sizeId}/price`,
    { params },
  )
  return data.priceNok
}

/** Returns the current price per eyelet (malje) in NOK. */
export async function fetchEyeletPriceNok(): Promise<number> {
  const { data } = await apiClient.get<{ pricePerEyeletNok: number }>('/sizes/eyelet-price')
  return data.pricePerEyeletNok
}

// ─── Shipping endpoint ──────────────────────────────────────────────────────

export type PackingMode = 'Rolled' | 'Folded'

export interface ShippingCalculateRequest {
  postalCode: string
  city?: string
  bannerSizeId: number
  customWidthCm?: number
  qty: number
  /** BANNERSH-143 — defaults to Rolled when omitted. */
  packingMode?: PackingMode
}

interface ShippingOptionResponse {
  cost: number
  estimatedDays: number
  carrierProductId?: string | null
  carrierProductName?: string | null
}

interface ShippingResponse {
  standard: ShippingOptionResponse
  express: ShippingOptionResponse
}

export async function calculateShipping(
  req: ShippingCalculateRequest,
): Promise<ShippingEstimate> {
  const { data } = await apiClient.post<ShippingResponse>('/shipping/calculate', req)
  return {
    standard: { costNok: data.standard.cost, estimatedDays: data.standard.estimatedDays },
    express:  { costNok: data.express.cost,  estimatedDays: data.express.estimatedDays  },
  }
}

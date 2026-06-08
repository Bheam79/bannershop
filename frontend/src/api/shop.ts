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

/** Fetches a single price for a banner size (optionally for a custom width). */
export async function fetchPrice(
  sizeId: number,
  customWidthCm?: number,
): Promise<number> {
  const params: Record<string, number> = {}
  if (customWidthCm) params.customWidthCm = customWidthCm
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

export interface ShippingCalculateRequest {
  postalCode: string
  city?: string
  bannerSizeId: number
  customWidthCm?: number
  qty: number
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

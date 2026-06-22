import apiClient from './client'
import type { BannerSize, Material, ShippingEstimate } from '@/types'

// ─── Materials endpoint ──────────────────────────────────────────────────────

/** Fetches all active materials (roll widths, gsm, availability). */
export async function fetchMaterials(): Promise<Material[]> {
  const { data } = await apiClient.get<Material[]>('/materials')
  return data
}

// ─── Banner size endpoints ──────────────────────────────────────────────────

/**
 * Fetches all active banner pricing rules. BANNERSH-255 — each row describes a
 * (material × widthRange × heightRange) combo plus a pricing formula, NOT a
 * single concrete size. Callers that need a price for specific dimensions
 * should use {@link fetchPrice} which returns the cheapest matching rule.
 */
export async function fetchSizes(): Promise<BannerSize[]> {
  const { data } = await apiClient.get<BannerSize[]>('/sizes')
  return data
}

interface PriceResponseDto {
  sizeId: number
  widthCm: number
  heightCm: number
  materialId: number
  priceNok: number
}

/**
 * Looks up the cheapest matching pricing rule for the given banner dimensions.
 * Optionally pin to a specific material. Returns the resolved size id alongside
 * the price so callers can persist the chosen rule on the cart line.
 */
export async function fetchPrice(
  widthCm: number,
  heightCm: number,
  materialId?: number,
): Promise<PriceResponseDto> {
  const params: Record<string, number> = { widthCm, heightCm }
  if (materialId) params.materialId = materialId
  const { data } = await apiClient.get<PriceResponseDto>('/sizes/price', { params })
  return data
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
  materialId: number
  widthCm: number
  heightCm: number
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

// ─── Parcel preview (no postal code) — BANNERSH-180 ─────────────────────────

export interface ParcelPreviewRequest {
  materialId: number
  widthCm: number
  heightCm: number
  qty: number
  packingMode: PackingMode
}

export interface ParcelDimensions {
  lengthCm: number
  widthCm: number
  heightCm: number
  weightKg: number
  /** Total number of physical packages (each holds ≤ 4 banners). Defaults to 1. */
  packageCount?: number
}

/**
 * Returns the parcel dimensions + weight a banner would ship as for the given
 * packing mode. Used by the checkout UI to show "what we'll send to Bring"
 * under each Pakkemetode option. No carrier call, no postal code required.
 */
export async function previewParcel(req: ParcelPreviewRequest): Promise<ParcelDimensions> {
  const { data } = await apiClient.post<ParcelDimensions>('/shipping/parcel-preview', req)
  return data
}

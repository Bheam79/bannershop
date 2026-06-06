import apiClient from './client'
import type { DeliveryType } from '@/types'

// ── Request / Response types ─────────────────────────────────────────────────

export interface OrderDraftRequest {
  deliveryType: DeliveryType
  shippingAddress: {
    line1: string
    postalCode: string
    city: string
    country?: string
  }
  items: Array<{
    bannerSizeId: number
    customWidthCm?: number
    quantity: number
    notes?: string
  }>
}

export interface OrderDraftResponse {
  orderId: number
  clientSecret: string
  totalNok: number
  breakdown: {
    itemsSubtotalNok: number
    shippingCostNok: number
    expressFeeNok: number
    totalNok: number
  }
}

export interface OrderDetailResponse {
  id: number
  status: string
  deliveryType: string
  shippingCostNok: number
  expressFeeNok: number
  totalNok: number
  estimatedDelivery: string | null
  createdAt: string
  shippingAddress: {
    line1: string
    line2?: string | null
    postalCode: string
    city: string
    country: string
  } | null
  items: Array<{
    id: number
    bannerSizeName: string | null
    customWidthCm: number | null
    heightCm: number
    quantity: number
    unitPriceNok: number
    lineTotalNok: number
  }>
}

// ── API calls ────────────────────────────────────────────────────────────────

export async function createOrderDraft(req: OrderDraftRequest): Promise<OrderDraftResponse> {
  const { data } = await apiClient.post<OrderDraftResponse>('/orders/draft', req)
  return data
}

export async function getOrder(id: number): Promise<OrderDetailResponse> {
  const { data } = await apiClient.get<OrderDetailResponse>(`/orders/${id}`)
  return data
}

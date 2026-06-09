import apiClient from './client'
import type { DeliveryType, EyeletOption } from '@/types'

// ── Request types ─────────────────────────────────────────────────────────────

export interface OrderDraftRequest {
  deliveryType: DeliveryType
  /** Required for Standard/Express delivery. Omit for Pickup. */
  shippingAddress?: {
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
    bannerDesignId?: number
    eyeletOption?: EyeletOption
  }>
}

// ── Response types ────────────────────────────────────────────────────────────

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

export interface OrderListItem {
  id: number
  status: string
  deliveryType: string
  totalNok: number
  itemCount: number
  createdAt: string
  estimatedDelivery: string | null
  // Present in admin responses
  customerName?: string | null
  customerEmail?: string | null
}

export interface OrdersPage {
  items: OrderListItem[]
  page: number
  pageSize: number
  totalCount: number
  totalPages: number
}

export interface ProductionStatusEntry {
  id: number
  stage: string
  updatedAt: string
  notes: string | null
}

export interface OrderItemDetail {
  id: number
  bannerSizeName: string | null
  customWidthCm: number | null
  heightCm: number
  quantity: number
  unitPriceNok: number
  lineTotalNok: number
  notes: string | null
  bannerDesignId: number | null
  currentProductionStage: string
  productionStatusHistory: ProductionStatusEntry[]
}

export interface ShipmentTracking {
  carrier: string
  trackingNumber: string
  trackingUrl: string | null
  shippedAt: string | null
  estimatedArrival: string | null
  deliveredAt: string | null
}

export interface OrderDetailResponse {
  id: number
  userId?: number
  customerName?: string | null
  customerEmail?: string | null
  status: string
  deliveryType: string
  shippingCostNok: number
  expressFeeNok: number
  totalNok: number
  estimatedDelivery: string | null
  createdAt: string
  updatedAt: string
  shippingAddress: {
    line1: string
    line2?: string | null
    postalCode: string
    city: string
    country: string
  } | null
  items: OrderItemDetail[]
  shipmentTracking: ShipmentTracking | null
}

// ── API calls ─────────────────────────────────────────────────────────────────

export async function listOrders(page = 1, pageSize = 20): Promise<OrdersPage> {
  const { data } = await apiClient.get<OrdersPage>('/orders', { params: { page, pageSize } })
  return data
}

export async function createOrderDraft(req: OrderDraftRequest): Promise<OrderDraftResponse> {
  const { data } = await apiClient.post<OrderDraftResponse>('/orders/draft', req)
  return data
}

export async function getOrder(id: number): Promise<OrderDetailResponse> {
  const { data } = await apiClient.get<OrderDetailResponse>(`/orders/${id}`)
  return data
}

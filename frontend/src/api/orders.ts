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
  /**
   * Cart-level packaging choice (BANNERSH-174). All items share one mode.
   * "Folded" = flat 50×60 cm box; "Rolled" = tube.
   * Defaults to "Folded" when omitted.
   */
  packingMode?: 'Rolled' | 'Folded'
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

// ── Type-specific detail sub-objects ─────────────────────────────────────────

export interface CustomBannerDetail {
  previewUrl: string | null
  bannerSizeName: string | null
  materialName: string | null
}

export interface AiBannerDetail {
  previewUrl: string | null
  themeDescription: string | null
  personName: string | null
  revisionCount: number
  designRequestId: number | null
}

export interface ManualDesignDetail {
  previewUrl: string | null
  aspectRatio: string | null
  designerNotes: string | null
  designRequestId: number | null
}

export interface OrderListItem {
  id: number
  status: string
  /** Fulfilment flow: CustomBanner / AiBanner / ManualDesign */
  orderType?: string
  /** Lifecycle state per state-machine */
  orderState?: string
  deliveryType: string
  totalNok: number
  itemCount: number
  createdAt: string
  estimatedDelivery: string | null
  // Present in admin responses
  customerName?: string | null
  customerEmail?: string | null
  // Type-specific sub-objects (admin responses only)
  customBanner?: CustomBannerDetail | null
  aiBanner?: AiBannerDetail | null
  manualDesign?: ManualDesignDetail | null
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
  designRequestId: number | null
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
  /** Fulfilment flow: CustomBanner / AiBanner / ManualDesign */
  orderType?: string
  /** Lifecycle state per state-machine */
  orderState?: string
  deliveryType: string
  /** How the banner is packed for shipping: "Rolled" or "Folded" (BANNERSH-149) */
  packingMode?: string
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
  // Type-specific sub-objects
  customBanner?: CustomBannerDetail | null
  aiBanner?: AiBannerDetail | null
  manualDesign?: ManualDesignDetail | null
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

/**
 * BANNERSH-182: testing-only override that flips an order to Paid without
 * going through Stripe. Used by the checkout's "Marker som betalt
 * (testmodus)" modal. Requires the caller to own the order and the
 * server-side <c>Testing:EnableMockPayment</c> flag to be on; the password
 * is configured server-side (<c>Testing:MockPaymentPassword</c>, default
 * "test1234"). Returns the updated order detail on success.
 */
export async function mockPayOrder(
  orderId: number,
  password: string,
): Promise<OrderDetailResponse> {
  const { data } = await apiClient.post<OrderDetailResponse>(
    `/orders/${orderId}/mock-pay`,
    { password },
  )
  return data
}

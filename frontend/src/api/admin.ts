import apiClient from './client'
import type { OrderDetailResponse, OrdersPage } from './orders'

// ── Admin Orders ──────────────────────────────────────────────────────────────

export interface AdminOrderFilter {
  status?: string
  fromUtc?: string
  toUtc?: string
  search?: string
  page?: number
  pageSize?: number
}

export async function listAdminOrders(filter: AdminOrderFilter = {}): Promise<OrdersPage> {
  // Strip undefined/empty values
  const params: Record<string, string | number> = {}
  if (filter.status) params.status = filter.status
  if (filter.fromUtc) params.fromUtc = filter.fromUtc
  if (filter.toUtc) params.toUtc = filter.toUtc
  if (filter.search) params.search = filter.search
  if (filter.page) params.page = filter.page
  if (filter.pageSize) params.pageSize = filter.pageSize
  const { data } = await apiClient.get<OrdersPage>('/admin/orders', { params })
  return data
}

export async function getAdminOrder(id: number): Promise<OrderDetailResponse> {
  const { data } = await apiClient.get<OrderDetailResponse>(`/admin/orders/${id}`)
  return data
}

export async function updateOrderStatus(id: number, status: string): Promise<OrderDetailResponse> {
  const { data } = await apiClient.put<OrderDetailResponse>(`/admin/orders/${id}/status`, { status })
  return data
}

export async function updateProductionStage(
  orderId: number,
  itemId: number,
  stage: string,
  notes?: string,
): Promise<OrderDetailResponse> {
  const { data } = await apiClient.put<OrderDetailResponse>(
    `/admin/orders/${orderId}/items/${itemId}/production`,
    { stage, notes: notes || null },
  )
  return data
}

export interface SetShippingRequest {
  carrier: string
  trackingNumber: string
  trackingUrl?: string
  shippedAt?: string
  estimatedArrival?: string
}

export async function setShipping(
  orderId: number,
  req: SetShippingRequest,
): Promise<OrderDetailResponse> {
  const { data } = await apiClient.post<OrderDetailResponse>(
    `/admin/orders/${orderId}/shipping`,
    req,
  )
  return data
}

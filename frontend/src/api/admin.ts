import apiClient from './client'
import type { OrderDetailResponse, OrdersPage } from './orders'

// ── Admin Design Requests ──────────────────────────────────────────────────────

export interface AdminDesignRequestListItem {
  id: number
  mode: string
  status: string
  aspectRatio: string
  priceNok: number
  bannerTemplateId: number
  personName: string
  personAge: number | null
  userId: number
  customerName: string
  customerEmail: string
  revisionCount: number
  createdAt: string
  updatedAt: string
}

export interface AdminDesignRequestFilter {
  status?: string
  mode?: string
  fromUtc?: string
  toUtc?: string
  search?: string
  page?: number
  pageSize?: number
}

export interface AdminDesignRequestsPage {
  items: AdminDesignRequestListItem[]
  totalCount: number
  page: number
  pageSize: number
  totalPages: number
}

export interface DesignRequestRevision {
  id: number
  revisionNumber: number
  customerComment: string
  createdAt: string
}

export interface AdminDesignRequestDetail {
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
  customerApprovedAt: string | null
  designerNotes: string | null
  revisions: DesignRequestRevision[]
  createdAt: string
  updatedAt: string
  // Admin-only extras
  customerName: string
  customerEmail: string
  uploadedPhotoUrl: string | null
  templateName: string | null
}

export async function listAdminDesignRequests(
  filter: AdminDesignRequestFilter = {},
): Promise<AdminDesignRequestsPage> {
  const params: Record<string, string | number> = {}
  if (filter.status) params.status = filter.status
  if (filter.mode) params.mode = filter.mode
  if (filter.fromUtc) params.fromUtc = filter.fromUtc
  if (filter.toUtc) params.toUtc = filter.toUtc
  if (filter.search) params.search = filter.search
  if (filter.page) params.page = filter.page
  if (filter.pageSize) params.pageSize = filter.pageSize
  const { data } = await apiClient.get<AdminDesignRequestsPage>('/admin/design-requests', { params })
  return data
}

export async function getAdminDesignRequest(id: number): Promise<AdminDesignRequestDetail> {
  const { data } = await apiClient.get<AdminDesignRequestDetail>(`/admin/design-requests/${id}`)
  return data
}

export async function updateDesignRequestStatus(
  id: number,
  status: string,
  notes?: string,
): Promise<AdminDesignRequestDetail> {
  const { data } = await apiClient.put<AdminDesignRequestDetail>(
    `/admin/design-requests/${id}/status`,
    { status, notes: notes || null },
  )
  return data
}

export async function uploadDesignRequestPreview(
  id: number,
  file: File,
): Promise<AdminDesignRequestDetail> {
  const form = new FormData()
  form.append('file', file)
  const { data } = await apiClient.post<AdminDesignRequestDetail>(
    `/admin/design-requests/${id}/upload-preview`,
    form,
    { headers: { 'Content-Type': 'multipart/form-data' } },
  )
  return data
}

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

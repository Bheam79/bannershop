// ─── Materials & Sizes ───────────────────────────────────────────────────────

export interface Material {
  id: number
  name: string
  widthCm: number
  weightGsm: number
  pricePerSqm: number
  availableFrom: string | null
}

export interface BannerSize {
  id: number
  widthCm: number | null
  heightCm: number
  isCustomWidth: boolean
  name: string
  isActive: boolean
  materialId: number
  material?: Material
  fixedPrice: number | null
  sortOrder: number
  calculatedPrice?: number
  availableFrom?: string | null
}

// ─── Pricing ─────────────────────────────────────────────────────────────────

export interface PricingParameter {
  id: number
  name: string
  key: string
  value: number
  description: string | null
}

export interface ShippingEstimate {
  standard: { costNok: number; estimatedDays: number }
  express: { costNok: number; estimatedDays: number }
}

// ─── Auth ────────────────────────────────────────────────────────────────────

export type UserRole = 'Customer' | 'Admin'

export interface User {
  id: number
  email: string
  name: string
  phone: string | null
  role: UserRole
}

export interface AuthResponse {
  accessToken: string
  refreshToken: string
  user: User
}

// ─── Orders ──────────────────────────────────────────────────────────────────

export type OrderStatus =
  | 'Draft'
  | 'PendingPayment'
  | 'Paid'
  | 'InProduction'
  | 'ReadyToShip'
  | 'Shipped'
  | 'Delivered'
  | 'Cancelled'

export type DeliveryType = 'Standard' | 'Express'

export type ProductionStage = 'Queued' | 'Printing' | 'Finishing' | 'ReadyToShip'

export interface Address {
  id?: number
  line1: string
  line2?: string
  postalCode: string
  city: string
  country: string
}

export interface ProductionStatus {
  id: number
  stage: ProductionStage
  updatedAt: string
  notes: string | null
}

export interface ShipmentTracking {
  carrier: string
  trackingNumber: string
  trackingUrl: string | null
  shippedAt: string | null
  estimatedArrival: string | null
  deliveredAt: string | null
}

export interface OrderItem {
  id: number
  bannerSizeId: number | null
  bannerSizeName: string | null
  customWidthCm: number | null
  heightCm: number
  quantity: number
  areaSqm: number
  unitPriceNok: number
  lineTotalNok: number
  notes: string | null
  productionStatuses: ProductionStatus[]
}

export interface Order {
  id: number
  status: OrderStatus
  deliveryType: DeliveryType
  shippingAddress: Address | null
  shippingCostNok: number
  expressFeeNok: number
  totalNok: number
  createdAt: string
  updatedAt: string
  estimatedDelivery: string | null
  items: OrderItem[]
  shipmentTracking: ShipmentTracking | null
}

// ─── Cart (frontend only) ────────────────────────────────────────────────────

export interface CartItem {
  bannerSizeId: number | null
  bannerSizeName: string
  customWidthCm: number | null
  heightCm: number
  quantity: number
  unitPriceNok: number
  notes?: string
}

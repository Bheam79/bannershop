// ─── Materials & Sizes ───────────────────────────────────────────────────────

export interface Material {
  id: number
  name: string
  widthCm: number
  /**
   * Max banner width producible as a single panel — anything wider is priced as
   * a 2× / 3× / … multi-panel banner (BANNERSH-88). Defaults to {@link widthCm} on
   * the server when set to 0/unset.
   */
  maxBannerWidthCm: number
  weightGsm: number
  pricePerSqm: number
  availableFrom: string | null
}

export interface BannerSize {
  id: number
  widthCm: number | null
  heightCm: number
  isCustomWidth: boolean
  isCustomHeight: boolean
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

export type DeliveryType = 'Standard' | 'Express' | 'Pickup'

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

// ─── Eyelets (maljer) ────────────────────────────────────────────────────────

/** Eyelet (malje) finishing option. Hem is not possible on PVC banners. */
export type EyeletOption = 'None' | 'FourCorners' | 'PerMeter'

/**
 * Count intermediate eyelets between two corner eyelets separated by `sideLength` cm.
 * Mirrors `EyeletCalculator.CountIntermediatesOnSegment` in the backend (BANNERSH-93).
 */
export function countIntermediatesOnSegment(sideLength: number): number {
  if (sideLength <= 120) return 0
  if (sideLength <= 260) return 1
  return 2 + countIntermediatesOnSegment(sideLength - 200)
}

/** Total eyelet count for a banner. Mirrors `EyeletCalculator.CountEyelets`. */
export function countEyelets(widthCm: number, heightCm: number, option: EyeletOption): number {
  if (option === 'None') return 0
  if (option === 'FourCorners') return 4
  // PerMeter
  if (widthCm <= 0 || heightCm <= 0) return 0
  return (
    4
    + countIntermediatesOnSegment(widthCm)  // top
    + countIntermediatesOnSegment(widthCm)  // bottom
    + countIntermediatesOnSegment(heightCm) // left
    + countIntermediatesOnSegment(heightCm) // right
  )
}

// ─── Cart (frontend only) ────────────────────────────────────────────────────

export interface CartItem {
  bannerSizeId: number | null
  bannerSizeName: string
  customWidthCm: number | null
  heightCm: number
  quantity: number
  /** Base banner price per unit (excl. eyelets). */
  unitPriceNok: number
  /** Eyelet finishing option chosen by the customer. */
  eyeletOption: EyeletOption
  /** Eyelet fee per unit (count × price_per_eyelet), pre-computed client-side for display. */
  eyeletFeeNok: number
  notes?: string
  /** Optional reference to a BannerDesign uploaded via the banner builder. */
  designId?: number
  /**
   * For banners linked to a server-side DesignRequest. AI banners use this to
   * pass the AI activation fee + design preview to the backend; Manual banners
   * use this to bundle the 495 kr designer fee into the linked banner item's
   * LineTotalNok server-side (BANNERSH-249). A single banner is one cart line —
   * there is no separate "designer fee" line item.
   */
  designRequestId?: number
  /**
   * Manual designer fee (in NOK) bundled into THIS banner item — set by the
   * manual-builder flow so the cart subtotal matches what the backend will
   * compute (BANNERSH-249). 0 / undefined for non-manual banners. The server
   * is the source of truth; this field is for cart display only.
   */
  manualDesignFeeNok?: number
  /**
   * Optional preview URL for the banner thumbnail (BANNERSH-140). Cached when the
   * item is added to the cart so the checkout view can render a thumbnail without
   * a round-trip. Falls back to `getBannerDesign(designId)` when missing.
   */
  previewUrl?: string
  /**
   * When true the backend skips the `custom_width_surcharge` when computing the
   * unit price.  Set for AI banner items whose dimensions are derived automatically
   * (not manually chosen as a custom size by the customer) — BANNERSH-228.
   */
  skipCustomSurcharge?: boolean
}

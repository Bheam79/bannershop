import { defineStore } from 'pinia'
import { ref } from 'vue'
import type { DeliveryType } from '@/types'
import type { PackingMode } from '@/api/shop'

export interface CheckoutAddress {
  line1: string
  postalCode: string
  city: string
}

export interface CheckoutState {
  recipientName: string
  address: CheckoutAddress
  deliveryType: DeliveryType
  shippingCostNok: number
  expressFeeNok: number
  /** BANNERSH-174: cart-level packaging choice (Folded = default). */
  packingMode: PackingMode
}

// BANNERSH-181: persist the customer's last-used delivery address to
// localStorage so it's pre-filled on the next checkout (survives both
// `checkout.clear()` after a successful order AND page reloads).
const LAST_ADDRESS_KEY = 'bannershop_last_address'

// BANNERSH-249: persist the in-flight draft order so we don't mint a fresh
// Order row every time the customer re-enters the PaymentView. The cached id
// is dropped when the cart contents change (see `markCartChanged`) or on
// successful payment (via `clearDraftOrder`).
const DRAFT_ORDER_KEY = 'bannershop_draft_order'

interface DraftOrderSnapshot {
  orderId: number
  cartHash: string
}

function readDraftOrder(): DraftOrderSnapshot | null {
  try {
    const raw = localStorage.getItem(DRAFT_ORDER_KEY)
    if (!raw) return null
    const parsed = JSON.parse(raw) as Partial<DraftOrderSnapshot> | null
    if (!parsed || typeof parsed.orderId !== 'number' || typeof parsed.cartHash !== 'string') return null
    return { orderId: parsed.orderId, cartHash: parsed.cartHash }
  } catch {
    return null
  }
}

function writeDraftOrder(snapshot: DraftOrderSnapshot | null): void {
  try {
    if (snapshot === null) localStorage.removeItem(DRAFT_ORDER_KEY)
    else localStorage.setItem(DRAFT_ORDER_KEY, JSON.stringify(snapshot))
  } catch {
    // non-fatal — duplicate prevention degrades to per-mount cache only
  }
}

interface LastAddressSnapshot {
  recipientName: string
  address: CheckoutAddress
  deliveryType: DeliveryType
  packingMode: PackingMode
}

function readLastAddress(): LastAddressSnapshot | null {
  try {
    const raw = localStorage.getItem(LAST_ADDRESS_KEY)
    if (!raw) return null
    const parsed = JSON.parse(raw) as Partial<LastAddressSnapshot> | null
    if (!parsed || typeof parsed !== 'object') return null
    const recipientName = typeof parsed.recipientName === 'string' ? parsed.recipientName : ''
    const addr = parsed.address ?? { line1: '', postalCode: '', city: '' }
    const address: CheckoutAddress = {
      line1: typeof addr.line1 === 'string' ? addr.line1 : '',
      postalCode: typeof addr.postalCode === 'string' ? addr.postalCode : '',
      city: typeof addr.city === 'string' ? addr.city : '',
    }
    const deliveryType: DeliveryType =
      parsed.deliveryType === 'Express' || parsed.deliveryType === 'Pickup'
        ? parsed.deliveryType
        : 'Standard'
    const packingMode: PackingMode =
      parsed.packingMode === 'Rolled' ? 'Rolled' : 'Folded'
    return { recipientName, address, deliveryType, packingMode }
  } catch {
    return null
  }
}

function writeLastAddress(snapshot: LastAddressSnapshot): void {
  try {
    localStorage.setItem(LAST_ADDRESS_KEY, JSON.stringify(snapshot))
  } catch {
    // Quota / serialization errors are non-fatal — checkout still proceeds.
  }
}

export const useCheckoutStore = defineStore('checkout', () => {
  // Pre-fill from the previously saved address (if any) so the form is
  // populated even on a fresh page load after `checkout.clear()`.
  const last = readLastAddress()

  const recipientName = ref(last?.recipientName ?? '')
  const address = ref<CheckoutAddress>(
    last?.address ?? { line1: '', postalCode: '', city: '' },
  )
  const deliveryType = ref<DeliveryType>(last?.deliveryType ?? 'Standard')
  const shippingCostNok = ref(0)
  const expressFeeNok = ref(0)
  /** Default Folded per BANNERSH-174 spec. */
  const packingMode = ref<PackingMode>(last?.packingMode ?? 'Folded')

  // BANNERSH-249: re-hydrate the persisted draft-order pointer so a customer who
  // bounces between the cart and the payment page reuses the same Order row
  // (via /retry-payment) instead of spawning a fresh draft on every mount.
  const draftSnapshot = readDraftOrder()
  const draftOrderId = ref<number | null>(draftSnapshot?.orderId ?? null)
  const draftCartHash = ref<string | null>(draftSnapshot?.cartHash ?? null)

  const isReady = () => {
    if (!recipientName.value.trim()) return false
    if (deliveryType.value === 'Pickup') return true
    return (
      !!address.value.line1.trim() &&
      /^\d{4}$/.test(address.value.postalCode) &&
      !!address.value.city.trim()
    )
  }

  function setCheckout(state: CheckoutState) {
    recipientName.value = state.recipientName
    address.value = { ...state.address }
    deliveryType.value = state.deliveryType
    shippingCostNok.value = state.shippingCostNok
    expressFeeNok.value = state.expressFeeNok
    packingMode.value = state.packingMode
    // BANNERSH-181: snapshot the address as "last used" the moment the
    // customer commits it (Gå til betaling). Persisting at this point —
    // instead of waiting for payment success — means the form is
    // pre-filled even if the user abandons the order mid-payment.
    writeLastAddress({
      recipientName: state.recipientName,
      address: { ...state.address },
      deliveryType: state.deliveryType,
      packingMode: state.packingMode,
    })
  }

  function clear() {
    recipientName.value = ''
    address.value = { line1: '', postalCode: '', city: '' }
    deliveryType.value = 'Standard'
    shippingCostNok.value = 0
    expressFeeNok.value = 0
    packingMode.value = 'Folded'
    // BANNERSH-249: a completed checkout invalidates the draft pointer.
    draftOrderId.value = null
    draftCartHash.value = null
    writeDraftOrder(null)
  }

  /**
   * BANNERSH-249: remember the draft Order id we just minted (or successfully
   * reused) along with the cart-content hash it was created against. Subsequent
   * visits to the payment page with the SAME cart hash will reuse this id via
   * <c>retryOrderPayment</c> instead of creating a duplicate Order row.
   */
  function setDraftOrder(orderId: number, cartHash: string) {
    draftOrderId.value = orderId
    draftCartHash.value = cartHash
    writeDraftOrder({ orderId, cartHash })
  }

  /**
   * BANNERSH-249: drop the cached draft pointer — call this on successful
   * payment, on customer-initiated cancel, or when the cart is mutated in a
   * way that invalidates the snapshotted contents.
   */
  function clearDraftOrder() {
    draftOrderId.value = null
    draftCartHash.value = null
    writeDraftOrder(null)
  }

  /**
   * BANNERSH-181: re-hydrate recipient/address/deliveryType/packingMode from
   * the persisted "last used" snapshot in localStorage. Used by CheckoutView
   * to repopulate the form after a successful order (which called `clear()`
   * on the in-memory store) without forcing a page reload.
   *
   * Only fields the user actually filled in are restored — money fields
   * (shippingCostNok, expressFeeNok) intentionally stay at 0 so they're
   * recomputed from the live Bring quote on the new order.
   */
  function loadLastAddress(): boolean {
    const last = readLastAddress()
    if (!last) return false
    recipientName.value = last.recipientName
    address.value = { ...last.address }
    deliveryType.value = last.deliveryType
    packingMode.value = last.packingMode
    return true
  }

  return {
    recipientName,
    address,
    deliveryType,
    shippingCostNok,
    expressFeeNok,
    packingMode,
    draftOrderId,
    draftCartHash,
    isReady,
    setCheckout,
    clear,
    loadLastAddress,
    setDraftOrder,
    clearDraftOrder,
  }
})

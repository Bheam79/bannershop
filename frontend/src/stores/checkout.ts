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
    isReady,
    setCheckout,
    clear,
    loadLastAddress,
  }
})

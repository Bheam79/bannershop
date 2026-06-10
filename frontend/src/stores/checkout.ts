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

export const useCheckoutStore = defineStore('checkout', () => {
  const recipientName = ref('')
  const address = ref<CheckoutAddress>({ line1: '', postalCode: '', city: '' })
  const deliveryType = ref<DeliveryType>('Standard')
  const shippingCostNok = ref(0)
  const expressFeeNok = ref(0)
  /** Default Folded per BANNERSH-174 spec. */
  const packingMode = ref<PackingMode>('Folded')

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
  }

  function clear() {
    recipientName.value = ''
    address.value = { line1: '', postalCode: '', city: '' }
    deliveryType.value = 'Standard'
    shippingCostNok.value = 0
    expressFeeNok.value = 0
    packingMode.value = 'Folded'
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
  }
})

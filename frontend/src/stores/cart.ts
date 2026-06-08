import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import type { CartItem, DeliveryType } from '@/types'

export const useCartStore = defineStore('cart', () => {
  const items = ref<CartItem[]>([])
  const deliveryType = ref<DeliveryType>('Standard')
  const shippingCostNok = ref<number>(0)
  const expressFeeNok = ref<number>(0)

  const subtotal = computed(() =>
    items.value.reduce(
      (sum, item) => sum + (item.unitPriceNok + item.eyeletFeeNok) * item.quantity,
      0,
    )
  )

  const total = computed(() =>
    subtotal.value + shippingCostNok.value + expressFeeNok.value
  )

  const itemCount = computed(() =>
    items.value.reduce((sum, item) => sum + item.quantity, 0)
  )

  function addItem(item: CartItem) {
    items.value.push(item)
  }

  function removeItem(index: number) {
    items.value.splice(index, 1)
  }

  function updateQuantity(index: number, qty: number) {
    if (qty < 1) {
      removeItem(index)
    } else {
      const item = items.value[index]
      if (item) item.quantity = qty
    }
  }

  function clear() {
    items.value = []
    shippingCostNok.value = 0
    expressFeeNok.value = 0
    deliveryType.value = 'Standard'
  }

  function setShipping(cost: number, expressfee: number, type: DeliveryType) {
    shippingCostNok.value = cost
    expressFeeNok.value = expressfee
    deliveryType.value = type
  }

  return {
    items,
    deliveryType,
    shippingCostNok,
    expressFeeNok,
    subtotal,
    total,
    itemCount,
    addItem,
    removeItem,
    updateQuantity,
    clear,
    setShipping,
  }
})

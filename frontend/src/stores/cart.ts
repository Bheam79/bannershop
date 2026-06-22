import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import type { CartItem, DeliveryType } from '@/types'

// BANNERSH-249: stable hash of the cart contents used by the checkout store to
// determine whether a previously-minted draft Order can be reused (same hash)
// or must be re-created (cart has been mutated). Includes only fields that
// affect server-side pricing/validation so trivial UI-only mutations (preview
// URL caching, eyelet preview animations, etc.) don't invalidate the draft.
export function computeCartHash(items: ReadonlyArray<CartItem>): string {
  return items
    .map((i) =>
      [
        i.bannerSizeId ?? 'null',
        i.materialId ?? 'null',
        i.widthCm,
        i.heightCm,
        i.quantity,
        i.unitPriceNok,
        i.eyeletOption,
        i.eyeletFeeNok,
        i.designId ?? 'null',
        i.designRequestId ?? 'null',
        i.manualDesignFeeNok ?? 0,
      ].join(':'),
    )
    .join('|')
}

export const useCartStore = defineStore('cart', () => {
  const items = ref<CartItem[]>([])
  const deliveryType = ref<DeliveryType>('Standard')
  const shippingCostNok = ref<number>(0)
  const expressFeeNok = ref<number>(0)

  const cartHash = computed(() => computeCartHash(items.value))

  const subtotal = computed(() =>
    items.value.reduce(
      // BANNERSH-249: manual designer fee is per-DR (once), not per-quantity.
      (sum, item) => sum
        + (item.unitPriceNok + item.eyeletFeeNok) * item.quantity
        + (item.manualDesignFeeNok ?? 0),
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
    cartHash,
    addItem,
    removeItem,
    updateQuantity,
    clear,
    setShipping,
  }
})

<script setup lang="ts">
import { ref, computed, watch, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { useCartStore } from '@/stores/cart'
import { useCheckoutStore } from '@/stores/checkout'
import { calculateShipping } from '@/api/shop'
import type { DeliveryType, ShippingEstimate } from '@/types'

const router = useRouter()
const cart = useCartStore()
const checkout = useCheckoutStore()

// ── Redirect if cart is empty ────────────────────────────────────────────────
onMounted(() => {
  if (cart.items.length === 0) {
    router.replace('/')
  }
})

// ── Form state (pre-fill from checkout store if user came back) ──────────────
const recipientName = ref(checkout.recipientName)
const addressLine1 = ref(checkout.address.line1)
const postalCode = ref(checkout.address.postalCode)
const city = ref(checkout.address.city)
const deliveryType = ref<DeliveryType>(checkout.deliveryType)

// ── Shipping calculation ─────────────────────────────────────────────────────
const shippingEstimate = ref<ShippingEstimate | null>(null)
const shippingLoading = ref(false)
const shippingError = ref<string | null>(null)

async function computeShipping() {
  shippingError.value = null
  const pc = postalCode.value.trim()
  if (!/^\d{4}$/.test(pc)) {
    shippingEstimate.value = null
    return
  }
  const firstItem = cart.items[0]
  if (!firstItem?.bannerSizeId) {
    shippingEstimate.value = null
    return
  }
  shippingLoading.value = true
  try {
    shippingEstimate.value = await calculateShipping({
      postalCode: pc,
      city: city.value.trim() || undefined,
      bannerSizeId: firstItem.bannerSizeId,
      customWidthCm: firstItem.customWidthCm ?? undefined,
      qty: cart.itemCount,
    })
  } catch {
    shippingError.value = 'Kunne ikke beregne frakt. Sjekk postnummeret og prøv igjen.'
    shippingEstimate.value = null
  } finally {
    shippingLoading.value = false
  }
}

let shippingTimer: ReturnType<typeof setTimeout> | null = null
function scheduleShipping() {
  if (shippingTimer) clearTimeout(shippingTimer)
  shippingTimer = setTimeout(computeShipping, 500)
}
watch(postalCode, scheduleShipping)
watch(city, () => {
  if (/^\d{4}$/.test(postalCode.value.trim())) scheduleShipping()
})

// ── Price calculations ───────────────────────────────────────────────────────
const subtotal = computed(() => cart.subtotal)

const shippingCost = computed(() => {
  if (!shippingEstimate.value) return 0
  return deliveryType.value === 'Express'
    ? shippingEstimate.value.express.costNok
    : shippingEstimate.value.standard.costNok
})

const expressFee = computed(() => (deliveryType.value === 'Express' ? 500 : 0))

const total = computed(() => subtotal.value + shippingCost.value + expressFee.value)

// MVA (25%) is included in Norwegian prices. To extract:  total × 0.25 / 1.25 = total × 0.2
const vatAmount = computed(() => total.value * 0.2)

const estimatedDays = computed(() => {
  if (!shippingEstimate.value) return null
  return deliveryType.value === 'Express'
    ? shippingEstimate.value.express.estimatedDays
    : shippingEstimate.value.standard.estimatedDays
})

const estimatedDeliveryText = computed(() => {
  if (estimatedDays.value == null) return null
  const d = new Date()
  d.setDate(d.getDate() + estimatedDays.value)
  return d.toLocaleDateString('nb-NO', { day: '2-digit', month: 'long', year: 'numeric' })
})

// ── Form validation ──────────────────────────────────────────────────────────
const formErrors = ref<Record<string, string>>({})

function validate(): boolean {
  const errs: Record<string, string> = {}
  if (!recipientName.value.trim()) errs.recipientName = 'Navn er påkrevd'
  if (!addressLine1.value.trim()) errs.addressLine1 = 'Adresse er påkrevd'
  if (!/^\d{4}$/.test(postalCode.value.trim())) errs.postalCode = 'Ugyldig postnummer (4 siffer)'
  if (!city.value.trim()) errs.city = 'Poststed er påkrevd'
  formErrors.value = errs
  return Object.keys(errs).length === 0
}

// ── Proceed to payment ───────────────────────────────────────────────────────
function proceed() {
  if (!validate()) return
  if (!shippingEstimate.value) {
    formErrors.value.postalCode = 'Beregn frakt før du fortsetter'
    return
  }
  checkout.setCheckout({
    recipientName: recipientName.value.trim(),
    address: {
      line1: addressLine1.value.trim(),
      postalCode: postalCode.value.trim(),
      city: city.value.trim(),
    },
    deliveryType: deliveryType.value,
    shippingCostNok: shippingCost.value,
    expressFeeNok: expressFee.value,
  })
  router.push('/checkout/payment')
}

// ── Formatting helpers ───────────────────────────────────────────────────────
function formatNok(n: number): string {
  return new Intl.NumberFormat('nb-NO', { maximumFractionDigits: 0 }).format(n) + ' kr'
}
</script>

<template>
  <div class="max-w-6xl mx-auto px-4 py-8 sm:py-12">
    <!-- Header / stepper -->
    <header class="mb-8">
      <h1 class="text-2xl sm:text-3xl font-bold text-gray-900 mb-2">Kasse</h1>
      <nav class="flex items-center gap-2 text-sm">
        <span class="font-semibold text-blue-700">1. Oversikt &amp; levering</span>
        <span class="text-gray-400">›</span>
        <span class="text-gray-400">2. Betaling</span>
        <span class="text-gray-400">›</span>
        <span class="text-gray-400">3. Bekreftelse</span>
      </nav>
    </header>

    <div class="grid lg:grid-cols-3 gap-8">
      <!-- ── Left col: form ─────────────────────────────────────────────── -->
      <div class="lg:col-span-2 space-y-6">

        <!-- Order summary -->
        <section class="bg-white border border-gray-200 rounded-xl p-6">
          <h2 class="text-lg font-semibold text-gray-900 mb-4">Din bestilling</h2>
          <ul class="divide-y divide-gray-100">
            <li
              v-for="(item, idx) in cart.items"
              :key="idx"
              class="flex items-center justify-between py-3"
            >
              <div>
                <div class="font-medium text-gray-900">{{ item.bannerSizeName }}</div>
                <div class="text-sm text-gray-500">{{ item.quantity }} stk × {{ formatNok(item.unitPriceNok) }}</div>
              </div>
              <div class="font-semibold text-gray-900">
                {{ formatNok(item.unitPriceNok * item.quantity) }}
              </div>
            </li>
          </ul>
        </section>

        <!-- Delivery address -->
        <section class="bg-white border border-gray-200 rounded-xl p-6">
          <h2 class="text-lg font-semibold text-gray-900 mb-4">Leveringsadresse</h2>
          <div class="grid sm:grid-cols-2 gap-4">
            <!-- Recipient name -->
            <div class="sm:col-span-2">
              <label class="block text-sm font-medium text-gray-700 mb-1" for="recipientName">
                Mottaker
              </label>
              <input
                id="recipientName"
                v-model="recipientName"
                type="text"
                autocomplete="name"
                placeholder="Fullt navn"
                class="w-full border rounded-lg px-3 py-2 focus:outline-none focus:ring-2 focus:ring-blue-500"
                :class="formErrors.recipientName ? 'border-red-400' : 'border-gray-300'"
              />
              <p v-if="formErrors.recipientName" class="mt-1 text-xs text-red-600">
                {{ formErrors.recipientName }}
              </p>
            </div>

            <!-- Address line 1 -->
            <div class="sm:col-span-2">
              <label class="block text-sm font-medium text-gray-700 mb-1" for="addressLine1">
                Gateadresse
              </label>
              <input
                id="addressLine1"
                v-model="addressLine1"
                type="text"
                autocomplete="address-line1"
                placeholder="Gatenavn og husnummer"
                class="w-full border rounded-lg px-3 py-2 focus:outline-none focus:ring-2 focus:ring-blue-500"
                :class="formErrors.addressLine1 ? 'border-red-400' : 'border-gray-300'"
              />
              <p v-if="formErrors.addressLine1" class="mt-1 text-xs text-red-600">
                {{ formErrors.addressLine1 }}
              </p>
            </div>

            <!-- Postal code -->
            <div>
              <label class="block text-sm font-medium text-gray-700 mb-1" for="postalCode">
                Postnummer
              </label>
              <input
                id="postalCode"
                v-model="postalCode"
                type="text"
                inputmode="numeric"
                maxlength="4"
                autocomplete="postal-code"
                placeholder="0000"
                class="w-full border rounded-lg px-3 py-2 focus:outline-none focus:ring-2 focus:ring-blue-500"
                :class="formErrors.postalCode ? 'border-red-400' : 'border-gray-300'"
              />
              <p v-if="formErrors.postalCode" class="mt-1 text-xs text-red-600">
                {{ formErrors.postalCode }}
              </p>
            </div>

            <!-- City -->
            <div>
              <label class="block text-sm font-medium text-gray-700 mb-1" for="city">
                Poststed
              </label>
              <input
                id="city"
                v-model="city"
                type="text"
                autocomplete="address-level2"
                placeholder="Oslo"
                class="w-full border rounded-lg px-3 py-2 focus:outline-none focus:ring-2 focus:ring-blue-500"
                :class="formErrors.city ? 'border-red-400' : 'border-gray-300'"
              />
              <p v-if="formErrors.city" class="mt-1 text-xs text-red-600">
                {{ formErrors.city }}
              </p>
            </div>
          </div>
        </section>

        <!-- Delivery type -->
        <section class="bg-white border border-gray-200 rounded-xl p-6">
          <h2 class="text-lg font-semibold text-gray-900 mb-4">Leveringstype</h2>
          <div class="grid sm:grid-cols-2 gap-3">
            <!-- Standard -->
            <button
              type="button"
              class="text-left border-2 rounded-xl p-4 transition"
              :class="deliveryType === 'Standard'
                ? 'border-blue-700 bg-blue-50'
                : 'border-gray-200 hover:border-gray-300'"
              @click="deliveryType = 'Standard'"
            >
              <div class="flex items-start justify-between">
                <div>
                  <div class="font-semibold text-gray-900">Standard</div>
                  <div class="text-sm text-gray-600 mt-0.5">
                    Estimert levering: ca. 2 uker
                  </div>
                  <div v-if="shippingEstimate && deliveryType === 'Standard'" class="text-sm text-gray-500 mt-1">
                    {{ estimatedDeliveryText }} ({{ shippingEstimate.standard.estimatedDays }} virkedager)
                  </div>
                </div>
                <div class="ml-3 shrink-0">
                  <div
                    class="w-5 h-5 rounded-full border-2 flex items-center justify-center mt-0.5"
                    :class="deliveryType === 'Standard' ? 'border-blue-700' : 'border-gray-300'"
                  >
                    <div v-if="deliveryType === 'Standard'" class="w-2.5 h-2.5 rounded-full bg-blue-700" />
                  </div>
                </div>
              </div>
              <div v-if="shippingEstimate" class="mt-2 font-bold text-blue-700">
                {{ formatNok(shippingEstimate.standard.costNok) }}
              </div>
              <div v-else-if="shippingLoading" class="mt-2 text-sm text-gray-400">Beregner…</div>
              <div v-else class="mt-2 text-sm text-gray-400">Skriv inn postnummer for pris</div>
            </button>

            <!-- Express -->
            <button
              type="button"
              class="text-left border-2 rounded-xl p-4 transition"
              :class="deliveryType === 'Express'
                ? 'border-yellow-500 bg-yellow-50'
                : 'border-gray-200 hover:border-gray-300'"
              @click="deliveryType = 'Express'"
            >
              <div class="flex items-start justify-between">
                <div>
                  <div class="font-semibold text-gray-900">
                    Ekspress
                    <span class="ml-1 text-xs font-normal bg-yellow-200 text-yellow-900 px-1.5 py-0.5 rounded-full">
                      +500 kr gebyr
                    </span>
                  </div>
                  <div class="text-sm text-gray-600 mt-0.5">
                    Estimert levering: ca. 3 dager
                  </div>
                  <div v-if="shippingEstimate && deliveryType === 'Express'" class="text-sm text-gray-500 mt-1">
                    {{ estimatedDeliveryText }} ({{ shippingEstimate.express.estimatedDays }} virkedager)
                  </div>
                </div>
                <div class="ml-3 shrink-0">
                  <div
                    class="w-5 h-5 rounded-full border-2 flex items-center justify-center mt-0.5"
                    :class="deliveryType === 'Express' ? 'border-yellow-500' : 'border-gray-300'"
                  >
                    <div v-if="deliveryType === 'Express'" class="w-2.5 h-2.5 rounded-full bg-yellow-500" />
                  </div>
                </div>
              </div>
              <div v-if="shippingEstimate" class="mt-2 font-bold text-yellow-700">
                {{ formatNok(shippingEstimate.express.costNok) }}
                <span class="text-xs font-normal text-gray-600">frakt + 500 kr gebyr</span>
              </div>
              <div v-else-if="shippingLoading" class="mt-2 text-sm text-gray-400">Beregner…</div>
              <div v-else class="mt-2 text-sm text-gray-400">Skriv inn postnummer for pris</div>
            </button>
          </div>

          <p v-if="shippingError" class="mt-3 text-sm text-red-600">{{ shippingError }}</p>
        </section>
      </div>

      <!-- ── Right col: order total ──────────────────────────────────────── -->
      <aside class="space-y-4">
        <div class="bg-white border border-gray-200 rounded-xl p-6 sticky top-4">
          <h2 class="text-lg font-semibold text-gray-900 mb-4">Ordresammendrag</h2>

          <dl class="space-y-2 text-sm">
            <div class="flex justify-between">
              <dt class="text-gray-600">Varer ({{ cart.itemCount }} stk)</dt>
              <dd class="font-medium text-gray-900">{{ formatNok(subtotal) }}</dd>
            </div>

            <div class="flex justify-between">
              <dt class="text-gray-600">
                Frakt ({{ deliveryType === 'Express' ? 'ekspress' : 'standard' }})
              </dt>
              <dd class="font-medium text-gray-900">
                <span v-if="shippingLoading" class="text-gray-400">Beregner…</span>
                <span v-else-if="shippingEstimate">{{ formatNok(shippingCost) }}</span>
                <span v-else class="text-gray-400">–</span>
              </dd>
            </div>

            <div v-if="deliveryType === 'Express'" class="flex justify-between">
              <dt class="text-gray-600">Ekspress produksjonsgebyr</dt>
              <dd class="font-medium text-gray-900">{{ formatNok(expressFee) }}</dd>
            </div>

            <div class="border-t border-gray-200 pt-3 mt-3">
              <div class="flex justify-between text-base font-bold">
                <dt class="text-gray-900">Totalt inkl. MVA</dt>
                <dd class="text-blue-700">{{ formatNok(total) }}</dd>
              </div>
              <div class="flex justify-between text-xs text-gray-500 mt-1">
                <dt>Herav MVA (25%)</dt>
                <dd>{{ formatNok(vatAmount) }}</dd>
              </div>
            </div>
          </dl>

          <div v-if="estimatedDeliveryText && shippingEstimate" class="mt-4 p-3 bg-green-50 border border-green-200 rounded-lg text-sm text-green-800">
            <div class="font-medium">Estimert levering</div>
            <div>{{ estimatedDeliveryText }}</div>
          </div>

          <p v-if="!shippingEstimate" class="mt-3 text-xs text-gray-500">
            Skriv inn leveringsadresse for å se fraktkostnader.
          </p>

          <button
            type="button"
            class="mt-5 w-full bg-blue-700 hover:bg-blue-800 disabled:bg-gray-300 disabled:cursor-not-allowed text-white font-semibold py-3 rounded-lg transition"
            @click="proceed"
          >
            Gå til betaling →
          </button>
        </div>
      </aside>
    </div>
  </div>
</template>

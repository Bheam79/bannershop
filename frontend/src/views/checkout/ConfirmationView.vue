<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { useRoute, RouterLink } from 'vue-router'
import { getOrder } from '@/api/orders'
import type { OrderDetailResponse } from '@/api/orders'

const route = useRoute()
const orderId = Number(route.params.orderId)

const order = ref<OrderDetailResponse | null>(null)
const loading = ref(true)
const error = ref<string | null>(null)

onMounted(async () => {
  try {
    order.value = await getOrder(orderId)
  } catch {
    error.value = 'Kunne ikke hente ordredetaljer. Sjekk Mine ordrer for å se statusen.'
  } finally {
    loading.value = false
  }
})

function formatNok(n: number): string {
  return new Intl.NumberFormat('nb-NO', { maximumFractionDigits: 0 }).format(n) + ' kr'
}

function formatDate(isoDate: string | null): string {
  if (!isoDate) return '—'
  return new Date(isoDate).toLocaleDateString('nb-NO', {
    day: '2-digit',
    month: 'long',
    year: 'numeric',
  })
}
</script>

<template>
  <div class="max-w-3xl mx-auto px-4 py-12">
    <!-- Header / stepper -->
    <nav class="flex items-center gap-2 text-sm mb-8 text-gray-500">
      <span>1. Oversikt &amp; levering</span>
      <span class="text-gray-300">›</span>
      <span>2. Betaling</span>
      <span class="text-gray-300">›</span>
      <span class="font-semibold text-blue-700">3. Bekreftelse</span>
    </nav>

    <!-- Loading -->
    <div v-if="loading" class="text-center py-16 text-gray-500">
      <div class="inline-block w-8 h-8 border-4 border-blue-600 border-t-transparent rounded-full animate-spin mb-4" />
      <p>Laster ordredetaljer…</p>
    </div>

    <template v-else>
      <!-- Thank you banner -->
      <div class="bg-green-50 border border-green-200 rounded-2xl p-8 text-center mb-8">
        <div class="text-5xl mb-3">🎉</div>
        <h1 class="text-2xl sm:text-3xl font-bold text-green-900 mb-2">
          Tusen takk for din bestilling!
        </h1>
        <p class="text-green-800">
          Din ordre er mottatt og er under behandling.
        </p>
      </div>

      <!-- Error fallback -->
      <div v-if="error" class="bg-amber-50 border border-amber-200 rounded-xl p-5 text-amber-800 mb-6">
        {{ error }}
      </div>

      <template v-if="order">
        <!-- Order number + status -->
        <div class="bg-white border border-gray-200 rounded-xl p-6 mb-4">
          <div class="grid sm:grid-cols-3 gap-4 text-center sm:text-left">
            <div>
              <div class="text-xs font-semibold uppercase tracking-wider text-gray-500 mb-1">Ordrenummer</div>
              <div class="text-2xl font-bold text-gray-900">#{{ order.id }}</div>
            </div>
            <div>
              <div class="text-xs font-semibold uppercase tracking-wider text-gray-500 mb-1">Status</div>
              <div class="text-base font-medium text-blue-700">
                <span v-if="order.status === 'PendingPayment'">Venter på betaling</span>
                <span v-else-if="order.status === 'Paid'">Betalt ✓</span>
                <span v-else-if="order.status === 'InProduction'">Under produksjon</span>
                <span v-else>{{ order.status }}</span>
              </div>
            </div>
            <div>
              <div class="text-xs font-semibold uppercase tracking-wider text-gray-500 mb-1">Estimert levering</div>
              <div class="text-base font-medium text-gray-900">
                {{ formatDate(order.estimatedDelivery) }}
              </div>
            </div>
          </div>
        </div>

        <!-- Order items -->
        <div class="bg-white border border-gray-200 rounded-xl p-6 mb-4">
          <h2 class="text-base font-semibold text-gray-900 mb-3">Bestilte varer</h2>
          <ul class="divide-y divide-gray-100">
            <li
              v-for="item in order.items"
              :key="item.id"
              class="flex items-center justify-between py-3 text-sm"
            >
              <div>
                <div class="font-medium text-gray-900">{{ item.bannerSizeName }}</div>
                <div class="text-gray-500">{{ item.quantity }} stk × {{ formatNok(item.unitPriceNok) }}</div>
              </div>
              <div class="font-semibold text-gray-900">{{ formatNok(item.lineTotalNok) }}</div>
            </li>
          </ul>

          <!-- Price breakdown -->
          <dl class="mt-4 border-t border-gray-100 pt-4 space-y-1.5 text-sm">
            <div class="flex justify-between">
              <dt class="text-gray-600">Frakt</dt>
              <dd class="font-medium">{{ formatNok(order.shippingCostNok) }}</dd>
            </div>
            <div v-if="order.expressFeeNok > 0" class="flex justify-between">
              <dt class="text-gray-600">Ekspressgebyr</dt>
              <dd class="font-medium">{{ formatNok(order.expressFeeNok) }}</dd>
            </div>
            <div class="flex justify-between font-bold text-base border-t border-gray-200 pt-2 mt-1">
              <dt class="text-gray-900">Totalt inkl. MVA</dt>
              <dd class="text-blue-700">{{ formatNok(order.totalNok) }}</dd>
            </div>
            <div class="flex justify-between text-xs text-gray-500">
              <dt>Herav MVA (25%)</dt>
              <dd>{{ formatNok(order.totalNok * 0.2) }}</dd>
            </div>
          </dl>
        </div>

        <!-- Delivery address -->
        <div v-if="order.shippingAddress" class="bg-white border border-gray-200 rounded-xl p-6 mb-4">
          <h2 class="text-base font-semibold text-gray-900 mb-2">Leveringsadresse</h2>
          <address class="not-italic text-sm text-gray-700 space-y-0.5">
            <div>{{ order.shippingAddress.line1 }}</div>
            <div v-if="order.shippingAddress.line2">{{ order.shippingAddress.line2 }}</div>
            <div>{{ order.shippingAddress.postalCode }} {{ order.shippingAddress.city }}</div>
          </address>
        </div>
      </template>

      <!-- Actions -->
      <div class="flex flex-col sm:flex-row gap-3 mt-6">
        <RouterLink
          to="/account/orders"
          class="flex-1 text-center bg-blue-700 hover:bg-blue-800 text-white font-semibold py-3 px-5 rounded-lg transition"
        >
          Følg ordren din →
        </RouterLink>
        <RouterLink
          to="/"
          class="flex-1 text-center bg-white border border-gray-300 hover:bg-gray-50 text-gray-700 font-semibold py-3 px-5 rounded-lg transition"
        >
          Fortsett å handle
        </RouterLink>
      </div>
    </template>
  </div>
</template>

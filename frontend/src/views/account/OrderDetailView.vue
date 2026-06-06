<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useRoute, RouterLink } from 'vue-router'
import { getOrder } from '@/api/orders'
import type { OrderDetailResponse, OrderItemDetail, ProductionStatusEntry } from '@/api/orders'

const route = useRoute()
const orderId = Number(route.params.id)

const order = ref<OrderDetailResponse | null>(null)
const loading = ref(true)
const error = ref<string | null>(null)

onMounted(async () => {
  try {
    order.value = await getOrder(orderId)
  } catch {
    error.value = 'Kunne ikke laste ordredetaljer. Prøv igjen.'
  } finally {
    loading.value = false
  }
})

// ── Formatting ────────────────────────────────────────────────────────────────
function formatNok(n: number): string {
  return new Intl.NumberFormat('nb-NO', { maximumFractionDigits: 0 }).format(n) + ' kr'
}
function formatDate(iso: string | null | undefined): string {
  if (!iso) return '—'
  return new Date(iso).toLocaleDateString('nb-NO', { day: '2-digit', month: 'long', year: 'numeric' })
}
function formatDateTime(iso: string | null | undefined): string {
  if (!iso) return '—'
  return new Date(iso).toLocaleString('nb-NO', {
    day: '2-digit', month: 'short', year: 'numeric',
    hour: '2-digit', minute: '2-digit',
  })
}

// ── Status helpers ────────────────────────────────────────────────────────────
const STATUS_LABELS: Record<string, string> = {
  Draft: 'Utkast',
  PendingPayment: 'Venter betaling',
  Paid: 'Betalt',
  InProduction: 'Under produksjon',
  ReadyToShip: 'Klar til frakt',
  Shipped: 'Sendt',
  Delivered: 'Levert',
  Cancelled: 'Kansellert',
}
const STATUS_CLASSES: Record<string, string> = {
  Draft: 'bg-gray-100 text-gray-600',
  PendingPayment: 'bg-yellow-100 text-yellow-800',
  Paid: 'bg-blue-100 text-blue-800',
  InProduction: 'bg-blue-100 text-blue-800',
  ReadyToShip: 'bg-purple-100 text-purple-800',
  Shipped: 'bg-green-100 text-green-800',
  Delivered: 'bg-green-100 text-green-700',
  Cancelled: 'bg-red-100 text-red-700',
}

function statusLabel(s: string) { return STATUS_LABELS[s] ?? s }
function statusClass(s: string) { return STATUS_CLASSES[s] ?? 'bg-gray-100 text-gray-600' }

// ── Production stage helpers ──────────────────────────────────────────────────
const PRODUCTION_STEPS = [
  { key: 'Queued',      label: 'I kø' },
  { key: 'Printing',    label: 'Trykking' },
  { key: 'Finishing',   label: 'Etterbehandling' },
  { key: 'ReadyToShip', label: 'Klar til frakt' },
]

function stageIndex(stage: string): number {
  return PRODUCTION_STEPS.findIndex(s => s.key === stage)
}

function getHistoryEntry(item: OrderItemDetail, stage: string): ProductionStatusEntry | null {
  return (
    [...item.productionStatusHistory]
      .filter(e => e.stage === stage)
      .sort((a, b) => new Date(b.updatedAt).getTime() - new Date(a.updatedAt).getTime())[0] ?? null
  )
}

function itemLabel(item: OrderItemDetail): string {
  if (item.bannerSizeName) return item.bannerSizeName
  if (item.customWidthCm) return `${item.customWidthCm} × ${item.heightCm} cm`
  return `Banner ${item.heightCm} cm høy`
}

// ── Shipping helpers ──────────────────────────────────────────────────────────
const isShipped = computed(() =>
  order.value?.status === 'Shipped' || order.value?.status === 'Delivered'
)

const deliveryLabel = computed(() =>
  order.value?.deliveryType === 'Express' ? 'Ekspress' : 'Standard'
)
</script>

<template>
  <div class="max-w-4xl mx-auto px-4 py-10">
    <div class="flex items-center gap-2 mb-6 text-sm">
      <RouterLink to="/account/orders" class="text-blue-700 hover:underline">Mine ordrer</RouterLink>
      <span class="text-gray-400">›</span>
      <span class="text-gray-600">Ordre #{{ orderId }}</span>
    </div>

    <!-- Loading -->
    <div v-if="loading" class="flex justify-center py-16">
      <div class="w-8 h-8 border-4 border-blue-600 border-t-transparent rounded-full animate-spin" />
    </div>

    <!-- Error -->
    <div v-else-if="error" class="bg-red-50 border border-red-200 text-red-800 rounded-xl p-6 text-center">
      {{ error }}
    </div>

    <template v-else-if="order">
      <!-- ── Order header ─────────────────────────────────────────────────── -->
      <div class="bg-white border border-gray-200 rounded-xl p-6 mb-5">
        <div class="flex flex-wrap items-start justify-between gap-4">
          <div>
            <h1 class="text-xl font-bold text-gray-900">Ordre #{{ order.id }}</h1>
            <p class="text-sm text-gray-500 mt-0.5">Bestilt {{ formatDate(order.createdAt) }}</p>
          </div>
          <span
            class="text-sm font-semibold px-3 py-1.5 rounded-full"
            :class="statusClass(order.status)"
          >
            {{ statusLabel(order.status) }}
          </span>
        </div>

        <div class="grid sm:grid-cols-3 gap-4 mt-5 text-sm">
          <div>
            <div class="text-xs font-semibold uppercase tracking-wide text-gray-400 mb-0.5">Leveringstype</div>
            <div class="font-medium text-gray-800">{{ deliveryLabel }}</div>
          </div>
          <div>
            <div class="text-xs font-semibold uppercase tracking-wide text-gray-400 mb-0.5">Estimert levering</div>
            <div class="font-medium text-gray-800">{{ formatDate(order.estimatedDelivery) }}</div>
          </div>
          <div>
            <div class="text-xs font-semibold uppercase tracking-wide text-gray-400 mb-0.5">Totalt inkl. MVA</div>
            <div class="font-bold text-blue-700 text-base">{{ formatNok(order.totalNok) }}</div>
          </div>
        </div>
      </div>

      <!-- ── Production tracking (per item) ─────────────────────────────── -->
      <section v-if="order.status !== 'Cancelled' && order.status !== 'PendingPayment' && order.status !== 'Draft'"
               class="mb-5">
        <h2 class="text-base font-semibold text-gray-900 mb-3">Produksjonsstatus</h2>

        <div
          v-for="item in order.items"
          :key="item.id"
          class="bg-white border border-gray-200 rounded-xl p-5 mb-3"
        >
          <!-- Item header -->
          <div class="flex items-start justify-between gap-2 mb-5">
            <div>
              <div class="font-medium text-gray-900">{{ itemLabel(item) }}</div>
              <div class="text-sm text-gray-500">{{ item.quantity }} stk</div>
            </div>
            <div class="text-sm font-semibold text-gray-700 shrink-0">
              {{ formatNok(item.lineTotalNok) }}
            </div>
          </div>

          <!-- Progress stepper -->
          <div class="relative">
            <!-- Connecting line -->
            <div class="absolute top-4 left-0 right-0 h-0.5 bg-gray-200" aria-hidden="true">
              <div
                class="h-full bg-blue-600 transition-all duration-500"
                :style="{
                  width: `${Math.min(stageIndex(item.currentProductionStage) / (PRODUCTION_STEPS.length - 1), 1) * 100}%`
                }"
              />
            </div>

            <ol class="relative flex justify-between">
              <li
                v-for="(step, idx) in PRODUCTION_STEPS"
                :key="step.key"
                class="flex flex-col items-center"
                :style="{ width: `${100 / PRODUCTION_STEPS.length}%` }"
              >
                <!-- Circle -->
                <div
                  class="w-8 h-8 rounded-full border-2 flex items-center justify-center relative z-10 transition-colors duration-300"
                  :class="idx <= stageIndex(item.currentProductionStage)
                    ? 'bg-blue-600 border-blue-600'
                    : 'bg-white border-gray-300'"
                >
                  <svg
                    v-if="idx < stageIndex(item.currentProductionStage)"
                    class="w-4 h-4 text-white"
                    fill="none" viewBox="0 0 24 24" stroke="currentColor" stroke-width="3"
                  >
                    <path stroke-linecap="round" stroke-linejoin="round" d="M5 13l4 4L19 7" />
                  </svg>
                  <div
                    v-else-if="idx === stageIndex(item.currentProductionStage)"
                    class="w-3 h-3 rounded-full bg-white"
                  />
                </div>

                <!-- Step label -->
                <div class="mt-2 text-center px-1">
                  <div
                    class="text-xs font-semibold leading-tight"
                    :class="idx === stageIndex(item.currentProductionStage)
                      ? 'text-blue-700'
                      : idx < stageIndex(item.currentProductionStage)
                        ? 'text-gray-600'
                        : 'text-gray-400'"
                  >
                    {{ step.label }}
                  </div>
                  <!-- Timestamp for completed / current steps -->
                  <div
                    v-if="getHistoryEntry(item, step.key)"
                    class="text-xs text-gray-400 mt-0.5 leading-tight"
                  >
                    {{ formatDateTime(getHistoryEntry(item, step.key)!.updatedAt) }}
                  </div>
                </div>
              </li>
            </ol>
          </div>

          <!-- Notes from most recent stage entry -->
          <div
            v-if="item.productionStatusHistory.length > 0 && item.productionStatusHistory.slice(-1)[0]?.notes"
            class="mt-4 text-sm text-gray-600 bg-gray-50 rounded-lg px-3 py-2"
          >
            <span class="font-medium">Merknad: </span>
            {{ item.productionStatusHistory.slice(-1)[0]?.notes }}
          </div>
        </div>
      </section>

      <!-- ── Shipping tracking ──────────────────────────────────────────── -->
      <section v-if="isShipped && order.shipmentTracking" class="mb-5">
        <h2 class="text-base font-semibold text-gray-900 mb-3">Fraktstatus</h2>
        <div class="bg-white border border-gray-200 rounded-xl p-6">
          <div class="grid sm:grid-cols-2 gap-5 text-sm">
            <div>
              <div class="text-xs font-semibold uppercase tracking-wide text-gray-400 mb-1">Transportør</div>
              <div class="font-medium text-gray-800">{{ order.shipmentTracking.carrier }}</div>
            </div>
            <div>
              <div class="text-xs font-semibold uppercase tracking-wide text-gray-400 mb-1">Sporingsnummer</div>
              <div class="font-medium">
                <a
                  v-if="order.shipmentTracking.trackingUrl"
                  :href="order.shipmentTracking.trackingUrl"
                  target="_blank"
                  rel="noopener"
                  class="text-blue-700 hover:underline"
                >
                  {{ order.shipmentTracking.trackingNumber }} ↗
                </a>
                <span v-else class="text-gray-800">{{ order.shipmentTracking.trackingNumber }}</span>
              </div>
            </div>
            <div>
              <div class="text-xs font-semibold uppercase tracking-wide text-gray-400 mb-1">Sendt dato</div>
              <div class="font-medium text-gray-800">{{ formatDate(order.shipmentTracking.shippedAt) }}</div>
            </div>
            <div>
              <div class="text-xs font-semibold uppercase tracking-wide text-gray-400 mb-1">Estimert ankomst</div>
              <div class="font-medium text-gray-800">{{ formatDate(order.shipmentTracking.estimatedArrival) }}</div>
            </div>
            <div v-if="order.shipmentTracking.deliveredAt" class="sm:col-span-2">
              <div class="text-xs font-semibold uppercase tracking-wide text-gray-400 mb-1">Levert</div>
              <div class="font-medium text-green-700">{{ formatDate(order.shipmentTracking.deliveredAt) }} ✓</div>
            </div>
          </div>
        </div>
      </section>

      <!-- ── Order items + price breakdown ─────────────────────────────── -->
      <section class="mb-5">
        <h2 class="text-base font-semibold text-gray-900 mb-3">Varer</h2>
        <div class="bg-white border border-gray-200 rounded-xl overflow-hidden">
          <ul class="divide-y divide-gray-100">
            <li
              v-for="item in order.items"
              :key="item.id"
              class="flex items-center justify-between px-5 py-4 text-sm"
            >
              <div>
                <div class="font-medium text-gray-900">{{ itemLabel(item) }}</div>
                <div class="text-gray-500 mt-0.5">{{ item.quantity }} stk × {{ formatNok(item.unitPriceNok) }}</div>
              </div>
              <div class="font-semibold text-gray-900 ml-4 shrink-0">{{ formatNok(item.lineTotalNok) }}</div>
            </li>
          </ul>

          <!-- Price breakdown -->
          <dl class="border-t border-gray-100 px-5 py-4 space-y-2 text-sm">
            <div class="flex justify-between">
              <dt class="text-gray-600">Frakt</dt>
              <dd class="font-medium text-gray-900">{{ formatNok(order.shippingCostNok) }}</dd>
            </div>
            <div v-if="order.expressFeeNok > 0" class="flex justify-between">
              <dt class="text-gray-600">Ekspressgebyr</dt>
              <dd class="font-medium text-gray-900">{{ formatNok(order.expressFeeNok) }}</dd>
            </div>
            <div class="flex justify-between font-bold text-base pt-2 border-t border-gray-200">
              <dt class="text-gray-900">Totalt inkl. MVA</dt>
              <dd class="text-blue-700">{{ formatNok(order.totalNok) }}</dd>
            </div>
            <div class="flex justify-between text-xs text-gray-500">
              <dt>Herav MVA (25%)</dt>
              <dd>{{ formatNok(order.totalNok * 0.2) }}</dd>
            </div>
          </dl>
        </div>
      </section>

      <!-- ── Shipping address ───────────────────────────────────────────── -->
      <section v-if="order.shippingAddress" class="mb-5">
        <h2 class="text-base font-semibold text-gray-900 mb-3">Leveringsadresse</h2>
        <div class="bg-white border border-gray-200 rounded-xl px-5 py-4 text-sm">
          <address class="not-italic text-gray-700 space-y-0.5">
            <div>{{ order.shippingAddress.line1 }}</div>
            <div v-if="order.shippingAddress.line2">{{ order.shippingAddress.line2 }}</div>
            <div>{{ order.shippingAddress.postalCode }} {{ order.shippingAddress.city }}</div>
          </address>
        </div>
      </section>

      <RouterLink to="/account/orders" class="text-sm text-blue-700 hover:underline">← Tilbake til ordrelisten</RouterLink>
    </template>
  </div>
</template>

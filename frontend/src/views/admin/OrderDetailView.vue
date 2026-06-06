<script setup lang="ts">
import { ref, reactive, computed, onMounted } from 'vue'
import { useRoute, RouterLink } from 'vue-router'
import {
  getAdminOrder,
  updateOrderStatus,
  updateProductionStage,
  setShipping,
} from '@/api/admin'
import type { OrderDetailResponse, OrderItemDetail } from '@/api/orders'

const route = useRoute()
const orderId = Number(route.params.id)

// ── Order data ────────────────────────────────────────────────────────────────
const order = ref<OrderDetailResponse | null>(null)
const loading = ref(true)
const loadError = ref<string | null>(null)

async function loadOrder() {
  loading.value = true
  loadError.value = null
  try {
    order.value = await getAdminOrder(orderId)
    syncForms()
  } catch {
    loadError.value = 'Kunne ikke laste ordre.'
  } finally {
    loading.value = false
  }
}

onMounted(loadOrder)

// ── Sync form state from loaded order ────────────────────────────────────────
function syncForms() {
  if (!order.value) return
  newStatus.value = order.value.status

  // Init per-item production forms
  for (const item of order.value.items) {
    if (!prodForms.value[item.id]) {
      prodForms.value[item.id] = { stage: item.currentProductionStage, notes: '' }
    } else {
      prodForms.value[item.id]!.stage = item.currentProductionStage
    }
  }

  // Pre-fill shipping from existing tracking
  if (order.value.shipmentTracking) {
    const t = order.value.shipmentTracking
    shipForm.carrier = t.carrier
    shipForm.trackingNumber = t.trackingNumber
    shipForm.trackingUrl = t.trackingUrl ?? ''
    shipForm.shippedAt = t.shippedAt ? t.shippedAt.slice(0, 10) : todayStr()
    shipForm.estimatedArrival = t.estimatedArrival ? t.estimatedArrival.slice(0, 10) : ''
  }
}

function todayStr(): string {
  return new Date().toISOString().slice(0, 10)
}

// ── Status update ─────────────────────────────────────────────────────────────
const newStatus = ref('')
const statusSaving = ref(false)
const statusError = ref('')
const statusSuccess = ref('')

async function saveStatus() {
  if (!newStatus.value) return
  statusSaving.value = true
  statusError.value = ''
  statusSuccess.value = ''
  try {
    order.value = await updateOrderStatus(orderId, newStatus.value)
    syncForms()
    statusSuccess.value = 'Status oppdatert.'
    setTimeout(() => { statusSuccess.value = '' }, 3000)
  } catch (err: unknown) {
    const e = err as { response?: { data?: { error?: string } } }
    statusError.value = e.response?.data?.error ?? 'Lagring feilet.'
  } finally {
    statusSaving.value = false
  }
}

// ── Production stage update ───────────────────────────────────────────────────
const prodForms = ref<Record<number, { stage: string; notes: string }>>({})
const prodSaving = ref<Record<number, boolean>>({})
const prodError = ref<Record<number, string>>({})
const prodSuccess = ref<Record<number, string>>({})

async function saveProd(item: OrderItemDetail) {
  const f = prodForms.value[item.id]
  if (!f) return
  prodSaving.value[item.id] = true
  prodError.value[item.id] = ''
  prodSuccess.value[item.id] = ''
  try {
    order.value = await updateProductionStage(orderId, item.id, f.stage, f.notes || undefined)
    syncForms()
    prodForms.value[item.id]!.notes = ''
    prodSuccess.value[item.id] = 'Oppdatert.'
    setTimeout(() => { prodSuccess.value[item.id] = '' }, 3000)
  } catch (err: unknown) {
    const e = err as { response?: { data?: { error?: string } } }
    prodError.value[item.id] = e.response?.data?.error ?? 'Lagring feilet.'
  } finally {
    prodSaving.value[item.id] = false
  }
}

// ── Shipping form ─────────────────────────────────────────────────────────────
const shipForm = reactive({
  carrier: 'Bring',
  trackingNumber: '',
  trackingUrl: '',
  shippedAt: todayStr(),
  estimatedArrival: '',
})
const shipSaving = ref(false)
const shipError = ref('')
const shipSuccess = ref('')

async function saveShipping() {
  if (!shipForm.carrier.trim() || !shipForm.trackingNumber.trim()) {
    shipError.value = 'Transportør og sporingsnummer er påkrevd.'
    return
  }
  shipSaving.value = true
  shipError.value = ''
  shipSuccess.value = ''
  try {
    order.value = await setShipping(orderId, {
      carrier: shipForm.carrier.trim(),
      trackingNumber: shipForm.trackingNumber.trim(),
      trackingUrl: shipForm.trackingUrl.trim() || undefined,
      shippedAt: shipForm.shippedAt ? new Date(shipForm.shippedAt).toISOString() : undefined,
      estimatedArrival: shipForm.estimatedArrival ? new Date(shipForm.estimatedArrival).toISOString() : undefined,
    })
    syncForms()
    shipSuccess.value = 'Fraktinfo lagret. Ordrestatus satt til Sendt.'
    setTimeout(() => { shipSuccess.value = '' }, 4000)
  } catch (err: unknown) {
    const e = err as { response?: { data?: { error?: string } } }
    shipError.value = e.response?.data?.error ?? 'Lagring feilet.'
  } finally {
    shipSaving.value = false
  }
}

// ── Status / label helpers ────────────────────────────────────────────────────
const ORDER_STATUSES = [
  { value: 'Draft', label: 'Utkast' },
  { value: 'PendingPayment', label: 'Venter betaling' },
  { value: 'Paid', label: 'Betalt' },
  { value: 'InProduction', label: 'Under produksjon' },
  { value: 'ReadyToShip', label: 'Klar til frakt' },
  { value: 'Shipped', label: 'Sendt' },
  { value: 'Delivered', label: 'Levert' },
  { value: 'Cancelled', label: 'Kansellert' },
]
const PRODUCTION_STAGES = [
  { value: 'Queued', label: 'I kø' },
  { value: 'Printing', label: 'Trykking' },
  { value: 'Finishing', label: 'Etterbehandling' },
  { value: 'ReadyToShip', label: 'Klar til frakt' },
]
const STATUS_LABELS: Record<string, string> = Object.fromEntries(ORDER_STATUSES.map(s => [s.value, s.label]))
const STATUS_CLASSES: Record<string, string> = {
  Draft: 'bg-gray-100 text-gray-600', PendingPayment: 'bg-yellow-100 text-yellow-800',
  Paid: 'bg-blue-100 text-blue-800', InProduction: 'bg-blue-100 text-blue-800',
  ReadyToShip: 'bg-purple-100 text-purple-800', Shipped: 'bg-green-100 text-green-800',
  Delivered: 'bg-green-100 text-green-700', Cancelled: 'bg-red-100 text-red-700',
}
function statusLabel(s: string) { return STATUS_LABELS[s] ?? s }
function statusClass(s: string) { return STATUS_CLASSES[s] ?? 'bg-gray-100 text-gray-600' }
function prodStageLabel(s: string) {
  return PRODUCTION_STAGES.find(x => x.value === s)?.label ?? s
}
function itemLabel(item: OrderItemDetail): string {
  if (item.bannerSizeName) return item.bannerSizeName
  if (item.customWidthCm) return `${item.customWidthCm} × ${item.heightCm} cm`
  return `Banner ${item.heightCm} cm`
}
function formatNok(n: number): string {
  return new Intl.NumberFormat('nb-NO', { maximumFractionDigits: 0 }).format(n) + ' kr'
}
function formatDate(iso: string | null | undefined): string {
  if (!iso) return '—'
  return new Date(iso).toLocaleDateString('nb-NO', { day: '2-digit', month: 'long', year: 'numeric' })
}
function formatDateTime(iso: string | null | undefined): string {
  if (!iso) return '—'
  return new Date(iso).toLocaleString('nb-NO', { day: '2-digit', month: 'short', year: 'numeric', hour: '2-digit', minute: '2-digit' })
}

const deliveryLabel = computed(() =>
  order.value?.deliveryType === 'Express' ? 'Ekspress' : 'Standard'
)
</script>

<template>
  <div class="max-w-5xl mx-auto px-4 py-8">
    <!-- Breadcrumb -->
    <div class="flex items-center gap-2 mb-6 text-sm">
      <RouterLink to="/admin/orders" class="text-blue-600 hover:underline">Ordrer</RouterLink>
      <span class="text-gray-400">›</span>
      <span class="text-gray-600">Ordre #{{ orderId }}</span>
    </div>

    <!-- Loading -->
    <div v-if="loading" class="flex justify-center py-16">
      <div class="w-8 h-8 border-4 border-blue-600 border-t-transparent rounded-full animate-spin" />
    </div>
    <div v-else-if="loadError" class="bg-red-50 border border-red-200 text-red-800 rounded-xl p-6 text-center">
      {{ loadError }}
    </div>

    <template v-else-if="order">
      <!-- ── Order header ─────────────────────────────────────────────────── -->
      <div class="bg-white border border-gray-200 rounded-xl p-6 mb-5">
        <div class="flex flex-wrap items-start justify-between gap-4">
          <div>
            <h1 class="text-xl font-bold text-gray-900">Ordre #{{ order.id }}</h1>
            <p class="text-sm text-gray-400 mt-0.5">Opprettet {{ formatDateTime(order.createdAt) }}</p>
          </div>
          <span class="text-sm font-semibold px-3 py-1.5 rounded-full" :class="statusClass(order.status)">
            {{ statusLabel(order.status) }}
          </span>
        </div>
        <div class="grid sm:grid-cols-4 gap-4 mt-5 text-sm">
          <div>
            <div class="text-xs font-semibold uppercase tracking-wide text-gray-400 mb-0.5">Leveringstype</div>
            <div class="font-medium">{{ deliveryLabel }}</div>
          </div>
          <div>
            <div class="text-xs font-semibold uppercase tracking-wide text-gray-400 mb-0.5">Estimert levering</div>
            <div class="font-medium">{{ formatDate(order.estimatedDelivery) }}</div>
          </div>
          <div>
            <div class="text-xs font-semibold uppercase tracking-wide text-gray-400 mb-0.5">Totalt inkl. MVA</div>
            <div class="font-bold text-blue-700">{{ formatNok(order.totalNok) }}</div>
          </div>
          <div>
            <div class="text-xs font-semibold uppercase tracking-wide text-gray-400 mb-0.5">Sist oppdatert</div>
            <div class="font-medium text-gray-600">{{ formatDateTime(order.updatedAt) }}</div>
          </div>
        </div>
      </div>

      <!-- ── Customer info ──────────────────────────────────────────────── -->
      <div class="bg-white border border-gray-200 rounded-xl p-6 mb-5">
        <h2 class="text-base font-semibold text-gray-900 mb-3">Kundeinformasjon</h2>
        <div class="grid sm:grid-cols-2 gap-4 text-sm">
          <div>
            <div class="text-xs font-semibold uppercase tracking-wide text-gray-400 mb-0.5">Navn</div>
            <div class="font-medium">{{ order.customerName ?? '—' }}</div>
          </div>
          <div>
            <div class="text-xs font-semibold uppercase tracking-wide text-gray-400 mb-0.5">E-post</div>
            <div>
              <a v-if="order.customerEmail" :href="`mailto:${order.customerEmail}`"
                 class="text-blue-600 hover:underline">
                {{ order.customerEmail }}
              </a>
              <span v-else>—</span>
            </div>
          </div>
          <div v-if="order.shippingAddress">
            <div class="text-xs font-semibold uppercase tracking-wide text-gray-400 mb-0.5">Leveringsadresse</div>
            <address class="not-italic text-gray-700 space-y-0.5">
              <div>{{ order.shippingAddress.line1 }}</div>
              <div v-if="order.shippingAddress.line2">{{ order.shippingAddress.line2 }}</div>
              <div>{{ order.shippingAddress.postalCode }} {{ order.shippingAddress.city }}</div>
            </address>
          </div>
        </div>
      </div>

      <!-- ── Status updater ────────────────────────────────────────────── -->
      <div class="bg-white border border-gray-200 rounded-xl p-6 mb-5">
        <h2 class="text-base font-semibold text-gray-900 mb-3">Oppdater ordrestatus</h2>
        <div class="flex items-center gap-3 flex-wrap">
          <select
            v-model="newStatus"
            class="border border-gray-300 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500 bg-white"
          >
            <option v-for="s in ORDER_STATUSES" :key="s.value" :value="s.value">
              {{ s.label }}
            </option>
          </select>
          <button
            :disabled="statusSaving || newStatus === order.status"
            class="bg-blue-700 text-white px-4 py-2 rounded-lg text-sm font-medium hover:bg-blue-800 disabled:opacity-50 disabled:cursor-not-allowed"
            @click="saveStatus"
          >
            {{ statusSaving ? 'Lagrer…' : 'Lagre status' }}
          </button>
          <span v-if="statusSuccess" class="text-green-600 text-sm">✓ {{ statusSuccess }}</span>
          <span v-if="statusError" class="text-red-600 text-sm">{{ statusError }}</span>
        </div>
      </div>

      <!-- ── Production stage per item ─────────────────────────────────── -->
      <div class="bg-white border border-gray-200 rounded-xl p-6 mb-5">
        <h2 class="text-base font-semibold text-gray-900 mb-4">Produksjonsstatus per vare</h2>
        <div v-for="item in order.items" :key="item.id" class="mb-5 last:mb-0">
          <div class="flex items-start justify-between mb-2">
            <div>
              <div class="font-medium text-gray-900 text-sm">{{ itemLabel(item) }}</div>
              <div class="text-xs text-gray-500">{{ item.quantity }} stk · nåværende: <strong>{{ prodStageLabel(item.currentProductionStage) }}</strong></div>
            </div>
            <div class="text-sm font-semibold text-gray-700 ml-4 shrink-0">{{ formatNok(item.lineTotalNok) }}</div>
          </div>

          <div v-if="prodForms[item.id]" class="bg-gray-50 rounded-lg p-4 border border-gray-200">
            <div class="grid sm:grid-cols-3 gap-3">
              <div>
                <label class="block text-xs font-medium text-gray-600 mb-1">Ny status</label>
                <select
                  v-model="prodForms[item.id]!.stage"
                  class="w-full border border-gray-300 rounded-lg px-2 py-1.5 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500 bg-white"
                >
                  <option v-for="s in PRODUCTION_STAGES" :key="s.value" :value="s.value">
                    {{ s.label }}
                  </option>
                </select>
              </div>
              <div class="sm:col-span-2">
                <label class="block text-xs font-medium text-gray-600 mb-1">Merknad (valgfritt)</label>
                <input
                  v-model="prodForms[item.id]!.notes"
                  type="text"
                  placeholder="Legg til merknad…"
                  class="w-full border border-gray-300 rounded-lg px-2 py-1.5 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
                />
              </div>
            </div>
            <div class="flex items-center gap-3 mt-3">
              <button
                :disabled="prodSaving[item.id]"
                class="bg-blue-700 text-white px-3 py-1.5 rounded-lg text-xs font-medium hover:bg-blue-800 disabled:opacity-50"
                @click="saveProd(item)"
              >
                {{ prodSaving[item.id] ? 'Lagrer…' : 'Oppdater produksjon' }}
              </button>
              <span v-if="prodSuccess[item.id]" class="text-green-600 text-xs">✓ {{ prodSuccess[item.id] }}</span>
              <span v-if="prodError[item.id]" class="text-red-600 text-xs">{{ prodError[item.id] }}</span>
            </div>
          </div>

          <!-- Production history (compact) -->
          <div v-if="item.productionStatusHistory.length > 0" class="mt-2">
            <details class="text-xs">
              <summary class="text-gray-400 cursor-pointer hover:text-gray-600">Historikk ({{ item.productionStatusHistory.length }})</summary>
              <ul class="mt-1 space-y-0.5 pl-2 border-l border-gray-200 ml-2">
                <li v-for="h in [...item.productionStatusHistory].reverse()" :key="h.id" class="text-gray-500">
                  <span class="font-medium">{{ prodStageLabel(h.stage) }}</span>
                  · {{ formatDateTime(h.updatedAt) }}
                  <span v-if="h.notes" class="text-gray-400"> — {{ h.notes }}</span>
                </li>
              </ul>
            </details>
          </div>
        </div>
      </div>

      <!-- ── Shipping form ───────────────────────────────────────────────── -->
      <div class="bg-white border border-gray-200 rounded-xl p-6 mb-5">
        <h2 class="text-base font-semibold text-gray-900 mb-1">Fraktinformasjon</h2>
        <p class="text-xs text-gray-500 mb-4">
          Lagring setter ordrestatusen til <strong>Sendt</strong>.
          {{ order.shipmentTracking ? 'Eksisterende fraktinfo oppdateres.' : '' }}
        </p>

        <div class="grid sm:grid-cols-2 gap-4">
          <div>
            <label class="block text-sm font-medium text-gray-700 mb-1">Transportør <span class="text-red-500">*</span></label>
            <input v-model="shipForm.carrier" type="text" placeholder="Bring, PostNord…"
              class="w-full border border-gray-300 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500" />
          </div>
          <div>
            <label class="block text-sm font-medium text-gray-700 mb-1">Sporingsnummer <span class="text-red-500">*</span></label>
            <input v-model="shipForm.trackingNumber" type="text" placeholder="370799000000000000"
              class="w-full border border-gray-300 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500" />
          </div>
          <div class="sm:col-span-2">
            <label class="block text-sm font-medium text-gray-700 mb-1">Sporings-URL (valgfritt)</label>
            <input v-model="shipForm.trackingUrl" type="url" placeholder="https://sporing.bring.no/…"
              class="w-full border border-gray-300 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500" />
          </div>
          <div>
            <label class="block text-sm font-medium text-gray-700 mb-1">Sendedato</label>
            <input v-model="shipForm.shippedAt" type="date"
              class="w-full border border-gray-300 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500" />
          </div>
          <div>
            <label class="block text-sm font-medium text-gray-700 mb-1">Estimert ankomst</label>
            <input v-model="shipForm.estimatedArrival" type="date"
              class="w-full border border-gray-300 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500" />
          </div>
        </div>

        <div class="flex items-center gap-3 mt-4">
          <button
            :disabled="shipSaving"
            class="bg-green-700 text-white px-5 py-2 rounded-lg text-sm font-medium hover:bg-green-800 disabled:opacity-50"
            @click="saveShipping"
          >
            {{ shipSaving ? 'Lagrer…' : 'Lagre fraktinfo' }}
          </button>
          <span v-if="shipSuccess" class="text-green-600 text-sm">✓ {{ shipSuccess }}</span>
          <span v-if="shipError" class="text-red-600 text-sm">{{ shipError }}</span>
        </div>
      </div>

      <!-- ── Items table + price breakdown ─────────────────────────────── -->
      <div class="bg-white border border-gray-200 rounded-xl overflow-hidden mb-5">
        <div class="px-5 py-4 border-b border-gray-100">
          <h2 class="text-base font-semibold text-gray-900">Varer og priser</h2>
        </div>
        <table class="w-full text-sm">
          <thead class="bg-gray-50 border-b border-gray-100">
            <tr>
              <th class="text-left px-5 py-3 font-medium text-gray-500">Størrelse</th>
              <th class="text-right px-5 py-3 font-medium text-gray-500">Antall</th>
              <th class="text-right px-5 py-3 font-medium text-gray-500">Enhetspris</th>
              <th class="text-right px-5 py-3 font-medium text-gray-500">Sum</th>
            </tr>
          </thead>
          <tbody class="divide-y divide-gray-100">
            <tr v-for="item in order.items" :key="item.id">
              <td class="px-5 py-3 font-medium text-gray-800">{{ itemLabel(item) }}</td>
              <td class="px-5 py-3 text-right text-gray-600">{{ item.quantity }}</td>
              <td class="px-5 py-3 text-right text-gray-600">{{ formatNok(item.unitPriceNok) }}</td>
              <td class="px-5 py-3 text-right font-semibold text-gray-900">{{ formatNok(item.lineTotalNok) }}</td>
            </tr>
          </tbody>
        </table>
        <dl class="px-5 py-4 border-t border-gray-100 space-y-1.5 text-sm">
          <div class="flex justify-between">
            <dt class="text-gray-500">Frakt</dt>
            <dd>{{ formatNok(order.shippingCostNok) }}</dd>
          </div>
          <div v-if="order.expressFeeNok > 0" class="flex justify-between">
            <dt class="text-gray-500">Ekspressgebyr</dt>
            <dd>{{ formatNok(order.expressFeeNok) }}</dd>
          </div>
          <div class="flex justify-between font-bold text-base pt-2 border-t border-gray-200">
            <dt class="text-gray-900">Totalt inkl. MVA</dt>
            <dd class="text-blue-700">{{ formatNok(order.totalNok) }}</dd>
          </div>
          <div class="flex justify-between text-xs text-gray-400">
            <dt>Herav MVA (25%)</dt>
            <dd>{{ formatNok(order.totalNok * 0.2) }}</dd>
          </div>
        </dl>
      </div>

      <RouterLink to="/admin/orders" class="text-sm text-blue-600 hover:underline">← Tilbake til ordrelisten</RouterLink>
    </template>
  </div>
</template>

<script setup lang="ts">
import { ref, reactive, computed, onMounted } from 'vue'
import { useRoute, RouterLink } from 'vue-router'
import {
  getAdminOrder,
  updateOrderStatus,
  updateProductionStage,
  setShipping,
  advanceOrderState,
  captureOrderPayment,
  uploadDesignRequestPreview,
} from '@/api/admin'
import type { OrderDetailResponse, OrderItemDetail } from '@/api/orders'
import { formatNok, formatDateLong, formatDateTime } from '@/utils/format'

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

  for (const item of order.value.items) {
    if (!prodForms.value[item.id]) {
      prodForms.value[item.id] = { stage: item.currentProductionStage, notes: '' }
    } else {
      prodForms.value[item.id]!.stage = item.currentProductionStage
    }
  }

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

// ── State-machine helpers ─────────────────────────────────────────────────────
const STATE_SEQUENCES: Record<string, string[]> = {
  CustomBanner:  ['Draft', 'Paid', 'InProduction', 'Shipped', 'Delivered'],
  AiBanner:      ['Draft', 'Paid', 'CustomerApproval', 'InProduction', 'Shipped', 'Delivered'],
  ManualDesign:  ['Draft', 'Paid', 'DesignReady', 'CustomerApproval', 'InProduction', 'Shipped', 'Delivered'],
}

const STATE_LABELS: Record<string, string> = {
  Draft:            'Utkast',
  Paid:             'Betalt',
  DesignReady:      'Design klart',
  CustomerApproval: 'Kundeaproval',
  InProduction:     'Under produksjon',
  Shipped:          'Sendt',
  Delivered:        'Levert',
  Cancelled:        'Kansellert',
}

const currentSequence = computed((): string[] => {
  if (!order.value?.orderType) return []
  return STATE_SEQUENCES[order.value.orderType] ?? (STATE_SEQUENCES['CustomBanner'] as string[])
})

const currentStateIndex = computed(() => {
  if (!order.value?.orderState) return -1
  return currentSequence.value.indexOf(order.value.orderState)
})

// ── Order type & state helpers ────────────────────────────────────────────────
const ORDER_TYPE_LABELS: Record<string, string> = {
  CustomBanner: 'Tilpasset banner',
  AiBanner: 'AI-banner',
  ManualDesign: 'Designerbannem',
}
const ORDER_TYPE_CLASSES: Record<string, string> = {
  CustomBanner: 'bg-blue-900/50 text-blue-300',
  AiBanner: 'bg-cyan-900/50 text-cyan-300',
  ManualDesign: 'bg-indigo-900/50 text-indigo-300',
}

function orderTypeLabel(t: string | undefined) { return t ? (ORDER_TYPE_LABELS[t] ?? t) : '—' }
function orderTypeClass(t: string | undefined) { return t ? (ORDER_TYPE_CLASSES[t] ?? 'bg-gray-700 text-gray-300') : '' }

const ORDER_STATE_LABELS: Record<string, string> = {
  Draft: 'Utkast', Paid: 'Betalt', DesignReady: 'Design klart',
  CustomerApproval: 'Venter kundeaproval', InProduction: 'Under produksjon',
  Shipped: 'Sendt', Delivered: 'Levert', Cancelled: 'Kansellert',
}
const ORDER_STATE_CLASSES: Record<string, string> = {
  Draft: 'bg-gray-100 text-gray-600', Paid: 'bg-blue-100 text-blue-800',
  DesignReady: 'bg-cyan-100 text-cyan-800', CustomerApproval: 'bg-orange-100 text-orange-800',
  InProduction: 'bg-indigo-100 text-indigo-800', Shipped: 'bg-green-100 text-green-800',
  Delivered: 'bg-green-100 text-green-700', Cancelled: 'bg-red-100 text-red-700',
}

function stateLabel(s: string | undefined) {
  if (!s) return '—'
  return ORDER_STATE_LABELS[s] ?? s
}
function stateClass(s: string | undefined) {
  if (!s) return 'bg-gray-100 text-gray-600'
  return ORDER_STATE_CLASSES[s] ?? 'bg-gray-100 text-gray-600'
}

// ── Contextual action computeds ───────────────────────────────────────────────
const isCustomBanner  = computed(() => order.value?.orderType === 'CustomBanner')
const isAiBanner      = computed(() => order.value?.orderType === 'AiBanner')
const isManualDesign  = computed(() => order.value?.orderType === 'ManualDesign')
const currentState    = computed(() => order.value?.orderState)

// For ManualDesign: the linked design request ID (from manualDesign sub-object)
const designRequestId = computed<number | null>(() => {
  if (isManualDesign.value) return order.value?.manualDesign?.designRequestId ?? null
  if (isAiBanner.value) return order.value?.aiBanner?.designRequestId ?? null
  return null
})

// ── Action: Capture payment ────────────────────────────────────────────────────
const captureBusy = ref(false)
const captureError = ref('')
const captureSuccess = ref('')

async function capturePayment() {
  captureBusy.value = true
  captureError.value = ''
  captureSuccess.value = ''
  try {
    order.value = await captureOrderPayment(orderId)
    syncForms()
    captureSuccess.value = 'Betaling innkassert.'
    setTimeout(() => { captureSuccess.value = '' }, 5000)
  } catch (err: unknown) {
    const e = err as { response?: { data?: { error?: string } } }
    captureError.value = e.response?.data?.error ?? 'Innkassering feilet.'
  } finally {
    captureBusy.value = false
  }
}

// ── Action: Send til produksjon (CustomBanner Paid → InProduction) ────────────
const sendToProductionBusy = ref(false)
const sendToProductionError = ref('')
const sendToProductionSuccess = ref('')

async function sendToProduction() {
  sendToProductionBusy.value = true
  sendToProductionError.value = ''
  sendToProductionSuccess.value = ''
  try {
    order.value = await advanceOrderState(orderId, 'InProduction')
    syncForms()
    sendToProductionSuccess.value = 'Ordren er sendt til produksjon. Kunden varsles.'
    setTimeout(() => { sendToProductionSuccess.value = '' }, 4000)
  } catch (err: unknown) {
    const e = err as { response?: { data?: { error?: string } } }
    sendToProductionError.value = e.response?.data?.error ?? 'Feil ved oppdatering.'
  } finally {
    sendToProductionBusy.value = false
  }
}

// ── Action: Last opp ferdig design (ManualDesign Paid → DesignReady) ──────────
const designFile = ref<File | null>(null)
const designFileInput = ref<HTMLInputElement | null>(null)
const uploadDesignBusy = ref(false)
const uploadDesignError = ref('')
const uploadDesignSuccess = ref('')

function onDesignFileChange(e: Event) {
  const input = e.target as HTMLInputElement
  designFile.value = input.files?.[0] ?? null
  uploadDesignError.value = ''
}

async function uploadFinishedDesign() {
  if (!designFile.value || !designRequestId.value) return
  if (designFile.value.size > 20 * 1024 * 1024) {
    uploadDesignError.value = 'Filen er for stor (maks 20 MB).'
    return
  }
  uploadDesignBusy.value = true
  uploadDesignError.value = ''
  uploadDesignSuccess.value = ''
  try {
    await uploadDesignRequestPreview(designRequestId.value, designFile.value)
    // Reload order to get updated state
    order.value = await getAdminOrder(orderId)
    syncForms()
    designFile.value = null
    if (designFileInput.value) designFileInput.value.value = ''
    uploadDesignSuccess.value = 'Design lastet opp. Kunden varsles for godkjenning.'
    setTimeout(() => { uploadDesignSuccess.value = '' }, 4000)
  } catch (err: unknown) {
    const e = err as { response?: { data?: { error?: string } } }
    uploadDesignError.value = e.response?.data?.error ?? 'Opplasting feilet.'
  } finally {
    uploadDesignBusy.value = false
  }
}

// ── Action: Force-advance from CustomerApproval → InProduction (admin override) ──
const forceApprovalBusy = ref(false)
const forceApprovalError = ref('')
const forceApprovalSuccess = ref('')

async function forceAdvanceFromApproval() {
  forceApprovalBusy.value = true
  forceApprovalError.value = ''
  forceApprovalSuccess.value = ''
  try {
    order.value = await advanceOrderState(orderId, 'InProduction')
    syncForms()
    forceApprovalSuccess.value = 'Ordren er sendt til produksjon.'
    setTimeout(() => { forceApprovalSuccess.value = '' }, 4000)
  } catch (err: unknown) {
    const e = err as { response?: { data?: { error?: string } } }
    forceApprovalError.value = e.response?.data?.error ?? 'Feil ved oppdatering.'
  } finally {
    forceApprovalBusy.value = false
  }
}

// ── Action: Merk som levert (Shipped → Delivered) ─────────────────────────────
const markDeliveredBusy = ref(false)
const markDeliveredError = ref('')
const markDeliveredSuccess = ref('')

async function markDelivered() {
  markDeliveredBusy.value = true
  markDeliveredError.value = ''
  markDeliveredSuccess.value = ''
  try {
    order.value = await advanceOrderState(orderId, 'Delivered')
    syncForms()
    markDeliveredSuccess.value = 'Ordren er merket som levert.'
    setTimeout(() => { markDeliveredSuccess.value = '' }, 4000)
  } catch (err: unknown) {
    const e = err as { response?: { data?: { error?: string } } }
    markDeliveredError.value = e.response?.data?.error ?? 'Feil ved oppdatering.'
  } finally {
    markDeliveredBusy.value = false
  }
}

// ── Status update (legacy / advanced) ────────────────────────────────────────
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
function prodStageLabel(s: string) {
  return PRODUCTION_STAGES.find(x => x.value === s)?.label ?? s
}
function itemLabel(item: OrderItemDetail): string {
  if (item.bannerSizeName) return item.bannerSizeName
  if (item.customWidthCm) return `${item.customWidthCm} × ${item.heightCm} cm`
  return `Banner ${item.heightCm} cm`
}
const formatDate = formatDateLong

const deliveryLabel = computed(() => {
  if (order.value?.deliveryType === 'Express') return 'Ekspress'
  if (order.value?.deliveryType === 'Pickup') return 'Henting'
  return 'Standard'
})

const packingLabel = computed(() => {
  if (order.value?.packingMode === 'Folded') return 'Brettes (flatt)'
  return 'Rulles (rørform)'
})
</script>

<template>
  <div class="max-w-5xl mx-auto px-4 py-8">
    <!-- Breadcrumb -->
    <div class="flex items-center gap-2 mb-6 text-sm">
      <RouterLink to="/admin/orders" class="text-blue-400 hover:underline">Ordrer</RouterLink>
      <span class="text-gray-600">›</span>
      <span class="text-gray-400">Ordre #{{ orderId }}</span>
    </div>

    <!-- Loading -->
    <div v-if="loading" class="flex justify-center py-16">
      <div class="w-8 h-8 border-4 border-blue-500 border-t-transparent rounded-full animate-spin" />
    </div>
    <div v-else-if="loadError" class="bg-red-900/30 border border-red-700 text-red-400 rounded-xl p-6 text-center">
      {{ loadError }}
    </div>

    <template v-else-if="order">
      <!-- ── Order header ─────────────────────────────────────────────────── -->
      <div class="bg-gray-800 border border-gray-700 rounded-xl p-6 mb-5">
        <div class="flex flex-wrap items-start justify-between gap-4">
          <div>
            <div class="flex items-center gap-3 mb-1">
              <h1 class="text-xl font-bold text-gray-100">Ordre #{{ order.id }}</h1>
              <!-- Order type chip -->
              <span
                v-if="order.orderType"
                class="text-xs font-semibold px-2 py-0.5 rounded-full"
                :class="orderTypeClass(order.orderType)"
              >
                {{ orderTypeLabel(order.orderType) }}
              </span>
            </div>
            <p class="text-sm text-gray-500 mt-0.5">Opprettet {{ formatDateTime(order.createdAt) }}</p>
          </div>
          <!-- Current state badge -->
          <span class="text-sm font-semibold px-3 py-1.5 rounded-full" :class="stateClass(order.orderState)">
            {{ stateLabel(order.orderState) }}
          </span>
        </div>

        <!-- State progression stepper -->
        <div v-if="currentSequence.length" class="mt-5">
          <div class="flex items-center overflow-x-auto pb-1 gap-0">
            <template v-for="(step, idx) in currentSequence" :key="step">
              <!-- Connector line (not before first) -->
              <div
                v-if="idx > 0"
                class="flex-1 h-0.5 min-w-4"
                :class="idx <= currentStateIndex ? 'bg-blue-500' : 'bg-gray-700'"
              />
              <!-- Step circle + label -->
              <div class="flex flex-col items-center shrink-0">
                <div
                  class="w-8 h-8 rounded-full flex items-center justify-center text-sm font-bold border-2 transition-colors"
                  :class="
                    idx < currentStateIndex
                      ? 'bg-blue-600 border-blue-600 text-white'
                      : idx === currentStateIndex
                        ? 'bg-blue-500 border-blue-400 text-white ring-2 ring-blue-400/40'
                        : 'bg-gray-800 border-gray-600 text-gray-500'
                  "
                >
                  <span v-if="idx < currentStateIndex">✓</span>
                  <span v-else>{{ idx + 1 }}</span>
                </div>
                <span
                  class="text-xs mt-1 text-center max-w-16 leading-tight"
                  :class="idx <= currentStateIndex ? 'text-gray-300' : 'text-gray-600'"
                >
                  {{ STATE_LABELS[step] ?? step }}
                </span>
              </div>
            </template>
          </div>
        </div>

        <div class="grid sm:grid-cols-5 gap-4 mt-5 text-sm">
          <div>
            <div class="text-xs font-semibold uppercase tracking-wide text-gray-500 mb-0.5">Leveringstype</div>
            <div class="font-medium text-gray-200">{{ deliveryLabel }}</div>
          </div>
          <div>
            <div class="text-xs font-semibold uppercase tracking-wide text-gray-500 mb-0.5">Pakking</div>
            <div class="font-medium text-gray-200">{{ packingLabel }}</div>
          </div>
          <div>
            <div class="text-xs font-semibold uppercase tracking-wide text-gray-500 mb-0.5">Estimert levering</div>
            <div class="font-medium text-gray-200">{{ formatDate(order.estimatedDelivery) }}</div>
          </div>
          <div>
            <div class="text-xs font-semibold uppercase tracking-wide text-gray-500 mb-0.5">Totalt inkl. MVA</div>
            <div class="font-bold text-blue-400">{{ formatNok(order.totalNok) }}</div>
          </div>
          <div>
            <div class="text-xs font-semibold uppercase tracking-wide text-gray-500 mb-0.5">Sist oppdatert</div>
            <div class="font-medium text-gray-400">{{ formatDateTime(order.updatedAt) }}</div>
          </div>
        </div>
      </div>

      <!-- ── Contextual action buttons ────────────────────────────────────── -->
      <div class="bg-gray-800 border border-gray-700 rounded-xl p-6 mb-5">
        <h2 class="text-base font-semibold text-gray-100 mb-4">Handlinger</h2>

        <!-- Capture payment banner (shown for all Paid orders with a Stripe PI) -->
        <div
          v-if="currentState === 'Paid' && order.stripePaymentIntentId"
          class="bg-yellow-900/20 border border-yellow-700/50 rounded-lg px-4 py-3 mb-4"
        >
          <p class="text-sm text-yellow-300 font-medium mb-2">
            💳 Betaling er reservert — ikke trukket ennå
          </p>
          <p class="text-xs text-yellow-500 mb-3">
            Kortreservasjonen utløper etter 7 dager. Kasser betalingen før du starter produksjon for å bekrefte at den går gjennom.
          </p>
          <div class="flex items-center gap-3 flex-wrap">
            <button
              :disabled="captureBusy"
              class="bg-yellow-700 text-white px-4 py-2 rounded-lg text-sm font-medium hover:bg-yellow-600 disabled:opacity-50 disabled:cursor-not-allowed"
              @click="capturePayment"
            >
              {{ captureBusy ? 'Innkasserer…' : '💰 Kasser betaling nå' }}
            </button>
            <span v-if="captureSuccess" class="text-green-400 text-sm">✓ {{ captureSuccess }}</span>
            <span v-if="captureError" class="text-red-400 text-sm">{{ captureError }}</span>
          </div>
        </div>

        <!-- CustomBanner + Paid: Send til produksjon -->
        <template v-if="isCustomBanner && currentState === 'Paid'">
          <div class="flex items-center gap-3 flex-wrap">
            <button
              :disabled="sendToProductionBusy"
              class="bg-blue-700 text-white px-5 py-2 rounded-lg text-sm font-medium hover:bg-blue-600 disabled:opacity-50 disabled:cursor-not-allowed"
              @click="sendToProduction"
            >
              {{ sendToProductionBusy ? 'Behandler…' : '🖨 Send til produksjon' }}
            </button>
            <span v-if="sendToProductionSuccess" class="text-green-400 text-sm">✓ {{ sendToProductionSuccess }}</span>
            <span v-if="sendToProductionError" class="text-red-400 text-sm">{{ sendToProductionError }}</span>
          </div>
          <p class="text-xs text-gray-400 mt-2">Setter ordren til «Under produksjon» og varsler kunden på e-post.</p>
        </template>

        <!-- ManualDesign + Paid: Last opp ferdig design -->
        <template v-else-if="isManualDesign && currentState === 'Paid'">
          <div class="mb-2">
            <p class="text-sm text-gray-300 mb-3">
              Last opp det ferdige designet for å varsle kunden om godkjenning.
            </p>
            <div class="flex flex-col sm:flex-row gap-3 items-start">
              <input
                ref="designFileInput"
                type="file"
                accept="image/jpeg,image/jpg,image/png"
                class="block text-sm text-gray-400 file:mr-3 file:py-2 file:px-4 file:rounded-lg file:border-0 file:text-sm file:font-medium file:bg-blue-900/50 file:text-blue-300 hover:file:bg-blue-900"
                @change="onDesignFileChange"
              />
              <button
                :disabled="!designFile || uploadDesignBusy"
                class="bg-blue-700 text-white px-5 py-2 rounded-lg text-sm font-medium hover:bg-blue-600 disabled:opacity-50 disabled:cursor-not-allowed whitespace-nowrap"
                @click="uploadFinishedDesign"
              >
                {{ uploadDesignBusy ? 'Laster opp…' : '📤 Last opp ferdig design' }}
              </button>
            </div>
            <div v-if="designFile" class="mt-1 text-xs text-gray-500">
              Valgt: {{ designFile.name }} ({{ (designFile.size / 1024 / 1024).toFixed(1) }} MB)
            </div>
            <div v-if="uploadDesignSuccess" class="mt-2 text-green-400 text-sm">✓ {{ uploadDesignSuccess }}</div>
            <div v-if="uploadDesignError" class="mt-2 text-red-400 text-sm">{{ uploadDesignError }}</div>
          </div>
        </template>

        <!-- AI/Manual in CustomerApproval: waiting for customer, admin can force -->
        <template v-else-if="(isAiBanner || isManualDesign) && currentState === 'CustomerApproval'">
          <div class="bg-orange-900/20 border border-orange-700 rounded-lg px-4 py-3 mb-4">
            <p class="text-sm text-orange-300 font-medium">
              ⏳ Venter på kundegodkjenning
            </p>
            <p class="text-xs text-orange-500 mt-0.5">
              Kunden har fått tilsendt designet og må godkjenne det før produksjon starter.
            </p>
          </div>
          <!-- Admin force-advance -->
          <div class="flex items-center gap-3 flex-wrap">
            <button
              :disabled="forceApprovalBusy"
              class="border border-gray-500 text-gray-300 px-4 py-2 rounded-lg text-sm hover:bg-gray-700 disabled:opacity-50"
              @click="forceAdvanceFromApproval"
            >
              {{ forceApprovalBusy ? 'Behandler…' : 'Admin: tving til produksjon →' }}
            </button>
            <span class="text-xs text-gray-500">Hopper over kundegodkjenning</span>
            <span v-if="forceApprovalSuccess" class="text-green-400 text-sm">✓ {{ forceApprovalSuccess }}</span>
            <span v-if="forceApprovalError" class="text-red-400 text-sm">{{ forceApprovalError }}</span>
          </div>
        </template>

        <!-- InProduction: Merk som sendt + fraktinfo -->
        <template v-else-if="currentState === 'InProduction'">
          <p class="text-sm text-gray-400 mb-4">
            Fyll inn fraktinfo og lagre for å sende ordren. Status settes til <strong>Sendt</strong> og kunden varsles.
            {{ order.shipmentTracking ? 'Eksisterende fraktinfo oppdateres.' : '' }}
          </p>
          <div class="grid sm:grid-cols-2 gap-4">
            <div>
              <label class="block text-sm font-medium text-gray-300 mb-1">Transportør <span class="text-red-400">*</span></label>
              <input v-model="shipForm.carrier" type="text" placeholder="Bring, PostNord…"
                class="w-full bg-gray-900 border border-gray-600 text-gray-100 placeholder:text-gray-500 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500" />
            </div>
            <div>
              <label class="block text-sm font-medium text-gray-300 mb-1">Sporingsnummer <span class="text-red-400">*</span></label>
              <input v-model="shipForm.trackingNumber" type="text" placeholder="370799000000000000"
                class="w-full bg-gray-900 border border-gray-600 text-gray-100 placeholder:text-gray-500 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500" />
            </div>
            <div class="sm:col-span-2">
              <label class="block text-sm font-medium text-gray-300 mb-1">Sporings-URL (valgfritt)</label>
              <input v-model="shipForm.trackingUrl" type="url" placeholder="https://sporing.bring.no/…"
                class="w-full bg-gray-900 border border-gray-600 text-gray-100 placeholder:text-gray-500 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500" />
            </div>
            <div>
              <label class="block text-sm font-medium text-gray-300 mb-1">Sendedato</label>
              <input v-model="shipForm.shippedAt" type="date"
                class="w-full bg-gray-900 border border-gray-600 text-gray-100 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500" />
            </div>
            <div>
              <label class="block text-sm font-medium text-gray-300 mb-1">Estimert ankomst</label>
              <input v-model="shipForm.estimatedArrival" type="date"
                class="w-full bg-gray-900 border border-gray-600 text-gray-100 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500" />
            </div>
          </div>
          <div class="flex items-center gap-3 mt-4 flex-wrap">
            <button
              :disabled="shipSaving"
              class="bg-green-700 text-white px-5 py-2 rounded-lg text-sm font-medium hover:bg-green-600 disabled:opacity-50"
              @click="saveShipping"
            >
              {{ shipSaving ? 'Lagrer…' : '🚚 Merk som sendt' }}
            </button>
            <span v-if="shipSuccess" class="text-green-400 text-sm">✓ {{ shipSuccess }}</span>
            <span v-if="shipError" class="text-red-400 text-sm">{{ shipError }}</span>
          </div>
        </template>

        <!-- Shipped: Merk som levert -->
        <template v-else-if="currentState === 'Shipped'">
          <div class="flex items-center gap-3 flex-wrap">
            <button
              :disabled="markDeliveredBusy"
              class="bg-green-700 text-white px-5 py-2 rounded-lg text-sm font-medium hover:bg-green-600 disabled:opacity-50 disabled:cursor-not-allowed"
              @click="markDelivered"
            >
              {{ markDeliveredBusy ? 'Behandler…' : '✅ Merk som levert' }}
            </button>
            <span v-if="markDeliveredSuccess" class="text-green-400 text-sm">✓ {{ markDeliveredSuccess }}</span>
            <span v-if="markDeliveredError" class="text-red-400 text-sm">{{ markDeliveredError }}</span>
          </div>
        </template>

        <!-- Draft / PendingPayment / DesignReady / Delivered / Cancelled: informational -->
        <template v-else>
          <p v-if="currentState === 'Draft'" class="text-sm text-gray-400">
            Ordren er i utkast-tilstand. Venter på betaling.
          </p>
          <p v-else-if="currentState === 'DesignReady'" class="text-sm text-gray-400">
            Design er klart. Venter på kundegodkjenning.
          </p>
          <p v-else-if="currentState === 'Delivered'" class="text-sm text-green-400 font-medium">
            ✓ Bestillingen er levert til kunden.
          </p>
          <p v-else-if="currentState === 'Cancelled'" class="text-sm text-red-400">
            Ordren er kansellert.
          </p>
          <p v-else class="text-sm text-gray-400">Ingen handlinger tilgjengelig for nåværende tilstand.</p>
        </template>
      </div>

      <!-- ── Type-specific sub-content ────────────────────────────────────── -->

      <!-- AI banner: generated image -->
      <div v-if="isAiBanner" class="bg-gray-800 border border-gray-700 rounded-xl p-6 mb-5">
        <h2 class="text-base font-semibold text-gray-100 mb-3">AI-generert bilde</h2>
        <div v-if="order.aiBanner?.previewUrl">
          <img
            :src="order.aiBanner.previewUrl"
            alt="AI-generert forhåndsvisning"
            class="w-full max-w-xl border border-gray-600 shadow-sm"
          />
          <div class="mt-3 flex items-center gap-4 text-sm">
            <a
              :href="order.aiBanner.previewUrl"
              target="_blank"
              rel="noopener noreferrer"
              class="text-blue-400 hover:underline"
            >
              Se full størrelse ↗
            </a>
            <RouterLink
              v-if="designRequestId"
              :to="`/admin/design-requests/${designRequestId}`"
              class="text-blue-400 hover:underline"
            >
              Åpne design-bestilling ↗
            </RouterLink>
          </div>
          <div v-if="order.aiBanner.personName" class="mt-2 text-sm text-gray-400">
            Person: <span class="text-gray-300">{{ order.aiBanner.personName }}</span>
          </div>
          <div v-if="order.aiBanner.themeDescription" class="mt-1 text-sm text-gray-400">
            Tema: <span class="text-gray-300">{{ order.aiBanner.themeDescription }}</span>
          </div>
        </div>
        <div v-else class="text-sm text-gray-500">
          Ingen AI-generert bilde ennå.
          <RouterLink
            v-if="designRequestId"
            :to="`/admin/design-requests/${designRequestId}`"
            class="text-blue-400 hover:underline ml-1"
          >
            Se design-bestilling ↗
          </RouterLink>
        </div>
      </div>

      <!-- Manual design: uploaded design + link to design request -->
      <div v-if="isManualDesign" class="bg-gray-800 border border-gray-700 rounded-xl p-6 mb-5">
        <h2 class="text-base font-semibold text-gray-100 mb-3">Manuelt design</h2>
        <div v-if="order.manualDesign?.previewUrl" class="mb-4">
          <div class="text-xs font-semibold uppercase tracking-wide text-gray-500 mb-2">Nåværende design</div>
          <img
            :src="order.manualDesign.previewUrl"
            alt="Designforslag"
            class="w-full max-w-xl rounded-xl border border-gray-600 shadow-sm"
          />
          <a
            :href="order.manualDesign.previewUrl"
            target="_blank"
            rel="noopener noreferrer"
            class="mt-2 block text-sm text-blue-400 hover:underline"
          >
            Se full størrelse ↗
          </a>
        </div>
        <div v-else class="text-sm text-gray-500 mb-3">
          Ingen design lastet opp ennå.
        </div>
        <div v-if="order.manualDesign?.designerNotes" class="text-sm text-gray-400 mb-3">
          Designernotat: <span class="text-gray-300 italic">{{ order.manualDesign.designerNotes }}</span>
        </div>
        <RouterLink
          v-if="designRequestId"
          :to="`/admin/design-requests/${designRequestId}`"
          class="text-sm text-blue-400 hover:underline"
        >
          Åpne full design-bestilling (revisjonshistorikk, opplasting) ↗
        </RouterLink>
      </div>

      <!-- Custom banner: uploaded design preview (if any) -->
      <div v-if="isCustomBanner && order.customBanner?.previewUrl" class="bg-gray-800 border border-gray-700 rounded-xl p-6 mb-5">
        <h2 class="text-base font-semibold text-gray-100 mb-3">Opplastet design</h2>
        <img
          :src="order.customBanner.previewUrl"
          alt="Kundens opplastede design"
          class="w-full max-w-xl rounded-xl border border-gray-600 shadow-sm"
        />
        <a
          :href="order.customBanner.previewUrl"
          target="_blank"
          rel="noopener noreferrer"
          class="mt-2 block text-sm text-blue-400 hover:underline"
        >
          Se full størrelse ↗
        </a>
        <div v-if="order.customBanner.bannerSizeName" class="mt-2 text-sm text-gray-400">
          Størrelse: <span class="text-gray-300">{{ order.customBanner.bannerSizeName }}</span>
          <span v-if="order.customBanner.materialName"> · {{ order.customBanner.materialName }}</span>
        </div>
      </div>

      <!-- ── Customer info ──────────────────────────────────────────────── -->
      <div class="bg-gray-800 border border-gray-700 rounded-xl p-6 mb-5">
        <h2 class="text-base font-semibold text-gray-100 mb-3">Kundeinformasjon</h2>
        <div class="grid sm:grid-cols-2 gap-4 text-sm">
          <div>
            <div class="text-xs font-semibold uppercase tracking-wide text-gray-500 mb-0.5">Navn</div>
            <div class="font-medium text-gray-200">{{ order.customerName ?? '—' }}</div>
          </div>
          <div>
            <div class="text-xs font-semibold uppercase tracking-wide text-gray-500 mb-0.5">E-post</div>
            <div>
              <a v-if="order.customerEmail" :href="`mailto:${order.customerEmail}`"
                 class="text-blue-400 hover:underline">
                {{ order.customerEmail }}
              </a>
              <span v-else class="text-gray-400">—</span>
            </div>
          </div>
          <div v-if="order.shippingAddress">
            <div class="text-xs font-semibold uppercase tracking-wide text-gray-500 mb-0.5">Leveringsadresse</div>
            <address class="not-italic text-gray-300 space-y-0.5">
              <div>{{ order.shippingAddress.line1 }}</div>
              <div v-if="order.shippingAddress.line2">{{ order.shippingAddress.line2 }}</div>
              <div>{{ order.shippingAddress.postalCode }} {{ order.shippingAddress.city }}</div>
            </address>
          </div>
        </div>
      </div>

      <!-- ── Shipping tracking (when exists) ──────────────────────────────── -->
      <div v-if="order.shipmentTracking" class="bg-gray-800 border border-gray-700 rounded-xl p-6 mb-5">
        <h2 class="text-base font-semibold text-gray-100 mb-3">Fraktinformasjon</h2>
        <div class="grid sm:grid-cols-3 gap-4 text-sm">
          <div>
            <div class="text-xs font-semibold uppercase tracking-wide text-gray-500 mb-0.5">Transportør</div>
            <div class="font-medium text-gray-200">{{ order.shipmentTracking.carrier }}</div>
          </div>
          <div>
            <div class="text-xs font-semibold uppercase tracking-wide text-gray-500 mb-0.5">Sporingsnummer</div>
            <div class="font-medium text-gray-200">{{ order.shipmentTracking.trackingNumber }}</div>
          </div>
          <div v-if="order.shipmentTracking.trackingUrl">
            <div class="text-xs font-semibold uppercase tracking-wide text-gray-500 mb-0.5">Sporingslenke</div>
            <a
              :href="order.shipmentTracking.trackingUrl"
              target="_blank"
              rel="noopener noreferrer"
              class="text-blue-400 hover:underline text-sm"
            >
              Spor pakken ↗
            </a>
          </div>
          <div v-if="order.shipmentTracking.shippedAt">
            <div class="text-xs font-semibold uppercase tracking-wide text-gray-500 mb-0.5">Sendedato</div>
            <div class="text-gray-300">{{ formatDate(order.shipmentTracking.shippedAt) }}</div>
          </div>
          <div v-if="order.shipmentTracking.estimatedArrival">
            <div class="text-xs font-semibold uppercase tracking-wide text-gray-500 mb-0.5">Estimert ankomst</div>
            <div class="text-gray-300">{{ formatDate(order.shipmentTracking.estimatedArrival) }}</div>
          </div>
          <div v-if="order.shipmentTracking.deliveredAt">
            <div class="text-xs font-semibold uppercase tracking-wide text-gray-500 mb-0.5">Levert</div>
            <div class="text-green-400">{{ formatDate(order.shipmentTracking.deliveredAt) }}</div>
          </div>
        </div>
      </div>

      <!-- ── Production stage per item ─────────────────────────────────── -->
      <div v-if="order.orderState === 'InProduction' || order.orderState === 'Shipped' || order.orderState === 'Delivered'" class="bg-gray-800 border border-gray-700 rounded-xl p-6 mb-5">
        <h2 class="text-base font-semibold text-gray-100 mb-4">Produksjonsstatus per vare</h2>
        <div v-for="item in order.items" :key="item.id" class="mb-5 last:mb-0">
          <div class="flex items-start justify-between mb-2">
            <div>
              <div class="font-medium text-gray-200 text-sm">{{ itemLabel(item) }}</div>
              <div class="text-xs text-gray-400">{{ item.quantity }} stk · nåværende: <strong>{{ prodStageLabel(item.currentProductionStage) }}</strong></div>
            </div>
            <div class="text-sm font-semibold text-gray-300 ml-4 shrink-0">{{ formatNok(item.lineTotalNok) }}</div>
          </div>

          <div v-if="prodForms[item.id]" class="bg-gray-900 rounded-lg p-4 border border-gray-700">
            <div class="grid sm:grid-cols-3 gap-3">
              <div>
                <label class="block text-xs font-medium text-gray-400 mb-1">Ny status</label>
                <select
                  v-model="prodForms[item.id]!.stage"
                  class="w-full bg-gray-800 border border-gray-600 text-gray-100 rounded-lg px-2 py-1.5 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
                >
                  <option v-for="s in PRODUCTION_STAGES" :key="s.value" :value="s.value">
                    {{ s.label }}
                  </option>
                </select>
              </div>
              <div class="sm:col-span-2">
                <label class="block text-xs font-medium text-gray-400 mb-1">Merknad (valgfritt)</label>
                <input
                  v-model="prodForms[item.id]!.notes"
                  type="text"
                  placeholder="Legg til merknad…"
                  class="w-full bg-gray-800 border border-gray-600 text-gray-100 placeholder:text-gray-500 rounded-lg px-2 py-1.5 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
                />
              </div>
            </div>
            <div class="flex items-center gap-3 mt-3">
              <button
                :disabled="prodSaving[item.id]"
                class="bg-blue-700 text-white px-3 py-1.5 rounded-lg text-xs font-medium hover:bg-blue-600 disabled:opacity-50"
                @click="saveProd(item)"
              >
                {{ prodSaving[item.id] ? 'Lagrer…' : 'Oppdater produksjon' }}
              </button>
              <span v-if="prodSuccess[item.id]" class="text-green-400 text-xs">✓ {{ prodSuccess[item.id] }}</span>
              <span v-if="prodError[item.id]" class="text-red-400 text-xs">{{ prodError[item.id] }}</span>
            </div>
          </div>

          <!-- Production history (compact) -->
          <div v-if="item.productionStatusHistory.length > 0" class="mt-2">
            <details class="text-xs">
              <summary class="text-gray-500 cursor-pointer hover:text-gray-300">Historikk ({{ item.productionStatusHistory.length }})</summary>
              <ul class="mt-1 space-y-0.5 pl-2 border-l border-gray-700 ml-2">
                <li v-for="h in [...item.productionStatusHistory].reverse()" :key="h.id" class="text-gray-400">
                  <span class="font-medium">{{ prodStageLabel(h.stage) }}</span>
                  · {{ formatDateTime(h.updatedAt) }}
                  <span v-if="h.notes" class="text-gray-500"> — {{ h.notes }}</span>
                </li>
              </ul>
            </details>
          </div>
        </div>
      </div>

      <!-- ── Items table + price breakdown ─────────────────────────────── -->
      <div class="bg-gray-800 border border-gray-700 rounded-xl overflow-hidden mb-5">
        <div class="px-5 py-4 border-b border-gray-700">
          <h2 class="text-base font-semibold text-gray-100">Varer og priser</h2>
        </div>
        <table class="w-full text-sm">
          <thead class="bg-gray-900 border-b border-gray-700">
            <tr>
              <th class="text-left px-5 py-3 font-medium text-gray-400">Størrelse</th>
              <th class="text-right px-5 py-3 font-medium text-gray-400">Antall</th>
              <th class="text-right px-5 py-3 font-medium text-gray-400">Enhetspris</th>
              <th class="text-right px-5 py-3 font-medium text-gray-400">Sum</th>
            </tr>
          </thead>
          <tbody class="divide-y divide-gray-700">
            <tr v-for="item in order.items" :key="item.id">
              <td class="px-5 py-3 font-medium text-gray-200">{{ itemLabel(item) }}</td>
              <td class="px-5 py-3 text-right text-gray-400">{{ item.quantity }}</td>
              <td class="px-5 py-3 text-right text-gray-400">{{ formatNok(item.unitPriceNok) }}</td>
              <td class="px-5 py-3 text-right font-semibold text-gray-100">{{ formatNok(item.lineTotalNok) }}</td>
            </tr>
          </tbody>
        </table>
        <dl class="px-5 py-4 border-t border-gray-700 space-y-1.5 text-sm">
          <div class="flex justify-between">
            <dt class="text-gray-400">Frakt</dt>
            <dd class="text-gray-300">{{ formatNok(order.shippingCostNok) }}</dd>
          </div>
          <div v-if="order.expressFeeNok > 0" class="flex justify-between">
            <dt class="text-gray-400">Ekspressgebyr</dt>
            <dd class="text-gray-300">{{ formatNok(order.expressFeeNok) }}</dd>
          </div>
          <div class="flex justify-between font-bold text-base pt-2 border-t border-gray-700">
            <dt class="text-gray-100">Totalt inkl. MVA</dt>
            <dd class="text-blue-400">{{ formatNok(order.totalNok) }}</dd>
          </div>
          <div class="flex justify-between text-xs text-gray-500">
            <dt>Herav MVA (25%)</dt>
            <dd>{{ formatNok(order.totalNok * 0.2) }}</dd>
          </div>
        </dl>
      </div>

      <!-- ── Advanced: legacy status updater (collapsed) ───────────────── -->
      <details class="mb-5">
        <summary class="cursor-pointer text-sm text-gray-500 hover:text-gray-300 bg-gray-800 border border-gray-700 rounded-xl px-5 py-3">
          ⚙ Avansert: endre status direkte
        </summary>
        <div class="bg-gray-800 border border-gray-700 border-t-0 rounded-b-xl px-5 py-4">
          <div class="flex items-center gap-3 flex-wrap">
            <select
              v-model="newStatus"
              class="bg-gray-900 border border-gray-600 text-gray-100 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
            >
              <option v-for="s in ORDER_STATUSES" :key="s.value" :value="s.value">
                {{ s.label }}
              </option>
            </select>
            <button
              :disabled="statusSaving || newStatus === order.status"
              class="bg-blue-700 text-white px-4 py-2 rounded-lg text-sm font-medium hover:bg-blue-600 disabled:opacity-50 disabled:cursor-not-allowed"
              @click="saveStatus"
            >
              {{ statusSaving ? 'Lagrer…' : 'Lagre status' }}
            </button>
            <span v-if="statusSuccess" class="text-green-400 text-sm">✓ {{ statusSuccess }}</span>
            <span v-if="statusError" class="text-red-400 text-sm">{{ statusError }}</span>
          </div>
        </div>
      </details>

      <RouterLink to="/admin/orders" class="text-sm text-blue-400 hover:underline">← Tilbake til ordrelisten</RouterLink>
    </template>
  </div>
</template>

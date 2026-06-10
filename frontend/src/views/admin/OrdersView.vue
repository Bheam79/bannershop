<script setup lang="ts">
import { ref, reactive, onMounted, computed } from 'vue'
import { useRouter } from 'vue-router'
import { listAdminOrders } from '@/api/admin'
import type { OrderListItem } from '@/api/orders'
import { formatNok, formatDate } from '@/utils/format'
import {
  ORDER_STATUS_LABELS as STATUS_LABELS,
  ORDER_STATUS_ADMIN_CLASSES as STATUS_CLASSES,
  orderStatusLabel as statusLabel,
  orderStatusAdminClass as statusClass,
} from '@/utils/orderStatus'

const router = useRouter()

// ── Filter state ──────────────────────────────────────────────────────────────
const filters = reactive({
  status: '',
  orderType: '',
  fromDate: '',
  toDate: '',
  search: '',
  // BANNERSH-139: include AI credit-pack purchases in the list. Defaults to false
  // so the production team isn't distracted by them.
  includeCreditPacks: false,
  // BANNERSH-169: hide 0-kr AI design-tracking orders by default — they have no
  // production items and only confuse the fulfilment team.
  excludeZeroValueAiOrders: true,
})

// ── Table state ───────────────────────────────────────────────────────────────
const orders = ref<OrderListItem[]>([])
const page = ref(1)
const totalPages = ref(1)
const totalCount = ref(0)
const loading = ref(true)
const error = ref<string | null>(null)
const PAGE_SIZE = 20

async function load(p = 1) {
  loading.value = true
  error.value = null
  try {
    const result = await listAdminOrders({
      status: filters.status || undefined,
      orderType: filters.orderType || undefined,
      fromUtc: filters.fromDate ? `${filters.fromDate}T00:00:00Z` : undefined,
      toUtc: filters.toDate ? `${filters.toDate}T23:59:59Z` : undefined,
      search: filters.search || undefined,
      page: p,
      pageSize: PAGE_SIZE,
      includeCreditPacks: filters.includeCreditPacks || undefined,
      excludeZeroValueAiOrders: filters.excludeZeroValueAiOrders,
    })
    orders.value = result.items
    page.value = result.page
    totalPages.value = result.totalPages
    totalCount.value = result.totalCount
  } catch {
    error.value = 'Kunne ikke laste ordrer.'
  } finally {
    loading.value = false
  }
}

function applyFilters() {
  load(1)
}

function clearFilters() {
  filters.status = ''
  filters.orderType = ''
  filters.fromDate = ''
  filters.toDate = ''
  filters.search = ''
  filters.includeCreditPacks = false
  filters.excludeZeroValueAiOrders = true
  load(1)
}

onMounted(() => load(1))

const hasPrev = computed(() => page.value > 1)
const hasNext = computed(() => page.value < totalPages.value)

// ── Helpers ───────────────────────────────────────────────────────────────────
function deliveryLabel(d: string) {
  if (d === 'Express') return 'Ekspress'
  if (d === 'Pickup') return 'Henting'
  return 'Standard'
}

// Order type chip helpers
const ORDER_TYPE_LABELS: Record<string, string> = {
  CustomBanner: 'Tilpasset',
  AiBanner: 'AI',
  ManualDesign: 'Designer',
  // BANNERSH-139: AI credit-pack purchases. Norwegian "AI-kjøp" — gold tone to
  // visually distinguish them from production orders.
  CreditPack: 'AI-kjøp',
}
const ORDER_TYPE_CLASSES: Record<string, string> = {
  CustomBanner: 'bg-blue-900/50 text-blue-300',
  AiBanner: 'bg-cyan-900/50 text-cyan-300',
  ManualDesign: 'bg-indigo-900/50 text-indigo-300',
  CreditPack: 'bg-yellow-900/50 text-yellow-300',
}
function orderTypeLabel(t: string | undefined) { return t ? (ORDER_TYPE_LABELS[t] ?? t) : '—' }
function orderTypeClass(t: string | undefined) { return t ? (ORDER_TYPE_CLASSES[t] ?? 'bg-gray-700 text-gray-300') : 'bg-gray-700 text-gray-300' }

// State badge uses orderState when available, falls back to status
function displayState(order: OrderListItem) { return order.orderState ?? order.status }
function stateLabel(order: OrderListItem): string {
  const s = displayState(order)
  return STATUS_LABELS[s] ?? s
}
function stateClass(order: OrderListItem): string {
  const s = displayState(order)
  return STATUS_CLASSES[s] ?? 'bg-gray-100 text-gray-600'
}

const ALL_STATUSES = Object.keys(STATUS_LABELS)
const ALL_ORDER_TYPES = Object.keys(ORDER_TYPE_LABELS)
</script>

<template>
  <div class="max-w-7xl mx-auto px-4 py-8">
    <div class="flex items-center justify-between mb-6">
      <div>
        <h1 class="text-2xl font-bold text-white">Ordrer</h1>
        <p v-if="!loading" class="text-sm text-gray-400 mt-0.5">{{ totalCount }} ordre{{ totalCount !== 1 ? 'r' : '' }} totalt</p>
      </div>
    </div>

    <!-- ── Filters ──────────────────────────────────────────────────────── -->
    <div class="bg-gray-800 border border-gray-700 rounded-xl p-4 mb-5">
      <div class="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-5 gap-3">
        <!-- Search -->
        <div class="lg:col-span-2">
          <input
            v-model="filters.search"
            type="text"
            placeholder="Søk ordre #, navn, e-post…"
            class="w-full bg-gray-900 border border-gray-600 text-gray-100 placeholder:text-gray-500 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
            @keyup.enter="applyFilters"
          />
        </div>
        <!-- Order type -->
        <div>
          <select
            v-model="filters.orderType"
            class="w-full bg-gray-900 border border-gray-600 text-gray-100 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
          >
            <option value="">Alle typer</option>
            <option v-for="t in ALL_ORDER_TYPES" :key="t" :value="t">{{ orderTypeLabel(t) }}</option>
          </select>
        </div>
        <!-- Status -->
        <div>
          <select
            v-model="filters.status"
            class="w-full bg-gray-900 border border-gray-600 text-gray-100 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
          >
            <option value="">Alle statuser</option>
            <option v-for="s in ALL_STATUSES" :key="s" :value="s">{{ statusLabel(s) }}</option>
          </select>
        </div>
        <!-- From date -->
        <div class="flex gap-2">
          <input
            v-model="filters.fromDate"
            type="date"
            class="w-full bg-gray-900 border border-gray-600 text-gray-100 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
            placeholder="Fra dato"
          />
        </div>
      </div>
      <div class="flex flex-wrap gap-2 mt-3 items-center">
        <button
          class="bg-blue-700 text-white px-4 py-2 rounded-lg text-sm font-medium hover:bg-blue-600"
          @click="applyFilters"
        >
          Søk
        </button>
        <button
          class="border border-gray-600 text-gray-300 px-4 py-2 rounded-lg text-sm hover:bg-gray-700"
          @click="clearFilters"
        >
          Nullstill
        </button>
        <!-- BANNERSH-169: opt-in toggle to show AI design-tracking orders (0 kr, no items) -->
        <label
          class="ml-2 inline-flex items-center gap-2 text-sm text-gray-300 cursor-pointer select-none"
          title="Vis AI designforespørsler som ikke er produksjonsordrer (0 kr, ingen varer)"
        >
          <input
            type="checkbox"
            :checked="!filters.excludeZeroValueAiOrders"
            class="accent-cyan-500 w-4 h-4"
            @change="filters.excludeZeroValueAiOrders = !($event.target as HTMLInputElement).checked; applyFilters()"
          />
          Vis AI design-ordrer
        </label>
        <!-- BANNERSH-139: opt-in toggle to reveal credit-pack purchases -->
        <label
          class="ml-2 inline-flex items-center gap-2 text-sm text-gray-300 cursor-pointer select-none"
          title="Vis også AI kreditt-pakke kjøp i listen"
        >
          <input
            type="checkbox"
            v-model="filters.includeCreditPacks"
            class="accent-yellow-500 w-4 h-4"
            @change="applyFilters"
          />
          Inkluder AI-kjøp
        </label>
      </div>
    </div>

    <!-- Loading -->
    <div v-if="loading" class="flex justify-center py-12">
      <div class="w-8 h-8 border-4 border-blue-500 border-t-transparent rounded-full animate-spin" />
    </div>

    <!-- Error -->
    <div v-else-if="error" class="bg-red-900/30 border border-red-700 text-red-400 rounded-xl p-5 text-center">
      {{ error }}
    </div>

    <!-- Empty -->
    <div v-else-if="orders.length === 0" class="bg-gray-800 border border-gray-700 rounded-xl p-12 text-center text-gray-500">
      Ingen ordrer funnet.
    </div>

    <!-- Table -->
    <template v-else>
      <div class="bg-gray-800 border border-gray-700 rounded-xl overflow-hidden">
        <table class="w-full text-sm">
          <thead class="bg-gray-900 border-b border-gray-700">
            <tr>
              <th class="text-left px-4 py-3 font-medium text-gray-400">Ordre #</th>
              <th class="text-left px-4 py-3 font-medium text-gray-400">Type</th>
              <th class="text-left px-4 py-3 font-medium text-gray-400">Kunde</th>
              <th class="text-left px-4 py-3 font-medium text-gray-400">Dato</th>
              <th class="text-left px-4 py-3 font-medium text-gray-400">Tilstand</th>
              <th class="text-left px-4 py-3 font-medium text-gray-400 hidden md:table-cell">Levering</th>
              <th class="text-left px-4 py-3 font-medium text-gray-400 hidden lg:table-cell">Varer</th>
              <th class="text-right px-4 py-3 font-medium text-gray-400">Totalt</th>
            </tr>
          </thead>
          <tbody class="divide-y divide-gray-700">
            <tr
              v-for="order in orders"
              :key="order.id"
              class="hover:bg-gray-700 cursor-pointer transition"
              @click="router.push(`/admin/orders/${order.id}`)"
            >
              <td class="px-4 py-3 font-medium text-blue-400">#{{ order.id }}</td>
              <td class="px-4 py-3">
                <span
                  class="text-xs font-semibold px-2 py-0.5 rounded-full"
                  :class="orderTypeClass(order.orderType)"
                >
                  {{ orderTypeLabel(order.orderType) }}
                </span>
              </td>
              <td class="px-4 py-3">
                <div class="font-medium text-gray-200">{{ order.customerName ?? '—' }}</div>
                <div class="text-xs text-gray-500">{{ order.customerEmail }}</div>
              </td>
              <td class="px-4 py-3 text-gray-400">{{ formatDate(order.createdAt) }}</td>
              <td class="px-4 py-3">
                <span class="text-xs font-semibold px-2 py-0.5 rounded-full" :class="stateClass(order)">
                  {{ stateLabel(order) }}
                </span>
              </td>
              <td class="px-4 py-3 text-gray-400 hidden md:table-cell">{{ deliveryLabel(order.deliveryType) }}</td>
              <td class="px-4 py-3 text-gray-400 hidden lg:table-cell">{{ order.itemCount }} stk</td>
              <td class="px-4 py-3 text-right font-semibold text-gray-100">{{ formatNok(order.totalNok) }}</td>
            </tr>
          </tbody>
        </table>
      </div>

      <!-- Pagination -->
      <div v-if="totalPages > 1" class="flex items-center justify-between mt-4 text-sm">
        <button
          :disabled="!hasPrev"
          class="px-4 py-2 rounded-lg border border-gray-600 text-gray-300 disabled:opacity-40 disabled:cursor-not-allowed hover:bg-gray-700"
          @click="load(page - 1)"
        >
          ← Forrige
        </button>
        <span class="text-gray-400">Side {{ page }} av {{ totalPages }}</span>
        <button
          :disabled="!hasNext"
          class="px-4 py-2 rounded-lg border border-gray-600 text-gray-300 disabled:opacity-40 disabled:cursor-not-allowed hover:bg-gray-700"
          @click="load(page + 1)"
        >
          Neste →
        </button>
      </div>
    </template>
  </div>
</template>

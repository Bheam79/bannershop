<script setup lang="ts">
import { ref, reactive, onMounted, computed } from 'vue'
import { useRouter } from 'vue-router'
import { listAdminOrders } from '@/api/admin'
import type { OrderListItem } from '@/api/orders'

const router = useRouter()

// ── Filter state ──────────────────────────────────────────────────────────────
const filters = reactive({
  status: '',
  fromDate: '',
  toDate: '',
  search: '',
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
      fromUtc: filters.fromDate ? `${filters.fromDate}T00:00:00Z` : undefined,
      toUtc: filters.toDate ? `${filters.toDate}T23:59:59Z` : undefined,
      search: filters.search || undefined,
      page: p,
      pageSize: PAGE_SIZE,
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
  filters.fromDate = ''
  filters.toDate = ''
  filters.search = ''
  load(1)
}

onMounted(() => load(1))

const hasPrev = computed(() => page.value > 1)
const hasNext = computed(() => page.value < totalPages.value)

// ── Helpers ───────────────────────────────────────────────────────────────────
function formatNok(n: number): string {
  return new Intl.NumberFormat('nb-NO', { maximumFractionDigits: 0 }).format(n) + ' kr'
}
function formatDate(iso: string): string {
  return new Date(iso).toLocaleDateString('nb-NO', { day: '2-digit', month: 'short', year: 'numeric' })
}

const STATUS_LABELS: Record<string, string> = {
  Draft: 'Utkast', PendingPayment: 'Venter betaling', Paid: 'Betalt',
  InProduction: 'Under produksjon', ReadyToShip: 'Klar til frakt',
  Shipped: 'Sendt', Delivered: 'Levert', Cancelled: 'Kansellert',
}
const STATUS_CLASSES: Record<string, string> = {
  Draft: 'bg-gray-100 text-gray-600', PendingPayment: 'bg-yellow-100 text-yellow-800',
  Paid: 'bg-blue-100 text-blue-800', InProduction: 'bg-blue-100 text-blue-800',
  ReadyToShip: 'bg-purple-100 text-purple-800', Shipped: 'bg-green-100 text-green-800',
  Delivered: 'bg-green-100 text-green-700', Cancelled: 'bg-red-100 text-red-700',
}
function statusLabel(s: string) { return STATUS_LABELS[s] ?? s }
function statusClass(s: string) { return STATUS_CLASSES[s] ?? 'bg-gray-100 text-gray-600' }
function deliveryLabel(d: string) { return d === 'Express' ? 'Ekspress' : 'Standard' }

const ALL_STATUSES = Object.keys(STATUS_LABELS)
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
      <div class="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-3">
        <!-- Search -->
        <div class="lg:col-span-1">
          <input
            v-model="filters.search"
            type="text"
            placeholder="Søk ordre #, navn, e-post…"
            class="w-full bg-gray-900 border border-gray-600 text-gray-100 placeholder:text-gray-500 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
            @keyup.enter="applyFilters"
          />
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
        <div>
          <input
            v-model="filters.fromDate"
            type="date"
            class="w-full bg-gray-900 border border-gray-600 text-gray-100 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
            placeholder="Fra dato"
          />
        </div>
        <!-- To date -->
        <div>
          <input
            v-model="filters.toDate"
            type="date"
            class="w-full bg-gray-900 border border-gray-600 text-gray-100 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
            placeholder="Til dato"
          />
        </div>
      </div>
      <div class="flex gap-2 mt-3">
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
              <th class="text-left px-4 py-3 font-medium text-gray-400">Kunde</th>
              <th class="text-left px-4 py-3 font-medium text-gray-400">Dato</th>
              <th class="text-left px-4 py-3 font-medium text-gray-400">Status</th>
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
                <div class="font-medium text-gray-200">{{ order.customerName ?? '—' }}</div>
                <div class="text-xs text-gray-500">{{ order.customerEmail }}</div>
              </td>
              <td class="px-4 py-3 text-gray-400">{{ formatDate(order.createdAt) }}</td>
              <td class="px-4 py-3">
                <span class="text-xs font-semibold px-2 py-0.5 rounded-full" :class="statusClass(order.status)">
                  {{ statusLabel(order.status) }}
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

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { listOrders } from '@/api/orders'
import type { OrderListItem } from '@/api/orders'

const router = useRouter()

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
    const result = await listOrders(p, PAGE_SIZE)
    orders.value = result.items
    page.value = result.page
    totalPages.value = result.totalPages
    totalCount.value = result.totalCount
  } catch {
    error.value = 'Kunne ikke laste ordrer. Prøv igjen.'
  } finally {
    loading.value = false
  }
}

onMounted(() => load(1))

function goToOrder(id: number) {
  router.push(`/account/orders/${id}`)
}

function formatNok(n: number): string {
  return new Intl.NumberFormat('nb-NO', { maximumFractionDigits: 0 }).format(n) + ' kr'
}

function formatDate(iso: string): string {
  return new Date(iso).toLocaleDateString('nb-NO', {
    day: '2-digit',
    month: 'short',
    year: 'numeric',
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

function statusLabel(s: string) {
  return STATUS_LABELS[s] ?? s
}
function statusClass(s: string) {
  return STATUS_CLASSES[s] ?? 'bg-gray-100 text-gray-600'
}
function deliveryLabel(d: string) {
  return d === 'Express' ? 'Ekspress' : 'Standard'
}

const hasPrev = computed(() => page.value > 1)
const hasNext = computed(() => page.value < totalPages.value)
</script>

<template>
  <div class="max-w-5xl mx-auto px-4 py-10">
    <div class="flex items-center justify-between mb-6">
      <div>
        <h1 class="text-2xl font-bold text-gray-900">Mine ordrer</h1>
        <p v-if="!loading && totalCount > 0" class="text-sm text-gray-500 mt-0.5">
          {{ totalCount }} ordre{{ totalCount !== 1 ? 'r' : '' }} totalt
        </p>
      </div>
      <RouterLink to="/account" class="text-sm text-blue-700 hover:underline">← Min konto</RouterLink>
    </div>

    <!-- Loading -->
    <div v-if="loading" class="flex justify-center py-16">
      <div class="w-8 h-8 border-4 border-blue-600 border-t-transparent rounded-full animate-spin" />
    </div>

    <!-- Error -->
    <div v-else-if="error" class="bg-red-50 border border-red-200 text-red-800 rounded-xl p-6 text-center">
      {{ error }}
      <button class="mt-2 block mx-auto underline text-sm" @click="load(page)">Prøv igjen</button>
    </div>

    <!-- Empty -->
    <div v-else-if="orders.length === 0" class="text-center py-16 text-gray-500">
      <div class="text-4xl mb-3">📦</div>
      <p class="text-lg font-medium text-gray-700">Ingen ordrer ennå</p>
      <p class="text-sm mt-1">Dine bestillinger vil vises her.</p>
      <RouterLink
        to="/"
        class="mt-5 inline-block bg-blue-700 text-white px-5 py-2 rounded-lg font-medium hover:bg-blue-800 text-sm"
      >
        Handle bannere
      </RouterLink>
    </div>

    <!-- Table -->
    <template v-else>
      <div class="bg-white border border-gray-200 rounded-xl overflow-hidden">
        <!-- Desktop table -->
        <table class="w-full text-sm hidden sm:table">
          <thead class="bg-gray-50 border-b border-gray-200">
            <tr>
              <th class="text-left px-5 py-3 font-semibold text-gray-600">Ordre #</th>
              <th class="text-left px-5 py-3 font-semibold text-gray-600">Dato</th>
              <th class="text-left px-5 py-3 font-semibold text-gray-600">Status</th>
              <th class="text-left px-5 py-3 font-semibold text-gray-600">Levering</th>
              <th class="text-left px-5 py-3 font-semibold text-gray-600">Varer</th>
              <th class="text-right px-5 py-3 font-semibold text-gray-600">Totalt</th>
            </tr>
          </thead>
          <tbody class="divide-y divide-gray-100">
            <tr
              v-for="order in orders"
              :key="order.id"
              class="hover:bg-gray-50 cursor-pointer transition"
              @click="goToOrder(order.id)"
            >
              <td class="px-5 py-4 font-medium text-blue-700">#{{ order.id }}</td>
              <td class="px-5 py-4 text-gray-600">{{ formatDate(order.createdAt) }}</td>
              <td class="px-5 py-4">
                <span
                  class="inline-block text-xs font-semibold px-2.5 py-1 rounded-full"
                  :class="statusClass(order.status)"
                >
                  {{ statusLabel(order.status) }}
                </span>
              </td>
              <td class="px-5 py-4 text-gray-600">{{ deliveryLabel(order.deliveryType) }}</td>
              <td class="px-5 py-4 text-gray-600">{{ order.itemCount }} stk</td>
              <td class="px-5 py-4 text-right font-semibold text-gray-900">{{ formatNok(order.totalNok) }}</td>
            </tr>
          </tbody>
        </table>

        <!-- Mobile card list -->
        <ul class="sm:hidden divide-y divide-gray-100">
          <li
            v-for="order in orders"
            :key="order.id"
            class="px-4 py-4 flex items-center justify-between cursor-pointer hover:bg-gray-50 active:bg-gray-100"
            @click="goToOrder(order.id)"
          >
            <div class="space-y-1">
              <div class="font-medium text-blue-700">#{{ order.id }}</div>
              <div class="text-xs text-gray-500">{{ formatDate(order.createdAt) }} · {{ deliveryLabel(order.deliveryType) }}</div>
              <span
                class="inline-block text-xs font-semibold px-2 py-0.5 rounded-full"
                :class="statusClass(order.status)"
              >
                {{ statusLabel(order.status) }}
              </span>
            </div>
            <div class="text-right">
              <div class="font-semibold text-gray-900">{{ formatNok(order.totalNok) }}</div>
              <div class="text-xs text-gray-400 mt-0.5">{{ order.itemCount }} vare{{ order.itemCount !== 1 ? 'r' : '' }}</div>
            </div>
          </li>
        </ul>
      </div>

      <!-- Pagination -->
      <div v-if="totalPages > 1" class="flex items-center justify-between mt-5 text-sm">
        <button
          :disabled="!hasPrev"
          class="px-4 py-2 rounded-lg border border-gray-300 text-gray-700 disabled:opacity-40 disabled:cursor-not-allowed hover:bg-gray-50 transition"
          @click="load(page - 1)"
        >
          ← Forrige
        </button>
        <span class="text-gray-500">Side {{ page }} av {{ totalPages }}</span>
        <button
          :disabled="!hasNext"
          class="px-4 py-2 rounded-lg border border-gray-300 text-gray-700 disabled:opacity-40 disabled:cursor-not-allowed hover:bg-gray-50 transition"
          @click="load(page + 1)"
        >
          Neste →
        </button>
      </div>
    </template>
  </div>
</template>

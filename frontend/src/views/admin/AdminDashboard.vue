<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { useRouter, RouterLink } from 'vue-router'
import { useAuthStore } from '@/stores/auth'
import { listAdminOrders } from '@/api/admin'
import type { OrderListItem } from '@/api/orders'

const auth = useAuthStore()
const router = useRouter()

// ── KPI state ─────────────────────────────────────────────────────────────────
const kpiLoading = ref(true)
const ordersToday = ref(0)
const ordersThisWeek = ref(0)
const revenueThisMonth = ref(0)
const inProductionCount = ref(0)
const recentOrders = ref<OrderListItem[]>([])

function isoStartOf(date: Date): string {
  const d = new Date(date)
  d.setHours(0, 0, 0, 0)
  return d.toISOString()
}

function mondayOfWeek(date: Date): Date {
  const d = new Date(date)
  const day = d.getDay() || 7
  d.setDate(d.getDate() - day + 1)
  d.setHours(0, 0, 0, 0)
  return d
}

function firstOfMonth(date: Date): Date {
  return new Date(date.getFullYear(), date.getMonth(), 1, 0, 0, 0, 0)
}

onMounted(async () => {
  const now = new Date()
  const todayStart = isoStartOf(now)
  const weekStart = isoStartOf(mondayOfWeek(now))
  const monthStart = firstOfMonth(now).toISOString()

  try {
    const [todayRes, weekRes, monthRes, inProdRes, recentRes] = await Promise.all([
      listAdminOrders({ fromUtc: todayStart, pageSize: 1 }),
      listAdminOrders({ fromUtc: weekStart, pageSize: 1 }),
      listAdminOrders({ fromUtc: monthStart, pageSize: 200 }),
      listAdminOrders({ status: 'InProduction', pageSize: 1 }),
      listAdminOrders({ pageSize: 10 }),
    ])
    ordersToday.value = todayRes.totalCount
    ordersThisWeek.value = weekRes.totalCount
    revenueThisMonth.value = monthRes.items.reduce((sum, o) => sum + o.totalNok, 0)
    inProductionCount.value = inProdRes.totalCount
    recentOrders.value = recentRes.items
  } catch {
    // Non-critical — leave zeros
  } finally {
    kpiLoading.value = false
  }
})

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
</script>

<template>
  <div class="max-w-7xl mx-auto px-4 py-8">
    <div class="mb-8">
      <h1 class="text-2xl font-bold text-gray-900">Admin-panel</h1>
      <p class="text-gray-500 mt-1">Innlogget som <strong>{{ auth.user?.name }}</strong></p>
    </div>

    <!-- ── KPI cards ──────────────────────────────────────────────────────── -->
    <div class="grid grid-cols-2 lg:grid-cols-4 gap-4 mb-8">
      <div class="bg-white rounded-xl border border-gray-200 shadow-sm p-5">
        <div class="text-xs font-semibold uppercase tracking-wider text-gray-400 mb-1">Ordrer i dag</div>
        <div class="text-3xl font-bold text-gray-900">
          <span v-if="kpiLoading" class="text-gray-300">—</span>
          <span v-else>{{ ordersToday }}</span>
        </div>
      </div>
      <div class="bg-white rounded-xl border border-gray-200 shadow-sm p-5">
        <div class="text-xs font-semibold uppercase tracking-wider text-gray-400 mb-1">Denne uken</div>
        <div class="text-3xl font-bold text-gray-900">
          <span v-if="kpiLoading" class="text-gray-300">—</span>
          <span v-else>{{ ordersThisWeek }}</span>
        </div>
      </div>
      <div class="bg-white rounded-xl border border-gray-200 shadow-sm p-5">
        <div class="text-xs font-semibold uppercase tracking-wider text-gray-400 mb-1">Omsetning denne mnd.</div>
        <div class="text-2xl font-bold text-blue-700">
          <span v-if="kpiLoading" class="text-gray-300">—</span>
          <span v-else>{{ formatNok(revenueThisMonth) }}</span>
        </div>
      </div>
      <div class="bg-white rounded-xl border border-gray-200 shadow-sm p-5">
        <div class="text-xs font-semibold uppercase tracking-wider text-gray-400 mb-1">Under produksjon</div>
        <div class="text-3xl font-bold text-purple-700">
          <span v-if="kpiLoading" class="text-gray-300">—</span>
          <span v-else>{{ inProductionCount }}</span>
        </div>
      </div>
    </div>

    <!-- ── Recent orders ──────────────────────────────────────────────────── -->
    <div class="bg-white rounded-xl border border-gray-200 shadow-sm mb-8">
      <div class="flex items-center justify-between px-5 py-4 border-b border-gray-100">
        <h2 class="text-base font-semibold text-gray-900">Siste 10 ordrer</h2>
        <RouterLink to="/admin/orders" class="text-sm text-blue-600 hover:underline">Se alle →</RouterLink>
      </div>

      <div v-if="kpiLoading" class="px-5 py-8 text-center text-gray-400">Laster…</div>
      <div v-else-if="recentOrders.length === 0" class="px-5 py-8 text-center text-gray-400">Ingen ordrer ennå.</div>
      <table v-else class="w-full text-sm">
        <thead class="bg-gray-50 border-b border-gray-100">
          <tr>
            <th class="text-left px-5 py-3 font-medium text-gray-500">Ordre #</th>
            <th class="text-left px-5 py-3 font-medium text-gray-500">Kunde</th>
            <th class="text-left px-5 py-3 font-medium text-gray-500">Dato</th>
            <th class="text-left px-5 py-3 font-medium text-gray-500">Status</th>
            <th class="text-right px-5 py-3 font-medium text-gray-500">Totalt</th>
          </tr>
        </thead>
        <tbody class="divide-y divide-gray-100">
          <tr
            v-for="order in recentOrders"
            :key="order.id"
            class="hover:bg-gray-50 cursor-pointer transition"
            @click="router.push(`/admin/orders/${order.id}`)"
          >
            <td class="px-5 py-3 font-medium text-blue-700">#{{ order.id }}</td>
            <td class="px-5 py-3 text-gray-700">
              <div class="font-medium">{{ order.customerName ?? '—' }}</div>
              <div class="text-xs text-gray-400">{{ order.customerEmail }}</div>
            </td>
            <td class="px-5 py-3 text-gray-500">{{ formatDate(order.createdAt) }}</td>
            <td class="px-5 py-3">
              <span class="text-xs font-semibold px-2 py-0.5 rounded-full" :class="statusClass(order.status)">
                {{ statusLabel(order.status) }}
              </span>
            </td>
            <td class="px-5 py-3 text-right font-semibold text-gray-900">{{ formatNok(order.totalNok) }}</td>
          </tr>
        </tbody>
      </table>
    </div>

    <!-- ── Quick nav tiles ────────────────────────────────────────────────── -->
    <div class="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
      <RouterLink
        to="/admin/orders"
        class="block bg-white rounded-xl border border-gray-200 shadow-sm p-6 hover:border-blue-400 hover:shadow-md transition"
      >
        <div class="text-3xl mb-3">📦</div>
        <h2 class="font-semibold text-gray-800">Ordrer</h2>
        <p class="text-sm text-gray-500 mt-1">Se og administrer alle kundeordrer</p>
      </RouterLink>
      <RouterLink
        to="/admin/sizes"
        class="block bg-white rounded-xl border border-gray-200 shadow-sm p-6 hover:border-blue-400 hover:shadow-md transition"
      >
        <div class="text-3xl mb-3">📐</div>
        <h2 class="font-semibold text-gray-800">Bannerstørrelser</h2>
        <p class="text-sm text-gray-500 mt-1">Administrer størrelser og priser</p>
      </RouterLink>
      <RouterLink
        to="/admin/materials"
        class="block bg-white rounded-xl border border-gray-200 shadow-sm p-6 hover:border-blue-400 hover:shadow-md transition"
      >
        <div class="text-3xl mb-3">🧵</div>
        <h2 class="font-semibold text-gray-800">Materialer</h2>
        <p class="text-sm text-gray-500 mt-1">Administrer banner-materialer</p>
      </RouterLink>
      <RouterLink
        to="/admin/pricing"
        class="block bg-white rounded-xl border border-gray-200 shadow-sm p-6 hover:border-blue-400 hover:shadow-md transition"
      >
        <div class="text-3xl mb-3">💰</div>
        <h2 class="font-semibold text-gray-800">Prissetting</h2>
        <p class="text-sm text-gray-500 mt-1">Juster prisparametere</p>
      </RouterLink>
    </div>
  </div>
</template>

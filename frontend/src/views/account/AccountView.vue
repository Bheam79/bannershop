<script setup lang="ts">
import { ref, reactive, computed, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/auth'
import apiClient from '@/api/client'
import type { User } from '@/types'
import { listOrders } from '@/api/orders'
import type { OrderListItem } from '@/api/orders'

const auth = useAuthStore()
const router = useRouter()

// ── Active orders summary ─────────────────────────────────────────────────────
const recentOrders = ref<OrderListItem[]>([])
const ordersLoading = ref(true)

const ACTIVE_STATUSES = new Set([
  'PendingPayment', 'Paid', 'InProduction', 'ReadyToShip', 'Shipped',
])

const activeOrders = computed(() =>
  recentOrders.value.filter(o => ACTIVE_STATUSES.has(o.status))
)

onMounted(async () => {
  try {
    const result = await listOrders(1, 5)
    recentOrders.value = result.items
  } catch {
    // non-critical — ignore
  } finally {
    ordersLoading.value = false
  }
})

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
function formatNok(n: number): string {
  return new Intl.NumberFormat('nb-NO', { maximumFractionDigits: 0 }).format(n) + ' kr'
}
function formatDate(iso: string): string {
  return new Date(iso).toLocaleDateString('nb-NO', { day: '2-digit', month: 'short', year: 'numeric' })
}

// ── Profile form ──────────────────────────────────────────────────────────────
const profile = reactive({
  name: auth.user?.name ?? '',
  phone: auth.user?.phone ?? '',
})
const profileError = ref('')
const profileSuccess = ref('')
const profileLoading = ref(false)

async function saveProfile() {
  profileError.value = ''
  profileSuccess.value = ''
  profileLoading.value = true
  try {
    const { data } = await apiClient.put<User>('/auth/me', {
      name: profile.name,
      phone: profile.phone || null,
    })
    // Update local auth store
    auth.setAuth({
      accessToken: auth.accessToken!,
      refreshToken: auth.refreshTokenValue!,
      user: data,
    })
    profileSuccess.value = 'Profilen er oppdatert.'
  } catch (err: any) {
    profileError.value = err.response?.data?.error ?? 'Kunne ikke oppdatere profilen.'
  } finally {
    profileLoading.value = false
  }
}

// ── Change password form ──────────────────────────────────────────────────────
const pwForm = reactive({
  currentPassword: '',
  newPassword: '',
  confirmPassword: '',
})
const pwError = ref('')
const pwSuccess = ref('')
const pwLoading = ref(false)

async function changePassword() {
  pwError.value = ''
  pwSuccess.value = ''
  if (pwForm.newPassword !== pwForm.confirmPassword) {
    pwError.value = 'De nye passordene stemmer ikke overens.'
    return
  }
  if (pwForm.newPassword.length < 8) {
    pwError.value = 'Nytt passord må være minst 8 tegn.'
    return
  }
  pwLoading.value = true
  try {
    await apiClient.post('/auth/change-password', {
      currentPassword: pwForm.currentPassword,
      newPassword: pwForm.newPassword,
    })
    pwSuccess.value = 'Passordet er endret.'
    pwForm.currentPassword = ''
    pwForm.newPassword = ''
    pwForm.confirmPassword = ''
  } catch (err: any) {
    pwError.value = err.response?.data?.error ?? 'Kunne ikke endre passordet.'
  } finally {
    pwLoading.value = false
  }
}
</script>

<template>
  <div class="max-w-2xl mx-auto px-4 py-12 space-y-10">
    <div>
      <h1 class="text-2xl font-bold text-gray-900">
        Hei{{ auth.user?.name ? `, ${auth.user.name.split(' ')[0]}` : '' }}! 👋
      </h1>
      <p class="text-gray-500 text-sm mt-1">{{ auth.user?.email }}</p>
    </div>

    <!-- Active orders summary -->
    <div class="bg-white rounded-2xl shadow-sm border border-gray-200 p-6">
      <div class="flex items-center justify-between mb-4">
        <h2 class="text-lg font-semibold text-gray-800">Aktive ordrer</h2>
        <RouterLink to="/account/orders" class="text-sm text-blue-700 hover:underline font-medium">
          Se alle ordrer →
        </RouterLink>
      </div>

      <div v-if="ordersLoading" class="text-center py-4">
        <div class="inline-block w-5 h-5 border-2 border-blue-500 border-t-transparent rounded-full animate-spin" />
      </div>

      <div v-else-if="activeOrders.length === 0 && recentOrders.length === 0" class="text-center py-4 text-gray-400 text-sm">
        Ingen ordrer ennå.
        <RouterLink to="/" class="text-blue-700 hover:underline ml-1">Handle nå</RouterLink>
      </div>

      <div v-else-if="activeOrders.length === 0" class="text-sm text-gray-500 py-2">
        Ingen aktive ordrer for øyeblikket.
        <RouterLink to="/account/orders" class="text-blue-700 hover:underline ml-1">Se ordrehistorikk</RouterLink>
      </div>

      <ul v-else class="divide-y divide-gray-100">
        <li
          v-for="order in activeOrders"
          :key="order.id"
          class="flex items-center justify-between py-3 cursor-pointer hover:bg-gray-50 -mx-2 px-2 rounded-lg transition"
          @click="router.push(`/account/orders/${order.id}`)"
        >
          <div class="space-y-0.5">
            <div class="font-medium text-blue-700 text-sm">#{{ order.id }}</div>
            <div class="text-xs text-gray-400">{{ formatDate(order.createdAt) }}</div>
          </div>
          <div class="flex items-center gap-3">
            <span class="text-xs font-semibold px-2 py-0.5 rounded-full" :class="statusClass(order.status)">
              {{ statusLabel(order.status) }}
            </span>
            <span class="text-sm font-semibold text-gray-800">{{ formatNok(order.totalNok) }}</span>
          </div>
        </li>
      </ul>

      <div v-if="activeOrders.length > 0" class="mt-3 text-center">
        <RouterLink to="/account/orders" class="text-xs text-gray-400 hover:text-blue-700">
          Se alle ordrer →
        </RouterLink>
      </div>
    </div>

    <!-- Profile section -->
    <div class="bg-white rounded-2xl shadow-sm border border-gray-200 p-6">
      <h2 class="text-lg font-semibold text-gray-800 mb-4">Profilinformasjon</h2>
      <form @submit.prevent="saveProfile" class="space-y-4">
        <div>
          <label class="block text-sm font-medium text-gray-700 mb-1">Navn</label>
          <input
            v-model="profile.name"
            type="text"
            required
            class="w-full border border-gray-300 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
          />
        </div>
        <div>
          <label class="block text-sm font-medium text-gray-700 mb-1">Telefon</label>
          <input
            v-model="profile.phone"
            type="tel"
            placeholder="+47 900 00 000"
            class="w-full border border-gray-300 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
          />
        </div>

        <p v-if="profileError" class="text-red-600 text-sm bg-red-50 border border-red-200 rounded-lg px-3 py-2">
          {{ profileError }}
        </p>
        <p v-if="profileSuccess" class="text-green-700 text-sm bg-green-50 border border-green-200 rounded-lg px-3 py-2">
          {{ profileSuccess }}
        </p>

        <button
          type="submit"
          :disabled="profileLoading"
          class="bg-blue-700 text-white px-5 py-2 rounded-lg text-sm font-medium hover:bg-blue-800 disabled:opacity-60 transition"
        >
          {{ profileLoading ? 'Lagrer…' : 'Lagre endringer' }}
        </button>
      </form>
    </div>

    <!-- Change password section -->
    <div class="bg-white rounded-2xl shadow-sm border border-gray-200 p-6">
      <h2 class="text-lg font-semibold text-gray-800 mb-4">Endre passord</h2>
      <form @submit.prevent="changePassword" class="space-y-4">
        <div>
          <label class="block text-sm font-medium text-gray-700 mb-1">Nåværende passord</label>
          <input
            v-model="pwForm.currentPassword"
            type="password"
            required
            autocomplete="current-password"
            class="w-full border border-gray-300 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
          />
        </div>
        <div>
          <label class="block text-sm font-medium text-gray-700 mb-1">Nytt passord</label>
          <input
            v-model="pwForm.newPassword"
            type="password"
            required
            autocomplete="new-password"
            placeholder="Minst 8 tegn"
            class="w-full border border-gray-300 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
          />
        </div>
        <div>
          <label class="block text-sm font-medium text-gray-700 mb-1">Bekreft nytt passord</label>
          <input
            v-model="pwForm.confirmPassword"
            type="password"
            required
            autocomplete="new-password"
            class="w-full border border-gray-300 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
          />
        </div>

        <p v-if="pwError" class="text-red-600 text-sm bg-red-50 border border-red-200 rounded-lg px-3 py-2">
          {{ pwError }}
        </p>
        <p v-if="pwSuccess" class="text-green-700 text-sm bg-green-50 border border-green-200 rounded-lg px-3 py-2">
          {{ pwSuccess }}
        </p>

        <button
          type="submit"
          :disabled="pwLoading"
          class="bg-gray-800 text-white px-5 py-2 rounded-lg text-sm font-medium hover:bg-gray-900 disabled:opacity-60 transition"
        >
          {{ pwLoading ? 'Endrer passord…' : 'Endre passord' }}
        </button>
      </form>
    </div>

  </div>
</template>

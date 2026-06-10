<script setup lang="ts">
import { ref, onMounted, computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import {
  getAdminUser,
  grantAiCredits,
  type AdminUserDetail,
  type AdminAiCreditTransaction,
} from '@/api/admin'
import { formatDateTime as formatDate } from '@/utils/format'

const route = useRoute()
const router = useRouter()

const userId = computed(() => Number(route.params.id))

const user = ref<AdminUserDetail | null>(null)
const loading = ref(true)
const error = ref<string | null>(null)

// Grant-credits form state
const showGrantForm = ref(false)
const grantAmount = ref<number>(10)
const granting = ref(false)
const grantError = ref<string | null>(null)
const grantSuccess = ref<string | null>(null)

async function load() {
  loading.value = true
  error.value = null
  try {
    user.value = await getAdminUser(userId.value)
  } catch {
    error.value = 'Kunne ikke laste brukerdetaljer.'
  } finally {
    loading.value = false
  }
}

onMounted(load)

async function submitGrant() {
  if (!user.value) return
  if (!Number.isFinite(grantAmount.value) || grantAmount.value < 1) {
    grantError.value = 'Antall må være minst 1.'
    return
  }
  if (grantAmount.value > 10000) {
    grantError.value = 'Maks 10 000 kreditter per tildeling.'
    return
  }
  granting.value = true
  grantError.value = null
  grantSuccess.value = null
  try {
    const updated = await grantAiCredits(userId.value, Math.floor(grantAmount.value))
    user.value = updated
    grantSuccess.value = `Ga ${Math.floor(grantAmount.value)} kreditter til ${updated.name}.`
    grantAmount.value = 10
    showGrantForm.value = false
  } catch (err: unknown) {
    const msg = err instanceof Error ? err.message : 'Ukjent feil.'
    grantError.value = `Kunne ikke tildele kreditter: ${msg}`
  } finally {
    granting.value = false
  }
}

const REASON_LABELS: Record<string, string> = {
  FreeAnonymous: 'Gratis (anonym)',
  FreeAuthenticated: 'Gratis (innlogget)',
  CreditPack: 'Kjøpt kredittpakke',
  BannerOrderActivation: 'Banner-ordre aktivering',
  Consumed: 'Forbrukt',
  AdminGrant: 'Admin-tildeling',
}
function reasonLabel(r: string): string {
  return REASON_LABELS[r] ?? r
}
function amountClass(t: AdminAiCreditTransaction): string {
  return t.amount > 0 ? 'text-green-400' : 'text-red-400'
}
function amountStr(t: AdminAiCreditTransaction): string {
  return t.amount > 0 ? `+${t.amount}` : `${t.amount}`
}
</script>

<template>
  <div class="max-w-5xl mx-auto px-4 py-8">
    <!-- Breadcrumb / back -->
    <div class="mb-4">
      <button
        class="text-sm text-blue-400 hover:underline"
        @click="router.push('/admin/users')"
      >
        ← Tilbake til brukere
      </button>
    </div>

    <div v-if="loading" class="flex justify-center py-12">
      <div class="w-8 h-8 border-4 border-blue-500 border-t-transparent rounded-full animate-spin" />
    </div>

    <div
      v-else-if="error"
      class="bg-red-900/30 border border-red-700 text-red-400 rounded-xl p-5 text-center"
    >
      {{ error }}
    </div>

    <template v-else-if="user">
      <!-- Header -->
      <div
        class="bg-gray-800 rounded-xl border border-gray-700 p-6 mb-6 flex flex-col sm:flex-row sm:items-center sm:justify-between gap-4"
      >
        <div>
          <h1 class="text-2xl font-bold text-white">{{ user.name }}</h1>
          <p class="text-sm text-gray-400 mt-0.5">{{ user.email }}</p>
          <p v-if="user.phone" class="text-sm text-gray-400">{{ user.phone }}</p>
        </div>
        <div class="text-right">
          <div class="text-xs uppercase tracking-wider text-gray-400">Rolle</div>
          <div class="text-sm font-semibold text-gray-200">{{ user.role }}</div>
          <div class="text-xs text-gray-500 mt-2">Bruker-ID: #{{ user.id }}</div>
          <div class="text-xs text-gray-500">Registrert: {{ formatDate(user.createdAt) }}</div>
        </div>
      </div>

      <!-- Stats grid -->
      <div class="grid grid-cols-2 sm:grid-cols-4 gap-4 mb-6">
        <div class="bg-gray-800 rounded-xl border border-gray-700 p-5">
          <div class="text-xs font-semibold uppercase tracking-wider text-gray-400 mb-1">
            AI-kreditter
          </div>
          <div class="text-3xl font-bold text-blue-400">{{ user.aiCreditsRemaining }}</div>
        </div>
        <div class="bg-gray-800 rounded-xl border border-gray-700 p-5">
          <div class="text-xs font-semibold uppercase tracking-wider text-gray-400 mb-1">
            Gratis brukt?
          </div>
          <div class="text-2xl font-bold" :class="user.hasUsedFreeAiGeneration ? 'text-yellow-400' : 'text-green-400'">
            {{ user.hasUsedFreeAiGeneration ? 'Ja' : 'Nei' }}
          </div>
        </div>
        <div class="bg-gray-800 rounded-xl border border-gray-700 p-5">
          <div class="text-xs font-semibold uppercase tracking-wider text-gray-400 mb-1">Ordrer</div>
          <div class="text-3xl font-bold text-gray-100">{{ user.orderCount }}</div>
        </div>
        <div class="bg-gray-800 rounded-xl border border-gray-700 p-5">
          <div class="text-xs font-semibold uppercase tracking-wider text-gray-400 mb-1">
            Design-bestillinger
          </div>
          <div class="text-3xl font-bold text-gray-100">{{ user.designRequestCount }}</div>
        </div>
      </div>

      <!-- Grant credits panel -->
      <div class="bg-gray-800 rounded-xl border border-gray-700 mb-6">
        <div class="flex items-center justify-between px-5 py-4 border-b border-gray-700">
          <h2 class="text-base font-semibold text-gray-100">Gi gratis AI-kreditter</h2>
          <button
            v-if="!showGrantForm"
            class="bg-green-700 hover:bg-green-600 text-white text-sm font-medium px-3 py-1.5 rounded-lg"
            @click="
              () => {
                showGrantForm = true
                grantSuccess = null
                grantError = null
              }
            "
          >
            + Tildel kreditter
          </button>
        </div>

        <div v-if="grantSuccess" class="px-5 py-3 bg-green-900/30 border-b border-green-700 text-green-300 text-sm">
          {{ grantSuccess }}
        </div>

        <div v-if="showGrantForm" class="p-5">
          <p class="text-sm text-gray-400 mb-3">
            Kreditter tildeles uten betaling og loggføres som <strong>AdminGrant</strong> i hovedboken
            (uten referanseID — en «null betalingsrad»).
          </p>
          <div class="flex flex-col sm:flex-row gap-3 sm:items-end">
            <div class="flex-1">
              <label class="block text-xs font-medium text-gray-400 mb-1">
                Antall kreditter
              </label>
              <input
                v-model.number="grantAmount"
                type="number"
                min="1"
                max="10000"
                step="1"
                class="w-full bg-gray-900 border border-gray-600 text-gray-100 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-green-500"
              />
            </div>
            <div class="flex gap-2">
              <button
                :disabled="granting"
                class="bg-green-700 hover:bg-green-600 disabled:opacity-50 text-white text-sm font-medium px-4 py-2 rounded-lg"
                @click="submitGrant"
              >
                {{ granting ? 'Tildeler…' : 'Bekreft tildeling' }}
              </button>
              <button
                :disabled="granting"
                class="border border-gray-600 text-gray-300 disabled:opacity-50 px-4 py-2 rounded-lg text-sm hover:bg-gray-700"
                @click="
                  () => {
                    showGrantForm = false
                    grantError = null
                  }
                "
              >
                Avbryt
              </button>
            </div>
          </div>
          <div v-if="grantError" class="mt-3 text-sm text-red-400">{{ grantError }}</div>
        </div>
      </div>

      <!-- Recent transactions -->
      <div class="bg-gray-800 rounded-xl border border-gray-700">
        <div class="px-5 py-4 border-b border-gray-700">
          <h2 class="text-base font-semibold text-gray-100">
            AI-kreditt hovedbok
            <span class="text-xs text-gray-500 font-normal ml-2">(siste 50 rader)</span>
          </h2>
        </div>
        <div v-if="user.recentCreditTransactions.length === 0" class="px-5 py-8 text-center text-gray-500">
          Ingen kreditt-transaksjoner ennå.
        </div>
        <table v-else class="w-full text-sm">
          <thead class="bg-gray-900 border-b border-gray-700">
            <tr>
              <th class="text-left px-5 py-3 font-medium text-gray-400">Tidspunkt</th>
              <th class="text-left px-5 py-3 font-medium text-gray-400">Årsak</th>
              <th class="text-right px-5 py-3 font-medium text-gray-400">Endring</th>
              <th class="text-left px-5 py-3 font-medium text-gray-400">Referanse</th>
            </tr>
          </thead>
          <tbody class="divide-y divide-gray-700">
            <tr v-for="t in user.recentCreditTransactions" :key="t.id">
              <td class="px-5 py-3 text-gray-400 whitespace-nowrap">{{ formatDate(t.createdAt) }}</td>
              <td class="px-5 py-3 text-gray-200">{{ reasonLabel(t.reason) }}</td>
              <td class="px-5 py-3 text-right font-semibold" :class="amountClass(t)">
                {{ amountStr(t) }}
              </td>
              <td class="px-5 py-3 text-gray-500 font-mono text-xs">
                {{ t.referenceId || (t.ipAddress ? `IP ${t.ipAddress}` : '—') }}
              </td>
            </tr>
          </tbody>
        </table>
      </div>
    </template>
  </div>
</template>

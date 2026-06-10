<script setup lang="ts">
import { ref, reactive, onMounted, computed } from 'vue'
import { useRouter } from 'vue-router'
import { listAdminDesignRequests } from '@/api/admin'
import { fetchTemplates } from '@/api/designRequests'
import type { AdminDesignRequestListItem } from '@/api/admin'
import type { BannerTemplateItem } from '@/api/designRequests'
import { formatNok, formatDate } from '@/utils/format'
import {
  DR_STATUS_LABELS as STATUS_LABELS,
  DR_STATUS_ADMIN_CLASSES as STATUS_CLASSES,
  drStatusLabel as statusLabel,
  drStatusAdminClass as statusClass,
} from '@/utils/orderStatus'

const router = useRouter()

// ── Filter state ──────────────────────────────────────────────────────────────
const filters = reactive({
  status: '',
  mode: '',
  search: '',
})

// ── Table state ───────────────────────────────────────────────────────────────
const requests = ref<AdminDesignRequestListItem[]>([])
const page = ref(1)
const totalPages = ref(1)
const totalCount = ref(0)
const loading = ref(true)
const error = ref<string | null>(null)
const PAGE_SIZE = 20

const templates = ref<BannerTemplateItem[]>([])

async function load(p = 1) {
  loading.value = true
  error.value = null
  try {
    const result = await listAdminDesignRequests({
      status: filters.status || undefined,
      mode: filters.mode || undefined,
      search: filters.search || undefined,
      page: p,
      pageSize: PAGE_SIZE,
    })
    requests.value = result.items
    page.value = result.page
    totalPages.value = result.totalPages
    totalCount.value = result.totalCount
  } catch {
    error.value = 'Kunne ikke laste design-bestillinger.'
  } finally {
    loading.value = false
  }
}

function applyFilters() { load(1) }
function clearFilters() {
  filters.status = ''
  filters.mode = ''
  filters.search = ''
  load(1)
}

onMounted(async () => {
  const [, tpl] = await Promise.allSettled([load(1), fetchTemplates()])
  if (tpl.status === 'fulfilled') templates.value = tpl.value
})

const hasPrev = computed(() => page.value > 1)
const hasNext = computed(() => page.value < totalPages.value)

// ── Helpers ───────────────────────────────────────────────────────────────────
function templateName(id: number): string {
  return templates.value.find(t => t.id === id)?.nameNb ?? `Mal #${id}`
}

function modeLabel(m: string): string {
  return m === 'Manual' ? 'Manuell' : 'AI'
}

const ALL_STATUSES = Object.keys(STATUS_LABELS)
</script>

<template>
  <div class="max-w-7xl mx-auto px-4 py-8">
    <div class="flex items-center justify-between mb-6">
      <div>
        <h1 class="text-2xl font-bold text-white">Design-bestillinger</h1>
        <p v-if="!loading" class="text-sm text-gray-400 mt-0.5">
          {{ totalCount }} bestilling{{ totalCount !== 1 ? 'er' : '' }} totalt
        </p>
      </div>
    </div>

    <!-- ── Filters ───────────────────────────────────────────────────────── -->
    <div class="bg-gray-800 border border-gray-700 rounded-xl p-4 mb-5">
      <div class="grid grid-cols-1 sm:grid-cols-3 gap-3">
        <!-- Search -->
        <input
          v-model="filters.search"
          type="text"
          placeholder="Søk navn, e-post…"
          class="bg-gray-900 border border-gray-600 text-gray-100 placeholder:text-gray-500 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
          @keyup.enter="applyFilters"
        />
        <!-- Status -->
        <select
          v-model="filters.status"
          class="bg-gray-900 border border-gray-600 text-gray-100 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
        >
          <option value="">Alle statuser</option>
          <option v-for="s in ALL_STATUSES" :key="s" :value="s">{{ statusLabel(s) }}</option>
        </select>
        <!-- Mode -->
        <select
          v-model="filters.mode"
          class="bg-gray-900 border border-gray-600 text-gray-100 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
        >
          <option value="">Alle modi</option>
          <option value="Ai">AI (95 kr)</option>
          <option value="Manual">Manuell (495 kr)</option>
        </select>
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
    <div
      v-else-if="error"
      class="bg-red-900/30 border border-red-700 text-red-400 rounded-xl p-5 text-center"
    >
      {{ error }}
    </div>

    <!-- Empty -->
    <div
      v-else-if="requests.length === 0"
      class="bg-gray-800 border border-gray-700 rounded-xl p-12 text-center text-gray-500"
    >
      Ingen design-bestillinger funnet.
    </div>

    <!-- Table -->
    <template v-else>
      <div class="bg-gray-800 border border-gray-700 rounded-xl overflow-hidden">
        <table class="w-full text-sm">
          <thead class="bg-gray-900 border-b border-gray-700">
            <tr>
              <th class="text-left px-4 py-3 font-medium text-gray-400">ID</th>
              <th class="text-left px-4 py-3 font-medium text-gray-400">Kunde</th>
              <th class="text-left px-4 py-3 font-medium text-gray-400 hidden md:table-cell">Mal</th>
              <th class="text-left px-4 py-3 font-medium text-gray-400">Modus</th>
              <th class="text-left px-4 py-3 font-medium text-gray-400">Status</th>
              <th class="text-left px-4 py-3 font-medium text-gray-400 hidden lg:table-cell">Innsendt</th>
              <th class="text-right px-4 py-3 font-medium text-gray-400">Pris</th>
            </tr>
          </thead>
          <tbody class="divide-y divide-gray-700">
            <tr
              v-for="req in requests"
              :key="req.id"
              class="hover:bg-gray-700 cursor-pointer transition"
              @click="router.push(`/admin/design-requests/${req.id}`)"
            >
              <td class="px-4 py-3 font-medium text-blue-400">#{{ req.id }}</td>
              <td class="px-4 py-3">
                <div class="font-medium text-gray-200">{{ req.customerName }}</div>
                <div class="text-xs text-gray-500">{{ req.customerEmail }}</div>
              </td>
              <td class="px-4 py-3 text-gray-400 hidden md:table-cell">{{ templateName(req.bannerTemplateId) }}</td>
              <td class="px-4 py-3">
                <span
                  class="text-xs font-semibold px-2 py-0.5 rounded-full"
                  :class="req.mode === 'Manual'
                    ? 'bg-indigo-900/50 text-indigo-300'
                    : 'bg-cyan-900/50 text-cyan-300'"
                >
                  {{ modeLabel(req.mode) }}
                </span>
              </td>
              <td class="px-4 py-3">
                <span
                  class="text-xs font-semibold px-2 py-0.5 rounded-full"
                  :class="statusClass(req.status)"
                >
                  {{ statusLabel(req.status) }}
                </span>
              </td>
              <td class="px-4 py-3 text-gray-400 hidden lg:table-cell">{{ formatDate(req.createdAt) }}</td>
              <td class="px-4 py-3 text-right font-semibold text-gray-100">{{ formatNok(req.priceNok) }}</td>
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

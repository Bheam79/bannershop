<script setup lang="ts">
import { ref, reactive, onMounted, computed } from 'vue'
import { useRouter } from 'vue-router'
import { listAdminDesignRequests } from '@/api/admin'
import type { AdminDesignRequestListItem } from '@/api/admin'
import { formatDateTime } from '@/utils/format'
import {
  DR_STATUS_LABELS as STATUS_LABELS,
  drStatusLabel as statusLabel,
  drStatusAdminClass as statusClass,
} from '@/utils/orderStatus'

const router = useRouter()

// ── Filter state ──────────────────────────────────────────────────────────────
const filters = reactive({
  status: '',
  search: '',
})

// ── Table state ───────────────────────────────────────────────────────────────
const items = ref<AdminDesignRequestListItem[]>([])
const page = ref(1)
const totalPages = ref(1)
const totalCount = ref(0)
const loading = ref(true)
const error = ref<string | null>(null)
const PAGE_SIZE = 24

async function load(p = 1) {
  loading.value = true
  error.value = null
  try {
    const result = await listAdminDesignRequests({
      mode: 'Ai',
      status: filters.status || undefined,
      search: filters.search || undefined,
      page: p,
      pageSize: PAGE_SIZE,
    })
    items.value = result.items
    page.value = result.page
    totalPages.value = result.totalPages
    totalCount.value = result.totalCount
  } catch {
    error.value = 'Kunne ikke laste AI-generasjoner.'
  } finally {
    loading.value = false
  }
}

function applyFilters() { load(1) }
function clearFilters() {
  filters.status = ''
  filters.search = ''
  load(1)
}

onMounted(() => load(1))

const hasPrev = computed(() => page.value > 1)
const hasNext = computed(() => page.value < totalPages.value)

const ALL_STATUSES = Object.keys(STATUS_LABELS)

function isAnon(item: AdminDesignRequestListItem) {
  return item.userId === 0 || item.ipAddress != null
}
</script>

<template>
  <div class="max-w-7xl mx-auto px-4 py-8">
    <!-- Header -->
    <div class="flex items-center justify-between mb-6">
      <div>
        <h1 class="text-2xl font-bold text-white">AI-generasjoner</h1>
        <p v-if="!loading" class="text-sm text-gray-400 mt-0.5">
          {{ totalCount }} generasjon{{ totalCount !== 1 ? 'er' : '' }} totalt
        </p>
      </div>
    </div>

    <!-- Filters -->
    <div class="bg-gray-800 border border-gray-700 rounded-xl p-4 mb-5">
      <div class="grid grid-cols-1 sm:grid-cols-2 gap-3">
        <!-- Search -->
        <input
          v-model="filters.search"
          type="text"
          placeholder="Søk navn, e-post, tema…"
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
    <div v-if="loading" class="flex justify-center py-16">
      <div class="w-8 h-8 border-4 border-blue-500 border-t-transparent rounded-full animate-spin" />
    </div>

    <!-- Error -->
    <div
      v-else-if="error"
      class="bg-red-900/30 border border-red-700 text-red-400 rounded-xl p-6 text-center"
    >
      {{ error }}
    </div>

    <!-- Empty -->
    <div
      v-else-if="items.length === 0"
      class="bg-gray-800 border border-gray-700 rounded-xl p-12 text-center text-gray-500"
    >
      Ingen AI-generasjoner funnet.
    </div>

    <!-- Gallery grid -->
    <template v-else>
      <div class="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4 gap-4 mb-6">
        <div
          v-for="item in items"
          :key="item.id"
          class="bg-gray-800 border border-gray-700 rounded-xl overflow-hidden cursor-pointer hover:border-blue-500 transition group"
          @click="router.push(`/admin/design-requests/${item.id}`)"
        >
          <!-- Preview image -->
          <div class="relative aspect-video bg-gray-900 flex items-center justify-center overflow-hidden">
            <img
              v-if="item.previewUrl"
              :src="item.previewUrl"
              :alt="`AI-generasjon #${item.id}`"
              class="w-full h-full object-cover group-hover:scale-105 transition-transform duration-300"
            />
            <div v-else class="flex flex-col items-center gap-2 text-gray-600">
              <svg class="w-10 h-10" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                <path stroke-linecap="round" stroke-linejoin="round" stroke-width="1.5"
                  d="M4 16l4.586-4.586a2 2 0 012.828 0L16 16m-2-2l1.586-1.586a2 2 0 012.828 0L20 14m-6-6h.01M6 20h12a2 2 0 002-2V6a2 2 0 00-2-2H6a2 2 0 00-2 2v12a2 2 0 002 2z" />
              </svg>
              <span class="text-xs">Ingen bilde</span>
            </div>

            <!-- Status badge overlay -->
            <span
              class="absolute top-2 right-2 text-xs font-semibold px-2 py-0.5 rounded-full shadow"
              :class="statusClass(item.status)"
            >
              {{ statusLabel(item.status) }}
            </span>

            <!-- Anonymous badge -->
            <span
              v-if="isAnon(item)"
              class="absolute top-2 left-2 text-xs font-semibold px-2 py-0.5 rounded-full bg-yellow-900/80 text-yellow-300 shadow"
            >
              Anonym
            </span>
          </div>

          <!-- Card body -->
          <div class="p-3 space-y-2">
            <!-- ID + date -->
            <div class="flex items-center justify-between">
              <span class="text-xs font-semibold text-blue-400">#{{ item.id }}</span>
              <span class="text-xs text-gray-500">{{ formatDateTime(item.createdAt) }}</span>
            </div>

            <!-- Person name -->
            <div class="text-sm font-medium text-gray-200 truncate">
              {{ item.personName || '—' }}
              <span v-if="item.personAge" class="text-gray-400 font-normal">, {{ item.personAge }} år</span>
            </div>

            <!-- Theme description -->
            <div
              v-if="item.themeDescription"
              class="text-xs text-gray-400 line-clamp-2 leading-relaxed"
              :title="item.themeDescription"
            >
              {{ item.themeDescription }}
            </div>

            <!-- Customer / IP -->
            <div class="pt-1 border-t border-gray-700">
              <div class="text-xs text-gray-300 truncate font-medium">
                {{ item.customerName }}
              </div>
              <div v-if="item.customerEmail" class="text-xs text-gray-500 truncate">
                {{ item.customerEmail }}
              </div>
              <div v-if="item.ipAddress" class="text-xs text-yellow-600 font-mono mt-0.5">
                IP: {{ item.ipAddress }}
              </div>
            </div>
          </div>
        </div>
      </div>

      <!-- Pagination -->
      <div v-if="totalPages > 1" class="flex items-center justify-between text-sm">
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

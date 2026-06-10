<script setup lang="ts">
import { ref, reactive, onMounted, computed } from 'vue'
import { useRouter } from 'vue-router'
import { listAdminUsers, type AdminUserListItem } from '@/api/admin'
import { formatDate } from '@/utils/format'

const router = useRouter()

// ── Filter state ──────────────────────────────────────────────────────────────
const filters = reactive({
  search: '',
})

// ── Table state ───────────────────────────────────────────────────────────────
const users = ref<AdminUserListItem[]>([])
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
    const result = await listAdminUsers({
      search: filters.search || undefined,
      page: p,
      pageSize: PAGE_SIZE,
    })
    users.value = result.items
    page.value = result.page
    totalPages.value = result.totalPages
    totalCount.value = result.totalCount
  } catch {
    error.value = 'Kunne ikke laste brukere.'
  } finally {
    loading.value = false
  }
}

function applyFilters() {
  load(1)
}

function clearFilters() {
  filters.search = ''
  load(1)
}

onMounted(() => load(1))

const hasPrev = computed(() => page.value > 1)
const hasNext = computed(() => page.value < totalPages.value)

function roleLabel(role: string): string {
  return role === 'Admin' ? 'Admin' : 'Kunde'
}
function roleClass(role: string): string {
  return role === 'Admin'
    ? 'bg-purple-900/40 text-purple-300 border border-purple-700'
    : 'bg-gray-700 text-gray-300 border border-gray-600'
}
</script>

<template>
  <div class="max-w-7xl mx-auto px-4 py-8">
    <div class="flex items-center justify-between mb-6">
      <div>
        <h1 class="text-2xl font-bold text-white">Brukere</h1>
        <p v-if="!loading" class="text-sm text-gray-400 mt-0.5">
          {{ totalCount }} bruker{{ totalCount !== 1 ? 'e' : '' }} totalt
        </p>
      </div>
    </div>

    <!-- ── Filters ──────────────────────────────────────────────────────── -->
    <div class="bg-gray-800 border border-gray-700 rounded-xl p-4 mb-5">
      <div class="flex flex-col sm:flex-row gap-3">
        <div class="flex-1">
          <input
            v-model="filters.search"
            type="text"
            placeholder="Søk på navn, e-post, telefon eller bruker-ID…"
            class="w-full bg-gray-900 border border-gray-600 text-gray-100 placeholder:text-gray-500 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
            @keyup.enter="applyFilters"
          />
        </div>
        <div class="flex gap-2">
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
      v-else-if="users.length === 0"
      class="bg-gray-800 border border-gray-700 rounded-xl p-12 text-center text-gray-500"
    >
      Ingen brukere funnet.
    </div>

    <!-- Table -->
    <template v-else>
      <div class="bg-gray-800 border border-gray-700 rounded-xl overflow-hidden">
        <table class="w-full text-sm">
          <thead class="bg-gray-900 border-b border-gray-700">
            <tr>
              <th class="text-left px-4 py-3 font-medium text-gray-400">ID</th>
              <th class="text-left px-4 py-3 font-medium text-gray-400">Navn</th>
              <th class="text-left px-4 py-3 font-medium text-gray-400">E-post</th>
              <th class="text-left px-4 py-3 font-medium text-gray-400 hidden md:table-cell">
                Telefon
              </th>
              <th class="text-left px-4 py-3 font-medium text-gray-400">Rolle</th>
              <th class="text-right px-4 py-3 font-medium text-gray-400">AI-kreditter</th>
              <th class="text-right px-4 py-3 font-medium text-gray-400 hidden lg:table-cell">
                Ordrer
              </th>
              <th class="text-left px-4 py-3 font-medium text-gray-400 hidden md:table-cell">
                Opprettet
              </th>
            </tr>
          </thead>
          <tbody class="divide-y divide-gray-700">
            <tr
              v-for="u in users"
              :key="u.id"
              class="hover:bg-gray-700 cursor-pointer transition"
              @click="router.push(`/admin/users/${u.id}`)"
            >
              <td class="px-4 py-3 font-medium text-blue-400">#{{ u.id }}</td>
              <td class="px-4 py-3 text-gray-200">{{ u.name }}</td>
              <td class="px-4 py-3 text-gray-400">{{ u.email }}</td>
              <td class="px-4 py-3 text-gray-400 hidden md:table-cell">{{ u.phone || '—' }}</td>
              <td class="px-4 py-3">
                <span class="text-xs font-semibold px-2 py-0.5 rounded-full" :class="roleClass(u.role)">
                  {{ roleLabel(u.role) }}
                </span>
              </td>
              <td class="px-4 py-3 text-right font-semibold text-gray-100">
                {{ u.aiCreditsRemaining }}
              </td>
              <td class="px-4 py-3 text-right text-gray-400 hidden lg:table-cell">
                {{ u.orderCount }}
              </td>
              <td class="px-4 py-3 text-gray-400 hidden md:table-cell">
                {{ formatDate(u.createdAt) }}
              </td>
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

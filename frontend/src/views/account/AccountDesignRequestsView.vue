<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { useRouter, RouterLink } from 'vue-router'
import { listDesignRequests, fetchTemplates } from '@/api/designRequests'
import type { DesignRequestListItem, BannerTemplateItem } from '@/api/designRequests'

const router = useRouter()

const requests = ref<DesignRequestListItem[]>([])
const templates = ref<BannerTemplateItem[]>([])
const loading = ref(true)
const error = ref<string | null>(null)

onMounted(async () => {
  loading.value = true
  error.value = null
  try {
    const [reqs, tpls] = await Promise.all([listDesignRequests(), fetchTemplates()])
    requests.value = reqs
    templates.value = tpls
  } catch {
    error.value = 'Kunne ikke laste design-bestillinger. Prøv igjen.'
  } finally {
    loading.value = false
  }
})

function templateName(id: number): string {
  return templates.value.find(t => t.id === id)?.nameNb ?? `Mal #${id}`
}

function modeLabel(m: string): string {
  return m === 'Manual' ? 'Manuell' : 'AI'
}

function formatDate(iso: string): string {
  return new Date(iso).toLocaleDateString('nb-NO', {
    day: '2-digit', month: 'short', year: 'numeric',
  })
}

function formatNok(n: number): string {
  return new Intl.NumberFormat('nb-NO', { maximumFractionDigits: 0 }).format(n) + ' kr'
}

const STATUS_LABELS: Record<string, string> = {
  Pending:           'Venter',
  InProgress:        'Under arbeid',
  AwaitingApproval:  'Klar til godkjenning',
  Approved:          'Godkjent',
  RevisionRequested: 'Revisjon bedt',
  Revised:           'Revidert',
  Final:             'Levert',
  Failed:            'Feilet',
  Cancelled:         'Kansellert',
}
const STATUS_CLASSES: Record<string, string> = {
  Pending:           'bg-yellow-100 text-yellow-800',
  InProgress:        'bg-blue-100 text-blue-800',
  AwaitingApproval:  'bg-purple-100 text-purple-800',
  Approved:          'bg-green-100 text-green-700',
  RevisionRequested: 'bg-orange-100 text-orange-800',
  Revised:           'bg-sky-100 text-sky-800',
  Final:             'bg-green-100 text-green-800',
  Failed:            'bg-red-100 text-red-700',
  Cancelled:         'bg-red-100 text-red-700',
}
function statusLabel(s: string) { return STATUS_LABELS[s] ?? s }
function statusClass(s: string) { return STATUS_CLASSES[s] ?? 'bg-gray-100 text-gray-600' }
</script>

<template>
  <div class="max-w-5xl mx-auto px-4 py-10">
    <div class="flex items-center justify-between mb-6">
      <div>
        <h1 class="text-2xl font-bold text-gray-900">Mine design-bestillinger</h1>
        <p v-if="!loading && requests.length > 0" class="text-sm text-gray-500 mt-0.5">
          {{ requests.length }} bestilling{{ requests.length !== 1 ? 'er' : '' }} totalt
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
    </div>

    <!-- Empty -->
    <div v-else-if="requests.length === 0" class="text-center py-16 text-gray-500">
      <div class="text-4xl mb-3">🎨</div>
      <p class="text-lg font-medium text-gray-700">Ingen design-bestillinger ennå</p>
      <p class="text-sm mt-1">Bestill et AI-generert eller manuelt designet banner.</p>
      <div class="mt-5 flex justify-center gap-3">
        <RouterLink
          to="/banner-builder/ai"
          class="bg-blue-700 text-white px-5 py-2 rounded-lg font-medium hover:bg-blue-800 text-sm"
        >
          AI-banner (95 kr)
        </RouterLink>
        <RouterLink
          to="/banner-builder/manual"
          class="bg-indigo-700 text-white px-5 py-2 rounded-lg font-medium hover:bg-indigo-800 text-sm"
        >
          Manuelt design (495 kr)
        </RouterLink>
      </div>
    </div>

    <!-- Table -->
    <template v-else>
      <div class="bg-white border border-gray-200 rounded-xl overflow-hidden">
        <!-- Desktop table -->
        <table class="w-full text-sm hidden sm:table">
          <thead class="bg-gray-50 border-b border-gray-200">
            <tr>
              <th class="text-left px-5 py-3 font-semibold text-gray-600">ID</th>
              <th class="text-left px-5 py-3 font-semibold text-gray-600">Mal</th>
              <th class="text-left px-5 py-3 font-semibold text-gray-600">Modus</th>
              <th class="text-left px-5 py-3 font-semibold text-gray-600">Status</th>
              <th class="text-left px-5 py-3 font-semibold text-gray-600">Dato</th>
              <th class="text-right px-5 py-3 font-semibold text-gray-600">Pris</th>
            </tr>
          </thead>
          <tbody class="divide-y divide-gray-100">
            <tr
              v-for="req in requests"
              :key="req.id"
              class="hover:bg-gray-50 cursor-pointer transition"
              @click="router.push(`/account/design-requests/${req.id}`)"
            >
              <td class="px-5 py-4 font-medium text-blue-700">#{{ req.id }}</td>
              <td class="px-5 py-4 text-gray-700">{{ templateName(req.bannerTemplateId) }}</td>
              <td class="px-5 py-4">
                <span
                  class="text-xs font-semibold px-2 py-0.5 rounded-full"
                  :class="req.mode === 'Manual'
                    ? 'bg-indigo-100 text-indigo-800'
                    : 'bg-cyan-100 text-cyan-800'"
                >
                  {{ modeLabel(req.mode) }}
                </span>
              </td>
              <td class="px-5 py-4">
                <span
                  class="text-xs font-semibold px-2 py-0.5 rounded-full"
                  :class="statusClass(req.status)"
                >
                  {{ statusLabel(req.status) }}
                </span>
              </td>
              <td class="px-5 py-4 text-gray-600">{{ formatDate(req.createdAt) }}</td>
              <td class="px-5 py-4 text-right font-semibold text-gray-800">{{ formatNok(req.priceNok) }}</td>
            </tr>
          </tbody>
        </table>

        <!-- Mobile card list -->
        <ul class="sm:hidden divide-y divide-gray-100">
          <li
            v-for="req in requests"
            :key="req.id"
            class="px-4 py-4 flex items-center justify-between cursor-pointer hover:bg-gray-50"
            @click="router.push(`/account/design-requests/${req.id}`)"
          >
            <div class="space-y-1">
              <div class="font-medium text-blue-700">#{{ req.id }}</div>
              <div class="text-xs text-gray-600">{{ templateName(req.bannerTemplateId) }}</div>
              <div class="flex gap-1.5 flex-wrap">
                <span
                  class="text-xs font-semibold px-2 py-0.5 rounded-full"
                  :class="req.mode === 'Manual'
                    ? 'bg-indigo-100 text-indigo-800'
                    : 'bg-cyan-100 text-cyan-800'"
                >
                  {{ modeLabel(req.mode) }}
                </span>
                <span
                  class="text-xs font-semibold px-2 py-0.5 rounded-full"
                  :class="statusClass(req.status)"
                >
                  {{ statusLabel(req.status) }}
                </span>
              </div>
            </div>
            <div class="text-right shrink-0 ml-4">
              <div class="font-semibold text-gray-800 text-sm">{{ formatNok(req.priceNok) }}</div>
              <div class="text-xs text-gray-400 mt-0.5">{{ formatDate(req.createdAt) }}</div>
            </div>
          </li>
        </ul>
      </div>
    </template>
  </div>
</template>

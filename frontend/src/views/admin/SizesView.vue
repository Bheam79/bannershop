<script setup lang="ts">
import { ref, reactive, onMounted, computed } from 'vue'
import apiClient from '@/api/client'
import type { BannerSize, Material } from '@/types'

const sizes = ref<BannerSize[]>([])
const materials = ref<Material[]>([])
const loading = ref(false)
const error = ref('')

// Modal
const showModal = ref(false)
const isEditing = ref(false)
const saving = ref(false)
const modalError = ref('')
const form = reactive({
  id: 0,
  widthCm: null as number | null,
  heightCm: 150,
  isCustomWidth: false,
  name: '',
  isActive: true,
  materialId: 1,
  fixedPrice: null as number | null,
  sortOrder: 0,
})

const hasFixedPrice = ref(false)

async function load() {
  loading.value = true
  error.value = ''
  try {
    const [sizesRes, materialsRes] = await Promise.all([
      apiClient.get<BannerSize[]>('/admin/sizes'),
      apiClient.get<Material[]>('/admin/materials'),
    ])
    sizes.value = sizesRes.data
    materials.value = materialsRes.data
  } catch {
    error.value = 'Kunne ikke laste data.'
  } finally {
    loading.value = false
  }
}

function openCreate() {
  isEditing.value = false
  hasFixedPrice.value = false
  Object.assign(form, {
    id: 0, widthCm: null, heightCm: 150, isCustomWidth: false,
    name: '', isActive: true, materialId: materials.value[0]?.id ?? 1,
    fixedPrice: null, sortOrder: (sizes.value.length + 1) * 10,
  })
  modalError.value = ''
  showModal.value = true
}

function openEdit(s: BannerSize) {
  isEditing.value = true
  hasFixedPrice.value = s.fixedPrice != null
  Object.assign(form, {
    id: s.id, widthCm: s.widthCm, heightCm: s.heightCm,
    isCustomWidth: s.isCustomWidth, name: s.name, isActive: s.isActive,
    materialId: s.materialId, fixedPrice: s.fixedPrice, sortOrder: s.sortOrder,
  })
  modalError.value = ''
  showModal.value = true
}

async function save() {
  modalError.value = ''
  saving.value = true
  const payload = {
    widthCm: form.isCustomWidth ? null : form.widthCm,
    heightCm: form.heightCm,
    isCustomWidth: form.isCustomWidth,
    name: form.name,
    isActive: form.isActive,
    materialId: form.materialId,
    fixedPrice: hasFixedPrice.value ? form.fixedPrice : null,
    sortOrder: form.sortOrder,
  }
  try {
    if (isEditing.value) {
      await apiClient.put(`/admin/sizes/${form.id}`, payload)
    } else {
      await apiClient.post('/admin/sizes', payload)
    }
    showModal.value = false
    await load()
  } catch (err: any) {
    modalError.value = err.response?.data?.error ?? 'Lagring feilet.'
  } finally {
    saving.value = false
  }
}

async function deleteSize(s: BannerSize) {
  if (!confirm(`Slett størrelsen «${s.name}»?`)) return
  try {
    await apiClient.delete(`/admin/sizes/${s.id}`)
    await load()
  } catch (err: any) {
    alert(err.response?.data?.error ?? 'Sletting feilet.')
  }
}

function formatPrice(s: BannerSize): string {
  const p = s.fixedPrice ?? s.calculatedPrice
  return p != null ? `${p.toFixed(0)} NOK` : '—'
}

/**
 * Compute how many panels the given size needs based on the material's
 * MaxBannerWidthCm. Mirrors PricingService.PanelsNeeded server-side.
 * Returns null when width or material data are unavailable.
 *
 * BANNERSH-125: uses the minimum of width and height, since the banner is
 * oriented on the material roll so its smaller dimension runs along the roll
 * width (e.g. 300 × 150 cm on 160 cm material → height=150 fits, 1 panel).
 */
function panelsNeeded(s: BannerSize): number | null {
  if (s.fixedPrice != null) return null // fixed-price: panels irrelevant
  const mat = s.material
  if (!mat) return null
  const maxWidth = mat.maxBannerWidthCm || mat.widthCm
  if (!maxWidth) return null
  const rawW = s.isCustomWidth ? null : s.widthCm
  if (!rawW) return null // custom without width — panels unknown
  // Use the minimum dimension: the banner is oriented so its smaller side runs
  // along the material roll width. Only if even the smaller side exceeds the
  // roll width do we need multiple panels.
  const w = Math.min(rawW, s.heightCm)
  if (w <= maxWidth) return 1
  const overlap = 5 // matches banner_panel_overlap_cm default
  const safeOverlap = Math.max(0, Math.min(overlap, maxWidth - 1))
  return Math.ceil((w - safeOverlap) / (maxWidth - safeOverlap))
}

function formatDate(d: string | null | undefined) {
  if (!d) return ''
  const date = new Date(d)
  return date > new Date() ? date.toLocaleDateString('no-NO') : ''
}

onMounted(load)
</script>

<template>
  <div class="max-w-6xl mx-auto px-4 py-10">
    <div class="flex items-center justify-between mb-6">
      <h1 class="text-2xl font-bold text-white">Bannerstørrelser</h1>
      <button @click="openCreate" class="bg-blue-700 text-white px-4 py-2 rounded-lg text-sm font-medium hover:bg-blue-600">
        + Ny størrelse
      </button>
    </div>

    <p v-if="loading" class="text-gray-400">Laster…</p>
    <p v-else-if="error" class="text-red-400">{{ error }}</p>

    <div v-else class="bg-gray-800 rounded-xl border border-gray-700 overflow-hidden">
      <table class="w-full text-sm">
        <thead class="bg-gray-900 border-b border-gray-700">
          <tr>
            <th class="text-left px-4 py-3 font-medium text-gray-400">Navn</th>
            <th class="text-left px-4 py-3 font-medium text-gray-400">Materiale</th>
            <th class="text-left px-4 py-3 font-medium text-gray-400">Pris</th>
            <th class="text-left px-4 py-3 font-medium text-gray-400">Status</th>
            <th class="text-left px-4 py-3 font-medium text-gray-400">Tilg. fra</th>
            <th class="px-4 py-3"></th>
          </tr>
        </thead>
        <tbody class="divide-y divide-gray-700">
          <tr v-for="s in sizes" :key="s.id" class="hover:bg-gray-700">
            <td class="px-4 py-3 font-medium text-gray-200">
              {{ s.name }}
              <span v-if="s.isCustomWidth" class="ml-1 text-xs bg-purple-900/60 text-purple-300 px-1.5 py-0.5 rounded">Custom</span>
            </td>
            <td class="px-4 py-3 text-gray-400 text-xs">{{ s.material?.name }}</td>
            <td class="px-4 py-3 text-gray-300">
              {{ formatPrice(s) }}
              <span v-if="s.fixedPrice != null" class="ml-1 text-xs text-orange-400">(fast)</span>
              <span v-else-if="(panelsNeeded(s) ?? 1) > 1"
                class="ml-1 text-xs text-yellow-400"
                :title="`${panelsNeeded(s)} paneler — banner bredere enn materialets maks-bredde`">
                ×{{ panelsNeeded(s) }}
              </span>
            </td>
            <td class="px-4 py-3">
              <span :class="s.isActive ? 'bg-green-900/50 text-green-400' : 'bg-gray-700 text-gray-400'"
                class="text-xs px-2 py-0.5 rounded-full font-medium">
                {{ s.isActive ? 'Aktiv' : 'Inaktiv' }}
              </span>
            </td>
            <td class="px-4 py-3 text-gray-400 text-xs">
              <span v-if="formatDate(s.availableFrom)" class="text-orange-400">{{ formatDate(s.availableFrom) }}</span>
              <span v-else>Nå</span>
            </td>
            <td class="px-4 py-3 text-right space-x-2">
              <button @click="openEdit(s)" class="text-blue-400 hover:underline text-xs font-medium">Rediger</button>
              <button @click="deleteSize(s)" class="text-red-400 hover:underline text-xs font-medium">Slett</button>
            </td>
          </tr>
        </tbody>
      </table>
    </div>

    <!-- Modal -->
    <Teleport to="body">
      <div v-if="showModal" class="fixed inset-0 z-50 flex items-center justify-center bg-black/60 px-4 overflow-y-auto py-8">
        <div class="bg-gray-800 rounded-2xl shadow-xl w-full max-w-md p-6 border border-gray-700">
          <h2 class="text-lg font-semibold text-gray-100 mb-4">
            {{ isEditing ? 'Rediger størrelse' : 'Ny størrelse' }}
          </h2>
          <form @submit.prevent="save" class="space-y-3">
            <div>
              <label class="block text-sm font-medium text-gray-300 mb-1">Navn</label>
              <input v-model="form.name" type="text" required class="w-full bg-gray-900 border border-gray-600 text-gray-100 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500" />
            </div>

            <div class="flex items-center gap-2">
              <input v-model="form.isCustomWidth" type="checkbox" id="customWidth" class="rounded" />
              <label for="customWidth" class="text-sm text-gray-300">Valgfri bredde</label>
            </div>

            <div class="grid grid-cols-2 gap-3">
              <div>
                <label class="block text-sm font-medium text-gray-300 mb-1">Bredde (cm)</label>
                <input v-model.number="form.widthCm" type="number" min="1" :disabled="form.isCustomWidth"
                  :class="form.isCustomWidth ? 'opacity-40' : ''"
                  class="w-full bg-gray-900 border border-gray-600 text-gray-100 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500" />
              </div>
              <div>
                <label class="block text-sm font-medium text-gray-300 mb-1">Høyde (cm)</label>
                <input v-model.number="form.heightCm" type="number" min="1" required class="w-full bg-gray-900 border border-gray-600 text-gray-100 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500" />
              </div>
            </div>

            <div>
              <label class="block text-sm font-medium text-gray-300 mb-1">Materiale</label>
              <select v-model.number="form.materialId" class="w-full bg-gray-900 border border-gray-600 text-gray-100 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500">
                <option v-for="m in materials" :key="m.id" :value="m.id">{{ m.name }}</option>
              </select>
            </div>

            <div>
              <label class="block text-sm font-medium text-gray-300 mb-1">Sorteringsrekkefølge</label>
              <input v-model.number="form.sortOrder" type="number" class="w-full bg-gray-900 border border-gray-600 text-gray-100 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500" />
            </div>

            <div class="flex items-center gap-2">
              <input v-model="hasFixedPrice" type="checkbox" id="fixedPriceToggle" class="rounded" />
              <label for="fixedPriceToggle" class="text-sm text-gray-300">Fast pris (overstyrer beregning)</label>
            </div>
            <div v-if="hasFixedPrice">
              <label class="block text-sm font-medium text-gray-300 mb-1">Fast pris (NOK)</label>
              <input v-model.number="form.fixedPrice" type="number" min="0" step="0.01" class="w-full bg-gray-900 border border-gray-600 text-gray-100 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500" />
            </div>

            <div class="flex items-center gap-2">
              <input v-model="form.isActive" type="checkbox" id="isActive" class="rounded" />
              <label for="isActive" class="text-sm text-gray-300">Aktiv (synlig for kunder)</label>
            </div>

            <p v-if="modalError" class="text-red-400 text-sm bg-red-900/30 border border-red-700 rounded-lg px-3 py-2">
              {{ modalError }}
            </p>

            <div class="flex justify-end gap-3 pt-2">
              <button type="button" @click="showModal = false" class="px-4 py-2 text-sm text-gray-400 hover:text-gray-100">Avbryt</button>
              <button type="submit" :disabled="saving" class="bg-blue-700 text-white px-5 py-2 rounded-lg text-sm font-medium hover:bg-blue-600 disabled:opacity-60">
                {{ saving ? 'Lagrer…' : 'Lagre' }}
              </button>
            </div>
          </form>
        </div>
      </div>
    </Teleport>
  </div>
</template>

<script setup lang="ts">
import { ref, reactive, onMounted } from 'vue'
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
  name: '',
  isActive: true,
  materialId: 1,
  sortOrder: 0,
  minWidthCm: 1,
  maxWidthCm: 500,
  minHeightCm: 1,
  maxHeightCm: 154,
  pricingHeightCm: 154,
  pricingMultiplier: 1,
  fixedPrice: null as number | null,
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
    id: 0,
    name: '',
    isActive: true,
    materialId: materials.value[0]?.id ?? 1,
    sortOrder: (sizes.value.length + 1) * 10,
    minWidthCm: 1,
    maxWidthCm: 500,
    minHeightCm: 1,
    maxHeightCm: 154,
    pricingHeightCm: 154,
    pricingMultiplier: 1,
    fixedPrice: null,
  })
  modalError.value = ''
  showModal.value = true
}

function openEdit(s: BannerSize) {
  isEditing.value = true
  hasFixedPrice.value = s.fixedPrice != null
  Object.assign(form, {
    id: s.id,
    name: s.name,
    isActive: s.isActive,
    materialId: s.materialId,
    sortOrder: s.sortOrder,
    minWidthCm: s.minWidthCm,
    maxWidthCm: s.maxWidthCm,
    minHeightCm: s.minHeightCm,
    maxHeightCm: s.maxHeightCm,
    pricingHeightCm: s.pricingHeightCm,
    pricingMultiplier: s.pricingMultiplier,
    fixedPrice: s.fixedPrice,
  })
  modalError.value = ''
  showModal.value = true
}

async function save() {
  modalError.value = ''
  saving.value = true
  const payload = {
    name: form.name,
    isActive: form.isActive,
    materialId: form.materialId,
    sortOrder: form.sortOrder,
    minWidthCm: form.minWidthCm,
    maxWidthCm: form.maxWidthCm,
    minHeightCm: form.minHeightCm,
    maxHeightCm: form.maxHeightCm,
    pricingHeightCm: form.pricingHeightCm,
    pricingMultiplier: form.pricingMultiplier,
    fixedPrice: hasFixedPrice.value ? form.fixedPrice : null,
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
  if (s.fixedPrice != null) return `${s.fixedPrice.toFixed(0)} NOK (fast)`
  if (s.calculatedPrice != null) return `${s.calculatedPrice.toFixed(0)} NOK`
  return '—'
}

function formatRange(min: number, max: number, unit = 'cm') {
  return min === max ? `${min} ${unit}` : `${min}–${max} ${unit}`
}

function formatDate(d: string | null | undefined) {
  if (!d) return ''
  const date = new Date(d)
  return date > new Date() ? date.toLocaleDateString('no-NO') : ''
}

onMounted(load)
</script>

<template>
  <div class="max-w-7xl mx-auto px-4 py-10">
    <div class="flex items-center justify-between mb-6">
      <div>
        <h1 class="text-2xl font-bold text-white">Bannerstørrelser (prisregler)</h1>
        <p class="text-sm text-gray-400 mt-1">
          Definer prisregler basert på størrelsesområder og prismultiplikator —
          systemet velger automatisk billigste regel for kunden.
        </p>
      </div>
      <button @click="openCreate" class="bg-blue-700 text-white px-4 py-2 rounded-lg text-sm font-medium hover:bg-blue-600">
        + Ny regel
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
            <th class="text-left px-4 py-3 font-medium text-gray-400">Bredde-område</th>
            <th class="text-left px-4 py-3 font-medium text-gray-400">Høyde-område</th>
            <th class="text-left px-4 py-3 font-medium text-gray-400">Prisformel</th>
            <th class="text-left px-4 py-3 font-medium text-gray-400">Pris (eks.)</th>
            <th class="text-left px-4 py-3 font-medium text-gray-400">Status</th>
            <th class="text-left px-4 py-3 font-medium text-gray-400">Tilg. fra</th>
            <th class="px-4 py-3"></th>
          </tr>
        </thead>
        <tbody class="divide-y divide-gray-700">
          <tr v-for="s in sizes" :key="s.id" class="hover:bg-gray-700">
            <td class="px-4 py-3 font-medium text-gray-200">{{ s.name }}</td>
            <td class="px-4 py-3 text-gray-400 text-xs">{{ s.material?.name }}</td>
            <td class="px-4 py-3 text-gray-300 text-xs whitespace-nowrap">{{ formatRange(s.minWidthCm, s.maxWidthCm) }}</td>
            <td class="px-4 py-3 text-gray-300 text-xs whitespace-nowrap">{{ formatRange(s.minHeightCm, s.maxHeightCm) }}</td>
            <td class="px-4 py-3 text-gray-300 text-xs whitespace-nowrap">
              <template v-if="s.fixedPrice != null">
                <span class="text-orange-400">Fast pris</span>
              </template>
              <template v-else>
                pricingH={{ s.pricingHeightCm }} × {{ s.pricingMultiplier }}
              </template>
            </td>
            <td class="px-4 py-3 text-gray-300">{{ formatPrice(s) }}</td>
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
        <div class="bg-gray-800 rounded-2xl shadow-xl w-full max-w-lg p-6 border border-gray-700">
          <h2 class="text-lg font-semibold text-gray-100 mb-4">
            {{ isEditing ? 'Rediger prisregel' : 'Ny prisregel' }}
          </h2>
          <form @submit.prevent="save" class="space-y-3">
            <div>
              <label class="block text-sm font-medium text-gray-300 mb-1">Navn</label>
              <input v-model="form.name" type="text" required class="w-full bg-gray-900 border border-gray-600 text-gray-100 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500" />
            </div>

            <div>
              <label class="block text-sm font-medium text-gray-300 mb-1">Materiale</label>
              <select v-model.number="form.materialId" class="w-full bg-gray-900 border border-gray-600 text-gray-100 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500">
                <option v-for="m in materials" :key="m.id" :value="m.id">{{ m.name }}</option>
              </select>
            </div>

            <fieldset class="border border-gray-700 rounded-lg p-3">
              <legend class="text-xs text-gray-400 px-1">Størrelses-område (regelen gjelder banner i dette området)</legend>
              <div class="grid grid-cols-2 gap-3">
                <div>
                  <label class="block text-xs font-medium text-gray-400 mb-1">Min bredde (cm)</label>
                  <input v-model.number="form.minWidthCm" type="number" min="1" required class="w-full bg-gray-900 border border-gray-600 text-gray-100 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500" />
                </div>
                <div>
                  <label class="block text-xs font-medium text-gray-400 mb-1">Maks bredde (cm)</label>
                  <input v-model.number="form.maxWidthCm" type="number" min="1" required class="w-full bg-gray-900 border border-gray-600 text-gray-100 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500" />
                </div>
                <div>
                  <label class="block text-xs font-medium text-gray-400 mb-1">Min høyde (cm)</label>
                  <input v-model.number="form.minHeightCm" type="number" min="1" required class="w-full bg-gray-900 border border-gray-600 text-gray-100 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500" />
                </div>
                <div>
                  <label class="block text-xs font-medium text-gray-400 mb-1">Maks høyde (cm)</label>
                  <input v-model.number="form.maxHeightCm" type="number" min="1" required class="w-full bg-gray-900 border border-gray-600 text-gray-100 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500" />
                </div>
              </div>
            </fieldset>

            <fieldset class="border border-gray-700 rounded-lg p-3">
              <legend class="text-xs text-gray-400 px-1">Prisformel (ignoreres når fast pris er på)</legend>
              <div class="grid grid-cols-2 gap-3">
                <div>
                  <label class="block text-xs font-medium text-gray-400 mb-1">
                    Pris-høyde (cm)
                    <span class="block text-[10px] text-gray-500 mt-0.5">Faktureres alltid for denne høyden</span>
                  </label>
                  <input v-model.number="form.pricingHeightCm" type="number" min="1" required class="w-full bg-gray-900 border border-gray-600 text-gray-100 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500" />
                </div>
                <div>
                  <label class="block text-xs font-medium text-gray-400 mb-1">
                    Multiplikator
                    <span class="block text-[10px] text-gray-500 mt-0.5">Antall panel som limes (1, 2, 3…)</span>
                  </label>
                  <input v-model.number="form.pricingMultiplier" type="number" min="1" max="20" required class="w-full bg-gray-900 border border-gray-600 text-gray-100 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500" />
                </div>
              </div>
              <p class="text-[11px] text-gray-500 mt-2 leading-snug">
                Pris = max(min-pris, faktisk bredde × pris-høyde × pris-per-m²) × multiplikator
              </p>
            </fieldset>

            <div class="flex items-center gap-2">
              <input v-model="hasFixedPrice" type="checkbox" id="fixedPriceToggle" class="rounded" />
              <label for="fixedPriceToggle" class="text-sm text-gray-300">Fast pris (overstyrer beregning)</label>
            </div>
            <div v-if="hasFixedPrice">
              <label class="block text-sm font-medium text-gray-300 mb-1">Fast pris (NOK)</label>
              <input v-model.number="form.fixedPrice" type="number" min="0" step="0.01" class="w-full bg-gray-900 border border-gray-600 text-gray-100 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500" />
            </div>

            <div>
              <label class="block text-sm font-medium text-gray-300 mb-1">Sorteringsrekkefølge</label>
              <input v-model.number="form.sortOrder" type="number" class="w-full bg-gray-900 border border-gray-600 text-gray-100 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500" />
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

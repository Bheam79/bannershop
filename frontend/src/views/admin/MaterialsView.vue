<script setup lang="ts">
import { ref, reactive, onMounted } from 'vue'
import apiClient from '@/api/client'
import type { Material } from '@/types'

const materials = ref<Material[]>([])
const loading = ref(false)
const error = ref('')

// Modal state
const showModal = ref(false)
const isEditing = ref(false)
const saving = ref(false)
const modalError = ref('')
const form = reactive({
  id: 0,
  name: '',
  widthCm: 160,
  weightGsm: 400,
  pricePerSqm: 180,
  availableFrom: '' as string,
})

async function load() {
  loading.value = true
  error.value = ''
  try {
    const { data } = await apiClient.get<Material[]>('/admin/materials')
    materials.value = data
  } catch {
    error.value = 'Kunne ikke laste materialer.'
  } finally {
    loading.value = false
  }
}

function openCreate() {
  isEditing.value = false
  Object.assign(form, { id: 0, name: '', widthCm: 160, weightGsm: 400, pricePerSqm: 180, availableFrom: '' })
  modalError.value = ''
  showModal.value = true
}

function openEdit(m: Material) {
  isEditing.value = true
  Object.assign(form, {
    id: m.id,
    name: m.name,
    widthCm: m.widthCm,
    weightGsm: m.weightGsm,
    pricePerSqm: m.pricePerSqm,
    availableFrom: m.availableFrom ? m.availableFrom.slice(0, 10) : '',
  })
  modalError.value = ''
  showModal.value = true
}

async function save() {
  modalError.value = ''
  saving.value = true
  const payload = {
    name: form.name,
    widthCm: form.widthCm,
    weightGsm: form.weightGsm,
    pricePerSqm: form.pricePerSqm,
    availableFrom: form.availableFrom ? new Date(form.availableFrom).toISOString() : null,
  }
  try {
    if (isEditing.value) {
      await apiClient.put(`/admin/materials/${form.id}`, payload)
    } else {
      await apiClient.post('/admin/materials', payload)
    }
    showModal.value = false
    await load()
  } catch (err: any) {
    modalError.value = err.response?.data?.error ?? 'Lagring feilet.'
  } finally {
    saving.value = false
  }
}

async function deleteMaterial(m: Material) {
  if (!confirm(`Slett materialet «${m.name}»?`)) return
  try {
    await apiClient.delete(`/admin/materials/${m.id}`)
    await load()
  } catch (err: any) {
    alert(err.response?.data?.error ?? 'Sletting feilet.')
  }
}

function formatDate(d: string | null) {
  if (!d) return '—'
  return new Date(d).toLocaleDateString('no-NO')
}

onMounted(load)
</script>

<template>
  <div class="max-w-6xl mx-auto px-4 py-10">
    <div class="flex items-center justify-between mb-6">
      <h1 class="text-2xl font-bold text-white">Materialer</h1>
      <button @click="openCreate" class="bg-blue-700 text-white px-4 py-2 rounded-lg text-sm font-medium hover:bg-blue-600">
        + Nytt materiale
      </button>
    </div>

    <p v-if="loading" class="text-gray-400">Laster…</p>
    <p v-else-if="error" class="text-red-400">{{ error }}</p>

    <div v-else class="bg-gray-800 rounded-xl border border-gray-700 overflow-hidden">
      <table class="w-full text-sm">
        <thead class="bg-gray-900 border-b border-gray-700">
          <tr>
            <th class="text-left px-4 py-3 font-medium text-gray-400">Navn</th>
            <th class="text-left px-4 py-3 font-medium text-gray-400">Bredde</th>
            <th class="text-left px-4 py-3 font-medium text-gray-400">Vekt</th>
            <th class="text-left px-4 py-3 font-medium text-gray-400">Pris/m²</th>
            <th class="text-left px-4 py-3 font-medium text-gray-400">Tilgjengelig fra</th>
            <th class="px-4 py-3"></th>
          </tr>
        </thead>
        <tbody class="divide-y divide-gray-700">
          <tr v-for="m in materials" :key="m.id" class="hover:bg-gray-700">
            <td class="px-4 py-3 font-medium text-gray-200">{{ m.name }}</td>
            <td class="px-4 py-3 text-gray-400">{{ m.widthCm }} cm</td>
            <td class="px-4 py-3 text-gray-400">{{ m.weightGsm }} g/m²</td>
            <td class="px-4 py-3 text-gray-400">{{ m.pricePerSqm.toFixed(2) }} NOK</td>
            <td class="px-4 py-3 text-gray-400">{{ formatDate(m.availableFrom) }}</td>
            <td class="px-4 py-3 text-right space-x-2">
              <button @click="openEdit(m)" class="text-blue-400 hover:underline text-xs font-medium">Rediger</button>
              <button @click="deleteMaterial(m)" class="text-red-400 hover:underline text-xs font-medium">Slett</button>
            </td>
          </tr>
        </tbody>
      </table>
    </div>

    <!-- Modal -->
    <Teleport to="body">
      <div v-if="showModal" class="fixed inset-0 z-50 flex items-center justify-center bg-black/60 px-4">
        <div class="bg-gray-800 rounded-2xl shadow-xl w-full max-w-md p-6 border border-gray-700">
          <h2 class="text-lg font-semibold text-gray-100 mb-4">
            {{ isEditing ? 'Rediger materiale' : 'Nytt materiale' }}
          </h2>
          <form @submit.prevent="save" class="space-y-3">
            <div>
              <label class="block text-sm font-medium text-gray-300 mb-1">Navn</label>
              <input v-model="form.name" type="text" required class="w-full bg-gray-900 border border-gray-600 text-gray-100 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500" />
            </div>
            <div class="grid grid-cols-2 gap-3">
              <div>
                <label class="block text-sm font-medium text-gray-300 mb-1">Bredde (cm)</label>
                <input v-model.number="form.widthCm" type="number" min="1" required class="w-full bg-gray-900 border border-gray-600 text-gray-100 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500" />
              </div>
              <div>
                <label class="block text-sm font-medium text-gray-300 mb-1">Vekt (g/m²)</label>
                <input v-model.number="form.weightGsm" type="number" min="1" required class="w-full bg-gray-900 border border-gray-600 text-gray-100 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500" />
              </div>
            </div>
            <div>
              <label class="block text-sm font-medium text-gray-300 mb-1">Pris per m² (NOK)</label>
              <input v-model.number="form.pricePerSqm" type="number" min="0" step="0.01" required class="w-full bg-gray-900 border border-gray-600 text-gray-100 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500" />
            </div>
            <div>
              <label class="block text-sm font-medium text-gray-300 mb-1">Tilgjengelig fra (blank = nå)</label>
              <input v-model="form.availableFrom" type="date" class="w-full bg-gray-900 border border-gray-600 text-gray-100 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500" />
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

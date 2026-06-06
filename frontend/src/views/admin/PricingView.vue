<script setup lang="ts">
import { ref, onMounted } from 'vue'
import apiClient from '@/api/client'
import type { PricingParameter } from '@/types'

const params = ref<PricingParameter[]>([])
const loading = ref(false)
const error = ref('')

// Track per-row edit state
const editing = ref<Record<number, boolean>>({})
const editValues = ref<Record<number, number>>({})
const saving = ref<Record<number, boolean>>({})
const saveError = ref<Record<number, string>>({})

async function load() {
  loading.value = true
  error.value = ''
  try {
    const { data } = await apiClient.get<PricingParameter[]>('/admin/pricing-parameters')
    params.value = data
    data.forEach((p) => {
      editValues.value[p.id] = p.value
    })
  } catch {
    error.value = 'Kunne ikke laste prisparametere.'
  } finally {
    loading.value = false
  }
}

function startEdit(p: PricingParameter) {
  editValues.value[p.id] = p.value
  editing.value[p.id] = true
  saveError.value[p.id] = ''
}

function cancelEdit(p: PricingParameter) {
  editing.value[p.id] = false
  editValues.value[p.id] = p.value
}

async function saveParam(p: PricingParameter) {
  saving.value[p.id] = true
  saveError.value[p.id] = ''
  try {
    const { data } = await apiClient.put<PricingParameter>(`/admin/pricing-parameters/${p.id}`, {
      value: editValues.value[p.id],
    })
    // Update local
    const idx = params.value.findIndex((x) => x.id === p.id)
    if (idx !== -1) params.value[idx] = data
    editing.value[p.id] = false
  } catch (err: any) {
    saveError.value[p.id] = err.response?.data?.error ?? 'Lagring feilet.'
  } finally {
    saving.value[p.id] = false
  }
}

onMounted(load)
</script>

<template>
  <div class="max-w-4xl mx-auto px-4 py-10">
    <div class="mb-6">
      <h1 class="text-2xl font-bold text-gray-900">Prissetting</h1>
      <p class="text-gray-500 text-sm mt-1">Juster prisparametere som brukes til å beregne bannerprisene.</p>
    </div>

    <p v-if="loading" class="text-gray-400">Laster…</p>
    <p v-else-if="error" class="text-red-600">{{ error }}</p>

    <div v-else class="bg-white rounded-xl border border-gray-200 overflow-hidden shadow-sm">
      <table class="w-full text-sm">
        <thead class="bg-gray-50 border-b border-gray-200">
          <tr>
            <th class="text-left px-4 py-3 font-medium text-gray-600">Parameter</th>
            <th class="text-left px-4 py-3 font-medium text-gray-600 w-48">Verdi (NOK)</th>
            <th class="text-left px-4 py-3 font-medium text-gray-600 hidden sm:table-cell">Beskrivelse</th>
            <th class="px-4 py-3 w-32"></th>
          </tr>
        </thead>
        <tbody class="divide-y divide-gray-100">
          <tr v-for="p in params" :key="p.id" class="hover:bg-gray-50">
            <td class="px-4 py-3">
              <div class="font-medium text-gray-800">{{ p.name }}</div>
              <div class="text-xs text-gray-400 font-mono">{{ p.key }}</div>
            </td>
            <td class="px-4 py-3">
              <div v-if="editing[p.id]">
                <input
                  v-model.number="editValues[p.id]"
                  type="number"
                  step="0.01"
                  min="0"
                  class="w-full border border-blue-400 rounded-lg px-2 py-1 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
                  @keyup.enter="saveParam(p)"
                  @keyup.escape="cancelEdit(p)"
                />
                <p v-if="saveError[p.id]" class="text-red-500 text-xs mt-1">{{ saveError[p.id] }}</p>
              </div>
              <span v-else class="font-mono text-gray-700">{{ p.value.toFixed(2) }}</span>
            </td>
            <td class="px-4 py-3 text-gray-500 text-xs hidden sm:table-cell">{{ p.description }}</td>
            <td class="px-4 py-3 text-right">
              <template v-if="editing[p.id]">
                <button
                  @click="saveParam(p)"
                  :disabled="saving[p.id]"
                  class="text-green-600 hover:underline text-xs font-medium mr-2 disabled:opacity-60"
                >{{ saving[p.id] ? '…' : 'Lagre' }}</button>
                <button @click="cancelEdit(p)" class="text-gray-500 hover:underline text-xs">Avbryt</button>
              </template>
              <button v-else @click="startEdit(p)" class="text-blue-600 hover:underline text-xs font-medium">
                Rediger
              </button>
            </td>
          </tr>
        </tbody>
      </table>
    </div>

    <p class="text-xs text-gray-400 mt-4">
      Prisformel: max(minimumspris, areal × basispris/m²) + hem-avgift + (valgfri bredde ? tillegg : 0)
    </p>
  </div>
</template>

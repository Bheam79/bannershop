<script setup lang="ts">
import { ref, watch } from 'vue'
import type { ShippingEstimate } from '@/types'
import { calculateShipping } from '@/api/shop'

const props = defineProps<{
  bannerSizeId: number | null
  customWidthCm: number | null
  qty: number
}>()

const emit = defineEmits<{
  (e: 'estimate', value: ShippingEstimate | null): void
  (e: 'postal-code', value: string): void
}>()

const postalCode = ref('')
const city = ref('')
const estimate = ref<ShippingEstimate | null>(null)
const loading = ref(false)
const error = ref<string | null>(null)

function formatDateOffset(daysFromNow: number): string {
  const d = new Date()
  d.setDate(d.getDate() + daysFromNow)
  return d.toLocaleDateString('nb-NO', { day: '2-digit', month: 'short' })
}

async function compute() {
  error.value = null
  if (!/^\d{4}$/.test(postalCode.value.trim())) {
    estimate.value = null
    emit('estimate', null)
    return
  }
  if (!props.bannerSizeId) {
    error.value = 'Velg en bannerstørrelse først.'
    return
  }
  loading.value = true
  try {
    const result = await calculateShipping({
      postalCode: postalCode.value.trim(),
      city: city.value.trim() || undefined,
      bannerSizeId: props.bannerSizeId,
      customWidthCm: props.customWidthCm ?? undefined,
      qty: props.qty,
    })
    estimate.value = result
    emit('estimate', result)
    emit('postal-code', postalCode.value.trim())
  } catch (e: unknown) {
    const ex = e as { response?: { data?: { error?: string } } }
    error.value = ex.response?.data?.error || 'Kunne ikke beregne frakt.'
    estimate.value = null
    emit('estimate', null)
  } finally {
    loading.value = false
  }
}

// Recompute when banner selection / qty changes (only if a valid postcode is set)
watch(
  () => [props.bannerSizeId, props.customWidthCm, props.qty],
  () => {
    if (/^\d{4}$/.test(postalCode.value.trim())) compute()
  },
)
</script>

<template>
  <div class="bg-white border border-gray-200 rounded-xl p-5">
    <h3 class="text-lg font-semibold text-gray-900 mb-3">Beregn fraktkostnad</h3>
    <p class="text-sm text-gray-600 mb-4">
      Skriv inn postnummer for å se fraktpris og estimert leveringsdato.
    </p>
    <div class="flex flex-col sm:flex-row gap-3">
      <input
        v-model="postalCode"
        type="text"
        inputmode="numeric"
        maxlength="4"
        placeholder="Postnr (4 siffer)"
        class="flex-1 border border-gray-300 rounded-lg px-3 py-2 focus:outline-none focus:ring-2 focus:ring-blue-500"
        @keyup.enter="compute"
      />
      <input
        v-model="city"
        type="text"
        placeholder="Poststed (valgfritt)"
        class="flex-1 border border-gray-300 rounded-lg px-3 py-2 focus:outline-none focus:ring-2 focus:ring-blue-500"
        @keyup.enter="compute"
      />
      <button
        type="button"
        :disabled="loading || !postalCode"
        class="bg-blue-700 hover:bg-blue-800 disabled:bg-gray-300 text-white font-medium px-5 py-2 rounded-lg"
        @click="compute"
      >
        {{ loading ? 'Beregner…' : 'Beregn' }}
      </button>
    </div>

    <p v-if="error" class="mt-3 text-sm text-red-600">{{ error }}</p>

    <div v-if="estimate" class="mt-5 grid sm:grid-cols-2 gap-3">
      <div class="border border-gray-200 rounded-lg p-4">
        <div class="text-sm text-gray-500">Standard levering</div>
        <div class="text-2xl font-bold text-gray-900 mt-1">
          {{ estimate.standard.costNok.toFixed(0) }} kr
        </div>
        <div class="text-xs text-gray-500 mt-1">
          Levering ca. {{ formatDateOffset(estimate.standard.estimatedDays) }}
          ({{ estimate.standard.estimatedDays }} virkedager)
        </div>
      </div>
      <div class="border border-yellow-300 bg-yellow-50 rounded-lg p-4">
        <div class="text-sm text-yellow-800">Ekspresslevering</div>
        <div class="text-2xl font-bold text-gray-900 mt-1">
          {{ estimate.express.costNok.toFixed(0) }} kr
          <span class="text-sm font-normal text-gray-600">+ 500 kr produksjonsgebyr</span>
        </div>
        <div class="text-xs text-gray-700 mt-1">
          Levering ca. {{ formatDateOffset(estimate.express.estimatedDays) }}
          ({{ estimate.express.estimatedDays }} virkedager)
        </div>
      </div>
    </div>
  </div>
</template>

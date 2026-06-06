<script setup lang="ts">
import { ref, watch, onUnmounted, onMounted } from 'vue'
import {
  rotateBanner,
  setBannerHeight,
  fetchPreviewBlobUrl,
} from '@/api/bannerBuilder'

const props = defineProps<{
  designId: number
  initialHeightCm: number
  initialComputedWidthCm: number
  initialRotationDegrees: number
}>()

const emit = defineEmits<{
  (e: 'change', state: {
    heightCm: number
    computedWidthCm: number
    rotationDegrees: number
  }): void
}>()

const previewSrc = ref<string | null>(null)
const previewBlobUrl = ref<string | null>(null)
const loadingPreview = ref(false)
const previewError = ref<string | null>(null)

const heightCm = ref<number>(props.initialHeightCm)
const computedWidthCm = ref<number>(props.initialComputedWidthCm)
const rotationDegrees = ref<number>(props.initialRotationDegrees)

const rotating = ref(false)
const settingHeight = ref(false)
const error = ref<string | null>(null)

function notify() {
  emit('change', {
    heightCm: heightCm.value,
    computedWidthCm: computedWidthCm.value,
    rotationDegrees: rotationDegrees.value,
  })
}

async function loadPreview() {
  loadingPreview.value = true
  previewError.value = null
  try {
    if (previewBlobUrl.value) {
      URL.revokeObjectURL(previewBlobUrl.value)
      previewBlobUrl.value = null
    }
    const url = await fetchPreviewBlobUrl(props.designId)
    previewBlobUrl.value = url
    previewSrc.value = url
  } catch (e: unknown) {
    const ex = e as { response?: { status?: number }; message?: string }
    previewError.value =
      ex.response?.status === 401
        ? 'Forhåndsvisning krever innlogging.'
        : 'Kunne ikke laste forhåndsvisning.'
  } finally {
    loadingPreview.value = false
  }
}

async function rotate(delta: number) {
  if (rotating.value) return
  rotating.value = true
  error.value = null
  try {
    const resp = await rotateBanner(props.designId, delta)
    rotationDegrees.value = resp.rotationDegrees
    computedWidthCm.value = resp.computedWidthCm
    notify()
    await loadPreview()
  } catch (e: unknown) {
    const ex = e as { response?: { data?: { error?: string } }; message?: string }
    error.value = ex.response?.data?.error || ex.message || 'Rotasjon feilet.'
  } finally {
    rotating.value = false
  }
}

async function selectHeight(h: number) {
  if (settingHeight.value || heightCm.value === h) {
    heightCm.value = h
    return
  }
  settingHeight.value = true
  error.value = null
  try {
    const resp = await setBannerHeight(props.designId, h)
    heightCm.value = resp.selectedHeightCm
    computedWidthCm.value = resp.computedWidthCm
    notify()
  } catch (e: unknown) {
    const ex = e as { response?: { data?: { error?: string } }; message?: string }
    error.value = ex.response?.data?.error || ex.message || 'Kunne ikke endre høyde.'
  } finally {
    settingHeight.value = false
  }
}

onMounted(() => {
  void loadPreview()
  notify()
})

watch(() => props.designId, () => {
  heightCm.value = props.initialHeightCm
  computedWidthCm.value = props.initialComputedWidthCm
  rotationDegrees.value = props.initialRotationDegrees
  void loadPreview()
  notify()
})

onUnmounted(() => {
  if (previewBlobUrl.value) URL.revokeObjectURL(previewBlobUrl.value)
})
</script>

<template>
  <div class="space-y-5">
    <!-- Preview image -->
    <div class="relative w-full bg-gray-100 rounded-lg overflow-hidden border border-gray-200 flex items-center justify-center" style="min-height: 280px;">
      <div v-if="loadingPreview" class="text-sm text-gray-500 py-8">
        Laster forhåndsvisning…
      </div>
      <div v-else-if="previewError" class="text-sm text-red-600 py-8 px-4 text-center">
        {{ previewError }}
      </div>
      <img
        v-else-if="previewSrc"
        :src="previewSrc"
        alt="Forhåndsvisning av banner"
        class="max-w-full max-h-[60vh] object-contain"
      />
    </div>

    <!-- Rotation buttons (large tap targets) -->
    <div>
      <div class="text-sm font-medium text-gray-700 mb-2">Rotering</div>
      <div class="grid grid-cols-2 gap-3">
        <button
          type="button"
          class="bg-white border-2 border-gray-200 hover:border-blue-700 rounded-lg py-4 px-4 text-base font-medium text-gray-800 transition flex items-center justify-center gap-2 disabled:opacity-50"
          :disabled="rotating"
          aria-label="Roter mot klokken"
          @click="rotate(-90)"
        >
          <span class="text-2xl" aria-hidden="true">↺</span>
          <span>Roter venstre</span>
        </button>
        <button
          type="button"
          class="bg-white border-2 border-gray-200 hover:border-blue-700 rounded-lg py-4 px-4 text-base font-medium text-gray-800 transition flex items-center justify-center gap-2 disabled:opacity-50"
          :disabled="rotating"
          aria-label="Roter med klokken"
          @click="rotate(90)"
        >
          <span class="text-2xl" aria-hidden="true">↻</span>
          <span>Roter høyre</span>
        </button>
      </div>
    </div>

    <!-- Height selector -->
    <div>
      <div class="text-sm font-medium text-gray-700 mb-2">Høyde</div>
      <div class="grid grid-cols-2 gap-3">
        <button
          type="button"
          class="rounded-lg py-4 px-4 text-base font-semibold border-2 transition disabled:opacity-50"
          :class="heightCm === 150
            ? 'border-blue-700 bg-blue-50 text-blue-800'
            : 'border-gray-200 bg-white text-gray-800 hover:border-gray-400'"
          :disabled="settingHeight"
          @click="selectHeight(150)"
        >
          150 cm
          <div class="text-xs font-normal text-gray-500 mt-0.5">400g Frontlit</div>
        </button>
        <button
          type="button"
          class="rounded-lg py-4 px-4 text-base font-semibold border-2 transition disabled:opacity-50"
          :class="heightCm === 180
            ? 'border-blue-700 bg-blue-50 text-blue-800'
            : 'border-gray-200 bg-white text-gray-800 hover:border-gray-400'"
          :disabled="settingHeight"
          @click="selectHeight(180)"
        >
          180 cm
          <div class="text-xs font-normal text-gray-500 mt-0.5">680g Heavy Duty</div>
        </button>
      </div>
    </div>

    <!-- Derived size display -->
    <div class="bg-blue-50 border border-blue-200 rounded-lg px-4 py-3">
      <div class="text-xs uppercase tracking-wider text-blue-700 font-semibold">
        Beregnet størrelse
      </div>
      <div class="text-xl font-bold text-blue-900 mt-1">
        {{ computedWidthCm }} × {{ heightCm }} cm
      </div>
      <div class="text-xs text-blue-800 mt-1">
        Bredden beregnes automatisk fra bildets størrelsesforhold.
      </div>
    </div>

    <p v-if="error" class="text-sm text-red-700 bg-red-50 border border-red-200 rounded-lg px-3 py-2">
      {{ error }}
    </p>
  </div>
</template>

<script setup lang="ts">
import { ref, computed } from 'vue'
import { uploadBannerFile, type UploadResponse } from '@/api/bannerBuilder'

const emit = defineEmits<{
  (e: 'uploaded', resp: UploadResponse): void
}>()

const ACCEPTED_MIME = ['image/jpeg', 'image/png', 'image/webp', 'application/pdf']
const ACCEPTED_ACCEPT = ACCEPTED_MIME.join(',')
const MAX_BYTES = 50 * 1024 * 1024 // 50 MB

const fileInput = ref<HTMLInputElement | null>(null)
const dragging = ref(false)
const uploading = ref(false)
const progress = ref(0)
const error = ref<string | null>(null)

const progressText = computed(() => `${progress.value}%`)

function openPicker() {
  if (uploading.value) return
  fileInput.value?.click()
}

function onFileChange(e: Event) {
  const input = e.target as HTMLInputElement
  const file = input.files?.[0]
  if (file) void handleFile(file)
  // Reset so picking the same file twice re-fires the event
  if (input) input.value = ''
}

function onDragOver(e: DragEvent) {
  e.preventDefault()
  dragging.value = true
}
function onDragLeave() {
  dragging.value = false
}
function onDrop(e: DragEvent) {
  e.preventDefault()
  dragging.value = false
  const file = e.dataTransfer?.files?.[0]
  if (file) void handleFile(file)
}

function formatBytes(n: number): string {
  if (n < 1024) return `${n} B`
  if (n < 1024 * 1024) return `${(n / 1024).toFixed(1)} kB`
  return `${(n / 1024 / 1024).toFixed(1)} MB`
}

async function handleFile(file: File) {
  error.value = null

  if (!ACCEPTED_MIME.includes(file.type)) {
    error.value = `Filtypen ${file.type || 'ukjent'} støttes ikke. Bruk JPEG, PNG, WEBP eller PDF.`
    return
  }
  if (file.size > MAX_BYTES) {
    error.value = `Filen er for stor (${formatBytes(file.size)}). Maks 50 MB.`
    return
  }

  uploading.value = true
  progress.value = 0
  try {
    const resp = await uploadBannerFile(file, (pct) => { progress.value = pct })
    emit('uploaded', resp)
  } catch (e: unknown) {
    const ex = e as { response?: { status?: number; data?: { error?: string } }; message?: string }
    if (ex.response?.status === 401) {
      error.value = 'Du må være innlogget for å laste opp en banner. Logg inn og prøv igjen.'
    } else if (ex.response?.status === 413) {
      error.value = 'Filen er for stor. Maks 50 MB.'
    } else {
      error.value = ex.response?.data?.error || ex.message || 'Opplasting feilet. Prøv igjen.'
    }
  } finally {
    uploading.value = false
  }
}
</script>

<template>
  <div>
    <div
      role="button"
      tabindex="0"
      class="relative w-full rounded-xl border-2 border-dashed transition cursor-pointer select-none flex flex-col items-center justify-center text-center px-6 py-12 sm:py-16"
      :class="[
        dragging
          ? 'border-blue-600 bg-blue-50'
          : 'border-gray-300 bg-gray-50 hover:bg-gray-100 hover:border-gray-400',
        uploading ? 'opacity-60 cursor-progress' : '',
      ]"
      @click="openPicker"
      @keydown.enter.prevent="openPicker"
      @keydown.space.prevent="openPicker"
      @dragover="onDragOver"
      @dragleave="onDragLeave"
      @drop="onDrop"
    >
      <input
        ref="fileInput"
        type="file"
        class="hidden"
        :accept="ACCEPTED_ACCEPT"
        @change="onFileChange"
      />

      <!-- Icon -->
      <svg class="w-14 h-14 text-gray-400 mb-3" fill="none" viewBox="0 0 24 24" stroke="currentColor" aria-hidden="true">
        <path stroke-linecap="round" stroke-linejoin="round" stroke-width="1.5"
              d="M7 16a4 4 0 01-.88-7.9 5 5 0 019.66-1.5A4.5 4.5 0 0117 16M12 12v6m0-6l-3 3m3-3l3 3" />
      </svg>

      <p class="text-lg font-semibold text-gray-900 mb-1">
        Slipp filen her, eller klikk for å velge
      </p>
      <p class="text-sm text-gray-600">
        JPEG, PNG, WEBP eller PDF – maks 50 MB
      </p>

      <!-- Upload progress overlay -->
      <div
        v-if="uploading"
        class="absolute inset-0 flex flex-col items-center justify-center bg-white/80 rounded-xl"
      >
        <div class="w-3/4 max-w-xs">
          <div class="text-sm font-medium text-gray-800 text-center mb-2">
            Laster opp… {{ progressText }}
          </div>
          <div class="w-full h-2.5 bg-gray-200 rounded-full overflow-hidden">
            <div
              class="h-full bg-blue-600 transition-all"
              :style="{ width: progressText }"
            />
          </div>
        </div>
      </div>
    </div>

    <p v-if="error" class="mt-3 text-sm text-red-700 bg-red-50 border border-red-200 rounded-lg px-3 py-2">
      {{ error }}
    </p>
  </div>
</template>

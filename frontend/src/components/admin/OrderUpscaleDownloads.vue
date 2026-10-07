<script setup lang="ts">
import { onBeforeUnmount, ref } from 'vue'
import apiClient from '@/api/client'

const props = defineProps<{ orderId: number; itemId: number }>()
const busy = ref<2 | 4 | null>(null)
const error = ref('')
let disposed = false
const abort = new AbortController()
onBeforeUnmount(() => { disposed = true; abort.abort() })

async function download(scale: 2 | 4) {
  if (busy.value) return
  busy.value = scale
  error.value = ''
  const path = `/admin/orders/${props.orderId}/items/${props.itemId}/upscale`
  try {
    // Short requests avoid reverse-proxy timeouts. Repeated clicks/reloads resume
    // the saved queue handle rather than buying a new generation.
    const deadline = Date.now() + 15 * 60 * 1000
    while (!disposed) {
      const { data } = await apiClient.post<{ ready: boolean }>(path, null, {
        params: { scale }, signal: abort.signal,
      })
      if (data.ready) break
      if (Date.now() >= deadline) {
        error.value = 'Oppskaleringen pågår fortsatt. Klikk igjen senere for å fortsette samme jobb.'
        return
      }
      await new Promise((resolve) => setTimeout(resolve, 3000))
    }
    if (disposed) return
    const { data } = await apiClient.get<Blob>(`${path}/download`, {
      params: { scale }, responseType: 'blob', signal: abort.signal,
    })
    const url = URL.createObjectURL(data)
    const link = document.createElement('a')
    link.href = url
    link.download = `ordre-${props.orderId}-banner-${props.itemId}-${scale}x.png`
    document.body.appendChild(link)
    link.click()
    link.remove()
    setTimeout(() => URL.revokeObjectURL(url), 60_000)
  } catch (err: unknown) {
    if (disposed) return
    const e = err as { response?: { data?: { error?: string } | Blob } }
    const data = e.response?.data
    if (data instanceof Blob) {
      try { error.value = JSON.parse(await data.text()).error } catch { /* use fallback */ }
    } else {
      error.value = data?.error ?? ''
    }
    error.value ||= 'Kunne ikke laste ned oppskalert bilde. Prøv igjen for å fortsette samme jobb.'
  } finally {
    busy.value = null
  }
}
</script>

<template>
  <div class="pt-2 space-y-2">
    <div class="flex flex-wrap gap-2">
      <button
        v-for="scale in ([2, 4] as const)"
        :key="scale"
        type="button"
        :disabled="busy !== null"
        class="inline-flex items-center gap-1.5 bg-indigo-700 text-white px-3 py-1.5 rounded-lg text-xs font-medium hover:bg-indigo-600 disabled:opacity-50 disabled:cursor-wait"
        @click="download(scale)"
      >
        {{ busy === scale ? `Oppskalerer ${scale}x …` : `⬇ Last ned ${scale}x oppskalert` }}
      </button>
    </div>
    <p class="text-xs text-gray-400">fal.ai · Første nedlasting genererer bildet. Resultatet lagres for senere nedlastinger.</p>
    <p v-if="error" role="alert" class="text-xs text-red-400">{{ error }}</p>
    <p v-if="busy" role="status" class="text-xs text-indigo-300">Dette kan ta noen minutter. Originalfilen endres ikke.</p>
  </div>
</template>

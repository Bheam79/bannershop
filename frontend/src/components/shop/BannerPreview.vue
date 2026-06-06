<script setup lang="ts">
import { computed } from 'vue'

const props = withDefaults(
  defineProps<{
    widthCm: number
    heightCm: number
    label?: string
  }>(),
  { label: '' },
)

// Scale the banner so its longest edge fits 100% of the preview area.
const maxEdge = computed(() => Math.max(props.widthCm, props.heightCm))
const widthPct = computed(() => (props.widthCm / maxEdge.value) * 100)
const heightPct = computed(() => (props.heightCm / maxEdge.value) * 100)
</script>

<template>
  <div class="relative w-full bg-gray-100 rounded-lg overflow-hidden border border-gray-200" style="aspect-ratio: 2 / 1;">
    <div class="absolute inset-0 flex items-center justify-center p-4">
      <div
        class="relative bg-gradient-to-br from-blue-600 to-blue-800 rounded-sm shadow-md flex items-center justify-center"
        :style="{ width: widthPct + '%', height: heightPct + '%' }"
      >
        <!-- Eyelet markers in the four corners -->
        <span class="absolute top-1 left-1 w-1.5 h-1.5 rounded-full bg-gray-200 ring-1 ring-gray-400"></span>
        <span class="absolute top-1 right-1 w-1.5 h-1.5 rounded-full bg-gray-200 ring-1 ring-gray-400"></span>
        <span class="absolute bottom-1 left-1 w-1.5 h-1.5 rounded-full bg-gray-200 ring-1 ring-gray-400"></span>
        <span class="absolute bottom-1 right-1 w-1.5 h-1.5 rounded-full bg-gray-200 ring-1 ring-gray-400"></span>
        <span class="text-white text-xs sm:text-sm font-semibold tracking-wide select-none px-2 text-center">
          {{ label || `${widthCm} × ${heightCm} cm` }}
        </span>
      </div>
    </div>
    <!-- Width dimension label -->
    <div class="absolute bottom-1 left-1/2 -translate-x-1/2 text-[10px] text-gray-500 bg-white/70 px-1 rounded">
      {{ widthCm }} cm
    </div>
    <!-- Height dimension label -->
    <div class="absolute top-1/2 right-1 -translate-y-1/2 text-[10px] text-gray-500 bg-white/70 px-1 rounded">
      {{ heightCm }} cm
    </div>
  </div>
</template>

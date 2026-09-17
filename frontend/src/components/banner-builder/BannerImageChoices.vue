<script setup lang="ts">
import type { BannerGenerationHistoryItem } from '@/api/designRequests'
defineProps<{
  generations: BannerGenerationHistoryItem[]
  activatingGenerationId: number | null
  locked?: boolean
}>()
const emit = defineEmits<{ select: [generation: BannerGenerationHistoryItem] }>()
function label(index: number) {
  return `Alternativ ${index + 1}`
}
</script>

<template>
  <section v-if="generations.length > 1" class="image-choices">
    <h3>Velg banneret du liker best</h3>
    <p>Se på alternativene og velg bildet du vil bruke.</p>
    <div class="image-choices__grid">
      <button v-for="(generation, index) in generations" :key="generation.id" type="button"
        :aria-pressed="generation.isActive" :aria-label="`Velg ${label(index)}`"
        :disabled="locked || activatingGenerationId !== null || generation.isActive"
        :class="{ selected: generation.isActive }" @click="emit('select', generation)">
        <img v-if="generation.previewUrl" :src="generation.previewUrl" :alt="label(index)" />
        <span>{{ label(index) }} · {{ activatingGenerationId === generation.id ? 'Velger…' : generation.isActive ? 'Valgt' : 'Velg dette' }}</span>
      </button>
    </div>
  </section>
</template>

<style scoped>
.image-choices { margin: 0 0 16px; }
h3 { font-size: 18px; font-weight: 700; color: var(--text); }
p { font-size: 14px; color: var(--muted); margin: 4px 0 12px; }
.image-choices__grid { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 12px; }
button { overflow: hidden; border: 2px solid var(--border, #ddd); border-radius: 10px; text-align: left; background: var(--surface, #fff); cursor: pointer; }
button.selected { border-color: var(--accent); box-shadow: 0 0 0 1px var(--accent); }
button:focus-visible { outline: 3px solid var(--accent); outline-offset: 3px; }
button:disabled { cursor: default; }
img { width: 100%; aspect-ratio: 16 / 9; object-fit: contain; display: block; }
span { display: block; padding: 10px; font-size: 14px; color: var(--text); }
@media (max-width: 540px) { .image-choices__grid { grid-template-columns: 1fr; } }
</style>

<script setup lang="ts">
import { computed } from 'vue'
import { useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/auth'
import { useAiCreditsStore } from '@/stores/aiCredits'

interface Props {
  /** "light" = bright pill on dark navbar (default). "dark" = subdued pill. */
  variant?: 'light' | 'dark'
  /**
   * Optional override for which route the badge links to.
   * Defaults to `/account/credits` — the dedicated one-pager for buying AI credit
   * packs (BANNERSH-183). Previously pointed at `/account`, which had nothing to
   * do with AI credits.
   */
  to?: string
}

const props = withDefaults(defineProps<Props>(), {
  variant: 'light',
  to: '/account/credits',
})

const router = useRouter()
const auth = useAuthStore()
const credits = useAiCreditsStore()

const visible = computed(() => auth.isLoggedIn && credits.creditsRemaining !== null)

const tooltip = computed(() => {
  const n = credits.creditsRemaining ?? 0
  if (n === 0) return 'Ingen AI-kreditter — klikk for å kjøpe'
  if (n === 1) return '1 AI-kreditt igjen — klikk for å kjøpe flere'
  return `${n} AI-kreditter igjen — klikk for å kjøpe flere`
})

function open() {
  router.push(props.to)
}
</script>

<template>
  <button
    v-if="visible"
    type="button"
    class="ai-credit-badge"
    :class="variant"
    :title="tooltip"
    :aria-label="tooltip"
    @click="open"
  >
    <i class="fa-solid fa-wand-magic-sparkles" aria-hidden="true"></i>
    <span class="num">{{ credits.creditsRemaining }}</span>
    <span class="lbl">AI-kreditt<template v-if="credits.creditsRemaining !== 1">er</template></span>
  </button>
</template>

<style scoped>
.ai-credit-badge {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  height: 34px;
  padding: 0 12px;
  border-radius: 999px;
  background: rgba(255, 106, 61, 0.12);
  border: 1px solid rgba(255, 106, 61, 0.35);
  color: var(--text, #f5f1ea);
  font-family: var(--font-ui, system-ui), sans-serif;
  font-size: 13.5px;
  font-weight: 600;
  cursor: pointer;
  transition:
    background 0.15s ease,
    border-color 0.15s ease,
    transform 0.05s ease;
  white-space: nowrap;
}
.ai-credit-badge:hover {
  background: rgba(255, 106, 61, 0.2);
  border-color: rgba(255, 106, 61, 0.55);
}
.ai-credit-badge:active {
  transform: scale(0.97);
}
.ai-credit-badge .fa-wand-magic-sparkles {
  color: var(--accent, #ff6a3d);
  font-size: 13px;
}
.ai-credit-badge .num {
  font-variant-numeric: tabular-nums;
  font-weight: 700;
}
.ai-credit-badge .lbl {
  color: var(--muted, #c9c0b1);
  font-weight: 500;
}

/* "dark" variant for surfaces that already have a coloured background (e.g. admin) */
.ai-credit-badge.dark {
  background: rgba(255, 255, 255, 0.06);
  border-color: rgba(255, 255, 255, 0.12);
  color: #f5f5f5;
}
.ai-credit-badge.dark:hover {
  background: rgba(255, 255, 255, 0.1);
  border-color: rgba(255, 255, 255, 0.22);
}
.ai-credit-badge.dark .lbl {
  color: #a0a0a0;
}

/* Hide the word label on very narrow viewports, keep the icon + number */
@media (max-width: 640px) {
  .ai-credit-badge .lbl {
    display: none;
  }
}
</style>

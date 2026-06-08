import { defineStore } from 'pinia'
import { ref } from 'vue'
import { getAiCreditsBalance } from '@/api/aiCredits'

/**
 * Globally shared AI-credit balance (BANNERSH-87).
 *
 * Populated lazily — call {@link fetchBalance} after login, navigation, or any
 * operation that grants/consumes credits. The header reads the reactive
 * {@link creditsRemaining} to show the running total without each view
 * having to track it locally.
 *
 * Views that already know the new balance (e.g. after a successful
 * /design-requests/ai POST) can call {@link setBalance} to update the badge
 * without an extra round-trip.
 */
export const useAiCreditsStore = defineStore('aiCredits', () => {
  const creditsRemaining = ref<number | null>(null)
  const hasUsedFreeGeneration = ref<boolean>(false)
  const loading = ref<boolean>(false)
  const loaded = ref<boolean>(false)

  async function fetchBalance(): Promise<void> {
    loading.value = true
    try {
      const b = await getAiCreditsBalance()
      creditsRemaining.value = b.creditsRemaining
      hasUsedFreeGeneration.value = b.hasUsedFreeGeneration
      loaded.value = true
    } catch {
      // Likely 401 (not logged in) — silently reset and let consumers retry later.
      creditsRemaining.value = null
      loaded.value = false
    } finally {
      loading.value = false
    }
  }

  function setBalance(credits: number, usedFree?: boolean): void {
    creditsRemaining.value = credits
    if (usedFree !== undefined) hasUsedFreeGeneration.value = usedFree
    loaded.value = true
  }

  function reset(): void {
    creditsRemaining.value = null
    hasUsedFreeGeneration.value = false
    loaded.value = false
    loading.value = false
  }

  return {
    creditsRemaining,
    hasUsedFreeGeneration,
    loading,
    loaded,
    fetchBalance,
    setBalance,
    reset,
  }
})

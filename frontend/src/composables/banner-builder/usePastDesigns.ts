/**
 * usePastDesigns — loads + filters the current user's past design requests
 * for display in the banner-builder sidebar / paywall gallery.
 *
 * Used by AiBannerBuilderView (both AI and Manual modes).
 */
import { ref } from 'vue'
import { listDesignRequests, type DesignRequestListItem } from '@/api/designRequests'
import { useAuthStore } from '@/stores/auth'

export function usePastDesigns(getMode: () => 'ai' | 'manual') {
  const auth = useAuthStore()
  const pastDesigns = ref<DesignRequestListItem[]>([])
  const pastDesignsLoading = ref(false)

  /**
   * Fetch and filter the signed-in user's design requests.
   * AI mode: only `mode === 'Ai'` entries with a preview URL.
   * Manual mode: only `mode === 'Manual'` entries (preview may be absent).
   */
  async function loadPastDesigns() {
    if (!auth.isLoggedIn) return
    pastDesignsLoading.value = true
    try {
      const all = await listDesignRequests()
      const m = getMode()
      if (m === 'manual') {
        pastDesigns.value = all.filter((d) => d.mode === 'Manual')
      } else {
        pastDesigns.value = all.filter((d) => d.mode === 'Ai' && d.previewUrl !== null)
      }
    } catch {
      // Non-critical — gallery just stays empty.
      pastDesigns.value = []
    } finally {
      pastDesignsLoading.value = false
    }
  }

  return { pastDesigns, pastDesignsLoading, loadPastDesigns }
}

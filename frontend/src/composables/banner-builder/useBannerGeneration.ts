/**
 * useBannerGeneration — generation/polling/approve/regenerate/reorder state.
 *
 * Used by AiBannerBuilderView (AI + Manual modes).
 *
 * All external dependencies are passed in via the `options` argument so the
 * composable has no hard reference to the parent component's template refs.
 */
import { ref } from 'vue'
import { useRouter } from 'vue-router'
import { getBannerDesign } from '@/api/bannerBuilder'
import { fetchSizes } from '@/api/shop'
import {
  createAiRequest,
  getDesignRequest,
  approveDesignRequest,
  regenerateDesignRequest,
  type DesignRequestDetail,
  type DesignRequestListItem,
  type AiPaywallData,
} from '@/api/designRequests'
import { generateRequestIntegrity } from '@/composables/useRequestIntegrity'
import { useAuthStore } from '@/stores/auth'
import { useCartStore } from '@/stores/cart'
import type { BannerSize } from '@/types'

export type GenPhase =
  | 'idle'
  | 'submitting'
  | 'generating'
  | 'anon_pending'
  | 'ready'
  | 'tilpass'
  | 'error'

export interface BannerGenerationOptions {
  /** The currently-selected template id */
  getTemplateId: () => number | null
  /** 'nb' | 'en' */
  getLanguage: () => 'nb' | 'en'
  getPersonName: () => string
  getPersonAge: () => number | null
  getTextContent: () => string
  getThemeDescription: () => string
  getAspectRatioForBackend: () => string
  getUploadedPhotoBannerDesignId: () => number | null | undefined
  /** Returns { width, height } for the chosen quality option */
  getSelectedDimensions: () => { width: number; height: number }
  /** Called when a 402 paywall response is received */
  onPaywall: (data: AiPaywallData, action: 'generate' | 'regenerate') => void
  /** Called when a generation finishes (so the past-designs sidebar refreshes) */
  onGenerationComplete: () => void | Promise<void>
  /** Called to enter the tilpass step with the given banner design id */
  loadTilpassPricing: (bannerDesignId: number) => Promise<void>
  /** Expose whether we're in manual mode (some paths differ) */
  isManual: () => boolean
}

export function useBannerGeneration(options: BannerGenerationOptions) {
  const router = useRouter()
  const auth = useAuthStore()
  const cart = useCartStore()

  // ── Generation state ───────────────────────────────────────────────────────
  const genPhase = ref<GenPhase>('idle')
  const currentDesignRequest = ref<DesignRequestDetail | null>(null)
  const designRequestId = ref<number | null>(null)
  const requiresAuthHint = ref(false)
  const generateApiError = ref<string | null>(null)
  const approveError = ref<string | null>(null)
  const approving = ref(false)
  const regenerating = ref(false)
  const regenerateError = ref<string | null>(null)
  const editExpanded = ref(false)

  // ── Reorder ────────────────────────────────────────────────────────────────
  const reordering = ref(false)
  const reorderError = ref<string | null>(null)

  // ── Polling ────────────────────────────────────────────────────────────────
  const TERMINAL_STATUSES = ['AwaitingApproval', 'Approved', 'Final', 'Failed', 'Cancelled']
  let pollTimer: ReturnType<typeof setInterval> | null = null

  function startPolling(id: number) {
    genPhase.value = 'generating'
    stopPolling()
    pollTimer = setInterval(() => void pollOnce(id), 3000)
    void pollOnce(id)
  }

  function stopPolling() {
    if (pollTimer !== null) {
      clearInterval(pollTimer)
      pollTimer = null
    }
  }

  async function pollOnce(id: number) {
    try {
      const detail = await getDesignRequest(id)
      currentDesignRequest.value = detail
      if (TERMINAL_STATUSES.includes(detail.status)) {
        stopPolling()
        if (
          detail.status === 'AwaitingApproval' ||
          detail.status === 'Approved' ||
          detail.status === 'Final'
        ) {
          genPhase.value = 'ready'
          editExpanded.value = false
          void options.onGenerationComplete()
        } else {
          genPhase.value = 'error'
        }
      }
    } catch {
      // Transient errors — keep polling
    }
  }

  // ── Generate ───────────────────────────────────────────────────────────────
  async function generateBanner() {
    if (genPhase.value === 'submitting' || genPhase.value === 'generating') return
    generateApiError.value = null

    genPhase.value = 'submitting'
    try {
      const integrity = await generateRequestIntegrity()
      const resp = await createAiRequest(
        {
          templateId: options.getTemplateId()!,
          language: options.getLanguage(),
          personName: options.getPersonName().trim(),
          personAge: options.getPersonAge() ?? undefined,
          textContent: options.getTextContent().trim(),
          themeDescription: options.getThemeDescription().trim(),
          aspectRatio: options.getAspectRatioForBackend(),
          uploadedPhotoBannerDesignId: options.getUploadedPhotoBannerDesignId() ?? undefined,
        },
        integrity,
      )

      designRequestId.value = resp.designRequestId
      localStorage.setItem('ai_banner_draft_id', String(resp.designRequestId))

      if (resp.requiresAuth) {
        requiresAuthHint.value = true
        genPhase.value = 'anon_pending'
      } else {
        startPolling(resp.designRequestId)
      }

      return { creditsRemaining: resp.creditsRemaining }
    } catch (e: unknown) {
      const ex = e as {
        response?: { status?: number; data?: AiPaywallData & { error?: string } }
        message?: string
      }
      if (ex.response?.status === 402 && ex.response.data) {
        const d = ex.response.data
        options.onPaywall(
          {
            reason: d.reason ?? 'insufficient_credits',
            creditsRemaining: d.creditsRemaining ?? 0,
            paywallOptions: d.paywallOptions ?? defaultPaywallOptions(),
          },
          'generate',
        )
      } else {
        generateApiError.value =
          ex.response?.data?.error ?? ex.message ?? 'Generering feilet. Prøv igjen.'
      }
      genPhase.value = 'idle'
      return null
    }
  }

  // ── Approve ────────────────────────────────────────────────────────────────
  async function approve() {
    if (!designRequestId.value || approving.value) return
    approveError.value = null
    approving.value = true
    try {
      const chosen = options.getSelectedDimensions()
      const heightForApprove = chosen.height > 0 ? chosen.height : undefined
      const approved = await approveDesignRequest(designRequestId.value, heightForApprove)
      currentDesignRequest.value = approved
      localStorage.removeItem('ai_banner_draft_id')

      if (approved.finalBannerDesignId) {
        try {
          await options.loadTilpassPricing(approved.finalBannerDesignId)
          genPhase.value = 'tilpass'
          window.scrollTo({ top: 0, behavior: 'smooth' })
          return
        } catch {
          approveError.value = 'Prisberegning er ikke tilgjengelig. Prøv igjen.'
          return
        }
      }
      approveError.value =
        'Designet ble godkjent, men vi kunne ikke opprette bannerdesignet. Prøv igjen eller kontakt support.'
    } catch (e: unknown) {
      const ex = e as { response?: { data?: { error?: string } }; message?: string }
      approveError.value =
        ex.response?.data?.error || ex.message || 'Godkjenning feilet. Prøv igjen.'
    } finally {
      approving.value = false
    }
  }

  // ── Re-generate ────────────────────────────────────────────────────────────
  async function regenerate() {
    if (!designRequestId.value || regenerating.value) return
    regenerateError.value = null
    regenerating.value = true
    try {
      const integrity = await generateRequestIntegrity()
      const resp = await regenerateDesignRequest(
        designRequestId.value,
        {
          textContent: options.getTextContent().trim() || undefined,
          themeDescription: options.getThemeDescription().trim() || undefined,
        },
        integrity,
      )

      if (resp.newDesignRequestId) {
        designRequestId.value = resp.newDesignRequestId
        localStorage.setItem('ai_banner_draft_id', String(resp.newDesignRequestId))
      }
      genPhase.value = 'generating'
      currentDesignRequest.value = null
      editExpanded.value = false
      startPolling(designRequestId.value!)

      return { creditsRemaining: resp.creditsRemaining }
    } catch (e: unknown) {
      const ex = e as {
        response?: {
          status?: number
          data?: { error?: string; creditsRemaining?: number; paywallMetadata?: { reason?: string } }
        }
        message?: string
      }
      if (ex.response?.status === 402) {
        const d = ex.response?.data
        options.onPaywall(
          {
            reason: d?.paywallMetadata?.reason ?? d?.error ?? 'insufficient_credits',
            creditsRemaining: d?.creditsRemaining ?? 0,
            paywallOptions: defaultPaywallOptions(),
          },
          'regenerate',
        )
      } else if (ex.response?.status === 401) {
        regenerateError.value = 'Du må være innlogget for å generere på nytt.'
      } else {
        regenerateError.value =
          ex.response?.data?.error || ex.message || 'Ny generering feilet. Prøv igjen.'
      }
      return null
    } finally {
      regenerating.value = false
    }
  }

  // ── Reorder ────────────────────────────────────────────────────────────────
  async function reorderCurrentDesign() {
    const d = currentDesignRequest.value
    if (!d?.finalBannerDesignId || reordering.value) return
    reordering.value = true
    reorderError.value = null
    try {
      const design = await getBannerDesign(d.finalBannerDesignId)
      const sizes = await fetchSizes(design.computedWidthCm)
      const pricingSize = sizes.find(
        (s: BannerSize) => s.isCustomWidth && s.heightCm === design.selectedHeightCm,
      )
      if (pricingSize && pricingSize.calculatedPrice != null) {
        cart.addItem({
          bannerSizeId: pricingSize.id,
          bannerSizeName: `AI banner ${design.computedWidthCm} × ${design.selectedHeightCm} cm`,
          customWidthCm: design.computedWidthCm,
          heightCm: design.selectedHeightCm,
          quantity: 1,
          unitPriceNok: pricingSize.calculatedPrice,
          eyeletOption: 'None',
          eyeletFeeNok: 0,
          designId: d.finalBannerDesignId,
          previewUrl: d.previewUrl ?? design.previewUrl ?? undefined,
          notes: `AI banner design #${d.finalBannerDesignId}`,
        })
        void router.push('/checkout')
      } else {
        reorderError.value = 'Kunne ikke finne pris for dette banneret. Prøv igjen.'
      }
    } catch {
      reorderError.value = 'Noe gikk galt ved bestilling. Prøv igjen.'
    } finally {
      reordering.value = false
    }
  }

  // ── selectPastDesign (AI mode only) ────────────────────────────────────────
  async function selectPastDesign(item: DesignRequestListItem) {
    if (options.isManual()) {
      void router.push(`/account/design-requests/${item.id}`)
      return
    }
    try {
      const detail = await getDesignRequest(item.id)
      designRequestId.value = item.id
      currentDesignRequest.value = detail
      editExpanded.value = false
      window.scrollTo({ top: 0, behavior: 'smooth' })

      if (detail.status === 'AwaitingApproval' || detail.status === 'Approved' || detail.status === 'Final') {
        genPhase.value = 'ready'
      } else if (detail.status === 'InProgress' || detail.status === 'Pending') {
        startPolling(item.id)
      } else {
        genPhase.value = 'error'
      }
      return detail
    } catch {
      void router.push(`/account/design-requests/${item.id}`)
      return null
    }
  }

  // ── Return to wizard idle ──────────────────────────────────────────────────
  function returnToWizardIdle() {
    genPhase.value = 'idle'
    generateApiError.value = null
    regenerateError.value = null
    approveError.value = null
    currentDesignRequest.value = null
    designRequestId.value = null
    localStorage.removeItem('ai_banner_draft_id')
  }

  // ── Lifecycle ──────────────────────────────────────────────────────────────
  function cleanup() {
    stopPolling()
  }

  return {
    genPhase,
    currentDesignRequest,
    designRequestId,
    requiresAuthHint,
    generateApiError,
    approveError,
    approving,
    regenerating,
    regenerateError,
    editExpanded,
    reordering,
    reorderError,
    startPolling,
    stopPolling,
    generateBanner,
    approve,
    regenerate,
    reorderCurrentDesign,
    selectPastDesign,
    returnToWizardIdle,
    cleanup,
  }
}

// ── Helpers ──────────────────────────────────────────────────────────────────

function defaultPaywallOptions() {
  return {
    creditPackSmallPriceNok: 29,
    creditPackSmallCount: 5,
    creditPackLargePriceNok: 95,
    creditPackLargeCount: 20,
    creditPackPriceNok: 29,
    creditPackCount: 5,
    bannerOrderActivationFeeNok: 95,
    bannerOrderCreditBonus: 20,
    manualDesignerUrl: '/banner-builder/manual',
    uploadOwnUrl: '/banner-builder',
  }
}

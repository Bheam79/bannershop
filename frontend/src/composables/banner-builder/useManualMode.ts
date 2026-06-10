/**
 * useManualMode — Manual-banner-builder mode helpers.
 *
 * Handles:
 *  - Canvas placeholder generation ("Ditt banner")
 *  - Session-state save/restore (for post-login return)
 *  - createManualRequest API call + tilpass transition
 */
import { ref } from 'vue'
import { useRouter } from 'vue-router'
import { createManualRequest } from '@/api/designRequests'
import { fetchEyeletPriceNok } from '@/api/shop'
import { useAuthStore } from '@/stores/auth'
import type { DesignRequestDetail } from '@/api/designRequests'
import type { BannerSize } from '@/types'

export const MANUAL_DESIGN_FEE_NOK = 495
const MANUAL_SESSION_KEY = 'manual_banner_builder_state'

export type AspectRatioOption = '16:9' | '1:2' | '1:1' | '2:1' | '3:1' | '4:1'
export type QualityOption = 'high' | 'good' | 'custom'

interface ManualModeOptions {
  getTemplateId: () => number | null
  getLanguage: () => 'nb' | 'en'
  getPersonName: () => string
  getPersonAge: () => number | null
  getTextContent: () => string
  getThemeDescription: () => string
  getAspectRatioForBackend: () => string
  getUploadedPhotoBannerDesignId: () => number | null | undefined
  getSelectedAspectRatio: () => AspectRatioOption
  getSelectedQuality: () => QualityOption
  getCustomWidth: () => number | null
  getCustomHeight: () => number | null
  getCustomMaterialGsm: () => 400 | 680
  /** Provides the sizes catalogue and pickBannerSize helper from useBannerPricing */
  getSizes: () => BannerSize[]
  getSizesLoaded: () => boolean
  pickBannerSize: (
    catalog: BannerSize[],
    widthCm: number,
    heightCm: number,
    materialGsm?: number,
  ) => { size: BannerSize; customWidthCm?: number } | null
  getSelectedDimensions: () => { width: number; height: number }
  /** Called to enter the tilpass step — mirrors AI approve() */
  setTilpassState: (
    widthCm: number,
    heightCm: number,
    size: BannerSize,
    bannerPriceNok: number,
    eyeletPriceNok: number,
  ) => void
  /** State setters for restore */
  setSelectedTemplateId: (v: number | null) => void
  setLanguage: (v: 'nb' | 'en') => void
  setUploadedPhotoBannerDesignId: (v: number | null) => void
  setPersonName: (v: string) => void
  setPersonAge: (v: number | null) => void
  setTextContent: (v: string) => void
  setThemeDescription: (v: string) => void
  setSelectedAspectRatio: (v: AspectRatioOption) => void
  setSelectedQuality: (v: QualityOption) => void
  setCustomWidth: (v: number | null) => void
  setCustomHeight: (v: number | null) => void
  setCustomMaterialGsm: (v: 400 | 680) => void
}

export function useManualMode(options: ManualModeOptions) {
  const router = useRouter()
  const auth = useAuthStore()

  const manualSubmitting = ref(false)
  const manualSubmitError = ref<string | null>(null)
  const manualDesignRequestId = ref<number | null>(null)
  const manualBannerPriceNok = ref<number>(0)
  const manualDesignPriceNok = ref<number>(MANUAL_DESIGN_FEE_NOK)
  const manualPlaceholderUrl = ref<string | null>(null)

  // ── Canvas placeholder ─────────────────────────────────────────────────────
  function generatePlaceholderDataUrl(widthUnits: number, heightUnits: number): string {
    const targetH = 600
    const targetW = Math.max(1, Math.round((targetH * widthUnits) / Math.max(1, heightUnits)))
    const canvas = document.createElement('canvas')
    canvas.width = targetW
    canvas.height = targetH
    const ctx = canvas.getContext('2d')
    if (!ctx) return canvas.toDataURL('image/png')

    const gradient = ctx.createLinearGradient(0, 0, targetW, targetH)
    gradient.addColorStop(0, '#3a2e22')
    gradient.addColorStop(1, '#1e1813')
    ctx.fillStyle = gradient
    ctx.fillRect(0, 0, targetW, targetH)

    ctx.strokeStyle = 'rgba(231, 185, 78, 0.45)'
    ctx.lineWidth = 4
    ctx.strokeRect(8, 8, targetW - 16, targetH - 16)

    const titleSize = Math.round(Math.min(targetW, targetH * 1.4) * 0.13)
    ctx.fillStyle = '#e7b94e'
    ctx.font = `bold ${titleSize}px Georgia, serif`
    ctx.textAlign = 'center'
    ctx.textBaseline = 'middle'
    ctx.fillText('Ditt banner', targetW / 2, targetH / 2 - titleSize * 0.15)

    const subSize = Math.max(12, Math.round(titleSize * 0.32))
    ctx.fillStyle = 'rgba(244, 239, 232, 0.55)'
    ctx.font = `${subSize}px Hanken Grotesk, ui-sans-serif, system-ui, sans-serif`
    ctx.fillText(
      'Designer lager forhåndsvisning innen 2–3 virkedager',
      targetW / 2,
      targetH / 2 + titleSize * 0.8,
    )
    return canvas.toDataURL('image/png')
  }

  /**
   * Generates a canvas "Ditt banner" placeholder for the chosen aspect ratio,
   * and synthesises a minimal DesignRequestDetail so the 'ready' phase renders.
   * Returns the synthetic detail (caller should set it as currentDesignRequest).
   */
  function generateManualPlaceholder(
    selectedAspectRatio: AspectRatioOption,
    aspectRatioForBackend: string,
  ): { detail: DesignRequestDetail; ratio: number } {
    const parts = selectedAspectRatio.split(':')
    const rW = parseInt(parts[0] ?? '0', 10) || 16
    const rH = parseInt(parts[1] ?? '0', 10) || 9
    manualPlaceholderUrl.value = generatePlaceholderDataUrl(rW, rH)

    const detail: DesignRequestDetail = {
      id: 0,
      userId: null,
      bannerTemplateId: options.getTemplateId() ?? 0,
      mode: 'Manual',
      status: 'AwaitingApproval',
      language: options.getLanguage(),
      personName: options.getPersonName().trim(),
      personAge: options.getPersonAge() ?? null,
      textContent: options.getTextContent().trim(),
      themeDescription: options.getThemeDescription().trim(),
      aspectRatio: aspectRatioForBackend,
      revisionCount: 0,
      regenerationsRemaining: 0,
      priceNok: 0,
      stripePaymentIntentId: null,
      previewUrl: manualPlaceholderUrl.value,
      finalCroppedUrl: null,
      finalBannerDesignId: null,
      currentGenerationId: null,
      lastError: null,
      generationHistory: [],
      createdAt: new Date().toISOString(),
      updatedAt: new Date().toISOString(),
    }
    return { detail, ratio: rW / rH }
  }

  // ── Session save / restore (for post-login return) ─────────────────────────
  function saveManualSessionState() {
    try {
      sessionStorage.setItem(
        MANUAL_SESSION_KEY,
        JSON.stringify({
          selectedTemplateId: options.getTemplateId(),
          language: options.getLanguage(),
          uploadedPhotoBannerDesignId: options.getUploadedPhotoBannerDesignId() ?? null,
          personName: options.getPersonName(),
          personAge: options.getPersonAge(),
          textContent: options.getTextContent(),
          themeDescription: options.getThemeDescription(),
          selectedAspectRatio: options.getSelectedAspectRatio(),
          selectedQuality: options.getSelectedQuality(),
          customWidth: options.getCustomWidth(),
          customHeight: options.getCustomHeight(),
          customMaterialGsm: options.getCustomMaterialGsm(),
        }),
      )
    } catch { /* non-fatal */ }
  }

  function restoreManualSessionState(): boolean {
    const saved = sessionStorage.getItem(MANUAL_SESSION_KEY)
    if (!saved) return false
    try {
      const s = JSON.parse(saved) as {
        selectedTemplateId: number | null
        language: 'nb' | 'en'
        uploadedPhotoBannerDesignId: number | null
        personName: string
        personAge: number | null
        textContent: string
        themeDescription: string
        selectedAspectRatio?: AspectRatioOption
        selectedQuality?: QualityOption
        customWidth?: number | null
        customHeight?: number | null
        customMaterialGsm?: 400 | 680
      }
      if (s.selectedTemplateId !== null) options.setSelectedTemplateId(s.selectedTemplateId)
      options.setLanguage(s.language)
      options.setUploadedPhotoBannerDesignId(s.uploadedPhotoBannerDesignId)
      options.setPersonName(s.personName)
      options.setPersonAge(s.personAge)
      options.setTextContent(s.textContent)
      options.setThemeDescription(s.themeDescription)
      if (s.selectedAspectRatio) options.setSelectedAspectRatio(s.selectedAspectRatio)
      if (s.selectedQuality) options.setSelectedQuality(s.selectedQuality)
      if (s.customWidth != null) options.setCustomWidth(s.customWidth)
      if (s.customHeight != null) options.setCustomHeight(s.customHeight)
      if (s.customMaterialGsm) options.setCustomMaterialGsm(s.customMaterialGsm)
      sessionStorage.removeItem(MANUAL_SESSION_KEY)
      return true
    } catch {
      sessionStorage.removeItem(MANUAL_SESSION_KEY)
      return false
    }
  }

  // ── Manual "Gå videre" (creates the DesignRequest) ────────────────────────
  async function manualGoVidere() {
    if (manualSubmitting.value) return
    manualSubmitError.value = null

    if (!auth.isLoggedIn) {
      saveManualSessionState()
      void router.push('/login?redirect=/banner-builder/manual')
      return
    }

    manualSubmitting.value = true
    try {
      const resp = await createManualRequest({
        templateId: options.getTemplateId()!,
        language: options.getLanguage(),
        personName: options.getPersonName().trim(),
        personAge: options.getPersonAge() ?? undefined,
        textContent: options.getTextContent().trim(),
        themeDescription: options.getThemeDescription().trim(),
        aspectRatio: options.getAspectRatioForBackend(),
        uploadedPhotoBannerDesignId: options.getUploadedPhotoBannerDesignId() ?? undefined,
      })
      manualDesignRequestId.value = resp.designRequestId
      manualBannerPriceNok.value = resp.bannerPriceNok
      manualDesignPriceNok.value = resp.designPriceNok

      const dims = options.getSelectedDimensions()
      if (dims.width > 0 && dims.height > 0 && options.getSizesLoaded()) {
        const picked = options.pickBannerSize(
          options.getSizes(),
          dims.width,
          dims.height,
          options.getSelectedQuality() === 'custom' ? options.getCustomMaterialGsm() : undefined,
        )
        if (picked) {
          let eyeletPrice = 0
          try { eyeletPrice = await fetchEyeletPriceNok() } catch { /* non-critical */ }
          options.setTilpassState(
            dims.width,
            dims.height,
            picked.size,
            resp.bannerPriceNok,
            eyeletPrice,
          )
          return
        }
      }
      // Fallback: route to account detail page
      void router.push(`/account/design-requests/${resp.designRequestId}`)
    } catch (e: unknown) {
      const ex = e as { response?: { data?: { error?: string } }; message?: string }
      manualSubmitError.value =
        ex.response?.data?.error || ex.message || 'Kunne ikke opprette bestilling. Prøv igjen.'
    } finally {
      manualSubmitting.value = false
    }
  }

  function resetManual() {
    manualPlaceholderUrl.value = null
    manualSubmitError.value = null
  }

  return {
    MANUAL_DESIGN_FEE_NOK,
    manualSubmitting,
    manualSubmitError,
    manualDesignRequestId,
    manualBannerPriceNok,
    manualDesignPriceNok,
    manualPlaceholderUrl,
    generatePlaceholderDataUrl,
    generateManualPlaceholder,
    saveManualSessionState,
    restoreManualSessionState,
    manualGoVidere,
    resetManual,
  }
}

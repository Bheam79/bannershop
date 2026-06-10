/**
 * useBannerPricing — quality/size/price state for the post-generation picker.
 *
 * Used by AiBannerBuilderView (AI + Manual modes).
 *
 * Dependencies injected by the parent:
 *   none — all data fetched internally via fetchSizes / fetchPrice.
 */
import { ref, computed, watch, nextTick } from 'vue'
import { fetchSizes, fetchPrice } from '@/api/shop'
import type { BannerSize } from '@/types'

export type QualityOption = 'high' | 'good' | 'custom'

export interface OptionPriceState {
  price: number | null
  loading: boolean
  comingSoon: boolean
}

export function useBannerPricing() {
  // ── Catalogue ──────────────────────────────────────────────────────────────
  const sizes = ref<BannerSize[]>([])
  const sizesLoaded = ref(false)

  // ── Quality / size selection ───────────────────────────────────────────────
  const selectedQuality = ref<QualityOption>('high')
  const customWidth = ref<number | null>(null)
  const customHeight = ref<number | null>(null)
  const customMaterialGsm = ref<400 | 680>(400)

  // Price state for each quality option
  const option1State = ref<OptionPriceState>({ price: null, loading: false, comingSoon: false })
  const option2State = ref<OptionPriceState>({ price: null, loading: false, comingSoon: false })
  const customState = ref<OptionPriceState>({ price: null, loading: false, comingSoon: false })

  // ── BANNERSH-162: AI image aspect ratio ────────────────────────────────────
  // Set when the preview <img> fires @load; cleared on new generation.
  const aiImageNaturalRatio = ref<number | null>(null)

  // Effective aspect ratio: prefer loaded image; fall back to parsing
  // currentDesignRequest.aspectRatio ('WxH' or 'A:B').
  function computeAspectRatioFromString(raw: string | null | undefined): number | null {
    if (!raw) return null
    const dimsMatch = /^(\d+)x(\d+)$/i.exec(raw)
    if (dimsMatch && dimsMatch[1] && dimsMatch[2]) {
      const w = parseInt(dimsMatch[1], 10)
      const h = parseInt(dimsMatch[2], 10)
      if (w > 0 && h > 0) return w / h
    }
    const ratioMatch = /^(\d+):(\d+)$/.exec(raw)
    if (ratioMatch && ratioMatch[1] && ratioMatch[2]) {
      const a = parseInt(ratioMatch[1], 10)
      const b = parseInt(ratioMatch[2], 10)
      if (a > 0 && b > 0) return a / b
    }
    return null
  }

  // The parent provides the current designRequest's aspectRatio so this
  // computed works even before the image loads.
  const currentAspectRatioString = ref<string | null>(null)

  const aiImageAspectRatio = computed<number | null>(() => {
    if (aiImageNaturalRatio.value && aiImageNaturalRatio.value > 0)
      return aiImageNaturalRatio.value
    return computeAspectRatioFromString(currentAspectRatioString.value)
  })

  // ── Derived widths for preset quality options ──────────────────────────────
  const highOptionWidthCm = computed(() => {
    const r = aiImageAspectRatio.value
    return r ? Math.round(150 * r) : 360
  })
  const goodOptionWidthCm = computed(() => {
    const r = aiImageAspectRatio.value
    return r ? Math.round(180 * r) : 300
  })

  const selectedDimensions = computed(() => {
    if (selectedQuality.value === 'high') return { width: highOptionWidthCm.value, height: 150 }
    if (selectedQuality.value === 'good') return { width: goodOptionWidthCm.value, height: 180 }
    return { width: customWidth.value ?? 0, height: customHeight.value ?? 0 }
  })

  // ── Banner-size catalogue helpers ──────────────────────────────────────────
  function pickBannerSize(
    catalog: BannerSize[],
    targetWidthCm: number,
    targetHeightCm: number,
    materialGsm?: number,
  ): { size: BannerSize; customWidthCm?: number } | null {
    const exact = catalog.find(
      (s) =>
        s.isActive &&
        !s.isCustomWidth &&
        s.widthCm === targetWidthCm &&
        s.heightCm === targetHeightCm &&
        (materialGsm == null || s.material?.weightGsm === materialGsm),
    )
    if (exact) return { size: exact }
    const custom = catalog.find(
      (s) =>
        s.isActive &&
        s.isCustomWidth &&
        s.heightCm === targetHeightCm &&
        (materialGsm == null || s.material?.weightGsm === materialGsm),
    )
    if (custom) return { size: custom, customWidthCm: targetWidthCm }
    return null
  }

  function isComingSoon(size: BannerSize): boolean {
    if (!size.availableFrom) return false
    return new Date(size.availableFrom) > new Date()
  }

  // ── Price fetching ─────────────────────────────────────────────────────────
  async function computeOptionPrice(
    targetWidth: number,
    targetHeight: number,
    state: OptionPriceState,
    materialGsm?: number,
    skipSurcharge?: boolean,
  ) {
    state.loading = true
    state.price = null
    state.comingSoon = false
    try {
      const picked = pickBannerSize(sizes.value, targetWidth, targetHeight, materialGsm)
      if (!picked) { state.price = null; return }
      state.comingSoon = isComingSoon(picked.size)
      state.price = await fetchPrice(picked.size.id, picked.customWidthCm, skipSurcharge)
    } catch {
      state.price = null
    } finally {
      state.loading = false
    }
  }

  async function refreshAllPrices() {
    if (!sizesLoaded.value) return
    await Promise.all([
      computeOptionPrice(highOptionWidthCm.value, 150, option1State.value, undefined, true),
      computeOptionPrice(goodOptionWidthCm.value, 180, option2State.value, undefined, true),
    ])
  }

  async function refreshCustomPrice() {
    const w = customWidth.value ?? 0
    const h = customHeight.value ?? 0
    if (!sizesLoaded.value || w <= 0 || h <= 0) { customState.value.price = null; return }
    await computeOptionPrice(w, h, customState.value, customMaterialGsm.value)
  }

  async function loadSizesAndPricing() {
    try {
      sizes.value = await fetchSizes()
      sizesLoaded.value = true
      await refreshAllPrices()
    } catch {
      // Non-critical — prices just won't display
    }
  }

  // ── Preview image load handler (called from template @load) ────────────────
  function onPreviewImageLoaded(e: Event) {
    const img = e.target as HTMLImageElement
    if (img.naturalWidth > 0 && img.naturalHeight > 0) {
      aiImageNaturalRatio.value = img.naturalWidth / img.naturalHeight
    }
  }

  // ── Watchers ───────────────────────────────────────────────────────────────

  // Re-price when custom W/H/material changes
  watch([customWidth, customHeight, customMaterialGsm], () => {
    if (sizesLoaded.value) void refreshCustomPrice()
  })

  // Re-price when AI image ratio resolves or changes
  watch(aiImageAspectRatio, () => {
    if (sizesLoaded.value) void refreshAllPrices()
  })

  // Link custom W ↔ H via the AI image ratio so the print preserves proportions.
  // A lock prevents the linked-update from cascading back.
  let customLinkLock = false
  function releaseCustomLink() {
    void nextTick(() => { customLinkLock = false })
  }
  watch(customWidth, (w) => {
    if (customLinkLock) return
    const r = aiImageAspectRatio.value
    if (!r || !w || w <= 0) return
    customLinkLock = true
    customHeight.value = Math.max(1, Math.round(w / r))
    releaseCustomLink()
  })
  watch(customHeight, (h) => {
    if (customLinkLock) return
    const r = aiImageAspectRatio.value
    if (!r || !h || h <= 0) return
    customLinkLock = true
    customWidth.value = Math.max(1, Math.round(h * r))
    releaseCustomLink()
  })

  // Pre-fill custom size from high-quality defaults when custom tab first opened
  watch(selectedQuality, (q) => {
    if (q !== 'custom') return
    const r = aiImageAspectRatio.value
    if (!r) return
    if (customWidth.value == null && customHeight.value == null) {
      customLinkLock = true
      customHeight.value = 150
      customWidth.value = Math.max(1, Math.round(150 * r))
      releaseCustomLink()
    }
  })

  // Auto-switch away from a "Kommer snart" option (BANNERSH-167)
  watch(
    [() => option1State.value.comingSoon, () => option2State.value.comingSoon],
    () => {
      if (selectedQuality.value === 'high' && option1State.value.comingSoon) {
        selectedQuality.value = option2State.value.comingSoon ? 'custom' : 'good'
      } else if (selectedQuality.value === 'good' && option2State.value.comingSoon) {
        selectedQuality.value = option1State.value.comingSoon ? 'custom' : 'high'
      }
    },
  )

  /** Reset state between generations. */
  function resetForNewGeneration() {
    aiImageNaturalRatio.value = null
    currentAspectRatioString.value = null
  }

  return {
    sizes,
    sizesLoaded,
    selectedQuality,
    customWidth,
    customHeight,
    customMaterialGsm,
    option1State,
    option2State,
    customState,
    aiImageNaturalRatio,
    aiImageAspectRatio,
    currentAspectRatioString,
    highOptionWidthCm,
    goodOptionWidthCm,
    selectedDimensions,
    pickBannerSize,
    isComingSoon,
    loadSizesAndPricing,
    refreshAllPrices,
    refreshCustomPrice,
    onPreviewImageLoaded,
    resetForNewGeneration,
  }
}

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
import type { BannerSize, Material } from '@/types'

/**
 * One preset quality option derived from the loaded catalog.
 * BANNERSH-259: each option maps to exactly one material, and its target height
 * is that material's `pricingHeightCm` from the mult-1 (single-panel) rule.
 */
export interface MaterialOption {
  material: Material
  /** The single-panel (pricingMultiplier = 1, no fixedPrice) rule for this material. */
  singlePanelRule: BannerSize
  /** Target print height in cm — equals singlePanelRule.pricingHeightCm. */
  heightCm: number
}

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

  // ── BANNERSH-259: per-material preset options derived from the catalog ────────
  /**
   * One entry per active material that has a single-panel (pricingMultiplier = 1,
   * no fixedPrice) rule in the catalog, sorted by that rule's sortOrder. Each
   * entry's heightCm is the pricingHeightCm of that rule, which becomes the target
   * height for the corresponding quality button in the picker.
   *
   * With the current two-material seed:
   *   [0] → 680g outdoor, heightCm = 154  (Høykvalitet)
   *   [1] → 400g indoor,  heightCm = 180  (God kvalitet, Kommer snart)
   */
  const materialOptions = computed<MaterialOption[]>(() => {
    const byMaterial = new Map<number, MaterialOption>()
    for (const s of sizes.value) {
      if (!s.isActive || s.pricingMultiplier !== 1 || s.fixedPrice != null) continue
      if (!s.material) continue
      if (!byMaterial.has(s.materialId)) {
        byMaterial.set(s.materialId, {
          material: s.material,
          singlePanelRule: s,
          heightCm: s.pricingHeightCm,
        })
      }
    }
    return [...byMaterial.values()].sort(
      (a, b) => a.singlePanelRule.sortOrder - b.singlePanelRule.sortOrder,
    )
  })

  /**
   * Target height in cm for the "Høykvalitet" button (first catalog material, mult=1).
   * Falls back to 154 (680g pricingHeightCm) when the catalog is not yet loaded.
   */
  const highOptionHeightCm = computed(() => materialOptions.value[0]?.heightCm ?? 154)
  /**
   * Target height in cm for the "God kvalitet" button (second catalog material, mult=1).
   * Falls back to 180 (400g pricingHeightCm) when the catalog is not yet loaded.
   */
  const goodOptionHeightCm = computed(() => materialOptions.value[1]?.heightCm ?? 180)

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
  // BANNERSH-259: heights come from the catalog (material's mult=1 pricingHeightCm).
  const highOptionWidthCm = computed(() => {
    const r = aiImageAspectRatio.value
    return r ? Math.round(highOptionHeightCm.value * r) : 360
  })
  const goodOptionWidthCm = computed(() => {
    const r = aiImageAspectRatio.value
    return r ? Math.round(goodOptionHeightCm.value * r) : 300
  })

  const selectedDimensions = computed(() => {
    // BANNERSH-259: use catalog-derived heights, not hardcoded 150/180.
    if (selectedQuality.value === 'high') return { width: highOptionWidthCm.value, height: highOptionHeightCm.value }
    if (selectedQuality.value === 'good') return { width: goodOptionWidthCm.value, height: goodOptionHeightCm.value }
    return { width: customWidth.value ?? 0, height: customHeight.value ?? 0 }
  })

  // ── Banner-size catalogue helpers ──────────────────────────────────────────
  /**
   * BANNERSH-255: picks the cheapest matching pricing rule for the given
   * (width, height [, materialGsm]) by searching the loaded catalogue. Returns
   * the rule + the actual widthCm so the cart can persist the chosen rule id.
   */
  function pickBannerSize(
    catalog: BannerSize[],
    targetWidthCm: number,
    targetHeightCm: number,
    materialGsm?: number,
  ): { size: BannerSize; customWidthCm: number } | null {
    const matching = catalog.filter(
      (s) =>
        s.isActive &&
        s.minWidthCm <= targetWidthCm && targetWidthCm <= s.maxWidthCm &&
        s.minHeightCm <= targetHeightCm && targetHeightCm <= s.maxHeightCm &&
        (materialGsm == null || s.material?.weightGsm === materialGsm),
    )
    if (matching.length === 0) return null
    // BANNERSH-259: prefer currently-available rules — only fall back to
    // coming-soon when no available rule covers the requested dimensions.
    const availableMatching = matching.filter((s) => !isComingSoon(s))
    const candidates = availableMatching.length > 0 ? availableMatching : matching
    // Estimate price locally so we pick the cheapest. Server confirms via fetchPrice.
    const priceOf = (s: BannerSize) => {
      if (s.fixedPrice != null) return s.fixedPrice
      const pps = s.material?.pricePerSqm ?? 180
      const area = (targetWidthCm / 100) * (s.pricingHeightCm / 100)
      return Math.max(area * pps, 399) * (s.pricingMultiplier || 1)
    }
    let best = candidates[0]!
    let bestPrice = priceOf(best)
    for (const s of candidates.slice(1)) {
      const p = priceOf(s)
      if (p < bestPrice) { best = s; bestPrice = p }
    }
    return { size: best, customWidthCm: targetWidthCm }
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
  ) {
    state.loading = true
    state.price = null
    state.comingSoon = false
    try {
      const picked = pickBannerSize(sizes.value, targetWidth, targetHeight, materialGsm)
      if (!picked) { state.price = null; return }
      state.comingSoon = isComingSoon(picked.size)
      const resp = await fetchPrice(targetWidth, targetHeight, picked.size.materialId)
      state.price = resp.priceNok
    } catch {
      state.price = null
    } finally {
      state.loading = false
    }
  }

  async function refreshAllPrices() {
    if (!sizesLoaded.value) return
    // BANNERSH-259: pin each option to its catalog material so coming-soon
    // materials don't bleed into the wrong button.
    const opts = materialOptions.value
    await Promise.all([
      opts[0]
        ? computeOptionPrice(highOptionWidthCm.value, opts[0].heightCm, option1State.value, opts[0].material.weightGsm)
        : Promise.resolve(),
      opts[1]
        ? computeOptionPrice(goodOptionWidthCm.value, opts[1].heightCm, option2State.value, opts[1].material.weightGsm)
        : Promise.resolve(),
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
    materialOptions,
    highOptionHeightCm,
    goodOptionHeightCm,
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

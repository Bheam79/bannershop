<script setup lang="ts">
import { ref, computed, watch, onMounted, onBeforeUnmount } from 'vue'
import { useRouter, useRoute, RouterLink } from 'vue-router'
import { useAuthStore } from '@/stores/auth'
import { useCartStore } from '@/stores/cart'
import { getBannerDesign } from '@/api/bannerBuilder'
import { fetchSizes, fetchEyeletPriceNok } from '@/api/shop'
import type { BannerSize, EyeletOption, CartItem } from '@/types'
import { countEyelets } from '@/types'
import EyeletPreview from '@/components/shop/EyeletPreview.vue'
import {
  fetchTemplates,
  getDesignRequest,
  type BannerTemplateItem,
  type DesignRequestListItem,
  type AiPaywallData,
  type PaywallOptions,
} from '@/api/designRequests'
import { getAiCreditsBalance } from '@/api/aiCredits'
import { useAiCreditsStore } from '@/stores/aiCredits'
import { formatNok } from '@/utils/format'

// ── Composables ───────────────────────────────────────────────────────────────
import { usePhotoUpload } from '@/composables/banner-builder/usePhotoUpload'
import { usePastDesigns } from '@/composables/banner-builder/usePastDesigns'
import { useBannerPricing } from '@/composables/banner-builder/useBannerPricing'
import { useBannerGeneration } from '@/composables/banner-builder/useBannerGeneration'
import {
  useManualMode,
  MANUAL_DESIGN_FEE_NOK,
  type AspectRatioOption,
} from '@/composables/banner-builder/useManualMode'

// ── Sub-components ────────────────────────────────────────────────────────────
import PastBannersGallery from '@/components/banner-builder/PastBannersGallery.vue'
import PaywallModal from '@/components/banner-builder/PaywallModal.vue'

// ── Router / stores ───────────────────────────────────────────────────────────
const router = useRouter()
const route = useRoute()
const auth = useAuthStore()
const cart = useCartStore()
const creditsStore = useAiCreditsStore()

// ── Mode ──────────────────────────────────────────────────────────────────────
const mode = computed<'ai' | 'manual'>(() =>
  route.path.endsWith('/manual') ? 'manual' : 'ai',
)
const isManual = computed(() => mode.value === 'manual')

// ── Step state ────────────────────────────────────────────────────────────────
const step = ref<1 | 2 | 3>(1)

// ── Step 1: Templates + language ──────────────────────────────────────────────
const templates = ref<BannerTemplateItem[]>([])
const templatesLoading = ref(true)
const templatesError = ref<string | null>(null)
const selectedTemplateId = ref<number | null>(null)
const language = ref<'nb' | 'en'>('nb')

// ── Step 2: Personalization ───────────────────────────────────────────────────
const personName = ref('')
const personAge = ref<number | null>(null)
const textContent = ref('')
const themeDescription = ref('')

// ── Step 2: Aspect ratio ──────────────────────────────────────────────────────
const selectedAspectRatio = ref<AspectRatioOption>('16:9')
const ratioOptions = [
  { value: '16:9' as AspectRatioOption, label: '16:9', sub: 'anbefalt', iconW: 28, iconH: 16 },
  { value: '1:2' as AspectRatioOption, label: '1:2', sub: 'loddrett',  iconW: 11, iconH: 22 },
  { value: '1:1' as AspectRatioOption, label: '1:1', sub: 'firkantet', iconW: 20, iconH: 20 },
  { value: '2:1' as AspectRatioOption, label: '2:1', sub: 'avlangt',   iconW: 28, iconH: 14 },
  { value: '3:1' as AspectRatioOption, label: '3:1', sub: 'veldig langt', iconW: 28, iconH: 9 },
  { value: '4:1' as AspectRatioOption, label: '4:1', sub: 'superlangt', iconW: 28, iconH: 7 },
] as const

// ── Credits state (AI only) ───────────────────────────────────────────────────
const creditsRemaining = ref<number | null>(null)
const hasUsedFreeGeneration = ref<boolean | null>(null)

// ── Generation progress bar ───────────────────────────────────────────────────
const genProgress = ref(0)
let genProgressRaf: number | null = null
let genProgressStart = 0

function startProgressBar() {
  genProgress.value = 0
  genProgressStart = Date.now()
  function tick() {
    const elapsed = (Date.now() - genProgressStart) / 1000
    let p: number
    if (elapsed <= 60) {
      // Linear phase: 0 → 90% over 60 seconds
      p = (elapsed / 60) * 90
    } else {
      // Logarithmic slowdown after 90%: asymptotically approaches ~99.5%
      p = 90 + 9.5 * (1 - Math.exp(-(elapsed - 60) / 120))
    }
    genProgress.value = Math.min(99.5, p)
    genProgressRaf = requestAnimationFrame(tick)
  }
  genProgressRaf = requestAnimationFrame(tick)
}

function stopProgressBar() {
  if (genProgressRaf !== null) {
    cancelAnimationFrame(genProgressRaf)
    genProgressRaf = null
  }
  genProgress.value = 0
}

// ── Paywall state ─────────────────────────────────────────────────────────────
const paywallOpen = ref(false)
const paywallData = ref<AiPaywallData | null>(null)
const pendingAction = ref<'generate' | 'regenerate'>('generate')

// ── Tilpass state ─────────────────────────────────────────────────────────────
const tilpassDesignWidthCm = ref<number>(0)
const tilpassDesignHeightCm = ref<number>(0)
const tilpassBannerSize = ref<BannerSize | null>(null)
const tilpassBannerPriceNok = ref<number>(0)
const tilpassEyeletOption = ref<EyeletOption>('None')
const tilpassEyeletPriceNok = ref<number>(0)
const tilpassLoading = ref(false)
const tilpassError = ref<string | null>(null)

// ── Composable: Photo upload ──────────────────────────────────────────────────
const {
  uploadedPhotoBannerDesignId, photoPreviewUrl, photoUploading, photoUploadProgress,
  photoUploadError, photoFileInput, photoDragging,
  openPhotoPicker, onPhotoFileChange, onPhotoDragOver, onPhotoDragLeave, onPhotoDrop, removePhoto,
} = usePhotoUpload()

// ── Composable: Past designs ──────────────────────────────────────────────────
const { pastDesigns, loadPastDesigns } = usePastDesigns(() => mode.value)

// ── Composable: Banner pricing ────────────────────────────────────────────────
const {
  sizes, sizesLoaded, selectedQuality, customWidth, customHeight, customMaterialGsm,
  option1State, option2State, customState,
  aiImageNaturalRatio, aiImageAspectRatio, currentAspectRatioString,
  highOptionWidthCm, goodOptionWidthCm, selectedDimensions,
  pickBannerSize, loadSizesAndPricing, onPreviewImageLoaded,
  resetForNewGeneration: resetPricing,
} = useBannerPricing()

// ── Aspect-ratio string for backend (pre-generation: from ratio buttons) ──────
const aspectRatioForBackend = computed(() => {
  const parts = selectedAspectRatio.value.split(':')
  const rW = parseInt(parts[0] ?? '0', 10)
  const rH = parseInt(parts[1] ?? '0', 10)
  if (rW > 0 && rH > 0) {
    const ratio = rW / rH
    if (ratio < 1) {
      const h = 180
      return `${Math.max(1, Math.round(h * ratio))}x${h}`
    } else {
      const h = 150
      return `${Math.round(h * ratio)}x${h}`
    }
  }
  return '360x150'
})

// ── Composable: Banner generation ─────────────────────────────────────────────
const {
  genPhase, currentDesignRequest, designRequestId, requiresAuthHint, generateApiError,
  approveError, approving, regenerating, regenerateError, editExpanded,
  reordering, reorderError, startPolling, cleanup: cleanupGeneration,
  generateBanner: _generateBanner, approve, regenerate: _regenerate,
  reorderCurrentDesign, selectPastDesign: _selectPastDesign,
  returnToWizardIdle: _returnToWizardIdle,
} = useBannerGeneration({
  getTemplateId: () => selectedTemplateId.value,
  getLanguage: () => language.value,
  getPersonName: () => personName.value,
  getPersonAge: () => personAge.value,
  getTextContent: () => textContent.value,
  getThemeDescription: () => themeDescription.value,
  getAspectRatioForBackend: () => aspectRatioForBackend.value,
  getUploadedPhotoBannerDesignId: () => uploadedPhotoBannerDesignId.value,
  getSelectedDimensions: () => selectedDimensions.value,
  onPaywall: (data, action) => {
    paywallData.value = data
    pendingAction.value = action
    paywallOpen.value = true
  },
  onGenerationComplete: () => void loadPastDesigns(),
  loadTilpassPricing: async (bannerDesignId) => {
    await loadTilpassPricing(bannerDesignId)
    step.value = 3
  },
  isManual: () => isManual.value,
})

// ── Composable: Manual mode ───────────────────────────────────────────────────
const {
  manualSubmitting, manualSubmitError, manualDesignRequestId, manualBannerPriceNok, manualDesignPriceNok,
  generateManualPlaceholder: _generateManualPlaceholder,
  saveManualSessionState, restoreManualSessionState, manualGoVidere, resetManual,
} = useManualMode({
  getTemplateId: () => selectedTemplateId.value,
  getLanguage: () => language.value,
  getPersonName: () => personName.value,
  getPersonAge: () => personAge.value,
  getTextContent: () => textContent.value,
  getThemeDescription: () => themeDescription.value,
  getAspectRatioForBackend: () => aspectRatioForBackend.value,
  getUploadedPhotoBannerDesignId: () => uploadedPhotoBannerDesignId.value,
  getSelectedAspectRatio: () => selectedAspectRatio.value,
  getSelectedQuality: () => selectedQuality.value,
  getCustomWidth: () => customWidth.value,
  getCustomHeight: () => customHeight.value,
  getCustomMaterialGsm: () => customMaterialGsm.value,
  getSizes: () => sizes.value,
  getSizesLoaded: () => sizesLoaded.value,
  pickBannerSize,
  getSelectedDimensions: () => selectedDimensions.value,
  setTilpassState: (w, h, size, bannerPrice, eyeletPrice) => {
    tilpassDesignWidthCm.value = w
    tilpassDesignHeightCm.value = h
    tilpassBannerSize.value = size
    tilpassBannerPriceNok.value = bannerPrice
    tilpassEyeletOption.value = 'None'
    tilpassEyeletPriceNok.value = eyeletPrice
    step.value = 3
    genPhase.value = 'tilpass'
    window.scrollTo({ top: 0, behavior: 'smooth' })
  },
  setSelectedTemplateId: (v) => { selectedTemplateId.value = v },
  setLanguage: (v) => { language.value = v },
  setUploadedPhotoBannerDesignId: (v) => { uploadedPhotoBannerDesignId.value = v },
  setPersonName: (v) => { personName.value = v },
  setPersonAge: (v) => { personAge.value = v },
  setTextContent: (v) => { textContent.value = v },
  setThemeDescription: (v) => { themeDescription.value = v },
  setSelectedAspectRatio: (v) => { selectedAspectRatio.value = v },
  setSelectedQuality: (v) => { selectedQuality.value = v as typeof selectedQuality.value },
  setCustomWidth: (v) => { customWidth.value = v },
  setCustomHeight: (v) => { customHeight.value = v },
  setCustomMaterialGsm: (v) => { customMaterialGsm.value = v },
})

// ── Computed helpers ──────────────────────────────────────────────────────────
const selectedTemplate = computed(() =>
  templates.value.find((t) => t.id === selectedTemplateId.value) ?? null,
)
const templateName = computed(() => {
  const t = selectedTemplate.value
  if (!t) return ''
  return language.value === 'en' ? t.nameEn : t.nameNb
})

const step1Valid = computed(() => selectedTemplateId.value !== null)
const step2Valid = computed(() => {
  if (
    personName.value.trim().length === 0 ||
    textContent.value.trim().length === 0 ||
    themeDescription.value.trim().length === 0
  ) return false
  if (isManual.value && uploadedPhotoBannerDesignId.value === null) return false
  if (genPhase.value === 'ready' && selectedQuality.value === 'custom') {
    return (customWidth.value ?? 0) > 0 && (customHeight.value ?? 0) > 0
  }
  return true
})

const effectivePaywallOptions = computed<PaywallOptions>(() => ({
  creditPackSmallPriceNok: paywallData.value?.paywallOptions?.creditPackSmallPriceNok ?? paywallData.value?.paywallOptions?.creditPackPriceNok ?? 29,
  creditPackSmallCount: paywallData.value?.paywallOptions?.creditPackSmallCount ?? paywallData.value?.paywallOptions?.creditPackCount ?? 5,
  creditPackLargePriceNok: paywallData.value?.paywallOptions?.creditPackLargePriceNok ?? 95,
  creditPackLargeCount: paywallData.value?.paywallOptions?.creditPackLargeCount ?? 20,
  creditPackPriceNok: paywallData.value?.paywallOptions?.creditPackSmallPriceNok ?? paywallData.value?.paywallOptions?.creditPackPriceNok ?? 29,
  creditPackCount: paywallData.value?.paywallOptions?.creditPackSmallCount ?? paywallData.value?.paywallOptions?.creditPackCount ?? 5,
  bannerOrderActivationFeeNok: paywallData.value?.paywallOptions?.bannerOrderActivationFeeNok ?? 95,
  bannerOrderCreditBonus: paywallData.value?.paywallOptions?.bannerOrderCreditBonus ?? 20,
  manualDesignerUrl: paywallData.value?.paywallOptions?.manualDesignerUrl ?? '/banner-builder/manual',
  uploadOwnUrl: paywallData.value?.paywallOptions?.uploadOwnUrl ?? '/banner-builder',
}))

const canGenerateForFree = computed<boolean | null>(() => {
  if (!auth.isLoggedIn) return null
  if (hasUsedFreeGeneration.value === null) return null
  return !hasUsedFreeGeneration.value
})
const hasCreditsAvailable = computed<boolean>(() => (creditsRemaining.value ?? 0) > 0)
const isOutOfGenerations = computed<boolean>(() =>
  auth.isLoggedIn &&
  hasUsedFreeGeneration.value === true &&
  !hasCreditsAvailable.value,
)
const generateButtonLabel = computed<string>(() => {
  if (genPhase.value === 'submitting') return 'Sender…'
  if (canGenerateForFree.value === true) return 'Generer banner gratis'
  if (hasCreditsAvailable.value) return `Generer banner (1 kreditt)`
  if (isOutOfGenerations.value) return 'Kjøp kreditter for å generere'
  return 'Generer banner gratis'
})
const generateButtonSubtitle = computed<string>(() => {
  if (canGenerateForFree.value === true)
    return 'Ingen betalingsinformasjon nødvendig for første generering'
  if (hasCreditsAvailable.value)
    return `${creditsRemaining.value} forslag igjen — bruker 1 kreditt`
  if (isOutOfGenerations.value)
    return 'Du har brukt opp den gratis genereringen — kjøp en kredittpakke for å fortsette'
  return 'Ingen betalingsinformasjon nødvendig for første generering'
})

const tilpassEyeletCount = computed(() =>
  countEyelets(tilpassDesignWidthCm.value, tilpassDesignHeightCm.value, tilpassEyeletOption.value),
)
const tilpassEyeletFeeNok = computed(() => tilpassEyeletCount.value * tilpassEyeletPriceNok.value)
const tilpassTotalNok = computed(() =>
  tilpassBannerPriceNok.value
  + tilpassEyeletFeeNok.value
  + (isManual.value ? manualDesignPriceNok.value : 0),
)

// BANNERSH-87: mirror the local creditsRemaining ref into the shared store so
// the navbar credit badge updates the instant a generation succeeds, without
// waiting for a route change to trigger App.vue's refetch.
watch([creditsRemaining, hasUsedFreeGeneration], ([n, used]) => {
  if (n !== null) creditsStore.setBalance(n, used ?? undefined)
})

// BANNERSH-157: keep ?dr= URL query param in sync with the active design request
// so that the URL is bookmarkable.  When a past banner is selected (or a new one
// is generated) the param is written; when the user returns to idle it is removed.
watch(designRequestId, (id) => {
  const currentDr = route.query.dr as string | undefined
  const newDr = id !== null ? String(id) : undefined
  if (currentDr === newDr) return  // already correct, avoid no-op replace
  const query = { ...route.query }
  if (newDr !== undefined) {
    query.dr = newDr
  } else {
    delete query.dr
  }
  void router.replace({ query })
})

// ── Category icons / placeholders ─────────────────────────────────────────────
const categoryIconClass: Record<string, string> = {
  Birthday: 'fa-cake-candles', Confirmation: 'fa-graduation-cap',
  Wedding: 'fa-ring', Anniversary: 'fa-champagne-glasses',
  Christmas: 'fa-tree', NewYear: 'fa-champagne-glasses',
  Baptism: 'fa-dove', Other: 'fa-gift',
}
const categoryBannerTextPlaceholder: Record<string, string> = {
  Birthday: 'f.eks. Gratulerer med dagen', Confirmation: 'f.eks. Gratulerer med konfirmasjonen',
  Wedding: 'f.eks. Gratulerer med bryllupsdagen', Baptism: 'f.eks. Til lykke med dåpsdagen',
  Anniversary: 'f.eks. Gratulerer med jubileet', Christmas: 'f.eks. God jul',
  NewYear: 'f.eks. Godt nytt år', Other: 'f.eks. Velkommen til festen',
}
const textContentPlaceholder = computed(() => {
  const cat = selectedTemplate.value?.category
  return (cat && categoryBannerTextPlaceholder[cat]) ?? 'f.eks. Gratulerer med dagen'
})
const themeDescriptionPlaceholder = computed(() => {
  const cat = selectedTemplate.value?.category
  switch (cat) {
    case 'Birthday':     return 'f.eks. Prinsessetema, rosa og gull'
    case 'Confirmation': return 'f.eks. Elegant, dempede farger'
    case 'Wedding':      return 'f.eks. Romantisk, hvit og gull'
    case 'Baptism':      return 'f.eks. Lyse pastellfarger, duer og blomster'
    case 'Anniversary':  return 'f.eks. Klassisk, gull og sølv'
    case 'Christmas':    return 'f.eks. Tradisjonell jul, rødt og grønt'
    case 'NewYear':      return 'f.eks. Festlig, gull og fyrverkeri'
    case 'Other':        return 'f.eks. Sommerfest, sol og strand'
    default:             return 'f.eks. Tropisk fest, lilla og gull'
  }
})

// ── Template loading ──────────────────────────────────────────────────────────
async function loadTemplates() {
  templatesLoading.value = true
  templatesError.value = null
  try {
    templates.value = await fetchTemplates()
    if (templates.value.length > 0 && selectedTemplateId.value === null) {
      const categoryParam = (route.query.category as string | undefined)?.trim()
      let preselected: BannerTemplateItem | undefined
      if (categoryParam) {
        preselected = templates.value.find(
          (t) => t.category.toLowerCase() === categoryParam.toLowerCase(),
        )
      }
      selectedTemplateId.value = (preselected ?? templates.value[0])?.id ?? null
    }
  } catch (e: unknown) {
    const ex = e as { response?: { data?: { error?: string } }; message?: string }
    templatesError.value = ex.response?.data?.error || ex.message || 'Kunne ikke laste maler.'
  } finally {
    templatesLoading.value = false
  }
}

// ── Credits balance ───────────────────────────────────────────────────────────
async function loadCreditsBalance() {
  if (!auth.isLoggedIn) return
  try {
    const balance = await getAiCreditsBalance()
    creditsRemaining.value = balance.creditsRemaining
    hasUsedFreeGeneration.value = balance.hasUsedFreeGeneration
  } catch { /* Non-critical */ }
}

// ── View-level wrapper functions ──────────────────────────────────────────────

/** Generate with pre-flight paywall check + credits update */
async function generateBanner() {
  if (isOutOfGenerations.value) {
    paywallData.value = paywallData.value ?? {
      reason: 'insufficient_credits',
      creditsRemaining: 0,
      paywallOptions: effectivePaywallOptions.value,
    }
    pendingAction.value = 'generate'
    paywallOpen.value = true
    return
  }
  resetPricing()
  const result = await _generateBanner()
  if (result && result.creditsRemaining !== undefined && auth.isLoggedIn) {
    creditsRemaining.value = result.creditsRemaining
    hasUsedFreeGeneration.value = true
  }
}

/** Regenerate with pricing reset + credits update */
async function regenerate() {
  resetPricing()
  const result = await _regenerate()
  if (result && result.creditsRemaining !== undefined) {
    creditsRemaining.value = result.creditsRemaining
  }
}

/** Manual-mode "Se forhåndsvisning": generate canvas placeholder + enter ready phase */
function generateManualPlaceholder() {
  const result = _generateManualPlaceholder(selectedAspectRatio.value, aspectRatioForBackend.value)
  currentDesignRequest.value = result.detail
  aiImageNaturalRatio.value = result.ratio
  genPhase.value = 'ready'
  window.scrollTo({ top: 0, behavior: 'smooth' })
}

/** Return to idle: composable reset + manual reset + step back */
function returnToWizardIdle() {
  _returnToWizardIdle()
  resetManual()
  step.value = 2
}

/** Select a past design: composable call + restore form fields + step navigation */
async function handleSelectPastDesign(item: DesignRequestListItem) {
  resetPricing()
  const detail = await _selectPastDesign(item)
  if (detail) {
    personName.value = detail.personName
    personAge.value = detail.personAge ?? null
    textContent.value = detail.textContent
    themeDescription.value = detail.themeDescription
    selectedTemplateId.value = detail.bannerTemplateId
    language.value = detail.language === 'en' ? 'en' : 'nb'
    if (detail.aspectRatio === '18:9' || detail.aspectRatio?.startsWith('300x')) {
      selectedQuality.value = 'good'
    } else {
      selectedQuality.value = 'high'
    }
    step.value = 2
  }
}

// ── PaywallModal event handlers ───────────────────────────────────────────────
function onPaywallRetryAction() {
  if (pendingAction.value === 'generate') void generateBanner()
  else void regenerate()
}
function onPaywallCreditsUpdated(remaining: number, usedFree: boolean) {
  creditsRemaining.value = remaining
  hasUsedFreeGeneration.value = usedFree
}
function onPaywallNavigateTo(url: string) { void router.push(url) }
function onPaywallSelectPastDesign(item: DesignRequestListItem) {
  paywallOpen.value = false
  void handleSelectPastDesign(item)
}
function onPaywallGoToCheckout() {
  const id = designRequestId.value
  paywallOpen.value = false
  void router.push(id ? `/checkout?designRequestId=${id}` : '/checkout')
}

// ── Clear age when switching to a non-birthday template ──────────────────────
watch(selectedTemplateId, () => {
  if (selectedTemplate.value?.category !== 'Birthday') personAge.value = null
})

// ── Step navigation ───────────────────────────────────────────────────────────
function goToStep(s: 1 | 2 | 3) {
  if (s === 2 && !step1Valid.value) return
  if (s === 3 && (!step1Valid.value || !step2Valid.value)) return
  step.value = s
}

// ── Tilpass: load pricing + eyelet info ──────────────────────────────────────
async function loadTilpassPricing(bannerDesignId: number) {
  tilpassLoading.value = true
  tilpassError.value = null
  try {
    const design = await getBannerDesign(bannerDesignId)
    const designSizes = await fetchSizes(design.computedWidthCm)
    const pricingSize = designSizes.find(
      (s: BannerSize) => s.isCustomWidth && s.heightCm === design.selectedHeightCm,
    )
    if (!pricingSize || pricingSize.calculatedPrice == null) {
      throw new Error('Pricing not available for this banner.')
    }
    tilpassDesignWidthCm.value = design.computedWidthCm
    tilpassDesignHeightCm.value = design.selectedHeightCm
    tilpassBannerSize.value = pricingSize
    tilpassBannerPriceNok.value = pricingSize.calculatedPrice
    tilpassEyeletOption.value = 'None'
    try {
      tilpassEyeletPriceNok.value = await fetchEyeletPriceNok()
    } catch {
      tilpassEyeletPriceNok.value = 0
    }
  } finally {
    tilpassLoading.value = false
  }
}

// ── Tilpass: add to cart + go to checkout ────────────────────────────────────
function addTilpassToCartAndCheckout() {
  if (isManual.value) { addManualToCartAndCheckout(); return }
  const d = currentDesignRequest.value
  const size = tilpassBannerSize.value
  if (!d?.finalBannerDesignId || !size) return
  cart.addItem({
    bannerSizeId: size.id,
    bannerSizeName: `AI banner ${tilpassDesignWidthCm.value} × ${tilpassDesignHeightCm.value} cm`,
    customWidthCm: tilpassDesignWidthCm.value,
    heightCm: tilpassDesignHeightCm.value,
    quantity: 1,
    unitPriceNok: tilpassBannerPriceNok.value,
    eyeletOption: tilpassEyeletOption.value,
    eyeletFeeNok: tilpassEyeletFeeNok.value,
    designId: d.finalBannerDesignId,
    previewUrl: d.previewUrl ?? undefined,
    notes: `AI banner design #${d.finalBannerDesignId}`,
  })
  void router.push('/checkout')
}

function backFromTilpass() {
  step.value = 2
  genPhase.value = 'ready'
  tilpassError.value = null
}

function addManualToCartAndCheckout() {
  const reqId = manualDesignRequestId.value
  const size = tilpassBannerSize.value
  if (!reqId || !size) return
  const bannerItem: CartItem = {
    bannerSizeId: size.id,
    bannerSizeName: `Manuelt banner ${tilpassDesignWidthCm.value} × ${tilpassDesignHeightCm.value} cm`,
    customWidthCm: size.isCustomWidth ? tilpassDesignWidthCm.value : null,
    heightCm: tilpassDesignHeightCm.value,
    quantity: 1,
    unitPriceNok: manualBannerPriceNok.value,
    eyeletOption: tilpassEyeletOption.value,
    eyeletFeeNok: tilpassEyeletFeeNok.value,
    notes: `Manuelt designet banner — bestilling #${reqId}`,
    designRequestId: reqId,
  }
  cart.addItem(bannerItem)
  const designFeeItem: CartItem = {
    bannerSizeId: null,
    bannerSizeName: 'Designer-tjeneste (manuelt banner)',
    customWidthCm: null,
    heightCm: 0,
    quantity: 1,
    unitPriceNok: manualDesignPriceNok.value,
    eyeletOption: 'None',
    eyeletFeeNok: 0,
    notes: `Designhonorar for bestilling #${reqId}`,
    designRequestId: reqId,
  }
  cart.addItem(designFeeItem)
  void router.push('/checkout')
}

// ── Lifecycle ─────────────────────────────────────────────────────────────────
onMounted(async () => {
  await loadTemplates()
  // BANNERSH-189: credits/paywall logic is AI-only — skip the API call in manual mode.
  if (!isManual.value) {
    await loadCreditsBalance()
  }
  await loadPastDesigns()

  // Load banner sizes for quality option price display
  await loadSizesAndPricing()

  // BANNERSH-189: restore manual-mode form state saved before a login redirect.
  if (isManual.value && auth.isLoggedIn) {
    const restored = restoreManualSessionState()
    if (restored && selectedTemplateId.value !== null && personName.value.trim() !== '') {
      // Drop the user back on step 2 with their inputs intact — they can review
      // and re-click "Generer banner" → "Gå videre" to complete the order.
      step.value = 2
      return
    }
  }

  // BANNERSH-105: when arriving from a front-page category card the matching
  // template has already been pre-selected by loadTemplates(); skip the
  // template-picker step and drop the user straight into "Tilpass".
  const categoryParam = (route.query.category as string | undefined)?.trim()

  // BANNERSH-130: when arriving from "copy" action on an existing design request,
  // pre-fill wizard inputs from that request and skip to step 2.
  const copyFromParam = (route.query.copyFrom as string | undefined)?.trim()
  if (copyFromParam) {
    const copyFromId = parseInt(copyFromParam, 10)
    if (!isNaN(copyFromId) && copyFromId > 0) {
      try {
        const detail = await getDesignRequest(copyFromId)
        selectedTemplateId.value = detail.bannerTemplateId
        language.value = detail.language === 'en' ? 'en' : 'nb'
        personName.value = detail.personName
        personAge.value = detail.personAge ?? null
        textContent.value = detail.textContent
        themeDescription.value = detail.themeDescription
        if (detail.aspectRatio === '18:9' || detail.aspectRatio?.startsWith('300x')) {
          selectedQuality.value = 'good'
        } else {
          selectedQuality.value = 'high'
        }
        step.value = 2
      } catch {
        // Non-critical — just keep defaults and let the user fill in manually.
      }
      return
    }
  }

  // BANNERSH-157: restore a specific design request from the bookmarkable URL
  // ?dr=<id>.  This param is written by the designRequestId watcher whenever a
  // past banner is selected or a new one is generated, so F5 / sharing the URL
  // always restores the same view.  Skip if ?category= is present (explicit
  // intent to start fresh) or if ?copyFrom= already handled things above.
  const drParam = (route.query.dr as string | undefined)?.trim()
  if (drParam && !categoryParam) {
    const drId = parseInt(drParam, 10)
    if (!isNaN(drId) && drId > 0) {
      await handleSelectPastDesign({ id: drId } as DesignRequestListItem)
      return
    }
  }

  // Resume a pending AI design from a previous session.
  // Common case: anonymous user generated a banner, registered/logged in, and was
  // redirected back here.  The design-id was stored in localStorage before they left.
  //
  // BANNERSH-124: skip draft resumption when the user arrived via a front-page
  // category card (?category=…) — that signifies explicit intent to start a new
  // banner, so we should NOT redirect them back to the previous draft.
  //
  // BANNERSH-142: only resume when the URL explicitly opts in via ?resume=1.
  // Plain navigation to /banner-builder/ai (e.g. clicking "Lag ditt eget banner")
  // must always land on a fresh, empty form — auto-redirecting users back to a
  // previously generated banner was confusing.  The anon→login flow keeps the
  // resumption behaviour by appending ?resume=1 to its post-auth redirect URL.
  const resumeParam = (route.query.resume as string | undefined)?.trim()
  const draftIdStr = localStorage.getItem('ai_banner_draft_id')
  if (resumeParam === '1' && draftIdStr && auth.isLoggedIn && !categoryParam) {
    const draftId = parseInt(draftIdStr, 10)
    if (!isNaN(draftId) && draftId > 0) {
      step.value = 2
      designRequestId.value = draftId
      startPolling(draftId)
      return
    }
  }

  if (categoryParam && selectedTemplateId.value !== null) {
    step.value = 2
  }
})

// Sync currentDesignRequest.aspectRatio → currentAspectRatioString so the
// pricing composable can compute correct banner widths even before the image loads.
watch(currentDesignRequest, (d) => {
  currentAspectRatioString.value = d?.aspectRatio ?? null
})

// Manual mode: regenerate the canvas placeholder when the aspect ratio changes
// while the user is on the ready phase (they chose a different ratio in the picker).
watch(selectedAspectRatio, () => {
  if (isManual.value && genPhase.value === 'ready') {
    generateManualPlaceholder()
  }
})

// Start / stop the progress bar whenever the generation phase changes
watch(genPhase, (phase) => {
  if (phase === 'generating') {
    startProgressBar()
  } else {
    stopProgressBar()
  }
})

onBeforeUnmount(() => {
  cleanupGeneration()
  stopProgressBar()
  if (photoPreviewUrl.value) URL.revokeObjectURL(photoPreviewUrl.value)
})
</script>

<template>
  <div style="max-width:1200px;margin:0 auto;padding:2rem 1.5rem 4rem">

    <!-- Header (with credits badge for logged-in users in AI mode) -->
    <header style="margin-bottom:2.5rem;text-align:center;position:relative">
      <h1 class="display" style="font-size:clamp(28px,4vw,44px);color:var(--text);margin-bottom:12px">
        <template v-if="isManual">Manuelt designet feiringsbanner</template>
        <template v-else>AI-generert feiringsbanner</template>
      </h1>
      <p style="font-size:18px;color:var(--muted);max-width:36em;margin:0 auto">
        <template v-if="isManual">
          Beskriv ønsket og legg ved et portrettfoto — vi designer banneret manuelt og sender deg en
          forhåndsvisning innen 2–3 virkedager. Designhonorar
          <strong style="color:var(--text)">{{ formatNok(MANUAL_DESIGN_FEE_NOK) }}</strong>
          + bannerproduksjon.
        </template>
        <template v-else>
          Fortell oss om feiringen — vi lager et unikt banner med kunstig intelligens.
          <strong style="color:var(--text)">Første generering er gratis.</strong>
        </template>
      </p>
      <!-- Credits badge — AI mode only -->
      <div
        v-if="!isManual && auth.isLoggedIn && creditsRemaining !== null"
        style="display:inline-flex;align-items:center;gap:7px;margin-top:14px;background:rgba(255,106,61,.12);border:1px solid rgba(255,106,61,.3);border-radius:99px;padding:5px 14px;font-size:13px;font-weight:700;color:var(--accent)"
      >
        <i class="fa-solid fa-wand-magic-sparkles" style="font-size:11px"></i>
        <template v-if="canGenerateForFree === true">1 gratis generering tilgjengelig</template>
        <template v-else>{{ creditsRemaining }} AI forslag igjen</template>
      </div>
    </header>

    <!-- Soft auth hint (anonymous user after creation) — full-width, above the grid -->
    <!-- AI mode only — manual mode auth-gates at "Gå videre" instead of generation. -->
    <div v-if="!isManual && requiresAuthHint" class="notice-gold" style="margin-bottom:2rem">
      <i class="fa-solid fa-circle-info" style="margin-top:2px;flex-shrink:0"></i>
      <span>
        <strong>Opprett konto for å godkjenne og bestille.</strong>
        Banneret ditt genereres i bakgrunnen — logg inn for å se og godkjenne resultatet.
        <RouterLink :to="`/register?redirect=${encodeURIComponent('/banner-builder/ai?resume=1')}`" style="color:var(--accent);font-weight:600">Registrer deg</RouterLink>
        eller
        <RouterLink :to="`/login?redirect=${encodeURIComponent('/banner-builder/ai?resume=1')}`" style="color:var(--accent);font-weight:600">logg inn</RouterLink>.
      </span>
    </div>

    <!-- Step indicator — full-width, above the two-column grid -->
    <nav class="step-nav" style="margin-bottom:2rem" aria-label="Steg">
      <button
        v-for="(label, idx) in ['Velg mal', 'Tilpass', 'Fullfør']"
        :key="idx"
        type="button"
        class="step-nav-btn"
        :class="{
          'step-active': step === idx + 1,
          'step-done': step > idx + 1,
          'step-future': step < idx + 1,
        }"
        :disabled="idx + 1 > step"
        @click="idx + 1 < step ? (step = (idx + 1) as 1 | 2 | 3) : undefined"
      >
        <span
          class="step-circle"
          :class="{
            'step-circle-active': step === idx + 1,
            'step-circle-done': step > idx + 1,
            'step-circle-future': step < idx + 1,
          }"
        >
          <i v-if="step > idx + 1" class="fa-solid fa-check" style="font-size:11px"></i>
          <span v-else>{{ idx + 1 }}</span>
        </span>
        <span class="step-label">{{ label }}</span>
      </button>
    </nav>

    <!-- ═══════════════════════════════════════════════════════════════════
         TWO-COLUMN LAYOUT: past banners sidebar (left) + wizard (right)
         BANNERSH-145: moved gallery from horizontal strip above wizard to
         a sticky vertical sidebar so it doesn't disrupt the input flow.
    ════════════════════════════════════════════════════════════════════════ -->
    <div :class="auth.isLoggedIn && pastDesigns.length > 0 ? 'wizard-with-sidebar' : ''">

      <!-- Left column: past banners sidebar (extracted component) -->
      <!-- BANNERSH-189: filtered by mode (Ai vs Manual) in loadPastDesigns(). -->
      <PastBannersGallery
        v-if="auth.isLoggedIn && pastDesigns.length > 0"
        :designs="pastDesigns"
        :active-id="designRequestId"
        :is-manual="isManual"
        @select="handleSelectPastDesign"
      />

      <!-- Right column: main wizard content -->
      <div>

    <!-- ═══════════════════════════════════════════════════════════════════
         STEP 1: Choose template + upload photo + language
    ════════════════════════════════════════════════════════════════════════ -->
    <div v-if="step === 1">
      <div v-if="templatesLoading" style="text-align:center;color:var(--muted);padding:3rem 0">
        <i class="fa-solid fa-circle-notch fa-spin" style="font-size:24px;margin-bottom:12px;display:block;color:var(--accent)"></i>
        Laster maler…
      </div>
      <div v-else-if="templatesError" class="error-box" style="justify-content:center;flex-direction:column;text-align:center;padding:2rem">
        <i class="fa-solid fa-circle-exclamation" style="font-size:24px;margin-bottom:8px"></i>
        {{ templatesError }}
        <button style="margin-top:12px;color:var(--accent);background:none;border:none;cursor:pointer;font-weight:600;font-size:14px" @click="loadTemplates">Prøv igjen</button>
      </div>
      <template v-else>
        <!-- Language toggle -->
        <div style="margin-bottom:24px;display:flex;align-items:center;gap:12px">
          <span style="font-size:14px;font-weight:600;color:var(--muted)">Språk:</span>
          <button type="button" class="lang-btn" :class="{ 'lang-btn-active': language === 'nb' }" @click="language = 'nb'">
            🇳🇴 Norsk
          </button>
          <button type="button" class="lang-btn" :class="{ 'lang-btn-active': language === 'en' }" @click="language = 'en'">
            🇬🇧 English
          </button>
        </div>

        <!-- Template grid -->
        <div style="margin-bottom:2rem">
          <h2 class="display" style="font-size:20px;color:var(--text);margin-bottom:16px">Velg feiringsmal</h2>
          <div class="tpl-grid">
            <button
              v-for="t in templates"
              :key="t.id"
              type="button"
              class="tpl-card"
              :class="{ 'tpl-card-sel': selectedTemplateId === t.id }"
              @click="selectedTemplateId = t.id"
            >
              <span class="tpl-ico">
                <i :class="['fa-solid', categoryIconClass[t.category] ?? 'fa-star']"></i>
              </span>
              <span style="font-size:13.5px;font-weight:600;color:var(--text);text-align:center;line-height:1.3">
                {{ language === 'en' ? t.nameEn : t.nameNb }}
              </span>
            </button>
          </div>
        </div>

        <!-- Next -->
        <div style="display:flex;justify-content:flex-end">
          <button type="button" class="btn btn-primary" style="padding:12px 28px;font-size:15px" :disabled="!step1Valid" @click="goToStep(2)">
            Neste: Tilpass <i class="fa-solid fa-arrow-right" style="font-size:12px"></i>
          </button>
        </div>
      </template>
    </div>

    <!-- ═══════════════════════════════════════════════════════════════════
         STEP 2: Personalize
    ════════════════════════════════════════════════════════════════════════ -->
    <div v-else-if="step === 2">
      <!-- BANNERSH-105: selected template summary so the user can see which
           celebration they're customising for (especially when arriving from a
           front-page category card that skipped step 1). -->
      <div v-if="selectedTemplate" class="selected-template-card">
        <div class="selected-template-ico">
          <i :class="['fa-solid', categoryIconClass[selectedTemplate.category] ?? 'fa-star']"></i>
        </div>
        <div style="flex:1;min-width:0">
          <div class="selected-template-eyebrow">Du tilpasser</div>
          <div class="selected-template-name">{{ templateName }}</div>
        </div>
        <button
          type="button"
          class="selected-template-change"
          @click="step = 1"
          aria-label="Bytt feiringsmal"
        >
          <i class="fa-solid fa-pen-to-square" style="font-size:11px"></i>
          Bytt mal
        </button>
      </div>

      <div class="bb-panel" style="display:grid;gap:20px">
        <div>
          <label for="personName" class="field-label">Navn <span style="color:var(--accent)">*</span></label>
          <input id="personName" v-model="personName" type="text" maxlength="200" class="dark-input" placeholder="f.eks. Ole Petter" />
        </div>
        <div v-if="selectedTemplate?.category === 'Birthday'">
          <label for="personAge" class="field-label">Alder <span style="color:var(--faint);font-weight:400">(valgfritt)</span></label>
          <input id="personAge" v-model.number="personAge" type="number" min="0" max="130" class="dark-input" style="width:120px" placeholder="f.eks. 50" />
        </div>
        <div>
          <label for="textContent" class="field-label">Tekst på banneret <span style="color:var(--accent)">*</span></label>
          <textarea id="textContent" v-model="textContent" rows="3" maxlength="500" class="dark-input" style="resize:none" :placeholder="textContentPlaceholder" />
          <p style="margin-top:5px;font-size:13px;color:var(--faint)">{{ textContent.length }} / 500 tegn</p>
        </div>
        <div>
          <label for="themeDescription" class="field-label">Tema / stil <span style="color:var(--accent)">*</span></label>
          <input id="themeDescription" v-model="themeDescription" type="text" maxlength="500" class="dark-input" :placeholder="themeDescriptionPlaceholder" />
        </div>

        <!-- Portrait photo upload (moved here from step 1) -->
        <!-- BANNERSH-189: in manual mode the photo is REQUIRED (designer needs the
             reference); in AI mode it's optional (only the person-centred templates
             use it, and the AI degrades gracefully without it). -->
        <div>
          <div class="field-label" style="margin-bottom:4px">
            Portrettfoto
            <span v-if="isManual" style="color:var(--accent)">*</span>
            <span v-else style="font-size:13px;font-weight:400;color:var(--faint)">(valgfritt)</span>
          </div>
          <p style="font-size:13px;color:var(--muted);margin-bottom:12px">
            <template v-if="isManual">
              Vi trenger et bilde av personen som feires — designeren bruker det som referanse.
            </template>
            <template v-else>
              Last opp et bilde av personen som feires — AI-en vil inkorporere det i banneret.
            </template>
          </p>

          <div v-if="photoPreviewUrl" style="display:flex;align-items:flex-start;gap:18px">
            <img :src="photoPreviewUrl" alt="Opplastet portrettfoto" style="width:100px;height:100px;object-fit:cover;border-radius:12px;border:1px solid var(--line-soft)" />
            <div style="display:flex;flex-direction:column;gap:10px;margin-top:6px">
              <span style="font-size:14px;color:#4ade80;font-weight:600;display:flex;align-items:center;gap:7px">
                <i class="fa-solid fa-circle-check"></i> Foto lastet opp
              </span>
              <button type="button" style="font-size:13.5px;color:var(--accent);font-weight:600;background:none;border:none;cursor:pointer;padding:0;text-align:left" @click="removePhoto">
                <i class="fa-solid fa-trash-can" style="font-size:12px"></i> Fjern foto
              </button>
            </div>
          </div>

          <div v-else>
            <div
              role="button"
              tabindex="0"
              class="upload-zone"
              :class="{ 'upload-zone-drag': photoDragging, 'upload-zone-busy': photoUploading }"
              @click="openPhotoPicker"
              @keydown.enter.prevent="openPhotoPicker"
              @keydown.space.prevent="openPhotoPicker"
              @dragover="onPhotoDragOver"
              @dragleave="onPhotoDragLeave"
              @drop="onPhotoDrop"
            >
              <input ref="photoFileInput" type="file" style="display:none" accept="image/jpeg,image/png,image/webp" @change="onPhotoFileChange" />
              <i class="fa-solid fa-user-circle" style="font-size:36px;color:var(--faint);margin-bottom:10px"></i>
              <p style="font-size:14px;font-weight:600;color:var(--text);margin-bottom:4px">Slipp bilde her, eller klikk for å velge</p>
              <p style="font-size:13px;color:var(--faint)">JPEG, PNG, WEBP – maks 10 MB</p>
              <div v-if="photoUploading" class="upload-overlay">
                <div style="width:66%;max-width:260px">
                  <div style="font-size:14px;font-weight:600;color:var(--text);text-align:center;margin-bottom:10px">Laster opp… {{ photoUploadProgress }}%</div>
                  <div style="width:100%;height:6px;background:var(--line);border-radius:999px;overflow:hidden">
                    <div style="height:100%;background:var(--accent);border-radius:999px;transition:width .2s" :style="{ width: `${photoUploadProgress}%` }" />
                  </div>
                </div>
              </div>
            </div>
            <div v-if="photoUploadError" class="error-box" style="margin-top:10px">
              <i class="fa-solid fa-circle-exclamation"></i> {{ photoUploadError }}
            </div>
          </div>
        </div>

        <!-- BANNERSH-170: Aspect-ratio selection — one row of buttons below image upload -->
        <div>
          <div class="field-label" style="margin-bottom:10px">
            Bildeforhold
            <span style="font-size:11px;font-weight:400;color:var(--faint);text-transform:none;letter-spacing:0;margin-left:4px">— velg formen på banneret</span>
          </div>
          <div class="ratio-row">
            <button
              v-for="opt in ratioOptions"
              :key="opt.value"
              type="button"
              class="ratio-btn"
              :class="{ 'ratio-btn-active': selectedAspectRatio === opt.value }"
              @click="selectedAspectRatio = opt.value"
            >
              <!-- small rectangle icon visualising the aspect ratio -->
              <div class="ratio-icon-wrap">
                <div class="ratio-icon" :style="{ width: opt.iconW + 'px', height: opt.iconH + 'px' }" />
              </div>
              <span class="ratio-label">{{ opt.label }}</span>
              <span class="ratio-sub">{{ opt.sub }}</span>
            </button>
          </div>
        </div>

        <!-- BANNERSH-162: quality / size selection is rendered BELOW the
             generated banner preview (further down in this template) instead of
             here, and is hidden until a banner has been generated. -->
      </div>

      <!-- ── Inline preview + generate area (BANNERSH-146) ─────────────── -->
      <div style="margin-top:20px">

        <!-- Error from generateBanner -->
        <div v-if="generateApiError" class="error-box" style="margin-bottom:12px">
          <i class="fa-solid fa-circle-exclamation"></i> {{ generateApiError }}
        </div>

        <!-- Phase: submitting / generating — spinner inside the frame -->
        <div v-if="genPhase === 'submitting' || genPhase === 'generating'" class="preview-generating">
          <div style="position:relative;width:56px;height:56px">
            <div style="position:absolute;inset:0;border-radius:50%;border:4px solid var(--surface-2)"></div>
            <div style="position:absolute;inset:0;border-radius:50%;border:4px solid transparent;border-top-color:var(--accent);animation:spin 1s linear infinite"></div>
          </div>
          <div style="text-align:center">
            <div class="display" style="font-size:20px;color:var(--text);margin-bottom:4px">
              {{ genPhase === 'submitting' ? 'Sender forespørsel…' : 'Genererer banner…' }}
            </div>
            <p v-if="genPhase === 'generating'" style="font-size:13.5px;color:var(--muted)">
              AI-en jobber med designet ditt. Dette tar vanligvis 20–60 sekunder.
            </p>
          </div>
          <!-- Generation progress bar -->
          <div v-if="genPhase === 'generating'" style="width:100%;max-width:260px">
            <div style="width:100%;height:4px;background:var(--surface-2);border-radius:999px;overflow:hidden">
              <div
                style="height:100%;background:var(--accent);border-radius:999px;transition:width .4s ease"
                :style="{ width: `${genProgress}%` }"
              />
            </div>
          </div>
        </div>

        <!-- Phase: anon_pending — anonymous user after generation -->
        <div v-else-if="genPhase === 'anon_pending'" class="preview-anon">
          <i class="fa-solid fa-circle-check" style="font-size:40px;color:#4ade80;margin-bottom:12px"></i>
          <h3 class="display" style="font-size:20px;color:var(--text);margin-bottom:8px">Banneret genereres!</h3>
          <p style="font-size:14px;color:var(--muted);max-width:28em;text-align:center;margin:0 0 16px">
            Opprett en konto for å se og godkjenne resultatet — og for å bestille det ferdige banneret.
          </p>
          <div style="display:flex;gap:10px;flex-wrap:wrap;justify-content:center">
            <RouterLink :to="`/register?redirect=${encodeURIComponent('/banner-builder/ai?resume=1')}`" class="btn btn-primary" style="padding:10px 20px">
              <i class="fa-solid fa-user-plus"></i> Opprett konto
            </RouterLink>
            <RouterLink :to="`/login?redirect=${encodeURIComponent('/banner-builder/ai?resume=1')}`" class="btn btn-ghost" style="padding:10px 20px">
              Logg inn
            </RouterLink>
          </div>
          <p v-if="designRequestId" style="margin-top:16px;font-size:13px;color:var(--faint)">
            Design-ID: {{ designRequestId }}
          </p>
        </div>

        <!-- Phase: ready — show the generated image (or "Ditt banner" placeholder in manual mode) -->
        <template v-else-if="genPhase === 'ready' && currentDesignRequest">
          <div class="bb-panel" style="padding:0;overflow:hidden;border-radius:0">
            <img
              v-if="currentDesignRequest.previewUrl"
              :src="currentDesignRequest.previewUrl"
              :alt="isManual ? 'Ditt banner — forhåndsvisning' : `AI-generert banner for ${currentDesignRequest.personName}`"
              style="width:100%;height:auto;object-fit:contain;display:block"
              @load="onPreviewImageLoaded"
            />
            <div v-else style="display:flex;align-items:center;justify-content:center;height:180px;color:var(--faint)">
              Forhåndsvisning ikke tilgjengelig
            </div>
          </div>

          <!-- BANNERSH-162: quality / size picker rendered BELOW the preview.
               Widths for the two preset options are derived from the AI image's
               actual aspect ratio (set in onPreviewImageLoaded), and the custom
               option's width/height inputs auto-link via that same ratio. -->
          <div v-if="currentDesignRequest.previewUrl" class="bb-panel" style="margin-top:20px">
            <div class="field-label" style="margin-bottom:12px">Velg kvalitet og størrelse</div>
            <div class="quality-grid">

              <!-- Option 1: Høykvalitet — 150 cm tall, width = 150 × image ratio -->
              <button
                type="button"
                class="quality-btn"
                :class="{ 'quality-btn-active': selectedQuality === 'high', 'quality-btn-disabled': option1State.comingSoon }"
                :disabled="option1State.comingSoon"
                @click="!option1State.comingSoon && (selectedQuality = 'high')"
              >
                <span v-if="option1State.comingSoon" class="coming-soon-pill">Kommer snart</span>
                <div class="quality-btn-title">Høykvalitet</div>
                <div class="quality-btn-sub">3 års fargegaranti</div>
                <div class="quality-btn-dims">
                  <template v-if="aiImageNaturalRatio">ca. {{ highOptionWidthCm }} × 150 cm</template>
                  <i v-else class="fa-solid fa-circle-notch fa-spin" style="font-size:10px;color:var(--faint)"></i>
                </div>
                <div class="quality-btn-price">
                  <template v-if="option1State.loading || !aiImageNaturalRatio">
                    <i class="fa-solid fa-circle-notch fa-spin" style="font-size:11px"></i>
                  </template>
                  <template v-else-if="option1State.price !== null">
                    {{ formatNok(option1State.price) }}
                  </template>
                  <template v-else>–</template>
                </div>
              </button>

              <!-- Option 2: God kvalitet — 180 cm tall, width = 180 × image ratio -->
              <button
                type="button"
                class="quality-btn"
                :class="{ 'quality-btn-active': selectedQuality === 'good', 'quality-btn-disabled': option2State.comingSoon }"
                :disabled="option2State.comingSoon"
                @click="!option2State.comingSoon && (selectedQuality = 'good')"
              >
                <span v-if="option2State.comingSoon" class="coming-soon-pill">Kommer snart</span>
                <div class="quality-btn-title">God kvalitet</div>
                <div class="quality-btn-sub">3 måneders fargegaranti</div>
                <div class="quality-btn-dims">
                  <template v-if="aiImageNaturalRatio">ca. {{ goodOptionWidthCm }} × 180 cm</template>
                  <i v-else class="fa-solid fa-circle-notch fa-spin" style="font-size:10px;color:var(--faint)"></i>
                </div>
                <div class="quality-btn-price">
                  <template v-if="option2State.loading || !aiImageNaturalRatio">
                    <i class="fa-solid fa-circle-notch fa-spin" style="font-size:11px"></i>
                  </template>
                  <template v-else-if="option2State.price !== null">
                    {{ formatNok(option2State.price) }}
                  </template>
                  <template v-else>–</template>
                </div>
              </button>

              <!-- Option 3: Custom -->
              <button
                type="button"
                class="quality-btn"
                :class="{ 'quality-btn-active': selectedQuality === 'custom' }"
                @click="selectedQuality = 'custom'"
              >
                <span v-if="customState.comingSoon" class="coming-soon-pill">Kommer snart</span>
                <div class="quality-btn-title">Egendefinert</div>
                <div class="quality-btn-sub">Velg kvalitet og størrelse</div>
                <div class="quality-btn-dims">skriv inn mål</div>
                <div class="quality-btn-price" style="color:var(--faint);font-size:13px">
                  <template v-if="customState.loading">
                    <i class="fa-solid fa-circle-notch fa-spin" style="font-size:11px"></i>
                  </template>
                  <template v-else-if="customState.price !== null">
                    {{ formatNok(customState.price) }}
                  </template>
                  <template v-else>–</template>
                </div>
              </button>

            </div>

            <!-- Custom option inline form (width ↔ height linked via image ratio) -->
            <div v-if="selectedQuality === 'custom'" class="custom-size-form">
              <div style="display:flex;gap:12px;flex-wrap:wrap;align-items:flex-end">
                <div>
                  <label class="field-label" style="margin-bottom:6px">Bredde (cm)</label>
                  <input
                    v-model.number="customWidth"
                    type="number"
                    min="50"
                    max="2000"
                    class="dark-input"
                    style="width:110px"
                    placeholder="f.eks. 300"
                  />
                </div>
                <div>
                  <label class="field-label" style="margin-bottom:6px">Høyde (cm)</label>
                  <input
                    v-model.number="customHeight"
                    type="number"
                    min="50"
                    max="500"
                    class="dark-input"
                    style="width:110px"
                    placeholder="f.eks. 150"
                  />
                </div>
                <div>
                  <label class="field-label" style="margin-bottom:6px">Materialkvalitet</label>
                  <div style="display:flex;gap:8px">
                    <button
                      type="button"
                      class="mat-btn"
                      :class="{ 'mat-btn-active': customMaterialGsm === 400 }"
                      @click="customMaterialGsm = 400"
                    >400g</button>
                    <button
                      type="button"
                      class="mat-btn"
                      :class="{ 'mat-btn-active': customMaterialGsm === 680 }"
                      @click="customMaterialGsm = 680"
                    >680g</button>
                  </div>
                </div>
              </div>
              <p v-if="aiImageAspectRatio" style="margin-top:8px;font-size:12.5px;color:var(--faint)">
                <i class="fa-solid fa-link"></i>
                Bredde og høyde er låst til bildets forhold — endrer du den ene oppdateres den andre.
              </p>
              <div v-if="customState.comingSoon" style="margin-top:8px;font-size:13px;color:var(--gold)">
                <i class="fa-solid fa-clock"></i> Denne kombinasjonen er ikke tilgjengelig ennå.
              </div>
            </div>
          </div>
        </template>

        <!-- Phase: error -->
        <div v-else-if="genPhase === 'error'" class="preview-generating preview-error-frame">
          <i class="fa-solid fa-triangle-exclamation" style="font-size:36px;color:var(--accent);margin-bottom:8px"></i>
          <div class="display" style="font-size:18px;color:var(--text);margin-bottom:6px">Noe gikk galt</div>
          <p style="font-size:13.5px;color:var(--muted);text-align:center;max-width:26em">
            {{ currentDesignRequest?.lastError ?? 'AI-genereringen feilet. Prøv igjen.' }}
          </p>
        </div>

        <!-- Phase: idle — placeholder frame -->
        <div v-else class="preview-placeholder">
          <i class="fa-solid fa-wand-magic-sparkles" style="font-size:30px;color:var(--accent);opacity:.45;margin-bottom:10px"></i>
          <div class="display" style="font-size:20px;color:var(--muted)">{{ templateName || 'AI Banner' }}</div>
          <p style="font-size:13px;color:var(--faint);margin-top:6px">Banneret ditt vil vises her</p>
        </div>

        <!-- Action buttons + credits (hidden while generating or anon_pending) -->
        <div v-if="genPhase !== 'submitting' && genPhase !== 'generating' && genPhase !== 'anon_pending'" style="margin-top:16px;display:grid;gap:12px">

          <!-- Error rows -->
          <div v-if="approveError" class="error-box">
            <i class="fa-solid fa-circle-exclamation"></i> {{ approveError }}
          </div>
          <div v-if="regenerateError" class="error-box">
            <i class="fa-solid fa-circle-exclamation"></i> {{ regenerateError }}
          </div>
          <div v-if="reorderError" class="error-box">
            <i class="fa-solid fa-circle-exclamation"></i> {{ reorderError }}
          </div>

          <!-- "Gå videre" — AwaitingApproval: approve → tilpass (step 3) -->
          <!-- BANNERSH-189: manual mode wires this button to manualGoVidere() which
               creates the DesignRequest via /design-requests/manual instead of the
               AI-only approve endpoint. -->
          <button
            v-if="genPhase === 'ready' && currentDesignRequest?.status === 'AwaitingApproval'"
            type="button"
            class="btn"
            style="width:100%;justify-content:center;padding:14px;font-size:16px;border-radius:12px;background:#3a9d7e;color:#fff"
            :disabled="isManual ? manualSubmitting : approving"
            @click="isManual ? manualGoVidere() : approve()"
          >
            <i v-if="isManual ? manualSubmitting : approving" class="fa-solid fa-circle-notch fa-spin"></i>
            <i v-else class="fa-solid fa-arrow-right"></i>
            <template v-if="isManual">{{ manualSubmitting ? 'Behandler…' : 'Gå videre' }}</template>
            <template v-else>{{ approving ? 'Behandler…' : 'Gå videre' }}</template>
          </button>
          <!-- Manual mode error surface (mirrors approveError below). -->
          <div v-if="isManual && manualSubmitError" class="error-box">
            <i class="fa-solid fa-circle-exclamation"></i> {{ manualSubmitError }}
          </div>

          <!-- "Bestill" / "Bestill på nytt" — Approved / Final (AI only) -->
          <!-- Approved = approved but never ordered (first purchase).
               Final    = already ordered at least once (re-order). -->
          <button
            v-if="!isManual && genPhase === 'ready' && currentDesignRequest?.finalBannerDesignId && (currentDesignRequest?.status === 'Approved' || currentDesignRequest?.status === 'Final')"
            type="button"
            class="btn"
            style="width:100%;justify-content:center;padding:14px;font-size:16px;border-radius:12px;background:#3a9d7e;color:#fff"
            :disabled="reordering"
            @click="reorderCurrentDesign"
          >
            <i v-if="reordering" class="fa-solid fa-circle-notch fa-spin"></i>
            <i v-else class="fa-solid fa-cart-shopping"></i>
            {{ reordering ? 'Legger i handlekurv…' : currentDesignRequest?.status === 'Final' ? 'Bestill på nytt' : 'Bestill' }}
          </button>

          <!-- Generate (idle) / Regenerate (ready/error) button -->
          <!-- BANNERSH-189: manual mode generates a "Ditt banner" placeholder client-side
               (no API call, no credit), and the "ready" phase has NO regenerate option
               — only "Gå videre" → tilpass. So the button is hidden in manual mode once
               we're on the ready phase. -->
          <button
            v-if="!(isManual && genPhase === 'ready')"
            type="button"
            class="btn btn-primary"
            style="width:100%;justify-content:center;padding:14px;font-size:16px;border-radius:12px"
            :disabled="!step2Valid"
            @click="isManual
              ? (genPhase === 'ready' ? null : generateManualPlaceholder())
              : (genPhase === 'ready' ? regenerate() : generateBanner())"
          >
            <i v-if="genPhase === 'error'" class="fa-solid fa-rotate"></i>
            <i v-else-if="!isManual && isOutOfGenerations && genPhase !== 'ready'" class="fa-solid fa-bag-shopping"></i>
            <i v-else-if="isManual" class="fa-solid fa-image"></i>
            <i v-else class="fa-solid fa-wand-magic-sparkles"></i>
            <template v-if="isManual">
              <template v-if="genPhase === 'error'">Prøv igjen</template>
              <template v-else>Se forhåndsvisning</template>
            </template>
            <template v-else-if="genPhase === 'ready'">
              <template v-if="canGenerateForFree === true">Generer ny versjon (gratis)</template>
              <template v-else-if="hasCreditsAvailable">Generer ny versjon (1 kreditt)</template>
              <template v-else>Generer ny versjon</template>
            </template>
            <template v-else-if="genPhase === 'idle'">{{ generateButtonLabel }}</template>
            <template v-else>Prøv igjen</template>
          </button>

          <!-- Credits / hint text — AI mode shows credits state, manual mode shows the workflow hint. -->
          <p style="font-size:13px;color:var(--faint);text-align:center;margin:0">
            <template v-if="isManual">
              <i class="fa-solid fa-palette" style="color:var(--accent);margin-right:5px"></i>
              Designeren vår lager forhåndsvisningen og sender den til godkjenning innen 2–3 virkedager.
            </template>
            <template v-else-if="canGenerateForFree === true">
              <i class="fa-solid fa-gift" style="color:var(--accent);margin-right:5px"></i>
              Du har 1 gratis AI bilde igjen
            </template>
            <template v-else-if="auth.isLoggedIn && creditsRemaining !== null && creditsRemaining > 0">
              <i class="fa-solid fa-wand-magic-sparkles" style="color:var(--accent);margin-right:5px"></i>
              Du har {{ creditsRemaining }} AI kreditter igjen
            </template>
            <template v-else-if="auth.isLoggedIn && isOutOfGenerations">
              <i class="fa-solid fa-circle-exclamation" style="color:var(--accent);margin-right:5px"></i>
              Ingen genereringer igjen —
              <button type="button" style="color:var(--accent);font-weight:600;background:none;border:none;cursor:pointer;padding:0;font-family:var(--font-ui);font-size:13px" @click="pendingAction = 'generate'; paywallOpen = true">kjøp kreditter</button>
            </template>
            <template v-else-if="!auth.isLoggedIn">
              <i class="fa-solid fa-shield-halved" style="margin-right:5px"></i>
              Første generering er gratis — ingen betalingsinformasjon nødvendig
            </template>
          </p>
        </div>
      </div>

      <!-- Navigation: back only (generation is now inline) -->
      <div style="margin-top:24px">
        <button type="button" class="btn btn-ghost" @click="step = 1">
          <i class="fa-solid fa-arrow-left" style="font-size:12px"></i> Tilbake
        </button>
      </div>
    </div>

    <!-- ═══════════════════════════════════════════════════════════════════
         STEP 3: Fullfør — eyelet picker + add to cart (BANNERSH-146)
         Only reached via approve() which always sets genPhase = 'tilpass'.
    ════════════════════════════════════════════════════════════════════════ -->
    <div v-else-if="step === 3">

      <!-- ── Phase: idle (summary + generate button) ────────────────────── -->
      <div v-if="genPhase === 'idle'" style="display:grid;grid-template-columns:1.2fr .8fr;gap:24px" class="pay-grid">
        <!-- Left: Generate -->
        <div style="display:grid;gap:20px">
          <div v-if="generateApiError" class="error-box">
            <i class="fa-solid fa-circle-exclamation"></i> {{ generateApiError }}
          </div>

          <div class="bb-panel" style="display:flex;flex-direction:column;gap:14px">
            <h2 class="display" style="font-size:18px;color:var(--text);display:flex;align-items:center;gap:10px">
              <i class="fa-solid fa-wand-magic-sparkles" style="color:var(--accent)"></i>
              Klar til å generere
            </h2>
            <p style="font-size:14px;color:var(--muted)">
              <template v-if="canGenerateForFree === true">
                AI-en lager et unikt banner basert på informasjonen din. Første generering er <strong style="color:var(--text)">gratis</strong>.
              </template>
              <template v-else-if="hasCreditsAvailable">
                AI-en lager et unikt banner basert på informasjonen din. Bruker <strong style="color:var(--text)">1 av {{ creditsRemaining }} kreditter</strong>.
              </template>
              <template v-else-if="isOutOfGenerations">
                Du har brukt opp den gratis genereringen. Kjøp en kredittpakke for å lage flere banner.
              </template>
              <template v-else>
                AI-en lager et unikt banner basert på informasjonen din. Første generering er <strong style="color:var(--text)">gratis</strong>.
              </template>
            </p>
            <button
              type="button"
              class="btn btn-primary"
              style="width:100%;justify-content:center;padding:15px;font-size:16px;border-radius:13px"
              @click="generateBanner"
            >
              <i v-if="isOutOfGenerations" class="fa-solid fa-bag-shopping"></i>
              <i v-else class="fa-solid fa-wand-magic-sparkles"></i>
              {{ generateButtonLabel }}
            </button>
            <p style="font-size:13px;color:var(--faint);text-align:center;display:flex;align-items:center;justify-content:center;gap:6px">
              <i class="fa-solid fa-shield-halved"></i>
              {{ generateButtonSubtitle }}
            </p>
          </div>
        </div>

        <!-- Right: Summary -->
        <aside>
          <div class="bb-panel" style="position:sticky;top:20px;display:grid;gap:16px">
            <h2 class="display" style="font-size:17px;color:var(--text)">Oppsummering</h2>
            <dl style="display:grid;gap:12px">
              <div>
                <dt class="field-label" style="margin-bottom:3px">Mal</dt>
                <dd style="color:var(--text);font-weight:600;display:flex;align-items:center;gap:8px">
                  <i :class="['fa-solid', categoryIconClass[selectedTemplate?.category ?? ''] ?? 'fa-star']" style="color:var(--accent)"></i>
                  {{ templateName }}
                </dd>
              </div>
              <div>
                <dt class="field-label" style="margin-bottom:3px">Navn</dt>
                <dd style="color:var(--text)">{{ personName }}<span v-if="personAge">, {{ personAge }} år</span></dd>
              </div>
              <div>
                <dt class="field-label" style="margin-bottom:3px">Bannertekst</dt>
                <dd style="color:var(--muted);font-style:italic">{{ textContent }}</dd>
              </div>
              <div>
                <dt class="field-label" style="margin-bottom:3px">Tema</dt>
                <dd style="color:var(--text)">{{ themeDescription }}</dd>
              </div>
              <div>
                <dt class="field-label" style="margin-bottom:3px">Størrelse</dt>
                <dd style="color:var(--text)">
                  <span v-if="selectedQuality === 'high'">Høykvalitet — ca. {{ highOptionWidthCm }} × 150 cm</span>
                  <span v-else-if="selectedQuality === 'good'">God kvalitet — ca. {{ goodOptionWidthCm }} × 180 cm</span>
                  <span v-else>Egendefinert — {{ customWidth ?? '?' }} × {{ customHeight ?? '?' }} cm</span>
                </dd>
              </div>
              <div v-if="uploadedPhotoBannerDesignId">
                <dt class="field-label" style="margin-bottom:3px">Portrettfoto</dt>
                <dd style="color:var(--text);display:flex;align-items:center;gap:8px">
                  <img v-if="photoPreviewUrl" :src="photoPreviewUrl" style="width:38px;height:38px;object-fit:cover;border-radius:8px;border:1px solid var(--line-soft)" alt="Portrettfoto" />
                  <span><i class="fa-solid fa-circle-check" style="color:#4ade80"></i> Lastet opp</span>
                </dd>
              </div>
              <div>
                <dt class="field-label" style="margin-bottom:3px">Språk</dt>
                <dd style="color:var(--text)">{{ language === 'nb' ? '🇳🇴 Norsk' : '🇬🇧 English' }}</dd>
              </div>
            </dl>
          </div>
        </aside>
      </div>

      <!-- ── Phase: submitting ──────────────────────────────────────────── -->
      <div v-else-if="genPhase === 'submitting'" style="text-align:center;padding:4rem 0">
        <i class="fa-solid fa-circle-notch fa-spin" style="font-size:36px;color:var(--accent);margin-bottom:18px;display:block"></i>
        <p style="color:var(--muted);font-size:16px">Sender forespørsel…</p>
      </div>

      <!-- ── Phase: generating (polling) ────────────────────────────────── -->
      <div v-else-if="genPhase === 'generating'" style="text-align:center;padding:5rem 0">
        <div style="display:inline-flex;flex-direction:column;align-items:center;gap:24px">
          <div style="position:relative;width:72px;height:72px">
            <div style="position:absolute;inset:0;border-radius:50%;border:4px solid var(--surface-2)"></div>
            <div style="position:absolute;inset:0;border-radius:50%;border:4px solid transparent;border-top-color:var(--accent);animation:spin 1s linear infinite"></div>
          </div>
          <div>
            <h2 class="display" style="font-size:26px;color:var(--text);margin-bottom:8px">Genererer banner…</h2>
            <p style="color:var(--muted);max-width:28em">AI-en jobber med designet ditt. Dette tar vanligvis 20–60 sekunder.</p>
          </div>
          <div style="display:grid;gap:10px;width:240px;text-align:left">
            <div style="display:flex;align-items:center;gap:10px;font-size:14px;color:var(--muted)">
              <span style="width:18px;height:18px;border-radius:50%;background:var(--accent);display:grid;place-items:center;flex-shrink:0">
                <i class="fa-solid fa-check" style="font-size:9px;color:var(--accent-ink)"></i>
              </span>
              Forespørsel mottatt
            </div>
            <div style="display:flex;align-items:center;gap:10px;font-size:14px;color:var(--text)">
              <span style="width:18px;height:18px;border-radius:50%;border:2px solid var(--accent);background:rgba(255,106,61,.15);animation:pulse 1.4s ease-in-out infinite;flex-shrink:0"></span>
              Lager AI-design…
            </div>
            <div style="display:flex;align-items:center;gap:10px;font-size:14px;color:var(--faint)">
              <span style="width:18px;height:18px;border-radius:50%;background:var(--surface-2);border:1px solid var(--line);flex-shrink:0"></span>
              Klart til godkjenning
            </div>
          </div>
          <!-- Generation progress bar -->
          <div style="width:240px">
            <div style="width:100%;height:5px;background:var(--surface-2);border-radius:999px;overflow:hidden">
              <div
                style="height:100%;background:var(--accent);border-radius:999px;transition:width .4s ease"
                :style="{ width: `${genProgress}%` }"
              />
            </div>
          </div>
        </div>
      </div>

      <!-- ── Phase: anon_pending (anonymous user, can't poll) ───────────── -->
      <div v-else-if="genPhase === 'anon_pending'" style="text-align:center;padding:4rem 1rem">
        <i class="fa-solid fa-circle-check" style="font-size:52px;color:#4ade80;margin-bottom:18px;display:block"></i>
        <h2 class="display" style="font-size:26px;color:var(--text);margin-bottom:10px">Banneret genereres!</h2>
        <p style="color:var(--muted);max-width:34em;margin:0 auto 24px">
          AI-en jobber med designet ditt. Opprett en konto for å se og godkjenne resultatet — og for å bestille det ferdige banneret.
        </p>
        <div style="display:flex;gap:12px;justify-content:center;flex-wrap:wrap">
          <RouterLink :to="`/register?redirect=${encodeURIComponent('/banner-builder/ai?resume=1')}`" class="btn btn-primary" style="padding:12px 24px">
            <i class="fa-solid fa-user-plus"></i> Opprett konto
          </RouterLink>
          <RouterLink :to="`/login?redirect=${encodeURIComponent('/banner-builder/ai?resume=1')}`" class="btn btn-ghost" style="padding:12px 24px">
            Logg inn
          </RouterLink>
        </div>
        <p v-if="designRequestId" style="margin-top:20px;font-size:13px;color:var(--faint)">
          Design-ID: {{ designRequestId }} — lagret lokalt, tilgjengelig etter innlogging.
        </p>
      </div>

      <!-- ── Phase: ready (preview + edit-and-regenerate) ──────────────── -->
      <div v-else-if="genPhase === 'ready' && currentDesignRequest" style="display:grid;gap:24px">
        <div style="text-align:center">
          <h2 class="display" style="font-size:28px;color:var(--text);margin-bottom:8px">
            <i class="fa-solid fa-party-horn" style="color:var(--accent);margin-right:8px"></i>
            Banneret ditt er klart!
          </h2>
          <p style="color:var(--muted)">Se over designet og godkjenn, eller juster og generer en ny versjon.</p>
        </div>

        <!-- Preview -->
        <div class="bb-panel" style="padding:0;overflow:hidden;border-radius:0">
          <img
            v-if="currentDesignRequest.previewUrl"
            :src="currentDesignRequest.previewUrl"
            :alt="isManual ? 'Ditt banner — forhåndsvisning' : `AI-generert banner for ${currentDesignRequest.personName}`"
            style="width:100%;height:auto;object-fit:contain;display:block"
            @load="onPreviewImageLoaded"
          />
          <div v-else style="display:flex;align-items:center;justify-content:center;height:240px;color:var(--faint)">
            Forhåndsvisning ikke tilgjengelig
          </div>
        </div>

        <!-- Approved / Final status + reorder / copy actions (BANNERSH-130) -->
        <div
          v-if="currentDesignRequest.status === 'Approved' || currentDesignRequest.status === 'Final'"
          style="display:grid;gap:14px"
        >
          <div style="display:flex;align-items:center;gap:10px;background:rgba(74,222,128,.1);border:1px solid rgba(74,222,128,.25);border-radius:12px;padding:14px 18px;color:#4ade80;font-size:14px">
            <i class="fa-solid fa-circle-check"></i>
            Banneret er godkjent og sendt til produksjon.
          </div>
          <!-- Reorder + copy actions -->
          <div style="display:flex;gap:14px;flex-wrap:wrap">
            <button
              v-if="currentDesignRequest.finalBannerDesignId"
              type="button"
              class="btn"
              style="flex:1;justify-content:center;padding:14px;font-size:15px;border-radius:12px;background:#3a9d7e;color:#fff;min-width:200px"
              :disabled="reordering"
              @click="reorderCurrentDesign"
            >
              <i v-if="reordering" class="fa-solid fa-circle-notch fa-spin"></i>
              <i v-else class="fa-solid fa-cart-shopping"></i>
              {{ reordering ? 'Legger i handlekurv…' : currentDesignRequest.status === 'Final' ? 'Bestill på nytt' : 'Bestill' }}
            </button>
            <button
              type="button"
              class="btn btn-ghost"
              style="flex:1;justify-content:center;padding:14px;font-size:15px;border-radius:12px;min-width:200px"
              @click="returnToWizardIdle"
            >
              <i class="fa-solid fa-copy"></i>
              Kopier og lag ny versjon
            </button>
          </div>
          <div v-if="reorderError" class="error-box">
            <i class="fa-solid fa-circle-exclamation"></i> {{ reorderError }}
          </div>
        </div>

        <!-- Action buttons (AwaitingApproval) -->
        <!-- BANNERSH-133: button flow re-ordered.
             Row 1: Back (left) + Generer ny versjon (right) — both secondary actions.
             Row 2: Green "Godkjenn og tilpass" (full width) — the primary call-to-action
             that proceeds to the eyelet (malje) picker step. -->
        <div v-if="currentDesignRequest.status === 'AwaitingApproval'" style="display:grid;gap:14px">
          <div style="display:flex;gap:14px;flex-wrap:wrap">
            <button
              type="button"
              class="btn btn-ghost"
              style="flex:1;justify-content:center;padding:14px;font-size:15px;border-radius:12px;min-width:220px"
              @click="returnToWizardIdle"
            >
              <i class="fa-solid fa-arrow-left"></i>
              Tilbake
            </button>
            <button
              type="button"
              class="btn btn-ghost"
              style="flex:1;justify-content:center;padding:14px;font-size:15px;border-radius:12px;min-width:220px"
              :disabled="regenerating"
              @click="regenerate"
            >
              <i v-if="regenerating" class="fa-solid fa-circle-notch fa-spin"></i>
              <i v-else class="fa-solid fa-rotate"></i>
              <template v-if="canGenerateForFree === true">Generer ny versjon (gratis)</template>
              <template v-else-if="hasCreditsAvailable">Generer ny versjon (1 kreditt)</template>
              <template v-else>Generer ny versjon (krever kreditter)</template>
            </button>
          </div>
          <button
            type="button"
            class="btn"
            style="width:100%;justify-content:center;padding:14px;font-size:16px;border-radius:12px;background:#3a9d7e;color:#fff"
            :disabled="approving"
            @click="approve"
          >
            <i v-if="approving" class="fa-solid fa-circle-notch fa-spin"></i>
            <i v-else class="fa-solid fa-circle-check"></i>
            Godkjenn og tilpass
          </button>
        </div>

        <!-- Credits badge inline -->
        <div
          v-if="auth.isLoggedIn && creditsRemaining !== null && currentDesignRequest.status === 'AwaitingApproval'"
          style="font-size:13px;color:var(--faint);text-align:center"
        >
          <i class="fa-solid fa-wand-magic-sparkles" style="color:var(--accent);margin-right:5px"></i>
          <template v-if="canGenerateForFree === true">1 gratis generering tilgjengelig</template>
          <template v-else>{{ creditsRemaining }} AI forslag igjen</template>
        </div>

        <!-- Errors -->
        <div v-if="approveError" class="error-box">
          <i class="fa-solid fa-circle-exclamation"></i> {{ approveError }}
        </div>
        <div v-if="regenerateError" class="error-box">
          <i class="fa-solid fa-circle-exclamation"></i> {{ regenerateError }}
        </div>

        <!-- ── Edit-and-regenerate panel ─────────────────────────────────── -->
        <div v-if="currentDesignRequest.status === 'AwaitingApproval'" class="bb-panel" style="display:grid;gap:0">
          <button
            type="button"
            style="display:flex;align-items:center;gap:10px;background:none;border:none;cursor:pointer;padding:4px 0;font-family:var(--font-ui);font-size:14.5px;font-weight:700;color:var(--muted);text-align:left"
            @click="editExpanded = !editExpanded"
          >
            <i :class="['fa-solid', editExpanded ? 'fa-chevron-down' : 'fa-chevron-right']" style="font-size:12px;color:var(--faint)"></i>
            <i class="fa-solid fa-pen-to-square" style="color:var(--accent);font-size:13px"></i>
            Rediger og generer ny versjon
          </button>

          <div v-if="editExpanded" style="display:grid;gap:16px;margin-top:18px;padding-top:18px;border-top:1px solid var(--line-soft)">
            <p style="font-size:13px;color:var(--faint)">
              Endre feltene under og klikk <em>Generer ny versjon</em> — tekst og tema oppdateres på nytt design.
            </p>

            <!-- Template selection inline -->
            <div>
              <div class="field-label" style="margin-bottom:10px">Feiringsmal</div>
              <div class="tpl-grid tpl-grid-sm">
                <button
                  v-for="t in templates"
                  :key="t.id"
                  type="button"
                  class="tpl-card"
                  :class="{ 'tpl-card-sel': selectedTemplateId === t.id }"
                  @click="selectedTemplateId = t.id"
                >
                  <span class="tpl-ico" style="width:34px;height:34px;font-size:15px">
                    <i :class="['fa-solid', categoryIconClass[t.category] ?? 'fa-star']"></i>
                  </span>
                  <span style="font-size:13px;font-weight:600;color:var(--text);text-align:center;line-height:1.3">
                    {{ language === 'en' ? t.nameEn : t.nameNb }}
                  </span>
                </button>
              </div>
            </div>

            <!-- Person name -->
            <div>
              <label for="editPersonName" class="field-label">Navn</label>
              <input id="editPersonName" v-model="personName" type="text" maxlength="200" class="dark-input" />
            </div>

            <!-- Banner text -->
            <div>
              <label for="editTextContent" class="field-label">Tekst på banneret <span style="color:var(--accent)">*</span></label>
              <textarea id="editTextContent" v-model="textContent" rows="3" maxlength="500" class="dark-input" style="resize:none" />
              <p style="margin-top:4px;font-size:13px;color:var(--faint)">{{ textContent.length }} / 500 tegn</p>
            </div>

            <!-- Theme -->
            <div>
              <label for="editThemeDescription" class="field-label">Tema / stil <span style="color:var(--accent)">*</span></label>
              <input id="editThemeDescription" v-model="themeDescription" type="text" maxlength="500" class="dark-input" />
            </div>

            <!-- Photo (re-upload) -->
            <div>
              <div class="field-label" style="margin-bottom:8px">Portrettfoto</div>
              <div v-if="photoPreviewUrl" style="display:flex;align-items:center;gap:12px">
                <img :src="photoPreviewUrl" style="width:64px;height:64px;object-fit:cover;border-radius:10px;border:1px solid var(--line-soft)" alt="Portrettfoto" />
                <button type="button" style="font-size:13px;color:var(--accent);background:none;border:none;cursor:pointer;font-weight:600;padding:0" @click="removePhoto">
                  <i class="fa-solid fa-trash-can"></i> Fjern
                </button>
              </div>
              <div v-else>
                <div
                  role="button"
                  tabindex="0"
                  class="upload-zone"
                  style="padding:1.5rem"
                  :class="{ 'upload-zone-drag': photoDragging, 'upload-zone-busy': photoUploading }"
                  @click="openPhotoPicker"
                  @keydown.enter.prevent="openPhotoPicker"
                  @dragover="onPhotoDragOver"
                  @dragleave="onPhotoDragLeave"
                  @drop="onPhotoDrop"
                >
                  <input ref="photoFileInput" type="file" style="display:none" accept="image/jpeg,image/png,image/webp" @change="onPhotoFileChange" />
                  <i class="fa-solid fa-user-circle" style="font-size:24px;color:var(--faint);margin-bottom:8px"></i>
                  <p style="font-size:13px;color:var(--text)">Klikk for å laste opp portrettfoto</p>
                  <div v-if="photoUploading" class="upload-overlay">
                    <span style="font-size:13px;color:var(--text)">{{ photoUploadProgress }}%</span>
                  </div>
                </div>
                <div v-if="photoUploadError" class="error-box" style="margin-top:8px">
                  <i class="fa-solid fa-circle-exclamation"></i> {{ photoUploadError }}
                </div>
              </div>
            </div>

            <!-- Regenerate CTA -->
            <button
              type="button"
              class="btn btn-primary"
              style="width:100%;justify-content:center;padding:13px;font-size:15px;border-radius:12px"
              :disabled="regenerating || !step2Valid"
              @click="regenerate"
            >
              <i v-if="regenerating" class="fa-solid fa-circle-notch fa-spin"></i>
              <i v-else class="fa-solid fa-rotate"></i>
              {{ regenerating ? 'Genererer…' : 'Generer ny versjon' }}
            </button>
          </div>
        </div>
      </div>

      <!-- ── Phase: tilpass (eyelet picker + add-to-cart) ───────────────── -->
      <!-- BANNERSH-133: post-approval step where the customer picks an eyelet
           option and sees the running total before sending the banner to the
           cart. -->
      <div v-if="genPhase === 'tilpass' && currentDesignRequest" style="display:grid;gap:24px">
        <div style="text-align:center">
          <h2 class="display" style="font-size:28px;color:var(--text);margin-bottom:8px">
            <i class="fa-solid fa-sliders" style="color:var(--accent);margin-right:8px"></i>
            Tilpass banneret
          </h2>
          <p style="color:var(--muted)">Velg om du vil ha maljer (øyebolter), og legg banneret i handlekurven.</p>
        </div>

        <!-- Preview -->
        <div class="bb-panel" style="padding:0;overflow:hidden;border-radius:0">
          <img
            v-if="currentDesignRequest.previewUrl"
            :src="currentDesignRequest.previewUrl"
            :alt="isManual ? 'Ditt banner — forhåndsvisning' : `AI-generert banner for ${currentDesignRequest.personName}`"
            style="width:100%;height:auto;object-fit:contain;display:block"
            @load="onPreviewImageLoaded"
          />
          <div v-else style="display:flex;align-items:center;justify-content:center;height:240px;color:var(--faint)">
            Forhåndsvisning ikke tilgjengelig
          </div>
        </div>

        <!-- Loading -->
        <div v-if="tilpassLoading" style="text-align:center;padding:1.5rem;color:var(--muted)">
          <i class="fa-solid fa-circle-notch fa-spin" style="margin-right:8px"></i>
          Henter pris…
        </div>

        <template v-else-if="tilpassBannerSize">
          <!-- Banner price summary -->
          <div class="bb-panel" style="display:grid;gap:14px">
            <div>
              <div class="field-label">Størrelse</div>
              <div class="display" style="font-size:22px;color:var(--text);margin-top:4px">
                {{ tilpassDesignWidthCm }} × {{ tilpassDesignHeightCm }} cm
              </div>
            </div>
            <div style="display:flex;justify-content:space-between;align-items:center;font-size:15px;padding-top:8px;border-top:1px solid var(--line-soft)">
              <span style="color:var(--muted)">Bannerpris</span>
              <span style="color:var(--text);font-weight:600">{{ formatNok(tilpassBannerPriceNok) }}</span>
            </div>
          </div>

          <!-- Eyelet option picker -->
          <div class="bb-panel" style="display:grid;gap:14px">
            <div>
              <div class="field-label" style="margin-bottom:4px">
                Maljer (øyebolter)
                <span style="font-size:13px;font-weight:400;color:var(--faint);margin-left:4px">tilvalg</span>
              </div>
            </div>
            <!-- BANNERSH-173: eyelet placement preview -->
            <EyeletPreview
              v-if="tilpassDesignWidthCm > 0 && tilpassDesignHeightCm > 0"
              :width-cm="tilpassDesignWidthCm"
              :height-cm="tilpassDesignHeightCm"
              :eyelet-option="tilpassEyeletOption"
              :image-url="currentDesignRequest?.previewUrl ?? undefined"
              style="border-radius:8px;overflow:hidden;border:1px solid var(--line-soft)"
            />
            <div style="display:grid;gap:10px">
              <label
                v-for="opt in ([
                  { value: 'None',        label: 'Ingen maljer',         sub: 'Uten hull' },
                  { value: 'FourCorners', label: '4 maljer (hjørner)',    sub: 'En i hvert hjørne' },
                  { value: 'PerMeter',    label: 'Maljer per meter',      sub: `Ca. 1 per 100 cm – ${countEyelets(tilpassDesignWidthCm, tilpassDesignHeightCm, 'PerMeter')} stk totalt` },
                ] as const)"
                :key="opt.value"
                class="eyelet-option"
                :class="{ 'eyelet-option--active': tilpassEyeletOption === opt.value }"
              >
                <input
                  type="radio"
                  :value="opt.value"
                  v-model="tilpassEyeletOption"
                  style="display:none"
                />
                <div style="flex:1">
                  <div style="font-weight:600;font-size:14.5px;color:var(--text)">{{ opt.label }}</div>
                  <div style="font-size:13px;color:var(--faint)">{{ opt.sub }}</div>
                </div>
                <div
                  v-if="opt.value !== 'None' && tilpassEyeletPriceNok > 0"
                  style="font-size:13px;color:var(--accent);font-weight:700;white-space:nowrap"
                >
                  +{{ formatNok(countEyelets(tilpassDesignWidthCm, tilpassDesignHeightCm, opt.value) * tilpassEyeletPriceNok) }}
                </div>
                <div class="eyelet-radio">
                  <div class="radio-outer" :class="{ 'radio-outer--active': tilpassEyeletOption === opt.value }">
                    <div v-if="tilpassEyeletOption === opt.value" class="radio-inner"></div>
                  </div>
                </div>
              </label>
            </div>
          </div>

          <!-- Sum -->
          <div class="bb-panel" style="display:grid;gap:10px">
            <!-- BANNERSH-189: manual mode adds a designer-fee line (cart will charge
                 it as a second item — keep the summary honest here). -->
            <div v-if="isManual" style="display:flex;justify-content:space-between;font-size:14.5px">
              <span style="color:var(--muted)">Designer-tjeneste</span>
              <span style="color:var(--text);font-weight:500">{{ formatNok(manualDesignPriceNok) }}</span>
            </div>
            <div style="display:flex;justify-content:space-between;font-size:14.5px">
              <span style="color:var(--muted)">Bannerpris</span>
              <span style="color:var(--text);font-weight:500">{{ formatNok(tilpassBannerPriceNok) }}</span>
            </div>
            <div v-if="tilpassEyeletFeeNok > 0" style="display:flex;justify-content:space-between;font-size:14.5px">
              <span style="color:var(--muted)">Maljer ({{ tilpassEyeletCount }} stk)</span>
              <span style="color:var(--text);font-weight:500">{{ formatNok(tilpassEyeletFeeNok) }}</span>
            </div>
            <div style="display:flex;justify-content:space-between;font-size:17px;padding-top:10px;border-top:1px solid var(--line-soft)">
              <span style="font-weight:700;color:var(--text)">Sum</span>
              <span style="font-weight:800;color:var(--accent)">{{ formatNok(tilpassTotalNok) }}</span>
            </div>
            <p style="font-size:13px;color:var(--faint);margin:0">
              Frakt og eventuelt ekspressgebyr beregnes i kassen.
            </p>
          </div>

          <!-- CTA row -->
          <div style="display:grid;gap:14px">
            <button
              type="button"
              class="btn"
              style="width:100%;justify-content:center;padding:14px;font-size:16px;border-radius:12px;background:#3a9d7e;color:#fff"
              @click="addTilpassToCartAndCheckout"
            >
              <i class="fa-solid fa-cart-shopping"></i>
              Legg i handlekurven
            </button>
            <button
              type="button"
              class="btn btn-ghost"
              style="justify-content:center;padding:12px;font-size:14.5px;border-radius:12px"
              @click="backFromTilpass"
            >
              <i class="fa-solid fa-arrow-left" style="font-size:12px"></i> Tilbake
            </button>
          </div>
        </template>

        <div v-if="tilpassError" class="error-box">
          <i class="fa-solid fa-circle-exclamation"></i> {{ tilpassError }}
        </div>
      </div>

      <!-- ── Phase: error ────────────────────────────────────────────────── -->
      <div v-else-if="genPhase === 'error'" style="text-align:center;padding:4rem 0">
        <i class="fa-solid fa-triangle-exclamation" style="font-size:52px;color:var(--accent);margin-bottom:18px;display:block"></i>
        <h2 class="display" style="font-size:26px;color:var(--text);margin-bottom:10px">Noe gikk galt</h2>
        <p style="color:var(--muted);margin-bottom:24px;max-width:30em;margin-left:auto;margin-right:auto">
          {{ currentDesignRequest?.lastError ?? 'AI-genereringen feilet. Prøv igjen eller kontakt support.' }}
        </p>
        <div style="display:flex;gap:12px;justify-content:center;flex-wrap:wrap">
          <button type="button" class="btn btn-primary" @click="genPhase = 'idle'; generateApiError = null">
            <i class="fa-solid fa-rotate"></i> Prøv igjen
          </button>
          <RouterLink to="/account" class="btn btn-ghost">
            <i class="fa-solid fa-house"></i> Min konto
          </RouterLink>
        </div>
      </div>

      <!-- Back button (only in idle phase) -->
      <div v-if="genPhase === 'idle'" style="margin-top:24px">
        <button type="button" class="btn btn-ghost" @click="step = 2">
          <i class="fa-solid fa-arrow-left" style="font-size:12px"></i> Tilbake
        </button>
      </div>
    </div>

      </div><!-- end wizard main content -->
    </div><!-- end wizard-with-sidebar -->

    <!-- ═══════════════════════════════════════════════════════════════════
         PAYWALL MODAL (extracted component)
    ════════════════════════════════════════════════════════════════════════ -->
    <PaywallModal
      v-model="paywallOpen"
      :paywall-options="effectivePaywallOptions"
      :past-designs="pastDesigns"
      :pending-action="pendingAction"
      :design-request-id="designRequestId"
      @retry-action="onPaywallRetryAction"
      @credits-updated="onPaywallCreditsUpdated"
      @navigate-to="onPaywallNavigateTo"
      @select-past-design="onPaywallSelectPastDesign"
      @go-to-checkout="onPaywallGoToCheckout"
    />


  </div>
</template>

<style scoped>
/* ── Step indicator ──────────────────────────────────────────── */
.step-nav {
  display: flex;
  align-items: center;
  gap: 8px;
}
.step-nav-btn {
  display: flex;
  align-items: center;
  gap: 8px;
  font-size: 14px;
  font-weight: 600;
  background: none;
  border: none;
  padding: 0;
  cursor: pointer;
  font-family: var(--font-ui);
  transition: color 0.15s;
}
.step-active { color: var(--text); cursor: default; }
.step-done { color: var(--muted); }
.step-done:hover { color: var(--accent); }
.step-future { color: var(--faint); cursor: default; }

.step-circle {
  width: 28px;
  height: 28px;
  border-radius: 50%;
  display: grid;
  place-items: center;
  font-size: 13px;
  font-weight: 700;
  flex-shrink: 0;
  transition: background 0.15s;
}
.step-circle-active { background: var(--accent); color: var(--accent-ink); }
.step-circle-done { background: #3a9d7e; color: #fff; }
.step-circle-future { background: var(--surface-2); color: var(--faint); border: 1px solid var(--line); }
.step-label { display: none; }
@media (min-width: 480px) { .step-label { display: inline; } }

/* ── Language toggle ─────────────────────────────────────────── */
.lang-btn {
  border: 2px solid var(--line);
  border-radius: 10px;
  padding: 7px 16px;
  font-size: 14px;
  font-weight: 600;
  cursor: pointer;
  transition: border-color 0.15s, background 0.15s;
  background: transparent;
  color: var(--muted);
  font-family: var(--font-ui);
}
.lang-btn:hover { border-color: var(--line); color: var(--text); }
.lang-btn-active { border-color: var(--accent); color: var(--accent-2); background: rgba(255,106,61,.08); }

/* ── Template grid ───────────────────────────────────────────── */
.tpl-grid {
  display: grid;
  grid-template-columns: repeat(4, 1fr);
  gap: 12px;
}
.tpl-grid-sm {
  grid-template-columns: repeat(4, 1fr);
  gap: 8px;
}
@media (max-width: 600px) {
  .tpl-grid { grid-template-columns: repeat(2, 1fr); }
  .tpl-grid-sm { grid-template-columns: repeat(3, 1fr); }
}

.tpl-card {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 10px;
  border: 2px solid var(--line-soft);
  border-radius: 14px;
  padding: 18px 12px;
  cursor: pointer;
  transition: border-color 0.15s, background 0.15s, transform 0.15s;
  background: var(--surface);
  font-family: var(--font-ui);
}
.tpl-card:hover { border-color: var(--line); transform: translateY(-2px); }
.tpl-card-sel {
  border-color: var(--accent);
  background: rgba(255,106,61,.07);
  box-shadow: 0 0 0 2px rgba(255,106,61,.25);
}
.tpl-ico {
  width: 46px;
  height: 46px;
  border-radius: 12px;
  background: var(--surface-2);
  border: 1px solid var(--line-soft);
  display: grid;
  place-items: center;
  font-size: 20px;
  color: var(--accent);
}

/* ── Selected-template summary (BANNERSH-105) ────────────────── */
.selected-template-card {
  display: flex;
  align-items: center;
  gap: 14px;
  background: var(--surface);
  border: 1px solid var(--line-soft);
  border-radius: var(--radius);
  padding: 14px 18px;
  margin-bottom: 18px;
}
.selected-template-ico {
  width: 44px;
  height: 44px;
  border-radius: 12px;
  background: rgba(255,106,61,.12);
  border: 1px solid rgba(255,106,61,.28);
  display: grid;
  place-items: center;
  font-size: 19px;
  color: var(--accent);
  flex-shrink: 0;
}
.selected-template-eyebrow {
  font-size: 13px;
  font-weight: 700;
  color: var(--faint);
  text-transform: uppercase;
  letter-spacing: .06em;
  margin-bottom: 2px;
}
.selected-template-name {
  font-family: var(--font-display);
  font-weight: 700;
  font-size: 18px;
  color: var(--text);
  line-height: 1.2;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
.selected-template-change {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  background: transparent;
  border: 1px solid var(--line);
  border-radius: 10px;
  padding: 7px 12px;
  font-size: 13px;
  font-weight: 600;
  color: var(--muted);
  cursor: pointer;
  font-family: var(--font-ui);
  transition: border-color .15s, color .15s, background .15s;
  flex-shrink: 0;
}
.selected-template-change:hover {
  border-color: var(--accent);
  color: var(--accent-2);
  background: rgba(255,106,61,.06);
}

/* ── Photo upload zone ───────────────────────────────────────── */
.upload-zone {
  position: relative;
  width: 100%;
  border-radius: 14px;
  border: 2px dashed var(--line);
  background: var(--surface-2);
  cursor: pointer;
  user-select: none;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  text-align: center;
  padding: 2.5rem 1.5rem;
  transition: border-color 0.15s, background 0.15s;
}
.upload-zone:hover { border-color: var(--accent); background: rgba(255,106,61,.05); }
.upload-zone-drag { border-color: var(--accent); background: rgba(255,106,61,.1); }
.upload-zone-busy { opacity: 0.6; cursor: progress; }

.upload-overlay {
  position: absolute;
  inset: 0;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  background: rgba(21,18,14,.85);
  border-radius: 12px;
}

/* ── Panel ───────────────────────────────────────────────────── */
.bb-panel {
  background: var(--surface);
  border: 1px solid var(--line-soft);
  border-radius: var(--radius);
  padding: 26px;
}

/* ── Form inputs ─────────────────────────────────────────────── */
.field-label {
  display: block;
  font-size: 13px;
  font-weight: 700;
  color: var(--muted);
  margin-bottom: 8px;
  text-transform: uppercase;
  letter-spacing: .04em;
}
.dark-input {
  width: 100%;
  background: var(--surface-2);
  border: 1px solid var(--line);
  border-radius: 10px;
  padding: 10px 14px;
  font-size: 15px;
  color: var(--text);
  font-family: var(--font-ui);
  outline: none;
  transition: border-color 0.15s, box-shadow 0.15s;
}
.dark-input::placeholder { color: var(--faint); }
.dark-input:focus { border-color: var(--accent); box-shadow: 0 0 0 3px rgba(255,106,61,.18); }

/* ── Quality / size selector ─────────────────────────────────── */
.quality-grid {
  display: grid;
  grid-template-columns: repeat(3, 1fr);
  gap: 12px;
}
@media (max-width: 560px) { .quality-grid { grid-template-columns: 1fr; } }

.quality-btn {
  position: relative;
  display: flex;
  flex-direction: column;
  gap: 2px;
  border: 2px solid var(--line);
  border-radius: 14px;
  padding: 14px 16px;
  background: transparent;
  cursor: pointer;
  font-family: var(--font-ui);
  transition: border-color .15s, background .15s, box-shadow .15s;
  text-align: left;
}
.quality-btn:hover:not(:disabled) { border-color: var(--line-soft); color: var(--text); }
.quality-btn:disabled,
.quality-btn-disabled {
  opacity: 0.42;
  cursor: not-allowed;
  pointer-events: none;
}
.quality-btn-active {
  border-color: var(--accent);
  background: rgba(255,106,61,.08);
  color: var(--text);
  box-shadow: 0 0 0 2px rgba(255,106,61,.2);
}
.quality-btn-title {
  font-weight: 700;
  font-size: 15px;
  color: var(--text);
}
.quality-btn-sub {
  font-size: 13px;
  color: var(--muted);
  margin-bottom: 4px;
}
.quality-btn-dims {
  font-size: 13px;
  color: var(--faint);
}
.quality-btn-price {
  margin-top: 8px;
  font-size: 14px;
  font-weight: 700;
  color: var(--accent-2);
}

/* "Kommer snart" pill */
.coming-soon-pill {
  position: absolute;
  top: 8px;
  right: 8px;
  background: rgba(231,185,78,.18);
  color: var(--gold);
  border: 1px solid rgba(231,185,78,.35);
  border-radius: 999px;
  font-size: 11px;
  font-weight: 700;
  padding: 2px 8px;
  pointer-events: none;
}

/* Custom size inline form */
.custom-size-form {
  margin-top: 14px;
  padding: 16px;
  background: var(--surface-2);
  border: 1px solid var(--line-soft);
  border-radius: 12px;
}

/* Material selector buttons */
.mat-btn {
  border: 2px solid var(--line);
  border-radius: 8px;
  padding: 7px 14px;
  font-size: 14px;
  font-weight: 600;
  cursor: pointer;
  background: transparent;
  color: var(--muted);
  transition: border-color .15s, color .15s, background .15s;
  font-family: var(--font-ui);
}
.mat-btn:hover { color: var(--text); }
.mat-btn-active { border-color: var(--accent); color: var(--text); background: rgba(255,106,61,.08); }

/* ── Notices + errors ────────────────────────────────────────── */
.notice-gold {
  display: flex;
  align-items: flex-start;
  gap: 10px;
  background: rgba(231,185,78,.1);
  border: 1px solid rgba(231,185,78,.28);
  border-radius: 12px;
  padding: 14px 18px;
  font-size: 14px;
  color: var(--gold);
}
.notice-gold i { margin-top: 2px; flex-shrink: 0; }
.notice-gold a { color: var(--accent); font-weight: 600; text-decoration: none; }

.error-box {
  display: flex;
  align-items: center;
  gap: 9px;
  color: #f4a57a;
  background: rgba(255,106,61,.1);
  border: 1px solid rgba(255,106,61,.3);
  border-radius: 10px;
  padding: 10px 14px;
  font-size: 14px;
}
.error-box i { color: var(--accent); flex-shrink: 0; }

/* ── Step-2 inline preview area (BANNERSH-146) ───────────────── */
.preview-placeholder {
  width: 100%;
  aspect-ratio: 16 / 9;
  border: 2px dashed var(--line);
  border-radius: var(--radius);
  background: var(--surface);
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  text-align: center;
  padding: 2rem;
  gap: 4px;
}
.preview-generating {
  width: 100%;
  aspect-ratio: 16 / 9;
  border: 1px solid var(--line-soft);
  border-radius: var(--radius);
  background: var(--surface);
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 20px;
  padding: 2rem;
}
.preview-error-frame {
  border-color: rgba(255,106,61,.3);
  background: rgba(255,106,61,.04);
}
.preview-anon {
  width: 100%;
  border: 1px solid var(--line-soft);
  border-radius: var(--radius);
  background: var(--surface);
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  padding: 2.5rem 2rem;
  text-align: center;
}

/* ── Spinner / pulse animations ──────────────────────────────── */
@keyframes spin { to { transform: rotate(360deg); } }
@keyframes pulse {
  0%, 100% { opacity: 1; }
  50% { opacity: .4; }
}

/* ── Responsive grid ─────────────────────────────────────────── */
.pay-grid { grid-template-columns: 1.2fr .8fr; }
@media (max-width: 768px) { .pay-grid { grid-template-columns: 1fr !important; } }

/* ── Two-column layout: sidebar + wizard (BANNERSH-145) ──────── */
.wizard-with-sidebar {
  display: grid;
  grid-template-columns: 210px 1fr;
  gap: 28px;
  align-items: start;
}
@media (max-width: 820px) {
  .wizard-with-sidebar {
    grid-template-columns: 1fr;
  }
}

/* ── BANNERSH-133: Eyelet (malje) option selector (mirrors BannerBuilderView) ── */
.eyelet-option {
  display: flex;
  align-items: center;
  gap: 10px;
  background: var(--surface-2);
  border: 1.5px solid var(--line);
  border-radius: 10px;
  padding: 10px 14px;
  cursor: pointer;
  transition: border-color 0.15s, background 0.15s;
}
.eyelet-option:hover { border-color: var(--muted); }
.eyelet-option--active {
  border-color: var(--accent) !important;
  background: rgba(255, 106, 61, 0.07) !important;
}
.eyelet-radio { flex-shrink: 0; }
.radio-outer {
  width: 16px;
  height: 16px;
  border-radius: 50%;
  border: 2px solid var(--line);
  display: flex;
  align-items: center;
  justify-content: center;
  transition: border-color 0.15s;
}
.radio-outer--active { border-color: var(--accent); }
.radio-inner {
  width: 7px;
  height: 7px;
  border-radius: 50%;
  background: var(--accent);
}

/* ── BANNERSH-170: Aspect-ratio selector row ─────────────────── */
.ratio-row {
  display: flex;
  gap: 8px;
  flex-wrap: nowrap;
  overflow-x: auto;
  padding-bottom: 2px;
  /* hide scrollbar on desktop while keeping it functional */
  scrollbar-width: thin;
  scrollbar-color: var(--line) transparent;
}
.ratio-btn {
  flex-shrink: 0;
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 6px;
  border: 2px solid var(--line);
  border-radius: 12px;
  padding: 14px 18px 12px;
  background: var(--surface-2);
  cursor: pointer;
  font-family: var(--font-ui);
  transition: border-color 0.15s, background 0.15s, box-shadow 0.15s;
  min-width: 78px;
}
.ratio-btn:hover {
  border-color: var(--line-soft);
  background: var(--surface);
}
.ratio-btn-active {
  border-color: var(--accent);
  background: rgba(255, 106, 61, 0.08);
  box-shadow: 0 0 0 2px rgba(255, 106, 61, 0.2);
}
.ratio-icon-wrap {
  width: 34px;
  height: 28px;
  display: flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
}
.ratio-icon {
  border: 2px solid currentColor;
  border-radius: 3px;
  opacity: 0.7;
  transition: opacity 0.15s;
}
.ratio-btn-active .ratio-icon { opacity: 1; }
.ratio-label {
  font-size: 13px;
  font-weight: 700;
  color: var(--text);
  line-height: 1;
  white-space: nowrap;
}
.ratio-sub {
  font-size: 13px;
  color: var(--faint);
  line-height: 1;
  white-space: nowrap;
}
.ratio-btn-active .ratio-label { color: var(--accent-2); }
.ratio-btn-active .ratio-sub   { color: var(--accent);   }
</style>

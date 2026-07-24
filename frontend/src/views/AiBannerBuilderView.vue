<script setup lang="ts">
import { ref, computed, watch, onMounted, onBeforeUnmount } from 'vue'
import { useRouter, useRoute, RouterLink } from 'vue-router'
import { useAuthStore } from '@/stores/auth'
import { useCartStore } from '@/stores/cart'
import { getBannerDesign } from '@/api/bannerBuilder'
import { fetchSizes, fetchPrice, fetchEyeletPriceNok } from '@/api/shop'
import type { BannerSize, EyeletOption, CartItem } from '@/types'
import { countEyelets } from '@/types'
import EyeletPreview from '@/components/shop/EyeletPreview.vue'
import {
  fetchTemplates,
  getDesignRequest,
  type BannerTemplateItem,
  type DesignRequestListItem,
  type BannerGenerationHistoryItem,
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
import BannerQualitySizePicker from '@/components/banner-builder/BannerQualitySizePicker.vue'
import BannerBuilderStep1 from '@/components/banner-builder/BannerBuilderStep1.vue'
import BannerStep2PersonalizeForm from '@/components/banner-builder/BannerStep2PersonalizeForm.vue'
import BannerGenerationInlineArea from '@/components/banner-builder/BannerGenerationInlineArea.vue'
import BannerBuilderStep3 from '@/components/banner-builder/BannerBuilderStep3.vue'

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
  materialOptions,
  highOptionWidthCm, goodOptionWidthCm,
  highOptionHeightCm, goodOptionHeightCm, // BANNERSH-259: catalog-derived heights
  selectedDimensions,
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
  approveError, approving, regenerating, regenerateError,
  reordering, reorderError,
  activatingGenerationId, activateGenerationError,
  startPolling, cleanup: cleanupGeneration,
  generateBanner: _generateBanner, approve, regenerate: _regenerate,
  reorderCurrentDesign, selectPastDesign: _selectPastDesign,
  selectGeneration,
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
  getMaterialOptions: () => materialOptions.value,
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

/** All completed generation attempts for the current design request, oldest first. */
const completedGenerations = computed<BannerGenerationHistoryItem[]>(() =>
  (currentDesignRequest.value?.generationHistory ?? []).filter(
    (g) => g.status === 'Completed' && g.previewUrl !== null,
  ),
)
const hasGenerationHistory = computed(() => completedGenerations.value.length > 1)

const templateName = computed(() => {
  const t = selectedTemplate.value
  if (!t) return ''
  return language.value === 'en' ? t.nameEn : t.nameNb
})

const step1Valid = computed(() => selectedTemplateId.value !== null)
const step2Valid = computed(() => {
  // The preview is a local object URL and appears before the upload request
  // finishes. Do not let Generate race that request and submit without the
  // BannerDesign id, which would make the server correctly choose text-to-image
  // while the customer believes their portrait was attached.
  if (photoUploading.value) return false
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
  if (!step2Valid.value) return
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
  if (!step2Valid.value) return
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

/**
 * Map a stored `DesignRequest.aspectRatio` (either `'WxH'` like `'266x150'` or
 * `'A:B'` like `'16:9'`) to the closest matching wizard {@link AspectRatioOption}
 * preset so the past design's shape is recalled accurately when the customer
 * re-opens it from the sidebar (BANNERSH-252).
 */
function aspectRatioToOption(raw: string | null | undefined): AspectRatioOption | null {
  if (!raw) return null
  let w = 0
  let h = 0
  const dims = /^(\d+)x(\d+)$/i.exec(raw)
  if (dims && dims[1] && dims[2]) {
    w = parseInt(dims[1], 10)
    h = parseInt(dims[2], 10)
  } else {
    const ratio = /^(\d+):(\d+)$/.exec(raw)
    if (ratio && ratio[1] && ratio[2]) {
      w = parseInt(ratio[1], 10)
      h = parseInt(ratio[2], 10)
    }
  }
  if (w <= 0 || h <= 0) return null
  const target = w / h
  const candidates: { opt: AspectRatioOption; ratio: number }[] = [
    { opt: '16:9', ratio: 16 / 9 },
    { opt: '1:2',  ratio: 1 / 2 },
    { opt: '1:1',  ratio: 1 },
    { opt: '2:1',  ratio: 2 },
    { opt: '3:1',  ratio: 3 },
    { opt: '4:1',  ratio: 4 },
  ]
  let best = candidates[0]!
  let bestDiff = Math.abs(target - best.ratio)
  for (const c of candidates) {
    const d = Math.abs(target - c.ratio)
    if (d < bestDiff) { best = c; bestDiff = d }
  }
  return best.opt
}

/** Select a past design: composable call + restore form fields + step navigation.
 *  BANNERSH-252: always reopens the wizard with values pre-filled — never
 *  redirects to /account/design-requests/<id>. In Manual mode the wizard then
 *  shows the synthetic "Ditt banner" placeholder + "Gå videre" CTA so the
 *  customer can edit and re-order (a new DesignRequest is minted on submit). */
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
    const restoredRatio = aspectRatioToOption(detail.aspectRatio)
    if (restoredRatio) selectedAspectRatio.value = restoredRatio
    if (detail.aspectRatio === '18:9' || detail.aspectRatio?.startsWith('300x')) {
      selectedQuality.value = 'good'
    } else {
      selectedQuality.value = 'high'
    }

    // BANNERSH-258: restore the uploaded portrait photo so the wizard shows
    // the thumbnail and carries the id forward when the user clicks "Gå videre"
    // (Manual) or "Generer ny versjon" (AI). Revoke any previous blob-URL first.
    if (photoPreviewUrl.value?.startsWith('blob:')) {
      URL.revokeObjectURL(photoPreviewUrl.value)
    }
    photoPreviewUrl.value = detail.uploadedPhotoUrl ?? null
    uploadedPhotoBannerDesignId.value = detail.uploadedPhotoBannerDesignId ?? null

    step.value = 2
    // Manual mode: refresh the canvas placeholder so the "Gå videre" CTA is
    // visible regardless of the past design's status (Approved / Final / etc.)
    // and the picker reflects the recalled aspect ratio.
    if (isManual.value) {
      generateManualPlaceholder()
    }
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

/** Moderation block: save current form state and switch to manual design flow */
function switchToManualMode() {
  saveManualSessionState()
  void router.push('/banner-builder/manual')
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

    // BANNERSH-281: pin to the material that matches the user's quality selection so
    // the server returns the rule for the right material (e.g. 680g "Høykvalitet")
    // rather than the cheapest rule across ALL materials (which could be a cheaper
    // 400g rule and produce a visibly lower — and wrong — banner price on the page).
    let tilpassMaterialId: number | undefined
    if (selectedQuality.value === 'high') {
      tilpassMaterialId = materialOptions.value[0]?.material.id
    } else if (selectedQuality.value === 'good') {
      tilpassMaterialId = materialOptions.value[1]?.material.id
    } else {
      // custom quality: find the material that matches the chosen gsm
      tilpassMaterialId = materialOptions.value.find(
        (m) => m.material.weightGsm === customMaterialGsm.value,
      )?.material.id
    }

    // BANNERSH-255: ask the server for the cheapest matching pricing rule,
    // pinned to the correct material so the displayed price matches the picker.
    const priceResp = await fetchPrice(design.computedWidthCm, design.selectedHeightCm, tilpassMaterialId)
    // Load the matched size for display (sortOrder, name, material).
    const allSizes: BannerSize[] = await fetchSizes()
    const pricingSize = allSizes.find((s: BannerSize) => s.id === priceResp.sizeId) ?? null
    if (!pricingSize) {
      throw new Error('Pricing not available for this banner.')
    }
    tilpassDesignWidthCm.value = design.computedWidthCm
    tilpassDesignHeightCm.value = design.selectedHeightCm
    tilpassBannerSize.value = pricingSize
    tilpassBannerPriceNok.value = priceResp.priceNok
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
    materialId: size.materialId,
    widthCm: tilpassDesignWidthCm.value,
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
  // BANNERSH-249: a manual order is ONE cart item — the 495 kr designer fee is
  // bundled on the banner line (server snaps it from the linked DesignRequest)
  // so there is no separate "Designer-tjeneste" line that would otherwise spawn
  // two OrderItem rows + obscure which banner the designer is working on.
  const bannerItem: CartItem = {
    bannerSizeId: size.id,
    bannerSizeName: `Manuelt banner ${tilpassDesignWidthCm.value} × ${tilpassDesignHeightCm.value} cm`,
    materialId: size.materialId,
    widthCm: tilpassDesignWidthCm.value,
    heightCm: tilpassDesignHeightCm.value,
    quantity: 1,
    unitPriceNok: manualBannerPriceNok.value,
    eyeletOption: tilpassEyeletOption.value,
    eyeletFeeNok: tilpassEyeletFeeNok.value,
    notes: `Manuelt designet banner — bestilling #${reqId}`,
    designRequestId: reqId,
    manualDesignFeeNok: manualDesignPriceNok.value,
  }
  cart.addItem(bannerItem)
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

// Regenerate the canvas placeholder when the aspect ratio changes while the
// user is on the ready phase (they chose a different ratio in the picker).
// BANNERSH-271: in AI mode the AI image keeps its original ratio (no live
// regeneration on ratio click — that needs an explicit "Generer ny versjon"),
// but the pricing picker below should still reflect the newly-selected ratio
// so the customer can see what their next attempt would cost. We override
// `aiImageNaturalRatio` with the chosen ratio; the price computeds + watcher
// in useBannerPricing then re-fetch prices for the new dimensions.
watch(selectedAspectRatio, (newRatio) => {
  if (genPhase.value !== 'ready') return
  if (isManual.value) {
    generateManualPlaceholder()
    return
  }
  const parts = newRatio.split(':')
  const rW = parseInt(parts[0] ?? '0', 10)
  const rH = parseInt(parts[1] ?? '0', 10)
  if (rW > 0 && rH > 0) {
    aiImageNaturalRatio.value = rW / rH
  }
})

// BANNERSH-251: Manual mode skips the "Se forhåndsvisning" intermediate step —
// the material picker + "Gå videre" CTA must appear immediately when the user
// reaches step 2.  Auto-generate the synthetic placeholder so the picker (which
// is gated on currentDesignRequest.previewUrl + genPhase === 'ready') renders
// without requiring the customer to click anything.
watch(step, (s) => {
  if (s === 2 && isManual.value && genPhase.value !== 'ready') {
    generateManualPlaceholder()
  }
}, { immediate: true })

// Handle the edge case where the user switches from /banner-builder/ai to
// /banner-builder/manual without remounting (e.g. via the "Velg manuell design"
// error fallback) while already on step 2 — the step watcher above won't fire
// because step didn't change, so trigger from the mode change too.
watch(isManual, (manual) => {
  if (manual && step.value === 2 && genPhase.value !== 'ready') {
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
         BANNERSH-263: extracted into BannerBuilderStep1 component.
    ════════════════════════════════════════════════════════════════════════ -->
    <BannerBuilderStep1
      v-if="step === 1"
      :templates="templates"
      :templatesLoading="templatesLoading"
      :templatesError="templatesError"
      v-model:selectedTemplateId="selectedTemplateId"
      v-model:language="language"
      :step1Valid="step1Valid"
      :categoryIconClass="categoryIconClass"
      @next="goToStep(2)"
      @retry="loadTemplates"
    />

    <!-- ═══════════════════════════════════════════════════════════════════
         STEP 2: Personalize
    ════════════════════════════════════════════════════════════════════════ -->
    <div v-else-if="step === 2">
      <!-- BANNERSH-265: extracted into BannerStep2PersonalizeForm component -->
      <BannerStep2PersonalizeForm
        :selected-template="selectedTemplate"
        :templates="templates"
        :selected-template-id="selectedTemplateId"
        :language="language"
        :person-name="personName"
        :text-content="textContent"
        :theme-description="themeDescription"
        :person-age="personAge"
        :selected-aspect-ratio="selectedAspectRatio"
        :ratio-options="ratioOptions"
        :category-icon-class="categoryIconClass"
        :text-content-placeholder="textContentPlaceholder"
        :theme-description-placeholder="themeDescriptionPlaceholder"
        :is-manual="isManual"
        :photo-preview-url="photoPreviewUrl"
        :photo-uploading="photoUploading"
        :photo-dragging="photoDragging"
        :photo-upload-progress="photoUploadProgress"
        :photo-upload-error="photoUploadError"
        @update:person-name="personName = $event"
        @update:text-content="textContent = $event"
        @update:theme-description="themeDescription = $event"
        @update:person-age="personAge = $event"
        @update:selected-template-id="selectedTemplateId = $event"
        @update:selected-aspect-ratio="selectedAspectRatio = $event"
        @change-template="step = 1"
        @on-photo-file-change="onPhotoFileChange"
        @on-photo-drag-over="onPhotoDragOver"
        @on-photo-drag-leave="onPhotoDragLeave"
        @on-photo-drop="onPhotoDrop"
        @remove-photo="removePhoto"
      />

      <!-- ── Inline preview + generate area (BANNERSH-266: extracted into BannerGenerationInlineArea) -->
      <BannerGenerationInlineArea
        :gen-phase="genPhase"
        :gen-progress="genProgress"
        :generate-api-error="generateApiError"
        :design-request-id="designRequestId"
        :current-design-request="currentDesignRequest"
        :is-manual="isManual"
        :has-generation-history="hasGenerationHistory"
        :completed-generations="completedGenerations"
        :activating-generation-id="activatingGenerationId"
        :activate-generation-error="activateGenerationError"
        :approve-error="approveError"
        :regenerate-error="regenerateError"
        :reorder-error="reorderError"
        :manual-submit-error="manualSubmitError"
        :approving="approving"
        :reordering="reordering"
        :manual-submitting="manualSubmitting"
        :step2-valid="step2Valid"
        :can-generate-for-free="canGenerateForFree"
        :has-credits-available="hasCreditsAvailable"
        :is-out-of-generations="isOutOfGenerations"
        :credits-remaining="creditsRemaining"
        :is-logged-in="auth.isLoggedIn"
        :generate-button-label="generateButtonLabel"
        :template-name="templateName"
        :option1-state="option1State"
        :option2-state="option2State"
        :custom-state="customState"
        :selected-quality="selectedQuality"
        :high-option-width-cm="highOptionWidthCm"
        :high-option-height-cm="highOptionHeightCm"
        :good-option-width-cm="goodOptionWidthCm"
        :good-option-height-cm="goodOptionHeightCm"
        :ai-image-natural-ratio="aiImageNaturalRatio"
        :ai-image-aspect-ratio="aiImageAspectRatio"
        :custom-width="customWidth"
        :custom-height="customHeight"
        :custom-material-gsm="customMaterialGsm"
        @preview-image-loaded="onPreviewImageLoaded"
        @regenerate="genPhase === 'ready' ? regenerate() : generateBanner()"
        @proceed="isManual ? manualGoVidere() : approve()"
        @reorder-current-design="reorderCurrentDesign"
        @select-generation="selectGeneration"
        @switch-to-manual-mode="switchToManualMode"
        @open-paywall="pendingAction = 'generate'; paywallOpen = true"
        @update:selected-quality="selectedQuality = $event"
        @update:custom-width="customWidth = $event"
        @update:custom-height="customHeight = $event"
        @update:custom-material-gsm="customMaterialGsm = $event"
      />

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
         BANNERSH-267: extracted into BannerBuilderStep3 component.
    ════════════════════════════════════════════════════════════════════════ -->
    <BannerBuilderStep3
      v-else-if="step === 3"
      :gen-phase="genPhase"
      :is-manual="isManual"
      :current-design-request="currentDesignRequest"
      :design-request-id="designRequestId"
      :generate-api-error="generateApiError"
      :approve-error="approveError"
      :regenerate-error="regenerateError"
      :reorder-error="reorderError"
      :activate-generation-error="activateGenerationError"
      :approving="approving"
      :regenerating="regenerating"
      :reordering="reordering"
      :step2-valid="step2Valid"
      :can-generate-for-free="canGenerateForFree"
      :has-credits-available="hasCreditsAvailable"
      :is-out-of-generations="isOutOfGenerations"
      :credits-remaining="creditsRemaining"
      :is-logged-in="auth.isLoggedIn"
      :generate-button-label="generateButtonLabel"
      :generate-button-subtitle="generateButtonSubtitle"
      :has-generation-history="hasGenerationHistory"
      :completed-generations="completedGenerations"
      :activating-generation-id="activatingGenerationId"
      :gen-progress="genProgress"
      :selected-template="selectedTemplate"
      :template-name="templateName"
      :person-name="personName"
      :person-age="personAge"
      :text-content="textContent"
      :theme-description="themeDescription"
      :selected-quality="selectedQuality"
      :high-option-width-cm="highOptionWidthCm"
      :high-option-height-cm="highOptionHeightCm"
      :good-option-width-cm="goodOptionWidthCm"
      :good-option-height-cm="goodOptionHeightCm"
      :custom-width="customWidth"
      :custom-height="customHeight"
      :uploaded-photo-banner-design-id="uploadedPhotoBannerDesignId"
      :photo-preview-url="photoPreviewUrl"
      :language="language"
      :category-icon-class="categoryIconClass"
      :templates="templates"
      :selected-template-id="selectedTemplateId"
      :photo-dragging="photoDragging"
      :photo-uploading="photoUploading"
      :photo-upload-progress="photoUploadProgress"
      :photo-upload-error="photoUploadError"
      :tilpass-loading="tilpassLoading"
      :tilpass-error="tilpassError"
      :tilpass-banner-size="tilpassBannerSize"
      :tilpass-design-width-cm="tilpassDesignWidthCm"
      :tilpass-design-height-cm="tilpassDesignHeightCm"
      :tilpass-banner-price-nok="tilpassBannerPriceNok"
      :tilpass-eyelet-price-nok="tilpassEyeletPriceNok"
      :tilpass-eyelet-option="tilpassEyeletOption"
      :tilpass-eyelet-count="tilpassEyeletCount"
      :tilpass-eyelet-fee-nok="tilpassEyeletFeeNok"
      :tilpass-total-nok="tilpassTotalNok"
      :manual-design-price-nok="manualDesignPriceNok"
      @preview-image-loaded="onPreviewImageLoaded"
      @generate="generateBanner"
      @regenerate="regenerate"
      @proceed="isManual ? manualGoVidere() : approve()"
      @reorder-current-design="reorderCurrentDesign"
      @select-generation="selectGeneration"
      @return-to-wizard-idle="returnToWizardIdle"
      @switch-to-manual-mode="switchToManualMode"
      @back="step = 2"
      @reset-to-idle="genPhase = 'idle'; generateApiError = null"
      @add-to-cart="addTilpassToCartAndCheckout"
      @back-from-tilpass="backFromTilpass"
      @update:tilpass-eyelet-option="tilpassEyeletOption = $event"
      @update:selected-template-id="selectedTemplateId = $event"
      @update:person-name="personName = $event"
      @update:text-content="textContent = $event"
      @update:theme-description="themeDescription = $event"
      @on-photo-file-change="onPhotoFileChange"
      @on-photo-drag-over="onPhotoDragOver"
      @on-photo-drag-leave="onPhotoDragLeave"
      @on-photo-drop="onPhotoDrop"
      @remove-photo="removePhoto"
    />

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

/* ── Notices ─────────────────────────────────────────────────── */
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
</style>

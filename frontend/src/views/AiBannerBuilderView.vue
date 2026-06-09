<script setup lang="ts">
import { ref, computed, watch, onMounted, onBeforeUnmount } from 'vue'
import { useRouter, useRoute, RouterLink } from 'vue-router'
import { loadStripe } from '@stripe/stripe-js'
import type { Stripe, StripeCardElement } from '@stripe/stripe-js'
import { useAuthStore } from '@/stores/auth'
import { useCartStore } from '@/stores/cart'
import { uploadBannerFile, getBannerDesign } from '@/api/bannerBuilder'
import { fetchSizes, fetchEyeletPriceNok } from '@/api/shop'
import type { BannerSize, EyeletOption } from '@/types'
import { countEyelets } from '@/types'
import {
  fetchTemplates,
  createAiRequest,
  getDesignRequest,
  approveDesignRequest,
  regenerateDesignRequest,
  listDesignRequests,
  type BannerTemplateItem,
  type DesignRequestDetail,
  type DesignRequestListItem,
  type AiPaywallData,
  type PaywallOptions,
} from '@/api/designRequests'
import { getAiCreditsBalance, buyCreditPack, type CreditPackTier } from '@/api/aiCredits'
import { generateRequestIntegrity } from '@/composables/useRequestIntegrity'
import { useAiCreditsStore } from '@/stores/aiCredits'

// ── Router / auth / cart ──────────────────────────────────────────────────────
const router = useRouter()
const route = useRoute()
const auth = useAuthStore()
const cart = useCartStore()
// Shared credit-badge store (BANNERSH-87) — mirror every local creditsRemaining
// update into the store so the header pill stays accurate without forcing the
// user to navigate away from the wizard.
const creditsStore = useAiCreditsStore()

// ── Step state ────────────────────────────────────────────────────────────────
const step = ref<1 | 2 | 3>(1)

// ── Step 1: Template, photo, language ────────────────────────────────────────
const templates = ref<BannerTemplateItem[]>([])
const templatesLoading = ref(true)
const templatesError = ref<string | null>(null)
const selectedTemplateId = ref<number | null>(null)
const language = ref<'nb' | 'en'>('nb')

// Portrait photo upload
const uploadedPhotoBannerDesignId = ref<number | null>(null)
const photoPreviewUrl = ref<string | null>(null)
const photoUploading = ref(false)
const photoUploadProgress = ref(0)
const photoUploadError = ref<string | null>(null)
const photoFileInput = ref<HTMLInputElement | null>(null)
const photoDragging = ref(false)

// ── Step 2: Personalization ───────────────────────────────────────────────────
const personName = ref('')
const personAge = ref<number | null>(null)
const textContent = ref('')
const themeDescription = ref('')
const aspectRatio = ref<'16:9' | '18:9'>('16:9')

// ── Step 3: Generation state ──────────────────────────────────────────────────
// BANNERSH-133: `tilpass` is a post-approval phase where the customer picks an
// eyelet (malje) finishing option before the banner goes into the cart.
type GenPhase = 'idle' | 'submitting' | 'generating' | 'anon_pending' | 'ready' | 'tilpass' | 'error'
const genPhase = ref<GenPhase>('idle')
const currentDesignRequest = ref<DesignRequestDetail | null>(null)
const designRequestId = ref<number | null>(null)
const requiresAuthHint = ref(false)    // true when anonymous post returns requiresAuth
const generateApiError = ref<string | null>(null)
const approveError = ref<string | null>(null)
const approving = ref(false)
const regenerating = ref(false)
const regenerateError = ref<string | null>(null)

// Edit-and-regenerate panel toggle (shown in 'ready' phase)
const editExpanded = ref(false)

// ── Reorder (Approved / Final designs) ───────────────────────────────────────
const reordering = ref(false)
const reorderError = ref<string | null>(null)

// ── BANNERSH-133: Tilpass (eyelet picker) state ──────────────────────────────
const tilpassDesignWidthCm = ref<number>(0)
const tilpassDesignHeightCm = ref<number>(0)
const tilpassBannerSize = ref<BannerSize | null>(null)
const tilpassBannerPriceNok = ref<number>(0)
const tilpassEyeletOption = ref<EyeletOption>('None')
const tilpassEyeletPriceNok = ref<number>(0)
const tilpassLoading = ref(false)
const tilpassError = ref<string | null>(null)

const tilpassEyeletCount = computed(() =>
  countEyelets(tilpassDesignWidthCm.value, tilpassDesignHeightCm.value, tilpassEyeletOption.value),
)
const tilpassEyeletFeeNok = computed(() =>
  tilpassEyeletCount.value * tilpassEyeletPriceNok.value,
)
const tilpassTotalNok = computed(() =>
  tilpassBannerPriceNok.value + tilpassEyeletFeeNok.value,
)

// ── Credits badge (auth users only) ──────────────────────────────────────────
const creditsRemaining = ref<number | null>(null)
// BANNERSH-83: track whether the user has spent their free generation. Together with
// `creditsRemaining`, this lets us label the Generate button accurately ("gratis" vs.
// "1 kreditt") and surface the paywall *before* hitting the API when we already know
// the call would 402 — avoiding the confusing "I clicked free and got a popup" flow.
const hasUsedFreeGeneration = ref<boolean | null>(null)

// ── Past banners (BANNERSH-83) ────────────────────────────────────────────────
// Auth users only: load their previously-generated AI designs so they can revisit
// and pick one instead of regenerating.
const pastDesigns = ref<DesignRequestListItem[]>([])
const pastDesignsLoading = ref(false)

// ── Paywall modal ─────────────────────────────────────────────────────────────
const paywallOpen = ref(false)
const paywallData = ref<AiPaywallData | null>(null)
// null = closed, 'generate' = retry initial create, 'regenerate' = retry regenerate
type PendingAction = 'generate' | 'regenerate'
const pendingAction = ref<PendingAction>('generate')

type CreditPackPhase = 'menu' | 'loading' | 'card' | 'processing' | 'done' | 'error'
const creditPackPhase = ref<CreditPackPhase>('menu')
const creditPackError = ref<string | null>(null)
const packDetails = ref<{ clientSecret: string; creditCount: number; priceNok: number } | null>(null)
const stripeCardError = ref<string | null>(null)

// Stripe (lazy – only initialised when user opens credit-pack purchase)
const stripeRef = ref<Stripe | null>(null)
const cardElement = ref<StripeCardElement | null>(null)
const cardMountEl = ref<HTMLDivElement | null>(null)

// ── Computed helpers ──────────────────────────────────────────────────────────
const selectedTemplate = computed(() =>
  templates.value.find((t) => t.id === selectedTemplateId.value) ?? null,
)

const templateName = computed(() => {
  const t = selectedTemplate.value
  if (!t) return ''
  return language.value === 'en' ? t.nameEn : t.nameNb
})

const aspectDimensions = computed(() => {
  if (aspectRatio.value === '18:9') return { width: 300, height: 150 }
  return { width: 266, height: 150 }
})

const step1Valid = computed(() => selectedTemplateId.value !== null)

const step2Valid = computed(
  () =>
    personName.value.trim().length > 0 &&
    textContent.value.trim().length > 0 &&
    themeDescription.value.trim().length > 0,
)

// Effective paywall options: use last-known from API, or sensible defaults
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

// BANNERSH-87: mirror the local creditsRemaining ref into the shared store so
// the navbar credit badge updates the instant a generation succeeds, without
// waiting for a route change to trigger App.vue's refetch.
watch([creditsRemaining, hasUsedFreeGeneration], ([n, used]) => {
  if (n !== null) creditsStore.setBalance(n, used ?? undefined)
})

// ── BANNERSH-83: free-generation eligibility ─────────────────────────────────
// Logged-in users with no remaining free generation AND zero credits will be hit by
// the backend's 402 paywall on the very next /design-requests/ai POST.  Detect that
// up front so the wizard can label the Generate button accurately and surface the
// paywall modal *before* posting (rather than after).
//
// Returns null for users whose status we don't yet know (anonymous callers, or
// auth callers before /ai-credits/me has resolved).  In that case we keep the
// original "free first" UX — letting the server be authoritative.
const canGenerateForFree = computed<boolean | null>(() => {
  if (!auth.isLoggedIn) return null            // anonymous: trust backend IP check
  if (hasUsedFreeGeneration.value === null) return null
  return !hasUsedFreeGeneration.value
})

const hasCreditsAvailable = computed<boolean>(() =>
  (creditsRemaining.value ?? 0) > 0,
)

/** True when an auth user is known to have no free generation left AND no credits. */
const isOutOfGenerations = computed<boolean>(() =>
  auth.isLoggedIn &&
  hasUsedFreeGeneration.value === true &&
  !hasCreditsAvailable.value,
)

// Pretty label for the Generate button — reflects what the click will actually do.
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

// ── Category icons (FontAwesome) ──────────────────────────────────────────────
const categoryIconClass: Record<string, string> = {
  Birthday: 'fa-cake-candles',
  Confirmation: 'fa-graduation-cap',
  Wedding: 'fa-ring',
  Anniversary: 'fa-champagne-glasses',
  Christmas: 'fa-tree',
  NewYear: 'fa-champagne-glasses',
  Baptism: 'fa-dove',
  Other: 'fa-gift',
}

// BANNERSH-105: per-category placeholder strings for the "Tekst på banneret"
// field. Birthday is the default fallback when the category is unknown.
const categoryBannerTextPlaceholder: Record<string, string> = {
  Birthday: 'f.eks. Gratulerer med dagen',
  Confirmation: 'f.eks. Gratulerer med konfirmasjonen',
  Wedding: 'f.eks. Gratulerer med bryllupsdagen',
  Baptism: 'f.eks. Til lykke med dåpsdagen',
  Anniversary: 'f.eks. Gratulerer med jubileet',
  Christmas: 'f.eks. God jul',
  NewYear: 'f.eks. Godt nytt år',
  Other: 'f.eks. Velkommen til festen',
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

// ── Load templates ────────────────────────────────────────────────────────────
async function loadTemplates() {
  templatesLoading.value = true
  templatesError.value = null
  try {
    templates.value = await fetchTemplates()
    if (templates.value.length > 0 && selectedTemplateId.value === null) {
      // BANNERSH-105: when the user arrived from a front-page category card
      // (e.g. /banner-builder/ai?category=Birthday), pre-select the matching
      // template so they can skip the template-picker step entirely.
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
    templatesError.value =
      ex.response?.data?.error || ex.message || 'Kunne ikke laste maler.'
  } finally {
    templatesLoading.value = false
  }
}

// ── Credits balance (auth only) ───────────────────────────────────────────────
async function loadCreditsBalance() {
  if (!auth.isLoggedIn) return
  try {
    const balance = await getAiCreditsBalance()
    creditsRemaining.value = balance.creditsRemaining
    hasUsedFreeGeneration.value = balance.hasUsedFreeGeneration
  } catch {
    // Non-critical — badge stays hidden
  }
}

// ── Past designs (auth only, BANNERSH-83) ────────────────────────────────────
async function loadPastDesigns() {
  if (!auth.isLoggedIn) return
  pastDesignsLoading.value = true
  try {
    const all = await listDesignRequests()
    // Show only AI designs that produced a viewable image — Manual designs live in
    // their own /account/design-requests area and shouldn't clutter the wizard.
    pastDesigns.value = all.filter(
      (d) => d.mode === 'Ai' && d.previewUrl !== null,
    )
  } catch {
    // Non-critical — gallery just stays empty.
    pastDesigns.value = []
  } finally {
    pastDesignsLoading.value = false
  }
}

// ── Pick a past banner: load it into the wizard's "ready" view ───────────────
async function selectPastDesign(item: DesignRequestListItem) {
  // Load full detail so all the action-button logic in the ready phase has the
  // data it expects (status, revisionsRemaining, etc.).
  try {
    const detail = await getDesignRequest(item.id)
    designRequestId.value = item.id
    currentDesignRequest.value = detail
    // Pre-fill the editable fields so "Generer ny versjon" / "Gå tilbake og endre"
    // start from the same inputs the user picked last time.
    personName.value = detail.personName
    personAge.value = detail.personAge ?? null
    textContent.value = detail.textContent
    themeDescription.value = detail.themeDescription
    selectedTemplateId.value = detail.bannerTemplateId
    language.value = detail.language === 'en' ? 'en' : 'nb'
    aspectRatio.value = detail.aspectRatio === '18:9' ? '18:9' : '16:9'

    step.value = 2
    if (detail.status === 'AwaitingApproval' || detail.status === 'Approved' || detail.status === 'Final') {
      genPhase.value = 'ready'
    } else if (detail.status === 'InProgress' || detail.status === 'Pending') {
      startPolling(item.id)
    } else {
      genPhase.value = 'error'
    }
    editExpanded.value = false
    // Scroll to top so the loaded banner is immediately visible.
    window.scrollTo({ top: 0, behavior: 'smooth' })
  } catch {
    // If detail load fails, fall back to the orders view where the user can drill in.
    void router.push(`/account/design-requests/${item.id}`)
  }
}

/** Back to the wizard idle state (keeps inputs so user can refine and generate again). */
function returnToWizardIdle() {
  genPhase.value = 'idle'
  generateApiError.value = null
  regenerateError.value = null
  approveError.value = null
  // The just-generated banner is now in the past-banners gallery; let the user
  // start a fresh generation (which will hit the paywall if they're out of free /
  // credits — see `isOutOfGenerations` guard in generateBanner()).
  currentDesignRequest.value = null
  designRequestId.value = null
  localStorage.removeItem('ai_banner_draft_id')
  step.value = 2
}

// ── Photo upload ──────────────────────────────────────────────────────────────
const PHOTO_MAX_BYTES = 10 * 1024 * 1024 // 10 MB
const PHOTO_ACCEPTED = ['image/jpeg', 'image/png', 'image/webp']

function openPhotoPicker() {
  if (photoUploading.value) return
  photoFileInput.value?.click()
}

function onPhotoFileChange(e: Event) {
  const input = e.target as HTMLInputElement
  const file = input.files?.[0]
  if (file) void handlePhotoFile(file)
  if (input) input.value = ''
}

function onPhotoDragOver(e: DragEvent) {
  e.preventDefault()
  photoDragging.value = true
}

function onPhotoDragLeave() {
  photoDragging.value = false
}

function onPhotoDrop(e: DragEvent) {
  e.preventDefault()
  photoDragging.value = false
  const file = e.dataTransfer?.files?.[0]
  if (file) void handlePhotoFile(file)
}

async function handlePhotoFile(file: File) {
  photoUploadError.value = null
  if (!PHOTO_ACCEPTED.includes(file.type)) {
    photoUploadError.value = `Filtypen ${file.type || 'ukjent'} støttes ikke. Bruk JPEG, PNG eller WEBP.`
    return
  }
  if (file.size > PHOTO_MAX_BYTES) {
    photoUploadError.value = `Filen er for stor (${(file.size / 1024 / 1024).toFixed(1)} MB). Maks 10 MB.`
    return
  }
  if (photoPreviewUrl.value) URL.revokeObjectURL(photoPreviewUrl.value)
  photoPreviewUrl.value = URL.createObjectURL(file)

  photoUploading.value = true
  photoUploadProgress.value = 0
  try {
    const resp = await uploadBannerFile(file, (pct) => {
      photoUploadProgress.value = pct
    })
    uploadedPhotoBannerDesignId.value = resp.designId
  } catch (e: unknown) {
    const ex = e as { response?: { status?: number; data?: { error?: string } }; message?: string }
    if (ex.response?.status === 401) {
      photoUploadError.value = 'Du må være innlogget for å laste opp et bilde.'
    } else {
      photoUploadError.value =
        ex.response?.data?.error || ex.message || 'Opplasting feilet. Prøv igjen.'
    }
    if (photoPreviewUrl.value) {
      URL.revokeObjectURL(photoPreviewUrl.value)
      photoPreviewUrl.value = null
    }
    uploadedPhotoBannerDesignId.value = null
  } finally {
    photoUploading.value = false
  }
}

function removePhoto() {
  if (photoPreviewUrl.value) URL.revokeObjectURL(photoPreviewUrl.value)
  photoPreviewUrl.value = null
  uploadedPhotoBannerDesignId.value = null
  photoUploadError.value = null
}

// ── Clear age when switching to a non-birthday template ──────────────────────
watch(selectedTemplateId, () => {
  if (selectedTemplate.value?.category !== 'Birthday') {
    personAge.value = null
  }
})

// ── Step navigation ───────────────────────────────────────────────────────────
function goToStep(s: 1 | 2 | 3) {
  if (s === 2 && !step1Valid.value) return
  if (s === 3 && (!step1Valid.value || !step2Valid.value)) return
  step.value = s
}

// ── Generate banner (free-first flow, no Stripe upfront) ─────────────────────
async function generateBanner() {
  if (genPhase.value === 'submitting' || genPhase.value === 'generating') return
  generateApiError.value = null

  // BANNERSH-83: when we already know this auth user has spent their free generation
  // and has 0 credits, surface the paywall *before* posting — otherwise the user sees
  // a confusing "I clicked the FREE button and got a popup" flow. The same paywall
  // is still wired to the 402 path below for cases we couldn't predict (e.g. the
  // /ai-credits/me call failed, or the user just consumed their last credit).
  if (isOutOfGenerations.value) {
    paywallData.value = paywallData.value ?? {
      reason: 'insufficient_credits',
      creditsRemaining: 0,
      paywallOptions: effectivePaywallOptions.value,
    }
    pendingAction.value = 'generate'
    openPaywallModal()
    return
  }

  genPhase.value = 'submitting'

  try {
    const integrity = await generateRequestIntegrity()
    const resp = await createAiRequest(
      {
        templateId: selectedTemplateId.value!,
        language: language.value,
        personName: personName.value.trim(),
        personAge: personAge.value ?? undefined,
        textContent: textContent.value.trim(),
        themeDescription: themeDescription.value.trim(),
        aspectRatio: aspectRatio.value,
        uploadedPhotoBannerDesignId: uploadedPhotoBannerDesignId.value ?? undefined,
      },
      integrity,
    )

    designRequestId.value = resp.designRequestId
    localStorage.setItem('ai_banner_draft_id', String(resp.designRequestId))

    if (resp.creditsRemaining !== undefined && auth.isLoggedIn) {
      creditsRemaining.value = resp.creditsRemaining
      // The backend has now flipped `HasUsedFreeAiGeneration` on this user — record
      // it locally so the button label updates immediately on the next render.
      hasUsedFreeGeneration.value = true
    }

    if (resp.requiresAuth) {
      requiresAuthHint.value = true
      // Anonymous user: show "anon_pending" — cannot poll without auth
      genPhase.value = 'anon_pending'
    } else {
      startPolling(resp.designRequestId)
    }
  } catch (e: unknown) {
    const ex = e as {
      response?: {
        status?: number
        data?: AiPaywallData & { error?: string }
      }
      message?: string
    }
    if (ex.response?.status === 402 && ex.response.data) {
      const d = ex.response.data
      paywallData.value = {
        reason: d.reason ?? 'insufficient_credits',
        creditsRemaining: d.creditsRemaining ?? 0,
        paywallOptions: d.paywallOptions ?? {
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
        },
      }
      pendingAction.value = 'generate'
      openPaywallModal()
    } else {
      generateApiError.value =
        ex.response?.data?.error ?? ex.message ?? 'Generering feilet. Prøv igjen.'
    }
    genPhase.value = 'idle'
  }
}

// ── Polling ───────────────────────────────────────────────────────────────────
let pollTimer: ReturnType<typeof setInterval> | null = null
const TERMINAL_STATUSES = ['AwaitingApproval', 'Approved', 'Final', 'Failed', 'Cancelled']

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
        // Past banners gallery shows this new finished design — refresh asynchronously.
        void loadPastDesigns()
      } else {
        genPhase.value = 'error'
      }
    }
  } catch {
    // Transient errors — keep polling
  }
}

// ── Approve ───────────────────────────────────────────────────────────────────
// BANNERSH-133: approval no longer jumps straight to /checkout — it transitions
// the wizard into the `tilpass` phase so the customer can pick an eyelet (malje)
// option (Ingen / 4 hjørner / Per meter) and see the running total before adding
// the banner to the cart.
async function approve() {
  if (!designRequestId.value || approving.value) return
  approveError.value = null
  approving.value = true
  try {
    const approved = await approveDesignRequest(designRequestId.value)
    currentDesignRequest.value = approved
    localStorage.removeItem('ai_banner_draft_id')

    // Resolve pricing for the produced BannerDesign so the tilpass phase can render
    // the running total. If the lookup fails we still hand the user off to the
    // design-request detail page rather than throwing them onto a broken screen.
    if (approved.finalBannerDesignId) {
      try {
        await loadTilpassPricing(approved.finalBannerDesignId)
        step.value = 3
        genPhase.value = 'tilpass'
        // Scroll to top so the new step is immediately visible.
        window.scrollTo({ top: 0, behavior: 'smooth' })
        return
      } catch {
        // Non-fatal: if pricing fails, fall through to the design-request detail page
      }
    }

    // Fallback: navigate to the design-request detail page where the user can
    // manually proceed to order.
    router.push(`/account/design-requests/${designRequestId.value}`)
  } catch (e: unknown) {
    const ex = e as { response?: { data?: { error?: string } }; message?: string }
    approveError.value =
      ex.response?.data?.error || ex.message || 'Godkjenning feilet. Prøv igjen.'
  } finally {
    approving.value = false
  }
}

// ── Tilpass: load pricing + eyelet info ──────────────────────────────────────
async function loadTilpassPricing(bannerDesignId: number) {
  tilpassLoading.value = true
  tilpassError.value = null
  try {
    const design = await getBannerDesign(bannerDesignId)
    const sizes = await fetchSizes(design.computedWidthCm)
    const pricingSize = sizes.find(
      (s) => s.isCustomWidth && s.heightCm === design.selectedHeightCm,
    )
    if (!pricingSize || pricingSize.calculatedPrice == null) {
      throw new Error('Pricing not available for this banner.')
    }
    tilpassDesignWidthCm.value = design.computedWidthCm
    tilpassDesignHeightCm.value = design.selectedHeightCm
    tilpassBannerSize.value = pricingSize
    tilpassBannerPriceNok.value = pricingSize.calculatedPrice
    // Reset eyelet selection to "Ingen" each time we (re-)enter the tilpass step.
    tilpassEyeletOption.value = 'None'

    // Best-effort eyelet price fetch — falls back to 0 (hidden in the UI).
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
  router.push('/checkout')
}

// ── Tilpass: back to the ready phase ─────────────────────────────────────────
function backFromTilpass() {
  step.value = 2
  genPhase.value = 'ready'
  tilpassError.value = null
}

// ── Reorder current Approved / Final design ───────────────────────────────────
async function reorderCurrentDesign() {
  const d = currentDesignRequest.value
  if (!d?.finalBannerDesignId || reordering.value) return
  reordering.value = true
  reorderError.value = null
  try {
    const design = await getBannerDesign(d.finalBannerDesignId)
    const sizes = await fetchSizes(design.computedWidthCm)
    const pricingSize = sizes.find(
      (s) => s.isCustomWidth && s.heightCm === design.selectedHeightCm,
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

// ── Re-generate ───────────────────────────────────────────────────────────────
async function regenerate() {
  if (!designRequestId.value || regenerating.value) return
  regenerateError.value = null
  regenerating.value = true
  try {
    const integrity = await generateRequestIntegrity()
    const resp = await regenerateDesignRequest(
      designRequestId.value,
      {
        textContent: textContent.value.trim() || undefined,
        themeDescription: themeDescription.value.trim() || undefined,
      },
      integrity,
    )
    creditsRemaining.value = resp.creditsRemaining
    genPhase.value = 'generating'
    currentDesignRequest.value = null
    editExpanded.value = false
    startPolling(designRequestId.value)
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
      // For regenerate 402, the backend doesn't return full paywallOptions —
      // reuse the last-known paywallData if available.
      paywallData.value = {
        reason: d?.paywallMetadata?.reason ?? d?.error ?? 'insufficient_credits',
        creditsRemaining: d?.creditsRemaining ?? 0,
        paywallOptions: paywallData.value?.paywallOptions ?? {
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
        },
      }
      pendingAction.value = 'regenerate'
      openPaywallModal()
    } else if (ex.response?.status === 401) {
      regenerateError.value = 'Du må være innlogget for å generere på nytt.'
    } else {
      regenerateError.value =
        ex.response?.data?.error || ex.message || 'Ny generering feilet. Prøv igjen.'
    }
  } finally {
    regenerating.value = false
  }
}

// ── Paywall modal ─────────────────────────────────────────────────────────────
function openPaywallModal() {
  creditPackPhase.value = 'menu'
  creditPackError.value = null
  stripeCardError.value = null
  packDetails.value = null
  paywallOpen.value = true
}

function closePaywall() {
  if (creditPackPhase.value === 'processing') return // Don't close mid-payment
  paywallOpen.value = false
  creditPackPhase.value = 'menu'
  cardElement.value?.destroy()
  cardElement.value = null
  stripeRef.value = null
}

async function navigateFromPaywall(url: string) {
  paywallOpen.value = false
  await router.push(url)
}

function goToCheckoutWithDesign() {
  const id = designRequestId.value
  paywallOpen.value = false
  void router.push(id ? `/checkout?designRequestId=${id}` : '/checkout')
}

// Stripe initialisation (lazy)
async function initStripe(): Promise<boolean> {
  if (stripeRef.value) return true
  const key = import.meta.env.VITE_STRIPE_PUBLISHABLE_KEY as string | undefined
  if (!key || key.startsWith('pk_test_REPLACE')) {
    creditPackError.value =
      'Stripe er ikke konfigurert i dette miljøet. Kortbetaling er ikke tilgjengelig.'
    creditPackPhase.value = 'error'
    return false
  }
  try {
    const stripe = await loadStripe(key)
    if (!stripe) {
      creditPackError.value = 'Stripe kunne ikke lastes. Prøv igjen.'
      creditPackPhase.value = 'error'
      return false
    }
    stripeRef.value = stripe
    return true
  } catch {
    creditPackError.value = 'Stripe kunne ikke initialiseres.'
    creditPackPhase.value = 'error'
    return false
  }
}

async function startCreditPackPurchase(pack: 'small' | 'large' = 'small') {
  if (!auth.isLoggedIn) {
    void router.push(`/login?redirect=${encodeURIComponent('/banner-builder/ai')}`)
    return
  }
  creditPackPhase.value = 'loading'
  creditPackError.value = null

  try {
    packDetails.value = await buyCreditPack(pack)
    const ok = await initStripe()
    if (!ok) return
    creditPackPhase.value = 'card'
    // Card element will be mounted via watch(cardMountEl) below
  } catch (e: unknown) {
    const ex = e as { response?: { data?: { error?: string } }; message?: string }
    creditPackError.value =
      ex.response?.data?.error ?? ex.message ?? 'Feil ved oppstart av betaling.'
    creditPackPhase.value = 'error'
  }
}

// Mount Stripe card element when its ref becomes available (phase transitions to 'card')
watch(cardMountEl, (el) => {
  if (!el || !stripeRef.value) return
  if (cardElement.value) {
    cardElement.value.mount(el)
    return
  }
  const elements = stripeRef.value.elements()
  const card = elements.create('card', {
    style: {
      base: {
        fontFamily: 'Hanken Grotesk, ui-sans-serif, system-ui, sans-serif',
        fontSize: '16px',
        color: '#f4efe8',
        '::placeholder': { color: '#8a8073' },
      },
      invalid: { color: '#ff6a3d' },
    },
    hidePostalCode: true,
  })
  card.on('change', (event) => {
    stripeCardError.value = event.error?.message ?? null
  })
  card.mount(el)
  cardElement.value = card
})

async function confirmCreditPackPayment() {
  if (!stripeRef.value || !cardElement.value || !packDetails.value) return
  creditPackPhase.value = 'processing'
  stripeCardError.value = null

  const { error } = await stripeRef.value.confirmCardPayment(packDetails.value.clientSecret, {
    payment_method: { card: cardElement.value },
  })

  if (error) {
    stripeCardError.value = error.message ?? 'Betalingen feilet. Prøv igjen.'
    creditPackPhase.value = 'card'
    return
  }

  // Payment succeeded — credits are granted by the Stripe webhook asynchronously.
  // Optimistically refresh balance and close modal, then retry the pending action.
  creditPackPhase.value = 'done'
  if (auth.isLoggedIn) {
    try {
      const balance = await getAiCreditsBalance()
      creditsRemaining.value = balance.creditsRemaining
      hasUsedFreeGeneration.value = balance.hasUsedFreeGeneration
    } catch {
      // Non-critical
    }
  }
  // Brief "Betaling godkjent!" pause, then close and retry
  setTimeout(() => {
    paywallOpen.value = false
    cardElement.value?.destroy()
    cardElement.value = null
    stripeRef.value = null
    if (pendingAction.value === 'generate') {
      void generateBanner()
    } else {
      void regenerate()
    }
  }, 1400)
}

// ── Utils ─────────────────────────────────────────────────────────────────────
function formatNok(n: number | null | undefined): string {
  if (n == null) return '–'
  return new Intl.NumberFormat('nb-NO', { maximumFractionDigits: 0 }).format(n) + ' kr'
}

// ── Lifecycle ─────────────────────────────────────────────────────────────────
onMounted(async () => {
  await loadTemplates()
  await loadCreditsBalance()
  await loadPastDesigns()

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
        aspectRatio.value = detail.aspectRatio === '18:9' ? '18:9' : '16:9'
        step.value = 2
      } catch {
        // Non-critical — just keep defaults and let the user fill in manually.
      }
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

onBeforeUnmount(() => {
  stopPolling()
  cardElement.value?.destroy()
  if (photoPreviewUrl.value) URL.revokeObjectURL(photoPreviewUrl.value)
})
</script>

<template>
  <div style="max-width:1200px;margin:0 auto;padding:2rem 1.5rem 4rem">

    <!-- Header (with credits badge for logged-in users) -->
    <header style="margin-bottom:2.5rem;text-align:center;position:relative">
      <h1 class="display" style="font-size:clamp(28px,4vw,44px);color:var(--text);margin-bottom:12px">
        AI-generert feiringsbanner
      </h1>
      <p style="font-size:18px;color:var(--muted);max-width:36em;margin:0 auto">
        Fortell oss om feiringen — vi lager et unikt banner med kunstig intelligens.
        <strong style="color:var(--text)">Første generering er gratis.</strong>
      </p>
      <!-- Credits badge -->
      <div
        v-if="auth.isLoggedIn && creditsRemaining !== null"
        style="display:inline-flex;align-items:center;gap:7px;margin-top:14px;background:rgba(255,106,61,.12);border:1px solid rgba(255,106,61,.3);border-radius:99px;padding:5px 14px;font-size:13px;font-weight:700;color:var(--accent)"
      >
        <i class="fa-solid fa-wand-magic-sparkles" style="font-size:11px"></i>
        <template v-if="canGenerateForFree === true">1 gratis generering tilgjengelig</template>
        <template v-else>{{ creditsRemaining }} AI forslag igjen</template>
      </div>
    </header>

    <!-- Soft auth hint (anonymous user after creation) — full-width, above the grid -->
    <div v-if="requiresAuthHint" class="notice-gold" style="margin-bottom:2rem">
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

      <!-- Left column: past banners sidebar -->
      <aside
        v-if="auth.isLoggedIn && pastDesigns.length > 0"
        class="past-sidebar"
        aria-label="Tidligere genererte banner"
      >
        <div class="past-sidebar-hd">
          <span class="display past-title">
            <i class="fa-solid fa-clock-rotate-left"></i>
            Tidligere
          </span>
          <span class="past-count">{{ pastDesigns.length }}</span>
        </div>
        <p class="past-sub">Klikk for å åpne — godkjenn eller bruk som utgangspunkt.</p>
        <div class="past-sidebar-list">
          <button
            v-for="d in pastDesigns"
            :key="d.id"
            type="button"
            class="past-card"
            :class="{ 'past-card-active': designRequestId === d.id }"
            @click="selectPastDesign(d)"
          >
            <div class="past-thumb">
              <img v-if="d.previewUrl" :src="d.previewUrl" :alt="`Tidligere banner for ${d.personName}`" />
            </div>
            <div class="past-meta">
              <div class="past-name">{{ d.personName || 'Uten navn' }}</div>
              <div class="past-theme">{{ d.themeDescription || '—' }}</div>
              <div class="past-status">
                <i v-if="d.status === 'Final' || d.status === 'Approved'" class="fa-solid fa-circle-check" style="color:#4ade80"></i>
                <i v-else-if="d.status === 'AwaitingApproval'" class="fa-solid fa-hourglass-half" style="color:var(--gold)"></i>
                <i v-else class="fa-solid fa-circle-info" style="color:var(--faint)"></i>
                {{ d.status === 'AwaitingApproval' ? 'Venter godkjenning'
                  : d.status === 'Final' ? 'Bestilt'
                  : d.status === 'Approved' ? 'Godkjent'
                  : d.status }}
              </div>
            </div>
          </button>
        </div>
      </aside>

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
        <div>
          <div class="field-label" style="margin-bottom:4px">
            Portrettfoto <span style="font-size:13px;font-weight:400;color:var(--faint)">(valgfritt)</span>
          </div>
          <p style="font-size:13px;color:var(--muted);margin-bottom:12px">
            Last opp et bilde av personen som feires — AI-en vil inkorporere det i banneret.
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

        <div>
          <div class="field-label" style="margin-bottom:10px">Størrelsesformat</div>
          <div style="display:flex;gap:12px;flex-wrap:wrap">
            <button type="button" class="ratio-btn" :class="{ 'ratio-btn-active': aspectRatio === '16:9' }" @click="aspectRatio = '16:9'">
              <div style="font-weight:700;margin-bottom:2px">16:9 (Standard)</div>
              <div style="font-size:13px;opacity:.7">ca. 266 × 150 cm</div>
            </button>
            <button type="button" class="ratio-btn" :class="{ 'ratio-btn-active': aspectRatio === '18:9' }" @click="aspectRatio = '18:9'">
              <div style="font-weight:700;margin-bottom:2px">18:9 (Bred)</div>
              <div style="font-size:13px;opacity:.7">ca. 300 × 150 cm</div>
            </button>
          </div>
          <div class="size-preview" style="margin-top:16px">
            <div class="ratio-visual" :style="aspectRatio === '16:9' ? { width: '96px', height: '54px' } : { width: '108px', height: '54px' }">
              {{ aspectRatio }}
            </div>
            <div>
              <div style="font-size:14.5px;font-weight:700;color:var(--text)">Ca. {{ aspectDimensions.width }} × {{ aspectDimensions.height }} cm</div>
              <div style="font-size:13px;color:var(--faint);margin-top:2px">
                {{ aspectRatio === '16:9' ? 'Standard panoramabanner – passer de fleste anledninger' : 'Bredere format – flott for lange vegger og rekkverk' }}
              </div>
            </div>
          </div>
        </div>
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

        <!-- Phase: ready — show the generated image -->
        <div v-else-if="genPhase === 'ready' && currentDesignRequest" class="bb-panel" style="padding:0;overflow:hidden">
          <img
            v-if="currentDesignRequest.previewUrl"
            :src="currentDesignRequest.previewUrl"
            :alt="`AI-generert banner for ${currentDesignRequest.personName}`"
            style="width:100%;height:auto;object-fit:contain;display:block"
          />
          <div v-else style="display:flex;align-items:center;justify-content:center;height:180px;color:var(--faint)">
            Forhåndsvisning ikke tilgjengelig
          </div>
        </div>

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
          <button
            v-if="genPhase === 'ready' && currentDesignRequest?.status === 'AwaitingApproval'"
            type="button"
            class="btn"
            style="width:100%;justify-content:center;padding:14px;font-size:16px;border-radius:12px;background:#3a9d7e;color:#fff"
            :disabled="approving"
            @click="approve"
          >
            <i v-if="approving" class="fa-solid fa-circle-notch fa-spin"></i>
            <i v-else class="fa-solid fa-arrow-right"></i>
            {{ approving ? 'Behandler…' : 'Gå videre' }}
          </button>

          <!-- "Bestill på nytt" — Approved / Final -->
          <button
            v-if="genPhase === 'ready' && currentDesignRequest?.finalBannerDesignId && (currentDesignRequest?.status === 'Approved' || currentDesignRequest?.status === 'Final')"
            type="button"
            class="btn"
            style="width:100%;justify-content:center;padding:14px;font-size:16px;border-radius:12px;background:#3a9d7e;color:#fff"
            :disabled="reordering"
            @click="reorderCurrentDesign"
          >
            <i v-if="reordering" class="fa-solid fa-circle-notch fa-spin"></i>
            <i v-else class="fa-solid fa-cart-shopping"></i>
            {{ reordering ? 'Legger i handlekurv…' : 'Bestill på nytt' }}
          </button>

          <!-- Generate (idle) / Regenerate (ready/error) button -->
          <button
            type="button"
            class="btn btn-primary"
            style="width:100%;justify-content:center;padding:14px;font-size:16px;border-radius:12px"
            :disabled="!step2Valid"
            @click="genPhase === 'ready' ? regenerate() : generateBanner()"
          >
            <i v-if="genPhase === 'error'" class="fa-solid fa-rotate"></i>
            <i v-else-if="isOutOfGenerations && genPhase !== 'ready'" class="fa-solid fa-bag-shopping"></i>
            <i v-else class="fa-solid fa-wand-magic-sparkles"></i>
            <template v-if="genPhase === 'ready'">
              <template v-if="canGenerateForFree === true">Generer ny versjon (gratis)</template>
              <template v-else-if="hasCreditsAvailable">Generer ny versjon (1 kreditt)</template>
              <template v-else>Generer ny versjon</template>
            </template>
            <template v-else-if="genPhase === 'idle'">{{ generateButtonLabel }}</template>
            <template v-else>Prøv igjen</template>
          </button>

          <!-- Credits text (per task: "Du har xx gratis ai bilder igjen" / "Du har xx ai kreditter igjen") -->
          <p style="font-size:13px;color:var(--faint);text-align:center;margin:0">
            <template v-if="canGenerateForFree === true">
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
              <button type="button" style="color:var(--accent);font-weight:600;background:none;border:none;cursor:pointer;padding:0;font-family:var(--font-ui);font-size:13px" @click="pendingAction = 'generate'; openPaywallModal()">kjøp kreditter</button>
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
                <dt class="field-label" style="margin-bottom:3px">Format</dt>
                <dd style="color:var(--text)">{{ aspectRatio }} — ca. {{ aspectDimensions.width }} × {{ aspectDimensions.height }} cm</dd>
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
        <div class="bb-panel" style="padding:0;overflow:hidden">
          <img
            v-if="currentDesignRequest.previewUrl"
            :src="currentDesignRequest.previewUrl"
            :alt="`AI-generert banner for ${currentDesignRequest.personName}`"
            style="width:100%;height:auto;object-fit:contain;display:block"
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
              {{ reordering ? 'Legger i handlekurv…' : 'Bestill på nytt' }}
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
        <div class="bb-panel" style="padding:0;overflow:hidden">
          <img
            v-if="currentDesignRequest.previewUrl"
            :src="currentDesignRequest.previewUrl"
            :alt="`AI-generert banner for ${currentDesignRequest.personName}`"
            style="width:100%;height:auto;object-fit:contain;display:block"
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
              <p style="font-size:13px;color:var(--faint);margin:0">
                Hem (søm) er ikke mulig på PVC-bannere — kun maljer tilbys.
              </p>
            </div>
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
         PAYWALL MODAL
    ════════════════════════════════════════════════════════════════════════ -->
    <Teleport to="body">
      <div v-if="paywallOpen" class="modal-backdrop" @click.self="closePaywall">
        <div class="modal-box" role="dialog" aria-modal="true" aria-label="Generer flere AI-banner">

          <!-- Close button -->
          <button
            v-if="creditPackPhase !== 'processing'"
            type="button"
            class="modal-close-btn"
            aria-label="Lukk"
            @click="closePaywall"
          >
            <i class="fa-solid fa-xmark"></i>
          </button>

          <!-- ── Menu phase ──────────────────────────────────────────────── -->
          <div v-if="creditPackPhase === 'menu'">
            <div style="text-align:center;margin-bottom:24px">
              <div style="width:52px;height:52px;border-radius:50%;background:rgba(255,106,61,.15);border:1px solid rgba(255,106,61,.3);display:grid;place-items:center;margin:0 auto 14px;font-size:22px;color:var(--accent)">
                <i class="fa-solid fa-wand-magic-sparkles"></i>
              </div>
              <h2 class="display" style="font-size:22px;color:var(--text);margin-bottom:8px">Generer flere AI-banner</h2>
              <p style="font-size:14px;color:var(--muted);max-width:28em;margin:0 auto">
                Du har brukt opp den gratis genereringen. Velg et alternativ for å fortsette.
              </p>
            </div>

            <div style="display:grid;gap:12px">
              <!-- Option 1a: Buy small credit pack (Liten) -->
              <button
                type="button"
                class="paywall-option paywall-option-primary"
                @click="startCreditPackPurchase('small')"
              >
                <div style="display:flex;align-items:flex-start;gap:14px">
                  <span class="paywall-option-ico" style="background:rgba(255,106,61,.15);color:var(--accent)">
                    <i class="fa-solid fa-bag-shopping"></i>
                  </span>
                  <div style="text-align:left">
                    <div style="font-weight:700;font-size:15px;color:var(--text)">
                      Liten pakke — {{ effectivePaywallOptions.creditPackSmallCount }} AI forslag
                      <span style="color:var(--accent)">({{ formatNok(effectivePaywallOptions.creditPackSmallPriceNok) }})</span>
                    </div>
                    <div style="font-size:13px;color:var(--muted);margin-top:3px">
                      Kortbetaling via Stripe — umiddelbar tilgang
                    </div>
                  </div>
                </div>
                <i class="fa-solid fa-chevron-right" style="color:var(--faint);font-size:12px;flex-shrink:0"></i>
              </button>

              <!-- Option 1b: Buy large credit pack (Stor) -->
              <button
                type="button"
                class="paywall-option paywall-option-primary"
                @click="startCreditPackPurchase('large')"
              >
                <div style="display:flex;align-items:flex-start;gap:14px">
                  <span class="paywall-option-ico" style="background:rgba(255,106,61,.2);color:var(--accent)">
                    <i class="fa-solid fa-bags-shopping"></i>
                  </span>
                  <div style="text-align:left">
                    <div style="font-weight:700;font-size:15px;color:var(--text)">
                      Stor pakke — {{ effectivePaywallOptions.creditPackLargeCount }} AI forslag
                      <span style="color:var(--accent)">({{ formatNok(effectivePaywallOptions.creditPackLargePriceNok) }})</span>
                    </div>
                    <div style="font-size:13px;color:var(--muted);margin-top:3px">
                      Beste verdi — spar over 50% per forslag
                    </div>
                  </div>
                </div>
                <i class="fa-solid fa-chevron-right" style="color:var(--faint);font-size:12px;flex-shrink:0"></i>
              </button>

              <!-- Option 2: Order the banner now -->
              <button
                type="button"
                class="paywall-option"
                @click="goToCheckoutWithDesign"
              >
                <div style="display:flex;align-items:flex-start;gap:14px">
                  <span class="paywall-option-ico" style="background:rgba(74,222,128,.1);color:#4ade80">
                    <i class="fa-solid fa-cart-shopping"></i>
                  </span>
                  <div style="text-align:left">
                    <div style="font-weight:700;font-size:15px;color:var(--text)">
                      Betal for banneret nå
                      <span style="color:#4ade80">({{ effectivePaywallOptions.bannerOrderCreditBonus }} ytterligere forslag inkludert)</span>
                    </div>
                    <div style="font-size:13px;color:var(--muted);margin-top:3px">
                      Du kan fortsatt lage flere design før du sender inn bestillingen
                    </div>
                  </div>
                </div>
                <i class="fa-solid fa-chevron-right" style="color:var(--faint);font-size:12px;flex-shrink:0"></i>
              </button>

              <!-- Option 3: Manual designer -->
              <button
                type="button"
                class="paywall-option"
                @click="navigateFromPaywall(effectivePaywallOptions.manualDesignerUrl)"
              >
                <div style="display:flex;align-items:flex-start;gap:14px">
                  <span class="paywall-option-ico" style="background:rgba(231,185,78,.1);color:var(--gold)">
                    <i class="fa-solid fa-paintbrush"></i>
                  </span>
                  <div style="text-align:left">
                    <div style="font-weight:700;font-size:15px;color:var(--text)">Få vår designer til å lage design for deg</div>
                    <div style="font-size:13px;color:var(--muted);margin-top:3px">Menneskelig designer — 495 kr</div>
                  </div>
                </div>
                <i class="fa-solid fa-chevron-right" style="color:var(--faint);font-size:12px;flex-shrink:0"></i>
              </button>

              <!-- Option 4: Upload own design -->
              <button
                type="button"
                class="paywall-option"
                @click="navigateFromPaywall(effectivePaywallOptions.uploadOwnUrl)"
              >
                <div style="display:flex;align-items:flex-start;gap:14px">
                  <span class="paywall-option-ico" style="background:rgba(255,106,61,.08);color:var(--muted)">
                    <i class="fa-solid fa-file-arrow-up"></i>
                  </span>
                  <div style="text-align:left">
                    <div style="font-weight:700;font-size:15px;color:var(--text)">Last opp ditt eget design</div>
                    <div style="font-size:13px;color:var(--muted);margin-top:3px">Du betaler bare for banneren</div>
                  </div>
                </div>
                <i class="fa-solid fa-chevron-right" style="color:var(--faint);font-size:12px;flex-shrink:0"></i>
              </button>
            </div>

            <!-- BANNERSH-83: surface past banners inside the paywall so the user can pick
                 a previous design instead of paying. -->
            <div v-if="pastDesigns.length > 0" style="margin-top:22px;border-top:1px solid var(--line-soft);padding-top:18px">
              <div style="font-size:13px;font-weight:700;color:var(--muted);text-transform:uppercase;letter-spacing:.04em;margin-bottom:10px;display:flex;align-items:center;gap:8px">
                <i class="fa-solid fa-clock-rotate-left" style="color:var(--accent)"></i>
                Eller bruk et tidligere banner
              </div>
              <div style="display:flex;gap:10px;overflow-x:auto;padding-bottom:6px">
                <button
                  v-for="d in pastDesigns.slice(0, 6)"
                  :key="d.id"
                  type="button"
                  class="past-mini"
                  @click="() => { paywallOpen = false; selectPastDesign(d) }"
                  :title="`${d.personName} — ${d.themeDescription}`"
                >
                  <img v-if="d.previewUrl" :src="d.previewUrl" :alt="`Banner for ${d.personName}`" />
                  <span class="past-mini-name">{{ d.personName || 'Uten navn' }}</span>
                </button>
              </div>
            </div>
          </div>

          <!-- ── Loading phase ───────────────────────────────────────────── -->
          <div v-else-if="creditPackPhase === 'loading'" style="text-align:center;padding:2rem 0">
            <i class="fa-solid fa-circle-notch fa-spin" style="font-size:32px;color:var(--accent);margin-bottom:14px;display:block"></i>
            <p style="color:var(--muted)">Forbereder betaling…</p>
          </div>

          <!-- ── Card phase (Stripe) ─────────────────────────────────────── -->
          <div v-else-if="creditPackPhase === 'card' && packDetails">
            <button
              type="button"
              style="display:flex;align-items:center;gap:8px;font-size:13px;color:var(--muted);background:none;border:none;cursor:pointer;padding:0 0 18px;font-family:var(--font-ui)"
              @click="creditPackPhase = 'menu'"
            >
              <i class="fa-solid fa-arrow-left" style="font-size:11px"></i> Tilbake
            </button>

            <h3 class="display" style="font-size:18px;color:var(--text);margin-bottom:6px">
              Kjøp {{ packDetails.creditCount }} AI banner forslag
            </h3>
            <p style="font-size:14px;color:var(--muted);margin-bottom:20px">
              Pris: <strong style="color:var(--text)">{{ formatNok(packDetails.priceNok) }}</strong> — belastes med en gang
            </p>

            <label class="field-label" style="margin-bottom:8px">Kortdetaljer</label>
            <div ref="cardMountEl" class="stripe-mount" />
            <p v-if="stripeCardError" class="error-box" style="margin-top:10px">
              <i class="fa-solid fa-circle-exclamation"></i> {{ stripeCardError }}
            </p>

            <button
              type="button"
              class="btn btn-primary"
              style="width:100%;justify-content:center;padding:14px;font-size:15px;border-radius:12px;margin-top:18px"
              @click="confirmCreditPackPayment"
            >
              <i class="fa-solid fa-lock" style="font-size:12px"></i>
              Betal {{ formatNok(packDetails.priceNok) }}
            </button>
            <p style="font-size:13px;color:var(--faint);text-align:center;margin-top:10px">
              <i class="fa-solid fa-shield-halved"></i> Sikret av Stripe. Vi lagrer ikke kortinformasjon.
            </p>
          </div>

          <!-- ── Processing phase ────────────────────────────────────────── -->
          <div v-else-if="creditPackPhase === 'processing'" style="text-align:center;padding:2rem 0">
            <i class="fa-solid fa-circle-notch fa-spin" style="font-size:32px;color:var(--accent);margin-bottom:14px;display:block"></i>
            <p style="color:var(--muted)">Behandler betaling…</p>
          </div>

          <!-- ── Done phase ──────────────────────────────────────────────── -->
          <div v-else-if="creditPackPhase === 'done'" style="text-align:center;padding:2rem 0">
            <i class="fa-solid fa-circle-check" style="font-size:48px;color:#4ade80;margin-bottom:14px;display:block"></i>
            <h3 class="display" style="font-size:20px;color:var(--text);margin-bottom:8px">Betaling godkjent!</h3>
            <p style="color:var(--muted)">Kreditene er lagt til. Genererer nytt banner…</p>
          </div>

          <!-- ── Error phase ─────────────────────────────────────────────── -->
          <div v-else-if="creditPackPhase === 'error'" style="text-align:center;padding:1rem 0">
            <div class="error-box" style="justify-content:center;flex-direction:column;gap:12px;padding:20px">
              <i class="fa-solid fa-circle-exclamation" style="font-size:28px"></i>
              <p>{{ creditPackError }}</p>
              <button type="button" class="btn btn-ghost" @click="creditPackPhase = 'menu'">Prøv igjen</button>
            </div>
          </div>

        </div>
      </div>
    </Teleport>

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

/* ── Aspect ratio buttons ────────────────────────────────────── */
.ratio-btn {
  border: 2px solid var(--line);
  border-radius: 12px;
  padding: 12px 20px;
  font-size: 14px;
  cursor: pointer;
  transition: border-color 0.15s, background 0.15s;
  background: transparent;
  color: var(--muted);
  font-family: var(--font-ui);
  text-align: left;
}
.ratio-btn:hover { border-color: var(--line); color: var(--text); }
.ratio-btn-active { border-color: var(--accent); color: var(--text); background: rgba(255,106,61,.08); }

/* ── Size preview ────────────────────────────────────────────── */
.size-preview {
  background: var(--surface-2);
  border: 1px solid var(--line-soft);
  border-radius: 12px;
  padding: 16px;
  display: flex;
  align-items: center;
  gap: 16px;
}
.ratio-visual {
  border: 2px solid var(--accent);
  background: rgba(255,106,61,.08);
  border-radius: 6px;
  flex-shrink: 0;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 13px;
  color: var(--accent-2);
  font-weight: 700;
}

/* ── Stripe mount ────────────────────────────────────────────── */
.stripe-mount {
  background: var(--surface-2);
  border: 1px solid var(--line);
  border-radius: 10px;
  padding: 12px 14px;
  min-height: 44px;
  transition: border-color 0.15s, box-shadow 0.15s;
}
.stripe-mount:focus-within {
  border-color: var(--accent);
  box-shadow: 0 0 0 3px rgba(255,106,61,.18);
}

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

/* ── Paywall modal ───────────────────────────────────────────── */
.modal-backdrop {
  position: fixed;
  inset: 0;
  background: rgba(10, 8, 6, 0.78);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 1000;
  padding: 1rem;
  backdrop-filter: blur(4px);
}
.modal-box {
  background: var(--surface);
  border: 1px solid var(--line-soft);
  border-radius: 18px;
  padding: 28px 28px 24px;
  width: 100%;
  max-width: 480px;
  position: relative;
  max-height: calc(100vh - 2rem);
  overflow-y: auto;
}
.modal-close-btn {
  position: absolute;
  top: 16px;
  right: 16px;
  width: 32px;
  height: 32px;
  border-radius: 50%;
  border: 1px solid var(--line);
  background: var(--surface-2);
  cursor: pointer;
  display: grid;
  place-items: center;
  color: var(--muted);
  font-size: 14px;
  transition: background 0.15s, color 0.15s;
}
.modal-close-btn:hover { background: var(--line); color: var(--text); }

/* ── Paywall option buttons ──────────────────────────────────── */
.paywall-option {
  width: 100%;
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  background: var(--surface-2);
  border: 1px solid var(--line-soft);
  border-radius: 14px;
  padding: 14px 16px;
  cursor: pointer;
  font-family: var(--font-ui);
  text-align: left;
  transition: border-color 0.15s, background 0.15s, transform 0.1s;
}
.paywall-option:hover {
  border-color: var(--line);
  background: var(--surface);
  transform: translateY(-1px);
}
.paywall-option-primary {
  border-color: rgba(255,106,61,.35);
  background: rgba(255,106,61,.06);
}
.paywall-option-primary:hover { border-color: var(--accent); background: rgba(255,106,61,.1); }

.paywall-option-ico {
  width: 40px;
  height: 40px;
  border-radius: 10px;
  display: grid;
  place-items: center;
  font-size: 17px;
  flex-shrink: 0;
}

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

/* ── Past-banners sidebar (BANNERSH-83, BANNERSH-145) ────────── */
.past-sidebar {
  position: sticky;
  top: 20px;
  background: var(--surface);
  border: 1px solid var(--line-soft);
  border-radius: var(--radius);
  padding: 14px 12px 16px;
  max-height: calc(100vh - 48px);
  overflow-y: auto;
}
.past-sidebar-hd {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-bottom: 4px;
}
.past-title {
  font-size: 14px;
  color: var(--text);
  display: flex;
  align-items: center;
  gap: 7px;
  margin: 0;
  font-weight: 700;
  line-height: 1.3;
}
.past-title i { color: var(--accent); font-size: 13px; }
.past-count {
  background: rgba(255,106,61,.12);
  border: 1px solid rgba(255,106,61,.3);
  color: var(--accent);
  border-radius: 99px;
  padding: 2px 9px;
  font-size: 13px;
  font-weight: 700;
  flex-shrink: 0;
}
.past-sub {
  font-size: 13px;
  color: var(--muted);
  margin: 0 0 12px;
  line-height: 1.4;
}
.past-sidebar-list {
  display: flex;
  flex-direction: column;
  gap: 13px;
}
@media (max-width: 820px) {
  .past-sidebar {
    position: static;
    max-height: none;
  }
  .past-sidebar-list {
    flex-direction: row;
    overflow-x: auto;
    padding-bottom: 4px;
    scroll-snap-type: x mandatory;
  }
  .past-sidebar-list .past-card {
    flex: 0 0 160px;
    width: 160px;
  }
}
.past-card {
  width: 100%;
  display: flex;
  flex-direction: column;
  background: var(--surface-2);
  border: 2px solid var(--line-soft);
  border-radius: 12px;
  overflow: hidden;
  cursor: pointer;
  transition: border-color 0.15s, transform 0.15s, background 0.15s;
  scroll-snap-align: start;
  text-align: left;
  font-family: var(--font-ui);
  padding: 0;
}
.past-card:hover {
  border-color: var(--accent);
  transform: translateY(-2px);
  background: rgba(255,106,61,.06);
}
.past-card-active {
  border-color: var(--accent);
  background: rgba(255,106,61,.08);
  box-shadow: 0 0 0 2px rgba(255,106,61,.25);
}
.past-thumb {
  width: 100%;
  aspect-ratio: 16 / 9;
  background: var(--surface);
  display: grid;
  place-items: center;
  overflow: hidden;
}
.past-thumb img {
  width: 100%;
  height: 100%;
  object-fit: cover;
  display: block;
}
.past-meta {
  padding: 8px 10px 10px;
  display: flex;
  flex-direction: column;
  gap: 3px;
  /* 10% more lightness than --surface-2 (#2a251e ≈ hsl(35,17%,14%)) */
  background: hsl(35 17% 24%);
}
.past-name {
  font-size: 14px;
  font-weight: 700;
  color: var(--text);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
.past-theme {
  font-size: 13px;
  color: var(--muted);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
.past-status {
  margin-top: 3px;
  font-size: 13px;
  color: var(--faint);
  display: flex;
  align-items: center;
  gap: 5px;
}

/* Compact past-banner thumbnails for inside the paywall modal */
.past-mini {
  flex: 0 0 110px;
  display: flex;
  flex-direction: column;
  gap: 5px;
  background: var(--surface-2);
  border: 1.5px solid var(--line-soft);
  border-radius: 10px;
  overflow: hidden;
  cursor: pointer;
  padding: 0;
  font-family: var(--font-ui);
  transition: border-color 0.15s, transform 0.1s;
}
.past-mini:hover {
  border-color: var(--accent);
  transform: translateY(-1px);
}
.past-mini img {
  width: 100%;
  aspect-ratio: 16 / 9;
  object-fit: cover;
  display: block;
}
.past-mini-name {
  font-size: 13px;
  font-weight: 700;
  color: var(--text);
  padding: 0 8px 7px;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  display: block;
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
</style>

<script setup lang="ts">
import { ref, computed, watch, onMounted, onBeforeUnmount } from 'vue'
import { useRouter, RouterLink } from 'vue-router'
import { loadStripe } from '@stripe/stripe-js'
import type { Stripe, StripeCardElement } from '@stripe/stripe-js'
import { useAuthStore } from '@/stores/auth'
import { uploadBannerFile } from '@/api/bannerBuilder'
import {
  fetchTemplates,
  createAiRequest,
  getDesignRequest,
  approveDesignRequest,
  regenerateDesignRequest,
  type BannerTemplateItem,
  type DesignRequestDetail,
  type AiPaywallData,
  type PaywallOptions,
} from '@/api/designRequests'
import { getAiCreditsBalance, buyCreditPack } from '@/api/aiCredits'
import { generateRequestIntegrity } from '@/composables/useRequestIntegrity'

// ── Router / auth ─────────────────────────────────────────────────────────────
const router = useRouter()
const auth = useAuthStore()

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
type GenPhase = 'idle' | 'submitting' | 'generating' | 'anon_pending' | 'ready' | 'error'
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

// ── Credits badge (auth users only) ──────────────────────────────────────────
const creditsRemaining = ref<number | null>(null)

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
  creditPackPriceNok: paywallData.value?.paywallOptions?.creditPackPriceNok ?? 29,
  creditPackCount: paywallData.value?.paywallOptions?.creditPackCount ?? 10,
  bannerOrderActivationFeeNok: paywallData.value?.paywallOptions?.bannerOrderActivationFeeNok ?? 20,
  manualDesignerUrl: paywallData.value?.paywallOptions?.manualDesignerUrl ?? '/banner-builder/manual',
  uploadOwnUrl: paywallData.value?.paywallOptions?.uploadOwnUrl ?? '/banner-builder',
}))

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

// ── Load templates ────────────────────────────────────────────────────────────
async function loadTemplates() {
  templatesLoading.value = true
  templatesError.value = null
  try {
    templates.value = await fetchTemplates()
    if (templates.value.length > 0 && selectedTemplateId.value === null) {
      selectedTemplateId.value = templates.value[0]?.id ?? null
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
  } catch {
    // Non-critical — badge stays hidden
  }
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
          creditPackPriceNok: 29,
          creditPackCount: 10,
          bannerOrderActivationFeeNok: 20,
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
      } else {
        genPhase.value = 'error'
      }
    }
  } catch {
    // Transient errors — keep polling
  }
}

// ── Approve ───────────────────────────────────────────────────────────────────
async function approve() {
  if (!designRequestId.value || approving.value) return
  approveError.value = null
  approving.value = true
  try {
    await approveDesignRequest(designRequestId.value)
    currentDesignRequest.value = await getDesignRequest(designRequestId.value)
    localStorage.removeItem('ai_banner_draft_id')
    router.push('/account/orders')
  } catch (e: unknown) {
    const ex = e as { response?: { data?: { error?: string } }; message?: string }
    approveError.value =
      ex.response?.data?.error || ex.message || 'Godkjenning feilet. Prøv igjen.'
  } finally {
    approving.value = false
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
          creditPackPriceNok: 29,
          creditPackCount: 10,
          bannerOrderActivationFeeNok: 20,
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

async function startCreditPackPurchase() {
  if (!auth.isLoggedIn) {
    void router.push(`/login?redirect=${encodeURIComponent('/banner-builder/ai')}`)
    return
  }
  creditPackPhase.value = 'loading'
  creditPackError.value = null

  try {
    packDetails.value = await buyCreditPack()
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

  // Resume a pending AI design from a previous session.
  // Common case: anonymous user generated a banner, registered/logged in, and was
  // redirected back here.  The design-id was stored in localStorage before they left.
  const draftIdStr = localStorage.getItem('ai_banner_draft_id')
  if (draftIdStr && auth.isLoggedIn) {
    const draftId = parseInt(draftIdStr, 10)
    if (!isNaN(draftId) && draftId > 0) {
      step.value = 3
      designRequestId.value = draftId
      startPolling(draftId)
    }
  }
})

onBeforeUnmount(() => {
  stopPolling()
  cardElement.value?.destroy()
  if (photoPreviewUrl.value) URL.revokeObjectURL(photoPreviewUrl.value)
})
</script>

<template>
  <div style="max-width:960px;margin:0 auto;padding:2rem 1.5rem 4rem">

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
        {{ creditsRemaining }} AI forslag igjen
      </div>
    </header>

    <!-- Soft auth hint (anonymous user after creation) -->
    <div v-if="requiresAuthHint" class="notice-gold" style="margin-bottom:2rem">
      <i class="fa-solid fa-circle-info" style="margin-top:2px;flex-shrink:0"></i>
      <span>
        <strong>Opprett konto for å godkjenne og bestille.</strong>
        Banneret ditt genereres i bakgrunnen — logg inn for å se og godkjenne resultatet.
        <RouterLink to="/register?redirect=/banner-builder/ai" style="color:var(--accent);font-weight:600">Registrer deg</RouterLink>
        eller
        <RouterLink to="/login?redirect=/banner-builder/ai" style="color:var(--accent);font-weight:600">logg inn</RouterLink>.
      </span>
    </div>

    <!-- Step indicator -->
    <nav class="step-nav" style="margin-bottom:2rem" aria-label="Steg">
      <button
        v-for="(label, idx) in ['Velg mal', 'Tilpass', 'Generer']"
        :key="idx"
        type="button"
        class="step-nav-btn"
        :class="{
          'step-active': step === idx + 1,
          'step-done': step > idx + 1,
          'step-future': step < idx + 1,
        }"
        :disabled="idx + 1 > step && genPhase === 'idle'"
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

        <!-- Portrait photo upload -->
        <div style="margin-bottom:2rem">
          <h2 class="display" style="font-size:20px;color:var(--text);margin-bottom:4px">
            Portrettfoto <span style="font-size:15px;font-weight:400;color:var(--faint)">(valgfritt)</span>
          </h2>
          <p style="font-size:14px;color:var(--muted);margin-bottom:16px">
            Last opp et bilde av personen som feires — AI-en vil inkorporere det i banneret.
          </p>

          <div v-if="photoPreviewUrl" style="display:flex;align-items:flex-start;gap:18px">
            <img :src="photoPreviewUrl" alt="Opplastet portrettfoto" style="width:120px;height:120px;object-fit:cover;border-radius:12px;border:1px solid var(--line-soft)" />
            <div style="display:flex;flex-direction:column;gap:10px;margin-top:8px">
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
              <p style="font-size:12.5px;color:var(--faint)">JPEG, PNG, WEBP – maks 10 MB</p>
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
      <div class="bb-panel" style="display:grid;gap:20px">
        <div>
          <label for="personName" class="field-label">Navn <span style="color:var(--accent)">*</span></label>
          <input id="personName" v-model="personName" type="text" maxlength="200" class="dark-input" placeholder="f.eks. Ole Petter" />
        </div>
        <div>
          <label for="personAge" class="field-label">Alder <span style="color:var(--faint);font-weight:400">(valgfritt)</span></label>
          <input id="personAge" v-model.number="personAge" type="number" min="0" max="130" class="dark-input" style="width:120px" placeholder="f.eks. 50" />
        </div>
        <div>
          <label for="textContent" class="field-label">Tekst på banneret <span style="color:var(--accent)">*</span></label>
          <textarea id="textContent" v-model="textContent" rows="3" maxlength="500" class="dark-input" style="resize:none" placeholder="f.eks. Gratulerer med 50-årsdagen!" />
          <p style="margin-top:5px;font-size:12px;color:var(--faint)">{{ textContent.length }} / 500 tegn</p>
        </div>
        <div>
          <label for="themeDescription" class="field-label">Tema / stil <span style="color:var(--accent)">*</span></label>
          <input id="themeDescription" v-model="themeDescription" type="text" maxlength="500" class="dark-input" placeholder="f.eks. Tropisk fest, lilla og gull" />
        </div>
        <div>
          <div class="field-label" style="margin-bottom:10px">Størrelsesformat</div>
          <div style="display:flex;gap:12px;flex-wrap:wrap">
            <button type="button" class="ratio-btn" :class="{ 'ratio-btn-active': aspectRatio === '16:9' }" @click="aspectRatio = '16:9'">
              <div style="font-weight:700;margin-bottom:2px">16:9 (Standard)</div>
              <div style="font-size:12px;opacity:.7">ca. 266 × 150 cm</div>
            </button>
            <button type="button" class="ratio-btn" :class="{ 'ratio-btn-active': aspectRatio === '18:9' }" @click="aspectRatio = '18:9'">
              <div style="font-weight:700;margin-bottom:2px">18:9 (Bred)</div>
              <div style="font-size:12px;opacity:.7">ca. 300 × 150 cm</div>
            </button>
          </div>
          <div class="size-preview" style="margin-top:16px">
            <div class="ratio-visual" :style="aspectRatio === '16:9' ? { width: '96px', height: '54px' } : { width: '108px', height: '54px' }">
              {{ aspectRatio }}
            </div>
            <div>
              <div style="font-size:14.5px;font-weight:700;color:var(--text)">Ca. {{ aspectDimensions.width }} × {{ aspectDimensions.height }} cm</div>
              <div style="font-size:12.5px;color:var(--faint);margin-top:2px">
                {{ aspectRatio === '16:9' ? 'Standard panoramabanner – passer de fleste anledninger' : 'Bredere format – flott for lange vegger og rekkverk' }}
              </div>
            </div>
          </div>
        </div>
      </div>

      <div style="margin-top:24px;display:flex;justify-content:space-between">
        <button type="button" class="btn btn-ghost" @click="step = 1">
          <i class="fa-solid fa-arrow-left" style="font-size:12px"></i> Tilbake
        </button>
        <button type="button" class="btn btn-primary" style="padding:12px 28px" :disabled="!step2Valid" @click="goToStep(3)">
          Neste: Generer <i class="fa-solid fa-arrow-right" style="font-size:12px"></i>
        </button>
      </div>
    </div>

    <!-- ═══════════════════════════════════════════════════════════════════
         STEP 3: Generate + results
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
              AI-en lager et unikt banner basert på informasjonen din. Første generering er <strong style="color:var(--text)">gratis</strong>.
            </p>
            <button
              type="button"
              class="btn btn-primary"
              style="width:100%;justify-content:center;padding:15px;font-size:16px;border-radius:13px"
              @click="generateBanner"
            >
              <i class="fa-solid fa-wand-magic-sparkles"></i>
              Generer banner gratis
            </button>
            <p style="font-size:12.5px;color:var(--faint);text-align:center;display:flex;align-items:center;justify-content:center;gap:6px">
              <i class="fa-solid fa-shield-halved"></i>
              Ingen betalingsinformasjon nødvendig for første generering
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
          <RouterLink to="/register?redirect=/banner-builder/ai" class="btn btn-primary" style="padding:12px 24px">
            <i class="fa-solid fa-user-plus"></i> Opprett konto
          </RouterLink>
          <RouterLink to="/login?redirect=/banner-builder/ai" class="btn btn-ghost" style="padding:12px 24px">
            Logg inn
          </RouterLink>
        </div>
        <p v-if="designRequestId" style="margin-top:20px;font-size:12px;color:var(--faint)">
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

        <!-- Approved / Final status -->
        <div
          v-if="currentDesignRequest.status === 'Approved' || currentDesignRequest.status === 'Final'"
          style="display:flex;align-items:center;gap:10px;background:rgba(74,222,128,.1);border:1px solid rgba(74,222,128,.25);border-radius:12px;padding:14px 18px;color:#4ade80;font-size:14px"
        >
          <i class="fa-solid fa-circle-check"></i>
          Banneret er godkjent og sendt til produksjon.
        </div>

        <!-- Action buttons (AwaitingApproval) -->
        <div v-if="currentDesignRequest.status === 'AwaitingApproval'" style="display:flex;gap:14px;flex-wrap:wrap">
          <button
            type="button"
            class="btn"
            style="flex:1;justify-content:center;padding:14px;font-size:15px;border-radius:12px;background:#3a9d7e;color:#fff"
            :disabled="approving"
            @click="approve"
          >
            <i v-if="approving" class="fa-solid fa-circle-notch fa-spin"></i>
            <i v-else class="fa-solid fa-circle-check"></i>
            Godkjenn og bestill
          </button>
          <button
            type="button"
            class="btn btn-ghost"
            style="flex:1;justify-content:center;padding:14px;font-size:15px;border-radius:12px"
            :disabled="regenerating"
            @click="regenerate"
          >
            <i v-if="regenerating" class="fa-solid fa-circle-notch fa-spin"></i>
            <i v-else class="fa-solid fa-rotate"></i>
            Generer ny versjon
          </button>
        </div>

        <!-- Credits badge inline -->
        <div
          v-if="auth.isLoggedIn && creditsRemaining !== null && currentDesignRequest.status === 'AwaitingApproval'"
          style="font-size:13px;color:var(--faint);text-align:center"
        >
          <i class="fa-solid fa-wand-magic-sparkles" style="color:var(--accent);margin-right:5px"></i>
          {{ creditsRemaining }} AI forslag igjen
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
                  <span style="font-size:12px;font-weight:600;color:var(--text);text-align:center;line-height:1.3">
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
              <p style="margin-top:4px;font-size:12px;color:var(--faint)">{{ textContent.length }} / 500 tegn</p>
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
              <!-- Option 1: Buy credit pack -->
              <button
                type="button"
                class="paywall-option paywall-option-primary"
                @click="startCreditPackPurchase"
              >
                <div style="display:flex;align-items:flex-start;gap:14px">
                  <span class="paywall-option-ico" style="background:rgba(255,106,61,.15);color:var(--accent)">
                    <i class="fa-solid fa-bag-shopping"></i>
                  </span>
                  <div style="text-align:left">
                    <div style="font-weight:700;font-size:15px;color:var(--text)">
                      Kjøp pakke med {{ effectivePaywallOptions.creditPackCount }} AI banner forslag
                      <span style="color:var(--accent)">({{ formatNok(effectivePaywallOptions.creditPackPriceNok) }})</span>
                    </div>
                    <div style="font-size:13px;color:var(--muted);margin-top:3px">
                      Kortbetaling via Stripe — umiddelbar tilgang
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
                      Legg inn bestillingen nå
                      <span style="color:#4ade80">({{ effectivePaywallOptions.bannerOrderActivationFeeNok }} ytterligere forslag inkludert)</span>
                    </div>
                    <div style="font-size:13px;color:var(--muted);margin-top:3px">
                      Gå til kassen med ditt nåværende design
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
            <p style="font-size:12px;color:var(--faint);text-align:center;margin-top:10px">
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
  font-size: 12px;
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
  font-size: 11.5px;
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
</style>

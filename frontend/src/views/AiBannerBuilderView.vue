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
} from '@/api/designRequests'

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

// ── Step 3: Review + Stripe payment ──────────────────────────────────────────
const stripeRef = ref<Stripe | null>(null)
const cardElement = ref<StripeCardElement | null>(null)
const cardMountEl = ref<HTMLDivElement | null>(null)
const stripeReady = ref(false)
const stripeError = ref<string | null>(null)
const stripeCardError = ref<string | null>(null)

const processingPayment = ref(false)
const paymentApiError = ref<string | null>(null)

// Post-payment generation state
type GenPhase = 'idle' | 'generating' | 'ready' | 'error'
const genPhase = ref<GenPhase>('idle')
const currentDesignRequest = ref<DesignRequestDetail | null>(null)
const designRequestId = ref<number | null>(null)
const approveError = ref<string | null>(null)
const approving = ref(false)
const regenerating = ref(false)
const regenerateError = ref<string | null>(null)

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

const step1Valid = computed(
  () => selectedTemplateId.value !== null,
)

const step2Valid = computed(
  () =>
    personName.value.trim().length > 0 &&
    textContent.value.trim().length > 0 &&
    themeDescription.value.trim().length > 0,
)

// ── Category icons (FontAwesome) ──────────────────────────────────────────────
const categoryIconClass: Record<string, string> = {
  Birthday: 'fa-cake-candles',
  Confirmation: 'fa-graduation-cap',
  Wedding: 'fa-ring',
  Anniversary: 'fa-champagne-glasses',
  Christmas: 'fa-tree',
  NewYear: 'fa-champagne-glasses',
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

  // Create a local preview URL immediately
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
    // Revoke on error
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
  if (s === 3) {
    // Init Stripe when entering step 3 (card not yet mounted — watch cardMountEl)
    void initStripe()
  }
}

// ── Stripe ────────────────────────────────────────────────────────────────────
async function initStripe() {
  if (stripeRef.value) return // already initialised
  const key = import.meta.env.VITE_STRIPE_PUBLISHABLE_KEY as string | undefined
  if (!key || key.startsWith('pk_test_REPLACE')) {
    stripeError.value =
      'Stripe er ikke konfigurert (mangler VITE_STRIPE_PUBLISHABLE_KEY). ' +
      'Betalingsfunksjonen er ikke tilgjengelig i dette testmiljøet.'
    return
  }

  try {
    const stripe = await loadStripe(key)
    if (!stripe) {
      stripeError.value = 'Stripe kunne ikke lastes. Prøv igjen.'
      return
    }
    stripeRef.value = stripe

    const elements = stripe.elements()
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
    cardElement.value = card

    card.on('change', (event) => {
      stripeCardError.value = event.error?.message ?? null
    })

    // Mount as soon as the DOM element is available
    if (cardMountEl.value) {
      card.mount(cardMountEl.value)
      stripeReady.value = true
    }
  } catch (err) {
    stripeError.value = 'Stripe kunne ikke initialiseres.'
    console.error('Stripe init error:', err)
  }
}

// Mount card when ref becomes available (happens after step 3 renders)
watch(cardMountEl, (el) => {
  if (el && cardElement.value && !stripeReady.value) {
    cardElement.value.mount(el)
    stripeReady.value = true
  }
})

// ── Payment ───────────────────────────────────────────────────────────────────
async function pay() {
  if (processingPayment.value) return
  paymentApiError.value = null
  stripeCardError.value = null
  processingPayment.value = true

  let reqId: number
  let clientSecret: string

  try {
    const resp = await createAiRequest({
      templateId: selectedTemplateId.value!,
      language: language.value,
      personName: personName.value.trim(),
      personAge: personAge.value ?? undefined,
      textContent: textContent.value.trim(),
      themeDescription: themeDescription.value.trim(),
      aspectRatio: aspectRatio.value,
      uploadedPhotoBannerDesignId: uploadedPhotoBannerDesignId.value ?? undefined,
    })
    reqId = resp.designRequestId
    clientSecret = resp.clientSecret
    designRequestId.value = reqId
  } catch (e: unknown) {
    const ex = e as { response?: { data?: { error?: string } }; message?: string }
    paymentApiError.value =
      ex.response?.data?.error || ex.message || 'Kunne ikke opprette bestilling. Prøv igjen.'
    processingPayment.value = false
    return
  }

  // Dev/mock bypass
  if (clientSecret.startsWith('pi_mock_')) {
    processingPayment.value = false
    startPolling(reqId)
    return
  }

  if (!stripeRef.value || !cardElement.value) {
    paymentApiError.value = 'Stripe er ikke initialisert. Last siden på nytt.'
    processingPayment.value = false
    return
  }

  const { error } = await stripeRef.value.confirmCardPayment(clientSecret, {
    payment_method: { card: cardElement.value },
  })

  if (error) {
    stripeCardError.value = error.message ?? 'Betalingen feilet. Prøv igjen.'
    processingPayment.value = false
    return
  }

  processingPayment.value = false
  startPolling(reqId)
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
      if (detail.status === 'AwaitingApproval' || detail.status === 'Approved' || detail.status === 'Final') {
        genPhase.value = 'ready'
      } else {
        genPhase.value = 'error'
      }
    }
  } catch {
    // Keep polling on transient errors
  }
}

// ── Approve ───────────────────────────────────────────────────────────────────
async function approve() {
  if (!designRequestId.value || approving.value) return
  approveError.value = null
  approving.value = true
  try {
    await approveDesignRequest(designRequestId.value)
    // Refresh detail
    currentDesignRequest.value = await getDesignRequest(designRequestId.value)
    // Navigate to account or show success message
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
    await regenerateDesignRequest(designRequestId.value)
    // Re-enter polling state
    genPhase.value = 'generating'
    currentDesignRequest.value = null
    startPolling(designRequestId.value)
  } catch (e: unknown) {
    const ex = e as { response?: { data?: { error?: string } }; message?: string }
    regenerateError.value =
      ex.response?.data?.error || ex.message || 'Ny generering feilet. Prøv igjen.'
  } finally {
    regenerating.value = false
  }
}

// ── Utils ─────────────────────────────────────────────────────────────────────
function formatNok(n: number | null | undefined): string {
  if (n == null) return '–'
  return new Intl.NumberFormat('nb-NO', { maximumFractionDigits: 0 }).format(n) + ' kr'
}

// ── Lifecycle ─────────────────────────────────────────────────────────────────
onMounted(loadTemplates)

onBeforeUnmount(() => {
  stopPolling()
  cardElement.value?.unmount()
  if (photoPreviewUrl.value) URL.revokeObjectURL(photoPreviewUrl.value)
})
</script>

<template>
  <div style="max-width:960px;margin:0 auto;padding:2rem 1.5rem 4rem">
    <!-- Header -->
    <header style="margin-bottom:2.5rem;text-align:center">
      <h1 class="display" style="font-size:clamp(28px,4vw,44px);color:var(--text);margin-bottom:12px">
        AI-generert feiringsbanner
      </h1>
      <p style="font-size:18px;color:var(--muted);max-width:36em;margin:0 auto">
        Fortell oss om feiringen — vi lager et unikt banner med kunstig intelligens på under ett minutt.
        <strong style="color:var(--text)">95 kr</strong>.
      </p>
    </header>

    <!-- Auth guard -->
    <div v-if="!auth.isLoggedIn" class="notice-gold" style="margin-bottom:2rem">
      <i class="fa-solid fa-circle-info"></i>
      <span>
        <strong>Du må være innlogget</strong> for å bestille et AI-banner.
        <RouterLink to="/login?redirect=/banner-builder/ai" style="color:var(--accent);font-weight:600">Logg inn</RouterLink>
        eller
        <RouterLink to="/register" style="color:var(--accent);font-weight:600">registrer deg</RouterLink>
        for å fortsette.
      </span>
    </div>

    <!-- Step indicator -->
    <nav class="step-nav" style="margin-bottom:2rem" aria-label="Steg">
      <button
        v-for="(label, idx) in ['Velg mal', 'Tilpass', 'Betal']"
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
         STEP 1: Choose template + upload photo + language
    ════════════════════════════════════════════════════════════════════════ -->
    <div v-if="step === 1">
      <!-- Loading templates -->
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
          <button
            type="button"
            class="lang-btn"
            :class="{ 'lang-btn-active': language === 'nb' }"
            @click="language = 'nb'"
          >
            🇳🇴 Norsk
          </button>
          <button
            type="button"
            class="lang-btn"
            :class="{ 'lang-btn-active': language === 'en' }"
            @click="language = 'en'"
          >
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
            <img
              :src="photoPreviewUrl"
              alt="Opplastet portrettfoto"
              style="width:120px;height:120px;object-fit:cover;border-radius:12px;border:1px solid var(--line-soft)"
            />
            <div style="display:flex;flex-direction:column;gap:10px;margin-top:8px">
              <span style="font-size:14px;color:#4ade80;font-weight:600;display:flex;align-items:center;gap:7px">
                <i class="fa-solid fa-circle-check"></i> Foto lastet opp
              </span>
              <button
                type="button"
                style="font-size:13.5px;color:var(--accent);font-weight:600;background:none;border:none;cursor:pointer;padding:0;text-align:left"
                @click="removePhoto"
              >
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
              <input
                ref="photoFileInput"
                type="file"
                style="display:none"
                accept="image/jpeg,image/png,image/webp"
                @change="onPhotoFileChange"
              />
              <i class="fa-solid fa-user-circle" style="font-size:36px;color:var(--faint);margin-bottom:10px"></i>
              <p style="font-size:14px;font-weight:600;color:var(--text);margin-bottom:4px">
                Slipp bilde her, eller klikk for å velge
              </p>
              <p style="font-size:12.5px;color:var(--faint)">JPEG, PNG, WEBP – maks 10 MB</p>

              <!-- Upload progress overlay -->
              <div v-if="photoUploading" class="upload-overlay">
                <div style="width:66%;max-width:260px">
                  <div style="font-size:14px;font-weight:600;color:var(--text);text-align:center;margin-bottom:10px">
                    Laster opp… {{ photoUploadProgress }}%
                  </div>
                  <div style="width:100%;height:6px;background:var(--line);border-radius:999px;overflow:hidden">
                    <div
                      style="height:100%;background:var(--accent);border-radius:999px;transition:width .2s"
                      :style="{ width: `${photoUploadProgress}%` }"
                    />
                  </div>
                </div>
              </div>
            </div>
            <div v-if="photoUploadError" class="error-box" style="margin-top:10px">
              <i class="fa-solid fa-circle-exclamation"></i> {{ photoUploadError }}
            </div>
          </div>
        </div>

        <!-- Next button -->
        <div style="display:flex;justify-content:flex-end">
          <button
            type="button"
            class="btn btn-primary"
            style="padding:12px 28px;font-size:15px"
            :disabled="!step1Valid"
            @click="goToStep(2)"
          >
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
        <!-- Person name -->
        <div>
          <label for="personName" class="field-label">
            Navn <span style="color:var(--accent)">*</span>
          </label>
          <input
            id="personName"
            v-model="personName"
            type="text"
            maxlength="200"
            class="dark-input"
            placeholder="f.eks. Ole Petter"
          />
        </div>

        <!-- Age -->
        <div>
          <label for="personAge" class="field-label">
            Alder <span style="color:var(--faint);font-weight:400">(valgfritt)</span>
          </label>
          <input
            id="personAge"
            v-model.number="personAge"
            type="number"
            min="0"
            max="130"
            class="dark-input"
            style="width:120px"
            placeholder="f.eks. 50"
          />
        </div>

        <!-- Banner text -->
        <div>
          <label for="textContent" class="field-label">
            Tekst på banneret <span style="color:var(--accent)">*</span>
          </label>
          <textarea
            id="textContent"
            v-model="textContent"
            rows="3"
            maxlength="500"
            class="dark-input"
            style="resize:none"
            placeholder="f.eks. Gratulerer med 50-årsdagen!"
          />
          <p style="margin-top:5px;font-size:12px;color:var(--faint)">{{ textContent.length }} / 500 tegn</p>
        </div>

        <!-- Theme description -->
        <div>
          <label for="themeDescription" class="field-label">
            Tema / stil <span style="color:var(--accent)">*</span>
          </label>
          <input
            id="themeDescription"
            v-model="themeDescription"
            type="text"
            maxlength="500"
            class="dark-input"
            placeholder="f.eks. Tropisk fest, lilla og gull"
          />
        </div>

        <!-- Aspect ratio -->
        <div>
          <div class="field-label" style="margin-bottom:10px">Størrelsesformat</div>
          <div style="display:flex;gap:12px;flex-wrap:wrap">
            <button
              type="button"
              class="ratio-btn"
              :class="{ 'ratio-btn-active': aspectRatio === '16:9' }"
              @click="aspectRatio = '16:9'"
            >
              <div style="font-weight:700;margin-bottom:2px">16:9 (Standard)</div>
              <div style="font-size:12px;opacity:.7">ca. 266 × 150 cm</div>
            </button>
            <button
              type="button"
              class="ratio-btn"
              :class="{ 'ratio-btn-active': aspectRatio === '18:9' }"
              @click="aspectRatio = '18:9'"
            >
              <div style="font-weight:700;margin-bottom:2px">18:9 (Bred)</div>
              <div style="font-size:12px;opacity:.7">ca. 300 × 150 cm</div>
            </button>
          </div>

          <!-- Size preview -->
          <div class="size-preview" style="margin-top:16px">
            <div
              class="ratio-visual"
              :style="aspectRatio === '16:9'
                ? { width: '96px', height: '54px' }
                : { width: '108px', height: '54px' }"
            >
              {{ aspectRatio }}
            </div>
            <div>
              <div style="font-size:14.5px;font-weight:700;color:var(--text)">
                Ca. {{ aspectDimensions.width }} × {{ aspectDimensions.height }} cm
              </div>
              <div style="font-size:12.5px;color:var(--faint);margin-top:2px">
                {{ aspectRatio === '16:9'
                  ? 'Standard panoramabanner – passer de fleste anledninger'
                  : 'Bredere format – flott for lange vegger og rekkverk'
                }}
              </div>
            </div>
          </div>
        </div>
      </div>

      <!-- Nav buttons -->
      <div style="margin-top:24px;display:flex;justify-content:space-between">
        <button type="button" class="btn btn-ghost" @click="step = 1">
          <i class="fa-solid fa-arrow-left" style="font-size:12px"></i> Tilbake
        </button>
        <button
          type="button"
          class="btn btn-primary"
          style="padding:12px 28px"
          :disabled="!step2Valid"
          @click="goToStep(3)"
        >
          Neste: Se over og betal <i class="fa-solid fa-arrow-right" style="font-size:12px"></i>
        </button>
      </div>
    </div>

    <!-- ═══════════════════════════════════════════════════════════════════
         STEP 3: Review + payment + generation
    ════════════════════════════════════════════════════════════════════════ -->
    <div v-else-if="step === 3">

      <!-- ── Phase: idle / payment form ──────────────────────────────────── -->
      <div v-if="genPhase === 'idle'" style="display:grid;grid-template-columns:1.2fr .8fr;gap:24px" class="pay-grid">

        <!-- Left: Stripe card + pay button -->
        <div style="display:grid;gap:20px">

          <!-- Stripe not configured -->
          <div v-if="stripeError" class="notice-gold">
            <i class="fa-solid fa-triangle-exclamation"></i>
            <span><strong>Stripe ikke tilgjengelig:</strong> {{ stripeError }}</span>
          </div>

          <template v-else>
            <section class="bb-panel">
              <h2 class="display" style="font-size:18px;color:var(--text);margin-bottom:18px;display:flex;align-items:center;gap:10px">
                <i class="fa-solid fa-credit-card" style="color:var(--accent)"></i>
                Kortbetaling
                <span style="font-size:12px;font-weight:400;color:var(--faint);background:var(--surface-2);border:1px solid var(--line);padding:3px 10px;border-radius:6px;font-family:var(--font-ui)">
                  Sikret av Stripe
                </span>
              </h2>

              <label class="field-label" style="margin-bottom:8px">Kortdetaljer</label>
              <div
                ref="cardMountEl"
                class="stripe-mount"
              />
              <p v-if="stripeCardError" class="error-box" style="margin-top:10px">
                <i class="fa-solid fa-circle-exclamation"></i> {{ stripeCardError }}
              </p>
            </section>

            <div v-if="paymentApiError" class="error-box">
              <i class="fa-solid fa-circle-exclamation"></i> {{ paymentApiError }}
            </div>

            <button
              type="button"
              class="btn btn-primary"
              style="width:100%;justify-content:center;padding:15px;font-size:16px;border-radius:13px"
              :disabled="processingPayment || !stripeReady"
              @click="pay"
            >
              <i v-if="processingPayment" class="fa-solid fa-circle-notch fa-spin"></i>
              <i v-else class="fa-solid fa-wand-magic-sparkles"></i>
              {{ processingPayment ? 'Behandler…' : 'Generer og betal 95 kr' }}
            </button>

            <p style="font-size:12.5px;color:var(--faint);text-align:center;display:flex;align-items:center;justify-content:center;gap:6px">
              <i class="fa-solid fa-lock" style="color:var(--faint)"></i>
              Betalingen er kryptert og håndteres av Stripe. Vi lagrer ikke kortinformasjon.
            </p>
          </template>
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
                  <img
                    v-if="photoPreviewUrl"
                    :src="photoPreviewUrl"
                    style="width:38px;height:38px;object-fit:cover;border-radius:8px;border:1px solid var(--line-soft)"
                    alt="Portrettfoto"
                  />
                  <span><i class="fa-solid fa-circle-check" style="color:#4ade80"></i> Lastet opp</span>
                </dd>
              </div>
              <div>
                <dt class="field-label" style="margin-bottom:3px">Språk</dt>
                <dd style="color:var(--text)">{{ language === 'nb' ? '🇳🇴 Norsk' : '🇬🇧 English' }}</dd>
              </div>
            </dl>

            <div style="border-top:1px solid var(--line-soft);padding-top:14px;display:flex;justify-content:space-between;align-items:center">
              <span style="font-weight:700;color:var(--text)">Totalt</span>
              <span style="font-weight:800;color:var(--accent);font-size:20px;font-family:var(--font-display)">95 kr</span>
            </div>
          </div>
        </aside>
      </div>

      <!-- ── Phase: generating (polling) ────────────────────────────────── -->
      <div v-else-if="genPhase === 'generating'" style="text-align:center;padding:5rem 0">
        <div style="display:inline-flex;flex-direction:column;align-items:center;gap:24px">
          <!-- Spinner -->
          <div style="position:relative;width:72px;height:72px">
            <div style="position:absolute;inset:0;border-radius:50%;border:4px solid var(--surface-2)"></div>
            <div style="position:absolute;inset:0;border-radius:50%;border:4px solid transparent;border-top-color:var(--accent);animation:spin 1s linear infinite"></div>
          </div>
          <div>
            <h2 class="display" style="font-size:26px;color:var(--text);margin-bottom:8px">Genererer banner…</h2>
            <p style="color:var(--muted);max-width:28em">
              AI-en jobber med designet ditt. Dette tar vanligvis 20–60 sekunder.
            </p>
          </div>
          <!-- Progress steps -->
          <div style="display:grid;gap:10px;width:240px;text-align:left">
            <div style="display:flex;align-items:center;gap:10px;font-size:14px;color:var(--muted)">
              <span style="width:18px;height:18px;border-radius:50%;background:var(--accent);display:grid;place-items:center;flex-shrink:0">
                <i class="fa-solid fa-check" style="font-size:9px;color:var(--accent-ink)"></i>
              </span>
              Betaling bekreftet
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

      <!-- ── Phase: ready (preview available) ──────────────────────────── -->
      <div v-else-if="genPhase === 'ready' && currentDesignRequest" style="display:grid;gap:24px">
        <div style="text-align:center">
          <h2 class="display" style="font-size:28px;color:var(--text);margin-bottom:8px">
            <i class="fa-solid fa-party-horn" style="color:var(--accent);margin-right:8px"></i>
            Banneret ditt er klart!
          </h2>
          <p style="color:var(--muted)">Se over designet og godkjenn, eller be om en ny generering.</p>
        </div>

        <!-- Preview image -->
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

        <!-- Status info -->
        <div
          v-if="currentDesignRequest.status === 'Approved' || currentDesignRequest.status === 'Final'"
          style="display:flex;align-items:center;gap:10px;background:rgba(74,222,128,.1);border:1px solid rgba(74,222,128,.25);border-radius:12px;padding:14px 18px;color:#4ade80;font-size:14px"
        >
          <i class="fa-solid fa-circle-check"></i>
          Banneret er godkjent og sendt til produksjon.
        </div>

        <!-- Action buttons (when AwaitingApproval) -->
        <div
          v-if="currentDesignRequest.status === 'AwaitingApproval'"
          style="display:flex;gap:14px;flex-wrap:wrap"
        >
          <button
            type="button"
            class="btn"
            style="flex:1;justify-content:center;padding:14px;font-size:15px;border-radius:12px;background:#3a9d7e;color:#fff"
            :disabled="approving"
            @click="approve"
          >
            <i v-if="approving" class="fa-solid fa-circle-notch fa-spin"></i>
            <i v-else class="fa-solid fa-circle-check"></i>
            Godkjenn
          </button>
          <button
            v-if="currentDesignRequest.regenerationsRemaining > 0"
            type="button"
            class="btn btn-ghost"
            style="flex:1;justify-content:center;padding:14px;font-size:15px;border-radius:12px"
            :disabled="regenerating"
            @click="regenerate"
          >
            <i v-if="regenerating" class="fa-solid fa-circle-notch fa-spin"></i>
            <i v-else class="fa-solid fa-rotate"></i>
            Prøv igjen
          </button>
        </div>

        <div v-if="approveError" class="error-box">
          <i class="fa-solid fa-circle-exclamation"></i> {{ approveError }}
        </div>
        <div v-if="regenerateError" class="error-box">
          <i class="fa-solid fa-circle-exclamation"></i> {{ regenerateError }}
        </div>
      </div>

      <!-- ── Phase: error ────────────────────────────────────────────────── -->
      <div v-else-if="genPhase === 'error'" style="text-align:center;padding:4rem 0">
        <i class="fa-solid fa-triangle-exclamation" style="font-size:52px;color:var(--accent);margin-bottom:18px;display:block"></i>
        <h2 class="display" style="font-size:26px;color:var(--text);margin-bottom:10px">Noe gikk galt</h2>
        <p style="color:var(--muted);margin-bottom:24px;max-width:30em;margin-left:auto;margin-right:auto">
          {{ currentDesignRequest?.lastError ?? 'AI-genereringen feilet. Vennligst kontakt support.' }}
        </p>
        <RouterLink to="/account" class="btn btn-primary">
          <i class="fa-solid fa-house"></i> Gå til min konto
        </RouterLink>
      </div>

      <!-- Back button (only when in idle phase) -->
      <div v-if="genPhase === 'idle'" style="margin-top:24px">
        <button type="button" class="btn btn-ghost" @click="step = 2">
          <i class="fa-solid fa-arrow-left" style="font-size:12px"></i> Tilbake
        </button>
      </div>
    </div>
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
@media (max-width: 600px) { .tpl-grid { grid-template-columns: repeat(2, 1fr); } }

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
.tpl-card:hover {
  border-color: var(--line);
  transform: translateY(-2px);
}
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

/* ── Responsive ──────────────────────────────────────────────── */
.pay-grid { grid-template-columns: 1.2fr .8fr; }
@media (max-width: 768px) {
  .pay-grid { grid-template-columns: 1fr !important; }
}
</style>

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

// ── Category emojis ───────────────────────────────────────────────────────────
const categoryEmoji: Record<string, string> = {
  Birthday: '🎂',
  Confirmation: '⛪',
  Wedding: '💍',
  Anniversary: '🎉',
  Christmas: '🎄',
  NewYear: '🥂',
  Other: '🎊',
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
          fontFamily: 'Inter, ui-sans-serif, system-ui, sans-serif',
          fontSize: '16px',
          color: '#111827',
          '::placeholder': { color: '#9ca3af' },
        },
        invalid: { color: '#ef4444' },
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
  <div class="max-w-4xl mx-auto px-4 py-8 sm:py-12">
    <!-- Header -->
    <header class="mb-8 text-center">
      <h1 class="text-3xl sm:text-4xl font-bold text-gray-900 mb-3">
        AI-generert feiringsbanner
      </h1>
      <p class="text-lg text-gray-600 max-w-2xl mx-auto">
        Fortell oss om feiringen — vi lager et unikt banner med kunstig intelligens på under ett minutt.
        <strong class="text-gray-900">95 kr</strong>.
      </p>
    </header>

    <!-- Auth guard -->
    <div
      v-if="!auth.isLoggedIn"
      class="mb-8 bg-amber-50 border border-amber-200 rounded-xl px-5 py-4 text-sm text-amber-900"
    >
      <strong>Du må være innlogget</strong> for å bestille et AI-banner.
      <RouterLink
        to="/login?redirect=/banner-builder/ai"
        class="underline font-medium ml-1"
      >
        Logg inn
      </RouterLink>
      eller
      <RouterLink to="/register" class="underline font-medium">
        registrer deg
      </RouterLink>
      for å fortsette.
    </div>

    <!-- Step indicator -->
    <nav class="mb-8 flex items-center gap-2 sm:gap-4" aria-label="Steg">
      <button
        v-for="(label, idx) in ['Velg mal', 'Tilpass', 'Betal']"
        :key="idx"
        type="button"
        class="flex items-center gap-2 text-sm font-medium transition"
        :class="step === idx + 1
          ? 'text-blue-700'
          : step > idx + 1
            ? 'text-gray-500 hover:text-blue-600 cursor-pointer'
            : 'text-gray-400 cursor-default'"
        :disabled="idx + 1 > step"
        @click="idx + 1 < step ? (step = (idx + 1) as 1 | 2 | 3) : undefined"
      >
        <span
          class="w-7 h-7 rounded-full flex items-center justify-center text-xs font-bold shrink-0"
          :class="step > idx + 1
            ? 'bg-green-600 text-white'
            : step === idx + 1
              ? 'bg-blue-700 text-white'
              : 'bg-gray-200 text-gray-500'"
        >
          <svg
            v-if="step > idx + 1"
            class="w-4 h-4"
            fill="none"
            viewBox="0 0 24 24"
            stroke="currentColor"
            stroke-width="3"
          >
            <path stroke-linecap="round" stroke-linejoin="round" d="M5 13l4 4L19 7" />
          </svg>
          <span v-else>{{ idx + 1 }}</span>
        </span>
        <span class="hidden sm:inline">{{ label }}</span>
      </button>
    </nav>

    <!-- ═══════════════════════════════════════════════════════════════════
         STEP 1: Choose template + upload photo + language
    ════════════════════════════════════════════════════════════════════════ -->
    <div v-if="step === 1">
      <!-- Loading templates -->
      <div v-if="templatesLoading" class="text-center text-gray-500 py-12">
        Laster maler…
      </div>
      <div
        v-else-if="templatesError"
        class="bg-red-50 border border-red-200 text-red-800 rounded-xl p-6 text-center"
      >
        {{ templatesError }}
        <button class="mt-3 underline" @click="loadTemplates">Prøv igjen</button>
      </div>
      <template v-else>
        <!-- Language toggle -->
        <div class="mb-6 flex items-center gap-3">
          <span class="text-sm font-medium text-gray-700">Språk:</span>
          <button
            type="button"
            class="border-2 rounded-lg px-4 py-1.5 text-sm font-medium transition"
            :class="language === 'nb'
              ? 'border-blue-700 bg-blue-50 text-blue-800'
              : 'border-gray-200 text-gray-600 hover:border-gray-300'"
            @click="language = 'nb'"
          >
            🇳🇴 Norsk
          </button>
          <button
            type="button"
            class="border-2 rounded-lg px-4 py-1.5 text-sm font-medium transition"
            :class="language === 'en'
              ? 'border-blue-700 bg-blue-50 text-blue-800'
              : 'border-gray-200 text-gray-600 hover:border-gray-300'"
            @click="language = 'en'"
          >
            🇬🇧 English
          </button>
        </div>

        <!-- Template grid -->
        <div class="mb-8">
          <h2 class="text-xl font-semibold text-gray-900 mb-4">Velg feiringsmal</h2>
          <div class="grid grid-cols-2 sm:grid-cols-3 md:grid-cols-4 gap-3">
            <button
              v-for="t in templates"
              :key="t.id"
              type="button"
              class="flex flex-col items-center gap-2 border-2 rounded-xl p-4 transition hover:shadow-md"
              :class="selectedTemplateId === t.id
                ? 'border-blue-700 ring-2 ring-blue-200 bg-blue-50 shadow-md'
                : 'border-gray-200 bg-white'"
              @click="selectedTemplateId = t.id"
            >
              <span class="text-4xl leading-none">{{ categoryEmoji[t.category] ?? '🎊' }}</span>
              <span class="text-sm font-semibold text-gray-900 text-center leading-tight">
                {{ language === 'en' ? t.nameEn : t.nameNb }}
              </span>
            </button>
          </div>
        </div>

        <!-- Portrait photo upload -->
        <div class="mb-8">
          <h2 class="text-xl font-semibold text-gray-900 mb-1">
            Portrettfoto <span class="text-base font-normal text-gray-500">(valgfritt)</span>
          </h2>
          <p class="text-sm text-gray-600 mb-4">
            Last opp et bilde av personen som feires — AI-en vil inkorporere det i banneret.
          </p>

          <div v-if="photoPreviewUrl" class="flex items-start gap-4">
            <img
              :src="photoPreviewUrl"
              alt="Opplastet portrettfoto"
              class="w-32 h-32 object-cover rounded-xl border border-gray-200 shadow-sm"
            />
            <div class="flex flex-col gap-2 mt-2">
              <span class="text-sm text-green-700 font-medium flex items-center gap-1">
                <svg class="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" stroke-width="2.5">
                  <path stroke-linecap="round" stroke-linejoin="round" d="M5 13l4 4L19 7" />
                </svg>
                Foto lastet opp
              </span>
              <button
                type="button"
                class="text-sm text-red-600 hover:text-red-800 underline"
                @click="removePhoto"
              >
                Fjern foto
              </button>
            </div>
          </div>

          <div v-else>
            <div
              role="button"
              tabindex="0"
              class="relative w-full rounded-xl border-2 border-dashed transition cursor-pointer select-none flex flex-col items-center justify-center text-center px-6 py-8"
              :class="[
                photoDragging
                  ? 'border-blue-600 bg-blue-50'
                  : 'border-gray-300 bg-gray-50 hover:bg-gray-100 hover:border-gray-400',
                photoUploading ? 'opacity-60 cursor-progress' : '',
              ]"
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
                class="hidden"
                accept="image/jpeg,image/png,image/webp"
                @change="onPhotoFileChange"
              />
              <svg
                class="w-10 h-10 text-gray-400 mb-2"
                fill="none"
                viewBox="0 0 24 24"
                stroke="currentColor"
              >
                <path
                  stroke-linecap="round"
                  stroke-linejoin="round"
                  stroke-width="1.5"
                  d="M16 7a4 4 0 11-8 0 4 4 0 018 0zM12 14a7 7 0 00-7 7h14a7 7 0 00-7-7z"
                />
              </svg>
              <p class="text-sm font-semibold text-gray-900 mb-0.5">
                Slipp bilde her, eller klikk for å velge
              </p>
              <p class="text-xs text-gray-500">JPEG, PNG, WEBP – maks 10 MB</p>

              <!-- Upload progress overlay -->
              <div
                v-if="photoUploading"
                class="absolute inset-0 flex flex-col items-center justify-center bg-white/80 rounded-xl"
              >
                <div class="w-2/3 max-w-xs">
                  <div class="text-sm font-medium text-gray-800 text-center mb-2">
                    Laster opp… {{ photoUploadProgress }}%
                  </div>
                  <div class="w-full h-2 bg-gray-200 rounded-full overflow-hidden">
                    <div
                      class="h-full bg-blue-600 transition-all"
                      :style="{ width: `${photoUploadProgress}%` }"
                    />
                  </div>
                </div>
              </div>
            </div>
            <p
              v-if="photoUploadError"
              class="mt-2 text-sm text-red-700 bg-red-50 border border-red-200 rounded-lg px-3 py-2"
            >
              {{ photoUploadError }}
            </p>
          </div>
        </div>

        <!-- Next button -->
        <div class="flex justify-end">
          <button
            type="button"
            class="bg-blue-700 hover:bg-blue-800 disabled:bg-gray-300 disabled:cursor-not-allowed text-white font-semibold px-8 py-3 rounded-lg transition"
            :disabled="!step1Valid"
            @click="goToStep(2)"
          >
            Neste: Tilpass →
          </button>
        </div>
      </template>
    </div>

    <!-- ═══════════════════════════════════════════════════════════════════
         STEP 2: Personalize
    ════════════════════════════════════════════════════════════════════════ -->
    <div v-else-if="step === 2">
      <div class="bg-white border border-gray-200 rounded-xl p-6 sm:p-8 space-y-5">
        <!-- Person name -->
        <div>
          <label for="personName" class="block text-sm font-semibold text-gray-800 mb-1">
            Navn <span class="text-red-500">*</span>
          </label>
          <input
            id="personName"
            v-model="personName"
            type="text"
            maxlength="200"
            class="w-full border border-gray-300 rounded-lg px-3 py-2.5 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500 focus:border-blue-500"
            placeholder="f.eks. Ole Petter"
          />
        </div>

        <!-- Age -->
        <div>
          <label for="personAge" class="block text-sm font-semibold text-gray-800 mb-1">
            Alder <span class="text-gray-400 font-normal">(valgfritt)</span>
          </label>
          <input
            id="personAge"
            v-model.number="personAge"
            type="number"
            min="0"
            max="130"
            class="w-32 border border-gray-300 rounded-lg px-3 py-2.5 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500 focus:border-blue-500"
            placeholder="f.eks. 50"
          />
        </div>

        <!-- Banner text -->
        <div>
          <label for="textContent" class="block text-sm font-semibold text-gray-800 mb-1">
            Tekst på banneret <span class="text-red-500">*</span>
          </label>
          <textarea
            id="textContent"
            v-model="textContent"
            rows="3"
            maxlength="500"
            class="w-full border border-gray-300 rounded-lg px-3 py-2.5 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500 focus:border-blue-500 resize-none"
            placeholder="f.eks. Gratulerer med 50-årsdagen!"
          />
          <p class="mt-1 text-xs text-gray-500">{{ textContent.length }} / 500 tegn</p>
        </div>

        <!-- Theme description -->
        <div>
          <label for="themeDescription" class="block text-sm font-semibold text-gray-800 mb-1">
            Tema / stil <span class="text-red-500">*</span>
          </label>
          <input
            id="themeDescription"
            v-model="themeDescription"
            type="text"
            maxlength="500"
            class="w-full border border-gray-300 rounded-lg px-3 py-2.5 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500 focus:border-blue-500"
            placeholder="f.eks. Tropisk fest, lilla og gull"
          />
        </div>

        <!-- Aspect ratio -->
        <div>
          <div class="text-sm font-semibold text-gray-800 mb-2">Størrelsesformat</div>
          <div class="flex gap-3">
            <button
              type="button"
              class="border-2 rounded-xl px-5 py-3 text-sm font-medium transition flex-1 sm:flex-none"
              :class="aspectRatio === '16:9'
                ? 'border-blue-700 bg-blue-50 text-blue-800'
                : 'border-gray-200 text-gray-700 hover:border-gray-300'"
              @click="aspectRatio = '16:9'"
            >
              <div class="font-semibold">16:9 (Standard)</div>
              <div class="text-xs mt-0.5 opacity-75">ca. 266 × 150 cm</div>
            </button>
            <button
              type="button"
              class="border-2 rounded-xl px-5 py-3 text-sm font-medium transition flex-1 sm:flex-none"
              :class="aspectRatio === '18:9'
                ? 'border-blue-700 bg-blue-50 text-blue-800'
                : 'border-gray-200 text-gray-700 hover:border-gray-300'"
              @click="aspectRatio = '18:9'"
            >
              <div class="font-semibold">18:9 (Bred)</div>
              <div class="text-xs mt-0.5 opacity-75">ca. 300 × 150 cm</div>
            </button>
          </div>

          <!-- Size preview -->
          <div class="mt-4 bg-gray-50 border border-gray-200 rounded-xl p-4 flex items-center gap-4">
            <!-- Visual ratio preview -->
            <div
              class="border-2 border-blue-400 bg-blue-50 rounded shrink-0 flex items-center justify-center text-xs text-blue-600 font-medium"
              :style="aspectRatio === '16:9'
                ? { width: '96px', height: '54px' }
                : { width: '108px', height: '54px' }"
            >
              {{ aspectRatio }}
            </div>
            <div>
              <div class="text-sm font-semibold text-gray-900">
                Ca. {{ aspectDimensions.width }} × {{ aspectDimensions.height }} cm
              </div>
              <div class="text-xs text-gray-500 mt-0.5">
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
      <div class="mt-6 flex justify-between">
        <button
          type="button"
          class="border border-gray-300 text-gray-700 hover:bg-gray-50 font-medium px-6 py-2.5 rounded-lg transition"
          @click="step = 1"
        >
          ← Tilbake
        </button>
        <button
          type="button"
          class="bg-blue-700 hover:bg-blue-800 disabled:bg-gray-300 disabled:cursor-not-allowed text-white font-semibold px-8 py-3 rounded-lg transition"
          :disabled="!step2Valid"
          @click="goToStep(3)"
        >
          Neste: Se over og betal →
        </button>
      </div>
    </div>

    <!-- ═══════════════════════════════════════════════════════════════════
         STEP 3: Review + payment + generation
    ════════════════════════════════════════════════════════════════════════ -->
    <div v-else-if="step === 3">

      <!-- ── Phase: idle / payment form ──────────────────────────────────── -->
      <div v-if="genPhase === 'idle'" class="grid lg:grid-cols-5 gap-6">

        <!-- Left: Stripe card + pay button -->
        <div class="lg:col-span-3 space-y-5">

          <!-- Stripe not configured -->
          <div
            v-if="stripeError"
            class="bg-amber-50 border border-amber-200 rounded-xl p-4 text-sm text-amber-800"
          >
            <strong>Stripe ikke tilgjengelig:</strong> {{ stripeError }}
          </div>

          <template v-else>
            <section class="bg-white border border-gray-200 rounded-xl p-6">
              <h2 class="text-lg font-semibold text-gray-900 mb-4">
                Kortbetaling
                <span class="ml-2 text-xs font-normal text-gray-500 bg-gray-100 px-2 py-0.5 rounded">
                  Sikret av Stripe
                </span>
              </h2>

              <label class="block text-sm font-medium text-gray-700 mb-2">Kortdetaljer</label>
              <div
                ref="cardMountEl"
                class="border border-gray-300 rounded-lg px-3 py-3 min-h-[44px] focus-within:ring-2 focus-within:ring-blue-500 focus-within:border-blue-500 transition"
              />
              <p v-if="stripeCardError" class="mt-2 text-sm text-red-600">
                {{ stripeCardError }}
              </p>
            </section>

            <p
              v-if="paymentApiError"
              class="text-sm text-red-700 bg-red-50 border border-red-200 rounded-xl px-4 py-3"
            >
              {{ paymentApiError }}
            </p>

            <button
              type="button"
              class="w-full bg-blue-700 hover:bg-blue-800 disabled:bg-gray-300 disabled:cursor-not-allowed text-white font-semibold py-3.5 rounded-xl transition flex items-center justify-center gap-2 text-base"
              :disabled="processingPayment || !stripeReady"
              @click="pay"
            >
              <span
                v-if="processingPayment"
                class="inline-block w-4 h-4 border-2 border-white border-t-transparent rounded-full animate-spin"
              />
              <span>{{ processingPayment ? 'Behandler…' : 'Generer og betal 95 kr' }}</span>
            </button>

            <p class="text-xs text-gray-500 text-center">
              🔒 Betalingen er kryptert og håndteres av Stripe. Vi lagrer ikke kortinformasjon.
            </p>
          </template>
        </div>

        <!-- Right: Summary -->
        <aside class="lg:col-span-2">
          <div class="bg-white border border-gray-200 rounded-xl p-6 sticky top-4 space-y-4 text-sm">
            <h2 class="text-base font-semibold text-gray-900">Oppsummering</h2>

            <dl class="space-y-2">
              <div>
                <dt class="text-xs uppercase tracking-wider text-gray-500 font-semibold mb-0.5">Mal</dt>
                <dd class="text-gray-900 font-medium flex items-center gap-1.5">
                  <span>{{ categoryEmoji[selectedTemplate?.category ?? ''] ?? '🎊' }}</span>
                  <span>{{ templateName }}</span>
                </dd>
              </div>
              <div>
                <dt class="text-xs uppercase tracking-wider text-gray-500 font-semibold mb-0.5">Navn</dt>
                <dd class="text-gray-900">{{ personName }}<span v-if="personAge">, {{ personAge }} år</span></dd>
              </div>
              <div>
                <dt class="text-xs uppercase tracking-wider text-gray-500 font-semibold mb-0.5">Bannertekst</dt>
                <dd class="text-gray-900 italic">{{ textContent }}</dd>
              </div>
              <div>
                <dt class="text-xs uppercase tracking-wider text-gray-500 font-semibold mb-0.5">Tema</dt>
                <dd class="text-gray-900">{{ themeDescription }}</dd>
              </div>
              <div>
                <dt class="text-xs uppercase tracking-wider text-gray-500 font-semibold mb-0.5">Format</dt>
                <dd class="text-gray-900">{{ aspectRatio }} — ca. {{ aspectDimensions.width }} × {{ aspectDimensions.height }} cm</dd>
              </div>
              <div v-if="uploadedPhotoBannerDesignId">
                <dt class="text-xs uppercase tracking-wider text-gray-500 font-semibold mb-0.5">Portrettfoto</dt>
                <dd class="text-gray-900 flex items-center gap-1">
                  <img
                    v-if="photoPreviewUrl"
                    :src="photoPreviewUrl"
                    class="w-10 h-10 object-cover rounded border border-gray-200"
                    alt="Portrettfoto"
                  />
                  <span>Lastet opp ✓</span>
                </dd>
              </div>
              <div>
                <dt class="text-xs uppercase tracking-wider text-gray-500 font-semibold mb-0.5">Språk</dt>
                <dd class="text-gray-900">{{ language === 'nb' ? '🇳🇴 Norsk' : '🇬🇧 English' }}</dd>
              </div>
            </dl>

            <div class="border-t border-gray-200 pt-3 flex justify-between font-semibold text-base">
              <span class="text-gray-900">Totalt</span>
              <span class="text-blue-700">95 kr</span>
            </div>
          </div>
        </aside>
      </div>

      <!-- ── Phase: generating (polling) ────────────────────────────────── -->
      <div v-else-if="genPhase === 'generating'" class="text-center py-16">
        <div class="inline-flex flex-col items-center gap-6">
          <!-- Spinner -->
          <div class="relative w-20 h-20">
            <div class="absolute inset-0 rounded-full border-4 border-blue-100" />
            <div class="absolute inset-0 rounded-full border-4 border-blue-600 border-t-transparent animate-spin" />
          </div>
          <div>
            <h2 class="text-2xl font-bold text-gray-900 mb-2">Genererer banner…</h2>
            <p class="text-gray-600 max-w-sm">
              AI-en jobber med designet ditt. Dette tar vanligvis 20–60 sekunder.
            </p>
          </div>
          <!-- Animated progress steps -->
          <div class="flex flex-col gap-2 w-64 text-left text-sm text-gray-600">
            <div class="flex items-center gap-2">
              <span class="w-4 h-4 rounded-full bg-green-500 flex items-center justify-center">
                <svg class="w-2.5 h-2.5 text-white" fill="none" viewBox="0 0 24 24" stroke="currentColor" stroke-width="3">
                  <path stroke-linecap="round" stroke-linejoin="round" d="M5 13l4 4L19 7" />
                </svg>
              </span>
              Betaling bekreftet
            </div>
            <div class="flex items-center gap-2">
              <span class="w-4 h-4 rounded-full bg-blue-200 border-2 border-blue-500 animate-pulse" />
              Lager AI-design…
            </div>
            <div class="flex items-center gap-2">
              <span class="w-4 h-4 rounded-full bg-gray-200" />
              Klart til godkjenning
            </div>
          </div>
        </div>
      </div>

      <!-- ── Phase: ready (preview available) ──────────────────────────── -->
      <div v-else-if="genPhase === 'ready' && currentDesignRequest" class="space-y-6">
        <div class="text-center">
          <h2 class="text-2xl font-bold text-gray-900 mb-2">Banneret ditt er klart! 🎉</h2>
          <p class="text-gray-600">Se over designet og godkjenn, eller be om en ny generering.</p>
        </div>

        <!-- Preview image -->
        <div class="bg-white border border-gray-200 rounded-xl overflow-hidden shadow-sm">
          <img
            v-if="currentDesignRequest.previewUrl"
            :src="currentDesignRequest.previewUrl"
            :alt="`AI-generert banner for ${currentDesignRequest.personName}`"
            class="w-full h-auto object-contain"
          />
          <div v-else class="flex items-center justify-center h-64 bg-gray-50 text-gray-500">
            Forhåndsvisning ikke tilgjengelig
          </div>
        </div>

        <!-- Status info -->
        <div v-if="currentDesignRequest.status === 'Approved' || currentDesignRequest.status === 'Final'" class="bg-green-50 border border-green-200 rounded-xl p-4 text-green-800 text-sm">
          ✓ Banneret er godkjent og sendt til produksjon.
        </div>

        <!-- Action buttons (when AwaitingApproval) -->
        <div
          v-if="currentDesignRequest.status === 'AwaitingApproval'"
          class="flex flex-col sm:flex-row gap-3"
        >
          <button
            type="button"
            class="flex-1 bg-green-600 hover:bg-green-700 disabled:bg-gray-300 text-white font-semibold py-3 rounded-xl transition flex items-center justify-center gap-2"
            :disabled="approving"
            @click="approve"
          >
            <span
              v-if="approving"
              class="inline-block w-4 h-4 border-2 border-white border-t-transparent rounded-full animate-spin"
            />
            ✓ Godkjenn
          </button>
          <button
            v-if="currentDesignRequest.regenerationsRemaining > 0"
            type="button"
            class="flex-1 border-2 border-gray-300 hover:border-gray-400 text-gray-700 font-semibold py-3 rounded-xl transition flex items-center justify-center gap-2"
            :disabled="regenerating"
            @click="regenerate"
          >
            <span
              v-if="regenerating"
              class="inline-block w-4 h-4 border-2 border-gray-500 border-t-transparent rounded-full animate-spin"
            />
            ✗ Prøv igjen
          </button>
        </div>

        <p
          v-if="approveError"
          class="text-sm text-red-700 bg-red-50 border border-red-200 rounded-xl px-4 py-3"
        >
          {{ approveError }}
        </p>
        <p
          v-if="regenerateError"
          class="text-sm text-red-700 bg-red-50 border border-red-200 rounded-xl px-4 py-3"
        >
          {{ regenerateError }}
        </p>
      </div>

      <!-- ── Phase: error ────────────────────────────────────────────────── -->
      <div v-else-if="genPhase === 'error'" class="text-center py-12">
        <div class="text-5xl mb-4">⚠️</div>
        <h2 class="text-2xl font-bold text-gray-900 mb-2">Noe gikk galt</h2>
        <p class="text-gray-600 mb-4">
          {{ currentDesignRequest?.lastError ?? 'AI-genereringen feilet. Vennligst kontakt support.' }}
        </p>
        <RouterLink
          to="/account"
          class="inline-block bg-blue-700 hover:bg-blue-800 text-white font-semibold px-6 py-2.5 rounded-lg transition"
        >
          Gå til min konto
        </RouterLink>
      </div>

      <!-- Back button (only when in idle phase) -->
      <div v-if="genPhase === 'idle'" class="mt-6 flex justify-start">
        <button
          type="button"
          class="border border-gray-300 text-gray-700 hover:bg-gray-50 font-medium px-6 py-2.5 rounded-lg transition"
          @click="step = 2"
        >
          ← Tilbake
        </button>
      </div>
    </div>
  </div>
</template>

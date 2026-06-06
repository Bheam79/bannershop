<script setup lang="ts">
import { ref, computed, watch, onMounted, onBeforeUnmount } from 'vue'
import { RouterLink } from 'vue-router'
import { loadStripe } from '@stripe/stripe-js'
import type { Stripe, StripeCardElement } from '@stripe/stripe-js'
import { useAuthStore } from '@/stores/auth'
import { uploadBannerFile } from '@/api/bannerBuilder'
import {
  fetchTemplates,
  createManualRequest,
  type BannerTemplateItem,
} from '@/api/designRequests'

// ── Router / auth ─────────────────────────────────────────────────────────────
const auth = useAuthStore()

// ── Step state ────────────────────────────────────────────────────────────────
const step = ref<1 | 2 | 3>(1)

// ── Step 1: Template, photo (required), language ──────────────────────────────
const templates = ref<BannerTemplateItem[]>([])
const templatesLoading = ref(true)
const templatesError = ref<string | null>(null)
const selectedTemplateId = ref<number | null>(null)
const language = ref<'nb' | 'en'>('nb')

// Portrait photo upload — REQUIRED for manual orders
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

// Post-payment: just a confirmation (no polling needed for manual)
type Phase = 'idle' | 'confirmed'
const phase = ref<Phase>('idle')
const completedRequestId = ref<number | null>(null)

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

// Photo required for manual — the designer needs it
const step1Valid = computed(
  () => selectedTemplateId.value !== null && uploadedPhotoBannerDesignId.value !== null,
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
    templatesError.value = ex.response?.data?.error || ex.message || 'Kunne ikke laste maler.'
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
    photoUploadError.value = `Filtypen støttes ikke. Bruk JPEG, PNG eller WEBP.`
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
  if (s === 3) void initStripe()
}

// ── Stripe ────────────────────────────────────────────────────────────────────
async function initStripe() {
  if (stripeRef.value) return
  const key = import.meta.env.VITE_STRIPE_PUBLISHABLE_KEY as string | undefined
  if (!key || key.startsWith('pk_test_REPLACE')) {
    stripeError.value =
      'Stripe er ikke konfigurert (mangler VITE_STRIPE_PUBLISHABLE_KEY). ' +
      'Betalingsfunksjonen er ikke tilgjengelig i dette testmiljøet.'
    return
  }
  try {
    const stripe = await loadStripe(key)
    if (!stripe) { stripeError.value = 'Stripe kunne ikke lastes. Prøv igjen.'; return }
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
    card.on('change', (event) => { stripeCardError.value = event.error?.message ?? null })
    if (cardMountEl.value) { card.mount(cardMountEl.value); stripeReady.value = true }
  } catch {
    stripeError.value = 'Stripe kunne ikke initialiseres.'
  }
}

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
    const resp = await createManualRequest({
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
    completedRequestId.value = reqId
  } catch (e: unknown) {
    const ex = e as { response?: { data?: { error?: string } }; message?: string }
    paymentApiError.value =
      ex.response?.data?.error || ex.message || 'Kunne ikke opprette bestilling. Prøv igjen.'
    processingPayment.value = false
    return
  }

  // Dev/mock bypass — skip Stripe card confirmation
  if (clientSecret.startsWith('pi_mock_')) {
    processingPayment.value = false
    phase.value = 'confirmed'
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
  phase.value = 'confirmed'
}

// ── Utils ─────────────────────────────────────────────────────────────────────
function formatNok(n: number): string {
  return new Intl.NumberFormat('nb-NO', { maximumFractionDigits: 0 }).format(n) + ' kr'
}

// ── Lifecycle ─────────────────────────────────────────────────────────────────
onMounted(loadTemplates)

onBeforeUnmount(() => {
  cardElement.value?.unmount()
  if (photoPreviewUrl.value) URL.revokeObjectURL(photoPreviewUrl.value)
})
</script>

<template>
  <div class="max-w-4xl mx-auto px-4 py-8 sm:py-12">
    <!-- Header -->
    <header class="mb-8 text-center">
      <h1 class="text-3xl sm:text-4xl font-bold text-gray-900 mb-3">
        Manuelt designet feiringsbanner
      </h1>
      <p class="text-lg text-gray-600 max-w-2xl mx-auto">
        Beskriv ønsket og legg ved et portrettfoto — vi designer banneret manuelt og sender deg en
        forhåndsvisning innen 2–3 virkedager.
        <strong class="text-gray-900">{{ formatNok(495) }}</strong>.
      </p>
    </header>

    <!-- Auth guard -->
    <div
      v-if="!auth.isLoggedIn"
      class="mb-8 bg-amber-50 border border-amber-200 rounded-xl px-5 py-4 text-sm text-amber-900"
    >
      <strong>Du må være innlogget</strong> for å bestille et manuelt banner.
      <RouterLink to="/login?redirect=/banner-builder/manual" class="underline font-medium ml-1">
        Logg inn
      </RouterLink>
      eller
      <RouterLink to="/register" class="underline font-medium">registrer deg</RouterLink>
      for å fortsette.
    </div>

    <!-- ── Post-payment confirmation ───────────────────────────────────── -->
    <div v-if="phase === 'confirmed'" class="text-center py-16">
      <div class="text-6xl mb-6">🎉</div>
      <h2 class="text-3xl font-bold text-gray-900 mb-3">Bestillingen er mottatt!</h2>
      <p class="text-lg text-gray-600 max-w-xl mx-auto mb-8">
        Du vil motta en e-post når designforslaget er klart til godkjenning.
        Vi leverer innen <strong>2–3 virkedager</strong>.
      </p>
      <RouterLink
        to="/account/design-requests"
        class="inline-block bg-blue-700 hover:bg-blue-800 text-white font-semibold px-8 py-3 rounded-xl transition"
      >
        Se mine design-bestillinger
      </RouterLink>
    </div>

    <!-- ── Stepper UI ─────────────────────────────────────────────────── -->
    <template v-else>
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
           STEP 1: Choose template + upload photo (required) + language
      ════════════════════════════════════════════════════════════════════════ -->
      <div v-if="step === 1">
        <div v-if="templatesLoading" class="text-center text-gray-500 py-12">Laster maler…</div>
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

          <!-- Portrait photo upload — REQUIRED -->
          <div class="mb-8">
            <h2 class="text-xl font-semibold text-gray-900 mb-1">
              Portrettfoto
              <span class="text-red-500 ml-1">*</span>
              <span class="text-base font-normal text-gray-500 ml-2">(obligatorisk)</span>
            </h2>
            <p class="text-sm text-gray-600 mb-4">
              Vi trenger et bilde av personen som feires for å designe banneret.
              Designeren vil bruke bildet som referanse.
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
                  Fjern og velg nytt foto
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
                <svg class="w-10 h-10 text-gray-400 mb-2" fill="none" viewBox="0 0 24 24" stroke="currentColor">
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

          <p
            v-if="!selectedTemplateId"
            class="mt-2 text-right text-xs text-gray-400"
          >
            Velg en mal for å gå videre
          </p>
          <p
            v-else-if="!uploadedPhotoBannerDesignId && !photoUploading"
            class="mt-2 text-right text-xs text-gray-400"
          >
            Last opp et portrettfoto for å gå videre
          </p>
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
           STEP 3: Review + pay (495 kr)
      ════════════════════════════════════════════════════════════════════════ -->
      <div v-else-if="step === 3">
        <div class="grid lg:grid-cols-5 gap-6">

          <!-- Left: Explanation + payment -->
          <div class="lg:col-span-3 space-y-5">

            <!-- Information box -->
            <div class="bg-blue-50 border border-blue-200 rounded-xl p-5 text-sm text-blue-900">
              <div class="font-semibold text-blue-800 mb-2 flex items-center gap-2">
                <span>🎨</span>
                <span>Slik fungerer manuell design</span>
              </div>
              <ul class="space-y-1.5 text-blue-800">
                <li class="flex items-start gap-2">
                  <span class="mt-0.5 text-blue-500 shrink-0">✓</span>
                  Vi designer banneret ditt manuelt og sender deg en forhåndsvisning innen 2–3 virkedager.
                </li>
                <li class="flex items-start gap-2">
                  <span class="mt-0.5 text-blue-500 shrink-0">✓</span>
                  Du kan be om én gratis korrigering.
                </li>
                <li class="flex items-start gap-2">
                  <span class="mt-0.5 text-blue-500 shrink-0">✓</span>
                  Deretter godkjenner du og banneret sendes i produksjon.
                </li>
              </ul>
            </div>

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
                <span>{{ processingPayment ? 'Behandler…' : 'Bestill og betal 495 kr' }}</span>
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
                <div>
                  <dt class="text-xs uppercase tracking-wider text-gray-500 font-semibold mb-0.5">Portrettfoto</dt>
                  <dd class="text-gray-900 flex items-center gap-2">
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
                <span class="text-blue-700">495 kr</span>
              </div>

              <p class="text-xs text-gray-400">
                Inkl. én gratis korrigering og levering innen 2–3 virkedager.
              </p>
            </div>
          </aside>
        </div>

        <!-- Back button -->
        <div class="mt-6 flex justify-start">
          <button
            type="button"
            class="border border-gray-300 text-gray-700 hover:bg-gray-50 font-medium px-6 py-2.5 rounded-lg transition"
            @click="step = 2"
          >
            ← Tilbake
          </button>
        </div>
      </div>
    </template>
  </div>
</template>

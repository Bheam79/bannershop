<script setup lang="ts">
import { ref, computed, watch, onMounted, onBeforeUnmount } from 'vue'
import { RouterLink, useRouter } from 'vue-router'
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
const router = useRouter()
const auth = useAuthStore()

const MANUAL_SESSION_KEY = 'manual_banner_builder_state'

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
    photoUploadError.value =
      ex.response?.data?.error || ex.message || 'Opplasting feilet. Prøv igjen.'
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
function saveFormState() {
  try {
    sessionStorage.setItem(MANUAL_SESSION_KEY, JSON.stringify({
      selectedTemplateId: selectedTemplateId.value,
      language: language.value,
      uploadedPhotoBannerDesignId: uploadedPhotoBannerDesignId.value,
      personName: personName.value,
      personAge: personAge.value,
      textContent: textContent.value,
      themeDescription: themeDescription.value,
      aspectRatio: aspectRatio.value,
    }))
  } catch { /* non-fatal */ }
}

function goToStep(s: 1 | 2 | 3) {
  if (s === 2 && !step1Valid.value) return
  if (s === 3 && (!step1Valid.value || !step2Valid.value)) return

  // Gate auth at step 3: if not logged in, save form state and redirect to login
  if (s === 3 && !auth.isLoggedIn) {
    saveFormState()
    void router.push('/login?redirect=/banner-builder/manual')
    return
  }

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
onMounted(async () => {
  await loadTemplates()

  // Restore form state saved before a login redirect (BANNERSH-97)
  const saved = sessionStorage.getItem(MANUAL_SESSION_KEY)
  if (saved && auth.isLoggedIn) {
    try {
      const s = JSON.parse(saved) as {
        selectedTemplateId: number | null
        language: 'nb' | 'en'
        uploadedPhotoBannerDesignId: number | null
        personName: string
        personAge: number | null
        textContent: string
        themeDescription: string
        aspectRatio: '16:9' | '18:9'
      }
      if (s.selectedTemplateId !== null) selectedTemplateId.value = s.selectedTemplateId
      language.value = s.language
      uploadedPhotoBannerDesignId.value = s.uploadedPhotoBannerDesignId
      personName.value = s.personName
      personAge.value = s.personAge
      textContent.value = s.textContent
      themeDescription.value = s.themeDescription
      aspectRatio.value = s.aspectRatio
      sessionStorage.removeItem(MANUAL_SESSION_KEY)
      // If form is complete, go straight to step 3 (payment)
      if (step1Valid.value && step2Valid.value) {
        step.value = 3
        void initStripe()
      }
    } catch {
      sessionStorage.removeItem(MANUAL_SESSION_KEY)
    }
  }
})

onBeforeUnmount(() => {
  cardElement.value?.unmount()
  if (photoPreviewUrl.value) URL.revokeObjectURL(photoPreviewUrl.value)
})
</script>

<template>
  <div style="max-width:960px;margin:0 auto;padding:2rem 1.5rem 4rem">
    <!-- Header -->
    <header style="margin-bottom:2.5rem;text-align:center">
      <h1 class="display" style="font-size:clamp(28px,4vw,44px);color:var(--text);margin-bottom:12px">
        Manuelt designet feiringsbanner
      </h1>
      <p style="font-size:18px;color:var(--muted);max-width:36em;margin:0 auto">
        Beskriv ønsket og legg ved et portrettfoto — vi designer banneret manuelt og sender deg en
        forhåndsvisning innen 2–3 virkedager.
        <strong style="color:var(--text)">{{ formatNok(495) }}</strong>.
      </p>
    </header>

    <!-- ── Post-payment confirmation ───────────────────────────────────── -->
    <div v-if="phase === 'confirmed'" style="text-align:center;padding:5rem 0">
      <div style="width:80px;height:80px;border-radius:50%;background:rgba(255,106,61,.15);border:2px solid rgba(255,106,61,.3);display:grid;place-items:center;margin:0 auto 24px;font-size:36px">
        <i class="fa-solid fa-envelope-open-text" style="color:var(--accent)"></i>
      </div>
      <h2 class="display" style="font-size:32px;color:var(--text);margin-bottom:12px">Bestillingen er mottatt!</h2>
      <p style="font-size:18px;color:var(--muted);max-width:36em;margin:0 auto 30px">
        Du vil motta en e-post når designforslaget er klart til godkjenning.
        Vi leverer innen <strong style="color:var(--text)">2–3 virkedager</strong>.
      </p>
      <RouterLink to="/account/design-requests" class="btn btn-primary" style="font-size:16px;padding:13px 28px">
        <i class="fa-solid fa-palette"></i> Se mine design-bestillinger
      </RouterLink>
    </div>

    <!-- ── Stepper UI ─────────────────────────────────────────────────── -->
    <template v-else>
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
           STEP 1: Choose template + upload photo (required) + language
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

          <!-- Portrait photo upload — REQUIRED -->
          <div style="margin-bottom:2rem">
            <h2 class="display" style="font-size:20px;color:var(--text);margin-bottom:4px">
              Portrettfoto
              <span style="color:var(--accent)">*</span>
              <span style="font-size:14px;font-weight:400;color:var(--faint);margin-left:8px">(obligatorisk)</span>
            </h2>
            <p style="font-size:14px;color:var(--muted);margin-bottom:16px">
              Vi trenger et bilde av personen som feires for å designe banneret.
              Designeren vil bruke bildet som referanse.
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
                  <i class="fa-solid fa-trash-can" style="font-size:12px"></i> Fjern og velg nytt foto
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
          <div style="display:flex;justify-content:flex-end;flex-direction:column;align-items:flex-end;gap:8px">
            <button
              type="button"
              class="btn btn-primary"
              style="padding:12px 28px;font-size:15px"
              :disabled="!step1Valid"
              @click="goToStep(2)"
            >
              Neste: Tilpass <i class="fa-solid fa-arrow-right" style="font-size:12px"></i>
            </button>
            <p v-if="!selectedTemplateId" style="font-size:12px;color:var(--faint)">
              Velg en mal for å gå videre
            </p>
            <p v-else-if="!uploadedPhotoBannerDesignId && !photoUploading" style="font-size:12px;color:var(--faint)">
              Last opp et portrettfoto for å gå videre
            </p>
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
           STEP 3: Review + pay (495 kr)
      ════════════════════════════════════════════════════════════════════════ -->
      <div v-else-if="step === 3">
        <div style="display:grid;grid-template-columns:1.2fr .8fr;gap:24px" class="pay-grid">

          <!-- Left: Explanation + payment -->
          <div style="display:grid;gap:20px">

            <!-- Information box -->
            <div style="display:grid;gap:12px;background:rgba(255,106,61,.07);border:1px solid rgba(255,106,61,.25);border-radius:14px;padding:20px">
              <div style="font-weight:700;color:var(--accent-2);display:flex;align-items:center;gap:8px;font-size:15px">
                <i class="fa-solid fa-palette" style="color:var(--accent)"></i>
                Slik fungerer manuell design
              </div>
              <ul style="display:grid;gap:9px;list-style:none;padding:0;margin:0">
                <li style="display:flex;align-items:flex-start;gap:10px;font-size:14px;color:var(--muted)">
                  <i class="fa-solid fa-check" style="color:var(--accent);margin-top:2px;flex-shrink:0"></i>
                  Vi designer banneret ditt manuelt og sender deg en forhåndsvisning innen 2–3 virkedager.
                </li>
                <li style="display:flex;align-items:flex-start;gap:10px;font-size:14px;color:var(--muted)">
                  <i class="fa-solid fa-check" style="color:var(--accent);margin-top:2px;flex-shrink:0"></i>
                  Du kan be om én gratis korrigering.
                </li>
                <li style="display:flex;align-items:flex-start;gap:10px;font-size:14px;color:var(--muted)">
                  <i class="fa-solid fa-check" style="color:var(--accent);margin-top:2px;flex-shrink:0"></i>
                  Deretter godkjenner du og banneret sendes i produksjon.
                </li>
              </ul>
            </div>

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
                <div ref="cardMountEl" class="stripe-mount" />
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
                <i v-else class="fa-solid fa-palette"></i>
                {{ processingPayment ? 'Behandler…' : 'Bestill og betal 495 kr' }}
              </button>

              <p style="font-size:12.5px;color:var(--faint);text-align:center;display:flex;align-items:center;justify-content:center;gap:6px">
                <i class="fa-solid fa-lock"></i>
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
                <div>
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
                <span style="font-weight:800;color:var(--accent);font-size:20px;font-family:var(--font-display)">495 kr</span>
              </div>

              <p style="font-size:12.5px;color:var(--faint)">
                Inkl. én gratis korrigering og levering innen 2–3 virkedager.
              </p>
            </div>
          </aside>
        </div>

        <!-- Back button -->
        <div style="margin-top:24px">
          <button type="button" class="btn btn-ghost" @click="step = 2">
            <i class="fa-solid fa-arrow-left" style="font-size:12px"></i> Tilbake
          </button>
        </div>
      </div>
    </template>
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
.lang-btn:hover { color: var(--text); }
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
.ratio-btn:hover { color: var(--text); }
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

/* ── Responsive ──────────────────────────────────────────────── */
.pay-grid { grid-template-columns: 1.2fr .8fr; }
@media (max-width: 768px) {
  .pay-grid { grid-template-columns: 1fr !important; }
}
</style>

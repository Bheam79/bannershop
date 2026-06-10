<script setup lang="ts">
import { ref, computed, watch, onMounted, onBeforeUnmount } from 'vue'
import { RouterLink, useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/auth'
import { useCartStore } from '@/stores/cart'
import { uploadBannerFile } from '@/api/bannerBuilder'
import {
  fetchTemplates,
  createManualRequest,
  type BannerTemplateItem,
} from '@/api/designRequests'
import { fetchSizes, fetchPrice, fetchEyeletPriceNok } from '@/api/shop'
import type { BannerSize, CartItem, EyeletOption } from '@/types'
import { countEyelets } from '@/types'
import EyeletPreview from '@/components/shop/EyeletPreview.vue'

// ── Router / auth / cart ──────────────────────────────────────────────────────
const router = useRouter()
const auth = useAuthStore()
const cart = useCartStore()

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

// ── Step 2: Quality / size selection ─────────────────────────────────────────
type QualityOption = 'high' | 'good' | 'custom'
const selectedQuality = ref<QualityOption>('high')

// Custom option inputs
const customWidth = ref<number | null>(null)
const customHeight = ref<number | null>(null)
const customMaterialGsm = ref<400 | 680>(400)

// ── Step 3: Confirmation / tilpass step (BANNERSH-136) ───────────────────────
// No Stripe — after the API call we add two cart lines and route to /checkout.
const eyeletOption = ref<EyeletOption>('None')
const eyeletPriceNok = ref<number>(0)
const submittingOrder = ref(false)
const submitError = ref<string | null>(null)

const eyeletCount = computed(() =>
  countEyelets(selectedDimensions.value.width, selectedDimensions.value.height, eyeletOption.value),
)
const eyeletFeePerUnit = computed(() => eyeletCount.value * eyeletPriceNok.value)

// ── BANNERSH-104: design fee + banner production cost breakdown ──────────────
const DESIGN_FEE_NOK = 495
const sizes = ref<BannerSize[]>([])
const sizesLoaded = ref(false)
const bannerPriceError = ref<string | null>(null)

// Per-option state
interface OptionPriceState {
  price: number | null
  loading: boolean
  comingSoon: boolean
}
const option1State = ref<OptionPriceState>({ price: null, loading: false, comingSoon: false })
const option2State = ref<OptionPriceState>({ price: null, loading: false, comingSoon: false })
const customState  = ref<OptionPriceState>({ price: null, loading: false, comingSoon: false })

// ── Computed helpers ──────────────────────────────────────────────────────────
const selectedTemplate = computed(() =>
  templates.value.find((t) => t.id === selectedTemplateId.value) ?? null,
)

const templateName = computed(() => {
  const t = selectedTemplate.value
  if (!t) return ''
  return language.value === 'en' ? t.nameEn : t.nameNb
})

// Dimensions for the currently selected quality option
const selectedDimensions = computed(() => {
  if (selectedQuality.value === 'high') return { width: 250, height: 150 }
  if (selectedQuality.value === 'good') return { width: 270, height: 180 }
  return { width: customWidth.value ?? 0, height: customHeight.value ?? 0 }
})

// Aspect ratio string sent to the backend
const aspectRatioForBackend = computed(() => {
  const { width, height } = selectedDimensions.value
  if (width > 0 && height > 0) return `${width}x${height}`
  return '250x150'
})

// Label for the summary panel
const selectedQualityLabel = computed(() => {
  if (selectedQuality.value === 'high') return 'Høykvalitet (3 år)'
  if (selectedQuality.value === 'good') return 'God kvalitet (3 mnd)'
  return `Egendefinert ${customMaterialGsm.value}g`
})

// Current banner price based on which option is selected
const bannerPriceNok = computed<number | null>(() => {
  if (selectedQuality.value === 'high') return option1State.value.price
  if (selectedQuality.value === 'good') return option2State.value.price
  return customState.value.price
})

const bannerPriceLoading = computed(() => {
  if (selectedQuality.value === 'high') return option1State.value.loading
  if (selectedQuality.value === 'good') return option2State.value.loading
  return customState.value.loading
})

// Running total for step 3 (banner + eyelet fee + designer fee)
const totalPriceNok = computed(() => {
  const banner = bannerPriceNok.value ?? 0
  return DESIGN_FEE_NOK + banner + eyeletFeePerUnit.value
})

// ── Banner cost resolution ────────────────────────────────────────────────────
function pickBannerSize(
  catalog: BannerSize[],
  targetWidthCm: number,
  targetHeightCm: number,
  materialGsm?: number,
): { size: BannerSize; customWidthCm?: number } | null {
  // Prefer exact fixed-width match
  const exact = catalog.find(
    (s) =>
      s.isActive &&
      !s.isCustomWidth &&
      s.widthCm === targetWidthCm &&
      s.heightCm === targetHeightCm &&
      (materialGsm == null || s.material?.weightGsm === materialGsm),
  )
  if (exact) return { size: exact }
  // Fall back to custom-width of the same height
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
    if (!picked) {
      state.price = null
      return
    }
    state.comingSoon = isComingSoon(picked.size)
    state.price = await fetchPrice(picked.size.id, picked.customWidthCm)
  } catch {
    state.price = null
  } finally {
    state.loading = false
  }
}

async function refreshAllPrices() {
  if (!sizesLoaded.value) return
  bannerPriceError.value = null
  await Promise.all([
    computeOptionPrice(250, 150, option1State.value),
    computeOptionPrice(270, 180, option2State.value),
  ])
}

async function refreshCustomPrice() {
  const w = customWidth.value ?? 0
  const h = customHeight.value ?? 0
  if (!sizesLoaded.value || w <= 0 || h <= 0) {
    customState.value.price = null
    return
  }
  await computeOptionPrice(w, h, customState.value, customMaterialGsm.value)
}

watch([customWidth, customHeight, customMaterialGsm], () => {
  if (sizesLoaded.value) void refreshCustomPrice()
})

// Photo required for manual — the designer needs it
const step1Valid = computed(
  () => selectedTemplateId.value !== null && uploadedPhotoBannerDesignId.value !== null,
)

const step2Valid = computed(() => {
  if (!personName.value.trim() || !textContent.value.trim() || !themeDescription.value.trim())
    return false
  // Custom option requires both dimensions to be filled
  if (selectedQuality.value === 'custom') {
    return (customWidth.value ?? 0) > 0 && (customHeight.value ?? 0) > 0
  }
  return true
})

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
      selectedQuality: selectedQuality.value,
      customWidth: customWidth.value,
      customHeight: customHeight.value,
      customMaterialGsm: customMaterialGsm.value,
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
}

// ── Add to cart (BANNERSH-136: no Stripe — call API then add both cart lines) ─
async function addToCart() {
  if (submittingOrder.value) return
  submitError.value = null
  submittingOrder.value = true

  let resp: Awaited<ReturnType<typeof createManualRequest>>
  try {
    resp = await createManualRequest({
      templateId: selectedTemplateId.value!,
      language: language.value,
      personName: personName.value.trim(),
      personAge: personAge.value ?? undefined,
      textContent: textContent.value.trim(),
      themeDescription: themeDescription.value.trim(),
      aspectRatio: aspectRatioForBackend.value,
      uploadedPhotoBannerDesignId: uploadedPhotoBannerDesignId.value ?? undefined,
    })
  } catch (e: unknown) {
    const ex = e as { response?: { data?: { error?: string } }; message?: string }
    submitError.value =
      ex.response?.data?.error || ex.message || 'Kunne ikke opprette bestilling. Prøv igjen.'
    submittingOrder.value = false
    return
  }

  const reqId = resp.designRequestId
  const { width, height } = selectedDimensions.value
  const picked = pickBannerSize(sizes.value, width, height)

  // ── Cart line 1: Physical banner ────────────────────────────────────────────
  const bannerItem: CartItem = {
    bannerSizeId: picked?.size.id ?? null,
    bannerSizeName: `Manuelt banner ${width > 0 ? `${width} × ${height} cm` : selectedQualityLabel.value}`,
    customWidthCm: picked?.customWidthCm ?? null,
    heightCm: height,
    quantity: 1,
    unitPriceNok: resp.bannerPriceNok,
    eyeletOption: eyeletOption.value,
    eyeletFeeNok: eyeletFeePerUnit.value,
    notes: `Manuelt designet banner — bestilling #${reqId}`,
    designRequestId: reqId,
  }
  cart.addItem(bannerItem)

  // ── Cart line 2: Designer service fee ───────────────────────────────────────
  const designFeeItem: CartItem = {
    bannerSizeId: null,
    bannerSizeName: 'Designer-tjeneste (manuelt banner)',
    customWidthCm: null,
    heightCm: 0,
    quantity: 1,
    unitPriceNok: resp.designPriceNok,
    eyeletOption: 'None',
    eyeletFeeNok: 0,
    notes: `Designhonorar for bestilling #${reqId}`,
    designRequestId: reqId,
  }
  cart.addItem(designFeeItem)

  submittingOrder.value = false
  void router.push('/checkout')
}

// ── Utils ─────────────────────────────────────────────────────────────────────
function formatNok(n: number): string {
  return new Intl.NumberFormat('nb-NO', { maximumFractionDigits: 0 }).format(n) + ' kr'
}

// ── Lifecycle ─────────────────────────────────────────────────────────────────
async function loadSizesAndPrice() {
  try {
    sizes.value = await fetchSizes()
    sizesLoaded.value = true
    await refreshAllPrices()
  } catch (e: unknown) {
    const ex = e as { message?: string }
    bannerPriceError.value =
      ex.message || 'Kunne ikke laste prisinformasjon for banneret.'
  }
}

async function loadEyeletPrice() {
  try {
    eyeletPriceNok.value = await fetchEyeletPriceNok()
  } catch {
    eyeletPriceNok.value = 0
  }
}

onMounted(async () => {
  await Promise.all([loadTemplates(), loadSizesAndPrice(), loadEyeletPrice()])

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
        selectedQuality?: QualityOption
        customWidth?: number | null
        customHeight?: number | null
        customMaterialGsm?: 400 | 680
      }
      if (s.selectedTemplateId !== null) selectedTemplateId.value = s.selectedTemplateId
      language.value = s.language
      uploadedPhotoBannerDesignId.value = s.uploadedPhotoBannerDesignId
      personName.value = s.personName
      personAge.value = s.personAge
      textContent.value = s.textContent
      themeDescription.value = s.themeDescription
      if (s.selectedQuality) selectedQuality.value = s.selectedQuality
      if (s.customWidth != null) customWidth.value = s.customWidth
      if (s.customHeight != null) customHeight.value = s.customHeight
      if (s.customMaterialGsm) customMaterialGsm.value = s.customMaterialGsm
      sessionStorage.removeItem(MANUAL_SESSION_KEY)
      // If form is complete, go straight to step 3 (confirmation)
      if (step1Valid.value && step2Valid.value) {
        step.value = 3
      }
    } catch {
      sessionStorage.removeItem(MANUAL_SESSION_KEY)
    }
  }
})

onBeforeUnmount(() => {
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
        forhåndsvisning innen 2–3 virkedager. Designhonorar
        <strong style="color:var(--text)">{{ formatNok(DESIGN_FEE_NOK) }}</strong>
        + bannerproduksjon (varierer med størrelse).
      </p>
    </header>

    <!-- ── Stepper UI ─────────────────────────────────────────────────── -->
    <!-- Step indicator -->
    <nav class="step-nav" style="margin-bottom:2rem" aria-label="Steg">
      <button
        v-for="(label, idx) in ['Velg mal', 'Tilpass', 'Bekreft']"
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
              <p style="font-size:13px;color:var(--faint)">JPEG, PNG, WEBP – maks 10 MB</p>

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
          <p v-if="!selectedTemplateId" style="font-size:13px;color:var(--faint)">
            Velg en mal for å gå videre
          </p>
          <p v-else-if="!uploadedPhotoBannerDesignId && !photoUploading" style="font-size:13px;color:var(--faint)">
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
          <p style="margin-top:5px;font-size:13px;color:var(--faint)">{{ textContent.length }} / 500 tegn</p>
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

        <!-- Quality / size selection -->
        <div>
          <div class="field-label" style="margin-bottom:12px">Velg kvalitet og størrelse</div>
          <div class="quality-grid">

            <!-- Option 1: Høykvalitet -->
            <button
              type="button"
              class="quality-btn"
              :class="{ 'quality-btn-active': selectedQuality === 'high' }"
              @click="selectedQuality = 'high'"
            >
              <span v-if="option1State.comingSoon" class="coming-soon-pill">Kommer snart</span>
              <div class="quality-btn-title">Høykvalitet</div>
              <div class="quality-btn-sub">3 års fargegaranti</div>
              <div class="quality-btn-dims">ca. 250 × 150 cm</div>
              <div class="quality-btn-price">
                <template v-if="option1State.loading">
                  <i class="fa-solid fa-circle-notch fa-spin" style="font-size:11px"></i>
                </template>
                <template v-else-if="option1State.price !== null">
                  {{ formatNok(option1State.price) }}
                </template>
                <template v-else>–</template>
              </div>
            </button>

            <!-- Option 2: God kvalitet -->
            <button
              type="button"
              class="quality-btn"
              :class="{ 'quality-btn-active': selectedQuality === 'good' }"
              @click="selectedQuality = 'good'"
            >
              <span v-if="option2State.comingSoon" class="coming-soon-pill">Kommer snart</span>
              <div class="quality-btn-title">God kvalitet</div>
              <div class="quality-btn-sub">3 måneders fargegaranti</div>
              <div class="quality-btn-dims">ca. 270 × 180 cm</div>
              <div class="quality-btn-price">
                <template v-if="option2State.loading">
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

          <!-- Custom option inline form -->
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
            <div v-if="customState.comingSoon" style="margin-top:8px;font-size:13px;color:var(--gold)">
              <i class="fa-solid fa-clock"></i> Denne kombinasjonen er ikke tilgjengelig ennå.
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
          Neste: Se over og bekreft <i class="fa-solid fa-arrow-right" style="font-size:12px"></i>
        </button>
      </div>
    </div>

    <!-- ═══════════════════════════════════════════════════════════════════
         STEP 3: Confirmation / tilpass — eyelet picker + "Legg i handlekurven"
         BANNERSH-136: no Stripe card — payment collected at checkout.
    ════════════════════════════════════════════════════════════════════════ -->
    <div v-else-if="step === 3">
      <div style="display:grid;grid-template-columns:1.2fr .8fr;gap:24px" class="pay-grid">

        <!-- Left: Information + eyelet picker -->
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
              <li style="display:flex;align-items:flex-start;gap:10px;font-size:14px;color:var(--muted)">
                <i class="fa-solid fa-shopping-cart" style="color:var(--accent);margin-top:2px;flex-shrink:0"></i>
                Du betaler ikke nå — banneret legges i handlekurven og du betaler ved kassen.
              </li>
            </ul>
          </div>

          <!-- Eyelet (malje) picker -->
          <section class="bb-panel">
            <h2 class="display" style="font-size:18px;color:var(--text);margin-bottom:6px;display:flex;align-items:center;gap:10px">
              <i class="fa-solid fa-circle-dot" style="color:var(--accent)"></i>
              Maljer (valgfritt)
            </h2>
            <p style="font-size:13.5px;color:var(--muted);margin-bottom:16px">
              Maljer gjør det enklere å henge opp banneret. Velg alternativet som passer best.
            </p>
            <!-- BANNERSH-173: eyelet placement preview -->
            <EyeletPreview
              v-if="selectedDimensions.width > 0 && selectedDimensions.height > 0"
              :width-cm="selectedDimensions.width"
              :height-cm="selectedDimensions.height"
              :eyelet-option="eyeletOption"
              style="margin-bottom:16px;border-radius:8px;overflow:hidden;border:1px solid var(--line-soft)"
            />
            <div class="eyelet-grid">
              <button
                v-for="opt in (['None', 'FourCorners', 'PerMeter'] as EyeletOption[])"
                :key="opt"
                type="button"
                class="eyelet-btn"
                :class="{ 'eyelet-btn-active': eyeletOption === opt }"
                @click="eyeletOption = opt"
              >
                <div class="eyelet-btn-title">
                  {{ opt === 'None' ? 'Ingen maljer' : opt === 'FourCorners' ? '4 hjørnemaljer' : 'Per meter' }}
                </div>
                <div class="eyelet-btn-sub">
                  {{ opt === 'None' ? 'Ingen boring' : opt === 'FourCorners' ? '4 stk' : `${eyeletCount} stk` }}
                </div>
                <div class="eyelet-btn-price">
                  <template v-if="opt === 'None'">Gratis</template>
                  <template v-else-if="eyeletPriceNok === 0">–</template>
                  <template v-else>
                    + {{ formatNok(countEyelets(selectedDimensions.width, selectedDimensions.height, opt) * eyeletPriceNok) }}
                  </template>
                </div>
              </button>
            </div>
          </section>

          <div v-if="submitError" class="error-box">
            <i class="fa-solid fa-circle-exclamation"></i> {{ submitError }}
          </div>

          <button
            type="button"
            class="btn btn-primary"
            style="width:100%;justify-content:center;padding:15px;font-size:16px;border-radius:13px"
            :disabled="submittingOrder"
            @click="addToCart"
          >
            <i v-if="submittingOrder" class="fa-solid fa-circle-notch fa-spin"></i>
            <i v-else class="fa-solid fa-cart-plus"></i>
            {{ submittingOrder ? 'Oppretter bestilling…' : 'Legg i handlekurven' }}
          </button>

          <p style="font-size:13px;color:var(--faint);text-align:center;display:flex;align-items:center;justify-content:center;gap:6px">
            <i class="fa-solid fa-shield-halved"></i>
            Ingen betaling nå — du betaler trygt via kassen etter at du har sett over handlekurven.
          </p>
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
                  {{ selectedQualityLabel }}
                  <span v-if="selectedDimensions.width > 0 && selectedDimensions.height > 0">
                    — ca. {{ selectedDimensions.width }} × {{ selectedDimensions.height }} cm
                  </span>
                </dd>
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

            <!-- Itemised total with eyelet line -->
            <div style="border-top:1px solid var(--line-soft);padding-top:14px;display:grid;gap:8px">
              <div style="display:flex;justify-content:space-between;align-items:center;font-size:14px;color:var(--muted)">
                <span>Designer-tjeneste</span>
                <span style="color:var(--text);font-weight:600">{{ formatNok(DESIGN_FEE_NOK) }}</span>
              </div>
              <div style="display:flex;justify-content:space-between;align-items:center;font-size:14px;color:var(--muted)">
                <span>
                  Banner
                  <template v-if="selectedDimensions.width > 0 && selectedDimensions.height > 0">
                    ({{ selectedDimensions.width }}×{{ selectedDimensions.height }} cm)
                  </template>
                </span>
                <span style="color:var(--text);font-weight:600">
                  <span v-if="bannerPriceLoading"><i class="fa-solid fa-circle-notch fa-spin" style="font-size:11px"></i></span>
                  <template v-else-if="bannerPriceNok !== null">{{ formatNok(bannerPriceNok) }}</template>
                  <template v-else>–</template>
                </span>
              </div>
              <div
                v-if="eyeletOption !== 'None' && eyeletFeePerUnit > 0"
                style="display:flex;justify-content:space-between;align-items:center;font-size:14px;color:var(--muted)"
              >
                <span>Maljer ({{ eyeletCount }} stk)</span>
                <span style="color:var(--text);font-weight:600">{{ formatNok(eyeletFeePerUnit) }}</span>
              </div>
              <div style="display:flex;justify-content:space-between;align-items:center;border-top:1px solid var(--line-soft);padding-top:10px;margin-top:2px">
                <span style="font-weight:700;color:var(--text)">Totalt</span>
                <span style="font-weight:800;color:var(--accent);font-size:20px;font-family:var(--font-display)">{{ formatNok(totalPriceNok) }}</span>
              </div>
            </div>

            <p v-if="bannerPriceError" style="font-size:13px;color:var(--gold)">
              <i class="fa-solid fa-triangle-exclamation"></i> {{ bannerPriceError }}
            </p>
            <p style="font-size:13px;color:var(--faint)">
              Inkl. én gratis korrigering og levering innen 2–3 virkedager.
              Frakt kommer i tillegg og beregnes ved kassen.
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
  gap: 4px;
  border: 2px solid var(--line);
  border-radius: 14px;
  padding: 16px 14px 14px;
  font-size: 14px;
  cursor: pointer;
  transition: border-color 0.15s, background 0.15s;
  background: var(--surface-2);
  color: var(--muted);
  font-family: var(--font-ui);
  text-align: left;
}
.quality-btn:hover { border-color: var(--line-soft); color: var(--text); }
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
  color: var(--accent);
}

/* "Kommer snart" pill */
.coming-soon-pill {
  position: absolute;
  top: 8px;
  right: 8px;
  background: rgba(231,185,78,.18);
  border: 1px solid rgba(231,185,78,.4);
  color: #e7d08a;
  font-size: 13px;
  font-weight: 700;
  padding: 2px 8px;
  border-radius: 99px;
  letter-spacing: 0.03em;
  white-space: nowrap;
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
  font-size: 13px;
  font-weight: 600;
  cursor: pointer;
  transition: border-color 0.15s, background 0.15s;
  background: transparent;
  color: var(--muted);
  font-family: var(--font-ui);
}
.mat-btn:hover { color: var(--text); }
.mat-btn-active { border-color: var(--accent); color: var(--text); background: rgba(255,106,61,.08); }

/* ── Eyelet picker ────────────────────────────────────────────── */
.eyelet-grid {
  display: grid;
  grid-template-columns: repeat(3, 1fr);
  gap: 10px;
}
@media (max-width: 560px) { .eyelet-grid { grid-template-columns: 1fr; } }

.eyelet-btn {
  display: flex;
  flex-direction: column;
  gap: 3px;
  border: 2px solid var(--line);
  border-radius: 12px;
  padding: 14px 12px;
  font-size: 13.5px;
  cursor: pointer;
  transition: border-color 0.15s, background 0.15s;
  background: var(--surface-2);
  color: var(--muted);
  font-family: var(--font-ui);
  text-align: left;
}
.eyelet-btn:hover { border-color: var(--line-soft); color: var(--text); }
.eyelet-btn-active {
  border-color: var(--accent);
  background: rgba(255,106,61,.08);
  color: var(--text);
  box-shadow: 0 0 0 2px rgba(255,106,61,.2);
}
.eyelet-btn-title {
  font-weight: 700;
  font-size: 14px;
  color: var(--text);
}
.eyelet-btn-sub {
  font-size: 13px;
  color: var(--muted);
}
.eyelet-btn-price {
  margin-top: 6px;
  font-size: 13px;
  font-weight: 700;
  color: var(--accent);
}

/* ── Errors ──────────────────────────────────────────────────── */
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

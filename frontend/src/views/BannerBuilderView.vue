<script setup lang="ts">
import { ref, computed, watch, onMounted, nextTick } from 'vue'
import { useRouter, useRoute, RouterLink } from 'vue-router'
import { useAuthStore } from '@/stores/auth'
import { useCartStore } from '@/stores/cart'
import { fetchSizes, fetchEyeletPriceNok } from '@/api/shop'
import type { BannerSize, CartItem, EyeletOption } from '@/types'
import { countEyelets } from '@/types'
import UploadZone from '@/components/banner-builder/UploadZone.vue'
import EyeletPreview from '@/components/shop/EyeletPreview.vue'
import { getBannerDesign, rotateBanner, setBannerHeight, generateBannerPreview } from '@/api/bannerBuilder'
import type { UploadResponse } from '@/api/bannerBuilder'
import { formatNok } from '@/utils/format'

const router = useRouter()
const route = useRoute()
const auth = useAuthStore()
const cart = useCartStore()

// ── Session persistence key ───────────────────────────────────────────────────
const SESSION_KEY = 'banner_upload_state'

// ── Upload state ─────────────────────────────────────────────────────────────
const design = ref<UploadResponse | null>(null)

// Editor state
const heightCm = ref<number>(150)
const computedWidthCm = ref<number>(0)
const rotationDegrees = ref<number>(0)

// ── Preview URL (served by the unified BannerPreviewService, GUID-keyed) ────────
// No blob management needed — the URL is a stable server path that can be used
// directly in <img src> without URL.createObjectURL / revokeObjectURL.
const previewBlobUrl = ref<string | null>(null) // kept as 'previewBlobUrl' for template compat
const previewLoading = ref(false)
const previewError = ref<string | null>(null)

async function loadPreview() {
  if (!design.value) return
  previewLoading.value = true
  previewError.value = null
  try {
    // Fetch a plain (no-eyelet) server preview; the EyeletPreview SVG overlay adds
    // eyelet circles interactively so the builder feels instant without extra round-trips.
    previewBlobUrl.value = await generateBannerPreview(design.value.designId)
  } catch (e: unknown) {
    const ex = e as { response?: { status?: number }; message?: string }
    previewError.value =
      ex.response?.status === 401
        ? 'Forhåndsvisning krever innlogging.'
        : 'Kunne ikke laste forhåndsvisning.'
  } finally {
    previewLoading.value = false
  }
}

// ── Rotation ──────────────────────────────────────────────────────────────────
const rotating = ref(false)
const rotateError = ref<string | null>(null)

async function rotate(delta: number) {
  if (!design.value || rotating.value) return
  rotating.value = true
  rotateError.value = null
  try {
    const resp = await rotateBanner(design.value.designId, delta)
    rotationDegrees.value = resp.rotationDegrees
    computedWidthCm.value = resp.computedWidthCm
    await loadPreview()
    if (sizeMode.value === 'custom') {
      // In custom mode: keep the user's chosen height; the rotate API already
      // recalculated the width — sync the display fields.
      customLinkLock = true
      customWidth.value = resp.computedWidthCm
      releaseCustomLink()
    } else {
      // In material mode: re-apply the material height so the backend stores
      // the correct selectedHeightCm after rotation.
      await applyMaterialSize(sizeMode.value)
    }
  } catch (e: unknown) {
    const ex = e as { response?: { data?: { error?: string } }; message?: string }
    rotateError.value = ex.response?.data?.error || ex.message || 'Rotasjon feilet.'
  } finally {
    rotating.value = false
  }
}

// ── Size / material selection ─────────────────────────────────────────────────
// sizeMode: ID of the chosen isCustomWidth BannerSize, or 'custom' for manual entry
type SizeMode = number | 'custom'
const sizeMode = ref<SizeMode>(0)

const isCustomMode = computed(() => sizeMode.value === 'custom')

// All sizes (custom-width ones indexed by material for the option buttons)
const allSizes = ref<BannerSize[]>([])
const sizesLoading = ref(false)

// The isCustomWidth=true sizes — one per material
const customWidthSizes = computed<BannerSize[]>(() =>
  allSizes.value.filter((s) => s.isCustomWidth),
)

function isComingSoon(size: BannerSize): boolean {
  if (!size.availableFrom) return false
  return new Date(size.availableFrom) > new Date()
}

// ── Client-side dimension math (mirrors BannerDimensions.cs) ─────────────────
function computeWidthFromHeight(
  widthPx: number,
  heightPx: number,
  rotation: number,
  selectedHeightCm: number,
): number {
  const rot = ((rotation % 360) + 360) % 360
  const aspect = rot === 90 || rot === 270 ? heightPx / widthPx : widthPx / heightPx
  const raw = selectedHeightCm * aspect
  const rounded = Math.round(raw / 10) * 10
  return Math.max(50, Math.min(1000, rounded))
}

// Width to display for a material option button (before user clicks it)
function previewWidthForMaterialSize(sz: BannerSize): number {
  if (!design.value) return 0
  return computeWidthFromHeight(
    design.value.widthPx,
    design.value.heightPx,
    rotationDegrees.value,
    sz.heightCm,
  )
}

// ── Applying a material size ──────────────────────────────────────────────────
const settingHeight = ref(false)
const setHeightError = ref<string | null>(null)

async function applyMaterialSize(szId: number) {
  if (!design.value || settingHeight.value) return
  const sz = allSizes.value.find((s) => s.id === szId)
  if (!sz) return
  settingHeight.value = true
  setHeightError.value = null
  try {
    const resp = await setBannerHeight(design.value.designId, sz.heightCm)
    heightCm.value = resp.selectedHeightCm
    computedWidthCm.value = resp.computedWidthCm
  } catch (e: unknown) {
    const ex = e as { response?: { data?: { error?: string } }; message?: string }
    setHeightError.value = ex.response?.data?.error || ex.message || 'Kunne ikke sette høyde.'
  } finally {
    settingHeight.value = false
  }
}

watch(sizeMode, async (mode) => {
  if (mode === 'custom' || !design.value) return
  await applyMaterialSize(mode)
})

// ── Custom size ───────────────────────────────────────────────────────────────
const customHeight = ref<number | null>(null)
const customWidth = ref<number | null>(null)
const customMaterialSizeId = ref<number | null>(null) // ID of the chosen isCustomWidth size

// Image aspect ratio (width / height, accounting for rotation)
const imageAspectRatio = computed<number | null>(() => {
  if (!design.value) return null
  const { widthPx, heightPx } = design.value
  const rot = ((rotationDegrees.value % 360) + 360) % 360
  return rot === 90 || rot === 270 ? heightPx / widthPx : widthPx / heightPx
})

// Linked custom width ↔ height (like AI wizard)
let customLinkLock = false
function releaseCustomLink() {
  void nextTick(() => { customLinkLock = false })
}

watch(customHeight, (h) => {
  if (customLinkLock) return
  const r = imageAspectRatio.value
  if (!r || !h || h <= 0) return
  customLinkLock = true
  customWidth.value = Math.max(50, Math.min(1000, Math.round((h * r) / 10) * 10))
  releaseCustomLink()
})

watch(customWidth, (w) => {
  if (customLinkLock) return
  const r = imageAspectRatio.value
  if (!r || !w || w <= 0) return
  customLinkLock = true
  customHeight.value = Math.max(1, Math.round(w / r))
  releaseCustomLink()
})

// When custom mode is activated, prefill from current dims
watch(sizeMode, (mode) => {
  if (mode !== 'custom') return
  if (customHeight.value == null) {
    customHeight.value = heightCm.value || 150
  }
  // Auto-pick first non-coming-soon material as default for custom
  if (customMaterialSizeId.value == null && customWidthSizes.value.length > 0) {
    const available = customWidthSizes.value.find((s) => !isComingSoon(s))
    customMaterialSizeId.value = (available ?? customWidthSizes.value[0])?.id ?? null
  }
})

// Debounced API call when custom height changes
let customHeightTimer: ReturnType<typeof setTimeout> | null = null
watch([customHeight, customMaterialSizeId], () => {
  if (sizeMode.value !== 'custom' || !design.value) return
  if (customHeightTimer) clearTimeout(customHeightTimer)
  customHeightTimer = setTimeout(async () => {
    if (!design.value || !customHeight.value) return
    const resp = await setBannerHeight(design.value.designId, customHeight.value)
    heightCm.value = resp.selectedHeightCm
    computedWidthCm.value = resp.computedWidthCm
    // Sync customWidth from API response
    if (!customLinkLock) {
      customLinkLock = true
      customWidth.value = resp.computedWidthCm
      releaseCustomLink()
    }
  }, 400)
})

// Selected custom material max height (for overflow warning)
const customMaterialMaxHeight = computed<number | null>(() => {
  if (!customMaterialSizeId.value) return null
  const sz = allSizes.value.find((s) => s.id === customMaterialSizeId.value)
  return sz?.material?.widthCm ?? null
})

const showHeightWarning = computed<boolean>(() => {
  if (sizeMode.value !== 'custom') return false
  const h = customHeight.value ?? 0
  const max = customMaterialMaxHeight.value ?? Infinity
  return h > max
})

// ── Pricing size (the isCustomWidth BannerSize to use for cart/price) ────────
const pricingSize = computed<BannerSize | null>(() => {
  if (sizeMode.value === 'custom') {
    if (!customMaterialSizeId.value) return null
    return allSizes.value.find((s) => s.id === customMaterialSizeId.value) ?? null
  }
  return allSizes.value.find((s) => s.id === sizeMode.value) ?? null
})

const selectedMaterial = computed(() => pricingSize.value?.material ?? null)

// ── Quantity ─────────────────────────────────────────────────────────────────
const qty = ref<number>(1)

// ── Eyelet (malje) option ────────────────────────────────────────────────────
const eyeletOption = ref<EyeletOption>('None')
const eyeletPriceNok = ref<number>(0)

const eyeletCount = computed(() =>
  countEyelets(computedWidthCm.value, heightCm.value, eyeletOption.value),
)
const eyeletFeePerUnit = computed(() => eyeletCount.value * eyeletPriceNok.value)

// ── Pricing ───────────────────────────────────────────────────────────────────
const customPriceNok = ref<number | null>(null)
const priceLoading = ref<boolean>(false)
const priceError = ref<string | null>(null)

async function refreshPrice() {
  if (!design.value || !pricingSize.value || !computedWidthCm.value) {
    customPriceNok.value = null
    return
  }
  priceLoading.value = true
  priceError.value = null
  try {
    const sizes = await fetchSizes(computedWidthCm.value)
    const match = sizes.find(
      (s) => s.id === pricingSize.value?.id,
    )
    customPriceNok.value = match?.calculatedPrice ?? null
    if (customPriceNok.value == null) {
      priceError.value = 'Fant ikke prisinformasjon for valgt materiale.'
    }
  } catch (e: unknown) {
    const ex = e as { response?: { data?: { error?: string } }; message?: string }
    priceError.value = ex.response?.data?.error || ex.message || 'Kunne ikke beregne pris.'
    customPriceNok.value = null
  } finally {
    priceLoading.value = false
  }
}

let priceTimer: ReturnType<typeof setTimeout> | null = null
watch([computedWidthCm, heightCm, pricingSize], () => {
  if (!design.value) return
  priceLoading.value = true
  if (priceTimer) clearTimeout(priceTimer)
  priceTimer = setTimeout(async () => {
    await refreshPrice()
    priceLoading.value = false
  }, 300)
})

const lineTotal = computed(() =>
  ((customPriceNok.value ?? 0) + eyeletFeePerUnit.value) * qty.value,
)


// ── Upload callback ───────────────────────────────────────────────────────────
function onUploaded(resp: UploadResponse) {
  design.value = resp
  heightCm.value = resp.selectedHeightCm
  computedWidthCm.value = resp.computedWidthCm
  rotationDegrees.value = resp.rotationDegrees
  previewBlobUrl.value = null
  void loadPreview()
  // Select first available material size
  const firstAvailable = customWidthSizes.value.find((s) => !isComingSoon(s))
  if (firstAvailable) {
    sizeMode.value = firstAvailable.id
  } else if (customWidthSizes.value.length > 0) {
    const first = customWidthSizes.value[0]
    if (first) sizeMode.value = first.id
  }
}

// ── Add to cart + checkout ────────────────────────────────────────────────────
function addToCartAndCheckout() {
  if (!design.value || !pricingSize.value || customPriceNok.value == null) return

  const item: CartItem = {
    bannerSizeId: pricingSize.value.id,
    bannerSizeName: `Egen design ${computedWidthCm.value} × ${heightCm.value} cm`,
    customWidthCm: computedWidthCm.value,
    heightCm: heightCm.value,
    quantity: qty.value,
    unitPriceNok: customPriceNok.value,
    eyeletOption: eyeletOption.value,
    eyeletFeeNok: eyeletFeePerUnit.value,
    designId: design.value.designId,
    previewUrl: previewBlobUrl.value ?? undefined,
    notes: `Banner design #${design.value.designId} (lastet opp av kunde, rotasjon ${rotationDegrees.value}°)`,
  }
  cart.addItem(item)

  if (!auth.isLoggedIn) {
    router.push('/login?redirect=/checkout')
    return
  }
  router.push('/checkout')
}

// ── sessionStorage persistence ────────────────────────────────────────────────
watch(
  [design, heightCm, computedWidthCm, rotationDegrees, qty, eyeletOption, sizeMode,
   customHeight, customWidth, customMaterialSizeId],
  () => {
    if (design.value) {
      sessionStorage.setItem(SESSION_KEY, JSON.stringify({
        design: design.value,
        heightCm: heightCm.value,
        computedWidthCm: computedWidthCm.value,
        rotationDegrees: rotationDegrees.value,
        qty: qty.value,
        eyeletOption: eyeletOption.value,
        sizeMode: sizeMode.value,
        customHeight: customHeight.value,
        customWidth: customWidth.value,
        customMaterialSizeId: customMaterialSizeId.value,
      }))
    } else {
      sessionStorage.removeItem(SESSION_KEY)
    }
  },
  { deep: true },
)

// ── Initialisation ────────────────────────────────────────────────────────────
onMounted(async () => {
  // Load all sizes first (needed for option rendering)
  try {
    allSizes.value = await fetchSizes()
  } catch { /* non-fatal */ }

  try {
    eyeletPriceNok.value = await fetchEyeletPriceNok()
  } catch { /* non-fatal */ }

  // ?designId=<id>: load a previously-uploaded design directly
  const designIdParam = (route.query.designId as string | undefined)?.trim()
  if (designIdParam) {
    const designId = parseInt(designIdParam, 10)
    if (!isNaN(designId) && designId > 0) {
      try {
        const resp = await getBannerDesign(designId)
        design.value = resp
        heightCm.value = resp.selectedHeightCm
        computedWidthCm.value = resp.computedWidthCm
        rotationDegrees.value = resp.rotationDegrees
        // Pick the matching material size
        const matchingSz = customWidthSizes.value.find(
          (s) => s.heightCm === resp.selectedHeightCm,
        )
        sizeMode.value = matchingSz?.id ?? customWidthSizes.value[0]?.id ?? 0
        void loadPreview()
      } catch { /* non-fatal */ }
      return
    }
  }

  // Restore from sessionStorage
  const saved = sessionStorage.getItem(SESSION_KEY)
  if (saved) {
    try {
      const state = JSON.parse(saved) as {
        design: UploadResponse
        heightCm: number
        computedWidthCm: number
        rotationDegrees: number
        qty: number
        eyeletOption: EyeletOption
        sizeMode: SizeMode
        customHeight: number | null
        customWidth: number | null
        customMaterialSizeId: number | null
      }
      design.value = state.design
      heightCm.value = state.heightCm
      computedWidthCm.value = state.computedWidthCm
      rotationDegrees.value = state.rotationDegrees
      qty.value = state.qty
      eyeletOption.value = state.eyeletOption
      sizeMode.value = state.sizeMode ?? customWidthSizes.value[0]?.id ?? 0
      customHeight.value = state.customHeight ?? null
      customWidth.value = state.customWidth ?? null
      customMaterialSizeId.value = state.customMaterialSizeId ?? null
      void loadPreview()
    } catch {
      sessionStorage.removeItem(SESSION_KEY)
    }
    return
  }
})
</script>

<template>
  <div style="max-width:1100px;margin:0 auto;padding:2rem 1.5rem 4rem">
    <!-- Header -->
    <header style="margin-bottom:2.5rem;text-align:center">
      <div style="margin-bottom:14px">
        <RouterLink
          to="/banner-builder"
          style="font-size:14px;color:var(--accent);font-weight:600;display:inline-flex;align-items:center;gap:6px"
        >
          <i class="fa-solid fa-arrow-left" style="font-size:12px"></i> Tilbake til bannervalgene
        </RouterLink>
      </div>
      <h1 class="display" style="font-size:clamp(28px,4vw,44px);color:var(--text);margin-bottom:12px">
        Eget bilde eller PDF
      </h1>
      <p style="font-size:18px;color:var(--muted);max-width:36em;margin:0 auto 20px">
        Last opp din egen design — vi beregner størrelsen automatisk og trykker
        på det banneret du velger.
      </p>
      <RouterLink
        to="/banner-builder/ai"
        class="btn btn-primary"
        style="font-size:14px;padding:10px 20px"
      >
        <i class="fa-solid fa-wand-magic-sparkles"></i> AI-generert feiringsbanner — 95 kr
      </RouterLink>
    </header>

    <!-- Step 1: Upload -->
    <section v-if="!design" style="margin-bottom:2.5rem">
      <div style="display:flex;align-items:center;gap:12px;margin-bottom:18px">
        <span class="step-badge">1</span>
        <h2 class="display" style="font-size:20px;color:var(--text)">Last opp din design</h2>
      </div>
      <UploadZone @uploaded="onUploaded" />

      <div class="hint-grid" style="margin-top:20px">
        <div class="hint-card">
          <div class="hint-ico"><i class="fa-solid fa-ruler-combined"></i></div>
          <strong style="color:var(--text);display:block;margin-bottom:4px">Automatisk størrelse</strong>
          <span style="color:var(--muted);font-size:13.5px">Vi beregner bredden ut fra bildets størrelsesforhold.</span>
        </div>
        <div class="hint-card">
          <div class="hint-ico"><i class="fa-solid fa-rotate"></i></div>
          <strong style="color:var(--text);display:block;margin-bottom:4px">Roter</strong>
          <span style="color:var(--muted);font-size:13.5px">Snu bildet 90° om det er feil vei.</span>
        </div>
        <div class="hint-card">
          <div class="hint-ico"><i class="fa-solid fa-scissors"></i></div>
          <strong style="color:var(--text);display:block;margin-bottom:4px">Klar til trykk</strong>
          <span style="color:var(--muted);font-size:13.5px">Sydde kanter og maljer i hjørnene inkludert.</span>
        </div>
      </div>
    </section>

    <!-- Step 2: Preview + options -->
    <template v-else>
      <section style="display:grid;grid-template-columns:1fr 1fr;gap:24px;margin-bottom:2rem" class="step-grid">

        <!-- LEFT: Preview + rotation -->
        <div style="display:flex;flex-direction:column;gap:16px">
          <div style="display:flex;align-items:center;justify-content:space-between">
            <div style="display:flex;align-items:center;gap:12px">
              <span class="step-badge">2</span>
              <h2 class="display" style="font-size:20px;color:var(--text)">Forhåndsvisning</h2>
            </div>
            <button
              type="button"
              style="font-size:13.5px;color:var(--accent);font-weight:600;background:none;border:none;cursor:pointer;padding:0"
              @click="design = null"
            >
              <i class="fa-solid fa-arrow-rotate-left" style="font-size:11px;margin-right:4px"></i> Last opp en annen
            </button>
          </div>

          <!-- Single preview: EyeletPreview with uploaded image -->
          <div
            style="border-radius:12px;overflow:hidden;border:1px solid var(--line-soft);background:var(--surface-2)"
          >
            <div
              v-if="previewLoading"
              style="min-height:220px;display:flex;align-items:center;justify-content:center;color:var(--faint);font-size:14px"
            >
              <i class="fa-solid fa-spinner fa-spin" style="margin-right:8px"></i> Laster forhåndsvisning…
            </div>
            <div
              v-else-if="previewError"
              style="min-height:220px;display:flex;align-items:center;justify-content:center;color:#f4a57a;font-size:14px;padding:16px;text-align:center"
            >
              <i class="fa-solid fa-circle-exclamation" style="margin-right:8px"></i> {{ previewError }}
            </div>
            <EyeletPreview
              v-else-if="computedWidthCm > 0 && heightCm > 0"
              :width-cm="computedWidthCm"
              :height-cm="heightCm"
              :eyelet-option="eyeletOption"
              :image-url="previewBlobUrl ?? undefined"
            />
          </div>

          <!-- Rotation buttons -->
          <div style="display:grid;grid-template-columns:1fr 1fr;gap:10px">
            <button
              type="button"
              class="rotate-btn"
              :disabled="rotating"
              @click="rotate(-90)"
            >
              <span style="font-size:18px">↺</span>
              <span>Roter venstre</span>
            </button>
            <button
              type="button"
              class="rotate-btn"
              :disabled="rotating"
              @click="rotate(90)"
            >
              <span style="font-size:18px">↻</span>
              <span>Roter høyre</span>
            </button>
          </div>

          <p v-if="rotateError" class="error-box">
            <i class="fa-solid fa-circle-exclamation"></i> {{ rotateError }}
          </p>
        </div>

        <!-- RIGHT: Size selection + summary + cart -->
        <div class="bb-panel" style="display:flex;flex-direction:column;gap:20px">
          <div style="display:flex;align-items:center;gap:12px">
            <span class="step-badge">3</span>
            <h2 class="display" style="font-size:20px;color:var(--text)">Størrelse og detaljer</h2>
          </div>

          <!-- Size selector (3 options: one per material + custom) -->
          <div>
            <div class="field-label" style="margin-bottom:10px">Velg størrelse</div>
            <div style="display:grid;gap:8px">
              <!-- One option per custom-width size (one per material) -->
              <label
                v-for="sz in customWidthSizes"
                :key="sz.id"
                class="size-option"
                :class="{
                  'size-option--active': sizeMode === sz.id,
                  'size-option--disabled': isComingSoon(sz),
                }"
                @click="!isComingSoon(sz) && (sizeMode = sz.id)"
              >
                <div class="size-option-icon">
                  <div
                    class="size-icon-rect"
                    :style="{
                      aspectRatio: `${previewWidthForMaterialSize(sz)} / ${sz.heightCm}`,
                      opacity: isComingSoon(sz) ? 0.4 : 1,
                    }"
                  ></div>
                </div>
                <div style="flex:1;min-width:0">
                  <div style="display:flex;align-items:center;gap:8px;flex-wrap:wrap">
                    <span style="font-weight:700;font-size:15px;color:var(--text)">
                      {{ previewWidthForMaterialSize(sz) }} × {{ sz.heightCm }} cm
                    </span>
                    <span
                      v-if="isComingSoon(sz)"
                      style="font-size:11px;font-weight:700;color:var(--gold);background:rgba(231,185,78,.12);border:1px solid rgba(231,185,78,.3);border-radius:4px;padding:1px 6px"
                    >Kommer snart</span>
                  </div>
                  <div style="font-size:13px;color:var(--faint);margin-top:2px">
                    {{ sz.material?.name }}
                  </div>
                  <div v-if="sizeMode === sz.id && !isComingSoon(sz)" style="font-size:13px;color:var(--muted);margin-top:2px">
                    Bredde beregnet fra ditt bilde
                  </div>
                </div>
                <div class="size-option-radio">
                  <div class="radio-outer" :class="{ 'radio-outer--active': sizeMode === sz.id && !isComingSoon(sz) }">
                    <div v-if="sizeMode === sz.id && !isComingSoon(sz)" class="radio-inner"></div>
                  </div>
                </div>
              </label>

              <!-- Custom size option -->
              <label
                class="size-option"
                :class="{ 'size-option--active': isCustomMode }"
                @click="sizeMode = 'custom'"
              >
                <div class="size-option-icon">
                  <i class="fa-solid fa-ruler-combined" style="font-size:18px;color:var(--muted)"></i>
                </div>
                <div style="flex:1;min-width:0">
                  <div style="font-weight:700;font-size:15px;color:var(--text)">Egendefinert størrelse</div>
                  <div style="font-size:13px;color:var(--faint);margin-top:2px">Velg materiale og oppgi mål</div>
                </div>
                <div class="size-option-radio">
                  <div class="radio-outer" :class="{ 'radio-outer--active': isCustomMode }">
                    <div v-if="isCustomMode" class="radio-inner"></div>
                  </div>
                </div>
              </label>

              <!-- Custom fields (expanded when custom is chosen) -->
              <div
                v-if="isCustomMode"
                style="border:1px solid var(--line);border-radius:12px;padding:16px;background:var(--surface-2);display:grid;gap:14px"
              >
                <!-- Material picker -->
                <div>
                  <div class="field-label" style="margin-bottom:8px">Materiale</div>
                  <div style="display:grid;gap:6px">
                    <label
                      v-for="sz in customWidthSizes"
                      :key="sz.id"
                      class="mat-option"
                      :class="{
                        'mat-option--active': customMaterialSizeId === sz.id,
                        'mat-option--disabled': isComingSoon(sz),
                      }"
                      @click="!isComingSoon(sz) && (customMaterialSizeId = sz.id)"
                    >
                      <div style="flex:1">
                        <div style="font-size:13.5px;font-weight:600;color:var(--text)">
                          {{ sz.material?.name }}
                        </div>
                        <div style="font-size:12px;color:var(--faint)">
                          Maks høyde {{ sz.material?.widthCm }} cm
                          <span
                            v-if="isComingSoon(sz)"
                            style="margin-left:6px;font-size:11px;font-weight:700;color:var(--gold)"
                          >Kommer snart</span>
                        </div>
                      </div>
                      <div class="radio-outer" :class="{ 'radio-outer--active': customMaterialSizeId === sz.id && !isComingSoon(sz) }">
                        <div v-if="customMaterialSizeId === sz.id && !isComingSoon(sz)" class="radio-inner"></div>
                      </div>
                    </label>
                  </div>
                </div>

                <!-- Width & Height inputs (linked via image ratio) -->
                <div style="display:grid;grid-template-columns:1fr 1fr;gap:12px">
                  <div>
                    <label class="field-label" style="display:block;margin-bottom:6px" for="custom-w">Bredde (cm)</label>
                    <input
                      id="custom-w"
                      v-model.number="customWidth"
                      type="number"
                      min="50"
                      max="1000"
                      class="dark-input"
                      style="width:100%"
                      placeholder="f.eks. 270"
                    />
                  </div>
                  <div>
                    <label class="field-label" style="display:block;margin-bottom:6px" for="custom-h">Høyde (cm)</label>
                    <input
                      id="custom-h"
                      v-model.number="customHeight"
                      type="number"
                      min="1"
                      max="500"
                      class="dark-input"
                      style="width:100%"
                      placeholder="f.eks. 150"
                    />
                  </div>
                </div>
                <div style="font-size:12px;color:var(--faint)">
                  <i class="fa-solid fa-link" style="margin-right:4px"></i>
                  Bredde og høyde er låst til bildets størrelsesforhold.
                </div>

                <!-- Height-over-max warning -->
                <div
                  v-if="showHeightWarning"
                  style="display:flex;align-items:flex-start;gap:10px;background:rgba(231,185,78,.1);border:1px solid rgba(231,185,78,.28);border-radius:10px;padding:10px 14px"
                >
                  <i class="fa-solid fa-triangle-exclamation" style="color:var(--gold);flex-shrink:0;margin-top:2px"></i>
                  <span style="font-size:13px;color:var(--gold)">
                    Vi må lime flere banner for å oppnå din ønskede størrelse
                  </span>
                </div>
              </div>
            </div>

            <p v-if="setHeightError" class="error-box" style="margin-top:10px">
              <i class="fa-solid fa-circle-exclamation"></i> {{ setHeightError }}
            </p>
          </div>

          <!-- Final size display -->
          <div v-if="computedWidthCm > 0">
            <div class="field-label">Endelig størrelse</div>
            <div class="display" style="font-size:26px;color:var(--text);margin-top:4px">
              {{ computedWidthCm }} × {{ heightCm }} cm
            </div>
            <div v-if="selectedMaterial" style="font-size:13px;color:var(--faint);margin-top:4px">
              {{ selectedMaterial.name }}
            </div>
          </div>

          <!-- Quantity -->
          <div style="display:flex;align-items:center;gap:12px">
            <label for="qty" style="font-size:14px;color:var(--muted);font-weight:600">Antall</label>
            <input
              id="qty"
              v-model.number="qty"
              type="number"
              min="1"
              max="1000"
              class="dark-input"
              style="width:90px"
            />
          </div>

          <!-- Eyelet (malje) option -->
          <div>
            <div class="field-label" style="margin-bottom:8px">
              Maljer (øyebolter)
              <span style="font-size:13px;font-weight:400;color:var(--faint);margin-left:4px">tilvalg</span>
            </div>
            <div style="display:grid;gap:8px">
              <label
                v-for="opt in ([
                  { value: 'None',        label: 'Ingen maljer',        sub: 'Uten hull' },
                  { value: 'FourCorners', label: '4 maljer (hjørner)',   sub: 'En i hvert hjørne' },
                  { value: 'PerMeter',    label: 'Maljer per meter',     sub: `Ca. 1 per 100 cm – ${eyeletCount} stk totalt` },
                ] as const)"
                :key="opt.value"
                class="eyelet-option"
                :class="{ 'eyelet-option--active': eyeletOption === opt.value }"
              >
                <input type="radio" :value="opt.value" v-model="eyeletOption" style="display:none" />
                <div style="flex:1">
                  <div style="font-weight:600;font-size:14px;color:var(--text)">{{ opt.label }}</div>
                  <div style="font-size:13px;color:var(--faint)">{{ opt.sub }}</div>
                </div>
                <div
                  v-if="opt.value !== 'None' && eyeletPriceNok > 0"
                  style="font-size:13px;color:var(--accent);font-weight:600;white-space:nowrap"
                >
                  +{{ formatNok(countEyelets(computedWidthCm, heightCm, opt.value) * eyeletPriceNok) }}
                </div>
                <div class="eyelet-radio">
                  <div class="radio-outer" :class="{ 'radio-outer--active': eyeletOption === opt.value }">
                    <div v-if="eyeletOption === opt.value" class="radio-inner"></div>
                  </div>
                </div>
              </label>
            </div>
          </div>

          <!-- Price box -->
          <div style="border-top:1px solid var(--line-soft);padding-top:16px;display:grid;gap:8px">
            <div style="display:flex;justify-content:space-between;font-size:14px">
              <span style="color:var(--muted)">Bannerpris</span>
              <span style="color:var(--text);font-weight:500">
                <span v-if="priceLoading" style="color:var(--faint)">Beregner…</span>
                <span v-else-if="customPriceNok != null">{{ formatNok(customPriceNok) }}</span>
                <span v-else style="color:var(--faint)">–</span>
              </span>
            </div>
            <div
              v-if="eyeletFeePerUnit > 0"
              style="display:flex;justify-content:space-between;font-size:14px"
            >
              <span style="color:var(--muted)">Maljer ({{ eyeletCount }} stk)</span>
              <span style="color:var(--text);font-weight:500">{{ formatNok(eyeletFeePerUnit) }}</span>
            </div>
            <div style="display:flex;justify-content:space-between;font-size:14px">
              <span style="color:var(--muted)">Antall</span>
              <span style="color:var(--text);font-weight:500">{{ qty }} stk</span>
            </div>
            <div
              style="display:flex;justify-content:space-between;font-size:16px;padding-top:10px;border-top:1px solid var(--line-soft)"
            >
              <span style="font-weight:700;color:var(--text)">Delsum</span>
              <span style="font-weight:800;color:var(--accent)">
                <span v-if="customPriceNok != null">{{ formatNok(lineTotal) }}</span>
                <span v-else style="color:var(--faint)">–</span>
              </span>
            </div>
            <p style="font-size:13px;color:var(--faint)">
              Frakt og eventuelt ekspressgebyr beregnes i kassen.
            </p>
          </div>

          <div v-if="priceError" class="error-box">
            <i class="fa-solid fa-circle-exclamation"></i> {{ priceError }}
          </div>

          <!-- Soft login nudge -->
          <div v-if="!auth.isLoggedIn" class="notice-gold">
            <i class="fa-solid fa-circle-info"></i>
            <span style="font-size:13px">
              Du vil bli bedt om å
              <RouterLink to="/login?redirect=/checkout" style="color:var(--accent);font-weight:600">logge inn</RouterLink>
              eller
              <RouterLink to="/register" style="color:var(--accent);font-weight:600">registrere deg</RouterLink>
              for å fullføre bestillingen.
            </span>
          </div>

          <button
            type="button"
            class="btn btn-primary"
            style="width:100%;justify-content:center;padding:14px;font-size:16px;border-radius:12px"
            :disabled="customPriceNok == null || qty < 1 || priceLoading || settingHeight || rotating"
            @click="addToCartAndCheckout"
          >
            <i class="fa-solid fa-cart-shopping"></i>
            Legg i handlekurven
          </button>
        </div>
      </section>
    </template>
  </div>
</template>

<style scoped>
.step-badge {
  width: 30px;
  height: 30px;
  border-radius: 50%;
  background: var(--accent);
  color: var(--accent-ink);
  display: grid;
  place-items: center;
  font-weight: 700;
  font-size: 14px;
  flex-shrink: 0;
}
.bb-panel {
  background: var(--surface);
  border: 1px solid var(--line-soft);
  border-radius: var(--radius);
  padding: 26px;
}
.hint-grid {
  display: grid;
  grid-template-columns: repeat(3, 1fr);
  gap: 14px;
}
@media (max-width: 640px) {
  .hint-grid { grid-template-columns: 1fr; }
  .step-grid { grid-template-columns: 1fr !important; }
}
.hint-card {
  background: var(--surface);
  border: 1px solid var(--line-soft);
  border-radius: 12px;
  padding: 16px 18px;
}
.hint-ico {
  font-size: 20px;
  color: var(--accent);
  margin-bottom: 10px;
}
.field-label {
  font-size: 13px;
  text-transform: uppercase;
  letter-spacing: .06em;
  color: var(--faint);
  font-weight: 700;
}
.dark-input {
  background: var(--surface-2);
  border: 1px solid var(--line);
  border-radius: 9px;
  padding: 8px 12px;
  font-size: 15px;
  color: var(--text);
  font-family: var(--font-ui);
  outline: none;
  transition: border-color 0.15s, box-shadow 0.15s;
}
.dark-input:focus {
  border-color: var(--accent);
  box-shadow: 0 0 0 3px rgba(255,106,61,.18);
}
.notice-gold {
  display: flex;
  align-items: flex-start;
  gap: 10px;
  background: rgba(231,185,78,.1);
  border: 1px solid rgba(231,185,78,.28);
  border-radius: 12px;
  padding: 12px 16px;
  font-size: 14px;
  color: var(--gold);
}
.notice-gold i { margin-top: 2px; flex-shrink: 0; }
.notice-gold a { text-decoration: none; }
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

/* ── Rotation buttons ─────────────────────────────────────────── */
.rotate-btn {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 8px;
  background: var(--surface);
  border: 1.5px solid var(--line-soft);
  border-radius: 10px;
  padding: 10px 14px;
  font-size: 14px;
  font-weight: 600;
  color: var(--text);
  cursor: pointer;
  transition: border-color 0.15s, background 0.15s;
  font-family: var(--font-ui);
}
.rotate-btn:hover:not(:disabled) {
  border-color: var(--accent);
  background: rgba(255,106,61,.06);
}
.rotate-btn:disabled { opacity: 0.5; cursor: not-allowed; }

/* ── Size option cards ────────────────────────────────────────── */
.size-option {
  display: flex;
  align-items: center;
  gap: 12px;
  background: var(--surface-2);
  border: 1.5px solid var(--line);
  border-radius: 12px;
  padding: 12px 14px;
  cursor: pointer;
  transition: border-color 0.15s, background 0.15s;
  user-select: none;
}
.size-option:hover:not(.size-option--disabled) { border-color: var(--muted); }
.size-option--active {
  border-color: var(--accent) !important;
  background: rgba(255,106,61,.07) !important;
}
.size-option--disabled {
  cursor: default;
  opacity: 0.6;
}

.size-option-icon {
  width: 36px;
  flex-shrink: 0;
  display: flex;
  align-items: center;
  justify-content: center;
}
.size-icon-rect {
  background: rgba(255,255,255,.12);
  border: 1.5px solid var(--line);
  border-radius: 3px;
  width: 100%;
  min-height: 14px;
  max-height: 36px;
}
.size-option--active .size-icon-rect {
  border-color: var(--accent);
  background: rgba(255,106,61,.18);
}

.size-option-radio { flex-shrink: 0; }

/* ── Material option (inside custom expanded) ─────────────────── */
.mat-option {
  display: flex;
  align-items: center;
  gap: 10px;
  background: var(--surface);
  border: 1.5px solid var(--line);
  border-radius: 10px;
  padding: 10px 12px;
  cursor: pointer;
  transition: border-color 0.15s, background 0.15s;
  user-select: none;
}
.mat-option:hover:not(.mat-option--disabled) { border-color: var(--muted); }
.mat-option--active {
  border-color: var(--accent) !important;
  background: rgba(255,106,61,.07) !important;
}
.mat-option--disabled { cursor: default; opacity: 0.6; }

/* ── Radio buttons ────────────────────────────────────────────── */
.radio-outer {
  width: 16px;
  height: 16px;
  border-radius: 50%;
  border: 2px solid var(--line);
  display: flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
  transition: border-color 0.15s;
}
.radio-outer--active { border-color: var(--accent); }
.radio-inner {
  width: 7px;
  height: 7px;
  border-radius: 50%;
  background: var(--accent);
}

/* ── Eyelet option selector ───────────────────────────────────── */
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
</style>

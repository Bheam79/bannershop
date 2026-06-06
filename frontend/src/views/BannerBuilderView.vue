<script setup lang="ts">
import { ref, computed, watch, onMounted } from 'vue'
import { useRouter, RouterLink } from 'vue-router'
import { useAuthStore } from '@/stores/auth'
import { useCartStore } from '@/stores/cart'
import { fetchSizes } from '@/api/shop'
import type { BannerSize, CartItem } from '@/types'
import UploadZone from '@/components/banner-builder/UploadZone.vue'
import BannerPreviewEditor from '@/components/banner-builder/BannerPreviewEditor.vue'
import type { UploadResponse } from '@/api/bannerBuilder'

const router = useRouter()
const auth = useAuthStore()
const cart = useCartStore()

// ── Upload state ─────────────────────────────────────────────────────────────
const design = ref<UploadResponse | null>(null)

// Editor state mirrored from BannerPreviewEditor `change` event
const heightCm = ref<number>(150)
const computedWidthCm = ref<number>(0)
const rotationDegrees = ref<number>(0)

// Quantity
const qty = ref<number>(1)

// Pricing
const customPriceNok = ref<number | null>(null)
const priceLoading = ref<boolean>(false)
const priceError = ref<string | null>(null)

// Pricing requires us to find the right "custom width" size for the selected height
// material. We re-fetch sizes when height changes to get the calculated price
// for the new (customWidthCm, heightCm) combination.
const allSizes = ref<BannerSize[]>([])
const sizesLoading = ref<boolean>(false)

function onUploaded(resp: UploadResponse) {
  design.value = resp
  heightCm.value = resp.selectedHeightCm
  computedWidthCm.value = resp.computedWidthCm
  rotationDegrees.value = resp.rotationDegrees
}

function onEditorChange(state: {
  heightCm: number
  computedWidthCm: number
  rotationDegrees: number
}) {
  heightCm.value = state.heightCm
  computedWidthCm.value = state.computedWidthCm
  rotationDegrees.value = state.rotationDegrees
}

// ── Pricing logic ────────────────────────────────────────────────────────────
// Find the BannerSize row used for pricing the current (customWidthCm, heightCm).
// Strategy: pick the custom-width size with matching height (150 → 400g, 180 → 680g).
const pricingSize = computed<BannerSize | null>(() => {
  return allSizes.value
    .find((s) => s.isCustomWidth && s.heightCm === heightCm.value) ?? null
})

const selectedMaterial = computed(() => pricingSize.value?.material ?? null)

const materialLabel = computed(() => {
  if (heightCm.value === 180) return '680g Heavy Duty (180 cm rull)'
  return '400g Frontlit (160 cm rull)'
})

async function loadSizesForHeight() {
  if (!computedWidthCm.value) return
  sizesLoading.value = true
  priceError.value = null
  try {
    allSizes.value = await fetchSizes(computedWidthCm.value)
    // Pricing for this (customWidth, height) — read the matching custom-width row.
    const match = allSizes.value
      .find((s) => s.isCustomWidth && s.heightCm === heightCm.value)
    customPriceNok.value = match?.calculatedPrice ?? null
    if (customPriceNok.value == null) {
      priceError.value = 'Fant ikke prisinformasjon for valgt materiale.'
    }
  } catch (e: unknown) {
    const ex = e as { response?: { data?: { error?: string } }; message?: string }
    priceError.value = ex.response?.data?.error || ex.message || 'Kunne ikke beregne pris.'
    customPriceNok.value = null
  } finally {
    sizesLoading.value = false
  }
}

// Debounced re-fetch on dimension or height change
let priceTimer: ReturnType<typeof setTimeout> | null = null
watch([computedWidthCm, heightCm], () => {
  if (!design.value) return
  priceLoading.value = true
  if (priceTimer) clearTimeout(priceTimer)
  priceTimer = setTimeout(async () => {
    await loadSizesForHeight()
    priceLoading.value = false
  }, 250)
})

const lineTotal = computed(() => (customPriceNok.value ?? 0) * qty.value)

function formatNok(n: number): string {
  return new Intl.NumberFormat('nb-NO', { maximumFractionDigits: 0 }).format(n) + ' kr'
}

// ── Add to cart + checkout ───────────────────────────────────────────────────
function addToCartAndCheckout() {
  if (!design.value || !pricingSize.value || customPriceNok.value == null) return

  const item: CartItem = {
    bannerSizeId: pricingSize.value.id,
    bannerSizeName: `Egen design ${computedWidthCm.value} × ${heightCm.value} cm`,
    customWidthCm: computedWidthCm.value,
    heightCm: heightCm.value,
    quantity: qty.value,
    unitPriceNok: customPriceNok.value,
    designId: design.value.designId,
    notes: `Banner design #${design.value.designId} (lastet opp av kunde, rotasjon ${rotationDegrees.value}°)`,
  }
  cart.addItem(item)
  router.push('/checkout')
}

// Pre-fetch sizes once so material info is available immediately after upload.
onMounted(async () => {
  try {
    allSizes.value = await fetchSizes()
  } catch {
    // Non-fatal: pricing fetch is triggered post-upload anyway.
  }
})
</script>

<template>
  <div class="max-w-5xl mx-auto px-4 py-8 sm:py-12">
    <!-- Header -->
    <header class="mb-8 text-center">
      <h1 class="text-3xl sm:text-4xl font-bold text-gray-900 mb-3">
        Lag ditt eget banner
      </h1>
      <p class="text-lg text-gray-600 max-w-2xl mx-auto">
        Last opp din egen design — vi beregner størrelsen automatisk og trykker
        på det banneret du velger.
      </p>
      <!-- AI builder promo card -->
      <RouterLink
        to="/banner-builder/ai"
        class="inline-flex items-center gap-2 mt-5 bg-purple-700 hover:bg-purple-800 text-white font-semibold px-5 py-2.5 rounded-lg text-sm shadow-sm transition"
      >
        ✨ AI-generert feiringsbanner — 95 kr
      </RouterLink>
    </header>

    <!-- Auth notice (page is public, but upload requires login) -->
    <div
      v-if="!auth.isLoggedIn && !design"
      class="mb-6 bg-amber-50 border border-amber-200 rounded-lg px-4 py-3 text-sm text-amber-900"
    >
      <strong>Tips:</strong> Du må være innlogget for å lagre designet ditt.
      <RouterLink to="/login?redirect=/banner-builder" class="underline font-medium">
        Logg inn
      </RouterLink>
      eller
      <RouterLink to="/register" class="underline font-medium">
        registrer en konto
      </RouterLink>
      før du laster opp.
    </div>

    <!-- Step 1: Upload -->
    <section v-if="!design" class="mb-10">
      <div class="flex items-baseline gap-3 mb-4">
        <span class="bg-blue-700 text-white rounded-full w-7 h-7 flex items-center justify-center font-bold text-sm">1</span>
        <h2 class="text-xl font-semibold text-gray-900">Last opp din design</h2>
      </div>
      <UploadZone @uploaded="onUploaded" />

      <!-- Empty-state hint -->
      <div class="mt-6 grid sm:grid-cols-3 gap-3 text-sm text-gray-600">
        <div class="bg-gray-50 rounded-lg p-3 border border-gray-200">
          <div class="text-2xl mb-1">📐</div>
          <strong class="text-gray-800 block">Automatisk størrelse</strong>
          Vi beregner bredden ut fra bildets størrelsesforhold.
        </div>
        <div class="bg-gray-50 rounded-lg p-3 border border-gray-200">
          <div class="text-2xl mb-1">↻</div>
          <strong class="text-gray-800 block">Roter</strong>
          Snu bildet 90° om det er feil vei.
        </div>
        <div class="bg-gray-50 rounded-lg p-3 border border-gray-200">
          <div class="text-2xl mb-1">🧵</div>
          <strong class="text-gray-800 block">Klar til trykk</strong>
          Sydde kanter og maljer i hjørnene inkludert.
        </div>
      </div>
    </section>

    <!-- Step 2: Preview + edit -->
    <template v-else>
      <section class="grid lg:grid-cols-2 gap-8 mb-8">
        <div class="bg-white border border-gray-200 rounded-xl p-6">
          <div class="flex items-baseline justify-between mb-4">
            <div class="flex items-baseline gap-3">
              <span class="bg-blue-700 text-white rounded-full w-7 h-7 flex items-center justify-center font-bold text-sm">2</span>
              <h2 class="text-xl font-semibold text-gray-900">Tilpass</h2>
            </div>
            <button
              type="button"
              class="text-sm text-gray-500 hover:text-blue-700 underline"
              @click="design = null"
            >
              Last opp en annen
            </button>
          </div>
          <BannerPreviewEditor
            :design-id="design.designId"
            :initial-height-cm="design.selectedHeightCm"
            :initial-computed-width-cm="design.computedWidthCm"
            :initial-rotation-degrees="design.rotationDegrees"
            @change="onEditorChange"
          />
        </div>

        <!-- Step 3: Dimensions summary + add to cart -->
        <div class="bg-white border border-gray-200 rounded-xl p-6 flex flex-col">
          <div class="flex items-baseline gap-3 mb-4">
            <span class="bg-blue-700 text-white rounded-full w-7 h-7 flex items-center justify-center font-bold text-sm">3</span>
            <h2 class="text-xl font-semibold text-gray-900">Oppsummering</h2>
          </div>

          <div class="space-y-4 flex-1">
            <div>
              <div class="text-xs uppercase tracking-wider text-gray-500 font-semibold">Endelig størrelse</div>
              <div class="text-2xl font-bold text-gray-900 mt-1">
                {{ computedWidthCm }} × {{ heightCm }} cm
              </div>
            </div>

            <div>
              <div class="text-xs uppercase tracking-wider text-gray-500 font-semibold">Materiale</div>
              <div class="text-base font-medium text-gray-900 mt-1">
                {{ materialLabel }}
              </div>
              <div v-if="selectedMaterial" class="text-xs text-gray-500">
                Rull-bredde: {{ selectedMaterial.widthCm }} cm
              </div>
            </div>

            <!-- Quantity -->
            <div class="flex items-center gap-3">
              <label for="qty" class="text-sm text-gray-700 font-medium">Antall</label>
              <input
                id="qty"
                v-model.number="qty"
                type="number"
                min="1"
                max="1000"
                class="w-24 border border-gray-300 rounded-md px-3 py-2 focus:outline-none focus:ring-2 focus:ring-blue-500"
              />
            </div>

            <!-- Price box -->
            <div class="border-t border-gray-200 pt-4">
              <div class="flex justify-between text-sm">
                <span class="text-gray-600">Stykkpris</span>
                <span class="text-gray-900 font-medium">
                  <span v-if="priceLoading || sizesLoading" class="text-gray-400">Beregner…</span>
                  <span v-else-if="customPriceNok != null">{{ formatNok(customPriceNok) }}</span>
                  <span v-else class="text-gray-400">–</span>
                </span>
              </div>
              <div class="flex justify-between text-sm mt-1">
                <span class="text-gray-600">Antall × pris</span>
                <span class="text-gray-900 font-medium">
                  <span v-if="customPriceNok != null">{{ qty }} × {{ formatNok(customPriceNok) }}</span>
                  <span v-else class="text-gray-400">–</span>
                </span>
              </div>
              <div class="flex justify-between text-base pt-2 mt-2 border-t border-gray-100">
                <span class="font-semibold text-gray-900">Delsum</span>
                <span class="font-bold text-blue-700">
                  <span v-if="customPriceNok != null">{{ formatNok(lineTotal) }}</span>
                  <span v-else class="text-gray-400">–</span>
                </span>
              </div>
              <p class="text-xs text-gray-500 mt-2">
                Frakt og eventuelt ekspressgebyr beregnes i kassen.
              </p>
            </div>

            <p v-if="priceError" class="text-sm text-red-700 bg-red-50 border border-red-200 rounded-lg px-3 py-2">
              {{ priceError }}
            </p>
          </div>

          <button
            type="button"
            class="mt-6 w-full bg-blue-700 hover:bg-blue-800 disabled:bg-gray-300 disabled:cursor-not-allowed text-white font-semibold py-3 rounded-lg transition"
            :disabled="customPriceNok == null || qty < 1 || priceLoading || sizesLoading"
            @click="addToCartAndCheckout"
          >
            🛒 Legg i handlekurv og gå til kasse
          </button>
        </div>
      </section>
    </template>
  </div>
</template>

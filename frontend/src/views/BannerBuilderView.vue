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
      <!-- AI builder promo -->
      <RouterLink
        to="/banner-builder/ai"
        class="btn btn-primary"
        style="font-size:14px;padding:10px 20px"
      >
        <i class="fa-solid fa-wand-magic-sparkles"></i> AI-generert feiringsbanner — 95 kr
      </RouterLink>
    </header>

    <!-- Auth notice (page is public, but upload requires login) -->
    <div
      v-if="!auth.isLoggedIn && !design"
      class="notice-gold"
      style="margin-bottom:1.5rem"
    >
      <i class="fa-solid fa-circle-info"></i>
      <span>
        <strong>Tips:</strong> Du må være innlogget for å lagre designet ditt.
        <RouterLink to="/login?redirect=/banner-builder" style="color:var(--accent);font-weight:600">Logg inn</RouterLink>
        eller
        <RouterLink to="/register" style="color:var(--accent);font-weight:600">registrer en konto</RouterLink>
        før du laster opp.
      </span>
    </div>

    <!-- Step 1: Upload -->
    <section v-if="!design" style="margin-bottom:2.5rem">
      <div style="display:flex;align-items:center;gap:12px;margin-bottom:18px">
        <span class="step-badge">1</span>
        <h2 class="display" style="font-size:20px;color:var(--text)">Last opp din design</h2>
      </div>
      <UploadZone @uploaded="onUploaded" />

      <!-- Hint cards -->
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

    <!-- Step 2: Preview + edit -->
    <template v-else>
      <section style="display:grid;grid-template-columns:1fr 1fr;gap:24px;margin-bottom:2rem" class="step-grid">
        <!-- Preview / edit panel -->
        <div class="bb-panel">
          <div style="display:flex;align-items:center;justify-content:space-between;margin-bottom:18px">
            <div style="display:flex;align-items:center;gap:12px">
              <span class="step-badge">2</span>
              <h2 class="display" style="font-size:20px;color:var(--text)">Tilpass</h2>
            </div>
            <button
              type="button"
              style="font-size:13.5px;color:var(--accent);font-weight:600;background:none;border:none;cursor:pointer;padding:0"
              @click="design = null"
            >
              <i class="fa-solid fa-arrow-rotate-left" style="font-size:11px;margin-right:4px"></i> Last opp en annen
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

        <!-- Step 3: Summary + cart -->
        <div class="bb-panel" style="display:flex;flex-direction:column">
          <div style="display:flex;align-items:center;gap:12px;margin-bottom:18px">
            <span class="step-badge">3</span>
            <h2 class="display" style="font-size:20px;color:var(--text)">Oppsummering</h2>
          </div>

          <div style="flex:1;display:grid;gap:18px">
            <div>
              <div class="field-label">Endelig størrelse</div>
              <div class="display" style="font-size:26px;color:var(--text);margin-top:4px">
                {{ computedWidthCm }} × {{ heightCm }} cm
              </div>
            </div>

            <div>
              <div class="field-label">Materiale</div>
              <div style="font-size:15px;color:var(--text);margin-top:4px;font-weight:500">
                {{ materialLabel }}
              </div>
              <div v-if="selectedMaterial" style="font-size:12.5px;color:var(--faint);margin-top:2px">
                Rull-bredde: {{ selectedMaterial.widthCm }} cm
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

            <!-- Price box -->
            <div style="border-top:1px solid var(--line-soft);padding-top:16px;display:grid;gap:8px">
              <div style="display:flex;justify-content:space-between;font-size:14px">
                <span style="color:var(--muted)">Stykkpris</span>
                <span style="color:var(--text);font-weight:500">
                  <span v-if="priceLoading || sizesLoading" style="color:var(--faint)">Beregner…</span>
                  <span v-else-if="customPriceNok != null">{{ formatNok(customPriceNok) }}</span>
                  <span v-else style="color:var(--faint)">–</span>
                </span>
              </div>
              <div style="display:flex;justify-content:space-between;font-size:14px">
                <span style="color:var(--muted)">Antall × pris</span>
                <span style="color:var(--text);font-weight:500">
                  <span v-if="customPriceNok != null">{{ qty }} × {{ formatNok(customPriceNok) }}</span>
                  <span v-else style="color:var(--faint)">–</span>
                </span>
              </div>
              <div style="display:flex;justify-content:space-between;font-size:16px;padding-top:10px;border-top:1px solid var(--line-soft)">
                <span style="font-weight:700;color:var(--text)">Delsum</span>
                <span style="font-weight:800;color:var(--accent)">
                  <span v-if="customPriceNok != null">{{ formatNok(lineTotal) }}</span>
                  <span v-else style="color:var(--faint)">–</span>
                </span>
              </div>
              <p style="font-size:12.5px;color:var(--faint)">
                Frakt og eventuelt ekspressgebyr beregnes i kassen.
              </p>
            </div>

            <div v-if="priceError" class="error-box">
              <i class="fa-solid fa-circle-exclamation"></i> {{ priceError }}
            </div>
          </div>

          <button
            type="button"
            class="btn btn-primary"
            style="width:100%;justify-content:center;padding:14px;font-size:16px;border-radius:12px;margin-top:20px"
            :disabled="customPriceNok == null || qty < 1 || priceLoading || sizesLoading"
            @click="addToCartAndCheckout"
          >
            <i class="fa-solid fa-cart-shopping"></i>
            Legg i handlekurv og gå til kasse
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
  font-size: 12px;
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
.notice-gold i {
  margin-top: 2px;
  flex-shrink: 0;
}
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
</style>

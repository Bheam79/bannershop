<script setup lang="ts">
import { ref, computed, watch, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { useCartStore } from '@/stores/cart'
import { useCheckoutStore } from '@/stores/checkout'
import { useAuthStore } from '@/stores/auth'
import { calculateShipping } from '@/api/shop'
import type { PackingMode } from '@/api/shop'
import { getBannerDesign } from '@/api/bannerBuilder'
import type { DeliveryType, EyeletOption, ShippingEstimate } from '@/types'
import { countEyelets } from '@/types'

const router = useRouter()
const cart = useCartStore()
const checkout = useCheckoutStore()
const auth = useAuthStore()

// ── Thumbnails (BANNERSH-140) ────────────────────────────────────────────────
// Maps `designId` → resolved preview URL. Items rendered twice share a single
// lookup. The Map drops back to an in-flight Promise while a fetch is pending
// so concurrent renders don't trigger duplicate API calls.
const thumbCache = ref<Map<number, string>>(new Map())
const thumbInflight = new Map<number, Promise<string | null>>()

function thumbFor(designId: number | undefined): string | null {
  if (designId == null) return null
  return thumbCache.value.get(designId) ?? null
}

async function ensureThumb(designId: number | undefined): Promise<void> {
  if (designId == null) return
  if (thumbCache.value.has(designId)) return
  if (thumbInflight.has(designId)) {
    await thumbInflight.get(designId)
    return
  }
  const p = (async () => {
    try {
      const design = await getBannerDesign(designId)
      // The preview endpoint is [AllowAnonymous] so the URL can be used directly
      // in an <img src>. Fall back to empty string if the backend ever returns null.
      const url = design.previewUrl || ''
      if (url) thumbCache.value.set(designId, url)
      return url || null
    } catch {
      return null
    } finally {
      thumbInflight.delete(designId)
    }
  })()
  thumbInflight.set(designId, p)
  await p
}

function preloadThumbnails() {
  for (const item of cart.items) {
    if (item.previewUrl && item.designId != null && !thumbCache.value.has(item.designId)) {
      thumbCache.value.set(item.designId, item.previewUrl)
    }
    if (item.designId != null && !thumbCache.value.has(item.designId)) {
      void ensureThumb(item.designId)
    }
  }
}

// ── Redirect if cart is empty ────────────────────────────────────────────────
onMounted(() => {
  if (cart.items.length === 0) {
    router.replace('/')
    return
  }
  preloadThumbnails()
})

// Refresh thumbnails if the cart changes (e.g. removed item, new design added).
watch(() => cart.items.length, () => {
  preloadThumbnails()
})

// Also redirect if the cart becomes empty after a removal during checkout.
watch(
  () => cart.items.length,
  (n) => {
    if (n === 0) router.replace('/')
  },
)

// ── Remove item from cart ────────────────────────────────────────────────────
function removeItem(idx: number) {
  const item = cart.items[idx]
  const label = item?.bannerSizeName ?? 'banner'
  if (!window.confirm(`Fjerne "${label}" fra handlekurven?`)) return
  cart.removeItem(idx)
}

// ── Form state (pre-fill from checkout store if user came back) ──────────────
const recipientName = ref(checkout.recipientName)
const addressLine1 = ref(checkout.address.line1)
const postalCode = ref(checkout.address.postalCode)
const city = ref(checkout.address.city)
const deliveryType = ref<DeliveryType>(checkout.deliveryType)
/** BANNERSH-174: cart-level packaging method. Default Folded, then Rolled. */
const packingMode = ref<PackingMode>(checkout.packingMode)

// ── Shipping calculation ─────────────────────────────────────────────────────
const shippingEstimate = ref<ShippingEstimate | null>(null)
const shippingLoading = ref(false)
const shippingError = ref<string | null>(null)

async function computeShipping() {
  shippingError.value = null
  const pc = postalCode.value.trim()
  if (!/^\d{4}$/.test(pc)) {
    shippingEstimate.value = null
    return
  }
  const firstItem = cart.items[0]
  if (!firstItem?.bannerSizeId) {
    shippingEstimate.value = null
    return
  }
  shippingLoading.value = true
  try {
    shippingEstimate.value = await calculateShipping({
      postalCode: pc,
      city: city.value.trim() || undefined,
      bannerSizeId: firstItem.bannerSizeId,
      customWidthCm: firstItem.customWidthCm ?? undefined,
      qty: cart.itemCount,
      packingMode: packingMode.value,
    })
  } catch {
    shippingError.value = 'Kunne ikke beregne frakt. Sjekk postnummeret og prøv igjen.'
    shippingEstimate.value = null
  } finally {
    shippingLoading.value = false
  }
}

let shippingTimer: ReturnType<typeof setTimeout> | null = null
function scheduleShipping() {
  if (shippingTimer) clearTimeout(shippingTimer)
  shippingTimer = setTimeout(computeShipping, 500)
}
watch(postalCode, scheduleShipping)
watch(city, () => {
  if (/^\d{4}$/.test(postalCode.value.trim())) scheduleShipping()
})
// Recompute when packing mode changes (Folded vs Rolled affects parcel size + price).
watch(packingMode, () => {
  if (/^\d{4}$/.test(postalCode.value.trim())) scheduleShipping()
})

// ── Price calculations ───────────────────────────────────────────────────────
const subtotal = computed(() => cart.subtotal)

const shippingCost = computed(() => {
  if (deliveryType.value === 'Pickup') return 0
  if (!shippingEstimate.value) return 0
  return deliveryType.value === 'Express'
    ? shippingEstimate.value.express.costNok
    : shippingEstimate.value.standard.costNok
})

const expressFee = computed(() => (deliveryType.value === 'Express' ? 500 : 0))

const total = computed(() => subtotal.value + shippingCost.value + expressFee.value)

// MVA (25%) is included in Norwegian prices. To extract:  total × 0.25 / 1.25 = total × 0.2
const vatAmount = computed(() => total.value * 0.2)

// Fixed production lead times shown to the customer.
// Standard: 14 calendar days (2-week production — carrier transit is within this window).
// Express: 3 calendar days.
// These are independent of the Bring carrier estimate (which only covers transit days and
// caused the bug where only 3 days were added, showing today+3 instead of today+14).
const STANDARD_LEAD_DAYS = 14
const EXPRESS_LEAD_DAYS = 3

const estimatedDays = computed(() => {
  if (deliveryType.value === 'Pickup') return null
  return deliveryType.value === 'Express' ? EXPRESS_LEAD_DAYS : STANDARD_LEAD_DAYS
})

const estimatedDeliveryText = computed(() => {
  if (estimatedDays.value == null) return null
  const d = new Date()
  d.setDate(d.getDate() + estimatedDays.value)
  return d.toLocaleDateString('nb-NO', { day: '2-digit', month: 'long', year: 'numeric' })
})

// ── Form validation ──────────────────────────────────────────────────────────
const formErrors = ref<Record<string, string>>({})

function validate(): boolean {
  const errs: Record<string, string> = {}
  if (!recipientName.value.trim()) errs.recipientName = 'Navn er påkrevd'
  if (deliveryType.value !== 'Pickup') {
    if (!addressLine1.value.trim()) errs.addressLine1 = 'Adresse er påkrevd'
    if (!/^\d{4}$/.test(postalCode.value.trim())) errs.postalCode = 'Ugyldig postnummer (4 siffer)'
    if (!city.value.trim()) errs.city = 'Poststed er påkrevd'
  }
  formErrors.value = errs
  return Object.keys(errs).length === 0
}

// ── Proceed to payment ───────────────────────────────────────────────────────
function proceed() {
  if (!validate()) return
  if (deliveryType.value !== 'Pickup' && !shippingEstimate.value) {
    formErrors.value.postalCode = 'Beregn frakt før du fortsetter'
    return
  }
  // Save checkout state before any redirect so it's available on return
  checkout.setCheckout({
    recipientName: recipientName.value.trim(),
    address: {
      line1: addressLine1.value.trim(),
      postalCode: postalCode.value.trim(),
      city: city.value.trim(),
    },
    deliveryType: deliveryType.value,
    shippingCostNok: shippingCost.value,
    expressFeeNok: expressFee.value,
    packingMode: packingMode.value,
  })

  // Auth gate: cart + address survive in localStorage/store — the user just needs
  // to log in and they'll land back on /checkout/payment automatically.
  if (!auth.isLoggedIn) {
    router.push('/login?redirect=/checkout/payment')
    return
  }

  router.push('/checkout/payment')
}

// ── Formatting helpers ───────────────────────────────────────────────────────
function formatNok(n: number): string {
  return new Intl.NumberFormat('nb-NO', { maximumFractionDigits: 0 }).format(n) + ' kr'
}

// Eyelet labels — kept consistent with BannerBuilderView / AiBannerBuilderView wording.
function eyeletShortLabel(option: EyeletOption): string {
  switch (option) {
    case 'FourCorners': return '4 hjørner'
    case 'PerMeter':    return 'Per meter'
    default:            return 'Ingen'
  }
}

function eyeletCountFor(item: import('@/types').CartItem): number {
  // Compute width: prefer customWidthCm (set by the banner builder) when present.
  const widthCm = item.customWidthCm ?? 0
  return countEyelets(widthCm, item.heightCm, item.eyeletOption)
}
</script>

<template>
  <div class="checkout-wrap">
    <!-- Header / stepper -->
    <header class="checkout-header">
      <h1 class="display checkout-title">Handlekurv</h1>
      <nav class="stepper">
        <span class="stepper-step active">1. Handlekurv &amp; levering</span>
        <span class="stepper-sep">›</span>
        <span class="stepper-step">2. Betaling</span>
        <span class="stepper-sep">›</span>
        <span class="stepper-step">3. Bekreftelse</span>
      </nav>
    </header>

    <div class="checkout-grid">
      <!-- ── Left col: form ─────────────────────────────────────────────── -->
      <div class="checkout-main">

        <!-- Order summary -->
        <section class="panel">
          <h2 class="section-title">Din bestilling</h2>
          <ul class="item-list">
            <li
              v-for="(item, idx) in cart.items"
              :key="idx"
              class="item-row"
            >
              <!-- Thumbnail: design preview when we have a BannerDesign id,
                   otherwise a neutral placeholder. -->
              <div class="item-thumb" aria-hidden="true">
                <img
                  v-if="thumbFor(item.designId)"
                  :src="thumbFor(item.designId)!"
                  :alt="`Forhåndsvisning av ${item.bannerSizeName}`"
                />
                <i v-else class="fa-solid fa-image item-thumb-placeholder"></i>
              </div>

              <div class="item-info">
                <div class="item-name">{{ item.bannerSizeName }}</div>

                <!-- Base banner line -->
                <div class="item-sub">
                  {{ item.quantity }} stk × {{ formatNok(item.unitPriceNok) }}
                  <span style="color:var(--faint)">banner</span>
                </div>

                <!-- Eyelet sub-line: only when a paid option was picked -->
                <div
                  v-if="item.eyeletOption !== 'None' && item.eyeletFeeNok > 0"
                  class="item-eyelet-row"
                >
                  <span class="item-eyelet-label">
                    + Maljer
                    <span class="item-eyelet-badge">
                      {{ eyeletShortLabel(item.eyeletOption) }}<template v-if="eyeletCountFor(item) > 0">, {{ eyeletCountFor(item) }} stk</template>
                    </span>
                  </span>
                  <span class="item-eyelet-price">
                    +{{ formatNok(item.eyeletFeeNok * item.quantity) }}
                  </span>
                </div>
              </div>

              <div class="item-price">
                {{ formatNok((item.unitPriceNok + item.eyeletFeeNok) * item.quantity) }}
              </div>
              <button
                type="button"
                class="item-remove"
                :aria-label="`Fjern ${item.bannerSizeName} fra handlekurven`"
                title="Fjern fra handlekurven"
                @click="removeItem(idx)"
              >
                <i class="fa-solid fa-trash"></i>
              </button>
            </li>
          </ul>
        </section>

        <!-- Delivery address -->
        <section class="panel">
          <h2 class="section-title">
            {{ deliveryType === 'Pickup' ? 'Kontaktinformasjon' : 'Leveringsadresse' }}
          </h2>
          <div class="form-grid">
            <!-- Recipient name (always required) -->
            <div class="form-field full">
              <label class="field-label" for="recipientName">
                {{ deliveryType === 'Pickup' ? 'Navn (for å identifisere bestillingen)' : 'Mottaker' }}
              </label>
              <input
                id="recipientName"
                v-model="recipientName"
                type="text"
                autocomplete="name"
                placeholder="Fullt navn"
                class="field-input"
                :class="{ 'field-input--error': formErrors.recipientName }"
              />
              <p v-if="formErrors.recipientName" class="field-error">
                {{ formErrors.recipientName }}
              </p>
            </div>

            <!-- Address fields — only shown for Standard / Express delivery -->
            <template v-if="deliveryType !== 'Pickup'">
              <!-- Address line 1 -->
              <div class="form-field full">
                <label class="field-label" for="addressLine1">Gateadresse</label>
                <input
                  id="addressLine1"
                  v-model="addressLine1"
                  type="text"
                  autocomplete="address-line1"
                  placeholder="Gatenavn og husnummer"
                  class="field-input"
                  :class="{ 'field-input--error': formErrors.addressLine1 }"
                />
                <p v-if="formErrors.addressLine1" class="field-error">
                  {{ formErrors.addressLine1 }}
                </p>
              </div>

              <!-- Postal code -->
              <div class="form-field">
                <label class="field-label" for="postalCode">Postnummer</label>
                <input
                  id="postalCode"
                  v-model="postalCode"
                  type="text"
                  inputmode="numeric"
                  maxlength="4"
                  autocomplete="postal-code"
                  placeholder="0000"
                  class="field-input"
                  :class="{ 'field-input--error': formErrors.postalCode }"
                />
                <p v-if="formErrors.postalCode" class="field-error">
                  {{ formErrors.postalCode }}
                </p>
              </div>

              <!-- City -->
              <div class="form-field">
                <label class="field-label" for="city">Poststed</label>
                <input
                  id="city"
                  v-model="city"
                  type="text"
                  autocomplete="address-level2"
                  placeholder="Oslo"
                  class="field-input"
                  :class="{ 'field-input--error': formErrors.city }"
                />
                <p v-if="formErrors.city" class="field-error">
                  {{ formErrors.city }}
                </p>
              </div>
            </template>
          </div>
        </section>

        <!-- Packaging method — BANNERSH-174 -->
        <!-- Only relevant for shipped orders; pickup customers pack their own. -->
        <section v-if="deliveryType !== 'Pickup'" class="panel">
          <h2 class="section-title">Pakkemetode</h2>
          <p style="font-size:0.8125rem;color:var(--muted);margin-bottom:1rem">
            Alle varer i handlekurven pakkes på samme måte. Pakkemetoden påvirker fraktkostnaden.
          </p>
          <div class="packing-grid">
            <!-- Folded (default) -->
            <button
              type="button"
              class="packing-btn"
              :class="{ 'packing-btn--active': packingMode === 'Folded' }"
              @click="packingMode = 'Folded'"
            >
              <div class="packing-btn__inner">
                <div class="packing-btn__icon" :class="{ 'packing-btn__icon--active': packingMode === 'Folded' }">
                  <i class="fa-solid fa-box"></i>
                </div>
                <div class="packing-btn__body">
                  <div class="packing-btn__title">
                    Brettet
                    <span class="badge-packing-default">Anbefalt</span>
                  </div>
                  <div class="packing-btn__sub">Flat eske 50 × 60 cm</div>
                </div>
                <div class="packing-btn__radio">
                  <div class="radio-outer" :class="{ 'radio-outer--active': packingMode === 'Folded' }">
                    <div v-if="packingMode === 'Folded'" class="radio-inner"></div>
                  </div>
                </div>
              </div>
            </button>
            <!-- Rolled -->
            <button
              type="button"
              class="packing-btn"
              :class="{ 'packing-btn--active': packingMode === 'Rolled' }"
              @click="packingMode = 'Rolled'"
            >
              <div class="packing-btn__inner">
                <div class="packing-btn__icon" :class="{ 'packing-btn__icon--active': packingMode === 'Rolled' }">
                  <i class="fa-solid fa-scroll"></i>
                </div>
                <div class="packing-btn__body">
                  <div class="packing-btn__title">Rullet</div>
                  <div class="packing-btn__sub">Sendt som rør (tubes)</div>
                </div>
                <div class="packing-btn__radio">
                  <div class="radio-outer" :class="{ 'radio-outer--active': packingMode === 'Rolled' }">
                    <div v-if="packingMode === 'Rolled'" class="radio-inner"></div>
                  </div>
                </div>
              </div>
            </button>
          </div>
        </section>

        <!-- Delivery type -->
        <section class="panel">
          <h2 class="section-title">Leveringstype</h2>
          <div class="delivery-grid">
            <!-- Standard -->
            <button
              type="button"
              data-delivery="standard"
              class="delivery-btn"
              :class="{ 'delivery-btn--active': deliveryType === 'Standard' }"
              @click="deliveryType = 'Standard'"
            >
              <div class="delivery-btn__inner">
                <div class="delivery-btn__icon">
                  <i class="fa-solid fa-truck"></i>
                </div>
                <div class="delivery-btn__body">
                  <div class="delivery-btn__title">Standard</div>
                  <div class="delivery-btn__sub">Estimert levering: ca. 2 uker</div>
                  <div v-if="deliveryType === 'Standard'" class="delivery-btn__eta">
                    {{ estimatedDeliveryText }}
                  </div>
                  <div v-if="shippingEstimate" class="delivery-btn__price">
                    {{ formatNok(shippingEstimate.standard.costNok) }}
                  </div>
                  <div v-else-if="shippingLoading" class="delivery-btn__loading">Beregner…</div>
                  <div v-else class="delivery-btn__hint">Skriv inn postnummer for pris</div>
                </div>
                <div class="delivery-btn__radio">
                  <div class="radio-outer" :class="{ 'radio-outer--active': deliveryType === 'Standard' }">
                    <div v-if="deliveryType === 'Standard'" class="radio-inner"></div>
                  </div>
                </div>
              </div>
            </button>

            <!-- Express -->
            <button
              type="button"
              data-delivery="express"
              class="delivery-btn"
              :class="{ 'delivery-btn--active': deliveryType === 'Express' }"
              @click="deliveryType = 'Express'"
            >
              <div class="delivery-btn__inner">
                <div class="delivery-btn__icon delivery-btn__icon--express">
                  <i class="fa-solid fa-bolt"></i>
                </div>
                <div class="delivery-btn__body">
                  <div class="delivery-btn__title">
                    Ekspress
                    <span class="badge-express">+500 kr gebyr</span>
                  </div>
                  <div class="delivery-btn__sub">Estimert levering: 3–5 dager</div>
                  <div v-if="deliveryType === 'Express'" class="delivery-btn__eta">
                    {{ estimatedDeliveryText }}
                  </div>
                  <div v-if="shippingEstimate" class="delivery-btn__price delivery-btn__price--express">
                    {{ formatNok(shippingEstimate.express.costNok) }}
                    <span class="delivery-btn__price-note">frakt + 500 kr gebyr</span>
                  </div>
                  <div v-else-if="shippingLoading" class="delivery-btn__loading">Beregner…</div>
                  <div v-else class="delivery-btn__hint">Skriv inn postnummer for pris</div>
                </div>
                <div class="delivery-btn__radio">
                  <div class="radio-outer" :class="{ 'radio-outer--active': deliveryType === 'Express' }">
                    <div v-if="deliveryType === 'Express'" class="radio-inner"></div>
                  </div>
                </div>
              </div>
            </button>

            <!-- Pickup -->
            <button
              type="button"
              data-delivery="pickup"
              class="delivery-btn delivery-btn--pickup"
              :class="{ 'delivery-btn--active': deliveryType === 'Pickup' }"
              @click="deliveryType = 'Pickup'"
            >
              <div class="delivery-btn__inner">
                <div class="delivery-btn__icon delivery-btn__icon--pickup">
                  <i class="fa-solid fa-store"></i>
                </div>
                <div class="delivery-btn__body">
                  <div class="delivery-btn__title">
                    Henting
                    <span class="badge-pickup">Gratis</span>
                  </div>
                  <div class="delivery-btn__sub">Rigedalen 43, 4626 Kristiansand</div>
                  <div class="delivery-btn__sub">Mandag–fredag kl. 09–15. Oppmøte KUN etter avtale.</div>
                  <div class="delivery-btn__price delivery-btn__price--pickup">0 kr</div>
                </div>
                <div class="delivery-btn__radio">
                  <div class="radio-outer" :class="{ 'radio-outer--active': deliveryType === 'Pickup' }">
                    <div v-if="deliveryType === 'Pickup'" class="radio-inner"></div>
                  </div>
                </div>
              </div>
            </button>
          </div>

          <p v-if="shippingError && deliveryType !== 'Pickup'" class="alert-error">
            <i class="fa-solid fa-circle-exclamation"></i>
            {{ shippingError }}
          </p>
        </section>
      </div>

      <!-- ── Right col: order total ──────────────────────────────────────── -->
      <aside class="checkout-aside">
        <div class="panel summary-sticky">
          <h2 class="section-title">Ordresammendrag</h2>

          <dl class="summary-list">
            <div class="summary-row">
              <dt class="summary-label">Varer ({{ cart.itemCount }} stk)</dt>
              <dd class="summary-value">{{ formatNok(subtotal) }}</dd>
            </div>

            <!-- Packing mode line (only for shipped orders) -->
            <div v-if="deliveryType !== 'Pickup'" class="summary-row">
              <dt class="summary-label">Pakking</dt>
              <dd class="summary-value summary-faint" style="font-size:0.8125rem">
                {{ packingMode === 'Folded' ? 'Brettet (50×60 cm)' : 'Rullet (rør)' }}
              </dd>
            </div>

            <div class="summary-row">
              <dt class="summary-label">
                <template v-if="deliveryType === 'Pickup'">Henting</template>
                <template v-else>Frakt ({{ deliveryType === 'Express' ? 'ekspress' : 'standard' }})</template>
              </dt>
              <dd class="summary-value">
                <span v-if="deliveryType === 'Pickup'" class="summary-free">Gratis</span>
                <span v-else-if="shippingLoading" class="summary-faint">Beregner…</span>
                <span v-else-if="shippingEstimate">{{ formatNok(shippingCost) }}</span>
                <span v-else class="summary-faint">–</span>
              </dd>
            </div>

            <div v-if="deliveryType === 'Express'" class="summary-row">
              <dt class="summary-label">Ekspress produksjonsgebyr</dt>
              <dd class="summary-value">{{ formatNok(expressFee) }}</dd>
            </div>

            <div class="summary-divider">
              <div class="summary-row summary-row--total">
                <dt>Totalt inkl. MVA</dt>
                <dd class="summary-total-price">{{ formatNok(total) }}</dd>
              </div>
              <div class="summary-row">
                <dt class="summary-faint">Herav MVA (25%)</dt>
                <dd class="summary-faint">{{ formatNok(vatAmount) }}</dd>
              </div>
            </div>
          </dl>

          <div v-if="estimatedDeliveryText && deliveryType !== 'Pickup'" class="alert-delivery">
            <i class="fa-solid fa-truck"></i>
            <div>
              <div class="alert-delivery__label">Estimert levering</div>
              <div>{{ estimatedDeliveryText }}</div>
            </div>
          </div>

          <p v-if="deliveryType !== 'Pickup' && !shippingEstimate" class="summary-hint">
            Skriv inn leveringsadresse for å se fraktkostnader.
          </p>

          <button
            type="button"
            class="btn btn-primary btn-proceed"
            @click="proceed"
          >
            Gå til betaling
            <i class="fa-solid fa-arrow-right"></i>
          </button>
        </div>
      </aside>
    </div>
  </div>
</template>

<style scoped>
/* ── Layout ─────────────────────────────────────────────────── */
.checkout-wrap {
  max-width: 1100px;
  margin: 0 auto;
  padding: 2rem 1.25rem 3rem;
}

.checkout-header {
  margin-bottom: 2rem;
}

.checkout-title {
  font-size: clamp(1.5rem, 3vw, 2rem);
  color: var(--text);
  margin-bottom: 0.5rem;
}

.checkout-grid {
  display: grid;
  gap: 2rem;
}
@media (min-width: 1024px) {
  .checkout-grid { grid-template-columns: 1fr minmax(0, 340px); }
}

.checkout-main { display: flex; flex-direction: column; gap: 1.25rem; }
.checkout-aside { display: flex; flex-direction: column; gap: 1rem; }

/* ── Stepper ────────────────────────────────────────────────── */
.stepper {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  font-size: 0.875rem;
}
.stepper-step {
  color: var(--faint);
}
.stepper-step.active {
  color: var(--accent);
  font-weight: 600;
}
.stepper-sep {
  color: var(--line);
}

/* ── Section title ──────────────────────────────────────────── */
.section-title {
  font-size: 1rem;
  font-weight: 700;
  color: var(--text);
  margin-bottom: 1rem;
  font-family: var(--font-display);
}

/* ── Item list ──────────────────────────────────────────────── */
.item-list {
  list-style: none;
  padding: 0;
  margin: 0;
  display: flex;
  flex-direction: column;
}
.item-row {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  padding: 0.75rem 0;
  border-bottom: 1px solid var(--line-soft);
}
.item-row:last-child { border-bottom: none; }
.item-info { flex: 1; min-width: 0; }
.item-name { font-weight: 600; color: var(--text); }
.item-sub { font-size: 0.8125rem; color: var(--muted); margin-top: 0.125rem; }
.item-price { font-weight: 700; color: var(--text); }

/* ── Thumbnail (BANNERSH-140) ────────────────────────────────── */
.item-thumb {
  flex-shrink: 0;
  width: 64px;
  height: 64px;
  border-radius: 0;
  background: var(--surface-2);
  border: 1px solid var(--line-soft);
  display: flex;
  align-items: center;
  justify-content: center;
  overflow: hidden;
}
.item-thumb img {
  width: 100%;
  height: 100%;
  object-fit: contain;
  background: #2a251e;
}
.item-thumb-placeholder {
  color: var(--faint);
  font-size: 18px;
}

/* ── Eyelet sub-line ─────────────────────────────────────────── */
.item-eyelet-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 0.5rem;
  margin-top: 0.25rem;
  font-size: 0.8125rem;
  color: var(--muted);
}
.item-eyelet-label {
  display: inline-flex;
  align-items: center;
  gap: 0.4rem;
  flex-wrap: wrap;
}
.item-eyelet-badge {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  font-size: 0.7rem;
  font-weight: 600;
  background: rgba(231, 185, 78, 0.14);
  color: var(--gold);
  border: 1px solid rgba(231, 185, 78, 0.3);
  padding: 1px 7px;
  border-radius: 99px;
  white-space: nowrap;
}
.item-eyelet-price {
  font-weight: 600;
  color: var(--text);
  white-space: nowrap;
}
.item-remove {
  background: transparent;
  border: 1px solid var(--line);
  color: var(--muted);
  width: 32px;
  height: 32px;
  border-radius: 8px;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  cursor: pointer;
  transition: color 0.15s, border-color 0.15s, background 0.15s;
  flex-shrink: 0;
}
.item-remove:hover {
  color: #e05252;
  border-color: rgba(220, 60, 60, 0.55);
  background: rgba(220, 60, 60, 0.08);
}
.item-remove:focus-visible {
  outline: none;
  box-shadow: 0 0 0 3px rgba(220, 60, 60, 0.25);
  border-color: rgba(220, 60, 60, 0.7);
}

/* ── Form ───────────────────────────────────────────────────── */
.form-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 1rem;
}
.form-field { display: flex; flex-direction: column; }
.form-field.full { grid-column: 1 / -1; }
@media (max-width: 640px) {
  .form-grid { grid-template-columns: 1fr; }
  .form-field.full { grid-column: auto; }
}

.field-label {
  display: flex;
  align-items: center;
  gap: 6px;
  font-size: 0.8125rem;
  font-weight: 600;
  color: var(--muted);
  margin-bottom: 6px;
}

.field-input {
  width: 100%;
  background: var(--surface-2);
  border: 1px solid var(--line);
  border-radius: 10px;
  padding: 10px 14px;
  font-size: 0.9375rem;
  color: var(--text);
  font-family: var(--font-ui);
  outline: none;
  transition: border-color 0.15s, box-shadow 0.15s;
}
.field-input::placeholder { color: var(--faint); }
.field-input:focus {
  border-color: var(--accent);
  box-shadow: 0 0 0 3px rgba(255, 106, 61, 0.18);
}
.field-input--error {
  border-color: #e05252 !important;
}
.field-error {
  margin-top: 4px;
  font-size: 0.75rem;
  color: #f4a57a;
}

/* ── Packaging buttons (BANNERSH-174) ───────────────────────── */
.packing-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 0.75rem;
}
@media (max-width: 500px) { .packing-grid { grid-template-columns: 1fr; } }

.packing-btn {
  text-align: left;
  background: var(--surface-2);
  border: 2px solid var(--line);
  border-radius: 14px;
  padding: 0.875rem;
  cursor: pointer;
  transition: border-color 0.15s, background 0.15s;
  color: var(--text);
  font-family: var(--font-ui);
}
.packing-btn:hover { border-color: var(--muted); }
.packing-btn--active {
  border-color: var(--accent) !important;
  background: rgba(255, 106, 61, 0.08) !important;
}
.packing-btn__inner {
  display: flex;
  align-items: flex-start;
  gap: 10px;
}
.packing-btn__icon {
  flex-shrink: 0;
  width: 34px;
  height: 34px;
  border-radius: 9px;
  background: var(--surface);
  border: 1px solid var(--line);
  display: flex;
  align-items: center;
  justify-content: center;
  color: var(--muted);
  font-size: 14px;
  margin-top: 1px;
  transition: background 0.15s, border-color 0.15s, color 0.15s;
}
.packing-btn--active .packing-btn__icon {
  background: rgba(255, 106, 61, 0.15);
  border-color: var(--accent);
  color: var(--accent);
}
.packing-btn__body { flex: 1; min-width: 0; }
.packing-btn__title {
  font-weight: 700;
  font-size: 0.9rem;
  color: var(--text);
  display: flex;
  align-items: center;
  gap: 6px;
  flex-wrap: wrap;
}
.packing-btn__sub {
  font-size: 0.8rem;
  color: var(--muted);
  margin-top: 2px;
}
.packing-btn__radio { flex-shrink: 0; margin-top: 2px; }
.badge-packing-default {
  font-size: 0.68rem;
  font-weight: 600;
  background: rgba(78, 201, 132, 0.18);
  color: #4ec984;
  border: 1px solid rgba(78, 201, 132, 0.35);
  padding: 1px 7px;
  border-radius: 99px;
}

/* ── Delivery buttons ───────────────────────────────────────── */
.delivery-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 0.75rem;
}
/* Pickup spans full width as it's the 3rd option */
.delivery-btn--pickup {
  grid-column: 1 / -1;
}
@media (max-width: 640px) { .delivery-grid { grid-template-columns: 1fr; } }

.delivery-btn {
  text-align: left;
  background: var(--surface-2);
  border: 2px solid var(--line);
  border-radius: 14px;
  padding: 1rem;
  cursor: pointer;
  transition: border-color 0.15s, background 0.15s;
  color: var(--text);
  font-family: var(--font-ui);
}
.delivery-btn:hover {
  border-color: var(--muted);
}
.delivery-btn--active {
  border-color: var(--accent) !important;
  background: rgba(255, 106, 61, 0.08) !important;
}

.delivery-btn__inner {
  display: flex;
  align-items: flex-start;
  gap: 12px;
}
.delivery-btn__icon {
  flex-shrink: 0;
  width: 36px;
  height: 36px;
  border-radius: 9px;
  background: var(--surface);
  border: 1px solid var(--line);
  display: flex;
  align-items: center;
  justify-content: center;
  color: var(--muted);
  font-size: 15px;
  margin-top: 2px;
}
.delivery-btn--active .delivery-btn__icon {
  background: rgba(255, 106, 61, 0.15);
  border-color: var(--accent);
  color: var(--accent);
}
.delivery-btn__icon--express { color: var(--gold); }
.delivery-btn--active .delivery-btn__icon--express {
  background: rgba(231, 185, 78, 0.15);
  border-color: var(--gold);
  color: var(--gold);
}
.delivery-btn__icon--pickup { color: #4ec984; }
.delivery-btn--active .delivery-btn__icon--pickup {
  background: rgba(78, 201, 132, 0.15);
  border-color: #4ec984;
  color: #4ec984;
}

.delivery-btn__body { flex: 1; min-width: 0; }
.delivery-btn__title {
  font-weight: 700;
  font-size: 0.9375rem;
  color: var(--text);
  display: flex;
  align-items: center;
  gap: 6px;
  flex-wrap: wrap;
}
.delivery-btn__sub {
  font-size: 0.8125rem;
  color: var(--muted);
  margin-top: 2px;
}
.delivery-btn__eta {
  font-size: 0.8rem;
  color: var(--faint);
  margin-top: 4px;
}
.delivery-btn__price {
  margin-top: 8px;
  font-weight: 700;
  font-size: 0.9375rem;
  color: var(--accent);
}
.delivery-btn__price--express { color: var(--gold); }
.delivery-btn__price-note {
  font-size: 0.75rem;
  font-weight: 400;
  color: var(--muted);
  margin-left: 4px;
}
.delivery-btn__loading,
.delivery-btn__hint {
  margin-top: 8px;
  font-size: 0.8125rem;
  color: var(--faint);
}

.badge-express {
  font-size: 0.7rem;
  font-weight: 600;
  background: rgba(231, 185, 78, 0.18);
  color: var(--gold);
  border: 1px solid rgba(231, 185, 78, 0.35);
  padding: 1px 7px;
  border-radius: 99px;
}
.badge-pickup {
  font-size: 0.7rem;
  font-weight: 600;
  background: rgba(78, 201, 132, 0.18);
  color: #4ec984;
  border: 1px solid rgba(78, 201, 132, 0.35);
  padding: 1px 7px;
  border-radius: 99px;
}
.delivery-btn__price--pickup { color: #4ec984; }

.delivery-btn__radio { flex-shrink: 0; margin-top: 2px; }
.radio-outer {
  width: 18px;
  height: 18px;
  border-radius: 50%;
  border: 2px solid var(--line);
  display: flex;
  align-items: center;
  justify-content: center;
  transition: border-color 0.15s;
}
.radio-outer--active { border-color: var(--accent); }
.radio-inner {
  width: 8px;
  height: 8px;
  border-radius: 50%;
  background: var(--accent);
}

/* ── Alerts ─────────────────────────────────────────────────── */
.alert-error {
  display: flex;
  align-items: center;
  gap: 9px;
  margin-top: 0.75rem;
  padding: 10px 14px;
  background: rgba(220, 60, 60, 0.12);
  border: 1px solid rgba(220, 60, 60, 0.3);
  border-radius: 10px;
  color: #f4a57a;
  font-size: 0.875rem;
}
.alert-error i { color: #e05252; flex-shrink: 0; }

.alert-delivery {
  display: flex;
  align-items: flex-start;
  gap: 10px;
  margin-top: 1rem;
  padding: 10px 14px;
  background: rgba(60, 180, 100, 0.1);
  border: 1px solid rgba(60, 180, 100, 0.25);
  border-radius: 10px;
  font-size: 0.875rem;
  color: #7de0a8;
}
.alert-delivery i { color: #4ec984; flex-shrink: 0; margin-top: 3px; }
.alert-delivery__label { font-weight: 600; margin-bottom: 1px; }

/* ── Summary sidebar ────────────────────────────────────────── */
.summary-sticky {
  position: sticky;
  top: 1rem;
}
.summary-list {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
  margin-bottom: 0;
  font-size: 0.875rem;
}
.summary-row {
  display: flex;
  justify-content: space-between;
  align-items: baseline;
}
.summary-label { color: var(--muted); }
.summary-value { font-weight: 600; color: var(--text); }
.summary-faint { color: var(--faint); font-size: 0.8125rem; }
.summary-free { color: #4ec984; font-weight: 600; }
.summary-divider {
  border-top: 1px solid var(--line-soft);
  padding-top: 0.75rem;
  margin-top: 0.25rem;
  display: flex;
  flex-direction: column;
  gap: 0.375rem;
}
.summary-row--total {
  font-weight: 700;
  font-size: 1rem;
  color: var(--text);
}
.summary-total-price { color: var(--accent); }
.summary-hint {
  margin-top: 0.75rem;
  font-size: 0.78rem;
  color: var(--faint);
}

.btn-proceed {
  width: 100%;
  justify-content: center;
  padding: 13px;
  font-size: 1rem;
  border-radius: 12px;
  margin-top: 1.25rem;
}
</style>

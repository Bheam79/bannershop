<script setup lang="ts">
// BANNERSH-183: Dedicated one-pager for buying AI credit packs.
// Reached from the AI credit badge in the header (NavBar → AiCreditBadge).
//
// Flow:
//   1. Loads both pack tiers via GET /api/ai-credits/packs (public endpoint).
//   2. User picks small or large.
//   3. POST /api/ai-credits/packs/buy returns a Stripe PaymentIntent client secret.
//   4. Customer fills in the Stripe card element and we confirm the payment.
//   5. The webhook grants the credits asynchronously; we refresh the balance.
//
// Lazy-initialises Stripe (loadStripe) only after a pack has been selected so the
// first paint is fast and the page is usable even if Stripe.js fails to load.
import { ref, computed, onMounted, onBeforeUnmount, watch } from 'vue'
import { useRouter } from 'vue-router'
import { loadStripe } from '@stripe/stripe-js'
import type { Stripe, StripeCardElement } from '@stripe/stripe-js'
import { useAuthStore } from '@/stores/auth'
import { useAiCreditsStore } from '@/stores/aiCredits'
import {
  getCreditPackInfo,
  buyCreditPack,
  activateCreditPack,
  getAiCreditsBalance,
  type CreditPackInfo,
} from '@/api/aiCredits'
import { formatNok } from '@/utils/format'

const router = useRouter()
const auth = useAuthStore()
const creditsStore = useAiCreditsStore()

// ── Pack tier data ────────────────────────────────────────────────────────────
type PackKey = 'small' | 'large'

const packs = ref<CreditPackInfo | null>(null)
const packsLoading = ref(true)
const packsError = ref<string | null>(null)
const selectedPack = ref<PackKey>('large')

async function loadPacks() {
  packsLoading.value = true
  packsError.value = null
  try {
    packs.value = await getCreditPackInfo()
  } catch (e: unknown) {
    const ex = e as { response?: { data?: { error?: string } }; message?: string }
    packsError.value =
      ex.response?.data?.error ?? ex.message ?? 'Kunne ikke laste kredittpakker. Prøv igjen.'
  } finally {
    packsLoading.value = false
  }
}

// ── Stripe state ──────────────────────────────────────────────────────────────
type PurchasePhase = 'pick' | 'loading' | 'card' | 'processing' | 'done' | 'error'
const phase = ref<PurchasePhase>('pick')
const purchaseError = ref<string | null>(null)
const stripeCardError = ref<string | null>(null)

const packDetails = ref<{
  clientSecret: string
  creditCount: number
  priceNok: number
  pack: PackKey
} | null>(null)

const stripeRef = ref<Stripe | null>(null)
const cardElement = ref<StripeCardElement | null>(null)
const cardMountEl = ref<HTMLDivElement | null>(null)

// Resolve Stripe publishable key: build-time env var → runtime /api/config/stripe
async function resolveStripePublishableKey(): Promise<string | null> {
  const envKey = import.meta.env.VITE_STRIPE_PUBLISHABLE_KEY as string | undefined
  if (envKey && !envKey.startsWith('pk_test_REPLACE') && !envKey.startsWith('REPLACE_')) {
    return envKey
  }
  try {
    const resp = await fetch('/api/config/stripe')
    if (resp.ok) {
      const data: { publishableKey?: string } = await resp.json()
      if (data.publishableKey && data.publishableKey.length > 0) return data.publishableKey
    }
  } catch {
    // fall through
  }
  return null
}

async function initStripe(): Promise<boolean> {
  if (stripeRef.value) return true
  const key = await resolveStripePublishableKey()
  if (!key) {
    purchaseError.value =
      'Stripe er ikke konfigurert. Sett stripe_publishable_key i adminpanelet.'
    phase.value = 'error'
    return false
  }
  try {
    const stripe = await loadStripe(key)
    if (!stripe) {
      purchaseError.value = 'Stripe kunne ikke lastes. Prøv igjen.'
      phase.value = 'error'
      return false
    }
    stripeRef.value = stripe
    return true
  } catch {
    purchaseError.value = 'Stripe kunne ikke initialiseres.'
    phase.value = 'error'
    return false
  }
}

// Mount Stripe card element once the mount point appears (phase = 'card')
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

// ── Actions ──────────────────────────────────────────────────────────────────

async function startPurchase() {
  if (!auth.isLoggedIn) {
    void router.push(`/login?redirect=${encodeURIComponent('/account/credits')}`)
    return
  }
  purchaseError.value = null
  stripeCardError.value = null
  phase.value = 'loading'

  try {
    const resp = await buyCreditPack(selectedPack.value)
    packDetails.value = {
      clientSecret: resp.clientSecret,
      creditCount: resp.creditCount,
      priceNok: resp.priceNok,
      pack: resp.pack,
    }
    const ok = await initStripe()
    if (!ok) return
    phase.value = 'card'
  } catch (e: unknown) {
    const ex = e as { response?: { data?: { error?: string } }; message?: string }
    purchaseError.value =
      ex.response?.data?.error ?? ex.message ?? 'Kunne ikke starte betaling. Prøv igjen.'
    phase.value = 'error'
  }
}

async function confirmPayment() {
  if (!stripeRef.value || !cardElement.value || !packDetails.value) return
  phase.value = 'processing'
  stripeCardError.value = null

  const { error } = await stripeRef.value.confirmCardPayment(packDetails.value.clientSecret, {
    payment_method: { card: cardElement.value },
  })

  if (error) {
    stripeCardError.value = error.message ?? 'Betalingen feilet. Prøv igjen.'
    phase.value = 'card'
    return
  }

  // Payment confirmed client-side. Call the activate endpoint so credits are
  // granted synchronously — without waiting for the Stripe webhook (BANNERSH-213).
  // The PI id lives at the start of the clientSecret before "_secret_".
  // clientSecret format: "pi_xxx_secret_yyy" → PI id is the part before "_secret_"
  const piId = packDetails.value.clientSecret.split('_secret_')[0] ?? packDetails.value.clientSecret
  try {
    const result = await activateCreditPack(piId)
    // Update the store from the server's authoritative balance.
    creditsStore.setBalance(result.creditsRemaining)
  } catch {
    // Activate failed (e.g. network blip) — fall back to a plain balance refresh
    // so the badge still updates once the webhook fires.
    try {
      const balance = await getAiCreditsBalance()
      creditsStore.setBalance(balance.creditsRemaining, balance.hasUsedFreeGeneration)
    } catch {
      // Non-critical — visibilitychange in App.vue will re-sync on next focus.
    }
  }

  phase.value = 'done'
}

function resetForNewPurchase() {
  cardElement.value?.destroy()
  cardElement.value = null
  stripeRef.value = null
  packDetails.value = null
  purchaseError.value = null
  stripeCardError.value = null
  phase.value = 'pick'
}

function continueToBuilder() {
  void router.push('/banner-builder/ai')
}

// ── Computed helpers ─────────────────────────────────────────────────────────
const selectedPackDetails = computed(() => {
  if (!packs.value) return null
  return selectedPack.value === 'large' ? packs.value.large : packs.value.small
})

const perCreditPrice = computed(() => {
  const p = selectedPackDetails.value
  if (!p || p.creditCount <= 0) return null
  return Math.round((p.priceNok / p.creditCount) * 10) / 10
})

function pricePerCreditFor(creditCount: number, priceNok: number): number {
  if (creditCount <= 0) return 0
  return Math.round((priceNok / creditCount) * 10) / 10
}

// Per-credit prices may be fractional (e.g. 163,3 kr) — format with 1 decimal.
// Wrap the shared formatNok with the correct decimal count.
function formatNokDecimal(n: number | null | undefined): string {
  return formatNok(n, 1)
}

// ── Lifecycle ────────────────────────────────────────────────────────────────
onMounted(async () => {
  await loadPacks()
  // Default to large pack only if it actually beats the small pack on per-credit price.
  if (packs.value) {
    const small = pricePerCreditFor(packs.value.small.creditCount, packs.value.small.priceNok)
    const large = pricePerCreditFor(packs.value.large.creditCount, packs.value.large.priceNok)
    selectedPack.value = large <= small ? 'large' : 'small'
  }
  // Make sure the credit badge has fresh data when the user lands here.
  if (auth.isLoggedIn) void creditsStore.fetchBalance()
})

onBeforeUnmount(() => {
  cardElement.value?.destroy()
})
</script>

<template>
  <div class="credits-wrap">
    <header class="credits-header">
      <h1 class="display credits-title">
        <i class="fa-solid fa-wand-magic-sparkles"></i>
        Kjøp AI-kreditter
      </h1>
      <p class="credits-sub">
        Hver kreditt gir deg én AI-generert bannerversjon. Du kan alltid bruke en
        kreditt på en ny variant av et eksisterende banner uten å miste det
        forrige.
      </p>
      <div
        v-if="auth.isLoggedIn && creditsStore.creditsRemaining !== null"
        class="balance-pill"
      >
        <i class="fa-solid fa-wand-magic-sparkles"></i>
        Du har <strong>{{ creditsStore.creditsRemaining }}</strong>
        AI-kreditt<template v-if="creditsStore.creditsRemaining !== 1">er</template> igjen
      </div>
    </header>

    <!-- ── Loading packs ────────────────────────────────────────────────── -->
    <div v-if="packsLoading" class="loading-row">
      <i class="fa-solid fa-circle-notch fa-spin"></i> Henter pakker…
    </div>

    <!-- ── Failed to load packs ─────────────────────────────────────────── -->
    <div v-else-if="packsError" class="alert-error">
      <i class="fa-solid fa-circle-exclamation"></i>
      {{ packsError }}
      <button type="button" class="retry-btn" @click="loadPacks">Prøv igjen</button>
    </div>

    <!-- ── Phase: pick (or error) — show selection cards + CTA ──────────── -->
    <template v-else-if="packs && (phase === 'pick' || phase === 'loading' || phase === 'error')">
      <div class="pack-grid">
        <!-- Small -->
        <button
          type="button"
          class="pack-card"
          :class="{ 'pack-card-active': selectedPack === 'small' }"
          @click="selectedPack = 'small'"
        >
          <div class="pack-name">Liten pakke</div>
          <div class="pack-count">
            {{ packs.small.creditCount }}
            <span class="pack-count-lbl">kreditter</span>
          </div>
          <div class="pack-price">{{ formatNok(packs.small.priceNok) }}</div>
          <div class="pack-per">
            {{ formatNokDecimal(pricePerCreditFor(packs.small.creditCount, packs.small.priceNok)) }}
            per kreditt
          </div>
        </button>

        <!-- Large -->
        <button
          type="button"
          class="pack-card"
          :class="{ 'pack-card-active': selectedPack === 'large' }"
          @click="selectedPack = 'large'"
        >
          <div class="pack-badge">Best verdi</div>
          <div class="pack-name">Stor pakke</div>
          <div class="pack-count">
            {{ packs.large.creditCount }}
            <span class="pack-count-lbl">kreditter</span>
          </div>
          <div class="pack-price">{{ formatNok(packs.large.priceNok) }}</div>
          <div class="pack-per">
            {{ formatNokDecimal(pricePerCreditFor(packs.large.creditCount, packs.large.priceNok)) }}
            per kreditt
          </div>
        </button>
      </div>

      <!-- Errors -->
      <div v-if="phase === 'error' && purchaseError" class="alert-error">
        <i class="fa-solid fa-circle-exclamation"></i>
        {{ purchaseError }}
      </div>

      <!-- CTA -->
      <div class="cta-row">
        <button
          type="button"
          class="btn btn-primary cta-btn"
          :disabled="phase === 'loading'"
          @click="startPurchase"
        >
          <i v-if="phase === 'loading'" class="fa-solid fa-circle-notch fa-spin"></i>
          <i v-else class="fa-solid fa-credit-card"></i>
          <span>
            <template v-if="phase === 'loading'">Forbereder betaling…</template>
            <template v-else-if="selectedPackDetails">
              Fortsett med
              {{ selectedPackDetails.creditCount }} kreditter
              ({{ formatNok(selectedPackDetails.priceNok) }})
            </template>
            <template v-else>Fortsett</template>
          </span>
        </button>
        <p v-if="perCreditPrice !== null" class="cta-sub">
          <i class="fa-solid fa-shield-halved"></i>
          Sikret betaling via Stripe — {{ formatNokDecimal(perCreditPrice) }} per AI-bilde
        </p>
      </div>
    </template>

    <!-- ── Phase: card — show Stripe card element + Pay button ─────────── -->
    <section v-else-if="phase === 'card' || phase === 'processing'" class="panel pay-panel">
      <h2 class="section-title">
        Kortbetaling
        <span class="stripe-badge">
          <i class="fa-solid fa-lock"></i>
          Sikret av Stripe
        </span>
      </h2>

      <div v-if="packDetails" class="summary-row">
        <div>
          <div class="summary-name">
            <i class="fa-solid fa-wand-magic-sparkles"></i>
            {{ packDetails.creditCount }} AI-kreditter
            <span v-if="packDetails.pack === 'large'" class="summary-pack-badge">stor</span>
            <span v-else class="summary-pack-badge summary-pack-badge--neutral">liten</span>
          </div>
          <div class="summary-sub">
            {{ formatNokDecimal(pricePerCreditFor(packDetails.creditCount, packDetails.priceNok)) }} per kreditt
          </div>
        </div>
        <div class="summary-total">{{ formatNok(packDetails.priceNok) }}</div>
      </div>

      <div class="card-field">
        <label class="field-label">
          <i class="fa-solid fa-credit-card"></i>
          Kortdetaljer
        </label>
        <div ref="cardMountEl" class="stripe-mount" />
        <p v-if="stripeCardError" class="field-error">
          <i class="fa-solid fa-circle-exclamation"></i>
          {{ stripeCardError }}
        </p>
      </div>

      <button
        type="button"
        class="btn btn-primary cta-btn"
        :disabled="phase === 'processing'"
        @click="confirmPayment"
      >
        <i v-if="phase === 'processing'" class="fa-solid fa-circle-notch fa-spin"></i>
        <i v-else class="fa-solid fa-lock"></i>
        <span>
          {{
            phase === 'processing'
              ? 'Behandler betaling…'
              : `Betal ${formatNok(packDetails?.priceNok ?? 0)}`
          }}
        </span>
      </button>

      <button
        v-if="phase !== 'processing'"
        type="button"
        class="ghost-back"
        @click="resetForNewPurchase"
      >
        <i class="fa-solid fa-arrow-left"></i>
        Velg en annen pakke
      </button>

      <p class="secure-note">
        <i class="fa-solid fa-shield-halved"></i>
        Betalingen er kryptert og håndteres av Stripe. Vi lagrer ikke kortinformasjon.
      </p>
    </section>

    <!-- ── Phase: done ─────────────────────────────────────────────────── -->
    <section v-else-if="phase === 'done'" class="panel done-panel">
      <div class="done-icon-wrap">
        <i class="fa-solid fa-circle-check"></i>
      </div>
      <h2 class="display done-title">Betaling godkjent</h2>
      <p class="done-text">
        <template v-if="packDetails">
          Vi har lagt
          <strong>{{ packDetails.creditCount }} AI-kreditt<template v-if="packDetails.creditCount !== 1">er</template></strong>
          til kontoen din. Du kan begynne å bruke dem med en gang.
        </template>
        <template v-else>
          Vi har lagt kredittene til kontoen din.
        </template>
      </p>
      <div class="done-actions">
        <button type="button" class="btn btn-primary cta-btn" @click="continueToBuilder">
          <i class="fa-solid fa-wand-magic-sparkles"></i>
          Lag et banner nå
        </button>
        <button type="button" class="ghost-back" @click="resetForNewPurchase">
          Kjøp flere kreditter
        </button>
      </div>
    </section>
  </div>
</template>

<style scoped>
.credits-wrap {
  max-width: 760px;
  margin: 0 auto;
  padding: 2.5rem 1.25rem 4rem;
  display: flex;
  flex-direction: column;
  gap: 1.75rem;
}

/* ── Header ────────────────────────────────────────────────── */
.credits-header {
  text-align: center;
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 0.65rem;
}
.credits-title {
  font-size: clamp(1.6rem, 3vw, 2.1rem);
  color: var(--text);
  display: inline-flex;
  align-items: center;
  gap: 0.6rem;
  margin: 0;
}
.credits-title i { color: var(--accent); }
.credits-sub {
  font-size: 0.95rem;
  color: var(--muted);
  max-width: 38em;
  line-height: 1.55;
  margin: 0;
}
.balance-pill {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  margin-top: 6px;
  background: rgba(255, 106, 61, .12);
  border: 1px solid rgba(255, 106, 61, .3);
  border-radius: 999px;
  padding: 6px 14px;
  font-size: 13.5px;
  color: var(--accent);
  font-weight: 600;
}
.balance-pill strong {
  color: var(--text);
  font-weight: 700;
  font-variant-numeric: tabular-nums;
}

/* ── Loading / errors ──────────────────────────────────────── */
.loading-row {
  text-align: center;
  padding: 2.5rem 0;
  color: var(--muted);
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 10px;
  font-size: 0.95rem;
}
.loading-row i { color: var(--accent); }

.alert-error {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 14px 16px;
  background: rgba(220, 60, 60, .12);
  border: 1px solid rgba(220, 60, 60, .3);
  border-radius: 12px;
  color: #f4a57a;
  font-size: 0.9rem;
}
.alert-error i { color: #e05252; flex-shrink: 0; }
.retry-btn {
  all: unset;
  cursor: pointer;
  color: var(--accent);
  font-weight: 600;
  text-decoration: underline;
  text-underline-offset: 2px;
  margin-left: auto;
}
.retry-btn:hover { color: var(--accent-2); }

/* ── Pack grid ─────────────────────────────────────────────── */
.pack-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 1rem;
}
@media (max-width: 540px) {
  .pack-grid { grid-template-columns: 1fr; }
}

.pack-card {
  all: unset;
  cursor: pointer;
  box-sizing: border-box;
  display: flex;
  flex-direction: column;
  gap: 6px;
  padding: 22px 20px 18px;
  background: var(--surface);
  border: 1.5px solid var(--line);
  border-radius: 16px;
  text-align: left;
  position: relative;
  transition: border-color .15s, background .15s, transform .05s;
}
.pack-card:hover {
  border-color: var(--accent);
  background: rgba(255, 106, 61, .04);
}
.pack-card:active { transform: scale(.99); }
.pack-card-active {
  border-color: var(--accent);
  background: rgba(255, 106, 61, .08);
  box-shadow: 0 0 0 3px rgba(255, 106, 61, .15);
}

.pack-badge {
  position: absolute;
  top: -10px;
  right: 16px;
  background: var(--gold);
  color: #1a0d06;
  font-size: 11px;
  font-weight: 700;
  letter-spacing: .04em;
  text-transform: uppercase;
  padding: 4px 10px;
  border-radius: 99px;
}
.pack-name {
  font-size: 14px;
  font-weight: 700;
  color: var(--muted);
  text-transform: uppercase;
  letter-spacing: .06em;
}
.pack-count {
  font-family: var(--font-display);
  font-size: 36px;
  font-weight: 700;
  color: var(--text);
  display: inline-flex;
  align-items: baseline;
  gap: 6px;
  line-height: 1;
}
.pack-count-lbl {
  font-size: 14px;
  color: var(--muted);
  font-weight: 500;
  font-family: var(--font-ui);
}
.pack-price {
  font-size: 22px;
  font-weight: 700;
  color: var(--accent);
  margin-top: 4px;
  font-variant-numeric: tabular-nums;
}
.pack-per {
  font-size: 12.5px;
  color: var(--faint);
}

/* ── CTA row ───────────────────────────────────────────────── */
.cta-row {
  display: flex;
  flex-direction: column;
  align-items: stretch;
  gap: 10px;
}
.cta-btn {
  width: 100%;
  justify-content: center;
  padding: 15px 24px;
  font-size: 1rem;
  border-radius: 13px;
  display: inline-flex;
  align-items: center;
  gap: 9px;
}
.cta-sub {
  font-size: 0.8125rem;
  color: var(--faint);
  text-align: center;
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 6px;
  margin: 0;
}

/* ── Pay panel ────────────────────────────────────────────── */
.panel {
  background: var(--surface);
  border: 1px solid var(--line);
  border-radius: 16px;
  padding: 1.5rem;
}
.pay-panel { display: flex; flex-direction: column; gap: 1rem; }
.section-title {
  font-size: 1rem;
  font-weight: 700;
  color: var(--text);
  font-family: var(--font-display);
  display: flex;
  align-items: center;
  gap: 0.625rem;
  margin: 0;
  flex-wrap: wrap;
}
.stripe-badge {
  font-size: 0.75rem;
  font-weight: 500;
  color: var(--faint);
  background: var(--surface-2);
  border: 1px solid var(--line);
  padding: 2px 8px;
  border-radius: 99px;
  display: inline-flex;
  align-items: center;
  gap: 5px;
  margin-left: auto;
}

.summary-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 16px;
  padding: 14px 16px;
  background: var(--surface-2);
  border: 1px solid var(--line-soft);
  border-radius: 12px;
}
.summary-name {
  color: var(--text);
  font-weight: 600;
  display: flex;
  align-items: center;
  gap: 8px;
  font-size: 0.95rem;
}
.summary-name i { color: var(--accent); }
.summary-pack-badge {
  font-size: 10.5px;
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: .05em;
  background: var(--gold);
  color: #1a0d06;
  padding: 2px 7px;
  border-radius: 999px;
}
.summary-pack-badge--neutral {
  background: rgba(255,255,255,.08);
  color: var(--muted);
}
.summary-sub {
  font-size: 12.5px;
  color: var(--faint);
  margin-top: 3px;
}
.summary-total {
  font-size: 1.2rem;
  font-weight: 700;
  color: var(--text);
  font-variant-numeric: tabular-nums;
}

.card-field { display: flex; flex-direction: column; gap: 6px; }
.field-label {
  display: flex;
  align-items: center;
  gap: 6px;
  font-size: 0.8125rem;
  font-weight: 600;
  color: var(--muted);
}
.field-label i { color: var(--faint); font-size: 0.75rem; }

.stripe-mount {
  background: var(--surface-2);
  border: 1px solid var(--line);
  border-radius: 10px;
  padding: 13px 14px;
  min-height: 44px;
  transition: border-color .15s, box-shadow .15s;
}
.stripe-mount:focus-within {
  border-color: var(--accent);
  box-shadow: 0 0 0 3px rgba(255, 106, 61, .18);
}

.field-error {
  margin: 0;
  font-size: 0.8125rem;
  color: #f4a57a;
  display: flex;
  align-items: center;
  gap: 6px;
}

.ghost-back {
  all: unset;
  cursor: pointer;
  text-align: center;
  color: var(--muted);
  font-size: 0.875rem;
  font-weight: 600;
  padding: 6px;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: 7px;
  transition: color .15s;
}
.ghost-back:hover { color: var(--accent); }

.secure-note {
  margin: 0;
  font-size: 0.78rem;
  color: var(--faint);
  text-align: center;
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 6px;
}

/* ── Done panel ───────────────────────────────────────────── */
.done-panel {
  display: flex;
  flex-direction: column;
  align-items: center;
  text-align: center;
  gap: 14px;
  padding: 2.5rem 1.5rem;
}
.done-icon-wrap {
  width: 64px;
  height: 64px;
  border-radius: 50%;
  background: rgba(74, 222, 128, .12);
  border: 1px solid rgba(74, 222, 128, .3);
  display: grid;
  place-items: center;
  font-size: 28px;
  color: #4ade80;
}
.done-title {
  font-size: 1.4rem;
  color: var(--text);
  margin: 0;
}
.done-text {
  color: var(--muted);
  font-size: 0.95rem;
  max-width: 32em;
  line-height: 1.55;
  margin: 0;
}
.done-text strong { color: var(--text); }
.done-actions {
  display: flex;
  flex-direction: column;
  align-items: stretch;
  gap: 8px;
  width: 100%;
  max-width: 380px;
  margin-top: 6px;
}
</style>

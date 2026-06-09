<script setup lang="ts">
import { ref, computed, onMounted, onBeforeUnmount } from 'vue'
import { useRouter } from 'vue-router'
import { loadStripe } from '@stripe/stripe-js'
import type { Stripe, StripeCardElement } from '@stripe/stripe-js'
import { useCartStore } from '@/stores/cart'
import { useCheckoutStore } from '@/stores/checkout'
import { useAuthStore } from '@/stores/auth'
import { createOrderDraft } from '@/api/orders'

const router = useRouter()
const cart = useCartStore()
const checkout = useCheckoutStore()
const auth = useAuthStore()

// ── Guard: redirect if no address set, cart is empty, or not logged in ────────
onMounted(async () => {
  if (cart.items.length === 0 || !checkout.isReady()) {
    router.replace('/checkout')
    return
  }
  if (!auth.isLoggedIn) {
    router.replace('/login?redirect=/checkout/payment')
    return
  }
  await initStripe()
})

// ── Stripe ───────────────────────────────────────────────────────────────────
const stripeRef = ref<Stripe | null>(null)
const cardElement = ref<StripeCardElement | null>(null)
const cardMountEl = ref<HTMLDivElement | null>(null)
const stripeReady = ref(false)
const stripeError = ref<string | null>(null)

async function initStripe() {
  const key = import.meta.env.VITE_STRIPE_PUBLISHABLE_KEY as string | undefined
  if (!key || key.startsWith('pk_test_REPLACE')) {
    stripeError.value =
      'Stripe er ikke konfigurert (mangler VITE_STRIPE_PUBLISHABLE_KEY). ' +
      'Betalingsfunksjonen er ikke tilgjengelig i denne testmiljøet.'
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
          backgroundColor: '#2a251e',
        },
        invalid: { color: '#f4a57a' },
      },
      hidePostalCode: true,
    })
    cardElement.value = card

    // Mount after the DOM element is ready
    if (cardMountEl.value) {
      card.mount(cardMountEl.value)
      stripeReady.value = true
    }

    card.on('change', (event) => {
      if (event.error) {
        paymentError.value = event.error.message
      } else {
        paymentError.value = null
      }
    })
  } catch (err) {
    stripeError.value = 'Stripe kunne ikke initialiseres.'
    console.error('Stripe init error:', err)
  }
}

onBeforeUnmount(() => {
  cardElement.value?.unmount()
})

// ── Payment state ─────────────────────────────────────────────────────────────
const processing = ref(false)
const paymentError = ref<string | null>(null)
const apiError = ref<string | null>(null)

// ── Mock-payment modal (shown when Stripe is not configured) ──────────────────
const showMockModal = ref(false)
const mockPassword = ref('')
const mockPasswordError = ref<string | null>(null)
const mockProcessing = ref(false)

function openMockModal() {
  mockPassword.value = ''
  mockPasswordError.value = null
  showMockModal.value = true
}

function closeMockModal() {
  showMockModal.value = false
}

async function confirmMockPayment() {
  if (mockPassword.value !== 'test1234') {
    mockPasswordError.value = 'Feil passord. Prøv igjen.'
    return
  }
  mockPasswordError.value = null
  mockProcessing.value = true
  apiError.value = null

  let orderId: number
  let clientSecret: string

  try {
    const resp = await createOrderDraft({
      deliveryType: checkout.deliveryType,
      shippingAddress: checkout.deliveryType !== 'Pickup' ? {
        line1: checkout.address.line1,
        postalCode: checkout.address.postalCode,
        city: checkout.address.city,
        country: 'NO',
      } : undefined,
      items: cart.items
        .filter((item) => item.bannerSizeId != null)
        .map((item) => ({
          bannerSizeId: item.bannerSizeId!,
          customWidthCm: item.customWidthCm ?? undefined,
          quantity: item.quantity,
          notes: item.notes ?? undefined,
          eyeletOption: item.eyeletOption,
        })),
    })
    orderId = resp.orderId
    clientSecret = resp.clientSecret
  } catch (err: unknown) {
    const e = err as { response?: { data?: { error?: string } }; message?: string }
    apiError.value =
      e.response?.data?.error || e.message || 'Kunne ikke opprette ordre. Prøv igjen.'
    mockProcessing.value = false
    closeMockModal()
    return
  }

  // Mock bypass — backend returns pi_mock_* when Stripe is not configured
  if (clientSecret.startsWith('pi_mock_')) {
    cart.clear()
    checkout.clear()
    router.push(`/checkout/confirmation/${orderId}`)
    return
  }

  // Unexpected: Stripe is actually configured; fall back to standard pay()
  mockProcessing.value = false
  closeMockModal()
  pay()
}

// ── Price calculations (mirror of CheckoutView) ───────────────────────────────
const subtotal = computed(() => cart.subtotal)
const shippingCost = computed(() => checkout.shippingCostNok)
const expressFee = computed(() => checkout.expressFeeNok)
const total = computed(() => subtotal.value + shippingCost.value + expressFee.value)
const vatAmount = computed(() => total.value * 0.2)

function formatNok(n: number): string {
  return new Intl.NumberFormat('nb-NO', { maximumFractionDigits: 0 }).format(n) + ' kr'
}

// ── Submit payment ────────────────────────────────────────────────────────────
async function pay() {
  if (processing.value) return
  paymentError.value = null
  apiError.value = null

  // 1. Create order draft
  processing.value = true
  let orderId: number
  let clientSecret: string

  try {
    const resp = await createOrderDraft({
      deliveryType: checkout.deliveryType,
      shippingAddress: checkout.deliveryType !== 'Pickup' ? {
        line1: checkout.address.line1,
        postalCode: checkout.address.postalCode,
        city: checkout.address.city,
        country: 'NO',
      } : undefined,
      items: cart.items
        .filter((item) => item.bannerSizeId != null)
        .map((item) => ({
          bannerSizeId: item.bannerSizeId!,
          customWidthCm: item.customWidthCm ?? undefined,
          quantity: item.quantity,
          notes: item.notes ?? undefined,
          eyeletOption: item.eyeletOption,
        })),
    })
    orderId = resp.orderId
    clientSecret = resp.clientSecret
  } catch (err: unknown) {
    const e = err as { response?: { data?: { error?: string } }; message?: string }
    apiError.value =
      e.response?.data?.error || e.message || 'Kunne ikke opprette ordre. Prøv igjen.'
    processing.value = false
    return
  }

  // 2. Dev/mock bypass: if the backend returned a mock client secret, skip Stripe
  if (clientSecret.startsWith('pi_mock_')) {
    cart.clear()
    checkout.clear()
    router.push(`/checkout/confirmation/${orderId}`)
    return
  }

  // 3. Confirm card payment with Stripe
  if (!stripeRef.value || !cardElement.value) {
    paymentError.value = 'Stripe er ikke initialisert. Last siden på nytt.'
    processing.value = false
    return
  }

  const { error } = await stripeRef.value.confirmCardPayment(clientSecret, {
    payment_method: {
      card: cardElement.value,
      billing_details: {
        name: checkout.recipientName,
        address: {
          city: checkout.address.city,
          postal_code: checkout.address.postalCode,
          country: 'NO',
        },
      },
    },
  })

  if (error) {
    paymentError.value = error.message ?? 'Betalingen feilet. Prøv igjen.'
    processing.value = false
    return
  }

  // 4. Success — clear cart and go to confirmation
  cart.clear()
  checkout.clear()
  router.push(`/checkout/confirmation/${orderId}`)
}
</script>

<template>
  <div class="checkout-wrap">
    <!-- Header / stepper -->
    <header class="checkout-header">
      <h1 class="display checkout-title">Kasse</h1>
      <nav class="stepper">
        <RouterLink to="/checkout" class="stepper-step stepper-link">1. Oversikt &amp; levering</RouterLink>
        <span class="stepper-sep">›</span>
        <span class="stepper-step active">2. Betaling</span>
        <span class="stepper-sep">›</span>
        <span class="stepper-step">3. Bekreftelse</span>
      </nav>
    </header>

    <div class="checkout-grid">
      <!-- ── Left col: payment form ─────────────────────────────────────── -->
      <div class="checkout-main">

        <!-- Delivery address review -->
        <section class="panel">
          <div class="addr-header">
            <h2 class="section-title" style="margin-bottom:0">Leveringsadresse</h2>
            <RouterLink to="/checkout" class="addr-edit">
              <i class="fa-solid fa-pen-to-square"></i> Endre
            </RouterLink>
          </div>
          <address class="addr-block">
            <div class="addr-name">{{ checkout.recipientName }}</div>
            <template v-if="checkout.deliveryType !== 'Pickup'">
              <div>{{ checkout.address.line1 }}</div>
              <div>{{ checkout.address.postalCode }} {{ checkout.address.city }}</div>
            </template>
            <template v-else>
              <div style="color: var(--muted); font-size: 0.875rem; margin-top: 4px;">
                Henting: Rigedalen 43, 4626 Kristiansand
              </div>
              <div style="color: var(--faint); font-size: 0.8125rem;">
                Mandag–fredag kl. 09–15, eller etter avtale
              </div>
            </template>
          </address>
          <div class="addr-delivery">
            <i :class="checkout.deliveryType === 'Pickup' ? 'fa-solid fa-store' : 'fa-solid fa-truck'"></i>
            Levering:
            <span class="addr-delivery__type">
              {{
                checkout.deliveryType === 'Express' ? 'Ekspress (3 dager)' :
                checkout.deliveryType === 'Pickup' ? 'Henting (gratis)' :
                'Standard (2 uker)'
              }}
            </span>
          </div>
        </section>

        <!-- Card payment -->
        <section class="panel">
          <h2 class="section-title">
            Kortbetaling
            <span class="stripe-badge">
              <i class="fa-solid fa-lock"></i>
              Sikret av Stripe
            </span>
          </h2>

          <!-- Stripe not configured warning -->
          <div v-if="stripeError" class="alert-warn">
            <i class="fa-solid fa-triangle-exclamation"></i>
            <div>
              <strong>Stripe ikke tilgjengelig:</strong> {{ stripeError }}
              <br/>
              <button type="button" class="mock-pay-link" @click="openMockModal">
                Marker ordre som betalt (testmodus)
              </button>
            </div>
          </div>

          <!-- Error from order-creation during mock pay -->
          <div v-if="stripeError && apiError" class="alert-error" style="margin-top:0.75rem">
            <i class="fa-solid fa-circle-exclamation"></i>
            {{ apiError }}
          </div>

          <template v-else>
            <!-- Card element mount point -->
            <div class="card-field-wrap">
              <label class="field-label">
                <i class="fa-solid fa-credit-card"></i>
                Kortdetaljer
              </label>
              <div
                ref="cardMountEl"
                class="stripe-mount"
              />
              <p v-if="paymentError" class="field-error">
                <i class="fa-solid fa-circle-exclamation"></i>
                {{ paymentError }}
              </p>
            </div>

            <!-- API / network error -->
            <div v-if="apiError" class="alert-error">
              <i class="fa-solid fa-circle-exclamation"></i>
              {{ apiError }}
            </div>

            <button
              type="button"
              :disabled="processing || !stripeReady"
              class="btn btn-primary btn-pay"
              @click="pay"
            >
              <i v-if="processing" class="fa-solid fa-circle-notch fa-spin"></i>
              <i v-else class="fa-solid fa-lock"></i>
              <span>{{ processing ? 'Behandler…' : `Betal ${formatNok(total)}` }}</span>
            </button>

            <p class="secure-note">
              <i class="fa-solid fa-shield-halved"></i>
              Betalingen er kryptert og håndteres av Stripe. Vi lagrer ikke kortinformasjon.
            </p>
          </template>
        </section>
      </div>

      <!-- ── Right col: order summary ──────────────────────────────────── -->
      <aside class="checkout-aside">
        <div class="panel summary-sticky">
          <h2 class="section-title">Ordresammendrag</h2>

          <ul class="item-list">
            <li
              v-for="(item, idx) in cart.items"
              :key="idx"
              class="item-row"
            >
              <div>
                <div class="item-name">{{ item.bannerSizeName }}</div>
                <div class="item-sub">{{ item.quantity }} stk</div>
              </div>
              <div class="item-price">{{ formatNok(item.unitPriceNok * item.quantity) }}</div>
            </li>
          </ul>

          <dl class="summary-list">
            <div class="summary-row">
              <dt class="summary-label">Varer</dt>
              <dd class="summary-value">{{ formatNok(subtotal) }}</dd>
            </div>
            <div class="summary-row">
              <dt class="summary-label">Frakt</dt>
              <dd class="summary-value">{{ formatNok(shippingCost) }}</dd>
            </div>
            <div v-if="expressFee > 0" class="summary-row">
              <dt class="summary-label">Ekspressgebyr</dt>
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
        </div>
      </aside>
    </div>
  </div>

  <!-- ── Mock-payment password modal ─────────────────────────────────────── -->
  <Teleport to="body">
    <div v-if="showMockModal" class="modal-backdrop" @click.self="closeMockModal">
      <div class="modal-box" role="dialog" aria-modal="true" aria-labelledby="mock-modal-title">
        <div class="modal-header">
          <h3 id="mock-modal-title" class="modal-title">
            <i class="fa-solid fa-key"></i>
            Testmodus – Marker som betalt
          </h3>
          <button type="button" class="modal-close" @click="closeMockModal" aria-label="Lukk">
            <i class="fa-solid fa-xmark"></i>
          </button>
        </div>

        <p class="modal-desc">
          Stripe er ikke konfigurert. Skriv inn testpassordet for å simulere en vellykket betaling og fullføre ordren.
        </p>

        <div class="modal-field">
          <label class="field-label" for="mock-pw-input">
            <i class="fa-solid fa-lock"></i>
            Passord
          </label>
          <input
            id="mock-pw-input"
            v-model="mockPassword"
            type="password"
            class="mock-pw-input"
            placeholder="Skriv inn passord…"
            autocomplete="off"
            @keyup.enter="confirmMockPayment"
          />
          <p v-if="mockPasswordError" class="field-error">
            <i class="fa-solid fa-circle-exclamation"></i>
            {{ mockPasswordError }}
          </p>
        </div>

        <div class="modal-actions">
          <button type="button" class="btn btn-ghost" @click="closeMockModal">
            Avbryt
          </button>
          <button
            type="button"
            class="btn btn-primary"
            :disabled="mockProcessing"
            @click="confirmMockPayment"
          >
            <i v-if="mockProcessing" class="fa-solid fa-circle-notch fa-spin"></i>
            <i v-else class="fa-solid fa-check"></i>
            {{ mockProcessing ? 'Behandler…' : 'Bekreft' }}
          </button>
        </div>
      </div>
    </div>
  </Teleport>
</template>

<style scoped>
/* ── Layout ─────────────────────────────────────────────────── */
.checkout-wrap {
  max-width: 1100px;
  margin: 0 auto;
  padding: 2rem 1.25rem 3rem;
}
.checkout-header { margin-bottom: 2rem; }
.checkout-title {
  font-size: clamp(1.5rem, 3vw, 2rem);
  color: var(--text);
  margin-bottom: 0.5rem;
}
.checkout-grid { display: grid; gap: 2rem; }
@media (min-width: 1024px) {
  .checkout-grid { grid-template-columns: 1fr minmax(0, 340px); }
}
.checkout-main { display: flex; flex-direction: column; gap: 1.25rem; }
.checkout-aside { display: flex; flex-direction: column; }

/* ── Stepper ────────────────────────────────────────────────── */
.stepper { display: flex; align-items: center; gap: 0.5rem; font-size: 0.875rem; }
.stepper-step { color: var(--faint); }
.stepper-step.active { color: var(--accent); font-weight: 600; }
.stepper-link { color: var(--muted); text-decoration: none; transition: color 0.15s; }
.stepper-link:hover { color: var(--accent); }
.stepper-sep { color: var(--line); }

/* ── Section title ──────────────────────────────────────────── */
.section-title {
  font-size: 1rem;
  font-weight: 700;
  color: var(--text);
  margin-bottom: 1rem;
  font-family: var(--font-display);
  display: flex;
  align-items: center;
  gap: 0.625rem;
  flex-wrap: wrap;
}

/* ── Address review ─────────────────────────────────────────── */
.addr-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 0.75rem;
}
.addr-edit {
  font-size: 0.8125rem;
  color: var(--accent);
  text-decoration: none;
  display: flex;
  align-items: center;
  gap: 5px;
  transition: color 0.15s;
}
.addr-edit:hover { color: var(--accent-2); }
.addr-block {
  font-style: normal;
  font-size: 0.9rem;
  color: var(--muted);
  line-height: 1.6;
}
.addr-name { font-weight: 600; color: var(--text); }
.addr-delivery {
  margin-top: 0.625rem;
  font-size: 0.875rem;
  color: var(--muted);
  display: flex;
  align-items: center;
  gap: 7px;
}
.addr-delivery i { color: var(--faint); }
.addr-delivery__type { font-weight: 600; color: var(--text); }

/* ── Stripe badge ───────────────────────────────────────────── */
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
}

/* ── Card field ─────────────────────────────────────────────── */
.card-field-wrap { margin-bottom: 1rem; }
.field-label {
  display: flex;
  align-items: center;
  gap: 6px;
  font-size: 0.8125rem;
  font-weight: 600;
  color: var(--muted);
  margin-bottom: 8px;
}
.field-label i { color: var(--faint); font-size: 0.75rem; }

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
  box-shadow: 0 0 0 3px rgba(255, 106, 61, 0.18);
}

.field-error {
  margin-top: 6px;
  font-size: 0.8125rem;
  color: #f4a57a;
  display: flex;
  align-items: center;
  gap: 6px;
}

/* ── Alerts ─────────────────────────────────────────────────── */
.alert-error {
  display: flex;
  align-items: center;
  gap: 9px;
  margin-bottom: 0.75rem;
  padding: 10px 14px;
  background: rgba(220, 60, 60, 0.12);
  border: 1px solid rgba(220, 60, 60, 0.3);
  border-radius: 10px;
  color: #f4a57a;
  font-size: 0.875rem;
}
.alert-error i { color: #e05252; flex-shrink: 0; }

.alert-warn {
  display: flex;
  align-items: flex-start;
  gap: 9px;
  padding: 12px 14px;
  background: rgba(231, 185, 78, 0.1);
  border: 1px solid rgba(231, 185, 78, 0.3);
  border-radius: 10px;
  color: #e7d08a;
  font-size: 0.875rem;
}
.alert-warn i { color: var(--gold); flex-shrink: 0; margin-top: 2px; }

/* ── Pay button ─────────────────────────────────────────────── */
.btn-pay {
  width: 100%;
  justify-content: center;
  padding: 13px;
  font-size: 1rem;
  border-radius: 12px;
  margin-top: 4px;
}
.btn-pay:disabled {
  background: var(--surface-2) !important;
  color: var(--faint) !important;
  box-shadow: none !important;
  cursor: not-allowed;
}

.secure-note {
  margin-top: 0.75rem;
  font-size: 0.78rem;
  color: var(--faint);
  text-align: center;
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 6px;
}

/* ── Item list ──────────────────────────────────────────────── */
.item-list {
  list-style: none;
  padding: 0;
  margin: 0 0 1rem;
  display: flex;
  flex-direction: column;
}
.item-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 0.625rem 0;
  border-bottom: 1px solid var(--line-soft);
  font-size: 0.875rem;
}
.item-row:last-child { border-bottom: none; }
.item-name { font-weight: 600; color: var(--text); }
.item-sub { font-size: 0.8rem; color: var(--muted); }
.item-price { font-weight: 700; color: var(--text); }

/* ── Summary sidebar ────────────────────────────────────────── */
.summary-sticky { position: sticky; top: 1rem; }
.summary-list {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
  font-size: 0.875rem;
}
.summary-row { display: flex; justify-content: space-between; align-items: baseline; }
.summary-label { color: var(--muted); }
.summary-value { font-weight: 600; color: var(--text); }
.summary-faint { color: var(--faint); font-size: 0.8125rem; }
.summary-divider {
  border-top: 1px solid var(--line-soft);
  padding-top: 0.75rem;
  margin-top: 0.25rem;
  display: flex;
  flex-direction: column;
  gap: 0.375rem;
}
.summary-row--total { font-weight: 700; font-size: 1rem; color: var(--text); }
.summary-total-price { color: var(--accent); }

/* ── Mock pay link ──────────────────────────────────────────── */
.mock-pay-link {
  all: unset;
  cursor: pointer;
  display: inline-block;
  margin-top: 0.5rem;
  font-size: 0.8125rem;
  font-weight: 600;
  color: var(--accent);
  text-decoration: underline;
  text-underline-offset: 2px;
  transition: color 0.15s;
}
.mock-pay-link:hover { color: var(--accent-2); }

/* ── Modal backdrop + box ───────────────────────────────────── */
.modal-backdrop {
  position: fixed;
  inset: 0;
  z-index: 1000;
  background: rgba(0, 0, 0, 0.65);
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 1rem;
  backdrop-filter: blur(2px);
}

.modal-box {
  background: var(--surface);
  border: 1px solid var(--line);
  border-radius: 16px;
  padding: 1.5rem;
  width: 100%;
  max-width: 420px;
  box-shadow: 0 24px 64px rgba(0, 0, 0, 0.5);
}

.modal-header {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 0.75rem;
  margin-bottom: 0.75rem;
}

.modal-title {
  font-size: 1rem;
  font-weight: 700;
  color: var(--text);
  font-family: var(--font-display);
  display: flex;
  align-items: center;
  gap: 0.5rem;
  margin: 0;
}
.modal-title i { color: var(--accent); }

.modal-close {
  all: unset;
  cursor: pointer;
  color: var(--faint);
  font-size: 1.125rem;
  padding: 2px 4px;
  border-radius: 6px;
  transition: color 0.15s;
  flex-shrink: 0;
}
.modal-close:hover { color: var(--text); }

.modal-desc {
  font-size: 0.875rem;
  color: var(--muted);
  line-height: 1.6;
  margin: 0 0 1.25rem;
}

.modal-field { margin-bottom: 1rem; }

.mock-pw-input {
  width: 100%;
  box-sizing: border-box;
  background: var(--surface-2);
  border: 1px solid var(--line);
  border-radius: 10px;
  padding: 10px 14px;
  font-size: 0.9375rem;
  color: var(--text);
  font-family: inherit;
  outline: none;
  transition: border-color 0.15s, box-shadow 0.15s;
}
.mock-pw-input::placeholder { color: var(--faint); }
.mock-pw-input:focus {
  border-color: var(--accent);
  box-shadow: 0 0 0 3px rgba(255, 106, 61, 0.18);
}

.modal-actions {
  display: flex;
  justify-content: flex-end;
  gap: 0.625rem;
  margin-top: 1.25rem;
}
</style>

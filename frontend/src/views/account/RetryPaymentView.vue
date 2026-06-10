<script setup lang="ts">
import { ref, onMounted, onBeforeUnmount } from 'vue'
import { useRoute, useRouter, RouterLink } from 'vue-router'
import { loadStripe } from '@stripe/stripe-js'
import type { Stripe, StripeCardElement } from '@stripe/stripe-js'
import { retryOrderPayment } from '@/api/orders'
import { formatNok } from '@/utils/format'

// ─────────────────────────────────────────────────────────────────────────────
// BANNERSH-185: dedicated retry-payment view for orders the customer never
// paid for. Loaded via /account/orders/:id/pay — calls
// POST /api/orders/{id}/retry-payment which returns a fresh client secret
// reusing the existing PaymentIntent when possible. Mirrors the Stripe
// element setup from `checkout/PaymentView.vue` but skips all the cart /
// address / pricing recalc since the order already exists with its totals.
// ─────────────────────────────────────────────────────────────────────────────

const route = useRoute()
const router = useRouter()
const orderId = Number(route.params.id)

const loading = ref(true)
const loadError = ref<string | null>(null)
const totalNok = ref(0)
const clientSecret = ref<string | null>(null)

// Stripe state
const stripeRef = ref<Stripe | null>(null)
const cardElement = ref<StripeCardElement | null>(null)
const cardMountEl = ref<HTMLDivElement | null>(null)
const stripeReady = ref(false)
const stripeError = ref<string | null>(null)

// Payment state
const processing = ref(false)
const paymentError = ref<string | null>(null)

onMounted(async () => {
  // 1. Ask the backend for the (possibly fresh) client secret for this order.
  try {
    const resp = await retryOrderPayment(orderId)
    totalNok.value = resp.totalNok
    if (resp.alreadyPaid) {
      router.replace(`/checkout/confirmation/${orderId}`)
      return
    }
    if (!resp.clientSecret) {
      loadError.value = 'Kunne ikke starte ny betaling for denne ordren.'
      loading.value = false
      return
    }
    clientSecret.value = resp.clientSecret
  } catch (err: unknown) {
    const e = err as { response?: { status?: number; data?: { error?: string } }; message?: string }
    if (e.response?.status === 404) {
      loadError.value = 'Ordren finnes ikke (eller er slettet).'
    } else {
      loadError.value = e.response?.data?.error || e.message || 'Kunne ikke laste ordren.'
    }
    loading.value = false
    return
  }

  // 2. Init Stripe (or surface a mock-pay fallback if no key configured).
  await initStripe()
  loading.value = false
})

onBeforeUnmount(() => {
  cardElement.value?.unmount()
})

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
    /* fall through */
  }
  return null
}

async function initStripe() {
  // Mock client secrets (pi_mock_*) skip Stripe.js entirely — the "Betal"
  // button below handles the redirect directly.
  if (clientSecret.value && clientSecret.value.startsWith('pi_mock_')) {
    stripeReady.value = true
    return
  }

  const key = await resolveStripePublishableKey()
  if (!key) {
    stripeError.value =
      'Stripe er ikke konfigurert. Kontakt support hvis du ikke får betalt.'
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

    if (cardMountEl.value) {
      card.mount(cardMountEl.value)
      stripeReady.value = true
    }

    card.on('change', (event) => {
      paymentError.value = event.error?.message ?? null
    })
  } catch (err) {
    stripeError.value = 'Stripe kunne ikke initialiseres.'
    console.error('Stripe init error:', err)
  }
}

async function pay() {
  if (processing.value || !clientSecret.value) return
  paymentError.value = null
  processing.value = true

  // Mock secrets just hop to confirmation — the backend already linked the
  // order to a `pi_mock_*` PI which the test-mode flow can mark paid.
  if (clientSecret.value.startsWith('pi_mock_')) {
    router.push(`/checkout/confirmation/${orderId}`)
    return
  }

  if (!stripeRef.value || !cardElement.value) {
    paymentError.value = 'Stripe er ikke initialisert. Last siden på nytt.'
    processing.value = false
    return
  }

  const { error } = await stripeRef.value.confirmCardPayment(clientSecret.value, {
    payment_method: { card: cardElement.value },
  })

  if (error) {
    paymentError.value = error.message ?? 'Betalingen feilet. Prøv igjen.'
    processing.value = false
    return
  }

  router.push(`/checkout/confirmation/${orderId}`)
}

</script>

<template>
  <div class="retry-wrap">
    <!-- Breadcrumb -->
    <div class="breadcrumb">
      <RouterLink :to="`/account/orders/${orderId}`" class="breadcrumb-link">
        <i class="fa-solid fa-arrow-left"></i>
        Ordre #{{ orderId }}
      </RouterLink>
      <span class="breadcrumb-sep">›</span>
      <span class="breadcrumb-current">Betal</span>
    </div>

    <h1 class="display title">
      <i class="fa-solid fa-credit-card"></i>
      Fullfør betaling
    </h1>

    <!-- Loading -->
    <div v-if="loading" class="loading-state">
      <i class="fa-solid fa-circle-notch fa-spin loading-spinner"></i>
    </div>

    <!-- Load error -->
    <div v-else-if="loadError" class="alert-error">
      <i class="fa-solid fa-circle-exclamation"></i>
      <div>
        {{ loadError }}
        <RouterLink to="/account/orders" class="retry-link">
          Tilbake til ordrelisten
        </RouterLink>
      </div>
    </div>

    <template v-else>
      <section class="panel">
        <div class="amount-row">
          <span class="amount-label">Beløp å betale</span>
          <span class="amount-value">{{ formatNok(totalNok) }}</span>
        </div>

        <!-- Stripe not configured warning -->
        <div v-if="stripeError" class="alert-warn">
          <i class="fa-solid fa-triangle-exclamation"></i>
          <div>
            <strong>Stripe ikke tilgjengelig:</strong> {{ stripeError }}
          </div>
        </div>

        <template v-else>
          <div class="card-field-wrap">
            <label class="field-label">
              <i class="fa-solid fa-credit-card"></i>
              Kortdetaljer
            </label>
            <div ref="cardMountEl" class="stripe-mount" />
            <p v-if="paymentError" class="field-error">
              <i class="fa-solid fa-circle-exclamation"></i>
              {{ paymentError }}
            </p>
          </div>

          <button
            type="button"
            :disabled="processing || !stripeReady"
            class="btn btn-primary btn-pay"
            @click="pay"
          >
            <i v-if="processing" class="fa-solid fa-circle-notch fa-spin"></i>
            <i v-else class="fa-solid fa-lock"></i>
            <span>{{ processing ? 'Behandler…' : `Betal ${formatNok(totalNok)}` }}</span>
          </button>

          <p class="secure-note">
            <i class="fa-solid fa-shield-halved"></i>
            Betalingen er kryptert og håndteres av Stripe.
          </p>
        </template>
      </section>
    </template>
  </div>
</template>

<style scoped>
.retry-wrap {
  max-width: 540px;
  margin: 0 auto;
  padding: 2.5rem 1.25rem 3rem;
  display: flex;
  flex-direction: column;
  gap: 1.25rem;
}

.breadcrumb { display: flex; align-items: center; gap: 0.5rem; font-size: 0.875rem; }
.breadcrumb-link {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  color: var(--accent);
  text-decoration: none;
  font-weight: 600;
}
.breadcrumb-link:hover { color: var(--accent-2); }
.breadcrumb-sep { color: var(--line); }
.breadcrumb-current { color: var(--muted); }

.title {
  font-size: clamp(1.25rem, 2.5vw, 1.5rem);
  color: var(--text);
  display: flex;
  align-items: center;
  gap: 0.625rem;
}
.title i { color: var(--accent); }

.loading-state { display: flex; justify-content: center; padding: 4rem 0; }
.loading-spinner { font-size: 2rem; color: var(--accent); }

.alert-error,
.alert-warn {
  display: flex;
  align-items: flex-start;
  gap: 10px;
  padding: 14px 18px;
  border-radius: 14px;
  font-size: 0.9rem;
}
.alert-error {
  background: rgba(220,60,60,.12);
  border: 1px solid rgba(220,60,60,.3);
  color: #f4a57a;
}
.alert-error i { color: #e05252; flex-shrink: 0; margin-top: 2px; }
.alert-warn {
  background: rgba(231,185,78,.10);
  border: 1px solid rgba(231,185,78,.30);
  color: #e7d08a;
}
.alert-warn i { color: var(--gold); flex-shrink: 0; margin-top: 2px; }

.retry-link {
  display: block;
  margin-top: 6px;
  color: var(--accent);
  font-weight: 600;
  text-decoration: underline;
}

.amount-row {
  display: flex;
  justify-content: space-between;
  align-items: baseline;
  margin-bottom: 1.25rem;
  padding-bottom: 1rem;
  border-bottom: 1px solid var(--line-soft);
}
.amount-label { color: var(--muted); font-size: 0.875rem; }
.amount-value { font-size: 1.5rem; font-weight: 700; color: var(--accent); font-family: var(--font-display); }

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
</style>

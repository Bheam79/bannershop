<script setup lang="ts">
import { ref, computed, onMounted, onBeforeUnmount } from 'vue'
import { useRouter } from 'vue-router'
import { loadStripe } from '@stripe/stripe-js'
import type { Stripe, StripeCardElement } from '@stripe/stripe-js'
import { useCartStore } from '@/stores/cart'
import { useCheckoutStore } from '@/stores/checkout'
import { createOrderDraft } from '@/api/orders'

const router = useRouter()
const cart = useCartStore()
const checkout = useCheckoutStore()

// ── Guard: redirect if no address set or cart is empty ───────────────────────
onMounted(async () => {
  if (cart.items.length === 0 || !checkout.isReady()) {
    router.replace('/checkout')
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
      shippingAddress: {
        line1: checkout.address.line1,
        postalCode: checkout.address.postalCode,
        city: checkout.address.city,
        country: 'NO',
      },
      items: cart.items
        .filter((item) => item.bannerSizeId != null)
        .map((item) => ({
          bannerSizeId: item.bannerSizeId!,
          customWidthCm: item.customWidthCm ?? undefined,
          quantity: item.quantity,
          notes: item.notes ?? undefined,
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
  <div class="max-w-6xl mx-auto px-4 py-8 sm:py-12">
    <!-- Header / stepper -->
    <header class="mb-8">
      <h1 class="text-2xl sm:text-3xl font-bold text-gray-900 mb-2">Kasse</h1>
      <nav class="flex items-center gap-2 text-sm">
        <RouterLink to="/checkout" class="text-blue-700 hover:underline">1. Oversikt &amp; levering</RouterLink>
        <span class="text-gray-400">›</span>
        <span class="font-semibold text-blue-700">2. Betaling</span>
        <span class="text-gray-400">›</span>
        <span class="text-gray-400">3. Bekreftelse</span>
      </nav>
    </header>

    <div class="grid lg:grid-cols-3 gap-8">
      <!-- ── Left col: payment form ─────────────────────────────────────── -->
      <div class="lg:col-span-2 space-y-6">
        <!-- Delivery address review -->
        <section class="bg-white border border-gray-200 rounded-xl p-6">
          <div class="flex items-center justify-between mb-3">
            <h2 class="text-base font-semibold text-gray-900">Leveringsadresse</h2>
            <RouterLink to="/checkout" class="text-sm text-blue-600 hover:underline">Endre</RouterLink>
          </div>
          <address class="not-italic text-sm text-gray-700 space-y-0.5">
            <div class="font-medium">{{ checkout.recipientName }}</div>
            <div>{{ checkout.address.line1 }}</div>
            <div>{{ checkout.address.postalCode }} {{ checkout.address.city }}</div>
          </address>
          <div class="mt-2 text-sm text-gray-600">
            Levering:
            <span class="font-medium">
              {{ checkout.deliveryType === 'Express' ? 'Ekspress (3 dager)' : 'Standard (2 uker)' }}
            </span>
          </div>
        </section>

        <!-- Card payment -->
        <section class="bg-white border border-gray-200 rounded-xl p-6">
          <h2 class="text-lg font-semibold text-gray-900 mb-4">
            Kortbetaling
            <span class="ml-2 text-xs font-normal text-gray-500 bg-gray-100 px-2 py-0.5 rounded">
              Sikret av Stripe
            </span>
          </h2>

          <!-- Stripe not configured warning -->
          <div v-if="stripeError" class="rounded-lg bg-amber-50 border border-amber-200 p-4 text-sm text-amber-800">
            <strong>Stripe ikke tilgjengelig:</strong> {{ stripeError }}
          </div>

          <template v-else>
            <!-- Card element mount point -->
            <div class="mb-4">
              <label class="block text-sm font-medium text-gray-700 mb-2">Kortdetaljer</label>
              <div
                ref="cardMountEl"
                class="border border-gray-300 rounded-lg px-3 py-3 min-h-[44px] focus-within:ring-2 focus-within:ring-blue-500 focus-within:border-blue-500 transition"
              />
              <p v-if="paymentError" class="mt-2 text-sm text-red-600">{{ paymentError }}</p>
            </div>

            <!-- API / network error -->
            <p v-if="apiError" class="mb-3 text-sm text-red-600 bg-red-50 border border-red-200 rounded-lg px-3 py-2">
              {{ apiError }}
            </p>

            <button
              type="button"
              :disabled="processing || !stripeReady"
              class="w-full bg-blue-700 hover:bg-blue-800 disabled:bg-gray-300 disabled:cursor-not-allowed text-white font-semibold py-3 rounded-lg transition flex items-center justify-center gap-2"
              @click="pay"
            >
              <span v-if="processing" class="inline-block w-4 h-4 border-2 border-white border-t-transparent rounded-full animate-spin" />
              <span>{{ processing ? 'Behandler…' : `Betal ${formatNok(total)}` }}</span>
            </button>

            <p class="mt-3 text-xs text-gray-500 text-center">
              🔒 Betalingen er kryptert og håndteres av Stripe. Vi lagrer ikke kortinformasjon.
            </p>
          </template>
        </section>
      </div>

      <!-- ── Right col: order summary ──────────────────────────────────── -->
      <aside>
        <div class="bg-white border border-gray-200 rounded-xl p-6 sticky top-4">
          <h2 class="text-lg font-semibold text-gray-900 mb-4">Ordresammendrag</h2>

          <ul class="divide-y divide-gray-100 mb-4 text-sm">
            <li
              v-for="(item, idx) in cart.items"
              :key="idx"
              class="py-2"
            >
              <div class="flex justify-between items-start">
                <div>
                  <div class="font-medium text-gray-900">{{ item.bannerSizeName }}</div>
                  <div class="text-gray-500">{{ item.quantity }} stk</div>
                </div>
                <div class="font-medium text-gray-900 ml-3 shrink-0">
                  {{ formatNok(item.unitPriceNok * item.quantity) }}
                </div>
              </div>
            </li>
          </ul>

          <dl class="space-y-2 text-sm">
            <div class="flex justify-between">
              <dt class="text-gray-600">Varer</dt>
              <dd class="font-medium text-gray-900">{{ formatNok(subtotal) }}</dd>
            </div>
            <div class="flex justify-between">
              <dt class="text-gray-600">Frakt</dt>
              <dd class="font-medium text-gray-900">{{ formatNok(shippingCost) }}</dd>
            </div>
            <div v-if="expressFee > 0" class="flex justify-between">
              <dt class="text-gray-600">Ekspressgebyr</dt>
              <dd class="font-medium text-gray-900">{{ formatNok(expressFee) }}</dd>
            </div>
            <div class="border-t border-gray-200 pt-3 mt-2">
              <div class="flex justify-between font-bold text-base">
                <dt class="text-gray-900">Totalt inkl. MVA</dt>
                <dd class="text-blue-700">{{ formatNok(total) }}</dd>
              </div>
              <div class="flex justify-between text-xs text-gray-500 mt-1">
                <dt>Herav MVA (25%)</dt>
                <dd>{{ formatNok(vatAmount) }}</dd>
              </div>
            </div>
          </dl>
        </div>
      </aside>
    </div>
  </div>
</template>

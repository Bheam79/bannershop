<script setup lang="ts">
/**
 * PaywallModal — AI credit-pack purchase + alternative-options modal.
 *
 * Used by:
 *   - AiBannerBuilderView (AI mode)
 *   - AccountDesignRequestDetailView (regenerate 402 path)
 *
 * The component handles Stripe card-payment internally.
 * After a successful purchase it emits `credits-updated` so the parent can
 * refresh its local credit state, then emits `retry-action` so the pending
 * generate / regenerate call is retried automatically.
 */
import { ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { loadStripe } from '@stripe/stripe-js'
import type { Stripe, StripeCardElement } from '@stripe/stripe-js'
import { buyCreditPack } from '@/api/aiCredits'
import { getAiCreditsBalance } from '@/api/aiCredits'
import { useAuthStore } from '@/stores/auth'
import { formatNok } from '@/utils/format'
import type { PaywallOptions, DesignRequestListItem } from '@/api/designRequests'

// ── Props / emits ─────────────────────────────────────────────────────────────
const props = defineProps<{
  /** Controls visibility — bind with v-model */
  modelValue: boolean
  paywallOptions: PaywallOptions
  pastDesigns: DesignRequestListItem[]
  /** 'generate' | 'regenerate' — what to retry after credits are bought */
  pendingAction?: 'generate' | 'regenerate'
  /** Needed for the "Betal for banneret nå" option CTA */
  designRequestId?: number | null
}>()

const emit = defineEmits<{
  'update:modelValue': [value: boolean]
  /** After a credit-pack purchase succeeds, parent should retry the pending action */
  'retry-action': []
  /** After a credit-pack purchase succeeds, let the parent know the new balance */
  'credits-updated': [creditsRemaining: number, hasUsedFreeGeneration: boolean]
  'navigate-to': [url: string]
  'select-past-design': [item: DesignRequestListItem]
  'go-to-checkout': []
}>()

const router = useRouter()
const auth = useAuthStore()

// ── Internal state ────────────────────────────────────────────────────────────
type CreditPackPhase = 'menu' | 'loading' | 'card' | 'processing' | 'done' | 'error'
const creditPackPhase = ref<CreditPackPhase>('menu')
const creditPackError = ref<string | null>(null)
const packDetails = ref<{ clientSecret: string; creditCount: number; priceNok: number } | null>(null)
const stripeCardError = ref<string | null>(null)

// Stripe — lazy init
const stripeRef = ref<Stripe | null>(null)
const cardElement = ref<StripeCardElement | null>(null)
const cardMountEl = ref<HTMLDivElement | null>(null)

// ── Open / close ──────────────────────────────────────────────────────────────
function open() {
  creditPackPhase.value = 'menu'
  creditPackError.value = null
  stripeCardError.value = null
  packDetails.value = null
}

function close() {
  if (creditPackPhase.value === 'processing') return
  cardElement.value?.destroy()
  cardElement.value = null
  stripeRef.value = null
  emit('update:modelValue', false)
}

// Reset internal state whenever the modal is opened
watch(
  () => props.modelValue,
  (opened) => { if (opened) open() },
)

// ── Navigation helpers ────────────────────────────────────────────────────────
function navigateFromPaywall(url: string) {
  emit('update:modelValue', false)
  emit('navigate-to', url)
}

function goToCheckoutWithDesign() {
  emit('update:modelValue', false)
  emit('go-to-checkout')
}

// ── Stripe key resolution ─────────────────────────────────────────────────────
async function resolveStripePublishableKey(): Promise<string | null> {
  const envKey = import.meta.env.VITE_STRIPE_PUBLISHABLE_KEY as string | undefined
  if (envKey && !envKey.startsWith('pk_test_REPLACE') && !envKey.startsWith('REPLACE_')) return envKey
  try {
    const resp = await fetch('/api/config/stripe')
    if (resp.ok) {
      const data: { publishableKey?: string } = await resp.json()
      if (data.publishableKey?.length) return data.publishableKey
    }
  } catch { /* network error */ }
  return null
}

async function initStripe(): Promise<boolean> {
  if (stripeRef.value) return true
  const key = await resolveStripePublishableKey()
  if (!key) {
    creditPackError.value = 'Stripe er ikke konfigurert. Sett stripe_publishable_key i adminpanelet.'
    creditPackPhase.value = 'error'
    return false
  }
  try {
    const stripe = await loadStripe(key)
    if (!stripe) { creditPackError.value = 'Stripe kunne ikke lastes. Prøv igjen.'; creditPackPhase.value = 'error'; return false }
    stripeRef.value = stripe
    return true
  } catch {
    creditPackError.value = 'Stripe kunne ikke initialiseres.'
    creditPackPhase.value = 'error'
    return false
  }
}

// ── Credit-pack purchase ──────────────────────────────────────────────────────
async function startCreditPackPurchase(pack: 'small' | 'large' = 'small') {
  if (!auth.isLoggedIn) {
    void router.push(`/login?redirect=${encodeURIComponent('/banner-builder/ai')}`)
    return
  }
  creditPackPhase.value = 'loading'
  creditPackError.value = null
  try {
    packDetails.value = await buyCreditPack(pack)
    const ok = await initStripe()
    if (!ok) return
    creditPackPhase.value = 'card'
  } catch (e: unknown) {
    const ex = e as { response?: { data?: { error?: string } }; message?: string }
    creditPackError.value = ex.response?.data?.error ?? ex.message ?? 'Feil ved oppstart av betaling.'
    creditPackPhase.value = 'error'
  }
}

// Mount Stripe card element when its DOM ref becomes available
watch(cardMountEl, (el) => {
  if (!el || !stripeRef.value) return
  if (cardElement.value) { cardElement.value.mount(el); return }
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
  card.on('change', (event) => { stripeCardError.value = event.error?.message ?? null })
  card.mount(el)
  cardElement.value = card
})

async function confirmCreditPackPayment() {
  if (!stripeRef.value || !cardElement.value || !packDetails.value) return
  creditPackPhase.value = 'processing'
  stripeCardError.value = null

  const { error } = await stripeRef.value.confirmCardPayment(packDetails.value.clientSecret, {
    payment_method: { card: cardElement.value },
  })

  if (error) {
    stripeCardError.value = error.message ?? 'Betalingen feilet. Prøv igjen.'
    creditPackPhase.value = 'card'
    return
  }

  creditPackPhase.value = 'done'
  if (auth.isLoggedIn) {
    try {
      const balance = await getAiCreditsBalance()
      emit('credits-updated', balance.creditsRemaining, balance.hasUsedFreeGeneration)
    } catch { /* non-critical */ }
  }

  setTimeout(() => {
    emit('update:modelValue', false)
    cardElement.value?.destroy()
    cardElement.value = null
    stripeRef.value = null
    emit('retry-action')
  }, 1400)
}
</script>

<template>
  <Teleport to="body">
    <div v-if="props.modelValue" class="modal-backdrop" @click.self="close">
      <div class="modal-box" role="dialog" aria-modal="true" aria-label="Generer flere AI-banner">

        <!-- Close button -->
        <button
          v-if="creditPackPhase !== 'processing'"
          type="button"
          class="modal-close-btn"
          aria-label="Lukk"
          @click="close"
        >
          <i class="fa-solid fa-xmark"></i>
        </button>

        <!-- ── Menu phase ─────────────────────────────────────────────────── -->
        <div v-if="creditPackPhase === 'menu'">
          <div style="text-align:center;margin-bottom:24px">
            <div style="width:52px;height:52px;border-radius:50%;background:rgba(255,106,61,.15);border:1px solid rgba(255,106,61,.3);display:grid;place-items:center;margin:0 auto 14px;font-size:22px;color:var(--accent)">
              <i class="fa-solid fa-wand-magic-sparkles"></i>
            </div>
            <h2 class="display" style="font-size:22px;color:var(--text);margin-bottom:8px">Generer flere AI-banner</h2>
            <p style="font-size:14px;color:var(--muted);max-width:28em;margin:0 auto">
              Du har brukt opp den gratis genereringen. Velg et alternativ for å fortsette.
            </p>
          </div>

          <div style="display:grid;gap:12px">
            <!-- Option: Buy small credit pack -->
            <button type="button" class="paywall-option paywall-option-primary" @click="startCreditPackPurchase('small')">
              <div style="display:flex;align-items:flex-start;gap:14px">
                <span class="paywall-option-ico" style="background:rgba(255,106,61,.15);color:var(--accent)">
                  <i class="fa-solid fa-bag-shopping"></i>
                </span>
                <div style="text-align:left">
                  <div style="font-weight:700;font-size:15px;color:var(--text)">
                    Liten pakke — {{ props.paywallOptions.creditPackSmallCount }} AI forslag
                    <span style="color:var(--accent)">({{ formatNok(props.paywallOptions.creditPackSmallPriceNok) }})</span>
                  </div>
                  <div style="font-size:13px;color:var(--muted);margin-top:3px">Kortbetaling via Stripe — umiddelbar tilgang</div>
                </div>
              </div>
              <i class="fa-solid fa-chevron-right" style="color:var(--faint);font-size:12px;flex-shrink:0"></i>
            </button>

            <!-- Option: Buy large credit pack -->
            <button type="button" class="paywall-option paywall-option-primary" @click="startCreditPackPurchase('large')">
              <div style="display:flex;align-items:flex-start;gap:14px">
                <span class="paywall-option-ico" style="background:rgba(255,106,61,.2);color:var(--accent)">
                  <i class="fa-solid fa-bags-shopping"></i>
                </span>
                <div style="text-align:left">
                  <div style="font-weight:700;font-size:15px;color:var(--text)">
                    Stor pakke — {{ props.paywallOptions.creditPackLargeCount }} AI forslag
                    <span style="color:var(--accent)">({{ formatNok(props.paywallOptions.creditPackLargePriceNok) }})</span>
                  </div>
                  <div style="font-size:13px;color:var(--muted);margin-top:3px">Beste verdi — spar over 50% per forslag</div>
                </div>
              </div>
              <i class="fa-solid fa-chevron-right" style="color:var(--faint);font-size:12px;flex-shrink:0"></i>
            </button>

            <!-- Option: Order banner now -->
            <button type="button" class="paywall-option" @click="goToCheckoutWithDesign">
              <div style="display:flex;align-items:flex-start;gap:14px">
                <span class="paywall-option-ico" style="background:rgba(74,222,128,.1);color:#4ade80">
                  <i class="fa-solid fa-cart-shopping"></i>
                </span>
                <div style="text-align:left">
                  <div style="font-weight:700;font-size:15px;color:var(--text)">
                    Betal for banneret nå
                    <span style="color:#4ade80">({{ props.paywallOptions.bannerOrderCreditBonus }} ytterligere forslag inkludert)</span>
                  </div>
                  <div style="font-size:13px;color:var(--muted);margin-top:3px">Du kan fortsatt lage flere design før du sender inn bestillingen</div>
                </div>
              </div>
              <i class="fa-solid fa-chevron-right" style="color:var(--faint);font-size:12px;flex-shrink:0"></i>
            </button>

            <!-- Option: Manual designer -->
            <button type="button" class="paywall-option" @click="navigateFromPaywall(props.paywallOptions.manualDesignerUrl)">
              <div style="display:flex;align-items:flex-start;gap:14px">
                <span class="paywall-option-ico" style="background:rgba(231,185,78,.1);color:var(--gold)">
                  <i class="fa-solid fa-paintbrush"></i>
                </span>
                <div style="text-align:left">
                  <div style="font-weight:700;font-size:15px;color:var(--text)">Få vår designer til å lage design for deg</div>
                  <div style="font-size:13px;color:var(--muted);margin-top:3px">Menneskelig designer — 495 kr</div>
                </div>
              </div>
              <i class="fa-solid fa-chevron-right" style="color:var(--faint);font-size:12px;flex-shrink:0"></i>
            </button>

            <!-- Option: Upload own design -->
            <button type="button" class="paywall-option" @click="navigateFromPaywall(props.paywallOptions.uploadOwnUrl)">
              <div style="display:flex;align-items:flex-start;gap:14px">
                <span class="paywall-option-ico" style="background:rgba(255,106,61,.08);color:var(--muted)">
                  <i class="fa-solid fa-file-arrow-up"></i>
                </span>
                <div style="text-align:left">
                  <div style="font-weight:700;font-size:15px;color:var(--text)">Last opp ditt eget design</div>
                  <div style="font-size:13px;color:var(--muted);margin-top:3px">Du betaler bare for banneren</div>
                </div>
              </div>
              <i class="fa-solid fa-chevron-right" style="color:var(--faint);font-size:12px;flex-shrink:0"></i>
            </button>
          </div>

          <!-- Past banners gallery inside paywall (BANNERSH-83) -->
          <div v-if="props.pastDesigns.length > 0" style="margin-top:22px;border-top:1px solid var(--line-soft);padding-top:18px">
            <div style="font-size:13px;font-weight:700;color:var(--muted);text-transform:uppercase;letter-spacing:.04em;margin-bottom:10px;display:flex;align-items:center;gap:8px">
              <i class="fa-solid fa-clock-rotate-left" style="color:var(--accent)"></i>
              Eller bruk et tidligere banner
            </div>
            <div style="display:flex;gap:10px;overflow-x:auto;padding-bottom:6px">
              <button
                v-for="d in props.pastDesigns.slice(0, 6)"
                :key="d.id"
                type="button"
                class="past-mini"
                :title="`${d.personName} — ${d.themeDescription}`"
                @click="() => { emit('update:modelValue', false); emit('select-past-design', d) }"
              >
                <img v-if="d.previewUrl" :src="d.previewUrl" :alt="`Banner for ${d.personName}`" />
                <span class="past-mini-name">{{ d.personName || 'Uten navn' }}</span>
              </button>
            </div>
          </div>
        </div>

        <!-- ── Loading phase ──────────────────────────────────────────────── -->
        <div v-else-if="creditPackPhase === 'loading'" style="text-align:center;padding:2rem 0">
          <i class="fa-solid fa-circle-notch fa-spin" style="font-size:32px;color:var(--accent);margin-bottom:14px;display:block"></i>
          <p style="color:var(--muted)">Forbereder betaling…</p>
        </div>

        <!-- ── Card phase (Stripe) ────────────────────────────────────────── -->
        <div v-else-if="creditPackPhase === 'card' && packDetails">
          <button
            type="button"
            style="display:flex;align-items:center;gap:8px;font-size:13px;color:var(--muted);background:none;border:none;cursor:pointer;padding:0 0 18px;font-family:var(--font-ui)"
            @click="creditPackPhase = 'menu'"
          >
            <i class="fa-solid fa-arrow-left" style="font-size:11px"></i> Tilbake
          </button>
          <h3 class="display" style="font-size:18px;color:var(--text);margin-bottom:6px">
            Kjøp {{ packDetails.creditCount }} AI banner forslag
          </h3>
          <p style="font-size:14px;color:var(--muted);margin-bottom:20px">
            Pris: <strong style="color:var(--text)">{{ formatNok(packDetails.priceNok) }}</strong> — belastes med en gang
          </p>
          <label class="field-label" style="margin-bottom:8px">Kortdetaljer</label>
          <div ref="cardMountEl" class="stripe-mount" />
          <p v-if="stripeCardError" class="error-box" style="margin-top:10px">
            <i class="fa-solid fa-circle-exclamation"></i> {{ stripeCardError }}
          </p>
          <button
            type="button"
            class="btn btn-primary"
            style="width:100%;justify-content:center;padding:14px;font-size:15px;border-radius:12px;margin-top:18px"
            @click="confirmCreditPackPayment"
          >
            <i class="fa-solid fa-lock" style="font-size:12px"></i>
            Betal {{ formatNok(packDetails.priceNok) }}
          </button>
          <p style="font-size:13px;color:var(--faint);text-align:center;margin-top:10px">
            <i class="fa-solid fa-shield-halved"></i> Sikret av Stripe. Vi lagrer ikke kortinformasjon.
          </p>
        </div>

        <!-- ── Processing phase ───────────────────────────────────────────── -->
        <div v-else-if="creditPackPhase === 'processing'" style="text-align:center;padding:2rem 0">
          <i class="fa-solid fa-circle-notch fa-spin" style="font-size:32px;color:var(--accent);margin-bottom:14px;display:block"></i>
          <p style="color:var(--muted)">Behandler betaling…</p>
        </div>

        <!-- ── Done phase ─────────────────────────────────────────────────── -->
        <div v-else-if="creditPackPhase === 'done'" style="text-align:center;padding:2rem 0">
          <i class="fa-solid fa-circle-check" style="font-size:48px;color:#4ade80;margin-bottom:14px;display:block"></i>
          <h3 class="display" style="font-size:20px;color:var(--text);margin-bottom:8px">Betaling godkjent!</h3>
          <p style="color:var(--muted)">Kreditene er lagt til. Genererer nytt banner…</p>
        </div>

        <!-- ── Error phase ────────────────────────────────────────────────── -->
        <div v-else-if="creditPackPhase === 'error'" style="text-align:center;padding:1rem 0">
          <div class="error-box" style="justify-content:center;flex-direction:column;gap:12px;padding:20px">
            <i class="fa-solid fa-circle-exclamation" style="font-size:28px"></i>
            <p>{{ creditPackError }}</p>
            <button type="button" class="btn btn-ghost" @click="creditPackPhase = 'menu'">Prøv igjen</button>
          </div>
        </div>

      </div>
    </div>
  </Teleport>
</template>

<style scoped>
.modal-backdrop {
  position: fixed;
  inset: 0;
  background: rgba(10, 8, 6, 0.78);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 1000;
  padding: 1rem;
  backdrop-filter: blur(4px);
}
.modal-box {
  background: var(--surface);
  border: 1px solid var(--line-soft);
  border-radius: 18px;
  padding: 28px 28px 24px;
  width: 100%;
  max-width: 480px;
  position: relative;
  max-height: calc(100vh - 2rem);
  overflow-y: auto;
}
.modal-close-btn {
  position: absolute;
  top: 16px;
  right: 16px;
  width: 32px;
  height: 32px;
  border-radius: 50%;
  border: 1px solid var(--line);
  background: var(--surface-2);
  cursor: pointer;
  display: grid;
  place-items: center;
  color: var(--muted);
  font-size: 14px;
  transition: background 0.15s, color 0.15s;
}
.modal-close-btn:hover { background: var(--line); color: var(--text); }

.paywall-option {
  width: 100%;
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  background: var(--surface-2);
  border: 1px solid var(--line-soft);
  border-radius: 14px;
  padding: 14px 16px;
  cursor: pointer;
  font-family: var(--font-ui);
  text-align: left;
  transition: border-color 0.15s, background 0.15s, transform 0.1s;
}
.paywall-option:hover {
  border-color: var(--line);
  background: var(--surface);
  transform: translateY(-1px);
}
.paywall-option-primary {
  border-color: rgba(255,106,61,.35);
  background: rgba(255,106,61,.06);
}
.paywall-option-primary:hover { border-color: var(--accent); background: rgba(255,106,61,.1); }
.paywall-option-ico {
  width: 40px;
  height: 40px;
  border-radius: 10px;
  display: grid;
  place-items: center;
  font-size: 17px;
  flex-shrink: 0;
}

.past-mini {
  flex: 0 0 110px;
  display: flex;
  flex-direction: column;
  gap: 5px;
  background: var(--surface-2);
  border: 1.5px solid var(--line-soft);
  border-radius: 0;
  overflow: hidden;
  cursor: pointer;
  padding: 0;
  font-family: var(--font-ui);
  transition: border-color 0.15s, transform 0.1s;
}
.past-mini:hover { border-color: var(--accent); transform: translateY(-1px); }
.past-mini img { width: 100%; aspect-ratio: 16 / 9; object-fit: cover; display: block; }
.past-mini-name {
  font-size: 13px;
  font-weight: 700;
  color: var(--text);
  padding: 0 8px 7px;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  display: block;
}

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

.field-label {
  display: block;
  font-size: 13px;
  font-weight: 700;
  color: var(--muted);
  margin-bottom: 8px;
  text-transform: uppercase;
  letter-spacing: .04em;
}

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
</style>

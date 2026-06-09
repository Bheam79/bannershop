<script setup lang="ts">
import { ref, reactive, computed, onMounted, onBeforeUnmount, watch } from 'vue'
import { useRouter } from 'vue-router'
import { loadStripe } from '@stripe/stripe-js'
import type { Stripe, StripeCardElement } from '@stripe/stripe-js'
import { useAuthStore } from '@/stores/auth'
import { useAiCreditsStore } from '@/stores/aiCredits'
import apiClient from '@/api/client'
import type { User } from '@/types'
import { listOrders } from '@/api/orders'
import type { OrderListItem } from '@/api/orders'
import { listDesignRequests } from '@/api/designRequests'
import type { DesignRequestListItem } from '@/api/designRequests'
import {
  getAiCreditsBalance,
  getCreditPackInfo,
  buyCreditPack,
  type AiCreditsBalance,
  type CreditPackInfo,
} from '@/api/aiCredits'

const auth = useAuthStore()
const creditsStore = useAiCreditsStore()
const router = useRouter()

// ── Unified active items summary ──────────────────────────────────────────────
interface UnifiedActiveItem {
  id: number
  kind: 'order' | 'design'
  typeLabel: string
  typeBadgeClass: string
  statusLabel: string
  statusClass: string
  date: string
  priceNok: number
  detailPath: string
  isAwaitingApproval: boolean
}

const recentOrders = ref<OrderListItem[]>([])
const designRequests = ref<DesignRequestListItem[]>([])
const ordersLoading = ref(true)

const ACTIVE_ORDER_STATUSES = new Set([
  'PendingPayment', 'Paid', 'InProduction', 'ReadyToShip', 'Shipped',
])
const ACTIVE_DR_STATUSES = new Set([
  'Pending', 'InProgress', 'AwaitingApproval', 'Approved', 'RevisionRequested', 'Revised',
])

const DR_STATUS_LABELS: Record<string, string> = {
  Pending:           'Venter',
  InProgress:        'Under arbeid',
  AwaitingApproval:  'Til godkjenning',
  Approved:          'Design klar',
  RevisionRequested: 'Revisjon',
  Revised:           'Revidert',
  Final:             'Levert',
  Failed:            'Feilet',
  Cancelled:         'Kansellert',
}
const DR_STATUS_CLASSES: Record<string, string> = {
  Pending:           'badge-pending',
  InProgress:        'badge-inprogress',
  AwaitingApproval:  'badge-awaiting',
  Approved:          'badge-approved',
  RevisionRequested: 'badge-revision',
  Revised:           'badge-revised',
  Final:             'badge-approved',
  Failed:            'badge-cancelled',
  Cancelled:         'badge-cancelled',
}
function drStatusLabel(s: string) { return DR_STATUS_LABELS[s] ?? s }
function drStatusClass(s: string) { return DR_STATUS_CLASSES[s] ?? 'badge-draft' }

const hasAnyItems = computed(
  () => recentOrders.value.length > 0 || designRequests.value.length > 0,
)

const activeItems = computed<UnifiedActiveItem[]>(() => {
  const orderItems: UnifiedActiveItem[] = recentOrders.value
    .filter(o => ACTIVE_ORDER_STATUSES.has(o.status))
    .map(o => ({
      id: o.id,
      kind: 'order' as const,
      typeLabel: 'Eget',
      typeBadgeClass: 'badge-type-order',
      statusLabel: statusLabel(o.status),
      statusClass: statusClass(o.status),
      date: o.createdAt,
      priceNok: o.totalNok,
      detailPath: `/account/orders/${o.id}`,
      isAwaitingApproval: false,
    }))
  const drItems: UnifiedActiveItem[] = designRequests.value
    .filter(dr => ACTIVE_DR_STATUSES.has(dr.status))
    .map(dr => ({
      id: dr.id,
      kind: 'design' as const,
      typeLabel: dr.mode === 'Ai' ? 'AI' : 'Designer',
      typeBadgeClass: dr.mode === 'Ai' ? 'badge-type-ai' : 'badge-type-designer',
      statusLabel: drStatusLabel(dr.status),
      statusClass: drStatusClass(dr.status),
      date: dr.createdAt,
      priceNok: dr.priceNok,
      detailPath: `/account/design-requests/${dr.id}`,
      isAwaitingApproval: dr.status === 'AwaitingApproval',
    }))
  return [...orderItems, ...drItems]
    .sort((a, b) => new Date(b.date).getTime() - new Date(a.date).getTime())
    .slice(0, 5)
})

onMounted(async () => {
  try {
    const [ordersResult, drList] = await Promise.all([
      listOrders(1, 20),
      listDesignRequests(),
    ])
    recentOrders.value = ordersResult.items
    designRequests.value = drList
  } catch {
    // non-critical — ignore
  } finally {
    ordersLoading.value = false
  }
  await loadAiCredits()
})

onBeforeUnmount(() => {
  cardElement.value?.destroy()
})

const STATUS_LABELS: Record<string, string> = {
  Draft: 'Utkast',
  PendingPayment: 'Venter betaling',
  Paid: 'Betalt',
  InProduction: 'Under produksjon',
  ReadyToShip: 'Klar til frakt',
  Shipped: 'Sendt',
  Delivered: 'Levert',
  Cancelled: 'Kansellert',
}
const STATUS_CLASSES: Record<string, string> = {
  Draft:          'badge-draft',
  PendingPayment: 'badge-pending',
  Paid:           'badge-paid',
  InProduction:   'badge-paid',
  ReadyToShip:    'badge-ready',
  Shipped:        'badge-shipped',
  Delivered:      'badge-shipped',
  Cancelled:      'badge-cancelled',
}
function statusLabel(s: string) { return STATUS_LABELS[s] ?? s }
function statusClass(s: string) { return STATUS_CLASSES[s] ?? 'badge-draft' }
function formatNok(n: number): string {
  return new Intl.NumberFormat('nb-NO', { maximumFractionDigits: 0 }).format(n) + ' kr'
}
function formatDate(iso: string): string {
  return new Date(iso).toLocaleDateString('nb-NO', { day: '2-digit', month: 'short', year: 'numeric' })
}

// ── AI credits widget (BANNERSH-71) ───────────────────────────────────────────
const creditsBalance = ref<AiCreditsBalance | null>(null)
const creditsLoading = ref(true)
const creditsError = ref<string | null>(null)
const packInfo = ref<CreditPackInfo | null>(null)

async function loadAiCredits() {
  if (!auth.isLoggedIn) {
    creditsLoading.value = false
    return
  }
  creditsLoading.value = true
  creditsError.value = null
  try {
    // Balance + pack-info in parallel — both are tiny GETs and independent.
    const [balance, info] = await Promise.all([
      getAiCreditsBalance(),
      getCreditPackInfo().catch(() => null), // pack info shouldn't fail the widget
    ])
    creditsBalance.value = balance
    // Mirror into the shared store so the header credit badge stays accurate
    // (BANNERSH-87).
    creditsStore.setBalance(balance.creditsRemaining, balance.hasUsedFreeGeneration)
    packInfo.value = info
  } catch (err: unknown) {
    const ex = err as { response?: { data?: { error?: string } }; message?: string }
    creditsError.value =
      ex.response?.data?.error ?? ex.message ?? 'Kunne ikke laste AI-kreditter.'
  } finally {
    creditsLoading.value = false
  }
}

// Credit-pack purchase modal (inline Stripe Elements, mirrors AiBannerBuilderView).
type CreditPackPhase = 'menu' | 'loading' | 'card' | 'processing' | 'done' | 'error'
const buyModalOpen = ref(false)
const creditPackPhase = ref<CreditPackPhase>('menu')
const creditPackError = ref<string | null>(null)
const packDetails = ref<{ clientSecret: string; creditCount: number; priceNok: number } | null>(null)
const stripeCardError = ref<string | null>(null)

const stripeRef = ref<Stripe | null>(null)
const cardElement = ref<StripeCardElement | null>(null)
const cardMountEl = ref<HTMLDivElement | null>(null)

function openBuyModal() {
  creditPackPhase.value = 'menu'
  creditPackError.value = null
  stripeCardError.value = null
  packDetails.value = null
  buyModalOpen.value = true
}

function closeBuyModal() {
  if (creditPackPhase.value === 'processing') return // never close mid-payment
  buyModalOpen.value = false
  creditPackPhase.value = 'menu'
  cardElement.value?.destroy()
  cardElement.value = null
  stripeRef.value = null
}

async function initStripe(): Promise<boolean> {
  if (stripeRef.value) return true
  const key = import.meta.env.VITE_STRIPE_PUBLISHABLE_KEY as string | undefined
  if (!key || key.startsWith('pk_test_REPLACE')) {
    creditPackError.value =
      'Stripe er ikke konfigurert i dette miljøet. Kortbetaling er ikke tilgjengelig.'
    creditPackPhase.value = 'error'
    return false
  }
  try {
    const stripe = await loadStripe(key)
    if (!stripe) {
      creditPackError.value = 'Stripe kunne ikke lastes. Prøv igjen.'
      creditPackPhase.value = 'error'
      return false
    }
    stripeRef.value = stripe
    return true
  } catch {
    creditPackError.value = 'Stripe kunne ikke initialiseres.'
    creditPackPhase.value = 'error'
    return false
  }
}

async function startCreditPackPurchase() {
  creditPackPhase.value = 'loading'
  creditPackError.value = null

  try {
    packDetails.value = await buyCreditPack()
    const ok = await initStripe()
    if (!ok) return
    creditPackPhase.value = 'card'
    // Card element mounts via the watch on cardMountEl below.
  } catch (e: unknown) {
    const ex = e as { response?: { data?: { error?: string } }; message?: string }
    creditPackError.value =
      ex.response?.data?.error ?? ex.message ?? 'Feil ved oppstart av betaling.'
    creditPackPhase.value = 'error'
  }
}

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

  // Payment succeeded — credits are granted by the Stripe webhook asynchronously.
  // Optimistically refresh balance and close modal.
  creditPackPhase.value = 'done'
  try {
    const updated = await getAiCreditsBalance()
    creditsBalance.value = updated
    creditsStore.setBalance(updated.creditsRemaining, updated.hasUsedFreeGeneration)
  } catch {
    // Non-critical — balance will refresh on next navigation.
  }
  setTimeout(() => {
    buyModalOpen.value = false
    cardElement.value?.destroy()
    cardElement.value = null
    stripeRef.value = null
    creditPackPhase.value = 'menu'
  }, 1400)
}

function goToBannerBuilder() {
  router.push('/banner-builder/ai')
}

// ── Profile form ──────────────────────────────────────────────────────────────
const profile = reactive({
  name: auth.user?.name ?? '',
  phone: auth.user?.phone ?? '',
})
const profileError = ref('')
const profileSuccess = ref('')
const profileLoading = ref(false)

async function saveProfile() {
  profileError.value = ''
  profileSuccess.value = ''
  profileLoading.value = true
  try {
    const { data } = await apiClient.put<User>('/auth/me', {
      name: profile.name,
      phone: profile.phone || null,
    })
    auth.setAuth({
      accessToken: auth.accessToken!,
      refreshToken: auth.refreshTokenValue!,
      user: data,
    })
    profileSuccess.value = 'Profilen er oppdatert.'
  } catch (err: any) {
    profileError.value = err.response?.data?.error ?? 'Kunne ikke oppdatere profilen.'
  } finally {
    profileLoading.value = false
  }
}

// ── Change password form ──────────────────────────────────────────────────────
const pwForm = reactive({
  currentPassword: '',
  newPassword: '',
  confirmPassword: '',
})
const pwError = ref('')
const pwSuccess = ref('')
const pwLoading = ref(false)

async function changePassword() {
  pwError.value = ''
  pwSuccess.value = ''
  if (pwForm.newPassword !== pwForm.confirmPassword) {
    pwError.value = 'De nye passordene stemmer ikke overens.'
    return
  }
  if (pwForm.newPassword.length < 8) {
    pwError.value = 'Nytt passord må være minst 8 tegn.'
    return
  }
  pwLoading.value = true
  try {
    await apiClient.post('/auth/change-password', {
      currentPassword: pwForm.currentPassword,
      newPassword: pwForm.newPassword,
    })
    pwSuccess.value = 'Passordet er endret.'
    pwForm.currentPassword = ''
    pwForm.newPassword = ''
    pwForm.confirmPassword = ''
  } catch (err: any) {
    pwError.value = err.response?.data?.error ?? 'Kunne ikke endre passordet.'
  } finally {
    pwLoading.value = false
  }
}
</script>

<template>
  <div class="account-wrap">

    <!-- Greeting -->
    <div class="account-header">
      <h1 class="display account-title">
        <i class="fa-solid fa-user greeting-icon"></i>
        Hei{{ auth.user?.name ? `, ${auth.user.name.split(' ')[0]}` : '' }}!
      </h1>
      <p class="account-email">{{ auth.user?.email }}</p>
    </div>

    <!-- Unified orders summary (Mine ordrer) -->
    <div class="panel">
      <div class="section-header">
        <h2 class="section-title">
          <i class="fa-solid fa-box"></i>
          Mine ordrer
        </h2>
        <RouterLink to="/account/orders" class="link-more">
          Se alle
          <i class="fa-solid fa-arrow-right"></i>
        </RouterLink>
      </div>

      <div v-if="ordersLoading" class="spinner-wrap">
        <i class="fa-solid fa-circle-notch fa-spin spinner-icon"></i>
      </div>

      <div v-else-if="!hasAnyItems" class="empty-msg">
        Ingen ordrer ennå.
        <RouterLink to="/" class="link-inline">Handle nå</RouterLink>
      </div>

      <div v-else-if="activeItems.length === 0" class="empty-msg">
        Ingen aktive ordrer for øyeblikket.
        <RouterLink to="/account/orders" class="link-inline">Se alle ordrer</RouterLink>
      </div>

      <ul v-else class="order-mini-list">
        <li
          v-for="item in activeItems"
          :key="`${item.kind}-${item.id}`"
          class="order-mini-row"
          @click="router.push(item.detailPath)"
        >
          <div class="order-mini-left">
            <div class="order-mini-id-row">
              <span class="type-chip" :class="item.typeBadgeClass">{{ item.typeLabel }}</span>
              <span class="order-mini-id">#{{ item.id }}</span>
            </div>
            <div class="order-mini-date">{{ formatDate(item.date) }}</div>
          </div>
          <div class="order-mini-right">
            <span class="badge" :class="item.statusClass">
              {{ item.statusLabel }}
            </span>
            <RouterLink
              v-if="item.isAwaitingApproval"
              :to="item.detailPath"
              class="approve-action"
              @click.stop
            >
              Godkjenn
              <i class="fa-solid fa-arrow-right fa-xs"></i>
            </RouterLink>
            <span class="order-mini-total">{{ formatNok(item.priceNok) }}</span>
          </div>
        </li>
      </ul>

      <div v-if="activeItems.length > 0" class="more-link-row">
        <RouterLink to="/account/orders" class="link-faint">
          Se alle ordrer
          <i class="fa-solid fa-arrow-right fa-xs"></i>
        </RouterLink>
      </div>
    </div>

    <!-- AI credits widget (BANNERSH-71) -->
    <div class="panel ai-credits-panel">
      <div class="ai-credits-header">
        <h2 class="section-title">
          <i class="fa-solid fa-wand-magic-sparkles"></i>
          AI banner forslag
        </h2>
        <RouterLink to="/banner-builder/ai" class="link-more">
          Lag nytt banner
          <i class="fa-solid fa-arrow-right"></i>
        </RouterLink>
      </div>

      <div v-if="creditsLoading" class="spinner-wrap">
        <i class="fa-solid fa-circle-notch fa-spin spinner-icon"></i>
      </div>

      <div v-else-if="creditsError" class="alert-error">
        <i class="fa-solid fa-circle-exclamation"></i>
        {{ creditsError }}
      </div>

      <div v-else-if="creditsBalance" class="ai-credits-body">
        <div class="ai-credits-balance">
          <span class="ai-credits-label">AI forslag tilgjengelig</span>
          <span class="ai-credits-count">{{ creditsBalance.creditsRemaining }}</span>
        </div>

        <p
          v-if="!creditsBalance.hasUsedFreeGeneration"
          class="ai-credits-free-hint"
        >
          <i class="fa-solid fa-gift"></i>
          Du har <strong>1 gratis AI-forslag</strong> igjen — prøv det!
        </p>

        <div class="ai-credits-actions">
          <button
            v-if="!creditsBalance.hasUsedFreeGeneration"
            type="button"
            class="btn btn-primary"
            @click="goToBannerBuilder"
          >
            <i class="fa-solid fa-wand-magic-sparkles"></i>
            Bruk gratis AI-forslag
          </button>
          <button
            type="button"
            class="btn"
            :class="creditsBalance.hasUsedFreeGeneration ? 'btn-primary' : 'btn-soft'"
            @click="openBuyModal"
          >
            <i class="fa-solid fa-bag-shopping"></i>
            <template v-if="packInfo">
              Kjøp {{ packInfo.creditCount }} AI forslag ({{ formatNok(packInfo.priceNok) }})
            </template>
            <template v-else>
              Kjøp AI forslag
            </template>
          </button>
        </div>
      </div>
    </div>

    <!-- Profile section -->
    <div class="panel">
      <h2 class="section-title">
        <i class="fa-solid fa-user"></i>
        Profilinformasjon
      </h2>
      <form @submit.prevent="saveProfile" class="form-grid">
        <div class="form-field">
          <label class="field-label">Navn</label>
          <input
            v-model="profile.name"
            type="text"
            required
            class="field-input"
          />
        </div>
        <div class="form-field">
          <label class="field-label">Telefon</label>
          <input
            v-model="profile.phone"
            type="tel"
            placeholder="+47 900 00 000"
            class="field-input"
          />
        </div>

        <div v-if="profileError" class="alert-error full-col">
          <i class="fa-solid fa-circle-exclamation"></i>
          {{ profileError }}
        </div>
        <div v-if="profileSuccess" class="alert-success full-col">
          <i class="fa-solid fa-circle-check"></i>
          {{ profileSuccess }}
        </div>

        <div class="full-col">
          <button
            type="submit"
            :disabled="profileLoading"
            class="btn btn-primary"
          >
            <i v-if="profileLoading" class="fa-solid fa-circle-notch fa-spin"></i>
            {{ profileLoading ? 'Lagrer…' : 'Lagre endringer' }}
          </button>
        </div>
      </form>
    </div>

    <!-- Change password section -->
    <div class="panel">
      <h2 class="section-title">
        <i class="fa-solid fa-lock"></i>
        Endre passord
      </h2>
      <form @submit.prevent="changePassword" class="form-grid">
        <div class="form-field full-col">
          <label class="field-label">Nåværende passord</label>
          <input
            v-model="pwForm.currentPassword"
            type="password"
            required
            autocomplete="current-password"
            class="field-input"
          />
        </div>
        <div class="form-field">
          <label class="field-label">Nytt passord</label>
          <input
            v-model="pwForm.newPassword"
            type="password"
            required
            autocomplete="new-password"
            placeholder="Minst 8 tegn"
            class="field-input"
          />
        </div>
        <div class="form-field">
          <label class="field-label">Bekreft nytt passord</label>
          <input
            v-model="pwForm.confirmPassword"
            type="password"
            required
            autocomplete="new-password"
            class="field-input"
          />
        </div>

        <div v-if="pwError" class="alert-error full-col">
          <i class="fa-solid fa-circle-exclamation"></i>
          {{ pwError }}
        </div>
        <div v-if="pwSuccess" class="alert-success full-col">
          <i class="fa-solid fa-circle-check"></i>
          {{ pwSuccess }}
        </div>

        <div class="full-col">
          <button
            type="submit"
            :disabled="pwLoading"
            class="btn btn-soft"
          >
            <i v-if="pwLoading" class="fa-solid fa-circle-notch fa-spin"></i>
            {{ pwLoading ? 'Endrer passord…' : 'Endre passord' }}
          </button>
        </div>
      </form>
    </div>

    <!-- ═════════════════════════════════════════════════════════════════════
         Buy credits modal — inline Stripe Elements card form (BANNERSH-71)
    ════════════════════════════════════════════════════════════════════════ -->
    <Teleport to="body">
      <div v-if="buyModalOpen" class="modal-backdrop" @click.self="closeBuyModal">
        <div class="modal-box" role="dialog" aria-modal="true" aria-label="Kjøp AI banner forslag">
          <button
            v-if="creditPackPhase !== 'processing'"
            type="button"
            class="modal-close-btn"
            aria-label="Lukk"
            @click="closeBuyModal"
          >
            <i class="fa-solid fa-xmark"></i>
          </button>

          <!-- Menu phase -->
          <div v-if="creditPackPhase === 'menu'">
            <div class="modal-head">
              <div class="modal-ico">
                <i class="fa-solid fa-wand-magic-sparkles"></i>
              </div>
              <h2 class="display modal-title">Kjøp AI banner forslag</h2>
              <p class="modal-sub">
                <template v-if="packInfo">
                  <strong>{{ packInfo.creditCount }} AI forslag</strong> for
                  <strong>{{ formatNok(packInfo.priceNok) }}</strong> — kreditene legges til
                  kontoen din umiddelbart etter betaling.
                </template>
                <template v-else>
                  Kortbetaling via Stripe — kreditene legges til kontoen din umiddelbart.
                </template>
              </p>
            </div>
            <button
              type="button"
              class="btn btn-primary modal-cta"
              @click="startCreditPackPurchase"
            >
              <i class="fa-solid fa-credit-card"></i>
              Fortsett til betaling
            </button>
          </div>

          <!-- Loading phase -->
          <div v-else-if="creditPackPhase === 'loading'" class="modal-status">
            <i class="fa-solid fa-circle-notch fa-spin modal-status-ico"></i>
            <p>Forbereder betaling…</p>
          </div>

          <!-- Card phase -->
          <div v-else-if="creditPackPhase === 'card' && packDetails">
            <button
              type="button"
              class="modal-back-btn"
              @click="creditPackPhase = 'menu'"
            >
              <i class="fa-solid fa-arrow-left"></i> Tilbake
            </button>

            <h3 class="display modal-title" style="margin-top: 0.5rem">
              Kjøp {{ packDetails.creditCount }} AI banner forslag
            </h3>
            <p class="modal-sub">
              Pris: <strong>{{ formatNok(packDetails.priceNok) }}</strong> — belastes med en gang.
            </p>

            <label class="field-label modal-card-label">Kortdetaljer</label>
            <div ref="cardMountEl" class="stripe-mount" />
            <p v-if="stripeCardError" class="alert-error modal-card-error">
              <i class="fa-solid fa-circle-exclamation"></i> {{ stripeCardError }}
            </p>

            <button
              type="button"
              class="btn btn-primary modal-cta"
              @click="confirmCreditPackPayment"
            >
              <i class="fa-solid fa-lock"></i>
              Betal {{ formatNok(packDetails.priceNok) }}
            </button>
            <p class="modal-foot">
              <i class="fa-solid fa-shield-halved"></i>
              Sikret av Stripe. Vi lagrer ikke kortinformasjon.
            </p>
          </div>

          <!-- Processing phase -->
          <div v-else-if="creditPackPhase === 'processing'" class="modal-status">
            <i class="fa-solid fa-circle-notch fa-spin modal-status-ico"></i>
            <p>Behandler betaling…</p>
          </div>

          <!-- Done phase -->
          <div v-else-if="creditPackPhase === 'done'" class="modal-status">
            <i class="fa-solid fa-circle-check modal-status-ico modal-status-ok"></i>
            <h3 class="display modal-title">Betaling godkjent!</h3>
            <p>Kreditene er lagt til kontoen din.</p>
          </div>

          <!-- Error phase -->
          <div v-else-if="creditPackPhase === 'error'" class="modal-status">
            <div class="alert-error modal-error-card">
              <i class="fa-solid fa-circle-exclamation"></i>
              <span>{{ creditPackError }}</span>
            </div>
            <button type="button" class="btn btn-soft modal-back-btn-bottom" @click="creditPackPhase = 'menu'">
              Prøv igjen
            </button>
          </div>
        </div>
      </div>
    </Teleport>

  </div>
</template>

<style scoped>
/* ── Layout ─────────────────────────────────────────────────── */
.account-wrap {
  max-width: 680px;
  margin: 0 auto;
  padding: 2.5rem 1.25rem 3.5rem;
  display: flex;
  flex-direction: column;
  gap: 1.5rem;
}

/* ── Greeting ───────────────────────────────────────────────── */
.account-header { margin-bottom: 0.25rem; }
.account-title {
  font-size: clamp(1.4rem, 3vw, 1.875rem);
  color: var(--text);
  display: flex;
  align-items: center;
  gap: 0.5rem;
}
.greeting-icon { color: var(--accent); font-size: 1.2rem; }
.account-email { color: var(--faint); font-size: 0.875rem; margin-top: 4px; }

/* ── Section header ─────────────────────────────────────────── */
.section-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 1rem;
}
.section-title {
  font-size: 1rem;
  font-weight: 700;
  color: var(--text);
  font-family: var(--font-display);
  display: flex;
  align-items: center;
  gap: 0.5rem;
  margin-bottom: 1rem;
}
.section-title i { color: var(--accent); font-size: 0.9rem; }
.section-header .section-title { margin-bottom: 0; }

.link-more {
  display: flex;
  align-items: center;
  gap: 6px;
  font-size: 0.8125rem;
  font-weight: 600;
  color: var(--accent);
  text-decoration: none;
  transition: color 0.15s;
  white-space: nowrap;
}
.link-more:hover { color: var(--accent-2); }

.link-inline {
  color: var(--accent);
  text-decoration: none;
  font-weight: 600;
}
.link-inline:hover { color: var(--accent-2); }

/* ── Spinner ────────────────────────────────────────────────── */
.spinner-wrap { display: flex; justify-content: center; padding: 1rem 0; }
.spinner-icon { font-size: 1.375rem; color: var(--accent); }

/* ── Empty message ──────────────────────────────────────────── */
.empty-msg {
  font-size: 0.875rem;
  color: var(--muted);
  padding: 0.5rem 0;
}

/* ── Order mini list ────────────────────────────────────────── */
.order-mini-list {
  list-style: none;
  padding: 0;
  margin: 0;
}
.order-mini-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 0.625rem 0.5rem;
  border-radius: 10px;
  cursor: pointer;
  transition: background 0.12s;
}
.order-mini-row:hover { background: var(--surface-2); }
.order-mini-left { display: flex; flex-direction: column; gap: 2px; }
.order-mini-id-row { display: flex; align-items: center; gap: 5px; }
.order-mini-id { font-size: 0.875rem; font-weight: 600; color: var(--accent); }
.order-mini-date { font-size: 0.75rem; color: var(--faint); margin-top: 1px; }
.order-mini-right { display: flex; align-items: center; gap: 0.625rem; flex-wrap: wrap; justify-content: flex-end; }
.order-mini-total { font-size: 0.875rem; font-weight: 700; color: var(--text); }

/* ── Type chip ──────────────────────────────────────────────── */
.type-chip {
  display: inline-block;
  font-size: 0.62rem;
  font-weight: 700;
  padding: 1px 5px;
  border-radius: 4px;
  letter-spacing: 0.02em;
  text-transform: uppercase;
  flex-shrink: 0;
}
.badge-type-order    { background: rgba(138,128,115,.18); color: var(--muted);  border: 1px solid rgba(138,128,115,.3); }
.badge-type-ai       { background: rgba(34,200,230,.12);  color: #7ddce8;       border: 1px solid rgba(34,200,230,.28); }
.badge-type-designer { background: rgba(130,100,220,.15); color: #c9a8f5;       border: 1px solid rgba(130,100,220,.3); }

/* ── Approve action ─────────────────────────────────────────── */
.approve-action {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  font-size: 0.72rem;
  font-weight: 700;
  color: #7de0a8;
  text-decoration: none;
  white-space: nowrap;
  transition: color 0.15s;
}
.approve-action:hover { color: #4ec984; }

/* ── Extended status badge classes (design requests) ────────── */
.badge-inprogress { background: rgba(255,106,61,.13);  color: var(--accent-2); border: 1px solid rgba(255,106,61,.3); }
.badge-awaiting   { background: rgba(160,110,220,.15); color: #c9a8f5;         border: 1px solid rgba(160,110,220,.3); }
.badge-approved   { background: rgba(60,180,100,.13);  color: #7de0a8;         border: 1px solid rgba(60,180,100,.28); }
.badge-revision   { background: rgba(255,140,60,.15);  color: #ffb07a;         border: 1px solid rgba(255,140,60,.3); }
.badge-revised    { background: rgba(34,200,230,.12);  color: #7ddce8;         border: 1px solid rgba(34,200,230,.28); }

.more-link-row {
  margin-top: 0.625rem;
  text-align: center;
}
.link-faint {
  font-size: 0.78rem;
  color: var(--faint);
  text-decoration: none;
  display: inline-flex;
  align-items: center;
  gap: 5px;
  transition: color 0.15s;
}
.link-faint:hover { color: var(--accent); }

/* ── Design link panel ──────────────────────────────────────── */
.design-link-inner {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 1rem;
}
.design-link-sub {
  font-size: 0.8125rem;
  color: var(--muted);
  margin-top: 3px;
}

/* ── Form ───────────────────────────────────────────────────── */
.form-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 1rem;
}
@media (max-width: 540px) { .form-grid { grid-template-columns: 1fr; } }

.form-field { display: flex; flex-direction: column; }
.full-col { grid-column: 1 / -1; }

.field-label {
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
  box-shadow: 0 0 0 3px rgba(255,106,61,.18);
}

/* ── Alerts ─────────────────────────────────────────────────── */
.alert-error {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 10px 14px;
  background: rgba(220,60,60,.12);
  border: 1px solid rgba(220,60,60,.3);
  border-radius: 10px;
  color: #f4a57a;
  font-size: 0.875rem;
}
.alert-error i { color: #e05252; flex-shrink: 0; }

.alert-success {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 10px 14px;
  background: rgba(60,180,100,.1);
  border: 1px solid rgba(60,180,100,.25);
  border-radius: 10px;
  color: #7de0a8;
  font-size: 0.875rem;
}
.alert-success i { color: #4ec984; flex-shrink: 0; }

/* ── Status badges ──────────────────────────────────────────── */
.badge {
  display: inline-block;
  font-size: 0.72rem;
  font-weight: 700;
  padding: 2px 8px;
  border-radius: 99px;
  letter-spacing: 0.01em;
}
.badge-draft    { background: rgba(138,128,115,.18); color: var(--muted); border: 1px solid rgba(138,128,115,.3); }
.badge-pending  { background: rgba(231,185,78,.15);  color: #e7d08a;      border: 1px solid rgba(231,185,78,.3); }
.badge-paid     { background: rgba(255,106,61,.13);  color: var(--accent-2); border: 1px solid rgba(255,106,61,.3); }
.badge-ready    { background: rgba(160,110,220,.15); color: #c9a8f5;      border: 1px solid rgba(160,110,220,.3); }
.badge-shipped  { background: rgba(60,180,100,.13);  color: #7de0a8;      border: 1px solid rgba(60,180,100,.28); }
.badge-cancelled{ background: rgba(220,60,60,.12);   color: #f4a57a;      border: 1px solid rgba(220,60,60,.28); }

/* ── AI credits widget (BANNERSH-71) ────────────────────────── */
.ai-credits-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 1rem;
}
.ai-credits-header .section-title { margin-bottom: 0; }
.ai-credits-body { display: flex; flex-direction: column; gap: 0.875rem; }
.ai-credits-balance {
  display: flex;
  align-items: baseline;
  justify-content: space-between;
  gap: 1rem;
  padding: 0.75rem 1rem;
  background: var(--surface-2);
  border: 1px solid var(--line-soft);
  border-radius: 12px;
}
.ai-credits-label {
  font-size: 0.875rem;
  color: var(--muted);
  font-weight: 600;
}
.ai-credits-count {
  font-size: 1.75rem;
  font-weight: 800;
  color: var(--accent);
  font-family: var(--font-display);
  line-height: 1;
}
.ai-credits-free-hint {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 10px 14px;
  background: rgba(231,185,78,.1);
  border: 1px solid rgba(231,185,78,.28);
  border-radius: 10px;
  color: var(--gold, #e7d08a);
  font-size: 0.875rem;
}
.ai-credits-free-hint i { color: var(--gold, #e7d08a); flex-shrink: 0; }
.ai-credits-actions {
  display: flex;
  flex-wrap: wrap;
  gap: 0.625rem;
}
.ai-credits-actions .btn { flex: 1; min-width: 180px; justify-content: center; }

/* ── Buy credits modal ──────────────────────────────────────── */
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
  max-width: 460px;
  position: relative;
  max-height: calc(100vh - 2rem);
  overflow-y: auto;
}
.modal-close-btn {
  position: absolute;
  top: 14px;
  right: 14px;
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
.modal-head { text-align: center; margin-bottom: 20px; }
.modal-ico {
  width: 52px;
  height: 52px;
  border-radius: 50%;
  background: rgba(255,106,61,.15);
  border: 1px solid rgba(255,106,61,.3);
  display: grid;
  place-items: center;
  margin: 0 auto 12px;
  font-size: 22px;
  color: var(--accent);
}
.modal-title {
  font-size: 1.25rem;
  color: var(--text);
  margin-bottom: 6px;
}
.modal-sub {
  font-size: 0.875rem;
  color: var(--muted);
  margin: 0 auto;
  max-width: 30em;
}
.modal-cta {
  width: 100%;
  justify-content: center;
  padding: 13px;
  font-size: 0.9375rem;
  border-radius: 12px;
  margin-top: 18px;
}
.modal-foot {
  font-size: 0.75rem;
  color: var(--faint);
  text-align: center;
  margin-top: 10px;
}
.modal-foot i { margin-right: 4px; }
.modal-status { text-align: center; padding: 1.5rem 0.5rem; }
.modal-status-ico { font-size: 32px; color: var(--accent); margin-bottom: 12px; display: block; }
.modal-status-ok { color: #4ade80; }
.modal-status p { color: var(--muted); }
.modal-back-btn {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  font-size: 0.8125rem;
  color: var(--muted);
  background: none;
  border: none;
  cursor: pointer;
  padding: 0 0 12px;
  font-family: var(--font-ui);
}
.modal-back-btn:hover { color: var(--accent); }
.modal-back-btn-bottom { margin-top: 12px; }
.modal-card-label { margin-bottom: 8px; }
.modal-card-error { margin-top: 10px; }
.modal-error-card {
  flex-direction: column;
  text-align: center;
  gap: 8px;
  padding: 18px;
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
</style>

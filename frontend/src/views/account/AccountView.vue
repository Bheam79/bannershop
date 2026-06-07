<script setup lang="ts">
import { ref, reactive, computed, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/auth'
import apiClient from '@/api/client'
import type { User } from '@/types'
import { listOrders } from '@/api/orders'
import type { OrderListItem } from '@/api/orders'

const auth = useAuthStore()
const router = useRouter()

// ── Active orders summary ─────────────────────────────────────────────────────
const recentOrders = ref<OrderListItem[]>([])
const ordersLoading = ref(true)

const ACTIVE_STATUSES = new Set([
  'PendingPayment', 'Paid', 'InProduction', 'ReadyToShip', 'Shipped',
])

const activeOrders = computed(() =>
  recentOrders.value.filter(o => ACTIVE_STATUSES.has(o.status))
)

onMounted(async () => {
  try {
    const result = await listOrders(1, 5)
    recentOrders.value = result.items
  } catch {
    // non-critical — ignore
  } finally {
    ordersLoading.value = false
  }
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

    <!-- Active orders summary -->
    <div class="panel">
      <div class="section-header">
        <h2 class="section-title">
          <i class="fa-solid fa-box"></i>
          Aktive ordrer
        </h2>
        <RouterLink to="/account/orders" class="link-more">
          Se alle ordrer
          <i class="fa-solid fa-arrow-right"></i>
        </RouterLink>
      </div>

      <div v-if="ordersLoading" class="spinner-wrap">
        <i class="fa-solid fa-circle-notch fa-spin spinner-icon"></i>
      </div>

      <div v-else-if="activeOrders.length === 0 && recentOrders.length === 0" class="empty-msg">
        Ingen ordrer ennå.
        <RouterLink to="/" class="link-inline">Handle nå</RouterLink>
      </div>

      <div v-else-if="activeOrders.length === 0" class="empty-msg">
        Ingen aktive ordrer for øyeblikket.
        <RouterLink to="/account/orders" class="link-inline">Se ordrehistorikk</RouterLink>
      </div>

      <ul v-else class="order-mini-list">
        <li
          v-for="order in activeOrders"
          :key="order.id"
          class="order-mini-row"
          @click="router.push(`/account/orders/${order.id}`)"
        >
          <div>
            <div class="order-mini-id">#{{ order.id }}</div>
            <div class="order-mini-date">{{ formatDate(order.createdAt) }}</div>
          </div>
          <div class="order-mini-right">
            <span class="badge" :class="statusClass(order.status)">
              {{ statusLabel(order.status) }}
            </span>
            <span class="order-mini-total">{{ formatNok(order.totalNok) }}</span>
          </div>
        </li>
      </ul>

      <div v-if="activeOrders.length > 0" class="more-link-row">
        <RouterLink to="/account/orders" class="link-faint">
          Se alle ordrer
          <i class="fa-solid fa-arrow-right fa-xs"></i>
        </RouterLink>
      </div>
    </div>

    <!-- Design requests quick link -->
    <div class="panel design-link-panel">
      <div class="design-link-inner">
        <div>
          <h2 class="section-title">
            <i class="fa-solid fa-paintbrush"></i>
            Mine design-bestillinger
          </h2>
          <p class="design-link-sub">AI-banner (95 kr) og manuelt design (495 kr)</p>
        </div>
        <RouterLink to="/account/design-requests" class="link-more">
          Se alle
          <i class="fa-solid fa-arrow-right"></i>
        </RouterLink>
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
.order-mini-id { font-size: 0.875rem; font-weight: 600; color: var(--accent); }
.order-mini-date { font-size: 0.75rem; color: var(--faint); margin-top: 1px; }
.order-mini-right { display: flex; align-items: center; gap: 0.625rem; }
.order-mini-total { font-size: 0.875rem; font-weight: 700; color: var(--text); }

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
</style>

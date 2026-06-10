<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { useRoute, RouterLink } from 'vue-router'
import { getOrder } from '@/api/orders'
import type { OrderDetailResponse } from '@/api/orders'
import { formatNok, formatDateLong } from '@/utils/format'

const route = useRoute()
const orderId = Number(route.params.orderId)

const order = ref<OrderDetailResponse | null>(null)
const loading = ref(true)
const error = ref<string | null>(null)

onMounted(async () => {
  try {
    order.value = await getOrder(orderId)
  } catch {
    error.value = 'Kunne ikke hente ordredetaljer. Sjekk Mine ordrer for å se statusen.'
  } finally {
    loading.value = false
  }
})

const formatDate = formatDateLong
</script>

<template>
  <div class="confirm-wrap">
    <!-- Stepper -->
    <nav class="stepper">
      <span class="stepper-step">1. Handlekurv &amp; levering</span>
      <span class="stepper-sep">›</span>
      <span class="stepper-step">2. Betaling</span>
      <span class="stepper-sep">›</span>
      <span class="stepper-step active">3. Bekreftelse</span>
    </nav>

    <!-- Loading -->
    <div v-if="loading" class="loading-state">
      <i class="fa-solid fa-circle-notch fa-spin loading-spinner"></i>
      <p>Laster ordredetaljer…</p>
    </div>

    <template v-else>
      <!-- Thank you banner -->
      <div class="thanks-banner">
        <div class="thanks-icon">
          <i class="fa-solid fa-circle-check"></i>
        </div>
        <h1 class="display thanks-title">Tusen takk for din bestilling!</h1>
        <p class="thanks-sub">Din ordre er mottatt og er under behandling.</p>
      </div>

      <!-- Error fallback -->
      <div v-if="error" class="alert-warn">
        <i class="fa-solid fa-triangle-exclamation"></i>
        <div>{{ error }}</div>
      </div>

      <template v-if="order">
        <!-- Order number + status -->
        <div class="panel order-meta">
          <div class="meta-grid">
            <div class="meta-cell">
              <div class="meta-label">Ordrenummer</div>
              <div class="meta-value meta-value--big">#{{ order.id }}</div>
            </div>
            <div class="meta-cell">
              <div class="meta-label">Status</div>
              <div class="meta-value meta-value--accent">
                <span v-if="order.status === 'PendingPayment'">
                  <i class="fa-solid fa-clock"></i> Venter på betaling
                </span>
                <span v-else-if="order.status === 'Paid'">
                  <i class="fa-solid fa-circle-check"></i> Betalt
                </span>
                <span v-else-if="order.status === 'InProduction'">
                  <i class="fa-solid fa-gears"></i> Under produksjon
                </span>
                <span v-else>{{ order.status }}</span>
              </div>
            </div>
            <div class="meta-cell">
              <div class="meta-label">Estimert levering</div>
              <div class="meta-value">{{ formatDate(order.estimatedDelivery) }}</div>
            </div>
          </div>
        </div>

        <!-- Order items -->
        <div class="panel">
          <h2 class="section-title">Bestilte varer</h2>
          <ul class="item-list">
            <li
              v-for="item in order.items"
              :key="item.id"
              class="item-row"
            >
              <div>
                <div class="item-name">{{ item.bannerSizeName }}</div>
                <div class="item-sub">{{ item.quantity }} stk × {{ formatNok(item.unitPriceNok) }}</div>
              </div>
              <div class="item-price">{{ formatNok(item.lineTotalNok) }}</div>
            </li>
          </ul>

          <!-- Price breakdown -->
          <dl class="summary-list">
            <div class="summary-row">
              <dt class="summary-label">Frakt</dt>
              <dd class="summary-value">{{ formatNok(order.shippingCostNok) }}</dd>
            </div>
            <div v-if="order.expressFeeNok > 0" class="summary-row">
              <dt class="summary-label">Ekspressgebyr</dt>
              <dd class="summary-value">{{ formatNok(order.expressFeeNok) }}</dd>
            </div>
            <div class="summary-divider">
              <div class="summary-row summary-row--total">
                <dt>Totalt inkl. MVA</dt>
                <dd class="summary-total-price">{{ formatNok(order.totalNok) }}</dd>
              </div>
              <div class="summary-row">
                <dt class="summary-faint">Herav MVA (25%)</dt>
                <dd class="summary-faint">{{ formatNok(order.totalNok * 0.2) }}</dd>
              </div>
            </div>
          </dl>
        </div>

        <!-- Delivery address -->
        <div v-if="order.shippingAddress" class="panel">
          <h2 class="section-title">
            <i class="fa-solid fa-location-dot"></i>
            Leveringsadresse
          </h2>
          <address class="addr-block">
            <div>{{ order.shippingAddress.line1 }}</div>
            <div v-if="order.shippingAddress.line2">{{ order.shippingAddress.line2 }}</div>
            <div>{{ order.shippingAddress.postalCode }} {{ order.shippingAddress.city }}</div>
          </address>
        </div>
      </template>

      <!-- Actions -->
      <div class="actions">
        <RouterLink to="/account/orders" class="btn btn-primary btn-action">
          <i class="fa-solid fa-box-open"></i>
          Følg ordren din
        </RouterLink>
        <RouterLink to="/" class="btn btn-ghost btn-action">
          <i class="fa-solid fa-arrow-left"></i>
          Fortsett å handle
        </RouterLink>
      </div>
    </template>
  </div>
</template>

<style scoped>
/* ── Layout ─────────────────────────────────────────────────── */
.confirm-wrap {
  max-width: 720px;
  margin: 0 auto;
  padding: 2rem 1.25rem 3.5rem;
  display: flex;
  flex-direction: column;
  gap: 1.25rem;
}

/* ── Stepper ────────────────────────────────────────────────── */
.stepper { display: flex; align-items: center; gap: 0.5rem; font-size: 0.875rem; }
.stepper-step { color: var(--faint); }
.stepper-step.active { color: var(--accent); font-weight: 600; }
.stepper-sep { color: var(--line); }

/* ── Loading ────────────────────────────────────────────────── */
.loading-state {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 1rem;
  padding: 4rem 0;
  color: var(--muted);
}
.loading-spinner { font-size: 2rem; color: var(--accent); }

/* ── Thank-you banner ───────────────────────────────────────── */
.thanks-banner {
  background: linear-gradient(135deg, rgba(60,180,100,.12) 0%, rgba(60,180,100,.06) 100%);
  border: 1px solid rgba(60,180,100,.25);
  border-radius: var(--radius);
  padding: 2.25rem 2rem;
  text-align: center;
}
.thanks-icon {
  font-size: 3rem;
  color: #4ec984;
  margin-bottom: 0.75rem;
  line-height: 1;
}
.thanks-title {
  font-size: clamp(1.4rem, 3vw, 1.875rem);
  color: var(--text);
  margin-bottom: 0.5rem;
}
.thanks-sub { color: #7de0a8; font-size: 0.9375rem; }

/* ── Alerts ─────────────────────────────────────────────────── */
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

/* ── Order meta grid ────────────────────────────────────────── */
.order-meta { }
.meta-grid {
  display: grid;
  grid-template-columns: repeat(3, 1fr);
  gap: 1rem;
  text-align: center;
}
@media (max-width: 540px) {
  .meta-grid { grid-template-columns: 1fr; text-align: left; }
}
.meta-label {
  font-size: 0.7rem;
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 0.07em;
  color: var(--muted);
  margin-bottom: 4px;
}
.meta-value {
  font-size: 1rem;
  font-weight: 600;
  color: var(--text);
}
.meta-value--big { font-size: 1.5rem; font-weight: 800; font-family: var(--font-display); }
.meta-value--accent { color: var(--accent); }

/* ── Section title ──────────────────────────────────────────── */
.section-title {
  font-size: 0.9375rem;
  font-weight: 700;
  color: var(--text);
  margin-bottom: 0.875rem;
  font-family: var(--font-display);
  display: flex;
  align-items: center;
  gap: 8px;
}
.section-title i { color: var(--faint); font-size: 0.875rem; }

/* ── Item list ──────────────────────────────────────────────── */
.item-list {
  list-style: none;
  padding: 0;
  margin: 0 0 0.75rem;
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

/* ── Summary ────────────────────────────────────────────────── */
.summary-list {
  display: flex;
  flex-direction: column;
  gap: 0.4rem;
  font-size: 0.875rem;
}
.summary-row { display: flex; justify-content: space-between; align-items: baseline; }
.summary-label { color: var(--muted); }
.summary-value { font-weight: 600; color: var(--text); }
.summary-faint { color: var(--faint); font-size: 0.8125rem; }
.summary-divider {
  border-top: 1px solid var(--line-soft);
  padding-top: 0.625rem;
  margin-top: 0.25rem;
  display: flex;
  flex-direction: column;
  gap: 0.35rem;
}
.summary-row--total { font-weight: 700; font-size: 1rem; color: var(--text); }
.summary-total-price { color: var(--accent); }

/* ── Address ────────────────────────────────────────────────── */
.addr-block {
  font-style: normal;
  font-size: 0.9rem;
  color: var(--muted);
  line-height: 1.7;
}

/* ── Actions ────────────────────────────────────────────────── */
.actions {
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
  margin-top: 0.5rem;
}
@media (min-width: 480px) {
  .actions { flex-direction: row; }
}
.btn-action {
  flex: 1;
  justify-content: center;
  padding: 13px;
  font-size: 0.9375rem;
  border-radius: 12px;
}
</style>

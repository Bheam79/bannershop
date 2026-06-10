<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useRoute, useRouter, RouterLink } from 'vue-router'
import { getOrder, deleteOrder } from '@/api/orders'
import type { OrderDetailResponse, OrderItemDetail, ProductionStatusEntry } from '@/api/orders'
import { formatNok, formatDateLong, formatDateTime } from '@/utils/format'
import { orderStatusLabel as statusLabel, orderStatusClass as statusClass } from '@/utils/orderStatus'

const route = useRoute()
const router = useRouter()
const orderId = Number(route.params.id)

const order = ref<OrderDetailResponse | null>(null)
const loading = ref(true)
const error = ref<string | null>(null)

// BANNERSH-185: customer "Betal nå" + "Slett" actions for unpaid orders.
const isUnpaid = computed(() =>
  order.value?.status === 'Draft' || order.value?.status === 'PendingPayment'
)
const canDelete = computed(() => {
  const s = order.value?.status
  return s === 'Draft' || s === 'PendingPayment' || s === 'Cancelled'
})

const deleting = ref(false)
async function onDeleteOrder() {
  if (!canDelete.value || deleting.value) return
  // eslint-disable-next-line no-alert
  if (!confirm(`Slette ordre #${orderId}? Dette kan ikke angres.`)) return
  deleting.value = true
  try {
    await deleteOrder(orderId)
    router.push('/account/orders')
  } catch (err: unknown) {
    const e = err as { response?: { data?: { error?: string } }; message?: string }
    error.value = e.response?.data?.error || e.message || 'Kunne ikke slette ordren.'
  } finally {
    deleting.value = false
  }
}

onMounted(async () => {
  try {
    order.value = await getOrder(orderId)
  } catch {
    error.value = 'Kunne ikke laste ordredetaljer. Prøv igjen.'
  } finally {
    loading.value = false
  }
})

// formatDate alias for templates in this view (long-month format for detail views)
const formatDate = formatDateLong

// ── Production stage helpers ──────────────────────────────────────────────────
const PRODUCTION_STEPS = [
  { key: 'Queued',      label: 'I kø' },
  { key: 'Printing',    label: 'Trykking' },
  { key: 'Finishing',   label: 'Etterbehandling' },
  { key: 'ReadyToShip', label: 'Klar til frakt' },
]

function stageIndex(stage: string): number {
  return PRODUCTION_STEPS.findIndex(s => s.key === stage)
}

function getHistoryEntry(item: OrderItemDetail, stage: string): ProductionStatusEntry | null {
  return (
    [...item.productionStatusHistory]
      .filter(e => e.stage === stage)
      .sort((a, b) => new Date(b.updatedAt).getTime() - new Date(a.updatedAt).getTime())[0] ?? null
  )
}

function itemLabel(item: OrderItemDetail): string {
  if (item.bannerSizeName) return item.bannerSizeName
  if (item.customWidthCm) return `${item.customWidthCm} × ${item.heightCm} cm`
  return `Banner ${item.heightCm} cm høy`
}

// ── Shipping helpers ──────────────────────────────────────────────────────────
const isShipped = computed(() =>
  order.value?.status === 'Shipped' || order.value?.status === 'Delivered'
)

const deliveryLabel = computed(() => {
  if (order.value?.deliveryType === 'Express') return 'Ekspress'
  if (order.value?.deliveryType === 'Pickup') return 'Henting'
  return 'Standard'
})

const packingLabel = computed(() => {
  if (order.value?.packingMode === 'Folded') return 'Brettes (flatt 50×60 cm)'
  return 'Rulles (rørform)'
})
</script>

<template>
  <div class="detail-wrap">
    <!-- Breadcrumb -->
    <div class="breadcrumb">
      <RouterLink to="/account/orders" class="breadcrumb-link">
        <i class="fa-solid fa-arrow-left"></i>
        Mine ordrer
      </RouterLink>
      <span class="breadcrumb-sep">›</span>
      <span class="breadcrumb-current">Ordre #{{ orderId }}</span>
    </div>

    <!-- Loading -->
    <div v-if="loading" class="loading-state">
      <i class="fa-solid fa-circle-notch fa-spin loading-spinner"></i>
    </div>

    <!-- Error -->
    <div v-else-if="error" class="alert-error">
      <i class="fa-solid fa-circle-exclamation"></i>
      {{ error }}
    </div>

    <template v-else-if="order">
      <!-- ── Order header ─────────────────────────────────────────────────── -->
      <div class="panel">
        <div class="order-header-top">
          <div>
            <h1 class="display order-title">Ordre #{{ order.id }}</h1>
            <p class="order-date">Bestilt {{ formatDate(order.createdAt) }}</p>
          </div>
          <span class="badge" :class="statusClass(order.status)">
            {{ statusLabel(order.status) }}
          </span>
        </div>

        <!-- BANNERSH-185: pay-now / delete actions for unpaid orders -->
        <div v-if="isUnpaid || canDelete" class="order-actions">
          <RouterLink
            v-if="isUnpaid"
            :to="`/account/orders/${order.id}/pay`"
            class="btn btn-primary"
          >
            <i class="fa-solid fa-credit-card"></i>
            Betal nå ({{ formatNok(order.totalNok) }})
          </RouterLink>
          <button
            v-if="canDelete"
            type="button"
            class="btn btn-ghost btn-delete"
            :disabled="deleting"
            @click="onDeleteOrder"
          >
            <i
              :class="deleting
                ? 'fa-solid fa-circle-notch fa-spin'
                : 'fa-solid fa-trash'"
            ></i>
            {{ deleting ? 'Sletter…' : 'Slett ordre' }}
          </button>
        </div>

        <div class="meta-grid">
          <div class="meta-cell">
            <div class="meta-label">Leveringstype</div>
            <div class="meta-value">{{ deliveryLabel }}</div>
          </div>
          <div class="meta-cell">
            <div class="meta-label">Pakking</div>
            <div class="meta-value">{{ packingLabel }}</div>
          </div>
          <div class="meta-cell">
            <div class="meta-label">Estimert levering</div>
            <div class="meta-value">{{ formatDate(order.estimatedDelivery) }}</div>
          </div>
          <div class="meta-cell">
            <div class="meta-label">Totalt inkl. MVA</div>
            <div class="meta-value meta-value--accent">{{ formatNok(order.totalNok) }}</div>
          </div>
        </div>
      </div>

      <!-- ── Production tracking (per item) ─────────────────────────────── -->
      <section
        v-if="order.status !== 'Cancelled' && order.status !== 'PendingPayment' && order.status !== 'Draft'"
      >
        <h2 class="section-title">
          <i class="fa-solid fa-gears"></i>
          Produksjonsstatus
        </h2>

        <div
          v-for="item in order.items"
          :key="item.id"
          class="panel prod-item"
        >
          <div class="prod-item__header">
            <div>
              <div class="prod-item__name">{{ itemLabel(item) }}</div>
              <div class="prod-item__qty">{{ item.quantity }} stk</div>
            </div>
            <div class="prod-item__total">{{ formatNok(item.lineTotalNok) }}</div>
          </div>

          <!-- Progress stepper -->
          <div class="prod-stepper">
            <!-- Connecting line -->
            <div class="prod-track" aria-hidden="true">
              <div
                class="prod-fill"
                :style="{
                  width: `${Math.min(stageIndex(item.currentProductionStage) / (PRODUCTION_STEPS.length - 1), 1) * 100}%`
                }"
              />
            </div>

            <ol class="prod-steps">
              <li
                v-for="(step, idx) in PRODUCTION_STEPS"
                :key="step.key"
                class="prod-step"
                :style="{ width: `${100 / PRODUCTION_STEPS.length}%` }"
              >
                <!-- Circle -->
                <div
                  class="prod-circle"
                  :class="{
                    'prod-circle--done':    idx < stageIndex(item.currentProductionStage),
                    'prod-circle--active':  idx === stageIndex(item.currentProductionStage),
                    'prod-circle--pending': idx > stageIndex(item.currentProductionStage),
                  }"
                >
                  <i
                    v-if="idx < stageIndex(item.currentProductionStage)"
                    class="fa-solid fa-check prod-check-icon"
                  />
                  <div v-else-if="idx === stageIndex(item.currentProductionStage)" class="prod-dot" />
                </div>

                <!-- Step label -->
                <div class="prod-step__label-wrap">
                  <div
                    class="prod-step__label"
                    :class="{
                      'prod-step__label--active':  idx === stageIndex(item.currentProductionStage),
                      'prod-step__label--done':    idx < stageIndex(item.currentProductionStage),
                      'prod-step__label--pending': idx > stageIndex(item.currentProductionStage),
                    }"
                  >
                    {{ step.label }}
                  </div>
                  <div
                    v-if="getHistoryEntry(item, step.key)"
                    class="prod-step__time"
                  >
                    {{ formatDateTime(getHistoryEntry(item, step.key)!.updatedAt) }}
                  </div>
                </div>
              </li>
            </ol>
          </div>

          <!-- Notes -->
          <div
            v-if="item.productionStatusHistory.length > 0 && item.productionStatusHistory.slice(-1)[0]?.notes"
            class="prod-note"
          >
            <i class="fa-solid fa-circle-info"></i>
            <span><strong>Merknad:</strong> {{ item.productionStatusHistory.slice(-1)[0]?.notes }}</span>
          </div>
        </div>
      </section>

      <!-- ── Shipping tracking ──────────────────────────────────────────── -->
      <section v-if="isShipped && order.shipmentTracking">
        <h2 class="section-title">
          <i class="fa-solid fa-truck"></i>
          Fraktstatus
        </h2>
        <div class="panel">
          <div class="ship-grid">
            <div class="ship-cell">
              <div class="meta-label">Transportør</div>
              <div class="meta-value">{{ order.shipmentTracking.carrier }}</div>
            </div>
            <div class="ship-cell">
              <div class="meta-label">Sporingsnummer</div>
              <div class="meta-value">
                <a
                  v-if="order.shipmentTracking.trackingUrl"
                  :href="order.shipmentTracking.trackingUrl"
                  target="_blank"
                  rel="noopener"
                  class="track-link"
                >
                  {{ order.shipmentTracking.trackingNumber }}
                  <i class="fa-solid fa-arrow-up-right-from-square fa-xs"></i>
                </a>
                <span v-else>{{ order.shipmentTracking.trackingNumber }}</span>
              </div>
            </div>
            <div class="ship-cell">
              <div class="meta-label">Sendt dato</div>
              <div class="meta-value">{{ formatDate(order.shipmentTracking.shippedAt) }}</div>
            </div>
            <div class="ship-cell">
              <div class="meta-label">Estimert ankomst</div>
              <div class="meta-value">{{ formatDate(order.shipmentTracking.estimatedArrival) }}</div>
            </div>
            <div v-if="order.shipmentTracking.deliveredAt" class="ship-cell ship-cell--full">
              <div class="meta-label">Levert</div>
              <div class="meta-value meta-value--green">
                <i class="fa-solid fa-circle-check"></i>
                {{ formatDate(order.shipmentTracking.deliveredAt) }}
              </div>
            </div>
          </div>
        </div>
      </section>

      <!-- ── Order items + price breakdown ─────────────────────────────── -->
      <section>
        <h2 class="section-title">
          <i class="fa-solid fa-list"></i>
          Varer
        </h2>
        <div class="panel panel--no-pad">
          <ul class="item-list">
            <li
              v-for="item in order.items"
              :key="item.id"
              class="item-row"
            >
              <div>
                <div class="item-name">{{ itemLabel(item) }}</div>
                <div class="item-sub">{{ item.quantity }} stk × {{ formatNok(item.unitPriceNok) }}</div>
              </div>
              <div class="item-price">{{ formatNok(item.lineTotalNok) }}</div>
            </li>
          </ul>

          <dl class="price-breakdown">
            <div class="price-row">
              <dt class="price-label">Frakt</dt>
              <dd class="price-value">{{ formatNok(order.shippingCostNok) }}</dd>
            </div>
            <div v-if="order.expressFeeNok > 0" class="price-row">
              <dt class="price-label">Ekspressgebyr</dt>
              <dd class="price-value">{{ formatNok(order.expressFeeNok) }}</dd>
            </div>
            <div class="price-row price-row--total">
              <dt>Totalt inkl. MVA</dt>
              <dd class="price-total">{{ formatNok(order.totalNok) }}</dd>
            </div>
            <div class="price-row">
              <dt class="price-faint">Herav MVA (25%)</dt>
              <dd class="price-faint">{{ formatNok(order.totalNok * 0.2) }}</dd>
            </div>
          </dl>
        </div>
      </section>

      <!-- ── Shipping address ───────────────────────────────────────────── -->
      <section v-if="order.shippingAddress">
        <h2 class="section-title">
          <i class="fa-solid fa-location-dot"></i>
          Leveringsadresse
        </h2>
        <div class="panel">
          <address class="addr-block">
            <div>{{ order.shippingAddress.line1 }}</div>
            <div v-if="order.shippingAddress.line2">{{ order.shippingAddress.line2 }}</div>
            <div>{{ order.shippingAddress.postalCode }} {{ order.shippingAddress.city }}</div>
          </address>
        </div>
      </section>

      <RouterLink to="/account/orders" class="back-link">
        <i class="fa-solid fa-arrow-left"></i>
        Tilbake til ordrelisten
      </RouterLink>
    </template>
  </div>
</template>

<style scoped>
/* ── Layout ─────────────────────────────────────────────────── */
.detail-wrap {
  max-width: 860px;
  margin: 0 auto;
  padding: 2.5rem 1.25rem 3rem;
  display: flex;
  flex-direction: column;
  gap: 1.5rem;
}

/* ── Breadcrumb ─────────────────────────────────────────────── */
.breadcrumb {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  font-size: 0.875rem;
}
.breadcrumb-link {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  color: var(--accent);
  text-decoration: none;
  font-weight: 600;
  transition: color 0.15s;
}
.breadcrumb-link:hover { color: var(--accent-2); }
.breadcrumb-sep { color: var(--line); }
.breadcrumb-current { color: var(--muted); }

/* ── Loading / error ────────────────────────────────────────── */
.loading-state { display: flex; justify-content: center; padding: 4rem 0; }
.loading-spinner { font-size: 2rem; color: var(--accent); }
.alert-error {
  display: flex;
  align-items: flex-start;
  gap: 10px;
  padding: 14px 18px;
  background: rgba(220,60,60,.12);
  border: 1px solid rgba(220,60,60,.3);
  border-radius: 14px;
  color: #f4a57a;
  font-size: 0.9rem;
}
.alert-error i { color: #e05252; flex-shrink: 0; margin-top: 2px; }

/* ── Order header ───────────────────────────────────────────── */
.order-header-top {
  display: flex;
  flex-wrap: wrap;
  align-items: flex-start;
  justify-content: space-between;
  gap: 1rem;
  margin-bottom: 1.25rem;
}
.order-title {
  font-size: clamp(1.25rem, 2.5vw, 1.5rem);
  color: var(--text);
}
.order-date { font-size: 0.8125rem; color: var(--muted); margin-top: 3px; }

/* BANNERSH-185: pay-now / delete row */
.order-actions {
  display: flex;
  gap: 0.625rem;
  flex-wrap: wrap;
  margin-bottom: 1.25rem;
}
.btn-delete {
  color: #f4a57a;
  border: 1px solid rgba(220,60,60,.32);
  background: rgba(220,60,60,.08);
}
.btn-delete:hover:not(:disabled) {
  background: rgba(220,60,60,.16);
  color: #ff8c6a;
}
.btn-delete:disabled { opacity: 0.55; cursor: not-allowed; }

/* ── Meta grid ──────────────────────────────────────────────── */
.meta-grid {
  display: grid;
  grid-template-columns: repeat(4, 1fr);
  gap: 1rem;
}
@media (max-width: 640px) { .meta-grid { grid-template-columns: 1fr 1fr; } }
.meta-cell { }
.meta-label {
  font-size: 0.7rem;
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 0.07em;
  color: var(--faint);
  margin-bottom: 3px;
}
.meta-value { font-size: 0.9375rem; font-weight: 600; color: var(--text); }
.meta-value--accent { color: var(--accent); }
.meta-value--green { color: #7de0a8; display: flex; align-items: center; gap: 6px; }

/* ── Section title ──────────────────────────────────────────── */
.section-title {
  font-size: 0.9375rem;
  font-weight: 700;
  color: var(--text);
  font-family: var(--font-display);
  display: flex;
  align-items: center;
  gap: 8px;
  margin-bottom: 0.75rem;
}
.section-title i { color: var(--accent); font-size: 0.875rem; }

/* ── Production tracking ────────────────────────────────────── */
.prod-item { display: flex; flex-direction: column; gap: 1.25rem; }
.prod-item__header {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 0.5rem;
}
.prod-item__name { font-weight: 600; color: var(--text); }
.prod-item__qty { font-size: 0.8125rem; color: var(--muted); margin-top: 2px; }
.prod-item__total { font-size: 0.875rem; font-weight: 700; color: var(--text); flex-shrink: 0; }

.prod-stepper { position: relative; }
.prod-track {
  position: absolute;
  top: 15px;
  left: 0; right: 0;
  height: 2px;
  background: var(--line);
  border-radius: 2px;
}
.prod-fill {
  height: 100%;
  background: var(--accent);
  border-radius: 2px;
  transition: width 0.5s ease;
}
.prod-steps {
  position: relative;
  display: flex;
  justify-content: space-between;
  list-style: none;
  padding: 0;
  margin: 0;
}
.prod-step {
  display: flex;
  flex-direction: column;
  align-items: center;
}
.prod-circle {
  width: 30px;
  height: 30px;
  border-radius: 50%;
  border: 2px solid var(--line);
  display: flex;
  align-items: center;
  justify-content: center;
  position: relative;
  z-index: 1;
  transition: border-color 0.3s, background 0.3s;
  background: var(--surface);
}
.prod-circle--done    { background: var(--accent); border-color: var(--accent); }
.prod-circle--active  { border-color: var(--accent); background: var(--surface); }
.prod-circle--pending { border-color: var(--line); background: var(--surface); }
.prod-check-icon { color: var(--accent-ink); font-size: 0.75rem; }
.prod-dot { width: 10px; height: 10px; border-radius: 50%; background: var(--accent); }

.prod-step__label-wrap {
  margin-top: 8px;
  text-align: center;
  padding: 0 4px;
}
.prod-step__label {
  font-size: 0.72rem;
  font-weight: 700;
  line-height: 1.2;
}
.prod-step__label--active  { color: var(--accent); }
.prod-step__label--done    { color: var(--muted); }
.prod-step__label--pending { color: var(--faint); }
.prod-step__time {
  font-size: 0.65rem;
  color: var(--faint);
  margin-top: 2px;
  line-height: 1.2;
}

.prod-note {
  display: flex;
  align-items: flex-start;
  gap: 8px;
  padding: 10px 12px;
  background: var(--surface-2);
  border: 1px solid var(--line-soft);
  border-radius: 10px;
  font-size: 0.8125rem;
  color: var(--muted);
}
.prod-note i { color: var(--faint); flex-shrink: 0; margin-top: 2px; }

/* ── Shipping grid ──────────────────────────────────────────── */
.ship-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 1rem;
}
.ship-cell { }
.ship-cell--full { grid-column: 1 / -1; }
.track-link {
  color: var(--accent);
  text-decoration: none;
  display: inline-flex;
  align-items: center;
  gap: 5px;
  transition: color 0.15s;
}
.track-link:hover { color: var(--accent-2); }

/* ── Item list ──────────────────────────────────────────────── */
.panel--no-pad { padding: 0; overflow: hidden; }
.item-list { list-style: none; padding: 0; margin: 0; }
.item-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 14px 20px;
  border-bottom: 1px solid var(--line-soft);
  font-size: 0.875rem;
}
.item-row:last-child { border-bottom: none; }
.item-name { font-weight: 600; color: var(--text); }
.item-sub { font-size: 0.8rem; color: var(--muted); margin-top: 2px; }
.item-price { font-weight: 700; color: var(--text); flex-shrink: 0; margin-left: 1rem; }

.price-breakdown {
  border-top: 1px solid var(--line-soft);
  padding: 14px 20px;
  display: flex;
  flex-direction: column;
  gap: 6px;
  font-size: 0.875rem;
}
.price-row { display: flex; justify-content: space-between; align-items: baseline; }
.price-label { color: var(--muted); }
.price-value { font-weight: 600; color: var(--text); }
.price-row--total {
  border-top: 1px solid var(--line-soft);
  padding-top: 8px;
  margin-top: 2px;
  font-weight: 700;
  font-size: 1rem;
  color: var(--text);
}
.price-total { color: var(--accent); }
.price-faint { color: var(--faint); font-size: 0.8rem; }

/* ── Address ────────────────────────────────────────────────── */
.addr-block {
  font-style: normal;
  font-size: 0.9rem;
  color: var(--muted);
  line-height: 1.7;
}

/* ── Back link ──────────────────────────────────────────────── */
.back-link {
  display: inline-flex;
  align-items: center;
  gap: 7px;
  font-size: 0.875rem;
  font-weight: 600;
  color: var(--accent);
  text-decoration: none;
  transition: color 0.15s;
}
.back-link:hover { color: var(--accent-2); }

/* ── Status badges ──────────────────────────────────────────── */
.badge {
  display: inline-block;
  font-size: 0.75rem;
  font-weight: 700;
  padding: 4px 11px;
  border-radius: 99px;
  letter-spacing: 0.01em;
  white-space: nowrap;
}
.badge-draft    { background: rgba(138,128,115,.18); color: var(--muted);     border: 1px solid rgba(138,128,115,.3); }
.badge-pending  { background: rgba(231,185,78,.15);  color: #e7d08a;          border: 1px solid rgba(231,185,78,.3); }
.badge-paid     { background: rgba(255,106,61,.13);  color: var(--accent-2);  border: 1px solid rgba(255,106,61,.3); }
.badge-ready    { background: rgba(160,110,220,.15); color: #c9a8f5;          border: 1px solid rgba(160,110,220,.3); }
.badge-shipped  { background: rgba(60,180,100,.13);  color: #7de0a8;          border: 1px solid rgba(60,180,100,.28); }
.badge-cancelled{ background: rgba(220,60,60,.12);   color: #f4a57a;          border: 1px solid rgba(220,60,60,.28); }
</style>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { listOrders } from '@/api/orders'
import type { OrderListItem } from '@/api/orders'

const router = useRouter()

const orders = ref<OrderListItem[]>([])
const page = ref(1)
const totalPages = ref(1)
const totalCount = ref(0)
const loading = ref(true)
const error = ref<string | null>(null)

const PAGE_SIZE = 20

async function load(p = 1) {
  loading.value = true
  error.value = null
  try {
    const result = await listOrders(p, PAGE_SIZE)
    orders.value = result.items
    page.value = result.page
    totalPages.value = result.totalPages
    totalCount.value = result.totalCount
  } catch {
    error.value = 'Kunne ikke laste ordrer. Prøv igjen.'
  } finally {
    loading.value = false
  }
}

onMounted(() => load(1))

function goToOrder(id: number) {
  router.push(`/account/orders/${id}`)
}

function formatNok(n: number): string {
  return new Intl.NumberFormat('nb-NO', { maximumFractionDigits: 0 }).format(n) + ' kr'
}

function formatDate(iso: string): string {
  return new Date(iso).toLocaleDateString('nb-NO', {
    day: '2-digit',
    month: 'short',
    year: 'numeric',
  })
}

// ── Status helpers ────────────────────────────────────────────────────────────
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
function deliveryLabel(d: string) {
  if (d === 'Express') return 'Ekspress'
  if (d === 'Pickup') return 'Henting'
  return 'Standard'
}

const hasPrev = computed(() => page.value > 1)
const hasNext = computed(() => page.value < totalPages.value)
</script>

<template>
  <div class="orders-wrap">
    <div class="page-header">
      <div>
        <h1 class="display page-title">
          <i class="fa-solid fa-box"></i>
          Mine ordrer
        </h1>
        <p v-if="!loading && totalCount > 0" class="page-sub">
          {{ totalCount }} ordre{{ totalCount !== 1 ? 'r' : '' }} totalt
        </p>
      </div>
      <RouterLink to="/account" class="back-link">
        <i class="fa-solid fa-arrow-left"></i>
        Min konto
      </RouterLink>
    </div>

    <!-- Loading -->
    <div v-if="loading" class="loading-state">
      <i class="fa-solid fa-circle-notch fa-spin loading-spinner"></i>
    </div>

    <!-- Error -->
    <div v-else-if="error" class="alert-error">
      <i class="fa-solid fa-circle-exclamation"></i>
      <div>
        {{ error }}
        <button class="retry-btn" @click="load(page)">Prøv igjen</button>
      </div>
    </div>

    <!-- Empty -->
    <div v-else-if="orders.length === 0" class="empty-state">
      <i class="fa-solid fa-box empty-icon"></i>
      <p class="empty-title">Ingen ordrer ennå</p>
      <p class="empty-sub">Dine bestillinger vil vises her.</p>
      <RouterLink to="/" class="btn btn-primary empty-action">
        Handle bannere
      </RouterLink>
    </div>

    <!-- Table -->
    <template v-else>
      <div class="orders-panel">
        <!-- Desktop table -->
        <table class="orders-table">
          <thead class="table-head">
            <tr>
              <th class="th">Ordre #</th>
              <th class="th">Dato</th>
              <th class="th">Status</th>
              <th class="th">Levering</th>
              <th class="th">Varer</th>
              <th class="th th--right">Totalt</th>
            </tr>
          </thead>
          <tbody>
            <tr
              v-for="order in orders"
              :key="order.id"
              class="order-row"
              @click="goToOrder(order.id)"
            >
              <td class="td td--id">#{{ order.id }}</td>
              <td class="td td--muted">{{ formatDate(order.createdAt) }}</td>
              <td class="td">
                <span class="badge" :class="statusClass(order.status)">
                  {{ statusLabel(order.status) }}
                </span>
              </td>
              <td class="td td--muted">{{ deliveryLabel(order.deliveryType) }}</td>
              <td class="td td--muted">{{ order.itemCount }} stk</td>
              <td class="td td--right td--bold">{{ formatNok(order.totalNok) }}</td>
            </tr>
          </tbody>
        </table>

        <!-- Mobile card list -->
        <ul class="mobile-list">
          <li
            v-for="order in orders"
            :key="order.id"
            class="mobile-row"
            @click="goToOrder(order.id)"
          >
            <div class="mobile-row__left">
              <div class="mobile-row__id">#{{ order.id }}</div>
              <div class="mobile-row__sub">{{ formatDate(order.createdAt) }} · {{ deliveryLabel(order.deliveryType) }}</div>
              <span class="badge" :class="statusClass(order.status)">
                {{ statusLabel(order.status) }}
              </span>
            </div>
            <div class="mobile-row__right">
              <div class="mobile-row__total">{{ formatNok(order.totalNok) }}</div>
              <div class="mobile-row__items">{{ order.itemCount }} vare{{ order.itemCount !== 1 ? 'r' : '' }}</div>
            </div>
          </li>
        </ul>
      </div>

      <!-- Pagination -->
      <div v-if="totalPages > 1" class="pagination">
        <button
          :disabled="!hasPrev"
          class="btn btn-ghost pager-btn"
          @click="load(page - 1)"
        >
          <i class="fa-solid fa-arrow-left"></i>
          Forrige
        </button>
        <span class="pager-info">Side {{ page }} av {{ totalPages }}</span>
        <button
          :disabled="!hasNext"
          class="btn btn-ghost pager-btn"
          @click="load(page + 1)"
        >
          Neste
          <i class="fa-solid fa-arrow-right"></i>
        </button>
      </div>
    </template>
  </div>
</template>

<style scoped>
/* ── Layout ─────────────────────────────────────────────────── */
.orders-wrap {
  max-width: 1000px;
  margin: 0 auto;
  padding: 2.5rem 1.25rem 3rem;
}

/* ── Header ─────────────────────────────────────────────────── */
.page-header {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 1rem;
  margin-bottom: 1.5rem;
}
.page-title {
  font-size: clamp(1.4rem, 3vw, 1.875rem);
  color: var(--text);
  display: flex;
  align-items: center;
  gap: 0.5rem;
}
.page-title i { color: var(--accent); }
.page-sub { font-size: 0.8125rem; color: var(--muted); margin-top: 3px; }

.back-link {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  font-size: 0.8125rem;
  font-weight: 600;
  color: var(--muted);
  text-decoration: none;
  transition: color 0.15s;
  white-space: nowrap;
  padding-top: 4px;
}
.back-link:hover { color: var(--accent); }

/* ── Loading ────────────────────────────────────────────────── */
.loading-state {
  display: flex;
  justify-content: center;
  padding: 4rem 0;
}
.loading-spinner { font-size: 2rem; color: var(--accent); }

/* ── Alert ──────────────────────────────────────────────────── */
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
.retry-btn {
  display: block;
  margin-top: 4px;
  background: none;
  border: none;
  color: var(--accent);
  font-size: 0.8125rem;
  font-weight: 600;
  cursor: pointer;
  padding: 0;
  text-decoration: underline;
}

/* ── Empty ──────────────────────────────────────────────────── */
.empty-state {
  text-align: center;
  padding: 4rem 1rem;
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 0.5rem;
}
.empty-icon { font-size: 2.5rem; color: var(--faint); margin-bottom: 0.5rem; }
.empty-title { font-size: 1.125rem; font-weight: 700; color: var(--text); }
.empty-sub { font-size: 0.875rem; color: var(--muted); }
.empty-action { margin-top: 1rem; }

/* ── Orders panel ───────────────────────────────────────────── */
.orders-panel {
  background: var(--surface);
  border: 1px solid var(--line-soft);
  border-radius: var(--radius);
  overflow: hidden;
}

/* Desktop table */
.orders-table {
  width: 100%;
  font-size: 0.875rem;
  border-collapse: collapse;
  display: none;
}
@media (min-width: 640px) { .orders-table { display: table; } }

.table-head { background: var(--surface-2); border-bottom: 1px solid var(--line-soft); }
.th {
  text-align: left;
  padding: 10px 18px;
  font-size: 0.75rem;
  font-weight: 700;
  color: var(--muted);
  text-transform: uppercase;
  letter-spacing: 0.04em;
}
.th--right { text-align: right; }

.order-row {
  border-bottom: 1px solid var(--line-soft);
  cursor: pointer;
  transition: background 0.12s;
}
.order-row:last-child { border-bottom: none; }
.order-row:hover { background: var(--surface-2); }

.td { padding: 14px 18px; color: var(--text); vertical-align: middle; }
.td--id { font-weight: 700; color: var(--accent); }
.td--muted { color: var(--muted); }
.td--right { text-align: right; }
.td--bold { font-weight: 700; }

/* Mobile list */
.mobile-list {
  list-style: none;
  padding: 0;
  margin: 0;
  display: flex;
  flex-direction: column;
}
@media (min-width: 640px) { .mobile-list { display: none; } }

.mobile-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 1rem;
  border-bottom: 1px solid var(--line-soft);
  cursor: pointer;
  transition: background 0.12s;
}
.mobile-row:last-child { border-bottom: none; }
.mobile-row:hover { background: var(--surface-2); }
.mobile-row__left { display: flex; flex-direction: column; gap: 4px; }
.mobile-row__id { font-size: 0.9375rem; font-weight: 700; color: var(--accent); }
.mobile-row__sub { font-size: 0.75rem; color: var(--faint); }
.mobile-row__right { text-align: right; flex-shrink: 0; margin-left: 1rem; }
.mobile-row__total { font-size: 0.9375rem; font-weight: 700; color: var(--text); }
.mobile-row__items { font-size: 0.75rem; color: var(--faint); margin-top: 2px; }

/* ── Pagination ─────────────────────────────────────────────── */
.pagination {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-top: 1.25rem;
  font-size: 0.875rem;
}
.pager-btn { font-size: 0.875rem; }
.pager-btn:disabled { opacity: 0.4; cursor: not-allowed; }
.pager-info { color: var(--muted); }

/* ── Status badges ──────────────────────────────────────────── */
.badge {
  display: inline-block;
  font-size: 0.72rem;
  font-weight: 700;
  padding: 3px 9px;
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

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { listOrders } from '@/api/orders'
import { listDesignRequests } from '@/api/designRequests'

const router = useRouter()

// ── Unified item type ─────────────────────────────────────────────────────────
interface UnifiedItem {
  id: number
  displayId: string           // e.g. 'O-3', 'AI-4', 'D-2' — unique across kinds
  kind: 'order' | 'design'
  typeLabel: string           // 'Eget' | 'AI' | 'Designer'
  typeBadgeClass: string
  status: string
  statusLabel: string
  statusClass: string
  date: string
  priceNok: number
  previewUrl: string | null
  detailPath: string
  isAwaitingApproval: boolean
}

const allItems = ref<UnifiedItem[]>([])
const page = ref(1)
const PAGE_SIZE = 20
const loading = ref(true)
const error = ref<string | null>(null)

const totalPages = computed(() => Math.ceil(allItems.value.length / PAGE_SIZE))
const pagedItems = computed(() =>
  allItems.value.slice((page.value - 1) * PAGE_SIZE, page.value * PAGE_SIZE),
)
const totalCount = computed(() => allItems.value.length)
const hasPrev = computed(() => page.value > 1)
const hasNext = computed(() => page.value < totalPages.value)

// ── Status maps ───────────────────────────────────────────────────────────────
const ORDER_STATUS_LABELS: Record<string, string> = {
  Draft:          'Utkast',
  PendingPayment: 'Venter betaling',
  Paid:           'Betalt',
  InProduction:   'I produksjon',
  ReadyToShip:    'Klar til frakt',
  Shipped:        'Sendt',
  Delivered:      'Levert',
  Cancelled:      'Kansellert',
}
const ORDER_STATUS_CLASSES: Record<string, string> = {
  Draft:          'badge-draft',
  PendingPayment: 'badge-pending',
  Paid:           'badge-paid',
  InProduction:   'badge-paid',
  ReadyToShip:    'badge-ready',
  Shipped:        'badge-shipped',
  Delivered:      'badge-shipped',
  Cancelled:      'badge-cancelled',
}
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

async function load() {
  loading.value = true
  error.value = null
  page.value = 1
  try {
    const [ordersResult, drList] = await Promise.all([
      listOrders(1, 100),
      listDesignRequests(),
    ])

    const orderItems: UnifiedItem[] = ordersResult.items.map(o => {
      // BANNERSH-139: AI credit-pack purchases are tracked as Orders with
      // OrderType=CreditPack. Surface them under the user's "Mine ordrer" list
      // with a clear "AI-pakke" label so the receipt is visible.
      const isCreditPack = o.orderType === 'CreditPack'
      return {
        id: o.id,
        displayId: isCreditPack ? `AI-pakke-${o.id}` : `O-${o.id}`,
        kind: 'order' as const,
        typeLabel: isCreditPack ? 'AI-pakke' : 'Eget',
        typeBadgeClass: isCreditPack ? 'badge-type-creditpack' : 'badge-type-order',
        status: o.status,
        statusLabel: ORDER_STATUS_LABELS[o.status] ?? o.status,
        statusClass: ORDER_STATUS_CLASSES[o.status] ?? 'badge-draft',
        date: o.createdAt,
        priceNok: o.totalNok,
        previewUrl: null,
        detailPath: `/account/orders/${o.id}`,
        isAwaitingApproval: false,
      }
    })

    const drItems: UnifiedItem[] = drList.map(dr => ({
      id: dr.id,
      displayId: dr.mode === 'Ai' ? `AI-${dr.id}` : `D-${dr.id}`,
      kind: 'design' as const,
      typeLabel: dr.mode === 'Ai' ? 'AI' : 'Designer',
      typeBadgeClass: dr.mode === 'Ai' ? 'badge-type-ai' : 'badge-type-designer',
      status: dr.status,
      statusLabel: DR_STATUS_LABELS[dr.status] ?? dr.status,
      statusClass: DR_STATUS_CLASSES[dr.status] ?? 'badge-draft',
      date: dr.createdAt,
      priceNok: dr.priceNok,
      previewUrl: dr.previewUrl,
      detailPath: `/account/design-requests/${dr.id}`,
      isAwaitingApproval: dr.status === 'AwaitingApproval',
    }))

    allItems.value = [...orderItems, ...drItems].sort(
      (a, b) => new Date(b.date).getTime() - new Date(a.date).getTime(),
    )
  } catch {
    error.value = 'Kunne ikke laste ordrer. Prøv igjen.'
  } finally {
    loading.value = false
  }
}

onMounted(() => load())

function goToItem(item: UnifiedItem) {
  router.push(item.detailPath)
}

function formatNok(n: number): string {
  return new Intl.NumberFormat('nb-NO', { maximumFractionDigits: 0 }).format(n) + ' kr'
}

function formatDate(iso: string): string {
  return new Date(iso).toLocaleDateString('nb-NO', {
    day: '2-digit', month: 'short', year: 'numeric',
  })
}
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
        <button class="retry-btn" @click="load()">Prøv igjen</button>
      </div>
    </div>

    <!-- Empty -->
    <div v-else-if="allItems.length === 0" class="empty-state">
      <i class="fa-solid fa-box empty-icon"></i>
      <p class="empty-title">Ingen ordrer ennå</p>
      <p class="empty-sub">Dine bestillinger vil vises her.</p>
      <RouterLink to="/" class="btn btn-primary empty-action">
        Handle bannere
      </RouterLink>
    </div>

    <!-- Unified table -->
    <template v-else>
      <div class="orders-panel">
        <!-- Desktop table -->
        <table class="orders-table">
          <thead class="table-head">
            <tr>
              <th class="th th--thumb"></th>
              <th class="th">Ordre #</th>
              <th class="th">Type</th>
              <th class="th">Status</th>
              <th class="th">Dato</th>
              <th class="th th--right">Totalt</th>
            </tr>
          </thead>
          <tbody>
            <tr
              v-for="item in pagedItems"
              :key="`${item.kind}-${item.id}`"
              class="order-row"
              @click="goToItem(item)"
            >
              <!-- Thumbnail -->
              <td class="td td--thumb">
                <div class="thumb-cell">
                  <img
                    v-if="item.previewUrl"
                    :src="item.previewUrl"
                    alt=""
                    class="thumb-img"
                  />
                  <div v-else class="thumb-placeholder">
                    <i class="fa-solid fa-image"></i>
                  </div>
                </div>
              </td>
              <td class="td td--id">#{{ item.displayId }}</td>
              <td class="td">
                <span class="type-chip" :class="item.typeBadgeClass">
                  {{ item.typeLabel }}
                </span>
              </td>
              <td class="td">
                <div class="status-cell">
                  <span class="badge" :class="item.statusClass">
                    {{ item.statusLabel }}
                  </span>
                  <RouterLink
                    v-if="item.isAwaitingApproval"
                    :to="item.detailPath"
                    class="approve-link"
                    @click.stop
                  >
                    Godkjenn design
                    <i class="fa-solid fa-arrow-right fa-xs"></i>
                  </RouterLink>
                </div>
              </td>
              <td class="td td--muted">{{ formatDate(item.date) }}</td>
              <td class="td td--right td--bold">{{ formatNok(item.priceNok) }}</td>
            </tr>
          </tbody>
        </table>

        <!-- Mobile card list -->
        <ul class="mobile-list">
          <li
            v-for="item in pagedItems"
            :key="`${item.kind}-${item.id}`"
            class="mobile-row"
            @click="goToItem(item)"
          >
            <!-- Preview thumbnail -->
            <div class="mobile-thumb">
              <img
                v-if="item.previewUrl"
                :src="item.previewUrl"
                alt=""
                class="mobile-thumb__img"
              />
              <div v-else class="mobile-thumb__placeholder">
                <i class="fa-solid fa-image"></i>
              </div>
            </div>

            <div class="mobile-row__body">
              <div class="mobile-row__top">
                <span class="type-chip" :class="item.typeBadgeClass">{{ item.typeLabel }}</span>
                <span class="mobile-row__id">#{{ item.displayId }}</span>
              </div>
              <div class="mobile-row__sub">{{ formatDate(item.date) }}</div>
              <div class="mobile-row__badges">
                <span class="badge" :class="item.statusClass">{{ item.statusLabel }}</span>
                <RouterLink
                  v-if="item.isAwaitingApproval"
                  :to="item.detailPath"
                  class="approve-link"
                  @click.stop
                >
                  Godkjenn design →
                </RouterLink>
              </div>
            </div>

            <div class="mobile-row__price">
              {{ formatNok(item.priceNok) }}
            </div>
          </li>
        </ul>
      </div>

      <!-- Pagination -->
      <div v-if="totalPages > 1" class="pagination">
        <button
          :disabled="!hasPrev"
          class="btn btn-ghost pager-btn"
          @click="page--"
        >
          <i class="fa-solid fa-arrow-left"></i>
          Forrige
        </button>
        <span class="pager-info">Side {{ page }} av {{ totalPages }}</span>
        <button
          :disabled="!hasNext"
          class="btn btn-ghost pager-btn"
          @click="page++"
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

/* ── Thumbnail ──────────────────────────────────────────────── */
.thumb-cell {
  width: 44px;
  height: 44px;
  border-radius: 8px;
  overflow: hidden;
  flex-shrink: 0;
}
.thumb-img {
  width: 100%;
  height: 100%;
  object-fit: cover;
  display: block;
}
.thumb-placeholder {
  width: 100%;
  height: 100%;
  background: var(--surface-2);
  border: 1px solid var(--line-soft);
  border-radius: 8px;
  display: grid;
  place-items: center;
  color: var(--faint);
  font-size: 0.875rem;
}

/* ── Type chip ──────────────────────────────────────────────── */
.type-chip {
  display: inline-block;
  font-size: 0.65rem;
  font-weight: 700;
  padding: 2px 7px;
  border-radius: 4px;
  letter-spacing: 0.03em;
  text-transform: uppercase;
  white-space: nowrap;
}
.badge-type-order      { background: rgba(138,128,115,.18); color: var(--muted);  border: 1px solid rgba(138,128,115,.3); }
.badge-type-ai         { background: rgba(34,200,230,.12);  color: #7ddce8;       border: 1px solid rgba(34,200,230,.28); }
.badge-type-designer   { background: rgba(130,100,220,.15); color: #c9a8f5;       border: 1px solid rgba(130,100,220,.3); }
.badge-type-creditpack { background: rgba(231,185,78,.15);  color: #e7d08a;       border: 1px solid rgba(231,185,78,.35); }

/* ── Status cell ────────────────────────────────────────────── */
.status-cell {
  display: flex;
  align-items: center;
  gap: 8px;
  flex-wrap: wrap;
}

/* ── Approve link ───────────────────────────────────────────── */
.approve-link {
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
.approve-link:hover { color: #4ec984; }

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
  padding: 10px 14px;
  font-size: 0.75rem;
  font-weight: 700;
  color: var(--muted);
  text-transform: uppercase;
  letter-spacing: 0.04em;
}
.th--right { text-align: right; }
.th--thumb { width: 60px; }

.order-row {
  border-bottom: 1px solid var(--line-soft);
  cursor: pointer;
  transition: background 0.12s;
}
.order-row:last-child { border-bottom: none; }
.order-row:hover { background: var(--surface-2); }

.td { padding: 12px 14px; color: var(--text); vertical-align: middle; }
.td--id { font-weight: 700; color: var(--accent); }
.td--muted { color: var(--muted); }
.td--right { text-align: right; }
.td--bold { font-weight: 700; }
.td--thumb { padding: 8px 10px 8px 14px; }

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
  gap: 12px;
  padding: 0.875rem 1rem;
  border-bottom: 1px solid var(--line-soft);
  cursor: pointer;
  transition: background 0.12s;
}
.mobile-row:last-child { border-bottom: none; }
.mobile-row:hover { background: var(--surface-2); }

.mobile-thumb {
  width: 48px;
  height: 48px;
  border-radius: 8px;
  overflow: hidden;
  flex-shrink: 0;
}
.mobile-thumb__img {
  width: 100%;
  height: 100%;
  object-fit: cover;
  display: block;
}
.mobile-thumb__placeholder {
  width: 100%;
  height: 100%;
  background: var(--surface-2);
  border: 1px solid var(--line-soft);
  display: grid;
  place-items: center;
  color: var(--faint);
  font-size: 0.9rem;
}

.mobile-row__body { flex: 1; min-width: 0; display: flex; flex-direction: column; gap: 4px; }
.mobile-row__top {
  display: flex;
  align-items: center;
  gap: 6px;
}
.mobile-row__id { font-size: 0.875rem; font-weight: 700; color: var(--accent); }
.mobile-row__sub { font-size: 0.75rem; color: var(--faint); }
.mobile-row__badges { display: flex; align-items: center; gap: 6px; flex-wrap: wrap; }
.mobile-row__price { font-size: 0.9375rem; font-weight: 700; color: var(--text); flex-shrink: 0; }

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
/* Order statuses */
.badge-draft     { background: rgba(138,128,115,.18); color: var(--muted);     border: 1px solid rgba(138,128,115,.3); }
.badge-pending   { background: rgba(231,185,78,.15);  color: #e7d08a;          border: 1px solid rgba(231,185,78,.3); }
.badge-paid      { background: rgba(255,106,61,.13);  color: var(--accent-2);  border: 1px solid rgba(255,106,61,.3); }
.badge-ready     { background: rgba(160,110,220,.15); color: #c9a8f5;          border: 1px solid rgba(160,110,220,.3); }
.badge-shipped   { background: rgba(60,180,100,.13);  color: #7de0a8;          border: 1px solid rgba(60,180,100,.28); }
.badge-cancelled { background: rgba(220,60,60,.12);   color: #f4a57a;          border: 1px solid rgba(220,60,60,.28); }
/* Design request statuses */
.badge-inprogress { background: rgba(255,106,61,.13);  color: var(--accent-2); border: 1px solid rgba(255,106,61,.3); }
.badge-awaiting   { background: rgba(160,110,220,.15); color: #c9a8f5;         border: 1px solid rgba(160,110,220,.3); }
.badge-approved   { background: rgba(60,180,100,.13);  color: #7de0a8;         border: 1px solid rgba(60,180,100,.28); }
.badge-revision   { background: rgba(255,140,60,.15);  color: #ffb07a;         border: 1px solid rgba(255,140,60,.3); }
.badge-revised    { background: rgba(34,200,230,.12);  color: #7ddce8;         border: 1px solid rgba(34,200,230,.28); }
</style>

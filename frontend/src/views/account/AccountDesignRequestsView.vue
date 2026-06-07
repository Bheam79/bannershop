<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { useRouter, RouterLink } from 'vue-router'
import { listDesignRequests, fetchTemplates } from '@/api/designRequests'
import type { DesignRequestListItem, BannerTemplateItem } from '@/api/designRequests'

const router = useRouter()

const requests = ref<DesignRequestListItem[]>([])
const templates = ref<BannerTemplateItem[]>([])
const loading = ref(true)
const error = ref<string | null>(null)

onMounted(async () => {
  loading.value = true
  error.value = null
  try {
    const [reqs, tpls] = await Promise.all([listDesignRequests(), fetchTemplates()])
    requests.value = reqs
    templates.value = tpls
  } catch {
    error.value = 'Kunne ikke laste design-bestillinger. Prøv igjen.'
  } finally {
    loading.value = false
  }
})

function templateName(id: number): string {
  return templates.value.find(t => t.id === id)?.nameNb ?? `Mal #${id}`
}

function modeLabel(m: string): string {
  return m === 'Manual' ? 'Manuell' : 'AI'
}

function formatDate(iso: string): string {
  return new Date(iso).toLocaleDateString('nb-NO', {
    day: '2-digit', month: 'short', year: 'numeric',
  })
}

function formatNok(n: number): string {
  return new Intl.NumberFormat('nb-NO', { maximumFractionDigits: 0 }).format(n) + ' kr'
}

const STATUS_LABELS: Record<string, string> = {
  Pending:           'Venter',
  InProgress:        'Under arbeid',
  AwaitingApproval:  'Klar til godkjenning',
  Approved:          'Godkjent',
  RevisionRequested: 'Revisjon bedt',
  Revised:           'Revidert',
  Final:             'Levert',
  Failed:            'Feilet',
  Cancelled:         'Kansellert',
}
const STATUS_CLASSES: Record<string, string> = {
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
function statusLabel(s: string) { return STATUS_LABELS[s] ?? s }
function statusClass(s: string) { return STATUS_CLASSES[s] ?? 'badge-draft' }
</script>

<template>
  <div class="dr-wrap">
    <div class="page-header">
      <div>
        <h1 class="display page-title">
          <i class="fa-solid fa-paintbrush"></i>
          Mine design-bestillinger
        </h1>
        <p v-if="!loading && requests.length > 0" class="page-sub">
          {{ requests.length }} bestilling{{ requests.length !== 1 ? 'er' : '' }} totalt
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
      {{ error }}
    </div>

    <!-- Empty -->
    <div v-else-if="requests.length === 0" class="empty-state">
      <i class="fa-solid fa-paintbrush empty-icon"></i>
      <p class="empty-title">Ingen design-bestillinger ennå</p>
      <p class="empty-sub">Bestill et AI-generert eller manuelt designet banner.</p>
      <div class="empty-actions">
        <RouterLink to="/banner-builder/ai" class="btn btn-primary">
          AI-banner (95 kr)
        </RouterLink>
        <RouterLink to="/banner-builder/manual" class="btn btn-ghost">
          Manuelt design (495 kr)
        </RouterLink>
      </div>
    </div>

    <!-- Table -->
    <template v-else>
      <div class="dr-panel">
        <!-- Desktop table -->
        <table class="dr-table">
          <thead class="table-head">
            <tr>
              <th class="th">ID</th>
              <th class="th">Mal</th>
              <th class="th">Modus</th>
              <th class="th">Status</th>
              <th class="th">Dato</th>
              <th class="th th--right">Pris</th>
            </tr>
          </thead>
          <tbody>
            <tr
              v-for="req in requests"
              :key="req.id"
              class="dr-row"
              @click="router.push(`/account/design-requests/${req.id}`)"
            >
              <td class="td td--id">#{{ req.id }}</td>
              <td class="td td--muted">{{ templateName(req.bannerTemplateId) }}</td>
              <td class="td">
                <span class="badge" :class="req.mode === 'Manual' ? 'badge-manual' : 'badge-ai'">
                  {{ modeLabel(req.mode) }}
                </span>
              </td>
              <td class="td">
                <span class="badge" :class="statusClass(req.status)">
                  {{ statusLabel(req.status) }}
                </span>
              </td>
              <td class="td td--muted">{{ formatDate(req.createdAt) }}</td>
              <td class="td td--right td--bold">{{ formatNok(req.priceNok) }}</td>
            </tr>
          </tbody>
        </table>

        <!-- Mobile card list -->
        <ul class="mobile-list">
          <li
            v-for="req in requests"
            :key="req.id"
            class="mobile-row"
            @click="router.push(`/account/design-requests/${req.id}`)"
          >
            <div class="mobile-row__left">
              <div class="mobile-row__id">#{{ req.id }}</div>
              <div class="mobile-row__template">{{ templateName(req.bannerTemplateId) }}</div>
              <div class="mobile-row__badges">
                <span class="badge" :class="req.mode === 'Manual' ? 'badge-manual' : 'badge-ai'">
                  {{ modeLabel(req.mode) }}
                </span>
                <span class="badge" :class="statusClass(req.status)">
                  {{ statusLabel(req.status) }}
                </span>
              </div>
            </div>
            <div class="mobile-row__right">
              <div class="mobile-row__price">{{ formatNok(req.priceNok) }}</div>
              <div class="mobile-row__date">{{ formatDate(req.createdAt) }}</div>
            </div>
          </li>
        </ul>
      </div>
    </template>
  </div>
</template>

<style scoped>
/* ── Layout ─────────────────────────────────────────────────── */
.dr-wrap {
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
.loading-state { display: flex; justify-content: center; padding: 4rem 0; }
.loading-spinner { font-size: 2rem; color: var(--accent); }

/* ── Alerts ─────────────────────────────────────────────────── */
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
.empty-actions { display: flex; gap: 0.75rem; margin-top: 1rem; flex-wrap: wrap; justify-content: center; }

/* ── Table panel ────────────────────────────────────────────── */
.dr-panel {
  background: var(--surface);
  border: 1px solid var(--line-soft);
  border-radius: var(--radius);
  overflow: hidden;
}

.dr-table {
  width: 100%;
  font-size: 0.875rem;
  border-collapse: collapse;
  display: none;
}
@media (min-width: 640px) { .dr-table { display: table; } }

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

.dr-row {
  border-bottom: 1px solid var(--line-soft);
  cursor: pointer;
  transition: background 0.12s;
}
.dr-row:last-child { border-bottom: none; }
.dr-row:hover { background: var(--surface-2); }

.td { padding: 13px 18px; color: var(--text); vertical-align: middle; }
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
.mobile-row__template { font-size: 0.8125rem; color: var(--muted); }
.mobile-row__badges { display: flex; gap: 5px; flex-wrap: wrap; }
.mobile-row__right { text-align: right; flex-shrink: 0; margin-left: 1rem; }
.mobile-row__price { font-size: 0.9375rem; font-weight: 700; color: var(--text); }
.mobile-row__date { font-size: 0.75rem; color: var(--faint); margin-top: 2px; }

/* ── Badges ─────────────────────────────────────────────────── */
.badge {
  display: inline-block;
  font-size: 0.72rem;
  font-weight: 700;
  padding: 3px 9px;
  border-radius: 99px;
  letter-spacing: 0.01em;
  white-space: nowrap;
}

/* Mode badges */
.badge-manual  { background: rgba(130,100,220,.15); color: #c9a8f5; border: 1px solid rgba(130,100,220,.3); }
.badge-ai      { background: rgba(34,200,230,.12);  color: #7ddce8; border: 1px solid rgba(34,200,230,.28); }

/* Status badges */
.badge-draft     { background: rgba(138,128,115,.18); color: var(--muted);     border: 1px solid rgba(138,128,115,.3); }
.badge-pending   { background: rgba(231,185,78,.15);  color: #e7d08a;          border: 1px solid rgba(231,185,78,.3); }
.badge-inprogress{ background: rgba(255,106,61,.13);  color: var(--accent-2);  border: 1px solid rgba(255,106,61,.3); }
.badge-awaiting  { background: rgba(160,110,220,.15); color: #c9a8f5;          border: 1px solid rgba(160,110,220,.3); }
.badge-approved  { background: rgba(60,180,100,.13);  color: #7de0a8;          border: 1px solid rgba(60,180,100,.28); }
.badge-revision  { background: rgba(255,140,60,.15);  color: #ffb07a;          border: 1px solid rgba(255,140,60,.3); }
.badge-revised   { background: rgba(34,200,230,.12);  color: #7ddce8;          border: 1px solid rgba(34,200,230,.28); }
.badge-cancelled { background: rgba(220,60,60,.12);   color: #f4a57a;          border: 1px solid rgba(220,60,60,.28); }
</style>

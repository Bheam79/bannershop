<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useRouter, RouterLink } from 'vue-router'
import { listDesignRequests, type DesignRequestListItem } from '@/api/designRequests'

const router = useRouter()

const items = ref<DesignRequestListItem[]>([])
const loading = ref(true)
const error = ref<string | null>(null)

const sortedItems = computed(() =>
  [...items.value].sort(
    (a, b) => new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime(),
  ),
)

const STATUS_LABELS: Record<string, string> = {
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

function statusLabel(s: string): string {
  return STATUS_LABELS[s] ?? s
}
function statusClass(s: string): string {
  return STATUS_CLASSES[s] ?? 'badge-draft'
}

function modeLabel(m: string): string {
  return m === 'Manual' ? 'Designer' : 'AI'
}
function modeClass(m: string): string {
  return m === 'Manual' ? 'badge-manual' : 'badge-ai'
}

function formatDate(iso: string): string {
  return new Date(iso).toLocaleDateString('nb-NO', {
    day: '2-digit', month: 'short', year: 'numeric',
  })
}

function goToDetail(id: number) {
  router.push(`/account/design-requests/${id}`)
}

async function load() {
  loading.value = true
  error.value = null
  try {
    items.value = await listDesignRequests()
  } catch {
    error.value = 'Kunne ikke laste design-bestillinger. Prøv igjen.'
  } finally {
    loading.value = false
  }
}

onMounted(load)
</script>

<template>
  <div class="dr-wrap">
    <div class="page-header">
      <div>
        <h1 class="display page-title">
          <i class="fa-solid fa-wand-magic-sparkles"></i>
          Mine design
        </h1>
        <p v-if="!loading && sortedItems.length > 0" class="page-sub">
          {{ sortedItems.length }} bestilling{{ sortedItems.length !== 1 ? 'er' : '' }} totalt
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
    <div v-else-if="sortedItems.length === 0" class="empty-state">
      <i class="fa-solid fa-wand-magic-sparkles empty-icon"></i>
      <p class="empty-title">Ingen design-bestillinger ennå</p>
      <p class="empty-sub">Bruk AI-banneren eller bestill en designer for å komme i gang.</p>
      <RouterLink to="/ai-banner" class="btn btn-primary empty-action">
        Start AI-banner
      </RouterLink>
    </div>

    <!-- List -->
    <template v-else>
      <div class="dr-panel">
        <!-- Desktop table -->
        <table class="dr-table">
          <thead class="table-head">
            <tr>
              <th class="th th--thumb"></th>
              <th class="th">ID</th>
              <th class="th">Modus</th>
              <th class="th">Status</th>
              <th class="th">Format</th>
              <th class="th">Dato</th>
            </tr>
          </thead>
          <tbody>
            <tr
              v-for="dr in sortedItems"
              :key="dr.id"
              class="dr-row"
              @click="goToDetail(dr.id)"
            >
              <td class="td td--thumb">
                <div class="thumb-cell">
                  <img
                    v-if="dr.previewUrl"
                    :src="dr.previewUrl"
                    alt=""
                    class="thumb-img"
                  />
                  <div v-else class="thumb-placeholder">
                    <i class="fa-solid fa-image"></i>
                  </div>
                </div>
              </td>
              <td class="td td--id">#{{ dr.id }}</td>
              <td class="td">
                <span class="badge" :class="modeClass(dr.mode)">
                  {{ modeLabel(dr.mode) }}
                </span>
              </td>
              <td class="td">
                <span class="badge" :class="statusClass(dr.status)">
                  {{ statusLabel(dr.status) }}
                </span>
              </td>
              <td class="td td--muted">{{ dr.aspectRatio }}</td>
              <td class="td td--muted">{{ formatDate(dr.createdAt) }}</td>
            </tr>
          </tbody>
        </table>

        <!-- Mobile list -->
        <ul class="mobile-list">
          <li
            v-for="dr in sortedItems"
            :key="dr.id"
            class="mobile-row"
            @click="goToDetail(dr.id)"
          >
            <div class="mobile-thumb">
              <img
                v-if="dr.previewUrl"
                :src="dr.previewUrl"
                alt=""
                class="mobile-thumb__img"
              />
              <div v-else class="mobile-thumb__placeholder">
                <i class="fa-solid fa-image"></i>
              </div>
            </div>

            <div class="mobile-row__body">
              <div class="mobile-row__top">
                <span class="badge" :class="modeClass(dr.mode)">{{ modeLabel(dr.mode) }}</span>
                <span class="mobile-row__id">#{{ dr.id }}</span>
              </div>
              <div class="mobile-row__sub">
                {{ formatDate(dr.createdAt) }} · {{ dr.aspectRatio }}
              </div>
              <div class="mobile-row__badges">
                <span class="badge" :class="statusClass(dr.status)">
                  {{ statusLabel(dr.status) }}
                </span>
              </div>
            </div>
          </li>
        </ul>
      </div>
    </template>
  </div>
</template>

<style scoped>
.dr-wrap {
  max-width: 1000px;
  margin: 0 auto;
  padding: 2.5rem 1.25rem 3rem;
}

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

.loading-state {
  display: flex;
  justify-content: center;
  padding: 4rem 0;
}
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

.dr-panel {
  background: var(--surface);
  border: 1px solid var(--line-soft);
  border-radius: var(--radius);
  overflow: hidden;
}

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
  padding: 10px 14px;
  font-size: 0.75rem;
  font-weight: 700;
  color: var(--muted);
  text-transform: uppercase;
  letter-spacing: 0.04em;
}
.th--thumb { width: 60px; }

.dr-row {
  border-bottom: 1px solid var(--line-soft);
  cursor: pointer;
  transition: background 0.12s;
}
.dr-row:last-child { border-bottom: none; }
.dr-row:hover { background: var(--surface-2); }

.td { padding: 12px 14px; color: var(--text); vertical-align: middle; }
.td--id { font-weight: 700; color: var(--accent); }
.td--muted { color: var(--muted); }
.td--thumb { padding: 8px 10px 8px 14px; }

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
.mobile-row__top { display: flex; align-items: center; gap: 6px; }
.mobile-row__id { font-size: 0.875rem; font-weight: 700; color: var(--accent); }
.mobile-row__sub { font-size: 0.75rem; color: var(--faint); }
.mobile-row__badges { display: flex; align-items: center; gap: 6px; flex-wrap: wrap; }

.badge {
  display: inline-block;
  font-size: 0.72rem;
  font-weight: 700;
  padding: 3px 9px;
  border-radius: 99px;
  letter-spacing: 0.01em;
  white-space: nowrap;
}
.badge-draft      { background: rgba(138,128,115,.18); color: var(--muted);     border: 1px solid rgba(138,128,115,.3); }
.badge-pending    { background: rgba(231,185,78,.15);  color: #e7d08a;          border: 1px solid rgba(231,185,78,.3); }
.badge-inprogress { background: rgba(255,106,61,.13);  color: var(--accent-2);  border: 1px solid rgba(255,106,61,.3); }
.badge-awaiting   { background: rgba(160,110,220,.15); color: #c9a8f5;          border: 1px solid rgba(160,110,220,.3); }
.badge-approved   { background: rgba(60,180,100,.13);  color: #7de0a8;          border: 1px solid rgba(60,180,100,.28); }
.badge-revision   { background: rgba(255,140,60,.15);  color: #ffb07a;          border: 1px solid rgba(255,140,60,.3); }
.badge-revised    { background: rgba(34,200,230,.12);  color: #7ddce8;          border: 1px solid rgba(34,200,230,.28); }
.badge-cancelled  { background: rgba(220,60,60,.12);   color: #f4a57a;          border: 1px solid rgba(220,60,60,.28); }
.badge-manual     { background: rgba(130,100,220,.15); color: #c9a8f5;          border: 1px solid rgba(130,100,220,.3); }
.badge-ai         { background: rgba(34,200,230,.12);  color: #7ddce8;          border: 1px solid rgba(34,200,230,.28); }
</style>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useRouter, RouterLink } from 'vue-router'
import { listDesignRequests, type DesignRequestListItem } from '@/api/designRequests'
import { listMyUploads, type UploadedDesignListItem } from '@/api/bannerBuilder'
import { useAuthStore } from '@/stores/auth'

const router = useRouter()
const auth = useAuthStore()

// ── Data ─────────────────────────────────────────────────────────────────────
const designRequests = ref<DesignRequestListItem[]>([])
const uploadedDesigns = ref<UploadedDesignListItem[]>([])
const loading = ref(true)
const error = ref<string | null>(null)

// ── Computed ──────────────────────────────────────────────────────────────────
const sortedRequests = computed(() =>
  [...designRequests.value].sort(
    (a, b) => new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime(),
  ),
)

const sortedUploads = computed(() =>
  [...uploadedDesigns.value].sort(
    (a, b) => new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime(),
  ),
)

const hasAny = computed(() => sortedRequests.value.length > 0 || sortedUploads.value.length > 0)

// ── Status helpers ────────────────────────────────────────────────────────────
const STATUS_LABELS: Record<string, string> = {
  Pending:           'Genererer',
  InProgress:        'Under arbeid',
  AwaitingApproval:  'Klar til godkjenning',
  Approved:          'Godkjent',
  RevisionRequested: 'Revisjon',
  Revised:           'Revidert',
  Final:             'Bestilt',
  Failed:            'Feilet',
  Cancelled:         'Kansellert',
}

function statusLabel(s: string): string {
  return STATUS_LABELS[s] ?? s
}

function statusDot(s: string): string {
  switch (s) {
    case 'AwaitingApproval': return '#e7b94e'
    case 'Approved':         return '#4ade80'
    case 'Final':            return '#4ade80'
    case 'InProgress':
    case 'Pending':          return 'var(--accent)'
    case 'Failed':
    case 'Cancelled':        return '#e05252'
    default:                 return 'var(--faint)'
  }
}

function modeLabel(m: string): string {
  return m === 'Manual' ? 'Designer' : 'AI'
}

function formatDate(iso: string): string {
  return new Date(iso).toLocaleDateString('nb-NO', {
    day: '2-digit', month: 'short', year: 'numeric',
  })
}

// ── Navigation ────────────────────────────────────────────────────────────────
function openDesignRequest(dr: DesignRequestListItem) {
  // All design requests — AI and Manual — open in the AI builder with inputs pre-filled.
  // For AI, this lets the user generate a new version; for Manual, start an AI variant.
  void router.push(`/banner-builder/ai?copyFrom=${dr.id}`)
}

function openUploadedDesign(d: UploadedDesignListItem) {
  void router.push(`/banner-builder/upload?designId=${d.id}`)
}

// ── Load ──────────────────────────────────────────────────────────────────────
async function load() {
  loading.value = true
  error.value = null
  try {
    const [reqs, uploads] = await Promise.all([
      listDesignRequests(),
      listMyUploads(),
    ])
    designRequests.value = reqs
    uploadedDesigns.value = uploads
  } catch {
    error.value = 'Kunne ikke laste designene dine. Prøv igjen.'
  } finally {
    loading.value = false
  }
}

onMounted(load)
</script>

<template>
  <div class="wrap">

    <!-- Page header -->
    <div class="page-header">
      <div>
        <h1 class="display page-title">
          <i class="fa-solid fa-palette"></i>
          Mine design
        </h1>
        <p v-if="!loading && hasAny" class="page-sub">
          {{ sortedRequests.length + sortedUploads.length }} design totalt
        </p>
      </div>
      <div class="header-actions">
        <RouterLink to="/banner-builder" class="btn btn-primary header-cta">
          <i class="fa-solid fa-plus"></i>
          Nytt design
        </RouterLink>
      </div>
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

    <!-- Empty state -->
    <div v-else-if="!hasAny" class="empty-state">
      <i class="fa-solid fa-palette empty-icon"></i>
      <p class="empty-title">Ingen design ennå</p>
      <p class="empty-sub">Generer et AI-banner, last opp din egen fil, eller bestill et profesjonelt design.</p>
      <RouterLink to="/banner-builder" class="btn btn-primary empty-action">
        <i class="fa-solid fa-plus"></i>
        Lag ditt første banner
      </RouterLink>
    </div>

    <!-- Content -->
    <template v-else>

      <!-- ── AI + Manual design requests section ──────────────────────── -->
      <section v-if="sortedRequests.length > 0" class="section">
        <div class="section-header">
          <h2 class="section-title">
            <i class="fa-solid fa-wand-magic-sparkles" style="color:var(--accent)"></i>
            Genererte og bestilte design
          </h2>
          <RouterLink to="/account/design-requests" class="section-link">
            Se som liste
            <i class="fa-solid fa-arrow-right" style="font-size:11px"></i>
          </RouterLink>
        </div>

        <div class="design-grid">
          <button
            v-for="dr in sortedRequests"
            :key="`dr-${dr.id}`"
            type="button"
            class="design-card"
            @click="openDesignRequest(dr)"
          >
            <!-- Preview image -->
            <div class="card-thumb">
              <img
                v-if="dr.previewUrl"
                :src="dr.previewUrl"
                :alt="dr.personName ? `Banner for ${dr.personName}` : 'Bannerbilde'"
                class="card-img"
              />
              <div v-else class="card-placeholder">
                <i
                  class="fa-solid"
                  :class="dr.mode === 'Ai' ? 'fa-wand-magic-sparkles' : 'fa-palette'"
                  style="font-size:24px;color:var(--faint)"
                ></i>
              </div>
              <!-- Status dot -->
              <div class="card-status-dot" :style="{ background: statusDot(dr.status) }"></div>
            </div>

            <!-- Card body -->
            <div class="card-body">
              <div class="card-name">
                {{ dr.personName || (dr.mode === 'Ai' ? 'AI-design' : 'Designerbestilling') }}
              </div>
              <div v-if="dr.themeDescription" class="card-theme">{{ dr.themeDescription }}</div>
              <div class="card-meta">
                <span class="chip" :class="dr.mode === 'Ai' ? 'chip-ai' : 'chip-manual'">
                  {{ modeLabel(dr.mode) }}
                </span>
                <span class="card-date">{{ formatDate(dr.createdAt) }}</span>
              </div>
              <div class="card-status-label" :style="{ color: statusDot(dr.status) }">
                {{ statusLabel(dr.status) }}
              </div>
            </div>

            <!-- Hover CTA -->
            <div class="card-hover-cta">
              <span>
                <i class="fa-solid fa-wand-magic-sparkles" style="font-size:13px"></i>
                Rediger og generer
              </span>
            </div>
          </button>
        </div>
      </section>

      <!-- ── Uploaded designs section ──────────────────────────────────── -->
      <section v-if="sortedUploads.length > 0" class="section">
        <div class="section-header">
          <h2 class="section-title">
            <i class="fa-solid fa-folder-open" style="color:var(--accent)"></i>
            Opplastede filer
          </h2>
        </div>

        <div class="design-grid">
          <button
            v-for="d in sortedUploads"
            :key="`up-${d.id}`"
            type="button"
            class="design-card"
            @click="openUploadedDesign(d)"
          >
            <!-- Preview image -->
            <div class="card-thumb">
              <img
                v-if="d.previewUrl"
                :src="d.previewUrl"
                :alt="d.originalFileName"
                class="card-img"
              />
              <div v-else class="card-placeholder">
                <i class="fa-solid fa-image" style="font-size:24px;color:var(--faint)"></i>
              </div>
            </div>

            <!-- Card body -->
            <div class="card-body">
              <div class="card-name">{{ d.originalFileName }}</div>
              <div class="card-theme">{{ d.computedWidthCm }} × {{ d.selectedHeightCm }} cm</div>
              <div class="card-meta">
                <span class="chip chip-upload">Opplastet</span>
                <span class="card-date">{{ formatDate(d.createdAt) }}</span>
              </div>
            </div>

            <!-- Hover CTA -->
            <div class="card-hover-cta">
              <span>
                <i class="fa-solid fa-cart-shopping" style="font-size:13px"></i>
                Åpne og bestill
              </span>
            </div>
          </button>
        </div>
      </section>

    </template>
  </div>
</template>

<style scoped>
.wrap {
  max-width: 1200px;
  margin: 0 auto;
  padding: 2.5rem 1.5rem 4rem;
}

/* ── Page header ──────────────────────────────────────────────── */
.page-header {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 1rem;
  margin-bottom: 2rem;
}
.page-title {
  font-size: clamp(1.5rem, 3.5vw, 2rem);
  color: var(--text);
  display: flex;
  align-items: center;
  gap: 0.5rem;
}
.page-title i { color: var(--accent); }
.page-sub { font-size: 0.8125rem; color: var(--muted); margin-top: 4px; }

.header-actions { display: flex; align-items: flex-start; gap: 10px; padding-top: 4px; }
.header-cta { font-size: 14px; padding: 9px 16px; white-space: nowrap; }

/* ── Loading / error / empty ──────────────────────────────────── */
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
  padding: 5rem 1rem;
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 0.5rem;
}
.empty-icon { font-size: 3rem; color: var(--faint); margin-bottom: 0.5rem; }
.empty-title { font-size: 1.25rem; font-weight: 700; color: var(--text); }
.empty-sub { font-size: 0.9rem; color: var(--muted); max-width: 28em; text-align: center; }
.empty-action { margin-top: 1.25rem; }

/* ── Section ──────────────────────────────────────────────────── */
.section { margin-bottom: 2.5rem; }

.section-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 1rem;
  margin-bottom: 1.25rem;
}
.section-title {
  font-size: 1rem;
  font-weight: 700;
  color: var(--text);
  font-family: var(--font-display);
  display: flex;
  align-items: center;
  gap: 0.5rem;
}
.section-link {
  font-size: 0.8125rem;
  font-weight: 600;
  color: var(--muted);
  text-decoration: none;
  display: inline-flex;
  align-items: center;
  gap: 5px;
  transition: color 0.15s;
}
.section-link:hover { color: var(--accent); }

/* ── Design grid ──────────────────────────────────────────────── */
.design-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(200px, 1fr));
  gap: 16px;
}
@media (max-width: 480px) {
  .design-grid { grid-template-columns: repeat(2, 1fr); gap: 12px; }
}

/* ── Design card ──────────────────────────────────────────────── */
.design-card {
  position: relative;
  background: var(--surface);
  border: 1px solid var(--line-soft);
  border-radius: 14px;
  overflow: hidden;
  cursor: pointer;
  text-align: left;
  padding: 0;
  transition: border-color 0.18s, transform 0.18s, box-shadow 0.18s;
  display: flex;
  flex-direction: column;
}
.design-card:hover {
  border-color: var(--accent);
  transform: translateY(-2px);
  box-shadow: 0 8px 24px rgba(255,106,61,.15);
}
.design-card:hover .card-hover-cta { opacity: 1; }

/* Thumb */
.card-thumb {
  position: relative;
  width: 100%;
  aspect-ratio: 16 / 9;
  background: var(--surface-2);
  overflow: hidden;
}
.card-img {
  width: 100%;
  height: 100%;
  object-fit: cover;
  display: block;
}
.card-placeholder {
  width: 100%;
  height: 100%;
  display: grid;
  place-items: center;
  background: var(--surface-2);
}

/* Status dot (top-right of thumb) */
.card-status-dot {
  position: absolute;
  top: 8px;
  right: 8px;
  width: 10px;
  height: 10px;
  border-radius: 50%;
  border: 2px solid var(--surface);
}

/* Card body */
.card-body {
  padding: 12px 14px 14px;
  display: flex;
  flex-direction: column;
  gap: 5px;
  flex: 1;
}
.card-name {
  font-size: 0.875rem;
  font-weight: 700;
  color: var(--text);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
.card-theme {
  font-size: 0.75rem;
  color: var(--muted);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
.card-meta {
  display: flex;
  align-items: center;
  gap: 7px;
  flex-wrap: wrap;
  margin-top: 2px;
}
.card-date {
  font-size: 0.72rem;
  color: var(--faint);
}
.card-status-label {
  font-size: 0.72rem;
  font-weight: 700;
  margin-top: 2px;
}

/* Chips */
.chip {
  display: inline-block;
  font-size: 0.68rem;
  font-weight: 700;
  padding: 2px 8px;
  border-radius: 99px;
  white-space: nowrap;
}
.chip-ai {
  background: rgba(34,200,230,.12);
  color: #7ddce8;
  border: 1px solid rgba(34,200,230,.28);
}
.chip-manual {
  background: rgba(130,100,220,.15);
  color: #c9a8f5;
  border: 1px solid rgba(130,100,220,.3);
}
.chip-upload {
  background: rgba(138,128,115,.18);
  color: var(--muted);
  border: 1px solid rgba(138,128,115,.3);
}

/* Hover CTA overlay */
.card-hover-cta {
  position: absolute;
  inset: 0;
  background: rgba(255,106,61,.08);
  border: 2px solid rgba(255,106,61,.4);
  border-radius: 14px;
  display: flex;
  align-items: flex-end;
  justify-content: center;
  padding-bottom: 12px;
  opacity: 0;
  pointer-events: none;
  transition: opacity 0.18s;
}
.card-hover-cta span {
  background: var(--accent);
  color: white;
  font-size: 0.8rem;
  font-weight: 700;
  padding: 6px 14px;
  border-radius: 99px;
  display: flex;
  align-items: center;
  gap: 6px;
}
</style>

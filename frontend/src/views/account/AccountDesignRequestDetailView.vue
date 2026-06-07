<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useRoute, RouterLink } from 'vue-router'
import {
  getDesignRequest,
  approveDesignRequest,
  requestRevision,
  fetchTemplates,
  type DesignRequestDetail,
  type BannerTemplateItem,
} from '@/api/designRequests'

const route = useRoute()
const requestId = Number(route.params.id)

// ── Data ──────────────────────────────────────────────────────────────────────
const request = ref<DesignRequestDetail | null>(null)
const templates = ref<BannerTemplateItem[]>([])
const loading = ref(true)
const loadError = ref<string | null>(null)

async function loadRequest() {
  loading.value = true
  loadError.value = null
  try {
    const [dr, tpls] = await Promise.all([getDesignRequest(requestId), fetchTemplates()])
    request.value = dr
    templates.value = tpls
  } catch {
    loadError.value = 'Kunne ikke laste design-bestillingen.'
  } finally {
    loading.value = false
  }
}

onMounted(loadRequest)

// ── Approve ───────────────────────────────────────────────────────────────────
const approving = ref(false)
const approveError = ref('')
const approveSuccess = ref('')

async function approve() {
  if (approving.value) return
  approving.value = true
  approveError.value = ''
  approveSuccess.value = ''
  try {
    await approveDesignRequest(requestId)
    request.value = await getDesignRequest(requestId)
    approveSuccess.value = 'Designet er godkjent! Banneret sendes til produksjon.'
  } catch (e: unknown) {
    const ex = e as { response?: { data?: { error?: string } }; message?: string }
    approveError.value = ex.response?.data?.error || ex.message || 'Godkjenning feilet.'
  } finally {
    approving.value = false
  }
}

// ── Request revision ──────────────────────────────────────────────────────────
const showRevisionForm = ref(false)
const revisionComment = ref('')
const revisionSaving = ref(false)
const revisionError = ref('')
const revisionSuccess = ref('')

async function submitRevision() {
  if (!revisionComment.value.trim() || revisionSaving.value) return
  revisionSaving.value = true
  revisionError.value = ''
  revisionSuccess.value = ''
  try {
    request.value = await requestRevision(requestId, revisionComment.value.trim())
    revisionSuccess.value = 'Korrigeringsønske er sendt. Vi kommer tilbake til deg.'
    revisionComment.value = ''
    showRevisionForm.value = false
  } catch (e: unknown) {
    const ex = e as { response?: { data?: { error?: string } }; message?: string }
    revisionError.value = ex.response?.data?.error || ex.message || 'Innsending feilet.'
  } finally {
    revisionSaving.value = false
  }
}

// ── Helpers ───────────────────────────────────────────────────────────────────
function templateName(id: number): string {
  return templates.value.find(t => t.id === id)?.nameNb ?? `Mal #${id}`
}

function modeLabel(m: string) { return m === 'Manual' ? 'Manuell' : 'AI' }
function langLabel(l: string) { return l === 'nb' ? 'Norsk' : l === 'en' ? 'Engelsk' : l }

function formatNok(n: number): string {
  return new Intl.NumberFormat('nb-NO', { maximumFractionDigits: 0 }).format(n) + ' kr'
}

function formatDateTime(iso: string | null | undefined): string {
  if (!iso) return '—'
  return new Date(iso).toLocaleString('nb-NO', {
    day: '2-digit', month: 'short', year: 'numeric',
    hour: '2-digit', minute: '2-digit',
  })
}

const STATUS_LABELS: Record<string, string> = {
  Pending:           'Venter',
  InProgress:        'Under arbeid',
  AwaitingApproval:  'Klar til godkjenning',
  Approved:          'Godkjent',
  RevisionRequested: 'Korrigering under behandling',
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

const isManual = computed(() => request.value?.mode === 'Manual')
const canRequestRevision = computed(
  () =>
    request.value?.mode === 'Manual' &&
    request.value.status === 'AwaitingApproval' &&
    request.value.revisionCount < 1,
)
</script>

<template>
  <div class="detail-wrap">
    <!-- Breadcrumb -->
    <div class="breadcrumb">
      <RouterLink to="/account/design-requests" class="breadcrumb-link">
        <i class="fa-solid fa-arrow-left"></i>
        Mine design-bestillinger
      </RouterLink>
      <span class="breadcrumb-sep">›</span>
      <span class="breadcrumb-current">Bestilling #{{ requestId }}</span>
    </div>

    <!-- Loading -->
    <div v-if="loading" class="loading-state">
      <i class="fa-solid fa-circle-notch fa-spin loading-spinner"></i>
    </div>
    <div v-else-if="loadError" class="alert-error">
      <i class="fa-solid fa-circle-exclamation"></i>
      {{ loadError }}
    </div>

    <template v-else-if="request">

      <!-- ── Header ─────────────────────────────────────────────────────── -->
      <div class="panel">
        <div class="header-top">
          <div>
            <h1 class="display header-title">Design-bestilling #{{ request.id }}</h1>
            <p class="header-date">Opprettet {{ formatDateTime(request.createdAt) }}</p>
          </div>
          <span class="badge" :class="statusClass(request.status)">
            {{ statusLabel(request.status) }}
          </span>
        </div>

        <div class="meta-grid">
          <div class="meta-cell">
            <div class="meta-label">Mal</div>
            <div class="meta-value">{{ templateName(request.bannerTemplateId) }}</div>
          </div>
          <div class="meta-cell">
            <div class="meta-label">Modus</div>
            <span class="badge" :class="isManual ? 'badge-manual' : 'badge-ai'">
              {{ modeLabel(request.mode) }}
            </span>
          </div>
          <div class="meta-cell">
            <div class="meta-label">Format</div>
            <div class="meta-value">{{ request.aspectRatio }}</div>
          </div>
          <div class="meta-cell">
            <div class="meta-label">Pris</div>
            <div class="meta-value meta-value--accent">{{ formatNok(request.priceNok) }}</div>
          </div>
        </div>
      </div>

      <!-- ── Status-specific sections ────────────────────────────────────── -->

      <!-- PENDING / IN PROGRESS -->
      <div
        v-if="request.status === 'Pending' || request.status === 'InProgress'"
        class="status-block status-block--progress"
      >
        <div class="status-block__spinner">
          <div class="spin-ring spin-ring--track"></div>
          <div class="spin-ring spin-ring--fill"></div>
        </div>
        <h2 class="status-block__title">
          {{ request.status === 'Pending' ? 'Venter på betaling…' : 'Designet er under arbeid' }}
        </h2>
        <p class="status-block__body">
          {{ request.status === 'InProgress'
            ? 'Designeren jobber med banneret ditt. Du mottar en e-post når forhåndsvisningen er klar.'
            : 'Bestillingen er registrert og venter på betalingsbekreftelse.' }}
        </p>
      </div>

      <!-- AWAITING APPROVAL -->
      <div
        v-else-if="request.status === 'AwaitingApproval'"
        class="panel status-block--approval"
      >
        <h2 class="approval-title">
          <i class="fa-solid fa-star"></i>
          Forhåndsvisning klar!
        </h2>
        <p class="approval-sub">
          Designeren har fullført banneret ditt. Se det gjennom og godkjenn eller be om en korrigering.
        </p>

        <!-- Preview image -->
        <div class="preview-wrap">
          <img
            v-if="request.previewUrl"
            :src="request.previewUrl"
            alt="Designforslag"
            class="preview-img"
          />
          <div v-else class="preview-placeholder">
            <i class="fa-solid fa-image"></i>
            Forhåndsvisning ikke tilgjengelig ennå
          </div>
        </div>

        <!-- Action buttons -->
        <div v-if="!approveSuccess" class="approval-actions">
          <button
            type="button"
            class="btn btn-approve"
            :disabled="approving"
            @click="approve"
          >
            <i v-if="approving" class="fa-solid fa-circle-notch fa-spin"></i>
            <i v-else class="fa-solid fa-circle-check"></i>
            Godkjenn design
          </button>

          <button
            v-if="canRequestRevision && !showRevisionForm"
            type="button"
            class="btn btn-revision"
            @click="showRevisionForm = true"
          >
            <i class="fa-solid fa-pen-to-square"></i>
            Be om korrigering
          </button>
        </div>

        <div v-if="approveSuccess" class="alert-success">
          <i class="fa-solid fa-circle-check"></i>
          {{ approveSuccess }}
        </div>
        <div v-if="approveError" class="alert-error">
          <i class="fa-solid fa-circle-exclamation"></i>
          {{ approveError }}
        </div>

        <!-- Revision form -->
        <div v-if="showRevisionForm && !approveSuccess" class="revision-form">
          <h3 class="revision-form__title">
            <i class="fa-solid fa-pen-to-square"></i>
            Beskriv hva du ønsker endret
            <span class="revision-form__free">(du har 1 gratis korrigering)</span>
          </h3>
          <textarea
            v-model="revisionComment"
            rows="3"
            maxlength="2000"
            placeholder="f.eks. Bytt bakgrunnsfargen til blå, gjør teksten større…"
            class="field-textarea"
          />
          <p class="char-count">{{ revisionComment.length }} / 2000 tegn</p>
          <div class="revision-form__actions">
            <button
              type="button"
              :disabled="!revisionComment.trim() || revisionSaving"
              class="btn btn-revision-submit"
              @click="submitRevision"
            >
              <i v-if="revisionSaving" class="fa-solid fa-circle-notch fa-spin"></i>
              {{ revisionSaving ? 'Sender…' : 'Send korrigeringsønske' }}
            </button>
            <button
              type="button"
              class="btn btn-ghost"
              @click="showRevisionForm = false; revisionComment = ''"
            >
              Avbryt
            </button>
          </div>
          <div v-if="revisionError" class="alert-error" style="margin-top:0.5rem">
            <i class="fa-solid fa-circle-exclamation"></i>
            {{ revisionError }}
          </div>
        </div>

        <p
          v-if="isManual && request.revisionCount >= 1 && request.status === 'AwaitingApproval'"
          class="revision-used-note"
        >
          <i class="fa-solid fa-circle-info"></i>
          Du har allerede brukt din gratis korrigering.
        </p>
      </div>

      <!-- REVISION REQUESTED -->
      <div
        v-else-if="request.status === 'RevisionRequested'"
        class="status-block status-block--revision"
      >
        <i class="fa-solid fa-pen-to-square status-block__icon"></i>
        <h2 class="status-block__title">Korrigering er under behandling</h2>
        <p class="status-block__body">
          Vi har mottatt ønsket ditt og jobber med korrigeringen. Du vil motta en e-post
          når det reviderte designet er klart.
        </p>
      </div>

      <!-- REVISED -->
      <div
        v-else-if="request.status === 'Revised'"
        class="status-block status-block--revised"
      >
        <i class="fa-solid fa-rotate status-block__icon"></i>
        <h2 class="status-block__title">Revidert design klart</h2>
        <p class="status-block__body">
          Det reviderte designet er sendt til godkjenning — sjekk e-posten din for varsling.
        </p>
      </div>

      <!-- APPROVED / FINAL -->
      <div
        v-else-if="request.status === 'Approved' || request.status === 'Final'"
        class="panel status-block--approved"
      >
        <div class="approved-header">
          <i class="fa-solid fa-circle-check approved-icon"></i>
          <h2 class="approved-title">
            {{ request.status === 'Final' ? 'Banneret er levert!' : 'Bestillingen er godkjent og sendes i produksjon' }}
          </h2>
        </div>
        <p class="approved-body">
          {{ request.status === 'Final'
            ? 'Banneret ditt er ferdig produsert og levert.'
            : 'Takk! Banneret ditt er nå i produksjon.' }}
        </p>

        <img
          v-if="request.previewUrl"
          :src="request.previewUrl"
          alt="Godkjent design"
          class="approved-preview"
        />
        <div v-if="request.finalCroppedUrl" class="approved-download">
          <a
            :href="request.finalCroppedUrl"
            target="_blank"
            rel="noopener noreferrer"
            class="download-link"
          >
            <i class="fa-solid fa-download"></i>
            Last ned full versjon
          </a>
        </div>
      </div>

      <!-- FAILED -->
      <div v-else-if="request.status === 'Failed'" class="status-block status-block--failed">
        <i class="fa-solid fa-triangle-exclamation status-block__icon"></i>
        <h2 class="status-block__title">Noe gikk galt</h2>
        <p class="status-block__body">
          {{ request.lastError ?? 'Genereringen feilet. Vennligst kontakt support.' }}
        </p>
      </div>

      <!-- CANCELLED -->
      <div v-else-if="request.status === 'Cancelled'" class="status-block status-block--cancelled">
        <i class="fa-solid fa-ban status-block__icon"></i>
        <h2 class="status-block__title">Bestillingen er kansellert</h2>
      </div>

      <!-- Revision success feedback -->
      <div v-if="revisionSuccess" class="alert-success">
        <i class="fa-solid fa-circle-check"></i>
        {{ revisionSuccess }}
      </div>

      <!-- ── Request details (read-only) ─────────────────────────────────── -->
      <div class="panel">
        <h2 class="section-title">
          <i class="fa-solid fa-clipboard-list"></i>
          Bestillingsdetaljer
        </h2>
        <dl class="details-grid">
          <div class="detail-cell">
            <dt class="detail-label">Språk</dt>
            <dd class="detail-value">{{ langLabel(request.language) }}</dd>
          </div>
          <div class="detail-cell">
            <dt class="detail-label">Navn på person</dt>
            <dd class="detail-value">{{ request.personName }}</dd>
          </div>
          <div v-if="request.personAge != null" class="detail-cell">
            <dt class="detail-label">Alder</dt>
            <dd class="detail-value">{{ request.personAge }} år</dd>
          </div>
          <div class="detail-cell">
            <dt class="detail-label">Sideforhold</dt>
            <dd class="detail-value">{{ request.aspectRatio }}</dd>
          </div>
          <div class="detail-cell detail-cell--full">
            <dt class="detail-label">Tekst på banneret</dt>
            <dd class="detail-value detail-textarea">{{ request.textContent }}</dd>
          </div>
          <div class="detail-cell detail-cell--full">
            <dt class="detail-label">Tema / stil</dt>
            <dd class="detail-value detail-textarea">{{ request.themeDescription }}</dd>
          </div>
        </dl>
      </div>

      <RouterLink to="/account/design-requests" class="back-link">
        <i class="fa-solid fa-arrow-left"></i>
        Tilbake til oversikten
      </RouterLink>
    </template>
  </div>
</template>

<style scoped>
/* ── Layout ─────────────────────────────────────────────────── */
.detail-wrap {
  max-width: 780px;
  margin: 0 auto;
  padding: 2.5rem 1.25rem 3rem;
  display: flex;
  flex-direction: column;
  gap: 1.5rem;
}

/* ── Breadcrumb ─────────────────────────────────────────────── */
.breadcrumb { display: flex; align-items: center; gap: 0.5rem; font-size: 0.875rem; }
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

/* ── Loading / errors ───────────────────────────────────────── */
.loading-state { display: flex; justify-content: center; padding: 4rem 0; }
.loading-spinner { font-size: 2rem; color: var(--accent); }

.alert-error {
  display: flex;
  align-items: flex-start;
  gap: 10px;
  padding: 12px 16px;
  background: rgba(220,60,60,.12);
  border: 1px solid rgba(220,60,60,.3);
  border-radius: 12px;
  color: #f4a57a;
  font-size: 0.875rem;
}
.alert-error i { color: #e05252; flex-shrink: 0; margin-top: 2px; }

.alert-success {
  display: flex;
  align-items: flex-start;
  gap: 10px;
  padding: 12px 16px;
  background: rgba(60,180,100,.1);
  border: 1px solid rgba(60,180,100,.25);
  border-radius: 12px;
  color: #7de0a8;
  font-size: 0.875rem;
}
.alert-success i { color: #4ec984; flex-shrink: 0; margin-top: 2px; }

/* ── Header ─────────────────────────────────────────────────── */
.header-top {
  display: flex;
  flex-wrap: wrap;
  align-items: flex-start;
  justify-content: space-between;
  gap: 1rem;
  margin-bottom: 1.25rem;
}
.header-title { font-size: clamp(1.125rem, 2.5vw, 1.375rem); color: var(--text); }
.header-date { font-size: 0.8rem; color: var(--faint); margin-top: 3px; }

/* ── Meta grid ──────────────────────────────────────────────── */
.meta-grid {
  display: grid;
  grid-template-columns: repeat(4, 1fr);
  gap: 1rem;
}
@media (max-width: 640px) { .meta-grid { grid-template-columns: 1fr 1fr; } }
.meta-label {
  font-size: 0.7rem;
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 0.07em;
  color: var(--faint);
  margin-bottom: 4px;
}
.meta-value { font-size: 0.9375rem; font-weight: 600; color: var(--text); }
.meta-value--accent { color: var(--accent); }

/* ── Section title ──────────────────────────────────────────── */
.section-title {
  font-size: 0.9375rem;
  font-weight: 700;
  color: var(--text);
  font-family: var(--font-display);
  display: flex;
  align-items: center;
  gap: 8px;
  margin-bottom: 1rem;
}
.section-title i { color: var(--accent); font-size: 0.875rem; }

/* ── Status blocks ──────────────────────────────────────────── */
.status-block {
  border-radius: var(--radius);
  border: 1px solid;
  padding: 1.75rem 1.5rem;
  text-align: center;
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 0.625rem;
}
.status-block__icon { font-size: 2rem; margin-bottom: 0.25rem; }
.status-block__title { font-size: 1.125rem; font-weight: 700; }
.status-block__body { font-size: 0.875rem; max-width: 480px; }

.status-block--progress {
  background: rgba(255,106,61,.06);
  border-color: rgba(255,106,61,.2);
  color: var(--text);
}
.status-block--progress .status-block__body { color: var(--muted); }

.status-block--revision {
  background: rgba(255,140,60,.08);
  border-color: rgba(255,140,60,.25);
}
.status-block--revision .status-block__icon { color: #ffb07a; }
.status-block--revision .status-block__title { color: #ffb07a; }
.status-block--revision .status-block__body { color: rgba(255,176,122,.75); }

.status-block--revised {
  background: rgba(34,200,230,.07);
  border-color: rgba(34,200,230,.22);
}
.status-block--revised .status-block__icon { color: #7ddce8; }
.status-block--revised .status-block__title { color: #7ddce8; }
.status-block--revised .status-block__body { color: rgba(125,220,232,.75); }

.status-block--failed {
  background: rgba(220,60,60,.1);
  border-color: rgba(220,60,60,.28);
}
.status-block--failed .status-block__icon { color: #f4a57a; }
.status-block--failed .status-block__title { color: #f4a57a; }
.status-block--failed .status-block__body { color: rgba(244,165,122,.75); }

.status-block--cancelled {
  background: rgba(100,95,90,.1);
  border-color: rgba(100,95,90,.25);
}
.status-block--cancelled .status-block__icon { color: var(--faint); }
.status-block--cancelled .status-block__title { color: var(--muted); }

/* Spinner animation */
.spin-ring {
  position: absolute;
  inset: 0;
  border-radius: 50%;
  border: 4px solid transparent;
}
.spin-ring--track { border-color: rgba(255,106,61,.18); }
.spin-ring--fill {
  border-top-color: var(--accent);
  animation: spin 0.9s linear infinite;
}
.status-block__spinner {
  position: relative;
  width: 52px;
  height: 52px;
  margin-bottom: 0.5rem;
}
@keyframes spin { to { transform: rotate(360deg); } }

/* ── Approval section ───────────────────────────────────────── */
.status-block--approval { }
.approval-title {
  font-size: 1.125rem;
  font-weight: 700;
  color: var(--text);
  display: flex;
  align-items: center;
  gap: 8px;
  margin-bottom: 0.375rem;
}
.approval-title i { color: var(--gold); }
.approval-sub { font-size: 0.875rem; color: var(--muted); margin-bottom: 1.25rem; }

.preview-wrap { margin-bottom: 1.25rem; }
.preview-img {
  width: 100%;
  border-radius: 14px;
  border: 1px solid var(--line-soft);
}
.preview-placeholder {
  background: var(--surface-2);
  border: 1px solid var(--line);
  border-radius: 14px;
  height: 10rem;
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 10px;
  color: var(--faint);
  font-size: 0.875rem;
}

.approval-actions { display: flex; flex-wrap: wrap; gap: 0.75rem; margin-bottom: 1rem; }
.btn-approve {
  flex: 1;
  min-width: 160px;
  justify-content: center;
  padding: 13px 18px;
  border-radius: 12px;
  font-size: 0.9375rem;
  font-weight: 700;
  background: rgba(60,180,100,.18);
  color: #7de0a8;
  border: 1px solid rgba(60,180,100,.35);
  cursor: pointer;
  display: flex;
  align-items: center;
  gap: 8px;
  transition: background 0.15s, border-color 0.15s;
  font-family: var(--font-ui);
}
.btn-approve:hover:not(:disabled) {
  background: rgba(60,180,100,.28);
  border-color: rgba(60,180,100,.5);
}
.btn-approve:disabled { opacity: 0.5; cursor: not-allowed; }

.btn-revision {
  flex: 1;
  min-width: 160px;
  justify-content: center;
  padding: 13px 18px;
  border-radius: 12px;
  font-size: 0.9375rem;
  font-weight: 700;
  background: transparent;
  color: #ffb07a;
  border: 2px solid rgba(255,140,60,.4);
  cursor: pointer;
  display: flex;
  align-items: center;
  gap: 8px;
  transition: border-color 0.15s, background 0.15s;
  font-family: var(--font-ui);
}
.btn-revision:hover {
  border-color: rgba(255,140,60,.7);
  background: rgba(255,140,60,.06);
}

/* ── Revision form ──────────────────────────────────────────── */
.revision-form {
  margin-top: 1rem;
  background: rgba(255,140,60,.07);
  border: 1px solid rgba(255,140,60,.25);
  border-radius: 14px;
  padding: 1.125rem;
}
.revision-form__title {
  font-size: 0.875rem;
  font-weight: 700;
  color: #ffb07a;
  margin-bottom: 0.625rem;
  display: flex;
  align-items: center;
  gap: 7px;
  flex-wrap: wrap;
}
.revision-form__free { font-weight: 400; color: rgba(255,176,122,.7); font-size: 0.8125rem; }

.field-textarea {
  width: 100%;
  background: var(--surface-2);
  border: 1px solid rgba(255,140,60,.35);
  border-radius: 10px;
  padding: 10px 14px;
  font-size: 0.875rem;
  color: var(--text);
  font-family: var(--font-ui);
  outline: none;
  resize: vertical;
  transition: border-color 0.15s, box-shadow 0.15s;
}
.field-textarea::placeholder { color: var(--faint); }
.field-textarea:focus {
  border-color: rgba(255,140,60,.6);
  box-shadow: 0 0 0 3px rgba(255,140,60,.12);
}

.char-count { font-size: 0.75rem; color: var(--faint); margin-top: 4px; text-align: right; }
.revision-form__actions { display: flex; gap: 0.5rem; margin-top: 0.75rem; flex-wrap: wrap; }

.btn-revision-submit {
  padding: 9px 18px;
  border-radius: 10px;
  font-size: 0.875rem;
  font-weight: 700;
  background: rgba(255,140,60,.2);
  color: #ffb07a;
  border: 1px solid rgba(255,140,60,.4);
  cursor: pointer;
  display: inline-flex;
  align-items: center;
  gap: 7px;
  font-family: var(--font-ui);
  transition: background 0.15s;
}
.btn-revision-submit:hover:not(:disabled) { background: rgba(255,140,60,.32); }
.btn-revision-submit:disabled { opacity: 0.5; cursor: not-allowed; }

.revision-used-note {
  margin-top: 0.75rem;
  font-size: 0.78rem;
  color: var(--faint);
  display: flex;
  align-items: center;
  gap: 6px;
}

/* ── Approved section ───────────────────────────────────────── */
.status-block--approved { }
.approved-header {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  margin-bottom: 0.5rem;
}
.approved-icon { font-size: 1.75rem; color: #4ec984; flex-shrink: 0; }
.approved-title { font-size: 1rem; font-weight: 700; color: var(--text); }
.approved-body { font-size: 0.875rem; color: var(--muted); margin-bottom: 1rem; }
.approved-preview {
  width: 100%;
  max-width: 520px;
  border-radius: 14px;
  border: 1px solid var(--line-soft);
  margin-top: 0.5rem;
}
.approved-download { margin-top: 0.75rem; }
.download-link {
  display: inline-flex;
  align-items: center;
  gap: 7px;
  font-size: 0.875rem;
  font-weight: 600;
  color: var(--accent);
  text-decoration: none;
  transition: color 0.15s;
}
.download-link:hover { color: var(--accent-2); }

/* ── Details section ────────────────────────────────────────── */
.details-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 1rem;
}
@media (max-width: 480px) { .details-grid { grid-template-columns: 1fr; } }
.detail-cell { display: flex; flex-direction: column; gap: 4px; }
.detail-cell--full { grid-column: 1 / -1; }
.detail-label {
  font-size: 0.7rem;
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 0.07em;
  color: var(--faint);
}
.detail-value { font-size: 0.9rem; color: var(--muted); }
.detail-textarea {
  background: var(--surface-2);
  border: 1px solid var(--line-soft);
  border-radius: 10px;
  padding: 10px 14px;
  white-space: pre-wrap;
  font-size: 0.875rem;
  line-height: 1.6;
  color: var(--muted);
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

/* ── Badges ─────────────────────────────────────────────────── */
.badge {
  display: inline-block;
  font-size: 0.72rem;
  font-weight: 700;
  padding: 4px 10px;
  border-radius: 99px;
  letter-spacing: 0.01em;
  white-space: nowrap;
}
.badge-draft     { background: rgba(138,128,115,.18); color: var(--muted);     border: 1px solid rgba(138,128,115,.3); }
.badge-pending   { background: rgba(231,185,78,.15);  color: #e7d08a;          border: 1px solid rgba(231,185,78,.3); }
.badge-inprogress{ background: rgba(255,106,61,.13);  color: var(--accent-2);  border: 1px solid rgba(255,106,61,.3); }
.badge-awaiting  { background: rgba(160,110,220,.15); color: #c9a8f5;          border: 1px solid rgba(160,110,220,.3); }
.badge-approved  { background: rgba(60,180,100,.13);  color: #7de0a8;          border: 1px solid rgba(60,180,100,.28); }
.badge-revision  { background: rgba(255,140,60,.15);  color: #ffb07a;          border: 1px solid rgba(255,140,60,.3); }
.badge-revised   { background: rgba(34,200,230,.12);  color: #7ddce8;          border: 1px solid rgba(34,200,230,.28); }
.badge-cancelled { background: rgba(220,60,60,.12);   color: #f4a57a;          border: 1px solid rgba(220,60,60,.28); }
.badge-manual    { background: rgba(130,100,220,.15); color: #c9a8f5;          border: 1px solid rgba(130,100,220,.3); }
.badge-ai        { background: rgba(34,200,230,.12);  color: #7ddce8;          border: 1px solid rgba(34,200,230,.28); }
</style>

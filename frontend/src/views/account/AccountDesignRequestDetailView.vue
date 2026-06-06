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
  Pending:           'bg-yellow-100 text-yellow-800',
  InProgress:        'bg-blue-100 text-blue-800',
  AwaitingApproval:  'bg-purple-100 text-purple-800',
  Approved:          'bg-green-100 text-green-700',
  RevisionRequested: 'bg-orange-100 text-orange-800',
  Revised:           'bg-sky-100 text-sky-800',
  Final:             'bg-green-100 text-green-800',
  Failed:            'bg-red-100 text-red-700',
  Cancelled:         'bg-red-100 text-red-700',
}
function statusLabel(s: string) { return STATUS_LABELS[s] ?? s }
function statusClass(s: string) { return STATUS_CLASSES[s] ?? 'bg-gray-100 text-gray-600' }

const isManual = computed(() => request.value?.mode === 'Manual')
const canRequestRevision = computed(
  () =>
    request.value?.mode === 'Manual' &&
    request.value.status === 'AwaitingApproval' &&
    request.value.revisionCount < 1,
)
</script>

<template>
  <div class="max-w-3xl mx-auto px-4 py-10">
    <!-- Breadcrumb -->
    <div class="flex items-center gap-2 mb-6 text-sm">
      <RouterLink to="/account/design-requests" class="text-blue-700 hover:underline">
        Mine design-bestillinger
      </RouterLink>
      <span class="text-gray-400">›</span>
      <span class="text-gray-600">Bestilling #{{ requestId }}</span>
    </div>

    <!-- Loading -->
    <div v-if="loading" class="flex justify-center py-16">
      <div class="w-8 h-8 border-4 border-blue-600 border-t-transparent rounded-full animate-spin" />
    </div>
    <div
      v-else-if="loadError"
      class="bg-red-50 border border-red-200 text-red-800 rounded-xl p-6 text-center"
    >
      {{ loadError }}
    </div>

    <template v-else-if="request">

      <!-- ── Header ─────────────────────────────────────────────────────── -->
      <div class="bg-white border border-gray-200 rounded-xl p-6 mb-5">
        <div class="flex flex-wrap items-start justify-between gap-4 mb-4">
          <div>
            <h1 class="text-xl font-bold text-gray-900">Design-bestilling #{{ request.id }}</h1>
            <p class="text-sm text-gray-400 mt-0.5">Opprettet {{ formatDateTime(request.createdAt) }}</p>
          </div>
          <span
            class="text-sm font-semibold px-3 py-1.5 rounded-full"
            :class="statusClass(request.status)"
          >
            {{ statusLabel(request.status) }}
          </span>
        </div>

        <!-- Summary row -->
        <div class="grid sm:grid-cols-4 gap-3 text-sm">
          <div>
            <div class="text-xs font-semibold uppercase tracking-wide text-gray-400 mb-0.5">Mal</div>
            <div class="text-gray-800 font-medium">{{ templateName(request.bannerTemplateId) }}</div>
          </div>
          <div>
            <div class="text-xs font-semibold uppercase tracking-wide text-gray-400 mb-0.5">Modus</div>
            <span
              class="text-xs font-semibold px-2 py-0.5 rounded-full"
              :class="isManual ? 'bg-indigo-100 text-indigo-800' : 'bg-cyan-100 text-cyan-800'"
            >
              {{ modeLabel(request.mode) }}
            </span>
          </div>
          <div>
            <div class="text-xs font-semibold uppercase tracking-wide text-gray-400 mb-0.5">Format</div>
            <div class="text-gray-700">{{ request.aspectRatio }}</div>
          </div>
          <div>
            <div class="text-xs font-semibold uppercase tracking-wide text-gray-400 mb-0.5">Pris</div>
            <div class="font-bold text-blue-700">{{ formatNok(request.priceNok) }}</div>
          </div>
        </div>
      </div>

      <!-- ── Status-specific sections ────────────────────────────────────── -->

      <!-- PENDING / IN PROGRESS: progress indicator -->
      <div
        v-if="request.status === 'Pending' || request.status === 'InProgress'"
        class="bg-blue-50 border border-blue-200 rounded-xl p-6 mb-5 text-center"
      >
        <div class="flex justify-center mb-4">
          <div class="relative w-14 h-14">
            <div class="absolute inset-0 rounded-full border-4 border-blue-100" />
            <div class="absolute inset-0 rounded-full border-4 border-blue-500 border-t-transparent animate-spin" />
          </div>
        </div>
        <h2 class="text-lg font-semibold text-blue-900 mb-1">
          {{ request.status === 'Pending' ? 'Venter på betaling…' : 'Designet er under arbeid' }}
        </h2>
        <p class="text-sm text-blue-700">
          {{ request.status === 'InProgress'
            ? 'Designeren jobber med banneret ditt. Du mottar en e-post når forhåndsvisningen er klar.'
            : 'Bestillingen er registrert og venter på betalingsbekreftelse.' }}
        </p>
      </div>

      <!-- AWAITING APPROVAL: show preview + action buttons -->
      <div
        v-else-if="request.status === 'AwaitingApproval'"
        class="bg-white border border-purple-200 rounded-xl p-6 mb-5"
      >
        <h2 class="text-lg font-bold text-gray-900 mb-1 flex items-center gap-2">
          <span class="text-purple-600">✨</span>
          Forhåndsvisning klar!
        </h2>
        <p class="text-sm text-gray-600 mb-4">
          Designeren har fullført banneret ditt. Se det gjennom og godkjenn eller be om en korrigering.
        </p>

        <!-- Preview image -->
        <div class="mb-5">
          <img
            v-if="request.previewUrl"
            :src="request.previewUrl"
            alt="Designforslag"
            class="w-full rounded-xl border border-gray-200 shadow-sm"
          />
          <div v-else class="bg-gray-50 rounded-xl border border-gray-200 h-40 flex items-center justify-center text-gray-400 text-sm">
            Forhåndsvisning ikke tilgjengelig ennå
          </div>
        </div>

        <!-- Action buttons -->
        <div v-if="!approveSuccess" class="flex flex-col sm:flex-row gap-3">
          <!-- Approve -->
          <button
            type="button"
            class="flex-1 bg-green-600 hover:bg-green-700 disabled:bg-gray-300 text-white font-semibold py-3 rounded-xl transition flex items-center justify-center gap-2"
            :disabled="approving"
            @click="approve"
          >
            <span
              v-if="approving"
              class="inline-block w-4 h-4 border-2 border-white border-t-transparent rounded-full animate-spin"
            />
            ✓ Godkjenn design
          </button>

          <!-- Request revision (Manual only, revisionCount < 1) -->
          <button
            v-if="canRequestRevision && !showRevisionForm"
            type="button"
            class="flex-1 border-2 border-orange-300 hover:border-orange-400 text-orange-700 font-semibold py-3 rounded-xl transition flex items-center justify-center gap-2"
            @click="showRevisionForm = true"
          >
            ✏️ Be om korrigering
          </button>
        </div>

        <p v-if="approveSuccess" class="mt-3 text-green-700 bg-green-50 border border-green-200 rounded-lg px-4 py-3 text-sm">
          ✓ {{ approveSuccess }}
        </p>
        <p v-if="approveError" class="mt-3 text-red-700 bg-red-50 border border-red-200 rounded-lg px-4 py-3 text-sm">
          {{ approveError }}
        </p>

        <!-- Revision form -->
        <div v-if="showRevisionForm && !approveSuccess" class="mt-4 bg-orange-50 border border-orange-200 rounded-xl p-4">
          <h3 class="text-sm font-semibold text-orange-800 mb-2">
            Beskriv hva du ønsker endret
            <span class="text-orange-500 font-normal">(du har 1 gratis korrigering)</span>
          </h3>
          <textarea
            v-model="revisionComment"
            rows="3"
            maxlength="2000"
            placeholder="f.eks. Bytt bakgrunnsfargen til blå, gjør teksten større…"
            class="w-full border border-orange-300 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-orange-400 resize-none"
          />
          <p class="text-xs text-gray-400 mt-1">{{ revisionComment.length }} / 2000 tegn</p>
          <div class="flex gap-2 mt-3">
            <button
              type="button"
              :disabled="!revisionComment.trim() || revisionSaving"
              class="bg-orange-600 text-white px-4 py-2 rounded-lg text-sm font-medium hover:bg-orange-700 disabled:opacity-50 disabled:cursor-not-allowed"
              @click="submitRevision"
            >
              {{ revisionSaving ? 'Sender…' : 'Send korrigeringsønske' }}
            </button>
            <button
              type="button"
              class="border border-gray-300 text-gray-600 px-4 py-2 rounded-lg text-sm hover:bg-gray-50"
              @click="showRevisionForm = false; revisionComment = ''"
            >
              Avbryt
            </button>
          </div>
          <p v-if="revisionError" class="mt-2 text-red-600 text-sm">{{ revisionError }}</p>
        </div>

        <!-- Already used revision -->
        <p
          v-if="isManual && request.revisionCount >= 1 && request.status === 'AwaitingApproval'"
          class="mt-3 text-xs text-gray-400"
        >
          Du har allerede brukt din gratis korrigering.
        </p>
      </div>

      <!-- REVISION REQUESTED -->
      <div
        v-else-if="request.status === 'RevisionRequested'"
        class="bg-orange-50 border border-orange-200 rounded-xl p-6 mb-5 text-center"
      >
        <div class="text-3xl mb-3">✏️</div>
        <h2 class="text-lg font-semibold text-orange-900 mb-1">Korrigering er under behandling</h2>
        <p class="text-sm text-orange-700">
          Vi har mottatt ønsket ditt og jobber med korrigeringen. Du vil motta en e-post
          når det reviderte designet er klart.
        </p>
      </div>

      <!-- REVISED: new preview pending approval -->
      <div
        v-else-if="request.status === 'Revised'"
        class="bg-sky-50 border border-sky-200 rounded-xl p-6 mb-5 text-center"
      >
        <div class="text-3xl mb-3">🔄</div>
        <h2 class="text-lg font-semibold text-sky-900 mb-1">Revidert design klart</h2>
        <p class="text-sm text-sky-700">
          Det reviderte designet er sendt til godkjenning — sjekk e-posten din for varsling.
        </p>
      </div>

      <!-- APPROVED / FINAL -->
      <div
        v-else-if="request.status === 'Approved' || request.status === 'Final'"
        class="bg-green-50 border border-green-200 rounded-xl p-6 mb-5"
      >
        <div class="flex items-center gap-3 mb-3">
          <span class="text-3xl">🎉</span>
          <h2 class="text-lg font-semibold text-green-900">
            {{ request.status === 'Final' ? 'Banneret er levert!' : 'Bestillingen er godkjent og sendes i produksjon' }}
          </h2>
        </div>
        <p class="text-sm text-green-800">
          {{ request.status === 'Final'
            ? 'Banneret ditt er ferdig produsert og levert.'
            : 'Takk! Banneret ditt er nå i produksjon.' }}
        </p>

        <!-- Show the approved preview if available -->
        <img
          v-if="request.previewUrl"
          :src="request.previewUrl"
          alt="Godkjent design"
          class="mt-4 w-full max-w-lg rounded-xl border border-green-200 shadow-sm"
        />
        <div v-if="request.finalCroppedUrl" class="mt-3">
          <a
            :href="request.finalCroppedUrl"
            target="_blank"
            rel="noopener noreferrer"
            class="text-sm text-blue-600 hover:underline"
          >
            Last ned full versjon ↗
          </a>
        </div>
      </div>

      <!-- FAILED -->
      <div
        v-else-if="request.status === 'Failed'"
        class="bg-red-50 border border-red-200 rounded-xl p-6 mb-5 text-center"
      >
        <div class="text-3xl mb-3">⚠️</div>
        <h2 class="text-lg font-semibold text-red-900 mb-1">Noe gikk galt</h2>
        <p class="text-sm text-red-700">
          {{ request.lastError ?? 'Genereringen feilet. Vennligst kontakt support.' }}
        </p>
      </div>

      <!-- CANCELLED -->
      <div
        v-else-if="request.status === 'Cancelled'"
        class="bg-gray-50 border border-gray-200 rounded-xl p-6 mb-5 text-center"
      >
        <div class="text-3xl mb-3">🚫</div>
        <h2 class="text-lg font-semibold text-gray-700 mb-1">Bestillingen er kansellert</h2>
      </div>

      <!-- Revision success feedback (outside status block) -->
      <p
        v-if="revisionSuccess"
        class="mb-5 text-green-700 bg-green-50 border border-green-200 rounded-xl px-4 py-3 text-sm"
      >
        ✓ {{ revisionSuccess }}
      </p>

      <!-- ── Request details (read-only) ─────────────────────────────────── -->
      <div class="bg-white border border-gray-200 rounded-xl p-6 mb-5">
        <h2 class="text-base font-semibold text-gray-900 mb-4">Bestillingsdetaljer</h2>
        <dl class="grid sm:grid-cols-2 gap-4 text-sm">
          <div>
            <dt class="text-xs font-semibold uppercase tracking-wide text-gray-400 mb-0.5">Språk</dt>
            <dd class="text-gray-700">{{ langLabel(request.language) }}</dd>
          </div>
          <div>
            <dt class="text-xs font-semibold uppercase tracking-wide text-gray-400 mb-0.5">Navn på person</dt>
            <dd class="text-gray-700">{{ request.personName }}</dd>
          </div>
          <div v-if="request.personAge != null">
            <dt class="text-xs font-semibold uppercase tracking-wide text-gray-400 mb-0.5">Alder</dt>
            <dd class="text-gray-700">{{ request.personAge }} år</dd>
          </div>
          <div>
            <dt class="text-xs font-semibold uppercase tracking-wide text-gray-400 mb-0.5">Sideforhold</dt>
            <dd class="text-gray-700">{{ request.aspectRatio }}</dd>
          </div>
          <div class="sm:col-span-2">
            <dt class="text-xs font-semibold uppercase tracking-wide text-gray-400 mb-0.5">Tekst på banneret</dt>
            <dd class="text-gray-700 bg-gray-50 rounded-lg p-3 border border-gray-100 whitespace-pre-wrap">{{ request.textContent }}</dd>
          </div>
          <div class="sm:col-span-2">
            <dt class="text-xs font-semibold uppercase tracking-wide text-gray-400 mb-0.5">Tema / stil</dt>
            <dd class="text-gray-700 bg-gray-50 rounded-lg p-3 border border-gray-100 whitespace-pre-wrap">{{ request.themeDescription }}</dd>
          </div>
        </dl>
      </div>

      <RouterLink
        to="/account/design-requests"
        class="text-sm text-blue-700 hover:underline"
      >
        ← Tilbake til oversikten
      </RouterLink>
    </template>
  </div>
</template>

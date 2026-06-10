<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useRoute, RouterLink } from 'vue-router'
import {
  getAdminDesignRequest,
  updateDesignRequestStatus,
  uploadDesignRequestPreview,
} from '@/api/admin'
import type { AdminDesignRequestDetail } from '@/api/admin'
import { formatNok, formatDateTime } from '@/utils/format'
import { drStatusLabel as statusLabel, drStatusAdminClass as statusClass } from '@/utils/orderStatus'

const route = useRoute()
const requestId = Number(route.params.id)

// ── Data ──────────────────────────────────────────────────────────────────────
const request = ref<AdminDesignRequestDetail | null>(null)
const loading = ref(true)
const loadError = ref<string | null>(null)

async function loadRequest() {
  loading.value = true
  loadError.value = null
  try {
    request.value = await getAdminDesignRequest(requestId)
    newStatus.value = request.value.status
  } catch {
    loadError.value = 'Kunne ikke laste design-bestillingen.'
  } finally {
    loading.value = false
  }
}

onMounted(loadRequest)

// ── Status updater ────────────────────────────────────────────────────────────
const newStatus = ref('')
const statusNotes = ref('')
const statusSaving = ref(false)
const statusError = ref('')
const statusSuccess = ref('')

const ADMIN_STATUSES = [
  { value: 'InProgress',       label: 'Under arbeid' },
  { value: 'AwaitingApproval', label: 'Venter godkjenning' },
  { value: 'Revised',          label: 'Revidert' },
  { value: 'Final',            label: 'Levert' },
  { value: 'Cancelled',        label: 'Kansellert' },
]

async function saveStatus() {
  if (!newStatus.value) return
  statusSaving.value = true
  statusError.value = ''
  statusSuccess.value = ''
  try {
    const updated = await updateDesignRequestStatus(
      requestId,
      newStatus.value,
      statusNotes.value.trim() || undefined,
    )
    // Merge admin-only fields back since the status endpoint returns DesignRequestDetailDto
    if (request.value) {
      Object.assign(request.value, updated, {
        customerName: request.value.customerName,
        customerEmail: request.value.customerEmail,
        uploadedPhotoUrl: request.value.uploadedPhotoUrl,
        templateName: request.value.templateName,
      })
    }
    newStatus.value = updated.status
    statusSuccess.value = 'Status oppdatert.'
    setTimeout(() => { statusSuccess.value = '' }, 3000)
  } catch (err: unknown) {
    const e = err as { response?: { data?: { error?: string } } }
    statusError.value = e.response?.data?.error ?? 'Lagring feilet.'
  } finally {
    statusSaving.value = false
  }
}

// ── Preview upload (Manual only) ──────────────────────────────────────────────
const previewFile = ref<File | null>(null)
const previewFileInput = ref<HTMLInputElement | null>(null)
const uploadSaving = ref(false)
const uploadError = ref('')
const uploadSuccess = ref('')

function onPreviewFileChange(e: Event) {
  const input = e.target as HTMLInputElement
  previewFile.value = input.files?.[0] ?? null
  uploadError.value = ''
}

async function uploadPreview() {
  if (!previewFile.value) return
  if (previewFile.value.size > 20 * 1024 * 1024) {
    uploadError.value = 'Filen er for stor (maks 20 MB).'
    return
  }
  uploadSaving.value = true
  uploadError.value = ''
  uploadSuccess.value = ''
  try {
    const updated = await uploadDesignRequestPreview(requestId, previewFile.value)
    if (request.value) {
      request.value.previewUrl = updated.previewUrl
      request.value.status = updated.status
      newStatus.value = updated.status
    }
    previewFile.value = null
    if (previewFileInput.value) previewFileInput.value.value = ''
    uploadSuccess.value = 'Forhåndsvisning lastet opp. Kunden varsles.'
    setTimeout(() => { uploadSuccess.value = '' }, 4000)
  } catch (err: unknown) {
    const e = err as { response?: { data?: { error?: string } } }
    uploadError.value = e.response?.data?.error ?? 'Opplasting feilet.'
  } finally {
    uploadSaving.value = false
  }
}

// ── Helpers ───────────────────────────────────────────────────────────────────
function modeLabel(m: string) { return m === 'Manual' ? 'Manuell' : 'AI' }

function langLabel(l: string) { return l === 'nb' ? 'Norsk' : l === 'en' ? 'Engelsk' : l }

// Latest revision comment for RevisionRequested state
const latestRevision = computed(() => {
  if (!request.value?.revisions?.length) return null
  return [...request.value.revisions].sort((a, b) => b.revisionNumber - a.revisionNumber)[0]
})

const isManual = computed(() => request.value?.mode === 'Manual')
const isAi     = computed(() => request.value?.mode === 'Ai')
</script>

<template>
  <div class="max-w-5xl mx-auto px-4 py-8">
    <!-- Breadcrumb -->
    <div class="flex items-center gap-2 mb-6 text-sm">
      <RouterLink to="/admin/design-requests" class="text-blue-400 hover:underline">
        Design-bestillinger
      </RouterLink>
      <span class="text-gray-600">›</span>
      <span class="text-gray-400">Bestilling #{{ requestId }}</span>
    </div>

    <!-- Loading -->
    <div v-if="loading" class="flex justify-center py-16">
      <div class="w-8 h-8 border-4 border-blue-500 border-t-transparent rounded-full animate-spin" />
    </div>
    <div
      v-else-if="loadError"
      class="bg-red-900/30 border border-red-700 text-red-400 rounded-xl p-6 text-center"
    >
      {{ loadError }}
    </div>

    <template v-else-if="request">

      <!-- ── Header ─────────────────────────────────────────────────────── -->
      <div class="bg-gray-800 border border-gray-700 rounded-xl p-6 mb-5">
        <div class="flex flex-wrap items-start justify-between gap-4">
          <div>
            <h1 class="text-xl font-bold text-gray-100">Design-bestilling #{{ request.id }}</h1>
            <p class="text-sm text-gray-500 mt-0.5">Opprettet {{ formatDateTime(request.createdAt) }}</p>
          </div>
          <span
            class="text-sm font-semibold px-3 py-1.5 rounded-full"
            :class="statusClass(request.status)"
          >
            {{ statusLabel(request.status) }}
          </span>
        </div>

        <!-- Revision-requested alert -->
        <div
          v-if="request.status === 'RevisionRequested' && latestRevision"
          class="mt-4 bg-orange-900/30 border border-orange-700 rounded-lg p-4"
        >
          <div class="text-xs font-semibold uppercase tracking-wide text-orange-400 mb-1">
            Kunden ba om revisjon
          </div>
          <p class="text-sm text-orange-200 font-medium">{{ latestRevision.customerComment }}</p>
          <p class="text-xs text-orange-500 mt-1">{{ formatDateTime(latestRevision.createdAt) }}</p>
        </div>
      </div>

      <!-- ── Customer info ──────────────────────────────────────────────── -->
      <div class="bg-gray-800 border border-gray-700 rounded-xl p-6 mb-5">
        <h2 class="text-base font-semibold text-gray-100 mb-4">Kundeinformasjon</h2>
        <div class="grid sm:grid-cols-3 gap-4 text-sm">
          <div>
            <div class="text-xs font-semibold uppercase tracking-wide text-gray-500 mb-0.5">Navn</div>
            <div class="font-medium text-gray-200">{{ request.customerName }}</div>
          </div>
          <div>
            <div class="text-xs font-semibold uppercase tracking-wide text-gray-500 mb-0.5">E-post</div>
            <a
              :href="`mailto:${request.customerEmail}`"
              class="text-blue-400 hover:underline text-sm"
            >
              {{ request.customerEmail }}
            </a>
          </div>
          <div>
            <div class="text-xs font-semibold uppercase tracking-wide text-gray-500 mb-0.5">Innsendt</div>
            <div class="text-gray-300">{{ formatDateTime(request.createdAt) }}</div>
          </div>
          <div>
            <div class="text-xs font-semibold uppercase tracking-wide text-gray-500 mb-0.5">Mal</div>
            <div class="text-gray-300">{{ request.templateName ?? `Mal #${request.bannerTemplateId}` }}</div>
          </div>
          <div>
            <div class="text-xs font-semibold uppercase tracking-wide text-gray-500 mb-0.5">Modus</div>
            <span
              class="text-xs font-semibold px-2 py-0.5 rounded-full"
              :class="isManual ? 'bg-indigo-900/50 text-indigo-300' : 'bg-cyan-900/50 text-cyan-300'"
            >
              {{ modeLabel(request.mode) }}
            </span>
          </div>
          <div>
            <div class="text-xs font-semibold uppercase tracking-wide text-gray-500 mb-0.5">Pris</div>
            <div class="font-bold text-blue-400">{{ formatNok(request.priceNok) }}</div>
          </div>
        </div>
      </div>

      <!-- ── Design inputs (read-only) ─────────────────────────────────── -->
      <div class="bg-gray-800 border border-gray-700 rounded-xl p-6 mb-5">
        <h2 class="text-base font-semibold text-gray-100 mb-4">Designinformasjon</h2>
        <div class="grid sm:grid-cols-2 gap-4 text-sm">
          <div>
            <div class="text-xs font-semibold uppercase tracking-wide text-gray-500 mb-0.5">Språk</div>
            <div class="text-gray-300">{{ langLabel(request.language) }}</div>
          </div>
          <div>
            <div class="text-xs font-semibold uppercase tracking-wide text-gray-500 mb-0.5">Navn på person</div>
            <div class="text-gray-300">{{ request.personName }}</div>
          </div>
          <div v-if="request.personAge != null">
            <div class="text-xs font-semibold uppercase tracking-wide text-gray-500 mb-0.5">Alder</div>
            <div class="text-gray-300">{{ request.personAge }} år</div>
          </div>
          <div>
            <div class="text-xs font-semibold uppercase tracking-wide text-gray-500 mb-0.5">Sideforhold</div>
            <div class="text-gray-300">{{ request.aspectRatio }}</div>
          </div>
          <div class="sm:col-span-2">
            <div class="text-xs font-semibold uppercase tracking-wide text-gray-500 mb-0.5">Tekstinnhold</div>
            <div class="text-gray-300 whitespace-pre-wrap bg-gray-900 rounded-lg p-3 border border-gray-700">{{ request.textContent }}</div>
          </div>
          <div class="sm:col-span-2">
            <div class="text-xs font-semibold uppercase tracking-wide text-gray-500 mb-0.5">Temabeskrivelse</div>
            <div class="text-gray-300 whitespace-pre-wrap bg-gray-900 rounded-lg p-3 border border-gray-700">{{ request.themeDescription }}</div>
          </div>
        </div>

        <!-- Uploaded portrait photo -->
        <div v-if="request.uploadedPhotoUrl" class="mt-4">
          <div class="text-xs font-semibold uppercase tracking-wide text-gray-500 mb-2">Opplastet portrettfoto</div>
          <div class="flex items-start gap-4">
            <img
              :src="request.uploadedPhotoUrl"
              alt="Portrettfoto"
              class="w-24 h-24 object-cover rounded-lg border border-gray-600 shadow-sm"
            />
            <a
              :href="request.uploadedPhotoUrl"
              target="_blank"
              rel="noopener noreferrer"
              class="text-sm text-blue-400 hover:underline mt-2"
            >
              Se full størrelse ↗
            </a>
          </div>
        </div>
      </div>

      <!-- ── Current preview ────────────────────────────────────────────── -->
      <div
        v-if="request.previewUrl"
        class="bg-gray-800 border border-gray-700 rounded-xl p-6 mb-5"
      >
        <h2 class="text-base font-semibold text-gray-100 mb-3">
          {{ isManual ? 'Designforslag (gjeldende)' : 'AI-generert bilde' }}
        </h2>

        <!-- Approval status banner -->
        <div v-if="request.status === 'Approved'" class="mb-3 bg-green-900/30 border border-green-700 text-green-400 rounded-lg px-4 py-2 text-sm font-medium">
          ✓ Kunden har godkjent denne forhåndsvisningen
          <span v-if="request.customerApprovedAt" class="font-normal text-green-500"> — {{ formatDateTime(request.customerApprovedAt) }}</span>
        </div>
        <div v-else-if="request.status === 'RevisionRequested'" class="mb-3 bg-orange-900/30 border border-orange-700 text-orange-400 rounded-lg px-4 py-2 text-sm font-medium">
          Kunden har bedt om revisjon
        </div>

        <img
          :src="request.previewUrl"
          alt="Forhåndsvisning"
          class="w-full max-w-xl rounded-xl border border-gray-600 shadow-sm"
        />

        <!-- AI: link to full version -->
        <div v-if="isAi && request.finalCroppedUrl" class="mt-3">
          <a
            :href="request.finalCroppedUrl"
            target="_blank"
            rel="noopener noreferrer"
            class="text-sm text-blue-400 hover:underline"
          >
            Last ned full 4096×4096 versjon ↗
          </a>
        </div>
      </div>

      <!-- ── AI result (no preview yet) ────────────────────────────────── -->
      <div
        v-else-if="isAi"
        class="bg-gray-800 border border-gray-700 rounded-xl p-6 mb-5"
      >
        <h2 class="text-base font-semibold text-gray-100 mb-2">AI-generert bilde</h2>
        <p class="text-sm text-gray-400">
          Ingen generert bilde ennå.
          <span v-if="request.status === 'InProgress'" class="text-blue-400">Generering pågår…</span>
          <span v-else-if="request.status === 'Failed' && request.lastError" class="text-red-400">
            Feil: {{ request.lastError }}
          </span>
        </p>
      </div>

      <!-- ── Preview upload (Manual only) ──────────────────────────────── -->
      <div v-if="isManual" class="bg-gray-800 border border-gray-700 rounded-xl p-6 mb-5">
        <h2 class="text-base font-semibold text-gray-100 mb-1">Last opp designforslag</h2>
        <p class="text-xs text-gray-400 mb-4">
          JPEG eller PNG, maks 20 MB. Kunden varsles automatisk når designet er klart til godkjenning.
        </p>

        <div class="flex flex-col sm:flex-row gap-3 items-start">
          <input
            ref="previewFileInput"
            type="file"
            accept="image/jpeg,image/jpg,image/png"
            class="block text-sm text-gray-400 file:mr-3 file:py-2 file:px-4 file:rounded-lg file:border-0 file:text-sm file:font-medium file:bg-blue-900/50 file:text-blue-300 hover:file:bg-blue-900"
            @change="onPreviewFileChange"
          />
          <button
            :disabled="!previewFile || uploadSaving"
            class="bg-blue-700 text-white px-5 py-2 rounded-lg text-sm font-medium hover:bg-blue-600 disabled:opacity-50 disabled:cursor-not-allowed whitespace-nowrap"
            @click="uploadPreview"
          >
            {{ uploadSaving ? 'Laster opp…' : 'Send til kunde' }}
          </button>
        </div>
        <div v-if="previewFile" class="mt-1 text-xs text-gray-500">
          Valgt: {{ previewFile.name }} ({{ (previewFile.size / 1024 / 1024).toFixed(1) }} MB)
        </div>
        <div v-if="uploadSuccess" class="mt-2 text-green-400 text-sm">✓ {{ uploadSuccess }}</div>
        <div v-if="uploadError" class="mt-2 text-red-400 text-sm">{{ uploadError }}</div>
      </div>

      <!-- ── Status updater ─────────────────────────────────────────────── -->
      <div class="bg-gray-800 border border-gray-700 rounded-xl p-6 mb-5">
        <h2 class="text-base font-semibold text-gray-100 mb-3">Oppdater status</h2>
        <div class="flex flex-wrap items-start gap-3">
          <select
            v-model="newStatus"
            class="bg-gray-900 border border-gray-600 text-gray-100 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
          >
            <option v-for="s in ADMIN_STATUSES" :key="s.value" :value="s.value">
              {{ s.label }}
            </option>
          </select>
          <button
            :disabled="statusSaving || newStatus === request.status"
            class="bg-blue-700 text-white px-4 py-2 rounded-lg text-sm font-medium hover:bg-blue-600 disabled:opacity-50 disabled:cursor-not-allowed"
            @click="saveStatus"
          >
            {{ statusSaving ? 'Lagrer…' : 'Lagre status' }}
          </button>
          <span v-if="statusSuccess" class="text-green-400 text-sm self-center">✓ {{ statusSuccess }}</span>
          <span v-if="statusError" class="text-red-400 text-sm self-center">{{ statusError }}</span>
        </div>
        <!-- Optional notes -->
        <div class="mt-3">
          <label class="block text-xs font-medium text-gray-400 mb-1">Designernotat (valgfritt)</label>
          <input
            v-model="statusNotes"
            type="text"
            placeholder="Intern merknad…"
            class="w-full max-w-lg bg-gray-900 border border-gray-600 text-gray-100 placeholder:text-gray-500 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
          />
          <p v-if="request.designerNotes" class="mt-1 text-xs text-gray-500">
            Siste notat: <em>{{ request.designerNotes }}</em>
          </p>
        </div>
      </div>

      <!-- ── Revision history ───────────────────────────────────────────── -->
      <div v-if="request.revisions && request.revisions.length > 0" class="bg-gray-800 border border-gray-700 rounded-xl p-6 mb-5">
        <h2 class="text-base font-semibold text-gray-100 mb-4">Revisjonshistorikk</h2>
        <ul class="space-y-4">
          <li
            v-for="rev in [...request.revisions].sort((a, b) => b.revisionNumber - a.revisionNumber)"
            :key="rev.id"
            class="bg-orange-900/20 border border-orange-800 rounded-lg p-4"
          >
            <div class="flex items-center gap-2 mb-1">
              <span class="text-xs font-semibold text-orange-400 uppercase tracking-wide">
                Revisjon {{ rev.revisionNumber }}
              </span>
              <span class="text-xs text-orange-600">{{ formatDateTime(rev.createdAt) }}</span>
            </div>
            <p class="text-sm text-gray-200">{{ rev.customerComment }}</p>
          </li>
        </ul>
      </div>

      <RouterLink
        to="/admin/design-requests"
        class="text-sm text-blue-400 hover:underline"
      >
        ← Tilbake til oversikten
      </RouterLink>
    </template>
  </div>
</template>

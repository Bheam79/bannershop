<script setup lang="ts">
import { ref, computed, onMounted, watch } from 'vue'
import { getAdminTraffic, getAdminReferrers } from '@/api/analytics'
import type { TrafficSummary, ReferrerStats, SessionBucket } from '@/api/analytics'

// ── Date helpers ──────────────────────────────────────────────────────────────
function toDateInputValue(d: Date): string {
  return d.toISOString().slice(0, 10)
}

function startOfDayUtc(dateStr: string): string {
  return new Date(dateStr + 'T00:00:00Z').toISOString()
}

function endOfDayUtc(dateStr: string): string {
  return new Date(dateStr + 'T23:59:59Z').toISOString()
}

const now = new Date()
const defaultTo = toDateInputValue(now)
const defaultFrom = toDateInputValue(new Date(now.getFullYear(), now.getMonth(), now.getDate() - 29))

const fromDate = ref(defaultFrom)
const toDate = ref(defaultTo)
const groupBy = ref<'day' | 'hour'>('day')

// ── Quick ranges ──────────────────────────────────────────────────────────────
function setRange(days: number) {
  const t = new Date()
  toDate.value = toDateInputValue(t)
  fromDate.value = toDateInputValue(new Date(t.getFullYear(), t.getMonth(), t.getDate() - days + 1))
}

// ── Data loading ──────────────────────────────────────────────────────────────
const loading = ref(false)
const traffic = ref<TrafficSummary | null>(null)
const referrers = ref<ReferrerStats | null>(null)

async function load() {
  loading.value = true
  try {
    const params = {
      fromUtc: startOfDayUtc(fromDate.value),
      toUtc: endOfDayUtc(toDate.value),
      groupBy: groupBy.value,
    }
    const [t, r] = await Promise.all([
      getAdminTraffic(params),
      getAdminReferrers(params),
    ])
    traffic.value = t
    referrers.value = r
  } finally {
    loading.value = false
  }
}

onMounted(load)
watch([fromDate, toDate, groupBy], load)

// ── Chart helpers ─────────────────────────────────────────────────────────────

/** Normalise a value 0–maxVal to 0–maxBarPx pixels */
function barWidth(value: number, maxVal: number, maxBarPx = 300): number {
  if (maxVal === 0) return 0
  return Math.round((value / maxVal) * maxBarPx)
}

const maxSessions = computed(() =>
  Math.max(1, ...(traffic.value?.buckets ?? []).map((b: SessionBucket) => b.uniqueSessions))
)

// Referrer colour map
const sourceColour: Record<string, string> = {
  Direct: '#6366f1',
  Google: '#22c55e',
  Facebook: '#3b82f6',
  Instagram: '#ec4899',
  'Twitter/X': '#06b6d4',
  TikTok: '#f59e0b',
  Other: '#9ca3af',
}

function colourFor(source: string): string {
  return sourceColour[source] ?? '#9ca3af'
}

const referrerTotal = computed(() =>
  referrers.value?.sources.reduce((s, r) => s + r.sessions, 0) ?? 1
)

function referrerPercent(sessions: number): string {
  const pct = referrerTotal.value > 0 ? (sessions / referrerTotal.value) * 100 : 0
  return pct.toFixed(1) + '%'
}
</script>

<template>
  <div class="max-w-7xl mx-auto px-4 py-8">
    <!-- Header -->
    <div class="mb-6">
      <h1 class="text-2xl font-bold text-white">Trafikk-statistikk</h1>
      <p class="text-gray-400 mt-1">Besøkende, sesjoner og kilder over tid</p>
    </div>

    <!-- Filters -->
    <div class="bg-gray-800 rounded-xl border border-gray-700 p-4 mb-6 flex flex-wrap items-end gap-4">
      <!-- Quick range buttons -->
      <div class="flex gap-2">
        <button
          v-for="days in [7, 14, 30, 90]"
          :key="days"
          class="px-3 py-1.5 text-xs rounded-lg border border-gray-600 text-gray-300 hover:border-blue-500 hover:text-blue-400 transition"
          @click="setRange(days)"
        >
          {{ days }}d
        </button>
      </div>

      <div class="flex items-center gap-2">
        <label class="text-xs text-gray-400">Fra</label>
        <input
          v-model="fromDate"
          type="date"
          class="bg-gray-900 border border-gray-600 rounded-lg px-3 py-1.5 text-sm text-gray-200"
        />
        <label class="text-xs text-gray-400">Til</label>
        <input
          v-model="toDate"
          type="date"
          class="bg-gray-900 border border-gray-600 rounded-lg px-3 py-1.5 text-sm text-gray-200"
        />
      </div>

      <div class="flex items-center gap-2">
        <label class="text-xs text-gray-400">Gruppering</label>
        <select
          v-model="groupBy"
          class="bg-gray-900 border border-gray-600 rounded-lg px-3 py-1.5 text-sm text-gray-200"
        >
          <option value="day">Per dag</option>
          <option value="hour">Per time</option>
        </select>
      </div>
    </div>

    <div v-if="loading" class="text-center text-gray-500 py-16">Laster…</div>

    <template v-else-if="traffic">
      <!-- KPI cards -->
      <div class="grid grid-cols-2 lg:grid-cols-3 gap-4 mb-8">
        <div class="bg-gray-800 rounded-xl border border-gray-700 p-5">
          <div class="text-xs font-semibold uppercase tracking-wider text-gray-400 mb-1">Sidevisninger</div>
          <div class="text-3xl font-bold text-gray-100">{{ traffic.totalPageViews.toLocaleString() }}</div>
        </div>
        <div class="bg-gray-800 rounded-xl border border-gray-700 p-5">
          <div class="text-xs font-semibold uppercase tracking-wider text-gray-400 mb-1">Unike sesjoner</div>
          <div class="text-3xl font-bold text-blue-400">{{ traffic.totalSessions.toLocaleString() }}</div>
        </div>
        <div class="bg-gray-800 rounded-xl border border-gray-700 p-5">
          <div class="text-xs font-semibold uppercase tracking-wider text-gray-400 mb-1">Nye besøkende</div>
          <div class="text-3xl font-bold text-green-400">{{ traffic.newSessions.toLocaleString() }}</div>
        </div>
      </div>

      <!-- Sessions over time chart (horizontal bar chart, simple CSS) -->
      <div class="bg-gray-800 rounded-xl border border-gray-700 mb-8">
        <div class="px-5 py-4 border-b border-gray-700">
          <h2 class="text-base font-semibold text-gray-100">Sesjoner over tid</h2>
        </div>

        <div v-if="traffic.buckets.length === 0" class="px-5 py-8 text-center text-gray-500">
          Ingen data for valgt periode.
        </div>

        <div v-else class="px-5 py-4 space-y-1 overflow-auto" style="max-height: 480px">
          <!-- Legend -->
          <div class="flex gap-6 text-xs text-gray-400 mb-3">
            <span class="flex items-center gap-1.5">
              <span class="inline-block w-3 h-3 rounded-sm bg-blue-500"></span> Unike sesjoner
            </span>
            <span class="flex items-center gap-1.5">
              <span class="inline-block w-3 h-3 rounded-sm bg-green-500"></span> Nye besøkende
            </span>
          </div>

          <div
            v-for="bucket in traffic.buckets"
            :key="bucket.bucketUtc"
            class="flex items-center gap-3 py-1"
          >
            <span class="text-xs text-gray-400 w-32 shrink-0 text-right">{{ bucket.label }}</span>
            <div class="flex-1 space-y-1">
              <!-- Sessions bar -->
              <div class="flex items-center gap-2">
                <div
                  class="h-4 rounded-sm bg-blue-500 min-w-[2px] transition-all"
                  :style="{ width: barWidth(bucket.uniqueSessions, maxSessions) + 'px' }"
                ></div>
                <span class="text-xs text-gray-300">{{ bucket.uniqueSessions }}</span>
              </div>
              <!-- New sessions bar -->
              <div class="flex items-center gap-2">
                <div
                  class="h-2 rounded-sm bg-green-500 min-w-[2px] transition-all"
                  :style="{ width: barWidth(bucket.newSessions, maxSessions) + 'px' }"
                ></div>
                <span class="text-xs text-gray-500">{{ bucket.newSessions }} nye</span>
              </div>
            </div>
            <span class="text-xs text-gray-500 w-16 text-right">{{ bucket.pageViews }} visn.</span>
          </div>
        </div>
      </div>

      <!-- Referrer breakdown -->
      <div v-if="referrers" class="bg-gray-800 rounded-xl border border-gray-700">
        <div class="px-5 py-4 border-b border-gray-700">
          <h2 class="text-base font-semibold text-gray-100">Trafikkilder</h2>
          <p class="text-xs text-gray-400 mt-0.5">Sesjoner gruppert etter henvisende nettsted</p>
        </div>

        <div v-if="referrers.sources.length === 0" class="px-5 py-8 text-center text-gray-500">
          Ingen data for valgt periode.
        </div>

        <div v-else class="p-5 space-y-3">
          <!-- Stacked progress bar -->
          <div class="flex h-4 rounded-full overflow-hidden w-full mb-6">
            <div
              v-for="src in referrers.sources"
              :key="src.source"
              :style="{
                width: referrerPercent(src.sessions),
                backgroundColor: colourFor(src.source),
              }"
              :title="src.source + ': ' + src.sessions + ' sesjoner'"
            ></div>
          </div>

          <!-- Table -->
          <div
            v-for="src in referrers.sources"
            :key="src.source"
            class="flex items-center gap-3"
          >
            <span
              class="inline-block w-3 h-3 rounded-sm shrink-0"
              :style="{ backgroundColor: colourFor(src.source) }"
            ></span>
            <span class="text-sm text-gray-200 w-32">{{ src.source }}</span>
            <div class="flex-1 bg-gray-900 rounded-full h-3 overflow-hidden">
              <div
                class="h-full rounded-full transition-all"
                :style="{
                  width: referrerPercent(src.sessions),
                  backgroundColor: colourFor(src.source),
                }"
              ></div>
            </div>
            <span class="text-sm text-gray-300 w-20 text-right">{{ src.sessions }} ses.</span>
            <span class="text-xs text-gray-500 w-12 text-right">{{ referrerPercent(src.sessions) }}</span>
          </div>
        </div>
      </div>
    </template>
  </div>
</template>

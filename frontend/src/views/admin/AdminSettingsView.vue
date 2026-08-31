<script setup lang="ts">
import { computed, ref, onMounted } from 'vue'
import apiClient from '@/api/client'

interface SystemSetting {
  id: number
  key: string
  label: string
  isSensitive: boolean
  value: string
}

const settings = ref<SystemSetting[]>([])
const loading = ref(false)
const error = ref('')

// Per-row edit state
const editValues = ref<Record<string, string>>({})
const editing = ref<Record<string, boolean>>({})
const saving = ref<Record<string, boolean>>({})
const saveError = ref<Record<string, string>>({})
const saveSuccess = ref<Record<string, boolean>>({})

interface ClaudeOAuthStatus {
  isConfigured: boolean
  canRefresh: boolean
  source: 'database' | 'environment' | 'none'
  expiresAt: string | null
}

const claudeOAuthStatus = ref<ClaudeOAuthStatus | null>(null)
const claudeOAuthCode = ref('')
const claudeOAuthPending = ref(false)
const claudeOAuthBusy = ref(false)
const claudeOAuthError = ref('')
const managedClaudeKeys = new Set([
  'claude_code_oauth_refresh_token',
  'claude_code_oauth_expires_at',
])
const visibleSettings = computed(() => settings.value.filter((s) => !managedClaudeKeys.has(s.key)))

async function load() {
  loading.value = true
  error.value = ''
  try {
    const { data } = await apiClient.get<SystemSetting[]>('/admin/settings')
    settings.value = data
    data.forEach((s) => {
      // For sensitive fields, don't pre-fill — force a fresh entry
      editValues.value[s.key] = s.isSensitive ? '' : s.value
    })
    await loadClaudeOAuthStatus()
  } catch {
    error.value = 'Kunne ikke laste innstillinger.'
  } finally {
    loading.value = false
  }
}

async function loadClaudeOAuthStatus() {
  try {
    const { data } = await apiClient.get<ClaudeOAuthStatus>('/admin/settings/claude-oauth/status')
    claudeOAuthStatus.value = data
  } catch {
    claudeOAuthStatus.value = null
  }
}

async function startClaudeOAuth() {
  claudeOAuthBusy.value = true
  claudeOAuthError.value = ''
  try {
    const { data } = await apiClient.post<{ authorizationUrl: string }>('/admin/settings/claude-oauth/start')
    claudeOAuthPending.value = true
    window.open(data.authorizationUrl, '_blank', 'noopener,noreferrer')
  } catch (err: any) {
    claudeOAuthError.value = err.response?.data?.error ?? 'Kunne ikke starte Claude-tilkoblingen.'
  } finally {
    claudeOAuthBusy.value = false
  }
}

async function completeClaudeOAuth() {
  if (!claudeOAuthCode.value.trim()) return
  claudeOAuthBusy.value = true
  claudeOAuthError.value = ''
  try {
    const { data } = await apiClient.post<ClaudeOAuthStatus>('/admin/settings/claude-oauth/complete', {
      code: claudeOAuthCode.value.trim(),
    })
    claudeOAuthStatus.value = data
    claudeOAuthCode.value = ''
    claudeOAuthPending.value = false
    await load()
  } catch (err: any) {
    claudeOAuthError.value = err.response?.data?.error ?? 'Kunne ikke fullføre Claude-tilkoblingen.'
  } finally {
    claudeOAuthBusy.value = false
  }
}

function formatOAuthExpiry(value: string | null) {
  if (!value) return ''
  return new Intl.DateTimeFormat('nb-NO', { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(value))
}

function startEdit(s: SystemSetting) {
  editValues.value[s.key] = s.isSensitive ? '' : s.value
  editing.value[s.key] = true
  saveError.value[s.key] = ''
  saveSuccess.value[s.key] = false
}

function cancelEdit(s: SystemSetting) {
  editing.value[s.key] = false
  saveError.value[s.key] = ''
}

function isPromptSetting(s: SystemSetting) {
  return s.key.startsWith('claude_flux_')
}

async function saveSetting(s: SystemSetting) {
  saving.value[s.key] = true
  saveError.value[s.key] = ''
  saveSuccess.value[s.key] = false
  try {
    const { data } = await apiClient.put<SystemSetting>(`/admin/settings/${s.key}`, {
      value: editValues.value[s.key] ?? '',
    })
    // Update local state
    const idx = settings.value.findIndex((x) => x.key === s.key)
    if (idx !== -1) settings.value[idx] = data
    if (s.key === 'claude_code_oauth_token') await loadClaudeOAuthStatus()
    editing.value[s.key] = false
    saveSuccess.value[s.key] = true
    setTimeout(() => { saveSuccess.value[s.key] = false }, 3000)
  } catch (err: any) {
    saveError.value[s.key] = err.response?.data?.error ?? 'Lagring feilet.'
  } finally {
    saving.value[s.key] = false
  }
}

onMounted(load)
</script>

<template>
  <div class="max-w-3xl mx-auto px-4 py-10">
    <div class="mb-6">
      <h1 class="text-2xl font-bold text-white">Systeminnstillinger</h1>
      <p class="text-gray-400 text-sm mt-1">
        Konfigurer API-nøkler og andre runtime-innstillinger. Endringer trer i kraft umiddelbart uten omstart.
      </p>
    </div>

    <p v-if="loading" class="text-gray-400">Laster…</p>
    <p v-else-if="error" class="text-red-400">{{ error }}</p>

    <div v-else class="space-y-4">
      <div
        v-for="s in visibleSettings"
        :key="s.key"
        class="bg-gray-800 rounded-xl border border-gray-700 p-5"
      >
        <div class="flex items-start justify-between gap-4">
          <div class="flex-1 min-w-0">
            <div class="flex items-center gap-2 mb-1">
              <span class="font-semibold text-gray-100">{{ s.label }}</span>
              <span class="text-xs font-mono text-gray-500 bg-gray-900 px-1.5 py-0.5 rounded">{{ s.key }}</span>
              <span v-if="s.isSensitive" class="text-xs text-yellow-500 bg-yellow-900/30 px-1.5 py-0.5 rounded">
                🔒 Sensitiv
              </span>
            </div>

            <!-- Current status when not editing -->
            <div v-if="!editing[s.key]" class="mt-1">
              <span
                v-if="s.value && s.value !== ''"
                class="text-sm text-green-400"
              >
                ✓ {{
                  s.isSensitive
                    ? 'Nøkkel er satt'
                    : isPromptSetting(s)
                      ? `Prompt er satt (${s.value.length} tegn)`
                      : s.value
                }}
              </span>
              <span v-else class="text-sm text-orange-400">
                ⚠ Ikke konfigurert
              </span>
              <span
                v-if="saveSuccess[s.key]"
                class="ml-3 text-sm text-green-400 animate-pulse"
              >Lagret!</span>
            </div>

            <div
              v-if="s.key === 'claude_code_oauth_token'"
              class="mt-4 rounded-lg border border-gray-700 bg-gray-900/70 p-4"
            >
              <p class="text-sm text-gray-300">
                <template v-if="claudeOAuthStatus?.canRefresh">
                  ✓ OAuth er tilkoblet og fornyes automatisk
                  <span v-if="claudeOAuthStatus.expiresAt" class="text-gray-500">
                    (neste utløp {{ formatOAuthExpiry(claudeOAuthStatus.expiresAt) }})
                  </span>
                </template>
                <template v-else-if="claudeOAuthStatus?.source === 'environment'">
                  ✓ Token hentes fra tjenestens miljøvariabel
                </template>
                <template v-else-if="claudeOAuthStatus?.isConfigured">
                  ✓ Manuelt langtids-token er konfigurert
                </template>
                <template v-else>
                  Koble til en Claude-konto for å hente og fornye token automatisk.
                </template>
              </p>
              <button
                type="button"
                :disabled="claudeOAuthBusy"
                class="mt-3 rounded-md bg-violet-600 px-3 py-2 text-sm font-medium text-white hover:bg-violet-500 disabled:opacity-60"
                @click="startClaudeOAuth"
              >
                {{ claudeOAuthBusy ? 'Venter…' : (claudeOAuthStatus?.isConfigured ? 'Koble til på nytt' : 'Koble til Claude') }}
              </button>

              <div v-if="claudeOAuthPending" class="mt-3 space-y-2">
                <p class="text-xs text-gray-400">
                  Fullfør innloggingen i den nye fanen, kopier hele koden Claude viser (kode#state), og lim den inn her.
                </p>
                <div class="flex flex-col gap-2 sm:flex-row">
                  <input
                    v-model="claudeOAuthCode"
                    type="password"
                    autocomplete="off"
                    placeholder="kode#state"
                    class="min-w-0 flex-1 rounded-lg border border-violet-500 bg-gray-950 px-3 py-2 text-sm text-gray-100 focus:outline-none focus:ring-2 focus:ring-violet-500"
                    @keyup.enter="completeClaudeOAuth"
                  />
                  <button
                    type="button"
                    :disabled="claudeOAuthBusy || !claudeOAuthCode.trim()"
                    class="rounded-md bg-green-600 px-3 py-2 text-sm font-medium text-white hover:bg-green-500 disabled:opacity-60"
                    @click="completeClaudeOAuth"
                  >Fullfør</button>
                </div>
              </div>
              <p v-if="claudeOAuthError" class="mt-2 text-xs text-red-400">{{ claudeOAuthError }}</p>
            </div>

            <!-- Edit field -->
            <div v-else class="mt-2">
              <textarea
                v-if="isPromptSetting(s)"
                v-model="editValues[s.key]"
                rows="8"
                placeholder="Prompt"
                class="w-full bg-gray-900 border border-blue-500 text-gray-100 rounded-lg px-3 py-2 text-sm font-mono leading-relaxed focus:outline-none focus:ring-2 focus:ring-blue-500"
                @keyup.escape="cancelEdit(s)"
              />
              <input
                v-else
                v-model="editValues[s.key]"
                :type="s.isSensitive ? 'password' : 'text'"
                :placeholder="s.isSensitive ? 'Skriv inn ny nøkkel…' : 'Verdi'"
                class="w-full bg-gray-900 border border-blue-500 text-gray-100 rounded-lg px-3 py-2 text-sm font-mono focus:outline-none focus:ring-2 focus:ring-blue-500"
                @keyup.enter="saveSetting(s)"
                @keyup.escape="cancelEdit(s)"
              />
              <p v-if="saveError[s.key]" class="text-red-400 text-xs mt-1">{{ saveError[s.key] }}</p>
            </div>
          </div>

          <!-- Action buttons -->
          <div class="flex items-center gap-2 shrink-0 pt-1">
            <template v-if="editing[s.key]">
              <button
                @click="saveSetting(s)"
                :disabled="saving[s.key]"
                class="text-sm text-green-400 hover:text-green-300 font-medium disabled:opacity-60"
              >{{ saving[s.key] ? 'Lagrer…' : 'Lagre' }}</button>
              <button
                @click="cancelEdit(s)"
                class="text-sm text-gray-400 hover:text-gray-300"
              >Avbryt</button>
            </template>
            <button
              v-else
              @click="startEdit(s)"
              class="text-sm text-blue-400 hover:text-blue-300 font-medium"
            >Rediger</button>
          </div>
        </div>
      </div>
    </div>

    <!-- Help section -->
    <div class="mt-8 bg-gray-900 rounded-xl border border-gray-700 p-5 text-sm text-gray-400">
      <h2 class="text-gray-200 font-semibold mb-2">Hjelp</h2>
      <ul class="space-y-1.5 list-disc list-inside">
        <li>
          <strong class="text-gray-300">fal_api_key</strong>: Nøkkel fra
          <a href="https://fal.ai/dashboard/keys" target="_blank" class="text-blue-400 hover:underline">
            fal.ai/dashboard/keys
          </a>. Brukes til bildegenerering med <code class="bg-gray-800 px-1 rounded">fal-ai/flux-2-pro</code>.
        </li>
        <li>
          <strong class="text-gray-300">claude_code_oauth_token</strong>: Bruk «Koble til Claude» for
          OAuth med automatisk tokenfornyelse. Alternativt kan et langlivet token fra
          <code class="bg-gray-800 px-1 rounded">claude setup-token</code> lagres manuelt, eller settes som
          <code class="bg-gray-800 px-1 rounded">CLAUDE_CODE_OAUTH_TOKEN</code> i tjenestens miljø.
        </li>
        <li>
          <strong class="text-gray-300">claude_flux_*</strong>: Hovedinstruksjonen og én justerbar
          art direction per bannertype. Tom verdi bruker den innebygde standardteksten.
        </li>
        <li>
          <strong class="text-gray-300">openai_api_key</strong>: Nøkkel fra
          <a href="https://platform.openai.com/api-keys" target="_blank" class="text-blue-400 hover:underline">
            platform.openai.com/api-keys
          </a>. Beholdes for eldre OpenAI-funksjoner; bannerprompten forbedres nå av Claude CLI.
          Starter med <code class="bg-gray-800 px-1 rounded">sk-</code>.
        </li>
        <li>
          <strong class="text-gray-300">stripe_secret_key</strong>: Hemmelig nøkkel fra
          <a href="https://dashboard.stripe.com/apikeys" target="_blank" class="text-blue-400 hover:underline">
            dashboard.stripe.com/apikeys
          </a>. Starter med <code class="bg-gray-800 px-1 rounded">sk_live_</code>,
          <code class="bg-gray-800 px-1 rounded">sk_test_</code>,
          <code class="bg-gray-800 px-1 rounded">rk_live_</code> eller
          <code class="bg-gray-800 px-1 rounded">rk_test_</code> (begrenset nøkkel).
        </li>
        <li>
          <strong class="text-gray-300">stripe_publishable_key</strong>: Offentlig nøkkel brukt av nettleseren.
          Starter med <code class="bg-gray-800 px-1 rounded">pk_live_</code> eller
          <code class="bg-gray-800 px-1 rounded">pk_test_</code>. Trenger ikke ombygging av frontend.
        </li>
        <li>
          <strong class="text-gray-300">stripe_webhook_secret</strong>: Webhook-signeringshemmelighet fra
          Stripe-dashbordet under Developers → Webhooks. Starter med
          <code class="bg-gray-800 px-1 rounded">whsec_</code>.
        </li>
      </ul>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed, ref, onMounted } from 'vue'
import apiClient from '@/api/client'
import ImageCliConnections from '@/components/admin/ImageCliConnections.vue'

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
const claudeOAuthLinkOpened = ref(false)
const claudeOAuthAuthorizationUrl = ref('')
const claudeOAuthExpiresAt = ref<string | null>(null)
const claudeOAuthPending = computed(() => !!claudeOAuthAuthorizationUrl.value)
const claudeOAuthBusy = ref(false)
const claudeOAuthError = ref('')
const managedClaudeKeys = new Set([
  'claude_code_oauth_token',
  'codex_image_cli_credentials',
  'grok_image_cli_credentials',
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
  if (claudeOAuthBusy.value) return
  // Give the admin an ordinary link, not an automatic popup or a device code.
  claudeOAuthLinkOpened.value = false
  claudeOAuthBusy.value = true
  claudeOAuthError.value = ''
  claudeOAuthCode.value = ''
  claudeOAuthAuthorizationUrl.value = ''
  claudeOAuthExpiresAt.value = null
  try {
    const { data } = await apiClient.post<{ authorizationUrl: string; expiresAt: string }>('/admin/settings/claude-oauth/start')
    claudeOAuthAuthorizationUrl.value = data.authorizationUrl
    claudeOAuthExpiresAt.value = data.expiresAt
  } catch (err: any) {
    claudeOAuthError.value = err.response?.data?.error ?? 'Kunne ikke starte Claude-tilkoblingen.'
  } finally {
    claudeOAuthBusy.value = false
  }
}

function cancelClaudeOAuth() {
  claudeOAuthLinkOpened.value = false
  claudeOAuthCode.value = ''
  claudeOAuthAuthorizationUrl.value = ''
  claudeOAuthExpiresAt.value = null
  claudeOAuthError.value = ''
}

async function completeClaudeOAuth() {
  if (claudeOAuthBusy.value || !claudeOAuthPending.value || !claudeOAuthCode.value.trim()) return
  claudeOAuthBusy.value = true
  claudeOAuthError.value = ''
  try {
    const { data } = await apiClient.post<ClaudeOAuthStatus>('/admin/settings/claude-oauth/complete', {
      code: claudeOAuthCode.value.trim(),
    })
    claudeOAuthStatus.value = data
    cancelClaudeOAuth()
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
      <section data-testid="claude-oauth" class="bg-gray-800 rounded-xl border border-gray-700 p-5" aria-labelledby="claude-oauth-heading">
        <h2 id="claude-oauth-heading" class="font-semibold text-gray-100">Claude – innlogging</h2>
        <p class="mt-1 text-sm text-gray-400">
          Koble til Claude-kontoen som brukes til å forbedre bannerpromptene.
          Åpne lenken til Claude, klikk «Godkjenn» der, og lim deretter inn bekreftelseskoden du får tilbake her.
        </p>
        <p class="mt-3 text-sm text-gray-300" role="status">
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
          <template v-else>Ingen Claude-konto tilkoblet</template>
        </p>
        <button
          type="button"
          :disabled="claudeOAuthBusy"
          class="mt-3 rounded-md bg-violet-600 px-3 py-2 text-sm font-medium text-white hover:bg-violet-500 disabled:opacity-60"
          @click="startClaudeOAuth"
        >
          {{ claudeOAuthBusy ? 'Venter…' : claudeOAuthPending ? 'Start innlogging på nytt' : claudeOAuthStatus?.isConfigured ? 'Koble til på nytt' : 'Koble til Claude' }}
        </button>
        <div v-if="claudeOAuthPending" class="mt-4 space-y-3">
          <div class="text-sm text-gray-300 space-y-1">
            <p>1. Klikk på lenken og godkjenn tilgangen hos Claude.</p>
            <a
              data-testid="claude-oauth-link"
              :href="claudeOAuthAuthorizationUrl"
              @click="claudeOAuthLinkOpened = true"
              @auxclick="claudeOAuthLinkOpened = true"
              @contextmenu="claudeOAuthLinkOpened = true"
              target="_blank"
              rel="noopener noreferrer"
              class="inline-block text-violet-300 underline"
            >Åpne Claude og godkjenn tilgang ↗</a>
            <p class="text-xs text-gray-400">Dette er ikke en enhetskode-innlogging. Du får bekreftelseskoden fra Claude etter at du har godkjent tilgangen.</p>
            <p class="text-xs text-gray-400">Lenken utløper {{ formatOAuthExpiry(claudeOAuthExpiresAt) }}.</p>
          </div>
          <form v-if="claudeOAuthLinkOpened" class="space-y-2" @submit.prevent="completeClaudeOAuth">
            <label for="claude-oauth-code" class="block text-sm text-gray-300">
              2. Lim inn bekreftelseskoden fra Claude
            </label>
            <p id="claude-oauth-code-help" class="text-xs text-gray-400">
              Etter at du har klikket «Godkjenn» hos Claude: kopier hele bekreftelseskoden og lim den inn her, uten å endre den.
            </p>
            <div class="flex flex-col gap-2 sm:flex-row">
              <input
                id="claude-oauth-code"
                v-model="claudeOAuthCode"
                type="password"
                autocomplete="off"
                :spellcheck="false"
                autocapitalize="none"
                aria-describedby="claude-oauth-code-help"
                placeholder="Bekreftelseskoden du fikk fra Claude"
                :disabled="claudeOAuthBusy"
                class="min-w-0 flex-1 rounded-lg border border-violet-500 bg-gray-950 px-3 py-2 text-sm text-gray-100 focus:outline-none focus:ring-2 focus:ring-violet-500"
              />
              <button
                type="submit"
                :disabled="claudeOAuthBusy || !claudeOAuthCode.trim()"
                class="rounded-md bg-green-600 px-3 py-2 text-sm font-medium text-white hover:bg-green-500 disabled:opacity-60"
              >Fullfør tilkobling</button>
            </div>
          </form>
          <button type="button" :disabled="claudeOAuthBusy" class="text-sm text-gray-400 underline disabled:opacity-60" @click="cancelClaudeOAuth">Avbryt</button>
        </div>
        <p v-if="claudeOAuthError" class="mt-2 text-sm text-red-400" role="alert">{{ claudeOAuthError }}</p>
      </section>
      <ImageCliConnections />
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
          <strong class="text-gray-300">Claude</strong>: Bruk «Koble til Claude» øverst på siden.
          Åpne lenken, godkjenn hos Claude og lim inn bekreftelseskoden her.
          Token lagres sikkert og fornyes automatisk; du trenger ikke å opprette eller redigere et token selv.
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

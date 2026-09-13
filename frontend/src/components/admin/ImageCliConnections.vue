<script setup lang="ts">
import { onMounted, onUnmounted, ref } from 'vue'
import apiClient from '@/api/client'
import { isAxiosError } from 'axios'

type Provider = 'codex' | 'grok'
interface Status {
  isConfigured: boolean
  pending: boolean
  authorizationUrl: string | null
  userCode: string | null
  error: string | null
  loginMethod: 'device' | 'oauth'
}
const providers: Provider[] = ['codex', 'grok']
const statuses = ref<Partial<Record<Provider, Status>>>({})
const busy = ref<Partial<Record<Provider, boolean>>>({})
const errors = ref<Partial<Record<Provider, string>>>({})
const oauthCode = ref('')
const submitted = ref(false)
let timer: ReturnType<typeof setTimeout> | undefined
let disposed = false
async function refresh() {
  await Promise.all(providers.map(async (provider) => {
    try {
      const { data } = await apiClient.get<Status>(`/admin/settings/image-cli/${provider}/status`)
      if (!disposed) {
        statuses.value[provider] = data
        if (provider === 'grok' && !data.pending) oauthCode.value = ''
      }
    } catch {
      if (!disposed) errors.value[provider] = 'Kunne ikke hente tilkoblingsstatus.'
    }
  }))
  if (!disposed) timer = setTimeout(refresh, 2500)
}
async function start(provider: Provider, method = provider === 'grok' ? 'oauth' : 'device') {
  busy.value[provider] = true
  errors.value[provider] = ''
  if (provider === 'grok') { oauthCode.value = ''; submitted.value = false }
  try {
    const { data } = await apiClient.post<Status>(`/admin/settings/image-cli/${provider}/start`, null, { params: { method } })
    statuses.value[provider] = data
  } catch {
    errors.value[provider] = 'Kunne ikke starte tilkoblingen.'
  } finally { busy.value[provider] = false }
}
async function completeGrokOAuth() {
  if (!oauthCode.value.trim() || busy.value.grok || submitted.value) return
  busy.value.grok = true
  errors.value.grok = ''
  try {
    const { data } = await apiClient.post<Status>('/admin/settings/image-cli/grok/complete', { code: oauthCode.value.trim() })
    statuses.value.grok = data
    oauthCode.value = ''
    submitted.value = true
  } catch (error) {
    errors.value.grok = isAxiosError(error) ? error.response?.data?.error ?? 'Kunne ikke fullføre innloggingen.' : 'Kunne ikke fullføre innloggingen.'
  } finally { busy.value.grok = false }
}
async function cancel(provider: Provider) {
  if (provider === 'grok') oauthCode.value = ''
  try { await apiClient.post(`/admin/settings/image-cli/${provider}/cancel`) }
  catch { errors.value[provider] = 'Kunne ikke avbryte tilkoblingen.' }
}
onMounted(refresh)
onUnmounted(() => { disposed = true; clearTimeout(timer) })
</script>

<template>
  <section class="bg-gray-800 rounded-xl border border-gray-700 p-5 space-y-4">
    <h2 class="font-semibold text-gray-100">Bildegenerering med Codex og Grok</h2>
    <p class="text-sm text-gray-400">Koble til begge for å gi kunden to alternativer. Hvis bare én leverer et gyldig bilde, vises dette som vanlig.</p>
    <div v-for="provider in providers" :key="provider" class="border-t border-gray-700 pt-3 space-y-2">
      <h3 class="text-white font-semibold">{{ provider === 'codex' ? 'Codex / ChatGPT' : 'Grok' }}</h3>
      <p class="text-sm" :class="statuses[provider]?.isConfigured ? 'text-green-400' : 'text-gray-400'">
        {{ statuses[provider]?.isConfigured ? 'Konto tilkoblet' : 'Ingen konto tilkoblet' }}
      </p>
      <p class="text-xs text-gray-400">Kontoen må ha tilgang til bildegenerering. Innlogging og tokenfornyelse håndteres av leverandørens CLI.</p>
      <button v-if="!statuses[provider]?.pending" type="button" :disabled="busy[provider]"
        class="bg-blue-600 hover:bg-blue-500 disabled:opacity-50 text-white rounded px-3 py-2 text-sm" @click="start(provider)">
        {{ busy[provider] ? 'Starter…' : provider === 'grok' ? 'Koble til Grok med OAuth' : statuses[provider]?.isConfigured ? 'Koble til på nytt' : 'Koble til konto' }}
      </button>
      <button v-if="provider === 'grok' && !statuses[provider]?.pending" type="button" :disabled="busy[provider]"
        class="ml-3 text-sm text-gray-400 underline disabled:opacity-50" @click="start(provider, 'device')">Bruk enhetskode i stedet</button>
      <div v-if="statuses[provider]?.pending" class="space-y-2 text-sm text-gray-300" aria-live="polite">
        <template v-if="statuses[provider]?.authorizationUrl">
          <a :href="statuses[provider]!.authorizationUrl!" target="_blank" rel="noopener noreferrer" class="text-blue-400 underline">Åpne innloggingssiden</a>
          <p v-if="statuses[provider]?.userCode">Bekreft denne koden: <strong class="font-mono text-white select-all">{{ statuses[provider]?.userCode }}</strong></p>
          <template v-if="provider === 'grok' && statuses[provider]?.loginMethod === 'oauth'">
            <p>Fullfør innloggingen hos Grok. Kopier koden som vises, eller hele returadressen fra adressefeltet hvis nettleseren ikke får kontakt med 127.0.0.1.</p>
            <form v-if="!submitted" class="space-y-2" @submit.prevent="completeGrokOAuth">
              <label for="grok-oauth-code" class="block">Kode eller returadresse fra Grok</label>
              <input id="grok-oauth-code" v-model="oauthCode" type="text" autocomplete="off" :spellcheck="false" maxlength="8192"
                class="w-full rounded border border-gray-600 bg-gray-900 px-3 py-2 text-white" placeholder="Lim inn kode eller http://127.0.0.1:…/callback?…" />
              <button type="submit" :disabled="busy.grok || !oauthCode.trim()"
                class="rounded bg-blue-600 px-3 py-2 text-white disabled:opacity-50">{{ busy.grok ? 'Kobler til…' : 'Fullfør tilkobling' }}</button>
            </form>
            <p v-else>Koden er sendt. Venter på bekreftelse fra Grok…</p>
          </template>
          <p v-else>Fullfør innloggingen i den nye fanen. Status oppdateres automatisk.</p>
        </template>
        <p v-else>Venter på innloggingslenke…</p>
        <button type="button" class="text-gray-400 underline" @click="cancel(provider)">Avbryt</button>
      </div>
      <p v-if="errors[provider] || statuses[provider]?.error" class="text-sm text-red-400" role="alert">{{ errors[provider] || statuses[provider]?.error }}</p>
    </div>
  </section>
</template>

<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import apiClient from '@/api/client'

interface Prompt {
  id: string
  group: string
  title: string
  description: string
  builtIn: string
  effective: string
  settingKey: string | null
}
interface AgentModel { agent: string; role: string; model: string; versionNote: string }
const prompts = ref<Prompt[]>([])
const models = ref<AgentModel[]>([])
const loading = ref(true)
const error = ref('')
const groups = computed(() => [...new Set(prompts.value.map((p) => p.group))])

async function load() {
  loading.value = true
  error.value = ''
  try {
    const { data } = await apiClient.get<{ models: AgentModel[]; prompts: Prompt[] }>('/admin/settings/prompts')
    prompts.value = data.prompts
    models.value = data.models
  } catch {
    error.value = 'Kunne ikke laste promptene. Prøv igjen.'
  } finally {
    loading.value = false
  }
}
onMounted(load)
</script>

<template>
  <div class="max-w-5xl mx-auto px-4 py-10 text-gray-100">
    <RouterLink to="/admin/settings" class="text-indigo-300 hover:underline">← Systeminnstillinger</RouterLink>
    <h1 class="mt-4 text-2xl font-bold">AI-prompter og modeller</h1>
    <p class="mt-2 text-gray-400">
      Les alle appens innebygde instruksjoner i den aktive bannerflyten: grunnprompt → Claude → felles krav → Codex / Grok.
      Plassholdere i klammer erstattes med kundedata. Dette er ikke en kundes genererte prompt.
      Interne instruksjoner hos CLI-/modellleverandørene er ikke tilgjengelige her.
    </p>
    <p class="mt-2 text-sm text-gray-400">
      Oversikten er skrivebeskyttet. Bruk seksjonens navn når du foreslår endringer.
      Claude-innstillinger kan fortsatt redigeres under systeminnstillinger; øvrige instrukser endres i kildekoden.
    </p>
    <p v-if="loading" class="mt-6" role="status">Laster…</p>
    <div v-else-if="error" class="mt-6" role="alert">
      <p class="text-red-400">{{ error }}</p>
      <button type="button" class="mt-2 text-indigo-300 underline" @click="load">Prøv igjen</button>
    </div>
    <template v-else>
      <section class="mt-8" aria-labelledby="models-heading">
        <h2 id="models-heading" class="text-xl font-semibold">Modeller per agent</h2>
        <div class="mt-3 grid gap-4 md:grid-cols-3">
          <article v-for="model in models" :key="model.agent" class="rounded-xl border border-gray-700 bg-gray-800 p-4">
            <h3 class="font-semibold">{{ model.agent }} – {{ model.role }}</h3>
            <p class="mt-2 font-mono text-sm break-words">{{ model.model }}</p>
            <p class="mt-2 text-sm text-gray-400">{{ model.versionNote }}</p>
          </article>
        </div>
        <p class="mt-2 text-sm text-gray-400">CLI-programversjon og modellversjon er forskjellige ting. Ukjente modellversjoner vises ikke som sikre versjonsnumre.</p>
      </section>
      <section v-for="group in groups" :key="group" class="mt-8">
        <h2 class="text-xl font-semibold">{{ group }}</h2>
        <details v-for="prompt in prompts.filter((p) => p.group === group)" :key="prompt.id" class="mt-3 rounded-xl border border-gray-700 bg-gray-800 p-4">
          <summary class="cursor-pointer font-semibold">
            {{ prompt.title }}
            <span v-if="prompt.effective !== prompt.builtIn" class="ml-2 text-xs text-amber-300">Lagret overstyring i bruk</span>
          </summary>
          <p class="mt-3 text-sm text-gray-400">{{ prompt.description }}</p>
          <p v-if="prompt.settingKey" class="mt-2 text-xs text-gray-400 break-all">Innstilling: {{ prompt.settingKey }}</p>
          <h3 class="mt-4 text-sm font-semibold">Innebygd standard</h3>
          <pre class="mt-2 whitespace-pre-wrap break-words rounded bg-gray-900 p-4 text-sm font-mono">{{ prompt.builtIn }}</pre>
          <template v-if="prompt.effective !== prompt.builtIn">
            <h3 class="mt-4 text-sm font-semibold text-amber-300">Aktiv lagret prompt</h3>
            <pre class="mt-2 whitespace-pre-wrap break-words rounded bg-gray-900 p-4 text-sm font-mono">{{ prompt.effective }}</pre>
          </template>
          <p v-else class="mt-2 text-xs text-gray-400">Innebygd standard er i bruk.</p>
        </details>
      </section>
    </template>
  </div>
</template>

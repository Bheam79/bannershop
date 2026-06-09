<script setup lang="ts">
import { ref, onMounted } from 'vue'
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
  } catch {
    error.value = 'Kunne ikke laste innstillinger.'
  } finally {
    loading.value = false
  }
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
        v-for="s in settings"
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
                ✓ {{ s.isSensitive ? 'Nøkkel er satt' : s.value }}
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
              <input
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
          <strong class="text-gray-300">openai_api_key</strong>: Nøkkel fra
          <a href="https://platform.openai.com/api-keys" target="_blank" class="text-blue-400 hover:underline">
            platform.openai.com/api-keys
          </a>. Brukes for AI-banner-generering. Starter med <code class="bg-gray-800 px-1 rounded">sk-</code>.
        </li>
        <li>
          <strong class="text-gray-300">openai_image_model</strong>: La stå tom for å bruke standard (<code class="bg-gray-800 px-1 rounded">gpt-image-2</code>).
        </li>
        <li>
          <strong class="text-gray-300">openai_image_quality</strong>: Bildekvalitet sendt til OpenAI.
          Gyldige verdier: <code class="bg-gray-800 px-1 rounded">low</code>,
          <code class="bg-gray-800 px-1 rounded">medium</code>,
          <code class="bg-gray-800 px-1 rounded">high</code>,
          <code class="bg-gray-800 px-1 rounded">auto</code>.
          La stå tom for å bruke standardverdien fra appsettings (<code class="bg-gray-800 px-1 rounded">high</code>).
        </li>
      </ul>
    </div>
  </div>
</template>

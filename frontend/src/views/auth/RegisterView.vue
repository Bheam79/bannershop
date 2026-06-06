<script setup lang="ts">
import { ref } from 'vue'
import { useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/auth'
import apiClient from '@/api/client'
import type { AuthResponse } from '@/types'

const router = useRouter()
const auth = useAuthStore()

const name = ref('')
const email = ref('')
const phone = ref('')
const password = ref('')
const confirmPassword = ref('')
const error = ref('')
const loading = ref(false)

async function handleSubmit() {
  error.value = ''
  if (password.value !== confirmPassword.value) {
    error.value = 'Passordene stemmer ikke overens.'
    return
  }
  if (password.value.length < 8) {
    error.value = 'Passordet må være minst 8 tegn.'
    return
  }
  loading.value = true
  try {
    const { data } = await apiClient.post<AuthResponse>('/auth/register', {
      email: email.value,
      password: password.value,
      name: name.value,
      phone: phone.value || null,
    })
    auth.setAuth(data)
    router.push('/account')
  } catch (err: any) {
    error.value = err.response?.data?.error ?? 'Registrering feilet. Prøv igjen.'
  } finally {
    loading.value = false
  }
}
</script>

<template>
  <div class="min-h-[70vh] flex items-center justify-center px-4 py-12">
    <div class="w-full max-w-md bg-white rounded-2xl shadow-lg p-8">
      <h1 class="text-2xl font-bold text-gray-900 mb-2">Opprett konto</h1>
      <p class="text-gray-500 text-sm mb-6">
        Har du allerede konto?
        <RouterLink to="/login" class="text-blue-700 hover:underline font-medium">Logg inn</RouterLink>
      </p>

      <form @submit.prevent="handleSubmit" class="space-y-4">
        <div>
          <label class="block text-sm font-medium text-gray-700 mb-1">Fullt navn <span class="text-red-500">*</span></label>
          <input
            v-model="name"
            type="text"
            required
            autocomplete="name"
            placeholder="Ola Nordmann"
            class="w-full border border-gray-300 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
          />
        </div>

        <div>
          <label class="block text-sm font-medium text-gray-700 mb-1">E-post <span class="text-red-500">*</span></label>
          <input
            v-model="email"
            type="email"
            required
            autocomplete="email"
            placeholder="din@epost.no"
            class="w-full border border-gray-300 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
          />
        </div>

        <div>
          <label class="block text-sm font-medium text-gray-700 mb-1">Telefon (valgfritt)</label>
          <input
            v-model="phone"
            type="tel"
            autocomplete="tel"
            placeholder="+47 900 00 000"
            class="w-full border border-gray-300 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
          />
        </div>

        <div>
          <label class="block text-sm font-medium text-gray-700 mb-1">Passord <span class="text-red-500">*</span></label>
          <input
            v-model="password"
            type="password"
            required
            autocomplete="new-password"
            placeholder="Minst 8 tegn"
            class="w-full border border-gray-300 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
          />
        </div>

        <div>
          <label class="block text-sm font-medium text-gray-700 mb-1">Bekreft passord <span class="text-red-500">*</span></label>
          <input
            v-model="confirmPassword"
            type="password"
            required
            autocomplete="new-password"
            placeholder="••••••••"
            class="w-full border border-gray-300 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
          />
        </div>

        <p v-if="error" class="text-red-600 text-sm bg-red-50 border border-red-200 rounded-lg px-3 py-2">
          {{ error }}
        </p>

        <button
          type="submit"
          :disabled="loading"
          class="w-full bg-blue-700 text-white py-2.5 rounded-lg font-medium hover:bg-blue-800 disabled:opacity-60 transition"
        >
          {{ loading ? 'Oppretter konto…' : 'Opprett konto' }}
        </button>

        <p class="text-xs text-gray-400 text-center">
          Ved å registrere deg godtar du våre vilkår for bruk.
        </p>
      </form>
    </div>
  </div>
</template>

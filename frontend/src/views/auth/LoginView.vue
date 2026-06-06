<script setup lang="ts">
import { ref } from 'vue'
import { useRouter, useRoute } from 'vue-router'
import { useAuthStore } from '@/stores/auth'
import apiClient from '@/api/client'
import type { AuthResponse } from '@/types'

const router = useRouter()
const route = useRoute()
const auth = useAuthStore()

const email = ref('')
const password = ref('')
const error = ref('')
const loading = ref(false)

async function handleSubmit() {
  error.value = ''
  loading.value = true
  try {
    const { data } = await apiClient.post<AuthResponse>('/auth/login', {
      email: email.value,
      password: password.value,
    })
    auth.setAuth(data)
    const redirect = (route.query.redirect as string) || '/'
    router.push(redirect)
  } catch (err: any) {
    error.value = err.response?.data?.error ?? 'Innlogging feilet. Prøv igjen.'
  } finally {
    loading.value = false
  }
}
</script>

<template>
  <div class="min-h-[70vh] flex items-center justify-center px-4">
    <div class="w-full max-w-md bg-white rounded-2xl shadow-lg p-8">
      <h1 class="text-2xl font-bold text-gray-900 mb-2">Logg inn</h1>
      <p class="text-gray-500 text-sm mb-6">
        Har du ikke konto?
        <RouterLink to="/register" class="text-blue-700 hover:underline font-medium">Registrer deg</RouterLink>
      </p>

      <form @submit.prevent="handleSubmit" class="space-y-4">
        <div>
          <label class="block text-sm font-medium text-gray-700 mb-1">E-post</label>
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
          <label class="block text-sm font-medium text-gray-700 mb-1">Passord</label>
          <input
            v-model="password"
            type="password"
            required
            autocomplete="current-password"
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
          {{ loading ? 'Logger inn…' : 'Logg inn' }}
        </button>
      </form>
    </div>
  </div>
</template>

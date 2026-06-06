<script setup lang="ts">
import { ref, reactive } from 'vue'
import { useAuthStore } from '@/stores/auth'
import apiClient from '@/api/client'
import type { User } from '@/types'

const auth = useAuthStore()

// ── Profile form ──────────────────────────────────────────────────────────────
const profile = reactive({
  name: auth.user?.name ?? '',
  phone: auth.user?.phone ?? '',
})
const profileError = ref('')
const profileSuccess = ref('')
const profileLoading = ref(false)

async function saveProfile() {
  profileError.value = ''
  profileSuccess.value = ''
  profileLoading.value = true
  try {
    const { data } = await apiClient.put<User>('/auth/me', {
      name: profile.name,
      phone: profile.phone || null,
    })
    // Update local auth store
    auth.setAuth({
      accessToken: auth.accessToken!,
      refreshToken: auth.refreshTokenValue!,
      user: data,
    })
    profileSuccess.value = 'Profilen er oppdatert.'
  } catch (err: any) {
    profileError.value = err.response?.data?.error ?? 'Kunne ikke oppdatere profilen.'
  } finally {
    profileLoading.value = false
  }
}

// ── Change password form ──────────────────────────────────────────────────────
const pwForm = reactive({
  currentPassword: '',
  newPassword: '',
  confirmPassword: '',
})
const pwError = ref('')
const pwSuccess = ref('')
const pwLoading = ref(false)

async function changePassword() {
  pwError.value = ''
  pwSuccess.value = ''
  if (pwForm.newPassword !== pwForm.confirmPassword) {
    pwError.value = 'De nye passordene stemmer ikke overens.'
    return
  }
  if (pwForm.newPassword.length < 8) {
    pwError.value = 'Nytt passord må være minst 8 tegn.'
    return
  }
  pwLoading.value = true
  try {
    await apiClient.post('/auth/change-password', {
      currentPassword: pwForm.currentPassword,
      newPassword: pwForm.newPassword,
    })
    pwSuccess.value = 'Passordet er endret.'
    pwForm.currentPassword = ''
    pwForm.newPassword = ''
    pwForm.confirmPassword = ''
  } catch (err: any) {
    pwError.value = err.response?.data?.error ?? 'Kunne ikke endre passordet.'
  } finally {
    pwLoading.value = false
  }
}
</script>

<template>
  <div class="max-w-2xl mx-auto px-4 py-12 space-y-10">
    <div>
      <h1 class="text-2xl font-bold text-gray-900">Min konto</h1>
      <p class="text-gray-500 text-sm mt-1">{{ auth.user?.email }}</p>
    </div>

    <!-- Profile section -->
    <div class="bg-white rounded-2xl shadow-sm border border-gray-200 p-6">
      <h2 class="text-lg font-semibold text-gray-800 mb-4">Profilinformasjon</h2>
      <form @submit.prevent="saveProfile" class="space-y-4">
        <div>
          <label class="block text-sm font-medium text-gray-700 mb-1">Navn</label>
          <input
            v-model="profile.name"
            type="text"
            required
            class="w-full border border-gray-300 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
          />
        </div>
        <div>
          <label class="block text-sm font-medium text-gray-700 mb-1">Telefon</label>
          <input
            v-model="profile.phone"
            type="tel"
            placeholder="+47 900 00 000"
            class="w-full border border-gray-300 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
          />
        </div>

        <p v-if="profileError" class="text-red-600 text-sm bg-red-50 border border-red-200 rounded-lg px-3 py-2">
          {{ profileError }}
        </p>
        <p v-if="profileSuccess" class="text-green-700 text-sm bg-green-50 border border-green-200 rounded-lg px-3 py-2">
          {{ profileSuccess }}
        </p>

        <button
          type="submit"
          :disabled="profileLoading"
          class="bg-blue-700 text-white px-5 py-2 rounded-lg text-sm font-medium hover:bg-blue-800 disabled:opacity-60 transition"
        >
          {{ profileLoading ? 'Lagrer…' : 'Lagre endringer' }}
        </button>
      </form>
    </div>

    <!-- Change password section -->
    <div class="bg-white rounded-2xl shadow-sm border border-gray-200 p-6">
      <h2 class="text-lg font-semibold text-gray-800 mb-4">Endre passord</h2>
      <form @submit.prevent="changePassword" class="space-y-4">
        <div>
          <label class="block text-sm font-medium text-gray-700 mb-1">Nåværende passord</label>
          <input
            v-model="pwForm.currentPassword"
            type="password"
            required
            autocomplete="current-password"
            class="w-full border border-gray-300 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
          />
        </div>
        <div>
          <label class="block text-sm font-medium text-gray-700 mb-1">Nytt passord</label>
          <input
            v-model="pwForm.newPassword"
            type="password"
            required
            autocomplete="new-password"
            placeholder="Minst 8 tegn"
            class="w-full border border-gray-300 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
          />
        </div>
        <div>
          <label class="block text-sm font-medium text-gray-700 mb-1">Bekreft nytt passord</label>
          <input
            v-model="pwForm.confirmPassword"
            type="password"
            required
            autocomplete="new-password"
            class="w-full border border-gray-300 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
          />
        </div>

        <p v-if="pwError" class="text-red-600 text-sm bg-red-50 border border-red-200 rounded-lg px-3 py-2">
          {{ pwError }}
        </p>
        <p v-if="pwSuccess" class="text-green-700 text-sm bg-green-50 border border-green-200 rounded-lg px-3 py-2">
          {{ pwSuccess }}
        </p>

        <button
          type="submit"
          :disabled="pwLoading"
          class="bg-gray-800 text-white px-5 py-2 rounded-lg text-sm font-medium hover:bg-gray-900 disabled:opacity-60 transition"
        >
          {{ pwLoading ? 'Endrer passord…' : 'Endre passord' }}
        </button>
      </form>
    </div>

    <!-- Quick links -->
    <div class="flex gap-4 text-sm">
      <RouterLink to="/account/orders" class="text-blue-700 hover:underline">Mine ordrer →</RouterLink>
    </div>
  </div>
</template>

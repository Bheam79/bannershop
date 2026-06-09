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
  <div class="account-wrap">

    <!-- Greeting -->
    <div class="account-header">
      <h1 class="display account-title">
        <i class="fa-solid fa-user greeting-icon"></i>
        Hei{{ auth.user?.name ? `, ${auth.user.name.split(' ')[0]}` : '' }}!
      </h1>
      <p class="account-email">{{ auth.user?.email }}</p>
    </div>

    <!-- Profile section -->
    <div class="panel">
      <h2 class="section-title">
        <i class="fa-solid fa-user"></i>
        Profilinformasjon
      </h2>
      <form @submit.prevent="saveProfile" class="form-grid">
        <div class="form-field">
          <label class="field-label">Navn</label>
          <input
            v-model="profile.name"
            type="text"
            required
            class="field-input"
          />
        </div>
        <div class="form-field">
          <label class="field-label">Telefon</label>
          <input
            v-model="profile.phone"
            type="tel"
            placeholder="+47 900 00 000"
            class="field-input"
          />
        </div>

        <div v-if="profileError" class="alert-error full-col">
          <i class="fa-solid fa-circle-exclamation"></i>
          {{ profileError }}
        </div>
        <div v-if="profileSuccess" class="alert-success full-col">
          <i class="fa-solid fa-circle-check"></i>
          {{ profileSuccess }}
        </div>

        <div class="full-col">
          <button
            type="submit"
            :disabled="profileLoading"
            class="btn btn-primary"
          >
            <i v-if="profileLoading" class="fa-solid fa-circle-notch fa-spin"></i>
            {{ profileLoading ? 'Lagrer…' : 'Lagre endringer' }}
          </button>
        </div>
      </form>
    </div>

    <!-- Change password section -->
    <div class="panel">
      <h2 class="section-title">
        <i class="fa-solid fa-lock"></i>
        Endre passord
      </h2>
      <form @submit.prevent="changePassword" class="form-grid">
        <div class="form-field full-col">
          <label class="field-label">Nåværende passord</label>
          <input
            v-model="pwForm.currentPassword"
            type="password"
            required
            autocomplete="current-password"
            class="field-input"
          />
        </div>
        <div class="form-field">
          <label class="field-label">Nytt passord</label>
          <input
            v-model="pwForm.newPassword"
            type="password"
            required
            autocomplete="new-password"
            placeholder="Minst 8 tegn"
            class="field-input"
          />
        </div>
        <div class="form-field">
          <label class="field-label">Bekreft nytt passord</label>
          <input
            v-model="pwForm.confirmPassword"
            type="password"
            required
            autocomplete="new-password"
            class="field-input"
          />
        </div>

        <div v-if="pwError" class="alert-error full-col">
          <i class="fa-solid fa-circle-exclamation"></i>
          {{ pwError }}
        </div>
        <div v-if="pwSuccess" class="alert-success full-col">
          <i class="fa-solid fa-circle-check"></i>
          {{ pwSuccess }}
        </div>

        <div class="full-col">
          <button
            type="submit"
            :disabled="pwLoading"
            class="btn btn-soft"
          >
            <i v-if="pwLoading" class="fa-solid fa-circle-notch fa-spin"></i>
            {{ pwLoading ? 'Endrer passord…' : 'Endre passord' }}
          </button>
        </div>
      </form>
    </div>

  </div>
</template>

<style scoped>
/* ── Layout ─────────────────────────────────────────────────── */
.account-wrap {
  max-width: 680px;
  margin: 0 auto;
  padding: 2.5rem 1.25rem 3.5rem;
  display: flex;
  flex-direction: column;
  gap: 1.5rem;
}

/* ── Greeting ───────────────────────────────────────────────── */
.account-header { margin-bottom: 0.25rem; }
.account-title {
  font-size: clamp(1.4rem, 3vw, 1.875rem);
  color: var(--text);
  display: flex;
  align-items: center;
  gap: 0.5rem;
}
.greeting-icon { color: var(--accent); font-size: 1.2rem; }
.account-email { color: var(--faint); font-size: 0.875rem; margin-top: 4px; }

/* ── Section title ──────────────────────────────────────────── */
.section-title {
  font-size: 1rem;
  font-weight: 700;
  color: var(--text);
  font-family: var(--font-display);
  display: flex;
  align-items: center;
  gap: 0.5rem;
  margin-bottom: 1rem;
}
.section-title i { color: var(--accent); font-size: 0.9rem; }

/* ── Form ───────────────────────────────────────────────────── */
.form-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 1rem;
}
@media (max-width: 540px) { .form-grid { grid-template-columns: 1fr; } }

.form-field { display: flex; flex-direction: column; }
.full-col { grid-column: 1 / -1; }

.field-label {
  font-size: 0.8125rem;
  font-weight: 600;
  color: var(--muted);
  margin-bottom: 6px;
}
.field-input {
  width: 100%;
  background: var(--surface-2);
  border: 1px solid var(--line);
  border-radius: 10px;
  padding: 10px 14px;
  font-size: 0.9375rem;
  color: var(--text);
  font-family: var(--font-ui);
  outline: none;
  transition: border-color 0.15s, box-shadow 0.15s;
}
.field-input::placeholder { color: var(--faint); }
.field-input:focus {
  border-color: var(--accent);
  box-shadow: 0 0 0 3px rgba(255,106,61,.18);
}

/* ── Alerts ─────────────────────────────────────────────────── */
.alert-error {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 10px 14px;
  background: rgba(220,60,60,.12);
  border: 1px solid rgba(220,60,60,.3);
  border-radius: 10px;
  color: #f4a57a;
  font-size: 0.875rem;
}
.alert-error i { color: #e05252; flex-shrink: 0; }

.alert-success {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 10px 14px;
  background: rgba(60,180,100,.1);
  border: 1px solid rgba(60,180,100,.25);
  border-radius: 10px;
  color: #7de0a8;
  font-size: 0.875rem;
}
.alert-success i { color: #4ec984; flex-shrink: 0; }
</style>

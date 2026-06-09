<script setup lang="ts">
import { ref } from 'vue'
import { useRouter, useRoute } from 'vue-router'
import { useAuthStore } from '@/stores/auth'
import apiClient from '@/api/client'
import type { AuthResponse } from '@/types'

const router = useRouter()
const route = useRoute()
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
    const redirect = (route.query.redirect as string) || '/account'
    router.push(redirect)
  } catch (err: any) {
    error.value = err.response?.data?.error ?? 'Registrering feilet. Prøv igjen.'
  } finally {
    loading.value = false
  }
}
</script>

<template>
  <div style="min-height:70vh;display:flex;align-items:center;justify-content:center;padding:2.5rem 1rem;background:var(--bg)">
    <div class="auth-panel">
      <!-- Glow accent blob -->
      <div class="auth-glow"></div>

      <!-- Signup bonus chip -->
      <div class="bonus-chip">
        🎁 Få <strong>5 gratis AI-kreditter</strong> ved registrering
      </div>

      <h1 class="display" style="font-size:28px;margin-bottom:6px;color:var(--text)">Opprett konto</h1>
      <p style="color:var(--muted);font-size:15px;margin-bottom:28px">
        Har du allerede konto?
        <RouterLink :to="route.query.redirect ? `/login?redirect=${encodeURIComponent(route.query.redirect as string)}` : '/login'" style="color:var(--accent);font-weight:600;text-decoration:none">Logg inn</RouterLink>
      </p>

      <form @submit.prevent="handleSubmit" style="display:grid;gap:18px">

        <div>
          <label for="name" class="field-label">
            <i class="fa-solid fa-user"></i>
            Fullt navn
            <span class="req-star">*</span>
          </label>
          <input
            id="name"
            v-model="name"
            type="text"
            required
            autocomplete="name"
            placeholder="Ola Nordmann"
            class="field-input"
          />
        </div>

        <div>
          <label for="email" class="field-label">
            <i class="fa-solid fa-envelope"></i>
            E-post
            <span class="req-star">*</span>
          </label>
          <input
            id="email"
            v-model="email"
            type="email"
            required
            autocomplete="email"
            placeholder="din@epost.no"
            class="field-input"
          />
        </div>

        <div>
          <label for="phone" class="field-label">
            <i class="fa-solid fa-phone"></i>
            Telefon
            <span style="color:var(--faint);font-weight:400;font-size:13px">(valgfritt)</span>
          </label>
          <input
            id="phone"
            v-model="phone"
            type="tel"
            autocomplete="tel"
            placeholder="+47 900 00 000"
            class="field-input"
          />
        </div>

        <div>
          <label for="password" class="field-label">
            <i class="fa-solid fa-lock"></i>
            Passord
            <span class="req-star">*</span>
          </label>
          <input
            id="password"
            v-model="password"
            type="password"
            required
            autocomplete="new-password"
            placeholder="Minst 8 tegn"
            class="field-input"
          />
        </div>

        <div>
          <label for="confirm-password" class="field-label">
            <i class="fa-solid fa-lock"></i>
            Bekreft passord
            <span class="req-star">*</span>
          </label>
          <input
            id="confirm-password"
            v-model="confirmPassword"
            type="password"
            required
            autocomplete="new-password"
            placeholder="••••••••"
            class="field-input"
          />
        </div>

        <div v-if="error" class="error-box">
          <i class="fa-solid fa-circle-exclamation"></i>
          {{ error }}
        </div>

        <button
          type="submit"
          :disabled="loading"
          class="btn btn-primary"
          style="width:100%;justify-content:center;padding:13px;font-size:16px;border-radius:12px;margin-top:4px"
        >
          <i v-if="loading" class="fa-solid fa-circle-notch fa-spin"></i>
          {{ loading ? 'Oppretter konto…' : 'Opprett konto' }}
        </button>

        <p style="font-size:13px;color:var(--faint);text-align:center;margin-top:2px">
          Ved å registrere deg godtar du våre vilkår for bruk.
        </p>
      </form>
    </div>
  </div>
</template>

<style scoped>
.auth-panel {
  position: relative;
  width: 100%;
  max-width: 420px;
  background: var(--surface);
  border: 1px solid var(--line-soft);
  border-radius: var(--radius);
  padding: 36px;
  overflow: hidden;
}
.auth-glow {
  position: absolute;
  width: 300px;
  height: 300px;
  border-radius: 50%;
  background: rgba(255, 106, 61, 0.1);
  top: -140px;
  right: -100px;
  filter: blur(70px);
  pointer-events: none;
}
.field-label {
  display: flex;
  align-items: center;
  gap: 7px;
  font-size: 13.5px;
  font-weight: 600;
  color: var(--muted);
  margin-bottom: 7px;
}
.field-label i {
  color: var(--faint);
  font-size: 13px;
  width: 14px;
  text-align: center;
}
.req-star {
  color: var(--accent);
  font-weight: 700;
  font-size: 13px;
}
.field-input {
  width: 100%;
  background: var(--surface-2);
  border: 1px solid var(--line);
  border-radius: 10px;
  padding: 10px 14px;
  font-size: 15px;
  color: var(--text);
  font-family: var(--font-ui);
  outline: none;
  transition: border-color 0.15s, box-shadow 0.15s;
}
.field-input::placeholder {
  color: var(--faint);
}
.field-input:focus {
  border-color: var(--accent);
  box-shadow: 0 0 0 3px rgba(255, 106, 61, 0.18);
}
.error-box {
  display: flex;
  align-items: center;
  gap: 9px;
  color: #f4a57a;
  background: rgba(255, 106, 61, 0.1);
  border: 1px solid rgba(255, 106, 61, 0.3);
  border-radius: 10px;
  padding: 10px 14px;
  font-size: 14px;
}
.error-box i {
  color: var(--accent);
  flex-shrink: 0;
}

/* ── Signup bonus chip ──────────────────────────────────────── */
.bonus-chip {
  display: inline-flex;
  align-items: center;
  gap: 7px;
  font-size: 13.5px;
  font-weight: 500;
  color: #e7c35a;
  background: rgba(231,185,78,.12);
  border: 1px solid rgba(231,185,78,.32);
  border-radius: 999px;
  padding: 6px 14px;
  margin-bottom: 20px;
}
.bonus-chip strong {
  font-weight: 700;
  color: #f0d06a;
}
</style>

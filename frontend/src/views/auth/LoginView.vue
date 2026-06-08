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
    const redirect = (route.query.redirect as string) || '/account'
    router.push(redirect)
  } catch (err: any) {
    console.error('Login error:', err.response?.data?.error ?? err)
    error.value = 'Innlogging feilet. Sjekk e-post og passord og prøv igjen.'
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

      <h1 class="display" style="font-size:28px;margin-bottom:6px;color:var(--text)">Logg inn</h1>
      <p style="color:var(--muted);font-size:15px;margin-bottom:28px">
        Har du ikke konto?
        <RouterLink to="/register" style="color:var(--accent);font-weight:600;text-decoration:none">Registrer deg</RouterLink>
      </p>

      <form @submit.prevent="handleSubmit" style="display:grid;gap:18px">
        <div>
          <label for="email" class="field-label">
            <i class="fa-solid fa-envelope"></i> E-post
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
          <label for="password" class="field-label">
            <i class="fa-solid fa-lock"></i> Passord
          </label>
          <input
            id="password"
            v-model="password"
            type="password"
            required
            autocomplete="current-password"
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
          {{ loading ? 'Logger inn…' : 'Logg inn' }}
        </button>
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
  width: 260px;
  height: 260px;
  border-radius: 50%;
  background: rgba(255, 106, 61, 0.1);
  top: -120px;
  right: -80px;
  filter: blur(60px);
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
  font-size: 12px;
  width: 14px;
  text-align: center;
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
</style>

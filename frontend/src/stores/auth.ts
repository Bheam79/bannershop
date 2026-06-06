import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import type { User, AuthResponse } from '@/types'
import axios from 'axios'

export const useAuthStore = defineStore('auth', () => {
  const accessToken = ref<string | null>(localStorage.getItem('access_token'))
  const refreshTokenValue = ref<string | null>(localStorage.getItem('refresh_token'))
  const user = ref<User | null>(JSON.parse(localStorage.getItem('user') || 'null'))

  const isLoggedIn = computed(() => !!accessToken.value && !!user.value)
  const isAdmin = computed(() => user.value?.role === 'Admin')

  function setAuth(data: AuthResponse) {
    accessToken.value = data.accessToken
    refreshTokenValue.value = data.refreshToken
    user.value = data.user
    localStorage.setItem('access_token', data.accessToken)
    localStorage.setItem('refresh_token', data.refreshToken)
    localStorage.setItem('user', JSON.stringify(data.user))
  }

  function logout() {
    accessToken.value = null
    refreshTokenValue.value = null
    user.value = null
    localStorage.removeItem('access_token')
    localStorage.removeItem('refresh_token')
    localStorage.removeItem('user')
  }

  async function refreshToken(): Promise<boolean> {
    if (!refreshTokenValue.value) return false
    try {
      const response = await axios.post<AuthResponse>('/api/auth/refresh', {
        refreshToken: refreshTokenValue.value,
      })
      setAuth(response.data)
      return true
    } catch {
      logout()
      return false
    }
  }

  return {
    accessToken,
    user,
    isLoggedIn,
    isAdmin,
    setAuth,
    logout,
    refreshToken,
  }
})

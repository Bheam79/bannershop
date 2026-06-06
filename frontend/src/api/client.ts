import axios from 'axios'
import { useAuthStore } from '@/stores/auth'

const apiClient = axios.create({
  baseURL: '/api',
  headers: { 'Content-Type': 'application/json' },
})

// Attach JWT token to every request
apiClient.interceptors.request.use((config) => {
  const auth = useAuthStore()
  if (auth.accessToken) {
    config.headers.Authorization = `Bearer ${auth.accessToken}`
  }
  return config
})

// Handle 401 → try refresh, then redirect to login
apiClient.interceptors.response.use(
  (response) => response,
  async (error) => {
    const originalRequest = error.config
    if (error.response?.status === 401 && !originalRequest._retry) {
      originalRequest._retry = true
      const auth = useAuthStore()
      const refreshed = await auth.refreshToken()
      if (refreshed) {
        originalRequest.headers.Authorization = `Bearer ${auth.accessToken}`
        return apiClient(originalRequest)
      }
      auth.logout()
      window.location.href = '/login'
    }
    return Promise.reject(error)
  }
)

export default apiClient

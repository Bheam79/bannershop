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

// Unauthenticated auth endpoints — a 401 from these means "bad credentials"
// (login/register) or "invalid refresh token" and must NOT trigger the refresh
// + full-page-redirect dance, otherwise the page reloads and the calling view's
// catch block never runs, swallowing the error (see BANNERSH-72).
const AUTH_PUBLIC_PATHS = ['/auth/login', '/auth/register', '/auth/refresh', '/auth/logout']

function isAuthPublicEndpoint(url: string | undefined): boolean {
  if (!url) return false
  return AUTH_PUBLIC_PATHS.some((p) => url.includes(p))
}

// Handle 401 → try refresh, then redirect to login
apiClient.interceptors.response.use(
  (response) => response,
  async (error) => {
    const originalRequest = error.config
    if (
      error.response?.status === 401 &&
      !originalRequest._retry &&
      !isAuthPublicEndpoint(originalRequest?.url)
    ) {
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

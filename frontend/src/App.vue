<script setup lang="ts">
import { RouterView, useRoute } from 'vue-router'
import { computed, onMounted, onBeforeUnmount, watch } from 'vue'
import NavBar from '@/components/layout/NavBar.vue'
import AdminNavBar from '@/components/layout/AdminNavBar.vue'
import { useAuthStore } from '@/stores/auth'
import { useAiCreditsStore } from '@/stores/aiCredits'

const route = useRoute()
const isAdmin = computed(() => route.path.startsWith('/admin'))
const isHome  = computed(() => route.path === '/')

// ── AI credit badge bootstrap (BANNERSH-87) ───────────────────────────────────
// Keep the header badge in sync with the logged-in user's balance:
//   • Fetch once on mount when the session is already authenticated (page reload)
//   • Refetch when isLoggedIn flips false → true (after login)
//   • Reset to null when isLoggedIn flips true → false (after logout)
//   • Refetch on route change — covers the common "generated a banner, navigated
//     away" case without each view having to manually sync the store
//   • Refetch on tab-visibility return — picks up cross-tab activity
// Per-view consumers (AiBannerBuilderView etc.) can also call setBalance() to
// short-circuit the round-trip when they already know the new count.
const auth = useAuthStore()
const credits = useAiCreditsStore()

onMounted(() => {
  if (auth.isLoggedIn) credits.fetchBalance()
  document.addEventListener('visibilitychange', onVisibility)
})

onBeforeUnmount(() => {
  document.removeEventListener('visibilitychange', onVisibility)
})

function onVisibility() {
  if (document.visibilityState === 'visible' && auth.isLoggedIn) {
    credits.fetchBalance()
  }
}

watch(
  () => auth.isLoggedIn,
  (loggedIn, prev) => {
    if (loggedIn && !prev) credits.fetchBalance()
    if (!loggedIn && prev) credits.reset()
  },
)

// Refetch on every successful navigation when logged in. The /ai-credits/me
// endpoint is a single indexed DB read so this is cheap.
watch(
  () => route.fullPath,
  () => {
    if (auth.isLoggedIn) credits.fetchBalance()
  },
)
</script>

<template>
  <div class="min-h-screen flex flex-col">
    <AdminNavBar v-if="isAdmin" />
    <NavBar v-else-if="!isHome" />
    <main class="flex-1">
      <RouterView />
    </main>
    <!-- Minimal footer for non-home pages -->
    <footer v-if="!isHome && !isAdmin" class="py-8 mt-16 border-t" style="border-color:var(--line-soft)">
      <div class="wrap text-center" style="color:var(--faint);font-size:13.5px">
        <p>© 2026 BannerShop.no — Kvalitetsbannere fra norsk trykkeri</p>
        <p class="mt-1">Beatgrid AS · Org.nr. NO 928 177 572 MVA</p>
      </div>
    </footer>
  </div>
</template>

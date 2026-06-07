<script setup lang="ts">
import { RouterView, useRoute } from 'vue-router'
import { computed } from 'vue'
import NavBar from '@/components/layout/NavBar.vue'
import AdminNavBar from '@/components/layout/AdminNavBar.vue'

const route = useRoute()
const isAdmin = computed(() => route.path.startsWith('/admin'))
const isHome  = computed(() => route.path === '/')
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
      </div>
    </footer>
  </div>
</template>

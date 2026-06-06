<script setup lang="ts">
import { RouterView, useRoute } from 'vue-router'
import { computed } from 'vue'
import NavBar from '@/components/layout/NavBar.vue'
import AdminNavBar from '@/components/layout/AdminNavBar.vue'

const route = useRoute()
const isAdmin = computed(() => route.path.startsWith('/admin'))
</script>

<template>
  <div class="min-h-screen flex flex-col">
    <AdminNavBar v-if="isAdmin" />
    <NavBar v-else />
    <main class="flex-1">
      <RouterView />
    </main>
    <footer class="bg-gray-800 text-gray-300 py-8 mt-16">
      <div class="max-w-6xl mx-auto px-4 text-center text-sm">
        <p>© 2026 BannerShop.no — Kvalitetsbannere fra din lokale trykkeri</p>
      </div>
    </footer>
  </div>
</template>

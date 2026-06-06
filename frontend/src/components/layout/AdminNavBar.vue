<script setup lang="ts">
import { RouterLink } from 'vue-router'
import { useAuthStore } from '@/stores/auth'

const auth = useAuthStore()
</script>

<template>
  <nav class="bg-gray-900 text-white">
    <div class="max-w-7xl mx-auto px-4 h-14 flex items-center gap-6">
      <RouterLink to="/admin" class="font-bold text-yellow-400">⚙️ Admin</RouterLink>
      <RouterLink to="/admin/orders" class="text-gray-300 hover:text-white text-sm">Ordrer</RouterLink>
      <RouterLink to="/admin/design-requests" class="text-gray-300 hover:text-white text-sm">Design-bestillinger</RouterLink>
      <RouterLink to="/admin/sizes" class="text-gray-300 hover:text-white text-sm">Størrelser</RouterLink>
      <RouterLink to="/admin/materials" class="text-gray-300 hover:text-white text-sm">Materialer</RouterLink>
      <RouterLink to="/admin/pricing" class="text-gray-300 hover:text-white text-sm">Prissetting</RouterLink>
      <div class="ml-auto flex items-center gap-4">
        <RouterLink to="/" class="text-gray-400 hover:text-white text-sm">← Butikk</RouterLink>
        <span class="text-gray-400 text-sm">{{ auth.user?.name }}</span>
      </div>
    </div>
  </nav>
</template>

<script setup lang="ts">
import { RouterLink } from 'vue-router'
import { useAuthStore } from '@/stores/auth'

const auth = useAuthStore()
</script>

<template>
  <div class="max-w-6xl mx-auto px-4 py-12">
    <div class="mb-8">
      <h1 class="text-2xl font-bold text-gray-900">Admin-panel</h1>
      <p class="text-gray-500 mt-1">Innlogget som <strong>{{ auth.user?.name }}</strong></p>
    </div>

    <div class="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
      <RouterLink
        to="/admin/orders"
        class="block bg-white rounded-xl border border-gray-200 shadow-sm p-6 hover:border-blue-400 hover:shadow-md transition"
      >
        <div class="text-3xl mb-3">📦</div>
        <h2 class="font-semibold text-gray-800">Ordrer</h2>
        <p class="text-sm text-gray-500 mt-1">Se og administrer alle kundeordrer</p>
      </RouterLink>

      <RouterLink
        to="/admin/sizes"
        class="block bg-white rounded-xl border border-gray-200 shadow-sm p-6 hover:border-blue-400 hover:shadow-md transition"
      >
        <div class="text-3xl mb-3">📐</div>
        <h2 class="font-semibold text-gray-800">Bannerstørrelser</h2>
        <p class="text-sm text-gray-500 mt-1">Administrer størrelser og priser</p>
      </RouterLink>

      <RouterLink
        to="/admin/materials"
        class="block bg-white rounded-xl border border-gray-200 shadow-sm p-6 hover:border-blue-400 hover:shadow-md transition"
      >
        <div class="text-3xl mb-3">🧵</div>
        <h2 class="font-semibold text-gray-800">Materialer</h2>
        <p class="text-sm text-gray-500 mt-1">Administrer banner-materialer</p>
      </RouterLink>

      <RouterLink
        to="/admin/pricing"
        class="block bg-white rounded-xl border border-gray-200 shadow-sm p-6 hover:border-blue-400 hover:shadow-md transition"
      >
        <div class="text-3xl mb-3">💰</div>
        <h2 class="font-semibold text-gray-800">Prissetting</h2>
        <p class="text-sm text-gray-500 mt-1">Juster prisparametere</p>
      </RouterLink>
    </div>
  </div>
</template>

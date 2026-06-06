<script setup lang="ts">
import { RouterLink } from 'vue-router'
import { useAuthStore } from '@/stores/auth'
import { useCartStore } from '@/stores/cart'

const auth = useAuthStore()
const cart = useCartStore()

function handleLogout() {
  auth.logout()
}
</script>

<template>
  <nav class="bg-white shadow-sm border-b border-gray-200">
    <div class="max-w-6xl mx-auto px-4 h-16 flex items-center justify-between">
      <RouterLink to="/" class="text-xl font-bold text-blue-700">
        🖨️ BannerShop.no
      </RouterLink>

      <div class="flex items-center gap-6">
        <RouterLink to="/" class="text-gray-700 hover:text-blue-700 font-medium">Bannere</RouterLink>

        <template v-if="auth.isLoggedIn">
          <RouterLink to="/account/orders" class="text-gray-700 hover:text-blue-700">Mine ordrer</RouterLink>
          <button @click="handleLogout" class="text-sm text-gray-500 hover:text-red-600">Logg ut</button>
        </template>
        <template v-else>
          <RouterLink to="/login" class="text-gray-700 hover:text-blue-700">Logg inn</RouterLink>
          <RouterLink to="/register" class="bg-blue-700 text-white px-4 py-2 rounded-lg hover:bg-blue-800 text-sm font-medium">
            Registrer
          </RouterLink>
        </template>

        <RouterLink v-if="cart.itemCount > 0" to="/checkout"
          class="relative bg-yellow-400 hover:bg-yellow-500 text-yellow-900 px-4 py-2 rounded-lg font-medium text-sm">
          🛒 Kasse ({{ cart.itemCount }})
        </RouterLink>
      </div>
    </div>
  </nav>
</template>

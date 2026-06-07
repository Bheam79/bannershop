<script setup lang="ts">
import { RouterLink, useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/auth'
import { useCartStore } from '@/stores/cart'

const auth = useAuthStore()
const cart = useCartStore()
const router = useRouter()

async function handleLogout() {
  await auth.logoutFromServer()
  router.push('/login')
}
</script>

<template>
  <nav style="background:rgba(21,18,14,.92);backdrop-filter:saturate(140%) blur(14px);border-bottom:1px solid var(--line-soft);position:sticky;top:0;z-index:50">
    <div style="max-width:var(--maxw);margin:0 auto;padding:0 28px;height:72px;display:flex;align-items:center;justify-content:space-between">

      <!-- Brand -->
      <RouterLink to="/" style="display:flex;align-items:center;gap:11px;font-family:var(--font-display);font-weight:700;font-size:20px;letter-spacing:-.02em;text-decoration:none;color:var(--text)">
        <span style="width:34px;height:34px;border-radius:9px;display:grid;place-items:center;overflow:hidden;background:var(--accent)">
          <img src="/logo.png" alt="BannerShop.no" style="width:22px;height:22px;object-fit:contain">
        </span>
        <span>Banner<b style="color:var(--accent)">Shop</b>.no</span>
      </RouterLink>

      <!-- Links -->
      <div style="display:flex;align-items:center;gap:24px">
        <RouterLink to="/" style="color:var(--muted);font-weight:500;font-size:15px;text-decoration:none;transition:color .15s" class="nb-link">Hjem</RouterLink>
        <RouterLink to="/banner-builder" style="color:var(--muted);font-weight:500;font-size:15px;text-decoration:none;transition:color .15s" class="nb-link">Lag ditt banner</RouterLink>

        <template v-if="auth.isLoggedIn">
          <RouterLink to="/account" style="color:var(--muted);font-weight:500;font-size:15px;text-decoration:none" class="nb-link">Min konto</RouterLink>
          <RouterLink to="/account/orders" style="color:var(--muted);font-weight:500;font-size:15px;text-decoration:none" class="nb-link">Mine ordrer</RouterLink>
          <RouterLink to="/account/design-requests" style="color:var(--muted);font-weight:500;font-size:15px;text-decoration:none" class="nb-link">Mine design-bestillinger</RouterLink>
          <button @click="handleLogout" style="background:none;border:none;cursor:pointer;color:var(--faint);font-size:14px;font-family:var(--font-ui);transition:color .15s" class="nb-link">Logg ut</button>
        </template>
        <template v-else>
          <RouterLink to="/login" style="color:var(--muted);font-weight:500;font-size:15px;text-decoration:none" class="nb-link">Logg inn</RouterLink>
          <RouterLink to="/register" class="btn btn-primary" style="font-size:14px;padding:9px 16px">Registrer</RouterLink>
        </template>

        <RouterLink v-if="cart.itemCount > 0" to="/checkout" class="btn btn-primary" style="background:var(--gold);color:#1a0d06;box-shadow:none;font-size:14px;padding:9px 16px">
          🛒 Kasse ({{ cart.itemCount }})
        </RouterLink>
      </div>

    </div>
  </nav>
</template>

<style scoped>
.nb-link:hover { color: var(--text) !important; }
</style>

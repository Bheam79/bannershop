<script setup lang="ts">
import { ref, onMounted, onUnmounted } from 'vue'
import { RouterLink, useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/auth'
import { useCartStore } from '@/stores/cart'
import AiCreditBadge from '@/components/layout/AiCreditBadge.vue'

const auth = useAuthStore()
const cart = useCartStore()
const router = useRouter()

const menuOpen = ref(false)
const menuRef = ref<HTMLElement | null>(null)

async function handleLogout() {
  menuOpen.value = false
  await auth.logoutFromServer()
  router.push('/login')
}

function closeMenu() {
  menuOpen.value = false
}

function handleOutsideClick(e: MouseEvent) {
  if (menuRef.value && !menuRef.value.contains(e.target as Node)) {
    menuOpen.value = false
  }
}

onMounted(() => document.addEventListener('click', handleOutsideClick, true))
onUnmounted(() => document.removeEventListener('click', handleOutsideClick, true))
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

      <!-- Right side -->
      <div style="display:flex;align-items:center;gap:16px">
        <RouterLink to="/" style="color:var(--muted);font-weight:500;font-size:15px;text-decoration:none;transition:color .15s" class="nb-link">Hjem</RouterLink>

        <template v-if="auth.isLoggedIn">
          <AiCreditBadge />
        </template>
        <template v-else>
          <RouterLink to="/login" style="color:var(--muted);font-weight:500;font-size:15px;text-decoration:none" class="nb-link">Logg inn</RouterLink>
          <RouterLink to="/register" class="btn btn-primary" style="font-size:14px;padding:9px 16px">Registrer</RouterLink>
        </template>

        <RouterLink v-if="cart.itemCount > 0" to="/checkout" class="btn btn-primary" style="background:var(--gold);color:#1a0d06;box-shadow:none;font-size:14px;padding:9px 16px">
          <i class="fa-solid fa-cart-shopping"></i> Kasse ({{ cart.itemCount }})
        </RouterLink>

        <!-- Hamburger menu (logged in) -->
        <div v-if="auth.isLoggedIn" ref="menuRef" style="position:relative">
          <button
            @click="menuOpen = !menuOpen"
            class="nb-link hamburger-btn"
            :style="{
              background: menuOpen ? 'rgba(255,255,255,.07)' : 'none',
              border: '1px solid var(--line-soft)',
              borderRadius: '8px',
              cursor: 'pointer',
              color: 'var(--muted)',
              padding: '8px 11px',
              fontSize: '15px',
              fontFamily: 'var(--font-ui)',
              display: 'flex',
              alignItems: 'center',
              gap: '6px',
              transition: 'background .15s, color .15s',
            }"
            aria-label="Meny"
          >
            <i class="fa-solid fa-bars"></i>
          </button>

          <!-- Dropdown -->
          <div
            v-if="menuOpen"
            style="
              position:absolute;
              top:calc(100% + 8px);
              right:0;
              background:rgba(28,23,18,.97);
              backdrop-filter:saturate(140%) blur(18px);
              border:1px solid var(--line-soft);
              border-radius:12px;
              padding:8px;
              min-width:200px;
              box-shadow:0 8px 32px rgba(0,0,0,.45);
              z-index:100;
            "
          >
            <RouterLink
              to="/account"
              class="hm-item"
              @click="closeMenu"
            >
              <i class="fa-solid fa-circle-user" style="width:16px;opacity:.7"></i>
              Min konto
            </RouterLink>
            <RouterLink
              to="/account/orders"
              class="hm-item"
              @click="closeMenu"
            >
              <i class="fa-solid fa-box" style="width:16px;opacity:.7"></i>
              Mine ordrer
            </RouterLink>
            <RouterLink
              to="/account/design-requests"
              class="hm-item"
              @click="closeMenu"
            >
              <i class="fa-solid fa-wand-magic-sparkles" style="width:16px;opacity:.7"></i>
              Mine design
            </RouterLink>
            <RouterLink
              v-if="auth.isAdmin"
              to="/admin"
              class="hm-item"
              style="color:var(--accent) !important"
              @click="closeMenu"
            >
              <i class="fa-solid fa-gear" style="width:16px;opacity:.7"></i>
              Admin
            </RouterLink>

            <div style="height:1px;background:var(--line-soft);margin:6px 4px"></div>

            <button
              @click="handleLogout"
              class="hm-item hm-btn"
            >
              <i class="fa-solid fa-right-from-bracket" style="width:16px;opacity:.7"></i>
              Logg ut
            </button>
          </div>
        </div>
      </div>

    </div>
  </nav>
</template>

<style scoped>
.nb-link:hover { color: var(--text) !important; }

.hamburger-btn:hover {
  background: rgba(255,255,255,.07) !important;
  color: var(--text) !important;
}

.hm-item {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 10px 14px;
  border-radius: 8px;
  color: var(--muted);
  font-size: 14px;
  font-weight: 500;
  font-family: var(--font-ui);
  text-decoration: none;
  transition: background .12s, color .12s;
  cursor: pointer;
  width: 100%;
  box-sizing: border-box;
}
.hm-item:hover {
  background: rgba(255,255,255,.07);
  color: var(--text) !important;
}

.hm-btn {
  background: none;
  border: none;
  text-align: left;
}
</style>

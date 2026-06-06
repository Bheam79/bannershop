import { createRouter, createWebHistory } from 'vue-router'
import { useAuthStore } from '@/stores/auth'

const router = createRouter({
  history: createWebHistory(import.meta.env.BASE_URL),
  routes: [
    // ── Public ──────────────────────────────────────────────────────────────
    {
      path: '/',
      name: 'home',
      component: () => import('@/views/HomeView.vue'),
    },
    {
      path: '/banner-builder',
      name: 'banner-builder',
      component: () => import('@/views/BannerBuilderView.vue'),
    },
    {
      path: '/banner-builder/ai',
      name: 'banner-builder-ai',
      component: () => import('@/views/AiBannerBuilderView.vue'),
    },
    {
      path: '/checkout',
      name: 'checkout',
      component: () => import('@/views/checkout/CheckoutView.vue'),
    },
    {
      path: '/checkout/payment',
      name: 'checkout-payment',
      component: () => import('@/views/checkout/PaymentView.vue'),
    },
    {
      path: '/checkout/confirmation/:orderId',
      name: 'checkout-confirmation',
      component: () => import('@/views/checkout/ConfirmationView.vue'),
    },

    // ── Auth ─────────────────────────────────────────────────────────────────
    {
      path: '/login',
      name: 'login',
      component: () => import('@/views/auth/LoginView.vue'),
      meta: { guestOnly: true },
    },
    {
      path: '/register',
      name: 'register',
      component: () => import('@/views/auth/RegisterView.vue'),
      meta: { guestOnly: true },
    },

    // ── Customer account ─────────────────────────────────────────────────────
    {
      path: '/account',
      name: 'account',
      component: () => import('@/views/account/AccountView.vue'),
      meta: { requiresAuth: true },
    },
    {
      path: '/account/orders',
      name: 'account-orders',
      component: () => import('@/views/account/OrdersView.vue'),
      meta: { requiresAuth: true },
    },
    {
      path: '/account/orders/:id',
      name: 'account-order-detail',
      component: () => import('@/views/account/OrderDetailView.vue'),
      meta: { requiresAuth: true },
    },

    // ── Admin ─────────────────────────────────────────────────────────────────
    {
      path: '/admin',
      name: 'admin',
      component: () => import('@/views/admin/AdminDashboard.vue'),
      meta: { requiresAdmin: true },
    },
    {
      path: '/admin/sizes',
      name: 'admin-sizes',
      component: () => import('@/views/admin/SizesView.vue'),
      meta: { requiresAdmin: true },
    },
    {
      path: '/admin/materials',
      name: 'admin-materials',
      component: () => import('@/views/admin/MaterialsView.vue'),
      meta: { requiresAdmin: true },
    },
    {
      path: '/admin/pricing',
      name: 'admin-pricing',
      component: () => import('@/views/admin/PricingView.vue'),
      meta: { requiresAdmin: true },
    },
    {
      path: '/admin/orders',
      name: 'admin-orders',
      component: () => import('@/views/admin/OrdersView.vue'),
      meta: { requiresAdmin: true },
    },
    {
      path: '/admin/orders/:id',
      name: 'admin-order-detail',
      component: () => import('@/views/admin/OrderDetailView.vue'),
      meta: { requiresAdmin: true },
    },
    {
      path: '/admin/design-requests',
      name: 'admin-design-requests',
      component: () => import('@/views/admin/AdminDesignRequestsView.vue'),
      meta: { requiresAdmin: true },
    },
    {
      path: '/admin/design-requests/:id',
      name: 'admin-design-request-detail',
      component: () => import('@/views/admin/AdminDesignRequestDetailView.vue'),
      meta: { requiresAdmin: true },
    },

    // ── 404 ───────────────────────────────────────────────────────────────────
    {
      path: '/:pathMatch(.*)*',
      name: 'not-found',
      component: () => import('@/views/NotFoundView.vue'),
    },
  ],
})

// Navigation guards
router.beforeEach((to) => {
  const auth = useAuthStore()

  if (to.meta.requiresAdmin && !auth.isAdmin) {
    return auth.isLoggedIn ? { name: 'home' } : { name: 'login' }
  }

  if (to.meta.requiresAuth && !auth.isLoggedIn) {
    return { name: 'login', query: { redirect: to.fullPath } }
  }

  if (to.meta.guestOnly && auth.isLoggedIn) {
    return { name: 'home' }
  }
})

export default router

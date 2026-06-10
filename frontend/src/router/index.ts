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
      component: () => import('@/views/BannerBuilderHubView.vue'),
    },
    {
      path: '/banner-builder/upload',
      name: 'banner-builder-upload',
      component: () => import('@/views/BannerBuilderView.vue'),
    },
    {
      path: '/banner-builder/ai',
      name: 'banner-builder-ai',
      component: () => import('@/views/AiBannerBuilderView.vue'),
    },
    {
      // BANNERSH-189: manual designer reuses the AI wizard component with mode='manual'
      // — same form/flow, conditionally hides AI-only UI, generates a "Ditt banner"
      // placeholder instead of calling the AI image API.
      path: '/banner-builder/manual',
      name: 'banner-builder-manual',
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
      meta: { requiresAuth: true },
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

    // ── Mine design (grid view of all designs) ────────────────────────────────
    {
      path: '/mine-design',
      name: 'mine-design',
      component: () => import('@/views/MyDesignsView.vue'),
      meta: { requiresAuth: true },
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
    // BANNERSH-183: dedicated one-pager for buying AI credit packs.
    // Linked from the AI credit badge in the header (NavBar → AiCreditBadge).
    {
      path: '/account/credits',
      name: 'account-credits',
      component: () => import('@/views/account/BuyCreditsView.vue'),
      meta: { requiresAuth: true },
    },
    {
      path: '/account/orders/:id',
      name: 'account-order-detail',
      component: () => import('@/views/account/OrderDetailView.vue'),
      meta: { requiresAuth: true },
    },
    // BANNERSH-185: dedicated retry-payment view for unpaid orders. Loads a
    // fresh Stripe client secret for the existing order (reusing the previous
    // PaymentIntent when possible) so the customer can complete payment from
    // the "Mine ordrer" listing.
    {
      path: '/account/orders/:id/pay',
      name: 'account-order-pay',
      component: () => import('@/views/account/RetryPaymentView.vue'),
      meta: { requiresAuth: true },
    },
    {
      path: '/account/design-requests',
      name: 'account-design-requests',
      component: () => import('@/views/account/AccountDesignRequestsView.vue'),
      meta: { requiresAuth: true },
    },
    {
      path: '/account/design-requests/:id',
      name: 'account-design-request-detail',
      component: () => import('@/views/account/AccountDesignRequestDetailView.vue'),
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
    // BANNERSH-179: design-bestillinger merged into Ordrer. The list route now
    // redirects to /admin/orders (so old bookmarks/links land in the unified
    // table — order types AI / Designer / AI-kjøp are first-class filters
    // there). The detail route is kept because OrderDetailView deep-links into
    // it for revision history / new-revision upload tooling.
    {
      path: '/admin/design-requests',
      redirect: '/admin/orders',
    },
    {
      path: '/admin/design-requests/:id',
      name: 'admin-design-request-detail',
      component: () => import('@/views/admin/AdminDesignRequestDetailView.vue'),
      meta: { requiresAdmin: true },
    },
    {
      path: '/admin/users',
      name: 'admin-users',
      component: () => import('@/views/admin/UsersView.vue'),
      meta: { requiresAdmin: true },
    },
    {
      path: '/admin/users/:id',
      name: 'admin-user-detail',
      component: () => import('@/views/admin/UserDetailView.vue'),
      meta: { requiresAdmin: true },
    },
    {
      path: '/admin/settings',
      name: 'admin-settings',
      component: () => import('@/views/admin/AdminSettingsView.vue'),
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

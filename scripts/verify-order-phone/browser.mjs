// Targeted offline browser check. Start Vite on :10000; all API responses are fixtures.
import { chromium } from '../../e2e/node_modules/playwright/index.mjs'
import assert from 'node:assert/strict'
const browser = await chromium.launch({ headless: true })
try {
  const page = await browser.newPage()
  page.on('pageerror', e => console.error('Browser error:', e.message))
  const phone = '+47 912 34 567'
  const order = { id: 311, userId: 311, customerName: 'Phone check', customerEmail: 'phone@example.invalid', customerPhone: phone,
    status: 'Paid', orderType: 'CustomBanner', orderState: 'Paid', deliveryType: 'Pickup', packingMode: 'Folded',
    shippingCostNok: 0, expressFeeNok: 0, totalNok: 699, createdAt: new Date().toISOString(), updatedAt: new Date().toISOString(),
    shippingAddress: null, items: [], shipmentTracking: null }
  let draftPhone = null
  await page.addInitScript(() => {
    if (localStorage.getItem('bannersh311fixture')) return
    localStorage.setItem('bannersh311fixture', 'ready')
    localStorage.setItem('access_token', 'offline-phone-check')
    localStorage.setItem('user', JSON.stringify({ id: 311, name: 'Phone check', email: 'phone@example.invalid', phone: null, role: 'Admin' }))
    localStorage.removeItem('bannershop_last_address')
    localStorage.removeItem('bannershop_draft_order')
  })
  await page.route('**/api/**', async route => {
    const path = new URL(route.request().url()).pathname
    if (!path.startsWith('/api/')) return route.continue()
    let data = []
    if (/\/orders\/311$/.test(path) || path === '/api/orders/311/mock-pay') data = order
    if (path === '/api/orders/draft') {
      draftPhone = route.request().postDataJSON().customerPhone
      data = { orderId: 311, clientSecret: 'offline_secret', totalNok: 699, breakdown: {} }
    }
    if (path === '/api/config/stripe') data = { publishableKey: null }
    if (path === '/api/shipping/parcel-preview') data = { lengthCm: 50, widthCm: 60, heightCm: 12, weightKg: 2 }
    await route.fulfill({ json: data })
  })
  await page.goto('http://127.0.0.1:10000/admin/orders/311')
  await page.getByText('Kundeinformasjon', { exact: true }).waitFor().catch(async e => { console.error('URL:', page.url(), 'BODY:', await page.locator('body').innerText()); throw e })
  const adminPhone = await page.locator('a[href^="tel:"]').filter({ hasText: phone }).count()
  await page.goto('http://127.0.0.1:10000/account/orders/311')
  await page.getByRole('link', { name: /Tilbake til ordrelisten/ }).waitFor()
  const accountPhone = await page.locator('a[href^="tel:"]').filter({ hasText: phone }).count()
  await page.evaluate(async () => {
    const pinia = document.querySelector('#app').__vue_app__.config.globalProperties.$pinia
    const { useCartStore } = await import('/src/stores/cart.ts')
    useCartStore(pinia).addItem({ bannerSizeId: 7, bannerSizeName: 'Phone check banner', materialId: 1, widthCm: 300, heightCm: 180,
      quantity: 1, unitPriceNok: 699, eyeletOption: 'None', eyeletFeeNok: 0 })
    await document.querySelector('#app').__vue_app__.config.globalProperties.$router.push('/checkout')
  })
  await page.locator('#recipientName').waitFor()
  const checkoutPhone = await page.locator('input[type="tel"]').count()
  console.log(JSON.stringify({ adminPhone, accountPhone, checkoutPhone }))
  assert.equal(adminPhone, 1, 'Admin order must show phone')
  assert.equal(accountPhone, 1, 'Customer order must show phone for pickup')
  assert.equal(checkoutPhone, 1, 'Checkout must ask for phone')
  await page.locator('[data-delivery=pickup]').click()
  await page.locator('#recipientName').fill('Phone check')
  const proceed = page.getByRole('button', { name: 'Gå til betaling' })
  await proceed.click()
  await page.getByText('Telefonnummer er påkrevd', { exact: true }).waitFor()
  for (const invalid of ['abc', '12345', '+1234567890123456', '91234<script>']) {
    await page.locator('#customerPhone').fill(invalid)
    await proceed.click()
    await page.getByText('Oppgi et gyldig telefonnummer (6–15 siffer)', { exact: true }).waitFor()
    assert.ok(page.url().endsWith('/checkout'))
  }
  await page.locator('#customerPhone').fill('  ' + phone + '  ')
  await proceed.click()
  await page.waitForURL('**/checkout/payment')
  assert.equal(JSON.parse(await page.evaluate(() => localStorage.getItem('bannershop_last_address'))).customerPhone, phone)
  await page.getByRole('button', { name: /Marker ordre som betalt/ }).click()
  await page.locator('input[type=password]').fill('offline')
  await page.getByRole('button', { name: /Bekreft/ }).click()
  await page.waitForURL('**/checkout/confirmation/311')
  assert.equal(draftPhone, phone, 'Payment sends the checkout phone in the draft request')
  await page.locator('a[href="tel:+4791234567"]').waitFor()
  // Rebuild a cart after successful payment: saved contact should prefill the next checkout.
  await page.evaluate(async () => {
    const pinia = document.querySelector('#app').__vue_app__.config.globalProperties.$pinia
    const { useCartStore } = await import('/src/stores/cart.ts')
    useCartStore(pinia).addItem({ bannerSizeId: 7, bannerSizeName: 'Repeat banner', materialId: 1, widthCm: 300, heightCm: 180,
      quantity: 1, unitPriceNok: 699, eyeletOption: 'None', eyeletFeeNok: 0 })
    await document.querySelector('#app').__vue_app__.config.globalProperties.$router.push('/checkout')
  })
  await page.locator('#customerPhone').waitFor()
  assert.equal(await page.locator('#customerPhone').inputValue(), phone)
  await page.evaluate(async () => {
    const pinia = document.querySelector('#app').__vue_app__.config.globalProperties.$pinia
    const { useCheckoutStore } = await import('/src/stores/checkout.ts')
    const { useCartStore } = await import('/src/stores/cart.ts')
    useCheckoutStore(pinia).setDraftOrder(999, useCartStore(pinia).cartHash)
  })
  await page.locator('#customerPhone').fill('99887766')
  await proceed.click()
  await page.waitForURL('**/checkout/payment')
  assert.equal(await page.evaluate(() => localStorage.getItem('bannershop_draft_order')), null,
    'Changing phone invalidates stale draft')
  // A fresh store after a reload restores the saved number, even without the new field in old snapshots.
  await page.reload()
  assert.equal(await page.evaluate(async () => {
    const { useCheckoutStore } = await import('/src/stores/checkout.ts')
    return useCheckoutStore(document.querySelector('#app').__vue_app__.config.globalProperties.$pinia).customerPhone
  }), '99887766')
  const profilePrefill = await page.evaluate(async () => {
    const { useCheckoutStore } = await import('/src/stores/checkout.ts')
    const { useAuthStore } = await import('/src/stores/auth.ts')
    const { useCartStore } = await import('/src/stores/cart.ts')
    const pinia = document.querySelector('#app').__vue_app__.config.globalProperties.$pinia
    const checkout = useCheckoutStore(pinia)
    checkout.clear()
    localStorage.setItem('bannershop_last_address', JSON.stringify({recipientName: 'Old snapshot', address: { line1: '', city: '', postalCode: '' }, deliveryType: 'Pickup'}))
    checkout.loadLastAddress()
    const missingPhoneBlocksPayment = !checkout.isReady()
    useAuthStore(pinia).user.phone = '90123456'
    useCartStore(pinia).addItem({ bannerSizeId: 7, bannerSizeName: 'Profile banner', materialId: 1, widthCm: 300, heightCm: 180,
      quantity: 1, unitPriceNok: 699, eyeletOption: 'None', eyeletFeeNok: 0 })
    await document.querySelector('#app').__vue_app__.config.globalProperties.$router.push('/checkout')
    return missingPhoneBlocksPayment
  })
  assert.equal(profilePrefill, true, 'Old address snapshots without phone must return to checkout')
  await page.locator('#customerPhone').waitFor()
  assert.equal(await page.locator('#customerPhone').inputValue(), '90123456', 'Account phone prefilled when saved checkout has none')
  console.log('PASS order/confirmation phone display, required/invalid input, payment payload, saved contact/reload, profile prefill and draft invalidation')
} finally { await browser.close() }

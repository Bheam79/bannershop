<script setup lang="ts">
import { ref, computed, onMounted, watch } from 'vue'
import { useRouter } from 'vue-router'
import type { BannerSize, ShippingEstimate, DeliveryType, CartItem } from '@/types'
import { fetchSizes, fetchPrice } from '@/api/shop'
import { useCartStore } from '@/stores/cart'
import BannerPreview from '@/components/shop/BannerPreview.vue'
import ShippingEstimator from '@/components/shop/ShippingEstimator.vue'

const router = useRouter()
const cart = useCartStore()

const sizes = ref<BannerSize[]>([])
const loading = ref(true)
const loadError = ref<string | null>(null)

const selectedId = ref<number | null>(null)
const customWidthCm = ref<number>(300)
const customPrice = ref<number | null>(null)
const customPriceLoading = ref(false)
const qty = ref<number>(1)
const deliveryType = ref<DeliveryType>('Standard')

const shippingEstimate = ref<ShippingEstimate | null>(null)
const postalCode = ref('')

const selectedSize = computed(() =>
  sizes.value.find(s => s.id === selectedId.value) ?? null,
)

const effectiveWidthCm = computed(() => {
  const s = selectedSize.value
  if (!s) return 0
  if (s.isCustomWidth) return customWidthCm.value
  return s.widthCm ?? 0
})

const effectiveHeightCm = computed(() => selectedSize.value?.heightCm ?? 150)

const effectivePriceNok = computed(() => {
  const s = selectedSize.value
  if (!s) return 0
  if (s.isCustomWidth) return customPrice.value ?? 0
  return s.calculatedPrice ?? 0
})

const lineTotal = computed(() => effectivePriceNok.value * qty.value)

const productionFee = computed(() => (deliveryType.value === 'Express' ? 500 : 0))
const shippingCost = computed(() => {
  if (!shippingEstimate.value) return 0
  return deliveryType.value === 'Express'
    ? shippingEstimate.value.express.costNok
    : shippingEstimate.value.standard.costNok
})

const grandTotal = computed(
  () => lineTotal.value + shippingCost.value + productionFee.value,
)

function formatNok(n: number): string {
  return new Intl.NumberFormat('nb-NO', { maximumFractionDigits: 0 }).format(n) + ' kr'
}

function formatAvailability(iso: string | null | undefined): string | null {
  if (!iso) return null
  const d = new Date(iso)
  if (Number.isNaN(d.getTime()) || d.getTime() <= Date.now()) return null
  return d.toLocaleDateString('nb-NO', { day: '2-digit', month: 'long', year: 'numeric' })
}

async function loadSizes() {
  loading.value = true
  loadError.value = null
  try {
    sizes.value = await fetchSizes(customWidthCm.value)
    // Pre-select the special-offer 300×180 size if present, else first non-custom
    const special = sizes.value.find(s => s.widthCm === 300 && s.heightCm === 180)
    selectedId.value = special?.id ?? sizes.value.find(s => !s.isCustomWidth)?.id ?? null
    await refreshCustomPrice()
  } catch (e: unknown) {
    const ex = e as { response?: { data?: { error?: string } }; message?: string }
    loadError.value = ex.response?.data?.error || ex.message || 'Kunne ikke laste bannerstørrelser.'
  } finally {
    loading.value = false
  }
}

async function refreshCustomPrice() {
  const customSize = sizes.value.find(s => s.isCustomWidth)
  if (!customSize) return
  customPriceLoading.value = true
  try {
    customPrice.value = await fetchPrice(customSize.id, customWidthCm.value)
  } catch {
    customPrice.value = null
  } finally {
    customPriceLoading.value = false
  }
}

let customPriceTimer: ReturnType<typeof setTimeout> | null = null
watch(customWidthCm, (v) => {
  if (v < 50) return
  if (v > 1000) return
  if (customPriceTimer) clearTimeout(customPriceTimer)
  customPriceTimer = setTimeout(refreshCustomPrice, 250)
})

function selectSize(id: number) {
  selectedId.value = id
}

function selectCustom() {
  const customSize = sizes.value.find(s => s.isCustomWidth)
  if (customSize) selectedId.value = customSize.id
}

function addToCart() {
  const s = selectedSize.value
  if (!s) return

  const item: CartItem = {
    bannerSizeId: s.id,
    bannerSizeName: s.isCustomWidth
      ? `${customWidthCm.value} × ${s.heightCm} cm (valgfri bredde)`
      : s.name,
    customWidthCm: s.isCustomWidth ? customWidthCm.value : null,
    heightCm: s.heightCm,
    quantity: qty.value,
    unitPriceNok: effectivePriceNok.value,
  }
  cart.addItem(item)
  cart.setShipping(
    shippingCost.value,
    productionFee.value,
    deliveryType.value,
  )
  router.push('/checkout')
}

onMounted(loadSizes)
</script>

<template>
  <div class="max-w-6xl mx-auto px-4 py-8 sm:py-12">
    <!-- Hero -->
    <header class="text-center mb-10">
      <h1 class="text-3xl sm:text-4xl font-bold text-gray-900 mb-3">
        Bestill ditt banner
      </h1>
      <p class="text-lg text-gray-600 max-w-2xl mx-auto">
        Høykvalitets bannere fra vår lokale trykkeri – rask levering over hele Norge.
        Standard maljer i hjørnene og sydde kanter inkludert.
      </p>
    </header>

    <!-- Loading / error -->
    <div v-if="loading" class="text-center text-gray-500 py-12">
      Laster bannerstørrelser…
    </div>
    <div v-else-if="loadError" class="bg-red-50 border border-red-200 text-red-800 rounded-xl p-6 text-center">
      {{ loadError }}
      <button class="mt-3 underline" @click="loadSizes">Prøv igjen</button>
    </div>

    <template v-else>
      <!-- Banner sizes grid -->
      <section class="mb-10">
        <h2 class="text-xl font-semibold text-gray-900 mb-4">Velg størrelse</h2>
        <div class="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-4">
          <template v-for="size in sizes" :key="size.id">
            <button
              v-if="!size.isCustomWidth"
              type="button"
              class="text-left bg-white border-2 rounded-xl p-5 transition hover:shadow-md"
              :class="selectedId === size.id
                ? 'border-blue-700 ring-2 ring-blue-200 shadow-md'
                : 'border-gray-200'"
              @click="selectSize(size.id)"
            >
              <div class="flex items-baseline justify-between gap-2">
                <span class="font-semibold text-gray-900">{{ size.name }}</span>
                <span v-if="size.fixedPrice != null"
                      class="text-xs uppercase tracking-wider bg-yellow-400 text-yellow-900 px-2 py-0.5 rounded-full font-bold">
                  Tilbud
                </span>
              </div>
              <div class="mt-1 text-sm text-gray-600">
                {{ size.material?.weightGsm }}g materiale
                <span class="text-gray-400">•</span>
                {{ size.material?.name }}
              </div>
              <div class="mt-3 flex items-baseline gap-2">
                <span class="text-2xl font-bold text-blue-700">
                  {{ formatNok(size.calculatedPrice ?? 0) }}
                </span>
                <span v-if="size.fixedPrice != null" class="text-xs text-gray-500">spesialpris</span>
              </div>
              <p
                v-if="formatAvailability(size.availableFrom)"
                class="mt-3 text-xs text-amber-800 bg-amber-50 border border-amber-200 rounded-md px-2 py-1.5"
              >
                Tilgjengelig fra {{ formatAvailability(size.availableFrom) }} —
                bestill nå, leveres etter denne datoen.
              </p>
            </button>
          </template>

          <!-- Custom width card -->
          <button
            v-if="sizes.some(s => s.isCustomWidth)"
            type="button"
            class="text-left bg-white border-2 rounded-xl p-5 transition hover:shadow-md flex flex-col"
            :class="selectedSize?.isCustomWidth
              ? 'border-blue-700 ring-2 ring-blue-200 shadow-md'
              : 'border-gray-200'"
            @click="selectCustom"
          >
            <div class="flex items-baseline justify-between gap-2">
              <span class="font-semibold text-gray-900">Valgfri bredde</span>
              <span class="text-xs uppercase tracking-wider bg-blue-100 text-blue-800 px-2 py-0.5 rounded-full font-medium">
                Tilpass
              </span>
            </div>
            <div class="mt-1 text-sm text-gray-600">
              Høyde 150 cm
              <span class="text-gray-400">•</span>
              400g materiale
            </div>
            <div class="mt-3 flex items-center gap-2">
              <label class="text-sm text-gray-700" for="customWidth">Bredde (cm):</label>
              <input
                id="customWidth"
                v-model.number="customWidthCm"
                type="number"
                min="50"
                max="1000"
                class="w-24 border border-gray-300 rounded-md px-2 py-1 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
                @click.stop
                @focus="selectCustom"
              />
            </div>
            <div class="mt-3 text-2xl font-bold text-blue-700">
              <span v-if="customPriceLoading" class="text-gray-400 text-base">Beregner…</span>
              <span v-else-if="customPrice != null">{{ formatNok(customPrice) }}</span>
              <span v-else class="text-gray-400 text-base">–</span>
            </div>
          </button>
        </div>
      </section>

      <!-- Selection + preview + add-to-cart -->
      <section v-if="selectedSize" class="grid lg:grid-cols-2 gap-8 mb-10">
        <div class="bg-white border border-gray-200 rounded-xl p-6">
          <h3 class="text-lg font-semibold text-gray-900 mb-4">Forhåndsvisning</h3>
          <BannerPreview
            :width-cm="effectiveWidthCm"
            :height-cm="effectiveHeightCm"
            :label="`${effectiveWidthCm} × ${effectiveHeightCm} cm`"
          />
          <ul class="mt-4 text-sm text-gray-700 space-y-1">
            <li>✓ Sydde kanter rundt hele banneret</li>
            <li>✓ Metallmaljer i alle fire hjørner</li>
            <li>✓ UV-bestandig trykk i fullfarge</li>
            <li>✓ {{ selectedSize.material?.weightGsm }}g {{ selectedSize.material?.weightGsm === 680 ? 'forsterket' : 'standard' }} bannermateriale</li>
          </ul>
        </div>

        <div class="bg-white border border-gray-200 rounded-xl p-6 flex flex-col">
          <h3 class="text-lg font-semibold text-gray-900 mb-4">Bestillingsdetaljer</h3>

          <div class="space-y-4 flex-1">
            <div>
              <div class="text-sm text-gray-500">Valgt størrelse</div>
              <div class="text-lg font-semibold text-gray-900">
                {{ effectiveWidthCm }} × {{ effectiveHeightCm }} cm
              </div>
            </div>

            <div class="flex items-center gap-3">
              <label for="qty" class="text-sm text-gray-700">Antall</label>
              <input
                id="qty"
                v-model.number="qty"
                type="number"
                min="1"
                max="1000"
                class="w-24 border border-gray-300 rounded-md px-3 py-2 focus:outline-none focus:ring-2 focus:ring-blue-500"
              />
            </div>

            <div>
              <div class="text-sm text-gray-700 mb-2">Leveringstype</div>
              <div class="grid grid-cols-2 gap-2">
                <button
                  type="button"
                  class="border-2 rounded-lg px-3 py-2 text-sm font-medium transition"
                  :class="deliveryType === 'Standard'
                    ? 'border-blue-700 bg-blue-50 text-blue-800'
                    : 'border-gray-200 text-gray-700 hover:border-gray-300'"
                  @click="deliveryType = 'Standard'"
                >
                  Standard
                </button>
                <button
                  type="button"
                  class="border-2 rounded-lg px-3 py-2 text-sm font-medium transition"
                  :class="deliveryType === 'Express'
                    ? 'border-yellow-500 bg-yellow-50 text-yellow-900'
                    : 'border-gray-200 text-gray-700 hover:border-gray-300'"
                  @click="deliveryType = 'Express'"
                >
                  Ekspress (+500 kr)
                </button>
              </div>
            </div>

            <div class="border-t border-gray-200 pt-4 space-y-1 text-sm">
              <div class="flex justify-between">
                <span class="text-gray-600">Banner ({{ qty }} stk)</span>
                <span class="text-gray-900 font-medium">{{ formatNok(lineTotal) }}</span>
              </div>
              <div v-if="shippingEstimate" class="flex justify-between">
                <span class="text-gray-600">Frakt ({{ deliveryType === 'Express' ? 'ekspress' : 'standard' }})</span>
                <span class="text-gray-900 font-medium">{{ formatNok(shippingCost) }}</span>
              </div>
              <div v-if="deliveryType === 'Express'" class="flex justify-between">
                <span class="text-gray-600">Ekspress produksjonsgebyr</span>
                <span class="text-gray-900 font-medium">{{ formatNok(productionFee) }}</span>
              </div>
              <div class="flex justify-between text-base pt-2 border-t border-gray-100 mt-2">
                <span class="font-semibold text-gray-900">
                  {{ shippingEstimate ? 'Totalt' : 'Delsum' }}
                </span>
                <span class="font-bold text-blue-700">{{ formatNok(grandTotal) }}</span>
              </div>
              <p v-if="!shippingEstimate" class="text-xs text-gray-500 pt-1">
                Frakt beregnes når du angir postnummer nedenfor.
              </p>
            </div>
          </div>

          <button
            type="button"
            class="mt-6 w-full bg-blue-700 hover:bg-blue-800 disabled:bg-gray-300 text-white font-semibold py-3 rounded-lg transition"
            :disabled="!selectedSize || qty < 1"
            @click="addToCart"
          >
            🛒 Legg i handlekurv og gå til kasse
          </button>
        </div>
      </section>

      <!-- Shipping estimator -->
      <section class="mb-10">
        <ShippingEstimator
          :banner-size-id="selectedId"
          :custom-width-cm="selectedSize?.isCustomWidth ? customWidthCm : null"
          :qty="qty"
          @estimate="(v) => (shippingEstimate = v)"
          @postal-code="(v) => (postalCode = v)"
        />
      </section>

      <!-- Material info -->
      <section class="mb-10 grid sm:grid-cols-2 gap-4">
        <div class="bg-white border border-gray-200 rounded-xl p-5">
          <div class="flex items-center gap-2 mb-2">
            <span class="text-xs uppercase tracking-wider bg-blue-100 text-blue-800 px-2 py-0.5 rounded font-bold">
              400g
            </span>
            <h4 class="font-semibold text-gray-900">Standard bannermateriale</h4>
          </div>
          <p class="text-sm text-gray-600">
            Allsidig PVC-banner egnet for utendørs- og innendørsbruk. Lett, holdbart og
            værbestandig — perfekt for skilt, reklame, idrettsarrangementer og fester.
          </p>
        </div>
        <div class="bg-white border border-gray-200 rounded-xl p-5">
          <div class="flex items-center gap-2 mb-2">
            <span class="text-xs uppercase tracking-wider bg-amber-100 text-amber-900 px-2 py-0.5 rounded font-bold">
              680g
            </span>
            <h4 class="font-semibold text-gray-900">Forsterket bannermateriale</h4>
          </div>
          <p class="text-sm text-gray-600">
            Tykkere og kraftigere PVC for langvarig utendørs montering, byggeplasser
            eller stormutsatte områder. Ekstra rivebestandig med høyere fargeintensitet.
          </p>
        </div>
      </section>
    </template>
  </div>
</template>

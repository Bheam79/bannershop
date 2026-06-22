<script setup lang="ts">
import type { BannerSize, EyeletOption } from '@/types'
import { countEyelets } from '@/types'
import EyeletPreview from '@/components/shop/EyeletPreview.vue'
import { formatNok } from '@/utils/format'

const props = defineProps<{
  isManual: boolean
  tilpassLoading: boolean
  tilpassError: string | null
  tilpassBannerSize: BannerSize | null
  tilpassDesignWidthCm: number
  tilpassDesignHeightCm: number
  tilpassBannerPriceNok: number
  tilpassEyeletPriceNok: number
  tilpassEyeletOption: EyeletOption
  tilpassEyeletCount: number
  tilpassEyeletFeeNok: number
  tilpassTotalNok: number
  manualDesignPriceNok: number
  currentDesignRequestPreviewUrl: string | null
}>()

const emit = defineEmits<{
  'update:tilpassEyeletOption': [value: EyeletOption]
  addToCart: []
  back: []
}>()
</script>

<template>
  <div style="display:grid;gap:24px">
    <!-- Heading -->
    <div style="text-align:center">
      <h2 class="display" style="font-size:28px;color:var(--text);margin-bottom:8px">
        <i class="fa-solid fa-sliders" style="color:var(--accent);margin-right:8px"></i>
        Tilpass banneret
      </h2>
      <p style="color:var(--muted)">Velg om du vil ha maljer (øyebolter), og legg banneret i handlekurven.</p>
    </div>

    <!-- Loading -->
    <div v-if="tilpassLoading" style="text-align:center;padding:1.5rem;color:var(--muted)">
      <i class="fa-solid fa-circle-notch fa-spin" style="margin-right:8px"></i>
      Henter pris…
    </div>

    <template v-else-if="tilpassBannerSize">
      <!-- Banner price summary -->
      <div class="bb-panel" style="display:grid;gap:14px">
        <div>
          <div class="field-label">Størrelse</div>
          <div class="display" style="font-size:22px;color:var(--text);margin-top:4px">
            {{ tilpassDesignWidthCm }} × {{ tilpassDesignHeightCm }} cm
          </div>
        </div>
        <div style="display:flex;justify-content:space-between;align-items:center;font-size:15px;padding-top:8px;border-top:1px solid var(--line-soft)">
          <span style="color:var(--muted)">Bannerpris</span>
          <span style="color:var(--text);font-weight:600">{{ formatNok(tilpassBannerPriceNok) }}</span>
        </div>
      </div>

      <!-- Eyelet option picker -->
      <div class="bb-panel" style="display:grid;gap:14px">
        <div>
          <div class="field-label" style="margin-bottom:4px">
            Maljer (øyebolter)
            <span style="font-size:13px;font-weight:400;color:var(--faint);margin-left:4px">tilvalg</span>
          </div>
        </div>
        <!-- BANNERSH-173: eyelet placement preview -->
        <EyeletPreview
          v-if="tilpassDesignWidthCm > 0 && tilpassDesignHeightCm > 0"
          :width-cm="tilpassDesignWidthCm"
          :height-cm="tilpassDesignHeightCm"
          :eyelet-option="tilpassEyeletOption"
          :image-url="currentDesignRequestPreviewUrl ?? undefined"
          style="border-radius:8px;overflow:hidden;border:1px solid var(--line-soft)"
        />
        <div style="display:grid;gap:10px">
          <label
            v-for="opt in ([
              { value: 'None',        label: 'Ingen maljer',         sub: 'Uten hull' },
              { value: 'FourCorners', label: '4 maljer (hjørner)',    sub: 'En i hvert hjørne' },
              { value: 'PerMeter',    label: 'Maljer per meter',      sub: `Ca. 1 per 100 cm – ${countEyelets(tilpassDesignWidthCm, tilpassDesignHeightCm, 'PerMeter')} stk totalt` },
            ] as const)"
            :key="opt.value"
            class="eyelet-option"
            :class="{ 'eyelet-option--active': tilpassEyeletOption === opt.value }"
          >
            <input
              type="radio"
              :value="opt.value"
              :checked="tilpassEyeletOption === opt.value"
              style="display:none"
              @change="emit('update:tilpassEyeletOption', opt.value)"
            />
            <div style="flex:1">
              <div style="font-weight:600;font-size:14.5px;color:var(--text)">{{ opt.label }}</div>
              <div style="font-size:13px;color:var(--faint)">{{ opt.sub }}</div>
            </div>
            <div
              v-if="opt.value !== 'None' && tilpassEyeletPriceNok > 0"
              style="font-size:13px;color:var(--accent);font-weight:700;white-space:nowrap"
            >
              +{{ formatNok(countEyelets(tilpassDesignWidthCm, tilpassDesignHeightCm, opt.value) * tilpassEyeletPriceNok) }}
            </div>
            <div class="eyelet-radio">
              <div class="radio-outer" :class="{ 'radio-outer--active': tilpassEyeletOption === opt.value }">
                <div v-if="tilpassEyeletOption === opt.value" class="radio-inner"></div>
              </div>
            </div>
          </label>
        </div>
      </div>

      <!-- Sum -->
      <div class="bb-panel" style="display:grid;gap:10px">
        <!-- BANNERSH-189: manual mode adds a designer-fee line. -->
        <div v-if="isManual" style="display:flex;justify-content:space-between;font-size:14.5px">
          <span style="color:var(--muted)">Designer-tjeneste</span>
          <span style="color:var(--text);font-weight:500">{{ formatNok(manualDesignPriceNok) }}</span>
        </div>
        <div style="display:flex;justify-content:space-between;font-size:14.5px">
          <span style="color:var(--muted)">Bannerpris</span>
          <span style="color:var(--text);font-weight:500">{{ formatNok(tilpassBannerPriceNok) }}</span>
        </div>
        <div v-if="tilpassEyeletFeeNok > 0" style="display:flex;justify-content:space-between;font-size:14.5px">
          <span style="color:var(--muted)">Maljer ({{ tilpassEyeletCount }} stk)</span>
          <span style="color:var(--text);font-weight:500">{{ formatNok(tilpassEyeletFeeNok) }}</span>
        </div>
        <div style="display:flex;justify-content:space-between;font-size:17px;padding-top:10px;border-top:1px solid var(--line-soft)">
          <span style="font-weight:700;color:var(--text)">Sum</span>
          <span style="font-weight:800;color:var(--accent)">{{ formatNok(tilpassTotalNok) }}</span>
        </div>
        <p style="font-size:13px;color:var(--faint);margin:0">
          Frakt og eventuelt ekspressgebyr beregnes i kassen.
        </p>
      </div>

      <!-- CTA row -->
      <div style="display:grid;gap:14px">
        <button
          type="button"
          class="btn"
          style="width:100%;justify-content:center;padding:14px;font-size:16px;border-radius:12px;background:#3a9d7e;color:#fff"
          @click="emit('addToCart')"
        >
          <i class="fa-solid fa-cart-shopping"></i>
          Legg i handlekurven
        </button>
        <button
          type="button"
          class="btn btn-ghost"
          style="justify-content:center;padding:12px;font-size:14.5px;border-radius:12px"
          @click="emit('back')"
        >
          <i class="fa-solid fa-arrow-left" style="font-size:12px"></i> Tilbake
        </button>
      </div>
    </template>

    <!-- Error -->
    <div v-if="tilpassError" class="error-box">
      <i class="fa-solid fa-circle-exclamation"></i> {{ tilpassError }}
    </div>
  </div>
</template>

<style scoped>
/* ── Banner summary / eyelet panels ──────────────────────────── */
.bb-panel {
  background: var(--surface);
  border: 1px solid var(--line-soft);
  border-radius: var(--radius);
  padding: 26px;
}

.field-label {
  display: block;
  font-size: 13px;
  font-weight: 700;
  color: var(--muted);
  margin-bottom: 8px;
  text-transform: uppercase;
  letter-spacing: .04em;
}

/* ── BANNERSH-133: Eyelet (malje) option selector ──────────────── */
.eyelet-option {
  display: flex;
  align-items: center;
  gap: 10px;
  background: var(--surface-2);
  border: 1.5px solid var(--line);
  border-radius: 10px;
  padding: 10px 14px;
  cursor: pointer;
  transition: border-color 0.15s, background 0.15s;
}
.eyelet-option:hover { border-color: var(--muted); }
.eyelet-option--active {
  border-color: var(--accent) !important;
  background: rgba(255, 106, 61, 0.07) !important;
}
.eyelet-radio { flex-shrink: 0; }
.radio-outer {
  width: 16px;
  height: 16px;
  border-radius: 50%;
  border: 2px solid var(--line);
  display: flex;
  align-items: center;
  justify-content: center;
  transition: border-color 0.15s;
}
.radio-outer--active { border-color: var(--accent); }
.radio-inner {
  width: 7px;
  height: 7px;
  border-radius: 50%;
  background: var(--accent);
}

/* ── Error box ───────────────────────────────────────────────── */
.error-box {
  display: flex;
  align-items: center;
  gap: 9px;
  color: #f4a57a;
  background: rgba(255,106,61,.1);
  border: 1px solid rgba(255,106,61,.3);
  border-radius: 10px;
  padding: 10px 14px;
  font-size: 14px;
}
.error-box i { color: var(--accent); flex-shrink: 0; }
</style>

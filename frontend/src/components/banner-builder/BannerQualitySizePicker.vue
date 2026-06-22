<script setup lang="ts">
import { computed } from 'vue'
import type { OptionPriceState, QualityOption } from '@/composables/banner-builder/useBannerPricing'
import { formatNok } from '@/utils/format'

const props = defineProps<{
  option1State: OptionPriceState
  option2State: OptionPriceState
  customState: OptionPriceState
  selectedQuality: QualityOption
  highOptionWidthCm: number
  highOptionHeightCm: number
  goodOptionWidthCm: number
  goodOptionHeightCm: number
  /** Set after the preview image's @load fires; null while loading. */
  aiImageNaturalRatio: number | null
  /** Effective aspect ratio (image-loaded or parsed from aspectRatio string). */
  aiImageAspectRatio: number | null
  customWidth: number | null
  customHeight: number | null
  customMaterialGsm: 400 | 680
}>()

const emit = defineEmits<{
  'update:selectedQuality': [value: QualityOption]
  'update:customWidth': [value: number | null]
  'update:customHeight': [value: number | null]
  'update:customMaterialGsm': [value: 400 | 680]
}>()

// Computed models for v-model.number on the width/height inputs.
const localCustomWidth = computed({
  get: () => props.customWidth,
  set: (v: number | string | null) => emit('update:customWidth', typeof v === 'number' ? v : null),
})
const localCustomHeight = computed({
  get: () => props.customHeight,
  set: (v: number | string | null) => emit('update:customHeight', typeof v === 'number' ? v : null),
})
</script>

<template>
  <div class="bb-panel">
    <div class="field-label" style="margin-bottom:12px">Velg kvalitet og størrelse</div>
    <div class="quality-grid">

      <!-- Option 1: Høykvalitet — height from catalog mult=1 rule, width = height × image ratio (BANNERSH-259) -->
      <button
        type="button"
        class="quality-btn"
        :class="{ 'quality-btn-active': selectedQuality === 'high', 'quality-btn-disabled': option1State.comingSoon }"
        :disabled="option1State.comingSoon"
        @click="!option1State.comingSoon && emit('update:selectedQuality', 'high')"
      >
        <span v-if="option1State.comingSoon" class="coming-soon-pill">Kommer snart</span>
        <div class="quality-btn-title">Høykvalitet</div>
        <div class="quality-btn-sub">3 års fargegaranti</div>
        <div class="quality-btn-dims">
          <template v-if="aiImageNaturalRatio">ca. {{ highOptionWidthCm }} × {{ highOptionHeightCm }} cm</template>
          <i v-else class="fa-solid fa-circle-notch fa-spin" style="font-size:10px;color:var(--faint)"></i>
        </div>
        <div class="quality-btn-price">
          <template v-if="option1State.loading || !aiImageNaturalRatio">
            <i class="fa-solid fa-circle-notch fa-spin" style="font-size:11px"></i>
          </template>
          <template v-else-if="option1State.price !== null">
            {{ formatNok(option1State.price) }}
          </template>
          <template v-else>–</template>
        </div>
      </button>

      <!-- Option 2: God kvalitet — height from catalog mult=1 rule, width = height × image ratio (BANNERSH-259) -->
      <button
        type="button"
        class="quality-btn"
        :class="{ 'quality-btn-active': selectedQuality === 'good', 'quality-btn-disabled': option2State.comingSoon }"
        :disabled="option2State.comingSoon"
        @click="!option2State.comingSoon && emit('update:selectedQuality', 'good')"
      >
        <span v-if="option2State.comingSoon" class="coming-soon-pill">Kommer snart</span>
        <div class="quality-btn-title">God kvalitet</div>
        <div class="quality-btn-sub">3 måneders fargegaranti</div>
        <div class="quality-btn-dims">
          <template v-if="aiImageNaturalRatio">ca. {{ goodOptionWidthCm }} × {{ goodOptionHeightCm }} cm</template>
          <i v-else class="fa-solid fa-circle-notch fa-spin" style="font-size:10px;color:var(--faint)"></i>
        </div>
        <div class="quality-btn-price">
          <template v-if="option2State.loading || !aiImageNaturalRatio">
            <i class="fa-solid fa-circle-notch fa-spin" style="font-size:11px"></i>
          </template>
          <template v-else-if="option2State.price !== null">
            {{ formatNok(option2State.price) }}
          </template>
          <template v-else>–</template>
        </div>
      </button>

      <!-- Option 3: Custom -->
      <button
        type="button"
        class="quality-btn"
        :class="{ 'quality-btn-active': selectedQuality === 'custom' }"
        @click="emit('update:selectedQuality', 'custom')"
      >
        <span v-if="customState.comingSoon" class="coming-soon-pill">Kommer snart</span>
        <div class="quality-btn-title">Egendefinert</div>
        <div class="quality-btn-sub">Velg kvalitet og størrelse</div>
        <div class="quality-btn-dims">skriv inn mål</div>
        <div class="quality-btn-price" style="color:var(--faint);font-size:13px">
          <template v-if="customState.loading">
            <i class="fa-solid fa-circle-notch fa-spin" style="font-size:11px"></i>
          </template>
          <template v-else-if="customState.price !== null">
            {{ formatNok(customState.price) }}
          </template>
          <template v-else>–</template>
        </div>
      </button>

    </div>

    <!-- Custom option inline form (width ↔ height linked via image ratio) -->
    <div v-if="selectedQuality === 'custom'" class="custom-size-form">
      <div style="display:flex;gap:12px;flex-wrap:wrap;align-items:flex-end">
        <div>
          <label class="field-label" style="margin-bottom:6px">Bredde (cm)</label>
          <input
            v-model.number="localCustomWidth"
            type="number"
            min="50"
            max="2000"
            class="dark-input"
            style="width:110px"
            placeholder="f.eks. 300"
          />
        </div>
        <div>
          <label class="field-label" style="margin-bottom:6px">Høyde (cm)</label>
          <input
            v-model.number="localCustomHeight"
            type="number"
            min="50"
            max="500"
            class="dark-input"
            style="width:110px"
            placeholder="f.eks. 150"
          />
        </div>
        <div>
          <label class="field-label" style="margin-bottom:6px">Materialkvalitet</label>
          <div style="display:flex;gap:8px">
            <button
              type="button"
              class="mat-btn"
              :class="{ 'mat-btn-active': customMaterialGsm === 400 }"
              @click="emit('update:customMaterialGsm', 400)"
            >400g</button>
            <button
              type="button"
              class="mat-btn"
              :class="{ 'mat-btn-active': customMaterialGsm === 680 }"
              @click="emit('update:customMaterialGsm', 680)"
            >680g</button>
          </div>
        </div>
      </div>
      <p v-if="aiImageAspectRatio" style="margin-top:8px;font-size:12.5px;color:var(--faint)">
        <i class="fa-solid fa-link"></i>
        Bredde og høyde er låst til bildets forhold — endrer du den ene oppdateres den andre.
      </p>
      <div v-if="customState.comingSoon" style="margin-top:8px;font-size:13px;color:var(--gold)">
        <i class="fa-solid fa-clock"></i> Denne kombinasjonen er ikke tilgjengelig ennå.
      </div>
    </div>
  </div>
</template>

<style scoped>
.bb-panel {
  background: var(--surface);
  border: 1px solid var(--line-soft);
  border-radius: var(--radius);
  padding: 26px;
}

/* ── Form inputs ─────────────────────────────────────────────── */
.field-label {
  display: block;
  font-size: 13px;
  font-weight: 700;
  color: var(--muted);
  margin-bottom: 8px;
  text-transform: uppercase;
  letter-spacing: .04em;
}
.dark-input {
  width: 100%;
  background: var(--surface-2);
  border: 1px solid var(--line);
  border-radius: 10px;
  padding: 10px 14px;
  font-size: 15px;
  color: var(--text);
  font-family: var(--font-ui);
  outline: none;
  transition: border-color 0.15s, box-shadow 0.15s;
}
.dark-input::placeholder { color: var(--faint); }
.dark-input:focus { border-color: var(--accent); box-shadow: 0 0 0 3px rgba(255,106,61,.18); }

/* ── Quality / size selector ─────────────────────────────────── */
.quality-grid {
  display: grid;
  grid-template-columns: repeat(3, 1fr);
  gap: 12px;
}
@media (max-width: 560px) { .quality-grid { grid-template-columns: 1fr; } }

.quality-btn {
  position: relative;
  display: flex;
  flex-direction: column;
  gap: 2px;
  border: 2px solid var(--line);
  border-radius: 14px;
  padding: 14px 16px;
  background: transparent;
  cursor: pointer;
  font-family: var(--font-ui);
  transition: border-color .15s, background .15s, box-shadow .15s;
  text-align: left;
}
.quality-btn:hover:not(:disabled) { border-color: var(--line-soft); color: var(--text); }
.quality-btn:disabled,
.quality-btn-disabled {
  opacity: 0.42;
  cursor: not-allowed;
  pointer-events: none;
}
.quality-btn-active {
  border-color: var(--accent);
  background: rgba(255,106,61,.08);
  color: var(--text);
  box-shadow: 0 0 0 2px rgba(255,106,61,.2);
}
.quality-btn-title {
  font-weight: 700;
  font-size: 15px;
  color: var(--text);
}
.quality-btn-sub {
  font-size: 13px;
  color: var(--muted);
  margin-bottom: 4px;
}
.quality-btn-dims {
  font-size: 13px;
  color: var(--faint);
}
.quality-btn-price {
  margin-top: 8px;
  font-size: 14px;
  font-weight: 700;
  color: var(--accent-2);
}

/* "Kommer snart" pill */
.coming-soon-pill {
  position: absolute;
  top: 8px;
  right: 8px;
  background: rgba(231,185,78,.18);
  color: var(--gold);
  border: 1px solid rgba(231,185,78,.35);
  border-radius: 999px;
  font-size: 11px;
  font-weight: 700;
  padding: 2px 8px;
  pointer-events: none;
}

/* Custom size inline form */
.custom-size-form {
  margin-top: 14px;
  padding: 16px;
  background: var(--surface-2);
  border: 1px solid var(--line-soft);
  border-radius: 12px;
}

/* Material selector buttons */
.mat-btn {
  border: 2px solid var(--line);
  border-radius: 8px;
  padding: 7px 14px;
  font-size: 14px;
  font-weight: 600;
  cursor: pointer;
  background: transparent;
  color: var(--muted);
  transition: border-color .15s, color .15s, background .15s;
  font-family: var(--font-ui);
}
.mat-btn:hover { color: var(--text); }
.mat-btn-active { border-color: var(--accent); color: var(--text); background: rgba(255,106,61,.08); }
</style>

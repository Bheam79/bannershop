<script setup lang="ts">
import { RouterLink } from 'vue-router'
import type { DesignRequestDetail, BannerGenerationHistoryItem } from '@/api/designRequests'
import type { GenPhase } from '@/composables/banner-builder/useBannerGeneration'
import type { QualityOption, OptionPriceState } from '@/composables/banner-builder/useBannerPricing'
import BannerQualitySizePicker from './BannerQualitySizePicker.vue'

const props = defineProps<{
  // ── Phase / generation state ───────────────────────────────────
  genPhase: GenPhase
  genProgress: number
  generateApiError: string | null
  designRequestId: number | null
  currentDesignRequest: DesignRequestDetail | null
  isManual: boolean
  // ── Generation history (ready phase) ──────────────────────────
  hasGenerationHistory: boolean
  completedGenerations: BannerGenerationHistoryItem[]
  activatingGenerationId: number | null
  activateGenerationError: string | null
  // ── Error surfaces ─────────────────────────────────────────────
  approveError: string | null
  regenerateError: string | null
  reorderError: string | null
  manualSubmitError: string | null
  // ── Loading / busy flags ───────────────────────────────────────
  approving: boolean
  reordering: boolean
  manualSubmitting: boolean
  // ── Validation ────────────────────────────────────────────────
  step2Valid: boolean
  // ── Credits / paywall state ───────────────────────────────────
  canGenerateForFree: boolean | null
  hasCreditsAvailable: boolean
  isOutOfGenerations: boolean
  creditsRemaining: number | null
  isLoggedIn: boolean
  generateButtonLabel: string
  // ── Idle placeholder ──────────────────────────────────────────
  templateName: string
  // ── BannerQualitySizePicker forwarded props ────────────────────
  option1State: OptionPriceState
  option2State: OptionPriceState
  customState: OptionPriceState
  selectedQuality: QualityOption
  highOptionWidthCm: number
  highOptionHeightCm: number
  goodOptionWidthCm: number
  goodOptionHeightCm: number
  aiImageNaturalRatio: number | null
  aiImageAspectRatio: number | null
  customWidth: number | null
  customHeight: number | null
  customMaterialGsm: 400 | 680
}>()

const emit = defineEmits<{
  /** Preview image @load — parent calls onPreviewImageLoaded (updates aspect ratio). */
  'previewImageLoaded': [e: Event]
  /** "Tilbake og endre detaljer" / back to wizard idle. */
  'returnToWizardIdle': []
  /** Generate (idle/error phase) or regenerate (ready phase) — parent picks which. */
  'regenerate': []
  /** "Gå videre" — parent calls approve() (AI) or manualGoVidere() (Manual). */
  'proceed': []
  /** "Bestill" / "Bestill på nytt" */
  'reorderCurrentDesign': []
  /** Clicked a generation history thumbnail. */
  'selectGeneration': [gen: BannerGenerationHistoryItem]
  /** Error state "Velg manuell design" button. */
  'switchToManualMode': []
  /** "kjøp kreditter" link in credits hint. */
  'openPaywall': []
  // ── BannerQualitySizePicker v-model forwards ───────────────────
  'update:selectedQuality': [value: QualityOption]
  'update:customWidth': [value: number | null]
  'update:customHeight': [value: number | null]
  'update:customMaterialGsm': [value: 400 | 680]
}>()

function formatGenTime(iso: string | null | undefined): string {
  if (!iso) return '—'
  return new Date(iso).toLocaleTimeString('nb-NO', { hour: '2-digit', minute: '2-digit' })
}
</script>

<template>
  <!-- ── Inline preview + generate area (BANNERSH-146) ─────────────── -->
  <div style="margin-top:20px">

    <!-- Error from generateBanner -->
    <div v-if="generateApiError" class="error-box" style="margin-bottom:12px">
      <i class="fa-solid fa-circle-exclamation"></i> {{ generateApiError }}
    </div>

    <!-- Phase: submitting / generating — spinner inside the frame -->
    <div v-if="genPhase === 'submitting' || genPhase === 'generating'" class="preview-generating">
      <div style="position:relative;width:56px;height:56px">
        <div style="position:absolute;inset:0;border-radius:50%;border:4px solid var(--surface-2)"></div>
        <div style="position:absolute;inset:0;border-radius:50%;border:4px solid transparent;border-top-color:var(--accent);animation:spin 1s linear infinite"></div>
      </div>
      <div style="text-align:center">
        <div class="display" style="font-size:20px;color:var(--text);margin-bottom:4px">
          {{ genPhase === 'submitting' ? 'Sender forespørsel…' : 'Genererer banner…' }}
        </div>
        <p v-if="genPhase === 'generating'" style="font-size:13.5px;color:var(--muted)">
          AI-en jobber med designet ditt. Dette tar vanligvis 20–60 sekunder.
        </p>
      </div>
      <!-- Generation progress bar -->
      <div v-if="genPhase === 'generating'" style="width:100%;max-width:260px">
        <div style="width:100%;height:4px;background:var(--surface-2);border-radius:999px;overflow:hidden">
          <div
            style="height:100%;background:var(--accent);border-radius:999px;transition:width .4s ease"
            :style="{ width: `${genProgress}%` }"
          />
        </div>
      </div>
    </div>

    <!-- Phase: anon_pending — anonymous user after generation -->
    <div v-else-if="genPhase === 'anon_pending'" class="preview-anon">
      <i class="fa-solid fa-circle-check" style="font-size:40px;color:#4ade80;margin-bottom:12px"></i>
      <h3 class="display" style="font-size:20px;color:var(--text);margin-bottom:8px">Banneret genereres!</h3>
      <p style="font-size:14px;color:var(--muted);max-width:28em;text-align:center;margin:0 0 16px">
        Opprett en konto for å se og godkjenne resultatet — og for å bestille det ferdige banneret.
      </p>
      <div style="display:flex;gap:10px;flex-wrap:wrap;justify-content:center">
        <RouterLink :to="`/register?redirect=${encodeURIComponent('/banner-builder/ai?resume=1')}`" class="btn btn-primary" style="padding:10px 20px">
          <i class="fa-solid fa-user-plus"></i> Opprett konto
        </RouterLink>
        <RouterLink :to="`/login?redirect=${encodeURIComponent('/banner-builder/ai?resume=1')}`" class="btn btn-ghost" style="padding:10px 20px">
          Logg inn
        </RouterLink>
      </div>
      <p v-if="designRequestId" style="margin-top:16px;font-size:13px;color:var(--faint)">
        Design-ID: {{ designRequestId }}
      </p>
    </div>

    <!-- Phase: ready — show the generated image (or "Ditt banner" placeholder in manual mode) -->
    <template v-else-if="genPhase === 'ready' && currentDesignRequest">
      <!-- Generation history thumbnails — shown when there are ≥2 completed AI generations -->
      <div
        v-if="!isManual && hasGenerationHistory"
        style="display:flex;gap:8px;overflow-x:auto;padding:10px 12px;background:rgba(0,0,0,.04);border-radius:10px;margin-bottom:8px"
      >
        <button
          v-for="gen in completedGenerations"
          :key="gen.id"
          type="button"
          class="gen-thumb"
          :class="{ 'gen-thumb--active': gen.isActive }"
          :disabled="activatingGenerationId === gen.id || gen.isActive"
          :title="`Versjon ${formatGenTime(gen.completedAt)}`"
          @click="emit('selectGeneration', gen)"
        >
          <span class="gen-thumb__img-wrap">
            <img v-if="gen.previewUrl" :src="gen.previewUrl" />
            <span v-else class="gen-thumb__placeholder">?</span>
            <span v-if="activatingGenerationId === gen.id" class="gen-thumb__spinner">
              <i class="fa-solid fa-circle-notch fa-spin"></i>
            </span>
            <span v-else-if="gen.isActive" class="gen-thumb__check">
              <i class="fa-solid fa-check"></i>
            </span>
          </span>
          <span class="gen-thumb__time">{{ formatGenTime(gen.completedAt) }}</span>
        </button>
      </div>
      <p
        v-if="activateGenerationError && !isManual"
        style="color:#ef4444;font-size:13px;margin:0 0 6px"
      >{{ activateGenerationError }}</p>

      <!-- BANNERSH-251: in manual mode the preview image is hidden entirely —
           the customer goes straight from the personalization form to the
           material picker + "Gå videre" CTA. The synthetic placeholder is
           still generated client-side so currentDesignRequest.previewUrl is
           set (the picker depends on it), but the <img> is not rendered. -->
      <div v-if="!isManual" class="bb-panel" style="padding:0;overflow:hidden;border-radius:0">
        <img
          v-if="currentDesignRequest.previewUrl"
          :src="currentDesignRequest.previewUrl"
          :alt="`AI-generert banner for ${currentDesignRequest.personName}`"
          style="width:100%;height:auto;object-fit:contain;display:block"
          @load="emit('previewImageLoaded', $event)"
        />
        <div v-else style="display:flex;align-items:center;justify-content:center;height:180px;color:var(--faint)">
          Forhåndsvisning ikke tilgjengelig
        </div>
      </div>

      <!-- BANNERSH-162: quality / size picker rendered BELOW the preview.
           Widths for the two preset options are derived from the AI image's
           actual aspect ratio (set in previewImageLoaded), and the custom
           option's width/height inputs auto-link via that same ratio.
           BANNERSH-262: extracted into BannerQualitySizePicker component. -->
      <BannerQualitySizePicker
        v-if="currentDesignRequest.previewUrl"
        :style="isManual ? '' : 'margin-top:20px'"
        :option1State="option1State"
        :option2State="option2State"
        :customState="customState"
        :selectedQuality="selectedQuality"
        :highOptionWidthCm="highOptionWidthCm"
        :highOptionHeightCm="highOptionHeightCm"
        :goodOptionWidthCm="goodOptionWidthCm"
        :goodOptionHeightCm="goodOptionHeightCm"
        :aiImageNaturalRatio="aiImageNaturalRatio"
        :aiImageAspectRatio="aiImageAspectRatio"
        :customWidth="customWidth"
        :customHeight="customHeight"
        :customMaterialGsm="customMaterialGsm"
        @update:selectedQuality="emit('update:selectedQuality', $event)"
        @update:customWidth="emit('update:customWidth', $event)"
        @update:customHeight="emit('update:customHeight', $event)"
        @update:customMaterialGsm="emit('update:customMaterialGsm', $event)"
      />
    </template>

    <!-- Phase: error -->
    <div v-else-if="genPhase === 'error'" class="preview-generating preview-error-frame">
      <i class="fa-solid fa-triangle-exclamation" style="font-size:36px;color:var(--accent);margin-bottom:8px"></i>
      <div class="display" style="font-size:18px;color:var(--text);margin-bottom:6px">Noe gikk galt</div>
      <template v-if="currentDesignRequest?.lastError?.startsWith('moderation_block')">
        <p style="font-size:13.5px;color:var(--muted);text-align:center;max-width:28em">
          Beklager, vår AI kunne ikke lage dette banneret — det ble stoppet av innholdsfilteret. Dette skjer oftest
          med opphavsrettsbeskyttede figurer (f.eks. «spiderman» — prøv heller «Superhelt i edderkopp drakt som
          svinger seg mellom skyskrapere»), men kan også skje pga. det opplastede bildet av personen.<br><br>
          Prøv en annen temabeskrivelse eller et annet bilde, eller velg manuell design hvis du ønsker dette — så
          skal vi se hva vi kan få til!
        </p>
        <button type="button" class="btn btn-ghost" style="margin-top:4px" @click="emit('switchToManualMode')">
          <i class="fa-solid fa-palette"></i> Velg manuell design
        </button>
      </template>
      <p v-else-if="currentDesignRequest?.lastError === 'openai_quota_exceeded'" style="font-size:13.5px;color:var(--muted);text-align:center;max-width:26em">
        AI-genereringen er midlertidig utilgjengelig. Vi jobber med å løse dette — prøv igjen om litt, eller kontakt support.
      </p>
      <p v-else style="font-size:13.5px;color:var(--muted);text-align:center;max-width:26em">
        {{ currentDesignRequest?.lastError ?? 'AI-genereringen feilet. Prøv igjen.' }}
      </p>
    </div>

    <!-- Phase: idle — placeholder frame.
         BANNERSH-251: hidden in manual mode (the step 2 watcher auto-enters
         the 'ready' phase so this branch never renders, but we gate on
         !isManual defensively to avoid a brief flash during the tick the
         watcher schedules in). -->
    <div v-else-if="!isManual" class="preview-placeholder">
      <i class="fa-solid fa-wand-magic-sparkles" style="font-size:30px;color:var(--accent);opacity:.45;margin-bottom:10px"></i>
      <div class="display" style="font-size:20px;color:var(--muted)">{{ templateName || 'AI Banner' }}</div>
      <p style="font-size:13px;color:var(--faint);margin-top:6px">Banneret ditt vil vises her</p>
    </div>

    <!-- Action buttons + credits (hidden while generating or anon_pending) -->
    <div v-if="genPhase !== 'submitting' && genPhase !== 'generating' && genPhase !== 'anon_pending'" style="margin-top:16px;display:grid;gap:12px">

      <!-- Error rows -->
      <div v-if="approveError" class="error-box">
        <i class="fa-solid fa-circle-exclamation"></i> {{ approveError }}
      </div>
      <div v-if="regenerateError" class="error-box">
        <i class="fa-solid fa-circle-exclamation"></i> {{ regenerateError }}
      </div>
      <div v-if="reorderError" class="error-box">
        <i class="fa-solid fa-circle-exclamation"></i> {{ reorderError }}
      </div>

      <!-- "Gå videre" — AwaitingApproval: approve → tilpass (step 3) -->
      <!-- BANNERSH-189: manual mode wires this button to manualGoVidere() which
           creates the DesignRequest via /design-requests/manual instead of the
           AI-only approve endpoint.
           BANNERSH-251: manual mode also gates on step2Valid (the personalize
           form must be filled in) since there is no "Se forhåndsvisning"
           intermediate step to enforce that anymore. -->
      <button
        v-if="genPhase === 'ready' && currentDesignRequest?.status === 'AwaitingApproval'"
        type="button"
        class="btn"
        style="width:100%;justify-content:center;padding:14px;font-size:16px;border-radius:12px;background:#3a9d7e;color:#fff"
        :disabled="isManual ? (manualSubmitting || !step2Valid) : approving"
        @click="emit('proceed')"
      >
        <i v-if="isManual ? manualSubmitting : approving" class="fa-solid fa-circle-notch fa-spin"></i>
        <i v-else class="fa-solid fa-arrow-right"></i>
        <template v-if="isManual">{{ manualSubmitting ? 'Behandler…' : 'Gå videre' }}</template>
        <template v-else>{{ approving ? 'Behandler…' : 'Gå videre' }}</template>
      </button>
      <!-- Manual mode error surface (mirrors approveError below). -->
      <div v-if="isManual && manualSubmitError" class="error-box">
        <i class="fa-solid fa-circle-exclamation"></i> {{ manualSubmitError }}
      </div>

      <!-- "Bestill" / "Bestill på nytt" — Approved / Final (AI only) -->
      <!-- Approved = approved but never ordered (first purchase).
           Final    = already ordered at least once (re-order). -->
      <button
        v-if="!isManual && genPhase === 'ready' && currentDesignRequest?.finalBannerDesignId && (currentDesignRequest?.status === 'Approved' || currentDesignRequest?.status === 'Final')"
        type="button"
        class="btn"
        style="width:100%;justify-content:center;padding:14px;font-size:16px;border-radius:12px;background:#3a9d7e;color:#fff"
        :disabled="reordering"
        @click="emit('reorderCurrentDesign')"
      >
        <i v-if="reordering" class="fa-solid fa-circle-notch fa-spin"></i>
        <i v-else class="fa-solid fa-cart-shopping"></i>
        {{ reordering ? 'Legger i handlekurv…' : currentDesignRequest?.status === 'Final' ? 'Bestill på nytt' : 'Bestill' }}
      </button>

      <!-- Generate (idle) / Regenerate (ready/error) button -->
      <!-- BANNERSH-189: manual mode previously rendered a client-side
           "Ditt banner" placeholder via this button; BANNERSH-251 removes
           that intermediate step entirely — the picker + "Gå videre" CTA
           appear directly. So the generate button is AI-only now. -->
      <button
        v-if="!isManual"
        type="button"
        class="btn btn-primary"
        style="width:100%;justify-content:center;padding:14px;font-size:16px;border-radius:12px"
        :disabled="!step2Valid"
        @click="emit('regenerate')"
      >
        <i v-if="genPhase === 'error'" class="fa-solid fa-rotate"></i>
        <i v-else-if="isOutOfGenerations && genPhase !== 'ready'" class="fa-solid fa-bag-shopping"></i>
        <i v-else class="fa-solid fa-wand-magic-sparkles"></i>
        <template v-if="genPhase === 'ready'">
          <template v-if="canGenerateForFree === true">Generer ny versjon (gratis)</template>
          <template v-else-if="hasCreditsAvailable">Generer ny versjon (1 kreditt)</template>
          <template v-else>Generer ny versjon</template>
        </template>
        <template v-else-if="genPhase === 'idle'">{{ generateButtonLabel }}</template>
        <template v-else>Prøv igjen</template>
      </button>

      <!-- Credits / hint text — AI mode shows credits state, manual mode shows the workflow hint. -->
      <p style="font-size:13px;color:var(--faint);text-align:center;margin:0">
        <template v-if="isManual">
          <i class="fa-solid fa-palette" style="color:var(--accent);margin-right:5px"></i>
          Designeren vår lager forhåndsvisningen og sender den til godkjenning innen 2–3 virkedager.
        </template>
        <template v-else-if="canGenerateForFree === true">
          <i class="fa-solid fa-gift" style="color:var(--accent);margin-right:5px"></i>
          Du har 1 gratis AI bilde igjen
        </template>
        <template v-else-if="isLoggedIn && creditsRemaining !== null && creditsRemaining > 0">
          <i class="fa-solid fa-wand-magic-sparkles" style="color:var(--accent);margin-right:5px"></i>
          Du har {{ creditsRemaining }} AI kreditter igjen
        </template>
        <template v-else-if="isLoggedIn && isOutOfGenerations">
          <i class="fa-solid fa-circle-exclamation" style="color:var(--accent);margin-right:5px"></i>
          Ingen genereringer igjen —
          <button type="button" style="color:var(--accent);font-weight:600;background:none;border:none;cursor:pointer;padding:0;font-family:var(--font-ui);font-size:13px" @click="emit('openPaywall')">kjøp kreditter</button>
        </template>
        <template v-else-if="!isLoggedIn">
          <i class="fa-solid fa-shield-halved" style="margin-right:5px"></i>
          Første generering er gratis — ingen betalingsinformasjon nødvendig
        </template>
      </p>
    </div>
  </div>
</template>

<style scoped>
/* ── Phase: idle placeholder ─────────────────────────────────── */
.preview-placeholder {
  width: 100%;
  aspect-ratio: 16 / 9;
  border: 2px dashed var(--line);
  border-radius: var(--radius);
  background: var(--surface);
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  text-align: center;
  padding: 2rem;
  gap: 4px;
}

/* ── Phase: generating / error frame ─────────────────────────── */
.preview-generating {
  width: 100%;
  aspect-ratio: 16 / 9;
  border: 1px solid var(--line-soft);
  border-radius: var(--radius);
  background: var(--surface);
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 20px;
  padding: 2rem;
}
.preview-error-frame {
  border-color: rgba(255,106,61,.3);
  background: rgba(255,106,61,.04);
}

/* ── Phase: anon_pending ─────────────────────────────────────── */
.preview-anon {
  width: 100%;
  border: 1px solid var(--line-soft);
  border-radius: var(--radius);
  background: var(--surface);
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  padding: 2.5rem 2rem;
  text-align: center;
}

/* ── Spinner animation ───────────────────────────────────────── */
@keyframes spin { to { transform: rotate(360deg); } }

/* ── Panel (image wrapper in ready phase) ────────────────────── */
.bb-panel {
  background: var(--surface);
  border: 1px solid var(--line-soft);
  border-radius: var(--radius);
  padding: 26px;
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

/* ── Generation history thumbnail strip ──────────────────────── */
.gen-thumb {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 4px;
  flex-shrink: 0;
  background: transparent;
  border: 2px solid transparent;
  border-radius: 8px;
  padding: 4px;
  cursor: pointer;
  transition: border-color .15s, opacity .15s;
  opacity: .65;
}
.gen-thumb:hover:not(:disabled) { opacity: 1; }
.gen-thumb--active {
  border-color: var(--accent, #3a9d7e);
  opacity: 1;
  cursor: default;
}
.gen-thumb__img-wrap {
  position: relative;
  width: 72px;
  height: 48px;
  border-radius: 5px;
  overflow: hidden;
  background: var(--surface-2, #eee);
}
.gen-thumb__img-wrap img {
  width: 100%;
  height: 100%;
  object-fit: cover;
  display: block;
}
.gen-thumb__placeholder {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 100%;
  height: 100%;
  font-size: 18px;
  color: var(--muted);
}
.gen-thumb__spinner,
.gen-thumb__check {
  position: absolute;
  top: 3px;
  right: 3px;
  width: 16px;
  height: 16px;
  border-radius: 50%;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 8px;
}
.gen-thumb__spinner {
  background: rgba(0,0,0,.55);
  color: #fff;
}
.gen-thumb__check {
  background: var(--accent, #3a9d7e);
  color: #fff;
}
.gen-thumb__time {
  font-size: 10px;
  color: var(--muted);
  white-space: nowrap;
  line-height: 1;
}
</style>

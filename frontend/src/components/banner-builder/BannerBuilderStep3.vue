<script setup lang="ts">
import { ref } from 'vue'
import { RouterLink } from 'vue-router'
import type { DesignRequestDetail, BannerTemplateItem, BannerGenerationHistoryItem } from '@/api/designRequests'
import type { GenPhase } from '@/composables/banner-builder/useBannerGeneration'
import type { QualityOption } from '@/composables/banner-builder/useBannerPricing'
import type { BannerSize, EyeletOption } from '@/types'
import BannerTilpassPanel from './BannerTilpassPanel.vue'

const props = defineProps<{
  // ── Phase / generation state ──────────────────────────────────
  genPhase: GenPhase
  isManual: boolean
  currentDesignRequest: DesignRequestDetail | null
  designRequestId: number | null
  // ── Error states ──────────────────────────────────────────────
  generateApiError: string | null
  approveError: string | null
  regenerateError: string | null
  reorderError: string | null
  activateGenerationError: string | null
  // ── Busy flags ────────────────────────────────────────────────
  approving: boolean
  regenerating: boolean
  reordering: boolean
  // ── Validation ───────────────────────────────────────────────
  step2Valid: boolean
  // ── Credits / generation ──────────────────────────────────────
  canGenerateForFree: boolean | null
  hasCreditsAvailable: boolean
  isOutOfGenerations: boolean
  creditsRemaining: number | null
  isLoggedIn: boolean
  generateButtonLabel: string
  generateButtonSubtitle: string
  // ── Generation history (ready phase) ─────────────────────────
  hasGenerationHistory: boolean
  completedGenerations: BannerGenerationHistoryItem[]
  activatingGenerationId: number | null
  // ── Generation progress ───────────────────────────────────────
  genProgress: number
  // ── Idle summary ─────────────────────────────────────────────
  selectedTemplate: BannerTemplateItem | null
  templateName: string
  personName: string
  personAge: number | null
  textContent: string
  themeDescription: string
  selectedQuality: QualityOption
  highOptionWidthCm: number
  highOptionHeightCm: number
  goodOptionWidthCm: number
  goodOptionHeightCm: number
  customWidth: number | null
  customHeight: number | null
  uploadedPhotoBannerDesignId: number | null
  photoPreviewUrl: string | null
  language: 'nb' | 'en'
  categoryIconClass: Record<string, string>
  // ── Edit panel (ready phase) ──────────────────────────────────
  templates: BannerTemplateItem[]
  selectedTemplateId: number | null
  photoDragging: boolean
  photoUploading: boolean
  photoUploadProgress: number
  photoUploadError: string | null
  // ── BannerTilpassPanel forwarded props ────────────────────────
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
}>()

const emit = defineEmits<{
  /** Preview image @load — updates aspect ratio in parent. */
  'previewImageLoaded': [e: Event]
  /** Generate (idle phase) — calls generateBanner in parent. */
  'generate': []
  /** Regenerate from the ready phase. */
  'regenerate': []
  /** "Godkjenn og tilpass" — approve (AI) or manualGoVidere (manual). */
  'proceed': []
  /** "Bestill" / "Bestill på nytt" in Approved/Final state. */
  'reorderCurrentDesign': []
  /** Clicked a generation history thumbnail. */
  'selectGeneration': [gen: BannerGenerationHistoryItem]
  /** "Tilbake" / "Kopier og lag ny versjon" — returns wizard to idle state. */
  'returnToWizardIdle': []
  /** "Velg manuell design" in error state. */
  'switchToManualMode': []
  /** "Tilbake" back button in idle phase (goes to step 2). */
  'back': []
  /** "Prøv igjen" in error phase — resets genPhase to idle + clears error. */
  'resetToIdle': []
  /** "Legg i handlekurven" from BannerTilpassPanel. */
  'addToCart': []
  /** "Tilbake" from BannerTilpassPanel. */
  'backFromTilpass': []
  // ── BannerTilpassPanel v-model forward ────────────────────────
  'update:tilpassEyeletOption': [value: EyeletOption]
  // ── Edit panel v-model forwards ──────────────────────────────
  'update:selectedTemplateId': [value: number | null]
  'update:personName': [value: string]
  'update:textContent': [value: string]
  'update:themeDescription': [value: string]
  // ── Photo upload in edit panel ────────────────────────────────
  'onPhotoFileChange': [e: Event]
  'onPhotoDragOver': [e: DragEvent]
  'onPhotoDragLeave': []
  'onPhotoDrop': [e: DragEvent]
  'removePhoto': []
}>()

// ── Local state ───────────────────────────────────────────────────────────────

/** Controls the expandable "Rediger og generer ny versjon" panel. */
const editExpanded = ref(false)

/** Hidden file input used by the edit panel's photo re-upload zone. */
const photoFileInput = ref<HTMLInputElement | null>(null)

// ── Local helpers ─────────────────────────────────────────────────────────────

function formatGenTime(iso: string | null | undefined): string {
  if (!iso) return '—'
  return new Date(iso).toLocaleTimeString('nb-NO', { hour: '2-digit', minute: '2-digit' })
}

/** Trigger the hidden file input for the edit panel photo upload. */
function openPhotoPicker() {
  if (props.photoUploading) return
  photoFileInput.value?.click()
}

/** Collapse edit panel then emit returnToWizardIdle. */
function handleReturnToWizardIdle() {
  editExpanded.value = false
  emit('returnToWizardIdle')
}
</script>

<template>
  <!-- ── Phase: idle (summary + generate button) ─────────────────────────── -->
  <div v-if="genPhase === 'idle'" style="display:grid;grid-template-columns:1.2fr .8fr;gap:24px" class="pay-grid">
    <!-- Left: Generate -->
    <div style="display:grid;gap:20px">
      <div v-if="generateApiError" class="error-box">
        <i class="fa-solid fa-circle-exclamation"></i> {{ generateApiError }}
      </div>

      <div class="bb-panel" style="display:flex;flex-direction:column;gap:14px">
        <h2 class="display" style="font-size:18px;color:var(--text);display:flex;align-items:center;gap:10px">
          <i class="fa-solid fa-wand-magic-sparkles" style="color:var(--accent)"></i>
          Klar til å generere
        </h2>
        <p style="font-size:14px;color:var(--muted)">
          <template v-if="canGenerateForFree === true">
            AI-en lager et unikt banner basert på informasjonen din. Første generering er <strong style="color:var(--text)">gratis</strong>.
          </template>
          <template v-else-if="hasCreditsAvailable">
            AI-en lager et unikt banner basert på informasjonen din. Bruker <strong style="color:var(--text)">1 av {{ creditsRemaining }} kreditter</strong>.
          </template>
          <template v-else-if="isOutOfGenerations">
            Du har brukt opp den gratis genereringen. Kjøp en kredittpakke for å lage flere banner.
          </template>
          <template v-else>
            AI-en lager et unikt banner basert på informasjonen din. Første generering er <strong style="color:var(--text)">gratis</strong>.
          </template>
        </p>
        <button
          type="button"
          class="btn btn-primary"
          style="width:100%;justify-content:center;padding:15px;font-size:16px;border-radius:13px"
          @click="emit('generate')"
        >
          <i v-if="isOutOfGenerations" class="fa-solid fa-bag-shopping"></i>
          <i v-else class="fa-solid fa-wand-magic-sparkles"></i>
          {{ generateButtonLabel }}
        </button>
        <p style="font-size:13px;color:var(--faint);text-align:center;display:flex;align-items:center;justify-content:center;gap:6px">
          <i class="fa-solid fa-shield-halved"></i>
          {{ generateButtonSubtitle }}
        </p>
      </div>
    </div>

    <!-- Right: Summary -->
    <aside>
      <div class="bb-panel" style="position:sticky;top:20px;display:grid;gap:16px">
        <h2 class="display" style="font-size:17px;color:var(--text)">Oppsummering</h2>
        <dl style="display:grid;gap:12px">
          <div>
            <dt class="field-label" style="margin-bottom:3px">Mal</dt>
            <dd style="color:var(--text);font-weight:600;display:flex;align-items:center;gap:8px">
              <i :class="['fa-solid', categoryIconClass[selectedTemplate?.category ?? ''] ?? 'fa-star']" style="color:var(--accent)"></i>
              {{ templateName }}
            </dd>
          </div>
          <div>
            <dt class="field-label" style="margin-bottom:3px">Navn</dt>
            <dd style="color:var(--text)">{{ personName }}<span v-if="personAge">, {{ personAge }} år</span></dd>
          </div>
          <div>
            <dt class="field-label" style="margin-bottom:3px">Bannertekst</dt>
            <dd style="color:var(--muted);font-style:italic">{{ textContent }}</dd>
          </div>
          <div>
            <dt class="field-label" style="margin-bottom:3px">Tema</dt>
            <dd style="color:var(--text)">{{ themeDescription }}</dd>
          </div>
          <div>
            <dt class="field-label" style="margin-bottom:3px">Størrelse</dt>
            <dd style="color:var(--text)">
              <span v-if="selectedQuality === 'high'">Høykvalitet — ca. {{ highOptionWidthCm }} × {{ highOptionHeightCm }} cm</span>
              <span v-else-if="selectedQuality === 'good'">God kvalitet — ca. {{ goodOptionWidthCm }} × {{ goodOptionHeightCm }} cm</span>
              <span v-else>Egendefinert — {{ customWidth ?? '?' }} × {{ customHeight ?? '?' }} cm</span>
            </dd>
          </div>
          <div v-if="uploadedPhotoBannerDesignId">
            <dt class="field-label" style="margin-bottom:3px">Portrettfoto</dt>
            <dd style="color:var(--text);display:flex;align-items:center;gap:8px">
              <img v-if="photoPreviewUrl" :src="photoPreviewUrl" style="width:38px;height:38px;object-fit:cover;border-radius:8px;border:1px solid var(--line-soft)" alt="Portrettfoto" />
              <span><i class="fa-solid fa-circle-check" style="color:#4ade80"></i> Lastet opp</span>
            </dd>
          </div>
          <div>
            <dt class="field-label" style="margin-bottom:3px">Språk</dt>
            <dd style="color:var(--text)">{{ language === 'nb' ? '🇳🇴 Norsk' : '🇬🇧 English' }}</dd>
          </div>
        </dl>
      </div>
    </aside>
  </div>

  <!-- ── Phase: submitting ────────────────────────────────────────────────── -->
  <div v-else-if="genPhase === 'submitting'" style="text-align:center;padding:4rem 0">
    <i class="fa-solid fa-circle-notch fa-spin" style="font-size:36px;color:var(--accent);margin-bottom:18px;display:block"></i>
    <p style="color:var(--muted);font-size:16px">Sender forespørsel…</p>
  </div>

  <!-- ── Phase: generating (polling) ──────────────────────────────────────── -->
  <div v-else-if="genPhase === 'generating'" style="text-align:center;padding:5rem 0">
    <div style="display:inline-flex;flex-direction:column;align-items:center;gap:24px">
      <div style="position:relative;width:72px;height:72px">
        <div style="position:absolute;inset:0;border-radius:50%;border:4px solid var(--surface-2)"></div>
        <div style="position:absolute;inset:0;border-radius:50%;border:4px solid transparent;border-top-color:var(--accent);animation:spin 1s linear infinite"></div>
      </div>
      <div>
        <h2 class="display" style="font-size:26px;color:var(--text);margin-bottom:8px">Genererer banner…</h2>
        <p style="color:var(--muted);max-width:28em">AI-en jobber med designet ditt. Dette tar vanligvis 20–60 sekunder.</p>
      </div>
      <div style="display:grid;gap:10px;width:240px;text-align:left">
        <div style="display:flex;align-items:center;gap:10px;font-size:14px;color:var(--muted)">
          <span style="width:18px;height:18px;border-radius:50%;background:var(--accent);display:grid;place-items:center;flex-shrink:0">
            <i class="fa-solid fa-check" style="font-size:9px;color:var(--accent-ink)"></i>
          </span>
          Forespørsel mottatt
        </div>
        <div style="display:flex;align-items:center;gap:10px;font-size:14px;color:var(--text)">
          <span style="width:18px;height:18px;border-radius:50%;border:2px solid var(--accent);background:rgba(255,106,61,.15);animation:pulse 1.4s ease-in-out infinite;flex-shrink:0"></span>
          Lager AI-design…
        </div>
        <div style="display:flex;align-items:center;gap:10px;font-size:14px;color:var(--faint)">
          <span style="width:18px;height:18px;border-radius:50%;background:var(--surface-2);border:1px solid var(--line);flex-shrink:0"></span>
          Klart til godkjenning
        </div>
      </div>
      <!-- Generation progress bar -->
      <div style="width:240px">
        <div style="width:100%;height:5px;background:var(--surface-2);border-radius:999px;overflow:hidden">
          <div
            style="height:100%;background:var(--accent);border-radius:999px;transition:width .4s ease"
            :style="{ width: `${genProgress}%` }"
          />
        </div>
      </div>
    </div>
  </div>

  <!-- ── Phase: anon_pending (anonymous user, can't poll) ──────────────────── -->
  <div v-else-if="genPhase === 'anon_pending'" style="text-align:center;padding:4rem 1rem">
    <i class="fa-solid fa-circle-check" style="font-size:52px;color:#4ade80;margin-bottom:18px;display:block"></i>
    <h2 class="display" style="font-size:26px;color:var(--text);margin-bottom:10px">Banneret genereres!</h2>
    <p style="color:var(--muted);max-width:34em;margin:0 auto 24px">
      AI-en jobber med designet ditt. Opprett en konto for å se og godkjenne resultatet — og for å bestille det ferdige banneret.
    </p>
    <div style="display:flex;gap:12px;justify-content:center;flex-wrap:wrap">
      <RouterLink :to="`/register?redirect=${encodeURIComponent('/banner-builder/ai?resume=1')}`" class="btn btn-primary" style="padding:12px 24px">
        <i class="fa-solid fa-user-plus"></i> Opprett konto
      </RouterLink>
      <RouterLink :to="`/login?redirect=${encodeURIComponent('/banner-builder/ai?resume=1')}`" class="btn btn-ghost" style="padding:12px 24px">
        Logg inn
      </RouterLink>
    </div>
    <p v-if="designRequestId" style="margin-top:20px;font-size:13px;color:var(--faint)">
      Design-ID: {{ designRequestId }} — lagret lokalt, tilgjengelig etter innlogging.
    </p>
  </div>

  <!-- ── Phase: ready (preview + edit-and-regenerate) ─────────────────────── -->
  <div v-else-if="genPhase === 'ready' && currentDesignRequest" style="display:grid;gap:24px">
    <div style="text-align:center">
      <h2 class="display" style="font-size:28px;color:var(--text);margin-bottom:8px">
        <i class="fa-solid fa-party-horn" style="color:var(--accent);margin-right:8px"></i>
        Banneret ditt er klart!
      </h2>
      <p style="color:var(--muted)">Se over designet og godkjenn, eller juster og generer en ny versjon.</p>
    </div>

    <!-- Generation history thumbnails — shown when there are ≥2 completed AI generations -->
    <div
      v-if="!isManual && hasGenerationHistory"
      style="display:flex;gap:8px;overflow-x:auto;padding:10px 12px;background:rgba(0,0,0,.04);border-radius:10px"
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
      style="color:#ef4444;font-size:13px;margin:0"
    >{{ activateGenerationError }}</p>

    <!-- Preview -->
    <div class="bb-panel" style="padding:0;overflow:hidden;border-radius:0">
      <img
        v-if="currentDesignRequest.previewUrl"
        :src="currentDesignRequest.previewUrl"
        :alt="isManual ? 'Ditt banner — forhåndsvisning' : `AI-generert banner for ${currentDesignRequest.personName}`"
        style="width:100%;height:auto;object-fit:contain;display:block"
        @load="emit('previewImageLoaded', $event)"
      />
      <div v-else style="display:flex;align-items:center;justify-content:center;height:240px;color:var(--faint)">
        Forhåndsvisning ikke tilgjengelig
      </div>
    </div>

    <!-- Approved / Final status + reorder / copy actions (BANNERSH-130) -->
    <div
      v-if="currentDesignRequest.status === 'Approved' || currentDesignRequest.status === 'Final'"
      style="display:grid;gap:14px"
    >
      <div style="display:flex;align-items:center;gap:10px;background:rgba(74,222,128,.1);border:1px solid rgba(74,222,128,.25);border-radius:12px;padding:14px 18px;color:#4ade80;font-size:14px">
        <i class="fa-solid fa-circle-check"></i>
        Banneret er godkjent og sendt til produksjon.
      </div>
      <!-- Reorder + copy actions -->
      <div style="display:flex;gap:14px;flex-wrap:wrap">
        <button
          v-if="currentDesignRequest.finalBannerDesignId"
          type="button"
          class="btn"
          style="flex:1;justify-content:center;padding:14px;font-size:15px;border-radius:12px;background:#3a9d7e;color:#fff;min-width:200px"
          :disabled="reordering"
          @click="emit('reorderCurrentDesign')"
        >
          <i v-if="reordering" class="fa-solid fa-circle-notch fa-spin"></i>
          <i v-else class="fa-solid fa-cart-shopping"></i>
          {{ reordering ? 'Legger i handlekurv…' : currentDesignRequest.status === 'Final' ? 'Bestill på nytt' : 'Bestill' }}
        </button>
        <button
          type="button"
          class="btn btn-ghost"
          style="flex:1;justify-content:center;padding:14px;font-size:15px;border-radius:12px;min-width:200px"
          @click="handleReturnToWizardIdle"
        >
          <i class="fa-solid fa-copy"></i>
          Kopier og lag ny versjon
        </button>
      </div>
      <div v-if="reorderError" class="error-box">
        <i class="fa-solid fa-circle-exclamation"></i> {{ reorderError }}
      </div>
    </div>

    <!-- Action buttons (AwaitingApproval) -->
    <!-- BANNERSH-133: button flow re-ordered.
         Row 1: Back (left) + Generer ny versjon (right) — both secondary actions.
         Row 2: Green "Godkjenn og tilpass" (full width) — the primary call-to-action
         that proceeds to the eyelet (malje) picker step. -->
    <div v-if="currentDesignRequest.status === 'AwaitingApproval'" style="display:grid;gap:14px">
      <div style="display:flex;gap:14px;flex-wrap:wrap">
        <button
          type="button"
          class="btn btn-ghost"
          style="flex:1;justify-content:center;padding:14px;font-size:15px;border-radius:12px;min-width:220px"
          @click="handleReturnToWizardIdle"
        >
          <i class="fa-solid fa-arrow-left"></i>
          Tilbake
        </button>
        <button
          type="button"
          class="btn btn-ghost"
          style="flex:1;justify-content:center;padding:14px;font-size:15px;border-radius:12px;min-width:220px"
          :disabled="regenerating"
          @click="emit('regenerate')"
        >
          <i v-if="regenerating" class="fa-solid fa-circle-notch fa-spin"></i>
          <i v-else class="fa-solid fa-rotate"></i>
          <template v-if="canGenerateForFree === true">Generer ny versjon (gratis)</template>
          <template v-else-if="hasCreditsAvailable">Generer ny versjon (1 kreditt)</template>
          <template v-else>Generer ny versjon (krever kreditter)</template>
        </button>
      </div>
      <button
        type="button"
        class="btn"
        style="width:100%;justify-content:center;padding:14px;font-size:16px;border-radius:12px;background:#3a9d7e;color:#fff"
        :disabled="approving"
        @click="emit('proceed')"
      >
        <i v-if="approving" class="fa-solid fa-circle-notch fa-spin"></i>
        <i v-else class="fa-solid fa-circle-check"></i>
        Godkjenn og tilpass
      </button>
    </div>

    <!-- Credits badge inline -->
    <div
      v-if="isLoggedIn && creditsRemaining !== null && currentDesignRequest.status === 'AwaitingApproval'"
      style="font-size:13px;color:var(--faint);text-align:center"
    >
      <i class="fa-solid fa-wand-magic-sparkles" style="color:var(--accent);margin-right:5px"></i>
      <template v-if="canGenerateForFree === true">1 gratis generering tilgjengelig</template>
      <template v-else>{{ creditsRemaining }} AI forslag igjen</template>
    </div>

    <!-- Errors -->
    <div v-if="approveError" class="error-box">
      <i class="fa-solid fa-circle-exclamation"></i> {{ approveError }}
    </div>
    <div v-if="regenerateError" class="error-box">
      <i class="fa-solid fa-circle-exclamation"></i> {{ regenerateError }}
    </div>

    <!-- ── Edit-and-regenerate panel ──────────────────────────────────────── -->
    <div v-if="currentDesignRequest.status === 'AwaitingApproval'" class="bb-panel" style="display:grid;gap:0">
      <button
        type="button"
        style="display:flex;align-items:center;gap:10px;background:none;border:none;cursor:pointer;padding:4px 0;font-family:var(--font-ui);font-size:14.5px;font-weight:700;color:var(--muted);text-align:left"
        @click="editExpanded = !editExpanded"
      >
        <i :class="['fa-solid', editExpanded ? 'fa-chevron-down' : 'fa-chevron-right']" style="font-size:12px;color:var(--faint)"></i>
        <i class="fa-solid fa-pen-to-square" style="color:var(--accent);font-size:13px"></i>
        Rediger og generer ny versjon
      </button>

      <div v-if="editExpanded" style="display:grid;gap:16px;margin-top:18px;padding-top:18px;border-top:1px solid var(--line-soft)">
        <p style="font-size:13px;color:var(--faint)">
          Endre feltene under og klikk <em>Generer ny versjon</em> — tekst og tema oppdateres på nytt design.
        </p>

        <!-- Template selection inline -->
        <div>
          <div class="field-label" style="margin-bottom:10px">Feiringsmal</div>
          <div class="tpl-grid tpl-grid-sm">
            <button
              v-for="t in templates"
              :key="t.id"
              type="button"
              class="tpl-card"
              :class="{ 'tpl-card-sel': selectedTemplateId === t.id }"
              @click="emit('update:selectedTemplateId', t.id)"
            >
              <span class="tpl-ico" style="width:34px;height:34px;font-size:15px">
                <i :class="['fa-solid', categoryIconClass[t.category] ?? 'fa-star']"></i>
              </span>
              <span style="font-size:13px;font-weight:600;color:var(--text);text-align:center;line-height:1.3">
                {{ language === 'en' ? t.nameEn : t.nameNb }}
              </span>
            </button>
          </div>
        </div>

        <!-- Person name -->
        <div>
          <label for="editPersonName" class="field-label">Navn</label>
          <input
            id="editPersonName"
            type="text"
            maxlength="200"
            class="dark-input"
            :value="personName"
            @input="emit('update:personName', ($event.target as HTMLInputElement).value)"
          />
        </div>

        <!-- Banner text -->
        <div>
          <label for="editTextContent" class="field-label">Tekst på banneret <span style="color:var(--accent)">*</span></label>
          <textarea
            id="editTextContent"
            rows="3"
            maxlength="500"
            class="dark-input"
            style="resize:none"
            :value="textContent"
            @input="emit('update:textContent', ($event.target as HTMLTextAreaElement).value)"
          />
          <p style="margin-top:4px;font-size:13px;color:var(--faint)">{{ textContent.length }} / 500 tegn</p>
        </div>

        <!-- Theme -->
        <div>
          <label for="editThemeDescription" class="field-label">Tema / stil <span style="color:var(--accent)">*</span></label>
          <input
            id="editThemeDescription"
            type="text"
            maxlength="500"
            class="dark-input"
            :value="themeDescription"
            @input="emit('update:themeDescription', ($event.target as HTMLInputElement).value)"
          />
        </div>

        <!-- Photo (re-upload) -->
        <div>
          <div class="field-label" style="margin-bottom:8px">Portrettfoto</div>
          <div v-if="photoPreviewUrl" style="display:flex;align-items:center;gap:12px">
            <img :src="photoPreviewUrl" style="width:64px;height:64px;object-fit:cover;border-radius:10px;border:1px solid var(--line-soft)" alt="Portrettfoto" />
            <button type="button" style="font-size:13px;color:var(--accent);background:none;border:none;cursor:pointer;font-weight:600;padding:0" @click="emit('removePhoto')">
              <i class="fa-solid fa-trash-can"></i> Fjern
            </button>
          </div>
          <div v-else>
            <div
              role="button"
              tabindex="0"
              class="upload-zone"
              style="padding:1.5rem"
              :class="{ 'upload-zone-drag': photoDragging, 'upload-zone-busy': photoUploading }"
              @click="openPhotoPicker"
              @keydown.enter.prevent="openPhotoPicker"
              @dragover="emit('onPhotoDragOver', $event)"
              @dragleave="emit('onPhotoDragLeave')"
              @drop="emit('onPhotoDrop', $event)"
            >
              <input ref="photoFileInput" type="file" style="display:none" accept="image/jpeg,image/png,image/webp" @change="emit('onPhotoFileChange', $event)" />
              <i class="fa-solid fa-user-circle" style="font-size:24px;color:var(--faint);margin-bottom:8px"></i>
              <p style="font-size:13px;color:var(--text)">Klikk for å laste opp portrettfoto</p>
              <div v-if="photoUploading" class="upload-overlay">
                <span style="font-size:13px;color:var(--text)">{{ photoUploadProgress }}%</span>
              </div>
            </div>
            <div v-if="photoUploadError" class="error-box" style="margin-top:8px">
              <i class="fa-solid fa-circle-exclamation"></i> {{ photoUploadError }}
            </div>
          </div>
        </div>

        <!-- Regenerate CTA -->
        <button
          type="button"
          class="btn btn-primary"
          style="width:100%;justify-content:center;padding:13px;font-size:15px;border-radius:12px"
          :disabled="regenerating || !step2Valid"
          @click="emit('regenerate')"
        >
          <i v-if="regenerating" class="fa-solid fa-circle-notch fa-spin"></i>
          <i v-else class="fa-solid fa-rotate"></i>
          {{ regenerating ? 'Genererer…' : 'Generer ny versjon' }}
        </button>
      </div>
    </div>
  </div>

  <!-- ── Phase: tilpass (eyelet picker + add-to-cart) ─────────────────────── -->
  <!-- BANNERSH-133: post-approval step where the customer picks an eyelet
       option and sees the running total before sending the banner to the cart.
       BANNERSH-264: extracted into BannerTilpassPanel component. -->
  <BannerTilpassPanel
    v-if="genPhase === 'tilpass' && currentDesignRequest"
    :isManual="isManual"
    :tilpassLoading="tilpassLoading"
    :tilpassError="tilpassError"
    :tilpassBannerSize="tilpassBannerSize"
    :tilpassDesignWidthCm="tilpassDesignWidthCm"
    :tilpassDesignHeightCm="tilpassDesignHeightCm"
    :tilpassBannerPriceNok="tilpassBannerPriceNok"
    :tilpassEyeletPriceNok="tilpassEyeletPriceNok"
    :tilpassEyeletOption="tilpassEyeletOption"
    @update:tilpassEyeletOption="emit('update:tilpassEyeletOption', $event)"
    :tilpassEyeletCount="tilpassEyeletCount"
    :tilpassEyeletFeeNok="tilpassEyeletFeeNok"
    :tilpassTotalNok="tilpassTotalNok"
    :manualDesignPriceNok="manualDesignPriceNok"
    :currentDesignRequestPreviewUrl="currentDesignRequest?.previewUrl ?? null"
    @addToCart="emit('addToCart')"
    @back="emit('backFromTilpass')"
  />

  <!-- ── Phase: error ──────────────────────────────────────────────────────── -->
  <div v-else-if="genPhase === 'error'" style="text-align:center;padding:4rem 0">
    <i class="fa-solid fa-triangle-exclamation" style="font-size:52px;color:var(--accent);margin-bottom:18px;display:block"></i>
    <h2 class="display" style="font-size:26px;color:var(--text);margin-bottom:10px">Noe gikk galt</h2>
    <template v-if="currentDesignRequest?.lastError === 'moderation_block'">
      <p style="color:var(--muted);margin-bottom:24px;max-width:34em;margin-left:auto;margin-right:auto">
        Beklager, vår AI kan ikke lage plakater med opphavsrettsbeskyttede karakterer og innhold.<br><br>
        I stedet for f.eks. «spiderman», prøv «Superhelt i edderkopp drakt som svinger seg mellom skyskrapere».<br><br>
        Eventuelt velg manuell design hvis du ønsker dette — så skal vi se hva vi kan få til!
      </p>
      <div style="display:flex;gap:12px;justify-content:center;flex-wrap:wrap">
        <button type="button" class="btn btn-primary" @click="emit('resetToIdle')">
          <i class="fa-solid fa-rotate"></i> Prøv igjen med annet tema
        </button>
        <button type="button" class="btn btn-ghost" @click="emit('switchToManualMode')">
          <i class="fa-solid fa-palette"></i> Manuell design
        </button>
      </div>
    </template>
    <template v-else>
      <p style="color:var(--muted);margin-bottom:24px;max-width:30em;margin-left:auto;margin-right:auto">
        {{ currentDesignRequest?.lastError ?? 'AI-genereringen feilet. Prøv igjen eller kontakt support.' }}
      </p>
      <div style="display:flex;gap:12px;justify-content:center;flex-wrap:wrap">
        <button type="button" class="btn btn-primary" @click="emit('resetToIdle')">
          <i class="fa-solid fa-rotate"></i> Prøv igjen
        </button>
        <RouterLink to="/account" class="btn btn-ghost">
          <i class="fa-solid fa-house"></i> Min konto
        </RouterLink>
      </div>
    </template>
  </div>

  <!-- ── Back button (only in idle phase) ─────────────────────────────────── -->
  <div v-if="genPhase === 'idle'" style="margin-top:24px">
    <button type="button" class="btn btn-ghost" @click="emit('back')">
      <i class="fa-solid fa-arrow-left" style="font-size:12px"></i> Tilbake
    </button>
  </div>
</template>

<style scoped>
/* ── Two-column layout (idle phase) ──────────────────────────── */
.pay-grid { grid-template-columns: 1.2fr .8fr; }
@media (max-width: 768px) { .pay-grid { grid-template-columns: 1fr !important; } }

/* ── Panel ───────────────────────────────────────────────────── */
.bb-panel {
  background: var(--surface);
  border: 1px solid var(--line-soft);
  border-radius: var(--radius);
  padding: 26px;
}

/* ── Form labels ─────────────────────────────────────────────── */
.field-label {
  display: block;
  font-size: 13px;
  font-weight: 700;
  color: var(--muted);
  margin-bottom: 8px;
  text-transform: uppercase;
  letter-spacing: .04em;
}

/* ── Form inputs ─────────────────────────────────────────────── */
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

/* ── Photo upload zone ───────────────────────────────────────── */
.upload-zone {
  position: relative;
  width: 100%;
  border-radius: 14px;
  border: 2px dashed var(--line);
  background: var(--surface-2);
  cursor: pointer;
  user-select: none;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  text-align: center;
  padding: 2.5rem 1.5rem;
  transition: border-color 0.15s, background 0.15s;
}
.upload-zone:hover { border-color: var(--accent); background: rgba(255,106,61,.05); }
.upload-zone-drag { border-color: var(--accent); background: rgba(255,106,61,.1); }
.upload-zone-busy { opacity: 0.6; cursor: progress; }

.upload-overlay {
  position: absolute;
  inset: 0;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  background: rgba(21,18,14,.85);
  border-radius: 12px;
}

/* ── Template grid (edit panel) ──────────────────────────────── */
.tpl-grid {
  display: grid;
  grid-template-columns: repeat(4, 1fr);
  gap: 12px;
}
.tpl-grid-sm {
  grid-template-columns: repeat(4, 1fr);
  gap: 8px;
}
@media (max-width: 600px) {
  .tpl-grid { grid-template-columns: repeat(2, 1fr); }
  .tpl-grid-sm { grid-template-columns: repeat(3, 1fr); }
}

.tpl-card {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 10px;
  border: 2px solid var(--line-soft);
  border-radius: 14px;
  padding: 18px 12px;
  cursor: pointer;
  transition: border-color 0.15s, background 0.15s, transform 0.15s;
  background: var(--surface);
  font-family: var(--font-ui);
}
.tpl-card:hover { border-color: var(--line); transform: translateY(-2px); }
.tpl-card-sel {
  border-color: var(--accent);
  background: rgba(255,106,61,.07);
  box-shadow: 0 0 0 2px rgba(255,106,61,.25);
}
.tpl-ico {
  width: 46px;
  height: 46px;
  border-radius: 12px;
  background: var(--surface-2);
  border: 1px solid var(--line-soft);
  display: grid;
  place-items: center;
  font-size: 20px;
  color: var(--accent);
}

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

/* ── Spinner / pulse animations ──────────────────────────────── */
@keyframes spin { to { transform: rotate(360deg); } }
@keyframes pulse {
  0%, 100% { opacity: 1; }
  50% { opacity: .4; }
}
</style>

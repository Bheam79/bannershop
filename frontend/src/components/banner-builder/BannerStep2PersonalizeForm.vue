<script setup lang="ts">
import { ref, computed } from 'vue'
import type { BannerTemplateItem } from '@/api/designRequests'
import type { AspectRatioOption } from '@/composables/banner-builder/useManualMode'

const props = defineProps<{
  selectedTemplate: BannerTemplateItem | null
  templates: BannerTemplateItem[]
  selectedTemplateId: number | null
  language: 'nb' | 'en'
  personName: string
  textContent: string
  themeDescription: string
  personAge: number | null
  selectedAspectRatio: AspectRatioOption
  ratioOptions: ReadonlyArray<{ value: AspectRatioOption; label: string; sub: string; iconW: number; iconH: number }>
  categoryIconClass: Record<string, string>
  textContentPlaceholder: string
  themeDescriptionPlaceholder: string
  isManual: boolean
  photoPreviewUrl: string | null
  photoUploading: boolean
  photoDragging: boolean
  photoUploadProgress: number
  photoUploadError: string | null
}>()

const emit = defineEmits<{
  'update:personName': [value: string]
  'update:textContent': [value: string]
  'update:themeDescription': [value: string]
  'update:personAge': [value: number | null]
  'update:selectedTemplateId': [value: number | null]
  'update:selectedAspectRatio': [value: AspectRatioOption]
  'changeTemplate': []
  'openPhotoPicker': []
  'onPhotoFileChange': [e: Event]
  'onPhotoDragOver': [e: DragEvent]
  'onPhotoDragLeave': []
  'onPhotoDrop': [e: DragEvent]
  'removePhoto': []
}>()

const templateName = computed(() => {
  const t = props.selectedTemplate
  if (!t) return ''
  return props.language === 'en' ? t.nameEn : t.nameNb
})

/** Local ref for the hidden file input — used only in this component's upload zone. */
const photoFileInput = ref<HTMLInputElement | null>(null)

/** Trigger the hidden file input (delegates upload state to parent via emits). */
function openPhotoPicker() {
  if (props.photoUploading) return
  photoFileInput.value?.click()
  emit('openPhotoPicker')
}
</script>

<template>
  <!-- BANNERSH-105: selected template summary so the user can see which
       celebration they're customising for (especially when arriving from a
       front-page category card that skipped step 1). -->
  <div v-if="selectedTemplate" class="selected-template-card">
    <div class="selected-template-ico">
      <i :class="['fa-solid', categoryIconClass[selectedTemplate.category] ?? 'fa-star']"></i>
    </div>
    <div style="flex:1;min-width:0">
      <div class="selected-template-eyebrow">Du tilpasser</div>
      <div class="selected-template-name">{{ templateName }}</div>
    </div>
    <button
      type="button"
      class="selected-template-change"
      @click="emit('changeTemplate')"
      aria-label="Bytt feiringsmal"
    >
      <i class="fa-solid fa-pen-to-square" style="font-size:11px"></i>
      Bytt mal
    </button>
  </div>

  <div class="bb-panel" style="display:grid;gap:20px">
    <div>
      <label for="personName" class="field-label">Navn <span style="color:var(--accent)">*</span></label>
      <input
        id="personName"
        type="text"
        maxlength="200"
        class="dark-input"
        placeholder="f.eks. Ole Petter"
        :value="personName"
        @input="emit('update:personName', ($event.target as HTMLInputElement).value)"
      />
    </div>
    <div v-if="selectedTemplate?.category === 'Birthday'">
      <label for="personAge" class="field-label">Alder <span style="color:var(--faint);font-weight:400">(valgfritt)</span></label>
      <input
        id="personAge"
        type="number"
        min="0"
        max="130"
        class="dark-input"
        style="width:120px"
        placeholder="f.eks. 50"
        :value="personAge ?? ''"
        @input="emit('update:personAge', ($event.target as HTMLInputElement).value !== '' ? Number(($event.target as HTMLInputElement).value) : null)"
      />
    </div>
    <div>
      <label for="textContent" class="field-label">Tekst på banneret <span style="color:var(--accent)">*</span></label>
      <textarea
        id="textContent"
        rows="3"
        maxlength="500"
        class="dark-input"
        style="resize:none"
        :placeholder="textContentPlaceholder"
        :value="textContent"
        @input="emit('update:textContent', ($event.target as HTMLTextAreaElement).value)"
      />
      <p style="margin-top:5px;font-size:13px;color:var(--faint)">{{ textContent.length }} / 500 tegn</p>
    </div>
    <div>
      <label for="themeDescription" class="field-label">Tema / stil <span style="color:var(--accent)">*</span></label>
      <input
        id="themeDescription"
        type="text"
        maxlength="500"
        class="dark-input"
        :placeholder="themeDescriptionPlaceholder"
        :value="themeDescription"
        @input="emit('update:themeDescription', ($event.target as HTMLInputElement).value)"
      />
    </div>

    <!-- Portrait photo upload (moved here from step 1) -->
    <!-- BANNERSH-189: in manual mode the photo is REQUIRED (designer needs the
         reference); in AI mode it's optional (only the person-centred templates
         use it, and the AI degrades gracefully without it). -->
    <div>
      <div class="field-label" style="margin-bottom:4px">
        Portrettfoto
        <span v-if="isManual" style="color:var(--accent)">*</span>
        <span v-else style="font-size:13px;font-weight:400;color:var(--faint)">(valgfritt)</span>
      </div>
      <p style="font-size:13px;color:var(--muted);margin-bottom:12px">
        <template v-if="isManual">
          Vi trenger et bilde av personen som feires — designeren bruker det som referanse.
        </template>
        <template v-else>
          Last opp et bilde av personen som feires — AI-en vil inkorporere det i banneret.
        </template>
      </p>

      <div v-if="photoPreviewUrl" style="display:flex;align-items:flex-start;gap:18px">
        <img :src="photoPreviewUrl" alt="Opplastet portrettfoto" style="width:100px;height:100px;object-fit:cover;border-radius:12px;border:1px solid var(--line-soft)" />
        <div style="display:flex;flex-direction:column;gap:10px;margin-top:6px">
          <span style="font-size:14px;color:#4ade80;font-weight:600;display:flex;align-items:center;gap:7px">
            <i class="fa-solid fa-circle-check"></i> Foto lastet opp
          </span>
          <button type="button" style="font-size:13.5px;color:var(--accent);font-weight:600;background:none;border:none;cursor:pointer;padding:0;text-align:left" @click="emit('removePhoto')">
            <i class="fa-solid fa-trash-can" style="font-size:12px"></i> Fjern foto
          </button>
        </div>
      </div>

      <div v-else>
        <div
          role="button"
          tabindex="0"
          class="upload-zone"
          :class="{ 'upload-zone-drag': photoDragging, 'upload-zone-busy': photoUploading }"
          @click="openPhotoPicker"
          @keydown.enter.prevent="openPhotoPicker"
          @keydown.space.prevent="openPhotoPicker"
          @dragover="emit('onPhotoDragOver', $event)"
          @dragleave="emit('onPhotoDragLeave')"
          @drop="emit('onPhotoDrop', $event)"
        >
          <input ref="photoFileInput" type="file" style="display:none" accept="image/jpeg,image/png,image/webp" @change="emit('onPhotoFileChange', $event)" />
          <i class="fa-solid fa-user-circle" style="font-size:36px;color:var(--faint);margin-bottom:10px"></i>
          <p style="font-size:14px;font-weight:600;color:var(--text);margin-bottom:4px">Slipp bilde her, eller klikk for å velge</p>
          <p style="font-size:13px;color:var(--faint)">JPEG, PNG, WEBP – maks 10 MB</p>
          <div v-if="photoUploading" class="upload-overlay">
            <div style="width:66%;max-width:260px">
              <div style="font-size:14px;font-weight:600;color:var(--text);text-align:center;margin-bottom:10px">Laster opp… {{ photoUploadProgress }}%</div>
              <div style="width:100%;height:6px;background:var(--line);border-radius:999px;overflow:hidden">
                <div style="height:100%;background:var(--accent);border-radius:999px;transition:width .2s" :style="{ width: `${photoUploadProgress}%` }" />
              </div>
            </div>
          </div>
        </div>
        <div v-if="photoUploadError" class="error-box" style="margin-top:10px">
          <i class="fa-solid fa-circle-exclamation"></i> {{ photoUploadError }}
        </div>
      </div>
    </div>

    <!-- BANNERSH-170: Aspect-ratio selection — one row of buttons below image upload -->
    <div>
      <div class="field-label" style="margin-bottom:10px">
        Bildeforhold
        <span style="font-size:11px;font-weight:400;color:var(--faint);text-transform:none;letter-spacing:0;margin-left:4px">— velg formen på banneret</span>
      </div>
      <div class="ratio-row">
        <button
          v-for="opt in ratioOptions"
          :key="opt.value"
          type="button"
          class="ratio-btn"
          :class="{ 'ratio-btn-active': selectedAspectRatio === opt.value }"
          @click="emit('update:selectedAspectRatio', opt.value)"
        >
          <!-- small rectangle icon visualising the aspect ratio -->
          <div class="ratio-icon-wrap">
            <div class="ratio-icon" :style="{ width: opt.iconW + 'px', height: opt.iconH + 'px' }" />
          </div>
          <span class="ratio-label">{{ opt.label }}</span>
          <span class="ratio-sub">{{ opt.sub }}</span>
        </button>
      </div>
    </div>

    <!-- BANNERSH-162: quality / size selection is rendered BELOW the
         generated banner preview (further down in this template) instead of
         here, and is hidden until a banner has been generated. -->
  </div>
</template>

<style scoped>
/* ── Selected-template summary (BANNERSH-105) ────────────────── */
.selected-template-card {
  display: flex;
  align-items: center;
  gap: 14px;
  background: var(--surface);
  border: 1px solid var(--line-soft);
  border-radius: var(--radius);
  padding: 14px 18px;
  margin-bottom: 18px;
}
.selected-template-ico {
  width: 44px;
  height: 44px;
  border-radius: 12px;
  background: rgba(255,106,61,.12);
  border: 1px solid rgba(255,106,61,.28);
  display: grid;
  place-items: center;
  font-size: 19px;
  color: var(--accent);
  flex-shrink: 0;
}
.selected-template-eyebrow {
  font-size: 13px;
  font-weight: 700;
  color: var(--faint);
  text-transform: uppercase;
  letter-spacing: .06em;
  margin-bottom: 2px;
}
.selected-template-name {
  font-family: var(--font-display);
  font-weight: 700;
  font-size: 18px;
  color: var(--text);
  line-height: 1.2;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
.selected-template-change {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  background: transparent;
  border: 1px solid var(--line);
  border-radius: 10px;
  padding: 7px 12px;
  font-size: 13px;
  font-weight: 600;
  color: var(--muted);
  cursor: pointer;
  font-family: var(--font-ui);
  transition: border-color .15s, color .15s, background .15s;
  flex-shrink: 0;
}
.selected-template-change:hover {
  border-color: var(--accent);
  color: var(--accent-2);
  background: rgba(255,106,61,.06);
}

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

/* ── Panel ───────────────────────────────────────────────────── */
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

/* ── BANNERSH-170: Aspect-ratio selector row ─────────────────── */
.ratio-row {
  display: flex;
  gap: 8px;
  flex-wrap: nowrap;
  overflow-x: auto;
  padding-bottom: 2px;
  /* hide scrollbar on desktop while keeping it functional */
  scrollbar-width: thin;
  scrollbar-color: var(--line) transparent;
}
.ratio-btn {
  flex-shrink: 0;
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 6px;
  border: 2px solid var(--line);
  border-radius: 12px;
  padding: 14px 18px 12px;
  background: var(--surface-2);
  cursor: pointer;
  font-family: var(--font-ui);
  transition: border-color 0.15s, background 0.15s, box-shadow 0.15s;
  min-width: 78px;
}
.ratio-btn:hover {
  border-color: var(--line-soft);
  background: var(--surface);
}
.ratio-btn-active {
  border-color: var(--accent);
  background: rgba(255, 106, 61, 0.08);
  box-shadow: 0 0 0 2px rgba(255, 106, 61, 0.2);
}
.ratio-icon-wrap {
  width: 34px;
  height: 28px;
  display: flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
}
.ratio-icon {
  border: 2px solid currentColor;
  border-radius: 3px;
  opacity: 0.7;
  transition: opacity 0.15s;
}
.ratio-btn-active .ratio-icon { opacity: 1; }
.ratio-label {
  font-size: 13px;
  font-weight: 700;
  color: var(--text);
  line-height: 1;
  white-space: nowrap;
}
.ratio-sub {
  font-size: 13px;
  color: var(--faint);
  line-height: 1;
  white-space: nowrap;
}
.ratio-btn-active .ratio-label { color: var(--accent-2); }
.ratio-btn-active .ratio-sub   { color: var(--accent);   }
</style>

<script setup lang="ts">
import type { BannerTemplateItem } from '@/api/designRequests'

const props = defineProps<{
  templates: BannerTemplateItem[]
  templatesLoading: boolean
  templatesError: string | null
  selectedTemplateId: number | null
  language: 'nb' | 'en'
  step1Valid: boolean
  categoryIconClass: Record<string, string>
}>()

const emit = defineEmits<{
  'update:selectedTemplateId': [value: number | null]
  'update:language': [value: 'nb' | 'en']
  next: []
  retry: []
}>()
</script>

<template>
  <!-- Loading -->
  <div v-if="templatesLoading" style="text-align:center;color:var(--muted);padding:3rem 0">
    <i class="fa-solid fa-circle-notch fa-spin" style="font-size:24px;margin-bottom:12px;display:block;color:var(--accent)"></i>
    Laster maler…
  </div>

  <!-- Error -->
  <div
    v-else-if="templatesError"
    class="error-box"
    style="justify-content:center;flex-direction:column;text-align:center;padding:2rem"
  >
    <i class="fa-solid fa-circle-exclamation" style="font-size:24px;margin-bottom:8px"></i>
    {{ templatesError }}
    <button
      style="margin-top:12px;color:var(--accent);background:none;border:none;cursor:pointer;font-weight:600;font-size:14px"
      @click="emit('retry')"
    >Prøv igjen</button>
  </div>

  <!-- Content -->
  <template v-else>
    <!-- Language toggle -->
    <div style="margin-bottom:24px;display:flex;align-items:center;gap:12px">
      <span style="font-size:14px;font-weight:600;color:var(--muted)">Språk:</span>
      <button
        type="button"
        class="lang-btn"
        :class="{ 'lang-btn-active': language === 'nb' }"
        @click="emit('update:language', 'nb')"
      >
        🇳🇴 Norsk
      </button>
      <button
        type="button"
        class="lang-btn"
        :class="{ 'lang-btn-active': language === 'en' }"
        @click="emit('update:language', 'en')"
      >
        🇬🇧 English
      </button>
    </div>

    <!-- Template grid -->
    <div style="margin-bottom:2rem">
      <h2 class="display" style="font-size:20px;color:var(--text);margin-bottom:16px">Velg feiringsmal</h2>
      <div class="tpl-grid">
        <button
          v-for="t in templates"
          :key="t.id"
          type="button"
          class="tpl-card"
          :class="{ 'tpl-card-sel': selectedTemplateId === t.id }"
          @click="emit('update:selectedTemplateId', t.id)"
        >
          <span class="tpl-ico">
            <i :class="['fa-solid', categoryIconClass[t.category] ?? 'fa-star']"></i>
          </span>
          <span style="font-size:13.5px;font-weight:600;color:var(--text);text-align:center;line-height:1.3">
            {{ language === 'en' ? t.nameEn : t.nameNb }}
          </span>
        </button>
      </div>
    </div>

    <!-- Next button -->
    <div style="display:flex;justify-content:flex-end">
      <button
        type="button"
        class="btn btn-primary"
        style="padding:12px 28px;font-size:15px"
        :disabled="!step1Valid"
        @click="emit('next')"
      >
        Neste: Tilpass <i class="fa-solid fa-arrow-right" style="font-size:12px"></i>
      </button>
    </div>
  </template>
</template>

<style scoped>
/* ── Language toggle ─────────────────────────────────────────── */
.lang-btn {
  border: 2px solid var(--line);
  border-radius: 10px;
  padding: 7px 16px;
  font-size: 14px;
  font-weight: 600;
  cursor: pointer;
  transition: border-color 0.15s, background 0.15s;
  background: transparent;
  color: var(--muted);
  font-family: var(--font-ui);
}
.lang-btn:hover { border-color: var(--line); color: var(--text); }
.lang-btn-active { border-color: var(--accent); color: var(--accent-2); background: rgba(255,106,61,.08); }

/* ── Template grid ───────────────────────────────────────────── */
.tpl-grid {
  display: grid;
  grid-template-columns: repeat(4, 1fr);
  gap: 12px;
}
@media (max-width: 600px) {
  .tpl-grid { grid-template-columns: repeat(2, 1fr); }
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

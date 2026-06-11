<script setup lang="ts">
/**
 * BANNERSH-222: shared layout for static info / legal pages
 * (Frakt & levering, Materialer, Kontakt oss, Brukervilkår, Personvern).
 *
 * Renders a centred wrap with a title + optional intro. The page body
 * goes into the default slot — wrap content blocks in <section> /
 * <article> / plain divs and use the helper classes defined at the
 * bottom of this file for consistent typography.
 */
interface Props {
  title: string
  intro?: string
  icon?: string
}
defineProps<Props>()
</script>

<template>
  <div class="info-page">
    <div class="wrap">
      <header class="info-header">
        <h1 class="display info-title">
          <i v-if="icon" :class="['fa-solid', icon]" aria-hidden="true"></i>
          {{ title }}
        </h1>
        <p v-if="intro" class="info-intro">{{ intro }}</p>
      </header>

      <div class="info-body">
        <slot />
      </div>
    </div>
  </div>
</template>

<style scoped>
.info-page {
  background: var(--bg);
  color: var(--text);
  font-family: var(--font-ui);
  min-height: 100%;
  padding: 48px 0 64px;
}
.wrap {
  max-width: 820px;
  margin: 0 auto;
  padding: 0 28px;
}

/* ── Header ──────────────────────────────────────────────────── */
.info-header {
  margin-bottom: 32px;
  padding-bottom: 22px;
  border-bottom: 1px solid var(--line-soft);
}
.info-title {
  font-size: clamp(28px, 4vw, 40px);
  margin-bottom: 10px;
  display: flex;
  align-items: center;
  gap: 14px;
}
.info-title i {
  color: var(--accent);
  font-size: 0.8em;
}
.info-intro {
  color: var(--muted);
  font-size: 17px;
  max-width: 60ch;
  line-height: 1.5;
}

/* ── Body typography (applied via :slotted) ──────────────────── */
.info-body :slotted(h2) {
  font-family: var(--font-display);
  font-weight: 700;
  font-size: 22px;
  letter-spacing: -.01em;
  margin: 32px 0 12px;
  color: var(--text);
}
.info-body :slotted(h3) {
  font-family: var(--font-display);
  font-weight: 600;
  font-size: 17px;
  margin: 24px 0 8px;
  color: var(--text);
}
.info-body :slotted(p) {
  color: var(--muted);
  font-size: 15.5px;
  line-height: 1.65;
  margin-bottom: 14px;
}
.info-body :slotted(ul),
.info-body :slotted(ol) {
  color: var(--muted);
  font-size: 15.5px;
  line-height: 1.65;
  margin: 0 0 16px 22px;
  padding: 0;
}
.info-body :slotted(li) {
  margin-bottom: 6px;
}
.info-body :slotted(a) {
  color: var(--accent);
  text-decoration: none;
  transition: color .15s;
}
.info-body :slotted(a:hover) {
  color: var(--accent-2);
  text-decoration: underline;
}
.info-body :slotted(strong) {
  color: var(--text);
  font-weight: 600;
}
.info-body :slotted(.info-callout) {
  background: var(--surface);
  border: 1px solid var(--line-soft);
  border-radius: 14px;
  padding: 18px 22px;
  margin: 20px 0;
}
.info-body :slotted(.info-callout p:last-child) {
  margin-bottom: 0;
}

@media (max-width: 480px) {
  .wrap { padding: 0 16px; }
  .info-page { padding: 32px 0 48px; }
}
</style>
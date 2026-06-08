<script setup lang="ts">
import { RouterLink } from 'vue-router'
</script>

<template>
  <div style="max-width:1000px;margin:0 auto;padding:2.5rem 1.5rem 4rem">
    <!-- Hero -->
    <header style="text-align:center;margin-bottom:3rem">
      <h1 class="display" style="font-size:clamp(30px,4vw,46px);color:var(--text);margin-bottom:12px">
        Lag ditt eget banner
      </h1>
      <p style="font-size:18px;color:var(--muted);max-width:36em;margin:0 auto">
        Last opp din fil, la AI lage det, eller bestill et profesjonelt design fra oss.
      </p>
    </header>

    <!-- Option cards -->
    <div class="hub-grid">
      <!-- 1. Upload own file -->
      <div class="hub-card">
        <div class="hub-ico">
          <i class="fa-solid fa-folder-open"></i>
        </div>
        <h2 class="display" style="font-size:21px;color:var(--text);margin-bottom:10px">
          Eget bilde eller PDF
        </h2>
        <p style="font-size:14.5px;color:var(--muted);flex:1;margin-bottom:16px">
          Last opp bildet eller PDF-en din, velg høyde (150 eller 180 cm) — bredden
          beregnes automatisk. Se forhåndsvisning og legg i handlekurven.
        </p>
        <div style="font-size:13px;color:var(--faint);margin-bottom:18px">
          Samme pris som standard banner
        </div>
        <RouterLink to="/banner-builder/upload" class="btn btn-soft" style="justify-content:center;width:100%">
          Start →
        </RouterLink>
      </div>

      <!-- 2. AI banner -->
      <div class="hub-card hub-feat">
        <span class="hub-tag">
          <i class="fa-solid fa-bolt"></i> Ferdig på minutter
        </span>
        <div class="hub-ico hub-ico-accent">
          <i class="fa-solid fa-wand-magic-sparkles"></i>
        </div>
        <h2 class="display" style="font-size:21px;color:var(--text);margin-bottom:10px">
          AI-designet banner
        </h2>
        <p style="font-size:14.5px;color:var(--muted);flex:1;margin-bottom:16px">
          Velg mal (bursdag, bryllup, konfirmasjon m.m.), skriv inn navn og ønsket tekst
          — AI genererer et unikt banner for deg på minutter.
        </p>
        <div style="font-size:14px;color:var(--faint);margin-bottom:18px">
          <b style="color:#4ade80;font-family:var(--font-display);font-size:22px;font-weight:700">Gratis</b>
          <span style="font-size:13px;margin-left:4px">for første generering</span>
        </div>
        <RouterLink to="/banner-builder/ai" class="btn btn-primary" style="justify-content:center;width:100%">
          Lag AI-banner →
        </RouterLink>
      </div>

      <!-- 3. Manual / professional design -->
      <div class="hub-card">
        <span class="hub-tag hub-tag-gold">
          <i class="fa-solid fa-clock"></i> Innen 2–3 virkedager
        </span>
        <div class="hub-ico">
          <i class="fa-solid fa-palette"></i>
        </div>
        <h2 class="display" style="font-size:21px;color:var(--text);margin-bottom:10px">
          Vi designer for deg
        </h2>
        <p style="font-size:14.5px;color:var(--muted);flex:1;margin-bottom:16px">
          Velg mal og fyll inn ønskene dine — teamet vårt designer banneret og sender deg
          en forhåndsvisning til godkjenning. Én gratis korrigering inkludert.
        </p>
        <div style="font-size:14px;color:var(--faint);margin-bottom:18px">
          <b style="color:var(--text);font-family:var(--font-display);font-size:22px;font-weight:700">495 kr</b>
          <span style="font-size:13px;margin-left:4px">for designtjeneste</span>
        </div>
        <RouterLink to="/banner-builder/manual" class="btn btn-soft" style="justify-content:center;width:100%">
          Bestill design →
        </RouterLink>
      </div>
    </div>
  </div>
</template>

<style scoped>
.hub-grid {
  display: grid;
  grid-template-columns: repeat(3, 1fr);
  gap: 20px;
}
@media (max-width: 768px) {
  .hub-grid { grid-template-columns: 1fr; }
}

.hub-card {
  position: relative;
  background: var(--surface);
  border: 1px solid var(--line-soft);
  border-radius: var(--radius);
  padding: 28px;
  display: flex;
  flex-direction: column;
  transition: border-color 0.18s, transform 0.18s;
}
.hub-card:hover {
  border-color: var(--line);
  transform: translateY(-3px);
}
.hub-feat {
  border-color: rgba(255,106,61,.4);
  background: linear-gradient(170deg, rgba(255,106,61,.07), var(--surface));
}

.hub-ico {
  width: 52px;
  height: 52px;
  border-radius: 14px;
  background: var(--surface-2);
  border: 1px solid var(--line-soft);
  display: grid;
  place-items: center;
  font-size: 22px;
  color: var(--muted);
  margin-bottom: 18px;
}
.hub-ico-accent {
  background: rgba(255,106,61,.14);
  border-color: rgba(255,106,61,.3);
  color: var(--accent);
}

.hub-tag {
  position: absolute;
  top: 18px;
  right: 18px;
  font-size: 11.5px;
  font-weight: 600;
  color: var(--accent-2);
  background: rgba(255,106,61,.12);
  border: 1px solid rgba(255,106,61,.25);
  padding: 4px 10px;
  border-radius: 999px;
  display: flex;
  align-items: center;
  gap: 5px;
}
.hub-tag-gold {
  color: var(--gold);
  background: rgba(231,185,78,.12);
  border-color: rgba(231,185,78,.28);
}
</style>

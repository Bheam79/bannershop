<script setup lang="ts">
import { RouterLink } from 'vue-router'
</script>

<template>
  <div class="max-w-5xl mx-auto px-4 py-10 sm:py-14">
    <!-- Hero -->
    <header class="text-center mb-12">
      <h1 class="text-3xl sm:text-4xl font-bold text-gray-900 mb-3">
        Lag ditt eget banner
      </h1>
      <p class="text-lg text-gray-600 max-w-2xl mx-auto">
        Upload din fil, la AI lage det, eller bestill et profesjonelt design fra oss.
      </p>
    </header>

    <!-- Option cards -->
    <div class="grid gap-6 sm:grid-cols-3">
      <!-- 1. Upload own file -->
      <div class="bg-white border border-gray-200 rounded-2xl p-6 flex flex-col shadow-sm hover:shadow-md transition">
        <div class="text-4xl mb-4">📁</div>
        <h2 class="text-xl font-bold text-gray-900 mb-2">Eget bilde eller PDF</h2>
        <p class="text-sm text-gray-600 flex-1 mb-4">
          Last opp bildet eller PDF-en din, velg høyde (150 eller 180 cm) — bredden
          beregnes automatisk. Se forhåndsvisning og legg i handlekurven.
        </p>
        <div class="text-sm font-semibold text-gray-700 mb-4">
          Samme pris som standard banner
        </div>
        <RouterLink
          to="/banner-builder/upload"
          class="mt-auto block text-center bg-blue-700 hover:bg-blue-800 text-white font-semibold px-5 py-2.5 rounded-lg text-sm transition"
        >
          Start →
        </RouterLink>
      </div>

      <!-- 2. AI banner -->
      <div class="bg-white border border-gray-200 rounded-2xl p-6 flex flex-col shadow-sm hover:shadow-md transition relative">
        <span class="absolute top-4 right-4 bg-purple-100 text-purple-800 text-xs font-bold px-2.5 py-1 rounded-full">
          Ferdig på minutter
        </span>
        <div class="text-4xl mb-4">✨</div>
        <h2 class="text-xl font-bold text-gray-900 mb-2">AI-designet banner</h2>
        <p class="text-sm text-gray-600 flex-1 mb-4">
          Velg mal (bursdag, bryllup, konfirmasjon m.m.), skriv inn navn og ønsket tekst
          — AI genererer et unikt banner for deg på minutter.
        </p>
        <div class="text-sm font-semibold text-gray-700 mb-4">
          95 kr
        </div>
        <RouterLink
          to="/banner-builder/ai"
          class="mt-auto block text-center bg-purple-700 hover:bg-purple-800 text-white font-semibold px-5 py-2.5 rounded-lg text-sm transition"
        >
          Lag AI-banner →
        </RouterLink>
      </div>

      <!-- 3. Manual / professional design -->
      <div class="bg-white border border-gray-200 rounded-2xl p-6 flex flex-col shadow-sm hover:shadow-md transition relative">
        <span class="absolute top-4 right-4 bg-amber-100 text-amber-900 text-xs font-bold px-2.5 py-1 rounded-full">
          Innen 2–3 virkedager
        </span>
        <div class="text-4xl mb-4">🎨</div>
        <h2 class="text-xl font-bold text-gray-900 mb-2">Vi designer for deg</h2>
        <p class="text-sm text-gray-600 flex-1 mb-4">
          Velg mal og fyll inn ønskene dine — teamet vårt designer banneret og sender deg
          en forhåndsvisning til godkjenning. Én gratis korrigering inkludert.
        </p>
        <div class="text-sm font-semibold text-gray-700 mb-4">
          495 kr
        </div>
        <RouterLink
          to="/banner-builder/manual"
          class="mt-auto block text-center bg-amber-600 hover:bg-amber-700 text-white font-semibold px-5 py-2.5 rounded-lg text-sm transition"
        >
          Bestill design →
        </RouterLink>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { countIntermediatesOnSegment } from '@/types'
import type { EyeletOption } from '@/types'

/**
 * Renders a scaled SVG preview of a banner with eyelet positions drawn on it.
 *
 * The SVG viewBox is set to the actual banner dimensions in cm so that all
 * coordinates are 1:1 with real-world measurements.  The eyelet diameter of
 * 22 mm (1.1 cm radius) and the 2.5 cm edge inset are therefore both exact.
 *
 * BANNERSH-173
 */
const props = defineProps<{
  widthCm: number
  heightCm: number
  eyeletOption: EyeletOption
  /** Optional uploaded-design preview URL to use as the banner background. */
  imageUrl?: string
}>()

/** Distance from banner edge to eyelet centre (standard hem depth). */
const EDGE_INSET = 2.5
/** Physical eyelet radius in cm (22 mm diameter). */
const EYELET_R = 1.1
/** Visual punch-through hole radius. */
const HOLE_R = 0.5

interface Point { x: number; y: number }

const eyeletPositions = computed<Point[]>(() => {
  const { widthCm: W, heightCm: H, eyeletOption: opt } = props
  if (opt === 'None' || W <= 0 || H <= 0) return []

  const e = EDGE_INSET
  const pts: Point[] = [
    { x: e,     y: e },
    { x: W - e, y: e },
    { x: e,     y: H - e },
    { x: W - e, y: H - e },
  ]

  if (opt === 'FourCorners') return pts

  // PerMeter: corners + intermediates evenly spaced along each edge
  const addH = (n: number, fixedY: number) => {
    if (n <= 0) return
    const step = (W - 2 * e) / (n + 1)
    for (let i = 1; i <= n; i++) pts.push({ x: e + i * step, y: fixedY })
  }
  const addV = (n: number, fixedX: number) => {
    if (n <= 0) return
    const step = (H - 2 * e) / (n + 1)
    for (let i = 1; i <= n; i++) pts.push({ x: fixedX, y: e + i * step })
  }

  const nH = countIntermediatesOnSegment(W)
  const nV = countIntermediatesOnSegment(H)
  addH(nH, e)       // top edge
  addH(nH, H - e)   // bottom edge
  addV(nV, e)        // left edge
  addV(nV, W - e)    // right edge

  return pts
})

const viewBox = computed(() => `0 0 ${props.widthCm} ${props.heightCm}`)
</script>

<template>
  <div class="eyelet-preview-wrap">
    <svg
      xmlns="http://www.w3.org/2000/svg"
      :viewBox="viewBox"
      style="width:100%;height:auto;display:block"
      role="img"
      :aria-label="`Forhåndsvisning av ${widthCm}×${heightCm} cm banner med ${eyeletOption === 'None' ? 'ingen' : eyeletOption === 'FourCorners' ? '4 hjørne-' : 'per-meter '}maljer`"
    >
      <defs>
        <!-- Metallic eyelet gradient: top-left highlight → bottom-right shadow -->
        <radialGradient id="ep-eyelet" cx="35%" cy="32%" r="68%">
          <stop offset="0%"   stop-color="#e8e8e8"/>
          <stop offset="45%"  stop-color="#b4b4b4"/>
          <stop offset="100%" stop-color="#6a6a6a"/>
        </radialGradient>
        <!-- Soft drop-shadow filter for eyelets -->
        <filter id="ep-shadow" x="-30%" y="-30%" width="160%" height="160%">
          <feDropShadow dx="0" dy="0.3" stdDeviation="0.5" flood-color="rgba(0,0,0,0.5)"/>
        </filter>
        <!-- Banner image clip -->
        <clipPath id="ep-banner-clip">
          <rect x="0" y="0" :width="widthCm" :height="heightCm"/>
        </clipPath>
      </defs>

      <!-- ── Banner body ─────────────────────────────────────────────────── -->
      <rect
        x="0" y="0"
        :width="widthCm"
        :height="heightCm"
        rx="1"
        fill="#1a2e45"
      />

      <!-- Uploaded preview image (if provided) -->
      <image
        v-if="imageUrl"
        :href="imageUrl"
        x="0" y="0"
        :width="widthCm"
        :height="heightCm"
        preserveAspectRatio="xMidYMid slice"
        clip-path="url(#ep-banner-clip)"
        opacity="0.88"
      />

      <!-- Hem / sew-line guide (dashed rectangle inside the banner) -->
      <rect
        :x="EDGE_INSET"
        :y="EDGE_INSET"
        :width="widthCm - 2 * EDGE_INSET"
        :height="heightCm - 2 * EDGE_INSET"
        fill="none"
        stroke="rgba(255,255,255,0.18)"
        stroke-width="0.35"
        stroke-dasharray="2.5 1.8"
        rx="0.5"
      />

      <!-- ── Eyelets ─────────────────────────────────────────────────────── -->
      <g v-if="eyeletOption !== 'None'">
        <g
          v-for="(pt, idx) in eyeletPositions"
          :key="idx"
          :transform="`translate(${pt.x}, ${pt.y})`"
          filter="url(#ep-shadow)"
        >
          <!-- Metallic ring body -->
          <circle :r="EYELET_R" fill="url(#ep-eyelet)" stroke="#888" stroke-width="0.12"/>
          <!-- Inner highlight ring -->
          <circle :r="EYELET_R * 0.72" fill="none" stroke="rgba(255,255,255,0.25)" stroke-width="0.12"/>
          <!-- Punch-through hole -->
          <circle :r="HOLE_R" fill="#111" opacity="0.92"/>
        </g>
      </g>

      <!-- "Ingen maljer" placeholder hint (very subtle) -->
      <text
        v-if="eyeletOption === 'None' && widthCm > 0 && heightCm > 0"
        :x="widthCm / 2"
        :y="heightCm / 2"
        text-anchor="middle"
        dominant-baseline="middle"
        fill="rgba(255,255,255,0.22)"
        :font-size="Math.min(widthCm, heightCm) * 0.08"
        font-family="sans-serif"
        letter-spacing="0.5"
      >ingen maljer</text>
    </svg>
  </div>
</template>

<style scoped>
.eyelet-preview-wrap {
  border-radius: 8px;
  overflow: hidden;
  background: transparent;
  line-height: 0; /* remove inline-block gap under SVG */
}
</style>

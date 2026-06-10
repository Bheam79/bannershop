<script setup lang="ts">
import type { DesignRequestListItem } from '@/api/designRequests'

const props = defineProps<{
  designs: DesignRequestListItem[]
  activeId: number | null
  isManual: boolean
}>()

const emit = defineEmits<{
  select: [item: DesignRequestListItem]
}>()
</script>

<template>
  <aside
    class="past-sidebar"
    :aria-label="props.isManual ? 'Tidligere manuelle banner' : 'Tidligere genererte banner'"
  >
    <div class="past-sidebar-hd">
      <span class="display past-title">
        <i class="fa-solid fa-clock-rotate-left"></i>
        Tidligere
      </span>
      <span class="past-count">{{ props.designs.length }}</span>
    </div>
    <p class="past-sub">
      <template v-if="props.isManual">Tidligere manuelle bestillinger — klikk for detaljer.</template>
      <template v-else>Klikk for å åpne — godkjenn eller bruk som utgangspunkt.</template>
    </p>
    <div class="past-sidebar-list">
      <button
        v-for="d in props.designs"
        :key="d.id"
        type="button"
        class="past-card"
        :class="{ 'past-card-active': props.activeId === d.id }"
        @click="emit('select', d)"
      >
        <div class="past-thumb">
          <img v-if="d.previewUrl" :src="d.previewUrl" :alt="`Tidligere banner for ${d.personName}`" />
          <i v-else class="fa-solid fa-palette" style="font-size:24px;color:var(--faint)"></i>
        </div>
        <div class="past-meta">
          <div class="past-name">{{ d.personName || 'Uten navn' }}</div>
          <div class="past-theme">{{ d.themeDescription || '—' }}</div>
          <div class="past-status">
            <i v-if="d.status === 'Final' || d.status === 'Approved'" class="fa-solid fa-circle-check" style="color:#4ade80"></i>
            <i v-else-if="d.status === 'AwaitingApproval'" class="fa-solid fa-hourglass-half" style="color:var(--gold)"></i>
            <i v-else class="fa-solid fa-circle-info" style="color:var(--faint)"></i>
            {{ d.status === 'AwaitingApproval' ? 'Venter godkjenning'
              : d.status === 'Final' ? 'Bestilt'
              : d.status === 'Approved' ? 'Godkjent'
              : d.status }}
          </div>
        </div>
      </button>
    </div>
  </aside>
</template>

<style scoped>
.past-sidebar {
  position: sticky;
  top: 20px;
  background: var(--surface);
  border: 1px solid var(--line-soft);
  border-radius: var(--radius);
  padding: 14px 12px 16px;
  max-height: calc(100vh - 48px);
  overflow-y: auto;
}
.past-sidebar-hd {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-bottom: 4px;
}
.past-title {
  font-size: 14px;
  color: var(--text);
  display: flex;
  align-items: center;
  gap: 7px;
  margin: 0;
  font-weight: 700;
  line-height: 1.3;
}
.past-title i { color: var(--accent); font-size: 13px; }
.past-count {
  background: rgba(255,106,61,.12);
  border: 1px solid rgba(255,106,61,.3);
  color: var(--accent);
  border-radius: 99px;
  padding: 2px 9px;
  font-size: 13px;
  font-weight: 700;
  flex-shrink: 0;
}
.past-sub {
  font-size: 13px;
  color: var(--muted);
  margin: 0 0 12px;
  line-height: 1.4;
}
.past-sidebar-list {
  display: flex;
  flex-direction: column;
  gap: 13px;
}
@media (max-width: 820px) {
  .past-sidebar {
    position: static;
    max-height: none;
  }
  .past-sidebar-list {
    flex-direction: row;
    overflow-x: auto;
    padding-bottom: 4px;
    scroll-snap-type: x mandatory;
  }
  .past-sidebar-list .past-card {
    flex: 0 0 160px;
    width: 160px;
  }
}
.past-card {
  width: 100%;
  display: flex;
  flex-direction: column;
  background: var(--surface-2);
  border: 2px solid var(--line-soft);
  border-radius: 0;
  overflow: hidden;
  cursor: pointer;
  transition: border-color 0.15s, transform 0.15s, background 0.15s;
  scroll-snap-align: start;
  text-align: left;
  font-family: var(--font-ui);
  padding: 0;
}
.past-card:hover {
  border-color: var(--accent);
  transform: translateY(-2px);
  background: rgba(255,106,61,.06);
}
.past-card-active {
  border-color: var(--accent);
  background: rgba(255,106,61,.08);
  box-shadow: 0 0 0 2px rgba(255,106,61,.25);
}
.past-thumb {
  width: 100%;
  aspect-ratio: 16 / 9;
  background: var(--surface);
  display: grid;
  place-items: center;
  overflow: hidden;
}
.past-thumb img {
  width: 100%;
  height: 100%;
  object-fit: cover;
  display: block;
}
.past-meta {
  padding: 8px 10px 10px;
  display: flex;
  flex-direction: column;
  gap: 3px;
  background: hsl(35 17% 24%);
}
.past-name {
  font-size: 14px;
  font-weight: 700;
  color: var(--text);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
.past-theme {
  font-size: 13px;
  color: var(--muted);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
.past-status {
  margin-top: 3px;
  font-size: 13px;
  color: var(--faint);
  display: flex;
  align-items: center;
  gap: 5px;
}
</style>

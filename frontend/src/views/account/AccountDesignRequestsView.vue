<script setup lang="ts">
// This view has been superseded by the unified Mine ordrer list at /account/orders.
// We redirect immediately so any old bookmarks keep working.
import { useRouter } from 'vue-router'

const router = useRouter()
router.replace('/account/orders')
</script>

<template>
  <div></div>
</template>

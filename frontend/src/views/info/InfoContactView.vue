<script setup lang="ts">
import InfoPageLayout from './InfoPageLayout.vue'
</script>

<template>
  <InfoPageLayout
    title="Kontakt oss"
    icon="fa-envelope"
    intro="Vi svarer normalt innen samme virkedag. Send oss en melding eller ring — vi hjelper deg gjerne i gang."
  >
    <p>
      <em>Kundeservice-portal og chat kommer snart. I mellomtiden når du oss
      enklest på e-post.</em>
    </p>

    <h2>E-post</h2>
    <p>
      <a href="mailto:post@bannershop.no">post@bannershop.no</a><br>
      Vi svarer normalt innen samme virkedag (mandag–fredag 09–16).
    </p>

    <h2>Spørsmål om en bestilling?</h2>
    <p>
      Oppgi <strong>ordrenummeret ditt</strong> i e-posten — det er det
      raskeste for oss å hjelpe deg. Du finner ordrenummeret i
      bekreftelsesmailen og under
      <a href="/account/orders">Mine ordrer</a> hvis du har konto.
    </p>

    <h2>Selskapsinformasjon</h2>
    <p>
      <strong>Beatgrid AS</strong><br>
      Org.nr. NO 928 177 572 MVA<br>
      Norge
    </p>

    <div class="info-callout">
      <p>
        <strong>Trenger du hjelp med design?</strong>
        Du kan også bestille
        <a href="/banner-builder/manual">profesjonell designertjeneste</a>
        direkte — vi lager utkast og sender til godkjenning.
      </p>
    </div>
  </InfoPageLayout>
</template>

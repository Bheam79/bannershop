<script setup lang="ts">
import InfoPageLayout from './InfoPageLayout.vue'
</script>

<template>
  <InfoPageLayout
    title="Personvern"
    icon="fa-shield-halved"
    intro="Vi tar personvern på alvor. Her finner du en oversikt over hvilke opplysninger vi behandler og hvorfor."
  >
    <p>
      <em>Dette er en plassholder. Endelig personvernerklæring vil bli
      publisert i sin helhet før lansering.</em>
    </p>

    <h2>1. Behandlingsansvarlig</h2>
    <p>
      Beatgrid AS (org.nr. NO 928 177 572 MVA) er behandlingsansvarlig
      for personopplysningene som samles inn via bannershop.no.
    </p>

    <h2>2. Hvilke opplysninger samler vi inn?</h2>
    <ul>
      <li><strong>Kontoopplysninger</strong> — navn, e-post og passord (kryptert).</li>
      <li><strong>Bestillingsdata</strong> — leveringsadresse, telefonnummer og ordrehistorikk.</li>
      <li><strong>Betalingsdata</strong> — håndteres direkte av Stripe; vi lagrer ikke kortdetaljer.</li>
      <li><strong>Bannerdesign og portrettbilder</strong> du laster opp i banner-byggeren.</li>
      <li><strong>Teknisk informasjon</strong> — IP-adresse, nettleser og enkel bruksstatistikk.</li>
    </ul>

    <h2>3. Hvorfor behandler vi opplysningene?</h2>
    <p>
      Opplysningene brukes til å produsere og sende ut bestillinger,
      yte kundeservice, drive nettsiden teknisk, og holde regnskap i
      tråd med norsk lov.
    </p>

    <h2>4. Underleverandører</h2>
    <p>
      Vi bruker noen utvalgte underleverandører for å levere tjenesten:
    </p>
    <ul>
      <li><strong>Stripe</strong> — betalingsbehandling.</li>
      <li><strong>OpenAI</strong> — AI-generering av bannerdesign.</li>
      <li><strong>Bring/Posten</strong> — frakt og levering.</li>
    </ul>

    <h2>5. Lagringstid</h2>
    <p>
      Bestillinger og fakturadata lagres så lenge regnskapsloven krever
      (5 år). Kontoinformasjon lagres så lenge du har konto. Du kan be
      om sletting av kontoen din ved å kontakte oss.
    </p>

    <h2>6. Dine rettigheter</h2>
    <p>
      Du har rett til innsyn, retting, sletting og dataportabilitet etter
      personopplysningsloven og GDPR. Send oss en e-post på
      <a href="mailto:post@bannershop.no">post@bannershop.no</a> så
      hjelper vi deg.
    </p>

    <h2>7. Informasjonskapsler (cookies)</h2>
    <p>
      Vi bruker funksjonelle informasjonskapsler for å holde deg innlogget
      og huske handlekurven. Vi bruker <strong>ikke</strong> sporings-
      eller markedsføringscookies fra tredjepart.
    </p>

    <p style="margin-top:24px;font-size:13px">
      <em>Sist oppdatert: 11. juni 2026</em>
    </p>
  </InfoPageLayout>
</template>

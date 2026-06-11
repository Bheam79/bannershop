<script setup lang="ts">
import InfoPageLayout from './InfoPageLayout.vue'
</script>

<template>
  <InfoPageLayout
    title="Personvernerklæring"
    icon="fa-shield-halved"
    intro="Beatgrid AS tar personvern på alvor. Her finner du en oversikt over hvilke personopplysninger vi behandler, hvorfor, og hvilke rettigheter du har."
  >
    <h2>1. Behandlingsansvarlig</h2>
    <p>
      <strong>Beatgrid AS</strong>, org.nr. NO 928 177 572 MVA, er
      behandlingsansvarlig for personopplysningene som samles inn via
      bannershop.no. Kontakt oss på
      <a href="mailto:post@bannershop.no">post@bannershop.no</a> ved
      spørsmål om personvern.
    </p>

    <h2>2. Hvilke opplysninger samler vi inn?</h2>
    <p>Vi samler inn følgende kategorier personopplysninger:</p>
    <ul>
      <li>
        <strong>Kontoopplysninger</strong> — navn, e-postadresse og
        kryptert passord ved registrering.
      </li>
      <li>
        <strong>Kontakt- og leveringsdata</strong> — postadresse,
        telefonnummer og andre opplysninger du oppgir i forbindelse med
        en bestilling.
      </li>
      <li>
        <strong>Betalingsdata</strong> — betalinger behandles direkte av
        vår betalingsleverandør Stripe. Beatgrid lagrer ikke fullstendige
        kortopplysninger.
      </li>
      <li>
        <strong>Designinnhold</strong> — banneroppsett, tekst, bilder og
        eventuelle portrettfoto du laster opp eller genererer via
        tjenesten.
      </li>
      <li>
        <strong>Teknisk informasjon</strong> — IP-adresse, nettlesertype,
        og tidspunkt for forespørsler, brukt til feilsøking, sikkerhet
        og misbruksforebygging.
      </li>
      <li>
        <strong>Kjøpshistorikk</strong> — ordreinformasjon og
        transaksjonsdata nødvendig for kundeservice og regnskap.
      </li>
    </ul>

    <h2>3. Formål og rettslig grunnlag</h2>
    <table>
      <thead>
        <tr><th>Formål</th><th>Grunnlag (GDPR art.)</th></tr>
      </thead>
      <tbody>
        <tr>
          <td>Oppfylle bestillingen (produksjon og levering)</td>
          <td>Art. 6(1)(b) — avtaleoppfyllelse</td>
        </tr>
        <tr>
          <td>Kundeservice og reklamasjonsbehandling</td>
          <td>Art. 6(1)(b) — avtaleoppfyllelse</td>
        </tr>
        <tr>
          <td>Fakturering og regnskapsplikt</td>
          <td>Art. 6(1)(c) — rettslig forpliktelse</td>
        </tr>
        <tr>
          <td>Sikkerhetslogging og misbruksforebygging</td>
          <td>Art. 6(1)(f) — berettiget interesse</td>
        </tr>
        <tr>
          <td>Forbedring av tjenesten og feilsøking</td>
          <td>Art. 6(1)(f) — berettiget interesse</td>
        </tr>
      </tbody>
    </table>

    <h2>4. Deling med underleverandører</h2>
    <p>
      Vi deler nødvendige opplysninger med følgende databehandlere
      utelukkende for å levere tjenesten:
    </p>
    <ul>
      <li>
        <strong>Stripe, Inc.</strong> (USA) — betalingsbehandling. Data
        overføres i henhold til EU-US Data Privacy Framework.
      </li>
      <li>
        <strong>OpenAI, LLC</strong> (USA) — AI-generering av
        bannerdesign. Bilder og eventuelle portrettfoto sendes til
        OpenAI kun når du benytter AI-designtjenesten.
      </li>
      <li>
        <strong>Bring / Posten Norge AS</strong> (Norge) — frakt og
        levering av bestilte varer.
      </li>
    </ul>
    <p>
      Vi selger ikke personopplysninger til tredjepart og deler ikke
      data til markedsføringsformål.
    </p>

    <h2>5. Overføring til tredjeland</h2>
    <p>
      Stripe og OpenAI er basert i USA. Overføring skjer basert på EUs
      standard kontraktsklausuler (SCC) og/eller EU-US Data Privacy
      Framework der gjeldende. Du kan be om mer informasjon om
      overføringsmekanismene ved å kontakte oss.
    </p>

    <h2>6. Lagringstid</h2>
    <ul>
      <li>
        <strong>Bestillinger og fakturaer</strong> — lagres i 5 år etter
        transaksjonsdato i henhold til bokføringsloven.
      </li>
      <li>
        <strong>Kontoopplysninger</strong> — lagres så lenge kontoen er
        aktiv. Du kan be om sletting av kontoen når som helst.
      </li>
      <li>
        <strong>Designfiler og opplastede bilder</strong> — lagres så
        lenge kontoen er aktiv eller til du ber om sletting.
      </li>
      <li>
        <strong>Sikkerhetslogger (IP m.m.)</strong> — lagres i 90 dager.
      </li>
    </ul>

    <h2>7. Dine rettigheter</h2>
    <p>
      Under GDPR og personopplysningsloven har du rett til:
    </p>
    <ul>
      <li><strong>Innsyn</strong> — få en kopi av opplysningene vi har om deg.</li>
      <li><strong>Retting</strong> — korrigere uriktige opplysninger.</li>
      <li><strong>Sletting</strong> — be om at vi sletter opplysningene («retten til å bli glemt»), med forbehold om lovpålagte lagringsforpliktelser.</li>
      <li><strong>Begrensning</strong> — be om at vi begrenser behandlingen i visse situasjoner.</li>
      <li><strong>Dataportabilitet</strong> — motta dine opplysninger i et maskinlesbart format.</li>
      <li><strong>Innsigelse</strong> — protestere mot behandling basert på berettiget interesse.</li>
    </ul>
    <p>
      Send forespørsel til
      <a href="mailto:post@bannershop.no">post@bannershop.no</a>. Vi
      svarer innen 30 dager. Du kan også klage til
      <a href="https://www.datatilsynet.no" target="_blank" rel="noopener">Datatilsynet</a>
      dersom du mener vi behandler opplysningene dine feil.
    </p>

    <h2>8. Informasjonskapsler (cookies)</h2>
    <p>
      Vi bruker kun funksjonelle informasjonskapsler som er nødvendige
      for at tjenesten skal fungere — bl.a. for å holde deg innlogget
      og huske handlekurv-status. Vi bruker <strong>ikke</strong> sporings-
      eller markedsføringscookies fra tredjepart, og vi viser ingen
      annonser.
    </p>

    <h2>9. Sikkerhet</h2>
    <p>
      Vi bruker HTTPS for all kommunikasjon, krypterte passord og
      begrensede tilgangsrettigheter internt. Betaling håndteres direkte
      av Stripe og passerer aldri våre servere ukryptert.
    </p>

    <h2>10. Endringer i personvernerklæringen</h2>
    <p>
      Vi kan oppdatere denne erklæringen. Vesentlige endringer varsles
      på forsiden av nettsiden. Datert versjon publiseres alltid her.
    </p>

    <p style="margin-top:32px;font-size:13px;color:#6b7280">
      Beatgrid AS · Org.nr. NO 928 177 572 MVA ·
      <a href="mailto:post@bannershop.no">post@bannershop.no</a><br>
      <em>Sist oppdatert: 11. juni 2026</em>
    </p>
  </InfoPageLayout>
</template>

<style scoped>
table {
  width: 100%;
  border-collapse: collapse;
  margin: 12px 0 20px;
  font-size: 14px;
}
th, td {
  border: 1px solid #e5e7eb;
  padding: 8px 12px;
  text-align: left;
  vertical-align: top;
}
th {
  background: #f9fafb;
  font-weight: 600;
}
</style>

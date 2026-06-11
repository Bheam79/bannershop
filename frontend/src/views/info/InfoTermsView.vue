<script setup lang="ts">
import InfoPageLayout from './InfoPageLayout.vue'
</script>

<template>
  <InfoPageLayout
    title="Brukervilkår"
    icon="fa-file-contract"
    intro="Disse vilkårene regulerer bruken av bannershop.no og alle bestillinger som gjøres gjennom nettsiden. Ved å bruke tjenesten godtar du vilkårene nedenfor."
  >
    <h2>1. Om tjenesten</h2>
    <p>
      bannershop.no eies og drives av <strong>Beatgrid AS</strong>,
      org.nr. NO 928 177 572 MVA, heretter kalt «Beatgrid». Tjenesten
      lar privatpersoner og bedrifter designe og bestille spesialtrykte
      bannere som produseres og sendes med post.
    </p>

    <h2>2. Pris og betaling</h2>
    <p>
      Alle priser er angitt i norske kroner inkl. MVA med mindre noe
      annet er oppgitt. Betaling gjennomføres sikkert via vår
      betalingsleverandør Stripe. Vi aksepterer de vanligste
      betalingskortene (Visa, Mastercard). Ordren settes i produksjon
      umiddelbart etter bekreftet betaling.
    </p>
    <p>
      Du mottar en ordrebekreftelse på e-post. Ta vare på denne som
      kvittering. Faktura kan fås ved henvendelse til
      <a href="mailto:post@bannershop.no">post@bannershop.no</a>.
    </p>

    <h2>3. Produksjon og levering</h2>
    <p>
      Normal produksjonstid er 2–4 virkedager etter mottatt og bekreftet
      bestilling, avhengig av størrelse og antall. Leveringstid via
      Bring/Posten kommer i tillegg — typisk 1–3 virkedager innenlands.
      Vi varsler deg på e-post når pakken er sendt, med sporingsnummer.
    </p>
    <p>
      Beatgrid er ikke ansvarlig for forsinkelser som skyldes
      transportøren eller force majeure-hendelser utenfor vår kontroll.
    </p>

    <h2>4. Angrerett</h2>
    <p>
      Bannere er <strong>spesialtilpassede varer</strong> som produseres
      etter ditt unike design. Angreretten gjelder therefore som
      hovedregel <strong>ikke</strong> for ferdig produserte bannere, jf.
      angrerettloven § 22 bokstav e.
    </p>
    <p>
      For digitale AI-designtjenester som er levert (generert) anses
      tjenesten som forbrukt, jf. angrerettloven § 22 bokstav a
      (digitalt innhold).
    </p>

    <h2>5. Reklamasjon og garanti</h2>
    <p>
      Mottar du et banner med trykk- eller materialfeil som skyldes oss,
      kontakt oss på
      <a href="mailto:post@bannershop.no">post@bannershop.no</a> innen
      rimelig tid etter at feilen ble oppdaget — senest innen 2 måneder.
      Legg ved bestillingsnummer og bilder av feilen. Vi omtrykker
      banneret vederlagsfritt eller refunderer kjøpesummen i tråd med
      forbrukerkjøpsloven.
    </p>
    <p>
      Kunden er ansvarlig for at opplastet innhold (tekst, bilder,
      design) er korrekt. Vi kontrollerer ikke korrektur med mindre du
      har kjøpt en designtjeneste.
    </p>

    <h2>6. AI-designtjeneste</h2>
    <p>
      Når du benytter AI-designtjenesten, genereres bildeinnhold ved
      hjelp av en tredjepartsleverandør (OpenAI). Det genererte designet
      er ment for personlig bruk knyttet til banneret du bestiller.
      Beatgrid garanterer ikke at AI-generert innhold er fritt for
      feil, og anbefaler at du gjennomgår designet nøye før bestilling.
    </p>
    <p>
      Du beholder bruksretten til ditt eget opplastede innhold.
      AI-genererte deler av designet leveres «som de er».
    </p>

    <h2>7. Kundens ansvar</h2>
    <p>
      Du er ansvarlig for at opplastet innhold ikke krenker tredjeparts
      opphavsrettigheter, varemerker eller andre rettigheter, og at
      innholdet ikke er ulovlig, støtende eller villedende. Beatgrid
      forbeholder seg retten til å avvise bestillinger som inneholder
      slikt materiale.
    </p>

    <h2>8. Ansvarsbegrensning</h2>
    <p>
      Beatgrids samlede ansvar overfor kunden er i alle tilfeller
      begrenset til verdien av den aktuelle ordren. Vi er ikke ansvarlige
      for indirekte tap, tapt fortjeneste eller konsekvenstap.
    </p>

    <h2>9. Personopplysninger</h2>
    <p>
      Vi behandler personopplysninger i henhold til vår
      <a href="/info/privacy">personvernerklæring</a>.
    </p>

    <h2>10. Tvisteløsning</h2>
    <p>
      Disse vilkårene er underlagt norsk rett. Tvister søkes løst i
      minnelighet. Dersom dette ikke lykkes, kan saken bringes inn for
      <a href="https://www.forbrukerradet.no" target="_blank" rel="noopener">Forbrukerrådet</a>
      eller de ordinære norske domstoler med Oslo tingrett som verneting.
    </p>

    <h2>11. Endringer i vilkårene</h2>
    <p>
      Beatgrid forbeholder seg retten til å endre disse vilkårene.
      Vesentlige endringer varsles på forsiden av nettsiden. Fortsatt
      bruk etter ikrafttredelsesdato innebærer aksept av de nye vilkårene.
    </p>

    <p style="margin-top:32px;font-size:13px;color:#6b7280">
      Beatgrid AS · Org.nr. NO 928 177 572 MVA ·
      <a href="mailto:post@bannershop.no">post@bannershop.no</a><br>
      <em>Sist oppdatert: 11. juni 2026</em>
    </p>
  </InfoPageLayout>
</template>

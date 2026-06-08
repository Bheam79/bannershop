<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/auth'
import { useCartStore } from '@/stores/cart'
import AiCreditBadge from '@/components/layout/AiCreditBadge.vue'

const router = useRouter()
const auth = useAuthStore()
const cart = useCartStore()

/* ── Category data ──────────────────────────────────────────── */
interface Cat {
  id: string
  icon: string
  name: string
  occ: string
  price: number
  img: string
  big: string
  sub: string
}

const CATS: Cat[] = [
  {
    id: 'bursdag', icon: 'fa-cake-candles', name: 'Bursdag', occ: 'Bursdagsbanner', price: 810,
    img: '/banners/banner-emma-princess.png',
    big: 'Gratulerer med dagen!', sub: 'Emma fyller 6 år',
  },
  {
    id: 'konfirmasjon', icon: 'fa-graduation-cap', name: 'Konfirmasjon', occ: 'Konfirmasjonsbanner', price: 810,
    img: '/banners/banner-konfirmasjon.png',
    big: 'Konfirmant 2026', sub: 'Gratulerer, Jonas!',
  },
  {
    id: 'dap', icon: 'fa-leaf', name: 'Dåp', occ: 'Dåpsbanner', price: 810,
    img: '/banners/banner-dap.png',
    big: 'Velkommen til verden', sub: 'Lille Markus',
  },
  {
    id: 'bryllup', icon: 'fa-ring', name: 'Bryllup', occ: 'Bryllupsbanner', price: 945,
    img: '/banners/banner-bryllup.png',
    big: 'Maria & Johan', sub: 'For alltid · 12.07.2026',
  },
  {
    id: 'sommerfest', icon: 'fa-sun', name: 'Sommerfest', occ: 'Sommerfestbanner', price: 810,
    img: '/banners/banner-sommerfest.png',
    big: 'Sommerfest 2026', sub: 'Velkommen alle sammen',
  },
]

const selectedCat = ref<Cat>(CATS[0]!)

function select(cat: Cat) {
  selectedCat.value = cat
  // Scroll to preview section
  const el = document.getElementById('bestill')
  if (el) el.scrollIntoView({ behavior: 'smooth', block: 'start' })
}

function scrollTo(id: string) {
  const el = document.getElementById(id)
  if (el) el.scrollIntoView({ behavior: 'smooth', block: 'start' })
}

async function handleLogout() {
  await auth.logoutFromServer()
  router.push('/login')
}
</script>

<template>
  <div style="background:var(--bg);color:var(--text);font-family:var(--font-ui)">

    <!-- ═══════════════════════ NAV ═══════════════════════════ -->
    <header style="position:sticky;top:0;z-index:50;background:rgba(21,18,14,.82);backdrop-filter:saturate(140%) blur(14px);border-bottom:1px solid var(--line-soft)">
      <div class="wrap" style="display:flex;align-items:center;justify-content:space-between;height:72px">

        <!-- Brand -->
        <a href="/" style="display:flex;align-items:center;gap:11px;font-family:var(--font-display);font-weight:700;font-size:20px;letter-spacing:-.02em;text-decoration:none;color:var(--text)">
          <span style="width:34px;height:34px;border-radius:9px;display:grid;place-items:center;overflow:hidden;background:var(--accent)">
            <img src="/logo.png" alt="BannerShop.no" style="width:22px;height:22px;object-fit:contain">
          </span>
          <span>Banner<b style="color:var(--accent)">Shop</b>.no</span>
        </a>

        <!-- Links -->
        <nav style="display:flex;align-items:center;gap:30px">
          <a href="#anledninger" @click.prevent="scrollTo('anledninger')" style="color:var(--muted);font-weight:500;font-size:15.5px;transition:color .15s;text-decoration:none" class="nav-link">Anledninger</a>
          <a href="#bestill" @click.prevent="scrollTo('bestill')" style="color:var(--muted);font-weight:500;font-size:15.5px;transition:color .15s;text-decoration:none" class="nav-link">Bestill</a>
          <a href="#lagselv" @click.prevent="scrollTo('lagselv')" style="color:var(--muted);font-weight:500;font-size:15.5px;transition:color .15s;text-decoration:none" class="nav-link">Lag ditt banner</a>
          <a href="#om-oss" style="color:var(--muted);font-weight:500;font-size:15.5px;transition:color .15s;text-decoration:none" class="nav-link">Om oss</a>
        </nav>

        <!-- CTA -->
        <div style="display:flex;align-items:center;gap:14px">
          <template v-if="auth.isLoggedIn">
            <a href="/account" @click.prevent="router.push('/account')" style="color:var(--muted);font-weight:500;font-size:15.5px;text-decoration:none" class="nav-link">Min konto</a>
            <AiCreditBadge />
            <button class="btn btn-ghost" style="font-size:14px;padding:8px 14px" @click="handleLogout">Logg ut</button>
          </template>
          <template v-else>
            <a href="/login" @click.prevent="router.push('/login')" style="color:var(--muted);font-weight:500;font-size:15.5px;text-decoration:none" class="nav-link">Logg inn</a>
            <button class="btn btn-primary" @click="router.push('/register')">Registrer</button>
          </template>
          <a v-if="cart.itemCount > 0" href="/checkout" @click.prevent="router.push('/checkout')"
             class="btn btn-primary" style="background:var(--gold);color:#1a0d06;box-shadow:none">
            <i class="fa-solid fa-cart-shopping"></i> ({{ cart.itemCount }})
          </a>
        </div>
      </div>
    </header>

    <!-- ═══════════════════════ HERO ══════════════════════════ -->
    <section id="top" style="position:relative;padding:74px 0 30px;overflow:hidden">
      <!-- glow blobs -->
      <div style="position:absolute;width:520px;height:520px;border-radius:50%;background:rgba(255,106,61,.18);top:-160px;right:-120px;filter:blur(90px);z-index:0;pointer-events:none"></div>
      <div style="position:absolute;width:420px;height:420px;border-radius:50%;background:rgba(197,65,122,.14);bottom:-200px;left:-140px;filter:blur(90px);z-index:0;pointer-events:none"></div>

      <div class="wrap" style="display:grid;grid-template-columns:1.05fr .95fr;gap:54px;align-items:center;position:relative;z-index:1">

        <!-- Left text -->
        <div>
          <span style="display:inline-flex;align-items:center;gap:9px;font-size:13px;font-weight:600;letter-spacing:.04em;text-transform:uppercase;color:var(--accent-2);background:rgba(255,106,61,.1);border:1px solid rgba(255,106,61,.25);border-radius:999px;padding:7px 14px;margin-bottom:22px">
            <span style="width:7px;height:7px;border-radius:50%;background:var(--accent);box-shadow:0 0 0 3px rgba(255,106,61,.2)"></span>
            Norsk trykkeri · Levering i hele Norge
          </span>
          <h1 class="display" style="font-size:clamp(40px,5.4vw,68px);margin-bottom:20px">
            Bannere for de <em style="color:var(--accent);font-style:normal;white-space:nowrap">store øyeblikkene</em> i livet.
          </h1>
          <p style="font-size:19px;color:var(--muted);max-width:30em;margin-bottom:30px">
            Fra dåp til bryllup, konfirmasjon til sommerfest – vi trykker værbestandige bannere i fullfarge på kraftig PVC. Velg en anledning, så er du i gang.
          </p>
          <div style="display:flex;gap:14px;flex-wrap:wrap;align-items:center">
            <a href="#anledninger" @click.prevent="scrollTo('anledninger')" class="btn btn-primary btn-lg">Velg anledning →</a>
            <a href="#lagselv" @click.prevent="scrollTo('lagselv')" class="btn btn-ghost btn-lg">Lag ditt eget design</a>
          </div>
          <div style="display:flex;gap:22px;flex-wrap:wrap;margin-top:30px">
            <span style="display:flex;align-items:center;gap:9px;color:var(--muted);font-size:14.5px;font-weight:500"><i class="fa-solid fa-check" style="color:var(--accent)"></i> Kraftig værbestandig PVC</span>
            <span style="display:flex;align-items:center;gap:9px;color:var(--muted);font-size:14.5px;font-weight:500"><i class="fa-solid fa-check" style="color:var(--accent)"></i> UV-bestandig fullfargetrykk</span>
            <span style="display:flex;align-items:center;gap:9px;color:var(--muted);font-size:14.5px;font-weight:500"><i class="fa-solid fa-check" style="color:var(--accent)"></i> Levering i hele Norge</span>
          </div>
        </div>

        <!-- Right visual: two overlapping banner cards -->
        <div style="position:relative;height:440px">
          <!-- Confetti -->
          <span style="position:absolute;top:18px;left:30%;width:10px;height:10px;background:var(--gold);transform:rotate(20deg);border-radius:2px;opacity:.85"></span>
          <span style="position:absolute;top:60px;right:8%;width:8px;height:8px;background:var(--accent-2);transform:rotate(-15deg);border-radius:2px;opacity:.85"></span>
          <span style="position:absolute;bottom:40px;left:6%;width:9px;height:9px;background:#3a9d7e;border-radius:50%;opacity:.85"></span>
          <span style="position:absolute;top:0;left:62%;width:7px;height:7px;background:#c5417a;border-radius:50%;opacity:.85"></span>

          <!-- Main banner card (princess) -->
          <div class="hero-banner-card" style="position:absolute;top:54px;left:0;width:77%;height:210px;z-index:3;transform:rotate(-3deg)">
            <img src="/banners/banner-emma-princess.png" alt="Bursdagsbanner Emma" style="width:100%;height:100%;object-fit:cover;display:block;border-radius:14px">
            <span class="grommet" style="top:9px;left:9px"></span>
            <span class="grommet" style="top:9px;right:9px"></span>
            <span class="grommet" style="bottom:9px;left:9px"></span>
            <span class="grommet" style="bottom:9px;right:9px"></span>
          </div>

          <!-- Secondary banner card (summer) -->
          <div class="hero-banner-card" style="position:absolute;bottom:6px;right:0;width:64%;height:152px;z-index:2;transform:rotate(4deg)">
            <img src="/banners/banner-sommerfest.png" alt="Sommerfestbanner" style="width:100%;height:100%;object-fit:cover;display:block;border-radius:14px">
            <span class="grommet" style="top:9px;left:9px"></span>
            <span class="grommet" style="top:9px;right:9px"></span>
            <span class="grommet" style="bottom:9px;left:9px"></span>
            <span class="grommet" style="bottom:9px;right:9px"></span>
          </div>
        </div>
      </div>
    </section>

    <!-- ═══════════════════ CATEGORIES ════════════════════════ -->
    <section id="anledninger" style="position:relative;padding:64px 0;z-index:1">
      <div class="wrap">
        <div style="margin-bottom:34px;max-width:640px">
          <div style="font-size:13px;font-weight:700;letter-spacing:.08em;text-transform:uppercase;color:var(--accent-2);margin-bottom:12px">Hva feirer du?</div>
          <h2 class="display" style="font-size:clamp(28px,3.4vw,42px);margin-bottom:12px">Velg anledning</h2>
          <p style="color:var(--muted);font-size:18px">Vi har ferdige oppsett for de vanligste feiringene. Trykk på en anledning for å se forhåndsvisning og pris.</p>
        </div>

        <div style="display:grid;grid-template-columns:repeat(3,1fr);gap:20px">
          <!-- Occasion cards -->
          <button
            v-for="cat in CATS"
            :key="cat.id"
            class="cat-card"
            :class="{ sel: selectedCat.id === cat.id }"
            @click="select(cat)"
          >
            <div class="cat-media">
              <img :src="cat.img" :alt="cat.name + 'sbanner'" class="cat-img">
              <span class="cat-emoji"><i :class="['fa-solid', cat.icon]"></i></span>
              <div class="cat-scrim"></div>
            </div>
            <div class="cat-body">
              <div>
                <h3 class="display" style="font-size:21px;letter-spacing:-.01em;color:var(--text)">{{ cat.name }}</h3>
                <div style="font-size:13px;color:var(--faint);margin-top:3px">fra <b style="color:var(--text);font-weight:600">{{ cat.price }} kr</b></div>
              </div>
              <span class="cat-arrow">→</span>
            </div>
          </button>

          <!-- Custom card -->
          <button class="cat-card cat-custom" @click="router.push('/banner-builder')">
            <div class="cat-body" style="flex-direction:column;text-align:center;width:100%">
              <span style="width:38px;height:38px;border-radius:10px;background:var(--surface-2);border:1px solid rgba(255,255,255,.1);display:grid;place-items:center;font-size:18px;margin-bottom:12px"><i class="fa-solid fa-wand-magic-sparkles" style="color:var(--gold)"></i></span>
              <h3 class="display" style="font-size:21px;letter-spacing:-.01em;color:var(--text)">Noe annet?</h3>
              <div style="font-size:13px;color:var(--faint);margin-top:6px">Egendesignet banner – din anledning, din tekst</div>
              <span class="cat-arrow" style="margin-top:12px">→</span>
            </div>
          </button>
        </div>
      </div>
    </section>

    <!-- ═══════════════════ PREVIEW + PRICE ═══════════════════ -->
    <section id="bestill" style="padding:8px 0 64px;position:relative;z-index:1;scroll-margin-top:90px">
      <div class="wrap">
        <div style="display:grid;grid-template-columns:1.25fr .85fr;gap:24px;align-items:stretch">

          <!-- Preview panel -->
          <div class="panel">
            <div class="panel-title">
              Forhåndsvisning
              <span class="badge-sel">{{ selectedCat.name }}</span>
            </div>
            <div class="stage">
              <div class="pv-banner">
                <img :src="selectedCat.img" :key="selectedCat.id" alt="" style="position:absolute;inset:0;width:100%;height:100%;object-fit:cover;border-radius:8px">
                <span class="pv-grommet" style="top:10px;left:10px"></span>
                <span class="pv-grommet" style="top:10px;right:10px"></span>
                <span class="pv-grommet" style="bottom:10px;left:10px"></span>
                <span class="pv-grommet" style="bottom:10px;right:10px"></span>
              </div>
              <span class="dim-tag">300 × 150 cm · mest populær</span>
            </div>
            <ul class="feat-list">
              <li><i class="fa-solid fa-check" style="color:var(--accent)"></i> UV-bestandig trykk i fullfarge</li>
              <li><i class="fa-solid fa-check" style="color:var(--accent)"></i> Klart for både inne og ute</li>
              <li><i class="fa-solid fa-check" style="color:var(--accent)"></i> Kraftig, værbestandig PVC</li>
              <li><i class="fa-solid fa-check" style="color:var(--accent)"></i> Maljer i hjørnene som tilvalg</li>
            </ul>
          </div>

          <!-- Price card -->
          <div class="panel" style="display:flex;flex-direction:column">
            <div>
              <div style="font-size:14px;color:var(--faint);margin-bottom:2px">{{ selectedCat.name }}</div>
              <div class="display" style="font-size:24px;font-weight:700;margin-bottom:18px">{{ selectedCat.occ }}</div>
            </div>
            <div style="display:flex;align-items:baseline;gap:8px;margin-bottom:4px">
              <span style="font-size:14px;color:var(--faint)">fra</span>
              <span class="display" style="font-weight:800;font-size:40px;color:var(--text);white-space:nowrap">
                {{ selectedCat.price }} <span style="font-size:22px;color:var(--muted);font-weight:600">kr</span>
              </span>
            </div>
            <div style="font-size:13.5px;color:var(--faint);margin-bottom:20px">Standardstørrelse 300 × 150 cm. Pris inkl. trykk &amp; ferdigstilling.</div>
            <div style="height:1px;background:var(--line-soft);margin:4px 0 18px"></div>
            <ul style="display:grid;gap:9px;margin-bottom:22px">
              <li style="list-style:none;display:flex;align-items:center;gap:10px;font-size:14.5px;color:var(--muted)"><i class="fa-solid fa-check" style="color:var(--accent)"></i> Ferdig oppsett – bare bytt navn &amp; dato</li>
              <li style="list-style:none;display:flex;align-items:center;gap:10px;font-size:14.5px;color:var(--muted)"><i class="fa-solid fa-check" style="color:var(--accent)"></i> Gratis korrektur før trykk</li>
              <li style="list-style:none;display:flex;align-items:center;gap:10px;font-size:14.5px;color:var(--muted)"><i class="fa-solid fa-check" style="color:var(--accent)"></i> Maljer i hjørnene som rimelig tilvalg</li>
            </ul>
            <div style="margin-top:auto;display:grid;gap:10px">
              <button class="btn btn-primary btn-lg" style="justify-content:center;width:100%" @click="router.push('/banner-builder/ai')">
                <i class="fa-solid fa-cart-shopping"></i> Kom i gang med dette
              </button>
              <div style="font-size:13px;color:var(--faint);text-align:center;margin-top:2px">
                Trenger du en annen størrelse? <b style="color:var(--muted);font-weight:600">Velg egen størrelse i neste steg.</b>
              </div>
            </div>
          </div>
        </div>
      </div>
    </section>

    <!-- ═══════════════════ MAKE YOUR OWN ═════════════════════ -->
    <section id="lagselv" style="padding:64px 0;position:relative;z-index:1">
      <div class="wrap">
        <div style="margin-bottom:34px;max-width:640px;margin-left:auto;margin-right:auto;text-align:center">
          <div style="font-size:13px;font-weight:700;letter-spacing:.08em;text-transform:uppercase;color:var(--accent-2);margin-bottom:12px">Helt ditt eget</div>
          <h2 class="display" style="font-size:clamp(28px,3.4vw,42px);margin-bottom:12px">Lag ditt eget banner</h2>
          <p style="color:var(--muted);font-size:18px">Last opp et ferdig design, la AI lage det for deg, eller få proffene våre til å designe – du velger.</p>
        </div>

        <div style="display:grid;grid-template-columns:repeat(3,1fr);gap:20px">

          <!-- Upload -->
          <div class="make-card" @click="router.push('/banner-builder/upload')" style="cursor:pointer">
            <div class="make-ico"><i class="fa-solid fa-folder-open"></i></div>
            <h3 class="display" style="font-size:20px;margin-bottom:8px">Last opp eget design</h3>
            <p style="color:var(--muted);font-size:15px;flex:1;margin-bottom:16px">Har du fil klar? Last opp bilde eller PDF, så regner vi ut bredden automatisk og viser forhåndsvisning.</p>
            <div style="font-size:14px;color:var(--faint);margin-bottom:14px">Samme pris som standardbanner</div>
            <button class="btn btn-soft" style="justify-content:center;width:100%">Last opp fil →</button>
          </div>

          <!-- AI -->
          <div class="make-card make-feat" style="cursor:pointer" @click="router.push('/banner-builder/ai')">
            <span class="make-tag">Ferdig på minutter</span>
            <div class="make-ico" style="background:rgba(255,106,61,.14);border-color:rgba(255,106,61,.3)"><i class="fa-solid fa-wand-magic-sparkles" style="color:var(--accent)"></i></div>
            <h3 class="display" style="font-size:20px;margin-bottom:8px">AI-designet banner</h3>
            <p style="color:var(--muted);font-size:15px;flex:1;margin-bottom:16px">Velg anledning, skriv inn navn og tekst – så lager AI et unikt banner for deg på minutter.</p>
            <div style="font-size:14px;color:var(--faint);margin-bottom:14px"><b style="color:#4ade80;font-family:var(--font-display);font-weight:700;font-size:18px">Gratis</b> for første generering</div>
            <button class="btn btn-primary" style="justify-content:center;width:100%">Lag AI-banner →</button>
          </div>

          <!-- Manual design -->
          <div class="make-card" style="cursor:pointer" @click="router.push('/banner-builder/manual')">
            <span class="make-tag">2–3 virkedager</span>
            <div class="make-ico"><i class="fa-solid fa-palette"></i></div>
            <h3 class="display" style="font-size:20px;margin-bottom:8px">Vi designer for deg</h3>
            <p style="color:var(--muted);font-size:15px;flex:1;margin-bottom:16px">Fyll inn ønskene dine, så lager designteamet vårt et forslag og sender til godkjenning.</p>
            <div style="font-size:14px;color:var(--faint);margin-bottom:14px"><b style="color:var(--text);font-family:var(--font-display);font-weight:700;font-size:18px">495 kr</b> for designtjeneste</div>
            <button class="btn btn-soft" style="justify-content:center;width:100%">Bestill design →</button>
          </div>
        </div>
      </div>
    </section>

    <!-- ═══════════════════ TRUST STRIP ════════════════════════ -->
    <div style="border-top:1px solid var(--line-soft);border-bottom:1px solid var(--line-soft);background:var(--bg-2)">
      <div class="wrap" style="display:grid;grid-template-columns:repeat(4,1fr);gap:24px;padding:42px 0">
        <div class="strip-item">
          <span class="strip-icon"><i class="fa-solid fa-industry"></i></span>
          <h4 class="display" style="font-size:17px;margin-bottom:4px">Eget lokalt trykkeri</h4>
          <p style="color:var(--muted);font-size:14px">Vi trykker alt selv – full kontroll på kvalitet og leveringstid.</p>
        </div>
        <div class="strip-item">
          <span class="strip-icon"><i class="fa-solid fa-truck"></i></span>
          <h4 class="display" style="font-size:17px;margin-bottom:4px">Rask levering</h4>
          <p style="color:var(--muted);font-size:14px">Standard 2–3 dager, eller ekspress når det haster.</p>
        </div>
        <div class="strip-item">
          <span class="strip-icon"><i class="fa-solid fa-shield-halved"></i></span>
          <h4 class="display" style="font-size:17px;margin-bottom:4px">Værbestandig</h4>
          <p style="color:var(--muted);font-size:14px">UV-bestandig fullfargetrykk på kraftig PVC – tåler norsk vær.</p>
        </div>
        <div class="strip-item">
          <span class="strip-icon"><i class="fa-solid fa-circle-check"></i></span>
          <h4 class="display" style="font-size:17px;margin-bottom:4px">Alltid ferdigstilt</h4>
          <p style="color:var(--muted);font-size:14px">Sydde kanter og maljer i hjørnene følger med på alle bannere.</p>
        </div>
      </div>
    </div>

    <!-- ═══════════════════ CTA BAND ═══════════════════════════ -->
    <section style="padding:64px 0;position:relative;z-index:1">
      <div class="wrap">
        <div class="cta-band">
          <span style="position:absolute;top:24px;left:12%;width:12px;height:12px;background:#fff;transform:rotate(20deg);border-radius:2px;opacity:.5"></span>
          <span style="position:absolute;top:54px;right:14%;width:10px;height:10px;background:var(--gold);border-radius:50%;opacity:.5"></span>
          <span style="position:absolute;bottom:30px;left:22%;width:9px;height:9px;background:#1a0d06;transform:rotate(-15deg);border-radius:2px;opacity:.5"></span>
          <h2 class="display" style="font-size:clamp(28px,3.6vw,40px);color:#fff;margin-bottom:12px;font-weight:800">Klar til å feire?</h2>
          <p style="color:rgba(255,255,255,.92);font-size:18px;margin-bottom:26px;max-width:34em;margin-left:auto;margin-right:auto">Velg en anledning, tilpass teksten, og ha banneret hengende før festen starter.</p>
          <a href="#anledninger" @click.prevent="scrollTo('anledninger')" class="btn btn-lg" style="background:#1a0d06;color:#fff">Kom i gang nå →</a>
        </div>
      </div>
    </section>

    <!-- ═══════════════════ FOOTER ═════════════════════════════ -->
    <footer style="padding:46px 0 40px;border-top:1px solid var(--line-soft);margin-top:8px">
      <div class="wrap">
        <div style="display:flex;justify-content:space-between;align-items:flex-start;gap:30px;flex-wrap:wrap">
          <!-- Brand -->
          <div style="max-width:280px">
            <a href="/" style="display:flex;align-items:center;gap:11px;font-family:var(--font-display);font-weight:700;font-size:20px;letter-spacing:-.02em;text-decoration:none;color:var(--text);margin-bottom:12px">
              <span style="width:34px;height:34px;border-radius:9px;display:grid;place-items:center;overflow:hidden;background:var(--accent)">
                <img src="/logo.png" alt="BannerShop.no" style="width:22px;height:22px;object-fit:contain">
              </span>
              <span>Banner<b style="color:var(--accent)">Shop</b>.no</span>
            </a>
            <p style="color:var(--muted);font-size:14.5px">Kvalitetsbannere fra norsk trykkeri. Vi gjør de store øyeblikkene synlige.</p>
          </div>

          <!-- Anledninger -->
          <div>
            <h5 style="font-size:13px;text-transform:uppercase;letter-spacing:.06em;color:var(--faint);margin-bottom:12px;font-weight:700">Anledninger</h5>
            <a href="#anledninger" @click.prevent="scrollTo('anledninger')" style="display:block;color:var(--muted);font-size:14.5px;margin-bottom:8px" class="foot-link">Bursdag</a>
            <a href="#anledninger" @click.prevent="scrollTo('anledninger')" style="display:block;color:var(--muted);font-size:14.5px;margin-bottom:8px" class="foot-link">Konfirmasjon</a>
            <a href="#anledninger" @click.prevent="scrollTo('anledninger')" style="display:block;color:var(--muted);font-size:14.5px;margin-bottom:8px" class="foot-link">Dåp</a>
            <a href="#anledninger" @click.prevent="scrollTo('anledninger')" style="display:block;color:var(--muted);font-size:14.5px;margin-bottom:8px" class="foot-link">Bryllup</a>
            <a href="#anledninger" @click.prevent="scrollTo('anledninger')" style="display:block;color:var(--muted);font-size:14.5px;margin-bottom:8px" class="foot-link">Sommerfest</a>
          </div>

          <!-- Lag banner -->
          <div>
            <h5 style="font-size:13px;text-transform:uppercase;letter-spacing:.06em;color:var(--faint);margin-bottom:12px;font-weight:700">Lag banner</h5>
            <a href="/banner-builder/upload" @click.prevent="router.push('/banner-builder/upload')" style="display:block;color:var(--muted);font-size:14.5px;margin-bottom:8px" class="foot-link">Last opp design</a>
            <a href="/banner-builder/ai" @click.prevent="router.push('/banner-builder/ai')" style="display:block;color:var(--muted);font-size:14.5px;margin-bottom:8px" class="foot-link">AI-banner</a>
            <a href="/banner-builder/manual" @click.prevent="router.push('/banner-builder/manual')" style="display:block;color:var(--muted);font-size:14.5px;margin-bottom:8px" class="foot-link">Vi designer for deg</a>
          </div>

          <!-- Kundeservice -->
          <div>
            <h5 style="font-size:13px;text-transform:uppercase;letter-spacing:.06em;color:var(--faint);margin-bottom:12px;font-weight:700">Kundeservice</h5>
            <a href="#" style="display:block;color:var(--muted);font-size:14.5px;margin-bottom:8px" class="foot-link">Frakt &amp; levering</a>
            <a href="#" style="display:block;color:var(--muted);font-size:14.5px;margin-bottom:8px" class="foot-link">Materialer</a>
            <a href="#" style="display:block;color:var(--muted);font-size:14.5px;margin-bottom:8px" class="foot-link">Kontakt oss</a>
            <a href="/login" @click.prevent="router.push('/login')" style="display:block;color:var(--muted);font-size:14.5px;margin-bottom:8px" class="foot-link">Logg inn</a>
          </div>
        </div>

        <div style="margin-top:34px;padding-top:22px;border-top:1px solid var(--line-soft);color:var(--faint);font-size:13.5px;display:flex;justify-content:space-between;flex-wrap:wrap;gap:10px">
          <span>© 2026 BannerShop.no — Kvalitetsbannere fra norsk trykkeri</span>
          <span>Org. 999 888 777 · Laget i Norge 🇳🇴</span>
        </div>
      </div>
    </footer>

  </div>
</template>

<style scoped>
/* ── Nav link hover ─────────────────────────────────────────── */
.nav-link:hover { color: var(--text) !important; }
.foot-link:hover { color: var(--text) !important; }

/* ── Hero banner cards ──────────────────────────────────────── */
.hero-banner-card {
  border-radius: 14px;
  overflow: hidden;
  box-shadow: 0 30px 60px -22px rgba(0,0,0,.7);
  border: 1px solid rgba(255,255,255,.07);
  position: relative;
}
.grommet {
  position: absolute;
  width: 11px;
  height: 11px;
  border-radius: 50%;
  background: #d8d2c6;
  box-shadow: inset 0 1px 2px rgba(0,0,0,.5);
  z-index: 2;
}

/* ── Category cards ─────────────────────────────────────────── */
.cat-card {
  position: relative;
  border-radius: var(--radius);
  overflow: hidden;
  cursor: pointer;
  background: var(--surface);
  border: 1px solid var(--line-soft);
  transition: transform .18s ease, border-color .18s ease, box-shadow .25s ease;
  text-align: left;
  min-height: 260px;
  display: flex;
  flex-direction: column;
  color: var(--text);
  font-family: var(--font-ui);
}
.cat-card:hover {
  transform: translateY(-4px);
  border-color: var(--accent);
  box-shadow: 0 24px 46px -26px var(--glow);
}
.cat-card.sel {
  border-color: var(--accent);
  box-shadow: 0 0 0 2px var(--accent), 0 24px 46px -26px var(--glow);
}
.cat-card:hover .cat-arrow,
.cat-card.sel .cat-arrow {
  background: var(--accent);
  color: var(--accent-ink);
  border-color: var(--accent);
}
.cat-media {
  position: relative;
  height: 172px;
  background: var(--surface-2);
  overflow: hidden;
}
.cat-img {
  width: 100%;
  height: 100%;
  object-fit: cover;
  display: block;
}
.cat-scrim {
  position: absolute;
  inset: 0;
  background: linear-gradient(180deg, rgba(21,18,14,0) 40%, rgba(21,18,14,.82) 100%);
  pointer-events: none;
}
.cat-emoji {
  position: absolute;
  top: 12px;
  left: 12px;
  width: 38px;
  height: 38px;
  border-radius: 10px;
  background: rgba(21,18,14,.6);
  backdrop-filter: blur(6px);
  display: grid;
  place-items: center;
  font-size: 20px;
  border: 1px solid rgba(255,255,255,.1);
}
.cat-body {
  padding: 16px 18px 18px;
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  flex: 1;
}
.cat-arrow {
  flex: none;
  width: 34px;
  height: 34px;
  border-radius: 50%;
  border: 1px solid var(--line);
  display: grid;
  place-items: center;
  color: var(--muted);
  transition: all .18s;
  font-size: 15px;
}
.cat-custom {
  background: linear-gradient(160deg, var(--surface-2), var(--surface));
  border-style: dashed;
  border-color: var(--line);
  min-height: auto;
  justify-content: center;
  align-items: center;
  text-align: center;
  padding: 30px 20px;
}
.cat-custom .cat-body { flex-direction: column; text-align: center; width: 100%; }
.cat-custom .cat-arrow { margin-top: 6px; }

/* ── Preview panel ──────────────────────────────────────────── */
.panel {
  background: var(--surface);
  border: 1px solid var(--line-soft);
  border-radius: var(--radius);
  padding: 26px;
}
.panel-title {
  font-family: var(--font-display);
  font-weight: 700;
  font-size: 18px;
  margin-bottom: 18px;
  display: flex;
  align-items: center;
  gap: 10px;
}
.badge-sel {
  font-family: var(--font-ui);
  font-size: 12px;
  font-weight: 600;
  color: var(--accent-2);
  background: rgba(255,106,61,.12);
  border: 1px solid rgba(255,106,61,.25);
  padding: 3px 10px;
  border-radius: 999px;
}
.stage {
  background:
    linear-gradient(0deg, rgba(0,0,0,.25), rgba(0,0,0,.25)),
    repeating-linear-gradient(45deg, #1d1a14 0 14px, #201c16 14px 28px);
  border: 1px solid var(--line-soft);
  border-radius: 14px;
  padding: 34px;
  display: grid;
  place-items: center;
  min-height: 280px;
  position: relative;
}
.pv-banner {
  position: relative;
  width: 86%;
  max-width: 440px;
  aspect-ratio: 300/150;
  border-radius: 8px;
  box-shadow: 0 22px 44px -18px rgba(0,0,0,.7);
  overflow: hidden;
  transition: opacity .25s ease;
}
.pv-grommet {
  position: absolute;
  width: 12px;
  height: 12px;
  border-radius: 50%;
  background: #e6e0d4;
  box-shadow: inset 0 1px 2px rgba(0,0,0,.55);
  z-index: 2;
}
.dim-tag {
  position: absolute;
  bottom: 12px;
  left: 50%;
  transform: translateX(-50%);
  font-size: 12px;
  color: var(--faint);
  background: rgba(21,18,14,.7);
  padding: 4px 10px;
  border-radius: 6px;
  font-variant-numeric: tabular-nums;
  white-space: nowrap;
}
.feat-list {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 10px 18px;
  margin-top: 20px;
  padding: 0;
  list-style: none;
}
.feat-list li {
  display: flex;
  align-items: center;
  gap: 9px;
  color: var(--muted);
  font-size: 14.5px;
}

/* ── Make-your-own cards ────────────────────────────────────── */
.make-card {
  background: var(--surface);
  border: 1px solid var(--line-soft);
  border-radius: var(--radius);
  padding: 26px;
  display: flex;
  flex-direction: column;
  transition: border-color .18s, transform .18s;
  position: relative;
}
.make-card:hover {
  border-color: var(--line);
  transform: translateY(-3px);
}
.make-feat {
  border-color: rgba(255,106,61,.4);
  background: linear-gradient(170deg, rgba(255,106,61,.07), var(--surface));
}
.make-ico {
  width: 48px;
  height: 48px;
  border-radius: 13px;
  display: grid;
  place-items: center;
  font-size: 24px;
  background: var(--surface-2);
  border: 1px solid var(--line-soft);
  margin-bottom: 16px;
}
.make-tag {
  position: absolute;
  top: 18px;
  right: 18px;
  font-size: 12px;
  font-weight: 600;
  color: var(--gold);
  background: rgba(231,185,78,.12);
  border: 1px solid rgba(231,185,78,.28);
  padding: 4px 10px;
  border-radius: 999px;
}

/* ── Trust strip ────────────────────────────────────────────── */
.strip-item { display: flex; flex-direction: column; gap: 6px; }
.strip-icon {
  font-size: 20px;
  width: 42px;
  height: 42px;
  border-radius: 10px;
  background: var(--surface-2);
  border: 1px solid var(--line-soft);
  display: grid;
  place-items: center;
  color: var(--accent);
  margin-bottom: 6px;
}

/* ── CTA band ───────────────────────────────────────────────── */
.cta-band {
  background: linear-gradient(120deg, #ff6a3d, #ff4d6d 60%, #c5417a);
  border-radius: 24px;
  padding: 48px;
  text-align: center;
  position: relative;
  overflow: hidden;
}

/* ── Responsive ─────────────────────────────────────────────── */
@media (max-width: 980px) {
  .hero-grid { grid-template-columns: 1fr !important; }
  .order-grid { grid-template-columns: 1fr !important; }
}
@media (max-width: 768px) {
  nav.links { display: none; }
}
</style>

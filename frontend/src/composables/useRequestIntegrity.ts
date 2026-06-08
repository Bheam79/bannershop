/**
 * Generates a lightweight browser fingerprint for bot-protection (BANNERSH-70).
 *
 * Combines:
 *   - navigator.userAgent
 *   - current timestamp (Date.now)
 *   - a crypto.getRandomValues nonce
 *   - a canvas 2D shape drawn with a nonce-seeded fill colour
 *
 * The concatenated string is hashed with SubtleCrypto SHA-256 and returned as hex.
 *
 * Attach the result as the `X-Request-Integrity` request header on all
 * /api/design-requests/ai and /api/design-requests/{id}/regenerate calls.
 */
export async function generateRequestIntegrity(): Promise<string> {
  const userAgent = window.navigator.userAgent
  const timestamp = Date.now().toString()

  // Cryptographic random nonce
  const nonceBytes = crypto.getRandomValues(new Uint8Array(16))
  const nonceHex = Array.from(nonceBytes)
    .map((b) => b.toString(16).padStart(2, '0'))
    .join('')

  // Canvas 2D nonce: draw a small shape with a nonce-derived fill colour
  let canvasData = 'canvas-unavailable'
  try {
    const canvas = document.createElement('canvas')
    canvas.width = 32
    canvas.height = 32
    const ctx = canvas.getContext('2d')
    if (ctx) {
      ctx.fillStyle = `#${nonceHex.slice(0, 6)}`
      ctx.fillRect(0, 0, 32, 32)
      ctx.fillStyle = 'rgba(255, 106, 61, 0.7)'
      ctx.beginPath()
      ctx.arc(16, 16, 10, 0, Math.PI * 2)
      ctx.fill()
      canvasData = canvas.toDataURL()
    }
  } catch {
    // Canvas may be blocked in some environments — fall back to the literal string
  }

  const message = [userAgent, timestamp, nonceHex, canvasData].join('|')
  const encoded = new TextEncoder().encode(message)
  const hashBuf = await crypto.subtle.digest('SHA-256', encoded)

  return Array.from(new Uint8Array(hashBuf))
    .map((b) => b.toString(16).padStart(2, '0'))
    .join('')
}

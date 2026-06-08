import apiClient from './client'

// ── DTOs ──────────────────────────────────────────────────────────────────────

export interface AiCreditsBalance {
  creditsRemaining: number
  hasUsedFreeGeneration: boolean
}

export interface CreditPackBuyResponse {
  clientSecret: string
  creditCount: number
  priceNok: number
}

export interface CreditPackInfo {
  priceNok: number
  creditCount: number
}

// ── API calls ─────────────────────────────────────────────────────────────────

/** GET /api/ai-credits/me — requires auth. */
export async function getAiCreditsBalance(): Promise<AiCreditsBalance> {
  const { data } = await apiClient.get<AiCreditsBalance>('/ai-credits/me')
  return data
}

/**
 * GET /api/ai-credits/packs — no auth required.
 * Returns the current credit-pack price and credit count so widgets can show
 * the buy CTA without first hitting the paywall 402 flow (BANNERSH-71).
 */
export async function getCreditPackInfo(): Promise<CreditPackInfo> {
  const { data } = await apiClient.get<CreditPackInfo>('/ai-credits/packs')
  return data
}

/**
 * POST /api/ai-credits/packs/buy — requires auth.
 * Creates a Stripe PaymentIntent for the credit pack; returns the client secret,
 * credit count, and price so the frontend can present the card form.
 * Credits are granted by the Stripe webhook (payment_intent.succeeded) after
 * payment completes.
 */
export async function buyCreditPack(): Promise<CreditPackBuyResponse> {
  const { data } = await apiClient.post<CreditPackBuyResponse>('/ai-credits/packs/buy')
  return data
}

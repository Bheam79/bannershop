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
  pack: 'small' | 'large'
}

/** One tier of the credit-pack offering (BANNERSH-137). */
export interface CreditPackTier {
  priceNok: number
  creditCount: number
}

/** Both tiers returned by GET /api/ai-credits/packs (BANNERSH-137). */
export interface CreditPackInfo {
  small: CreditPackTier
  large: CreditPackTier
}

// ── API calls ─────────────────────────────────────────────────────────────────

/** GET /api/ai-credits/me — requires auth. */
export async function getAiCreditsBalance(): Promise<AiCreditsBalance> {
  const { data } = await apiClient.get<AiCreditsBalance>('/ai-credits/me')
  return data
}

/**
 * GET /api/ai-credits/packs — no auth required.
 * Returns both pack tiers (small + large) so widgets can show buy CTAs without
 * going through the paywall 402 flow (BANNERSH-71, BANNERSH-137).
 */
export async function getCreditPackInfo(): Promise<CreditPackInfo> {
  const { data } = await apiClient.get<CreditPackInfo>('/ai-credits/packs')
  return data
}

/**
 * POST /api/ai-credits/packs/buy — requires auth.
 * Creates a Stripe PaymentIntent for the chosen credit-pack tier (BANNERSH-137).
 * Credits are granted by the Stripe webhook (payment_intent.succeeded) after payment.
 */
export async function buyCreditPack(pack: 'small' | 'large' = 'small'): Promise<CreditPackBuyResponse> {
  const { data } = await apiClient.post<CreditPackBuyResponse>('/ai-credits/packs/buy', { pack })
  return data
}

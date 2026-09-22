/** Keep a guest's banner wizard when entering auth via the header as well as its CTA. */
export function authReturnPath(redirect: unknown): string {
  if (typeof redirect === 'string' && redirect.startsWith('/') && !redirect.startsWith('//')) return redirect
  const id = localStorage.getItem('ai_banner_draft_id')
  if (id && /^\d+$/.test(id) && localStorage.getItem(`ai_banner_guest_${id}`)) {
    return `/banner-builder/ai?dr=${id}`
  }
  return '/account'
}

/** Accept national/international numbers with common display separators. */
export function isValidPhone(value: string): boolean {
  const phone = value.trim()
  const digits = phone.replace(/[^0-9]/g, '').length
  return phone.length <= 50 && /^\+?[0-9][0-9 ()-]*[0-9]$/.test(phone) && digits >= 6 && digits <= 15
}

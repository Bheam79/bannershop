import apiClient from './client'

// ─── DTOs (match BannerShop.Api.Models.BannerBuilder) ───────────────────────

export interface UploadResponse {
  designId: number
  previewUrl: string
  widthPx: number
  heightPx: number
  rotationDegrees: number
  selectedHeightCm: number
  computedWidthCm: number
}

export interface RotateResponse {
  previewUrl: string
  rotationDegrees: number
  computedWidthCm: number
  computedHeightCm: number
}

export interface HeightResponse {
  selectedHeightCm: number
  computedWidthCm: number
}

// ─── API calls ──────────────────────────────────────────────────────────────

/** Fetches metadata (dimensions, height) for an existing BannerDesign. */
export async function getBannerDesign(id: number): Promise<UploadResponse> {
  const { data } = await apiClient.get<UploadResponse>(`/banner-builder/${id}`)
  return data
}

/** Uploads a banner image (or PDF). Reports upload progress via callback. */
export async function uploadBannerFile(
  file: File,
  onProgress?: (pct: number) => void,
): Promise<UploadResponse> {
  const form = new FormData()
  form.append('file', file)

  const { data } = await apiClient.post<UploadResponse>(
    '/banner-builder/upload',
    form,
    {
      headers: { 'Content-Type': 'multipart/form-data' },
      onUploadProgress: (evt) => {
        if (!onProgress) return
        if (!evt.total) {
          onProgress(0)
          return
        }
        onProgress(Math.min(100, Math.round((evt.loaded / evt.total) * 100)))
      },
    },
  )
  return data
}

/** Rotates a banner design. `degrees` is a delta (90/180/270 or -90). */
export async function rotateBanner(
  designId: number,
  degrees: number,
): Promise<RotateResponse> {
  const { data } = await apiClient.put<RotateResponse>(
    `/banner-builder/${designId}/rotate`,
    { degrees },
  )
  return data
}

/** Updates the selected height (cm) for a banner design. */
export async function setBannerHeight(
  designId: number,
  heightCm: number,
): Promise<HeightResponse> {
  const { data } = await apiClient.put<HeightResponse>(
    `/banner-builder/${designId}/height`,
    { heightCm },
  )
  return data
}

/**
 * Fetches the preview image as a blob URL (object URL).
 * The GET endpoint requires auth, so we cannot use the URL directly in an &lt;img src&gt;.
 * Caller is responsible for `URL.revokeObjectURL()` when done.
 */
export async function fetchPreviewBlobUrl(designId: number): Promise<string> {
  const { data } = await apiClient.get<Blob>(
    `/banner-builder/${designId}/preview`,
    { responseType: 'blob' },
  )
  return URL.createObjectURL(data)
}

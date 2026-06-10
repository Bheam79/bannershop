/**
 * usePhotoUpload — reusable portrait-photo upload composable.
 *
 * Used by:
 *   - AiBannerBuilderView  (wizard step 2 + edit panel)
 *   - AccountDesignRequestDetailView  (edit panel for AI regeneration)
 */
import { ref, onBeforeUnmount } from 'vue'
import { uploadBannerFile } from '@/api/bannerBuilder'

const PHOTO_MAX_BYTES = 10 * 1024 * 1024 // 10 MB
const PHOTO_ACCEPTED = ['image/jpeg', 'image/png', 'image/webp']

export function usePhotoUpload() {
  /** ID of the BannerDesign row created by the upload endpoint. */
  const uploadedPhotoBannerDesignId = ref<number | null>(null)
  /** Object-URL for preview — revoked on remove / unmount. */
  const photoPreviewUrl = ref<string | null>(null)
  const photoUploading = ref(false)
  const photoUploadProgress = ref(0)
  const photoUploadError = ref<string | null>(null)
  /** Template ref — bind with `ref="photoFileInput"`. */
  const photoFileInput = ref<HTMLInputElement | null>(null)
  const photoDragging = ref(false)

  function openPhotoPicker() {
    if (photoUploading.value) return
    photoFileInput.value?.click()
  }

  function onPhotoFileChange(e: Event) {
    const input = e.target as HTMLInputElement
    const file = input.files?.[0]
    if (file) void handlePhotoFile(file)
    if (input) input.value = ''
  }

  function onPhotoDragOver(e: DragEvent) {
    e.preventDefault()
    photoDragging.value = true
  }

  function onPhotoDragLeave() {
    photoDragging.value = false
  }

  function onPhotoDrop(e: DragEvent) {
    e.preventDefault()
    photoDragging.value = false
    const file = e.dataTransfer?.files?.[0]
    if (file) void handlePhotoFile(file)
  }

  async function handlePhotoFile(file: File) {
    photoUploadError.value = null
    if (!PHOTO_ACCEPTED.includes(file.type)) {
      photoUploadError.value = `Filtypen ${file.type || 'ukjent'} støttes ikke. Bruk JPEG, PNG eller WEBP.`
      return
    }
    if (file.size > PHOTO_MAX_BYTES) {
      photoUploadError.value = `Filen er for stor (${(file.size / 1024 / 1024).toFixed(1)} MB). Maks 10 MB.`
      return
    }

    if (photoPreviewUrl.value) URL.revokeObjectURL(photoPreviewUrl.value)
    photoPreviewUrl.value = URL.createObjectURL(file)

    photoUploading.value = true
    photoUploadProgress.value = 0
    try {
      const resp = await uploadBannerFile(file, (pct) => {
        photoUploadProgress.value = pct
      })
      uploadedPhotoBannerDesignId.value = resp.designId
    } catch (e: unknown) {
      const ex = e as { response?: { status?: number; data?: { error?: string } }; message?: string }
      if (ex.response?.status === 401) {
        photoUploadError.value = 'Du må være innlogget for å laste opp et bilde.'
      } else {
        photoUploadError.value =
          ex.response?.data?.error || ex.message || 'Opplasting feilet. Prøv igjen.'
      }
      if (photoPreviewUrl.value) {
        URL.revokeObjectURL(photoPreviewUrl.value)
        photoPreviewUrl.value = null
      }
      uploadedPhotoBannerDesignId.value = null
    } finally {
      photoUploading.value = false
    }
  }

  function removePhoto() {
    if (photoPreviewUrl.value) URL.revokeObjectURL(photoPreviewUrl.value)
    photoPreviewUrl.value = null
    uploadedPhotoBannerDesignId.value = null
    photoUploadError.value = null
  }

  onBeforeUnmount(() => {
    if (photoPreviewUrl.value) URL.revokeObjectURL(photoPreviewUrl.value)
  })

  return {
    uploadedPhotoBannerDesignId,
    photoPreviewUrl,
    photoUploading,
    photoUploadProgress,
    photoUploadError,
    photoFileInput,
    photoDragging,
    openPhotoPicker,
    onPhotoFileChange,
    onPhotoDragOver,
    onPhotoDragLeave,
    onPhotoDrop,
    handlePhotoFile,
    removePhoto,
  }
}

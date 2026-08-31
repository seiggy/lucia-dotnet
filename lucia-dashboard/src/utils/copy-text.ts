export async function copyTextToClipboard(text: string): Promise<void> {
  if (navigator.clipboard?.writeText) {
    try {
      await navigator.clipboard.writeText(text)
      return
    } catch (error) {
      if (!(error instanceof DOMException)) {
        throw error
      }
    }
  }

  const activeElement = document.activeElement instanceof HTMLElement
    ? document.activeElement
    : null
  const textArea = document.createElement('textarea')
  textArea.value = text
  textArea.readOnly = true
  textArea.style.position = 'fixed'
  textArea.style.opacity = '0'
  textArea.style.pointerEvents = 'none'
  document.body.appendChild(textArea)
  textArea.select()

  const copied = document.execCommand('copy')
  textArea.remove()
  activeElement?.focus()

  if (!copied) {
    throw new Error('Browser denied clipboard access')
  }
}

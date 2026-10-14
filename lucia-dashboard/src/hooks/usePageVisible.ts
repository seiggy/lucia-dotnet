import { useSyncExternalStore } from 'react'

function subscribe(onChange: () => void) {
  document.addEventListener('visibilitychange', onChange)
  return () => document.removeEventListener('visibilitychange', onChange)
}

/**
 * True while this browser tab is visible. Long-lived streams and pollers use it
 * to disconnect in background tabs, because browsers cap connections per host
 * (6 on HTTP/1.1) and every open dashboard tab otherwise holds its streams
 * until the cap starves new requests.
 */
export function usePageVisible(): boolean {
  return useSyncExternalStore(subscribe, () => !document.hidden, () => true)
}

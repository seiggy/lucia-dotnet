export const input =
  'min-h-11 w-full rounded-xl border border-stone bg-basalt px-3 py-2 text-sm text-light placeholder:text-dust input-focus disabled:cursor-not-allowed disabled:opacity-50'

export const primaryButton =
  'inline-flex min-h-11 items-center justify-center gap-2 rounded-xl bg-amber px-5 py-2 text-sm font-semibold text-on-accent transition-colors hover:bg-amber-glow focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-amber/60 disabled:cursor-not-allowed disabled:opacity-40'

export const secondaryButton =
  'inline-flex min-h-11 items-center justify-center gap-2 rounded-xl border border-stone bg-basalt px-4 py-2 text-sm font-medium text-fog transition-colors hover:border-amber/40 hover:text-light focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-amber/60 disabled:cursor-not-allowed disabled:opacity-40'

export const dangerButton =
  'inline-flex min-h-11 items-center justify-center gap-2 rounded-xl border border-rose/30 px-4 py-2 text-sm font-medium text-rose transition-colors hover:bg-rose/8 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-rose/50 disabled:cursor-not-allowed disabled:opacity-40'

export const section = 'space-y-4 border-t border-stone pt-6 first:border-t-0 first:pt-0'

export const sectionTitle = 'font-display text-base font-semibold text-light'

export const sectionHint = 'mt-1 max-w-[70ch] text-sm leading-6 text-fog'

export function errorText(error: unknown, fallback: string) {
  return error instanceof Error ? error.message : fallback
}

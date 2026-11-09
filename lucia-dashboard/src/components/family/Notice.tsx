interface NoticeProps {
  notice: { error: boolean; text: string } | null
}

/** Inline result of the last save or action, announced to assistive technology. */
export default function Notice({ notice }: NoticeProps) {
  if (!notice) return null
  return (
    <p
      role={notice.error ? 'alert' : 'status'}
      className={`rounded-xl border px-4 py-3 text-sm ${notice.error ? 'border-rose/30 bg-rose/8 text-rose' : 'border-sage/30 bg-sage/8 text-sage'}`}
    >
      {notice.text}
    </p>
  )
}

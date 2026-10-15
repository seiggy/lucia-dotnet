import { useId } from 'react'
import type { ReactNode } from 'react'

/** Props to spread onto the form control so it is labelled and described. */
export interface FieldControlProps {
  id: string
  'aria-describedby'?: string
  'aria-invalid'?: true
}

interface FieldProps {
  label: string
  /** Help text shown under the control. */
  hint?: string
  /** Error text; replaces the hint and marks the control invalid. */
  error?: string
  /** Appends "(optional)" to the label. */
  optional?: boolean
  className?: string
  children: (control: FieldControlProps) => ReactNode
}

/**
 * Labelled form field. Associates the label, hint and error with the control through
 * `htmlFor`/`aria-describedby`, which hand-written `<label>` blocks were missing.
 *
 * @example
 * <Field label="Server ID" hint="Lowercase, no spaces">
 *   {control => <input {...control} value={id} onChange={...} className="input-focus ..." />}
 * </Field>
 */
export default function Field({ label, hint, error, optional, className = '', children }: FieldProps) {
  const id = useId()
  const messageId = `${id}-message`
  const message = error ?? hint

  return (
    <div className={className}>
      <label htmlFor={id} className="mb-1.5 block text-xs font-medium text-fog">
        {label}
        {optional && <span className="ml-1 font-normal text-dust">(optional)</span>}
      </label>
      {children({
        id,
        ...(message ? { 'aria-describedby': messageId } : {}),
        ...(error ? { 'aria-invalid': true as const } : {}),
      })}
      {message && (
        <p id={messageId} role={error ? 'alert' : undefined} className={`mt-1.5 text-xs ${error ? 'text-rose' : 'text-dust'}`}>
          {message}
        </p>
      )}
    </div>
  )
}

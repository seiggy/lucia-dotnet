interface ToggleSwitchProps {
  checked: boolean
  onChange: (val: boolean) => void
  disabled?: boolean
  label: string
}

/**
 * Accessible toggle switch component.
 *
 * Renders as a styled on/off switch with proper `role="switch"` and
 * `aria-checked` semantics. The `label` prop provides its accessible name.
 */
export default function ToggleSwitch({
  checked,
  onChange,
  disabled,
  label,
}: ToggleSwitchProps) {
  return (
    <button
      type="button"
      role="switch"
      aria-checked={checked}
      aria-label={label}
      disabled={disabled}
      onClick={() => onChange(!checked)}
      className={`inline-flex h-11 w-14 shrink-0 cursor-pointer items-center justify-center rounded-lg transition-colors duration-200 input-focus focus:ring-2 focus:ring-amber focus:ring-offset-2 focus:ring-offset-void ${
        disabled ? 'cursor-not-allowed opacity-50' : ''
      }`}
    >
      <span
        className={`pointer-events-none relative inline-flex h-6 w-11 rounded-full transition-colors duration-200 ${
          checked ? 'bg-amber-glow' : 'bg-stone'
        }`}
      >
        <span
          className={`inline-block h-5 w-5 transform rounded-full bg-white shadow ring-0 transition duration-200 ${
            checked ? 'translate-x-5' : 'translate-x-0'
          }`}
        />
      </span>
    </button>
  )
}

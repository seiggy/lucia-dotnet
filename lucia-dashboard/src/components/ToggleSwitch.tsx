interface ToggleSwitchProps {
  checked: boolean
  onChange: (val: boolean) => void
  disabled?: boolean
  label?: string
}

/**
 * Accessible toggle switch component.
 *
 * Renders as a styled on/off switch with proper `role="switch"` and
 * `aria-checked` semantics. Use the optional `label` prop to provide
 * an accessible name when there is no visible label.
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
      className={`relative inline-flex h-6 w-11 shrink-0 cursor-pointer rounded-full border-2 border-transparent transition-colors duration-200 input-focus focus:ring-2 focus:ring-amber focus:ring-offset-2 focus:ring-offset-void ${
        checked ? 'bg-amber-glow' : 'bg-stone'
      } ${disabled ? 'opacity-50 cursor-not-allowed' : ''}`}
    >
      <span
        className={`pointer-events-none inline-block h-5 w-5 transform rounded-full bg-white shadow ring-0 transition duration-200 ${
          checked ? 'translate-x-5' : 'translate-x-0'
        }`}
      />
    </button>
  )
}

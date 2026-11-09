import { useEffect, useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Loader2, Save } from 'lucide-react'
import Field from '../Field'
import ToggleSwitch from '../ToggleSwitch'
import Notice from './Notice'
import { getOidcSettings, saveOidcSettings } from '../../family-api'
import { errorText, input, primaryButton, section, sectionHint, sectionTitle, secondaryButton } from './familyStyles'

/** Single sign-on for the family apps. Changes apply after Lucia restarts. */
export default function OidcSettingsPanel() {
  const queryClient = useQueryClient()
  const settingsQuery = useQuery({ queryKey: ['oidc-settings'], queryFn: ({ signal }) => getOidcSettings(signal) })
  const [draft, setDraft] = useState({ enabled: false, authority: '', audience: '', requireHttpsMetadata: true })
  const [busy, setBusy] = useState(false)
  const [notice, setNotice] = useState<{ error: boolean; text: string } | null>(null)
  const settings = settingsQuery.data

  useEffect(() => {
    if (settings) {
      setDraft({
        enabled: settings.enabled,
        authority: settings.authority ?? '',
        audience: settings.audience ?? '',
        requireHttpsMetadata: settings.requireHttpsMetadata,
      })
    }
  }, [settings])

  const missing = draft.enabled && (!draft.authority.trim() || !draft.audience.trim())

  async function save() {
    setBusy(true)
    setNotice(null)
    try {
      const saved = await saveOidcSettings({ ...draft, authority: draft.authority.trim(), audience: draft.audience.trim() })
      queryClient.setQueryData(['oidc-settings'], saved)
      setNotice({ error: false, text: saved.restartRequired ? 'Saved. Restart Lucia to apply these settings.' : 'Saved.' })
    } catch (error) {
      setNotice({ error: true, text: errorText(error, 'Could not save app sign-in settings.') })
    } finally {
      setBusy(false)
    }
  }

  if (settingsQuery.isLoading) {
    return <div role="status" aria-label="Loading app sign-in settings" className="h-64 animate-pulse rounded-xl bg-basalt/70" />
  }
  if (settingsQuery.isError) {
    return (
      <div role="alert" className="rounded-xl border border-rose/30 bg-rose/8 p-4 text-sm text-rose">
        <p>{settingsQuery.error.message}</p>
        <button type="button" className={`mt-3 ${secondaryButton}`} onClick={() => void settingsQuery.refetch()}>Try again</button>
      </div>
    )
  }

  return (
    <div className="max-w-3xl space-y-6">
      <div className={section}>
        <div className="flex items-start justify-between gap-4">
          <div>
            <h2 className={sectionTitle}>Single sign-on for family apps</h2>
            <p className={sectionHint}>
              Let family members sign in to the Lucia apps with an OpenID Connect provider such as Microsoft Entra ID, Google, or Authentik.
              Pairing with a QR code works with or without it. A token is accepted only for an account you linked to a member.
            </p>
          </div>
          <ToggleSwitch label="Enable single sign-on" checked={draft.enabled} disabled={busy}
            onChange={enabled => { setDraft({ ...draft, enabled }); setNotice(null) }} />
        </div>
        <p className="text-sm text-fog">
          Status: <span className={settings?.active ? 'text-sage' : 'text-dust'}>{settings?.active ? 'Active' : 'Not active'}</span>
          {settings?.restartRequired && <span className="ml-2 text-amber">Saved changes apply after a restart.</span>}
        </p>
      </div>

      <fieldset disabled={!draft.enabled || busy} className={`${section} disabled:opacity-60`}>
        <legend className="sr-only">Provider</legend>
        <Field label="Authority" hint="Your provider's issuer URL. Lucia reads its discovery document from here."
          error={draft.enabled && !draft.authority.trim() ? 'Required when sign-on is enabled.' : undefined}>
          {control => (
            <input {...control} className={input} type="url" value={draft.authority} placeholder="https://login.microsoftonline.com/<tenant>/v2.0"
              onChange={event => { setDraft({ ...draft, authority: event.target.value }); setNotice(null) }} />
          )}
        </Field>
        <Field label="Audience" hint="The audience (aud) the apps request tokens for, usually the API's client ID or URI."
          error={draft.enabled && !draft.audience.trim() ? 'Required when sign-on is enabled.' : undefined}>
          {control => (
            <input {...control} className={input} value={draft.audience} placeholder="api://lucia"
              onChange={event => { setDraft({ ...draft, audience: event.target.value }); setNotice(null) }} />
          )}
        </Field>
        <div className="flex items-center justify-between gap-4">
          <div>
            <p className="text-sm font-medium text-light">Require HTTPS for the provider</p>
            <p className="text-xs leading-5 text-fog">Turn off only for a provider on your local network without TLS.</p>
          </div>
          <ToggleSwitch label="Require HTTPS for the provider" checked={draft.requireHttpsMetadata}
            onChange={requireHttpsMetadata => { setDraft({ ...draft, requireHttpsMetadata }); setNotice(null) }} />
        </div>
      </fieldset>

      <Notice notice={notice} />

      <div className="border-t border-stone pt-6">
        <button type="button" className={primaryButton} disabled={busy || missing} onClick={() => void save()}>
          {busy ? <Loader2 className="h-4 w-4 animate-spin" aria-hidden="true" /> : <Save className="h-4 w-4" aria-hidden="true" />}
          Save sign-in settings
        </button>
      </div>
    </div>
  )
}

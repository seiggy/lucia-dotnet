import { useEffect, useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import QRCode from 'qrcode'
import { Check, Copy, KeyRound, Link2, Loader2, Mic, QrCode, ShieldCheck, Trash2, X } from 'lucide-react'
import Field from '../Field'
import Notice from './Notice'
import { useConfirm } from '../../hooks/useConfirm'
import { listSpeakerProfiles } from '../../api'
import { cancelPairing, createPairing, linkIdentity, listIdentities, listPairings, unlinkIdentity } from '../../family-api'
import type { DevicePairing, FamilyMember, MemberIdentityKind, MemberIdentityLink, PairingCode } from '../../family-api'
import { errorText, input, primaryButton, secondaryButton, section, sectionHint, sectionTitle } from './familyStyles'

const KIND_LABELS: Record<MemberIdentityKind, string> = {
  ApiKey: 'Device key',
  SpeakerProfile: 'Voice profile',
  Oidc: 'Single sign-on',
}

const KIND_ICONS: Record<MemberIdentityKind, typeof KeyRound> = {
  ApiKey: KeyRound,
  SpeakerProfile: Mic,
  Oidc: ShieldCheck,
}

/** Pair a member's devices with a QR code and manage the identities that sign in as them. */
export default function MemberDevices({ member }: { member: FamilyMember }) {
  const confirmAction = useConfirm()
  const queryClient = useQueryClient()
  const [pairing, setPairing] = useState<PairingCode | null>(null)
  const [busy, setBusy] = useState(false)
  const [notice, setNotice] = useState<{ error: boolean; text: string } | null>(null)
  const [voiceProfile, setVoiceProfile] = useState('')
  const [sso, setSso] = useState({ issuer: '', subject: '', label: '' })

  const identitiesQuery = useQuery({
    queryKey: ['family-identities', member.id],
    queryFn: ({ signal }) => listIdentities(member.id, signal),
  })
  const pairingsQuery = useQuery({
    queryKey: ['family-pairings', member.id],
    queryFn: ({ signal }) => listPairings(member.id, signal),
    refetchInterval: pairing ? 3000 : false,
  })
  const speakersQuery = useQuery({ queryKey: ['memory-speaker-profiles'], queryFn: listSpeakerProfiles, retry: false })

  const identities = identitiesQuery.data ?? []
  const redeemed = pairing ? pairingsQuery.data?.find(candidate => candidate.id === pairing.id && candidate.redeemedAt) : undefined
  const linkedVoiceIds = new Set(identities.filter(link => link.kind === 'SpeakerProfile').map(link => link.subject))
  const voiceOptions = (speakersQuery.data ?? []).filter(profile => !profile.isProvisional && !linkedVoiceIds.has(profile.id))

  useEffect(() => {
    if (redeemed) void queryClient.invalidateQueries({ queryKey: ['family-identities', member.id] })
  }, [redeemed, member.id, queryClient])

  async function run(action: () => Promise<void>, success: string, fallback: string) {
    setBusy(true)
    setNotice(null)
    try {
      await action()
      setNotice({ error: false, text: success })
    } catch (error) {
      setNotice({ error: true, text: errorText(error, fallback) })
    } finally {
      setBusy(false)
    }
  }

  const refresh = () => Promise.all([
    queryClient.invalidateQueries({ queryKey: ['family-identities', member.id] }),
    queryClient.invalidateQueries({ queryKey: ['family-pairings', member.id] }),
  ])

  async function startPairing() {
    setBusy(true)
    setNotice(null)
    try {
      setPairing(await createPairing(member.id))
      await refresh()
    } catch (error) {
      setNotice({ error: true, text: errorText(error, 'Could not create a pairing code.') })
    } finally {
      setBusy(false)
    }
  }

  async function removeIdentity(link: MemberIdentityLink) {
    const name = link.label || KIND_LABELS[link.kind]
    if (!(await confirmAction({
      title: `Remove ${name}?`,
      message: link.kind === 'ApiKey'
        ? `The device stops working for ${member.displayName} immediately. Pair it again to restore access.`
        : `${member.displayName} will no longer be recognized through this ${KIND_LABELS[link.kind].toLowerCase()}.`,
      confirmLabel: 'Remove',
    }))) return
    await run(async () => { await unlinkIdentity(member.id, link); await refresh() }, `Removed ${name}.`, 'Could not remove this sign-in method.')
  }

  return (
    <div className="space-y-6">
      <div className={section}>
        <div className="flex flex-wrap items-start justify-between gap-3">
          <div>
            <h3 className={sectionTitle}>Pair a phone or tablet</h3>
            <p className={sectionHint}>
              Open the Lucia app on {member.displayName}'s device and scan the code. Each code works once and gives that device its own key, which you can remove at any time.
            </p>
          </div>
          {!pairing && (
            <button type="button" className={primaryButton} disabled={busy || !member.isActive} onClick={() => void startPairing()}>
              {busy ? <Loader2 className="h-4 w-4 animate-spin" aria-hidden="true" /> : <QrCode className="h-4 w-4" aria-hidden="true" />}
              Create pairing code
            </button>
          )}
        </div>
        {!member.isActive && <p className="text-sm text-amber">Reactivate {member.displayName} on the Profile tab to pair a device.</p>}
        {pairing && (
          <PairingCard
            pairing={pairing}
            redeemed={redeemed}
            onDone={() => setPairing(null)}
            onCancel={() => void run(async () => {
              await cancelPairing(pairing.id)
              setPairing(null)
              await refresh()
            }, 'Pairing code cancelled.', 'Could not cancel this pairing code.')}
          />
        )}
      </div>

      <div className={section}>
        <h3 className={sectionTitle}>Sign-in methods</h3>
        <p className={sectionHint}>Everything that identifies {member.displayName}: paired devices, a recognized voice, and single sign-on accounts.</p>
        {identitiesQuery.isLoading ? (
          <div role="status" aria-label="Loading sign-in methods" className="h-24 animate-pulse rounded-xl bg-basalt/70" />
        ) : identitiesQuery.isError ? (
          <p role="alert" className="text-sm text-rose">{identitiesQuery.error.message}</p>
        ) : identities.length === 0 ? (
          <p className="rounded-xl border border-stone p-4 text-sm text-fog">
            Nothing yet. Pair a device above, or link a voice profile below so Lucia knows when {member.displayName} is speaking.
          </p>
        ) : (
          <ul className="divide-y divide-stone rounded-xl border border-stone">
            {identities.map(link => {
              const Icon = KIND_ICONS[link.kind]
              return (
                <li key={`${link.kind}:${link.issuer}:${link.subject}`} className="flex items-center gap-3 px-4 py-2">
                  <Icon className="h-4 w-4 shrink-0 text-dust" aria-hidden="true" />
                  <div className="min-w-0 flex-1">
                    <p className="truncate text-sm text-light">{link.label || KIND_LABELS[link.kind]}</p>
                    <p className="truncate text-xs text-dust">
                      {KIND_LABELS[link.kind]}{link.kind === 'Oidc' ? ` · ${link.issuer}` : ''} · added {new Date(link.createdAt).toLocaleDateString()}
                    </p>
                  </div>
                  <button type="button" aria-label={`Remove ${link.label || KIND_LABELS[link.kind]}`} disabled={busy}
                    onClick={() => void removeIdentity(link)}
                    className="flex h-11 w-11 items-center justify-center rounded-lg text-dust hover:bg-rose/8 hover:text-rose focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-amber/60 disabled:opacity-40">
                    <Trash2 className="h-4 w-4" aria-hidden="true" />
                  </button>
                </li>
              )
            })}
          </ul>
        )}
      </div>

      {!speakersQuery.isError && (
        <div className={section}>
          <h3 className={sectionTitle}>Link a voice profile</h3>
          <p className={sectionHint}>
            When Lucia recognizes this voice, requests from shared speakers follow {member.displayName}'s permissions and model settings.
          </p>
          {voiceOptions.length === 0 ? (
            <p className="text-sm text-dust">{speakersQuery.isLoading ? 'Loading voice profiles…' : 'No unlinked enrolled voice profiles. Enroll one on the Voice Platform page.'}</p>
          ) : (
            <div className="flex flex-col gap-2 sm:flex-row">
              <label className="sr-only" htmlFor={`voice-${member.id}`}>Voice profile</label>
              <select id={`voice-${member.id}`} className={`${input} sm:max-w-sm`} value={voiceProfile} disabled={busy}
                onChange={event => setVoiceProfile(event.target.value)}>
                <option value="">Choose an enrolled voice</option>
                {voiceOptions.map(profile => <option key={profile.id} value={profile.id}>{profile.name}</option>)}
              </select>
              <button type="button" className={secondaryButton} disabled={busy || !voiceProfile} onClick={() => void run(async () => {
                const profile = voiceOptions.find(candidate => candidate.id === voiceProfile)
                await linkIdentity(member.id, { kind: 'SpeakerProfile', subject: voiceProfile, label: profile?.name })
                setVoiceProfile('')
                await refresh()
              }, 'Voice profile linked.', 'Could not link this voice profile.')}>
                <Link2 className="h-4 w-4" aria-hidden="true" /> Link voice
              </button>
            </div>
          )}
        </div>
      )}

      <div className={section}>
        <h3 className={sectionTitle}>Link a single sign-on account</h3>
        <p className={sectionHint}>
          Used when app sign-in is set up on the App sign-in tab. Enter the token's issuer and the account's subject (<code className="font-mono text-xs text-cloud">sub</code>) claim.
        </p>
        <div className="grid gap-3 sm:grid-cols-3">
          <Field label="Issuer">
            {control => <input {...control} className={input} value={sso.issuer} disabled={busy} placeholder="https://login.example.com/"
              onChange={event => setSso({ ...sso, issuer: event.target.value })} />}
          </Field>
          <Field label="Subject">
            {control => <input {...control} className={`${input} font-mono`} value={sso.subject} disabled={busy}
              onChange={event => setSso({ ...sso, subject: event.target.value })} />}
          </Field>
          <Field label="Label" optional>
            {control => <input {...control} className={input} value={sso.label} disabled={busy} placeholder={`${member.displayName}'s account`}
              onChange={event => setSso({ ...sso, label: event.target.value })} />}
          </Field>
        </div>
        <button type="button" className={secondaryButton} disabled={busy || !sso.issuer.trim() || !sso.subject.trim()}
          onClick={() => void run(async () => {
            await linkIdentity(member.id, { kind: 'Oidc', issuer: sso.issuer.trim(), subject: sso.subject.trim(), label: sso.label.trim() || undefined })
            setSso({ issuer: '', subject: '', label: '' })
            await refresh()
          }, 'Single sign-on account linked.', 'Could not link this account.')}>
          <Link2 className="h-4 w-4" aria-hidden="true" /> Link account
        </button>
      </div>

      <Notice notice={notice} />
    </div>
  )
}

interface PairingCardProps {
  pairing: PairingCode
  redeemed?: DevicePairing
  onDone: () => void
  onCancel: () => void
}

function PairingCard({ pairing, redeemed, onDone, onCancel }: PairingCardProps) {
  const [qr, setQr] = useState<string | null>(null)
  const [now, setNow] = useState(() => Date.now())
  const [copied, setCopied] = useState(false)
  const remaining = Math.max(0, new Date(pairing.expiresAt).getTime() - now)
  const expired = remaining === 0

  useEffect(() => {
    let cancelled = false
    QRCode.toDataURL(pairing.pairingUri, { errorCorrectionLevel: 'M', margin: 2, width: 240, color: { dark: '#111111', light: '#ffffff' } })
      .then(url => { if (!cancelled) setQr(url) })
      .catch(() => { if (!cancelled) setQr(null) })
    return () => { cancelled = true }
  }, [pairing.pairingUri])

  useEffect(() => {
    if (redeemed || expired) return
    const timer = window.setInterval(() => setNow(Date.now()), 1000)
    return () => window.clearInterval(timer)
  }, [redeemed, expired])

  if (redeemed) {
    return (
      <div role="status" className="flex flex-wrap items-center justify-between gap-3 rounded-xl border border-sage/30 bg-sage/8 p-4">
        <p className="flex items-center gap-2 text-sm text-sage">
          <Check className="h-4 w-4" aria-hidden="true" /> Paired {redeemed.deviceName ? `with ${redeemed.deviceName}` : 'a device'}.
        </p>
        <button type="button" className={secondaryButton} onClick={onDone}>Done</button>
      </div>
    )
  }

  const minutes = Math.floor(remaining / 60_000)
  const seconds = Math.floor((remaining % 60_000) / 1000).toString().padStart(2, '0')

  return (
    <div className="flex flex-col gap-5 rounded-xl border border-stone bg-basalt/50 p-4 sm:flex-row sm:items-center">
      <div className={`shrink-0 self-center rounded-lg bg-white p-2 ${expired ? 'opacity-30' : ''}`}>
        {qr
          ? <img src={qr} alt="Pairing QR code" width={200} height={200} className="h-[200px] w-[200px]" />
          : <div className="h-[200px] w-[200px] animate-pulse rounded bg-stone/30" />}
      </div>
      <div className="min-w-0 flex-1 space-y-3">
        <p aria-live="polite" className={`text-sm font-medium ${expired ? 'text-rose' : 'text-light'}`}>
          {expired ? 'This code expired. Create a new one.' : `Waiting for the app · expires in ${minutes}:${seconds}`}
        </p>
        <div>
          <p className="text-xs text-dust">If the camera can't scan it, enter this server and code in the app:</p>
          <p className="mt-1 break-all text-sm text-cloud">{pairing.serverUrl}</p>
          <div className="mt-1 flex items-center gap-2">
            <code className="min-w-0 break-all rounded-lg bg-obsidian px-2 py-1 font-mono text-sm text-light">{pairing.code}</code>
            <button type="button" aria-label="Copy pairing code" className="flex h-11 w-11 shrink-0 items-center justify-center rounded-lg text-dust hover:bg-stone/40 hover:text-light focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-amber/60"
              onClick={() => void navigator.clipboard.writeText(pairing.code).then(() => setCopied(true))}>
              {copied ? <Check className="h-4 w-4 text-sage" aria-hidden="true" /> : <Copy className="h-4 w-4" aria-hidden="true" />}
            </button>
          </div>
        </div>
        <button type="button" className={secondaryButton} onClick={expired ? onDone : onCancel}>
          <X className="h-4 w-4" aria-hidden="true" /> {expired ? 'Close' : 'Cancel code'}
        </button>
      </div>
    </div>
  )
}

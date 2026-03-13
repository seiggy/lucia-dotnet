import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import type { ReactNode } from 'react'
import { Check, Cpu, Download, Globe, Loader2, Mic, Radio, Trash2, User, Volume2 } from 'lucide-react'
import type { AsrModel, WyomingStatus } from '../api'
import {
  activateModel,
  deleteModel,
  deleteSpeakerProfile,
  deleteWakeWord,
  downloadModel,
  fetchBackgroundTask,
  fetchActiveModel,
  fetchAvailableModels,
  fetchInstalledModels,
  fetchWyomingStatus,
  getOnboardingStatus,
  listSpeakerProfiles,
  listWakeWords,
  startOnboarding,
  uploadVoiceSample,
} from '../api'

type Tab = 'status' | 'models' | 'profiles' | 'wake-words'
type Notice = { type: 'success' | 'error' | 'info'; message: string } | null

type SpeakerProfileSummary = {
  id: string
  name: string
  isProvisional: boolean
  isAuthorized: boolean
  interactionCount: number
  enrolledAt: string
  lastSeenAt: string | null
}

type WakeWordSummary = {
  id: string
  phrase: string
  userId?: string | null
  boostScore?: number
  threshold?: number
  isCalibrated?: boolean
}

type StartOnboardingResponse = {
  id: string
  wakeWordId?: string | null
  firstPrompt?: string | null
  totalPrompts: number
}

type OnboardingStatusResponse = {
  status: string
  currentPromptIndex: number
  nextPrompt?: string | null
}

type OnboardingStepResult = {
  status: 'NextPrompt' | 'Retry' | 'Complete' | 'Error'
  message: string
  nextPrompt?: string | null
  completedProfile?: { id: string; name: string } | null
}

const tabs: { id: Tab; label: string; icon: typeof Mic }[] = [
  { id: 'status', label: 'Status', icon: Mic },
  { id: 'models', label: 'Models', icon: Cpu },
  { id: 'profiles', label: 'Profiles', icon: User },
  { id: 'wake-words', label: 'Wake Words', icon: Volume2 },
]

const architectureLabels: Record<string, string> = {
  ZipformerTransducer: 'Zipformer Transducer',
  ZipformerCtc: 'Zipformer CTC',
  Paraformer: 'Paraformer',
  Conformer: 'Conformer',
  NemoFastConformer: 'NeMo FastConformer',
  NemoParakeet: 'NeMo Parakeet',
  NemoNemotron: 'NeMo Nemotron',
  NemoCanary: 'NeMo Canary',
  Whisper: 'Whisper',
  SenseVoice: 'SenseVoice',
  Lstm: 'LSTM',
  Telespeech: 'TeleSpeech',
  Unknown: 'Unknown',
}

function archLabel(value: string | number): string {
  if (typeof value === 'number') {
    // Fallback for integer enum values from older API responses
    const names = Object.keys(architectureLabels)
    return architectureLabels[names[value] ?? ''] ?? `Type ${value}`
  }
  return architectureLabels[value] ?? String(value)
}

const buttonPrimary = 'inline-flex items-center justify-center gap-2 rounded-xl bg-amber px-4 py-2 text-sm font-semibold text-void transition-colors hover:bg-amber-glow disabled:cursor-not-allowed disabled:opacity-50'
const buttonSecondary = 'inline-flex items-center justify-center gap-2 rounded-xl border border-stone/50 bg-basalt px-4 py-2 text-sm font-medium text-fog transition-colors hover:border-amber/30 hover:text-light disabled:cursor-not-allowed disabled:opacity-50'
const inputClass = 'w-full rounded-xl border border-stone/50 bg-basalt px-4 py-2.5 text-sm text-light placeholder:text-dust/70 focus:border-amber/40 focus:outline-none'
const TARGET_SAMPLE_RATE = 16_000
const TRAINING_SAMPLE_COUNT = 5

export default function VoicePlatformPage() {
  const [activeTab, setActiveTab] = useState<Tab>('status')
  const [notice, setNotice] = useState<Notice>(null)

  const [status, setStatus] = useState<WyomingStatus | null>(null)
  const [statusLoading, setStatusLoading] = useState(true)

  const [models, setModels] = useState<AsrModel[]>([])
  const [installedIds, setInstalledIds] = useState<Set<string>>(new Set())
  const [activeModelId, setActiveModelId] = useState('')
  const [modelsLoading, setModelsLoading] = useState(true)
  const [busyModelIds, setBusyModelIds] = useState<Set<string>>(new Set())
  const [languageFilter, setLanguageFilter] = useState('all')
  const [architectureFilter, setArchitectureFilter] = useState('all')
  const [streamingOnly, setStreamingOnly] = useState(false)

  const [profiles, setProfiles] = useState<SpeakerProfileSummary[]>([])
  const [wakeWords, setWakeWords] = useState<WakeWordSummary[]>([])
  const [directoryLoading, setDirectoryLoading] = useState(true)
  const [showEnrollment, setShowEnrollment] = useState(false)
  const [speakerName, setSpeakerName] = useState('')
  const [wakePhrase, setWakePhrase] = useState('')
  const [sessionId, setSessionId] = useState<string | null>(null)
  const [prompt, setPrompt] = useState('Introduce yourself and say your name clearly.')
  const [trainingCount, setTrainingCount] = useState(0)
  const [enrollmentBusy, setEnrollmentBusy] = useState(false)
  const [enrollmentError, setEnrollmentError] = useState<string | null>(null)
  const [statusNote, setStatusNote] = useState('Capture five short samples so Lucia can learn this speaker.')
  const [meterLevel, setMeterLevel] = useState(0)
  const [isRecording, setIsRecording] = useState(false)

  const [showWakeWordForm, setShowWakeWordForm] = useState(false)
  const [wakeWordDraft, setWakeWordDraft] = useState('')
  const [wakeWordOwner, setWakeWordOwner] = useState('')

  const streamRef = useRef<MediaStream | null>(null)
  const audioContextRef = useRef<AudioContext | null>(null)
  const analyserRef = useRef<AnalyserNode | null>(null)
  const sourceRef = useRef<MediaStreamAudioSourceNode | null>(null)
  const processorRef = useRef<ScriptProcessorNode | null>(null)
  const muteGainRef = useRef<GainNode | null>(null)
  const animationFrameRef = useRef<number | null>(null)
  const pcmChunksRef = useRef<Float32Array[]>([])
  const sampleRateRef = useRef(TARGET_SAMPLE_RATE)
  const modelDownloadPollRef = useRef<Record<string, number>>({})

  const markModelBusy = useCallback((id: string, busy: boolean) => {
    setBusyModelIds(current => {
      const next = new Set(current)
      if (busy) next.add(id)
      else next.delete(id)
      return next
    })
  }, [])

  const loadStatus = useCallback(async () => {
    setStatusLoading(true)
    try {
      setStatus(await fetchWyomingStatus())
    } catch {
      setStatus(null)
    } finally {
      setStatusLoading(false)
    }
  }, [])

  const loadModels = useCallback(async () => {
    setModelsLoading(true)
    try {
      const [catalog, installed, active] = await Promise.all([
        fetchAvailableModels(),
        fetchInstalledModels(),
        fetchActiveModel(),
      ])
      setModels(catalog)
      setInstalledIds(new Set(installed.map(model => model.id)))
      setActiveModelId(active.activeModel)
    } catch (error) {
      setNotice({
        type: 'error',
        message: error instanceof Error ? error.message : 'Failed to load Wyoming models.',
      })
    } finally {
      setModelsLoading(false)
    }
  }, [])

  const loadDirectories = useCallback(async () => {
    setDirectoryLoading(true)
    try {
      const [nextProfiles, nextWakeWords] = await Promise.all([
        listSpeakerProfiles() as Promise<SpeakerProfileSummary[]>,
        listWakeWords() as Promise<WakeWordSummary[]>,
      ])
      setProfiles(nextProfiles)
      setWakeWords(nextWakeWords)
    } catch (error) {
      setNotice({
        type: 'error',
        message: error instanceof Error ? error.message : 'Failed to load profiles and wake words.',
      })
    } finally {
      setDirectoryLoading(false)
    }
  }, [])

  const clearModelDownloadPoll = useCallback((modelId: string) => {
    const timeoutId = modelDownloadPollRef.current[modelId]
    if (timeoutId !== undefined) {
      window.clearTimeout(timeoutId)
      delete modelDownloadPollRef.current[modelId]
    }
  }, [])

  const watchModelDownload = useCallback((modelId: string, taskId: string) => {
    clearModelDownloadPoll(modelId)

    const poll = async () => {
      try {
        const task = await fetchBackgroundTask(taskId)
        if (!task || task.status === 'Queued' || task.status === 'Running') {
          modelDownloadPollRef.current[modelId] = window.setTimeout(() => {
            void poll()
          }, 2_000)
          return
        }

        clearModelDownloadPoll(modelId)
        await Promise.all([loadModels(), loadStatus()])

        if (task.status === 'Complete') {
          setNotice({ type: 'success', message: 'Model downloaded successfully.' })
        } else if (task.status === 'Failed') {
          setNotice({ type: 'error', message: task.error || 'Model download failed.' })
        } else {
          setNotice({ type: 'info', message: 'Model download was cancelled.' })
        }

        markModelBusy(modelId, false)
      } catch {
        modelDownloadPollRef.current[modelId] = window.setTimeout(() => {
          void poll()
        }, 4_000)
      }
    }

    void poll()
  }, [clearModelDownloadPoll, loadModels, loadStatus, markModelBusy])

  const teardownAudio = useCallback(() => {
    if (animationFrameRef.current !== null) cancelAnimationFrame(animationFrameRef.current)
    processorRef.current?.disconnect()
    muteGainRef.current?.disconnect()
    sourceRef.current?.disconnect()
    streamRef.current?.getTracks().forEach(track => track.stop())
    void audioContextRef.current?.close()

    animationFrameRef.current = null
    processorRef.current = null
    muteGainRef.current = null
    sourceRef.current = null
    analyserRef.current = null
    audioContextRef.current = null
    streamRef.current = null
    pcmChunksRef.current = []
    setIsRecording(false)
    setMeterLevel(0)
  }, [])

  useEffect(() => {
    let ignore = false

    async function hydrate() {
      await Promise.all([loadStatus(), loadModels(), loadDirectories()])
      if (ignore) return
    }

    void hydrate()
    return () => {
      ignore = true
      Object.values(modelDownloadPollRef.current).forEach(timeoutId => window.clearTimeout(timeoutId))
      modelDownloadPollRef.current = {}
      teardownAudio()
    }
  }, [loadDirectories, loadModels, loadStatus, teardownAudio])

  const languageOptions = useMemo(
    () => ['all', ...new Set(models.flatMap(model => model.languages ?? []).filter(Boolean).sort((a, b) => String(a).localeCompare(String(b))))],
    [models],
  )

  const architectureOptions = useMemo(
    () => ['all', ...new Set(models.map(model => model.architecture).filter(Boolean).sort((a, b) => String(a).localeCompare(String(b))))],
    [models],
  )

  const filteredModels = useMemo(() => {
    const visible = models
      .filter(model => languageFilter === 'all' || model.languages.includes(languageFilter))
      .filter(model => architectureFilter === 'all' || model.architecture === architectureFilter)
      .filter(model => !streamingOnly || model.isStreaming)
      .map(model => ({
        ...model,
        isInstalled: installedIds.has(model.id),
        isActive: activeModelId === model.id,
      }))
      .sort((a, b) => a.name.localeCompare(b.name))

    return {
      installed: visible.filter(model => model.isInstalled).sort((a, b) => Number(b.isActive) - Number(a.isActive) || a.name.localeCompare(b.name)),
      catalog: visible.filter(model => !model.isInstalled),
    }
  }, [activeModelId, architectureFilter, installedIds, languageFilter, models, streamingOnly])

  const startMeter = useCallback(() => {
    const analyser = analyserRef.current
    if (!analyser) return
    const data = new Uint8Array(analyser.fftSize)

    const tick = () => {
      analyser.getByteTimeDomainData(data)
      let sum = 0
      for (let index = 0; index < data.length; index++) {
        const normalized = (data[index] - 128) / 128
        sum += normalized * normalized
      }
      setMeterLevel(Math.min(1, Math.sqrt(sum / data.length) * 3.5))
      animationFrameRef.current = requestAnimationFrame(tick)
    }

    if (animationFrameRef.current !== null) cancelAnimationFrame(animationFrameRef.current)
    tick()
  }, [])

  const ensureMicrophone = useCallback(async () => {
    if (!navigator.mediaDevices?.getUserMedia) throw new Error('This browser does not support microphone access.')

    if (streamRef.current && audioContextRef.current && sourceRef.current && analyserRef.current) {
      if (audioContextRef.current.state === 'suspended') await audioContextRef.current.resume()
      return
    }

    const stream = await navigator.mediaDevices.getUserMedia({
      audio: { channelCount: 1, echoCancellation: true, noiseSuppression: true, autoGainControl: true },
    })

    const audioContext = new AudioContext()
    await audioContext.resume()

    const source = audioContext.createMediaStreamSource(stream)
    const analyser = audioContext.createAnalyser()
    analyser.fftSize = 512
    source.connect(analyser)

    streamRef.current = stream
    audioContextRef.current = audioContext
    sourceRef.current = source
    analyserRef.current = analyser
    sampleRateRef.current = audioContext.sampleRate
    startMeter()
  }, [startMeter])

  const beginRecording = useCallback(async () => {
    setEnrollmentError(null)
    try {
      await ensureMicrophone()
      const audioContext = audioContextRef.current
      const source = sourceRef.current
      if (!audioContext || !source) throw new Error('Microphone is not ready yet.')

      pcmChunksRef.current = []
      const processor = audioContext.createScriptProcessor(4096, 1, 1)
      const muteGain = audioContext.createGain()
      muteGain.gain.value = 0
      processor.onaudioprocess = event => {
        const samples = event.inputBuffer.getChannelData(0)
        pcmChunksRef.current.push(new Float32Array(samples))
      }

      source.connect(processor)
      processor.connect(muteGain)
      muteGain.connect(audioContext.destination)

      processorRef.current = processor
      muteGainRef.current = muteGain
      setIsRecording(true)
      setStatusNote('Recording sample… speak naturally and stop when you finish the prompt.')
    } catch (error) {
      setEnrollmentError(error instanceof Error ? error.message : 'Failed to access the microphone.')
    }
  }, [ensureMicrophone])

  const finishRecording = useCallback(async () => {
    if (!isRecording) throw new Error('Recording has not started yet.')
    processorRef.current?.disconnect()
    muteGainRef.current?.disconnect()
    processorRef.current = null
    muteGainRef.current = null
    setIsRecording(false)

    const merged = flattenSamples(pcmChunksRef.current)
    if (merged.length < TARGET_SAMPLE_RATE) throw new Error('Recording was too short. Please try again.')
    const resampled = downsampleBuffer(merged, sampleRateRef.current, TARGET_SAMPLE_RATE)
    return encodeWav(resampled, TARGET_SAMPLE_RATE)
  }, [isRecording])

  async function handleModelDownload(modelId: string) {
    markModelBusy(modelId, true)
    try {
      const { taskId } = await downloadModel(modelId)
      setNotice({ type: 'info', message: 'Model download started. Track progress from the task tracker.' })
      watchModelDownload(modelId, taskId)
    } catch (error) {
      clearModelDownloadPoll(modelId)
      markModelBusy(modelId, false)
      console.error('Failed to start download:', error)
      setNotice({ type: 'error', message: error instanceof Error ? error.message : 'Failed to start model download.' })
    }
  }

  async function handleModelActivate(modelId: string) {
    markModelBusy(modelId, true)
    try {
      await activateModel(modelId)
      await Promise.all([loadModels(), loadStatus()])
      setNotice({ type: 'success', message: 'Active STT model updated.' })
    } catch (error) {
      setNotice({ type: 'error', message: error instanceof Error ? error.message : 'Failed to activate model.' })
    } finally {
      markModelBusy(modelId, false)
    }
  }

  async function handleModelDelete(modelId: string) {
    if (!confirm('Delete this local model?')) return
    markModelBusy(modelId, true)
    try {
      await deleteModel(modelId)
      await Promise.all([loadModels(), loadStatus()])
      setNotice({ type: 'success', message: 'Model deleted.' })
    } catch (error) {
      setNotice({ type: 'error', message: error instanceof Error ? error.message : 'Failed to delete model.' })
    } finally {
      markModelBusy(modelId, false)
    }
  }

  async function handleDeleteSpeaker(id: string) {
    if (!confirm('Delete this speaker profile?')) return
    try {
      await deleteSpeakerProfile(id)
      await loadDirectories()
      setNotice({ type: 'success', message: 'Speaker profile deleted.' })
    } catch (error) {
      setNotice({ type: 'error', message: error instanceof Error ? error.message : 'Failed to delete speaker profile.' })
    }
  }

  async function handleDeleteWakeWord(id: string) {
    if (!confirm('Delete this wake word?')) return
    try {
      await deleteWakeWord(id)
      await loadDirectories()
      setNotice({ type: 'success', message: 'Wake word deleted.' })
    } catch (error) {
      setNotice({ type: 'error', message: error instanceof Error ? error.message : 'Failed to delete wake word.' })
    }
  }

  async function handleStartEnrollment() {
    setEnrollmentBusy(true)
    setEnrollmentError(null)
    try {
      const result = await startOnboarding(speakerName.trim(), wakePhrase.trim() || undefined) as StartOnboardingResponse
      setSessionId(result.id)
      setPrompt(result.firstPrompt || 'Please say the displayed prompt clearly.')
      setTrainingCount(0)
      setStatusNote(`Session ready. Capture ${result.totalPrompts} prompts to finish enrollment.`)
    } catch (error) {
      setEnrollmentError(error instanceof Error ? error.message : 'Failed to start enrollment.')
    } finally {
      setEnrollmentBusy(false)
    }
  }

  async function handleSubmitSample() {
    if (!sessionId) return
    setEnrollmentBusy(true)
    setEnrollmentError(null)
    try {
      const wav = await finishRecording()
      const result = await uploadVoiceSample(sessionId, wav) as OnboardingStepResult
      if (result.status === 'Retry') {
        setStatusNote(result.message || 'Please retry the same prompt.')
        return
      }

      const nextStatus = await getOnboardingStatus(sessionId) as OnboardingStatusResponse
      const completed = result.status === 'Complete' || nextStatus.status === 'Complete'
      const nextCount = completed ? TRAINING_SAMPLE_COUNT : Math.max(trainingCount + 1, nextStatus.currentPromptIndex)

      setTrainingCount(nextCount)
      setPrompt(result.nextPrompt || nextStatus.nextPrompt || 'Great. Continue with the next phrase.')
      setStatusNote(result.message || (completed ? 'Enrollment complete.' : 'Sample accepted.'))

      if (completed) {
        setNotice({ type: 'success', message: `${result.completedProfile?.name || speakerName.trim()} enrolled successfully.` })
        setShowEnrollment(false)
        setSessionId(null)
        setSpeakerName('')
        setWakePhrase('')
        teardownAudio()
        await Promise.all([loadDirectories(), loadStatus()])
      }
    } catch (error) {
      setEnrollmentError(error instanceof Error ? error.message : 'Failed to upload the voice sample.')
    } finally {
      setEnrollmentBusy(false)
    }
  }

  function handlePrepareWakeWord() {
    if (!wakeWordDraft.trim()) {
      setNotice({ type: 'info', message: 'Enter a wake word phrase first.' })
      return
    }
    setSpeakerName(wakeWordOwner.trim())
    setWakePhrase(wakeWordDraft.trim())
    setShowEnrollment(true)
    setShowWakeWordForm(false)
    setActiveTab('profiles')
    setNotice({ type: 'info', message: 'Wake words are registered during speaker enrollment. Continue below to finish setup.' })
  }

  return (
    <div className="mx-auto max-w-7xl space-y-6">
      <section className="relative overflow-hidden rounded-[28px] border border-stone/60 bg-obsidian px-6 py-7 shadow-[0_20px_80px_rgba(0,0,0,0.35)] sm:px-8">
        <div className="pointer-events-none absolute inset-0 bg-[radial-gradient(circle_at_top_right,rgba(245,158,11,0.16),transparent_32%),radial-gradient(circle_at_bottom_left,rgba(34,211,238,0.1),transparent_28%)]" />
        <div className="relative flex flex-col gap-4 lg:flex-row lg:items-end lg:justify-between">
          <div className="max-w-3xl space-y-3">
            <div className="inline-flex items-center gap-2 rounded-full border border-amber/20 bg-amber/10 px-3 py-1 text-[11px] font-semibold uppercase tracking-[0.28em] text-amber">
              Voice platform
            </div>
            <div>
              <h1 className="font-display text-3xl font-semibold tracking-tight text-light sm:text-4xl">Wyoming control room</h1>
              <p className="mt-3 max-w-2xl text-sm leading-6 text-fog">
                Install speech models, verify readiness, manage enrolled speakers, and keep custom wake words tidy from one dashboard surface.
              </p>
            </div>
          </div>
          <div className="grid gap-3 sm:grid-cols-2">
            <MetricCard label="Installed models" value={String(installedIds.size)} />
            <MetricCard label="Profiles" value={String(profiles.length)} />
          </div>
        </div>
      </section>

      {notice && <NoticeBanner notice={notice} onDismiss={() => setNotice(null)} />}

      <div className="flex flex-wrap gap-1 rounded-2xl border border-stone/40 bg-obsidian p-1">
        {tabs.map(({ id, label, icon: Icon }) => (
          <button
            key={id}
            onClick={() => setActiveTab(id)}
            className={`flex flex-1 items-center justify-center gap-2 rounded-xl px-4 py-3 text-sm font-medium transition-colors ${
              activeTab === id ? 'bg-amber/10 text-amber' : 'text-fog hover:bg-stone/40 hover:text-light'
            }`}
          >
            <Icon className="h-4 w-4" />
            {label}
          </button>
        ))}
      </div>

      {activeTab === 'status' && (
        <section className="grid gap-4 xl:grid-cols-[1.2fr,0.8fr]">
          <div className="rounded-[24px] border border-stone/60 bg-charcoal/70 p-5 sm:p-6">
            <div className="mb-5 flex items-center justify-between gap-3">
              <div>
                <p className="text-xs uppercase tracking-[0.24em] text-dust">Readiness</p>
                <h2 className="mt-2 font-display text-2xl text-light">Engine status</h2>
              </div>
              {statusLoading ? <Loader2 className="h-5 w-5 animate-spin text-amber" /> : <StatusBadge ready={status?.configured ?? false} />}
            </div>
            <div className="grid gap-3 sm:grid-cols-2">
              <StatusTile icon={Mic} label="STT Engine" ready={status?.stt.ready} />
              <StatusTile icon={Radio} label="Wake Word Detector" ready={status?.wakeWord.ready} />
              <StatusTile icon={User} label="Speaker Verification" ready={status?.diarization.ready} />
              <StatusTile icon={Volume2} label="Custom Wake Words" ready={status?.customWakeWords.ready} />
            </div>
          </div>
          <div className="rounded-[24px] border border-stone/60 bg-basalt/50 p-5 sm:p-6">
            <div className="flex items-center gap-2 text-amber">
              <Cpu className="h-4 w-4" />
              <h2 className="font-display text-xl text-light">Next steps</h2>
            </div>
            <p className="mt-3 text-sm leading-6 text-fog">
              {status?.configured
                ? 'Wyoming is configured. Keep at least one streaming-capable STT model installed and active for the smoothest voice experience.'
                : 'Wyoming still needs a working STT model and wake-word stack. Start in the Models tab, then enroll a speaker profile once speech is ready.'}
            </p>
            <ul className="mt-4 space-y-3 text-sm text-fog">
              <StatusListItem done={installedIds.size > 0} text="Download at least one sherpa-onnx STT model" />
              <StatusListItem done={Boolean(activeModelId)} text="Activate the preferred model for live transcription" />
              <StatusListItem done={profiles.length > 0} text="Enroll a speaker profile for personalization" />
              <StatusListItem done={wakeWords.length > 0} text="Register or verify custom wake words if needed" />
            </ul>
          </div>
        </section>
      )}

      {activeTab === 'models' && (
        <section className="space-y-4">
          <div className="grid gap-3 rounded-[24px] border border-stone/60 bg-charcoal/70 p-5 lg:grid-cols-[1fr,1fr,auto] lg:items-end">
            <label className="text-sm text-fog">
              <span className="mb-2 block text-xs font-semibold uppercase tracking-[0.24em] text-dust">Language</span>
              <select value={languageFilter} onChange={event => setLanguageFilter(event.target.value)} className={inputClass}>
                {languageOptions.map(option => <option key={option} value={option}>{option === 'all' ? 'All languages' : option}</option>)}
              </select>
            </label>
            <label className="text-sm text-fog">
              <span className="mb-2 block text-xs font-semibold uppercase tracking-[0.24em] text-dust">Architecture</span>
              <select value={architectureFilter} onChange={event => setArchitectureFilter(event.target.value)} className={inputClass}>
                {architectureOptions.map(option => <option key={option} value={option}>{option === 'all' ? 'All architectures' : archLabel(option)}</option>)}
              </select>
            </label>
            <label className="flex items-center gap-3 rounded-xl border border-stone/50 bg-basalt px-4 py-3 text-sm text-fog">
              <input type="checkbox" checked={streamingOnly} onChange={event => setStreamingOnly(event.target.checked)} className="h-4 w-4 accent-amber" />
              Streaming only
            </label>
          </div>

          {modelsLoading ? <LoadingPanel label="Loading model catalog…" /> : (
            <div className="space-y-6">
              <ModelSection title="Installed models" subtitle="Installed models stay pinned to the top for quick activation and cleanup." models={filteredModels.installed} emptyMessage="No models installed yet." renderCard={model => (
                <ModelCard key={model.id} model={model} busy={busyModelIds.has(model.id)} onDownload={() => handleModelDownload(model.id)} onActivate={() => handleModelActivate(model.id)} onDelete={() => handleModelDelete(model.id)} />
              )} />
              <ModelSection title="Available catalog" subtitle="Browse the Wyoming catalog and download only the models you actually need." models={filteredModels.catalog} emptyMessage="No models match the current filters." renderCard={model => (
                <ModelCard key={model.id} model={model} busy={busyModelIds.has(model.id)} onDownload={() => handleModelDownload(model.id)} onActivate={() => handleModelActivate(model.id)} onDelete={() => handleModelDelete(model.id)} />
              )} />
            </div>
          )}
        </section>
      )}

      {activeTab === 'profiles' && (
        <section className="space-y-4">
          <div className="flex flex-wrap items-center justify-between gap-3 rounded-[24px] border border-stone/60 bg-charcoal/70 p-5 sm:p-6">
            <div>
              <h2 className="font-display text-2xl text-light">Speaker profiles</h2>
              <p className="mt-2 text-sm text-fog">Enrolled speakers improve speaker-aware routing and make custom wake words easier to manage.</p>
            </div>
            <button type="button" className={buttonPrimary} onClick={() => setShowEnrollment(current => !current)}>
              <Mic className="h-4 w-4" />
              {showEnrollment ? 'Hide enrollment' : 'Enroll New Speaker'}
            </button>
          </div>

          {showEnrollment && (
            <div className="grid gap-4 rounded-[24px] border border-stone/60 bg-basalt/50 p-5 xl:grid-cols-[1.1fr,0.9fr]">
              <div className="space-y-4 rounded-[20px] border border-stone/60 bg-charcoal/70 p-5">
                <div>
                  <p className="text-xs uppercase tracking-[0.24em] text-dust">Inline wizard</p>
                  <h3 className="mt-2 font-display text-xl text-light">Speaker enrollment</h3>
                </div>
                {!sessionId ? (
                  <>
                    <input className={inputClass} value={speakerName} onChange={event => setSpeakerName(event.target.value)} placeholder="Speaker name" />
                    <input className={inputClass} value={wakePhrase} onChange={event => setWakePhrase(event.target.value)} placeholder="Optional wake phrase" />
                    <button type="button" className={buttonPrimary} onClick={() => void handleStartEnrollment()} disabled={enrollmentBusy || !speakerName.trim()}>
                      {enrollmentBusy ? <Loader2 className="h-4 w-4 animate-spin" /> : <Mic className="h-4 w-4" />}
                      Start enrollment
                    </button>
                  </>
                ) : (
                  <>
                    <PromptCard prompt={prompt} count={trainingCount} />
                    <RecorderCard meterLevel={meterLevel} statusNote={statusNote} isRecording={isRecording} onStart={() => void beginRecording()} onStop={() => void handleSubmitSample()} disabled={enrollmentBusy} />
                  </>
                )}
                {enrollmentError && <div className="rounded-xl border border-rose/30 bg-rose/10 px-4 py-3 text-sm text-rose">{enrollmentError}</div>}
              </div>
              <div className="space-y-4 rounded-[20px] border border-stone/60 bg-obsidian/70 p-5">
                <h3 className="font-display text-lg text-light">Enrollment notes</h3>
                <ul className="space-y-3 text-sm text-fog">
                  <StatusListItem done={Boolean(sessionId)} text="Start the onboarding session and register the wake phrase." />
                  <StatusListItem done={trainingCount >= 2} text="Capture a few clear prompts with a steady speaking distance." />
                  <StatusListItem done={trainingCount >= TRAINING_SAMPLE_COUNT} text="Complete all five accepted samples." />
                </ul>
              </div>
            </div>
          )}

          {directoryLoading ? <LoadingPanel label="Loading speaker profiles…" /> : <DirectoryGrid>
            {profiles.length === 0 ? <EmptyCard message="No speaker profiles enrolled yet." /> : profiles.map(profile => (
              <div key={profile.id} className="rounded-[24px] border border-stone/60 bg-charcoal/70 p-5">
                <div className="flex items-start justify-between gap-4">
                  <div>
                    <div className="flex items-center gap-2">
                      <h3 className="font-display text-xl text-light">{profile.name}</h3>
                      {profile.isProvisional && <Badge tone="amber">Provisional</Badge>}
                    </div>
                    <p className="mt-2 text-sm text-fog">{profile.isAuthorized ? 'Authorized speaker' : 'Needs review'} · {profile.interactionCount} interactions</p>
                    <p className="mt-1 text-xs text-dust">Enrolled {formatDate(profile.enrolledAt)} · Last seen {formatDate(profile.lastSeenAt)}</p>
                  </div>
                  <IconButton label="Delete speaker" onClick={() => void handleDeleteSpeaker(profile.id)}><Trash2 className="h-4 w-4" /></IconButton>
                </div>
              </div>
            ))}
          </DirectoryGrid>}
        </section>
      )}

      {activeTab === 'wake-words' && (
        <section className="space-y-4">
          <div className="flex flex-wrap items-center justify-between gap-3 rounded-[24px] border border-stone/60 bg-charcoal/70 p-5 sm:p-6">
            <div>
              <h2 className="font-display text-2xl text-light">Custom wake words</h2>
              <p className="mt-2 text-sm text-fog">Review tuned phrases, calibration state, and the speaker association backing each wake word.</p>
            </div>
            <button type="button" className={buttonSecondary} onClick={() => setShowWakeWordForm(current => !current)}>
              <Volume2 className="h-4 w-4" />
              {showWakeWordForm ? 'Hide form' : 'Add Wake Word'}
            </button>
          </div>

          {showWakeWordForm && (
            <div className="grid gap-3 rounded-[24px] border border-stone/60 bg-basalt/50 p-5 lg:grid-cols-[1fr,1fr,auto] lg:items-end">
              <input className={inputClass} value={wakeWordDraft} onChange={event => setWakeWordDraft(event.target.value)} placeholder="Wake phrase" />
              <input className={inputClass} value={wakeWordOwner} onChange={event => setWakeWordOwner(event.target.value)} placeholder="Speaker name for enrollment (optional)" />
              <button type="button" className={buttonPrimary} onClick={handlePrepareWakeWord}>Continue to enrollment</button>
            </div>
          )}

          {directoryLoading ? <LoadingPanel label="Loading wake words…" /> : <DirectoryGrid>
            {wakeWords.length === 0 ? <EmptyCard message="No wake words registered yet." /> : wakeWords.map(word => (
              <div key={word.id} className="rounded-[24px] border border-stone/60 bg-charcoal/70 p-5">
                <div className="flex items-start justify-between gap-4">
                  <div className="space-y-2">
                    <div className="flex items-center gap-2">
                      <h3 className="font-display text-xl text-light">{word.phrase}</h3>
                      {word.isCalibrated && <Badge tone="sage">Calibrated</Badge>}
                    </div>
                    <div className="flex flex-wrap gap-2 text-xs text-dust">
                      <InlineMeta icon={Volume2} label={`Boost ${formatNumber(word.boostScore, 1.5)}`} />
                      <InlineMeta icon={Radio} label={`Threshold ${formatNumber(word.threshold, 0.3)}`} />
                      <InlineMeta icon={User} label={word.userId ? `User ${word.userId}` : 'No speaker linked'} />
                    </div>
                  </div>
                  <IconButton label="Delete wake word" onClick={() => void handleDeleteWakeWord(word.id)}><Trash2 className="h-4 w-4" /></IconButton>
                </div>
              </div>
            ))}
          </DirectoryGrid>}
        </section>
      )}
    </div>
  )
}

function ModelSection({ title, subtitle, models, emptyMessage, renderCard }: { title: string; subtitle: string; models: Array<AsrModel & { isInstalled: boolean; isActive: boolean }>; emptyMessage: string; renderCard: (model: AsrModel & { isInstalled: boolean; isActive: boolean }) => ReactNode }) {
  return (
    <div className="space-y-3">
      <div>
        <h2 className="font-display text-xl text-light">{title}</h2>
        <p className="mt-1 text-sm text-fog">{subtitle}</p>
      </div>
      {models.length === 0 ? <EmptyCard message={emptyMessage} /> : <DirectoryGrid>{models.map(renderCard)}</DirectoryGrid>}
    </div>
  )
}

function ModelCard({ model, busy, onDownload, onActivate, onDelete }: { model: AsrModel & { isInstalled: boolean; isActive: boolean }; busy: boolean; onDownload: () => void; onActivate: () => void; onDelete: () => void }) {
  return (
    <div className="rounded-[24px] border border-stone/60 bg-charcoal/70 p-5">
      <div className="flex items-start justify-between gap-4">
        <div className="min-w-0 space-y-3">
          <div className="flex flex-wrap items-center gap-2">
            <h3 className="font-display text-xl text-light">{model.name}</h3>
            {model.isActive && <Badge tone="sage">Active</Badge>}
            {model.isInstalled && <Badge tone="sky">Installed</Badge>}
            {model.isStreaming && <Badge tone="amber">Streaming</Badge>}
          </div>
          <div className="flex flex-wrap gap-2 text-xs text-dust">
            <InlineMeta icon={Globe} label={model.languages.join(', ')} />
            <InlineMeta icon={Cpu} label={archLabel(model.architecture)} />
            <InlineMeta icon={Download} label={formatBytes(model.sizeBytes)} />
          </div>
          <p className="text-sm leading-6 text-fog">{model.description || 'Sherpa-onnx speech recognition model.'}</p>
        </div>
        <div className="flex shrink-0 items-center gap-2">
          {!model.isInstalled && <button type="button" onClick={onDownload} disabled={busy} className={buttonPrimary}>{busy ? <Loader2 className="h-4 w-4 animate-spin" /> : <Download className="h-4 w-4" />}Download</button>}
          {model.isInstalled && !model.isActive && <button type="button" onClick={onActivate} disabled={busy} className={buttonSecondary}>{busy ? <Loader2 className="h-4 w-4 animate-spin" /> : <Check className="h-4 w-4" />}Activate</button>}
          {model.isInstalled && !model.isActive && <IconButton label="Delete model" onClick={onDelete}><Trash2 className="h-4 w-4" /></IconButton>}
        </div>
      </div>
    </div>
  )
}

function PromptCard({ prompt, count }: { prompt: string; count: number }) {
  return (
    <div className="rounded-2xl border border-amber/20 bg-amber/10 p-5">
      <p className="text-xs uppercase tracking-[0.24em] text-dust">Prompt {Math.min(count + 1, TRAINING_SAMPLE_COUNT)} of {TRAINING_SAMPLE_COUNT}</p>
      <p className="mt-3 font-display text-2xl leading-tight text-light">{prompt}</p>
    </div>
  )
}

function RecorderCard({ meterLevel, statusNote, isRecording, onStart, onStop, disabled }: { meterLevel: number; statusNote: string; isRecording: boolean; onStart: () => void; onStop: () => void; disabled: boolean }) {
  return (
    <div className="rounded-[24px] border border-stone/60 bg-obsidian/70 p-5">
      <div className="mb-4 flex items-center justify-between gap-3">
        <p className="text-sm text-fog">{statusNote}</p>
        <Badge tone={isRecording ? 'rose' : 'sage'}>{isRecording ? 'Recording' : 'Ready'}</Badge>
      </div>
      <div className="mb-5 flex h-20 items-end gap-2 rounded-2xl border border-stone/50 bg-charcoal/70 px-4 py-4">
        {Array.from({ length: 16 }).map((_, index) => {
          const threshold = (index + 1) / 16
          const active = Math.max(meterLevel, 0.05) >= threshold
          return <div key={index} className={`flex-1 rounded-full transition-all duration-100 ${active ? 'bg-gradient-to-t from-amber to-cyan-300' : 'bg-basalt'}`} style={{ height: `${28 + Math.max(meterLevel * 100 - index * 4, 0)}%` }} />
        })}
      </div>
      {!isRecording ? <button type="button" className={buttonPrimary} onClick={onStart} disabled={disabled}><Mic className="h-4 w-4" />Record sample</button> : <button type="button" className={buttonSecondary} onClick={onStop} disabled={disabled}><Mic className="h-4 w-4" />Stop and upload</button>}
    </div>
  )
}

function StatusTile({ icon: Icon, label, ready }: { icon: typeof Mic; label: string; ready?: boolean }) {
  return <div className="rounded-2xl border border-stone/60 bg-basalt/40 p-4"><div className="flex items-center gap-3"><div className={`flex h-10 w-10 items-center justify-center rounded-2xl ${ready ? 'bg-sage/15 text-sage' : 'bg-amber/10 text-amber'}`}><Icon className="h-4 w-4" /></div><div><p className="text-sm text-light">{label}</p><p className="text-xs text-dust">{ready ? 'Ready' : 'Not configured'}</p></div></div></div>
}

function StatusBadge({ ready }: { ready: boolean }) {
  return <span className={`inline-flex items-center gap-2 rounded-full px-3 py-1 text-xs font-medium ${ready ? 'bg-sage/15 text-sage' : 'bg-amber/10 text-amber'}`}>{ready ? <Check className="h-3.5 w-3.5" /> : <Cpu className="h-3.5 w-3.5" />}{ready ? 'Configured' : 'Setup needed'}</span>
}

function StatusListItem({ done, text }: { done: boolean; text: string }) {
  return <li className="flex items-start gap-3"><span className={`mt-0.5 flex h-5 w-5 items-center justify-center rounded-full ${done ? 'bg-sage/15 text-sage' : 'bg-stone/50 text-dust'}`}><Check className="h-3 w-3" /></span><span>{text}</span></li>
}

function MetricCard({ label, value }: { label: string; value: string }) {
  return <div className="rounded-2xl border border-stone/50 bg-charcoal/60 px-4 py-3"><p className="text-xs uppercase tracking-[0.22em] text-dust">{label}</p><p className="mt-2 font-display text-2xl text-light">{value}</p></div>
}

function NoticeBanner({ notice, onDismiss }: { notice: Exclude<Notice, null>; onDismiss: () => void }) {
  return <div className={`flex items-center justify-between gap-3 rounded-2xl border px-4 py-3 text-sm ${notice.type === 'error' ? 'border-rose/30 bg-rose/10 text-rose' : notice.type === 'success' ? 'border-sage/30 bg-sage/10 text-sage' : 'border-sky-500/30 bg-sky-500/10 text-sky-300'}`}><span>{notice.message}</span><button type="button" onClick={onDismiss} className="text-xs uppercase tracking-[0.2em]">Dismiss</button></div>
}

function Badge({ tone, children }: { tone: 'sage' | 'amber' | 'sky' | 'rose'; children: ReactNode }) {
  const styles = {
    sage: 'bg-sage/15 text-sage',
    amber: 'bg-amber/10 text-amber',
    sky: 'bg-sky-500/15 text-sky-300',
    rose: 'bg-rose/15 text-rose',
  }
  return <span className={`inline-flex items-center rounded-full px-2.5 py-1 text-xs font-medium ${styles[tone]}`}>{children}</span>
}

function IconButton({ label, onClick, children }: { label: string; onClick: () => void; children: ReactNode }) {
  return <button type="button" aria-label={label} title={label} onClick={onClick} className="rounded-xl border border-stone/50 p-2 text-fog transition-colors hover:border-rose/30 hover:text-rose">{children}</button>
}

function InlineMeta({ icon: Icon, label }: { icon: typeof Mic; label: string }) {
  return <span className="inline-flex items-center gap-1.5 rounded-full border border-stone/50 bg-basalt/60 px-2.5 py-1"><Icon className="h-3 w-3" />{label}</span>
}

function DirectoryGrid({ children }: { children: ReactNode }) {
  return <div className="grid gap-4 xl:grid-cols-2">{children}</div>
}

function LoadingPanel({ label }: { label: string }) {
  return <div className="flex items-center justify-center rounded-[24px] border border-stone/60 bg-charcoal/70 px-6 py-10 text-fog"><Loader2 className="mr-3 h-5 w-5 animate-spin text-amber" />{label}</div>
}

function EmptyCard({ message }: { message: string }) {
  return <div className="rounded-[24px] border border-dashed border-stone/60 bg-charcoal/40 px-6 py-10 text-center text-sm text-dust">{message}</div>
}

function formatBytes(size: number) {
  if (!size) return 'Unknown size'
  const units = ['B', 'KB', 'MB', 'GB']
  let value = size
  let unitIndex = 0
  while (value >= 1024 && unitIndex < units.length - 1) {
    value /= 1024
    unitIndex++
  }
  return `${value.toFixed(unitIndex === 0 ? 0 : 1)} ${units[unitIndex]}`
}

function formatDate(value?: string | null) {
  if (!value) return 'recently'
  return new Date(value).toLocaleString()
}

function formatNumber(value: number | undefined, fallback: number) {
  return (value ?? fallback).toFixed(2)
}

function flattenSamples(chunks: Float32Array[]) {
  const totalLength = chunks.reduce((sum, chunk) => sum + chunk.length, 0)
  const output = new Float32Array(totalLength)
  let offset = 0
  for (const chunk of chunks) {
    output.set(chunk, offset)
    offset += chunk.length
  }
  return output
}

function downsampleBuffer(buffer: Float32Array, inputRate: number, outputRate: number) {
  if (outputRate >= inputRate) return buffer
  const ratio = inputRate / outputRate
  const newLength = Math.round(buffer.length / ratio)
  const result = new Float32Array(newLength)
  let resultOffset = 0
  let bufferOffset = 0

  while (resultOffset < result.length) {
    const nextOffset = Math.round((resultOffset + 1) * ratio)
    let accum = 0
    let count = 0
    for (let index = bufferOffset; index < nextOffset && index < buffer.length; index++) {
      accum += buffer[index]
      count++
    }
    result[resultOffset] = count > 0 ? accum / count : 0
    resultOffset++
    bufferOffset = nextOffset
  }

  return result
}

function encodeWav(samples: Float32Array, sampleRate: number) {
  const buffer = new ArrayBuffer(44 + samples.length * 2)
  const view = new DataView(buffer)
  const writeString = (offset: number, value: string) => {
    for (let index = 0; index < value.length; index++) view.setUint8(offset + index, value.charCodeAt(index))
  }

  writeString(0, 'RIFF')
  view.setUint32(4, 36 + samples.length * 2, true)
  writeString(8, 'WAVE')
  writeString(12, 'fmt ')
  view.setUint32(16, 16, true)
  view.setUint16(20, 1, true)
  view.setUint16(22, 1, true)
  view.setUint32(24, sampleRate, true)
  view.setUint32(28, sampleRate * 2, true)
  view.setUint16(32, 2, true)
  view.setUint16(34, 16, true)
  writeString(36, 'data')
  view.setUint32(40, samples.length * 2, true)

  for (let index = 0; index < samples.length; index++) {
    const sample = Math.max(-1, Math.min(1, samples[index]))
    view.setInt16(44 + index * 2, sample < 0 ? sample * 0x8000 : sample * 0x7fff, true)
  }

  return new Blob([buffer], { type: 'audio/wav' })
}

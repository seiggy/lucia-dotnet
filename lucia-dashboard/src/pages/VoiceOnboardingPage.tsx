import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import type { ReactNode } from 'react'
import { ArrowLeft, ArrowRight, CheckCircle2, Loader2, Mic, MicOff, Trash2, Volume2 } from 'lucide-react'
import {
  deleteSpeakerProfile,
  deleteWakeWord,
  fetchWyomingStatus,
  getOnboardingStatus,
  listSpeakerProfiles,
  listWakeWords,
  startOnboarding,
  uploadVoiceSample,
} from '../api'

type WizardStep = 'welcome' | 'wake-word' | 'training' | 'calibration' | 'complete'
type WakeWordMode = 'default' | 'custom'

type StartOnboardingResponse = {
  id: string
  wakeWordId?: string | null
  firstPrompt?: string | null
  totalPrompts: number
}

type OnboardingStatusResponse = {
  id: string
  speakerName: string
  status: string
  currentPromptIndex: number
  totalPrompts: number
  nextPrompt?: string | null
}

type OnboardingStepResult = {
  status: 'NextPrompt' | 'Retry' | 'Complete' | 'Error'
  message: string
  nextPrompt?: string | null
  progressPercent: number
  completedProfile?: { id: string; name: string } | null
}

type SpeakerProfileSummary = {
  id: string
  name: string
  isAuthorized: boolean
  interactionCount: number
  enrolledAt: string
}

type WakeWordSummary = {
  id: string
  phrase: string
  isDefault: boolean
  isCalibrated: boolean
  calibrationSamples: number
}

const steps: { key: WizardStep; label: string }[] = [
  { key: 'welcome', label: 'Welcome' },
  { key: 'wake-word', label: 'Wake Word' },
  { key: 'training', label: 'Voice Training' },
  { key: 'calibration', label: 'Calibration' },
  { key: 'complete', label: 'Complete' },
]

const btnPrimary = 'inline-flex items-center justify-center gap-2 rounded-xl bg-amber px-5 py-2.5 text-sm font-semibold text-void transition-all hover:bg-amber-glow disabled:cursor-not-allowed disabled:opacity-40'
const btnSecondary = 'inline-flex items-center justify-center gap-2 rounded-xl border border-stone bg-basalt px-5 py-2.5 text-sm font-medium text-fog transition-colors hover:border-amber/30 hover:text-light disabled:cursor-not-allowed disabled:opacity-40'
const inputStyle = 'w-full rounded-xl border border-stone bg-basalt px-4 py-3 text-sm text-light placeholder-dust/60 input-focus transition-colors'
const defaultWakeWords = ['Hey Lucia', 'Okay Lucia', 'Lucia, listen']
const TARGET_SAMPLE_RATE = 16_000
const TRAINING_SAMPLE_COUNT = 5
const CALIBRATION_SAMPLE_COUNT = 3

export default function VoiceOnboardingPage() {
  const [platformReady, setPlatformReady] = useState<boolean | null>(null)
  const [step, setStep] = useState<WizardStep>('welcome')
  const [speakerName, setSpeakerName] = useState('')
  const [wakeWordMode, setWakeWordMode] = useState<WakeWordMode>('default')
  const [selectedWakeWord, setSelectedWakeWord] = useState(defaultWakeWords[0])
  const [customWakeWord, setCustomWakeWord] = useState('')
  const [sessionId, setSessionId] = useState<string | null>(null)
  const [wakeWordId, setWakeWordId] = useState<string | null>(null)
  const [currentPrompt, setCurrentPrompt] = useState('Introduce yourself and say your name clearly.')
  const [trainingCount, setTrainingCount] = useState(0)
  const [calibrationCount, setCalibrationCount] = useState(0)
  const [completedProfileName, setCompletedProfileName] = useState<string | null>(null)
  const [meterLevel, setMeterLevel] = useState(0)
  const [recordingMode, setRecordingMode] = useState<'training' | 'calibration' | null>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [statusNote, setStatusNote] = useState('We will capture five short samples to build your voice profile.')
  const [speakerProfiles, setSpeakerProfiles] = useState<SpeakerProfileSummary[]>([])
  const [wakeWords, setWakeWords] = useState<WakeWordSummary[]>([])
  const [loadingSummary, setLoadingSummary] = useState(false)

  const streamRef = useRef<MediaStream | null>(null)
  const mediaRecorderRef = useRef<MediaRecorder | null>(null)
  const audioContextRef = useRef<AudioContext | null>(null)
  const analyserRef = useRef<AnalyserNode | null>(null)
  const sourceRef = useRef<MediaStreamAudioSourceNode | null>(null)
  const processorRef = useRef<ScriptProcessorNode | null>(null)
  const muteGainRef = useRef<GainNode | null>(null)
  const animationFrameRef = useRef<number | null>(null)
  const sampleRateRef = useRef(TARGET_SAMPLE_RATE)
  const pcmChunksRef = useRef<Float32Array[]>([])
  const isRecordingRef = useRef(false)

  const activeWakeWord = wakeWordMode === 'custom' ? customWakeWord.trim() : selectedWakeWord
  const stepIndex = steps.findIndex(item => item.key === step)
  const progressPercent = useMemo(() => ((stepIndex + 1) / steps.length) * 100, [stepIndex])

  useEffect(() => {
    let ignore = false

    async function loadPlatformStatus() {
      const status = await fetchWyomingStatus()
      if (ignore) return
      setPlatformReady(status?.configured ?? false)
    }

    void loadPlatformStatus()

    return () => {
      ignore = true
    }
  }, [])

  const stopMeter = useCallback(() => {
    if (animationFrameRef.current !== null) {
      cancelAnimationFrame(animationFrameRef.current)
      animationFrameRef.current = null
    }
  }, [])

  const teardownAudio = useCallback(() => {
    stopMeter()
    processorRef.current?.disconnect()
    muteGainRef.current?.disconnect()
    sourceRef.current?.disconnect()
    streamRef.current?.getTracks().forEach(track => track.stop())
    void audioContextRef.current?.close()

    processorRef.current = null
    muteGainRef.current = null
    sourceRef.current = null
    mediaRecorderRef.current = null
    analyserRef.current = null
    audioContextRef.current = null
    streamRef.current = null
    pcmChunksRef.current = []
    isRecordingRef.current = false
    setMeterLevel(0)
    setRecordingMode(null)
  }, [stopMeter])

  useEffect(() => teardownAudio, [teardownAudio])

  const startMeter = useCallback(() => {
    const analyser = analyserRef.current
    if (!analyser) return

    const data = new Uint8Array(analyser.fftSize)
    const tick = () => {
      analyser.getByteTimeDomainData(data)
      let sum = 0
      for (let i = 0; i < data.length; i++) {
        const normalized = (data[i] - 128) / 128
        sum += normalized * normalized
      }
      setMeterLevel(Math.min(1, Math.sqrt(sum / data.length) * 3.5))
      animationFrameRef.current = requestAnimationFrame(tick)
    }

    stopMeter()
    tick()
  }, [stopMeter])

  const ensureMicrophone = useCallback(async () => {
    if (!navigator.mediaDevices?.getUserMedia) {
      throw new Error('This browser does not support microphone access.')
    }

    if (streamRef.current && audioContextRef.current && sourceRef.current && analyserRef.current) {
      if (audioContextRef.current.state === 'suspended') await audioContextRef.current.resume()
      return
    }

    const stream = await navigator.mediaDevices.getUserMedia({
      audio: {
        channelCount: 1,
        echoCancellation: true,
        noiseSuppression: true,
        autoGainControl: true,
      },
    })

    const audioContext = new AudioContext()
    await audioContext.resume()

    const source = audioContext.createMediaStreamSource(stream)
    const analyser = audioContext.createAnalyser()
    analyser.fftSize = 512
    source.connect(analyser)

    streamRef.current = stream
    sourceRef.current = source
    analyserRef.current = analyser
    audioContextRef.current = audioContext
    mediaRecorderRef.current = new MediaRecorder(stream)
    sampleRateRef.current = audioContext.sampleRate
    startMeter()
  }, [startMeter])

  const startCapture = useCallback(async (mode: 'training' | 'calibration') => {
    setError(null)
    try {
      await ensureMicrophone()
      const audioContext = audioContextRef.current
      const source = sourceRef.current
      if (!audioContext || !source) throw new Error('Microphone is not ready yet.')

      pcmChunksRef.current = []
      isRecordingRef.current = true
      setRecordingMode(mode)
      setStatusNote(mode === 'training' ? 'Recording sample… keep your pace natural and steady.' : `Say “${activeWakeWord}” in your usual tone.`)

      const processor = audioContext.createScriptProcessor(4096, 1, 1)
      const muteGain = audioContext.createGain()
      muteGain.gain.value = 0
      processor.onaudioprocess = event => {
        if (!isRecordingRef.current) return
        const samples = event.inputBuffer.getChannelData(0)
        pcmChunksRef.current.push(new Float32Array(samples))
      }

      source.connect(processor)
      processor.connect(muteGain)
      muteGain.connect(audioContext.destination)

      processorRef.current = processor
      muteGainRef.current = muteGain
      mediaRecorderRef.current?.start()
    } catch (err) {
      setRecordingMode(null)
      const message = err instanceof Error && /denied|Permission/i.test(err.message)
        ? 'Microphone access was denied. Allow microphone access and try again.'
        : err instanceof Error
          ? err.message
          : 'Unable to access your microphone.'
      setError(message)
    }
  }, [activeWakeWord, ensureMicrophone])

  const stopCapture = useCallback(async (): Promise<Blob> => {
    const recorder = mediaRecorderRef.current
    if (!recorder) throw new Error('No active recorder found.')

    return new Promise((resolve, reject) => {
      recorder.onstop = () => {
        try {
          isRecordingRef.current = false
          processorRef.current?.disconnect()
          muteGainRef.current?.disconnect()
          processorRef.current = null
          muteGainRef.current = null
          setRecordingMode(null)

          const merged = flattenSamples(pcmChunksRef.current)
          if (merged.length < TARGET_SAMPLE_RATE) {
            reject(new Error('Recording too short. Please speak for at least one second.'))
            return
          }

          const resampled = downsampleBuffer(merged, sampleRateRef.current, TARGET_SAMPLE_RATE)
          resolve(encodeWav(resampled, TARGET_SAMPLE_RATE))
        } catch (err) {
          reject(err)
        }
      }

      recorder.onerror = () => reject(new Error('The recorder stopped unexpectedly.'))
      recorder.stop()
    })
  }, [])

  const refreshSummary = useCallback(async () => {
    setLoadingSummary(true)
    try {
      const [profiles, wakeWordRows] = await Promise.all([
        listSpeakerProfiles() as Promise<SpeakerProfileSummary[]>,
        listWakeWords() as Promise<WakeWordSummary[]>,
      ])
      setSpeakerProfiles(profiles)
      setWakeWords(wakeWordRows)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load enrollment summary.')
    } finally {
      setLoadingSummary(false)
    }
  }, [])

  useEffect(() => {
    if (step === 'complete') {
      void refreshSummary()
    }
  }, [refreshSummary, step])

  async function beginOnboarding() {
    setBusy(true)
    setError(null)
    try {
      const result = await startOnboarding(speakerName.trim(), activeWakeWord || undefined) as StartOnboardingResponse
      setSessionId(result.id)
      setWakeWordId(result.wakeWordId ?? null)
      setCurrentPrompt(result.firstPrompt || 'Please say the displayed prompt clearly.')
      setTrainingCount(0)
      setStatusNote('Session started. Use the meter to find a comfortable speaking level.')
      setStep('training')
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to start onboarding.')
    } finally {
      setBusy(false)
    }
  }

  async function finishTrainingRecording() {
    if (!sessionId) return

    setBusy(true)
    setError(null)
    try {
      const wav = await stopCapture()
      const result = await uploadVoiceSample(sessionId, wav) as OnboardingStepResult

      if (result.status === 'Retry') {
        setStatusNote(result.message || 'Try that prompt one more time with clearer speech.')
        return
      }

      const status = await getOnboardingStatus(sessionId) as OnboardingStatusResponse
      const completed = result.status === 'Complete' || status.status === 'Complete'
      const nextCount = completed ? TRAINING_SAMPLE_COUNT : Math.max(trainingCount + 1, status.currentPromptIndex)

      setTrainingCount(nextCount)
      setCurrentPrompt(result.nextPrompt || status.nextPrompt || 'Great. Continue with the next phrase.')
      setStatusNote(result.message)

      if (result.completedProfile?.name) {
        setCompletedProfileName(result.completedProfile.name)
      }

      if (completed) {
        setCompletedProfileName(result.completedProfile?.name ?? speakerName.trim())
        setStep('calibration')
        setStatusNote(`Voice profile saved. Now let's rehearse “${activeWakeWord}” three times.`)
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to process the voice sample.')
    } finally {
      setBusy(false)
    }
  }

  async function finishCalibrationRecording() {
    setBusy(true)
    setError(null)
    try {
      await stopCapture()
      const next = calibrationCount + 1
      setCalibrationCount(next)
      setStatusNote(next >= CALIBRATION_SAMPLE_COUNT
        ? 'Calibration rehearsal captured. Your voice profile is ready.'
        : `Nice. Repeat the wake phrase ${CALIBRATION_SAMPLE_COUNT - next} more time${CALIBRATION_SAMPLE_COUNT - next === 1 ? '' : 's'}.`)

      if (next >= CALIBRATION_SAMPLE_COUNT) {
        teardownAudio()
        setStep('complete')
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to capture the calibration sample.')
    } finally {
      setBusy(false)
    }
  }

  async function handleDeleteSpeaker(id: string) {
    if (!confirm('Delete this speaker profile?')) return
    try {
      await deleteSpeakerProfile(id)
      await refreshSummary()
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to delete the speaker profile.')
    }
  }

  async function handleDeleteWakeWord(id: string) {
    if (!confirm('Delete this wake word?')) return
    try {
      await deleteWakeWord(id)
      await refreshSummary()
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to delete the wake word.')
    }
  }

  if (platformReady === null) {
    return (
      <div className="flex justify-center p-8">
        <Loader2 className="h-6 w-6 animate-spin text-amber" />
      </div>
    )
  }

  if (!platformReady) {
    return (
      <div className="mx-auto mt-16 max-w-lg rounded-xl bg-white p-8 text-center shadow dark:bg-gray-800">
        <Mic className="mx-auto mb-4 h-12 w-12 text-gray-400" />
        <h2 className="mb-2 text-xl font-semibold text-gray-900 dark:text-white">
          Voice Platform Not Configured
        </h2>
        <p className="mb-6 text-gray-500 dark:text-gray-400">
          The voice platform requires sherpa-onnx models to be installed before you can set up
          voice profiles and wake words.
        </p>
        <a
          href="/configuration"
          className="inline-flex items-center gap-2 rounded-lg bg-indigo-600 px-4 py-2 text-white hover:bg-indigo-700"
        >
          Go to Configuration <ArrowRight className="h-4 w-4" />
        </a>
      </div>
    )
  }

  return (
    <div className="mx-auto max-w-6xl space-y-6">
      <section className="relative overflow-hidden rounded-[28px] border border-stone/60 bg-obsidian px-6 py-7 shadow-[0_20px_80px_rgba(0,0,0,0.35)] sm:px-8">
        <div className="pointer-events-none absolute inset-0 bg-[radial-gradient(circle_at_top_right,rgba(245,158,11,0.16),transparent_34%),radial-gradient(circle_at_bottom_left,rgba(34,211,238,0.12),transparent_32%)]" />
        <div className="pointer-events-none absolute inset-0 opacity-30 [background-image:linear-gradient(rgba(255,255,255,0.04)_1px,transparent_1px),linear-gradient(90deg,rgba(255,255,255,0.04)_1px,transparent_1px)] [background-size:32px_32px]" />
        <div className="relative flex flex-col gap-6 lg:flex-row lg:items-end lg:justify-between">
          <div className="max-w-2xl space-y-4">
            <div className="inline-flex items-center gap-2 rounded-full border border-amber/20 bg-amber/10 px-3 py-1 text-[11px] font-semibold uppercase tracking-[0.28em] text-amber">
              Voice Studio
            </div>
            <div>
              <h1 className="font-display text-3xl font-semibold tracking-tight text-light sm:text-4xl">
                Voice profile enrollment and wake word calibration
              </h1>
              <p className="mt-3 max-w-xl text-sm leading-6 text-fog">
                Build Lucia's ear for your voice with five guided samples, then rehearse the wake phrase in a clean studio-style flow.
              </p>
            </div>
          </div>
          <div className="grid min-w-[280px] grid-cols-2 gap-3 rounded-2xl border border-stone/60 bg-basalt/70 p-4 backdrop-blur-sm">
            <MetricCard label="Voice samples" value={`${trainingCount}/${TRAINING_SAMPLE_COUNT}`} />
            <MetricCard label="Calibration" value={`${calibrationCount}/${CALIBRATION_SAMPLE_COUNT}`} />
          </div>
        </div>
      </section>

      <section className="glass-panel rounded-[28px] border border-stone/60 p-5 sm:p-6">
        <div className="mb-6 flex flex-wrap items-center justify-between gap-3">
          <div>
            <p className="text-xs uppercase tracking-[0.28em] text-dust">Progress</p>
            <h2 className="mt-1 font-display text-xl text-light">{steps[stepIndex].label}</h2>
          </div>
          <span className="rounded-full border border-amber/20 bg-amber/10 px-3 py-1 text-xs font-medium text-amber">
            Step {stepIndex + 1} of {steps.length}
          </span>
        </div>

        <div className="mb-6 overflow-hidden rounded-full bg-basalt">
          <div className="h-2 rounded-full bg-gradient-to-r from-amber via-amber-glow to-cyan-300 transition-all duration-500" style={{ width: `${progressPercent}%` }} />
        </div>

        <div className="mb-6 grid gap-3 sm:grid-cols-5">
          {steps.map((item, index) => {
            const complete = index < stepIndex
            const active = item.key === step
            return (
              <div key={item.key} className={`rounded-2xl border px-3 py-3 transition-colors ${active ? 'border-amber/40 bg-amber/10' : 'border-stone/60 bg-basalt/40'} ${complete ? 'text-light' : 'text-fog'}`}>
                <div className="flex items-center gap-2 text-sm font-medium">
                  {complete ? <CheckCircle2 className="h-4 w-4 text-sage" /> : <span className={`flex h-5 w-5 items-center justify-center rounded-full text-[11px] ${active ? 'bg-amber/20 text-amber' : 'bg-charcoal text-dust'}`}>{index + 1}</span>}
                  <span>{item.label}</span>
                </div>
              </div>
            )
          })}
        </div>

        {error && (
          <div className="mb-6 rounded-2xl border border-rose/30 bg-rose/10 px-4 py-3 text-sm text-rose">
            {error}
          </div>
        )}

        {step === 'welcome' && (
          <div className="grid gap-6 lg:grid-cols-[1.2fr,0.8fr]">
            <StepShell
              eyebrow="Step 1"
              title="Who's enrolling today?"
              description="Give the household member a clear profile name so Lucia can associate voice traits and wake word ownership."
            >
              <label className="block text-sm text-fog">
                <span className="mb-2 block text-xs font-semibold uppercase tracking-[0.24em] text-dust">Speaker name</span>
                <input
                  className={inputStyle}
                  value={speakerName}
                  onChange={event => setSpeakerName(event.target.value)}
                  placeholder="e.g. Zack, Kitchen Host, Guest 1"
                />
              </label>
            </StepShell>
            <AsidePanel
              title="Studio checklist"
              items={[
                'Use the microphone you expect to use for wake word detection.',
                'Record in a normal room, not a silent closet.',
                'Keep TV and music low during the five training prompts.',
              ]}
            />
          </div>
        )}

        {step === 'wake-word' && (
          <div className="grid gap-6 lg:grid-cols-[1.1fr,0.9fr]">
            <StepShell
              eyebrow="Step 2"
              title="Choose the wake phrase"
              description="Pick a built-in phrase or define a custom cue. Lucia will register the phrase now, then you'll rehearse it after training."
            >
              <div className="grid gap-3 sm:grid-cols-3">
                {defaultWakeWords.map(word => (
                  <button
                    key={word}
                    type="button"
                    onClick={() => {
                      setWakeWordMode('default')
                      setSelectedWakeWord(word)
                    }}
                    className={`rounded-2xl border px-4 py-4 text-left transition-colors ${wakeWordMode === 'default' && selectedWakeWord === word ? 'border-amber/40 bg-amber/10 text-light' : 'border-stone/60 bg-basalt/50 text-fog hover:text-light'}`}
                  >
                    <p className="text-xs uppercase tracking-[0.24em] text-dust">Default</p>
                    <p className="mt-2 font-display text-lg">{word}</p>
                  </button>
                ))}
              </div>
              <label className="block text-sm text-fog">
                <span className="mb-2 block text-xs font-semibold uppercase tracking-[0.24em] text-dust">Custom phrase</span>
                <input
                  className={inputStyle}
                  value={customWakeWord}
                  onFocus={() => setWakeWordMode('custom')}
                  onChange={event => {
                    setWakeWordMode('custom')
                    setCustomWakeWord(event.target.value)
                  }}
                  placeholder="e.g. Lucia Night Shift"
                />
              </label>
            </StepShell>
            <AsidePanel
              title="Wake phrase preview"
              items={[
                `Selected phrase: ${activeWakeWord || 'Choose a phrase to continue'}`,
                'Short phrases are easier to repeat consistently.',
                'Avoid phrases that sound like common conversation starters.',
              ]}
            />
          </div>
        )}

        {step === 'training' && (
          <div className="grid gap-6 xl:grid-cols-[1.1fr,0.9fr]">
            <StepShell
              eyebrow="Step 3"
              title="Record five training samples"
              description="Follow the prompt exactly, speak naturally, and stop when you've finished the sentence."
            >
              <PromptPanel title={`Prompt ${Math.min(trainingCount + 1, TRAINING_SAMPLE_COUNT)} of ${TRAINING_SAMPLE_COUNT}`} body={currentPrompt} />
              <RecorderPanel
                meterLevel={meterLevel}
                statusNote={statusNote}
                isRecording={recordingMode === 'training'}
                onStart={() => void startCapture('training')}
                onStop={() => void finishTrainingRecording()}
                disabled={busy}
                buttonText="Record sample"
              />
            </StepShell>
            <AsidePanel
              title="Training status"
              items={[
                `${trainingCount} of ${TRAINING_SAMPLE_COUNT} accepted samples recorded`,
                'The level meter should hover in the amber band while you speak.',
                'If Lucia asks for a retry, just record the same prompt again.',
              ]}
            >
              <div className="rounded-2xl border border-stone/60 bg-charcoal/70 p-4 text-sm text-fog">
                <div className="mb-2 flex items-center justify-between text-xs uppercase tracking-[0.22em] text-dust">
                  <span>Session</span>
                  <span>{sessionId?.slice(0, 8) ?? 'pending'}</span>
                </div>
                <div className="h-2 overflow-hidden rounded-full bg-basalt">
                  <div className="h-full rounded-full bg-gradient-to-r from-cyan-300 to-amber transition-all" style={{ width: `${(trainingCount / TRAINING_SAMPLE_COUNT) * 100}%` }} />
                </div>
              </div>
            </AsidePanel>
          </div>
        )}

        {step === 'calibration' && (
          <div className="grid gap-6 xl:grid-cols-[1.1fr,0.9fr]">
            <StepShell
              eyebrow="Step 4"
              title="Wake word rehearsal"
              description="This optional pass helps you practice the registered phrase in the same voice Lucia will hear every day."
            >
              <PromptPanel title={`Wake phrase ${Math.min(calibrationCount + 1, CALIBRATION_SAMPLE_COUNT)} of ${CALIBRATION_SAMPLE_COUNT}`} body={`Say “${activeWakeWord}” clearly and leave a brief pause after the phrase.`} />
              <RecorderPanel
                meterLevel={meterLevel}
                statusNote={statusNote}
                isRecording={recordingMode === 'calibration'}
                onStart={() => void startCapture('calibration')}
                onStop={() => void finishCalibrationRecording()}
                disabled={busy}
                buttonText="Capture rehearsal"
              />
              <button
                type="button"
                className={btnSecondary}
                onClick={() => {
                  teardownAudio()
                  setStep('complete')
                }}
                disabled={busy || recordingMode !== null}
              >
                Skip calibration
              </button>
            </StepShell>
            <AsidePanel
              title="Calibration notes"
              items={[
                wakeWordId ? `Wake word registered: ${wakeWordId}` : 'Wake word registration will complete with the onboarding session.',
                'Keep your distance from the mic consistent with daily use.',
                'You can skip this step and refine later if needed.',
              ]}
            />
          </div>
        )}

        {step === 'complete' && (
          <div className="grid gap-6 xl:grid-cols-[1fr,1fr]">
            <StepShell
              eyebrow="Step 5"
              title="Enrollment complete"
              description="Your profile is ready for speaker-aware flows. Review the saved speakers and wake words below."
            >
              <div className="rounded-2xl border border-sage/30 bg-sage/10 p-5 text-sm text-sage">
                <div className="flex items-center gap-3">
                  <CheckCircle2 className="h-6 w-6" />
                  <div>
                    <p className="font-display text-lg text-light">{completedProfileName || speakerName || 'Voice profile saved'}</p>
                    <p className="text-fog">Wake phrase: <span className="text-light">{activeWakeWord || 'None'}</span></p>
                  </div>
                </div>
              </div>
              <div className="grid gap-4 md:grid-cols-2">
                <SummaryList
                  title="Speaker profiles"
                  loading={loadingSummary}
                  emptyMessage="No speaker profiles enrolled yet."
                  items={speakerProfiles.map(profile => ({
                    id: profile.id,
                    title: profile.name,
                    detail: `${profile.interactionCount} interactions • ${profile.isAuthorized ? 'Authorized' : 'Review needed'}`,
                    action: () => void handleDeleteSpeaker(profile.id),
                  }))}
                />
                <SummaryList
                  title="Wake words"
                  loading={loadingSummary}
                  emptyMessage="No wake words registered yet."
                  items={wakeWords.map(word => ({
                    id: word.id,
                    title: word.phrase,
                    detail: `${word.isCalibrated ? 'Calibrated' : 'Registered'} • ${word.calibrationSamples} sample${word.calibrationSamples === 1 ? '' : 's'}`,
                    action: () => void handleDeleteWakeWord(word.id),
                  }))}
                />
              </div>
            </StepShell>
            <AsidePanel
              title="Next moves"
              items={[
                'Open the Wyoming streaming dashboard and verify speaker-aware routing is enabled.',
                'Try the wake phrase from the room where the satellite microphone lives.',
                'Delete and re-enroll profiles if the voice characteristics change dramatically.',
              ]}
            />
          </div>
        )}

        <div className="mt-8 flex flex-wrap items-center justify-between gap-3 border-t border-stone/50 pt-5">
          <button
            type="button"
            className={btnSecondary}
            disabled={busy || recordingMode !== null || step === 'welcome' || step === 'training' || step === 'calibration'}
            onClick={() => setStep(previousStep(step))}
          >
            <ArrowLeft className="h-4 w-4" />
            Back
          </button>

          {step === 'welcome' && (
            <button type="button" className={btnPrimary} onClick={() => setStep('wake-word')} disabled={!speakerName.trim()}>
              Continue
              <ArrowRight className="h-4 w-4" />
            </button>
          )}

          {step === 'wake-word' && (
            <button type="button" className={btnPrimary} onClick={() => void beginOnboarding()} disabled={busy || !speakerName.trim() || !activeWakeWord}>
              {busy ? 'Starting…' : 'Start enrollment'}
              <ArrowRight className="h-4 w-4" />
            </button>
          )}

          {step === 'complete' && (
            <button
              type="button"
              className={btnPrimary}
              onClick={() => {
                setStep('welcome')
                setSpeakerName('')
                setCustomWakeWord('')
                setWakeWordMode('default')
                setSelectedWakeWord(defaultWakeWords[0])
                setSessionId(null)
                setWakeWordId(null)
                setTrainingCount(0)
                setCalibrationCount(0)
                setCompletedProfileName(null)
                setStatusNote('We will capture five short samples to build your voice profile.')
                setError(null)
                teardownAudio()
              }}
            >
              Enroll another profile
              <ArrowRight className="h-4 w-4" />
            </button>
          )}
        </div>
      </section>
    </div>
  )
}

function StepShell({
  eyebrow,
  title,
  description,
  children,
}: {
  eyebrow: string
  title: string
  description: string
  children: ReactNode
}) {
  return (
    <div className="space-y-5 rounded-[24px] border border-stone/60 bg-charcoal/70 p-5 sm:p-6">
      <div>
        <p className="text-xs font-semibold uppercase tracking-[0.24em] text-amber">{eyebrow}</p>
        <h3 className="mt-2 font-display text-2xl text-light">{title}</h3>
        <p className="mt-2 max-w-2xl text-sm leading-6 text-fog">{description}</p>
      </div>
      {children}
    </div>
  )
}

function AsidePanel({ title, items, children }: { title: string; items: string[]; children?: ReactNode }) {
  return (
    <div className="rounded-[24px] border border-stone/60 bg-basalt/50 p-5 sm:p-6">
      <div className="mb-4 flex items-center gap-2 text-amber">
        <Volume2 className="h-4 w-4" />
        <h3 className="font-display text-lg text-light">{title}</h3>
      </div>
      <ul className="space-y-3 text-sm text-fog">
        {items.map(item => (
          <li key={item} className="flex gap-3">
            <span className="mt-1 h-1.5 w-1.5 rounded-full bg-amber" />
            <span>{item}</span>
          </li>
        ))}
      </ul>
      {children && <div className="mt-5">{children}</div>}
    </div>
  )
}

function PromptPanel({ title, body }: { title: string; body: string }) {
  return (
    <div className="rounded-2xl border border-amber/20 bg-amber/10 p-5">
      <p className="text-xs uppercase tracking-[0.24em] text-dust">{title}</p>
      <p className="mt-3 font-display text-2xl leading-tight text-light">{body}</p>
    </div>
  )
}

function RecorderPanel({
  meterLevel,
  statusNote,
  isRecording,
  onStart,
  onStop,
  disabled,
  buttonText,
}: {
  meterLevel: number
  statusNote: string
  isRecording: boolean
  onStart: () => void
  onStop: () => void
  disabled: boolean
  buttonText: string
}) {
  return (
    <div className="rounded-[24px] border border-stone/60 bg-obsidian/70 p-5">
      <div className="mb-5 flex flex-wrap items-center justify-between gap-3">
        <div>
          <p className="text-xs uppercase tracking-[0.22em] text-dust">Audio level</p>
          <p className="mt-2 text-sm text-fog">{statusNote}</p>
        </div>
        <span className={`rounded-full px-3 py-1 text-xs font-medium ${isRecording ? 'bg-rose/15 text-rose' : 'bg-sage/15 text-sage'}`}>
          {isRecording ? 'Recording' : 'Ready'}
        </span>
      </div>
      <div className="mb-6 flex h-24 items-end gap-2 rounded-2xl border border-stone/50 bg-charcoal/70 px-4 py-4">
        {Array.from({ length: 18 }).map((_, index) => {
          const threshold = (index + 1) / 18
          const active = Math.max(meterLevel, 0.06) >= threshold
          return (
            <div
              key={index}
              className={`flex-1 rounded-full transition-all duration-100 ${active ? 'bg-gradient-to-t from-amber to-cyan-300' : 'bg-basalt'}`}
              style={{ height: `${28 + Math.max(meterLevel * 100 - index * 3.5, 0)}%` }}
            />
          )
        })}
      </div>
      <div className="flex flex-wrap items-center gap-3">
        {!isRecording ? (
          <button type="button" className={btnPrimary} onClick={onStart} disabled={disabled}>
            <Mic className="h-4 w-4" />
            {buttonText}
          </button>
        ) : (
          <button type="button" className={btnSecondary} onClick={onStop} disabled={disabled}>
            <MicOff className="h-4 w-4" />
            Stop and continue
          </button>
        )}
      </div>
    </div>
  )
}

function SummaryList({
  title,
  emptyMessage,
  loading,
  items,
}: {
  title: string
  emptyMessage: string
  loading: boolean
  items: { id: string; title: string; detail: string; action: () => void }[]
}) {
  return (
    <div className="rounded-2xl border border-stone/60 bg-basalt/40 p-4">
      <h3 className="font-display text-lg text-light">{title}</h3>
      <div className="mt-4 space-y-3">
        {loading && <div className="rounded-xl border border-stone/50 bg-charcoal/60 px-4 py-5 text-sm text-dust">Loading…</div>}
        {!loading && items.length === 0 && <div className="rounded-xl border border-dashed border-stone/50 px-4 py-5 text-sm text-dust">{emptyMessage}</div>}
        {!loading && items.map(item => (
          <div key={item.id} className="flex items-center justify-between gap-4 rounded-xl border border-stone/50 bg-charcoal/70 px-4 py-3">
            <div>
              <p className="font-medium text-light">{item.title}</p>
              <p className="text-xs text-dust">{item.detail}</p>
            </div>
            <button type="button" onClick={item.action} className="rounded-lg border border-stone/50 p-2 text-dust transition-colors hover:border-rose/30 hover:text-rose">
              <Trash2 className="h-4 w-4" />
            </button>
          </div>
        ))}
      </div>
    </div>
  )
}

function MetricCard({ label, value }: { label: string; value: string }) {
  return (
    <div className="rounded-2xl border border-stone/50 bg-charcoal/60 px-4 py-3">
      <p className="text-xs uppercase tracking-[0.22em] text-dust">{label}</p>
      <p className="mt-2 font-display text-2xl text-light">{value}</p>
    </div>
  )
}

function previousStep(step: WizardStep): WizardStep {
  const index = steps.findIndex(item => item.key === step)
  return steps[Math.max(index - 1, 0)].key
}

function flattenSamples(chunks: Float32Array[]): Float32Array {
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
  let offsetResult = 0
  let offsetBuffer = 0

  while (offsetResult < result.length) {
    const nextOffsetBuffer = Math.round((offsetResult + 1) * ratio)
    let accum = 0
    let count = 0
    for (let i = offsetBuffer; i < nextOffsetBuffer && i < buffer.length; i++) {
      accum += buffer[i]
      count++
    }
    result[offsetResult] = count > 0 ? accum / count : 0
    offsetResult++
    offsetBuffer = nextOffsetBuffer
  }

  return result
}

function encodeWav(samples: Float32Array, sampleRate: number): Blob {
  const buffer = new ArrayBuffer(44 + samples.length * 2)
  const view = new DataView(buffer)
  const writeStr = (offset: number, value: string) => {
    for (let i = 0; i < value.length; i++) view.setUint8(offset + i, value.charCodeAt(i))
  }

  writeStr(0, 'RIFF')
  view.setUint32(4, 36 + samples.length * 2, true)
  writeStr(8, 'WAVE')
  writeStr(12, 'fmt ')
  view.setUint32(16, 16, true)
  view.setUint16(20, 1, true)
  view.setUint16(22, 1, true)
  view.setUint32(24, sampleRate, true)
  view.setUint32(28, sampleRate * 2, true)
  view.setUint16(32, 2, true)
  view.setUint16(34, 16, true)
  writeStr(36, 'data')
  view.setUint32(40, samples.length * 2, true)
  for (let i = 0; i < samples.length; i++) {
    const sample = Math.max(-1, Math.min(1, samples[i]))
    view.setInt16(44 + i * 2, sample < 0 ? sample * 0x8000 : sample * 0x7fff, true)
  }

  return new Blob([buffer], { type: 'audio/wav' })
}

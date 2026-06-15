import { useState, useEffect, useCallback } from 'react'
import { useQuery, useMutation } from '@tanstack/react-query'
import { getSettings, updateSettings, saveSettings, cleanDatabase, clearDatabase, type AppSettings } from '../api/settings'
import { ConfirmDialog } from '../components/shared/ConfirmDialog'
import { Spinner } from '../components/shared/Spinner'

export function SettingsPage() {
  const [localSettings, setLocalSettings] = useState<AppSettings | null>(null)
  const [saveFeedback, setSaveFeedback] = useState<string | null>(null)
  const [showCleanConfirm, setShowCleanConfirm] = useState(false)
  const [showClearConfirm, setShowClearConfirm] = useState(false)

  const { data: settings, isLoading } = useQuery({
    queryKey: ['settings'],
    queryFn: getSettings,
  })

  useEffect(() => {
    if (settings && !localSettings) {
      setLocalSettings(settings)
    }
  }, [settings, localSettings])

  const updateMutation = useMutation({
    mutationFn: (s: Partial<AppSettings>) => updateSettings(s),
  })

  const saveMutation = useMutation({
    mutationFn: saveSettings,
    onSuccess: () => {
      setSaveFeedback('Settings saved')
      setTimeout(() => setSaveFeedback(null), 3000)
    },
  })

  const cleanMutation = useMutation({
    mutationFn: cleanDatabase,
    onSuccess: (res) => {
      setSaveFeedback(`Cleaned: ${res.removed} entries removed, ${res.remaining} remaining`)
      setTimeout(() => setSaveFeedback(null), 5000)
    },
  })

  const clearMutation = useMutation({
    mutationFn: clearDatabase,
    onSuccess: () => {
      setSaveFeedback('Database cleared')
      setTimeout(() => setSaveFeedback(null), 3000)
    },
  })

  const handleChange = useCallback((key: keyof AppSettings, value: unknown) => {
    if (!localSettings) return
    const updated = { ...localSettings, [key]: value }
    setLocalSettings(updated)
    updateMutation.mutate(updated)
  }, [localSettings, updateMutation])

  const handleSave = useCallback(() => {
    if (localSettings) {
      updateMutation.mutate(localSettings, {
        onSuccess: () => saveMutation.mutate(),
      })
    }
  }, [localSettings, updateMutation, saveMutation])

  if (isLoading || !localSettings) {
    return (
      <div style={{ display: 'flex', alignItems: 'center', gap: '0.75rem', padding: '3rem', color: 'var(--text-muted)' }}>
        <Spinner size={20} />
        Loading settings...
      </div>
    )
  }

  return (
    <div className="settings-page" style={{ maxWidth: 720, animation: 'fadeInUp 0.4s ease both' }}>
      {/* Header */}
      <div style={{
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'space-between',
        marginBottom: '1.75rem',
        paddingBottom: '1rem',
        borderBottom: '1px solid var(--border-subtle)',
      }}>
        <h1 style={{
          fontFamily: 'var(--font-display)',
          fontSize: '1.6rem',
          fontWeight: 700,
          margin: 0,
          color: 'var(--text-primary)',
          letterSpacing: '0.02em',
        }}>
          Settings
        </h1>
        <div style={{ display: 'flex', alignItems: 'center', gap: '0.75rem' }}>
          {saveFeedback && (
            <span style={{
              fontSize: '0.78rem',
              color: 'var(--accent-success-text)',
              fontFamily: 'var(--font-mono)',
              background: 'rgba(34, 197, 94, 0.08)',
              padding: '0.3rem 0.7rem',
              borderRadius: 'var(--radius-sm)',
              border: '1px solid rgba(34, 197, 94, 0.15)',
            }}>
              {saveFeedback}
            </span>
          )}
          <button
            onClick={handleSave}
            disabled={saveMutation.isPending}
            style={{
              padding: '0.55rem 1.5rem',
              borderRadius: 'var(--radius-md)',
              border: 'none',
              background: 'linear-gradient(135deg, var(--accent-primary), #0284c7)',
              color: '#fff',
              cursor: saveMutation.isPending ? 'not-allowed' : 'pointer',
              fontSize: '0.82rem',
              fontWeight: 600,
              fontFamily: 'var(--font-sans)',
              display: 'flex',
              alignItems: 'center',
              gap: '0.4rem',
              boxShadow: '0 0 20px var(--accent-primary-glow), var(--shadow-sm)',
              transition: 'all var(--transition-base)',
              letterSpacing: '0.03em',
              opacity: saveMutation.isPending ? 0.7 : 1,
            }}
          >
            {saveMutation.isPending && <Spinner size={12} />}
            Save
          </button>
        </div>
      </div>

      {/* Similarity */}
      <Section title="Similarity">
        <SliderField label="Similarity Threshold" value={localSettings.threshhold} min={0} max={100} step={1}
          onChange={v => handleChange('threshhold', v)} />
        <SliderField label="Percent Match" value={localSettings.percent} min={0} max={100} step={0.5}
          onChange={v => handleChange('percent', v)} format={v => `${v}%`} />
        <SliderField label="Duration Tolerance (%)" value={localSettings.percentDurationDifference} min={0} max={100} step={1}
          onChange={v => handleChange('percentDurationDifference', v)} format={v => `${v}%`} />
        <ToggleField label="Compare Horizontally Flipped" value={localSettings.compareHorizontallyFlipped}
          onChange={v => handleChange('compareHorizontallyFlipped', v)} />
        <ToggleField label="Ignore Black Pixels" value={localSettings.ignoreBlackPixels}
          onChange={v => handleChange('ignoreBlackPixels', v)} />
        <ToggleField label="Ignore White Pixels" value={localSettings.ignoreWhitePixels}
          onChange={v => handleChange('ignoreWhitePixels', v)} />
      </Section>

      {/* Scanning */}
      <Section title="Scanning">
        <ToggleField label="Include Sub-Directories" value={localSettings.includeSubDirectories}
          onChange={v => handleChange('includeSubDirectories', v)} />
        <ToggleField label="Include Images" value={localSettings.includeImages}
          onChange={v => handleChange('includeImages', v)} />
        <ToggleField label="Use Perceptual Hashing" value={localSettings.usePHashing}
          onChange={v => handleChange('usePHashing', v)} />
        <ToggleField label="Ignore Read-Only Folders" value={localSettings.ignoreReadOnlyFolders}
          onChange={v => handleChange('ignoreReadOnlyFolders', v)} />
        <ToggleField label="Ignore Reparse Points" value={localSettings.ignoreReparsePoints}
          onChange={v => handleChange('ignoreReparsePoints', v)} />
        <ToggleField label="Exclude Hard Links" value={localSettings.excludeHardLinks}
          onChange={v => handleChange('excludeHardLinks', v)} />
        <ToggleField label="Use EXIF Creation Date" value={localSettings.useExifCreationDate}
          onChange={v => handleChange('useExifCreationDate', v)} />
        <ToggleField label="Include Non-Existing Files" value={localSettings.includeNonExistingFiles}
          onChange={v => handleChange('includeNonExistingFiles', v)} />
        <ToggleField label="Scan Against Entire Database" value={localSettings.scanAgainstEntireDatabase}
          onChange={v => handleChange('scanAgainstEntireDatabase', v)} />
        <SliderField label="Max Degree of Parallelism" value={localSettings.maxDegreeOfParallelism} min={1} max={32} step={1}
          onChange={v => handleChange('maxDegreeOfParallelism', v)} />
        <SliderField label="Thumbnail Count" value={localSettings.thumbnailCount} min={1} max={10} step={1}
          onChange={v => handleChange('thumbnailCount', v)} />
      </Section>

      {/* File Filters */}
      <Section title="File Filters">
        <ToggleField label="Filter by File Size" value={localSettings.filterByFileSize}
          onChange={v => handleChange('filterByFileSize', v)} />
        {localSettings.filterByFileSize && (
          <>
            <SliderField label="Minimum File Size (MB)" value={localSettings.minimumFileSize} min={0} max={10000} step={1}
              onChange={v => handleChange('minimumFileSize', v)} format={v => `${v} MB`} />
            <SliderField label="Maximum File Size (MB)" value={localSettings.maximumFileSize} min={0} max={100000} step={1}
              onChange={v => handleChange('maximumFileSize', v)} format={v => `${v} MB`} />
          </>
        )}
        <ToggleField label="Filter by Path Contains" value={localSettings.filterByFilePathContains}
          onChange={v => handleChange('filterByFilePathContains', v)} />
        <ToggleField label="Filter by Path Not Contains" value={localSettings.filterByFilePathNotContains}
          onChange={v => handleChange('filterByFilePathNotContains', v)} />
        <SliderField label="Duration Min Difference (s)" value={localSettings.durationDifferenceMinSeconds} min={0} max={300} step={0.5}
          onChange={v => handleChange('durationDifferenceMinSeconds', v)} />
        <SliderField label="Duration Max Difference (s)" value={localSettings.durationDifferenceMaxSeconds} min={0} max={300} step={0.5}
          onChange={v => handleChange('durationDifferenceMaxSeconds', v)} />
      </Section>

      {/* FFmpeg */}
      <Section title="FFmpeg">
        <ToggleField label="Use Native FFmpeg Binding" value={localSettings.useNativeFfmpegBinding}
          onChange={v => handleChange('useNativeFfmpegBinding', v)} />
        <ToggleField label="Extended FFTools Logging" value={localSettings.extendedFFToolsLogging}
          onChange={v => handleChange('extendedFFToolsLogging', v)} />
        <ToggleField label="Always Retry Failed Sampling" value={localSettings.alwaysRetryFailedSampling}
          onChange={v => handleChange('alwaysRetryFailedSampling', v)} />
        <ToggleField label="Log Excluded Files" value={localSettings.logExcludedFiles}
          onChange={v => handleChange('logExcludedFiles', v)} />
        <SelectField label="Hardware Acceleration" value={localSettings.hardwareAccelerationMode}
          options={['Auto', 'QSV', 'CUDA', 'D3D11VA', 'VAAPI', 'None']}
          onChange={v => handleChange('hardwareAccelerationMode', v)} />
        <TextField label="Custom FF Arguments" value={localSettings.customFFArguments}
          onChange={v => handleChange('customFFArguments', v)} placeholder="e.g. -hwaccel cuda" />
        <SliderField label="Max Sampling Duration (s)" value={localSettings.maxSamplingDurationSeconds} min={0} max={600} step={1}
          onChange={v => handleChange('maxSamplingDurationSeconds', v)} />
      </Section>

      {/* Partial Clip Detection */}
      <Section title="Partial Clip Detection">
        <ToggleField label="Enable Partial Clip Detection" value={localSettings.enablePartialClipDetection}
          onChange={v => handleChange('enablePartialClipDetection', v)} />
        {localSettings.enablePartialClipDetection && (
          <>
            <SliderField label="Min Clip Ratio" value={localSettings.partialClipMinRatio} min={0} max={1} step={0.01}
              onChange={v => handleChange('partialClipMinRatio', v)} format={v => `${(v * 100).toFixed(0)}%`} />
            <SliderField label="Similarity Threshold" value={localSettings.partialClipSimilarityThreshold} min={0} max={1} step={0.01}
              onChange={v => handleChange('partialClipSimilarityThreshold', v)} format={v => `${(v * 100).toFixed(0)}%`} />
            <ToggleField label="Require Visual Match" value={localSettings.partialClipRequireVisualMatch}
              onChange={v => handleChange('partialClipRequireVisualMatch', v)} />
            <SliderField label="Visual Threshold" value={localSettings.partialClipVisualThreshold} min={0} max={1} step={0.01}
              onChange={v => handleChange('partialClipVisualThreshold', v)} format={v => `${(v * 100).toFixed(0)}%`} />
          </>
        )}
      </Section>

      {/* WebUI Thumbnails */}
      <Section title="WebUI Thumbnails">
        <ToggleField label="Auto-Load Thumbnails" value={localSettings.autoLoadThumbnails}
          onChange={v => handleChange('autoLoadThumbnails', v)} />
        <SliderField label="Thumbnail Width (px)" value={localSettings.thumbnailWidth} min={48} max={960} step={16}
          onChange={v => handleChange('thumbnailWidth', v)} format={v => `${v}px`} />
        <SliderField label="JPEG Quality" value={localSettings.thumbnailJpegQuality} min={10} max={95} step={5}
          onChange={v => handleChange('thumbnailJpegQuality', v)} />
      </Section>

      {/* Database */}
      <Section title="Database">
        <TextField label="Custom Database Folder" value={localSettings.customDatabaseFolder}
          onChange={v => handleChange('customDatabaseFolder', v)} placeholder="Leave empty for default" />
        <SliderField label="Checkpoint Interval (min)" value={localSettings.databaseCheckpointIntervalMinutes} min={1} max={60} step={1}
          onChange={v => handleChange('databaseCheckpointIntervalMinutes', v)} />
        <div style={{ display: 'flex', gap: '0.6rem', marginTop: '0.75rem' }}>
          <button
            onClick={() => setShowCleanConfirm(true)}
            disabled={cleanMutation.isPending}
            style={dangerBtnStyle}
          >
            {cleanMutation.isPending ? 'Cleaning...' : 'Clean Database'}
          </button>
          <button
            onClick={() => setShowClearConfirm(true)}
            disabled={clearMutation.isPending}
            style={dangerBtnStyle}
          >
            {clearMutation.isPending ? 'Clearing...' : 'Clear Database'}
          </button>
        </div>
      </Section>

      <ConfirmDialog
        open={showCleanConfirm}
        title="Clean Database"
        message="Remove database entries for files that no longer exist on disk?"
        confirmLabel="Clean"
        variant="warning"
        onConfirm={() => { cleanMutation.mutate(); setShowCleanConfirm(false) }}
        onCancel={() => setShowCleanConfirm(false)}
      />

      <ConfirmDialog
        open={showClearConfirm}
        title="Clear Database"
        message="This will permanently delete ALL entries from the scan database. This cannot be undone."
        confirmLabel="Clear All"
        variant="danger"
        onConfirm={() => { clearMutation.mutate(); setShowClearConfirm(false) }}
        onCancel={() => setShowClearConfirm(false)}
      />
    </div>
  )
}

const dangerBtnStyle: React.CSSProperties = {
  padding: '0.45rem 1rem',
  borderRadius: 'var(--radius-md)',
  border: '1px solid var(--accent-error-border)',
  background: 'var(--accent-error-bg)',
  color: 'var(--accent-danger-text)',
  cursor: 'pointer',
  fontSize: '0.78rem',
  fontWeight: 500,
  fontFamily: 'var(--font-sans)',
  transition: 'all var(--transition-fast)',
  letterSpacing: '0.02em',
}

function Section({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <div style={{
      background: 'var(--bg-surface)',
      border: '1px solid var(--border-default)',
      borderLeft: '3px solid var(--accent-primary)',
      borderRadius: 'var(--radius-lg)',
      padding: '1.15rem 1.35rem',
      marginBottom: '0.85rem',
      boxShadow: 'var(--shadow-sm)',
      transition: 'box-shadow var(--transition-base), border-color var(--transition-base)',
    }}>
      <h2 style={{
        margin: '0 0 0.85rem',
        fontFamily: 'var(--font-display)',
        fontSize: '0.82rem',
        fontWeight: 600,
        color: 'var(--accent-primary)',
        textTransform: 'uppercase',
        letterSpacing: '0.12em',
      }}>
        {title}
      </h2>
      <div style={{ display: 'flex', flexDirection: 'column', gap: '0.65rem' }}>
        {children}
      </div>
    </div>
  )
}

interface FieldProps {
  label: string
}

function ToggleField({ label, value, onChange }: FieldProps & { value: boolean; onChange: (v: boolean) => void }) {
  return (
    <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: '1rem' }}>
      <span style={{ fontSize: '0.84rem', color: 'var(--text-secondary)', fontFamily: 'var(--font-sans)' }}>{label}</span>
      <button
        onClick={() => onChange(!value)}
        style={{
          width: 40,
          height: 22,
          borderRadius: 11,
          border: 'none',
          background: value ? 'var(--accent-primary)' : 'var(--bg-input)',
          cursor: 'pointer',
          position: 'relative',
          transition: 'background var(--transition-base), box-shadow var(--transition-base)',
          flexShrink: 0,
          boxShadow: value ? '0 0 12px var(--accent-primary-glow)' : 'inset 0 1px 3px rgba(0,0,0,0.3)',
        }}
      >
        <div style={{
          width: 16,
          height: 16,
          borderRadius: '50%',
          background: value ? '#fff' : 'var(--text-muted)',
          position: 'absolute',
          top: 3,
          left: value ? 21 : 3,
          transition: 'left var(--transition-base), background var(--transition-base), box-shadow var(--transition-base)',
          boxShadow: value ? '0 0 6px rgba(14, 165, 233, 0.4)' : 'none',
        }} />
      </button>
    </div>
  )
}

function SliderField({ label, value, min, max, step, onChange, format }: FieldProps & {
  value: number; min: number; max: number; step: number
  onChange: (v: number) => void; format?: (v: number) => string
}) {
  const pct = ((value - min) / (max - min)) * 100
  return (
    <div>
      <div style={{ display: 'flex', justifyContent: 'space-between', marginBottom: '0.35rem', alignItems: 'baseline' }}>
        <span style={{ fontSize: '0.84rem', color: 'var(--text-secondary)', fontFamily: 'var(--font-sans)' }}>{label}</span>
        <span style={{
          fontSize: '0.78rem',
          color: 'var(--accent-primary)',
          fontFamily: 'var(--font-mono)',
          background: 'var(--accent-primary-glow)',
          padding: '0.1rem 0.5rem',
          borderRadius: 'var(--radius-sm)',
          fontWeight: 500,
        }}>
          {format ? format(value) : value}
        </span>
      </div>
      <input
        type="range"
        min={min}
        max={max}
        step={step}
        value={value}
        onChange={e => onChange(Number(e.target.value))}
        style={{
          width: '100%',
          accentColor: 'var(--accent-primary)',
          cursor: 'pointer',
          height: 4,
          background: `linear-gradient(to right, var(--accent-primary) 0%, var(--accent-primary) ${pct}%, var(--border-default) ${pct}%, var(--border-default) 100%)`,
          borderRadius: 2,
          appearance: 'none',
          WebkitAppearance: 'none',
          outline: 'none',
        } as React.CSSProperties}
      />
    </div>
  )
}

function SelectField({ label, value, options, onChange }: FieldProps & {
  value: string; options: string[]; onChange: (v: string) => void
}) {
  return (
    <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: '1rem' }}>
      <span style={{ fontSize: '0.84rem', color: 'var(--text-secondary)', fontFamily: 'var(--font-sans)' }}>{label}</span>
      <select
        value={value}
        onChange={e => onChange(e.target.value)}
        style={{
          padding: '0.4rem 0.65rem',
          border: '1px solid var(--border-input)',
          borderRadius: 'var(--radius-md)',
          background: 'var(--bg-input)',
          color: 'var(--text-primary)',
          fontSize: '0.8rem',
          fontFamily: 'var(--font-mono)',
          cursor: 'pointer',
          outline: 'none',
          transition: 'border-color var(--transition-fast), box-shadow var(--transition-fast)',
          minWidth: 120,
        }}
      >
        {options.map(opt => (
          <option key={opt} value={opt}>{opt}</option>
        ))}
      </select>
    </div>
  )
}

function TextField({ label, value, onChange, placeholder }: FieldProps & {
  value: string; onChange: (v: string) => void; placeholder?: string
}) {
  return (
    <div>
      <div style={{ marginBottom: '0.35rem' }}>
        <span style={{ fontSize: '0.84rem', color: 'var(--text-secondary)', fontFamily: 'var(--font-sans)' }}>{label}</span>
      </div>
      <input
        value={value}
        onChange={e => onChange(e.target.value)}
        placeholder={placeholder}
        style={{
          width: '100%',
          padding: '0.5rem 0.7rem',
          border: '1px solid var(--border-input)',
          borderRadius: 'var(--radius-md)',
          background: 'var(--bg-input)',
          color: 'var(--text-primary)',
          fontSize: '0.84rem',
          fontFamily: 'var(--font-mono)',
          outline: 'none',
          transition: 'border-color var(--transition-fast), box-shadow var(--transition-fast)',
          boxSizing: 'border-box',
        }}
      />
    </div>
  )
}

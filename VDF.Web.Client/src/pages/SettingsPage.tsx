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
          onChange={v => handleChange('threshhold', v)} description="Hash difference threshold. Lower = stricter matching (fewer false positives)." />
        <SliderField label="Percent Match" value={localSettings.percent} min={0} max={100} step={0.5}
          onChange={v => handleChange('percent', v)} format={v => `${v}%`} description="Minimum similarity percentage to report as duplicate." />
        <SliderField label="Duration Tolerance (%)" value={localSettings.percentDurationDifference} min={0} max={100} step={1}
          onChange={v => handleChange('percentDurationDifference', v)} format={v => `${v}%`} description="Maximum allowed duration difference between files to be considered duplicates." />
        <ToggleField label="Compare Horizontally Flipped" value={localSettings.compareHorizontallyFlipped}
          onChange={v => handleChange('compareHorizontallyFlipped', v)} description="Also detect duplicates that are horizontally mirrored." />
        <ToggleField label="Ignore Black Pixels" value={localSettings.ignoreBlackPixels}
          onChange={v => handleChange('ignoreBlackPixels', v)} description="Exclude black pixels from similarity comparison." />
        <ToggleField label="Ignore White Pixels" value={localSettings.ignoreWhitePixels}
          onChange={v => handleChange('ignoreWhitePixels', v)} description="Exclude white pixels from similarity comparison." />
      </Section>

      {/* Scanning */}
      <Section title="Scanning">
        <ToggleField label="Include Sub-Directories" value={localSettings.includeSubDirectories}
          onChange={v => handleChange('includeSubDirectories', v)} description="Recursively scan all subdirectories." />
        <ToggleField label="Include Images" value={localSettings.includeImages}
          onChange={v => handleChange('includeImages', v)} description="Also scan image files for duplicates." />
        <ToggleField label="Use Perceptual Hashing" value={localSettings.usePHashing}
          onChange={v => handleChange('usePHashing', v)} description="Use perceptual hashing for better detection of resized/compressed duplicates." />
        <ToggleField label="Ignore Read-Only Folders" value={localSettings.ignoreReadOnlyFolders}
          onChange={v => handleChange('ignoreReadOnlyFolders', v)} description="Skip folders that cannot be written to." />
        <ToggleField label="Ignore Reparse Points" value={localSettings.ignoreReparsePoints}
          onChange={v => handleChange('ignoreReparsePoints', v)} description="Skip symbolic links and junction points." />
        <ToggleField label="Exclude Hard Links" value={localSettings.excludeHardLinks}
          onChange={v => handleChange('excludeHardLinks', v)} description="Skip hard links to already-scanned files." />
        <ToggleField label="Use EXIF Creation Date" value={localSettings.useExifCreationDate}
          onChange={v => handleChange('useExifCreationDate', v)} description="Use EXIF metadata for image creation dates." />
        <ToggleField label="Include Non-Existing Files" value={localSettings.includeNonExistingFiles}
          onChange={v => handleChange('includeNonExistingFiles', v)} description="Compare against database entries whose files no longer exist." />
        <ToggleField label="Scan Against Entire Database" value={localSettings.scanAgainstEntireDatabase}
          onChange={v => handleChange('scanAgainstEntireDatabase', v)} description="Compare new files against all known entries, not just scanned folders." />
        <SliderField label="Max Degree of Parallelism" value={localSettings.maxDegreeOfParallelism} min={1} max={32} step={1}
          onChange={v => handleChange('maxDegreeOfParallelism', v)} description="Number of parallel scanning threads. Higher = faster but more CPU usage." />
        <SliderField label="Thumbnail Count" value={localSettings.thumbnailCount} min={1} max={10} step={1}
          onChange={v => handleChange('thumbnailCount', v)} description="Number of frame samples per video for comparison." />
      </Section>

      {/* File Filters */}
      <Section title="File Filters">
        <ToggleField label="Filter by File Size" value={localSettings.filterByFileSize}
          onChange={v => handleChange('filterByFileSize', v)} description="Only scan files within a size range." />
        {localSettings.filterByFileSize && (
          <>
            <SliderField label="Minimum File Size (MB)" value={localSettings.minimumFileSize} min={0} max={10000} step={1}
              onChange={v => handleChange('minimumFileSize', v)} format={v => `${v} MB`} />
            <SliderField label="Maximum File Size (MB)" value={localSettings.maximumFileSize} min={0} max={100000} step={1}
              onChange={v => handleChange('maximumFileSize', v)} format={v => `${v} MB`} />
          </>
        )}
        <ToggleField label="Filter by Path Contains" value={localSettings.filterByFilePathContains}
          onChange={v => handleChange('filterByFilePathContains', v)} description="Only scan files whose path contains specific text." />
        <ToggleField label="Filter by Path Not Contains" value={localSettings.filterByFilePathNotContains}
          onChange={v => handleChange('filterByFilePathNotContains', v)} description="Skip files whose path contains specific text." />
        <SliderField label="Duration Min Difference (s)" value={localSettings.durationDifferenceMinSeconds} min={0} max={300} step={0.5}
          onChange={v => handleChange('durationDifferenceMinSeconds', v)} description="Minimum duration difference to consider (0 = disabled)." />
        <SliderField label="Duration Max Difference (s)" value={localSettings.durationDifferenceMaxSeconds} min={0} max={300} step={0.5}
          onChange={v => handleChange('durationDifferenceMaxSeconds', v)} description="Maximum duration difference allowed (0 = no limit)." />
      </Section>

      {/* FFmpeg */}
      <Section title="FFmpeg">
        <ToggleField label="Use Native FFmpeg Binding" value={localSettings.useNativeFfmpegBinding}
          onChange={v => handleChange('useNativeFfmpegBinding', v)} description="Use FFmpeg.AutoGen native bindings instead of CLI (faster but requires FFmpeg shared libraries)." />
        <ToggleField label="Extended FFTools Logging" value={localSettings.extendedFFToolsLogging}
          onChange={v => handleChange('extendedFFToolsLogging', v)} description="Log detailed FFmpeg/FFprobe command output for debugging." />
        <ToggleField label="Always Retry Failed Sampling" value={localSettings.alwaysRetryFailedSampling}
          onChange={v => handleChange('alwaysRetryFailedSampling', v)} description="Re-attempt frame sampling on files that failed previously." />
        <ToggleField label="Log Excluded Files" value={localSettings.logExcludedFiles}
          onChange={v => handleChange('logExcludedFiles', v)} description="Log files skipped by filters (increases log size)." />
        <SelectField label="Hardware Acceleration" value={localSettings.hardwareAccelerationMode}
          options={['Auto', 'QSV', 'CUDA', 'D3D11VA', 'VAAPI', 'None']}
          onChange={v => handleChange('hardwareAccelerationMode', v)} description="GPU acceleration for video decoding (requires compatible hardware)." />
        <TextField label="Custom FF Arguments" value={localSettings.customFFArguments}
          onChange={v => handleChange('customFFArguments', v)} placeholder="e.g. -hwaccel cuda" description="Additional FFmpeg command-line arguments." />
        <SliderField label="Max Sampling Duration (s)" value={localSettings.maxSamplingDurationSeconds} min={0} max={600} step={1}
          onChange={v => handleChange('maxSamplingDurationSeconds', v)} description="Maximum seconds of video to sample (0 = entire video)." />
      </Section>

      {/* Partial Clip Detection */}
      <Section title="Partial Clip Detection">
        <ToggleField label="Enable Partial Clip Detection" value={localSettings.enablePartialClipDetection}
          onChange={v => handleChange('enablePartialClipDetection', v)} description="Detect when a shorter video is a clip from a longer one (audio fingerprinting)." />
        {localSettings.enablePartialClipDetection && (
          <>
            <SliderField label="Min Clip Ratio" value={localSettings.partialClipMinRatio} min={0} max={1} step={0.01}
              onChange={v => handleChange('partialClipMinRatio', v)} format={v => `${(v * 100).toFixed(0)}%`}
              description="Minimum clip duration as percentage of source (e.g. 10% means clip must be at least 10% of source)." />
            <SliderField label="Similarity Threshold" value={localSettings.partialClipSimilarityThreshold} min={0} max={1} step={0.01}
              onChange={v => handleChange('partialClipSimilarityThreshold', v)} format={v => `${(v * 100).toFixed(0)}%`}
              description="Minimum audio fingerprint similarity for a match (higher = fewer false positives)." />
            <ToggleField label="Require Visual Match" value={localSettings.partialClipRequireVisualMatch}
              onChange={v => handleChange('partialClipRequireVisualMatch', v)} description="Also verify visual similarity at the matched offset." />
            <SliderField label="Visual Threshold" value={localSettings.partialClipVisualThreshold} min={0} max={1} step={0.01}
              onChange={v => handleChange('partialClipVisualThreshold', v)} format={v => `${(v * 100).toFixed(0)}%`}
              description="Minimum visual similarity for confirmation (when enabled)." />
          </>
        )}
      </Section>

      {/* WebUI Thumbnails */}
      <Section title="WebUI Thumbnails">
        <ToggleField label="Auto-Load Thumbnails" value={localSettings.autoLoadThumbnails}
          onChange={v => handleChange('autoLoadThumbnails', v)} description="Automatically load thumbnails on the results page." />
        <SliderField label="Thumbnail Width (px)" value={localSettings.thumbnailWidth} min={48} max={960} step={16}
          onChange={v => handleChange('thumbnailWidth', v)} format={v => `${v}px`}
          description="Thumbnail resolution. Lower = less memory usage, more pixelated." />
        <SliderField label="JPEG Quality" value={localSettings.thumbnailJpegQuality} min={10} max={95} step={5}
          onChange={v => handleChange('thumbnailJpegQuality', v)}
          description="JPEG compression quality. Lower = smaller files, more artifacts." />
      </Section>

      {/* Database */}
      <Section title="Database">
        <TextField label="Custom Database Folder" value={localSettings.customDatabaseFolder}
          onChange={v => handleChange('customDatabaseFolder', v)} placeholder="Leave empty for default"
          description="Custom location for the scan database (default: system config folder)." />
        <SliderField label="Checkpoint Interval (min)" value={localSettings.databaseCheckpointIntervalMinutes} min={1} max={60} step={1}
          onChange={v => handleChange('databaseCheckpointIntervalMinutes', v)}
          description="How often to save scan progress during scanning (prevents data loss on crash)." />
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
  description?: string
}

function ToggleField({ label, value, onChange, description }: FieldProps & { value: boolean; onChange: (v: boolean) => void }) {
  return (
    <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: '1rem' }}>
      <div>
        <span style={{ fontSize: '0.84rem', color: 'var(--text-secondary)', fontFamily: 'var(--font-sans)' }}>{label}</span>
        {description && <div style={{ fontSize: '0.72rem', color: 'var(--text-dim)', marginTop: '0.15rem' }}>{description}</div>}
      </div>
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

function SliderField({ label, value, min, max, step, onChange, format, description }: FieldProps & {
  value: number; min: number; max: number; step: number
  onChange: (v: number) => void; format?: (v: number) => string
}) {
  const pct = ((value - min) / (max - min)) * 100
  return (
    <div>
      <div style={{ display: 'flex', justifyContent: 'space-between', marginBottom: '0.15rem', alignItems: 'baseline' }}>
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
      {description && <div style={{ fontSize: '0.72rem', color: 'var(--text-dim)', marginBottom: '0.35rem' }}>{description}</div>}
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

function SelectField({ label, value, options, onChange, description }: FieldProps & {
  value: string; options: string[]; onChange: (v: string) => void
}) {
  return (
    <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: '1rem' }}>
      <div>
        <span style={{ fontSize: '0.84rem', color: 'var(--text-secondary)', fontFamily: 'var(--font-sans)' }}>{label}</span>
        {description && <div style={{ fontSize: '0.72rem', color: 'var(--text-dim)', marginTop: '0.15rem' }}>{description}</div>}
      </div>
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

function TextField({ label, value, onChange, placeholder, description }: FieldProps & {
  value: string; onChange: (v: string) => void; placeholder?: string
}) {
  return (
    <div>
      <div style={{ marginBottom: '0.15rem' }}>
        <span style={{ fontSize: '0.84rem', color: 'var(--text-secondary)', fontFamily: 'var(--font-sans)' }}>{label}</span>
        {description && <div style={{ fontSize: '0.72rem', color: 'var(--text-dim)', marginTop: '0.15rem' }}>{description}</div>}
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

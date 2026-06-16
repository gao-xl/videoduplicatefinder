import { useState, useEffect, useCallback, useRef } from 'react'
import { useQuery, useMutation } from '@tanstack/react-query'
import { getSettings, updateSettings, saveSettings, cleanDatabase, clearDatabase, type AppSettings } from '../api/settings'
import { ConfirmDialog } from '../components/shared/ConfirmDialog'
import { Spinner } from '../components/shared/Spinner'
import { useI18n } from '../i18n/i18n'
import { availableLanguages } from '../i18n/i18n'
import { useTheme } from '../contexts/useTheme'

type TabId = 'scanning' | 'directories' | 'filters' | 'processing' | 'appearance' | 'database'

const TABS: { id: TabId; labelKey: string }[] = [
  { id: 'scanning', labelKey: 'Scanning' },
  { id: 'directories', labelKey: 'Directories' },
  { id: 'filters', labelKey: 'File Filters' },
  { id: 'processing', labelKey: 'Processing' },
  { id: 'appearance', labelKey: 'Appearance' },
  { id: 'database', labelKey: 'Database' },
]

// Debounce hook for settings updates
function useDebouncedSettings(updateFn: (data: Partial<AppSettings>) => void) {
  const timeoutRef = useRef<ReturnType<typeof setTimeout> | null>(null)
  const pendingRef = useRef<Partial<AppSettings> | null>(null)

  const debouncedUpdate = useCallback((settings: Partial<AppSettings>) => {
    pendingRef.current = { ...pendingRef.current, ...settings }

    if (timeoutRef.current) {
      clearTimeout(timeoutRef.current)
    }

    timeoutRef.current = setTimeout(() => {
      if (pendingRef.current) {
        updateFn(pendingRef.current)
        pendingRef.current = null
      }
      timeoutRef.current = null
    }, 500) // 500ms debounce
  }, [updateFn])

  // Cleanup on unmount
  useEffect(() => {
    return () => {
      if (timeoutRef.current) {
        clearTimeout(timeoutRef.current)
      }
    }
  }, [])

  return debouncedUpdate
}

export function SettingsPage() {
  const { t, lang, setLang } = useI18n()
  const { theme, toggleTheme } = useTheme()
  const [activeTab, setActiveTab] = useState<TabId>('scanning')
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

  const debouncedUpdate = useDebouncedSettings(updateMutation.mutate)

  const saveMutation = useMutation({
    mutationFn: saveSettings,
    onSuccess: () => {
      setSaveFeedback(t('Settings saved'))
      setTimeout(() => setSaveFeedback(null), 3000)
    },
  })

  const cleanMutation = useMutation({
    mutationFn: cleanDatabase,
    onSuccess: (res) => {
      setSaveFeedback(`${t('Cleaned')}: ${res.removed} ${t('entries removed')}, ${res.remaining} ${t('remaining')}`)
      setTimeout(() => setSaveFeedback(null), 5000)
    },
  })

  const clearMutation = useMutation({
    mutationFn: clearDatabase,
    onSuccess: () => {
      setSaveFeedback(t('Database cleared'))
      setTimeout(() => setSaveFeedback(null), 3000)
    },
  })

  const handleChange = useCallback((key: keyof AppSettings, value: unknown) => {
    if (!localSettings) return
    const updated = { ...localSettings, [key]: value }
    setLocalSettings(updated)
    debouncedUpdate({ [key]: value })
  }, [localSettings, debouncedUpdate])

  const handleLanguageChange = useCallback((languageCode: string) => {
    setLang(languageCode as any)
    handleChange('languageCode', languageCode)
  }, [setLang, handleChange])

  const handleSave = useCallback(() => {
    if (localSettings) {
      updateMutation.mutate(localSettings, {
        onSuccess: () => saveMutation.mutate(),
      })
    }
  }, [localSettings, updateMutation, saveMutation])

  const handleListChange = useCallback((key: 'includeList' | 'blackList' | 'filePathContainsTexts' | 'filePathNotContainsTexts', raw: string) => {
    const items = raw.split('\n').map(s => s.trim()).filter(Boolean)
    handleChange(key, items)
  }, [handleChange])

  if (isLoading || !localSettings) {
    return (
      <div style={{ display: 'flex', alignItems: 'center', gap: '0.75rem', padding: '3rem', color: 'var(--text-muted)' }}>
        <Spinner size={20} />
        {t('Loading settings...')}
      </div>
    )
  }

  return (
    <div className="settings-page" style={{ animation: 'fadeInUp 0.4s ease both', height: '100%' }}>
      {/* Top bar with Save */}
      <div style={{
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'flex-end',
        gap: '0.75rem',
        padding: '0.6rem 1rem',
        borderBottom: '1px solid var(--border-default)',
        background: 'var(--bg-surface)',
      }}>
        {saveFeedback && (
          <span style={{
            fontSize: '11px',
            color: 'var(--accent-success-text)',
            fontFamily: 'var(--font-mono)',
            background: 'rgba(34, 197, 94, 0.08)',
            padding: '0.2rem 0.6rem',
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
            padding: '0.4rem 1.2rem',
            borderRadius: 'var(--radius-md)',
            border: 'none',
            background: 'var(--accent-primary)',
            color: '#fff',
            cursor: saveMutation.isPending ? 'not-allowed' : 'pointer',
            fontSize: '11px',
            fontWeight: 600,
            fontFamily: 'var(--font-sans)',
            display: 'flex',
            alignItems: 'center',
            gap: '0.35rem',
            transition: 'all var(--transition-fast)',
            opacity: saveMutation.isPending ? 0.7 : 1,
          }}
        >
          {saveMutation.isPending && <Spinner size={11} />}
          {t('Save')}
        </button>
      </div>

      {/* Main layout: sidebar tabs + content */}
      <div style={{ display: 'flex', height: 'calc(100% - 41px)' }}>
        {/* Sidebar tabs */}
        <div style={{
          width: 150,
          minWidth: 150,
          background: 'var(--bg-sidebar)',
          borderRight: '1px solid var(--border-default)',
          padding: '0.5rem 0.4rem',
          display: 'flex',
          flexDirection: 'column',
          gap: '2px',
          overflowY: 'auto',
        }}>
          {TABS.map(tab => (
            <button
              key={tab.id}
              className={`nav-tab${activeTab === tab.id ? ' active' : ''}`}
              onClick={() => setActiveTab(tab.id)}
            >
              {t(tab.labelKey)}
            </button>
          ))}
        </div>

        {/* Content area */}
        <div style={{
          flex: 1,
          overflowY: 'auto',
          padding: '0.75rem 1rem',
          background: 'var(--bg-content)',
        }}>
          {activeTab === 'scanning' && (
            <ScanningTab settings={localSettings} onChange={handleChange} t={t} />
          )}
          {activeTab === 'directories' && (
            <DirectoriesTab settings={localSettings} onListChange={handleListChange} t={t} />
          )}
          {activeTab === 'filters' && (
            <FiltersTab settings={localSettings} onChange={handleChange} onListChange={handleListChange} t={t} />
          )}
          {activeTab === 'processing' && (
            <ProcessingTab settings={localSettings} onChange={handleChange} t={t} />
          )}
          {activeTab === 'appearance' && (
            <AppearanceTab
              settings={localSettings}
              onChange={handleChange}
              onLanguageChange={handleLanguageChange}
              lang={lang}
              theme={theme}
              toggleTheme={toggleTheme}
              t={t}
            />
          )}
          {activeTab === 'database' && (
            <DatabaseTab
              settings={localSettings}
              onChange={handleChange}
              onClean={() => setShowCleanConfirm(true)}
              onClear={() => setShowClearConfirm(true)}
              cleanPending={cleanMutation.isPending}
              clearPending={clearMutation.isPending}
              t={t}
            />
          )}
        </div>
      </div>

      <ConfirmDialog
        open={showCleanConfirm}
        title={t('Clean Database')}
        message={t('Remove database entries for files that no longer exist on disk?')}
        confirmLabel={t('Clean')}
        variant="warning"
        onConfirm={() => { cleanMutation.mutate(); setShowCleanConfirm(false) }}
        onCancel={() => setShowCleanConfirm(false)}
      />

      <ConfirmDialog
        open={showClearConfirm}
        title={t('Clear Database')}
        message={t('This will permanently delete ALL entries from the scan database. This cannot be undone.')}
        confirmLabel={t('Clear All')}
        variant="danger"
        onConfirm={() => { clearMutation.mutate(); setShowClearConfirm(false) }}
        onCancel={() => setShowClearConfirm(false)}
      />
    </div>
  )
}

/* ────────────────────────────────────────────────
   Tab: Scanning
   ──────────────────────────────────────────────── */
function ScanningTab({ settings, onChange, t }: { settings: AppSettings; onChange: (k: keyof AppSettings, v: unknown) => void; t: (k: string) => string }) {
  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: '0.6rem' }}>
      <Section title={t('Similarity')}>
        <SliderField label={t('Similarity Threshold')} value={settings.threshhold} min={0} max={100} step={1}
          onChange={v => onChange('threshhold', v)} description={t('Hash difference threshold. Lower = stricter matching (fewer false positives).')} />
        <SliderField label={t('Percent Match')} value={settings.percent} min={0} max={100} step={0.5}
          onChange={v => onChange('percent', v)} format={v => `${v}%`} description={t('Minimum similarity percentage to report as duplicate.')} />
        <SliderField label={t('Duration Tolerance (%)')} value={settings.percentDurationDifference} min={0} max={100} step={1}
          onChange={v => onChange('percentDurationDifference', v)} format={v => `${v}%`} description={t('Maximum allowed duration difference between files to be considered duplicates.')} />
        <ToggleField label={t('Compare Horizontally Flipped')} value={settings.compareHorizontallyFlipped}
          onChange={v => onChange('compareHorizontallyFlipped', v)} description={t('Also detect duplicates that are horizontally mirrored.')} />
        <ToggleField label={t('Ignore Black Pixels')} value={settings.ignoreBlackPixels}
          onChange={v => onChange('ignoreBlackPixels', v)} description={t('Exclude black pixels from similarity comparison.')} />
        <ToggleField label={t('Ignore White Pixels')} value={settings.ignoreWhitePixels}
          onChange={v => onChange('ignoreWhitePixels', v)} description={t('Exclude white pixels from similarity comparison.')} />
      </Section>

      <Section title={t('Scanning')}>
        <ToggleField label={t('Include Sub-Directories')} value={settings.includeSubDirectories}
          onChange={v => onChange('includeSubDirectories', v)} description={t('Recursively scan all subdirectories.')} />
        <ToggleField label={t('Include Images')} value={settings.includeImages}
          onChange={v => onChange('includeImages', v)} description={t('Also scan image files for duplicates.')} />
        <ToggleField label={t('Use Perceptual Hashing')} value={settings.usePHashing}
          onChange={v => onChange('usePHashing', v)} description={t('Use perceptual hashing for better detection of resized/compressed duplicates.')} />
        <ToggleField label={t('Ignore Read-Only Folders')} value={settings.ignoreReadOnlyFolders}
          onChange={v => onChange('ignoreReadOnlyFolders', v)} description={t('Skip folders that cannot be written to.')} />
        <ToggleField label={t('Ignore Reparse Points')} value={settings.ignoreReparsePoints}
          onChange={v => onChange('ignoreReparsePoints', v)} description={t('Skip symbolic links and junction points.')} />
        <ToggleField label={t('Exclude Hard Links')} value={settings.excludeHardLinks}
          onChange={v => onChange('excludeHardLinks', v)} description={t('Skip hard links to already-scanned files.')} />
        <ToggleField label={t('Use EXIF Creation Date')} value={settings.useExifCreationDate}
          onChange={v => onChange('useExifCreationDate', v)} description={t('Use EXIF metadata for image creation dates.')} />
        <ToggleField label={t('Include Non-Existing Files')} value={settings.includeNonExistingFiles}
          onChange={v => onChange('includeNonExistingFiles', v)} description={t('Compare against database entries whose files no longer exist.')} />
        <ToggleField label={t('Scan Against Entire Database')} value={settings.scanAgainstEntireDatabase}
          onChange={v => onChange('scanAgainstEntireDatabase', v)} description={t('Compare new files against all known entries, not just scanned folders.')} />
        <SliderField label={t('Max Degree of Parallelism')} value={settings.maxDegreeOfParallelism} min={1} max={32} step={1}
          onChange={v => onChange('maxDegreeOfParallelism', v)} description={t('Number of parallel scanning threads. Higher = faster but more CPU usage.')} />
        <SliderField label={t('Thumbnail Count')} value={settings.thumbnailCount} min={1} max={10} step={1}
          onChange={v => onChange('thumbnailCount', v)} description={t('Number of frame samples per video for comparison.')} />
      </Section>

      <Section title={t('Partial Clip Detection')}>
        <ToggleField label={t('Enable Partial Clip Detection')} value={settings.enablePartialClipDetection}
          onChange={v => onChange('enablePartialClipDetection', v)} description={t('Detect when a shorter video is a clip from a longer one (audio fingerprinting).')} />
        {settings.enablePartialClipDetection && (
          <>
            <SliderField label={t('Min Clip Ratio')} value={settings.partialClipMinRatio} min={0} max={1} step={0.01}
              onChange={v => onChange('partialClipMinRatio', v)} format={v => `${(v * 100).toFixed(0)}%`}
              description={t('Minimum clip duration as percentage of source (e.g. 10% means clip must be at least 10% of source).')} />
            <SliderField label={t('Similarity Threshold')} value={settings.partialClipSimilarityThreshold} min={0} max={1} step={0.01}
              onChange={v => onChange('partialClipSimilarityThreshold', v)} format={v => `${(v * 100).toFixed(0)}%`}
              description={t('Minimum audio fingerprint similarity for a match (higher = fewer false positives).')} />
            <ToggleField label={t('Require Visual Match')} value={settings.partialClipRequireVisualMatch}
              onChange={v => onChange('partialClipRequireVisualMatch', v)} description={t('Also verify visual similarity at the matched offset.')} />
            <SliderField label={t('Visual Threshold')} value={settings.partialClipVisualThreshold} min={0} max={1} step={0.01}
              onChange={v => onChange('partialClipVisualThreshold', v)} format={v => `${(v * 100).toFixed(0)}%`}
              description={t('Minimum visual similarity for confirmation (when enabled).')} />
          </>
        )}
      </Section>
    </div>
  )
}

/* ────────────────────────────────────────────────
   Tab: Directories
   ──────────────────────────────────────────────── */
function DirectoriesTab({ settings, onListChange, t }: {
  settings: AppSettings
  onListChange: (k: 'includeList' | 'blackList', raw: string) => void
  t: (k: string) => string
}) {
  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: '0.6rem' }}>
      <Section title={t('Directories')}>
        <ListField label={t('Include Directories')} value={settings.includeList}
          onChange={v => onListChange('includeList', v)}
          description={t('Only scan files in these directories (one per line). Leave empty to scan all.')} />
        <ListField label={t('Exclude Directories')} value={settings.blackList}
          onChange={v => onListChange('blackList', v)}
          description={t('Skip files in these directories (one per line).')} />
      </Section>
    </div>
  )
}

/* ────────────────────────────────────────────────
   Tab: File Filters
   ──────────────────────────────────────────────── */
function FiltersTab({ settings, onChange, onListChange, t }: {
  settings: AppSettings
  onChange: (k: keyof AppSettings, v: unknown) => void
  onListChange: (k: 'filePathContainsTexts' | 'filePathNotContainsTexts', raw: string) => void
  t: (k: string) => string
}) {
  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: '0.6rem' }}>
      <Section title={t('File Filters')}>
        <ToggleField label={t('Filter by File Size')} value={settings.filterByFileSize}
          onChange={v => onChange('filterByFileSize', v)} description={t('Only scan files within a size range.')} />
        {settings.filterByFileSize && (
          <>
            <SliderField label={t('Minimum File Size (MB)')} value={settings.minimumFileSize} min={0} max={10000} step={1}
              onChange={v => onChange('minimumFileSize', v)} format={v => `${v} MB`} />
            <SliderField label={t('Maximum File Size (MB)')} value={settings.maximumFileSize} min={0} max={100000} step={1}
              onChange={v => onChange('maximumFileSize', v)} format={v => `${v} MB`} />
          </>
        )}
        <ToggleField label={t('Filter by Path Contains')} value={settings.filterByFilePathContains}
          onChange={v => onChange('filterByFilePathContains', v)} description={t('Only scan files whose path contains specific text.')} />
        {settings.filterByFilePathContains && (
          <ListField label={t('Path Contains Texts')} value={settings.filePathContainsTexts}
            onChange={v => onListChange('filePathContainsTexts', v)}
            description={t('One text per line. Files matching any of these will be included.')} />
        )}
        <ToggleField label={t('Filter by Path Not Contains')} value={settings.filterByFilePathNotContains}
          onChange={v => onChange('filterByFilePathNotContains', v)} description={t('Skip files whose path contains specific text.')} />
        {settings.filterByFilePathNotContains && (
          <ListField label={t('Path Not Contains Texts')} value={settings.filePathNotContainsTexts}
            onChange={v => onListChange('filePathNotContainsTexts', v)}
            description={t('One text per line. Files matching any of these will be excluded.')} />
        )}
        <SliderField label={t('Duration Min Difference (s)')} value={settings.durationDifferenceMinSeconds} min={0} max={300} step={0.5}
          onChange={v => onChange('durationDifferenceMinSeconds', v)} description={t('Minimum duration difference to consider (0 = disabled).')} />
        <SliderField label={t('Duration Max Difference (s)')} value={settings.durationDifferenceMaxSeconds} min={0} max={300} step={0.5}
          onChange={v => onChange('durationDifferenceMaxSeconds', v)} description={t('Maximum duration difference allowed (0 = no limit).')} />
      </Section>
    </div>
  )
}

/* ────────────────────────────────────────────────
   Tab: Processing (FFmpeg settings)
   ──────────────────────────────────────────────── */
function ProcessingTab({ settings, onChange, t }: { settings: AppSettings; onChange: (k: keyof AppSettings, v: unknown) => void; t: (k: string) => string }) {
  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: '0.6rem' }}>
      <Section title={t('FFmpeg')}>
        <ToggleField label={t('Use Native FFmpeg Binding')} value={settings.useNativeFfmpegBinding}
          onChange={v => onChange('useNativeFfmpegBinding', v)} description={t('Use FFmpeg.AutoGen native bindings instead of CLI (faster but requires FFmpeg shared libraries).')} />
        <ToggleField label={t('Extended FFTools Logging')} value={settings.extendedFFToolsLogging}
          onChange={v => onChange('extendedFFToolsLogging', v)} description={t('Log detailed FFmpeg/FFprobe command output for debugging.')} />
        <ToggleField label={t('Always Retry Failed Sampling')} value={settings.alwaysRetryFailedSampling}
          onChange={v => onChange('alwaysRetryFailedSampling', v)} description={t('Re-attempt frame sampling on files that failed previously.')} />
        <ToggleField label={t('Log Excluded Files')} value={settings.logExcludedFiles}
          onChange={v => onChange('logExcludedFiles', v)} description={t('Log files skipped by filters (increases log size).')} />
        <SelectField label={t('Hardware Acceleration')} value={settings.hardwareAccelerationMode}
          options={['Auto', 'QSV', 'CUDA', 'D3D11VA', 'VAAPI', 'None']}
          onChange={v => onChange('hardwareAccelerationMode', v)} description={t('GPU acceleration for video decoding (requires compatible hardware).')} />
        <TextField label={t('Custom FF Arguments')} value={settings.customFFArguments}
          onChange={v => onChange('customFFArguments', v)} placeholder="e.g. -hwaccel cuda" description={t('Additional FFmpeg command-line arguments.')} />
        <SliderField label={t('Max Sampling Duration (s)')} value={settings.maxSamplingDurationSeconds} min={0} max={600} step={1}
          onChange={v => onChange('maxSamplingDurationSeconds', v)} description={t('Maximum seconds of video to sample (0 = entire video).')} />
      </Section>

      <Section title={t('WebUI Thumbnails')}>
        <ToggleField label={t('Auto-Load Thumbnails')} value={settings.autoLoadThumbnails}
          onChange={v => onChange('autoLoadThumbnails', v)} description={t('Automatically load thumbnails on the results page.')} />
        <SliderField label={t('Thumbnail Width (px)')} value={settings.thumbnailWidth} min={48} max={960} step={16}
          onChange={v => onChange('thumbnailWidth', v)} format={v => `${v}px`}
          description={t('Thumbnail resolution. Lower = less memory usage, more pixelated.')} />
        <SliderField label={t('JPEG Quality')} value={settings.thumbnailJpegQuality} min={10} max={95} step={5}
          onChange={v => onChange('thumbnailJpegQuality', v)}
          description={t('JPEG compression quality. Lower = smaller files, more artifacts.')} />
      </Section>
    </div>
  )
}

/* ────────────────────────────────────────────────
   Tab: Appearance
   ──────────────────────────────────────────────── */
function AppearanceTab({ settings, onChange, onLanguageChange, lang, theme, toggleTheme, t }: {
  settings: AppSettings
  onChange: (k: keyof AppSettings, v: unknown) => void
  onLanguageChange: (code: string) => void
  lang: string
  theme: string
  toggleTheme: () => void
  t: (k: string) => string
}) {
  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: '0.6rem' }}>
      <Section title={t('Appearance')}>
        <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: '1rem' }}>
          <div>
            <span style={{ fontSize: '11px', color: 'var(--text-secondary)', fontFamily: 'var(--font-sans)' }}>{t('Select language')}</span>
          </div>
          <select
            value={lang}
            onChange={e => onLanguageChange(e.target.value)}
            style={{
              padding: '0.3rem 0.5rem',
              border: '1px solid var(--border-input)',
              borderRadius: 'var(--radius-md)',
              background: 'var(--bg-input)',
              color: 'var(--text-primary)',
              fontSize: '11px',
              fontFamily: 'var(--font-mono)',
              cursor: 'pointer',
              outline: 'none',
              minWidth: 140,
            }}
          >
            {availableLanguages.map(l => (
              <option key={l.code} value={l.code}>{l.name}</option>
            ))}
          </select>
        </div>
        <ToggleField label={t('Dark Mode')} value={theme === 'dark'}
          onChange={() => toggleTheme()} description={t('Enable dark theme')} />
        <ToggleField label={t('Show Welcome Guide')} value={settings.showWelcomeGuide}
          onChange={v => onChange('showWelcomeGuide', v)} description={t('Show the getting started guide when first visiting the app.')} />
      </Section>
    </div>
  )
}

/* ────────────────────────────────────────────────
   Tab: Database
   ──────────────────────────────────────────────── */
function DatabaseTab({ settings, onChange, onClean, onClear, cleanPending, clearPending, t }: {
  settings: AppSettings
  onChange: (k: keyof AppSettings, v: unknown) => void
  onClean: () => void
  onClear: () => void
  cleanPending: boolean
  clearPending: boolean
  t: (k: string) => string
}) {
  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: '0.6rem' }}>
      <Section title={t('Database')}>
        <TextField label={t('Custom Database Folder')} value={settings.customDatabaseFolder}
          onChange={v => onChange('customDatabaseFolder', v)} placeholder={t('Leave empty for default')}
          description={t('Custom location for the scan database (default: system config folder).')} />
        <SliderField label={t('Checkpoint Interval (min)')} value={settings.databaseCheckpointIntervalMinutes} min={1} max={60} step={1}
          onChange={v => onChange('databaseCheckpointIntervalMinutes', v)}
          description={t('How often to save scan progress during scanning (prevents data loss on crash).')} />
        <div style={{ display: 'flex', gap: '0.5rem', marginTop: '0.5rem' }}>
          <button
            onClick={onClean}
            disabled={cleanPending}
            style={dangerBtnStyle}
          >
            {cleanPending ? `${t('Clean')}ing...` : t('Clean Database')}
          </button>
          <button
            onClick={onClear}
            disabled={clearPending}
            style={dangerBtnStyle}
          >
            {clearPending ? `${t('Clear')}ing...` : t('Clear Database')}
          </button>
        </div>
      </Section>
    </div>
  )
}

/* ────────────────────────────────────────────────
   Shared field components
   ──────────────────────────────────────────────── */

const dangerBtnStyle: React.CSSProperties = {
  padding: '0.35rem 0.85rem',
  borderRadius: 'var(--radius-md)',
  border: '1px solid var(--accent-error-border)',
  background: 'var(--accent-error-bg)',
  color: 'var(--accent-danger-text)',
  cursor: 'pointer',
  fontSize: '11px',
  fontWeight: 500,
  fontFamily: 'var(--font-sans)',
  transition: 'all var(--transition-fast)',
}

function Section({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <div style={{ marginBottom: '0.25rem' }}>
      <h2 style={{
        margin: '0 0 0.5rem',
        fontFamily: 'var(--font-display)',
        fontSize: '11px',
        fontWeight: 600,
        color: 'var(--text-muted)',
        textTransform: 'uppercase',
        letterSpacing: '0.1em',
        paddingBottom: '0.35rem',
        borderBottom: '1px solid var(--border-default)',
      }}>
        {title}
      </h2>
      <div style={{ display: 'flex', flexDirection: 'column', gap: '0.45rem' }}>
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
      <div style={{ minWidth: 0 }}>
        <span style={{ fontSize: '11px', color: 'var(--text-secondary)', fontFamily: 'var(--font-sans)' }}>{label}</span>
        {description && <div style={{ fontSize: '10px', color: 'var(--text-dim)', marginTop: '1px' }}>{description}</div>}
      </div>
      <button
        role="switch"
        aria-checked={value}
        aria-label={label}
        onClick={() => onChange(!value)}
        style={{
          width: 36,
          height: 20,
          borderRadius: 10,
          border: 'none',
          background: value ? 'var(--accent-primary)' : 'var(--bg-input)',
          cursor: 'pointer',
          position: 'relative',
          transition: 'background var(--transition-base)',
          flexShrink: 0,
          boxShadow: value ? '0 0 10px var(--accent-primary-glow)' : 'inset 0 1px 3px rgba(0,0,0,0.3)',
        }}
      >
        <div style={{
          width: 14,
          height: 14,
          borderRadius: '50%',
          background: value ? '#fff' : 'var(--text-muted)',
          position: 'absolute',
          top: 3,
          left: value ? 19 : 3,
          transition: 'left var(--transition-base)',
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
      <div style={{ display: 'flex', justifyContent: 'space-between', marginBottom: '1px', alignItems: 'baseline' }}>
        <span style={{ fontSize: '11px', color: 'var(--text-secondary)', fontFamily: 'var(--font-sans)' }}>{label}</span>
        <span style={{
          fontSize: '11px',
          color: 'var(--accent-primary)',
          fontFamily: 'var(--font-mono)',
          fontWeight: 500,
        }}>
          {format ? format(value) : value}
        </span>
      </div>
      {description && <div style={{ fontSize: '10px', color: 'var(--text-dim)', marginBottom: '2px' }}>{description}</div>}
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
      <div style={{ minWidth: 0 }}>
        <span style={{ fontSize: '11px', color: 'var(--text-secondary)', fontFamily: 'var(--font-sans)' }}>{label}</span>
        {description && <div style={{ fontSize: '10px', color: 'var(--text-dim)', marginTop: '1px' }}>{description}</div>}
      </div>
      <select
        value={value}
        onChange={e => onChange(e.target.value)}
        style={{
          padding: '0.3rem 0.5rem',
          border: '1px solid var(--border-input)',
          borderRadius: 'var(--radius-md)',
          background: 'var(--bg-input)',
          color: 'var(--text-primary)',
          fontSize: '11px',
          fontFamily: 'var(--font-mono)',
          cursor: 'pointer',
          outline: 'none',
          minWidth: 100,
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
      <div style={{ marginBottom: '1px' }}>
        <span style={{ fontSize: '11px', color: 'var(--text-secondary)', fontFamily: 'var(--font-sans)' }}>{label}</span>
        {description && <div style={{ fontSize: '10px', color: 'var(--text-dim)', marginTop: '1px' }}>{description}</div>}
      </div>
      <input
        value={value}
        onChange={e => onChange(e.target.value)}
        placeholder={placeholder}
        style={{
          width: '100%',
          padding: '0.35rem 0.55rem',
          border: '1px solid var(--border-input)',
          borderRadius: 'var(--radius-md)',
          background: 'var(--bg-input)',
          color: 'var(--text-primary)',
          fontSize: '11px',
          fontFamily: 'var(--font-mono)',
          outline: 'none',
          boxSizing: 'border-box',
        }}
      />
    </div>
  )
}

function ListField({ label, value, onChange, description }: FieldProps & {
  value: string[]; onChange: (v: string) => void
}) {
  return (
    <div>
      <div style={{ marginBottom: '1px' }}>
        <span style={{ fontSize: '11px', color: 'var(--text-secondary)', fontFamily: 'var(--font-sans)' }}>{label}</span>
        {description && <div style={{ fontSize: '10px', color: 'var(--text-dim)', marginTop: '1px' }}>{description}</div>}
      </div>
      <textarea
        value={value.join('\n')}
        onChange={e => onChange(e.target.value)}
        rows={4}
        style={{
          width: '100%',
          padding: '0.35rem 0.55rem',
          border: '1px solid var(--border-input)',
          borderRadius: 'var(--radius-md)',
          background: 'var(--bg-input)',
          color: 'var(--text-primary)',
          fontSize: '11px',
          fontFamily: 'var(--font-mono)',
          outline: 'none',
          resize: 'vertical',
          boxSizing: 'border-box',
          lineHeight: 1.4,
        }}
      />
    </div>
  )
}

import { apiRequest } from './client'

export interface AppSettings {
  includeList: string[]
  blackList: string[]
  threshhold: number
  percent: number
  percentDurationDifference: number
  maxDegreeOfParallelism: number
  thumbnailCount: number
  includeSubDirectories: boolean
  includeImages: boolean
  usePHashing: boolean
  ignoreReadOnlyFolders: boolean
  ignoreReparsePoints: boolean
  excludeHardLinks: boolean
  useExifCreationDate: boolean
  alwaysRetryFailedSampling: boolean
  extendedFFToolsLogging: boolean
  logExcludedFiles: boolean
  useNativeFfmpegBinding: boolean
  hardwareAccelerationMode: string
  customFFArguments: string
  customDatabaseFolder: string
  databaseCheckpointIntervalMinutes: number
  compareHorizontallyFlipped: boolean
  ignoreBlackPixels: boolean
  ignoreWhitePixels: boolean
  includeNonExistingFiles: boolean
  scanAgainstEntireDatabase: boolean
  folderMatchMode: string
  sameFolderDepth: number
  durationDifferenceMinSeconds: number
  durationDifferenceMaxSeconds: number
  maxSamplingDurationSeconds: number
  filterByFileSize: boolean
  minimumFileSize: number
  maximumFileSize: number
  filterByFilePathContains: boolean
  filePathContainsTexts: string[]
  filterByFilePathNotContains: boolean
  filePathNotContainsTexts: string[]
  enablePartialClipDetection: boolean
  partialClipMinRatio: number
  partialClipSimilarityThreshold: number
  partialClipRequireVisualMatch: boolean
  partialClipVisualThreshold: number
  autoLoadThumbnails: boolean
  thumbnailWidth: number
  thumbnailJpegQuality: number
  languageCode: string
  showWelcomeGuide: boolean
}

export interface WebSettings {
  autoLoadThumbnails: boolean
  thumbnailWidth: number
  thumbnailJpegQuality: number
}

export interface DatabaseCleanResponse {
  removed: number
  remaining: number
}

export interface DatabaseClearResponse {
  success: boolean
}

export async function getSettings(): Promise<AppSettings> {
  return apiRequest<AppSettings>('/settings', { method: 'GET' })
}

export async function updateSettings(settings: Partial<AppSettings>): Promise<{ updated: boolean }> {
  return apiRequest<{ updated: boolean }>('/settings', {
    method: 'PUT',
    body: settings,
  })
}

export async function saveSettings(): Promise<{ saved: boolean }> {
  return apiRequest<{ saved: boolean }>('/settings/save', { method: 'POST' })
}

export async function cleanDatabase(): Promise<DatabaseCleanResponse> {
  return apiRequest<DatabaseCleanResponse>('/settings/database/clean', { method: 'POST' })
}

export async function clearDatabase(): Promise<DatabaseClearResponse> {
  return apiRequest<DatabaseClearResponse>('/settings/database/clear', { method: 'POST' })
}

export async function getWebSettings(): Promise<WebSettings> {
  return apiRequest<WebSettings>('/settings/web', { method: 'GET' })
}

export async function updateWebSettings(settings: Partial<WebSettings>): Promise<WebSettings> {
  return apiRequest<WebSettings>('/settings/web', {
    method: 'PUT',
    body: settings,
  })
}

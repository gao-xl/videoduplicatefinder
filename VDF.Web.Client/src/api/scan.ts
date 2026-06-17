import { apiRequest } from './client'

export interface ScanProgressResponse {
  state: string
  filesHashed: number
  currentFile: string
  current: number
  max: number
  elapsedSeconds: number
  remainingSeconds: number
  currentStage: string
  stageCurrent: number
  stageMax: number
  errorMessage?: string
  currentThumbnailPath?: string
}

export interface ScanStateResponse {
  state: string
  errorMessage?: string
}

export async function startScan(): Promise<{ scanId: string }> {
  return apiRequest<{ scanId: string }>('/scan/start', {
    method: 'POST',
    body: {},
  })
}

export async function stopScan(): Promise<{ status: string }> {
  return apiRequest<{ status: string }>('/scan/stop', { method: 'POST' })
}

export async function pauseScan(): Promise<{ status: string }> {
  return apiRequest<{ status: string }>('/scan/pause', { method: 'POST' })
}

export async function resumeScan(): Promise<{ status: string }> {
  return apiRequest<{ status: string }>('/scan/resume', { method: 'POST' })
}

export async function getScanProgress(): Promise<ScanProgressResponse> {
  return apiRequest<ScanProgressResponse>('/scan/progress', { method: 'GET' })
}

export async function getScanState(): Promise<ScanStateResponse> {
  return apiRequest<ScanStateResponse>('/scan/state', { method: 'GET' })
}

export async function resetScan(): Promise<{ status: string }> {
  return apiRequest<{ status: string }>('/scan/reset', { method: 'POST' })
}

export async function clearDatabase(): Promise<{ status: string }> {
  return apiRequest<{ status: string }>('/scan/clear-database', { method: 'POST' })
}

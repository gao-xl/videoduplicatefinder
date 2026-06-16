import { apiRequest } from './client'

export interface DuplicateGroupDto {
  groupId: string
  items: DuplicateItemDto[]
}

export interface DuplicateItemDto {
  path: string
  folder: string
  sizeBytes: number
  durationSeconds: number
  frameSize: string | null
  fps: number
  bitRateKbs: number
  format: string | null
  audioFormat: string | null
  audioChannel: string | null
  audioSampleRate: number
  audioBitRateKbs: number
  similarity: number
  dateCreated: string
  isImage: boolean
  hdrFormat: string
  flags: string
  partialClipOffsetSeconds: number
  groupId: string
}

export interface ResultsPageResponse {
  groups: DuplicateGroupDto[]
  totalGroups: number
  page: number
  pageSize: number
  totalFiles: number
  totalSizeBytes: number
  potentialSavingsBytes: number
}

export interface DeleteItemsRequest {
  paths: string[]
  permanent: boolean
}

export interface MoveItemsRequest {
  paths: string[]
  destination: string
}

export interface CreateLinksRequest {
  paths: string[]
  hardlink: boolean
}

export interface RemoveItemsRequest {
  paths: string[]
}

export interface AutoSelectRequest {
  mode: 'lowestQuality' | 'smallestFile' | 'oldest' | 'newest' | 'hundredPercentEqual'
}

export interface KeepBestRequest {
  groupId: string
}

export interface FileOpResultDto {
  done: number
  failed: number
  freedBytes: number
  errors: string[]
  warnings: string[]
}

export interface AutoSelectResponse {
  selectedPaths: string[]
  count: number
}

export interface KeepBestResponse {
  keeperPath: string
  selectedPaths: string[]
  count: number
}

export async function getResults(page = 1, pageSize = 50, search = ''): Promise<ResultsPageResponse> {
  const params = new URLSearchParams()
  params.set('page', String(page))
  params.set('pageSize', String(pageSize))
  if (search) params.set('search', search)
  return apiRequest<ResultsPageResponse>(`/results?${params.toString()}`, { method: 'GET' })
}

export async function deleteItems(req: DeleteItemsRequest): Promise<FileOpResultDto> {
  return apiRequest<FileOpResultDto>('/results/items', {
    method: 'DELETE',
    body: req,
  })
}

export async function moveItems(req: MoveItemsRequest): Promise<FileOpResultDto> {
  return apiRequest<FileOpResultDto>('/results/move', {
    method: 'POST',
    body: req,
  })
}

export async function createLinks(req: CreateLinksRequest): Promise<FileOpResultDto> {
  return apiRequest<FileOpResultDto>('/results/links', {
    method: 'POST',
    body: req,
  })
}

export async function removeItems(req: RemoveItemsRequest): Promise<{ removed: number }> {
  return apiRequest<{ removed: number }>('/results/remove', {
    method: 'DELETE',
    body: req,
  })
}

export async function exportCsv(): Promise<Blob> {
  const { getToken, ApiError } = await import('./client')
  const token = getToken()
  const res = await fetch('/api/results/export/csv', {
    headers: {
      Authorization: token ? `Bearer ${token}` : '',
      Accept: 'text/csv',
    },
  })
  if (!res.ok) {
    throw new ApiError(res.status, `Export failed: ${res.status}`)
  }
  return res.blob()
}

export async function autoSelect(req: AutoSelectRequest): Promise<AutoSelectResponse> {
  return apiRequest<AutoSelectResponse>('/results/autoselect', {
    method: 'POST',
    body: req,
  })
}

export async function keepBest(req: KeepBestRequest): Promise<KeepBestResponse> {
  return apiRequest<KeepBestResponse>('/results/keepbest', {
    method: 'POST',
    body: req,
  })
}

export function thumbnailUrl(path: string, width?: number, quality?: number): string {
  const params = new URLSearchParams()
  params.set('path', path)
  if (width) params.set('w', String(width))
  if (quality) params.set('q', String(quality))
  return `/api/thumbnail/hq?${params.toString()}`
}

export function thumbnailFullUrl(path: string): string {
  return `/api/thumbnail/full?path=${encodeURIComponent(path)}`
}

const API_BASE = '/api'

interface RequestOptions extends Omit<RequestInit, 'body'> {
  body?: unknown
}

// Refresh lock to prevent multiple simultaneous token refresh attempts
let refreshPromise: Promise<string | null> | null = null

function getToken(): string | null {
  return localStorage.getItem('vdf-access-token')
}

function setTokens(access: string, refresh: string) {
  localStorage.setItem('vdf-access-token', access)
  localStorage.setItem('vdf-refresh-token', refresh)
}

function clearTokens() {
  localStorage.removeItem('vdf-access-token')
  localStorage.removeItem('vdf-refresh-token')
}

async function refreshAccessToken(): Promise<string | null> {
  // If a refresh is already in progress, wait for it
  if (refreshPromise) {
    return refreshPromise
  }

  refreshPromise = (async () => {
    const refreshToken = localStorage.getItem('vdf-refresh-token')
    if (!refreshToken) return null

    try {
      const res = await fetch(`${API_BASE}/auth/refresh`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ refresh_token: refreshToken }),
      })
      if (!res.ok) {
        clearTokens()
        return null
      }
      const data = await res.json()
      localStorage.setItem('vdf-access-token', data.access_token)
      return data.access_token
    } catch {
      clearTokens()
      return null
    } finally {
      refreshPromise = null
    }
  })()

  return refreshPromise
}

export async function apiRequest<T>(
  endpoint: string,
  options: RequestOptions = {},
): Promise<T> {
  const { body, headers: customHeaders, ...rest } = options

  const headers = new Headers(customHeaders as HeadersInit)
  if (body && !headers.has('Content-Type')) {
    headers.set('Content-Type', 'application/json')
  }

  const token = getToken()
  if (token) {
    headers.set('Authorization', `Bearer ${token}`)
  }

  const res = await fetch(`${API_BASE}${endpoint}`, {
    ...rest,
    headers,
    body: body ? JSON.stringify(body) : undefined,
  })

  if (res.status === 401) {
    const newToken = await refreshAccessToken()
    if (newToken) {
      headers.set('Authorization', `Bearer ${newToken}`)
      // Only retry safe methods (GET, HEAD) to avoid duplicate side effects
      const method = (rest.method || 'GET').toUpperCase()
      if (method === 'GET' || method === 'HEAD') {
        const retry = await fetch(`${API_BASE}${endpoint}`, {
          ...rest,
          headers,
        })
        if (!retry.ok) {
          throw new ApiError(retry.status, await retry.text())
        }
        if (retry.status === 204) return undefined as T
        return retry.json() as Promise<T>
      }
      // For non-safe methods, don't retry to avoid duplicate operations
      throw new ApiError(401, 'Session expired - please retry your action')
    }
    clearTokens()
    window.location.href = '/login'
    throw new ApiError(401, 'Session expired')
  }

  if (!res.ok) {
    throw new ApiError(res.status, await res.text())
  }

  if (res.status === 204) return undefined as T
  return res.json() as Promise<T>
}

export class ApiError extends Error {
  status: number
  body: string

  constructor(status: number, body: string) {
    super(`API Error ${status}: ${body}`)
    this.name = 'ApiError'
    this.status = status
    this.body = body
  }
}

export { getToken, setTokens, clearTokens }

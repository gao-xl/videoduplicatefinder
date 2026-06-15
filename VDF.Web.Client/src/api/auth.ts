import { apiRequest, setTokens, clearTokens } from './client'

export interface LoginRequest {
  password: string
  remember?: boolean
}

export interface LoginResponse {
  access_token: string
  refresh_token: string
  expires_in: number
}

export interface RefreshRequest {
  refresh_token: string
}

export interface RefreshResponse {
  access_token: string
  expires_in: number
}

export interface AuthStatusResponse {
  authenticated: boolean
  authEnabled: boolean
}

export async function login(req: LoginRequest): Promise<LoginResponse> {
  const data = await apiRequest<LoginResponse>('/auth/login', {
    method: 'POST',
    body: req,
  })
  setTokens(data.access_token, data.refresh_token)
  return data
}

export async function logout(): Promise<void> {
  try {
    await apiRequest<void>('/auth/logout', { method: 'POST' })
  } finally {
    clearTokens()
  }
}

export async function checkAuth(): Promise<AuthStatusResponse> {
  return apiRequest<AuthStatusResponse>('/auth/status', {
    method: 'GET',
  })
}

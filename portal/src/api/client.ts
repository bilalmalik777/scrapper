import type { ApiResponse } from './types'

const API_BASE_URL = import.meta.env.VITE_API_BASE_URL || 'https://localhost:7098/api/v1'

export class ApiError extends Error {
  status: number

  constructor(message: string, status: number) {
    super(message)
    this.name = 'ApiError'
    this.status = status
  }
}

function buildQuery(params?: Record<string, unknown>): string {
  if (!params) return ''
  const search = new URLSearchParams()
  Object.entries(params).forEach(([key, value]) => {
    if (value === undefined || value === null) return
    search.append(key, String(value))
  })
  const query = search.toString()
  return query ? `?${query}` : ''
}

async function parseErrorResponse(response: Response): Promise<string> {
  try {
    const body = await response.json()
    if (body?.message) return body.message as string
    if (body?.detail) return body.detail as string
    // ASP.NET's automatic FluentValidation response (a ValidationProblemDetails) puts the
    // actually-useful text in `errors` (per-field messages) and leaves `title` as a generic
    // "One or more validation errors occurred." — surface the first real message instead.
    if (body?.errors && typeof body.errors === 'object') {
      const firstMessages = Object.values(body.errors as Record<string, unknown>)[0]
      if (Array.isArray(firstMessages) && typeof firstMessages[0] === 'string') {
        return firstMessages[0]
      }
    }
    if (body?.title) return body.title as string
  } catch {
    // response body was not JSON; fall through to generic message
  }
  return 'An unexpected error occurred. Please try again.'
}

async function request<T>(
  path: string,
  options: RequestInit = {},
  params?: Record<string, unknown>,
): Promise<T> {
  const response = await fetch(`${API_BASE_URL}${path}${buildQuery(params)}`, {
    ...options,
    headers: {
      'Content-Type': 'application/json',
      ...options.headers,
    },
  })

  if (!response.ok) {
    throw new ApiError(await parseErrorResponse(response), response.status)
  }

  const envelope = (await response.json()) as ApiResponse<T>
  return envelope.data
}

async function requestBlob(path: string, options: RequestInit = {}): Promise<Blob> {
  const response = await fetch(`${API_BASE_URL}${path}`, options)

  if (!response.ok) {
    throw new ApiError(await parseErrorResponse(response), response.status)
  }

  return response.blob()
}

export const api = {
  get: <T>(path: string, params?: Record<string, unknown>) =>
    request<T>(path, { method: 'GET' }, params),
  post: <T>(path: string, body?: unknown) =>
    request<T>(path, {
      method: 'POST',
      body: body !== undefined ? JSON.stringify(body) : undefined,
    }),
  postBlob: (path: string, body?: unknown) =>
    requestBlob(path, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: body !== undefined ? JSON.stringify(body) : undefined,
    }),
}

const API_BASE = '/api'

// Cached anti-forgery request token. Minimal API endpoints that bind IFormFile
// (payment evidence uploads) validate a `__RequestVerificationToken` field on
// multipart/form-data requests. The token is issued by /api/antiforgery/token
// together with its matching cookie (sent via credentials: 'include').
let antiforgeryToken: string | null = null
let antiforgeryTokenPromise: Promise<string | null> | null = null

function loadAntiforgeryToken(): Promise<string | null> {
  if (antiforgeryToken) return Promise.resolve(antiforgeryToken)
  if (antiforgeryTokenPromise) return antiforgeryTokenPromise
  antiforgeryTokenPromise = fetch(`${API_BASE}/antiforgery/token`, {
    credentials: 'include',
  })
    .then(async (res) => {
      if (!res.ok) return null
      const body = (await res.json()) as { requestToken?: string }
      antiforgeryToken = body.requestToken ?? null
      return antiforgeryToken
    })
    .catch(() => null)
    .finally(() => {
      antiforgeryTokenPromise = null
    })
  return antiforgeryTokenPromise
}

export class ApiError extends Error {
  status: number
  constructor(status: number, message: string) {
    super(message)
    this.name = 'ApiError'
    this.status = status
  }
}

async function handle<T>(res: Response): Promise<T> {
  if (res.status === 401) {
    // Unauthenticated - treated as a normal navigation concern by callers.
    throw new ApiError(401, 'Not authenticated')
  }
  if (!res.ok) {
    let message = `Request failed with status ${res.status}`
    try {
      const body = await res.json()
      if (body?.title) message = body.title
      else if (body?.message) message = body.message
    } catch {
      // ignore non-JSON error bodies
    }
    throw new ApiError(res.status, message)
  }
  if (res.status === 204) return undefined as T
  return res.json() as Promise<T>
}

export const api = {
  get<T>(path: string, signal?: AbortSignal): Promise<T> {
    return fetch(`${API_BASE}${path}`, { credentials: 'include', signal }).then(
      (r) => handle<T>(r),
    )
  },
  post<T>(path: string, body?: unknown): Promise<T> {
    return fetch(`${API_BASE}${path}`, {
      method: 'POST',
      credentials: 'include',
      headers: { 'Content-Type': 'application/json' },
      body: body === undefined ? undefined : JSON.stringify(body),
    }).then((r) => handle<T>(r))
  },
  put<T>(path: string, body?: unknown): Promise<T> {
    return fetch(`${API_BASE}${path}`, {
      method: 'PUT',
      credentials: 'include',
      headers: { 'Content-Type': 'application/json' },
      body: body === undefined ? undefined : JSON.stringify(body),
    }).then((r) => handle<T>(r))
  },
  delete<T>(path: string): Promise<T> {
    return fetch(`${API_BASE}${path}`, {
      method: 'DELETE',
      credentials: 'include',
    }).then((r) => handle<T>(r))
  },
  async postForm<T>(path: string, body: FormData): Promise<T> {
    const token = await loadAntiforgeryToken()
    if (token) body.append('__RequestVerificationToken', token)
    return fetch(`${API_BASE}${path}`, {
      method: 'POST',
      credentials: 'include',
      body,
    }).then((r) => handle<T>(r))
  },
}

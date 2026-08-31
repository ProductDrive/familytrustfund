const API_BASE = '/api'

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
}

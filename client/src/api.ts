const API_BASE = import.meta.env.VITE_API_BASE_URL ?? ''

let csrfToken: string | null = null

async function getCsrfToken(): Promise<string> {
  if (csrfToken) return csrfToken
  const response = await fetch(`${API_BASE}/api/auth/csrf`, { credentials: 'include' })
  if (!response.ok) throw new Error('Nie udało się rozpocząć bezpiecznej sesji.')
  const data = (await response.json()) as { token: string }
  csrfToken = data.token
  return csrfToken
}

export async function api<T>(path: string, init: RequestInit = {}): Promise<T> {
  const method = (init.method ?? 'GET').toUpperCase()
  const headers = new Headers(init.headers)
  if (init.body && !headers.has('Content-Type')) headers.set('Content-Type', 'application/json')
  if (['POST', 'PUT', 'PATCH', 'DELETE'].includes(method)) {
    headers.set('X-XSRF-TOKEN', await getCsrfToken())
  }

  const response = await fetch(`${API_BASE}/api${path}`, {
    ...init,
    method,
    headers,
    credentials: 'include',
  })
  if (response.ok && path === '/auth/login') {
    csrfToken = null
    await getCsrfToken()
  } else if (response.ok && path === '/auth/logout') {
    csrfToken = null
  }
  if (!response.ok) {
    const body = (await response.json().catch(() => null)) as
      | { error?: string; errors?: string[] }
      | null
    throw new Error(body?.error ?? body?.errors?.join(' ') ?? `Błąd serwera (${response.status}).`)
  }
  if (response.status === 204) return undefined as T
  return (await response.json()) as T
}

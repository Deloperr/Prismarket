import axios, { AxiosError, type InternalAxiosRequestConfig } from 'axios'
import { useAuth } from '@/store/auth'
import type { TokenResponse } from './types'

export const http = axios.create({ baseURL: '/api', withCredentials: true })

http.interceptors.request.use((config) => {
  const token = useAuth.getState().accessToken
  if (token) config.headers.Authorization = `Bearer ${token}`
  return config
})

// Single-flight refresh: concurrent 401s wait for one refresh request.
let refreshing: Promise<string | null> | null = null

export async function refreshAccessToken(): Promise<string | null> {
  refreshing ??= axios
    .post<TokenResponse>('/api/auth/refresh', null, { withCredentials: true })
    .then((r) => {
      if (r.data.accessToken && r.data.user) {
        useAuth.getState().setSession(r.data.accessToken, r.data.user)
        return r.data.accessToken
      }
      return null
    })
    .catch(() => {
      useAuth.getState().clear()
      return null
    })
    .finally(() => {
      refreshing = null
    })
  return refreshing
}

http.interceptors.response.use(
  (r) => r,
  async (error: AxiosError) => {
    const original = error.config as (InternalAxiosRequestConfig & { _retry?: boolean }) | undefined
    const url = original?.url ?? ''
    if (error.response?.status === 401 && original && !original._retry && !url.startsWith('/auth/')) {
      original._retry = true
      const token = await refreshAccessToken()
      if (token) {
        original.headers.Authorization = `Bearer ${token}`
        return http(original)
      }
    }
    return Promise.reject(error)
  },
)

/** Human readable error message from ProblemDetails / validation errors. */
export function errorMessage(error: unknown): string {
  if (axios.isAxiosError(error)) {
    const data = error.response?.data as { title?: string; errors?: Record<string, string[]> } | undefined
    if (data?.errors) return Object.values(data.errors).flat().join(' ')
    if (data?.title) return data.title
    if (error.response?.status === 429) return 'Слишком много попыток. Подождите минуту.'
    if (!error.response) return 'Сервер недоступен. Проверьте подключение.'
  }
  return error instanceof Error ? error.message : 'Что-то пошло не так'
}

/** Downloads a file returned by an authenticated endpoint. */
export async function download(url: string, params?: Record<string, unknown>) {
  const response = await http.get(url, { params, responseType: 'blob' })
  const disposition = response.headers['content-disposition'] as string | undefined
  const match = disposition?.match(/filename\*?=(?:UTF-8'')?"?([^";]+)"?/i)
  const name = match ? decodeURIComponent(match[1]) : 'download'
  const link = document.createElement('a')
  link.href = URL.createObjectURL(response.data as Blob)
  link.download = name
  link.click()
  setTimeout(() => URL.revokeObjectURL(link.href), 1000)
}

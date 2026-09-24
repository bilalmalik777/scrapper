import { api } from '../api/client'
import type { CrawlJobRecordsResponse, CrawlJobState, StartCrawlJobRequest } from '../api/types'

export const crawlJobsApi = {
  start: (request: StartCrawlJobRequest) => api.post<string>('/crawl-jobs', request),
  getStatus: (jobId: string) => api.get<CrawlJobState>(`/crawl-jobs/${encodeURIComponent(jobId)}`),
  resume: (jobId: string) => api.post<string>(`/crawl-jobs/${encodeURIComponent(jobId)}/resume`),
  retryFailed: (jobId: string) => api.post<string>(`/crawl-jobs/${encodeURIComponent(jobId)}/retry-failed`),
  getRecords: (jobId: string) => api.get<CrawlJobRecordsResponse>(`/crawl-jobs/${encodeURIComponent(jobId)}/records`),
}

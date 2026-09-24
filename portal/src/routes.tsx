import React, { LazyExoticComponent, FC, ReactNode } from 'react'
import { Translation } from 'react-i18next'

export type Route = {
  element?: LazyExoticComponent<FC>
  exact?: boolean
  name?: ReactNode
  path?: string
  routes?: Route[]
}

// Scraper
const ScraperPage = React.lazy(() => import('./views/scraper/ScraperPage'))
const CrawlJobsPage = React.lazy(() => import('./views/scraper/CrawlJobsPage'))

const routes: Route[] = [
  { path: '/', exact: true, name: <Translation>{(t) => t('home')}</Translation> },
  { path: '/scraper', name: 'Web Scraper', element: ScraperPage },
  { path: '/crawl-jobs', name: 'Paged Crawl Jobs', element: CrawlJobsPage },
]

export default routes

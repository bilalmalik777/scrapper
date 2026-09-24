import React, { ElementType, JSX } from 'react'
import CIcon from '@coreui/icons-react'
import { cilLoopCircular, cilSpreadsheet } from '@coreui/icons'
import { CNavItem } from '@coreui/react-pro'

export type Badge = {
  color: string
  text: string
}

export type NavItem = {
  badge?: Badge
  component: string | ElementType
  href?: string
  icon?: string | JSX.Element
  items?: NavItem[]
  name: string | JSX.Element
  to?: string
}

const _nav: NavItem[] = [
  {
    component: CNavItem,
    name: 'Web Scraper',
    to: '/scraper',
    icon: <CIcon icon={cilSpreadsheet} customClassName="nav-icon" />,
  },
  {
    component: CNavItem,
    name: 'Paged Crawl Jobs',
    to: '/crawl-jobs',
    icon: <CIcon icon={cilLoopCircular} customClassName="nav-icon" />,
  },
]

export default _nav

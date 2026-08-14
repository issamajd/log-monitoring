import { defineConfig } from 'vitepress'

// Support-first log monitoring plan, served as a VitePress site.
// The markdown pages under site/guide/ are the current source of truth.
export default defineConfig({
  title: 'Log Monitoring',
  description: 'Support-first observability for on-prem Windows deployments.',
  lang: 'en',
  lastUpdated: true,

  themeConfig: {
    siteTitle: 'Log Monitoring',

    search: {
      provider: 'local'
    },

    nav: [
      { text: 'Overview', link: '/guide/overview' },
      { text: 'Milestone 1', link: '/guide/milestone-1' },
      { text: 'Roadmap', link: '/guide/roadmap' }
    ],

    sidebar: [
      {
        text: 'Start here',
        collapsed: false,
        items: [
          { text: 'Overview', link: '/guide/overview' }
        ]
      },
      {
        text: 'The plan',
        collapsed: false,
        items: [
          { text: 'Milestone 1', link: '/guide/milestone-1' },
          { text: 'Architecture', link: '/guide/architecture' },
          { text: 'Deployment & sizing', link: '/guide/deployment-sizing' },
          { text: 'Support workflows', link: '/guide/support-workflows' },
          { text: 'Verification runbook', link: '/guide/verification-runbook' },
          { text: 'Data & security', link: '/guide/data-security' },
          { text: 'Roadmap', link: '/guide/roadmap' }
        ]
      }
    ],

    outline: { level: [2, 3] },

    footer: {
      message: 'Log Monitoring internal planning documentation.'
    }
  }
})
